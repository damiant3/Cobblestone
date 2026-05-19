# Test UEFI heap heuristic and explicit heap= flag.
# Requires CL 1176 (heap-pages parameter) in the seed.
# 1. Compile with -Img -Fat16 -Uefi (no -Heap) -> log has WD:UEFI-HEAP info
# 2. Compile with -Img -Fat16 -Uefi -Heap 256 -> log has WD:UEFI-HEAP-WARN
[CmdletBinding()]
param([int]$PCore = 1)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$outDir = Join-Path $root 'build-output\uefi-heap-test'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$compile = Join-Path $PSScriptRoot 'test-compile.ps1'
$src = Join-Path $root 'apps\works\UefiBoot.codex'

Write-Host "=== UEFI heap heuristic test ===" -ForegroundColor Cyan

# Test 1: heuristic (no -Heap flag)
Write-Host "Test 1: heuristic (auto)..."
$log1 = Join-Path $outDir 'heuristic.log'
$img1 = Join-Path $outDir 'heuristic.img'
& pwsh -NoProfile -File $compile -Src $src -Out $img1 -Log $log1 -PCore $PCore -Img -Fat16 -Uefi 2>&1 | Out-Null
$log1Lines = if (Test-Path $log1) { Get-Content $log1 } else { @() }
$heapLine = $log1Lines | Where-Object { $_ -match 'WD:UEFI-HEAP:' } | Select-Object -First 1
if ($heapLine) {
    Write-Host "  PASS: $heapLine" -ForegroundColor Green
} else {
    Write-Host "  FAIL: no WD:UEFI-HEAP line" -ForegroundColor Red
    exit 1
}

# Test 2: explicit small heap -> warning
Write-Host "Test 2: heap=256 (expect warning)..."
$log2 = Join-Path $outDir 'small-heap.log'
$img2 = Join-Path $outDir 'small-heap.img'
& pwsh -NoProfile -File $compile -Src $src -Out $img2 -Log $log2 -PCore $PCore -Img -Fat16 -Uefi -Heap 256 2>&1 | Out-Null
$log2Lines = if (Test-Path $log2) { Get-Content $log2 } else { @() }
$warnLine = $log2Lines | Where-Object { $_ -match 'WD:UEFI-HEAP-WARN' } | Select-Object -First 1
if ($warnLine) {
    Write-Host "  PASS: $warnLine" -ForegroundColor Green
} else {
    Write-Host "  FAIL: no WD:UEFI-HEAP-WARN line" -ForegroundColor Red
    $log2Lines | Where-Object { $_ -match 'WD:UEFI' } | ForEach-Object { Write-Host "  $_" }
    exit 1
}

# Test 3: explicit large heap -> no warning
Write-Host "Test 3: heap=65536 (no warning)..."
$log3 = Join-Path $outDir 'large-heap.log'
$img3 = Join-Path $outDir 'large-heap.img'
& pwsh -NoProfile -File $compile -Src $src -Out $img3 -Log $log3 -PCore $PCore -Img -Fat16 -Uefi -Heap 65536 2>&1 | Out-Null
$log3Lines = if (Test-Path $log3) { Get-Content $log3 } else { @() }
$warnLine3 = $log3Lines | Where-Object { $_ -match 'WD:UEFI-HEAP-WARN' } | Select-Object -First 1
$heapLine3 = $log3Lines | Where-Object { $_ -match 'WD:UEFI-HEAP:' } | Select-Object -First 1
if ($heapLine3 -and -not $warnLine3) {
    Write-Host "  PASS: $heapLine3 (no warning)" -ForegroundColor Green
} else {
    Write-Host "  FAIL: unexpected state" -ForegroundColor Red
    exit 1
}

Write-Host "All heap tests passed." -ForegroundColor Green
