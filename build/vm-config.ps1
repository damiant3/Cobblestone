# vm-config.ps1 — shared VM config + helpers for the harness.
# Memory-mapped I/O: no serial ports, no TCP sockets.
# The VM loads input from a file into guest memory at 0x400000.
# Output is written by the guest to 0x500000 and dumped to a file on exit.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:CodexVmBin = Join-Path (Split-Path $PSScriptRoot) 'tools\codex-vm.exe'
if (-not (Test-Path -PathType Leaf $script:CodexVmBin)) {
    throw "codex-vm not found at $($script:CodexVmBin). Build with tools/build-vm.ps1."
}

# CCE encode/decode tables — shared by plug run scripts.
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

# Resolve a code address to "function+offset" using a .map file.
$script:MapCache = @{}
function Resolve-Rip {
    param([long]$Rip, [string]$MapFile = '')
    if (-not $MapFile) { $MapFile = Join-Path (Split-Path $PSScriptRoot) 'seed\Codex.map' }
    if (-not (Test-Path $MapFile)) { return $null }
    if (-not $script:MapCache[$MapFile]) {
        $entries = @()
        foreach ($line in [System.IO.File]::ReadAllLines($MapFile)) {
            if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
                $entries += @{ Addr = [Convert]::ToInt64($matches[1], 16); Size = [int]$matches[2]; Name = $matches[3] }
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

function Resolve-Name {
    param([string]$Name, [string]$MapFile = '')
    if (-not $MapFile) { $MapFile = Join-Path (Split-Path $PSScriptRoot) 'seed\Codex.map' }
    if (-not (Test-Path $MapFile)) { return 0 }
    foreach ($line in [System.IO.File]::ReadAllLines($MapFile)) {
        if ($line -match '^(0x[0-9a-fA-F]+)\s+\d+\s+(.+)$') {
            if ($matches[2].Trim() -eq $Name) { return [Convert]::ToInt64($matches[1], 16) }
        }
    }
    return 0
}

function Format-CrashReport {
    param([string[]]$ExcLines)
    $report = [System.Collections.Generic.List[string]]::new()
    if ($ExcLines.Count -eq 0) { return $report }
    $excLine = $ExcLines[0]
    $vec = ''
    if ($excLine -match '!EXC=([0-9a-fA-F]+)') { $vec = $matches[1] }
    $vecInt = [Convert]::ToInt32($vec, 16)
    $vecName = switch ($vecInt) { 0 { 'divide error' } 6 { 'invalid opcode' } 13 { 'general protection' } 14 { 'page fault' } default { "vector $vecInt" } }
    $rip = 0
    if ($excLine -match 'RIP=([0-9a-fA-F]+)') { $rip = [Convert]::ToInt64($matches[1], 16) }
    $ripSym = Resolve-Rip -Rip $rip
    if (-not $ripSym) { $ripSym = "0x$($rip.ToString('X8'))" }
    $cr2 = ''
    if ($excLine -match 'CR2=([0-9a-fA-F]+)') {
        $cr2val = [Convert]::ToInt64($matches[1], 16)
        if ($cr2val -ne 0) { $cr2 = ", CR2=0x$($cr2val.ToString('X12'))" }
    }
    $header = "CRASH in $ripSym ($vecName$cr2)"
    $report.Add($header)
    $report.Add("  RIP   0x$($rip.ToString('X8').PadLeft(8,'0'))  $ripSym")
    foreach ($regName in @('callR','RBX','R12','R13','R14','R10','RDI','RSI','R15')) {
        if ($excLine -match "$regName=([0-9a-fA-F]+)") {
            $val = [Convert]::ToInt64($matches[1], 16)
            $sym = Resolve-Rip -Rip $val
            $hex = "0x$($val.ToString('X8').PadLeft(8,'0'))"
            $extra = ''
            if ($regName -eq 'R10') {
                $heapMB = [math]::Round(($val - 0x600000) / 1048576.0, 1)
                $extra = "  (heap @ $heapMB MB)"
            }
            if ($sym) { $report.Add("  $($regName.PadRight(5)) $hex  $sym$extra") }
            else { $report.Add("  $($regName.PadRight(5)) $hex$extra") }
        }
    }
    for ($i = 1; $i -lt $ExcLines.Count; $i++) {
        $sl = $ExcLines[$i]
        if ($sl -match 'S\[([0-9a-fA-F]+)\]=([0-9a-fA-F]+)') {
            $soff = $matches[1]; $sval = [Convert]::ToInt64($matches[2], 16)
            $ssym = Resolve-Rip -Rip $sval
            if ($ssym) {
                if ($i -eq 1) { $report.Add("  Stack trace (heuristic):") }
                $report.Add("    S[$soff] 0x$($sval.ToString('X8').PadLeft(8,'0'))  $ssym")
            }
        }
    }
    return $report
}

function Write-SweepLog {
    param([string]$Message)
    $logPath = $env:CODEX_SWEEP_LOG
    if ($logPath) {
        $ts = (Get-Date).ToString('HH:mm:ss.fff')
        Add-Content -Path $logPath -Value "$ts $Message" -Encoding UTF8
    }
}

function Normalize-TripleNewlines {
    param([byte[]]$Bytes)
    $out = [System.Collections.Generic.List[byte]]::new($Bytes.Length)
    $nlRun = 0
    foreach ($b in $Bytes) {
        if ($b -eq 10) {
            $nlRun++
            if ($nlRun -le 2) { $out.Add($b) }
        } else {
            $nlRun = 0
            $out.Add($b)
        }
    }
    return $out.ToArray()
}

# ── TCP socket helpers (for explorer server and legacy TCP plugs) ──

$script:UseCodexVm = Test-Path -PathType Leaf $script:CodexVmBin
$script:FallbackVmBin = $env:QEMU_BIN_WHPX
if (-not $script:FallbackVmBin) {
    foreach ($p in 'D:\Program Files\qemu\qemu-system-x86_64.exe','C:\Program Files\qemu\qemu-system-x86_64.exe') {
        if (Test-Path -PathType Leaf $p) { $script:FallbackVmBin = $p; break }
    }
}
$script:FallbackAccelFlags = @('-accel', 'whpx')
$script:AgentSlot = switch -Wildcard ((Split-Path -Leaf (Get-Location).Path)) { '*-nib' { 1 }; default { 0 } }
$script:PerSlot = 3700

function Get-VmPort {
    param([int]$Attempt = 0)
    $offset = ((Get-Random -Min 0 -Max 32768) + $Attempt * 991) % $script:PerSlot
    return 50200 + ($script:AgentSlot * $script:PerSlot + $offset) * 2
}
function Get-VmChardevData { param([int]$Port) "socket,id=ch0,host=127.0.0.1,port=$Port,server=on,wait=on,nodelay=on" }
function Get-VmChardevCtrl { param([int]$Port) "socket,id=ch1,host=127.0.0.1,port=$Port,server=on,wait=on,nodelay=on" }

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
            return [System.Text.Encoding]::UTF8.GetString($bytes).TrimEnd("`r")
        }
        $buf.Add($one[0])
    }
}

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
        for ($i = 0; $i -lt 50 -and -not $Process.HasExited; $i++) { Start-Sleep -Milliseconds 100 }
        if (-not $Process.HasExited) {
            try { Stop-Process -Id $Process.Id -Force -ErrorAction Stop } catch {}
        }
    }
}

function Start-VmRun {
    param(
        [string]$Kernel, [int]$ConnectTimeoutSec = 30,
        [int]$MemMB = 3072, [int]$PCore = 1, [string[]]$ExtraArgs = @()
    )
    if ($script:UseCodexVm) { return Start-CodexVmRun @PSBoundParameters }
    if (-not $script:FallbackVmBin) { Write-Host "No fallback VM"; return $null }
    $stdoutFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        $dataPort = Get-VmPort -Attempt $attempt
        $ctrlPort = $dataPort + 1
        $args = @($script:FallbackAccelFlags) + @(
            '-machine', 'kernel-irqchip=off', '-kernel', $Kernel,
            '-chardev', (Get-VmChardevData -Port $dataPort), '-chardev', (Get-VmChardevCtrl -Port $ctrlPort),
            '-serial', 'chardev:ch0', '-serial', 'chardev:ch1',
            '-device', 'isa-debug-exit,iobase=0xf4,iosize=0x04',
            '-netdev', 'user,id=net0', '-device', 'ne2k_isa,netdev=net0,irq=9,iobase=0x300,mac=52:54:00:12:34:56',
            '-display', 'none', '-no-reboot', '-m', "$MemMB"
        )
        if ($ExtraArgs.Count -gt 0) { $args += $ExtraArgs }
        $proc = Start-Process -FilePath $script:FallbackVmBin -ArgumentList $args -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) { continue }
        $conn = Connect-Vm -DataPort $dataPort -CtrlPort $ctrlPort -TimeoutSec $ConnectTimeoutSec
        if ($conn) { return @{ Process = $proc; Conn = $conn; StdoutFile = $stdoutFile; StderrFile = $stderrFile } }
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
        Start-Sleep -Milliseconds 500
    }
    Remove-Item -Force $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    return $null
}

function Start-CodexVmRun {
    param(
        [string]$Kernel, [int]$ConnectTimeoutSec = 30,
        [int]$MemMB = 3072, [int]$PCore = 1, [string[]]$ExtraArgs = @()
    )
    $stdoutFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $disk = $null
    foreach ($ea in $ExtraArgs) { if ($ea -match 'file=([^,]+)') { $disk = $Matches[1] } }
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
        Start-Sleep -Milliseconds 500
    }
    Remove-Item -Force $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    return $null
}
