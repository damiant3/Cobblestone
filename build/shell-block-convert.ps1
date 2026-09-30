# shell-block-convert.ps1 -- ShellDslReadability step 3: rewrite a generator's raw
# blocks (ScRaw runs from an `if`/`while`/`foreach` opener to its `}`) as ScIf /
# ScWhile / ScForEach nodes, wherever every line has a node that renders it byte for
# byte: conditions through SeBare over SeVar/SeLit/SeInt, the comparison nodes,
# SeMatch, SeNot and SeFileExists; depth through ScIndent (4 spaces a level); inner
# lines as ScBlank, ScComment, ScBreak, ScContinue, ScSetStrictMode, ScSetErrorStop,
# ScIncrement, ScDecrement, ScAssign/ScExit/ScReturn/ScEcho of one atom. A block with
# any non-raw node inside its span, or a child off its depth, is left alone.
#
#   $env:GEN = 'codex/build/<X>Script.codex'; pwsh build/shell-block-convert.ps1           # dry run: prints the count
#   $env:GEN = ...; $env:WRITE = 1; pwsh build/shell-block-convert.ps1                      # rewrites the generator
#
# Then prove it: build/check-generated-scripts.ps1 -Only <emitted-name> -OutRoot <scratch>
# must stay byte-level, and build/check-shell-raw.ps1 -Update -Only <X>Script in the same CL.$ErrorActionPreference = 'Stop'
Set-Location D:\Projects\Cobblestone-reek
$g = $env:GEN
$c = [IO.File]::ReadAllText($g)
function Esc([string]$s) { $s.Replace('\', '\\').Replace('"', '\"') }
$atomRx = "'(?:[^']|'')*'|-?\d+|\`$[A-Za-z_][A-Za-z0-9_]*"
function Atom([string]$a) {
    if ($a -match "^'((?:[^']|'')*)'$") { return 'SeLit "' + (Esc $Matches[1].Replace("''", "'")) + '"' }
    if ($a -match '^-?\d+$') { return "(SeInt $a)" }
    if ($a -match '^\$([A-Za-z_][A-Za-z0-9_]*)$') { return 'SeVar "' + $Matches[1] + '"' }
    throw "not an atom: $a"
}
function Wrap([string]$e) { if ($e.StartsWith('(')) { $e } else { "($e)" } }
$ops = @{ 'eq' = 'SeIntEq'; 'ne' = 'SeStringNeq'; 'lt' = 'SeIntLt'; 'gt' = 'SeIntGt'; 'le' = 'SeIntLe'; 'ge' = 'SeIntGe' }
function Cond([string]$s) {
    if ($s -match "^($atomRx)$") { return Atom $s }
    if ($s -match "^($atomRx) -(eq|ne|lt|gt|le|ge) ($atomRx)$") { return "SeBare ($($ops[$Matches[2]]) $(Wrap (Atom $Matches[1])) $(Wrap (Atom $Matches[3])))" }
    if ($s -match "^($atomRx) -match '((?:[^']|'')*)'$") { return "SeBare (SeMatch $(Wrap (Atom $Matches[1])) `"$(Esc $Matches[2].Replace("''", "'"))`")" }
    if ($s -match "^-not ($atomRx)$") { return "SeBare (SeNot $(Wrap (Atom $Matches[1])))" }
    if ($s -match "^Test-Path -PathType Leaf ($atomRx)$") { return "SeBare (SeFileExists $(Wrap (Atom $Matches[1])))" }
    if ($s -match "^-not \(Test-Path -PathType Leaf ($atomRx)\)$") { return "SeBare (SeNot (SeFileExists $(Wrap (Atom $Matches[1]))))" }
    return $null
}
function LineNode([string]$b) {
    if ($b -eq '') { return 'ScBlank' }
    if ($b.StartsWith('# ')) { return 'ScComment "' + (Esc $b.Substring(2)) + '"' }
    if ($b -eq 'break') { return 'ScBreak' }
    if ($b -eq 'continue') { return 'ScContinue' }
    if ($b -match '^continue ([A-Za-z_]\w*)$') { return 'ScContinueLabel "' + $Matches[1] + '"' }
    if ($b -eq 'Set-StrictMode -Version Latest') { return 'ScSetStrictMode' }
    if ($b -eq "`$ErrorActionPreference = 'Stop'") { return 'ScSetErrorStop' }
    if ($b -match '^\$([A-Za-z_]\w*)\+\+$') { return 'ScIncrement "' + $Matches[1] + '"' }
    if ($b -match '^\$([A-Za-z_]\w*)--$') { return 'ScDecrement "' + $Matches[1] + '"' }
    if ($b -match "^\`$([A-Za-z_][A-Za-z0-9_]*) = ($atomRx)$") { return 'ScAssign "' + $Matches[1] + '" ' + (Wrap (Atom $Matches[2])) }
    if ($b -match "^exit ($atomRx)$") { return 'ScExit ' + (Wrap (Atom $Matches[1])) }
    if ($b -match "^return ($atomRx)$") { return 'ScReturn ' + (Wrap (Atom $Matches[1])) }
    if ($b -match "^Write-Host ($atomRx)$") { return 'ScEcho ' + (Wrap (Atom $Matches[1])) }
    return $null
}
function Opener([string]$b) {
    if ($b -match '^if \((.*)\) \{$') { $k = Cond $Matches[1]; if ($k) { return @('if', $k) } }
    if ($b -match "^while \((.*)\) \{$") { $k = Cond $Matches[1]; if ($k) { return @('while', $k) } }
    if ($b -match "^foreach \(\`$([A-Za-z_]\w*) in ($atomRx)\) \{$") { return @('foreach', $Matches[1], (Atom $Matches[2])) }
    return $null
}
$m = @([regex]::Matches($c, 'ScRaw "((?:[^"\\]|\\.)*)"'))
$pay = @($m | ForEach-Object { $_.Groups[1].Value.Replace('\"', '"').Replace('\\', '\') })
function Ind([string]$s) { $s.Length - $s.TrimStart(' ').Length }
# Parse a block starting at raw index i with opener indent d. Returns @(node, nextIndex) or $null.
function Block([int]$i, [int]$d) {
    $op = Opener $pay[$i].Trim()
    if (-not $op -or (Ind $pay[$i]) -ne $d) { return $null }
    $kids = @(); $j = $i + 1
    while ($j -lt $pay.Count) {
        $s = $pay[$j]; $b = $s.Trim()
        if ($b -eq '}' -and (Ind $s) -eq $d) { break }
        if ($b -ne '' -and (Ind $s) -ne $d + 4) { return $null }
        $sub = if (Opener $b) { Block $j ($d + 4) } else { $null }
        if ($sub) { $kids += $sub[0]; $j = $sub[1]; continue }
        $n = LineNode $b; if (-not $n) { return $null }
        if ($b -eq '' -and $s.Length -ne 0) { return $null }
        $kids += $n; $j++
    }
    if ($j -ge $pay.Count) { return $null }
    $list = '[' + ($kids -join ', ') + ']'
    $node = switch ($op[0]) { 'if' { "ScIf ($($op[1])) $list []" } 'while' { "ScWhile ($($op[1])) $list" } 'foreach' { "ScForEach `"$($op[1])`" ($($op[2])) $list" } }
    return @($node, ($j + 1))
}
$edits = @()
$i = 0
while ($i -lt $pay.Count) {
    $d = Ind $pay[$i]
    $blk = if ((Opener $pay[$i].Trim()) -and $d % 4 -eq 0 -and $d -gt 0) { Block $i $d } else { $null }
    if ($blk) {
        $first = $m[$i]; $last = $m[$blk[1] - 1]
        $span = $c.Substring($first.Index, $last.Index + $last.Length - $first.Index)
        $clean = $true
        for ($k = $i; $k -lt $blk[1] - 1; $k++) { $gap = $c.Substring($m[$k].Index + $m[$k].Length, $m[$k + 1].Index - $m[$k].Index - $m[$k].Length); if ($gap -notmatch '^\s*,\s*$') { $clean = $false } }
        if ($clean) {
            $node = $blk[0]; for ($n = 0; $n -lt $d / 4; $n++) { $node = "ScIndent [$node]" }
            $edits += [pscustomobject]@{ Start = $first.Index; Length = $span.Length; Node = $node; Lines = $blk[1] - $i }
            $i = $blk[1]; continue
        }
    }
    $i++
}
"blocks converted: $($edits.Count), raw lines replaced: $(($edits | Measure-Object Lines -Sum).Sum)"
foreach ($e in ($edits | Sort-Object Start -Descending)) { $c = $c.Remove($e.Start, $e.Length).Insert($e.Start, $e.Node) }
if ($env:WRITE) { [IO.File]::WriteAllText($g, $c) }
