# Current Plan

**Date**: 2026-03-24 (verified via system clock)

---

## Status

**Peak I (Self-Hosting) achieved.** The Codex compiler compiles itself. Fixed point proven.
**Camp II-A (IL Backend) summited.** Standalone `.exe` emission via IL, no C# compiler needed.
**Camp II-B (Native Codegen) summited.** RISC-V native + WASM + ARM64 backends. Four binary targets.
**V1 (Repository Views) complete.** Named views, consistency, composition, view-aware build.
**R2b (Effects Formalized) complete.** Twelve effects as `.codex` source, loaded by parser.
**Camp III-B (Capability System) complete.** CapabilityChecker + CLI `--capabilities` enforcement merged.
**Camp III-A (Linear Allocator) Phase 1 complete.** IRRegion node + WASM region-based allocator merged.
**Camp III-A Phase 2a complete.** IRRegion `NeedsEscapeCopy` annotation (Cam).
**Camp III-A Phase 2b complete.** RISC-V escape copy for Text, Record, List, Sum regions (Cam). Reviewed, merged.
**Camp III-A Phase 2c complete (2026-03-24).** Region reclamation enabled on all native backends (x86-64, ARM64, WASM extended). Escape copy deep-copies return values to parent region on function exit. Closures skip regions (capture types unknown).
**Register spill verified (2026-03-23).** AllocLocal saturation bug found by Linux review, spill-to-stack + IRRegion SP fix verified under QEMU — 40/40 RISC-V tests green.
**Camp II-C (Self-Hosted Native) SUMMITED (2026-03-23).** The Codex compiler, compiled to a 227KB RISC-V ELF, compiles Codex source to valid C# under QEMU. No .NET, no CLR, no JIT. Native machine code, start to finish.
**ARM64 backend complete (2026-03-23).** Arm64Encoder, Arm64CodeGen (1,740 lines), ElfWriterArm64. `codex build --target arm64` produces ELF64 AArch64 binaries. **QEMU-verified (2026-03-24)** — 33/33 tests green under qemu-aarch64. Two bugs found and fixed by Agent Linux: `__escape_text` null guard (CBZ→CBNZ), `__str_concat` byte copy. ELF section headers added for Android bionic compatibility.
**Phone effects complete (2026-03-23).** 7 new effects: Network, Display, Camera, Microphone, Location, Sensors, Identity. 7 prelude files, 13 new tests, capability enforcement verified.
**Phone hardware ready (2026-03-23).** Samsung SM-G935T (T-Mobile S7 Edge) backed up, SIM removed, OEM unlock enabled, Odin connected. TWRP build handed off to Agent Linux — no pre-built images exist for hero2qlte.
**x86-64 backend SUMMITED (2026-03-23).** X86_64Encoder, X86_64CodeGen (~2,500 lines), ElfWriterX86_64. Self-hosted compiler compiles to 248KB x86-64 ELF and produces correct C# output running natively in WSL. 20 bugs found and fixed in one evening session (Cam + Agent Linux). No QEMU — native execution on the dev machine.

The C# bootstrap compiler is locked. All forward development happens in `.codex` source.

### Snapshot

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~4,900 lines |
| Prelude | 23 modules, ~1,300 lines (11 type + 12 effect) |
| Backends | 12 transpilation + IL + RISC-V native + RISC-V bare metal + WASM + ARM64 + x86-64 |
| Tests | 470 in Codex.Types.Tests (40 RISC-V QEMU, 33 ARM64 QEMU, 23 x86-64 native, 31 WASM wasmtime) |
| Type debt | 0 |
| Fixed point | Proven (Stage 1 = Stage 3 at 255,344 chars) |
| Reference compiler | 🔒 Locked |
| Binary targets | RISC-V 64 (Linux user + bare metal), WASM/WASI, ARM64 (Linux), x86-64 (Linux) |
| RiscVCodeGen | 2,248 lines — register spill, closures, lists, file I/O, runtime helpers |
| Arm64CodeGen | 1,740 lines — full IR→ARM64 codegen, callee-saved regs, heap pointer |
| X86_64CodeGen | ~2,500 lines — closures, builtins, 7+ param stack passing, self-hosted verified |
| Agents | 3 (Windows/Copilot, Linux/sandbox, Cam/CLI) |
| Phone | Samsung SM-G935T — OEM unlocked, awaiting TWRP build |

---

## Completed Work (this cycle — 2026-03-22)

### V1 — Repository Views ✅ COMPLETE

Four phases delivered in one day:
- **Phase 1**: Named views — CRUD, legacy bridge, name validation, existence guards (27 tests)
- **Phase 2**: View consistency — type-check all definitions in a view together (5 tests)
- **Phase 3**: View composition — Override, Merge (with conflict detection), Filter (14 tests)
- **Phase 4**: View-aware compilation — `codex build --view <n>` (the view IS the build manifest)

69 total ViewTests. The repository model is now a working build system.

### R2b — Formalize Effects ✅

Effect definitions moved from hard-coded TypeEnvironment to parsed `.codex` source:
`Console`, `FileSystem`, `State`, `Time`, `Random` (5 prelude files).
`BuiltinEffects.Load()` parses once, caches forever. 8 new prelude tests.

### Camp III-B — Capability System (Phase 1) ✅

`CapabilityChecker`: post-type-check pass that extracts effect annotations
and optionally enforces capability grants. `CDX4001` diagnostic when a
required capability is missing. Wired into all compile pipelines.
`CapabilityReport` carried on `IRCompilationResult`. 9 tests.

### Camp III-B — Capability System (Phase 2) ✅

CLI `--capabilities Console,FileSystem` flag wired into `codex build` and
`codex check`. Enforcement at compile time: `CDX4001` fires when a required
capability isn't granted. `PrintCapabilityReport` shows required capabilities
in build/check output. Merged from `linux/camp3b-capability-checker`.

### Camp III-A — Linear Allocator (Phase 1) ✅

`IRRegion` IR node wraps every definition body. WASM backend implements real
region-based allocation: push heap pointer on enter, restore on exit (bulk free).
Escape promotion skips heap-returning types (Phase 2). All 5 backends updated.
1 new WASM test (1000-iteration stability). Merged from `linux/camp3a-region-allocator`.

### WASM Backend ✅

**Phase 1**: Direct bytecode emission (no Cranelift), WASI fd_write, bump allocator,
length-prefixed strings, runtime helpers (print i64/bool). 10 tests.

**Phase 2**: String equality (byte-by-byte with pointer fast path), text builtins
(text-to-integer, integer-to-text, char-at, substring, negate), f64.neg opcode.
13 new tests (23 total WASM, all wasmtime-verified).

### Camp II-B — RISC-V Native Backend ✅ (2026-03-21)

RiscVEncoder, ElfWriter, RiscVCodeGen, bare metal UART. 13 + 5 QEMU tests.

### RISC-V Feature Parity ✅ (2026-03-22)

~1,000 lines of RISC-V machine code generation in 6 phases:
- **Phase 1**: Bump-alloc heap (S1 register, brk for Linux, fixed addr bare metal)
- **Phase 2**: Records (heap-alloc + field store/load at 8-byte offsets)
- **Phase 3**: Sum types (tagged unions `[tag:8B][fields...]`)
- **Phase 4**: Pattern matching (wildcard, variable, literal, constructor patterns)
- **Phase 5**: Text builtins (text-length, char-at, substring, to/from integer,
  show, string equality via __str_eq, concatenation via __str_concat)
- **Phase 7**: Region-based allocation (push/pop heap ptr, text escape)

Register allocator split: temps (T3-T6, recycled) vs locals (S2-S11, monotonic).
Equality bug fixed (slti→sltu+xori). 15 new tests (34 total RISC-V, all QEMU-verified).
Design: `docs/Designs/RISCV-PARITY.md`. Review: `docs/Reviews/RISCV-PARITY-PHASES1-4-REVIEW.md`.

### Previously Completed

- P1 — Self-Hosted Builtin Expansion ✅
- P2 — File Input & Stage 1 Verification ✅
- R6 — IL Native Executable Bootstrap ✅

---

### V2 — Narration Layer (CPL Implementation) ✅

All 6 CPL sentence forms implemented in one session (2026-03-22):
- **Form 1**: Type declarations (record/variant) — V1, extended with constraints
- **Form 2**: Constraint templates (`such that`, `where`, `provided that`)
- **Form 3**: Function templates (`To V (x : T) gives Y, failing if P`)
- **Form 4**: Proof assertions (`Claim:` / `Proof:` with CDX1105 validation)
- **Form 5**: Procedure steps (`first,`/`then,`/`finally,` with let/return/if)
- **Form 6**: Quantified statements (`for every`, `there exists`, `no`)

Also: prose-notation consistency checking (CDX1101/CDX1102), inline code
refs (backtick), inline type refs (PascalCase), transition markers (`We say:`).
44 prose template tests. Design: `docs/Designs/V2-NARRATION-LAYER.md`.

### IL Emitter — maxstack fix ✅

Fixed `InvalidProgramException` caused by hardcoded `maxStack=32`. Now scales
with `max(16, max(locals.Count, exprDepth) + 16)` using `EstimateStackDepth`
recursive IR walker. Found by dogfooding codex-agent.

### codex-agent — per-agent cognitive check ✅

`check cam` uses 800K budget (1M context), `check windows`/`check linux` use
60K. Agent name, label, and budget-appropriate load assessment in output.

---

## Active Work

### Codex Phone — Phase 1 (In Progress)

**Goal**: A Codex program running on the Samsung S7 Edge (SM-G935T).

**Done:**
- ARM64 backend: 2,236 lines, `codex build --target arm64` produces ELF64 AArch64 binaries
- Phone effects: 7 new effects (Network, Display, Camera, Microphone, Location, Sensors, Identity) with capability enforcement
- Phone hardware: backed up, SIM removed, OEM unlocked, Odin connected on COM7
- V4 proof-carrying facts: proofs verified at view composition time

**Blocked on:**
- Phase D: Flash to phone — recovery image packed and validated, awaiting human go/no-go

**After TWRP:**
1. Flash TWRP via Odin
2. Wipe Android
3. Install minimal Linux (postmarketOS)
4. `adb push` ARM64 binary, run natively

**Design doc**: `docs/Designs/CODEX-PHONE.md`

### x86-64 Backend ✅ SUMMITED (2026-03-23)

**Camp II-C x86-64**: Self-hosted Codex compiler runs natively on x86-64.

248KB ELF binary, running in WSL (no QEMU). Compiles Codex source to valid C#.
Built in one evening session — 21 commits, 20 bugs found and fixed:

- X86_64Encoder (REX/ModR/M/SIB), ElfWriterX86_64, X86_64CodeGen (~2,500 lines)
- Closures, sum constructors, partial application, 7+ parameter stack passing
- All runtime helpers: itoa, str_concat, str_eq, escape_text, read_file, read_line,
  text_to_int, list_cons, list_append, str_replace, text_contains, text_starts_with
- 20 builtins: text-replace, char-code-at, char-code, code-to-char, is-letter,
  char-at, substring, list-at, list-length, list-cons, list-append, And, Or, etc.
- Region reclamation disabled (pure bump allocator, same as RISC-V before escape copy)

**Bugs fixed during summit push:**

| Bug | Root Cause |
|-----|-----------|
| Frame layout | Callee-saved pushes after sub rsp |
| EFLAGS clobbering | xor before setcc |
| Register pool aliasing | R8/R9 shared between spill scratch and temps |
| Closure heap clobber | AllocTemp ptrReg recycled before store |
| 18 unresolved calls | 5 missing builtins from RISC-V |
| ConstructedType | 4 codegen paths not resolving |
| Escape copy stubs | Real per-type helpers needed |
| Record/list/ctor AllocLocal | HeapReg in recycled temp |
| __read_file heap bump | Didn't account for filename scratch |
| __read_file alignment | Variable-length filename not padded to 8 |
| Null escape helpers | Empty lists as null pointers |
| AppendList/ConsList | Binary ops not handled |
| R8/R9 arg conflict | Push/pop arg setup for 6-arg functions |
| Byte overflow | Spill IDs wrapped at 224 |
| Top-level constants | EmitName fallthrough didn't call zero-arg functions |
| And/Or ops | Boolean logic returned 0 |
| 7+ parameters | Stack passing for args beyond 6 registers |
| list-at clobber | shl/add modified index register in place |

Three-agent collaboration: Cam built + debugged (GDB in WSL), Agent Linux traced with GDB on sandbox, Human routed between agents.

### Camp III-A Phase 2 — Escape Analysis ✅ Phase 2a+2b+2c (Cam)

Escape copy and region reclamation across all backends:
- Phase 2a: `NeedsEscapeCopy` flag on `IRRegion` node
- Phase 2b: RISC-V per-type escape copy helpers for Text, Record, List, Sum types
- Phase 2c (2026-03-24): Region reclamation enabled on x86-64, ARM64, WASM
  - x86-64: EmitRegion wired to existing escape helpers
  - ARM64: Full escape infrastructure built from scratch (~200 lines)
  - WASM: Extended to scalar-only records/sums (flat copy)

Remaining: closure escape (capture types unknown), WASM deep copy for nested heap types.

### Camp II-C — Self-Hosted on RISC-V ✅ SUMMITED (2026-03-23)

The Codex compiler, compiled to a 227,600-byte RISC-V ELF, successfully
compiles Codex source to valid C# under QEMU. No .NET, no CLR, no JIT.
493 definitions, 26 `.codex` files → native machine code → compiler output.

**Summit verification:**
```
echo "/tmp/summit-test.codex" | qemu-riscv64 ./Codex.Codex/out/Codex.Codex
```
Output: clean C# (`public static long main() => 42;`). Exit code 0.

**Bugs found and fixed during the summit push (2026-03-22/23):**

| Bug | Symptom | Fix |
|-----|---------|-----|
| AllocLocal saturation | Silent register aliasing at >10 locals | Spill to stack (virtual regs ≥32) |
| EmitRegion SP shift | Spill offsets corrupted by mid-function SP push | AllocLocal for heap save instead of SP shift |
| 12-bit addi overflow | 2128-byte frame truncated to 1968 (silent) | `li t0, N; sub sp, sp, t0` for large frames |
| Forward references | Calls to later-defined functions became NOPs | Removed guard; calls patched after all functions emitted |
| Zero-arg builtins in do-blocks | `read-line` as IRName never hit TryEmitBuiltin | EmitName tries zero-arg builtins first |
| T0 clobbering in list-at | LoadLocal for spilled values overwrote index in T0 | Use T2 (safe scratch) for index computation |
| Closures / partial application | CPS patterns returned Reg.Zero → NULL deref | Heap-allocated closures with trampolines |
| Record field ordering | EmitRecord used source order; field access used type order | Reorder fields in EmitRecord to match RecordType |
| ConstructedType in field access | Lowering didn't resolve ConstructedType → RecordType | Resolve in LowerFieldAccess |
| Region text escape | Heap reclaimed while pointers still live | Disable region reclamation (escape analysis needed) |
| 5 missing builtins | text-replace, char-code-at, char-code, code-to-char, is-letter | Implemented in TryEmitBuiltin + __str_replace helper |

**Three-agent collaboration:** Windows agent built features. Cam (1M Opus)
debugged at full speed — 10 fix commits in one session, closures included.
Linux agent reviewed, ran QEMU traces, verified each fix, found the initial
AllocLocal saturation bug. Human routed between agents across session boundaries.

**Design doc**: `docs/Designs/CAMP-IIC-SELF-HOSTED-RISCV.md`

---

## Forward Direction — Next Rocks to Climb

### Ready Now
| Task | What | Why |
|------|------|-----|
| ~~RISC-V parity~~ | ~~Records, sum types, pattern matching, text builtins on RISC-V~~ | ✅ Done (2026-03-22) |
| ~~Register spill~~ | ~~Spill locals to stack when S-regs exhausted~~ | ✅ Done (2026-03-23, verified by Linux) |
| ~~Camp II-C~~ | ~~Self-hosted compiler on RISC-V~~ | ✅ **SUMMITED** (2026-03-23) |
| ~~V4~~ | ~~Proof-carrying facts~~ | ✅ Done (2026-03-23) |
| ~~ARM64 backend~~ | ~~Extend native codegen to ARM64~~ | ✅ Done (2026-03-23) |
| ~~Phone effects~~ | ~~Network, Display, Camera, Microphone, Location, Sensors, Identity~~ | ✅ Done (2026-03-23) |
| ~~Camp III-A Phase 2a/2b~~ | ~~IRRegion escape copy annotation + per-type helpers~~ | ✅ Done (Cam, 2026-03-23) |
| ~~TWRP build~~ | ~~Build TWRP recovery.img for hero2qlte~~ | ✅ Boot image packer validated, ready for flash |
| ~~ARM64 QEMU verification~~ | ~~Verify ARM64 binaries under qemu-aarch64~~ | ✅ 33/33 (Agent Linux, 2026-03-24) |
| ~~Camp III-A Phase 2c~~ | ~~Full escape analysis for region reclamation~~ | ✅ Done (2026-03-24) |

### Medium Term
- **Camp III-C**: Structured concurrency — `par`, `race`, work-stealing
- **V3**: Repository federation — multi-repo sync, cross-repo trust
- **Phone Phase 2**: Replace Android — postmarketOS, framebuffer, touch input, UI toolkit

### Long Term
- **V5 — Intelligence layer**: AI agents as first-class participants
- **V6 — Trust lattice**: vouching with degrees, trust-ranked search
- **Phone Phase 3 — Codex.OS**: ARM64 bare metal on the phone. No Linux. The summit.
- **Peak IV — Codex.OS**: The summit

---

## Process

- **Reference compiler is LOCKED.** See `docs/REFERENCE-COMPILER-LOCK.md`.
- **Stdlib design**: `docs/Designs/STDLIB-AND-CONCURRENCY.md`
- **RISC-V parity plan**: `docs/Designs/RISCV-PARITY.md`
- **V2 narration design**: `docs/Designs/V2-NARRATION-LAYER.md`
- **Agent toolkit**: `tools/codex-agent/` — peek, snap, build, test, handoff, doctor
- **MCP server**: `tools/Codex.Mcp/` — compiler-as-a-tool for agents
- **Principles**: `docs/10-PRINCIPLES.md` — unchanged, still governing.
- **Three-agent workflow**: Windows (Copilot/VS) builds + pushes, Linux (Claude/sandbox) tests + reviews, Cam (Claude Code CLI, 1M Opus) fast iteration + parallel work. Git is the coordination protocol. Cam works from `D:\Projects\NewRepository-cam` worktree. Linux reviews are pushed to `docs/reviews/`.
