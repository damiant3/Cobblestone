# Effect Rows — Effect Polymorphism and Subsumption

**Status: SHIPPED — all stages complete, enforcement live.**
Stage 0 (probes, CLs 6508-6511), 1a/1b (representation + syntax,
CLs 6512/6513), 2 (row unification, CL 6532), 3a (ambient-row
calculus per the ruled Γ ⊢ e : τ | ε architecture, CL 6550), 3b
(migration + enforcement, CL 6557), 4 (transitional-form
retirement + CDX2094, CL 6560), each with its own seed and a green
fixed point. The §13 probe catalog is green in both directions: the
five open laundering routes reject, the must-accept set passes, and
the compiler's own source needed zero effect-declaration fixes.
Deferred odds (argument-boundary dotted widening, handler dotted
discharge, CDX2093 escape assertion, tail-defaulting pass) are
listed in `docs/Agents/blu-workplan.md` and land when a real
program needs them. Original design reek; rigor revision, stage 0,
and stages 1-4 as-built: blu, all 2026-07-02.

Companion: `ClaimsCalibration.md`, `TrustedComputingBase.md`. Motivated by
the effect-laundering hole closed partially in CL 6494.

Every code citation below was verified against the tree at CL 6506.

---

## 1. The problem

Codex tracks effects as a flat name set attached to the *return type*:
`EffectfulTy (List Name) (List Text) (CodexType)` — effects, per-effect
scopes, inner type (`CodexType.codex:26`). So `Text -> [Console] Nothing`
is `FunTy Text (EffectfulTy [Console] [] Nothing)` (`CodexType.codex:18`
— `FunTy` is a two-field curried arrow with no effect component of its
own). Enforcement today is four point checks:

0. **Let boundary** — CDX2033 rejects let-binding an effectful value
   outside an act-bind, closing the simplest laundering route (an
   effectful call let-bound in a plain pure body). Stage 0 finding —
   the original draft and the first revision both missed it.
1. **Definition boundary** — `check-effect-subset`
   (`TypeChecker.codex:629`): the body's extracted effect names must each
   be covered by the signature's, where "covered" includes the one-level
   dotted prefix relation (`Console.Write` covered by `Console`,
   `effect-covered-by`, `TypeChecker.codex:654`). The prefix relation was
   dead code until CL 6509: `find-dot` compared char codes against ASCII
   46, which is the letter H in CCE, so real dots never matched. Fixed
   and pinned by the `effect-dotted-allow`/`effect-dotted-deny` probes.
2. **Application argument boundary** (CL 6494) — `effect-le`
   (`TypeCheckerInference.codex:439`): a function argument may not carry
   effects a *concrete* pure parameter type forbids. Deliberately lenient
   when the parameter is a type variable — which is every HOF.
3. **Act-block union** (CL 6494) — `infer-act-loop`
   (`TypeCheckerInference.codex:788-805`) unions each statement's
   extracted effects so a mid-block effect is not dropped.

This is effect *checking*, not an effect *system*. The structural gaps:

1. **`unify-at` strips `EffectfulTy`** (`Unifier.codex:173-181`): it
   unifies the inner value type and discards the effect set on both
   sides, so `[Console] Int` unifies with `Int`. Every fact inference
   establishes about effects is erased at the join points where
   inference actually happens.

2. **No effect variable.** A higher-order function cannot be
   effect-polymorphic. `map` is `ForAllTy 0 (ForAllTy 1 (FunTy (FunTy
   (TypeVar 0) (TypeVar 1)) ...))` (`TypeEnv.codex:145`) — its function
   parameter is a *pure* arrow, so an effectful argument is not
   constrained and its effects do not surface in `map`'s result. This is
   the residual hole `effect-le` is deliberately lenient about
   (`TypeCheckerInference.codex:418-431`). The same shape recurs at
   `par` (`TypeEnv.codex:173`), `fork`/`await` (`:171-172`),
   `race` (`:174`), the foreword's `list-map`/`filter`/`fold` family,
   every user-declared HOF, and every `class` method arrow (an
   effectful `instance` behind a pure class signature launders through
   the dictionary).

3. **Handlers subtract, the unifier erases.** `when E handle` typing
   removes the handled effect from the body's type and from every
   binding in the environment (`AHandleExpr`,
   `TypeCheckerInference.codex:910-916`; `strip-handled-effect`,
   `:1010`; `strip-effect-from-env`, `:995`). This subtraction is only
   as trustworthy as the effect information reaching it — which gap 1
   destroys, and the env-rewriting form of it (`strip-effect-from-env`)
   is a scoping approximation that rows make unnecessary (§7).

The headline claim in `KingsAndCourts.md` ("a compromised library cannot
silently exfiltrate data because the effect would not type-check") is only
as strong as this. Full effect rows make it true by construction, and the
adversarial-probe stream in `docs/PM/BACKLOG.md` makes it *stay* true.

## 2. Goals / non-goals

**Goals.** Effects become first-class inferred data: every arrow carries
an effect row; row variables make HOFs effect-polymorphic; unification
solves row constraints; subsumption (fewer effects usable where more are
expected) is enforced with correct variance at declared boundaries. The
property being established, stated so it can be attacked:

> **Effect safety.** If a definition type-checks with effect row ε, then
> no evaluation of it performs an effect operation whose label (after
> sub-effect widening) is outside ε, except operations discharged by an
> enclosing handler. The row is an *upper bound* on observable effects.

This is the standard soundness statement for row-based effect systems
(Leijen 2014 for Koka; Lucassen & Gifford 1988 for the original effect
discipline). We claim the upper-bound direction only — rows may
over-approximate (dead branches, imprecise tails); they may never
under-approximate. Every stage gate in §12 preserves the self-compile
fixed point.

**Non-goals.** No effect-handler *runtime* redesign — `IrHandle` /
`emit-handle` and the resume trampolines are complete and untouched. No
new runtime cost — effects stay erased at codegen and at the IR wire
boundary (§10). Not a scoped-effects / named-instance system
(Frank-style) and not Koka's scoped-label rows (§5 says why); a single
global row per arrow. No effect-indexed types (`Task e a`) in this
design — the conservative `fork` typing in §9 avoids them; revisit only
if structured concurrency demands precision later.

## 3. Surface syntax (DECIDED — brackets, 2026-07-02)

`-(e)->` (Koka) is rejected — ugly, and alien to a language that reads
like a book. **Decision: reuse the existing `[...]` effect notation and
allow a lowercase identifier inside it as an effect-row variable.** This
mirrors the distinction Codex already draws everywhere else —
`PascalCase` is a concrete thing (`Console`, `FileSystem`), lowercase is
a variable (like type variables `a`, `b`). No new operator.

```
  greet   : Text -> [Console] Nothing          -- concrete effect (unchanged)
  square  : Integer -> Integer                 -- pure = no [...] (unchanged)
  map     : (a -> [e] b), List a -> [e] List b -- e is an effect-row variable
  compose : (b -> [e] c), (a -> [e] b), a -> [e] c
```

The `[e]` on the argument arrow and the result arrow are the *same* row
variable, so `map` propagates exactly the effects of its function
argument. A pure function passed to `map` instantiates `e := {}` and the
result is pure; an effectful one instantiates `e` to its effects and they
surface.

**Open rows** (concrete effects plus a tail variable) list the concretes
then the variable, comma-separated:

```
  logged-map : (a -> [Audit, e] b), List a -> [Audit, e] List b
```

`[Audit, e]` = "at least Audit, plus whatever `e` is." `|` is not used
inside `[...]` (it reads as boolean-or elsewhere; comma-with-a-lowercase-
tail is unambiguous and quieter).

**Grammar.** Extending the existing effect production
(`parse-effect-names`, `Parser.codex:256`):

```
  effect-row    := '[' row-items? ']'
  row-items     := row-item (',' row-item)*
  row-item      := effect-label | row-tail
  effect-label  := TypeIdent ('.' Ident)? TextLiteral?     -- Console, Console.Write, Console "scope"
  row-tail      := Ident                                    -- lowercase; at most one per row
```

Well-formedness (parse errors, not warnings):

- At most one `row-tail` per row (CDX1120). Position is free in source;
  the canonical form (§4) and the printer place it last.
- A `row-tail` takes no scope literal and no dot (`[e "s"]`, `[e.Write]`
  are CDX1121).
- Duplicate labels are accepted and collapsed to the canonical set —
  rows are idempotent (§5). The printer never emits duplicates, so the
  text fixed point is safe.
- `[]` (empty brackets) is the explicit empty row, identical in meaning
  to writing no brackets: both denote the closed pure row. The printer
  always omits empty brackets — one rendering per type, or pingpong
  breaks.

**Reclassification hazard.** The current parser already accepts a
lowercase identifier inside effect brackets — `is-ident` alongside
`is-type-ident` (`Parser.codex:261`) — and today it becomes a *concrete
effect name*. Stage 1 changes the meaning of that text. Before stage 1
lands, sweep the depot for lowercase identifiers in effect position
(`codex/`, `apps/`, `codex/test/`, foreword); each hit is either renamed
to PascalCase or is a latent bug. `effect` declarations use PascalCase by
convention throughout the tree, so the expected hit count is zero — but
the sweep is a gate, not an assumption.

Rationale: the bracket already means "this is an effect." Keeping the row
variable inside the same bracket says *this is an effect* whether the row
is concrete or a variable — one visual vocabulary for effects, no second
notation to learn. Decided 2026-07-02; drives the lexer/parser and every
effect-type rendering, and everything below assumes it.

## 4. Type representation

Changes to `CodexType` (`CodexType.codex`). **As-built note (stage 1a,
CL 6512):** rows are *not* CodexTypes. The shipped representation is

  ```
  EffectRow = record {
   labels  : List EffectLabel,    -- canonical: name-sorted, duplicate-free
   tail-id : Integer              -- -1 = closed; >= 0 = row variable id
  }
  EffectLabel = record {
   name  : Name,                  -- Console, Console.Write (dotted stored whole)
   scope : Text                   -- "" = unscoped; parallel to today's List Text
  }
  ```

with `FunTy (CodexType) (EffectRow) (CodexType)` and a single interned
`empty-row`. The draft's `EffectVar (Integer)` CodexType constructor is
*not needed*: a row variable can only ever appear as a row's tail, so
representing it as an integer id inside the row (solved against a
separate row-substitution table in stage 2) makes a type/row kind
confusion unrepresentable rather than merely checked — the unifier
cannot bind a `TypeVar` to a row because no CodexType *is* a row.
`ForAllEff (Integer) (CodexType)` is still planned, deferred to stage 2
where signature row variables first need binding and freshening
(`instantiate-type`). Rationale for a separate binder constructor
stands: reusing `ForAllTy` with a kind tag would thread the tag through
every existing consumer; a distinct constructor makes forgetting it a
compile error in the compiler itself.

**As-built (stage 2).** The `tail-id` field (removed as write-only in
stage 1b) is restored now that stage 2 reads it:
`tail-id : Integer between -1 and 4294967295`, -1 meaning no id (a
closed row, or a source tail awaiting parameterization). `tail-name`
is kept alongside for diagnostics and printing — ids never print.
`ForAllEff` is **appended as the last CodexType constructor**, not
inserted beside `ForAllTy`: constructor ordinals are load-bearing in
emission (runtime sum tags), so the ctor list is append-only. Row
bindings live in `row-substitutions : List EffectRow` on
`UnificationState` — a dense table indexed by row id (slot i holds
row id i's binding; an unbound slot holds the self-row `row-var i`,
mirroring the TypeVar self-slot trick; the table grows on first
binding past its length, so fresh ids alone allocate nothing). Row
ids come from a dedicated `next-row-id` counter with the same 32-bit
bound as type-variable ids — the bound is the ruling; a shared
counter bought nothing once the tables were separate, and a dense
table needs its own id space. The stage-2 CL shipped a flat
association list here; it was converted before stage 3 because
stage 3 mints a row variable per application site, which would have
made association-list resolution quadratic in program size. The GADT-arm snapshot
(`SubstSnapshot`) covers both tables; restoring one without the other
would leak speculative bindings from an abandoned branch. Signature
row names parameterize through the same `ParamEntry` list as type
names with an `is-row` kind flag — name AND kind match, so one
lowercase letter can serve as both a type variable and a row tail in
a single signature without collision. Two lint discoveries: a
negative literal in field position is a negate application at AST
level, not `ALitExpr`, so `tail-id = -1` trips the CDX2051 narrowing
lint unless written `__narrow (-1)`; and bounded integers do not
parse in function-signature positions (params, returns, tuple
payloads) — they are record-field and ctor-payload syntax only, so
the id plumbing uses plain `Integer` with `__narrow` at the
construction boundary.

**Scopes are not dropped.** Today's `EffectfulTy` carries a per-effect
scope list (`List Text`, populated by `parse-effect-with-scope`,
`Parser.codex:268`, used by `with-timeout` and the capability machinery).
The flat pair-of-parallel-lists becomes a list of labels each carrying
its scope. Row operations move labels *with* their scopes; two labels are
identical only if name and scope both match. Whether a scoped label is a
sub-effect of its unscoped name (`Console "auth"` ⊑ `Console`) follows
the same lattice rule as dotted names (§5) — decide once, in
`row-covered-by`, with a test.

**Canonical form is an invariant, not a convention.** A single smart
constructor `make-row` sorts labels by (name, scope), removes duplicates,
and normalizes `tail`. Every row in the checker is built through it.
This gives: deterministic printing (text fixed point), O(n) merge in
unification (both sides sorted), and structural equality = set equality.

**Curried arrows.** Codex multi-parameter functions are curried `FunTy`
chains. The row lives on *every* arrow; for `A, B -> [E] C` the desugarer
produces `FunTy A {} (FunTy B {E} C)` — inner arrows empty, the row on
the arrow whose application performs the effects. Partial application is
therefore pure by construction, matching today's semantics where the
`EffectfulTy` wraps the final return. The desugarer owns this placement;
the checker never guesses.

**`EffectfulTy` is retained for effectful values (DECIDED here,
was §9-open).** A parameterless effectful binding — `read-line :
[Console] (Maybe Text)` (`TypeEnv.codex:146`), `current-dir : [Process]
Text` (`:167`) — is an effectful *value*, not an arrow. Modeling it as a
nullary arrow would ripple a call-shim through lowering and codegen for
zero user benefit. Keep `EffectfulTy` for the value case only; the arrow
case migrates to the `FunTy` row. Post-stage-4, an `EffectfulTy` whose
inner type is `FunTy` is ill-formed — add a checker assertion (CDX9xxx
internal error) so the transitional form cannot silently survive.

The row on `FunTy` is the empty closed row for a pure arrow, so the
overwhelming majority of signatures (pure functions) are unchanged in
meaning and print with no `[...]`.

## 5. Row theory and unification

**The equational theory is ACI1 label sets, not Koka scoped labels.**
Rows are finite sets of labels plus an optional tail: associative,
commutative, idempotent, with `{}` as unit. Koka's rows are *multisets*
(scoped labels — duplicates are meaningful, because a nested handler for
the same effect discharges one occurrence). Codex's handler discharge is
already set-semantics: `filter-effect` removes **every** occurrence of
the handled name (`TypeCheckerInference.codex:1021-1026`), so an inner
`when Console handle` shadows an outer one completely. Sets match the
implemented and documented semantics; multisets would silently change
nested-handler behavior. If scoped/nested handler semantics is ever
wanted, that is a new design, not a parameter of this one.

**Why the record-row principality pathologies do not apply.** The known
failure of principal unification for set-based rows arises from label
*payloads* (record fields with types: unifying `{x : Int | ρ1}` with
`{x : Text | ρ2}` forces a field-type conflict through the tails).
Effect labels carry no payload — a label is present or absent. Under ACI1
with payload-free labels and at most one tail per row, the algorithm
below produces a unifier that is most general up to ACI1 equivalence:
the only candidate divergence (re-adding a shared label into both tails)
is collapsed by idempotence. State this as a lemma in the implementation
prose and keep the proof sketch next to `unify-row`; it is the answer to
the obvious reviewer objection.

**Unification uses exact label equality. The sub-effect lattice does
not participate.** `Console.Write` and `Console` are distinct labels to
`unify-row`. The lattice (`effect-covered-by` prefix rule, plus the
scoped-label rule from §4) applies only in the *directed* subsumption
check `row-le` (§6), where a direction exists to make widening coherent.
Putting the lattice into symmetric unification poses an unanswerable
question — which label survives cancellation? — and forfeits principal
solutions. This resolves the "dotted sub-effects" open question of the
original draft.

**Algorithm** (`unify-row r1 r2`, both in canonical form):

1. One sorted merge partitions labels: `shared`, `only1`, `only2`.
   Shared labels cancel.
2. Case on tails:
   - closed / closed: `only1 = only2 = ∅` or fail (CDX2090).
   - open t1 / closed: the closed side cannot absorb, so `only1 = ∅` or
     fail; bind `t1 := make-row only2 None` — the tail absorbs exactly
     the closed side's leftovers and closes.
   - open t1 / open t2, t1 ≠ t2: fresh `t3`; bind `t1 := make-row only2
     (Some t3)`, `t2 := make-row only1 (Some t3)` — each tail absorbs
     the other side's leftovers, sharing one fresh remainder.
   - same tail both sides: `only1 = only2 = ∅` or fail (a tail cannot
     absorb labels on one side of itself).
3. **Occurs check.** Rows do not nest — a row contains labels and at
   most one variable — so the occurs check is a walk of the tail
   substitution chain (bind t1 to a row whose resolved tail chain
   reaches t1 = infinite row, CDX2091). O(chain length), no recursion
   into types, unlike `occurs-in` (`Unifier.codex:151`).
4. **Substitution.** `EffectVar` bindings go through the same
   `add-subst` table as `TypeVar` (ids are from the shared counter so
   they cannot collide), but `resolve-silent` must dispatch on
   constructor so a type position never resolves to a row and vice
   versa. A row-in-type-position or type-in-row-position resolution is
   an internal error, asserted, not ignored.

Arrow vs arrow in `unify-at` becomes: unify params, unify rows
(`unify-row`), unify results. Symmetric, no polarity — see §6 for where
direction lives. `unify-at`'s `EffectfulTy`-stripping arms
(`Unifier.codex:173-181`) are deleted in stage 3; `EffectfulTy` (values
only, §4) unifies by unifying rows and inner types.

**Complexity.** Canonical rows make `unify-row` O(n) in label count per
call (sorted merge), and rows are small (the compiler's own maximum is 2
labels today; assert-and-log if a row exceeds 16). No quadratic behavior;
the term-depth fuel (`max-recursion-depth`) already bounds pathological
nesting of arrows.

**As-built (stage 2).** Implemented exactly as specified, in
`Unifier.codex` (`Section: Effect Row Substitution` and `Section: Row
Unification`): `make-row` canonical constructor, `labels-minus`
single sorted merge, the four tail cases, tail-chain occurs check
(CDX2091), `resolve-row` chasing and merging the substitution chain.
The principality lemma prose sits with the code. Until stage 3
migrates concrete effects onto arrows, live rows carry no labels, so
the closed/closed and same-tail failure paths (CDX2090) are
unreachable — the label machinery is exercised in anger by stage 3's
probe catalog. The 16-label assert-and-log is deferred to stage 3
with label liveness.

## 6. Subsumption without a subtyped unifier

The original draft proposed threading an expected/actual polarity through
`unify-at`. That is the known-dangerous road: unification-with-subtyping
loses principal types and turns every unifier bug into a soundness bug
(Dolan & Mycroft's algebraic subtyping solves it properly and is far out
of scope). **Rejected.** The revised strategy keeps the unifier
symmetric and puts direction in exactly two places, both of which
already exist in embryo:

**(a) Implicit open instantiation at use sites (the Koka "open" rule).**
When a named function's type is instantiated at a *use* site
(`instantiate-type`, `TypeCheckerInference.codex:117`), each arrow whose
row is closed is opened with a fresh tail: `Integer -> Integer` is used
at `Integer -[t]-> Integer`, `t` fresh. This is sound — a function
performing `{}` may be ascribed any superset — and it makes pure and
less-effectful functions flow into effectful contexts through *plain
unification*, no subtyping: passing `square` where `a -> [Console] b` is
expected solves `t := {Console}`. Two hard rules:

  - Opening applies at instantiation of a *referenced* name and to
    inferred lambda literals (fresh tail from the start). It never
    applies to a type that is the declared type of a *binding position*
    — a definition's own signature, a record field, a parameter
    annotation. Those stay as written.
  - **Mutable-record fields are invariant.** A field of a `mutable`
    record holding an arrow type admits no opening and no widening in
    either direction (the Java-array variance hole, verbatim: write a
    widened function in, read it back at the narrower type). Immutable
    record/ctor fields are covariant in their arrow rows like any other
    value position, but the *stored* type is the declared one.
  - Unsolved tails are defaulted to `{}` (closed) at the definition
    boundary, after `check-effect-subset`. An effect variable that
    survives to codegen is an internal error.

**(b) Directed `row-le` at the declared boundaries.** The lattice-aware
subset check — actual row ⊑ expected row, where ⊑ consults
`effect-covered-by` (dotted prefix) and the scope rule — runs at:

  1. the definition boundary (`check-effect-subset` generalizes to
     `row-le` on the full arrow type, recursing with the flip:
     contravariant in parameters, covariant in results — the same
     recursion `effect-le` already sketches at
     `TypeCheckerInference.codex:449-451`);
  2. the application argument boundary (`effect-le` becomes row-aware;
     its leniency at type-variable parameters is *removed* — a
     row-polymorphic parameter now constrains via unification, so the
     leniency has nothing left to excuse);
  3. act statements (the act union now falls out of arrow rows, but the
     boundary against the enclosing signature remains the diagnostic
     site).

`row-le` is ~40 lines, directed by construction, and auditable in
isolation — the variance subtlety the draft correctly feared lives
there and only there, not smeared through the unifier. Getting variance
wrong in `row-le` rejects valid code or accepts laundering; that is what
the probe catalog (§13) exists to pin from both sides.

## 7. Handler typing under rows

New — absent from the original draft, and it is the one place effect
*elimination* meets row inference.

Current rule (`AHandleExpr`, `TypeCheckerInference.codex:910-916`): strip
the handled name from the body's inferred type and from every env binding
(`strip-effect-from-env` — an approximation that rewrites the whole
environment because flat effects cannot express "this call's effects are
discharged here").

Row rule, replacing both:

```
  body checked at row unify(row-of body, {E, t'})   -- t' fresh
  result row = t'  (plus the handler clauses' own row, unioned)
```

Unifying the body row against the extension `{E | t'}` forces `E` into
the concrete part if the body's row is open, so the subtraction is exact
— not the "remove concrete occurrences and hope the tail is honest"
approximation. The result row is `t'`: everything the body does *except*
`E`, plus whatever the handler clauses themselves do (clause bodies are
checked normally and their rows unioned in — a `Console` handler whose
clause writes files yields `[FileSystem, t']`). If the body's row is
closed and lacks `E` entirely, warn (CDX2092: handling an effect the
body cannot perform — legal, suspicious). `strip-effect-from-env` and
`strip-handled-effect` (`:995`, `:1010`) are retired in stage 4;
discharge-all-occurrences semantics is preserved by set idempotence
(`E` appears in a row at most once).

Note the soundness asymmetry: removing only concrete occurrences while a
tail hides more `E` would still be *sound* (over-approximation — the
runtime handler catches every dynamic perform in its extent regardless),
but the extension-unification rule is both sound and precise, and it is
one unify call. Take precise.

## 8. Generalization / instantiation

Codex is signature-driven: `ForAllTy` binders come from lowercase names
in declared signatures (`parameterize-walk`, `TypeChecker.codex`), and
let-bindings are monomorphic. Row variables follow identically:
lowercase-in-brackets in a signature produces a `ForAllEff` binder at
that def; `instantiate-type` (`TypeCheckerInference.codex:117`) freshens
`ForAllEff` exactly as it freshens `ForAllTy` (then applies §6(a)
opening). There is no HM generalization pass and therefore no
value-restriction question: an effect variable is born quantified (from
a signature) or born free-and-locally-solved (fresh tail), never
generalized from an inference residue. Monomorphic recursion holds — a
self-call sees the declared signature.

`check-effect-subset` becomes redundant once rows flow through the body
— and is kept anyway, as `row-le` at the def boundary: it is free, it is
the natural site for the primary diagnostic ("Effect 'X' not declared in
function signature", `cdx-effect-undeclared`), and it is the backstop
if a unifier bug ever lets a row through. Belt and suspenders, in that
order.

## 9. Builtins and foreword

Re-type in `TypeEnv.codex` (all citations current):

| Binding | Today | Becomes |
|---|---|---|
| `map` (`:145`), `par` (`:173`) | pure arrows | `(a -[e]-> b), List a -[e]-> List b` |
| `race` (`:174`) | pure thunks | `List (Nothing -[e]-> a) -[e]-> a` |
| `fork` (`:171`) | `(Nothing -> a) -> Task a` | `(Nothing -[e]-> a) -[e]-> Task a` |
| `await` (`:172`) | `Task a -> a` | unchanged (pure) |
| `print-line` et al. (`:132-137`) | `EffectfulTy [Console]` | concrete `{Console}` row on the arrow |
| `read-line`, `current-dir` (`:146`, `:167`) | `EffectfulTy` value | unchanged (§4: values keep `EffectfulTy`) |

**`fork` charges the spawner (DECIDED here).** The forked thunk's
effects are attributed to the `fork` call site, not deferred to `await`.
Attributing them to `await` would require effect-indexed `Task e a`
(a non-goal, §2) and is unsound anyway under fire-and-forget (a task
never awaited would perform effects no row ever paid for). Conservative,
sound, and matches structured-concurrency intuition: the parent that
spawns the work answers for it.

**`lazy` thunks:** `ALazyExpr` types as `FunTy int {} inner`
(`TypeCheckerInference.codex:953-958`); the deferred body's row must land
on that arrow, not vanish — `lazy (print-line "x")` forced in a pure
context is probe material (§13).

**Class methods:** a `class` operation's declared arrow rows are the
contract; `instance` bodies are checked against them by the ordinary def
machinery, so an effectful instance behind a pure class signature dies at
the instance's definition boundary. No special dictionary rule needed —
but the probe exists because the claim is load-bearing.

**Foreword HOFs** (`list-map`, `filter`, `fold-left`, `Iterate`,
`Pipeline`, the `Sort` comparators, ...) get row variables in their
declared signatures. Foreword signature changes bake into the seed:
stage 2 requires a seed rebuild per the procedure in
`DevelopersGuide.md`, and the sweep must include `codex/test/forewords/`.

## 10. IR wire format and plugs (new constraint)

`IRTextEmitter.codex:206` renders arrows as `(fn <param> <ret>)` and
declared effect types as `(a-eff (effs ...) (scopes ...) <ret>)`
(`:243`). Every plug — all 52, native backends included — parses this
with the shared `IRTextParser`. **Decision: rows are erased at the IR
boundary.** `(fn p r)` stays two-field; `EffectVar`/`ForAllEff` never
reach IR (defaulted or instantiated away by codegen time, §6(a));
`(a-eff ...)` continues to carry the declared concrete effects for plugs
that surface them (documentation emitters, capability manifests). This
matches the "no runtime cost" non-goal — effects are a checking
artifact — and means **zero plug rebuilds** for stages 1-3. The
alternative (widening the wire format) buys nothing today and would
require a lock-step rebuild of every plug plus the cross-arch batteries;
rejected. If a future plug needs per-arrow rows (a Koka or Haskell
emitter), extend `(fn ...)` with an *optional* third field then, with a
parser that accepts both arities during the transition.

## 11. Diagnostics

Per Virtue 4, the new failure modes get numbered, worded diagnostics at
design time, not after:

| Code | Severity | Condition | Message sketch |
|---|---|---|---|
| CDX1120 | error | two row tails in one bracket | "an effect row may name at most one row variable; found 'e' and 'f'" |
| CDX1121 | error | scope/dot on a row variable | "'e' is an effect-row variable; scopes and sub-effects attach to concrete effects" |
| CDX2090 | error | closed-row mismatch | "this expression performs [FileSystem] but the context allows only [Console]" |
| CDX2091 | error | row occurs check | "effect row would be infinite (row variable absorbs itself)" |
| CDX2092 | warning | handling an effect the body cannot perform | "handler for 'Console' encloses a body whose type performs no Console" |
| CDX2093 | error | effect variable escapes to codegen | internal — assert, do not ship programs past it |
| (reuse) `cdx-effect-undeclared` | error | def-boundary `row-le` failure | existing wording, now row-aware |

Messages name the effects, per the diagnostics doctrine — "cannot unify
row ?e3" is a bug, not a message.

## 12. Migration plan (each stage a green fixed point)

Blast radius, measured (word-boundary grep, CL 6506): `FunTy` — 256
occurrences across 15 compiler files; `EffectfulTy` — 53 across 13. The
heavy files: `TypeEnv.codex` (188 — almost all builtin signatures, one
mechanical shape), `TypeChecker.codex` (12), `TypeCheckerInference.codex`
(9), `Lowering.codex` (9), `X86_64Compound.codex` (7),
`CodexEmitter.codex` (6), remainder ≤ 4 each. AST-level `AFunType` /
`AEffectType` are separate constructors and stage 1 leaves their shapes
alone (the desugarer maps them onto the new `FunTy`).

0. **Probes first.** DONE 2026-07-02 — see §13 Stage 0 results. The
   catalog is landed, the depot sweep came back clean, and two
   pre-existing checker bugs found by the probes were fixed and
   seed-rebuilt (CLs 6508-6510).
1. **Syntax + representation, inert.** Lexer/parser accept row
   variables; add `EffectVar`, `ForAllEff`, `EffectRow`, the third
   `FunTy` field; every existing site constructs the empty closed row;
   `make-row` is the only row constructor. Unifier ignores rows
   (strip-equivalent). IR emission unchanged (§10). Printer emits `[...]`
   only for non-empty rows — **the text fixed point is the gate that
   proves the printer and parser round-trip**; run semantic equivalence,
   pingpong, CDX fixed point, full battery. Seed rebuild (compiler
   changed). A regression here is a shape bug, not a semantics bug —
   that is the point of the stage.
2. **Row unification, no direction.** `unify-row` per §5 (canonical
   merge, tail absorption, occurs check, kind-disjoint resolution);
   arrow-vs-arrow unifies rows; handler rule from §7. Retype the
   builtins and foreword HOFs (§9); seed rebuild. This is where
   legitimately-effectful compiler code first flows effects through
   inference — budget for surfacing latent under-declarations in the
   compiler's own `[Console, FileSystem]` code (each is a real bug being
   found, but it is throughput, not a soundness event).

   **As-built (stage 2, CL 6532; seed CL 6533).** Three scoping
   decisions against
   the paragraph above, each because the transitional split
   (concretes in `EffectfulTy`, tails on the arrow) is still in
   force:

   - **The EffectfulTy-to-row migration of concrete effects is
     deferred to stage 3.** Every effect reader — `extract-effects`
     at the def boundary, `effect-le` at the argument boundary, the
     act-union — reads `EffectfulTy`. Moving concretes onto arrow
     rows before those readers are row-aware would drain the existing
     point checks silently and regress the stage-0 policed probes
     (direct CDX2031, let CDX2033). Stage 3 makes the readers
     row-aware; that is when concretes move.
   - **The §7 handler rule is deferred to stage 3 with it.** The rule
     unifies the BODY's row against the extension {E, t'} — but under
     the split a body's effects are not in any row, so the rule would
     unify empty rows and enforce nothing while appearing to be
     implemented. It lands with the migration that makes body rows
     real.
   - **HOF retyping shipped:** TypeEnv `map`/`par`/`race`/`fork` (per
     the §9 table, `fork` charging the spawner), the compiler's own
     `Collections` map/fold family (every `for` expression desugars
     to `map-list`, so row instantiation and unify-row run at every
     for loop in the selfhost — the fixed point exercises the
     machinery end to end), and the foreword `ListUtils`
     map/fold/filter family. `Iterate`, `Pipeline`, and the `Sort`
     comparators follow in stage 3 with the enforcement that makes
     their rows meaningful. The `print-line` row migration in the §9
     table is part of the deferred concrete migration.

   No latent under-declarations surfaced: with the split intact and
   rows inert, every solved row is the empty row and the battery and
   fixed point are behavior-identical by construction — the
   under-declaration budget moves to stage 3.
3. **Open instantiation + directed `row-le`.** §6(a) opening in
   `instantiate-type`; §6(b) row-le at the three boundaries; delete the
   `unify-at` stripping arms; remove `effect-le`'s type-variable
   leniency. The laundering probes flip from documenting the hole to
   enforcing its absence. This is the stage with real type-theory risk;
   it ships with the full probe catalog green in both directions.

   **Stage-3 architecture (RULED by Damian, 2026-07-02): ambient row
   threading.** The checker's judgment becomes Γ ⊢ e : τ | ε —
   `CheckResult` gains an `effect-row` field carrying the expression's
   ambient effects (what evaluating it performs; the effects of a
   function VALUE it produces live on that value's arrow, not in the
   ambient). The alternative (keep `EffectfulTy` as the ambient
   carrier on result types, wrap at applications) was rejected: it
   preserves the dual representation forever and forfeits the §12
   stage-3/4 deletions. Consequences, worked out before
   implementation:

   - **Ambient union equates open tails (the Koka shared-ε move).**
     `row-union` of two resolved open rows with distinct tails t1 ≠
     t2 binds `t1 := {| t2}` and yields `{labels1 ∪ labels2 | t2}` —
     representable rows have at most one tail, so independent
     subexpression tails inside one body are EQUATED. This is exactly
     what Koka's single ambient ε per judgment does implicitly (every
     performing site unifies with the same ε); it over-approximates
     (two HOF calls in one body share an effect variable) but never
     under-approximates, which is the §2 soundness direction.
     `row-union` therefore threads UnificationState. Fast paths: an
     empty operand returns the other operand unchanged — pre-migration
     every union is empty ∪ empty and returns the interned empty-row
     with zero allocation.
   - **Applications capture the callee row with a fresh variable.**
     `infer-application` synthesizes `FunTy arg {t} ret` with t fresh
     (today: interned empty-row, which post-migration would hard-fail
     CDX2090 against every effectful callee). Its ambient is
     `fun.ambient ∪ arg.ambient ∪ {|t}`; t resolves to the callee's
     row once unified. Argument-evaluation effects are charged even
     when the application itself is a partial application (arg
     evaluation happens; the arrow's own row only surfaces at the
     performing arrow, matching §4 curried placement).
   - **Lambda and lazy arrows get their rows for free.** A lambda's
     ambient is EMPTY (a value performs nothing); its BODY's ambient
     row becomes the innermost synthesized arrow's row
     (`wrap-fun-type` takes it as the last-arrow row). Same for
     `ALazyExpr`'s thunk arrow — which closes the lazy laundering
     probe by construction. Pre-migration the body ambient is always
     empty, so synthesized types are bit-identical to today's.
   - **Undeclared defs** get a fresh row variable on the innermost
     arrow of `build-undeclared-fun-type`; the body's ambient row
     unifies into it at the def boundary.
   - **Effectful VALUES** (§4: `read-line`-shaped `EffectfulTy`
     bindings, which stay) contribute their effect set to the AMBIENT
     row at `infer-name`, and their type unwraps to the inner value
     type there. This lands with the migration, not the plumbing.

   **As-built (stage 3b).** Shipped as specified, with these
   decisions worth recording:

   - **Zero under-declarations surfaced in the compiler's own 28k
     lines** — the old boundary check had kept direct declarations
     honest, and the compiler never exploited the HOF routes
     internally. The budgeted triage was not needed.
   - **The battery's only failures were the five open laundering
     probes flipping to rejects** (map/lazy 2031 at the def
     boundary; record/fork 2090 at unification; handler-clause
     2031+2033) plus two already-rejecting probes whose mechanism
     moved from effect-le's 2031 to unification's 2090
     (launder-hof, launder-partial). Catalog green both directions.
   - **effect-le is deleted, not made row-aware**: its job is done by
     row unification itself (a concrete pure parameter's closed row
     rejects an effectful argument's opened row), and its
     type-variable leniency has nothing left to excuse — a
     row-polymorphic parameter constrains through its variable.
   - **The def boundary reuses CDX2031** via check-effect-row-subset
     (name-lattice coverage through effect-covered-by; tails
     ignored — the signature's own variable is trivially allowed and
     any other unsolved tail absorbed nothing). Scope literals ride
     the labels but are not enforced at this boundary; scopes remain
     the capability machinery's. Argument-boundary DOTTED widening
     (passing a Console.Write-rowed function where a Console-rowed
     one is expected) is not lattice-aware — unification is exact —
     and is deferred until a real program needs it.
   - **CDX2033 reads the binding's ambient labels**; a row that is
     only an unsolved tail is allowed at a let (the HOF-body
     pattern; the def boundary answers for it).
   - **Handler**: body row unified against {E | t'} when it can
     perform E (has the label or is open); closed-without-E warns
     CDX2092 and discharges nothing. Clause BODIES are inferred for
     their ambient (params + resume bound as fresh vars) and unioned
     into the result; clause VALUE typing stays with lowering, as it
     was. Dotted names are not discharged by a parent handler
     (exact-name membership), matching filter-effect's existing
     exactness.
   - **The CodexType printer learned to render row labels** (with
     scopes) — emit-def prints signatures from the CHECKED type, and
     the stage-1b emit-row-result only knew tails. Canonical
     name-sorted label order is the single rendering; the text fixed
     point pins it.
   - **check-rt-effects (punctual CDX6004) and the opening
     capability check read effects through collect-effect-names** —
     spine arrow-row labels plus effectful-value sets — instead of
     the EffectfulTy-only extract-effects.
   - **Sort comparators and Pipeline stay strictly pure by design**:
     an effectful comparator observed under sort reordering is
     exactly what the system should refuse, and Pipeline is
     Integer-monomorphic utility code. ListUtils, Collections, and
     Iterate are the effect-polymorphic families.
   - Value defs (pcount = 0 with an EffectfulTy signature) unwrap
     the declared type for the body unify; their effect set becomes
     the declared row for the boundary check via
     declared-performing-row.

   **Stage 3 ships as two gated CLs.** CL 3a — the ambient calculus,
   inert: the `effect-row` field, `row-union`, per-arm unions
   (union of subexpression ambients everywhere; match unions
   scrutinee + guards + arm bodies; act unions statements alongside
   the untouched EffectfulTy machinery; handler unions body +
   clauses, no subtraction yet), application capture, lambda/lazy/
   undeclared-def arrow rows. Pre-migration every ambient row
   computes to the empty row, so the fixed point and battery are
   byte-identical — that gate PROVES the union plumbing drops
   nothing. CL 3b — the migration and the teeth: concretes move onto
   arrows (`resolve-type-expr`, TypeEnv `print-line` et al.),
   `infer-name` value-effects, readers switch to ambient rows
   (act/def-boundary/let-boundary), §6(a) opening, §6(b) row-le,
   §7 handler subtraction, stripping arms deleted, effect-le
   leniency removed, probes flipped. 3b is where under-declarations
   in the compiler's own code surface; budget for it.
4. **Retire the transitional forms.** `strip-effect-from-env` /
   `strip-handled-effect` (§7), the act-union's manual effect
   accumulation if arrow rows now subsume it, `EffectfulTy`-wrapping of
   arrows (checker assertion per §4). Keep `check-effect-subset`-as-
   `row-le` per §8.

   **As-built (stage 4, CL 6560).** All deleted: act-wrap-effects /
   act-value-type / union-names and the act loop's name threading
   (act types are bare; the ambient row is the only carrier);
   strip-effect-from-env's whole-environment rewrite and
   strip-handled-effect / filter-effect (discharge is the §7
   unification; the handle expression's type is the body's); the
   act-bind EffectfulTy arm (infer-name already unwraps values);
   extract-effects / extract-scopes (no callers survived 3b). The §4
   assertion landed as a USER diagnostic, not an internal one:
   `[E] (A -> B)` in a value signature is expressible source, so it
   is rejected at def registration with CDX2094 and the
   write-it-on-the-arrow fix in the message (the depot had zero
   occurrences). check-effect-row-subset stays as the def-boundary
   check per §8.

Stages 1-2 are mechanical-but-wide; stage 3 is the hard type theory.
Every stage: shelve, revert, sync, unshelve, inspect, then gates — per
`docs/Agents/PerforceProcess.md`. Zero failures before copy-up, verified
against the last-known-good baseline, not asserted.

## 13. Adversarial probe catalog

Each probe is a small `.codex` test; "reject" probes get `.failing`
sidecars with the expected CDX code, "accept" probes get `.expected`.
The catalog is the executable form of the soundness claim.

**Must reject (laundering routes):**

| Probe | Route |
|---|---|
| effectful fn → `map` in pure def | the original CL 6494 residual hole |
| effectful fn → `fold`/`filter`/foreword HOF | same, via seed-baked signatures |
| effectful thunk → `fork`, never awaited | fire-and-forget laundering |
| effectful thunk → `lazy`, forced in pure context | deferral laundering |
| effectful `instance` behind pure `class` method | dictionary laundering |
| effectful closure stored in record field, called from pure def | data-structure laundering |
| partial application: effect on last arrow, applied through pure HOF | curried-row placement |
| `when E handle` then perform `E2` (unhandled) in clauses | handler-clause effects dropped |
| act block whose mid-statement effect exceeds the signature | regression guard on the CL 6494 union |
| widened arrow written into a `mutable` record field | variance hole (§6(a) invariance) |
| `Console.Write` declared, `Console` performed | lattice direction (wrong way) |

**Must accept (over-rejection guards):**

| Probe | Why it must stay legal |
|---|---|
| pure fn → parameter expecting `[Console]` fn | subsumption via opening |
| `Console.Write` performed under declared `[Console]` | lattice, right way |
| pure `map` usage everywhere it exists today | `e := {}` instantiation; the whole battery is this probe |
| handler discharging an open-row body | §7 extension rule |
| nested same-effect handlers | set-discharge semantics preserved |
| the compiler compiling itself | the fixed point, as always |

### Stage 0 results (landed 2026-07-02, probes in `codex/test/`)

Twelve probes compiled against the current seed. Ground truth:

**Open laundering routes, confirmed at runtime** (probes land as
passing `.expected` tests documenting the hole; stage 3 flips each to
`.failing` CDX2031): `effect-launder-map` (declared-pure def prints
via `list-map`), `effect-launder-record` (effectful closure through a
pure record field), `effect-launder-lazy` (deferred effect forced via
a HOF), `effect-handler-clause` (handler clauses are not effect-checked
at all — the §7 clause-union rule is not optional), and
`effect-launder-fork` (type-level only; the unawaited thunk never ran).

**Already policed** (land in `errors/` as `.failing` regressions):
direct concrete-parameter laundering (CDX2031, pre-existing test),
partial application (CDX2031), let-bound effectful value (CDX2033),
effectful instance body behind a pure class op (CDX2031 — instance
defs are held to the class signature by the ordinary def boundary),
dotted wrong-direction (CDX2031).

**Two compiler bugs found and fixed by this stage:**

- CL 6508 — act-bind matched the raw inferred type when stripping
  `EffectfulTy`, missing a type variable bound to one; every later use
  of the bound name tripped CDX2031. (`TypeCheckerInference.codex`,
  act-bind arm; pinned by `effect-map-effctx`.)
- CL 6509 — `find-dot` compared against ASCII 46 ('H' in CCE); the
  dotted sub-effect lattice had never functioned. (Pinned by
  `effect-dotted-allow`/`-deny`.)

Seed rebuilt with both fixes: CL 6510, digest `7928F8FD…`. Nested
same-effect handler semantics pinned by `effect-handler-nested-same`
(innermost discharges completely — the §5 set-semantics commitment).
The §3 depot sweep found zero lowercase identifiers in effect-bracket
type position; the stage 1 reclassification is unobstructed.

## 14. Memory and time-complexity verdict (Rule 8)

**Heap.** `FunTy` gains one field: +8 bytes per arrow node, plus one
shared static empty-row record (intern it — one allocation, every pure
arrow points at it; do *not* allocate a fresh empty row per arrow, which
would be ~256+ sites × per-instantiation copies). Rows allocate only
when non-empty (compiler tree today: a few dozen effectful signatures)
or when opened (§6(a): one `EffectVar` + one small row per *arrow* per
*instantiation* of a referenced name — bounded by application count,
the same order as the fresh `TypeVar`s `instantiate-type` already
mints). `subst-type-var` / `codex-type-map-children` walks gain a field
to copy but no new recursion. Expected CHECK-deck growth: single-digit
MB on the selfhost (baseline ~69 MB, survey `S × 400 + units × 296000 +
1 MB` with 120% headroom — Sketchbook). **Measure, don't trust:** run
`MEASURE` mode before/after stages 1 and 3, diff `heap hwm` per phase;
bump `survey-check-mul` only on evidence (CDX9002 is an error, so
under-reservation halts cleanly rather than corrupting).

**Time.** `unify-row` is a sorted merge, O(labels) per call with labels
≤ 2 in practice; occurs check is a tail-chain walk. Opening adds
O(arrows-in-type) work to instantiation, same shape as existing
`ForAllTy` freshening. No new loops over defs, no accumulators that
outlive a phase, no `buf-read-bytes`, no deck retention across phases.
Verdict: bounded, phase-local, measurable — no red flags. Pingpong
elapsed-time diff at each stage is the enforcement.

## 15. Risks and open questions

Resolved into decisions above (kept here so reviewers see they were
raised): dotted sub-effects in unification (§5 — lattice only in
`row-le`); `EffectfulTy` retire-or-keep (§4 — keep for values);
polarity through the unifier (§6 — rejected for open instantiation +
directed `row-le`); handler typing (§7); wire format (§10).

Remaining risks, honestly held:

- **Blast radius of the FunTy shape change.** 256 sites. Mitigation
  unchanged from the draft and still correct: stage 1 is purely
  representational, so a regression is a shape bug caught by the fixed
  point, not a semantics bug caught three phases later.
- **`row-le` variance.** The one place direction lives. Wrong either
  way = over-rejection or a reopened hole. Mitigation: the §13 catalog
  tests both directions of every boundary, and `row-le` is small enough
  to review as a unit against the variance table.
- **Fixed point under stricter rules.** The compiler's own effectful
  code must re-typecheck; every under-declaration surfaced in stage 2-3
  is a latent bug being found (good) and a schedule cost (budget it).
  Unknown count until stage 2 runs — that is *why* stage 2 is its own
  gated CL.
- **Text fixed point vs printing.** The printer must emit exactly one
  rendering per row (canonical order, no empty brackets, tail last) or
  pingpong breaks in the most tedious possible way. `make-row` + one
  printer function, tested by the existing text round-trip gate.
- **Opening-rule leaks.** §6(a)'s "never open binding positions" is a
  rule about *sites*, enforced by code paths, not by types — a missed
  site is an unsoundness (opening a mutable field's type) or an
  over-acceptance. The mutable-field probe and a checker-internal
  assertion (opened row reaching a binding position) both guard it.
- **Scope-label subsumption semantics** (`Console "auth"` vs
  `Console`). Deferred to the `row-le` implementation with a
  one-decision test; either answer is sound, the risk is deciding it
  twice inconsistently.

## 16. Prior art (Virtue 10 — read before implementing)

- Lucassen & Gifford, *Polymorphic Effect Systems* (POPL 1988) — the
  origin of effect rows on arrows.
- Rémy, *Type Inference for Records in a Natural Extension of ML*
  (1989/1994) — row variables, tail absorption, why payloads make
  principality hard (and why our payload-free labels dodge it).
- Leijen, *Extensible Records with Scoped Labels* (TFP 2005) — the
  multiset alternative we deliberately reject (§5).
- Leijen, *Koka: Programming with Row-Polymorphic Effect Types*
  (MSFP 2014) — the open-rows instantiation trick (§6(a)), effect
  safety statement, handler row rule (§7).
- Pretnar, *An Introduction to Algebraic Effects and Handlers* (2015)
  — handler semantics the runtime already implements.
- Dolan & Mycroft, *Polymorphism, Subtyping, and Type Inference in
  MLsub* (POPL 2017) — the principled subtyped-inference road not
  taken, cited so the rejection is informed, not ignorant.

## 17. Recommendation

Syntax (§3) is decided: brackets. Land stage 0 (probes + depot sweep)
immediately — it has value even if the rest slips, because it converts
the BACKLOG's "verify the safety claims" stream into executable form.
Then stage 1 (representation) as one mechanical CL gated on the fixed
point, before any semantics change. Stages 2-3 carry the real risk and
each want their own CL with the probe catalog green in both directions;
stage 4 is cleanup. This is multi-CL, fixed-point-gated type-system
work, not a single change — and at the end of it, the KingsAndCourts
claim stops being a hope and becomes a theorem with a regression suite.
