# IL backend parity lift

The IL backend (`src/Codex.Emit.IL/`) is REF-only and has not had a
systematic parity pass since the effect-handler work in 2026-03-21 (which
actually pushed IL *ahead* of CSharp/X86_64 on `IRHandle` / `IRRunState`).
Meanwhile CSharp and X86_64 have picked up language features -- bitwise
ops, `PowInt`, `int-mod`/`min`/`max`, various list/text builtins -- that
IL silently lacks. This doc tracks the lift to close the gap.

Scope boundary: "parity" here means **within REF**, across `Codex.Emit.IL`
vs `Codex.Emit.CSharp` vs `Codex.Emit.X86_64`. Self-host has no IL
backend; porting IL to Codex is post-MM4 at best.

## Status -- work remaining

Same convention as the compiler matrix: `denominator − numerator = work
remaining`. Sweep denominator is the same 90-sample applied set used by
Row 6/9; IL-specific denominators come from the unit-test tables in
`tests/Codex.Types.Tests/ILEmitter*Tests.cs` (105 Facts across 5 files).

Sweep baseline prior to this lift (CL 269): **43 pass / 18 runtime-mismatch
/ 21 diag-harness-quirk / 8 skip of 90**. The 21 diag-mismatches are a
pre-existing sweep-harness issue (identical on `--compiler=ref`), not
IL-specific -- see "Non-findings" below.

| Item | Status | Notes |
|------|--------|-------|
| Effect handlers (`IRHandle`, `IRRunState`, `IRGetState`, `IRSetState`) | ✅ | Phase 1+2 complete (2026-03-21); IL is ahead of X86_64 here |
| Generics, sum-type ctor tagging, records, pattern matching, TCO | ✅ | Parity with CSharp/X86_64 |
| Lit/name/binary/if/let/apply/act/list/field-access/region emit | ✅ | |
| `IRLambda` emit | ✅ | CL 273 -- `TargetNeedsLambdaLifting` now includes `il`; lifting rewrites lambdas to top-level defs before emit |
| `IRError` emit | ✅ | CL 273 -- `throw new InvalidOperationException(err.Message)` |
| `PowInt` binary op | ✅ | CL 273 -- `conv.r8 / Math.Pow / conv.i8` |
| `AppendList` / `ConsList` binary ops | ✅ | CL 273 -- `new List<T>(); AddRange / Add` pattern |
| Default `throw` in `EmitExpr` / `EmitBinary` switches | ✅ | CL 273 -- future gaps fail loud instead of silent |
| Bitwise builtins (6 incl. `bit-not`) | ✅ | CL 273 -- `bit-and/or/xor/shl/shr/not` |
| Arithmetic builtins (4 incl. `abs`) | ✅ | CL 273 -- `abs`, `int-mod` (Euclidean), `min`, `max` |
| Text builtins (2) | ✅ | CL 273 -- `text-compare` (CompareOrdinal), `text-concat-list` |
| List mutation builtins (3) | ✅ | `list-snoc` (CL 273), `list-insert-at`, `list-set-at` -- samples: `list-insert-at-test`, `list-set-at-test` |
| `write-binary` builtin | ✅ | `Console.OpenStandardOutput / Stream.WriteByte / Flush` loop -- sample: `write-binary-test` |
| Partial application (single remaining arg) | ✅ | Per-function closure class with `Func<A, TRet>` wrapper -- handles `add 10` / `make-box lbl offset` / `partial 10 5` shapes. |
| Arity-1 bare method reference | ✅ | `ldnull + ldftn + newobj Func<T, TRet>` with null target; handles `emit = emit-one` shape. |
| Invoke-function-value via `Func<>.Invoke` | ✅ | Walks function-typed locals/params/fields; peels one FunctionType level per arg. |
| Multi-step partial application (>1 remaining arg) | 🟡 | Cascading closures not yet implemented. Blocks `multi-lambda-in-record`, `poly-runtime`, `list-test` (via `list-foldl add`), `expr-calculator`. |
| Concurrency builtins (`fork`/`await`/`race`/`par`) | ⚪ | Camp II -- grammar-only on X86_64 too; not a parity gap |
| `--compiler=il` sweep mode in `ref-sweep.sh` | ✅ | CL 269 -- harness wired, parallel-capable via `--jobs=N` |

Sweep progression:

- Pre-audit baseline: 43 pass / 90
- CL 269 (harness): baseline measurement set -- 43 pass
- CL 273 (blockers + builtins): 49 pass (+6)
- CL 279 (builtin parity + samples): 52 pass (+3 via new samples)
- Current (partial-app + bare method refs): **57 pass / 28 runtime/diag-fail / 8 skip of 93**.

The 28 fails break down: 21 are pre-existing diag-harness quirks
shared with `--compiler=ref`; 3 are target-semantic divergence
(`effectful-hello`, `shapes`, `w3` -- same 3 that fail under
`--compiler=selfhost-cs`); 4 are multi-step partial app (tracked
above).

## Remaining work

### Multi-step partial application (cascading closures)

The single-remaining-arg case is done. The remaining gap is
closures that must themselves return a `Func<>` -- i.e. when a method
of arity N is partial-applied with k args where N − k > 1. Each
intermediate level needs its own closure class whose `Invoke`
constructs the next closure up the chain:

```
__f_closure_1 { A _a0; Invoke(B b) => new __f_closure_2(_a0, b); }
__f_closure_2 { A _a0; B _a1; Invoke(C c) => f(_a0, _a1, c); }
```

For a method of arity N partial-applied with k args, we need N − k
closure classes (k, k+1, …, N − 1). The scan in `PartialApp.cs`
already emits the (method, captured_count) pair for the observed
partial-app site; extending it to *also* emit the intermediate
classes (captured_count + 1, …, N − 1) would close the gap.

Sweep samples blocked by this:

- `multi-lambda-in-record`: lambda `\a b c -> ...` lifted to a 3-arg
  def used as a record field → 0-captured-of-3 method reference.
- `poly-runtime`: polymorphic partial-app chain (needs inspection).
- `list-test`: uses `list-foldl add 0 nums` from the Foreword List
  module -- `add` is 2-arg method ref, scanned as 0-captured-of-2.
- `expr-calculator`: recursive-descent parser with higher-order
  combinator helpers; fails mid-sample with wrong operand selection,
  likely cascading from a multi-step partial app.

Implementation is in `ILAssemblyBuilder.PartialApp.cs`:
`ScanPartialApps` walks the module IR to identify `(method, captured_count)`
pairs; `EmitClosureClasses` synthesizes a `TypeDef` with captured-arg
fields, a ctor, and an `Invoke` that calls the original method. The caller
site at `EmitApply` ({ILAssemblyBuilder.cs} -- look for `ClosureKey`) emits
`newobj closure; ldftn Invoke; newobj Func<T,TRet>(object, nativeint)`.
`EmitInvokeFuncChain` handles the counterparts: loading a function-typed
value and peeling one arg at a time via `callvirt Func<>.Invoke`.

To extend to multi-step partial-app: `ScanPartialApps.VisitApply` currently
emits a single spec per site. Change it to also emit closure specs for
`captured_count + 1, …, arity − 1`. `EmitOneClosureClass` currently assumes
`remaining.Count == 1` (matches CSharp's single-arg `Func<T,TRet>`); it
needs a branch that, when `remaining.Count > 1`, emits an `Invoke` body
that constructs the next closure up the chain and returns it as a
`Func<A_{k+1}, Func<…>>`. Each level is independently cacheable in
`m_funcCache`.

## Non-findings (audit cleared)

For the record, so the next person doesn't re-chase these:

- No `TODO` / `FIXME` / `NotImplementedException` in the IL sources.
- IRRunState / IRGetState / IRSetState are handled at
  `ILAssemblyBuilder.cs:1030–1043`. IRHandle at `:1045`.
- Pattern matching (`IRVarPattern`/`IRLiteralPattern`/`IRCtorPattern`/
  `IRWildcardPattern`) handled at `:2050–2230`.
- TCO loop rewriting present at `:802–2203`.
- `ILOpCode` enum casing (`Clt_un`/`Brfalse_s` etc.) is correct
  throughout -- the pitfall documented in the agent-memory notes hasn't
  reintroduced itself.
- The 21 `FAIL_WRONG_DIAGNOSTIC` bucket in the IL sweep is identical
  to the set reported on `--compiler=ref` -- a pre-existing
  sweep-harness quirk in how it matches `.failing` sidecar codes, not
  an IL issue.

## Non-findings (audit cleared)

For the record, so the next person doesn't re-chase these:

- No `TODO` / `FIXME` / `NotImplementedException` in the IL sources.
- IRRunState / IRGetState / IRSetState are handled at
  `ILAssemblyBuilder.cs:1030–1043`. IRHandle at `:1045`.
- Pattern matching (`IRVarPattern`/`IRLiteralPattern`/`IRCtorPattern`/
  `IRWildcardPattern`) handled at `:2050–2230`.
- TCO loop rewriting present at `:802–2203`.
- `ILOpCode` enum casing (`Clt_un`/`Brfalse_s` etc.) is correct
  throughout -- the pitfall documented in the agent-memory notes hasn't
  reintroduced itself.
