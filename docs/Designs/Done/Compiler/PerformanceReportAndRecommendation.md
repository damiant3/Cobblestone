# Performance Report & Recommendations

**Date**: 2026-03-19 (verified via system clock)
**Agent**: Copilot (VS 2022, Windows)
**Branch**: `master`

---

## Executive Summary

The self-hosted Codex compiler is **28× slower** than the reference C# compiler on
the large workload (compiling itself: 163K chars, 458 definitions). Nearly **85%** of
that gap is in a single stage: the **lexer**, which takes 1,504ms vs the reference's
1.89ms — an **800× difference**. The remaining stages range from 4–39× slower, with
clear algorithmic causes in every case.

The good news: every bottleneck is a known pattern with a known fix. The compiler
could realistically be brought within **2–5× of the reference** with targeted work
on data structures, without changing any language semantics.

---

## Benchmark Results

### Environment

- .NET 8, Release build, Windows
- 3 warmup iterations, 10 measured iterations, median reported
- Same source input to both compilers

### End-to-End (median ms)

| Workload | Chars | REF ms | SELF ms | Ratio | Verdict |
|----------|-------|--------|---------|-------|---------|
| small (expr-calculator) | 4,779 | 1.01 | 2.50 | 2.5× | CLOSE |
| medium (prelude combined) | 40,039 | 9.18 | 84.30 | 9.2× | SLOWER |
| large (self-hosted compiler) | 163,290 | 62.52 | 1,775.47 | **28.4×** | VERY SLOW |

### Per-Stage Breakdown (large workload)

| Stage | REF ms | SELF ms | Ratio | % of SELF total |
|-------|--------|---------|-------|-----------------|
| **lex** | 1.89 | **1,503.65** | **796×** | **84.7%** |
| parse | 1.03 | 12.78 | 12.4× | 0.7% |
| desugar | 0.87 | 5.48 | 6.3× | 0.3% |
| resolve | 1.77 | 13.91 | 7.9× | 0.8% |
| typecheck | 11.74 | 52.57 | 4.5× | 3.0% |
| lower | 2.27 | 24.28 | 10.7× | 1.4% |
| emit | 4.37 | 172.02 | 39.4× | 9.7% |

### Memory (allocations per iteration, large workload)

| Compiler | Alloc/iter |
|----------|-----------|
| REF | ~35 MB |
| SELF | ~6,739 MB |

The self-hosted compiler allocates **192× more memory** than the reference compiler.

---

## Root Cause Analysis

### 1. LEXER — 800× slower (84.7% of total time)

**The #1 problem.** The self-hosted lexer is character-by-character with
`char-at source offset` on every step. In the generated C# this becomes
`source.Substring(offset, 1)` — a **heap allocation per character** for a 163K-char
input.

The reference compiler's `Lexer` uses `source[offset]` (a single char index, zero
allocation) and `ReadOnlySpan<char>` for string slicing.

**Root cause**: The Codex language has no `Char` type — only `Text`. So `char-at`
returns a `Text` (string), and every character comparison allocates a new string.

**Impact on memory**: 163K chars × multiple passes × string allocations = billions
of bytes of garbage, explaining the 192× allocation ratio.

### 2. EMITTER — 39× slower (9.7% of total time)

The C# emitter builds output via string concatenation (`++`). In the generated C#
this becomes `string.Concat(a, b)` chains. For 458 definitions producing ~248K chars
of output, this creates enormous intermediate string garbage.

The reference compiler uses `StringBuilder` internally through its emit methods.

**Root cause**: No `StringBuilder` or buffer type in self-hosted code. Pure functional
string concat is O(n²) for building large outputs.

### 3. PARSE — 12× slower

The parser uses `list-at tokens pos` for every token access. In the generated C# this
is `tokens[pos]` which is fine for `List<T>`, but the `ParseState` record is recreated
on every `advance` call: `ParseState { tokens = st.tokens, pos = st.pos + 1 }`. This
allocates a new record for every token consumed.

The reference compiler's parser uses a mutable `m_position` field — zero allocation
per token advance.

### 4. TYPE CHECKER — 4.5× slower (the closest!)

The type checker is actually the most competitive stage. Its 4.5× gap comes from:

- **TypeEnv**: Uses `List TypeBinding` with O(n) linear scan for lookups. The reference
  uses `Map<string, CodexType>` (hash-based, O(1) amortized).
- **UnificationState**: Uses `List SubstEntry` with O(n) linear scan for substitution
  lookups. The reference uses `Map<int, CodexType>`.
- **Record recreation**: Every `add-subst`, `env-bind`, etc. creates a new record with
  the full list copied.

4.5× is impressively close given these structural disadvantages — the algorithmic
logic itself is well-written.

### 5. NAME RESOLVER — 7.9× slower

**Scope** is `{ names : List Text }` with `scope-has-loop` doing O(n) linear scan.
The reference uses `Set<string>` (hash-based, O(1) lookup).

With 458 top-level names + builtins + constructors, every name lookup during resolution
scans hundreds of entries.

### 6. LOWERING — 10.7× slower

The lowering pass has the same `List`-as-map pattern. Every type lookup, constructor
lookup, and arity table access is O(n). The reference uses `Map<K,V>` throughout.

### 7. DESUGAR — 6.3× slower

The desugarer is mostly straight traversal, so its gap comes primarily from record
allocation overhead (every `map-list` creates a new list via `acc ++ [item]` which
is O(n) per append).

---

## Algorithmic Hot Spots

### Pattern: `acc ++ [item]` (Quadratic List Building)

This pattern appears **everywhere**:

```
map-list-loop f xs (i + 1) len (acc ++ [f (list-at xs i)])
```

Each `acc ++ [item]` copies the entire accumulator. For n items this is O(n²).
In the generated C#: `Enumerable.Concat(acc, new List<T> { item }).ToList()` —
allocates a new list every iteration.

**Occurrences**: `map-list`, `fold-list`, `collect-top-level-names`, `resolve-list-elems`,
`resolve-match-arms`, `collect-ctor-names`, `parse-imports`, every accumulator loop
in the parser, lowering, and emitter.

### Pattern: Linear Scan Where Hash Lookup Is Needed

| Data structure | Operations | Self-hosted | Reference |
|---------------|------------|------------|-----------|
| TypeEnv | lookup, bind | O(n) list scan | O(1) Map |
| Scope | has, add | O(n) list scan | O(1) Set |
| UnificationState.substitutions | lookup, add | O(n) list scan | O(1) Map |
| Arity tables | lookup | O(n) list scan | O(1) Map |

### Pattern: Per-Character String Allocation in Lexer

```
char-at source offset → source.Substring(offset, 1)
```

This allocates a new `string` for every character examined. The lexer examines
each character at least once, plus keyword lookups and whitespace skipping.

---

## Recommendations

### Priority 1: Fix the Lexer (would eliminate 85% of slowdown)

**Option A — Add a `Char` type to the language.**

Add a `Char` primitive type that maps to `char` in C# emission. Change `char-at`
to return `Char` instead of `Text`. Character comparisons become value comparisons
(zero allocation). This is a language change but a small one — affects only the
lexer and character-processing code.

**Estimated impact**: 800× → ~5× (lexer would go from 1,504ms to ~10ms).

**Option B — Emit `source[offset]` for `char-at` as a builtin optimization.**

Recognize `char-at` in the emitter and emit `source[offset]` instead of
`source.Substring(offset, 1)`. Requires the emitter to understand that single-char
operations can use `char` under the hood while presenting `Text` to the type system.

**Estimated impact**: Similar to Option A, less language-level change.

### Priority 2: Replace List Accumulators with Efficient Builders

The `acc ++ [item]` pattern needs to become a proper builder pattern.
Options:

1. **Use the Hamt prelude module** — it already exists. Migrate `TypeEnv`, `Scope`,
   and `UnificationState` to use `Hamt` for O(log n) lookups.
2. **Add a `MutableBuilder` builtin** — a mutable list builder that the emitter
   translates to `List<T>.Add()` instead of `Concat + ToList`.
3. **Compiler optimization** — detect `acc ++ [x]` in the lowering pass and emit
   `List<T>.Add` instead of `Concat`.

**Estimated impact**: Would bring parse, resolve, typecheck, lower, and emit
stages to within 2–4× of reference.

### Priority 3: Mutable Parser State

Change `ParseState` from a record recreated on every advance to a design where
`pos` is mutated in place. Options:

1. **Emit optimization** — recognize the pattern `{ ...record, pos = pos + 1 }`
   and emit an in-place mutation.
2. **Ref-cell pattern** — add a `Ref a` type that wraps a mutable reference.

### Priority 4: StringBuilder for Emitter

The emitter's string concat pattern needs a buffer:

1. Use the existing `StringBuilder` prelude module.
2. Or add a `text-builder` builtin that the emitter recognizes.

---

## Projected Impact

| Fix | Current total | After fix | Projected ratio |
|-----|--------------|-----------|-----------------|
| Baseline | 1,775ms | — | 28× |
| P1: Fix lexer | — | ~270ms | ~4.3× |
| P2: Hash-based lookups | — | ~150ms | ~2.4× |
| P3: Mutable parser state | — | ~140ms | ~2.2× |
| P4: StringBuilder emitter | — | ~100ms | ~1.6× |
| **All fixes** | — | **~100ms** | **~1.6×** |

Getting to **1.6× of the reference compiler** is realistic with these four changes.
The remaining gap would be inherent overhead from the functional compilation style
(immutable records, curried functions, etc.) which is acceptable.

---

## What "Fast Enough" Looks Like

| Metric | Current | Target | Reference |
|--------|---------|--------|-----------|
| Compile self (163K chars) | 1,775ms | <150ms | 63ms |
| Compile sample (5K chars) | 2.5ms | <2ms | 1ms |
| Memory per compile | 6.7 GB | <100 MB | 35 MB |

The target of **<150ms to compile itself** means a developer could rebuild the
entire compiler on every save and get sub-second feedback. That's fast enough.

---

## Recommended Sequence

```
P1 (lexer char type)  ← 85% of the problem, do this first
    │
    └──→ P2 (hash lookups: Hamt for TypeEnv, Scope, SubstMap)
              │
              └──→ P4 (StringBuilder for emitter)
                        │
                        └──→ P3 (mutable parser state)
```

P1 alone would take the compiler from "unusably slow" (1.8s) to "tolerable" (270ms).
P1+P2 would bring it to "good" (150ms). P1+P2+P4 would bring it to "competitive" (~100ms).

---

## Appendix: Raw Numbers

### Reference Compiler (C#)

```
End-to-end:
  small      median=1.01ms   min=0.87ms   max=2.02ms   alloc≈641KB/iter
  medium     median=9.18ms   min=8.78ms   max=13.10ms  alloc≈6,299KB/iter
  large      median=62.52ms  min=40.34ms  max=88.02ms  alloc≈35,188KB/iter

Per-stage (large):
  lex          1.89ms
  parse        1.03ms
  desugar      0.87ms
  resolve      1.77ms
  typecheck   11.74ms
  lower        2.27ms
  emit         4.37ms
```

### Self-Hosted Compiler (Codex → C#)

```
End-to-end:
  small      median=2.50ms      min=2.06ms      max=3.72ms      alloc≈10,159KB/iter
  medium     median=84.30ms     min=73.16ms     max=98.10ms     alloc≈638,609KB/iter
  large      median=1,775.47ms  min=1,746.02ms  max=1,810.32ms  alloc≈6,739,062KB/iter

Per-stage (large):
  lex        1,503.65ms
  parse         12.78ms
  desugar        5.48ms
  resolve       13.91ms
  typecheck     52.57ms
  lower         24.28ms
  emit         172.02ms
```
