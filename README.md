# Codex

**The first self-sustaining compiler.**

A *self-sustaining compiler* is a single artifact that, on its own,
runs on bare metal, takes its own source as input, and produces another
instance of itself — with no external host, no separate operating system,
no runtime to install. The compiler *is* the runtime. The compiler *is*
the OS. They are not two things cohabiting; they are one entity.

The operational test:

> Hand me the compiler artifact. Nothing else. Can I do anything with it?

| Artifact | Can I do anything with just this? |
|---|---|
| gcc / rust / ghc / ocaml | **No.** Need Linux/macOS/Windows. |
| HolyC binary (TempleOS) | **No.** Need TempleOS underneath. |
| Oberon compiler | **No.** Need the Oberon System. |
| Lisp Machine compiler | **No.** Need the Lisp Machine environment. |
| Forth interpreter | I can run Forth code, but it can't reproduce itself. |
| **Codex.Codex.elf** | **Yes.** Boot it. Compile its own source. Get another copy of itself. |

That property — *the artifact alone is sufficient* — has not been
achieved by any prior compiler. Self-hosting (the compiler can compile
itself) has been done thousands of times. Self-sustainment of a *system*
(TempleOS, Native Oberon, Lisp Machines) has been done a handful of
times — but in each case the compiler is one component within an
operating system, not the whole. Codex collapses that distinction. The
compiler-as-binary *is* the OS, not because they're packaged together,
but because there is no internal seam between them.

Built solo by one human in collaboration with a fleet of AI agents, in
41 days (2026-03-14 → 2026-04-24).

---

## Verified

As of 2026-04-24:

- **Bootstrap 1** (.NET, C# output): stage 1 = stage 3 byte-identical.
- **Bootstrap 1.1** (.NET, Codex-text output): stage 1 = stage 2 byte-identical.
- **Bootstrap 2** (bare-metal QEMU, Codex-text output): stage 1 === stage 2
  byte-identical at 685,903 bytes. Heap HWM 373 MB. Stack HWM 2.2 MB.
- **Bootstrap 3** (bare-metal QEMU, ELF output): stage 1 === stage 2
  byte-identical at 1,223,024 bytes. Heap HWM 645 MB. Stack HWM 2.6 MB.
- **Fixed-point continuity**: ten consecutive iterations of Bootstrap 3
  (stage 2 through stage 10) produced byte-identical output, with
  identical heap and stack high-water marks — the heap allocator is
  fully deterministic.

The compiler is a hard fixed point of itself on bare metal.

---

## Why

Most software is built on borrowed trust — someone else's OS, someone
else's runtime, someone else's certificate authority. Every dependency
is an assumption you can't verify. Codex is the project that stops
assuming.

- **Single-artifact substrate.** Boot `Codex.Codex.elf`. There is no
  layer beneath it that you didn't compile yourself.
- **Literate by design.** Chapters and Sections aren't comments. They're
  structure. The compiler parses prose alongside code.
- **Own character encoding.** CCE (Codex Character Encoding) is
  frequency-sorted: `is-letter` is one comparison, not a table lookup.
  Unicode only at I/O boundaries.
- **Multiple backends.** The same compiler can target managed runtimes
  (C#, IL, JavaScript, Wasm) and native code (x86-64 bare metal). The
  bare-metal target is the perf and trust target; the others are
  ergonomic.
- **Algebraic effects.** Side effects are declared in types and handled
  explicitly. No surprise mutations.
- **Trust by structure, not by audit.** Capability lattice and CCE-level
  attack-surface reduction designed in from the substrate, not bolted
  on after.

```codex
Chapter: Greeting

  A module that greets people by name.

Section: Functions

    greet : Text -> Text
    greet (name) = "Hello, " ++ name ++ "!"

    main : Text
    main = greet "World"
```

---

## Quick Start

**Prerequisites**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
for the reference path, WSL + QEMU for the bare-metal path.

```sh
# Build everything
dotnet build Codex.sln

# Run all tests
dotnet test Codex.sln

# Compile and run a program
dotnet run --project tools/Codex.Cli -- run samples/hello.codex

# Compile to multiple targets
dotnet run --project tools/Codex.Cli -- build samples/hello.codex --targets cs,js,rust

# Bootstrap 1 + 1.1 (.NET-hosted self-compilation)
dotnet run --project tools/Codex.Cli -- bootstrap

# Bootstrap 2 (bare-metal pingpong, Codex-text output)
wsl bash tools/pingpong.sh

# Bootstrap 3 (bare-metal ELF emits bare-metal ELF — the self-sustaining test)
wsl bash tools/bootstrap3.sh
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
  is Circle (r) -> 3.14 * r * r
  is Rectangle (w) (h) -> w * h

-- Polymorphism
identity : a -> a
identity (x) = x

-- Effects and act-blocks
opening : [Console] Nothing = act
  print-line "What is your name?"
  name <- read-line
  print-line ("Hello, " ++ name ++ "!")
end
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

The pipeline exists twice: in C# (the reference implementation) and in
Codex (the self-hosted compiler, ~12,000 lines across 50 `.codex`
files). The Codex version is the one that runs on bare metal. The C#
version is scaffolding.

---

## Backends

| Backend | Target | Role |
|---------|--------|------|
| C# | `--targets cs` | Reference. Bootstrap 1. |
| Codex-text | (internal) | Bootstrap 1.1, 2. Re-emits self as Codex source. |
| .NET IL | `--targets il` | Sample target for `.dll`/`.exe` output |
| JavaScript | `--targets js` | Web target |
| WebAssembly | `--targets wasm` | Binary `.wasm` modules |
| Python, Rust, C++, Go, Java, Ada, Fortran, COBOL | various | Sample/research transpilation targets |
| RISC-V | `--targets riscv` | Native machine code (deferred) |
| ARM64 | `--targets arm64` | Native machine code (deferred) |
| **x86-64** | `--targets x86-64-bare` | **Native bare-metal ELF. Self-sustaining target.** |

x86-64 is the trust target. Everything else is for ergonomics or
research. When the README says *self-sustaining*, it means the x86-64
bare-metal output.

---

## CCE — Codex Character Encoding

Codex has its own 128-byte character encoding, frequency-sorted for
computation:

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

Unicode exists only at I/O boundaries. Internally, everything is CCE.

---

## Self-Sustaining Bootstrap

Two notations for convergence:

- `==` — semantic fixed point (stage N and stage N+1 produce equivalent output)
- `===` — byte-perfect identity (stage N and stage N+1 are identical binaries)

`stage1 === stage2` proves convergence on the artifact.
`stage2 === stage3 === ... === stageN` proves the fixed point holds
under continued iteration. Codex has been verified through stage 10.

```sh
# Full bootstrap chain (BS1, BS1.1, then optionally BS2 and BS3)
dotnet run --project tools/Codex.Cli -- bootstrap

# BS2 (pingpong: bare-metal Codex-text emit, byte-identity gate)
wsl bash tools/pingpong.sh

# BS3 (bare-metal ELF emit, the self-sustaining test)
wsl bash tools/bootstrap3.sh

# Stage N from stage N-1 (continued fixed-point check)
wsl bash tools/bootstrap3-stageN.sh 5    # produces stage5.elf from stage4.elf
```

---

## Project Structure

```
Codex.sln                        Reference compiler + tests + tools
├── src/                         Reference compiler (C#)
│   ├── Codex.Core               Diagnostics, SourceText, CceTable, Map<K,V>
│   ├── Codex.Syntax             Lexer, Parser, ProseParser
│   ├── Codex.Ast                Desugarer, AST nodes
│   ├── Codex.Semantics          ChapterScoper, NameResolver
│   ├── Codex.Types              TypeChecker, Unifier
│   ├── Codex.IR                 IR nodes, Lowering, LambdaLifting
│   ├── Codex.Emit.*             Backend emitters
│   └── ...                      LSP, Repository, Narration, Proofs
├── Codex.Codex/                 Self-hosted compiler (~50 .codex files, ~12K lines)
├── foreword/                    Standard library
├── tests/                       Test projects across the solution
├── tools/
│   ├── Codex.Cli                Command-line interface
│   ├── Codex.Bootstrap-Codex    Codex-side bootstrap harness
│   ├── Codex.Bootstrap          Legacy C# bootstrap harness
│   ├── pingpong.sh              Bootstrap 2 driver
│   ├── bootstrap3.sh            Bootstrap 3 driver
│   ├── bootstrap3-stageN.sh     Continued fixed-point check
│   ├── codex-agent/             AI agent toolkit
│   └── Codex.VsExtension        Visual Studio extension
├── samples/                     Example programs
└── docs/
    ├── FOUNDING-VISION.md       Read this first
    ├── CurrentPlan.md           What we're doing now
    ├── Active/                  Work in progress
    ├── Designs/                 Future work
    ├── Done/                    Completed milestones
    ├── Stories/                 Narrative documents — including VoodooChild.md, the BS3-green night
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
codex bootstrap                            BS1 + BS1.1 self-hosting verification
codex version                              Print version
```

---

## Editor Support

**VS Code** — install from `editors/vscode/`:

```sh
cd editors/vscode
npm install
npx vsce package && code --install-extension codex-lang-0.1.0.vsix
```

Syntax highlighting, bracket matching, auto-indentation, and LSP
integration (diagnostics, hover, go-to-definition).

**Visual Studio** — extension project at `tools/Codex.VsExtension/`.

---

## The Road

| Milestone | What | Status |
|-----------|------|--------|
| Foundation | Reference compiler in C#, type system, IR | Done |
| Self-hosting | Codex compiler in Codex, BS1 fixed-point | Done |
| CCE | Frequency-sorted byte encoding, no Unicode internally | Done |
| Bare metal | x86-64 ELF emit, runs on QEMU with no OS | Done |
| **Self-sustaining** | **BS3 bare-metal compiler reproduces itself byte-identical** | **Done (2026-04-24)** |
| Phase discipline | Survey-before-allocate replaces guess-based reclamation | Designed, implementation pending |
| Codex.OS | Capability network, agent protocol, shell, FS — built on the self-sustaining substrate | Designed |

See [docs/CurrentPlan.md](docs/CurrentPlan.md) for the active plan,
[docs/Stories/VoodooChild.md](docs/Stories/VoodooChild.md) for the
night BS3 went green, and
[docs/Stories/THE-LAST-PEAK.md](docs/Stories/THE-LAST-PEAK.md) for
where this is going.

---

## Documentation

- [FOUNDING-VISION.md](docs/FOUNDING-VISION.md) — **Read this first**
- [CurrentPlan.md](docs/CurrentPlan.md) — Active plan and direction
- [Active/](docs/Active/) — Work in progress
- [Designs/](docs/Designs/) — Feature and OS design documents
- [Done/](docs/Done/) — Completed milestones and postmortems
- [Stories/VoodooChild.md](docs/Stories/VoodooChild.md) — The night BS3 went green
- [Stories/THE-ASCENT.md](docs/Stories/THE-ASCENT.md) — The story so far
- [SYNTAX-QUICKREF.md](docs/SYNTAX-QUICKREF.md) — Language syntax reference

---

## No Dates

Every estimate has been wrong by orders of magnitude, in both
directions. We don't put dates on mountains. The critical path is
ordered. That's all we need to know.

---

## License

See repository for license details.
