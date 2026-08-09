# The T3ISA plug: a non-binary target as an adversarial test of the plug thesis

*alpha, 2026-08-09. This is a PROPOSAL. It carries no measurements of its own
subject, because the subject does not exist yet; every number below is either
read out of a source file in this tree, or quoted from a published external
spec, and each is marked as one or the other. Nothing here has been built or
run.*

## What is being proposed

Write `codex/plugs/t3isa`: an emitter that consumes the IR text and produces
programs for **T3ISA**, a balanced-ternary instruction set defined by an
external project (`manitc`, see "The external project" below). The plug's
output runs on that project's cycle-accurate emulator, which becomes the
oracle.

The purpose is not the target. **The purpose is to falsify or establish
README claim 7** -- *"53 plugs, all building clean. A new target is a plug,
not a compiler change."*

## Why the claim is currently untested

Claim 7 rests on 53 plugs. Every one of them targets a binary machine or a
language hosted on a binary machine: 31 languages, 14 UI frameworks, three GPU
targets (PTX, SPIR-V, WGSL), four binary container formats (CDX, ELF, PE,
IMG). ARM64 and RISC-V are the two closest things to a hostile target and they
are both two's-complement byte-addressed register machines.

A claim that a boundary is clean cannot be tested by adding a 54th thing on
the same side of it. All 53 plugs share the assumptions the IR was born with,
so none of them can report on whether those assumptions exist. **We do not
currently know whether "a new target is a plug" is a property of the
architecture or an artifact of every target so far having been binary.**

T3ISA shares almost none of those assumptions:

| | Codex's existing targets | T3ISA |
|---|---|---|
| Digit | bit | trit (−1, 0, +1) |
| Word | 8/16/32/64 bits | 27 trits (~42.8 bits) |
| Negative numbers | two's complement | signed by construction, no sign bit |
| Overflow | wraps | **saturates** at ±3,812,798,742,493 |
| Memory | byte-addressed | word-addressed, 65,536 cells |
| Shifts | ×2, ÷2 | ×3, ÷3 (`TSHI`/`TSHR`) |
| Logic | two-valued | Kleene three-valued |
| Strings | in addressable memory | **not addressable** -- sidecar table |

Every place the IR quietly assumes binary should surface as a concrete
compile failure. That is the deliverable.

## What the tree already says about the answer

Enough of this is checkable by reading that the proposal should not be
launched on a guess. Read 2026-08-09:

**1. Integers are already radix-independent, and this is the load-bearing
finding.** `codex/compiler/Types/CodexType.codex:6` declares

```
IntegerTy (Integer) (Integer) (OverflowMode)
```

-- a lower bound, an upper bound, and a mode. Not `i8`/`i32`/`i64`. A range is
a fact about a number; a width is a fact about a binary machine. The bounded
integers of README claim 2 were built for safety, and it turns out they are
also what makes a non-binary port conceivable at all. Had the IR carried
widths, this proposal would be dead on arrival rather than merely hard.

**2. `OvClamping` already means what T3ISA does.**
`Types/CodexType.codex:95` gives `OvError | OvWrapping | OvClamping`. T3ISA
arithmetic saturates at the 27-trit boundary rather than wrapping (external
spec, `docs/t3isa-reference.md` §1). So `OvClamping` maps to bare T3ISA
arithmetic with no lowering at all, `OvError` maps to a compare-and-trap
sequence, and **`OvWrapping` is the interesting one**: wrapping is defined by
a binary modulus and has no natural ternary meaning. It is the first thing to
find out about.

**3. The IR operator set carries no bit-width and no bitwise operators.**
`codex/plugs/common/IRTextParser.codex:204-251` is the whole arithmetic
vocabulary: `add-int`, `div-int`, `add-num`, `add-real-approx`,
`add-real-trapping`, `add-real-saturating`, `add-vec`. No `shl`, no `shr`, no
`bitand`, no `bitor`, no `xor` anywhere in the parser. `and` and `or` at
`:240-241` are boolean. A grep of that file for `Int8|Int16|Int32|Int64|Byte|
Shl|Shr|BitAnd|Xor|size-of|align` returns **nothing**.

**4. The binarism that does exist is in floats, and it is in the type
system.** `Types/CodexType.codex:107` fixes `RealWidth = RwF64 | RwF32`, and
`:102` documents `RmDefault` as IEEE 754. IEEE 754 is a binary format by
definition -- a binary significand, a binary exponent, a sign bit. There is no
ternary reading of it. This is the sharpest obstacle in the proposal and it is
not in a backend where it could be papered over; it is in the 26-constructor
`CodexType` that the whole compiler is written against.

**5. Bytes live at the container layer, which T3ISA does not use.**
`codex/plugs/common/ByteHelpers.codex` is 68 lines and is consumed by the
ELF/PE/IMG plugs. T3ISA's artifact is a word file (`.t3b`) plus a string
sidecar (`.t3d`), so the T3ISA plug bypasses that layer entirely rather than
fighting it.

So the honest prior is: **the arithmetic and integer core looks portable, and
floats, vectors and booleans look like they will fail.** That is a good shape
for an experiment. A probe expected to pass everywhere teaches nothing.

## The four expected failures, named in advance

Named now so that finding them counts as confirmation rather than discovery
after the fact, and so a lane that hits one knows it was anticipated.

| # | Expected failure | Why | If confirmed |
|---|---|---|---|
| F1 | `RealTy RwF64/RwF32` has no ternary lowering | IEEE 754 is binary by construction | Report it. Do **not** attempt to widen `RealWidth` -- see "What this must not do" |
| F2 | `OvWrapping` has no ternary meaning | wrapping is defined by a binary modulus | Plug rejects with a named diagnostic |
| F3 | `VectorTy (Integer)` / `VectorMaskTy` have no T3ISA form | no SIMD in the ISA | Scalarize, or reject; decide by what the gate program needs |
| F4 | Integer ranges wider than ±3,812,798,742,493 need multi-word lowering | T3ISA word is ~42 bits, narrower than i64 | Reject in v1; multi-word is a second campaign, not this one |

F4 deserves emphasis because it is the one that bites silently. Any Codex
integer declared over the full i64 range does not fit a T3ISA word. Rejecting
loudly in v1 is correct; **a plug that silently truncates is worse than no
plug**, because it would make the gate green for the wrong reason.

## The gate

One command, one falsifiable outcome, in the tree's usual shape:

> Compile a small Codex program to T3ISA through the plug, run it under the
> external emulator, and get **byte-identical stdout** to the same program
> compiled and run through an existing path.

The program must be small, and this is a hard constraint rather than a
convenience. T3ISA has 65,536 words of address space with the heap based
around 50,000 (external spec, §3) -- about 350 kilobytes' worth of
information, total, for code and data together. Nothing resembling `deskboot`
or the compiler will fit. The gate program should be arithmetic and
control-flow over bounded integers, with output through the emulator's
`print_int` / `print_str` / `print_newline` syscalls (external spec, §8).

**Ternary-specific behaviour is not in scope for the gate.** The gate asks
whether the IR can cross a non-binary boundary at all. Programs that exploit
trits, three-way branching or Kleene logic are the *second* experiment, and
they need IR vocabulary that does not exist yet (see "Kleene logic" below).

## Cost

By comparison with the two closest existing plugs, both read 2026-08-09:

| plug | plug entry | emitter | total |
|---|---|---|---|
| ptx | `PtxPlug.codex`, 19 lines | `PtxEmitter.codex`, 1,173 | 1,192 |
| riscv | `RiscVPlug.codex`, 140 lines | seven chapters | 7,976 |

T3ISA is a much simpler target than RISC-V: 24 standard-encoding opcodes plus
five wide-immediate forms, one addressing mode, 27 registers with a fixed
calling convention, and no ELF -- the artifact is a flat word file. It is also
harder than PTX, which emits text and delegates register allocation. **The
estimate is 1,500-2,500 lines**, in PTX's shape:

```
T3IsaPlug.codex      ~20 lines   read-serial-cce -> parse-ir-chapter
                                 -> emit-t3isa-chapter -> print-line-uni
T3IsaEmitter.codex   ~1,200      IR -> T3ISA assembly
T3IsaEncode.codex    ~400        assembly -> 27-trit words, .t3b + .t3d
```

plus `build.ps1` calling `Build-TranspilerPlug` and a `run.ps1` following
`codex/plugs/ptx/run.ps1` -- the `-IrCce` compile, the CCE mode header, the
serial handoff. **This is an estimate from two comparables, not a measurement,
and it should be replaced by the real count the moment there is one.**

Step zero is toolchain, and it is not free: the external emulator is a Rust
program, and `cargo`, `rustc`, `clang` and `clang-19` are all absent from this
machine (checked 2026-08-09). Without them there is no oracle and therefore no
gate.

## What this must not do

**No compiler change.** That is the whole point. If landing T3ISA requires
touching `CodexType`, the IR text, or the emitter, **claim 7 is falsified for
this target and the correct output of this work is that finding, written
down** -- not a compiler change that rescues the schedule at the cost of the
result. Specifically: do not widen `RealWidth`, do not add a trit primitive to
`CodexType`, do not add IR operators. If F1 blocks the gate, the gate is
restated as integers-only and the float failure is reported as the measured
edge of the claim.

A negative result here is publishable and worth as much as a positive one. A
list of the exact places the IR assumes binary is a middle-end finding
available no other way, and it matters long before anyone has ternary
hardware. **The failure mode to guard against is a green gate obtained by
quietly moving the boundary** -- which is the same failure `IRTypeEmission.md`
records in its self-test that passed under sabotage.

## Kleene logic: the second experiment, deliberately not this one

`CodexType` has `BooleanTy`, two-valued. T3ISA's native logic is Kleene
three-valued (`True`/`Unknown`/`False`, with `TMIN`/`TMAX` as and/or). Codex
booleans map onto a two-value subset of a trit without difficulty; the reverse
does not map at all, because there is no IR type that can hold `Unknown`.

That gap is worth a separate probe against the proof system rather than the
plug. Codex has dependent types with structural induction and per-constructor
subgoals; three-valued logic is a small, sharp test bed with a built-in
negative:

- `tnot (tnot t) === t` over a three-constructor type is **true**, and is the
  direct analogue of the `reverse (reverse xs) === xs` flagship.
- `t tor (tnot t) === True` is **false** -- excluded middle fails at
  `Unknown`.

A proof system that proves the first and *refuses* the second is
demonstrating something real about the unifier, and the second is exactly the
"write the program that should be rejected" method claim 2 was built by. This
needs no plug, no emulator and no Rust toolchain, so it can run in parallel
with, or instead of, the plug work.

## The external project

`manitc` is a balanced-ternary systems language and compiler in ~20,100 lines
of Rust, sole author Manish Jagdish Thatte, with two backends (LLVM IR and
T3ISA), an assembler, a cycle-accurate emulator, a debugger and a profiler.
T3ISA is fully specified in that project's `docs/t3isa-reference.md`:
architecture, register file, memory model, word encoding, instruction set,
assembly syntax, calling convention, syscall table, binary format, emulator
behaviour.

It is the mirror image of this project on the bootstrap axis. It is radical
about what a machine computes with and entirely conventional about what it
stands on -- it needs Rust, LLVM, clang-19 and a host OS, and its "ternary
machine" is an emulator inside a hosted binary. Codex is the reverse. Neither
project has the other's axis, which is why the exchange is worth anything.

### Sourcing -- settle before writing a line

**The plug is to be written from the published T3ISA specification**
(`docs/t3isa-reference.md` in that project), and from nothing else. The
document is complete enough to work from on its own: encoding, opcodes,
addressing, calling convention and the syscall table are all specified in it.
Working from the specification rather than from another implementation is
also simply the better engineering -- it is what tests whether the
specification is sufficient, which is itself worth knowing.

The emulator is used as a **black-box oracle**, invoked as a separate process
on its own published command line.

`manitc` is released under the AGPL-3.0. This document does not attempt to
work out what that implies for us. **Damian's call before any code is
written.**

## What is being asked for

1. A ruling on the sourcing question above.
2. Whether the T3ISA plug is worth a lane at ~1,500-2,500 lines plus toolchain
   setup, given that the most likely outcome is a *partial* pass -- integers
   and control flow through, floats and vectors rejected.
3. Whether the Kleene proof probe should go first. It is far cheaper, needs no
   external toolchain, tests a different claim (2 and 3 rather than 7), and
   its result is not contingent on the sourcing question.

Recommendation: **the Kleene probe first**, because it is unblocked today and
this proposal is not; the plug second, once the sourcing question is settled.
