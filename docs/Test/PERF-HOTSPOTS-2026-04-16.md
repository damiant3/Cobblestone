# Perf Hotspot Re-Evaluation — 2026-04-16

Agent: Hex-Cam. Purpose: verify which `BACKLOG.md` quadratic-hotspot entries
are still real on the *current* self-host workload, vs. stale claims from
before recent compiler growth.

> **P9 landed later the same day in `2293b2e`**: dense `var-id`-indexed
> substitution list replacing the sorted `List<SubstEntry>`. Typecheck
> 1418.91ms → 85.00ms (16.7x), total compile 2.03x. Pingpong green. The
> findings below are the pre-fix snapshot that motivated the commit.

## Method

Instrumented six hot functions in the bootstrap's generated `CodexLib.g.cs`
with per-call counters (`env_bind`, `add_subst`, `set_insert`,
`build_type_def_map`, `resolve`, `replace_def`). Counters tracked: call count,
accumulated N at entry, max N, and (for `resolve`) hop depth. Counters printed
after a single clean run inside the `--bench` harness. Instrumentation
reverted before committing.

## Workload

| Property | Value |
|---|---|
| Source | `Codex.Codex/` self-host (all `.codex` files, quire-concatenated) |
| Size | 589,518 chars |
| Compiler | Reference (C#) running self-host-emitted C# via `Codex_Codex_Codex.*` |
| Protocol | `--bench`: 3 warmup + 1 counter run + 10 measured, median reported |

## Phase timings (median, 10 runs)

| Phase | ms | % of total |
|---|---|---|
| lex | 64.38 | 2.2% |
| parse | 102.91 | 3.5% |
| desugar | 57.11 | 2.0% |
| resolve | 41.72 | 1.4% |
| **typecheck** | **1418.91** | **48.6%** |
| lower | 100.31 | 3.4% |
| **emit** | **1136.20** | **38.9%** |
| total | **2919.99** | 100% |

Typecheck + emit = **87.5%** of total time.

## Hotspot call counts (single clean run, typecheck pass)

| Backlog # | Function | Calls | Avg N | Max N | Total work | Est. ms | Verdict |
|---|---|---|---|---|---|---|---|
| **P9 (real)** | `add-subst` | 20,394 | 10,196 | **20,393** | **207,947,421** | **~400ms** | **biggest hotspot** |
| P2 | `env-bind` | 10,493 | 1,641 | 1,877 | 17,219,440 | ~35ms | real, small (~2% of typecheck) |
| P7 | `build-tdm` | 142 | 70 | 141 | 10,011 | ~0ms | negligible — drop |
| P3 | `set-insert` | **0** | — | — | 0 | 0 | not a typecheck hotspot — drop |
| P9 (as written) | `resolve` chain walk | 409,337 | **0** avg-hops | **2** max-depth | 23,675 hops | 0 | **misdiagnosed** — chains flat |
| P13-tail | `replace-def` | **0** | — | — | 0 | 0 | Codex-emit only, not on bench — drop from typecheck list |

"Total work" = Σ N at entry. Each unit = one list-reference copy or shift
(~2ns on .NET `List<T>` over ref-typed entries). "Est. ms" = total work × 2ns.

## Findings

### P9 is misdiagnosed

Backlog claim: `resolve` needs path compression. Reality: max chain depth is
**2**, average is **0 hops**. Path compression would save nothing measurable.

Real cost is the insert side: **`add-subst` rebuilds the whole sorted
substitution list on every call**, and the list grows to 20K entries. 208M
copy operations = ~400ms = **28% of typecheck = ~14% of total compile**.

Shape of fix: `var_id` is sequentially allocated by `next_id`, so the
substitution table can be a dense `List<SubstEntry>` indexed by `var_id`
(O(1) insert, O(1) lookup). No sort, no bsearch, no copy. Estimated win:
~400ms of typecheck, bringing typecheck closer to ~1000ms on this workload.

### P2 is real but small

17M ops ≈ 35ms = ~2% of typecheck. The 9.2x speedup from `85cfa4d`
(sorted binary search + `list-snoc`) is still mostly holding — the quadratic
is there but the constant (tight ref-memcpy) is modest. Worth eventually,
not urgent.

Also: the backlog cites HAMT-partial commits `88e056a` / `1a90eeb` as
"partial mitigation", but those were **reverted** in `f85d031` ("P2 HAMT —
regression, not improvement, 2.3s vs 3.2s"). HAMT lost; sorted-list won.
The wording should be corrected.

### P3 and P7 are not hotspots on this workload

- `set-insert`: **0 calls during typecheck**. Used only in name resolution
  (41ms phase total) and not material there either.
- `build-type-def-map`: 142 calls, total work 10K ops. Not worth optimizing.

Both should come off the perf list. Leaving them invites someone to spend
a day optimizing for a no-op gain.

### P13-tail is emit-path-only, not typecheck-path

`replace-def` had **0 calls** during the C# emit stage of this bench. The
quadratic only fires in the Codex-to-Codex emitter, which isn't on the
pingpong hot path. Keep the entry but label it clearly as Codex-emit-only.

### Emit is 1136ms (40% of total) but not in the hotspot list

Not currently in `BACKLOG.md` as a quadratic. Obvious next profiling target
once `add-subst` is addressed.

## Recommended backlog rewrite

1. **Rename P9** from `resolve` to `add-subst`. Rewrite as "sorted-list
   substitution table → dense var-id-indexed array". Mark as highest perf
   priority.
2. **Keep P2** but demote phrasing to "moderate, ~2% of typecheck". Drop the
   stale HAMT reference.
3. **Drop P3 and P7.**
4. **Keep P13-tail** with a Codex-emit-only label.
5. **Add** an emit-phase profiling TODO.

## Reproduction

```bash
dotnet build tools/Codex.Bootstrap/Codex.Bootstrap.csproj -c Debug -p:SkipCodexRegenerate=true
cd tools/Codex.Bootstrap && dotnet run -c Debug --no-build -- --bench
```

Instrumentation was reverted; to reproduce the counters, re-apply:

- Static counter class (call counts + sum N + max N per function).
- Single-line `PerfCounters.Record*()` call at the top of each hot function's
  C# body in `CodexLib.g.cs`.
- `PerfCounters.Reset()` and `PerfCounters.Report()` wired into `RunBench`
  between warmup and measured runs.
