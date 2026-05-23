# vm-config.ps1 — shared VM config + helpers for the harness.
# Default VM is codex-vm; set $env:USE_QEMU=1 to force fallback (QEMU).
# Dot-source from compile.ps1, test-run.ps1, test.ps1, build.ps1.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Locate codex-vm or fallback VM. Default is codex-vm; set $env:USE_QEMU=1
# to force fallback (needed for GDB watchpoints / TCG-only debugging).
$script:CodexVmBin = Join-Path (Split-Path $PSScriptRoot) 'tools\codex-vm.exe'
$script:UseCodexVm = -not $env:USE_QEMU -and (Test-Path -PathType Leaf $script:CodexVmBin)

$script:FallbackVmBin = $env:QEMU_BIN_WHPX
if (-not $script:FallbackVmBin) {
    foreach ($p in 'D:\Program Files\qemu\qemu-system-x86_64.exe','C:\Program Files\qemu\qemu-system-x86_64.exe','C:\Program Files (x86)\qemu\qemu-system-x86_64.exe') {
        if (Test-Path -PathType Leaf $p) { $script:FallbackVmBin = $p; break }
    }
}
if (-not $script:UseCodexVm -and (-not $script:FallbackVmBin -or -not (Test-Path -PathType Leaf $script:FallbackVmBin))) {
    throw "Neither codex-vm nor fallback VM found. Build tools/codex-vm.c or install QEMU."
}
$script:FallbackAccelFlags = @('-accel', 'whpx')

# Per-agent port slot (workdir basename suffix). cam = 0..3699, nib = 3700..7399.
# Each slot is 3700 even ports starting at 50200, stride 2 (data + ctrl pair).
$script:AgentSlot = switch -Wildcard ((Split-Path -Leaf (Get-Location).Path)) {
    '*-nib' { 1 }
    default { 0 }
}
$script:PerSlot = 3700

function Get-VmPort {
    param([int]$Attempt = 0)
    $offset = ((Get-Random -Min 0 -Max 32768) + $Attempt * 991) % $script:PerSlot
    return 50200 + ($script:AgentSlot * $script:PerSlot + $offset) * 2
}

function Get-VmChardevData { param([int]$Port) "socket,id=ch0,host=127.0.0.1,port=$Port,server=on,wait=on,nodelay=on" }
function Get-VmChardevCtrl { param([int]$Port) "socket,id=ch1,host=127.0.0.1,port=$Port,server=on,wait=on,nodelay=on" }

# Connect data + ctrl TCP sockets to a freshly-launched VM. The VM binds
# the listener and blocks until we connect; once connected, the guest
# boots and READY arrives on the ctrl channel.
# Returns a hashtable @{ Data = TcpClient; Ctrl = TcpClient } or $null.
function Connect-Vm {
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
function Read-VmReady {
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

function Close-Vm {
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

# CCE encode/decode tables — shared by all plug run scripts.
$script:CceToUnicode = @(
    0, 10, 32,
    48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
    101, 116, 97, 111, 105, 110, 115, 104, 114, 100,
    108, 99, 117, 109, 119, 102, 103, 121, 112, 98,
    118, 107, 106, 120, 113, 122,
    69, 84, 65, 79, 73, 78, 83, 72, 82, 68,
    76, 67, 85, 77, 87, 70, 71, 89, 80, 66,
    86, 75, 74, 88, 81, 90,
    46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,
    43, 61, 42, 60, 62,
    47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,
    94,
    36, 37,
    233, 232, 234, 235, 225, 224, 226, 228,
    243, 244, 246, 250, 252, 241, 231, 237
)
$script:UnicodeToCce = [byte[]]::new(256)
for ($i = 0; $i -lt 256; $i++) { $script:UnicodeToCce[$i] = 68 }
for ($i = 0; $i -lt $script:CceToUnicode.Length; $i++) {
    $u = $script:CceToUnicode[$i] % 256
    $script:UnicodeToCce[$u] = [byte]$i
}

function ConvertTo-Cce {
    param([byte[]]$Bytes)
    $out = [byte[]]::new($Bytes.Length)
    for ($i = 0; $i -lt $Bytes.Length; $i++) {
        $out[$i] = $script:UnicodeToCce[$Bytes[$i]]
    }
    return $out
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

# Resolve a code address to "function+offset" using a .map file.
# Returns $null if no match. Accepts hex (0x...) or decimal.
$script:MapCache = @{}
function Resolve-Rip {
    param([long]$Rip, [string]$MapFile = '')
    if (-not $MapFile) {
        $MapFile = Join-Path (Split-Path $PSScriptRoot) 'seed\Codex.map'
    }
    if (-not (Test-Path -PathType Leaf $MapFile)) { return $null }
    if (-not $script:MapCache[$MapFile]) {
        $entries = @()
        foreach ($line in [System.IO.File]::ReadAllLines($MapFile)) {
            if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
                $entries += [PSCustomObject]@{
                    Addr = [Convert]::ToInt64($matches[1], 16)
                    Size = [int]$matches[2]
                    Name = $matches[3]
                }
            }
        }
        $script:MapCache[$MapFile] = $entries
    }
    foreach ($e in $script:MapCache[$MapFile]) {
        if ($Rip -ge $e.Addr -and $Rip -lt ($e.Addr + $e.Size)) {
            $off = $Rip - $e.Addr
            return "$($e.Name)+0x$($off.ToString('X'))"
        }
    }
    return $null
}

# Parse an !EXC line and append resolved function names.
function Resolve-ExcLine {
    param([string]$Line, [string]$MapFile = '')
    $resolved = $Line
    if ($Line -match 'RIP=([0-9a-fA-F]+)') {
        $rip = [Convert]::ToInt64($matches[1], 16)
        $sym = Resolve-Rip -Rip $rip -MapFile $MapFile
        if ($sym) { $resolved = "$resolved  --> $sym" }
    }
    return $resolved
}

# Reverse lookup: function name -> address. Returns 0 if not found.
function Resolve-Name {
    param([string]$Name, [string]$MapFile = '')
    if (-not $MapFile) { $MapFile = Join-Path (Split-Path $PSScriptRoot) 'seed\Codex.map' }
    if (-not (Test-Path -PathType Leaf $MapFile)) { return 0 }
    if (-not $script:MapCache[$MapFile]) { Resolve-Rip -Rip 0 -MapFile $MapFile | Out-Null }
    foreach ($e in $script:MapCache[$MapFile]) {
        if ($e.Name -eq $Name) { return $e.Addr }
    }
    return 0
}

# Exception vector names for readable crash reports.
$script:ExcNames = @{
    0 = 'divide error'; 1 = 'debug'; 3 = 'breakpoint'; 6 = 'invalid opcode'
    8 = 'double fault'; 13 = 'general protection'; 14 = 'page fault'
}

# Parse a full !EXC line (plus optional S[n] stack lines) into a readable
# multi-line crash report with resolved addresses.
function Format-CrashReport {
    param([string[]]$Lines, [string]$MapFile = '')
    $excLine = $Lines[0]
    $report = [System.Collections.Generic.List[string]]::new()
    $vec = '??'
    $vecName = 'exception'
    if ($excLine -match '!EXC=([0-9a-fA-F]+)') {
        $vec = $matches[1]
        $vecInt = [Convert]::ToInt32($vec, 16)
        if ($script:ExcNames[$vecInt]) { $vecName = $script:ExcNames[$vecInt] }
    }
    $rip = 0; $ripSym = '<unknown>'
    if ($excLine -match 'RIP=([0-9a-fA-F]+)') {
        $rip = [Convert]::ToInt64($matches[1], 16)
        $s = Resolve-Rip -Rip $rip -MapFile $MapFile
        if ($s) { $ripSym = $s }
    }
    $cr2 = ''
    if ($excLine -match 'CR2=([0-9a-fA-F]+)') { $cr2 = $matches[1] }
    $header = "CRASH in $ripSym ($vecName"
    if ($cr2 -and $vecInt -eq 14) { $header += ", CR2=0x$cr2" }
    $header += ')'
    $report.Add($header)
    $report.Add("  RIP   0x$($rip.ToString('X16').TrimStart('0').PadLeft(8,'0'))  $ripSym")
    foreach ($regName in @('callR','RBX','R12','R13','R14','R10','RDI','RSI','R15')) {
        if ($excLine -match "$regName=([0-9a-fA-F]+)") {
            $val = [Convert]::ToInt64($matches[1], 16)
            $sym = Resolve-Rip -Rip $val -MapFile $MapFile
            $label = if ($sym) { "  $sym" } elseif ($regName -eq 'R10') { "  (heap @ $([math]::Round(($val - 0x600000) / 1048576.0, 1)) MB)" } else { '' }
            $report.Add("  $($regName.PadRight(5)) 0x$($val.ToString('X16').TrimStart('0').PadLeft(8,'0'))$label")
        }
    }
    $stackAddrs = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $Lines) {
        $m = [regex]::Matches($line, 'S\[(\d+)\]=([0-9a-fA-F]+)')
        foreach ($hit in $m) {
            $idx = $hit.Groups[1].Value
            $sval = [Convert]::ToInt64($hit.Groups[2].Value, 16)
            if ($sval -ge 0x100000 -and $sval -lt 0x400000) {
                $sym = Resolve-Rip -Rip $sval -MapFile $MapFile
                if ($sym) { $stackAddrs.Add("    S[$idx] 0x$($sval.ToString('X8'))  $sym") }
            }
        }
    }
    if ($stackAddrs.Count -gt 0) {
        $report.Add('  Stack trace (heuristic):')
        foreach ($sa in $stackAddrs) { $report.Add($sa) }
    }
    return $report.ToArray()
}

# Launch VM (codex-vm or fallback) with the standard dual-socket layout.
# Returns @{ Process; Conn } on success, $null on failure (after 4 retry
# attempts). $PCore is the P-core index (0-7); default 1.
function Start-VmRun {
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
        $dataPort = Get-VmPort -Attempt $attempt
        $ctrlPort = $dataPort + 1
        $args = @($script:FallbackAccelFlags) + @(
            '-machine', 'kernel-irqchip=off',
            '-kernel', $Kernel,
            '-chardev', (Get-VmChardevData -Port $dataPort),
            '-chardev', (Get-VmChardevCtrl -Port $ctrlPort),
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
        $proc = Start-Process -FilePath $script:FallbackVmBin -ArgumentList $args -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) { continue }
        $conn = Connect-Vm -DataPort $dataPort -CtrlPort $ctrlPort -TimeoutSec $ConnectTimeoutSec
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
        $dataPort = Get-VmPort -Attempt $attempt
        $ctrlPort = $dataPort + 1
        $vmArgs = @('-kernel', $Kernel, '-data-port', "$dataPort", '-ctrl-port', "$ctrlPort", '-mem', "$MemMB", '-headless')
        if ($disk) { $vmArgs += @('-disk', $disk) }
        foreach ($ea in $ExtraArgs) { if ($ea -notmatch 'file=') { $vmArgs += $ea } }
        $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) { continue }
        $conn = Connect-Vm -DataPort $dataPort -CtrlPort $ctrlPort -TimeoutSec $ConnectTimeoutSec
        if ($conn) { return @{ Process = $proc; Conn = $conn; StdoutFile = $stdoutFile; StderrFile = $stderrFile } }
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    return $null
}
