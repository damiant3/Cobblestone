# Packed vector widths

Every currently supported packed vector occupies 128 bits. `Vector 2 Real`
contains two f64 lanes; `Vector 4 (Real approximate)` contains four f32 lanes.

## Four-lane f32 contract

Arithmetic and comparisons use the ordinary operators. Comparisons produce
an inferred `VectorMask 4`; mask types have no surface declaration syntax.

| Operation | Four-lane name | Result |
|---|---|---|
| Broadcast | `vec4-splat` | Four equal f32 lanes |
| Extract | `vec4-extract` | One f32 lane |
| Mask predicates | `mask4-any`, `mask4-all`, `mask4-none` | Boolean |
| Mask population | `mask4-count` | Integer from 0 through 4 |
| Selection | `vec4-select` | Per-lane choice between two f32 vectors |
| Reduction | `vec4-reduce-add` | Left-to-right f32 sum |

The new mask consumers, selection and reduction are implemented on x86-64
and wasm. ARM64 and RISC-V explicitly refuse those names. Other plug targets
are not certified for the new family.

On x86-64, MOVMSKPS extracts four sign bits and the all-set pattern is 15.
Selection blends the full 128-bit payload, independent of lane width;
MOVUPD and the packed-double bitwise instructions preserve the f32 bit
patterns. No new register width or allocation size is required.

Mask queries and reduction add no runtime heap allocation beyond evaluation
of their operands. Selection allocates one 16-byte vector. Each consumer has
constant work for the fixed four-lane width.

`codex/test/vector-f32-mask` grades every four-bit mask, asymmetric selection
with distinct lane values, and order-sensitive f32 reduction. The existing
`vec-wide-refused` arm retains the two-lane contract of the unnumbered names.

## Remaining width work

`Vector 4 Real` and `Vector 8 (Real approximate)` need 256-bit storage and
YMM/AVX encoding, with runtime feature admission on machines lacking support.
Neither width is implemented by adding a lane-count parameter to the
128-bit emitters.

The registry keys types, allocation classes and emitters by builtin name.
Instantiation-keyed dispatch remains a separate design choice; the current
four-lane family introduces no such machinery.

Any additional width requires matching producer and consumer implementations,
explicit refusal on unsupported targets, and executed lane-sensitive tests.
Arithmetic retains the operator spelling rather than adding duplicate
`vec4-add` names.
