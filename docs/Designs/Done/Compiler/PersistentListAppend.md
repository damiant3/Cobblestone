# Persistent list append without quadratic memory -- REFUSED

**THIS DESIGN IS REFUSED AND EVERYTHING BUILT FROM IT IS REMOVED FROM THE SEED
(Damian, 2026-09-03). It is kept only as the account of what was tried and why
it was wrong. There is a MORATORIUM: no agent discusses `list-push`,
`list-snoc`, `&` or list-append semantics again, and this is not to be
reopened. Do not read anything below as a plan.**

**The premise was wrong.** `list-push` mutating its argument in place is not a
defect to repair, it is the contract, and it is what makes this compiler fit
its memory budget: **it took more than 4 GB before appends grew in place.** In
Damian's ruling, `list-push` in no case should ever copy the list, ever; and
**a holder with an alias to a list that is going to mutate deals with it AT THE
HOLDER** -- the one caller that needs the old value copies it there, where one
caller pays, instead of every call site paying through the allocator.

The evidence was available the whole time and was read backwards three times:
capacity equal to length killed `apps/brotli-hostile` and
`apps/deflate-hostile`; a build that copied at unproven call sites could not
compile a three-line source; and the compiler's own driver calls
`__list-with-capacity` precisely to obtain the in-place write.

Removed from the seed: the `list-push-copy` builtin, `__list_snoc_copy` and its
x86 emission, the wasm `$list_push_copy`, and the whole `own-report` /
`own-rewrite` ownership apparatus. `codex/compiler/Types/Builtins.codex` now
states the contract at the builtin table, because the ABSENT contract is what
made the behaviour read as a bug and pulled two agents into rebuilding it.

*Originally proposed 2026-09-02 by blu under a standing rule that has since
been closed.*

## Where the implementation actually stands (blu, 2026-09-03)

`own-rewrite` is landed, registered in `run-ir-pass`, and OFF by default
(`passes=+own-rewrite`). It proves ownership three ways:

1. a parameter clause (a) accepts, `own-param-owned`;
2. a `let`-bound name whose bound value is FRESH (a push result or a list
   literal) and which the body uses at most once;
3. a push applied directly to another push's result, with no `let` between.

2 and 3 rest on a single inductive argument: a push site this pass has decided
owns its block either way, because a KEPT site was proven owned and a REWRITTEN
site allocates.

**Clause (a) is answered in EVALUATION ORDER rather than by counting**
(`own-scan`, a four-state scan: none, used, push, bad). `own-seq` composes two
things that both run and is where the rule lives: once the left is `push`, any
use on the right is `bad`. `own-alt` composes ALTERNATIVES, where a read in the
arm not taken does not follow the push in the arm that was, and that
distinction is exactly what a count cannot make -- `if t == 64 then w else ..
list-push w ..` reads `w` in one arm and pushes it in the other. Clause (b),
escape, is folded into the same walk: a use inside a lambda, a fork, a record
field, a field store or a handler clause is `bad` outright.

**Census over the compiler's own reachable IR: 69 push sites, 51 keep the
in-place path, 18 copy.** It read 24/45 with rule 1 in its counting form, then
49/20 after rules 2 and 3, then this. Each figure supersedes the one before
rather than measuring a different population.

**TWO copies remain in self-recursive definitions, and they are the two sites
the analysis must NOT fix**: `opening.codex:1474` and `:1503`, the deliberate
in-place mutation described in the section below. Every other self-recursive
push in the compiler now keeps its fast path, `sha256-schedule-loop` and
`sha512-schedule-loop` included. **They are the whole of the remaining work,
and it is in their source rather than in the rule.**

**The arm that certifies clause (a), because it fails on the working shape**
(L-VACUOUS): one definition reads its list parameter twice then pushes, another
pushes then reads. The first keeps its in-place path, the second copies, and
the fresh `let`-bound result inside the second still keeps. Were `own-seq`
wrong, both would keep, so the arm can actually fail.

**The two controls the pass is held to**, both re-run after each widening: on a
program with no push sites the emitted IR is byte-identical with the pass in
the pipeline and without it, so the tree rebuild drops nothing; and on a probe
carrying both shapes the owned accumulator keeps `list-push` while a twice-used
parameter copies, which is what distinguishes a pass that DECIDES from one that
rewrites everything and reports the same green (L-CAPABILITY-LOST).

## RETRACTION: every census figure below is WRONG, and so is the diagnosis (blu, 2026-09-03)

**Read this before any number in the two sections that follow.** They were
measured with a broken extractor and they are off by roughly a factor of ten.

**The instrument.** The census read the compiler's IR dump out of a compile
log by taking the text between the first line matching `IR-BEGIN` and the
first line matching `IR-END`. The subject is the COMPILER, and the compiler
contains `emit-ir-uni`, the definition that PRINTS those markers, so the string
`IR-END` appears inside that definition's own IR at log line 1069. The
extractor stopped there. **The marker appeared in the data because the data is
the program that emits the marker**, and nothing about the result announced it:
the region parsed, the counts were plausible, and both arms were cut at the
same place so comparisons between them still looked consistent.

**Measured correctly, over lines 19..5896 rather than 19..1068:**

| | published | actual |
|---|---|---|
| definitions seen | 710 | **5,538** |
| push sites | 69 | **740** |
| keep in place | 51 | **415** |
| rewritten to copy | 18 | **325** |

So "69 push sites" was 13 per cent of the program (L-DENOM: the figure was
exact, current, honestly produced, and answered a question about a sample while
being read as the population).

**What that invalidates.** "TWO copies remain in self-recursive definitions and
they are the two the analysis must NOT fix" is unfounded: there are 325 copy
sites spread across the whole compiler, and the ones named below are simply the
ones that happened to fall in the first 13 per cent. **The diagnosis built on
it is also retracted** -- `opening.codex:1474` and `:1503` are NOT the cause of
the crash. Fixing both (they now return the list they build; shelved as blu
22263) leaves the crash byte-identical, same registers, same
`mcopy-labels+0x10E`. A bisect confirms it from the other side: rewriting
everything EXCEPT the `Opening` chapter still crashes, while rewriting the sha
chapters alone does not.

**What survives, because it was measured on small probes whose extraction was
complete (verified: exact and loose region bounds agree on those logs):**

- the identity control, emitted IR byte-identical with the pass in the
  pipeline and without it on a program with no push sites;
- the owned-versus-shared discrimination arm, and the read-then-push versus
  push-then-read ordering arm;
- five list-heavy subjects (`hamt-test`, `sort-test`, `list-growth`,
  `typeclass-smoke`, `linear-smoke`) producing identical output with the pass
  on and off;
- the growth arm reading LINEAR with the pass on, its QUADRATIC control still
  firing.

So the analysis still decides correctly on every shape tested, and the memory
answer still holds. **What is open is why a compiler built with the rewrite
cannot compile, and the honest state of that is: not yet known.** Two
hypotheses have been tested and refuted (the chunk sites; pipeline order
against the inliner). The next step is a real bisect over the 325 sites rather
than a third guess.

## The end-to-end arm has now RUN, and it is a crash, not a memory problem (blu, 2026-09-03)

Arms 2, 3 and 5 asked whether the fix costs memory. Measured, with the pass on:

- **Arm 2, the calibrated growth harness (`codex/test/ops/list-growth`),
  is unmoved.** `lin 100->200` and `lin 200->400` both read LINEAR compiled with
  `passes=+own-rewrite`, exactly as with the pass off, and the `quad` control
  still reads QUADRATIC in both, so the instrument can still see a blow-up. The
  accumulator loop keeps its in-place path.
- **Arm 5 does not get as far as a number, because the compiler built WITH the
  rewrite applied to itself CANNOT COMPILE.** Not its own 3.1 MB source, and
  not `codex/test/factorial.codex` either: `!EXC=06`, `CRASH in
  mcopy-labels+0x10E (invalid opcode)`, `RDI=0`. The same source compiles
  cleanly under the same compiler built without the pass, which is the control.

**The first reading of this was wrong and the correction matters.** On the
compiler's own source the failure is `CDX9002: Deck overflow in LEX`, which
reads as memory pressure and invites raising `demand-lex-floor`. It is not:
`-Decks 300` fails identically, and a three-line source fails too. A defect
that presents as a deck overflow on a big input and a null dereference on a
small one is one defect wearing a memory costume, and tuning deck floors would
have been the expensive wrong move.

**The crash is on the path the section below predicts.**
`tail-copy-binding-chunk` reads `list-at chunk j` for `j < n` and hands each
element to `mcopy-type`, which reaches `mcopy-labels` (`Unifier.codex:1208`).
When the pushes into `chunk` COPY, `chunk` stays empty while `n` still counts,
so that loop reads past the end and `mcopy-labels` dereferences a null. The
crash site is the corroboration rather than the claim (L-MECHANISM).

**A timing figure that had to be thrown away.** The rewritten compiler
"finished" the self-compile in 1.9 s against 13.7 s. That is the early exit of
a failure, not a speedup, and it is exactly the shape L-FASTER names: a
component that stops doing the work reports a better number.

**So the remaining work is NOT in the analysis.** The analysis reaches 51 of 69
sites and costs no measured growth. What blocks enabling is the two sites
below, and they need a source change: the chunk functions must RETURN the list
they build rather than a bare count, and their callers must read the returned
one. Passing `pushed` down the recursion is not enough on its own, because the
CALLER still reads its own `chunk`.

**One correction to how the census was reported.** "Only two copies remain in
self-recursive definitions" was true and was the wrong axis to reassure anyone
with (L-AXIS): the 18 copying sites are concentrated in the driver --
`compile-desugar-and-scope` 4, `compile-type-check` 3, `compile-frontend-cdx`
3 -- and none of those are self-recursive while all of them carry large lists.
Self-recursion is not the same set as loop-shaped risk.

## STOP: "copying is always semantically correct" is FALSE in this tree, at two measured sites

**Everything below that treats the copying path as always-safe and in-place as
a mere optimisation is wrong, and the census in the call-site rewrite (blu,
2026-09-03) is what found it.** `opening.codex:1474` and `:1503`, inside the
demand-driven type resolution, read:

    in let pushed = list-push chunk resolved
    in tail-resolve-binding-chunk st bs (i + 1) len chunk scratch-base bivy-base (n + 1)

`pushed` is bound and **never referenced** -- those two lines are the only
occurrences of the name in the file -- and the recursion carries `chunk`, not
`pushed`. The caller allocates `chunk` as
`deck-record (__list-with-capacity (len - i))` precisely so the in-place path
is reachable, takes back a COUNT, and then reads the elements out with
`tail-copy-binding-chunk chunk 0 n acc mc`. The mutation IS the mechanism, the
returned pointer is deliberately discarded, and the preallocated capacity is
the signal that this is intended rather than accidental.

So a change that makes `list-push` persistent does not make these sites slower,
it makes them WRONG: the copy is thrown away, `chunk` stays empty, `n` still
counts, and the caller reads `list-at chunk j` over a list that has nothing in
it. Silently, in the compiler's own type resolution.

**This is why the flip to copy-by-default cannot be a mechanical rename.** Both
sites must first be rewritten to consume the value they push
(`in let chunk2 = list-push chunk resolved in ... chunk2 ...`), or to use an
explicitly-mutating spelling that survives the flip. Until then any pass that
rewrites `list-push` to a copying spelling MUST be off by default, which is why
`own-rewrite` is registered in `run-ir-pass` and kept out of
`default-ir-pipeline`.

The other ten `__list-with-capacity` sites were checked and are NOT in this
shape: `acc-all` and `acc-et` (`opening.codex:699,701`) are threaded through
and returned by value, so their capacity is headroom rather than a dependency.
Twelve sites total, two of them load-bearing.

## The acceptance bar, and the one place it is ambiguous

The rule is absolute: **if a change makes even ONE line of code that is
currently linear go quadratic, that change has utterly failed.**

There is exactly one reading this design has to fix before it can be measured
against, and it is stated here rather than assumed. Today `list-snoc` mutates
the caller's block whenever there is spare capacity, so a snoc in a loop is
amortised O(1) **whether or not the program that contains it is correct**. Some
of those sites are linear today only because the aliasing defect is present:
the old list is observed afterwards and silently sees the new element. Making
those sites correct necessarily makes them copy.

**This design reads the bar as protecting currently-linear AND currently-CORRECT
lines.** A line that is fast today only because it is silently wrong is not a
line the bar defends. Every such site is enumerated by the census in step 1
rather than waved through, so the reading is falsifiable: if the census finds a
site that is correct today and goes quadratic under this design, the design has
failed by its own terms and must be withdrawn.

## What is actually broken

`__list_snoc` (`Emit/X86_64ListHelpers.codex:224`) has three paths, and the
list layout is capacity at `[p-8]`, length at `[p+0]`, element `i` at
`[p + 8 + 8i]`:

1. **spare capacity** (`len < cap`): store into the caller's block, bump the
   length in place, return the SAME pointer.
2. **frontier extend**: the block ends exactly at the heap or deck frontier, so
   the frontier moves and the block grows in place.
3. **copy**: allocate a new block and copy.

Paths 1 and 2 mutate a value the caller may still hold under another name, so
`list-snoc` is not persistent. The probe in COMPILER-42 answers
`e-len 2, p-len 2, q-len 2, lit-len 1, one-len 2` where a persistent snoc
answers `0, 1, 1, 0, 1`.

The naive repair, capacity equals length everywhere, was measured and is
refused: it makes every snoc copy, which is O(N^2) bytes in a bump allocator
that never reclaims, and it took `apps/brotli-hostile` and `apps/deflate-hostile`
from green to `memory.grow` returning -1.

## The design: mutate only what is provably dead

Keep the representation exactly as it is. No header change, no tree, no change
to any read path. Every operation other than snoc is untouched, which is what
keeps the blast radius off the rest of the language.

**The rule.** `list-snoc xs v` takes the in-place path if and only if the
compiler proves `xs` is uniquely owned and dead at that call. Otherwise it
copies.

Two facts make the proof cheap and local, and neither existed a month ago:

- **COMPILER-38 (main 20995) gave every binder a unique IR name inside a
  definition**, so two live bindings never share a name. A plain backward
  liveness walk over one definition's IR is therefore sound without any
  renaming or alias machinery of its own.
- The compiler already carries an ablation-scored inference of this shape, the
  Cost Model's `growing` inference, so the pattern of inferring a property per
  definition and scoring it against a corpus is established rather than new.

**Ownership test, per call site, all within one definition.** `xs` is owned iff

- (a) its binding has no use after this call, and
- (b) it was never stored into a heap object, captured by a lambda, returned,
  or passed to any call other than this one, before this point, and
- (c) it is not a parameter, unless the accumulator rule below applies.

**The accumulator rule, which is the whole point.** Clause (c) would otherwise
reject the shape that matters, because the canonical fast loop is tail
recursion carrying the list as a PARAMETER. So: for a self-recursive definition,
a parameter is a **linear accumulator** if within that definition it is only
ever (i) passed to `list-snoc`, and (ii) passed to the same parameter position
of a self-call, and is never stored, captured, returned or passed elsewhere.
That is a decidable per-definition property needing no interprocedural
analysis and no new syntax, and it is exactly the shape of the accumulator
loops that are linear today.

Anything not proven owned copies. Correct by default, fast where it is proven.

## R-COST

The analysis is a backward walk over one definition's IR, linear in the size of
that definition, run once per definition at lower time. It allocates nothing in
the emitted program. The emitted in-place path is byte-identical to today's, so
a site that keeps the fast path pays exactly nothing at runtime; a site that
loses it pays a copy it should always have been paying for correctness.

## The measurement plan

A green corpus cannot establish this. The current defect is silent and 52 of 60
subjects pass over it, so "the tests still pass" is what a change that removes a
check reports as well as one that fixes it (L-CAPABILITY-LOST). Six arms, and
arm 6 is the one that makes the rest mean anything.

1. **A static census of every `list-snoc` call site, by assigned path.**
   Publish SITES, not a total (L-PARTIAL). Every site that moves from in-place
   to copy is listed individually, with whether it sits inside a loop. A site
   that is correct today, is in a loop, and moves to copy is a failure of the
   bar and stops the change.

2. **Growth, not pass/fail.** For each subject, run at N, 2N and 4N and record
   heap high-water mark. Linear roughly doubles per doubling; quadratic roughly
   quadruples. A pass/fail arm cannot tell these apart, so the arm records the
   RATIO and the bar is on the ratio (L-THRESHOLD: count the signature rather
   than threshold the magnitude). **This harness does not exist yet and must be
   built first.** `build/test-growth.ps1` is NOT it and must not be pressed into
   service: it ballasts the compiler SOURCE and asserts pingpong stays
   byte-identical, which is a different question entirely.

3. **The two known victims are the positive controls.** `apps/brotli-hostile`
   and `apps/deflate-hostile` are the subjects the naive repair killed. They
   must stay green and their heap high-water mark must not move materially. If
   they move, the ownership inference is not reaching the accumulators that
   matter.

4. **The correctness arm must FAIL before the change.** COMPILER-42's probe,
   two snocs off one empty literal, answers `2, 2, 2, 1, 2` today and must
   answer `0, 1, 1, 0, 1` after. It discriminates because it is red now
   (L-VACUOUS): an arm that passes before and after measures nothing.

5. **The compiler's own memory contract is the ceiling.** The full self-compile
   must stay within the standing 2 GB heap high-water mark in BOTH text and CDX
   modes (CurrentPlan, "THE COMPILER'S MEMORY CONTRACT"). The self-compile is
   the largest list-heavy program in the tree, so a hot accumulator that loses
   its fast path shows up here before it shows up anywhere else.

6. **Sabotage the inference, and this is the arm that certifies the others.**
   Force the ownership answer to "not owned" for one named accumulator that the
   inference otherwise accepts, and confirm arm 2 actually reports a quadratic
   ratio for it. If it does not, arm 2 cannot observe the failure it exists to
   catch and every green above it is worthless (L-SABOTAGE). Compute what the
   sabotage does to the subject's actual input sizes BEFORE believing the
   result: an accumulator that never grows past its initial capacity will not
   move under sabotage, and choosing that one returns a free pass.

## What this design does NOT do

It does not make `list-snoc` persistent in the presence of aliasing the
inference cannot see; those sites copy, which is correct and slower. It does
not change `&`, `list-append`, or any read path. It does not touch the plugs:
reek's wasm-side change (literal capacity equals length, frontier-extend paths
removed) is compatible with this and the second-push aliasing it left is what
this closes, but the plug emitters consume the same ownership decision from the
IR rather than each inventing one, which is the COMPILER-38 lesson applied to
this path.

It does not propose an ownership ANNOTATION. Codex has `linear`, and a future
row may let an author declare an accumulator explicitly, but requiring a
declaration to get today's speed would be a regression for every existing
chapter and is therefore out of scope here.

## Stage 1, first census: the compiler quire (blu, 2026-09-02, reading only)

Measured over `codex/compiler`, excluding `build-output`. **These are
DEFINITIONS, not call sites**, and the distinction is the point of the row: a
count of definitions is a shape, and the number of call sites inside them is
not yet measured.

| population | count | what it means for the design |
|---|---|---|
| definitions parsed | 4,285 | the denominator, from the selector rather than the directory |
| containing `list-push` | 603 | every definition the change can possibly touch |
| self-recursive | 373 | a loop, so a copy here is the quadratic risk |
| self-recursive AND pushing onto a PARAMETER | **358** | linear-accumulator candidates: the rule is meant to keep every one of these in place |
| self-recursive, pushing a non-parameter | 15 | need individual reading; the accumulator is not the obvious one |
| not self-recursive | 230 | a single push. It cannot go quadratic by itself, though it can sit inside someone else's loop |

**What this census does NOT establish, stated because a number without its
limits becomes a false claim in the next reader's hands.** It does not check
that the parameter is threaded to the SAME argument position of the self-call,
nor that it never escapes by being stored, captured or returned. Both are
required by the ownership rule. So **358 is an UPPER BOUND on the compiler's
accumulator population**, not the answer, and the 15 and the 230 are the
populations that need real reading rather than a pattern.

**The first version of this instrument was SATURATED and said 608 of 608 were
self-recursive.** The body it searched included the definition's own header
line, so every definition trivially contained its own name. It was caught by a
control rather than by inspection: `copy-sx-pos` is not self-recursive and the
instrument said it was. The controls that now stand are `copy-sx-pos` False and
`shl-1` True, and any re-run of this census should keep them. A saturated
instrument reads as a strong finding, which is exactly why the control is not
optional (L-FALSIF).

## Stage 1, tightened: argument position and escape (blu, 2026-09-02, reading only)

Root asked for a measured set rather than a cap. Every occurrence of each
candidate accumulator parameter is now classified into exactly one bucket:
PUSH (it is the list being pushed onto), READ (`list-length`, `list-at` and
friends), SELF (it is threaded into a self-call), RETURN (it is the value of a
base case) and OTHER.

| | |
|---|---|
| candidate (definition, parameter) pairs | 366 across 358 definitions |
| **every occurrence accounted for, OTHER = 0** | **289** |
| at least one unclassified occurrence | 77 |

**A CORRECTION TO THE RULE ABOVE, FOUND BY READING TWO OF THE 289.** Clause (b)
lists "returned" as an escape that forbids the in-place path. That is wrong,
and it would reject the exact shape the rule exists to protect. `hex-to-bytes`
and `filter-library-cites` both end `if i == len then acc`: the accumulator IS
the result at the base case, and every accumulator loop in this tree is written
that way. **Returning the accumulator is a MOVE of ownership to the caller, not
a second live reference to it**, so it must be permitted. What clause (b) has
to forbid is a reference that stays live ALONGSIDE the returned one: stored
into a heap object, captured by a lambda, or passed to a non-self call whose
result outlives the loop. The rule is amended here rather than in place above,
so the reasoning stays visible.

**THE FIRST TIGHTENED COUNT WAS 290 AND IT WAS ARRIVED AT BY TWO ERRORS THAT
CANCELLED.** PUSH and SELF both matched the same occurrence in
`hex-to-bytes t (i + 2) len (list-push acc ...)`, double-counting it, while the
base-case `then acc` was counted by nothing. The double-count and the omission
were equal and opposite, so the total looked plausible and the categories were
meaningless. Per-occurrence, mutually exclusive classification gives 289, which
is barely different and is the point worth keeping: **the number moved by one
while the reasoning under it went from invalid to sound**, so agreement between
two versions of a count says nothing about whether either was measured. The
standing control is `hex-to-bytes`, which must classify as PUSH 1, RETURN 1,
OTHER 0.

**What is still not established.** SELF does not verify the ARGUMENT POSITION:
it fires when the parameter appears anywhere in a line that also names the
definition, so a parameter threaded into the wrong slot of a self-call would
still count. The 289 is therefore a measured set under a stated classifier, not
a proof, and stage 2 owes the position check. The 77 need individual reading;
one has been read (`copy-as-defs-guarded`, whose extra occurrence is a second
base case under a heap-ceiling guard, so it is a RETURN the classifier missed
rather than an escape), which suggests the 77 is dominated by multi-base-case
loops rather than by real escapes. That is a hypothesis, not a measurement, and
it is written down as one.

## Stage 2, argument position: 277, and it is a FLOOR not a ceiling

The census now also checks the thing stage 1 owed: that every self-call passes
the accumulator at ITS OWN parameter slot. Arguments are split at top level with
a paren-depth splitter, so `parts[k+1]` is argument `k`; the standing control is
`hex-to-bytes` (accumulator at index 3, one self-call, threaded correctly) and
`filter-library-cites` (two self-calls, one pushing and one passing it bare,
both correct).

| | |
|---|---|
| candidate (definition, parameter) pairs | 366 |
| occurrence-clean (stage 1, OTHER = 0) | 289 |
| position-clean (every self-call threads it at its own slot) | 335 |
| **both** | **277** |

**THE 31 POSITION FAILURES ARE MOSTLY MY CHECKER, NOT THE CODE, AND THE ERROR
RUNS TOWARD REJECTING GOOD ACCUMULATORS.** Three were read and all three are
artifacts:

- `codex-emit-if-chain` binds `acc2 = list-push acc ...` and threads `acc2`
  into the self-call. That is a genuine linear accumulator reached through a
  `let` alias, and a checker looking for the literal parameter name cannot see
  it.
- `fill-empty-values` is correct at its one real self-call. Its second "call"
  is `offset-table-empty` SEEDING it from outside with a fresh
  `__list-with-capacity`, which is exactly what a caller should do.
- `synth-instance-defs` was not read.

So **277 is a LOWER BOUND and the real population lies between 277 and 335.**

**A SYSTEMATIC SLICING BIAS IN EVERY NUMBER THIS DESIGN HAS PUBLISHED, found
only at stage 2 and stated rather than quietly corrected.** The definition
splitter ends a definition when it sees a `name (args) =` header, so a
definition written `name : Type = ...` with no parenthesised parameters does
NOT end the previous body. Those bodies over-run into the following
definitions, which can add spurious `list-push` mentions, spurious occurrences
of the parameter name, and, as in `fill-empty-values`, calls from a NEIGHBOUR
counted as self-calls. Every figure above and in the two sections before it
carries that bias, and its direction is not uniform: it inflates the
denominators and it pushes pairs OUT of the clean sets. The counts are
therefore indicative of shape and must not be quoted as exact.

**NEITHER LIMITATION SURVIVES INTO THE REAL ANALYSIS, which is the reason the
design puts the walk on the IR rather than on source text.** The IR-level
liveness walk follows DATAFLOW, so a `let` alias is transparent to it, and it
sees definition boundaries structurally rather than by indentation. COMPILER-38
having made every binder uniquely named inside a definition is what makes that
walk sound without alias machinery. The value of this source-level census is
that it bounds the population and names the shapes; it is not, and cannot be,
the analysis.

## Stage 3, the IR walk: 267 owned, and 64 of them are the loops

Measured 2026-09-02 (blu, reading only) over the whole-compiler concat
`build/output/Codex.codex` emitted as `IR-UNI` by seed `15A1A565`. The IR is
one `(def "name" "chapter" (params ...) type body 0 0)` per line, so
definition boundaries are structural and the stage 1/2 slicing bias is absent
rather than reduced.

| population | count |
|---|---|
| distinct definitions in the IR | 5,526 |
| candidate (definition, parameter) pairs: a list-typed parameter that is pushed onto | 358 |
| **OWNED (no escaping occurrence)** | **267** |
| rejected | 91 |
| reached through a `let` alias, invisible to stage 2 | 11 |

**The split stage 2 could not make, and it is the one the bar turns on.** Of
the 267 owned pairs, **64 are self-recursive loops** and 203 are single-shot
builders that push and return without recursing. The acceptance bar protects
currently-linear lines; a builder that pushes a bounded number of times is not
where quadratic growth lives. **64 is the set the ownership inference has to
reach**, and it is the denominator for arm 1 of the measurement plan.

**Do NOT read 267 against stage 2's 277 as a delta.** The populations differ:
the IR covers the whole self-concat including lifted lambdas, stage 2 covered
`codex/compiler` source alone. The comparable result is the 11 alias-reached
pairs, which stage 2 structurally could not see, `codex-emit-if-chain` among
them -- the pair stage 2 itself published as a known false rejection.

**Why a site is rejected, and one cause dominates.**

| site | count | reading |
|---|---|---|
| stored under `field-val` | 42 | genuine escape, clause (b): the list is written into a record field |
| passed to `ir-check-ty` | 17 | genuine, a non-self call |
| captured into `MkTup2`/`MkTup3` | 14 | genuine, a tuple is a heap object |
| under `if` / under `branch` | 8 | residual, not individually read |
| other named callees | 10 | genuine |

Storing the accumulator into a record field is more than four times any other
cause. An inference built for this tree earns most of its rejections on one
clause, which is worth knowing before the implementation weighs its cases.

**THE NUMBER MOVED THREE TIMES AND EVERY MOVE WAS A DEFECT IN THE WALKER, ALL
BIASED THE SAME WAY: toward rejecting good accumulators.** 253, then 259, then
267. Four structural assumptions about this IR were made and all four were
wrong: `def` carries two trailing `0` atoms, so "the body is the last element"
analysed the atom `0` and reported ZERO candidates tree-wide; `branch` carries
a trailing `bool-lit`, so the same rule selected the flag; a `match` puts its
arms two levels down under `branches`, so a walk over the match's own children
marked no arm tail and every accumulator returned from an arm read as an
escape; and a self-call does not always receive the accumulator as a bare
name, because the canonical filter loop threads it through a conditional --
`keep-not-int t (i + 1) (if p acc (list-push acc x))` -- which a walk
recognising only a bare name in the slot rejects, and that is precisely the
shape the rule exists to protect. Self-call argument positions are now
dataflow-transparent the same way a `let` binding is.

**Read the node's arity before writing the walk that depends on it.** None of
the four was guessed correctly and each was settled by one command against the
tree.

**The controls, and the one that matters is the third.** `hex-to-bytes` gives
PUSH 1, RETURN 1, OTHER 0 and `filter-library-cites` gives PUSH 1, SELF 1,
RETURN 1, OTHER 0, both as stage 2 states them. The negative control is
`compile-parse`, which must and does REJECT, `under field-val`, and the escape
was confirmed by reading the IR rather than by trusting the label: three green
arms from a classifier that could only ever answer "owned" would look
identical to three green arms from a correct one (L-FALSIF). Each of the four
repairs above was re-run against all of them, so no repair was accepted on the
strength of the number it produced (L-CAPABILITY-LOST).

**The population is accounted for in both directions.** 5,552 `(def` lines
parse to 5,527, and the 25 that do not are truncated duplicates from a second
IR emission in the same log; every one has a complete copy that parsed, so
nothing is lost. That check exists because a definition dropped in parsing is
invisible to every other check here -- it never enters the population, and a
missing candidate is indistinguishable from a definition with no accumulator.

**What stage 3 does NOT establish.** It is a census of ownership under the
stated rule, not the inference and not a measurement of growth. Arms 2 through
6 of the measurement plan are untouched: no growth harness exists yet, and
until arm 6 sabotages the inference and arm 2 is shown to report a quadratic
ratio for it, none of these counts says anything about whether the change is
safe. The 8 unread `under if`/`under branch` residuals are the place a fifth
walker defect would be hiding; they are named here rather than rounded away.

## Stage 4: the accumulator rule was too narrow, and the axis was wrong

Stage 3 left 30 pairs that are linear TODAY and would move to copy, which the
acceptance bar does not permit. Reading them settled what they are: 19 of the
30 escape only into other CALLS, and those calls are the mutually recursive
walker families the AST and IR passes are built from -- `ir-dce-collect-expr`
and `ir-dce-collect-stmts`, `inline-expr` and `inline-act-stmts`,
`collect-rt-mentions` and `collect-rt-mentions-stmts`, `ir-tvars-of-type` and
`ir-tvars-of-type-list`. The accumulator is threaded to a partner and comes
back. That is the shape the rule exists to protect, and the rule's clause
"passed to the same parameter position of a SELF-call" cannot express it.

**Three arms, same parse, so the delta is the rule and nothing else.**

| arm | rule | OWNED of 358 | recursive | rejected |
|---|---|---|---|---|
| A | self-call only (stage 3) | 267 | 64 | 91 |
| B | same parameter slot within the call graph SCC | 276 | 73 | 82 |
| C | **ownership TRANSFER, no SCC requirement** | **301** | **98** | **57** |

Arm A is the control: it reproduces stage 3's 358 / 267 / 64 / 91 exactly from
the rewritten walker, so the restructure into occurrence records and a
fixpoint changed nothing but the rule.

**THE SCC FRAMING IS THE WRONG AXIS (L-AXIS), and arm B is kept here to show
it.** Grouping by strongly connected component recovered only 9 of the 30. The
four families above then sat in one component and were STILL demoted, with
zero hard escapes, because each also passes the accumulator to a helper
OUTSIDE the cycle: `ir-dce-append-list`, `ir-int-member-loop`. Both helpers
are themselves owned. Strong connectivity is a property of the call graph;
what the rule needs is a property of OWNERSHIP. A callee that takes the
accumulator at an owned slot and hands it back is a MOVE, which is the same
reading the design already applied to returning (stage 1, tightened). The
SCC test simultaneously rejects owned helpers and would admit a same-component
callee that stores the list, so it is wrong in both directions.

**The rule, restated.** A use of the accumulator is acceptable if it is a
push, a read, a return, a self-call at its own slot, or **a call to any
definition whose corresponding parameter slot is itself an owned
accumulator**. Ownership is a greatest fixpoint over (definition, parameter)
pairs: start optimistic and demote until stable. Over-accepting is the
dangerous direction, since it would license an in-place write to a list the
caller still holds, so a pair survives only if nothing demotes it. The
fixpoint settles in 4 rounds over 358 pairs.

**A family partner must be a CANDIDATE even when it never pushes.**
`ir-dce-collect-stmts` threads the accumulator and calls no pusher itself.
Requiring PUSH >= 1 to enter the census leaves its slot unknown, the
fixpoint's "is (callee, slot) owned?" can never be answered yes, and no
family can ever close. That single omission is why arm B recovered 9 rather
than 28. There are 2,565 such threaded-only slots; they are candidates for
the fixpoint and are NOT added to the 358, so the population stays comparable
to stage 3.

**What is left, measured rather than estimated.** 32 pairs are still both
recursive and rejected, and asking the walker to name the callee that
receives the accumulator splits them cleanly:

| | count | verdict |
|---|---|---|
| a hard escape: stored under `field-val`, or a bare use | 13 | correct rejection, the bar does not defend these |
| passed into a CONSTRUCTOR (`MkTup2`, `MkTup3`, `VecPat`, `LiCall`) | 10 | correct rejection: a tuple is a heap object |
| passed to an ordinary function | **9** | genuinely open |

So **23 of the 32 are real escapes and only 9 are open**, against the "19 need
individual reading" this section first published. The difference is entirely
that a capture into `MkTup2` is recorded as a CALL rather than an ESCAPE, so
it never appeared in the escape column while being one.

**The 9, with the callee that blocks each.** `occ-insert` and `env-replace-in`
reach `list-set-at`; `collect-type-var-ids` reaches `list-contains-int`. Those
are list PRIMITIVES missing from the walker's reader and pusher tables, so
they are an instrument gap and not a property of the tree: a call that only
inspects the list is a BORROW and must not break ownership, and `list-set-at`
returns an updated list, which is a transfer. The remaining six are
per-function questions that need reading: `check-all-defs` (`check-batch-*`),
`scan-chapter-pages` (`set-last-page`), `build-x86-arities`
(`sort-x86-arities`), `lower-call-args` and `lower-match-chain`
(`lower-lir-expr`, `lower-fail`), and `parse-row-tail` (`parse-effect-names`).

**Both remaining categories push the number DOWN, so 9 is an upper bound on
what is unresolved.**

### The tables completed, and the remainder falls to 2

The walker's list tables were guessed. Censusing them instead: of the 26
distinct `list-*` callees in the IR, only **five have no definition in the
tree** and are therefore primitives -- `list-at`, `list-length`, `list-push`,
`list-set-at`, `list-insert-at`. Everything else spelled `list-*`
(`list-contains-int`, `list-elem-of`, `list-replace-at`, `list-tail`) is an
ordinary definition and belongs to the fixpoint like any other callee, which
is where the borrow question is actually answered.

Two corrections followed, and the first was caught by its own denominator.
**`list-set-at` and `list-insert-at` preserve ownership but are not appends**:
admitting them as pushers moved the candidate population 358 to 444, which
would have silently changed what the census is a census OF. They are recorded
as a distinct MUTATE use that does not confer candidacy. **And a callee slot
that is only READ is a BORROW**: the callee inspects the list and cannot
retain or alter it, so passing an accumulator there does not break ownership.
Without that, a read-only helper is indistinguishable from an escape.

| arm | OWNED of 358 | recursive | rejected |
|---|---|---|---|
| A, self-call only | 268 | 65 | 90 |
| C, ownership transfer | **305** | **102** | **53** |

Arm A moves 267 to 268 under the table fix alone, which is the attributable
delta rather than drift. All five positive controls hold (`hex-to-bytes`,
`filter-library-cites`, `keep-not-int`, `ir-dce-collect-expr`, `inline-expr`)
and `compile-parse` still rejects on its `field-val` store.

**The bar-critical set is now 28, and 26 of them are correct rejections:**
13 hard escapes, 10 constructor captures, and 3 whose accumulator reaches a
real escape one call away (`lower-match-chain` and `lower-call-args` through
`lower-lir-expr`, which stores; `check-all-defs` through
`check-all-defs-done`, which stores). **Two are genuinely open**:
`collect-type-var-ids`, blocked by `codex-type-fold-children` slot 2, which
is a higher-order fold and not a candidate at all, and `parse-row-tail`,
blocked by an unclosed chain through `parse-effect-names`.

**So the design's rule covers this tree except for two pairs and a
higher-order fold.** That is the claim stage 4 supports, and it is a claim
about a CENSUS: no growth has been measured, clause (a) liveness is still
unchecked, and arms 2 through 6 remain untouched.

## Stage 5, clause (a): the occurrence census OVER-ACCEPTS, by six

Clause (a) is "the binding has no use after this call". Stages 3 and 4 count
occurrences and not their ORDER, so a definition that pushes onto the
accumulator and then reads the ORIGINAL binding afterwards scores exactly like
one that does not. That is the aliasing defect the design exists to fix, so
the census must not be silent about it.

Two decidable violation patterns, both syntactic sites where the pre-push
value is still reachable:

- **P1**: `(let b = <...push(a)...> in BODY)` where `BODY` still mentions `a`.
- **P2**: an application where one argument contains `push(a)` and a DIFFERENT
  argument mentions `a`; both are evaluated for the same call.

**Measured over the same 358 pushed pairs: 6 violations, and ALL SIX ARE
PAIRS THE OWNERSHIP CENSUS CALLS OWNED.**

| definition | parameter | pattern |
|---|---|---|
| `tail-resolve-binding-chunk` | `chunk` | P1 |
| `tail-resolve-expr-type-chunk` | `chunk` | P1 |
| `inline-expr` | `bound` | P2 x2 |
| `once-expr` | `bound` | P2 x2 |
| `inline-act-stmts` | `bound` | P2 |
| `once-act-stmts` | `bound` | P2 |

That the two walkers agree on the denominator (358 both ways) is the
cross-check that they are looking at the same candidate set. **The finding is
that ownership by occurrence-counting is not sufficient**: granting the
in-place path on it would write into a list the caller still holds at six
sites, which is precisely the defect being repaired. Read at
`tail-resolve-binding-chunk`: `chunk` is pushed into `pushed`, and `chunk` is
referenced again inside that let's body, so both lists are live.

**The instrument was proven able to fire before it was believed.** Three
synthetic definitions were fed to it: a clean push-then-use-the-result, a P1,
and a P2. It reported 0, 1 and 1 respectively. A checker that only ever
answers "no violation" reproduces a green census perfectly (L-FALSIF).

**The limitation, stated because it bounds the claim in the unsafe
direction.** Both patterns are PATH-INSENSITIVE: if the later mention of `a`
sits in a branch that excludes the branch using the pushed result, the two are
never live together and the report is a false positive. So 6 is an upper bound
on real violations, and each of the six needs a path reading before anyone
calls it a defect. What it is NOT is an upper bound on the general problem:
absence of P1 and P2 does not prove linearity, which needs the real backward
liveness walk the design specifies. This stage reports violations found, never
absence of violations as a proof.

## Arm 2 exists, and it is calibrated: `codex/test/ops/list-growth`

The measurement plan's arm 2 needed a harness that reports GROWTH rather than
pass/fail, and said it did not exist. It exists now, and it was calibrated on
two known answers before being pointed at anything.

Allocation is read with `__heap-save`, the bump pointer. Bare metal has no GC
and nothing is reclaimed inside a call, so the difference between two reads is
the allocation total over the span. Two arms with answers known in advance:
`build-lin` is an ordinary accumulator loop; `build-quad` copies the whole
accumulator before every push, so it allocates the triangular number of cells
BY CONSTRUCTION.

| arm | N=100 | N=200 | N=400 | ratio per doubling |
|---|---|---|---|---|
| `build-lin` | 1,040 | 2,064 | 4,112 | **1.99, 1.99** |
| `build-quad` | 60,368 | 238,096 | 945,808 | **3.94, 3.97** |

Two against four, with nothing between them. **That separation is the whole
value of the arm**: a pass/fail probe returns "still broken" for both shapes,
and the campaign needs to tell a change that slowed down from a change that
went quadratic.

**The fixture asserts the RATIO, never the byte counts.** A raw count is a
fact about today's allocator: it goes red the first time anything about
allocation changes, and an arm that cries wolf trains its reader to ignore it
(L-SHORT's lesson about reporting every mismatch as SHORT). The classifier
cut sits at 300 per cent, between the two measured populations of about 199
and about 396, and nowhere near either.

**What it does NOT yet do.** It measures two synthetic loops, not the tree.

## Arm 4 is RED, as it must be. Arms 3, 5 and 6 cannot run yet.

**Arm 4, run 2026-09-02 against seed `15A1A565`.** Two pushes onto one empty
literal, printing every intermediate length:

    e-len : 2    p-len : 2    q-len : 2    lit-len : 0    one-len : 1

**The defect is present and the arm discriminates**: `p` and `q` are separate
pushes onto the same empty list and they come back as ONE list of length 2,
which a persistent snoc would answer `0, 1, 1`. That is what arm 4 is for. An
arm that passed before the change would measure nothing (L-VACUOUS), so its
value today is entirely in this failing reading.

The last two positions differ from the tuple the COMPILER-42 row records
(`lit-len 1, one-len 2` there, `0` and `1` here). Both probes are built from
the row's one-line description, which does not pin down WHICH literal `lit`
and `one` name, and that is the whole of the difference; the row is not
contradicted on the defect itself, which both readings show identically. The
figure to quote is the first three.

**This probe is deliberately NOT landed as a graded fixture.** Its `.expected`
would record `2, 2, 2, 0, 1` and so enshrine the defect as correct behaviour,
which is the trap `codex/test/ops/cap-word-64` names in its own prose: a
sidecar captured from the subject asserts whatever the subject does. It
becomes a fixture when the change lands and the expected answer is the
persistent one.

**Arms 3, 5 and 6 are POST-CHANGE arms by construction and cannot be run
now.** Arm 3 requires `apps/brotli-hostile` and `apps/deflate-hostile` to stay
green with their high-water marks unmoved AFTER the repair; arm 5 asks whether
the self-compile still fits the 2 GB contract AFTER it; and **arm 6 sabotages
THE INFERENCE, which does not exist** -- stages 3 to 5 are censuses of the
rule on paper, and no compiler change has been made. Running a "sabotage" with
nothing to sabotage would produce a green that certifies nothing, which is the
failure arm 6 exists to prevent.

**So the honest state of the measurement plan is: arm 2 built and calibrated,
arm 4 red as required, arms 1 and 3 and 5 and 6 waiting on an implementation.**
The next unit of work is the ownership inference in the compiler, not another
census; every remaining arm is a question about a change nobody has made.

## The implementation shape, established by reading the call chain

**The decision belongs in LOWERING and travels as a distinct builtin name.**
Read at head 2026-09-02: `list-push` and `list-snoc` both carry
`bs-emit = \s a -> emit-helper-call-2 s a "__list_snoc"`
(`Types/Builtins.codex:84-85`), and `emit-helper-call-2`
(`Emit/X86_64Builtins.codex:43`) receives only the state and the argument
list. It has no idea which definition it sits in, so an ownership decision
cannot be consulted there without threading a per-call-site table through the
state and inventing node identities the IR does not carry.

Rewriting the CALL SITE at lower time avoids all of that: an owned site
becomes a different builtin, and each builtin names one helper.

    list-push        -> __list_snoc_copy     (always allocates; correct by default)
    list-push-owned  -> __list_snoc          (today's code, byte-identical)

**This is a precedent, not an invention.** COMPILER-36 did exactly this for
trapping arithmetic: the mode is decided upstream and "the wire spells
`mul-int-wrapping`". The emitters stay dumb, every backend and every plug
consumes ONE decision instead of each re-deriving it (the COMPILER-38 lesson),
and a site that keeps the fast path emits the same bytes it emits today, so it
pays nothing.

**`__list_snoc` path 1 is the whole defect, and it is three instructions.**
`Emit/X86_64ListHelpers.codex:224` loads length from `[rdi+0]` and capacity
from `[rdi-8]`; when `len < cap` it stores the element at `[rdi + 8 + 8*len]`,
bumps the length in place, and returns the SAME pointer. Path 2 grows at the
frontier, path 3 copies. So the copying helper already exists as path 3 and
the new helper is mostly a re-entry into it rather than new code.

**A load-bearing thing found while reading, which any implementation must not
break.** `tco-grow-temps` (`Emit/X86_64.codex:505`) carries prose saying the
identity comprehension above it "is load-bearing and must not be removed",
precisely BECAUSE `__list_snoc` extends its argument in place when that
argument is the topmost allocation: growing the caller's list would hand the
next tail call the spill slots this one just took. The compiler already works
around this defect by construction, in the register allocator, with a comment
explaining why. That is independent evidence the defect is real and reachable,
and it is a site to re-read after the change, since the workaround assumes the
in-place behaviour it is defending against.

**Order of work.** The analysis is the risky half and is verifiable on its
own: it must reproduce this design's four controls (`hex-to-bytes`,
`filter-library-cites` and `keep-not-int` owned, `compile-parse` rejected) and
must NOT mark any of the six clause (a) violators as owned. Wiring the second
helper is mechanical once the analysis agrees with the census. It is a
multi-CL arc and takes ONE token at the end, per `CoordinationProtocol.md`.

### CORRECTION, measured after the section above was written: the wire spelling costs 26 plugs

The section above proposes a new builtin name and calls it precedent-following
and cheap. **Measured 2026-09-02: `"list-push"` is named explicitly in 37
files across 26 plugs** (ada, arm64, cobol, csharp, elixir, fortran, go,
haskell, javascript, kotlin, lua, maui, nim, objc, ocaml, pascal, php, python,
recheck, riscv, ruby, rust, scala, swift, wasm, zig). A new builtin in the
shared IR is therefore a 26-plug change, not a compiler-local one, and since
L-ACCEPTED's repair made an unrecognised builtin a DIAGNOSED REFUSAL rather
than a silent pass-through, every one of those plugs breaks loudly the moment
the name appears. That is the correct failure mode and it is still a 26-plug
blast radius.

**The estimate above was wrong, and the correction is recorded rather than the
section quietly edited**, because the reasoning that produced it is the part
worth distrusting: COMPILER-36 spelled a mode on a wire whose consumers that
campaign had already enumerated, and this change does not inherit that.

**Three sequencings, and the choice is a fleet-scope question rather than a
list-append one.**

1. **Compiler-local first.** Decide ownership in the compiler's own emit path
   and never put a second name in the shared IR. The plugs are untouched and
   keep today's behaviour, so x86-64 is fixed and the other back ends are not.
   The COMPILER-42 row says the defect is on ANY back end, so this closes part
   of it and must say so.
2. **All back ends at once.** 26 plugs learn the name; each may map it onto
   its existing push, since copying is always semantically correct and
   in-place is the optimisation. Mechanical, wide, one gate.
3. **Machine-code pipeline only.** The pass registry already distinguishes
   plugs that resolve calls by NAME from those that do not (`Passes.codex`,
   the 2026-08-16 ruling, settled by probe rather than by target shape). If
   the rename rides only the pipeline the machine-code plugs consume, the
   source-emitting plugs never see it. Cheapest correct option IF that split
   holds for builtin names as well as for inline substitution, which is NOT
   measured and must not be assumed from the inline case.

Nothing here is a question for Damian under the standing rule. Sequencing
across 26 plugs is the commander's to weigh against what else is in flight.

**Two instrument defects, both found by a known-answer probe rather than by
reading a total.** A hand-rolled iterative Tarjan left 13 of 358 candidates
with NO component; an unassigned node is indistinguishable from "not in your
family" and demotes its caller silently, so the first SCC number was
unsound. It is replaced by Kosaraju (two plain DFS passes, no low-link
bookkeeping) with a postcondition that THROWS if any parsed definition lacks
a component. The probe that caught it asks a question with a known answer:
the four families above must share a component. Re-running the arms that
already passed would have measured nothing, because they pass under every
version of the rule (L-CAPABILITY-LOST).

**What stage 4 does NOT establish.** Clause (a) of the ownership test, no use
after the call, is still not checked: this census counts occurrences, not
their order, so a definition that uses the accumulator AFTER passing it on
scores the same as one that does not. That is a liveness question and it is
what the real IR walk in the compiler has to answer. Arms 2 through 6 of the
measurement plan remain untouched, and no growth has been measured by anyone.
