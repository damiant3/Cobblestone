# Review: Cam Phase 2c — Region Reclamation + x86-64 Expansion

**Reviewer**: Agent Windows (Copilot, VS 2022)
**Date**: 2026-03-24
**Branch**: Cam worktree `D:\Projects\NewRepository-cam` (master, 4 commits ahead of origin)
**Commits reviewed**: `e00e077`, `bf77d8f`, `2681fe6`, `844afbd`

---

## Summary

Four commits totalling **+1,138 / -112 lines** across 8 files:

1. **`e00e077`** — Region reclamation enabled on x86-64, ARM64, WASM (Camp III-A Phase 2c)
2. **`bf77d8f`** — x86-64 automated test suite (23 tests, native WSL execution)
3. **`2681fe6`** — `show` builtin added to x86-64
4. **`844afbd`** — `write-file`, `file-exists`, `get-args`, `current-dir` builtins for x86-64

**Verdict**: ✅ **Approve with one required fix and a few nits.**

The region reclamation work (x86-64, WASM) is solid. The test suite is excellent — 23
tests covering arithmetic, records, sum types, lists, text, HOFs, and register spill,
all running natively in WSL. The x86-64 builtin stubs match the RISC-V equivalents.

ARM64 review deferred — Agent Linux found bugs; updated code incoming.

---

## Build & Test

- `dotnet build Codex.sln` — ✅ Green (only CS5001 in Codex.Codex, pre-existing)
- `dotnet test` — ✅ 435 passed in Codex.Types.Tests (23 new x86-64 tests all pass)
- Pre-existing failures only: `Peek_non_numeric_start_does_not_crash` + 3 agent session log tests

---

## 🔴 Required Fix

### `EmitShow` boolean branch returns wrong register

In `X86_64CodeGen.cs`, the `EmitShow` method for `BooleanType`:

```csharp
case BooleanType:
{
    // ...
    byte trRd = AllocTemp();
    EmitLoadRodataAddress(trRd, trueOff);
    int jmpEnd = m_text.Count;
    X86_64Encoder.Jmp(m_text, 0);
    int falseLbl = m_text.Count;
    PatchJcc(jeFalse, falseLbl);
    byte flRd = AllocTemp();
    EmitLoadRodataAddress(flRd, falseOff);
    int endLbl = m_text.Count;
    PatchJmp(jmpEnd, endLbl);
    return flRd;    // ← BUG: true path wrote to trRd, not flRd
}
```

On the true branch, the result is in `trRd`, but `flRd` is returned. The caller
reads an uninitialized register when `show True` executes.

**Compare RISC-V**: Both branches converge into `A0` (`Mv(Reg.A0, trReg)` / `Mv(Reg.A0, flReg)`).

**Fix**: Either:
- (a) Use the same dest register for both branches, or
- (b) Add `X86_64Encoder.MovRR(m_text, flRd, trRd)` before `jmpEnd` (merge into one reg)

The test `Show_integer_runs_natively` passes because it only tests the Integer path.
A `show True` test would expose this.

---

## 🟡 Nits (non-blocking)

### 1. Dead store in `get-args`

```csharp
case "get-args" when args.Count == 0:
{
    byte gaRd = AllocTemp();
    X86_64Encoder.MovRR(m_text, gaRd, HeapReg);
    X86_64Encoder.MovStore(m_text, HeapReg, Reg.RAX, 0); // ← dead: RAX is garbage
    X86_64Encoder.Li(m_text, Reg.R11, 0);
    X86_64Encoder.MovStore(m_text, HeapReg, Reg.R11, 0); // ← overwrites immediately
    X86_64Encoder.AddRI(m_text, HeapReg, 8);
    return gaRd;
}
```

The first `MovStore` writes uninitialized `RAX` to `[HeapReg]` and is immediately
overwritten by the `R11` store. Harmless (the second store wins) but dead code.
Remove the first `MovStore` line.

### 2. `Helpers.cs` duplication

`CompileToX86_64` and `CompileToArm64` are nearly identical (~86 lines each) —
only the emitter type differs. Consider extracting a shared `CompileToIR` helper
that returns the `IRModule`, then each method just calls its backend. Low priority.

### 3. WASM `ComputeFlatSize` could assert

```csharp
static int ComputeFlatSize(CodexType type) => type switch
{
    RecordType rt => rt.Fields.Length * 8,
    SumType st => (1 + st.Constructors.Max(c => c.Fields.Length)) * 8,
    _ => 8
};
```

The `_ => 8` default is only reachable for types that shouldn't be getting flat-copied
(Text and List are handled elsewhere, Functions skip regions). A `throw` or diagnostic
would catch accidental misuse. Very low priority.

### 4. Missing `show True` / `show False` test

Would catch the 🔴 bug above. Add alongside the fix.

---

## What's Good

- **Region reclamation pattern is clean**: save HeapReg → emit body → restore HeapReg →
  escape copy return value. Consistent across x86-64, ARM64, WASM. Closure skip is correct.

- **`EmitEscapeCopy` signature fix** (`byte` → `int`): Necessary. `AllocLocal()` returns
  `int` (spill slots are ≥32). The old `byte` parameter would truncate. Good catch.

- **`EmitStart` improvements**: Proper function prologue for `_start` (enables AllocLocal),
  type-aware return value printing (Integer→itoa→print, Bool→print, Text→print),
  null-main guard, frame size patching. Well-structured.

- **Heap grow cleanup**: Removed the dead `MovRR/AddRI` pair for the 1MB grow and uses
  the `Li + Add` pattern that was already present. Cleaner.

- **Test suite**: 23 tests with excellent coverage. The WSL detection (`IsWslAvailable`)
  is graceful — tests silently skip on non-WSL machines. `CompileAndRun` cleanup is proper
  (temp dir deleted in `finally`). Good engineering.

- **WASM `TypeHasNestedHeapPointers`**: Smart incremental approach — flat-copy scalar-only
  records/sums, skip types with nested pointers. Gets region reclamation working for the
  common case without needing WASM function-table helpers.

- **Design doc update**: `CAMP-IIIA-ESCAPE-ANALYSIS.md` properly reflects Phase 2c status,
  documents what each backend supports, and the open questions are updated.

---

## Scope Not Reviewed

- **ARM64 escape infrastructure** (~200 lines): Deferred per user direction. Agent Linux
  found bugs; updated code incoming.
- **Self-hosted compilation verification**: Not tested (would require running the native
  compiler under WSL with Codex source input). The structural and unit tests provide
  good confidence.

---

## Recommendation

Fix the `EmitShow` boolean bug, add a `show True` test, optionally clean up the `get-args`
dead store. Then push the branch and merge.
