# Repository Restructure + Compiler Scoping Fix

## Problem Statement

Three related issues surfaced when attempting a full build at head:

### 1. Broken mainline: seed cannot compile its own source

CL 1569 renamed `text-buf-size` → `code-buffer-size` and
`rodata-buf-size` → `data-buffer-size` in the compiler source, but the
submitted seed binary is byte-identical to the CL 1563 seed (same MD5).
The seed doesn't know about the new names, so it can't compile head.

### 2. Concat ordering breaks repl-mode self-compilation

`concat-codex-self.ps1` concatenates the compiler's 59 source files for
self-compilation. It sorts files alphabetically within each subdirectory
and emits root files (`opening.codex`) before subdirectories. This
produces an ordering where:

- `opening.codex` (the orchestrator that calls everything) appears
  before the definitions it references
- Within subdirectories, files that define shared utilities
  (`X86_64Encoder`, `X86_64State`) sort after files that use them
  (`CdxWriter`, `CodexEmitter`)
- Across subdirectories, `Emit/` sorts before `Syntax/`, but Emit
  chapters reference Syntax definitions like `is-literal` and
  `parse-type`

In repl mode (which `build.ps1` uses via `-Repl`), scope accumulates
forward — chapter N can see definitions from chapters 1..N-1 but not
N+1. So ordering matters.

### 3. Structural inconsistency: the compiler uses subdirectories, nothing else does

The compiler (`codex/`) organizes its 59 files into 7 subdirectories:
`Ast/`, `Core/`, `Emit/`, `IR/`, `Semantics/`, `Syntax/`, `Types/`.
The concat script handles this with quire prefixes
(`Core--Build Settings`). No other quire uses subdirectories.

Meanwhile, the top-level directory has exploded to **17 quire
directories** (`codex.foreword`, `codex.foreword.ai`,
`codex.foreword.game`, `codex.os`, `codex.os.net`, `codex.os.trust`,
`codex.games`, `codex.magic`, etc.). Each is a flat bag of `.codex`
files. The naming convention `codex.foreword.game` implies hierarchy
but the filesystem doesn't express it — they're all siblings.

## Current Repository Layout

```
D:\Projects\NewRepository\
├── codex\                     # 59 files in 7 subdirs (compiler)
│   ├── opening.codex
│   ├── Ast\        (2 files)
│   ├── Core\       (17 files)
│   ├── Emit\       (23 files)
│   ├── IR\         (4 files)
│   ├── Semantics\  (2 files)
│   ├── Syntax\     (6 files)
│   └── Types\      (6 files)
├── codex.foreword\            # 86 files (core library)
├── codex.foreword.ai\         # 19 files
├── codex.foreword.compress\   # 8 files
├── codex.foreword.encode\     # 32 files
├── codex.foreword.game\       # 26 files
├── codex.foreword.math\       # 12 files
├── codex.foreword.signal\     # 14 files
├── codex.foreword.sim\        # 7 files
├── codex.foreword.ui\         # 28 files
├── codex.games\               # 21 files
├── codex.kernel\              # 16 files
├── codex.magic\               # 20 files
├── codex.os\                  # 4 files
├── codex.os.dev\              # 5 files
├── codex.os.net\              # 15 files
├── codex.os.observe\          # 7 files
├── codex.os.replay\           # 3 files
├── codex.os.sched\            # 6 files
├── codex.os.trust\            # 11 files
├── codex.os.verify\           # 5 files
├── codex.test\                # 127+ files
├── codex.works\               # 53 files
├── codex.build\               # build scripts
├── codex.annotations\
├── docs\
├── old\
├── plugs\
├── seed\
└── tools\
```

17 quire directories at the top level. The `codex.foreword.*` family
alone is 9 directories. This is getting unwieldy and the flat structure
fights against the logical hierarchy.

## Proposal

### Phase 1: Restructure into a clean hierarchy

Everything lives under four top-level peers: `annotations`, `apps`,
`build`, `codex`. The dot-separated directory explosion is replaced by
real subdirectories. Plugs move into `codex/plugs`.

```
annotations\                     # unchanged
apps\                            # was: codex.works + codex.games + codex.magic
│   ├── games\
│   │   ├── classic\             # was: codex.games\ (21 files)
│   │   └── magic\               # was: codex.magic\ (20 files)
│   └── works\                   # was: codex.works\ (53 files)
build\                           # was: codex.build\
codex\                           # all Codex source under one root
│   ├── compiler\                # was: codex\ (59 files)
│   │   ├── opening.codex
│   │   ├── Ast\
│   │   ├── Core\
│   │   ├── Emit\
│   │   ├── IR\
│   │   ├── Semantics\
│   │   ├── Syntax\
│   │   └── Types\
│   ├── foreword\                # was: codex.foreword + codex.foreword.*
│   │   ├── core\                # was: codex.foreword\ (86 files)
│   │   ├── ai\                  # was: codex.foreword.ai\ (19 files)
│   │   ├── compress\            # was: codex.foreword.compress\ (8 files)
│   │   ├── encode\              # was: codex.foreword.encode\ (32 files)
│   │   ├── game\                # was: codex.foreword.game\ (26 files)
│   │   ├── math\                # was: codex.foreword.math\ (12 files)
│   │   ├── signal\              # was: codex.foreword.signal\ (14 files)
│   │   ├── sim\                 # was: codex.foreword.sim\ (7 files)
│   │   └── ui\                  # was: codex.foreword.ui\ (28 files)
│   ├── os\                      # was: codex.os + codex.os.* + codex.kernel
│   │   ├── core\                # was: codex.os\ (4 files)
│   │   ├── dev\                 # was: codex.os.dev\ (5 files)
│   │   ├── kernel\              # was: codex.kernel\ (16 files)
│   │   ├── net\                 # was: codex.os.net\ (15 files)
│   │   ├── observe\             # was: codex.os.observe\ (7 files)
│   │   ├── replay\              # was: codex.os.replay\ (3 files)
│   │   ├── sched\               # was: codex.os.sched\ (6 files)
│   │   ├── trust\               # was: codex.os.trust\ (11 files)
│   │   └── verify\              # was: codex.os.verify\ (5 files)
│   ├── plugs\                   # was: plugs\ (transpiler backends)
│   │   ├── ada\
│   │   ├── babbage\
│   │   ├── csharp\
│   │   ├── cobol\
│   │   ├── common\
│   │   ├── fortran\
│   │   ├── javascript\
│   │   ├── python\
│   │   └── rust\
│   └── test\                    # was: codex.test\
docs\                            # unchanged
old\                             # unchanged
seed\                            # unchanged
tools\                           # unchanged
```

**Top-level reduction**: 31 directories -> 7 (`annotations`, `apps`,
`build`, `codex`, `docs`, `old`, `seed`, `tools`).

### Phase 1b: New `cites` syntax — explicit quire path

The current `cites` syntax is ambiguous. `cites Foreword chapter Sort`
works only because there's a flat lookup table mapping `Foreword` to a
single directory. With the new hierarchy, a quire name like `Foreword`
is a family with sub-quires (`core`, `ai`, `game`, etc.). The chapter
`Sort` lives in `core`, but the old syntax doesn't say that.

**New syntax**:

```
cites Foreword quire Core chapter Sort
cites Foreword quire Game chapter Klondike
cites OS quire Net chapter Tcp
cites OS quire Kernel chapter DiskFacts
cites Apps quire Works chapter WebServer
```

The grammar is: `cites <family> quire <sub-quire> chapter <name>`.

- **Family** maps to the second level: `Foreword` -> `codex/foreword`,
  `OS` -> `codex/os`, `Apps` -> `apps`, `Compiler` -> `codex/compiler`.
- **Sub-quire** maps to the leaf directory within the family: `Core` ->
  `codex/foreword/core`, `Net` -> `codex/os/net`.
- **Chapter** is the chapter name inside the file, as before.

| Old syntax | New syntax | Resolves to |
|-----------|-----------|-------------|
| `cites Foreword chapter Sort` | `cites Foreword quire Core chapter Sort` | `codex/foreword/core/Sort.codex` |
| `cites Game chapter Klondike` | `cites Foreword quire Game chapter Klondike` | `codex/foreword/game/Klondike.codex` |
| `cites Net chapter Tcp` | `cites OS quire Net chapter Tcp` | `codex/os/net/Tcp.codex` |
| `cites Kernel chapter DiskFacts` | `cites OS quire Kernel chapter DiskFacts` | `codex/os/kernel/DiskFacts.codex` |
| `cites Works chapter Http` | `cites Apps quire Works chapter Http` | `apps/works/Http.codex` |
| `cites Magic chapter Engine` | `cites Apps quire Magic chapter Engine` | `apps/games/magic/Engine.codex` |

For families with no sub-quires (like `Compiler`), the quire keyword
is omitted or the family name doubles as the quire:

```
cites Compiler chapter Phase Allocator
```

**Backwards compatibility**: The old single-name syntax
(`cites Foreword chapter X`) can be supported during migration by
treating it as sugar for the most common sub-quire (e.g., `Foreword`
implies `quire Core`). This lets us migrate incrementally rather than
rewriting all 400+ cites in one CL. The old syntax is deprecated and
the compiler emits a CDX5002 warning. Once all cites are updated, the
old form is removed.

**Why this matters**: The old syntax required a global flat map from
quire name to directory. With 20+ leaf directories, name collisions
become likely (e.g., `Core` could mean `codex/foreword/core` or
`codex/compiler/Core` or `codex/os/core`). The new syntax eliminates
ambiguity by making the path explicit in the source.

**Migration path**: Perforce `p4 move` preserves file history. The
`$QuireDirs` map in `test-compile.ps1` and `test-compile-batch.ps1`
becomes a two-level map keyed by `(family, quire)` instead of a flat
quire name. `concat-codex-self.ps1` changes `$CodexDir` to
`codex/compiler`. The `build/` rename from `codex.build/` requires
updating every script that dot-sources `vm-config.ps1`. Apps and plugs
get full subdirectory quire resolution — each leaf dir under `apps/`
and `codex/plugs/` is a quire.

### Phase 2: Fix compiler scoping for self-compilation

Two changes needed in the compiler and build tooling:

#### 2a. Concat dependency ordering

`concat-codex-self.ps1` must emit chapters in dependency order. The
current alphabetical sort is wrong. The fix (partially implemented on
the current default changelist):

1. **Subdirectories before root** — `opening.codex` is the top-level
   orchestrator and must come last.
2. **Directory order follows the dependency graph** — hardcoded:
   `Core → Ast → Syntax → Types → Semantics → IR → Emit`.
3. **Topological sort within each directory** — files that declare
   `cites` dependencies are placed after the files they cite.

This is sufficient for non-repl mode compilation. For repl mode, the
compiler also needs:

#### 2b. Compiler: scope accumulation across repl rounds

In repl mode, the compiler currently resets scope on compilation
failure. If a foreword chapter fails to parse (e.g., `CDX1000` token
error), its definitions don't enter scope, and every subsequent chapter
that references those definitions also fails. This cascading failure
makes the build fragile.

**Proposed change**: In repl mode, scope should be **additive across
chapters regardless of compilation outcome**. Successfully parsed
definitions should enter scope even if later phases (type checking,
codegen) fail for that chapter. The repl should report errors per
chapter but continue accumulating the symbol table.

This matches the mental model: repl mode is "feed me chapters one at a
time, I'll compile each and keep going." The current behavior
("one failure poisons all subsequent chapters") makes repl-mode
self-compilation order-dependent and brittle.

#### 2c. Compiler: cross-chapter name visibility

The compiler's name resolver treats each chapter as an independent
compilation unit in repl mode. Definitions from chapter N are visible
to chapter N+1 only if chapter N compiled successfully.

For self-compilation, the compiler needs **all** definitions from **all**
preceding chapters visible, regardless of whether they came from a
foreword, a type definition chapter, or a codegen chapter. The concat
ordering (Phase 2a) handles the "preceding" constraint; the scope
accumulation fix (Phase 2b) handles the "regardless of failure"
constraint.

### Phase 3: Rebuild the bootstrap chain

Once scoping and concat are fixed:

1. Use the CL 1563 seed (last known-good) with the fixed concat to
   compile the CL 1563 source → stage1.cdx
2. Use stage1.cdx to compile the CL 1569 source (with the
   `code-buffer-size` rename) → stage2.cdx
3. Use stage2.cdx to compile head → stage3.cdx
4. Verify stage3.cdx compiles head again → stage4.cdx
5. SHA-256 of stage3 must equal stage4 (fixed point)
6. Submit stage4 as the new seed

## Risk Assessment

**Memory**: The restructure is a file move, no new allocations. The
scoping fix adds a few bytes of state to the repl symbol table
(keeping definitions from failed chapters). No heap blowup risk.

**Time complexity**: Topological sort in the concat is O(N + E) where
N = files in a directory (max ~23 in Emit) and E = cite edges.
Negligible.

**Breaking changes**: Every `cites` statement in every `.codex` file
must be updated to the new `family quire sub-quire chapter name`
syntax. This is ~400+ lines across the codebase. A mechanical
find-and-replace handles most of it (the old-to-new mapping is
deterministic). The compiler supports the old syntax with a deprecation
warning during migration.

Other changes:

- `$QuireDirs` map in `test-compile.ps1` and `test-compile-batch.ps1`
  becomes a two-level `(family, quire)` -> path map
- `concat-codex-self.ps1` — `$CodexDir` -> `codex/compiler`,
  `$ForewordDir` -> `codex/foreword/core`
- `build/` rename — every script that dot-sources `vm-config.ps1`
  updates from `codex.build` to `build`; plugs scripts update their
  relative paths (now under `codex/plugs/`)
- `build/build.ps1` references like `codex.test\factorial.codex`
  become `codex\test\factorial.codex`
- CLAUDE.md paths in rules and examples

**Perforce**: `p4 move` for every file. One large CL. History is
preserved. Agent workspaces (`BigWhite_Codex_cam`, etc.) need a
`p4 sync -f` after the move lands.

## Priority

1. **Fix the concat ordering** — unblocks the build immediately
2. **Fix repl scope accumulation** — makes the build robust
3. **Rebuild the seed** — restores the bootstrap chain
4. **Restructure directories** — do it right after the seed is green,
   while the codebase is at a natural boundary; the longer we wait
   the more files accumulate in the flat layout and the bigger the
   move CL becomes

## Open Questions

- Should `apps/games` be two sub-quires (`classic`, `magic`) or should
  each game collection be its own family? Two sub-quires keeps the
  tree shallow. Recommendation: sub-quires under `Apps` family, with
  `Games` as an intermediate grouping in the filesystem but not in
  the `cites` syntax (`cites Apps quire Classic chapter TicTacToe`).
- Should the compiler's internal subdirectories (`Ast`, `Core`, `Emit`,
  etc.) also adopt the `quire` keyword? Currently the compiler's
  internal cites use `cites Codex chapter X` which is already
  ambiguous. Moving to `cites Compiler quire Core chapter Phase`
  would be consistent. Recommendation: yes, do it as part of the
  migration.
- The `tools/` directory contains `codex-vm.c`, `status-server.ps1`,
  etc. Should it move under `build/tools`? It's not Codex source so
  it doesn't belong under `codex/`. Recommendation: keep `tools/`
  at top level — it contains binaries and host utilities, not Codex
  language source.
