# Run the IMG plug: send PE + CDX bytes and receive a GPT disk image.
#
# Usage:
#   plugs/img/run.ps1 -PeInput <file.efi> -CdxInput <file.cdx> -Out <file.img> [-Fat16] [-Source <file>]
#
# Default is FAT32. Pass -Fat16 for FAT16 with optional source embedding.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$PeInput,
    [Parameter(Mandatory=$true)] [string]$CdxInput,
    [Parameter(Mandatory=$true)] [string]$Out,
    [switch]$Fat16,
    [string]$Source = '',
    [int]$TotalSectors = 16384
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\img-plug.cdx'

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/img/build.ps1 first")
    exit 2
}
foreach ($f in @($PeInput, $CdxInput)) {
    if (-not (Test-Path -PathType Leaf $f)) { [Console]::Error.WriteLine("MISSING: $f"); exit 2 }
}

$peBytes = [System.IO.File]::ReadAllBytes($PeInput)
$cdxBytes = [System.IO.File]::ReadAllBytes($CdxInput)
if ($Source -and (Test-Path -PathType Leaf $Source)) {
    [byte[]]$srcBytes = [System.IO.File]::ReadAllBytes($Source)
} else {
    [byte[]]$srcBytes = [byte[]]::new(0)
}

# Build payload: [fs-type(1)] [total-sectors(4)] [pe-size(4)] [cdx-size(4)] [src-size(4)] [pe][cdx][src]
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)
$bw.Write([byte]$(if ($Fat16) { 1 } else { 0 }))
$bw.Write([int]$TotalSectors)
$bw.Write([int]$peBytes.Length)
$bw.Write([int]$cdxBytes.Length)
$bw.Write([int]$srcBytes.Length)
$bw.Write($peBytes)
$bw.Write($cdxBytes)
if ($srcBytes.Length -gt 0) { $bw.Write($srcBytes) }
$bw.Flush()
$inputBytes = $ms.ToArray()

Write-Host "[img-run] PE=$($peBytes.Length) CDX=$($cdxBytes.Length) src=$($srcBytes.Length) sectors=$TotalSectors fs=$(if ($Fat16) {'FAT16'} else {'FAT32'})"

# -- Start TCP listener ----------------------------------------------
$plugPort = 9118
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $plugPort)
$listener.Start()

# -- Boot plug CDX ---------------------------------------------------
$stderrFile = [System.IO.Path]::GetTempFileName()
$consoleFile = [System.IO.Path]::GetTempFileName()
try {
    $proc = Start-PlugVm -Kernel $PlugCdx -ConsoleFile $consoleFile -StderrFile $stderrFile -MemMB 3072
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
    Write-Host "[img-run] Plug connected"

    # -- Send payload as framed message (tag=5) ----------------------
    $msgLen = $inputBytes.Length + 1
    $header = [BitConverter]::GetBytes([int]$msgLen)
    $tcpStream.Write($header, 0, 4)
    $tcpStream.WriteByte(5)
    $chunkSize = 4096
    $off = 0
    while ($off -lt $inputBytes.Length) {
        $n = [Math]::Min($chunkSize, $inputBytes.Length - $off)
        $tcpStream.Write($inputBytes, $off, $n)
        $tcpStream.Flush()
        $off += $n
        if ($off -lt $inputBytes.Length) { Start-Sleep -Milliseconds 50 }
    }
    Write-Host "[img-run] Sent $($inputBytes.Length) bytes (tag=5)"

    # -- Receive IMG output ------------------------------------------
    $tcpStream.ReadTimeout = 600000
    # A per-byte accumulate here cost 116.77 s for a 16 MB artifact against
    # 0.02 s for the bulk write, measured 2026-08-18 over the shipped shape.
    # The cost is one interpreter iteration per byte, not the transport, so it
    # scales with the ARTIFACT, and this plug's artifact is the whole disk
    # image: 8,388,608 bytes at the default sector count.
    $allBytes = [System.IO.MemoryStream]::new(1048576)
    $readBuf = [byte[]]::new(65536)
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
        # This was a bare catch {}, so a 16 MB image that stopped arriving
        # half way through was indistinguishable from one that all arrived:
        # the partial buffer was written out and the run reported OK.
        $recvAborted = $true
        $recvError = $_.Exception.Message
    }
    [System.IO.File]::WriteAllBytes($Out, $allBytes.ToArray())
    $tcpClient.Close()

    # The guest says TRUNCATED sent= on a refused send and then closes cleanly,
    # so the read loop above ends normally and the image is short with nothing
    # to show for it. codex-vm dumps its output ring to -output ON EXIT, so the
    # wait is load-bearing: without it this greps a file the console has not
    # reached yet.
    if ($proc -and -not $proc.HasExited) { $proc.WaitForExit(20000) }
    $truncHit = @()
    if (Test-Path $consoleFile) { $truncHit = @(Select-String -Path $consoleFile -Pattern 'TRUNCATED sent=') }
    if ($truncHit.Count -gt 0) {
        [Console]::Error.WriteLine("FAIL: the plug could not send the whole image -- $($truncHit[0].Line.Trim())")
        exit 7
    }
    if ($recvAborted) {
        [Console]::Error.WriteLine("FAIL: the receive ended by exception, not by end of stream -- $recvError. Wrote $($allBytes.Length) bytes and cannot tell whether that is all of them.")
        exit 8
    }

    # The guest STATES the length it meant to send, and this harness already
    # captures the console for the grep above, so the one number that can
    # refuse a silent truncation was being printed and thrown away. Measured
    # 2026-08-17: two runs in four came back short with a clean EOF, the
    # guest reporting OK, and nothing here able to notice.
    $guestImg = -1
    $guestSent = -1
    if (Test-Path $consoleFile) {
        $okHit = @(Select-String -Path $consoleFile -Pattern 'OK img=(\d+) sent=(\d+)')
        if ($okHit.Count -gt 0) {
            $m = [regex]::Match($okHit[0].Line, 'OK img=(\d+) sent=(\d+)')
            $guestImg = [int64]$m.Groups[1].Value
            $guestSent = [int64]$m.Groups[2].Value
        }
    }
    # NO WITNESS IS A FAILURE, NOT A REASON TO SKIP THE CHECK. Both greps above
    # test for something the guest SAYS -- 'OK img=' or 'TRUNCATED sent=' -- and
    # the comparison was guarded on a -1 sentinel, so a guest that says neither
    # meant no comparison rather than no confidence. Measured 2026-08-18: a
    # 5.5 MB payload into 16384 sectors FAULTED the plug, which printed a
    # register dump and no line either grep matches, and this harness wrote the
    # 2800 bytes of handshake it had and reported '[img-run] OK'. The plug also
    # has a bare 'FAIL' branch for a message that never assembles, which no
    # grep here matches either; this arm covers both without guessing which.
    if ($guestImg -lt 0) {
        [Console]::Error.WriteLine("FAIL: the plug never reported a length, so there is nothing to check $($allBytes.Length) received bytes against.")
        if (Test-Path $consoleFile) {
            $tail = @(Get-Content $consoleFile -Tail 5)
            foreach ($l in $tail) { [Console]::Error.WriteLine("  guest: $($l.Trim())") }
        }
        exit 10
    }
    Write-Host "[img-run] guest built $guestImg, guest sent $guestSent, host received $($allBytes.Length)"
    if ($guestSent -ne $guestImg -or $allBytes.Length -ne $guestImg) {
        [Console]::Error.WriteLine("FAIL: byte counts disagree -- guest built $guestImg, guest sent $guestSent, host received $($allBytes.Length).")
        exit 9
    }
    Write-Host "[img-run] OK: $Out ($($allBytes.Length) bytes)"

} finally {
    if ($proc -and -not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
    }
    Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
    Remove-Item -Force $consoleFile -ErrorAction SilentlyContinue
}
