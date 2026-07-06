# Proof Totality Probe — Circular Proofs (FIXED: CDX4023)

**Status:** Stage 1 SHIPPED same day (2026-07-04). Circular proofs
are rejected with CDX4023; all six probes flipped to
`errors/*.failing`. Damian's ruling: fix the code to match the goal,
log bugs, no README hedging — Option A (acyclicity) built
immediately. §5's Option B (positive proof-term grammar) remains an
optional later hardening.

**Author:** blu, 2026-07-04. Part of the Vision Check honesty audit
(BACKLOG 1); the sixth leg, after effects, bounded integers, linear
ownership, capabilities, and punctual.

---

## 1. The Claim Under Audit

- README: "`Refl` verified by the unifier (**invalid proofs are type
  errors**) ... Flagship proof: `reverse (reverse xs) === xs`
  machine-checked by induction."
- GitHubUpdate30: the flagship "validates the entire dependent-type
  and proof infrastructure."
- TrustedComputingBase §4 ranks the proof layer as the #2 backing for
  correctness, "now sound after the CL that fixed the vacuous
  PropEqTy unifier arm."

The Induction campaign (val, `Induction.md`, CLs 6425-6473) made the
proof CONTENT checks real: wrong equations reject (refl-mismatch,
cong-mismatch, term-mismatch, normalize-false), unsound induction
steps reject (induction-unsound, induction-list-unsound,
reverse-reverse-unsound), and `assume` warns (CDX4021). All verified
still green this session.

What no one had probed: **totality**. Every proof assistant since
Coq's positivity/termination checkers has had to close the same hole
— a proof term that refers to itself inhabits any proposition,
because the typing rule for recursion assumes what it is proving.
Codex has no termination check on definitions, and proofs are erased
at emit (CDX4020), so the divergence that would expose the lie at
runtime never executes.

## 2. Stage-0 Probes (2026-07-04, seed 8CA1E63B, post-merge main 7035)

Six adversarial programs, each "proving" a FALSE proposition
(`Zero === Succ Zero`, or `for all (n : Nat), n === Zero`). **All six
compile clean and run.** The only diagnostic on any of them is the
info-level CDX4020 "proof erased" line — no warning, no error.

| Probe (codex/test/) | Route | Result |
|---|---|---|
| `proof-launder-self` | Annotated def, body is itself: `bad = bad` | ACCEPTED silently |
| `proof-launder-mutual` | Two defs justifying each other | ACCEPTED silently |
| `proof-launder-helper` | Generally-recursive fn returning the prop: `helper (n) = helper n`; `bad-c = helper Zero` | ACCEPTED silently |
| `proof-launder-qed` | `claim`/`proof`/`qed` sugar, `proof bad-d = bad-d` | ACCEPTED silently |
| `proof-launder-lemma-self` | Induction step cites THE CLAIM BEING PROVEN as a lemma: `is Succ (k) (ih) -> all-zero (Succ k)` | ACCEPTED silently — **and as a checked proof, not the CDX4022 unproven route** |
| `proof-launder-lemma-mutual` | Two induction proofs citing each other as lemmas | ACCEPTED silently |

Positive guards pinned alongside:

- `proof-assume-axiom` (+ `.diag` 4021): the explicit axiom door
  WARNS correctly — the trust trail works for `assume`.
- Negative control `5 === 6` rejects CDX2001 (literal equality is
  genuinely checked; the DevelopersGuide `5 === 5` example is real,
  not vacuous).

Per stage-0 convention the six launder probes are pinned as PASSING
`.expected` tests documenting the hole; the fix flips them to
`errors/*.failing`.

## 3. Root Cause

Two independent mechanisms, both silent:

1. **Proof definitions are ordinary definitions.** `register-all-defs`
   registers every def's declared type before any body is checked (as
   it must, for ordinary mutual recursion), so a proof body that
   mentions itself — directly, mutually, or through a prop-returning
   recursive helper — type-checks trivially: the mention's type IS the
   declared proposition. Nothing distinguishes the recursion principle
   the proof layer sanctions (structural induction, which generates
   its own IH soundly) from raw recursion, which assumes the goal.

2. **`instantiate-claim` has no acyclicity check.** The applicable-
   lemmas elaboration (`elab-claim-apps`, TypeChecker.codex, Stage 5b)
   resolves ANY claim by name from the def list — including the claim
   currently being proven and claims that cite the current one. The
   induction machinery itself is sound (subgoals + IH are structural);
   the lemma door circumvents it.

Erasure completes the trap: `is-proof-def` (X86_64.codex:810) erases
the diverging bodies, so there is no runtime signal either.

(Erasure side-note, found while reading: `is-proof-def` matches
proof-returning `FunTy` only to nesting depth 2 — a 3-parameter
prop-returning function would be emitted as real code. Not a
soundness issue; fold into stage 1 while the function is open.)

## 4. Severity

Highest of any Vision Check leg so far. The other legs guarded
runtime-safety claims; this one guards the layer the project calls
"proof" — the one place the word is supposed to mean something
absolute (ClaimsCalibration already made exactly this point when it
banned "WCET proofs"). A skeptical reader falsifies the README
sentence in one line (`bad = bad`), and the flagship's rhetorical
value inverts: "your compiler also accepts THIS proof of False."
With Chlipala aware of the project, assume this exact probe gets
written by someone else if we don't close it first.

The blast radius of the HOLE is confined to the proof layer: no
runtime behavior, no codegen, no memory-safety leg depends on
propositional equality today. The blast radius of the FIX is equally
small — which is why stage 1 should ship promptly.

## 5. Fix Shapes (ruling needed: A now, or A then B)

**Option A — acyclicity over the proof reference graph (recommended
stage 1).** A def is *proof-relevant* when its declared type mentions
`ProofTy`/`PropEqTy` anywhere (walk the type tree; covers values,
prop-returning functions at any arity, and containers embedding
proofs). Build the reference graph among proof-relevant defs (body
name-mentions + `instantiate-claim` citations) and reject any cycle,
including self-loops, with a new error (proposed CDX4023 "circular
proof: 'X' justifies itself (via ...)"). Sound because the only
recursion a proof legitimately needs is structural induction, which
is already checked by subgoal generation and never routes through a
name cycle. Cheap: one graph walk over the (tiny) proof-def subset at
CHECK phase. Kills all six routes.

**Option B — positive proof-term grammar (later, optional).** A proof
body may consist only of: proof builtins (`Refl`/`sym`/`trans`/
`cong`/`app-cong`/`assume`), IH names, acyclic claim applications,
`induction` expressions, and `let` of proof terms. Anything else
rejects. Stronger (closes unknown laundering routes through
general-purpose expressions), heavier, and it constrains future proof
styles. Not needed to close the known routes.

Sequencing note: Option A alone restores the README claim with the
same standing caveat every LCF-family assistant carries (axioms via
`assume` are visible in the trust trail; everything else is checked
and total).

## 6. Campaign Stages

| Stage | Deliverable | Status |
|---|---|---|
| 0 | Probes pinned (6 launder + assume/literal guards), this doc, ClaimsCalibration entry | **DONE** |
| 1 | Acyclicity check (Option A, CDX4023) + `is-proof-def` depth generalization; all six probes flipped to `errors/*.failing`; gates + seed | **DONE (same day)** |
| 2 | Optional: positive proof-term grammar (Option B) | OPEN — rule if/when wanted |

### 6.1 Stage 1 as built

- **`check-proof-cycles`** (`TypeChecker.codex`, Section: Proof
  Acyclicity), chained after `check-rt-cycles` in `check-chapter`.
  Proof-relevance is judged on the DEEP-RESOLVED checked type
  (`result.types`, index-aligned with `mod.defs`), not the declared
  type — so an undeclared intermediary (`bad = bad` with no
  signature) whose inferred type unifies to a proposition is still in
  the graph. `type-mentions-proof` walks every CodexType shape
  (fuel-capped at 64, exhaustion answers True — errs toward
  checking), including SumTy ctor payloads and RecordTy fields, so a
  proposition smuggled inside a nominal container still marks the
  carrier def proof-relevant.
- **Edges reuse the punctual machinery**: `collect-rt-mentions` with
  `self = ""` (so direct self-mentions are edges — punctual excludes
  them because `has-self-call` owns that case; proofs need them),
  `RtEdges`/`rt-edges-for`/`rt-reaches` verbatim. One new walk arm:
  `AInductionExpr` was missing from `collect-rt-mentions` (the
  punctual-lesson under-coverage — without it the two lemma routes
  slip through the very check meant to catch them); adding it also
  extends punctual's own cycle coverage to induction bodies.
- **CDX4023 CircularProof** (`CdxCodes.codex`), sev-error,
  phase-type-checker; message points to induction as the sound
  recursion and `assume` as the honest escape hatch. One error per
  cycle-participating def with a declared span.
- **`is-proof-def`** (`X86_64.codex`) generalized from hardcoded
  FunTy depth 1-2 to any arity via `is-proof-return` recursion —
  same semantics, no EffectfulTy arm added (erasing an effectful
  prop-returning def would delete its effects; they stay emitted).
- **Gates**: one-pass hard fixed point on the first build (Sut ===
  stage1, 0 non-signature byte diffs; the selfhost has no
  proof-typed defs so the check early-outs), BVT 46/0, full battery
  green (count in the CL description), self-verify green.
- **Memory/time verdict**: selfhost cost is one fuel-capped boolean
  type-walk per def (no allocation, proof-names empty, early-out).
  For proof-bearing programs, edge records are CHECK-phase scratch
  reclaimed at phase-compact; graph is O(P^2) reachability over P =
  proof-relevant defs (single digits in every real program).

### 6.2 Residual edges (honest scope, logged not hedged)

- **Cross-chapter cycles**: the check is per-chapter, like the
  punctual cycle check. Today claim application requires the
  same-chapter DefMap (Induction.md deferred cross-chapter lookup),
  so a cross-chapter proof cycle is not constructible; if DefMap
  goes cross-chapter, this check must follow it.
- **Divergence without a proof-typed name**: a value-level function
  that diverges and returns a nominal wrapper is still emitted code
  (not a proof def), and extracting a proposition from it is caught
  only because the extracting def and the carrier type are
  proof-relevant. Option B closes this class wholesale if it ever
  proves reachable in practice.
- **CDX4022's registry description is stale** (says subgoal checking
  "is not yet implemented (Stage 5)" — Stage 5 shipped). val's lane;
  logged here.

## 7. Claim-Surface Impact

- README proof bullet states the enforced behavior: circular proof
  terms are rejected (CDX4023); `assume` axioms warn (CDX4021). No
  hedging — the code matches the sentence (Damian's ruling: fix the
  code, log bugs, don't tweak the README).
- `TrustedComputingBase.md` §4: proof-layer entry lists acyclicity
  as checked.
- `ClaimsCalibration.md`: entry records found-and-fixed-same-day.
- `Induction.md` is NOT amended (val's as-built record is accurate
  about what it built; totality was never in its scope — §8.4
  explicitly deferred the dependent-type foundations this would ride
  on).

## 8. Cross-References

- `docs/Designs/Language/Active/Induction.md` — the proof layer as built
- `docs/Designs/Compiler/Active/ClaimsCalibration.md` — the register
- `docs/Designs/Compiler/Active/TrustedComputingBase.md` — §4 ranking
- `codex/compiler/Types/TypeChecker.codex` — register-all-defs, elab-claim-apps, Induction Checking
- `codex/compiler/Emit/X86_64.codex:804-826` — is-proof-def / erasure
