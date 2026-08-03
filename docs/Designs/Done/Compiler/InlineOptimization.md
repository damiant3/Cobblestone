# Inline Optimization

> **Filed to Done 2026-07-15 (val):** Phase 1 (leaf inlining) shipped and always on; Phases 2 (single-caller) and 3 (cost-based) are tracked in BACKLOG 2.9. Moved out of Active to keep init light; reopen if a later phase is picked up.

**Status: Phase 1 SHIPPED and always on. Phases 2 and 3 open.**

Leaf inlining is in the compiler today, unconditionally, with no flag:

- `inline-leaf-calls-in-chapter` and the `InlineLeaf` record live in
  `codex/compiler/IR/Lowering.codex` (~1256–1590).
- It is called from `codex/compiler/opening.codex` (~582), in
  `compile-frontend-cdx`, immediately after `fold-constants-in-chapter`
  and inside the LOWER phase: `deck-record (inline-leaf-calls-in-chapter
  (fold-constants-in-chapter ir-raw))`.
- `demand-inline-floor` is reserved in
  `codex/compiler/Core/BuildSettings.codex` for the day the pass earns
  its own deck; today it runs under `demand-lower-floor`.

There is **no `inline=` mode flag**, no `CompileFlags.inline` field, and
no `codex/compiler/IR/Inline.codex`. Earlier drafts of this document
prescribed all three; none of them were built, and none are needed --
leaf inlining is always profitable, so it is simply always on. The
sections below describe what shipped (Phase 1) and what remains
(Phases 2 and 3).

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

Three-phase automatic inlining, implemented as an IR-to-IR pass inside
LOWER. Each phase feeds the next. Phase 1 is built; Phases 2 and 3 are
the remaining work.

### Phase 1: Leaf Inlining -- SHIPPED

As built, a leaf candidate is deliberately narrower than the original
"any body with no `IrApply`" sketch. `collect-leaf-defs` admits a def
only when all of the following hold:

- 1 to 3 parameters.
- The body is pure integer arithmetic over the parameters and integer
  literals: `IrIntLit`, `IrName` naming a parameter, and `IrBinary`
  with `IrAddInt`/`IrSubInt`/`IrMulInt`/`IrDivInt`. Anything else
  disqualifies the def (`leaf-body-ok`).
- Body size within a fuel budget of 12 nodes.
- The def is not `deck-record`.
- The def has no bounded parameter or return (`has-bounded-boundary`).
  A bounded signature is enforced by runtime guards at the callee's
  entry and epilogue (BoundedSignatures stage B); splicing the body
  into the caller would bypass those guards, so bounded-boundary
  functions are never candidates.

Substitution (`inline-expr` / `inline-apply-site`) replaces a matching
`IrApply` with the def's body, substituting the argument expressions
for the parameter names, with the caller's bound names threaded through
so a parameter shadowed at the call site is not captured. Defs are
scanned read-only first (`has-leaf-call`) and rebuilt only when they
actually contain a qualifying call, so the LOWER deck pays copies only
for the defs that change.

The pass runs on the **CDX path only**, after constant folding. The
TEXT path is untouched, so text round-trip and semantic equivalence
never see inlined bodies.

Widening the candidate rule (record constructors, field access, pattern
matches over arguments -- the cases the original sketch assumed) is
future work, and each widening must re-clear the fixed-point gates.

### Phase 2: Single-Caller Inlining -- OPEN

After leaf inlining, build a call graph: for each remaining def, count
how many other defs reference it via `IrApply`. Inline every function
with exactly one caller, regardless of body size.

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

### Phase 3: Cost-Based Inlining -- OPEN

Implies Phases 1 and 2. After the fixed-point iteration, inline
remaining multi-caller functions whose body is below a cost
threshold of N IR nodes. The threshold controls the tradeoff
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

Suggested default threshold: 20 IR nodes. This catches small helpers,
predicates, and single-branch dispatchers without inlining large
recursive functions.

## No flag

Inlining is not mode-selectable and should not become so. Phase 1 is
always profitable -- the prologue/epilogue always costs more than an
integer expression of at most 12 nodes -- so it runs on every CDX
compile. Phases 2 and 3 should land the same way: always on, or not at
all. A flag would mean the seed and the battery disagree about what the
compiler does, which is the one thing this project cannot afford.

## Pipeline Placement

```
LOWER: lower-chapter -> fold-constants -> INLINE -> RESOLVE -> LIFT -> EMIT    (CDX)
LOWER: lower-chapter -> EMIT                                                   (TEXT)
```

The pass takes an `IRChapter` and returns an `IRChapter`. It does not
interact with the type checker, scope resolver, or emitter. It has no
deck of its own -- it runs inside LOWER, under `demand-lower-floor`, and
`deck-record`s the rebuilt defs there. (`demand-inline-floor` is
declared in `BuildSettings.codex` and is currently unused; it exists
for the day the pass is promoted to its own phase.)

### Fixed-Point Iteration (Phases 2 and 3)

Phase 1 as shipped is a single pass -- its candidate rule is closed
under nothing, so there is no fixed point to reach. Phases 2 and 3
introduce one:

```
repeat:
  leaf-inline all leaf defs
  single-caller-inline all defs with caller-count == 1
  remove dead defs (caller-count == 0, not "opening")
until no changes
if cost-based:
  cost-inline defs with body-cost < N and code-budget permits
  remove newly dead defs
```

## Implementation Outline (Phases 2 and 3)

Phases 2 and 3 extend `codex/compiler/IR/Lowering.codex` alongside the
shipped Phase 1 machinery. They need:

| Function | Purpose |
|----------|---------|
| `count-callers : List IRDef -> List CallerEntry` | Build call graph: def name -> caller count |
| `ir-cost : IRExpr -> Integer` | Count IR nodes recursively |
| `remove-dead-defs : List IRDef, List CallerEntry -> List IRDef` | Drop defs with 0 callers (except `opening`) |

Substitution and the bound-name threading already exist
(`inline-expr`, `inline-apply-site`, `has-leaf-call`) and generalize --
what is missing is the call graph, the cost model, and dead-def
removal.

## Correctness Constraints

- **Never inline recursive functions.** A function that appears in
  its own call graph (directly or transitively) must not be inlined.
  The leaf check naturally excludes direct recursion (a recursive
  function calls itself, so it contains `IrApply`). Mutual recursion
  requires checking the call graph for cycles.

- **Never inline `opening`.** The entry point must remain a named
  def for the emitter to find.

- **Never inline across a bounded boundary.** A def with a bounded
  parameter or return is enforced by runtime guards at its entry and
  epilogue. Splicing its body into the caller bypasses them.
  `has-bounded-boundary` rejects these; Phases 2 and 3 must keep doing
  so.

- **Avoid capture.** When substituting a body at a call site, a
  parameter name that is also bound in the caller's scope must not be
  captured. Phase 1 threads the caller's bound names through
  `inline-expr` and declines the substitution rather than renaming;
  Phases 2 and 3 may need real freshening (`__inline_<param>_<counter>`)
  once bodies can contain `IrLet`.

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

Because the pass is always on and runs on the CDX path, it *does*
affect the compiler's own compilation. Any change to the candidate rule
or the substitution is a codegen change and must clear all the gates:
text round-trip, pingpong, and the hard fixed point.

## Open Questions

1. **Widen the Phase 1 candidate rule?** The shipped rule is integer
   arithmetic only. Record constructors and field accesses -- the
   original motivating cases -- are still called out of line. Each
   widening is a separate, separately-gated change.

2. Should the cost threshold be per-function or global? A per-
   function annotation (`@inline` / `@noinline`) would give authors
   control, but annotations are not yet load-bearing in the
   compiler.

3. Should inlining interact with TCO? If a tail-recursive function
   has its helper inlined, the tail call may now be to the outer
   function, not the helper. The TCO detector should re-run after
   inlining. Phase 1 cannot trigger this (its candidates contain no
   calls at all); Phase 2 can.
