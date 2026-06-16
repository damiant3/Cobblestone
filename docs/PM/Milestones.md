# Codex Milestones

Detailed development timeline. See [README.md](../../README.md) for highlights.

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
| DRY + list-push rename | Shared forewords (ListUtils, math-mod), IrNegate constant fold, list-snoc -> list-push | 2026-05-06 |
| GPT/FAT32 writers | Native GPT + FAT32 in Codex, IMG compile mode, 64 MB bootable disk image | 2026-05-06 |
| **UEFI console** | **ConOut routing: print-line -> screen on real hardware. UEFI app PE stub (no EBS)** | **2026-05-06** |
| Prose buildout | Load-bearing prose: consistency checks, banned words, flag-gated pipeline | 2026-05-06 |
| Agent lifecycle | AgentRuntime (GGUF loader), AgentAcquisition, AgentCoordinator, FirstBoot wizard | 2026-05-06 |
| **288 modules** | **19 quires, 40 works modules** | **2026-05-06** |
| **Real hardware boot** | **"Welcome to Codex" on Asus x86-64: PE stub alignment fix, ImageBase=0, pure-PS1 toolchain** | **2026-05-07** |
| bit-shr/shru | SAR (arithmetic) + SHR (logical) split, 78-file codebase migration | 2026-05-07 |
| Fat16 reader | Foreword for reading FAT16 filesystems | 2026-05-07 |
| **VMX hypervisor** | **codex-vm.exe (WHP), VmSerial, VmIde, DevHypervisor -- replaces QEMU** | **2026-05-07** |
| VM build tools | VmCompile, VmRunner, VmPingpong, VmSweep -- Codex-native build pipeline | 2026-05-07 |
| Source embedding | FAT16 8MB IMG with SOURCE.CDX, SourceConcat transitive resolution | 2026-05-07 |
| codex-vm.exe | WHP-based VM host: PIT, serial, disk -- replaces QEMU | 2026-05-07 |
| rdmsr/wrmsr builtins | MSR access + vmlaunch-full/vmresume-full | 2026-05-07 |
| **UEFI dev console** | **Interactive menus, source indexing, ConOut/ConIn, write-usb** | **2026-05-07** |
| FAT16 IMG OOM fix | Emit deck reclaim: peak heap ~990 to ~350 MB | 2026-05-07 |
| **295 modules** | **19 quires, 44 works, 212 test samples** | **2026-05-07** |
| Pip joins | Third Claude Code agent (Cam, Nib, Pip) | 2026-05-08 |
| Annotations H1-H12 | First-class fact-publication surface for AI agents (codex.annotations/) | 2026-05-08 |
| Frontend de-deck | Lex / parse / desugar / scope phases move scratch to bivy; tighter heap surveys | 2026-05-08 |
| **Resilient act blocks** | **`trying N times ... falling back to ... on failure`: retry loops with bivy reclaim, fallback, failure handler** | **2026-05-09** |
| **Plug architecture** | **Emitters as standalone CDX programs reading IR text on stdin; first plug = C#** | **2026-05-09** |
| Diagnostic dangler fix | `make-diagnostic` deck-copies message text; `bag-add` deck-records the bag | 2026-05-09 |
| Library expansion | 295 -> 352 modules (+57: Path, Hkdf, Locale, Transformer, Toml, Cbor, Decimal, Argon2, Zstd, Brotli, Wavelet, ...) | 2026-05-09 |
| **352 modules** | **19 quires, 52 works, 401 test samples** | **2026-05-09** |
| New syntax | `&` replaces `++` for concat; comma-separated params (`Integer, Integer -> Integer`) | 2026-05-13 |
| Syntax conversion | Entire codebase (compiler + 352 library modules + 245 tests) converted | 2026-05-15 |
| 2GB address space | DEV_2GB_SYNTAX branch: 2 GB identity-mapped, 2 MB page tables | 2026-05-13 |
| Memory layout fix | Serial ring buffer collision discovered and fixed (0x300000->0x500000) | 2026-05-16 |
| **New builtins** | **print-line-raw, read-file-raw, chan-text-send, chan-text-recv + IPC helpers** | **2026-05-16** |
| Test harness overhaul | Crash recovery, per-test timing, batch parallelism, 3-min full sweep | 2026-05-16 |
| Editor features | Find/replace, undo, go-to-line, multi-file buffers | 2026-05-16 |
| **357 modules** | **19 quires, 59 compiler files, 581 test samples** | **2026-05-16** |
| codex-vm NE2000 NIC | Network-enabled VM, VGA display, keyboard, mouse | 2026-05-18 |
| codex-vm UEFI emulation | ConOut/ConIn trap dispatch, ReadKeyStroke, AllocatePages | 2026-05-18 |
| Compiler: pipe-forward | `\|>` operator and `.field` record selectors | 2026-05-18 |
| **Codex.Spark** | **85-module creative suite: 3D modeling, image editor, animation, audio/DAW, video compositor, procedural gen, interactive UI shell on GOP framebuffer** | **2026-05-18** |
| **codex-vm GOP** | **Graphics Output Protocol framebuffer -- Spark renders 3D on screen** | **2026-05-18** |
| Codex.DB | Relational database server (38 modules) with pipe-forward queries | 2026-05-18 |
| CodexMagic | Card game + game server with web portal (56 modules) | 2026-05-18 |
| Mutable records | `__record-set-mut` for in-place mutation under linear ownership | 2026-05-18 |
| **575 modules** | **24 quires, 59 compiler files, 581+ test samples** | **2026-05-18** |
| Emitter Exodus | PE, ELF, GPT, FAT writers extracted from compiler to plug CDX binaries | 2026-05-23 |
| **codex-vm hardware** | **PCI, xHCI USB (mass storage + HID + UVC camera), Intel HDA audio, HPET, IOAPIC, ACPI, SMBIOS, Bochs VBE, PC speaker -- VM grows from 400 to ~4500 lines** | **2026-05-23** |
| UsbVideo.codex | USB Video Class kernel driver: discovery, Probe/Commit, YUYV-to-RGB, framebuffer blit | 2026-05-23 |
| xHCI transfers | Xhci.codex: command/transfer ring management, bulk/control transfers, event ring | 2026-05-23 |
| **Static bounds prover** | **Compiler proves bounded-integer range safety at compile time, elides runtime checks (CDX4010)** | **2026-05-23** |
| Short-circuit AND/OR | `IrAnd`/`IrOr` emit proper short-circuit codegen | 2026-05-23 |
| **Dependent types** | **PropEqTy, Refl, proof erasure, claim/proof/qed, induction keyword** | **2026-05-23** |
| **420 modules** | **24 quires, 53 compiler files, 205 test samples** | **2026-05-23** |
| **Lazy evaluation** | **`lazy` keyword with memoized thunks; `force` builtin** | **2026-05-26** |
| Serial removal | TCP eliminated from the build -- memory-mapped I/O (ring-buffer in, UART out) | 2026-05-27 |
| Multi-pattern matching | `\|` alternation in `when`/`is` arms | 2026-05-27 |
| Exhaustiveness checking | A non-exhaustive `when` is a static error | 2026-05-28 |
| Constant folding | Compile-time arithmetic folding; IR dead-code elimination | 2026-05-28 |
| **Type classes** | **`class`/`instance` via dictionary passing: multi-instance dispatch, return-type polymorphism, generic constrained functions** | **2026-05-29** |
| **Linear types (Phase 3)** | **`linear` resources used exactly once (CDX2061/2063); `freeze : linear a -> a`; `mutable`-record aliasing (CDX2062); borrow/move from signatures** | **2026-05-29** |
| Mutable records + pointer map | In-place field mutation under linear ownership; pointer-map foundation | 2026-05-29 |
| **Tuples** | **(A, B) type sugar, let (x, y) = e destructuring; all 15 plugs emit idiomatic tuples** | **2026-05-30** |
| Scoped constraint dispatch | Dictionary dispatch only for parameter-typed args; locals use direct dispatch | 2026-05-31 |
| C# full-compiler emit | Emitted full compiler (2376 defs) compiles under `dotnet build` with 0 errors | 2026-05-30 |
| Interactive debugger | `-debug -break <fn> -map <file>`, command shell, symbol resolution, conditional breakpoints | 2026-05-31 |
| Lambda-def parse fix | `LambdaExpr` added to `is-compound`; app loop no longer consumes next def as lambda arg | 2026-05-31 |
| Durable disk writes | codex-vm IDE WRITE SECTORS + flush to host image; accounts persist across restarts | 2026-05-30 |
| SCOPE phase discipline | Precise pmap-walk escape check; phase-compact reclaims scope bivy; peak heap -102 MB | 2026-05-30 |
| CHECK survey | Count-aware reserve (records+ctors); peak heap selfhost 897->405 MB (-55%) | 2026-05-30 |
| **237 foreword modules** | **24 quires, 54 compiler files, 208 test samples, seed `3C624969`** | **2026-05-31** |
| **For-expressions** | **`for x in xs do f x` sugar for map loops; dogfooded across ~50 call sites** | **2026-06-02** |
| CHECK/LOWER heap reduction | Phase-aware deck sizing, EOF settle counter; ~80 MB saved | 2026-06-02 |
| x86-64 codegen optimization | Comparison folding, preamble elision, store-load elimination, immediate ops — fib(35) cut from 107 to 53 instructions | 2026-06-06 |
| Spark WebGPU Studio | 161KB WASM, 400+ exports, 28s build. WASM TCO: 255 functions. CAD workbench. UV editor. KvStore. Codex Designer. | 2026-06-06 |
| CodexMagic web platform | Card game server + web portal, game engine, economy, clans, seasons | 2026-06-06 |
| **Native-class codegen** | **TCO parallel-move shuffle, R8/R9-staged operands, leaf/near-leaf frame elision, IrRemInt + inliner — sum 14 insns (beats C /O2), fact 17, fib 23, gcd 23** | **2026-06-10** |
| **Application wave** | **630 app modules across 47 apps: ERP + 5 verticals, Market, Browser, FileShare, Secrets, Diagram, Globe, Star Atlas, MathBook, CVMM, 20 page apps on WebApp template** | **2026-06-10** |
| **Punctual functions** | **`punctual` keyword: per-function bounded-execution enforcement (novel). CDX6001-6005 compile errors. Instruction count reporting (CDX6010). Optional budget (CDX6011).** | **2026-06-13** |
| **Unit types** | **`Second = unit Integer` — distinct types with zero runtime overhead. Arithmetic preserves units. Cross-unit is a type error. Bounded + unit composition.** | **2026-06-13** |
| IoT protocol stack | MQTT v5, CoAP (RFC 7252), LwM2M, OTA updates with A/B slots and anti-rollback | 2026-06-13 |
| Board drivers | STM32F4, ESP32-C6, Raspberry Pi 4 — GPIO, UART, SPI, I2C with smoke tests | 2026-06-13 |
| EU compliance evidence | ComplianceEvidence module: 17 CRA/ETSI/NIST requirements mapped to Codex features | 2026-06-13 |
| CCE Tier 1 | 2048 two-byte codepoints covering all 27 EU languages; block-offset table (~48 bytes rodata) | 2026-06-14 |
| Test consolidation | 232 → 137 samples; BVT in 113s | 2026-06-13 |
| **Cross-arch GCC parity** | **ARM64 + RISC-V meet or beat GCC -O0 on all 4 micro-benchmarks. 24 optimization CLs: dest-driven emission, selective pro/epilogue, fused cmp+branch, frameless TCO. No optimizer.** | **2026-06-15** |
| Prism IDE | Compiler integration for web IDE — dynamic compile, plug facade, syntax highlighting, error display | 2026-06-15 |
| **SIMD / Vector types** | **`Vector N T` first-class type with dependent lane count. SSE2 packed codegen (ADDPD/SUBPD/MULPD/DIVPD). `~` approximate equality (4 ULP). CDX2085 (no == on Real). vec-splat/extract/reduce-add builtins.** | **2026-06-15** |
| **GPU plugs (PTX + SPIR-V)** | **Dual-target GPU compilation via plugs. PTX plug (NVIDIA sm_89) + SPIR-V plug (Vulkan 1.2+) built and compiling. Device IR emission. Design: DualTargetGpuCompilation.md.** | **2026-06-15** |
| **Punctual foreword quire** | **8 chapters: IntOps, BitOps, Saturate, FastMath, Trig, ColorOps, Kinematic, Endian. Every function is `punctual`. Compiler whitelist fix for builtins.** | **2026-06-15** |
| **Game engine foreword** | **21-chapter `codex.foreword.engine` quire: Renderer3D, Scene3D, Material, Texture, Mesh, Skinning, LOD, Culling, PostProcess, Audio3D, AudioBus, Input, GameLoop, GameplayTags, AbilitySystem, DebugDraw, EdgeMesh, HelmBridge.** | **2026-06-16** |
| **Poisoned compact** | **`__memset` builtin + per-phase poison bytes. Reclaimed memory is poisoned to catch stale-pointer reads.** | **2026-06-16** |
| **Number → Real rename** | **`Number` renamed to `Real` across compiler, all plugs, and tests. Confidence-level qualifiers (`Real`, `Real approximate`, `Real guess`).** | **2026-06-16** |
| **VectorMaskTy + comparisons** | **`VectorMask N` type, vector comparison operators, `vec-select` per-lane conditional. SSE2 CMPPD/MOVMSKPD codegen.** | **2026-06-16** |
| **377 library modules** | **26 quires (11 foreword + 9 OS sub-quires), 54 compiler files, seed `24FEA310`** | **2026-06-16** |
