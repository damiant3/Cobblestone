# Build a bootable GPT FAT16 disk image from a PE and optional source.
#
# Layout:
#   Sector 0:     Protective MBR
#   Sector 1:     GPT header
#   Sectors 2-33: GPT entries (1 partition)
#   Sector 2048+: FAT16 partition
#     EFI/BOOT/BOOTX64.EFI  (the PE)
#     SOURCE.SRC             (compiler source, if provided)
#
# Usage: build-img.ps1 -PeInput <file.efi> -Out <file.img> [-Source <file>] [-TotalSectors 16384]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$PeInput,
    [Parameter(Mandatory=$true)] [string]$Out,
    [string]$Source = '',
    # The CDX seed, written to the ESP root as CODEX.CDX. The booted payload
    # reads it back with its own drivers and verifies it (WakeCeremony).
    [string]$Seed = '',
    [int]$TotalSectors = 16384
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR at line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$SectorSize = 512
$PartStart  = 2048
$PartSectors = $TotalSectors - $PartStart - 33  # leave room for backup GPT
$ImageSize  = $TotalSectors * $SectorSize

$pe = [System.IO.File]::ReadAllBytes($PeInput)
if ($Source -and (Test-Path $Source)) { $srcBytes = [System.IO.File]::ReadAllBytes($Source) } else { $srcBytes = [byte[]]::new(0) }
if ($Seed -and (Test-Path $Seed)) { $seedBytes = [System.IO.File]::ReadAllBytes($Seed) } else { $seedBytes = [byte[]]::new(0) }

Write-Host "[build-img] PE=$($pe.Length) bytes  Source=$($srcBytes.Length) bytes  Seed=$($seedBytes.Length) bytes  Image=$($ImageSize / 1MB) MB"
$payloadBytes = $pe.Length + $srcBytes.Length + $seedBytes.Length
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
W64 ($gptOff + 40) $PartStart  # FirstUsableLBA
W64 ($gptOff + 48) ($TotalSectors - 34) # LastUsableLBA
# Disk GUID (random-ish)
WBytes ($gptOff + 56) ([byte[]]@(0xC0,0xDE,0xC0,0xDE,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C))
W64 ($gptOff + 72) 2           # PartitionEntryLBA
W32 ($gptOff + 80) 1           # NumberOfPartitionEntries
W32 ($gptOff + 84) 128         # SizeOfPartitionEntry
W32 ($gptOff + 88) 0           # PartitionEntryArrayCRC32 (filled later)

# --- GPT Partition Entry (sector 2) ---
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

$entryCrc = Crc32 $img (2 * $SectorSize) 128
W32 ($gptOff + 88) $entryCrc

# GPT header CRC (bytes 0-91 of header, with CRC field zeroed)
$hdrCrc = Crc32 $img $gptOff 92
W32 ($gptOff + 16) $hdrCrc

# --- FAT16 Partition ---
$fatBase = $PartStart * $SectorSize
$bytesPerSector = 512
$reservedSectors = 1
$numFats = 2
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

# FAT tables
$fat1Off = $fatBase + $reservedSectors * $bytesPerSector
$fat2Off = $fat1Off + $fatSectors * $bytesPerSector

# FAT entries: cluster 0 = media, cluster 1 = end-of-chain marker
W16 $fat1Off 0xFFF8
W16 ($fat1Off + 2) 0xFFFF

# Root directory
$rootOff = $fat2Off + $fatSectors * $bytesPerSector
$dataOff = $fatBase + $dataSector0 * $bytesPerSector

# Create EFI directory entry in root
function Add-DirEntry($dirOff, $idx, $name, $attr, $cluster, $size) {
    $eOff = $dirOff + $idx * 32
    WStr $eOff $name 11
    W8 ($eOff + 11) $attr
    W16 ($eOff + 26) $cluster
    W32 ($eOff + 28) $size
}

# Helper: allocate clusters for a file, write to FAT
$nextCluster = 2
function Alloc-File([byte[]]$fileData) {
    $clustSz = $sectorsPerCluster * $bytesPerSector
    $fileLen = $fileData.Length
    $numClusters = [int][Math]::Ceiling($fileLen / $clustSz)
    $startCluster = $script:nextCluster
    for ($c = 0; $c -lt $numClusters; $c++) {
        $cn = $startCluster + $c
        $fatEntry = if ($c -eq $numClusters - 1) { [ushort]0xFFFF } else { [ushort]($cn + 1) }
        [BitConverter]::GetBytes($fatEntry).CopyTo($img, $fat1Off + $cn * 2)
        [BitConverter]::GetBytes($fatEntry).CopyTo($img, $fat2Off + $cn * 2)
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
W16 ($fat1Off + $efiCluster * 2) 0xFFFF
W16 ($fat2Off + $efiCluster * 2) 0xFFFF
# . and .. entries
Add-DirEntry $efiDirOff 0 ".          " 0x10 $efiCluster 0
Add-DirEntry $efiDirOff 1 "..         " 0x10 0 0

# Create BOOT subdirectory inside EFI
$bootCluster = $script:nextCluster
$script:nextCluster++
$bootDirOff = $dataOff + ($bootCluster - 2) * $clustSz
W16 ($fat1Off + $bootCluster * 2) 0xFFFF
W16 ($fat2Off + $bootCluster * 2) 0xFFFF
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

# Volume label entry in root
Add-DirEntry $rootOff $rootIdx "CODEX      " 0x08 0 0

[System.IO.File]::WriteAllBytes($Out, $img)
Write-Host "[build-img] OK: $Out ($($img.Length / 1MB) MB, PE=$($pe.Length) src=$($srcBytes.Length) seed=$($seedBytes.Length))"
