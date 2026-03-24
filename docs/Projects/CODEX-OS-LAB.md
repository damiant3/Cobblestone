# Codex.OS Lab — The Garage Box

**Date**: 2026-03-24
**Status**: Provisioned, WSL + QEMU + wasmtime installed
**Author**: Nut (Copilot, VS2026 Community on DESKTOP-F4FJQ8V)
**Depends on**: Peak III (Runtime), Camp III-A (Memory), Camp III-C (Concurrency)

---

## The Machine

A desktop PC pulled from a garage, cleaned up, and pressed into service as
the Codex.OS development and testing platform.

| Spec | Value |
|------|-------|
| Hostname | DESKTOP-F4FJQ8V |
| CPU | Intel Core i7-6700K @ 4.00 GHz (4 cores / 8 threads, Skylake) |
| RAM | 32 GB |
| Storage | Samsung SSD 850 EVO 500 GB (295 GB free) |
| GPU | NVIDIA GeForce GTX 970 (4 GB VRAM) |
| OS | Windows 10 Home |
| BIOS | AMI, 2015-08-31 |
| IDE | Visual Studio 2026 Community (18.4.2) |
| SDK | .NET 10.0.201 targeting net8.0 |
| WSL | Ubuntu (QEMU 8.2.2, wasmtime 43.0.0) |
| Git | 2.7.1 (ancient — upgrade recommended) |

**Known quirks from provisioning (2026-03-24):**
- Ghost NuGet source `C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages`
  from deleted VS2022. Fixed by creating the empty directory.
- Stale `LIB` env var pointing to Windows Kits 8.1. Cosmetic CS1668 warnings.
  Fix: install Windows 11 SDK via VS Installer, or ignore.
- Git 2.7.1 has no credential manager. Credentials configured manually.

---

## The Mission

This box exists to build and test **Codex.OS** — Peak IV of The Ascent.

The end state is **no Windows, no Linux, no foreign OS**. Codex.OS runs on
the bare hardware. The compiler produces the kernel. The type system enforces
capabilities. The effect system replaces syscalls. We own every byte from
the bootloader to the blinking cursor.

Getting there is a ladder. Each rung removes a dependency:

```
Rung 0 (now):    Windows + WSL + QEMU — develop and test in emulation
Rung 1:          Hyper-V VM — Codex.OS boots in a VM, Windows stays for Odin/VS
Rung 2:          Partition — Codex.OS boots on real hardware, dual-boot for tooling
Rung 3:          Codex.OS only — Windows is gone. The box runs what we built.
```

### What This Box Does

1. **Native backend verification** — RISC-V, ARM64, x86-64, WASM binaries
   compiled and tested via WSL + QEMU + wasmtime.
2. **x86-64 native execution** — the x86-64 backend produces ELF binaries
   that run directly in WSL on this CPU. No emulation.
3. **Codex.OS kernel development** — this box is the build-and-boot platform
   for the OS itself. First in QEMU, then in a VM, then on the bare metal.
4. **Phone flash station** — Odin runs here (for now). The Samsung S7 Edge is
   connected via USB. TWRP images are flashed from this box.
5. **Push to master** — Nut has full commit/push authority, same as any agent.

---

## Disk Layout — Current

Single partition, single OS:

```
C: NTFS 465 GB (295 GB free)
   ├── Windows 10 Home (build 19045)
   ├── VS2026 Community
   ├── .NET SDK 10.0.201
   ├── WSL2 (Ubuntu)
   └── Codex repo clone
```

## Boot Strategy

The goal is to run Codex.OS on this hardware. The question is how to get
from "Windows box with a compiler" to "Codex.OS box" without losing the
ability to develop along the way.

### Rung 0: QEMU (Current)

Codex compiles a flat binary. QEMU boots it. We already do this for
RISC-V bare metal (`qemu-system-riscv64 -bios none -kernel binary`).
The same works for x86-64:

```
qemu-system-x86_64 -nographic -no-reboot -kernel codex-os.bin
```

This is where all early kernel work happens. No risk. Iterate fast.
Serial output via QEMU's emulated UART. Debug with `-d in_asm,exec`.

### Rung 1: Hyper-V VM or QEMU-KVM

Windows 10 Home has the hypervisor platform enabled (WSL2 needs it) but
not the full Hyper-V Manager. Two sub-options:

**1a. QEMU with WHPX acceleration (Windows-native)**

QEMU on Windows can use the Windows Hypervisor Platform (WHPX) for
near-native speed:

```
qemu-system-x86_64 -accel whpx -nographic -kernel codex-os.bin
```

No Linux needed. Runs directly on Windows. The Codex.OS kernel gets
real hardware-speed execution in a VM. This is probably the first
move — test on this before touching any partition.

**1b. QEMU-KVM in WSL2 (if KVM passthrough works)**

WSL2 on recent Windows builds can expose `/dev/kvm`. Currently not
available on this box (checked). May require a Windows update or
insider build. If it works, QEMU in WSL gets hardware acceleration.

**1c. Full Hyper-V (requires Windows 10 Pro/Enterprise)**

Windows 10 Home doesn't include Hyper-V Manager. Options:
- Upgrade to Pro ($99 — or free if the old license was Pro)
- Use the WHPX path instead (same hypervisor, different interface)

### Rung 2: Partition + Real Boot

When Codex.OS can boot to a serial console, manage memory, and do I/O,
we partition the SSD:

```
C: NTFS ~250 GB — Windows (VS, Odin, development)
D: raw  ~200 GB — Codex.OS (our bootloader, our kernel, our filesystem)
```

GRUB or a minimal bootloader on the MBR/ESP lets us choose at power-on.
Or: the Codex.OS bootloader IS the bootloader — it chain-loads Windows
if you hold a key, otherwise it boots Codex.OS.

The x86-64 backend already produces ELF binaries. What we need:
- Multiboot2 header (so GRUB can load us), OR
- UEFI application format (the SSD likely uses GPT + UEFI)
- VGA text mode or serial output for early console
- Basic interrupt handling (keyboard, timer)

### Rung 3: Codex.OS Only

Windows is gone. The box boots Codex.OS. The compiler runs on Codex.OS.
The editor runs on Codex.OS. We develop Codex.OS *in* Codex.OS.

This is Peak IV. Not soon. But the path is clear.

### Recommendation

**Rung 0 now. Rung 1a (QEMU + WHPX) as soon as we have a bootable kernel
image. Rung 2 when we need real hardware I/O (disk, USB, network).
Rung 3 when we're ready to live there.**

The key insight: we don't need to repartition or dual-boot until Rung 2.
QEMU with WHPX gives us near-native x86-64 execution in a VM, running
directly on Windows, no Linux involved. That's enough for kernel dev
until we need real device drivers.

---

## Agent: Nut

The agent on this box is **Nut** — GitHub Copilot in VS2026 Community.

Named for the simplest piece of trad climbing protection: a metal wedge
slotted into a crack in the rock. No moving parts. Relies on geometry.
The oldest and most trusted gear on the rack. Also: kernel → nut.

| Agent | Home | Notes |
|-------|------|-------|
| Agent Windows | Main box, VS + Copilot | First agent. Builds features, reviews, pushes. |
| Agent Linux | Claude on sandbox | Tests on real hardware/emulators, traces execution. |
| Cam | Claude Code CLI (1M Opus) | Fast iteration, parallel work, GDB debugging. |
| **Nut** | **Garage box, VS2026 + Copilot** | **Hardware lab, OS dev, phone flash.** |

Roles aren't rigid. Any agent can push to master. Any agent can build
features, review, test, or debug. The home base is just where each agent
lives — the work goes wherever it needs to go.

### Branch Convention

To avoid stomping on the main box's branches:
- Main box: `windows/<topic>`, `linux/<topic>`, `staging/<topic>`
- This box: `nut/<topic>`

---

## Next Steps

1. ~~Provision WSL + Ubuntu~~ ✅
2. ~~Install QEMU (riscv64, aarch64, system-riscv64)~~ ✅
3. ~~Install wasmtime~~ ✅
4. Run native backend tests from WSL to verify the full matrix
5. Install QEMU for Windows + verify WHPX acceleration works
6. Flash phone with TWRP (see `docs/Projects/PHONE-WIPE.md`)
7. `adb push` an ARM64 Codex binary to the phone
8. Build a minimal x86-64 bootable kernel image from Codex
9. Boot it in QEMU (Rung 0), then QEMU+WHPX (Rung 1a)
10. When kernel has serial console + memory: partition SSD (Rung 2)
11. When kernel is self-sustaining: delete Windows (Rung 3)

---

*This box is expendable. The phone is expendable. The data is in git.
Windows is a temporary host. Linux is a temporary crutch. The destination
is Codex.OS on bare metal — nothing between our code and the hardware.*
