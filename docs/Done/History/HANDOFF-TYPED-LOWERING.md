# Handoff: Close the Self-Hosting Type Gap

**Goal:** Make the self-hosted Codex compiler (Stage 1) produce correctly
typed C# output so it can compile Codex programs with the same fidelity
as Stage 0 (the C# bootstrap compiler). Right now Stage 1 emits `object`
for every inferred type because the lowering pass ignores the type
checker's results.

---

## The One Sentence Problem

`Codex.Codex/IR/Lowering.codex` hard-codes `ErrorTy` for every type
annotation, so the generated C# has `object` everywhere instead of
`long`, `string`, `Func<…>`, etc.

---

## Files You Must Read (in this order)

| # | File | Why |
|---|------|-----|
| 1 | `Codex.Codex/main.codex` | The pipeline: `compile-checked` runs type checking then lowering **independently**. |
| 2 | `Codex.Codex/IR/Lowering.codex` | **The broken file.** `lower-def` sets `type-val = ErrorTy` for every def, param, and pattern. ~130 lines. |
| 3 | `Codex.Codex/Types/TypeChecker.codex` | Produces `ModuleResult` with `List TypeBinding` (name→type). This output is never consumed by lowering. ~340 lines. |
| 4 | `Codex.Codex/Types/Unifier.codex` | `deep-resolve` and `resolve` — needed to chase `TypeVar` → concrete type. ~200 lines. |
| 5 | `Codex.Codex/Types/TypeEnv.codex` | `builtin-type-env` — missing entries for `show`, `print-line`, `list-at`, `list-length`, `map`, `filter`, `fold`. ~75 lines. |
| 6 | `Codex.Codex/Emit/CSharpEmitter.codex` | `cs-type` maps `CodexType` → C# type string. Already correct — just never receives real types. ~350 lines. |
| 7 | `Codex.Codex/IR/IRModule.codex` | Data definitions for `IRDef`, `IRParam`, `IRExpr`, etc. ~75 lines. |
| 8 | `Codex.Codex/Ast/AstNodes.codex` | `ADef` (has `declared-type : List ATypeExpr`), `AParam`, `AModule`. Needed to understand lowering input shape. |

### Reference files (Stage 0 — read only to understand the target behavior)

| File | Why |
|------|-----|
| `tools/Codex.Cli/Program.Compile.cs` lines 78–82 | Shows how Stage 0 wires type checker output into `Lowering`: `new Lowering(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics)` |
| `src/Codex.IR/Lowering.cs` lines 1–70 | Stage 0 `LowerDefinition` — uses `m_typeMap[def.Name.Value]` to get full type, peels `FunctionType` layers for params. This is the behavior to replicate. |
| `src/Codex.Emit.CSharp/CSharpEmitter.cs` | Stage 0 emitter. Not broken, just useful for comparing output. |

---

## Files You Do NOT Need to Read

- `docs/00-OVERVIEW.md` through `docs/10-PRINCIPLES.md` — architecture spec, unchanged
- `docs/FORWARD-PLAN.md`, `docs/08-MILESTONES.md` — background only, no actionable info beyond what's here
- `docs/DECISIONS.md`, `docs/GLOSSARY.md` — reference, not needed for this task
- Any emitter other than `Codex.Emit.CSharp` (JS, Rust, Python, etc.)
- `Codex.Codex/Syntax/` — Lexer/Parser are working and not touched
- `Codex.Codex/Semantics/NameResolver.codex` — working, not touched
- `codex-in-codex/` — old copy of Codex source, superseded by `Codex.Codex/`
- `codex-src/output.cs` — old generated output, not the current one
- `src/Codex.Lsp/`, `src/Codex.Proofs/`, `src/Codex.Repository/` — unrelated

---

## Exactly What to Change

### Change 1: Thread type map into lowering (`Lowering.codex`)

**Current:** `lower-module` and `lower-def` take only the `AModule`/`ADef`
and use `ErrorTy` for all types.

**Target:** `lower-module` accepts `List TypeBinding` (from `check-module`
result) plus the `UnificationState` (for resolving type variables). Each
`lower-def` looks up its full type in the type binding list, peels
`FunTy` layers for parameters, and passes the return type into `lower-expr`.

Specific sub-changes in `Lowering.codex`:

1. Add a `TypeMap` record or use `List TypeBinding` directly:
   ```
   lookup-type : List TypeBinding -> Text -> CodexType
   ```

2. Change `lower-module` signature:
   ```
   lower-module : AModule -> List TypeBinding -> UnificationState -> IRModule
   ```

3. Change `lower-def` to look up the definition's type, peel `FunTy`
   for each parameter, and pass the remaining type as `expectedType`
   to `lower-expr`:
   ```
   lower-def : ADef -> List TypeBinding -> UnificationState -> IRDef
   ```

4. Change `lower-expr` to accept and propagate `expectedType`:
   - `lower-apply`: infer function type, peel `FunTy`, pass return type
   - `lower-let`: lower value with `ErrorTy`, lower body with `expectedType`
   - `lower-lambda`: create fresh param types from expected `FunTy`
   - `lower-match`: propagate `expectedType` to branches
   - `lower-pattern`: for `ACtorPat`, use scrutinee type context

5. Change `lower-param` to accept the resolved parameter type instead
   of hardcoding `ErrorTy`.

### Change 2: Wire it up in `main.codex`

**Current (`compile-checked`):**
```
let check-result = check-module ast
in let ir = lower-module ast
```

**Target:**
```
let check-result = check-module ast
in let ir = lower-module ast check-result.types check-result.state
```

Also update the simpler `compile` pipeline to either call through
`compile-checked` or add its own type checking step.

### Change 3: Add missing builtins to `TypeEnv.codex`

`builtin-type-env` is missing several functions that the Codex source
itself uses. Add:

| Name | Type |
|------|------|
| `show` | `ForAllTy 0 (FunTy (TypeVar 0) TextTy)` |
| `print-line` | `FunTy TextTy NothingTy` |
| `list-length` | `ForAllTy 0 (FunTy (ListTy (TypeVar 0)) IntegerTy)` |
| `list-at` | `ForAllTy 0 (FunTy (ListTy (TypeVar 0)) (FunTy IntegerTy (TypeVar 0)))` |
| `map` | polymorphic `(a → b) → List a → List b` |
| `filter` | polymorphic `(a → Boolean) → List a → List a` |
| `fold` | polymorphic `(b → a → b) → b → List a → b` |
| `read-line` | `TextTy` (nullary) |

### Change 4: Handle `deep-resolve` in lowering

After looking up a type from the type binding list, call `deep-resolve`
on it using the `UnificationState` to chase all `TypeVar` references
to their concrete types before using it.

---

## How to Verify

1. `dotnet build Codex.sln` — zero warnings (warnings are errors)
2. `dotnet test Codex.sln` — all 689 tests pass
3. Build the Codex project: run `codex build .` in `Codex.Codex/`
4. Inspect `Codex.Codex/out/Codex.Codex.cs`:
   - `main()` should return `object` (correct — `[Console] Nothing`)
   - `compile` should return `string` (not `object`)
   - `tokenize` should return `List<Token>` (not `object`)
   - `emit-expr` should return `string` (not `object`)
   - Function parameters should have concrete types (`long`, `string`,
     `List<Token>`, etc.) not `object`
5. Count `object` references — should be ≤ 4 (the `NothingTy`/`ErrorTy`
   cases that correctly map to `object`)

---

## Constraints (from `.github/copilot-instructions.md`)

- Private fields: `m_` prefix (not applicable here — Codex source has no classes)
- No `///` doc comments
- No speculative `using` directives
- 4-space indent, 120 char max line, UTF-8
- `sealed record` for immutable types, `readonly record struct` for small values
- Always read a file before editing
- Always run `dotnet build Codex.sln` and `dotnet test Codex.sln` after changes
- Never run multi-line PowerShell scripts — use `.ps1` files
- Codex source files use Codex syntax, not C#. Edits to `.codex` files
  must follow Codex language conventions visible in the existing files.

---

## What Success Looks Like

Stage 1 (the compiled `Codex.Codex.cs`) produces correctly-typed C#
output when given a test program like `square : Integer -> Integer`.
The function `square` should emit as `public static long square(long x)`
not `public static object square(object x)`. This closes the gap
identified in Milestone 13 and moves toward full bootstrap fixed-point.

## ✅ COMPLETED

All four changes implemented. `dotnet build` zero warnings, `dotnet test` 689 passed.
Stage 0 output (`Codex.Codex/out/Codex.Codex.cs`) now shows:
- `compile` → `string`, `tokenize` → `List<Token>`, `emit_expr` → `string`
- `main` → `object` (correct: `[Console] Nothing`)
- Only 5 `object` references total (2 string literals in `cs_type`, 1 `when`-expression
  cast in `lower_list`, 2 for `main`'s `NothingTy` return)
- `lower_module` takes `(AModule, List<TypeBinding>, UnificationState)`
- `lower_def` peels `FunTy` layers for concrete parameter types

### What was changed
1. **`Codex.Codex/IR/Lowering.codex`** — Added `lookup-type`, `peel-fun-param`,
   `peel-fun-return`, `strip-forall-ty`. Changed `lower-module`/`lower-def` to
   accept `List TypeBinding` + `UnificationState`, look up and `deep-resolve`
   types, peel `FunTy` for params. Changed `lower-lambda` to extract param
   types from expected type. Changed `lower-list` to extract element type.
2. **`Codex.Codex/main.codex`** — Both `compile` and `compile-checked` now
   run `check-module` and pass `.types`/`.state` to `lower-module`.
3. **`Codex.Codex/Types/TypeEnv.codex`** — Added 8 missing builtins:
   `show`, `print-line`, `list-length`, `list-at`, `map`, `filter`, `fold`, `read-line`.

---

**Goal:** Make the self-hosted Codex compiler (Stage 1) produce correctly
