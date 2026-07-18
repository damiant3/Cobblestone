# Current Plan — Closing the Toolbox

**Updated**: 2026-07-15

This file is forward-looking. It says where we are, what is missing, and
in what order we intend to close it. It does not archive completed work
— the authoritative record of a closure is its changelist in Perforce
(`p4 changes -m 100 //Codex/main/...`).

Every open capability lives in `docs/PM/BACKLOG.md`. This file is the
shape of the thing; the backlog is the list.

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

The compiler is a hard fixed point of itself on bare metal: Codex
compiles Codex, and the binary that produces is byte-identical to
itself. That property is the floor everything else stands on, and it
holds today.

- **Seed**: `seed/Codex.cdx`, digest `5A6B432B…`, ~2.28 MB, boots on
  codex-vm with no OS and no libc beneath it.
- **Compiler**: 55 chapters, ~36,300 lines of Codex, in
  `codex/compiler/`.
- **Battery**: 342 tests, 328 pass, 0 fail, 14 skip (`build/test.ps1`),
  measured 2026-07-13. **Re-measure before quoting; never carry a count
  forward.**
- **Backends**: x86-64 native; ARM64 and RISC-V via plugs, both at or
  near GCC parity on the micro-benchmarks. 53 plugs total.
- **Type system**: effect rows, linear types, bounded-integer contracts,
  dependent types with an induction-capable proof checker, `punctual`
  bounded execution. Each of these has been adversarially probed, and
  each probe that compiled clean became a filed gap rather than a
  quiet one.

What is *not* done is the wiring. Chapters exist that are not yet the
default path, and a handful of capabilities still lean on host tools we
intend to delete.

## Gaps, In Rough Priority

The order is the priority. There are no dates — every estimate this
project has made has been wrong by orders of magnitude in both
directions.

### 1. The capability model is enforced at compile time, not at runtime

Still the highest-priority gap, because it is a *safety claim we make in
public* that the compiler does not yet fully keep — but the shape of it
changed on 2026-07-13 and the old three bullets here were all wrong.
Scope **is** checked now (CDX4002, and it is three relations, not one: a
path prefix, a network authority, a console channel). The GPU compute bit
**is** consulted. `opening`-as-a-value **is** caught. What is left:

- **Scope is now enforced at RUNTIME for every relation that has a
  runtime resource.** The process table carries scope data beside the
  cap bits, and the two families whose resource survives to runtime are
  gated: the **filesystem** path at the three Fat16 gates (fester,
  2026-07-14) and the **network** authority at the `http-request`
  chokepoint (val, 2026-07-15) — both fed by a boot ring, a spawn-copy,
  and a verified-loader ring, pinned by the `scope-runtime-*` and
  `network-scope-*` tests plus the two `loader-*-scope-test`s. The
  **console** channel has no runtime resource (it is fixed at compile
  time by the builtin name and never becomes a runtime value), so it is
  compile-time-complete and needs no cell. What is left under 1.5 is
  narrow: the compiler-emitted manifest scope is verified by inspection,
  not by a battery test, and the boot/loader grants still disagree about
  *direction* (BACKLOG 1.12). (BACKLOG 1.5.)
- **`cap-gpu-memory` is granted and read by nobody.** The compute half is
  gated on the port window; the memory half has no window to hang on.
  Either device buffers get a real kernel allocation syscall, or
  `KingsAndCourts.md` stops implying the bit is enforced. (BACKLOG 1.6.)
- **The `Network` effect is implemented, but only over HTTP.** `fetch`,
  `post` and `resolve-dns` are real in `Net chapter HttpFetch` (not the
  foreword — an implementation needs `NetIO`, which is `codex.os`), over
  the NE2K → NetIO → TCP stack, with the correct `[Network.Read,
  Network.Write]` rows and network-scope checked at compile time
  (CDX4002). `codex/test/network-effect` compiles clean (no CDX2040) and
  passes headless. What is left: an `https` URL is **refused**, because
  there is no TLS on the transport (BACKLOG 1.9); and the live wire path
  (a real GET/POST and a name resolved on the wire) is exercised by hand,
  not by any gated test — `network-effect` is not in the BVT, so a
  regression in `HttpFetch` is caught by nothing at `build.ps1`. (BACKLOG
  1.7 is closed; the successor is 1.9.)
- **The kernel quires are exempt from effect checking entirely.**
  `Kernel`, `Dev`, `Os` and `Net` are on `quire-effect-exempt`, so a
  driver there touches ports and MMIO while typed pure. `Boards` came off
  that list; the kernel has not, and nobody has decided whether it should.
  Until then, "effects are explicit" has a hole the size of `codex/os/`.
  (BACKLOG 1.8.)
- **Effect-row subtyping is incomplete.** Subset-checking at application,
  not a real effect system: a generic parameter carries no effect
  constraint, so an effectful function passed to a `map`-style `(a -> b)`
  is not caught. (BACKLOG 1.3.)

Detail: `docs/Designs/Active/Language/CAPABILITY-REFINEMENT.md`.

### 2. First-boot ceremony on real hardware

The ceremony itself landed: `apps/works/GopWizard.codex` and
`GopBoot.codex` generate an Ed25519 identity from RDRAND entropy,
persist it wrapped, and detect it on the next boot. It is pinned by
battery probes (`gop-wake`, `gop-fat16`, `disk-write`, `fat-write`).

What remains is validation on a physical machine, which is live work in
fester's stream. See `docs/Designs/Active/Hardware/BootRoadmap.md`.

### 3. USB install from inside Codex

The USB MSC driver, DriveManager, DevConsole "Install to USB", and the
xHCI transfer rings are all built. What remains is end-to-end validation
on a physical stick — also live in fester's stream.

Acceptable interim: `build/flash-usb.ps1` performs the *first* install
only; after that, Codex-on-Codex installs (one stick reflashing another)
must work without leaving the system.

### 4. Repository protocol replaces Perforce

**Still the largest unrealized piece of the founding vision, but the
store layer is now built.** The vision document opens by proposing to
delete GitHub. We have not yet deleted Perforce — but the content-addressed
store beneath the cutover is real and dogfoodable (BACKLOG 6.1, val,
2026-07-15).

The parts exist — `RepoProtocol`, `KeyManager`, `Annotation*`,
`BuildRecord`, `Historian`, `SignedAnnotation`, `Discussion` are all
built in `apps/works/`. The store itself now works end to end:

- **Source-as-facts — DONE.** A `.codex` file round-trips through
  `DiskFacts` as a content-addressed, Ed25519-signed fact, and a real
  tool (`tools/cdx-store`) writes one in. (BACKLOG 6.1.)
- **Import-by-hash and the trust gate on import — DONE.** The compiler
  resolves a `quotes` by digest out of the store through the four
  guards, blob or no blob. (BACKLOG 6.1b.)
- **History walking for definitions — DONE.** `repo-index-from-disk`
  gives a tree, lookup-by-path, and lookup-by-hash; every superseded
  edition stays addressable forever; the index persists as a kind-40
  snapshot (6.1f); removal is authenticated and replay-resistant (6.1c).
- **Replication over the wire — DONE at the store layer.** `FactSync`
  reconciles two stores conflict-free and rides `TrustTransport`
  (`MsgSyncOffer`/`MsgSyncReply`, 6.1g) — but only in-process today; a
  live two-peer sync across booted guests, and federation as the
  *source-of-truth* path, remain.
- **Cutover — NOT started.** Dual-store while federation is proven, then
  the P4 depot becomes a frozen mirror. This, peer resolution (6.2), and
  the multi-disk story are the unowned remainder — everything below the
  store is built; making the store *the* store is not.

### 5. Pure Codex VMX host — retire `codex-vm.exe`

`tools/codex-vm.exe` is ~6,000 lines of C wrapping the Windows
Hypervisor Platform. It has *grown* — it was ~4,500 lines when this gap
was first written. Every feature we add to it is a feature we will have
to reimplement or abandon.

Codex already has the VMX builtins (`vmxon`, `vmlaunch-full`, `rdmsr`,
`wrmsr`) and `DevHypervisor` is the orchestration layer. The missing
piece is the boot path: today `DevHypervisor` runs inside a Codex
program running inside `codex-vm.exe` running on Windows.

- Kernel-mode VMX entry on bare metal (post-EBS: the kernel sets up the
  VMXON region and runs guest VMs directly).
- Nested boot: host Codex boots from USB, loads a guest `Codex.cdx` into
  a VMX-isolated partition, each gate runs in its own guest.
- Move `VmSerial` / `VmIde` device models into kernel space so they are
  available pre-EBS.

This unblocks the no-host-OS story entirely.

### 6. Agent acquisition — the bundled path

`AgentAcquisition` has bundled / local / network paths. Bundled is the
one the USB-stick promise depends on, and it has not been touched since
CL 2981.

- An actual GGUF model on the image.
- A verification gate: after the image is built, the bundled agent's
  manifest signature checks out and `agent-runtime-init` loads it with
  no external tools.
- DevConsole "Agent Manager" wired through to register / inspect / swap.

### 7. Editor and debugger maturity

- Syntax highlighting in GUI mode (the UI substrate has the primitives;
  they are not wired).
- F5: compile-and-run the current chapter through `vm-compile` /
  `vm-run-cdx`.
- Watch expressions in the debugger.
- Real debug info emitted into the CDX, so backtraces stop relying on a
  heuristic stack walk plus the MAP1 symbol map.

### 8. Phase discipline — finish the compiler's memory story

From `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`:

- **Precise escape roots for CHECK and LOWER.** PARSE and SCOPE are
  pinned; CHECK and LOWER have no typed root walk, so the conservative
  scan cannot tell a real escape from an integer that looks like a
  pointer. The copying compactor is blocked on this, and only on this.
- Remove the TCO reset (phase boundaries replace within-phase reclaim).

Each one moves the heap high-water mark down, which is what decides
whether compile-on-stick succeeds on a lower-RAM board.

### 9. Spark WebGPU Studio

Spark is an 89-module creative suite compiled to WASM — the first Codex
application a non-Codex user would ever see. It is blocked one step from
running:

- `wat2wasm` rejects the emitted WAT: `undefined function variable
  "$AbsorbedDose"`. A unit type from the punctual foreword is referenced
  but never emitted by the WASM plug — most likely missing from the
  plug's function export table. Nobody has looked at it.
- Mesh CSG booleans exist in Codex but are not wired through the UI.
- ~1,100 lines of JS in the HTML harness should be emitted from Codex.

### 10. The apps are lifted by breakage class

**Not a gate, and not a battery.** Damian's call, 2026-07-14: the apps
battery costs a full hour, there are no customers, and nobody is
complaining. It is deliberately not run. The bugs will keep.

What the apps need is not a sweep of *apps* but a sweep of *defects*.
They are not N broken programs — they are a handful of mechanical
breakages repeated across N programs, because the language moved and the
apps did not. Filed per app, one defect gets diagnosed five times; that
is exactly what happened to `[Console] None`, which was found in
`apps/works`, fixed there, and left standing in **seven other app entry
points**.

So we fix what we see, and when we see it we lift the whole class. The
classes are enumerated in BACKLOG **7.16**. Two of them cost nothing to
find — they are pure grep, no compile and no battery — and between them
they unbreak seven entry points:

- **`None` used as a type** (it is a `Maybe` value constructor; the type
  is `Nothing`) — 8 files.
- **`fn x ->` lambda** (Codex is `\x ->`) — 5 sites, 2 files.

The rest (undeclared effect rows, bounded-signature violations,
multi-line applications) need a compile to enumerate, which is a
**type-check sweep on demand** — no boot, no run, no hour. BACKLOG 7.11
keeps the *gate* on the register as a wanted capability, because we do
not walk one back; it is simply not what we are buying today.

## A Note on the Fleet

Check `p4 changes -m 20 //Codex/main/...` against the agent streams
before starting a campaign. As of 2026-07-14 all four agents (blu,
fester, reek, val) have copied up and main is current, but that was not
true 24 hours earlier and will not stay true for long — main moved
through ~90 changelists on 2026-07-13 alone, and agents merged down from
it four and five times in a single session. Expect it to move under you.

## Cross-References

- `docs/PM/BACKLOG.md` — every open capability, itemized.
- `docs/VisionAndVirtues.md` — the founding vision behind this gap list.
  Read it before redesigning anything in gap 4.
- `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md` — gap 8.
- `docs/Designs/Active/Language/CAPABILITY-REFINEMENT.md` — gap 1.
- `docs/Designs/Active/Hardware/BootRoadmap.md` — gaps 2 and 3.
