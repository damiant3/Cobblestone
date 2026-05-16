# ARM64 QEMU Verification + 5-Commit Review

**Agent**: Linux  
**Date**: 2026-03-24  
**Branch**: `linux/arm64-fixes-and-tests`  
**Status**: 96/96 native backend tests green (33 ARM64 + 40 RISC-V + 23 x86-64)

---

## Review of 5 Commits (2026-03-24 morning push)

### 1. Region reclamation on x86-64, ARM64, WASM (e00e077) ✅

Good design — `EmitRegion` now saves/restores HeapReg and calls type-specific
escape copy helpers for heap-allocated return values. ARM64 implementation adds
~200 lines: `EmitEscapeCopy`, `EmitEscapeTextHelper`, `EmitEscapeRecordHelper`,
`EmitEscapeSumHelper`, `EmitEscapeListHelper` with a lazy helper queue pattern.

**Bug found (fixed on this branch):** `__escape_text` null guard used `CBZ`
(branch if zero) with offset `2*4` which jumped into `Ret`, causing the non-null
path to fall through into `Li X0, 0` + `Ret` — zeroing out valid pointers.
Changed to `CBNZ` (branch if non-zero) with offset `3*4` to skip the null-return
block. This was the root cause of the `show` regression.

### 2. x86-64 automated test suite (bf77d8f) ✅

23 tests, clean pattern. `_start` now prints main's return value via `__itoa` +
`write` syscall, matching the RISC-V convention. Tests run natively — no QEMU
needed. All 23 pass on this sandbox. `CompileToX86_64` helper follows the
established `Helpers.cs` pattern.

### 3. x86-64 show builtin (2681fe6) ✅

Straightforward — `show` was missing from `TryEmitBuiltin`, now calls `__itoa`.
Unblocks `main : Text` tests on x86-64.

### 4. x86-64 file I/O builtins (844afbd) ✅

`write-file`, `file-exists`, `get-args`, `current-dir` — all needed for
self-hosting. Direct Linux syscall implementations. Consistent with the RISC-V
pattern.

### 5. ARM64 __str_replace (1070817) ✅

Was a bare `Ret()` stub, now a full 100-instruction implementation. Follows the
same algorithm as RISC-V: scan for pattern, copy non-matching prefix, insert
replacement, advance past match. Uses callee-saved x19-x27 for state across
the main loop. Unblocks the compiler's string manipulation paths on ARM64.

**Bug found (fixed on this branch):** `__str_concat` second copy loop used 8-byte
`Ldr`/`Str` with stride 8 instead of 1-byte `LdrbReg`/`StrbReg` with stride 1.
First copy loop was already correct. Matched second to first.

---

## Fixes on This Branch

| Bug | Location | Symptom | Fix |
|-----|----------|---------|-----|
| `__escape_text` null guard | Arm64CodeGen.cs:1176 | `show 42` segfault (regression from region reclamation) | `CBZ` → `CBNZ`, offset `2*4` → `3*4` |
| `__str_concat` loop 2 | Arm64CodeGen.cs:1295-1300 | `"hello " ++ "world"` segfault | `Ldr`/`Str`/stride 8 → `LdrbReg`/`StrbReg`/stride 1 |

Both bugs were in pre-existing code, not in the 5 reviewed commits, but the
region reclamation commit exposed the escape_text bug by calling it for the
first time.

---

## Test Totals After This Branch

| Backend | Tests | Status |
|---------|-------|--------|
| RISC-V  | 40    | 40/40 ✅ (qemu-riscv64) |
| ARM64   | 33    | 33/33 ✅ (qemu-aarch64) |
| x86-64  | 23    | 23/23 ✅ (native WSL) |
| **Total** | **96** | **96/96** |

---

*Review by Agent Linux. Five commits reviewed, two bugs found and fixed.*
