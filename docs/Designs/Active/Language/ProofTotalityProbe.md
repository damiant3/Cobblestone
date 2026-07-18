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
| 2 | Optional: positive proof-term grammar (Option B) | BUILT + verified 2026-07-17 (blu), CDX4024; design in §9. Seed-carrying, awaiting copy-up |

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

- `docs/Reference/ClaimsCalibration.md` — the register
- `docs/Reference/TrustedComputingBase.md` — §4 ranking
- `codex/compiler/Types/TypeChecker.codex` — register-all-defs, elab-claim-apps, Induction Checking
- `codex/compiler/Emit/X86_64.codex:804-826` — is-proof-def / erasure

## 9. Stage 2 Design — Positive Proof-Term Grammar (blu, 2026-07-17)

Stage 1 (CDX4023) rejects proof reference *cycles*. Stage 2 restricts a
proof term to a positive grammar, so the classes that are not cycles --
an inline `if`/`when`/`act`/lambda/arithmetic expression that happens to
produce a proof-typed value, or a general expression laundering a
proposition -- become unrepresentable rather than merely acyclic.

### 9.1 The design tension (resolved)

A naive "every subterm of a proof body must be a proof form" grammar
**rejects the flagship**. Legitimate proofs mix two kinds of argument:

- proof arguments to the proof builtins -- `trans (app-cong (cong ih))
  (...)`, `sym (append-nil (reverse ys))`; and
- **value** arguments that instantiate a `for all` -- in
  `append-assoc (reverse ys) (reverse t) (MyCons h MyNil)` the three
  arguments are ordinary data terms (a `reverse` call, a constructor
  application), not proofs.

So the grammar cannot constrain application arguments uniformly. The
sound formulation is **type-directed**: a subexpression is constrained
**iff its checked type is a proposition**. Value subterms are ignored;
proof-typed subterms must be an allowed proof form. This admits the
value-argument claim applications and forbids an inline non-proof form
that produces a proof (`trans (if c then Refl else Refl) Refl`).

Why this is sound without descending into every argument: any *named*
def that produces a proof is proof-relevant (its type mentions proof),
so its body is grammar-checked independently by the same pass. The only
gap that leaves is a proof produced **inline** by a non-proof control
form, which is exactly what the type-directed check catches at that node.

### 9.2 The grammar

A proof-typed expression `e` (checked type satisfies
`type-mentions-proof`) is legal iff it is one of:

- `AVarExpr name` -- a proof builtin, an induction-hypothesis binding, a
  cited claim, or a `let`-bound proof. (Names are already checked: cycles
  by Stage 1, type by the checker.)
- `AApplyExpr f a` -- the application spine, provided the spine head
  peels to an `AVarExpr` (a proof builtin or a proof-relevant/claim
  name). Recurse into `f`; recurse into `a` **only if `a`'s checked type
  is a proposition** (proof argument); a value argument is left alone.
- `AInductionExpr scrutinee arms` -- the scrutinee is a value (skip);
  each arm body is a proof-typed position (recurse).
- `ALetExpr binds body` -- recurse into `body`; recurse into each bound
  value that is proof-typed.

Every other `AExpr` shape at a proof-typed position is rejected:
`AIfExpr`, `AMatchExpr` (a `when` that is not the induction form),
`ALambdaExpr`, `AActExpr`, `ATryExpr`, `AHandleExpr`,
`AWithTimeoutExpr`, `AFieldAssignExpr`, `ALazyExpr`, `ABinaryExpr`,
`AUnaryExpr`, `AListExpr`, `ARecordExpr`, `AFieldAccess`, and literal
forms. A proposition has no business being built by any of these.

### 9.3 Where it hooks and what it reuses

- New `check-proof-grammar` in `TypeChecker.codex` (Section: Proof
  Acyclicity, beside `check-proof-cycles`), chained in `check-chapter`
  right after `check-proof-cycles` (line ~1879), taking the same
  `(mod.defs) (result.types)` and threading the bag through
  `UnificationState`.
- Proof-relevant def set: **reuse `build-proof-names`** verbatim -- the
  bodies to constrain are exactly the proof-relevant defs.
- Per-subexpression type: `expr-types` is keyed by **span**
  (`record-expr-type st span ty` in `TypeCheckerInference.codex`); the
  walk looks up each node's span to decide "proposition or value". Reuse
  `type-mentions-proof` for the predicate (it already exists and is
  fuel-capped, erring toward "checking").
- Proof builtins are the six in `Builtins.codex` whose `bs-type` mentions
  `ProofTy`/`PropEqTy`: `Refl`, `assume`, `sym`, `trans`, `cong`,
  `app-cong`. Identify by that type predicate, not a hardcoded name list,
  so a future proof builtin is covered by construction ([[builtin-table-2-14]]).
- New diagnostic **CDX4024 NonGrammaticalProof** in `CdxCodes.codex`,
  sev-error, phase-type-checker: names the offending form and points to
  the proof calculus (builtins, induction, cited lemmas, `assume`).

### 9.4 Test plan

- New `errors/*.failing` probes (the class Stage 1 does NOT catch, i.e.
  no cycle): `proof-inline-if` (`bad : Zero === Zero`, body
  `if True then Refl else Refl` -- was accepted, must now be CDX4024),
  `proof-inline-match` (a `when` producing a proof), `proof-inline-act`
  (an `act` block producing a proof), `proof-arith` (a proof-typed value
  from arithmetic). Each must reach the failure only via Stage 2, not a
  pre-existing code -- verify against the pre-change seed
  ([[hot-path-tuple-allocation]] rule 6: for a `.failing`, compare the
  CODES on control vs SUT, not just "it halted").
- **Regression must-pass** (the whole risk of the feature): the flagship
  `reverse-reverse`, `proof-smoke`, `induction-list`, `induction-*`,
  `add-zero`, and the Stage 1 launder probes stay exactly as they are
  (Stage 1 already rejects them; Stage 2 must not change their codes).
- Memory/time verdict to state in the CL: one span-keyed type lookup +
  a bounded structural walk per proof-relevant def; zero cost on the
  selfhost (no proof-typed defs, `build-proof-names` early-outs).

### 9.5 Scope and risk

Seed-carrying (compiler `Types` change). No Emit or plug surface. The
one real risk is over-rejection of a legitimate proof style; the
type-directed formulation is specifically chosen to avoid it, and the
regression set above is the guard. The design doc's own §5 caveat holds:
Option B "constrains future proof styles" -- any new legitimate proof
form (e.g. a future proof-level `let`-group) must be added to §9.2.

### 9.6 As built (blu, 2026-07-17)

Built exactly as designed above. `check-proof-grammar` +
`check-proof-term` / `check-proof-arms` / `check-proof-binds` /
`pg-is-prop` / `pg-form-name` in `TypeChecker.codex` (Section: Proof
Acyclicity), chained in `check-chapter` after `check-proof-cycles`.
`expr-types` is sorted into a copy once per chapter, guarded by
`build-proof-names` being non-empty, so the selfhost (no proof defs)
pays nothing. CDX4024 `NonGrammaticalProof` in `CdxCodes.codex`.

Verified against a fresh SUT (`build/build.ps1` green: CDX fixed point,
BVT, plug gates; capability/effect/constant tables MATCH):
- `errors/proof-inline-if` and `errors/proof-inline-when` (new probes,
  non-cyclic so Stage 1 passes them) each reject with exactly CDX4024,
  naming the form.
- `reverse-reverse` (the flagship: induction, `cong`/`app-cong`/`trans`,
  and claim applications carrying VALUE arguments like
  `append-assoc (reverse ys) (reverse t) (MyCons h MyNil)`),
  `proof-smoke`, and `induction-list` all still compile to a binary with
  no CDX4024 -- confirming the type-directed walk leaves value arguments
  alone. The Stage 1 launder probes still reject with CDX4023 (their
  bodies are var/apply forms, grammatical; only the cycle is the fault).

Memory/time verdict: one span-keyed type lookup plus a bounded
structural walk per proof-relevant def; one `sort-expr-types` copy per
chapter, only when a proof def exists. Zero cost on the selfhost
(`build-proof-names` early-outs). No codegen change (CHECK-phase
diagnostic only; proofs already erase), so the self-compile is a
one-pass fixed point.

Not yet copied up: seed-carrying, pending a battery run over the full
proof-test class and AgentGrid coordination (seed order vs reek's LIR).
