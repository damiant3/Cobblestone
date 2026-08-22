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
#   build/flash-usb.ps1 -Image build/boot/diag.img -DiskNumber N -SpecFit
#   build/flash-usb.ps1 -Image <an image no bed has flown> -DiskNumber N -UnrehearsedAnyway
#
# ONLY A REHEARSED HASH FLIES, and that is the default since 2026-08-20 (red).
# It used to be -Rehearsed, opt-in, which meant the 2026-08-14 ruling in
# HardwareSitting's QUICKREF -- no flash without a same-bytes full-loop bed
# rehearsal -- was enforced only when the person flashing remembered to ask for
# it. The override is -UnrehearsedAnyway and it is deliberately NOT -Force:
# -Force answers the "type YES" prompt, and one switch must not carry both a
# convenience and a safety guarantee (red's ruling, L-BODY).
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
    # L-REHEARSE: the flash refuses an image whose SHA-256 is in no rehearsal
    # record. build/boot/diag-arm.ps1 appends a line for the exact bytes it ran
    # through EVERY arm in both beds; a partial run writes nothing. So the only
    # image that reaches a stick is one that completed its full mission in the
    # bed as those bytes. -RehearsalRecord names one list explicitly; by default
    # BOTH <image>.rehearsed beside the image and the diag ladder's record are
    # searched, because what matters is that the hash was recorded, not which
    # file recorded it, and diag-arm.ps1 writes build/boot/diag.rehearsed for
    # whatever image it rehearses. -ExpectHash pins the one hash the flight card
    # names, on top of the record check.
    #
    # RETAINED AND NOW A NO-OP. Every flight card in HardwareSitting quotes a
    # command line carrying -Rehearsed, and those must keep running; the switch
    # asks for what now always happens.
    [switch]$Rehearsed,
    # The override, and it is NOT -Force. -Force answers the "type YES" prompt;
    # this skips a safety check, and red's ruling is that one switch must not
    # carry both. It prints exactly what it is waiving.
    [switch]$UnrehearsedAnyway,
    [string]$RehearsalRecord = '',
    [string]$ExpectHash = '',
    [switch]$Force,
    # Transcript of the whole run. This is how a non-elevated caller (an agent
    # session) reads the result back out of the elevated window: pass -Log,
    # check the process exit code, read the file. It exists so nobody writes a
    # one-off wrapper script around this file again.
    [string]$Log = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Log) { Start-Transcript -Path $Log -Force | Out-Null }

if (-not (Test-Path -PathType Leaf $Image)) { throw "Image not found: $Image" }
$imgPath = (Resolve-Path $Image).Path

# --- Provenance checks, BEFORE any disk enumeration ---
#
# These ask only about the image, so they run first. Two reasons, and the second
# is the one that matters. A flash that is going to be refused for its bytes
# should be refused before it touches Get-Disk. And a guard that needs a USB
# stick present in order to run is a guard nobody can test: with this ordering
# the accept, refuse and waive arms are all exercisable with a bogus
# -DiskNumber, which is how they were proven when this became the default.
$imgHash = (Get-FileHash $imgPath -Algorithm SHA256).Hash
Write-Host "Image : $imgPath"
Write-Host "SHA256: $imgHash"
if ($ExpectHash -and ($ExpectHash.ToUpper() -ne $imgHash)) {
    throw "Image hash $imgHash is not the expected $($ExpectHash.ToUpper()). Refusing to flash: these are not the bytes the flight card names."
}
$rehearsalRecords = if ($RehearsalRecord) { @($RehearsalRecord) } else {
    @(
        (Join-Path (Split-Path $imgPath -Parent) ([System.IO.Path]::GetFileNameWithoutExtension($imgPath) + '.rehearsed')),
        (Join-Path $PSScriptRoot 'boot\diag.rehearsed')
    ) | Select-Object -Unique
}
$rehearsalHit = @()
foreach ($rec in $rehearsalRecords) {
    if (Test-Path -PathType Leaf $rec) { $rehearsalHit += @(Get-Content $rec | Where-Object { $_ -like "$imgHash *" }) }
}
if ($UnrehearsedAnyway) {
    Write-Host ''
    Write-Host 'WAIVED: -UnrehearsedAnyway is set, so the L-REHEARSE check did not run.'
    Write-Host "  These bytes ($imgHash) are not recorded as having completed a full"
    Write-Host '  mission in the bed. Boot-and-read green is not mission green; the'
    Write-Host '  failure modes live at the last write, and this stick may carry them.'
    Write-Host "  Records searched: $($rehearsalRecords -join ', ')"
    Write-Host ''
} elseif ($rehearsalHit.Count -eq 0) {
    throw @"
REFUSED: image hash $imgHash is in no rehearsal record.
  Searched: $($rehearsalRecords -join ', ')
  This image has not completed its full mission in the bed as these exact bytes
  (L-REHEARSE, and the 2026-08-14 ruling in HardwareSitting's QUICKREF).
  Rehearse it:  build/boot/diag-arm.ps1  with every arm and both beds. A -Only
  or -SkipOvmf run is a dev loop and deliberately records nothing.
  If you mean to fly unrehearsed bytes anyway, say so by name: -UnrehearsedAnyway.
  Do NOT reach for -Force; that answers the confirmation prompt and has nothing
  to do with this check.
"@
} else {
    Write-Host "Rehearsed: $($rehearsalHit[0])"
}

# --- Disk safety checks ---
$disk = Get-Disk -Number $DiskNumber
if ($disk.BusType -ne 'USB') {
    throw "Disk $DiskNumber is '$($disk.BusType)', not USB (FriendlyName='$($disk.FriendlyName)'). Refusing to write."
}
$imgBytes = [System.IO.File]::ReadAllBytes($imgPath)
Write-Host "Size  : $($imgBytes.Length) bytes ($([math]::Round($imgBytes.Length/1MB,2)) MB)"
Write-Host "Target: Disk $DiskNumber  $($disk.FriendlyName)  $([math]::Round($disk.Size/1GB,1)) GB"
if ($imgBytes.Length -gt $disk.Size) { throw "Image ($($imgBytes.Length)) exceeds disk ($($disk.Size))" }

if (-not $Force) {
    $ans = Read-Host "This ERASES disk $DiskNumber ($($disk.FriendlyName)). Type YES to proceed"
    if ($ans -ne 'YES') { Write-Host "Aborted."; return }
}

# Take the disk offline so Windows does not fight the raw write (no Clear-Disk).
try { Set-Disk -Number $DiskNumber -IsOffline $true -ErrorAction Stop } catch { Write-Host "  (could not offline disk: $_)" }

# LOCK AND DISMOUNT EVERY VOLUME ON THE TARGET, AND HOLD THE HANDLES.
#
# This is what Rufus does and what we were missing. Removable media refuses
# -IsOffline (above), and Dismount-Volume does not exist in this PowerShell, so
# until now we had NO exclusion at all: AutoPlay fired in the middle of a raw
# write and partmgr repaired the partition table underneath us, which is how
# the fixup readback below caught LBA 1 changing between write and verify.
#
# It became load-bearing the moment the image geometry was corrected. While our
# GPT was malformed Windows could not mount the ESP, so raw writes went through
# unopposed; a conforming table gets mounted, and the write then fails outright
# with "Access to the path is denied". Being denied is the honest version of
# what was previously happening silently.
#
# FSCTL_LOCK_VOLUME (0x00090018) then FSCTL_DISMOUNT_VOLUME (0x00090020). The
# handles stay open for the whole write and are closed in the finally block, so
# Windows cannot remount and inspect the disk while we are laying it down.
Add-Type -Namespace Win32 -Name Vol -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
public static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
    IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
[DllImport("kernel32.dll", SetLastError=true)]
public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer,
    uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);
[DllImport("kernel32.dll", SetLastError=true)]
public static extern bool CloseHandle(IntPtr hObject);
'@ -ErrorAction SilentlyContinue

# Locked by ACCESS PATH, not by drive letter. An EFI System Partition is
# mounted without a letter -- Get-Partition reports DriveLetter blank and
# AccessPaths \\?\Volume{GUID}\ -- so a letter-based loop finds nothing and
# silently locks nothing, which is exactly what the first version of this did.
# CreateFileW wants the volume path with the trailing backslash removed.
$volHandles = @()
foreach ($p in @(Get-Partition -DiskNumber $DiskNumber -ErrorAction SilentlyContinue)) {
    foreach ($ap in @($p.AccessPaths)) {
        if (-not $ap) { continue }
        $vpath = $ap.TrimEnd('\')
        if ($vpath -notmatch '^\\\\[\?\.]\\') { continue }
        # GENERIC_READ|GENERIC_WRITE cast explicitly: PowerShell parses
        # 0xC0000000 as a signed int and refuses to marshal it as UInt32.
        $vh = [Win32.Vol]::CreateFileW($vpath, ([uint32]3221225472), 3, [IntPtr]::Zero, 3, 0, [IntPtr]::Zero)
        if ($vh -eq [IntPtr]::new(-1)) { Write-Host "  (could not open $vpath)"; continue }
        $br = 0
        $lk = [Win32.Vol]::DeviceIoControl($vh, 0x00090018, [IntPtr]::Zero, 0, [IntPtr]::Zero, 0, [ref]$br, [IntPtr]::Zero)
        $dm = [Win32.Vol]::DeviceIoControl($vh, 0x00090020, [IntPtr]::Zero, 0, [IntPtr]::Zero, 0, [ref]$br, [IntPtr]::Zero)
        Write-Host "  part $($p.PartitionNumber) $vpath : locked=$lk dismounted=$dm"
        $volHandles += $vh
    }
}
if ($volHandles.Count -eq 0) { Write-Host "  (no volumes to lock on disk $DiskNumber)" }
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
        $peCount = [BitConverter]::ToUInt32($imgBytes, 512 + 80)
        $peSize  = [BitConverter]::ToUInt32($imgBytes, 512 + 84)
        # The backup entry array ends immediately below the backup header, so it
        # starts at last - (its size in sectors). This was hardcoded to
        # $last - 33, while build-img places its own backup array at
        # TotalSectors - 33, which is last - 32. The two writers disagreed by one
        # sector, so even a conforming image was laid down non-conformingly.
        #
        # Windows supplied the answer rather than the other way round: once the
        # image geometry was fixed it stopped mangling the table and made exactly
        # this one adjustment, moving the array from last-33 to last-32 and
        # LastUsableLBA with it. Deriving the position from the array size leaves
        # nothing for it to correct.
        $arrSectors = [int][Math]::Ceiling(($peCount * $peSize) / 512.0)
        $arrLba = $last - $arrSectors
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
        # A fresh disk GUID per flash. THIS DOES NOT FIX THE REINSERTION REWRITE
        # AND IT WAS SUPPOSED TO. Kept for forensics only; read on before
        # believing it does anything else.
        #
        # OsHardwareRoadmap's Loop B has carried "randomize the disk GUID at flash
        # time" as a pending patch since 2026-07-10, on the theory that Windows
        # partmgr caches its GPT repair ruling BY DISK GUID, so a ruling made
        # against one of our sticks applies to every stick we write, because
        # build-img stamps a deterministic GUID for image reproducibility.
        #
        # THE HAZARD BELOW IS FIXED AT THE CAUSE AND THE INSTRUCTION IS RETIRED,
        # exactly as the block near the end of this file is. It is kept because
        # it killed the disk-GUID theory with a control, and deleting it invites
        # that patch back. IT IS NOT LIVE ADVICE AND MUST NOT BE READ AS ANY.
        # build-img.ps1:208-215 now writes a CONFORMING table (128 entries, the
        # UEFI 16 KB minimum, FirstUsableLBA 34), so Windows has nothing to
        # normalise, and this script takes the disk offline and locks every
        # volume for the whole write, so the eject that triggered it is not
        # reachable. A conforming stick survives reinsertion unchanged, and
        # reinsertion and eject are NOT hazards (Damian, 2026-08-18;
        # HardwareSitting section 3). Measured again 2026-08-20: disk 2 dumped
        # before the fifth diag flight came back byte-identical to
        # diag4-returned-20260819.img across every insertion between the two,
        # which a live rewrite could not survive.
        #
        # MEASURED 2026-07-29, AND THE DISK-GUID THEORY IS WRONG. Two facts, in
        # order, both HISTORICAL:
        #
        # 1. -SpecFit does NOT stop Windows rewriting the GPT. A stick flashed and
        #    verified clean by this script's own readback, then ejected and
        #    reinserted once, came back with LBA 1 rewritten: PartitionEntryLBA
        #    moved from 2 to 2047, which holds 512 bytes of ZEROS, header CRC
        #    recomputed, array CRC left stale. The good array is orphaned at LBA
        #    2. The backup is repointed 60506078 -> 60506110 the same way. Both
        #    GPTs then fail validation, so firmware sees NO partitions: this is
        #    the "firmware never lists the stick" failure arriving silently after
        #    a successful flash. The claim that a conformant GPT means Windows
        #    "finds nothing to fix" is false.
        #
        # 2. The disk GUID is not the mechanism. With this randomisation in place
        #    the stick was flashed with GUID 6222f486-5f70-4c24-9007-62b2537617f6,
        #    a value partmgr had provably never seen, and ONE eject-and-reinsert
        #    produced byte-for-byte the same rewrite. The GUID on the medium was
        #    untouched afterwards, so nothing cached by GUID can explain it.
        #
        # What is left is that Windows normalises the entry-array POSITIONS to its
        # own convention: primary array immediately below FirstUsableLBA, backup
        # immediately below the backup header. Untested, and it is a hypothesis
        # rather than the next patch -- the candidate worth checking first is that
        # our header declares NumberOfPartitionEntries=2 where UEFI reserves a
        # 16 KB minimum, which would make our table non-conformant and invite the
        # repair. Do not implement that on this note alone; measure it.
        #
        # The operational answer AT THE TIME was flash, verify, PULL. That is
        # history now: the cause is fixed and the instruction is retired.
        #
        # Randomising here rather than in build-img keeps the .img byte-identical
        # so recorded digests still reproduce. The reason to keep three lines that
        # fixed nothing is that each written stick is now individually
        # identifiable, which is exactly what let the above be measured.
        $diskGuid = [Guid]::NewGuid()
        $diskGuid.ToByteArray().CopyTo($hdr, 56)
        Write-Host "  specfit: fresh disk GUID $diskGuid"
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

    # Read the blobs back through a SEPARATE, unbuffered handle. $fs carries a
    # 1 MB FileStream buffer, and on a raw device a buffered Read near the last
    # sector issues a 1 MB ReadFile that runs past the end of the medium: the
    # SpecFit blobs at the disk's tail then fail with "The drive cannot find the
    # sector requested" after writing and flushing perfectly well. That reads as
    # a bad stick during a sitting, which is the worst possible time for it.
    $vfs = [System.IO.FileStream]::new($diskPath, [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite, 512,
        [System.IO.FileOptions]::None)
    try {
        foreach ($b in $blobs) {
            $off = $b.Lba * 512
            $vfs.Seek($off, 'Begin') | Out-Null
            $vb = New-Object byte[] $b.Bytes.Length
            $got = 0
            while ($got -lt $vb.Length) { $n = $vfs.Read($vb, $got, $vb.Length - $got); if ($n -le 0) { break }; $got += $n }
            if ($got -ne $vb.Length) { throw "FIXUP VERIFY FAILED blob-$($b.Lba): read $got of $($vb.Length) bytes" }
            for ($i = 0; $i -lt $vb.Length; $i++) { if ($vb[$i] -ne $b.Bytes[$i]) { throw "FIXUP VERIFY FAILED blob-$($b.Lba) at +$i" } }
            Write-Host "  fixup: verified blob at LBA $($b.Lba)"
        }
    }
    finally { $vfs.Close() }
}
finally {
    $fs.Close()
    # Release the volume locks only after the physical write is closed, so
    # Windows cannot remount and inspect the disk mid-operation.
    foreach ($vh in $volHandles) { [void][Win32.Vol]::CloseHandle($vh) }
    try { Set-Disk -Number $DiskNumber -IsOffline $false -ErrorAction SilentlyContinue } catch {}
}
Write-Host ""
Write-Host "DONE. Pull the stick out when you are ready." -ForegroundColor Yellow
Write-Host "Then boot the target from USB (UEFI, CSM/Legacy off)."
Write-Host "Everything is already flushed and read back byte for byte, so there is"
Write-Host "nothing for Safely Remove to finish."
# On a throw the transcript is finalized by process exit instead; the log is
# written either way when this runs as its own pwsh -File process.
if ($Log) { Stop-Transcript | Out-Null }

# THE HAZARD BELOW IS FIXED AT THE CAUSE (2026-08-02) AND THE INSTRUCTION IS
# RETIRED. The disk is taken offline and every volume on it is locked and
# dismounted for the whole write (see the block above), so Explorer does not
# offer Eject on this stick at all and the action that destroyed the GPT is
# not reachable. Pull it when you are ready. The account is kept because the
# failure it describes was real, was measured with a control, and is what the
# offline-and-lock code exists to prevent -- delete the history and the next
# person re-derives it from a corrupted stick.
#
# WHY IT WAS SHOUTED, MEASURED 2026-07-29.
#
# This line used to read "Eject the stick, then boot the target", and following
# it destroys the GPT that the lines above just verified. Explorer's Eject
# re-enumerates the device, and on arrival Windows rewrites the partition table:
# PartitionEntryLBA moves from 2 to 2047, a sector of ZEROS, LastUsableLBA moves
# 60506077 -> 60506109, the backup array is repointed 60506078 -> 60506110, and
# the header CRCs are recomputed over the new values while the array CRC is left
# stale. Both GPTs then fail validation and Windows itself reports the disk as
# MBR. Firmware sees no partitions, which is the "firmware never lists the
# stick" failure in docs/Hardware/HardwareSitting.md section 3b.
#
# Isolated with a control rather than inferred. Same stick, one flash: three
# consecutive raw reads returned byte-identical, correct GPTs (EntryLBA=2,
# header CRC c25e02bc). The operator then used Explorer's Eject and nothing
# else -- the stick was never unplugged -- and the next read showed the rewrite.
# Reads are harmless; the eject is not.
#
# Two things it is NOT, both ruled out by measurement the same day: it is not
# physical reinsertion, and it is not the deterministic disk GUID that
# OsHardwareRoadmap blamed. A stick flashed with a freshly randomised GUID that
# partmgr had never seen was rewritten identically.
#
# This is a strong candidate for why attempt 1 did not boot on 2026-07-29. That
# stick was flashed, verified, and then ejected exactly as this script
# instructed, and "the firmware never listed it" is precisely what an invalid
# GPT produces. Not provable now -- that medium has been overwritten -- but the
# mechanism is proven and the procedure that triggers it was followed.
