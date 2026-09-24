param(
    [string]$Kernel = 'seed\Codex.cdx',
    [string[]]$Programs = @(),
    # A FILE of program paths, one per line: pwsh -File splits an array into
    # separate positional arguments, so -Programs binds only from in-process calls.
    [string]$ProgramsFile = '',
    [string]$CorpusDir = '',
    [int]$Limit = 0,
    [switch]$Passes,
    [int]$Expect = -1,
    [switch]$Grade
)

# Does every application on the typed IR wire agree with itself?
#
# The rule is Steve Howell's (GitHub issue 153, his apply_check.py in
# showell/codex-zig-ladder outbound/evidence-issue153-census), re-implemented
# here over ir-wire.ps1 so that it runs on this box with nothing else installed:
#
#   (apply F A T)                  F's type is (fn P R ...), P agrees with A's
#                                  type and R agrees with T
#   (list-expr (elems ...) E)      every element's type agrees with E and
#                                  carries no `error`
#
# `(tvar N)` agrees with anything, because an unresolved variable says it does
# not know. Spellings the wire uses interchangeably are equal: a bounded
# `(int ...)` is `int-default`, a `(unit N C)` is its carrier C, and
# `record-ty` / `sum` of one name are `ctd` of that name.
#
# What a CLEAN result does NOT mean: a node whose type this reader cannot name
# (only the heads in $TypedLast carry their type last, and a `let` takes its
# body's) is
# never compared, and an application whose function type is not an `fn` cell
# is skipped. Every such skip is silent by construction, so a clean run is a
# statement about the applications the reader could type, not about the wire.
#
# It is an instrument and not a gate: a site is a wrong type on the wire, and
# every program measured so far RAN correctly with those sites in it.
#
#   pwsh build/ir-fidelity/ir-apply-agree.ps1 -Programs codex\test\tuple-syntax.codex
#   pwsh build/ir-fidelity/ir-apply-agree.ps1 -CorpusDir codex\test -Limit 50
#   pwsh build/ir-fidelity/ir-apply-agree.ps1 -Grade

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ir-wire.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not [System.IO.Path]::IsPathRooted($Kernel)) { $Kernel = Join-Path $repoRoot $Kernel }
$work = Join-Path $env:TEMP ("irapply-" + [System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Force $work | Out-Null

$LitTypes = @{ 'int-lit' = 'int-default'; 'text-lit' = 'text'; 'bool-lit' = 'boolean'; 'char-lit' = 'char'; 'num-lit' = 'real' }
$TypedLast = @('name', 'apply', 'binary', 'if', 'match', 'field-access', 'record', 'act', 'lambda', 'negate', 'error')

function Get-TypeOf($e) {
    $h = Get-IrHead $e
    if (-not $h) { return $null }
    if ($LitTypes.ContainsKey($h)) { return [IrAtom]::new($LitTypes[$h], $false) }
    # (let "n" T value body): T is the BINDER's type (Lowering.codex builds
    # IrLet name binder-type value body), so the expression's type is its body's.
    if ($h -eq 'let' -and $e.Count -ge 5) { return (Get-TypeOf $e[4]) }
    if ($h -eq 'list-expr' -and $e.Count -ge 3) { return , @([IrAtom]::new('list', $false), $e[2]) }
    if ($TypedLast -contains $h -and $e.Count -ge 2) { return $e[$e.Count - 1] }
    return $null
}

function Get-NormType($t) {
    $h = Get-IrHead $t
    if ($h -eq 'int') { return [IrAtom]::new('int-default', $false) }
    if ($h -eq 'unit' -and $t.Count -ge 3) { return (Get-NormType $t[2]) }
    if (($h -eq 'record-ty' -or $h -eq 'sum') -and $t.Count -ge 2) {
        return , (@([IrAtom]::new('ctd', $false)) + @($t[1..($t.Count - 1)]))
    }
    return $t
}

function Test-TypeAgree($a, $b) {
    if ($null -eq $a -or $null -eq $b) { return $true }
    $a = Get-NormType $a
    $b = Get-NormType $b
    $ha = Get-IrHead $a
    $hb = Get-IrHead $b
    if ($ha -eq 'tvar' -or $hb -eq 'tvar') { return $true }
    $la = $a -isnot [IrAtom]
    $lb = $b -isnot [IrAtom]
    if ($la -and $lb) {
        if ($a.Count -ne $b.Count -or $ha -ne $hb) {
            if ($ha -eq 'fn' -and $hb -eq 'fn' -and $a.Count -ge 3 -and $b.Count -ge 3) {
                return ((Test-TypeAgree $a[1] $b[1]) -and (Test-TypeAgree $a[2] $b[2]))
            }
            return $false
        }
        if ($ha -eq 'row') { return $true }
        for ($k = 1; $k -lt $a.Count; $k++) {
            if (-not (Test-TypeAgree $a[$k] $b[$k])) { return $false }
        }
        return $true
    }
    return ((Format-IrNode $a) -eq (Format-IrNode $b))
}

function Get-SpineHead($f) {
    while ((Get-IrHead $f) -eq 'apply') { $f = $f[1] }
    if ((Get-IrHead $f) -eq 'name' -and $f.Count -ge 2) { return $f[1].Value }
    $h = Get-IrHead $f
    if ($h) { return "<$h>" } else { return '<?>' }
}

function Test-CarriesError($t) {
    if ($null -eq $t) { return $false }
    if ($t -is [IrAtom]) { return (-not $t.Quoted -and $t.Value -eq 'error') }
    foreach ($c in $t) { if ($c -is [IrAtom] -and -not $c.Quoted -and $c.Value -eq 'error') { return $true } }
    return $false
}

function Find-ApplySites($node, [string]$def, $out) {
    if ($null -eq $node -or $node -is [IrAtom] -or $node.Count -eq 0) { return }
    $h = Get-IrHead $node
    if ($h -eq 'def' -and $node.Count -ge 2 -and $node[1] -is [IrAtom]) { $def = $node[1].Value }
    if ($h -eq 'apply' -and $node.Count -ge 4) {
        $ft = Get-TypeOf $node[1]
        if ((Get-IrHead $ft) -eq 'fn' -and $ft.Count -ge 3) {
            $at = Get-TypeOf $node[2]
            if (-not (Test-TypeAgree $ft[1] $at)) {
                [void]$out.Add([pscustomobject]@{ Def = $def; Kind = 'arg'; Head = (Get-SpineHead $node[1]); Want = (Format-IrNode $ft[1]); Got = (Format-IrNode $at) })
            }
            if (-not (Test-TypeAgree $ft[2] $node[3])) {
                [void]$out.Add([pscustomobject]@{ Def = $def; Kind = 'result'; Head = (Get-SpineHead $node[1]); Want = (Format-IrNode $ft[2]); Got = (Format-IrNode $node[3]) })
            }
        }
    }
    if ($h -eq 'list-expr' -and $node.Count -ge 3 -and $node[1] -isnot [IrAtom]) {
        $want = $node[2]
        for ($k = 1; $k -lt $node[1].Count; $k++) {
            $got = Get-TypeOf $node[1][$k]
            if (-not (Test-TypeAgree $want $got) -or (Test-CarriesError $got)) {
                [void]$out.Add([pscustomobject]@{ Def = $def; Kind = 'element'; Head = 'list-expr'; Want = (Format-IrNode $want); Got = (Format-IrNode $got) })
            }
        }
    }
    $start = if ($node[0] -isnot [IrAtom]) { 0 } else { 1 }
    for ($k = $start; $k -lt $node.Count; $k++) { Find-ApplySites $node[$k] $def $out }
}

function Get-WireSites([string]$Wire) {
    $out = [System.Collections.Generic.List[object]]::new()
    Find-ApplySites (ConvertFrom-IrWire -Text $Wire) '' $out
    return , $out
}

function Invoke-ApplyCompile([string]$Src, [string]$KernelPath) {
    $tag = [System.IO.Path]::GetFileNameWithoutExtension($Src) + '-' + [System.IO.Path]::GetRandomFileName()
    $log = Join-Path $work "$tag.log"
    $cargs = @{ Src = $Src; Out = (Join-Path $work "$tag.ir"); Log = $log; Kernel = $KernelPath; IrUni = $true }
    if (-not $Passes) { $cargs['Passes'] = 'none' }
    Push-Location $repoRoot
    try { & (Join-Path $repoRoot 'build\compile.ps1') @cargs 2>&1 | Out-Null }
    finally { Pop-Location }
    return (Get-IrWireText -LogPath $log)
}

if ($Grade) {
    $fail = 0
    function Check([string]$name, [bool]$ok, [string]$detail = '') {
        if ($ok) { Write-Output "  ok    $name" } else { Write-Output "  FAIL  $name $detail"; $script:fail++ }
    }
    Write-Output '--- the rule, on hand-written wires ---'
    $bad = '(chapter (defs (def "f" "C" (params) int-default (apply (name "g" (fn int-default int-default)) (name "x" (list int-default)) int-default) 0 0)))'
    $res = '(chapter (defs (def "f" "C" (params) int-default (apply (name "g" (fn int-default text)) (int-lit 1) int-default) 0 0)))'
    $good = '(chapter (defs (def "f" "C" (params) int-default (apply (name "g" (fn (list int-default) int-default)) (name "x" (list int-default)) int-default) 0 0)))'
    $tv = '(chapter (defs (def "f" "C" (params) int-default (apply (name "g" (fn (tvar 3) int-default)) (name "x" (list int-default)) int-default) 0 0)))'
    $err = '(chapter (defs (def "f" "C" (params) int-default (list-expr (elems (list-expr (elems) error)) int-default) 0 0)))'
    $bnd = '(chapter (defs (def "f" "C" (params) int-default (apply (name "g" (fn int-default int-default)) (name "x" (int 0 255 trapping)) int-default) 0 0)))'
    Check 'a list applied where Integer is wanted is a site' ((Get-WireSites $bad).Count -eq 1)
    Check 'a result that disagrees is a site' ((Get-WireSites $res).Count -eq 1)
    Check 'an agreeing apply is not a site' ((Get-WireSites $good).Count -eq 0)
    Check 'a tvar parameter agrees with anything' ((Get-WireSites $tv).Count -eq 0)
    Check 'an element carrying error is a site' ((Get-WireSites $err).Count -ge 1)
    Check 'a bounded Integer is an Integer' ((Get-WireSites $bnd).Count -eq 0)
    $letok = '(chapter (defs (def "f" "C" (params) text (apply (name "show" (fn int-default text)) (let "xs" (list int-default) (name "ys" (list int-default)) (int-lit 1)) text) 0 0)))'
    $letbad = '(chapter (defs (def "f" "C" (params) text (apply (name "show" (fn int-default text)) (let "n" int-default (int-lit 1) (name "ys" (list int-default))) text) 0 0)))'
    Check 'a let is typed by its body, not its binder' ((Get-WireSites $letok).Count -eq 0)
    Check 'a let whose body disagrees is a site' ((Get-WireSites $letbad).Count -eq 1)

    Write-Output '--- live wires: seeds that shipped each defect must be caught ---'
    $cases = Join-Path $PSScriptRoot 'apply-cases'
    $old = @{}
    foreach ($rev in 800, 801) {
        $p = Join-Path $work "seed$rev.cdx"
        & p4 print -q -o $p "//Codex/main/seed/Codex.cdx#$rev" 2>&1 | Out-Null
        if (-not (Test-Path $p)) { Write-Output "  FAIL  could not fetch seed #$rev"; exit 1 }
        $old[$rev] = $p
    }
    $arms = @(
        @{ Src = 'tuple-two-lists'; Kernel = $old[801]; Want = 'caught'; Label = 'MkTup2 typed Integer, seed #801 (before COMPILER-85)' },
        @{ Src = 'nested-empty'; Kernel = $old[800]; Want = 'caught'; Label = '[[]] carrying error, seed #800 (before issue 153)' },
        @{ Src = 'agreeing-applies'; Kernel = $old[800]; Want = 'clean'; Label = 'agreeing applies, seed #800' },
        @{ Src = 'tuple-two-lists'; Kernel = $Kernel; Want = 'clean'; Label = 'MkTup2, kernel under test' },
        @{ Src = 'nested-empty'; Kernel = $Kernel; Want = 'clean'; Label = '[[]], kernel under test' },
        @{ Src = 'agreeing-applies'; Kernel = $Kernel; Want = 'clean'; Label = 'agreeing applies, kernel under test' }
    )
    foreach ($arm in $arms) {
        $wire = Invoke-ApplyCompile (Join-Path $cases "$($arm.Src).codex") $arm.Kernel
        if (-not $wire) { Check $arm.Label $false '(no wire)'; continue }
        $n = (Get-WireSites $wire).Count
        $got = if ($n -gt 0) { 'caught' } else { 'clean' }
        Check "$($arm.Label): $got ($n)" ($got -eq $arm.Want) "want $($arm.Want)"
    }
    if ($fail -gt 0) { Write-Output "GRADE: $fail FAILED"; exit 1 }
    Write-Output 'GRADE: all passed'
    exit 0
}

if ($ProgramsFile) { $Programs = @(Get-Content $ProgramsFile | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
if ($Programs.Count -eq 0) {
    if (-not $CorpusDir) { Write-Output 'give -Programs, -CorpusDir or -Grade'; exit 2 }
    $all = @(Get-ChildItem -Path (Join-Path $repoRoot $CorpusDir) -Filter *.codex -File | Sort-Object Name | ForEach-Object FullName)
    $Programs = if ($Limit -gt 0) { @($all | Select-Object -First $Limit) } else { $all }
    Write-Output "corpus: $CorpusDir, $($Programs.Count) of $($all.Count) programs$(if ($Limit -gt 0) { ' (capped by -Limit)' })"
}

$kernelHash = (Get-FileHash -Algorithm SHA256 $Kernel).Hash.Substring(0, 16)
Write-Output "kernel: $Kernel [$kernelHash]"
Write-Output "passes: $(if ($Passes) { 'pipeline' } else { 'none' })"
$total = 0; $noWire = 0; $withSites = 0
$byHead = @{}
foreach ($src in $Programs) {
    $path = if ([System.IO.Path]::IsPathRooted($src)) { $src } else { Join-Path $repoRoot $src }
    $name = [System.IO.Path]::GetFileNameWithoutExtension($path)
    $wire = Invoke-ApplyCompile $path $Kernel
    if (-not $wire) { $noWire++; Write-Output "NO-WIRE  $name"; continue }
    $sites = Get-WireSites $wire
    if ($sites.Count -gt 0) { $withSites++ }
    foreach ($s in $sites) {
        $total++
        $byHead[$s.Head] = 1 + $(if ($byHead.ContainsKey($s.Head)) { $byHead[$s.Head] } else { 0 })
        Write-Output ("SITE  {0}  {1}  {2}  {3}  wants {4}  got {5}" -f $name, $s.Def, $s.Kind, $s.Head, $s.Want, $s.Got)
    }
}
Write-Output ''
Write-Output "programs: $($Programs.Count), no wire (did not compile clean): $noWire, with sites: $withSites"
foreach ($k in ($byHead.Keys | Sort-Object)) { Write-Output ("  {0,-30} {1}" -f $k, $byHead[$k]) }
Write-Output "sites: $total"
if ($Expect -ge 0 -and $total -ne $Expect) { Write-Output "EXPECTED $Expect sites, measured $total"; exit 1 }
exit 0
