# GitHub Update 13 -- CL 1300 to CL 1518 (2026-05-16)

Previous update: CL 1300 (GitHubUpdate12).
This update: CL 1518.

One week, ~220 changes across main and DEV_2GB_SYNTAX. The headline
is that Codex has a new syntax: `&` replaces `++` for text/list
concatenation, and multi-parameter function types use commas
(`Integer, Integer -> Integer`) instead of chained arrows. The
entire codebase -- compiler, forewords, kernel, OS, works, and 245
test files -- has been converted. A new 2GB seed is in progress.

## New Syntax (Cam CL 1315, Gollum CLs 1481-1499, 1505)

The language now uses `&` as the concatenation operator (text and
list append). The old `++` operator is accepted but emits a CDX5001
deprecation warning. Function types with multiple parameters use
commas: `add : Integer, Integer -> Integer`. The last `->` separates
parameters from return type. Higher-order parameters stay in parens:
`map : (a -> b), List a -> List b`.

The conversion touched every source file in the project:
- Compiler source (`codex/`): 52 files
- Foreword library: 85 files
- Kernel + OS: 100+ files
- Works: 50+ files
- Tests: 245 files

## 2GB Seed Branch (DEV_2GB_SYNTAX)

Branched from main at CL 1334. The compiler now targets a 2GB
address space (up from the previous ~1.8 MB binary size). This
eliminates the list-literal ceiling that required `&`-splitting
large builtin lists on main. All main CLs 1335-1385 have been
merged down or confirmed subsumed.

## New Foreword Abstractions (Gollum, CL 1513)

Three new library modules to reduce boilerplate across the codebase:

- **Iterate** -- `list-map-generic`, `list-filter-generic`,
  `list-find-index`, `list-any-generic`, `list-all-generic`,
  `list-take-generic`, `list-drop-generic`, `list-zip-with-generic`,
  `list-count-where`. Polymorphic versions of what Pipeline provides
  only for Integer.
- **TextScan** -- `text-fold-indexed`, `text-fold-back`,
  `text-map-chars`, `text-find-char`, `text-match-at`, `text-join`,
  `text-repeat`, `text-pad-left`.
- **Parse** -- `parse-decimal`, `parse-hex`, `parse-decimal-full`,
  `parse-hex-full`, `is-hex-digit`, `hex-digit-value`. CCE-aware
  numeric parsing via `to-unicode` conversion.

## Performance Fix: pipe-unique O(n log n) (Gollum, CL 1514)

`pipe-unique` was O(n²) -- linear search of accumulator per element.
Now uses `pipe-sort` + adjacent dedup for O(n log n). Verified with
test coverage (`pipe-unique-test`).

## Test Harness Overhaul (Gollum, CL 1505)

The batch test harness had a critical bug: when a test crashed the
compiler (GPF during compilation), the persistent QEMU session would
halt. Every subsequent test in that batch would wait 120 seconds
against a dead machine before timing out.

Fixes:
- Detect `!EXC` in compile output → restart QEMU immediately.
- Detect timeout → restart QEMU immediately.
- New `.fatal` sidecar category for tests that kill QEMU at runtime
  (exception demos), skipped by default.
- Per-test timing in test.log (`name done 2.4s pcore=N`).
- Reduced compile timeout from 120s to 30s (max observed: 14s).
- Total test battery time: ~3 minutes (was 60+ minutes with the
  poisoning bug).

## Dead Code + DRY Cleanup (Gollum, CLs 1511, 1514)

Removed dead functions (`html-li`, `html-ul`, `list-subdirs-with-pattern`,
`is-codex-file`). Replaced 4 redundant local `starts-with`
implementations with the canonical `text-starts-with` from StringUtils
(ShellClarifier, History, TabComplete). Replaced char-by-char
`tab-shared-loop` with `substring` in TabComplete.

## DevConsole Stub Views Wired (Gollum, CL 1512)

The DevConsole's `ModeEdit`, `ModeVerify`, and `ModeAnnotations`
modes previously hit a `"(view not implemented)"` catch-all. ModeEdit
now renders the editor state (visible lines + status) or an empty-file
message. ModeVerify and ModeAnnotations show their submenus.

## Compiler Work (Main, CLs 1335-1351)

- **Structural sum type equality** (Nib, CL 1335) -- field-by-field
  comparison for constructors with fields.
- **Address map coordination** (Nib, CL 1337) -- sort addresses for
  deterministic layout.
- **Helper file split** (Nib, CL 1338) -- X86_64Helpers split into
  X86_64IO, X86_64IPCHelpers, X86_64ListHelpers, X86_64ProcessHelpers.
- **Builtin list split** (Nib, CL 1341) -- `&` split workaround for
  list-literal ceiling.
- **Text buffer doubled** (Nib, CL 1342) -- 2MB → 4MB.
- **Syntax conversion** (Cam, CL 1343) -- full compiler source
  converted to comma-separated type syntax.
- **Plug fixes** (Cam, CLs 1345-1346) -- rename collisions, extract
  PlugTypes.
- **BuildSettings centralization** (Nib, CL 1348) -- compiler
  constants into one chapter.
- **Ed25519 constant-time** (Cam, CL 1349) -- fix 7 timing leaks in
  the signing code.
- **Exception handler stack dump** (Nib, CLs 1350-1351) -- save RSP to
  R11, reset to stack-top before printing. Serial drain wired in.

## Documentation (Gollum, CLs 1516, 1518)

DevelopersGuide updated for new syntax (comma params, `&` operator,
`++` deprecated). 9 active design docs updated to use `&` in code
examples. Corrected stale O(n²) claim for string concatenation
(`__str_concat` fast-path is O(1) when accumulator is at heap top).

## Foreword: SkipListText (Reek, CL 1357)

General-purpose skip-list text data structure, providing O(log n)
lookup for sorted text collections. Used by the compiler's builtin
name resolver.

## Seed Rebuild (Gollum + Reek, CL 1526)

Combined memory layout bump (serial ring buffer 0x300000→0x500000,
heap start 0x400000→0x600000) with new builtins and parser fix.
Hard fixed point confirmed: stage1 CDX = stage2 CDX, text
round-trip byte-identical, semantic equivalence verified, 171/171
test battery pass.

Seed: 2,109,248 bytes.
SHA256: 4BA66C6DF21A9A2108970F4095A90D10E1BB3D663CCB2F7D7EE856C432D13DDC

## Parser Fix: Match Arm Column Gate (Gollum, CL 1526)

`parse-match-branch-body` now uses `parse-expr-col st3 col` instead
of ungated `parse-expr st3`. Match arm bodies no longer absorb
trailing binary operators that belong to the outer expression. This
closes the long-standing `[list] & when ... & [list]` mis-parse bug
(documented in KNOWN-CONDITIONS since CL 1222). De-workarounds
applied to 5 files that previously hoisted `when` expressions to
avoid the bug.

## Editor Features (Gollum, CLs 1521-1523)

- Find / replace (editor-find, editor-replace, editor-replace-all)
- Undo stack (UndoStack, undo-push, editor-undo)
- Go-to-line (editor-goto-line)
- Full key dispatch (arrows, pgup/dn, home/end, ctrl+s/f/g/z)
- Delete forward with line-merge
- Multi-file buffer list (BufferList, buffers-open/next/prev/close)

## Append-Only Mutation Log (Gollum, CL 1524)

CRC-framed append-only log for annotation operations (Gap 9 from
CurrentPlan). Each entry carries sequence number, operation type,
target, content, timestamp, depot-CL, and SHA-256 CRC. Supports
replay to rebuild AnnotationStore, staleness queries, and
incremental "since seq N" queries.

## Numbers

- Compiler: 52 files, ~21,000 lines of Codex.
- Foreword library: 88 chapters across `codex.foreword/` + sub-quires.
- Test samples: 189 (171 pass, 0 fail, 18 skipped).
- Test battery: ~3 minutes at `-Jobs 4` on 12th gen i7.
- Seed: 2,109,248 bytes, hard fixed point on DEV_2GB_SYNTAX.
