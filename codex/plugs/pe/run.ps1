# Run the PE plug: send CDX bytes and receive a PE32+ UEFI binary.
#
# Usage:
#   plugs/pe/run.ps1 -CdxInput <file.cdx> -Out <file.efi> [-App] [-HeapPages <n>]
#
# -App selects UEFI application mode (with heap allocation).
# Default is UEFI kernel mode.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$CdxInput,
    [Parameter(Mandatory=$true)] [string]$Out,
    [switch]$App,
    [int]$HeapPages = 512
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\pe-plug.cdx'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/pe/build.ps1 first")
    exit 2
}
if (-not (Test-Path -PathType Leaf $CdxInput)) {
    [Console]::Error.WriteLine("MISSING: $CdxInput")
    exit 2
}

$cdxBytes = [System.IO.File]::ReadAllBytes($CdxInput)
Write-Host "[pe-run] Input: $($cdxBytes.Length) bytes from $CdxInput"

# Build payload: [mode byte] [heap-pages if app] [CDX bytes]
$payloadMs = [System.IO.MemoryStream]::new()
if ($App) {
    $payloadMs.WriteByte(1)
    $payloadMs.Write([BitConverter]::GetBytes([int]$HeapPages), 0, 4)
} else {
    $payloadMs.WriteByte(0)
}
$payloadMs.Write($cdxBytes, 0, $cdxBytes.Length)
$inputBytes = $payloadMs.ToArray()

# ── Start TCP listener ──────────────────────────────────────────────
$plugPort = 9100
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()
Write-Host "[pe-run] Listening on port $plugPort"

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
    Write-Host "[pe-run] Plug connected"

    # ── Send CDX as framed message (tag=4) ──────────────────────────
    $msgLen = $inputBytes.Length + 1
    $header = [BitConverter]::GetBytes([int]$msgLen)
    $tcpStream.Write($header, 0, 4)
    $tcpStream.WriteByte(4)
    $chunkSize = 4096
    $off = 0
    while ($off -lt $inputBytes.Length) {
        $n = [Math]::Min($chunkSize, $inputBytes.Length - $off)
        $tcpStream.Write($inputBytes, $off, $n)
        $tcpStream.Flush()
        $off += $n
        if ($off -lt $inputBytes.Length) { Start-Sleep -Milliseconds 50 }
    }
    Write-Host "[pe-run] Sent $($inputBytes.Length) bytes (tag=4, mode=$(if ($App) {'app'} else {'kernel'}))"

    # ── Receive PE output ───────────────────────────────────────────
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
    Write-Host "[pe-run] OK: $Out ($($allBytes.Count) bytes)"

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
    if ($serialDrain.Length -gt 0) { Write-Host "[pe-run] Serial: $serialDrain" }
    exit 0
} finally {
    if ($listener.Server.IsBound) { try { $listener.Stop() } catch {} }
    if ($run) {
        Write-Host "[pe-run] VM stderr:"
        Copy-Item $run.StderrFile "test-output/vm-stderr.txt" -Force -ErrorAction SilentlyContinue
        Get-Content $run.StderrFile -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $_" }
        Close-Vm -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
}
