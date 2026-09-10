# Widening the packed vector family

*The design row COMPILER-77 defers to, written 2026-09-09 (red) off the
emitter measurements taken while landing COMPILER-77 and COMPILER-78. No code,
no seed.*

## The question is not "how many lanes", it is "how many BITS"

**Every packed vector this compiler produces is exactly 16 bytes, and that is
one XMM register.** Read from the emitters rather than assumed:

| builtin | allocation | lane arithmetic | lane width |
|---|---|---|---|
| `vec-splat` | `emit-bivy-alloc 16` | two 8-byte stores at 0 and 8 | 8 |
| `vec-extract` | none | `shl-ri 3`, `mov-load` | 8 |
| `vec-add`/`sub`/`mul`/`div` | `emit-bivy-alloc 16` | `movupd` at offset 0 | 8 |
| `vec-select` | `emit-bivy-alloc 16` | `movupd`, `andpd`, `andnpd`, `orpd` | 8 |
| `vec4-splat` | `emit-bivy-alloc 16` | `movq-to-xmm`, `shufps`, `movups-store` | 4 |
| `vec4-extract` | none | `shl-ri 2`, `mov-load-u32` | 4 |

So `Vector 2 Real` and `Vector 4 (Real approximate)` are THE SAME SIXTEEN
BYTES. The lane count is not a free parameter: it is 128 divided by the lane
width. That is why a 4-lane f32 family could be added at all, and it is why a
4-lane f64 family is a different machine rather than a bigger loop.

**The row's phrasing hides the split.** "Widening to 4 lanes" is two unrelated
pieces of work:

- **Inside 128 bits.** `Vector 4 (Real approximate)` already exists, and
  arithmetic and comparison already work on it through the OPERATORS. What is
  missing is the mask consumers and reduce. No new register width, no CPU
  feature, no allocation change.
- **Past 128 bits.** `Vector 4 Real` (four f64) or `Vector 8 (Real
  approximate)` is 256 bits, which is YMM and AVX: a wider allocation, a
  different instruction encoding with a VEX prefix, and a runtime feature
  check on a machine that can lack it.

**Do the first before deciding the second.** The first completes a family the
compiler already half-ships and cannot fail on any machine that runs the
current seed; the second changes what hardware the artifact requires.

## What four lanes already does, measured

Measured 2026-09-09 (red) against depot seed `77F6A5D09CFF6BD5`, compiled and
run one program at a time under codex-vm. **The operators are not the builtin
registry and do not go through it**: `emit-vec-op` (`Emit/X86_64.codex:2731`)
takes an f64, an integer and an f32 instruction and picks between them on
`is-f32-vector operand-ty`, so `+`, `-`, `*` and `/` on `Vector 4 (Real
approximate)` already emit packed-single arithmetic.

- `codex/test/vector-f32` PASSES at head, its `.expected` pinning `add0: 7.0`,
  `add3: 7.0`, `mul0: 12.0`. Lane 3 answering proves all four lanes are real.
- Subtract and divide answer in every lane, and `4.0 / 3.0` gives
  `1.333333373069763`, which is float32(4/3) rather than the f64 value. The
  arithmetic is genuinely single-precision, not f64 arithmetic on a 4-lane
  type.
- **Comparison at four lanes also works and produces a `VectorMask 4`.** What
  refuses is every CONSUMER of that mask: `a < b` on two 4-lane vectors
  followed by `mask-any` is CDX2001, `VectorMask 2 vs VectorMask 4`.

**So the remaining f32 work is the mask consumers and reduce, not arithmetic.**
`mask-any`, `mask-all`, `mask-none`, `mask-count`, `vec-select` and
`vec-reduce-add` are declared over two lanes in `Types/Builtins.codex`, and the
compiler already builds the wider mask that none of them accepts.

A named `vec4-add` family was built and reverted rather than landed: the
operators already carry the capability, so the named builtins would add a
second spelling of something that ships, and the registry question below is
moot for arithmetic because arithmetic never reaches the registry.

## What is hardwired to two lanes, exactly

Each of these takes an immediate that IS the lane count or a value derived
from it, so widening within 128 bits is not a matter of parameterising one
constant:

- `emit-vec-extract-builtin` passes a literal `2` to `emit-vec-lane-bounds`,
  and `emit-vec4-extract-builtin` passes `4`. The guard is already
  per-builtin; this one is ready.
- `emit-mask-extract` is `movupd-load` then **`movmskpd`**, which extracts one
  sign bit per PACKED DOUBLE and therefore yields two bits. A 4-lane f32 mask
  wants `movmskps`, four bits, a different opcode.
- `emit-mask-all-builtin` is `cmp-ri bits-reg 3`, the two-lane all-set
  pattern. Four lanes wants 15.
- `emit-mask-count-builtin` is an unrolled two-bit popcount: `shr 1`, `and 1`,
  `add`. Four lanes wants a different sequence or a real popcount.
- `emit-vec-select-builtin` blends with `andpd`, `andnpd`, `orpd`, the
  double-precision forms. The f32 forms are `andps`, `andnps`, `orps`.
- `emit-vec-arith-core` takes its op bytes as a parameter but loads and stores
  with `movupd`, so the packed-single path needs its own loader.

**The pattern in that list is the finding.** Nothing here is a lane COUNT
threaded through shared code; it is a set of double-precision opcodes chosen
per builtin. A packed-single family is a parallel set of emitters, the way
`vec4-splat` and `vec4-extract` already are, and not a generalisation of the
existing ones. Sizing this as "parameterise the lane count" would be wrong by
the shape of the work rather than by a factor.

## The registry cannot express a per-instantiation class today

`bs-alloc` and `bs-type` are keyed by builtin NAME (`Types/Builtins.codex`),
so a single `vec-add` cannot be f64-at-2-lanes and f32-at-4-lanes with
different emitters. That is the same structural gap `CostModel.md` records for
`show`, whose allocation depends on the type it is instantiated at. Two ways
out, and the choice is a ruling:

1. **A parallel `vec4-` family**, one name per operation, which is what the
   two existing `vec4-` builtins already do. Cheap, honest, and it doubles the
   registry rows for the packed family.
2. **Instantiation-keyed emitters**, where `vec-add` dispatches on its
   argument's resolved element type the way `emit-show-builtin` dispatches on
   its argument's type. Fewer names, and it needs the registry to carry more
   than one emitter per row.

Option 1 needs no compiler machinery that does not exist. Option 2 is the
better language and is a larger change to the builtin table's shape.

## What must be true before any of it lands

- **A refusal must stay a refusal.** COMPILER-77's ruling is that a `vec-`
  builtin on a width it cannot serve refuses at compile time with a named
  diagnostic, never a silent number. Adding a width must not widen the type
  and leave the emitter behind: that converts a CDX2001 into a wrong answer,
  which is the direction this whole area exists to prevent.
- **Every new width needs an arm that RUNS**, not one that compiles. The
  vector defects this tree has recorded were all runtime-visible and
  compile-clean: a lane read past its end, a mask compared against the wrong
  all-set pattern, and a comparison that answered a pointer. `codex/test`
  already carries the shape to copy in `vec-mask-hazards`, which pins the
  answers rather than the emission.
- **The mask is the half that hides.** `mask-count` and `mask-all` encode the
  lane count in a constant, so a four-lane mask read by the two-lane sequence
  answers a plausible small integer rather than failing. Any widening lands
  the mask consumers in the same CL as the producer, or not at all.

## Not proposed

Threading a lane count through the existing double-precision emitters. The
opcodes differ by element type, not by count, so a count parameter would name
the wrong axis and the code would still be wrong for f32.
