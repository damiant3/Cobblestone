# Camp II-C Summit — Investigation Handoff

**Date**: 2026-03-23
**From**: Agent Windows (review + diagnosis assist)
**For**: Cam (next session)
**Status**: Rate-limited mid-investigation. Resume here.

---

## Where Cam Left Off

Cam was 16 minutes into diagnosing why the self-hosted RISC-V binary
produces corrupted output (stage3 ≠ stage1). He had narrowed it to a
**field access type propagation bug**.

### His Last Words

> "The field text is at index 1 in Token, but EmitFieldAccess defaults
> to index 0 if RecordType is unavailable. Let me check if the bootstrap
> compiler correctly propagates RecordType through field access chains."

He was checking type definition processing order in the self-hosted
compiler's type checker when the rate limit hit.

---

## The Bug (Agent Windows Analysis)

### `EmitFieldAccess` (RiscVCodeGen.cs:656-675)

```csharp
int fieldIndex = 0;  // ← defaults to 0 if type is not RecordType
if (fa.Record.Type is RecordType rt)
{
    for (int i = 0; i < rt.Fields.Length; i++)
        if (rt.Fields[i].FieldName.Value == fa.FieldName)
        { fieldIndex = i; break; }
}
```

If `fa.Record.Type` is NOT a `RecordType` (e.g., it's `ErrorType`,
`TypeVariable`, or some unresolved type), **every field access returns
field 0** regardless of which field was requested.

### `LowerFieldAccess` (Lowering.cs:596-607)

```csharp
IRExpr record = LowerExpr(fa.Record, ErrorType.s_instance);
```

The record sub-expression is lowered with `ErrorType.s_instance` as
expected type. If the type checker didn't resolve the record expression
to a concrete `RecordType`, this propagates through to the IR and the
emitter sees a non-RecordType on `fa.Record.Type`.

### Impact

For `Token { kind = K, text = T, span = S }`:
- `token.kind` → field 0 → correct (it IS field 0)
- `token.text` → field 0 → **WRONG** (should be field 1)
- `token.span` → field 0 → **WRONG** (should be field 2)

This would cause silent data corruption in the self-hosted binary —
the lexer reads `.text` but gets `.kind`, etc. This explains why the
RISC-V binary compiles but produces wrong output.

### WASM Has the Same Bug

`WasmModuleBuilder.Emit.cs:743` — identical pattern. Both native
backends default field index to 0 when RecordType is unavailable.

---

## Likely Root Cause

The self-hosted compiler's **type definitions may be processed in the
wrong order**, causing forward-referenced record types to not be resolved
when they appear in field access expressions. Cam was investigating
this when rate-limited.

Check:
1. Does `TypeChecker` resolve all type definitions before checking
   function bodies?
2. Are there circular type references that prevent resolution?
3. Does `LowerFieldAccess` see `RecordType` on the record expression,
   or some placeholder?

---

## Defensive Fix (Optional — doesn't fix root cause)

Add a warning in `EmitFieldAccess` when type is not RecordType:

```csharp
if (fa.Record.Type is not RecordType)
    Console.Error.WriteLine($"RISCV WARNING: field access '{fa.FieldName}' on non-RecordType {fa.Record.Type}");
```

This would immediately show which field accesses are falling through
to index 0 during compilation of the self-hosted binary.

---

## Test Status

- **390 tests pass, 0 fail** (all suites green)
- **All 40 RISC-V QEMU tests pass** (including text builtins)
- Build green, 0 code warnings
- Backup taken: `D:\NewRepository-backup-20260323-091752.zip` (3.3 MB)
- CurrentPlan.md updated with review notes

---

## Session Stats

Today's session (2026-03-22/23):
- V2 Narration Layer: 6 CPL forms, 44 tests
- RISC-V Parity: phases 1-7, heap/records/sums/patterns/text/regions
- Camp II-C: lists, closures, file I/O, register spill, 11 bug fixes
- RiscVCodeGen.cs: 513 → 2,248 lines
- Total tests: 390+ passing
- Cam's pace: ~2 minutes per feature branch, 15 min for RISC-V phases 1-4
