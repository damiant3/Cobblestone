# Independent Rechecker -- a plug that re-derives what the compiler asserted

**Status:** DESIGN. Not built. Phase 0 was re-confirmed against main on
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
| Effect rows | `(effectful (effs ...) (scopes ...) ret)` | **nullary definitions only** |
| Linear ownership | none: `LinearTy` is erased | **no** |
| Sums / records | ctor payloads and field types emitted | yes |
| Units, vectors | `(unit ...)`, `(vector n t)`, `(vector-mask n)` | yes |

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

**Four variants have no arm and are indistinguishable from a type
error.** `ProofTy`, `PropEqTy`, `TypeCon` and `TypeApply` fall through
to `is otherwise -> "error"`, which emits the same atom `error` that
`ErrorTy` emits. A rechecker cannot tell a proof type from a failure on
the wire, and must answer UNSUPPORTED on the atom rather than treat it
as either.

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
need no compiler change. **Stage 3 as written is not implementable over
IR text**: its linear half has no input at all, and its effect half
would cover only nullary definitions while appearing to cover the
claim. Under section 6 that is the forbidden shape, so stage 3 needs
`FunTy`'s row and the linear fact pushed onto the wire first.

**That is an established pattern here, not new work invented by this
design** (Damian, 2026-08-03: things get pushed out to the IR so the
plugs can deal with them the way they already do with the CDX). The
precedent is in the same constructor: `IRTextParser.codex` rebuilds a
def as `IRDef { ... is-punctual = punct, wcet-budget = budget,
unique-params = [], ... }`. Punctual and WCET are read back off the
wire as trailing positional fields on `(def ...)`; `unique-params` is
hardcoded empty. Of the four claims in the README sentence, bounded
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

- `docs/Reference/ClaimsCalibration.md` -- the claim register; the
  README sentence in section 1 belongs there with whatever the
  kill-rate ends up being
- `docs/Reference/TrustedComputingBase.md` -- section 4 ranks the proof
  layer; this document is an instrument for that ranking, not a change
  to it
- `docs/Designs/Active/Language/ProofTotalityProbe.md` -- the probe
  convention section 8 builds on, and the CDX4023/CDX4024 properties
  stage 4 re-checks
- `codex/compiler/Emit/IRTextEmitter.codex` -- `ir-emit-type`, the
  input format
- `codex/compiler/opening.codex:1414` -- IR text path (proofs pruned)
- `codex/compiler/opening.codex:1165-1168` -- CDX path (proofs kept)
