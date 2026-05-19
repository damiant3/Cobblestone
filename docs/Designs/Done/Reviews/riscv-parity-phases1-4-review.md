# Code Review: windows/riscv-parity-phases1-4

**Reviewer:** Agent Linux (Opus)  
**Date:** 2026-03-22  
**Commits:** `94074d4`, `9db51a9`, `26bd0d2` (merged in `ad8b485`)  
**Scope:** +1,019 lines across `RiscVCodeGen.cs` and `RiscVEmitterTests.cs`

---

## Summary

Three commits bringing RISC-V feature parity with the IL/WASM backends: heap-allocated records with field access, sum types with tagged constructors, recursive pattern matching, text builtins (length, concat, char-at, substring, show, string equality, integer-to-text, text-to-integer), and a register allocator split that fixes multi-field pattern clobbering.

**Verdict:** Architecturally sound. One critical silent-corruption issue in `AllocLocal` saturation needs fixing before complex programs are compiled.

---

## What's Good

- **Register pool split (26bd0d2)** — Temps (T3–T6, caller-saved, rotating) vs locals (S2–S11, callee-saved, linear) is the right design. Fixes the root cause of multi-field pattern clobbering.
- **Frame layout** — 12 slots × 8 = 96 bytes, offsets consistent between prologue and epilogue, S1 correctly excluded from CalleeSaved as global heap pointer.
- **Runtime helpers** — `__str_eq`, `__str_concat`, `__itoa`, `__text_to_int` all stick to caller-saved registers (plus S1 for heap bumping). No prologues needed.
- **EmitPrintI64 S1 save/restore** — Correct fix for the inline itoa that predates the helper refactor and still uses S1 as scratch.
- **Pattern matching** — Recursive `EmitMatchBranches` with forward-patched branch offsets is clean. Handles wildcard, variable, literal, and constructor patterns.
- **Equality fix** — Old `Slti(rd, rd, 1)` was wrong for negative differences (e.g. `5 == 7` → sub gives -2, slti says -2 < 1 → true). New `Sltu(zero, rd)` + `Xori(rd, 1)` is correct.
- **Test coverage** — Records, sum types, both match arms, combined scenarios, text builtins, string equality/inequality — all with QEMU execution validation.

---

## Issues

### 1. CRITICAL — `AllocLocal` saturates silently

**File:** `RiscVCodeGen.cs` line ~1765  
**Severity:** High — silent data corruption

```csharp
uint AllocLocal()
{
    uint reg = m_nextLocal;
    m_nextLocal++;
    if (m_nextLocal > Reg.S11) m_nextLocal = Reg.S11; // saturate — no wrap
    return reg;
}
```

Once S2–S11 are exhausted (10 registers), every subsequent call returns S11. Multiple variables silently alias to the same register. A function with 3 parameters + a let binding + a binary op + a match with 2 constructor arms each binding 2 fields is already 10+ locals.

**Fix options (pick one):**
- Spill to stack frame when S-registers are exhausted
- Emit a compile-time diagnostic ("register pressure exceeded in function X")
- At minimum: throw an exception so it fails loud instead of producing wrong code

### 2. MODERATE — `AllocTemp` rotates over only 4 registers (T3–T6)

**File:** `RiscVCodeGen.cs` line ~1757  
**Severity:** Medium — silent clobbering in deeply nested expressions

The wrap-around means 5+ simultaneously live temps silently overwrite each other. Less likely to hit than issue 1 since temps are short-lived, but nested `EmitRecord` → `EmitExpr` → `EmitBinary` → `AllocTemp` chains could get there.

**Fix:** Same as above — spill or fail loud.

### 3. LOW — `EmitRegion` skips save/restore for composite types

**File:** `RiscVCodeGen.cs`, `EmitRegion`  
**Severity:** Low (tech debt)

```csharp
if (region.Type is RecordType or SumType or ListType)
    return EmitExpr(region.Body);
```

Region semantics are effectively disabled for the most allocation-heavy types. Heap pointer never gets restored, so memory is never reclaimed. Fine for now, but defeats the purpose of the region allocator for these types. Track as tech debt for Camp III-A integration.

### 4. COSMETIC — `m_nextTemp` field initializer is dead code

**File:** `RiscVCodeGen.cs` line 22

```csharp
uint m_nextTemp = Reg.T0;  // never used — EmitFunction resets to Reg.T3
```

`EmitFunction` immediately overwrites this to `Reg.T3`. Nothing between construction and the first `EmitFunction` call uses `AllocTemp`. Should be `Reg.T3` or removed to avoid confusion.

### 5. COSMETIC — Duplicate test section header

**File:** `RiscVEmitterTests.cs`

Two identical `// ═══ Bare Metal tests ═══` banners with nothing between them. Looks like a merge artifact.

---

## Recommendation

Fix issue 1 (AllocLocal saturation) before compiling anything more complex than the current test suite. Everything else can be addressed incrementally.

— Agent Linux
