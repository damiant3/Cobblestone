# cdx-to-elf.ps1 -- wrap a HOSTED CDX in a static Linux x86-64 ELF64 executable.
#
# The compiler emits position-dependent code patched against bare-metal-load-addr
# and, under the `hosted` mode flag, runtime cells offset by hosted-cell-base.
# Nothing here relocates anything: it declares the addresses the code was built
# for and lets the kernel map them.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CdxInput,
    [Parameter(Mandatory=$true)][string]$Out
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# These four must agree with the compiler. X86_64State.codex bare-metal-load-addr,
# CdxWriter.codex compute-data-vaddr-bare, X86_64Boot.codex hosted-cell-base,
# X86_64Chapter.codex bare-metal-heap-base.
$LoadAddr   = 1048576
$CellBase   = 131072
$HeapBase   = 6291456
$CellSpan   = 0x10000      # covers the relocated cells and the print scratch
$HeapSpan   = 3221225472L  # 3 GB, matching the bare-metal test VM envelope

$cdx = [System.IO.File]::ReadAllBytes($CdxInput)
function R64($off) { [BitConverter]::ToInt64($cdx, $off) }

$HeaderSize = 224
if ($cdx.Length -lt $HeaderSize) { throw "CDX is $($cdx.Length) bytes, shorter than its $HeaderSize-byte header" }
if ($cdx[0] -ne 67 -or $cdx[1] -ne 68 -or $cdx[2] -ne 88 -or $cdx[3] -ne 49) { throw "not a CDX1 file" }

$textOff = R64 168
$textSz  = R64 176
$rodOff  = R64 184
$rodSz   = R64 192
$entry   = R64 200

if ($textOff -ne $HeaderSize) { throw "text offset $textOff is not the fixed header size $HeaderSize" }
$textAligned = ($textSz + 7) -band -8
if ($rodSz -gt 0 -and $rodOff -ne $textOff + $textAligned) { throw "sections do not tile: rodata at $rodOff, text implies $($textOff + $textAligned)" }
foreach ($s in @(@{N='text';O=$textOff;S=$textSz}, @{N='rodata';O=$rodOff;S=$rodSz})) {
    if ($s.S -lt 0) { throw "$($s.N) size is negative" }
    if ($s.S -gt 0 -and $s.O + $s.S -gt $cdx.Length) { throw "$($s.N) claims bytes to $($s.O + $s.S), file is $($cdx.Length)" }
}

# Text and rodata are contiguous in the CDX and contiguous in memory, so keeping
# them contiguous in the file makes p_offset congruent to p_vaddr mod the page
# size for both without padding between them.
$content    = New-Object byte[] ($textAligned + $rodSz)
[Array]::Copy($cdx, $textOff, $content, 0, $textSz)
if ($rodSz -gt 0) { [Array]::Copy($cdx, $rodOff, $content, $textAligned, $rodSz) }

$PageSize    = 4096
$ehSize      = 64
$phEntSize   = 56
$phNum       = 3
$contentOff  = $PageSize
$fileLen     = $contentOff + $content.Length

$img = New-Object byte[] $fileLen
function PutBytes([byte[]]$v, [int]$at) { [Array]::Copy($v, 0, $img, $at, $v.Length) }
function Put16([int]$v, [int]$at)   { PutBytes ([BitConverter]::GetBytes([uint16]$v)) $at }
function Put32([int]$v, [int]$at)   { PutBytes ([BitConverter]::GetBytes([uint32]$v)) $at }
function Put64([long]$v, [int]$at)  { PutBytes ([BitConverter]::GetBytes([uint64]$v)) $at }

# ELF64 header
PutBytes ([byte[]]@(0x7f, 0x45, 0x4c, 0x46, 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0)) 0
Put16 2  16      # ET_EXEC
Put16 62 18      # EM_X86_64
Put32 1  20      # EV_CURRENT
Put64 ($LoadAddr + $entry) 24   # e_entry
Put64 $ehSize 32                # e_phoff
Put64 0 40                      # e_shoff
Put32 0 48                      # e_flags
Put16 $ehSize 52
Put16 $phEntSize 54
Put16 $phNum 56
Put16 0 58
Put16 0 60
Put16 0 62

$PF_X = 1; $PF_W = 2; $PF_R = 4
$PT_LOAD = 1
function Phdr([int]$i, [int]$flags, [long]$off, [long]$vaddr, [long]$filesz, [long]$memsz) {
    $b = $ehSize + $i * $phEntSize
    Put32 $PT_LOAD $b
    Put32 $flags ($b + 4)
    Put64 $off ($b + 8)
    Put64 $vaddr ($b + 16)
    Put64 $vaddr ($b + 24)
    Put64 $filesz ($b + 32)
    Put64 $memsz ($b + 40)
    Put64 $PageSize ($b + 48)
}

# The runtime cells, demand-zero, below the text so they can never collide with it.
Phdr 0 ($PF_R -bor $PF_W) 0 $CellBase 0 $CellSpan
# Text and rodata. Read plus execute; the emitted code does not write its rodata.
Phdr 1 ($PF_R -bor $PF_X) $contentOff $LoadAddr $content.Length $content.Length
# The arena, demand-zero.
Phdr 2 ($PF_R -bor $PF_W) 0 $HeapBase 0 $HeapSpan

PutBytes $content $contentOff
[System.IO.File]::WriteAllBytes($Out, $img)

Write-Host "[cdx-to-elf] entry 0x$(($LoadAddr + $entry).ToString('x'))  text $textSz  rodata $rodSz  file $fileLen bytes -> $Out"
