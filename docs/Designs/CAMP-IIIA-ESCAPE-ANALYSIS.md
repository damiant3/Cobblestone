# Camp III-A Phase 2 — Escape Analysis for Regions

**Status**: Phase 2a + 2b implemented (RISC-V escape copy for Text, Record, List)
**Prerequisite for**: Re-enabling RISC-V region reclamation
**Date**: 2026-03-23

---

## Problem

Region-based allocation works by saving the heap pointer on region entry
and restoring it on exit, bulk-freeing everything allocated in that region.
But some values must survive the region — they "escape."

Currently:
- **WASM**: Handles text escape only (copies string to parent region before exit)
- **RISC-V**: Regions disabled entirely — all allocations are permanent (1MB bump)
- **IL**: No-op (CLR GC handles it)

The 1MB bump allocator works for compilation but won't scale to long-running
programs or constrained environments.

---

## What Escapes

A value escapes a region if it's reachable after the region closes:

1. **Return values** — the function's result survives the function's region
2. **Closure captures** — values captured by a lambda that outlives the region
3. **Store to outer scope** — writing to a mutable reference in a parent region

In Codex today, (1) is the common case. (2) exists (closures are implemented).
(3) doesn't exist (no mutable references in the language).

---

## Approach: Type-Driven Escape Analysis

Codex already has the type information to determine what escapes:

### Scalars never allocate
- `Integer`, `Boolean`, `Number` — live in registers/stack, not heap
- These always survive region exit for free

### The return type tells you what escapes
- If a function returns `Integer`, nothing heap-allocated escapes
- If a function returns `Text`, one string escapes — copy it to parent
- If a function returns `Token` (a record), the record + all its fields escape
- If a function returns `List Token`, the list spine + all elements + their fields escape

### The IR already knows the return type
`IRRegion(Body, Type)` — `Type` is the return type. The escape analysis
only needs to walk this type to determine what to copy.

---

## Escape Copy Strategy

For each type that can escape:

| Type | Escape action |
|------|--------------|
| Integer/Boolean/Number | No action (scalar) |
| Text | Copy: allocate in parent, memcpy len+data |
| Record | Deep copy: allocate in parent, copy each field recursively |
| Sum type | Deep copy: copy tag + active variant's fields |
| List | Deep copy: walk spine, copy each cons cell + element |
| Function (closure) | Deep copy: copy closure struct + captured values |

### Deep Copy Complexity

Records and sum types have finite depth — walk the type definition.
Lists are recursive — need a copy loop.
Closures capture a fixed set of values — walk the capture list.

The WASM text escape is already doing this for Text. The pattern generalizes.

---

## Implementation Plan

### Phase 2a: Annotate IRRegion with escape info

```csharp
public sealed record IRRegion(
    IRExpr Body,
    CodexType Type,
    bool NeedsEscapeCopy)  // true if Type contains heap-allocated data
    : IRExpr(Type);
```

Add a static helper `NeedsHeapEscape(CodexType)` that returns true for
Text, RecordType, SumType, ListType, FunctionType (closures).

### Phase 2b: Implement escape copy in RISC-V

```
enter_region:
    sd s1, offset(sp)      ; save heap pointer

    ... body ...

exit_region:
    mv t0, a0              ; save return value
    ld s1, offset(sp)      ; restore heap pointer (reclaim region)
    ; if return type needs escape copy:
    call __deep_copy        ; copy return value from old region to parent
    mv a0, t0              ; return copied value
```

The `__deep_copy` runtime helper would dispatch on a type tag or be
specialized per return type at compile time.

### Phase 2c: Generalize WASM escape beyond text

Extend `EmitRegion` in WASM to handle records, sum types, and lists
using the same deep-copy pattern already proven for text.

---

## Open Questions

1. **Compile-time vs runtime dispatch**: Should escape copy be specialized
   per function (emitter generates type-specific copy code) or generic
   (runtime helper walks type tags)? Specialized is faster but bloats code.

2. **Closure escapes**: When a closure captures a value that was allocated
   in the current region, the closure's environment must be copied too.
   This interacts with the trampoline-based closure implementation.

3. **Nested regions**: Phase 1 only has function-level regions. When we add
   sub-function regions (around `let` bindings), escape analysis must handle
   values escaping to the parent region within the same function, not just
   at function return boundaries.

4. **LinearityChecker integration**: Linear values are consumed exactly once.
   A linear value that's returned (escapes) is consumed by the caller.
   Can we use linearity to prove that non-escaping values are dead at
   region exit? This would be a formal safety guarantee.

---

## What's Already Built

- `IRRegion` node wraps every function body (Lowering.cs:68-71)
- WASM region stack with enter/exit and text escape promotion
- RISC-V region infrastructure (currently no-op, but save/restore S1 pattern exists)
- `LinearityChecker` tracks consume-once semantics
- Type definitions accessible via `m_typeDefMap` in lowering and `IRModule.TypeDefinitions` in emitters

## Dependencies

- None. Can be implemented independently of V4 or other work.
- Should be tested with the self-hosted compiler (the real workload).
