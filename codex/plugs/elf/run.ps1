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

# -- Start TCP listener ----------------------------------------------
$plugPort = 9110
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()
Write-Host "[elf-run] Listening on port $plugPort"

# -- Boot plug CDX ---------------------------------------------------
$stderrFile = [System.IO.Path]::GetTempFileName()
$consoleFile = [System.IO.Path]::GetTempFileName()
    $proc = Start-PlugVm -Kernel $PlugCdx -ConsoleFile $consoleFile -StderrFile $stderrFile -MemMB 3072
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

    # -- Send x86 output as framed message (tag=2) -------------------
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

    # -- Receive ELF output ------------------------------------------
    $tcpStream.ReadTimeout = 600000
    # A per-byte accumulate here cost 116.77 s for a 16 MB artifact against
    # 0.02 s for the bulk write, measured 2026-08-18 over the shipped shape.
    # The cost is one interpreter iteration per byte, not the transport, so it
    # scales with the ARTIFACT and is invisible on a plug whose output is a few
    # KB. This plug's output is a whole ELF binary.
    $allBytes = [System.IO.MemoryStream]::new(65536)
    $readBuf = [byte[]]::new(8192)
    $recvAborted = $false
    $recvError = ''
    try {
        while ($true) {
            $n = $tcpStream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) { break }
            $allBytes.Write($readBuf, 0, $n)
        }
    } catch {
        # A read timeout and a connection reset are NOT a clean end of stream.
        # This was a bare catch {}, so a transfer that stopped part way through
        # was indistinguishable from one that finished: the partial buffer was
        # written out and the run reported OK.
        $recvAborted = $true
        $recvError = $_.Exception.Message
    }
    [System.IO.File]::WriteAllBytes($Out, $allBytes.ToArray())
    if ($recvAborted) {
        [Console]::Error.WriteLine("FAIL: the receive ended by exception, not by end of stream -- $recvError. Wrote $($allBytes.Length) bytes and cannot tell whether that is all of them.")
        exit 8
    }
    # codex-vm dumps its output ring to -output ON EXIT, so grepping before the
    # VM has gone reads a file the guest console has not reached yet.
    if ($proc -and -not $proc.HasExited) { $proc.WaitForExit(20000) }
    $truncHit = @()
    if (Test-Path $consoleFile) { $truncHit = @(Select-String -Path $consoleFile -Pattern 'TRUNCATED sent=') }
    if ($truncHit.Count -gt 0) {
        [Console]::Error.WriteLine("FAIL: the plug could not send its whole output -- $($truncHit[0].Line.Trim())")
        exit 7
    }    # The guest STATES the length it meant to send. Comparing it against what
    # arrived is the only check that can see a short transfer BOTH ENDS call
    # successful, which is how the img transit loss was found.
    $gBuilt = -1; $gSent = -1
    if (Test-Path $consoleFile) {
        $okHit = @(Select-String -Path $consoleFile -Pattern 'OK elf=(\d+).* sent=(\d+)')
        if ($okHit.Count -gt 0) {
            $m = [regex]::Match($okHit[0].Line, 'OK elf=(\d+).* sent=(\d+)')
            $gBuilt = [int64]$m.Groups[1].Value
            $gSent = [int64]$m.Groups[2].Value
        }
    }
    # NO WITNESS IS A FAILURE, NOT A REASON TO SKIP THE CHECK. Guarded on a -1
    # sentinel, an absent length meant no comparison rather than no confidence.
    # Both greps here test for something the guest SAYS, so a guest that faults
    # says neither. Measured on the img twin 2026-08-18: a faulted plug printed
    # a register dump and that harness reported OK on 2800 bytes.
    if ($gBuilt -lt 0) {
        [Console]::Error.WriteLine("FAIL: the plug never reported a length, so there is nothing to check $($allBytes.Length) received bytes against.")
        if (Test-Path $consoleFile) {
            $tail = @(Get-Content $consoleFile -Tail 5)
            foreach ($l in $tail) { [Console]::Error.WriteLine("  guest: $($l.Trim())") }
        }
        exit 10
    }
    Write-Host "[elf-run] guest built $gBuilt, guest sent $gSent, host received $($allBytes.Length)"
    if ($gSent -ne $gBuilt -or $allBytes.Length -ne $gBuilt) {
        [Console]::Error.WriteLine("FAIL: byte counts disagree -- guest built $gBuilt, guest sent $gSent, host received $($allBytes.Length).")
        exit 9
    }
    Write-Host "[elf-run] OK: $Out ($($allBytes.Length) bytes)"

    $tcpClient.Close()

    # -- Drain serial ------------------------------------------------
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
    Remove-Item -Force $consoleFile -ErrorAction SilentlyContinue
