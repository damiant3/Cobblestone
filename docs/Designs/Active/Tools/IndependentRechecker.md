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
`empty-list-element-type` case is its runner, and reports DROPPED.

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

### The abstention set, re-measured and split (2026-08-08, val)

Whole compiler as ONE payload, `-Passes none`, against seed
`43189C1E7D762144`, kill-rate 18 of 18 with a passing control before the
run. **4720 definitions, AGREE 13776, DISAGREE 0, UNSUPPORTED 384**, in
257 s.

**Two different units are in play and they have been quoted
interchangeably.** `13776 + 0 + 384 = 14160 = 4720 x 3`, so the verdict
line counts one verdict per definition per stage. The by-kind table
counts individual FINDINGS, and those total 1365. A definition can carry
many findings. Say which unit before quoting either.

The five classes re-measured EXACTLY as previously recorded, so these
counts are confirmed rather than stale: `apply-arg-int-bounds` 198,
`apply-arg-undecided-nested` 262, `apply-arg-undecided-opaque-param` 403,
`apply-result-undecided-nested` 401, `bounds-underived` 101,
`no-wire-type` zero.

**The largest class was a category, not an assertion, so it was split.**
`rc-ty-opaque` answers True for eight constructors, and the label said
only "opaque". `rc-ty-opaque-kind` now names which one. Re-swept with the
split, every total held identical (4720 / 13776 / 0 / 384, 1365
findings), which is the control proving the split moved no verdict:

**All 403 are `tvar`. Not one is `error-atom`, `forall` or `foralleff`.**

So the largest abstention class is entirely PARAMETRIC POLYMORPHISM, not
lost or erroneous types. A parameter typed `TypeVar i` accepts any
argument by parametricity, and the checker is abstaining where the rule
is not actually ambiguous.

**It does not follow that these can be decided, and the one-line version
is the trap section 9 already warns about.** Answering TyEq whenever the
parameter is a type variable would decide each argument INDEPENDENTLY,
and the thing that makes an instantiation wrong is a relationship BETWEEN
argument positions: `f : a, a -> a` applied to an Integer and a Text is
inconsistent, and neither argument is wrong on its own. `rc-same-tvar`
covers only the case where both sides are already the same variable.

Deciding this properly means tracking a substitution from type variables
to types across an application and requiring consistency, which is a
stage of work, not a relaxation. **The naive version converts an honest
UNSUPPORTED into a false AGREE on exactly the inputs it cannot judge**,
which section 6 forbids in as many words. Today's answer is the correct
one for a checker that does not yet track substitution, and the 403 is a
statement about a missing capability rather than a defect.

Whoever takes it: the corpus needs a mutation that instantiates one type
variable two ways, and that mutation must be confirmed SILENT before the
change and CAUGHT after. Without that arm the change cannot be
distinguished from tuning the checker quiet.

**That instruction could not be followed as written, and the reason is
itself a finding (2026-08-10).** A mutation instantiating one variable two
ways needs a site where one variable has TWO witnesses, and no such site
exists in this compiler: SINGLE-WITNESS reads 92 of 92. Where a polymorphic
call carries enough information for two witnesses the COMPILER ALREADY
INSTANTIATES IT and the wire holds no variable at all, so the shape that
could hold the conflict is exactly the shape that never reaches the checker.
The arms written instead satisfy the requirement behind the instruction --
silent before, caught after -- by corrupting the CONSUMING PARAMETER rather
than the instantiation.

### After the variance ruling (2026-08-08, val)

Whole compiler as one payload, `-Passes none`, seed `065D92E60292492D`,
kill-rate 19 of 19 with a passing control:

**4721 definitions, AGREE 13780, DISAGREE 1, UNSUPPORTED 382.**

**The rechecker's first DISAGREE against the compiler.** It is
`fresh-row-id` (`Types/Unifier.codex`), reported `apply-result-type`: the
definition binds `st.next-row-id`, declared
`Integer between 0 and 4294967295`, and returns it as the first element of
a declared `(Integer, UnificationState)`. Under R3 that widening inside a
type argument is refused; the compiler accepts it. Section 3 governs what
this is -- a bug report against one of the two implementations, unresolved
until a human reads it -- and it is NOT a licence to soften the rule.

`-variance` abstentions read zero, against 1 before, which is the control
proving the rule reaches every widening rather than the one site it was
aimed at.

**No delta against the counts above is quoted, deliberately.** They were
taken against seed `AEB5ED2B5043C7C1` and this run is
`065D92E60292492D` with the source moved underneath it, which is the
re-baselining confound this section already warns about.

### The stage-2 disagreements, adjudicated (2026-08-09, val)

Substitution tracking made the checker decide 1062 comparisons it used to
decline, and the first sweep reported **DISAGREE 3 over 13 sites**, all
`apply-result-type`. **All three were the checker's, in one line**
(L-MYSIDE): `rc-expr-ty` typed an integer literal as `IntegerTy v v` where
the compiler types it plain `Integer`, so a literal in a tuple pinned the
tuple's element type and invariance refused a well typed definition. `[v,
v]` is the literal's VALUE, it answers the bounded-parameter question, and
`RecheckBounds` already uses it for exactly that. The account and the
measurements are in
`docs/Designs/Done/Language/TypeVariableRules.md` section 5; the language
rule is now published as `docs/DevelopersGuide.md` "Integer literals".

**The part that belongs in THIS document is what nearly shipped instead.**
The first fix reversed the substitution direction, fixing the callee's
variables from the site's recorded result type. It cleared all three
disagreements, held the whole compiler at DISAGREE 0, left the abstention
counts untouched at 184 and 86, and scored 21 of 21 on the corpus. It was
also blind to `fresh-row-id`, the one real compiler defect this lane has
found: with the variable fixed from the site, a bounded argument flowing
into a plain declared slot only has to FIT, and it does.

**Every aggregate measurement this lane owns was satisfied by a change that
removed a real capability.** The sweep cannot see it, DISAGREE 0 cannot see
it, and the abstention count cannot see it, because a checker that stops
asking a question reports the same numbers as one that asks and agrees.
Only a mutation aimed at the direction being changed separated them, and
the arm that does it (`bounded-arg-into-plain-slot`) had to be written for
the occasion. **Section 8's rule therefore has a sharper form: when a
change alters HOW a comparison is decided, the corpus needs a new arm
before the change is believed, not merely a re-run of the existing ones.**

### What the abstention set actually measures: the LANGUAGE is unspecified

**Reframed 2026-08-08 at Damian's reading, and it holds up when
measured.** The abstentions are not a list of things this rechecker is
too weak to do. They are a map of where `docs/DevelopersGuide.md`
publishes no rule, and section 7 instructs exactly this response: where
the documented rule is ambiguous, record the ambiguity and answer
UNSUPPORTED.

Measured against the guide:

- **Type variables are never introduced.** The `## Types` table lists 16
  forms and not one is a type variable or a parametric type declaration.
  `List Integer` and `Maybe Text` appear only as USES of types that are
  already parametric.
- **Their first and only appearance is inside an example about something
  else.** Line 135 writes `map : (a -> b), List a -> List b` to
  illustrate higher-order parameters. Nothing says what `a` is, what
  scopes it, or that a signature may introduce one.
- **No rule for applying a polymorphic function.** Nothing states that
  occurrences of one variable must be instantiated consistently, which
  is the rule substitution tracking would need.
- **No variance rule for type arguments**, which is the gap already
  recorded against `rc-nominal-fits`.
- **No rule for declaring a parametric type.** `Box (a) = | Boxed (a) |
  Empty` compiles and the AST carries `tps`, but the guide never shows
  the form.

**This is the sentence in section 1 biting from an angle the design did
not anticipate.** "Safety claims are compiler-enforced, not
aspirational" was read as a claim about enforcement. It is also a claim
about a LANGUAGE, and where the language is defined only by the
implementation that checks it, a second implementation cannot be
independent: it either abstains, or it reads `TypeChecker.codex` and
inherits whatever that says, which section 7 forbids precisely because
agreement would then prove nothing.

**So the rechecker has a second product besides the disagreement set,
and today it is the more valuable one, because the disagreement set is
empty and this is not.** Sweeping for abstentions is an instrument for
finding unspecified corners of the language.

### Two kinds of missing rule, and only one of them is a ruling

Separating these matters, because the first is cheap and the second is
not anyone's to decide unilaterally.

**Standard and merely unwritten.** That a type variable parameter admits
any argument, that occurrences of one variable in a signature must agree,
how a variable is scoped, how a parametric type is declared. Writing
these down is recording what the language already does. Low risk, and it
unblocks the 403.

**A genuine design decision, and it has teeth. RULED 2026-08-08 by
Damian: INVARIANT, enforced strictly.** `list-set-at` is a builtin, so
`List a` is mutable in place, and covariance of a type argument is unsound
in the classic way: accept `List (Integer between 0 and 10)` where
`List (Integer between 0 and 20)` is wanted, store 15 through the wider
view, and the narrower view's bounds claim is false. Bounded integers are
one of the four claims in section 1.

The rule is published in `docs/DevelopersGuide.md` "Variance of Type
Arguments" and the rechecker enforces it, so `rc-nominal-fits` is gone and
`rc-ty-fits` compares type arguments by equality. Abstaining was the
correct response to silence; it is not the correct response to a published
rule. The design record is
`docs/Designs/Done/Language/TypeVariableRules.md` R3.

**Do not close this by writing the rules out of `TypeChecker.codex`.**
That converts the guide into a transcription of the implementation, and
then the rechecker agreeing with the compiler means only that both read
the same file. The rule is decided, then published, then both
implementations are checked against it.

### Two disciplines from the kill-rate corpus

- **A mutation whose two candidate readings AGREE scores nothing while
  reading as CAUGHT.** Write down what the OTHER candidate answer would be,
  and if it is the same answer the mutation is decoration. A mutation also
  needs an explicit `Kind` or it can never match its own label and always
  scores "caught but reported under another class".
- **Sabotage the check itself and require EXACTLY the predicted rows to
  move.** That is what separates a corpus that measures a checker from one
  that measures whether a checker exists.

### The failure this lane exists to prevent, and it happened here

`UNSUPPORTED must never render as AGREE.` Four sites consumed a comparison
into a verdict and did not agree with each other: two reported unsupported
while two answered "undecided" and added no finding at all, and a definition
with no finding falls through to the AGREE default arm. An undecided
application argument was therefore being COUNTED AS AGREEMENT. Measured when
the two silent sites were made to report: roughly 12 per cent of one
directory had been counted as agreement while undecided.

**The lesson is L-FALSIF one level up: the comparison was not the
instrument, the verdict site was.** Predicting a verdict from the comparison
without reading the consumer is how it survived. When a checker is tuned
quiet, deciding a large population of previously abstaining comparisons and
still reporting zero disagreements is exactly the shape to distrust; add a
mutation that was silent before the change and require it to be caught.

### The rechecker's first real compiler defect: `lower-lambda` (2026-08-10)

**This lane exists to find a defect in the compiler that no test can see, and
it has now found one.** It is not a soundness hole -- every program compiled
correctly throughout -- but it is a defect in the artifact the compiler
publishes, and it is exactly the shape section 1 describes: silent, invisible
to the battery, findable only by something that reads the IR and asks whether
it says what the compiler knows.

`IR/Lowering.codex`, `lower-lambda`, recorded the **expected** type it was
handed as the lambda's type. At a polymorphic call that is the callee's
declared parameter with its type variables still in it, recorded at the
moment the body has just been lowered and its concrete type is in hand.
`lower-let` twenty lines above records `deep-resolve (ctx.ust) (ir-expr-type
val-ir)` -- the resolved type.

The consequence was not local, and that is why it mattered.
`subst-type-vars-from-arg` at every call site learns the callee's variables
by matching its declared parameter against the argument's recorded type. A
lambda argument therefore matched `(fn (tvar 24) (tvar 25))` against itself,
substituted every variable for itself, and learned nothing. **The compiler
resolved the instantiation and then discarded it**, and the application's
result type reached the wire uninstantiated for every consumer: this
rechecker, every transpiler plug, and any auditor written later.

Fixed by recording the lambda's actual type, reusing the compiler's own
`subst-type-vars-from-arg` rather than adding a primitive, guarded twice:
nothing happens unless the recorded type has variables, and nothing happens
when the body's own type has variables, since substituting a variable for a
variable buys nothing. **No change to `ir-emit-type` was needed** -- the
emitter always printed the node's type, and the node is now right.

| | before | after |
|---|---|---|
| lambda node | `(fn (tvar 23) (tvar 24))` | `(fn (tvar 23) (sum "Tree"))` |
| application result | `(list (tvar 24))` | `(list (sum "Tree"))` |

**Whole compiler after the fix: AGREE 4821, DISAGREE 0, UNSUPPORTED 0,
IMPROVED 4.** 88 of the 92 close in the ARTIFACT, the rechecker
independently derives the remaining 4, and the two implementations agree.
That is the "both" position of the fork below, reached by measurement rather
than chosen. Gate green, hard fixed point in one pass.

**The 4 that remain are the same defect one level down**, deliberately not
fixed here: an `if`- or `when`-bodied lambda. `fold-expr`,
`inline-leaf-calls-in-chapter`, `inline-single-caller-in-chapter`,
`inline-cost-based-in-chapter`. (This paragraph also said the body's "own
recorded type is the bare variable, so there is nothing concrete to
substitute." That was wrong; see the section below, which closes them.)

### The last 4: the branching node, not the body (2026-08-14)

**CLOSED, 4 to 0** (val 15022, main 15023, seed-affecting, seed converged at
2760410). The rechecker now raises exactly ONE finding against the whole
compiler: the underived range in `compile-type-check`.

**The recorded reason was wrong, and that is the part worth keeping.** The
branch bodies were never bare variables. In `fold-expr`'s
`for stmt in ss -> when stmt is IrDoBind ... is IrDoExec ...` both arms type
as `(sum "IRActStmt")`, concrete and identical, and the node discarded them:

```
lower-match    result-ty = when ty is ErrorTy -> infer-match-type branches
                                    is otherwise -> ty
lower-expr-at  result-ty = merge-ty hint-0 else-ty     -- answers `a` unless
                                                          `a` is ErrorTy
```

`infer-match-type` ALREADY derives the type from the arms, and was consulted
only when there was no expectation at all. A bare type variable is not
ErrorTy, so it took the other arm. The instrument that settled it was a
probe returning a type that could never legitimately appear (`BooleanTy`)
from the new helper: the node came back `boolean`, which proved the path ran
and moved the question from "is my code reached" to "why is the witness
rejected".

| | before | after |
|---|---|---|
| `fold-expr`'s `stmt` lambda | `(fn (tvar 72) (tvar 73))` | `(fn (tvar 72) (sum "IRActStmt" (args)))` |
| the three `inline-*-in-chapter` | `(fn (tvar 72) (tvar 73))` | `(fn (tvar 72) (record-ty "IRDef" (args)))` |

`lower-lambda` needed no change: its existing substitution propagates the
now-concrete body type to the lambda for free.

**The witness rule.** One arm's type may stand for the whole node only where
the unifier admits no widening. It DOES admit widening for integers and reals
at a non-argument position -- merely overlapping ranges unify -- so two arms
can legitimately carry different bounds and the first is not the join.
`ty-admits-widening` refuses those; every other form is nominal or invariant,
so arms that unified against one variable cannot differ. A too-narrow recorded
bound is exactly the silent wrongness this lane exists to catch.

**`types-equal` could not compare two identical sum types, and that is fixed
rather than documented** (`Types/Unification`, same CL). It had arms for the
primitives, `UnitTy`, `VectorTy`, `TypeCon` and `TypeApply` and fell through
to `otherwise -> False` for the other ten variants, so the first attempt at
the guard above rejected every witness and a working fix measured as no change
at all. It was never a soundness gap -- its only caller is the short-circuit
at `unify-resolved`, where `False` merely declines a fast path -- but a
predicate that answers "not equal" for two identical types is a trap for the
next caller whatever its call site does today.

**Whole compiler, `-Passes none`, against the shipped seed: AGREE 4862,
DISAGREE 0, UNSUPPORTED 0, IMPROVED 0, SINGLE-WITNESS 0** (was IMPROVED 4,
SINGLE-WITNESS 4). Kill-rate **27/27** with a passing control, and
`tvar-spine-branch-arms` is still CAUGHT -- unlike the lambda fix, this one
costs the rechecker no arm.

**`run.ps1` could serve a STALE IR and did, twice, during this work.** It
gated the compile on `error CDX`, but an unresolvable cite is `error 3010:`
with no prefix, so a failed compile passed the check and `last-run.ir` from a
previous session was rechecked and reported as a verdict -- a plausible,
entirely fictitious "before" reading, once from a four-day-old artifact. The
IR is now deleted before the compile and the pattern matches the number form,
so a silent failure is an ABSENT IR rather than a stale one.

**A control was the evidence and it was read as a fact.** `control-let`, the
same comprehension bound through a `let` first, came out clean while the
lambda form abstained; the 2026-08-09 account below calls that "the row that
pays" and concludes the gap is about recorded types. It was pointing at
`lower-lambda` the whole time. **A control that behaves correctly is evidence
about the DIFFERENCE between the arms, and the difference can be a defect in
the arm you called normal** rather than a property of the one under study.
Recorded as L-CONTROL.

**The fix cost an arm, and that is recorded rather than absorbed.**
`tvar-spine-substitution` was confirmed MISSED-before and CAUGHT-after
against the old compiler, and is DECORATION against the new one: with
`rc-arg-spine` forced to the empty map it is still caught, because its
argument now arrives concrete. Retired in `kill-rate.ps1` with that
measurement written beside it so nobody re-adds it.
`tvar-spine-branch-arms` stays live and covers both halves of the
substitution, since its result variable is fixed at the inner node and
consumed on the outer node's recorded type.

### The 92 nested type variables: CLOSED 2026-08-10, 92 to 0

Built as designed below: spine-scoped substitution, plug only, no compiler
change and no seed. Whole compiler as one payload, `-Passes none`, kill-rate
**28 of 28 with a passing control** before the run.

**Stage 1 is AGREE 4820, DISAGREE 0, UNSUPPORTED 0**, against 4776 / 0 / 44
before. Findings 93 to 1, and the one that remains is the pre-existing
`bounds-underived` in `compile-type-check`, which is stage 2 and another
question. Elapsed 254 s against a 255 s baseline, so the walk costs nothing
measurable.

**IMPROVED 92, SINGLE-WITNESS 92, and that second number is the honest half
of the result.** Every comparison the substitution decided was decided by a
variable fixed from exactly ONE witness, which makes it tautological in that
direction; the counters are printed on the stage line precisely so a fallen
abstention count cannot be read as more than it is. What the change buys is
not a constraint between witnesses but a CONCRETE TYPE where there was a
variable: `Node`'s parameter now meets `(list (sum "Tree"))` instead of
`(list (tvar 25))`, and that comparison can fail. Two mutations say so.

**The conflict path has never fired anywhere in this tree, and that is said
rather than left to be assumed.** `apply-tvar-inconsistent` from a spine
needs one variable with two disagreeing witnesses, and SINGLE-WITNESS 92 of
92 means no site in the compiler has two. `via-named` in the corpus is why:
written with a NAMED function in place of the lambda, the compiler
instantiates the call and the wire carries no variable at all, so the shape
that could hold two witnesses is exactly the shape the compiler already
resolves. The guard is present, unfired, and untested by anything.

**Two arms, and each was confirmed MISSED before and CAUGHT after.**
`tvar-spine-substitution` is the spine itself; `tvar-spine-branch-arms` is
the witness taken one level deeper, for a lambda body the compiler records as
a bare variable. Both consume through an ORDINARY FUNCTION rather than a
constructor, because a constructor's declared payload is separately policed
by `ctor-ref-payload-type` and the same corruption there is caught with or
without any of this. Sabotage confirmed: disabling the branch descent moves
`tvar-spine-branch-arms` to MISSED and **exactly that row**, with
`tvar-spine-substitution` still caught, which is what says the two arms are
aimed at different mechanisms rather than one of them riding the other.

**The four sites the spine substitution alone could not close** were a
comprehension whose lambda body is an `if` or a `when`: the compiler records
that node as the bare `(tvar 73)` while every arm beneath carries a concrete
type. `fold-expr`, `inline-leaf-calls-in-chapter`,
`inline-single-caller-in-chapter` and `inline-cost-based-in-chapter`, all in
`IR/Lowering.codex`. The descent requires the arms to AGREE, which is the
soundness condition: taking one arm where another contradicts it would assert
a return the lambda does not have, and a wrong binding manufactures a false
disagreement, which is the direction this design exists to avoid.

**The unsound fix was not taken.** Binding the argument's variable from the
parameter it flows into closes all 92 and makes every such comparison vacuous.
Nothing below reads the consuming parameter: `rc-arg-spine` is handed the
argument and nothing else, and both witnesses live inside the argument's own
spine. `bounded-arg-into-plain-slot`, the arm that caught the 2026-08-09
rejected fix, still scores.

The design as written follows, unchanged except where the measurement
corrected it.

### The 92 nested type variables: DIAGNOSED 2026-08-09

R1 to R4 are published and the abstention set is 96 findings on the whole
compiler. **92 of them are one shape, and the nearest fix for it is
unsound**, which is why this is written down instead of closed.

Reproduced in nine definitions (`nest3.codex`, four arms, two of them
controls). A comprehension desugars to `map-list`, which reaches the apply
with its type variables STILL ON THE WIRE:

```
(apply (name "map-list" (fn (fn (tvar 23) (tvar 24))
                            (fn (list (tvar 23)) (list (tvar 24)))))
       ... (name "kids" (list (sum "Tree" (args)))) ...)
```

Its RESULT is recorded as `(list (tvar 24))`, uninstantiated, and that flows
into a concrete parameter, `Node`'s `(list (sum "Tree"))`. So the comparison
is a concrete parameter against an argument carrying an unbound variable,
and `rc-match` binds only PATTERN-side variables, so it answers Unknown and
the verdict abstains.

| arm | result |
|---|---|
| comprehension into a constructor | ABSTAINS |
| comprehension into an ordinary `List Tree` parameter | ABSTAINS |
| control: same call with no comprehension | clean |
| control: same comprehension bound through a `let` first | **clean** |

The last row is the one that pays for itself. It is the same expression
reaching the same constructor, and binding it through a `let` records a
resolved type, which is what says the gap is the recorded type on that node
rather than anything about constructors or comprehensions.

**The nearest reachable fix is to match the other way and bind the
ARGUMENT's variable from the parameter it flows into. It must not be
done.** It would close all 92 and make the check vacuous, because a variable
bound from the parameter fits that parameter by construction and every
argument would pass. That is the same error as the rejected stage-2 fix,
which scored 21 of 21 while blinding the checker to `fresh-row-id`
(L-CAPABILITY-LOST), and it would be invisible to every aggregate for the
same reason.

**What closing it soundly requires** is designed below.

### Design: spine-scoped substitution (proposed 2026-08-09, BUILT 2026-08-10)

**1. The scope is the APPLICATION SPINE, and that is measured rather than
chosen.** Type-variable ids belong to the CALLEE'S SIGNATURE and are
reproduced verbatim at every call site. Measured on a chapter with three
comprehensions:

| definition | tvar ids on the wire |
|---|---|
| `map-list`, the library definition | 23, 24 |
| `map-list-loop` | 25, 26 |
| `twice-in-one-def`, containing TWO comprehensions | **23, 24** |
| `other-def`, containing one | **23, 24** |

Both comprehensions in one definition carry the SAME ids, and so does an
unrelated definition's. **A definition-scoped map keyed by tvar id is
therefore unsound**: two independent `map-list` calls in one body both bind
tvar 23, so differing instantiations either raise a false
`apply-tvar-inconsistent` or, worse, let the first call's instantiation
silently decide the second's comparison. An earlier version of this section
said the substitution should be threaded across a definition; that reading
is wrong and this table is why.

**2. What escapes a spine is a substituted TYPE, not a binding.** The map
lives for one spine, is applied to that spine's recorded result type, and is
released. The consumer that currently abstains, `Node`'s parameter against
`(list (tvar 24))`, then sees `(list (sum "Tree"))` and the comparison is
real. Nothing keyed by tvar id ever crosses a spine boundary, so ids cannot
collide, and rule 8's reclaim point stays the spine.

**3. A variable may only be bound from a source INDEPENDENT of the
comparison that binding will decide.** This is the sentence whose absence
produced the rejected stage-2 fix, and it carries the soundness argument:

- **Admissible witnesses.** An argument's recorded type matched against the
  callee's declared parameter, and a lambda argument's BODY type, which
  gives the lambda's true return where its recorded type still says
  `(tvar 24)`.
- **Inadmissible.** The spine's own recorded result type, when the
  comparison being decided is the result. That is exactly "fix the callee's
  variables from the site", which cleared three disagreements, held
  DISAGREE 0 and scored 21 of 21 while blinding the checker to
  `fresh-row-id`.
- **Never** the parameter under test. Binding the argument's variable from
  the parameter it flows into closes all 92 and makes every such comparison
  vacuous.

Consistency across witnesses is what gives the substitution teeth: a
variable with two witnesses that disagree is a real
`apply-tvar-inconsistent`.

**4. Vacuity must be COUNTED, not argued away.** A variable with exactly one
witness makes the comparison it decides tautological in that direction. The
run must report how many comparisons were decided by a single-witness
substitution, alongside the abstention count. Without it, "abstentions fell"
cannot be told apart from "checking improved", which is L-CAPABILITY-LOST in
a new costume: the aggregate moves either way.

**5. Fuel.** `rc-match` is capped at 32. A spine needs its own cap and must
answer unknown rather than recurse; a curried call of many arguments is one
spine.

**6. Open, and deliberately not answered here: effect variables.** The
measured case carries none, and whether `(a -> [e] b)` needs the same
machinery for `e` is unmeasured. Do not assume it falls out.

**Still unmeasured after the build, and the reason is not reassuring.** The
substitution leaves `FunTy`'s row untouched, so nothing here bears on `e`.
Stage 3 reads AGREE 4820, UNSUPPORTED 0 both before and after, which says no
effect-variable abstention is VISIBLE, not that none exists: section 4
records that only rows carrying concrete labels are published and that a bare
row variable never reaches the wire at all. An instrument that cannot see the
thing has nothing to say about it.

### The fork underneath this, and it is a taste question

The alternative is to fix the ARTIFACT rather than the auditor: have the
compiler emit the instantiated type at the site, since it already computed
it. Then all 92 vanish with no checker change.

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

**They are not exclusive, and the strongest position is both**: the compiler
publishes its instantiation, the rechecker derives one independently, and
they must agree. That is the same shape as the rest of this lane and it is
also the most expensive. Doing the derivation first is not wasted if
emitting lands later, because the derivation is what would check it. If only
one is ever built, which one it should be is a judgement about whether this
artifact is meant to be SELF-DESCRIBING or merely AUDITABLE, and that is a
ruling rather than a measurement: both readings are consistent with
everything the compiler does today.

**The DERIVING half is built and the fork is still open.** Deriving was taken
first for the reason above: it is plug only, reversible, and it is the arm
that would check an emitted instantiation if one ever lands. Nothing about it
forecloses emitting. What emitting would still buy is every auditor written
against this IR later, and what it would cost is a seed-affecting change to
`ir-emit-type` plus a larger IR. That remains Damian's call and it is not
blocking anything.

### The 3 overflow-mode findings: CLOSED, and the routing was the mistake

**This section said they were a ruling of the same shape as variance and
that the lane should not take them alone. That was wrong**, and the
correction is worth more than the three findings: the measurement decided
it, and taking the measurement was the work being deferred. Closed at main
14456, 3 to 0, published as `docs/DevelopersGuide.md` "Overflow mode is not
part of type identity".

Mode is accepted in both directions at a parameter and inside a type
argument, while a BAND difference in the identical position is refused with
CDX2001. The invariance argument does not carry over: a band is a claim
about which VALUES a slot holds, a mode is a claim about what happens at an
OPERATION and is governed by the declared type at the site performing it.
`list-set-at` through a wide view falsifies a narrow view's claim about its
values; a `wrapping` view and an `error` view over one list cannot falsify
each other, because every mode keeps the value inside the band. That last
clause is the premise and the guide states it as the thing to attack.

**The test that separates a ruling from a measurement**, since this lane
will face it again: ask whether the two candidate answers differ in what the
COMPILER ALREADY DOES. Variance was a ruling because the compiler accepted
both readings and something had to choose. Mode was not, because the
compiler had already chosen consistently in every position and the only
missing thing was the sentence saying so.

It also caught a **latent false disagreement**: `rc-ty-eq` answered TyNeq on
a mode difference where the compiler agrees, and had not fired only because
no such site exists in the compiler today. `rc-ty-fits` answered TyUnknown
for the same pair, so the checker disagreed with itself as well.

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
