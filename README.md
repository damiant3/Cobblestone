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

As of 2026-05-27:

- **CDX fixed point**: pingpong all phases green — text round-trip
  (stage1 === stage2) + CDX fixed-point (stage1.cdx === stage2.cdx),
  byte-identical. The compiler reproduces itself on bare metal.
- **357 library modules** across 19 quires: data structures, crypto,
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
- **Mutable records**: `mutable` keyword with in-place field
  assignment, type-checked immutability enforcement (CDX2060).
- **Repository restructure**: 31 top-level dirs to 8. codex-vm
  replaces QEMU as default VM (WHP-based, ~4500 lines C: PCI, xHCI USB,
  Intel HDA audio, HPET, IOAPIC, ACPI, SMBIOS, UEFI firmware, Bochs VBE).
- **Sample battery**: 172 samples (incl. 29 error tests); 119 pass,
  0 fail in the gate battery (52 skipped: slow, fatal, or platform-specific).
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

**`seed/Codex.cdx`** (2,267,679 bytes) — the canonical seed:

| Algorithm | Digest |
|---|---|
| SHA-256 (file) | `011DB01D75F8AAE4AD269C00A9AB29387F3877B51FD7D782913F65A2EF80B2EC` |

**`seed/Codex.img`** (8,388,608 bytes) — bootable GPT disk image:

| Algorithm | Digest |
|---|---|
| SHA-256 | `3233F3F1B183F6D655121398C058B45CA7BCD51A54434F8F3C0DCD4E430C2484` |

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

## Language Features

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
~29,300 lines across 54 `.codex` files. Each phase has its own deck
allocation and `phase-compact` cycle; cumulative deck ~228 MB, peak
working set ~252 MB for selfhost. This is the only path that is
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
with **367 modules** total (420 including the 53-file compiler):

| Quire | Directory | Count | Highlights |
|-------|-----------|------:|------------|
| **Foreword** | `codex/foreword/core/` | 89 | Hamt, Sort, PriorityQueue, Trie, LruCache, UnionFind, Graph, B+Tree, Deque, Rope, IntervalTree, ConsistentHash, BloomFilter, Regex, DateTime, Ed25519, SHA-256/512, CCE, MathLib, Path, Format, Hkdf, NumberTheory, Probability, Locale |
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
| **OS** | `codex/os/*/` | 57 | Trust lattice, verifier, scheduler, IPC, identity, shell, clarifier, replay, observability, dev tools |
| **Works** | `apps/works/` | 53 | DevConsole, UefiConsole, ConsoleEditor, FirstBoot, AgentRuntime, AgentCoordinator, AgentAcquisition, VmCompile, VmPingpong, VmSweep, Http, WebServer, AnnotationDriver |
| **Spark** | `apps/spark/` | 85 | 3D modeling, software rasterizer, image editor, animation/skeletal IK, audio/DAW, video compositor, procedural noise, interactive GOP framebuffer UI |
| **Games** | `apps/games/` | 56 | CodexMagic card game engine, classic board games, game server, AI opponents, web portal |
| **Data** | `apps/data/` | 38 | Relational database server, B-tree indexes, query planner, WAL, transactions, deadlock detection |

Quires cite each other via `cites Game chapter AStar` or
`cites Net chapter Tcp`. The quire name is the last segment
of the directory name, capitalized.

---

## Project Structure

```
codex/
  compiler/               Self-hosted compiler (59 files, ~30.5K lines)
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
  test/                   Compiler samples + OS integration tests (203 samples)
apps/
  works/                  Console, agents, VM tools, first boot (53 modules)
  spark/                  Codex.Spark — 3D, image, animation, audio, video (85 modules)
  games/                  CodexMagic — card game, classic games, web portal (56 modules)
  data/                   Codex.DB — relational database server (38 modules)
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
| Codex.OS kernel | Preemptive scheduler, IPC channels, process management | 2026-05-03 |
| Identity + crypto | RDRAND, Ed25519 identity, trust lattice, in-place keygen | 2026-05-03 |
| Verifier (5-phase) | Integrity, author, capabilities, effects, proofs; cache; verified loader | 2026-05-04 |
| **Networking** | **Full TCP/IP: Ethernet, ARP, IPv4, TCP, UDP, ICMP, DNS, DHCP, NTP, Syslog, TFTP** | **2026-05-05** |
| OS shell + VGA | Interactive REPL, 13 command types, VGA 80x25, colored boot | 2026-05-05 |
| Trust network | Authenticated TCP sessions, agent protocol, peer management | 2026-05-05 |
| C# emitter removed | Legacy emitter deleted. CDX-only pipeline. Seed shrunk to 1.74MB | 2026-05-05 |
| **176+ forewords** | **14 quires: game, AI, signal, encoding, math, compression, simulation** | **2026-05-05** |
| Developer debugger | Memory/IO inspectors, ATA debugger, perf monitor | 2026-05-05 |
| GPU compute design | Shared-memory proxy protocol, PCIe enumeration, F32 conversion | 2026-05-05 |
| **UI substrate** | **18-chapter themeable GUI: widgets, layout, compositor, events, bindings, animations, icons, font, orchestrator** | **2026-05-05** |
| UEFI boot path | PE32+ builder, CDX loader stub, diagnostic shell, LAPIC management | 2026-05-05 |
| Boot gate | Hold-any-key for diagnostic shell, welcome screen for normal boot | 2026-05-05 |
| DRY + list-push rename | Shared forewords (ListUtils, math-mod), IrNegate constant fold, list-snoc → list-push | 2026-05-06 |
| GPT/FAT32 writers | Native GPT + FAT32 in Codex, IMG compile mode, 64 MB bootable disk image | 2026-05-06 |
| **UEFI console** | **ConOut routing: print-line → screen on real hardware. UEFI app PE stub (no EBS)** | **2026-05-06** |
| Prose buildout | Load-bearing prose: consistency checks, banned words, flag-gated pipeline | 2026-05-06 |
| Agent lifecycle | AgentRuntime (GGUF loader), AgentAcquisition, AgentCoordinator, FirstBoot wizard | 2026-05-06 |
| **288 modules** | **19 quires, 40 works modules** | **2026-05-06** |
| **Real hardware boot** | **"Welcome to Codex" on Asus x86-64: PE stub alignment fix, ImageBase=0, pure-PS1 toolchain** | **2026-05-07** |
| bit-shr/shru | SAR (arithmetic) + SHR (logical) split, 78-file codebase migration | 2026-05-07 |
| Fat16 reader | Foreword for reading FAT16 filesystems | 2026-05-07 |
| **VMX hypervisor** | **codex-vm.exe (WHP), VmSerial, VmIde, DevHypervisor — replaces QEMU** | **2026-05-07** |
| VM build tools | VmCompile, VmRunner, VmPingpong, VmSweep — Codex-native build pipeline | 2026-05-07 |
| Source embedding | FAT16 8MB IMG with SOURCE.CDX, SourceConcat transitive resolution | 2026-05-07 |
| codex-vm.exe | WHP-based VM host: PIT, serial, disk — replaces QEMU | 2026-05-07 |
| rdmsr/wrmsr builtins | MSR access + vmlaunch-full/vmresume-full | 2026-05-07 |
| **UEFI dev console** | **Interactive menus, source indexing, ConOut/ConIn, write-usb** | **2026-05-07** |
| FAT16 IMG OOM fix | Emit deck reclaim: peak heap ~990 to ~350 MB | 2026-05-07 |
| **295 modules** | **19 quires, 44 works, 212 test samples** | **2026-05-07** |
| Pip joins | Third Claude Code agent (Cam, Nib, Pip) | 2026-05-08 |
| Annotations H1-H12 | First-class fact-publication surface for AI agents (codex.annotations/) | 2026-05-08 |
| Frontend de-deck | Lex / parse / desugar / scope phases move scratch to bivy; tighter heap surveys | 2026-05-08 |
| **Resilient act blocks** | **`trying N times … falling back to … on failure`: retry loops with bivy reclaim, fallback, failure handler** | **2026-05-09** |
| **Plug architecture** | **Emitters as standalone CDX programs reading IR text on stdin; first plug = C#** | **2026-05-09** |
| Diagnostic dangler fix | `make-diagnostic` deck-copies message text; `bag-add` deck-records the bag — emit-time errors no longer dangle past `__heap-restore` | 2026-05-09 |
| Library expansion | 295 → 352 modules (+57: Path, Hkdf, Locale, Transformer, Toml, Cbor, Decimal, Argon2, Zstd, Brotli, Wavelet, ...) | 2026-05-09 |
| **352 modules** | **19 quires, 52 works, 401 test samples** | **2026-05-09** |
| New syntax | `&` replaces `++` for concat; comma-separated params (`Integer, Integer -> Integer`) | 2026-05-13 |
| Syntax conversion | Entire codebase (compiler + 352 library modules + 245 tests) converted | 2026-05-15 |
| 2GB address space | DEV_2GB_SYNTAX branch: 2 GB identity-mapped, 2 MB page tables | 2026-05-13 |
| Memory layout fix | Serial ring buffer collision discovered and fixed (0x300000→0x500000) | 2026-05-16 |
| **New builtins** | **print-line-raw, read-file-raw, chan-text-send, chan-text-recv + IPC helpers** | **2026-05-16** |
| Test harness overhaul | Crash recovery, per-test timing, batch parallelism, 3-min full sweep | 2026-05-16 |
| Editor features | Find/replace, undo, go-to-line, multi-file buffers | 2026-05-16 |
| **357 modules** | **19 quires, 59 compiler files, 581 test samples** | **2026-05-16** |
| codex-vm NE2000 NIC | Network-enabled VM, VGA display, keyboard, mouse | 2026-05-18 |
| codex-vm UEFI emulation | ConOut/ConIn trap dispatch, ReadKeyStroke, AllocatePages | 2026-05-18 |
| Compiler: pipe-forward | `\|>` operator and `.field` record selectors | 2026-05-18 |
| **Codex.Spark** | **85-module creative suite: 3D modeling, image editor, animation, audio/DAW, video compositor, procedural gen, interactive UI shell on GOP framebuffer** | **2026-05-18** |
| **codex-vm GOP** | **Graphics Output Protocol framebuffer — Spark renders 3D on screen** | **2026-05-18** |
| Codex.DB | Relational database server (38 modules) with pipe-forward queries | 2026-05-18 |
| CodexMagic | Card game + game server with web portal (56 modules) | 2026-05-18 |
| Mutable records | `__record-set-mut` for in-place mutation under linear ownership | 2026-05-18 |
| **575 modules** | **24 quires, 59 compiler files, 581+ test samples** | **2026-05-18** |
| Emitter Exodus | PE, ELF, GPT, FAT writers extracted from compiler to plug CDX binaries | 2026-05-23 |
| **codex-vm hardware** | **PCI, xHCI USB (mass storage + HID + UVC camera), Intel HDA audio, HPET, IOAPIC, ACPI, SMBIOS, Bochs VBE, PC speaker — VM grows from 400 to ~4500 lines** | **2026-05-23** |
| UsbVideo.codex | USB Video Class kernel driver: discovery, Probe/Commit, YUYV-to-RGB, framebuffer blit | 2026-05-23 |
| Hardware tests | 5 new test samples: pci-scan, xhci-discover, uvc-discover, usb-msc-detect, hda-codec | 2026-05-23 |
| xHCI transfers | Xhci.codex: command/transfer ring management, bulk/control transfers, event ring | 2026-05-23 |
| **Static bounds prover** | **Compiler proves bounded-integer range safety at compile time, elides runtime checks (CDX4010). Handles literals, fields, arithmetic (+,-,*,/), int-mod, bit-and, bit-shru, negate, if/else union** | **2026-05-23** |
| Short-circuit AND/OR | `IrAnd`/`IrOr` emit proper short-circuit codegen (test + jcc, no right-operand eval when unnecessary) | 2026-05-23 |
| Plug emitter fixes | Rust + JS Unicode escape (CCE→codepoint), C# O(n²) concat→text-concat-list, deprecated `++`→`&` across all 6 emitters (2661 replacements) | 2026-05-23 |
| Debugger | Symbolic breakpoints (name→MAP1→INT3), backtrace (stack walk + symbol resolve), register dump, perf counters, single-step (#DB), all views wired | 2026-05-23 |
| Editor undo/redo | Ctrl+Z/Ctrl+Y wired to undo/redo stacks, snapshots on every edit (char, backspace, delete, enter) | 2026-05-23 |
| **Dependent types** | **PropEqTy: types carry values. `Nil === Nil` in type position produces PropEqTy; `Refl` verified by unifier; invalid proofs are type errors. ProofTy, proof erasure (zero machine code), CDX4020 diagnostic. Builtins: Refl, sym, trans, cong, assume. claim/proof parser, induction keyword.** | **2026-05-23** |
| **420 modules** | **24 quires, 53 compiler files, 205 test samples** | **2026-05-23** |

---

## Documentation

- [docs/VisionAndVirtues.md](docs/VisionAndVirtues.md) — **Read this first**
- [docs/PM/CurrentPlan.md](docs/PM/CurrentPlan.md) — Active plan and direction
- [docs/DevelopersGuide.md](docs/DevelopersGuide.md) — Language syntax, types, how to write Codex
- [docs/UsersHandbook.md](docs/UsersHandbook.md) — Boot the IMG, first steps, using the system
- [docs/OperatorsManual.md](docs/OperatorsManual.md) — Build process, test harness, VM setup, debugging
- [docs/ArchitectsSketchbook.md](docs/ArchitectsSketchbook.md) — Memory layout, registers, allocators, phase maps
- [docs/DevelopersRulebook.md](docs/DevelopersRulebook.md) — Foreword quire catalog, library rules

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
