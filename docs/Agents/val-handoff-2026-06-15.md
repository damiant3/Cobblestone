# Val Handoff — 2026-06-15: SIMD/Real Story

## What Was Done

Eight CLs implementing the foundation of the SIMD and Real type story:

| CL | Stream | Main | What |
|------|--------|------|------|
| 4392 | design | 4428 | SIMD design doc + architecture references |
| 4399 | design | 4428 | Decisions: separate vector reg domain, defer WASM, fixed-width Phase 1 |
| 4402 | design | 4428 | Real type: confidence tiers (Real/approximate/guess), safety modes |
| 4403 | design | 4428 | ~ operator design, CDX2085 design |
| 4411 | impl | 4428 | ~ operator end-to-end (Tilde token, OpApproxEq, IrApproxEq, ULP codegen) |
| 4412 | impl | 4428 | ULP codegen fix: xor-rr is 32-bit, xor-rr64 required for 64-bit IEEE 754 |
| 4417 | impl | 4428 | CDX2085: ban == and /= on Number types |
| 4418 | impl | 4428 | ~0 zero-tolerance operator (TildeZero token, IrApproxEqExact) |
| 4419 | design | 4428 | Design doc updated to match implemented state |
| 4420 | impl | 4428 | VectorTy(N, T) type constructor in type system |
| 4425 | impl | 4428 | vec-splat/extract/add/sub/mul/div builtins + SSE2 packed codegen |
| 4426 | merge | — | merge-down from main (4410-4423) |
| 4427 | seed | 4428 | seed rebuild for copy-up |
| 4433 | impl | 4436 | Vector +,-,*,/ operator overloading (IrAddVec etc.) |

## Current State

**Seed:** 2,297,938 bytes. Hard fixed point (one-pass). 149 pass / 1 pre-existing (multiline-app-continuation).

**What works:**
- `x ~ y` — approximate equality, 4 ULP tolerance (float-to-ordinal algorithm)
- `x ~0 y` — zero-tolerance ordinal comparison
- `x == y` on Number — CDX2085 compile error with helpful diagnostic
- `Vector 2 Number` — type annotation accepted, type-checked, unified
- `vec-splat 3.0` — broadcast scalar to both lanes (heap-allocated 16 bytes)
- `vec-extract v 0` — extract lane by index
- `a + b`, `a - b`, `a * b`, `a / b` on vectors — SSE2 ADDPD/SUBPD/MULPD/DIVPD
- IR text emitter/parser handles all new ops (plug backends can see them)

**Architecture:**
- Vectors are heap-allocated 16-byte blocks (pointer in GPR)
- XMM0/XMM1 used only during operations (MOVUPD load/store from heap)
- No dedicated vector register allocation yet (separate domain, per design)
- float-to-ordinal: sign-magnitude to monotonic ordering via sar/shr/xor64/sub

## Key Files

| File | What Changed |
|------|-------------|
| codex/compiler/Syntax/Lexer.codex | cc-tilde, Tilde/TildeZero tokens |
| codex/compiler/Syntax/Token.codex | Tilde, TildeZero variants |
| codex/compiler/Syntax/ParserCore.codex | Tilde/TildeZero at precedence 4 |
| codex/compiler/Syntax/SyntaxNodes.codex | copy-sx-kind for new tokens |
| codex/compiler/Ast/AstNodes.codex | OpApproxEq, OpApproxEqExact in BinaryOp |
| codex/compiler/Ast/Desugarer.codex | Tilde/TildeZero to OpApproxEq/Exact |
| codex/compiler/Types/TypeCheckerInference.codex | is-real-type, CDX2085 check, VectorTy in is-arithmetic-type |
| codex/compiler/Types/CodexType.codex | VectorTy(Integer, CodexType), is-pointer-type = True |
| codex/compiler/Types/TypeChecker.codex | resolve-applied-type for Vector N T |
| codex/compiler/Types/Unifier.codex | types-equal + unify-structural for VectorTy |
| codex/compiler/Types/CodexTypeTree.codex | map/fold children for VectorTy |
| codex/compiler/Types/TypeEnv.codex | vec-splat/extract/add/sub/mul/div type bindings |
| codex/compiler/Core/CdxCodes.codex | cdx-real-equality-banned = 2085 |
| codex/compiler/IR/IRChapter.codex | IrApproxEq, IrApproxEqExact, IrAddVec/SubVec/MulVec/DivVec |
| codex/compiler/IR/LoweringTypes.codex | is-vector-type, vector dispatch in lower-bin-op |
| codex/compiler/IR/ResolveTypes.codex | identity maps for all new IR ops |
| codex/compiler/Emit/IRTextEmitter.codex | text emission for all new ops + VectorTy |
| codex/compiler/Emit/CodexEmitter.codex | precedence, text, associativity for vec ops |
| codex/compiler/Emit/X86_64.codex | emit-approx-eq, float-to-ordinal, emit-approx-eq-exact, vector arith inline |
| codex/compiler/Emit/X86_64Encoder.codex | movupd-load/store, addpd/subpd/mulpd/divpd, unpcklpd |
| codex/compiler/Emit/X86_64Builtins.codex | vec-splat/extract/add/sub/mul/div emitters |
| codex/compiler/Emit/X86_64Compound.codex | emit-const-codextype stub for VectorTy |
| codex/compiler/Semantics/NameResolver.codex | vec-* in builtin names |
| codex/plugs/common/IRTextParser.codex | parse all new IR ops + VectorTy |

## Lessons Learned

1. **xor-rr is 32-bit.** The encoder's `xor-rr` omits REX.W (only adds REX for high registers). For 64-bit XOR on IEEE 754 bit patterns, use `xor-rr64`. Cost: 3 hours of debugging garbage output.

2. **Closures with integer captures crash the seed.** Passing an Integer parameter through a lambda closure (e.g., `\s a -> emit-vec-arith-builtin s a 88`) causes a GPF during seed compilation. Workaround: create individual wrapper functions or pass pre-computed List Integer instead.

3. **print-line vs print-line-uni.** Test programs must use `print-line-uni` for Unicode output. `print-line` emits raw CCE bytes. The test harness compares byte-for-byte without CCE conversion.

4. **Merge down before adding functions to large files.** The seed crashed when adding a function to X86_64.codex (~94KB). After merging down (which brought compact TCO refactoring), the same edit worked. The file was at a capacity edge.

5. **show on Number includes .0 suffix.** `show 7.0` produces `"7.0"`, not `"7"`. Expected files must match.

## What's Next (Phase 1 Remaining)

From docs/Designs/Features/Active/SIMD.md:

- **vec-reduce-add** — horizontal sum (HADDPD or shuffle+ADDSD). Enables dot product: `vec-reduce-add (a * b)`.
- **Vector comparison** — `~` and `~0` on vectors producing VectorMask N.
- **VectorMask type** — vec-select for conditional per-lane operations.
- **Integer vector types** — Vector 4 i32, Vector 16 i8 (different element widths).
- **Vector pattern matching** — `when v is Vector [0, 0] -> ...` (PCMPEQ + PMOVMSKB).
- **Real approximate (f32)** — Real approximate type with scalar ADDSS/MULSS and packed ADDPS/MULPS. Doubles lane count.
- **Number → Real rename** — mechanical, large CL.
- **Safety modes** — trapping/saturating/checked on Real types.
- **suggested-vector-width** — comptime builtin returning natural lane count for target.

The most impactful next step is vec-reduce-add (completes the dot product story) followed by integer vectors (widens the use cases).
