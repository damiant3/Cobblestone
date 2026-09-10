# Run the IMG plug: send PE + CDX bytes and receive a GPT disk image.
#
# Usage:
#   plugs/img/run.ps1 -PeInput <file.efi> -CdxInput <file.cdx> -Out <file.img> [-Fat16] [-Source <file>...] [-Bucketed]
#
# Default is FAT32. Pass -Fat16 for FAT16 with optional source embedding.
# -Source takes any number of files and each keeps its OWN name on the image,
# 8.3-folded. It used to take one file and write it as SOURCE.SRC, a literal in
# the writer, so no image in the tree could hold a directory of tests.
# -Bucketed spreads the sources over SRC0, SRC1, ... subdirectories instead of
# the root, which is what lifts the root's 509-entry bound and separates names
# that fold to one 8.3 spelling.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$PeInput,
    [Parameter(Mandatory=$true)] [string]$CdxInput,
    [Parameter(Mandatory=$true)] [string]$Out,
    [switch]$Fat16,
    [string[]]$Source = @(),
    # A FILE of source paths, one per line, added to whatever -Source names.
    # It is a file and not just the array because `pwsh -File` splits an array
    # into separate positional arguments, so only the first element ever binds
    # and the rest come back as "a positional parameter cannot be found";
    # build/bvt.ps1 carries the same note over -SubjectsFile. A caller with a
    # DIRECTORY of sources, which is the whole point of taking more than one,
    # reaches this script through -File.
    [string]$SourceList = '',
    # Place the sources in subdirectories instead of the root. The FAT16 root
    # holds 512 entries of which three are spoken for, and two names folding to
    # the same 8.3 spelling cannot both live in one directory; both bounds are
    # per-directory, so buckets lift both at once.
    [switch]$Bucketed,
    [int]$BucketSize = 256,
    # Write the image's own name mapping: one TSV row per source, DIR, the 8.3
    # name, and the path it came from. A caller reading verdicts back off the
    # image needs that mapping, and DERIVING it a second time on the caller's
    # side would agree with a shared mistake by construction (L-BOTHARMS).
    [string]$ManifestOut = '',
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

# The root directory is the bound and it is a real one: 512 entries, of which
# EFI, SEED and the volume label take three. Refuse rather than write the
# entries that fit, because a silently short image is a wrong answer that looks
# like a right one.
$rootEntryCount = 512
$maxSources = $rootEntryCount - 3

function ConvertTo-Name83([string]$path) {
    $leaf = [System.IO.Path]::GetFileNameWithoutExtension($path)
    $ext  = [System.IO.Path]::GetExtension($path).TrimStart('.')
    $clean = { param($s) (($s.ToUpperInvariant().ToCharArray() | ForEach-Object {
        if ($_ -match '[A-Z0-9_\-]') { $_ } else { '_' } }) -join '') }
    $n = (& $clean $leaf); $e = (& $clean $ext)
    if ($n.Length -gt 8) { $n = $n.Substring(0, 8) }
    if ($e.Length -gt 3) { $e = $e.Substring(0, 3) }
    return ($n.PadRight(8) + $e.PadRight(3))
}

$sourcePaths = @($Source)
if ($SourceList) {
    if (-not (Test-Path -PathType Leaf $SourceList)) { [Console]::Error.WriteLine("MISSING -SourceList: $SourceList"); exit 2 }
    $sourcePaths += @(Get-Content $SourceList | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
}

$sources = @()
foreach ($s in $sourcePaths) {
    if (-not (Test-Path -PathType Leaf $s)) { [Console]::Error.WriteLine("MISSING source: $s"); exit 2 }
    $sources += [pscustomobject]@{
        Path  = $s
        Name  = (ConvertTo-Name83 $s)
        Bytes = [System.IO.File]::ReadAllBytes($s)
    }
}
# $buckets is a list of directories, each an object with an 8.3 Name and the
# indices into $sources it holds. A flat image has none of them.
$buckets = @()
if ($Bucketed) {
    if ($BucketSize -lt 1) { [Console]::Error.WriteLine("REFUSED: -BucketSize must be at least 1."); exit 3 }
    # First fit: a source goes in the first bucket with room whose 8.3 name it
    # does not already claim, and a new bucket opens when no bucket takes it.
    # Collisions are therefore separated rather than refused.
    $bucketNames = @()   # index -> hashtable of 8.3 names held
    $bucketIdx = @()     # index -> list of source indices
    for ($i = 0; $i -lt $sources.Count; $i++) {
        $placed = -1
        for ($b = 0; $b -lt $bucketIdx.Count; $b++) {
            if ($bucketIdx[$b].Count -ge $BucketSize) { continue }
            if ($bucketNames[$b].ContainsKey($sources[$i].Name)) { continue }
            $placed = $b
            break
        }
        if ($placed -lt 0) {
            $bucketNames += ,(@{})
            $bucketIdx += ,([System.Collections.Generic.List[int]]::new())
            $placed = $bucketIdx.Count - 1
        }
        $bucketNames[$placed][$sources[$i].Name] = $true
        $bucketIdx[$placed].Add($i)
    }
    for ($b = 0; $b -lt $bucketIdx.Count; $b++) {
        $buckets += [pscustomobject]@{
            Name    = "SRC$b".PadRight(8) + '   '
            Indices = $bucketIdx[$b]
        }
    }
    if ($buckets.Count -gt $maxSources) {
        [Console]::Error.WriteLine("REFUSED: $($buckets.Count) subdirectories exceeds the FAT16 root directory's $maxSources usable entries. Raise -BucketSize.")
        exit 3
    }
} else {
    if ($sources.Count -gt $maxSources) {
        [Console]::Error.WriteLine("REFUSED: $($sources.Count) sources exceeds the FAT16 root directory's $maxSources usable entries. Pass -Bucketed to place them in subdirectories.")
        exit 3
    }
    # Two files folding to one 8.3 name would put two directory entries under the
    # same name and the reader would get whichever it found first. That is data
    # loss with no diagnostic, so it is named here.
    $dupes = @($sources | Group-Object Name | Where-Object { $_.Count -gt 1 })
    if ($dupes.Count -gt 0) {
        foreach ($d in $dupes) {
            [Console]::Error.WriteLine("REFUSED: 8.3 name '$($d.Name)' is claimed by $($d.Count) files: $(($d.Group.Path) -join ', ')")
        }
        [Console]::Error.WriteLine("Pass -Bucketed to place colliding names in separate subdirectories.")
        exit 4
    }
}

# The wire wants the sources grouped by directory, in directory order, so the
# order they are sent in is the bucket order rather than the caller's order.
if ($buckets.Count -gt 0) {
    $ordered = @()
    foreach ($bk in $buckets) { foreach ($ix in $bk.Indices) { $ordered += $sources[$ix] } }
    $sources = $ordered
}

# Build payload:
#   [fs-type(1)] [total-sectors(4)] [pe-size(4)] [cdx-size(4)] [src-count(4)]
#   [dir-count(4)] [ per directory: name83(11) file-count(4) ]
#   [ per source: name83(11) size(4) ] ... [pe][cdx][ each source's bytes ]
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)
$bw.Write([byte]$(if ($Fat16) { 1 } else { 0 }))
$bw.Write([int]$TotalSectors)
$bw.Write([int]$peBytes.Length)
$bw.Write([int]$cdxBytes.Length)
$bw.Write([int]$sources.Count)
$bw.Write([int]$buckets.Count)
foreach ($bk in $buckets) {
    $bw.Write([System.Text.Encoding]::ASCII.GetBytes($bk.Name))
    $bw.Write([int]$bk.Indices.Count)
}
foreach ($s in $sources) {
    $bw.Write([System.Text.Encoding]::ASCII.GetBytes($s.Name))
    $bw.Write([int]$s.Bytes.Length)
}
$bw.Write($peBytes)
$bw.Write($cdxBytes)
foreach ($s in $sources) { if ($s.Bytes.Length -gt 0) { $bw.Write($s.Bytes) } }
$bw.Flush()
$inputBytes = $ms.ToArray()

$srcTotal = 0
foreach ($s in $sources) { $srcTotal += $s.Bytes.Length }
Write-Host "[img-run] PE=$($peBytes.Length) CDX=$($cdxBytes.Length) sources=$($sources.Count) dirs=$($buckets.Count) srcBytes=$srcTotal sectors=$TotalSectors fs=$(if ($Fat16) {'FAT16'} else {'FAT32'})"
foreach ($bk in $buckets) { Write-Host "[img-run]   dir $($bk.Name.Trim()) holds $($bk.Indices.Count)" }
if ($ManifestOut) {
    # Written from the SAME arrays the payload is built from, after the bucket
    # reorder, so a row cannot disagree with what the image holds.
    $rows = @()
    if ($buckets.Count -gt 0) {
        $ix = 0
        foreach ($bk in $buckets) {
            foreach ($void in $bk.Indices) {
                $rows += ("{0}`t{1}`t{2}" -f $bk.Name.Trim(), $sources[$ix].Name, $sources[$ix].Path)
                $ix++
            }
        }
    } else {
        foreach ($s in $sources) { $rows += ("{0}`t{1}`t{2}" -f '', $s.Name, $s.Path) }
    }
    [System.IO.File]::WriteAllLines($ManifestOut, [string[]]$rows)
    Write-Host "[img-run] manifest: $ManifestOut ($($rows.Count) rows)"
}
if ($buckets.Count -eq 0) {
    foreach ($s in $sources) { Write-Host "[img-run]   $($s.Name) <- $($s.Path) ($($s.Bytes.Length) bytes)" }
}

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
