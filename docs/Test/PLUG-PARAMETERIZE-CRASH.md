# Plug Compile Crash: parameterize-walk-children Stack Overflow

**Status:** CRASH RESOLVED (verified 2026-05-29, reek); one residual
perf item open. The crash was not stack overflow but a CHECK-phase deck
overflow — fixed by raising `survey-check-mul` to 400
(`codex/Core/BuildSettings.codex`) and promoting CDX9002 from warning to
error. The HTML plug now builds past 76KB.

**Residual open item — re-verified 2026-05-29 (reek). The timeout is REAL
and current (not stale), but its attribution to `parse-ir-chapter` is
wrong.** Measured end-to-end: `codex\plugs\html\run.ps1 -Src
apps\explorer\ExplorerTheme.codex` → IR-CCE is 6,236,094 bytes (~6.2 MB)
and the plug run hits the 300 s cap (346 s total = ~46 s IR-compile + 300 s
plug-run, 0 bytes output). So the migration is still blocked. BUT
micro-benchmarks show every parser/emitter PRIMITIVE is O(1)/sub-quadratic
at 1M scale — `char-code-at` (literal and large-Text index), `list-push`,
`list-at`, and `&` (text append) all measured flat (ratios ~1.0). So the
blow-up is an emergent algorithmic-or-memory cost at the 6.2 MB /
millions-of-nodes scale, NOT primitive complexity — and it is at least as
likely in `emit-html-chapter` (HtmlEmitter builds output via recursive `&`
chains, the pattern the compiler's own IRTextEmitter avoids with
list-snoc + text-concat-list) as in the parser.

Ruled out this pass: (1) transport — the html plug uses memory-mapped
`-input`/`-output`, not NE2K TCP, so the time is real compute, not I/O;
(2) missing IR DCE — the compiler already prunes via `ir-prune-unreachable`
during IR-CCE emit (this is why `ir-dce.ps1` was deleted), but it does NOT
shrink ExplorerTheme because a library chapter has no `opening` to prune
from. The plug also BUILDS fine now (the CHECK-deck crash is fixed); only
the run times out.

**Next step: profile the actual plug run** — instrument `HtmlPlug` with
`prof-start`/`prof-dump`, run on a smaller IR that completes, read the
histogram to pinpoint the hot frame; or sweep IR size (1/2/4 MB) to confirm
O(n²) and isolate parse-vs-emit. Micro-benchmarks have exhausted their
usefulness; the next move is measuring the real pipeline.
**Severity (residual):** Blocks migrating plug types to `cites UI chapter`.

**Original status:** Active. Discovered 2026-05-27 by val.
**Severity:** Blocks HTML plug growth past ~75KB source.

## Summary

The HTML plug (`codex/plugs/html/HtmlEmitter.codex`) crashes during
compilation when the bundled source exceeds approximately 75KB. The
crash occurs in the TypeChecker's `parameterize-walk-children`
function due to stack overflow from deep recursion on nested type
trees.

## Reproduction

```powershell
# Build the plug source (bundles foreword deps + 4 chapters)
pwsh codex/plugs/html/build.ps1
```

The build bundles 5 chapters (Foreword--Sort, PlugTypes, IRTextParser,
HtmlEmitter, HtmlPlug) into a single source file. At ~75,447 bytes
(1,853 lines), the plug compiles successfully. At ~76,339 bytes
(1,870 lines), it crashes.

## Crash Details

```
!EXC=0d RIP=00000000002d31b2 R13=0x9 R14=0x11 R10=0x02cd9e81
CallR=000000007fffcc48 CR2=0x0
```

- **Exception:** 0x0d (General Protection Fault)
- **Function:** `parameterize-walk-children+0x52A`
  (TypeChecker.codex:139)
- **Phase:** Frontend (CHECK) — `DBG:frontend src=76331` is the
  last debug line before the crash
- **Stack depth at crash:** 13,240 bytes from stack top
  (0x80000000 - 0x7fffcc48 = 13,240). Stack is nearly exhausted.
- **CR2 = 0:** Not a page fault — the GPF is from accessing an
  unmapped address after the stack collided with the heap.

## Root Cause Analysis

`parameterize-walk-children` (TypeChecker.codex:139-198) recursively
walks CodexType trees to parameterize type variables. It pattern-matches
on type constructors:

- `FunTy(param, ret)` → recurse into both param and ret
- `ListTy(elem)` → recurse into elem
- `RecordTy(name, args, fields)` → recurse into args list then fields
- `SumTy(name, args, ctors)` → recurse into args list then ctors
- `ConstructedTy(name, args)` → recurse into args list

Each recursive call pushes a stack frame (prologue: push rbp, push rbx,
push r12, push r13, push r14 = 40 bytes + locals). For a type tree N
levels deep, the stack usage is ~N * frame_size.

The HTML plug source contains many text `&` concatenation chains. A
chain like `"a" & "b" & "c" & ... & "z"` with K operands creates a
left-leaning binary tree K levels deep. The type of this expression
is `Text` but the type checker walks the full expression tree and
parameterizes the result type at each call site.

The critical pattern is a function that concatenates the results of
many other functions that themselves contain deep concatenation chains:

```codex
emit-dom-builtins : Text
emit-dom-builtins =
  emit-dom-core & emit-dom-ajax & emit-dom-state & emit-dom-events
  & emit-dom-reactive & emit-dom-dialog & emit-dom-a11y & emit-dom-anim
  & emit-dom-persist & emit-dom-sd & emit-dom-theme & emit-dom-widget
```

Each sub-function (e.g., `emit-dom-theme`) itself contains 15-20 `&`
concatenations. The type checker parameterizes the return type of
`emit-dom-builtins`, which requires walking into the return types of
all 14 sub-functions, each of which walks into 15-20 concatenation
nodes. Total recursion depth: 14 * ~18 = ~252 levels, each consuming
~100-200 bytes of stack.

At 75KB source the stack usage is near the limit. At 76KB it crosses
it.

## Evidence

### Successful build (75,447 bytes)

```
[html-plug] bundled 1853 lines, 75447 bytes
[html-plug] OK: html-plug.cdx (210961 bytes)
```

### Failed build (76,339 bytes — 892 bytes more)

```
[html-plug] bundled 1870 lines, 76339 bytes
  compile: crash with 2048MB, retrying with 4096MB
FAIL: compile errors
```

The delta is 892 bytes / 17 lines — adding 4 short functions
(`save_theme`, `load_theme`, `save_layout`, `load_layout`). The
extra definitions add just enough type-walking depth to overflow
the stack.

### RAM is not the issue

Increasing RAM from 2048MB to 4096MB does not help. The crash
address (CallR = 0x7fffcc48) shows the stack is at its ceiling,
not that the heap has grown into it. R10 (heap pointer) is at
0x02cd9e81 (~45MB) — well below the stack. This is pure stack
depth, not heap pressure.

## Affected Code

- `codex/compiler/Types/TypeChecker.codex:139-198` —
  `parameterize-walk-children` (recursive type walker)
- `codex/compiler/Types/TypeChecker.codex:107-137` —
  `parameterize-walk` (calls `parameterize-walk-children`)

## Potential Fixes

### Option A: Iterative type walking

Convert `parameterize-walk-children` from recursive to iterative
using an explicit stack (a list of work items). This eliminates the
call-stack dependency entirely. The pattern:

```
parameterize-walk-iter : UnificationState, List ParamEntry,
    List CodexType -> WalkIterResult
```

Push child types onto a work list instead of recursing. Process
the list in a loop.

### Option B: Tail-call optimization for linear type chains

`FunTy(param, ret)` recurses into both `param` and `ret`. The
`ret` call is in tail position if the function returned
`parameterize-walk st entries ret` instead of constructing a
`WalkResult` after the call. Restructuring the function to process
`param` iteratively and tail-call into `ret` would halve the stack
depth for function-type chains.

### Option C: Increase stack size

Change `bare-metal-stack-top` or reduce `bare-metal-heap-base` to
give more stack room. Current stack region is 0x80000000 downward,
heap starts at 0x600000 — there's ~2GB of gap. The issue is that
the stack starts at the very top and only has ~2GB of virtual
address space total. With 4GB RAM the stack top would be at
0x100000000, giving much more room. But this hits the 4GB barrier
(PCI MMIO reservation).

A simpler variant: the prologue collision check (`cmp rsp, r10`)
should catch this before the GPF. If RSP descends past R10, the
`__out_of_memory` handler fires. But in this case the stack hasn't
reached R10 (heap at 45MB, stack at ~2GB - 13KB) — the GPF
happens because the stack frame writes to an address that triggers
a protection fault, likely from hitting a page boundary that
isn't mapped.

### Option D: Reduce type tree depth at the source level

The immediate workaround used in HtmlEmitter: break long `&`
chains into named sub-expressions so no single function has more
than ~8 concatenations. This keeps the type tree shallow. Each
new sub-function adds one level of indirection but caps the
recursion depth per call.

This is what val has been doing (splitting `emit-dom-widget` into
`emit-dom-widget-style`, `emit-dom-widget-create`,
`emit-dom-widget-mount`, etc.). It works up to ~75KB but the
ceiling is hard.

## Impact

The HTML plug cannot grow past ~75KB of bundled source. This blocks:

- Adding more JS runtime builtins (theme persistence, etc.)
- Migrating ExplorerTheme from inlined types to foreword cites
  (would add ~37KB of foreword UI chapters)
- Any significant expansion of the plug emitter

## Workaround

Split all text-concatenation functions so no single function has
more than ~6-8 `&` operands. Name each sub-expression. This
keeps the type tree shallow at the cost of more function definitions.

Current HtmlEmitter uses 13 named sub-sections for the DOM builtins,
each split into 2-3 sub-functions. The practical ceiling with this
approach is approximately 75KB.

## Fix History

### CL 2586 — Ground-type early-out (fester)

Added `| IntegerTy | NumberTy | TextTy | BooleanTy | NothingTy |
VoidTy | ProofTy | ErrorTy -> WalkResult { walked = ty, ... }` as
the first branch in `parameterize-walk`. Leaf types return in 1
frame instead of 3. Reduces stack usage per `&` operator from 3
frames to 1.

**Test result (val, 2026-05-27):** Still crashes at 76,339 bytes
(1,870 lines). The ground-type early-out saves frames on leaf nodes
but the `FunTy` chain itself still recurses for every `&` operator.
The dominant cost is the `FunTy(param, ret)` branch in
`parameterize-walk-children` which recurses into both `param` and
`ret`. For `Text & Text` the param is `TextTy` (now fast) but the
recursion into `ret` still costs a full frame per level.

### Next fix needed: FunTy ret-chain iteration (Option B)

The `FunTy` branch in `parameterize-walk-children` currently does:

```
is FunTy (param) (ret) ->
  let pr = parameterize-walk st entries param
  in let rr = parameterize-walk (pr.state) (pr.entries) ret
  in WalkResult { ... FunTy (pr.walked) (rr.walked) ... }
```

The `ret` call is nearly in tail position. If restructured to
process `param` and then loop on `ret` instead of recursing, the
stack depth for a chain of N `&` operators drops from O(N) to O(1):

```
is FunTy (param) (ret) ->
  let pr = parameterize-walk st entries param
  in parameterize-walk-funty-chain (pr.state) (pr.entries) (pr.walked) ret
```

Where `parameterize-walk-funty-chain` iteratively accumulates the
chain. This is the highest-impact remaining fix.

### CL 2586 v2 — FunTy inline handling (fester)

Added `FunTy(param, ret)` as a direct branch in `parameterize-walk`,
eliminating the bounce through `parameterize-walk-children`. Per-level
frame count drops from 2 to 1.

**Test result (val, 2026-05-27):** Still crashes at 76,339 bytes.
The crash moved from `parameterize-walk-children+0x52A` to
`find-param-entry+0x136`. Same stack depth (CallR = 0x7fffcc48).
The fix successfully halved the parameterize-walk frames, but the
total call chain from `check-def` → `infer-expr` → ... →
`parameterize-walk` → `find-param-entry` is still too deep. The
bottleneck is now the aggregate depth of the entire type-checking
call stack, not just the parameterize-walk recursion.

Stack at crash: 13,240 bytes from top (unchanged). The fix freed
frames in parameterize-walk but the same total depth is consumed
by the broader call chain.

## Root Cause Found (CL 2595/2596, fester)

**The GPF was NOT stack overflow.** It was a CHECK phase deck
overflow. The deck survey (`source_len × 95 + 1MB`) underestimated
deck usage for type-dense source. PlugTypes.codex redefines the
compiler's entire type system (~40 types) in 368 lines / 10KB,
creating ~15x the normal type density per source byte. The deck
overflowed silently (CDX9002 was only a warning), corrupting heap
data. Subsequent `parameterize-walk` read a corrupted `ParamEntry`
record whose `param-name` field contained a non-canonical address,
triggering the GPF.

### Fixes Applied (CL 2595/2596)

1. Apply `survey-headroom` (120%) to CHECK, matching LOWER/RESOLVE/LIFT
2. Increase `survey-check-mul` from 95 to 200
3. Promote CDX9002 deck overflow from warning to **error** (halt
   cleanly instead of corrupting)

### Current Status (CL 2596 merged)

CDX9002 now fires as an error at 76KB plug source:
```
error CDX9002: Deck overflow in CHECK; survey multiplier too low
```

The deck height for CHECK = `source_len × 200 × 120 / 100 + 1MB`
= `76339 × 240 + 1048576 ≈ 18.5MB`. The type-dense plug source
needs more. The `survey-check-mul` of 200 is sufficient for normal
compiler source but insufficient for plug source with its
concentrated type definitions.

### Fix Applied: survey-check-mul = 400 (CL 2598, val)

Bumped `survey-check-mul` from 200 to 400 in BuildSettings.codex.
At 400: 76KB plug source gets `76339 × 480 + 1MB ≈ 36.6MB` for
CHECK. This is sufficient. Plug builds clean at 76KB.

### Remaining Blocker: Foreword Cites Migration (Item 12)

Migrating ExplorerTheme from inlined types to `cites UI chapter`
requires three fixes, two of which are done:

| # | Issue | Status |
|---|-------|--------|
| 1 | CHECK deck overflow | **Fixed** (survey-check-mul=400) |
| 2 | VM output buffer (4MB cap) | **Fixed** (codex-vm.c: 4MB→16MB) |
| 3 | Plug IR parse speed | **BLOCKER** — plug CDX times out on 6.5MB IR |

**Evidence:**

- CDX compilation of foreword-cited source succeeds (213KB CDX, 258 defs)
- IR-CCE compilation produces 6,587,796 bytes (6.5MB) of IR text
- With 16MB VM output buffer, compile.ps1 correctly captures all 6.5MB
- But the HTML plug CDX times out (>300s) parsing 6.5MB of IR S-expressions
- The plug's `parse-ir-chapter` is the bottleneck — it wasn't designed
  for IR streams this large

**Path forward:** Either (A) optimize the IR text parser in the plug,
(B) add dead-code elimination at the IR level so unused foreword
functions are stripped before the plug sees them, or (C) add a
"cited-only" IR mode that only emits IR for definitions the page
actually references.

## Suggested Next Steps

1. **Increase stack space.** The simplest fix: lower
   `bare-metal-heap-base` from 0x600000 to 0x500000 (giving 1MB
   more stack at the cost of losing the serial ring buffer region)
   or raise `bare-metal-ram-size` beyond 2GB if the page tables
   support it. Even 64KB more stack would clear the 76KB plug.

2. **Profile the full call chain.** Use `-Break "find-param-entry"`
   to capture the RSP at entry. Compare with the RSP at
   `parameterize-walk` entry. The delta shows how much stack is
   consumed by the callers above parameterize-walk.

3. **Reduce prologue size.** Every function pushes 5 callee-saved
   registers (40 bytes) plus frame setup. If `find-param-entry`
   and `parameterize-walk` use fewer locals, the prologue could
   be lighter. This requires emitter changes.
