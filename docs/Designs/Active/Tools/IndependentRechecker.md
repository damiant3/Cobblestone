# Independent Rechecker -- a plug that re-derives what the compiler asserted

**Status:** Stages 1-3 BUILT (`codex/plugs/recheck/`); stage 4 (proof
retention) open. Phase 0 was re-confirmed against main on
2026-08-03 by val (stage 0). Three of section 4's five claims did not
survive the re-measurement; section 4 is rewritten below and the stage
table in section 5 is re-scoped. Stage 3 is now blocked on a wire
change and is no longer free.

**Author:** AgentGrid session, 2026-08-02, at Damian's request.
Implementation to be assigned to a fleet agent.

---

## 1. The claim under audit

The battery proves the compiler WORKS: 1,402 samples, 66 apps, a hard
fixed point on bare metal. Nothing in this document questions any of
that, and the rechecker adds nothing to it.

It audits a different sentence. README: "Safety claims are
compiler-enforced, not aspirational." That is a claim about every
program, including every program nobody has written yet: a linear value
CANNOT leak, an effect CANNOT cross a boundary undeclared, a bounded
store CANNOT go out of range, a `punctual` function CANNOT allocate.

For a universal claim there is no run that confirms it. The checker's
acceptance is the entire evidence. So a bug in the checker is not like a
bug in a pass: the seed still compiles, every sample still passes, every
app still runs, and the sentence above is quietly false. The failure is
silent by construction, and no instrument the project currently runs
would see it.

Lean shipped exactly this failure in kernel bug #14576 (July 2026): a
phantom parameter in a nested inductive escaped type checking, `False`
became provable, and nothing downstream broke or complained. The
postmortem does not even state what it invalidated.

## 2. Why a SECOND implementation, and what independence means here

Independence means a different implementation of the same rules. It does
NOT mean an outside authority, a third party, or anyone whose judgement
the project defers to. The rechecker is ours, runs on our hardware, and
has no standing to overrule anything. Its only product is a
disagreement set for a human to read.

The value is decorrelation: two implementations rarely carry the same
bug. This project already bought that argument once, at a different
layer -- the cross-architecture battery runs on both Renode and QEMU and
they agree, which is worth paying for precisely because agreement
between unrelated implementations catches what neither catches alone.
The type and proof layer is the one layer with no such instrument.

The Lean case also bounds the expectation. Their independent checker
(nanoda, a separate Rust implementation) MISSED #14576, because of an
unrelated bug of its own that had been fixed a week earlier. Two
watchers, overlapping blind spots. A second checker lowers correlated
risk; it does not eliminate it, and a rechecker that is never itself
tested is decoration. Section 8 is the part of this design that matters
most.

## 3. What it is NOT

- **Not a gate.** It does not run in `build/build.ps1` and cannot make
  the fleet's build red. See section 9 for the promotion criteria.
- **Not authoritative.** A DISAGREE is a bug report against one of the
  two implementations, unresolved until a human reads it.
- **Not a signer, not a seed input, not a codegen change.** It consumes
  an artifact and emits a report. Emitted programs are byte-identical
  whether it exists or not.
- **Not a second compiler.** It answers yes/no about a given IR. It
  never lowers, allocates, or emits.

## 4. Input: what the artifact already carries (Phase 0, re-measured 2026-08-03)

**The 2026-08-02 version of this section was wrong in the direction that
sounded safer, on the two claims the lane exists to audit.** It said
`ir-emit-type` is a closed match over every `CodexType` variant and that
linear ownership and effect rows are both sufficient to re-check from IR
text. Measured against main, `ir-emit-type`
(`codex/compiler/Emit/IRTextEmitter.codex`) ends in `is otherwise ->
"error"`, and two of the five rows are false.

| Claim | IR form | Sufficient to re-check |
|---|---|---|
| Bounded integers | `(int lo hi mode)` via `ir-emit-int-bounds`; `(a-bounded ...)` on record fields | yes, with the caveat below |
| Effect rows | `(effectful (effs ...) (scopes ...) ret)`; on an arrow, a fourth `(row (labels ...) tail id)` slot | yes (was nullary-only; **stage 3a shipped**) |
| Linear ownership | a trailing `(unique "n" ...)` field on `(def ...)` | yes (was none; **stage 3a shipped**) |
| Sums / records | ctor payloads and field types emitted | yes |
| Units, vectors | `(unit ...)`, `(vector n t)`, `(vector-mask n)` | yes |

**Stage 3a is BUILT, and the two rows above are a measurement rather than a
plan.** Re-measured 2026-08-27 against seed `0634584EF849D297` by
`build/ir-fidelity`, whose `linear-param` and `effect-row` cases are the two
pairs this section had reasoned about by hand:

```
consume : linear Integer -> Integer   (def "consume" ... (fn int-default int-default) ... 0 0 (unique "n"))
consume : Integer -> Integer          (def "consume" ... (fn int-default int-default) ... 0 0)
f : Integer -> [Console] Integer      (fn int-default int-default (row (labels (label "Console" "")) "" -1))
f : Integer -> Integer                (fn int-default int-default)
```

Both halves reach the consumer as well as the wire:
`codex/plugs/common/IRTextParser.codex` reads the trailing field through
`parse-unique` and rebuilds the arrow's row through `parse-row`. The sentences
below about it hardcoding `unique-params = []` and rebuilding every arrow as
`FunTy ... empty-row ...` describe that parser as it was.

The type cell itself is unchanged, so the paragraph immediately following still
holds exactly: `linear T` and `T` remain byte-identical AS TYPES, and the
linear fact rides beside the type rather than in it. What moved is that the
fact reaches the wire at all.

**Linear ownership does not reach the wire at all.** The arm is
`is LinearTy (linner) -> ir-emit-type linner`: it unwraps and emits the
inner type, so a `linear T` is byte-identical to a `T`. Measured with
two sources differing only in the keyword, compiled with `-Passes none`
so the callee survived inlining: both emitted the same 1153 bytes, both
carrying `(param "n" int-default)` and `(fn int-default int-default)`.
The front end does enforce the discipline (using the parameter twice
raises CDX2061), so the fact exists and is discarded at the boundary.
An `(a-linear ...)` atom does exist, but on the surface-annotation path
`ir-emit-atype-expr`, which is reached from exactly two sites, both
type-definition shapes: `unit-def` and `rec-field`. It is never reached
for a definition's own signature.

**The linear fact is nevertheless already computed and carried, which
makes stage 3a much cheaper than the erasure suggests.** `IRDef` has a
`unique-params : List Text` field (`codex/compiler/IR/IRChapter.codex`),
populated from exactly the `LinearTy` parameters by `linear-param-names`
in `IR/Lowering.codex`, threaded through `ResolveTypes` and
`LambdaLifting`, and consumed by `Emit/X86_64.codex` to build noalias
slots. It survives the whole pipeline. `ir-emit-def` simply does not
print it. So the linear half of 3a is one field added to the `def` form
and its parser, not a new analysis.

**Effect rows survive only where the type is not an arrow.** The arm is
`is FunTy (p) (row) (r) -> "(fn " & ir-emit-type p & " " & ir-emit-type r
& ")"`: `row` is bound and never emitted, and `ForAllEff` likewise emits
only its body. Concrete effects still ride in `EffectfulTy` (rows remain
inert through the compiler's own stage 2, per `Types/CodexType.codex`),
so a definition with **no parameters** emits its effects intact:
`opening : [Console] Nothing` emits `(effectful (effs "Console")
(scopes "") nothing)`. A definition **with** a parameter does not:
`noisy : Integer -> [Console] Nothing` emits `(fn int-default nothing)`,
at its own definition and at every call site, with `Console` nowhere on
the wire. The compiler enforces it (dropping the declaration raises
CDX2031). Nullary definitions are the case that cannot express the
distinction, which is why the earlier reading passed.

The consumer side confirms the loss is not merely unprinted but
unrecoverable: `codex/plugs/common/IRTextParser.codex` rebuilds every
arrow as `FunTy ... empty-row ...`. Stage 3a therefore lands in two
places, the emitter and that parser.

**Bounded integers reach the wire, but expression nodes carry the
operand type, not a derived range.** `narrow : Integer between 0 and 10
-> Integer between 0 and 20` with body `n * 2` emits the `binary` node
typed `(int 0 10 ov-error)`, the left operand's type, not `0..20` and
not the declared return. So stage 2 must re-derive ranges from operand
types and compare them against **declared** sites, and must never read a
node's own type as a derived range. A rechecker that did would disagree
with almost every arithmetic node in the tree and the disagreements
would all be its own.

**Still true, re-measured 2026-08-27, and now it has a runner.**
`build/ir-fidelity`'s `bounded-int-derived-range` reports DROPPED: two
programs whose declared returns are `0..20` and `0..30` both emit the
body node as `(int 0 10 ov-error)`, the operand type. The checker
demonstrably computes the derived range, because refusing a too-narrow
declaration it names it -- CDX2051, "the value's proven range is 0..20".
So the derivation exists and does not reach the wire, which is a
sharper statement than the caveat above and is what the case banks.

**Four variants have no arm and are indistinguishable from a type
error.** `ProofTy`, `PropEqTy`, `TypeCon` and `TypeApply` fall through
to `is otherwise -> "error"`, which emits the same atom `error` that
`ErrorTy` emits. A rechecker cannot tell a proof type from a failure on
the wire, and must answer UNSUPPORTED on the atom rather than treat it
as either.

**CLOSED. Re-measured 2026-08-27 against seed `4341370C8FE5BAD6`: all
four now have distinct arms in `ir-emit-type`** -- `proof`,
`(propeq ...)`, `(tycon ...)`, `(tyapply ...)`. The recommendation this
section made, that the four be given distinct atoms so a real defect is
diagnosable from the wire, has been carried out. `is otherwise ->
"error"` still exists as the floor, but no named variant reaches it.

**The collision that remains is a different one and is NOT four variants
sharing an atom.** It is `ErrorTy` itself carrying two meanings: the
type-FAILURE atom, and `lower-let`'s no-expectation sentinel. That is
Steve Howell's named residue and it is live -- `build/ir-fidelity`'s
`empty-list-element-type` case is its runner. It reported DROPPED until
COMPILER-30's witness (PR 101, main 20944) landed; measured at the Update 54
release head (seed FCBABF07) it reports CARRIED and the case is re-baselined
to that. The overload itself (one atom, two meanings) is still the row.

**A list expression's type field is the ELEMENT type, and nothing on the
wire says so** (found 2026-08-03 by stage 1, which reported every list
argument in the tree as a type mismatch until it was accounted for).
`(list-expr (elems ...) int-default)` reads as `int-default` where the
node's type is `List Integer`: `IRChapter.codex`'s own `ir-expr-type`
accessor answers `ListTy t` for an `IrList`, and `IR/Lowering.codex`
constructs the node with the element type. Every other node's type field
is that node's type. **Any consumer of this wire has the same trap
waiting**, and it is invisible to a plug that only emits, because
emitting the element type is what a transpiler wants anyway.

**One type has TWO spellings on this wire and they meet each other.** A
sum or record type is emitted inline with its constructors where it
stands on its own, but a RECURSIVE reference inside its own constructor
field list is emitted as the truncated `(ctd "Expr" (args))` -- the
emitter says re-emitting the fields caused exponential IR bloat on
recursive types, so this is deliberate. The consequence is that the
constructor `Mul` carries a parameter typed as the inlined
`(sum "Expr" ...)` while the same sum's own field list for `Mul` says
`(ctd "Expr" (args))`, and a checker comparing them structurally calls
one type two types.

That is what the first full sweep found: four disagreements on
`expr-calculator`, eleven on `final-batch-test`, all on recursive
constructors and every one of them the rechecker's own. Nominal identity
(same name, same type arguments) is the right comparison across the
spelling boundary, and a different name still disagrees, so nothing is
widened except across a difference that was never real. **The kill-rate
was re-run and `ctor-ref-payload-type` still fires**, which is the check
that the relaxation did not blind the arm it touches.

**A PARAMETRIC sum type does not inline its constructors, and its
pattern bindings carry the `error` atom** (found 2026-08-03 by the first
sweep, and the first thing this instrument found that is not about
itself). A non-parametric sum declared in the chapter under test arrives
inlined with its constructor list and correct field types. A parametric
one does not, whether it is cited or declared in the same chapter:

```
  (ctor-pat "Just" (subs (var-pat "v" error)) (ctd "Maybe" (args int-default)))
  ...
  (name "v" int-default)
```

The binding says the variable's type is `error`. The use, three tokens
later, says `int-default`. They disagree and one of them is wrong.
Reproduced with `Box (a) = | Boxed (a) | Empty` declared in the same
chapter, so citation is not the cause: **parametricity is.** It affects
every `Maybe`, `Result` and `Either` pattern in the tree, which is to
say most of them, and a plug that declares a pattern variable from its
`var-pat` type has been emitting a type-error atom all along.

**CLOSED, re-measured 2026-08-27 against seed `4341370C8FE5BAD6`.** The
same shape over the foreword's own `Maybe` now emits
`(var-pat "v" int-default)` against `(var-pat "v" text)`, and the
enclosing pattern carries `(ctd "Maybe" (args int-default))`, so the
binding and the use agree. `build/ir-fidelity`'s
`parametric-sum-pattern-binding` case is the standing guard and reports
CARRIED; it is banked so that a regression here reds rather than
waiting for somebody to re-derive this paragraph.

**What the wire cannot say is WHICH type it is**, and that is section
4's four-variants-one-atom finding biting a second time: `error` is what
`ErrorTy`, `ProofTy`, `PropEqTy`, `TypeCon` and `TypeApply` all emit.
Whether the lowering genuinely assigned `ErrorTy` here (a type-inference
gap) or assigned a `TypeApply` that `ir-emit-type` has no arm for (an
emitter gap) cannot be told from the artifact. That is the second
argument for giving those four variants distinct atoms: not tidiness,
but that a real defect is currently undiagnosable from the wire.

**The IR text is emitted AFTER the optimizer, so the rechecker rechecks
the optimized program, not the author's.** The probe's own log opens
with `info CDX4030: PIPELINE fold-constants,inline-leaf-calls,
inline-single-caller`, and the first version of the linear probe was
useless because `consume` had been inlined out of existence: the def
list did not contain the function whose parameter was the subject. Both
readings are defensible and they are different claims. Rechecking the
optimized form audits what actually runs and implicitly covers the
optimizer; rechecking `-Passes none` audits the sentence the author
wrote. **The design must say which, because a run that does not say is
not interpretable**, and a rechecker pointed at the optimized form will
find UNSUPPORTED where a definition has been inlined away rather than
AGREE. Recommendation: run both, and treat a claim that holds under
`-Passes none` but not after the pipeline as an optimizer soundness
finding, which is the same free-standing value the mutation corpus has
against the compiler.

**Consequence for sequencing.** Stages 1 and 2 are unaffected and still
need no compiler change. **Stage 3 as written was not implementable over
IR text**: its linear half had no input at all, and its effect half
would cover only nullary definitions while appearing to cover the
claim. Under section 6 that is the forbidden shape, so stage 3 needed
`FunTy`'s row and the linear fact pushed onto the wire first. Both have
since been pushed out, so this blocker is cleared; the table above is the
measurement.

**That is an established pattern here, not new work invented by this
design** (Damian, 2026-08-03: things get pushed out to the IR so the
plugs can deal with them the way they already do with the CDX). The
precedent is in the same constructor: `IRTextParser.codex` rebuilds a
def as `IRDef { ... is-punctual = punct, wcet-budget = budget,
unique-params = [], ... }`. Punctual and WCET are read back off the
wire as trailing positional fields on `(def ...)`; `unique-params` is
hardcoded empty. Of the four claims in the `TechnicalDetails.md` sentence, bounded
integers and punctual were pushed out and linear and effects were not.

So stage 3a is additive serialization with no new analysis, in two
places each:

- **linear** -- one trailing field on `(def ...)`, exactly the shape
  `punct` and `wcet` already use, and the parser reading it instead of
  `[]`
- **effects** -- an atom for `FunTy`'s row in `ir-emit-type`, and the
  matching parser arm

It is still under `codex/compiler/`, so it is seed-affecting and
token-bearing, like stage 4. Note the wire has no version atom and the
`def` form is positional, so a new field goes at the end and the
existing atom spellings do not move (the emitter's own prose on the
real-type atoms states that compatibility rule). The rechecker itself remains a plug consuming an
artifact the compiler already produces, in the shape the 53 plugs under
`codex/plugs/` already use (a plug is a directory with a `build.ps1`;
`common/`, `test-input/` and `test-output/` are not plugs).

**Proofs: not there.** `opening.codex:1414` builds the IR text path as
`ir-prune-unreachable (fe.ir) "opening"`. Nothing calls a proof, so
proof definitions are dead-code-eliminated out of the IR text. They
survive only on the CDX path, where `opening.codex:1165-1168` collects
them with `keep-proof-defs` and re-appends them via
`ir-chapter-with-proofs`, after which `is-proof-def`
(`Emit/X86_64.codex`) erases the bodies at x86 emit.

Consequence, and it is the one real cost in this document: **a proof
term is not re-checkable by anything that is not the compiler.** Every
vouch for a Codex proof today is transitively a vouch for the
TypeChecker. Closing that means giving the IR text path the same two
steps the CDX path already performs -- which is a change under
`codex/compiler/`, therefore seed-affecting, therefore token-bearing,
even though no emitted program changes by a byte.

Sequencing follows from that split: everything free comes first.

## 5. Stages

| Stage | Deliverable | Compiler change | Seed |
|---|---|---|---|
| 0 | Re-confirm section 4 against current main; fix this doc if it moved | none | no |
| 1 | **BUILT 2026-08-03, `codex/plugs/recheck/`, kill-rate 9 of 9 with a passing control.** Well-formedness re-check over IR text: every name resolved, no free variables, application arity, constructor applied to its declared field types, every match arm's payload types agreeing with the ctor | none | no |
| 2 | **BUILT 2026-08-03, `RecheckBounds.codex`, kill-rate 12 of 12 overall and sabotage-verified.** Bounded-integer re-derivation: every narrowing site either statically fits or carries the mode the IR declares. Independent of the compiler's own prover | none | no |
| 3a | **BUILT 2026-08-03.** Emit what stage 3 needs: `FunTy`'s effect row as an optional trailing element, and `unique-params` as an optional trailing field on `(def ...)`. Both spellings additive, so every existing positional reader is unaffected. Only rows carrying concrete labels are published (bare row variables cost 15.4 per cent of IR text and are inert) | yes | yes |
| 3b | **BUILT 2026-08-03, `RecheckEffects.codex`, kill-rate 15 of 15.** Effect-row and linear re-derivation: no call escapes a declared row; every linear parameter used exactly once on every path | none | no |
| 4 | Retain proof terms in the IR text path (mirror `keep-proof-defs` / `ir-chapter-with-proofs`), then re-check them: `Refl`/`sym`/`trans`/`cong`/`app-cong`, induction subgoals and IH use, the CDX4023 acyclicity property, the CDX4024 grammar | yes | yes |

Stage 1 is the one that would have caught the Lean bug's shape: a
declaration accepted with an argument nothing ever type-checked. It is
also the cheapest and needs nothing from anybody.

Stage 3 was split by the 2026-08-03 re-measurement. Its 3a half is the
only unplanned seed-affecting work this design has acquired, and it is
small: two arms of `ir-emit-type` that currently discard what they were
handed. It changes no emitted program, exactly as stage 4 does not.

## 6. Output contract

Per definition, exactly one of:

- **AGREE** -- rechecked and consistent with what the IR asserts.
- **DISAGREE** -- rechecked and inconsistent. Names the definition, the
  span, and the judgement that failed.
- **UNSUPPORTED** -- outside the fragment this rechecker covers.

**UNSUPPORTED must never be reported, counted, or rendered as AGREE.**
A checker that answers "fine" for what it did not examine is worse than
no checker, because it converts an unknown into a false assurance. The
summary line reports all three counts, always, and a run whose
UNSUPPORTED count is unstated is a failed run.

## 7. Independence rules

The rechecker earns its keep only by not sharing the mistake. Binding
constraints:

- **It may reuse the foreword** (List, Text, Map, the IR text reader).
  Shared data structures are not shared judgement, and reimplementing
  `list-at` buys nothing.
- **It may NOT call, cite, or copy any module under
  `codex/compiler/Types/`.** No `unify`, no `type-mentions-proof`, no
  `check-*`. If a rule is needed, it is re-implemented from the rule as
  documented, not lifted.

  **As literally written this rule cannot be satisfied by any plug, and
  the fix is to name judgement rather than a directory** (measured
  2026-08-03). `Build-TranspilerPlug` in
  `codex/plugs/common/plug-build-lib.ps1` bundles
  `codex/compiler/Types/CodexType.codex` into **every** plug, along with
  `Name`, `SourceText`, `AstNodes` and `IRChapter`, because there is one
  declaration of those types in the tree and the plug needs it to speak
  about the wire at all. `IRTextParser`, which section 7 explicitly
  permits, produces `CodexType` values, so forbidding the chapter
  forbids reading the IR. The rule as written rules out the vehicle the
  design chose in section 4.

  What the chapter actually contains, measured: the `CodexType` variant
  declaration, the `EffectRow` records and their constructors, the
  overflow and real-mode enums, and about a dozen one-line accessors.
  There is no `unify`, no `check-*`, no assignability and no subtyping.
  So the binding form of the rule is:

  **The rechecker may use `CodexType` as a DATA declaration -- construct
  it, match on it, compare it structurally -- and may NOT call any
  function that encodes a compatibility, ordering or transparency
  DECISION.** Named today, so the list can be checked rather than
  interpreted: `strip-unit-ty` (encodes when a unit type is transparent
  to its inner type), `real-mode-rank` (encodes an ordering used to pick
  a result mode) and `is-pointer-type`. Type equality, assignability and
  every narrowing rule are re-implemented from `docs/DevelopersGuide.md`.
  If that list grows, it grows here, and a function not on it that turns
  out to encode a decision is a finding against this section.
- **It should be written by an agent who did not write the checker it
  is checking**, and from the DevelopersGuide plus the language docs
  rather than from `TypeChecker.codex`. An implementation transcribed
  from the original inherits the original's misreadings.
- Where the documented rule is ambiguous, the rechecker records the
  ambiguity as a finding and answers UNSUPPORTED. It does not resolve
  the ambiguity by consulting the compiler.

## 8. What tests the rechecker

This is the section that answers "who watches the watcher", and it is
not answered by adding another watcher.

A rechecker that agrees with the compiler on every input in the tree is
**indistinguishable from a program that returns AGREE unconditionally**.
Agreement is not evidence; sensitivity is. So the rechecker ships with
its sensitivity measured, using the convention this project already
uses for stage-0 probes (see `ProofTotalityProbe.md` section 2): write
the artifact that SHOULD be rejected, confirm it is, pin it.

**The mutation corpus.** A generator takes valid IR from the tree and
applies single, targeted corruptions, each of which the rechecker MUST
report as DISAGREE:

- widen a bounded integer past its declared `hi`
- drop an effect from an `effectful` row whose body still performs it
- use a linear binding twice; drop one on one branch of an `if`
- apply a constructor to a payload of the wrong type
- swap two field types in a record
- reference a name no definition binds
- (stage 4) replace a proof body with a self-reference; cite the claim
  under proof as its own lemma

**A mutation whose two candidate readings agree scores nothing** (measured
2026-08-03, and it had to be fixed twice). The first bounds mutation
narrowed a declared return to `0..5`, which a rechecker catches whether
it derives the product or merely reads the binary node's type, because
both `0..10` and `0..20` exceed `0..5`. At `0..15` the readings diverge
and only a real derivation fails it. The first constructor mutation had
the same defect from the other side: it named something that binds
nowhere, so the unbound-name arm caught it and the arm under test never
ran, and the corpus reported 9 of 9 with one check unexercised.

**So the corpus is not finished when every row says CAUGHT. It is
finished when each row can only be caught by the thing it names.** The
cheap check is mechanical: write down what the OTHER candidate answer
would be, and if it is the same answer, the mutation is decoration.
Sabotaging the check and requiring exactly the predicted rows to move is
the confirmation, and it is worth the rebuild -- it is the difference
between a corpus that measures a rechecker and one that measures whether
a rechecker exists.

**The kill-rate is the deliverable, not the rechecker.** A run reports
mutations caught over mutations planted, per class. A class at less than
100 per cent is a hole in the rechecker, stated in the report rather
than discovered later. A rechecker with no published kill-rate is not
evidence of anything and should not be cited in any claim document.

Note the corpus is also independently useful: any mutation the
**compiler** accepts is a compiler soundness bug found directly, without
the rechecker agreeing or disagreeing about anything.

## 9. Where it runs

A differential harness, off the critical path: for each definition in
the selfhost and in `codex/test`, compile to IR text, recheck, report
the disagreement set and the three counts. Run it on seed rebuild and
on demand -- the natural cadence is the same moment the seed is proven,
since that is when a divergence matters.

It joins `build/build.ps1` only after it has run clean over the whole
tree with zero false positives across at least two seed generations,
and never before its kill-rate is published. A checker bug that turns
into a fleet-wide build outage costs more than the bug it would have
caught.

### Running it

```powershell
pwsh codex/plugs/recheck/build.ps1
pwsh codex/plugs/recheck/kill-rate.ps1     # expect control PASS
pwsh codex/plugs/recheck/sweep.ps1 -Limit 0
```

- **`-Dir` is ONE directory level, not a recurse.** `-Dir codex/foreword`
  sweeps nothing, and `-Dir codex/compiler` sweeps exactly one chapter
  because only `opening.codex` sits at that level. **Sweep the quires, not
  the root**, and run the foreword as one run per leaf directory. A run
  over zero chapters now exits 5 rather than printing a green summary, and
  a shard that dies exits 4 rather than being silently dropped by the
  merge; both were silent before 2026-08-06, and that guard earned its
  keep on its first real use.
- **The whole compiler goes through in ONE payload.** Chapter-wise sweeping
  stalls around a fifth of it because compiler chapters cite their siblings
  and will not compile standalone; concatenating `codex/compiler` and
  passing it as a single `-Src` is what that always needed.
- `sweep-all.ps1` shards N-wide on private ports from 9250 and does not use
  9100. **Serial `sweep.ps1` still binds fleet-shared TCP 9100 and refuses
  to start when held** (L-SHARED); kill the holder rather than waiting.
- **A sweep summary is PARTIAL whenever the plug's payload ceiling bites,
  and it does not say so unless somebody makes it.** Do not compare sweep
  counts across seeds without stating which run carried unchecked chapters,
  and re-measure the ceiling rather than quoting it.
- **Re-baseline against the CURRENT source before quoting any delta.** One
  before/after was confounded by an older seed and older source with
  definitions moved underneath it; the axis being watched happened to read
  the same in both, which made the confound invisible while another count
  was off by thousands.

### Where the rechecker stands

Whole compiler as ONE payload, `-Passes none`, against the shipped seed:
**AGREE 4862, DISAGREE 0, UNSUPPORTED 0, IMPROVED 0, SINGLE-WITNESS 0.**
Kill-rate **27 of 27** with a passing control. The rechecker raises exactly
one finding against the whole compiler, the underived range in
`compile-type-check`, which is stage 2 and a separate question.

**Two different units are in play and they have been quoted
interchangeably.** The verdict line counts one verdict per definition per
stage, so its total is the definition count times three. The by-kind table
counts individual FINDINGS, and one definition can carry many. Say which
unit before quoting either.

### What the abstention set measures: the LANGUAGE, not the checker

An abstention is not a thing this rechecker is too weak to decide. It is a
place where `docs/DevelopersGuide.md` publishes no rule, and section 7
instructs exactly this response: where the documented rule is ambiguous,
record the ambiguity and answer UNSUPPORTED.

**So the rechecker has a second product besides the disagreement set, and
it is the one that has paid.** Sweeping for abstentions is an instrument
for finding unspecified corners of the language. Four rules were published
out of this lane's abstentions and the guide now carries them: "Type
Variables", "Integer literals", "Variance of Type Arguments" and "Overflow
mode is not part of type identity".

**This is section 1's sentence biting from an angle the design did not
anticipate.** "Safety claims are compiler-enforced, not aspirational" reads
as a claim about enforcement; it is also a claim about a LANGUAGE, and
where the language is defined only by the implementation that checks it, a
second implementation cannot be independent. It either abstains, or it
reads `TypeChecker.codex` and inherits whatever that says, which section 7
forbids precisely because agreement would then prove nothing.

**Do not close an abstention by writing the rule out of
`TypeChecker.codex`.** That converts the guide into a transcription of the
implementation, and then the rechecker agreeing with the compiler means
only that both read the same file. The rule is decided, then published,
then both implementations are checked against it.

### Two kinds of missing rule, and only one of them is a ruling

**Standard and merely unwritten**: that a type variable parameter admits
any argument, that occurrences of one variable in a signature must agree,
how a variable is scoped, how a parametric type is declared. Writing these
down records what the language already does. Low risk.

**A genuine design decision, and it has teeth.** Variance of type arguments
is RULED INVARIANT and enforced strictly (Damian, 2026-08-08). `list-set-at`
is a builtin, so `List a` is mutable in place and covariance of a type
argument is unsound in the classic way: accept
`List (Integer between 0 and 10)` where `List (Integer between 0 and 20)`
is wanted, store 15 through the wider view, and the narrower view's bounds
claim is false. Bounded integers are one of the four claims in section 1.
`rc-ty-fits` compares type arguments by equality. The design record is
`docs/Designs/Done/Language/TypeVariableRules.md` R3.

**The test that separates a ruling from a measurement**, because this lane
will face it again: ask whether the two candidate answers differ in what
the COMPILER ALREADY DOES. Variance was a ruling, because the compiler
accepted both readings and something had to choose. Overflow mode was not,
because the compiler had already chosen consistently in every position and
the only missing thing was the sentence saying so.

### The soundness rules for substitution, which is what the checker rests on

The scope is the APPLICATION SPINE, and that is measured rather than
chosen. Type-variable ids belong to the CALLEE'S SIGNATURE and are
reproduced verbatim at every call site: two comprehensions in one
definition carry the same ids, and so does an unrelated definition's.
**A definition-scoped map keyed by tvar id is therefore unsound** -- two
independent `map-list` calls in one body both bind tvar 23, so differing
instantiations either raise a false `apply-tvar-inconsistent` or, worse,
let the first call's instantiation silently decide the second's
comparison.

1. **What escapes a spine is a substituted TYPE, not a binding.** The map
   lives for one spine, is applied to that spine's recorded result type,
   and is released. Nothing keyed by tvar id crosses a spine boundary, so
   ids cannot collide, and rule 8's reclaim point stays the spine.
2. **A variable may only be bound from a source INDEPENDENT of the
   comparison that binding will decide.** Admissible: an argument's
   recorded type matched against the callee's declared parameter, and a
   lambda argument's BODY type. Inadmissible: the spine's own recorded
   result type, when the comparison being decided is the result. NEVER the
   parameter under test -- binding the argument's variable from the
   parameter it flows into closes every such comparison and makes all of
   them vacuous.
3. **Consistency across witnesses is what gives the substitution teeth.** A
   variable with two witnesses that disagree is a real
   `apply-tvar-inconsistent`. **That path has never fired anywhere in this
   tree**, and it is said rather than left to be assumed: SINGLE-WITNESS
   read 92 of 92 when the substitution landed, because where a polymorphic
   call carries enough information for two witnesses the compiler ALREADY
   instantiates it and the wire holds no variable at all. The guard is
   present, unfired, and untested by anything.
4. **Vacuity must be COUNTED, not argued away.** A variable with exactly
   one witness makes the comparison it decides tautological in that
   direction, so the run reports SINGLE-WITNESS alongside the abstention
   count. Without it, "abstentions fell" cannot be told apart from
   "checking improved", which is L-CAPABILITY-LOST in a new costume.
5. **Fuel.** `rc-match` is capped at 32. A spine needs its own cap and must
   answer unknown rather than recurse; a curried call of many arguments is
   one spine.
6. **Effect variables are open and deliberately unanswered.** The
   substitution leaves `FunTy`'s row untouched, so nothing here bears on
   `e`. The stage-3 reading of UNSUPPORTED 0 before and after says no
   effect-variable abstention is VISIBLE, not that none exists: section 4
   records that only rows carrying concrete labels are published and that a
   bare row variable never reaches the wire at all. An instrument that
   cannot see the thing has nothing to say about it.

**The witness rule, for taking a branching node's type from its arms.** One
arm's type may stand for the whole node only where the unifier admits no
widening. It DOES admit widening for integers and reals at a non-argument
position, where merely overlapping ranges unify, so two arms can
legitimately carry different bounds and the first is not the join.
`ty-admits-widening` refuses those; every other form is nominal or
invariant, so arms that unified against one variable cannot differ. A
too-narrow recorded bound is exactly the silent wrongness this lane exists
to catch.

### The fork underneath this, and it is Damian's call

The alternative to deriving the instantiation is to fix the ARTIFACT: have
the compiler emit the instantiated type at the site, since it already
computed it.

- **For emitting.** L-ERASED is the precedent. A rule the compiler enforces
  but does not emit is invisible to any auditor reading the artifact, and
  the repair there was to make the artifact carry the distinction. Every
  auditor written against this IR, now and later, gets it free.
- **For deriving.** This lane exists for INDEPENDENCE. An auditor that
  recomputes the instantiation catches a WRONG one; an auditor that reads
  the compiler's own answer agrees with it by construction, which is the
  failure section 7 exists to prevent.
- **Cost.** Deriving is plug-only and reversible. Emitting is
  seed-affecting, needs the build token and a gate, and enlarges the IR.

**They are not exclusive, and the strongest position is both**: the
compiler publishes its instantiation, the rechecker derives one
independently, and they must agree. The DERIVING half is built; nothing
about it forecloses emitting, because the derivation is what would check an
emitted instantiation. Whether this artifact is meant to be
SELF-DESCRIBING or merely AUDITABLE is a ruling rather than a measurement,
both readings are consistent with everything the compiler does today, and
it blocks nothing.

### What this lane has learned the expensive way

- **When a change alters HOW a comparison is decided, the corpus needs a
  new arm before the change is believed, not merely a re-run of the
  existing ones** (L-CAPABILITY-LOST). A rejected stage-2 fix fixed the
  callee's variables from the site's recorded result type: it cleared all
  three disagreements, held the whole compiler at DISAGREE 0, left the
  abstention counts untouched and scored 21 of 21 on the corpus. It was
  also blind to `fresh-row-id`, the one real compiler defect this lane has
  found, because with the variable fixed from the site a bounded argument
  flowing into a plain declared slot only has to FIT. **Every aggregate
  measurement this lane owns was satisfied by a change that REMOVED a real
  capability**, and a checker that stops asking a question reports the same
  numbers as one that asks and agrees. The arm that separates them,
  `bounded-arg-into-plain-slot`, had to be written for the occasion.
- **A control that behaves correctly is evidence about the DIFFERENCE
  between the arms, and the difference can be a defect in the arm you
  called normal** (L-CONTROL). `control-let`, the same comprehension bound
  through a `let` first, came out clean while the lambda form abstained,
  and that was read as a property of the lambda. It was pointing at
  `lower-lambda` the whole time: the compiler recorded the EXPECTED type it
  was handed as a lambda's type rather than the resolved one, so
  `subst-type-vars-from-arg` matched `(fn (tvar 24) (tvar 25))` against
  itself and learned nothing. The compiler resolved the instantiation and
  then discarded it, and the application's result type reached the wire
  uninstantiated for every consumer: this rechecker, every transpiler plug,
  and any auditor written later.
- **When two implementations disagree, suspect yours first** (L-MYSIDE).
  All three of the first stage-2 disagreements were the checker's, in one
  line: `rc-expr-ty` typed an integer literal as `IntegerTy v v` where the
  compiler types it plain `Integer`, so a literal in a tuple pinned the
  tuple's element type and invariance refused a well typed definition.
- **`UNSUPPORTED must never render as AGREE`, and the verdict site is the
  instrument, not the comparison.** Four sites consumed a comparison into a
  verdict and did not agree with each other: two reported unsupported while
  two answered "undecided" and added no finding at all, and a definition
  with no finding falls through to the AGREE default arm. Roughly 12 per
  cent of one directory had been counted as agreement while undecided.
  Predicting a verdict from the comparison without reading the consumer is
  how it survived. When a checker is tuned quiet, deciding a large
  population of previously abstaining comparisons and still reporting zero
  disagreements is exactly the shape to distrust.
- **Two disciplines from the kill-rate corpus.** A mutation whose two
  candidate readings AGREE scores nothing while reading as CAUGHT: write
  down what the OTHER candidate answer would be, and if it is the same
  answer the mutation is decoration. A mutation also needs an explicit
  `Kind` or it can never match its own label and always scores "caught but
  reported under another class". And sabotage the check itself, requiring
  EXACTLY the predicted rows to move, which is what separates a corpus that
  measures a checker from one that measures whether a checker exists.
- **`run.ps1` served a STALE IR twice**, because it gated the compile on
  `error CDX` while an unresolvable cite is `error 3010:` with no prefix.
  The IR is now deleted before the compile and the pattern matches the
  number form, so a silent failure is an ABSENT IR rather than a stale one.

## 10. Memory and time (rule 8)

- Single pass per definition over the IR text; no retained state across
  definitions. Nothing accumulates chapter to chapter.
- Environments are scoped to the definition being checked and released
  with it. No AST or IR is held across phases.
- Every structural walk is fuel-capped, and fuel exhaustion answers
  UNSUPPORTED, never AGREE. (Note this is the OPPOSITE of
  `type-mentions-proof`, which errs toward checking; here the safe
  direction is admitting ignorance.)
- Expected shape: O(size of IR), no nested walk over the definition
  list, no quadratic name resolution -- build one offset table per
  chapter, as `ir-dce-build-index` already does.
- Bare metal, no GC: every allocation is permanent until the producing
  function returns. The per-definition boundary is the reclaim point.

## 11. Risks and honest scope

- **It will disagree, and most early disagreements will be its own
  bugs.** That is expected and is why it does not gate. The failure to
  guard against is the opposite one: a rechecker quietly tuned until it
  agrees, which reintroduces the correlation it exists to break.
- **It does not make Codex proofs certain.** It makes a checker bug
  survivable by one more implementation, which is a different and
  smaller claim. Any statement in a claims document must say which.
- **Stages 3a and 4 are the seed-affecting work here.** Stage 4 can be
  deferred indefinitely. Stage 3a cannot be deferred without deferring
  the linear and effect claims with it, which are two of the four the
  README sentence in section 1 rests on. Neither blocks stages 1 and 2.
- **Scope discipline:** the fragment covered is whatever the kill-rate
  table says it covers, and nothing wider. UNSUPPORTED is a first-class
  answer precisely so the covered fragment can stay small and honest.

## 12. Required reading before building

Per the on-demand contract in `CLAUDE.md` step 5:

- `.codex` source conventions -- `docs/DevelopersGuide.md`
- test and probe conventions -- `docs/ExaminersAssay.md`
- builds, the VM, plug invocation -- `docs/OperatorsManual.md`
- the plug transport and how an existing plug is structured -- read one
  under `codex/plugs/` end to end before writing a new one

## 13. Cross-references

- `docs/PM/Done/ClaimsCalibration.md` -- the claim register; the
  README sentence in section 1 belongs there with whatever the
  kill-rate ends up being
- `docs/PM/Done/TrustedComputingBase.md` -- section 4 ranks the proof
  layer; this document is an instrument for that ranking, not a change
  to it
- `docs/Designs/Done/Language/ProofTotalityProbe.md` -- the probe
  convention section 8 builds on, and the CDX4023/CDX4024 properties
  stage 4 re-checks
- `codex/compiler/Emit/IRTextEmitter.codex` -- `ir-emit-type`, the
  input format
- `codex/compiler/opening.codex:1414` -- IR text path (proofs pruned)
- `codex/compiler/opening.codex:1165-1168` -- CDX path (proofs kept)
