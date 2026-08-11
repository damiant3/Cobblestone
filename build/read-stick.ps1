# Read files out of a returned stick's ESP by walking the FAT chain on the RAW
# DEVICE. Read-only: it opens the physical drive for reading and never writes.
#
# Why raw rather than a drive letter. Windows writes to a FAT volume it mounts
# -- System Volume Information, recycle-bin metadata -- before anyone has read a
# byte, and those writes ALLOCATE CLUSTERS. A sitting that asks "were clusters
# allocated to nothing" has its own evidence manufactured by the act of looking,
# and it is unrecoverable once done. So: read \\.\PhysicalDriveN directly.
#
# Requires elevation for raw device access. Pass -Log and read it back the way
# flash-usb.ps1 does.
#
#   build/read-stick.ps1 -DiskNumber 2 -OutDir build-output/stick -Log ...
#
# With no -Name it lists the root directory and extracts nothing.
#
# -ImageFile reads a .img instead of a device. That is how this script is
# CALIBRATED before it is pointed at evidence: run it against an image known to
# contain the file and against one known not to, and require it to say so. A
# reader first used on the returned stick has never been shown to work.
[CmdletBinding()]
param(
    [int]$DiskNumber = -1,
    [string]$ImageFile = '',
    # 8.3 names to extract, e.g. 'OUT.CDX','OUT.TXT'. Dotted form is fine.
    [string[]]$Name = @(),
    [string]$OutDir = '',
    [int]$PartStart = 2048,
    # Dump the first N bytes of the medium to a file, so the returned stick can
    # be diffed against the image that was flashed to it. That comparison is the
    # only thing that separates "the board wrote this" from "this was already on
    # the stick" when the expected output is byte-for-byte predictable.
    [string]$RawDump = '',
    [int64]$RawBytes = 16777216,
    [string]$Log = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Log) { Start-Transcript -Path $Log -Force | Out-Null }

if ($ImageFile) {
    if (-not (Test-Path -PathType Leaf $ImageFile)) { throw "Image not found: $ImageFile" }
    $path = (Resolve-Path $ImageFile).Path
    Write-Host "Reading IMAGE $path (READ ONLY)"
} elseif ($DiskNumber -ge 0) {
    $disk = Get-Disk -Number $DiskNumber
    if ($disk.BusType -ne 'USB') { throw "Disk $DiskNumber is '$($disk.BusType)', not USB. Refusing." }
    Write-Host "Reading Disk $DiskNumber  $($disk.FriendlyName)  $([math]::Round($disk.Size/1GB,1)) GB (READ ONLY)"
    $path = "\\.\PhysicalDrive$DiskNumber"
} else {
    throw "Give -DiskNumber or -ImageFile"
}
$fs = [System.IO.FileStream]::new($path, [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite, 512,
    [System.IO.FileOptions]::None)

# Raw device reads must be whole sectors and sector-aligned, so every read goes
# through here rather than seeking to an arbitrary byte.
function Read-Sectors($stream, [int64]$lba, [int]$count) {
    $buf = New-Object byte[] ($count * 512)
    $stream.Seek($lba * 512, 'Begin') | Out-Null
    $got = 0
    while ($got -lt $buf.Length) {
        $n = $stream.Read($buf, $got, $buf.Length - $got)
        if ($n -le 0) { break }
        $got += $n
    }
    if ($got -ne $buf.Length) { throw "short read at LBA $lba ($got of $($buf.Length))" }
    return $buf
}

try {
    if ($RawDump) {
        $chunk = 1048576
        $out = [System.IO.File]::Create($RawDump)
        try {
            $fs.Seek(0, 'Begin') | Out-Null
            $left = $RawBytes
            $buf = New-Object byte[] $chunk
            while ($left -gt 0) {
                $want = [int][Math]::Min([int64]$chunk, $left)
                $got = 0
                while ($got -lt $want) { $n = $fs.Read($buf, $got, $want - $got); if ($n -le 0) { break }; $got += $n }
                if ($got -le 0) { break }
                $out.Write($buf, 0, $got)
                $left -= $got
            }
        } finally { $out.Close() }
        Write-Host "raw dump: $RawBytes bytes -> $RawDump"
    }

    $bpb = Read-Sectors $fs $PartStart 1
    $bps   = [BitConverter]::ToUInt16($bpb, 11)
    $spc   = $bpb[13]
    $rsvd  = [BitConverter]::ToUInt16($bpb, 14)
    $nfat  = $bpb[16]
    $rootEnts = [BitConverter]::ToUInt16($bpb, 17)
    $fatSz = [BitConverter]::ToUInt16($bpb, 22)
    if ($bps -ne 512) { throw "unexpected bytes-per-sector $bps (is PartStart $PartStart right?)" }

    $fatStart  = $PartStart + $rsvd
    $rootStart = $fatStart + $nfat * $fatSz
    # [int] ROUNDS in PowerShell, so the usual (x + n - 1)/n ceil idiom returns
    # 33 where the answer is 32 and every cluster then reads one sector late --
    # the file comes back the right LENGTH with the wrong bytes, which looks
    # exactly like the writer being broken. Floor explicitly.
    $rootSecs  = [int][Math]::Floor(($rootEnts * 32 + $bps - 1) / $bps)
    $dataStart = $rootStart + $rootSecs
    Write-Host "bps=$bps spc=$spc rsvd=$rsvd nfat=$nfat fatSz=$fatSz rootEnts=$rootEnts"
    Write-Host "fat@$fatStart root@$rootStart data@$dataStart"

    $root = Read-Sectors $fs $rootStart $rootSecs
    $entries = @()
    for ($i = 0; $i -lt $rootEnts; $i++) {
        $off = $i * 32
        if ($root[$off] -eq 0) { break }
        if ($root[$off] -eq 0xE5) { continue }
        $attr = $root[$off + 11]
        if ($attr -eq 0x0F) { continue }           # long-name entry
        $raw = [Text.Encoding]::ASCII.GetString($root, $off, 11)
        $base = $raw.Substring(0,8).TrimEnd()
        $ext  = $raw.Substring(8,3).TrimEnd()
        $disp = if ($ext) { "$base.$ext" } else { $base }
        $entries += [pscustomobject]@{
            Name    = $disp
            Raw     = $raw
            Attr    = '0x{0:X2}' -f $attr
            Cluster = [BitConverter]::ToUInt16($root, $off + 26)
            Size    = [BitConverter]::ToUInt32($root, $off + 28)
        }
    }
    Write-Host ""
    Write-Host "Root directory:"
    $entries | Format-Table -AutoSize | Out-String | Write-Host

    if ($Name.Count -eq 0) { Write-Host "(no -Name given, nothing extracted)"; return }
    if (-not $OutDir) { throw "-OutDir is required when -Name is given" }
    New-Item -ItemType Directory -Force $OutDir | Out-Null

    # One FAT read for the whole table beats a read per link.
    $fat = Read-Sectors $fs $fatStart $fatSz

    foreach ($want in $Name) {
        $e = $entries | Where-Object { $_.Name -eq $want.ToUpper() } | Select-Object -First 1
        if (-not $e) { Write-Host "MISSING: $want"; continue }
        $outBytes = New-Object byte[] $e.Size
        $copied = 0
        $chain = 0
        $cluster = [int]$e.Cluster
        $seen = @{}
        while ($copied -lt $e.Size -and $cluster -ge 2 -and $cluster -lt 0xFFF8) {
            if ($seen.ContainsKey($cluster)) { Write-Host "  LOOP in chain at cluster $cluster"; break }
            $seen[$cluster] = $true
            $chain++
            $sec = $dataStart + ($cluster - 2) * $spc
            $n = [int][Math]::Min([int64]($e.Size - $copied), [int64]($bps * $spc))
            $cl = Read-Sectors $fs $sec $spc
            [Array]::Copy($cl, 0, $outBytes, $copied, $n)
            $copied += $n
            $cluster = [BitConverter]::ToUInt16($fat, $cluster * 2)
        }
        $dest = Join-Path $OutDir $want
        [IO.File]::WriteAllBytes($dest, $outBytes)
        $sha = (Get-FileHash $dest -Algorithm SHA256).Hash
        $status = if ($copied -eq $e.Size) { 'complete' } else { "SHORT: $copied of $($e.Size)" }
        Write-Host "$want -> $dest"
        Write-Host "  $status, $chain clusters, sha256=$sha"
    }
}
finally {
    $fs.Close()
    if ($Log) { Stop-Transcript | Out-Null }
}
