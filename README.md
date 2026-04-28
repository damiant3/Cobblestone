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

As of 2026-04-28:

- **Bootstrap 1** (.NET, C# output): stage 1 = stage 3 byte-identical.
- **Bootstrap 1.1** (.NET, Codex-text output): stage 1 = stage 2 byte-identical.
- **Bootstrap 2** (bare-metal QEMU, Codex-text output): stage 1 === stage 2
  byte-identical at 766,624 bytes. Heap HWM **9 MB**. Stack HWM 2.8 MB.
- **Bootstrap 3** (bare-metal QEMU, ELF output): stage 1 === stage 2
  byte-identical at 1,384,568 bytes. Heap HWM **41 MB**. Stack HWM 2.8 MB.
- **Fixed-point continuity**: ten consecutive iterations of Bootstrap 3
  produced byte-identical output, with identical heap and stack high-water
  marks — the heap allocator is fully deterministic.
- **Sample battery**: 72 verified runtime + 22 expected-fail diagnostics +
  11 skipped + 0 fail of 105 samples (`tools/sweep.sh`).

The compiler is a hard fixed point of itself on bare metal, and runs
end-to-end in tens of megabytes — down ~16× since the first BS3 green.

**`seed/Codex.Codex.elf`** (1,384,568 bytes):

| Algorithm | Digest |
|---|---|
| MD5    | `32f1d28d31492ea934f4423f40df484e` |
| SHA-256 | `514ccddcab051c2a1d4e4b5ecedbed8b252755488530e0c1f03c1dde4b789498` |

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
  cites Codex chapter Text

  A small program that greets the user by name. The opening declares
  `[Console]` in its type — the effect is part of the contract, not a
  surprise that happens at runtime.

Section: Functions

  greet : Text -> Text
  greet (name) = "Hello, " ++ name ++ "!"

  opening : [Console] Nothing = act
    print-line "What is your name?"
    name <- read-line
    print-line (greet name)
  end
```

---

## Quick Start

**Prerequisites**: WSL + QEMU for the bare-metal path. The .NET reference
compiler under `src/` has been retired; the live story is the bare-metal
selfhost driven by `seed/Codex.Codex.elf`.

> **Note on `Codex.sln`**: the reference compiler is **disabled** at the
> current head — `dotnet build Codex.sln` and `dotnet run --project
> tools/Codex.Cli` will not work. To exercise the legacy reference path,
> sync to an earlier checkin (pre-CL 447 era) where the `.NET solution still
> built. The seed ELF and the selfhost compiler do not depend on it.

```sh
# Sample battery (bare-metal selfhost, ~2-5s per sample; --jobs=N for parallel)
bash tools/sweep.sh --jobs=8

# Bootstrap 2 (bare-metal pingpong, Codex-text output, selfhost-driven)
wsl bash tools/pingpong-self.sh

# Bootstrap 3 (bare-metal ELF emits bare-metal ELF — the self-sustaining test)
wsl bash tools/bootstrap3.sh
```

### Try it without building (just QEMU + the seed ELF)

The ELF in `seed/Codex.Codex.elf` is a complete compiler. Boot it under
QEMU, hand it source bytes on serial, and it hands back another ELF on
the same socket. Two helpers wrap the protocol — feed and run:

```sh
# Stage the seed where the helpers expect it.
mkdir -p build-output/bare-metal build-output/try
cp seed/Codex.Codex.elf build-output/bare-metal/

# Compile a sample with the seed (boots QEMU, sends source, reads emitted ELF).
wsl bash tools/sample-compile-selfhost.sh samples/hello.codex build-output/try/hello.elf build-output/try/build.log

# Boot the just-compiled program in QEMU and capture its serial output.
wsl bash tools/run-for-sweep.sh build-output/try/hello.elf build-output/try/hello.out
cat build-output/try/hello.out
```

Two-channel serial: COM1 carries data (source in, ELF or runtime stdout
out), COM2 carries control (`READY` greeting). Inspect
`tools/sample-compile-selfhost.sh` and `tools/qemu-config.sh` to see the
raw `qemu-system-x86_64` invocation.

---

## Language Features

```codex
Chapter: Feature Tour
  cites Codex chapter Text

Section: Sum types

  Shape =
    | Circle (Integer)
    | Rectangle (Integer) (Integer)

Section: Records

  Person = record {
    name : Text,
    age : Integer
  }

Section: Pattern matching

  area : Shape -> Integer
  area (s) = when s
    is Circle (r) -> r * r * 3
    is Rectangle (w) (h) -> w * h

Section: Polymorphism

  identity : a -> a
  identity (x) = x
```

### Bounded integers (subtypes + auto-narrowing)

Codex doesn't have `Int8` / `UInt16` / `Int32` / `UInt64`. It has one
`Integer` and a *range constraint*:

- `Integer between 0 and 255` — compiler picks 8-bit unsigned storage
- `Integer between -32768 and 32767` — 16-bit signed
- `Integer between 1 and 1048576` — 24-bit, unsigned (no negatives)
- bare `Integer` — 64-bit machine word

The width and signedness are *derived* from the declared range, not
spelled by the author. Record fields pack tight: three `0..65535`
fields take 6 bytes, not 24.

Out-of-range values are a static error (CDX2050: literal out of bound,
CDX2051: wider type than bound). At assignment sites where the
compiler can't statically prove the value fits, the field's
**overflow mode** decides what happens at runtime:

```codex
Byte       = Integer between 0 and 255 wrapping     -- modular arithmetic
Percentage = Integer between 0 and 100 clamping     -- saturates at bounds
SafeIndex  = Integer between 0 and 1024             -- default `error` (traps)
```

`__narrow expr` is the explicit-narrow primitive: write it at a
narrowing site to acknowledge the intent. The downstream check still
runs — `__narrow` is intent, not a cast.

```codex
Chapter: Net
  cites Codex chapter Text

Section: Types

  Port = record {
    num : Integer between 0 and 65535
  }

  Pair = record {
    lo : Integer between 0 and 255,
    hi : Integer between 0 and 255
  }

Section: Functions

  split-u16 : Port -> Pair
  split-u16 (p) =
    let lo = __narrow (int-mod (p.num) 256)
    in let hi = __narrow (p.num / 256)
    in Pair { lo = lo, hi = hi }
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

The pipeline lives in `Codex.Codex/` — the self-hosted compiler,
~12,000 lines across ~50 `.codex` files. This is the only path that is
maintained, exercised, and load-bearing.

The original C# reference implementation under `src/` was the
scaffolding that bootstrapped the language. It is **retired**: the
`.csproj` files have been stripped of their `TargetFramework` and no
longer build, the legacy CLI under `tools/Codex.Cli` is no longer in
the bootstrap chain, and the REF-driven sweep harness was removed in
CL 461. The reference code remains in the depot as historical record;
no further maintenance is planned and no change to the live compiler
should depend on it. To exercise the legacy reference path, sync to a
pre-CL 447 era checkin where the `.NET solution still built.

---

## Backends (Codegen and Emitters)

| Backend | Target | Status | Role |
|---------|--------|--------|------|
| **x86-64 bare metal** | `--targets x86-64-bare` | **Full support** | Native bare-metal ELF. **Self-sustaining target.** |
| **Codex-text** | (internal) | **Full support** | Bootstrap 1.1, 2. Re-emits self as Codex source. |
| C# | `--targets cs` | various states (YMMV) | Reference. Bootstrap 1. |
| .NET IL | `--targets il` | various states (YMMV) | Sample target for `.dll`/`.exe` output |
| JavaScript | `--targets js` | various states (YMMV) | Web target |
| WebAssembly | `--targets wasm` | various states (YMMV) | Binary `.wasm` modules |
| Python, Rust, C++, Go, Java, Ada, Fortran, COBOL | various | various states (YMMV) | Sample/research transpilation targets |
| RISC-V | `--targets riscv` | deferred | Native machine code |
| ARM64 | `--targets arm64` | deferred | Native machine code |

x86-64 bare metal and Codex-text are the load-bearing pair: x86-64 is
the trust target (the binary that runs on hardware), Codex-text is the
self-emit target (the binary that re-emits its own source). Both are
fully supported and gated by the bootstrap battery. Every other backend
is for ergonomics or research and may not track current language
features.

---

## CCE — Codex Character Encoding

Codex has its own 128-byte character encoding, frequency-sorted for
computation:

| Range | Category | Count | Characters |
|-------|----------|------:|------------|
| 0-2 | Whitespace | 3 | `NUL` `LF` (space) |
| 3-12 | Digits | 10 | `0 1 2 3 4 5 6 7 8 9` |
| 13-38 | Lowercase (frequency-sorted) | 26 | `e t a o i n s h r d l c u m w f g y p b v k j x q z` |
| 39-64 | Uppercase (frequency-sorted) | 26 | `E T A O I N S H R D L C U M W F G Y P B V K J X Q Z` |
| 65-96 | Punctuation | 32 | `. , ! ? : ; ' " - ( ) + = * < > / @ # & _ \ \| [ ] { } ~ \` ^ $ %` |
| 97-112 | Accented Latin | 16 | `é è ê ë á à â ä ó ô ö ú ü ñ ç í` |
| 113-127 | Cyrillic | 15 | `а о е и н т с р в л к м д п у` |

Character classification is arithmetic, not table lookup:
- `is-letter (c)` = `char-code c >= 13 && char-code c <= 64`
- `is-digit (c)` = `char-code c >= 3 && char-code c <= 12`
- `is-punct (c)` = `char-code c >= 65 && char-code c <= 96`
- `to-upper (c)` = `is-lower c then code-to-char (char-code c + 26) else c`
  (case shift is just `±26`)

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

# BS2 (pingpong: bare-metal Codex-text emit, byte-identity gate, selfhost-driven)
wsl bash tools/pingpong-self.sh

# BS3 (bare-metal ELF emit, the self-sustaining test)
wsl bash tools/bootstrap3.sh

# Stage N from stage N-1 (continued fixed-point check)
wsl bash tools/bootstrap3-stageN.sh 5    # produces stage5.elf from stage4.elf
```

---

## Project Structure

```
Codex.Codex/                     Self-hosted compiler (~50 .codex files, ~12K lines).
│                                The live, maintained pipeline.
├── Core/                        SourceText, CCE table, SkipList, Diagnostics, CdxCodes
├── Syntax/                      Lexer, Parser, ProseParser
├── Ast/                         AST nodes, Desugarer
├── Semantics/                   ChapterScoper, NameResolver
├── Types/                       TypeChecker, Unifier
├── IR/                          IR nodes, Lowering, LambdaLifting
└── Emit/                        Codex-text emitter, C# emitter, X86_64 codegen,
                                 ElfWriter, DwarfWriter

seed/
└── Codex.Codex.elf              Canonical bootstrap seed (~1.4 MB) — the
                                 proven hard fixed point; pingpong starts here.
foreword/                        Standard library (cited from .codex sources)
samples/                         Example programs (105 samples)

tools/
├── Codex.Cli-Codex/             Selfhost-side CLI (Codex source compiled to
│                                .NET dll by the selfhost itself)
├── CodexHost/                   Stack-bumped launcher for the selfhost dll
├── Codex.Bootstrap-Codex/       Selfhost-side bootstrap harness
├── sweep.sh                     Sample battery (bare-metal selfhost via QEMU)
├── pingpong-self.sh             Bootstrap 2 driver (selfhost-driven; live)
├── bootstrap3.sh                Bootstrap 3 driver
├── bootstrap3-stageN.sh         Continued fixed-point check
├── sample-compile-selfhost.sh   One-shot: ELF + sample → ELF
├── run-for-sweep.sh             One-shot: boot ELF in QEMU and capture stdout
├── qemu-config.sh               WHPX/KVM selection, chardev setup
├── sublime/                     Sublime Text syntax
├── vscode/                      VS Code grammar
├── codex-agent/                 AI agent toolkit
└── Codex.VsExtension/           Visual Studio extension

docs/
├── FOUNDING-VISION.md           Read this first
├── CurrentPlan.md               What we're doing now
├── BACKLOG.md                   Outstanding work
├── Active/                      Work in progress
├── Designs/                     Future work — language, OS, capabilities, etc.
├── Done/                        Completed milestones and postmortems
├── Stories/                     Narrative documents — VoodooChild.md (the BS3-green night)
└── Vision/                      Original vision documents

--- retired (depot history only, no longer maintained) ---

Codex.sln                        Solution file. .csproj TargetFrameworks
                                 stripped — `dotnet build` is a no-op.
src/                             C# reference compiler. Bootstrapped the language;
                                 the cord was cut at MM4 (CL 340). Code stays in
                                 the depot as historical record. No further work.
tests/                           xUnit tests for the C# reference compiler.
                                 Retired by implication — the targets they exercise
                                 are gone. Pre-CL 447 history if you need them.
tools/Codex.Cli/                 Legacy C# CLI. Not in the bootstrap chain.
tools/Codex.Bootstrap/           Legacy C# bootstrap driver. Replaced by
                                 Codex.Bootstrap-Codex.
```

---

## CLI

The selfhost CLI lives at:

```
tools/Codex.Cli-Codex/bin/Debug/net8.0/Codex.Cli-Codex.exe
```

It is the Codex source for the CLI compiled to a .NET dll by the
selfhost itself. The CLI runs in CCE internally; .NET hosts it.

**Working commands** (verified at CL 466):

```
codex build       <file.codex|dir> --target x86-64-bare \
                  [--output-dir <d>] [--exit-mode repl|qemu-exit] \
                  [--watchdog progress|pet]
                                            Compile to bare-metal ELF
codex dump-source <dir> [out]               Quire-aware concat of all .codex
                                            in <dir> (cite-resolved, in deps order)
codex version                               Print version
codex --help, -h                            Show usage
```

**Stubbed** (port pending — they print `not yet implemented` and exit):
`run`, `check`, `parse`, `encode`, `bootstrap`. Targets other than
`x86-64-bare` print a not-supported diagnostic.

For everything not yet on the CLI, the wrappers under `tools/`
(`pingpong-self.sh`, `bootstrap3.sh`, `sweep.sh`,
`sample-compile-selfhost.sh`, `run-for-sweep.sh`) cover the live workflows.

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
| Reference retired | Stripped C# reference; selfhost is the only path | Done (CL 461, 2026-04-28) |
| Bounded integers | `Integer between L and H`, auto-width, overflow modes, `__narrow` | Done (CL 411, 2026-04-27) |
| HWM reduction | Compiler runs end-to-end in tens of MB (645 → 41 MB BS3) | Done (CL 463, 2026-04-28) |
| Seed in depot | `seed/Codex.Codex.elf` checked-in canonical bootstrap | Done (CL 449, 2026-04-27) |
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
