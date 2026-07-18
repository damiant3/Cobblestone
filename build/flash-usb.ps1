# Flash a raw disk image to a USB stick -- reliably.
#
# This is the flasher docs/UsersHandbook.md refers to. Unlike tools/write-usb.ps1
# it does NOT call Clear-Disk (which races Windows disk management against the
# raw write), it forces a real device sync with FlushFileBuffers via
# Flush($true), and it verifies the ENTIRE image after writing -- not just the
# first 4 KB. These three things are the documented causes of "same image,
# same stick, boots sometimes" non-determinism.
#
# Must run elevated (raw \\.\PhysicalDrive access). Find the disk number with:
#   Get-Disk | Where-Object BusType -eq 'USB'
#
# Usage (from an elevated pwsh):
#   build/flash-usb.ps1 -Image build/boot/optiona.img -DiskNumber N
# Or launch elevated in one shot:
#   Start-Process pwsh -Verb RunAs -ArgumentList '-NoProfile','-File',
#     'D:\Projects\NewRepository-fester\build\flash-usb.ps1','-Image',
#     'D:\Projects\NewRepository-fester\build\boot\optiona.img','-DiskNumber','N'
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Image,
    [Parameter(Mandatory)] [int]$DiskNumber,
    # Directory of blob-<lba>.bin sector patches (build/boot/gpt-fixup.py) applied
    # after the image write: relocates the backup GPT to the disk's true last
    # sectors so firmware that validates its position (Dell) lists the stick.
    [string]$FixupDir = '',
    # Refit the GPT to the TARGET DISK at flash time: protective MBR spans the
    # reported disk, primary AlternateLBA/LastUsable point at the disk's last
    # sectors, and the backup array+header are written there. This is the
    # spec-correct on-disk state: firmware that validates the backup position
    # (Dell) will list the stick, and Windows GPT auto-repair - which rewrites
    # any disk whose AlternateLBA is not the disk's last sector ON EVERY
    # INSERTION - finds nothing to fix. Every patched sector is verified by
    # readback, so a stick that silently drops tail writes fails loudly here
    # instead of mysteriously at boot. Supersedes -FixupDir (no Python).
    [switch]$SpecFit,
    [switch]$Force
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -PathType Leaf $Image)) { throw "Image not found: $Image" }
$imgPath = (Resolve-Path $Image).Path

# --- Safety checks ---
$disk = Get-Disk -Number $DiskNumber
if ($disk.BusType -ne 'USB') {
    throw "Disk $DiskNumber is '$($disk.BusType)', not USB (FriendlyName='$($disk.FriendlyName)'). Refusing to write."
}
$imgBytes = [System.IO.File]::ReadAllBytes($imgPath)
Write-Host "Image : $imgPath"
Write-Host "Size  : $($imgBytes.Length) bytes ($([math]::Round($imgBytes.Length/1MB,2)) MB)"
Write-Host "Target: Disk $DiskNumber  $($disk.FriendlyName)  $([math]::Round($disk.Size/1GB,1)) GB"
if ($imgBytes.Length -gt $disk.Size) { throw "Image ($($imgBytes.Length)) exceeds disk ($($disk.Size))" }

if (-not $Force) {
    $ans = Read-Host "This ERASES disk $DiskNumber ($($disk.FriendlyName)). Type YES to proceed"
    if ($ans -ne 'YES') { Write-Host "Aborted."; return }
}

# Take the disk offline so Windows does not fight the raw write (no Clear-Disk).
try { Set-Disk -Number $DiskNumber -IsOffline $true -ErrorAction Stop } catch { Write-Host "  (could not offline disk: $_)" }
Start-Sleep -Milliseconds 300

$diskPath = "\\.\PhysicalDrive$DiskNumber"
# FileShare.ReadWrite avoids "access denied" on some configurations; WriteThrough
# plus an explicit Flush($true) forces the bytes to the device, not just the OS cache.
$fs = [System.IO.FileStream]::new($diskPath, [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::ReadWrite, 1048576,
    [System.IO.FileOptions]::WriteThrough)
try {
    Write-Host "Writing $($imgBytes.Length) bytes..."
    $chunk = 1048576
    for ($off = 0; $off -lt $imgBytes.Length; $off += $chunk) {
        $len = [math]::Min($chunk, $imgBytes.Length - $off)
        $fs.Write($imgBytes, $off, $len)
        if (($off / $chunk) % 8 -eq 0) { Write-Host ("  {0:P0}" -f ($off / $imgBytes.Length)) -NoNewline; Write-Host "`r" -NoNewline }
    }
    $fs.Flush($true)   # FlushFileBuffers -> sync to the physical device
    Write-Host "  100% written, synced.            "

    $blobs = @()
    if ($FixupDir -and (Test-Path $FixupDir)) {
        $blobs += @(Get-ChildItem $FixupDir -Filter 'blob-*.bin' | ForEach-Object {
            @{ Lba = [int64]($_.BaseName -replace 'blob-',''); Bytes = [System.IO.File]::ReadAllBytes($_.FullName) }
        })
    }
    if ($SpecFit) {
        if ([System.Text.Encoding]::ASCII.GetString($imgBytes, 512, 8) -ne 'EFI PART') { throw "SpecFit: image has no GPT header at LBA 1" }
        function Crc32($data, $off, $len) {
            [long]$crc = 0xFFFFFFFF
            for ($i = 0; $i -lt $len; $i++) {
                $crc = $crc -bxor $data[$off + $i]
                for ($j = 0; $j -lt 8; $j++) {
                    if ($crc -band 1) { $crc = (($crc -shr 1) -band 0x7FFFFFFF) -bxor 0xEDB88320 }
                    else { $crc = ($crc -shr 1) -band 0x7FFFFFFF }
                }
            }
            return ($crc -bxor 0xFFFFFFFF) -band 0xFFFFFFFF
        }
        $diskSectors = [int64]($disk.Size / 512)
        $last = $diskSectors - 1
        $arrLba = $last - 33
        $peCount = [BitConverter]::ToUInt32($imgBytes, 512 + 80)
        $peSize  = [BitConverter]::ToUInt32($imgBytes, 512 + 84)
        $arrLen  = [int]($peCount * $peSize)
        # protective MBR: 0xEE entry spans the reported disk
        $mbr = New-Object byte[] 512
        [Array]::Copy($imgBytes, 0, $mbr, 0, 512)
        [BitConverter]::GetBytes([uint32][Math]::Min([int64][uint32]::MaxValue, $last)).CopyTo($mbr, 0x1CA)
        # primary header: AlternateLBA -> disk last sector, LastUsable below the backup array
        $hdr = New-Object byte[] 512
        [Array]::Copy($imgBytes, 512, $hdr, 0, 512)
        [BitConverter]::GetBytes([uint64]$last).CopyTo($hdr, 32)
        [BitConverter]::GetBytes([uint64]($arrLba - 1)).CopyTo($hdr, 48)
        $hdr[16] = 0; $hdr[17] = 0; $hdr[18] = 0; $hdr[19] = 0
        [BitConverter]::GetBytes([int](Crc32 $hdr 0 92)).CopyTo($hdr, 16)
        # backup entry array (copy of primary's), padded to whole sectors
        $arr = New-Object byte[] ([int][Math]::Ceiling($arrLen / 512.0) * 512)
        [Array]::Copy($imgBytes, 1024, $arr, 0, $arrLen)
        # backup header at the disk's last sector
        $bak = New-Object byte[] 512
        [Array]::Copy($hdr, 0, $bak, 0, 512)
        [BitConverter]::GetBytes([uint64]$last).CopyTo($bak, 24)
        [BitConverter]::GetBytes([uint64]1).CopyTo($bak, 32)
        [BitConverter]::GetBytes([uint64]$arrLba).CopyTo($bak, 72)
        $bak[16] = 0; $bak[17] = 0; $bak[18] = 0; $bak[19] = 0
        [BitConverter]::GetBytes([int](Crc32 $bak 0 92)).CopyTo($bak, 16)
        $blobs += @(
            @{ Lba = [int64]0;       Bytes = $mbr },
            @{ Lba = [int64]1;       Bytes = $hdr },
            @{ Lba = [int64]$arrLba; Bytes = $arr },
            @{ Lba = [int64]$last;   Bytes = $bak }
        )
        Write-Host "  specfit: disk=$diskSectors sectors, backup header @ $last, entries @ $arrLba"
    }
    if ($blobs.Count -gt 0) {
        foreach ($b in $blobs) {
            $off = $b.Lba * 512
            if ($off + $b.Bytes.Length -gt $disk.Size) { throw "fixup blob-$($b.Lba) exceeds disk" }
            $fs.Seek($off, 'Begin') | Out-Null
            $fs.Write($b.Bytes, 0, $b.Bytes.Length)
            if ($off -lt $imgBytes.Length) {
                [Array]::Copy($b.Bytes, 0, $imgBytes, $off, [math]::Min($b.Bytes.Length, $imgBytes.Length - $off))
            }
            Write-Host "  fixup: wrote $($b.Bytes.Length) bytes at LBA $($b.Lba)"
        }
        $fs.Flush($true)
    }

    Write-Host "Verifying full image..."
    $fs.Seek(0, 'Begin') | Out-Null
    $rbuf = New-Object byte[] $chunk
    $bad = -1
    for ($off = 0; $off -lt $imgBytes.Length -and $bad -lt 0; $off += $chunk) {
        $len = [math]::Min($chunk, $imgBytes.Length - $off)
        $got = 0
        while ($got -lt $len) {
            $n = $fs.Read($rbuf, $got, $len - $got)
            if ($n -le 0) { break }
            $got += $n
        }
        for ($i = 0; $i -lt $len; $i++) {
            if ($rbuf[$i] -ne $imgBytes[$off + $i]) { $bad = $off + $i; break }
        }
    }
    if ($bad -ge 0) { throw "VERIFY FAILED at byte $bad (wrote $($imgBytes[$bad]), read back $($rbuf[$bad - $off]))" }
    Write-Host "Verified: all $($imgBytes.Length) bytes match."

    foreach ($b in $blobs) {
        $off = $b.Lba * 512
        $fs.Seek($off, 'Begin') | Out-Null
        $vb = New-Object byte[] $b.Bytes.Length
        $got = 0
        while ($got -lt $vb.Length) { $n = $fs.Read($vb, $got, $vb.Length - $got); if ($n -le 0) { break }; $got += $n }
        for ($i = 0; $i -lt $vb.Length; $i++) { if ($vb[$i] -ne $b.Bytes[$i]) { throw "FIXUP VERIFY FAILED blob-$($b.Lba) at +$i" } }
        Write-Host "  fixup: verified blob at LBA $($b.Lba)"
    }
}
finally {
    $fs.Close()
    try { Set-Disk -Number $DiskNumber -IsOffline $false -ErrorAction SilentlyContinue } catch {}
}
Write-Host "DONE. Eject the stick, then boot the target from USB (UEFI, CSM/Legacy off)."
