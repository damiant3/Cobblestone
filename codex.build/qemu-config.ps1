# qemu-config.ps1 — shared QEMU config + helpers for the harness.
# Dot-source from sample-compile-selfhost.ps1, run-for-test.ps1, test.ps1, build.ps1.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Locate codex-vm or QEMU. $env:CODEX_VM=1 prefers codex-vm.exe.
$script:UseCodexVm = $false
$script:CodexVmBin = Join-Path (Split-Path $PSScriptRoot) 'tools\codex-vm.exe'
if ($env:CODEX_VM -and (Test-Path -PathType Leaf $script:CodexVmBin)) {
    $script:UseCodexVm = $true
}

$script:QemuBin = $env:QEMU_BIN_WHPX
if (-not $script:QemuBin) {
    foreach ($p in 'D:\Program Files\qemu\qemu-system-x86_64.exe','C:\Program Files\qemu\qemu-system-x86_64.exe','C:\Program Files (x86)\qemu\qemu-system-x86_64.exe') {
        if (Test-Path -PathType Leaf $p) { $script:QemuBin = $p; break }
    }
}
if (-not $script:UseCodexVm -and (-not $script:QemuBin -or -not (Test-Path -PathType Leaf $script:QemuBin))) {
    throw "Neither codex-vm nor QEMU found. Set CODEX_VM=1 or install QEMU."
}
$script:QemuAccelFlags = @('-accel', 'whpx')

# Per-agent port slot (workdir basename suffix). cam = 0..3699, nib = 3700..7399.
# Each slot is 3700 even ports starting at 50200, stride 2 (data + ctrl pair).
$script:AgentSlot = switch -Wildcard ((Split-Path -Leaf (Get-Location).Path)) {
    '*-nib' { 1 }
    default { 0 }
}
$script:PerSlot = 3700

function Get-QemuPort {
    param([int]$Attempt = 0)
    $offset = ((Get-Random -Min 0 -Max 32768) + $Attempt * 991) % $script:PerSlot
    return 50200 + ($script:AgentSlot * $script:PerSlot + $offset) * 2
}

function Get-QemuChardevData { param([int]$Port) "socket,id=ch0,host=127.0.0.1,port=$Port,server=on,wait=on,nodelay=on" }
function Get-QemuChardevCtrl { param([int]$Port) "socket,id=ch1,host=127.0.0.1,port=$Port,server=on,wait=on,nodelay=on" }

# Connect data + ctrl TCP sockets to a freshly-launched QEMU. QEMU is started
# with `wait=on`, so it binds the listener and blocks until we connect; once
# connected, the guest boots and READY arrives on FD 4 (ctrl).
# Returns a hashtable @{ Data = TcpClient; Ctrl = TcpClient } or $null.
function Connect-Qemu {
    param([int]$DataPort, [int]$CtrlPort, [int]$TimeoutSec = 30)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $data = $null; $ctrl = $null
        try {
            $data = [System.Net.Sockets.TcpClient]::new()
            $data.Connect('127.0.0.1', $DataPort)
            $ctrl = [System.Net.Sockets.TcpClient]::new()
            $ctrl.Connect('127.0.0.1', $CtrlPort)
            return @{ Data = $data; Ctrl = $ctrl }
        } catch {
            if ($data) { $data.Dispose() }
            if ($ctrl) { $ctrl.Dispose() }
            Start-Sleep -Milliseconds 500
        }
    }
    return $null
}

# Read bytes from a NetworkStream until LF (0x0A); return the line as UTF-8
# without the trailing LF/CR. $null on timeout or EOF.
function Read-StreamLine {
    param([System.IO.Stream]$Stream, [int]$TimeoutSec = 60)
    $buf = [System.Collections.Generic.List[byte]]::new()
    $one = New-Object byte[] 1
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        $remainMs = [int][math]::Max(100, ($deadline - (Get-Date)).TotalMilliseconds)
        if ($Stream.CanTimeout) { $Stream.ReadTimeout = $remainMs }
        try { $n = $Stream.Read($one, 0, 1) } catch { return $null }
        if ($n -le 0) { return $null }
        if ($one[0] -eq 10) {
            $bytes = $buf.ToArray()
            $line = [System.Text.Encoding]::UTF8.GetString($bytes)
            return $line.TrimEnd("`r")
        }
        $buf.Add($one[0])
    }
}

# Read exactly $Count bytes from the stream. Returns byte[] or $null.
function Read-StreamBytes {
    param([System.IO.Stream]$Stream, [int]$Count, [int]$TimeoutSec = 60)
    $buf = New-Object byte[] $Count
    $offset = 0
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($offset -lt $Count) {
        $remainMs = [int][math]::Max(100, ($deadline - (Get-Date)).TotalMilliseconds)
        if ($Stream.CanTimeout) { $Stream.ReadTimeout = $remainMs }
        try { $n = $Stream.Read($buf, $offset, $Count - $offset) } catch { return $null }
        if ($n -le 0) { return $null }
        $offset += $n
    }
    return $buf
}

# Read the READY greeting on the control channel (FD 4 in bash).
function Read-QemuReady {
    param($Conn, [int]$TimeoutSec = 60)
    if (-not $Conn -or -not $Conn.Ctrl) { return $false }
    $stream = $Conn.Ctrl.GetStream()
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remaining = [int]($deadline - [DateTime]::UtcNow).TotalSeconds
        if ($remaining -le 0) { return $false }
        $line = Read-StreamLine -Stream $stream -TimeoutSec $remaining
        if ($line -and $line.StartsWith('READY')) { return $true }
        if (-not $line) { return $false }
    }
    return $false
}

function Close-Qemu {
    param($Conn, $Process)
    if ($Conn) {
        if ($Conn.Data) { try { $Conn.Data.Dispose() } catch {} }
        if ($Conn.Ctrl) { try { $Conn.Ctrl.Dispose() } catch {} }
    }
    if ($Process -and -not $Process.HasExited) {
        try { Stop-Process -Id $Process.Id -Force -ErrorAction Stop } catch {}
        for ($i = 0; $i -lt 10 -and -not $Process.HasExited; $i++) { Start-Sleep -Milliseconds 100 }
        if (-not $Process.HasExited) {
            try { & taskkill.exe /F /T /PID $Process.Id 2>&1 | Out-Null } catch {}
        }
    }
}

# Append a timestamped line to $env:CODEX_SWEEP_LOG (no-op if unset). Safe
# under ForEach-Object -Parallel via a named system-wide mutex.
function Write-SweepLog {
    param([string]$Line)
    $logPath = $env:CODEX_SWEEP_LOG
    if (-not $logPath) { return }
    $stamp = (Get-Date).ToString('HH:mm:ss.fff')
    $entry = "[$stamp] $Line`n"
    $mutex = New-Object System.Threading.Mutex($false, 'Global\Codex-SweepLog')
    try {
        [void]$mutex.WaitOne()
        try { [System.IO.File]::AppendAllText($logPath, $entry, [System.Text.UTF8Encoding]::new($false)) } catch {}
    } finally {
        try { $mutex.ReleaseMutex() } catch {}
        $mutex.Dispose()
    }
}

# Launch QEMU with the standard dual-chardev layout. Returns @{ Process; Conn }
# on success, $null on failure (after 4 retry attempts). Every guest is pinned
# to a 2-logical-core P-core via ProcessorAffinity as the WHPX BSOD mitigation.
# $PCore is the P-core index (0-7); default 1 (host harness lives on P-core 0).
function Start-QemuRun {
    param(
        [string]$Kernel,
        [int]$ConnectTimeoutSec = 30,
        [int]$MemMB = 2048,
        [int]$PCore = 1,
        [string[]]$ExtraArgs = @()
    )
    if ($script:UseCodexVm) { return Start-CodexVmRun @PSBoundParameters }
    $stdoutFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $debugDir = $env:QEMU_DEBUG_LOG_DIR
    if ($debugDir) { New-Item -ItemType Directory -Force -Path $debugDir | Out-Null }
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        $dataPort = Get-QemuPort -Attempt $attempt
        $ctrlPort = $dataPort + 1
        $args = @($script:QemuAccelFlags) + @(
            '-machine', 'kernel-irqchip=off',
            '-kernel', $Kernel,
            '-chardev', (Get-QemuChardevData -Port $dataPort),
            '-chardev', (Get-QemuChardevCtrl -Port $ctrlPort),
            '-serial', 'chardev:ch0',
            '-serial', 'chardev:ch1',
            '-device', 'isa-debug-exit,iobase=0xf4,iosize=0x04',
            '-netdev', 'user,id=net0',
            '-device', 'ne2k_isa,netdev=net0,irq=9,iobase=0x300,mac=52:54:00:12:34:56',
            '-display', 'none',
            '-no-reboot',
            '-m', "$MemMB"
        )
        if ($ExtraArgs.Count -gt 0) { $args += $ExtraArgs }
        if ($debugDir) {
            $debugLog = Join-Path $debugDir "qemu-${dataPort}.log"
            $args += @('-D', $debugLog, '-d', 'guest_errors,unimp,cpu_reset')
        }
        $proc = Start-Process -FilePath $script:QemuBin -ArgumentList $args -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) { continue }
        $conn = Connect-Qemu -DataPort $dataPort -CtrlPort $ctrlPort -TimeoutSec $ConnectTimeoutSec
        if ($conn) { return @{ Process = $proc; Conn = $conn; StdoutFile = $stdoutFile; StderrFile = $stderrFile } }
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    return $null
}

function Start-CodexVmRun {
    param(
        [string]$Kernel,
        [int]$ConnectTimeoutSec = 30,
        [int]$MemMB = 2048,
        [int]$PCore = 1,
        [string[]]$ExtraArgs = @()
    )
    $stdoutFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $disk = $null
    foreach ($ea in $ExtraArgs) {
        if ($ea -match 'file=([^,]+)') { $disk = $Matches[1] }
    }
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        $dataPort = Get-QemuPort -Attempt $attempt
        $ctrlPort = $dataPort + 1
        $vmArgs = @('-kernel', $Kernel, '-data-port', "$dataPort", '-ctrl-port', "$ctrlPort", '-mem', "$MemMB")
        if ($disk) { $vmArgs += @('-disk', $disk) }
        $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) { continue }
        $conn = Connect-Qemu -DataPort $dataPort -CtrlPort $ctrlPort -TimeoutSec $ConnectTimeoutSec
        if ($conn) { return @{ Process = $proc; Conn = $conn; StdoutFile = $stdoutFile; StderrFile = $stderrFile } }
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    return $null
}
