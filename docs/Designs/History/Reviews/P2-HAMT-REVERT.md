# P2 HAMT Revert — Post-Mortem

**Date**: 2026-03-25
**Agent**: Cam (Claude Code CLI)
**Branch**: `cam/revert-p2`

---

## What Happened

The performance report (`docs/Designs/PerformanceReportAndRecommendation.md`)
identified O(n) linear scans in TypeEnv, Scope, and UnificationState as the P2
bottleneck. The recommendation was to replace `List TypeBinding` with hash-based
lookup structures.

I implemented a HAMT (Hash-Array Mapped Trie) — a functional persistent hash map —
monomorphized as `TypeMap` for `CodexType` values. Migrated TypeEnv, Scope,
UnificationState, LowerCtx, and all TypeChecker type resolution to use it.

## The Result

| Metric | Before P2 | With HAMT | After revert |
|--------|-----------|-----------|--------------|
| Stage 1 (self-compile) | ~1,775ms (baseline) | 3,233ms | 2,323ms |
| Stage 2 (fixed-point) | — | 4,572ms | 3,669ms |
| Output size | 255,344 chars | 268,236 chars | 258,626 chars |

**The HAMT made the compiler 82% slower, not faster.**

## Why It Failed

### 1. Functional list operations inside the trie are O(n)

The HAMT uses `List<TypeMapNode>` for its branch children. In Codex, all list
operations are O(n):

- `list-tail xs` → rebuilds the list from index 1 (O(n))
- `list-insert-at xs i x` → copies i elements, inserts, copies rest (O(n))
- `list-replace-at xs i x` → copies i elements, replaces, copies rest (O(n))
- `[x] ++ xs` → creates new list (O(n) in generated C#: `.Concat().ToList()`)

Each HAMT insert touches 1-3 levels of the trie. At each level, it replaces a child
in the branch's list. That's 1-3 × O(k) list operations where k is the number of
children at that level (up to 32). For 500+ insertions during type checking, this
adds up to more work than the O(n) linear scan it replaced.

### 2. The HAMT overhead exceeds the lookup savings

The old linear scan: `env-lookup` walks a list from head to tail, O(n) per lookup.
For 500 names, average scan length is ~250.

The HAMT lookup: O(log32 n) = ~2 hash computations + bitmap checks + list indexing.
For n=500, that's ~2 levels. Each level does: `djb2-hash` (O(key_length) string
scan), `extract-chunk` (division), `bitmap-has` (division + modulo), `bitmap-index`
(popcount via loop). Total: ~20 operations per lookup.

The lookup IS faster (20 ops vs 250 comparisons). But the INSERT is much slower
(list-copy-and-replace vs cons-to-head). Since the self-hosted compiler does roughly
equal inserts and lookups, the insert overhead dominates.

### 3. Additional bugs found

The original HAMT prelude (`prelude/Hamt.codex`) has a bitmap overflow bug:
`bitmap-has` uses `pow32(bit)` which computes 32^bit (power of 32) instead of
2^bit (power of 2). For bit values > 12, `32^bit` overflows `long`, producing 0
and causing `DivideByZeroException`. This bug exists in the prelude but is masked
because the prelude tests don't exercise enough keys to trigger high chunk values.

Fixed with `tm-pow2` during the investigation, but reverted with the rest of P2.

### 4. Polymorphic records not supported by reference parser

The HAMT prelude uses `HamtMap a = record { root : HamtNode a, size : Integer }` —
a polymorphic record. The reference C# parser doesn't support type parameters on
record definitions. Required monomorphizing to `TypeMap` with `CodexType` values
only, which added complexity and reduced generality.

## The Right Fix

The performance report's diagnosis was correct: O(n) lookups are a bottleneck. But
the prescription — a persistent hash map — was wrong for a language without:

1. **Mutable arrays** (HAMT children need O(1) array update)
2. **Bitwise operations** (bitmap math via division is 10x slower than shifts)
3. **O(1) cons-cells** (Codex lists are `.Concat().ToList()` in C#, not linked lists)

Better approaches for future work:

- **Sorted list with binary search**: O(log n) lookup, O(n) insert, but insert
  is a single list rebuild (not trie-level copying). Net win for the lookup-heavy
  type checker.
- **Emitter-level optimization**: Detect `List<T>` accumulator patterns in the
  lowering pass and emit `List<T>.Add()` instead of `.Concat().ToList()`. This
  would make ALL list operations O(1) amortized.
- **Add Array<T> to the language**: A mutable/copy-on-write array type would
  enable efficient HAMT, sorted containers, and general-purpose data structures.

## What Was Kept

- **P4 (string.Concat flattening)**: The reference C# emitter change that flattens
  `a ++ b ++ c` into `string.Concat(a, b, c)` is still on master. This is a pure
  win with no downside.
- **Lexer Char fixes**: `char-to-text` wrapping in `process-escapes` and
  `scan-multi-char-operator`. These are correctness fixes for the Char type.
- **Unifier CharTy**: `type-tag` and `types-equal` handle CharTy.

## Lessons

1. **Profile before optimizing.** I should have benchmarked a single HAMT insert
   + lookup cycle against the list pattern before committing to the full migration.
2. **Know your runtime.** Functional data structures assume O(1) cons and O(1)
   pointer updates. When the runtime uses eager `.ToList()`, those assumptions
   break.
3. **The two-failures rule applies to optimization too.** The first failure was the
   `pow32`/`pow2` bug. The second was the performance regression. Should have
   stopped after the first and reconsidered the approach.
