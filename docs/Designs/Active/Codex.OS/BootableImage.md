# Bootable Disk Image (`seed/Codex.img`)

**Date**: 2026-05-06
**Status**: Implementation in progress (depends on CL 987 PE32+ emitter)

## Purpose

`seed/Codex.img` is the project's distribution artifact. It is a
UEFI-bootable GPT disk image that contains everything needed to boot
Codex on real hardware, compile programs, and continue development of
the project itself. Someone with only the `.img` can run to the hills.

## Image Layout

```
GPT disk (64 MB default):
  Sector 0           : Protective MBR
  Sector 1           : GPT header
  Sectors 2-33       : GPT partition entries
  Sectors 2048..end  : EFI System Partition (FAT32)

FAT32 filesystem contents:
  EFI/BOOT/BOOTX64.EFI    UEFI-bootable Codex compiler (PE32+ from CDX)
  seed/Codex.cdx           Self-hosting compiler CDX binary
  codex/                   Compiler source (52 files)
  codex.foreword/          Foreword libraries
  codex.foreword.*/        Foreword sub-quires (game, signal, compress, etc.)
  codex.kernel/            Kernel source
  codex.os/                OS source
  codex.os.*/              OS sub-quires (trust, net, verify, replay, sched, observe)
  codex.works/             Works source
  codex.test/              Test suite
  codex.build/             Build scripts
  docs/                    Documentation
```

## Boot Process

1. UEFI firmware reads GPT, finds ESP, loads `EFI/BOOT/BOOTX64.EFI`
2. PE32+ UEFI stub calls `AllocatePages` at 0x100000, copies Codex
   text+rodata sections, disables LAPIC, jumps to `__start`
3. `__start` (Codex-compiled) sets up stack, heap, page tables,
   enters the Codex kernel
4. Kernel reads serial (COM1) for commands, compiles programs

## Build Pipeline

```
seed/Codex.cdx
  → make-efi.ps1       → BOOTX64.EFI (PE32+ UEFI application)
  → make-usb-image.ps1 → seed/Codex.img (GPT + FAT32 + dev surface)
```

Orchestrated by `codex.build/build-boot-img.ps1`, called from
step 4 of the seed rebuild checklist.

Once CL 987 (PE32+ emitter) lands, the CDX→PE32+ conversion
moves inside the compiler itself (`-Efi` emit mode), replacing
the `make-efi.ps1` script with the Codex-native `PeWriter.codex`.

## Hardware Requirements

- x86-64 CPU with UEFI firmware
- USB boot support
- Serial port (COM1, 0x3F8) for program I/O — USB-to-serial works
- No BIOS/CSM required (pure UEFI)

## Image Size

Target: 64 MB. Contents are ~10 MB (CDX ~1.8 MB, EFI ~3.5 MB,
source+docs ~5 MB). Remaining space is available for user programs
and data once the OS gains filesystem write support.

## Legacy BIOS Boot

The previous `Codex.img` format (512-byte boot sector + raw CDX)
is retired. The BIOS boot path (`bootsect.asm`, `build-disk-image.ps1`)
remains in the repo for reference but is no longer the seed artifact.
UEFI is the only supported boot path going forward.
