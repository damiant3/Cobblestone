# Mint the two partition fixtures the fact-store addressing tests read.
#
#   codex/test/apps/facts-partition.disk    a valid GPT carrying an ESP and a
#                                           Codex fact-store partition
#   codex/test/apps/disk-facts-mbr-guard.disk  an MBR-only disk: a boot
#                                           signature at sector 0 and no GPT
#
# Both images are authored from the GPT SPECIFICATION here, and NOT by running
# build/build-img.ps1 or the IMG plug. A fixture produced by the writer under
# test proves only that the reader agrees with its own writer, which is the
# failure docs/PM/Active/Stories/BrotliBeatsOpus.md is about. The type GUID's
# agreement across the three writers is a separate question with its own
# runner, build/check-facts-guid.ps1; it is a textual fact and does not need a
# VM to settle.
#
# The layout is deliberately NOT the shipping policy (an eighth of the medium
# off the top). It is whatever makes a small fixture, because what these tests
# are about is that the store addresses relative to wherever the partition
# happens to be -- a fixture that copied the shipping arithmetic would pass for
# a store that had the shipping arithmetic hardcoded.

param(
    [string]$OutDir = "codex/test/apps"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$SectorSize   = 512
$TotalSectors = 256                       # 128 KB
$LastUsable   = $TotalSectors - 34        # 222; backup entry array at 223
$EspStart     = 34
$EspEnd       = 127
$FactsStart   = 128
$FactsEnd     = $LastUsable               # 222, so 95 sectors of store

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

# ---------------------------------------------------------------- GPT fixture

$img = New-Object byte[] ($TotalSectors * $SectorSize)

function W8 ($off, $v)  { $img[$off] = [byte]$v }
function W16($off, $v)  { [BitConverter]::GetBytes([ushort]$v).CopyTo($img, $off) }
function W32($off, $v)  { [BitConverter]::GetBytes([int]$v).CopyTo($img, $off) }
function W64($off, $v)  { [BitConverter]::GetBytes([ulong]$v).CopyTo($img, $off) }
function WBytes($off, $bs) { [Array]::Copy($bs, 0, $img, $off, $bs.Length) }
function WStr($off, $s, $len) {
    $b = [System.Text.Encoding]::ASCII.GetBytes($s)
    [Array]::Copy($b, 0, $img, $off, [Math]::Min($b.Length, $len))
}

# Protective MBR
W8  0x1BE 0x00
W8  0x1C2 0xEE
W32 0x1C6 1
W32 0x1CA ($TotalSectors - 1)
W16 0x1FE 0xAA55

# GPT header at sector 1
$g = $SectorSize
WStr $g "EFI PART" 8
W32 ($g + 8)  0x00010000
W32 ($g + 12) 92
W32 ($g + 16) 0
W64 ($g + 24) 1
W64 ($g + 32) ($TotalSectors - 1)
W64 ($g + 40) $EspStart
W64 ($g + 48) $LastUsable
WBytes ($g + 56) ([byte[]]@(0xF1,0xF2,0xF3,0xF4,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C))
W64 ($g + 72) 2
W32 ($g + 80) 2
W32 ($g + 84) 128
W32 ($g + 88) 0

# Entry 0: the ESP. Present so the fact store has to pick the right partition
# rather than the first one -- with a single entry a lookup by type GUID and a
# lookup that takes whatever is there produce the same answer.
$e0 = 2 * $SectorSize
WBytes $e0 ([byte[]]@(0x28,0x73,0x2A,0xC1,0x1F,0xF8,0xD2,0x11,0xBA,0x4B,0x00,0xA0,0xC9,0x3E,0xC9,0x3B))
WBytes ($e0 + 16) ([byte[]]@(0x11,0x11,0x11,0x11,0x22,0x22,0x33,0x33,0x44,0x44,0x55,0x55,0x55,0x55,0x55,0x55))
W64 ($e0 + 32) $EspStart
W64 ($e0 + 40) $EspEnd
WBytes ($e0 + 56) ([System.Text.Encoding]::Unicode.GetBytes("EFI System"))

# Entry 1: the fact store. Type C0DE1A11-FAC7-4C0D-9E75-C0DEC0DE5EED, stored
# mixed-endian: the first field's four bytes reversed, then the second and
# third reversed, then the last eight as written.
$e1 = $e0 + 128
WBytes $e1 ([byte[]]@(0x11,0x1A,0xDE,0xC0,0xC7,0xFA,0x0D,0x4C,0x9E,0x75,0xC0,0xDE,0xC0,0xDE,0x5E,0xED))
WBytes ($e1 + 16) ([byte[]]@(0x99,0x99,0x99,0x99,0x88,0x88,0x77,0x77,0x66,0x66,0x55,0x55,0x55,0x55,0x55,0x56))
W64 ($e1 + 32) $FactsStart
W64 ($e1 + 40) $FactsEnd
WBytes ($e1 + 56) ([System.Text.Encoding]::Unicode.GetBytes("Codex Facts"))

W32 ($g + 88) (Crc32 $img $e0 256)
W32 ($g + 16) (Crc32 $img $g 92)

# Backup GPT. Present because a fixture the firmware would reject is a fixture
# that tests the reader against something no real disk looks like.
$bakArr = $TotalSectors - 33
[Array]::Copy($img, $e0, $img, $bakArr * $SectorSize, 256)
$bakHdr = ($TotalSectors - 1) * $SectorSize
[Array]::Copy($img, $g, $img, $bakHdr, 92)
W32 ($bakHdr + 16) 0
W64 ($bakHdr + 24) ($TotalSectors - 1)
W64 ($bakHdr + 32) 1
W64 ($bakHdr + 72) $bakArr
W32 ($bakHdr + 16) (Crc32 $img $bakHdr 92)

$gptOut = Join-Path $OutDir 'facts-partition.disk'
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $gptOut), $img)
Write-Host "wrote $gptOut ($($img.Length) bytes)  ESP $EspStart..$EspEnd  facts $FactsStart..$FactsEnd"

# ---------------------------------------------------------------- MBR fixture
#
# A boot signature at sector 0 and nothing else. This is the residual case the
# old guard missed: it looked only for "EFI PART" at sector 1, so a disk with
# an MBR partition table and no GPT was written straight through and its
# partition table eaten. A bare FAT volume with no partition table at all has
# the same two bytes in the same place and is refused for the same reason.
#
# Sector 1 carries a sentinel so the test can say the store did not write over
# it, and sector 0's signature is checked afterwards for the same reason.

$mbr = New-Object byte[] (64 * $SectorSize)
$mbr[510] = 0x55
$mbr[511] = 0xAA
for ($i = 0; $i -lt 512; $i++) { $mbr[512 + $i] = 0xA5 }

$mbrOut = Join-Path $OutDir 'disk-facts-mbr-guard.disk'
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $mbrOut), $mbr)
Write-Host "wrote $mbrOut ($($mbr.Length) bytes)"
