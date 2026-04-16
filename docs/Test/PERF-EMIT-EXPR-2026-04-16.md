# emit-expr Profile — 2026-04-16

Agent: Hex-Cam. Goal: find the next C# emit hotspot after the P9 + P13 +
csharp-emit-defs-list wins. Emit is still 71% of compile time (1035ms of
1496ms); this report tells us where inside emit-expr.

## Method

Wrapped `emit__csharp_emitter_emit_expr` in `tools/Codex.Bootstrap/CodexLib.g.cs`
with instrumentation that counts each call, records the returned string's
length, and tracks nesting depth. Per-variant totals keyed off the IR node
type (`e.GetType().Name`). One clean run, counters printed after bench
warmup. Instrumentation reverted.

## Workload

| Property | Value |
|---|---|
| Source | `Codex.Codex/` self-host (39 files, 1393 defs) |
| Size | 590,704 chars |
| Compiler | `Codex_Codex_Codex.*` (self-host C# emit) via `--bench` |
| Runner | Debug build (same as earlier perf reports) |

## Phase timings (after landed P9 + P13 + csharp-emit-defs-list)

| Phase | ms |
|---|---|
| lex | 70.3 |
| parse | 121.8 |
| desugar | 35.2 |
| resolve | 56.6 |
| typecheck | 84.2 |
| lower | 86.2 |
| **emit** | **1053.7** |
| total | 1511.6 |

Emit is **70%** of compile.

## emit-expr totals (single run)

| Metric | Value |
|---|---|
| Total calls | 42,985 |
| Total out chars (Σ result.Length) | 5,620,938 |
| Single-call max out chars | 18,336 |
| Max nest depth | 82 |

Note: `sum-out-chars` ≫ final emit size (~948K). The extra 6× is because
every nested call's return value is already concatenated into its parent's
return. So `sum-out-chars` measures **total concat work**, not output size.

## Per-variant breakdown (sorted by total output chars)

| Variant | calls | sum-out | max-out | avg-out | % sum |
|---|---|---|---|---|---|
| **IrLet** | **3,261** | **3,755,493** | 11,745 | 1,151 | **66.8%** |
| IrBinary | 2,688 | 616,035 | 13,463 | 229 | 11.0% |
| IrIf | 633 | 410,105 | 7,460 | 647 | 7.3% |
| IrApply | 8,735 | 360,658 | 18,336 | 41 | 6.4% |
| IrMatch | 229 | 110,584 | 8,090 | 482 | 2.0% |
| IrName | 18,665 | 103,238 | 53 | 5 | 1.8% |
| IrTextLit | 1,446 | 94,296 | 842 | 65 | 1.7% |
| IrRecord | 467 | 56,091 | 1,283 | 120 | 1.0% |
| IrList | 375 | 45,945 | 18,321 | 122 | 0.8% |
| IrLambda | 186 | 30,976 | 1,175 | 166 | 0.6% |
| IrFieldAccess | 2,437 | 25,604 | 33 | 10 | 0.5% |
| IrIntLit | 3,331 | 8,179 | 11 | 2 | 0.1% |
| IrBoolLit | 469 | 2,079 | 5 | 4 | 0.0% |
| IrAct | 4 | 1,483 | 520 | 370 | 0.0% |
| IrNegate | 18 | 94 | 7 | 5 | 0.0% |
| IrCharLit | 41 | 78 | 2 | 1 | 0.0% |

## Findings

### IrLet is the overwhelming hotspot

**66.8% of total emit concat work.** 3,261 let-expressions accumulate
3.76M chars of output, with an average of 1,151 chars per call. Max 11,745
chars for a single let call.

The cause is the self-host's `emit-let` shape:

```codex
emit-let (name) (ty) (val) (body) (arities) =
 "((Func<" ++ cs-type-or-dynamic ty ++ ", " ++ cs-type (ir-expr-type body) ++ ">)(("
   ++ sanitize name ++ ") => " ++ emit-expr body arities ++ "))("
   ++ emit-expr val arities ++ ")"
```

When `body` is itself another let (which is the **norm** in the self-host —
the whole compiler is written in long `let ... in let ... in ...` chains),
each level wraps the already-large inner result with ~60 chars of `Func<>`
boilerplate and re-concats. For a chain of N nested lets, total concat
cost is O(N² · wrapper-size).

The 82-level max nest depth confirms this: expressions go 82 layers deep
of emit-expr dispatch, each layer wrapping the layer below.

### IrBinary, IrIf, IrApply all follow behind

Three secondary hotspots, roughly 6–11% each. Each also uses recursive
`++` concat in its emit helper:

- `emit-binary`: `emit-expr l ++ " " ++ op ++ " " ++ emit-expr r`
- `emit-if`: `"(" ++ emit-expr c ++ " ? " ++ emit-expr t ++ " : " ++ emit-expr e ++ ")"`
- `emit-apply`: args concatenated via `emit-apply-args` (recursive `++`)

The IrIf nesting is often deep — `if-else` chains translated from
`when ... is X -> … is Y -> …` patterns produce towers of ternary.

### Leaf variants are fine

IrName at 18,665 calls is the most-called variant (every identifier is one),
but avg-out = 5 chars, sum-out = 103K. The dispatch overhead dominates the
string work there. Not a fix target.

## Candidate fixes (ordered by expected ROI)

### 1. Flatten let chains in `emit-let` (biggest win)

The quadratic wrapper pattern — `((Func<T,R>)((name) => body))(val)` with
`body` being another let — can be collapsed at emit time. Options:

- **A. Turn chained lets into a single C# block**: emit
  `{ var x = val1; var y = val2; ...; return body; }` when `body` is a
  chain of lets. Requires detecting the chain at emit time (walking the
  body until we hit a non-let) and emitting all bindings at once. Output
  shape changes but semantics identical. Bootstrap 2 byte-equivalence must
  still hold (stage 1 emits the same way).
- **B. Collect into a List<Text> in emit-let**: if all emit functions
  started returning `List<Text>` instead of `Text`, a single `text-concat-list`
  at the chapter level would eliminate all per-level concat work. Massive
  refactor of ~30 emit-* functions.

Start with **A** — scoped change to `emit-let`, biggest payoff, smallest
diff. Estimated saving: 200–400ms (most of the 67% IrLet share).

### 2. Same collapse for `emit-if` (-40ms) and `emit-apply-args` (-20ms)

Secondary fixes in the same shape. After A lands, rebench to confirm these
are still worth chasing.

### 3. Measurement sanity check

`sum-out` of 5.6M chars at typical .NET `string.Concat` speed
(~1 GB/s for ref copies) = ~6ms. That's **way less** than the 1053ms
emit phase. Something besides raw concat is eating the time:

- Debug-mode Func<> delegate dispatch (the emitter is written in
  heavily curried style — `((Func<T, string>)((x) => ...))(arg)` per
  pattern arm).
- Per-call object allocations for Func<> instances, boxed patterns,
  etc.

Before committing to a big refactor, run the bench in `-c Release` to see
if the dispatch overhead shrinks. If Release brings emit below ~300ms, the
current quadratic is fine and the real issue is Debug build cost.

## Reproduction

```bash
dotnet build tools/Codex.Bootstrap/Codex.Bootstrap.csproj -c Debug -p:SkipCodexRegenerate=true
cd tools/Codex.Bootstrap && dotnet run -c Debug --no-build -- --bench
```

Re-apply instrumentation: add a `PerfCounters` static class (per-variant
call count + sum of output lengths + max depth), rename
`emit__csharp_emitter_emit_expr` to `…_impl`, wrap with counter updates.
Report after warmup, before measured runs.
