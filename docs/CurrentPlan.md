# Current Plan

**Date**: 2026-03-26 late night

---

## MM2 IS PROVEN

The self-hosted Codex compiler — running on bare metal x86-64 under QEMU,
268 KB kernel, no OS, no runtime, just the UART — received `main : Integer`
/ `main = 42` over serial, compiled it through the full pipeline (tokenize,
parse, desugar, resolve, typecheck, lower, emit), and emitted valid C# back
over serial. Complete with CCE runtime preamble, using directives, and entry
point call.

**Source code in over serial. Valid compiled output out over serial. On bare
metal. MM2: The High Camp is reached.**

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~5,000 lines |
| Backends | 15 (12 transpilation + IL + RISC-V + WASM + ARM64 + x86-64, including bare metal) |
| Tests | ~900+ (519 compiler + 139 syntax + 110 repository + 70 core + 23 semantics + 18 LSP + 16 AST + MM2 integration) |
| Self-compile time | 312ms median (perf checked: +0.8% from 310ms baseline) |
| Fixed point | **Proven** (Stage 2 = Stage 3 at 298,752 chars, CCE-native) |
| Language features | Lambda, fork/await/par/race, Char, CCE-native text, linear closures, linear function types, CCE Tier 0-3 (full Unicode) |
| Codex.OS | 268 KB kernel, Rings 0-4, arena REPL, preemptive multitasking, capability-enforced syscalls, **compiles programs on bare metal** |
| CCE encoding | Full Unicode coverage — Tier 0 (1B, 128 chars), Tier 1 (2B, 500+ chars, 7 scripts), Tier 2 (3B, all BMP), Tier 3 (4B, emoji/supplementary) |
| Agents | 4 (Windows/Copilot, Linux/sandbox, Cam/CLI, Nut/garage-box) |

**History**: `docs/OldStatus/CurrentPlan-2026-03-26-evening.md`
**Route map**: `docs/THE-ASCENT.md`, `docs/THE-LAST-PEAK.md`

---

## What Got Done (2026-03-26 night)

### MM2 Proven (Agent Linux)
- QEMU bare metal test: `.codex` source → serial → compile → C# output → serial
- Fixed 2 test errors found during integration (list-contains registration, test fixes)
- MM2IntegrationTests.cs added to test suite

### CCE Full Unicode (Cam)
- Tier 1 multi-byte: Latin Extended (128), Cyrillic (77), Greek (49+),
  Arabic (44), Devanagari (53), CJK (~150), Japanese (176), Korean (96)
- Tier 2/3 pass-through: any Unicode character roundtrips — 3 bytes for BMP,
  4 bytes for supplementary (emoji). No data loss. Ever.
- 70 core encoding tests

### Closure Escape Analysis Complete (Cam)
- Step 4: `linear` function types for higher-order callbacks
- `TryResolveExprType` resolves through curried application chains
- 25 linearity tests, all 4 steps shipped

### x86-64 Fixes (Cam)
- `__ipow` runtime helper (was stubbed as 0) — exponentiation by squaring
- `list-contains` registered across full pipeline (bug found by Linux)
- 6 MM2 builtins merged (text-compare, list-snoc, list-insert-at,
  list-contains, text-concat-list, text-split)
- Zero TODOs remaining in compiler pipeline

### Escape Diagnostics (Cam, merged by Linux)
- CDX0005/CDX0006: `\t` and `\r` are compile-time errors
- Boundary normalization: TAB→spaces, CR→strip

### Perf Tracking (Cam)
- Baseline: 310ms. Current: 312ms (+0.8%). Within 10% threshold.
- `--bench-check` mode operational

### Design (Cam)
- Capability refinement: direction, scope, time-boxing
- Unified trust lattice: capabilities = positions in the repository trust lattice
- Design doc: `docs/Designs/CAPABILITY-REFINEMENT.md`

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

## Cam Session Handoff (2026-03-27)

### DONE: Two-space region reclamation (both backends)

**Branch**: `cam/mm3-shared-fixes`

The #1 MM3 blocker — heap exhaustion on self-compile — is addressed by
splitting the heap into **working space** and **result space** with a
dedicated register for each:

| | x86-64 | RISC-V |
|---|--------|--------|
| Working space | R10 (HeapReg) | S1 |
| Result space | R15 (ResultReg) | S11 (ResultReg) |
| Working size | 256 MB | 64 MB |
| Result size | 256 MB | 64 MB |

#### How it works

Every `let` binding is wrapped in an `IRRegion` by the Lowering pass.
- **Scalar-returning regions** (integers, booleans): save HeapReg, run body,
  restore HeapReg → all intermediates reclaimed, value survives in register.
- **Heap-returning regions** (lists, records, text, sum types): save HeapReg
  as mark, run body, switch HeapReg to result space, escape-copy the result
  (deep copy into result space), switch back and restore HeapReg to mark →
  all working-space intermediates reclaimed, live result in result space.

The escape-copy helpers already existed (for closures and fork/await). The
new code reuses them, just switching which heap space receives the copies.

#### Why this helps MM3

Without reclamation: all 7 pipeline stages' garbage accumulates (~1GB+).
With reclamation: each stage's working memory is reset after every `let`.
Only live data (stage outputs) persists in result space.

Estimated memory: ~50MB live data across all stages vs ~1GB+ garbage before.
The 256MB working space only needs to hold one stage's intermediates at a time.

#### Files changed

- `src/Codex.Emit.X86_64/X86_64CodeGen.cs` — R15 reserved as ResultReg,
  removed from LocalRegs (4 callee-saved instead of 5), EmitRegion rewritten,
  startup: 512MB brk (256+256), bare metal: 4MB heap, REPL arena reset
- `src/Codex.Emit.X86_64/ElfWriterX86_64.cs` — bare metal ELF memsz 2MB→4MB
- `src/Codex.Emit.RiscV/RiscVCodeGen.cs` — S11 reserved as ResultReg,
  removed from CalleeSaved (9 instead of 10), base frame 96→88 bytes,
  EmitRegion rewritten, startup: 128MB brk (64+64), bare metal: +2MB result

#### Verification

- 1,003 reference compiler tests pass (0 failures)
- Build clean (expected CS5001 only)

#### DONE: Type resolution in escape-copy helpers (f2f4008)

Fixed `ResolveType` to recurse into `ListType.Element`, so
`ListType(ConstructedType("Token"))` resolves to `ListType(RecordType(...))`.
Added `ConstructedType` case to `EscapeCopyKey` for unique keys. Resolve
types in `GetOrQueueEscapeHelper`/`GetOrQueueRelocateHelper` before keying.
Changed EmitRegion guard from TextType-only to ConstructedType-fallback,
enabling two-space reclamation for all heap types (List, Record, Sum, Text).

#### DONE: DoBind region wrapping (142f517)

`LowerDoExpr` was not wrapping `DoBind` values in `IRRegion`. Do-block
bindings like `source <- read-file path` never got escape-copy or
working-space reclamation. Now wraps with `boundType` (unwrapped from
`EffectfulType`) for the needs-escape check, matching `LowerLetExpr`.

#### Current status: result-space-aware escape-copy needed

Both fixes verified: 541 compiler tests pass, escape helpers now generated
for all types (`__escape_list_record_Token`, `__escape_sum_DoStmt`, etc.),
do-block bindings wrapped in regions. Simple programs (factorial) self-compile
correctly on both x86-64 and RISC-V user mode.

Self-compile of full 180KB source crashes in `__escape_record_LexState`.
Root cause: escape-copy blindly deep-copies ALL pointers, including pointers
that already point to result space. The `LexState.source` field (180KB full
source text) is deep-copied every time ANY LexState is escape-copied in
a let-binding region. The lexer creates ~30,000 LexStates during tokenization.
30,000 × 180KB = 5.4GB — no heap size is sufficient.

**#1 blocker: result-space-aware escape-copy**. Escape helpers must check:
"is this pointer already in result space? If so, return it unchanged."

Implementation plan:
1. At startup, store `result_space_base` (= brk_base + working_space_size)
   in a known location (first 8 bytes of heap, or a reserved register)
2. In each escape helper, before following a pointer: compare against
   `result_space_base`. If ptr >= base, it's already in result space → skip copy
3. Apply to both x86-64 and RISC-V backends

This is a small change in `EmitEscapeHelperPrologue` / each field-copy
helper. The check is: `cmp rdi, [result_base_addr]; jge .skip_copy`.

### Previous: TCO/match register clobbering (fixed last session)

See `docs/OldStatus/CurrentPlan-2026-03-26-evening.md` for the full
root-cause analysis (commits `ea35113`, `42c2fd0`).

### Also found (assigned to Agent Windows)

`Codex.Codex/Ast/Desugarer.codex` — `desugar-type-expr` missing `EffectTypeExpr` case.
`Codex.Codex/Ast/Desugarer_.codex` — duplicate file, renamed to `.bak`.

---

## What's Next

### The path to MM3: Summit

MM3 is the self-hosted compiler compiling *itself* on bare metal — the
ultimate fixed point. The compiler that compiled the compiler, on hardware
it built the OS for.

### Near-term (days)

| Item | Notes |
|------|-------|
| ~~Fix native self-hosted crash~~ | **DONE** — TCO/match register clobbering in both backends |
| ~~Escape-copy type resolution~~ | **DONE** — ResolveType recurses into ListType, ConstructedType keyed, all heap types enabled |
| ~~DoBind region wrapping~~ | **DONE** — do-block bindings now wrapped in IRRegion for reclamation |
| Result-space-aware escape-copy | **#1 blocker** — escape helpers must skip pointers already in result space |
| Add EffectTypeExpr to desugar-type-expr | Missing case (assigned to Agent Windows) |
| Perf automation | Wire `--bench-check` into CI or pre-commit hook |

### Medium-term (weeks)

| Item | Notes |
|------|-------|
| MM3 gap analysis | What's missing to self-compile on bare metal? |
| Codex.UI substrate | Semantic primitives, typed themes |
| Capability refinement Steps 2-8 | Scope, time-boxing, unified trust lattice |
| Multi-language syntax | Parser per locale, shared AST |

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
