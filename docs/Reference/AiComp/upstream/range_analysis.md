# Value-Range Analysis and Range Folding

`compiler/range_analysis.py` computes, for every scalar SSA value, a sound
unsigned 32-bit interval `(lo, hi)`: every value the SSA takes at runtime, in
any execution, lies inside it. It is a standalone analysis in the style of
`AliasAnalysis`: constructed by a pass over the current HIR, queried through
a small API (`range_of`, `try_const`, `try_compare_lt`, `try_compare_eq`,
`is_boolean`), never reused across passes.

The one soundness contract everything else builds on:

```text
for every runtime execution, for every SSA value v:
    range_of(v).lo <= value(v) <= range_of(v).hi
```

Only non-full entries are stored; an absent entry means `FULL_RANGE =
(0, 2**32 - 1)`, so "don't know" is always safe.

## Domain and mod-2**32 discipline

The VM reduces every result (const immediates included) mod 2**32. The
analysis therefore reads constants masked, and any transfer function whose
result could wrap widens to FULL instead of modeling the wrap: intervals are
kept non-wrapping (`lo <= hi`), which keeps every comparison and hull
trivially correct. Examples: `+` widens when `hi1 + hi2 > MASK`; `-` widens
unless `lo1 >= hi2`; `&` is bounded by `min(hi1, hi2)`; `%` by a positive
constant divisor is bounded by the divisor.

## Structured control flow

The HIR is structured (ForLoop/If with SSA-carried values), so the analysis
is structural induction rather than a CFG worklist.

### If: joins and branch refinement

Both branches are walked under an "overlay" of refinements derived from the
condition: inside the then-branch of `x < C`, `x` is narrowed to
`[lo, C-1]`; the else-branch narrows the other direction; `==` intersects
(then) and trims point endpoints (else). A refinement that would produce an
empty interval (a statically dead branch) is skipped rather than modeled, so
stored intervals are always non-empty. The If results join the two sides'
yields, or take one side when the condition itself is provably constant.

Values defined inside a branch keep their refined ranges globally. This is
sound because SSA dominance confines their uses to that branch: the range is
only ever consulted at program points where the path condition held.

### select arm refinement

`select(cond, a, b)` evaluates `a` under the then-refinement of `cond` and
`b` under the else-refinement. This is load-bearing, not an optimization:
the canonical wrap recurrence `x' = select(2x + d < n, 2x + d, 0)` only
stabilizes at `[0, n-1]` because the true arm is evaluated as
`range(2x + d) intersect [0, n-1]`. Without it, the first widening past `n`
destroys the bound and the recurrence diverges to FULL.

### ForLoop: fixpoint, widening, narrowing

The lowered loop is top-tested (`while counter < end`), so:

- `may_run = lo(start) < hi(end)`; if false, the loop provably never runs
  and results carry the iter_args through unchanged.
- `must_run = hi(start) < lo(end)`; if false, results are hulled with the
  iter_args (the zero-trip case).
- The counter ranges over `[lo(start), hi(end) - 1]`.

Loop-carried values (`iter_args -> body_params -> yields`) are iterated to a
fixpoint. The interval lattice is ~2**33 tall, so naive iteration cannot
terminate: after `_EXACT_ITERATIONS = 2` exact join rounds, unstable upper
bounds are widened along the threshold ladder `1, 3, 7, ..., 2**32 - 1`
(lower bounds drop to 0). The ladder is chosen because the bounds that
matter on this VM are mask- and wrap-shaped (`2**k - 1`), so widening
usually lands exactly on the true bound. Two exact rounds first, because the
wrap recurrence above converges in two.

A descending phase (`_NARROW_ITERATIONS = 2`) then re-evaluates the body and
accepts `hull(init, yields)` whenever it is contained in the current params,
recovering precision a widening overshoot discarded (e.g. `[0, 7]` back down
to `[0, 6]` for a wrap bounded by 7). Each narrowing step is re-verified by
a fresh body walk, so the final params are always a checked post-fixpoint
and the final walk leaves every loop-internal range consistent with them.

### Warm start (and why it is not a cache)

An enclosing loop's fixpoint re-solves each inner loop once per outer round,
which is multiplicative in nesting depth. The mitigation is a warm start:
each solve seeds its params from the loop's previous fixpoint
(`hull`ed with the current inits). The first ascending walk then acts as a
VERIFICATION: if the seed is still a post-fixpoint under the current
environment, the solve costs one walk; otherwise the ascent continues from
where the check failed.

This replaced an earlier signature-keyed memo `(bounds, inits, overlay) ->
params` that was unsound: an inner body can capture a CHANGING outer SSA
directly (not through bounds or iter_args), so the signature missed it and a
stale narrow fixpoint leaked into folds, unrolling, and alias proofs
(reproduced end-to-end before the fix). The warm start avoids the entire
class of bugs by construction: nothing is assumed cacheable, and the
post-fixpoint property is re-proven inside every solve.

### Budget

`_ANALYSIS_BUDGET` (2M statement visits) bounds pathological nesting. On
exhaustion the analysis degrades to all-FULL (sound) and sets
`budget_exhausted` instead of stalling compilation.

## Consumers

- **Simplify `range_fold`**: any op whose result interval is a single point
  is replaced by the Const (loads excluded). This runs BEFORE the
  select-specific handling so `select(c, 7, 7)` folds too, and it fires
  inside retained loops (partial-unroll or dynamic-bound programs). On
  tree_hash this is what statically discharges all wrap checks.
- **Simplify `_is_boolean`**: falls back to `range.is_boolean` when the
  syntactic tracker (`<`, `==`, `& 1`) cannot see a boolean (If results,
  loop-carried bits), enabling select-to-mul and ALU-mux rewrites.
- **AliasAnalysis shared-component proof**: every non-composite address base
  is anchored to one SSA with `value(key) = value(anchor) + offset`. For two
  keys sharing one identical component X (`p+i` vs `p+j`, or `p+c` vs
  `p+i`), the shared `value(X)` rotates both footprints by the same amount
  mod 2**32, so it cancels: the accesses are disjoint iff the circular arcs
  `[dyn_lo + offset, dyn_hi + offset + width)` are. This proves NO_ALIAS for
  bounded dynamic indices WITHOUT the restrict_ptr contract. The analysis is
  built lazily on the first query that can use it, so passes whose queries
  never reach the shared-root case pay nothing.
- **SROA out-of-region proof**: `PointerProvenance` reports
  dynamic base-relative offsets with an `offset_range` for the shapes
  `static + unrelated` and `static - unrelated` (only when the interval
  provably does not wrap). A dynamic access whose whole footprint
  (`offset_range` plus access width) lies past the region extent is an
  ordinary access to other memory and no longer rejects the promotion.
- **LoopUnroll computed bounds**: bounds that are range-proven constants
  (e.g. `end = 2 * 3` before any folding ran) unroll like literal Consts.
  The analysis is built lazily on the first non-Const bound.

## Testing strategy

Four layers, in `compiler/tests/test_range_analysis.py` and the consumer
test files:

1. Unit tests per transfer function, refinement, and loop shape (including
   the wrap recurrence converging without widening, zero-trip/must-run
   results, and budget exhaustion).
2. Ground-truth soundness fuzzing: a seeded generator builds random
   programs (nested loops forced to carry iter_args, dynamic masked bounds,
   ifs); a reference HIR interpreter records every runtime value of every
   SSA; the test asserts containment in the computed intervals. This is a
   direct check of the soundness contract, independent of any consumer.
3. Consumption tests: transforms that are only provable via the structured
   analysis (folds inside retained loops, loop-result booleans driving
   select-to-mul) assert the consumer actually fired.
4. Full-pipeline differentials: programs whose loops survive to the VM
   (dynamic bounds / pragma-pinned) are compiled and executed, and outputs
   compared against the reference interpreter -- including the
   nested-capture shape that caught the warm-start predecessor bug.

## Costs and known limitations

- One build over the fully unrolled tree_hash body (~147k statements) costs
  ~0.1s. The two `simplify` instances share one config, so the first builds
  an analysis that folds little on tree_hash (~1% of compile time); fixing
  that properly needs occurrence-specific pass configs or a shared analysis
  manager, which is deliberately deferred.
- Ranges are per-SSA and context-joined: a value used at several program
  points gets one interval covering all of them (except branch-internal
  values, which inherit their path refinement by dominance).
- The domain is non-relational: it cannot express `i != j` or correlate a
  carried value with the loop counter (such bounds go FULL or stay at the
  widened threshold).
