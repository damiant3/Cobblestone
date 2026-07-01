# Review: cam/ring4-self-hosting

**Reviewer:** Agent Linux  
**Date:** 2026-03-25  
**Status:** Merged to master — 1 bug found by new regression tests

---

## Bug: x86_64 `is-whitespace` — returns true for non-whitespace characters

**File:** `src/Codex.Emit.X86_64/X86_64CodeGen.cs`  
**Severity:** Functional — false positives

The `is-whitespace` implementation accumulates matches using `Setcc` + `AddRR` across four character comparisons (space, tab, newline, CR). The letter "a" incorrectly returns 1.

**Suspected cause:** Register aliasing in `AllocTemp()`. The implementation calls `AllocTemp()` for both `rd` (the loaded character byte) and `t2` (the scratch for each Setcc). If these are assigned overlapping registers, `Setcc` into `t2` clobbers the character value in `rd` before the remaining comparisons execute.

Trace through the code:
```csharp
byte rd = AllocTemp();           // e.g. R8
X86_64Encoder.MovzxByte(...);    // rd = char byte
byte result = AllocTemp();       // e.g. R9
X86_64Encoder.Li(..., result, 0);
X86_64Encoder.CmpRI(..., rd, ' ');
X86_64Encoder.Setcc(..., CC_E, result);
byte t2 = AllocTemp();           // ← if this returns R8 (same as rd), we're clobbered
```

If `AllocTemp()` wraps or reuses `rd`'s register, the `CmpRI(rd, '\t')` on the next line compares garbage.

**Fix:** Either pin `rd` as a local (`AllocLocal`) so it isn't recycled, or explicitly save it to a callee-saved register before the comparison chain.

**Regression test:** `LinuxNativeTests.IsWhitespace_letter_rejected_x86_64` — expects "a"→0, gets 1.

---

## is-letter CC_BE fix — LGTM

The `CC_LE → CC_BE` fix for `is-letter` (both lowercase and uppercase range checks) is correct. Same bug class as the `is-digit` fix. Confirmed by the existing test suite passing.
