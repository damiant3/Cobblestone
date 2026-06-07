# Current Plan — Closing the Toolbox

**Updated**: 2026-06-06 (Spark Studio: 16/18 items complete)

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
  consoles, GUI substrate, agent runtime, ~360 library modules across
  20 quires.
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
- **Plug pipeline complete** (CLs 1899–2305): all 48 transpiler plugs
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

The remaining gap to the vision is the *wiring* — chapters that exist
but aren't yet the default path, plus a handful of capabilities that
still rely on host tools.

## Gaps, In Rough Priority

### 1. PS1 wire-out — replace external-script gates with internal paths

The Codex-native gate chapters exist (`VmCompile`, `VmRunner`,
`VmPingpong`, `VmSweep`, all CL 1153) and `codex-vm.exe` is now the
default VM for all PS1 build/test scripts (`vm-config.ps1`; QEMU
available via `$env:USE_QEMU=1`). Until the PS1 scripts are deleted
(or reduced to thin shims that call into Codex), "Codex compiles
itself" is true only under the assumption that someone runs PowerShell
first.

- Add DevConsole menu items that drive `vm-pingpong` and `vm-sweep`
  from inside the booted system. The menu placeholders ("Run All
  Samples", "Run Failing Only") under the Sweep mode are still stubs
  in `DevConsole.codex`.
- Once the dev-console paths are green, delete the PS1 scripts.
  `vm-config.ps1`, `clean-zombies.ps1`, `run-with-disk.ps1`,
  `stress-sweep.ps1`, the `test-disk-*.ps1` family, and
  `test-self-verify.ps1` should all go.
- Keep at most one PS1 — a thin shim that knows how to bring up
  `codex-vm.exe` if you're booting onto Windows for the first time.
  Aim is zero, but parking-lot one.

### 2. First-boot ceremony — end-to-end on real hardware

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

### 3. Agent acquisition — bundled path proven

`AgentAcquisition` has bundled / local / network paths. Bundled is the
critical one for the USB-stick promise. Need:

- An actual GGUF model on the .img (size budget: USB 16 GB minus 8 MB
  Codex.img leaves room for a small model).
- Verification gate: after the .img is built, the bundled agent's
  manifest signature checks out and `agent-runtime-init` loads it
  without external tools.
- DevConsole "Agent Manager" menu wires through to register / inspect /
  swap the active agent.

### 4. USB install from inside Codex

USB MSC driver, DriveManager integration, DevConsole "Install to USB",
and XHCI transfer rings are all done. Remaining:

- End-to-end validation on a physical USB stick.
- Acceptable interim: keep `write-usb.ps1` as the *first* install only,
  then Codex-on-Codex installs (one stick reflashing another) work
  without leaving the system.

### 5. Pure Codex VMX host — retire `codex-vm.exe`

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

### 6. Repository protocol replaces Perforce

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

### 7. Editor and Debugger maturity

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

### 8. Phase discipline — finish compiler optimizations

From `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`:
- Deck-record toggle ratchet (per-sub-allocation classification).
- Escape invariant enforcement (seal-time pointer validation).
- Remove TCO reset (phase boundaries replace within-phase reclaim).
- Survey tightening (per-phase multipliers — lex survey already
  increased to 40x in CL 2306).

Compiler-correctness work, not user-facing, but each one moves the
heap HWM down and improves the chance that compile-on-stick succeeds
on lower-RAM boards.

### 9. Spark WebGPU Studio — creative suite in the browser

**Updated**: 2026-06-06

Spark is an 86-module creative suite (3D modeling, animation, image
editing, materials, audio) compiled to WASM and running in the browser
via WebGPU. The studio is the first Codex application visible to
non-Codex users — a working demo of the language and tools.

**Completed** (CLs 3130–3177):
- WASM-owned scene (128-byte objects, 7 primitives, per-vertex PBR)
- Data-driven panel layout (outliner, properties, viewport, timeline, scene panel)
- 6 editor modes (Model, Animate, Render, Image, Audio, Stage)
- Animation timeline with linear interpolation, 5 channels, auto-key
- Image editor with brush painting, layers, eyedropper, brush presets, PNG export
- Undo system (16-entry circular buffer of scene snapshots)
- Project save/load via IndexedDB, OBJ/STL export, viewport screenshot
- Camera presets (Front/Top/Right/Perspective), continuous orbit
- Object rename, duplicate, group, lock, hide/show/isolate, snap-to-grid
- Context menus, hover tooltips, search filtering, scene statistics
- Audio synth preview, scene presets, mode-aware right panel
- 55+ WASM exports, 21KB binary, 256-page (16MB) memory

**Remaining — Foundations:**

a. **KvStore data layer** — migrate from flat shared-memory scene to
   KvStore-backed project database. Keys like `obj:{id}:pos-x`. Enables
   proper undo via HAMT structural sharing. Requires solving HAMT
   persistence across `__heap_reset` (the critical WASM challenge).

b. ~~Asset browser~~ — **PARTIAL**. Scene panel has primitive catalog,
   material library (5 presets), light list. Full KvStore-backed
   asset browser deferred to KvStore data layer.

c. ~~Project deserialize~~ — **DONE** (CL 3192). Text parser in WASM,
   round-trip via IndexedDB.

**~~Animation depth~~ — DONE:**

d. ~~Bezier keyframes~~ — **DONE**. 5 easing modes (linear, ease-in,
   ease-out, ease-in-out, cubic). Timeline zoom in/out/frame-all.

e. ~~Camera path animation~~ — **DONE**. Camera keyframes with
   interpolation, Cam button in timeline.

f. ~~Playback rendering~~ — **DONE**. Frame-by-frame render export
   (PNG sequence from range markers). Animation CSV export.

**~~Image editor depth~~ — DONE:**

g. ~~Brush interpolation~~ — **DONE**. Stroke interpolation between
   drag points. Eraser with line interpolation.

h. ~~Selection tools~~ — **DONE**. Rect selection (shift+drag),
   fill selection (Enter), clear (Escape). Overlay rendering.

i. ~~Image filters~~ — **DONE**. 7 filters: grayscale, invert,
   brighten, darken, contrast+/-, sepia. One-click buttons.

j. ~~Layer blend modes~~ — **DONE**. Normal/Multiply/Screen/Overlay
   selectable per layer. Click to cycle.

**~~3D depth~~ — MOSTLY DONE:**

k. ~~Material library~~ — **DONE**. 5 presets (Default/Metal/Glass/
   Blue/Emissive), one-click assign, add custom materials.

l. ~~Light editor~~ — **DONE**. 3 default lights, add directional/
   point, color display in scene panel.

m. ~~Transform gizmo~~ — **DONE**. RGB axis lines rendered at selected
   object position. Snap rotation (15/45/90°), rotate-by, copy/paste
   transforms, mirror, align/distribute.

n. ~~Mesh operations~~ — **PARTIAL**. Flatten (zero rotation/anim),
   subdivision level stored per-object (0-3). CSG not wired.

o. **UV editor** — Not started. UvEditor.codex exists but needs
   dedicated panel and WebGPU overlay.

**Remaining — Architecture:**

p. **Move JS to Codex** — the HTML harness still has ~600 lines of JS
   for DOM rendering, MVP construction, event forwarding. Long-term
   these should be emitted from Codex via the HtmlEmitter or WebEmitter
   plug. The view matrix (lookAt with horizon-flip) must eventually
   move to Codex once the fixed-point scale issue is resolved.

q. ~~WasmEmitter export convention~~ — **DONE** (CL 3221). Replaced
   115 if-branches with a single `text-contains` check against a
   pipe-separated export list string. Adding a new export = add its
   name to the string. WASM plug shrank 20KB.

r. ~~Test harness~~ — **DONE**. `self-test` export verifies scene
   constants. Command palette includes "Self Test" command.

## No Dates

Every estimate has been wrong by orders of magnitude in both
directions. The order above is the priority order. That's all.

## Cross-References

- `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md` — gap 8 detail.
- `docs/Designs/Active/Compiler/Annotations.md` — gaps 6 and 7 (editor
  overlay).
- `docs/Reference/UEFI_Spec_Summary.md` — gaps 4 and 5 (USB MSC,
  bare-metal VMX entry).
- `docs/VisionAndVirtues.md` — the founding vision behind this gap
  list. Read that before redesigning anything in gap 6.
- `codex/plugs/wasm/build-output/spark-webgpu.codex` — gap 9 WASM
  module (scene, camera, timeline, canvas, export, 55+ exports).
- `apps/spark/` — 86 Spark modules, most 100% complete, awaiting
  WASM wiring (gap 9 items d–o).
