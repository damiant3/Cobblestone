# Induction -- General Structural Induction Over Variants

**Status:** Design. No code yet. Supersedes the `induction`-keyword
portions of `ProofSystemSurvey.md` (2026-05-23) with a concrete,
staged implementation plan.

**Author:** val, 2026-06-29.

> **Implementation note (2026-06-29, CL pending).** Stage 1 below was
> planned as "un-degenerate cong". On building it, empirical probing
> overturned the survey's central claim: propositional equality was
> **vacuous**, not sound. Two distinct bugs were found:
>
> 1. **PropEqTy unification dropped the second component's binding.**
>    The `PropEqTy` arm of the unifier recursed with `unify-resolved`
>    (which does not resolve through the substitution) instead of
>    `unify-at` (which does), so `Integer === Text` was accepted via
>    `Refl`. Every other structural arm uses `unify-at`; this one was
>    the anomaly. **Fixed** (`Unifier.codex`), gated green.
> 2. **The claim/proof/qed sugar does not thread the claim type to the
>    proof body.** Bisected to the `proof`+`qed` combination: with both
>    present the proof is checked unconstrained. The plain
>    annotated-def form (`name : prop` / `name = term`) checks
>    correctly. **FIXED** (2026-06-30, CL 6425 parser workaround, then
>    CL 6430 the real root cause). The root cause was NOT the parser:
>    it was an x86 **codegen** bug -- `emit-if-to-local` unsoundly elided
>    an if's terminating jmp when the then-branch was a nested-if whose
>    else ended in a tail-call jmp, so inside the tail-recursive
>    `parse-top-level` if-chain the whole `claim` arm was silently
>    skipped (claim type never attached). Fixed by gating the elision on
>    `tail-is-join`. Regression tests: `proof-qed-vacuous.codex`
>    (un-skipped, rejects CDX2001) and `tco-nested-if.codex` (in BVT).
>
> `cong` was also un-degenerated (§4D) since it was needed for the
> congruence tests. So the doc's §2 below is corrected: before this CL,
> `Refl`/`sym`/`trans`/`cong` did **not** verify anything.

---

## 1. The Goal

The founding document (`docs/PM/Stories/Vision/NewRepository.txt`)
makes structural induction its flagship proof:

```
claim reverse-reverse : for all (xs : List a), reverse (reverse xs) === xs
proof reverse-reverse =
  induction on xs
    is base   -> Refl
    is step (h) (t) (ih) -> trans (cong reverse-append) ih
  qed
```

The user's chosen scope is **general variant induction**: `induction
on x` over *any* user-defined variant type, generating one subgoal per
constructor, binding an inductive hypothesis at every recursive field,
and checking each branch's proof against the goal with the constructor
substituted in. Sound, not a skeleton.

---

## 2. Current State (verified against the seed, 2026-06-29)

What works *after this CL* (was vacuous before -- see the note above):

- `Refl : forall a. a === a` (`TypeEnv.codex:283`). With the unifier
  fix, `refl-bad : Integer === Text; refl-bad = Refl` now fails CDX2001
  (verified: `codex/test/errors/refl-mismatch.codex`). Before the fix
  the PropEqTy arm accepted it.
- `sym`, `trans` properly typed (`TypeEnv.codex:285-286`); now checked
  by instantiation + unification once the PropEqTy arm resolves the
  second component through the substitution.
- `cong` now `forall a b f. (a===b) -> (f a === f b)`
  (`TypeEnv.codex:287`), replacing the degenerate
  `(a->a) -> (Proof -> Proof)`. Verified sound and rejecting:
  `codex/test/proof-smoke.codex` (cong-list) and
  `codex/test/errors/cong-mismatch.codex`.
- `assume : ProofTy` (`TypeEnv.codex:284`) and `ProofTy` unifies with
  any `PropEqTy` both directions (`Unifier.codex:304-312`) -- the
  intended axiom escape hatch (unchanged).
- Proofs erase to no code (CDX4020), checked-or-not.
- **Both forms now check.** As of CL 6430 the `claim/proof/qed` sugar
  threads the claim type to the proof body, so it rejects bad proofs
  identically to the plain annotated-def form (verified: a `claim cc :
  Integer === Integer / proof cc = Refl / qed` erases, while `Integer
  === Text` rejects CDX2001). The earlier vacuity was a codegen bug
  (bug #2 note), not a missing proof feature.

What is absent:

- **`induction` is a reserved keyword (CLAUDE.md:687) with zero
  implementation** -- no lexer-beyond-reserved, no parser rule, no
  TypeEnv binding.
- **`cong` is degenerate**: `forall a. (a -> a) -> (Proof -> Proof)`
  returning bare `ProofTy` (`TypeEnv.codex:287`) -- proves nothing
  structurally.
- **Propositions are type-level only.** `PropEqTy (CodexType)
  (CodexType)` (`CodexType.codex:28`); `===` parses both sides via
  `parse-type` (`Parser.codex:99`). The only substitution that exists
  is `subst-type-var` -- *type-variable* substitution
  (`TypeCheckerInference.codex:123`).
- **No definitional-equality normalizer.** Nothing reduces `reverse
  (reverse xs)` to `xs`. `AppType (TypeExpr) (List TypeExpr)` exists in
  the grammar (`SyntaxNodes.codex:76`) so the term *parses*, but it
  resolves to opaque/unknown type constructors with no reduction rule.

## 3. The Core Obstacle

General induction that *checks* the flagship example presupposes a
dependent-type foundation that does not exist:

1. Propositions must carry **value-level terms** (`reverse (reverse
   xs)`), not just types.
2. A **definitional-equality normalizer** must reduce those terms
   (β for application, δ for unfolding a function's definition,
   ι for `when`/constructor reduction) under a **fuel cap** (bare
   metal, no GC -- Rule 8 and Virtue 12).

Induction is the *top* of the stack; the normalizer and value-level
proposition terms are the *bottom*. Implementing the keyword first
yields subgoals it cannot soundly discharge. The foundation comes
first.

This matches the type-system roadmap (`03-TYPE-SYSTEM.md`, Phase 4:
"proof obligations depend on dependent types … type-level computation
via normalizer") -- the normalizer is the long-deferred piece.

---

## 4. Architecture

Five layers, bottom-up. Each is independently testable.

### A. Proposition terms (`Term` IR for the proof layer)

Introduce a small term language that propositions range over, distinct
from `Expr` (surface) and `IRExpr` (codegen). A `Term` is what a
proposition's two sides reduce within:

```
Term =
 | TVar (Integer)                 -- bound variable (de Bruijn or id)
 | TCtor (Text) (List Term)       -- saturated constructor application
 | TApp (Text) (List Term)        -- saturated call to a named function
 | TLit (LiteralKind) (Text)      -- literal
```

`PropEqTy` is widened (or a sibling `TermEqTy (Term) (Term)` added) so
equality can hold terms. Surface `===` over value expressions lowers
to `TermEqTy`; the existing type-level `PropEqTy` stays for
type-equality proofs (`Nil === Cons`). Keep both -- do not break the
working reflexivity path.

Open question: reuse `PropEqTy` by letting `CodexType` embed a `Term`
(a `TermTy (Term)` leaf), or add a parallel `TermEqTy`. Leaning toward
`TermTy` leaf so the unifier's existing `PropEqTy` arm extends
naturally -- one structural-equality path, not two.

### B. Definitional-equality normalizer

```
normalize : NormEnv, Term, Fuel -> Term
defeq    : NormEnv, Term, Term, Fuel -> Boolean
```

- **δ (delta):** unfold a `TApp f args` by looking up `f`'s definition
  (already available post-desugar) and substituting args. Requires the
  proof checker to see function bodies as `Term`s -- a lowering from
  `Expr` to `Term` for the (small) set of functions referenced in
  proofs. Do not lower the whole program; lower on demand, memoized.
- **ι (iota):** reduce `when (TCtor c args) is c (xs) -> body` to the
  matching branch.
- **β:** substitute actuals for formals.
- **Fuel:** a hard step counter (reuse `Foreword chapter Fuel`).
  Exhausting fuel means "cannot decide definitional equality" →
  proof obligation reported as *unproven* (a diagnostic, never a
  silent pass). This is the safety valve: non-termination becomes a
  clean CDX error, not a hang.

`defeq` normalizes both sides and compares structurally (αN-equality).

### C. Induction principle generation

Given `induction on x` where `x : T` and `T` is a variant with
constructors `C_1 … C_k`:

- For each `C_i (f_1 : A_1) … (f_m : A_m)`, generate a **subgoal**:
  the original proposition with `x` replaced by `C_i f_1 … f_m`.
- For each field `f_j` whose type is `T` (recursive position), bind an
  **inductive hypothesis** `ih_j` : the proposition with `x` replaced
  by `f_j`.
- The branch must be matched syntactically: `is C_i (f_1) … (f_m)
  (ih_1) … -> proof-term`. Exhaustiveness over constructors is checked
  exactly like `when` (reuse the existing exhaustiveness machinery).

### D. Subgoal checking

Each branch's proof term is checked against its subgoal proposition
using the unifier extended with `defeq` (layer B): a proof of `lhs ===
rhs` succeeds when `defeq lhs rhs` after the proof term's rewrites
(`trans`/`sym`/`cong`/`ih`) are applied. The IH bindings are added to
the proof-local environment as ordinary `PropEqTy`/`TermEqTy` facts.

`cong` must be **un-degenerated** here: `cong : forall a b f. (a === b)
-> (f a === f b)`. The current `(a->a)->(Proof->Proof)` form cannot
support `cong reverse-append`. This is a prerequisite, tracked as its
own CL.

### E. Erasure

No new work -- induction proofs return `Proof`/`PropEqTy`, already
erased at emit (CDX4020). Confirm the new `Term`-carrying props are on
the erasure path.

---

## 5. Surface Syntax

```
proof name =
  induction on <var>
    is <Ctor> (<field>)… (<ih>)… -> <proof-term>
    …
  qed
```

- `induction` consumes the reserved keyword, then `on`, then an
  identifier naming a bound variable of the `claim`'s `for all`.
- Requires the claim to be a quantified proposition `for all (x : T),
  P`. This pulls in minimal `for all` proof syntax (today `forall` is
  reserved but unused at the proof level) -- scope it to exactly what
  induction needs, not general first-order quantification.
- Branch heads reuse the `when`/`is` lexical machinery.

---

## 6. Staged Implementation Plan

Each stage is one CL, passes both gates (pingpong + battery), and is
independently useful. Order is bottom-up so nothing ships that can't be
checked.

| Stage | Deliverable | Risk |
|-------|-------------|------|
| 0 | This design + negative/positive test scaffolding in `codex/test/` (`.failing` for unsound proofs) | none | **DONE** |
| 1 | **Soundness fix**: PropEqTy unifier arm uses `unify-at` (was `unify-resolved`, accepting any equality); un-degenerate `cong` to `(a===b)->(f a === f b)`; proof-smoke + cong-mismatch + refl-mismatch tests | low -- `Unifier.codex` + `TypeEnv.codex`, one-pass fixed point | **DONE** |
| 1b | **Thread claim type to proof body** (bug #2): the `claim/proof/qed` sugar must attach the claim's declared type to the `proof` def. Root cause was an x86 codegen jmp-end elision bug (CL 6430), not the parser. `proof-qed-vacuous.codex` un-skipped (rejects CDX2001); `tco-nested-if.codex` added to BVT | done (CL 6425/6430) | **DONE** |
| 2 | Value-level `===` with syntactic equality. **DONE** (CL pending) -- but NOT via a new `TermTy` leaf. Probing showed the existing `TypeCon`/`TypeApply`/`TypeVar` machinery (set up by the cong un-degeneration, Stage 1) already serves as the term language: `reverse (reverse xs) === reverse (reverse xs)`, `Cons h t === Cons h t`, `xs === xs` all parse (via `parse-type`) and check soundly through the `PropEqTy` unifier arm; mismatches reject CDX2001 (`value-eq.codex`, `errors/term-mismatch.codex`). No CodexType change needed, so none of the medium-risk blast radius. **Deferred to Stage 3:** integer/text *literals* and arithmetic operators in `===` (parse-type rejects `1 + 1`); low value without the normalizer, and the flagship `reverse-reverse` is all names/application, so literals are not on its path | none (reuse) | **DONE** |
| 3 | Normalizer (δ/ι/β) with Fuel cap; `defeq`; integrate at the proof-check path | **high** -- theory core; soundness-critical; heap/time scrutiny | **DONE** (CL 6447) |
| 4a | `for all (x:T), P` claim syntax + `induction on x` proof body PARSE; accepted UNVERIFIED (CDX4022, erased) | **DONE** (CL 6455) |
| 4b/5 | Real induction node + principle generation (subgoals + IH) + subgoal checking via `defeq`; **add-zero (Nat) green** | **DONE** (CL 6460) |
| 5a | **N-ary constructor congruence** (unifier curried peel); **append-nil (binary ctor) green** | **DONE** (CL 6462) |
| 5b | **Flagship `reverse-reverse`** -- nested `for all`, `app-cong`, applicable lemmas, capture-avoiding normalizer | **DONE** (CL 6467) |
| 5c | Parametric-type induction (`Lst a`) -- resolve applied `ConstructedTy` to its `SumTy` | **DONE** (CL 6473) |
| 6 | Strengthen `assume` story: `assume` emits CDX4021 (warning) at each use so axioms are visible in the trust trail | low | **DONE** (CL 6437/6438) |

Stages 1, 1b, 2, 3, 6, 4a, and 4b/5 (add-zero) are DONE and shipped.
Stage 4b/5 (CL 6460) landed the real `AForallType`/`AInductionExpr`
nodes, per-constructor subgoal generation with rigid field variables and
inductive-hypothesis binding, and sound subgoal checking via the Stage 3
normalizer + the ordinary proof unifier. `add-zero : for all (n : Nat),
add n Zero === n` is now PROVEN (Zero -> Refl; Succ -> cong ih), erased
CDX4020; `codex/test/errors/induction-unsound.codex` (the Succ case
offered as bare `Refl`) rejects CDX2001 -- the soundness tripwire. Both
in the BVT.

**Stage 4b/5 implementation notes (CL 6460), for the reverse-reverse author:**
- Nodes threaded through every exhaustive CST/AST walker. The checker
  (`TypeChecker.codex` Section: Induction Checking) is intercepted in
  `check-def` when the def is a `for all`-claimed `induction` proof;
  `tdm` was threaded into `check-def`/`check-all-defs` (the `defs` param
  was already present as the normalizer DefMap).
- Field variables are kept RIGID by resolving the proposition through
  `resolve-type-expr` (which maps value-names to opaque `ConstructedTy`)
  and NEVER through `parameterize-type` -- that inversion was the crux.
- **Normalizer backoff (essential):** `normalize-app` now delta-unfolds a
  def ONLY when the fully-normalized result is `is-aterm-normal`; else it
  keeps the application (`rebuild-app`) with normalized arguments. Without
  it, `add k Zero` (with `k` opaque) unfolds to a stuck `when k is ...`
  match, which is not a closed constructor tree, so the whole subgoal
  `Succ (add k Zero)` falls back UNREDUCED and `cong ih` cannot unify.
  The backoff leaves `add k Zero` as a clean stuck application so
  `Succ (add k Zero)` is a normal constructor tree. Stage 3's closed-term
  tests are unaffected (they reduce fully to a constructor).
- `cong ih` discharges the step case because the unifier already unifies
  `TypeApply ?f a` (cong's `f a`) against a 1-argument `ConstructedTy Ci [a]`,
  solving `?f := TypeCon Ci` (`Unifier.codex` ~339/388). Binary
  constructors are handled as of Stage 5a (below).

**Stage 5a implementation notes (CL 6462) -- N-ary constructor congruence:**
- The step case of a list induction reduces to
  `Cons h (f t) === Cons h (g t)` and is discharged by `cong ih`, where
  `cong` must solve its function variable to `Cons h` -- a PARTIAL
  application of a 2-argument constructor. The unifier now peels a
  saturated `ConstructedTy name [.. , last]` as a curried type application
  `(ConstructedTy name init) last` and unifies against `TypeApply fb ab`
  accordingly (`unify-ctor-apply-peel`, generalizing the 1-arg arms).
- The 1-argument path is preserved verbatim (head unified as `TypeCon name`
  so `reduce-type-apply` still collapses it) -- add-zero is unchanged.
- `codex/test/induction-list.codex` proves
  `append xs MyNil === xs` over a user list with `MyCons`;
  `codex/test/errors/induction-list-unsound.codex` (MyCons case by bare
  `Refl`) rejects CDX2001, guarding the peel's injectivity. Both in BVT.
  Fixed point held in one pass (no self-compile unification changed);
  full battery 203/0.

**Stage 5b (CL 6467) -- the flagship `reverse (reverse xs) === xs` is
PROVEN.** `codex/test/reverse-reverse.codex` machine-checks it over a
self-contained `MyList` (the builtin `List` rejects `Cons`/`Nil` pattern
matching, so a faithful proof uses a user list -- which also sidesteps the
cross-chapter DefMap). The proof rests on the lemma chain `append-nil` ->
`append-assoc` -> `reverse-append` -> `reverse-reverse`, each an induction
applied at specific arguments inside the next. Four pieces landed:

- **Nested `for all`.** `for all (a), for all (b), P` already parses (the
  type parser recurses); the checker walks the `AForallType` chain,
  inducts on the *named* scrutinee variable, and treats the other binders
  as opaque constants. NameResolver adds all `for all` binders to the
  proof-body scope (so `ys` in `reverse ys` resolves).
- **`app-cong`** builtin: `forall f g x. (f === g) -> (f x === g x)` --
  congruence in *function* position, for rewriting under the first
  argument of a 2-ary function (the last-arg peel only reaches the last
  argument). Registered in TypeEnv + NameResolver + X86_64Builtins.
- **Applicable lemmas.** A proof term may apply a proven `claim` to
  arguments (`reverse-append (reverse t) (MyCons h MyNil)`). Before
  inference, each maximal claim-application spine is replaced by a fresh
  local bound to the claim's proposition instantiated at the argument
  terms and normalized (`elab-claim-apps` / `instantiate-claim` /
  `aexpr-to-cterm` in `TypeChecker.codex`). The ordinary proof unifier
  then checks the surrounding `trans`/`sym`/`cong`/`app-cong`.
- **Capture-avoiding normalizer (soundness fix).** Delta-unfolding a
  function substitutes actuals (which may carry free variables) into its
  body, whose own binders can then capture them at the following iota
  step -- e.g. `append`'s `is MyCons (h) (t) ->` binder capturing the `t`
  in a substituted `reverse (reverse t)`, silently rewriting it to
  `MyNil`. `normalize-app` now alpha-renames the def body's binders that
  collide with the actuals' free variables (to fuel-suffixed fresh names)
  *before* substituting. add-zero/append-nil escaped this only because
  their variable names happened to align; the fix is general.

Free variables in a proposition already behave as rigid opaque constants
(= universal quantification). All four proofs erase (CDX4020);
`errors/reverse-reverse-unsound.codex` (the `MyCons` step by `cong ih`
alone, without the lemma) rejects CDX2001. One-pass hard fixed point;
full battery green. The founding flagship is machine-checked.

**Stage 4a integration (CL 6455), for the Stage 5 author:** parser-only,
NO new AST/CST nodes. `parse-forall-type` (Parser.codex) consumes
`for all (x:T), P` and returns a synthetic `Proof` NamedType (structure
discarded -- Stage 5 must retain (x, T, P)). `parse-induction-expr`
(ParserExpressions.codex) reuses `parse-match-expr` to consume the
branches, then discards the match and returns an `assume`-equivalent +
CDX4022. `is-induction-keyword` routes it in `parse-atom` (removed from
`is-when-keyword`). Stage 5 replaces both: retain the for-all structure
(add `AForallType`), build a real `AInductionExpr` node WITH its checker.
GOTCHA: `token-text` = `substring(t.source, offset, length)` and
`.source` is the WHOLE file -- synthetic tokens need offset 0, and peeks
must call `token-text`, never raw `.source`.

### 6.1 Stage 3 -- decided architecture (IMPLEMENTED, CL 6447)

> **Implemented (2026-06-30, CL 6447).** Built as described below, with
> one correction: the integration point is **`register-all-defs`**
> (`TypeChecker.codex`), NOT `resolve-declared-type`. By the time
> `resolve-declared-type` runs, `parameterize-type` has already rewritten
> the operands' value-names (`flip`, `reverse`, ...) into fresh
> `TypeVar`s, so δ cannot recover the function name to unfold. At
> `register-all-defs` (line ~597, right after `resolve-type-expr` and
> BEFORE `parameterize-type`), the operands are still `ConstructedTy`
> with source names intact. `normalize-prop-eq defs resolved` runs there.
> The def list (`mod.defs`, the same `defs` param) is the DefMap; no
> `UnificationState` change. `is-term-ctype` guards so only
> `ConstructedTy` operands are touched (primitive/`ListTy` operands like
> `Integer === Integer` are left as-is). A normalized side replaces the
> operand ONLY when it reduces to a closed name/application tree
> (`is-aterm-normal`); otherwise it falls back to the original syntactic
> form, so a stuck reduction is unproven, never a spurious pass. Tests:
> `normalize-eq.codex` (delta+iota) and `errors/normalize-false.codex`
> (the false `flip On === On` rejects CDX2001). Both in BVT. One-pass
> hard fixed point; the compiler source has no value-`===`, so the
> normalizer never runs during self-compile (zero heap/time impact).
> Bool/Int literal folding and cross-chapter (foreword) DefMap lookup
> are deferred to Stage 4/5 as induction needs them.


Probing (2026-06-30) settled the open §4A/§8 questions. No `TermTy` leaf
in `CodexType` and no separate `Term` IR: value terms are already
represented as `TypeCon`/`TypeApply`/`TypeVar`, and syntactic equality
works (Stage 2). The normalizer reduces those, reusing `AExpr` for the
intermediate (which has `AMatchExpr`/`AApplyExpr`/`ALitExpr`/`AIfExpr`;
`CodexType` has no match node, so it cannot hold a mid-reduction term,
but it can hold the closed-constructor normal form).

Pipeline (`normalize-prop-eq : DefMap, CodexType, Fuel -> CodexType`):
1. `codextype-to-aterm : CodexType -> Maybe AExpr` -- `TypeCon n` ->
   `ANameExpr n`; `TypeApply f a` -> `AApplyExpr`; `TypeVar i` ->
   a stuck free name `?tvN`. Returns None for non-term-shaped types
   (e.g. `IntegerTy` from `Integer === Integer`), left untouched
   (type-equality path, unchanged).
2. `normalize-aterm : DefMap, AExpr, Fuel -> AExpr` -- collect the
   application spine; **delta** unfold a saturated call to a def-map
   function (substitute args into its body); **iota** reduce
   `AMatchExpr` whose normalized scrutinee is a constructor application
   (bind the `ACtorPat` vars, substitute, recurse); **beta** is the
   substitution both use; also fold `AIfExpr` on a bool literal and
   `ABinaryExpr` on two literals. Decrement Fuel per delta/iota step.
   **Fuel exhaustion returns the partial form** -> `defeq` fails ->
   proof reported *unproven* (never a silent pass). The soundness valve.
3. `aterm-to-codextype` -- the normal form (closed constructor tree) maps
   back: `ANameExpr` -> `TypeCon`, `AApplyExpr` -> `TypeApply`.

Integration: in `resolve-declared-type` (`TypeChecker.codex:353`), when
the declared type is `PropEqTy (l) (r)`, replace it with
`PropEqTy (normalize l) (normalize r)` before the existing unify runs.
**Soundness reduces to "the normalizer performs only valid reductions"**
-- the proven unifier and the rest of the proof path are unchanged. Guard
with negatives (`not True === True` must still reject).

DefMap: build `name -> ADef` once in `check-all-defs` from `mod.defs`
(already in scope; bodies are `AExpr`) and pass it to `check-def` ->
`resolve-declared-type` (one new param down that path; no
`UnificationState` record-layout change, which a `mutable` record edit
would force).

Memory/time: normal forms allocate in the CHECK-phase deck (reclaimed at
the phase boundary); Fuel bounds steps, hence allocation. Lower only the
proof-referenced functions on demand via the DefMap; do not pre-lower the
program. Measure pingpong heap-hwm before/after on the implementing CL.

---

## 7. Memory & Time-Complexity (Rule 8 / Virtue 12)

- **Normalizer is the only heap/time risk.** It allocates `Term`s per
  reduction step. Mitigations: (a) hard Fuel cap -- bounded steps, so
  bounded allocation; (b) normalization runs in the CHECK phase deck,
  reclaimed at phase boundary; (c) lower only proof-referenced function
  bodies, memoized -- not the whole program.
- **`Term` leaf in `CodexType`** adds one variant; every phase that
  walks types gains one arm. No size change to existing types (variant
  tag widens only if it crosses a byte boundary -- verify against
  field-byte-width).
- Proofs erase, so **zero runtime/codegen cost** -- this is all
  compile-time.
- Every implementing CL states its own memory/time verdict; Stage 3's
  is the one that matters and will be measured (pingpong heap-hwm
  before/after).

---

## 8. Risks & Open Questions

1. **`Term` vs reuse of `Expr`.** Lowering `Expr → Term` duplicates
   structure. Alternative: normalize over `IRExpr` directly (post-LOWER).
   But IR is past desugar/erasure and harder to map back to source
   propositions. Leaning `Term` for a clean, small, proof-only IR.
2. **δ-unfolding and recursion.** Unfolding a recursive function under
   fuel is correct but can be slow. Acceptable: Fuel makes it bounded;
   undecided equalities are reported, not guessed.
3. **`PropEqTy` reuse vs `TermEqTy`.** Decided tentatively in §4A
   (TermTy leaf). Revisit at Stage 2 if the unifier arm gets ugly.
4. **Scope creep into full dependent types.** Resist. Build exactly the
   normalizer induction needs (closed terms over constructors and named
   functions), not a general type-level lambda calculus. Virtue 8.
5. **Collision risk.** None of the other live agents (fester ARM64,
   reek×3 apps/GPU, blu PTX) touch `Types/` or the proof layer. Lane is
   clear.

---

## 9. Cross-References

- `docs/Designs/Language/Active/ProofSystemSurvey.md` -- prior survey
- `docs/Designs/Language/Active/03-TYPE-SYSTEM.md` -- Phase 4 dependent types
- `docs/PM/Stories/Vision/NewRepository.txt` -- the flagship example
- `codex/compiler/Types/Unifier.codex:304-313` -- PropEqTy/ProofTy arms
- `codex/compiler/Types/TypeEnv.codex:283-287` -- proof builtin bindings
- `codex/compiler/Types/CodexType.codex:27-28` -- ProofTy/PropEqTy
- `codex/compiler/Syntax/Parser.codex:357-401, 1011-1015` -- claim/proof parsing
