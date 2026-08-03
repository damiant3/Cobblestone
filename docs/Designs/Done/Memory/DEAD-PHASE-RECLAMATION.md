# Dead-Phase Deck Reclamation

**Status:** Design / feasibility proven. 2026-05-29 (reek). No code yet.
Successor step to PHASE-ARCHITECTURE.md (which reclaims only bivy, ~16 B/
phase; the 149 MB is durable *decks* that currently persist for the whole
compile). Realizes Damian's "once lex and parse are no longer needed, drop
those phases and re-cover that area of the heap."

## The finding (PROVEN, structural)

After DESUGAR, the LEX deck (tokens) and PARSE deck (CST) hold **no live
inbound pointers** and are dead. This is guaranteed by the AST type
definitions, not by discipline:

- Every desugared node (`AExpr`'s 19 variants, `APat`, `ATypeExpr`, `ADef`,
  `ATypeDef`, … in `codex/compiler/Ast/AstNodes.codex`) carries only
  `Name`, `SourceSpan`, `Text`, scalars, and recursive `AExpr`. **No
  `Token` field, no CST-node field anywhere.**
- `SourceSpan` (`Core/SourceText.codex`) is pure integers (line/col/offset/
  file-id) -- never a pointer into the source buffer.
- Desugar copies, it doesn't alias: `is NameExpr (tok) -> ANameExpr
  (make-name (token-text tok)) ...` -- `token-text` = `substring`, a fresh
  string in the desugar deck.

Lifetime: tokens/CST are consumed *through* desugar (parser reads tokens;
`Desugarer.codex:92` reads `tok.source` for literal text), then dead.
Measured deck sizes (PHASE-ARCHITECTURE): lex ~27 MB + parse ~24 MB ≈
**51 MB reclaimable** of ~149 MB frontend total.

## The blocker (the only one)

Decks cascade. `build` (PhaseAllocator) sets `deck-pos` at the current heap
top and packs each phase's deck contiguously upward; `phase-compact =
__heap-restore(__deck-pos)` reclaims only the *bivy above* the sealed deck.
So LEX+PARSE sit at the **bottom**, buried under the live DESUGAR deck. A
bump allocator cannot free a buried region without moving what's above it.

## Why the obvious moves don't work / the right mechanism

- **Free the bottom region in place** -- impossible with bump alloc (hole in
  the middle; later `build`s allocate at the top).
- **TCO heap-reset / CL 327 compacting reset** -- already tried and shelved.
  It corrupted `sort-bindings-loop` (BS3 blocker) because it rewound `r10`
  *mid-loop* into a live, cumulatively-grown list whose growth was invisible
  to per-iteration checks (TCO-RESET-COMPACTION.md). **That is a different
  problem:** within-phase, per-iteration, undecidable liveness.
- **Right mechanism: a phase-boundary copying compaction.** At the
  DESUGAR→SCOPE boundary there is a *single live root* (the desugared
  `AChapter`) and a *clean, known point in time*. Scavenge that root down
  over the dead LEX+PARSE region (copy reachable objects, patch pointers via
  forwarding), set `deck-pos` past the copied AST, reclaim the rest. This is
  a one-shot deterministic semispace copy with a known root -- decidable
  liveness, no mid-loop hazard -- categorically safer than the TCO reset that
  failed. It is NOT a general GC; it runs at fixed boundaries.

## Evaluating the two ideas on the table

- **Plan A -- drop dead phases + re-cover the area (allocator remembers).**
  Feasible and proven (above). Win ≈ 51 MB now; more if applied at later
  boundaries (e.g. drop the scope/check decks once LOWER's IR no longer
  references them -- needs the same structural check per boundary). Cost: a
  copying compactor with pointer patching -- real complexity, and pointer
  patching is exactly the delicate part that bit CL 327 (mitigated here by
  the clean-boundary/single-root setup).
- **Plan B -- bivy up-north halfway to the stack, skip surveys.** This is the
  pre-discipline regime that became the 4 GB monster: immutable
  copy-on-append lists (O(n²), ~900 MB in the tokenizer alone) + every phase
  kept live in `compile`'s let-chain. The phase discipline exists *because*
  B failed. Re-adopting it without the O(n²) fixes re-creates the monster;
  with them it just trades determinism for a higher, unmanaged peak. Not
  recommended.

## Prerequisite + concrete next steps (no seed yet)

1. **Pointer-aware escape/liveness check.** The existing `scan-deck-dangling`
   (the `-EscapeCheck` seal-time check) can't tell a pointer from an integer,
   so its counts are confounded for large-bivy phases (SCOPE read 126 K,
   mostly false positives). A reclamation gate must *prove* a deck has zero
   live inbound pointers -- which needs distinguishing pointers from integers
   in a header-less bump heap (open problem: per-allocation tags, or a typed
   walk from roots). This same check de-confounds the escape counts and is
   the already-listed "escape invariant enforcement" open item. **Do this
   first** -- it derisks the compaction and pays off independently.
2. **Audit the later boundaries** the way LEX/PARSE were audited here: does
   any post-SCOPE structure reference the scope/check decks? (Same
   AST-types-are-token-free style proof per boundary.)
3. **Prototype the DESUGAR-boundary scavenge** behind a flag; measure heap
   HWM before/after (validate by HWM, not the confounded escape count).

## The enabling primitive: type pointer-maps

The crux under both step 1 and the compactor is the same: the bump heap is
header-less (`__alloc` returns a bare pointer; records are raw width-sorted
field arrays with no type tag), so a deck slot's pointer-vs-integer nature
cannot be recovered from the heap. A conservative scan (any 8-aligned
in-range value = maybe-pointer) is all `scan-deck-dangling` can do -- hence
the false positives.

But the **compiler has the missing information at emit time**: it lays out
every record/variant by width-sort and knows exactly which fields are
pointers. So emit a **per-type pointer-map** (a bitmap of pointer-valued
field offsets) into the binary, and tag each allocation with its type id (or
co-allocate the map reference). Then both consumers become *precise*:

- the escape/liveness check walks objects by their type's pointer-map (real
  proof, not a heuristic -- de-confounds SCOPE too);
- the copying compactor knows exactly which fields to forward/patch.

This is the one foundational feature that unlocks both precise escape
enforcement (an existing open item) and dead-phase reclamation. It is also
the standard prerequisite any precise (non-conservative) reclaimer needs.
Cost: a small per-type table + a type tag per allocation; the type info
already exists in the emitter.

## Implementation status (2026-05-29, reek) -- typed walk COMPLETE + validated

The typed-walk half is built and runtime-validated (WIP on MutableRecords,
unlanded, gated behind -EscapeCheck via a temporary self-test). NO per-object
type tags are needed -- the typed-walk insight holds: a walk from a typed root
uses the static type + the variant tag already at offset 0. So the original
"tag each allocation with a type id" idea is REJECTED (it would force a header
word onto headerless records -- a layout change touching every offset). What
IS needed is the root's type, resolved at runtime.

Built (all in codex/compiler, runtime-proven):
- `is-pointer-type : CodexType -> Boolean` (Types/CodexType.codex) -- 19
  variants; nullary ctors heap-box so ALL SumTy are pointers; TypeVar is the
  one conservative case (monomorphized maps are future work).
- `build-record-pointer-map` / `build-ctor-pointer-map` (Emit/X86_64Compound)
  -- pointer-field offsets (record width-sort reuses cce-byte-offset-and-type;
  ctor layout is positional, tag@0, field i @ 8+i*8).
- `pmap-walk` + helpers (Emit/X86_64Compound) -- precise typed traversal from a
  typed root; handles RecordTy/SumTy/ListTy/ConstructedTy (resolved by name
  against a `List TypeBinding` table via lookup-type-binding); fuel-bounded;
  counts pointers landing in [lo,hi). Validated: nested-record self-test -> 3.
- `address-of : ForAllTy 0 (FunTy (TypeVar 0) Int)` (the bridge) -- a polymorphic
  builtin, pure inline identity (a pointer value already IS its address; same
  shape as `show`). Validated in-session (address-of(rec) == __heap-save
  captured before alloc). DEFERRED -- NOT in the foundation CL; lands with its
  real consumer (walking an EXISTING phase root, where the capture-before-build
  trick fails because a root is built children-first, so it is the LAST alloc).

What LANDS in the foundation CL (a pure library addition -- no new builtin, no
emitter change, so the existing seed compiles it and the fixed point holds):
is-pointer-type, pmap-walk + helpers, and `pmap-self-test` reframed as a
PERMANENT gated conformance check (opening.codex pmap-selftest-bag; fires a
diagnostic only on FAILURE, under -EscapeCheck only -- gates/smoke don't set
that flag, so they are unaffected). The self-test uses the __heap-save
capture-before-build trick (cap) instead of address-of, so no builtin is
needed. It walks PmTestRec{tr-num:Int, tr-ptr:Text, tr-inner:PmInner} -> 3.

GOTCHA learned: bare string literals emit to RODATA (emit-text-lit), real
pointers but BELOW the heap -> correctly excluded by the [lo,hi) range filter,
never a false escape. New builtins (like address-of, next CL) need a TWO-PASS
bootstrap (pass1 compiler registers it w/o using it; pass2 that compiler
compiles source that uses it) -- which is why address-of is deferred.

## The remaining gap + chosen solution: emit a self-type-table (Damian, b)

pmap-walk needs the root's type at runtime, but the compiler does NOT carry
descriptions of its own types -- the type checker builds CodexTypes for the
PROGRAM being compiled, not for AChapter/AExpr. Decision (2026-05-29): EMIT a
self-type-table, not hand-author one. The emitter already holds every type in
`st.type-defs : List TypeBinding` (CodegenState); during a SELF-compile that
IS the compiler's own type set, so the emitted table bootstraps naturally and
also serves the compactor.

KEY ENABLER (proven by emit-text-lit): a rodata pointer is INTERCHANGEABLE
with a heap pointer -- emit-text-lit lays a Text into rodata as [len:i64][bytes]
and hands back its address as a normal Text value. So we emit the type-defs
graph into rodata in EXACT heap layout, write internal pointers as relocations
(RodataFixup: rf-poffsets = code patch sites, rf-roffsets = target rodata
offsets, patched at link by collect-rodata-patches with data-vaddr), and a
runtime accessor returns the table as a live `List TypeBinding`. NO deserializer
-- the rodata bytes ARE the object graph. pmap-walk consumes it unchanged.

Heap layouts to replicate (verified in source):
- List X: alloc (count+1)*8; cap@ptr-8 = count; length@ptr+0 = count; elem i @
  ptr+8+i*8. The List pointer points at the LENGTH word (so a rodata list =
  [cap][len][e0..], pointer relocated to rodata-off+8). (emit-list-bivy)
- Record: NO cap word; pointer at alloc-start; fields width-sorted (Integer by
  bounds 1/2/4/8, Bool 1, else 8-byte pointer), offsets via
  cce-byte-offset-and-type. (emit-record / emit-store-record-fields-by-type)
- Sum: heap-boxed [tag:i64@0][field i @ 8+i*8]; nullary too (8B + tag).
- Text: [len:i64@0][bytes@8][pad8]; pointer at len word. (emit-text-lit)
- Name: a record { value : Text } -> one pointer field.

Implementation plan (each brick its own build, keep tree green):
1. PROOF brick: a 0-arg accessor builtin that emits a constant `List Integer`
   ([cap][len][e0..]) into rodata and returns it; self-test asserts list-length
   + list-at round-trip. Validates the List-in-rodata layout + relocation at +8.
   (Layout is inspection-clear from emit-list-bivy / emit-text-lit -- may skip
   the runtime proof and go straight to brick 2 to save build cycles.)
2. Recursive rodata-graph emitter `emit-const-<T>` over the closure {List,
   Text, Name, RecordField, SumCtor, CodexType(19), TypeBinding}: each returns
   the rodata offset of the object it wrote; pointer fields recorded as
   relocations. Emit ONCE at codegen start (st.type-defs available, data-buffer
   empty -> table at a known low offset) and stash the table offset in
   CodegenState. Accessor builtin `__self-type-defs : List TypeBinding` loads
   that offset.
3. Wire into check-escape-invariant: replace scan-deck-dangling with
   `pmap-walk (address-of root) (ConstructedTy <root-type-name>) __self-type-defs
   bivy-origin bivy-hwm fuel` (>0 = escape). Thread each phase's durable ROOT
   value into the metrics/check. Validate: DESUGAR precise == 0 (known clean
   after CL 2699) vs conservative 6; then read SCOPE's real number.
4. Then the copying compactor: a pmap-walk variant that pokes forwarded
   pointers (needs poke-qword -- only poke-32 exists today).

Risk: replicating layouts exactly across the whole graph is corruption-prone;
build/validate brick-by-brick. The self-test harness (gated -EscapeCheck) is
the fast loop. The defs table MUST be sorted by name (lookup-type-binding
bsearch) -- sort st.type-defs (sort-type-bindings) before emitting.

## Open questions

- Table size: st.type-defs for the full self-compile may be large (every type
  in the compiler). Measure the rodata growth; if big, emit only types
  reachable from phase roots (a reachability prune) rather than all.
- Does the existing two-phase streaming pipeline (MM3-REALITY-CHECK, 220→64
  MB) already reclaim across stages in some mode, and how does it relate to
  the CDX-path 149 MB cascade? Reconcile before building a second mechanism.
