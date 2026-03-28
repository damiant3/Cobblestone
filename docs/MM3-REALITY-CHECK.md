# MM3 Reality Check

**Date**: 2026-03-28
**Author**: Cam (session 3)
**Branch**: `cam/tco-heap-reset` (merged to master)

---

## What We Measured

The self-hosted Codex compiler (26 files, 4738 lines, 180KB source) compiled
itself in usermode on x86-64 under QEMU. We instrumented the binary with a
heap high-water-mark tracker (updated before every scalar region reclamation)
and collected timing, RSS, and heap position data.

This is the first time the self-hosted compiler has run a full self-compile
in usermode on a native backend.

---

## Hard Numbers

### x86-64 Self-Compile (full pipeline: lex, parse, resolve, typecheck, lower, emit)

| Metric | Value |
|--------|-------|
| Peak working-space heap | **220 MB** |
| Result-space usage | 0 bytes (escape-copy disabled) |
| Max RSS (clean, no instrumentation) | 234 MB |
| Wall time | **0.91 seconds** |
| Output | 261,654 chars of valid C# |
| Binary size | 280 KB |
| Input | 180 KB source, 4738 lines |
| brk allocation | 320 MB (256 MB working + 64 MB result) |

### x86-64 Compiling Factorial (trivial input)

| Metric | Value |
|--------|-------|
| Peak working-space heap | 72 KB |
| Max RSS | 8 MB |
| Wall time | 0.03 seconds |

### RISC-V

| Metric | Value |
|--------|-------|
| Factorial RSS | 7 MB |
| TCO stress (1M iterations) RSS | 7 MB (zero heap growth) |
| Self-compile | Crashes (pre-existing bug, not from this session) |

### TCO Test Suite

| Backend | Tests | Result |
|---------|-------|--------|
| x86-64 | 10/10 | All pass (includes 1M-iteration stress test) |
| RISC-V | 10/10 | All pass |

---

## What the Optimizations Bought

Before this session's work, the estimated heap for self-compile was >1 GB
(O(N^2) list-snoc copies alone consumed ~900 MB for the tokenizer). The
current 220 MB peak reflects:

| Optimization | Estimated savings |
|--------------|-------------------|
| Capacity-aware lists (geometric doubling) | ~900 MB (O(N^2) to O(N)) |
| Scalar region reclamation | Continuous — reclaims intermediate integers/booleans |
| Bulk offset scanning (Phase 0) | ~4.3 MB dead LexState records |
| In-place list-insert-at + min capacity 4 | ~96 bytes per small list |
| TCO heap reset (Phase 2a) | Reclaims per-iteration garbage in scanner loops |
| TCO record decomposition (Phase 2b) | Enables reset for tokenize-loop, parser loops |
| ListType safety fix | Prevents use-after-free from in-place list-snoc |

The 220 MB that remains is **live data** — it must exist simultaneously:

- Token list: ~15K tokens with string data
- Full AST: thousands of Expr/Pat/TypeExpr nodes
- Type environment: UnificationState, TypeEnv, type variable maps
- IR module: IRDefinition for every function
- Output text: 261K chars of emitted C#

---

## The MM3 Gap

### Budget Comparison

| Resource | Usermode | Bare-metal (current) | Gap |
|----------|----------|---------------------|-----|
| Working space | 256 MB (220 MB used) | 2 MB | **110x** |
| Result space | 64 MB (0 MB used) | 2 MB | N/A |
| Total heap | 320 MB | 4 MB | **80x** |
| Stack | Linux managed | 512 KB | OK (measured ~32KB used) |

### Why 220 MB

The self-hosted compiler runs all 7 pipeline stages in sequence, but keeps
ALL intermediate data alive until `main` returns:

```
source (180KB)
  -> tokens (List Token)        ~2 MB estimated
  -> AST (List Def)             ~10-20 MB estimated
  -> resolved AST               ~10-20 MB estimated (shares structure)
  -> typed AST + type env       ~20-40 MB estimated
  -> IR module (List IRDef)     ~30-50 MB estimated
  -> output text                ~2 MB
  + garbage between regions     ~100 MB (reclaimed by scalar regions)
```

The **peak** occurs during lowering/emission when the IR module coexists with
the AST, type environment, and partially-built output. All are reachable from
local variables in the `compile` function's let-chain.

---

## Paths to MM3

### Path A: Larger Bare-Metal Heap (easiest, least elegant)

Bump the bare-metal heap from 4 MB to 256+ MB. Real hardware with 512 MB+
RAM makes this trivial. The 268 KB kernel leaves plenty of physical memory.

**Pros**: Zero compiler changes. Works today.
**Cons**: Doesn't fit on a floppy. Requires real hardware with sufficient RAM.

### Path B: Stage-by-Stage Streaming (most impactful)

Restructure `compile` to process one definition at a time:

```
for each def in parse(tokenize(source)):
    emit(lower(typecheck(resolve(def))))
```

Each definition flows through the full pipeline and is emitted immediately.
Only one definition's AST/IR is live at a time. The token list is consumed
incrementally. Peak heap drops from ~220 MB to roughly the size of the
largest single definition (~1-3 MB).

**Pros**: Fits in 4 MB bare-metal. Scales to any source size.
**Cons**: Requires significant refactoring. Type checking currently needs
global context (type environment built from all definitions). Would need a
two-pass approach: first pass collects type signatures, second pass compiles
each definition.

### Path C: Inter-Stage Heap Reset (middle ground)

Add explicit heap boundaries between pipeline stages. After tokenization,
copy the token list to a fresh region and reset the tokenizer's working
space. Repeat between each stage.

**Pros**: Less refactoring than Path B. Each stage gets the full heap.
**Cons**: Still peaks at the largest single stage. Copy overhead. Requires
careful separation of live data from garbage.

### Path D: Hybrid (recommended)

1. **Immediate**: Path A for first MM3 proof (bump to 256 MB, prove it works)
2. **Near-term**: Path C to reduce peak per-stage
3. **Long-term**: Path B for the floppy-disk vision

---

## What Works Today

- x86-64 usermode self-compile: **verified working** (0.91s, 261K chars output)
- TCO heap reset with record decomposition: **both backends**
- ListType safety invariant: **identified and fixed**
- 1,003 reference compiler tests pass
- 20/20 QEMU tests pass on both backends

## What Doesn't Work Yet

- RISC-V usermode self-compile: pre-existing crash (not from this session)
- Bare-metal self-compile: heap too small (4 MB vs 220 MB needed)
- Result-space escape-copy: disabled (crashes on cross-references)

---

## Appendix: Measurement Methodology

**Peak heap**: Instrumented `EmitRegion` (scalar path) with a high-water-mark
global stored in the text section at offset 8. Before every HeapReg restore,
compared HeapReg against the stored maximum and conditionally updated. Read
back at `__start` exit and printed to stderr as ASCII decimal. The
instrumentation added ~7x runtime overhead (7.35s vs 0.91s) due to the
per-region memory load/compare/store.

**RSS**: `/usr/bin/time -v` wrapping `qemu-x86_64` / `qemu-riscv64`. RSS
includes QEMU runtime overhead (~5-8 MB baseline for an empty program).

**Timing**: Wall clock from `/usr/bin/time -v`, clean build without
instrumentation. QEMU user-mode on WSL Ubuntu 24.04, host Windows 11,
AMD CPU.
