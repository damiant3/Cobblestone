# emit-expr Release-Mode Sanity Check — 2026-04-16

Follow-up to `PERF-EMIT-EXPR-2026-04-16.md`. That report flagged that
5.6M chars of string concat at .NET's ~1 GB/s = ~6ms, nowhere near the
1053ms emit phase. Hypothesis: Debug-mode `Func<>` dispatch and per-call
allocations were inflating the number.

Decision gate: if Release-mode emit drops below ~300ms, the quadratic
isn't worth fixing; stop. If it stays above, the algorithmic cost is
real.

## Method

Same `--bench` workload and protocol as the earlier report, only change
is `-c Release` for the Bootstrap build.

```bash
dotnet build tools/Codex.Bootstrap/Codex.Bootstrap.csproj -c Release
cd tools/Codex.Bootstrap && dotnet run -c Release --no-build -- --bench
```

## Results

| Phase | Debug (ms) | Release (ms) | Δ |
|---|---|---|---|
| lex | 70.3 | 44.2 | −37% |
| parse | 121.8 | 43.2 | −65% |
| desugar | 35.2 | 46.4 | noise |
| resolve | 56.6 | 49.8 | −12% |
| typecheck | 84.2 | 78.7 | −7% |
| lower | 86.2 | 72.8 | −16% |
| **emit** | **1053.7** | **588.2** | **−44%** |
| total | 1511.6 | 933.8 | −38% |

Source size: 590,860 chars (unchanged). 10 measured runs, median
reported, on the same machine.

## Interpretation

**Debug build was inflating emit by ~466ms (44% of its cost)** — curried
`Func<>` dispatch from the emitter's "CPS-like" style is expensive in
Debug but gets aggressively inlined in Release.

**Release emit is still 588ms = 63% of total compile.** The quadratic
IrLet pattern identified in the previous report is still meaningful.

For context, the raw-concat floor is about 6ms (5.6M chars at 1 GB/s).
So **even in Release, emit is doing ~580ms of non-concat work per run**:
per-expression allocations, dictionary lookups for arity/builtins,
pattern-match dispatch, string sanitization, etc. The quadratic
nesting in `emit-let` amplifies all of these per level.

## Decision

**Gate: emit < 300ms Release?** No — 588ms. Go ahead with the IrLet fix.

Ordering after this:

1. **Flatten `emit-let` chains** — biggest single quadratic, 67% of
   output-char share. Rewrite to detect `IrLet` chains at emit and
   produce `{ var x = v1; var y = v2; return body; }` instead of
   nested `((Func<T,R>)((x) => body))(val)`. Expected savings:
   300–400ms of Release emit (most of IrLet's 67% share), putting emit
   at ~200ms and total compile at ~550ms.

2. If that lands cleanly, consider the secondary concat hotspots
   (`emit-binary`, `emit-if`, `emit-apply-args`) — each ~5–10% share.

3. If time later: rebench both Release and Debug; the Debug number
   is what pingpong shows you during day-to-day work, so Debug wins
   matter even if Release is a better predictor of real cost.

## Memory: use Release for perf measurement, Debug for dev loop

Default to Debug for feature work (faster builds, useful stack traces).
Switch to Release for perf bench numbers because the curried-`Func<>`
emitter style masks algorithmic cost under dispatch tax in Debug.
