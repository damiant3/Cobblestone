# The T3ISA plug: a non-binary target as an adversarial test of the plug thesis

*CLOSED 2026-08-11. The experiment ran, the question it was written to answer
is answered, and this document is the account. The plug is built, gated and
landed in `codex/plugs/t3isa`; the Kleene proof probe is in the BVT. Read
"State" for what holds and "What is left, and why it is not being done now"
for the follow-ons that were deliberately not taken.*

*This header said "the PLUG is still a proposal and nothing of it has been
built or run" until the day it was closed, two days after step two shipped a
green gate. It is left visible rather than quietly overwritten because it is
the ordinary way a live design goes wrong: the sections below were kept
current every step, and the one line nobody re-reads was not.*

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
convenience. T3ISA has 65,536 words of address space, with the reference
emulator putting the stack's start at 60,000 and its own heap at 63,000 (spec
v1.3, §3; the revision this section was written against said 65,535 and "about
50,000") -- a few hundred kilobytes' worth of information, total, for code and
data together. Nothing resembling `deskboot`
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

**Step zero is DONE 2026-08-09: the toolchain is installed and the oracle
runs.** It needed less than this section assumed. `manitc` has no `llvm-sys`
dependency -- `clap`, `tower-lsp`, `tokio`, `serde_json`, all pure Rust -- so
its LLVM backend emits IR as text and neither LLVM nor clang is required for
our purpose. Only Rust was missing; MSVC 14.44 and the Windows 10 SDK were
already on the machine.

Everything lives in `D:\Toolchain-Ternary` so it can be quarantined by
deleting one folder: `RUSTUP_HOME`, `CARGO_HOME` and `CARGO_TARGET_DIR` all
point inside it and rustup was run with `--no-modify-path`, so there is no
`.cargo` or `.rustup` in the user profile and the user PATH is untouched.
Rust 1.97.1, 603 MB. `rustup-init.exe` matched its published SHA-256, which
proves the download was not corrupted and nothing more, both being served by
the same host.

**Building the oracle on `x86_64-pc-windows-msvc` needs one extra link flag.**
The MSVC CRT exports `_isatty` where the link expects `isatty`, so every
dependency compiles and the final link stops at `LNK2019: unresolved external
symbol isatty`. The name is reached only from the tty check that decides
whether to colour error output, so nothing about compiler behaviour depends on
it. **Build it with**

```
RUSTFLAGS=-Clink-arg=/ALTERNATENAME:isatty=_isatty
```

which aliases the undefined name onto the CRT's at link time. **Build the
oracle unmodified.** It is our reference, and a locally modified reference is a
weaker instrument, so the adjustment belongs in the link flags and not in the
source. The `x86_64-pc-windows-gnu` toolchain is not the way around it either
-- it fails separately, because rustup's mingw component ships no
`dlltool.exe`.

**Oracle verified end to end.** `manitc compile <f>.mt --target t3 -o <f>.t3b`
then `manitc run-t3 <f>.t3b`. On `benchmarks/01_arithmetic.mt`: `fib(20) =
6765`, `fib_iter(50) = 12586269025`, longest Collatz under 1000 at `n=871
len=178`, all correct, from a 553-word `.t3b` with a 204-byte `.t3d` string
sidecar. That run ends `TRAP: step limit exceeded`, which is their benchmark
outrunning the emulator's step budget and is a bound the gate program must
stay under.

## Step one, DONE 2026-08-09: the back end is specified and proven

The plug's back half (`T3IsaEncode`, assembly to 27-trit words) is no longer an
estimate against a spec. It is a measured encoding, implemented as a prototype
assembler and proven byte-for-byte against the oracle's own output.

**The numeric opcode values are not in the published reference, so they were
measured.** `docs/t3isa-reference.md` section 4 gives the field layout and the
two formulas, `word = opcode * 3^18 + r1 * 3^13 + r2 * 3^8 + r3 * 3^3 + imm`
and the wide form, and section 5 names all 29 mnemonics.
`compiler-internals.md:965` records that the `Opcode` enum is `repr(i64)` with
each variant equal to its opcode number, which points at the source rather than
stating the values. Working from the reference alone therefore stops at the
first word, and the remaining numbers had to come from somewhere else.

They were recovered by arithmetic, without reading `src/`. `manitc compile
--target t3` drops a `.t3s` assembly listing beside the `.t3b`, so every
program is a set of matched (assembly, word) pairs. Decoding a word into
balanced-ternary fields and reading the top field off against the mnemonic on
the same line gives the opcode. Across 14 example programs, **22,180
instructions aligned with zero conflicts** -- no two programs disagree about
any mnemonic's number, and every program's instruction stream consumed its
code words exactly.

| op | mnemonic | op | mnemonic | op | mnemonic |
|---|---|---|---|---|---|
| 1 | `TADD` | 12 | `TMIN` | 21 | `CALL` |
| 2 | `TSUB` | 13 | `TMAX` | 22 | `RET` |
| 3 | `TMUL` | 14 | `TCMP` | 24 | `SYSCALL` |
| 4 | `TDIV` | 15 | `LOAD` | 25 | `TBR_POS` |
| 5 | `TMOD` | 16 | `STORE` | 26 | `TBR_ZERO` |
| 6 | `TNEG` | 17 | `TLIT` | 28 | `CALLR` |
| 10 | `TSHI` | 18 | `MOV` | | |
| 11 | `TSHR` | 20 | `JUMP` | | |

**Six mnemonics are not in that table and could not be derived**, because none
of them occurs anywhere in the corpus: `TAND`, `TOR`, `TNOT`, `TBR_NEG`,
`HALT`, `NOP`, leaving 0, 7, 8, 9, 19, 23 and 27 unclaimed. It would have been easy to
guess -- the spec's Logic table runs `TAND, TOR, TNOT, TSHI, TSHR, TMIN,
TMAX`, and with `TSHI` at 10 and `TMIN` at 12 the numbers 7, 8, 9 fall out for
the first three. **That guess is not in the table and must not be made**, on
the evidence of the one guess this work did make and got wrong: `CALLR` looked
like it belonged at 23 between `RET` and `SYSCALL`, and it measured 28.

**The plug does not need any of the six.** The spec's own definitions make
three of them redundant -- `TAND` is `min`, `TOR` is `max`, `TNOT` is negate,
which are exactly `TMIN`, `TMAX` and `TNEG`. Termination needs no `HALT`: a
compiled `main` ends in `RET` and the emulator stops cleanly on it, measured on
`hello` with exit code 0.

**Field placement, measured, not assumed.** A three-address op puts the third
operand in `r3` with `imm` zero, or in `imm` with `r3` zero when it is an
immediate -- `TSUB Rd, Ra, Rb` and `TSUB Rd, Ra, #k` are the same opcode.
`LOAD Rd, [Ra+#k]` is `r1=d, r2=a, imm=k`; `STORE Rs, [Ra+#k]` the same shape.
`MOV`/`TNEG` use `r1, r2`. `TLIT`, `JUMP`, `CALL`, `SYSCALL`, `TBR_POS` and
`TBR_ZERO` take the wide form. `TBRANCH` expands to exactly three words,
`TBR_POS`/`TBR_ZERO`/`JUMP`, confirmed by the alignment holding over 22,180
instructions. A label's address is the word index of the instruction after it.

**Two further encoding details, both established by measurement after the
artifact refused to match.** String literal addresses are `code_size + 1024 + i`
as section 3 states, and `i` is the label's position in **ordinal lexicographic
order of the label name** -- `str0, str1, str10, str11, ..., str2, str20` --
rather than declaration order, which is the shape a sorted map produces. And
the `.t3d` sidecar carries `"` and `\` raw where the `.t3s` literal escapes
them `\"` and `\\`, while `\n` stays escaped as section 9 says.

**The proof.** Four scripts under `codex/plugs/t3isa/spec/`:
`build-corpus.ps1` compiles the example corpus, `derive-opcodes.ps1` recovers
the table above, `t3isa-assembler.ps1` assembles a `.t3s` to words and a
sidecar, and `validate.ps1` requires the result to equal manitc's `.t3b` and
`.t3d`.
**All 14 programs byte-identical, 22,772 words, sidecars identical as sets.**

**`derive-opcodes.ps1` is superseded as a tool and kept as evidence.** It
existed because section 4 of the reference stated both encoding formulas and no
numeric opcode value, so the table had to be recovered by arithmetic from
emitted `.t3s`/`.t3b` pairs. v1.3 publishes all 36 values, so nothing needs
deriving again. Do not delete it: it is the executable record that we recovered
those numbers by measurement rather than by reading their `src/`, which is the
cleanroom claim's evidence, and evidence is not obsoleted by the answer being
published later.

**It also earned a confirmation it could not have had before.** Checked
2026-08-10, every opcode we derived independently against the table v1.3 now
publishes: **22 of 22 agree in the PowerShell assembler and 22 of 22 in the
Codex encoder, zero mismatches**, and `TBRANCH`'s expansion to
`TBR_POS`/`TBR_ZERO`/`JUMP` matches 25/26/20 as published. That is two
independent derivations of the same table agreeing, which is worth more than
either alone. The check is cheap enough to redo on any future spec bump, and
the first pass of it produced a false mismatch worth remembering: a regex for
`m == "X" then N` over the whole encoder catches `t3e-width`'s
`if m == "TBRANCH" then 3`, which is a word count and not an opcode. Scope the
match to `t3e-opcode`'s body.

The instrument is not one that cannot fail (L-FALSIF). It was red at every
stage of its construction, and `sabotage.ps1` mutates one part of the encoding
at a time in a copy of the library and reruns the whole validation. All four
arms fire, and each takes down exactly the programs it should:

| arm | takes | why that number |
|---|---|---|
| `TADD` renumbered 1 to 7 | 14 of 14 | every program does arithmetic |
| `CALLR` renumbered 28 to 23 | 1 of 14 | one program makes an indirect call, and the derivation counts one `CALLR` in the corpus |
| string table in declaration order | 11 of 14 | the three survivors have fewer than ten string literals, where lexicographic and declaration order agree |
| `TBRANCH` counted as one word | 13 of 14 | `stream_demo` has no `TBRANCH` |

The survivors are the point. A sabotage that took all 14 every time would be
consistent with an arm that simply breaks the run, and the two arms with
predicted, named survivors are what show each is breaking the specific thing
it claims to break rather than the harness.

One caution on what this proves. Byte-identical output against the reference
implementation says the **encoder** is right. It says nothing about whether
Codex IR can be lowered to this machine, which is the actual claim under test
and is entirely in the emitter half.

### Target behaviour the plug is built on

Measured against the oracle, and listed here because the emitter and the
encoder each depend on one of them being true. Anything in this list that
changed under us would break the plug quietly rather than loudly, so it is the
list to re-measure first if the gate ever goes red for no visible reason.

**Re-measured 2026-08-10 against spec v1.3 and an oracle built from it.** Four
of the seven moved. That is the list doing its job: it was written down
precisely so a change underneath it would be caught by re-reading rather than
by a mysterious red gate, and the re-measurement is what caught them.

- **`TCMP` is three-operand.** `TCMP Rd, Ra, Rb` writes the sign of `Ra - Rb`
  into `Rd`, and all 214 occurrences in the corpus take that form. The whole
  comparison lowering rests on it: each Codex relation is a small arithmetic
  recovery from that sign rather than a branch. **Unchanged, and normative in
  v1.3** rather than contradicted by the document as it was before.
- **`TSHI` and `TSHR` accept a register OR an immediate.** MOVED. v1.3 states
  the rule that makes both work, `rhs = regs[r3] + imm`, so `r3 = 0` with an
  immediate and `imm = 0` with a register are the same instruction. Measured
  2026-08-09 the register form answered 1 instead of 1594323, a silent
  multiply by 3^0; re-measured 2026-08-10 it answers 1594323, the same as the
  immediate form. Both guards we carried against it are removed, since they
  would now reject a conforming program. The
  materialiser still emits the immediate form, which costs one instruction
  fewer.
- **`TSHR` rounds to nearest, it does not truncate.** MOVED, and it is the one
  that would have bitten silently. `5 / 3^1` answers 2 and `8 / 3^1` answers 3;
  truncation would give 1 and 2. v1.3 corrected the table to say so. Nothing in
  the plug emits `TSHR`, which is the only reason this was never a bug: the
  constant materialiser needs multiply, not divide.
- **`TMOD` is the truncating remainder**, taking the sign of the dividend:
  `-7 mod 3` is -1, `7 mod -3` is 1, `-7 mod -3` is -1. That is Codex
  `int-rem` exactly and is NOT `int-mod`, which is synthesised from it. Picking
  wrong here is silent for positive operands. **Unchanged, and now documented.**
- **Division by zero traps and halts**, exit 70. Confirmed. It is what the
  runtime bounds check, the match fall-through and the allocator's
  out-of-memory arm are built out of, and all three still behave as designed.
- **An unknown syscall now DOES halt.** MOVED. It printed a trap line and let
  execution continue; it now traps, halts and exits 70. Nothing in the plug
  depends on either behaviour, but the old note said this was the one fault
  shape that could not be used as a fault, and that is no longer true.
- **Syscalls 140 and 142 never existed.** MOVED. The real `fmt::show_int` is
  syscall 14 and it WORKS: 42 in R1, syscall 14, syscall 3 prints "42".
  `fmt::concat` is genuinely unavailable, being one of the 22 of 31 `fmt`
  natives the T3 backend does not implement, and it fails at assemble time
  rather than at run time. So text append is still impossible, but `show` is
  now a backend choice rather than a machine limit: what is missing is a Text
  representation in the emitter to carry the handle. See "What v1.3 opens up".
- **Exit codes now DO report faults.** MOVED. A trapped program exits 70 rather
  than 0. `run.ps1` and the gate still scan stdout for `TRAP:`, which is
  correct either way and costs nothing; `build-corpus.ps1` still checks for the
  artifact. Trusting the old exit code once cost a phantom corpus entry that
  inflated every sabotage arm by one, so the scan stays.

Fourteen of the twenty example programs compile and form the corpus, which is
the bound on any future cross-validation run.

## Step two, DONE 2026-08-09: the gate passes, and claim 7 holds

**The plug is written and the gate is green. No compiler change was needed.**
`codex/plugs/t3isa/` is 876 lines of Codex (`T3IsaEmitter` 860, `T3IsaPlug`
16) plus a runner and a gate script. Nothing under `codex/compiler/` was
touched, no IR operator was added, `CodexType` was not widened, and
`RealWidth` was left alone.

The gate, `codex/plugs/t3isa/gate.ps1`, compiles one program two ways and
requires identical stdout:

```
--- x86-64 (codex-vm) ---            --- T3ISA (external emulator) ---
fib 20 = 6765                        fib 20 = 6765
gcd 1071 462 = 21                    gcd 1071 462 = 21
collatz 27 = 111                     collatz 27 = 111
```

Recursion, three-way branching, Euclidean and truncating remainder,
saturating arithmetic and formatted output, from Codex source through the
Codex IR to 388 balanced-ternary words. **A new target was a plug.**

The comparison has a negative control in the same script (`-Sabotage`):
rewriting one `TADD` to `TSUB` in the emitted assembly and reassembling
gives `fib 20 = 1`, `gcd 1071 462 = 1071`, `collatz 27 = -4`. Plausible,
wrong, and caught. A gate whose control has never fired would not be
evidence that the two paths are being compared at all.

### The four predicted failures all fire, and they are named

`codex/plugs/t3isa/test/refusals.codex` exercises them in one program and
the plug refuses it, with these reasons:

| # | predicted | what the plug says |
|---|---|---|
| F1 | reals have no ternary lowering | `real arithmetic: IEEE 754 is a binary format by construction and has no ternary form` |
| F2 | `OvWrapping` has no ternary meaning | `wrapping overflow has no ternary meaning` |
| F3 | vectors have no T3ISA form | `vector arithmetic: T3ISA has no SIMD` |
| F4 | bands wider than the word | `integer band wider than a 27-trit word` |

F4 is the one that shapes ordinary code: a bare `Integer` is the full
64-bit range and does not fit, so every band in the gate program is
declared. That is the predicted bite and it is real rather than
theoretical.

**A refusal is a hard failure of the run, not a comment.** Every one is a
marker in the assembly and an entry in the emitter's context, and `run.ps1`
exits non-zero on any of them. `OvClamping` needs no lowering at all, as
predicted: T3ISA saturates natively.

### What the experiment found that was NOT predicted

**1. `__narrow` does not carry its band on the node, and a consumer that
reads only the node will get this wrong.** The emitted form is

```
(apply (name "__narrow" (fn int-default int-default)) <arg> int-default)
```

typed default to default whatever band was asserted.

**This section said the band was ABSENT FROM THE IR and that a consumer
sees "an assertion with no subject". That was wrong, it was published in
main 14418, and it is corrected here.** The band is in the IR; it is held
by the enclosing context, and which context depends on the position.
Measured across three of them:

| position | where the band is |
|---|---|
| a def's return | the def's own type, `(fn int-default (int 0 100 ov-error))` |
| a call argument | the callee's type, `(name "takes-small" (fn (int 0 100 ov-error) ...))` |
| a record field | the field's declaration in the type-defs, `(rec-field "val" (a-bounded (a-named "Integer") 0 100 ov-error))` |

The instrument that produced the wrong claim was a look at the `__narrow`
node, which structurally cannot show a band the context holds. It is the
failure this project already documents in the other direction: ask whether
the measurement could have shown the opposite before publishing it.

**So the plug emits the real check**, taking the band from the position:
argument narrows read the callee's parameter type, return narrows read the
def's. A narrow anywhere else is refused rather than approximated, because
a word-range test standing in for `0 and 100` would pass two million and
read as an enforced bound.

`test/narrow-trap.codex` is the proof that the check is real in both
directions, since a bounds check nothing has seen fire is not a bounds
check. One value inside its band and one outside, and **both targets agree**:

```
x86-64            in band: 50 / about to narrow 2000 into 0..100 / !EXC=06 (UD2), RDI=0x7d0
T3ISA             in band: 50 / about to narrow 2000 into 0..100 / must not print: TRAP: division by zero
```

Re-measured 2026-08-10 against the v1.3 oracle, unchanged except that the trap
now sets exit 70 where it used to exit 0. **The `must not print: ` prefix on
the T3ISA line is not a leak and this transcript used to elide it.** A print is
linearised into one syscall per leaf on this target, so the literal goes out
before the value beside it is computed and traps; x86-64 evaluates the whole
argument first and prints nothing. The guarded value never prints on either.

Same point, same value, different fault mechanism because the machines
differ. `scale` exists in that program so the static bounds prover cannot
see the value: a literal out of range is CDX2050 at compile time and would
never reach the emitted check.

What survives of the original finding is smaller and still worth having:
the band is recoverable but only non-locally, every plug lane must know to
walk the context to find it, and nothing tells a backend author that. This
plug had it wrong for a full cycle.

**That last clause is no longer true, as of step four.** `DevelopersGuide.md`
now carries the band table and the field-index asymmetry beside it, under
"Reading the IR". Read it there rather than quoting this paragraph.

**2. There are no first-class Text values on this target, and print has to
linearise.** Strings are not addressable memory, and the two syscalls that
would build text at runtime, `fmt::show_int` (140) and
`fmt::concat` (142), **answer `TRAP: unknown syscall`**. So `show` cannot
produce a value and `&` cannot join one. What can be done is emit a line:
`print-line-uni` walks its argument's append tree and emits one syscall per
leaf, `print_str` for a literal and `print_int` for an integer, with the
newline once at the end. That is the general lowering of formatted output
for a machine with no string heap. `show` or `&` anywhere else is refused
rather than faked.

**3. `TLIT` reaches only plus or minus 797,161, and an over-range immediate
WRAPS rather than failing.** The wide field is 13 trits against the word's
27, and the encoding is a `rem_euclid`. The emitter materialises anything
larger from balanced base-3^13 digits with `TSHI`. This was caught by
adding a guard to the assembler in `spec/`, which then refused the
emitter's attempt to `TLIT` a bound of 1,000,000 -- an instrument built for
one job finding a silent-truncation bug in another.

**4. Division by zero TRAPS and stops**, measured. The plug uses a deliberate
divide by zero as the only fault this machine is known to raise. **Measured
behaviour governs on this target wherever the two could differ**, which is the
standing rule the rest of these items are instances of.

**5. `TSHI` and `TSHR` take an IMMEDIATE shift amount, and the register form
is a silent identity.** Measured: `TSHI R11, R11, #13` answers 1594323, while
`TLIT R21, #13` then `TSHI R11, R11, R21` answers **1**. The emulator reads
the imm field and ignores `r3`, so the register form is a multiply by 3^0 that
hands the operand straight back. It does not fault.

This is the worst-shaped of the discrepancies because nothing announces it,
and **the byte-identical proof could not have caught it**: `TSHI` occurs
exactly once in the 22,180-instruction corpus and `TSHR` once, both in the
immediate form, so reproducing the corpus byte-for-byte exercised the
encoding and never the operand shape. A count of one is a coverage hole
wearing the costume of a proof. What caught it was the gate going red on a
constant that came out as -594322 instead of 1000000.

The assembler now refuses a register third operand on either mnemonic, for
the same reason it refuses an over-range wide immediate: the layer that
would silently normalise is the layer that should refuse.

**6. `TMOD` is the TRUNCATING remainder.** Measured on all three sign
combinations: `-7 mod 3` is -1, `7 mod -3` is 1, `-7 mod -3` is -1. That is
Codex `int-rem` exactly and it is NOT `int-mod`. Codex has both and they are
not distinguishable by name, so which one this is had to be measured; the
Euclidean one is synthesised from it. Picking wrong here is silent for
positive operands, which is how it would have shipped.

### Cost, measured

**876 lines of Codex**, against the estimate of 1,500-2,500 from the PTX
and RISC-V comparables. The estimate was high because the encoder half
turned out to be a PowerShell script proven in step one rather than a
Codex chapter, and because a stack discipline replaced a register
allocator: every expression leaves exactly one word on the stack, so a
threaded `sp-delta` addresses locals correctly under arbitrary nesting on
a machine where all 27 registers are caller-save. That is slower code than
a register allocator would give and it is not what this experiment was
measuring.

### What v1 does not do

Match, records, lists, lambdas, closures, indirect calls, effect handlers
beyond a plain `act` block, reals and vectors. Each is refused by name.
(The encoder port is done; see "Step three". Records, variants and match
landed in step four.)

## Step three, DONE 2026-08-09: the encoder is Codex, and the plug owns the artifact

`T3IsaEncode.codex`, 495 lines, assembles T3ISA assembly text into words and
the string sidecar. **`run.ps1` no longer calls the PowerShell assembler**;
it makes a second plug invocation in the new `T3-ASM` mode, so the artifact
is produced by Codex end to end. `spec/t3isa-assembler.ps1` stays as the
specification and the proof, out of the path.

**Held to the same corpus, and it passes it: 14 of 14 byte-identical, 22,772
words**, via `spec/validate-codex-encoder.ps1`. The `T3-ASM` mode exists for
exactly this. Reaching the encoder only through the emitter would make it an
instrument reachable only through the thing it measures, which is the shape
this project already has a lesson about.

The plug is now **1,432 lines of Codex** (emitter 911, encoder 495, entry
26), against the original 1,500-2,500 estimate from the PTX and RISC-V
comparables. That estimate is finally a fair comparison, since both halves
are Codex now, and it was close.

### What the port found

**A qualified label needs the LAST colon of its run, not the first.**
`Process::new:` is one label. Taking the first colon splits it into three
bogus ones (`Process`, empty, `new`), the real name never gets registered,
and every `CALL Process::new` resolves to nothing. Three of the fourteen
programs failed on it. The PowerShell version was right by accident of
regex greediness (`[A-Za-z0-9_.:]*` then `:`); reimplementing the scan by
hand is what exposed that the rule had never been stated.

Two harness defects worth recording because both read as encoder bugs:

- **The plug's first output line carries the serial framing's control
  bytes.** A reader that does not strip them loses that line, and the
  symptom is the encoder appearing to drop its first instruction, in every
  program, by exactly one word. `run.ps1` already stripped them for the
  emitter; the new validation script did not. It cost a debugging cycle and
  several wrong hypotheses about `list-push`, and what settled it was
  emitting the word COUNT alongside the words and seeing the count was
  right.
- **The ASCII-to-CCE conversion in the harness is Latin-1 only, and an
  unmappable character became a zero byte.** `read-serial-cce` is
  null-terminated, so that truncated the input and corrupted everything
  after it. Their listings contain an arrow and a micro sign. Unmappable
  characters are now dropped rather than zeroed, and a program whose string
  bodies are not ASCII has its SIDECAR comparison skipped and says so;
  its words are still fully checked. Six of the fourteen compare sidecars,
  and the tool prints which.

Neither was a defect in the emitted artifact, and both are the same class:
a hand-written check disagreeing with the harness it was modelled on.

## Step four, DONE 2026-08-10: aggregates cross too, and still no compiler change

Step two proved that arithmetic, control flow and formatted output cross a
non-binary boundary. It did not touch the part most likely to break, which is
DATA: a record on a machine with no bytes, and a variant on a machine whose
word is 27 trits. That is what this step adds, and the answer is the same one.
**Nothing under `codex/compiler/` was touched.**

`codex/plugs/t3isa/test/aggregates.codex` is the second gate program, and both
paths produce the same eleven lines:

```
point = 3 7      dot = 0            or F U = 1
manhattan = 10   line 11 = 11       or U T = 2
span = 8         rect 3 4 = 12      or U U = 1
                 rect 5 5 = 100     or F F = 0
```

947 balanced-ternary words. It covers a record, a record NESTED in a record,
a variant of three nullary constructors, a variant carrying payloads of one
and two fields, a match binding those payloads, and field access through a
value returned from a call.

**The layout.** A record is its fields in declared order, one word each. A
variant is a tag word followed by the constructor's fields, the tag being the
constructor's position in the type's own list. That is the same convention the
RISC-V plug uses, arrived at independently because the machine leaves little
choice: one word per field is what a word-addressed machine offers.

**The allocator is a bump pointer in a fixed memory cell**, not a register,
because every register on this machine is caller-save and a pointer held
across a call would be lost. Base 32,768, ceiling 49,152, the pointer itself
at 32,767. Those addresses sit in the free run between the string
pseudo-addresses just past the code and the emulator's own object allocator at
about 50,000, and that the run is usable was MEASURED before anything was
built on it: words at 32,768, 32,771, 40,000 and 49,151 all store and load
back through both the direct and the offset form. Exhaustion traps rather than
running on, because the heap grows toward the stack and an unguarded bump
would answer with corrupted data instead of failing.

### The controls, and what they corrected

`test/sabotage-aggregates.ps1` mutates one layout decision at a time in the
emitter, rebuilds the plug against each, and reports which output rows move.
Four arms, all four fire, and the emitter restores to baseline afterwards.
The arms are shaped so their SURVIVORS carry the information:

| arm | moves | why that set |
|---|---|---|
| record fields stored by written position | 1 of 11 | only `point`, which is built with its fields in the opposite order to their declaration. `manhattan` survives because addition commutes and `span` because `Segment` IS written in declaration order |
| variant tag read from offset one | 7 of 11 | see below |
| constructor payload read from the tag slot | 3 of 11 | exactly the constructors carrying a payload; the four `Tri` rows and `dot` have none to misread |
| heap ceiling lowered to the heap base | 11 of 11 | the first allocation is already past the ceiling, so the guard traps before the first line |

**Two of the four predictions were wrong, and both corrections are worth more
than the passes.**

The tag arm was predicted to take 8 rows and took 7. `dot` survives, and the
reason is a coincidence rather than a miss: reading one word PAST a nullary
object finds unwritten heap, which is zero, and `Dot`'s tag is also zero, so
the wrong read lands on the right answer. It is a small instance of the thing
this whole design is about, a check passing for a reason unrelated to the
thing it claims to check. The rows after `line 11` are missing rather than
wrong because the match's fall-through TRAPPED, which is the only sighting so
far of that backstop doing its job.

The payload arm was predicted to take 2 and took 3. The prediction said
`rect 5 5` would survive arithmetically, both operands becoming the same tag
and the equal-sides arm still answering 100. That was simply wrong: each bound
name shifts down by one INDEPENDENTLY, so `w` reads the tag slot and `h` reads
the first field, giving 2 and 5 rather than two equal values, and the answer
is 10. There is no arithmetic survivor. The predictions in the script have
been corrected to what was measured; leaving the original ones would have left
a confident claim in the tree that nothing re-evaluates.

### What the experiment found that was not predicted

**The IR hands a backend the resolved field index at the READ and not at the
WRITE.** A `field-access` node names its field `px/0`, name and declared index
joined by a slash. The `field-val` nodes of a record literal name theirs
plainly, `px`, in the order the literal wrote them. So the two halves of the
same record are described differently, and a backend that assumes either
convention holds on both sides is wrong on one of them. The plug uses the
suffix where it is given and CROSS-CHECKS it against the type-definition
lookup rather than treating that lookup as a mere fallback, on the principle
that two roads to one offset should never disagree.

This closes the loose end step two left open. That step reported that a band
is recoverable only non-locally and that "nothing tells a backend author
that". Something does now, in two places in `docs/DevelopersGuide.md`. The
band table under Bounded Integers had already been written up from step two by
another hand; this step adds its fourth row, a constructor payload, which the
aggregate work is what exercised. The field-index asymmetry is new and is in a
short "Reading the IR" section that points at the band table rather than
restating it. Two copies of one table is how a doc starts disagreeing with
itself.

### What the emitted code costs, which matters more here than usual

Two characteristics a caller has to know, because this machine has 16
kilowords of heap and no reclamation of any kind.

**Every constructor mention allocates, including a nullary one.** `Dot` is a
one-word object built fresh at each mention, the same cost the
`DevelopersGuide` pitfall records for the x86-64 lane. There it grows the
heap; here a nullary constructor mentioned inside a loop exhausts the address
space and traps. A mode that a loop dispatches on should be an integer, not a
variant, and on this target that is a hard constraint rather than a
performance note.

**A match is a linear chain of tag tests, not a jump table**, so a match of
n branches costs O(n) comparisons in the branch taken last. With three
constructors that is not worth improving, and a table would cost address
space this machine does not have to spare.

### Cost, measured

The plug is **1,826 lines of Codex** (emitter 1,305, encoder 495, entry 26),
up from 1,432. So aggregates cost about 394 lines, and the whole plug remains
inside the original 1,500-2,500 estimate from the PTX and RISC-V comparables
even after doubling the language surface it accepts.

### What v2 still does not do

Lists, lambdas, closures, indirect calls, effect handlers beyond a plain `act`
block, reals, vectors, and field STORE: records are built once and not
mutated, `__record-set` being a concession that needs an ownership discipline
this backend does not model. A nested pattern under a constructor is refused
rather than flattened, because matching it loosely would accept values the
source rejects. Each is refused by name.

A list is the interesting one of those and it is deliberately still out. A
`List` is a growable value, and this backend has a bump allocator with no
reclamation on a machine with 16 kilowords of heap, so the honest v3 question
is not "can a list be emitted" but "what does a list COST here", which is a
different experiment.

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

### DONE 2026-08-09. Both arms behave, and the refusal is partial

`codex/test/kleene-tribool.codex` and `codex/test/errors/kleene-excluded-middle.codex`,
both in the BVT whitelist. `Tri = TFalse | TUnknown | TTrue`, `tnot`, and
Kleene `tor`.

| claim | result |
|---|---|
| `tnot (tnot t) === t` | proved, induction, three arms Refl |
| `tor t TTrue === TTrue` | proved, induction, three arms Refl |
| `tor t (tnot t) === TTrue` | **refused, CDX2001 `Con:TTrue vs Con:TUnknown`** |

**The refusal is PARTIAL and that is the whole result.** Excluded middle holds
at `TFalse` and at `TTrue` and fails only at `TUnknown`, so two of the three
subgoals discharge and one cannot, and the diagnostic names the two values it
could not equate. That exercises the per-constructor subgoal generator
(`check-induction-arm`) rather than a claim rejected wholesale, which is what
every existing tripwire in the whitelist tests.

It also covers a shape the other proof tests do not: three NULLARY
constructors, so every arm is `Refl` with no inductive hypothesis and no
recursive field. The existing induction tests all have a recursive
constructor and reach `cong ih`.

**Claims 2 and 3 stand on this evidence.** Nothing here is contingent on the
sourcing ruling or on the Rust toolchain, and it needed no compiler change.

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

`manitc` is released under the AGPL-3.0.

**RULED 2026-08-09, Damian: cleanroom from the published specification, go.**
The author has acknowledged and approved that approach, and has granted Damian
rights to the implementing code. The plug is still to be written from
`docs/t3isa-reference.md` and from nothing else, as this section already
prescribed -- the ruling removes the blocker and does not relax the sourcing
rule.

### The specification is versioned, and the permission is now written down

**As of 2026-08-10 upstream the sourcing question is settled in public.** The
reference is tagged `t3isa-spec-v1.0` through `v1.3`, it declares itself the
normative definition, and it asks implementations to cite the tagged version
they were written against. Where the document and the emulator disagree, the
document now says that is a specification bug and asks for a report.

`NOTICE` gained a section granting exactly what we are doing:

> Independent SOFTWARE implementations of T3ISA -- compilers, assemblers,
> emulators, and tooling -- written from the published specification without
> use of manitc source code are welcome and encouraged. The author does not
> regard such spec-conformant software implementations as derivative works of
> manitc, and does not intend to assert the patent applications listed above
> against them.

Hardware is explicitly carved out and still needs a separate licence, which
does not touch us.

**The one obligation is attribution**, and it is asked for in a specific form:
"T3ISA designed and specified by Manish Jagdish Thatte". That line, with a link
to the tagged reference, is carried in `README.md` claim 7 and at the head of
`T3IsaEmitter.codex`. If the plug grows another entry point that reads as a
standalone artifact, it carries the line too.

**We are written against `t3isa-spec-v1.3`.** Cite that version, not "the
reference", in anything that goes outward.

## State, 2026-08-10

1. **Sourcing: RULED, cleanroom from the spec, go.** See above.
2. **The Kleene probe: DONE**, both arms in the BVT.
3. **Toolchain and oracle: DONE.** Rust in `D:\Toolchain-Ternary`, `manitc`
   built, a T3ISA program compiled and run with correct output. See "Cost".
4. **The back end: DONE and proven.** Opcodes, field placement, label
   addressing, string table and sidecar format all measured; the prototype
   assembler reproduces 22,772 words of their output byte-for-byte with four
   sabotages confirming the check bites. See "Step one".
5. **The emitter: DONE, and the gate is green.** See "Step two". 876 lines
   of Codex, no compiler change, both paths producing identical output, the
   four predicted failures all firing by name, and six findings that were
   not predicted.
6. **Aggregates: DONE, and the second gate is green.** See "Step four".
   Records, nested records, variants and match, on a heap whose usable region
   was measured before anything was built on it. Still no compiler change.
   Four layout sabotages all fire, and two of the four predictions were wrong
   and are corrected.
7. **Spec v1.3: ADOPTED 2026-08-10.** See "Step five".

### The size, re-measured 2026-08-10

**Do not quote 1,826 again.** It was wrong when written and this design and
README both carried it. Measured over the three chapters
(`T3IsaEmitter`, `T3IsaEncode`, `T3IsaPlug`), counting every line of the files
on disk:

| | lines |
|---|---|
| code | 1,466 |
| column-2 prose | 388 |
| blank | 408 |
| **total** | **2,262** |

The method matters, because two obvious ways of counting disagree here by
enough to mislead. `Measure-Object -Line` silently drops empty lines, and
`p4 print` piped through it undercounts by a further 12 against the file on
disk. Count the array `Get-Content` returns. Today's change added 33 lines of
code and the rest prose, all of it about the external machine, which is the one
category rule 12 keeps.

## Step five, 2026-08-10: upstream moved, and we moved with it

**Every finding we routed upstream was accepted and fixed**, and the
specification is now versioned, with a written grant for independent software
implementations. See "The specification is versioned".

**A second oracle was built** from `t3isa-spec-v1.3` (`a4da664`) into
`D:\Toolchain-Ternary\target-v13`, deliberately NOT over the v1.0 build, so the
instrument the whole encoding was derived against still exists and a
disagreement can be attributed to the emulator rather than to us. It needed no
`/ALTERNATENAME:isatty=_isatty` flag, which is the `_isatty` link finding
confirmed fixed from the outside.

Four of the seven load-bearing target facts moved; see "Target behaviour the
plug is built on", which now carries both the old and the new measurement for
each. **None of them broke the plug**, and the reason is worth stating because
it is not luck in every case. `TSHR` rounding to nearest rather than truncating
would have been silent and wrong, and we were clear of it only because the
constant materialiser needs multiply and never emits `TSHR`. The two guards we
carried against the `TSHI` register form were correct when written and are now
removed, because the behaviour they guarded against changed in v1.3 and the
guards would now reject a conforming program.

### What v1.3 opens up

Not taken, listed so the next session does not have to rediscover it:

- **`show` outside a print is now possible.** `fmt::show_int` is syscall 14 and
  is measured working. The blocker is no longer the machine; it is that this
  emitter has no Text value to carry the returned handle. Text append stays
  impossible, since `fmt::concat` is not implemented on T3 at all.
- **Syscall 218, `heap_alloc_words`,** is a real bump allocator with a
  trap-on-exhaustion bound. It could replace the hand-rolled allocator at
  32,768. Ours is measured, works, and does not depend on their layout, so
  this is a trade and not an improvement: theirs is one syscall against our
  eleven instructions, ours keeps the plug independent of a region they are
  free to move. They moved it once already, 64,000 to 63,000.
- **Opcodes 29-35** are documented for the first time: `BAND`, `BOR`, `BXOR`,
  `BSHL`, `BSHR` for binary interop and `LOADT`/`STORET` for single-trit
  memory. Nothing in Codex needs them yet. `LOADT`/`STORET` are the obvious
  lowering if a Trit type ever wants one word per trit.

**The question this design was written to answer is answered.** README claim
7 holds for a target sharing almost none of the assumptions the other 53 were
built under: a new target was a plug. The honest qualification is that it
holds for integers, control flow, formatted output and now aggregates, and
that reals, vectors and wrapping overflow sit outside it by construction
rather than by omission. The 1,500-2,500 line estimate is superseded by the
measurement in "Step four" and should not be quoted again.

**The scripts under `codex/plugs/t3isa/spec/` do not run without the external
toolchain**, which is on this machine only: `D:\Toolchain-Ternary` for
`manitc.exe` and `D:\Projects\maniTC-main` for the example corpus. They are
landed as the executable record of how the encoding was derived, not as a
gate. Nothing in `build/build.ps1` reaches them.

## What is left, and why it is not being done now

**Damian's ruling, 2026-08-11: closed, revisit when the specification next
moves.** The result is academic to us in the precise sense that it changes
nothing we ship: no product of this tree runs on a ternary machine, and the
finding it produced -- that the IR's binary assumptions are thin enough for a
non-binary target to be a plug -- is already banked in the README and here.
The OS is the priority. A follow-on that deepens a result we already have
loses to work that does not yet exist.

Three things are named and not taken. None is a defect and none blocks
anything; they are recorded here so that a session picking this up later does
not rediscover the list, and in `codex/plugs/plugs-backlog.md` so it is
reachable from the register that owns the quire.

1. **v3, and what a list COSTS.** The honest question is not whether a list
   can be emitted. It is what a growable value costs on a bump allocator with
   no reclamation and 16 kilowords of heap, which is a different experiment
   from the one this design ran.
2. **`show` outside a print.** Possible since v1.3 (`fmt::show_int`, syscall
   14, measured working). The blocker is now ours rather than the machine's:
   this emitter has no Text value to carry the returned handle. Text append
   stays impossible either way, `fmt::concat` not being implemented on T3.
3. **The heap syscall trade.** `heap_alloc_words` (218) could replace the
   hand-rolled allocator. It is a trade, not an improvement: one syscall
   against our eleven instructions, against a dependence on a region they are
   free to move, and which moved once already.

**What a later session must re-do rather than trust.** The plug was proven
against `t3isa-spec-v1.3` and two oracle builds that exist on this machine
only. If the specification has moved, every row of "Target behaviour the plug
is built on" is a measurement whose date has passed: re-measure it against the
new emulator before believing a green gate, because four of the seven rows
moved between v1.0 and v1.3 and one of them (`TSHR` rounding) would have been
silent and wrong had the emitter depended on it. L-COUNT applies to the size
table for the same reason.

**There are two oracles and the difference matters.**
`target\release\manitc.exe` is the v1.0 build the encoding was derived against;
`target-v13\release\manitc.exe` is v1.3 and is the current one. `gate.ps1`
defaults to v1.3; pass `-Manitc` to reach the older build. The corpus under
`spec/build-output/` was generated by the v1.0 build and is NOT regenerated:
v1.3 changed string-literal addressing for any program with ten or more
literals and moved struct allocation from the stack to the heap, so those
artifacts would shift by design. They are a record of a derivation that
happened, not a fixture to keep current.

**Cleanroom discipline, now that the upstream source is on this machine**
(`D:\Projects\maniTC-main`, `maniTC-alpha`). The plug is written from
`docs/t3isa-reference.md` only. Their `src/` is not read for how to emit
T3ISA. Building the compiler and invoking it as a black-box oracle is the
design and does not compromise that; reading its backend would.
