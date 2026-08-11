# The type-variable rules the language does not publish

**Status: DONE 2026-08-09. Opened 2026-08-08 by val at Damian's direction
after the abstention sweep turned out to be measuring the specification
rather than the checker.** R1, R2 and R3 are published in
`docs/DevelopersGuide.md` (`## Type Variables`, `## Integer literals`,
`## Variance of Type Arguments`), the rechecker enforces them, and since
stage 3b the compiler enforces R3 too. R4, the integer-bounds question
behind 299 findings, is a different subject and was never part of this
campaign; see section 3.

**When this was opened, nothing here was a defect in the compiler**: no
disagreement had been found, in that sweep or any before it, and what was
missing was the written rule. That is no longer the whole story. R3 was
ruled invariant on 2026-08-08 and publishing it turned the abstention at
`fresh-row-id` into the compiler's first DISAGREE (stage 3 below), and a
`List` covariance the compiler accepts outright was measured beside it.
Writing a rule down converts silence into a verdict, which is the point of
the exercise and is also how a rule acquires its first counterexample.

The rest of the document stands: the remaining gap is written rules, and
the cost is that a second implementation cannot check 78 per cent of what
the first one asserts.

## 1. What is missing

`docs/DevelopersGuide.md` never introduces type variables.

- The `## Types` table lists 16 forms. None is a type variable and none
  is a parametric type declaration. `List Integer` and `Maybe Text`
  appear only as USES of types that are already parametric.
- `a` first appears at line 135, inside an example about higher-order
  parameters: `map : (a -> b), List a -> List b`. It is never
  introduced, never scoped, never given a meaning.
- There is no rule for applying a polymorphic function, none for
  declaring a parametric type, and none for variance of type arguments.

The language plainly HAS all of this. `Box (a) = | Boxed (a) | Empty`
compiles, the AST carries type parameters (`AVariantTypeDef` has `tps`),
and `Maybe`, `Result` and `List` are used everywhere.

**Why it matters beyond tidiness.** `IndependentRechecker.md` section 7
binds the rechecker to re-implement rules from the guide and forbids
reading `Types/`, because a checker written from the implementation it
audits inherits that implementation's misreadings and its agreement then
proves nothing. Where the guide is silent the rechecker must abstain.
So the abstention set is not a list of the checker's weaknesses. It is a
map of where the language is defined only by the program that enforces
it.

## 2. The measurement

Whole compiler as one payload, `-Passes none`, seed
`AEB5ED2B5043C7C1`, kill-rate 18 of 18 with a passing control before and
after every arm. **4721 definitions, AGREE 13779, DISAGREE 0,
UNSUPPORTED 384 verdicts carrying 1365 findings.**

Verdicts are per definition per stage (`13779 + 0 + 384 = 14160 =
4721 x 3`); the by-kind table counts findings. The two have been quoted
interchangeably in the past. Say which.

| cause | findings | share |
|---|---|---|
| **type variable** (403 at top level, 659 nested) | **1062** | 78% |
| integer bounds (`apply-arg-int-bounds` 198, `bounds-underived` 101) | 299 | 22% |
| overflow-mode mismatch | 3 | 0.2% |
| **variance of a type argument** | **1** | 0.07% |

Every opaque type in the population is a `tvar`. Not one is
`error-atom`, `forall` or `foralleff`, at top level or nested.

**The classifier passes its own self-check.** `other`, `no-arm`, `shape`
and `fuel-exhausted` are deliberate buckets rather than a flattering
default, and all four are ZERO, so the classification explains the whole
population. Each of the three label splits was re-swept and every total
held identical, which is the control proving they moved no verdict.

**The counts are stable across a seed change.** They were first taken
against seed `43189C1E7D762144` and re-taken against
`AEB5ED2B5043C7C1` after C1 closed COMPILER-3 and COMPILER-4, including
a fix to a type published as a type APPLICATION on the declaration wire.
Every class held.

## 3. The rules to publish

**R1 and R2 are standard and merely unwritten. R3 is a decision. R4 is a
different subject and is listed only so it is not confused with them.**

### R1. Type variables exist, and this is their scope

A lowercase identifier in a type position is a type variable. A
definition's signature implicitly binds every type variable it mentions,
universally, at that definition. A parametric type declaration binds its
parameters explicitly: `Box (a) = | Boxed (a) | Empty`.

Not controversial. It is describing what already compiles.

### R2. Applying a polymorphic function

At an application, the callee's type variables are instantiated by
matching the parameter types against the argument types. **Every
occurrence of one variable in one signature must instantiate to the same
type.** The application's result type is the declared return with that
substitution applied.

This is the rule that makes `f : a, a -> a` applied to an Integer and a
Text wrong, and it is the reason the naive per-argument shortcut is
unsound: neither argument is wrong on its own, so a checker that decides
arguments independently cannot see it.

**R2 is correct as written and it was nearly rewritten on 2026-08-09 on
the strength of three false disagreements.** The rule that was actually
missing is not this one. It is what type an integer literal has, and it is
now published as `docs/DevelopersGuide.md` "Integer literals". See section
5, stage 2.

### R3. Variance of type arguments. RULED INVARIANT, 2026-08-08 by Damian

Published in `docs/DevelopersGuide.md` "Variance of Type Arguments". A
type argument is invariant: `List (Integer between 0 and 10)` does not
stand where `List (Integer between 0 and 20)` is wanted, nor the reverse.
The argument is `list-set-at`, a builtin, so `List a` is mutable in place
and covariance lets the narrow view's bounds claim be falsified through
the wide one.

The rule is stated for ALL type arguments rather than only the mutable
containers, because variance is otherwise a per-constructor fact we do not
publish and every reader and checker would have to look it up.

It governs a type in an ARGUMENT position only. Ordinary assignability is
untouched, so a narrower integer still fits a wider parameter, and
function subtyping is untouched.

**The rechecker enforces it** (`RecheckCore.codex`, `rc-ty-fits`): the
three nominal arms compare arguments with `rc-nominal-eq`, and the `List`
and `LinkedList` arms with `rc-ty-eq`. Those last two are the ones worth
naming, because `List` is the case the whole soundness argument rests on
and it sits on its own path rather than under `rc-nominal-fits`. A change
that fixed only the nominal arms would have looked complete and left the
motivating case covariant.

Sensitivity: `variance-widened-type-arg` in `kill-rate.ps1`, confirmed
MISSED before the rule and CAUGHT after, kill-rate 18/19 to 19/19 with the
control passing and no other arm moving.

### R4. Not this subject: the integer-bounds rule

299 findings are the bounds checker's question, not the type system's:
whether an integer argument whose declared bounds differ from its
parameter's fits, and how a range is derived for a value. Publishing R1
to R3 does not touch them. Listed here only so the 299 is not read as
part of this work.

## 4. Blast radius

**R1 and R2: no compiler change, and they unblock 1062 findings.** The
compiler already implements them; what is missing is the sentence. The
work is a guide section plus substitution tracking in the rechecker.

**R3 invariant: ONE site in the whole compiler**, `fresh-row-id`, the
sole `apply-result-undecided-nested-variance` finding at 4721 definitions.

**That number is the RECHECKER's exposure and it is not the compiler's.**
It counts undecided comparisons the classifier attributed to a widening at
an application in one payload. A source-level covariance the compiler
accepts can produce no finding at all, and `widen-view` in stage 3 is
exactly such a case. The sentence that used to stand here -- if the
compiler agrees, nothing changes anywhere -- read the one figure as an
answer to both questions, which is the failure this project keeps
repeating. Measured: the compiler does NOT agree.

**R4: untouched, 299 findings, a separate track.**

**The risk that is real, and it points the other way.** Stage 2 makes
the rechecker START CHECKING 1062 comparisons it currently declines. The
sweep reports zero disagreements today partly because it is not looking.
Expect disagreements to appear, expect most of them to be the
rechecker's own, and do not tune them quiet. That is the whole warning
in `IndependentRechecker.md` section 9, and this change is exactly the
shape it describes: a large population of abstentions becoming decided.

## 5. The plan

**Stage 1. Publish R1 and R2 in `docs/DevelopersGuide.md`. DONE
2026-08-09.** `## Type Variables`, between `## Types` and
`## Variance of Type Arguments`: what a type variable is, that a signature
binds the ones it mentions at that definition, the parametric declaration
form, and the consistent-instantiation rule.

**Written from measurement, not from `TypeChecker.codex`**, which section 6
forbids and which would have made the guide a transcript of the
implementation. Four arms, seed `A1EBA5A03016A128`:

| arm | result |
|---|---|
| `Box (a) = \| Boxed (a) \| Empty` | compiles |
| `ident : a -> a` used at Integer AND Text | compiles, so binding is per definition |
| **control** `diff-two : a, b -> a` applied to `(1, "text")` | ACCEPTED |
| **subject** `same-two : a, a -> a` applied to `(1, "text")` | CDX2001, Integer vs Text |

The control is the arm that matters: it is the SAME call with the SAME
argument types, differing only in whether the signature spells one variable
or two. Without it, the subject's rejection would only show that the
checker dislikes mixing types somewhere.

**Stage 2. Substitution tracking in the rechecker. BUILT 2026-08-09.**

**The wire made this local, and the design's fear of threading a
substitution across an application was unfounded.** A curried call is nested
applies and the compiler records each node's result type with the
substitution SO FAR already applied. `(n, t)` emits

```
(apply (apply (name "MkTup2" (fn (tvar 2) (fn (tvar 3) (Tup2 (tvar 2) (tvar 3)))))
              n (fn (tvar 3) (Tup2 int-default (tvar 3))))
       t (Tup2 int-default text))
```

so the inner node already carries `tvar 2` as `int-default`. Match THIS
node's parameter against THIS node's argument, apply the result to the
declared return, compare. Nothing is threaded between nodes, and R2
consistency falls out: a variable used two ways arrives at the second apply
as a CONCRETE type that disagrees, which the ordinary comparison catches.

**MkTup2 is the only callee that reaches an apply with variables still on
the wire.** An ordinary polymorphic call is already instantiated at its site
(`ident` appears as `(fn int-default int-default)` in one caller and
`(fn text text)` in another), which is why the 403 `opaque-param-tvar`
findings concentrate in tuple-returning definitions: `unify-structural` 90,
`parse-bound-int` 8.

`RecheckCore` gains `rc-match` (one-way, pattern side binds), `rc-subst`,
and `RcSubst` with three answers, the third being Unknown so an
unmatchable pair still abstains. Sensitivity: `tvar-substitution` in
`kill-rate.ps1`, confirmed MISSED before and CAUGHT after, 19/20 to 20/20.

**Three things this cost, all worth the line:**

- **The corpus arm was wrong first.** It corrupted the inner node's RESULT
  type, which is also the OUTER apply's callee, so it disagreed concretely
  one node up and was caught with or without the change. It now corrupts
  the ARGUMENT type, which only substitution can detect.
- **`deck-record` around a CONSTRUCTED `CodexType` traps at runtime** in a
  plug (EXC=06, jumping into unrelated foreword code). `rc-expr-ty` in the
  same file builds variants without it; that is the precedent.
- **Rebuilding a type at every application exhausted the heap** on the
  whole-compiler payload and the report came back truncated mid-line with no
  summary. `rc-subst` now answers the input unchanged when the map is empty
  or the type mentions no variable, and only then builds.

**Stage 2's three disagreements are ADJUDICATED, 2026-08-09. All three
were the rechecker's, in ONE LINE, and the missing rule was about
literals.**

The first sweep with substitution tracking reported DISAGREE 3 over 13
sites, every one `apply-result-type`: `parse-bound-int` (2),
`mod-proven-range` (1), `builtin-return-range` (10). Every one of those
bodies returns a tuple containing a bare integer literal.

`rc-expr-ty` typed `IrIntLit v` as `IntegerTy v v`. The compiler does not:
`infer-literal` answers `int-ty-default`, a plain `Integer`
(`Types/TypeCheckerInference.codex`). So the checker bound `MkTup2`'s type
variable to a one-value range, the substituted return then disagreed with
the tuple type the site recorded, and invariance refused it. The fix is
that one arm.

**`[v, v]` is not wrong, it is the answer to the other question.** It is
the literal's VALUE, it decides admission to a bounded parameter, and
`RecheckBounds`' `rc-derive` already uses it there. Reading it as the
literal's TYPE is what produced all three.

**Measured, and the compile results are what settled it** rather than
reading either implementation:

| arm | result |
|---|---|
| `list-of-literal : List Integer = [0]` | **CLEAN**, so a literal is not `[0,0]` |
| `tuple-of-literal : Integer -> (Integer, Integer)` = `(0, x)` | **CLEAN** |
| **contrast** `(h.h-small, x)`, field declared `0..255` | **CDX2001** |
| `narrow 5` into a `0..10` parameter | **CLEAN**, admitted by value |
| `narrow 500` | **CDX2050**, out of range |
| `narrow x` where `x : Integer` | **CDX2051**, wants a proven range |

The third row is the one that pays for itself. It is the same SHAPE as the
first two and it is rejected, which says the literal is the special case
and not the tuple, and it is the `fresh-row-id` class from stage 3. Now
published as `docs/DevelopersGuide.md` "Integer literals".

**The first fix was WRONG and passed everything except one arm.** It made
the checker fix the callee's variables from the site's recorded result type
instead. That also cleared all three disagreements, also kept the whole
compiler at DISAGREE 0, also left the abstention counts untouched, and
scored 21 of 21. It was blind: with the variable fixed from the site, a
BOUNDED argument flowing into a plain declared slot merely has to FIT,
which it does, so `fresh-row-id` itself would have gone silent. **The
whole-compiler sweep cannot tell these two fixes apart. Only a mutation
aimed at the direction being changed can.**

That mutation is now `bounded-arg-into-plain-slot`, the argument-side twin
of `variance-widened-type-arg`: the argument's type becomes `0..10`, which
FITS `int-default`, so only invariance rejects it. Sabotage confirmed, and
it moved exactly one row: under the rejected fix it is MISSED, 20 of 21,
every other arm CAUGHT and the control still passing.

Kill-rate **21 of 21 with a passing control**. Whole compiler, `-Passes
none`, 4815 definitions: **AGREE 4631 / DISAGREE 0 / UNSUPPORTED 184** at
stage 1, 4729 / 0 / 86 at stage 2, 4815 / 0 / 0 at stage 3, with the
abstention counts unmoved.

**One consequence worth naming.** A literal typed plain `Integer` no longer
fits a bounded parameter, so `narrow 5` would land in the
`apply-arg-int-bounds` abstention on every such call in the tree. It does
not, because that pair is skipped here and decided in `RecheckBounds` by
value, which is the same division `rc-wf-narrow-into-int` already makes for
`__narrow`.

**Stage 2, as originally written:**
Build a variable-to-type map while matching parameters against
arguments, require consistency, and apply it to the declared return
before comparing against the recorded result type. Plug only, not
seed-affecting.
**It does not land without its corpus arm:** a mutation that
instantiates one type variable two ways, confirmed SILENT before the
change and CAUGHT after. Without that arm the change cannot be
distinguished from tuning the checker quiet, and the kill-rate re-runs
in the same changelist either way.

**Stage 3. The variance ruling, then `fresh-row-id`. DONE 2026-08-08.**
Damian ruled invariant. The rule is published, the rechecker enforces it,
and the corpus arm is in.

Whole compiler as one payload, `-Passes none`, seed `065D92E60292492D`,
kill-rate 19 of 19 with a passing control:

**4721 definitions, AGREE 13780, DISAGREE 1, UNSUPPORTED 382**
(`13780 + 1 + 382 = 14163 = 4721 x 3`, verdicts per definition per stage).

**The single DISAGREE is `fresh-row-id`, reported `apply-result-type`.**
The rule decided exactly the site it was predicted to decide and produced
no other disagreement anywhere in the compiler.

**Zero `-variance` abstentions remain**, against 1 before. That is the
control, and it is why the diagnostic classifier was kept rather than
deleted with `rc-nominal-fits`: a widening now makes a comparison TyNeq,
TyNeq dominates, and a decided pair never reaches the classifier, so a
nonzero count here would have meant a widening surviving on a path the
change did not reach. It reads zero.

**No delta is quoted against the 2026-08-08 figures in section 2 and none
should be.** Those were taken against seed `AEB5ED2B5043C7C1`; this run is
`065D92E60292492D` with the compiler source moved underneath it, so a
before/after on the abstention count would be confounded exactly as the
`IndependentRechecker.md` re-baselining note warns. The claims above need
no baseline: they are counts from one run.

**The compiler does NOT agree, and that is the finding.** `fresh-row-id`
binds `st.next-row-id`, declared `Integer between 0 and 4294967295`, and
returns it as the first element of a declared `(Integer, UnificationState)`.
That is a widening inside a type argument, which invariance refuses, and
the compiler accepts it. Reproduced minimally: a record field of the same
bounds returned in a two-element tuple declared with plain `Integer`
compiles clean and the rechecker now reports `apply-result-type`, while a
control declaring the tuple argument exactly is unmoved and a control
widening at an ordinary parameter stays agreed.

**Separately measured, and it is the stronger fact:**
`widen-view : List (Integer between 0 and 10) -> List Integer` whose body
is its own parameter COMPILES CLEAN. So the compiler is covariant on the
`List` type argument, which is the exact case the ruling rests on and a
worse one than `fresh-row-id`, because a `list-set-at` through the wide
view is what falsifies the narrow view's bounds.

**Whether that is exploitable at runtime is NOT measured and must not be
assumed either way.** Two attempts to observe a poisoned read produced no
usable output, and a trivial print control run the same way also produced
none, so the invocation was at fault and both readings were void. The
static acceptance above is a compile result and does not depend on them.
Anyone taking this further runs the probe through `build/test-run.ps1`
rather than by hand (L-SIDECAR).

**RE-MEASURED 2026-08-09 after stage 3b: the compiler now REFUSES it.**
The two paragraphs above record the state BEFORE the compiler enforced the
rule, which is the state that motivated the ruling, and they are kept as
that evidence. Four arms against the current seed, differing only in the
thing under test:

| arm | expected | result |
|---|---|---|
| control `plain-view : List Integer -> List Integer` | clean | CLEAN |
| subject `widen-view : List (Integer between 0 and 10) -> List Integer` | refused | CDX2001, `Integer vs Integer between 0 and 10` |
| subject `narrow-view : List Integer -> List (Integer between 0 and 10)` | refused | CDX2001, `Integer between 0 and 10 vs Integer` |
| control `widen-int : Integer between 0 and 10 -> Integer` | clean | CLEAN |

**The controls are what make it a measurement rather than two refusals.**
The first differs from the subject only in the type argument, so a blanket
refusal of `List` signatures would have shown up there instead; the last
confirms invariance still governs argument positions only and leaves
ordinary widening untouched, which is the half of the rule a strict reading
could easily have broken. Both directions are refused, as the rule states.
The runtime question above is now unreachable from new source and stays
unmeasured.

**Stage 3b. The compiler enforces it too, at Damian's direction.**

`Types/Unifier.codex` threads an `inv` flag meaning "this pair sits in a
type ARGUMENT position". It is set at every argument position (the element
of a `List`, `LinkedList` and `Vector`, every `unify-cargs-loop` pair, and
the argument half of each `TypeApply` peel) and never cleared, so nesting
inside an argument stays invariant. `unify-structural`'s integer arm
compares bounds exactly when it is set.

**The permissiveness was wider than "covariance" and that is worth
recording.** The integer arm accepted two ranges that merely OVERLAP, in
either direction, so `Integer between 0 and 10` and
`Integer between 5 and 20` unified anywhere, argument position included.
Overflow mode was and still is not compared at all; that is pre-existing
and a separate question, deliberately left alone here.

**Measured cost across the whole tree: FIFTEEN sites.**

| where | sites | fix |
|---|---|---|
| the compiler | 2 | `fresh-row-id` and `emit-mask-extract`, each widening locally |
| the foreword | 7 | 5 encoders widen through 3 new `Rgb` accessors; `Coap` through a local `coap-byte` |
| os, boards, apps | 6 | `int-widen`, new in `Foreword chapter MathLib` |

**Widen INSIDE the function, do not declare the narrow type.** The first
`fresh-row-id` fix declared its return as
`Tup2 (Integer between 0 and 4294967295) UnificationState`, which type
checks at the definition and pushes those bounds out to all eleven call
sites. That moves the mismatch rather than fixing it. A one-line identity
at the construction keeps every caller seeing a plain Integer.

**Only the SELF-APPLICATION catches a compiler-source violation, and
nothing weaker does.** Compiling the compiler source with the OLD seed is
clean by construction, because the old seed does not enforce the rule. The
gate's text stage is the first step where the NEW compiler reads the
compiler source, and that is where `emit-mask-extract` surfaced, after
everything else had been reported green. The check to run before believing
this class of change is: seed compiles new source to A, then A compiles new
source to B, then A and B are byte-identical. They are.

**The rechecker could not see it, and that is L-ERASED.** Run over the same
source it reports DISAGREE 0, because the violation lives at the type-check
level and the IR it reads does not carry the distinction. A tuple mismatch
is not a wrong artifact; it is an artifact that never gets emitted.

**Do not quote the file count as the cost, and this is the number that
would have been quoted.** Sweeping all 1425 chapters of `codex/test`
against the new compiler produced 93 files reporting CDX2001, of which a
control run against the seed showed 20 were already failing, leaving **73
newly failing chapters. Those 73 collapse to SIX distinct source sites**,
because they are test chapters citing a handful of shared library
chapters: `TrustTransport.codex:206` and `CdxBinary.codex:340` account for
most of them on their own. The file count is twelve times the site count,
and a report of "73 broken" would have been true, useless and alarming.

The control mattered for the same reason. `codex/test` holds deliberately
failing and negative tests, so the raw sweep reports dozens of error codes
that have nothing to do with this change; only re-running the same files
against the seed separates them. After the fixes the whole corpus is back
to that baseline: 1425 chapters swept, CDX2001 in 20 files, every one of
them a negative test under `codex/test/errors/` that was failing before.

**The error is reported at the `if`, not at the branch that is wrong, and
that cost a cycle.** `Page.codex` returns `(pg, -1, False)` on one arm and
`pg.header.slot-count` on the other; the span points at the `if`, the `-1`
looks like the odd one, and widening it changed nothing because the bounded
field was on the other arm. When a branch mismatch is reported at the join,
widen the arm that reads a DECLARED field, not the arm that looks unusual.

**Every one of the fourteen was the same shape**: a list literal or tuple
of bounded values flowing into a plain `List Integer` or `Integer`.
`infer-list` types a literal from its FIRST element with no expected type,
so `[c.cr, c.cg, c.cb]` is a `List (Integer between 0 and 255)` and cannot
join a `List Integer` with `&`. `Page.codex` is the tuple form: two `if`
branches building a `Tup3` whose integer components carry different bounds.
The language has no cast and no ascription, so the fix is always a widening
function at an ordinary position.

**Widening is spelled ONE way: `int-widen` in `Foreword chapter MathLib`.**
The language has no cast and no type ascription, so widening is a function
from the narrow type to the wide one. Four differently-named local copies
of that identity function were written while this landed -- `coap-byte`,
`urow-widen`, `reg-widen`, and three `Rgb` channel accessors -- and all of
them are deleted. Thirteen chapters now cite the one primitive.

**Bidirectional checking of the literal was considered and rejected**, and
this is the reasoning rather than a preference. It would make the same
expression mean different things by context, and under invariance
`[c.cr]` genuinely IS a `List (Integer between 0 and 255)`; calling it a
`List Integer` is a claim the author should make explicitly rather than one
the checker infers. Threading an expected type through `infer-expr-at`
would also be a large refactor bought for ergonomics alone. The cost of the
explicit form is measured and small: fifteen sites in the whole tree.

**The diagnostic was unusable and is fixed in the same change.**
`type-desc` rendered every `IntegerTy` as bare `"Integer"`, so every site in
this class reported `Type mismatch: Integer vs Integer`, naming neither
side. It now prints `between lo and hi` when narrower than the default; the
compiler-source violation became findable the moment it said
`Integer vs Integer between 0 and 65535`. No `.expected` in the tree pins
that text. Shipping the rule without this would have left the next reader
the dead end it cost a cycle here.

**A tuple mismatch has NO span.** `Desugarer.codex` lowers a tuple to a
`MkTupN` application carrying `synthetic-span`, so the error prints with no
file and no line, in a 57,000-line payload. That is worth fixing on its own
and is not in scope here; the bounds in the message are what made it
tractable.

**Stage 4. The bounds question (R4), separately.**
299 findings. Not scoped here.

**Ordering is forced.** Stage 2 cannot precede stage 1 without the
rechecker inventing the rule it is supposed to be checking against, and
stage 3 is independent of both.

## 6. How this goes wrong

**Do not write the rules out of `TypeChecker.codex`.** The guide becomes
a transcript of the implementation, the rechecker is then written from
that transcript, and the two agreeing means only that both read the same
file. The rule is decided, then published, then both implementations are
measured against it. This is the one failure that would quietly destroy
the value of the whole C2 lane, and it is the cheap path, which is why
it is called out here.

**Do not let stage 2 report a smaller UNSUPPORTED count as success.**
The deliverable of stage 2 is the kill-rate and the disagreement set,
not the abstention count going down. An abstention converted to a false
AGREE is worse than the abstention, and section 6 forbids it in as many
words.

## 7. Memory and time (rule 8)

The measurement code already landed is diagnostic and cold: it runs only
where a comparison has ALREADY come back undecided, and it allocates a
short Text label on a path that was already building a finding record.
Every walk is fuel-capped at 32 and answers `fuel-exhausted` rather than
recursing, and that bucket measured zero.

Stage 2's substitution map is the one thing here with a real cost. It is
per application, released with the definition being checked, and must
not be retained across definitions; section 10 of
`IndependentRechecker.md` already binds that and the per-definition
boundary is the reclaim point.

## 8. Cross-references

- `docs/Designs/Active/Tools/IndependentRechecker.md` sections 6, 7 and
  9 -- the output contract, the independence rule this exists to
  protect, and the tuned-quiet warning
- `docs/DevelopersGuide.md` `## Types` (line 94) and the `map` example
  (line 135) -- where the gap is
- `docs/PM/CurrentPlan.md` -- C2, and the variance ruling in the queue
