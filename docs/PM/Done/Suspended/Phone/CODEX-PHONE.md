# Codex Phone

**Date**: 2026-03-23
**Status**: ARM64 backend complete, awaiting QEMU verification
**Author**: Agent Windows (Copilot)
**Depends on**: ARM64 backend ✅, Capability System ✅, Effects ✅, Repository ✅

---

## The Problem

Modern smartphones are supercomputers that spy on their owners.

The microphone listens and an ad network hears. The camera feeds data to
analytics. Apps collect location history, contact graphs, and behavioral
fingerprints. The user carries a surveillance device that they paid for,
that they believe they own, but that works for someone else.

Can we fix this? Not all of it. But we can fix the foundation. If the
software on the phone is written in a language that **tracks effects**,
**enforces capabilities**, and **makes permission violations a compile-time
error**, then the phone can't spy on you — not because you trust the
developer, but because the compiler won't let the code compile if it
tries to access the microphone without an explicit, auditable grant.

Codex already has these building blocks. This document is the plan for
putting them on a real phone.

---

## The Target

### Samsung Galaxy S7 Edge (Confirmed)

| Spec | Value |
|------|-------|
| Model | SM-G935T (T-Mobile USA) |
| Serial | RF8H30790RF |
| SoC | Qualcomm Snapdragon 820 (MSM8996) |
| CPU | Kryo quad-core, AArch64 / ARM64 (ARMv8-A) |
| RAM | 4 GB |
| Storage | 32 GB |
| Display | 5.5" 2560×1440 Super AMOLED, capacitive touch |
| Sensors | Accelerometer, gyroscope, fingerprint, barometer, SpO2 |
| Camera | 12 MP rear, 5 MP front |
| Cellular | LTE Cat 9 (Qualcomm X12 modem) |
| Bootloader | T-Mobile — unlockable via OEM unlock |
| Custom ROM | TWRP + LineageOS available; postmarketOS experimental |
| Status | Expendable lab device — owner willing to wipe |

### Why This Phone

1. **It exists.** Wiped and ready. No procurement, no waiting.
2. **ARM64 is the dominant mobile ISA.** Everything we learn applies to
   every phone made in the last decade.
3. **It's expendable.** If we brick it, nobody cares.
4. **Unlockable bootloader.** T-Mobile allows OEM unlock on the G935T.
5. **4 GB RAM / quad-core Kryo** is more than enough. The self-hosted
   compiler is ~5K lines and compiles to a 227KB binary.
6. **Community support.** Snapdragon 820 has good Linux kernel support.

---

## What's Built

### ARM64 Backend ✅ (2026-03-23)

| File | Lines | Status |
|------|-------|--------|
| `Arm64Encoder.cs` | 367 | ✅ Complete — all instruction formats |
| `Arm64CodeGen.cs` | 1,740 | ✅ Complete — full IR→ARM64 codegen |
| `ElfWriterArm64.cs` | 113 | ✅ Complete — ELF64 AArch64 writer |
| `Arm64Emitter.cs` | 16 | ✅ Complete — IAssemblyEmitter wrapper |
| CLI integration | — | ✅ `codex build --target arm64` wired |

**Total: 2,236 lines.** Produces valid ELF64 AArch64 binaries.
Smoke-tested: `hello.codex` → 4,112-byte ELF (correct EM_AARCH64 header).

Covers: function prologue/epilogue, callee-saved x19-x27, register spill
to stack, records, sum types, pattern matching, closures, text builtins,
runtime helpers (itoa, atoi, str_eq, str_concat, list_cons, list_append,
read_file, read_line), Linux AArch64 syscalls (write=64, exit=93, brk=214,
openat=56, read=63, close=57).

**Awaiting**: `qemu-aarch64` verification by Agent Linux. Once confirmed,
binaries can run on the phone itself.

### Effect Tracking ✅

Five formalized effects: Console, FileSystem, State, Time, Random.
Each is a `.codex` prelude file. Adding phone effects is just more
prelude files (see Phone Effects section below).

### Compile-Time Capability Enforcement ✅

`CapabilityChecker` + `CDX4001` rejects code at compile time if it
uses effects not in the granted set. This already works today.

### Repository + Views ✅

Named views, consistency checking, composition, view-aware compilation.
An app is already a view in the repository model.

### Self-Hosted Native Compiler ✅ (Camp II-C)

The Codex compiler compiles itself to a 227,600-byte RISC-V ELF.
Under QEMU, it reads `.codex` source and produces valid output.
No .NET. No CLR. Native machine code, start to finish.

---

## The Plan

### Phase 1: Verify and Run (NOW)

**Goal**: A Codex program running on the S7 Edge.

| Step | What | Who | Status |
|------|------|-----|--------|
| 1a | Agent Linux pulls `windows/arm64-backend` | Linux | Awaiting |
| 1b | Run `qemu-aarch64` on hello/factorial | Linux | Awaiting |
| 1c | Fix any QEMU failures | Windows/Linux | — |
| 1d | Install Termux on S7 Edge | Human | — |
| 1e | `adb push` binary to phone, run natively | Human | — |

```bash
# Build on dev machine
codex build hello.codex --target arm64 --capabilities Console

# Push to phone (via adb over USB)
adb push hello /data/local/tmp/
adb shell chmod +x /data/local/tmp/hello
adb shell /data/local/tmp/hello

# Or inside Termux on the phone itself
./hello
```

**What this proves**: "I compiled a program with `--capabilities Console`.
Here it is running on my phone. It literally cannot access the network,
camera, or microphone. That's a compile-time guarantee, not a promise."

**Timeline**: Days.

### Phase 2: Replace Android (NEXT)

**Goal**: The phone boots Linux. Codex programs are the only user-space.

| Step | What | Notes |
|------|------|-------|
| 2a | Unlock bootloader (OEM unlock) | T-Mobile allows this |
| 2b | Flash TWRP recovery | Well-documented for SM-G935T |
| 2c | Install minimal Linux | postmarketOS or stripped LineageOS |
| 2d | Framebuffer rendering | Write pixels to `/dev/fb0` or DRM |
| 2e | Touch input | Read `/dev/input/eventN` |
| 2f | Simple UI toolkit | Widget → framebuffer draw calls |
| 2g | Phone app launcher | Capability-declared apps from repository |

**What this proves**: "My phone runs no proprietary application software.
Every app is compiled from auditable Codex source with declared
capabilities. Android is gone."

**Timeline**: Weeks after Phase 1.

### Phase 3: Codex OS (FUTURE — Peak IV)

**Goal**: Replace Linux itself. ARM64 bare metal. Boot, MMU, device
drivers, scheduler — all written in Codex.

Requires Camp III completion (linear allocator escape analysis,
capability I/O runtime enforcement, structured concurrency). This
is months out. But Phases 1 and 2 are independently useful — we
don't need Peak IV to put a verified-capability phone in your hand.

---

## Phone Effects (New Prelude Files)

These extend the existing five effects. Same machinery, more names:

```
effect Display where
  draw : Widget -> {Display} Unit

effect Microphone where
  listen : Duration -> {Microphone} Audio

effect Camera where
  capture : CaptureMode -> {Camera} Image

effect Location where
  locate : Accuracy -> {Location} Coordinates

effect Network where
  fetch : Url -> {Network} Response

effect Sensors where
  accelerometer : {Sensors} Vector3
  gyroscope : {Sensors} Vector3

effect Identity where
  authenticate : {Identity} Principal
```

### Identity Model

| Mode | Who | Capability Set |
|------|-----|---------------|
| **Owner** | Biometric-verified | All granted capabilities |
| **Guest** | Anyone holding it | `[Display]` only — "return this phone" screen |
| **Trusted** | Owner-vouched person | Per-person set defined by owner |
| **Lost** | No owner biometric for N hours | `[Display]` — return info, everything else locked |

The identity system runs in an `Identity` effect handler that **does not
have `Network`**. Biometric data stays on-device. Type system guarantee.

---

## The Tests

**Bar test**: You mention margaritas. No running app has `Microphone` in
its capability set. The conversation stays at the bar.

**Drop test**: 10 minutes with no owner biometric → Lost mode. Screen
shows return info. Phone is a brick for everything else.

**Flashlight test**: The app has `capabilities: [Display]`. The compiler
verified this. It turns pixels white. That's all it can do.

---

## Technical Notes

### Register Mapping

```
ARM64 Register    Codex Use
─────────────────────────────────
x0–x7             Arguments/returns
x8                Syscall number
x9–x15            Temps (caller-saved, rotated)
x16–x17           Intra-procedure scratch
x18               Platform register (reserved)
x19–x27           Locals (callee-saved, monotonic alloc)
x28               Heap pointer (callee-saved)
x29 (FP)          Frame pointer
x30 (LR)          Link register
SP                Stack pointer
```

### Linux AArch64 Syscalls

```
write  = 64   (x8=64,  x0=fd, x1=buf, x2=len → svc #0)
exit   = 93   (x8=93,  x0=code → svc #0)
brk    = 214  (x8=214, x0=addr → svc #0)
openat = 56   (x8=56,  x0=dirfd, x1=path, x2=flags → svc #0)
read   = 63   (x8=63,  x0=fd, x1=buf, x2=len → svc #0)
close  = 57   (x8=57,  x0=fd → svc #0)
```

### Framebuffer Access (Phase 2)

On postmarketOS with a downstream kernel, the display should be
accessible via either legacy framebuffer (`/dev/fb0`) or DRM
(`/dev/dri/card0`). The S7 Edge's display controller is the
Qualcomm MDP5. DRM is preferred — it supports page flipping and
vsync. A minimal DRM client needs:
- `drmOpen` → `drmModeGetResources` → `drmModeGetConnector`
- Allocate a dumb buffer → `drmModeSetCrtc`
- Write pixels directly to the mapped buffer

All of this can be done via `ioctl` syscalls. No GPU driver needed
for 2D rendering.

### Touch Input (Phase 2)

The capacitive touchscreen reports events on `/dev/input/eventN`.
Read `struct input_event` (16 bytes on 64-bit): timestamp, type,
code, value. Touch events are `EV_ABS` with `ABS_MT_POSITION_X`
and `ABS_MT_POSITION_Y`. This is pure `read()` syscalls — no
library needed.

---

## Continuity Plan

If the human gets mauled by dogs (or otherwise becomes unavailable):

### What Exists

1. **The compiler works.** Self-hosting proven. 390+ tests. Fixed point.
2. **The ARM64 backend is complete.** 2,236 lines in `Codex.Emit.Arm64`.
   `codex build --target arm64` produces ELF64 AArch64 binaries.
3. **The RISC-V backend is complete.** Self-hosted on native RISC-V.
4. **The capability system works.** `--capabilities` flag enforces at
   compile time. `CDX4001` fires on violations.
5. **The repository model works.** Views, consistency, composition.

### Where Things Are

| Artifact | Location |
|----------|----------|
| Source | `https://github.com/damiant3/NewRepository` |
| ARM64 branch | `windows/arm64-backend` (awaiting merge) |
| Solution | `Codex.sln` — 37 projects, .NET 8 |
| ARM64 backend | `src/Codex.Emit.Arm64/` |
| RISC-V backend | `src/Codex.Emit.RiscV/` |
| Self-hosted source | `Codex.Codex/` (26 `.codex` files) |
| Design docs | `docs/Designs/` |
| Handoff docs | `docs/OldStatus/` |
| Agent toolkit | `tools/codex-agent/` |
| Phone design | `docs/Designs/CODEX-PHONE.md` (this file) |

### How to Continue

1. **Merge `windows/arm64-backend` to master** after Linux verifies.
2. **Run `qemu-aarch64`** on any compiled binary to verify ARM64.
3. **Get the phone**: SM-G935T, serial RF8H30790RF. Unlock bootloader
   via Settings → Developer Options → OEM Unlock.
4. **Flash TWRP**: Download for `hero2qlte` (T-Mobile S7 Edge).
   Boot to download mode (Vol Down + Home + Power), flash via Odin.
5. **Install Termux** from F-Droid (or flash postmarketOS for Phase 2).
6. **Build and push**: `codex build hello.codex --target arm64`, then
   `adb push` to phone.

### Who's Who

- **Agent Windows** (GitHub Copilot in VS): Built features, ARM64 backend.
  Runs on the dev machine with the solution open.
- **Agent Linux** (Claude sandbox): Tests on real hardware/QEMU. Reviews
  branches. Found the AllocLocal saturation bug that led to Camp II-C.
- **Agent Cam** (Claude Code CLI, 1M context): Fast iteration. Fixed 11
  bugs in one session for Camp II-C summit. Works from a separate worktree.
- **The Human** (Damian): Routes between agents. Picks the line. Has the
  phone. If absent, the agents can continue — git is the shared state,
  `dotnet test` is the acceptance criterion.

### The Three Rules

1. **Build passes.** `dotnet build Codex.sln` — zero warnings.
2. **Tests pass.** `dotnet test Codex.sln` — all green (ignore CS5001
   in Codex.Codex and `Peek_non_numeric_start` — both pre-existing).
3. **Don't merge your own work.** Push to a branch. Another agent reviews.

---

## Why This Matters

A Codex program running on a real phone, with compile-time capability
guarantees, is worth a thousand design documents about hypothetical
platforms.

The ARM64 backend is built. The phone is identified. The plan is three
phases — the first one is days away. The only step between here and
"Codex program running in your hand" is one QEMU verification run.
