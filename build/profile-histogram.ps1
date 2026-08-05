# profile-histogram.ps1 -- Resolve profiler RIP addresses and print frequency histogram
# Generated from Codex Shell DSL. Do not edit by hand.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$ProfFile,
    [string]$MapFile = '',
    [int]$Top = 40
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ((-not $MapFile)) {
    $MapFile = ([System.IO.Path]::ChangeExtension($ProfFile, '.map'))
}
if ((-not (Test-Path -PathType Leaf $MapFile))) {
    [Console]::Error.WriteLine(([string]'Symbol map not found: ' + $MapFile))
    exit 1
}
if ((-not (Test-Path -PathType Leaf $ProfFile))) {
    [Console]::Error.WriteLine(([string]'Profile file not found: ' + $ProfFile))
    exit 1
}


$map = @()
foreach ($line in ([System.IO.File]::ReadAllLines($MapFile))) {
    if (($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$')) {
        $addr = ([Convert]::ToInt64($matches[1], 16))
        $size = ([Convert]::ToInt32($matches[2]))
        $name = $matches[3].Trim()
        $map += [PSCustomObject]@{ Addr = $addr; Size = $size; Name = $name }
    }
}
$map = ($map | Sort-Object Addr)


function Resolve-Addr([long]$rip) {
    $i = ($map.Length - 1)
    while (($i -ge 0)) {
        if ((($map[$i].Addr -le $rip) -and (($map[$i].Addr + $map[$i].Size) -gt $rip))) {
            return $map[$i].Name
        }
        $i--
    }
    return '<unknown>'
}


$profLines = [System.IO.File]::ReadAllLines($ProfFile)
$totalSamples = $profLines.Length
if (($totalSamples -eq 0)) {
    Write-Host 'No samples in profile.'
    exit 0
}
$tally = @{}
foreach ($line in $profLines) {
    $hex = $line.Trim()
    if (($hex.Length -eq 0)) {
        continue
    }
    $rip = ([Convert]::ToInt64($hex, 16))
    $func = (Resolve-Addr $rip)
    if ($tally.ContainsKey($func)) {
        $tally[$func] = ($tally[$func] + 1)
    } else {
        $tally[$func] = 1
    }
}


$sorted = ($tally.GetEnumerator() | Sort-Object Value -Descending)
$barWidth = 40
Write-Host ''
Write-Host ('Profiler Histogram -- {0:N0} samples, {1:N0} unique functions' -f $totalSamples, $tally.Count)
Write-Host ''
Write-Host (([string]([string]'{0,7} {1,6}  {2,-' + $barWidth) + '} {3}') -f 'Samples', '%', '', 'Function')
Write-Host (([string]([string]'{0,7} {1,6}  {2,-' + $barWidth) + '} {3}') -f '-------', '------', ('-' * $barWidth), '--------')

$shown = 0
$otherSamples = 0
foreach ($entry in $sorted) {
    if (($shown -ge $Top)) {
        $otherSamples += $entry.Value
        continue
    }
    $pct = (($entry.Value / $totalSamples) * 100)
    $barLen = ([Math]::Max(1, ([Math]::Round((($entry.Value / $totalSamples) * $barWidth)))))
    $bar = ('#' * ([Math]::Min($barLen, $barWidth)))
    Write-Host (([string]([string]'{0,7} {1,5:F1}%  {2,-' + $barWidth) + '} {3}') -f $entry.Value, $pct, $bar, $entry.Key)
    $shown++

}

if (($otherSamples -gt 0)) {
    $pct = (($otherSamples / $totalSamples) * 100)
    Write-Host (([string]([string]'{0,7} {1,5:F1}%  {2,-' + $barWidth) + '} {3}') -f $otherSamples, $pct, '', ([string]([string]'... (' + ($tally.Count - $Top)) + ' more)'))
}
Write-Host ''
