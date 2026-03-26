# Codex

**A programming language that compiles itself, reads like a book, and targets fifteen backends — including bare metal.**

Codex is a statically typed, purely functional language where source files are
literate documents — prose explains intent, indented notation is executable.
The compiler is written in Codex. It compiles itself. It has its own character
encoding. It runs on bare metal with no OS, no libc, no runtime.

```codex
Chapter: Greeting

  A module that greets people by name.

Section: Functions

    greet : Text -> Text
    greet (name) = "Hello, " ++ name ++ "!"

    main : Text
    main = greet "World"
```

> **March 26, 2026 — CCE-Native Text Complete.**
> The Codex compiler uses its own character encoding (CCE) natively — Char is
> a CCE byte, Text is a CCE string, Unicode only at I/O boundaries. Fixed point
> proven at 298,752 chars. 926 tests pass. 15 backends. Self-compile in 208ms.
> Four agents shipped 111 commits in a single day.

---

## Why

Most compilers are written in languages that look nothing like what they compile.
Codex is the language, the compiler, and the document — all the same thing.

- **Literate by design.** Chapters and Sections aren't comments. They're structure.
  The compiler parses prose alongside code.
- **Self-hosting.** The compiler compiles itself. The bootstrap is proven:
  Stage 1 = Stage 3 = fixed point at 298,752 chars.
- **Own character encoding.** CCE (Codex Character Encoding) is frequency-sorted:
  `is-letter` is a single comparison, not a table lookup. Unicode only at I/O
  boundaries. The typewriter is dead.
- **Fifteen backends.** C#, JavaScript, Python, Rust, C++, Go, Java, Ada, Fortran,
  COBOL, .NET IL, RISC-V, ARM64, x86-64, and WebAssembly. Four of those generate
  native machine code — no toolchain, no runtime, no libc.
- **Bare metal.** Codex.OS boots from a floppy image, runs a preemptive multitasking
  kernel in 7KB, and compiles Codex programs on bare hardware.
- **Algebraic effects.** Side effects are declared in types and handled explicitly.
  No monads. No surprise mutations.
- **Content-addressed repository.** Code is facts. Facts are immutable, hashed,
  and append-only. No more "merge conflict."

---

## Quick Start

**Prerequisites**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```sh
# Build everything
dotnet build Codex.sln

# Run all 926 tests
dotnet test Codex.sln

# Compile and run a program
dotnet run --project tools/Codex.Cli -- run samples/hello.codex

# Compile to multiple targets
dotnet run --project tools/Codex.Cli -- build samples/hello.codex --targets cs,js,rust

# Build a multi-file project
dotnet run --project tools/Codex.Cli -- build samples/word-freq/

# Bootstrap: compile the compiler with itself
dotnet run --project tools/Codex.Bootstrap

# Convert between Unicode and CCE
dotnet run --project tools/Codex.Cli -- encode -f cce -t utf8 file.cce
```

---

## Language Features

```codex
-- Sum types (algebraic data types)
Shape =
  | Circle (Number)
  | Rectangle (Number) (Number)

-- Record types
Person = record {
  name : Text,
  age : Integer
}

-- Pattern matching
area : Shape -> Number
area (s) = when s
  if Circle (r) -> 3.14 * r * r
  if Rectangle (w) (h) -> w * h

-- Polymorphism
identity : a -> a
identity (x) = x

-- Let bindings
hypotenuse : Number -> Number -> Number
hypotenuse (a) (b) =
  let a2 = a * a
  in let b2 = b * b
    in a2 + b2

-- Effects and do-notation
main : [Console] Nothing
main = do
  print-line "What is your name?"
  let name = read-line ()
  print-line ("Hello, " ++ name ++ "!")
```

---

## Compilation Pipeline

```
Source (.codex)
    → Lexer         token stream
    → Parser        concrete syntax tree
    → Desugarer     abstract syntax tree
    → NameResolver  resolved names
    → TypeChecker   bidirectional type inference
    → Lowering      typed intermediate representation
    → Emitter       target source code / machine code
```

The entire pipeline exists twice: once in C# (the locked reference implementation)
and once in Codex (the self-hosted compiler, 26 files, ~5,000 lines). The Codex
version is the one that matters now.

---

## Fifteen Backends

| Backend | Target | Status |
|---------|--------|--------|
| C# | `--targets cs` | Primary. Full pipeline. CCE-native. |
| JavaScript | `--targets js` | Full |
| Python | `--targets python` | Full |
| Rust | `--targets rust` | Full |
| C++ | `--targets cpp` | Full |
| Go | `--targets go` | Full |
| Java | `--targets java` | Full |
| Ada | `--targets ada` | Full |
| Fortran | `--targets fortran` | Full |
| COBOL | `--targets cobol` | Full |
| .NET IL | `--targets il` | Records, sums, pattern matching, builtins |
| **RISC-V** | `--targets riscv` | **Native machine code. Bare metal.** |
| **ARM64** | `--targets arm64` | **Native machine code. Bare metal.** |
| **x86-64** | `--targets x86-64` | **Native machine code. Bare metal. Codex.OS.** |
| **WebAssembly** | `--targets wasm` | **Binary .wasm modules** |

Native backends include: region-based memory, escape analysis, tail-call
optimization, and deep escape copy for heap values crossing region boundaries.

---

## CCE — Codex Character Encoding

Codex has its own 128-byte character encoding, frequency-sorted for computation:

| Range | Category | Count |
|-------|----------|-------|
| 0-2 | Whitespace (NUL, LF, Space) | 3 |
| 3-12 | Digits | 10 |
| 13-38 | Lowercase (frequency-sorted) | 26 |
| 39-64 | Uppercase | 26 |
| 65-93 | Punctuation (prose + operators + syntax) | 29 |
| 94-112 | Accented Latin | 19 |
| 113-127 | Cyrillic | 15 |

Character classification is arithmetic, not table lookup:
- `is-letter(b)` = `b >= 13 && b <= 64`
- `is-digit(b)` = `b >= 3 && b <= 12`
- `to-lower(b)` = `b - 26` (if uppercase)

Unicode exists only at I/O boundaries. Internally, everything is CCE.

---

## Standard Library (Prelude)

11 modules, ~1,200 lines of Codex:

| Module | What it does |
|--------|-------------|
| `Maybe` | Option type — `Just a` or `Nothing` |
| `Result` | Error handling — `Ok a` or `Err e` |
| `Either` | Sum of two types |
| `Pair` | Product type |
| `List` | Functional list operations |
| `Hamt` | Hash Array Mapped Trie — persistent map |
| `Set` | Persistent set |
| `Queue` | Functional queue |
| `StringBuilder` | Efficient string building |
| `CCE` | Character encoding — classification, conversion, roundtrip |
| `TextSearch` | Text search utilities |

---

## Self-Hosting Bootstrap

The compiler compiles itself. Here's how:

```
.codex source ──→ [Stage 0: C# reference compiler] ──→ Codex.Codex.cs
Codex.Codex.cs ──→ [dotnet build]                   ──→ Stage 1 binary
Stage 1 binary ──→ [compiles .codex source]          ──→ stage1-output.cs

Stage 1 binary ──→ [rebuild from stage1-output.cs]   ──→ Stage 2 binary
Stage 2 binary ──→ [compiles .codex source]          ──→ stage3-output.cs

stage1-output.cs == stage3-output.cs → Fixed point. 298,752 chars.
```

Self-compile time: **208ms median** (26 files, ~180K chars, full pipeline).

```sh
# Run the bootstrap
dotnet run --project tools/Codex.Bootstrap

# Benchmark (3 warmup + 10 measured, median)
dotnet run --project tools/Codex.Bootstrap -- --bench
```

---

## Codex.OS

A bare-metal operating system written for the Codex compiler.

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32→64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done |
| 2 | Process table (16 slots), preemptive context switch, page tables | Done |
| 3 | Capability-enforced syscalls | Done |
| 4 | Self-hosting compiler on bare metal | In progress |

7KB kernel. Boots from floppy. Compiles Codex programs over a serial REPL.

---

## Project Structure

```
Codex.sln
├── src/                         Reference compiler (C#, locked)
│   ├── Codex.Core               Diagnostics, SourceText, CceTable, Map<K,V>
│   ├── Codex.Syntax             Lexer, Parser, ProseParser, CST
│   ├── Codex.Ast                Desugarer, AST nodes
│   ├── Codex.Semantics          Name resolution, scope analysis
│   ├── Codex.Types              Type checker, unifier, linearity checker
│   ├── Codex.IR                 IR nodes, lowering, regions
│   ├── Codex.Emit.*             15 backend emitters
│   ├── Codex.Lsp                Language Server Protocol
│   ├── Codex.Repository         Content-addressed fact store
│   ├── Codex.Narration          Prose rendering
│   └── Codex.Proofs             Proof terms and verification
├── Codex.Codex/                 Self-hosted compiler (26 .codex files)
├── prelude/                     Standard library (11 modules)
├── tests/                       926 tests across 8 projects
├── tools/
│   ├── Codex.Cli                Command-line interface
│   ├── Codex.Bootstrap          Bootstrap harness + benchmark
│   └── Codex.VsExtension        Visual Studio extension
├── editors/vscode/              VS Code extension (syntax + LSP)
├── samples/                     24 example programs + 2 multi-file projects
├── generated-output/            Backend output corpus
└── docs/
    ├── 00-OVERVIEW.md           Project overview
    ├── 10-PRINCIPLES.md         Engineering principles
    ├── CurrentPlan.md           Active plan
    ├── Designs/                 Feature design documents
    └── Vision/                  Original vision documents
```

---

## CLI

```
codex run       <file.codex>              Compile and execute
codex build     <file.codex|dir>          Compile (multi-target, incremental)
codex check     <file.codex>              Type-check only
codex parse     <file.codex>              Print tokens / CST / AST
codex encode    [file]                    Convert between Unicode and CCE
codex add       <package> [--version v]   Add a dependency
codex remove    <package>                 Remove a dependency
codex pack      <dir>                     Package a library
codex packages                            List installed packages
codex version                             Print version
```

---

## Editor Support

**VS Code** — install from `editors/vscode/`:

```sh
cd editors/vscode
npm install
npx vsce package && code --install-extension codex-lang-0.1.0.vsix
```

Syntax highlighting, bracket matching, auto-indentation, and LSP integration
(diagnostics, hover, go-to-definition).

**Visual Studio** — extension project at `tools/Codex.VsExtension/`.

---

## Samples

| File | What it demonstrates |
|------|---------------------|
| `hello.codex` | `square 5` → `25` |
| `factorial.codex` | Recursion: `factorial 10` → `3628800` |
| `fibonacci.codex` | TCO: `fib 20` → `6765` |
| `shapes.codex` | Sum types + pattern matching |
| `person.codex` | Records + field access |
| `effects-demo.codex` | Algebraic effects |
| `expr-calculator.codex` | Quine disproof — the program that proved self-hosting |
| `hamt-test.codex` | Persistent hash maps |
| `is-prime-fancy.codex` | Higher-order functions |
| `state-demo.codex` | Stateful effects |
| `word-freq/` | Multi-file project: word frequency counter |
| `mini-bootstrap.codex` | All-features smoke test |

---

## Milestone History

| # | Milestone | What happened |
|---|-----------|--------------|
| M0 | Foundation | Solution structure, diagnostics, SourceText |
| M1 | Notation | Lexer + parser for literate syntax |
| M2 | Types | Bidirectional type checker with unification |
| M3 | Execution | C# backend — programs run |
| M4–M7 | Type system | Polymorphism, effects, dependent types, proofs |
| M8–M10 | Infrastructure | LSP, repository, 11 more backends |
| M11–M12 | Self-hosting | Compiler written in Codex, bootstrap harness |
| M13 | Fixed point | Stage 2 = Stage 1. Self-hosting proven. |
| **MM1** | **Freedom** | Reference compiler locked. All development in Codex. |
| Peak II | Native backends | RISC-V, ARM64, x86-64, WASM. Bare metal code generation. |
| Peak III | Codex.OS | 7KB kernel. Preemptive multitasking. Capability-enforced syscalls. |
| CCE | Own encoding | Frequency-sorted character encoding. Fixed point at 298,752 chars. |
| **MM2** | **Bare metal** | Compiler runs on bare hardware. Compiles programs over serial. No OS beneath but ours. |
| **MM3** | **???** | The compiler compiles itself on bare metal. The ultimate fixed point. |

---

## The Team

Four AI agents and a human, coordinating through git:

| Agent | Environment | Role |
|-------|-------------|------|
| Windows | VS 2022 + Copilot | Features, reviews |
| Linux | Ubuntu container + Claude | Testing on real hardware, tracing, bare metal |
| Cam | Claude Code CLI (1M Opus) | Fast iteration, parallel work, CCE migration |
| Nut | VS 2026 + Copilot (garage box) | Hardware lab, OS dev, phone flash |
| **Human** | Routes between agents | Architecture, design decisions, the why |

Nobody merges their own code to master without review. Git is the coordination
protocol. The agents review each other's work.

---

## What's Ahead

- **Closure escape analysis** — linear closures, CDX2043 to error
- **Codex.OS Ring 4** — self-hosting compiler on bare metal
- **Codex.UI** — semantic primitives, typed themes
- **The floppy disk** — boot → compiler → self-compile, all in 1.44 MB

See [docs/CurrentPlan.md](docs/CurrentPlan.md) for the active plan.

---

## Documentation

- [00-OVERVIEW.md](docs/00-OVERVIEW.md) — Project overview and status
- [10-PRINCIPLES.md](docs/10-PRINCIPLES.md) — Engineering principles
- [CurrentPlan.md](docs/CurrentPlan.md) — Active plan and direction
- [Designs/](docs/Designs/) — Feature design documents
- [Vision/NewRepository.txt](docs/Vision/NewRepository.txt) — The original vision
- [Vision/IntelligenceLayer.txt](docs/Vision/IntelligenceLayer.txt) — The manifesto

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for coding standards, project conventions,
and agent collaboration rules.

---

## License

See repository for license details.
