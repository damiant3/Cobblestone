# UEFI PE32+ Application Spec Summary

Everything needed to produce a working UEFI application binary on real
hardware. All offsets are for x86-64.

## PE32+ Header Layout

### DOS Header (64 bytes)

| Offset | Size | Field | Value |
|--------|------|-------|-------|
| 0x00 | 2 | e_magic | `0x5A4D` ("MZ") |
| 0x3C | 4 | e_lfanew | Offset to PE signature (e.g. `0x80`) |

Everything else in the DOS header can be zeros.

### PE Signature (4 bytes at e_lfanew)

`0x50 0x45 0x00 0x00` ("PE\0\0")

### COFF Header (20 bytes)

| Offset | Size | Field | Value |
|--------|------|-------|-------|
| +0 | 2 | Machine | `0x8664` (AMD64) |
| +2 | 2 | NumberOfSections | N |
| +4 | 4 | TimeDateStamp | 0 |
| +8 | 4 | PointerToSymbolTable | 0 |
| +12 | 4 | NumberOfSymbols | 0 |
| +16 | 2 | SizeOfOptionalHeader | 240 (`0xF0`) for PE32+ with 16 data dirs |
| +18 | 2 | Characteristics | `0x0022` (EXECUTABLE_IMAGE + LARGE_ADDRESS_AWARE) |

Do NOT set IMAGE_FILE_RELOCS_STRIPPED (0x0001) unless you are absolutely
certain firmware will load at your preferred ImageBase. Real firmware
relocates.

### Optional Header (PE32+, 240 bytes)

| Offset | Size | Field | Value |
|--------|------|-------|-------|
| +0 | 2 | Magic | `0x020B` (PE32+) |
| +2 | 1 | MajorLinkerVersion | 0 |
| +3 | 1 | MinorLinkerVersion | 0 |
| +4 | 4 | SizeOfCode | Rounded .text size |
| +8 | 4 | SizeOfInitializedData | Sum of non-code section sizes |
| +12 | 4 | SizeOfUninitializedData | 0 |
| +16 | 4 | AddressOfEntryPoint | RVA of entry (e.g. `0x1000`) |
| +20 | 4 | BaseOfCode | RVA of .text (e.g. `0x1000`) |
| +24 | 8 | ImageBase | Preferred load address, 64KB-aligned |
| +32 | 4 | SectionAlignment | `0x1000` (4096) -- MUST be page-aligned |
| +36 | 4 | FileAlignment | `0x200` (512) minimum |
| +40 | 2 | MajorOSVersion | 0 |
| +42 | 2 | MinorOSVersion | 0 |
| +44 | 2 | MajorImageVersion | 0 |
| +46 | 2 | MinorImageVersion | 0 |
| +48 | 2 | MajorSubsystemVersion | 0 |
| +50 | 2 | MinorSubsystemVersion | 0 |
| +52 | 4 | Win32VersionValue | 0 |
| +56 | 4 | SizeOfImage | Virtual end of last section, rounded to SectionAlignment |
| +60 | 4 | SizeOfHeaders | All headers + section table, rounded to FileAlignment |
| +64 | 4 | CheckSum | 0 (not validated) |
| +68 | 2 | Subsystem | `10` = EFI_APPLICATION |
| +70 | 2 | DllCharacteristics | `0x0160` (NX_COMPAT + DYNAMIC_BASE + HIGH_ENTROPY_VA) |
| +72 | 8 | SizeOfStackReserve | 0 |
| +80 | 8 | SizeOfStackCommit | 0 |
| +88 | 8 | SizeOfHeapReserve | 0 |
| +96 | 8 | SizeOfHeapCommit | 0 |
| +104 | 4 | LoaderFlags | 0 |
| +108 | 4 | NumberOfRvaAndSizes | 16 |
| +112 | 128 | DataDirectories | 16 entries x 8 bytes. Index 5 = Base Reloc. Rest zeros. |

### Section Headers (40 bytes each)

| Offset | Size | Field |
|--------|------|-------|
| +0 | 8 | Name (e.g. ".text\0\0\0") |
| +8 | 4 | VirtualSize (actual code size, unaligned) |
| +12 | 4 | VirtualAddress (RVA, e.g. 0x1000) |
| +16 | 4 | SizeOfRawData (FileAlignment-aligned) |
| +20 | 4 | PointerToRawData (file offset = SizeOfHeaders) |
| +24 | 12 | Relocations/LineNumbers (all zero) |
| +36 | 4 | Characteristics |

Section flag values:
- `.text` (code): `0x60000020` (CODE + EXECUTE + READ)
- `.data` (mutable): `0xC0000040` (INITIALIZED_DATA + READ + WRITE)
- `.rodata` (const): `0x40000040` (INITIALIZED_DATA + READ)
- `.reloc`: `0x42000040` (INITIALIZED_DATA + READ + DISCARDABLE)

NX-strict firmware (post-2020 boards) enforces W^X: no section may be
both WRITE and EXECUTE. Separate code from data into distinct sections.

---

## EFI Entry Point

Microsoft x64 calling convention:
- RCX = ImageHandle (EFI_HANDLE)
- RDX = SystemTable (EFI_SYSTEM_TABLE*)
- Return RAX = EFI_STATUS (0 = success)
- Stack is 16-byte aligned on entry (return address makes it 8-misaligned,
  so first thing is `sub rsp, 0x28` or similar odd multiple of 8)
- 32 bytes shadow space required before every `call`

Minimal hello world:
```asm
entry:
    sub  rsp, 0x28          ; shadow(32) + alignment(8) = 0x28
    mov  rax, [rdx+0x40]   ; ConOut = SystemTable->ConOut
    mov  rcx, rax           ; arg1 = This (ConOut)
    lea  rdx, [rel msg]    ; arg2 = UCS-2 string (RIP-relative!)
    call [rax+0x08]         ; ConOut->OutputString
    xor  eax, eax           ; return EFI_SUCCESS
    add  rsp, 0x28
    ret
msg: dw 'H','e','l','l','o',0x0D,0x0A,0
```

---

## EFI_SYSTEM_TABLE (x64 offsets)

| Offset | Field |
|--------|-------|
| +0x00 | Hdr.Signature (UINT64) |
| +0x08 | Hdr.Revision (UINT32) |
| +0x0C | Hdr.HeaderSize (UINT32) |
| +0x10 | Hdr.CRC32 (UINT32) |
| +0x14 | Hdr.Reserved (UINT32) |
| +0x18 | FirmwareVendor (CHAR16*) |
| +0x20 | FirmwareRevision (UINT32, +4 padding) |
| +0x28 | ConsoleInHandle |
| +0x30 | ConIn |
| +0x38 | ConsoleOutHandle |
| **+0x40** | **ConOut (EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL*)** |
| +0x48 | StandardErrorHandle |
| +0x50 | StdErr |
| +0x58 | RuntimeServices |
| **+0x60** | **BootServices (EFI_BOOT_SERVICES*)** |
| +0x68 | NumberOfTableEntries |
| +0x70 | ConfigurationTable |

---

## EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL (ConOut vtable)

| Offset | Function |
|--------|----------|
| +0x00 | Reset |
| **+0x08** | **OutputString** |
| +0x10 | TestString |
| +0x18 | QueryMode |
| +0x20 | SetMode |
| +0x28 | SetAttribute |
| +0x30 | ClearScreen |
| +0x38 | SetCursorPosition |
| +0x40 | EnableCursor |
| +0x48 | Mode (data, not function) |

OutputString signature: `EFI_STATUS OutputString(THIS, CHAR16* String)`
- RCX = This (ConOut pointer)
- RDX = null-terminated UCS-2 (UTF-16LE) string
- Shadow space (32 bytes on stack) before call

---

## EFI_BOOT_SERVICES Key Offsets

| Offset | Function |
|--------|----------|
| +0x28 (40) | AllocatePages |
| +0x30 (48) | FreePages |
| +0x38 (56) | GetMemoryMap |
| +0x40 (64) | AllocatePool |
| +0x48 (72) | FreePool |
| +0x60 (96) | WaitForEvent |
| +0x98 (152) | HandleProtocol |
| +0xE8 (232) | ExitBootServices |
| +0x140 (320) | LocateProtocol |

AllocatePages signature:
```
EFI_STATUS AllocatePages(
    EFI_ALLOCATE_TYPE Type,     // RCX: 0=AllocateAnyPages, 2=AllocateAddress
    EFI_MEMORY_TYPE MemType,    // RDX: 2=EfiLoaderData
    UINTN Pages,                // R8: page count
    EFI_PHYSICAL_ADDRESS *Addr  // R9: in/out address pointer
)
```

---

## .reloc Section (Base Relocations)

Required for firmware that relocates the image. Even if the code is fully
RIP-relative and needs no fixups, a dummy .reloc section prevents firmware
from rejecting the image.

Minimal dummy .reloc (8 bytes):
```
dd 0x00000000    ; PageRVA = 0 (base of block)
dd 0x00000008    ; BlockSize = 8 (just this header, no entries)
```

Point data directory index 5 (Base Relocation Table) to this section:
- VirtualAddress = RVA of .reloc section
- Size = 8

---

## Why UEFI Apps Fail on Real Hardware

### 1. Relocation (most common)

Real firmware loads the image at an arbitrary address, NOT at ImageBase.
If code uses absolute addresses (mov rax, imm64; jmp rax) to reference
locations within the image, those addresses are WRONG after relocation.

Fix: Use ONLY RIP-relative addressing for anything within the image.
`lea rax, [rip + offset]` not `mov rax, absolute_address`.

### 2. No .reloc section

Without .reloc, some firmware refuses to load the image entirely. Others
load it but can't fix up references. Add a dummy .reloc even if you have
no fixups.

### 3. NX / W^X enforcement

Post-2020 boards enforce: no page is both writable and executable.
A single section with CODE+WRITE flags fails. Separate into .text
(execute+read) and .data (read+write).

### 4. DllCharacteristics = 0

Some boards require NX_COMPAT (0x0100) in DllCharacteristics.
Set to `0x0160` for maximum compatibility.

### 5. SectionAlignment not page-aligned

Must be 4096. Firmware validates this strictly.

### 6. SizeOfImage wrong

Must equal last section's VirtualAddress + VirtualSize, rounded up to
SectionAlignment. If wrong, firmware may allocate wrong amount and crash.

### 7. SizeOfHeaders wrong

Must be >= actual header bytes, rounded up to FileAlignment. If too small,
firmware reads garbage into section data.

### 8. Stack alignment

UEFI gives 16-byte aligned stack. The `call` to your entry pushes 8 bytes
(return address), so RSP is 8-misaligned at entry. You must adjust by an
odd multiple of 8 before calling any firmware function (e.g. `sub rsp, 0x28`).

### 9. Missing shadow space

Every call requires 32 bytes of shadow space on the stack above the return
address. Firmware will write into those 32 bytes. If you don't allocate
them, firmware corrupts your locals.

### 10. Absolute addresses to low memory

Physical addresses like 0x7000, 0x8000, 0x100000 are NOT guaranteed
available before ExitBootServices. They may be reserved, MMIO, or
firmware-owned. Use AllocatePages to get memory, then use the RETURNED
address -- don't assume specific physical addresses are free.

---

## Architecture: Kernel Stub vs. UEFI App Stub

### Kernel stub (ExitBootServices path)
- Calls ExitBootServices -- firmware releases all memory
- After EBS, we own everything: write to 0x100000, 0x7000, etc. freely
- Absolute addresses are fine
- No ConOut available after EBS (firmware services gone)

### UEFI app stub (ConOut alive, no EBS)
- Firmware owns memory -- must allocate before using
- Image is at arbitrary firmware-chosen address
- All self-references must be RIP-relative
- ConOut is alive for printing
- Stack is firmware's stack (don't smash it)
- Must return cleanly or call EFI Exit()
- Cannot write to arbitrary physical addresses without AllocatePages

The UEFI app should NOT copy itself anywhere. It should run in-place at
whatever address firmware loaded it. The PE loader already set up all
sections correctly in virtual memory. Just use them.

---

## Codex-Specific Notes

Our compiled Codex programs expect:
- Code at 0x100000 (text segment)
- Rodata at a fixed vaddr
- Heap at 0x400000
- Metadata at 0x7000
- SystemTable ptr at 0x8000

For a UEFI app, we need to either:
1. Allocate those addresses with AllocatePages(AllocateAddress) and copy
   (works if addresses are free -- may fail on some boards)
2. Or make the compiled program position-independent (big change)
3. Or AllocatePages(AllocateAnyPages) and patch all references (needs relocs)

Option 1 is simplest. But if 0x100000 is firmware-reserved on a specific
board, it fails silently. The AllocatePages call returns EFI_NOT_FOUND.
The stub MUST check the return status and print a diagnostic if allocation
fails.

For FirstBoot (small app, ~38KB), the safest path:
- Allocate any pages for code + heap
- Use the returned addresses
- Don't rely on specific physical addresses
- Or: run directly from the PE image sections (no copy at all)

---

## PE Header Byte Budget (512-byte header)

Layout at file offsets:
```
0x000-0x07F  DOS Header (128 bytes, mostly zeros)
0x080-0x083  PE Signature ("PE\0\0")
0x084-0x097  COFF Header (20 bytes)
0x098-0x187  Optional Header PE32+ (240 bytes)
0x188-0x1AF  Section Header 1: .text (40 bytes)
0x1B0-0x1D7  Section Header 2: .reloc (40 bytes)
0x1D8-0x1FF  Padding (40 bytes free)
```

Total headers = 472 bytes, well within 512. Room for one more section
if needed (3 sections = 512 exactly).

---

## ExitBootServices Protocol

EBS requires a valid MapKey from GetMemoryMap. The key goes stale on any
allocation/free. Sequence:

```
GetMemoryMap(&mapSize, buf, &mapKey, &descSize, &descVer)
ExitBootServices(ImageHandle, mapKey)
```

After EBS returns EFI_SUCCESS:
- All Boot Services are dead (ConOut, AllocatePages, etc.)
- Runtime Services survive (ResetSystem, GetTime, etc.)
- You own all physical memory except Runtime regions
- Interrupts must be disabled (firmware IDT is gone)
- Must set up your own page tables before accessing > 4GB

If EBS fails (stale key), call GetMemoryMap again and retry ONCE.

ImageHandle is the first argument to the EFI entry point (RCX). Must be
saved across the entire stub for the EBS call.

---

## GetMemoryMap Details

```
EFI_STATUS GetMemoryMap(
    UINTN *MemoryMapSize,       // RCX: in/out -- buffer size
    EFI_MEMORY_DESCRIPTOR *Map, // RDX: output buffer
    UINTN *MapKey,              // R8: out -- key for EBS
    UINTN *DescriptorSize,      // R9: out -- size of each desc
    UINT32 *DescriptorVersion   // [RSP+0x20]: out
)
```

Typical buffer: 16KB is generous. DescriptorSize is usually 48 bytes.
MapKey changes on any memory operation -- call GetMemoryMap and EBS
back-to-back with no intervening allocations.

---

## Page Table Setup After EBS

UEFI leaves paging enabled with its own page tables. After EBS you must
either use the existing tables or load new ones. For Codex kernel boot:

```
Identity map first 4GB using 2MB pages:
  PML4[0] -> PDPT at 0x2000
  PDPT[0..3] -> PD0..PD3 at 0x3000..0x6000
  Each PD entry: phys_addr | 0x83 (Present + Writable + PageSize=2MB)
  2048 entries total (4GB / 2MB)

Write PML4 address to CR3.
```

Must write page table structures to allocated (or post-EBS free) memory.
Low pages (0x1000-0x6FFF) are commonly free after EBS on most platforms.

If writing page tables BEFORE EBS: must AllocatePages for them first.
If writing AFTER EBS: any physical memory not marked Runtime is yours.

---

## ConIn -- Reading Keyboard Input

`EFI_SIMPLE_TEXT_INPUT_PROTOCOL` = SystemTable->ConIn (offset +0x30)

| Offset | Function |
|--------|----------|
| +0x00 | Reset |
| +0x08 | ReadKeyStroke |

ReadKeyStroke signature: `EFI_STATUS ReadKeyStroke(THIS, EFI_INPUT_KEY *Key)`
- RCX = This (ConIn pointer)
- RDX = pointer to EFI_INPUT_KEY structure (4 bytes: UINT16 ScanCode + UINT16 UnicodeChar)
- Returns EFI_NOT_READY if no key available (poll in loop)

ScanCode values: ESC=0x17, Up=0x01, Down=0x02, Right=0x03, Left=0x04,
Home=0x05, End=0x06, Insert=0x07, Delete=0x08, PgUp=0x09, PgDn=0x0A,
F1-F10=0x0B-0x14.

For printable keys, ScanCode=0 and UnicodeChar has the character.

---

## RuntimeServices -- ResetSystem

After or before EBS, RuntimeServices remains available.
RS = SystemTable + 0x58.

| Offset | Function |
|--------|----------|
| +0x68 | ResetSystem |

```
VOID ResetSystem(
    EFI_RESET_TYPE ResetType,  // RCX: 0=Cold, 1=Warm, 2=Shutdown
    EFI_STATUS Status,          // RDX: status code (0 = success)
    UINTN DataSize,             // R8: 0
    VOID *ResetData             // R9: NULL
)
```

Does not return.

---

## Practical PE Constants (decimal for Codex source)

| Name | Hex | Decimal | Use |
|------|-----|---------|-----|
| Machine AMD64 | 0x8664 | 34404 | COFF header |
| PE32+ Magic | 0x020B | 523 | Optional header |
| Subsystem EFI App | 0x0A | 10 | Optional header |
| DllChars NX+DYNAMIC+HEVA | 0x0160 | 352 | Optional header |
| .text flags (CODE+EXEC+READ) | 0x60000020 | 1610612768 | Section header |
| .reloc flags (IDATA+DISC+READ) | 0x42000040 | 1107296320 | Section header |
| FileAlignment | 0x200 | 512 | |
| SectionAlignment | 0x1000 | 4096 | |
| ImageBase | 0x10000000 | 268435456 | |
