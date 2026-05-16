# Helper invoked by test.ps1. Boots a CDX/ELF kernel under QEMU, captures
# serial output between READY and HEAP, writes to the output file.
#
# Two modes:
#   FILE MODE (default): chardev file backend. No TCP, no race. QEMU writes
#     serial output to host files; script reads them after QEMU exits.
#   TCP MODE (when -StdinFile is given): chardev socket backend. Needed to
#     pump stdin bytes to the guest via COM1.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Kernel,
    [Parameter(Mandatory=$true)] [string]$OutFile,
    [string]$StdinFile = '',
    [string]$DiskFile = '',
    [int]$PCore = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'qemu-config.ps1')

$sample = [System.IO.Path]::GetFileNameWithoutExtension($Kernel)
$wallBudgetSec = 60
$deadline = (Get-Date).AddSeconds($wallBudgetSec)

function Remaining { param([datetime]$D) [int][math]::Max(1, [math]::Ceiling(($D - (Get-Date)).TotalSeconds)) }

$needsTcp = $StdinFile -and (Test-Path -PathType Leaf $StdinFile)

# ═══════════════════════════════════════════════════════════════════════
# FILE MODE — no sockets, no race
# ═══════════════════════════════════════════════════════════════════════
if (-not $needsTcp) {
    $com1File = Join-Path $env:TEMP "cdx-$sample-com1-$PID.tmp"
    $com2File = Join-Path $env:TEMP "cdx-$sample-com2-$PID.tmp"
    $stdoutFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllBytes($com1File, [byte[]]::new(0))
    [System.IO.File]::WriteAllBytes($com2File, [byte[]]::new(0))

    try {
        Write-SweepLog "$sample run-start pcore=$PCore file-mode"
        $diskArgs = @()
        if ($DiskFile -and (Test-Path -PathType Leaf $DiskFile)) {
            $diskArgs = @('-drive', "file=$DiskFile,format=raw,if=ide,index=0")
        }
        $qemuArgs = @($script:QemuAccelFlags) + @(
            '-machine', 'kernel-irqchip=off',
            '-kernel', $Kernel,
            '-chardev', "file,id=ch0,path=$com1File",
            '-chardev', "file,id=ch1,path=$com2File",
            '-serial', 'chardev:ch0',
            '-serial', 'chardev:ch1',
            '-device', 'isa-debug-exit,iobase=0xf4,iosize=0x04',
            '-netdev', 'user,id=net0',
            '-device', 'ne2k_isa,netdev=net0,irq=9,iobase=0x300,mac=52:54:00:12:34:56',
            '-display', 'none',
            '-no-reboot',
            '-m', '2048'
        )
        if ($diskArgs.Count -gt 0) { $qemuArgs += $diskArgs }

        $proc = Start-Process -FilePath $script:QemuBin -ArgumentList $qemuArgs -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        Write-SweepLog "$sample qemu-pid=$($proc.Id) file-mode"

        $remainMs = (Remaining $deadline) * 1000
        if (-not $proc.WaitForExit($remainMs)) {
            Write-SweepLog "$sample run-fail wall-budget-exceeded pid=$($proc.Id)"
            try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
            [System.IO.File]::WriteAllText($OutFile, '', [System.Text.UTF8Encoding]::new($false))
            exit 1
        }

        $ctrl = [System.IO.File]::ReadAllText($com2File)
        if ($ctrl -notmatch 'READY') {
            Write-SweepLog "$sample run-fail no-ready pid=$($proc.Id)"
            [System.IO.File]::WriteAllText($OutFile, '', [System.Text.UTF8Encoding]::new($false))
            exit 1
        }

        $raw = [System.IO.File]::ReadAllText($com1File) -replace "`r", ''
        $allLines = $raw -split "`n"
        $lines = [System.Collections.Generic.List[string]]::new()
        foreach ($l in $allLines) {
            if ($l.StartsWith('HEAP:') -or $l.StartsWith('WD:') -or $l.StartsWith('STACK:')) { continue }
            $lines.Add($l)
        }
        while ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }
        $body = if ($lines.Count -gt 0) { ($lines -join "`n") + "`n" } else { '' }
        [System.IO.File]::WriteAllText($OutFile, $body, [System.Text.UTF8Encoding]::new($false))
        Write-SweepLog "$sample run-ok"
        exit 0
    } finally {
        if ($proc -and -not $proc.HasExited) {
            try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
        }
        Remove-Item -Force $com1File, $com2File, $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    }
}

# ═══════════════════════════════════════════════════════════════════════
# TCP MODE — for stdin tests only
# ═══════════════════════════════════════════════════════════════════════
$run = $null
try {
    Write-SweepLog "$sample run-start pcore=$PCore tcp-mode"
    $connectBudget = [math]::Min(5, (Remaining $deadline))
    $diskArgs = @()
    if ($DiskFile -and (Test-Path -PathType Leaf $DiskFile)) {
        $diskArgs = @('-drive', "file=$DiskFile,format=raw,if=ide,index=0")
    }
    $run = Start-QemuRun -Kernel $Kernel -ConnectTimeoutSec $connectBudget -MemMB 2048 -PCore $PCore -ExtraArgs $diskArgs
    if (-not $run) {
        Write-SweepLog "$sample run-fail no-connect"
        [System.IO.File]::WriteAllText($OutFile, '', [System.Text.UTF8Encoding]::new($false))
        exit 1
    }
    Write-SweepLog "$sample qemu-pid=$($run.Process.Id) connected"
    $stream = $run.Conn.Data.GetStream()
    $ctrlStream = $run.Conn.Ctrl.GetStream()
    $lines = [System.Collections.Generic.List[string]]::new()
    $sawReady = $false
    $sawHeap = $false
    $stdinSent = $false
    while ((Get-Date) -lt $deadline -and -not $sawHeap) {
        while ($true) {
            try { if (-not $ctrlStream.DataAvailable) { break } } catch { break }
            $cline = Read-StreamLine -Stream $ctrlStream -TimeoutSec 2
            if ($null -eq $cline) { break }
            if ($cline.StartsWith('READY')) { $sawReady = $true }
            if ($cline.StartsWith('WD:')) { [Console]::Error.WriteLine(">>> $cline") }
            if ($cline.StartsWith('HEAP:')) { $sawHeap = $true; break }
        }
        if ($sawHeap) {
            while ($true) {
                $line = Read-StreamLine -Stream $stream -TimeoutSec 1
                if ($null -eq $line) { break }
                if (-not $line.StartsWith('HEAP:') -and -not $line.StartsWith('WD:') -and -not $line.StartsWith('STACK:')) { $lines.Add($line) }
            }
            break
        }
        if ($sawReady -and -not $stdinSent) {
            $stdinSent = $true
            try {
                $bytes = [System.IO.File]::ReadAllBytes($StdinFile)
                if ($bytes.Length -gt 0) { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush() }
                Write-SweepLog "$sample stdin-pumped bytes=$($bytes.Length)"
                $eofByte = [byte[]]@(4)
                $ctrlStream.Write($eofByte, 0, 1)
                $ctrlStream.Flush()
            } catch {
                Write-SweepLog "$sample stdin-write-failed"
            }
        }
        while ($true) {
            try { if (-not $stream.DataAvailable) { break } } catch { break }
            $line = Read-StreamLine -Stream $stream -TimeoutSec 2
            if ($null -eq $line) { break }
            if ($line.StartsWith('HEAP:')) { $sawHeap = $true }
            elseif ($line.StartsWith('WD:')) { [Console]::Error.WriteLine(">>> $line") }
            elseif (-not $line.StartsWith('STACK:')) { $lines.Add($line) }
        }
        if ($sawHeap) { break }
        if (-not $sawReady) {
            Start-Sleep -Milliseconds 50
        } else {
            $lineBudget = [math]::Min(4, (Remaining $deadline))
            $line = Read-StreamLine -Stream $stream -TimeoutSec $lineBudget
            if ($null -ne $line) {
                if ($line.StartsWith('HEAP:')) { $sawHeap = $true }
                elseif ($line.StartsWith('WD:')) { [Console]::Error.WriteLine(">>> $line") }
                elseif (-not $line.StartsWith('STACK:')) { $lines.Add($line) }
            } elseif ($run.Process.HasExited) {
                break
            }
        }
    }
    while ($true) {
        $line = Read-StreamLine -Stream $stream -TimeoutSec 1
        if ($null -eq $line) { break }
        if ($line.StartsWith('HEAP:') -or $line.StartsWith('STACK:')) { continue }
        if ($line.StartsWith('WD:')) { [Console]::Error.WriteLine(">>> $line") }
        else { $lines.Add($line) }
    }
    if (-not $sawHeap) {
        while ($true) {
            $cline = Read-StreamLine -Stream $ctrlStream -TimeoutSec 1
            if ($null -eq $cline) { break }
            if ($cline.StartsWith('HEAP:')) { $sawHeap = $true; break }
        }
    }
    if (-not $sawReady) {
        Write-SweepLog "$sample run-fail no-ready pid=$($run.Process.Id)"
        [System.IO.File]::WriteAllText($OutFile, '', [System.Text.UTF8Encoding]::new($false))
        exit 1
    }
    if ((Get-Date) -ge $deadline -and -not $sawHeap) {
        Write-SweepLog "$sample run-fail wall-budget-exceeded pid=$($run.Process.Id)"
        [System.IO.File]::WriteAllText($OutFile, '', [System.Text.UTF8Encoding]::new($false))
        exit 1
    }
    $body = if ($lines.Count -gt 0) { ($lines -join "`n") + "`n" } else { '' }
    [System.IO.File]::WriteAllText($OutFile, $body, [System.Text.UTF8Encoding]::new($false))
    Write-SweepLog "$sample run-ok"
    exit 0
} finally {
    if ($run) {
        Close-Qemu -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
}
