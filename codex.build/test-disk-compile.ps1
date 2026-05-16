# Test DISK compile mode end-to-end:
# 1. Compile a sample into a FAT16 IMG (source embedded as SOURCE.SRC)
# 2. Boot the compiler with that IMG as IDE disk
# 3. Send "DISK\nSOURCE.SRC\n" to compile from disk
# 4. Boot the resulting CDX, check output matches expected
[CmdletBinding()]
param(
    [string]$SampleSrc = 'codex.test\absorb-outer-lambda.codex',
    [string]$Expected = '12',
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'qemu-config.ps1')

$root = Split-Path $PSScriptRoot
$outDir = Join-Path $root 'build-output\disk-compile-test'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$compile = Join-Path $PSScriptRoot 'test-compile.ps1'
$runScript = Join-Path $PSScriptRoot 'test-run.ps1'

Write-Host "=== DISK compile test ===" -ForegroundColor Cyan

# Step 1: Build FAT16 IMG with source embedded
Write-Host "Step 1: Build FAT16 IMG..."
$imgOut = Join-Path $outDir 'disk-test.img'
$imgLog = Join-Path $outDir 'img-build.log'
& pwsh -NoProfile -File $compile -Src $SampleSrc -Out $imgOut -Log $imgLog -PCore $PCore -Img -Fat16
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $imgOut)) {
    Write-Host "FAIL: IMG build failed" -ForegroundColor Red
    if (Test-Path $imgLog) { Get-Content $imgLog | Write-Host }
    exit 1
}
Write-Host "  IMG: $((Get-Item $imgOut).Length) bytes"

# Step 2: Boot compiler with IMG as IDE disk, send DISK mode
Write-Host "Step 2: DISK compile..."
$Stage0 = Join-Path $root 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path $Stage0)) { Write-Host "FAIL: no compiler CDX at $Stage0"; exit 1 }

$diskArgs = @('-drive', "file=$imgOut,format=raw,if=ide,index=0")
$run = Start-QemuRun -Kernel $Stage0 -ConnectTimeoutSec 10 -MemMB 2048 -PCore $PCore -ExtraArgs $diskArgs
if (-not $run) { Write-Host "FAIL: QEMU did not start"; exit 1 }

try {
    if (-not (Read-QemuReady -Conn $run.Conn -TimeoutSec 30)) {
        Write-Host "FAIL: no READY"; exit 1
    }
    $stream = $run.Conn.Data.GetStream()

    # Send "DISK\n" then "SOURCE.SRC\n"
    $hdr = [System.Text.Encoding]::UTF8.GetBytes("DISK`n")
    $stream.Write($hdr, 0, $hdr.Length)
    $path = [System.Text.Encoding]::UTF8.GetBytes("SOURCE.SRC`n")
    $stream.Write($path, 0, $path.Length)
    $stream.Flush()

    # Read response: text lines until SIZE: or CODEGEN-HALTED
    $binSize = 0
    $status = ''
    $logLines = @()
    while ($true) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 120
        if ($null -eq $line) { break }
        $logLines += $line
        if ($line.StartsWith('SIZE:')) {
            if ($line -match '^\D*(\d+)') { $binSize = [int]$matches[1] }
            $status = 'size'
            break
        }
        if ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            $status = 'error'
            break
        }
    }

    if ($status -ne 'size' -or $binSize -le 0) {
        Write-Host "FAIL: DISK compile failed (status=$status)" -ForegroundColor Red
        $logLines | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    Write-Host "  Compiled: $binSize bytes"

    $binBytes = Read-StreamBytes -Stream $stream -Count $binSize -TimeoutSec 60
    if (-not $binBytes -or $binBytes.Length -ne $binSize) {
        Write-Host "FAIL: binary read incomplete"; exit 1
    }
    $cdxOut = Join-Path $outDir 'disk-compiled.cdx'
    [System.IO.File]::WriteAllBytes($cdxOut, $binBytes)
    Write-Host "  Written: $cdxOut"
} finally {
    Close-Qemu -Conn $run.Conn -Process $run.Process
}

# Step 3: Boot the compiled CDX, check output
Write-Host "Step 3: Run compiled CDX..."
$actual = Join-Path $outDir 'runtime.actual'
& pwsh -NoProfile -File $runScript -Kernel $cdxOut -OutFile $actual -PCore $PCore
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: runtime failed"; exit 1 }

$output = if (Test-Path $actual) { (Get-Content -Raw $actual).TrimEnd() } else { '' }
Write-Host "  Output: '$output'"
Write-Host "  Expected: '$Expected'"

if ($output -eq $Expected) {
    Write-Host "PASS: DISK compile mode works" -ForegroundColor Green
} else {
    Write-Host "FAIL: output mismatch" -ForegroundColor Red
    exit 1
}
