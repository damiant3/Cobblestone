# build-arm64-img.ps1 -- Build a bootable ARM64 UEFI GPT FAT16 disk image from a PE and optional source
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$PeInput,
    [Parameter(Mandatory=$true)]
    [string]$Out,
    [string]$Source = '',
    [int]$TotalSectors = 65536
)

# Wraps build-img.ps1 with ARM64-specific boot filename (BOOTAA64.EFI).
# Input: ARM64 PE32+ UEFI application.
# Output: Raw GPT disk image (convert to QCOW2 with qemu-img for OCI).
# 
# Usage:
#   build-arm64-img.ps1 -PeInput <arm64.efi> -Out <disk.img> [-Source <file>] [-TotalSectors 16384]


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host "ERROR at line $($_.InvocationInfo.ScriptLineNumber): $_"; throw $_ }

$SectorSize = 512
$PartStart = 2048
$PartSectors = $TotalSectors - $PartStart - 33
$ImageSize = $TotalSectors * $SectorSize


$pe = [System.IO.File]::ReadAllBytes($PeInput)
if (($Source -and (Test-Path -PathType Leaf $Source))) {
    $srcBytes = [System.IO.File]::ReadAllBytes($Source)
} else {
    $srcBytes = [byte[]]::new(0)
}

Write-Host "[build-arm64-img] PE=$($pe.Length) bytes  Source=$($srcBytes.Length) bytes  Image=$($ImageSize / 1MB) MB"

$img = New-Object byte[] $ImageSize


function W8($off, $v) { $img[$off] = [byte]$v }
function W16($off, $v) { [BitConverter]::GetBytes([ushort]$v).CopyTo($img, $off) }
function W32($off, $v) { [BitConverter]::GetBytes([int]$v).CopyTo($img, $off) }
function W64($off, $v) { [BitConverter]::GetBytes([ulong]$v).CopyTo($img, $off) }
function WBytes($off, $bs) { [Array]::Copy($bs, 0, $img, $off, $bs.Length) }
function WStr($off, $s, $len) { $b = [System.Text.Encoding]::ASCII.GetBytes($s); $n = [Math]::Min($b.Length, $len); [Array]::Copy($b, 0, $img, $off, $n) }


# Protective MBR (sector 0)
W8 0x1BE 0x00
W8 0x1C2 0xEE
W32 0x1C6 1
W32 0x1CA ($TotalSectors - 1)
W16 0x1FE 0xAA55


# GPT Header (sector 1)
$gptOff = $SectorSize
WStr $gptOff 'EFI PART' 8
W32 ($gptOff + 8) 0x00010000
W32 ($gptOff + 12) 92
W32 ($gptOff + 16) 0
W32 ($gptOff + 20) 0
W64 ($gptOff + 24) 1
W64 ($gptOff + 32) ($TotalSectors - 1)
W64 ($gptOff + 40) $PartStart
W64 ($gptOff + 48) ($TotalSectors - 34)
# Disk GUID
$diskGuid = [byte[]]@(0xCD,0xEF,0x01,0x23,0x45,0x67,0x89,0xAB,0xCD,0xEF,0x01,0x23,0x45,0x67,0x89,0xAB)
WBytes ($gptOff + 56) $diskGuid
W64 ($gptOff + 72) 2
W32 ($gptOff + 80) 128
W32 ($gptOff + 84) 128


# GPT Partition Entry (sector 2)
$entOff = 2 * $SectorSize
# EFI System Partition GUID
$espGuid = [byte[]]@(0x28,0x73,0x2A,0xC1,0x1F,0xF8,0xD2,0x11,0xBA,0x4B,0x00,0xA0,0xC9,0x3E,0xC9,0x3B)
WBytes $entOff $espGuid
$partGuid = [byte[]]@(0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F,0x10)
WBytes ($entOff + 16) $partGuid
W64 ($entOff + 32) $PartStart
W64 ($entOff + 40) ($TotalSectors - 34)
$pName = [System.Text.Encoding]::Unicode.GetBytes('EFI System')
WBytes ($entOff + 56) $pName


# CRC32 (same implementation as build-img.ps1)
function Crc32($data, $off, $len) { [long]$crc = 0xFFFFFFFF; for ($i = 0; $i -lt $len; $i++) { $crc = $crc -bxor $data[$off + $i]; for ($j = 0; $j -lt 8; $j++) { if ($crc -band 1) { $crc = (($crc -shr 1) -band 0x7FFFFFFF) -bxor 0xEDB88320 } else { $crc = ($crc -shr 1) -band 0x7FFFFFFF } } }; return ($crc -bxor 0xFFFFFFFF) -band 0xFFFFFFFF }

$entryCrc = Crc32 $img (2 * $SectorSize) (128 * 128)
W32 ($gptOff + 88) $entryCrc

$hdrCrc = Crc32 $img $gptOff 92
W32 ($gptOff + 16) $hdrCrc


# FAT16 Partition
# BPB (BIOS Parameter Block) -- must match UEFI FAT16 expectations exactly
$fatBase = $PartStart * $SectorSize
$bytesPerSector = 512
$sectorsPerCluster = 8
$reservedSectors = 1
$numFats = 2
$rootEntries = 512
$rootDirSectors = [int][Math]::Ceiling($rootEntries * 32 / $bytesPerSector)
$fatSectors = [int][Math]::Ceiling(($PartSectors / $sectorsPerCluster + 2) / 256)
$dataSector0 = $reservedSectors + $numFats * $fatSectors + $rootDirSectors

WBytes $fatBase ([byte[]]@(0xEB, 0x3C, 0x90))  # Jump + NOP
WStr ($fatBase + 3) 'CODEX   ' 8               # OEM name
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
WStr ($fatBase + 43) 'CODEX      ' 11           # Volume label
WStr ($fatBase + 54) 'FAT16   ' 8               # FS type
W16 ($fatBase + 510) 0xAA55                     # Boot signature


# FAT tables and root directory
$fat1Off = $fatBase + $reservedSectors * $bytesPerSector
$fat2Off = $fat1Off + $fatSectors * $bytesPerSector

W16 $fat1Off 0xFFF8
W16 ($fat1Off + 2) 0xFFFF

$rootOff = $fat2Off + $fatSectors * $bytesPerSector
$dataOff = $fatBase + $dataSector0 * $bytesPerSector


function Add-DirEntry($dirOff, $idx, $name, $attr, $cluster, $size) { $eOff = $dirOff + $idx * 32; WStr $eOff $name 11; W8 ($eOff + 11) $attr; W16 ($eOff + 26) $cluster; W32 ($eOff + 28) $size }

$nextCluster = 2
function Alloc-File([byte[]]$fileData) { $clustSz = $sectorsPerCluster * $bytesPerSector; $fileLen = $fileData.Length; $numClusters = [int][Math]::Ceiling($fileLen / $clustSz); $startCluster = $script:nextCluster; for ($c = 0; $c -lt $numClusters; $c++) { $cn = $startCluster + $c; $fatEntry = if ($c -eq $numClusters - 1) { [ushort]0xFFFF } else { [ushort]($cn + 1) }; [BitConverter]::GetBytes($fatEntry).CopyTo($img, $fat1Off + $cn * 2); [BitConverter]::GetBytes($fatEntry).CopyTo($img, $fat2Off + $cn * 2); $fileOff = $dataOff + ($cn - 2) * $clustSz; $srcOff = $c * $clustSz; $chunkLen = [Math]::Min($fileLen - $srcOff, $clustSz); [Array]::Copy($fileData, $srcOff, $img, $fileOff, $chunkLen) }; $script:nextCluster += $numClusters; return $startCluster }


# EFI directory
$efiCluster = $nextCluster
$script:nextCluster++
$clustSz = $sectorsPerCluster * $bytesPerSector
$efiDirOff = $dataOff + ($efiCluster - 2) * $clustSz
W16 ($fat1Off + $efiCluster * 2) 0xFFFF
W16 ($fat2Off + $efiCluster * 2) 0xFFFF
Add-DirEntry $efiDirOff 0 '.          ' 0x10 $efiCluster 0
Add-DirEntry $efiDirOff 1 '..         ' 0x10 0 0

# BOOT subdirectory
$bootCluster = $nextCluster
$script:nextCluster++
$bootDirOff = $dataOff + ($bootCluster - 2) * $clustSz
W16 ($fat1Off + $bootCluster * 2) 0xFFFF
W16 ($fat2Off + $bootCluster * 2) 0xFFFF
Add-DirEntry $bootDirOff 0 '.          ' 0x10 $bootCluster 0
Add-DirEntry $bootDirOff 1 '..         ' 0x10 $efiCluster 0


# Write BOOT entry, ARM64 PE, root PE copy, startup.nsh, optional source
Add-DirEntry $efiDirOff 2 'BOOT       ' 0x10 $bootCluster 0

$peFileCluster = Alloc-File $pe
# ARM64 UEFI boot: BOOTAA64.EFI
Add-DirEntry $bootDirOff 2 'BOOTAA64EFI' 0x20 $peFileCluster $pe.Length

Add-DirEntry $rootOff 0 'EFI        ' 0x10 $efiCluster 0

# PE copy in root for direct execution
$rootPeCluster = Alloc-File $pe
Add-DirEntry $rootOff 1 'CODEX   EFI' 0x20 $rootPeCluster $pe.Length

# startup.nsh (UCS-2 LE BOM)
$nshText = 'FS0:' + [char]13 + [char]10 + 'CODEX.EFI' + [char]13 + [char]10
$nshUcs2 = [System.Text.Encoding]::Unicode.GetBytes($nshText)
$nshBom = [byte[]]@(0xFF, 0xFE)
$nshContent = $nshBom + $nshUcs2
$nshCluster = Alloc-File $nshContent
Add-DirEntry $rootOff 2 'STARTUP NSH' 0x20 $nshCluster $nshContent.Length


if ($srcBytes.Length -gt 0) {
    $srcCluster = Alloc-File $srcBytes
    Add-DirEntry $rootOff 3 'SOURCE  SRC' 0x20 $srcCluster $srcBytes.Length
}

Add-DirEntry $rootOff ($(if ($srcBytes.Length -gt 0) { 4 } else { 3 })) 'CODEX      ' 0x08 0 0


[System.IO.File]::WriteAllBytes($Out, $img)
Write-Host "[build-arm64-img] OK: $Out ($($img.Length / 1MB) MB, PE=$($pe.Length) src=$($srcBytes.Length))"
