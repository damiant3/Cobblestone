param(
    [Parameter(Mandatory=$true)] [string]$TraceFile,
    [string]$MapFile = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $MapFile) {
    $MapFile = [System.IO.Path]::ChangeExtension($TraceFile, '.map')
}
if (-not (Test-Path $MapFile)) {
    Write-Error "Symbol map not found: $MapFile"
    exit 1
}
if (-not (Test-Path $TraceFile)) {
    Write-Error "Trace file not found: $TraceFile"
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
            $off = $rip - $map[$i].Addr
            return "$($map[$i].Name)+0x$($off.ToString('X'))"
        }
    }
    return "0x$($rip.ToString('X'))"
}

# Parse entries -- supports both text (T:hex:hex per line) and binary (8-byte count + 16-byte entries) formats.
$entries = [System.Collections.Generic.List[long[]]]::new()
$firstLine = (Get-Content $TraceFile -First 1).Trim()

if ($firstLine.StartsWith('T:')) {
    foreach ($line in [System.IO.File]::ReadAllLines($TraceFile)) {
        if ($line -match '^T:([0-9a-fA-F]+):([0-9a-fA-F]+)$') {
            $entries.Add(@([Convert]::ToInt64($matches[1], 16), [Convert]::ToInt64($matches[2], 16)))
        }
    }
} else {
    $bytes = [System.IO.File]::ReadAllBytes($TraceFile)
    if ($bytes.Length -lt 8) { Write-Error "Trace file too small"; exit 1 }
    $count = [BitConverter]::ToInt64($bytes, 0)
    if ($bytes.Length -lt 8 + $count * 16) {
        Write-Warning "Trace file truncated"
        $count = [Math]::Floor(($bytes.Length - 8) / 16)
    }
    for ($i = 0; $i -lt $count; $i++) {
        $off = 8 + $i * 16
        $entries.Add(@([BitConverter]::ToInt64($bytes, $off), [BitConverter]::ToInt64($bytes, $off + 8)))
    }
}

$phaseNames = @{ 1='lex'; 2='parse'; 3='desugar'; 4='scope'; 5='check'; 6='lower'; 7='emit' }
$phase = 'init'
$summary = @{}
$phaseTotal = @{}
$totalAllocs = 0
$totalBytes = [long]0

foreach ($e in $entries) {
    $size = $e[0]
    $rip = $e[1]

    if ($size -eq 0 -and $phaseNames.ContainsKey([int]$rip)) {
        $phase = $phaseNames[[int]$rip]
        continue
    }

    $func = Resolve-Addr $rip
    $key = "$phase|$func"
    if (-not $summary.ContainsKey($key)) {
        $summary[$key] = [PSCustomObject]@{ Phase=$phase; Func=$func; Count=0; Bytes=[long]0 }
    }
    $summary[$key].Count++
    $summary[$key].Bytes += $size
    if (-not $phaseTotal.ContainsKey($phase)) {
        $phaseTotal[$phase] = [PSCustomObject]@{ Count=0; Bytes=[long]0 }
    }
    $phaseTotal[$phase].Count++
    $phaseTotal[$phase].Bytes += $size
    $totalAllocs++
    $totalBytes += $size
}

Write-Host ""
Write-Host ("{0,-12} {1,-40} {2,8} {3,12}" -f 'Phase', 'Function', 'Count', 'Total Bytes')
Write-Host ("{0,-12} {1,-40} {2,8} {3,12}" -f '-----', '--------', '-----', '-----------')

$sorted = $summary.Values | Sort-Object { $_.Phase }, { -$_.Bytes }
foreach ($entry in $sorted) {
    Write-Host ("{0,-12} {1,-40} {2,8} {3,12}" -f $entry.Phase, $entry.Func, $entry.Count, $entry.Bytes.ToString('N0'))
}

Write-Host ""
Write-Host "--- Phase Totals ---"
foreach ($p in ($phaseTotal.Keys | Sort-Object)) {
    $pt = $phaseTotal[$p]
    Write-Host ("{0,-12} {1,8} allocs  {2,12} bytes ({3:F1} MB)" -f $p, $pt.Count, $pt.Bytes.ToString('N0'), ($pt.Bytes / 1048576.0))
}
Write-Host ""
Write-Host ("TOTAL: {0:N0} allocations, {1:N0} bytes ({2:F1} MB)" -f $totalAllocs, $totalBytes, ($totalBytes / 1048576.0))
