# Phase Architecture

**Status:** Partially implemented (2026-05-02). The vocabulary,
allocator primitives, per-phase build, phase-measure, and phase-compact
are in place across all 6 frontend phases (CLs 500, 552, 632–645). The
emitter wall is enforced (CL 644). Outstanding: lower/emit isolation,
escape invariant enforcement, TCO reset removal, survey tightening.

**Empirical limit observed (2026-05-02 audit):** within-phase scratch
that the discipline reclaims is small — bivy-usage measured at 16
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
status updated 2026-05-02.

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
   mechanism — TCO reset's check-1/2/3 guards, region reclaim's
   scope analysis, escape copy's pointer chasing — is guessing at a
   fact (what is live right now) that the program never explicitly
   states. Each guess has a failure mode; each failure mode surfaces
   somewhere.

BS3's stage-2 corruption in `sort-bindings-loop` (see
`TCO-RESET-COMPACTION.md`) is the specific manifestation that
triggered the rethink. The deeper pattern is broader: every
accumulator-in-TCO-loop in Codex has the same structural
vulnerability, and every memory mechanism that tries to fix it at
runtime inherits the original guess-based weakness.

## The proposed architecture

Replace implicit durability and runtime guessing with
*survey-before-allocate*: each phase begins at a col, surveys to
determine its deck's height, pitches a bivy for its scratch work,
builds its deck atop the base to the surveyed height, strikes the
bivy and seals the deck at phase end — returning to a new col from
which the next phase begins. The base — the stack of all sealed
decks — is the durable spine of the compile; every later phase
surveys against it.

Vocabulary per `//Theory/Phases.md`:

- **pinnacle** — the phase's memory peak (base + deck + bivy while the phase is working)
- **col** — the memory trough after a phase's strike (base + sealed deck); becomes the base for the next phase
- **base** — the accumulated structure of all previously-sealed decks at any point during the compile
- **deck** — phase-durable contribution, built to a surveyed height and sealed at phase end
- **bivy** — phase-local scratch, pitched at phase start and struck atomically at phase end; has lifetime discipline but no pre-declared size
- **survey** — the measurement step that produces deck heights before the phase allocates
- **deck height** — the numeric output of a survey, the committed size the deck builds to
- **prominence** — the phase's HWM above its starting col (deck height + observed bivy peak); partially observed, not fully pre-declarable

Allocator primitive verbs:

- **pitch(size) → bivy** — allocate a scratch region of exactly
  `size` bytes
- **build(size) → deck** — allocate a durable region of exactly
  `size` bytes
- **strike(bivy)** — atomically release all memory in the bivy (O(1))
- **seal(deck)** — mark the deck read-only, validate no bivy
  references, add to the base

## Codex phases and their decks

The current compiler has roughly these phases in order. Only deck
contents are tabulated — bivy contents are not surveyed, not
pre-declared, and not part of the phase's schema.

| Phase | Purpose | Deck contents (surveyed, sealed) |
|---|---|---|
| lex | Tokenize source text | tokens, offset table |
| parse | Build AST | AST nodes, chapter/section index, def list, cite list |
| scope | Resolve names | name binding table, slug-mangled names |
| types | Type-check, infer | type environment, resolved types per expression |
| lower | Desugar to IR | IR defs, IR expressions, lambda list |
| lambda-lift | Lift nested lambdas | final IR with top-level defs, closure envs |
| emit | Generate target code | target output buffer(s) (`.text`, `.rodata`, DWARF, patches) |

Each phase also pitches a bivy for whatever scratch its algorithm
requires — lexer state, parser stack, unification scratch, codegen
patch lists, etc. Bivy contents are per-phase implementation detail,
not declared in the schema.

Survey sources per phase (each survey is a function of already-sealed
decks plus source text):

- lex surveys source text directly (byte count → rough token budget)
- parse surveys the lex deck
- scope surveys the parse deck
- types surveys the scope deck and parse deck
- lower surveys the types deck and parse deck
- lambda-lift surveys the lower deck
- emit surveys the lambda-lift deck

A survey never consults its own phase's in-progress state. This keeps
the survey cheap (bounded by already-sealed material) and ensures
deck heights are determined before any allocation for that phase
happens. Bivies, by contrast, are not surveyed — their size is
determined by the phase's algorithm at runtime and is not
pre-declarable.

## Survey pass signature

```
survey_<phase>(base: Base, source: Source) -> DeckHeights
```

`DeckHeights` is a record of `(deck_component_name, size)` pairs. The
component names are fixed per phase (part of the phase's declared
schema); sizes are computed by the survey. Only the deck is surveyed;
bivy size is not pre-declared.

Surveys must be fast relative to the phase they precede. 5-10%
overhead is acceptable; 50% is not. If a survey is expensive, the
phase should be decomposed into sub-phases with their own surveys.

Survey error cases:

- **Under-count.** If the survey returns a deck height smaller than
  what the phase actually needs, the phase will hit the deck ceiling
  during `build`. This is a compile error; the survey must be
  corrected. Not recoverable at runtime.
- **Over-count.** Survey overestimates. Wastes memory proportional to
  the overestimate. Acceptable. Surveys should aim for tight upper
  bounds, not exact figures; tight is better than exact for both
  cost and robustness.

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
   Catches violations but allows them to be constructed — compile
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
   change — `pitch`, `build`, `strike`, `seal` wrap the existing
   allocator. Existing code unchanged. Internal bookkeeping only.
2. **Introduce survey for one phase at a time.** Start with lex
   (simplest deck). Write a survey routine that estimates token
   count; pitch a bivy for lexer scratch; build a deck for tokens to
   the surveyed height; seal at phase end. Under-count is a compile
   error; correct the survey.
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
  computes heap HWM per phase. That is survey-adjacent — it measures
  the *result* of each phase. Survey moves the measurement upstream
  (before the phase, not after), turning a diagnostic into a
  contract.
- **`__list_with_capacity`.** Already exists in Codex. Under phase
  discipline, this becomes the pre-emptive allocator for deck-bound
  lists, sized from surveyed deck heights. `__list_insert_at` path-2
  (grow-in-place) and path-3 (alloc-new-buffer) become unnecessary
  for deck lists; a deck overflow is a compile error, not a resize
  event. Bivy-bound lists may still grow (bivies have no size
  discipline); whether they use the same allocator is an
  implementation choice.
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
3. **What's the cost of surveys?** A rough token-count over source
   is near-free; a precise type-node count requires walking the
   parse deck. Expectation: surveys add 5-15% to total compile time
   in exchange for a deterministic deck-memory ceiling. Measure when
   we have a concrete implementation.
4. **How does the discipline interact with parallelism?** Phases
   sharing no bivy can run concurrently. Phase A writes only to its
   own deck; phase B reads only from earlier decks; they can
   parallelize. Eventual parallel codegen requires disciplined phase
   boundaries; this architecture provides them.
5. **What if a later phase discovers its deck height was wrong?**
   Current answer: compile error, the survey was wrong, fix it. A
   more forgiving answer: allow one layer of overflow recovery via
   an overflow deck the next phase consumes as secondary input.
   Overflow recovery is optional; correctness does not depend on it.
6. **How is within-phase bivy growth bounded?** The discipline kills
   cross-phase growth; within-phase scratch can still accumulate if
   a long-running loop inside one phase generates intermediates that
   live to phase end. Options: (a) accept the cost if it's small —
   peak memory is dominated by the base, not the bivy, for most
   phases; (b) decompose long phases into sub-phases with their own
   bivies, each struck at its own boundary; (c) introduce in-phase
   arena primitives (secondary pitch/strike) governed by the
   algorithm — which reintroduces TCO-reset-like soundness questions.
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
   allocator. No survey required yet; just the bookkeeping
   machinery.
2. **Convert the lex phase.** Simplest, cleanest pinnacles. Validates
   the primitives against real use.
3. **Measure.** Compile-time overhead from survey, deck-memory
   ceiling improvement from surveyed heights, bivy peak as observed
   behavior. Go/no-go on continuing.
4. **Parse, scope, types phases.** If the lex conversion holds,
   these are mechanical.
5. **Lower, lambda-lift, emit.** The hardest; these have the largest
   decks and the most interdependent surveys. Address last.

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
2. ✅ **All 6 frontend phases converted.** Lex, parse, desugar, scope, type-check, lower — each with per-phase `build(survey)`, `phase-start`, deck-record work, `phase-measure`, result sealed on deck, `phase-compact`.
3. ✅ **Measure.** Full phase metrics surfaced in watchdog output (deck-origin, deck-end, deck-usage, bivy-hwm, bivy-usage per phase). Per-phase surveys based on measured multipliers.
4. ✅ **Emitter wall.** `emit-build` gives the emitter its own deck for IR rewrites. Frontend deck is read-only to the emitter. `deck-record` calls in `rewrite-ir-defs` write to the emitter's deck, not the frontend's.
5. ⬜ **Remove TCO reset.** Not yet addressed.
6. ⬜ **Escape invariant enforcement.** Not yet addressed.
7. ⬜ **Survey tightening.** Current multipliers are generous (10% headroom over measurements). Can be tightened further as the codebase stabilizes.

### Phase measurement (2026-05-02, CL 644)

Source: selfhost compiler (~892 KB). Per-phase builds with compact.

| Phase | Deck origin | Deck end | Deck usage | Survey multiplier |
|---|---|---|---|---|
| lex | 7,760,272 | 34,714,352 | **27.0 MB** | 34x + 1 MB |
| parse | 34,714,464 | 58,708,824 | **24.0 MB** | 30x + 1 MB |
| desugar | 58,709,008 | 72,661,648 | **13.9 MB** | 18x + 1 MB |
| scope | 72,661,680 | 100,729,280 | **28.1 MB** | 35x + 1 MB |
| check | 100,729,448 | 156,376,416 | **55.6 MB** | 65x + 1 MB |
| lower | (not yet measured in text-streaming path) | | | 48x + 1 MB |
| **total (5 phases)** | | | **148.6 MB** | |

Bivy usage: 16 bytes per phase (the PhaseStart record). All real allocations go to the deck via `deck-record`. Strikes reclaim bivy only.

Key observations:

- **Memory reduced from 260 MB to ~149 MB** — per-phase builds right-sized from measurements, each with ~10% headroom.
- **Check is the heaviest phase** (55.6 MB) — type inference allocates extensively. Scope (28 MB) and lex (27 MB) are next.
- **Deck-pos cascades correctly** — each phase starts where the previous sealed its deck.
- **Emitter wall enforced** — `emit-build` gives the emitter its own deck for IR rewrites. Frontend deck is read-only to the emitter.
- **Phase-measure/phase-compact split** — metrics captured while bivy is live, result sealed on deck, then compact. No allocations after compact.

### Emitter encapsulation status

| Concern | Before (CL ≤ 631) | After (CL 636) |
|---|---|---|
| Deck-record pointer math | Inline in `emit-deck-record-wrapper` (40 lines, 3 fixed addresses) | Delegates to `emit-deck-enter-builtin` / `emit-deck-exit-builtin` |
| Output buffer addresses | Raw `text-buf-addr` / `rodata-buf-addr` on `CodegenState` | `workspace : EmitWorkspace` |
| Bivy allocation (records, ctors, closures, lists) | 5-line inline R10 bump at 6 sites | `emit-bivy-alloc` → `call __alloc` |
| Per-function state reset | 28-line `CodegenState` reconstruction | `codegen-carry-forward` (1 call) |
| Remaining inline R10 bumps | ~16 sites in runtime helpers + small builtins | Unchanged — tight assembly, not worth call overhead |
