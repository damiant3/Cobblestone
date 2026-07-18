# 01 — System Architecture

## Design Philosophy

Codex is built as a layered system where each layer depends only on the layers below it. No circular dependencies. No god classes. Every layer has a clean public API and an internal implementation that can be replaced without affecting consumers.

We follow the compiler-as-library pattern: every phase of the compiler is a library that can be consumed independently. The IDE, the CLI, the REPL, and the test harness are all thin shells over the same libraries.

---

## Solution Structure

```
Codex.sln
│
├── src/
│   ├── Codex.Core/                  # Shared primitives: names, spans, diagnostics, content hashing
│   ├── Codex.Syntax/                # Lexer, parser, concrete syntax tree (CST)
│   ├── Codex.Ast/                   # Abstract syntax tree, desugaring from CST → AST
│   ├── Codex.Types/                 # Type system: inference, checking, dependent types, linear types, effects
│   ├── Codex.Semantics/             # Name resolution, scope analysis, semantic model
│   ├── Codex.IR/                    # Intermediate representation: typed, optimized, target-agnostic
│   ├── Codex.Proofs/                # Proof engine: verification of claims, induction, rewriting
│   ├── Codex.Repository/            # Content-addressed fact store, proposals, verdicts, views
│   ├── Codex.Emit/                  # Base emission framework + target-specific emitters
│   │   ├── Codex.Emit.CSharp/      # C# / .NET code generation
│   │   ├── Codex.Emit.Rust/        # Rust code generation
│   │   ├── Codex.Emit.JavaScript/  # JavaScript/TypeScript code generation
│   │   ├── Codex.Emit.Python/      # Python code generation
│   │   ├── Codex.Emit.Wasm/        # WebAssembly code generation
│   │   └── Codex.Emit.LLVM/        # LLVM IR code generation
│   ├── Codex.Narration/            # English-language explanation engine
│   └── Codex.Stdlib/               # Standard library written in Codex (bootstrapped)
│
├── tools/
│   ├── Codex.Cli/                   # Command-line compiler and REPL
│   ├── Codex.Lsp/                   # Language Server Protocol implementation
│   └── Codex.DevEnv/               # The unified environment (Reader/Writer/Verifier/Explorer)
│
├── tests/
│   ├── Codex.Core.Tests/
│   ├── Codex.Syntax.Tests/
│   ├── Codex.Ast.Tests/
│   ├── Codex.Types.Tests/
│   ├── Codex.Semantics.Tests/
│   ├── Codex.IR.Tests/
│   ├── Codex.Proofs.Tests/
│   ├── Codex.Repository.Tests/
│   ├── Codex.Emit.Tests/
│   └── Codex.Integration.Tests/    # End-to-end: source → compile → execute → verify
│
├── docs/                            # You are here
│   ├── 00-OVERVIEW.md
│   ├── 01-ARCHITECTURE.md          # This document
│   └── ...
│
├── samples/                         # Example Codex programs
│   ├── hello-world.codex
│   ├── account-module.codex         # The example from NewRepository.txt
│   ├── sorting-chapter.codex        # The sorting chapter from the vision
│   └── proof-reverse.codex          # The reverse-reverse proof
│
└── bootstrap/                       # Self-hosting artifacts
    ├── stage0/                      # C# compiler compiling Codex
    ├── stage1/                      # Stage0-compiled Codex compiler compiling itself
    └── stage2/                      # Stage1-compiled compiler — should match stage1 output
```

---

## Dependency Graph

```
                    ┌─────────────┐
                    │  Codex.Core  │
                    └──────┬──────┘
                           │
                    ┌──────┴──────┐
                    │ Codex.Syntax │
                    └──────┬──────┘
                           │
                    ┌──────┴──────┐
                    │  Codex.Ast   │
                    └──────┬──────┘
                           │
              ┌────────────┼────────────┐
              │            │            │
       ┌──────┴──────┐ ┌──┴───┐ ┌──────┴──────┐
       │Codex.Semantics│ │Codex │ │ Codex.Types │
       └──────┬──────┘ │.Proofs│ └──────┬──────┘
              │        └──┬───┘         │
              └────────────┼────────────┘
                           │
                    ┌──────┴──────┐
                    │   Codex.IR   │
                    └──────┬──────┘
                           │
                    ┌──────┴──────┐
                    │  Codex.Emit  │ ─── per-target backends
                    └──────┬──────┘
                           │
              ┌────────────┼────────────┐
              │            │            │
       ┌──────┴──────┐ ┌──┴────┐ ┌─────┴──────┐
       │  Codex.Cli   │ │Codex  │ │Codex.DevEnv│
       │              │ │.Lsp   │ │            │
       └─────────────┘ └───────┘ └────────────┘

       Codex.Repository is orthogonal — consumed by Cli, Lsp, DevEnv, 
       and by the Emit layer (for content addressing of compiled artifacts).
```

---

## Layer Responsibilities

### Codex.Core
- `ContentHash` — SHA-256 content addressing for all artifacts
- `Name` / `QualifiedName` — identifier representation, hyphenated-lowercase convention
- `Span` / `SourceLocation` — source position tracking for error reporting
- `Diagnostic` — errors, warnings, suggestions with rich location info
- `DiagnosticBag` — accumulator for diagnostics throughout compilation
- No dependencies beyond .NET 8 BCL.

### Codex.Syntax
- **Lexer** — hand-written, not generated. The prose-aware tokenization is too nuanced for a generator.
  - Tokens include: prose blocks, notation blocks, keywords, operators, identifiers, literals, indentation
  - The lexer must understand the Codex indentation model (prose at one level, code indented beneath it)
- **Parser** — recursive descent with Pratt parsing for expressions
  - Produces a Concrete Syntax Tree (CST) that preserves all whitespace, comments, and prose
  - The CST is the basis for formatting, the Reader view, and round-trip fidelity
- **Grammar** — defined in a formal notation in the docs, implemented in code

### Codex.Ast
- **Desugaring** — transforms CST into AST, removing syntactic sugar
- **AST nodes** — algebraic data types representing the abstract structure
  - `Chapter`, `Section`, `Definition`, `Claim`, `Proof`
  - `Expression`, `Pattern`, `TypeExpression`, `EffectRow`
  - `ProseBlock` — the English text that is load-bearing
- **Visitor / Rewriter** — standard infrastructure for AST traversal and transformation

### Codex.Types
- **Type representation** — algebraic types, dependent types, linear types, effect rows
- **Type inference** — bidirectional type checking with unification
- **Constraint solver** — solves type constraints generated during inference
- **Linearity checker** — verifies that linear values are used exactly once
- **Effect checker** — verifies that effects are properly declared and propagated
- **Dependent type evaluator** — normalizes type-level expressions (e.g., `Vector (m + n)`)

### Codex.Semantics
- **Name resolution** — binds identifiers to their definitions across chapters/sections
- **Scope analysis** — builds scope chains, detects shadowing, enforces visibility
- **Module system** — chapters as modules, sections as sub-modules, import/export
- **Semantic model** — the queryable model of a fully analyzed program (consumed by LSP, Narrator)

### Codex.IR
- **IR nodes** — a typed, effect-annotated, linearity-annotated intermediate representation
- **Lowering** — AST + types → IR
- **Optimization passes** — dead code elimination, inlining, constant folding, effect erasure for pure contexts
- **Monomorphization** — specializes generic definitions for specific types (needed for some backends)

### Codex.Proofs
- **Proof terms** — representation of proofs (induction, rewriting, case analysis)
- **Proof checker** — verifies that a proof term inhabits its claimed type
- **Tactics** — automated proof search strategies (simple cases only in bootstrap)
- **Integration with types** — proof obligations generated by dependent type constraints

### Codex.Repository
- **Fact store** — append-only, content-addressed storage
- **Fact types** — Definition, Proposal, Verdict, Test, Benchmark, Discussion
- **Views** — consistent selections of facts, branching without branches
- **Trust lattice** — vouching, proof records, test records per definition
- **Query engine** — search by type signature, capability, proof coverage
- **Serialization** — facts stored as content-addressed blobs with metadata

### Codex.Emit
- **Emitter interface** — each backend implements a common interface
- **Capability model** — each backend declares what language features it supports
- **Fallback strategy** — when a backend can't represent a feature, insert runtime check or reject
- **Per-backend projects** — isolated, independently testable

### Codex.Narration
- **Explanation engine** — given a semantic model node, produce English explanation
- **This is NOT an LLM** — it is a structured template system that reads the prose already in the source and composes it with type information, proof records, and history
- **Used by** — the DevEnv (Narrator panel), the LSP (hover documentation), the CLI (explain command)

---

## Cross-Cutting Concerns

### Cancellation & Progress
All long-running operations accept `CancellationToken` and report progress via `IProgress<T>`. The compiler pipeline, proof checker, and repository queries are all cancellable.

### Immutability
All AST, IR, type, and fact representations are immutable. We use `record` types and `ImmutableArray<T>` pervasively. Mutation happens only in builders during construction.

### Diagnostics
Every phase produces diagnostics. Diagnostics are accumulated in a `DiagnosticBag` and surfaced uniformly. Diagnostics are never thrown as exceptions — they are values.

### Testing Strategy
- Unit tests per project, testing each phase in isolation
- Integration tests that feed source through the full pipeline
- Snapshot tests for parser output (CST), type checker output, IR output, emitted code
- Property-based tests for the type system (if it type-checks, it should not crash at runtime)
- The samples/ directory contains canonical test programs

---

## Technology Choices

| Concern | Choice | Rationale |
|---------|--------|-----------|
| Language | C# 12 / .NET 8 | The bootstrap language. Strong type system, good performance, excellent tooling. |
| Testing | xUnit + FluentAssertions | Standard .NET testing stack |
| Property testing | FsCheck (via FsCheck.Xunit) | For type system property tests |
| Serialization | System.Text.Json + MessagePack | JSON for human-readable, MessagePack for repository blobs |
| Hashing | SHA-256 via System.Security.Cryptography | Content addressing |
| Collections | System.Collections.Immutable | Immutable data structures throughout |
| CLI | System.CommandLine | Modern .NET CLI framework |
| LSP | OmniSharp LSP libraries | Language Server Protocol |

No external dependencies are added unless they solve a problem that would take more than a week to solve ourselves. We are building a language — we control our dependencies.
