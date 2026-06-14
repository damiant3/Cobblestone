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
| **Codex.cdx** | **Yes.** Boot it. Compile its own source. Get another copy of itself. |
| **Codex.img** | **Yes.** Write it to a USB stick. Put it in your computer. It runs. |

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

As of 2026-06-13:

- **CDX fixed point**: pingpong all phases green — text round-trip
  (stage1 === stage2) + CDX fixed-point (stage1.cdx === stage2.cdx),
  byte-identical. The compiler reproduces itself on bare metal.
- **331 library modules** (238 foreword + 93 OS) across 24 quires: data structures, crypto,
  networking (full TCP/IP + UDP/ICMP/DNS/DHCP/NTP/Syslog/TFTP), game
  engine (A*, hex maps, ECS, physics, Voronoi, Perlin), AI inference
  (tensors, neural nets, GGUF model loading, genetic algorithms),
  encoding (JSON, Base64, Protobuf, CSV), math (quaternions, matrices,
  Bezier, FFT), compression (LZ77, Huffman, RLE), UI toolkit (themeable
  widgets, compositor, layout engine, event system, orchestrator), and more.
- **OS stack**: preemptive scheduler, IPC channels, identity (Ed25519),
  trust lattice, 5-phase CDX verifier, interactive shell, VGA console,
  developer debugger, HTTP server, process management, UEFI boot path,
  diagnostic shell.
- **GUI substrate**: 18-chapter UI foreword with themeable primitives
  (swap one Theme record to get LCARS, cockpit, or terminal look),
  flex layout, compositor with z-order and alpha blending, event dispatch,
  reactive bindings, animations/throbbers, multi-size bitmap icons,
  bitmap font rendering (CCE-indexed), overlays, sound effects, and a
  central orchestrator loop.
- **Agent lifecycle**: local AI runtime (GGUF loader, inference),
  agent acquisition (bundled/USB/network, verification), coordinator
  (local/upstream escalation, role dispatch), first-boot wizard.
- **GPU compute proxy**: shared-memory command protocol for host-side CUDA
  dispatch. Design doc for RTX 4060 Ti integration.
- **Signed CDX seed**: Ed25519-signed, self-verified, UEFI-bootable
  GPT disk image.
- **VMX hypervisor**: codex-vm.exe (WHP-based VM host), DevHypervisor,
  VmSerial, VmIde — Codex can host and manage virtual machines.
- **UEFI dev console**: interactive colored menus via ConOut/ConIn,
  source tree indexing, keyboard navigation. Hold Escape at UEFI boot.
- **Resilient act blocks**: `trying N times … falling back to … on
  failure … end` — retry loops with bivy-reclaim between attempts,
  optional fallback path, and explicit failure handler. New `fail`
  builtin sets a flag the body's tail checks.
- **Plug architecture**: emitters as standalone CDX programs. Compiler
  emits IR text; plug consumes IR via file I/O, emits target source.
  **48 transpiler plugs** across languages (Ada, Babbage, C#,
  Clojure, COBOL, D, Elixir, Fortran, Go, Groovy, Haskell, Java,
  JavaScript, Julia, Kotlin, Lua, Nim, Objective-C, OCaml, Pascal,
  Perl, PHP, Python, Ruby, Rust, Scala, Scheme, Swift, TypeScript,
  WASM, Zig), UI frameworks (Angular, Electron, Flutter, GTK, HTML,
  Jetpack Compose, MAUI, Qt, React, Svelte, SwiftUI, Vue, WinForms,
  WPF), and binary formats (CDX, ELF, PE, IMG). Port forwarding in
  codex-vm enables host-to-guest TCP for plug data exchange.
- **Dependent types**: `PropEqTy` — the first type carrying value
  information. `===` in type position produces propositional equality;
  `Refl` verified by the unifier (invalid proofs are type errors).
  `Proof` as first-class type name. Proof erasure at emit (zero machine
  code for proof definitions). `claim`/`proof` parser with `induction`
  keyword. Proof builtins: Refl, sym, trans, cong, assume.
- **Static bounds prover**: compiler proves bounded-integer range safety
  at compile time and elides runtime bounds checks (CDX4010). Handles
  literals, field access, int-mod, bit-and, negation. O(1) shallow
  analysis per narrow store (10x compile speedup over prior deep walker).
- **Lazy evaluation with memoization**: `lazy` keyword defers computation;
  `force` builtin evaluates the thunk. Memoizing cell ensures the body
  executes at most once. Desugars to a closure over a mutable cell with
  done-flag check.
- **Type classes (phase 1-2)**: `class`/`instance` keywords, dictionary-
  passing desugaring. `class Show where show : Integer -> Text` generates
  `ShowDict` record type; `instance Show Integer` generates a dict
  constructor and per-instance specialized methods (`show-Integer`).
- **Escape invariant (CDX9003)**: seal-time scan of deck pointers for
  bivy references. Compile with `-EscapeCheck` to detect deck-to-bivy
  violations before phase-compact reclaims bivy scratch. PARSE and SCOPE
  compact disabled pending violation fixes (127K violations identified).
- **Configurable surveys**: phase deck multipliers in `BuildSettings.codex`
  (no longer hardcoded in opening.codex). Deck overflow is a warning, not
  an error --- compilation continues. `compile.ps1` auto-retries with 4 GB
  on VM crash.
- **Deck overflow guard**: all 8 compiler phases (LEX, PARSE, DESUGAR,
  SCOPE, CHECK, LOWER, RESOLVE, LIFT) detect when deck allocation
  exceeds its survey budget and emit CDX9002 as a warning.
- **Fuzz corpus**: 44 adversarial inputs (binary garbage, huge
  identifiers, deep nesting, unclosed syntax, 100KB lines, recursive
  types, keyword abuse) — 0 crashes.
- **Punctual functions (hard real-time)**: `punctual` keyword enforces
  bounded execution at compile time. No heap (CDX6002), no recursion
  (CDX6005), no unsafe calls (CDX6001), no closures (CDX6003), no bare
  I/O (CDX6004). Instruction count reported at CDX6010; optional budget
  with CDX6011 warning. No production language has this combination --
  Ada Ravenscar is global and needs external WCET tools; Rust has nothing;
  MISRA-C is external linters. See `docs/Designs/OS/Active/HardRealtime.md`.
- **Unit types**: `Second = unit Integer` declares a distinct type with
  zero runtime overhead (erased at codegen). Arithmetic preserves units
  (`Second + Second = Second`, `Second * Integer = Second`). Cross-unit
  assignment is a type error. Bounded + unit composition
  (`Second between 0 and 3600`). Conversion declarations parsed
  (`1 Minute = 60 Second`).
- **Mutable records**: `mutable` keyword with in-place field
  assignment, type-checked immutability enforcement (CDX2060).
- **Repository restructure**: 31 top-level dirs to 8. codex-vm
  replaces QEMU as default VM (WHP-based, ~4500 lines C: PCI, xHCI USB,
  Intel HDA audio, HPET, IOAPIC, ACPI, SMBIOS, UEFI firmware, Bochs VBE).
- **Tuples**: `(A, B)` sugar in type position, `let (x, y) = e`
  destructuring. Desugars to foreword `Tup2`..`Tup5`; all 15 transpiler
  plugs emit idiomatic tuple syntax for their target language.
- **Scoped constraint dispatch**: `show`/`compare` inside class-
  constrained functions dispatches through the dictionary only for
  parameter-typed arguments; let-bound locals use direct dispatch.
- **C# plug full-compiler emit**: the emitted full compiler (2376 defs)
  now compiles under `dotnet build` with 0 errors.
- **Durable disk writes**: codex-vm IDE WRITE SECTORS + flush to host
  image file. Accounts and SystemDb persist across restarts.
- **Interactive debugger**: `-debug -break <fn> -map <file>` with
  command shell, guest `!EXC=03` serial interception, symbol resolution,
  conditional breakpoints.
- **For-expressions**: `for x in xs do f x` syntactic sugar for map
  loops. Desugars to `list-map` with a lambda. Dogfooded across ~50
  call sites in 19 files.
- **Sample battery**: 137 smoke tests (consolidated from 232 individual
  tests); 127 pass, 0 fail (10 skipped: fatal or platform-specific) --
  run against stage1, the self-applied compiler. BVT mode: 10-test
  subset, full build in ~113s.
- **Native-class codegen**: sixteen-CL optimization campaign. sum-to-N
  compiles to 14 x86-64 instructions -- below C at /Od (20) and /O2
  (23); fact within 1 of C /Od; fib within 2 of the .NET JITs. TCO
  parallel-move arg shuffle, R8/R9-staged binary operands (binary
  expressions consume zero locals), minimal/near-leaf frame elision,
  IrRemInt (idiv-RDX remainder), conservative leaf inliner.
- **ERP suite**: 23 modules -- GL/AP/AR/Materials/HR/Treasury/
  Controlling/Sales/Production/Maintenance/Quality/Warehouse/Projects/
  BW plus Real Estate, Banking, Insurance, Utilities, and Healthcare
  verticals.
- **628 application modules across 46 apps** -- see Applications below.
- **Codex.Spark creative suite**: 85-module application (3D modeling,
  image editor, animation, audio/DAW, video compositor, skeletal
  animation, particles, procedural noise, interactive UI shell) running
  on bare metal with GOP framebuffer display via codex-vm.
- **Codex.DB**: relational database server (42 modules). Typed schemas,
  pipe-forward queries (`RelScan |> RelFilter |> RelSort |> query`),
  MVCC transactions, WAL, B-tree indexes, hash joins, full-text search,
  replication, graph store, column store, time series, spatial indexes.
- **SystemDb**: on-device persistent store (DiskFacts format) for
  identity, boot config, trust vouches, and drive registry. Lives on
  the boot stick as `CODEX/SYSDB.BIN`.
- **CodexMagic game server**: 56 game modules with web portal.
- **Explorer app**: 17-module parameter explorer with web UI (card
  designer, character designer, item designer, settings editor,
  workflow exporter). CDX server + PS1 bridge + HTML/JS frontend.

The compiler is a hard fixed point of itself on bare metal.

**`seed/Codex.cdx`** (2,094,667 bytes) — the canonical seed:

| Algorithm | Digest |
|---|---|
| SHA-256 (file) | `E9E869A80630BD35C62B42CF08997601C8306EC70C16D9917D475372348935AB` |

**`seed/Codex.img`** (8,388,608 bytes) — bootable GPT disk image:

| Algorithm | Digest |
|---|---|
| SHA-256 | `83E76CC16C326BDEA9F792EE87FE9E3F28360A0BFB645DE57918F5F5DFA2DC2A` |

The IMG contains `EFI/BOOT/BOOTX64.EFI` (interactive UEFI dev console as a
UEFI PE32+ application) and `SOURCE.SRC` (compiler source) in an 8 MB
FAT16 GPT image. Colored menu with keyboard navigation, live RTC clock,
system info screen. Tested on real Asus TUF hardware and codex-vm (auto-
extracts PE from GPT, no manual extraction needed).

Flash to USB (requires elevated PowerShell):

```powershell
# Find your USB disk number:  Get-Disk | Where-Object BusType -eq USB
tools\write-usb.ps1 -Image seed\Codex.img -DiskNumber <N>
```

---

## Why

Most software is built on borrowed trust — someone else's OS, someone
else's runtime, someone else's certificate authority. Every dependency
is an assumption you can't verify. Codex is the project that stops
assuming.

- **Single-artifact substrate.** Boot `Codex.cdx`. There is no
  layer beneath it that you didn't compile yourself.
- **Literate by design.** Chapters and Sections aren't comments. They're
  structure. The compiler parses prose alongside code.
- **Own character encoding.** CCE (Codex Character Encoding) is
  frequency-sorted: `is-letter` is one comparison, not a table lookup.
  Unicode only at I/O boundaries.
- **Multiple backends.** The same compiler can target managed runtimes
  (C#, IL, JavaScript, Wasm) and native code (x86-64 bare metal, CDX).
  The bare-metal target is the perf and trust target; the others are
  ergonomic.
- **Algebraic effects.** Side effects are declared in types and handled
  explicitly. No surprise mutations.
- **Capability model.** Trust lattice, direction markers, and scoped
  capabilities designed in from the substrate, not bolted on after.

```codex
Chapter: Greeting
  cites Foreword chapter Console

  A small program that greets the user by name. The opening declares
  [Console] in its type — the effect is part of the contract, not a
  surprise that happens at runtime.

Section: Functions

  greet : Text -> Text
  greet (name) = "Hello, " & name & "!"

  opening : [Console] Nothing = act
    print-line "What is your name?"
    name <- read-line
    print-line (greet name)
  end

Page 1
```

---

## Quick Start

**Prerequisites**: `codex-vm.exe` (build via `tools/build-vm.ps1`) or
QEMU (with WHPX) for the bare-metal path.

```powershell
# Sample battery (bare-metal selfhost, ~2-5s per sample; -Jobs N for parallel)
build/test.ps1 -Jobs 4

# Full build: text round-trip + CDX fixed-point + test battery
build/build.ps1
```

### Try it without building (just codex-vm + the seed CDX)

The CDX in `seed/Codex.cdx` is a complete compiler. Boot it under
codex-vm, feed it source via a file, and it hands back CDX or
Codex-text — the output format is selected by the mode line.
Container formats (ELF, PE, GPT/FAT images) are produced by plug
CDX binaries in `codex/plugs/`. The `compile.ps1` helper wraps
the protocol:

```powershell
# Stage the seed where the helper expects it.
New-Item -ItemType Directory -Force build-output/bare-metal
Copy-Item seed/Codex.cdx build-output/bare-metal/Codex.cdx

# Compile a sample (boots codex-vm, loads source into guest memory, captures output).
build/compile.ps1 -Src codex/test/arithmetic.codex -Out build-output/arith.cdx -Log build-output/arith.log

# Boot the compiled program and capture its output.
build/test-run.ps1 -Kernel build-output/arith.cdx -OutFile build-output/arith.out
Get-Content build-output/arith.out
```

Memory-mapped I/O: source is pre-loaded into the guest ring buffer
at boot; output is captured from guest UART writes. No TCP sockets.

---

## Language Examples

The following is real Codex source — the same syntax the compiler parses.

```codex
Chapter: Feature Tour
  cites Foreword chapter Console

Section: Sum Types

  Shape =
    | Circle (Integer)
    | Rectangle (Integer) (Integer)

Section: Records

  Person = record {
    name : Text,
    age : Integer
  }

Section: Mutable Records

  mutable Counter = record {
    value : Integer
  }

  increment : Counter -> Counter
  increment (c) =
    c.value = c.value + 1
    c

Section: Pattern Matching

  area : Shape -> Integer
  area (s) = when s
    is Circle (r) -> r * r * 3
    is Rectangle (w) (h) -> w * h

  classify : Integer -> Text
  classify (n) = when n
    is 0 -> "zero"
    is 1 -> "one"
    is otherwise -> "other"

Section: Effects and Act Blocks

  greet : Text -> [Console] Nothing
  greet (name) = act
    print-line ("Hello, " & name & "!")
  end

  ask-name : [Console] Text
  ask-name = act
    print-line "What is your name?"
    result <- read-line
    when result
      is Just (name) -> name
      is None -> "stranger"
  end

Section: Effect Handlers

  effect Counter where
    tick : [Counter] Integer

  counted : Integer
  counted = with Counter (tick + tick + tick)
    tick (resume) = resume 1

Section: Resilient Act Blocks

  fetch-config : [Console, FileSystem] Text
  fetch-config = trying 3 times
    act
      content <- read-file "config.cdx"
      content
    end
  falling back to
    act
      print-line "Using default config"
      "{}"
    end
  on failure
    act
      print-line "All attempts failed"
      ""
    end

Section: Polymorphism

  identity : f -> f
  identity (x) = x

Section: Multi-Parameter Types

  add : Integer, Integer -> Integer
  add (x) (y) = x + y

  apply : (Integer, Integer -> Integer), Integer, Integer -> Integer
  apply (f) (x) (y) = f x y

Section: Linear Types

  open-file : Text -> [FileSystem] linear FileHandle
  close-file : linear FileHandle -> [FileSystem] Nothing

  freeze : linear a -> a

Section: Database Queries (Codex.DB)

  employees-table : TableDef
  employees-table = table-def-with-pk "employees" [
    col-def-not-null "id" ColInteger,
    col-def-not-null "name" ColText,
    col-def-not-null "department" ColText,
    col-def "salary" ColInteger
  ] ["id"]

  engineering-roster : Catalog -> QueryResult
  engineering-roster (cat) =
    RelScan "employees"
      |> RelFilter (PredColCmp "department" CmpEq (ValText "Engineering"))
      |> RelProject (proj-columns ["name", "salary"])
      |> RelSort [SortSpec { sort-col = "salary", sort-dir = SortDesc }]
      |> query cat

  department-summary : Catalog -> QueryResult
  department-summary (cat) =
    RelScan "employees"
      |> RelGroup ["department"] [
        AggSpec { agg-func = AggCount, agg-alias = "headcount" },
        AggSpec { agg-func = AggSum "salary", agg-alias = "total-salary" }
      ]
      |> query cat

Section: System Database (SystemDb)

  SysBootConfig = record {
    sbc-boot-mode : Integer,
    sbc-boot-drive-serial : Text,
    sbc-boot-partition : Integer
  }

Section: Lazy Evaluation

  expensive-computation : Integer -> Integer
  expensive-computation (x) = x * x * x + 7

  opening : [Console] Nothing = act
    let thunk = lazy (expensive-computation 42)
    in let r1 = force thunk
    in let r2 = force thunk
    in print-line (show r1 & " " & show r2)
  end

Section: Type Classes

  class Showable where
    to-text : Integer -> Text

  instance Showable Integer where
    to-text (x) = show x

Section: Web Emitter (HTML/JS Plugs)

  effect UI where
    render : Widget -> [UI] Nothing

  dashboard : [Console, UI] Nothing = act
    let chart = Charts.bar-chart sales-data
    in render (Window "Dashboard" (layout-vertical [chart, status-bar]))
  end

Section: Proofs and Dependent Types

  nil-eq : Nil === Nil
  nil-eq = Refl

  sym-proof : Nil === Nil
  sym-proof = sym Refl

  chain-proof : Nil === Nil
  chain-proof = trans Refl Refl

  wrap-proof : Proof
  wrap-proof = assume

Page 1
```

### Linear types and safe mutation

Codex pulls apart two ideas that most languages tangle together:
**`linear` is for resources, `mutable` is for data** — two orthogonal
uniqueness disciplines, neither implying the other.

A `linear` value must be **used exactly once** along every path. It can't
be silently dropped — a file you forgot to close is a leak (CDX2063) — and
it can't be used twice — a handle you closed and then read is a
double-use (CDX2061). Every mention counts. This is the discipline for
resources with a lifecycle: file handles, sockets, capabilities.

```codex
  consume : linear Integer -> Integer
  consume (n) = n * 2          -- OK: used exactly once
  -- leak (n)  = 0             -- REJECTED CDX2063: linear value never used
  -- twice (n) = n + n         -- REJECTED CDX2061: used twice
```

A `mutable` record is the other face of uniqueness: **data you own and
update in place**. You may read its fields as often as you like, but you
may not *alias* it — handing the same record to two owners is a compile
error (CDX2062). In-place field assignment (`r.field = v`) is safe
*precisely because* the record is uniquely owned: no GC, no copy, no
hidden sharing.

```codex
  mutable GameState = record { turn : Integer, score : Integer }

  add-score : mutable GameState, Integer -> mutable GameState
  add-score (gs) (points) =
    gs.score = gs.score + points
    gs.turn = gs.turn + 1
    gs
```

`freeze : linear a -> a` is the one-way door between the two worlds. It
consumes a uniquely-owned value and hands back an ordinary immutable one
that can be shared freely. Because the source is unique and is spent here,
no copy is needed — `freeze` is the identity, and its whole meaning lives
in the type.

Borrow-vs-move is inferred from signatures: pass a mutable record to a
function that only reads it (returns a plain value) and you *borrow* it;
pass it to one that threads it onward and you *consume* it. The compiler
itself is the proof — its hottest state records (the type-checker's
unification state, the lexer, the name-resolver scope) carry the `mutable`
discipline, and the self-compile reports zero aliasing violations.

### Type classes

`class`/`instance` give ad-hoc polymorphism through dictionary passing,
fully resolved at compile time — zero runtime dispatch:

```codex
  class Showable where
    to-text : Integer -> Text

  instance Showable Integer where
    to-text (x) = show x
```

Multiple instances, **return-type polymorphism** (the result type selects
the instance), generic functions constrained by a class, and instances
over parametric types all work. A missing instance is a static error
(CDX2040), never a runtime crash.

### Pattern matching: multi-pattern arms and exhaustiveness

One `when`/`is` arm can match several shapes with `|`, and the compiler
checks that every match is **exhaustive** — a forgotten constructor is a
compile error, not a silent fall-through.

```codex
  describe : Shape -> Text
  describe (s) = when s
    is Circle (r) | Rectangle (w) (h) -> "has area"
    -- dropping a constructor here is a static non-exhaustiveness error
```

### Proofs and dependent types

Codex has dependent types — types that carry values. The `===` operator
in type position creates a *propositional equality type*. The proof term
`Refl` inhabits `a === a` for any `a`; the unifier rejects it when both
sides differ.

```codex
  nil-eq : Nil === Nil
  nil-eq = Refl              -- TYPE-CHECKS: both sides are Nil

  -- bad : Nil === Cons
  -- bad = Refl              -- REJECTED: Nil /= Cons
```

Proof terms: `Refl` (reflexivity), `sym` (symmetry: `a === b -> b === a`),
`trans` (transitivity: `a === b -> b === c -> a === c`), `assume` (axiom).
All proofs are **erased at emit time** — they generate zero machine code.
The `claim`/`proof`/`qed` syntax declares and discharges proof obligations:

```codex
  claim id-nil : Nil === Nil
  proof id-nil = Refl
  qed

  claim chain : 5 === 5
  proof chain = trans Refl Refl
  qed
```

The compiler also statically proves bounded-integer range safety. When it
can prove a value fits within a field's declared bounds, the runtime check
is elided (CDX4010). The bounds prover handles literals, field access,
arithmetic, `int-mod`, `bit-and`, `bit-shru`, negation, if/else, and
let-bound values.

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
    → Resolve       ConstructedTy → concrete RecordTy/SumTy (CDX path)
    → LambdaLifting nested lambdas → top-level defs (CDX path)
    → Emitter       target source code / machine code
```

The pipeline lives in `codex/` — the self-hosted compiler,
~25,000 lines across 54 `.codex` files. Each phase has its own deck
allocation and `phase-compact` cycle; cumulative deck ~208 MB, peak
working set ~210 MB for selfhost. This is the only path that is
maintained, exercised, and load-bearing.

The original C# reference implementation under `old/src/` was the
scaffolding that bootstrapped the language. It is **retired** and
remains in the depot as historical record only.

---

## Backends

| Backend | Status | Role |
|---------|--------|------|
| **CDX binary** | **Full support** | **Self-sustaining target.** Signed, verified, bootable. The canonical seed. |
| **Codex-text** | **Full support** | Bootstrap 2. Re-emits self as Codex source. |
| **x86-64 bare metal (ELF)** | **Full support** | Derived from CDX. Maintained less frequently. |
| **ARM64 (AArch64)** | Early | ELF64 plug, QEMU virt board, Thumb-2 encoder, 29 runtime functions. Cross-arch test 8/10 content lines. |
| **RISC-V (RV32IMC/RV64)** | Early | ELF plug, QEMU virt board, RV32C compressed. Cross-arch test 10/10 perfect. |
| C#, .NET IL, JS, Wasm, others | Legacy | Research/sample targets, may not track current features. |

CDX and Codex-text are the load-bearing pair. The ELF is a derived artifact.

---

## CCE — Codex Character Encoding

Codex has its own character encoding (128 codepoints, one byte each),
frequency-sorted for computation:

| Range | Category | Count |
|-------|----------|------:|
| 0-2 | Whitespace | 3 |
| 3-12 | Digits | 10 |
| 13-38 | Lowercase (frequency-sorted) | 26 |
| 39-64 | Uppercase (frequency-sorted) | 26 |
| 65-96 | Punctuation | 32 |
| 97-112 | Accented Latin | 16 |
| 113-127 | Cyrillic | 15 |

Character classification is arithmetic, not table lookup:
- `is-letter (c)` = `char-code c >= 13 && char-code c <= 64`
- `is-digit (c)` = `char-code c >= 3 && char-code c <= 12`
- `to-upper (c)` = `code-to-char (char-code c + 26)` (case shift is `+26`)

Unicode exists only at I/O boundaries. Internally, everything is CCE.

---

## Library Quires

Code outside the compiler is organized into **24 quires** (library namespaces)
with **331 library modules** (238 foreword + 93 OS); **1,126 modules** in the depot including the 54-file compiler, 113 plug files, and 628 application modules:

| Quire | Directory | Count | Highlights |
|-------|-----------|------:|------------|
| **Foreword** | `codex/foreword/core/` | 91 | Hamt, Sort, PriorityQueue, Trie, LruCache, UnionFind, Graph, B+Tree, Deque, Rope, IntervalTree, ConsistentHash, BloomFilter, Regex, DateTime, Ed25519, SHA-256/512, CCE, MathLib, Path, Format, Hkdf, NumberTheory, Probability, Locale |
| **Game** | `codex/foreword/game/` | 26 | A*, Dijkstra, DiamondSquare, HexMap, Voronoi, FloodFill, Octree, Quadtree, Bresenham, CellularAutomata, ECS, StateMachine, Tween, TileMap, CardDeck, Rasterizer, Sprite, Scene2D, Color, Raytracer, Klondike, Camera |
| **AI** | `codex/foreword/ai/` | 19 | Tensor, NeuralNet, Transformer, GGUF, SparseLattice, KNN, DecisionTree, GeneticAlgorithm, Tokenizer, KvCache, Sampling, Optimizer, Attention, Embedding, Loss, DiffusionScheduler |
| **UI** | `codex/foreword/ui/` | 28 | Theme (3 built-in), Widget, Layout, Render, Surface, Event, Binding, Animation, Icon (5 sizes), Overlay, Sound, Font (CCE), Cursor, Scroll, Focus, Dialog, Orchestrator, Selection, TextField, Clipboard, RichText, Charts, Accessibility |
| **Signal** | `codex/foreword/signal/` | 14 | FFT, Perlin, Convolution, ADSR Envelope, Resample, Wavelet, Pitch |
| **Compress** | `codex/foreword/compress/` | 8 | LZ77, Huffman, RLE, Deflate, Gzip, Lz4, Zstd, Brotli |
| **Encode** | `codex/foreword/encode/` | 32 | JSON, Base64, Hex, URI, UUID, CSV, CRC32, Protobuf, Toml, Cbor, Yaml, MessagePack, Bencode, GrayCode |
| **Math** | `codex/foreword/math/` | 12 | Quaternion, Matrix4, Bezier, CORDIC, Complex, Spline, Geodesic, LinearAlgebra, Numeric, Decimal |
| **Sim** | `codex/foreword/sim/` | 7 | Verlet Physics, Collision, ParticleSystem, Steering, SpatialHash |
| **Net** | `codex/os/net/` | 16 | Ethernet, ARP, IPv4, TCP, UDP, ICMP, DNS, DHCP, NTP, Syslog, TFTP, HttpClient, Tls (AesGcm + X25519) |
| **Kernel** | `codex/os/kernel/` | 22 | DiskFacts, DriveManager, Vga, VgaGraphics, Pci, Keyboard, Mouse, BitmapFont, Console, DiagnosticShell, GpuBridge, IdentityManager, Ivshmem, Ne2k, SystemDb, Usb, UsbAudio, UsbMassStorage, UsbVideo, Xhci, VmSerial, VmIde |
| **OS** | `codex/os/*/` | 81 | Trust lattice, verifier, scheduler, IPC, identity, shell, clarifier, replay, observability, dev tools |
| **Works** | `apps/works/` | 54 | DevConsole, UefiConsole, ConsoleEditor, FirstBoot, AgentRuntime, AgentCoordinator, AgentAcquisition, VmCompile, VmPingpong, VmSweep, Http, WebServer, AnnotationDriver |
| **Spark** | `apps/spark/` | 89 | 3D modeling, software rasterizer, image editor, animation/skeletal IK, audio/DAW, video compositor, procedural noise, interactive GOP framebuffer UI |
| **Games** | `apps/games/` | 128 | CodexMagic card game engine, classic board games, game server, AI opponents, web portal |
| **Data** | `apps/data/` | 42 | Relational database server, B-tree indexes, query planner, WAL, transactions, deadlock detection |

Quires cite each other via `cites Game chapter AStar` or
`cites Net chapter Tcp`. The quire name is the last segment
of the directory name, capitalized.

---

## Applications

**628 modules across 46 apps**, all written in Codex, all compiled by the
seed. Web apps serve generated HTML/JS from CDX servers; WASM apps
compile through the WASM plug; bare-metal apps render via the GOP
framebuffer UI foreword.

### Productivity and platform

| App | Modules | What it does |
|---|---:|---|
| **CVMM** | 66 | OS desktop environment: window manager, system monitors, productivity suite, sync providers |
| **Works** | 54 | Dev platform: DevConsole, UEFI console, editor, first-boot wizard, agent runtime/coordinator, HTTP server |
| **Explorer** | 25 | Parameter explorer: card/character/item designers, settings, workflow exporter |
| **Browser** | 22 | Content-addressed browser: trust model, media player, data channels |
| **Helm** | 12 | Operations bridge console: cluster view, mixer, attention routing, voice |
| **Collab** | 8 | Collaborative editing |
| **Secrets** | 8 | AES-GCM vaults, PBKDF2, hash-chained audit, team sharing, generator |
| **FileShare** | 8 | Merkle-verified pieces, Kademlia DHT, Ed25519 announces, rarest-first |
| **Diagram** | 13 | Flowchart/ERD/UML/network/state-machine editor with routing and undo |
| **Vision** | 13 | Computer-vision pipeline |
| **NetTool** | 6 | Packet analyzer, port scanner, group admin |
| **Workflow** | 4 | State-machine engine: SLA, doc gates, audit; title/claims/mortgage flows |
| **Designer** | 4 | WYSIWYG UI builder rendering through WASM |

### Business

| App | Modules | What it does |
|---|---:|---|
| **ERP** | 23 | GL, AP/AR, Materials, HR, Treasury, Controlling, Sales, Production, Maintenance, Quality, Warehouse, Projects, BW analytics; Real Estate / Banking / Insurance / Utilities / Healthcare verticals |
| **Market** | 17 | E-commerce: products, cart/tax/payment, shipping, coupons, reviews, digital goods, bundles, subscriptions, auctions (4 types), merchants, drop-shipping, affiliates |
| **Data** | 42 | Relational DB server: MVCC, WAL, B-trees, query planner, replication, column/graph/time-series stores |
| **Services** | 5 | System services: TimeService (RTC+NTP+HPET, TOTP), Revocation (trust-lattice evidence) |

### Creative and games

| App | Modules | What it does |
|---|---:|---|
| **Spark** | 89 | Creative suite: 3D modeling, CAD workbench, image editor, animation/IK, audio/DAW, video compositor, UV editor -- WebGPU at 120fps via WASM |
| **Games** | 128 | CodexMagic card platform (engine, server, web portal, clans, marketplace, parental controls), classic board games, AI opponents |
| **CodexMagic Mobile** | 8 | Mobile client |
| **MathBook** | 17 | Symbolic CAS: calculus, number theory, circuits, proofs, statistics |
| **FishTank** | 14 | Boids AI (1000 fish), particles, WebGPU, WASM |
| **Globe** | 8 | 11 live data feeds + GIS: road network, routing, POIs, geocoding |
| **Star Atlas** | 7 | Planetarium: 125 deep-sky objects, constellation figures |
| **Radio** | 3 | DJ console: dual decks, mixer, Web Audio |
| **Piano** | 1 | Playable piano |

### Page apps (WebApp template)

Twenty single-purpose web apps on the shared WebRuntime/WebTheme/
WebWidgets quire: news, podcasts, books, recorder, capture, publisher,
imagetools, fitness, pomodoro, markets, calendar, chat, mail, music,
notes, weather, tasks, photos, and maps.

---

## Project Structure

```
codex/
  compiler/               Self-hosted compiler (54 files, ~25K lines)
  foreword/
    core/                 Core forewords — data structures, crypto (89 modules)
    ai/                   AI — tensors, neural nets, GGUF, transformer (19 modules)
    compress/             Compression — LZ77, Huffman, RLE, Deflate, Zstd, Brotli (8 modules)
    encode/               Encoding — JSON, Base64, Protobuf, Toml, Cbor, Yaml (32 modules)
    game/                 Game — A*, hex, ECS, physics, terrain (26 modules)
    math/                 Math — quaternions, matrices, Bezier, CORDIC (12 modules)
    signal/               Signal — FFT, Perlin, convolution, wavelet (14 modules)
    sim/                  Simulation — physics, collision, particles (7 modules)
    ui/                   UI — themeable widgets, layout, compositor (28 modules)
  os/
    core/                 OS core — shell, registry, clarifier (4 modules)
    dev/                  Developer tools — debugger, inspectors (5 modules)
    kernel/               Kernel — disk, VGA, USB, PCI, audio, video (22 modules)
    net/                  Networking — full TCP/IP + protocols (16 modules)
    observe/              Observability — metrics, health, journal (7 modules)
    replay/               Replay — deterministic record/replay (3 modules)
    sched/                Scheduler — process groups, watchdog (6 modules)
    trust/                Trust — lattice, policy, sessions (11 modules)
    verify/               Verification — 5-phase CDX verifier (5 modules)
  plugs/                  Plug architecture — IR-text-driven emitters
  test/                   Compiler samples + OS integration tests (211 gate + 400 app tests)
apps/                     46 applications, 628 modules (see Applications)
  works/                  Console, agents, VM tools, first boot (54 modules)
  games/                  CodexMagic — card platform, classic games, web portal (128 modules)
  spark/                  Codex.Spark — 3D, CAD, image, animation, audio, video (89 modules)
  cvmm/                   OS desktop environment (66 modules)
  data/                   Codex.DB — relational database server (42 modules)
  erp/                    ERP suite + industry verticals (23 modules)
  browser/ explorer/ market/ mathbook/ fishtank/ vision/ helm/ diagram/
  secrets/ fileshare/ collab/ globe/ starmap/ workflow/ nettool/ radio/
  webapp/ + 20 page apps (chat, mail, music, notes, weather, tasks, ...)
annotations/              On-disk annotation sidecars (JSON facts)
build/                    Build/test harness (PowerShell)
tools/                    codex-vm, status server, USB writer, VS extensions
seed/                     Bootstrap seed CDX (~2.1 MB) + UEFI disk image (8 MB)
docs/                     Design documents, plans, stories
old/                      Retired C# reference compiler — historical only
```

---

## The Road

| Milestone | What | Date |
|-----------|------|------|
| Foundation | Reference compiler in C#, type system, IR, transpiler backends | 2026-03-14 |
| Self-hosting (BS1) | Fixed point — stage 1 === stage 3 | 2026-03-16 |
| Bare metal | x86-64 ELF on bare-metal VM, no OS, no libc | 2026-03-23 |
| Pingpong (BS2) | Bare-metal semantic equivalence | 2026-04-07 |
| **Self-sustaining (BS3)** | **Bare-metal CDX reproduces itself byte-identical** | **2026-04-24** |
| CDX binary format | Signed CDX with SHA-256, capability tables, effect metadata | 2026-04-30 |
| Codex.OS kernel | Preemptive scheduler, IPC, identity, trust lattice, 5-phase verifier | 2026-05-03 |
| **Networking** | **Full TCP/IP: Ethernet, ARP, IPv4, TCP, UDP, ICMP, DNS, DHCP, NTP, TLS** | **2026-05-05** |
| **Real hardware boot** | **"Welcome to Codex" on Asus x86-64 — UEFI PE stub, pure-PS1 toolchain** | **2026-05-07** |
| **codex-vm** | **WHP VM host (~4500 lines C): PCI, xHCI USB, Intel HDA, HPET, IOAPIC, ACPI, SMBIOS, Bochs VBE, GOP framebuffer, NE2K NIC** | **2026-05-07** |
| **Plug architecture** | **48 transpiler plugs (Ada → Zig, 14 UI frameworks, 4 binary formats)** | **2026-05-09** |
| **Codex.Spark** | **85-module creative suite on GOP framebuffer** | **2026-05-18** |
| **Static bounds prover** | **Compiler proves bounded-integer range safety, elides runtime checks** | **2026-05-23** |
| **Dependent types** | **PropEqTy, Refl, proof erasure, claim/proof/qed** | **2026-05-23** |
| **Type classes** | **`class`/`instance` via dictionary passing, return-type polymorphism** | **2026-05-29** |
| **Linear types** | **`linear` (resources) + `mutable` (data) as orthogonal disciplines** | **2026-05-29** |
| **Tuples + debugger** | **`(A, B)` sugar, `let (x, y) = e`, C# full-compiler emit (0 errors), interactive debugger** | **2026-05-31** |
| **For-exprs + phase heap** | **`for x in xs do f x` sugar, CHECK/LOWER heap reduction (~80 MB saved), EOF settle counter, 201/211 tests pass** | **2026-06-02** |
| **x86-64 codegen optimization** | **Comparison folding, preamble elision, store-load elimination, immediate ops, single-arg mov — fib(35) cut from 107 to 53 instructions. WASM backend + WebGPU 3D. Spark Studio. CodexMagic web platform.** | **2026-06-06** |
| **Native-class codegen** | **TCO parallel-move shuffle, R8/R9-staged operands, leaf/near-leaf frame elision, IrRemInt + inliner — sum 14 insns (beats C /O2), fact 17, fib 23, gcd 23. Self-verifying fixed point at every step.** | **2026-06-10** |
| **Application wave** | **628 app modules across 46 apps: ERP suite + 5 verticals, Market e-commerce, Browser, FileShare, Secrets, Diagram, Globe GIS, Star Atlas, MathBook CAS, CVMM desktop, mesh OS (Raft/SWIM), 20 page apps on WebApp template.** | **2026-06-10** |
| **punctual + unit types + cross-arch** | **Per-function bounded-execution keyword (novel -- no production language has this). Unit types with zero-overhead erasure. ARM64 + RISC-V backend plugs (Hello World on QEMU). IoT protocol stack (MQTT v5, CoAP). Test consolidation (232 -> 137 tests, BVT in 113s).** | **2026-06-13** |

Full detailed milestone history: [docs/PM/Milestones.md](docs/PM/Milestones.md)

---

## Documentation

- [docs/VisionAndVirtues.md](docs/VisionAndVirtues.md) — **Read this first**
- [docs/PM/CurrentPlan.md](docs/PM/CurrentPlan.md) — Active plan and direction
- [docs/DevelopersGuide.md](docs/DevelopersGuide.md) — Language syntax, types, how to write Codex
- [docs/UsersHandbook.md](docs/UsersHandbook.md) — Boot the IMG, first steps, using the system
- [docs/OperatorsManual.md](docs/OperatorsManual.md) — Build process, test harness, VM setup, debugging
- [docs/ArchitectsSketchbook.md](docs/ArchitectsSketchbook.md) — Memory layout, registers, allocators, phase maps
- [docs/DevelopersRulebook.md](docs/DevelopersRulebook.md) — Foreword quire catalog, library rules
- [docs/ExaminersAssay.md](docs/ExaminersAssay.md) — Test infrastructure, coverage, known results
- [docs/TheShimmeringPortal.md](docs/TheShimmeringPortal.md) — Web developer's guide to the UI-to-browser pipeline

---

## Apps

44 applications built on the Codex stack, from full database servers to
single-file UI prototypes. Each has a `README.md` with module inventory,
completeness estimate, and conformance assessment.

**Conformance key**: *Full* = pure Codex, emits through plugs for any
external format. *Partial* = mostly Codex but has hand-written non-Codex
client code or unconnected wiring gaps.

### Flagship (75-90%)

| App | What | Complete | Conform |
|-----|------|:--------:|:-------:|
| [data](apps/data/) | Multi-model database server (OLTP, OLAP, graph, spatial, time-series, full-text) | 90% | Full |
| [games/classic](apps/games/) | 35 classic board and card games with AI opponents | 90% | Full |
| [workflow](apps/workflow/) | Long-running business process engine with industry templates | 85% | Full |
| [diagram](apps/diagram/) | Diagramming editor (flowcharts, ERDs, UML, state machines) with undo/redo | 82% | Full |
| [designer](apps/designer/) | WYSIWYG UI widget tree builder compiled to WASM | 80% | Full |
| [helm](apps/helm/) | Scalable comms: The River (auto-clustering chat) + The Bridge (ranked voice) | 78% | Full |
| [games/codexmagic](apps/games/) | Collectible card game platform: economy, clans, dungeons, multiverse registry | 75% | Full |
| [cvmm](apps/cvmm/) | Desktop management shell: system managers, fleet/mesh, 11 productivity mini-apps | 75% | Partial |
| [collab](apps/collab/) | Video collaboration: calls, screen share, meetings, whiteboard, recording | 75% | Full |
| [mathbook](apps/mathbook/) | Symbolic CAS and interactive math notebook (parse, simplify, differentiate, integrate) | 75% | Full |

### Substantial (60-70%)

| App | What | Complete | Conform |
|-----|------|:--------:|:-------:|
| [explorer](apps/explorer/) | World-building and game-asset design suite with CDX server | 70% | Full |
| [fishtank](apps/fishtank/) | WebGPU aquarium: boids AI, procedural 3D fish, volumetric lighting, particles | 70% | Partial |
| [nettool](apps/nettool/) | Network admin toolkit: packet capture, port scanning, mesh admin | 70% | Full |
| [browser](apps/browser/) | Bare-metal web browser: content-addressed pages, capability tiers, HDA audio | 65% | Full |
| [erp](apps/erp/) | Enterprise resource planning: GL, AP/AR, HR, manufacturing, compliance | 65% | Full |
| [market](apps/market/) | Self-hosted e-commerce: catalog, cart, checkout, multi-vendor, auctions | 65% | Partial |
| [secrets](apps/secrets/) | Encrypted password manager: AES-GCM vaults, PBKDF2, team sharing via DH | 65% | Full |
| [spark](apps/spark/) | Creative suite: 3D modeling, image editing, animation, audio/DAW, video | 65% | Full |
| [vision](apps/vision/) | Organizational intelligence: weighted signal cascade, portfolio health | 65% | Full |
| [chat](apps/chat/) | E2E-encrypted messaging with Signal-style interface | 65% | Partial |
| [starmap](apps/starmap/) | 3D star map: 80+ celestial objects, constellations, spatial DB, WASM/WebGPU | 65% | Full |
| [globe](apps/globe/) | Earth visualization with 16 data overlays and Dijkstra turn-by-turn routing | 60% | Partial |
| [fileshare](apps/fileshare/) | P2P file sharing: Merkle pieces, Kademlia DHT, Ed25519 announce, tit-for-tat | 60% | Full |
| [works](apps/works/) | Developer environment: UEFI console, repo protocol, build system, AI agents | 60% | Full |
| [markets](apps/markets/) | Financial data dashboard: stocks, bonds, commodities, watchlist | 60% | Full |

### Growing (50-59%)

| App | What | Complete | Conform |
|-----|------|:--------:|:-------:|
| [codexmagic-mobile](apps/codexmagic-mobile/) | .NET MAUI companion app for CodexMagic | 55% | Partial |
| [services](apps/services/) | Shared system services: time, revocation, parental controls | 55% | Full |
| [calendar](apps/calendar/) | Month/Week/Day/Agenda views with mini-calendar | 55% | Full |
| [tasks](apps/tasks/) | Five-column Kanban board with priority-colored cards | 55% | Partial |
| [radio](apps/radio/) | Two-deck internet radio station with mixer/crossfader/EQ | 55% | Full |
| [notes](apps/notes/) | Two-pane note-taking app with folder sidebar | 55% | Partial |
| [mail](apps/mail/) | Three-pane email client with folder sidebar | 50% | Full |
| [pomodoro](apps/pomodoro/) | Focus timer with work/break modes and session tracking | 50% | Partial |

### Early (25-45%)

| App | What | Complete | Conform |
|-----|------|:--------:|:-------:|
| [news](apps/news/) | RSS/news-feed reader with categorized sidebar | 45% | Partial |
| [fitness](apps/fitness/) | Activity rings, weekly bar chart, workout history | 45% | Full |
| [books](apps/books/) | E-book reader with library shelf and reading view | 40% | Full |
| [music](apps/music/) | Spotify-style music player with album grid and player bar | 40% | Full |
| [photos](apps/photos/) | Photo gallery with grid, albums, and lightbox | 40% | Partial |
| [publisher](apps/publisher/) | Long-form content authoring with rich-text editor | 40% | Partial |
| [capture](apps/capture/) | Screenshot and annotation tool | 35% | Full |
| [imagetools](apps/imagetools/) | Image editing utility: crop, resize, rotate, adjust, export | 35% | Full |
| [piano](apps/piano/) | Two-octave virtual piano keyboard | 35% | Partial |
| [podcasts](apps/podcasts/) | Podcast client with subscription sidebar and playback bar | 35% | Partial |
| [recorder](apps/recorder/) | Voice recorder with waveform and recording library | 35% | Partial |
| [weather](apps/weather/) | Weather dashboard: current, hourly, 7-day, atmospheric details | 30% | Full |
| [maps](apps/maps/) | Map viewer with layer selector and info panel | 25% | Full |

---

## No Dates

Every estimate has been wrong by orders of magnitude, in both
directions. We don't put dates on mountains. The critical path is
ordered. That's all we need to know.

---

## Bootstrapping Freedom in 3 Easy Steps

*(j/k, this was hard but fun. It's also done, so you don't have to.)*

![Codex Bootstrap](docs/PM/CodexBootstrap.png)

---

## Kudos

To Anthropic and the Claude team — Codex's bootstrap was built with Claude
Opus 4.6/4.7 (1M context) running as a small team of parallel agents
under Claude Code. The 1M-token window made it tractable to review thousand-line
codegen diffs against IR invariants in a single pass. The Agent SDK's
parallel-agent model let multiple agents work distinct CLs simultaneously
without cross-contaminating their reasoning. The harness's permission model
and sandboxing made it safe to give the agents direct access to git, p4, WSL,
codex-vm, and gdb without supervising every command. Persistent memory across
sessions meant context compounded instead of evaporating between runs.

Forty-one days from project start to a self-sustaining bare-metal compiler is
not a thing one human plus one shell does. It's a thing one human plus a team
of disciplined agents does. Codex stands on the shoulders of the C# self-host,
which stands on the shoulders of Claude. Thank you.

---

## License

See repository for license details.
