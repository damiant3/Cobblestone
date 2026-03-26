# Current Plan

**Date**: 2026-03-26 afternoon (Ring 4 push)

---

## Where We Stand

CCE is **merged to master and reflected on**. The Janus reflection
(`docs/Designs/CCE-NATIVE-TEXT.md`, "Cam's Think" section) assessed the
trade-offs honestly: 34% overhead in the .NET pipeline, temporary and diffuse,
paid in a depreciating currency. Decision: stay on the forward path. No shortcut
trail back to Unicode internals.

The CCE whitespace decision (`docs/Designs/CCE-WHITESPACE-DECISION.md`) is
**open** — Options A through E on the table. Option E (dual compilation modes:
`--encoding cce` vs `--encoding unicode`) is the leading candidate. Cam's action
items from that doc are partially resolved (see below).

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~5,000 lines |
| Backends | 15 (12 transpilation + IL + RISC-V + WASM + ARM64 + x86-64, including bare metal) |
| Tests | 926 |
| Self-compile time | 279ms median (CCE-native), 208ms pre-CCE |
| Fixed point | **Proven** (Stage 2 = Stage 3 at 298,328 chars, CCE-native) |
| CCE perf overhead | 34% constant-factor, diffuse across all stages |
| Language features | Lambda, fork/await/par/race, Char, CCE-native text |
| Codex.OS | 7 KB kernel, Rings 0-4, preemptive multitasking, capability-enforced syscalls |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-24-peak3-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## What Got Done (2026-03-26)

### CCE-Native Text — Complete, Merged, Reflected

- All 6 phases complete. Branch `cam/cce-native-text` merged to master.
- Tier 0 revision: 128 chars, frequency-sorted, 3 whitespace + 10 digits +
  26 lower + 26 upper + 29 punct + 19 accented + 15 Cyrillic.
- Self-hosted emitter fully CCE-native: `_Cce` runtime, I/O wrapping, escape
  rewrite, char-literal-based classification.
- Fixed point proven at 298,328 chars.
- Perf report: `docs/reviews/CCE-PERF-IMPACT.md` — 34% overhead, no algorithmic
  regression.
- P1 optimization done (`text-concat-list`, commit `a46bcf1`): O(n²) escape-text
  fixed but didn't move the needle — n too small. Confirms overhead is diffuse.
- Janus reflection: `docs/Designs/CCE-NATIVE-TEXT.md`, "Cam's Think" section.
  Conclusion: forward path, no dual-encoding retreat.

### CCE Whitespace Decision — Open

- Linux + Damian identified TAB/CR silent NUL corruption.
- Five options documented. Option E (dual compilation modes) leading.
- **Cam's action items resolved**:
  - `\t` and `\r` in `.codex` source: only in `Lexer.codex` escape handler
    (lines 230-231). This is correct — the Lexer processes escape sequences in
    string literals. No `.codex` source uses literal tabs.
  - Go/Python emitter indentation: **spaces, not tabs.** The emitters do have
    `Replace("\t", "\\t")` for string escaping in output, but indentation is
    spaces throughout. No tab dependency.
  - Emitter maintenance surface for Option E: ~30 builtin emission sites were
    touched during CCE migration. Both code paths already exist in git history.
    Formalizing them is bounded work, not open-ended.
- **Still open**: silent NUL fix (Option D, orthogonal), default mode choice,
  per-project vs per-invocation flag.

### Docs

- README refreshed: 15 backends, CCE, Codex.OS, 926 tests.
- Milestones named: MM2 The High Camp, MM3 Summit.
- CCE encoding integration design (Linux): gconv, EncodingProvider, editor
  plugins — the rope from the col back to base camp.

---

## Codex.OS Status

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32-to-64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done (7KB) |
| 2 | Process table (16 slots), preemptive context switch, per-process page tables | Done |
| 3 | Capability-enforced syscalls (SYS_WRITE_SERIAL, SYS_READ_KEY, SYS_GET_TICKS, SYS_EXIT) | Done |
| 4 | Self-hosting compiler on bare metal, TCO, serial I/O | Serial REPL works for short programs |

---

## What's Next

### Done this session (2026-03-26)

| Item | Commit | Notes |
|------|--------|-------|
| Janus reflection | `1640e79` | CCE trade-off analysis, forward-path decision |
| Silent NUL fix | `cd69946` | Unmapped chars → '?' not NUL. Option D shipped. |
| Closure escape analysis (Cam) | `54b24e7` | CDX2043 → error, linear propagation, direct-apply bypass |
| Closure escape analysis (Linux) | `f894a3e` | Better architecture: CheckLambdaExpr returns captured set |
| Merged closure analysis | `f74e1e3` | Linux's architecture + Cam's severity test, on `cam/closure-escape-merged` |
| `is-whitespace` x86-64 fix | `f43018a` | Register aliasing → branch-based rewrite. Unblocks bare metal lexer. |
| Arena-based REPL loop | `606c8b3` | Heap reset between compilations. Infinite REPL. Enables MM2/MM3. |
| `codex encode` CLI | Already existed | Roundtrip verified working. |

### Waiting on

| Item | Blocked on | Who |
|------|-----------|-----|
| QEMU verification of Ring 4 REPL | QEMU install on Linux sandbox | Agent Linux |
| Merge `cam/closure-escape-merged` | Linux review | Agent Linux |
| Phone flash | Bootloader signature issue | Human |

### Near-term (days)

| Item | Notes |
|------|-------|
| QEMU test: send .codex over serial, verify compilation output | First real MM2 validation |
| CCE Tier 1 multi-byte | Load-bearing for repository promise |
| Perf tracking | Track compound regression before it bites |

### Medium-term (weeks)

| Item | Notes |
|------|-------|
| Codex.UI substrate | Design: semantic primitives, typed themes |
| Capability refinement | Direction, scope, time-boxing in effect syntax |
| Multi-language syntax | Parser per locale, shared AST |
| CCE Tier 1 encoding | Multi-byte for CJK, Arabic, extended Latin |

### Long-term

| Item | Notes |
|------|-------|
| Codex.OS on real hardware | WHPX (Nut's box), then actual boot device |
| Ring 5+: filesystem, networking | Content-addressed FactStore as the filesystem? |
| Self-hosted compiler compiling itself on bare metal (MM3) | The ultimate fixed-point |
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
