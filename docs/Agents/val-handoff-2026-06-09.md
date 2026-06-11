# Val Agent Handoff -- 2026-06-09

## Session Summary

**Five codegen optimizations** landed on CodexMagic (CLs 3575-3604),
plus cross-compiler benchmark documentation (CL 3595) and cmp-imm
copy-up to main (CL 3563).

Seed: 2,124,472 -> 2,117,367 bytes (-7,105 bytes this session).
Project total: 2,191,873 -> 2,117,367 = -74,506 bytes.

## CLs This Session

| CL   | Description                         | Seed delta |
|------|-------------------------------------|------------|
| 3561 | merge down from main (apps/build)   | -          |
| 3562 | merge down starmap data             | -          |
| 3563 | copy-up cmp-imm to main             | -          |
| 3575 | emit-binary-reg-right               | -7,644 B   |
| 3595 | codegen analysis doc (C/C#/F#/Py)   | -          |
| 3602 | eval-tail-arg-direct                | +2,608 B   |
| 3603 | nop-sub-rsp-0 + dead TCO removal    | -1,920 B   |
| 3604 | cmp-ri-or-test                      | -149 B     |

## Benchmark Progress

| Bench | Before | After | C /Od | C /O2 | C# JIT | F# JIT |
|-------|--------|-------|-------|-------|--------|--------|
| fib   | 32     | 32    | 19    | 20    | 21     | 21     |
| fact  | 27     | 27    | 16    | 15    | 16     | 15     |
| gcd   | 32     | 32    | 18    | 14    | 11     | 9      |
| sum   | 36     | 29    | 20    | 23    | 9      | 4      |

Sum improved 36->29 from TCO arg direct emit. Others unchanged
because fib/fact are not TCO, and gcd's bottleneck is the math-mod
function call.

## New Optimizations Explained

### emit-binary-reg-right (CL 3575)

When right operand of any binary op is IrName referencing a register
local (slot < spill-base), route around emit-binary-standard entirely.
Passes left result and right's register directly to emit-binary-op.
No alloc-local, no store, no load. Guarded by `is-safe-for-reg-right`
which excludes IrEq/IrNotEq on SumTy (where emit-sum-full-eq would
alloc-local and potentially conflict with right-reg).

### eval-tail-arg-direct (CL 3602)

Replaces emit-expr + store-local in TCO arg evaluation with pattern-
specific fast paths:
- IrIntLit/IrBoolLit/IrCharLit: `li target, imm` directly
- IrBinary add/sub with IrIntLit: `lea target, [l.reg +/- imm]`
- IrBinary add/sub/mul with register-local right: `mov target, l.reg;
  op target, right-reg` (guarded by `is-tco-binop`)
- Everything else: falls back to emit-expr + store-local

Key lesson: `is-tco-binop` must be a strict whitelist (IrAddInt,
IrSubInt, IrMulInt only). The original `emit-tco-binop` had an
`otherwise -> add-rr` fallback that silently produced wrong code
for comparisons as TCO args.

### nop-sub-rsp-0 (CL 3603)

When frame-size is 0 (no spills, all locals fit in registers),
overwrite the `sub rsp, imm32` with a 7-byte NOP sequence. Also
removed the unreachable `alloc-temp + li 0` after the TCO backward
jmp in emit-tail-call.

### cmp-ri-or-test (CL 3604)

Use `test reg, reg` (3 bytes) instead of `cmp reg, 0` (4 bytes)
for zero comparisons in emit-if-fused-imm, emit-if-to-local, and
emit-binary-cmp-imm paths.

## Cross-Compiler Benchmark Analysis

Full comparison added to `docs/Designs/Compiler/Active/CodegenAnalysis.md`.
Key finding: .NET 9 RyuJIT (C# and F#) produces code that matches or
beats MSVC /O2 on all four benchmarks. F# sum-to compiles to 4
instructions (movsxd + add + dec + jne).

Benchmark sources added: bench/csharp/, bench/fsharp/, bench/python/.

## Attempted and Reverted

**Per-function stack guard elision for leaf functions:** Attempted to
detect leaf functions by comparing cp-offsets before/after body
emission. FAILED because indirect calls (`call rax` / `[0xFF, 0xD0]`)
are not tracked by cp-offsets. Functions with closure calls (map,
sort-by, etc.) appeared as leaf but weren't. Crashed during self-
compile at RIP=0x139f2e with CR2=0xffffffffffffff83 (negative offset
dereference).

**Fix needed:** Add `func-has-calls : Boolean` to CodegenState (requires
updating 5+ construction sites). Set True in emit-call-to AND in
emit-indirect-call / emit-indirect-call-value / emit-over-apply-extras
(where `[255, 208]` is emitted). Then the leaf detection is correct.

**emit-to-local in eval-tail-args:** Direct swap of emit-expr +
store-local with emit-to-local caused 147 test failures. Root cause:
emit-to-local's IrIf fast path (emit-if-to-local) skips an alloc-local
that emit-if would allocate, changing next-local. Fixed by using
targeted pattern matching (eval-tail-arg-direct) instead.

## Next Priorities

1. **Stack guard elision** (needs func-has-calls on CodegenState)
2. **TCO copy-temps-to-params elision** (dependency analysis for 2-arg
   parallel assignment -- sum could go from 29 to ~24)
3. **Inline math-mod for integers** (emit cqo+idiv -- gcd 32->~22)
4. **Register allocator Option A** (linear-scan pre-pass)

## Workspace State

- Stream: `//Codex/CodexMagic`, client: `BigWhite_Codex_val`
- Parent: `//Codex/main`
- Merged down through CL 3554
- p4 clean, full build green, hard fixed point one pass
- Seed: 2,117,367 bytes (CL 3604)
- Copy-up of CLs 3575-3604 to main still pending
