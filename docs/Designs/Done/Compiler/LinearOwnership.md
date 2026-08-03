# LinearOwnership -- Closing the Linearity Laundering Routes

**Status:** CAMPAIGN COMPLETE (stages 0-4 shipped 2026-07-03). ALL
NINE laundering routes from the stage-0 probe catalog are enforced;
every probe lives in errors/ with a .failing sidecar, plus positive
guards pinning the sanctioned patterns. Residual refinements (not
probe routes) are listed in the stage 4 as-built notes: locals
minted from linear-returning calls, container literals in
argument/tail position, effectful linear returns, move-site spans
in diagnostics.
**RULED (Damian, 2026-07-03): ownership-move semantics -- Rust-like,
kept linear.** The decision rule was "multiplicity if it's better,
Rust-like if it's the same or better"; the literature verdict (§6)
found multiplicity better on one axis (HOF polymorphism) and worse
on the axes that carry Codex's actual promises, so moves win.
Implementation NOT STARTED; stages in §5 are the ruled shape.

**Provenance:** BACKLOG "Fulfill the Vision Check" item 1, linear
leg. Found by the EffectRows stage-0 method: write adversarial
programs that try to break the headline claim, see which ones the
compiler rejects. blu, 2026-07-03.

---

## 1. The claim under test

DevelopersGuide (pre-fix): "A `linear` value must be used exactly
once on every path -- not dropped (leak, CDX2063) and not reused
(CDX2061)." VisionAndVirtues: "Use-after-free is a type error here."
KingsAndCourts CRA 1(a): "Linear types (no UAF/double-free) --
BY-CONSTRUCTION."

## 2. The mechanism as built

`check-linearity-def` (TypeChecker.codex, Linearity Checking
section) runs per def: for each *declared parameter* whose declared
type is syntactically `linear T` (or a `mutable` record type), count
mentions of the parameter name in the def body (`lin-of` /
`consume-of`). Exactly-once per path is enforced by mention count
with branch consistency. `ALinearType` is *erased* at type
resolution (`resolve-type-expr` unwraps it), so linearity does not
exist in the type system at all -- it is a per-def syntactic lint on
parameter names. The section's own prose says: "This slice enforces
both for function parameters; tracking ownership through arbitrary
let-bound locals is later work."

## 3. Stage-0 probe results (seed E1E4A10B)

Nine probes. **All nine compile clean and execute the violation at
runtime.** Each lands as a passing `.expected` test pinning the
permissive behavior; when enforcement closes a route, its probe
flips to `errors/` with a `.failing` sidecar -- the catalog must be
green in BOTH directions at every stage (the EffectRows ship gate).

| Probe (codex/test/) | Route | Today |
|---|---|---|
| linear-launder-local-leak | param aliased to let-local, local dropped | CLOSED stage 2 (CDX2063) |
| linear-launder-local-dup | param aliased to let-local, local used twice | CLOSED stage 2 (CDX2061) |
| linear-launder-closure | captured in `\x ->`, closure called twice | CLOSED stage 4 (CDX2061, call-once) |
| linear-launder-partial | captured by partial application, applied twice | CLOSED stage 3 (CDX2065 via the plain-param boundary) |
| linear-launder-list | stashed in `[n]`, two list-at reads | CLOSED stage 4 (CDX2065, container owns) |
| linear-launder-record | stashed in record field, two field reads | CLOSED stage 4 (CDX2061, container owns) |
| linear-launder-boundary | linear arg passed to plain (non-linear) param | CLOSED stage 3 (CDX2065) |
| linear-launder-return | `launder (n) = n` typed with plain return | CLOSED stage 3 (CDX2066) |
| mutable-launder-alias | mutable param aliased to let-local, two owners | CLOSED stage 2 (CDX2062) |

Policed routes (still correctly rejected, pinned by existing tests
`errors/linear-errors` + `linear-branch`): direct double mention
(CDX2061), direct drop (CDX2063), branch-count mismatch,
freeze-then-reuse in one expression.

## 4. Reading the results

The failure shape is IDENTICAL to the pre-EffectRows effect system:
a real discipline enforced at exactly one syntactic position
(declared def parameters), silently shed by every indirection. Two
distinct deficits compose:

1. **No flow tracking.** Mentions are counted, ownership is not
   followed -- a let-alias, capture, or container store is one
   mention and the trail ends there.
2. **No type-level presence.** `linear` is erased at resolution, so
   boundaries (argument passing, returns, fields) cannot possibly
   enforce what the types no longer say. `freeze : linear a -> a` is
   currently a no-op ceremony -- the checker treats every boundary as
   a free freeze.

Deficit 2 gates deficit 1: without linearity surviving into
CodexType, interprocedural routes (boundary, return, container
element types) cannot close. This mirrors EffectRows exactly
(effects had to become first-class on arrows before enforcement
could live in unification).

## 5. The ruled shape -- ownership moves, kept linear

Move semantics with mandatory consumption. Rust-like, not
Rust-identical: Rust is *affine* (silent drop runs a destructor);
Codex keeps *linear* (a drop is CDX2063 -- resources are released by
an explicit consuming call, never implicitly). No borrow checker in
scope: Codex's read-borrow niche is already served by `freeze` (one
way, permanent) and by `mutable`'s free-reads rule; lifetimes and
temporary borrows are explicitly OUT of this campaign.

The core judgment: a linear value has exactly one **owner** (a
binding). A mention in a consuming position **moves** ownership;
the old binding is dead from that point (later mention = CDX2061,
now "moved", with the move site in the diagnostic); every owner
must be consumed exactly once on every path (unconsumed at scope
end = CDX2063). Consuming positions: passing as an argument,
let-binding (transfer to the new name), storing in a container or
record field, capture by a lambda or partial application, and
returning.

Staging (EffectRows-patterned; every stage gates on the probe
catalog green in BOTH directions plus the standard gates):

- **Stage 1 -- representation. SHIPPED (blu CL 6840 + seed 6841,
  digest 4A4B42DB...).** `LinearTy (CodexType)` appended as the LAST
  CodexType ctor (append-only rule); `resolve-type-expr` keeps the
  wrapper instead of erasing. Inertness architecture, as built:
  `unify-at` strips both sides at entry (`strip-linear-ty` in
  CodexTypeHelpers) so no substitution ever carries the wrapper;
  `bind-def-params` binds parameters STRIPPED, so body inference and
  the IR never see it -- the wrapper survives only on the registered
  signature, which is exactly where stage 2's walk will read it.
  Representation-transparent arms: is-pointer-type,
  integer-ty-hw-width, field-byte-width/signed, emit-const-codextype,
  resolve-ty-deep (IR-side erasure), ir-emit-type / ir-tvars-of-type
  (wire bytes unchanged -- no plug rebuilds). Recursion arms: the
  CodexTypeTree map/fold helpers (which cover subst-type-var,
  subst-type-var-in-target, deep-resolve, ty-has-typevars, and thus
  instantiation), parameterize-walk-children (freeze stays
  polymorphic -- the old-map-#8 trap class), occurs-in, collect-tvars,
  normalize-type, type-mentions-mut. Printers: emit-type renders
  "linear T" (TEXT totality), type-desc renders it in diagnostics.
  Gates: hard fixed point in ONE PASS on the first build (the new
  arms are dead code for the selfhost -- the predicted inertness
  proof), battery 287/272/0/15 (+1 = new regression guard
  linear-poly-freeze pinning freeze at two instantiations),
  self-verify green.
  AS-BUILT DEVIATION: the `mutable` flag is DEFERRED to stage 2. The
  compiler's own source declares mutable records (UnificationState,
  ADef, LexState, Scope, SurveyConfig), so flagging mutable types in
  the representation stage would fire on the selfhost and forfeit
  the inertness proof -- and the flag has no reader until the
  stage-2 walk exists. The `__mutable-<name>` env marker remains the
  mutable representation until then. The unification of both
  disciplines (§6 verdict 4) is unchanged; it lands with the walk
  that reads it.
- **Stage 2 -- local flow. SHIPPED (blu CL 6856 + seed 6860, digest
  351FF9CE..., 2026-07-03).**
  The mention count became an ownership walk: a let-bind whose value
  is a BARE mention of the tracked owner is a move (`lin-bind-is-alias`
  in TypeChecker's Linearity section). The walk re-roots on the new
  name via `lin-let-moved` / `consume-let-moved`, which join two walks
  of the remainder (`lin-move-join`): the live chain rooted at the new
  owner (which inherits the exactly-once obligation) and the dead walk
  of the old name, EVERY residual mention of which -- reads included,
  for mutable -- tallies into a new `dead` field on `LinResult`.
  `dead > 0` is CDX2061 (linear) / CDX2062 (mutable) with a "moved to
  a new owner ... the original name is dead" message; the move-site
  span in the diagnostic remains deferred (would need
  UnificationState threaded through the walk). A self-alias
  (`let n = n`) transfers to the same name and the walk continues. The
  mutable consume walk was converted from bare Integer returns to the
  same `LinResult` record, so both disciplines now share one ownership
  walk (the section 6 verdict-4 unification) -- alias stopped being a
  counted consume and became a transfer, branch max moved into
  `lin-path-max`, and `consume-max` was deleted. Chained aliases
  (`let a = m in let b = a`) re-root cleanly; an alias OF a dead name
  is conservatively at least one dead mention. mutable-smoke's `tally`
  (`let x = b`, then field reads/assigns through x only) stays legal:
  transfer plus free reads, zero consumes. Gates: one-pass hard fixed
  point on the first build, battery 287/272/0/15 with the three probes
  now green in the failing direction, self-verify green. The
  `mutable` representation flag deferred from stage 1 remains the
  `__mutable-<name>` env marker -- stage 2's walk reads it there; the
  flag-on-CodexType consolidation can ride stage 4.
- **Stage 3 -- boundaries. SHIPPED (blu CL 6868 + seed 6871, digest
  F5A68CBD..., 2026-07-03).**
  Argument position: a bare mention of the tracked owner in argument
  position is sanctioned only when the callee's parameter at that
  position is declared linear in its REGISTERED signature -- the
  wrapper stage 1 deliberately left there (`lin-spine`/`lin-arg`/
  `callee-param-linear` in the Linearity section; positions resolved
  left-indexed off the apply spine, ForAll wrappers unwrapped).
  freeze's own `linear a` parameter makes it the sanctioned door with
  NO special case at call sites. An unknown or unregistered callee (a
  function value, a lambda head) is conservatively a plain boundary.
  Violation = CDX2065 LinearEscape, carried in a new `esc` field on
  LinResult (first offending callee's name). Return position: the
  walk threads a `tail` flag (True only where the expression is the
  def's return value; act-block statements conservatively non-tail);
  a bare owner mention in tail sets `ret`, and `ret` without a
  linear-declared final return type is CDX2066 LinearReturn. The
  freeze identity itself (def named freeze whose body is a bare name)
  is the ONE exempt door. Ownership chains keep the tail obligation:
  `let h = n in h` under a plain return is CDX2066 through the moved
  chain. AS-BUILT NOTES: this closed the PARTIAL route too -- the
  probe's `let g = add2 n` hands the linear to add2's plain first
  parameter, so the capture route runs through the argument boundary
  (capture through a linear-declared parameter remains stage 4's
  call-once rule). Handler-clause capture (ruled here: a clause may
  run zero or many times, so clause capture of a linear is an error)
  is ENFORCED with stage 4's capture semantics, where it belongs
  mechanically. A linear return under an effect row (`-> [E] linear
  T` returning the bare param) is not yet recognized as sanctioned --
  no such code exists in the depot; refine when the first appears.
  Local minting (a local bound from a linear-returning call, e.g.
  serial-line's bus-scenario) is still untracked -- that is not one of
  the nine probe routes; file it with stage 4+ scoping. Gates:
  one-pass hard fixed point on the first build, battery 287/272/0/15
  (probes green in the failing direction, codes verified by the
  harness), self-verify green. Closes boundary, return, partial.
- **Stage 4 -- capture and containers. SHIPPED (blu CL 6883 + seed
  6886, digest 47CABCEA..., 2026-07-03).** One mechanism serves all three routes: a let-bind
  whose value RETAINS the owner moves ownership into the binding
  (`lin-bind-retains` / `lin-retain-join`), and the binding is then
  held to the same exactly-once discipline -- so the stage 2/3 rules
  compose from there with no container- or closure-specific error
  machinery. Retaining values: a lambda whose body mentions the
  owner (the binding is the closure, call-once -- each application
  is a head-position mention, so `g 1 + g 2` is CDX2061 "used 2
  times"; Rust's FnOnce without a kind system); an APPLY that
  involves the owner and is PARTIAL by registered arity
  (`registered-fun-param-count` vs `apply-arg-count`, so capture
  through a linear-declared parameter is call-once too); a
  list/record literal with the BARE owner as an element or field
  (recursively through nested literals -- `lin-stash-elem`; an
  element that merely consumes the owner through a call, like
  `[consume n]`, is NOT a stash, since the container holds a plain
  result). Container consequences fall out: `list-at xs 0` hands
  the linear-holding container to a plain parameter (CDX2065,
  steering toward consuming the container as a whole), and `b.h +
  b.h` is a double mention (CDX2061); one field read is tolerated
  as the single extraction. Capture in run-many contexts is a new
  error, CDX2067 LinearCapture, via a `cap` field on LinResult: a
  handler clause body mentioning the owner (may run zero or many
  times -- the ruled rule from stage 3, enforced here), and a
  capturing lambda escaping as a function ARGUMENT (the callee's
  call discipline is unknown). A capturing lambda in TAIL position
  takes the return obligation instead (`ret`, so CDX2066 unless the
  return is declared linear) -- returning a capturing closure is
  returning the resource. Variant/tuple constructor stash
  (`Some n`, `(n, 5)`) already errors as CDX2065, since ctors are
  plain-param callees. New guards: linear-capture-once (positive:
  let-bound closure called once, partial through a linear param
  applied once), errors/linear-capture-arg and
  errors/linear-capture-clause (CDX2067). Gates: one-pass hard
  fixed point on the first build, battery 290/275/0/15 (287 + 3 new
  guards; all nine probes green in the failing direction),
  self-verify green. Closes closure, list, record -- the catalog.
  RESIDUAL (documented, not probe routes): container literals in
  argument or tail position are counted mentions but not
  ownership-tracked (stash-vs-consume precision needs design;
  pinned open by codex/test/linear-mint-container); the mutable
  flag consolidation onto CodexType still rides a future slice.

- **Minted locals CLOSED (2026-07-07, NoAliasCodegen stage 1,
  blu).** A local bound from a call whose registered return type is
  linear -- `let g = serial-bus-acquire 7`, act-block `h <- ...`
  binds included -- now roots a tracked owner with the full
  exactly-once discipline (`check-mints` walk in TypeChecker's
  Minted Linear Owners section, reusing `lin-let`/`lin-stmts` so
  moves, retains, shadowing, and the argument boundary apply
  unchanged). The callee's return is read from the registered
  signature (LinearTy survives in return position), unwrapping
  EffectfulTy, so `-> [E] linear T` mints too -- and
  `linear-return-sanctioned` now unwraps AEffectType, closing the
  effectful-linear-return residual in the same slice. Probes:
  errors/linear-mint-dup (CDX2061), errors/linear-mint-drop
  (CDX2063); positive guards: serial-line bus-scenario (unchanged,
  stays legal), linear-mint-container (documents the container
  residual). Depot fallout: zero (surveyed -- the only in-scope
  binding sites were bus-scenario's three clean lines). This closed
  residual is what makes the WI-1 no-alias fact airtight for
  declared linear parameters (PhysicalCostCodegen.md).

Deferred by this ruling, with revisit triggers:
- **Multiplicity-polymorphic HOFs** (`map` over a list of linears
  with one signature). Trigger: the first real program that needs
  it. The layering is safe -- arrows already carry one inferred
  annotation dimension (effect rows); a multiplicity dimension can
  be added later WITHOUT unsoundness because the move discipline
  underneath stays valid (moves are strictly stronger; a future
  multiplicity layer would only relax where it can prove safety).
- **Borrows/lifetimes.** Trigger: measured, recurring freeze-copy
  pain in real resource code. Until then, freeze + mutable reads
  cover the observed patterns.

## 6. Literature verdict -- why moves, not multiplicities

Surveyed per Virtue 10: Linear Haskell (Bernardy, Boespflug,
Newton, Peyton Jones, Spiwack -- POPL 2018, multiplicities on
arrows, 1/omega with multiplicity polymorphism; also QTT/Idris 2's
0/1/omega), Clean-style uniqueness typing, Rust ownership
(affine moves + borrow checking), and Granule (which carries both
modalities and demonstrates they are DIFFERENT things). Damian's
decision rule: multiplicity if it's better; Rust-like if it's the
same or better.

1. **The existing semantic promise requires uniqueness, and
   multiplicity does not provide it.** `Linear.codex`'s own prose:
   "While it is held it may be updated in place, because no one
   else can observe the change... Because the linear value is
   unique and is consumed here, no surviving reference can ever
   witness a later mutation -- so freeze needs no copy." That is a
   claim about the PAST (no aliases exist), i.e. uniqueness. Linear
   Haskell multiplicities constrain the FUTURE (how many times a
   value will be consumed): a `%1->` function can legally receive
   an ALIASED argument from an unrestricted context -- linearity of
   the arrow says nothing about other references. Under
   multiplicities, freeze-as-identity-without-copy is UNSOUND and
   in-place update is unsafe without extra machinery. Under
   ownership moves, both are sound by construction (a moved-in
   value provably has no other owner). Multiplicity does not merely
   tie here -- it fails the existing spec.
2. **Implementation fit.** The shipped checker is a per-def
   ownership-flavored walk (`lin-of`/`consume-of` -- consume-of
   already speaks in "passing it on, aliasing, returning").
   Moves EXTEND this walk (track owner, transfer on move, error on
   dead mention). Multiplicities REPLACE it with context-splitting
   judgments and multiplicity variables in unification -- a second
   EffectRows-sized campaign through Unifier/TypeEnv/inference for
   a guarantee that still would not cover the in-place promise.
3. **Diagnostics (Virtue 4).** "`n` was moved into `h` at line 12
   and mentioned again at line 14" names the value, the move site,
   and the fix. Rust proved this teachable at industrial scale.
   Multiplicity mismatch errors ("cannot unify multiplicity 1 with
   omega") reproduce exactly the `cannot unify ?a with Integer`
   class this project calls a bug.
4. **`mutable` unification.** The open question from stage 0 is
   answered by moves: `mutable`'s consume-counting (pass on, alias,
   return -- reads free) IS move semantics with free reads. One
   ownership walk serves both; multiplicities would leave `mutable`
   as a disconnected sibling lint (uniqueness again).
5. **Migration cost.** Depot linear adoption is tiny (Linear.codex
   + tests), so the stricter-now choice is nearly free today and
   buys the strongest base. Linear Haskell's headline virtue --
   gradual retrofit onto a huge existing unrestricted codebase -- is
   solving a problem Codex does not have.
6. **Where multiplicity genuinely wins.** Multiplicity-polymorphic
   HOFs: one `map` signature serving linear and unrestricted uses.
   Real, but currently hypothetical here (zero depot users), and
   deferrable without foreclosure (§5). Better on one axis, worse
   on 1-5: by the decision rule, Rust-like moves win.

## 7. What this is NOT

No compiler change shipped with stage 0 or the ruling. The probes + honest docs
ARE the deliverable: the gap is now load-bearing in the battery
(nobody can half-close a route without a probe flipping), and the
claim surface (DevelopersGuide, ClaimsCalibration) states the true
scope. Remaining vision-check legs (capabilities, punctual) get
their own stage-0 passes; bounded integers were effectively probed
to death by the BoundedSignatures campaign; effects by EffectRows.
