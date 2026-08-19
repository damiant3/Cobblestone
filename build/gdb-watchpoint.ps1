# gdb-watchpoint.ps1 -- Memory watchpoint / breakpoint debugger for bare-metal Codex via QEMU + GDB
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Src,
    [string]$Watch = '',
    [string]$Break = '',
    [string]$ReadWatch = '',
    [ValidateSet('tcg','whpx')]
    [string]$Accel = 'tcg',
    [int]$TimeoutSec = 300,
    [string]$Kernel = ''
)

# Launches QEMU in TCG mode (required for hardware watchpoints via GDB),
# feeds source over TCP serial, and uses WSL gdb to set a write-watchpoint
# on a given address. Reports the instruction and register state when the
# watchpoint fires.
# 
# NOTE: This script always uses QEMU (not codex-vm) because GDB stubs
# require QEMU's -gdb flag and TCG mode for hardware watchpoints.
# 
# Requirements: WSL with /usr/bin/gdb, QEMU installed on Windows.
# 
# Modes:
#   -Watch 0xADDR    Hardware write-watchpoint (TCG only, slow but catches writer)
#   -Break 0xADDR    Hardware breakpoint (works with WHPX too, fast)
#   -ReadWatch 0xADDR  Hardware read-watchpoint (TCG only)
# 
# Usage:
#   build/gdb-watchpoint.ps1 -Src plug-source.codex -Watch 0x1a6f7c5
#   build/gdb-watchpoint.ps1 -Src plug-source.codex -Break 0x273f22
#   build/gdb-watchpoint.ps1 -Src plug-source.codex -Watch 0x1a6f7c5 -Accel whpx
# 
# Notes:
#   - TCG is ~20-50x slower than WHPX. A compile that takes 2s under WHPX
#     may take 60-120s under TCG. Budget accordingly.
#   - WHPX does NOT support watchpoints (only breakpoints). If you pass
#     -Watch with -Accel whpx, the script will warn and exit.
#   - The source file should be pre-bundled (forewords inlined). Use the
#     output of plugs/csharp/build.ps1 phase 1 or test-compile-batch.ps1's
#     foreword resolver.


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path
$env:CODEX_VM_HOST = 'qemu'
. (Join-Path $PSScriptRoot 'vm-config.ps1')

if ((-not $Kernel)) {
    $Kernel = 'build-output\bare-metal\Codex.cdx'
}
if ((-not (Test-Path -PathType Leaf $Kernel))) {
    [Console]::Error.WriteLine("MISSING: $Kernel")
    exit 2
}
if ((((-not $Watch) -and (-not $Break)) -and (-not $ReadWatch))) {
    [Console]::Error.WriteLine('Specify -Watch, -Break, or -ReadWatch with an address')
    exit 2
}
if ((($Accel -eq 'whpx') -and ($Watch -or $ReadWatch))) {
    [Console]::Error.WriteLine('WHPX does not support watchpoints. Use -Accel tcg or use -Break instead.')
    exit 2
}

$gdbCheck = wsl -- which gdb 2>&1
if ((-not ($LASTEXITCODE -eq 0))) {
    [Console]::Error.WriteLine('gdb not found in WSL. Install: sudo apt install gdb')
    exit 2
}


$srcBytes = [System.IO.File]::ReadAllBytes($Src)
$addr = if ($Watch) { $Watch } elseif ($Break) { $Break } else { $ReadWatch }

# Build GDB command string
$bpCmd = if ($Watch) { "watch *(unsigned long long *)$addr" } elseif ($ReadWatch) { "rwatch *(unsigned long long *)$addr" } else { "hbreak *$addr" }



$gdbFile = Join-Path $env:TEMP "codex-gdb-$PID.gdb"
$gdbContent = @"
set architecture i386:x86-64
target remote localhost:1234
set pagination off
set confirm off
$bpCmd
continue
printf "\n=== STOPPED at %s ===\n", "$bpCmd"
printf "RIP=%#lx RAX=%#lx RBX=%#lx RCX=%#lx\n", `$rip, `$rax, `$rbx, `$rcx
printf "RDX=%#lx RSI=%#lx RDI=%#lx RBP=%#lx\n", `$rdx, `$rsi, `$rdi, `$rbp
printf "R8=%#lx R9=%#lx R10=%#lx R11=%#lx\n", `$r8, `$r9, `$r10, `$r11
printf "R12=%#lx R13=%#lx R14=%#lx R15=%#lx\n", `$r12, `$r13, `$r14, `$r15
printf "RSP=%#lx\n", `$rsp
printf "\nDisassembly around RIP:\n"
x/3i `$rip-16
printf "--->\n"
x/5i `$rip
printf "\nValue at watched address:\n"
x/2gx $($addr)
printf "\nStack (8 slots):\n"
x/8gx `$rsp
printf "\nBacktrace (RBP chain, 4 frames):\n"
x/2gx `$rbp
set `$_f1 = *(unsigned long long *)`$rbp
x/2gx `$_f1
kill
quit
"@

$gdbContent | Set-Content $gdbFile -Encoding UTF8 -NoNewline

$wslGdbPath = $gdbFile -replace '^([A-Z]):', { '/mnt/' + $_.Groups[1].Value.ToLower() } -replace '\\', '/'
$gdbOut = Join-Path $env:TEMP "codex-gdb-$PID.out"
$gdbErr = Join-Path $env:TEMP "codex-gdb-$PID.err"


try {
    $savedAccel = $script:FallbackAccelFlags
    $script:FallbackAccelFlags = @('-accel', $Accel)
    Write-Host "[1/4] Starting QEMU ($Accel mode) with GDB stub on :1234..."
    $run = Start-VmRun -Kernel $Kernel -ConnectTimeoutSec 30 -MemMB 3072 -ExtraArgs @('-gdb', 'tcp::1234', '-S')
    $script:FallbackAccelFlags = $savedAccel
    if ((-not $run)) {
        [Console]::Error.WriteLine('QEMU failed to start')
        exit 3
    }
    Write-Host "  PID=$($run.Process.Id)"


    # Start GDB (this sends 'continue' which unpauses the CPU)
    Write-Host "[2/4] Launching GDB ($bpCmd)..."
    $gdbProc = Start-Process -FilePath 'wsl' -ArgumentList @('--', 'timeout', "$TimeoutSec", '/usr/bin/gdb', '-batch', '-nx', '-x', $wslGdbPath) -PassThru -WindowStyle Hidden -RedirectStandardOutput $gdbOut -RedirectStandardError $gdbErr
    Start-Sleep -Seconds 3


    # Wait for READY (CPU is now running via GDB continue)
    Write-Host '[3/4] Waiting for READY...'
    $conn = $run.Conn
    if ((-not (Read-VmReady -Conn $conn -TimeoutSec 120))) {
        Write-Host 'READY not received within 120s.'
        Write-Host 'GDB output so far:'
        if ((Test-Path -PathType Leaf $gdbOut)) {
            Get-Content $gdbOut | Select-Object -First 10
        }
        exit 4
    }

    # Send source bytes over TCP serial
    $stream = $conn.Data.GetStream()
    $hdr = [System.Text.Encoding]::UTF8.GetBytes("CDX`n")
    $stream.Write($hdr, 0, $hdr.Length)
    $stream.Write($srcBytes, 0, $srcBytes.Length)
    $stream.WriteByte(4)
    $stream.Flush()
    Write-Host "  Source sent ($($srcBytes.Length) bytes)"


    Write-Host '[4/4] Waiting for watchpoint/breakpoint hit...'
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    :wp_wait while ((Get-Date) -lt $deadline) {
        if ($gdbProc.HasExited) {
            break
        }
        $line = Read-StreamLine -Stream $stream -TimeoutSec 2
        if ($null -ne $line) {
            if ((($line.StartsWith('!EXC') -or $line.StartsWith('SIZE:')) -or $line.StartsWith('CODEGEN-'))) {
                Write-Host "SERIAL: $line"
                break
            }
        }

    }
    Start-Sleep -Seconds 2
    if ((-not $gdbProc.HasExited)) {
        Write-Host 'GDB timed out - killing.'
        Stop-Process -Id $gdbProc.Id -Force -ErrorAction SilentlyContinue
    }


    Write-Host ''
    Write-Host '==========================================='
    Write-Host ' GDB OUTPUT'
    Write-Host '==========================================='
    if ((Test-Path -PathType Leaf $gdbOut)) {
        Get-Content $gdbOut
    }
    $errTxt = if (Test-Path $gdbErr) { Get-Content $gdbErr -Raw } else { '' }
    if (($errTxt.Trim() -and (-not ($errTxt -match '^warning:')))) {
        Write-Host ''
        Write-Host 'GDB STDERR:'
        Write-Host $errTxt
    }

} finally {
    if ($run) {
        Close-Vm -Conn $run.Conn -Process $run.Process
    }
    Remove-Item -Force $gdbFile, $gdbOut, $gdbErr -ErrorAction SilentlyContinue
}
