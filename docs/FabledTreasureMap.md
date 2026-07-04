# Fabled Treasure Map

Deferred wins discovered in passing — things found while digging for
something else that were too big, too separate, or too early to grab
on the spot. Each entry records where the treasure is buried, why it
is treasure, and roughly how big the dig is. When one is claimed,
move it to the Claimed section with its CL rather than deleting it —
the map is also a record of instincts that paid off.

Entry format: what / where found / the win / the dig / pointer.

**This map is fully dug up** (every entry claimed or debunked as of
2026-07-03). The successor is `docs/QuartermastersMap.md` — drawn
deliberately for delegation rather than filled with finds-in-passing.
New deferred finds still land here; new scoped, delegatable work goes
on the Quartermaster's Map.

---

## Buried

### 1. Ctor field packing by bounds

**What:** Sum-constructor fields are laid out as flat 8-byte slots
regardless of their declared bounds — `emit-sum-ctor` computes
`total-size = (1 + field-count) * 8` (`X86_64Compound.codex`).
Record fields already narrow-store by bounds (1/2/4/8 bytes); variants
never got the same treatment.

**Found:** 2026-07-02, while bounding type-variable ids to 32 bits
(CL 6516) — the payload bound turned out to be contract-only because
the slots don't shrink.

**The win:** Every boxed variant node in every phase shrinks. The
CHECK deck (~69 MB on the selfhost, survey S × 400) is dominated by
CodexType nodes — TypeVar drops 16→12, bounded-field-heavy variants
more. IR nodes, tokens-as-variants, and every user program's sum
values benefit identically. This is a deck-survey-multiplier-scale
win, bought once in codegen.

**The dig:** Medium codegen project. Field offsets become
width-cumulative instead of `i * 8` in: `emit-sum-ctor` /
`emit-store-ctor-fields` (construction), pattern-match field
extraction (offset computation at `is Ctor (a) (b)` sites),
`emit-const-codextype` (const-boxed variants), and the pointer-map /
escape-check field walkers. Alignment rules per Sketchbook. Gates:
the fixed point catches every mistake loudly.

**Scoped 2026-07-02 (fester), execute-ready plan:**
- Records already do this via a CCE-*sorted* layout: `build-cce-byte-offsets`
  + `cce-byte-offset-and-type` + `field-byte-width` (X86_64Compound
  ~1049/1458). Variant ctor fields are *positional* (no names), so the
  analog is simpler -- no sort: `offset(i) = 8 + sum(field-byte-width
  tys[j] for j<i)`, tag stays 8 bytes at offset 0. Add
  `sum-ctor-field-offset : List CodexType, Integer -> Integer` and
  `sum-ctor-total-size`, matching `cumsum-widths` alignment.
- The declared ctor field types ARE reachable at construction:
  `emit-apply` (X86_64Compound:157-161) resolves `SumTy sname args ctors`
  and computes `tag`; pass `(list-at ctors tag).fields` into
  `emit-sum-ctor` -> `emit-store-ctor-fields`, which currently only gets
  value locals. Narrow-store each field with `emit-narrow-store-proven`.
- Reader sites all carry the ctor field types already; they hardcode
  `8 + i * 8`. Two classes: (a) POINTER-only walkers read 8 bytes and only
  need the offset swapped -- `pmap-walk-ctor` (X86_64Compound:1128) and the
  escape walker; (b) VALUE readers must become WIDTH-aware (mirror record
  field load w/ sign-extend): pattern extraction (`is Ctor (a) (b)`),
  `emit-sum-fields-eq` (X86_64:2290), `emit-opening-print-sum-fields`
  (X86_64Chapter:264), and `emit-const-codextype` (rodata layout).
- Atomic, all-or-nothing: any offset/width/sign mismatch between a
  writer and a reader silently corrupts. Variants saturate the compiler
  (AST/IR/types), so the self-host fixed point + FULL battery (not just
  BVT) must both be green. Do this as a dedicated focused effort, not
  batched.

**CLAIMED CL 6541 (fester).** Implemented exactly per the plan: shared
sum-ctor-field-offset helper (widest-first, like records) at all sites
(construction, pattern extraction, eq, opening-print, pmap/escape),
const-box deferred for -EscapeCheck (documented in source). Two-pass
convergence to a one-pass hard fixed point; full battery 231/0/13;
self-verify green. CAVEAT: the headline win did NOT materialize. Total
size rounds up to 8 to preserve heap 8-alignment (cross-arch), so a
single-small-field variant like TypeVar (tag 8 + id 4) still rounds to
16, unchanged -- only variants with 2+ sub-8-byte fields shrink an
8-byte unit, and bounded fields cluster in RECORDS not variants, so the
aggregate heap win is small (and unmeasurable via measure-survey, which
overflows in TEXT mode on the full self-source for both old and new
seed). Shipped as the correct/uniform layout (variants pack like
records), win a bonus. To actually get TypeVar 16->12 one would drop the
round-up-to-8 and confirm unaligned heap objects are safe on x86 AND
ARM64 (records round today for an unconfirmed reason) -- a separate,
riskier follow-up.

### 2. CDX2051 silent-truncation class (96 remaining)

**What:** 96 warnings per build where a plain i64 value is stored
into a bounded record field that codegen will silently truncate:
`code` (0..65535), `severity` (0..3/0..4), `phase` (0..15) in the
diagnostics records; `bivy-origin`/`deck-origin` in PhaseAllocator;
`pos` in SkipListText.

**Found:** 2026-07-02, clearing the `var-id` members of the same
class (CL 6516 — zero var-id warnings remain).

**The win:** Silent truncation is a live corruption class — a heap
position past 4 GB in `bivy-origin` would wrap silently. CL 6516
shows the pattern: bound the value at its *source* so it flows
bounded end-to-end, rather than `__narrow` at every store. Endgame:
promote CDX2051 warning → error once the count is zero, the same
promotion CDX9002 got.

**The dig:** Mechanical per field family; each is small. Diagnostics
codes first (source: CdxCodes constants — already statically in
range, just typed wide).

**Progress 2026-07-02 (fester):** The live-corruption member landed
(CL 6527): `bivy-origin`/`deck-origin`/`bivy-hwm`/`deck-end` in
PhaseStart/PhaseMetrics were `Integer between 0 and 4294967295` holding
heap addresses; widened to plain Integer (addresses are pointer-width),
killing the latent 4 GB truncation and 4 warnings (96 -> 92). Remaining
92 by field (self-compile): reg 18, b0-b3 28 (7 each), pos 8, data-len 4,
code-len 4, code 3, severity 3, file-id 3, plus singles.

**RULED (Damian, 2026-07-02): the widening approach is WRONG and the
guidance above and below is superseded.** The bounds on these fields
are load-bearing twice over -- they are documentation of the value's
domain and they are automatic bounds checking at every store. A
CDX2051 here indicates a COMPILER FEATURE NOT YET BUILT, not
problematic client code: the static bounds prover cannot yet see
through the flows (function returns, list-at, arithmetic on bounded
operands, constants declared wide) that would let it prove the store
safe and elide the warning, CDX4010-style. Removing the bounds threw
away the contract to silence the messenger. The widening CLs
(6527 PhaseAllocator addresses, 6562/6565 emit-layer ~40 fields,
6566/6567 PatchEntry i32 refactor insofar as it existed to delete
byte bounds) took the wrong direction; the bounds should be RESTORED
and the campaign redirected at the compiler:

- **Prover reach:** teach the static bounds prover the missing
  patterns so provably-safe stores stop warning (bounded constants
  in CdxCodes are the trivial first case -- `code = 2031` into
  0..65535 should prove, today it warns because the CONSTANT is
  declared wide).
- **Source bounding where the domain is real** (reg 0..15 from the
  allocator rotation, sev-*/phase-* constants).
- **List element bounds / covariance** for the b0-b3 byte family --
  a language feature, the real blocker fester correctly identified.
- **Address fields**: 0..2^32-1 on bivy-origin et al. was CORRECT
  documentation for the 3 GB-RAM design era; if RAM outgrows it, the
  bound is raised deliberately, not deleted.

Endgame unchanged (promote CDX2051 to error at zero count) but the
zero comes from the compiler getting smarter, not from the contracts
getting weaker.

**Restoration status (2026-07-02, fester):** the widening is undone.
- 6562 (emit-layer ~40 fields) — reverted by CL 6573; bounds restored.
- 6527 (PhaseAllocator `bivy-origin`/`deck-origin`/`bivy-hwm`/`deck-end`)
  — reverted this session; `Integer between 0 and 4294967295` restored on
  PhaseStart/PhaseMetrics. One-pass hard fixed point, self-verify green,
  full battery clean. Restored on the fester stream; pending copy-up to
  main (6527 reached main via 6528).
- 6566 (PatchEntry `{ pos, value : Integer }`) — KEPT, per 6573: a patch
  IS a 32-bit little-endian write, so one `value` is the honest
  representation, not a bounds-dodge; `pos` stays bounded. This was never
  a widened bounded field, so nothing to restore.

The campaign is now redirected at the compiler (prover reach / source
bounding / list-element bounds), per the ruling above — no more deleting
contracts to silence the warning.

**Prover reach slice 1 landed (2026-07-02, blu, CLs 6611 + seed 6612):**
the narrowing lint gained an AST-level range analysis
(`aexpr-proven-range`, the checker-side twin of `ir-expr-proven-range`)
consulted only on the would-warn path: literals, literal-defined
constants (a `TypeEnv.const-ranges` side table filled at registration —
the registered TYPE is untouched, refining it would make distinct
constants disjoint under unification), if-union, and let-body. A proven
store reports info CDX2053 (NarrowingProven) instead of the warning.
Selfhost 66 -> 53; the 13 proven are the reg-rax/reg-rdx/sev-error
constant family. Test const-narrow-proven pins the shapes both ways.

**Slice 2 landed (2026-07-02, blu, CLs 6625 + seed 6626, main 6628):**
`aexpr-proven-range` gained an application arm — `__narrow x` passes
its argument's range through, and a head name resolving to a
structurally bounded builtin proves its return (`list-length`,
`__deck-pos` are 0..2^32-1, guarded by env-is-local). Selfhost
53 -> 48 (four `code-len/data-len = list-length` + `deck-origin =
__deck-pos`). Test builtin-narrow-proven.

**Slice 3 landed (2026-07-03, blu, CLs 6635 + seed 6636, main 6638):**
let-local range flow. TypeEnv gained a `local-ranges` side table;
infer-let-bindings records a local's proven range; the lint's name
arm consults it. Added `__buf-write-bytes` builtin fact. Selfhost
48 -> 45 (two `field = new-len` where `new-len = __buf-write-bytes` +
one bonus). Test local-narrow-proven.

**Lint-side fruit now exhausted (2026-07-03).** The remaining 45 are
dominated by ~24 parameter pass-through sites that need bounded
integers in function signatures (a parser gap) or interprocedural
join - NOT clearable lint-side. Field source-bounding is blocked on
parameter bounds (narrowing a field makes its own unbounded-param
stores fail). The next real step is the bounded-ints-in-signatures
parser feature, a decision for Damian. Full analysis and the
remaining-45 breakdown live in
`docs/Designs/Compiler/Active/BoundsProverReach.md`.

### 3. Audit ASCII char-code literals against CCE

**What:** `find-dot` compared `char-code` output against ASCII 46,
which is the letter 'H' in CCE — the dotted sub-effect lattice was
dead code from birth (fixed, CL 6509). That was one site, found by a
probe. The *class* — comparing `char-code`/`char-code-at` results
against integer literals that are ASCII codes — may have other
members. In CCE: newline is 1, space is 2, digits start at 3,
letters at 13; almost no ASCII code means what it looks like.

**Found:** 2026-07-02, EffectRows stage 0.

**The win:** Each hit is a silently-dead or silently-wrong branch in
internal text handling. The fix idiom is established:
`char-code (char-at "." 0)` — self-describing, table-independent.

**The dig:** Grep `char-code.*== \d` and `== \d+.*char-code` across
`codex/` (excluding I/O-boundary code that legitimately works on
Unicode bytes), eyeball each site. An afternoon.

**CLAIMED CL 6524 (fester -> main CL 6528).** Swept the compiler: the
ONLY real hits were in `int-lit-out-of-range` (TypeCheckerInference)
comparing against ASCII `48` ('0') and `95` ('_'), so leading zeros and
grouping underscores were never skipped -> a valid grouped literal was
falsely rejected CDX2071. Every other `char-code` site already uses
`char-code 'x'` char-literals or `char-code-at "x" 0` (self-describing,
table-independent) -- correct. Fixing the checker EXPOSED a latent
second bug: `lit-text-to-integer`'s decimal path called the
`text-to-integer` builtin which never stripped underscores (hex did), so
grouped decimals produced corrupted VALUES. Added `dec-text-value`.
Shipped together (checker fix alone would turn a clean reject into
silent corruption). +positive test int-literal-underscore.

### 4. TEXT printer drops effect scopes

**What:** `emit-type`'s EffectfulTy arm renders `[Console] T` and
never prints the scope list — a scoped effect `[Console "auth"]`
loses its scope through TEXT emission. The AST-path renderer
(`emit-type-expr`) has the same gap.

**Found:** 2026-07-02, while teaching the printer to render row
tails (CL 6513).

**The win:** Removes a latent pingpong break waiting for the first
scoped effect in compiler source, and a real information loss for
any TEXT-mode consumer today.

**The dig:** Small — mirror the effs rendering with the parallel
scopes list in both printers; add a round-trip test with a scoped
effect.

**Status 2026-07-02 (fester): UNREACHABLE today, deferred.** Grep of the
whole depot (compiler + foreword + os + apps + tests) found zero scoped
effects `[Name "scope"]` -- every `[X "..."]` hit is a list-of-ctor
literal, not an effect scope. So nothing currently loses a scope: no
pingpong break, no live TEXT consumer. The gap is real (printer
totality) but speculative -- changing the fixed-point-critical TEXT
emitter for a case nothing produces, with no battery regression path
(there is no TEXT-round-trip sidecar), is a Less-Is-More / no-premature
call. Do #4 + #5 together WITH a dedicated round-trip probe harness if/
when a scoped effect actually appears.

### 5. Effectful-arrow-returning types render unparenthesized

**What:** `A -> [Console] (B -> C)` prints via `emit-fun-params` →
`emit-type` on the inner FunTy without parentheses, producing text
that re-parses as a different (chained/comma) type or errors. No
depot code hits it; discovered by reading, not by a failure.

**Found:** 2026-07-02, while designing `emit-row-result` (CL 6513).

**The win:** Printer totality — every legal type should round-trip.

**The dig:** Small — parenthesize function-typed results after an
effect bracket in `emit-row-result` / the EffectfulTy arm; add the
round-trip test.

**Status 2026-07-02 (fester): NON-BUG in result position; won't fix.**
Traced the parser: `parse-effect-type` does `ret = parse-type st-effs`,
so an effect bracket greedily grabs the ENTIRE following type. Emitting
`A -> [Console] B -> C` re-parses to exactly `A -> [Console] (B -> C)` --
it round-trips. The only position where an un-parenthesized `[E] (B->C)`
would mis-parse is as a function *parameter* (`([E](B->C)) -> D`), but a
bare `[E] T` value type is not expressible as a first-class parameter in
Codex (effects live on arrows, not on values), so that case is
unreachable. Net: nothing to fix in `emit-row-result`. If parameter-
position effect types ever become expressible, the fix is in
`wrap-fun-param` / `wrap-complex` (which parenthesize FunTy but not
EffectfulTy), not the result arm.

### 6. `map` builtin is type-level only

**What:** `TypeEnv.codex` binds `map` with a full polymorphic
signature, but there is no runtime implementation — using it
compiles through the checker and dies at codegen with CDX2040
"Unresolved call to 'map'". Users get a late, confusing error for
what is effectively an unknown name; `list-map` (foreword) is the
real function.

**Found:** 2026-07-02, EffectRows stage 0 (the laundering probe
originally targeted `map` and hit the codegen wall).

**The win:** Either implement it (alias to list-map at lowering) or
delete the binding so misuse fails at name resolution with a hint.
Small UX win, removes a trap.

**The dig:** Tiny either way. Deleting also slims the stage-2 HOF
retyping list.

**CLAIMED CL 6525 (fester -> main CL 6528).** Deleted the binding
(TypeEnv, rewired the env chain past it) + the "map" entry in the
NameResolver builtin list. Misuse now fails early at name resolution
with CDX3002 "Undefined name: map" instead of the late CDX2040. No
compiler/foreword code used it. +negative test errors/map-undefined.

### 7. Effect-system holes and mechanisms (owned elsewhere)

The open laundering routes (list-map, record field, lazy, handler
clauses, fork), the effect-le shared-substitution subtlety that
stage 3's row-le must not re-trip, and the staged fix plan are owned
by `docs/Designs/Compiler/Active/EffectRows.md` (§13 stage-0
results) with executable probes in `codex/test/`. Listed here only
so the map is complete; dig coordinates live there.

### 8. Type variables under EffectfulTy never parameterize

**What:** `parameterize-walk-children` has no `EffectfulTy` arm, so a
lowercase type name under an effectful VALUE type — a signature like
`peek : [State] a` — falls through `otherwise` unchanged: the `a`
stays a `ConstructedTy` instead of becoming a bound `TypeVar`. Arrow
signatures are unaffected (the row rides the FunTy and the inner
walk recurses param/ret); only the effectful-value form loses its
polymorphism. No depot signature hits it today (effectful values are
all concrete: `read-line`, `current-dir`), which is why it has never
fired.

**Found:** 2026-07-02, EffectRows stage 2, while threading row
entries through the parameterize walk.

**The win:** Removes a silent-monomorphization trap that will
otherwise fire the first time someone writes a polymorphic effectful
value binding, three pipeline stages from the cause.

**The dig:** Tiny — add `is EffectfulTy (effs) (sc) (ret)` to
`parameterize-walk-children` recursing into `ret`, plus a test with
a polymorphic effectful value signature.

### 9. TEXT printer drops parens around for-expressions in operands

**What:** `(for l in xs -> l.name) & rest` emits as
`for l in xs -> l.name & rest` — the parentheses vanish and the
re-parse absorbs `& rest` into the for-body, changing the program
(caught as CDX2001 in stage2 of the pingpong). The expression
printer parenthesizes lambdas and applications where precedence
demands but has no arm for for-expressions in binary-operand
position.

**Found:** 2026-07-02, EffectRows stage 3b — collect-effect-names
originally used the pattern; the pingpong's stage2 caught the
mis-parse. Sidestepped with an explicit accumulator loop.

**The win:** Printer totality for `for` in operand position; today
the pattern silently round-trips to a different program until a
type error surfaces it.

**The dig:** Small — wrap for-expressions in the printer's operand
positions (mirror the lambda arm), plus a round-trip test with
`(for ...) & suffix`.

### 10. Const-boxed FunTy descriptor still two words

**What:** `emit-const-codextype` boxes `FunTy` as tag 8 with two
zeroed words (`X86_64Compound.codex`) — the shape of the two-field
pre-row arrow. Stage 1a made FunTy three fields. Nothing observably
breaks, which strongly suggests these const type descriptors are
never pattern-matched as CodexType values at runtime (their tag
numbers do not match constructor ordinals either — RealTy prints
tag 1 but sits at a different decl position). Either the consts are
consumed by something with its own tag table, or they are dead
weight.

**Found:** 2026-07-02, EffectRows stage 2, while auditing CodexType
serialization for the ForAllEff addition.

**The win:** Establish ground truth: find the consumer of the
const-boxed TypeBinding descriptors or delete the emission. If a
consumer exists, the FunTy box needs its third word and ForAllEff
may need a real tag; if none, this is dead code hiding a future
corruption.

**The dig:** Small investigation — trace `emit-const-typebinding`'s
output symbol to its readers; then either a two-line shape fix or a
deletion.

**Progress 2026-07-02 (from fester's #1 packing, CL 6541):** consumer
identified — the const boxes are read only under `-EscapeCheck`,
which is gated off by default; fester left const-box at the old
layout with a documented follow-up in source. So the two-word FunTy
box is dead in normal builds; the dig narrows to "fix the shape when
touching the escape-check path, or fold it into that follow-up".

**VERDICT 2026-07-02 (fester): NIT — deferred to escape-check revival,
no standalone fix.** Traced the reader (`X86_64Compound.codex`
~1209-1221 + the in-source NOTE at ~1271-1281). The FunTy 2-vs-3-word
box cannot bite: (1) normal builds never read the const boxes
(`-EscapeCheck` off — fixed point and battery never touch them); (2)
even under `-EscapeCheck`, `pmap-walk`/`is-pointer-type` read only a
descriptor's TAG and the pointer slots they recurse on (ListTy.elem,
RecordTy.fields, SumTy.ctors, ConstructedTy.name) — FunTy is not a
recursion target, so its payload words are never indexed regardless of
count; (3) navigation is pointer-based (absolute vaddrs to children),
not size-based, so a wrong word count can't misalign siblings. The
map's "dead code hiding a future corruption" overstates it for FunTy.
The one genuine latent item here is the SEPARATE variant-packing
mismatch (const-box lays variant fields flat `8 + i*8` while runtime
packs by width), which is already documented in the source NOTE and
only matters if `-EscapeCheck` becomes a supported path — at which
point the whole const-box serializer gets a width-packing pass and
FunTy's third word falls out of it. Touching the fixed-point-critical
emitter to add one inert zero word in isolation is no-premature churn
(cf. #4/#5). Trigger to revisit: escape-check revival.

### 11. Variant construction has no narrowing lint (new exposure from #1)

**What:** A plain Integer flowing into a bounded ctor field unifies
silently (IntegerTy unification is overlap-permissive), and there is
no variant-construction analog of the record lints (CDX2050 literal
out of range, CDX2051 wider value). Before packing this was a
contract violation with no value change — the flat 8-byte slot
stored and reloaded the full value. After #1 (CL 6541),
emit-store-ctor-fields narrow-stores at the declared width, so an
out-of-range value is silently truncated at construction
(`Byte-ish 300` becomes 44) with no diagnostic anywhere on the path.

**Found:** 2026-07-02, blu reviewing fester's packing CL during
merge-down. Battery green means nothing hits it today.

**The win:** Close the silent-truncation surface that packing
opened: the same tripwire the record paths already have, at variant
construction (and `when` literal patterns against bounded fields,
which share the width assumption).

**The dig:** Small checker change — mirror lint-narrowing-check
where ctor application unifies argument types against declared
field types (infer-application on a ctor head, or bind-ctor-...
construction path); reuse CDX2050/2051. One positive + one negative
test. Belongs with the #2 campaign (same endgame: promote to error).

---

## Claimed

- **#3 Audit ASCII char-code literals vs CCE** -- CL 6524 (main 6528).
  One real site (int-lit overflow checker); exposed + fixed a latent
  decimal-underscore value corruption. Details in entry #3 above.
- **#6 `map` builtin type-level only** -- CL 6525 (main 6528). Binding
  deleted; misuse now CDX3002 at name resolution. Details in #6 above.
- **#2 (partial) phase-allocator address fields** -- CL 6527 (main 6528),
  REVERTED 2026-07-02 (fester). Widening the PhaseStart/PhaseMetrics
  `bivy-origin`/`deck-origin`/`bivy-hwm`/`deck-end` fields to plain Integer
  was ruled the wrong trade (see entry #2): the `Integer between 0 and
  4294967295` bounds are load-bearing documentation + a store-time check,
  and the CDX2051 they raise is a bounds-prover gap, not bad code. Bounds
  restored on the fester stream (pending copy-up to main). The rest of #2
  is now a compiler-side campaign, not more widening -- see entry #2.
- **#1 Ctor field packing by bounds** -- CL 6541 (fester). Variant fields
  pack by width like records; one-pass fixed point + full battery green.
  Headline win negated by 8-alignment rounding (see entry #1). Correct/
  uniform layout shipped; larger win needs dropping the rounding.
- **#8 Type variables under EffectfulTy never parameterize** -- blu
  CL 6581. parameterize-walk-children gained the EffectfulTy arm;
  discriminating test effect-value-poly (old seed: CDX2001
  Integer-vs-Con:a; fixed: runs at two instantiations).
- **#9 TEXT printer drops parens around for-exprs in operands** -- blu
  CL 6581. wrap-binary-left/right parenthesize applications that print
  as for-sugar; collect-effect-names deliberately keeps the
  (for ...) & rest shape so the text fixed point re-proves it every
  build.
- **#11 Variant construction narrowing lint** -- blu CL 6586. Ctor-headed
  applications run the shared lint-narrowing-check against the declared
  arrow parameter: CDX2050 error on out-of-range literals, CDX2051
  warning on wider static types, __narrow suppresses. Probes:
  errors/ctor-literal-narrow + ctor-narrow-warn. Zero hits across
  compiler/foreword/tests -- the tripwire is armed, not tripped.

## Debunked / deferred

- **#5 Effectful-arrow result unparenthesized** -- NON-BUG (result
  position round-trips; parameter position is unexpressible). Won't fix.
  See entry #5.
- **#4 TEXT printer drops effect scopes** -- real but UNREACHABLE (no
  scoped effect exists in the depot); deferred as speculative emitter
  churn with no regression path. See entry #4.
