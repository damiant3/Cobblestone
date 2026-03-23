# ARM64 QEMU Verification

**Reviewer:** Agent Linux (Opus 4.6)  
**Date:** 2026-03-23  
**Scope:** `src/Codex.Emit.Arm64/ElfWriterArm64.cs`, `src/Codex.Emit.Arm64/Arm64Encoder.cs`, `src/Codex.Emit.Arm64/Arm64CodeGen.cs`  
**Also affects:** `src/Codex.Emit.RiscV/ElfWriter.cs` (same bug)

---

## Summary

No ARM64 Linux user-mode binary produced by the Codex compiler has ever run.
QEMU silently rejects every binary with exit code 1 — the same code it returns
for a garbage file. Root cause: ELF LOAD segment alignment is 16 bytes;
Linux requires page-size alignment (4096).

Binary-patching `p_align` from 16 to 0x1000 confirms the fix — hello, factorial,
and fibonacci all run correctly under `qemu-aarch64`. The RISC-V Linux user-mode
ELF writer has the identical bug.

**Verdict:** Three bugs. Bug 1 is a one-line fix per ELF writer. Bug 2 causes
string/list heap corruption. Bug 3 is cosmetic.

---

## Bug 1 — BLOCKER: `p_align = 16` in ELF LOAD segments

**Files:**
- `src/Codex.Emit.Arm64/ElfWriterArm64.cs` lines 72 and 82
- `src/Codex.Emit.RiscV/ElfWriter.cs` (same two lines)

Both LOAD program headers write `(ulong)16` as `p_align`. Linux (and QEMU
user-mode) requires LOAD segment alignment to be at least the page size.
QEMU rejects the binary before executing a single instruction.

**Fix:** Change `(ulong)16` to `(ulong)0x1000` in both places, in both files.

**Verification:** Binary-patched both ARM64 and RISC-V ELFs with `p_align = 0x1000`.
Results under QEMU:

| Sample | ARM64 | RISC-V |
|--------|-------|--------|
| `main = 42` | Prints `42`, exit 0 ✓ | Prints `42`, exit 0 ✓ |
| `hello` (square 5) | Prints `25`, exit 0 ✓ | — |
| `factorial` | Prints `3628800`, exit 0 ✓ | — |
| `fibonacci` | Prints `6765`, exit 0 ✓ | — |
| `greeting` (strings) | Segfault (exit 139) | — |

Note: RISC-V bare metal is unaffected — QEMU `-kernel` loads raw binary, no ELF loader.

---

## Bug 2 — HIGH: `AndImm(-8)` bitmask encoding is inverted

**File:** `src/Codex.Emit.Arm64/Arm64Encoder.cs`, `AndImm` method

The encoding for `AND Xd, Xn, #-8` (mask `0xFFFFFFFFFFFFFFF8`) uses
`N=1, immr=0, imms=60`, which actually encodes `0x1FFFFFFFFFFFFFFF` — clearing
the top 3 bits instead of the bottom 3 bits.

**Fix:** Change `immr` from `0` to `61`:
```csharp
// Before (wrong):
n_immr_imms = (1u << 22) | (0u << 16) | (60u << 10);
// After (correct):
n_immr_imms = (1u << 22) | (61u << 16) | (60u << 10);
```

ARM64 bitmask immediate encoding: `imms` defines the pattern width (61 ones),
`immr` defines the rotation. With `immr=0`, the 61 ones sit at bits [60:0].
With `immr=61`, they rotate to bits [63:3], producing `0xFFFFFFFFFFFFFFF8`.

This bug affects every alignment operation: string allocation in `EmitPrintI64`/itoa,
`__str_concat`, `__list_cons`, `__escape_text`. It's the cause of the greeting
segfault — heap pointer gets corrupted by the wrong mask.

---

## Bug 3 — LOW: `Mov(Fp, Sp)` encodes as `mov x29, #0`

**File:** `src/Codex.Emit.Arm64/Arm64Encoder.cs`, `Mov` method

`Mov(rd, rm)` is implemented as `ORR Xd, XZR, Xm`. In ARM64, register 31 in
the ORR instruction is XZR (zero register), not SP. So `Mov(X29, SP)` becomes
`ORR X29, XZR, XZR` = `mov x29, #0`.

**Fix:** Add a special case for SP:
```csharp
public static uint Mov(uint rd, uint rm)
{
    if (rm == Arm64Reg.Sp) return AddImm(rd, Arm64Reg.Sp, 0);  // ADD Xd, SP, #0
    return Or(rd, Arm64Reg.Xzr, rm);
}
```

Currently cosmetic — nothing reads FP, all frame offsets use SP. But it will
bite when stack unwinding, debuggers, or frame pointer chaining is needed.

---

## Environment

- QEMU 8.2.2 (aarch64 user-mode + riscv64 user-mode + riscv64 system)
- .NET 8.0.419
- Verification method: binary-patched `p_align` field in ELF, ran under QEMU,
  compared with handwritten ARM64 assembly (known-good reference)
