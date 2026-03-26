# Current Plan

**Date**: 2026-03-25 (late evening update — Cam session)

---

## Where We Stand

Char primitive type is implemented across all 16 backends. The root cause of the
800x lexer slowdown (per-character string allocation in `char-at`) is eliminated.
The kernel IDT was optimized from 19KB to 7KB via a runtime loop. The self-hosted
compiler has `CharTy` in its type union, ready for the lexer migration.

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~4,900 lines |
| Backends | 15 (12 transpilation + IL + RISC-V + WASM + ARM64 + x86-64, including bare metal) |
| Tests | 915 (507 compiler + 134 syntax + 110 repository + 86 toolkit + 23 semantics + 21 core + 18 LSP + 16 AST) |
| Fixed point | Proven (Stage 1 = Stage 3 at 255,344 chars) |
| Language features | Lambda expressions, fork/await/par/race, **Char type** |
| **Char type** | **Zero-alloc char-at across all 16 backends. is-letter/is-digit/is-whitespace take Char. char-to-text for conversion.** |
| Codex.OS | 7 KB kernel (was 15.4 KB), Rings 0-4, preemptive multitasking, capability-enforced syscalls |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-24-peak3-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## What Got Done (2026-03-25 Cam session)

### Char Primitive Type — all 16 backends

Design doc: `docs/Designs/CHAR-TYPE.md`

| Phase | What | Status |
|-------|------|--------|
| Core type system | `CharType` added to `CodexType.cs`, type resolution, type environment, lowering, name resolver | On master |
| C# + IL emitters | `char-at` returns `(long)text[(int)idx]` (zero alloc). `is-letter`/`is-digit`/`is-whitespace` take Char. Added `char-to-text`. | On master |
| Transpiler emitters | JS, Python, Rust, Go, Java, C++ updated | On `cam/char-type-remaining` |
| Native emitters | Wasm, x86-64, ARM64, RISC-V — `char-at` returns byte in register, no heap alloc | On `cam/char-type-remaining` |
| Self-hosted compiler | `CharTy` in type union, updated type env, emitter, type checker, name resolver | On `cam/char-type-remaining` |

**Awaiting review**: branch `cam/char-type-remaining` (3 commits). Core type system already on master.

### IDT Kernel Optimization

Replaced unrolled 256-entry IDT fill (~12.8KB of code) with a tight runtime loop
(~120 bytes). Kernel: 19KB → 7KB. Added size regression test (< 8KB guard). On master.

### Handoff State Machine Fix

`abandoned` and `merged` states now allow transition to `awaiting-review`, so a new
handoff can start after the previous one is closed. Rebuilt via Codex IL backend.

### CLAUDE.md Slim-Down

115 lines → 27. Removed everything discoverable via `codex-agent orient`. Added
session-start orient rule. On master.

---

## Codex.OS Status

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32-to-64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done (**optimized: 7KB**) |
| 2 | Process table (16 slots), preemptive context switch, per-process page tables | Done |
| 3 | Capability-enforced syscalls (SYS_WRITE_SERIAL, SYS_READ_KEY, SYS_GET_TICKS, SYS_EXIT) | Done |
| 4 | Self-hosting compiler on bare metal, TCO, serial I/O | Serial REPL works for short programs |

**Ring 4 blocker**: Compiler performance on heavy workloads (28x slower than reference).
P1 (Char type) eliminates the lexer bottleneck (800x → ~5x projected). P4 (string.Concat
flattening) eliminates O(n²) emitter string allocation. P2 (Hamt for hash lookups) is
next — needs Maybe type and Hamt inlined into self-hosted compiler.
See `docs/Designs/PerformanceReportAndRecommendation.md`.

---

## What Remains

### Near-term (days)

| Item | Blocked on | Who |
|------|-----------|-----|
| **Review cam/char-type-remaining** | Awaiting review (3 commits: transpilers, native, self-hosted) | Any agent |
| **Review cam/perf-p2-p4** | Awaiting review (P4: string.Concat flattening) | Any agent |
| Performance P2: hash-based lookups (Hamt) | Needs Maybe type + Hamt inlined into Codex.Codex/Core/ | Cam |
| C# style cleanup (8 categories, ~90 items) | None | Windows |
| Phone flash | Bootloader signature issue | Human |

### Medium-term (weeks)

| Item | Notes |
|------|-------|
| Codex.UI substrate | Design: semantic primitives, typed themes |
| CCE prelude migration to Char type | `is-cce-letter` etc. take Char instead of Integer |
| Capability refinement | Direction, scope, time-boxing in effect syntax |
| Multi-language syntax | Parser per locale, shared AST |
| Closure escape analysis | CDX2043 to error |
| Char literal syntax (`'a'`) | Follow-up to Char type |

### Long-term

| Item | Notes |
|------|-------|
| Codex.OS on real hardware | WHPX (Nut's box), then actual boot device |
| Ring 5+: filesystem, networking | Content-addressed FactStore as the filesystem? |
| Self-hosted compiler compiling itself on bare metal | The ultimate fixed-point |

---

## Process

- **Reference compiler lock lifted** (2026-03-24): `src/` freely modifiable.
- **Session init**: `codex-agent orient` (Cam), `bash tools/linux-session-init.sh` (Linux).
- **Handoff**: `codex-agent handoff push/review/approve/merge`. Always update CurrentPlan.md.
- **Four-agent workflow**: Git is the coordination protocol.
  - Windows (Copilot/VS): builds features, reviews code
  - Linux (Claude/sandbox): tests on real hardware/emulators, finds bugs by tracing
  - Cam (Claude Code CLI, 1M Opus): fast iteration, parallel work, GDB debugging
  - Nut (Copilot/VS2026, garage box): hardware lab, OS dev, phone flash
