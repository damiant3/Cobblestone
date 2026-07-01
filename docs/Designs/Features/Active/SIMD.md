# SIMD — Data-Parallel Vector Types for Codex

## Status

Phase 1 shipped: the Real type, SSE2-packed vector ops
(addpd/paddq/addps/cmppd), VectorMask, and the `~` operator are live
with tests (vector-basic / vector-f32 / vector-int, vec-pattern).
Pending: AVX/VEX (Phase 2), AVX-512 / SVE / RISC-V V (Phase 3), and
vector intrinsics (Phase 4).

References: `docs/Reference/SIMD_Architecture_References.md`

---

## Real — Floating-Point Types with Confidence Levels

`Number` is renamed to `Real`. The qualifier communicates confidence
level, not bit width. A programmer who has never heard of IEEE 754
reads the type name and immediately understands the precision tradeoff.

```
  Real                    -- f64, ~15 significant digits
  Real approximate        -- f32, ~7 significant digits
  Real guess              -- f16, ~3 significant digits (future, ML inference)
```

### Safety Modes

Like `wrapping`/`clamping`/`error` on bounded integers, Real types
carry a safety mode that determines edge-case behavior:

```
  Real                        -- default: NaN/Inf propagate silently (IEEE 754)
  Real trapping               -- traps (ud2) on NaN or Inf instead of propagating
  Real saturating             -- clamps to +-MAX instead of producing Inf
  Real checked                -- every op returns Result Real, NaN is a type error
  Real approximate trapping   -- f32 that traps on NaN/Inf
  Real guess saturating       -- f16 that clamps to +-MAX
```

Safety modes compose with precision tiers. The default mode is
IEEE 754 semantics (silent propagation) for compatibility with
existing code and hardware behavior. The safer modes insert checks
after each arithmetic operation.

### Codegen for Safety Modes

**trapping:** After each arithmetic op, emit `ucomisd` (unordered
compare) against itself. NaN is the only value where `x /= x`. If
unordered (PF=1), jump to trap. For Inf, compare against `+-MAX`.
Cost: 2-3 instructions per operation.

**saturating:** After each op, emit a compare-and-conditional-move
sequence to clamp the result to `[Real-MIN, Real-MAX]`. NaN becomes
zero (or trap — TBD). Cost: 3-4 instructions per operation.

**checked:** The return type changes to `Result Real`. The compiler
wraps each operation in a NaN/Inf check and returns `Err` on failure.
Cost: the Result wrapper plus branch, but the type system forces the
caller to handle it.

### Migration

`Number` becomes an alias for `Real` during the transition period.
Existing code continues to compile. The alias is deprecated and
removed in a future CL. The IR node `IrAddNum` becomes `IrAddReal`,
etc. The foreword math library functions gain Real variants.

### Approximate Comparison (`~` operator)

Equality (`==`, `/=`) is a compile-time error on Real types (CDX2085).
IEEE 754 equality is a well-documented source of silent bugs
(`0.1 + 0.2 == 0.3` is false). Codex makes approximate comparison
explicit via the `~` operator:

```
  x ~ y                        -- "about equal": within 4 ULP (default)
  x ~0 y                       -- bitwise exact (zero tolerance)
```

The `~` operator reads as "approximately equal" and returns `Boolean`
(scalar) or `VectorMask N` (vector).

**Default (4 ULP):** `x ~ y` computes the ULP distance between x and
y. If the distance is <= 4, the result is True. ULP distance scales
automatically with magnitude — comparing 1e20 values uses the same
operator as comparing 1e-20 values. The default of 4 ULP accounts for
typical accumulated rounding (one or two chained operations). This
covers 99% of floating-point comparison needs without the programmer
having to think about epsilon values.

**Zero tolerance:** `x ~0 y` is ordinal comparison — the values must
map to the same position in the IEEE 754 ordering. +0.0 ~0 -0.0 is
True (same ordinal). NaN ~0 NaN is False. The programmer explicitly
wrote the zero, acknowledging that exact floating-point comparison is
intentional. This covers hash table keys, NaN-boxing, serialization
round-trip checks, and other cases where bit-exact equality is correct.

**Negation:** `x /~ y` is "not approximately equal" (future).

**Variable tolerances deferred.** `~Nulp` (explicit ULP count) and
`~(expr)` (arbitrary comptime tolerance) are not implemented. The two
forms above — default ULP and exact — cover the common cases. Variable
tolerances can be added via feature request if a real use case arises.

**Ordering operators remain.** `<`, `>`, `<=`, `>=` are well-defined
in IEEE 754 (NaN comparisons return False, which is correct behavior).
These are not banned. Only `==` and `/=` are errors.

**NaN handling in `~`:** NaN is not approximately equal to anything,
including itself. `NaN ~ NaN` is False. `NaN ~0 NaN` is False. This
matches IEEE 754 semantics and avoids a class of bugs where NaN
silently compares as "close enough."

#### Codegen

**ULP comparison (`~`, default 4 ULP):** IMPLEMENTED (CL 4412).

```
  float-to-ordinal converts IEEE 754 sign-magnitude to monotonic
  integer ordering (sar/shr/xor64/sub). Then |a - b| via sar/xor64/sub.
  Compare against threshold (4 for ~, 0 for ~0). setcc/movzx result.
```

Cost: ~15 instructions scalar. Both `~` and `~0` use the same
`float-to-ordinal` path — only the `cmp-ri` threshold differs.

#### Diagnostic

| Code | Severity | Meaning |
|------|----------|---------|
| CDX2085 | error | `==` or `/=` used on Real type — use `~` operator |

The diagnostic message reads:

```
CDX2085: Floating-point equality is not safe. Use the ~ operator:
  x ~ y         (approximately equal, 4 ULP tolerance)
  x ~0 y        (bitwise exact, if you are certain)
```

---

## Design Goals

1. **Type-safe vectors as first-class types.** Vector width and element
   type are part of the type. A `Vector 4 Integer` is not a
   `Vector 8 Integer`. Mismatched widths are type errors. Element types
   are restricted to numeric primitives (Integer, Number, bounded
   integers).

2. **Portable by default, architecture-specific by escape hatch.** Code
   written against the portable vector API compiles to SSE, AVX, NEON,
   SVE, or RISC-V V depending on the target. Architecture-specific
   intrinsics are available but opt-in and target-gated.

3. **No autovectorization.** The compiler does not silently vectorize
   scalar loops. SIMD is explicit — the programmer writes vector types
   and vector operations. This follows the Codex virtue of "no magic"
   and avoids the fragility of autovectorization (where an innocuous
   code change silently de-vectorizes a hot loop with no warning).

4. **Alignment is in the type.** A `Vector 4 Real` carries 32-byte
   alignment. The compiler enforces alignment at allocation and rejects
   misaligned casts. This avoids the C++26 std::simd mistake where
   alignment is not encoded in the type.

5. **Dependent-type integration.** The lane count is a type-level
   integer. Operations that change width (concat, split, shuffle) carry
   their arithmetic through the type system. `concat : Vector N a,
   Vector M a -> Vector (N + M) a`. This follows the Mojo precedent
   and fits naturally into Codex's existing dependent type machinery.

6. **Bounded integers in lanes.** `Vector 16 (Integer between 0 and 255)`
   is a valid type — a vector of 16 bytes with range guarantees. The
   bounds prover applies per-lane. This is unique to Codex and enables
   safe SIMD for pixel processing, crypto, and protocol parsing.

7. **Linear vectors.** A `linear Vector 4 FileHandle` is meaningful —
   each lane holds a resource. The linearity checker counts lane-wise.
   This is a stretch goal, not required for Phase 1.

---

## The Type

```
Vector N T
```

- `N` : comptime-known positive integer (1, 2, 4, 8, 16, 32, 64)
- `T` : element type, restricted to:
  - `Integer` (64-bit signed, maps to i64 lanes)
  - `Real` (IEEE 754 f64, maps to f64 lanes)
  - `Real approximate` (IEEE 754 f32, maps to f32 lanes)
  - `Real guess` (IEEE 754 f16, maps to f16 lanes — future)
  - `Integer between L and H` (bounded, maps to i8/i16/i32/i64 based on range)
  - Any Real with a safety mode (`Real trapping`, `Real approximate checked`, etc.)

The lane count `N` is a type-level value, not a runtime value. The
compiler rejects non-power-of-two N and N > 64 (covers all current
hardware: AVX-512 = 64 bytes, SVE max = 256 bytes at 2048 bits).

### Alignment

Vector values carry natural alignment: `N * sizeof(T)` rounded up to
the next power of two, minimum 16. A `Vector 4 Real` (4 x 8 = 32
bytes) is 32-byte aligned. A `Vector 2 Integer` (2 x 8 = 16 bytes) is
16-byte aligned.

The allocator (`__alloc`) must respect this. On the current bump
allocator, alignment is achieved by rounding R10 up before allocation.

### Suggested Width

```
  suggested-vector-width : Type -> Integer
```

Returns the natural lane count for a given element type on the current
target, or 1 if the target has no SIMD. This is the portable way to
write width-agnostic code:

```
  let n = suggested-vector-width Real
  in process-batch n data
```

The value is comptime-known (resolved at emit time from the target
profile). It is NOT a runtime CPUID query — the compiler picks the
width when emitting code for a specific target.

| Target | Real (f64) | Real approx (f32) | Integer (i64) | Byte (i8) |
|--------|:---:|:---:|:---:|:---:|
| x86-64 SSE2 (baseline) | 2 | 4 | 2 | 16 |
| x86-64 AVX2 | 4 | 8 | 4 | 32 |
| x86-64 AVX-512 | 8 | 16 | 8 | 64 |
| ARM64 NEON | 2 | 4 | 2 | 16 |
| ARM64 SVE (256-bit) | 4 | 8 | 4 | 32 |
| RISC-V V (VLEN=128) | 2 | 4 | 2 | 16 |
| WASM SIMD128 | 2 | 4 | 2 | 16 |
| Fallback (no SIMD) | 1 | 1 | 1 | 1 |

---

## Operations

### Construction

```
  vec-splat : a -> Vector N a
  vec-splat (x) = Vector N filled with x in every lane

  vec-from-list : List a -> Vector N a
  vec-from-list (xs) = first N elements of xs (compile-time length check)

  vec-literal : comptime [a, a, ...] -> Vector N a
  vec-literal = inline construction from literal list
```

### Arithmetic (element-wise)

All standard arithmetic operators work on vectors, element-wise:

```
  + : Vector N a, Vector N a -> Vector N a
  - : Vector N a, Vector N a -> Vector N a
  * : Vector N a, Vector N a -> Vector N a
  / : Vector N a, Vector N a -> Vector N a
```

The type checker enforces matching N and a on both operands.

Scalar broadcast is explicit via `vec-splat`, not implicit:

```
  let v = vec-splat 3
  in w * v
```

### Comparison (element-wise, produces mask)

```
  <  : Vector N a, Vector N a -> VectorMask N
  >  : Vector N a, Vector N a -> VectorMask N
  <= : Vector N a, Vector N a -> VectorMask N
  >= : Vector N a, Vector N a -> VectorMask N
```

For integer vectors, `==` and `/=` are also available:

```
  == : Vector N Integer, Vector N Integer -> VectorMask N
  /= : Vector N Integer, Vector N Integer -> VectorMask N
```

For Real vectors, `==`/`/=` are CDX2085 errors. Use `~` instead:

```
  ~      : Vector N Real, Vector N Real -> VectorMask N
  ~0     : Vector N Real, Vector N Real -> VectorMask N
```

`VectorMask N` is a vector of N booleans, one per lane. On x86-64 this
maps to a k-register (AVX-512) or an XMM/YMM comparison result
(SSE/AVX). On ARM64 NEON it maps to a comparison result register. On
ARM64 SVE it maps to a predicate register.

### Reduction (vector to scalar)

```
  vec-reduce-add : Vector N a -> a
  vec-reduce-mul : Vector N a -> a
  vec-reduce-min : Vector N a -> a
  vec-reduce-max : Vector N a -> a
  vec-reduce-and : Vector N Integer -> Integer
  vec-reduce-or  : Vector N Integer -> Integer
  vec-reduce-xor : Vector N Integer -> Integer
```

### Selection and Shuffle

```
  vec-select : VectorMask N, Vector N a, Vector N a -> Vector N a
  vec-select (mask) (if-true) (if-false) = per-lane conditional

  vec-shuffle : Vector N a, Vector N a, [N x Integer] -> Vector N a
  vec-shuffle (a) (b) (indices) = rearrange lanes by index
```

`vec-shuffle` indices are comptime-known. Negative indices select from
the second source vector (following the Zig convention: ~i selects
lane i from the second operand).

### Lane Access

```
  vec-extract : Vector N a, Integer -> a
  vec-extract (v) (i) = element at lane i (bounds-checked)

  vec-insert : Vector N a, Integer, a -> Vector N a
  vec-insert (v) (i) (x) = v with lane i replaced by x
```

These are scalar operations and should be avoided in hot paths.

### Memory

```
  vec-load  : Pointer a -> Vector N a
  vec-store : Pointer a, Vector N a -> Nothing

  vec-gather : Vector N (Pointer a) -> Vector N a
  vec-scatter : Vector N (Pointer a), Vector N a -> Nothing
```

Gather/scatter require AVX2+ on x86, SVE on ARM64, V on RISC-V. The
compiler emits a diagnostic (CDX4030) if gather/scatter is used on a
target that lacks hardware support, and falls back to scalar
extract-load-insert sequences.

### Bitwise (integer vectors only)

```
  bit-and  : Vector N Integer, Vector N Integer -> Vector N Integer
  bit-or   : Vector N Integer, Vector N Integer -> Vector N Integer
  bit-xor  : Vector N Integer, Vector N Integer -> Vector N Integer
  bit-not  : Vector N Integer -> Vector N Integer
  bit-shl  : Vector N Integer, Vector N Integer -> Vector N Integer
  bit-shru : Vector N Integer, Vector N Integer -> Vector N Integer
```

### Mask Operations

```
  mask-and   : VectorMask N, VectorMask N -> VectorMask N
  mask-or    : VectorMask N, VectorMask N -> VectorMask N
  mask-not   : VectorMask N -> VectorMask N
  mask-all   : VectorMask N -> Boolean
  mask-any   : VectorMask N -> Boolean
  mask-none  : VectorMask N -> Boolean
  mask-count : VectorMask N -> Integer
```

---

## Architecture-Specific Intrinsics (Escape Hatch)

For operations not covered by the portable API (AES-NI, CRC32,
CLMUL, specific rounding modes, dot product, SAD, etc.), each backend
exposes a gated intrinsic module:

```
  cites Foreword chapter X86Simd
  cites Foreword chapter Arm64Simd
  cites Foreword chapter RiscVSimd
```

Intrinsic functions are annotated with their required feature level:

```
  @requires "avx2"
  x86-vpermps : Vector 8 Real, Vector 8 Integer -> Vector 8 Real
```

Using an intrinsic without the target having the required feature is a
compile-time error (CDX2080). The target feature set is part of the
compile profile (already established: `ELF QEMU-11.0.0`,
`CDX Cortex-A72`, etc.).

---

## x86-64 Codegen

### Encoding Infrastructure

The current X86_64Encoder.codex has SSE2 scalar instructions using
legacy prefixes (0x66, 0xF2, 0xF3) + 2-byte opcodes (0x0F XX). Packed
SIMD requires:

**Phase 1 (SSE2 packed):** Same legacy encoding, different opcodes.
Example: `addpd` (packed double add) is `[0x66, 0x0F, 0x58, ModRM]`
vs the existing `addsd` which is `[0xF2, 0x0F, 0x58, ModRM]`. The
only difference is the prefix byte.

**Phase 2 (AVX/AVX2):** VEX prefix encoding. New encoder functions:

```
  vex-2byte : Integer, Integer, Integer, Integer -> List Integer
  vex-2byte (r) (vvvv) (l) (pp) =
   [#C5, (bit-not r) * 128 + (bit-not vvvv) * 8 + l * 4 + pp]

  vex-3byte : Integer, Integer, Integer, Integer, Integer, Integer, Integer -> List Integer
  vex-3byte (r) (x) (b) (mmmmm) (w) (vvvv) (l) (pp) =
   [#C4, (bit-not r) * 128 + (bit-not x) * 64 + (bit-not b) * 32 + mmmmm,
    w * 128 + (bit-not vvvv) * 8 + l * 4 + pp]
```

VEX instructions are 3-operand (non-destructive): `vaddpd ymm0, ymm1, ymm2`.
The vvvv field encodes the second source register (inverted).

**Phase 3 (AVX-512):** EVEX prefix (4 bytes after 0x62). Adds mask
registers (k1-k7), zeroing/merging, broadcast, and 512-bit width.

### Register Allocation

XMM0-XMM15 (SSE/AVX) share encodings with YMM0-YMM15 (AVX, selected
by VEX.L=1) and ZMM0-ZMM31 (AVX-512, selected by EVEX.LL=2 and
EVEX.V' for regs 16-31).

Vector register allocation is independent of the integer register pool:

```
  Vector temps: XMM0-XMM7 (rotation, like integer alloc-temp)
  Vector locals: XMM8-XMM15 (callee-saved in our convention)
```

Integer registers remain untouched. The existing temp/local rotation
scheme extends naturally — add `alloc-vector-temp` and
`alloc-vector-local` to CodegenState.

### Spill/Reload

Vector spill slots are 16/32/64 bytes depending on the active width.
Stack frame size computation must account for alignment. The prologue
already aligns RSP to 16 bytes; AVX requires 32-byte alignment,
AVX-512 requires 64-byte.

### Instruction Table (Phase 1 — SSE2 Packed)

| Operation | Instruction | Encoding |
|-----------|------------|----------|
| `Vector 2 Real + Vector 2 Real` | ADDPD xmm, xmm | `[0x66, 0x0F, 0x58, ModRM]` |
| `Vector 2 Real - Vector 2 Real` | SUBPD xmm, xmm | `[0x66, 0x0F, 0x5C, ModRM]` |
| `Vector 2 Real * Vector 2 Real` | MULPD xmm, xmm | `[0x66, 0x0F, 0x59, ModRM]` |
| `Vector 2 Real / Vector 2 Real` | DIVPD xmm, xmm | `[0x66, 0x0F, 0x5E, ModRM]` |
| `Vector 2 Real == Vector 2 Real` | CMPPD xmm, xmm, 0 | `[0x66, 0x0F, 0xC2, ModRM, 0]` |
| `Vector 4 (Real approximate) + ...` | ADDPS xmm, xmm | `[0x0F, 0x58, ModRM]` |
| `Vector 4 (Real approximate) - ...` | SUBPS xmm, xmm | `[0x0F, 0x5C, ModRM]` |
| `Vector 4 (Real approximate) * ...` | MULPS xmm, xmm | `[0x0F, 0x59, ModRM]` |
| `Vector 4 (Real approximate) / ...` | DIVPS xmm, xmm | `[0x0F, 0x5E, ModRM]` |
| `Vector 16 Byte + Vector 16 Byte` | PADDB xmm, xmm | `[0x66, 0x0F, 0xFC, ModRM]` |
| `Vector 8 i16 + Vector 8 i16` | PADDW xmm, xmm | `[0x66, 0x0F, 0xFD, ModRM]` |
| `Vector 4 i32 + Vector 4 i32` | PADDD xmm, xmm | `[0x66, 0x0F, 0xFE, ModRM]` |
| `Vector 2 Integer + Vector 2 Integer` | PADDQ xmm, xmm | `[0x66, 0x0F, 0xD4, ModRM]` |
| `vec-load (aligned)` | MOVDQA xmm, [mem] | `[0x66, 0x0F, 0x6F, ModRM]` |
| `vec-store (aligned)` | MOVDQA [mem], xmm | `[0x66, 0x0F, 0x7F, ModRM]` |
| `vec-reduce-add (f64x2)` | HADDPD + extract | multi-insn |
| `vec-shuffle` | SHUFPD / PSHUFD | depends on element width |

---

## ARM64 Codegen

### NEON (128-bit fixed)

ARM64 NEON uses the V0-V31 register file (128-bit). Instructions are
fixed 32-bit words. The `Q` bit selects 64-bit vs 128-bit operation.
The `size` field selects element width (00=8b, 01=16b, 10=32b, 11=64b).

Vector register encoding reuses the same 5-bit Rd/Rn/Rm fields as
scalar ARM64. New encoder functions:

```
  arm64-neon-rrr : Integer, Integer, Integer, Integer, Integer -> List Integer
  arm64-neon-rrr (opcode) (q) (size) (rd) (rn) (rm) = arm64-encode (...)
```

### SVE (scalable, future)

SVE uses predicate registers (P0-P15) and a runtime-variable vector
length. The programming model differs: loops use `whilelt` to generate
predicates and `incp` to advance. This is a Phase 3 concern.

### Instruction Table (Phase 1 — NEON)

| Operation | Instruction | Encoding |
|-----------|------------|----------|
| `Vector 2 Real + Vector 2 Real` | FADD V0.2D, V1.2D, V2.2D | Q=1, size=11 |
| `Vector 4 (Real approximate) + ...` | FADD V0.4S, V1.4S, V2.4S | Q=1, size=10 (float) |
| `Vector 4 i32 + Vector 4 i32` | ADD V0.4S, V1.4S, V2.4S | Q=1, size=10 |
| `Vector 16 Byte + Vector 16 Byte` | ADD V0.16B, V1.16B, V2.16B | Q=1, size=00 |

---

## RISC-V Codegen

### V Extension

RISC-V V is scalable (like SVE). Vector length is configured at runtime
via VSETVLI. The programming model:

1. `vsetvli t0, a0, e64, m1` — configure 64-bit elements, LMUL=1
2. `vle64.v v1, (a1)` — vector load
3. `vadd.vv v3, v1, v2` — vector add
4. `vse64.v v3, (a3)` — vector store

The scalable model means the compiler emits loops with `vsetvli` as
the trip-count control, processing `vl` elements per iteration.

This is Phase 3. Phase 1 on RISC-V emits scalar fallbacks for vector
types (element-wise loops using integer registers).

---

## IR Representation

New IR nodes:

```
  IrVecSplat    : IrExpr, VectorWidth -> IrExpr
  IrVecArith    : IrBinOp, IrExpr, IrExpr, VectorWidth, ElemType -> IrExpr
  IrVecCompare  : IrCmpOp, IrExpr, IrExpr, VectorWidth, ElemType -> IrExpr
  IrVecReduce   : IrReduceOp, IrExpr, VectorWidth, ElemType -> IrExpr
  IrVecShuffle  : IrExpr, IrExpr, List Integer, VectorWidth, ElemType -> IrExpr
  IrVecSelect   : IrExpr, IrExpr, IrExpr, VectorWidth -> IrExpr
  IrVecLoad     : IrExpr, VectorWidth, ElemType -> IrExpr
  IrVecStore    : IrExpr, IrExpr, VectorWidth, ElemType -> IrExpr
  IrVecExtract  : IrExpr, Integer, VectorWidth, ElemType -> IrExpr
  IrVecInsert   : IrExpr, Integer, IrExpr, VectorWidth, ElemType -> IrExpr
  IrVecGather   : IrExpr, VectorWidth, ElemType -> IrExpr
  IrVecScatter  : IrExpr, IrExpr, VectorWidth, ElemType -> IrExpr
```

The VectorWidth and ElemType are carried through IR so the emitter can
select the correct instruction without re-deriving the type. This
follows the existing pattern where IrAddInt vs IrAddNum distinguishes
integer and float arithmetic.

The IR text emitter writes these as:
```
  (vec-splat 4 f64 (expr))
  (vec-add 4 f64 (left) (right))
  (vec-reduce-add 4 f64 (expr))
```

The plug backends (ARM64, RISC-V, WASM) receive these IR nodes via the
text parser and emit their target instructions.

---

## Type Checker Integration

### New Type Constructor

```
  VectorTy : Integer, Type -> Type
```

`VectorTy(N, T)` where N is a literal integer and T is a numeric type.
The unifier rejects unification of `VectorTy(4, Integer)` with
`VectorTy(8, Integer)` — different widths are different types.

### VectorMask

```
  VectorMaskTy : Integer -> Type
```

A mask type parameterized only by lane count. Produced by vector
comparisons, consumed by `vec-select` and masked operations.

### Overloaded Operators

The existing operator dispatch (IrAddInt, IrAddNum, etc.) extends with
IrVecArith. The type checker resolves `v1 + v2` where both are
`VectorTy(4, RealTy)` to `IrVecArith(IrAddReal, v1, v2, 4, f64)`.
For `Real approximate` vectors: `IrVecArith(IrAddReal, v1, v2, 4, f32)`.

### Real Safety Mode Propagation

Vector operations on `Real trapping` lanes emit per-instruction NaN/Inf
checks after the packed operation. Example: `Vector 4 (Real approximate
trapping)` emits ADDPS followed by CMPUNORDPS + MOVMSKPS + test + jnz
(jump to trap if any lane produced NaN). The safety mode is carried in
the type and the IR ElemType field.

### Bounds Prover

The static bounds prover (CDX4010) extends to vector lanes. If both
operands are `Vector N (Integer between L and H)`, the result type
propagates range arithmetic lane-wise. For `vec-reduce-add` over
`Vector N (Integer between 0 and 255)`, the result range is
`[0, N * 255]`.

---

## Phasing

### Phase 1: Real type + SSE2 Packed + NEON (128-bit vectors)

- Rename `Number` to `Real` throughout the compiler (IR, type checker,
  emitter, foreword). `Number` kept as deprecated alias.
- Add `Real approximate` (f32) as a type. Scalar f32 SSE instructions
  (MOVSS, ADDSS, MULSS, etc.) for non-vector use.
- Add `Real trapping`, `Real saturating`, `Real checked` safety modes.
  Codegen for NaN/Inf checks after each fp operation.
- VectorTy and VectorMaskTy in the type checker
- IR nodes for vec arithmetic, compare, reduce, shuffle, select
- x86-64 encoder: SSE2 packed instructions (legacy prefix encoding)
- ARM64 encoder: NEON instructions
- RISC-V: scalar fallback (element-wise loops)
- `suggested-vector-width` builtin
- Foreword chapter: `Vector` (construction, arithmetic, reduction)
- Foreword chapter: `Real` (conversion, safety mode utilities)
- Test samples: dot product, pixel blend, byte search
- Vector pattern matching: `when v is Vector [0, 0, 0, 0] -> ...`

Scope: `Vector 2 Real`, `Vector 4 (Real approximate)`,
`Vector 2 Integer`, `Vector 4 i32`, `Vector 8 i16`, `Vector 16 i8`.
All fit in 128-bit XMM / NEON Q regs. Real approximate doubles the
lane count at every width tier — the primary motivation for f32.

### Phase 2: AVX/AVX2 + VEX Encoding

- VEX prefix encoder (2-byte and 3-byte forms)
- 256-bit operations: `Vector 4 Real`, `Vector 4 Integer`, etc.
- YMM register allocation (extends XMM pool)
- 32-byte aligned spill slots
- Non-destructive 3-operand form (vvvv source register)
- Gather instructions (VGATHERDPD, etc.)
- Profile-gated: only emitted when compile target includes `avx2`

### Phase 3: AVX-512 + SVE + RISC-V V

- EVEX prefix encoder
- 512-bit operations, ZMM0-ZMM31
- Mask registers (k1-k7) as first-class VectorMask storage
- Zeroing vs merging masking
- ARM64 SVE: scalable vectors, predicate registers, whilelt loops
- RISC-V V: VSETVLI configuration, scalable loop model
- 64-byte aligned spill slots

### Phase 4: Intrinsic Modules + Crypto

- X86Simd foreword: AES-NI, CLMUL, CRC32, VPERM, VPOPCNT
- Arm64Simd foreword: AES, SHA, polynomial multiply
- Crypto foreword rewrite: SHA-256 using vec-shuffle + AES-NI
  (currently scalar; the CryptoPrimitives.md performance targets
  assumed "no SIMD" — this is where the payoff arrives)

---

## Interaction with Existing Features

### Punctual Functions

A `punctual` function may use vector operations. The instruction
counter counts vector instructions at the same weight as scalar
instructions (one instruction = one count, regardless of lane width).
This is architecturally honest: a VADDPD is one instruction on the
pipeline, even though it does 4 additions.

### Effect System

Vector operations are pure — no effects. `vec-load` and `vec-store`
require a pointer, which comes from the memory effect. The vector
operations themselves do not introduce new effects.

### Unit Types

`Vector 4 (Second)` is a valid type — a vector of 4 time values. The
unit wrapper is erased at codegen (the vector contains raw integers).
Cross-unit vector arithmetic is a type error, same as scalar.

### Mutable Records with Vector Fields

A mutable record can contain a vector field. Field assignment works
as expected. The alignment requirement propagates to the record layout.

---

## Memory and Time-Complexity Assessment

**Memory:** Vector types are value types, same size as their hardware
representation (16/32/64 bytes). No heap allocation for vector
operations — they live in registers or on the stack. The only new
allocation is the vector spill slots in the stack frame, bounded by
the number of live vector variables (at most 16 XMM regs on x86-64).

**Time:** All portable vector operations map 1:1 to hardware
instructions (or short fixed-length sequences for reductions). No
hidden loops, no heap traversal. `vec-reduce-add` on a `Vector 4 Real`
is 2 instructions (VHADDPD + VHADDPD or VADDPD + permute + VADDPD).
Gather/scatter may be multi-cycle on some microarchitectures but are
single instructions.

**Compiler impact:** New IR nodes add ~200 bytes per vector operation
to the IR deck. The emitter adds per-function vector register tracking
(~48 bytes for the XMM allocation state). Negligible impact on the
selfhost compile budget.

---

## Decisions

**Q5: Register allocator interaction.** DECIDED: Separate domain.
Vector registers (XMM/YMM/ZMM, NEON V-regs, RISC-V V-regs) are a
separate allocation pool from integer registers. The planned
linear-scan allocator (Option A) targets integer registers only.
Vector allocation uses its own rotation/spill scheme
(`alloc-vector-temp`, `alloc-vector-local`). The two domains never
compete for the same physical registers.

**Q4: WASM SIMD.** DECIDED: Defer. WASM SIMD128 is not in Phase 1.
The WASM plug continues emitting scalar code for vector types until
a later phase adds SIMD128 lowering.

**Q3: Scalable vectors (SVE/RISC-V V).** DECIDED: Keep it simple for
Phase 1. Fixed-width `Vector N T` only. Scalable vectors are Phase 3.
When we get there, the simplest path is a separate `ScalableVector T`
type where the lane count is implicit (determined by hardware at
runtime via VSETVLI / whilelt). No need to over-design this now.

**Q2: Vector literals in pattern matching.** DECIDED: Yes. `when v is
Vector [0, 0, 0, 0] -> ...` will work. The pattern matcher emits a
per-lane comparison sequence (PCMPEQ + PMOVMSKB on x86, CMEQ + UMAXV
on NEON) and branches on the all-lanes-equal result. This is the hard
thing, and it makes vector types feel first-class in the language
rather than second-class intrinsic wrappers.

**Q1: Single-precision float.** DECIDED: `Number` becomes `Real` (f64).
`Real approximate` is f32 (scalar and vector). `Real guess` is f16
(future, ML inference). Safety modes `trapping`/`saturating`/`checked`
apply to all tiers. The qualifier names communicate confidence level,
not bit width — a programmer who has never heard of IEEE 754 reads
`Real approximate` and immediately understands the precision tradeoff.
f32 ships as part of Phase 1 (both scalar and `Vector 4 (Real
approximate)`), because it is the primary motivation for SIMD lane
density.

## Open Questions

No open questions remain. All five resolved.
