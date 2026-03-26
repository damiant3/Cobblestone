# Current Plan

**Date**: 2026-03-25 (night update — Cam session 2)

---

## Where We Stand

Self-hosted compiler performance improved **9.2x** (1,907ms → 208ms) via sorted
binary search for all lookup structures and `list-snoc` for O(1) amortized list
accumulation. Three new builtins: `text-compare`, `list-insert-at`, `list-snoc`.

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~5,000 lines |
| Backends | 15 (12 transpilation + IL + RISC-V + WASM + ARM64 + x86-64, including bare metal) |
| Tests | 915 (507 compiler + 134 syntax + 110 repository + 86 toolkit + 23 semantics + 21 core + 18 LSP + 16 AST) |
| **Self-compile time** | **208ms median** (was 1,907ms — 9.2x faster) |
| Fixed point | **Proven** (Stage 2 = Stage 3 at 261,175 chars) |
| Language features | Lambda expressions, fork/await/par/race, Char type |
| New builtins | `text-compare` (ordinal), `list-insert-at` (O(n) sorted insert), `list-snoc` (O(1) amortized append) |
| Codex.OS | 7 KB kernel, Rings 0-4, preemptive multitasking, capability-enforced syscalls |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-24-peak3-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## What Got Done (2026-03-25 Cam session 2)

### Performance P2-alt: Sorted Binary Search + list-snoc (9.2x speedup)

Post-mortem: `docs/reviews/P2-HAMT-REVERT.md` (previous session).

The HAMT approach failed (82% slower). This session implemented the post-mortem's
recommended alternative: sorted lists with binary search.

| Component | Change | Lookup improvement |
|-----------|--------|-------------------|
| TypeEnv | Sorted `List TypeBinding`, binary search via `text-compare` | O(n) → O(log n) |
| Scope | Sorted `List Text`, binary search via `text-compare` | O(n) → O(log n) |
| Unifier | Sorted `List SubstEntry`, binary search on `var-id` | O(n) → O(log n) |
| TypeChecker `tdm` | Sorted type def map, binary search | O(n) → O(log n) |
| Lexer tokenize-loop | `acc ++ [tok]` → `list-snoc acc tok` | O(n²) → O(n) |
| All accumulator loops | `acc ++ [x]` → `list-snoc acc x` across 30+ sites | O(n²) → O(n) |

**New builtins** (reference + self-hosted):
- `text-compare : Text -> Text -> Integer` — ordinal string comparison via `string.CompareOrdinal`
- `list-insert-at : List a -> Integer -> a -> List a` — O(n) insert at index via `List<T>.Insert`
- `list-snoc : List a -> a -> List a` — O(1) amortized in-place `List<T>.Add` (safe for linear accumulators)

**Benchmark results** (median, 10 runs, 3 warmup):

| Stage | Before | After | Speedup |
|-------|--------|-------|---------|
| lex | 1,738ms | 24ms | **72x** |
| parse | 33ms | 19ms | 1.7x |
| desugar | — | 1ms | — |
| resolve | — | 8ms | — |
| typecheck | 49ms | 78ms | 0.6x |
| lower | — | 28ms | — |
| emit | — | 45ms | — |
| **total** | **1,907ms** | **208ms** | **9.2x** |

Branch: `cam/revert-p2` (includes P2 HAMT revert + this optimization).

### Previous session work (on this branch)

- P2 HAMT revert (was 82% slower)
- P4 string.Concat flattening (kept, pure win)
- Lexer Char fixes (char-to-text wrapping)
- Unifier CharTy support

---

## Codex.OS Status

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32-to-64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done (**optimized: 7KB**) |
| 2 | Process table (16 slots), preemptive context switch, per-process page tables | Done |
| 3 | Capability-enforced syscalls (SYS_WRITE_SERIAL, SYS_READ_KEY, SYS_GET_TICKS, SYS_EXIT) | Done |
| 4 | Self-hosting compiler on bare metal, TCO, serial I/O | Serial REPL works for short programs |

**Ring 4 status**: Compiler performance now 208ms (was 1,907ms). P1 (Char type) fixed
per-character allocation. P2 (sorted binary search + list-snoc) gave 9.2x speedup.
P4 (string.Concat flattening) on master. Remaining gap to reference: ~2-3x.
See `docs/Designs/PerformanceReportAndRecommendation.md`.

---

## What Remains

### Near-term (days)

| Item | Blocked on | Who |
|------|-----------|-----|
| **Review cam/char-type-remaining** | Awaiting review (3 commits: transpilers, native, self-hosted) | Any agent |
| **Review cam/perf-p2-p4** | Awaiting review (P4: string.Concat flattening) | Any agent |
| ~~Performance P2~~ | **Done** — sorted binary search + list-snoc (9.2x speedup) | ~~Cam~~ |
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
