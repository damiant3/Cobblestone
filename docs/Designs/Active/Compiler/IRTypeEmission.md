# IR Type Emission: by structure or by reference

*fester, 2026-08-04. This is a DECISION document. It exists because the fix
it prices changes the IR text, and the IR text is the wire contract the plugs
parse, so it is not one lane's to take.*

## What is being decided

Every IR node emits its own type IN FULL. `IRTextEmitter.codex:273` writes
`(sum-ctors ...)` and `:274` writes `(record-fields ...)` at every node that
mentions the type, recursing through `ir-emit-sum-ctors:291` and
`ir-emit-record-fields:295`, so a record holding a record holding a record
renders all three every time the outermost one appears. IR size is therefore
O(nodes x structural size of each node's type).

The compiler is the program in this tree whose values carry the biggest
types, so it pays this worst. It is what blocks C1: the whole-compiler
`-IrCce` emit reaches definition 3,789 of 3,790 and dies on `builtins`, whose
259 elements (`Types/Builtins.codex:90`) each carry `Maybe CodexType` (a
26-constructor recursive sum, `Types/CodexType.codex:6`) and a lambda reaching
`CodegenState` (a record of 48 fields, `Emit/X86_64State.codex:83`). How that
was cornered is in `ProportionalDecks.md`; this document is only the fix and
its cost.

The question is whether to emit these types by REFERENCE, name plus type
arguments, and let a consumer that needs the structure look it up.

## The measurement

Six units, `-IrUni` against `seed/Codex.cdx` content `52E0A3A00218E19F`,
2026-08-04. "Inline structure" is the byte count of the outermost
`(record-fields ...)` and `(sum-ctors ...)` groups, which is exactly what
would stop being written.

The instrument is `build/ir-type-account.ps1`, in the tree so these numbers
can be re-measured rather than carried forward. `-SelfTest` is calibrated
against a hand-computed synthetic, and three sabotage arms were fired against
it: removing the string-literal handling, counting nested groups again, and
failing to recognise `sum-ctors`. Each makes the self-test fail and the
control passes. **The first version of that self-test passed under sabotage**,
because its quoted paren sat after every counted group had closed, so it was
blind to the axis it existed to check.

Reproduce with `build/compile.ps1 -Src <app> -Out <x>.out -Log <x>.log -IrUni
-Kernel seed/Codex.cdx`, then point the accountant at the log; the IR lands in
the log capture between `IR-BEGIN` and `IR-END`.

| unit | IR bytes | inline structure | share | remainder | shrink |
|---|---|---|---|---|---|
| prism | 1,995,081 | 1,706,339 | 85.5% | 288,742 | 6.9x |
| dbadmin | 1,971,794 | 1,760,894 | 89.3% | 210,900 | 9.4x |
| spark | 3,424,389 | 3,065,365 | 89.5% | 359,024 | 9.5x |
| circuits | 8,861,672 | 7,910,201 | 89.3% | 951,471 | 9.3x |
| deskboot | 29,865,019 | 27,134,897 | 90.9% | 2,730,122 | 10.9x |
| gopboot | 31,806,610 | 28,388,055 | 89.3% | 3,418,555 | 9.3x |

Six units spanning 16x in size land in a band 85.5 to 90.9 per cent wide.
That is a corpus result rather than a ratio fitted through two points.

### It removes the dimension, not just a constant factor

The corpus number says the artifact gets about 9x smaller. That alone would
not obviously unblock C1, because `builtins` is not 9x over the ceiling. The
question that matters is whether the change removes the TYPE dimension from
the size, and it does.

Probe (`build/ir-type-probes/ctorN.codex`): a 20-element list of
`record { nm : Text, ty : Ty }`, holding the element count fixed and varying
only how many constructors `Ty` carries.

| ctors on `Ty` | IR bytes | bytes after elision |
|---|---|---|
| 2 | 9,167 | 4,499 |
| 6 | 13,871 | 4,627 |
| 12 | 21,024 | 4,828 |
| 24 | 35,696 | 5,244 |

Across that range the IR grows **3.89x** and the remainder grows **1.17x**,
and that 1.17x is the type-defs section carrying more constructor names once.
The term that scales with the type disappears. `builtins` is precisely the
definition dominated by that term, which is why it renders to more than a
hundred times the next-largest definition in the compiler.

### The structure is already published, once, and it is nearly free

Every unit's IR prefix already carries a `(type-defs ...)` section
(`IRTextEmitter.codex:1058`) listing every `rec-def`, `var-def` and `unit-def`
with its full field and constructor lists. `fe.type-defs` is
`checked.scoped.type-defs` (`opening.codex:706`), the whole scoped unit, so
for the DDC case the lookup table is complete. That section costs:

| unit | type-defs section | share of IR |
|---|---|---|
| circuits | 12,706 | 0.14% |
| deskboot | 70,426 | 0.24% |
| gopboot | 79,289 | 0.25% |
| spark | 9,210 | 0.27% |
| prism | 9,851 | 0.49% |
| dbadmin | 27,470 | 1.39% |

The wire spends between a seventh of a per cent and one and a half per cent
describing every type once, and 85 to 91 per cent describing the same types
again at every node that mentions them.

**Where a lazy reading would overclaim.** The two are not the same
representation. `type-defs` carries SURFACE types (`ATypeExpr`), the inline
form carries ELABORATED types (`CodexType`). The duplication is exact for a
type's name, its constructor names and their arity, and its field names and
their order. Whether it is exact for a field's integer bounds is a measured
question, answered below.

## What the consumers actually read

There is exactly one parser of this format in the tree,
`codex/plugs/common/IRTextParser.codex`, and every plug goes through it. So
the wire contract is one file plus the plugs that pattern-match the parsed
`CodexType`. Auditing every `is SumTy (n) (args) (ctors)` and
`is RecordTy (n) (args) (fields)` arm, they fall into three groups.

**1. Ignore the structure entirely.** ada, fortran, html, zig, rust and most
csharp arms bind the third argument and use only `name.value`. Cost: nothing.

**2. Read the structure, and ALREADY have a by-name fallback in the same
function.** This is the large group and it is why the change is smaller than
it looks. A `ConstructedTy` is already a by-reference type on this wire, so
every plug has had to learn to resolve one, and the inline read is typically
the first arm with the lookup as the `else`:

- `a64-resolve-ctor-tag` (`Arm64CodeGen3.codex:834`): inline
  `a64-find-ctor-index ctors`, then `else a64-find-ctor-in-type-defs`.
- `a64-max-fields-for-type` (`Arm64CodeGen.codex:1144`): `SumTy` walks the
  inline constructors, `ConstructedTy` and `TypeCon` call
  `a64-max-fields-from-defs`.
- `field-index-from-ctx` (`WasmEmitter.codex:249`): `RecordTy` walks inline
  fields, `ConstructedTy` and `TypeCon` call `resolve-record-fields`.
- `rv-find-field-index-st` (`RiscVCodeGen2.codex:1376`): inline, then
  `rv-find-field-in-type-defs`, then a learned-fields table.
- `rc-ty-eq` (`RecheckCore.codex:115`, via `rc-ty-eq-known:126`) compares
  `SumTy` and `RecordTy` NOMINALLY at `:150-165`, ignoring the constructor and
  field lists outright, and already treats `SumTy` and `ConstructedTy` as
  equal.

For this group the work is to route the `SumTy` and `RecordTy` arms into the
path the `ConstructedTy` arm already takes. The destination code is written,
compiled, and exercised by every `ConstructedTy` in the tree today.

**3. Read the structure for an ELABORATED field or payload type.** This is
the whole cost:

- `clamp-field-val` (`CSharpEmitterExpressions.codex:1140`): looks up the
  field's type to decide whether a store needs `Math.Clamp`. Needs
  `IntegerTy (lo) (hi) OvClamping`.
- `a64-field-type-for-store` (`Arm64CodeGen3.codex:157`) into
  `a64-maybe-clamp`, and `rv-find-field-type-st` (`RiscVCodeGen2.codex:1396`):
  the same decision on ARM64 and RISC-V.
- `a64-collect-field-types` / `rv-collect-field-types`: field type lists for
  record layout.
- `rc-check-ctor-ref-sum` (`RecheckWellFormed.codex:356`): compares a sum's
  declared payload types against the constructor's own signature through
  `rc-ty-eq-list`, which for integers compares lo, hi and overflow mode. This
  is the SUM-side analogue and it is in val's lane.

**The failure mode differs by site and both matter.** For the three code
generators and the C# plug it is a silent wrong answer: a clamping bounded
integer that stops being recognised still compiles and still runs, it just
stops clamping. For the rechecker it is louder and worse in the short term:
under a naive option A, `ctors` arrives empty, `rc-has-ctor` answers False,
and `rc-check-ctor-ref-sum:358` fires `ctor-ref-unknown` DISAGREE on every
constructor reference in the corpus. val's sweep would go from 39 disagreements
to thousands.

### The elaborated bounds are already on the wire, and one plug already reads them

This is the finding that changes the recommendation, and I had it backwards in
the first draft of this document.

`IRTextEmitter.codex:313` emits `(a-bounded <base> <lo> <hi> <ov-mode>)` inside
the surface `type-defs` section: bounds AND overflow mode, published once,
today. And `a64-atype-to-codex-type` (`Arm64CodeGen2.codex:1926`) reconstructs
`IntegerTy lo hi mode` from exactly that at `:1929`, reached through
`a64-resolve-record-fields:1902` and `a64-afields-to-rfields:1919`.

So the by-name recovery of the precise thing group 3 needs is already
written, in one plug. `afields-to-rfields` (`WasmEmitter.codex:244`), which
fills every `type-val` with `ErrorTy`, is not evidence that the information is
missing from the wire. It is evidence that the Wasm plug did not bother. The
first draft cited the Wasm version as proof of a gap and never found the ARM64
version, which is the same defect this project documents everywhere else: an
instrument pointed at part of the question, read as an answer to all of it.

**Measured coverage** (`ir-type-account.ps1 -Coverage`), asking whether every
distinct record field emitted inline with explicit bounds is also carried as
`(a-bounded ...)` in type-defs:

| unit | bounded fields inline | in type-defs | uncovered |
|---|---|---|---|
| prism | 12 | 22 | **0** |
| dbadmin | 12 | 23 | **0** |
| spark | 0 | 0 | 0 |
| circuits | 0 | 34 | 0 |
| deskboot | 63 | 77 | **0** |
| gopboot | 65 | 89 | **0** |

Nothing uncovered anywhere, and type-defs consistently carries MORE bounded
fields than the inline form does, because it lists every field of every
declared type while the inline form only shows fields of types some node
mentions.

**The constructor-payload side is UNTESTED, not clean.** The same tool reports
zero bounded constructor payloads in all six units, on both sides. That is
zero instances, so it is no evidence at all about whether `var-ctor` field
bounds survive the round trip. It is the open question for
`rc-check-ctor-ref-sum`, and it needs a unit that has one, or a probe written
to have one.

The other honest limit: `a64-atype-to-codex-type:1937` defaults its
`otherwise` arm to a wide `OvError` integer, and `:1933` maps a bare `Integer`
the same way. The recovery is exact for an explicitly bounded field and an
approximation otherwise.

## The instruments that already exist

The first draft said the silent-wrong-answer failure mode "rules out change it
and see if the plugs still build" and named no alternative. Two exist:

- **`build/check-plug-types.ps1`**, wired into the standing gate at
  `build/build.ps1:337`. It diffs the forms the emitter writes against the
  arms the parser accepts, with three known holes recorded in
  `build/plug-wire-baseline.txt`, and fails the build on a fourth. A new form
  that the parser did not learn would be caught by `build/build.ps1`.
- **`build/plug-oracle-test.ps1`**, which compiles one subject two ways, runs
  both, and requires the answers to agree. That is the shape that catches a
  silent wrong answer. **It cannot see this one today**: its subject
  `codex/test/plug-oracle-arith.codex` contains no record and no clamped
  bounded integer, so the clamping path is not on it.

Adding a clamped bounded-integer record field to that subject is the cheap,
concrete de-risking step, and it should land BEFORE the emitter changes so it
is a control rather than a claim.

## The options

### A. Emit `SumTy` and `RecordTy` by reference, reusing what exists

Drop `(sum-ctors ...)` and `(record-fields ...)` from the inline form, keeping
name and type arguments. Group 2 routes into its existing `ConstructedTy`
path. Group 3 adopts the `a64-atype-to-codex-type` pattern to recover
elaborated bounds from `(a-bounded ...)` in type-defs.

- Buys: 85 to 91 per cent of the artifact, and the type dimension.
- Costs: no new emitter form and no new parser form. Rerouting in group 2,
  and lifting one existing ARM64 function into shared use for group 3.
- Measured risk: zero uncovered bounded record fields across six units. Open
  risk: the constructor-payload side, where the corpus has no instances.
- The emitter's generic-argument recovery is unaffected:
  `IRTextEmitter.codex:274` derives type arguments from the field tvars BEFORE
  emitting, so `SortPartition<T9>` still renders.

### B. A, plus an elaborated type table published once

Do A, and add to the chapter prefix a table keyed by type name carrying each
record field's and each constructor field's elaborated `CodexType`. This is
the shape val shipped for `grounds`: a keyed table in the header, published
once, ignorable by a consumer that does not need it.

- Buys: everything A buys, with no reliance on surface-to-elaborated recovery
  being exact.
- Costs: one new emitter form, one new parser form, four call sites. Bounded
  in size by the type-defs section beside it, so 0.14 to 1.39 per cent.
- **B is no longer the recommendation, but it is the answer if the
  constructor-payload measurement comes back uncovered**, and it can be scoped
  to the sum side alone if that is where the gap is.

### C. Leave the wire alone

For completeness, and to say plainly that I do not think it works. The
per-definition `__heap-save`/`__heap-restore` bracket already means peak is
ONE definition, so there is no batching left to win. `-Decks` was measured on
2026-08-03 and its usable window is about one percentage point wide.
`bare-metal-ram-size` buys a bigger number for the same design to grow into.
The one honest variant is to stream per NODE rather than per definition, which
does not reduce the artifact at all: `gopboot` would still ship 31.8 MB of IR
where 3.4 MB carries the same meaning.

## Recommendation and the order to do it in

**Take A.** Sequence, because two of these are controls and belong first:

1. Put a clamped bounded-integer record field on
   `codex/test/plug-oracle-arith.codex` so `plug-oracle-test.ps1` can see a
   clamping regression at all. This is a control, and it must be green before
   anything else moves.
2. Measure the constructor-payload side against a unit that HAS a bounded
   constructor payload. If covered, A is enough; if not, take B for the sum
   side only.
3. Lift `a64-atype-to-codex-type` into shared plug code and point the group-3
   sites at it, WITHOUT changing the emitter. Everything still passes, because
   the inline form is still there and still wins.
4. Only then change the emitter, and run the whole-compiler `-IrCce`.

Steps 1 through 3 are not seed-affecting and land independently. Step 4 is the
one that needs the token and the gate.

## Who has to agree

Not "the C# plug owner", which is what the first draft of this document and
`fester-workplan.md` both said. `docs/PM/CurrentPlan.md:234` claims
`codex/plugs/csharp/**` for fester, so that sentence was routing a decision to
the lane that wrote it.

- **Damian.** `Emit/IRTextEmitter.codex` is compiler source and seed-affecting.
- **val.** `codex/plugs/recheck/` is val's lane and `rc-check-ctor-ref-sum` is
  a group-3 site. A naive A would flood C2's sweep with false disagreements,
  and val's kill-rate is the instrument that would notice.
- arm64, riscv and wasm carry group-3 sites and are claimed by no lane in the
  `CurrentPlan.md:228-237` table.

## What is not established

- **The compiler's own fraction is not measured, and cannot be until the
  change exists.** Every number here is from units that fit. The corpus band
  is tight, the type-dimension probe explains the mechanism, and `builtins` is
  the definition most dominated by that mechanism, so the expectation is that
  it does better than 9x rather than worse. That is an expectation. The
  whole-compiler `-IrCce` run is the test, and it costs one run once the
  change is in.
- **Whether any plug's output actually changes** is a read of the group-3
  sites plus step 1's oracle subject, not a sweep. An app-class sweep measures
  compiles, and every consumer here keeps compiling either way.
- Bounded constructor payloads, as above: zero instances found is not
  coverage.

## What would falsify the case for A

A group-3 site needing an elaborated type that `type-defs` cannot key by name,
because the type is not nominal: a structural type in a field position with no
`type-defs` entry. I did not find one, and `rc-ty-eq` already comparing these
types nominally is evidence there is not one, but I have not proved the
negative and it is the thing to check before writing the emitter.

Cheaper and more likely to fire: a unit whose bounded record fields are NOT
all covered by `(a-bounded ...)`. Six units say zero uncovered. A seventh that
says otherwise moves the recommendation to B, and the command that would find
it is `ir-type-account.ps1 -Coverage` against IR text already on disk.
