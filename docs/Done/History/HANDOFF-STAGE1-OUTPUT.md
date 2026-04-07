# Handoff: Bootstrap Self-Compilation — Stage 1 Runs But Output Is Broken

**Date:** Session ended after Codex.Codex successfully compiled its own source.
**Status:** Stage 1 pipeline runs end-to-end. Output is syntactically broken C#.

---

## What Was Accomplished

1. **Fixed `unify_structural` in `Codex.Codex/out/Codex.Codex.cs`** (line ~3708).
   The Stage 0 emitter only emitted the `IntegerTy` case from a nested `when` expression.
   All cases from `Unifier.codex` were added by hand: `IntegerTy`, `NumberTy`, `TextTy`,
   `BooleanTy`, `NothingTy`, `VoidTy`, `ErrorTy`, `FunTy`, `ListTy`, and wildcard fallback.

2. **`Codex.Codex` builds and runs.** `dotnet build Codex.Codex/Codex.Codex.csproj` succeeds
   (zero errors, warnings-only). Running it compiles the hardcoded test source and produces
   valid C# output.

3. **Created `tools/Codex.Bootstrap/`** — a harness project that:
   - Reads all 21 `.codex` files from `Codex.Codex/`
   - Concatenates them into a single source string
   - Calls `Codex_Codex_Codex.compile(combined, "Codex_Codex")`
   - Runs on a 256MB stack thread (parser is deeply recursive)
   - Writes output to `Codex.Codex/stage1-output.cs`

4. **Codex compiled itself.** 122,946 chars of `.codex` source → 165,617 chars of C# in
   `stage1-output.cs`. The pipeline completed without errors.

---

## What Is Wrong With the Stage 1 Output

The output in `Codex.Codex/stage1-output.cs` is **structurally broken C#**. Three categories
of problems:

### Problem 1: All record types have empty constructors

```csharp
// Stage 0 (correct):
public sealed record ARecordFieldDef(Name name, ATypeExpr type_expr);

// Stage 1 (broken):
public sealed record ARecordFieldDef();
```

**Root cause:** The self-hosted type checker does not resolve record field types or pass
type information through to the emitter for type definitions. `emit-type-def` in
`CSharpEmitter.codex` calls `emit-record-field-defs` which reads `f.type-expr` and
`f.name.value` from `ARecordFieldDef`. But the Stage 1 type checker infers everything as
unresolved type variables. Since the emitter calls `emit-type-expr-tp` which uses
`when_type_name` to convert Codex type names to C# type names, but the type info on the
*definition* records is lost because `ATypeDef` nodes are passed through from the AST
*without being lowered through the type checker*. The `emit-full-module` function receives
`ast.type-defs` directly — these are desugared AST nodes, not IR nodes. The type defs
should flow correctly *if* the desugarer preserved the field info. This means the
desugarer's output is fine in Stage 0 but the Stage 1 desugarer is losing fields.

**Investigation needed:** Check whether `desugar_type_def` in Stage 1 is actually being
called on the parsed type definitions or whether the parser is failing to parse record
field definitions from the concatenated source. The parser may be mis-parsing `record {`
blocks when they appear after `Chapter:` / `Section:` headers in the middle of a
concatenated multi-file source.

### Problem 2: Function definitions have unresolved generic type parameters

```csharp
// Stage 0 (correct):
public static string sanitize(string name) => ...

// Stage 1 (broken):
public static T2283 The<T2283>() => syntax(tree)(is)(the)(desugared)(representation)(used);
```

**Root cause:** The self-hosted type checker uses `TypeVar` (type variables with integer IDs)
for all inferred types. When the emitter sees `TypeVar(id)`, it emits `T{id}` and adds a
generic type parameter. The Stage 0 C# type checker uses `deep_resolve` to collapse all
`TypeVar`s to concrete types before emission, but the self-hosted checker's type information
is not flowing correctly. The Stage 0 `Lowering.cs` has access to `ConstructorMap` and
`TypeDefMap` which provide concrete types for sum/record constructors — the self-hosted
lowering in `Lowering.codex` receives `check_result.types` and `check_result.state` but
these don't contain the same richness.

Additionally: the `T2283 The<T2283>()` example shows that **prose text** (the descriptive
paragraphs in `.codex` files) is being parsed as if it were code definitions. The parser is
treating `"The syntax tree is the desugared representation used"` as a function call chain.
This means the **prose-mode parsing** is broken or missing in the self-hosted parser.

### Problem 3: Parse errors emitted as `/* error: ... */ default`

```csharp
public static object \n() => /* error: \n */ default;
public static T2301 IntLit<T2301>() => /* error: | */ default(NumLit);
```

**Root cause:** When the parser encounters something it can't parse, it produces `AErrorExpr`
which the emitter renders as `/* error: ... */ default`. The `|` error shows the parser
failing to parse sum type variant syntax (`| IntLit | NumLit`). This is additional evidence
that the parser is not correctly handling type definition blocks.

---

## The Three Things That Must Be Fixed (In Order)

### Fix 1: Prose text must be skipped by the parser

The `.codex` format has prose paragraphs between `Chapter:` / `Section:` headers and the
indented code. The self-hosted parser in `Parser.codex` must skip lines that are not
indented (prose) and only parse indented lines as definitions. Check `parse_document` and
`parse_top_level` — they may be feeding prose tokens into the definition parser.

**Where to look:**
- `Codex.Codex/Syntax/Parser.codex` — `parse-document`, `parse-top-level`
- `Codex.Codex/Syntax/Lexer.codex` — how `ChapterHeader`, `SectionHeader`, `ProseText`
  tokens are emitted and whether the parser consumes them
- Compare with `src/Codex.Syntax/Parser.cs` — `ParseDocument()`, `ParseSection()`

### Fix 2: Type definition parsing must handle `record { ... }` and `| Ctor` syntax

Sum types (`LiteralKind = | IntLit | NumLit ...`) and record types
(`UnificationState = record { ... }`) must be parsed correctly. The `/* error: | */`
output shows the parser is failing on the `|` pipe syntax for sum type variants.

**Where to look:**
- `Codex.Codex/Syntax/Parser.codex` — type definition parsing functions
- `Codex.Codex/out/Codex.Codex.cs` — search for `try_parse_type_def`,
  `parse_record_body`, `parse_variant_body`
- Compare with `src/Codex.Syntax/Parser.cs` — `TryParseTypeDef()`

### Fix 3: Type resolution must produce concrete types, not TypeVars

After Fixes 1 and 2, the parser will produce correct AST. Then the type checker must
resolve all `TypeVar`s to concrete types before emission. The key function is
`deep_resolve` in `Unifier.codex` — verify it is called on all types in the final IR.

**Where to look:**
- `Codex.Codex/IR/Lowering.codex` — `lower-module`, `lower-def` — check if `deep-resolve`
  is called on inferred types
- `Codex.Codex/Types/TypeChecker.codex` — `check-module`, `check-def`
- Compare with `src/Codex.IR/Lowering.cs` — how it calls `Unifier.DeepResolve()`

---

## Files You Should Read

| # | File | Why |
|---|------|-----|
| 1 | `Codex.Codex/stage1-output.cs` | The broken output — look at lines 1-120 and 3230-3268 |
| 2 | `Codex.Codex/Syntax/Parser.codex` | The self-hosted parser — find prose handling |
| 3 | `src/Codex.Syntax/Parser.cs` | The Stage 0 parser — compare prose/type-def handling |
| 4 | `Codex.Codex/Syntax/Lexer.codex` | How prose/chapter/section tokens are produced |
| 5 | `src/Codex.Syntax/Lexer.cs` | Stage 0 lexer for comparison |
| 6 | `Codex.Codex/IR/Lowering.codex` | Check if deep-resolve is applied |
| 7 | `src/Codex.IR/Lowering.cs` | Stage 0 lowering for comparison |

## Files You Should NOT Read or Modify

- `Codex.Codex/out/Codex.Codex.cs` — **DO NOT EDIT THIS FILE WITH edit_file.** It is
  3700+ lines. The edit_file tool will mangle it. If you need to fix something in this
  file, tell the user the exact line number and exact replacement text and let them do it.
- `docs/00-OVERVIEW.md` through `docs/10-PRINCIPLES.md` — architecture spec, read-only
- `src/Codex.Types/Unifier.cs` — reference only
- Any emitter other than `Codex.Emit.CSharp`

## Files You CAN Modify

- `Codex.Codex/Syntax/Parser.codex` — to fix prose handling and type def parsing
- `Codex.Codex/Syntax/Lexer.codex` — if lexer changes needed
- `Codex.Codex/IR/Lowering.codex` — to add deep-resolve calls
- `Codex.Codex/Types/TypeChecker.codex` — if type resolution needs fixing
- `tools/Codex.Bootstrap/Program.cs` — the bootstrap harness

## How to Rebuild and Test After Changes

After modifying `.codex` files, you must re-run Stage 0 to regenerate `Codex.Codex.cs`:

```
dotnet run --project tools/Codex.Cli -- build Codex.Codex
```

Then rebuild the bootstrap harness and run:

```
dotnet build tools/Codex.Bootstrap/Codex.Bootstrap.csproj
dotnet run --project tools/Codex.Bootstrap -- D:\Projects\NewRepository\Codex.Codex
```

Check `Codex.Codex/stage1-output.cs` for improvements.

---

## Known Issue: Nested `when` in Stage 0 Emitter

The Stage 0 compiler (`Codex.Cli`) has a bug where nested `when` expressions (a `when`
inside a `when` branch) only emit the first outer branch. This is why `unify_structural`
was emitted with only the `IntegerTy` case. After fixing `.codex` source files, you must
check the regenerated `Codex.Codex.cs` for the same pattern: functions with `when`
expressions that have multiple branches where each branch contains another `when`. If
the generated code only has the first branch, you need to hand-fix it (tell the user the
exact change) or fix the bug in `src/Codex.Emit.CSharp/CSharpEmitter.cs`.

## What Success Looks Like

`stage1-output.cs` should have:
- Record types with fields: `public sealed record Name(string value);`
- No generic type parameters on concrete functions: `public static string sanitize(string name)`
- No `/* error: */` nodes
- No prose text parsed as code
- The output should compile as a C# project (even if not identical to Stage 0 output)
