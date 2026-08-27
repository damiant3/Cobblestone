# plug-run.ps1 -- Boot a plug CDX in a VM and send pre-compiled IR over TCP to produce output
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$IrInput,
    [Parameter(Mandatory=$true)]
    [string]$Out,
    [string]$PlugCdx = '',
    [int]$MemMB = 2048,
    [int]$TimeoutSec = 120,
    [int]$Port = 9100
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ((-not (Test-Path -PathType Leaf $IrInput))) {
    [Console]::Error.WriteLine("MISSING IR input: $IrInput")
    exit 2
}
if ((-not $PlugCdx)) {
    $PlugCdx = Join-Path $PSScriptRoot 'build-output\plug.cdx'
}
if ((-not (Test-Path -PathType Leaf $PlugCdx))) {
    [Console]::Error.WriteLine("MISSING plug CDX: $PlugCdx")
    exit 2
}


$irBytes = [System.IO.File]::ReadAllBytes($IrInput)
Write-Host "[plug-run] IR input: $($irBytes.Length) bytes"
Write-Host "[plug-run] Plug: $PlugCdx"


# The finally block reads $proc, and under Set-StrictMode an unset name THROWS.
# It was always assigned before anything could fail until the host became a
# choice; a missing VM binary now makes Start-Process throw first, and the
# StrictMode error about $proc buried the real cause.
# $stderrFile, $consoleFile and $listener are the same class one throw earlier:
# a port still held from the previous subject makes $listener.Start() throw
# before any of the three is assigned, and the finally's reference then masks
# the port error as "variable cannot be retrieved" (gate, 2026-08-27, three
# plugs reported "produced nothing" on their second subject).
$proc = $null
$stderrFile = $null
$consoleFile = $null
$listener = $null

try {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    $listener.Start()
    Write-Host "[plug-run] Listening on TCP $Port"


    $stderrFile = [System.IO.Path]::GetTempFileName()
    $consoleFile = [System.IO.Path]::GetTempFileName()
    # The host choice lives in Start-PlugVm (vm-config), not here: eighteen other
    # runners need the same selection and a copy in each is a copy to drift.
    $proc = Start-PlugVm -Kernel $PlugCdx -ConsoleFile $consoleFile -StderrFile $stderrFile -MemMB $MemMB
    Write-Host "[plug-run] VM PID $($proc.Id)"


    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    :wait_conn while ((-not $listener.Pending())) {
        if ($proc.HasExited) {
            $stderr = if (Test-Path $stderrFile) { Get-Content $stderrFile -Raw } else { '' }
            [Console]::Error.WriteLine("FAIL: VM exited before connecting. stderr: $stderr")
            exit 4
        }
        if ([DateTime]::UtcNow -gt $deadline) {
            [Console]::Error.WriteLine('FAIL: plug did not connect within 60s')
            exit 5
        }
        Start-Sleep -Milliseconds 100
    }
    $client = $listener.AcceptTcpClient()
    $stream = $client.GetStream()
    $listener.Stop()
    Write-Host '[plug-run] Connected'


    $msgLen = $irBytes.Length + 1
    $hdr = [BitConverter]::GetBytes([int]$msgLen)
    $stream.Write($hdr, 0, 4)
    $stream.WriteByte(1)
    $chunkSize = 4096
    $off = 0
    :send_loop while ($off -lt $irBytes.Length) {
        $n = [Math]::Min($chunkSize, $irBytes.Length - $off)
        $stream.Write($irBytes, $off, $n)
        $stream.Flush()
        $off += $n
        if ($off -lt $irBytes.Length) {
            Start-Sleep -Milliseconds 20
        }
    }
    Write-Host "[plug-run] Sent $($irBytes.Length) bytes"


    $stream.ReadTimeout = $TimeoutSec * 1000
    # A per-byte accumulate here cost 116.77 s for a 16 MB image against
    # 0.02 s for the bulk write, measured 2026-08-18 over the shipped shape.
    # The cost is the 16,777,216 interpreter iterations, not the transport.
    $allBytes = [System.IO.MemoryStream]::new(65536)
    $readBuf = [byte[]]::new(8192)
    $recvAborted = $false
    $recvError = ''
    try {
        :recv_loop while ($true) {
            $n = $stream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) {
                break
            }
            $allBytes.Write($readBuf, 0, $n)
        }
    } catch {
        # A read timeout and a connection reset are NOT a clean end of stream,
        # and this catch used to say they were: the partial buffer was written
        # out and the script reported OK. Record which path ended the loop so a
        # caller can tell a complete transfer from a truncated one.
        $recvAborted = $true
        $recvError = $_.Exception.Message
    }
    $client.Close()


    if ($allBytes.Length -eq 0) {
        if ((-not $proc.HasExited)) {
            $proc.WaitForExit(5000)
        }
        $stderr = if (Test-Path $stderrFile) { Get-Content $stderrFile -Raw } else { '' }
        $console = if (Test-Path $consoleFile) { Get-Content $consoleFile -Raw } else { '' }
        [Console]::Error.WriteLine("FAIL: plug produced no output. stderr: $stderr guest console: $console")
        exit 6
    }
    [System.IO.File]::WriteAllBytes($Out, $allBytes.ToArray())
    # codex-vm dumps its output ring to -output ON EXIT, so grepping before the
    # VM has gone reads a file the guest console has not reached yet. Measured
    # 2026-08-17: 1 byte before the wait, the guest's line only after it.
    if ((-not $proc.HasExited)) {
        $proc.WaitForExit(20000)
    }
    # codex-vm reports a dropped serial byte on STDERR, not on the guest
    # console it is dropping from. It runs first because a short console
    # makes every check below it read clean for the wrong reason.
    $dropHit = @()
    if (Test-Path $stderrFile) { $dropHit = @(Select-String -Path $stderrFile -Pattern 'output buffer growth failed') }
    if ($dropHit.Count -gt 0) {
        [Console]::Error.WriteLine("FAIL: codex-vm dropped guest serial bytes, so the captured console is SHORT and no verdict read from it can be trusted -- $($dropHit[0].Line.Trim())")
        exit 10
    }
    # A guest that dies mid-emission is never REFUSED, so it prints no
    # TRUNCATED line and closes cleanly enough that recvAborted stays false.
    # Its own death line is the only witness.
    $deathHit = @()
    if (Test-Path $consoleFile) { $deathHit = @(Select-String -Path $consoleFile -Pattern 'OUT OF MEMORY|!EXC=') }
    if ($deathHit.Count -gt 0) {
        [Console]::Error.WriteLine("FAIL: the plug guest died mid-run, so the output is a prefix and not an answer -- $($deathHit[0].Line.Trim()). Wrote $($allBytes.Length) bytes to $Out.")
        exit 9
    }
    $truncHit = @()
    if (Test-Path $consoleFile) { $truncHit = @(Select-String -Path $consoleFile -Pattern 'TRUNCATED sent=') }
    if ($truncHit.Count -gt 0) {
        [Console]::Error.WriteLine("FAIL: the plug could not send its whole output -- $($truncHit[0].Line.Trim())")
        exit 7
    }
    if ($recvAborted) {
        [Console]::Error.WriteLine("FAIL: the receive ended by exception, not by end of stream -- $recvError. Wrote $($allBytes.Length) bytes and cannot tell whether that is all of them.")
        exit 8
    }
    Write-Host "[plug-run] OK: $Out ($($allBytes.Length) bytes)"

} finally {
    if (($proc -and (-not $proc.HasExited))) {
        Stop-VmGraceful -ProcessId $proc.Id
    }
    if ($stderrFile) { Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue }
    if ($consoleFile) { Remove-Item -Force $consoleFile -ErrorAction SilentlyContinue }
    try { if ($listener) { $listener.Stop() } } catch {}
}
