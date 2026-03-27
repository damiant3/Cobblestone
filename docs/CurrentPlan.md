# Current Plan

**Date**: 2026-03-27 afternoon

---

## RISC-V AT PARITY WITH x86-64

Both native backends now have identical feature sets. The RISC-V codegen
compiles all 24 sample programs correctly on qemu-riscv64, and both
backends can self-compile integer programs in usermode. The CCE encoding
pipeline (Unicode→CCE→compiler→CCE→Unicode) is correct on both backends
at every OS boundary.

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~5,000 lines |
| Backends | 15 (12 transpilation + IL + RISC-V + WASM + ARM64 + x86-64, including bare metal) |
| Tests | 917 (525 compiler + 139 syntax + 110 repository + 86 toolkit + 23 semantics + 18 LSP + 16 AST) |
| Self-compile time | 312ms median (perf checked: +0.8% from 310ms baseline) |
| Fixed point | **Proven** (Stage 2 = Stage 3 at 298,752 chars, CCE-native) |
| Language features | Lambda, fork/await/par/race, Char, CCE-native text, linear closures, linear function types, CCE Tier 0-3 (full Unicode) |
| Codex.OS | 268 KB kernel, Rings 0-4, arena REPL, preemptive multitasking, capability-enforced syscalls, **compiles programs on bare metal** |
| CCE encoding | Full Unicode coverage — Tier 0 (1B, 128 chars), Tier 1 (2B, 500+ chars, 7 scripts), Tier 2 (3B, all BMP), Tier 3 (4B, emoji/supplementary) |
| RISC-V parity | **Complete** — all features, builtins, and CCE boundaries match x86-64 |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-26-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## What Got Done (2026-03-27, Cam)

### RISC-V Codegen Bugs Fixed
- **EmitBinary A0 clobbering**: right operand in A0 was clobbered by
  Mv(A0, leftReg) before Mv(A1, right) in function-call binary ops
  (++, text ==, ::, list ++). Fix: reversed Mv order.
- **char-to-text register save**: heap allocation Mv(A0, S1) overwrote
  the char value from char-at. Fix: save to T2 before allocation.
- **Stack arg spill offset**: >8 parameter calls adjusted SP before
  loading spill-slot values, corrupting offsets. Fix: store at negative
  offsets before SP adjust. 9, 10, 12 param functions verified.

### CCE Encoding Ported to RISC-V
Full audit revealed RISC-V was operating on UTF-8/ASCII while x86-64
used CCE throughout. Ported all encoding boundaries:
- AddRodataString/EmitTextLit: CceTable.Encode() (was UTF-8)
- is-letter/is-digit/is-whitespace: CCE ranges (was ASCII)
- __itoa/__text_to_int: CCE digit codes 3-12 (was ASCII 48-57)
- EmitPrintText: per-byte CCE→Unicode via lookup table (was raw)
- __read_line: per-byte Unicode→CCE via lookup table (was raw)

### File I/O CCE Boundaries (Both Backends)
- **read-file**: path CCE→Unicode for openat(), content Unicode→CCE
  after read. Fixed on both x86-64 and RISC-V. Rodata fixup slot
  reservation bug also fixed (Li(reg,0) emits 1 insn, fixup needs 2).
- **write-file**: CCE→Unicode conversion via EmitPrintTextNoNewline
  (was writing raw CCE bytes). Fixed on both backends.

### RISC-V __ipow
Ported exponentiation-by-squaring helper from x86-64. Verified:
2^10=1024, 3^5=243, 0^0=1, 5^3=125.

### Usermode Self-Compilation Tested
Both backends compile integer programs through the self-hosted compiler
in usermode (factorial verified on both qemu-x86_64 and qemu-riscv64).
Full self-compilation blocked by shared emitter issues (see below).

---

## Codex.OS Status

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32-to-64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done |
| 2 | Process table (16 slots), preemptive context switch, per-process page tables | Done |
| 3 | Capability-enforced syscalls | Done |
| 4 | Self-hosting compiler on bare metal, arena REPL, CCE-native | **MM2 PROVEN** — compiles `.codex` programs on bare metal |

---

## Known Issues (Shared, Both Backends)

These are NOT codegen issues — they are in the lowering/IR layer or the
self-hosted `.codex` source.

| Issue | Layer | Impact |
|-------|-------|--------|
| Boolean type → empty output in self-hosted compiler | Self-hosted emitter source | Blocks full usermode self-compile |
| String literal CCE escaping (bytes <32 escaped as \u00XX) | Self-hosted emitter source | Garbles string content in self-compiled output |
| show on parametric sum type fields | Lowering/IR | safe-divide.codex fails |
| List ++ in recursive functions → empty | Lowering/IR | is-prime-fancy.codex fails |
| run-state effect | Not implemented in native backends | state-demo.codex fails |

Details: `tools/_known_issues_native_backends.md`

---

## What's Next

### The path to MM3: Summit

MM3 is the self-hosted compiler compiling *itself* on bare metal — the
ultimate fixed point. RISC-V parity means we can now pursue MM3 on
either architecture.

### Near-term (days)

| Item | Notes |
|------|-------|
| Fix Boolean type in self-hosted emitter | #1 blocker for usermode self-compile |
| Fix CCE string escaping in self-hosted emitter | #2 blocker — uses ASCII rules instead of CCE |
| Merge cam/mm3-easy-builtins (4 commits) | Pending review |

### Medium-term (weeks)

| Item | Notes |
|------|-------|
| Usermode self-compilation (full) | Unblocked once Boolean + string escaping fixed |
| Fix parametric show / list ++ in recursion | Lowering/IR layer fixes |
| MM3 bare metal self-compile | Depends on usermode self-compile working first |
| Codex.UI substrate | Semantic primitives, typed themes |

### Long-term

| Item | Notes |
|------|-------|
| Self-hosted compiler compiling itself on bare metal (MM3) | The summit |
| Codex.OS on real hardware | WHPX (Nut's box), then actual boot device |
| Ring 5+: filesystem, networking | Content-addressed FactStore as filesystem |
| Floppy disk image | Boot → compiler → self-compile, all in 1.44 MB |
| Repository federation | Trust lattice, cross-repo sync, capability-gated imports |

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
