# Camp III-A Phase 2 — Escape Analysis for Regions

**Status**: Phase 2a–2c implemented. Region reclamation enabled on RISC-V, x86-64, ARM64. WASM extended.
**Date**: 2026-03-24

---

## Problem

Region-based allocation works by saving the heap pointer on region entry
and restoring it on exit, bulk-freeing everything allocated in that region.
But some values must survive the region — they "escape."

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

The return type alone determines what escapes. No flow-sensitive analysis needed.

### Scalars never allocate
- `Integer`, `Boolean`, `Number` — live in registers/stack, not heap
- These always survive region exit for free

### The return type tells you what escapes
- If a function returns `Integer`, nothing heap-allocated escapes
- If a function returns `Text`, one string escapes — copy it to parent
- If a function returns `Token` (a record), the record + all its fields escape
- If a function returns `List Token`, the list spine + all elements + their fields escape

### The IR already knows the return type
`IRRegion(Body, Type, NeedsEscapeCopy)` — `Type` is the return type.

---

## Escape Copy Strategy

| Type | Escape action |
|------|--------------|
| Integer/Boolean/Number | No action (scalar) |
| Text | Copy: allocate in parent, memcpy len+data |
| Record | Deep copy: allocate in parent, copy each field recursively |
| Sum type | Deep copy: copy tag + active variant's fields |
| List | Deep copy: walk spine, copy each cons cell + element |
| Function (closure) | Skip region (capture types unknown at region exit) |

Specialized per-type helper functions are emitted at compile time.
Helpers may enqueue additional helpers for nested types (e.g., a record
containing a list field triggers emission of both record and list helpers).

---

## Implementation Status

### Phase 2a: IRRegion annotation ✅

```csharp
public sealed record IRRegion(IRExpr Body, CodexType Type, bool NeedsEscapeCopy) : IRExpr(Type)
{
    public static bool TypeNeedsHeapEscape(CodexType type) => type is
        TextType or RecordType or SumType or ListType or ConstructedType
        or FunctionType { Return: not null };
}
```

Set in `Lowering.cs` when wrapping function bodies.

### Phase 2b: RISC-V escape copy ✅

Full deep copy for Text, Record, List, Sum types. Standalone helper
functions with A0=in/A0=out convention. Region reclamation enabled.
~300 lines in `RiscVCodeGen.cs`.

### Phase 2c: All backends ✅ (2026-03-24)

| Backend | Region reclamation | Escape copy |
|---------|-------------------|-------------|
| **RISC-V** | ✅ Enabled | Deep copy: Text, Record, List, Sum |
| **x86-64** | ✅ Enabled | Deep copy: Text, Record, List, Sum |
| **ARM64** | ✅ Enabled | Deep copy: Text, Record, List, Sum |
| **WASM** | ✅ Enabled | Text deep copy + flat copy for scalar-only records/sums |
| **IL** | N/A | CLR GC handles it |

**x86-64**: EmitRegion mirrors RISC-V pattern (save HeapReg R10, emit body,
restore, call escape helper). Escape helpers were already written during
the x86-64 summit push — just needed the EmitRegion wiring.

**ARM64**: Full escape infrastructure built from scratch (~200 lines):
EmitRegion, EmitEscapeCopy, ResolveType, per-type helpers (Record, List,
Sum), `__escape_text` runtime helper. Uses X19-X22 as working registers
in helpers, X28 as HeapReg.

**WASM**: Extended beyond text-only. Records/sums with scalar-only fields
get flat memcopy escape (safe because no pointers to become dangling).
Types with nested heap pointers still skip the region. Full WASM deep
copy would require function-table-based helpers — deferred.

### Closures

All backends skip regions for `FunctionType` returns. Closure capture
types are not statically known at region exit, so region reclamation
is not safe. This is the remaining gap — closures allocated in a region
will never be reclaimed until the program exits.

---

## Open Questions

1. **Closure escape**: Requires tracking what each closure captures and
   deep-copying the environment. Interacts with trampoline implementation.

2. **Nested regions**: Phase 1 only has function-level regions. Sub-function
   regions (around `let` bindings) would need escape analysis within a
   single function, not just at return boundaries.

3. **LinearityChecker integration**: Linear values consumed exactly once
   could provide formal proof that non-escaping values are dead at region
   exit. This would close the soundness gap.

4. **WASM deep copy for nested types**: Records/sums/lists with heap-pointer
   fields need recursive copy in WASM. Would require emitting WASM helper
   functions via the function table.

---

## What's Built

- `IRRegion` node wraps every function body (Lowering.cs)
- RISC-V: full escape copy + region reclamation (RiscVCodeGen.cs)
- x86-64: full escape copy + region reclamation (X86_64CodeGen.cs)
- ARM64: full escape copy + region reclamation (Arm64CodeGen.cs)
- WASM: text + scalar-only record/sum escape (WasmModuleBuilder.Emit.cs)
- IL: no-op (CLR GC handles it)
- `LinearityChecker` tracks consume-once semantics
- Type definitions accessible via `IRModule.TypeDefinitions` in all emitters
