# Review: cam/floppy-disk-streaming — Floppy Disk Phase 1

**Reviewer**: Agent Linux  
**Date**: 2026-03-28  
**Branch**: `cam/floppy-disk-streaming` (4f78669..b749c6a, 4 commits)  
**Verdict**: ✅ Merge — no regressions, clean design

---

## Summary

Phase 1 of the Floppy Disk Edition: streaming emission pipeline. The
self-hosted compiler now processes definitions one at a time instead of
building a full IRModule and accumulating output in a single Text value.

Pipeline change: `compile` (monolithic) → `compile-streaming` →
`stream-defs` (per-definition: lower → emit → print → discard → next).

This eliminates the IRModule (~30–50 MB) and accumulated output text
(~2 MB) from peak memory, a prerequisite for the sub-4 MB bare metal
target in Phase 2.

## Changes Reviewed

| Commit | Description |
|--------|-------------|
| 4f78669 | Core feature: streaming pipeline, emitter fixes, generated outputs |
| 8cd47b8 | CurrentPlan update with Phase 1 status |
| 7572fe8 | Docs: x86-64 streaming verified, CRLF root cause identified |
| b749c6a | Bare metal streaming verified (267K bytes in 11.5s), test script |

### Key code changes

1. **`compile-streaming` + `stream-defs`** (main.codex): New entry point
   replacing monolithic `compile`. `stream-defs` is a TCO-eligible tail
   recursive loop — the x86-64 backend's per-iteration heap reset reclaims
   IR tree + emitted text each cycle.

2. **`build-arity-map-from-ast`** (CSharpEmitterExpressions.codex): Builds
   the arity map directly from `ADef` nodes (`.name.value`) instead of
   requiring lowered IR defs. Necessary because the IRModule no longer
   exists as a single structure.

3. **`print-line` → IIFE** (both Codex and C# emitters): Changed from
   `Console.WriteLine(...)` to
   `((Func<object>)(() => { Console.WriteLine(...); return null; }))()`.
   Makes `print-line` an expression returning `object`, required for
   `do`-block expression contexts (ternary chains, conditional returns).

4. **Void-like effectful defs**: Changed from `<body>; return null;` to
   `return <body>;`. Works because `print-line` now returns `null` through
   the IIFE wrapper. Fixes CS0201 on conditional expressions.

5. **`_all-source.codex`** (5190 lines, ~206 KB): Concatenated compiler
   source used as bare metal self-compile input. Stays on repo as a
   committed artifact — it's the canonical single-file representation of
   the self-hosted compiler.

6. **`floppy-disk-test.py`**: QEMU bare metal streaming test harness with
   progress reporting, stall detection, and output validation.

## Test Results

Full suite run on branch in Linux sandbox, 2026-03-28:

| Suite | Passed | Failed | Notes |
|-------|--------|--------|-------|
| Codex.Types.Tests | 531 | 7 | Same 7 as master (bare metal QEMU serial env) |
| Codex.Repository.Tests | 110 | 0 | |
| Codex.Syntax.Tests | 139 | 0 | |
| Codex.Ast.Tests | 16 | 0 | |
| Codex.Core.Tests | 70 | 0 | |
| Codex.Semantics.Tests | 23 | 0 | |
| Codex.Lsp.Tests | 18 | 0 | |
| **Total** | **907** | **7** | Identical to master |

The 7 failures are x86-64 bare-metal tests returning empty serial output —
a sandbox environment issue (no KVM, serial timeout). RISC-V bare-metal
tests pass. These failures are identical on master and are not caused by
this branch.

## Observations & Follow-ups

### 1. print-line IIFE heap pressure (low concern)

The `Func<object>` IIFE allocates a delegate per `print-line` call. On
.NET this is negligible. On bare metal, it adds one allocation per print.
Since `stream-defs` is TCO with heap reset, per-iteration garbage is
reclaimed, so this is not a blocker. Worth revisiting if Phase 2 tightens
the memory budget further.

### 2. Stage1/stage3 output diffs are large but mechanical

~863 lines changed in each staged output file. Changes are:
- New streaming functions emitted (`compile_streaming`, `stream_defs`,
  `build_arity_map_from_ast`)
- Generic type parameter index shifts (e.g. `T759` → `T767`) from the
  additional definitions
- `print-line` IIFE pattern propagated to all call sites

No new `/* error: */` markers introduced. Existing ones (record
construction syntax, `->` type in expression context) shifted positions
only.

### 3. CRLF Windows→Linux issue (documented, not blocking)

`--target codex` on Windows emits CRLF. The x86-64 lexer expects LF.
Workaround: `tr -d '\r'` before use. Proper fix would be normalizing
line endings in the Codex emitter or in the lexer's whitespace handling.
Documented in CurrentPlan — not a blocker.

### 4. Bare metal 4 MB OOM confirms Phase 2 necessity

512 MB bare metal: works (267K output in 11.5s).
4 MB bare metal: OOM crash.
Source + tokens + AST + type env still dominate at ~40–60 MB. Phase 2's
two-pass design (signature extraction pass, then per-def reparse) targets
this.

### 5. _all-source.codex freshness

This file is the concatenated compiler source. If individual `.codex`
files change without regenerating this artifact, it goes stale. Consider
a script or Makefile target to regenerate it, even if it stays committed.

---

*Reviewed from Linux sandbox. Environment: .NET 8.0.419, QEMU 8.2.2,
Ubuntu 24.04. Full build + test clean.*
