# Current Plan

**Date**: 2026-03-25 (post-session update)

---

## Where We Stand

Peak IV has begun. In a single session (2026-03-24 evening to 2026-03-25), Cam built
Codex.OS from zero to Ring 4 (self-hosting compiler on bare metal x86-64), while
Agent Linux verified every commit under QEMU and found 4 bugs that Cam fixed in
real time. The evening prior, Cam delivered structured concurrency, repository
federation, and lambda syntax.

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~4,900 lines |
| Backends | 12 transpilation + IL + RISC-V + RISC-V bare metal + WASM + ARM64 + x86-64 + **x86-64 bare metal** |
| Tests | ~489 compiler + 134 syntax + 110 repository = **733+** |
| Fixed point | Proven (Stage 1 = Stage 3 at 255,344 chars) |
| Language features | Lambda expressions (`\x -> body`), fork/await/par/race |
| Concurrency | Real parallelism (C#/JS/Python/Go), per-thread arenas (x86-64 native), sequential (ARM64/RISC-V) |
| Repository | V1 (views) + V2 (narration) + V3 Phases 1-4 (imports, trust, proposals, **network sync**) |
| Memory | Sub-expression regions + CDX2043 closure capture warning + **heap-returning reclamation (RISC-V)** |
| **Codex.OS** | **15.4 KB kernel, Rings 0-4, preemptive multitasking, memory isolation, capability-enforced syscalls, TCO** |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-24-peak3-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## Codex.OS Status

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32-to-64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done |
| 2 | Process table (16 slots), preemptive context switch, per-process page tables | Done |
| 3 | Capability-enforced syscalls (SYS_WRITE_SERIAL, SYS_READ_KEY, SYS_GET_TICKS, SYS_EXIT) | Done |
| 4 | Self-hosting compiler on bare metal (269KB), TCO, all serial I/O paths | In progress |

**Verified under QEMU**: `42`, `3628800` (factorial 10), `100` (TCO count-down), `42ABABAB...` (two preemptive processes).

**Next for Ring 4**: Pipe `.codex` source through serial, get compiled output back.

---

## What Got Done (2026-03-25 session, Agent Linux + Cam)

### Native Fork/Await: 3 bugs found and fixed

| Bug | Backend | Fix |
|-----|---------|-----|
| ARM64 Str/Ldr offset encoding (1 not 8) | ARM64 | Byte offset, not slot index |
| Closure register not set before trampoline call | ARM64/RISC-V | Set X11/T2 before Blr/Jalr |
| TryEmitBuiltin result in temp reg, caller expects A0/X0 | ARM64/RISC-V | Mv to A0/X0 before return |

All 3 backends verified under QEMU: x86-64 (native), ARM64 (qemu-aarch64), RISC-V (qemu-riscv64).

### Branches Merged to Master

| Branch | What |
|--------|------|
| cam/thread-safe-heap | Per-thread heap arenas via clone + mmap (x86-64) |
| cam/fix-native-fork-offsets | 3 fork/await bugs fixed |
| cam/heap-reclamation | Copy-above-then-compact re-enabled (RISC-V) |
| cam/v3-network-sync | V3 Federation Phase 4: HTTP sync protocol (7 tests) |
| linux/verify-fork-fix | 9 Linux-native QEMU execution tests |
| cam/codex-os-rung0 | Bare metal boot, ELF32 LOAD segment fix |
| cam/ring1-interrupts | IDT, PIC, timer + keyboard handlers |
| cam/ring2-process-isolation | Process table, context switch, preemptive multitasking, page isolation |
| cam/ring3-capabilities | Capability-enforced syscalls |
| cam/ring4-self-hosting | Self-hosting compiler on bare metal, TCO, serial I/O fix |

### Additional Bug Found During OS Work

ELF32 LOAD segment p_offset=0 caused all trampoline addresses (GDT, far jump target)
to be off by 0x80 (textStart). Fix: LOAD segment starts at textStart, maps to LoadAddress.

---

## What Remains

### Near-term (days)

| Item | Blocked on | Who |
|------|-----------|-----|
| Ring 4 completion: serial REPL | Compiler hang under QEMU (heavy computation) | Cam/Linux |
| Kernel size optimization | Optional: runtime IDT loop saves ~12KB (15KB to 3KB) | Any |
| Phone flash | Bootloader signature issue | Human |

### Medium-term (weeks)

| Item | Blocked on | Notes |
|------|-----------|-------|
| Codex.UI substrate | Design: semantic primitives, typed themes | Human + Any |
| Capability refinement | Design: direction, scope, time-boxing in effect syntax | Human + Any |
| Multi-language syntax | Parser per locale, shared AST | Any |
| Closure escape analysis | CDX2043 to error | Promote warning to error, track closure linearity |

### Long-term

| Item | Notes |
|------|-------|
| Codex.OS on real hardware | WHPX (Nut's box), then actual boot device |
| Ring 5+: filesystem, networking | Content-addressed FactStore as the filesystem? |
| Self-hosted compiler compiling itself on bare metal | The ultimate fixed-point |

---

## Process

- **Reference compiler lock lifted** (2026-03-24): `src/` freely modifiable.
- **Principles**: `docs/10-PRINCIPLES.md`: unchanged, still governing.
- **Session init**: `bash tools/linux-session-init.sh`: installs .NET 8, QEMU, cross-compilers, clones, builds, tests, shows plan and unmerged branches.
- **Four-agent workflow**: Git is the coordination protocol. Any agent can push to master.
  - Windows (Copilot/VS): builds features, reviews code
  - Linux (Claude/sandbox): tests on real hardware/emulators, finds bugs by tracing
  - Cam (Claude Code CLI, 1M Opus): fast iteration, parallel work, GDB debugging
  - Nut (Copilot/VS2026, garage box): hardware lab, OS dev, phone flash
