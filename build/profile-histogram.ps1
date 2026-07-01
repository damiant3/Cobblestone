# Read a .prof file (hex RIP addresses, one per line) and a .map file
# (symbol map), resolve each RIP to a function name, and print a
# frequency histogram sorted by sample count.
#
# Usage: profile-histogram.ps1 -ProfFile out.prof [-MapFile out.map]
#        profile-histogram.ps1 -ProfFile out.prof -Top 20
param(
    [Parameter(Mandatory=$true)] [string]$ProfFile,
    [string]$MapFile = '',
    [int]$Top = 40
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $MapFile) {
    $MapFile = [System.IO.Path]::ChangeExtension($ProfFile, '.map')
}
if (-not (Test-Path $MapFile)) {
    Write-Error "Symbol map not found: $MapFile"
    exit 1
}
if (-not (Test-Path $ProfFile)) {
    Write-Error "Profile file not found: $ProfFile"
    exit 1
}

$map = @()
foreach ($line in [System.IO.File]::ReadAllLines($MapFile)) {
    if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
        $addr = [Convert]::ToInt64($matches[1], 16)
        $size = [int]$matches[2]
        $name = $matches[3].Trim()
        $map += [PSCustomObject]@{ Addr = $addr; Size = $size; Name = $name }
    }
}
$map = $map | Sort-Object Addr

function Resolve-Addr([long]$rip) {
    for ($i = $map.Length - 1; $i -ge 0; $i--) {
        if ($map[$i].Addr -le $rip -and ($map[$i].Addr + $map[$i].Size) -gt $rip) {
            return $map[$i].Name
        }
    }
    return "<unknown>"
}

$profLines = [System.IO.File]::ReadAllLines($ProfFile)
$totalSamples = $profLines.Length
if ($totalSamples -eq 0) {
    Write-Host "No samples in profile."
    exit 0
}

$tally = @{}
foreach ($line in $profLines) {
    $hex = $line.Trim()
    if ($hex.Length -eq 0) { continue }
    $rip = [Convert]::ToInt64($hex, 16)
    $func = Resolve-Addr $rip
    if ($tally.ContainsKey($func)) { $tally[$func]++ }
    else { $tally[$func] = 1 }
}

$sorted = $tally.GetEnumerator() | Sort-Object { -$_.Value }
$barWidth = 40

Write-Host ""
Write-Host ("Profiler Histogram — {0:N0} samples, {1:N0} unique functions" -f $totalSamples, $tally.Count)
Write-Host ""
Write-Host ("{0,7} {1,6}  {2,-$barWidth} {3}" -f 'Samples', '%', '', 'Function')
Write-Host ("{0,7} {1,6}  {2,-$barWidth} {3}" -f '-------', '------', ('-' * $barWidth), ('--------'))

$shown = 0
$otherSamples = 0
foreach ($entry in $sorted) {
    if ($shown -ge $Top) {
        $otherSamples += $entry.Value
        continue
    }
    $pct = ($entry.Value / $totalSamples) * 100
    $barLen = [Math]::Max(1, [Math]::Round(($entry.Value / $totalSamples) * $barWidth))
    $bar = '#' * [Math]::Min($barLen, $barWidth)
    Write-Host ("{0,7} {1,5:F1}%  {2,-$barWidth} {3}" -f $entry.Value, $pct, $bar, $entry.Key)
    $shown++
}

if ($otherSamples -gt 0) {
    $pct = ($otherSamples / $totalSamples) * 100
    Write-Host ("{0,7} {1,5:F1}%  {2,-$barWidth} {3}" -f $otherSamples, $pct, '', "... ($($tally.Count - $Top) more)")
}

Write-Host ""
