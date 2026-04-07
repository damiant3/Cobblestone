# Bootstrap Status — Step 1b Complete

**Date:** March 16, 2026
**State:** Stage 1 output (`stage1-output.cs`) successfully generated. Not yet at fixed point.
**Verified:** Build ✅, 689 tests ✅, Stage 0 regen consistent ✅, Bootstrap produces output ✅

---

## Gap #1 Fix Iteration — Round 3

### Round 1 (6 fixes — TypeChecker.codex + Unifier.codex)

1. **infer-name**: Instantiate `ForAllTy` wrappers with fresh vars (polymorphic builtins)
2. **AFieldAccess/ARecordExpr**: Return fresh vars / `ConstructedTy` instead of `ErrorTy`
3. **resolve-type-expr for AAppType**: Handle `List` + type args instead of discarding them
4. **check-def**: Thread module env through so defs can see other defs' types
5. **unify-structural**: Handle `ConstructedTy`, `SumTy`, `RecordTy`, `ForAllTy`, cross-matching
6. **deep-resolve**: Recurse into `ConstructedTy`, `ForAllTy`, `SumTy`, `RecordTy`

### Round 2 (3 fixes — TypeChecker.codex)

7. **register-type-defs**: Register variant constructors + record types into the type env
8. **bind-pattern for ACtorPat**: Decompose constructor function types for sub-pattern bindings
9. **check-module**: Wire up type definition registration before definition registration

### Round 3 (1 fix — Desugarer.codex) ← **THIS SESSION**

10. **desugar-def: Propagate type annotations.** The desugarer was setting
    `declared-type = []` for every definition, discarding all type annotations.
    Now it extracts the type expression from `d.ann` and desugars it, so annotated
    functions get their declared types propagated to the type checker. This was the
    single biggest remaining issue — it caused all parameter types to be `object`.

### Results

| Metric | Original | R1 | R2 | **R3 (now)** | Stage 0 target |
|--------|---------|----|----|-------------|----------------|
| Unification errors | 1,864 | 1,066 | 203 | **21** | 0 |
| `object` type refs | 1,105 | 1,091 | 1,113 | **73** | 5 |
| `string` concrete | 87 | 81 | 82 | **161** | 477 |
| `long` concrete | 21 | 25 | 25 | **163** | 292 |
| `Func<` types | 2 | 2 | 2 | **16** | 636 |
| `is var _` pattern | 321 | 330 | 346 | **348** | 1 |
| `string.Concat` | 0 | 0 | 0 | **0** | 180 |
| Output size (chars) | — | — | 121,615 | **126,459** | 207,587 |
| Type bindings | 332 | 340 | 346 | **347** | — |

### Key signatures now matching Stage 0

```
Stage 0: public static AExpr desugar_expr(Expr node)
Stage 1: public static AExpr desugar_expr(Expr node)          ✅ MATCH

Stage 0: public static List<Token> tokenize(string src)
Stage 1: public static List<Token> tokenize(string src)       ✅ MATCH

Stage 0: public static ModuleResult check_module(AModule mod)
Stage 1: public static ModuleResult check_module(AModule mod) ✅ MATCH

Stage 0: public static IRModule lower_module(AModule m, List<TypeBinding> types, UnificationState ust)
Stage 1: public static IRModule lower_module(AModule m, List<TypeBinding> types, UnificationState ust) ✅ MATCH
```

---

## Remaining Gaps

### Gap #1: Type Inference (21 errors remain)

Down from 1,864 to 21. The last 21 errors are likely edge cases in:
- Unannotated higher-order functions
- Complex polymorphic instantiation chains
- A few missing pattern cases

**Files:** `Codex.Codex/Types/TypeChecker.codex`, `Codex.Codex/Types/Unifier.codex`

### Gap #2: Let Expression Emission — `is var _` vs Lambda (348 instances)

Stage 1 emits `((Type x = expr) is var _ ? body : default)` (invalid C#).
Stage 0 emits `((Func<T,R>)((x) => body))(expr)` (valid C#).
**File:** `Codex.Codex/Emit/CSharpEmitter.codex` line 142-144
**Impact:** This is the single biggest code-shape difference. Fixing it would
make Stage 1 output compilable.

### Gap #3: String Concatenation — `+` vs `string.Concat`

Stage 1 uses `+` for text append. Stage 0 uses `string.Concat`.
**File:** `Codex.Codex/Emit/CSharpEmitter.codex`

### Gap #4: Curried vs Multi-Arg Calls

Stage 1: `f(a)(b)(c)`. Stage 0: `f(a, b, c)`.
**File:** `Codex.Codex/Emit/CSharpEmitter.codex`

---

## Verification Commands

```sh
dotnet build Codex.sln              # Stage 0 clean
dotnet test Codex.sln               # Tests pass
codex build Codex.Codex             # Regenerate out/Codex.Codex.cs (Step 0c)
# copy out/Codex.Codex.cs → tools/Codex.Bootstrap/CodexLib.g.cs
dotnet build Codex.sln              # Rebuild with new Stage 1
dotnet run --project Codex.Bootstrap # Produce stage1-output.cs (Step 1b)
# diff/analyze out/Codex.Codex.cs vs stage1-output.cs
```

## Verification Notes

- After fresh Stage 0 regeneration, all function bodies are byte-identical
  to the committed version. Only type declaration ordering differs (cosmetic,
  due to file discovery order). No functional divergence.
- The `out/Codex.Codex.cs` and `CodexLib.g.cs` should always be regenerated
  together after `.codex` changes to stay in sync.
