# Bootstrap Convergence Plan

> **Last updated**: After lowering context threading + pattern binding + constructor type extraction.

## Goal

Converge Stage 0 (reference compiler output) and Stage 1 (self-hosted
compiler output) until they are identical, proving the self-hosted Codex
compiler can correctly compile itself.

## The Convergence Loop

```
repeat:
  1. codex build Codex.Codex              → Stage 0 (out/Codex.Codex.cs)
  2. copy Stage 0 → CodexLib.g.cs         (strip main() call)
  3. dotnet build + test                   (689 tests must pass)
  4. dotnet run Codex.Bootstrap            → Stage 1 (stage1-output.cs)
  5. measure: diff sections, _p0_ lines, object lines, output sizes
  6. if Stage 0 ≈ Stage 1: DONE
  7. else: identify top divergence category, fix in .codex sources, goto 1
```

---

## Current Measurements

| Metric | Stage 0 | Stage 1 | Gap |
|--------|---------|---------|-----|
| Output size (chars) | 238,779 | 168,020 | 70K (Stage 1 = 70% of Stage 0) |
| Output lines | 4,590 | 1,151 | 3,439 |
| Function count | 372 | 376 | 4 extra in Stage 1 |
| Expression-bodied fns | 1 | 377 | **376 mismatch** |
| Block-bodied fns (TCO) | 376 | 0 | **376 mismatch** |
| `_p0_` (partial app) lines | 24 | 241 | 217 spurious |
| `object` (ErrorTy) lines | 5 | 114 | 109 spurious |
| Diff sections (fc /N) | — | 39 | — |
| Unification errors | — | 1,505 | — |
| Type bindings | — | 377 | — |

### Functions only in Stage 0

`main` — the entry point, stripped from CodexLib.g.cs by design.

### Functions only in Stage 1

`map_list_loop`, `fold_list_loop`, `fold_list`, `map_list`, `Console` —
these are polymorphic utility functions that the reference compiler inlines
or handles differently. `Console` is likely a false positive from a
`Console.WriteLine` detection issue.

---

## Divergence Categories (ranked by impact)

### D1: No tail-call optimization (376 functions)

**What**: Stage 0 emits every function as block-bodied with `while(true)`
TCO loops. Stage 1 emits every function as expression-bodied (`=>`).

**Why**: The reference C# emitter (`CSharpEmitter.cs`) detects tail-recursive
calls and rewrites them as `while(true) { ...; continue; }` loops. The
self-hosted emitter (`CSharpEmitter.codex`) does not implement TCO emission.

**Impact**: This is the **largest single source of divergence** — it affects
every function definition. It accounts for most of the 3,439 line count gap
(TCO expands each function to ~10 lines vs 1 for expression-bodied).

**Fix**: Implement `is-tail-call` detection and TCO emission in
`CSharpEmitter.codex`. The reference is `EmitTailRecursiveBody` in
`src/Codex.Emit.CSharp/CSharpEmitter.cs`. This is a **code generation
change**, not a type system change.

**Priority**: HIGH — but blocks on other fixes being stable first, since
TCO changes will massively change Stage 0 output, requiring a full
re-baseline.

### D2: Partial application markers (217 spurious `_p0_` lines)

**What**: Stage 1 wraps function calls in `(_p0_) => f(x, _p0_)` lambdas
when it thinks it has fewer args than the function's arity. Stage 0 has
only 24 such lines (genuinely partial applications).

**Root causes** (multiple):

1. **Polymorphic functions lose types**: `map_list`, `fold_list`, and similar
   polymorphic functions have type parameters that the self-hosted type
   checker can't resolve. The lowering sees `ErrorTy` for the function,
   can't decompose `FunTy` to find arg count, and falls back to curried
   emission. Example: `map_list(desugar_def(doc.defs), _p0_)`.

2. **Constructor types still missing for some patterns**: Even with
   constructor type extraction in `lower-module`, some nested patterns or
   ForAll-wrapped constructors may still fail lookup, causing pattern-bound
   variables to get `ErrorTy`.

3. **`let` bindings with `ErrorTy`**: When `lower-let` infers the value
   type as `ErrorTy`, the bound name gets `ErrorTy` in the context,
   poisoning all downstream uses.

**Fix**: Improve the self-hosted type checker's handling of:
- Polymorphic instantiation (ForAllTy / type variables)
- Constructor field types in pattern matching
- Let-binding type propagation

**Priority**: HIGH — this is the second-largest divergence source and directly
blocks correctness.

### D3: `object` type leakage (109 spurious lines)

**What**: Stage 1 emits `object` as the C# type for variables where the
lowering produced `ErrorTy`. Stage 0 emits concrete types.

**Manifestations**:
- `((object s = text_replace(...))` instead of `((string s = ...))`
- `new List<object>()` instead of `new List<IRExpr>()`
- `((object code = char_code(c))` instead of `((long code = ...))`

**Root cause**: Same as D2 — the self-hosted type checker returns `ErrorTy`
for expressions it can't type, and `ErrorTy` maps to `object` in C#
emission.

**Fix**: Same as D2 — improve type inference. Every fix to the type checker
reduces both `_p0_` and `object` counts.

**Priority**: HIGH — tied to D2.

### D4: Builtin function recognition failure

**What**: Stage 0 emits `name.Replace("-", "_")` for `text-replace name "-" "_"`.
Stage 1 emits `text_replace(name("-")("_"))` — it doesn't recognize the
builtin pattern, AND the arguments are curried.

**Root cause**: The self-hosted emitter's builtin detection (`emit-expr`
`IrApply` branch) checks the function type to decide if it's a method call
on a string/list. When the type is `ErrorTy`, the check fails and it falls
through to the generic function call path.

**Fix**: Two parts:
1. **Type system**: Fix type inference so `text-replace` gets type
   `Text -> Text -> Text -> Text` instead of `ErrorTy`.
2. **Emitter**: The self-hosted `CSharpEmitter.codex` `emit-expr` already
   has builtin detection logic — once types are correct it should work.

**Priority**: MEDIUM — affects ~20 builtin calls, but each is a localized
fix once types work.

### D5: Type definition ordering

**What**: Stage 0 and Stage 1 emit type definitions (records, variants) in
different orders. Stage 0 follows the order from the reference compiler's
IR lowering; Stage 1 follows the self-hosted lowering order.

**Impact**: Low — this is cosmetic and doesn't affect correctness. The
function definitions appear in the same order.

**Fix**: Adjust type definition emission order in `CSharpEmitter.codex`
or accept the difference.

**Priority**: LOW.

### D6: String concatenation style

**What**: Stage 0 emits `string.Concat(a, b)` for `++` on strings.
Stage 1 emits `(a + b)`.

**Impact**: Low — both are valid C# and produce identical runtime behavior.

**Fix**: Change `emit-binary` in `CSharpEmitter.codex` to emit
`string.Concat(...)` instead of `+` for `IrAppendText`. Or accept the
difference.

**Priority**: LOW.  (damian says zero.  if it will compile to IL the same, its the same.)

---

## Recommended Fix Order

### Phase 1: Type inference improvements (targets D2, D3, D4)

These are the highest-ROI fixes. Each improvement in the type checker
reduces `_p0_` lines, `object` lines, and builtin recognition failures
simultaneously.

1. **Polymorphic instantiation in `infer-apply`**: When inferring
   `f x` where `f : ForAll a. a -> List a`, the self-hosted checker must
   instantiate `a` to a fresh type variable, then unify with the argument
   type. Check that `instantiate-type` in `TypeChecker.codex` handles this
   correctly for all call patterns.

2. **Pattern-bound variable types in `infer-match`**: The self-hosted
   checker's `bind-ctor-sub-patterns` must decompose the constructor type
   to assign concrete types to each sub-pattern variable. Compare with the
   reference `BindCtorSubPatterns` in `TypeChecker.Inference.cs`.

3. **Let-binding type propagation**: Ensure `infer-let-binds` in the
   self-hosted checker doesn't lose types on intermediate bindings.

### Phase 2: TCO emission (targets D1)

Once Phase 1 stabilizes the type system:

4. **Detect tail-recursive calls** in `CSharpEmitter.codex`: scan the
   body for calls to the function's own name in tail position.

5. **Emit `while(true)` loops** with `continue` for tail calls and
   `return` for base cases. Reference: `EmitTailRecursiveBody` in
   `src/Codex.Emit.CSharp/CSharpEmitter.cs`.

### Phase 3: Cosmetic convergence (targets D5, D6)

6. **Type definition ordering**: Match the reference compiler's order.
7. **String concatenation**: Emit `string.Concat` instead of `+`.

---

## Work Completed

### Session 3: Lowering context threading + pattern binding

**Changes to `Codex.Codex/IR/Lowering.codex`**:
- Added `LowerCtx` record threading `List TypeBinding` and
  `UnificationState` through all lowering functions.
- `lower-name` now looks up names in `ctx.types` with `deep-resolve` to
  get concrete types instead of using the expected type (which was often
  `ErrorTy`).
- `lower-apply` decomposes the function's type via `ir-expr-type`,
  `peel-fun-param`, `peel-fun-return` to give each argument its correct
  expected type.
- `lower-let` and `lower-let-rest` bind let-variable types into the
  context using `ir-expr-type`.
- `lower-lambda` binds lambda parameters into the context.
- `lower-match` now passes scrutinee type to arms. `bind-pattern-to-ctx`
  binds `AVarPat` names with the scrutinee type, and `ACtorPat` names
  with field types decomposed from the constructor type.
- `lower-do-stmts-loop` binds `ADoBindStmt` names into the context.
- `lower-module` collects constructor type bindings from `ATypeDef` via
  `collect-ctor-bindings`, `ctor-bindings-for-typedef`,
  `collect-variant-ctor-bindings`, `build-ctor-type-for-lower`,
  `build-record-fields-for-lower`, `build-record-ctor-type-for-lower`,
  and prepends them to the type list so that `bind-pattern-to-ctx` can
  look up constructor types like `IrApply`.

**Changes to `Codex.Codex/IR/IRModule.codex`**:
- Added `ir-expr-type` function that extracts the `CodexType` from any
  `IRExpr` variant by pattern-matching on all IR node types.

**Results**: Stage 0 `_p0_` lines dropped from 28 → 24. Stage 0 output
size grew from 225,502 → 243,370 (+7.9%). All lowering fixes confirmed
correct by 689 passing tests.

### Session 2: Arity-flattening refactor

Refactored `CSharpEmitter.codex` to thread `List ArityEntry` and flatten
multi-arg function calls. Added `ArityEntry`, `ApplyChain`,
`collect-apply-chain`, `emit-apply`, partial application wrappers,
constructor detection. Stage 1 output grew from 129,085 → 151,893 (+18%).

### Session 1: Field access typing + parser fixes

- Field access typing in `TypeChecker.codex` via `RecordTy` lookup.
- Compound-expression parse fix in `Parser.codex`.
- Diagnostic improvements: `SourceSpan` carries `FileName`.

---

## Error Count History

| Change | Errors | Stage 1 Size | Stage 0 Size | Notes |
|--------|--------|-------------|-------------|-------|
| Old emitter (342 lines) | 203 | — | — | Simple emitter |
| New emitter + old CodexLib | 1,946 | — | — | — |
| New emitter + new CodexLib | 1,317 | 139,327 | — | — |
| Field access fix | 1,337 | 141,028 | — | — |
| Compound-parse fix | 1,255 | 129,085 | 219,586 | — |
| Arity-flattening | 1,368 | 151,893 | 225,502 | +23K Stage 1 |
| Lowering context threading | 1,505 | 168,021 | 243,370 | +16K Stage 1, +18K Stage 0 |

> **Note**: Error count rises as we add new code (more definitions = more
> type-check targets). The key health metric is Stage 1 output size, which
> has grown monotonically from 129K → 168K, and Stage 0 `_p0_` lines,
> which dropped from 28 → 24.

---

## Files to Modify

| File | What |
|------|------|
| `Codex.Codex/Types/TypeChecker.codex` | Type inference improvements |
| `Codex.Codex/Types/Unifier.codex` | Unification improvements |
| `Codex.Codex/Types/TypeEnv.codex` | Builtin type registrations |
| `Codex.Codex/Emit/CSharpEmitter.codex` | TCO emission, string.Concat |
| `Codex.Codex/IR/Lowering.codex` | Future lowering refinements |

## Files to Read (reference, read-only)

| File | Why |
|------|-----|
| `src/Codex.Types/TypeChecker.cs` | Reference: `CheckModule()` |
| `src/Codex.Types/TypeChecker.Inference.cs` | Reference: all `Infer*` methods |
| `src/Codex.Types/Unifier.cs` | Reference: `Unify()`, `Resolve()` |
| `src/Codex.Emit.CSharp/CSharpEmitter.cs` | Reference: `EmitTailRecursiveBody` |

## Verification Commands

```powershell
dotnet run --project tools/Codex.Cli -- build Codex.Codex
# Strip main() and copy:
$c = Get-Content Codex.Codex\out\Codex.Codex.cs -Raw
$c = $c -replace 'Codex_Codex_Codex\.main\(\);[\r\n]+', ''
Set-Content tools\Codex.Bootstrap\CodexLib.g.cs -Value $c -Encoding utf8 -NoNewline
dotnet build Codex.sln
dotnet test Codex.sln
dotnet run --project tools/Codex.Bootstrap
