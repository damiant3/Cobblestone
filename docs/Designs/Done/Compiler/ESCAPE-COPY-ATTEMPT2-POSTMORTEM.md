# Escape Copy Attempt 2 — Post-Mortem

**Date:** 2026-04-01
**Agent:** Cam
**Result:** Abandoned. Reverted .codex changes from master.
**Commits:** `80331f9` (feat), `1e80192` (merge), `c7413a6` (revert)

## Background

The C# x86-64 codegen (`X86_64CodeGen.cs`) has a bail-out at `EmitRegion`
line ~1328: when target is bare metal, heap-returning regions skip two-space
escape copy entirely. The bail-out comment says "2 MB heap too small for
512 KB forwarding table" — but the heap grew to 128 MB long ago. The
bail-out is obsolete in principle, but removing it has never worked in
practice.

The session goal was to make the C# reference compiler's escape copy work
on bare metal so we could verify the Codex-side port (feature/phase6-escape-copy)
before it matters.

## What Was Tried

### Approach: Factored region-escape wrappers

Instead of inlining the full escape copy infrastructure (~180 bytes) at
every region site, factor it into shared callable functions:

- **Per-region inline code** (~36-50 bytes): save mark, emit body, call
  `__region_escape_<type>` wrapper, restore mark.
- **Per-type region wrappers** (~80 bytes each, emitted once per unique type):
  call `__fwd_table_zero`, capture HWM, switch R10=R15, result-space-base
  check, call `__escape_<type>`, update R15, ret.
- **`__fwd_table_zero` function** (~40 bytes, emitted once): same as the
  inline `EmitFwdTableZero` but as a callable function using `rep stosq`.
- **`IsFunctionBody` flag on `IRRegion`**: let/do regions pass through on
  bare metal (no escape overhead); only function-body regions trigger escape.

### Diagnostic measurements (self-compile targeting x86-64-bare)

| Configuration | ELF size | Wrappers | Helpers | Notes |
|---|---|---|---|---|
| Baseline (bail-out active) | 606 KB | 0 | 0 | No escape copy at all |
| All regions, no factoring (attempt #1 from prompt) | 1.2 MB | — | — | Per-region inline bloat |
| Function-boundary only, no factoring (attempt #2) | 807 KB | — | — | Still too much inline |
| Factored wrappers, all regions | 852 KB | 146 (11 KB) | 194 (89 KB) | Per-region inline ~150 KB from ~3000 regions |
| Factored wrappers, function-boundary only | 749 KB | 113 (9 KB) | 194 (89 KB) | Helpers dominate |
| Post-revert baseline | 570 KB | 0 | 0 | Phase 6 .codex helpers also reverted |

Each of the 194 escape helpers inlines `EmitFwdTableLookup` (~120 bytes)
and `EmitFwdTableInsert` (~100 bytes). Factoring those into shared functions
would save ~40 KB, bringing helpers from 89 KB to ~49 KB. Not attempted
because runtime issues made ELF size moot.

### Runtime testing

**Small programs work.** `greeting.codex` (text concat + record),
`person.codex` (record + text escape), and `mini-bootstrap.codex` all
produce correct output with escape copy enabled:

```
READY
Hello, Alice!
HEAP:524344
STACK:2097152
```

HEAP:524344 = ~512 KB = the forwarding table allocation captured by HWM.
The escape copy mechanism itself is correct.

**Self-compile fails.** Three compounding issues:

1. **Result space overflow.** Bare metal layout has 1 MB result space
   (125 MB to 126 MB). Escaped data accumulates across the entire
   compilation with no reclamation. Even enlarging to 58 MB (working
   space 64 MB, result space 58 MB), the compiler crashed ~3% through
   output (~8.9 KB of ~260 KB expected, 548 lines, 31 seconds).

2. **Working space exhaustion.** `compile-streaming-v2` in `main.codex`
   is one function with ~10 nested `let` bindings holding tokens (~1.4 MB),
   scan results, type-defs, headers, type environments, ctor-types,
   all-types, and ctor-names simultaneously. With function-boundary-only
   escape, ALL of this accumulates in working space without reclamation
   until the function returns. 64 MB working space was not enough.

3. **The pipeline IS the function.** Two-space escape copy assumes
   functions produce small results and discard large intermediates.
   The self-compiler's pipeline holds everything live simultaneously.
   Function-boundary escape copy fundamentally cannot help when the
   function IS the entire pipeline.

## Why We Reverted the .codex Changes

The merge `1e80192` brought Codex-side escape copy code into master:
`X86_64Helpers.codex` (420 new lines), changes to `X86_64.codex` (298 lines),
`CodexEmitter.codex`, and `IRModule.codex`.

This code is **unverifiable**: the C# reference compiler's escape copy
doesn't work for the self-compile, so there's no way to prove the Codex
port is correct. The code was never tested on bare metal. Keeping it in
master risks:

- Breaking the self-compile if the untested helpers have bugs
- Creating merge conflicts for unrelated work
- Giving false confidence that escape copy is closer to working than it is

The revert `c7413a6` removes the .codex changes from master's working tree
while preserving the full history (the merge commit + original feature commit
are still in the log). The work is recoverable via
`git diff 1e80192^..1e80192`.

## What Would Need to Change

The fundamental issue is that the self-compiler's pipeline is structured as
one big function that holds all intermediate state live. Possible paths
forward:

**Application-level restructuring.** Break `compile-streaming-v2` into
smaller functions that return intermediate results, allowing escape copy
to reclaim at each boundary. This is a Codex source change, not a codegen
change. Biggest bang for the buck.

**Single reclamation point.** Instead of per-function regions, add one
explicit reclamation call in the compiler's main processing loop (between
definitions in `emit-defs-streaming`). The compiler already processes
definitions one at a time — each iteration could reclaim.

**In-place compaction.** Instead of two-space (working → result), compact
within working space by sliding live data down. Avoids the result-space
sizing problem entirely. Harder to implement — needs careful overlap
handling (memmove semantics).

**Accept the bail-out.** Escape copy isn't needed until memory pressure
is actually a problem. The self-compile currently fits in 128 MB. If it
grows, we deal with it then. The bail-out is ugly but functional.
