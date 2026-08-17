# Compiles one test to a native backend and boots it under QEMU with a
# virtio block device attached, so a test that reads a disk can run on the
# cross lanes. A test opts into it with a .disk sidecar, which IS the raw
# image (codex/test/fs-layer.disk is 16 MB of FAT16), not a path to one.
#
# Only QEMU is used: the committed Renode board .repl files carry no block
# device at all, which is why build/test-cross-batch.ps1 skips a .disk test
# rather than failing it. This is the routed runner, the way .smp routes to
# build/test-cross-smp.ps1.
#
# TRANSPORT: -device virtio-blk-device is the MMIO transport on -M virt
# (virtio-blk-pci would be the PCI one). codex/foreword/core/VirtioBlk.codex
# probes the MMIO slots -- 0x0a000000 stride 0x200 on arm64, 0x10001000
# stride 0x1000 on riscv64 -- so the -device suffix here and the driver's
# transport must stay the same choice.
#
# Usage:
#   build/test-cross-disk.ps1 -Arch arm64 -Test fs-layer
#
# Exit status: 0 on pass, 1 on failure.
[CmdletBinding()]
param(
    [ValidateSet('arm64','riscv64')]
    [string]$Arch = 'arm64',

    [Parameter(Mandatory=$true)]
    [string]$Test,

    [int]$TimeoutSec = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path
$Repo = (Get-Location).Path

$qemuExe = if ($Arch -eq 'riscv64') {
    'D:\Program Files\qemu\qemu-system-riscv64.exe'
} else {
    'D:\Program Files\qemu\qemu-system-aarch64.exe'
}
if (-not (Test-Path $qemuExe)) { Write-Host "SKIP: QEMU not found ($qemuExe)" -ForegroundColor Yellow; exit 0 }

$plugName = if ($Arch -eq 'arm64') { 'arm64' } else { 'riscv' }
$plugCdx = Join-Path $Repo "codex\plugs\$plugName\build-output\$plugName-plug.cdx"
if (-not (Test-Path $plugCdx)) {
    Write-Host "SKIP: plug not built ($plugCdx). Build: codex\plugs\$plugName\build.ps1" -ForegroundColor Yellow
    exit 0
}

$seedCdx = Join-Path $Repo 'seed\Codex.cdx'
$stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
New-Item -ItemType Directory -Force (Split-Path $stage0) | Out-Null
if (-not (Test-Path $stage0)) { Copy-Item -Force $seedCdx $stage0 }

$testFile = Get-Item -Path "codex\test\$Test.codex" -ErrorAction SilentlyContinue
if (-not $testFile) { Write-Host "ERROR: test not found: codex\test\$Test.codex" -ForegroundColor Red; exit 1 }
$name = $testFile.BaseName
$dir = $testFile.DirectoryName

$diskFile = Join-Path $dir "$name.disk"
if (-not (Test-Path -PathType Leaf $diskFile)) {
    Write-Host "ERROR: $name has no .disk sidecar; this runner is for block-device tests" -ForegroundColor Red
    exit 1
}

Write-Host "=== $($Arch.ToUpper()) DISK : $name ===" -ForegroundColor Cyan

# --- Compile ---
$testOutDir = Join-Path $Repo "test-output-cross\$Arch\$name"
New-Item -ItemType Directory -Force $testOutDir | Out-Null
$elfOut = Join-Path $testOutDir "$name.elf"
$compileScript = Join-Path $Repo "codex\plugs\$plugName\compile-$plugName.ps1"
$compileLog = Join-Path $testOutDir 'compile.log'
Write-Host -NoNewline "  compile ... "
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
# No -Smp here: this is a single-core boot, and asking the ARM64 plug for the
# CPU_ON sequence would issue HVC on a board without PSCI and park the guest.
& pwsh -NoProfile -File $compileScript -Src $testFile.FullName -Out $elfOut 2>&1 | Out-File -FilePath $compileLog -Encoding UTF8
$compileExit = $LASTEXITCODE
$ErrorActionPreference = $prev
if ($compileExit -ne 0) { Write-Host "FAIL (compile, exit=$compileExit)" -ForegroundColor Red; exit 1 }

# An unresolved call is not a compile failure today (the plug warns and exits
# 0, CrossLaneFilesystem.md step 0), so a block builtin the program never
# resolved would boot and read a stale register instead of reading the disk.
# Say so here rather than letting it read as a driver fault.
$unresolved = @(Select-String -Path $compileLog -Pattern 'unresolved call' -ErrorAction SilentlyContinue)
if ($unresolved.Count -gt 0) {
    Write-Host "WARN (unresolved calls, the disk path cannot work):" -ForegroundColor Yellow
    foreach ($u in $unresolved) { Write-Host "    $($u.Line.Trim())" -ForegroundColor Yellow }
}

$binFile = [System.IO.Path]::ChangeExtension($elfOut, '.bin')
if ($Arch -eq 'riscv64' -and -not (Test-Path $binFile)) { Write-Host "FAIL (no .bin produced)" -ForegroundColor Red; exit 1 }
Write-Host "OK"

$expectedFile = Join-Path $dir "$name.expected"
if (-not (Test-Path -PathType Leaf $expectedFile)) { Write-Host "  PASS (compile only)" -ForegroundColor DarkGreen; exit 0 }

# --- Run under QEMU with the image attached ---
Write-Host -NoNewline "  run ... "
$uartLog = Join-Path $testOutDir 'uart.disk.log'
if (Test-Path $uartLog) { Remove-Item $uartLog -Force }

# The image is opened snapshot=on so a test that writes cannot mutate the
# committed sidecar: codex/test/fs-layer writes NOTE.TXT before reading it
# back, and a depot file that changes when a test runs is not a fixture.
# -m 1024M matches test-cross-smp: both backends put their boot stack about
# 1 GB above the RAM base.
# force-legacy=false is REQUIRED, not tidiness. Measured 2026-08-16: without
# it QEMU's virt presents virtio-mmio VERSION 1 (the legacy transport, which
# configures a queue through QueuePFN and a guest page size), and
# codex/foreword/core/VirtioBlk.codex implements and probes for version 2
# only, refusing version 1 rather than half-driving it. With the flag every
# slot reports version 2 and the block device appears.
#
# The device lands in the LAST slot: QEMU fills virtio-mmio from the top, so
# on -M virt with 32 slots at 0x0a000000 it is slot 31, not slot 0. The
# driver scans rather than assuming, which is why that costs nothing here,
# but a reader who probes only the first slot will find an empty one and
# conclude there is no disk.
$driveArgs = @('-global','virtio-mmio.force-legacy=false',
               '-drive',"file=$diskFile,format=raw,if=none,id=hd0,snapshot=on",
               '-device','virtio-blk-device,drive=hd0')
$machArgs = if ($Arch -eq 'riscv64') {
    @('-M','virt','-m','1024M','-display','none','-monitor','none','-bios','none',
      '-device',"loader,file=$binFile,addr=0x80000000",'-serial',"file:$uartLog") + $driveArgs
} else {
    @('-M','virt','-cpu','cortex-a53','-m','1024M','-display','none','-monitor','none',
      '-kernel',$elfOut,'-serial',"file:$uartLog") + $driveArgs
}
$proc = Start-Process -FilePath $qemuExe -ArgumentList $machArgs -PassThru -NoNewWindow
$proc.WaitForExit($TimeoutSec * 1000) | Out-Null
if (-not $proc.HasExited) { try { $proc.Kill() } catch {} ; $proc.WaitForExit(3000) | Out-Null }
Start-Sleep -Milliseconds 200

if (-not (Test-Path $uartLog)) { Write-Host "FAIL (no uart output)" -ForegroundColor Red; exit 1 }

# --- Compare (same normalization as test-cross.ps1) ---
$raw = [System.IO.File]::ReadAllText($uartLog) -replace "`r",''
$allLines = $raw -split "`n"
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($l in $allLines) {
    if ($l.StartsWith('HEAP:') -or $l.StartsWith('WD:') -or $l.StartsWith('STACK:')) { continue }
    $lines.Add($l)
}
while ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }

$expectedText = [System.IO.File]::ReadAllText($expectedFile) -replace "`r",''
$expAllLines = @($expectedText -split "`n")
while ($expAllLines.Count -gt 0 -and $expAllLines[$expAllLines.Count - 1] -eq '') {
    $expAllLines = $expAllLines[0..($expAllLines.Count - 2)]
}
$expLineCount = $expAllLines.Count
if ($lines.Count -gt $expLineCount -and $expLineCount -gt 0) {
    $lines = [System.Collections.Generic.List[string]]::new([string[]]@($lines | Select-Object -First $expLineCount))
}
$actual = if ($lines.Count -gt 0) { ($lines -join "`n") + "`n" } else { '' }
[System.IO.File]::WriteAllText((Join-Path $testOutDir 'runtime.disk.actual'), $actual, [System.Text.UTF8Encoding]::new($false))

if ($expectedText -eq $actual) {
    Write-Host "PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host "FAIL (output mismatch)" -ForegroundColor Red
    Write-Host "  expected: [$($expectedText.Trim())]"
    Write-Host "  actual:   [$($actual.Trim())]"
    exit 1
}
