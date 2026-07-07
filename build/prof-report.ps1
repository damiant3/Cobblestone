# prof-report.ps1 — resolve sampling-profiler output into a histogram
#
# The guest builtins prof-start/prof-dump (see OperatorsManual, Sampling
# Profiler) emit PROF:<hex-rip> lines over serial, one per timer-tick
# sample. This script resolves each RIP against a symbol map and prints
# a hot-function histogram.
#
# Usage: build/prof-report.ps1 -Log <file-with-PROF-lines> -Map <symbol.map> [-Top 25]
#   -Log: serial capture or compile log containing PROF: lines
#   -Map: text symbol map (address size name per line), e.g. seed/Codex.map
#         or the <out>.map emitted by a non-repl compile.ps1 run

param(
    [Parameter(Mandatory=$true)][string]$Log,
    [Parameter(Mandatory=$true)][string]$Map,
    [int]$Top = 25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Parse the map: sorted list of (addr, size, name)
$syms = [System.Collections.Generic.List[object]]::new()
foreach ($line in Get-Content $Map) {
    if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
        $syms.Add([pscustomobject]@{
            Addr = [Convert]::ToInt64($matches[1], 16)
            Size = [int]$matches[2]
            Name = $matches[3].Trim()
        })
    }
}
if ($syms.Count -eq 0) { Write-Host "No symbols parsed from $Map"; exit 1 }
$sorted = $syms | Sort-Object Addr
$addrs = [long[]]($sorted | ForEach-Object { $_.Addr })

function Resolve-Sample([long]$rip) {
    $idx = [Array]::BinarySearch($addrs, $rip)
    if ($idx -lt 0) { $idx = (-bnot $idx) - 1 }
    if ($idx -lt 0) { return $null }
    $s = $sorted[$idx]
    if ($rip -ge $s.Addr -and $rip -lt ($s.Addr + [Math]::Max($s.Size, 1))) { return $s.Name }
    return $null
}

$samples = @()
foreach ($line in Get-Content $Log) {
    if ($line -match 'PROF:([0-9a-fA-F]{16})') {
        $samples += [Convert]::ToInt64($matches[1], 16)
    }
}
# First PROF line is the count (small integer), samples are code addresses.
$rips = @($samples | Where-Object { $_ -ge 0x100000 })
if ($rips.Count -eq 0) { Write-Host "No samples in $Log (count line only, or profiler never ran)"; exit 1 }

$hist = @{}
$unresolved = 0
foreach ($rip in $rips) {
    $name = Resolve-Sample $rip
    if ($name) { $hist[$name] = 1 + ($hist[$name] ?? 0) }
    else { $unresolved++ }
}

Write-Host ("samples: {0}  resolved: {1}  unresolved: {2}" -f $rips.Count, ($rips.Count - $unresolved), $unresolved)
Write-Host ""
$total = [double]$rips.Count
$hist.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First $Top | ForEach-Object {
    Write-Host ("{0,6:F1}%  {1,5}  {2}" -f (100.0 * $_.Value / $total), $_.Value, $_.Key)
}
