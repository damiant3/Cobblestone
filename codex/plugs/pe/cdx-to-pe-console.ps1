# cdx-to-pe-console.ps1 -- wrap a HOSTED-WINDOWS CDX in a console PE32+.
#
# The layout is FIXED and shared with the compiler: X86_64Boot.codex declares
# hosted-win-image-base, hosted-win-idata-rva, hosted-win-text-rva and the five
# IAT slot addresses, and the emitted code calls kernel32 through those exact
# addresses. This script asserts every one of them against what it builds,
# because a silent disagreement is a call into the wrong page rather than a
# diagnostic.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CdxInput,
    [Parameter(Mandatory=$true)][string]$Out
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Must equal the constants of the same name in codex/compiler/Emit/X86_64Boot.codex.
$ImageBase = 1048576      # 0x100000
$IdataRva  = 4096         # 0x1000
$TextRva   = 8192         # 0x2000
$IatSlots  = @{ GetStdHandle = 1052760; WriteFile = 1052768; ExitProcess = 1052776; VirtualAlloc = 1052784; ReadFile = 1052792 }
$Funcs     = @('GetStdHandle','WriteFile','ExitProcess','VirtualAlloc','ReadFile')   # ORDER IS LOAD-BEARING

$FILE_ALIGN = 512
$SECT_ALIGN = 4096

function AlignUp([int64]$v, [int64]$a) { if ($v % $a -eq 0) { $v } else { $v + ($a - ($v % $a)) } }

$cdx = [System.IO.File]::ReadAllBytes($CdxInput)
function R64($off) { [BitConverter]::ToInt64($cdx, $off) }
$HeaderSize = 224
if ($cdx.Length -lt $HeaderSize) { throw "CDX is $($cdx.Length) bytes, shorter than its header" }
if ($cdx[0] -ne 67 -or $cdx[1] -ne 68 -or $cdx[2] -ne 88 -or $cdx[3] -ne 49) { throw 'not a CDX1 file' }
$textOff = R64 168; $textSz = R64 176; $rodOff = R64 184; $rodSz = R64 192; $entry = R64 200
if ($textOff -ne $HeaderSize) { throw "text offset $textOff is not the header size $HeaderSize" }
$textAligned = ($textSz + 7) -band -8
if ($rodSz -gt 0 -and $rodOff -ne $textOff + $textAligned) { throw 'sections do not tile' }
if ($textSz -le 0 -or $textOff + $textSz -gt $cdx.Length) { throw 'text section is out of range' }

# Text and rodata are contiguous in memory, exactly as the ELF path lays them
# out, so one section carries both and compute-data-vaddr-at still holds.
$content = New-Object byte[] ($textAligned + $rodSz)
[Array]::Copy($cdx, $textOff, $content, 0, $textSz)
if ($rodSz -gt 0) { [Array]::Copy($cdx, $rodOff, $content, $textAligned, $rodSz) }

# --- .idata ------------------------------------------------------------------
$descSize = 40
$iltOff   = $descSize
$iltSize  = ($Funcs.Count + 1) * 8
$iatOff   = $iltOff + $iltSize
$iatSize  = $iltSize
$namesOff = $iatOff + $iatSize
$nameOffs = @{}
$cur = $namesOff
foreach ($f in $Funcs) { $nameOffs[$f] = $cur; $cur += AlignUp (2 + $f.Length + 1) 2 }
$dllOff = $cur
$cur += AlignUp ('kernel32.dll'.Length + 1) 2
$idataSize = $cur
if ($idataSize -gt $SECT_ALIGN) { throw "idata is $idataSize bytes and the layout reserves one $SECT_ALIGN-byte page" }

# THE ASSERTION THAT MAKES THE SHARED LAYOUT SAFE. The compiler emitted calls to
# these addresses; if this build would put the slots anywhere else, refuse.
for ($i = 0; $i -lt $Funcs.Count; $i++) {
    $want = $IatSlots[$Funcs[$i]]
    $got  = $ImageBase + $IdataRva + $iatOff + ($i * 8)
    if ($want -ne $got) { throw "IAT slot for $($Funcs[$i]) is $got here and $want in X86_64Boot.codex -- the two layouts have diverged" }
}

$idata = New-Object byte[] $idataSize
function PutInto([byte[]]$dst, [byte[]]$v, [int]$at) { [Array]::Copy($v, 0, $dst, $at, $v.Length) }
function B32([int64]$v) { [BitConverter]::GetBytes([uint32]($v -band 0xFFFFFFFFL)) }
function B64([int64]$v) { [BitConverter]::GetBytes([uint64]$v) }

PutInto $idata (B32 ($IdataRva + $iltOff))  0    # OriginalFirstThunk
PutInto $idata (B32 0)                      4    # TimeDateStamp
PutInto $idata (B32 0)                      8    # ForwarderChain
PutInto $idata (B32 ($IdataRva + $dllOff))  12   # Name
PutInto $idata (B32 ($IdataRva + $iatOff))  16   # FirstThunk
for ($i = 0; $i -lt $Funcs.Count; $i++) {
    $rva = $IdataRva + $nameOffs[$Funcs[$i]]
    PutInto $idata (B64 $rva) ($iltOff + $i * 8)
    PutInto $idata (B64 $rva) ($iatOff + $i * 8)
}
foreach ($f in $Funcs) {
    $at = $nameOffs[$f]
    PutInto $idata (B32 0) $at    # hint (2 bytes) then the name; a zero word is fine
    PutInto $idata ([System.Text.Encoding]::ASCII.GetBytes($f)) ($at + 2)
}
PutInto $idata ([System.Text.Encoding]::ASCII.GetBytes('kernel32.dll')) $dllOff

# --- layout ------------------------------------------------------------------
$sections = @(
    @{ Name = '.idata'; Rva = $IdataRva; Data = $idata;    Chars = 3221225536L },  # init data, R/W
    @{ Name = '.text';  Rva = $TextRva;  Data = $content;  Chars = 1610612768L }   # code, R/X
)
$hdrSize = AlignUp (0x40 + 0x40 + 0x18 + 0xF0 + ($sections.Count * 40)) $FILE_ALIGN
$fileOff = $hdrSize
foreach ($s in $sections) { $s.RawPtr = $fileOff; $s.RawSize = [int](AlignUp $s.Data.Length $FILE_ALIGN); $fileOff += $s.RawSize }
$fileLen   = $fileOff
$imageSize = [int](AlignUp ($TextRva + $content.Length) $SECT_ALIGN)

$img = New-Object byte[] $fileLen
function Put([byte[]]$v, [int]$at) { [Array]::Copy($v, 0, $img, $at, $v.Length) }
function P16([int]$v, [int]$at) { Put ([BitConverter]::GetBytes([uint16]$v)) $at }
function P32([int64]$v, [int]$at) { Put (B32 $v) $at }
function P64([int64]$v, [int]$at) { Put (B64 $v) $at }

Put ([byte[]]@(0x4D,0x5A)) 0          # MZ
P32 0x40 0x3C                          # e_lfanew
Put ([System.Text.Encoding]::ASCII.GetBytes("PE")) 0x40
$coff = 0x44
P16 0x8664 $coff                       # machine x86-64
P16 $sections.Count ($coff + 2)
P32 0 ($coff + 4)
P32 0 ($coff + 8)
P32 0 ($coff + 12)
P16 0xF0 ($coff + 16)                  # SizeOfOptionalHeader
P16 0x0022 ($coff + 18)                # EXECUTABLE_IMAGE | LARGE_ADDRESS_AWARE
$opt = $coff + 20
P16 0x20B $opt                         # PE32+
P32 $content.Length ($opt + 4)         # SizeOfCode
P32 $idataSize ($opt + 8)              # SizeOfInitializedData
P32 ($ImageBase + $TextRva + $entry - $ImageBase) ($opt + 16)   # AddressOfEntryPoint (RVA)
P32 $TextRva ($opt + 20)               # BaseOfCode
P64 $ImageBase ($opt + 24)
P32 $SECT_ALIGN ($opt + 32)
P32 $FILE_ALIGN ($opt + 36)
P16 6 ($opt + 40)                      # MajorOperatingSystemVersion
P16 0 ($opt + 42)
P16 0 ($opt + 44)
P16 0 ($opt + 46)
P16 6 ($opt + 48)                      # MajorSubsystemVersion
P16 0 ($opt + 50)
P32 0 ($opt + 52)
P32 $imageSize ($opt + 56)
P32 $hdrSize ($opt + 60)
P32 0 ($opt + 64)                      # CheckSum
P16 3 ($opt + 68)                      # Subsystem: CONSOLE
P16 0 ($opt + 70)                      # DllCharacteristics: no ASLR, we are position-dependent
P64 0x20000000 ($opt + 72)             # SizeOfStackReserve (512 MB; the compiler recurses past the 1 MB default)
P64 0x10000  ($opt + 80)               # SizeOfStackCommit
P64 0x100000 ($opt + 88)               # SizeOfHeapReserve
P64 0x1000   ($opt + 96)               # SizeOfHeapCommit
P32 0 ($opt + 104)                     # LoaderFlags
P32 16 ($opt + 108)                    # NumberOfRvaAndSizes
$dd = $opt + 112
P32 $IdataRva ($dd + 1 * 8); P32 $idataSize ($dd + 1 * 8 + 4)          # IMPORT
P32 ($IdataRva + $iatOff) ($dd + 12 * 8); P32 $iatSize ($dd + 12 * 8 + 4)  # IAT

$sh = $dd + 16 * 8
for ($i = 0; $i -lt $sections.Count; $i++) {
    $s = $sections[$i]; $o = $sh + $i * 40
    Put ([System.Text.Encoding]::ASCII.GetBytes($s.Name)) $o
    P32 $s.Data.Length ($o + 8)        # VirtualSize
    P32 $s.Rva ($o + 12)
    P32 $s.RawSize ($o + 16)
    P32 $s.RawPtr ($o + 20)
    P32 0 ($o + 24); P32 0 ($o + 28); P16 0 ($o + 32); P16 0 ($o + 34)
    P32 $s.Chars ($o + 36)
    Put $s.Data $s.RawPtr
}

[System.IO.File]::WriteAllBytes($Out, $img)
Write-Host "[cdx-to-pe-console] entry rva 0x$(($TextRva + $entry).ToString('x'))  va 0x$(($ImageBase + $TextRva + $entry).ToString('x'))  text $textSz  rodata $rodSz  file $fileLen bytes -> $Out"
