# Quick smoke test for codex-vm.exe: compile a sample, boot it in codex-vm, check output.
# Usage: tools\test-codex-vm.ps1 [-SampleName absorb-outer-lambda]
[CmdletBinding()]
param(
    [string]$SampleName = 'absorb-outer-lambda'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$vmExe = Join-Path $PSScriptRoot 'codex-vm.exe'
if (-not (Test-Path -PathType Leaf $vmExe)) { Write-Host "FAIL: codex-vm.exe not found"; exit 1 }

$sampleSrc = Join-Path $root "codex.test\$SampleName.codex"
if (-not (Test-Path -PathType Leaf $sampleSrc)) {
    $sampleSrc = Join-Path $root "codex.test\apps\$SampleName.codex"
}
if (-not (Test-Path -PathType Leaf $sampleSrc)) { Write-Host "FAIL: sample $SampleName not found"; exit 1 }

$expectedFile = $sampleSrc -replace '\.codex$', '.expected'
if (-not (Test-Path -PathType Leaf $expectedFile)) { Write-Host "FAIL: no .expected file for $SampleName"; exit 1 }
$expected = (Get-Content -Raw $expectedFile).TrimEnd("`r`n")

$outDir = Join-Path $root 'build-output'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$cdx = Join-Path $outDir "vm-test-$SampleName.cdx"
$log = Join-Path $outDir "vm-test-$SampleName.log"

Write-Host "Step 1: Compile $SampleName with QEMU..."
& (Join-Path $root 'codex.build\sample-compile-selfhost.ps1') -Src $sampleSrc -Out $cdx -Log $log
if (-not (Test-Path -PathType Leaf $cdx)) { Write-Host "FAIL: compile produced no output"; exit 1 }
Write-Host "  CDX: $(( Get-Item $cdx).Length) bytes"

Write-Host "Step 2: Boot in codex-vm..."
$dataPort = 19100 + (Get-Random -Max 100) * 2
$ctrlPort = $dataPort + 1

$stderrLog = Join-Path $outDir "vm-test-$SampleName-stderr.log"
$stdoutLog = Join-Path $outDir "vm-test-$SampleName-stdout.log"
$proc = Start-Process -FilePath $vmExe -ArgumentList '-kernel', $cdx, '-data-port', "$dataPort", '-ctrl-port', "$ctrlPort" `
    -PassThru -WindowStyle Hidden -RedirectStandardError $stderrLog -RedirectStandardOutput $stdoutLog

try {
    Start-Sleep -Milliseconds 1000
    if ($proc.HasExited) {
        Write-Host "FAIL: codex-vm exited immediately (code=$($proc.ExitCode))"
        Get-Content $stderrLog | Write-Host
        exit 1
    }

    $data = [System.Net.Sockets.TcpClient]::new()
    $data.Connect('127.0.0.1', $dataPort)
    $ctrl = [System.Net.Sockets.TcpClient]::new()
    $ctrl.Connect('127.0.0.1', $ctrlPort)
    Write-Host "  Connected to ports $dataPort/$ctrlPort"

    $ctrlStream = $ctrl.GetStream()
    $ctrlStream.ReadTimeout = 15000
    $dataStream = $data.GetStream()
    $dataStream.ReadTimeout = 15000

    # Wait for READY on ctrl
    $lineBuf = [System.Collections.Generic.List[byte]]::new()
    $deadline = (Get-Date).AddSeconds(15)
    $gotReady = $false
    while ((Get-Date) -lt $deadline) {
        $b = New-Object byte[] 1
        try { $n = $ctrlStream.Read($b, 0, 1) } catch { break }
        if ($n -le 0) { break }
        if ($b[0] -eq 10) {
            $line = [System.Text.Encoding]::UTF8.GetString($lineBuf.ToArray()).TrimEnd("`r")
            if ($line.StartsWith('READY')) { $gotReady = $true; break }
            $lineBuf.Clear()
        } else { $lineBuf.Add($b[0]) }
    }
    if (-not $gotReady) { Write-Host "FAIL: no READY from codex-vm"; exit 1 }
    Write-Host "  READY received"

    # Send EOT on ctrl
    $ctrlStream.Write([byte[]]@(4), 0, 1)
    $ctrlStream.Flush()

    # Read output from data
    $outBuf = [System.Collections.Generic.List[string]]::new()
    $deadline = (Get-Date).AddSeconds(15)
    $lineBuf.Clear()
    while ((Get-Date) -lt $deadline) {
        $b = New-Object byte[] 1
        try { $n = $dataStream.Read($b, 0, 1) } catch { break }
        if ($n -le 0) { break }
        if ($b[0] -eq 10) {
            $line = [System.Text.Encoding]::UTF8.GetString($lineBuf.ToArray()).TrimEnd("`r")
            if ($line.StartsWith('HEAP:') -or $line.StartsWith('WD:')) { break }
            $outBuf.Add($line)
            $lineBuf.Clear()
        } else { $lineBuf.Add($b[0]) }
    }

    $actual = ($outBuf -join "`n").TrimEnd()
    $data.Dispose()
    $ctrl.Dispose()

    Write-Host "  Output: '$actual'"
    Write-Host "  Expected: '$expected'"

    if ($actual -eq $expected) {
        Write-Host "PASS: codex-vm output matches expected"
    } else {
        Write-Host "FAIL: output mismatch"
        exit 1
    }
} finally {
    if (-not $proc.HasExited) {
        Start-Sleep -Milliseconds 500
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    }
}
