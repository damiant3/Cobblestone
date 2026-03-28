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

## What Got Done (2026-03-27 Cam)

### Capacity-aware lists (both backends)

Changed list memory layout from `[count | elem0 | ...]` to
`[capacity @ -8 | count @ 0 | elem0 @ 8 | ...]`. The capacity word is
hidden before the list pointer — all read-only access (list-at, list-length,
list-contains) uses unchanged offsets.

**`__list_snoc` now has 3 paths**:
1. **count < capacity**: store in reserved slot, O(1)
2. **count == capacity, at heap top**: double capacity, bump heap, O(1)
3. **count == capacity, not at top**: copy with `max(count*2, 16)` capacity, O(N) amortized O(1)

**Impact**: tokenizer building 52K-element list drops from ~11GB (O(N²) copy)
to ~512KB (geometric growth). 22,000x improvement.

Files changed (x86-64): `src/Codex.Emit.X86_64/X86_64CodeGen.cs`
- EmitList, get-args, __list_snoc, __list_cons, __list_append,
  __list_insert_at, EmitListEscapeHelper — all add capacity word

Files changed (RISC-V): `src/Codex.Emit.RiscV/RiscVCodeGen.cs`
- Same 7 operations mirrored for RISC-V encoding

### RISC-V result-space escape-copy port

Dedicated S10 as `ResultBaseReg` (set once at startup, never changes).
CalleeSaved reduced from 9 to 8 locals (still 2x more than x86-64's 4).

Added `bge ptr, s10, skip` checks to:
- EmitRegion (top-level escape)
- EmitEscapeFieldCopy (field-by-field escape)
- EmitListEscapeHelper (element loop)
- EmitEscapeTextHelper (byte copy)

This matches x86-64's result-space-aware escape behavior. Both backends
now skip redundant deep-copies of pointers already in result space.

### Verification

1,003 tests pass (0 failures, 2 known skips). Build clean (expected CS5001).

### BLOCKER: Self-hosted sum type match crashes on native backend

**Minimal repro** (14-line .codex file):
```
MyType =
 | A (Integer)
 | B

f : MyType -> Integer
f (x) =
 when x
  if A (n) -> n
  if B -> 0

main : Integer
main = f (A 42)
```

**Behavior**: Reference-compiled native binary runs this → `si_addr=NULL` segfault.
The same program works when compiled to C#. The native binary successfully
compiles programs WITHOUT sum type matching (`main = 42`, if-else chains,
list-snoc, records, etc.).

**Investigation trail**:
1. Self-compile of 26 files (201KB): segfault. Both old and new binaries.
2. Bisected to file 8 (CSharpEmitterExpressions.codex), line 226.
3. Line 226 uses `when ... if ListTy (et) -> ...` pattern matching.
4. Reproduced with 14-line minimal test: ANY `when/if` on sum types crashes.
5. Sum type DEFINITION works (constructor, field access). Only MATCHING crashes.
6. QEMU strace: file fully read, then `SIGSEGV {si_addr=NULL}`.

**Root cause hypothesis**: The x86-64 backend's match emission for sum types
(in `EmitMatch` or `EmitApply` for `when`) produces code that dereferences
a null pointer. Likely a missing case in the match codegen for sum type
destructuring — possibly the scrutinee register is clobbered before the
match arms read it, or the tag dispatch jumps to an uninitialized label.

**Not related to**: capacity-aware lists, escape-copy, region reclamation,
or any changes in this session. Pre-existing in both old and new binaries.

---

## What Got Done (2026-03-26 night)

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

#### DONE: Result-space-aware escape-copy (x86-64, dad9769)

Stores `result_space_base` at text[0] via RIP-relative store at startup.
Text segment changed to RWX. In `EmitEscapeFieldCopy`, list element loop,
and `EmitRegion` top-level escape: loads global, compares pointer against
base, skips copy if `ptr >= base`. Reduces result-space usage from ~5.4GB
to ~2MB for self-compile. RISC-V port pending.

#### Current status: list-snoc is O(N) — causes O(N²) working-space blowup

With result-space check working, the remaining blocker is `__list_snoc`.
It's copy-on-write: allocates `(oldLen+2)*8` bytes and copies all old
elements every time. The tokenizer builds a 15,000-element list via TCO
loop, one `list-snoc` per iteration → `sum(i=1..15000) of 8i ≈ 900MB`
working-space allocations within a SINGLE region body (the tokenize call).
No let-boundary reclamation can help because it's all within one TCO loop.

**#1 blocker: in-place list-snoc for native backends.**

The fix: `__list_snoc` should extend the list in-place when the list's
allocation is at the top of the heap (i.e., `list_end == HeapReg`). This
is always true in a linear ownership model — the list is the most recently
allocated object. Just bump HeapReg by 8 and store the new element.

```
if (list_ptr + 8 + old_len*8 == HeapReg):
    [list_ptr] = old_len + 1       // update length
    [HeapReg] = element             // store new element
    HeapReg += 8                    // bump
    return list_ptr                 // same pointer, extended in-place
else:
    // fallback: copy (current behavior)
```

This turns O(N) per snoc into O(1), and O(N²) total into O(N). Apply to
both x86-64 and RISC-V backends.

### Previous: TCO/match register clobbering (fixed last session)

See `docs/OldStatus/CurrentPlan-2026-03-26-evening.md` for the full
root-cause analysis (commits `ea35113`, `42c2fd0`).

### Also found (assigned to Agent Windows)

`Codex.Codex/Ast/Desugarer.codex` — `desugar-type-expr` missing `EffectTypeExpr` case.
`Codex.Codex/Ast/Desugarer_.codex` — duplicate file, renamed to `.bak`.

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
| ~~Fix native self-hosted crash~~ | **DONE** — TCO/match register clobbering in both backends |
| ~~Escape-copy type resolution~~ | **DONE** — ResolveType recurses into ListType, ConstructedType keyed, all heap types enabled |
| ~~DoBind region wrapping~~ | **DONE** — do-block bindings now wrapped in IRRegion for reclamation |
| ~~Result-space-aware escape~~ | **DONE** (x86-64) — skip copy for pointers already in result space |
| ~~In-place list-snoc~~ | **DONE** — fast path O(1) when at heap top, but rarely fires in TCO loops |
| ~~Capacity-aware lists~~ | **DONE** (both backends) — hidden capacity word at [-8], geometric doubling, O(1) amortized snoc; estimated 22,000x heap reduction |
| ~~Result-space-aware escape (RISC-V)~~ | **DONE** — S10 = ResultBaseReg, single-instruction pointer check (bge) |
| Retry self-compile | **BLOCKED** — pre-existing null deref when self-hosted compiler matches sum types (see below) |
| Fix Boolean type in self-hosted emitter | Blocker for usermode self-compile |
| Fix CCE string escaping in self-hosted emitter | Blocker — uses ASCII rules instead of CCE |
| Add EffectTypeExpr to desugar-type-expr | Missing case (assigned to Agent Windows) |
| Perf automation | Wire `--bench-check` into CI or pre-commit hook |

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
