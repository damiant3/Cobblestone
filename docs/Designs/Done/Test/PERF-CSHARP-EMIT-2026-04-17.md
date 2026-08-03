# C# emit-expr Profile -- 2026-04-17

Refresh of the C# emit profile now that the `emit-let`
flatten fix has landed (commit `e939b91`). The earlier report
(`PERF-EMIT-EXPR-2026-04-16.md`) identified IrLet as 67% of emit-expr by
inclusive time, driving the flatten fix. This report confirms the fix
worked and finds the next target under **exclusive** time, the same
methodology used for x86 emit in `PERF-X86-EMIT-2026-04-16.md`.

## Method

Mirrors `tools/profile-x86-emit.sh`. New script at
`tools/profile-csharp-emit.sh` wraps `emit__csharp_emitter_emit_expr`
with `PerfCounters.Enter/Finish`, recording per-variant exclusive time
via a depth-indexed child-ticks accumulator. Max emit-expr depth on
this workload is 54 (vs. 82 pre-flatten).

## Workload

| Property | Value |
|---|---|
| Source | `Codex.Codex/` self-host, quire-concatenated | 614,435 chars |
| Runner | default `--` mode in Bootstrap, Release build |
| Emit phase time | 1,028 ms (single cold run, first-pass JIT) |
| `--bench` Release median emit time | 598 ms |
| emit-expr total exclusive | **901 ms** (cold run) |

Numbers below are from the single cold run. Relative proportions are
stable across runs; absolute times are inflated by JIT warmup vs.
`--bench`, but the per-variant shares match.

## Results (sorted by exclusive time)

| Variant | calls | sum-bytes | avg-µs excl | sum-ms excl | % of emit-expr |
|---|---|---|---|---|---|
| **IrApply** | **8,869** | 382,437 | **51.8** | **459.6** | **51.0%** |
| **IrName** | **18,905** | 105,145 | **15.9** | **301.3** | **33.4%** |
| IrLet | 602 | 343,407 | 102.7 | 61.8 | 6.9% |
| IrMatch | 233 | 112,506 | 99.4 | 23.1 | 2.6% |
| IrFieldAccess | 2,445 | 25,825 | 9.1 | 22.2 | 2.5% |
| IrRecord | 459 | 54,407 | 32.8 | 15.1 | 1.7% |
| IrLambda | 186 | 31,459 | 25.9 | 4.8 | 0.5% |
| IrBinary | 2,752 | 669,731 | 1.5 | 4.2 | 0.5% |
| IrTextLit | 1,508 | 99,898 | 1.7 | 2.6 | 0.3% |
| IrIntLit | 3,159 | 7,668 | 0.7 | 2.1 | 0.2% |
| IrList | 374 | 48,562 | 5.4 | 2.0 | 0.2% |
| IrIf | 600 | 382,435 | 1.9 | 1.2 | 0.1% |
| IrAct | 5 | 6,335 | 209.4 | 1.0 | 0.1% |
| IrBoolLit / IrNegate / IrCharLit | 482 | 2,075 | -- | 0.4 | 0.0% |
| **Total** | 40,579 | 2.3 M | -- | **901** | 100% |

## Findings

### IrLet flatten fix worked

3,261 calls / 66.8% share pre-fix → 602 calls / 6.9% share post-fix.
Both call count and cost share dropped ~5x. Confirmed: the fix is
effective; IrLet is no longer the hotspot.

### IrApply is the dominant C# emit hotspot (51%)

Same shape as the x86-side report. 8,869 calls × 51.8 µs/call = 459ms
exclusive. Likely drivers (C# side, `CSharpEmitterExpressions.codex:458-474`):

1. **`lookup-arity arities n`** -- linear scan through all chapter defs
   (`lookup-arity-loop` at `CSharpEmitterExpressions.codex:93-102`).
   N ≈ 1,393 defs. Called once per IrApply whose root is IrName.
   `is-builtin-name` on the C# side is already a bsearch (line 229);
   `lookup-arity` is the remaining linear primitive.
2. `collect-apply-chain` -- recursive walk with per-level `[a] ++ acc`
   concatenation. Quadratic in apply chain depth; chains are typically
   short (≤5).
3. Dispatch cascade for constructor / direct / partial / curried paths.

### IrName is the surprise hotspot (33%)

18,905 calls at 15.9 µs/call = 301ms exclusive. Higher than expected
for what should be "emit an identifier".

Looking at `CSharpEmitterExpressions.codex:492`, the IrName path does
~10 explicit `n == "read-line"`-style string-equality checks before
falling through to the arity path, *and* calls `lookup-arity` **twice**
in the fallback expression:

```
else if lookup-arity arities n == 0 then sanitize n ++ "()"
else let ar = lookup-arity arities n in if ar >= 2 then ...
```

Trivial fix: let-bind the arity once. Structural fix: replace the
explicit cascade with a lookup into the sorted `builtin-emitters`
table (same idiom as `is-builtin-name`).

## Fix targets, in priority order

1. **`lookup-arity` linear-scan → bsearch.** Biggest leverage -- touches
   both IrApply (8,869 calls) and IrName (2 × 18,905 = 37,810 calls)
   for a total of ~46K calls × O(1,393) → O(log 1,393). Existing
   precedent: `is-builtin-name` bsearch on `builtin-emitters` (line
   229). Build-once sort in `build-arity-map`.

2. **IrName double `lookup-arity`.** One-line fix: let-bind `ar` once,
   branch on its value. Cuts IrName lookup work in half even before
   the bsearch lands.

3. **IrName builtin-name cascade.** 10-way `n == "name"` chain for
   raw-reference builtins (`read-line`, `get-args`, `heap-save`, etc.).
   Move to a small sorted table with bsearch. Secondary -- only worth
   doing after (1) and (2).

4. **`collect-apply-chain` `[a] ++ acc`.** Quadratic in chain depth.
   Depth is bounded (~5) so small in absolute terms; skip unless
   (1)-(3) land and IrApply is still hot.

## Bare-metal extrapolation

If the C# emit path runs `lookup-arity` 46K times at O(N=1,393) and
`add-subst`-style benchmarks suggest ~2ns per list-ref copy on .NET,
that's ~130ms of the 760ms IrApply+IrName cost attributable to this
one primitive. Bare-metal multiplier is ~33x (per the x86 doc), so
~4s per pingpong stage saved if `lookup-arity` goes to O(log N).

## Reproduction

```bash
wsl bash tools/profile-csharp-emit.sh
```

Script auto-builds (Release), injects the wrapper with
`SkipCodexRegenerate=true`, runs a single compile, and restores the
file on exit.
