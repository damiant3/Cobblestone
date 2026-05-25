# Current Plan

**Date**: 2026-03-26 late night

---

## MM2 IS PROVEN

The self-hosted Codex compiler — running on bare metal x86-64 under QEMU,
268 KB kernel, no OS, no runtime, just the UART — received `main : Integer`
/ `main = 42` over serial, compiled it through the full pipeline (tokenize,
parse, desugar, resolve, typecheck, lower, emit), and emitted valid C# back
over serial. Complete with CCE runtime preamble, using directives, and entry
point call.

**Source code in over serial. Valid compiled output out over serial. On bare
metal. MM2: The High Camp is reached.**

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~5,000 lines |
| Backends | 15 (12 transpilation + IL + RISC-V + WASM + ARM64 + x86-64, including bare metal) |
| Tests | ~900+ (519 compiler + 139 syntax + 110 repository + 70 core + 23 semantics + 18 LSP + 16 AST + MM2 integration) |
| Self-compile time | 312ms median (perf checked: +0.8% from 310ms baseline) |
| Fixed point | **Proven** (Stage 2 = Stage 3 at 298,752 chars, CCE-native) |
| Language features | Lambda, fork/await/par/race, Char, CCE-native text, linear closures, linear function types, CCE Tier 0-3 (full Unicode) |
| Codex.OS | 268 KB kernel, Rings 0-4, arena REPL, preemptive multitasking, capability-enforced syscalls, **compiles programs on bare metal** |
| CCE encoding | Full Unicode coverage — Tier 0 (1B, 128 chars), Tier 1 (2B, 500+ chars, 7 scripts), Tier 2 (3B, all BMP), Tier 3 (4B, emoji/supplementary) |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-26-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## What Got Done (2026-03-26 night)

### MM2 Proven (Agent Linux)
- QEMU bare metal test: `.codex` source → serial → compile → C# output → serial
- Fixed 2 test errors found during integration (list-contains registration, test fixes)
- MM2IntegrationTests.cs added to test suite

### CCE Full Unicode (Cam)
- Tier 1 multi-byte: Latin Extended (128), Cyrillic (77), Greek (49+),
  Arabic (44), Devanagari (53), CJK (~150), Japanese (176), Korean (96)
- Tier 2/3 pass-through: any Unicode character roundtrips — 3 bytes for BMP,
  4 bytes for supplementary (emoji). No data loss. Ever.
- 70 core encoding tests

### Closure Escape Analysis Complete (Cam)
- Step 4: `linear` function types for higher-order callbacks
- `TryResolveExprType` resolves through curried application chains
- 25 linearity tests, all 4 steps shipped

### x86-64 Fixes (Cam)
- `__ipow` runtime helper (was stubbed as 0) — exponentiation by squaring
- `list-contains` registered across full pipeline (bug found by Linux)
- 6 MM2 builtins merged (text-compare, list-snoc, list-insert-at,
  list-contains, text-concat-list, text-split)
- Zero TODOs remaining in compiler pipeline

### Escape Diagnostics (Cam, merged by Linux)
- CDX0005/CDX0006: `\t` and `\r` are compile-time errors
- Boundary normalization: TAB→spaces, CR→strip

### Perf Tracking (Cam)
- Baseline: 310ms. Current: 312ms (+0.8%). Within 10% threshold.
- `--bench-check` mode operational

### Design (Cam)
- Capability refinement: direction, scope, time-boxing
- Unified trust lattice: capabilities = positions in the repository trust lattice
- Design doc: `docs/Designs/CAPABILITY-REFINEMENT.md`

---

## Codex.OS Status

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32-to-64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done |
| 2 | Process table (16 slots), preemptive context switch, per-process page tables | Done |
| 3 | Capability-enforced syscalls | Done |
| 4 | Self-hosting compiler on bare metal, arena REPL, CCE-native | **MM2 PROVEN** — compiles `.codex` programs on bare metal |

---

## What's Next

### The path to MM3: Summit

MM3 is the self-hosted compiler compiling *itself* on bare metal — the
ultimate fixed point. The compiler that compiled the compiler, on hardware
it built the OS for.

### Near-term (days)

| Item | Notes |
|------|-------|
| MM2 celebration & documentation | Write up the achievement, update THE-ASCENT |
| Perf automation | Wire `--bench-check` into CI or pre-commit hook |
| Capability refinement Step 1 | Direction markers in effect syntax (design doc ready) |

### Medium-term (weeks)

| Item | Notes |
|------|-------|
| MM3 gap analysis | What's missing to self-compile on bare metal? |
| Codex.UI substrate | Semantic primitives, typed themes |
| Capability refinement Steps 2-8 | Scope, time-boxing, unified trust lattice |
| Multi-language syntax | Parser per locale, shared AST |

### Long-term

| Item | Notes |
|------|-------|
| Self-hosted compiler compiling itself on bare metal (MM3) | The summit |
| Codex.OS on real hardware | WHPX (Nut's box), then actual boot device |
| Ring 5+: filesystem, networking | Content-addressed FactStore as filesystem |
| Floppy disk image | Boot → compiler → self-compile, all in 1.44 MB |
| Repository federation | Trust lattice, cross-repo sync, capability-gated imports |

---

## Process

- **Reference compiler lock lifted** (2026-03-24): `src/` freely modifiable.
- **Session init**: `codex-agent orient` (Cam), `bash tools/linux-session-init.sh` (Linux).
- **Handoff**: Always update this file. `codex-agent handoff` for agent-to-agent.
- **Feature branches**: All work goes to feature branches for review. Direct master pushes for docs only.
- **Four-agent workflow**: Git is the coordination protocol.
  - Windows (Copilot/VS): builds features, reviews code
  - Linux (Claude/sandbox): tests on real hardware/emulators, finds bugs by tracing, reviews
  - Cam (Claude Code CLI, 1M Opus): fast iteration, parallel work
  - Nut (Copilot/VS2026, garage box): hardware lab, OS dev, phone flash
