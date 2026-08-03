# Phase Architecture

**CLOSED 2026-07-28. The campaign is finished and this document is the
record of it, not a plan.** Every mechanism it specifies is built: the
deck/bivy split, per-phase build, phase-measure, phase-compact, the
emitter wall, the demand floors, the four reservation-copies, and the
escape invariant with all four typed roots walking. The last two items
anyone was waiting on -- precise escape roots for CHECK and LOWER, and
TCO-reset removal -- were both already done when they were re-read on
2026-07-28, and both were re-verified against source. `CurrentPlan`'s gap
for this work is deleted rather than kept as a closed entry.

**The copying compactor this architecture was meant to drive does not
need building.** `-PoisonCompact` on the selfhost is byte-identical to
the normal compile, and a compile that reads anything which died at a
compact cannot produce the same bytes. So the emitter's inputs have no
live dependency on reclaimed phase memory, and the reservation-copies
already are the compactor.

Four things are left and none of them is mechanism, which is why this is
in `Done/`:

1. **The `-EscapeCheck` walk allocates.** `accumulate-offset-width-sort`
   runs per field visit and is O(fields^2) in `text-compare` calls, about
   915 MB across the selfhost map, so that map needs `-Decks 200`. It is
   an instrument behind an off-by-default flag; normal compilation and
   both gates are unaffected.
2. **A coverage question.** Whether any root OTHER than the two the
   emitter receives still depends on a reclaimed deck is unmeasured.
   Poison-compact is the instrument for it.
3. **One item is declined, not open.** "Keep the DECK-LIVE map in an
   `-EscapeCheck` battery run" below predates Damian's standing ruling of
   2026-07-27 that harnesses are built but not added to the standard
   battery. Do not act on it without asking.
4. **One item is aspirational and always was.** Migration step 6,
   deprecating the unified bump heap for explicit per-region allocators,
   was never scheduled and the demand-paged arena removed most of its
   motivation.

Everything below is as it was written, including the measurements and the
refuted guesses, because the reasoning is the part worth keeping.

**The survey is deleted (2026-07-07).** Deck heights are no longer
estimated from source size by per-phase multipliers. Every phase now
reserves a fixed generous FLOOR over demand-paged address space -- the
`demand-*-floor` constants in `codex/compiler/Core/BuildSettings.codex`
(Demand Decks section). A floor costs address space, not memory:
physical pages commit on first touch. There is no survey pass, no
survey signature, no under-count cliff, and nothing left to tighten.

Everything else in this document survives unchanged: the deck/bivy
split, phase-measure, phase-compact, the escape invariant, poison
doctrine, and the reservation-copy pattern (which still earns its keep
by rewinding addresses so compaction re-uses already-committed pages
instead of touching fresh ones).

**Status:** Substantially implemented. The vocabulary, allocator
primitives, per-phase build, phase-measure, and phase-compact are in
place across all 8 frontend phases (CLs 500, 552, 632–645, 2135,
2169). ConstructedTy resolution and lambda lifting were extracted
from the x86 emitter into their own phases with independent compact
cycles (CLs 2135, 2148, 2157, 2169). The emitter wall is enforced
(CL 644). **Escape-invariant enforcement is live for the pinned
roots (2026-07-04, blu -- see "Escape invariant: instrument repair
and first real result" below): the pmap precise walker works, its
self-test runs on every compile, escape hits self-locate, and the
first real measurement found and fixed two genuine dangling deck
pointers (AChapter.rt-budgets / AChapter.conversions shallow-copied
by copy-as-chapter).**

**Remaining work, re-measured 2026-07-26 (reek). Both items below were
closed, one by work and one by discovering it had been done three months
earlier. What replaced them is an instrument-usability problem.**

1. ~~**Precise escape roots for CHECK and LOWER.**~~ **DONE** (CL 10527).
   The roots were already wired; what was missing was evidence, because a
   root absent from the self-type table answers zero exactly as a clean
   phase does. `precise-escape-bag` now reports a reach count over a range
   containing every pointer, which no working traversal can answer zero to.
   All four roots walk and all four report zero escapes.
2. ~~**TCO-reset removal.**~~ **ALREADY DONE, 2026-04-24, and this document
   asked for it for three months afterwards.** The reset went out in the
   rip-out that cleared the way for BS3 (`docs/PM/Active/Stories/VoodooChild.md`
   records it: "TCO heap-mark save gone. The r10-rewind on tail-call-self-jump
   gone"). Verified against the source rather than the story: `TcoState`
   (`X86_64State.codex`) has no heap field, the loop-top setup in both
   `emit-function-minimal` and `emit-function-standard` touches no heap
   register, and `emit-tail-call` ends in a bare `jmp rel32`. There is no
   within-phase compacting reset left in the emitter to remove.
3. **CL 10538 regressed the instrument, and that is now the critical path.**
   `emit-const-sumctor` wrote a two-word box for a three-field `SumCtor`, so
   every constructor in the self-type table looked nullary and the walk
   entered NO variants. Fixing it was right and is validated, but opening the
   variant path broke `-EscapeCheck` on inputs where it used to work, and the
   regression shipped:

   - **`db-full-test` crashes** under `-EscapeCheck` (`#GP` in `bag-concat`,
     on a garbage word shaped like `0x352AB0 << 32`, which varies per run).
     The pre-fix seed `B75664340F568DE7` runs the same command clean at reach
     125,280, so this is a regression by observation, whatever the mechanism.
   - **The selfhost exceeds `compile.ps1`'s 600 s VM wait** even at
     `-Decks 200`, where it completed before. The walk is cheap per node
     (arithmetic 1.3 s at reach 17,320; acpi-parse 0.6 s at 70,450), so this
     is volume: the DECK-LIVE map alone is fourteen full traversals of the
     same two roots against different ranges and the walker has no visited
     set, so shared subgraphs are re-walked.

   **Normal compilation is unaffected** -- `db-full-test` builds clean without
   the flag, and both gates passed twice on the shipped seed. `-EscapeCheck`
   is off by default and not in the gate, so the blast radius is the
   instrument only.

   **FIXED. The walk was allocating, and the fix was to stop.** Two guesses
   were refuted first and both are worth naming, because both were reasoned
   from the symptom instead of measured: it was not the walk dereferencing
   non-pointer constructor payloads (a `[heap-base, ram-size)` guard before
   every descend changed nothing and was reverted rather than shipped
   unfired), and it was not a `sum-ctor-field-offset` width misalignment.
   Both would fault inside the walk, and the fault was not inside the walk.

   Two readings settled it. First, the reported symbol was WRONG: RIP
   `0x1555CF` lies in `bag-concat-loop`, not in `bag-concat`, which ends at
   `0x1555BD` -- the harness attributes a fault to the preceding symbol, and
   this map has mislabelled a frame before. So the fault was a list walk on a
   bad element pointer, not bag construction. Second, printing R10 around each
   DECK-LIVE walk showed it climbing **375,156,211 to 551,780,147 on
   `db-full-test` -- 176 MB, across fourteen walks that are all quiet and
   should allocate nothing.**

   `pmap-walk-record` reached a field's offset through
   `cce-byte-offset-and-type`, which builds a FieldOffsetType record to carry
   an offset and a type the caller already holds. One record per POINTER FIELD
   VISITED, harmless while variants were leaves and 176 MB once they were not.
   The walks run inside `deck-record`, so it landed on the phase deck, overran
   the floor, and the following merge read the corruption as a diagnostic list.
   Computing the offset in place from the field already in hand takes
   `db-full-test` to 17 MB and the selfhost from a 600 s timeout to **127.6 s**.

   Residual, and NOT a regression: the walk still allocates (~915 MB across the
   selfhost map at `-Decks 200`), because `accumulate-offset-width-sort` runs
   per field visit and is O(fields^2) in `text-compare` calls. It allocated
   per-visit before this work too, on far fewer visits. The selfhost at DEFAULT
   decks still dies in `copy-sx-pos`, which reproduces on the pre-change depot
   seed and is the pre-existing floor-tipping crash, plausibly this same
   allocation pressure. `-Decks 200` remains required.

**"The IR is fully self-contained" HOLDS, but every DECK-LIVE number below it
was measured with an instrument that could not enter a single IR expression,
and the number itself has moved.** `cdx-ir` is an IRChapter whose defs carry
`IRExpr`, a sum, so the variant-blind walker walked the record and list spine
and stopped at every expression node.

**An earlier revision of this section, mine, claimed the conclusion was FALSE
on the strength of the new counts alone. That was wrong and the correction is
the useful part.** Non-zero DECK-LIVE counts do not establish live pinning,
because this document's own caveat is the operative one: the reservation-copies
rewind addresses, so a pointer into a keep deck lands in the address range a
reclaimed deck used to occupy and the segment LABEL decays. I read a label as a
finding. Poison-compact settles it and is why it is the ground truth here.

Re-measured on the selfhost with the fixed walker, `-Decks 200`, 2026-07-26:

| emit input | pre-scope | scope | check | lower |
|---|---:|---:|---:|---:|
| cdx-ir | **18,225** | **10,100** | 0 | **54,168** |
| stds | 0 | 0 | 0 | 0 |

The old table read 0 / 0 / 0 / 0 for `cdx-ir` and 3,511 into check for `stds`.
Both halves move: the counts into pre-scope, scope and lower are real slots,
and `stds` no longer reaches CHECK at all (CL 7100 did that part).

**Settled against poison, 2026-07-26, and this is the answer to the copying
compactor.** `-PoisonCompact` on the 2.89 MB selfhost with seed
`28329946FD972861`: zero errors, 11.8 s, and the output is **byte-identical to
the normal compile** (`21513EF3B9F4F565`, 2,621,929 bytes both ways). Each
compact fills its reclaimed range with a per-phase byte, so a compile that
reads anything which dies at a compact cannot produce the same bytes. It does.

So the emit inputs have **no live dependency on reclaimed phase memory**, the
non-zero DECK-LIVE counts are recycled-address artifacts rather than pinning,
and the dead middle is already reclaimed by the four reservation-copies
(parse-keep, check-keep, frontend-keep, LOWER). **For the emitter's inputs
there is no copying compactor left to build; the reservation-copy pattern is
it.** What remains open under this heading is not a mechanism but a coverage
question: whether any root OTHER than the two the emitter receives still
depends on a reclaimed deck, and poison-compact is the instrument for that too.

## Escape invariant: instrument repair and first real result (2026-07-04)

Why this stayed uncracked: the enforcement scaffolding existed
(conservative deck scan + a pmap typed walker behind `-EscapeCheck`),
but the instrument itself had silently rotted, so every "PRECISE
escape: 0" was vacuous and the conservative counts (hundreds of
thousands of hits, mostly integers that happen to look like bivy
addresses) were untriageable. Two drift classes had broken the
walker, and its own self-test knew (`pmap-walk self-test FAILED: got
0 (expect 3)`) -- but the failure was a warning behind an off-by-
default flag, so nobody saw it.

Fixed (blu, same day):

1. **Tag drift closed by construction.** `emit-const-codextype`
   hardcoded CodexType tag literals from an ordering that predates
   the Real* variants moving next to RealTy -- every tag from TextTy
   up was wrong, so table descriptors decoded as the wrong
   constructor and the walk fell through to zero. Tags are now read
   off LIVE CodexType values at emit time (`live-type-tag`: a boxed
   variant's value is its address; qword 0 is its tag), so tag
   renumbering can never desynchronize the table again. Mixed-width
   ctor slots (ForAllTy's 4-byte id) are written in width order to
   match `sum-ctor-field-offset`.
2. **The self-test is always-on.** `pmap-selftest-bag True` in the
   compile pipeline -- every compile of every program checks the
   running compiler's own baked table (cost: four small allocations
   and a three-pointer walk). Layout drift now fires a CDX9003
   warning in every battery log instead of hiding for months. It is
   a warning, not an error, deliberately: a generation-1 bootstrap
   binary (new emission code, old-seed-emitted table) must still be
   able to compile generation 2.
3. **Escape hits self-locate.** The walker threads a location
   context (record type + field name, existing Texts only -- no
   per-visit allocation) and prints `CDX9003-AT: <Type.field>` at
   each hit under `-EscapeCheck`.

First real measurement (selfhost, seed AB11135E generation): PARSE
root Document -- clean. SCOPE root AChapter -- **2 real dangling
pointers**: `AChapter.rt-budgets` and `AChapter.conversions`,
shallow-copied by `copy-as-chapter` while every sibling field is
deep-copied (the fields were added after the copier was written --
rt-budgets with `punctual`, conversions with unit types). Both
pointed into the reclaimed SCOPE bivy; the emitter reads rt-budgets
for CDX6010/6011 budgets, i.e. the exact BS3 `sort-bindings-loop`
latent-corruption class. Fixed in the same CL (deep copies). After
the fix: SCOPE/AChapter precise count is 0.

The residual honest gaps: precise walks are pinned only for the
PARSE (Document) and SCOPE (AChapter) roots; CHECK and LOWER outputs
have no typed root walk yet, and the conservative scan alone cannot
distinguish their real escapes from false positives. The copying
compactor this machinery is meant to drive stays blocked on
extending the precise roots, not on the walker anymore.

## The deck-liveness map (2026-07-04, second result)

The founding intent for this architecture (Damian): decks hold
durables deeply, bivies hold scratch, run a phase in bivy and
deep-copy the survivors back when needed, and remove late-phase
dependencies on early decks so the early decks become dead space the
late game can overwrite. Four prior runs at "the memory issue"
stalled because nobody could prove which decks were actually dead --
the escape instrument was broken, so liveness was guesswork.

With the walker repaired, the map is now a diagnostic: under
`-EscapeCheck`, `compile-frontend-cdx` walks the emitter's exact
inputs (`cdx-ir`, the IRChapter; `stds`, the sorted TypeBindings --
the ONLY two values `x86-64-emit-cdx` receives) against the live
deck segments and reports `DECK-LIVE <root> -> <segment>: N ref(s)`.

First measurement (selfhost, CDX mode, seed 09DF0CE4 generation):

| emit input | pre-scope (frontend keep) | SCOPE | CHECK | LOWER |
|---|---|---|---|---|
| cdx-ir | 0 | 0 | 0 | 0 |
| stds | 0 | 0 | **3,511** | 0 |

Two conclusions. First, the IR is fully self-contained: the LOWER
reservation-copy (`rewrite-ir-defs`) carried every name forward, so
the long-standing worry that emit-time names still point into the
lexer/scoper decks is FALSE for the CDX path. Second, the entire
dead-middle prize hangs on one strand: `sort-bindings` copies the
TypeBinding list spine into the RESOLVE deck but the `bound-type`
CodexType graphs behind it still live in the CHECK deck -- 3,511
pointers pinning the single biggest deck (~69 MB, plus transitively
the ~37 MB of frontend-keep + SCOPE below it, since nothing else
from the emit inputs reaches those either).

## Poison-compact: the second broken-watchdog finding (2026-07-04, third result)

Damian's directive to "test clean" under the phase-specific poison
(`-PoisonCompact`: each compact fills its reclaimed range with a
per-phase byte, 0xA1 lex .. 0xA9 frontend) found the same pattern as
the escape walker: **the detector existed and the selfhost was
already red on main** -- 21 CDX2000 `unresolved type` errors at emit,
identical on every seed generation tested (8CA1E63B, 09DF0CE4,
current). Nobody had been running the gate.

Diagnosis chain (each step verified by rebuild + rerun; the emit
error was instrumented to print the annotation address + first
qword, and a repeated poison byte names the guilty phase):

1. Tags read 0xA5 = CHECK's poison: emit reads type data that died
   at the check compact. The holes are all one class (durables left
   in scratch): `sort-expr-types` was the one un-deck-wrapped
   allocation in the CHECK tail (the sorted expr-types SPINE -- the
   exact table lowering reads; deck-wrapped this CL), and the
   binding/expr-type GRAPHS behind it live in check SCRATCH because
   `check-all-defs` runs deck-exited and `deep-resolve` SHARES every
   typevar-free subtree instead of copying.
2. A prototype structural type copier (depth-capped, null-tolerant)
   at the CHECK tail took the failure set from 21 sites to 8 under
   poison -- the residual 8 are annotations still carrying TypeVars,
   resolved at LOWER time through the (by then poisoned)
   check-scratch substitutions table. Copying or pre-resolving the
   substitutions wholesale CRASHES under poison -- GPF (CR2=0,
   non-canonical poison-byte pointers) -- because the substitution
   graphs already dangle into SCOPE-poisoned scratch at check time:
   the live compile only ever touches the check-era subset of those
   graphs; any full sweep walks into the dead zone. The prototype
   also failed the TEXT-mode gate on its first full pipeline run,
   so it ships with the campaign stage below, not tonight -- the
   diagnosis is the deliverable; the copies must land phase-by-phase
   with the gates.
3. Root: the SCOPE phase output is not deck-disciplined
   (`scope-achapter` runs outside `deck-record`; its result graph
   lives in scope scratch and survives by overwrite-luck). This is
   the founding "put the durables on deck, all of them, deeply"
   discipline, unfinished for SCOPE and the checker's tables.

## Campaign outcome: GREEN (2026-07-05, blu CLs 7098 + 7100)

The "invalid nodes" finding above was REFUTED by four probe cycles
before the fix landed; the record is kept because the reframe is
the lesson:

1. A 200 MB dead pad at the check tail (geometry probe)
   self-compiles clean and fixed-points -- downstream is not
   layout-sensitive.
2. A crash-proof read-only deep walk of every expr-type graph
   (range-check BEFORE peek) found 87,518 entries, 30,055,417 node
   visits, ZERO invalid nodes. The "misaligned garbage" diagnosis
   was an artifact of the alignment heuristic -- heap boxes are not
   8-aligned, and the guard silently skipped ~87k VALID nodes.
3. A memoized re-walk visits only 3.37M nodes: ~9x sharing
   multiplicity. The unguarded prototype was not walking into bad
   data -- it was UNSHARING the closure into 2-3 GB of copies on a
   3 GB machine and marching R10 through the stack. The guarded
   prototype's "layout-dependent" crashes were the alignment
   guard's parity-random pruning making the copy volume a
   per-build lottery (measured swinging 7.84M to 96.7k boxes
   between consecutive builds).
4. lookup-expr-type instrumentation: lowering consults 98.2% of
   entries -- no lazy-subset shortcut exists.

**As built (CL 7098):** the Deep Type Copy section is a MEMOIZED
graph copy -- open-addressing address-to-copy table (key32/val32,
Fibonacci top-bits index, probe fuel, claim-before-recurse so
cycle back-edges receive originals), four typed side lists as the
read-back vehicle, alignment guard deleted, every degraded path
returns the original box. The check tail runs sort, then
`resolve-all-bindings` / `resolve-all-expr-types` (pre-resolve
WHILE the substitutions are alive -- essential: copies alone scored
21 to 20 because lowering's deep-resolve on typevar-carrying
entries pulls original scratch subtrees back in through the
substitutions table), then copies both closures through ONE shared
memo. Result: poison 21 -> 0 with the poisoned output
byte-identical to the normal compile.

**Reclaim as built (CL 7100):** CHECK reservation-copy in the
parse-keep shape. A check-keep deck is reserved BELOW the check
scratch deck (surveyed at the time; today `demand-check-keep-floor`,
with CDX9002 as the net); CHECK plus the tail sorts and
pre-resolve run on scratch; `__deck-set` points the deck at the
reservation and the memo copies land there with the full survivor
set (substitutions and row-substitutions through the same memo,
both bags via `copy-sx-bag`, heap-marks spine, phase-metrics
rebuilt); one compact reclaims the scratch deck, the pre-resolve
intermediates, the memo table and side lists, and the reservation
tail. Post-check-compact mark: 585.4 MB (memo, pre-reclaim) ->
371.6 MB -- BELOW the 429.9 MB pre-memo baseline, because the
superseded check deck is reclaimed too. Poison stays green and
byte-identical; the DECK-LIVE emit-input map reads zero into every
pre-keep segment.

Remaining items:

1. **Pin:** keep the DECK-LIVE map in an `-EscapeCheck` battery run
   so a future field addition that re-pins a dead deck is caught
   the day it lands (the copy-as-chapter lesson generalized).
   Caveat learned: after address recycling the map's SEGMENT LABELS
   decay (the keep layer reports under old scratch ranges; ranges
   from different phase metrics overlap) -- treat poison-compact as
   ground truth and the map as a relative-motion instrument.
2. **Transient peak:** RESOLVED by the demand-paged arena. The CHECK
   reservation is a fixed 640 MB floor over demand-paged address
   space; the in-phase peak is what CHECK actually touches (~156 MB
   for the selfhost), not the reservation. The survey mul and the
   DynamicSurvey retry this item tracked are deleted.

**Empirical limit observed (2026-05-02 audit):** within-phase scratch
that the discipline reclaims is small -- bivy-usage measured at 16
bytes per phase (the PhaseStart record only); decks total 153 MB
across the 5 frontend phases in stage 1 selfhost text-mode. Sub-step
deck breakdown by function: tokenize-into ~27 MB, parse-document 15.7
MB, scan-document 8.0 MB, desugar-document 14.5 MB, scope-achapter
9.5 MB, resolve-chapter 18.1 MB, check-chapter 56.0 MB. Most of those
allocations are durable phase outputs (consumed by later phases),
not within-phase scratch.

Adopts the discipline named in `//Theory/Phases.md` and specifies how
Codex's compiler phases implement it concretely. This document is the
"how" that complements the theory document's "why." Written 2026-04-24,
status updated 2026-07-13.

## The problem this addresses

Codex's memory architecture currently makes three implicit commitments
it cannot keep:

1. **Durability contracts that are hopes, not rules.** Mutable lists
   thread through compilation phases on an unstated assumption that
   they remain live and unperturbed across phase boundaries. TCO
   reset, escape copy, and region reclaim mechanisms can each violate
   this assumption silently.
2. **Immutability contracts that are unenforced.** Records and IR
   nodes are declared immutable at the language level but
   mutable-by-copy at the implementation level. When a path-3
   `__list_insert_at` realloc happens, the old slots become garbage,
   but no mechanism ensures references to them have been updated;
   stale aliasing is a latent source of corruption.
3. **Liveness guesses that drive reclamation.** Every reclamation
   mechanism -- TCO reset's check-1/2/3 guards, region reclaim's
   scope analysis, escape copy's pointer chasing -- is guessing at a
   fact (what is live right now) that the program never explicitly
   states. Each guess has a failure mode; each failure mode surfaces
   somewhere.

BS3's stage-2 corruption in `sort-bindings-loop` (see
`TCO-RESET-COMPACTION.md`) is the specific manifestation that
triggered the rethink. The deeper pattern is broader: every
accumulator-in-TCO-loop in Codex has the same structural
vulnerability, and every memory mechanism that tries to fix it at
runtime inherits the original guess-based weakness.

## The architecture (as built)

Replace implicit durability with the deck/bivy discipline: each phase
begins at a col, pitches a bivy for its scratch work, builds its deck
atop the base, strikes the bivy and seals the deck at phase end --
returning to a new col from which the next phase begins. The base --
the stack of all sealed decks -- is the durable spine of the compile.

Deck heights were originally *surveyed* (estimated from input size by
per-phase multipliers). The survey era ended 2026-07-07: heights are
now fixed generous floors over demand-paged address space
(BuildSettings, Demand Decks section). A floor costs address space,
not memory -- physical pages commit on first touch -- so floors are
sized for the arena, not for the input, and cannot be under-sized by
a formula. `check-deck-overflow`/CDX9002 survives as the floor guard.

Vocabulary per `//Theory/Phases.md`:

- **pinnacle** -- the phase's memory peak (base + deck + bivy while the phase is working)
- **col** -- the memory trough after a phase's strike (base + sealed deck); becomes the base for the next phase
- **base** -- the accumulated structure of all previously-sealed decks at any point during the compile
- **deck** -- phase-durable contribution, built under its floor and sealed at phase end
- **bivy** -- phase-local scratch, pitched at phase start and struck atomically at phase end; has lifetime discipline but no pre-declared size
- **floor** -- the fixed generous address-space reservation a deck builds under (successor of the surveyed deck height)
- **prominence** -- the phase's HWM above its starting col (deck growth + observed bivy peak); observed, not pre-declared. The touched-page counter (cell 30688) is the physical-consumption metric; the R10 HWM reports reservations

Allocator primitive verbs:

- **pitch(size) → bivy** -- allocate a scratch region of exactly
  `size` bytes
- **build(size) → deck** -- allocate a durable region of exactly
  `size` bytes
- **strike(bivy)** -- atomically release all memory in the bivy (O(1))
- **seal(deck)** -- mark the deck read-only, validate no bivy
  references, add to the base

## Codex phases and their decks

The current compiler has roughly these phases in order. Only deck
contents are tabulated -- bivy contents are not pre-declared and are
not part of the phase's schema.

| Phase | Purpose | Deck contents (sealed) |
|---|---|---|
| lex | Tokenize source text | tokens, offset table |
| parse | Build AST | AST nodes, chapter/section index, def list, cite list |
| scope | Resolve names | name binding table, slug-mangled names |
| types | Type-check, infer | type environment, resolved types per expression |
| lower | Desugar to IR | IR defs, IR expressions |
| resolve | Resolve ConstructedTy | IR with concrete RecordTy/SumTy, sorted type bindings |
| lift | Lift nested lambdas | final IR with top-level defs, closure envs (CDX path) |
| emit | Generate target code | target output buffer(s) (`.text`, `.rodata`, patches) |

Each phase also pitches a bivy for whatever scratch its algorithm
requires -- lexer state, parser stack, unification scratch, codegen
patch lists, etc. Bivy contents are per-phase implementation detail,
not declared in the schema.

## Deck floors

Floor values live in `BuildSettings.codex` (Demand Decks section), one
constant per deck:

```
demand-lex-floor              96 MB
demand-parse-keep-floor       64 MB
demand-parse-scratch-floor   192 MB
demand-desugar-floor          64 MB
demand-scope-floor            96 MB
demand-check-floor           640 MB
demand-check-keep-floor       96 MB
demand-frontend-keep-floor   192 MB
demand-lower-floor           320 MB
demand-resolve-floor         192 MB
demand-lift-floor             96 MB
demand-inline-floor           96 MB
```

A floor is a reservation of address space, not a commitment of memory.
The boot demand-pages the heap range; a physical page is committed the
first time it is touched, so a phase's real cost is what it touches,
not what it reserves. This is why floors are sized for the arena rather
than for the source, and why over-reservation is free.

The floors are the whole sizing story. There is no survey pass to run,
no multiplier to tune, and no under-count to guard against by
estimation. The selfhost runs at 2-6x headroom under every floor;
`check-deck-overflow` / CDX9002 remains as the breach guard, and it
fires only on a genuine floor breach.

(The retired survey computed each height from the previous phase's
sealed deck plus source text. The formula could not be sized honestly,
and its under-reservation cliff was the silent-corruption class the
demand-paged arena was built to kill.)

Bivies have no floor at all -- their size is determined by the phase's
algorithm at runtime and is not pre-declarable.

## Escape invariant

No deck pointer may target a bivy address. Enforcement options, from
strictest to laxest:

1. **Static (type-level).** Every pointer type carries an arena tag
   derived from its allocation site. Sealed-deck fields may only
   hold sealed-deck or same-phase-deck-pre-seal pointers. The type
   system refuses the illegal combination. Each deck and bivy is a
   distinct abstract type; the compiler rejects writes that cross
   classes. Most robust, requires language-level support.
2. **Seal-time check.** At `seal(deck)`, walk the deck's
   pointer-valued slots and verify each target is below the deck's
   top or in an earlier base deck. O(pointers_in_deck) per seal.
   Catches violations but allows them to be constructed -- compile
   fails at seal rather than at the illegal write.
3. **Write-barrier.** Every pointer write to a deck slot validates
   the target's address class before storing. O(1) per write,
   O(writes) aggregate. Catches violations at the moment of
   occurrence; most expensive to run.

Recommendation for initial implementation: **option 2 (seal-time
check)**. Cheapest to implement, strongest diagnostic when it fires,
no language-level work required. Migrate to option 1 once the deck
schemas are stable enough to codify in types.

## Migration path

Current Codex compiler uses one bump heap for everything, with TCO
reset attempting to reclaim within phases. Migration can be gradual;
each step is independently mergeable.

1. **Add the vocabulary and the allocator primitives.** No behavioral
   change -- `pitch`, `build`, `strike`, `seal` wrap the existing
   allocator. Existing code unchanged. Internal bookkeeping only.
2. **Convert one phase at a time.** Start with lex (simplest deck).
   Pitch a bivy for lexer scratch; build a deck for tokens under the
   phase's floor; seal at phase end. A floor breach is a compile error
   (CDX9002); raise the floor.
3. **Propagate.** Each phase converted in turn. Phases already have
   clear boundaries, so each conversion is local.
4. **Remove TCO reset.** Once phases are converted, within-phase
   reclamation is no longer needed. CL 327's compacting reset
   becomes unnecessary; revert to "no reset at all." The phase
   boundary is the reclamation point.
5. **Add escape invariant enforcement.** Start with seal-time check
   (option 2 above); migrate to static (option 1) once schemas
   stabilize.
6. **Deprecate the unified bump heap.** Long-term, replace the single
   bump allocator with explicit per-bivy and per-deck regions, each
   bounded by its declared prominence.

The compiler continues to work through every step. Each step
independently reduces memory footprint and increases determinism.

## Integration with existing work

- **TCO-RESET-COMPACTION.md (CL 326 doc, CL 327 shelved compact).**
  The compacting reset in CL 327 is a partial soundness fix that
  stays shelved for now. Under phase discipline, TCO reset becomes
  unnecessary. If migration to phase discipline happens, CL 327 is
  abandoned; if it doesn't, CL 327 remains a candidate improvement.
  Either way, the shelved CL is not the primary path forward.
- **`opening.codex` `compile-measure`.** Existing instrumentation
  computes heap HWM per phase. It measures the *result* of each phase,
  and under the demand-paged arena the touched-page counter (cell
  30688) is the honest physical-consumption number; the R10 HWM
  reports reservations.
- **`__list_with_capacity`.** Already exists in Codex. Under phase
  discipline it is the pre-emptive allocator for deck-bound lists.
  `__list_insert_at` path-2 (grow-in-place) and path-3
  (alloc-new-buffer) become unnecessary for deck lists; a floor breach
  is a compile error, not a resize event. Bivy-bound lists may still
  grow (bivies have no size discipline); whether they use the same
  allocator is an implementation choice.
- **`__record-set`'s in-place semantics on bare-metal.** Compatible
  with phase discipline, because the record being set lives in a
  deck that hasn't been sealed yet. Once sealed, the record is
  read-only. In-place is a pre-seal optimization.

## Open questions

1. **How does incremental compilation interact with phase
   discipline?** If only part of the source changes, can earlier
   phases' decks be reused? Probably yes for any phase whose inputs
   (source + base) are unchanged. Formalization is future work; the
   phase discipline makes this question tractable where it wasn't
   before.
2. **How does the discipline handle compilation errors?** Partial
   work in a phase that fails: the deck is never sealed, the bivy
   is struck, no memory added to the base. Error handling is
   cleaner under the discipline, not messier.
3. **What's the cost of sizing?** ANSWERED. Zero. Floors are
   constants; nothing is computed. The survey that this question was
   asked about was deleted in favour of demand paging, which trades a
   compile-time estimate for a page-fault handler.
4. **How does the discipline interact with parallelism?** Phases
   sharing no bivy can run concurrently. Phase A writes only to its
   own deck; phase B reads only from earlier decks; they can
   parallelize. Eventual parallel codegen requires disciplined phase
   boundaries; this architecture provides them.
5. **What if a phase exceeds its floor?** CDX9002, halt, raise the
   floor. Since a floor costs only address space, the honest answer to
   a breach is a bigger constant -- not an overflow-recovery mechanism.
6. **How is within-phase bivy growth bounded?** The discipline kills
   cross-phase growth; within-phase scratch can still accumulate if
   a long-running loop inside one phase generates intermediates that
   live to phase end. Options: (a) accept the cost if it's small --
   peak memory is dominated by the base, not the bivy, for most
   phases; (b) decompose long phases into sub-phases with their own
   bivies, each struck at its own boundary; (c) introduce in-phase
   arena primitives (secondary pitch/strike) governed by the
   algorithm -- which reintroduces TCO-reset-like soundness questions.
   Lean: start with (a), measure, move to (b) if peak bivy becomes a
   measured problem, avoid (c) unless forced.
7. **How does this affect the reference (C#) compiler?** REF uses
   .NET's GC, which makes the discipline optional. Phase discipline
   still reduces working set and improves determinism, but it isn't
   blocking for REF. Priority is self-host bare-metal.

## Relationship to `//Theory/Phases.md`

The theory document argues why this discipline is a consequence of
the middle-way axiom at the phase scale. This document specifies how
the discipline applies to Codex's concrete architecture. One states
the principle, the other states the implementation.

Keep them synchronized: if implementation reveals a principle change
(e.g., a seventh vocabulary term emerges), update both; if theory
revises (e.g., a different axis of the duality becomes primary),
propagate the implementation consequences here.

## Next concrete steps, if greenlit

1. **Prototype the allocator primitives.** `pitch`, `build`,
   `strike`, `seal` as library wrappers around the existing bump
   allocator. Just the bookkeeping machinery.
2. **Convert the lex phase.** Simplest, cleanest pinnacles. Validates
   the primitives against real use.
3. **Measure.** Deck-memory ceiling, bivy peak as observed behavior.
   Go/no-go on continuing.
4. **Parse, scope, types phases.** If the lex conversion holds,
   these are mechanical.
5. ✅ **Lower, resolve, lift, emit.** (CL 2169) Separated. These have the largest
   decks. Address last.

If the lex conversion reveals unexpected difficulty, stop and revise
the architecture. The vocabulary and the theory hold; the
implementation strategy is what's being tested.

## Implementation status

### Completed refactors (CLs 629–636, 2026-05-01 / 05-02)

| CL | Change | Impact |
|---|---|---|
| 632 | Deck-record wrapper delegates to `emit-deck-enter-builtin` / `emit-deck-exit-builtin` | Pointer math for deck-record removed from emitter |
| 636 | Allocator → **Phase Allocator** rename; **Emit Allocator** with `EmitWorkspace` type; `CodegenState.workspace` replaces raw `text-buf-addr`/`rodata-buf-addr`; `emit-bivy-alloc` helper; `codegen-carry-forward` helper; `__alloc` runtime function | Emitter no longer holds raw buffer pointers; bivy allocation goes through `__alloc`; per-function state reset centralized |
| 644 | Per-phase `build` for all 6 frontend phases; `phase-measure`/`phase-compact` split; result records sealed on deck before compact; `emit-build` in Emit Allocator; emitter owns its own deck for IR rewrites (wall between frontend and emitter); `init-phase-allocator` sets mountain base | Single 260 MB upfront build replaced by right-sized per-phase builds (~149 MB total); deck-pos cascades through phases; emitter isolated from phase allocator state |

Migration path status vs. the plan above:

1. ✅ **Vocabulary + allocator primitives.** `pitch`, `build`, `strike`, `seal`, `deck-record` in Phase Allocator. `EmitWorkspace`, `init-emit-workspace`, `emit-build` in Emit Allocator. `phase-start`, `phase-measure`, `phase-compact` for lifecycle.
2. ✅ **All 8 frontend phases converted.** Lex, parse, desugar, scope, type-check, lower, resolve, lift -- each with a per-phase `build`, `phase-start`, deck-record work, `phase-measure`, result sealed on deck, `phase-compact`.
3. ✅ **Measure.** Full phase metrics surfaced in watchdog output (deck-origin, deck-end, deck-usage, bivy-hwm, bivy-usage per phase), plus the touched-page counter for physical consumption.
4. ✅ **Emitter wall.** `emit-build` gives the emitter its own deck. Frontend deck is read-only to the emitter. ConstructedTy resolution (`rewrite-ir-defs`) and lambda lifting now run in their own frontend phases (RESOLVE, LIFT) before the emitter sees the IR. The x86 emitter takes `(IRChapter, List TypeBinding)` -- no `TypeEnv`.
5. ✅ **Deck sizing.** Fixed generous floors over a demand-paged heap (`demand-*-floor` in `BuildSettings.codex`). The survey is deleted; there is nothing left to tighten.
6. ✅ **Escape invariant enforcement.** All four typed roots walk: `Document` (PARSE), `AChapter` (SCOPE), `CompileChecked` (CHECK), `IRChapter` (LOWER), each through `precise-escape-bag` in `opening.codex`. CL 10527.
7. ✅ **Remove TCO reset.** Went out 2026-04-24 in the BS3 rip-out. `TcoState` has no heap field and neither loop-top setup touches a heap register.

**These two rows said "Open work" until 2026-07-28 while the section at
the top of this file recorded both as done, and CurrentPlan's gap for
this work quoted the stale pair as its whole body.** A reader who started
at either end got a different answer, and the gap was handed out and
picked up on the strength of the wrong one. Both closures were re-verified against source
rather than against the other half of this document before these rows
moved. A status list far from the finding it describes is the thing that
rots; when the top of this file moves, move these rows in the same
edit.

### Phase measurement (2026-05-02, CL 644 -- historical, survey era)

Source: selfhost compiler (~892 KB). Per-phase builds with compact.
The multiplier column is the deck sizing of the day; multipliers were
retired 2026-07-07 in favour of the demand floors above. The *usage*
figures remain the reason the floors are sized as they are.

| Phase | Deck origin | Deck end | Deck usage | Retired multiplier |
|---|---|---|---|---|
| lex | 7,760,272 | 34,714,352 | **27.0 MB** | 34x + 1 MB |
| parse | 34,714,464 | 58,708,824 | **24.0 MB** | 30x + 1 MB |
| desugar | 58,709,008 | 72,661,648 | **13.9 MB** | 18x + 1 MB |
| scope | 72,661,680 | 100,729,280 | **28.1 MB** | 35x + 1 MB |
| check | 100,729,448 | 156,376,416 | **55.6 MB** | 65x + 1 MB |
| lower | 90.9 MB | 457 MB | 91 MB | S × 300 + 1 MB |
| resolve | 7.7 MB | 438 MB | 8 MB | S × 200 + 1 MB |
| lift | 16.3 MB | 445 MB | 16 MB | S × 200 + 1 MB |
| **total (5 phases)** | | | **148.6 MB** | |

Bivy usage: 16 bytes per phase (the PhaseStart record). All real allocations go to the deck via `deck-record`. Strikes reclaim bivy only.

Key observations:

- **Memory reduced from 260 MB to ~149 MB** -- per-phase builds right-sized from measurements, each with ~10% headroom.
- **Check is the heaviest phase** (55.6 MB) -- type inference allocates extensively. Scope (28 MB) and lex (27 MB) are next.
- **Deck-pos cascades correctly** -- each phase starts where the previous sealed its deck.
- **Emitter wall enforced** -- `emit-build` gives the emitter its own deck. ConstructedTy resolution and lambda lifting run in dedicated frontend phases (RESOLVE, LIFT) with their own compact cycles. The emitter receives pre-resolved, pre-lifted IR.
- **Phase-measure/phase-compact split** -- metrics captured while bivy is live, result sealed on deck, then compact. No allocations after compact.

### Emitter encapsulation status

| Concern | Before (CL ≤ 631) | After (CL 636) |
|---|---|---|
| Deck-record pointer math | Inline in `emit-deck-record-wrapper` (40 lines, 3 fixed addresses) | Delegates to `emit-deck-enter-builtin` / `emit-deck-exit-builtin` |
| Output buffer addresses | Raw `text-buf-addr` / `rodata-buf-addr` on `CodegenState` | `workspace : EmitWorkspace` |
| Bivy allocation (records, ctors, closures, lists) | 5-line inline R10 bump at 6 sites | `emit-bivy-alloc` → `call __alloc` |
| Per-function state reset | 28-line `CodegenState` reconstruction | `codegen-carry-forward` (1 call) |
| Remaining inline R10 bumps | ~16 sites in runtime helpers + small builtins | Unchanged -- tight assembly, not worth call overhead |
