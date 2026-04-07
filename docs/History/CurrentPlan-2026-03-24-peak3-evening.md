# Current Plan

**Date**: 2026-03-24 (evening update)

---

## Where We Stand

Peak III is taking shape. In a single session today, Cam delivered structured
concurrency (fork/await/par/race across 8 backends), lambda syntax, repository
federation (imports + trust + proposals), linearity improvements, and native
backend fork/await with GDB-verified x86-64 execution.

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~4,900 lines |
| Backends | 12 transpilation + IL + RISC-V + RISC-V bare metal + WASM + ARM64 + x86-64 |
| Tests | ~475 compiler + 134 syntax + 103 repository = **712+** |
| Fixed point | Proven (Stage 1 = Stage 3 at 255,344 chars) |
| Language features | Lambda expressions (`\x -> body`), fork/await/par/race |
| Concurrency | Real parallelism (C#/JS/Python/Go), sequential (native), `[Concurrent]` effect |
| Repository | V1 (views) + V2 (narration) + V3 Phases 1-3 (imports, trust, proposals) |
| Memory | Sub-expression regions + CDX2043 closure capture warning |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-24-peak2-complete.md`
**Route map**: `docs/THE-ASCENT.md`

---

## Active Work

### Codex Phone — Phase 1 (Human)

Bootloader signature verification blocking all custom recovery images.
9 flash attempts documented. Official TWRP downloaded. USB state diagnosis
in progress. See `docs/Active/Projects/PHONE-WIPE.md` for full attempt log.

**Design**: `docs/Active/Projects/CODEX-PHONE.md`

---

## What Got Done Today (2026-03-24, Cam session)

### Camp III-C — Structured Concurrency: DONE through Phase 4

| Phase | What | Status |
|-------|------|--------|
| 1 | IrFork/IrAwait IR nodes in self-hosted compiler | Done |
| 2 | Sequential handler (Task.FromResult) | Done → upgraded to real parallelism |
| 3 | Lambda syntax + par/race + thunk-based fork | Done |
| 4a | Real parallelism: C# Task.Run/WhenAll/WhenAny | Done |
| 4b | JS Promise, Python ThreadPool, Go goroutines | Done |
| 4c | `[Concurrent]` effect enforcement in type system | Done |
| 4d | x86-64 native fork/await (sequential, GDB-verified) | Done |
| 4e | ARM64 + RISC-V native fork/await (compilation verified) | Done, Agent Linux testing execution |

### V3 — Repository Federation: Phases 1-3 DONE

| Phase | What | Status |
|-------|------|--------|
| 1 | Cross-repo imports by content hash | Done (10 tests) |
| 2 | Trust lattice with transitive vouching | Done (8 tests) |
| 3 | Proposal workflow (view diffs, consensus, apply) | Done (9 tests) |
| 4 | Network sync protocol | Not started |

### Camp III-A — Memory

- CDX2043 closure capture warning: Done (2 tests)
- Heap-returning reclamation: Not started (code exists in `#if false`)
- Thread-safe heap allocator: Not started (blocks native parallel execution)

### Language

- Lambda expressions (`\x -> body`): Done in both compilers (3 parser tests)
- `using System.Threading.Tasks` cleanup: Done

---

## What Remains

### Near-term (days)

| Item | Blocked on | Who |
|------|-----------|-----|
| Native fork/await QEMU execution (ARM64, RISC-V) | Agent Linux testing now | Linux |
| Phone flash | Bootloader signature issue | Human |
| V3 Phase 4: network sync | Design decision on protocol | Any |

### Medium-term (weeks)

| Item | Blocked on | Notes |
|------|-----------|-------|
| Thread-safe heap allocator | Design | Gates native parallel fork. Per-thread arenas or lock-free bump allocator. |
| Heap-returning reclamation | Escape analysis | `#if false` code in RISC-V backend. Copy-above-then-compact needs live-ref tracking. |
| Closure escape analysis | CDX2043 → error | Promote warning to error, track closure linearity. |
| `[Concurrent]` in native backends | Thread-safe heap | Sequential fork works now; real threads need safe allocation. |

### Long-term (Peak IV prerequisites)

| Item | Notes |
|------|-------|
| Codex.OS kernel image | Nut's box. QEMU first (Rung 0), WHPX next (Rung 1). |
| Bootloader / UEFI | x86-64 backend produces ELF; need Multiboot2 or UEFI PE. |
| Device drivers | Keyboard, timer, serial. Starts in QEMU. |
| Self-hosted native compiler on bare metal | Peak II-C done for RISC-V; need x86-64 self-hosting. |

---

## Process

- **Reference compiler lock lifted** (2026-03-24) — `src/` freely modifiable.
- **Principles**: `docs/10-PRINCIPLES.md` — unchanged, still governing.
- **Four-agent workflow**: Git is the coordination protocol. Any agent can push to master.
  - Windows (Copilot/VS): builds features, reviews code
  - Linux (Claude/sandbox): tests on real hardware/emulators, finds bugs by tracing
  - Cam (Claude Code CLI, 1M Opus): fast iteration, parallel work, GDB debugging
  - Nut (Copilot/VS2026, garage box): hardware lab, OS dev, phone flash
