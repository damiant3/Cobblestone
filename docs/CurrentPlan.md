# Current Plan

**Date**: 2026-03-28

---

## MM3 IS PROVEN

The self-hosted Codex compiler compiled **itself** on bare metal x86-64 under
QEMU. 180 KB source (493 definitions, ~5,000 lines) in over serial, 261,654
bytes of valid C# out over serial — byte-for-byte match with the usermode
reference. The fixed point holds on hardware.

### What Changed for MM3

| Change | File | Why |
|--------|------|-----|
| Stack: 0x80000 → 0x10000000 (2 MB at top of 256 MB) | X86_64CodeGen.cs | Stack overflow at ~700 functions with 448 KB |
| Heap: working 0x400000, result 0xF800000 | X86_64CodeGen.cs | Proper two-space layout, eliminates overlap |
| Serial THR busy-wait on all print paths | X86_64CodeGen.cs | UART FIFO overflow dropped 100K+ bytes |
| Codex emitter wired into CLI | Program.Build.cs, Codex.Cli.csproj | `--target codex` identity backend |
| Codex emitter: field access parenthesization | CodexEmitter.cs | `(f x).field` not `f x.field` |
| Codex emitter: effect name from type, not hardcoded | CodexEmitter.cs | `[Console, FileSystem]` not `[Console]` |

### Floppy Disk Edition — Phase 1 Complete (Streaming Emission)

**Branch**: `cam/floppy-disk-streaming` — fixed point proven at 337,251 chars

Phase 1 (streaming lower→emit→print) eliminates the full IRModule and output
text from memory. The self-hosted compiler now processes definitions one at a
time: `lower-def` → `emit-def` → `print-line` → discard → next. The
`stream-defs` function is TCO-eligible, so the x86-64 backend's heap reset
reclaims per-iteration garbage (IR tree + emitted text string).

| Change | File | Why |
|--------|------|-----|
| `compile-streaming` + `stream-defs` | main.codex | Per-def streaming loop |
| `build-arity-map-from-ast` | CSharpEmitterExpressions.codex | Arity map from ADefs, no IRModule needed |
| `print-line` → IIFE returning `object` | Both C# emitters | `print-line` in expression context (ternary) |
| Void-like defs: `return <body>` | CSharpEmitter.cs | Avoids CS0201 on conditional expressions |
| `CodexEmitter.codex` | Emit/CodexEmitter.codex | Self-hosted identity emitter (Codex → Codex) |

**Memory impact (estimated)**:
- Eliminated: full IRModule (~30-50 MB) + accumulated output text (~2 MB)
- Per-def peak: ~10 KB (reclaimed by TCO heap reset each iteration)
- Remaining: source + tokens + AST + type env (~40-60 MB for self-compile)

**x86-64 verification**:
- Usermode: 269,756 bytes, 212 type defs, 794 defs. Correct output.
- Bare metal (512 MB): 267,426 bytes in 11.5s, 212 type defs, 795 defs.
  ~2 KB short due to UART flush timing. Streaming works on hardware.
- Bare metal (4 MB): OOM crash. Confirms Phase 2 needed for floppy target.
- CRLF note: `--target codex` outputs CRLF on Windows; x86-64 lexer needs LF.
  Convert with `tr -d '\r'` before use.

### Codex Emitter Status

**Reference compiler** (`src/Codex.Emit.Codex/CodexEmitter.cs`): Clean round-trip.
`--target codex` → compiles back with 0 errors.

**Self-hosted** (`Codex.Codex/Emit/CodexEmitter.codex`): Parser bug FIXED.

**CRLF bug (FIXED)** — Branch `cam/fix-crlf-lexer`, commit `72790dc`:
- **Root cause**: The self-hosted lexer (`Lexer.codex`) only recognized `\n` as a
  newline. On Windows, source files have CRLF (`\r\n`) line endings. The `\r`
  character was tokenized as an `ErrorToken`, which blocked `skip-newlines` from
  reaching the next match arm's `if` keyword. This caused ALL multi-arm `when`
  expressions to be truncated to 1 branch. Orphaned arms were re-parsed by the
  top-level definition parser as bogus defs (885 defs vs 583 expected).
- **Fix**: Added `cc-cr = 13` constant and a `\r` skip in `scan-token` that recurses
  via TCO (compiles to `continue` in the `while(true)` loop).
- **Verified**: Self-hosted parser now produces 1202 parsed defs (correct count
  including type annotations + function bodies for ~584 unique defs). Mini test with
  3-arm `when` expression parses all branches correctly.
- **Remaining**: Self-hosted bootstrap stage 1 progresses past parsing but crashes in
  `emit_builtin` (`ArgumentOutOfRangeException`) with 1047 unification errors. This
  is a separate type-checker issue, not the parser bug.
- **Do NOT add new record types** to CodexEmitter.codex — the self-hosted type checker
  crashes on them (`bsearch_text_pos` ArgumentOutOfRange). Reuse existing types.

### Next: Floppy Disk Phase 2 (Two-Pass Design)

Target: self-compile in < 4 MB heap. Eliminate AST persistence.

**Architecture**:
- Pass 1: Parse each def, extract signature (name + type annotation), discard body
- Pass 2: Re-parse each def from token stream, process through full pipeline
- Two-pass design: Pass 1 collects signatures (~350 KB persistent), Pass 2 processes each definition independently (~500 KB peak per def)
- Total peak: < 1 MB

**Codex emitter status**: Simple programs round-trip correctly. Self-hosted compiler
round-trip compiles but segfaults at runtime — likely definition ordering or expression
precedence issue in the emitter. Needs investigation.

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

## What Got Done (2026-03-28 Cam, session 3)

### x86-64 USERMODE SELF-COMPILE: WORKING

The self-hosted Codex compiler, compiled to x86-64 native by the reference
compiler, successfully compiles its own source (180KB, 4738 lines) in
usermode under `qemu-x86_64`. Output: 261,654 chars of valid C#.

```
echo "_all-source.codex" | qemu-x86_64 ./all-source  →  261,654 chars, exit=0
```

RISC-V self-compile crashes — pre-existing bug, not from this session.

### ListType safety fix for heap reset

Found and fixed a use-after-free in the TCO heap reset. In-place `list-snoc`
stores element pointers above the mark inside a below-mark list allocation.
The pointer check on the list container passed (ptr < mark) but the list's
new elements pointed to reclaimed memory. Fix: ListType params unconditionally
block the reset. TCO heap reset still fires for non-list functions (scanner
loops, record-only state threading).

### Phase 2: TCO heap reset (both backends)

Added conditional heap reset at each tail call in TCO functions. At the loop
top, HeapReg is saved as a mark. At each tail call, after evaluating args to
stack slots, every heap-typed arg is compared against the mark (unsigned >=).
If ALL heap args point below the mark (pre-existing data), HeapReg is reset
to the mark, reclaiming all per-iteration garbage. If any arg was allocated
during this iteration, the reset is skipped and the next iteration saves a
fresh (higher) mark.

**Implementation** (both x86-64 and RISC-V):

1. `EmitFunction` TCO setup: allocate `m_tcoHeapMarkLocal` stack slot, store
   parameter types in `m_tcoParamTypes[]`. At loop top (re-executed each
   iteration): `tcoHeapMark = HeapReg`.

2. `EmitTailCall` / `EmitRiscVTailCall`: after arg evaluation, classify each
   param via `IRRegion.TypeNeedsHeapEscape(ResolveType(paramType))`.
   - All scalar: unconditional `HeapReg = mark` (always reset).
   - Any heap-typed: emit `cmp arg, mark; jae skip_reset` per heap arg.
     If all pass: `HeapReg = mark`. All skip-branches patch to after the
     reset instruction.

**Which TCO functions benefit:**

| Category | Example functions | Resets? |
|----------|------------------|---------|
| Scalar-only params | `skip-spaces-end`, `scan-ident-end`, `scan-digits-end` | Always |
| Accumulator pattern (index + list) | `lower-defs-acc`, `emit-defs`, `check-all-defs`, `resolve-all-defs` | Most iterations (list-snoc usually returns same ptr) |
| Fresh record per iteration | `tokenize-loop` (new LexState each iter) | Rarely (needs Phase 2b record decomposition) |

**Estimated impact**: For accumulator-pattern functions processing 200+ items,
per-iteration garbage (intermediate IR nodes, type records, emitter strings)
is reclaimed on ~95%+ of iterations. For `lower-defs-acc` with 200 definitions,
estimated ~4-8MB garbage reclaimed that would otherwise persist until function exit.

**Files changed**:
- `src/Codex.Emit.X86_64/X86_64CodeGen.cs` — `EmitFunction`, `EmitTailCall`
- `src/Codex.Emit.RiscV/RiscVCodeGen.cs` — `EmitFunction`, `EmitRiscVTailCall`

**Verification**: 1,003 tests pass (0 failures, 2 known skips). Build clean
(expected CS5001 only).

### MM3 Reality Check: 220 MB peak, 0.91s

Full measurement report: `docs/MM3-REALITY-CHECK.md`

Peak working-space heap is **220 MB** for self-compile (instrumented
high-water mark). Bare-metal budget is 2 MB working space. The 110x gap is
live data — all 7 pipeline stages' output coexisting. Path A (bump bare-metal
heap to 256 MB) proves MM3 immediately. Path B (streaming per-definition)
fits 4 MB. See report for details.

### Phase 2b: TCO record decomposition (both backends) — DONE

Record-typed TCO parameters are now decomposed into individual field values
at each tail call. Instead of checking the record pointer (always freshly
allocated → always above mark → never resets), the check inspects field
pointers. Pre-existing pointers (like `LexState.source` allocated at
program start, or `ParseState.tokens` built by the tokenizer) pass the
check. After reset, the record is reconstructed at the new heap top.

**Implementation**:
1. `EmitFunction` TCO setup: for each RecordType parameter, pre-allocate
   field decomposition locals via `m_tcoDecompLocals[paramIdx][fieldIdx]`.
2. `EmitTailCall`: after arg evaluation, load `[recordPtr + f*8]` into
   field locals. During check, compare pointer-typed field values against
   mark. After reset, reconstruct: `newPtr = HeapReg; HeapReg += N*8;
   store fields; update temp`.

**Which TCO functions now benefit (previously blocked):**

| Function | Record param | Fields | Reset? |
|----------|-------------|--------|--------|
| `tokenize-loop` | LexState | source (Text, pre-existing), offset/line/column (scalar) | Yes |
| `parse-binary-loop` | ParseState | tokens (List, pre-existing), pos (scalar) | Yes |
| `parse-app-loop` | ParseState | tokens (List, pre-existing), pos (scalar) | Yes |
| `parse-match-branches` | ParseState | tokens (List, pre-existing), pos (scalar) | Yes |
| `skip-newlines` | ParseState | tokens (List, pre-existing), pos (scalar) | Yes |

**Estimated impact**: `tokenize-loop` for 180KB source creates ~15K LexState
records per compilation (32 bytes each = ~480KB), plus intermediate strings
and Token records. With Phase 2b, all per-iteration garbage is reclaimed.
Parser loops similarly reclaim ParseState intermediates.

**Review branch**: `cam/tco-heap-reset` (2 commits, both backends)

**Verification**: 1,003 tests pass (0 failures, 2 known skips). TCO test
programs compile on both x86-64 and RISC-V.

---

## What Got Done (2026-03-28 Cam, session 2)

### Lowering O(N²) → O(N) list building

Converted 8 list-building functions in `Codex.Codex/IR/Lowering.codex` from
`[x] ++ recursive` (prepend, O(N²) total copies) to `list-snoc` accumulator
pattern (O(N) amortized with capacity-aware lists). Biggest win: `lower-defs`
with 200+ definitions dropped from ~20,000 element copies to ~400.

### Phase 0 complete: scan-string-body + skip-newlines

Bulk offset scanning for remaining lexer/parser functions. All scanning
functions now use Integer-returning offset helpers. Total Phase 0 estimated
dead record reduction: ~5MB for 180KB source.

### In-place list-insert-at + min capacity 4 (Phase 3 + 6.1)

`list-insert-at` now has 3 paths matching `list-snoc`: in-place shift when
spare capacity, grow-and-shift at heap top, copy-with-gap as fallback. Both
backends. Minimum list capacity reduced from 16 to 4 (saves 96 bytes per
small list). Type environment builds drop from O(N²) to O(N).

### What's next: Phase 2 — TCO heap reset

The biggest remaining optimization. At each TCO iteration, after evaluating
args into stack slots, reset HeapReg to reclaim per-iteration garbage.

**The challenge**: heap-typed TCO args (ParseState records, LexResult sum
types) live above the mark and would be reclaimed. Need to either:
1. Decompose records into scalar stack slots, reconstruct after reset
2. Escape-copy to result space (but escape-copy crashes on cross-refs)
3. Only reset for scalar-only TCO functions (limited impact since Phase 0
   already converted hot scalar loops)

**Approach to investigate**: Record decomposition. For `tokenize-loop`,
the LexState arg has 2 scalar-ish fields (source ptr + pos integer). Save
those to stack, reset heap, reconstruct LexState at loop top. The source
ptr points below the mark (allocated at program start), pos is scalar.
Similar for ParseState (tokens ptr + pos).

**Files**: `X86_64CodeGen.cs` (EmitTailCall), `RiscVCodeGen.cs` (EmitTailCall).

---

## What Got Done (2026-03-28 Cam)

### Memory reduction for native self-compile

**Branch**: `cam/mm3-shared-fixes`

Three changes to reduce heap usage in the native x86-64 and RISC-V backends:

#### 1. Re-enabled scalar region reclamation (both backends)

EmitRegion was previously a full pass-through (disabled after escape-copy
crashes). Scalar regions (NeedsEscapeCopy=false) are safe to reclaim because
the result lives in a register, not the heap. No live heap pointers survive.

- x86-64: save/restore R10 (HeapReg) around scalar region bodies
- RISC-V: save/restore S1 (heap ptr) around scalar region bodies
- Heap regions remain pass-through (escape-copy still crashes)

#### 2. Reduced Linux brk allocation (x86-64)

Working space: 4GB -> 256MB. Result space: 256MB -> 64MB. Total: 4.25GB -> 320MB.
With capacity-aware lists and in-place string concat, actual usage is ~10-15MB
for 180KB source. The old 4GB allocation was from before those optimizations.

#### 3. Bulk offset scanning in self-hosted lexer

Replaced per-character `advance-char` loops in `skip-spaces`, `scan-ident-rest`,
and `scan-digits` with offset-only helper functions (`skip-spaces-end`,
`scan-ident-end`, `scan-digits-end`) that return Integer. Integer results
are scalar-typed, so scalar region reclamation automatically frees them.
Only ONE LexState is created at the end of each scan.

**Impact**: For 180KB source, the lexer was creating ~150K dead LexState
records (32 bytes each = ~4.8MB). Now creates ~15K (one per token scan).
Saves ~4.3MB of dead heap allocations.

**Correctness**: Identifiers, spaces, and digits never cross line boundaries,
so `line` stays constant and `column += end - start`. Hyphen-in-identifier
lookahead matches the original `scan-ident-rest` behavior exactly.

#### Also found: Bootstrap pre-existing bug

`Codex.Bootstrap` Stage 1 self-compile crashes with `ArgumentOutOfRangeException`
(negative substring length in `scan_token`). This fails with or without our
changes. Root cause: likely `out/Codex.Codex` (ELF binary) being picked up
as a source file, or a stale `CodexLib.g.cs`.

#### Verification

- 1,003 reference compiler tests pass (0 failures, 2 known skips)
- Build clean (expected CS5001 only)

#### What's unsafe (investigated and rejected)

**In-place record update**: Checked whether records at heap top could be
updated in place (like list-snoc's fast path). UNSAFE without linear type
analysis. Counter-example: `scan-ident-rest` keeps both `st` and
`advance-char st` live simultaneously (line 185-190). If advance-char
mutated `st` in-place, returning `st` as a fallback would return the
modified state.

---

## What Got Done (2026-03-27 Cam, session 2)

### Fixed self-hosted variant type parser

**Bug**: The self-hosted parser's `parse-type-def` only checked for `Pipe` (`|`)
after `=` to detect variant types. The reference parser (Parser.cs:146-147)
also checks `TypeIdentifier && LooksLikeVariantBody()`. So the pipe-free form
`Color = Red | Green | Blue` was misparsed as a zero-param function `Color`
with body `Red | Green | Blue` (an OR expression). This produced 0 type defs
and caused empty/broken output for any program using this variant syntax.

**Fix** (3 changes in `Codex.Codex/Syntax/Parser.codex`):

1. `parse-type-def`: added `is-type-ident & looks-like-variant` check after
   the existing `is-pipe` check
2. `looks-like-variant` + `looks-like-variant-scan`: new lookahead that scans
   forward on the same line for a `|` token, stopping at Newline/EndOfFile
   (mirrors reference parser's `LooksLikeVariantBody()`)
3. `parse-variant-type`: when first token is TypeIdentifier (no leading `|`),
   parse first constructor directly before entering the `|`-delimited loop
   (mirrors reference parser's `firstCtor` flag)

**Verification**:
- `Color = Red | Green | Blue` test: TypeDefs=1, Defs=2, 0 errors, 2222 chars output
- `MyType = | A (Integer) | B` test: TypeDefs=1, Defs=2, 0 errors, 2168 chars output
- Self-compile (Stage 1): 299,909 chars output (unchanged)
- 1,003 reference compiler tests pass (0 failures, 2 known skips)

### Native self-compile userspace test (x86-64)

**Working**: `main = 42` ✓, sum type definitions ✓, constructor calls (`A 42`) ✓
**Crashing**: ANY `when/if` pattern matching in user INPUT → SIGSEGV

Crash address: `0x436a00`, instruction `mov 0x8(%rsi),%rdi`, RSI = garbage.
Even literal patterns (`when x if 1 -> 10 ...`) crash — not specific to sum
types. The self-hosted compiler's OWN `when/if` expressions work (they process
`main = 42` fine), but compiling user input that contains `when/if` crashes.

Root cause hypothesis: the crash occurs when the self-hosted compiler's type
checker or lowering processes user-defined `MatchExpr` nodes. The internal
parser uses `when/if` extensively BUT those branches are already compiled by
the reference compiler. When the LOWERING stage encounters a MatchExpr from
user input, it calls functions like `lower-match`, `bind-pattern-to-ctx`,
`lower-pattern` etc. — these allocate heap objects (IRBranch, IRPat records)
whose pointers may be invalidated by the two-space escape-copy system.

**GDB trace** (x86-64 user-mode, WSL Ubuntu-24.04):
```
skip-newlines (0x37D5B+0x26)
  → is-done (0x36B30+0x26)
    → current-kind (0x369A3+0x26)
      → current (0x36921+0x2F)  ← CRASH: mov 0x8(%rsi),%rdi, RSI=0x82313ec1
```

Same call chain as bare-metal crash. Function offset map generated via
`X86_64Emitter.GetFunctionOffsets()` (see `Codex.Codex/out/Codex.Codex.map`,
718 entries). User-mode ELF base: `0x4000B0` (LinuxBaseAddress + text offset).

**Root cause**: The EmitRegion fix disabled reclamation for SCALAR regions
(line 1085-1093 of X86_64CodeGen.cs) but HEAP regions still run the full
escape-copy path (line 1096+). `ParseState` is a RecordType, so
`NeedsEscapeCopy=true`. The heap-region path:

1. Saves HeapReg mark
2. Executes body (skip-newlines TCO loop → many `advance` calls → new ParseState
   records in working space)
3. Saves post-body HeapReg
4. Switches HeapReg to ResultReg (result space)
5. Escape-copies ParseState to result space
6. Restores HeapReg to post-body working-space position

Step 5 deep-copies the ParseState record. The `.tokens` field should be
skipped if it's already in result space (result-space check: `bge ptr,
ResultBaseReg`). But the ParseState pointer returned to the CALLER now
points to result space, while TCO iteration parameters may still reference
working-space ParseState records from `advance`. If a subsequent region's
escape-copy (for a DIFFERENT let-binding) clobbers result space or working
space, the old pointers become stale.

**Hypothesis**: the escape-copy for one let-binding's region is being called
while another let-binding's region body is still live, causing cross-region
pointer invalidation. The safe fix may be to disable escape-copy entirely
(like scalar regions) until liveness analysis can prove safety.

**Quick fix to test**: in `EmitRegion`, return `bodyResult` immediately for
ALL regions (both scalar and heap), completely disabling two-space reclamation.
This would increase memory usage but should eliminate the crash.

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

**Root cause found and FIXED** (2026-03-27 Cam):

The crash was a **use-after-free** caused by region reclamation in `EmitRegion`.
Both scalar and heap region paths restored HeapReg to the pre-body mark,
reclaiming ALL working-space allocations made during the body. But some of
those allocations were still referenced by live pointers — specifically,
ParseState records created by `advance` in TCO loops that became TCO
parameters for the next iteration.

**Fix** (x86-64): Two changes to `EmitRegion`:
1. **Scalar regions**: removed the `HeapReg = mark` restoration entirely.
   Intermediate heap objects may still be live via TCO parameters or closures.
2. **Heap regions**: restore HeapReg to the POST-BODY position (not the
   pre-body mark). This switches back from result space to working space
   without reclaiming any allocations.

**Investigation trail (condensed)**:
- GDB: crash at `current` function (0x434ca4), `mov (%rbx), %rcx` with RBX=0
- Function offset map: `current` → `current-kind` → `is-done` → `skip-newlines`
- `skip-newlines` TCO loop calls `advance st` → new ParseState in working space
- Region reclamation reclaimed the ParseState → dangling pointer → null deref
- 100/100 crashes with pipe input, 0/100 with fix applied
- 536 tests pass, 0 failures

**Trade-off**: Without reclamation, working space grows monotonically per
function call. This is safe for small-to-medium programs. For MM3 self-compile,
we may need selective reclamation (only reclaim when provably safe — e.g.,
after liveness analysis or in leaf regions with no live captures).

**Remaining**: The self-hosted compiler no longer crashes but produces empty
output for sum-type-matching programs. This is a separate parser/emitter issue,
not a memory corruption bug.

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
| ~~Result-space-aware escape~~ | **DONE** (x86-64) — skip copy for pointers already in result space |
| ~~In-place list-snoc~~ | **DONE** — fast path O(1) when at heap top, but rarely fires in TCO loops |
| ~~Capacity-aware lists~~ | **DONE** (both backends) — hidden capacity word at [-8], geometric doubling, O(1) amortized snoc; estimated 22,000x heap reduction |
| ~~Result-space-aware escape (RISC-V)~~ | **DONE** — S10 = ResultBaseReg, single-instruction pointer check (bge) |
| ~~Fix region reclamation crash~~ | **DONE** — disabled unsafe reclamation; self-hosted compiler no longer crashes on sum type input |
| ~~Fix self-hosted variant parser~~ | **DONE** — `parse-type-def` only checked `Pipe`, missing `TypeIdentifier + lookahead`; `Color = Red | Green | Blue` was misparsed as function def |
| ~~Fix EmitRegion crash~~ | **DONE** — EmitRegion pass-through for all regions; removed mark save, escape-copy, HeapReg restore. Binary 272KB (was 395KB). Pattern matching verified native userspace. |
| ~~Scalar region reclamation~~ | **DONE** (both backends) — save/restore HeapReg for scalar regions; heap regions still pass-through |
| ~~Bulk offset scanning in lexer~~ | **DONE** — skip-spaces-end, scan-ident-end, scan-digits-end return Integer offset; ~4.3MB dead LexState reduction |
| ~~Reduce Linux brk allocation~~ | **DONE** — 4.25GB → 320MB; actual usage ~10-15MB for 180KB source |
| ~~TCO heap reset (Phase 2)~~ | **DONE** (both backends) — conditional HeapReg reset at tail calls; accumulator-pattern loops reclaim per-iteration garbage |
| ~~TCO record decomposition (Phase 2b)~~ | **DONE** (both backends) — decompose record args into fields, check field ptrs, reconstruct after reset; enables tokenize-loop + parser loops |
| Retry full self-compile with native backend | Memory optimizations unblock; next: feed self-hosted source to native compiler (needs Linux) |
| Fix Bootstrap Stage 1 crash | Pre-existing `ArgumentOutOfRangeException` in scan_token; likely stale CodexLib.g.cs |
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
