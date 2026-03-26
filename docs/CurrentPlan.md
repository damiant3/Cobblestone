# Current Plan

**Date**: 2026-03-26 (Cam session — CCE complete)

---

## Where We Stand

CCE (Codex Character Encoding) is **complete**. The compiler is fully self-hosting
with CCE-native text: Char = CCE byte, Text = CCE string, Unicode only at I/O
boundaries. Fixed point proven at 298,328 chars.

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~5,000 lines |
| Backends | 15 (12 transpilation + IL + RISC-V + WASM + ARM64 + x86-64, including bare metal) |
| Tests | 915 (507 compiler + 134 syntax + 110 repository + 86 toolkit + 23 semantics + 21 core + 18 LSP + 16 AST) |
| Self-compile time | 208ms median (9.2x faster than pre-optimization) |
| Fixed point | **Proven** (Stage 2 = Stage 3 at 298,328 chars, CCE-native) |
| Language features | Lambda expressions, fork/await/par/race, Char type, CCE-native text |
| Codex.OS | 7 KB kernel, Rings 0-4, preemptive multitasking, capability-enforced syscalls |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-24-peak3-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## What Got Done (2026-03-26 Cam session)

### CCE-Native Text — All 6 Phases Complete

Branch: `cam/cce-native-text`. Design: `docs/Designs/CCE-NATIVE-TEXT.md`.

**Tier 0 revision** — corpus was prose-only, missing syntax chars:
- Whitespace trimmed 8→3 (NUL, LF, Space — no typewriter legacy)
- 9 syntax chars added: `| [ ] { } < > ~ `` `
- 4 chars demoted to Tier 1: ì, î, ß, г (lowest global frequency)
- Punctuation organized: prose (11) + operators (5) + syntax (13) = 29

**New Tier 0 layout (128):**

| Range | Category | Count |
|-------|----------|-------|
| 0-2 | Whitespace | 3 |
| 3-12 | Digits | 10 |
| 13-38 | Lowercase | 26 |
| 39-64 | Uppercase | 26 |
| 65-93 | Punctuation | 29 |
| 94-112 | Accented | 19 |
| 113-127 | Cyrillic | 15 |

**Self-hosted emitter CCE-native:**
- `_Cce` runtime class generation (FromUnicode/ToUnicode)
- All I/O builtins wrapped with CCE↔Unicode conversion
- `is-letter`/`is-digit`/`is-whitespace` use CCE range checks (not Unicode APIs)
- `escape-text` rewritten as per-character `EscapeCceString` (handles `\uXXXX`)
- `show`/`integer-to-text`/`text-to-integer` use `_Cce` conversion

**Phase 6 cleanup:**
- `is-cce-*` → `is-*`, `CCEClass` → `CharClass`, etc.
- Lexer escape processing uses char literals instead of hardcoded Unicode code points

**Design philosophy**: All dependencies shallow. Encoding carries meaning only.
Typographic/legacy concerns (CR, Tab, NBSP, hair space) belong at the I/O boundary.

### Previous sessions (on master)

- Performance P2-alt: sorted binary search + list-snoc (9.2x speedup)
- P4 string.Concat flattening
- Char type across all 16 backends

---

## Codex.OS Status

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32-to-64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done (**optimized: 7KB**) |
| 2 | Process table (16 slots), preemptive context switch, per-process page tables | Done |
| 3 | Capability-enforced syscalls (SYS_WRITE_SERIAL, SYS_READ_KEY, SYS_GET_TICKS, SYS_EXIT) | Done |
| 4 | Self-hosting compiler on bare metal, TCO, serial I/O | Serial REPL works for short programs |

---

## What Remains

### Near-term (days)

| Item | Blocked on | Who |
|------|-----------|-----|
| Merge `cam/cce-native-text` to master | Review | Any agent |
| Closure escape analysis | CDX2043 to error | Any agent |
| Phone flash | Bootloader signature issue | Human |

### Completed

| Item | Status |
|------|--------|
| **CCE-native text (all 6 phases)** | Branch `cam/cce-native-text`. Fixed point at 298,328 chars. |
| Char type — all 16 backends | On master. |
| P4 string.Concat flattening | On master. |
| P2 sorted binary search + list-snoc | On master. |

### Medium-term (weeks)

| Item | Notes |
|------|-------|
| Codex.UI substrate | Design: semantic primitives, typed themes |
| Capability refinement | Direction, scope, time-boxing in effect syntax |
| Multi-language syntax | Parser per locale, shared AST |
| Remaining ~2-3x perf gap to reference | Profile to find next bottleneck |

### Long-term

| Item | Notes |
|------|-------|
| Codex.OS on real hardware | WHPX (Nut's box), then actual boot device |
| Ring 5+: filesystem, networking | Content-addressed FactStore as the filesystem? |
| Self-hosted compiler compiling itself on bare metal | The ultimate fixed-point |
| Floppy disk image | Boot → compiler → self-compile, all in 1.44 MB |

---

## Process

- **Reference compiler lock lifted** (2026-03-24): `src/` freely modifiable.
- **Session init**: `codex-agent orient` (Cam), `bash tools/linux-session-init.sh` (Linux).
- **Handoff**: `codex-agent handoff push/review/approve/merge`. Always update CurrentPlan.md.
- **Four-agent workflow**: Git is the coordination protocol.
  - Windows (Copilot/VS): builds features, reviews code
  - Linux (Claude/sandbox): tests on real hardware/emulators, finds bugs by tracing
  - Cam (Claude Code CLI, 1M Opus): fast iteration, parallel work, GDB debugging
  - Nut (Copilot/VS2026, garage box): hardware lab, OS dev, phone flash
