# test-disk-compile.ps1 -- Test DISK compile mode end-to-end
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [string]$SampleSrc = 'codex\test\field-range-proven.codex',
    [string]$Expected = '12',
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$root = Split-Path $PSScriptRoot
$outDir = Join-Path $root 'build-output\disk-compile-test'
if ((-not (Test-Path -PathType Container $outDir))) {
    New-Item -ItemType Directory -Force $outDir | Out-Null
}
$compile = Join-Path $PSScriptRoot 'compile.ps1'
$runScript = Join-Path $PSScriptRoot 'test-run.ps1'
Write-Host '=== DISK compile test ===' -ForegroundColor Cyan


Write-Host 'Step 1a: Compile source to CDX...'
$imgOut = Join-Path $outDir 'disk-test.img'
$cdxOut = Join-Path $outDir 'disk-test.cdx'
$cdxLog = Join-Path $outDir 'cdx-build.log'
& pwsh -NoProfile -File $compile -Src $SampleSrc -Out $cdxOut -Log $cdxLog -PCore $PCore
if (($LASTEXITCODE -ne 0 -or (-not (Test-Path -PathType Leaf $cdxOut)))) {
    Write-Host 'FAIL: CDX compile failed' -ForegroundColor Red
    if ((Test-Path -PathType Leaf $cdxLog)) {
        Get-Content $cdxLog | Write-Host
    }
    exit 1
}


Write-Host 'Step 1b: CDX -> PE via plug...'
$peOut = Join-Path $outDir 'disk-test.efi'
$pePlug = Join-Path $root 'codex\plugs\pe\run.ps1'
& pwsh -NoProfile -File $pePlug -CdxInput $cdxOut -Out $peOut
if (($LASTEXITCODE -ne 0 -or (-not (Test-Path -PathType Leaf $peOut)))) {
    Write-Host 'FAIL: PE plug failed'
    exit 1
}


Write-Host 'Step 1c: PE + CDX -> FAT16 IMG via plug...'
$imgPlug = Join-Path $root 'codex\plugs\img\run.ps1'
& pwsh -NoProfile -File $imgPlug -PeInput $peOut -CdxInput $cdxOut -Out $imgOut -Fat16 -Source $SampleSrc
if (($LASTEXITCODE -ne 0 -or (-not (Test-Path -PathType Leaf $imgOut)))) {
    Write-Host 'FAIL: IMG plug failed'
    exit 1
}
Write-Host "  IMG: $((Get-Item $imgOut).Length) bytes"


Write-Host 'Step 2: DISK compile...'
$Stage0 = Join-Path $root 'build-output\bare-metal\Codex.cdx'
if ((-not (Test-Path -PathType Leaf $Stage0))) {
    Write-Host "FAIL: no compiler CDX at $Stage0"
    exit 1
}
$diskArgs = @('-drive', "file=$imgOut,format=raw,if=ide,index=0")
$run = Start-VmRun -Kernel $Stage0 -ConnectTimeoutSec 10 -MemMB 3072 -PCore $PCore -ExtraArgs $diskArgs
if ((-not $run)) {
    Write-Host 'FAIL: VM did not start'
    exit 1
}


try {
    if ((-not (Read-VmReady -Conn $run.Conn -TimeoutSec 30))) {
        Write-Host 'FAIL: no READY'
        exit 1
    }


    $stream = $run.Conn.Data.GetStream()
    $hdr = [System.Text.Encoding]::UTF8.GetBytes("DISK`n")
    $stream.Write($hdr, 0, $hdr.Length)
    $path = [System.Text.Encoding]::UTF8.GetBytes("SOURCE.SRC`n")
    $stream.Write($path, 0, $path.Length)
    $stream.Flush()
    $binSize = 0
    $status = ''
    $logLines = @()


    :read_loop while ($true) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 120
        if ($null -eq $line) {
            break
        }
        $logLines += $line
        if ($line.StartsWith('SIZE:')) {
            if (($line -match '^\D*(\d+)')) {
                $binSize = [int]$matches[1]
            }
            $status = 'size'
            break
        }
        if (($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS'))) {
            $status = 'error'
            break
        }
    }


    if ((($status -ne 'size') -or $binSize -le 0)) {
        Write-Host "FAIL: DISK compile failed (status=$status)" -ForegroundColor Red
        foreach ($ll in $logLines) {
            Write-Host "  $ll"
        }
        exit 1
    }
    Write-Host "  Compiled: $binSize bytes"
    $binBytes = Read-StreamBytes -Stream $stream -Count $binSize -TimeoutSec 60
    if (((-not $binBytes) -or $binBytes.Length -ne $binSize)) {
        Write-Host 'FAIL: binary read incomplete'
        exit 1
    }
    $cdxOut = Join-Path $outDir 'disk-compiled.cdx'
    [System.IO.File]::WriteAllBytes($cdxOut, $binBytes)
    Write-Host "  Written: $cdxOut"

} finally {
    Close-Vm -Conn $run.Conn -Process $run.Process
}

Write-Host 'Step 3: Run compiled CDX...'
$actual = Join-Path $outDir 'runtime.actual'
& pwsh -NoProfile -File $runScript -Kernel $cdxOut -OutFile $actual -PCore $PCore
if ($LASTEXITCODE -ne 0) {
    Write-Host 'FAIL: runtime failed'
    exit 1
}
$output = if (Test-Path $actual) { (Get-Content -Raw $actual).TrimEnd() } else { '' }
Write-Host "  Output: '$output'"
Write-Host "  Expected: '$Expected'"
if (($output -eq $Expected)) {
    Write-Host 'PASS: DISK compile mode works' -ForegroundColor Green
} else {
    Write-Host 'FAIL: output mismatch' -ForegroundColor Red
    exit 1
}
