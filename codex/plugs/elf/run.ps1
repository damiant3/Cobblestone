# Run the ELF plug: send x86 output (binary protocol) and receive an ELF binary.
#
# Usage:
#   plugs/elf/run.ps1 -X86Input <file> -Out <file>
#
# The -X86Input file must contain the binary wire protocol:
#   [4B code-len] [4B data-len] [4B func-count]
#   [code bytes] [data bytes]
#   [func entries: 2B name-len + name + 4B offset each]
#
# Use extract-x86-output.ps1 to create this from a compiler build.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$X86Input,
    [Parameter(Mandatory=$true)] [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\elf-plug.cdx'
$LogDir   = Join-Path $PlugDir 'build-output'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/elf/build.ps1 first")
    exit 2
}
if (-not (Test-Path -PathType Leaf $X86Input)) {
    [Console]::Error.WriteLine("MISSING: $X86Input")
    exit 2
}

$inputBytes = [System.IO.File]::ReadAllBytes($X86Input)
Write-Host "[elf-run] Input: $($inputBytes.Length) bytes from $X86Input"

# ── Start TCP listener ──────────────────────────────────────────────
$plugPort = 9100
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()
Write-Host "[elf-run] Listening on port $plugPort"

# ── Boot plug CDX ───────────────────────────────────────────────────
$run = Start-VmRun -Kernel $PlugCdx -ConnectTimeoutSec 30 -MemMB 4096
if (-not $run) {
    $listener.Stop()
    [Console]::Error.WriteLine("FAIL: VM did not start")
    exit 4
}

try {
    $conn = $run.Conn
    if (-not (Read-VmReady -Conn $conn -TimeoutSec 30)) {
        [Console]::Error.WriteLine("READY not received within 30s")
        exit 4
    }

    # Accept TCP connection from plug
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not $listener.Pending()) {
        if ([DateTime]::UtcNow -gt $deadline) {
            [Console]::Error.WriteLine("FAIL: plug did not connect within 30s")
            exit 5
        }
        Start-Sleep -Milliseconds 50
    }
    $tcpClient = $listener.AcceptTcpClient()
    $tcpStream = $tcpClient.GetStream()
    $listener.Stop()
    Write-Host "[elf-run] Plug connected"

    # ── Send x86 output as framed message (tag=2) ───────────────────
    # Throttle to avoid NE2000 NIC ring buffer overflow on large payloads.
    $msgLen = $inputBytes.Length + 1
    $header = [BitConverter]::GetBytes([int]$msgLen)
    $tcpStream.Write($header, 0, 4)
    $tcpStream.WriteByte(2)
    $chunkSize = 4096
    $off = 0
    while ($off -lt $inputBytes.Length) {
        $n = [Math]::Min($chunkSize, $inputBytes.Length - $off)
        $tcpStream.Write($inputBytes, $off, $n)
        $tcpStream.Flush()
        $off += $n
        if ($off -lt $inputBytes.Length) { Start-Sleep -Milliseconds 50 }
    }
    Write-Host "[elf-run] Sent $($inputBytes.Length) bytes (tag=2)"

    # ── Receive ELF output ──────────────────────────────────────────
    $tcpStream.ReadTimeout = 600000
    $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
    $readBuf = [byte[]]::new(8192)
    try {
        while ($true) {
            $n = $tcpStream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
        }
    } catch {}
    [System.IO.File]::WriteAllBytes($Out, $allBytes.ToArray())
    Write-Host "[elf-run] OK: $Out ($($allBytes.Count) bytes)"

    $tcpClient.Close()

    # ── Drain serial ────────────────────────────────────────────────
    $serialDrain = ''
    $dataStream = $conn.Data.GetStream()
    $dataStream.ReadTimeout = 5000
    $sBuf = [byte[]]::new(4096)
    try {
        while ($true) {
            $sn = $dataStream.Read($sBuf, 0, $sBuf.Length)
            if ($sn -le 0) { break }
            $serialDrain += [System.Text.Encoding]::UTF8.GetString($sBuf, 0, $sn)
        }
    } catch {}
    if ($serialDrain.Length -gt 0) { Write-Host "[elf-run] Serial: $serialDrain" }
    exit 0
} finally {
    if ($listener.Server.IsBound) { try { $listener.Stop() } catch {} }
    if ($run) {
        Close-Vm -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
}
