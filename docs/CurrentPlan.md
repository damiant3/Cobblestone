# Current Plan

**Date**: 2026-03-26 night (post-reboot session)

---

## Where We Stand

Feature push in progress. Two branches merged to master by Agent Linux.
One branch pending review. Linux concurrently running QEMU verification
(found 2 test errors, analyzing).

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~5,000 lines |
| Backends | 15 (12 transpilation + IL + RISC-V + WASM + ARM64 + x86-64, including bare metal) |
| Tests | ~880 (519 compiler + 139 syntax + 110 repository + 61 core + 23 semantics + 18 LSP + 16 AST) |
| Self-compile time | 279ms median (CCE-native) |
| Fixed point | **Proven** (Stage 2 = Stage 3 at 298,752 chars, CCE-native) |
| Language features | Lambda, fork/await/par/race, Char, CCE-native text, linear closures, **linear function types** (pending), **CCE Tier 1** (pending) |
| Codex.OS | 7–10 KB kernel, Rings 0-4, arena REPL, preemptive multitasking, capability-enforced syscalls |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-26-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## Pending Work

### On master (merged tonight by Linux)

| Item | Branch | Status |
|------|--------|--------|
| 6 MM2-blocking x86-64 builtins | `cam/mm2-builtins` | Merged |
| Boundary normalization (TAB→spaces, CR→strip) | `cam/boundary-normalization` | Merged |
| Perf tracking baseline (310ms) + `--bench-check` | `cam/boundary-normalization` | Merged |
| Tier 1 design spike (8 validation tests) | `cam/boundary-normalization` | Merged |
| CDX0005/CDX0006 escape diagnostics (`\t`/`\r` → error) | `cam/tier0-escape-diagnostics` | Merged |

### Awaiting Linux review

| Item | Branch | Commits |
|------|--------|---------|
| CCE Tier 1 multi-byte encoding (Latin Extended 128 + Cyrillic 77) | `cam/cce-tier1-multibyte` | `c5bb4b8` |
| Step 4: higher-order linear callbacks via `linear` function types | `cam/cce-tier1-multibyte` | `e355794` |
| `__ipow` runtime helper for x86-64 (was stubbed as 0) + 2 power tests | `cam/cce-tier1-multibyte` | `cc43e6f`, `ff09e4c` |
| Generated C# output regen + docs update | `cam/cce-tier1-multibyte` | `06a232d`, `4ecb148` |

### In progress (Agent Linux)

| Item | Notes |
|------|-------|
| QEMU serial compile test | First real MM2 validation. Found 2 test errors, analyzing. |

---

## What Got Done (2026-03-26 night — Cam)

### CCE Tier 1 Multi-Byte Encoding
- Self-synchronizing 2-byte framing: `110xxxxx 10xxxxxx`.
- 2,048 code point space. Latin Extended block (128 chars: ß, ã, å, æ,
  Latin Extended-A, uppercase, symbols). Cyrillic Extended block (77 chars:
  remaining Russian, Ukrainian, Serbian, Belarusian, Macedonian).
- `Encode()`/`Decode()` handle mixed Tier 0+1 streams.
- `GenerateRuntimeSource()` emits sparse Tier 1 dictionaries.
- 16 new core tests (61 total).

### Closure Escape Analysis Step 4
- `linear` function types: `linear (A -> B)` parameter guarantees
  exactly-once consumption. Lambdas capturing linear vars can be passed
  to such parameters without CDX2043.
- `TryResolveExprType` resolves types through curried application chains.
- 6 new linearity tests (25 total). All 4 steps now shipped.

### x86-64 Power Operation
- `__ipow` runtime helper: exponentiation by squaring, O(log n).
- Handles exp==0→1, exp<0→0.
- 2 native tests via WSL: `2^10=1024`, `7^0=1`.
- Zero TODOs remaining in compiler pipeline.

---

## Codex.OS Status

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32-to-64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done |
| 2 | Process table (16 slots), preemptive context switch, per-process page tables | Done |
| 3 | Capability-enforced syscalls | Done |
| 4 | Self-hosting compiler on bare metal, arena REPL, CCE-native | REPL verified, 6 MM2 builtins shipped, QEMU serial compile in progress |

---

## What's Next

### Near-term (days)

| Item | Owner | Notes |
|------|-------|-------|
| QEMU serial compile test | Linux | In progress — first real MM2 validation |
| Merge `cam/cce-tier1-multibyte` | Linux | Review + merge 6 commits |
| Perf tracking automation | Cam | Wire `--bench-check` into CI or pre-commit |

### Medium-term (weeks)

| Item | Notes |
|------|-------|
| CJK/Japanese/Korean Tier 1 blocks | Reserved in infrastructure, need character frequency analysis |
| Codex.UI substrate | Semantic primitives, typed themes |
| Capability refinement | Direction, scope, time-boxing in effect syntax |
| Multi-language syntax | Parser per locale, shared AST |
| Tier 2/3 multi-byte | 3-byte and 4-byte for full Unicode coverage |

### Long-term

| Item | Notes |
|------|-------|
| Self-hosted compiler compiling itself on bare metal (MM3) | The ultimate fixed-point |
| Codex.OS on real hardware | WHPX (Nut's box), then actual boot device |
| Ring 5+: filesystem, networking | Content-addressed FactStore as filesystem? |
| Floppy disk image | Boot → compiler → self-compile, all in 1.44 MB |

---

## Process

- **Reference compiler lock lifted** (2026-03-24): `src/` freely modifiable.
- **Session init**: `codex-agent orient` (Cam), `bash tools/linux-session-init.sh` (Linux).
- **Handoff**: Always update this file. `codex-agent handoff` for agent-to-agent.
- **Feature branches**: All work goes to feature branches for review. Direct master pushes for docs only.
- **Four-agent workflow**: Git is the coordination protocol.
  - Windows (Copilot/VS): builds features, reviews code
  - Linux (Claude/sandbox): tests on real hardware/emulators, finds bugs by tracing, reviews
  - Cam (Claude Code CLI, 1M Opus): fast iteration, parallel work
  - Nut (Copilot/VS2026, garage box): hardware lab, OS dev, phone flash
