# Inline Optimization

## Motivation

Every function call pays the full prologue/epilogue cost: push
callee-saved registers, allocate stack frame, `cmp rsp, r10` collision
check, pop, ret. For a one-line data constructor like
`load-step-ok (bytes) = LoadStep { step-ok = True, step-bytes = bytes }`
this is ~50-100 cycles of overhead for ~5 cycles of useful work.

The TechEmpower-style benchmark measures ~25K req/s on the HTTP handler
path. Each request makes ~30 function calls. At ~75 cycles overhead per
call, that is ~2,250 cycles/request of pure call tax on a pipeline that
takes ~88,000 cycles total (~2.5% of wall time). More importantly,
inlining enables further optimizations: a record constructor followed
by a field access can be folded away, dead branches can be eliminated,
and the register allocator sees the full expression tree instead of
opaque call boundaries.

## Design

Three-phase automatic inlining, implemented as an IR-to-IR pass after
LOWER (or after RESOLVE/LIFT for CDX mode). Each phase feeds the next.
Controlled by compile flags on the mode line.

### Phase 1: Leaf Inlining (`inline=leaf`)

Inline every function whose body contains no `IrApply` nodes (no
function calls). No size limit. Leaf functions are pure computation:
record constructors, field access, arithmetic, literals, pattern
matches over arguments. The prologue/epilogue always costs more than
the body.

Detection: walk `IRDef.body` recursively. If no `IrApply` is found,
the function is a leaf.

Substitution: at each `IrApply` call site where the callee is a
known leaf def, replace the apply node with the def's body, alpha-
renaming parameters to fresh names bound via `IrLet` to the argument
expressions.

```
-- Before:
load-step-ok (bytes) = LoadStep { step-ok = True, step-bytes = bytes }
...
let s = load-step-ok 42

-- After:
let s = let __inline_bytes_0 = 42
        in LoadStep { step-ok = True, step-bytes = __inline_bytes_0 }
```

After leaf inlining, re-scan the def list: functions that previously
called only leaves are now themselves leaves. Iterate until no new
leaves are found (fixed point).

Candidates in the HTTP hot path: `load-step-ok`, `load-step-fail`,
`load-result-add`, `load-result-error`, `http-plain`, `http-html`,
`http-not-found`, `http-error`, `te-plaintext-response`,
`te-json-response`, `router-empty`, `router-count`.

### Phase 2: Single-Caller Inlining (`inline=single`)

Implies Phase 1. After leaf inlining, build a call graph: for each
remaining def, count how many other defs reference it via `IrApply`.
Inline every function with exactly one caller, regardless of body
size.

This is free: the call site gets the body, and the original def is
dead (removed from the def list). No code size increase.

Single-caller inlining often creates new leaves (a function that
called one helper is now a leaf after the helper was inlined) and
new single-caller functions (a function with two callers, one of
which was the just-inlined function, now has one caller). Iterate
phases 1+2 until fixed point.

Candidates: `te-dispatch`, `te-handle`, `dispatch-local`,
`handle-request` (in the benchmark, each is called from exactly
one handler function).

### Phase 3: Cost-Based Inlining (`inline=cost:<N>`)

Implies Phases 1 and 2. After the fixed-point iteration, inline
remaining multi-caller functions whose body is below a cost
threshold of N IR nodes. The cost hint N controls the tradeoff
between code size and call elimination.

Cost model: count IR nodes in the def body recursively. An `IrLet`
counts as 1 + cost(value) + cost(body). An `IrApply` counts as 1 +
cost(fn) + cost(arg). Literals and names count as 1. This gives a
rough proxy for the emitted instruction count.

Each inlined call site duplicates the body, so the code size
increase per inlining is approximately `(callers - 1) * body-size`.
The pass should track cumulative code growth and stop when a budget
is reached (e.g., 50% increase over pre-inline size, or absolute
bytes remaining in `code-buffer-size`).

Suggested default threshold: `cost:20` (inline functions with fewer
than 20 IR nodes). This catches small helpers, predicates, and
single-branch dispatchers without inlining large recursive
functions.

## Compile Flag

Add an `inline` field to `CompileFlags`:

```
CompileFlags = record {
  prose : Boolean,
  escape-check : Boolean,
  survey : SurveyConfig,
  inline : InlineMode
}

InlineMode =
  | InlineNone
  | InlineLeaf
  | InlineSingle
  | InlineCost (Integer)
```

Mode line syntax:

| Flag | Behavior |
|------|----------|
| (default) | No inlining (`InlineNone`) |
| `inline=leaf` | Phase 1 only |
| `inline=single` | Phases 1 + 2 |
| `inline=cost:20` | Phases 1 + 2 + 3 with threshold 20 |

Each level implies all prior levels. The cost threshold is the
only tunable parameter.

Parse in `parse-inline-mode` alongside the existing `parse-survey-
config`. Thread `InlineMode` on `CompileFlags` to the pipeline
entry point, where the inline pass runs between LOWER and
RESOLVE (CDX) or between LOWER and EMIT (TEXT).

## Pipeline Placement

```
LOWER -> [INLINE] -> RESOLVE -> LIFT -> EMIT    (CDX)
LOWER -> [INLINE] -> EMIT                       (TEXT)
```

The inline pass receives `List IRDef` and returns `List IRDef`
(with inlined bodies and dead defs removed). It does not interact
with the type checker, scope resolver, or emitter. It needs no
new phase allocator deck — it operates on the existing LOWER deck
and allocates inlined IR nodes on bivy (reclaimed at phase end).

### Fixed-Point Iteration

```
repeat:
  leaf-inline all leaf defs (no IrApply in body)
  if mode >= InlineSingle:
    single-caller-inline all defs with caller-count == 1
  remove dead defs (caller-count == 0, not "opening")
until no changes
if mode is InlineCost(N):
  cost-inline defs with body-cost < N and code-budget permits
  remove newly dead defs
```

## Implementation Outline

New file: `codex/compiler/IR/Inline.codex`

Functions:

| Function | Purpose |
|----------|---------|
| `is-leaf : IRExpr -> Boolean` | Walk body, return False on any `IrApply` |
| `count-callers : List IRDef -> List CallerEntry` | Build call graph: def name -> caller count |
| `ir-cost : IRExpr -> Integer` | Count IR nodes recursively |
| `inline-at-call-sites : List IRDef, Text, IRDef -> List IRDef` | Substitute body at all `IrApply` sites matching the def name |
| `alpha-rename : IRExpr, List IRParam, List IRExpr -> IRExpr` | Wrap body in `IrLet` bindings for each param |
| `remove-dead-defs : List IRDef, List CallerEntry -> List IRDef` | Drop defs with 0 callers (except `opening`) |
| `inline-pass : List IRDef, InlineMode -> List IRDef` | Top-level driver: iterate phases to fixed point |

## Correctness Constraints

- **Never inline recursive functions.** A function that appears in
  its own call graph (directly or transitively) must not be inlined.
  The leaf check naturally excludes direct recursion (a recursive
  function calls itself, so it contains `IrApply`). Mutual recursion
  requires checking the call graph for cycles.

- **Never inline `opening`.** The entry point must remain a named
  def for the emitter to find.

- **Alpha-rename to avoid capture.** When substituting a body at a
  call site, parameter names must be freshened to avoid shadowing
  names in the caller's scope. Use a counter suffix:
  `__inline_<param>_<counter>`.

- **Preserve SourceSpan.** Inlined IR nodes retain their original
  spans for diagnostic reporting. The call site's span is not
  propagated inward.

- **Effect-safe.** Inlining a pure function into an `act` block is
  safe. Inlining an effectful function (body contains `IrAct`) into
  a non-effectful context would be a type error, but this cannot
  happen: the type checker already verified the call, and inlining
  preserves the body's structure.

## Memory and Time Assessment

The inline pass walks the def list (O(D) defs) and for each
candidate walks the body of every other def to find call sites
(O(D * N) where N is average body size). The fixed-point iteration
runs at most D times (each round inlines at least one def or
terminates). Worst case O(D^2 * N), but in practice the iteration
converges in 2-3 rounds.

Allocation: inlined IR nodes are freshly constructed on the current
allocator (bivy or deck depending on placement). Each inlined body
is a copy with renamed parameters. For a compiler with ~2,800 defs
and an average body size of ~50 IR nodes, the total allocation is
modest (~10-20 MB for the inline pass).

## Expected Impact

From the TechEmpower benchmark (200K requests, ~25K req/s):

- ~30 function calls per request in the handler path
- Leaf inlining eliminates ~10-12 (record constructors, accessors)
- Single-caller inlining eliminates ~5-8 (dispatch helpers)
- Cost-based inlining eliminates ~3-5 (small predicates)
- Estimated remaining: ~5-8 calls (recursive loops, large functions)
- Expected speedup: 1.5-2x on handler-path throughput

The inline pass does not affect the compiler's own compilation
(the selfhost). It is an optimization applied to user programs.
The seed is unaffected unless the compiler's own code is compiled
with `inline=` flags, which would require a seed rebuild.

## Open Questions

1. Should `inline=leaf` be the default for CDX mode? Leaf inlining
   is always profitable and has no code size cost for single-caller
   leaves.

2. Should the cost threshold be per-function or global? A per-
   function annotation (`@inline` / `@noinline`) would give authors
   control, but annotations are not yet load-bearing in the
   compiler.

3. Should inlining interact with TCO? If a tail-recursive function
   has its helper inlined, the tail call may now be to the outer
   function, not the helper. The TCO detector should re-run after
   inlining.
