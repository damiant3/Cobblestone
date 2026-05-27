# Iteration 5 — Handoff Summary

**Date**: 2026-03-14 Pi Day
**Commits**: `3d21ed2` (algebraic types), `e403956` (type params + exhaustiveness)
**Branch**: `master`
**Remote**: https://github.com/damiant3/NewRepository
**All pushed**: ✅ Yes

---

## What Was Done

### Milestone Status

| Milestone | Status | Notes |
|-----------|--------|-------|
| M0: Foundation | ✅ Complete | |
| M1: Hello Notation | ✅ Complete | |
| M2: Type Checking | ✅ Complete | Sum types, record types, type params, exhaustiveness all done |
| M3: Execution via C# | ✅ Complete | |
| M4: Prose Integration | ✅ Complete | |
| **M5: Effects** | **🔲 Not started** | Next major milestone |

### New Code This Iteration

| File | What |
|------|------|
| `src/Codex.Core/Map.cs` | Null-safe immutable dictionary wrapper; replaces `ImmutableDictionary` + `TryGetValue` throughout |
| `src/Codex.Syntax/SyntaxNodes.cs` | Added `TypeNode` abstract base, `NamedTypeNode`, `FunctionTypeNode`, `ApplicationTypeNode`, `ParenthesizedTypeNode`, `TypeAnnotationNode`, `TypeDefinitionNode`, `RecordTypeBody`, `RecordTypeFieldNode`, `VariantTypeBody`, `VariantConstructorNode`, `VariantFieldNode`; updated `DocumentNode` + `NotationBlockNode` to carry `TypeDefinitions` |
| `src/Codex.Syntax/Parser.cs` | `TryParseTypeDefinition`, `ParseRecordTypeBody`, `ParseVariantTypeBody`, `ParseTypeAnnotation`; `ParseDocument` routes `TypeIdent =` to type defs |
| `src/Codex.Ast/AstNodes.cs` | `TypeDef`, `RecordTypeDef`, `RecordFieldDef`, `VariantTypeDef`, `VariantCtorDef`, `VariantFieldDef`; `Module` now carries `TypeDefinitions` |
| `src/Codex.Ast/Desugarer.cs` | `DesugarTypeDefinition`, maps CST type defs to AST type defs |
| `src/Codex.Semantics/NameResolver.cs` | Registers type names and constructor names; `ResolvedModule` tracks `TypeNames` + `ConstructorNames` |
| `src/Codex.Types/CodexType.cs` | `RecordType` + `RecordFieldType`, `SumType` + `SumConstructorType` (both with `TypeParamIds`), `CtorInfo` |
| `src/Codex.Types/TypeChecker.cs` | `RegisterTypeDefinitions` (with type param scoping via `m_typeParamEnv`), `ResolveNamedType` checks type param env, `ResolveAppliedType` instantiates user-defined parametric types, `InstantiateParametricType`, `CheckExhaustiveness` (CDX2020 warning), `InferRecord`, `InferFieldAccess`; constructor types wrapped in `ForAllType`; `Instantiate` peels all `ForAllType` layers |
| `src/Codex.Types/TypeEnvironment.cs` | Converted to use `Map<K,V>` |
| `src/Codex.Types/Unifier.cs` | Structural field unification for `SumType`/`RecordType`; `DeepResolve` + `OccursIn` extended for both; `SubstituteVar` extended for both |
| `src/Codex.IR/IRModule.cs` | `IRCtorPattern`; `IRModule` carries `TypeDefinitions` map |
| `src/Codex.IR/Lowering.cs` | Accepts `CtorMap` + `TypeDefMap`; `LowerCtorPattern`; `LookupName` checks ctor map; passes `TypeDefinitions` to `IRModule` |
| `src/Codex.Emit.CSharp/CSharpEmitter.cs` | `EmitTypeDefinitions`, `EmitSumType` (abstract record + sealed record per ctor), `EmitRecordType`; `EmitCtorPatternBody` destructures fields as `Field0`, `Field1`…; `show` builtin maps to `Convert.ToString`, `negate` to unary minus |
| `tools/Codex.Cli/Program.cs` | Passes `ConstructorMap` + `TypeDefMap` to `Lowering` |
| `samples/shapes.codex` | Sum type demo: `Shape = Circle | Rect`, pattern match |
| `samples/person.codex` | Record type demo: field access |
| `samples/safe-divide.codex` | M2 milestone demo: parametric `Result (a)`, `safe-divide`, `describe` |
| `.github/copilot-instructions.md` | Codified `new()` minimisation, usings discipline, `Map<>` preference |

### Bug Fixes This Iteration

1. **`TypeNode` / `PatternNode` lost from `SyntaxNodes.cs`** — a stash/restore cycle during a previous session left both abstract base records missing; restored in correct order before concrete subtypes.
2. **Broken string interpolation in `CSharpEmitter`** — `{EmitType(x.Type}>)` had missing `)` inside interpolation; rewritten using local `string funcType = …` variable to avoid the issue.
3. **`True`/`False` vs `true`/`false`** — Codex keywords are `True`/`False` (capital); test used lowercase and hit CDX3002 undefined name errors. Fixed in test.
4. **`show` built-in not emitted** — `show n` was calling a non-existent C# method; now emitted as `Convert.ToString(n)`.
5. **`PatternNode` ordering** — abstract base record appeared after its concrete subclasses; fixed to appear before.

### New Tests (+17 vs iteration 4 baseline of 107)

| Project | Tests | Delta |
|---------|-------|-------|
| Codex.Core.Tests | 16 | — |
| Codex.Syntax.Tests | 44 | +5 (type def parsing) |
| Codex.Ast.Tests | 11 | — |
| Codex.Semantics.Tests | 10 | — |
| Codex.Types.Tests | 43 | +12 (integration + exhaustiveness + parametric) |
| **Total** | **124** | **+17** |

New test names in `IntegrationTests.cs`:
- `Sum_type_compiles_to_csharp`
- `Sum_type_pattern_match_type_checks`
- `Record_type_parses_and_type_checks`
- `Record_field_access_type_checks`
- `Constructor_as_function_type_checks`
- `Exhaustive_match_produces_no_warning`
- `NonExhaustive_match_produces_warning` (CDX2020 emitted for missing constructors)
- `Wildcard_match_is_exhaustive`
- `Parametric_sum_type_type_checks`
- `Parametric_sum_type_pattern_match_type_checks`
- `Parametric_sum_type_compiles_to_csharp`

---

## Architecture After This Iteration

### Type System Shape

```
CodexType
  ├── IntegerType / NumberType / TextType / BooleanType / NothingType / VoidType
  ├── FunctionType(Parameter, Return)
  ├── ListType(Element)
  ├── TypeVariable(Id)
  ├── ForAllType(VariableId, Body)          ← wraps polymorphic constructor types
  ├── ConstructedType(Name, Args)           ← fallback for unknown applied types
  ├── RecordType(TypeName, TypeParamIds, Fields)
  ├── SumType(TypeName, TypeParamIds, Constructors)
  └── ErrorType
```

Constructors (e.g. `Just`) are registered as `ForAllType(a, FunctionType(TypeVar(a), SumType(Maybe, [a], ...)))`.  
Applying `Just x` → `Instantiate` peels `ForAllType`, unification resolves the fresh var.

### Parametric Types

- `Maybe (a) = | Just (a) | None` — `a` bound as a fresh `TypeVariable` scoped to the definition
- `TypeParamIds` stored on `SumType`/`RecordType` so `InstantiateParametricType` can substitute args
- `ResolveAppliedType` handles `Maybe Integer` → looks up `Maybe` in `m_typeDefMap`, calls `InstantiateParametricType`
- `SubstituteVar` traverses into `SumType`/`RecordType` fields recursively

### Exhaustiveness Checking

After all branches of a `MatchExpr` are type-checked, `CheckExhaustiveness` runs:
- Only fires on `SumType` scrutinees
- Skipped if any branch is a `VarPattern` or `WildcardPattern` (catch-all)
- Collects `CtorPattern` names across all branches; reports missing ones as CDX2020 **warning** (not error — doesn't block compilation)

### C# Emission of Algebraic Types

Sum type `Color = | Red | Green | Blue` emits:
```csharp
public abstract record Color;
public sealed record Red : Color;
public sealed record Green : Color;
public sealed record Blue : Color;
```

Constructor pattern match `if Circle (r) ->` emits as:
```csharp
(scrutinee is Circle _mCircle_ ?
  ((Func<decimal, ...>)((r) => body))(_mCircle_.Field0) : ...)
```

Record type `Point = record { x : Number, y : Number }` emits:
```csharp
public sealed record Point(decimal x, decimal y);
```

---

## Known Limitations / Not Yet Done

- **Pattern matching exhaustiveness** is a warning only (CDX2020), not an error. This is correct for now since wildcards / var patterns are valid catch-alls.
- **No nested ctor patterns** — `if Just (Just (n)) ->` is not handled in `EmitCtorPatternBody` (only `IRVarPattern` sub-patterns are destructured; nested ctor sub-patterns are silently ignored).
- **Type parameter arity checking** — `Maybe Integer Text` (too many args) is not reported as an error.
- **No effect system** — all functions are pure.
- **ProseParser source spans** are relative to notation blocks, not the original file.
- **`codex run` overhead** — shells out to `dotnet build` + `dotnet run` (~2s).
- **`Codex.Proofs`**, **`Codex.Repository`**, **`Codex.Narration`** are empty stubs (added to solution, not implemented).
- **`show` only works at application site** — `show` as a first-class value (e.g. passed to `map`) won't emit correctly.

---

## What's Next (Suggested for Iteration 6)

### Milestone 5: Effects (recommended next)
- [ ] Effect row types in `Codex.Types`: `[Console]`, `[State s]`
- [ ] Effect checking: pure functions cannot call effectful functions
- [ ] `do` notation desugaring (sequencing + bind)
- [ ] Built-in effects: `Console` (print-line, read-line), `State`
- [ ] C# backend: effects as context/interface arguments
- [ ] Demo: `main : [Console] Nothing` that reads and prints

### M2 Remaining Gaps
- [ ] Nested constructor pattern matching in emitter (`if Just (Just (n)) ->`)
- [ ] Type parameter arity error (too many / too few args)
- [ ] `show` as first-class value

### M4 Remaining Gaps
- [ ] Prose template matching ("An X is a record containing:", "X is either:")
- [ ] The account module example from `NewRepository.txt` should parse end-to-end
- [ ] Inline code references in prose (backtick-delimited)

### Milestone 7: Repository (Local)
- [ ] Implement `Codex.Repository` — content-addressed fact store
- [ ] `codex init`, `codex publish`, `codex history`
- [ ] Import resolution from local store

---

## Environment Notes

- **Solution file**: `Codex.sln` (root)
- **Build**: `dotnet build Codex.sln` — zero warnings
- **Test**: `dotnet test Codex.sln` — 124 tests, all pass
- **TreatWarningsAsErrors**: `true`
- **Boolean literals**: `True` / `False` (capital) — not `true`/`false`
- **No XML doc comments** — ruled out in `.github/copilot-instructions.md`
- **No `var` when type is non-obvious**; use `new()` where target type is declared
- **`Map<K,V>`** (in `Codex.Core`) — use instead of `ImmutableDictionary` + `TryGetValue`
- **Agent instructions**: `.github/copilot-instructions.md` and `copilot-instructions.md`
- **New projects added to solution**: `Codex.Proofs`, `Codex.Repository`, `Codex.Narration` (all stubs)
