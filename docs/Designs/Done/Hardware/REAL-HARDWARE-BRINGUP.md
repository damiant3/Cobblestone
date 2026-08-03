# Real Hardware Bring-Up Plan

**Created**: 2026-05-05
**Status**: Active
**Trigger**: VGA terminal demo works in QEMU; USB boot fails on ASUS TUF board.

## Context

The Codex OS VGA terminal (keyboard-driven REPL, interactive on bare metal)
is proven in QEMU. Booting from USB on real hardware fails silently -- the
BIOS boot sector has two showstopper bugs and the kernel assumes QEMU-only
hardware.

## Boot Sector Bugs (codex.build/bootsect.asm)

### Bug 1: sector_count never patched

The image builder (`seed/Codex.img` construction in the seed rebuild
checklist) concatenates `bootsect.bin + Codex.cdx` but never writes the
CDX size into the boot sector's `sector_count` field. The boot sector
reads 0 sectors then jumps to empty memory at 0x100020.

**Fix**: image builder computes `ceil(cdx_size / 512)` and patches it
at the `sector_count` offset in the first 512 bytes.

### Bug 2: 32-bit protected mode only

The boot sector sets up a 32-bit GDT and jumps to 0x100020 in 32-bit
protected mode. The CDX code is compiled for x86-64 (long mode). QEMU's
multiboot loader handles the 64-bit transition internally -- the boot
sector doesn't.

**Fix**: after loading the CDX, set up identity-mapped page tables
(PML4 → PDPT → PD with 2MB pages), enable PAE (CR4.PAE), set
IA32_EFER.LME, enable paging (CR0.PG), load a 64-bit GDT, far-jump
to a 64-bit code segment, then jump to 0x100020.

## Bring-Up Checklist

### Phase 1: BIOS Boot (makes USB boot work)

| # | Item | What | Size | Status |
|---|------|------|------|--------|
| 1 | Patch sector count | Image builder writes CDX sector count into bootsect | Small | Not started |
| 2 | 64-bit long mode transition | Page tables, PAE, IA32_EFER.LME, CR0.PG, 64-bit GDT | Medium | Not started |
| 3 | VGA mode set | INT 10h AH=00 AL=03 before loading CDX | Tiny | Not started |
| 4 | INT 13h extensions check | AH=41h probe, error message if not supported | Small | Not started |
| 5 | A20 fallback | Current uses INT 15h/2401h only; add port 0x92 fallback | Small | Not started |

### Phase 2: Kernel Hardening (survive non-QEMU hardware)

| # | Item | What | Size | Status |
|---|------|------|------|--------|
| 6 | Serial-optional boot | Don't hang on COM init if UART not present; probe THR first | Small | Not started |
| 7 | Missing-NIC guard | NIC init fails gracefully if no NE2000 at 0x300 | Small | Not started |
| 8 | PCI enumeration | Detect devices at boot instead of hardcoded addresses | Medium | Not started |
| 9 | Memory map (E820) | INT 15h/E820h before leaving real mode; pass to kernel | Medium | Not started |

### Phase 3: UEFI Boot Path (modern boards)

| # | Item | What | Size | Status |
|---|------|------|------|--------|
| 10 | EFI stub loader | PE-format EFI app that loads CDX, sets up long mode | Large | Not started |
| 11 | EFI System Partition | FAT32 ESP with EFI/BOOT/BOOTX64.EFI | Medium | Not started |
| 12 | GOP framebuffer | Use UEFI Graphics Output Protocol instead of VGA text | Large | Not started |

### Phase 4: Driver Generalization

| # | Item | What | Size | Status |
|---|------|------|------|--------|
| 13 | AHCI/SATA driver | Replace ATA PIO with AHCI for modern disks | Large | Not started |
| 14 | USB HID keyboard | Replace PS/2 with USB keyboard support | Large | Not started |
| 15 | xHCI/EHCI stack | USB host controller for real USB devices | Very large | Not started |
| 16 | PCIe NIC driver | Intel e1000 or Realtek 8139 instead of NE2000 | Medium | Not started |

## Testing Strategy

- **Phase 1**: boot the .img from USB on the ASUS TUF board. Success = VGA terminal appears.
- **Phase 2**: kernel doesn't crash on missing hardware. Success = graceful fallback messages.
- **Phase 3**: boot on UEFI-only boards (no CSM). Success = same VGA terminal.
- **Phase 4**: interact with real disks, real NICs, real USB devices.

## Hardware Test Targets

| Board | Era | Boot | Notes |
|-------|-----|------|-------|
| ASUS TUF (Damian's garage) | ~2015? | BIOS/UEFI | First target. USB boot fails currently. |
| (add more as tested) | | | |

## Notes

- The QEMU WHPX path remains canonical for development. Real hardware
  is a demo/deployment target.
- Boot sector changes do NOT require the seed dance (boot sector is
  outside the compiler's fixed point). Only `bootsect.asm` + image
  builder changes.
- Phase 1 items 1-3 are likely sufficient to boot on the ASUS board
  if Legacy/CSM boot is enabled in BIOS setup.
