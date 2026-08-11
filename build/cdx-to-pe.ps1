# Convert a CDX binary to a PE32+ UEFI application.
#
# Usage: cdx-to-pe.ps1 -CdxInput <file.cdx> -Out <file.efi> [-HeapPages <n>]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$CdxInput,
    [Parameter(Mandatory=$true)] [string]$Out,
    [int]$HeapPages = 512,
    # Call GetMemoryMap + ExitBootServices (one stale-key retry) after the last
    # boot-services use, then cli and zero the SystemTable cells. This is what
    # the retired option_a_stub.asm always did and this stub never did, and the
    # difference is not academic: with boot services alive the FIRMWARE'S OWN
    # xHCI driver keeps driving the same controller our driver is bringing up,
    # so a keyboard probe measures two drivers fighting, not ours. Payloads
    # that need ConIn/ConOut (KeyProof, the dev console) must NOT set this;
    # driver-truth probes (KbdDiagProbe, XhciTruthProbe, MscAlignProbe) must.
    [switch]$ExitBootServices,
    # Enter at __start so the bare-metal runtime init runs. Required by any\n    # payload that reads a disk through block-read-sector; see the block below.
    [switch]$EntryStart,
    # Text to pre-load into the serial input ring, so a payload that reads stdin\n    # can run on a board with no serial port. See the block near BivySaveAddr.
    [string]$Stdin = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$cdx = [System.IO.File]::ReadAllBytes($CdxInput)
function R32($off) { [BitConverter]::ToInt32($cdx, $off) }
function R64($off) { [BitConverter]::ToInt64($cdx, $off) }

$textOff   = R64 168
$textSz    = R64 176
$rodataOff = R64 184
$rodataSz  = R64 192
$entryOff  = R64 200     # __start offset (text-relative)
$debugOff  = R32 220

# --- Look symbols up in the CDX's own embedded debug map (MAP1) ---
#
# The embedded map is the authoritative one. seed/Codex.map is NOT: the seed is
# built -Repl and -Repl emits no text MAP block, so nothing refreshes that file.
# Measured 2026-07-27 against seed/Codex.cdx, they disagree outright --
# embedded 0x22067E/2257 against text 0x21E1F0/1747 for __syscall_handler.
#
# Names in the map are CCE, not ASCII. The tier-0 table is frequency-ordered
# (codex/foreword/core/CCE.codex), so the letters are nowhere near their ASCII
# values: 'a' is 15, 'e' is 13, '_' is 85.
$CceTier0 = @(
    0, 10, 32,
    48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
    101, 116, 97, 111, 105, 110, 115, 104, 114, 100,
    108, 99, 117, 109, 119, 102, 103, 121, 112, 98,
    118, 107, 106, 120, 113, 122,
    69, 84, 65, 79, 73, 78, 83, 72, 82, 68,
    76, 67, 85, 77, 87, 70, 71, 89, 80, 66,
    86, 75, 74, 88, 81, 90,
    46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,
    43, 61, 42, 60, 62,
    47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,
    94,
    36, 37
)
$UniToCce = @{}
for ($i = 0; $i -lt $CceTier0.Count; $i++) {
    if (-not $UniToCce.ContainsKey($CceTier0[$i])) { $UniToCce[$CceTier0[$i]] = $i }
}

$mapCount = 0; $stringsBase = 0; $entriesBase = 0
if ($debugOff -gt 0 -and $debugOff -lt $cdx.Length) {
    $mapCount = R32 ($debugOff + 4)
    $stringsBase = $debugOff + (R32 ($debugOff + 8))
    $entriesBase = $debugOff + 12
}

function Find-FuncOffset([string]$name) {
    if ($mapCount -le 0) { return -1 }
    $pat = New-Object byte[] $name.Length
    for ($k = 0; $k -lt $name.Length; $k++) {
        $u = [int]$name[$k]
        if (-not $UniToCce.ContainsKey($u)) { return -1 }
        $pat[$k] = [byte]$UniToCce[$u]
    }
    # The string table is NUL-terminated entries; require the terminator so a
    # name that is a prefix of another ('opening' inside 'manifest-find-opening'
    # would not match anyway, but 'sha256' inside 'sha256-compress' would) does
    # not resolve to the wrong function.
    $strPos = -1
    for ($i = $stringsBase; $i -lt $cdx.Length - $pat.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $pat.Length -and $match; $j++) {
            if ($cdx[$i+$j] -ne $pat[$j]) { $match = $false }
        }
        if ($match -and ($i + $pat.Length -ge $cdx.Length -or $cdx[$i + $pat.Length] -eq 0)) {
            $strPos = $i - $stringsBase
            break
        }
    }
    if ($strPos -lt 0) { return -1 }
    for ($i = 0; $i -lt $mapCount; $i++) {
        $eOff = $entriesBase + $i * 12
        if ((R32 ($eOff + 8)) -eq $strPos) { return R32 $eOff }
    }
    return -1
}

# Entry point: `opening` by default, `__start` under -EntryStart.
#
# `__start` (emit-start, X86_64Chapter.codex) is the bare-metal runtime init:
# GDT, page tables, CR3, the syscall MSR, and -- the one that matters here --
# emit-ata-init at X86_64Chapter.codex:423. A payload entered at `opening`
# NEVER RUNS ANY OF IT, so every block-read-sector answers -1. Measured
# 2026-08-08: the compiler in DISK mode entered at `opening` triple-faults in
# fat16-find-in-root with CR2=0 on the unchecked -1, and entered at `__start`
# compiles from the volume and emits a CDX byte-identical to the host.
#
# The default stays `opening` because the shipping GOP payloads work that way
# and do not use this block layer at all -- they read the stick through their
# own USB mass-storage driver. Pass -EntryStart for a payload that uses the
# compiler's own disk path.
if ($EntryStart) {
    $openingFuncOff = $entryOff
    Write-Host "[cdx-to-pe] entry: __start (bare-metal init runs)"
} else {
    $openingFuncOff = Find-FuncOffset 'opening'
    if ($openingFuncOff -lt 0) {
        Write-Host "[cdx-to-pe] WARN: 'opening' not found in debug map, using __start"
        $openingFuncOff = $entryOff
    }
}
# The syscall dispatcher. This is NOT optional and there is no fallback: every
# disk read, every key read and every console write in a Codex program is a
# `syscall`, so a stub that does not program IA32_LSTAR produces a binary that
# triple-faults on the first one. That is exactly what the UEFI dev console did
# from the first image ever built until 2026-07-27 -- it reached `opening`, went
# straight to read-superblock -> block-read-sector, issued `syscall` against an
# unprogrammed MSR, landed at 0xa016, executed zeroes and wrote to address 0.
# Emitting a stub without it would be shipping that bug again silently.
$syscallFuncOff = Find-FuncOffset '__syscall_handler'
if ($syscallFuncOff -lt 0) {
    throw "[cdx-to-pe] __syscall_handler not found in the CDX debug map. The stub cannot program IA32_LSTAR, and a UEFI binary without it faults on its first syscall. Refusing to emit."
}

Write-Host "[cdx-to-pe] text=$textSz @ $textOff  rodata=$rodataSz @ $rodataOff  opening=0x$($openingFuncOff.ToString('X'))  __syscall_handler=0x$($syscallFuncOff.ToString('X'))"

$FileAlign    = 512
$SectionAlign = 4096
$HeaderSize   = 512
$ImageBase    = 0x100000

$textAligned = ($textSz + 7) -band -8
$dataVaddr = $ImageBase + $textAligned

# Memory layout constants (must match X86_64Boot.codex)
#
# $HeapCeiling MUST match `bare-metal-ram-size` in X86_64State.codex, and the
# `L` is load-bearing: PowerShell parses 0xC0000000 as Int32, which is
# NEGATIVE, and [long] then sign-extends it to 0xFFFFFFFFC0000000. Firmware
# reads that as a ceiling above all memory and ignores it, so the allocation
# silently goes top-down again and the bug looks unfixed. Measured 2026-08-08.
#
# The ceiling exists because the payload builds its own PML4 at 0x8000 and
# emit-start loads CR3 unconditionally: bare-metal-pd-count RAM PDs cover
# [0, 3 GB) write-back, plus ONE device PD over [3 GB, 4 GB) with PCD and NX.
# A heap above 3 GB is unmapped or uncacheable the moment CR3 loads, whatever
# the firmware thought it gave us.
$HeapCeiling = 0xC0000000L      # bare-metal-ram-size
$DeckPosAddr = 28720            # 0x7030
$DeckBoundCounterAddr = 28904   # 0x70E8
$HeapHwmAddr = 28728            # 0x7038
$BivySaveAddr = 28912           # 0x70F0
$SysTableAddr = 0x8000
# uefi-systab-addr, read by every uefi-* function. Moved off 36208 on
# 2026-08-09: that address is 0x8D70, which is inside the 4 KB PML4 the
# bare-metal init builds at 0x8000, so under -EntryStart the write below was
# zeroed by the page-table build before any helper could read it. Must match
# X86_64Boot.codex.
$UefiSystabAddr = 30704

# --- Assemble the UEFI app stub ---
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)

# Refuse to continue on a failed AllocatePages, and say which one failed.
#
# Both allocations below are load-bearing and neither has a recovery: the code
# pages ARE the address the guest is linked for, and the heap pages are its
# stack. Carrying on past a failure means writing into memory the firmware still
# owns, which is how this stub bricked a real board (CL 2019) and how it
# reboot-looped under edk2 until 2026-07-29.
#
# The byte goes to both UARTs deliberately, and it is cheap insurance rather
# than a claim about where anything else prints. This stub runs before the guest
# has said a word, so if it halts here the operator has whatever ports they
# happened to attach and nothing else. The reboot loop was recorded as "no
# output of ours on COM1" for a day because the only diagnostic being emitted at
# the time went to the port nobody was capturing.
function AllocPanic([char]$c) {
    $bw.Write([byte]0xB0); $bw.Write([byte][int]$c)   # mov al, c
    $bw.Write([byte[]]@(0x66, 0xBA, 0xF8, 0x03))      # mov dx, 0x3F8
    $bw.Write([byte]0xEE)                             # out dx, al
    $bw.Write([byte[]]@(0x66, 0xBA, 0xF8, 0x02))      # mov dx, 0x2F8
    $bw.Write([byte]0xEE)                             # out dx, al
    $bw.Write([byte]0xF4)                             # hlt
}
function AssertAllocOk([char]$c) {
    $bw.Write([byte[]]@(0x48, 0x85, 0xC0))            # test rax, rax
    $bw.Write([byte[]]@(0x74, 0x0D))                  # jz +13 (over the panic block)
    AllocPanic $c
}

# PROGRESS marks, the counterpart to the panic bytes above.
#
# Lowercase is progress, uppercase is a halt. Both UARTs, for the reason the
# comment above gives: the operator has whatever port they happened to attach.
# The marks work on both sides of ExitBootServices, which is why the sequence
# rather than the colours below is the instrument of record on a bed with a
# serial port.
function MarkBytes([char]$c) {
    return [byte[]]@(
        0xB0, [byte][int]$c,          # mov al, c
        0x66, 0xBA, 0xF8, 0x03,       # mov dx, 0x3F8
        0xEE,                         # out dx, al
        0x66, 0xBA, 0xF8, 0x02,       # mov dx, 0x2F8
        0xEE                          # out dx, al
    )
}
function Mark([char]$c) { $bw.Write([byte[]](MarkBytes $c)) }

# The camera-readable half of the same signal.
#
# The marks above go to the two UARTs, and the machine this image is FOR has no
# serial port: the sitting's telemetry rig is a phone camera pointed at the
# glass. So on the hardware that matters the marks are invisible, and a black
# screen still cannot distinguish "firmware never started us" from "we started
# and died" from "we handed off and the guest said nothing" -- three states with
# three different next actions. The retired option_a_stub.asm solved this with
# two solid framebuffer fills (main 12073) while this stub had nothing, which is
# why the marks were brought over here before that .asm was deleted.
#
# Same two colours at the same two points, so the one table serves every image:
#   firmware screen unchanged  never loaded, or LocateProtocol(GOP) failed
#   DARK BLUE                  we have a framebuffer, died inside the stub
#   DARK GREEN                 stub finished, control passed to the guest
#   text over dark green       the guest is alive and ConOut works
#
# EFI_GRAPHICS_OUTPUT_PROTOCOL_GUID 9042a9de-23dc-4a38-96fb-7aded080516a, laid
# out as raw bytes rather than composed from two qword literals, so no
# endianness reasoning is involved in reading it against the spec.
$GopGuid = [byte[]]@(
    0xDE, 0xA9, 0x42, 0x90, 0xDC, 0x23, 0x38, 0x4A,
    0x96, 0xFB, 0x7A, 0xDE, 0xD0, 0x80, 0x51, 0x6A
)

# The framebuffer handoff block (BootRoadmap B5.4). 0x8000 cannot hold it: the
# PML4 lands there and emit-start snapshots it away, so the durable address is
# this one, in the hole between the IST stacks and the AP idle stacks. Layout
# and the reasoning are in BootRoadmap.md; the magic is what lets a payload
# booted by a stub that does NOT publish it say so instead of reading whatever
# happens to be at the address.
$HandoffAddr = 0x1F000

# Offsets into the protocol, which are the spec's to choose and not ours:
#   GOP  + 0x18 -> Mode
#   Mode + 0x08 -> Info,  Mode + 0x18 -> FrameBufferBase
#   Info + 0x08 -> VerticalResolution,  Info + 0x20 -> PixelsPerScanLine
# BootServices + 0x140 is LocateProtocol. The fill counts
# VRes * PixelsPerScanLine, not VRes * HRes: on a padded-scanline panel (the
# real board reports stride 2048 against a visible 1920) the second one covers
# a fraction of the screen and leaves a stripe.
#
# The two values live in R12 and R13, not in stack slots, and that is not a
# style choice. This stub REPLACES RSP with the top of the heap allocation
# before it hands off, so any `[rsp+N]` written before that line reads a
# different address after it -- measured 2026-07-30: the first version of this
# used two frame slots, the blue fill (before the swap) painted, and the green
# fill (after it) read the guest's untouched stack, got zero, and skipped
# itself. R12 and R13 are callee-saved in the MS x64 ABI, so the firmware calls
# in between preserve them, and the prolog pushes both and the stub uses
# neither.
#
# LocateProtocol(GOP), then keep FrameBufferBase and the pixel count. Called
# once, before anything that can fail. A firmware without GOP leaves R12 zero
# and every later fill becomes a no-op, so this is additive: nothing that
# worked before now depends on it.
function GopAcquire() {
    $body = [System.Collections.Generic.List[byte]]::new()
    $body.AddRange([byte[]]@(0x48, 0x8B, 0x44, 0x24, 0x28))   # mov rax, [rsp+0x28]  (gop)
    $body.AddRange([byte[]]@(0x48, 0x8B, 0x40, 0x18))         # mov rax, [rax+0x18]  (Mode)
    $body.AddRange([byte[]]@(0x48, 0x8B, 0x48, 0x08))         # mov rcx, [rax+0x08]  (Info)
    $body.AddRange([byte[]]@(0x4C, 0x8B, 0x60, 0x18))         # mov r12, [rax+0x18]  (FrameBufferBase)
    $body.AddRange([byte[]]@(0x44, 0x8B, 0x69, 0x08))         # mov r13d, [rcx+0x08]  (VRes)
    $body.AddRange([byte[]]@(0x8B, 0x41, 0x20))               # mov eax, [rcx+0x20]  (PixelsPerScanLine)
    $body.AddRange([byte[]]@(0x44, 0x0F, 0xAF, 0xE8))         # imul r13d, eax

    # The framebuffer half of the handoff block, published while rcx still
    # holds Info and r12 the framebuffer base. rdi is dead here (the prolog
    # pushed it, and GopFill's note records rax/rcx/rdi as dead at both call
    # sites). The header and the ACPI field are written outside this body:
    # they do not depend on GOP, and this body is reached by a rel8 jnz whose
    # budget is 127 bytes.
    $body.AddRange([byte[]]@(0xBF))                           # mov edi, HandoffAddr
    $body.AddRange([BitConverter]::GetBytes([int]$HandoffAddr))
    $body.AddRange([byte[]]@(0x4C, 0x89, 0x67, 0x10))         # mov [rdi+0x10], r12   fb base
    $body.AddRange([byte[]]@(0x8B, 0x41, 0x04))               # mov eax, [rcx+0x04]   HRes
    $body.AddRange([byte[]]@(0x89, 0x47, 0x18))               # mov [rdi+0x18], eax
    $body.AddRange([byte[]]@(0x8B, 0x41, 0x08))               # mov eax, [rcx+0x08]   VRes
    $body.AddRange([byte[]]@(0x89, 0x47, 0x1C))               # mov [rdi+0x1C], eax
    $body.AddRange([byte[]]@(0x8B, 0x41, 0x20))               # mov eax, [rcx+0x20]   PixelsPerScanLine
    $body.AddRange([byte[]]@(0x89, 0x47, 0x20))               # mov [rdi+0x20], eax
    $body.AddRange([byte[]]@(0x8B, 0x41, 0x0C))               # mov eax, [rcx+0x0C]   PixelFormat
    $body.AddRange([byte[]]@(0x89, 0x47, 0x24))               # mov [rdi+0x24], eax

    $body.AddRange([byte[]](MarkBytes 'v'))                   # 'v' = video acquired

    $bw.Write([byte[]]@(0x4D, 0x31, 0xE4))                    # xor r12, r12
    $bw.Write([byte[]]@(0x4D, 0x31, 0xED))                    # xor r13, r13
    $bw.Write([byte[]]@(0x48, 0xB8)); $bw.Write($GopGuid, 8, 8)  # mov rax, guid[8..15]
    $bw.Write([byte]0x50)                                     # push rax
    $bw.Write([byte[]]@(0x48, 0xB8)); $bw.Write($GopGuid, 0, 8)  # mov rax, guid[0..7]
    $bw.Write([byte]0x50)                                     # push rax
    $bw.Write([byte[]]@(0x48, 0x89, 0xE1))                    # mov rcx, rsp  (&guid)
    $bw.Write([byte[]]@(0x48, 0x83, 0xEC, 0x30))              # sub rsp, 0x30
    $bw.Write([byte[]]@(0x4C, 0x8D, 0x44, 0x24, 0x28))        # lea r8, [rsp+0x28]
    $bw.Write([byte[]]@(0x48, 0x31, 0xD2))                    # xor rdx, rdx
    $bw.Write([byte[]]@(0x49, 0x8B, 0x47, 0x60))              # mov rax, [r15+0x60]
    $bw.Write([byte[]]@(0xFF, 0x90, 0x40, 0x01, 0x00, 0x00))  # call [rax+0x140]
    $bw.Write([byte[]]@(0x48, 0x85, 0xC0))                    # test rax, rax
    if ($body.Count -gt 127) { throw "[cdx-to-pe] GopAcquire body outgrew a rel8 branch" }
    $bw.Write([byte[]]@(0x75, [byte]$body.Count))             # jnz over the body
    $bw.Write($body.ToArray())
    $bw.Write([byte[]]@(0x48, 0x83, 0xC4, 0x40))              # add rsp, 0x40
}

# Fill the whole framebuffer with one colour. No firmware call, so it cannot
# clobber r10 (caller-saved, and the deck pointer the guest runs on -- see the
# reload below); rax/rcx/rdi are dead at both call sites.
function GopFill([int]$colour) {
    $body = [System.Collections.Generic.List[byte]]::new()
    $body.AddRange([byte[]]@(0x4C, 0x89, 0xE7))               # mov rdi, r12
    $body.AddRange([byte[]]@(0x44, 0x89, 0xE9))               # mov ecx, r13d
    $body.Add(0xB8)                                           # mov eax, colour
    $body.AddRange([BitConverter]::GetBytes([int]$colour))
    $body.Add(0xFC)                                           # cld
    $body.AddRange([byte[]]@(0xF3, 0xAB))                     # rep stosd

    $bw.Write([byte[]]@(0x4D, 0x85, 0xE4))                    # test r12, r12
    if ($body.Count -gt 127) { throw "[cdx-to-pe] GopFill body outgrew a rel8 branch" }
    $bw.Write([byte[]]@(0x74, [byte]$body.Count))             # jz over the body
    $bw.Write($body.ToArray())
}
$GopDarkBlue = 0x00202060
$GopDarkGreen = 0x00104020

# The block header, written unconditionally so the magic is present whenever
# this stub ran at all. Every payload field is zeroed first: a reader that
# finds the magic knows the block is ours, and a zero fb_base or acpi_rsdp
# then means "the stub looked and did not find one" rather than "nobody
# wrote here". That distinction is the whole point of the magic.
function HandoffInit() {
    $bw.Write([byte[]]@(0xBF)); $bw.Write([BitConverter]::GetBytes([int]$HandoffAddr))
    $bw.Write([byte[]]@(0x31, 0xC0))                          # xor eax, eax
    $bw.Write([byte[]]@(0x48, 0x89, 0x47, 0x10))              # mov [rdi+0x10], rax  fb base
    $bw.Write([byte[]]@(0x48, 0x89, 0x47, 0x18))              # mov [rdi+0x18], rax  w, h
    $bw.Write([byte[]]@(0x48, 0x89, 0x47, 0x20))              # mov [rdi+0x20], rax  stride, format
    $bw.Write([byte[]]@(0x48, 0x89, 0x47, 0x28))              # mov [rdi+0x28], rax  acpi rsdp
    $bw.Write([byte[]]@(0x48, 0xB8)); $bw.Write([Text.Encoding]::ASCII.GetBytes('CDXHANDF'))
    $bw.Write([byte[]]@(0x48, 0x89, 0x07))                    # mov [rdi+0x00], rax  magic
    $bw.Write([byte[]]@(0xB8, 0x01, 0x00, 0x00, 0x00))        # mov eax, 1
    $bw.Write([byte[]]@(0x89, 0x47, 0x08))                    # mov [rdi+0x08], eax  version
    $bw.Write([byte[]]@(0xB8, 0x30, 0x00, 0x00, 0x00))        # mov eax, 48
    $bw.Write([byte[]]@(0x89, 0x47, 0x0C))                    # mov [rdi+0x0C], eax  size
}

# EFI_ACPI_20_TABLE_GUID 8868e871-e4f1-11d3-bc22-0080c73c8881, in EFI_GUID's
# own mixed-endian byte order so the two qword compares below need no
# reasoning about it. GopAcpi reads the RSDP from the OTHER stub's block at
# 0x8000+40; publishing it here is what lets that chapter work under this one.
$Acpi20Guid = [byte[]]@(
    0x71, 0xE8, 0x68, 0x88, 0xF1, 0xE4, 0xD3, 0x11,
    0xBC, 0x22, 0x00, 0x80, 0xC7, 0x3C, 0x88, 0x81
)

# Walk SystemTable->ConfigurationTable: count at +0x68, array at +0x70, each
# entry a 16-byte GUID then an 8-byte pointer. Runs before ExitBootServices,
# which is the only window in which that table is valid. Deliberately NOT
# inside GopAcquire: it needs r15 and nothing from GOP, and that body is
# reached by a rel8 jnz with a 127-byte budget.
function AcpiPublish() {
    $bw.Write([byte[]]@(0x49, 0x8B, 0x4F, 0x68))              # mov rcx, [r15+0x68]  count
    $bw.Write([byte[]]@(0x49, 0x8B, 0x57, 0x70))              # mov rdx, [r15+0x70]  table
    $bw.Write([byte[]]@(0x49, 0xB8)); $bw.Write($Acpi20Guid, 0, 8)   # mov r8, guid[0..7]
    $bw.Write([byte[]]@(0x49, 0xB9)); $bw.Write($Acpi20Guid, 8, 8)   # mov r9, guid[8..15]

    # Displacements are hand-computed against the layout below; the assertions
    # after it are what keep them honest if anything here is edited.
    $loop = [System.Collections.Generic.List[byte]]::new()
    $loop.AddRange([byte[]]@(0x48, 0x85, 0xC9))               # 0  test rcx, rcx
    $loop.AddRange([byte[]]@(0x74, 0x23))                     # 3  jz done   (+35)
    $loop.AddRange([byte[]]@(0x4C, 0x39, 0x02))               # 5  cmp [rdx], r8
    $loop.AddRange([byte[]]@(0x75, 0x15))                     # 8  jne next  (+21)
    $loop.AddRange([byte[]]@(0x4C, 0x39, 0x4A, 0x08))         # 10 cmp [rdx+8], r9
    $loop.AddRange([byte[]]@(0x75, 0x0F))                     # 14 jne next  (+15)
    $loop.AddRange([byte[]]@(0x48, 0x8B, 0x42, 0x10))         # 16 mov rax, [rdx+0x10]
    $loop.AddRange([byte[]]@(0xBF))                           # 20 mov edi, HandoffAddr
    $loop.AddRange([BitConverter]::GetBytes([int]$HandoffAddr))
    $loop.AddRange([byte[]]@(0x48, 0x89, 0x47, 0x28))         # 25 mov [rdi+0x28], rax
    $loop.AddRange([byte[]]@(0xEB, 0x09))                     # 29 jmp done  (+9)
    $loop.AddRange([byte[]]@(0x48, 0x83, 0xC2, 0x18))         # 31 next: add rdx, 24
    $loop.AddRange([byte[]]@(0x48, 0xFF, 0xC9))               # 35 dec rcx
    $loop.AddRange([byte[]]@(0xEB, 0xD8))                     # 38 jmp loop  (-40)
    if ($loop.Count -ne 40) { throw "[cdx-to-pe] ACPI scan is $($loop.Count) bytes, displacements assume 40" }
    $bw.Write($loop.ToArray())
}

# Prolog: save all callee-saved regs
$bw.Write([byte[]]@(
    0x55,                         # push rbp
    0x48, 0x89, 0xE5,             # mov rbp, rsp
    0x53,                         # push rbx
    0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57,  # push r12-r15
    0x56, 0x57,                   # push rsi, rdi
    0x48, 0x83, 0xEC, 0x28,       # sub rsp, 0x28
    0x49, 0x89, 0xD7,             # mov r15, rdx (SystemTable)
    0x49, 0x89, 0xCE              # mov r14, rcx (ImageHandle)
))

# 's' -- firmware loaded us and we are executing. This is the byte whose
# ABSENCE is the whole finding: no 's' means the payload never ran, which is a
# boot-selection or medium problem and not a payload one. SystemTable and
# ImageHandle are already in r15/r14, so clobbering al and dx here is safe.
Mark 's'

# Disable watchdog: BootServices->SetWatchdogTimer(0,0,0,NULL)
$bw.Write([byte[]]@(
    0x49, 0x8B, 0x47, 0x60,       # mov rax, [r15+0x60]
    0x48, 0x31, 0xC9,             # xor rcx, rcx
    0x48, 0x31, 0xD2,             # xor rdx, rdx
    0x4D, 0x31, 0xC0,             # xor r8, r8
    0x4C, 0x89, 0x44, 0x24, 0x20, # mov [rsp+0x20], r8
    0xFF, 0x90, 0x00, 0x01, 0x00, 0x00  # call [rax+0x100]
))

# ConOut ClearScreen BEFORE GopAcquire, and the order is the fix for the ASUS
# display corruption of 2026-08-02. On AMI Aptio V the first real ConOut use
# activates the GraphicsConsole, and that activation SETS ITS OWN GRAPHICS
# MODE. This stub used to ClearScreen ~200 bytes after reading Mode->Info, so
# the handoff block carried the SPLASH mode's geometry (1920x1080, stride
# 2048) for a scanout the console had since switched (1024 px/row): every row
# the payload wrote spanned two scanlines, which is the measured symptom set
# exactly -- glyphs stretched, alternate lines black, long-line tails
# overpainting the next row, and StrideProbe's width-stepped bar shattering
# into eight aliased copies (4096/gcd(7680,4096) = 8). OVMF and codex-vm never
# re-mode on ClearScreen, which is why no bed could express it until
# codex-vm's -uefi-conout-remode; reproduced and then cured under that flag,
# same image bytes, only this ordering changed. Clear first, ask after.
$bw.Write([byte[]]@(
    0x49, 0x8B, 0x47, 0x40,       # mov rax, [r15+0x40] (ConOut)
    0x48, 0x89, 0xC1,             # mov rcx, rax
    0xFF, 0x50, 0x30              # call [rax+0x30] (ClearScreen)
))

# Before the first thing that can fail, so DARK BLUE covers every failure path
# in this stub. 'v' on the serial says the acquisition itself succeeded, which
# is the one state the colour cannot report: no GOP and never loaded both leave
# the firmware's own screen up.
HandoffInit
GopAcquire
GopFill $GopDarkBlue
AcpiPublish

# Get current IP (for relative addressing of text/rodata after stub)
# call $+5; pop rbx; sub rbx, 5  -> rbx = address of this call instruction
$getIpOff = $ms.Position
$bw.Write([byte[]]@(0xE8, 0x00, 0x00, 0x00, 0x00, 0x5B, 0x48, 0x83, 0xEB, 0x05))

# AllocatePages for code+rodata, AT 0x100000.
#
# The guest's text is linked absolute at bare-metal-load-addr
# (X86_64State.codex:204), so it can only ever run from there. This asked for
# AllocateAnyPages and then THREW THE RETURNED BUFFER AWAY, and the copy below
# wrote 370 KB to 0x100000 regardless -- memory the firmware still owned and had
# not given us. An allocator whose result is discarded is not an allocation.
$codePages = [int](($textSz + $rodataSz + 4095) / 4096)
$bw.Write([byte[]]@(0x48, 0xC7, 0x44, 0x24, 0x30))              # mov qword [rsp+0x30], ImageBase
$bw.Write([BitConverter]::GetBytes([int]$ImageBase))
$bw.Write([byte[]]@(0x48, 0xC7, 0xC1, 0x02, 0x00, 0x00, 0x00))  # mov rcx, 2 (AllocateAddress)
$bw.Write([byte[]]@(0x48, 0xC7, 0xC2, 0x01, 0x00, 0x00, 0x00))  # mov rdx, 1 (EfiLoaderCode)
$bw.Write([byte[]]@(0x49, 0xC7, 0xC0))                          # mov r8, codePages
$bw.Write([BitConverter]::GetBytes([int]$codePages))
$bw.Write([byte[]]@(0x4C, 0x8D, 0x4C, 0x24, 0x30))              # lea r9, [rsp+0x30]
$bw.Write([byte[]]@(0x49, 0x8B, 0x47, 0x60))                    # mov rax, [r15+0x60]
$bw.Write([byte[]]@(0xFF, 0x50, 0x28))                          # call [rax+0x28]
AssertAllocOk 'C'                                               # C = code allocation
Mark 'c'                                                        # code pages are ours

# AllocatePages for the heap, ANYWHERE.
#
# Nothing in the guest requires a particular heap base: it reads deck-pos-addr
# and heap-hwm-addr, which this stub writes. Asking for a fixed 0x1000000 is
# what fails under edk2 -- measured 2026-07-29, and it fails at 64 MB as well as
# at 512 MB, so it is the ADDRESS that is unavailable and not the size. The
# status was ignored, so the stub then used 512 MB it did not own as its stack.
$bw.Write([byte[]]@(0x48, 0xB8))                                # mov rax, $HeapCeiling
$bw.Write([BitConverter]::GetBytes([long]$HeapCeiling))
$bw.Write([byte[]]@(0x48, 0x89, 0x44, 0x24, 0x38))              # mov [rsp+0x38], rax (ceiling IN)
$bw.Write([byte[]]@(0x48, 0xC7, 0xC1, 0x01, 0x00, 0x00, 0x00))  # mov rcx, 1 (AllocateMaxAddress)
$bw.Write([byte[]]@(0x48, 0xC7, 0xC2, 0x02, 0x00, 0x00, 0x00))  # mov rdx, 2 (EfiLoaderData)
$bw.Write([byte[]]@(0x49, 0xC7, 0xC0))                          # mov r8, HeapPages
$bw.Write([BitConverter]::GetBytes([int]$HeapPages))
$bw.Write([byte[]]@(0x4C, 0x8D, 0x4C, 0x24, 0x38))              # lea r9, [rsp+0x38]
$bw.Write([byte[]]@(0x49, 0x8B, 0x47, 0x60))                    # mov rax, [r15+0x60]
$bw.Write([byte[]]@(0xFF, 0x50, 0x28))                          # call [rax+0x28]
AssertAllocOk 'H'                                               # H = heap allocation
Mark 'h'                                                        # heap pages are ours

# A GDT that SYSCALL can actually be given.
#
# SYSCALL does not read a descriptor. It sets CS = STAR[47:32] and
# SS = STAR[47:32] + 8 and forces flat 64-bit CPL-0 attributes onto the hidden
# halves. So it needs the GDT to hold a 64-bit code descriptor immediately
# followed by a data descriptor, and it will run without one -- which is the
# trap, because the selector VALUES are still installed and stay installed after
# the handler returns.
#
# Measured against OVMF 2026-07-29: the firmware GDT is nine entries and
# selector 0x08 is a DATA descriptor while 0x10 is a CODE descriptor, so the
# old STAR of (8 << 32) gave the guest CS = a data selector and SS = a code
# selector, exactly backwards. Execution continued, and then the first firmware
# IRET after the first syscall validated CS and refused it: #GP, error code
# 0x0008, reported by OVMF from CpuDxe. There is no consecutive (code64, data)
# pair anywhere in that GDT, so no value of STAR fixes this and the stub has to
# supply the pair.
#
# What this does: copy the firmware's own GDT into a page we own, append the
# CURRENT CS and SS descriptors as a consecutive pair at 0x40 and 0x48, and load
# it. Every selector the firmware already uses keeps its meaning because the
# copy is byte-identical, and the appended pair are the firmware's own
# descriptors rather than invented ones, so they are correct for this machine
# without hardcoding 0x38/0x30. STAR then points at 0x40.
$bw.Write([byte[]]@(0x48, 0xB8))                                # mov rax, $HeapCeiling
$bw.Write([BitConverter]::GetBytes([long]$HeapCeiling))
$bw.Write([byte[]]@(0x48, 0x89, 0x44, 0x24, 0x40))              # mov [rsp+0x40], rax (ceiling IN)
$bw.Write([byte[]]@(0x48, 0xC7, 0xC1, 0x01, 0x00, 0x00, 0x00))  # mov rcx, 1 (AllocateMaxAddress)
$bw.Write([byte[]]@(0x48, 0xC7, 0xC2, 0x02, 0x00, 0x00, 0x00))  # mov rdx, 2 (EfiLoaderData)
$bw.Write([byte[]]@(0x49, 0xC7, 0xC0, 0x01, 0x00, 0x00, 0x00))  # mov r8, 1 page
$bw.Write([byte[]]@(0x4C, 0x8D, 0x4C, 0x24, 0x40))              # lea r9, [rsp+0x40]
$bw.Write([byte[]]@(0x49, 0x8B, 0x47, 0x60))                    # mov rax, [r15+0x60]
$bw.Write([byte[]]@(0xFF, 0x50, 0x28))                          # call [rax+0x28]
AssertAllocOk 'G'                                               # G = GDT page
Mark 'g'                                                        # GDT page is ours

$bw.Write([byte[]]@(0x0F, 0x01, 0x44, 0x24, 0x48))              # sgdt [rsp+0x48]
$bw.Write([byte[]]@(0x48, 0x8B, 0x74, 0x24, 0x4A))              # mov rsi, [rsp+0x4A]  (old base)
$bw.Write([byte[]]@(0x48, 0x0F, 0xB7, 0x4C, 0x24, 0x48))        # movzx rcx, word [rsp+0x48]
$bw.Write([byte[]]@(0x48, 0xFF, 0xC1))                          # inc rcx  (limit -> length)
$bw.Write([byte[]]@(0x48, 0x8B, 0x7C, 0x24, 0x40))              # mov rdi, [rsp+0x40]  (new page)
$bw.Write([byte[]]@(0x49, 0x89, 0xFB))                          # mov r11, rdi  (keep the base)
$bw.Write([byte[]]@(0xF3, 0xA4))                                # rep movsb

$bw.Write([byte[]]@(0x66, 0x8C, 0xC8))                          # mov ax, cs
$bw.Write([byte[]]@(0x25, 0xF8, 0xFF, 0x00, 0x00))              # and eax, 0xFFF8
$bw.Write([byte[]]@(0x66, 0x8C, 0xD2))                          # mov dx, ss
$bw.Write([byte[]]@(0x81, 0xE2, 0xF8, 0xFF, 0x00, 0x00))        # and edx, 0xFFF8
$bw.Write([byte[]]@(0x49, 0x8B, 0x0C, 0x03))                    # mov rcx, [r11+rax]  (code desc)
$bw.Write([byte[]]@(0x49, 0x89, 0x4B, 0x40))                    # mov [r11+0x40], rcx
$bw.Write([byte[]]@(0x49, 0x8B, 0x0C, 0x13))                    # mov rcx, [r11+rdx]  (data desc)
$bw.Write([byte[]]@(0x49, 0x89, 0x4B, 0x48))                    # mov [r11+0x48], rcx

$bw.Write([byte[]]@(0x66, 0xC7, 0x44, 0x24, 0x48, 0x4F, 0x00))  # mov word [rsp+0x48], 0x4F
$bw.Write([byte[]]@(0x4C, 0x89, 0x5C, 0x24, 0x4A))              # mov [rsp+0x4A], r11
$bw.Write([byte[]]@(0x0F, 0x01, 0x54, 0x24, 0x48))              # lgdt [rsp+0x48]

# Store SystemTable at 0x8000, and again at uefi-systab-addr.
#
# 0x8000 is where the bare-metal __start looks for it; cell 36208 is where every
# `uefi-*` function in the guest reads it. Normally __start copies one to the
# other. This stub calls `opening` directly and __start never runs, so writing
# only 0x8000 leaves 36208 holding zero -- and the guest then walks a null
# SystemTable to reach ConOut. Some of those call sites test for null first and
# some do not: `uefi-con-set-attr` does not, so the dev console reached its main
# loop, tried to set a colour, and called address 0.
$bw.Write([byte[]]@(0x4C, 0x89, 0x3C, 0x25))                    # mov [SysTableAddr], r15
$bw.Write([BitConverter]::GetBytes([int]$SysTableAddr))
$bw.Write([byte[]]@(0x4C, 0x89, 0x3C, 0x25))                    # mov [UefiSystabAddr], r15
$bw.Write([BitConverter]::GetBytes([int]$UefiSystabAddr))

# r10 = the heap base the firmware actually gave us, not a hoped-for constant.
$bw.Write([byte[]]@(0x4C, 0x8B, 0x54, 0x24, 0x38))              # mov r10, [rsp+0x38]

# Set RSP to top of heap allocation (bare-metal stack-above-heap layout),
# computed from that base at runtime.
$heapBytes = [long]$HeapPages * 4096
$bw.Write([byte[]]@(0x4C, 0x89, 0xD0))                          # mov rax, r10
$bw.Write([byte[]]@(0x48, 0xB9))                                 # mov rcx, heapBytes
$bw.Write([BitConverter]::GetBytes([long]$heapBytes))
$bw.Write([byte[]]@(0x48, 0x01, 0xC8))                          # add rax, rcx
$bw.Write([byte[]]@(0x48, 0x89, 0xC4))                          # mov rsp, rax
$bw.Write([byte[]]@(0x48, 0x89, 0xC5))                          # mov rbp, rax

# Store stackTop at stack-min-rsp-addr (overflow check tracking)
$StackMinRspAddr = 28736
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, StackMinRspAddr
$bw.Write([BitConverter]::GetBytes([long]$StackMinRspAddr))
$bw.Write([byte[]]@(0x48, 0x89, 0x07))                          # mov [rdi], rax (stackTop still in rax)

# Store stackTop at ram-size-addr as well.
#
# Cell 4072 is where EVERY panic path relocates the stack before it can say
# anything: `__out_of_memory` and `__watchdog_panic` both begin `mov rsp,
# [4072]`, and so does emit-cpu-exception-dump. On bare metal `__start` fills it
# -- the RAM size IS the stack top there (X86_64State.codex:206-208) -- and this
# stub never did, so the cell held zero, every one of those handlers set RSP to
# 0, and the first push took a #PF at CR2 = -8, then #DF, then a triple fault.
#
# That is what the dev console's "reboot-loop under real UEFI" actually was. The
# guest was correctly detecting a stack/heap collision and calling the handler
# that exists to report it; the handler could not run. Measured 2026-07-29 under
# OVMF: 21 triple faults in 40 s before this store, 0 after, and the panic
# prints OUT OF MEMORY instead.
$RamSizeAddr = 4072
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, RamSizeAddr
$bw.Write([BitConverter]::GetBytes([long]$RamSizeAddr))
$bw.Write([byte[]]@(0x48, 0x89, 0x07))                          # mov [rdi], rax

# Store heap base at deck-pos-addr
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, DeckPosAddr
$bw.Write([BitConverter]::GetBytes([long]$DeckPosAddr))
$bw.Write([byte[]]@(0x4C, 0x89, 0x17))                          # mov [rdi], r10

# Store 0 at deck-bound-counter-addr
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, DeckBoundCounterAddr
$bw.Write([BitConverter]::GetBytes([long]$DeckBoundCounterAddr))
$bw.Write([byte[]]@(0x48, 0x31, 0xC0))                          # xor rax, rax
$bw.Write([byte[]]@(0x48, 0x89, 0x07))                          # mov [rdi], rax

# Store heap base at heap-hwm-addr
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, HeapHwmAddr
$bw.Write([BitConverter]::GetBytes([long]$HeapHwmAddr))
$bw.Write([byte[]]@(0x4C, 0x89, 0x17))                          # mov [rdi], r10

# Store 0 at bivy-save-addr
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, BivySaveAddr
$bw.Write([BitConverter]::GetBytes([long]$BivySaveAddr))
$bw.Write([byte[]]@(0x48, 0x89, 0x07))                          # mov [rdi], rax

# Pre-load the serial input ring, for a board with no serial port.
#
# `__bare_metal_read_serial` polls a ring at serial-ring-buf-addr (0x500000,
# X86_64Boot.codex:91) with the write position at cell 28704 and the read
# position at 28712. codex-vm's -input does exactly this and nothing else
# (load_input_file), which is why a bed run can feed the compiler a mode line
# while a board cannot: no UART, and nothing else fills the ring.
#
# The bytes are stored one at a time rather than through a data section because
# this stub is hand-assembled and has no relocations to spend.
if ($Stdin) {
    $sb = [System.Text.Encoding]::ASCII.GetBytes($Stdin)
    if ($sb.Length -gt 120) { throw "[cdx-to-pe] -Stdin is $($sb.Length) bytes; this emitter stores it with disp8 and tops out at 120." }
    $bw.Write([byte[]]@(0x48, 0xBF))                                # mov rdi, 0x500000
    $bw.Write([BitConverter]::GetBytes([long]0x500000))
    for ($i = 0; $i -lt $sb.Length; $i++) {
        $bw.Write([byte[]]@(0xC6, 0x47, [byte]$i, $sb[$i]))         # mov byte [rdi+i], imm8
    }
    $bw.Write([byte[]]@(0x48, 0xC7, 0xC0))                          # mov rax, len
    $bw.Write([BitConverter]::GetBytes([int]$sb.Length))
    $bw.Write([byte[]]@(0x48, 0xBF))                                # mov rdi, 28704 (write pos)
    $bw.Write([BitConverter]::GetBytes([long]28704))
    $bw.Write([byte[]]@(0x48, 0x89, 0x07))                          # mov [rdi], rax
    $bw.Write([byte[]]@(0x48, 0x31, 0xC0))                          # xor rax, rax
    $bw.Write([byte[]]@(0x48, 0xBF))                                # mov rdi, 28712 (read pos)
    $bw.Write([BitConverter]::GetBytes([long]28712))
    $bw.Write([byte[]]@(0x48, 0x89, 0x07))                          # mov [rdi], rax
    # Tell emit-start the ring is primed, or it zeroes both positions on the
    # way past and this prefill never existed. See the serial-primed-magic
    # guard in X86_64Chapter.codex. Cell 30696, magic 1347573316.
    $bw.Write([byte[]]@(0x48, 0xC7, 0xC0))                          # mov rax, magic
    $bw.Write([BitConverter]::GetBytes([int]1347573316))
    $bw.Write([byte[]]@(0x48, 0xBF))                                # mov rdi, 30696
    $bw.Write([BitConverter]::GetBytes([long]30696))
    $bw.Write([byte[]]@(0x48, 0x89, 0x07))                          # mov [rdi], rax
    Mark 'i'                                                        # input ring primed
}
# Copy text section: memcpy(0x100000, rbx + textDataOff, textSz)
# We'll patch textDataOff after we know the full stub size (two-pass)
$copyTextPatchPos = $ms.Position + 3  # offset of the imm32 in lea rsi, [rbx+imm32]
$bw.Write([byte[]]@(0x48, 0x8D, 0xB3))                          # lea rsi, [rbx + textDataOff]
$bw.Write([int]0)                                                # placeholder
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, ImageBase
$bw.Write([BitConverter]::GetBytes([long]$ImageBase))
$bw.Write([byte[]]@(0x48, 0xB9))                                 # mov rcx, textSz
$bw.Write([BitConverter]::GetBytes([long]$textSz))
$bw.Write([byte[]]@(0xF3, 0xA4))                                # rep movsb

# Copy rodata section
$copyRodataPatchPos = $ms.Position + 3
$bw.Write([byte[]]@(0x48, 0x8D, 0xB3))                          # lea rsi, [rbx + rodataDataOff]
$bw.Write([int]0)                                                # placeholder
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, dataVaddr
$bw.Write([BitConverter]::GetBytes([long]$dataVaddr))
$bw.Write([byte[]]@(0x48, 0xB9))                                 # mov rcx, rodataSz
$bw.Write([BitConverter]::GetBytes([long]$rodataSz))
$bw.Write([byte[]]@(0xF3, 0xA4))                                # rep movsb

# The ClearScreen that used to sit here moved to the top of the stub, before
# GopAcquire -- see the comment there. Nothing may call ConOut past this point:
# under -ExitBootServices the block below ends boot services entirely.

if ($ExitBootServices) {
    # GetMemoryMap + ExitBootServices, with the one stale-key retry the spec
    # anticipates (an allocation between the map read and the call invalidates
    # the key exactly once here, since nothing allocates in between -- the
    # retry covers firmware-internal churn). Slot layout keeps everything
    # ABOVE the 32-byte shadow space the callee owns: [rsp+0x28] MapSize,
    # [rsp+0x30] MapKey, [rsp+0x38] DescSize, [rsp+0x40] DescVersion,
    # buffer at [rsp+0x50], 128 KB. RSP here is the top of our own heap
    # allocation (4 KB aligned), so the sub keeps 16-byte call alignment.
    $ebsFrame = 0x20050
    function GmmChunk() {
        return [byte[]]@(
            0x48, 0xC7, 0x44, 0x24, 0x28, 0x80, 0xFF, 0x01, 0x00,  # mov qword [rsp+0x28], 0x1FF80
            0x48, 0x8D, 0x4C, 0x24, 0x28,                          # lea rcx, [rsp+0x28]
            0x48, 0x8D, 0x54, 0x24, 0x50,                          # lea rdx, [rsp+0x50]
            0x4C, 0x8D, 0x44, 0x24, 0x30,                          # lea r8,  [rsp+0x30]
            0x4C, 0x8D, 0x4C, 0x24, 0x38,                          # lea r9,  [rsp+0x38]
            0x48, 0x8D, 0x44, 0x24, 0x40,                          # lea rax, [rsp+0x40]
            0x48, 0x89, 0x44, 0x24, 0x20,                          # mov [rsp+0x20], rax (5th arg)
            0x49, 0x8B, 0x47, 0x60,                                # mov rax, [r15+0x60]
            0xFF, 0x90, 0x38, 0x00, 0x00, 0x00                     # call [rax+0x38] GetMemoryMap
        )
    }
    function EbsChunk() {
        return [byte[]]@(
            0x4C, 0x89, 0xF1,                                      # mov rcx, r14 (ImageHandle)
            0x48, 0x8B, 0x54, 0x24, 0x30,                          # mov rdx, [rsp+0x30] (MapKey)
            0x49, 0x8B, 0x47, 0x60,                                # mov rax, [r15+0x60]
            0xFF, 0x90, 0xE8, 0x00, 0x00, 0x00                     # call [rax+0xE8] ExitBootServices
        )
    }
    # Panic block bytes, matching AllocPanic: test/jz+13/mark-and-halt.
    function PanicBytes([char]$c) {
        return [byte[]]@(0x48, 0x85, 0xC0, 0x74, 0x0D,
                         0xB0, [byte][int]$c, 0x66, 0xBA, 0xF8, 0x03, 0xEE,
                         0x66, 0xBA, 0xF8, 0x02, 0xEE, 0xF4)
    }
    $bw.Write([byte[]]@(0x48, 0x81, 0xEC))                         # sub rsp, ebsFrame
    $bw.Write([BitConverter]::GetBytes([int]$ebsFrame))
    $attempt2 = [System.Collections.Generic.List[byte]]::new()
    $attempt2.AddRange([byte[]](GmmChunk))
    $attempt2.AddRange([byte[]](PanicBytes 'M'))
    $attempt2.AddRange([byte[]](EbsChunk))
    $attempt2.AddRange([byte[]](PanicBytes 'X'))
    $bw.Write([byte[]](GmmChunk))
    $bw.Write([byte[]](PanicBytes 'M'))
    $bw.Write([byte[]](EbsChunk))
    if ($attempt2.Count -gt 127) { throw "[cdx-to-pe] EBS retry block outgrew a rel8 branch ($($attempt2.Count))" }
    $bw.Write([byte[]]@(0x48, 0x85, 0xC0))                         # test rax, rax
    $bw.Write([byte[]]@(0x74, [byte]$attempt2.Count))              # jz over the retry
    $bw.Write($attempt2.ToArray())
    $bw.Write([byte]0xFA)                                          # cli -- firmware is gone
    $bw.Write([byte[]]@(0x48, 0x81, 0xC4))                         # add rsp, ebsFrame
    $bw.Write([BitConverter]::GetBytes([int]$ebsFrame))
    Mark 'x'                                                       # x = boot services exited
    # Zero both SystemTable cells. Boot services are dead; a guest uefi-*
    # call through a live-looking pointer would execute freed firmware code.
    # Null makes every guarded path take its no-firmware branch instead.
    $bw.Write([byte[]]@(0x48, 0x31, 0xC0))                         # xor rax, rax
    $bw.Write([byte[]]@(0x48, 0x89, 0x04, 0x25))                   # mov [SysTableAddr], rax
    $bw.Write([BitConverter]::GetBytes([int]$SysTableAddr))
    $bw.Write([byte[]]@(0x48, 0x89, 0x04, 0x25))                   # mov [UefiSystabAddr], rax
    $bw.Write([BitConverter]::GetBytes([int]$UefiSystabAddr))
}

# Program the SYSCALL MSRs, exactly as the bare-metal entry does in
# emit-runtime-init-fn / emit-start (X86_64Boot.codex, X86_64Chapter.codex).
# codex-vm sets EFER=0xD01 for a UEFI guest so SCE is already on, but nothing
# writes the three MSRs that say where a syscall GOES, and firmware does not
# leave them pointing anywhere useful.
#
# This must come after the text copy (LSTAR names an address inside it) and
# before the call to `opening`. rax/rcx/rdx are dead here -- ClearScreen was the
# last user -- and rdmsr/wrmsr clobber all three, which is why it cannot sit
# after `mov rax, opening`.
#
# The handler returns with `popfq; jmp rcx` rather than `sysret`, so it never
# needs a matching SYSRET selector layout. It does need the GDT built above.
#
# This block used to claim the opposite -- that because SYSCALL forces flat
# 64-bit CPL-0 attributes without consulting a descriptor table, "the firmware's
# GDT is not involved". Forcing the hidden half is why the guest keeps running;
# it does not make the selector VALUES legal, and they persist after the handler
# returns. The first firmware IRET afterwards validates CS and faults. That cost
# a day, and the refutation is in the GDT dump: on OVMF selector 0x08 is a data
# descriptor and 0x10 is a code descriptor.
# Decimal, matching X86_64Boot.codex, and not hex: PowerShell parses 0xC0000081
# as Int32 -1073741695, which happens to carry the right four bytes and reads as
# a mistake every time anyone checks it.
$MsrEfer   = 3221225600L    # 0xC0000080
$MsrStar   = 3221225601L    # 0xC0000081, [47:32] syscall CS/SS selector base
$MsrLstar  = 3221225602L    # 0xC0000082, 64-bit syscall entry point
$MsrSfmask = 3221225604L    # 0xC0000084, RFLAGS bits cleared on entry

# Take the halves out of the 8 bytes rather than masking. `-band 0xFFFFFFFF` does
# NOT mask in PowerShell: 0xFFFFFFFF parses as Int32 -1, so the AND is the
# identity and the value sails through to a failing [int] cast. Worth knowing in
# a file that emits imm32 operands by hand.
function Write-Msr([long]$msr, [long]$value) {
    $m8 = [BitConverter]::GetBytes($msr)
    $v8 = [BitConverter]::GetBytes($value)
    $bw.Write([byte]0xB9); $bw.Write($m8, 0, 4)     # mov ecx, msr
    $bw.Write([byte]0xB8); $bw.Write($v8, 0, 4)     # mov eax, lo32
    $bw.Write([byte]0xBA); $bw.Write($v8, 4, 4)     # mov edx, hi32
    $bw.Write([byte[]]@(0x0F, 0x30))                # wrmsr
}

# 0x40, the consecutive (code64, data) pair appended to the copied GDT above.
# This was 8, which on OVMF selected a data descriptor for CS and a code
# descriptor for SS.
Write-Msr $MsrStar   ([long]0x40 -shl 32)
Write-Msr $MsrLstar  ([long]($ImageBase + $syscallFuncOff))
Write-Msr $MsrSfmask 512                                        # 0x200, clear IF on entry

# EFER |= SCE. Redundant under codex-vm, which already sets it, but the bare
# metal path does it and real firmware is not required to.
$bw.Write([byte]0xB9); $bw.Write(([BitConverter]::GetBytes($MsrEfer)), 0, 4)  # mov ecx, IA32_EFER
$bw.Write([byte[]]@(0x0F, 0x32))                                # rdmsr
$bw.Write([byte[]]@(0x48, 0x83, 0xC8, 0x01))                    # or rax, 1
$bw.Write([byte[]]@(0x0F, 0x30))                                # wrmsr

# DARK GREEN: the stub is done and the guest is about to run. Placed after the
# stub's own ClearScreen so it is not wiped by it, and BEFORE the r10 reload
# below so that even though this fill makes no firmware call, the reload still
# covers it. A guest that lives clears the screen and prints over this; a guest
# that dies silently leaves it up, and those are the two states a human at the
# board could not previously tell apart.
GopFill $GopDarkGreen

# Reload r10 from deck-pos-addr, and do it AFTER the last firmware call.
#
# r10 is the deck pointer for the whole of generated Codex: every non-leaf
# prologue opens with `cmp rsp, r10; jb __out_of_memory`. It is also
# CALLER-SAVED in the Microsoft x64 ABI, so ClearScreen above is entitled to
# destroy it, and OVMF does. The stub set r10 before that call, so `opening`
# entered with a firmware pointer in it (measured 0x7e663238, unaligned and
# adjacent to r14/r15), the comparison in opening's OWN prologue went the wrong
# way, and the guest jumped to __out_of_memory before a line of its body ran.
#
# The stack depth is what identified it: RSP at the trip was exactly 48 bytes
# below the top, one return address plus the five callee-saved pushes, so
# nothing had run and nothing had allocated. **The OUT OF MEMORY this produced
# was a false report** -- the heap was untouched.
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, DeckPosAddr
$bw.Write([BitConverter]::GetBytes([long]$DeckPosAddr))
$bw.Write([byte[]]@(0x4C, 0x8B, 0x17))                          # mov r10, [rdi]

# 'o' -- everything the stub owes the guest is done and we are about to enter
# `opening`. Placed AFTER the r10 reload so it cannot disturb the deck pointer,
# and it touches only al and dx, both dead here (rax is loaded on the next
# line). 's' with no 'o' means we died inside the stub; 'o' with nothing after
# it means the guest itself did.
Mark 'o'

# Call opening: mov rax, ImageBase + openingFuncOff; call rax
$bw.Write([byte[]]@(0x48, 0xB8))                                 # mov rax, imm64
$bw.Write([BitConverter]::GetBytes([long]($ImageBase + $openingFuncOff)))
$bw.Write([byte[]]@(0xFF, 0xD0))                                # call rax

# Epilog: halt (we replaced the stack so we can't return to UEFI)
$bw.Write([byte[]]@(0xF4))                                      # hlt

$bw.Close()
$stub = $ms.ToArray()

# Patch the copy offsets: text data starts right after the stub
# rbx = address of the getIP call instruction
$rbxOff = $getIpOff  # offset of getIP within stub
$textDataOff = $stub.Length - $rbxOff          # text starts after stub, relative to rbx
$rodataDataOff = $stub.Length + $textSz - $rbxOff  # rodata after text

[BitConverter]::GetBytes([int]$textDataOff).CopyTo($stub, $copyTextPatchPos)
[BitConverter]::GetBytes([int]$rodataDataOff).CopyTo($stub, $copyRodataPatchPos)

# --- Extract text and rodata from CDX ---
$textBytes = New-Object byte[] $textSz
[Array]::Copy($cdx, $textOff, $textBytes, 0, $textSz)
$rodataBytes = New-Object byte[] $rodataSz
if ($rodataSz -gt 0) { [Array]::Copy($cdx, $rodataOff, $rodataBytes, 0, $rodataSz) }

$code = $stub + $textBytes + $rodataBytes
$codeSize = $code.Length
$codeRaw = (($codeSize + $FileAlign - 1) -band (-bnot ($FileAlign - 1)))
$codePad = $codeRaw - $codeSize
$codeSecEnd = (($codeSize + $SectionAlign - 1) -band (-bnot ($SectionAlign - 1)))
$relocRva = $SectionAlign + $codeSecEnd
$imageSize = (($relocRva + $SectionAlign + $SectionAlign - 1) -band (-bnot ($SectionAlign - 1)))
$relocRawOff = $HeaderSize + $codeRaw

# --- Build PE headers ---
$hms = [System.IO.MemoryStream]::new()
$hbw = [System.IO.BinaryWriter]::new($hms)

# DOS header
$hbw.Write([byte[]]@(0x4D, 0x5A))
$hbw.Write((New-Object byte[] 58))
$hbw.Write([int]128)
$hbw.Write((New-Object byte[] (128 - 64)))

# PE signature
$hbw.Write([byte[]]@(0x50, 0x45, 0x00, 0x00))

# COFF header
$hbw.Write([ushort]0x8664); $hbw.Write([ushort]2)
$hbw.Write([int]0); $hbw.Write([int]0); $hbw.Write([int]0)
$hbw.Write([ushort]240); $hbw.Write([ushort]0x0022)

# Optional header (PE32+)
$hbw.Write([ushort]0x020B)
$hbw.Write([byte]0); $hbw.Write([byte]0)
$hbw.Write([int]$codeRaw); $hbw.Write([int]0); $hbw.Write([int]0)
$hbw.Write([int]$SectionAlign)        # AddressOfEntryPoint
$hbw.Write([int]$SectionAlign)        # BaseOfCode
$hbw.Write([long]0)                   # ImageBase
$hbw.Write([int]$SectionAlign); $hbw.Write([int]$FileAlign)
$hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([int]0)
$hbw.Write([int]$imageSize); $hbw.Write([int]$HeaderSize); $hbw.Write([int]0)
$hbw.Write([ushort]10)               # EFI Application
$hbw.Write([ushort]0x0160)
$hbw.Write([long]0); $hbw.Write([long]0)
$hbw.Write([long]0); $hbw.Write([long]0)
$hbw.Write([int]0); $hbw.Write([int]16)

for ($i = 0; $i -lt 16; $i++) {
    if ($i -eq 5) { $hbw.Write([int]$relocRva); $hbw.Write([int]8) }
    else { $hbw.Write([long]0) }
}

# .text section
$hbw.Write([System.Text.Encoding]::ASCII.GetBytes(".text`0`0`0"))
$hbw.Write([int]$codeSize); $hbw.Write([int]$SectionAlign)
$hbw.Write([int]$codeRaw); $hbw.Write([int]$HeaderSize)
$hbw.Write([int]0); $hbw.Write([int]0)
$hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([int]0x60000020)

# .reloc section
$hbw.Write([System.Text.Encoding]::ASCII.GetBytes(".reloc`0`0"))
$hbw.Write([int]8); $hbw.Write([int]$relocRva)
$hbw.Write([int]$FileAlign); $hbw.Write([int]$relocRawOff)
$hbw.Write([int]0); $hbw.Write([int]0)
$hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([int]0x42000040)

$hbw.Close()
$headers = $hms.ToArray()
$headerPad = New-Object byte[] ($HeaderSize - $headers.Length)

$relocBlock = New-Object byte[] $FileAlign
[BitConverter]::GetBytes([int]$relocRva).CopyTo($relocBlock, 0)
[BitConverter]::GetBytes([int]8).CopyTo($relocBlock, 4)

# --- Write PE ---
$outMs = [System.IO.MemoryStream]::new()
$outMs.Write($headers, 0, $headers.Length)
$outMs.Write($headerPad, 0, $headerPad.Length)
$outMs.Write($code, 0, $code.Length)
if ($codePad -gt 0) { $outMs.Write((New-Object byte[] $codePad), 0, $codePad) }
$outMs.Write($relocBlock, 0, $relocBlock.Length)

[System.IO.File]::WriteAllBytes($Out, $outMs.ToArray())
Write-Host "[cdx-to-pe] OK: $Out ($($outMs.Length) bytes, stub=$($stub.Length) opening=0x$($openingFuncOff.ToString('X')))"
