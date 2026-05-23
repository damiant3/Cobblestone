# Test UEFI heap heuristic and explicit heap= flag.
# This test uses the compiler's internal IMG mode directly (not via
# compile.ps1) because it tests compiler diagnostics that are specific
# to the IMG code path. When the compiler's IMG mode is eventually
# removed, this test will move to the IMG plug.
[CmdletBinding()]
param([int]$PCore = 1)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$root = Split-Path $PSScriptRoot
$outDir = Join-Path $root 'build-output\uefi-heap-test'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$compile = Join-Path $PSScriptRoot 'compile.ps1'
$src = Join-Path $root 'apps\works\UefiBoot.codex'

function Invoke-InternalImgCompile {
    param([string]$Src, [string]$Out, [string]$Log, [string]$Mode)
    $Stage0 = Join-Path $root 'build-output\bare-metal\Codex.cdx'
    if (-not (Test-Path $Stage0)) { Write-Host "FAIL: no $Stage0"; exit 1 }
    $run = Start-VmRun -Kernel $Stage0 -ConnectTimeoutSec 30 -MemMB 2048 -PCore $PCore
    if (-not $run) { Write-Host "FAIL: VM did not start"; exit 1 }
    try {
        if (-not (Read-VmReady -Conn $run.Conn -TimeoutSec 30)) { Write-Host "FAIL: no READY"; exit 1 }
        $stream = $run.Conn.Data.GetStream()
        $hdr = [System.Text.Encoding]::UTF8.GetBytes("$Mode`n")
        $stream.Write($hdr, 0, $hdr.Length)
        $srcBytes = [System.IO.File]::ReadAllBytes($Src)
        $stream.Write($srcBytes, 0, $srcBytes.Length)
        $stream.WriteByte(4); $stream.Flush()
        Set-Content -Path $Log -Value '' -Encoding UTF8
        while ($true) {
            $line = Read-StreamLine -Stream $stream -TimeoutSec 120
            if ($null -eq $line) { break }
            Add-Content -Path $Log -Value $line -Encoding UTF8
            if ($line.StartsWith('SIZE:') -or $line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('HEAP:')) { break }
        }
    } finally {
        Close-Vm -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
}

Write-Host "=== UEFI heap heuristic test ===" -ForegroundColor Cyan

# Test 1: heuristic (no heap flag)
Write-Host "Test 1: heuristic (auto)..."
$log1 = Join-Path $outDir 'heuristic.log'
Invoke-InternalImgCompile -Src $src -Out (Join-Path $outDir 'h.img') -Log $log1 -Mode "IMG fat16 uefi"
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
Invoke-InternalImgCompile -Src $src -Out (Join-Path $outDir 's.img') -Log $log2 -Mode "IMG fat16 uefi heap=256"
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
Invoke-InternalImgCompile -Src $src -Out (Join-Path $outDir 'l.img') -Log $log3 -Mode "IMG fat16 uefi heap=65536"
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
