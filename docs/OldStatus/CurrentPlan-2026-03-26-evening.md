# Current Plan

**Date**: 2026-03-26 late evening

---

## Where We Stand

All major items from today's sessions are **merged, tested, and verified**. The
repo is clean: one branch (`master`), zero dirty files, zero stale feature
branches. `cam/ring4-cleanup` fully merged and closed.

CCE-native text is complete across all backends including x86-64 native. The
x86-64 backend now stores all strings as CCE bytes internally, converting at
I/O boundaries only (print, serial, REPL input). The whitespace decision is
closed (forward path, no dual mode). Closure escape analysis is shipped. The
bare metal REPL loop is verified under QEMU. The CCE encoding tooling
(`CceTable` single source of truth, `codex encode` CLI, 11 consistency tests)
is on master. Generated-output conflict markers cleaned.

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~5,000 lines |
| Backends | 15 (12 transpilation + IL + RISC-V + WASM + ARM64 + x86-64, including bare metal) |
| Tests | 844 (511 compiler + 134 syntax + 110 repository + 32 core + 23 semantics + 18 LSP + 16 AST) |
| Self-compile time | 279ms median (CCE-native) |
| Fixed point | **Proven** (Stage 2 = Stage 3 at 298,752 chars, CCE-native) |
| Language features | Lambda, fork/await/par/race, Char, CCE-native text, linear closures |
| Codex.OS | 7 KB kernel, Rings 0-4, arena REPL, preemptive multitasking, capability-enforced syscalls |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-26-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## What Got Done (2026-03-26)

### Performance: P2-alt Sorted Binary Search (9.2x speedup)

- Replaced O(n) linear scans with sorted binary search in TypeEnv, Scope,
  UnificationState, and type def map.
- `list-snoc` for O(1) amortized list accumulation across 30+ sites.
- Three new builtins: `text-compare`, `list-insert-at`, `list-snoc`.
- Self-compile: 1,907ms → 208ms. Fixed point proven at 261,175 chars.
- HAMT approach attempted, failed (82% slower), reverted with post-mortem.
  See `docs/reviews/P2-HAMT-REVERT.md`.

### CCE-Native Text — Complete

- All 6 phases shipped. Tier 0: 128 chars, frequency-sorted.
- Self-hosted emitter fully CCE-native: `_Cce` runtime, I/O boundaries,
  escape rewrite, char-literal-based classification.
- Phase 6 cleanup: dropped `is-cce-` prefixes, `CCEClass` → `CharClass`.
- `text-concat-list` builtin: `string.Concat` for batch joins.
- Fixed point proven at 298,752 chars.
- Perf impact: 34% constant-factor overhead, diffuse, no algorithmic regression.
  See `docs/reviews/CCE-PERF-IMPACT.md`.

### CCE Whitespace Decision — Closed

- Identified TAB/CR silent NUL corruption (TAB not in Tier 0 → `\t` produces NUL).
- Five options analyzed (A: boundary normalization, B: evict Cyrillic, C: multi-byte
  Tier 1, D: loud failure, E: dual compilation modes).
- **Decision**: CCE-only, forward path. No dual mode. TAB/CR are hardware birth
  defects from 1963 — boundary normalization on input, explicit Unicode interop
  for programs that need literal tabs. Building for 2999, not 1999.
- Silent NUL fixed: unmapped characters produce `?` not NUL.
- See `docs/Designs/CCE-WHITESPACE-DECISION.md`.

### CCE Encoding Tooling (Agent Linux)

- `CceTable.cs` in `Codex.Core`: single source of truth for the 128-entry table.
  Replaced duplicate tables in `CSharpEmitter.cs` and `CSharpEmitter.Utilities.cs`.
- `codex encode` CLI command: convert between UTF-8 and CCE. Roundtrip verified.
- 11 consistency tests: table bijectivity, roundtrip encoding, classification
  ranges, `GenerateRuntimeSource` consistency, self-hosted emitter table sync.
- Encoding integration design: `docs/Designs/CCE-ENCODING-INTEGRATION.md` —
  Linux gconv modules, .NET EncodingProvider, editor plugins.

### Closure Escape Analysis

- CDX2043 promoted from warning to error. Linear closures enforced.
- Architecture: `CheckLambdaExpr` returns captured set, caller decides policy.
- Three safe patterns: let binding (linear propagation), direct application
  (immediate consumption), naked lambda (error).
- Design doc: `docs/Designs/CLOSURE-ESCAPE-ANALYSIS.md`. Steps 1-3 shipped,
  Step 4 (higher-order linear callbacks) deferred.

### Bare Metal: is-whitespace Fix + Arena REPL

- x86-64 `is-whitespace` register aliasing bug fixed (branch-based rewrite).
- Arena-based REPL loop: heap reset between compilations via saved arena base
  at 0x7010. Infinite REPL — compile, discard arena, compile, repeat.
- QEMU verified: bare metal boot tests pass (first-line extraction for REPL output).
- Review: `docs/reviews/arena-repl-review.md`.

### x86-64 CCE-Native Backend Migration (Cam + Linux review)

- All internal strings now CCE-encoded in `.rodata` (was UTF-8).
- `IRCharLit` emits via `CceTable.UnicharToCce` (was: silently emitting 0).
- Classification intrinsics rewritten for CCE ranges: `is-letter` (13–64),
  `is-digit` (3–12), `is-whitespace` (≤2) — single `sub`/`cmp`/`setcc`
  instead of multi-branch Unicode logic.
- `__itoa` / `__text_to_int` use CCE digit offset (3).
- 128-byte CCE→Unicode + 256-byte Unicode→CCE lookup tables in `.rodata`.
- Print paths (`EmitPrintText`, `EmitPrintTextNoNewline`, `EmitSerialStringFromPtr`)
  do per-byte CCE→Unicode conversion at I/O boundary.
- Input paths (`__read_line`, `__bare_metal_read_serial`) convert Unicode→CCE in-place.
- `EmitPrintNewline` fixed: was emitting CCE byte 0x01 (SOH) via
  `AddRodataString("\n")` instead of literal Unicode 0x0A — root cause of
  24 test failures in the WIP commit.
- 38/38 x86-64 tests green. 844/844 full suite green.
- Merged via `cam/ring4-cleanup` (commits `fcf39e2`, `96ff068`).

### Docs & Design

- Janus reflection: `docs/Designs/CCE-NATIVE-TEXT.md`, "Cam's Think" section.
- Safe mutation principle: `docs/Designs/SAFE-MUTATION.md`.
- Milestones named: MM2 The High Camp, MM3 Summit.

---

## Codex.OS Status

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32-to-64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done (7KB) |
| 2 | Process table (16 slots), preemptive context switch, per-process page tables | Done |
| 3 | Capability-enforced syscalls (SYS_WRITE_SERIAL, SYS_READ_KEY, SYS_GET_TICKS, SYS_EXIT) | Done |
| 4 | Self-hosting compiler on bare metal, TCO, serial I/O, **arena REPL**, **CCE-native text** | REPL loop verified under QEMU, CCE I/O boundaries complete |

---

## What's Next

### Near-term (days)

| Item | Notes |
|------|-------|
| QEMU test: send .codex over serial, verify compilation | First real MM2 validation — compile a program on bare metal |
| Boundary normalization | TAB → spaces, CR → strip in `_Cce.FromUnicode` |
| Remove `\t`/`\r` escape sequences | Or compile-time diagnostic for Tier 0 violations |
| Perf tracking | Automated benchmark, track compound regression |
| CCE Tier 1 multi-byte | Load-bearing for repository's "remembers everything" promise |

### Medium-term (weeks)

| Item | Notes |
|------|-------|
| Higher-order linear callbacks | Step 4 of closure escape analysis — `linear` function types |
| Codex.UI substrate | Semantic primitives, typed themes |
| Capability refinement | Direction, scope, time-boxing in effect syntax |
| Multi-language syntax | Parser per locale, shared AST |

### Long-term

| Item | Notes |
|------|-------|
| Self-hosted compiler compiling itself on bare metal (MM3) | The ultimate fixed-point |
| Codex.OS on real hardware | WHPX (Nut's box), then actual boot device |
| Ring 5+: filesystem, networking | Content-addressed FactStore as the filesystem? |
| Floppy disk image | Boot → compiler → self-compile, all in 1.44 MB |

---

## Process

- **Reference compiler lock lifted** (2026-03-24): `src/` freely modifiable.
- **Session init**: `codex-agent orient` (Cam), `bash tools/linux-session-init.sh` (Linux).
- **Handoff**: `codex-agent handoff push/review/approve/merge`. Always update CurrentPlan.md.
- **Feature branches**: All work goes to feature branches for review. Direct master pushes for docs and single-line fixes only.
- **Four-agent workflow**: Git is the coordination protocol.
  - Windows (Copilot/VS): builds features, reviews code
  - Linux (Claude/sandbox): tests on real hardware/emulators, finds bugs by tracing, reviews
  - Cam (Claude Code CLI, 1M Opus): fast iteration, parallel work, GDB debugging
  - Nut (Copilot/VS2026, garage box): hardware lab, OS dev, phone flash
