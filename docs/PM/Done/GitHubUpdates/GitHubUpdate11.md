# GitHub Update 11 -- CL 1115 to CL 1168 (2026-05-07)

Previous update: CL 1114 (GitHubUpdate10).
This update: CL 1168.

## VMX Hypervisor + codex-vm.exe (CLs 1144-1162, Cam)

Codex can now host virtual machines. A Codex-native VMX hypervisor and
a 400-line C WHP-based VM host (`codex-vm.exe`) replace QEMU for
compilation and test workloads.

### VMX builtins (CL 1144)

Seven new hardware builtins: `vmxon`, `vmclear`, `vmptrld`,
`vmlaunch`, `vmresume`, `vmread`, `vmwrite`. Three-place registration
(TypeEnv, NameResolver, X86_64Builtins). Laid the foundation for
in-Codex virtualization.

### DevHypervisor (CLs 1145-1150)

Application-level hypervisor chapter in `codex.works/`:
- Guest physical memory allocation (4 MB identity-mapped)
- CPUID emulation (vendor + feature flags)
- MSR emulation (EFER, PAT, FS/GS/KERNEL_GS_BASE)
- Extended Page Tables (EPT) with 2 MB pages
- Full VMX lifecycle: VMXON region, VMCS allocation, control setup
- WRMSR emulation with serial-port passthrough (port 0x3F8)

### Virtual device models (CLs 1147-1148)

- **VmSerial** -- 16550 UART: DLAB-aware register access, baud/LCR/MCR/
  IER config, transmit buffer, LSR/MSR/IIR status, interrupt support.
- **VmIde** -- ATA PIO: command register file, IDENTIFY DEVICE, sector
  read/write (PIO), status/error/DRQ handling, 512-byte sector buffer.

### codex-vm.exe (CL 1154)

Drop-in QEMU replacement. 400 lines of C using Windows Hypervisor
Platform (WHP). TCP serial protocol on configurable port. Features:
- WHV partition with 1 vCPU
- Multiboot kernel loading (same protocol as QEMU `-kernel`)
- Serial I/O emulation (COM1 + COM2 via TCP)
- MMIO/PIO exit handling
- QEMU-compatible `-serial tcp:` flag syntax

### PIT interrupt emulation (CL 1162)

Timer interrupt injection (IRQ 0, vector 32) on HLT with IF=1. 55ms
period (18 Hz). Unblocked seed CDX booting under codex-vm -- the boot
sequence waits for a PIT tick before proceeding past S0-HW.

### Serial fixes (CL 1160)

DLAB tracking for baud rate configuration. LSR peek (bit 0 for
data-ready) vs recv separation. IIR and MSR register returns. Harness
integration: opt-in via `$env:CODEX_VM=1`.

### rdmsr/wrmsr/vmlaunch-full/vmresume-full (CL 1165)

Four new builtins for MSR access and VMX entry with full control-flow
return. VMX capability MSR discovery for IA32_VMX_BASIC,
IA32_VMX_PINBASED_CTLS, IA32_VMX_PROCBASED_CTLS.

## UEFI Console Builtins + Dev Console (CLs 1166-1167, Cam)

### UEFI console builtins (CL 1166)

Six new builtins wrapping UEFI ConOut and ConIn protocols:
`uefi-set-attr`, `uefi-clear-screen`, `uefi-set-cursor`,
`uefi-enable-cursor`, `uefi-set-mode`, `uefi-read-key`.

### UEFI dev console (CL 1167)

Interactive colored menus via ConOut/ConIn:
- **UefiConsole** -- typed abstraction: 16 EGA colors, key input
  (scan codes + char codes), cursor control, blocking/polling reads.
- **DevConsoleMenu** -- data-driven menu system with keyboard
  navigation (arrow keys, enter, escape), wrapping selection.
- **DevConsoleBoot** -- boot-time entry point: hold Escape/F12
  during UEFI boot to enter dev console. Source tree indexing
  across all quires.
- **write-usb.ps1** -- pure PowerShell USB writer via Win32
  P/Invoke. No external tools.

## VM Build Tools (CL 1153, Nib)

Codex-native build pipeline chapters:
- **VmCompile** -- compile source under a VM
- **VmRunner** -- boot and capture output from a VM
- **VmPingpong** -- fixed-point verification under a VM
- **VmSweep** -- sample battery under a VM

## Source Embedding + FAT16 8MB Image (CLs 1152-1156, Nib)

### SourceConcat (CL 1152)

Codex-native transitive foreword resolution. Reads all cited library
chapters recursively and concatenates into a single source blob.
Replaces `concat-codex-self.ps1` for in-Codex use.

### FAT16 8MB image with source (CL 1155)

IMG compile mode upgraded from 4MB to 8MB FAT16. `SOURCE.CDX` file
embedded in the disk image containing the full compiler source
(~1 MB). The UEFI-booted system can read its own source from disk.

## bit-shr/shru Split (CLs 1134-1139, Nib)

`bit-shr` was previously logical shift right (SHR). Split into:
- `bit-shr` -- arithmetic shift right (SAR), sign-extending
- `bit-shru` -- logical shift right (SHR), zero-extending

78-file codebase migration (CL 1139) updated all call sites.

## peek-qword + trie-prefix-keys (CLs 1126-1129, Nib)

- `peek-qword` builtin -- read 8 bytes from a memory address as
  a 64-bit integer. Used by UEFI console for system table access.
- `trie-prefix-keys` -- returns all keys with a given prefix from
  a Trie. Used by code browser for symbol search.

## Sweep Disk Sidecar Support (CL 1161, Cam)

Sweep harness recognizes `.disk` sidecar files for samples that
need an IDE disk attached during execution. `run-for-sweep.ps1`
attaches the disk image automatically.

## FAT16 IMG OOM Fix (CL 1168, Nib)

IMG mode OOM crash fixed. Root cause: frontend (~260 MB) + emit deck
(~730 MB) = ~990 MB, barely fitting in 1 GB. IMG mode adds PE
intermediates (~60 MB), exceeding 1 GB.

Fix: after `finalize-cdx` produces CDX bytes, save them to raw memory
at the emit deck base (which is dead post-finalize), then
`__heap-restore` to reclaim the entire ~730 MB emit deck. Read CDX
back, build PE, allocate IMG buffer -- all in the reclaimed space.
Peak heap drops from ~990 MB to ~350 MB.

## Dead Script Cleanup (CL 1141, Cam)

18 obsolete PowerShell scripts deleted (make-efi.ps1,
make-usb-image.ps1, test-*.ps1, etc.).

## Seed

- **seed/Codex.cdx**: 1,965,888 bytes
- **seed/Codex.img**: 8,388,608 bytes (8 MB FAT16 GPT)
- Compiler: 55 files, ~21,600 lines
- Modules: 295 across 19 quires
- Test samples: 212 (183 apps + 29 error tests)
- Sweep: 173 dispatched, 167 pass, 6 skip, 0 fail
