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

The main development box runs the three-agent team (Agent Windows, Agent
Linux, Cam) on the compiler, runtime, and repository. This box is the
**hardware lab**: the place where Codex code meets real metal and real
peripherals.

### What This Box Does

1. **Native backend verification** — RISC-V, ARM64, x86-64, WASM binaries
   compiled and tested via WSL + QEMU + wasmtime.
2. **x86-64 native execution** — the x86-64 backend produces ELF binaries
   that run directly in WSL on this CPU. No emulation.
3. **Codex.OS kernel development** — when the runtime (Peak III) is ready,
   this box becomes the build-and-boot platform for the OS itself.
4. **Phone flash station** — Odin runs here. The Samsung S7 Edge is
   connected via USB. TWRP images are flashed from this box.

### What This Box Does NOT Do

- Primary compiler development (that's the main box)
- CI/CD (no pipeline yet)
- Production anything (expendable lab equipment)

---

## Disk Layout — Current

Single partition, single OS:

```
C: NTFS 465 GB (295 GB free)
   ├── Windows 10 Home
   ├── VS2026 Community
   ├── .NET SDK 10.0.201
   ├── WSL2 (Ubuntu)
   └── Codex repo clone
```

## Disk Layout — Proposed

For Codex.OS development, we'll need to boot into a bare environment
eventually — either a custom kernel or a minimal Linux for hardware-level
testing. Options:

### Option A: WSL-Only (No Repartition)

Stay on Windows. Use WSL2 for all Linux work. Use QEMU for bare metal
testing. Flash the phone from Windows with Odin.

**Pros**: No risk. No repartition. Odin only runs on Windows. VS2026 is here.
**Cons**: Can't test Codex.OS as a real bootloader on this x86-64 hardware.
QEMU bare metal ≠ real bare metal.

### Option B: Dual Boot (Windows + Minimal Linux)

Shrink the Windows partition to ~250 GB. Create a 200 GB ext4 partition
with a minimal Linux (Arch or Void — just a kernel, shell, and GCC/LLVM
for cross-compilation). GRUB dual boot.

**Pros**: Real hardware testing. Can boot a Codex.OS kernel directly.
Native QEMU (not nested in WSL). Full Linux toolchain.
**Cons**: Repartition risk. Odin needs Windows (keep it). More complexity.

### Option C: USB Boot Linux (No Repartition)

Create a bootable USB with a persistent Linux install. Boot from USB when
doing bare metal work. Boot from SSD for Windows/Odin/VS work.

**Pros**: Zero risk to the SSD. Full Linux when needed. Can experiment
freely — worst case, reflash the USB.
**Cons**: USB speed (unless USB 3.0 + good drive). Need to change BIOS
boot order each time (or use boot menu).

### Recommendation

**Start with Option A. Move to Option C when we need real bare metal.**

Option A gives us everything we need for Peak III development and phone
flashing. WSL + QEMU covers native backend testing. When we're actually
writing the Codex.OS bootloader and need to test on real x86-64 hardware
(not emulated), we create a USB boot drive with minimal Linux. The SSD
stays untouched. Zero risk.

Option B is for later — when Codex.OS is mature enough to boot on its own
and we want a dedicated partition for it.

---

## Agent: Nut

The agent on this box is **Nut** — GitHub Copilot in VS2026 Community.

Named for the simplest piece of trad climbing protection: a metal wedge
slotted into a crack in the rock. No moving parts. Relies on geometry.
The oldest and most trusted gear on the rack. Also: kernel → nut.

| Agent | Platform | Role |
|-------|----------|------|
| Agent Windows | Main box, VS + Copilot | Builds features, reviews, pushes to master |
| Agent Linux | Claude on sandbox | Tests on real hardware/emulators, traces execution |
| Cam | Claude Code CLI (1M Opus) | Fast iteration, parallel work, GDB debugging |
| **Nut** | **Garage box, VS2026 + Copilot** | **Hardware lab: native testing, OS dev, phone flash** |

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
5. Flash phone with TWRP (see `docs/Projects/PHONE-WIPE.md`)
6. `adb push` an ARM64 Codex binary to the phone
7. Begin Peak III structured concurrency design
8. When ready: create USB boot Linux for bare metal x86-64 testing

---

*This box is expendable. The phone is expendable. The data is in git.
If we blow something up, we clone again and keep climbing.*
