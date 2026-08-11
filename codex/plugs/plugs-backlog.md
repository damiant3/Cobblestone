# Plugs -- open capabilities

Quire-domain backlog, same rules as the app registers: an entry says what
is still missing and nothing else, a closed entry is DELETED, and a gap
that is still real is never quietly dropped. There is no platform-wide
register; `docs/PM/CurrentPlan.md` carries the shape.

**The standing hazard in this quire: a plug that does not handle a
construct usually EMITS SOMETHING ANYWAY and reports OK.** A missing
builtin arm passes the name through as an ordinary call; a wrong field
spelling emits a division; a wrong `list-push` emits a mutating append.
The target's own toolchain or runtime is the first thing to notice, and
for most of these plugs nothing downstream ever runs. Assume silence is
silence, not agreement (L-GAP).

## 1.2 -- Fourteen plugs emit a DIVISION where a record field access belongs

The IR spells a field as `name/slot` (`ir-field-with-index`,
`compiler/Emit/IRTextEmitter.codex`); only html and csharp ever learned
to strip the slot, and python and javascript were fixed in main 13199.
**Still wrong: ada, clojure, d, electron, elixir, flutter, fortran, go,
groovy, gtk, haskell, pascal, react, ruby.**

Slot 0 divides by zero and crashes, slot 1 divides by one and is
SILENTLY CORRECT, slot 2+ is silently wrong; the map-lookup plugs (go,
flutter, groovy, elixir) get a silently missing key instead and need
different handling than a substring before the slash. Copy `py-field` /
`js-field`.

Deferred because none of these has a runtime wired into
`build/plug-oracle-test.ps1`, so the change cannot be verified today.
**This wants two or three more oracle arms first, and an owner. A blind
sweep would be a compiling change, not a correct one.**

## 1.7 -- Which plugs emit `list-push` as an unconditional in-place append?

Unswept. `list-push` is NOT unconditionally destructive on bare metal:
`__list_snoc` (`compiler/Emit/X86_64ListHelpers.codex`) stores in place
and returns the SAME pointer only when the backing array has spare
capacity, extends in place when the list is the topmost allocation, and
otherwise COPIES to a fresh allocation and returns a new pointer.
**An empty list literal has no spare capacity, so `list-push [] x`
returns a new list and leaves the original empty**, and the compiler
depends on that (`tco-ensure-temps`, `X86_64.codex:359`).

csharp emitted `_l.Add(v); return _l;` and silently reused spill slots
across tail calls in 17 functions of the 5,000 in the compiler. Fixed
there with a capacity-aware `_Buf.lpush` (in place when
`Count < Capacity`, else copy into twice the capacity, floor 4), which is
amortized O(1) with no measured slowdown.

**The failure is invisible in small programs: it needs a function with
two tail calls in one body before it shows.** The other 43 plugs are
unchecked. The semantics are published in `docs/DevelopersGuide.md`
under Lists.

## 1.8 -- Which plugs emit records into a mutable-capable target?

Still unswept, but no longer blocked: COMPILER-4 is closed and the wire
carries `(mutable)` as an optional trailing element on `rec-def` since
2026-08-08, so a plug CAN now read the answer off `ARecordTypeDef`.
Deriving it from the `field-store` nodes remains the stricter answer and
is what csharp does in `collect-mut-names`; the marker is the wider one.
**Consequence if a plug does neither: every store is a silent no-op and
the program still compiles.**

**Two measured instances, 2026-08-08, both from reading the emitters:**

- **kotlin is the silent no-op, and it is the bad direction.**
  `emit-kt-record-fields` emits `val` for EVERY field, and
  `IrFieldStore` is emitted as `rec.copy(field = value)` -- a functional
  copy whose result is discarded where a statement was wanted. It
  compiles and it throws the mutation away. Scala has the same shape
  (`case class` fields with no `var`).
- **swift is safe by accident.** `emit-sw-record-fields` emits `var` for
  every field regardless, so it is over-permissive and never refuses a
  store. It is wrong in the harmless direction.

Neither is fixed here. The sweep is the remaining work, and kotlin is
the one to do first.

## 1.3 -- RISC-V frameless TCO is a KNOWN-BAD PAIR, not a gap

The admission gate is `rv-is-frameless-tco`; the test that asks the
question that actually matters is `rv-body-is-frameless`, which NOTHING
CALLS (its only references are its own recursion, L-UNCALLED). The gate
in use reads a local count that cannot express the real budget, because
for callee-saved registers in a function emitting no prologue the budget
is zero rather than six.

**Do not simply wire the honest test in.** That was tried 2026-07-20 and
it makes the lane WORSE: a body it refuses falls to the framed path, and
the framed two-argument tail call is separately broken for the same
shapes (`v-shru`, a two-parameter loop over `bit-shru`, answers correctly
frameless and hangs framed). Both paths have to be right before the gate
can be turned on, so this is ONE item and not two.

Related and also open: `RiscVCodeGen.codex` 1880-1884 records that the
frameless literal-operand fix is NOT the general fix, and the general
temp-collision defect either side of a frameless binop is open. The full
account lives in `annotations/codex/plugs/riscv/RiscVCodeGen3.json`; a
live known-bad should not rest only in a sidecar, which is why it is
also here.

## 1.4 -- RED, pre-existing: the spirv text emitter drops a call

`codex/plugs/spirv/test-spirv.ps1` fails: `FAIL: missing
'OpFunctionCall %3'`, from a 9045-char `spirv-probe.spvasm`.

Established with a control rather than assumed: reverting every spirv
source to depot state, rebuilding the plug and re-running gives the
IDENTICAL failure and an identically sized artifact, and the plug binary
is 202,791 bytes either way. So the text path drops a call the probe
requires. `spirv-probe.codex` exercises a call deliberately, as one of
the four places the emitter used to produce something SPIR-V would
refuse.

`test-binary.ps1` (the word/validator path) PASSES with all four negative
controls firing, so **the defect is the disassembling text emitter, not
the binary encoder.** `test-emit.ps1` cannot run at all: it wants a plug
from `build-bin.ps1`, which does not exist in this directory.

**Nothing gates this.** `build/build.ps1` is green and does not run it,
which is why it can sit red.

## 1.1 -- Deferred: lift the plug type reconstruction into shared code

`a64-atype-to-codex-type` (`arm64/Arm64CodeGen2.codex:1926`) reconstructs
`IntegerTy lo hi mode` from `(a-bounded ...)` in the IR's `type-defs`
section, and it is the only by-name recovery of an elaborated field type
written anywhere. Step 3 of
`docs/Designs/Active/Compiler/IRTypeEmission.md` is to lift it into
shared plug code and point the group-3 sites at it with the emitter
UNCHANGED, so the rerouting is proven while the inline form still wins.

Group-3 sites: `clamp-field-val` (csharp), `a64-field-type-for-store`,
`rv-find-field-type-st`, `a64-collect-field-types`,
`rv-collect-field-types`, `rc-check-ctor-ref-sum`, and the python and
javascript clamp paths added in main 13199.

Deferred 2026-08-05 by Damian: it is a de-risking rehearsal, not a
prerequisite, and option A's own risks are measured closed.

## 1.9 -- The wasm plug has no clamp code at all

`afields-to-rfields` fills every `type-val` with `ErrorTy`. Pre-existing
gap, not a regression from any step.
