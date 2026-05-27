# Camp III-A Design — Region-Based Linear Allocator

**Date**: 2026-03-22
**Status**: Approved (design decision: regions over drops)
**Depends on**: LinearityChecker (existing), Camp III-B CapabilityChecker (in review)

---

## The Decision

Regions, not drops. The reasoning:

- **Provably correct in the easiest practical way.** That is the value.
- Regions nest with lexical scope, which the compiler already understands.
- Batch deallocation is deterministic and trivial to verify.
- The verifier must itself be verifiable. Simpler means more verifiable.
- Codex chose linear types over borrowing. Regions honor that choice.
- We don't have to be clever. We have to be correct.

We waste some memory (values live until their region closes, not until
their exact consumption point). We can recover precision later with
region refinement. The simplicity we'd lose with drop insertion, we
can never get back.

---

## How Regions Work

A **region** is a lexical scope that owns allocations. When the scope
closes, every allocation in it is freed in one operation.

```
let result =
  region                          -- enter region R1
    let x = Record { a = 1 }     -- allocated in R1
    let y = Record { b = 2 }     -- allocated in R1
    x.a + y.b                    -- result (Integer) escapes R1
                                  -- exit R1: x and y are freed
```

### Rules

1. **Every function body is a region.** Parameters are owned by the caller's
   region. The return value escapes to the caller's region.
2. **`let` bindings allocate in the current region.** The value lives until
   the region closes.
3. **Linear values are consumed exactly once** (already enforced by
   LinearityChecker). The region just handles when the memory is reclaimed.
4. **Scalars don't allocate.** Integer, Boolean, Number are value types.
   Only heap-allocated types (Record, List, Text from concatenation) need
   regions.
5. **Return values escape.** If a function returns a heap value, it's
   promoted to the caller's region. This is the only escape mechanism.

### What About Closures?

Closures that capture heap values extend the region lifetime. For Phase 1,
closures are not yet supported in WASM/RISC-V backends, so this is deferred.
The IL backend uses CLR garbage collection and doesn't need regions.

---

## Implementation Strategy

### Phase 1: WASM Region Stack (this task)

The WASM backend already has a bump allocator with a single `heap_ptr` global.
Regions map perfectly:

- **Enter region**: push `heap_ptr` onto a region stack
- **Allocate**: advance `heap_ptr` as usual (unchanged)
- **Exit region**: pop the region stack, restore `heap_ptr`

Everything allocated between enter and exit is freed in one instruction.

The region stack is a fixed-size array in linear memory (e.g. 256 entries
at a known base address). A `region_sp` global tracks the stack pointer.

```
Region stack layout (in linear memory):
  Base address: 0 (before data segments at 1024)
  Each entry: 4 bytes (i32 saved heap_ptr)
  Max depth: 256 (1024 bytes total)

Globals:
  region_sp: i32 (index into region stack, starts at 0)
  heap_ptr:  i32 (existing, starts at data_end)
```

**Enter region:**
```wasm
;; region_stack[region_sp] = heap_ptr
global.get $region_sp
i32.const 4
i32.mul
global.get $heap_ptr
i32.store
;; region_sp++
global.get $region_sp
i32.const 1
i32.add
global.set $region_sp
```

**Exit region:**
```wasm
;; region_sp--
global.get $region_sp
i32.const 1
i32.sub
global.set $region_sp
;; heap_ptr = region_stack[region_sp]
global.get $region_sp
i32.const 4
i32.mul
i32.load
global.set $heap_ptr
```

That's it. Two sequences, ~10 instructions each. No free lists, no
reference counting, no tracing, no pauses.

### New IR Nodes

```csharp
public sealed record IRRegionEnter() : IRExpr(VoidType.s_instance);
public sealed record IRRegionExit() : IRExpr(VoidType.s_instance);
```

Or, more naturally as a scoping construct:

```csharp
public sealed record IRRegion(IRExpr Body, CodexType Type) : IRExpr(Type);
```

The `IRRegion` node wraps a body expression. The emitter generates
enter-before, body, exit-after. The body's result is the region's result
(it escapes to the parent region).

### Where to Insert Regions

The **Lowering** pass inserts `IRRegion` nodes:
- Around every function body
- Around every `let` binding that allocates a heap value (optional
  refinement — Phase 1 can just do function bodies)

Phase 1 keeps it simple: **one region per function call**. This means
all allocations within a function live until the function returns. Not
optimal, but correct and simple.

### Emitter Changes

| Backend | Change |
|---------|--------|
| WASM | Add region stack in linear memory, emit enter/exit sequences |
| RISC-V | Save/restore heap pointer on the stack (already has a stack) |
| IL | No change needed (CLR GC handles memory) |

### Testing Strategy

1. **IR test**: Lowering wraps function bodies in IRRegion
2. **WASM test**: program that allocates records in a loop — without
   regions, bump allocator grows unbounded; with regions, memory is stable
3. **Memory stability test**: call a function 1000 times that allocates
   a record, verify heap_ptr returns to the same value each time

---

## Phase 2: Region Refinement (future)

Once Phase 1 works:
- Sub-function regions for `let` blocks that allocate
- Region escape analysis: if a value doesn't escape, it can be freed earlier
- Region fusion: adjacent regions with no escaping values merge

These are optimizations. Phase 1 is correct without them.

---

## Phase 3: RISC-V Regions (future)

Same model, different mechanism. The RISC-V backend uses direct memory
management. Region enter/exit saves/restores the heap pointer on the
call stack (which it already manages for function calls).

---

## What This Gives Us

After Phase 1:
- WASM programs that call functions in loops don't run out of memory
- The allocator is provably correct: enter saves, exit restores, done
- The foundation exists for sub-function regions when we need precision
- One step closer to Peak III's summit marker: "its own allocator"
