# SIMD Architecture References

References for the Codex SIMD design. Organized by category.

## Hardware Architecture Manuals

**Intel x86 SIMD (SSE through AVX-512)**
Intel Architecture Instruction Set Extensions and Future Features
Programming Reference, Document 319433-061, March 2026.
Covers SSE, SSE2, SSE3, SSSE3, SSE4.1/4.2, AVX, AVX2, AVX-512 (F, CD,
BW, DQ, VL, VBMI, VNNI, BF16, FP16).
https://cdrdv2-public.intel.com/915637/319433-061-architecture-instruction-set-extensions-programming-reference.pdf

**ARM NEON and SVE/SVE2**
Arm Architecture Reference Manual for A-profile Architecture (DDI 0487).
NEON: fixed 128-bit vectors, 32 x Q registers.
SVE/SVE2: scalable vectors 128-2048 bits, predicate registers, gather/scatter.
https://developer.arm.com/documentation/ddi0487/latest

**RISC-V V Extension (Vector)**
RISC-V "V" Vector Extension, Version 1.0 (Ratified November 2021).
Scalable vector model with VLEN 128-65536 bits, 32 vector registers,
7 vector mask registers, configurable element width (SEW) and grouping (LMUL).
https://docs.riscv.org/reference/isa/extensions/vector/_attachments/riscv-v-spec.pdf

## Research Papers

**ISPC: A SPMD Compiler for High-Performance CPU Programming**
Matt Pharr, William R. Mark.
IEEE Innovative Parallel Computing (InPar), 2012.
DOI: 10.1109/INPAR.2012.6339601.
SPMD-on-SIMD programming model. Scalar-looking code compiled to vector
instructions with automatic mask management. Key influence on the
"lanes as implicit parallelism" design pattern.
https://pharr.org/matt/assets/ispc.pdf

**Vc: A C++ Library for Explicit Vectorization**
Matthias Kretz, Volker Lindenstruth.
Software Practice & Experience, Vol. 42, No. 11, 2012.
DOI: 10.1002/spe.1149.
Precursor to C++26 std::simd. Type-safe vector wrappers with operator
overloading. Documents the tension between abstraction and codegen quality.

**Accelerator: Using Data Parallelism to Program GPUs for General-Purpose Uses**
David Tarditi, Sidd Puri, Jose Oglesby.
ASPLOS 2006. DOI: 10.1145/1168919.1168898.
Data-parallel array types with deferred execution. Relevant to the
expression-level vectorization model.

## Language Implementations

**Rust Portable SIMD (std::simd)**
RFC 2948, tracking: https://github.com/rust-lang/portable-simd
Nightly-only as of 2026. Generic `Simd<T, N>` type with N as const
generic. Portable ops + escape hatch to core::arch intrinsics.
Masks as `Mask<T, N>` with per-lane bool semantics.

**Zig @Vector**
https://ziglang.org/documentation/0.16.0/
Builtin `@Vector(len, T)` type. Comptime lane count. Compiler lowers
to LLVM vector IR. Builtins: @splat, @reduce, @shuffle, @select.
`std.simd.suggestVectorSize` for portable width selection.
No intrinsics escape hatch (deliberate design choice).

**Mojo SIMD[dtype, size]**
Dependent-type SIMD. Arithmetic on size parameter flows through return
types (e.g., concat returns SIMD[dtype, a + b]). Closest existing
example of dependent-type SIMD, relevant to Codex's type system.

**Google Highway**
https://github.com/google/highway
C++ library. Functions map closely to hardware instructions (not
high-level abstractions). Runtime dispatch selects best ISA at startup.
Supports x86 SSE through AVX-512, ARM NEON/SVE, WASM SIMD, RISC-V V.
Used in Chromium, Firefox, JPEG XL.

**C++26 std::simd (P0214/P1928)**
Voted into C++26 after ~10 years of committee work. Early
implementations show 10x compile-time overhead and cases where
std::simd runs 2.4x slower than auto-vectorized scalar loops due
to optimizer opacity through template layers. Alignment not encoded
in type. No scalable-width vector support.

## Encoding References

**VEX Prefix (AVX/AVX2)**
2-byte form: [0xC5, byte2]
3-byte form: [0xC4, byte2, byte3]
Encodes: R/X/B bits, map select (0F/0F38/0F3A), W bit, vvvv
(second source register, inverted), L (128/256), pp (66/F3/F2).

**EVEX Prefix (AVX-512)**
4-byte form: [0x62, P0, P1, P2]
Adds: z (zeroing), aaa (mask register k1-k7), b (broadcast),
LL (128/256/512), V' (extend vvvv to 5 bits for 32 registers).

**ARM64 NEON Encoding**
Advanced SIMD instructions use the same A64 32-bit encoding format.
Vector register file: V0-V31 (128-bit), aliased as Bn/Hn/Sn/Dn/Qn.
Opcode fields: size (element width), Q (64/128-bit vector).

**RISC-V V Encoding**
Uses funct3 field for vector configuration. VSET{I}VL{I} configures
SEW and LMUL. Vector arithmetic uses standard R-type with funct6
opcode field. Mask operations in v0.
