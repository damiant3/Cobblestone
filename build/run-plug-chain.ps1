# Run a chain of plug CDXs sequentially, piping output from each to the next.
# Usage:
#   build/run-plug-chain.ps1 -Input <file> -Output <file> -Plugs <cdx1>,<cdx2>,...
#
# Each plug VM boots, receives the previous stage's output over TCP (port 9100),
# sends its output back, and shuts down. The next plug then boots and receives
# that output as its input.
#
# The first plug receives the input file contents. The last plug's output is
# written to the output file.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Input,
    [Parameter(Mandatory=$true)] [string]$Output,
    [Parameter(Mandatory=$true)] [string[]]$Plugs,
    [int]$MemMB = 4096,
    [int]$TimeoutSec = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

if (-not (Test-Path -PathType Leaf $Input)) {
    [Console]::Error.WriteLine("MISSING: $Input")
    exit 2
}

foreach ($plug in $Plugs) {
    if (-not (Test-Path -PathType Leaf $plug)) {
        [Console]::Error.WriteLine("MISSING plug CDX: $plug")
        exit 2
    }
}

# Read initial input
$currentPayload = [System.IO.File]::ReadAllBytes($Input)
Write-Host "[chain] Input: $($currentPayload.Length) bytes from $Input"
Write-Host "[chain] Chain: $($Plugs -join ' -> ')"

$plugPort = 9100

for ($stage = 0; $stage -lt $Plugs.Length; $stage++) {
    $plugCdx = $Plugs[$stage]
    $plugName = [System.IO.Path]::GetFileNameWithoutExtension($plugCdx)
    Write-Host "[chain] Stage $($stage + 1)/$($Plugs.Length): $plugName ($($currentPayload.Length) bytes in)"

    # Start TCP listener
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
    $listener.Start()

    # Boot plug VM
    $run = Start-VmRun -Kernel $plugCdx -ConnectTimeoutSec 30 -MemMB $MemMB
    if (-not $run) {
        $listener.Stop()
        [Console]::Error.WriteLine("FAIL: VM did not start for $plugName")
        exit 4
    }

    try {
        $conn = $run.Conn
        if (-not (Read-VmReady -Conn $conn -TimeoutSec 30)) {
            [Console]::Error.WriteLine("FAIL: no READY from $plugName")
            exit 4
        }

        # Accept TCP connection from plug
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while (-not $listener.Pending()) {
            if ([DateTime]::UtcNow -gt $deadline) {
                [Console]::Error.WriteLine("FAIL: $plugName did not connect within 30s")
                exit 5
            }
            Start-Sleep -Milliseconds 50
        }
        $tcpClient = $listener.AcceptTcpClient()
        $tcpStream = $tcpClient.GetStream()
        $listener.Stop()
        Write-Host "[chain] $plugName connected"

        # Send payload as framed message (4-byte length + 1-byte tag + body)
        $tag = if ($stage -eq 0) { 1 } else { 2 }
        $msgLen = $currentPayload.Length + 1
        $header = [BitConverter]::GetBytes([int]$msgLen)
        $tcpStream.Write($header, 0, 4)
        $tcpStream.WriteByte($tag)
        $tcpStream.Write($currentPayload, 0, $currentPayload.Length)
        $tcpStream.Flush()
        Write-Host "[chain] Sent $($currentPayload.Length) bytes (tag=$tag)"

        # Receive output until plug closes connection
        $tcpStream.ReadTimeout = $TimeoutSec * 1000
        $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
        $readBuf = [byte[]]::new(8192)
        try {
            while ($true) {
                $n = $tcpStream.Read($readBuf, 0, $readBuf.Length)
                if ($n -le 0) { break }
                for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
            }
        } catch {}

        $currentPayload = $allBytes.ToArray()
        Write-Host "[chain] $plugName produced $($currentPayload.Length) bytes"

        $tcpClient.Close()

        # Drain serial for diagnostic output
        $serialDrain = ''
        $dataStream = $conn.Data.GetStream()
        $dataStream.ReadTimeout = 3000
        $sBuf = [byte[]]::new(4096)
        try {
            while ($true) {
                $sn = $dataStream.Read($sBuf, 0, $sBuf.Length)
                if ($sn -le 0) { break }
                $serialDrain += [System.Text.Encoding]::UTF8.GetString($sBuf, 0, $sn)
            }
        } catch {}
        if ($serialDrain.Length -gt 0) {
            Write-Host "[chain] $plugName serial: $($serialDrain.TrimEnd())"
        }
    } finally {
        if ($listener.Server.IsBound) { try { $listener.Stop() } catch {} }
        Close-Vm -Conn $run.Conn -Process $run.Process
        Remove-Item -Force $run.StdoutFile, $run.StderrFile -ErrorAction SilentlyContinue
    }
}

# Write final output
[System.IO.File]::WriteAllBytes($Output, $currentPayload)
Write-Host "[chain] OK: $Output ($($currentPayload.Length) bytes)"
exit 0
