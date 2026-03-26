# Current Plan

**Date**: 2026-03-26 night session (Cam)

---

## Where We Stand

Major feature push complete. Three feature branches on origin awaiting
Linux review and merge. All tests green locally. Agent Linux is
concurrently running QEMU verification and found 2 test issues under
analysis.

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~5,000 lines |
| Backends | 15 (12 transpilation + IL + RISC-V + WASM + ARM64 + x86-64, including bare metal) |
| Tests | ~870 (517 compiler + 139 syntax + 110 repository + 61 core + 23 semantics + 18 LSP + 16 AST) |
| Self-compile time | 279ms median (CCE-native) |
| Fixed point | **Proven** (Stage 2 = Stage 3 at 298,752 chars, CCE-native) |
| Language features | Lambda, fork/await/par/race, Char, CCE-native text, linear closures, **linear function types**, **CCE Tier 1** |
| Codex.OS | 7 KB kernel, Rings 0-4, arena REPL, preemptive multitasking, capability-enforced syscalls |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-26-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## What Got Done (2026-03-26 night — Cam session)

### MM2 Builtins for x86-64 Bare Metal

- 6 runtime helpers in `X86_64CodeGen.cs`: `text-compare`, `list-snoc`,
  `list-insert-at`, `list-contains`, `text-concat-list`, `text-split`.
- Load-bearing for self-hosted compiler on bare metal (P2-alt sorted
  binary search, emitter text joins, tokenizer splits).
- Kernel size limit bumped 8KB→10KB.

### Boundary Normalization + Perf Tracking

- `CceTable.NormalizeUnicode`: TAB→two spaces, CR→stripped.
- `Encode()` auto-normalizes before encoding.
- Lexer.codex: `\t`→two spaces, `\r`→empty string in escape processing.
- `bench-baseline.json`: 310.25ms median baseline.
- `--bench-check` mode: 3+10 protocol, compares against baseline, exits 1
  if regression exceeds 10%.

### CCE Tier 0 Escape Diagnostics

- CDX0005: `\t` escape is not valid in CCE — hard error in reference compiler.
  Recovery: two spaces (text) or space char (char literal).
- CDX0006: `\r` escape is not valid in CCE — hard error.
  Recovery: stripped (text) or newline (char literal).
- All three escape sites updated: plain text, char literal, interpolated fragment.
- Self-hosted lexer: char literal escapes remapped (`\t`→32, `\r`→10).
- 5 new syntax tests (139 total).

### CCE Tier 1 Multi-Byte Encoding

- Self-synchronizing 2-byte framing: `110xxxxx 10xxxxxx` (same as UTF-8).
- 2,048 code point space across 8 script blocks.
- **Latin Extended** (0x000-0x07F): 128 entries — ß, ã, å, æ, î, ï, ð,
  Latin Extended-A (š, ž, č, ć, đ, ł, ń, ś...), all uppercase equivalents,
  Latin-1 symbols (°, ±, ², ³, µ, ©, ®, «, », ¿, ¡...).
- **Cyrillic Extended** (0x080-0x0FF): 77 entries — remaining Russian
  lowercase/uppercase, Ukrainian, Serbian, Belarusian, Macedonian.
- Reserved blocks: Greek, Arabic+Devanagari, CJK top-512, Japanese, Korean.
- `Encode()`/`Decode()` handle mixed Tier 0+1 streams (output length may
  differ from input for Tier 1 characters).
- `TierOf()` helper for byte classification.
- `GenerateRuntimeSource()` emits Tier 1 tables as sparse dictionaries
  with multi-byte aware `FromUnicode`/`ToUnicode`.
- `UnicharToCce`/`CceToUnichar` remain Tier 0 only (bare metal path).
- 16 new core tests (61 total): bijectivity, no Tier 0/1 overlap, roundtrip
  Latin/Cyrillic/mixed, byte framing validation, self-synchronization,
  orphan/truncated byte handling.

### Closure Escape Analysis Step 4: Linear Function Types

- Higher-order linear callbacks now supported: `linear (A -> B)` parameter
  types guarantee exactly-once consumption by the callee.
- New `ApplyExpr` case in `LinearityChecker`: when a lambda argument
  captures linear vars AND the function's parameter type is `LinearType`,
  the closure is safe (consumed via callee guarantee, no CDX2043).
- `TryResolveExprType`: resolves expression types through `NameExpr`
  lookups and curried `ApplyExpr` chains.
- Handles curried calls: `with-resource "path" (\h -> close-file h)`.
- 6 new linearity tests (25 total): linear callback used once/unused/twice,
  linear closure to linear param (ok), to non-linear param (CDX2043),
  curried linear param (ok).

### Tier 1 Design Spike (prior session, merged)

- 8 tests validating 2-byte `110xxxxx 10xxxxxx` format.
- Roundtrip all 2048 code points, self-synchronization, no Tier 0 overlap,
  script block identification, mixed Tier 0+1 streams.

---

## Feature Branches on Origin

| Branch | Status | Depends on |
|--------|--------|------------|
| `cam/mm2-builtins-boundary-normalization-perf-tier1` | Awaiting Linux review | — |
| `cam/tier0-escape-diagnostics` | Awaiting Linux review | First branch |
| `cam/cce-tier1-multibyte` | Awaiting Linux review | First two |

Merge order: first → second → third.

---

## Codex.OS Status

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32-to-64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done (7KB) |
| 2 | Process table (16 slots), preemptive context switch, per-process page tables | Done |
| 3 | Capability-enforced syscalls (SYS_WRITE_SERIAL, SYS_READ_KEY, SYS_GET_TICKS, SYS_EXIT) | Done |
| 4 | Self-hosting compiler on bare metal, TCO, serial I/O, **arena REPL**, **CCE-native text** | REPL loop verified under QEMU, CCE I/O boundaries complete, **6 MM2 builtins shipped** |

---

## What's Next

### Near-term (days)

| Item | Notes |
|------|-------|
| QEMU test: send .codex over serial, verify compilation | Linux running — first real MM2 validation |
| Perf tracking automation | Wire --bench-check into CI or pre-commit |
| CJK/Japanese/Korean Tier 1 blocks | Reserved in infrastructure, need character allocation |

### Medium-term (weeks)

| Item | Notes |
|------|-------|
| Codex.UI substrate | Semantic primitives, typed themes |
| Capability refinement | Direction, scope, time-boxing in effect syntax |
| Multi-language syntax | Parser per locale, shared AST |
| Tier 2/3 multi-byte | 3-byte and 4-byte encodings for full Unicode coverage |

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
