# build-img.ps1 -- Build a bootable GPT FAT16 disk image from a PE and optional source.
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
    # An existing identity, written to the ESP root as IDENTITY.DAT. A stick
    # WITHOUT this file boots into the first-boot wizard rather than the desk,
    # so a freshly built image costs a passphrase, an entropy screen and a
    # keygen before anything can be tested. Passing an identity that was
    # generated on the target machine skips all of it.
    # 
    # The key inside is the whole trust story, so this is deliberately opt-in
    # and deliberately not defaulted to any path: an identity must be created
    # on the machine that carries it, and an image built with a key generated
    # in the bed puts a bed-generated key on the flown stick. Reuse one that
    # came off the target, or leave this empty and walk the wizard.
    [string]$Identity = '',
    # A directory of .codex chapters, written INDIVIDUALLY into an SRC
    # subdirectory of the ESP. -Source puts the whole tree on as one
    # concatenated file, which is what the compiler reads and what no person
    # can edit; this puts the chapters on as themselves. Both, not either:
    # the concat is the build input and these are the editable copies.
    [string]$SourceDir = '',
    # Extra files for the ESP root, each 'NAME.EXT=path' with an 8.3 name;
    # several may share one argument separated by ';' (pwsh -File hands a
    # comma list to a [string[]] as ONE element). The diagnostic image carries
    # DIAG.ID and DIAG.CFG this way. Written after the named optional files
    # above, before SRC/ and the label.
    [string[]]$Extra = @(),
    [int]$TotalSectors = 16384,
    # Format the ESP as FAT32 instead of FAT16 -- the layout every vendor
    # stick and every volume past 2 GB actually carries. A REAL FAT32 volume
    # needs >= 65525 clusters (UEFI classifies by cluster count, the CL 7289
    # lesson), so -TotalSectors must be >= ~70000 (34 MB).
    [switch]$Fat32,
    # Put the LIBRARY quires on the ESP, each in the directory quire-to-dir
    # names for it and each chapter under its real name, so disk-load-cite
    # resolves a cite straight off the medium. Opt-in: it is the whole library
    # and the ESP is finite.
    [switch]$Library
)

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
# A missing -Identity is a hard error rather than an empty byte array, unlike
# every optional input above. Falling back silently would produce an image that
# looks right, builds clean, and boots into the wizard the caller passed this

# flag to avoid -- and the caller finds out at the machine, after a flash.
if ($Identity) {
    if (-not (Test-Path -PathType Leaf $Identity)) { throw "-Identity file not found: $Identity" }
    $identBytes = [System.IO.File]::ReadAllBytes($Identity)
    if ($identBytes.Length -eq 0) { throw "-Identity file is empty: $Identity" }
} else { $identBytes = [byte[]]::new(0) }
$extraFiles = @()
foreach ($e in ($Extra -join ';').Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
    $eq = $e.IndexOf('=')
    if ($eq -lt 1) { throw "-Extra entry must be NAME.EXT=path: $e" }
    $ename = $e.Substring(0, $eq).ToUpper()
    $epath = $e.Substring($eq + 1)
    if ($ename -notmatch '^[A-Z0-9_]{1,8}(\.[A-Z0-9_]{1,3})?$') { throw "-Extra name is not 8.3: $ename" }
    if (-not (Test-Path -PathType Leaf $epath)) { throw "-Extra file not found: $epath" }
    $eparts = $ename.Split('.')
    $eshort = $eparts[0].PadRight(8) + $(if ($eparts.Count -gt 1) { $eparts[1].PadRight(3) } else { '   ' })
    $extraFiles += ,@($eshort, [System.IO.File]::ReadAllBytes($epath))
}

Write-Host "[build-img] PE=$($pe.Length) bytes  Source=$($srcBytes.Length) bytes  Seed=$($seedBytes.Length) bytes  Agent=$($agentBytes.Length) bytes  Image=$($ImageSize / 1MB) MB"
if ($identBytes.Length -gt 0) {
    Write-Host "[build-img] IDENTITY.DAT=$($identBytes.Length) bytes from $Identity -- skips the FIRST-BOOT WIZARD (keygen, entropy, confirm)"
} else {
    Write-Host "[build-img] no -Identity: this image runs the FIRST-BOOT WIZARD before the desk"
}
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
if ($identBytes.Length -gt 0) {
    $identCluster = Alloc-File $identBytes

    Add-DirEntry $rootOff $rootIdx "IDENTITYDAT" 0x20 $identCluster $identBytes.Length
    $rootIdx++
}
foreach ($ef in $extraFiles) {
    $efCluster = if ($ef[1].Length -gt 0) { Alloc-File $ef[1] } else { 0 }
    Add-DirEntry $rootOff $rootIdx $ef[0] 0x20 $efCluster $ef[1].Length
    $rootIdx++
}

# The chapters as individual files, under their real names.
#
# THE OLD LAYOUT COULD NOT BE READ BACK BY ANYTHING. It put every chapter in a
# flat SRC/ with the name mangled to 8.3 -- `X86_64CodeGen.codex` became
# X86_64CO.COD -- and wrote SRC/INDEX.TXT to explain the mangling. disk-load-cite
# asks the volume for `quire-to-dir <quire>` joined to `<Name>.codex`, so both
# halves were wrong: wrong directory and wrong name. It never complained,
# because that resolver drops a cite it cannot find without a word.
#
# Fat16.codex reads and writes VFAT long names now, so a chapter goes on under
# its own name. The checksum, the alias and the record layout below are a
# SECOND implementation of the format; the guest's reader is the first, and an
# image that boots and resolves a cite is what proves the two agree.

# The 13 UTF-16 slots of a long-name record sit at three disjoint runs -- five
# from byte 1, six from 14, two from 28 -- because the bytes between them are
# the fields an 8.3-only reader inspects. Attribute 0x0F reads to such a reader
# as read-only+hidden+system+volume-label at once, which no real file is, so it
# skips the run instead of showing it.
$LfnSlots = @(1,3,5,7,9, 14,16,18,20,22,24, 28,30)

# A byte-wide rotate-right of the running sum plus each of the eleven stored
# name bytes. Same arithmetic as fat16-short-checksum; it is the only thing
# binding a run to the short entry beneath it.
function Get-ShortChecksum([string]$name11) {
    if ($name11.Length -ne 11) { throw "short name must be 11 stored bytes: '$name11'" }
    $s = 0
    foreach ($ch in $name11.ToCharArray()) { $s = ((($s -band 1) -shl 7) + ($s -shr 1) + [int]$ch) -band 0xFF }
    return $s
}

function Split-CodexName([string]$name) {
    $dot = $name.IndexOf('.')
    if ($dot -lt 0) { return @($name, '') }
    return @($name.Substring(0, $dot), $name.Substring($dot + 1))
}

# Must agree with fat16-needs-long: a name equal to its own 8.3 form needs no
# run, and anything else does -- including a name that merely has lower case.
function Test-NeedsLfn([string]$name) {
    $p = Split-CodexName $name
    if ($p[0].Length -gt 8 -or $p[1].Length -gt 3) { return $true }
    $short = if ($p[1].Length -eq 0) { $p[0].ToUpper() } else { $p[0].ToUpper() + '.' + $p[1].ToUpper() }
    return ($short -cne $name)
}

function ConvertTo-AliasChars([string]$s) {
    $out = ''
    foreach ($c in $s.ToCharArray()) {
        $u = [int]$c
        if ($u -ge 97 -and $u -le 122) { $u = $u - 32 }
        $ok = ($u -ge 65 -and $u -le 90) -or ($u -ge 48 -and $u -le 57) -or $u -eq 95 -or $u -eq 45 -or $u -eq 126
        $out += $(if ($ok) { [char]$u } else { '_' })
    }
    return $out
}

# The alias is searched rather than derived: uniqueness is a fact about the
# directory, and two files answering to one alias is the duplicate-name failure
# every 8.3-only driver would then inherit.
function Get-ShortName([string]$name, [hashtable]$taken) {
    $p = Split-CodexName $name
    if (-not (Test-NeedsLfn $name)) {
        $s = (ConvertTo-AliasChars $p[0]).PadRight(8) + (ConvertTo-AliasChars $p[1]).PadRight(3)
        if ($taken.ContainsKey($s)) { throw "duplicate 8.3 name in one directory: $name" }
        $taken[$s] = $true
        return $s
    }
    $ext = ConvertTo-AliasChars $p[1]
    if ($ext.Length -gt 3) { $ext = $ext.Substring(0, 3) }
    for ($n = 1; $n -le 99; $n++) {
        $tag = "~$n"
        $keep = [Math]::Min(8 - $tag.Length, $p[0].Length)
        $cand = ((ConvertTo-AliasChars $p[0].Substring(0, $keep)) + $tag).PadRight(8) + $ext.PadRight(3)
        if (-not $taken.ContainsKey($cand)) { $taken[$cand] = $true; return $cand }
    }
    throw "no free 8.3 alias for '$name' after 99 tries"
}

function Get-EntrySlots([string]$name) {
    if (-not (Test-NeedsLfn $name)) { return 1 }
    return [int][Math]::Ceiling($name.Length / 13.0) + 1
}

# Records go down in DESCENDING sequence, the one first on disk carrying the
# highest ordinal with bit 0x40, and the short entry last. Answers the slots
# consumed so the caller advances by the right amount.
function Add-NamedEntry($dirOff, $idx, [string]$name, [hashtable]$taken, $attr, $cluster, $size) {
    $short = Get-ShortName $name $taken
    if (-not (Test-NeedsLfn $name)) {
        Add-DirEntry $dirOff $idx $short $attr $cluster $size
        return 1
    }
    $sum = Get-ShortChecksum $short
    $records = [int][Math]::Ceiling($name.Length / 13.0)
    for ($j = 0; $j -lt $records; $j++) {
        $seq = $records - $j
        $e = $dirOff + ($idx + $j) * 32
        W8 $e $(if ($j -eq 0) { $seq -bor 0x40 } else { $seq })
        W8 ($e + 11) 0x0F
        W8 ($e + 12) 0
        W8 ($e + 13) $sum
        W16 ($e + 26) 0
        $base = ($seq - 1) * 13
        for ($k = 0; $k -lt 13; $k++) {
            $i = $base + $k
            $u = if ($i -lt $name.Length) { [int]$name[$i] } elseif ($i -eq $name.Length) { 0 } else { 0xFFFF }
            W16 ($e + $LfnSlots[$k]) $u
        }
    }
    Add-DirEntry $dirOff ($idx + $records) $short $attr $cluster $size
    return $records + 1
}

# Allocates a contiguous directory of $slots entries, writes its dot pair, and
# answers its byte offset and first cluster. Contiguous and in one run so a
# single flat offset addresses every entry, which the bump allocator makes true
# only while nothing else allocates in the middle.
function New-SubDir($slots, $parentCluster) {
    $need = [int][Math]::Ceiling((($slots + 2) * 32) / $clustSz)
    $first = $script:nextCluster
    for ($c = 0; $c -lt $need; $c++) {
        $cn = $first + $c
        Set-Fat $cn $(if ($c -eq $need - 1) { $EOC } else { $cn + 1 })
    }
    $script:nextCluster += $need
    $off = $dataOff + ($first - 2) * $clustSz
    Add-DirEntry $off 0 ".          " 0x10 $first 0
    Add-DirEntry $off 1 "..         " 0x10 $parentCluster 0
    return @($off, $first)
}

# Alias uniqueness is per directory, and the root already holds entries this
# script wrote above (EFI, CODEX.CDX, the -Extra files). Read them back off the
# bytes rather than tracking them, so a name added earlier cannot be missed.
$rootTaken = @{}
for ($ri = 0; $ri -lt $rootIdx; $ri++) {
    $ro = $rootOff + $ri * 32
    if ($img[$ro] -ne 0 -and $img[$ro] -ne 0xE5) {
        $rootTaken[[System.Text.Encoding]::ASCII.GetString($img, $ro, 11)] = $true
    }
}

if ($SourceDir -and (Test-Path $SourceDir)) {
    $chapters = @(Get-ChildItem -Path $SourceDir -Recurse -Filter *.codex -File | Sort-Object FullName)
    if ($chapters.Count -gt 0) {
        $srcTaken = @{}
        $srcSlots = 0
        foreach ($ch in $chapters) { $srcSlots += Get-EntrySlots $ch.Name }
        $mk = New-SubDir $srcSlots 0
        $srcDirOff = $mk[0]
        $srcDirCluster = $mk[1]
        $sIdx = 2
        foreach ($ch in $chapters) {
            $bytes = [System.IO.File]::ReadAllBytes($ch.FullName)
            $cl = Alloc-File $bytes
            $sIdx += Add-NamedEntry $srcDirOff $sIdx $ch.Name $srcTaken 0x20 $cl $bytes.Length
        }
        $rootIdx += Add-NamedEntry $rootOff $rootIdx "SRC" $rootTaken 0x10 $srcDirCluster 0
        Write-Host "[build-img] SRC/ holds $($chapters.Count) chapters under their own names, $(($chapters | Measure-Object Length -Sum).Sum) bytes"
    }
}

# The library quires, each in the directory quire-to-dir names for it, so
# disk-load-cite resolves `<quire-to-dir q><Name>.codex` straight off the
# medium. Opt-in because it is the whole library and the ESP is finite.
if ($Library) {
    . (Join-Path $PSScriptRoot 'quire-map.ps1')
    $repoRoot = Split-Path $PSScriptRoot -Parent

    # The on-disk name is read out of quire-to-dir in the compiler's own source
    # rather than copied here, so the image layout and the resolver cannot
    # drift apart. Its fallback arm (`else "foreword/"`) is deliberately not
    # read: a quire it does not name is unreachable on the medium under its own
    # name, so putting one there would be a directory nothing ever opens.
    $q2dPath = Join-Path $repoRoot 'codex\compiler\opening.codex'
    $q2d = [ordered]@{}
    $inQ2d = $false
    foreach ($l in [System.IO.File]::ReadAllLines($q2dPath)) {
        if ($l -match '^\s*quire-to-dir\s*\(q\)\s*=') { $inQ2d = $true; continue }
        if (-not $inQ2d) { continue }
        if ($l -match 'q == "([A-Za-z0-9]+)" then "([^"]+)/"') { $q2d[$matches[1]] = $matches[2]; continue }
        break
    }
    if ($q2d.Count -eq 0) { throw "build-img: could not read quire-to-dir out of $q2dPath" }

    $libCount = 0
    $libBytes = 0
    foreach ($q in $q2d.Keys) {
        $srcRel = $QuireDirs[$q]
        if (-not $srcRel) { continue }
        $srcAbs = Join-Path $repoRoot $srcRel
        if (-not (Test-Path -PathType Container $srcAbs)) { continue }
        $files = @(Get-ChildItem -Path $srcAbs -Filter *.codex -File | Sort-Object Name)
        if ($files.Count -eq 0) { continue }
        $slots = 0
        foreach ($f in $files) { $slots += Get-EntrySlots $f.Name }
        $qmk = New-SubDir $slots $null
        $qOff = $qmk[0]
        $qCluster = $qmk[1]
        $qTaken = @{}
        $qIdx = 2
        foreach ($f in $files) {
            $fb = [System.IO.File]::ReadAllBytes($f.FullName)
            $fcl = Alloc-File $fb
            $qIdx += Add-NamedEntry $qOff $qIdx $f.Name $qTaken 0x20 $fcl $fb.Length
            $libBytes += $fb.Length
        }
        $rootIdx += Add-NamedEntry $rootOff $rootIdx $q2d[$q] $rootTaken 0x10 $qCluster 0
        $libCount += $files.Count
        Write-Host "[build-img]   $($q2d[$q])/ $($files.Count) chapters"
    }
    Write-Host "[build-img] library: $($q2d.Count) quires known to quire-to-dir, $libCount chapters, $libBytes bytes"
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
