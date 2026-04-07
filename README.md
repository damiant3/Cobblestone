# Codex

**A programming language that compiles itself, reads like a book, and trusts nothing it didn't build.**

Codex is a statically typed, purely functional language where source files are
literate documents — prose explains intent, indented notation is executable.
The compiler is written in Codex. It compiles itself on bare metal x86-64.
It has its own character encoding. It runs with no OS, no libc, no runtime.

The long-term goal is Codex.OS — a complete software stack on owned hardware
with trust at the binary boundary. No borrowed substrate. No borrowed trust.

```codex
Chapter: Greeting

  A module that greets people by name.

Section: Functions

    greet : Text -> Text
    greet (name) = "Hello, " ++ name ++ "!"

    main : Text
    main = greet "World"
```

> **Self-Hosting Proven.** The compiler, running as a native x86-64 binary
> (no OS, no runtime), compiles its own 33-file source and reaches a semantic
> fixed point. 1,149 tests pass. 15 backends. Zero failures.

---

## Why

Most software is built on borrowed trust — someone else's OS, someone else's
runtime, someone else's certificate authority. Every dependency is an
assumption you can't verify. Codex is the project that stops assuming.

- **Literate by design.** Chapters and Sections aren't comments. They're structure.
  The compiler parses prose alongside code.
- **Self-hosting.** The compiler compiles itself. The bootstrap is proven:
  Stage 1 = Stage 2 = fixed point. On bare-metal x86-64, the identity emitter
  reaches a semantic fixed point (12K lines, 1,144 definitions).
- **Own character encoding.** CCE (Codex Character Encoding) is frequency-sorted:
  `is-letter` is a single comparison, not a table lookup. Unicode only at I/O
  boundaries.
- **Fifteen backends.** C#, JavaScript, Python, Rust, C++, Go, Java, Ada, Fortran,
  COBOL, .NET IL, RISC-V, ARM64, x86-64, and WebAssembly.
- **Bare metal.** The compiler runs on x86-64 hardware with no OS beneath it.
  Codex.OS boots from a floppy image, runs a preemptive multitasking kernel
  in 7KB, and compiles Codex programs on bare hardware.
- **Algebraic effects.** Side effects are declared in types and handled explicitly.
  No monads. No surprise mutations.
- **Trust architecture.** CDX1 binary format with lattice signatures, capability
  refinement, and an agent trust protocol. Designed for environments where you
  can't patch your way out of a compromised dependency.

---

## Quick Start

**Prerequisites**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```sh
# Build everything
dotnet build Codex.sln

# Run all tests (1,149 tests)
dotnet test Codex.sln

# Compile and run a program
dotnet run --project tools/Codex.Cli -- run samples/hello.codex

# Compile to multiple targets
dotnet run --project tools/Codex.Cli -- build samples/hello.codex --targets cs,js,rust

# Build a multi-file project
dotnet run --project tools/Codex.Cli -- build samples/word-freq/

# Bootstrap: compile the compiler with itself
dotnet run --project tools/Codex.Bootstrap

# Agent toolkit
dotnet tools/codex-agent/codex-agent.dll orient
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
    → ChapterScoper namespace scoping across chapters
    → NameResolver  resolved names + citations
    → TypeChecker   bidirectional type inference
    → Lowering      typed intermediate representation
    → Emitter       target source code / machine code
```

The pipeline exists twice: in C# (the locked reference implementation)
and in Codex (the self-hosted compiler, 33 files, ~12,000 lines). The
Codex version is the one that matters.

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
| **x86-64** | `--targets x86-64` | **Native machine code. Bare metal. Self-hosting.** |
| RISC-V | `--targets riscv` | Native machine code. Deferred. |
| ARM64 | `--targets arm64` | Native machine code. Deferred. |
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

## Standard Library (Foreword)

23 modules, ~1,250 lines of Codex:

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
| `CCE` | Character encoding — classification, conversion |
| `TextSearch` | Text search utilities |
| `Identity` | Identity functor |
| `State` | Stateful computations |
| `Console` | Terminal I/O effects |
| `FileSystem` | File I/O effects |
| `Network` | Network effects |
| `Time` | Time effects |
| `Random` | Random number effects |
| `Camera`, `Display`, `Location`, `Microphone`, `Sensors` | Hardware effects for Codex.OS |

---

## Self-Hosting Bootstrap

The compiler compiles itself. Two notations for convergence:

- `==` — semantic fixed point (stage0 and stage1 produce equivalent output)
- `===` — byte-perfect identity (stage1 and stage2 are identical binaries)

`stage1 === stage2` proves convergence. Diverse double-compile against the
C# reference compiler proves correctness (Thompson attack resistance).

```sh
# Run the bootstrap
dotnet run --project tools/Codex.Bootstrap

# Benchmark (3 warmup + 10 measured, median)
dotnet run --project tools/Codex.Bootstrap -- --bench
```

---

## Codex.OS

A bare-metal operating system for the Codex compiler.

| Ring | What | Status |
|------|------|--------|
| 0 | Multiboot boot, 32→64 trampoline, serial I/O, heap + stack | Done |
| 1 | IDT (256 vectors), PIC, timer interrupts, keyboard input | Done |
| 2 | Process table (16 slots), preemptive context switch, page tables | Done |
| 3 | Capability-enforced syscalls | Done |
| 4 | Self-hosting compiler on bare metal | **Done** |

7KB kernel. Boots from floppy. The self-hosted compiler runs on bare metal
x86-64 (QEMU, 512 MB) and compiles its own source. No OS, no runtime,
just the UART.

The OS stack beyond the compiler — trust network, agent protocol, policy
contracts, filesystem, shell — is designed but awaits MM4 (the second
bootstrap: compiler compiled entirely by Codex with no C# in the chain).

---

## Project Structure

```
Codex.sln                        40 projects, builds clean, 1,149 tests
├── src/                         Reference compiler (C#, locked)
│   ├── Codex.Core               Diagnostics, SourceText, CceTable, Map<K,V>
│   ├── Codex.Syntax             Lexer, Parser, ProseParser
│   ├── Codex.Ast                Desugarer, AST nodes
│   ├── Codex.Semantics          ChapterScoper, NameResolver
│   ├── Codex.Types              TypeChecker, Unifier, LinearityChecker
│   ├── Codex.IR                 IR nodes, Lowering
│   ├── Codex.Emit.*             15 backend emitters
│   └── ...                      LSP, Repository, Narration, Proofs
├── Codex.Codex/                 Self-hosted compiler (33 .codex files, ~12K lines)
├── foreword/                    Standard library (23 modules, ~1,250 lines)
├── tests/                       1,149 tests across 9 projects
├── tools/
│   ├── Codex.Cli                Command-line interface
│   ├── Codex.Bootstrap          Bootstrap harness + benchmark
│   ├── codex-agent/             AI agent toolkit (orient, doctor, build, test, handoff)
│   └── Codex.VsExtension        Visual Studio extension
├── samples/                     41 example programs
└── docs/
    ├── FOUNDING-VISION.md       Read this first
    ├── CurrentPlan.md           What we're doing now
    ├── Active/                  Work in progress
    ├── Designs/                 Future work (Codex.OS, language, backends, tools)
    ├── Done/                    Completed milestones and postmortems
    ├── Stories/                 The Ascent, The Last Peak, and other narratives
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
codex bootstrap [dir]                     Full self-hosting verification
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

## The Road

| Milestone | What | Status |
|-----------|------|--------|
| M0–M12 | Foundation through self-hosting | Done |
| **MM1** | Reference compiler locked. All development in Codex. | **Done** |
| Peak II | Native backends (x86-64, RISC-V, ARM64, WASM) | Done |
| Peak III | Codex.OS — 7KB kernel, bare metal | Done |
| CCE | Own character encoding, frequency-sorted | Done |
| **MM2** | Compiler runs on bare hardware | **Done** |
| **MM3** | Compiler compiles itself on bare metal | **Done** |
| **MM4** | Second bootstrap — no C# in the chain | **Active** |
| Codex.OS | Trust network, agent protocol, shell | Designed, after MM4 |

See [docs/CurrentPlan.md](docs/CurrentPlan.md) for the active plan and
[docs/Stories/THE-LAST-PEAK.md](docs/Stories/THE-LAST-PEAK.md) for where
this is going.

---

## Documentation

- [FOUNDING-VISION.md](docs/FOUNDING-VISION.md) — **Read this first**
- [CurrentPlan.md](docs/CurrentPlan.md) — Active plan and direction
- [Active/](docs/Active/) — Work in progress
- [Designs/](docs/Designs/) — Feature and OS design documents
- [Done/](docs/Done/) — Completed milestones and postmortems
- [Stories/THE-ASCENT.md](docs/Stories/THE-ASCENT.md) — The story so far
- [SYNTAX-QUICKREF.md](docs/SYNTAX-QUICKREF.md) — Language syntax reference
- [KNOWN-CONDITIONS.md](docs/KNOWN-CONDITIONS.md) — Build and test notes

---

## No Dates

Every estimate has been wrong by orders of magnitude, in both directions.
We don't put dates on mountains. The critical path is ordered. That's all
we need to know.

---

## License

See repository for license details.
