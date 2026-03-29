# MM3 Memory Optimization Plan — Self-Compile in < 512 MB

**Date**: 2026-03-28 (updated 2026-03-28)
**Author**: Agent Windows
**Status**: Design — ready for implementation
**Assignee**: Cam (implementation), Agent Windows (review)
**Depends on**: Region reclamation fix (Cam, assumed done)
**Branch**: `cam/mm3-memory-opts`

---

## Goal

The self-hosted compiler (`_all-source.codex`, 4,949 lines, 193 KB) must compile
*itself* on native backends (x86-64 and RISC-V) within a **512 MB heap**. Current
estimate without optimization: **~16 GB**. Target: **< 64 MB** for bare-metal QEMU
with `-m 128`.

---

## Current State

- **Regions are pass-through** (`X86_64CodeGen.cs:1071–1079`, `RiscVCodeGen.cs`
  equivalent): `EmitRegion` calls `EmitExpr(region.Body)` with no reclamation.
  Working space grows monotonically.
- **`list-snoc` is capacity-aware** with 3 paths (O(1) in-place, O(1) heap-top
  grow, O(N) copy-with-doubling). Amortized O(1). **Done.**
- **`list-insert-at` always copies** — allocates a fresh `[capacity | count | ...]`,
  copies before, inserts, copies after. O(N) per call.
- **Result-space-aware escape-copy exists** (both backends) but is **disabled**
  because region reclamation was corrupting TCO loop pointers.
- **Cam indicates region reclamation is fixed** — assume the liveness/safety
  issue is resolved for both x86-64 and RISC-V.

---

## Memory Budget

Self-compile processes ~5,000 lines through 7 pipeline stages. Each stage builds
intermediate data structures:

| Stage | Dominant allocation | Estimated live data |
|-------|-------------------|-------------------|
| Tokenize | Token list (~15,000 tokens × 48B) | ~720 KB |
| Parse | CST nodes (Expr, Pat, TypeExpr trees) | ~2 MB |
| Desugar | AST nodes (mirror of CST) | ~2 MB |
| Resolve | Scope sorted-lists, diagnostics | ~500 KB |
| Type check | UnificationState (substitutions), TypeEnv (bindings) | ~3 MB |
| Lower | IR nodes (IRExpr trees, typed) | ~4 MB |
| Emit | Text accumulation (~300 KB output) | ~2 MB |

**Live data across all stages: ~15 MB.** Target ≤ 512 MB total heap means
≤ 34x overhead ratio. Without any reclamation the overhead is ~1000x (16 GB).
With the optimizations below we should hit ~5–10x.

---

## The Kill Chain — Seven Phases

### Phase 0: Source-Level Scalar Rewrites (Cam, in progress)

**The insight**: Many hot loops in the self-hosted `.codex` source allocate
heap records on every iteration when only the *final* record matters. Rewriting
these loops to work with scalar values (Integer — lives in a register, zero
heap allocation) and constructing the record once at the end eliminates the
allocations entirely. No runtime or backend changes needed.

**Primary target: Lexer scanning functions.**

Every character-scanning function creates a new `LexState` record per character
via `advance-char`:

```
advance-char (st) =
 if peek-code st == cc-newline
  then LexState { source = st.source, offset = st.offset + 1,
                  line = st.line + 1, column = 1 }
  else LexState { source = st.source, offset = st.offset + 1,
                  line = st.line, column = st.column + 1 }
```

For a 193 KB source, `scan-ident-rest`, `scan-digits`, `scan-string-body`,
`skip-spaces` call `advance-char` per character in TCO loops. That's ~193,000
`LexState` records (each 4 fields × 8 bytes = 32 bytes) of which only the
last one survives.

**The fix**: Rewrite scanning functions to compute the final offset as a scalar
Integer, then construct one `LexState` at the end:

```
scan-ident-rest-offset (source) (offset) (len) =
 if offset >= len then offset
 else let c = char-code-at source offset
  in if is-letter-code c then scan-ident-rest-offset source (offset + 1) len
  ...else offset

scan-ident-rest (st) =
 let final-offset = scan-ident-rest-offset st.source st.offset (text-length st.source)
 in LexState { source = st.source, offset = final-offset,
               line = st.line, column = st.column + (final-offset - st.offset) }
```

**Applicable scanning functions**:

| Function | Lines | Est. records eliminated |
|----------|-------|----------------------|
| `skip-spaces` | 272–279 | ~30,000 |
| `scan-ident-rest` | 282–300 | ~50,000 |
| `scan-digits` | 302–311 | ~5,000 |
| `scan-string-body` | 313–324 | ~10,000 |
| `skip-newlines` (parser) | 1169–1176 | ~5,000 |

**Total: ~100,000 dead `LexState` records eliminated = ~3.2 MB of heap
allocations that never happen.**

**Secondary targets (same pattern)**:

- **`process-escapes`** (line 326): builds text with `++` per character. Could
  accumulate into a list of char codes (scalars) and convert once at end.
- **`escape-text-loop`** (line 4733): same pattern — `list-snoc` per character
  building a `List Text` of single-char strings.
- **Parser `expect`/`skip-newlines` chains**: multiple `advance` calls creating
  intermediate `ParseState` records. Could batch-skip with offset arithmetic.

**Why this is safe**: Pure refactor of `.codex` source. No semantic change. No
runtime modification. Scalar TCO loop params live in registers — zero heap
pressure. The line/column tracking becomes approximate (computed from offset
delta instead of per-character), which is acceptable since the self-hosted
compiler doesn't emit source positions in its output.

**Impact**: ~3–6 MB direct savings. More importantly, reduces the *number of
heap allocations per TCO iteration* which compounds with Phase 2 (TCO heap
reset) — fewer objects to worry about surviving across iterations.

**Files**: `Codex.Codex/Syntax/Lexer.codex`, `Codex.Codex/Syntax/Parser.codex`,
and their concatenated form in `_all-source.codex`.

---

### Phase 1: Re-enable Region Reclamation

**Prereq**: Cam's liveness-safe region reclamation fix merged.

With the fix:
- Each `let` binding's working-space garbage is reclaimed at the region boundary.
- Result values escape to result space via escape-copy.
- Result-space-aware check (`ptr >= ResultBaseReg`) prevents re-copying.

**Impact**: Turns monotonic heap growth into bounded-per-function working space.
The 7-stage pipeline no longer accumulates all stages' garbage. Only live
inter-stage data persists.

**Estimated heap after Phase 1: ~2–4 GB** (still too much — hot loops within
stages still accumulate).

**Files**: `X86_64CodeGen.cs` (EmitRegion), `RiscVCodeGen.cs` (EmitRegion).

---

### Phase 2: TCO-Loop Heap Reset

**The problem**: TCO loops (`tokenize-loop`, `parse-binary-loop`, every `*-loop`
function) run inside a single function body. Region reclamation only fires at
`let` boundaries. A TCO loop that runs 15,000 iterations with `list-snoc`
allocates inside a *single region body* — the region never closes until the
function returns.

**The fix**: At the top of every TCO loop iteration, after evaluating tail-call
arguments into temp stack slots, reset HeapReg to the function-entry mark.

The correct ordering is critical:

```
TCO loop top:
  <evaluate all tail-call args into temp registers>   // may allocate on heap
  <store temps into TCO param stack slots>             // values are now on stack
  HeapReg = saved_mark                                 // reclaim everything on heap
  jump to loop top
```

**Why this is safe**: TCO param values are in *stack slots*, not heap. Heap
objects they *point to* are either:
1. Accumulator lists — `list-snoc` with capacity extends in-place (Path 1/2).
   The list pointer doesn't move. The list's backing memory is below the mark
   (allocated before the function entered TCO), or the list was the most recent
   allocation and sits at heap top.
2. Records from the current iteration's arg evaluation — these were just
   evaluated above and are referenced by stack-slot pointers.

**Problem case**: A TCO arg that is a *newly allocated record* gets reclaimed
by the reset because it's above the mark. Solution: evaluate args into
mini-regions with escape-copy:

```
for each TCO arg:
  mark = HeapReg
  result = evaluate(arg)
  escape-copy result to result space   // survives reset
  HeapReg = mark
  store escaped result in TCO temp
```

This reclaims per-arg temporaries while preserving the arg values themselves
in result space.

**Simpler alternative**: If the arg is a scalar (Integer, Boolean, Char), skip
escape-copy — it's in a register. Only heap-typed args need the mini-region.
Most TCO loops pass scalars (indices, counters) and one accumulator list. The
list is extended in-place by `list-snoc` and doesn't need escape-copy either
(its pointer doesn't change). Check: is the arg a `list-snoc` call? If so,
the returned pointer equals the input pointer — no escape needed.

**Impact**: Tokenize-loop drops from O(N) heap per iteration to O(1). Parse
loops, type-check loops, all `fold-list` loops — same.

**Estimated heap after Phase 2: ~200–500 MB.**

**Files**: `X86_64CodeGen.cs` (EmitTailCall), `RiscVCodeGen.cs` (EmitTailCall).

---

### Phase 3: In-Place `list-insert-at` for Sorted Environments

**The problem**: Type environment (`TypeEnv`), scope (`Scope`), and substitution
store (`UnificationState`) all use sorted lists with binary-search insertion via
`list-insert-at`. Every `env-bind`, `scope-add`, and `add-subst` copies the
entire list.

Hot paths in `_all-source.codex`:
- `env-bind` (line 3091): binary-search insert into `TypeEnv.bindings`
- `scope-add` (line 2103): binary-search insert into `Scope.names`
- `add-subst` (line 3213): binary-search insert into `UnificationState.substitutions`

The type checker calls `env-bind` ~500 times. The name resolver calls
`scope-add` ~200 times. Each copy is O(N).

**The fix**: Same capacity-aware layout as `list-snoc`, but for insertion:

1. If `count < capacity` AND the list is at heap top:
   - `memmove` elements right by 8 bytes from insertion point to count
   - Store new element at insertion point
   - Increment count
   - **Zero allocation. O(N) shift but no copy.**
2. If the list needs to grow:
   - Allocate with `max(count*2, 16)` capacity (geometric doubling)
   - Copy-with-gap
   - O(N) but only log₂(N) times total

The current `__list_insert_at` (both backends) *always* allocates a fresh list
with tight capacity (`newLen`). The fix adds a fast path that shifts in-place
when there's spare capacity.

**RISC-V** (`EmitListInsertAtHelper`, line 2707):
- Load capacity from `[list - 8]`
- Check `count < capacity`
- Check heap-top: `list + (capacity+1)*8 == S1`
- If both true: shift loop (backward from count to index), store, bump count
- Else: allocate-and-copy with doubled capacity (current code, but change
  tight capacity to doubled)

**x86-64** (`EmitListInsertAtHelper`, line 2798): same logic, different registers.

**Impact**: Type environment builds drop from O(N²) total allocation to O(N)
total (geometric growth). Substitution store same. ~100–300 MB saved.

**Estimated heap after Phase 3: ~80–200 MB.**

**Files**: `X86_64CodeGen.cs` (EmitListInsertAtHelper), `RiscVCodeGen.cs`
(EmitListInsertAtHelper).

---

### Phase 4: Capacity-Aware Text Concatenation

**The problem**: The C# emitter builds output via `++` (text concatenation). In
the self-hosted compiler, every `++` allocates a new text object. Lines like:

```
"public static " ++ cs-type ret ++ " " ++ sanitize d.name ++ gen ++ "(" ++ ...
```

Each `++` allocates a new string. For ~300 KB output, the emitter allocates
~50 MB of intermediate string fragments.

**The fix**: Make `++` on text use the same capacity-aware in-place growth as
lists. Store text as `[capacity @ -8 | length @ 0 | bytes @ 8...]`. If the
left operand is at heap top and has spare capacity, `memcpy` the right operand
into the spare space and bump length.

This makes `a ++ b` amortized O(|b|) instead of O(|a|+|b|) when `a` is at
heap top — the same trick as `list-snoc` Path 1/2.

**Alternative (no runtime change)**: Add `text-builder-new`,
`text-builder-append`, `text-builder-to-text` builtins. The self-hosted emitter
would need rewriting to use them. More work, same effect. Prefer the runtime
fix — it's invisible to the Codex language.

**Impact**: Emitter stage drops from ~50 MB to ~2 MB.

**Estimated heap after Phase 4: ~50–100 MB.**

**Files**: `X86_64CodeGen.cs` (EmitTextAppendHelper or `__text_append`),
`RiscVCodeGen.cs` (same). Both backends' text layout would need the capacity
word at `[-8]`, matching the list layout.

---

### Phase 5: Dead-Stage Reclamation (Ping-Pong Result Spaces)

**The problem**: After tokenization completes, the `LexState` intermediates are
dead, but the token list must survive for parsing. After parsing, the CST is
dead. After desugaring, the concrete `Document` is dead. Each pipeline stage's
*input* is dead after the stage completes — but it's in *result space*, which
is never reclaimed.

**The fix**: Ping-pong between two result spaces. After Stage N's result is
consumed by Stage N+1, swap which space is "result" and which is "old result."

Add a third register (`OldResultReg`). After each top-level pipeline stage:
```
temp = ResultBaseReg              // save current result base
ResultBaseReg = HeapReg           // new result space starts at current heap top
HeapReg = working_space_base      // reset working space
// Stage N+1 runs, its results go to new result space
// After Stage N+1, the old result space (temp) is dead
// Reset: old result space is reclaimable
```

**Complexity**: Higher than Phases 1–4. Requires an extra dedicated register
(tight on x86-64 — only 4 callee-saved locals remain). May need to use a
global memory location instead of a register.

**Defer**: Only implement if Phases 1–4 don't bring us under 512 MB. The
`compile` function in `_all-source.codex` has 7 nested `let`s — if region
reclamation works at those boundaries, each stage's working garbage is already
reclaimed. The *result-space* accumulation is only ~15 MB total (the live data
table above). It may not matter.

**Estimated heap after Phase 5 (if needed): ~30–50 MB.**

---

### Phase 6: Capacity Tuning and Constant-Factor Wins

Quick wins, no algorithmic changes:

1. **Minimum list capacity = 4** instead of 16. Most lists in the compiler are
   small (0–8 elements). Pattern match sub-patterns (`List Pat`) are 0–3
   elements. 16-slot minimum wastes 128 bytes per tiny list. Both backends:
   change the `Li(Reg, 16)` minimum in `__list_snoc` Path 2/3 and
   `__list_insert_at`.

2. **Token list pre-sizing**. The tokenizer knows `text-length source`. A
   reasonable estimate: `source_length / 4` tokens. Pre-allocate with that
   capacity to avoid *any* geometric growth copies during tokenization. This
   is a Codex-language-level change: add a `list-with-capacity` builtin that
   allocates `[capacity @ -8 | 0 @ 0 | <capacity slots>]`.

3. **Type environment sharing**. Currently each `env-bind` creates a new
   `TypeEnv { bindings = ... }` record wrapping the list (line 3094). The
   record is 8 bytes (pointer to bindings). With 500 binds, that's 500 dead
   `TypeEnv` wrappers on the heap. If `TypeEnv` were just the list directly
   (eliminate the wrapping record), those allocations vanish. This is a
   Codex-source-level refactor in `_all-source.codex` / the individual
   `.codex` files.

**Estimated impact**: ~10–30% reduction in constant-factor waste.

---

## Summary

| Phase | What | Heap reduction | Cumulative estimate |
|-------|------|---------------|-------------------|
| 0 | Source-level scalar rewrites (lexer, parser) | ~3–6 MB direct; reduces per-iteration alloc count | ~14–15 GB |
| 0 | Current (pass-through regions, capacity-aware snoc) | Baseline | ~16 GB |
| 1 | Re-enable region reclamation | 4–8x | ~2–4 GB |
| 2 | TCO-loop heap reset per arg | 5–10x | ~200–500 MB |
| 3 | In-place `list-insert-at` | 2–3x | ~80–200 MB |
| 4 | Capacity-aware text `++` | 2x | ~50–100 MB |
| 5 | Dead-stage reclamation (ping-pong) | 2x | ~30–50 MB |
| 6 | Capacity tuning | ~1.3x | ~25–40 MB |

**Phase 0 is safe to land immediately** — pure source refactor, no backend
risk, compounds with every subsequent phase. **Phases 1–3 are critical path.**
Phases 4–6 are polish to get comfortably under 64 MB
for bare-metal QEMU with `-m 128`.

**Implementation order**: Phase 0 (Cam, in progress — safe to land immediately) →
Phase 1 (prereq: Cam's region reclamation fix) → Phase 3 (independent,
pure runtime change) → Phase 2 (depends on understanding TCO arg liveness) →
Phase 4 → Phase 6 → Phase 5 (only if needed).

---

## Verification Plan

After each phase, run the scaling test suite:

```bash
# User-mode (fast iteration)
python3 tools/mm3-scaling-systematic.py

# Bare-metal (final verification)
python3 tools/mm3-scaling.py
```

Track peak heap usage via:
- x86-64 user mode: `strace -e brk` or `/proc/PID/status` VmPeak
- QEMU bare metal: serial print of HeapReg at each pipeline stage boundary
  (add a `__debug_heap` helper that emits HeapReg value over UART)

**Success criteria**: Full self-compile (`_all-source.codex`, 193 KB, 4,949
lines) produces valid C# output with peak heap < 512 MB (user mode) and
< 64 MB (bare metal with `-m 128`).

---

## Risk Register

| Risk | Mitigation |
|------|-----------|
| TCO heap reset reclaims a live record arg | Mini-region escape-copy per arg (Phase 2 detailed design) |
| In-place `list-insert-at` shift corrupts during self-referential insert | The Codex compiler never inserts into a list that references itself; sorted-list invariant guarantees single-owner |
| Text capacity word breaks existing text layout assumptions | Audit all `__text_*` helpers for offset assumptions; text length is at `[ptr+0]`, bytes at `[ptr+8]` — capacity at `[ptr-8]` is invisible to readers |
| Result-space ping-pong needs a third register (Phase 5) | Use a global memory location instead of a register; or defer Phase 5 entirely |
| Geometric doubling wastes ~50% capacity on average | Acceptable — 1.5x growth factor is an option if 2x wastes too much |

---

## Connection to the Bigger Picture

This plan takes the self-hosted compiler from "compiles small programs on bare
metal" (MM2) to "compiles *itself* on bare metal" (MM3). The memory
optimizations are not throwaway — they become the permanent allocator strategy
for Codex.OS Ring 4 and above. Every Codex program benefits from capacity-aware
lists, in-place insert, and region reclamation.

The five smooth stones: region reclamation, TCO heap reset, in-place insert,
capacity-aware text, stage ping-pong. David picks them up. Goliath falls.
