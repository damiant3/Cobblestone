# Current Plan

**Date**: 2026-03-24

---

## Where We Stand

Peaks I and II are behind us. The language self-hosts, compiles to five native
targets (RISC-V, ARM64, x86-64, WASM, IL), and runs its own compiler on bare
hardware. Region-based memory reclamation works via sub-expression regions.
470 tests. The C# bootstrap is locked.

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~4,900 lines |
| Backends | 12 transpilation + IL + RISC-V + RISC-V bare metal + WASM + ARM64 + x86-64 |
| Tests | 470 (40 RISC-V QEMU, 33 ARM64 QEMU, 25 x86-64 native, 31 WASM) |
| Fixed point | Proven (Stage 1 = Stage 3 at 255,344 chars) |
| Reference compiler | Locked |
| Memory | Sub-expression regions: scalar lets reclaim intermediates at let boundary |
| Concurrency | IR nodes + sequential handler (fork/await) — Phase 1+2 done |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-24-peak2-complete.md`
**Route map**: `docs/THE-ASCENT.md`

---

## Active Work

### Codex Phone — Phase 1 (Human)

A Codex program running on the Samsung S7 Edge (SM-G935T).

- ARM64 backend: QEMU-verified, 33/33 tests green
- Phone effects: 7 capabilities with compile-time enforcement
- TWRP recovery image packed and validated (Samsung boot header, DTB, SEANDROIDENFORCE)
- Awaiting human go/no-go for Odin flash
- After flash: TWRP recovery → `adb push` ARM64 binary → run on Android/Linux shell

**Design**: `docs/Projects/CODEX-PHONE.md` | **Flash procedure**: `docs/Projects/PHONE-WIPE.md`

---

## Next Climbs

### Camp III-C — Structured Concurrency

No threads. No locks. No data races. Codex concurrency is structured:
every concurrent operation has a parent scope. `[Concurrent]` is an effect.

**Phase 1–3 DONE** (2026-03-24, Cam):
- `IrFork` and `IrAwait` IR nodes added to self-hosted compiler
- `fork : (Nothing → a) → Task a` (thunk-based), `await : Task a → a` in builtin type env
- `par : (a → b) → List a → List b` and `race : List (Nothing → a) → a`
- Lambda syntax (`\x -> body`) in both reference and self-hosted compilers
- Lowering intercepts `fork`/`await` calls → specialized IR nodes
- Sequential C# handlers: `Task.FromResult`, `.Result`, `Select`, thunk invocation
- All tests green (134 syntax, 470 types, 86 repository)

**What remains (Phase 4):**
- Work-stealing scheduler in native backends (RISC-V, x86-64)
- Effect system tracks `[Concurrent]`
- Linear types guarantee no shared mutable state
- Transpilation targets (C# `Task.Run`, JS `Promise`, etc.)

**Design**: `docs/Designs/CAMP-IIIC-STRUCTURED-CONCURRENCY.md`

### V3 — Repository Federation

Multi-repo sync, cross-repo trust. The content-addressed fact store
extends across trust boundaries.

**Phase 1 DONE** (2026-03-24, Cam):
- `ImportFactIntoView` / `RemoveImportFromView` / `GetViewImports` on FactStore
- `CheckViewConsistency` resolves imported facts alongside local definitions
- Imports stored in `.imports.json` sidecar files (backward compatible)

**Phase 2 DONE** (2026-03-24, Cam):
- Trust lattice: `ComputeTrust` with transitive vouch graph walk (max depth 5, cycle detection)
- Weights: Reviewed=0.25, Tested=0.5, Verified=0.75, Critical=1.0; transitive decay
- `CheckViewConsistencyWithTrust` gates imports on trust threshold
- 94 repository tests green (8 new trust tests)

**What remains (Phase 3-4):**
- Proposal workflow (replace pull requests)
- Federated sync protocol (networking)

**Design**: `docs/Designs/V3-REPOSITORY-FEDERATION.md`

### Camp III-A — Memory (Remaining)

Sub-expression regions handle scalar returns. Remaining:
- Heap-returning reclamation (copy-above-then-compact infrastructure exists in `#if false`)
- Closure escape (capture types unknown at region exit)
- LinearityChecker integration for formal soundness proof

---

## Process

- **Reference compiler is LOCKED.** See `docs/REFERENCE-COMPILER-LOCK.md`.
- **Principles**: `docs/10-PRINCIPLES.md` — unchanged, still governing.
- **Four-agent workflow**: Git is the coordination protocol. Any agent can push to master.
  - Windows (Copilot/VS): builds features, reviews code
  - Linux (Claude/sandbox): tests on real hardware/emulators, finds bugs by tracing
  - Cam (Claude Code CLI, 1M Opus): fast iteration, parallel work, GDB debugging
  - Nut (Copilot/VS2026, garage box): hardware lab, OS dev, phone flash
