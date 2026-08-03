# Mutable Records with Linear Ownership

**Date**: 2026-05-17
**Status**: Phase 1+2 implemented (CL 1636, 1740); Phase 3 (linearity) pending
**Depends on**: SAFE-MUTATION.md (established linearity principle)
**Blocks**: game engines (quadratic record copy), OS schedulers, any hot loop with state

---

## Problem

Every record "update" in Codex allocates a new copy. A game loop that
runs 500 turns updating a 15-field `GameState` record allocates 500
complete copies -- one per turn, none freed (bare metal has no GC).
In practice this means:

- **Heap blowup.** A 200-byte record updated 500 times = 100KB of
  dead copies. A real game with nested records (board + players +
  permanents) can hit the heap wall in 30 turns.

- **Quadratic time.** Each copy is O(fields). Nested records
  (record containing List of records) are O(fields × list-length).
  Deep game states with 50+ permanents become O(n²) per turn.

- **Programmer gymnastics.** To avoid copies, the programmer must use
  `__record-set` (a raw intrinsic with no safety checks) or restructure
  code to avoid intermediate states. Both are error-prone.

The current workaround -- `__record-set` -- is the right mechanism at
the wrong abstraction level. It does in-place field mutation. It's
fast. But it has no ownership check: if anyone else holds a reference
to the old record, they silently see the new value. This is exactly
the aliasing bug that immutability was designed to prevent.

## Proposal

Add a `mutable` qualifier for record types. A mutable record:

1. **Must be linearly owned.** Exactly one binding holds the record at
   any point. The compiler tracks this -- passing a mutable record to
   two call sites is a compile error.

2. **Supports in-place field assignment.** `record.field = expr` emits
   a direct store to the field offset (same codegen as `__record-set`)
   without allocating a new record.

3. **Cannot be aliased.** Assigning a mutable record to a second
   binding moves it -- the original binding is consumed. Reading from
   a consumed binding is a compile error.

4. **Can be frozen.** `freeze record` produces an immutable copy. The
   mutable binding is consumed. The immutable result can be freely
   shared.

### Syntax

```
  mutable GameBoard = record {
    squares : List Integer,
    turn : Integer,
    score : Integer
  }

  update-board : mutable GameBoard -> mutable GameBoard
  update-board (board) =
    board.turn = board.turn + 1    -- in-place write
    board.score = board.score + 10 -- in-place write
    board                          -- return (moved, not copied)

  finalize : mutable GameBoard -> GameBoard
  finalize (board) = freeze board  -- immutable copy, mutable consumed
```

### Semantics

- `mutable` is a type-level qualifier, like `linear` today.
- Mutable records are heap-allocated (same as immutable).
- Field assignment (`r.field = val`) is only valid on mutable records.
- Ownership moves on binding: `let b2 = b` consumes `b`.
- Passing to a function consumes the argument binding.
- Returning from a function transfers ownership to the caller.
- `freeze` allocates a fresh immutable copy and consumes the mutable.

### Linearity Enforcement

The compiler must track a "liveness" flag per mutable binding:

| Operation | Effect on binding |
|-----------|-------------------|
| `let b = MutableRecord { ... }` | b is live |
| `b.field = val` | b must be live, stays live |
| `let b2 = b` | b is consumed (dead), b2 is live |
| `f b` | b is consumed (passed to f) |
| `freeze b` | b is consumed, result is immutable |
| Use after consume | **Compile error CDX2050** |
| Two live aliases | **Compile error CDX2051** |

This is strictly simpler than full linear type checking -- it only
applies to bindings of mutable record type, not all values.

## Compiler Changes

### Phase 1: Parser + AST (small)

- Add `MutableKeyword` token (or reuse `LinearKeyword`).
- Recognize `mutable Name = record { ... }` as a mutable record def.
- `MutableRecordDef` AST node, or flag on existing `RecordDef`.
- Parse `expr.field = expr` as `FieldAssignment` statement
  (currently this would parse as two separate expressions).

### Phase 2: Type System (moderate)

- Introduce `MutableRecordTy(Name, Fields)` alongside `RecordTy`.
- `MutableRecordTy` carries a linearity constraint.
- Type-check field assignment: LHS must be `MutableRecordTy`,
  field must exist, RHS must match field type.
- Track mutable bindings in `TypeEnv` with a consumed flag.
- Error on use-after-consume (CDX2050).
- Error on aliasing (CDX2051).
- `freeze` builtin: `mutable T -> T`.

### Phase 3: IR Lowering (small)

- Lower `FieldAssignment` to `IrFieldStore(record, field-index, value)`.
- Lower `freeze` to `IrRecordCopy(source)`.
- Mutable record construction lowers to same heap alloc as immutable.

### Phase 4: Codegen (small -- mostly reuse __record-set)

- `IrFieldStore` emits the same code as `emit-record-set-builtin`:
  load record pointer, compute field byte offset, store value.
- `IrRecordCopy` emits memcpy from source to new heap allocation.
- No new x86 instructions needed. The codegen path exists.

### Phase 5: Remove __record-set (cleanup)

- Once mutable records work, `__record-set` becomes unnecessary for
  user code. Keep it as a compiler-internal intrinsic only.
- Migrate compiler self-use of `__record-set` to mutable records
  where appropriate (NameResolver scope, emitter state).

## Interaction with Existing Features

### `linear` keyword

`linear` is already parsed and desugared (discarded). Two options:

**Option A**: Reuse `linear`. `linear` on a record type means mutable +
linearly owned. Pro: no new keyword. Con: conflates two concepts (a
linear immutable record is a valid thing -- consumed exactly once but
never mutated).

**Option B**: New `mutable` keyword. A mutable record is implicitly
linear. `linear` remains available for non-mutable linear values
(file handles, etc). Pro: clearer semantics. Con: new keyword.

**Recommendation**: Option B. `mutable` is the right word for the
programmer's intent. Add it to the lexer alongside `linear`.

### Effects

Mutable field assignment is not an effect -- it's a local operation on
an owned value, like `list-snoc`. No effect annotation needed. This
is consistent with SAFE-MUTATION.md: "mutation is safe when ownership
is linear."

### Records containing mutable fields

A mutable record can contain immutable fields (Integer, Text, etc)
and immutable sub-records. A mutable record CANNOT contain another
mutable record as a field -- that would create nested ownership
tracking. Mutable records contain values, not references to other
mutable records.

Lists inside mutable records are still immutable lists. Updating a
list field requires `list-snoc` or constructing a new list. This is
a limitation -- future work could add `mutable List` as well.

### `heap-save` / `heap-restore`

Mutable records live on the heap like all records. `heap-restore`
resets the heap pointer, invalidating all records allocated after
the save point -- including mutable ones. Mutable records do not
extend object lifetime beyond `heap-restore`. This is consistent
with current behavior.

## Migration Path

### Step 1: Compiler internals

Use mutable records for `CodegenState`, `NameResolverScope`, and
`TypeCheckState` -- the three largest records in the compiler that
currently use `__record-set` pervasively. This validates the feature
on the compiler's own codebase before exposing it to users.

### Step 2: Foreword libraries

Convert accumulator patterns in `Sort`, `StringBuilder`, `Sha256`
etc. to use mutable records for their working state.

### Step 3: Game engines

Convert `GameState`, `SimState`, `CheckerBoard` etc. to mutable
records. This eliminates the quadratic copy overhead that currently
limits game complexity on bare metal.

## Risk Assessment

**Memory**: No change -- mutable records use the same heap bytes as
immutable ones. The difference is fewer dead copies.

**Time complexity**: Strictly better -- O(1) field update instead of
O(fields) copy. No regression case exists.

**Correctness**: The linearity check is the critical safety net. If
it has a bug (allows aliasing), silent data corruption follows. The
check should be exhaustively tested with error-case samples.

## Open Questions

1. **Syntax for field assignment.** `board.turn = board.turn + 1` uses
   `=` which currently means "definition." Should field assignment use
   a different operator? `:=`? `<-`? Or is context (LHS is a field
   access) sufficient to disambiguate?

2. **Pattern matching on mutable records.** Can you `when` match on a
   mutable record? Matching would need to borrow (not consume) it.
   Simplest answer: no pattern matching on mutable records -- use
   field access.

3. **Mutable records in act blocks.** Field assignment looks like a
   statement. In an act block (newline-separated statements), this
   works naturally. Outside act blocks (pure let/in chains), field
   assignment would need to be sequenced somehow. Simplest: field
   assignment is only valid inside act blocks or as the body of a
   function that takes and returns the mutable record.

4. **Error message quality.** "Use after consume" errors need to
   identify which operation consumed the binding and where the
   illegal use occurs. The liveness tracker must record the consuming
   span.
