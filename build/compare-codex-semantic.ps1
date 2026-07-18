# compare-codex-semantic.ps1
# Semantic equivalence check between source Codex and stage1 emitter output.
# Ported from tools/Codex.Cli/Program.SemEquiv.cs (deleted CL 690).
#
# Usage: compare-codex-semantic.ps1 -Source <source.codex> -Stage1 <stage1.codex> [-Show <name>]
#
# Parses defs from both files, matches by name (with chapter-prefix demangling),
# normalizes whitespace/parens/type-vars, compares bodies.
# Exit 0 = PASS, exit 1 = FAIL.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Stage1,
    [string]$Show
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourceText = [System.IO.File]::ReadAllText($Source).Replace("`r`n", "`n")
$stage1Text = [System.IO.File]::ReadAllText($Stage1).Replace("`r`n", "`n")

class SemDef {
    [string]$Name
    [string]$Chapter
    [string]$Sig
    [string]$Body
    [int]$LineNo
    [bool]$IsType
    SemDef([string]$n, [string]$ch, [string]$s, [string]$b, [int]$l, [bool]$t) {
        $this.Name = $n; $this.Chapter = $ch; $this.Sig = $s; $this.Body = $b; $this.LineNo = $l; $this.IsType = $t
    }
}

# A type parameter may be written bare or parenthesized -- `HamtEntry a` and
# `HamtEntry (a)` are the same type. The text emitter canonicalizes to the
# parenthesized form, so a chapter written in the bare style (Hamt, Channel,
# Queue, SessionTypes, FunnelHash, ElasticHash, Concurrent, ElasticBloom)
# would key differently in source and stage1 and be reported as a dropped
# definition -- an emitter information-loss claim where nothing was lost.
# Canonicalize both sides. Parens are already normalized away inside bodies;
# this is the header doing the same.
function Canonicalize-TypeName([string]$n) {
    $parts = @(($n -replace '[()]', ' ') -split '\s+' | Where-Object { $_ -ne '' })
    if ($parts.Count -le 1) { return $n }
    $head = $parts[0]
    $params = @($parts[1..($parts.Count - 1)] | ForEach-Object { "($_)" })
    return "$head $($params -join ' ')"
}

function Parse-OneDef([string[]]$lines, [int]$start, [string]$chapter) {
    $firstLine = $lines[$start]
    $isType = [char]::IsUpper($firstLine[0])
    $name = ''; $sig = ''
    if ($isType) {
        $eqPos = $firstLine.IndexOf(' =')
        if ($eqPos -lt 0) { return @($null, ($start + 1)) }
        $name = Canonicalize-TypeName $firstLine.Substring(0, $eqPos).Trim()
    } else {
        $colonPos = $firstLine.IndexOf(' : ')
        if ($colonPos -lt 0) { return @($null, ($start + 1)) }
        $name = $firstLine.Substring(0, $colonPos)
        $sig = $firstLine.Substring($colonPos + 3)
        $inlineEq = $sig.IndexOf(' = ')
        $inlineBody = $null
        if ($inlineEq -ge 0) {
            $inlineBody = $sig.Substring($inlineEq + 3)
            $sig = $sig.Substring(0, $inlineEq)
        }
    }
    $bodyLines = [System.Text.StringBuilder]::new()
    if ($isType) { [void]$bodyLines.AppendLine($lines[$start]) }
    elseif ($inlineBody) { [void]$bodyLines.AppendLine("$name = $inlineBody") }
    $j = $start + 1
    while ($j -lt $lines.Count) {
        $ln = $lines[$j]
        if ($ln -match '^(HEAP|STACK):\d+') { $j++; continue }
        if ($ln.Length -eq 0) {
            $peek = $j + 1
            while ($peek -lt $lines.Count -and $lines[$peek].Length -eq 0) { $peek++ }
            if ($peek -ge $lines.Count) { break }
            if ($lines[$peek].Length -gt 0 -and -not [char]::IsWhiteSpace($lines[$peek][0])) {
                if (-not $isType -and $lines[$peek].StartsWith($name) -and ($lines[$peek].Length -eq $name.Length -or $lines[$peek][$name.Length] -eq ' ' -or $lines[$peek][$name.Length] -eq '(')) { [void]$bodyLines.AppendLine(); $j++; continue }
                if ($lines[$peek][0] -in '}',']',')') { [void]$bodyLines.AppendLine(); $j++; continue }
                if ($lines[$peek] -eq 'end' -or $lines[$peek] -eq 'qed') { [void]$bodyLines.AppendLine(); $j++; continue }
                break
            }
            [void]$bodyLines.AppendLine()
            $j++; continue
        }
        if ($ln.Length -gt 0 -and [char]::IsLetter($ln[0])) {
            if (-not $isType -and $ln.StartsWith($name) -and ($ln.Length -eq $name.Length -or $ln[$name.Length] -eq ' ' -or $ln[$name.Length] -eq '(')) {
                [void]$bodyLines.AppendLine($ln); $j++; continue
            }
            if ($ln -eq 'end' -or $ln -eq 'qed') {
                [void]$bodyLines.AppendLine($ln); $j++; continue
            }
            $looksLikeSig = $ln.Contains(' : ')
            $looksLikeType = [char]::IsUpper($ln[0]) -and $ln.Contains(' =')
            if ($isType -and -not $looksLikeSig -and -not $looksLikeType) {
                [void]$bodyLines.AppendLine($ln); $j++; continue
            }
            break
        }
        if ($ln.Length -gt 0 -and ([char]::IsWhiteSpace($ln[0]) -or $ln[0] -in '|','}',']',')')) {
            [void]$bodyLines.AppendLine($ln); $j++; continue
        }
        break
    }
    $def = [SemDef]::new($name, $chapter, $sig, $bodyLines.ToString().TrimEnd(), ($start + 1), $isType)
    return @($def, $j)
}

function Parse-Stage0([string]$text) {
    $chapters = [ordered]@{}
    $slugs = [System.Collections.Generic.List[string]]::new()
    $rawLines = $text.Split("`n")
    $currentSlug = ''
    $i = 0
    while ($i -lt $rawLines.Count) {
        $raw = $rawLines[$i]
        if ($raw.StartsWith('Chapter: ')) {
            $chName = $raw.Substring(9).Trim()
            $currentSlug = $chName.ToLowerInvariant().Replace(' ', '-')
            if (-not $slugs.Contains($currentSlug)) { $slugs.Add($currentSlug) }
            if (-not $chapters.Contains($currentSlug)) { $chapters[$currentSlug] = [System.Collections.Generic.List[object]]::new() }
            $i++; continue
        }
        if ($raw.StartsWith('Section: ') -or $raw.StartsWith('Page ')) { $i++; continue }
        if ($raw.Length -ge 1 -and $raw[0] -eq ' ' -and ($raw.Length -lt 2 -or $raw[1] -ne ' ')) { $i++; continue }
        if ($raw.Length -ge 2 -and $raw[0] -eq ' ' -and $raw[1] -eq ' ') {
            $dedented = $raw.Substring(2)
            if ($dedented.StartsWith('cites ') -or $dedented.StartsWith('import ')) { $i++; continue }
            if ($dedented.Length -gt 0 -and [char]::IsLetter($dedented[0])) {
                $defLines = [System.Collections.Generic.List[string]]::new()
                while ($i -lt $rawLines.Count) {
                    $r = $rawLines[$i]
                    if ($r.Length -ge 2 -and $r[0] -eq ' ' -and $r[1] -eq ' ') {
                        $defLines.Add($r.Substring(2)); $i++; continue
                    }
                    if ($r.Length -eq 0) {
                        $pk = $i + 1
                        while ($pk -lt $rawLines.Count -and $rawLines[$pk].Length -eq 0) { $pk++ }
                        if ($pk -ge $rawLines.Count) { break }
                        $nx = $rawLines[$pk]
                        if ($nx.Length -ge 2 -and $nx[0] -eq ' ' -and $nx[1] -eq ' ') {
                            $nxD = $nx.Substring(2)
                            $isNewDef = $nxD.Length -gt 0 -and -not [char]::IsWhiteSpace($nxD[0]) -and ($nxD.Contains(' : ') -or [char]::IsUpper($nxD[0]))
                            if (-not $isNewDef) {
                                $defLines.Add(''); $i++; continue
                            }
                        }
                        break
                    }
                    break
                }
                if ($defLines.Count -gt 0) {
                    $arr = $defLines.ToArray()
                    $r = Parse-OneDef $arr 0 $currentSlug
                    if ($r[0] -and $chapters.Contains($currentSlug)) { $chapters[$currentSlug].Add($r[0]) }
                }
                continue
            }
        }
        if ($raw.Length -gt 0 -and [char]::IsLetter($raw[0])) {
            $r = Parse-OneDef $rawLines $i $currentSlug
            if ($r[0] -and $chapters.Contains($currentSlug)) { $chapters[$currentSlug].Add($r[0]) }
            $i = $r[1]; continue
        }
        $i++
    }
    return @($chapters, $slugs)
}

function Parse-Stage1([string]$text) {
    $defs = [System.Collections.Generic.List[object]]::new()
    $lines = $text.Split("`n")
    $i = 0
    while ($i -lt $lines.Count) {
        $ln = $lines[$i]
        if ($ln -match '^(HEAP|STACK):\d+') { $i++; continue }
        if ($ln.Length -gt 0 -and [char]::IsLetter($ln[0])) {
            $r = Parse-OneDef $lines $i ''
            if ($r[0]) { $defs.Add($r[0]) }
            $i = $r[1]
        } else { $i++ }
    }
    return $defs
}

function Demangle-Name([string]$name, [string[]]$slugsSorted) {
    foreach ($slug in $slugsSorted) {
        $prefix = "${slug}_"
        if ($name.StartsWith($prefix) -and $name.Length -gt $prefix.Length) {
            return @($slug, $name.Substring($prefix.Length))
        }
    }
    return $null
}

function Demangle-Names([string]$body, [string[]]$slugsSorted) {
    $text = $body
    foreach ($slug in $slugsSorted) {
        $prefix = [regex]::Escape("${slug}_")
        $text = [regex]::Replace($text, "(?<![a-zA-Z0-9_-])${prefix}([a-z])", '$1')
    }
    return $text
}

$script:scopeKws = @('if','when','let','act','match','lambda','\','handle','fork','await','trying')
$script:nonAtomKws = @('let','in','act','end','qed','match','when','is','if','then','else','lambda','handle','fork','await','of','where','with')

function Get-OpPrec([string]$tok) {
    switch ($tok) {
        '||' { return 1 } '&' { return 2 } '|' { return 2 }
        '==' { return 3 } '/=' { return 3 } '<' { return 3 } '>' { return 3 } '<=' { return 3 } '>=' { return 3 }
        '::' { return 4 }
        '+' { return 5 } '-' { return 5 }
        '*' { return 6 } '/' { return 6 } '%' { return 6 }
        '^' { return 7 }
        '.' { return 20 }
        default { return 0 }
    }
}

function Is-AtomToken([string]$tok) {
    if (-not $tok) { return $false }
    $c = $tok[0]
    if ($c -eq '"' -or $c -eq "'") { return $true }
    if (-not ([char]::IsLetterOrDigit($c) -or $c -eq '_')) { return $false }
    return $tok -notin $script:nonAtomKws
}

function Find-MatchingClose([System.Collections.Generic.List[string]]$tokens, [int]$openIdx) {
    $depth = 1
    for ($j = $openIdx + 1; $j -lt $tokens.Count; $j++) {
        if ($tokens[$j] -in '(','[','{') { $depth++ }
        elseif ($tokens[$j] -in ')',']','}') {
            $depth--
            if ($depth -eq 0) { return $(if ($tokens[$j] -eq ')') { $j } else { -1 }) }
        }
    }
    return -1
}

function Strip-RedundantParens([System.Collections.Generic.List[string]]$tokens) {
    $AppPrec = 10
    $changed = $true
    while ($changed) {
        $changed = $false
        for ($i = 0; $i -lt $tokens.Count; $i++) {
            if ($tokens[$i] -ne '(') { continue }
            $close = Find-MatchingClose $tokens $i
            if ($close -lt 0) { break }
            $innerLen = $close - $i - 1
            if ($innerLen -le 0) { $tokens.RemoveAt($close); $tokens.RemoveAt($i); $changed = $true; break }
            if ($innerLen -eq 1) { $tokens.RemoveAt($close); $tokens.RemoveAt($i); $changed = $true; break }
            # ( [ ... ] ) — parens around a bracketed expression are always redundant
            if ($tokens[$i+1] -eq '[') {
                $bracketClose = Find-MatchingClose $tokens $i  # finds the ) not ]
                # check if inner is exactly one [...] group
                $depth3 = 0; $allBracketed = $true
                for ($k = $i+1; $k -lt $close; $k++) {
                    if ($tokens[$k] -eq '[') { $depth3++ } elseif ($tokens[$k] -eq ']') { $depth3-- }
                    if ($depth3 -eq 0 -and $k -lt $close - 1) { $allBracketed = $false; break }
                }
                if ($allBracketed) { $tokens.RemoveAt($close); $tokens.RemoveAt($i); $changed = $true; break }
            }

            $first = $tokens[$i + 1]
            if ($first -in $script:scopeKws) {
                $nextTok = if ($close -lt $tokens.Count - 1) { $tokens[$close + 1] } else { $null }
                $rp = if ($nextTok) { $p = Get-OpPrec $nextTok; if ($p -gt 0) { $p } elseif (Is-AtomToken $nextTok) { $AppPrec } else { 0 } } else { 0 }
                if ($rp -eq 0) { $tokens.RemoveAt($close); $tokens.RemoveAt($i); $changed = $true; break }
                continue
            }

            $innerOps = [System.Collections.Generic.List[object]]::new()
            $depth2 = 0; $afterOp = $false
            for ($k = $i + 1; $k -lt $close; $k++) {
                if ($tokens[$k] -in '(','[','{') { $depth2++; $afterOp = $false; continue }
                if ($tokens[$k] -in ')',']','}') { $depth2--; $afterOp = $true; continue }
                if ($depth2 -gt 0) { continue }
                $opP = Get-OpPrec $tokens[$k]
                if ($opP -gt 0 -and $afterOp) { $innerOps.Add(@($tokens[$k], $opP)); $afterOp = $false }
                elseif ($opP -eq 0) { $afterOp = $true }
            }

            $prev = if ($i -gt 0) { $tokens[$i - 1] } else { $null }
            $next = if ($close -lt $tokens.Count - 1) { $tokens[$close + 1] } else { $null }
            $lp = if ($prev) { $p = Get-OpPrec $prev; if ($p -gt 0) { $p } elseif ($prev -in ')',']','}') { $AppPrec } elseif (Is-AtomToken $prev) { $AppPrec } else { 0 } } else { 0 }
            $rp = if ($next) { $p = Get-OpPrec $next; if ($p -gt 0) { $p } elseif ($next -in '(','[','{') { $AppPrec } elseif (Is-AtomToken $next) { $AppPrec } else { 0 } } else { 0 }

            if ($innerOps.Count -eq 0) {
                $leftOK = $lp -lt $AppPrec -or $lp -eq 0
                $rightOK = $rp -le $AppPrec -or $rp -eq 0
                if ($leftOK -and $rightOK) { $tokens.RemoveAt($close); $tokens.RemoveAt($i); $changed = $true; break }
            } else {
                $precL = $innerOps[0][1]; $opL = $innerOps[0][0]
                $precR = $innerOps[-1][1]; $opR = $innerOps[-1][0]
                $isLAssocL = $opL -ne '^' -and $opL -ne '::'
                $isLAssocR = $opR -ne '^' -and $opR -ne '::'
                $leftOK = $lp -eq 0 -or $lp -lt $precL -or ($lp -eq $precL -and $isLAssocL)
                $rightOK = $rp -eq 0 -or $rp -lt $precR -or ($rp -eq $precR -and $isLAssocR)
                if ($leftOK -and $rightOK) { $tokens.RemoveAt($close); $tokens.RemoveAt($i); $changed = $true; break }
            }
        }
    }
}

function Normalize-OpAliases([System.Collections.Generic.List[string]]$tokens) {
    for ($i = 0; $i -lt $tokens.Count; $i++) {
        if ($tokens[$i] -eq ',' -and $i + 1 -lt $tokens.Count -and $tokens[$i+1].Length -gt 0 -and [char]::IsUpper($tokens[$i+1][0])) { $tokens[$i] = '->' }
    }
    # Normalize #HEX literals to decimal (the text emitter canonicalizes hex
    # literals to their decimal value; bit-pattern semantics, two's complement)
    for ($i = $tokens.Count - 2; $i -ge 0; $i--) {
        if ($tokens[$i] -eq '#' -and $tokens[$i+1] -match '^[0-9A-Fa-f_]+$') {
            $hex = $tokens[$i+1] -replace '_',''
            $u = [System.Convert]::ToUInt64($hex, 16)
            $v = [System.BitConverter]::ToInt64([System.BitConverter]::GetBytes($u), 0)
            if ($v -ge 0) {
                $tokens.RemoveAt($i+1); $tokens[$i] = "$v"
            } else {
                $tokens[$i] = '-'; $tokens[$i+1] = "$(-([System.Numerics.BigInteger]$v))"
            }
        }
    }
    # Normalize (__linked-list-empty 0) or __linked-list-empty 0 → [] (emitter desugars list literal)
    for ($i = $tokens.Count - 2; $i -ge 0; $i--) {
        if ($tokens[$i] -eq '__linked-list-empty' -and $tokens[$i+1] -eq '0') {
            if ($i -gt 0 -and $i + 2 -lt $tokens.Count -and $tokens[$i-1] -eq '(' -and $tokens[$i+2] -eq ')') {
                $tokens.RemoveAt($i+2); $tokens.RemoveAt($i+1); $tokens.RemoveAt($i); $tokens[$i-1] = '['; $tokens.Insert($i, ']')
            } else {
                $tokens[$i] = '['; $tokens[$i+1] = ']'
            }
        }
    }
}

function Collapse-Whitespace([string]$text) {
    $tokens = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($text, '[a-zA-Z0-9_][a-zA-Z0-9_\-]*|"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*''|::|->|<-|<=|>=|/=|&&|\|\||\S')) {
        $tokens.Add($m.Value)
    }
    Normalize-OpAliases $tokens
    Strip-RedundantParens $tokens
    return ($tokens -join ' ').Trim()
}

function Alpha-NormalizeTypeVars([string]$sig) {
    $varMap = @{}
    $nextIdx = 0
    $knownTypes = @('in','if','of','is','act','end','qed')
    $tokens = [regex]::Matches($sig, '\b([a-z]\d*)\b|.')
    $sb = [System.Text.StringBuilder]::new()
    foreach ($tok in $tokens) {
        $full = $tok.Value
        if ($full.Length -eq 1 -and [char]::IsLower($full[0]) -and $full -notin $knownTypes) {
            if (-not $varMap.ContainsKey($full)) { $varMap[$full] = "t$nextIdx"; $nextIdx++ }
            [void]$sb.Append($varMap[$full])
        } elseif ($full -match '^[a-z]\d+$' -and $full -notin $knownTypes) {
            if (-not $varMap.ContainsKey($full)) { $varMap[$full] = "t$nextIdx"; $nextIdx++ }
            [void]$sb.Append($varMap[$full])
        } else {
            [void]$sb.Append($full)
        }
    }
    return $sb.ToString()
}

# -- Parse --
$r0 = Parse-Stage0 $sourceText
$s0Chapters = $r0[0]
$slugs = $r0[1]
$slugsSorted = $slugs.ToArray() | Sort-Object { $_.Length } -Descending

# Demangle stage0 chapter-prefixed names
foreach ($ch in @($s0Chapters.Keys)) {
    $defs = $s0Chapters[$ch]
    for ($di = 0; $di -lt $defs.Count; $di++) {
        $dm = Demangle-Name $defs[$di].Name $slugsSorted
        if ($dm -and $dm[0] -eq $ch) { $defs[$di].Name = $dm[1] }
    }
}

$s1Defs = Parse-Stage1 $stage1Text

# Build collision set
$nameToChapters = @{}
foreach ($ch in $s0Chapters.Keys) {
    foreach ($d in $s0Chapters[$ch]) {
        if (-not $nameToChapters.ContainsKey($d.Name)) { $nameToChapters[$d.Name] = @() }
        $nameToChapters[$d.Name] += $ch
    }
}
$colliding = @($nameToChapters.Keys | Where-Object { $nameToChapters[$_].Count -gt 1 })

# Assign chapters to stage1 defs
$nameToChapter = @{}
foreach ($ch in $s0Chapters.Keys) {
    foreach ($d in $s0Chapters[$ch]) {
        if ($d.Name -notin $colliding -and -not $nameToChapter.ContainsKey($d.Name)) {
            $nameToChapter[$d.Name] = $ch
        }
    }
}
for ($i = 0; $i -lt $s1Defs.Count; $i++) {
    $d = $s1Defs[$i]
    $dm = Demangle-Name $d.Name $slugsSorted
    if ($dm) { $d.Name = $dm[1]; $d.Chapter = $dm[0] }
    elseif ($nameToChapter.ContainsKey($d.Name)) { $d.Chapter = $nameToChapter[$d.Name] }
    else { $d.Chapter = '?' }
}

# -- Match --
$s1ByKey = @{}
foreach ($d in $s1Defs) {
    $key = "$($d.Chapter)|$($d.Name)"
    if (-not $s1ByKey.ContainsKey($key)) { $s1ByKey[$key] = $d }
}

$matched = [System.Collections.Generic.List[object]]::new()
$dropped = [System.Collections.Generic.List[object]]::new()
$s0Keys = @{}
foreach ($ch in $s0Chapters.Keys) {
    foreach ($s0 in $s0Chapters[$ch]) {
        $key = "$ch|$($s0.Name)"
        $s0Keys[$key] = $true
        if ($s1ByKey.ContainsKey($key)) { $matched.Add(@($s0, $s1ByKey[$key])) }
        else { $dropped.Add($s0) }
    }
}
$extra = @($s1Defs | Where-Object { -not $s0Keys.ContainsKey("$($_.Chapter)|$($_.Name)") })

# Resolve colliding defs by body/sig match
$extraByName = @{}
foreach ($d in $extra) {
    if ($d.Chapter -ne '?') { continue }
    if (-not $extraByName.ContainsKey($d.Name)) { $extraByName[$d.Name] = [System.Collections.Generic.List[object]]::new() }
    $extraByName[$d.Name].Add($d)
}
$resolvedDropped = @()
$resolvedExtra = [System.Collections.Generic.HashSet[object]]::new()
foreach ($s0 in $dropped) {
    if (-not $extraByName.ContainsKey($s0.Name)) { continue }
    $body0 = Collapse-Whitespace (Demangle-Names $s0.Body $slugsSorted)
    $bestMatch = $null
    foreach ($s1 in $extraByName[$s0.Name]) {
        if ($resolvedExtra.Contains($s1)) { continue }
        $body1 = Collapse-Whitespace (Demangle-Names $s1.Body $slugsSorted)
        if ($body0 -eq $body1) { $bestMatch = $s1; break }
    }
    if (-not $bestMatch) {
        $sig0 = Alpha-NormalizeTypeVars $s0.Sig
        foreach ($s1 in $extraByName[$s0.Name]) {
            if ($resolvedExtra.Contains($s1)) { continue }
            if ($sig0 -eq (Alpha-NormalizeTypeVars $s1.Sig)) { $bestMatch = $s1; break }
        }
    }
    if ($bestMatch) {
        $bestMatch.Chapter = $s0.Chapter
        $matched.Add(@($s0, $bestMatch))
        $resolvedDropped += $s0
        [void]$resolvedExtra.Add($bestMatch)
    }
}
$dropped = [System.Collections.Generic.List[object]]::new(@($dropped | Where-Object { $_ -notin $resolvedDropped }))
$extra = @($extra | Where-Object { -not $resolvedExtra.Contains($_) })

# -- Compare --
$bodyMatches = 0; $bodyMismatches = 0
$sigMatches = 0; $sigMismatches = 0
$bodyMismatchList = [System.Collections.Generic.List[string]]::new()

foreach ($pair in $matched) {
    $s0 = $pair[0]; $s1 = $pair[1]
    $normSig0 = Alpha-NormalizeTypeVars ($s0.Sig -replace ',\s*', ' -> ')
    $normSig1 = Alpha-NormalizeTypeVars ($s1.Sig -replace ',\s*', ' -> ')
    if ($normSig0 -eq $normSig1) { $sigMatches++ } else { $sigMismatches++ }

    $b0 = Collapse-Whitespace (Demangle-Names $s0.Body $slugsSorted)
    $b1 = Collapse-Whitespace (Demangle-Names $s1.Body $slugsSorted)
    if ($b0 -eq $b1) { $bodyMatches++ }
    else {
        $bodyMismatches++
        $bodyMismatchList.Add("$($s0.Chapter): $($s0.Name)")
    }
}

# -- Verdict --
$pass = $dropped.Count -eq 0 -and $bodyMismatches -eq 0

if (-not $pass) {
    $totalS0 = ($s0Chapters.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
    Write-Host "=== Semantic Equivalence FAIL ==="
    Write-Host "Source: $totalS0 defs  Stage1: $($s1Defs.Count) defs  Matched: $($matched.Count)"
    Write-Host "Dropped: $($dropped.Count)  Body mismatches: $bodyMismatches"
    if ($dropped.Count -gt 0) {
        Write-Host "Dropped:"
        foreach ($d in $dropped) { Write-Host "  $($d.Chapter): $($d.Name) (line $($d.LineNo))" }
    }
    if ($bodyMismatchList.Count -gt 0) {
        Write-Host "Body mismatches:"
        foreach ($m in $bodyMismatchList) { Write-Host "  $m" }
    }
}

exit $(if ($pass) { 0 } else { 1 })
