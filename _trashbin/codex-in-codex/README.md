# Codex in Codex

This directory contains the **Codex compiler written in Codex** — the self-hosting source.
Every file is literate prose with Chapter/Section structure. When compiled by Stage 0
(the C# compiler), it produces `output.cs`, which is a standalone .NET 8 program that
can itself compile Codex source code.

## Structure

```
codex-in-codex/
├── Main.codex                  Entry point: compile pipeline, test harness
├── Core/
│   ├── Collections.codex       List utilities (map, filter, fold, etc.)
│   ├── Diagnostic.codex        Error/warning diagnostics
│   ├── Name.codex              Qualified names
│   └── SourceText.codex        Source text + spans
├── Syntax/
│   ├── TokenKind.codex         Token kind enumeration
│   ├── Token.codex             Token record
│   ├── Lexer.codex             Tokenizer (hand-written, state-threaded)
│   ├── Parser.codex            Recursive descent parser → CST
│   └── SyntaxNodes.codex       CST node definitions
├── Ast/
│   ├── AstNodes.codex          AST node definitions
│   └── Desugarer.codex         CST → AST desugaring
├── Semantics/
│   └── NameResolver.codex      Scope analysis, name validation
├── Types/
│   ├── CodexType.codex         Type definitions (primitives, functions, sums)
│   ├── TypeEnv.codex           Type environment (scoped type maps)
│   ├── Unifier.codex           Type unification (threaded UnificationState)
│   └── TypeChecker.codex       Bidirectional type checker
├── IR/
│   ├── IRModule.codex          IR node definitions
│   └── Lowering.codex          AST → IR lowering
└── Emit/
    └── CSharpEmitter.codex     IR → C# code generation
```

## Stats

| Metric | Value |
|--------|-------|
| Files | 21 |
| Lines | ~2,600 |
| Records | ~286 (in output.cs) |
| Functions | ~310 (in output.cs) |

## The Bootstrap Chain

```
codex-in-codex/*.codex
    → Stage 0 (C# compiler, in src/)
    → codex-src/output.cs
    → dotnet build (tools/Codex.Bootstrap/)
    → Stage 1 binary
    → compiles Codex source → valid C# output
```

## How to Compile

```sh
# Stage 0 compiles the Codex source to C#:
dotnet run --project tools/Codex.Cli -- build codex-in-codex/Main.codex

# Stage 1 (the generated compiler) compiles a test program:
dotnet run --project tools/Codex.Bootstrap
```

## Design Principles

Every file is **prose-first**: chapters and sections frame the code in natural language.
The compiler is purely functional — no mutable state. The `UnificationState` record is
threaded through all type-checking functions (see Types/Unifier.codex). See
[DECISIONS.md](../docs/DECISIONS.md) for the design rationale.
