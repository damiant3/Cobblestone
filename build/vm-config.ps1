# vm-config.ps1 -- Shared VM config + helpers for the harness
# Generated from Codex Shell DSL. Do not edit by hand.
[CmdletBinding()]
param(
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Locate codex-vm binary
$script:CodexVmBin = Join-Path (Split-Path $PSScriptRoot) 'tools\codex-vm.exe'
if ((-not (Test-Path -PathType Leaf $script:CodexVmBin))) {
    [Console]::Error.WriteLine("codex-vm not found at $($script:CodexVmBin). Build with tools/build-vm.ps1.")
    throw "codex-vm not found at $($script:CodexVmBin). Build with tools/build-vm.ps1."
}


# CCE encode/decode tables -- shared by plug run scripts.
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
for ($i = 0; $i -lt 256; $i++) {
    $script:UnicodeToCce[$i] = 68
}
for ($i = 0; $i -lt $script:CceToUnicode.Length; $i++) {
    $u = $script:CceToUnicode[$i] % 256
    $script:UnicodeToCce[$u] = [byte]$i
}


# Symbol resolution.
#
# A CDX carries its own symbol map -- a MAP1 block the emitter writes into the
# data segment -- and that is the map these functions read. The text sidecar
# (seed/Codex.map) is a fallback and an explicit override, never the default.
#
# The reason is drift, and it is not hypothetical. The seed is built -Repl, and
# -Repl does not emit the text MAP: block that compile.ps1 captures into
# <out>.map, so NEITHER a seed rebuild NOR a copy-up ever refreshes
# seed/Codex.map. It is refreshed by one release-gate step that routine work does
# not run. So the sidecar silently describes an older binary than the seed beside
# it, and the resolver answered from it with total confidence: on 2026-07-16 it
# placed a crash in `sorted-builtin-names` when the faulting function was
# `find-effect-op-addr`, a different function entirely, and cost an hour. The
# sidecar's mtime was seven hours behind the seed's.
#
# An embedded map cannot drift from the binary it describes, because it ships
# inside it. That is the whole fix: not "remember to refresh the map", but a map
# with no opportunity to be stale.
#
# MAP1 layout (little-endian), as written by the emitter and read by
# apps/works/DevDebugger.codex:
#   +0  magic "MAP1" (le32 826361677)
#   +4  count
#   +8  string-table offset, always 12 + count*12 -- this is the validity check
#   +12 count x 12-byte entries: code-offset, code-size, name-offset
#   strings at (block + string-offset), NUL-terminated, ONE CCE BYTE PER CHAR.
# Names are CCE, not ASCII -- decode them through $script:CceToUnicode or every
# symbol comes back as plausible garbage ("III", "UU") rather than as an error.
# Entry code-offsets are relative to the 1 MB load address.

$script:MapCache = @{}
$script:Map1Cache = @{}

function Get-DefaultKernel { Join-Path (Split-Path $PSScriptRoot) 'seed\Codex.cdx' }

# Parse the MAP1 block out of a CDX. Returns $null when the file has none.
function Get-Map1Symbols {
    param([string]$CdxFile)
    if (-not $CdxFile -or -not (Test-Path -PathType Leaf $CdxFile)) { return $null }
    $key = (Resolve-Path $CdxFile).Path
    if ($script:Map1Cache.ContainsKey($key)) { return $script:Map1Cache[$key] }

    $b = [System.IO.File]::ReadAllBytes($key)
    $at = -1
    for ($i = 0; $i -lt $b.Length - 12; $i++) {
        # "MAP1"
        if ($b[$i] -eq 0x4D -and $b[$i+1] -eq 0x41 -and $b[$i+2] -eq 0x50 -and $b[$i+3] -eq 0x31) {
            $c = [BitConverter]::ToUInt32($b, $i + 4)
            $s = [BitConverter]::ToUInt32($b, $i + 8)
            if ($c -gt 0 -and $c -lt 100000 -and $s -eq (12 + $c * 12)) { $at = $i; break }
        }
    }
    if ($at -lt 0) { $script:Map1Cache[$key] = $null; return $null }

    $count   = [BitConverter]::ToUInt32($b, $at + 4)
    $strBase = $at + [BitConverter]::ToUInt32($b, $at + 8)
    $entries = [System.Collections.Generic.List[object]]::new()
    for ($e = 0; $e -lt $count; $e++) {
        $a  = $at + 12 + $e * 12
        $no = [BitConverter]::ToUInt32($b, $a + 8)
        $p  = $strBase + $no
        $sb = [System.Text.StringBuilder]::new()
        while ($p -lt $b.Length -and $b[$p] -ne 0) {
            $cce = [int]$b[$p]
            $u = if ($cce -lt $script:CceToUnicode.Length) { $script:CceToUnicode[$cce] } else { 63 }
            [void]$sb.Append([char]$u)
            $p++
        }
        $entries.Add(@{
            Addr = [long]0x100000 + [BitConverter]::ToUInt32($b, $a)
            Size = [int][BitConverter]::ToUInt32($b, $a + 4)
            Name = $sb.ToString()
        })
    }
    $script:Map1Cache[$key] = $entries
    return $entries
}

function Get-TextMapSymbols {
    param([string]$MapFile)
    if (-not (Test-Path $MapFile)) { return $null }
    if (-not $script:MapCache.ContainsKey($MapFile)) {
        $entries = [System.Collections.Generic.List[object]]::new()
        foreach ($line in [System.IO.File]::ReadAllLines($MapFile)) {
            if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
                $entries.Add(@{ Addr = [Convert]::ToInt64($matches[1], 16); Size = [int]$matches[2]; Name = $matches[3] })
            }
        }
        $script:MapCache[$MapFile] = $entries
    }
    return $script:MapCache[$MapFile]
}

# -MapFile forces the text sidecar. Otherwise the symbols come from -Kernel's
# embedded MAP1 (default: the seed), and the sidecar is used only if that CDX
# has no MAP1 -- in which case say so, because a stale answer is worse than none.
function Get-Symbols {
    param([string]$MapFile = '', [string]$Kernel = '')
    if ($MapFile) { return Get-TextMapSymbols $MapFile }
    if (-not $Kernel) { $Kernel = Get-DefaultKernel }
    $syms = Get-Map1Symbols $Kernel
    if ($syms) { return $syms }
    $fallback = Join-Path (Split-Path $PSScriptRoot) 'seed\Codex.map'
    if (Test-Path $fallback) {
        Write-Warning "no MAP1 in '$Kernel'; falling back to $fallback, which may describe a different binary."
        return Get-TextMapSymbols $fallback
    }
    return $null
}

function Resolve-Rip {
    param([long]$Rip, [string]$MapFile = '', [string]$Kernel = '')
    $syms = Get-Symbols -MapFile $MapFile -Kernel $Kernel
    if (-not $syms) { return $null }
    foreach ($e in $syms) {
        if ($Rip -ge $e.Addr -and $Rip -lt ($e.Addr + $e.Size)) {
            return "$($e.Name)+0x$((($Rip - $e.Addr)).ToString('X'))"
        }
    }
    return $null
}


function Resolve-Name {
    param([string]$Name, [string]$MapFile = '', [string]$Kernel = '')
    $syms = Get-Symbols -MapFile $MapFile -Kernel $Kernel
    if (-not $syms) { return 0 }
    foreach ($e in $syms) {
        if ($e.Name -eq $Name) { return [long]$e.Addr }
    }
    return 0
}


# -Kernel names the CDX that crashed, so its own embedded MAP1 resolves the
# addresses. Without it the symbols come from the seed, which is the right answer
# only when the seed is what was running.
function Format-CrashReport {
    param([string[]]$ExcLines, [string]$Kernel = '')
    $report = [System.Collections.Generic.List[string]]::new()
    if ($ExcLines.Count -eq 0) { return $report }
    $excLine = $ExcLines[0]
    $vec = ''
    if ($excLine -match '!EXC=([0-9a-fA-F]+)') { $vec = $matches[1] }
    $vecInt = [Convert]::ToInt32($vec, 16)
    $vecName = switch ($vecInt) { 0 { 'divide error' } 6 { 'invalid opcode' } 13 { 'general protection' } 14 { 'page fault' } default { "vector $vecInt" } }
    $rip = 0
    if ($excLine -match 'RIP=([0-9a-fA-F]+)') { $rip = [Convert]::ToInt64($matches[1], 16) }
    $ripSym = Resolve-Rip -Rip $rip -Kernel $Kernel
    if (-not $ripSym) { $ripSym = "0x$($rip.ToString('X8'))" }
    # CR2 is the faulting address of a PAGE FAULT and of nothing else. The guest
    # dumps the register on every vector, so on a #GP or a #UD it holds whatever
    # the last page fault left there -- a plausible heap address, pointing at
    # nothing to do with this crash. Printing it unconditionally is presenting
    # stale garbage as evidence, and it sent a 2026-07-16 investigation chasing an
    # address the faulting instruction never touched. Show it only for vector 14.
    $cr2 = ''
    if ($vecInt -eq 14 -and $excLine -match 'CR2=([0-9a-fA-F]+)') {
        $cr2val = [Convert]::ToInt64($matches[1], 16)
        if ($cr2val -ne 0) { $cr2 = ", CR2=0x$($cr2val.ToString('X12'))" }
    }
    if ($vecInt -eq 13) { $cr2 = ', non-canonical or segment-limit; CR2 is not the faulting address here' }
    $header = "CRASH in $ripSym ($vecName$cr2)"
    $report.Add($header)
    $report.Add("  RIP   0x$($rip.ToString('X8').PadLeft(8,'0'))  $ripSym")

    # callR and RBP are stack addresses, not code addresses. callR in particular
    # is the interrupted RSP despite its name, read from offset 64 of the
    # handler's stack. Resolving either to a symbol can only ever be the value
    # landing inside some function's byte range by coincidence, and printing
    # that coincidence is the same failure the CR2 guard above exists to stop.
    foreach ($regName in @('callR','RBP','RBX','R12','R13','R14','R10','RDI','RSI','R15')) {
        if ($excLine -match "$regName=([0-9a-fA-F]+)") {
            $val = [Convert]::ToInt64($matches[1], 16)
            $isStackPtr = $regName -eq 'callR' -or $regName -eq 'RBP'
            $sym = if ($isStackPtr) { $null } else { Resolve-Rip -Rip $val -Kernel $Kernel }
            $hex = "0x$($val.ToString('X8').PadLeft(8,'0'))"
            $extra = ''
            if ($regName -eq 'callR') { $extra = '  (interrupted RSP)' }
            if ($regName -eq 'RBP')   { $extra = '  (frame chain root)' }
            if ($regName -eq 'R10') {
                $heapMB = [math]::Round(($val - 0x600000) / 1048576.0, 1)
                $extra = "  (heap @ $heapMB MB)"
            }
            if ($sym) { $report.Add("  $($regName.PadRight(5)) $hex  $sym$extra") }
            else { $report.Add("  $($regName.PadRight(5)) $hex$extra") }
        }
    }
    # F[] lines are an exact walk of the RBP frame chain, emitted by the guest
    # (emit-exc-frame-walk). Every address is the return slot of a frame reached
    # by following the chain, so unlike the S[] scan below there is nothing to
    # filter: an entry that fails to resolve means the chain broke there, which
    # is a finding rather than noise, and saying so beats dropping it silently.
    $frames = [System.Collections.Generic.List[string]]::new()
    foreach ($sl in $ExcLines) {
        if ($sl -match 'F\[([0-9a-fA-F]+)\]=([0-9a-fA-F]+)') {
            $fidx = [Convert]::ToInt32($matches[1], 16)
            $fval = [Convert]::ToInt64($matches[2], 16)
            $fsym = Resolve-Rip -Rip $fval -Kernel $Kernel
            if (-not $fsym) { $fsym = '<unresolved -- frame chain broke here>' }
            $frames.Add("    #$fidx 0x$($fval.ToString('X8').PadLeft(8,'0'))  $fsym")
        }
    }
    if ($frames.Count -gt 0) {
        $report.Add("  Stack trace (frame chain):")
        foreach ($f in $frames) { $report.Add($f) }
        return $report
    }
    # No frame chain in this dump, so fall back to the old scan. It prints any
    # stack qword that lands inside some function's byte range, which a stale
    # local or a plain integer can do by coincidence. Hence the label.
    for ($i = 1; $i -lt $ExcLines.Count; $i++) {
        $sl = $ExcLines[$i]
        if ($sl -match 'S\[([0-9a-fA-F]+)\]=([0-9a-fA-F]+)') {
            $soff = $matches[1]; $sval = [Convert]::ToInt64($matches[2], 16)
            $ssym = Resolve-Rip -Rip $sval -Kernel $Kernel
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


# TCP socket helpers (for explorer server and legacy TCP plugs)
$script:UseCodexVm = Test-Path -PathType Leaf $script:CodexVmBin
$script:FallbackVmBin = $env:QEMU_BIN_WHPX
if ((-not $script:FallbackVmBin)) {
    foreach ($p in @("D:\Program Files\qemu\qemu-system-x86_64.exe", "C:\Program Files\qemu\qemu-system-x86_64.exe")) {
        if (Test-Path -PathType Leaf $p) {
            $script:FallbackVmBin = $p; break
        }
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


function Stop-VmGraceful {
    param([int]$ProcessId, [int]$TimeoutMs = 5000)
    $evtName = "Global\CodexVmShutdown_$ProcessId"
    $evt = $null
    try { $evt = [System.Threading.EventWaitHandle]::OpenExisting($evtName) } catch {}
    if ($evt) {
        $evt.Set() | Out-Null
        $evt.Dispose()
        $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($proc -and -not $proc.HasExited) { $proc.WaitForExit($TimeoutMs) | Out-Null }
        $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($proc -and -not $proc.HasExited) {
            try { Stop-Process -Id $ProcessId -Force -ErrorAction Stop } catch {}
        }
    } else {
        try { Stop-Process -Id $ProcessId -Force -ErrorAction Stop } catch {}
    }
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
            Stop-VmGraceful -ProcessId $Process.Id
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
        Stop-VmGraceful -ProcessId $proc.Id -TimeoutMs 3000
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
        Stop-VmGraceful -ProcessId $proc.Id -TimeoutMs 3000
        Start-Sleep -Milliseconds 500
    }
    Remove-Item -Force $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    return $null
}



