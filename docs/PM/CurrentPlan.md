# Current Plan — Closing the Toolbox

**Updated**: 2026-05-20

This file is forward-looking only. Past work is in the Perforce log; if
you want milestones, run `p4 changes -m 100 //Codex/main/...`. Don't
add "✅ done" rows here.

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
  gate (CL 1526). Seed: 2,109,248 bytes, hard fixed point.
- **Append-only mutation log** (CL 1524): CRC-framed log replaces
  mutable JSON sidecar writes for annotations.
- **Test harness overhauled** (CL 1505): fatal detection, per-test
  timing, 30s timeout, `.fatal` category. Battery: 176/190 pass
  (CLs 1536-1541 promoted 5 skipped tests).
- **Repository restructure** (CL 1630): 31 top-level dirs to 8.
  `codex/compiler/`, `codex/foreword/`, `codex/os/`, `apps/`,
  `build/`, `tools/`, `docs/`, `seed/`.
- **codex-vm replaces QEMU** (CL 1592+1632): WHP-based VM host is
  default for all build/test scripts. Shadow register file workaround
  for WHP GPR corruption. NE2000 NIC, VGA display, PS/2 keyboard,
  UEFI emulation (CL 1696).
- **Mutable records** (CL 1636+1740): `mutable` keyword, field
  assignment syntax, type checking with CDX2060/2061/2062.
  Phase 3 (linearity tracking, `freeze`) pending.
- **Codex.Spark** (CL 1715+): 3D modeling framework — meshes,
  textures, armatures, IK, weight painting, particle systems,
  material editor, interactive app shell.
- **Seed** (CL 1802): 2,134,336 bytes. Restored variant-match REPL
  exit (reverted CL 1768 runtime flag). 103/105 pass (db-test,
  sort-test pre-existing).
- **lookup-expr-type OOB fix** (CL 1845): Non-short-circuit `&` in
  `lookup-expr-type` caused `list-at` one past end after binary search
  miss, reading stale heap in REPL batch mode. Root cause of
  handler-nested batch GPF and likely the plug crash. Seed: 2,165,928
  bytes, hard fixed point. 105/105 pass, 0 fail.
- **Short-circuit `&`/`|`** (CL 1885): `IrAnd`/`IrOr` now emit
  conditional jumps instead of bitwise ops. Right operand is only
  evaluated when needed. Eliminates the entire class of non-short-circuit
  guard bugs. Seed: 2,172,408 bytes.
- **Plug pipeline functional** (CLs 1899-1906): All 8 language emitters
  (C#, Rust, Python, JS, Ada, COBOL, Fortran, Babbage) handle
  `IrFieldStore` and `IrTry`. `-IrCce` compiler mode fixed (was
  outputting Unicode). Curried application fix in Rust emitter.
  Shared `IRTextParser` parses both new IR nodes.
- **Foreword library fixes** (CL 1908): Huffman tree (recursive variant
  + proper walk), Graph DFS (visited-state propagation), Bresenham
  (dead branch), Convolution (div-by-zero), HexFormat (spacing).
- **Seed** (CL 2247): 2,181,200 bytes. Hard fixed point. 108/108 pass.
  Parser `is-page-marker` fix (type name `Page` no longer swallowed).
  TCP transport: raw buffer receive (O(n) vs O(n²)), NAT half-close,
  plug framed protocol. Boot image rebuilt (8 MB FAT16).

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

- ~~Wire `codex-vm.exe` into build/test scripts~~ — done.
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

`tools/write-usb.ps1` is a PowerShell script that uses Windows
`Get-Disk` + raw `\\.\PhysicalDrive` writes to install Codex onto a
USB stick. To remove this dependency we need:

- ~~USB MSC (Mass Storage Class) driver in `codex.kernel`~~ — done.
  `UsbMassStorage.codex` implements Bulk-Only Transport (BBB) with
  SCSI READ(10)/WRITE(10)/READ CAPACITY/INQUIRY/TEST UNIT READY.
  Discovers MSC devices via XHCI port probing.
- ~~DriveManager USB integration~~ — done. `dm-enumerate-drives` now
  probes USB MSC devices after ATA. USB drives appear with
  `di-is-usb = True` and carry `di-usb-device` for direct MSC access.
  `dm-install-usb` writes sectors from a source ATA drive to a USB
  target via MSC bulk transport.
- ~~"Install to USB" mode in DevConsole~~ — done. Drive Manager menu
  has "Install Codex to USB" item. Dispatch enumerates USB drives,
  selects first, and calls `dm-install-usb`.
- Test: `usb-msc-test.codex` covers protocol structures (CBW/CSW
  encoding, SCSI command building, inquiry/capacity parsing).
- ~~XHCI transfer ring completion~~ — done. `Xhci.codex` rewritten
  with full ring infrastructure: command ring with link TRB wrap,
  event ring with cycle-bit polling, DCBAA, port reset, Enable Slot,
  Address Device, Configure Endpoint, control transfers (setup/data/
  status stages), and bulk in/out via Normal TRBs. Discovery now reads
  real capability registers (cap-length, HCSPARAMS1, doorbell offset,
  runtime offset). Tests: `xhci-enum-test.codex`.
- Remaining: end-to-end validation on a physical USB stick.
- Acceptable interim: keep `write-usb.ps1` as the *first* install only,
  then Codex-on-Codex installs (one stick reflashing another) work
  without leaving the system.

### 5. Pure Codex VMX host — retire `codex-vm.exe`

`codex-vm.exe` is 400 lines of C wrapping WHP. Codex has all the VMX
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

`ConsoleEditor` and `DevDebugger` exist and are functional but minimal.

Editor gaps (partially closed CL 1521-1523):
- ~~Find / replace~~ — done (CL 1521).
- ~~Multi-file~~ — done, BufferList (CL 1523).
- ~~Undo / redo stack~~ — done (undo CL 1521, redo CL 1543).
- ~~Key dispatch~~ — done, full keyboard handling (CL 1522).
- Syntax highlighting in GUI mode (`UI` substrate has the primitives;
  not wired).
- Build/run integration: F5 to compile-and-run the current chapter
  through `vm-compile` + `vm-run-cdx`.
- Annotation overlay: H1–H12 driver feeds editor margins (CL 1226 added
  the integration; verify all twelve roles surface in the editor and
  not just the data layer).

Debugger gaps (partially closed CL 1520):
- ~~DevDebugger menu placeholder routes~~ — done (CL 1520), dispatches
  with status prompts. Full integration needs DebuggerState on
  DevConsoleState.
- Symbolic breakpoints (currently address-based; needs debug info
  emitted into CDX).
- Watch expressions.
- Single-step (preempt-on-instruction; scheduler already handles
  preemption — wire to a debug interrupt).
- Backtrace with function names (requires debug info).

### 8. Phase discipline — finish the four remaining compiler steps

From `docs/Active/Compiler/PHASE-ARCHITECTURE.md`, four open items:
- Deck-record toggle ratchet (per-sub-allocation classification).
- Escape invariant enforcement (seal-time pointer validation).
- Remove TCO reset (phase boundaries replace within-phase reclaim).
- Survey tightening (per-phase multipliers currently 10% headroom).

Compiler-correctness work, not user-facing, but each one moves the
heap HWM down and improves the chance that compile-on-stick succeeds
on lower-RAM boards.

### 9. Plug emitters — complete the transpilation pipeline

All 8 emitters now handle the full IR node set (CL 1899). Remaining:
- Rust `rs-escape-text` still emits wrong Unicode escapes (CCE byte
  values instead of codepoints). All string literals garbled.
- C# plug emitter hangs on O(n^2) string concat in `emit-cce-runtime`
  (~50 chained `&`). Not a compiler bug — needs restructuring to
  stream output or use `text-concat-list`.
- No end-to-end test: compile → plug → target-language compile → run.
  The plugs produce source text but nothing verifies it compiles.

### ~~10. Append-only mutation log for annotations~~ — done (CL 1524)

`MutationLog.codex` implements append-only CRC-framed log with
`log-append`, `log-entries`, `log-is-stale`, `log-since`, `log-replay`.
JSON sidecars are materialized views.

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
