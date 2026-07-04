# Bounded Integers at the Function Boundary

Refinement typing for integer parameters and returns, enforced by a
hybrid static/dynamic discipline uniform with the existing bounded
record-field machinery. This document states the theory, grounds it in
the literature, argues the metatheory, and specifies a staged, gated
implementation.

Status: SHIPPED (2026-07-03, blu). All stages complete and on main:
A (static boundary lint), B (runtime callee guards), C1-C2 (checker
propagation + four adoption families), and the endgame — CDX2051 is
an ERROR. The selfhost went 66 -> 0 across the campaign, the battery
surface was swept clean, and an unproven flow into any bounded
position now fails the build. Successor scope to
`BoundsProverReach.md`, whose slices 1-3 cleared the lint-side flows
(66 -> 45) and established that the remaining plurality was parameter
pass-through no lint-side analysis could discharge.

## 1. The gap (empirical)

Bounded integers already parse and type-check in signatures. The
following compiles clean under seed 7FA0247B and runs:

```
inc-byte : Integer between 0 and 255 -> Integer
inc-byte (n) = n + 1

make-level : Integer -> Integer between 0 and 15
make-level (n) = n
```

But the boundary is neither statically checked nor runtime-enforced.
`inc-byte 300` compiles with no diagnostic and evaluates to 301;
`make-level 99` evaluates to 99. A literal `300` flowing into an
`Integer between 0 and 255` PARAMETER is silently accepted — while the
identical literal flowing into an `Integer between 0 and 255` RECORD
FIELD is a CDX2050 hard error plus a runtime trap. The parameter and
return positions are the one hole in an otherwise-sound refinement
discipline: the type is cosmetic there.

This is the defect. The fix is not to make bounded signatures parse
(they do) but to subject the function boundary to the SAME hybrid
enforcement every other refined position already receives.

## 2. What the bounded-field discipline already is

A bounded integer `Integer between L and H` is a refinement type: the
set `{ n : Integer | L <= n <= H }`. Codex already enforces this at
record-field and constructor-payload stores, by a hybrid rule
(`X86_64Compound.codex`, `emit-narrow-store-proven` /
`emit-narrow-store-checked`):

- **Static discharge.** `ir-expr-proven-range` computes an interval
  over-approximation of the stored value. If that interval is a subset
  of `[L,H]`, the runtime check is ELIDED and CDX4010 (BoundsProven) is
  reported. Zero cost.
- **Dynamic guard.** Otherwise, for the default `OvError` mode and a
  sub-8-byte field width, `emit-error-checked-store` emits
  `cmp; jcc; UD2` against `L` and `H`: an out-of-range value TRAPS at
  runtime before the (width-narrowing) store. `wrapping`/`clamping`
  modes substitute mask/saturate instead of trap. A provably-violating
  literal is rejected earlier still, at type-check time, as CDX2050.

So the field discipline is sound: no value inhabits a bounded field
while violating its bound. Enforcement is static where the compiler can
prove it, dynamic where it cannot, and the two are never both absent.
The checker-side twin of the interval analysis (`aexpr-proven-range`,
built in the reach campaign) drives the advisory layer CDX2051/CDX2053
over the same intervals.

The governing principle of this document:

> **A parameter and a return are refined positions like any other. A
> value flowing into one is governed by the identical hybrid rule: prove
> the refinement and elide, or guard it at runtime. The boundary is not
> special-cased; it is the same operation as a bounded store.**

Uniformity is the whole design. There is no new enforcement mechanism,
only its application to the position that was skipped.

## 3. The two boundary obligations (contract structure)

A function type `f : (Integer between L and H) -> (Integer between P and Q)`
carries two independent refinement obligations, in opposite directions.
This is the caller/callee contract split of Findler & Felleisen
(ICFP'02) and the blame calculus of Wadler & Findler (ESOP'09):

1. **Precondition (argument -> parameter).** At a call `f a`, the
   argument `a` must inhabit `[L,H]`. Discharge: prove `range(a) ⊆
   [L,H]` (elide) else guard `a` at runtime. A violation is blamed on
   the CALL SITE — the caller supplied a bad argument. This is the
   caller's obligation.

2. **Postcondition (body result -> return type).** The body of `f`
   must produce a value in `[P,Q]`. Discharge: prove `range(body) ⊆
   [P,Q]` (elide) else guard the result before RET. A violation is
   blamed on the FUNCTION BODY — it returned a bad result. This is the
   callee's obligation.

The two obligations compose, and the composition is the payoff:

- A bounded RETURN lets a caller of `f` treat the call expression's
  value as inhabiting `[P,Q]`. `let x = f a` gives `x` the range
  `[P,Q]`, which feeds `aexpr-proven-range` downstream. Refinements
  propagate forward through returns.
- A bounded PARAMETER lets the body of `f` treat `n` as inhabiting
  `[L,H]`. Inside `inc-byte`, `n ∈ [0,255]`, so `n + 1 ∈ [1,256]` is
  derivable by the interval arithmetic rules. Refinements propagate
  into bodies through parameters.

This composition is what finally discharges the ~24 internal
parameter-pass-through warnings from the reach campaign: once
`codegen-carry-forward (st) (new-code-len : Integer between 0 and 4294967295) ...`
declares its parameter bounded, the store `code-len = new-code-len`
proves — new-code-len's range IS `[0, 2^32-1]`, exactly the field's
bound.

## 4. The soundness-of-propagation dependency (the crux)

Propagation (Section 3's payoff) is sound ONLY behind runtime
enforcement. This is the subtle point a rigorous reading must not miss.

Suppose the body of `f` trusts `n ∈ [0,255]` (from the parameter bound)
and stores `n` into a `[0,255]` record field, and the prover ELIDES
that field's runtime check on the strength of the parameter bound. If
the parameter boundary itself is NOT runtime-enforced, a caller passing
`300` yields `n = 300`, an elided field check, and a silent
out-of-range store — a corruption that did NOT exist before `n` was
declared bounded (previously `n` was plain `Integer` and the field
check fired). Trusting an unenforced refinement to elide a downstream
check REGRESSES soundness.

Therefore:

> **The prover may treat a parameter's or return's declared range as a
> fact for elision purposes if and only if that range is guaranteed at
> the boundary — statically discharged or runtime-guarded. Until the
> boundary is enforced, a bounded parameter is `⊤` (unknown) to the
> prover, exactly as a plain `Integer` is.**

This gates the implementation staging (Section 7): static boundary
checking ships first and is sound on its own (it only ADDS checks,
never elides a pre-existing one); range PROPAGATION into the prover
ships together with the runtime guard that makes it sound.

## 5. The interval abstraction and its soundness

`aexpr-proven-range : TypeEnv, AExpr -> (Integer, Integer)` is an
abstract interpretation (Cousot & Cousot, POPL'77) over the interval
lattice, with `⊤ = [i64-min, i64-max]` (unknown) as the safe top. The
concretization is `γ([lo,hi]) = { n | lo <= n <= hi }`.

**Soundness theorem (interval abstraction).** For every expression `e`
that evaluates (in a well-typed environment respecting the propagation
gate of Section 4) to an integer value `v`, `v ∈ γ(aexpr-proven-range
env e)`.

*Proof sketch,* by structural induction on the shapes the analysis
models, each an interval transfer function:

- Integer literal `k`: range `[k,k]`; `v = k ∈ [k,k]`.
- Constant name (literal-defined): range is the constant's value
  interval, recorded at registration from its literal body; sound by
  the literal case.
- Let-local: range recorded at its binding from the value expression's
  range, sound by IH on that expression; the binding is immutable so
  `v` equals that value.
- Application head `__narrow x`: passes `range(x)`; `__narrow` is the
  identity on an in-range value and traps otherwise, so a value that
  proceeds is in `range(x)`.
- Application of a structural builtin (`list-length`, `__deck-pos`,
  `__buf-write-bytes`): range `[0, 2^32-1]`, sound because each returns
  a heap-backed count or position that cannot reach `2^32` in the
  3 GB-RAM address space (elements are >= 8 bytes; positions are
  sub-4GB addresses).
- Application of a bounded-RETURN function (Stage B): range is the
  declared return interval, sound because the postcondition guard (or
  static discharge) guarantees the returned value inhabits it — this is
  the propagation of Section 4, valid only behind the guard.
- `if c then t else e`: range is the join (interval union) of
  `range(t)` and `range(e)`; `v` comes from one branch, contained in
  its range, hence in the union.
- `let x = v in body`: range of `body`.
- Any other shape: `⊤`, which contains every value vacuously.

Interval arithmetic transfer functions (`+`, `-`, `*`, `/`, `mod`,
`bit-and`, `shr`, `negate`) are the standard sound interval rules,
already implemented on the emit side (`proven-*-range`) and mirrored as
needed. The bounded-parameter fact (Stage B) adds one case: a parameter
name's range is its declared interval, sound because the precondition
guard guarantees the bound argument inhabited it.

**Corollary (soundness of elision).** If `aexpr-proven-range env e ⊆
[L,H]`, then `e`'s runtime value is in `[L,H]`; eliding the `[L,H]`
guard is sound. This is the justification for every CDX4010/CDX2053.

The analysis is deliberately CONSERVATIVE: unmodeled shapes yield `⊤`,
which never satisfies a proper `[L,H] ⊂ ⊤`, so an unprovable value is
never elided. It cannot manufacture a false proof.

## 6. Metatheory of the boundary discipline

Let a program be *boundary-checked* if every argument-to-bounded-param
and body-to-bounded-return position is either statically discharged or
runtime-guarded (Stages A+B).

**Theorem (refinement preservation).** In a boundary-checked program,
no integer value ever inhabits a bounded type `[L,H]` at runtime while
violating `L <= v <= H`.

*Argument.* Values enter a bounded type only through a refined position:
a bounded store (already enforced), a bounded parameter (precondition
guard), or a bounded return (postcondition guard). Each position either
statically proves containment (Section 5 corollary) or traps a
violating value before it proceeds. No other introduction exists —
integers are immutable and `IntegerTy` unification is
overlap-permissive but does not fabricate values. Hence the invariant
holds at every bounded position, inductively over evaluation.

**Theorem (progress for the refined fragment).** A boundary-checked
program does not get stuck at a bounded operation: either the operand
is in range and the operation proceeds, or a guard traps (a defined
terminal outcome, UD2), never undefined behaviour or a silent
out-of-range store.

**Theorem (conservativity / no false traps).** A runtime guard for
`[L,H]` is emitted with the exact declared bounds and fires only on
`v < L` or `v > H`. No in-range value is ever trapped. Combined with
the soundness of elision, the discipline neither rejects a valid
execution nor admits an invalid one: it is a faithful hybrid of the
static and dynamic readings of the type.

**Blame correctness.** A precondition trap sites at the call (or callee
entry) and attributes to the argument; a postcondition trap sites at
the body's return and attributes to the result. Each violation is
localized to the party whose obligation it was (Section 3).

**Incompleteness (honest limitation).** The interval domain is
decidable and fast (no SMT, no fixpoint iteration over the heap) and
therefore incomplete: it returns `⊤` for arbitrary arithmetic on
unknown values, for cross-procedural flows through unbounded
signatures, and for any predicate not expressible as an interval.
Incompleteness costs a runtime guard (and possibly a CDX2051 advisory),
NEVER soundness. This is the deliberate engineering position for a
bare-metal, GC-free, self-hosting compiler under a byte-exact
fixed-point gate: a decidable interval analysis with a dynamic
backstop, rather than the SMT-backed inference of Liquid Types (Rondon
et al., PLDI'08), which is more complete but heavier and can diverge.
The design occupies the same point as Flanagan's Hybrid Type Checking
(POPL'06): static where decidable, dynamic where not, sound always.

## 7. Staged, gated implementation

Each stage is independently sound and passes all gates before the next.

### Stage A — Static boundary enforcement (type-checker only, one-pass)

Run the existing narrowing lint at the two boundary positions.

- **Argument -> parameter.** In `infer-application`
  (`TypeCheckerInference.codex`), alongside `lint-ctor-narrowing`, add
  `lint-param-narrowing`: when the callee's resolved parameter type is
  `IntegerTy L H OvError`, run `lint-narrowing-check` on the argument
  against `[L,H]`. A provably-violating literal becomes CDX2050 (error,
  as at a field); a wider unprovable value becomes CDX2051 (advisory);
  a proven value becomes CDX2053. `__narrow`-wrapped arguments are
  exempt (the programmer took explicit responsibility), exactly as at
  fields.
- **Body result -> return.** In `check-def-normal`
  (`TypeChecker.codex`, at the `remaining-inner` / body unification),
  when the declared return `remaining-inner` is `IntegerTy L H OvError`,
  run `lint-narrowing-check` on the body expression against `[L,H]`.

Soundness of Stage A: it only ADDS diagnostics and never elides any
pre-existing runtime check, so it cannot regress. It does NOT yet
propagate parameter/return ranges into the prover (Section 4 gate:
unsound before Stage B's guards). `inc-byte 300` becomes a CDX2050
error immediately — matching fields — and provable call sites report
CDX2053. This is the static layer of the discipline, uniform with the
static layer fields already have.

Gate: type-checker-only, so a one-pass hard fixed point; full battery;
new tests. No seed codegen change.

### Stage B — Runtime boundary guards (codegen, seed rebuild) — IMPLEMENTED

Placement decided with Damian: **callee-entry (Eiffel/DbC)** for the
precondition, the lowest-risk sound option, directly implementable
because `IRParam.type-val` carries each parameter's declared bound at
`emit-function`. Call-site precondition elision (the hybrid-typing
zero-cost variant) is a sound future optimization, deferred.

As built (`X86_64.codex`):

- **Precondition.** `emit-param-guards` runs before `bind-params` in
  both the standard and minimal-leaf paths, while `arg-regs` still
  hold the incoming arguments (the prologue's callee-saved pushes do
  not touch them). For each parameter whose `type-val` is an `OvError`
  `IntegerTy` narrower than full i64, it emits `emit-range-trap` on the
  arrival register (`arg-regs[i]` for i<6, else a stack load from
  `[rbp+16+(i-6)*8]`, mirroring `bind-params`' own addressing).
- **Postcondition.** `emit-return-guard` runs before the epilogue in
  both paths. It peels the parameter arrows off `IRDef.type-val`
  (`return-bound-of`) to the declared return; if that is an `OvError`
  bounded `IntegerTy`, it elides when `value-fits-field def.body`
  proves the body result in range (the emit prover's interval
  elision, CDX4010 — the exact static/dynamic correspondence a bounded
  field store has), else emits `emit-range-trap` on `reg-rax`.
- **`emit-range-trap`** is `emit-error-bound-trap` against `hi` (trap
  if greater) then `lo` (trap if less) — the same two conditional
  traps `emit-error-checked-store` uses, minus the store: a parameter
  or return value stays a full register, so only its RANGE is checked,
  not a storage width. This is deliberately more complete than the
  field path, which skips the check for width >= 8 (an 8-byte slot
  holds the value losslessly but a narrow *bound* in a wide slot still
  needs a range check); the boundary check has no width exemption.

Stage B does not yet propagate bounds into the prover — that is
Stage C, sound now that the guards exist.

**Deferred within Stage B (sound-preserving gaps, closed in Stage C or later):**

- **Precondition guard.** For each bounded parameter whose argument the
  caller could not statically discharge, emit a bounds guard. Placement
  decision, argued below.
- **Postcondition guard.** For each bounded return the body could not
  discharge, emit a bounds guard on the result register before RET,
  reusing `emit-error-bound-trap`.
- **Propagation, now sound.** With the guards in place, record a
  bounded parameter's range into `local-ranges` at `bind-def-params`,
  and give a bounded-return call expression its return range in
  `aexpr-proven-range`. This discharges the ~24 internal
  parameter-pass-through warnings and strengthens the prover globally.

**Guard placement — call-site vs callee-entry.** Two sound options:

- *Callee-entry:* one guard per bounded parameter in the prologue.
  DRY (independent of call-site count); blame is "argument to f out of
  range". Simpler; always pays one check per bounded param even when
  every caller could prove it.
- *Call-site:* guard the argument register before each call, elided
  when that call's argument is statically discharged. Zero-cost in the
  provable case (uniform with the field discipline, which elides
  per-store); precise per-call blame; costs code size at unprovable
  calls.

The call-site placement is the principled choice — it makes the
parameter boundary literally the same elide-or-guard operation as a
field store, and it is zero-cost exactly when the refinement is proven,
honouring "correctness over performance, but zero-cost when provable."
Callee-entry is the pragmatic fallback if call-site emission proves
invasive in the register-argument path. The design commits to
call-site; the implementation may land callee-entry first as a sound
intermediate and migrate, since both satisfy the metatheory.

Gate: codegen change, so verify one-pass vs two-pass per the seed
rebuild rules; full battery; runtime tests that a violating argument
and a violating return each TRAP (`.fatal` sidecars), and that in-range
values pass unguarded where proven.

### Stage C — Propagation and adoption

**C1 (checker propagation + first adoption) — SHIPPED.** With the
boundary now runtime-enforced (Stage B), a bounded parameter's range
is a sound fact inside the body (Section 4 gate satisfied):
`bind-def-params` records it into `local-ranges`, so the narrowing
lint proves a store of the parameter into an equal-or-wider bounded
field (CDX2051 -> CDX2053). First adoption: `mk-cdx` (the diagnostics
registry constructor) declares its real domains — `code 0..65535`,
`severity 0..3`, `phase 0..15`. It is called only by the registry,
whose entries pass literal `cdx-*`/`sev-*`/`phase-*` constants that
prove at the call boundary, so no caller cascade; the internal stores
`code = code` / `severity = sev` / `phase = phase` prove through the
propagated parameter range. Checker propagation is codegen-inert, so
the build stays one-pass.

**Scratch-reset (leak fixed in C1).** `local-ranges` is per-function
scratch, exactly like `locals`; both must reset at each definition
boundary. `check-all-defs` reset `locals` per def but not
`local-ranges`, so a bounded parameter's range (or a slice-3
let-local's) leaked into the next definition that reused the name — a
false CDX2053 (e.g. `make-level (n) = n` proving `n` in `0..15`
because the preceding `clamp-level` bound its own `n`). The per-def
reset now clears `local-ranges` alongside `locals`. This also closes
the same latent leak in the slice-3 local-flow analysis, which the
battery never exercised.

**Dedup (Stage A defect fixed in C1).** A constructor is a function
whose payload is a parameter, so Stage A's `lint-param-narrowing`
fired on constructor applications that `lint-ctor-narrowing`
(FabledTreasureMap entry 11) already covered — a duplicate CDX2051 at
every bounded-payload constructor site (e.g. `TypeVar (...)`). The two
are the same operation; they are merged into one `lint-arg-narrowing`
that names a constructor field or a bounded parameter by the head,
checking each site once.

**Cascade caution.** Bounding a function whose callers pass an
unproven (non-constant) value surfaces a CDX2051 at those call sites
unless they are bounded too. `make-diagnostic` is reached through
`make-error`/`make-warning`/`make-info` across the whole compiler and
would cascade; it is deferred until the chain is bounded together.
Adopt domains inward-out from self-contained constructors.

**C2+ — remaining families.** The register params (`0..31`, the
largest bucket), the length/position params (`0..2^32-1`), the
remaining diagnostic chain. The inliner exclusion (Stage B) means a
bounded hot signature will not inline — watch self-compile timing when
adopting a hot family; a measurable cost motivates the deferred
call-site precondition elision (Section 7 Stage B), which restores
zero-cost at proven call sites.

**C2 length/position family — SHIPPED (checker CL 6682, adoption CL
6691, seed 6693).** Two gated CLs, as built:

- *Checker prover reach (CL 6682, one-pass, checker-only).*
  `aexpr-proven-range` gained an `ABinaryExpr` arm — interval
  arithmetic over proven operand ranges (`binary-proven-range` +
  `range-arith-add/sub/mul/div`), the checker-side twin of the emit
  prover's `proven-*-range` with the same non-negative and
  i64-overflow guards. `builtin-return-range` gained `__heap-save`
  (structural: a heap address below 4 GB, the `__deck-pos` contract).
  Test `arith-narrow-proven` pins all four arms in both directions.
  Selfhost CDX2051 42 -> 39.

- *Adoption (CL 6691, one-pass — a source-level change; the stage-B
  seed already emits the guards).* Nineteen signatures declare their
  domains: the span chain (make-position line `0..131071`,
  column/offset `0..2^32-1`; make-span/span-at/tokenize/tokenize-into
  /make-lex-state/make-lex-state-prose file-id `0..65535`; make-token
  length; scan-to-eol-end offset + return; validate-escapes-into/loop
  positions), skip-newlines-pos (pos + return), codegen-carry-forward
  (three lengths), init-emit-workspace (both caps), cumsum-widths and
  accumulate-offset-width-sort (`0..65535` layout sums), assign-
  effect-op-addrs (index `0..65535`), pitch (return), and
  gen-unique-name-loop (counter). Five stores where a bound cannot
  flow took the `__narrow` assertion idiom: make-i32-patch pos
  (accumulator-list positions — no list-element bounds), emit-isr-
  stubs first-stub-vaddr (const + code-len statically exceeds the
  interval), build-x86-arities arity (list-length into `0..255`),
  emit-record byte-size (list-length * 8), install-new-node max-level
  (an if-over-field-read the checker walk cannot prove). Selfhost
  CDX2051 39 -> 16; battery 255/240/0/15; gate timing within
  run-to-run noise (all phases, including the unguarded seed-run
  phase, inflated uniformly under host load).

*Key flow facts learned (save future cascade analysis):*
`infer-arithmetic` types a binary expression with its LEFT operand's
type, so `bounded + anything` carries the bound and passes the lint
silently (the runtime guard still enforces); a let-local carries its
value's inferred type, so field reads and bounded-return calls flow
through locals for free; the aexpr walk (not the type path) is only
needed when the LEFT operand is a plain constant or literal. A
bounded param with a tuple return (`gen-unique-name-loop`) parses
fine. Residual 16: 9 register/slot, 3 diagnostics chain, 4 TypeVar
payload (from `tvar-map-lookup`, whose `-1`-on-miss return defeats a
return bound — those sites are structurally non-miss and will need
`__narrow` or a miss-free lookup shape).

**Endgame.** With the boundaries enforced and propagated and the
residual CDX2051 count driven to zero, promote CDX2051 from warning to
error — the campaign endgame — so a future unbounded flow into a
bounded position fails the build rather than warning.

**C2 families 2-3 + endgame — SHIPPED (CLs 6711, 6721, 6732, 6745,
6747; seed 6748).** As built:

- *Register/slot family (CL 6711).* The emitter's location encoding
  is unified (below spill-base = 32: register ordinal; at or above:
  spill slot), so the truthful location domain is the 0..65535
  already declared on slot/ptr-loc fields. Eight stores draw from
  list-at register pools, spill arithmetic, or reads of the wider
  EmitResult.reg — statically unprovable without tightening
  EmitResult.reg itself, which changes record layout and cascades
  across every construction site. Those took `__narrow`;
  load-local-scratch-for declared a provable 0..15 return (constant
  if-tree, prover-unioned). The EmitResult.reg-as-Location remodel
  is the principled root fix, deferred as follow-up.
- *Diagnostics chain + TypeVar payloads (CL 6721).* Twelve
  signatures bounded together, inward-out: make-diagnostic (severity
  0..4, code 0..65535), the make-error/warning/info/error-related
  wrappers, and the seven forwarders a caller survey found
  (st-add-error/info/warning, add-unify-error, add-lex-error,
  expect-coded, check-prose-type-name). Every external caller passes
  a cdx-*/sev-* constant, so the chain closed with no cascade. The
  four TypeVar/ForAllTy payload stores took `__narrow`
  (tvar-map-lookup returns -1 on miss, so a return bound would be
  dishonest; the sites are structurally non-miss).
- *TEXT-emitter bug (CL 6732) — found by the promotion gate.*
  emit-type printed every IntegerTy as bare "Integer", so bounded
  SIGNATURES lost their bounds in TEXT emission: stage1 compiled
  unbounded, silently shedding the stage-B guards from the text
  round-trip. The CDX chain never saw it (compiles source directly);
  as a warning the loss was masked. emit-integer-ty now prints the
  bound and mode; full-range prints bare Integer so unbounded
  signatures round-trip byte-identically. Record fields were never
  affected (they print from the syntax-level ABoundedIntType arm).
- *Promotion + library sweep (CL 6745) + verify tail (CL 6747).*
  lint-narrowing-prove mints make-error; the registry entry is
  sev-error with prove-or-__narrow guidance. The battery exposed the
  real blast radius — 29 tests compiling foreword/kernel chapters
  with their own unproven narrowings — swept with ~70 `__narrow`
  assertions across 21 files (UI constructors, color math, sprite
  animation, envelope, DateTime, OTA, TCP/transport, PCI/USB/xHCI
  descriptor parsers, CdxBinary proof decoding). Zero behavior
  change at every site: the store's runtime narrow-check already
  existed. `__narrow`-at-store was chosen over bounding constructor
  params because param bounds would cascade the new error into every
  app call site; per-chapter constructor param contracts are the
  follow-up. col-clamp8 gained a provable bounded return (its body
  is a top-level `__narrow` of the clamp — note a bounded return
  needs the `__narrow` at the BODY top level; under a let-chain the
  suppression does not reach it). Six deliberate-warning tests kept
  their proven halves; the unproven members moved to
  errors/narrowing-unproven (.failing 2051), one definition per
  retired shape.

Emit-side propagation (making `ir-expr-proven-range` trust a bounded
parameter to elide the DOWNSTREAM field runtime check, sound behind
the entry guard) is a separate zero-cost optimization, deferred; it is
not needed to clear a warning, only to remove a redundant runtime
check.

**Post-campaign follow-ups — SHIPPED (CLs 6783, 6787, 6790; seed
6791).** As built:

- *Two more prover doors (CL 6783).* `apply-proven-range` consults
  the head's declared bounded return after the builtin facts miss —
  the deferred propagation half of Section 7, sound because the
  stage-B postcondition guard enforces the declared return at every
  call. `record-local-range` falls back to the value's resolved
  bounded IntegerTy when the expression analysis cannot prove, making
  local-mediated flows exactly as permissive as direct ones and
  letting locals join the arithmetic arms. `text-length` joined the
  structural builtin facts. A field-access arm was considered and
  REJECTED: the AST does not carry receiver types, and re-inferring
  inside the lint would mutate unification state — field-heavy
  proving stays with the emit prover, whose IR carries types.
- *EmitResult.reg declares the location domain (CL 6787).* 0..65535,
  matching LocalBinding.slot / FieldLocal.slot / BivyAlloc.ptr-loc.
  The predicted cascade measured at zero: ~140 of the ~166
  constructions are `.reg` field reads that type-fit, the register
  constants and constant-ifs prove via const-ranges, and the
  allocator's list-at/spill-arithmetic sites already carried their
  family-2 `__narrow`. Two narrows came off (emit-bivy-alloc
  ptr-loc, emit-eval-record-fields slot). Field storage narrowed
  4 -> 2 bytes; internal and name-addressed.
- *Constructor contract pilot (CL 6790).* `adsr-new` declares
  `sustain 0..1000`; the store's `__narrow` came off (the declared
  param type carries it) and every caller proves. The campaign rule
  for the remaining sweep sites: convert a constructor only when its
  ENTIRE caller set — apps included — proves or is swept in the same
  CL. The 30-site app survey shows constructors like compositor-new
  receive computed values; those chapters take their app sweep
  together with the contract, per chapter, as an on-demand stream.

## 8. Diagnostics

Reuse the field codes for uniformity; no new codes needed at the
boundary:

- CDX2050 NarrowingRecordSetLiteral (error) — a literal argument/result
  provably outside the bounded parameter/return.
- CDX2051 NarrowingRecordSet (warning) — a wider unprovable value at a
  bounded parameter/return; a runtime guard will enforce it (Stage B).
- CDX2053 NarrowingProven (info) — the argument/result range proves
  within the bound; guard elided.
- CDX4010 BoundsProven (info, emit side) — the runtime guard was elided
  in codegen.

The message wording is generalized from "field" to "bounded position"
so parameter and return sites read correctly.

## 9. Testing

- Static (`.diag`): provable argument -> CDX2053; wider argument ->
  CDX2051; out-of-range literal argument -> CDX2050; the return-position
  duals; `__narrow` suppression; a bounded-return call feeding a
  downstream bounded store that then proves (Stage B).
- Runtime (`.expected` + `.fatal`): in-range argument/return run to the
  expected value; out-of-range argument traps; out-of-range return
  traps; a provably-in-range flow runs with no guard (inspected in the
  emitted code, and unchanged output).
- Regression: the full battery and the byte-exact fixed point at every
  stage; the reach-campaign tests (const/builtin/local-narrow-proven)
  continue to pass; the ~24 internal warnings measurably fall once
  Stage C declares the domains.

## 10. Literature

- Freeman & Pfenning, "Refinement types for ML", PLDI 1991 — subset
  types over a base language.
- Xi & Pfenning, "Dependent types in practical programming", POPL 1999
  — integer index refinements with decidable constraint checking; our
  interval bounds are a restricted, SMT-free case.
- Rondon, Kawaguchi, Jhala, "Liquid Types", PLDI 2008 — inference of
  refinements; we propagate/check intervals rather than infer arbitrary
  predicates, trading completeness for decidability and speed.
- Flanagan, "Hybrid Type Checking", POPL 2006 — static where provable,
  dynamic where not; the exact enforcement model here.
- Findler & Felleisen, "Contracts for higher-order functions", ICFP
  2002; Wadler & Findler, "Well-typed programs can't be blamed", ESOP
  2009 — the precondition/postcondition obligation split and blame.
- Cousot & Cousot, "Abstract interpretation", POPL 1977 — the interval
  domain and the soundness frame for `aexpr-proven-range`.
- Meyer, "Object-Oriented Software Construction" (Design by Contract) —
  require/ensure as precondition/postcondition; we elide the assertion
  when statically discharged.
