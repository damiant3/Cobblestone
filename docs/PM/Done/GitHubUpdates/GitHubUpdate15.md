# GitHub Update 15 -- CL 1812 to CL 1845 (2026-05-19)

Previous update: CL 1811 (GitHubUpdate14).
This update: CL 1845.

## Headline: lookup-expr-type Out-of-Bounds Read Fixed

A non-short-circuit `&` operator in `lookup-expr-type` (Unifier.codex)
caused `list-at entries pos` to read one element past the sorted
expr-types list when a binary search returned "not found" (`pos == len`).
In standalone compilation the memory past the list was typically zeros,
producing a benign `ErrorTy` fallback. In REPL batch mode after a large
predecessor compilation, the stale heap data at that address contained
type pointers from the previous compilation's reclaimed heap, causing
a GPF when the type walker (`occurs-in` / `lookup-expr-type`) followed
the dangling pointer.

**Root cause:** `if pos < len & (list-at entries pos).key == k` -- the
`&` operator is boolean AND, not short-circuit. Both sides evaluated
regardless of `pos < len`.

**Fix:** Split into nested `if pos < len then if ... key == k`.

**Impact:** Fixes the handler-nested batch GPF (backlog item #3, open
since the test was written). Also the likely root cause of the plug
crash documented in `docs/Test/PLUG-CRASH-INVESTIGATION.md` -- same
function, same code path, same class of stale-heap read.

**Sweep:** 105/105 pass, 0 fail, 52 skip. Up from 103/105 (handler-nested
promoted from `.fatal` to passing; db-test/sort-test sidecars updated).

**Seed:** 2,165,928 bytes. Hard fixed point verified (stage1 == stage2).

## Other Changes

### Mutable Records (CL 1838, copy-up from MutableRecords branch)

The `mutable` keyword for record types landed in the compiler. Mutable
records support in-place field assignment (`r.field = expr`) enforced
by the type checker: CDX2060 rejects field assignment on immutable
records, CDX2061/2062 are planned for linearity tracking. Phase 3
(`freeze` to convert mutable to immutable) is pending. Primary use
case: game engines and OS schedulers where copy-on-update has quadratic
overhead on bare metal with no GC.

### Codex.Spark -- 3D Modeling Framework (CL 1715+)

A complete 3D modeling framework built in Codex and running on bare
metal via codex-vm: meshes, textures, UV mapping, armatures with
inverse kinematics, weight painting, particle systems, material
editor with PBR properties, asset browser, render passes, and an
interactive app shell with real-time animated demo. Built in a single
day session.

### CodexMagic -- Card Game Engine (ongoing)

A collectible card game rules engine modeled on early Magic: The
Gathering (Revised through Onslaught). Two-player duel with core card
types, mana system, turn phases, LIFO spell stack, combat (attackers,
blockers, damage with first strike / trample / deathtouch / lifelink),
zones (library, hand, battlefield, graveyard, stack, exile), eight
state-based actions, and keyword abilities. Engine is a pure
deterministic state machine. Design docs in `docs/Designs/Active/CodexMagic/`.

### Codex.Data -- Database Modules (CL 1842+)

Relational database modules under `apps/data/`: Page (8 KB slotted
pages), Row (tuple storage), BTreeIndex, BufferPool, Catalog, Schema,
Executor (volcano-style), Optimizer, LockManager, Deadlock detection,
MVCC, Server (TCP protocol), Security, SortMerge, Heap, Protocol.
Designed per `docs/Designs/Active/Projects/CODEX-DB.md`.

### Web Dashboard for Games

A lightweight HTTP dashboard serving interactive card game and classic
game interfaces (Klondike, Sudoku, Tic-Tac-Toe, War, Yahtzee) via
`tools/web/`. Styling matches the dark theme from `codex.works/Http.codex`.

### Documentation Reorganization

Split `docs/OperatorsManual.md` into two documents:
- **OperatorsManual.md** -- build process, test harness, VM configuration,
  seed management, debugging with GDB
- **ArchitectsSketchbook.md** -- memory layout, register conventions,
  deck/bivy allocators, phase maps, platform constraints

### Dev Stream Merge-Down

Merged 3 compiler files (X86_64Chapter, X86_64State, opening) and
build scripts from main into the CodexMagic dev stream, resolving a
stale-seed issue where the branch's source had diverged from the seed
binary.
