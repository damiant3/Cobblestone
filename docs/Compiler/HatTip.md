# Hat Tip: Mutable Linear State

**Date:** 2026-04-01
**Status:** Plan — not yet implemented
**Goal:** Eliminate copy-on-update heap waste for linear state-threaded records,
reducing Stage 1 Heap HWM from 126 MB toward the ~62 MB observed in Stage 2.

## The Problem

The Codex self-compiler threads state through pipelines using immutable records.
Every state transition allocates a NEW record on the heap, copies unchanged fields,
and writes the changed field(s). The old record is immediately dead but never
reclaimed (bare-metal escape copy is disabled for heap-returning regions).

For a record with N fields updated K times, this wastes `K * N * 8` bytes.
CodegenState has 13 fields and is updated thousands of times during emission.
UnificationState has 3 fields and is updated hundreds of times during type
registration. ParseState and LexState are updated on every token.

The old records form a chain of dead garbage on the heap that is never collected.
This is the dominant contributor to the 126 MB Stage 1 Heap HWM.

## Why It's Safe

All identified instances follow the same pattern:
1. State is passed as a parameter (single reference)
2. A new record is constructed with 1-2 fields changed
3. The old record is never referenced again
4. The new record is returned or passed to the next call

This is **linear state** — exactly one live reference at any time. In-place
mutation is safe because no other code holds a pointer to the old record.

## Inventory (23 instances across 5 record types)

### UnificationState (3 fields: substitutions, next-id, errors)

| Function | File:Line | Changed | Impact |
|---|---|---|---|
| advance-id | Unifier.codex:44 | next-id | Called per fresh variable (~1000s) |
| add-subst | Unifier.codex:91 | substitutions | Called per unification (~100s) |
| add-unify-error | Unifier.codex:101 | errors | Rare but still wasteful |

### TypeEnv (1 field: bindings)

| Function | File:Line | Changed | Impact |
|---|---|---|---|
| env-bind | TypeEnv.codex:48 | bindings | Called per def header + per builtin (~230+) |

### ParseState (2 fields: tokens, pos)

| Function | File:Line | Changed | Impact |
|---|---|---|---|
| advance | ParserCore.codex:24 | pos | Called per token consumed (~20K) |
| skip-newlines | ParserCore.codex:293 | pos | Called frequently during parsing |

### LexState (4 fields: source, offset, line, column)

| Function | File:Line | Changed | Impact |
|---|---|---|---|
| advance-char | Lexer.codex:150-161 | offset, line, column | Called per character (~390K) |
| skip-spaces | Lexer.codex:180-185 | offset, column | Called per whitespace run |

### CodegenState (13 fields)

| Function | File:Line | Changed | Impact |
|---|---|---|---|
| st-append-text | X86_64.codex:166 | text | Called per instruction (~10Ks) |
| st-with-text | X86_64.codex:185 | text | Called for text replacement |
| record-func-offset | X86_64.codex:204 | func-offsets | Called per function |
| reset-func-state | X86_64.codex:234 | 7 fields | Called per function |
| alloc-temp | X86_64.codex:264 | next-temp | Called per temp allocation |
| alloc-local | X86_64.codex:288 | next-local, spill-count | Called per local allocation |
| load-local | X86_64.codex:342 | text, load-local-toggle | Called per local load |
| st-set-tail-pos | X86_64.codex:505 | tco (nested) | Called per TCO position change |
| add-local | X86_64.codex:614 | locals | Called per local binding |
| st-add-rodata-fixup | X86_64.codex:635 | rodata-fixups | Called per rodata reference |
| st-add-escape-name | X86_64.codex:686 | escape-names | Called per escape helper |
| st-enqueue-escape | X86_64.codex:705 | escape-queue | Called per escape enqueue |
| emit-load-func-addr | X86_64.codex:1416 | func-addr-fixups | Called per function reference |

## Waste Estimate

**CodegenState** (13 fields = 104 bytes per dead record):
- st-append-text alone is called ~10,000+ times during self-compile emission
- 10,000 * 104 = ~1 MB just from st-append-text dead records
- All CodegenState functions combined: ~5-10 MB dead records

**ParseState** (2 fields = 16 bytes per dead record):
- advance called ~20,000 times
- 20,000 * 16 = ~320 KB

**LexState** (4 fields = 32 bytes per dead record):
- advance-char called ~390,000 times (one per source character)
- 390,000 * 32 = ~12.5 MB

**UnificationState** (3 fields = 24 bytes per dead record):
- advance-id + add-subst called ~1000+ times
- 1,000 * 24 = ~24 KB (small, but compounds with nested type operations)

**Total estimated waste: ~18-25 MB of dead records from copy-on-update alone.**

The remaining gap to 126 MB is from intermediate heap data (type structures,
IR trees, emitted text strings) that is live during peak computation.

## Fix Approach: In-Place Field Write

### Option A: Compiler optimization (best, hardest)

Detect linear record usage in the IR/lowering phase. When a record is:
1. Bound to a name that has exactly one use
2. That use is a record construction with the same fields
3. Some fields reference the old record's fields directly

Then replace the "allocate new + copy all + write changed" pattern with
"write changed field(s) in place, return same pointer."

This is a linearity analysis — the compiler must prove the old record has
no other live references. In TCO loops this is straightforward (the old
param is dead once the new value is computed). In let chains it requires
tracking reference counts.

### Option B: Language-level `with` syntax (clean, medium)

Add a record update syntax:

```
st with { next-id = st.next-id + 1 }
```

The compiler emits an in-place field write when it can prove linearity,
or a full copy when it can't. This gives the programmer explicit control
over "I want to update this record" vs "I want to construct a new one."

### Option C: Builtin `record-set!` (pragmatic, easiest)

Add a primitive that mutates a record field in place:

```
record-set! st "next-id" (st.next-id + 1)
```

Or field-index based for the bare-metal backend:

```
__record_set_field(ptr, field_index, new_value) → same ptr
```

The caller is responsible for ensuring linearity. This is the "hat tip"
to procedural programming — explicit mutation where safety is guaranteed
by the programmer, not the type system.

### Chosen: Option A (general compiler optimization)

Option A eliminates all 23 instances automatically with zero .codex changes.
The optimization lives entirely in the C# codegen (IR analysis + emission).
No new syntax, no builtins, no per-site manual conversion.

**Why not Option C:** Requires touching 23 call sites in .codex, adding a
new builtin, and trusting the programmer to enforce linearity. Option A
is the same effort in C# but solves the problem generally — any future
copy-on-update pattern is optimized automatically.

**Why escape copy was different:** Escape copy ADDED overhead at every site
(forwarding table zeroing, deep copy traversal, result-space bookkeeping).
This optimization REMOVES overhead — instead of alloc + N field copies, it
emits 1-2 field writes. The generated code is strictly smaller and faster.
The only cost is a ref-counting pass at compile time in the C# codegen,
which is invisible in the 60s self-compile.

## Implementation

### The linearity check

For each `IRLet` where the value is a record construction of type T:

1. Walk the record construction expression, collect all references to
   bindings from enclosing scopes.
2. For each referenced binding B of the same record type T:
   a. Count all references to B in the ENTIRE let body (not just the
      construction).
   b. Count all references to B inside the record construction.
   c. If counts are equal AND every reference is a field access → B is
      linear with respect to this construction.
3. If linear: emit field writes to B's pointer for changed fields only.
   Return B's pointer. Skip allocation entirely.
4. If not linear: emit normal record construction (allocate + copy all).

### What "changed" means

Compare each field expression in the new record with `B.field_i`:
- If the field expression is exactly `B.field_i` → unchanged, skip write
- If the field expression is anything else → changed, emit write

### Field read/write ordering

The codegen already evaluates all field expressions into temps before writing.
With in-place mutation, the reads happen from the OLD record (same pointer),
then the changed fields are written. No ordering hazard.

### Where to implement

| Component | File | Change |
|---|---|---|
| Ref counting | X86_64CodeGen.cs | New method: `CountBindingRefs(IRExpr, string) → int` |
| Linear check | X86_64CodeGen.cs | In `EmitLet` or `EmitRegion`: detect same-type record construction with linear source |
| In-place emit | X86_64CodeGen.cs | New path in record construction: skip alloc, write changed fields only |

### Verification

1. `dotnet build Codex.sln` + `dotnet test Codex.sln`
2. `wsl bash tools/pingpong.sh` — fixed point must hold
3. Compare Heap HWM in performance summary — expected reduction 18-25 MB
4. ELF size should decrease (fewer alloc + copy instructions per site)

## Risks

1. **Ref counting bug**: If the count is wrong (misses a reference), an
   aliased record gets mutated, corrupting the other reference. Pingpong
   catches this immediately — a corrupted record means wrong output on
   the first self-compile.
2. **Conservative misses**: Match branches and if-then-else inflate the
   ref count. The optimization won't fire for those cases. Not a bug,
   just a missed opportunity. Can be refined later with branch-aware
   counting.
3. **Field read/write ordering**: Already handled by the existing codegen
   pattern (evaluate all expressions, then write). No new risk.
