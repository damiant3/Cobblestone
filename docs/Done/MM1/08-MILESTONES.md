# 08 — Milestones

## Philosophy

We build in vertical slices, not horizontal layers. Each milestone produces a working system that can do something real, even if that something is small. No milestone is "implement the type system" — instead it's "compile and run a program that uses type X."

Every milestone ends with a demo: a Codex program that exercises the new capability, compiled and executed.

Direction and priorities live in [FORWARD-PLAN.md](FORWARD-PLAN.md).
Design choices live in [DECISIONS.md](DECISIONS.md).

---

## Milestone 0: Foundation ✅
**Goal**: Project structure, build system, core primitives. Nothing compiles yet, but everything builds.

### Deliverables
- [x] Solution restructured into multi-project layout (see Architecture doc)
- [x] `Codex.Core`: `ContentHash`, `Name`, `Span`, `SourceLocation`, `Diagnostic`, `DiagnosticBag`
- [x] `Codex.Syntax`: Token types, `TokenKind` enum, `Span` on tokens
- [x] `Codex.Ast`: AST node types (empty implementations, just the shape)
- [x] `Codex.Core.Tests`: Tests for content hashing, name handling
- [x] Build passes. All tests pass.
- [x] `docs/` directory with all planning documents

### Demo
`dotnet build` succeeds. `dotnet test` passes. The project structure matches the architecture doc.

---

## Milestone 1: Hello Notation ✅
**Goal**: Lex and parse a minimal Codex program (notation only, no prose). Produce a CST.

### Deliverables
- [x] Lexer: tokenizes identifiers, operators, literals, keywords, indentation
- [x] Parser: parses simple expressions, let bindings, function definitions, type annotations
- [x] CST: concrete syntax tree with full trivia
- [x] AST: desugared abstract syntax tree
- [x] Pretty printer: CST → formatted source text (round-trip test)
- [x] `Codex.Syntax.Tests`: lexer and parser tests for each construct
- [x] `Codex.Ast.Tests`: desugaring tests

### Demo Program
```
square : Integer → Integer
square (x) = x * x

main : Integer
main = square 5
```

Parse it. Print the CST. Print the AST. No type checking, no execution.

---

## Milestone 2: Type Checking (Simple) ✅
**Goal**: Type-check programs with primitive types, functions, and simple algebraic types.

### Deliverables
- [x] `Codex.Semantics`: name resolution, scope analysis
- [x] `Codex.Types`: bidirectional type checker for simple types
- [x] Type inference for let bindings and function definitions
- [x] Type error diagnostics with source locations
- [x] Sum type definitions and pattern matching (exhaustiveness check)
- [x] Record type definitions and field access
- [x] `Codex.Types.Tests`: type checking tests — both success and failure cases

### Demo Program
```
Result (a) =
  | Success (a)
  | Failure Text

safe-divide : Integer → Integer → Result Integer
safe-divide (x) (y) =
  if y == 0
    then Failure "division by zero"
    else Success (x / y)

describe : Result Integer → Text
describe (result) =
  when result
    if Success (n) → "got " ++ show n
    if Failure (msg) → "error: " ++ msg
```

Type-check it. Report any errors. No execution yet.

---

## Milestone 3: Execution via C# ✅
**Goal**: Compile a type-checked Codex program to C# and run it.

### Deliverables
- [x] `Codex.IR`: IR definition, lowering from elaborated AST → IR
- [x] `Codex.Emit.CSharp`: C# code emitter
- [x] Codex runtime library for C# (Unit, Maybe, Result, CodexList)
- [x] `Codex.Cli`: `codex check`, `codex build`, `codex run` commands
- [x] Integration tests (source → compile → run → verify output)

### Key Decisions
- IR is not A-Normal Form — expressions nest freely. See [DECISIONS.md](DECISIONS.md).
- Curried application: `IRApply(IRApply(f, a), b)`. See [DECISIONS.md](DECISIONS.md).
- No separate runtime library — built-ins emitted inline. See [DECISIONS.md](DECISIONS.md).
- `long` for Integer, `double` for Number. See [DECISIONS.md](DECISIONS.md).

### Demo
```
codex run hello.codex
> 25
```

---

## Milestone 4: Prose Integration ⚠️
**Goal**: Parse and process Codex source that includes prose. The literate programming model works.

### Deliverables
- [x] Lexer: prose mode / notation mode switching
- [x] Parser: chapter headers, section headers, prose blocks
- [x] Prose template matching: "An X is a record containing:", "X is either:"
- [x] Templates produce `RecordTypeBody`/`VariantTypeBody` from bullet lists
- [x] The account module example from `NewRepository.txt` parses and type-checks
- [ ] **The Reader**: `codex read <file>` renders formatted prose to terminal (`Codex.Narration`)

### Status
Chapter/Section parsing, prose-aware compilation, and template matching all work end-to-end.
Templates desugar through the full pipeline (7 tests in `ProseTemplateTests`).
The Reader and `Codex.Narration` are deferred — the project exists but is empty.

### Demo Program
```codex
Chapter: Greeting

  This module provides a simple greeting function.

  An Account is a record containing:
  - owner : Text
  - balance : Integer

  We say:

    greet : Text → Text
    greet (name) = "Hello, " ++ name ++ "!"

    main : Text
    main = greet "World"
```

---

## Milestone 5: Effects ⚠️
**Goal**: The effect system works. Pure functions are enforced. Effectful functions declare their effects.

### Deliverables
- [x] Effect row types in `Codex.Types`
- [x] Effect checking: pure functions cannot call effectful functions
- [x] Effect polymorphism: row variables (`[e]`), `map` propagates effects
- [x] Built-in effects: `Console` (read/write console), `State`
- [x] C# backend: effects encoded as contexts/interfaces
- [x] **`run-state` effect handler**: `get-state`, `set-state`, `run-state` built-ins; type checker allows effects inside handler scope and eliminates them from return type; C# emitter emits mutable-cell closure; `do` blocks as handler arguments; 12 tests.
- [ ] **User-defined effects**: general `handle` syntax, custom effect declarations

### Key Decisions
- Direct I/O for effects, no monadic encoding. See [DECISIONS.md](DECISIONS.md): "Direct I/O for Effects."
- `run-state` as built-in with mutable-cell C# emission. See [DECISIONS.md](DECISIONS.md): "run-state Effect Handler."

### Status
Effect types, effect checking, effect polymorphism (row variables), Console/State effects, C# emission,
and the `run-state` effect handler all work. User-defined effect handlers and custom effect declarations deferred.

---

## Milestone 6: Linear Types ⚠️
**Goal**: Linear types enforce resource safety.

### Deliverables
- [x] Linearity annotations in `Codex.Types`
- [x] `FileHandle` as a linear type
- [x] File system effect + linear file handles
- [x] C# backend: linear types encoded as runtime checks
- [x] **Linearity checker** (`LinearityChecker.cs`) — usage-counting pass with CDX2040 (never used), CDX2041 (used more than once), CDX2042 (inconsistent across branches)
- [x] Let-forward tracking (rebinding a linear variable transfers linearity)
- [x] If/match branch merging (linear variable must be used consistently in all branches)
- [x] Wired into CLI compile pipeline and `Helpers.CheckWithLinearity` test harness
- [x] 6 tests in `IntegrationTests.Linearity.cs`
- [ ] **Advanced linearity**: linear values through record fields, linear closures

### Status
The linearity checker is implemented and wired into the pipeline. It rejects programs that
use a linear value twice (`CDX2041`), not at all (`CDX2040`), or inconsistently across
branches (`CDX2042`). Let-forwarding, lambda scoping, and do-expression scoping are handled.
Remaining work: tracking linearity through record field access and closures that capture linear values.

### Demo Program
```codex
Chapter: Safe File Reading

    read-entire-file : Path → [FileSystem] Result Text
    read-entire-file (path) = do
      handle ← open-file path
      contents ← read-all handle
      close-file handle
      succeed contents
```

If you forget `close-file`, the compiler rejects the program with CDX2040.

---

## Milestone 7: Repository (Local) ⚠️
**Goal**: The local fact store works. Definitions are content-addressed. Imports resolve from the store.

### Deliverables
- [x] `Codex.Repository`: local content-addressed store
- [x] Facts: Definition, Supersession, Proposal, Verdict, Trust
- [x] CLI: `codex init`, `codex publish`, `codex history`
- [x] Proposals + verdicts with consensus checking
- [x] Repository sync between stores
- [x] `import TypeName` resolves from the fact store via `IModuleLoader`
- [x] `RepositoryModuleLoader` in `Codex.Cli` wires fact store to name resolver
- [ ] **Views**: single-user views, view consistency checking

### Status
The repository, content hashing, fact storage, CLI commands, and import-from-repository all work.
`IModuleLoader` interface in `Codex.Semantics`, `RepositoryModuleLoader` in `Codex.Cli`
(per the architecture: Repository is orthogonal, consumed by Cli/Lsp/DevEnv).
Views are not yet integrated. See [05-REPOSITORY-MODEL.md](05-REPOSITORY-MODEL.md) for the design.

---

## Milestone 8: Dependent Types (Basic) ⚠️
**Goal**: Types can depend on values. Vector with length. Proof obligations generated.

### Deliverables
- [x] Dependent function types: `(n : Integer) → Vector n a → ...`
- [x] Type-level arithmetic: `m + n` evaluated during type checking
- [x] Proof obligations: `index` requires proof that index < length
- [x] Simple proof discharge: literal evidence and context-based evidence
- [ ] **The `Vector` type** with `append` having the correct dependent type, end-to-end

### Status
Dependent function types, type-level arithmetic, and proof obligations all work in the type checker.
Full dependent `Vector` example not yet end-to-end.

### Demo Program
```codex
Chapter: Vectors

    append : Vector m a → Vector n a → Vector (m + n) a
    
    example : Vector 5 Integer
    example = append [1, 2, 3] [4, 5]
```

Type-checks and compiles. The compiler verifies that 3 + 2 = 5.

---

## Milestone 9: LSP & Editor Integration ✅
**Goal**: Write Codex in VS Code with syntax highlighting, error reporting, and hover.

### Deliverables
- [x] `Codex.Lsp`: Language Server Protocol implementation
- [x] Diagnostics (errors/warnings pushed to editor)
- [x] Completion (definitions, constructors, type names, builtins, keywords)
- [x] Hover (type info for functions, type definitions, constructors)
- [x] Go to definition (functions, type definitions, constructors)
- [x] Document symbols (outline)
- [x] Semantic tokens (syntax highlighting)
- [x] VS Code extension (thin wrapper)

### Status
All major LSP features implemented. 18 tests in `Codex.Lsp.Tests`.

---

## Milestone 10: Proofs ⚠️
**Goal**: Users can write and verify proofs. The compiler checks them.

### Deliverables
- [x] `Codex.Proofs`: proof terms, proof checker
- [x] Proof by induction (with inductive hypothesis registration)
- [x] Proof by case analysis
- [x] Proof by rewriting (sym, trans, cong)
- [x] Claims and proofs in the source
- [x] Cong with bidirectional goal decomposition
- [x] Lemma application with argument instantiation
- [x] The reverse-reverse proof works (with `assume` for irreducible steps)
- [ ] **Type-level function reduction** (needed for non-trivial inductive steps)
- [ ] **Arithmetic induction** with Peano encoding

### Key Decisions
- IH registration: `__ih_{variable}` or claim name. See [DECISIONS.md](DECISIONS.md): "Inductive Hypothesis Registration."
- Cong goal decomposition: bidirectional. See [DECISIONS.md](DECISIONS.md): "Cong Goal Decomposition."

### Status
Refl, sym, trans, cong (bidirectional), induction with IH, lemma application all work.
The main gap: inductive steps that require unfolding function definitions use `assume`.
Sample: `samples/proofs.codex` (9 claims, 9 proofs, 0 errors).

---

## Milestone 11: Tests ⚠️
**Goal**: Comprehensive automation of test cases.

### Deliverables
- [x] Property-based testing framework
- [x] Integration test cases for each milestone (end-to-end)
- [x] 628 tests across 7 test projects (all passing)
- [x] **Corpus emission tests** — 15 samples × 11 backends = 165 theory tests + 1 full emission test
- [x] **Generated output checked in** — `generated-output/` with 11 language subdirectories, 165 files
- [ ] **Fuzz testing** for robustness
- [ ] **CI configuration** for automated testing

### Current Test Breakdown

| Project | Tests |
|---------|-------|
| `Codex.Core.Tests` | 16 |
| `Codex.Syntax.Tests` | 88 |
| `Codex.Ast.Tests` | 11 |
| `Codex.Semantics.Tests` | 15 |
| `Codex.Types.Tests` | 457 |
| `Codex.Lsp.Tests` | 18 |
| `Codex.Repository.Tests` | 23 |
| **Total** | **628** |

### Status
Integration tests cover all milestones. Corpus emission tests compile every sample through every
backend and write the output to `generated-output/` (checked in for git history). Fuzz testing and CI deferred.
See [DECISIONS.md](DECISIONS.md) re: CI pipeline — deferred until there are users or funding.

---

## Milestone 12: Additional Backends ✅
**Goal**: Codex compiles to JavaScript, Rust, and beyond.

### Deliverables
- [x] `Codex.Emit.JavaScript`: JavaScript emitter (BigInt integers, console.log, TCO)
- [x] `Codex.Emit.Rust`: Rust emitter (enum variants, `codex_main`, TCO)
- [x] `Codex.Emit.Python`: Python emitter (@dataclass, TCO)
- [x] `Codex.Emit.Cpp`: C++ emitter (std::variant, TCO). Verified MSVC /std:c++17 (14/15 samples)
- [x] `Codex.Emit.Go`: Go emitter (interface{}, TCO)
- [x] `Codex.Emit.Java`: Java emitter (sealed interface + record, TCO)
- [x] `Codex.Emit.Ada`: Ada emitter (discriminant records, TCO)
- [x] `Codex.Emit.Babbage`: Babbage Analytical Engine emitter (Store columns, TCO)
- [x] `Codex.Emit.Fortran`: Fortran emitter (tagged structs, `do while/.true./cycle` TCO)
- [x] `Codex.Emit.Cobol`: COBOL emitter (`PIC 9(2)` tags, `GO TO` TCO)
- [x] Integration tests: 15 samples × 11 backends = 165 tests
- [x] TCO in all 11 backends

### Key Decisions
- `codex_main` in Rust. See [DECISIONS.md](DECISIONS.md): "Codex Main Renamed."
- TCO via loop conversion in all backends. See [DECISIONS.md](DECISIONS.md): "Tail Call Optimization."
- Variant syntax without leading pipe. See [DECISIONS.md](DECISIONS.md): "Variant Type Syntax."

### Status
11 backends total. All 15 samples (including proof-only modules) emit and compile across all backends.

---

## Milestone 13: Self-Hosting ✅
**Goal**: The Codex compiler is written in Codex and compiles itself.

### Deliverables
- [x] Codex compiler source in Codex (lexer, parser, desugarer, lowering, emitter)
- [x] Stage 0 (C# compiler) compiles Stage 1 (Codex compiler)
- [x] **Codex-side type checker** (Unifier, TypeEnvironment, TypeChecker)
- [x] **Codex-side name resolver** (scope tracking, duplicate detection, built-ins)
- [x] **Full pipeline in Codex** — `compile-checked` chains lex → parse → desugar → resolve → typecheck → lower → emit
- [x] **C# generics support** — polymorphic functions emit as generic methods
- [x] **Stage 1 compiles and runs** — `output.cs` compiles as a standalone .NET 8 program with zero errors
- [x] **Stage 1 produces output** — the compiled Codex compiler successfully compiles a test Codex program
- [ ] Stage 1 output = Stage 2 output (full bootstrap fixed-point verification)

### Key Decisions
- C# generics replace `object` erasure. See [DECISIONS.md](DECISIONS.md): "C# Generics for Polymorphic Functions." (Supersedes the earlier "TypeVariable Emits as `object`" decision.)
- Threaded UnificationState for purely functional unifier. See [DECISIONS.md](DECISIONS.md): "Codex-Side Type Checker."
- Error-collecting scope walk for name resolution. See [DECISIONS.md](DECISIONS.md): "Codex-Side Name Resolver."

### Status
Stage 2 proof achieved. The compilation chain works:
```
Codex source → Stage 0 (C# compiler) → output.cs → dotnet build → Stage 1 binary → compiles Codex → C# output
```

Codex source: 21 files, ~2,600 lines. `output.cs`: ~286 records, ~310 functions. Zero C# errors.
Stage 1 emitter now outputs C# generics identical to Stage 0 (`map_list<T0, T1>`, `fold_list<T4, T5>`, etc.).
Only 4 `object` references remain in Stage 1 output (all correct: `NothingTy`/effectful returns).
Remaining gap to full fixed-point: Stage 1 type checker must resolve concrete types for the test program
(currently infers `object` for `square : Integer -> Integer`).

---

## Post-Milestone Work (Completed)

These were done after the original M0–M13 plan, not covered by numbered milestones.

### IDE / Syntax Highlighting ✅
- [x] TextMate grammar (`codex.tmLanguage.json`) for VS 2022 + VS Code
- [x] Keywords, proof keywords, type identifiers, strings, numbers, operators (including Unicode)
- [x] Effect brackets `[Console]`, annotations `@name`, prose headers `Chapter:` / `Section:`
- [x] Language configuration: bracket matching, auto-close, smart indentation
- [x] VS 2022 support: `.pkgdef` registration, `install-vs.ps1`, `build-vsix.ps1`
- [x] Project file: `codex.project.json` with schema validation, sources glob, targets, output
- [x] `codex init` creates project scaffold

### Incremental / Parallel Builds ✅
- [x] `--incremental` flag skips unchanged files (SHA256 content hash + timestamp)
- [x] Build manifest stored in `.codex-build/manifest.json`
- [x] Parallel front-end: `Parallel.ForEach` over source files (lex + parse + desugar)
- [x] Sequential middle: name resolution → type check → linearity → proofs → lowering
- [x] Parallel emission: `--targets cs,js,py,...` emits all backends concurrently
- [x] `codex.project.json` `targets` array for declarative multi-target builds
