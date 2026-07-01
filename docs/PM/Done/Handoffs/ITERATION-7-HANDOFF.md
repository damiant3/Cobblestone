# Iteration 7 — Handoff Summary

**Date**: 2026-03-14 Pi Day
**Branch**: `master`
**Remote**: https://github.com/damiant3/NewRepository

---

## What Was Done

### Milestone Status

| Milestone | Status | Notes |
|-----------|--------|-------|
| M0–M5 | ✅ Complete | See ITERATION-6-HANDOFF.md |
| **M6: Linear Types** | **✅ Complete** | Linearity annotations, checker, FileHandle, FileSystem builtins |
| **M7: Repository (Local)** | **✅ Complete** | Content-addressed fact store, `init`/`publish`/`history` CLI |

### M6 — Linear Types

| File | What |
|------|------|
| `src/Codex.Types/CodexType.cs` | Added `LinearType(Inner)` wrapper and `Usage` enum |
| `src/Codex.Syntax/SyntaxNodes.cs` | Added `LinearTypeNode(Inner)` CST node and `SyntaxKind.LinearType` |
| `src/Codex.Syntax/Parser.cs` | Parse `linear Type` in `ParseTypeAtom` → `LinearTypeNode` |
| `src/Codex.Ast/AstNodes.cs` | Added `LinearTypeExpr(Inner)` AST node |
| `src/Codex.Ast/Desugarer.cs` | Desugar `LinearTypeNode` → `LinearTypeExpr` |
| `src/Codex.Types/TypeChecker.cs` | Resolve `LinearTypeExpr` → `LinearType`; `SubstituteVar` handles `LinearType` |
| `src/Codex.Types/Unifier.cs` | `Unify`, `DeepResolve`, `OccursIn` handle `LinearType` |
| `src/Codex.Types/LinearityChecker.cs` | **New pass** — tracks linear variable usage per-definition; CDX2040 (unused), CDX2041 (double-use), CDX2042 (branch inconsistency) |
| `src/Codex.Types/TypeEnvironment.cs` | Added `open-file`, `read-all`, `close-file` builtins with `[FileSystem]` effect; `FileHandle` as `linear FileHandle` |
| `src/Codex.IR/Lowering.cs` | `BuildBuiltinTypes` includes FileSystem builtins |
| `src/Codex.Emit.CSharp/CSharpEmitter.cs` | `open-file` → `File.OpenRead`, `read-all` → `StreamReader.ReadToEnd`, `close-file` → `.Dispose()`; `LinearType` in `EmitType` (unwraps to inner) |
| `src/Codex.Semantics/NameResolver.cs` | Added `open-file`, `read-all`, `close-file` to builtin name set |
| `tools/Codex.Cli/Program.cs` | Linearity checker wired into compilation pipeline; `LinearTypeNode`/`LinearTypeExpr` in `FormatType`/`FormatTypeExpr` |

### M7 — Repository (Local)

| File | What |
|------|------|
| `src/Codex.Repository/FactStore.cs` | Full implementation: `Fact` record, `FactKind` enum, `FactStore` with `Init`/`Open`/`Store`/`Load`/`UpdateView`/`LookupView`/`GetView`/`GetHistory`. On-disk JSON in `.codex/facts/xx/xxxx...json` (2-char bucketing like git). View at `.codex/view.json`. |
| `tools/Codex.Cli/Codex.Cli.csproj` | Added `Codex.Repository` project reference |
| `tools/Codex.Cli/Program.cs` | `codex init [dir]`, `codex publish <file>`, `codex history <name>` implemented |
| `.gitignore` | Added `.codex/` (local repo data), `samples/*.cs` (build output), standard `[Bb]in/`, `[Oo]bj/`, `[Dd]ebug/`, `[Rr]elease/` patterns |

### Bug Fixes This Iteration

1. **C# `throw` in non-expression context** — top-level statements must precede type declarations in C#. Fixed by moving `Console.WriteLine(main())` before `EmitTypeDefinitions`.

2. **Missing `new` on constructor calls** — `Circle(5.0m)` emitted without `new`. Fixed: `FindConstructorName` detects uppercase-initial names in curried `IRApply` chains; zero-arg constructors (`None`) handled in `IRName` case (uppercase + non-FunctionType).

3. **C# type narrowing in multi-branch match** — `new Circle(...) is Rectangle` rejected by C# static analysis. Fixed: when a match has multiple ctor branches, the scrutinee is bound to `_scrutinee_` via a lambda wrapper `((Func<T,R>)((_scrutinee_) => ...))(expr)`.

4. **Missing `))` closing parens in match expression** — each `IRCtorPattern`/`IRLiteralPattern` branch opened `(` but never closed it. Fixed: `openParens` counter tracks opened parens; all are closed at the fallback throw and at early-return paths.

5. **Compound expression parse boundary** — `when`/`if`/`let`/`do` bodies were being consumed as application arguments by the next definition's name token. Fixed: `ParseApplication` returns immediately after a compound expression (`MatchExpressionNode`, `IfExpressionNode`, `LetExpressionNode`, `DoExpressionNode`).

6. **Curried calls to multi-param user functions** — `safe_divide(42L)(7L)` instead of `safe_divide(42L, 7L)`. Fixed: `m_definitionArity` map built at `Emit` time; `FindDefinitionName` detects lowercase root name in curried chain; when `args.Count == arity`, flat call is emitted.

7. **Cast paren bugs** — `(({funcType}((` should be `(({funcType})((`. Fixed in `EmitVarBindingsAndBody`, `EmitLet`, and `IRVarPattern` in `EmitMatch`.

8. **Generic type param erases to `object` in destructuring** — `Success(object Field0)` but pattern lambda typed `Func<long, string>`. Fixed: `EmitVarBindingsAndBody` now emits `((T)access)` cast when field type is concrete.

9. **`Name?.Value` nullable struct confusion** — `rec.TypeName` is `Name?` (nullable struct), so `.Value` returns `Name` not `string`. Fixed with explicit null check: `rec.TypeName is not null ? rec.TypeName.Value.Value : ...`.

10. **Record construction and field access not lowered** — `RecordExpr` and `FieldAccessExpr` fell through to `IRError`. Added `IRRecord`/`IRFieldAccess` IR nodes, `LowerRecord`/`LowerFieldAccess` in `Lowering`, and emission in `CSharpEmitter`.

11. **`EmitExpr` static + instance field conflict** — adding `m_constructorNames`/`m_definitionArity` instance fields required de-staticizing all `Emit*` methods that call `EmitExpr` transitively.

### Samples Status

| Sample | Output | Notes |
|--------|--------|-------|
| `hello.codex` | `25` | ✅ |
| `factorial.codex` | `3628800` | ✅ |
| `fibonacci.codex` | `6765` | ✅ |
| `greeting.codex` | `Hello, World!` | ✅ |
| `person.codex` | `Hello, Alice!` | ✅ record construction + field access now works |
| `effects-demo.codex` | `Hello, Alice! Hello, Bob! Done!` | ✅ |
| `shapes.codex` | `78.5000` | ✅ variant match now works |
| `safe-divide.codex` | `got 6` | ✅ polymorphic variant + multi-param call now works |

### Test Count

**151 tests, all passing** (16 Core + 11 Ast + 51 Syntax + 10 Semantics + 63 Types)

---

## Architecture Snapshot

```
Source (.codex)
  → Lexer           (Codex.Syntax)
  → Parser          (Codex.Syntax)
  → Desugarer       (Codex.Ast)
  → NameResolver    (Codex.Semantics)
  → TypeChecker     (Codex.Types)
  → LinearityChecker (Codex.Types)   ← NEW in M6
  → Lowering        (Codex.IR)
  → CSharpEmitter   (Codex.Emit.CSharp)
  → dotnet run
```

CLI commands: `parse`, `check`, `build`, `run`, `read`, `init`, `publish`, `history`, `version`

---

## Known Limitations / Next Agent Priorities

### Immediate Known Issues

- **Multi-line function bodies not supported** — `f (x) = \n  expr` fails to parse. The body must be on the same line as `=`. This affects any function that would naturally span multiple lines. The parser reads one line per definition body. **Fix**: after `=`, allow indented continuation lines (treat `Indent` as continuing the current expression).

- **`arithmetic.codex` sample** — uses multi-line body syntax, currently broken.

- **`prose-greeting.codex` sample** — prose samples not tested in this iteration.

### Emitter Known Gaps

- **First-class multi-arg functions** — `print-line` and user functions work when called directly, but passing a 2-arg function as a value and applying it partially produces curried C# calls that won't match the method signature. The flat-call optimization only triggers when `args.Count == arity`; partial application falls through to curried form which is incorrect for user-defined multi-param methods. **Fix**: emit user multi-param functions as `Func<A, Func<B, R>>` (fully curried type) in addition to, or instead of, the multi-param C# method signature. Or always emit as curried C# and de-curry only at call sites that have all args.

- **Generic type params in variants erased to `object`** — `Result (a) = | Success (a)` emits `Success(object Field0)`. The cast fix in `EmitVarBindingsAndBody` handles the common case. The root fix is to emit generic C# records: `record Success<T>(T Field0)`. Needs type param threading through IR.

### Next Milestone

**M8: Dependent Types (Basic)** — `Vector n a`, type-level arithmetic, proof obligations.
This is marked `XL` effort. Consider doing **M9: LSP** first as it has more immediate user value and is `M-L`.

---

## Key Code Locations for Next Agent

| Task | File | Where |
|------|------|--------|
| Add new type | `src/Codex.Types/CodexType.cs` | Bottom of file |
| Add new AST node | `src/Codex.Ast/AstNodes.cs` | After related node |
| Add new CST node | `src/Codex.Syntax/SyntaxNodes.cs` | `SyntaxKind` enum + new record |
| Add new IR node | `src/Codex.IR/IRModule.cs` | Bottom; add lowering in `Lowering.cs` and emission in `CSharpEmitter.cs` |
| Add new builtin | `TypeEnvironment.WithBuiltins()` + `Lowering.BuildBuiltinTypes()` + `NameResolver.s_builtins` + `CSharpEmitter.EmitExpr` IRApply case |
| New CLI command | `tools/Codex.Cli/Program.cs` — dispatch switch + `RunXxx` method |
| New test | `tests/Codex.Types.Tests/IntegrationTests.cs` — `CompileToCS`, `TypeCheck`, `CheckWithLinearity`, `TypeCheckWithDiagnostics` helpers available |
| Fix multiline parse | `src/Codex.Syntax/Parser.cs` — `ParseDefinition`, look at how `ParseDoExpression` handles `Indent`/`Dedent` for the pattern to follow |

---

## Repository Model (M7)

The local fact store lives at `.codex/` (gitignored). Structure:

```
.codex/
  facts/
    ab/
      abcdef...json      ← Fact JSON (hash-bucketed like git objects)
  view.json              ← name → hash map (the "current" view)
```

`codex init` creates the directory.
`codex publish <file.codex>` compiles, stores the fact, updates the view, emits a Supersession if a previous version exists.
`codex history <name>` walks the view backward through Supersession chains.
