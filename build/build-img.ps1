# Build a bootable GPT FAT16 disk image from a PE and optional source.
#
# Layout:
#   Sector 0:     Protective MBR
#   Sector 1:     GPT header
#   Sectors 2-33: GPT entries (2 partitions)
#   Sector 2048+: FAT ESP
#     EFI/BOOT/BOOTX64.EFI  (the PE)
#     SOURCE.SRC             (compiler source, if provided)
#   Top of medium: the Codex fact store partition
#   Last 33 sectors: backup GPT (entry array, then header AT the last sector)
#
# The second partition is why a booted stick can remember anything. DiskFacts
# addresses its sectors relative to a partition of type
# C0DE1A11-FAC7-4C0D-9E75-C0DEC0DE5EED, and on a disk that carries a partition
# table and no such partition it refuses to write at all -- because writing
# where it used to write ate the GPT. An image with one partition therefore
# produces a stick whose dev console reports "0 disk facts" forever.
#
# Usage: build-img.ps1 -PeInput <file.efi> -Out <file.img> [-Source <file>] [-TotalSectors 16384]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$PeInput,
    [Parameter(Mandatory=$true)] [string]$Out,
    [string]$Source = '',
    # A TrueType font, written to the ESP root as CMUNSS.TTF. The desktop
    # loads it through its own FAT driver post-EBS (H4c).
    [string]$Font = '',
    # The CDX seed, written to the ESP root as CODEX.CDX. The booted payload
    # reads it back with its own drivers and verifies it (WakeCeremony).
    [string]$Seed = '',
    # A bundled agent: the GGUF model and its signed manifest, written to the
    # ESP root as AGENT.GGU and AGENT.MAN. Produced by
    # `build/make-agent-bundle.ps1`; verified in the guest by
    # `apps/works/AgentBundle.codex`. Both or neither -- a model with no
    # manifest is unverifiable and a manifest with no model names nothing.
    [string]$Agent = '',
    [string]$AgentManifest = '',
    [int]$TotalSectors = 16384,
    # Format the ESP as FAT32 instead of FAT16 -- the layout every vendor
    # stick and every volume past 2 GB actually carries. A REAL FAT32 volume
    # needs >= 65525 clusters (UEFI classifies by cluster count, the CL 7289
    # lesson), so -TotalSectors must be >= ~70000 (34 MB).
    [switch]$Fat32
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR at line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$SectorSize = 512
$PartStart  = 2048
$LastUsable = $TotalSectors - 34            # backup entry array starts at -33

# The fact store takes an eighth of the medium off the top, capped at 128 MB
# and floored at 1 MB; the ESP takes the rest. These three numbers are the
# same policy codex/plugs/img/GptWriter.codex applies, and the two writers
# must agree -- a stick built by one and read by a guest that believes the
# other finds its store in the wrong place.
$FactsSectors = [Math]::Min(262144, [Math]::Max(2048, [int]($TotalSectors / 8)))
$FactsEnd     = $LastUsable
$FactsStart   = $FactsEnd - $FactsSectors + 1
$PartSectors  = $FactsStart - $PartStart    # the ESP, stopping below the store
$ImageSize    = $TotalSectors * $SectorSize

if ($PartSectors -le 0) {
    throw "-TotalSectors $TotalSectors leaves no room for an ESP once the $FactsSectors-sector fact store is reserved"
}

$pe = [System.IO.File]::ReadAllBytes($PeInput)
if ($Source -and (Test-Path $Source)) { $srcBytes = [System.IO.File]::ReadAllBytes($Source) } else { $srcBytes = [byte[]]::new(0) }
if ($Seed -and (Test-Path $Seed)) { $seedBytes = [System.IO.File]::ReadAllBytes($Seed) } else { $seedBytes = [byte[]]::new(0) }
if ($Font -and (Test-Path $Font)) { $fontBytes = [System.IO.File]::ReadAllBytes($Font) } else { $fontBytes = [byte[]]::new(0) }
if ($Agent -and (Test-Path $Agent)) { $agentBytes = [System.IO.File]::ReadAllBytes($Agent) } else { $agentBytes = [byte[]]::new(0) }
if ($AgentManifest -and (Test-Path $AgentManifest)) { $manBytes = [System.IO.File]::ReadAllBytes($AgentManifest) } else { $manBytes = [byte[]]::new(0) }
if (($agentBytes.Length -gt 0) -ne ($manBytes.Length -gt 0)) {
    throw "-Agent and -AgentManifest go together: a model with no manifest cannot be verified, and a manifest with no model names nothing."
}

Write-Host "[build-img] PE=$($pe.Length) bytes  Source=$($srcBytes.Length) bytes  Seed=$($seedBytes.Length) bytes  Agent=$($agentBytes.Length) bytes  Image=$($ImageSize / 1MB) MB"
Write-Host "[build-img] ESP LBA $PartStart..$($PartStart + $PartSectors - 1) ($([math]::Round($PartSectors * $SectorSize / 1MB, 1)) MB)  facts LBA $FactsStart..$FactsEnd ($([math]::Round($FactsSectors * $SectorSize / 1MB, 1)) MB)"
$payloadBytes = $pe.Length + $srcBytes.Length + $seedBytes.Length + $agentBytes.Length + $manBytes.Length
if ($payloadBytes -gt ($PartSectors * $SectorSize * 0.9)) {
    throw "payload $payloadBytes bytes does not fit in $($PartSectors * $SectorSize); raise -TotalSectors"
}

$img = New-Object byte[] $ImageSize

function W8($off, $v) { $img[$off] = [byte]$v }
function W16($off, $v) { [BitConverter]::GetBytes([ushort]$v).CopyTo($img, $off) }
function W32($off, $v) { [BitConverter]::GetBytes([int]$v).CopyTo($img, $off) }
function W64($off, $v) { [BitConverter]::GetBytes([ulong]$v).CopyTo($img, $off) }
function WBytes($off, $bs) { [Array]::Copy($bs, 0, $img, $off, $bs.Length) }
function WStr($off, $s, $len) {
    $b = [System.Text.Encoding]::ASCII.GetBytes($s)
    $n = [Math]::Min($b.Length, $len)
    [Array]::Copy($b, 0, $img, $off, $n)
}

# --- Protective MBR (sector 0) ---
W32 0x1B8 0xC0DEC0DE # MBR disk signature: nonzero, or Windows stamps its own
                     # random one into the live disk on enumeration
W8 0x1BE 0x00       # Status: inactive
W8 0x1C2 0xEE       # Type: GPT protective
W32 0x1C6 1          # LBA start
W32 0x1CA ($TotalSectors - 1)  # LBA size
W16 0x1FE 0xAA55    # Boot signature

# --- GPT Header (sector 1) ---
$gptOff = $SectorSize
WStr $gptOff "EFI PART" 8
W32 ($gptOff + 8) 0x00010000   # Revision 1.0
W32 ($gptOff + 12) 92          # Header size
W32 ($gptOff + 16) 0           # CRC32 (filled later)
W32 ($gptOff + 20) 0           # Reserved
W64 ($gptOff + 24) 1           # MyLBA
W64 ($gptOff + 32) ($TotalSectors - 1) # AlternateLBA
# 128 ENTRIES AND FirstUsableLBA 34, BECAUSE ANYTHING ELSE INVITES WINDOWS TO
# "REPAIR" THE TABLE, AND ITS REPAIR LEAVES THE DISK UNBOOTABLE.
#
# UEFI reserves a MINIMUM of 16,384 bytes for the partition entry array: 128
# entries of 128 bytes, LBA 2 through 33, with FirstUsableLBA at 34. This
# header used to declare NumberOfPartitionEntries=2 (a 256-byte array) while
# putting FirstUsableLBA at 2048, and the tail already reserved the full 33
# sectors -- LastUsableLBA is TotalSectors-34 immediately below -- so the image
# was internally inconsistent: it reserved room for 128 entries at the back and
# claimed 2 at the front.
#
# Measured 2026-07-29: Windows repairs that table on sight, and its repair is
# not cosmetic. PartitionEntryLBA is moved to sit immediately below
# FirstUsableLBA (2 -> 2047), LastUsableLBA is moved to sit immediately below
# the backup array (-34 -> -2), the backup array is moved to TotalSectors-2,
# and the header CRCs are recomputed while the array CRC is left stale. Both
# GPTs then fail validation, the good array is orphaned at LBA 2, and Windows
# itself reports the disk as MBR. Firmware sees no partitions, which is the
# "firmware never lists the stick" failure in HardwareSitting section 3b.
#
# The trigger is any partition-table re-read, and a raw write is one: AutoPlay
# fires DURING the flash and the repair races the flasher, which is how
# flash-usb's own fixup readback caught LBA 1 changing under it. Three
# hypotheses were tested and killed first -- physical reinsertion, the
# deterministic disk GUID, and disabling automount with mountvol /N -- so this
# is what is left, and it is the one that matches what the repair changes.
#
# Rufus writes bootable sticks on this same Windows and they survive, which is
# the existence proof that the behaviour is ours to avoid rather than Windows's
# to be endured. Give it a conforming table and it has nothing to normalise.
W64 ($gptOff + 40) 34          # FirstUsableLBA, immediately after the array
W64 ($gptOff + 48) ($TotalSectors - 34) # LastUsableLBA
# Disk GUID: deterministic so the .img reproduces byte for byte and a recorded
# digest means something. flash-usb randomises it on the MEDIUM at flash time.
WBytes ($gptOff + 56) ([byte[]]@(0xC0,0xDE,0xC0,0xDE,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C))
W64 ($gptOff + 72) 2           # PartitionEntryLBA
W32 ($gptOff + 80) 128         # NumberOfPartitionEntries (UEFI 16 KB minimum)
W32 ($gptOff + 84) 128         # SizeOfPartitionEntry
W32 ($gptOff + 88) 0           # PartitionEntryArrayCRC32 (filled later)

# --- GPT Partition Entry 0: the ESP (sector 2) ---
$peOff = 2 * $SectorSize
# EFI System Partition GUID: C12A7328-F81F-11D2-BA4B-00A0C93EC93B
WBytes $peOff ([byte[]]@(0x28,0x73,0x2A,0xC1,0x1F,0xF8,0xD2,0x11,0xBA,0x4B,0x00,0xA0,0xC9,0x3E,0xC9,0x3B))
# Unique partition GUID
WBytes ($peOff + 16) ([byte[]]@(0xAA,0xBB,0xCC,0xDD,0x11,0x22,0x33,0x44,0x55,0x66,0x77,0x88,0x99,0xAA,0xBB,0xCC))
W64 ($peOff + 32) $PartStart
W64 ($peOff + 40) ($PartStart + $PartSectors - 1)
W64 ($peOff + 48) 0           # Attributes
# Partition name: "EFI System" in UTF-16LE
$pName = [System.Text.Encoding]::Unicode.GetBytes("EFI System")
WBytes ($peOff + 56) $pName

# --- GPT Partition Entry 1: the Codex fact store ---
# Type C0DE1A11-FAC7-4C0D-9E75-C0DEC0DE5EED, mixed-endian on the medium. This
# is the same 16 bytes as gpt-codex-facts-guid in codex/foreword/core/Gpt.codex
# and in codex/plugs/img/GptWriter.codex; all three must agree.
$fsOff = $peOff + 128
WBytes $fsOff ([byte[]]@(0x11,0x1A,0xDE,0xC0,0xC7,0xFA,0x0D,0x4C,0x9E,0x75,0xC0,0xDE,0xC0,0xDE,0x5E,0xED))
WBytes ($fsOff + 16) ([byte[]]@(0xAA,0xBB,0xCC,0xDD,0x11,0x22,0x33,0x44,0x55,0x66,0x77,0x88,0x99,0xAA,0xBB,0xCD))
W64 ($fsOff + 32) $FactsStart
W64 ($fsOff + 40) $FactsEnd
W64 ($fsOff + 48) 0           # Attributes
$fName = [System.Text.Encoding]::Unicode.GetBytes("Codex Facts")
WBytes ($fsOff + 56) $fName

# --- CRC32 for partition entries ---
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

# The CRC covers NumberOfPartitionEntries * SizeOfPartitionEntry, which is now
# the full 16,384-byte reservation and not just the two populated entries. The
# figure has to match the header fields above or firmware rejects the table;
# the unused entries are zero and are part of the covered range.
$entryArrayBytes = 128 * 128
$entryCrc = Crc32 $img (2 * $SectorSize) $entryArrayBytes
W32 ($gptOff + 88) $entryCrc

# GPT header CRC (bytes 0-91 of header, with CRC field zeroed)
$hdrCrc = Crc32 $img $gptOff 92
W32 ($gptOff + 16) $hdrCrc

# --- Backup GPT (spec-required; the primary's AlternateLBA already points at
# the last sector). Entry array just below the backup header, header AT the
# image's last sector. Firmware that validates the backup (Dell) refuses the
# disk without this; Windows "repairs" any image that omits it on insertion.
$bakArrLba = $TotalSectors - 33
[Array]::Copy($img, 2 * $SectorSize, $img, $bakArrLba * $SectorSize, $entryArrayBytes)
$bakHdrOff = ($TotalSectors - 1) * $SectorSize
[Array]::Copy($img, $gptOff, $img, $bakHdrOff, 92)
W32 ($bakHdrOff + 16) 0                 # CRC32 (recomputed below)
W64 ($bakHdrOff + 24) ($TotalSectors - 1)  # MyLBA
W64 ($bakHdrOff + 32) 1                 # AlternateLBA -> primary
W64 ($bakHdrOff + 72) $bakArrLba        # PartitionEntryLBA
$bakCrc = Crc32 $img $bakHdrOff 92
W32 ($bakHdrOff + 16) $bakCrc

# --- FAT Partition (FAT16 default, FAT32 with -Fat32) ---
$fatBase = $PartStart * $SectorSize
$bytesPerSector = 512
$numFats = 2

if ($Fat32) {
    # A REAL FAT32 volume has >= 65525 clusters -- UEFI classifies by cluster
    # count (the FAT12/16 mislabel lesson, CL 7289), so a smaller volume
    # formatted "FAT32" reads back as something else on real firmware.
    $reservedSectors = 32
    $rootDirSectors = 0
    $sectorsPerCluster = 0
    foreach ($try in 1,2,4,8,16,32,64) {
        $fs = [int][Math]::Ceiling((($PartSectors / $try) + 2) * 4 / 512)
        $data = $PartSectors - ($reservedSectors + $numFats * $fs)
        $cl = [int][Math]::Floor($data / $try)
        if ($cl -ge 65525 -and $cl -le 1000000) { $sectorsPerCluster = $try; break }
    }
    if ($sectorsPerCluster -eq 0) { throw "FAT32 needs 65525..1000000 clusters; raise -TotalSectors (>= ~70000 sectors / 34 MB)." }
    $fatSectors = [int][Math]::Ceiling((($PartSectors / $sectorsPerCluster) + 2) * 4 / 512)
    $dataSector0 = $reservedSectors + $numFats * $fatSectors
    $dataClusters = [int][Math]::Floor(($PartSectors - $dataSector0) / $sectorsPerCluster)
    Write-Host "[build-img] FAT32: spc=$sectorsPerCluster clusters=$dataClusters (must be >= 65525)"
    if ($dataClusters -lt 65525) { throw "FAT32 needs >= 65525 clusters; got $dataClusters. Raise -TotalSectors." }

    WBytes $fatBase ([byte[]]@(0xEB, 0x58, 0x90))  # Jump + NOP (FAT32 jump target)
    WStr ($fatBase + 3) "CODEX   " 8
    W16 ($fatBase + 11) $bytesPerSector
    W8  ($fatBase + 13) $sectorsPerCluster
    W16 ($fatBase + 14) $reservedSectors
    W8  ($fatBase + 16) $numFats
    W16 ($fatBase + 17) 0                          # RootEntCnt: none, root is a chain
    W16 ($fatBase + 19) 0                          # TotalSectors16
    W8  ($fatBase + 21) 0xF8                       # Media type
    W16 ($fatBase + 22) 0                          # FATSz16: zero marks FAT32
    W16 ($fatBase + 24) 63
    W16 ($fatBase + 26) 255
    W32 ($fatBase + 28) $PartStart                 # HiddenSectors
    W32 ($fatBase + 32) $PartSectors               # TotalSectors32
    W32 ($fatBase + 36) $fatSectors                # FATSz32
    W16 ($fatBase + 40) 0                          # ExtFlags: mirrored FATs
    W16 ($fatBase + 42) 0                          # FSVer
    W32 ($fatBase + 44) 2                          # RootClus
    W16 ($fatBase + 48) 1                          # FSInfo sector
    W16 ($fatBase + 50) 6                          # Backup boot sector
    W8  ($fatBase + 64) 0x80                       # DriveNumber
    W8  ($fatBase + 66) 0x29                       # Extended boot sig
    W32 ($fatBase + 67) 0xC0DEC0DE                 # Volume serial
    WStr ($fatBase + 71) "CODEX      " 11
    WStr ($fatBase + 82) "FAT32   " 8
    W16 ($fatBase + 510) 0xAA55

    # FSInfo (sector 1): lead/struc signatures, free count and next-free
    # left "unknown" -- the hints are advisory by spec.
    $fsiOff = $fatBase + 512
    W32 $fsiOff 0x41615252
    W32 ($fsiOff + 484) 0x61417272
    W32 ($fsiOff + 488) (-1)
    W32 ($fsiOff + 492) (-1)
    W16 ($fsiOff + 510) 0xAA55
} else {
    $reservedSectors = 1
    $rootEntries = 512
    $rootDirSectors = [int][Math]::Ceiling($rootEntries * 32 / $bytesPerSector)

    # Choose sectors-per-cluster so the cluster count lands SOLIDLY in the FAT16
    # range (4085..65524). UEFI firmware determines FAT type by cluster count, not
    # by the "FAT16" label string -- an 8 MB image at spc=4 yields ~3560 clusters,
    # which the spec classifies as FAT12; the firmware then reads our 16-bit FAT as
    # 12-bit, misfollows every chain, and reports "no boot device." Pick the
    # smallest power-of-two spc that keeps clusters <= 60000 (margin under 65524),
    # which also maximizes clusters to stay well above the 4085 FAT12 boundary.
    $sectorsPerCluster = 1
    foreach ($try in 1,2,4,8,16,32,64) {
        $fs = [int][Math]::Ceiling(($PartSectors / $try + 2) / 256)
        $data = $PartSectors - ($reservedSectors + $numFats * $fs + $rootDirSectors)
        $cl = [int][Math]::Floor($data / $try)
        if ($cl -le 60000) { $sectorsPerCluster = $try; break }
    }
    $fatSectors = [int][Math]::Ceiling(($PartSectors / $sectorsPerCluster + 2) / 256)
    $dataSector0 = $reservedSectors + $numFats * $fatSectors + $rootDirSectors
    $dataClusters = [int][Math]::Floor(($PartSectors - $dataSector0) / $sectorsPerCluster)
    Write-Host "[build-img] FAT16: spc=$sectorsPerCluster clusters=$dataClusters (must be 4085..65524)"
    if ($dataClusters -lt 4085) { throw "FAT16 needs >= 4085 clusters; got $dataClusters (image too small). Increase TotalSectors." }
    if ($dataClusters -gt 65524) { throw "FAT16 allows <= 65524 clusters; got $dataClusters. Raise spc cap." }

    # BPB (BIOS Parameter Block)
    WBytes $fatBase ([byte[]]@(0xEB, 0x3C, 0x90))  # Jump + NOP
    WStr ($fatBase + 3) "CODEX   " 8               # OEM name
    W16 ($fatBase + 11) $bytesPerSector
    W8  ($fatBase + 13) $sectorsPerCluster
    W16 ($fatBase + 14) $reservedSectors
    W8  ($fatBase + 16) $numFats
    W16 ($fatBase + 17) $rootEntries
    W16 ($fatBase + 19) 0                           # TotalSectors16 (0 = use 32-bit)
    W8  ($fatBase + 21) 0xF8                        # Media type
    W16 ($fatBase + 22) $fatSectors
    W16 ($fatBase + 24) 63                          # SectorsPerTrack
    W16 ($fatBase + 26) 255                         # NumHeads
    W32 ($fatBase + 28) $PartStart                  # HiddenSectors
    W32 ($fatBase + 32) $PartSectors                # TotalSectors32
    W8  ($fatBase + 36) 0x80                        # DriveNumber
    W8  ($fatBase + 38) 0x29                        # Extended boot sig
    W32 ($fatBase + 39) 0xC0DEC0DE                  # Volume serial
    WStr ($fatBase + 43) "CODEX      " 11           # Volume label
    WStr ($fatBase + 54) "FAT16   " 8               # FS type
    W16 ($fatBase + 510) 0xAA55                     # Boot signature
}

# FAT tables
$fat1Off = $fatBase + $reservedSectors * $bytesPerSector
$fat2Off = $fat1Off + $fatSectors * $bytesPerSector

# One FAT entry in BOTH copies, at the volume's width. FAT32 entries keep
# the reserved top nibble zero.
$EOC = if ($Fat32) { 0x0FFFFFFF } else { 0xFFFF }
function Set-Fat($cluster, $value) {
    if ($Fat32) {
        [BitConverter]::GetBytes([uint32]$value).CopyTo($img, $fat1Off + $cluster * 4)
        [BitConverter]::GetBytes([uint32]$value).CopyTo($img, $fat2Off + $cluster * 4)
    } else {
        [BitConverter]::GetBytes([ushort]$value).CopyTo($img, $fat1Off + $cluster * 2)
        [BitConverter]::GetBytes([ushort]$value).CopyTo($img, $fat2Off + $cluster * 2)
    }
}

# FAT entries: cluster 0 = media, cluster 1 = end-of-chain marker
if ($Fat32) {
    Set-Fat 0 0x0FFFFFF8
    Set-Fat 1 0x0FFFFFFF
    Set-Fat 2 $EOC          # root directory: one cluster
} else {
    Set-Fat 0 0xFFF8
    Set-Fat 1 0xFFFF
}

# Root directory: FAT16 has a fixed region after the FATs; FAT32's root is
# cluster 2, the first data cluster.
$dataOff = $fatBase + $dataSector0 * $bytesPerSector
$rootOff = if ($Fat32) { $dataOff } else { $fat2Off + $fatSectors * $bytesPerSector }

# Create EFI directory entry in root. The cluster's high word always lands
# at offset 20 -- zero on FAT16, load-bearing on FAT32.
function Add-DirEntry($dirOff, $idx, $name, $attr, $cluster, $size) {
    $eOff = $dirOff + $idx * 32
    WStr $eOff $name 11
    W8 ($eOff + 11) $attr
    W16 ($eOff + 20) ($cluster -shr 16)
    W16 ($eOff + 26) ($cluster -band 0xFFFF)
    W32 ($eOff + 28) $size
}

# Helper: allocate clusters for a file, write to FAT
$nextCluster = if ($Fat32) { 3 } else { 2 }
function Alloc-File([byte[]]$fileData) {
    $clustSz = $sectorsPerCluster * $bytesPerSector
    $fileLen = $fileData.Length
    $numClusters = [int][Math]::Ceiling($fileLen / $clustSz)
    $startCluster = $script:nextCluster
    for ($c = 0; $c -lt $numClusters; $c++) {
        $cn = $startCluster + $c
        $fatEntry = if ($c -eq $numClusters - 1) { $EOC } else { $cn + 1 }
        Set-Fat $cn $fatEntry
        $fileOff = $dataOff + ($cn - 2) * $clustSz
        $srcOff = $c * $clustSz
        $chunkLen = [Math]::Min($fileLen - $srcOff, $clustSz)
        [Array]::Copy($fileData, $srcOff, $img, $fileOff, $chunkLen)
    }
    $script:nextCluster += $numClusters
    return $startCluster
}

# Create EFI directory (subdirectory)
$efiCluster = $script:nextCluster
$script:nextCluster++
$clustSz = $sectorsPerCluster * $bytesPerSector
$efiDirOff = $dataOff + ($efiCluster - 2) * $clustSz
# FAT: EFI dir is 1 cluster, end-of-chain
Set-Fat $efiCluster $EOC
# . and .. entries
Add-DirEntry $efiDirOff 0 ".          " 0x10 $efiCluster 0
Add-DirEntry $efiDirOff 1 "..         " 0x10 0 0

# Create BOOT subdirectory inside EFI
$bootCluster = $script:nextCluster
$script:nextCluster++
$bootDirOff = $dataOff + ($bootCluster - 2) * $clustSz
Set-Fat $bootCluster $EOC
Add-DirEntry $bootDirOff 0 ".          " 0x10 $bootCluster 0
Add-DirEntry $bootDirOff 1 "..         " 0x10 $efiCluster 0

# Add BOOT entry to EFI dir
Add-DirEntry $efiDirOff 2 "BOOT       " 0x10 $bootCluster 0

# Write BOOTX64.EFI into BOOT directory
$peFileCluster = Alloc-File $pe
Add-DirEntry $bootDirOff 2 "BOOTX64 EFI" 0x20 $peFileCluster $pe.Length

# Add EFI entry to root directory
Add-DirEntry $rootOff 0 "EFI        " 0x10 $efiCluster 0

# Write SOURCE.SRC and CODEX.CDX if provided, then the volume label. Root
# entries are indexed in order, so each optional file shifts the label.
$rootIdx = 1
if ($srcBytes.Length -gt 0) {
    $srcCluster = Alloc-File $srcBytes
    Add-DirEntry $rootOff $rootIdx "SOURCE  SRC" 0x20 $srcCluster $srcBytes.Length
    $rootIdx++
}
if ($seedBytes.Length -gt 0) {
    $seedCluster = Alloc-File $seedBytes
    Add-DirEntry $rootOff $rootIdx "CODEX   CDX" 0x20 $seedCluster $seedBytes.Length
    $rootIdx++
}
if ($fontBytes.Length -gt 0) {
    $fontCluster = Alloc-File $fontBytes
    Add-DirEntry $rootOff $rootIdx "CMUNSS  TTF" 0x20 $fontCluster $fontBytes.Length
    $rootIdx++
}
if ($agentBytes.Length -gt 0) {
    $agentCluster = Alloc-File $agentBytes
    Add-DirEntry $rootOff $rootIdx "AGENT   GGU" 0x20 $agentCluster $agentBytes.Length
    $rootIdx++
    $manCluster = Alloc-File $manBytes
    Add-DirEntry $rootOff $rootIdx "AGENT   MAN" 0x20 $manCluster $manBytes.Length
    $rootIdx++
}

# Volume label entry in root
Add-DirEntry $rootOff $rootIdx "CODEX      " 0x08 0 0

# FAT32 keeps a backup of the boot sector and FSInfo at sectors 6 and 7,
# where the BPB's BkBootSec field points recovery tools.
if ($Fat32) {
    [Array]::Copy($img, $fatBase, $img, $fatBase + 6 * 512, 512)
    [Array]::Copy($img, $fatBase + 512, $img, $fatBase + 7 * 512, 512)
}

[System.IO.File]::WriteAllBytes($Out, $img)
Write-Host "[build-img] OK: $Out ($($img.Length / 1MB) MB, PE=$($pe.Length) src=$($srcBytes.Length) seed=$($seedBytes.Length))"
