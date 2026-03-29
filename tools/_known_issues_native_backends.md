# Known Issues — Native Backends (x86-64 + RISC-V)

Date: 2026-03-28

## Usermode Self-Compilation Status

Full self-compilation works on x86-64 usermode AND bare metal (QEMU 512MB).
Identity emitter output: 5,063 lines, 187K chars, 1,126 arrow types.
Semantic fixed point verified (Stage 1 == Stage 2 after blank-line normalization).
C# emitter output: 310,424 chars, 252 Func<> types, 435 List<> types — within
31 chars of the .NET reference (cosmetic CCE boundary differences only).

### Resolved issues

1. **~~Boolean type produces empty output~~** — RESOLVED. Was a stale binary.

2. **~~String literal CCE escaping~~** — NOT A BUG. CCE runtime handles it.

3. **~~TCO list-concat crash~~** — RESOLVED (commit 3f1eef4, branch
   cam/fix-tco-binary-tail-position). Root cause: `EmitBinary` did not clear
   `m_inTailPosition` before evaluating operands. Self-recursive calls inside
   binary ops (`++`, list append) were incorrectly promoted to tail calls.
   Fix applied to all three native backends (x86-64, RISC-V, ARM64).
   Verified: `[n] ++ acc` in TCO loop now correctly builds the list.

3b. **~~Missing EffectTypeExpr in desugarer~~** — RESOLVED. Case already present
   at Desugarer.codex line 113.

5. **~~List ++ in recursive functions returns empty~~** — RESOLVED. Same root
   cause as item 3 (TCO binary tail-position bug).

### Remaining issues

4. **show on parametric sum type fields** — type variable not resolved to
   concrete type in IR. Reproducer: `safe-divide.codex`.

6. **run-state effect not implemented** in native backends.
   Reproducer: `state-demo.codex`.

## RISC-V specific

7. Missing `__ipow` (PowInt) — x86-64 has exponentiation by squaring.

## Test status

- 24/24 sample programs pass on qemu-riscv64 (direct compile)
- 24/24 sample programs pass on qemu-x86_64 (direct compile)
- 917/917 dotnet tests pass
- 9, 10, 12 parameter functions pass on RISC-V
- Self-hosted: integer programs compile on both backends
- Self-hosted: Boolean/full source fails on both backends

## Outstanding commits (cam/mm3-easy-builtins, rebased on master)

1. e2b4407 — RISC-V stack arg spill offset (>8 params)
2. 910abd3 — x86-64 read-file CCE boundary (path + content)
3. 2ec3355 — RISC-V read-file CCE boundary + rodata fixup slots
