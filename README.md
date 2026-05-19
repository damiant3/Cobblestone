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

As of 2026-05-18:

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
- **Plug architecture**: emitters move out of compiler core into
  standalone CDX programs. Compiler emits IR text (S-expressions);
  plug consumes IR on stdin, emits target source on stdout. C# plug
  is the first exemplar; Cam's lifting Ada / Babbage / COBOL / C++ /
  Fortran / Go / Java / JavaScript / Python / Rust / CodexText next.
- **Mutable records**: `mutable` keyword with in-place field
  assignment, type-checked immutability enforcement (CDX2060).
- **Repository restructure**: 31 top-level dirs to 8. codex-vm
  replaces QEMU as default VM (WHP-based, NE2000 NIC, VGA, UEFI).
- **Sample battery**: 156 samples; 103 pass, 2 fail (pre-existing),
  51 skipped.

The compiler is a hard fixed point of itself on bare metal.

**`seed/Codex.cdx`** (2,134,336 bytes) — the canonical seed:

| Algorithm | Digest |
|---|---|
| MD5    | `D0421F0A4D2BFF2B18B18B1ED0829168` |
| SHA-256 | `72E0EEE9416EBB143F2C0459E31D71B393356FA572A00752DD2CCD1AA1AC466F` |

**`seed/Codex.img`** (8,388,608 bytes / 8 MB) — UEFI-bootable FAT16 GPT disk image:

| Algorithm | Digest |
|---|---|
| MD5    | `6A1BDE1F274B43F335C5E7F1B464F504` |
| SHA-256 | `29507CDC50F397C9E65FEA8F0193CA415B041ED57D76CEDF9ED6BEE172682B7F` |

The IMG contains `EFI/BOOT/BOOTX64.EFI` (interactive UEFI dev console as a
UEFI PE32+ application) and `SOURCE.CDX` (compiler source) in an 8 MB
FAT16 GPT image. Colored menu with keyboard navigation. Escape to reboot.
Tested on real Asus TUF hardware.

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

**Prerequisites**: `codex-vm.exe` (build via `tools/build-vm.ps1`) or
QEMU (with WHPX) for the bare-metal path.

```powershell
# Sample battery (bare-metal selfhost, ~2-5s per sample; -Jobs N for parallel)
codex.build/test.ps1 -Jobs 4

# Full build: text round-trip + CDX fixed-point + test battery
codex.build/build.ps1
```

### Try it without building (just codex-vm + the seed CDX)

The CDX in `seed/Codex.cdx` is a complete compiler. Boot it under
codex-vm, hand it source bytes on serial, and it hands back CDX or ELF,
C# or Codex-text over the same socket — the output format is selected
by the mode line. Two helpers wrap the protocol — feed and run:

```powershell
# Stage the seed where the helpers expect it.
New-Item -ItemType Directory -Force build-output/bare-metal, build-output/try
Copy-Item seed/Codex.cdx build-output/bare-metal/Codex.cdx

# Compile a sample with the seed (boots codex-vm, sends source, reads emitted CDX).
codex.build/test-compile.ps1 -Src codex.test/hello.codex -Out build-output/try/hello.cdx -Log build-output/try/build.log

# Boot the just-compiled program and capture its serial output.
codex.build/test-run.ps1 -Kernel build-output/try/hello.cdx -OutFile build-output/try/hello.out
Get-Content build-output/try/hello.out
```

Two-channel serial: COM1 carries data (source in, CDX or runtime stdout
out), COM2 carries control (`READY` greeting). Inspect
`codex.build/test-compile.ps1` and `codex.build/vm-config.ps1` to see the
VM invocation (codex-vm by default, QEMU via `$env:USE_QEMU=1`).

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

The pipeline lives in `codex/` — the self-hosted compiler,
~30,500 lines across 59 `.codex` files. This is the only path that is
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

Code outside the compiler is organized into **19 quires** (library namespaces)
with **352 modules** total:

| Quire | Directory | Count | Highlights |
|-------|-----------|------:|------------|
| **Foreword** | `codex.foreword/` | 82 | Hamt, Sort, PriorityQueue, Trie, LruCache, UnionFind, Graph, B+Tree, Deque, Rope, IntervalTree, ConsistentHash, BloomFilter, Regex, DateTime, Ed25519, SHA-256/512, CCE, MathLib, ListUtils, Path, Format, Hkdf, Deflate, Gzip, NumberTheory, Filter, Probability, Locale |
| **Game** | `codex.foreword.game/` | 26 | A*, Dijkstra, DiamondSquare, HexMap, Voronoi, FloodFill, Octree, Quadtree, Bresenham, CellularAutomata, ECS, StateMachine, Tween, TileMap, CardDeck, Rasterizer, Sprite, Scene2D, Color, Raytracer, Klondike, Camera, Kinematics, SaveSlot, Netcode, Inventory |
| **AI** | `codex.foreword.ai/` | 19 | Tensor, NeuralNet, Activation, GGUF, SparseLattice, KNN, DecisionTree, GeneticAlgorithm, Tokenizer, Reservoir, GpuProxy, DiffusionScheduler, Transformer, KvCache, Sampling, Optimizer, Attention, Embedding, Loss |
| **UI** | `codex.foreword.ui/` | 28 | Theme (3 built-in), Widget, Layout, Render, Surface, Event, Binding, Animation, Icon (5 sizes), Overlay, Sound, Font (CCE), Cursor, Scroll, Focus, Dialog, Orchestrator, Selection, TextField, Clipboard, Constraint, Drag, RichText, Window, Touch, Charts, Accessibility |
| **Signal** | `codex.foreword.signal/` | 14 | FFT, Perlin, Convolution, ADSR Envelope, Resample, Wavelet, Pitch |
| **Compress** | `codex.foreword.compress/` | 8 | LZ77, Huffman, RLE, Deflate, Gzip, Lz4, Zstd, Brotli |
| **Encode** | `codex.foreword.encode/` | 32 | JSON (parser + emitter), Base64, Hex, URI, UUID, CSV, CRC32, GrayCode, Bencode, Protobuf, Toml, Cbor, Yaml, MessagePack |
| **Math** | `codex.foreword.math/` | 12 | Quaternion, Matrix4, Bezier, CORDIC, Complex, Catmull-Rom Spline, Geodesic, LinearAlgebra, Vector, Numeric, Decimal |
| **Sim** | `codex.foreword.sim/` | 7 | Verlet Physics, Collision (AABB/sphere), ParticleSystem, Steering (Reynolds), Optimize, SpatialHash |
| **Net** | `codex.os.net/` | 15 | Ethernet, ARP, IPv4, TCP, UDP, ICMP, DNS, DHCP, NTP, Syslog, TFTP, NetworkConfig, Router, HttpClient, Tls (with AesGcm + X25519) |
| **Kernel** | `codex.kernel/` | 16 | DiskFacts, Vga, VgaGraphics, Pci, Keyboard, Mouse, BitmapFont, Console, DiagnosticShell, GpuBridge, Ivshmem, Usb, UsbAudio, Xhci, VmSerial, VmIde |
| **OS** | `codex.os.*` | 41 | Trust lattice, verifier, scheduler, IPC, identity, shell, clarifier, replay, observability, dev tools |
| **Works** | `codex.works/` | 52 | DevConsole, UefiConsole, DevConsoleMenu, CodeBrowser, ConsoleEditor, FirstBoot, UefiBoot, AgentRuntime, AgentCoordinator, AgentAcquisition, CompilerDriver, VmCompile, VmPingpong, VmSweep, Http, WebServer, AnnotationDriver, AnnotationsSidecar |

Quires cite each other via `cites Game chapter AStar` or
`cites Net chapter Tcp`. The compiler and build harness
resolve quire names to directories at load time.

---

## Project Structure

```
codex/                    Self-hosted compiler (59 files, ~30.5K lines)
codex.foreword/           Core forewords — data structures, crypto (82 modules)
codex.foreword.game/      Game — A*, hex, ECS, physics, terrain (26 modules)
codex.foreword.ai/        AI — tensors, neural nets, GGUF, transformer (19 modules)
codex.foreword.ui/        UI — theme, widget, layout, render, compositor (28 modules)
codex.foreword.signal/    Signal — FFT, Perlin, convolution (14 modules)
codex.foreword.compress/  Compression — LZ77, Huffman, RLE, Deflate, Lz4, Zstd, Brotli (8 modules)
codex.foreword.encode/    Encoding — JSON, Base64, Protobuf, CSV, Toml, Cbor, Yaml (32 modules)
codex.foreword.math/      Math — quaternions, matrices, Bezier, LinearAlgebra (12 modules)
codex.foreword.sim/       Simulation — physics, collision, particles (7 modules)
codex.kernel/             Kernel — disk I/O, VGA, keyboard, font, diag (16 modules)
codex.os/                 OS core — shell, registry, clarifier (4 modules)
codex.os.trust/           Trust — lattice, policy, sessions (11 modules)
codex.os.net/             Networking — full TCP/IP + protocols (15 modules)
codex.os.verify/          Verification — 5-phase CDX verifier (5 modules)
codex.os.dev/             Developer tools — debugger, inspectors (5 modules)
codex.os.replay/          Replay — deterministic record/replay (3 modules)
codex.os.sched/           Scheduler — process groups, watchdog (6 modules)
codex.os.observe/         Observability — metrics, health, journal (7 modules)
codex.works/              Application — console, agents, VM tools, first boot, annotations (52 modules)
codex.annotations/        On-disk annotation sidecars (JSON facts about chapters)
plugs/                    Plug architecture — IR-text-driven emitters (csharp + common parser)
codex.test/               Compiler samples + OS integration tests (581 samples)
codex.build/              Build/test harness (PowerShell)
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

---

## Documentation

- [docs/VisionAndVirtues.md](docs/VisionAndVirtues.md) — **Read this first**
- [docs/PM/CurrentPlan.md](docs/PM/CurrentPlan.md) — Active plan and direction
- [docs/DevelopersGuide.md](docs/DevelopersGuide.md) — Language syntax, types, how to write Codex
- [docs/UsersHandbook.md](docs/UsersHandbook.md) — Boot the IMG, first steps, using the system
- [docs/OperatorsManual.md](docs/OperatorsManual.md) — System architecture, OS stack, build and test
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
