# Real Bitcast Intrinsics (IEEE-754 bit pattern of a Real)

**Status:** Implemented on x86-64 (val, 2026-07-03). All four intrinsics
land in NameResolver / TypeEnv / X86_64Builtins; acceptance test
`codex/test/real-bitcast.codex` passes 5/5 f64, 5/5 f32, round-trips clean.
Follow-on (ARM64/RISC-V plugs, Protobuf `fixed64`, Sparkplug float/double)
still open. See the Implementation Notes at the bottom.
**Raised by:** fester, 2026-07-03, while building Sparkplug B (`codex/foreword/encode/Sparkplug.codex`).
**Blocks:** float/double metric values in Sparkplug B; 32-bit float Modbus
register pairs; any binary wire format that carries IEEE-754 floats (Protobuf
`fixed32`/`fixed64`, MessagePack float, CBOR float, audio/image codecs that
serialize float samples).

## The Problem

To serialize a `Real` onto the wire in a binary protocol you need its raw
IEEE-754 bit pattern as an integer, so you can emit the 4 or 8 little-/big-endian
bytes. There is currently **no way to obtain those bits** at the language level.

`real-to-int` is a *value* conversion (x86-64 `cvttsd2si`): `real-to-int 1.5`
gives `1`, not the bit pattern. It truncates toward zero. It is the wrong tool —
we need a *bit reinterpret* (a bitcast), not a numeric conversion.

Concretely, Sparkplug B metrics of datatype Float (9) and Double (10) must encode
the value in Protobuf field 12 (`fixed32`, wire type 5) and field 13 (`fixed64`,
wire type 1). The Sparkplug module ships without them for exactly this reason —
see the prose note in `Sparkplug.codex` ("Float and double values ... are a later
addition"). Every other binary float sink hits the same wall.

The manual workaround — decomposing the float into sign/exponent/mantissa with
float arithmetic and reassembling the bits — is error-prone at the edges
(signed zero, denormals, infinities, NaN, round-to-nearest-even) and would
violate "correctness is absolute." This belongs in the compiler, where it is a
single machine instruction.

## Why This Is Cheap in Codegen

A `Real` (f64) already lives as its 64-bit IEEE-754 pattern; the emitter moves it
between an XMM register and a GPR with `movq` whenever it does float math (see
`movq-to-xmm` / `movq-from-xmm` in `codex/compiler/Emit/X86_64.codex`). The
bitcast is therefore *the move that is already happening*, with no numeric
conversion:

- `real-to-bits` (f64 -> its 64 bits): `movq rXX, xmmN` (XMM -> GPR). If the
  value is already materialized in a GPR, it is a no-op reinterpret.
- `real-approx-to-bits` (f32 -> its 32 bits): `movd eXX, xmmN` (32-bit move,
  zero-extended into the 64-bit GPR). If the Real is stored as f64, first
  `cvtsd2ss` to f32, then `movd`.
- Inverses for decode symmetry: `bits-to-real` (`movq xmmN, rXX`) and
  `bits-to-real-approx` (`movd xmmN, eXX`).

ARM64 and RISC-V have the equivalent GPR<->FPR move (`fmov` on AArch64;
`fmv.x.w` / `fmv.x.d` on RISC-V), so the plug backends can mirror it.

## Proposed Surface

```
real-to-bits         : Real -> Integer                 -- f64 -> 64-bit pattern
bits-to-real         : Integer -> Real                 -- 64-bit pattern -> f64
real-approx-to-bits  : Real approximate -> Integer      -- f32 -> 32-bit pattern
bits-to-real-approx  : Integer -> Real approximate      -- 32-bit pattern -> f32
```

Register the names in `NameResolver.codex` (alongside `real-to-int` /
`real-from-int`), the types in `TypeEnv.codex`, and lower them in
`X86_64Builtins.codex` (and the ARM64/RISC-V plugs) to the moves above. They are
pure, total, and effect-free.

## Acceptance Test (known IEEE-754 vectors)

A `codex/test/real-bitcast.codex` returning the count of passing checks. Ground
truth (verify independently):

| Value | `real-to-bits` (f64, hex) | `real-approx-to-bits` (f32, hex) |
|-------|---------------------------|----------------------------------|
| `0.0` | `0x0000000000000000` | `0x00000000` |
| `1.0` | `0x3FF0000000000000` | `0x3F800000` |
| `-2.0` | `0xC000000000000000` | `0xC0000000` |
| `0.5` | `0x3FE0000000000000` | `0x3F000000` |
| `42.5` | `0x4045400000000000` | `0x422A0000` |

Round-trip: `bits-to-real (real-to-bits x) ~0 x` for each, and the f32 pair via
`real-approx-to-bits` / `bits-to-real-approx`. Use `~0` (bitwise-exact), not
`==`, since `==` is a compile error on Real (CDX2085).

## Follow-on Library Work (trivial, after the builtin lands)

- `codex/foreword/encode/Protobuf.codex`: add `pb-encode-field-fixed64`
  (wire type 1; the module already has `pb-encode-field-fixed32`).
- `codex/foreword/encode/Sparkplug.codex`: add `SpbFloat (Real approximate)`
  and `SpbDouble (Real)` to `SpbValue`; encode field 12 via
  `pb-encode-field-fixed32 (real-approx-to-bits v)` and field 13 via
  `pb-encode-field-fixed64 (real-to-bits v)`. Extend `sparkplug-encode` with
  known-byte checks (e.g. Float 1.0 -> `[101, 0, 0, 128, 63]` little-endian
  after the field-12 tag).

## Implementation Notes (val, 2026-07-03)

**Representation.** The emitter carries a `Real` (f64) in a GPR as its raw
64-bit IEEE-754 pattern (that is why `emit-real-to-int` does
`movq-to-xmm 0 val.reg`), and a `Real approximate` (f32) as its 32-bit
pattern zero-extended to 64 (the `xorps` before `cvtsd2ss`+`movq` in
`to-real-approx`/`real-approx-*`; `emit-real-approx-arith` uses `addss`,
which touches only the low 32 bits, so computed f32 keeps a clean high
half). Consequences:

- `real-to-bits` / `bits-to-real` are **pure identity** — `Real` and
  `Integer` share the same GPR representation. Emitted as
  `alloc-temp` + `mov-rr` (a fresh temp is required — returning `val.reg`
  directly, as the `to-real-trapping` no-ops do, desyncs the temp
  allocator and corrupts chained expressions; that bug showed up as
  garbage sums in the acceptance test before the fix).
- `real-approx-to-bits` / `bits-to-real-approx` use `movd` through xmm0
  to guarantee a 32-bit-clean zero-extension regardless of the source's
  high half — cheap insurance against a stray high bit corrupting a wire
  format.

**Encoder bug found and fixed.** `movd-to-xmm` / `movd-from-xmm` in
`X86_64Encoder.codex` were emitting no REX prefix, so any operand in
R8–R15 (the temp allocator rotates through **R11**) or xmm8–15 was
mis-encoded as register `n & 7` — e.g. R11 read as RBX. Fixed to emit a
REX (no W) with R/B extension bits when either operand is ≥ 8; the 0–7
path is byte-identical, so no existing output changed (these encoders
had no other callers).

**Doc vector correction.** The f32 pattern for `42.5` in the acceptance
table was `0x42250000` (that value decodes to 41.25); the correct
IEEE-754 f32 of 42.5 is `0x422A0000`. Table above fixed; test uses the
corrected value.
