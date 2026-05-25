# codex-vm Simulated UEFI Firmware

**Created**: 2026-05-23
**Status**: Active — analysis complete, implementation not started
**Motivation**: The UEFI dev console boots on QEMU+OVMF and on the
ASUS TUF board (intermittently), but codex-vm's UEFI trap-page
emulation is too minimal to test the full application. Source browsing
(Simple File System Protocol), correct device enumeration (Loaded Image
Protocol), and realistic memory maps are all missing. Building these
into codex-vm means we can develop and test the UEFI app without
flashing USB sticks or juggling QEMU windows.

## Current State

codex-vm (`tools/codex-vm.c`, ~2600 lines C) uses Windows Hypervisor
Platform. The `-uefi` flag enables trap-page dispatch: fake UEFI tables
at 0xF0000, a page of HLT opcodes at 0xF1000. Each UEFI function is
a HLT at a known offset; the vmexit handler decodes the trap address to
a function ID, reads register arguments, and emulates the call.

### What works

| Protocol / Service | Trap IDs | Status |
|--------------------|----------|--------|
| ConOut (OutputString, SetAttribute, ClearScreen, SetCursorPosition, EnableCursor) | 4-12 | Working |
| ConIn (ReadKeyStroke, ReadKeyStrokeEx) | 0-1, 20 | Working |
| AllocatePages (AnyPages, MaxAddress, Address) | 30 | Working |
| FreePages / FreePool | 31, 34 | No-op stubs |
| GetMemoryMap | 32 | Minimal stub (single entry, fixed MapKey) |
| AllocatePool | 33 | Working (bump allocator) |
| ExitBootServices | 35 | No-op stub |
| Stall, SetWatchdogTimer | 36-37 | No-op stubs |
| HandleProtocol | 38 | Returns EFI_NOT_FOUND |
| LocateHandleBuffer | 39 | Returns EFI_NOT_FOUND |
| GOP (QueryMode, SetMode, Blt) | 41-44 | Working (3 modes, host-side framebuffer) |

### What's missing (blocks dev console features)

| Protocol / Service | Needed For | Difficulty |
|--------------------|------------|------------|
| **LocateProtocol** | Finding Block I/O, Simple File System | Medium |
| **Block I/O Protocol** | Reading raw disk sectors (fat16-read-text) | Medium |
| **Simple File System Protocol** | uefi-read-file (file-by-name access) | Medium-Large |
| **Loaded Image Protocol** | Finding the boot device handle | Small |
| **Device Path Protocol** | Identifying device topology | Small |
| **Runtime Services: GetTime** | RTC without CMOS port hacking | Small |
| **Realistic memory map** | Match ASUS TUF AMI Aptio V layout | Small |
| **LocateHandle (by protocol GUID)** | Enumerate handles with a given protocol | Medium |

## Target Hardware: ASUS TUF (2015)

The ASUS TUF board is a Z97 or Z170 chipset, circa 2015, running
**AMI Aptio V** UEFI firmware. Key behaviors to replicate:

### Memory Map

Real AMI Aptio V on this hardware reports:

| Range | Type | Notes |
|-------|------|-------|
| 0x0 — 0x3FF | Reserved | Interrupt Vector Table |
| 0x400 — 0x4FF | Reserved | BIOS Data Area |
| 0x500 — 0x9FBFF | EfiConventionalMemory | Low conventional memory (~639 KB) |
| 0x9FC00 — 0x9FFFF | Reserved | Extended BIOS Data Area (EBDA) |
| 0xA0000 — 0xBFFFF | Reserved | VGA framebuffer |
| 0xC0000 — 0xFFFFF | Reserved | ROM / firmware shadow |
| 0x100000 — ~0x7EFFFFFF | EfiConventionalMemory | Main RAM (up to ~2 GB) |
| 0xF0000000+ | Reserved | PCI MMIO / firmware tables |

Key differences from OVMF:
- EBDA is larger (up to 128 KB below 0xA0000 on some AMI boards)
- More reserved regions from Option ROMs and SMM
- AMI's DXE allocator prefers high-to-low allocation
- AllocateMaxAddress at 0xFFFFF succeeds but returns from the
  0x500-0x9FBFF region, not from above 1 MB

### USB Boot Enumeration

On the ASUS TUF, the USB boot disk's Block I/O handle is typically
the **first** handle enumerated (it's the boot device). This is why
`LocateProtocol` for Block I/O works on real hardware but fails on
QEMU/OVMF (where the first Block I/O is the NVRAM flash).

### AllocatePages at Low Addresses

The PE stub uses `AllocatePages(AllocateMaxAddress, EfiLoaderCode,
N, &0x100000)` to place code below 1 MB. On ASUS TUF, this sometimes
fails for larger binaries — the region is small and fragmented by
EBDA and legacy structures. CL 2019 added status checks (`test rax,
rax; jz ok; hlt`) that halt cleanly on failure.

## Design: Phased Implementation

### Phase 1: Protocol Handle Infrastructure (prerequisite for everything)

The current trap-page dispatch has no concept of handles or protocol
GUIDs. We need:

1. **Handle table**: a static array of handles, each with a list of
   installed protocol GUIDs and pointers to protocol interface structs
   in guest memory.

2. **LocateProtocol implementation**: walk the handle table, find the
   first handle with the requested GUID, return the protocol interface
   pointer. Currently returns EFI_NOT_FOUND.

3. **HandleProtocol implementation**: given a handle and GUID, return
   the protocol interface. Currently returns EFI_NOT_FOUND.

4. **LocateHandle / LocateHandleBuffer**: enumerate all handles with
   a given protocol. Currently returns EFI_NOT_FOUND.

**GUID matching**: UEFI protocol GUIDs are 16-byte structures. The
guest passes a pointer to a GUID in guest memory. codex-vm reads it,
compares against known GUIDs, and dispatches.

**Known GUIDs to support**:

```c
// Block I/O: 964E5B21-6459-11D2-8E39-00A0C969723B
// Simple File System: 964E5B22-6459-11D2-8E39-00A0C969723B
// Loaded Image: 5B1B31A1-9562-11D2-8E3F-00A0C969723B
// Device Path: 09576E91-6D3F-11D2-8E39-00A0C969723B
// GOP: 9042A9DE-23DC-4A38-96FB-7ADED080516A
// ConOut (Simple Text Output): 387477C2-69C7-11D2-8E39-00A0C969723B
// ConIn (Simple Text Input): 387477C1-69C7-11D2-8E39-00A0C969723B
```

**Implementation**: ~100 lines C. Static array of 4-8 handles,
pre-populated at UEFI setup time. GUID comparison is `memcmp`.

### Phase 2: Block I/O Protocol

The existing IDE disk emulation (`-disk file.img`) provides raw sector
access. Wire this into UEFI:

1. **Protocol struct in guest memory**: allocate space in the UEFI
   table page for a Block I/O protocol interface with trap addresses
   for ReadBlocks, WriteBlocks, FlushBlocks.

2. **New trap IDs**: UEFI_TRAP_BLK_READBLOCKS (50),
   UEFI_TRAP_BLK_WRITEBLOCKS (51), UEFI_TRAP_BLK_FLUSH (52).

3. **ReadBlocks handler**: reads from the disk image file into guest
   memory. Arguments: MediaId, LBA, BufferSize, Buffer (guest addr).

4. **Media descriptor**: block size = 512, media present = true,
   read-only = false, logical partition = false.

5. **Install on handle 0** (the boot disk handle).

**Implementation**: ~60 lines C (trap handlers + protocol setup).

**This alone fixes source browsing** — `fat16-read-text` uses
`LocateProtocol` to find Block I/O, reads sectors, parses FAT16.
If codex-vm returns the disk image's Block I/O as the first protocol
match, the dev console reads SOURCE.SRC correctly.

### Phase 3: Simple File System Protocol

For the `uefi-read-file` builtin (currently disconnected due to
assembly bugs in the helper):

1. **OpenVolume trap**: returns an EFI_FILE_PROTOCOL handle for the
   root directory of the FAT16 partition in the disk image.

2. **File protocol traps**: Open, Close, Read, GetInfo, SetPosition.

3. **FAT16 parsing in C**: codex-vm reads the FAT16 filesystem from
   the disk image, resolves filenames, and provides file content to
   the guest.

**Implementation**: ~200 lines C (FAT16 directory scan, cluster chain
walk, file read). This is the largest single piece.

**Alternative**: skip this phase entirely. If Phase 2 (Block I/O)
works, `fat16-read-text` already handles FAT16 parsing in Codex. The
Simple File System Protocol is only needed if we want the `uefi-read-file`
builtin to work, which requires fixing its assembly helper anyway.

### Phase 4: Loaded Image Protocol + Device Path

1. **Loaded Image**: returns a struct describing the loaded UEFI app —
   image base, image size, device handle (the boot disk handle from
   Phase 2), file path.

2. **Device Path**: minimal — a single EndEntire node is sufficient
   for our purposes.

3. **Install Loaded Image on the image handle** (passed as RCX to the
   UEFI entry point — currently 0, should be a real handle).

**Implementation**: ~40 lines C.

### Phase 5: Realistic Memory Map

Replace the current single-entry stub with a map matching ASUS TUF:

```c
// Memory map entries for AMI Aptio V (ASUS TUF ~2015)
{ 0x00000, 0x500,      EfiReservedMemoryType },  // IVT + BDA
{ 0x00500, 0x9F500,    EfiConventionalMemory },  // low conventional
{ 0x9FC00, 0x400,      EfiReservedMemoryType },  // EBDA
{ 0xA0000, 0x20000,    EfiReservedMemoryType },  // VGA
{ 0xC0000, 0x40000,    EfiReservedMemoryType },  // ROM shadow
{ 0x100000, RAM_TOP,   EfiConventionalMemory },  // main RAM
{ 0xF0000000, 0x10000, EfiMemoryMappedIO },      // PCI MMIO stub
```

Also: make AllocatePages respect the map. Currently it bumps from
0x10000000 without checking. Should allocate from the conventional
memory regions, preferring high addresses (matching AMI behavior).

**Implementation**: ~50 lines C (static map, linear scan allocator).

### Phase 6: Runtime Services (GetTime)

The dev console currently reads the RTC via CMOS ports (112/113).
In UEFI mode, the canonical way is RuntimeServices.GetTime.

1. **New trap**: UEFI_TRAP_RT_GETTIME (60).
2. **Handler**: reads host time via `GetLocalTime()` Win32 API,
   writes an EFI_TIME struct to guest memory.

**Implementation**: ~20 lines C.

## Priority Order

1. **Phase 1 + Phase 2** — unlocks source browsing in codex-vm via
   the existing `fat16-read-text` path. No changes to the Codex
   compiler needed.
2. **Phase 5** — realistic memory map catches allocation bugs before
   they hit real hardware.
3. **Phase 4** — Loaded Image gives the guest a proper boot device
   handle for future use.
4. **Phase 6** — GetTime replaces CMOS hacking.
5. **Phase 3** — Simple File System only matters if/when the
   `uefi-read-file` assembly helper is debugged.

## Estimated Size

| Phase | Lines C | Depends On |
|-------|---------|------------|
| 1 (Handles + LocateProtocol) | ~100 | — |
| 2 (Block I/O) | ~60 | Phase 1 |
| 3 (Simple File System) | ~200 | Phase 2 |
| 4 (Loaded Image + DevPath) | ~40 | Phase 1 |
| 5 (Memory map) | ~50 | — |
| 6 (GetTime) | ~20 | — |
| **Total** | **~470** | |

The VM is currently ~2600 lines. This adds ~18% to reach ~3070 lines,
keeping it well within the "small C program" design goal.

## What We Don't Build

- **Full UEFI firmware (EDK2/OVMF)**: 500K+ lines of code. We don't
  need a conforming implementation — we need enough to test our app.

- **USB stack emulation**: not needed. The `-disk` flag already gives
  us a virtual boot disk.

- **Secure Boot / TPM**: not needed for dev console testing.

- **Network boot (PXE)**: not needed. The NE2K NIC already works for
  bare-metal networking.

- **Multiple disk devices**: one disk is enough. The whole point is
  that codex-vm returns the right Block I/O on the first LocateProtocol
  call, unlike OVMF.

## Testing Strategy

1. Build the dev console PE image (`build/build-boot-img.ps1`)
2. Run in codex-vm: `codex-vm -uefi -disk seed/Codex.img seed/Codex.img`
3. Verify: menu works, clock ticks, system info shows codex-vm
   firmware vendor, source browsing lists chapters from SOURCE.SRC
4. Compare behavior with QEMU+OVMF and ASUS TUF hardware
5. Run the PE through all three platforms and confirm identical
   visible behavior

## References

- `tools/codex-vm.c` — current VM implementation
- `docs/Designs/Done/CODEX-VM-UEFI.md` — original UEFI trap-page design
- `docs/Reference/UEFI_Spec_Summary.md` — PE32+ header and protocol layout
- `docs/Designs/Active/Hardware/REAL-HARDWARE-BRINGUP.md` — broader hardware plan
- UEFI Spec 2.10 Boot Services: https://uefi.org/specs/UEFI/2.10/07_Services_Boot_Services.html
- UEFI Spec 2.10 Media Protocols: https://uefi.org/specs/UEFI/2.10/13_Protocols_Media_Access.html
- AMI Aptio V architecture: commercial UEFI firmware on ASUS TUF boards
- EDK2/OVMF source: https://github.com/tianocore/edk2 (OvmfPkg)
