# cite-gate.ps1 -- run the codex/test chapters that CITE what you changed.
#
# L-NOGATE: a test in no lane's gate goes red at head and stays red until a
# release finds it, and a static instrument cannot observe a runtime defect.
# The lesson's own candidate runner is this one: the gate RUNS, not merely
# compiles, the codex/test chapters citing changed source.
#
# WHAT IT SELECTS, and the direction is the point. It does not compute each
# test's cite closure and ask whether your change is in it; it walks the cite
# graph BACKWARDS from the files you changed, over "who cites me" edges, and
# takes every codex/test chapter it reaches. One walk over the graph rather
# than one closure per test, which is what makes 1,800 test chapters
# affordable. A changed test chapter selects itself.
#
# WHAT IT CANNOT SEE, stated because a selection reads like a guarantee:
#   - A test that reaches your change through no cite at all is not selected.
#     The compiler is assembled by GLOB (concat-codex-self.ps1), so a compiler
#     chapter's callers do not have to cite it and mostly do not. Pass
#     -Compiler to select every compiler-citing test regardless, or use the
#     BVT, which is what the seed chain already grades the compiler with.
#   - EXCEPT the allocation goldens: a codex/test chapter that reads the heap
#     frontier (alloc-bytes, __heap-save) prints numbers the COMPILER decides,
#     so any change under codex/compiler selects every one of them. A test
#     that prints a frontier value read inside a LIBRARY chapter is not; on
#     2026-09-23 the two libraries that print one (PerfMonitor perf-heap,
#     DiagnosticShell diag-run-heap) reached no .expected.
#   - A subject with no .expected is COMPILED and not RUN. bvt.ps1 grades it
#     as a compile, and this script reports the two counts apart rather than
#     summing them, because "42 subjects" over 6 that can fail at runtime is
#     the L-DENOM shape.
#
# WHAT IT COSTS, measured 2026-09-09 over two batches rather than derived from
# one subject times N (L-AMORTISED): 12 subjects compiled and run in 31.5 s and
# 24 in 72.7 s, both at -Jobs 1 on seed 7925988B55B1B5B5, so a subject costs
# about 2.6 to 3.0 s amortised and the two points do not fit one line, because
# subject cost varies more than any fixed setup does. A change to a
# widely-cited core chapter selects a lot: Fat16 selects 167 chapters, about
# eight to ten minutes at -Jobs 1. Read the count before starting the run.
#
# It boots a guest per subject through bvt.ps1 -Jobs. Fan-outs are self-serve
# on a runtime measurement: measure free memory against the per-guest bar in
# CoordinationProtocol.md ("The token does not cover RAM"), and name the run
# in status.json. bvt.ps1 does not clamp its slots to free memory.
#
# Usage:
#   build/cite-gate.ps1                            # changed = p4 opened here
#   build/cite-gate.ps1 -Files a.codex,b.codex     # changed = these
#   build/cite-gate.ps1 -ListOnly                  # the selection, no run
#   build/cite-gate.ps1 -Jobs 16 -Kernel seed\Codex.cdx
[CmdletBinding()]
param(
    [string[]]$Files = @(),
    # A FILE of paths, one per line, for a caller that cannot bind an array:
    # `pwsh -File` splits an array into positional arguments and only the
    # first element binds. bvt.ps1 -SubjectsFile carries the same note.
    [string]$FilesFile = '',
    [string]$Kernel = '',
    [int]$Jobs = 16,
    [switch]$ListOnly,
    # Select every test that cites ANY compiler chapter. The compiler is
    # assembled by glob, so its callers need no cite and the graph under-reads
    # a compiler change; this is the honest widening rather than a claim that
    # the walk found them.
    [switch]$Compiler
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
. (Join-Path $PSScriptRoot 'quire-map.ps1')

if ($Kernel -eq '') { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) {
    [Console]::Error.WriteLine("MISSING: $Kernel")
    exit 2
}

# -- what changed ------------------------------------------------------------
$changedPaths = @($Files)
if ($FilesFile) {
    if (-not (Test-Path -PathType Leaf $FilesFile)) { [Console]::Error.WriteLine("MISSING -FilesFile: $FilesFile"); exit 2 }
    $changedPaths += @(Get-Content $FilesFile | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
}
if ($changedPaths.Count -eq 0) {
    # p4 opened names depot paths; -Ac1 would name client paths but not every
    # server answers it, so the depot path is mapped by its tail instead.
    $opened = @(& p4 opened 2>$null | ForEach-Object {
        if ($_ -match '^//Codex/[^/]+/(.+?)#') { $matches[1] -replace '/', '\' }
    })
    $changedPaths = @($opened)
    if ($changedPaths.Count -gt 0) { Write-Host "[cite-gate] changed set from p4 opened: $($changedPaths.Count) file(s)" }
}
$changedFull = @()
foreach ($c in $changedPaths) {
    $p = if ([System.IO.Path]::IsPathRooted($c)) { $c } else { Join-Path $Repo $c }
    if ($p -notlike '*.codex') { continue }
    $changedFull += (($p -replace '/', '\').ToLowerInvariant())
}
$changedFull = @($changedFull | Sort-Object -Unique)
if ($changedFull.Count -eq 0) {
    Write-Host '[cite-gate] no changed .codex files, so no test cites anything you changed. Nothing to run.'
    exit 0
}

# -- the cite graph ----------------------------------------------------------
# One pass over the tree: each chapter's file, its declared name, and the
# chapters it cites. A cite is resolved the way plug-build-lib resolves one,
# through the quire registry, so two chapters sharing a name in different
# quires stay apart.
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$roots = @('codex', 'apps')
$allChapters = @(Get-ChildItem -Path $roots -Recurse -Filter *.codex -File |
           Where-Object { $_.FullName -notmatch '\\build-output\\|\\old\\' })

# A cite names a chapter two ways and both are in the tree: the FILE's name
# (ByteHelpers) and the HEADER's (Byte Helpers), so a lookup keyed on one form
# misses the other. Get-CiteKey in quire-map.ps1 is the tree's own answer,
# comparing with the spaces removed, and this index is keyed the same way, per
# quire directory.
$dirIndex = @{}
function Get-DirIndex([string]$dir) {
    if ($dirIndex.ContainsKey($dir)) { return $dirIndex[$dir] }
    $ix = @{}
    $full = Join-Path $Repo $dir
    if (Test-Path -PathType Container $full) {
        foreach ($c in (Get-ChildItem -Path $full -Filter *.codex -File)) {
            $ix[(Get-CiteKey $c.BaseName)] = $c.FullName.ToLowerInvariant()
        }
    }
    $dirIndex[$dir] = $ix
    return $ix
}

# A MANIFEST quire is one unit made of many files (Codex, Emit and Semantics
# are all build\compiler-order.txt), so a cite of one names the whole compiler.
# Expanding it is what carries a compiler change out to the tests that cite the
# compiler as a library, which is how DiskTestRunner reaches compile-frontend.
$manifestFiles = @{}
function Get-ManifestFiles([string]$mf) {
    if ($manifestFiles.ContainsKey($mf)) { return $manifestFiles[$mf] }
    $out = [System.Collections.Generic.List[string]]::new()
    $p = Join-Path $Repo $mf
    if (Test-Path -PathType Leaf $p) {
        foreach ($row in Get-Content $p) {
            $r = ($row -split '#')[0].Trim()
            if ($r -eq '') { continue }
            [void]$out.Add((Join-Path $Repo $r).ToLowerInvariant())
        }
    }
    $manifestFiles[$mf] = $out
    return $out
}

$citeCache = @{}
function Resolve-CiteTargets([string]$quire, [string]$name) {
    $key = "${quire}::${name}"
    if ($citeCache.ContainsKey($key)) { return $citeCache[$key] }
    $out = [System.Collections.Generic.List[string]]::new()
    if ($QuireManifests[$quire]) {
        foreach ($m in (Get-ManifestFiles $QuireManifests[$quire])) { [void]$out.Add($m) }
    } elseif ($QuireDirs[$quire]) {
        $hit = (Get-DirIndex $QuireDirs[$quire])[(Get-CiteKey $name)]
        if ($hit) { [void]$out.Add($hit) }
    }
    $citeCache[$key] = $out
    return $out
}

# cited file (lowercase full path) -> the files that cite it
$citers = @{}
$testFiles = [System.Collections.Generic.List[string]]::new()
$compilerCiters = [System.Collections.Generic.List[string]]::new()
$allocGoldens = [System.Collections.Generic.List[string]]::new()
foreach ($f in $allChapters) {
    $full = $f.FullName.ToLowerInvariant()
    $isTest = $full -match '\\codex\\test\\'
    if ($isTest) { [void]$testFiles.Add($full) }
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        if ($isTest -and ($line.Contains('alloc-bytes') -or $line.Contains('__heap-save'))) {
            [void]$allocGoldens.Add($full); $isTest = $false
        }
        # The regex over every line of every chapter is the whole cost of this
        # walk: 3,985 files is about a million lines and the pattern is
        # anchored but backtracking. The literal test in front of it takes the
        # graph from 33 s to about 2 s and changes no answer, because the
        # pattern cannot match a line with no 'cites' in it.
        if ($line.IndexOf('cites') -lt 0) { continue }
        if ($line -notmatch $StrictCitePat) { continue }
        $quire = $matches[1]; $name = $matches[2]
        if (($QuireManifests[$quire]) -or ($QuireDirs[$quire] -like 'codex\compiler*')) { [void]$compilerCiters.Add($full) }
        foreach ($target in (Resolve-CiteTargets $quire $name)) {
            if (-not $citers.ContainsKey($target)) { $citers[$target] = [System.Collections.Generic.List[string]]::new() }
            [void]$citers[$target].Add($full)
        }
    }
}
$graphMs = $sw.ElapsedMilliseconds

# -- walk backwards ----------------------------------------------------------
$seen = @{}
$queue = [System.Collections.Generic.Queue[string]]::new()
foreach ($c in $changedFull) { if (-not $seen.ContainsKey($c)) { $seen[$c] = $true; $queue.Enqueue($c) } }
while ($queue.Count -gt 0) {
    $cur = $queue.Dequeue()
    if (-not $citers.ContainsKey($cur)) { continue }
    foreach ($up in $citers[$cur]) {
        if ($seen.ContainsKey($up)) { continue }
        $seen[$up] = $true
        $queue.Enqueue($up)
    }
}
$selected = @($testFiles | Where-Object { $seen.ContainsKey($_) })
if ($Compiler) {
    $selected = @($selected + @($compilerCiters | Where-Object { $_ -match '\\codex\\test\\' }) | Sort-Object -Unique)
}
$compilerChanged = @($changedFull | Where-Object { $_ -like "$($Repo.ToLowerInvariant())\codex\compiler\*" }).Count -gt 0
if ($compilerChanged) {
    $before = $selected.Count
    $selected = @($selected + $allocGoldens | Sort-Object -Unique)
    Write-Host "[cite-gate] a codex/compiler change: $($selected.Count - $before) allocation golden(s) added, of $($allocGoldens.Count) test chapters reading the heap frontier"
}

# A selected chapter that declares no `opening` and is not an error test (no
# .failing sidecar) is a library a harness splices in; compiled alone it is
# CDX2040 by construction. It is named here, and its citers were walked above.
$library = @($selected | Where-Object {
    -not (Test-Path -PathType Leaf ($_ -replace '\.codex$', '.failing')) -and
    -not (Select-String -Path $_ -Pattern '^\s+opening\s*:' -Quiet) })
$selected = @($selected | Where-Object { $library -notcontains $_ })
foreach ($l in $library) { Write-Host "[cite-gate] library, not compiled (no opening): $($l.Substring($Repo.Length + 1))" }

# Relative paths, because that is what bvt.ps1 -SubjectsFile takes.
$rel = @($selected | ForEach-Object { $_.Substring($Repo.Length + 1) } | Sort-Object -Unique)
$runnable = @($rel | Where-Object { Test-Path (Join-Path $Repo ($_ -replace '\.codex$', '.expected')) })
$compileOnly = @($rel | Where-Object { $runnable -notcontains $_ })

Write-Host "[cite-gate] graph: $($allChapters.Count) chapters, $($citers.Count) cited targets, built in $graphMs ms"
Write-Host "[cite-gate] changed: $($changedFull.Count) file(s); reached $($seen.Count) chapter(s) through cites"
Write-Host "[cite-gate] selected $($rel.Count) test chapter(s): $($runnable.Count) RUN against an .expected, $($compileOnly.Count) compile only"
foreach ($r in $rel) { Write-Host "  $r" }

if ($rel.Count -eq 0) {
    # An empty selection is a statement about the CITE GRAPH, not an all-clear,
    # and it is worth saying out loud: it means no test chapter names what you
    # changed, which is a coverage finding rather than a green run.
    Write-Host '[cite-gate] NOTHING CITES YOUR CHANGE. That is a gap in the corpus, not a pass.'
    exit 0
}
if ($ListOnly) { exit 0 }

$subjectsFile = Join-Path $env:TEMP "cite-gate-subjects-$PID.txt"
[System.IO.File]::WriteAllLines($subjectsFile, [string[]]$rel)
try {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'bvt.ps1') -CodexCdx $Kernel -Jobs $Jobs -SubjectsFile $subjectsFile
    $code = $LASTEXITCODE
} finally {
    Remove-Item -Force $subjectsFile -ErrorAction SilentlyContinue
}
if ($code -ne 0) { Write-Host "[cite-gate] FAIL: the cited tests did not all pass"; exit 1 }

# THE CODEGEN COST PINS. A codex/test chapter with a .census sidecar is compiled
# under the callcensus mode flag, and every CDX6014 line pinned there is a
# ceiling: the definition must keep its emit path (lir beats minimal beats
# standard) and must not grow in instructions, pushes, pops or stack stores. A
# fall is reported so the gain can be re-pinned; a rise, a pinned definition
# that is no longer emitted, or an empty side fails. A pin set by a codegen
# change is NOT verified by any value inside it, only by the run that wrote it:
# read the delta before re-pinning (ExaminersAssay, the LIR dump pin).
function ConvertFrom-CensusLine([string]$line) {
    if ($line -notmatch "\[CALLS\] '([^']+)' path=(\w+) params=\d+ bytes=\d+ insns=(\d+) push=(\d+) pop=(\d+) stack-stores=(\d+)") { return $null }
    $rank = switch ($matches[2]) { 'lir' { 2 } 'minimal' { 1 } default { 0 } }
    [pscustomobject]@{ Name = $matches[1]; Path = $matches[2]; Rank = $rank; Insns = [int]$matches[3]; Push = [int]$matches[4]; Pop = [int]$matches[5]; Stores = [int]$matches[6] }
}
if ($compilerChanged) {
    $worse = 0
    foreach ($pin in @(Get-ChildItem -Path (Join-Path $Repo 'codex\test') -Recurse -Filter *.census -File)) {
        $tag = $pin.FullName.Substring($Repo.Length + 1)
        $cOut = Join-Path $env:TEMP "cite-gate-census-$PID.cdx"
        $cLog = Join-Path $env:TEMP "cite-gate-census-$PID.log"
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'compile.ps1') -Src ($pin.FullName -replace '\.census$', '.codex') -Out $cOut -Log $cLog -Kernel $Kernel -RawFlags callcensus *> $null
        $now = @{}
        if (Test-Path -PathType Leaf $cLog) { foreach ($l in [System.IO.File]::ReadAllLines($cLog)) { $c = ConvertFrom-CensusLine $l; if ($c) { $now[$c.Name] = $c } } }
        Remove-Item -Force $cOut, $cLog -ErrorAction SilentlyContinue
        $pinned = @([System.IO.File]::ReadAllLines($pin.FullName) | ForEach-Object { ConvertFrom-CensusLine $_ } | Where-Object { $_ })
        if ($pinned.Count -eq 0 -or $now.Count -eq 0) { Write-Host "[cite-gate] CENSUS FAIL $tag : $($pinned.Count) pinned, $($now.Count) measured; an empty side proves nothing"; $worse++; continue }
        $better = 0
        foreach ($p in $pinned) {
            $n = $now[$p.Name]
            if (-not $n) { Write-Host "  CENSUS $tag '$($p.Name)': pinned, not emitted"; $worse++; continue }
            $bad = @()
            if ($n.Rank -lt $p.Rank) { $bad += "path $($p.Path) -> $($n.Path)" } elseif ($n.Rank -gt $p.Rank) { $better++ }
            foreach ($k in 'Insns', 'Push', 'Pop', 'Stores') {
                if ($n.$k -gt $p.$k) { $bad += "$k $($p.$k) -> $($n.$k)" } elseif ($n.$k -lt $p.$k) { $better++ }
            }
            if ($bad.Count -gt 0) { Write-Host "  CENSUS $tag '$($p.Name)': $($bad -join ', ')"; $worse++ }
        }
        Write-Host "[cite-gate] census $tag : $($pinned.Count) pinned definition(s), $better value(s) improved$(if ($better -gt 0) { '; re-pin to keep the gain' })"
    }
    if ($worse -gt 0) { Write-Host "[cite-gate] FAIL: $worse codegen cost regression(s) against a .census pin"; exit 1 }
}
Write-Host "[cite-gate] OK: $($rel.Count) cited test chapter(s), $($runnable.Count) of them against an .expected"
exit 0
