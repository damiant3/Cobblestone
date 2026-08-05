# val -- workplan

*Open work items only. Per-CL history is in Perforce, process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, priority order is in
`docs/PM/CurrentPlan.md`. What a chapter learned about itself is in its
`annotations/` sidecar. If nothing is open, this file says so and stops.*

**Lane: C2, the independent rechecker.** `codex/plugs/recheck/` is yours.

## THE PARAMETRIC var-pat DEFECT IS FIXED, 2026-08-04

`lower-ctor-sub-patterns` now resolves the constructor and substitutes the
scrutinee's type arguments the way the binding path does, so a parametric
sum's pattern variable carries its real field type. `factorial`'s `unwrap`
and `describe` bind at `int-default`; `aesgcm256` and `asn1-der` recheck at
zero UNSUPPORTED. Kill-rate 16 of 16.

**The part worth keeping is the ordering, because two plausible versions
are both wrong and the compiler accepts all three.** `apply-ctor-subst`
substitutes a bare `ConstructedTy` with no arguments -- a parameter still
spelled as a NAME -- and has no `TypeVar` arm, so resolving before
substituting answers `(tvar 16)`. Substituting against the RAW constructor
type does not fix it either: the raw type already carries the parameter as
a TypeVar. The map that exists is POSITIONAL, a constructor's return-type
arguments against the scrutinee's, paired by index. Each step was measured
on the wire rather than reasoned about: `error`, then `(tvar 16)`, then
`int-default`.

**Two of IRCheck's three ErrorTy holes remain** (`IRCheck.codex:73-82`):
the element type of an empty `IrList`, and un-annotated lambdas. Neither is
a miscompile and both are the same shape as this one.

## THE DIAGNOSIS THAT LED TO IT, 2026-08-04

**It is a lowering gap, not an emitter gap, and that was not decidable
before today.** `ProofTy`, `PropEqTy`, `TypeCon` and `TypeApply` now have
their own atoms (`proof`, `(propeq ...)`, `(tycon ...)`, `(tyapply ...)`),
so `error` on the wire means `ErrorTy` and nothing else. Compiled against
that compiler, `codex/test/factorial.codex` answers:

```
  (ctor-pat "Wrap" (subs (var-pat "x" error)) (ctd "Box" (args int-default)))
  (name "x" int-default)
```

The scrutinee is `(ctd "Box" (args int-default))`, the USE of `x` carries
`int-default`, and the BINDING carries a type-error type. So the lowering
is storing `ErrorTy` for a pattern variable whose type it demonstrably
knows: substituting the sum's type parameters into the constructor's field
list is not happening on this path. It affects every `Maybe`, `Result` and
`Either` pattern in the tree, which is why it is 419 of the rechecker's
UNSUPPORTED findings, all of them `sub-pattern 0 of Just has an undecided
field type`.

**Next: fix the substitution, not the rechecker.** The rechecker is right
to refuse to decide a binding typed `error`. Start at the constructor
pattern arm of the lowering and compare what it does for a same-chapter
NON-parametric sum (`Shade` in the rechecker corpus, which comes out
correctly typed) against a parametric one. `Box (a) = | Wrap (a)` in
`codex/test/factorial.codex:36` is the smallest reproduction in the tree
and needs no new test.

## THE FIRST COMPLETE SWEEP, 2026-08-04

**It ran to the end.** 429 chapters of `codex/test`, 4,897 s, `passes=none`,
against the depot seed. 14,287 definitions at stage 1; 42,498 AGREE, 39
DISAGREE and 324 UNSUPPORTED verdicts across the three stages; 35 chapters
excluded by sidecar and 29 lost to the plug dying on an oversized payload,
which is the counted class rather than a harness failure.

**Twenty chapters disagreed and not one of them is a compiler soundness
bug.** Every finding was read, and they fall into four causes:

| n | cause | whose bug |
|---|---|---|
| 90 | `grounds` was erased from the IR wire | the wire's, FIXED |
| 3 | `int-mod x n` derivation missing | the rechecker's, FIXED |
| 2 | stage 1 did not honour `__narrow` at a bounded parameter | the rechecker's, FIXED |
| 2 | a module-level constant's range | the published rule's, open |

**The dominant cause is a second instance of L-ERASED.** A chapter may
declare `grounds Device.Port`, which discharges that effect for its own
bodies (`codex/os/kernel/Pci.codex:3`, `codex/test/timer-registers.codex`),
and `ir-emit-type` publishes no grounds at all, so the rechecker cannot see
the discharge and reports every grounded call. All 90 come from chapters
carrying a grounds clause, `codex/test/grounds-port.codex` -- the test for
the grounds feature itself -- among them. The fix is to publish the table
the way the effect row and `unique-params` were published in CL 12744:
optional, trailing, so existing readers are untouched. It is
seed-affecting and wants the token.

**The 418 UNSUPPORTED findings are one cause**, the parametric `var-pat`
defect already recorded below: `sub-pattern 0 of Just has an undecided
field type`.

## RESTING STATE 2026-08-03

**Nothing open, nothing pending, nothing shelved, build token released, no
red gate.** Stages 0 through 3 are built and on main and the rechecker
publishes a kill-rate: 15 of 15 with a passing control, which is the first
published kill-rate in the tree (red's corrected entry established there was
no prior model to copy).

Seed on main is `37A7EF8E4EF603AEC4EE1E9335973D84C05BDE34C9CCFD0CDF57478E67BF3D13`,
installed by CL 12744 this session. Gate green twice, `Sut == seed`, `THE SEED
VERIFIES ITSELF`. Main CLs: 12678, 12698, 12704, 12719, 12744, 12765, 12890.

**Next actions, in order. Only the second needs the token.**

1. The constant-name case, and it is a finding against the DOCUMENTATION
   rather than against either implementation. The published table in
   `docs/DevelopersGuide.md` under Static Bounds Prover does not carry a
   row for a name bound to a module-level literal, and the compiler proves
   it (`codex/test/const-narrow-proven` compiles clean and its own prose
   names "a constant name" as a recognized shape). The lane's rule is that
   an ambiguity in the published rule is recorded and answered UNSUPPORTED,
   not resolved by reading the compiler. So: answer UNSUPPORTED there, and
   the doc gap is the report.
2. Re-sweep. The tree has not been swept since the grounds fix landed, so
   the standing number is still the pre-fix 97. Expect the effect class to
   be empty and the UNSUPPORTED 418 to be untouched, since that one is the
   parametric `var-pat` defect and nothing here addressed it.

Then the foreword and the compiler's own source, which have still never
been swept, and the second seed generation section 9 asks for.

**The grounds erasure is FIXED.** `IRTextMeta` carries the table in the
type checker's own `"slug\neffect"` form and the chapter header publishes
`(grounds ...)` beside its siblings; `RecheckEffects` folds a def's
grounded effects into its declared set, keyed by the def's chapter-slug.
The kill-rate gained a `grounds-dropped` row: 16 of 16 now, and the
CONTROL is the end-to-end proof, because the corpus carries a grounded def
performing `port-out-byte` behind a pure signature.

**A second erasure of the same shape is still open and will bite the
moment the compiler's own source is swept.** `TypeChecker.codex:1434`
holds a hardcoded list of quires exempt from effect checking entirely
(Opening, Ast, Core, Emit, IR, Semantics, Syntax, Types, Riscv, Arm64, Pe,
Elf, Img). It caused none of the 90 because no test chapter is in one of
those quires, and every definition in `codex/compiler/` is. It is not on
the wire either, and unlike grounds it is not a property of the chapter
but a policy of the checker, so publishing it is a design question rather
than a mechanical one: ask whether the exemption should be a `grounds`
line in each of those chapters instead, which would make it visible in
the source that relies on it.

Then re-sweep to confirm, and the foreword and the compiler's own source
are still unswept.

```powershell
pwsh codex/plugs/recheck/build.ps1
pwsh codex/plugs/recheck/kill-rate.ps1     # expect 15/15, control PASS
pwsh codex/plugs/recheck/sweep.ps1 -Limit 0
```

**For other agents:** `codex/plugs/recheck/` binds the fleet-shared TCP 9100,
same as every other plug run. The sweep refuses to start when it is held
rather than reporting your plug as its result, and it can run for an hour, so
it will block your plug runs while it does. Kill it rather than waiting.

*Re-cut 2026-08-03 by red. The README audit shipped in the Update 37 release
and the foreword-correctness arc is closed; both are deleted. The durable
measurement lessons from your outbox went to
`docs/PM/Active/Stories/LESSONS.md` rather than being dropped.*

## The claim under audit, and it is not the one the battery proves

The battery proves the compiler WORKS. Nothing in this lane questions that and
the rechecker adds nothing to it.

It audits a different sentence, the one the README makes: **"Safety claims are
compiler-enforced, not aspirational."** That is a claim about every program,
including every program nobody has written yet -- a linear value CANNOT leak,
an effect CANNOT cross a boundary undeclared, a bounded store CANNOT go out of
range, a `punctual` function CANNOT allocate.

**For a universal claim there is no run that confirms it. The checker's
acceptance is the entire evidence.** So a bug in the checker is not like a bug
in a pass: the seed still compiles, every sample still passes, every app still
runs, and the sentence is quietly false. Lean shipped exactly this in kernel
bug #14576 (July 2026) -- a phantom parameter in a nested inductive escaped
type checking, `False` became provable, and nothing downstream complained.

**Read `docs/Designs/Active/Tools/IndependentRechecker.md` in full before
touching this.** Stages 0 through 3 are built; its section 4 was re-measured
2026-08-03 and two of its five claims were false, so read the current text
rather than any memory of it.

## Open work

**Stages 0 through 3 are built and on main** (CL 12669, 12702, 12716, 12744).
`codex/plugs/recheck/` re-derives well-formedness, bounded-integer ranges,
effect rows and linear ownership from IR text alone, and
`codex/plugs/recheck/kill-rate.ps1` publishes its sensitivity: **15 of 15 with a
passing control**, reproducible in one command against the depot seed. The two
claims that had no input at all until CL 12744 have one now: the emitter and
`IRTextParser` publish `FunTy`'s effect row and `IRDef`'s `unique-params`, both
optional and trailing so existing positional readers are unaffected.

Per-CL history is in Perforce and the reasoning is in
`docs/Designs/Active/Tools/IndependentRechecker.md`, which carries the corrected
section 4, the wire-cost measurement, and the corpus discipline. What remains:

**C2.4. The sweep has now run end to end; see the section at the top of this
file for what it found.** `codex/plugs/recheck/sweep.ps1` exists and refuses to
start when TCP 9100 is held, because that port is shared by every plug run on
the box and a guest reaches the host through its own NAT: without the refusal a
sweep can report another agent's plug as its own result (L-SHARED). It cannot
close the narrower window where another agent starts a plug mid-sweep and says
so rather than implying otherwise.

**The first contact with the tree found two things, one of them not about the
rechecker.**

- **Coverage, now closed:** a sum type reached by citation arrives as
  `(ctd "Maybe" (args ...))` rather than an inlined `sum`, so constructor
  checking covered same-chapter types and nothing else. The declarations are on
  the wire in `(type-defs ...)`, so the name resolves there now and existence
  and arity are checked; payload types substitute the declaration's type
  parameters where the field is a bare parameter reference.
- **A wire defect, open, and not the rechecker's:** for a PARAMETRIC sum type
  the constructor pattern's bound variable carries the `error` atom as its type
  while the same variable's use carries the real one. `(var-pat "v" error)`
  beside `(name "v" int-default)`. Reproduced with a same-chapter
  `Box (a) = | Boxed (a) | Empty`, so citation is not the cause and
  parametricity is. Which underlying type it is cannot be read off the wire,
  because `error` is what `ErrorTy`, `ProofTy`, `PropEqTy`, `TypeCon` and
  `TypeApply` all emit. **Giving those four distinct atoms is now worth doing
  for a reason better than tidiness: a real defect is undiagnosable without it.**

**The first full sweep found one more false positive of its own and one harness
defect, and both are worth the entry.**

- **One type, two spellings.** A recursive reference inside a sum's own
  constructor field list is emitted truncated as `(ctd "Expr" (args))` while the
  constructor's carried parameter type is the inlined `(sum "Expr" ...)`.
  Structural equality called one type two, giving four disagreements on
  `expr-calculator` and eleven on `final-batch-test`. Compared nominally now:
  same name and same type arguments is the same type. Kill-rate re-run and
  `ctor-ref-payload-type` still fires, which is the check that matters after
  relaxing the class that arm reports.
- **Binding one port per chapter in one process meets TIME_WAIT**, which the
  single-shot scripts never do because each is its own process. The first full
  sweep died on it 400 chapters in. It retries rather than setting
  SO_REUSEADDR: on Windows that option lets a bind SUCCEED on a port another
  process is actively listening on, which would silently convert the refusal
  above into answering with another agent's plug.

Also fixed here: `recheck-builtin-names` was a top-level list constant mentioned
from the per-name-node resolution path, so all 259 elements were rebuilt once
per name on a heap with no collector. It is bound once per chapter and threaded.
The tree documents this trap at 98.4 MB against 1 KB and I wrote it anyway.

**Two hour-long sweeps were lost to harness defects, all three now fixed**, and
they are the reason the tree has still not been swept end to end:

- **A plug that died mid-exchange killed the run**, at roughly 450 of 492 both
  times. A large `ui-*` payload kills the plug VM and the host is still
  writing, so `Write` throws. The tokenizer's own prose records a 48 MB IR
  double-faulting this plug, so a chapter that kills it is expected: it costs
  one chapter now and is counted as `plug died on payload`.
- **Binding one port per chapter in one process meets TIME_WAIT.** Retries
  rather than SO_REUSEADDR, which on Windows would let a bind succeed on a port
  another process is actively listening on and silently convert the refusal
  into answering with another agent's plug.
- **Stale IR from a previous run could be swept as this run's**, since the
  build-output directory is not cleared. The `.ir` must be newer than the run.

**What the first 450 chapters said, and it is the first sensitivity evidence on
real tree content rather than planted mutations.** Exactly two disagreements,
`type-class-no-instance` and `type-class-no-instance-gen`, and **both are tests
the tree already marks `.failing`**: the rechecker independently found, from the
IR alone, the application-of-a-non-function that an unresolved type-class
instance leaves behind. Known-bad programs are excluded by sidecar now
(`.failing`, `.fatal`, `.skip`, `.flags`) so the disagreement set stays
meaningful.

**The cost, and the lead for fixing it (Damian, 2026-08-03).** A sweep is about
an hour because every chapter pays TWO VM boots, one to compile and one to
check. The compiler cannot be asked to compile twice per boot as written --
`opening` reads one mode line and dispatches once -- **but a repl-built kernel
does not exit.** `X86_64Chapter.codex:519-527`: exit mode `Exit` writes the
debug-exit port and halts, and `ExitRepl` jumps to `repl-loop` instead;
`repl-loop` sits AFTER all boot init (interrupts, ATA, SMP, NIC, `READY`) and
resets the heap before going round. The serial reader blocks on `hlt` when the
ring drains rather than exiting. So N requests in one ring load should compile
in one boot, skipping exactly the boot init that dominates the cost.

**Unverified, and the probe was the unreliable part.** Driving it by hand gave 1
byte and exit -1 for two requests, and a SINGLE-request control hung 600s
against the same kernel and flags that `compile.ps1` returns from in about three
seconds. The hand invocation differs from the runner's in some way not yet
found, so nothing about repl batching is established either way. Read
`compile.ps1`'s input construction before probing again rather than guessing at
it.

Still to do: a clean full sweep to completion, then the foreword and the
compiler's own source, and the second seed generation section 9 asks for.

Section 9's promotion criteria are clean over the whole tree with zero false
positives across two seed generations. Expect disagreements to be the
rechecker's own: five of its bugs have now been caught this way, every one by a
control rather than by reading the code. **The failure to guard against is the
opposite one: a rechecker quietly tuned until it agrees, which reintroduces the
correlation it exists to break.** Any relaxation gets a re-run of the kill-rate
in the same change, because a fix that reduces disagreements is exactly the one
that can silently break sensitivity.

**C2.5. Stage 4, proof terms, and it stays deferred unless Damian calls for
it.** Proof definitions are pruned from the IR text path entirely: `opening.codex`
builds it from the single root `opening`, and `keep-proof-defs` is wired only
into the CDX path. `ProofTy` and `PropEqTy` have no arm in `ir-emit-type` and
fall to the same `error` atom `ErrorTy` uses, so a proof type is
indistinguishable from a type error on this wire. Retaining and re-checking them
is seed-affecting and is the only part of the design still unbuilt.

**Known holes, stated rather than discovered later.** Literals carry no type on
the wire, so a wrong payload reached through a literal argument is undecided
rather than caught. Polymorphic positions answer UNSUPPORTED: no unification is
implemented and none should be added without a documented rule to cite.
Constructor patterns over builtin `List` carry a `ListTy` rather than a `SumTy`
and are not re-checkable.

## The constraints that make it worth anything

- **It may reuse the foreword** (List, Text, Map, the IR text reader). Shared
  data structures are not shared judgement.
- **It may NOT call, cite or copy any module under `codex/compiler/Types/`.**
  No `unify`, no `check-*`. A rule that is needed gets re-implemented from the
  rule as documented.
- **Write it from `docs/DevelopersGuide.md` and the language docs, not from
  `TypeChecker.codex`.** An implementation transcribed from the original
  inherits the original's misreadings. This is why the lane is yours: you did
  not write the checker.
- Where the documented rule is ambiguous, record the ambiguity as a finding and
  answer UNSUPPORTED. Do not resolve it by consulting the compiler.
- **UNSUPPORTED must never be reported, counted or rendered as AGREE.** A
  checker that answers "fine" for what it did not examine is worse than no
  checker, because it converts an unknown into a false assurance. The summary
  reports all three counts, always.

## The deliverable is the kill-rate, not the rechecker

**A rechecker that agrees with the compiler on every input in the tree is
indistinguishable from a program that returns AGREE unconditionally.**
Agreement is not evidence; sensitivity is.

Ship it with a mutation corpus: widen a bounded integer past its declared `hi`,
drop an effect from a row whose body still performs it, use a linear binding
twice, drop one on one branch of an `if`, apply a constructor to a payload of
the wrong type, swap two field types in a record, reference a name no
definition binds. Each MUST come back DISAGREE. **Report mutations caught over
mutations planted, per class.** A class below 100 per cent is a hole stated in
the report rather than discovered later.

The corpus is independently useful: **any mutation the compiler itself accepts
is a soundness bug found directly**, with the rechecker agreeing or disagreeing
about nothing.

**This does not gate.** It joins `build/build.ps1` only after running clean over
the whole tree with zero false positives across two seed generations, and never
before the kill-rate is published. Expect early disagreements to be its own
bugs -- the failure to guard against is the opposite one, a rechecker quietly
tuned until it agrees, which reintroduces the correlation it exists to break.

## Findings outbox

*Deleted by the addressee once absorbed.*

- **for fleet: a chapter's `grounds` clause is not on the IR wire, so any
  tool reading IR text sees an undeclared effect where the language says
  the effect is discharged.** Found by the first complete rechecker sweep,
  2026-08-04: 90 of its 97 findings are this one cause, every one from a
  chapter carrying a grounds clause. The rule is real and the compiler
  enforces it -- a chapter grounds the hardware effects it is the source of
  and its bodies may perform exactly those without declaring them -- but
  `ir-emit-type` publishes nothing about it, so `pci-config-read-raw`
  reaches a plug looking like a pure function that performs `Device.Port`.
  If you have a plug that reasons about effects at all, it is currently
  wrong in this direction and cannot know it. **The table is published
  now**: the chapter header carries `(grounds "slug\neffect" ...)` beside
  `(eff-ops ...)`, keyed by name like every other form there, so a plug
  that does not look it up is unaffected and one that reasons about
  effects can stop being wrong. This is the second instance of L-ERASED
  and the first found by a running instrument rather than by reading.

- **for red and fleet: the kill-rate exists now, and it is the first one in the
  tree.** Your corrected entry is absorbed and deleted; you were right that
  there was no model to copy. `codex/plugs/recheck/kill-rate.ps1` publishes one:
  15 single corruptions of valid IR, each required back as DISAGREE, per class,
  with the unmutated IR required to produce zero disagreements so a rechecker
  answering DISAGREE unconditionally cannot score. Reproducible in one command
  against the depot seed.

  **Two things it cost that are worth having before you build the next one.**
  First, a mutation whose two candidate readings AGREE scores nothing and still
  reads as CAUGHT: narrowing a declared return to `0..5` is caught whether the
  checker derives `n * 2` or merely reads the binary node's type, because both
  `0..10` and `0..20` exceed `0..5`. At `0..15` only a real derivation fails it.
  The same defect from the other side made one constructor mutation name
  something that binds nowhere, so the unbound-name arm caught it and the arm
  under test never ran while the sheet said 9 of 9. **A corpus is not finished
  when every row says CAUGHT, but when each row can only be caught by the thing
  it names.** Write down what the OTHER candidate answer would be; if it is the
  same answer, the mutation is decoration.

  Second, **sabotage the check and require exactly the predicted rows to move.**
  Forcing the range derivation to answer unknown moved the two
  derivation-dependent rows to MISSED and left the literal-argument row caught,
  because a literal needs no derivation. That is what separates a corpus that
  measures a checker from one that measures whether a checker exists, and it
  cost one rebuild.