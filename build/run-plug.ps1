# Run a single plug CDX. Boots the plug in a VM with NE2K NIC, listens on
# TCP 9100, sends the input as ONE FRAMED MESSAGE, and writes the framed
# reply's body to -Output.
#
# Usage: build/run-plug.ps1 -Plug <plug.cdx> -InFile <ir.cce> -Output <file>
#
# The input is expected to be CCE bytes, which is what
# `build/compile.ps1 -IrCce` writes. THE WIRE IS CCE AND IT DOES NOT TELL
# YOU: hand a plug ASCII and it parses garbage rather than complaining.
#
# Two things here were wrong for as long as this script existed, and
# neither announced itself:
#
#   1. The parameter was named `Input`. `$Input` is a PowerShell AUTOMATIC
#      variable and it wins inside the script body, so the parameter never
#      arrived and every invocation died on "MISSING input:" before
#      booting anything. Nothing in the tree called this script, which is
#      why that went unnoticed.
#
#   2. It sent raw bytes and half-closed the socket. The plugs speak the
#      framed protocol from `codex/os/net`: `net-io-recv-loop` waits for a
#      length-prefixed message and answers with one. A raw send leaves the
#      plug waiting, and it eventually prints FAIL and exits having
#      emitted nothing -- which reads exactly like a plug that crashed,
#      and cost a debugging round here.
#
# Frame format, matching `frame-encode` and `build/work-wire.ps1`:
#     le32(1 + length of body) ++ [tag] ++ body
# The plugs do not inspect the tag.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Plug,
    [Parameter(Mandatory=$true)] [string]$InFile,
    [Parameter(Mandatory=$true)] [string]$Output,
    [int]$MemMB = 4096,
    [int]$TimeoutSec = 120,
    [int]$Port = 0,
    [byte]$Tag = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vm-config.ps1')
. (Join-Path $PSScriptRoot 'plug-ports.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# The port is the plug's, not this script's. It used to default to 9100 for
# every plug, which is the constant build/plug-ports.ps1 exists to un-duplicate:
# the guest dials its own port in net-session-new and the NAT maps that straight
# through, so listening on anything else hangs the run with no diagnostic.
# Derived from the plug path rather than passed, so a caller cannot get it wrong
# by omission.
if ($Port -eq 0) {
    if ($Plug -match '[\\/]plugs[\\/]([^\\/]+)[\\/]') { $Port = Get-PlugPort $Matches[1] }
    else { throw "run-plug: cannot tell which plug '$Plug' is, so cannot derive its port. Pass -Port, or run it from codex/plugs/<name>/." }
}
Assert-PlugPortFree -Port $Port -Plug $Plug

if (-not (Test-Path -PathType Leaf $Plug)) {
    [Console]::Error.WriteLine("MISSING plug: $Plug"); exit 2
}
if (-not (Test-Path -PathType Leaf $InFile)) {
    [Console]::Error.WriteLine("MISSING input: $InFile"); exit 2
}

$inputBytes = [System.IO.File]::ReadAllBytes($InFile)
Write-Host "[plug] Input: $($inputBytes.Length) bytes"
Write-Host "[plug] Plug: $Plug"

# Start TCP listener on port 9100
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
Write-Host "[plug] Listening on TCP $Port"

# Boot the plug VM with NIC enabled
$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
$stderrFile = [System.IO.Path]::GetTempFileName()
$vmArgs = @('-kernel', $Plug, '-mem', $MemMB, '-headless')
$proc = Start-Process -FilePath $vmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
Write-Host "[plug] VM PID $($proc.Id)"

try {
    # Wait for TCP connection from plug
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while (-not $listener.Pending()) {
        if ($proc.HasExited) {
            $stderr = if (Test-Path $stderrFile) { Get-Content $stderrFile -Raw } else { "" }
            [Console]::Error.WriteLine("FAIL: VM exited before connecting. stderr: $stderr")
            exit 4
        }
        if ([DateTime]::UtcNow -gt $deadline) {
            [Console]::Error.WriteLine("FAIL: plug did not connect within 60s")
            exit 5
        }
        Start-Sleep -Milliseconds 100
    }
    $client = $listener.AcceptTcpClient()
    $stream = $client.GetStream()
    $listener.Stop()
    Write-Host "[plug] Connected"

    # Send the input as one frame. Do NOT half-close: the plug replies on
    # this same connection and closes it itself.
    $frame = [System.Collections.Generic.List[byte]]::new()
    $flen = 1 + $inputBytes.Length
    $frame.Add([byte]($flen -band 0xFF))
    $frame.Add([byte](($flen -shr 8) -band 0xFF))
    $frame.Add([byte](($flen -shr 16) -band 0xFF))
    $frame.Add([byte](($flen -shr 24) -band 0xFF))
    $frame.Add($Tag)
    $frame.AddRange($inputBytes)
    $frameBytes = $frame.ToArray()
    $stream.Write($frameBytes, 0, $frameBytes.Length)
    $stream.Flush()
    Write-Host "[plug] Sent one frame of $($frameBytes.Length) bytes (tag $Tag), waiting for the reply..."

    # Receive output
    $stream.ReadTimeout = $TimeoutSec * 1000
    $allBytes = [System.Collections.Generic.List[byte]]::new(65536)
    $readBuf = [byte[]]::new(8192)
    try {
        while ($true) {
            $n = $stream.Read($readBuf, 0, $readBuf.Length)
            if ($n -le 0) { break }
            for ($bi = 0; $bi -lt $n; $bi++) { $allBytes.Add($readBuf[$bi]) }
        }
    } catch [System.IO.IOException] {
        # Read timeout or connection reset -- normal end
    }

    $client.Close()

    if ($allBytes.Count -eq 0) {
        # Check serial output for diagnostics
        if (-not $proc.HasExited) { $proc.WaitForExit(5000) }
        $stderr = if (Test-Path $stderrFile) { Get-Content $stderrFile -Raw } else { "" }
        [Console]::Error.WriteLine("FAIL: plug produced no output. stderr: $stderr")
        exit 6
    }

    # THE REPLY IS NOT FRAMED, THOUGH THE REQUEST MUST BE. The plugs read
    # with `net-io-recv-loop`, which requires a length prefix, and answer
    # with `net-io-send-text`, which does not add one. Stripping five
    # bytes unconditionally therefore eats the first five characters of
    # the emitted source -- `from ` off a Python file, which still greps
    # and still looks plausible line by line. Only strip a header that is
    # actually there, and prove it is there by its own length field.
    $raw = $allBytes.ToArray()
    $payload = $raw
    if ($raw.Length -ge 5) {
        # Cast to [int] BEFORE shifting: `-shl` on a [byte] keeps the left
        # operand's width, so the bits shift off the end and the length
        # silently reads back as (n -band 0xFF).
        $bodyLen = ([int]$raw[0]) -bor (([int]$raw[1]) -shl 8) -bor (([int]$raw[2]) -shl 16) -bor (([int]$raw[3]) -shl 24)
        if ($bodyLen -eq ($raw.Length - 4)) {
            $payload = [byte[]]::new($raw.Length - 5)
            [Array]::Copy($raw, 5, $payload, 0, $payload.Length)
            Write-Host "[plug] Reply was framed (tag $($raw[4]))"
        }
    }

    [System.IO.File]::WriteAllBytes($Output, $payload)
    Write-Host "[plug] OK: $Output ($($payload.Length) bytes)"

} finally {
    if (-not $proc.HasExited) {
        Stop-VmGraceful -ProcessId $proc.Id
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
    try { $listener.Stop() } catch {}
}
