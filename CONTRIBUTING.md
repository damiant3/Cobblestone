# Contributing Guidelines for Codex

This file documents the coding standards, development workflow, testing requirements, and acceptance criteria that all contributors (including automated agents) must follow when working on the Codex compiler and tooling. These rules are strict and intended to preserve the style, architecture, and quality of the codebase across iterations.

---

## Purpose

Codex is a bootstrapped programming language implementation in C# (.NET 8). The goals are correctness, reproducibility, testable invariants, and deterministic builds so that future automated agents can safely extend the compiler and toolchain.

---

## General Principles

1. **Ship working software at every milestone.** A program goes in, a result comes out. If a milestone doesn't end with a demo, the milestone is wrong.
2. **Correctness over performance.** The bootstrap compiler does not need to be fast; it needs to be right. Optimization comes after correctness is proven by tests.
3. **Immutability by default.** AST nodes, IR nodes, types, and facts are immutable. Builders are mutable during construction, then frozen.
4. **No premature abstraction.** Do not create an interface until you have two implementations. Do not create a base class until three subclasses exist.
5. **One thing at a time.** Each file does one thing. Each class does one thing. Each method does one thing. Each commit does one thing.

See [docs/10-PRINCIPLES.md](docs/10-PRINCIPLES.md) for the full set of governing principles.

---

## Branching and Commits

- Main branch: `master`. All changes should be committed with clear messages.
- Use Conventional Commits style: `feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`, `perf:`.
- Single logical change per commit.

---

## Code Style

These rules must be enforced by all contributors and automated agents when generating or modifying code:

- Use 4 spaces for indentation.
- End files with a single newline.
- Maximum line length: 120 characters.
- Encoding: UTF-8. Line endings: CRLF (normalized by `.editorconfig`).
- **Private instance fields MUST use the `m_` prefix** (e.g. `m_root`, `m_diagnostics`, `m_localEnv`).
- **Private static fields MUST use the `s_` prefix** (e.g. `s_instance`, `s_cache`). Constants are exempt (PascalCase).
- Private `readonly` fields: `m_` prefix and `readonly` where appropriate.
- Property and type names: **PascalCase**. Local variables and parameters: **camelCase**.
- Constants: PascalCase.
- **Omit default accessibility modifiers.** Don't write `private` on members or `internal` on top-level types.
- **No `var`.** Always use explicit types. Agents and humans both benefit from seeing the type on each line.
- Use `new()` when the target type is on the left side (e.g. `List<int> items = new();`).
- Prefer `readonly record struct` for small value types; `sealed record` for immutable reference types.
- Prefer `Map<K,V>` / `ValueMap<K,V>` (in `Codex.Core`) over `ImmutableDictionary`.
- `Nullable` is enabled project-wide — no null surprises.
- `TreatWarningsAsErrors` is `true` in `Directory.Build.props`. Do not leave unused variables, fields, or parameters.
- `LangVersion` is `12`. `ImplicitUsings` is enabled — `System`, `System.Collections.Generic`, `System.Linq`, `System.IO`, `System.Net.Http`, `System.Threading`, and `System.Threading.Tasks` are implicit. `System.Collections.Immutable` is **NOT** implicit — add it explicitly when needed.
- One primary type per file (matching the filename). Related small types (e.g., an enum used by one class) can share a file.
- File-scoped namespace declarations. One namespace per file.
- Use xUnit for tests. Match the style of existing test files in each project.
- Pattern matching (`switch` expressions) over visitor pattern where possible.

---

## Naming Conventions by Layer

| Layer | Key Types | Naming Pattern |
|-------|-----------|----------------|
| `Codex.Core` | `SourceText`, `Span`, `Diagnostic`, `DiagnosticBag` | Core prefix-free |
| `Codex.Syntax` | `Token`, `TokenKind`, CST nodes (`DocumentNode`, `DefinitionNode`) | `*Node` for CST |
| `Codex.Ast` | `Module`, `Definition`, `Expr`, `Desugarer` | No prefix |
| `Codex.Semantics` | `NameResolver`, `ResolvedModule` | `Resolved*` for outputs |
| `Codex.Types` | `TypeChecker`, `CodexType` and subclasses | `Codex*` for types |
| `Codex.IR` | `IRModule`, `IRDefinition`, `IRExpr` subclasses, `Lowering` | `IR*` prefix |
| `Codex.Emit` | `ICodeEmitter` | `I*` for interfaces |
| `Codex.Emit.CSharp` | `CSharpEmitter` | Target name prefix |
| `Codex.Cli` | `Program` | Standard CLI layout |

---

## Comments

Do not add `///` XML doc comments. Code should be self-documenting. Only add a regular `//` comment if it is genuinely needed to avoid re-discovering a non-obvious decision.

---

## Testing Requirements

Every change must include tests. Tests must be deterministic, fast, and run headlessly.

Minimum requirements per compiler phase change:

1. **Positive tests** — programs that should succeed; verify the output is correct.
2. **Negative tests** — programs that should fail; verify the diagnostic code and message are correct.
3. **Round-trip tests** — parse → print → parse produces the same result (for syntax changes).
4. **End-to-end tests** — for pipeline changes, at minimum run `codex check` against a sample file.

Tests live in the corresponding test project under `tests/`:

| Source project | Test project |
|----------------|-------------|
| `Codex.Core` | `Codex.Core.Tests` |
| `Codex.Syntax` | `Codex.Syntax.Tests` |
| `Codex.Ast` | `Codex.Ast.Tests` |
| `Codex.Semantics` | `Codex.Semantics.Tests` |
| `Codex.Types` | `Codex.Types.Tests` |

Use xUnit. Match the style of existing test files in each project.

---

## Build and Test Commands

```sh
dotnet build Codex.sln          # Build the full solution
dotnet test Codex.sln           # Run all tests
dotnet run --project tools/Codex.Cli -- run samples/hello.codex
dotnet run --project tools/Codex.Cli -- check samples/arithmetic.codex
```

---

## Architecture Rules

- **Dependency direction is strictly one-way** (upstream → downstream):
  `Core` → `Syntax` → `Ast` → `Semantics` → `Types` → `IR` → `Emit` → `Emit.CSharp` → `Cli`
- No downstream project may reference an upstream project in the reverse direction.
- `Codex.Core` has no project dependencies — it is the root of the graph.
- `Codex.Cli` references everything; it is the composition root.

---

## Adding a New Backend

To add a new emission target (e.g., Rust, Python):

1. Create `src/Codex.Emit.<Target>/` implementing `ICodeEmitter` from `Codex.Emit`.
2. Register it in `tools/Codex.Cli/Program.cs` alongside `CSharpEmitter`.
3. Add tests for representative IR constructs.

---

## How AI Agents Should Operate

- **Always read a file before editing it.**
- **Run `dotnet build Codex.sln` and `dotnet test Codex.sln`** before concluding a task.
- **Produce minimal diffs** — do not reformat unrelated code, rename symbols, or restructure files unless that is the explicit task.
- **Follow the `m_` field prefix rule** without exception — `TreatWarningsAsErrors` is on and an unused field is a build failure.
- **Do not modify `docs/Vision/`** (`NewRepository.txt`, `IntelligenceLayer.txt`) — ever.
- **Do not modify `docs/00-OVERVIEW.md` or `docs/10-PRINCIPLES.md`** unless explicitly asked. They are the north-star specification.
- **Do not modify `Directory.Build.props`** without permission.
- **When adding a new compiler phase**, update `docs/OldStatus/ITERATION-*-HANDOFF.md` with a brief summary of what was done.
- See [`.github/copilot-instructions.md`](.github/copilot-instructions.md) for terminal and file-editing discipline rules.

---

## CI Expectations

- Build must pass: `dotnet build Codex.sln` with no warnings (warnings are errors).
- All tests must pass: `dotnet test Codex.sln`.
- No new unused fields, variables, or parameters.

---

## Quick Checklist for Changes

- [ ] `dotnet build Codex.sln` passes (zero warnings).
- [ ] `dotnet test Codex.sln` passes (all existing tests + new tests).
- [ ] Private instance fields use the `m_` prefix.
- [ ] Private static fields use the `s_` prefix (constants exempt).
- [ ] No `var` — always explicit types.
- [ ] No `///` XML doc comments.
- [ ] No dead code (unused fields, variables, parameters).
- [ ] Default accessibility modifiers omitted (`private`, `internal`).
- [ ] Temp files cleaned up (`.bak`, `.new`, `.tmp`, `.snap`).
