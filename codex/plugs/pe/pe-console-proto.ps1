# Prototype a Windows CONSOLE PE32+ with a kernel32 import table, so the layout
# is proven by RUNNING it before any of it is written in Codex. Iterating here
# costs a second; iterating in the plug costs a 40s module rebuild.
#
# THE ImageBase + textRva == 0x100000 TRICK DOES NOT WORK AND THIS FILE USED TO
# SAY IT DID. Swept 2026-08-29: 0x10000, 0x20000, 0x40000, 0x50000, 0x60000,
# 0x80000, 0xC0000, 0xE0000 and 0xF0000 are ALL refused with "not a valid
# application for this OS platform", against 0x100000 with textRva 0x1000 as a
# positive control that runs and prints. So a hosted-windows build is patched
# for its own load address instead; X86_64Boot.codex hosted-win-load-addr.
#
# The payload is deliberately trivial -- GetStdHandle, WriteFile, ExitProcess --
# because what is under test is the CONTAINER: subsystem 3, the data directories,
# and an import table Windows can actually resolve. If this runs and prints, the
# header arithmetic is right and porting it to PeWriter is mechanical.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Out = if ($args.Count -gt 0) { $args[0] } else { Join-Path $PSScriptRoot 'proto.exe' }

function I16([int]$v) { ,[byte[]]@(($v -band 255), (($v -shr 8) -band 255)) }
function I32([int64]$v) { ,[byte[]]@(($v -band 255), (($v -shr 8) -band 255), (($v -shr 16) -band 255), (($v -shr 24) -band 255)) }
function I64([int64]$v) { ,[byte[]]@(($v -band 255), (($v -shr 8) -band 255), (($v -shr 16) -band 255), (($v -shr 24) -band 255), (($v -shr 32) -band 255), (($v -shr 40) -band 255), (($v -shr 48) -band 255), (($v -shr 56) -band 255)) }
function Ascii([string]$s) { ,[byte[]][System.Text.Encoding]::ASCII.GetBytes($s) }
function AlignUp([int]$v, [int]$a) { if ($v % $a -eq 0) { $v } else { $v + ($a - ($v % $a)) } }

$FILE_ALIGN = 512
$SECT_ALIGN = 4096
$HDR_SIZE   = 1024        # room for 3 section headers
$IMAGE_BASE = 0x100000    # the LOWEST base Windows accepts; see the note above

# --- .idata layout, all offsets relative to the section's RVA ----------------
$funcs = @('GetStdHandle','WriteFile','ExitProcess','VirtualAlloc')
$descSize = 40                       # one descriptor + a null terminator
$iltOff   = $descSize
$iltSize  = ($funcs.Count + 1) * 8
$iatOff   = $iltOff + $iltSize
$iatSize  = $iltSize
$namesOff = $iatOff + $iatSize
$nameOffs = @{}
$cur = $namesOff
foreach ($f in $funcs) {
    $nameOffs[$f] = $cur
    $cur += AlignUp (2 + $f.Length + 1) 2   # hint + name + NUL, 2-aligned
}
$dllOff = $cur
$cur += AlignUp ('kernel32.dll'.Length + 1) 2
$idataSize = $cur

# --- .text: the payload, assembled by hand ----------------------------------
# Entry is a leaf that calls three imports through the IAT, so every call is
# `call [rip+disp32]` -- the indirection Windows fills in at load time, and the
# thing an ELF on Linux does not need.
$msg = Ascii "hello from a Codex-built Windows console exe`r`n"
$textRva = 0x1000         # first section above the headers
$idataRva = 0   # filled once the text size is known

# Two passes: the code references .idata and .rdata RVAs, so size it first with
# placeholders, then emit for real once every RVA is known.
function Build-Text([int]$idataRvaIn, [int]$msgRva, [int]$textRvaIn) {
    $c = New-Object 'System.Collections.Generic.List[byte]'
    # sub rsp, 40  (shadow space + alignment)
    $c.AddRange([byte[]]@(0x48,0x83,0xEC,0x28))
    # mov ecx, -11  (STD_OUTPUT_HANDLE)
    $c.AddRange([byte[]]@(0xB9)); $c.AddRange((I32 -11))
    # call [rip+disp] -> GetStdHandle
    $c.AddRange([byte[]]@(0xFF,0x15))
    $here = $textRvaIn + $c.Count + 4
    $c.AddRange((I32 ($idataRvaIn + $iatOff + 0 - $here)))
    # mov rcx, rax   (handle)
    $c.AddRange([byte[]]@(0x48,0x89,0xC1))
    # lea rdx, [rip+disp] -> message
    $c.AddRange([byte[]]@(0x48,0x8D,0x15))
    $here = $textRvaIn + $c.Count + 4
    $c.AddRange((I32 ($msgRva - $here)))
    # mov r8d, len
    $c.AddRange([byte[]]@(0x41,0xB8)); $c.AddRange((I32 $msg.Length))
    # lea r9, [rsp+32]  (lpNumberOfBytesWritten)
    $c.AddRange([byte[]]@(0x4C,0x8D,0x4C,0x24,0x20))
    # mov qword [rsp+32], 0
    $c.AddRange([byte[]]@(0x48,0xC7,0x44,0x24,0x20,0x00,0x00,0x00,0x00))
    # WriteFile takes a 5th arg on the stack: mov qword [rsp+32], 0 already set;
    # the overlap is deliberate -- lpOverlapped NULL goes at [rsp+32].
    $c.AddRange([byte[]]@(0xFF,0x15))
    $here = $textRvaIn + $c.Count + 4
    $c.AddRange((I32 ($idataRvaIn + $iatOff + 8 - $here)))
    # xor ecx, ecx ; call ExitProcess
    $c.AddRange([byte[]]@(0x31,0xC9))
    $c.AddRange([byte[]]@(0xFF,0x15))
    $here = $textRvaIn + $c.Count + 4
    $c.AddRange((I32 ($idataRvaIn + $iatOff + 16 - $here)))
    # int3 (never reached)
    $c.Add(0xCC)
    return ,$c.ToArray()
}

$probe = Build-Text 0 0 $textRva
$textSize = [int]($probe.Length + $msg.Length)          # message rides in .text
$msgRva = $textRva + $probe.Length
$idataRva = $textRva + (AlignUp $textSize $SECT_ALIGN)
$tcode = Build-Text $idataRva $msgRva $textRva
$tl = New-Object 'System.Collections.Generic.List[byte]'
$tl.AddRange($tcode); $tl.AddRange($msg); $text = $tl.ToArray()

# --- .idata bytes -----------------------------------------------------------
$idata = New-Object 'System.Collections.Generic.List[byte]'
$idata.AddRange((I32 ($idataRva + $iltOff)))     # OriginalFirstThunk
$idata.AddRange((I32 0)); $idata.AddRange((I32 0))
$idata.AddRange((I32 ($idataRva + $dllOff)))     # Name
$idata.AddRange((I32 ($idataRva + $iatOff)))     # FirstThunk
$idata.AddRange((New-Object 'byte[]' 20))        # null descriptor
foreach ($tbl in 1..2) {
    foreach ($f in $funcs) { $idata.AddRange((I64 ($idataRva + $nameOffs[$f]))) }
    $idata.AddRange((New-Object 'byte[]' 8))
}
foreach ($f in $funcs) {
    $e = New-Object 'System.Collections.Generic.List[byte]'
    $e.AddRange((I16 0)); $e.AddRange((Ascii $f)); $e.Add(0)
    while ($e.Count % 2 -ne 0) { $e.Add(0) }
    $idata.AddRange($e)
}
$d = New-Object 'System.Collections.Generic.List[byte]'
$d.AddRange((Ascii 'kernel32.dll')); $d.Add(0)
while ($d.Count % 2 -ne 0) { $d.Add(0) }
$idata.AddRange($d)

$textRaw = AlignUp $text.Length $FILE_ALIGN
$idataRaw = AlignUp $idata.Count $FILE_ALIGN
$imageSize = $idataRva + (AlignUp $idata.Count $SECT_ALIGN)

# --- headers ----------------------------------------------------------------
$h = New-Object 'System.Collections.Generic.List[byte]'
$dos = New-Object 'byte[]' 64
$dos[0] = 0x4D; $dos[1] = 0x5A; $dos[60] = 64
$h.AddRange($dos)
$h.AddRange((Ascii 'PE')); $h.AddRange([byte[]]@(0,0))
$h.AddRange((I16 0x8664)); $h.AddRange((I16 2))          # machine, 2 sections
$h.AddRange((I32 0)); $h.AddRange((I32 0)); $h.AddRange((I32 0))
$h.AddRange((I16 240)); $h.AddRange((I16 0x0022))        # opt size, EXECUTABLE|LARGE_ADDRESS_AWARE
$h.AddRange((I16 0x20B)); $h.AddRange([byte[]]@(0,0))    # PE32+
$h.AddRange((I32 $textRaw)); $h.AddRange((I32 $idataRaw)); $h.AddRange((I32 0))
$h.AddRange((I32 $textRva))                              # entry point RVA
$h.AddRange((I32 $textRva))                              # base of code
$h.AddRange((I64 $IMAGE_BASE))
$h.AddRange((I32 $SECT_ALIGN)); $h.AddRange((I32 $FILE_ALIGN))
$h.AddRange((I16 6)); $h.AddRange((I16 0))               # OS ver
$h.AddRange((I16 0)); $h.AddRange((I16 0))
$h.AddRange((I16 6)); $h.AddRange((I16 0))               # subsystem ver
$h.AddRange((I32 0))
$h.AddRange((I32 $imageSize)); $h.AddRange((I32 $HDR_SIZE))
$h.AddRange((I32 0))
$h.AddRange((I16 3)); $h.AddRange((I16 0x0100))          # SUBSYSTEM 3 = console, NX only (no DYNAMIC_BASE: no .reloc, so the base must be honoured)
$h.AddRange((I64 0x100000)); $h.AddRange((I64 0x1000))   # stack reserve/commit
$h.AddRange((I64 0x100000)); $h.AddRange((I64 0x1000))   # heap reserve/commit
$h.AddRange((I32 0)); $h.AddRange((I32 16))              # loader flags, dir count
$h.AddRange((I64 0))                                      # [0] export
$h.AddRange((I32 $idataRva)); $h.AddRange((I32 $descSize)) # [1] IMPORT
for ($i = 2; $i -le 11; $i++) { $h.AddRange((I64 0)) }
$h.AddRange((I32 ($idataRva + $iatOff))); $h.AddRange((I32 $iatSize)) # [12] IAT
for ($i = 13; $i -le 15; $i++) { $h.AddRange((I64 0)) }

function Sect([string]$n, [int]$vsize, [int]$rva, [int]$rsize, [int]$roff, [int64]$chars) {
    $b = New-Object 'System.Collections.Generic.List[byte]'
    $nm = New-Object 'byte[]' 8
    $src = Ascii $n
    [Array]::Copy($src, $nm, [Math]::Min(8, $src.Length))
    $b.AddRange($nm)
    $b.AddRange((I32 $vsize)); $b.AddRange((I32 $rva))
    $b.AddRange((I32 $rsize)); $b.AddRange((I32 $roff))
    $b.AddRange((I32 0)); $b.AddRange((I32 0))
    $b.AddRange((I16 0)); $b.AddRange((I16 0))
    $b.AddRange((I32 $chars))
    return ,$b.ToArray()
}
$h.AddRange((Sect '.text'  $text.Length  $textRva  $textRaw  $HDR_SIZE            0x60000020))
$h.AddRange((Sect '.idata' $idata.Count  $idataRva $idataRaw ($HDR_SIZE+$textRaw) 0xC0000040))


$file = New-Object 'System.Collections.Generic.List[byte]'
$file.AddRange($h.ToArray())
$file.AddRange((New-Object 'byte[]' ($HDR_SIZE - $h.Count)))
$file.AddRange($text)
$file.AddRange((New-Object 'byte[]' ($textRaw - $text.Length)))
$file.AddRange($idata.ToArray())
$file.AddRange((New-Object 'byte[]' ($idataRaw - $idata.Count)))
[IO.File]::WriteAllBytes($Out, $file.ToArray())
Write-Host ("wrote {0} ({1:N0} bytes) text={2} idataRva=0x{3:X} imageSize=0x{4:X}" -f $Out, $file.Count, $text.Length, $idataRva, $imageSize)
