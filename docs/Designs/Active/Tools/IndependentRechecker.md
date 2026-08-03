# Independent Rechecker -- a plug that re-derives what the compiler asserted

**Status:** DESIGN. Not built. Phase 0 findings below are measured
against main on 2026-08-02 and must be re-confirmed before building.

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

## 4. Input: what the artifact already carries (Phase 0, measured)

**Types: already there.** `codex/compiler/Emit/IRTextEmitter.codex`,
`ir-emit-type`, is a closed match over every `CodexType` variant and
serializes the ones the safety claims rest on:

| Claim | IR form | Sufficient to re-check |
|---|---|---|
| Bounded integers | `IntegerTy lo hi mode` via `ir-emit-int-bounds` | yes |
| Effect rows | `(effectful (effs ...) (scopes ...) ret)` | yes |
| Linear ownership | `LinearTy` arm | yes |
| Sums / records | ctor payloads and field types emitted | yes |
| Units, vectors | `(unit ...)`, `(vector n t)` | yes |

So phases 1-3 below need **no compiler change at all**. The rechecker is
a plug consuming an artifact the compiler already produces, in the shape
53 plugs already use.

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
| 1 | Well-formedness re-check over IR text: every name resolved, no free variables, application arity, constructor applied to its declared field types, every match arm's payload types agreeing with the ctor | none | no |
| 2 | Bounded-integer re-derivation: every narrowing site either statically fits or carries the mode the IR declares. Independent of the compiler's own prover | none | no |
| 3 | Effect-row and linear re-derivation: no call escapes a declared row; every linear binding used exactly once on every path | none | no |
| 4 | Retain proof terms in the IR text path (mirror `keep-proof-defs` / `ir-chapter-with-proofs`), then re-check them: `Refl`/`sym`/`trans`/`cong`/`app-cong`, induction subgoals and IH use, the CDX4023 acyclicity property, the CDX4024 grammar | yes | yes |

Stage 1 is the one that would have caught the Lean bug's shape: a
declaration accepted with an argument nothing ever type-checked. It is
also the cheapest and needs nothing from anybody.

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
- **Stage 4 is the only seed-affecting work here** and can be deferred
  indefinitely without blocking stages 1-3.
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
