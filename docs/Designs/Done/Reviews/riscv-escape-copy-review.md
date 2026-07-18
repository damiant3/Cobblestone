# Code Review: cam/riscv-escape-copy

**Reviewer:** Agent Linux (Opus 4.6)  
**Date:** 2026-03-23  
**Commits:** `a874b2f`, `55ec5e4`  
**Scope:** +308/−227 lines across 13 files (core: `RiscVCodeGen.cs`, `RiscVEncoder.cs`)

---

## Summary

Two commits implementing RISC-V escape copy for heap types returned from regions: Text (via runtime helper), Record (field-by-field deep copy), List (loop with element copy), and Sum types (tag-dispatched per-constructor copy). Also adds `Slli`/`Srli` to the encoder and cleans up 7 placeholder prelude effect files with their tests.

**Verdict:** Architecture is sound — the region save/restore pattern, the type-dispatch, and the recursive deep copy design are all correct in principle. Two bugs block merge: one critical (stack overflow on recursive types), one high (register lifetime in sum copy).

---

## What's Good

- **`__escape_text` helper** — Clean leaf function. Alignment formula `(length + 15) & ~7` is correct. Byte copy loop properly structured with patched branch.
- **`EmitRecordEscapeCopy` / `EmitListEscapeCopy`** — Both correctly use `AllocLocal()` for pointers that must survive across `EmitEscapeCopy` subcalls. This is the right pattern.
- **List copy loop** — Correctly reloads `idxLocal` and `savedLen` from locals each iteration (handles spilled registers). Jump-back target is before the loads. Branch patch offset arithmetic is correct.
- **`Slli`/`Srli` encoder** — Correctly masks to 6-bit shift amounts (`& 0x3F`) for RV64I.
- **Closure skip** — `if (region.Type is FunctionType) return EmitExpr(region.Body)` is correct; capture types are unknown at region exit.
- **Region entry/exit** — Saving S1 to a local before the body and restoring after is the right pattern.
- **ConstructedType resolution** via `m_typeDefs` — Returns null safely on missing key (Map indexer doesn't throw). The `is CodexType resolved` guard handles the miss gracefully.
- **Cleanup** — Removing Camera, Display, Identity, Location, Microphone, Network, Sensors stubs and their tests is clean housekeeping.

---

## Issues

### 1. CRITICAL — Stack overflow on recursive sum types

**Repro:**
```bash
dotnet run --project tools/Codex.Cli -- build Codex.Codex --target riscv-bare
→ Stack overflow.
   at EmitSumCtorCopy → EmitSumTypeEscapeCopy → EmitEscapeCopy → EmitSumCtorCopy → ...
```

**Cause:** The self-hosted compiler has recursive sum types (e.g., AST nodes where a constructor field is itself the same sum type). `EmitEscapeCopy` tries to statically inline the deep copy for every nested type. When a type refers to itself (directly or indirectly), this produces unbounded compile-time recursion.

Simple samples (hello, greeting) compile fine because they don't have recursive types. The crash only manifests on the self-hosted compiler's own type definitions.

**Fix direction:** Emit **named per-type escape copy helper functions** instead of inlining. This mirrors the `__escape_text` pattern but generalized:

```
m_escapeCopyHelpers: Dictionary<string, int>   // typeKey → instruction offset

EmitEscapeCopy(srcLocal, type):
    typeKey = canonical key for type
    if typeKey in m_escapeCopyHelpers:
        // Already emitted (or in progress) — emit call
        emit: mv a0, srcLocal
        emit: call __escape_{typeKey}
        emit: mv result, a0
        return result
    m_escapeCopyHelpers[typeKey] = -1   // sentinel: "in progress"
    ... emit the helper function body ...
    m_escapeCopyHelpers[typeKey] = actual offset
```

The "in progress" sentinel handles mutual recursion: when type A contains type B which contains type A, the second encounter of A emits a call to the (not-yet-complete) helper, which gets patched when the helper finishes.

---

### 2. HIGH — Register lifetime bug in `EmitSumCtorCopy`

**File:** `RiscVCodeGen.cs`, `EmitSumCtorCopy` method

`newPtr` is allocated as a **temp** register:
```csharp
uint newPtr = AllocTemp();
Emit(RiscVEncoder.Mv(newPtr, Reg.S1));
```

But it's used *after* `EmitEscapeCopy` subcalls that allocate their own temps:
```csharp
if (IRRegion.TypeNeedsHeapEscape(fieldType))
{
    // EmitEscapeCopy internally calls AllocTemp() — may recycle newPtr's register
    fieldVal = EmitEscapeCopy(fieldLocal, fieldType);
}
Emit(RiscVEncoder.Sd(newPtr, fieldVal, (1 + i) * 8));  // newPtr may be stale
```

`EmitRecordEscapeCopy` gets this right — it uses `AllocLocal()` + `StoreLocal`/`LoadLocal` for `newPtr`. `EmitSumCtorCopy` should do the same.

**Fix:** Change to the local pattern:
```csharp
uint newPtrLocal = AllocLocal();
uint tmp = AllocTemp();
Emit(RiscVEncoder.Mv(tmp, Reg.S1));
StoreLocal(newPtrLocal, tmp);
Emit(RiscVEncoder.Addi(Reg.S1, Reg.S1, totalSize));

// Copy tag
Emit(RiscVEncoder.Sd(LoadLocal(newPtrLocal), LoadLocal(savedTag), 0));

// ... and LoadLocal(newPtrLocal) everywhere newPtr was used ...
```

---

## Minor Observations

- **ConstructedType resolution is single-level.** If a ConstructedType resolves to another ConstructedType, only one hop is taken. This is probably fine for concrete type defs but worth a comment noting the assumption.
- **No test for escape copy in the existing test suite.** The Emit.Tests project has no tests exercising regions with `NeedsEscapeCopy = true`. Consider adding a targeted test that compiles a small program with a string-returning region and verifies the QEMU output.

---

## Test Results

- `dotnet build tools/Codex.Cli` — **pass** (0 warnings, 0 errors)
- Simple RISC-V compilation (`hello.codex`, `greeting.codex`) — **pass**, runs correctly under QEMU
- Self-hosted compilation (`Codex.Codex --target riscv-bare`) — **FAIL: stack overflow** (Bug #1)
