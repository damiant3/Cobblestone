# MM3 Gap Analysis: Self-Compile on Bare Metal

**Status**: Analysis — REVISED (hard gap eliminated)
**Date**: 2026-03-26

---

## The Goal

MM3 is the self-hosted compiler compiling **itself** on bare metal. The 26
`.codex` files (~5,000 lines, 493 definitions) go in over serial. Valid C#
comes out over serial. The output matches what the hosted compiler produces.
The ultimate fixed point — on hardware.

## What MM2 Proved

MM2 compiled `main : Integer / main = 42` — a 2-line program using 0 of the
42 builtins the self-hosted compiler needs. MM3 needs all 42, plus enough
memory, I/O bandwidth, and multi-file support.

---

## REVISION: The Hard Gap Doesn't Exist

The original analysis identified higher-order functions (map, fold, filter,
all, any) as the critical blocker requiring function pointers on bare metal.

**This was wrong.** Investigation revealed:

1. The self-hosted compiler does NOT use the `map` builtin. It uses
   `map-list` defined in `Collections.codex` — a pure Codex function
   that loops with `list-at`/`list-snoc` (both already implemented).

2. `map-list` and `fold-list` DO pass function parameters (`f`) and call
   them (`f (list-at xs i)`). This requires indirect calls.

3. **The x86-64 backend already supports indirect calls.** The
   `EmitPartialApplication` system creates closures as `[code-ptr, captures...]`
   and `EmitApply` calls through them via `call rax` with `R11=closure`.

4. Verified experimentally: `map-it double [1,2,3]` compiles to x86-64
   and returns correct results under WSL. Exit code 0. Higher-order
   functions work today.

**Remaining gaps are all easy: multi-file serial protocol, arena sizing,
and boot protocol. No hard design decisions needed.**

---

## ~~Gap 1: Missing Builtins (13 of 42)~~ — REVISED

The self-hosted compiler uses 42 builtins. The x86-64 backend implements 35.
**13 are missing** (some are defined in .codex, some need runtime support):

| Builtin | Uses | Category | Difficulty | Notes |
|---------|------|----------|------------|-------|
| `head` | 17 | List | Easy | Return first element: `list-at xs 0` |
| `tail` | 32 | List | Easy | Return all but first: allocate new list, copy from index 1 |
| `append` | 21 | List | Easy | Alias for `list-append` (already exists as `__list_append`) |
| `map` | 145 | Higher-order | **Hard** | Apply function to each list element. Requires function pointer calls. |
| `map-list` | 56 | Higher-order | **Hard** | Same as `map` (variant name) |
| `filter` | 4 | Higher-order | **Hard** | Keep elements where predicate is true. Requires function pointer calls. |
| `fold` | 25 | Higher-order | **Hard** | Reduce list with accumulator. Requires function pointer calls. |
| `all` | 230 | Higher-order | **Hard** | Check if predicate holds for all elements. Requires function pointer calls. |
| `any` | 11 | Higher-order | **Hard** | Check if predicate holds for any element. Requires function pointer calls. |
| `abs` | 75 | Math | Trivial | `if x < 0 then -x else x` — one comparison + negate |
| `get-env` | 4 | Environment | Easy | Return empty string on bare metal |
| `list-files` | 4 | Environment | Easy | Return empty list on bare metal |
| `run-process` | 2 | I/O | Easy | Return empty string on bare metal (not meaningful on bare metal) |

### The Hard Gap: Higher-Order Functions on Bare Metal

**`map`, `fold`, `filter`, `all`, `any`** are the real blockers. They take a
function argument and call it on each list element. On the C# backend, this is
trivial (lambdas are delegates). On bare metal x86-64, this requires:

1. **Function pointers**: The function argument is a closure or a named function.
   The emitter must produce a callable address.
2. **Closure representation**: If the function is a lambda with captures, the
   closure must carry its captured environment.
3. **Calling convention**: The emitter must `call` through an indirect register
   (function pointer in `rax`, argument in `rdi`).

The self-hosted compiler uses `map` 145 times and `all` 230 times. These are
**load-bearing** — the compiler cannot function without them.

**Possible approaches**:

- **A: Inline at call site.** For known function arguments (named functions,
  simple lambdas), inline the loop + body at each call site. Works for the
  common case (`map emit-expr exprs`). Doesn't work for functions passed
  as values.

- **B: Closure as (code-ptr, env-ptr) pair.** Every function value is a pair:
  pointer to the code, pointer to the captured environment. `map` receives
  this pair, loops over the list, and does `call [code-ptr]` with env-ptr
  and element as arguments. This is the general solution.

- **C: Defunctionalization.** Convert all higher-order usage to a tagged union
  of known function shapes. Avoids function pointers entirely. Works because
  the set of functions passed to `map`/`fold` is finite and known at compile
  time. This is the functional-language-on-bare-metal approach.

**Recommendation**: Start with **A** (inline) for the common cases. If the
compiler's usage patterns require it, escalate to **B** (closure pairs).
**C** is the nuclear option if we need guaranteed no-function-pointer execution.

---

## Gap 2: Multi-File Input

MM2 received one program over serial. The self-hosted compiler reads 26 files.

**Current bare metal `read-file`**: Reads serial input until EOT/null byte.
It ignores the filename argument.

**What's needed**: A protocol for sending multiple files over serial:
```
<filename>\0<length-as-4-bytes><content><filename>\0<length-as-4-bytes><content>...\0\0
```

Or simpler: concatenate all 26 files into one with file boundary markers,
and have the bare metal `read-file` look up the requested filename from a
table built during an initial bulk-receive phase at boot.

**Difficulty**: Medium. The protocol is simple. The bare metal side needs a
string→buffer map (filename lookup table), which is just a linked list of
(name-ptr, content-ptr, length) triples.

---

## Gap 3: Memory

Self-compilation on hosted takes ~300ms and uses a bump allocator. The bare
metal arena starts at a fixed address with limited space.

**Current arena**: Starts at heap base, resets between REPL compilations.
For MM3, we need enough arena to hold:
- All 26 source files in memory (~150 KB of source text)
- All intermediate data structures (tokens, AST, IR, output strings)
- The output C# text (~300 KB)

**Estimate**: 2-4 MB should be sufficient. The bare metal kernel currently
has access to all physical memory above the kernel image. The arena allocator
just needs its limit raised.

**Difficulty**: Easy. Bump the arena size constant.

---

## Gap 4: Output Size

The self-hosted compiler's C# output is ~300K characters. Over serial at
115200 baud (11520 bytes/sec), that's ~26 seconds of transmission time.
Not a blocker, just slow.

**Mitigation**: Increase baud rate, or accept the wait. For verification,
we only need to hash the output and compare — don't need to capture all 300K
over serial. Could emit a SHA-256 hash of the output instead.

**Difficulty**: Trivial (patience) or Easy (hash comparison).

---

## Gap 5: `get-args` and Boot Protocol

The self-hosted compiler reads its input filename from `get-args`. On bare
metal, `get-args` returns an empty list. The compiler needs to know *what*
to compile.

**Solution**: On bare metal, hardcode the compilation target or receive it
as the first serial message. The `main` entry point could be modified to
check for bare metal mode and use serial input instead of file arguments.

**Difficulty**: Easy. A few lines in the compiler's main function.

---

## Implementation Order

| Step | What | Effort | Blockers |
|------|------|--------|----------|
| 1 | `abs` builtin | Trivial | None |
| 2 | `head`, `tail` builtins | Easy | None |
| 3 | `append` → wire to existing `__list_append` | Easy | None |
| 4 | `get-env`, `list-files`, `run-process` stubs | Easy | None |
| 5 | Multi-file serial protocol | Medium | None |
| 6 | Arena size increase | Easy | None |
| 7 | `map` / higher-order functions | **Hard** | Core design decision |
| 8 | `fold`, `filter`, `all`, `any` | Hard | Step 7 pattern |
| 9 | Boot protocol (`get-args` alternative) | Easy | Step 5 |
| 10 | Integration test: compile full self-hosted compiler | — | Steps 1-9 |
| 11 | Fixed-point verification: output matches hosted | — | Step 10 |

**Steps 1-6** are straightforward — maybe a session or two.
**Step 7** is the crux. The approach chosen for `map` determines the
architecture for all higher-order bare metal execution.

---

## The Critical Path

```
MM2 (proven)
  │
  ├── Steps 1-6: Easy builtins + infrastructure (days)
  │
  ├── Step 7: Higher-order functions on bare metal (THE decision)
  │   ├── Option A: Inline known call sites
  │   ├── Option B: Closure pairs (code-ptr, env-ptr)
  │   └── Option C: Defunctionalization
  │
  ├── Steps 8-9: Remaining HOFs + boot protocol (days after Step 7)
  │
  └── Steps 10-11: Integration + fixed point (the summit push)
          MM3
```

**Timeline estimate**: Omitted per project convention. The critical path
is Step 7. Everything else is straightforward.

---

## Connection to Existing Work

- **Linear closures (Step 4)**: The closure representation for `map` connects
  directly to the linear closure analysis. A closure passed to `map` is
  consumed once per element — not linear in the strict sense, but the
  captured environment must live for the duration of the `map` call.

- **Regions (Camp III-A)**: If closures are allocated in a region, `map`'s
  closure lives in the current region and is freed when the region ends.
  This is the zero-allocation higher-order path.

- **Capability refinement**: Higher-order functions on bare metal respect
  the capability system. A closure passed to `map` carries the capabilities
  of its creation site, not the call site.

- **Arena REPL**: The arena reset between compilations means MM3 doesn't need
  a garbage collector. Compile, emit, reset. The arena is the region.
