# Multi-core cross-architecture test runner.
#
# Compiles one test to a native backend and boots it under QEMU with
# multiple cores, comparing UART output against the .expected sidecar.
# This is the multi-core counterpart to build/test-cross.ps1 (which is
# single-core on Renode). A test opts into it with a .smp sidecar whose
# first line is the core count; the single-core batteries skip such
# tests, so a multi-core test is only ever run here.
#
# Only QEMU is used: the committed Renode board .repl files are
# single-core, and QEMU's virt machine brings every hart/core up at the
# kernel entry with no firmware (-bios none), which is exactly the
# park-the-secondaries boot model the plug's __start implements.
#
# Usage:
#   build/test-cross-smp.ps1 -Arch riscv64 -Test smp-riscv-boot
#   build/test-cross-smp.ps1 -Arch riscv64 -Test smp-riscv-boot -Cores 4
#
# Exit status: 0 on pass, 1 on failure.
[CmdletBinding()]
param(
    [ValidateSet('arm64','riscv64')]
    [string]$Arch = 'riscv64',

    [Parameter(Mandatory=$true)]
    [string]$Test,

    [int]$Cores = 0,
    [int]$TimeoutSec = 3
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

# Core count: -Cores override, else the .smp sidecar, else 2.
if ($Cores -le 0) {
    $smpFile = Join-Path $dir "$name.smp"
    if (Test-Path -PathType Leaf $smpFile) { $Cores = [int](Get-Content -TotalCount 1 $smpFile) }
    if ($Cores -le 0) { $Cores = 2 }
}

Write-Host "=== $($Arch.ToUpper()) SMP (-smp $Cores) : $name ===" -ForegroundColor Cyan

# --- Compile ---
$testOutDir = Join-Path $Repo "test-output-cross\$Arch\$name"
New-Item -ItemType Directory -Force $testOutDir | Out-Null
$elfOut = Join-Path $testOutDir "$name.elf"
$compileScript = Join-Path $Repo "codex\plugs\$plugName\compile-$plugName.ps1"
$compileLog = Join-Path $testOutDir 'compile.log'
Write-Host -NoNewline "  compile ... "
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
# ARM64 secondaries are held in PSCI, so this is the one path that asks the
# plug for the CPU_ON sequence. Every other ARM64 compile must NOT get it:
# HVC is undefined on boards without PSCI and parks the guest.
# RISC-V harts auto-enter __start, so its plug needs no such flag.
$compileArgs = @('-NoProfile','-File',$compileScript,'-Src',$testFile.FullName,'-Out',$elfOut)
if ($Arch -eq 'arm64') { $compileArgs += '-Smp' }
& pwsh @compileArgs 2>&1 | Out-File -FilePath $compileLog -Encoding UTF8
$compileExit = $LASTEXITCODE
$ErrorActionPreference = $prev
if ($compileExit -ne 0) { Write-Host "FAIL (compile, exit=$compileExit)" -ForegroundColor Red; exit 1 }

# RISC-V ships a flat .bin (every hart enters __start at 0x80000000, so it is
# loaded there). ARM64 has no .bin -- QEMU boots the ELF and honours its entry
# (0x40100880, past the vector table), and the secondaries are held in PSCI
# until core 0's __start issues CPU_ON.
$binFile = [System.IO.Path]::ChangeExtension($elfOut, '.bin')
if ($Arch -eq 'riscv64' -and -not (Test-Path $binFile)) { Write-Host "FAIL (no .bin produced)" -ForegroundColor Red; exit 1 }
Write-Host "OK"

$expectedFile = Join-Path $dir "$name.expected"
if (-not (Test-Path -PathType Leaf $expectedFile)) { Write-Host "  PASS (compile only)" -ForegroundColor DarkGreen; exit 0 }

# --- Run under QEMU with N cores ---
Write-Host -NoNewline "  run ... "
$uartLog = Join-Path $testOutDir 'uart.smp.log'
if (Test-Path $uartLog) { Remove-Item $uartLog -Force }

# Guest RAM must cover each backend's boot stack (RISC-V rv-sp = 0xBFFF0000,
# ARM64 sp = 0x80000000 -- both ~1 GB above the RAM base), so -m 1024M for both.
# Keep in sync with rv-sp (RiscVRuntime.codex) and the ARM64 sp (Arm64Runtime.codex).
# RISC-V: flat .bin at 0x80000000 (all harts enter there), -bios none. ARM64: the
# ELF (QEMU honours its entry), PSCI (default conduit HVC) brings up the secondaries.
$machArgs = if ($Arch -eq 'riscv64') {
    @('-M','virt','-m','1024M','-smp',"$Cores",'-display','none','-monitor','none','-bios','none',
      '-device',"loader,file=$binFile,addr=0x80000000",'-serial',"file:$uartLog")
} else {
    @('-M','virt','-cpu','cortex-a53','-m','1024M','-smp',"$Cores",'-display','none','-monitor','none',
      '-kernel',$elfOut,'-serial',"file:$uartLog")
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
[System.IO.File]::WriteAllText((Join-Path $testOutDir 'runtime.smp.actual'), $actual, [System.Text.UTF8Encoding]::new($false))

if ($expectedText -eq $actual) {
    Write-Host "PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host "FAIL (output mismatch)" -ForegroundColor Red
    Write-Host "  expected: [$($expectedText.Trim())]"
    Write-Host "  actual:   [$($actual.Trim())]"
    exit 1
}
