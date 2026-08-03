# sim-test.ps1 -- Parameterized simulation test driver for CodexMagic
# Runs single games with different configurations, logs results to CSV.
#
# Usage: tools/sim-test.ps1 [-Games 10] [-OutFile sim-results.csv]
[CmdletBinding()]
param(
    [int]$Games = 10,
    [string]$OutFile = 'build-output/sim-results.csv',
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
. (Join-Path 'build' 'vm-config.ps1')

$Kernel = 'build-output/codexmagic-baseline.cdx'
if (-not (Test-Path $Kernel)) {
    Write-Error "Compile SimBaseline first: pwsh build/compile.ps1 -Src apps/games/codexmagic/SimBaseline.codex -Out $Kernel -Log build-output/sim.log"
    exit 1
}

# CSV header
$header = "game_id,seed,p0_wins,p1_wins,draws,fp_wins,total_turns,screw,flood,config"
if (-not (Test-Path $OutFile)) {
    Set-Content -Path $OutFile -Value $header -Encoding UTF8
} else {
    Write-Host "Appending to existing $OutFile"
}

$existing = @(Get-Content $OutFile | Where-Object { $_ -ne $header }).Count
$startId = $existing + 1

Write-Host ""
Write-Host "  CodexMagic Simulation Test Driver" -ForegroundColor Cyan
Write-Host "  Games: $Games | Output: $OutFile | Starting ID: $startId"
Write-Host ""

for ($i = 0; $i -lt $Games; $i++) {
    $gameId = $startId + $i
    $seed = 42 + $i * 97
    Write-Host "  Game $gameId (seed $seed)..." -NoNewline

    $run = Start-VmRun -Kernel $Kernel -ConnectTimeoutSec 15 -MemMB 2048 -PCore $PCore
    if (-not $run) {
        Write-Host " VM FAILED" -ForegroundColor Red
        continue
    }

    $stream = $run.Conn.Data.GetStream()
    $lines = @()
    for ($j = 0; $j -lt 20; $j++) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 120
        if (-not $line) { break }
        $lines += $line
    }
    try { Close-Vm -Conn $run.Conn -Process $run.Process } catch {}

    # Parse output
    $result = $lines | Where-Object { $_ -match '^P0:' }
    if ($result) {
        $parsed = $false
        if ($result -match 'P0:\s*(\d+)\s*P1:\s*(\d+)\s*Draw:\s*(\d+)') {
            $p0 = $matches[1]; $p1 = $matches[2]; $dr = $matches[3]
            $parsed = $true
        }
        $fpLine = $lines | Where-Object { $_ -match 'FP%:' }
        $fp = '?'; $turns = '?'; $screw = '?'; $flood = '?'
        if ($fpLine -match 'FP%:\s*(\d+)\s*AvgT:\s*(\d+)') {
            $fp = $matches[1]; $turns = $matches[2]
        }
        $screwLine = $lines | Where-Object { $_ -match 'Screw:' }
        if ($screwLine -match 'Screw:\s*(\d+)\s*Flood:\s*(\d+)') {
            $screw = $matches[1]; $flood = $matches[2]
        }
        if ($parsed) {
            $row = "$gameId,$seed,$p0,$p1,$dr,$fp,$turns,$screw,$flood,mixed-mirror"
            Add-Content -Path $OutFile -Value $row -Encoding UTF8
            $winner = if ([int]$p0 -gt [int]$p1) { "P0 wins" } elseif ([int]$p1 -gt [int]$p0) { "P1 wins" } else { "Draw" }
            Write-Host " $winner (T$turns)" -ForegroundColor Green
        } else {
            Write-Host " PARSE FAIL: $result" -ForegroundColor Yellow
        }
    } else {
        Write-Host " NO OUTPUT" -ForegroundColor Red
        $raw = $lines -join ' | '
        Write-Host "    Raw: $raw" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "  Results written to $OutFile" -ForegroundColor Green

# Summary
$data = Import-Csv $OutFile
$total = $data.Count
if ($total -gt 0) {
    $p0Total = ($data | Measure-Object -Property p0_wins -Sum).Sum
    $p1Total = ($data | Measure-Object -Property p1_wins -Sum).Sum
    $drTotal = ($data | Measure-Object -Property draws -Sum).Sum
    $gamesPlayed = [int]$p0Total + [int]$p1Total + [int]$drTotal
    Write-Host ""
    Write-Host "  === Cumulative Results ($total runs, $gamesPlayed games) ===" -ForegroundColor Cyan
    Write-Host "  P0 wins: $p0Total  P1 wins: $p1Total  Draws: $drTotal"
    if ($gamesPlayed -gt 0) {
        $fpPct = [math]::Round([int]$p0Total * 100 / $gamesPlayed)
        Write-Host "  First-player win rate: ${fpPct}%"
    }
}
