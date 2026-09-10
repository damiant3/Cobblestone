# Ratchet the raw-shell-text escape hatches in the generated-script DSL.
#
# ScRaw and SeRaw pass shell text through the ShellCmd tree untouched. Both are
# marked DEPRECATED in codex/foreword/shell/ShellTypes.codex and the marking is
# PROSE, which nothing reads. Measured against the count the campaign design
# recorded on 2026-08-16:
#
#     2026-08-16   5,504 ScRaw   570 SeRaw   35 of 56 generators
#     2026-09-08   7,274 ScRaw   570 SeRaw   37 of 58 generators
#
# So the deprecation added about 1,770 uses in three weeks. A rule with no
# runner is a rule that records an intention, and the campaign it belongs to
# (docs/Designs/Active/Build/ShellDslReadability.md) cannot finish while
# conversion races addition and loses. This is the runner.
#
# Usage:
#   check-shell-raw.ps1              # compare against the baseline, fail a rise
#   check-shell-raw.ps1 -Update      # rewrite EVERY row from the tree
#   check-shell-raw.ps1 -Update -Only bvtScript   # rewrite ONE row, keep the rest
#   check-shell-raw.ps1 -List        # per-generator table, no verdict
#
# -Only exists because a blanket -Update absorbs every lane's rise, not just the
# rise of the lane running it. On 2026-09-08 one blanket run recorded three, of
# which the running lane had caused two SeRaw uses and another lane the other
# seventeen; the rows then read as the wrong lane's debt, and the ratchet had
# quietly granted permission for uses nobody had justified. A lane converts or
# justifies its own generator and moves its own row.
#
# Exit 1 when any generator's count RISES above its baseline, and exit 1 when a
# count FALLS without the baseline being lowered. Failing on an improvement
# looks strange and is the whole mechanism: a baseline left high after a
# conversion permits the raw node to come straight back, which is a ratchet that
# does not ratchet. The fix for that failure is one command, -Update, in the
# same changelist that did the conversion.
#
# IT BOOTS NOTHING. check-generated-scripts.ps1 beside it boots a guest per
# generator and is a run to ask the box for; this reads source text and costs
# nothing, so it can sit in front of the gate rather than beside a release.
[CmdletBinding()]
param(
    [switch]$Update,
    [switch]$List,
    [string]$Only
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$GenDir = Join-Path $Repo 'codex\build'
$Baseline = Join-Path $PSScriptRoot 'shell-raw-baseline.txt'

# Two exclusions, and the check found both by being run against its own wiring.
#
# Column-2 prose is skipped: a prose line begins with ONE space then a
# non-space, a definition begins with two. Without the skip, the header of a
# converted generator explaining why ScRaw left would count as a use, so the
# record would punish the explanation.
#
# STRING LITERALS ARE STRIPPED BEFORE COUNTING. A generator comment is
# `ScComment "..."` on a code line, so a comment naming the constructor it is
# removing counted as a use of that constructor: wiring this very check
# reported +2 ScRaw and +1 SeRaw where one raw node was added. A constructor
# name is always OUTSIDE the quotes and a raw payload is always inside them, so
# stripping the literals separates the two exactly rather than approximately.
#
# The BUILDERS count too. sh-raw and raw-expr are ShellBuild's aliases for
# ScRaw and SeRaw, so counting the constructors alone would let a rename pass
# for a removal.
function Measure-RawUses([string]$path) {
    $sc = 0; $se = 0
    foreach ($line in [System.IO.File]::ReadLines($path)) {
        if ($line -match '^ [^ ]') { continue }
        $code = [regex]::Replace($line, '"(?:[^"\\]|\\.)*"', '""')
        $sc += ([regex]::Matches($code, '\bScRaw\b')).Count
        $sc += ([regex]::Matches($code, '\bsh-raw\b')).Count
        $se += ([regex]::Matches($code, '\bSeRaw\b')).Count
        $se += ([regex]::Matches($code, '\braw-expr\b')).Count
    }
    return @{ ScRaw = $sc; SeRaw = $se }
}

$rows = [ordered]@{}
$allGens = New-Object System.Collections.Generic.List[string]
foreach ($g in (Get-ChildItem (Join-Path $GenDir '*Script.codex') | Sort-Object Name)) {
    $allGens.Add($g.BaseName)
    $m = Measure-RawUses $g.FullName
    if ($m.ScRaw -eq 0 -and $m.SeRaw -eq 0) { continue }
    $rows[$g.BaseName] = $m
}

# A flag that quietly does nothing is the defect this whole check is named for
# (L-ACCEPTED), so -Only refuses rather than falling through to a blanket run.
if ($Only) {
    if (-not $Update) {
        Write-Host 'FAIL: -Only applies to -Update. Without -Update there is no row to move.'
        exit 1
    }
    $match = $allGens | Where-Object { $_ -eq $Only }
    if (-not $match) {
        $match = $allGens | Where-Object { $_ -ieq $Only }
    }
    if (-not $match) {
        Write-Host "FAIL: -Only '$Only' names no generator. codex\build\ holds $($allGens.Count) *Script.codex files; the row key is the file's base name, for example bvtScript."
        exit 1
    }
    $Only = @($match)[0]
}

$totalSc = ($rows.Values | ForEach-Object { $_.ScRaw } | Measure-Object -Sum).Sum
$totalSe = ($rows.Values | ForEach-Object { $_.SeRaw } | Measure-Object -Sum).Sum
if ($null -eq $totalSc) { $totalSc = 0 }
if ($null -eq $totalSe) { $totalSe = 0 }

if ($List) {
    '{0,-34} {1,7} {2,7}' -f 'generator', 'ScRaw', 'SeRaw' | Write-Host
    foreach ($k in $rows.Keys) {
        '{0,-34} {1,7} {2,7}' -f $k, $rows[$k].ScRaw, $rows[$k].SeRaw | Write-Host
    }
    '{0,-34} {1,7} {2,7}' -f "TOTAL ($($rows.Count) generators)", $totalSc, $totalSe | Write-Host
    exit 0
}

function Read-Baseline([string]$path) {
    $t = [ordered]@{}
    foreach ($line in Get-Content $path) {
        if ($line -match '^\s*#' -or $line.Trim() -eq '') { continue }
        $p = $line -split '\s+' | Where-Object { $_ -ne '' }
        if ($p.Count -ge 3) { $t[$p[0]] = @{ ScRaw = [int]$p[1]; SeRaw = [int]$p[2] } }
    }
    return $t
}

if ($Update) {
    # -Only moves ONE row and leaves every other row exactly as the record holds
    # it, so a lane cannot absorb another lane's rise by updating its own.
    if ($Only) {
        if (-not (Test-Path $Baseline)) {
            Write-Host 'FAIL: there is no baseline to move one row in. Write the whole record first with -Update.'
            exit 1
        }
        $merged = Read-Baseline $Baseline
        if ($rows.Contains($Only)) {
            if ($merged.Contains($Only)) {
                $merged[$Only] = $rows[$Only]
            } else {
                # A generator absent from the record has to be placed, and the
                # record is ordered by name.
                $keys = @($merged.Keys) + $Only | Sort-Object
                $rebuilt = [ordered]@{}
                foreach ($k in $keys) {
                    $rebuilt[$k] = if ($k -eq $Only) { $rows[$Only] } else { $merged[$k] }
                }
                $merged = $rebuilt
            }
        } else {
            $merged.Remove($Only)
        }
        $rows = $merged
    }

    $totalSc = ($rows.Values | ForEach-Object { $_.ScRaw } | Measure-Object -Sum).Sum
    $totalSe = ($rows.Values | ForEach-Object { $_.SeRaw } | Measure-Object -Sum).Sum
    if ($null -eq $totalSc) { $totalSc = 0 }
    if ($null -eq $totalSe) { $totalSe = 0 }

    $out = New-Object System.Collections.Generic.List[string]
    $out.Add('# shell-raw-baseline.txt -- generated by build/check-shell-raw.ps1 -Update')
    $out.Add('#')
    $out.Add('# One row per generator that still constructs raw shell text, as')
    $out.Add('#   <generator> <ScRaw> <SeRaw>')
    $out.Add('#')
    $out.Add('# The record is a CEILING and a FLOOR at once: the check fails when a count')
    $out.Add('# rises, and fails when a count falls without this file being lowered in the')
    $out.Add('# same changelist. Shrinking the numbers is the work')
    $out.Add('# (docs/Designs/Active/Build/ShellDslReadability.md).')
    $out.Add('#')
    $stamp = if ($Only) { "Row $Only moved" } else { 'Measured' }
    $out.Add("# $stamp $((Get-Date).ToString('yyyy-MM-dd')): $totalSc ScRaw, $totalSe SeRaw, $($rows.Count) generators.")
    $out.Add('')
    foreach ($k in $rows.Keys) { $out.Add(('{0} {1} {2}' -f $k, $rows[$k].ScRaw, $rows[$k].SeRaw)) }
    Set-Content -Path $Baseline -Value $out -Encoding ascii
    if ($Only) {
        Write-Host "check-shell-raw: row $Only moved, record now $totalSc ScRaw / $totalSe SeRaw over $($rows.Count) generators"
    } else {
        Write-Host "check-shell-raw: baseline written, $totalSc ScRaw / $totalSe SeRaw over $($rows.Count) generators"
    }
    exit 0
}

if (-not (Test-Path $Baseline)) {
    Write-Host 'FAIL: build/shell-raw-baseline.txt is missing. Write it with check-shell-raw.ps1 -Update.'
    exit 1
}

$base = Read-Baseline $Baseline

$rises = New-Object System.Collections.Generic.List[string]
$falls = New-Object System.Collections.Generic.List[string]

foreach ($k in $rows.Keys) {
    $now = $rows[$k]
    $was = if ($base.Contains($k)) { $base[$k] } else { @{ ScRaw = 0; SeRaw = 0 } }
    if ($now.ScRaw -gt $was.ScRaw -or $now.SeRaw -gt $was.SeRaw) {
        $rises.Add(('  {0}: ScRaw {1} -> {2}, SeRaw {3} -> {4}' -f $k, $was.ScRaw, $now.ScRaw, $was.SeRaw, $now.SeRaw))
    } elseif ($now.ScRaw -lt $was.ScRaw -or $now.SeRaw -lt $was.SeRaw) {
        $falls.Add(('  {0}: ScRaw {1} -> {2}, SeRaw {3} -> {4}' -f $k, $was.ScRaw, $now.ScRaw, $was.SeRaw, $now.SeRaw))
    }
}
# A generator that lost its last raw node leaves the table entirely, which is
# the best outcome the campaign has and would otherwise go unnoticed.
foreach ($k in $base.Keys) {
    if (-not $rows.Contains($k)) {
        $falls.Add(('  {0}: ScRaw {1} -> 0, SeRaw {2} -> 0 (no raw nodes left)' -f $k, $base[$k].ScRaw, $base[$k].SeRaw))
    }
}

Write-Host "check-shell-raw: $totalSc ScRaw, $totalSe SeRaw, $($rows.Count) generators"

if ($rises.Count -gt 0) {
    Write-Host 'FAIL: raw shell text was ADDED to the generated-script DSL.'
    $rises | ForEach-Object { Write-Host $_ }
    Write-Host 'ScRaw and SeRaw are deprecated (codex/foreword/shell/ShellTypes.codex).'
    Write-Host 'Use a constructor, or say in the CL description why no constructor fits and run -Update.'
    exit 1
}

if ($falls.Count -gt 0) {
    Write-Host 'FAIL: raw shell text was REMOVED and the baseline still records the old count.'
    $falls | ForEach-Object { Write-Host $_ }
    Write-Host 'Lower the record in the same changelist: build/check-shell-raw.ps1 -Update'
    exit 1
}

Write-Host 'check-shell-raw: level with the baseline'
exit 0
