# GitHub Update 10 — CL 1059 to CL 1114 (2026-05-07)

Previous update: CL 1058 (GitHubUpdate9).
This update: CL 1114.

## Real Hardware UEFI Boot (CLs 1059-1107, Nib)

Codex boots on real hardware. "Welcome to Codex" prints on an Asus
x86-64 motherboard from a USB stick, compiled entirely by the Codex
compiler, with no external toolchain in the build path.

Two bugs in PeWriter.codex were the root cause of every black-screen
failure on real hardware:

- **Stack alignment** (CL 1104) — PE stub prolog did `sub rsp, 64`
  after 8 register saves, leaving RSP at 8 mod 16 before firmware
  calls. Real hardware ConOut drivers use SSE `movaps` which faults
  on misaligned stack. OVMF tolerates misalignment; Asus firmware
  does not. Fix: `sub rsp, 40` (3 sites: kernel prolog, UEFI app
  prolog, epilogue).

- **ImageBase 0x10000000** (CL 1104) — PE optional header specified
  a 256 MB preferred load address. With only a dummy .reloc section
  (no real entries), firmware that couldn't allocate at that address
  silently refused to load the PE. Fix: ImageBase = 0 (2 sites).

### Diagnostic methodology

Bugs were found by systematic isolation on real hardware using the
UEFI Shell (pbatard/UEFI-Shell v2.2, 26H1 release):

1. Boot UEFI Shell from USB — confirms firmware, GPT, FAT32 all work.
2. Run hand-written NASM diagnostic binaries from the shell:
   - DIAG1: just `xor eax,eax; ret` — confirms PE loading works.
   - DIAG2: ClearScreen + OutputString + halt — confirms ConOut works.
   - DIAG3: exact UEFI spec calling convention — confirms correct ABI.
3. Run third-party hello world (j-m-li/uefi-nasm) — hangs. Uses
   `sub rsp, 48` which misaligns the stack. Same bug as PeWriter.
4. Run compiler-generated PE — 4 dots (stub diagnostics), then hang.
   Stub works; compiled code's ConOut call faults on alignment.
5. Fix alignment, test — prints but firmware skips to next boot entry.
   ImageBase too high, firmware can't load without relocations.
6. Fix ImageBase to 0 — boots and prints on real hardware.

### Secure Boot

Asus firmware had Secure Boot enabled by default. Unsigned .efi
binaries are silently rejected (black screen, no error). Must disable
Secure Boot or clear platform keys before testing unsigned binaries.

## PE Stub Copy Offset Bug (CL 1100, Nib)

The UEFI app stub used `rbx` (set by `call; pop rbx`) as the base
for text/rodata copy offsets. But `rbx` points to the `call`
instruction, not the stub start. Text and rodata copies were shifted
by the prolog size, copying garbage. Fix: adjust offsets by prolog
length.

## UEFI App PE Hardening (CLs 1090-1097, Nib)

- **Dummy .reloc section** — prevents firmware from rejecting the PE
  outright. 8-byte block header, no entries.
- **DllCharacteristics = 0x0160** — NX_COMPAT + DYNAMIC_BASE +
  HIGH_ENTROPY_VA. Required by post-2020 boards.
- **Diagnostic dots** — PE stub prints a dot after each stage
  (prolog, AllocatePages, low-page alloc, section copy) via
  ConOut->OutputString. Visible progress on real hardware.

## Pure-PowerShell Toolchain (CLs 1107, Nib)

All Linux/WSL dependencies removed from the UEFI build path:

- `make-usb-image.ps1` — builds GPT + FAT32 disk image byte-by-byte
  in pure PowerShell. Protective MBR, GPT headers with CRC32, ESP
  partition, FAT32 BPB/FSInfo/FAT/directories.
- `write-usb.ps1` — raw disk writer using Win32 CreateFile/WriteFile
  via P/Invoke. Locks volume, dismounts, writes image, no external
  tools.
- CLAUDE.md updated: "Never use python, WSL, or Unix tools."

## Prose Fixes (CLs 1102-1103, Cam)

- **Parser banned-word enforcer** — bans vague words ("it", "this",
  "some", "may") in load-bearing prose after "We say:" sections.
- **Foreword-all-compile test** — mega test citing all 158 foreword
  chapters, catches prose parse errors.
- **Sha512 prose indentation** — continuation lines at column 4+
  weren't recognized as prose by the parser (expects column 2).
- **List.codex cl- prefix** — renamed ConsList functions to avoid
  shadowing builtins (cons -> cl-cons, head -> cl-head, etc.).

## Kernel PE Hardening (CL 1110, Cam)

- **Page tables in stub** — kernel PE stub now builds its own identity-
  mapped page tables (4 GB, 2 MB pages) and loads CR3 via a trampoline
  at 0x7000. Previously external to make-efi.ps1.
- **Kernel .reloc section** (CL 1113) — kernel PE gets the same dummy
  .reloc + DllCharacteristics treatment as the UEFI app PE.
- **build-boot-img.ps1** rewritten to use compiler's `-Efi` mode
  instead of make-efi.ps1 wrapper.

## FAT16 Auto-Sized Boot Image (CL 1114, Nib)

Codex.img shrunk from 64 MB to 4 MB. `make-usb-image.ps1` rewritten
from FAT32 to FAT16 with auto-sizing based on EFI file size (minimum
4 MB). The 64 MB FAT32 image was 16x larger than needed and bloated
both the Perforce depot and git history.

## Seed Rebuild

- **seed/Codex.cdx**: 1,885,424 bytes (signed, CDX fixed-point)
- **seed/Codex.img**: 4 MB UEFI-bootable GPT image (FAT16)
- Boots "Welcome to Codex" on real Asus x86-64 hardware
