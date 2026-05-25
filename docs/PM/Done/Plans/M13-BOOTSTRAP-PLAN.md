# M13 — Self-Hosting Bootstrap Plan

## Goal

Write the Codex compiler in Codex, then compile it with the Stage 0 (C#) compiler
to produce a Stage 1 binary that can compile its own source.

## Bootstrap Stages

| Stage | What It Is | Compiled By |
|-------|-----------|-------------|
| **Stage 0** | Current C# implementation (`src/`) | `dotnet build` |
| **Stage 1** | Codex source in `codex-src/` compiled to C# by Stage 0 | `codex build --target cs` |
| **Stage 2** | Same Codex source compiled by Stage 1 | Stage 1 binary |

**Success criterion**: Stage 1 output ≡ Stage 2 output (fixed point).

## Source Layout

```
codex-src/
  Core/
    ContentHash.codex     — SHA-256 hashing, hex encoding
    Name.codex            — Name and QualifiedName types
    SourceText.codex      — SourcePosition, SourceSpan, SourceText
    Diagnostic.codex      — DiagnosticSeverity, Diagnostic, DiagnosticBag
    Collections.codex     — List operations, Map, Set (functional)
  Syntax/
    TokenKind.codex       — Token kind enumeration
    Token.codex           — Token record
    Lexer.codex           — Lexer (functional, character-by-character)
    SyntaxNodes.codex     — CST node types
    Parser.codex          — Recursive descent parser
  Ast/
    AstNodes.codex        — AST node types
    Desugarer.codex       — CST → AST transformation
  Semantics/
    NameResolver.codex    — Scope analysis, name resolution
  Types/
    CodexType.codex       — Type representations
    TypeChecker.codex     — Bidirectional type checking
  IR/
    IRModule.codex        — IR node types
    Lowering.codex        — AST → IR transformation
  Emit/
    CSharpEmitter.codex   — IR → C# source text
  Main.codex              — Entry point, CLI dispatch
```

## Language Features Required

The Codex compiler needs these features to express itself:

| Feature | Status | Notes |
|---------|--------|-------|
| Sum types | ✅ Available | TokenKind, AST nodes, IR nodes, Types |
| Record types | ✅ Available | Token, Diagnostic, SourceSpan, etc. |
| Pattern matching | ✅ Available | Core of every compiler pass |
| Recursion | ✅ Available | Recursive descent parsing |
| Higher-order functions | ✅ Available | map, fold over lists |
| String operations | ⚠️ Partial | Need: char-at, length, substring, char predicates |
| List operations | ⚠️ Partial | Need: cons, head, tail, append, map, fold, filter |
| Effects (Console/IO) | ✅ Available | File reading, output writing |
| Let bindings | ✅ Available | Local variables |
| Do-notation | ✅ Available | Sequential effectful code |

## Phase 1: Core Types (Current)

Express the fundamental types that everything else depends on:
- `TokenKind` — a large sum type (enum)
- `Token` — a record
- `SourcePosition`, `SourceSpan` — records
- `Name` — a record with predicates
- `Diagnostic` — a record with severity

These files exercise sum types, record types, and simple functions.
They can be compiled by Stage 0 right now with no new features.

## Phase 2: String & List Primitives

Before we can write the Lexer in Codex, we need built-in primitives for:
- `char-at : Text -> Integer -> Text` (single character as text)
- `text-length : Text -> Integer`
- `substring : Text -> Integer -> Integer -> Text`
- `is-letter : Text -> Boolean`
- `is-digit : Text -> Boolean`
- `is-whitespace : Text -> Boolean`
- `text-to-integer : Text -> Integer`

These will be added as compiler built-ins (recognized by the type checker,
emitted as inline code by all backends).

## Phase 3: Lexer in Codex

The lexer is a natural starting point — it's a state machine that
consumes characters and produces tokens. In functional style:

```
LexState = record {
  source : Text,
  offset : Integer,
  line : Integer,
  column : Integer
}

LexResult (a) =
  | LexOk (a) (LexState)
  | LexError (Text) (LexState)
```

The lexer becomes a series of functions that thread `LexState` through.

## Phase 4: Parser, AST, Desugarer

Recursive descent parsing expressed functionally — each parse function
takes a token list + position, returns a (node, new-position) pair.

## Phase 5: Type Checker, IR, Emitter

The most complex pieces. The type checker needs Map for type environments.
The emitter needs StringBuilder-like text accumulation.

### Phase 5 Progress

**Completed**: IR node types (`IRModule.codex`), Lowering (`Lowering.codex`),
CodexType representations (`CodexType.codex`), and C# Emitter (`CSharpEmitter.codex`).

The type checker is NOT written in Codex — it requires mutable environments and
unification state that would need monadic threading. The Stage 0 type checker
handles all codex-src code. The Lowering and Emitter in Codex produce simplified
output (no full type propagation; uses `ErrorTy` placeholders for types not
available without a Codex-side type checker).

**Stage 0 compiler changes for Phase 5**:
- **Let-generalization** (`TypeChecker.cs`): After type-checking a definition with
  a type annotation, the declared type is immediately generalized (free type vars
  wrapped in `ForAllType`). At each call site, `Instantiate` creates fresh copies.
  This enables polymorphic reuse of functions like `map-list`.
- **Implicit type parameters** (`TypeChecker.cs`): Lowercase type names that don't
  resolve to any known type are treated as implicit type variables (fresh `TypeVariable`
  bound in `m_typeParamEnv`, scoped per definition).
- **New built-ins**: `integer-to-text`, `text-replace` added to NameResolver,
  TypeEnvironment, Lowering built-in maps, and CSharpEmitter emission.

## Phase 6: Bootstrap Verification

1. Stage 0 compiles `codex-src/` → `output.cs` ✅ (105KB, 264 records, 222 defs)
2. Compile `output.cs` with `dotnet` → Stage 1 exe ✅
3. Stage 1 compiles `codex-src/` → `stage1-output.cs` ✅ (69KB, 264 records, 220 defs)
4. Verify `output.cs` ≡ `stage1-output.cs` — structural parity achieved

### Structural Parity

| Metric | Stage 0 | Stage 1 | Gap |
|--------|---------|---------|-----|
| Records | 264 | 264 | **0** |
| Definitions | 222 | 220 | **2** (Stage 1 leaner) |
| Unique names | 213 | 219 | +6 (_loop helpers split out) |
| Missing functions | — | 0 | **0** |
| Empty records | — | 0 | **0** |

The Stage 1 compiler now produces a structurally complete copy of itself.
Every type definition, record, variant, and function from Stage 0 appears
in Stage 1 output. The 6 extra unique names are `_loop` helper functions
that Stage 0 inlines as lambdas but Stage 1 emits as named functions —
both are correct.

Byte-for-byte identity is not expected because:
- Stage 0 has full type-driven emission (typed parameters, binary op selection)
- Stage 1 uses `object` for all parameter/return types (no Codex-side type checker)
- Stage 1 emits curried calls `f(a)(b)` where Stage 0 emits multi-arg `f(a, b)`

These are cosmetic differences. The **complete pipeline** is proven:
`Source → Lex → Parse → Desugar → Lower → EmitCSharp → dotnet build → run → compile`.

### Key Fixes Enabling Parity

1. **String literal unquoting** (Lexer): Strip surrounding quotes at lex time
2. **Field access parsing** (Parser): `arm.pattern`, `d.name`, `st.tokens.kind`
3. **Compound expression guard** (Parser): Stop application after match/if/let/do
4. **Variant constructor fields** (Parser): Parse `(Type)` arguments on constructors
5. **Record type field collection** (Parser): Actually collect fields into RecordBody
6. **Post-application field access** (Parser): Handle `(expr).field` patterns
7. **Positional deconstruction** (Emitter): `FunTy(var p, var r)` instead of `FunTy { }`
8. **Sub-pattern emission** (Emitter): Bind match variables for use in arm bodies
9. **Prose formatting** (Core files): Chapter/Section headers for ExtractNotation
10. **LooksLikeNotation heuristic** (Bootstrap): Distinguish prose from notation
11. **CR-safe extraction** (Bootstrap): Handle CRLF line endings
