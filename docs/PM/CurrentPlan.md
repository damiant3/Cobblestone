# Current Plan — Closing the Toolbox

**Updated**: 2026-06-16

This file is forward-looking only. Past work is in the Perforce log; if
you want milestones, run `p4 changes -m 100 //Codex/main/...`. Don't
add "done" rows here.

## The Vision

Codex is a self-contained development environment for itself and the
software written inside it. Hand someone a USB stick, they boot it,
generate or pick a key, and from that point everything happens inside
Codex: editor, compiler, OS, hypervisor, debugger, repository,
provenance, agent. No PowerShell, no QEMU, no GDB, no GitHub, no
Perforce, no `dd`, no Windows. Every tool a developer reaches for is
already in the toolbox, fitted, and signed.

We are close. Not there.

## Where We Are

- **MM4 self-sustaining** since CL 340. Pipeline is CDX-only; C# emitter
  is gone since CL 887.
- **Real hardware boots** on Asus + Dell UEFI x86-64 (CLs 1108–1116;
  USB testing through CL 1167-onwards).
- **VMX hypervisor in Codex** (`DevHypervisor`, CLs 1145–1150) plus
  `codex-vm.exe` WHP host (CL 1154) replaces QEMU.
- **OS stack and library shipped**: preemptive scheduler, IPC, identity,
  trust lattice, full TCP/IP, 5-phase verifier, shell, VGA + UEFI
  consoles, GUI substrate, agent runtime, ~377 library modules across
  26 quires.
- **Repository protocol live** (CL 1018; annotations integration H1–H12
  CLs 1221–1243).
- **New syntax landed** (CL 1315 + DEV_2GB_SYNTAX): `&` replaces `++`,
  comma-separated multi-param types. Parser fix for match arm column
  gate (CL 1526).
- **Append-only mutation log** (CL 1524): CRC-framed log replaces
  mutable JSON sidecar writes for annotations.
- **Test harness overhauled** (CL 1505): fatal detection, per-test
  timing, 30s timeout, `.fatal` category.
- **Repository restructure** (CL 1630): 31 top-level dirs to 8.
  `codex/compiler/`, `codex/foreword/`, `codex/os/`, `apps/`,
  `build/`, `tools/`, `docs/`, `seed/`.
- **codex-vm replaces QEMU** (CL 1592+1632): WHP-based VM host is
  default for all build/test scripts. Shadow register file workaround
  for WHP GPR corruption. NE2000 NIC, VGA display, PS/2 keyboard,
  UEFI emulation (CL 1696). ~4500 lines C: PCI, xHCI USB, Intel HDA
  audio, HPET, IOAPIC, ACPI, SMBIOS, UEFI firmware, Bochs VBE.
- **Mutable records** (CL 1636+1740): `mutable` keyword, field
  assignment syntax, type checking with CDX2060/2061/2062.
  Phase 3 (linearity tracking, `freeze`) pending.
- **Codex.Spark** (CL 1715+): 3D modeling framework — meshes,
  textures, armatures, IK, weight painting, particle systems,
  material editor, interactive app shell.
- **Short-circuit `&`/`|`** (CL 1885): `IrAnd`/`IrOr` now emit
  conditional jumps instead of bitwise ops. Eliminates the entire class
  of non-short-circuit guard bugs.
- **Plug pipeline complete** (CLs 1899–2305): all 53 transpiler plugs
  build. Languages (Ada, Babbage, C#, Clojure, COBOL, D, Elixir,
  Fortran, Go, Groovy, Haskell, Java, JavaScript, Julia, Kotlin, Lua,
  Nim, Objective-C, OCaml, Pascal, Perl, PHP, Python, Ruby, Rust,
  Scala, Scheme, Swift, TypeScript, WASM, Zig), UI frameworks (Angular,
  Electron, Flutter, GTK, HTML, Jetpack Compose, MAUI, Qt, React,
  Svelte, SwiftUI, Vue, WinForms, WPF), and binary formats (CDX, ELF,
  PE, IMG).
- **Debugger wired** (CLs 2093–2104): symbolic breakpoints, backtrace,
  registers, perf, single-step, all views in DevConsole. codex-vm
  `#BP`/`#DB` exception handling.
- **Editor undo/redo** (CLs 2102–2103): Ctrl+Z/Ctrl+Y, undo snapshots
  on every edit.
- **Emitter Exodus** (CLs 2029–2042): PE, ELF, IMG writers extracted
  from compiler to plug CDX binaries. Compiler slimmed from 59 to 53
  files.
- **Static bounds prover** (CLs 2073–2095): `ir-expr-proven-range`
  handles literal, field, add, sub, mul, div, mod, bit-and, bit-shru,
  negate, if/else, let bindings. CDX4010 diagnostic.
- **Dependent types / proof system** (CLs 2123–2216): `PropEqTy`,
  `===` in type position, `Refl`/`sym`/`trans`/`cong`/`assume`,
  `claim`/`proof`/`qed` parser, proof erasure (CDX4020), `Proof` as
  first-class type.
- **Emitter separation** (CLs 2135–2169): ConstructedTy resolution and
  lambda lifting extracted into RESOLVE and LIFT phases, each with
  independent `phase-compact`. IRChapter split (text functions moved
  out of IR layer). AChapter dropped from frontend.
- **Sampling profiler** (CLs 2287–2301): `prof-start`/`prof-dump`
  builtins, `__prof_start`/`__prof_dump` runtime helpers, timer ISR
  samples RIP at 1 KHz when enabled.
- **Parser fixes** (CLs 2263–2308): keyword-as-field-name fix (CL 2263),
  `is-page-marker` `Page`→`DbPage` rename (CL 2247), lex deck survey
  increased 12x→40x to prevent GPF on token-dense source (CL 2306).
- **Seed** (CL 2309): 2,191,873 bytes. Hard fixed point. 125 expected
  tests + 2 failing + 5 skipped = 132 total samples.
- **For-expressions** (CL ~3100): `for x in xs do f x` sugar for
  map loops. Dogfooded across ~50 call sites.
- **x86-64 codegen optimization** (CLs 3100–3500): comparison folding,
  preamble elision, store-load elimination, immediate ops. fib(35)
  cut from 107 to 53 instructions. WASM backend + WebGPU 3D.
- **Native-class codegen** (CLs ~3500–3800): TCO parallel-move, R8/R9-
  staged operands, leaf/near-leaf frame elision, IrRemInt + inliner.
  sum 14 insns (beats C /O2), fact 17, fib 23.
- **Application wave** (CLs ~3800–4200): 630 app modules across 47 apps.
  ERP + 5 verticals, Market, Browser, FileShare, CVMM desktop, 20 page
  apps on WebApp template.
- **Punctual functions + unit types** (CLs 4200–4310): `punctual`
  keyword (novel — no production language has per-function bounded
  execution). Unit types with zero-overhead erasure. IoT protocol
  stack (MQTT v5, CoAP, LwM2M, OTA). Board drivers (STM32F4, ESP32-C6,
  RPi 4). EU compliance evidence module.
- **Cross-arch GCC parity** (CLs 4280–4410): ARM64 + RISC-V codegen
  meets or beats GCC -O0 on all 4 micro-benchmarks. 24 optimization
  CLs. No optimizer — all emitter-level.
- **SIMD / Vector types — Phase 1 complete** (CLs 4392–4580):
  `Vector N T` first-class type with dependent lane count. SSE2
  packed codegen for f64 (ADDPD/SUBPD/MULPD/DIVPD) and f32
  (ADDPS/SUBPS/MULPS/DIVPS/CMPPS). `~` approximate equality,
  CDX2085 (no == on Real). vec-splat, vec-extract, vec-reduce-add,
  vec-select builtins. VectorMask N type with vector comparisons.
  Vector pattern matching (PCMPEQ + PMOVMSKB). Integer vectors
  (PADDQ/PSUBQ). Real approximate (f32) scalar + packed. Real
  trapping + saturating safety modes. suggested-vector-width
  comptime builtin. Number renamed to Real across compiler + plugs.
- **GPU plugs** (CLs 4424–4468): PTX plug (NVIDIA) + SPIR-V plug
  (Vulkan/OpenCL) built and compiling (157KB/152KB CDX). Full IR
  expression coverage. Design: `DualTargetGpuCompilation.md`.
- **Punctual foreword** (CL 4446): 8-chapter `codex.foreword.punctual`
  quire. IntOps, BitOps, Saturate, FastMath, Trig, ColorOps, Kinematic,
  Endian. Compiler whitelist fix for builtins.
- **Game engine foreword** (CL 4468): 21-chapter `codex.foreword.engine`
  quire. Renderer3D, Scene3D, Material, Texture, Mesh, Skinning, LOD,
  Culling, PostProcess, Audio3D, AudioBus, Input, GameLoop, etc.
- **Poisoned compact** (CL 4474): `__memset` builtin + per-phase poison
  bytes. Reclaimed memory is poisoned to catch stale-pointer reads.
- **Seed** (CL 4474): 2,291,929 bytes. Hard fixed point. 543 tests.

The remaining gap to the vision is the *wiring* — chapters that exist
but aren't yet the default path, plus a handful of capabilities that
still rely on host tools.

## Gaps, In Rough Priority

### 1. First-boot ceremony — end-to-end on real hardware

`FirstBoot.codex` has the wizard skeleton (welcome → identity →
agent-select → upstream → mode → save → complete). Needs:

- Verified on USB-booted Asus and Dell. Hardware-USB testing has been
  flaky (BIOS not seeing the stick across reboots, Damian's experience
  2026-05-08); blocking issue for full validation.
- `is-first-boot` predicate persists state correctly across reboots
  via DiskFacts.
- Identity keypair generation lands in the on-disk fact store and
  survives reseat.
- One probe sample exercising the whole flow under codex-vm so it
  can be regression-tested without putting a stick in a port.

### 2. Agent acquisition — bundled path proven

`AgentAcquisition` has bundled / local / network paths. Bundled is the
critical one for the USB-stick promise. Need:

- An actual GGUF model on the .img (size budget: USB 16 GB minus 8 MB
  Codex.img leaves room for a small model).
- Verification gate: after the .img is built, the bundled agent's
  manifest signature checks out and `agent-runtime-init` loads it
  without external tools.
- DevConsole "Agent Manager" menu wires through to register / inspect /
  swap the active agent.

### 3. USB install from inside Codex

USB MSC driver, DriveManager integration, DevConsole "Install to USB",
and XHCI transfer rings are all done. Remaining:

- End-to-end validation on a physical USB stick.
- Acceptable interim: keep `write-usb.ps1` as the *first* install only,
  then Codex-on-Codex installs (one stick reflashing another) work
  without leaving the system.

### 4. Pure Codex VMX host — retire `codex-vm.exe`

`codex-vm.exe` is ~4500 lines of C wrapping WHP. Codex has all the VMX
builtins (`vmxon`, `vmlaunch-full`, `rdmsr`, `wrmsr`, etc., CLs 1144 +
1165) and `DevHypervisor` is the orchestration layer. The remaining
work is the boot path: today, `DevHypervisor` runs *inside* a Codex
program that runs *inside* `codex-vm.exe`, which runs on Windows. To
eliminate the C host:

- Codex kernel-mode VMX entry on bare metal (post-EBS, the kernel sets
  up VMXON region and runs guest VMs directly).
- Nested-boot story: the host Codex boots from USB, then loads a guest
  Codex.cdx into a VMX-isolated partition for compile / sweep runs.
  Each gate runs in its own guest.
- Move `VmSerial` / `VmIde` device models from the works tier into
  kernel space so they're available pre-EBS.
- This is the largest remaining work item but unblocks the no-host-OS
  story entirely.

### 5. Repository protocol replaces Perforce

`RepoProtocol`, `KeyManager`, `Annotation*`, `BuildRecord`, `Historian`,
`SignedAnnotation`, `Discussion` are all built. P4 is still the actual
store of code. To finish:

- Source-as-facts: store `.codex` files in `DiskFacts` with content-
  addressing and Ed25519 author signatures.
- Federated pull/push over `TrustTransport` (have the transport,
  designed in `Designs/`, not wired into the source-of-truth path).
- History walking for source (we have it for annotations via
  `Historian` H12; needs equivalent for definitions).
- Editor integration: opening a chapter shows recent verdicts,
  proposals, and the supersession chain.
- Cutover: dual-store while we prove federation, then the P4 depot
  becomes a frozen mirror.

### 6. Editor and Debugger maturity

Editor gaps:
- Syntax highlighting in GUI mode (`UI` substrate has the primitives;
  not wired).
- Build/run integration: F5 to compile-and-run the current chapter
  through `vm-compile` + `vm-run-cdx`.
- Annotation overlay: H1–H12 driver feeds editor margins (CL 1226 added
  the integration; verify all twelve roles surface in the editor and
  not just the data layer).

Debugger gaps:
- Watch expressions.
- Backtrace with function names requires debug info emitted into CDX
  (currently uses heuristic stack walk + MAP1 symbol map).

### 7. Phase discipline — finish compiler optimizations

From `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`:
- Deck-record toggle ratchet (per-sub-allocation classification).
- Escape invariant enforcement (seal-time pointer validation).
- Remove TCO reset (phase boundaries replace within-phase reclaim).
- ~~Survey tightening~~ DONE by deletion: the survey system was torn
  out 2026-07-07; decks are fixed floors over a demand-paged heap
  (`docs/Designs/Compiler/Done/DemandPagedArena.md`).

Compiler-correctness work, not user-facing, but each one moves the
heap HWM down and improves the chance that compile-on-stick succeeds
on lower-RAM boards.

### 8. Spark WebGPU Studio — creative suite in the browser

**Updated**: 2026-06-08

Spark is an 89-module creative suite (3D modeling, animation, image
editing, materials, audio, CAD) compiled to WASM and running in the
browser via WebGPU. The studio is the first Codex application visible
to non-Codex users — a working demo of the language and tools.

89 modules, 578KB source, 2.47MB IR, 1.39MB WAT. WASM tail-call
optimization enabled (CL 3504): 255 self-recursive functions compile
to WASM loop/br instead of call, eliminating stack overflow for deep
recursion (canvas clear at 1024x1024 = 1M iterations works in 0.4ms).
Also includes Codex Designer (12.5KB WASM, WYSIWYG UI builder).

**Completed** (CLs 3130–3504):
- All animation, image editor, 3D, and audio items (a–r below) done
- CAD workbench: Part/Sketch/Measure tabs, precision inputs, ortho
  views, engineering units (mm/cm/in), PLY/DXF/STL import/export,
  dimension annotations, section view, sketch entities (CL 3430–3435)
- KvStore data layer: hash table with chaining, text-key and i32-key
  helpers, 11 exports, JS bridge (CL 3387)
- UV editor: state management, wireframe emitter, gen-uv-panel with
  mesh stats and zoom controls, 10 exports (CL 3388)
- WASM TCO: 255 functions optimized, px-pack color bug fixed (CL 3504)
- Codex Designer: standalone WYSIWYG UI builder, 11 widget types, all
  rendering in Codex, code generation (CLs 3441–3458)

**Remaining:**

m. **wat2wasm undefined function** — `build-spark.ps1` produces WAT
   successfully (1.39MB) but `wat2wasm` rejects it: `undefined function
   variable "$AbsorbedDose"`. A unit type from the punctual foreword
   is referenced but not emitted by the WASM plug. Likely missing from
   the plug's function export table.

n. **Mesh operations** — CSG booleans exist in Codex (mesh-bool-union,
   intersect, difference) but not fully wired through UI. Mesh
   subdivide/smooth/flip work.

p. **Move JS to Codex** — the HTML harness has ~1100 lines of JS for
   DOM rendering, WebGPU pipeline, event forwarding. Long-term these
   should be emitted from Codex. The view matrix (lookAt with
   horizon-flip) must eventually move to Codex once the fixed-point
   scale issue is resolved.

## No Dates

Every estimate has been wrong by orders of magnitude in both
directions. The order above is the priority order. That's all.

## Cross-References

- `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md` — gap 7 detail.
- `docs/Designs/Active/Compiler/Annotations.md` — gaps 5 and 6 (editor
  overlay).
- `docs/Reference/UEFI_Spec_Summary.md` — gaps 3 and 4 (USB MSC,
  bare-metal VMX entry).
- `docs/VisionAndVirtues.md` — the founding vision behind this gap
  list. Read that before redesigning anything in gap 5.
- `codex/plugs/wasm/build-output/spark-webgpu.codex` — gap 8 WASM
  module (scene, camera, timeline, canvas, export, 400+ exports).
- `apps/spark/` — 86 Spark modules, most 100% complete, awaiting
  WASM wiring (gap 8 items d–o).
