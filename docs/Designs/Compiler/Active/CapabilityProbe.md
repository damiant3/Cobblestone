# Capability Scoping — Stage-0 Probe Results

**Status:** STAGE 1a SHIPPED (blu CL 6923, 2026-07-03) - Device.Port
on the port-* intrinsics + quire-level exemption for the owned
hardware stack. cap-launder-pure-io flipped to errors/.failing; the
port-I/O leg of "no undeclared I/O" is now enforced for foreword and
app code. Remaining: Device.Block + Fat16 annotation, poke-mmio, and
the manifest/OS wiring (stages 1b-4 below). Stage 0 (probes) complete.

**Provenance:** BACKLOG "Fulfill the Vision Check" item 1, capabilities
leg. blu, 2026-07-03.

---

## 1. The claim under test

- KingsAndCourts CRA 1(a): effect types give "no undeclared I/O",
  1(b) "Empty capability tables ... secure by default", 2(a)
  "Content-addressed CDX ... capabilities manifest".
- CodexIoTPlan: "signed, capability-scoped binaries ... a firmware
  update that requests capabilities not granted by the device's trust
  policy is rejected at load time - not at runtime, not after
  deployment. Every CDX binary carries an Ed25519 signature, a
  SHA-256 content hash, and an explicit capabilities manifest."
- VisionAndVirtues / effect-laundering framing: "a compromised library
  cannot silently exfiltrate data because the effect would not
  type-check."

## 2. The mechanism as built

Three layers exist and are individually real, but the pipeline is not
stitched together:

1. **Compile-time capability check** (`check-opening-capabilities`,
   TypeChecker.codex:2764) compares `opening`'s declared effect row
   against `granted-capabilities` (TypeChecker.codex:2761) — a
   hardcoded list of *everything* (Console, FileSystem, Network,
   Concurrent, Device, Gpu.*). It never writes anything into the
   binary.

2. **The CDX manifest is hardcoded empty.** `build-cdx`
   (CdxWriter.codex) emits `cap-sz = 0` unconditionally; the
   bare-metal header path (X86_64Chapter.codex) writes `cap-off = 224,
   cap-sz = 0`. No effect or capability bytes are ever appended.

3. **The verifier** (CdxVerifier.codex, five phases: Integrity,
   Author, Capabilities, Effects, Proofs) decodes the manifest and
   runs each entry through `eval-policy`, and checks each declared
   effect has a declared capability. Because the compiler emits
   `cap-sz = 0` and `eff-sz = 0`, **Phases 3 and 4 are vacuous no-ops
   on every compiler-produced binary.** Nothing anywhere scans the
   text section for actual intrinsic/syscall usage — the check is
   declaration-vs-policy, never behavior-vs-declaration.

4. **The kernel** has real per-process capability bits (cap word at
   `proc-cap-offset = 56`, checked by `emit-check-capability` in
   syscall handlers) — but boot grants process 0 *every* capability
   unconditionally (X86_64Chapter.codex), children inherit the
   parent's word, and the verified loader's computed grant bitmask
   (VerifiedLoader.codex) is never wired into the process table.

## 3. Stage-0 probe results (seed 47CABCEA)

Three probes. **All three compile clean and run**, pinned as passing
`.expected` tests in `codex/test/cap-launder-*`.

| Probe | Route | Today |
|---|---|---|
| cap-launder-pure-io | a function with a PURE signature drives a hardware I/O port via `port-out-byte` / `port-in-byte` | compiles; port I/O with no declared effect |
| cap-launder-pure-poke | a pure-signature function reads/writes arbitrary physical memory via `poke-byte` / `peek-byte` | compiles; unscoped raw memory access |
| cap-launder-manifest | `opening` declares a real effect; the shipped binary's capability manifest is empty | compiles; nothing for a policy to inspect or deny |

The root of the first two: the raw intrinsics are bound with an
**empty effect row** (TypeEnv.codex:243-250 — `port-in-byte`,
`port-out-byte`, `port-in/out-16/32`, `peek-byte`, `poke-byte`,
`block-read-sector`, `block-write-sector` are all `FunTy ... empty-row
...`). `print-line` carries `Console` and `read-line` is
`EffectfulTy [Console]`, so the effect machinery exists and is
enforced one layer up — but the primitive that actually touches
hardware opts out of it. "No undeclared I/O" holds for the library
wrappers and fails for the intrinsics beneath them.

## 4. Reading the results

This is the effect-laundering hole at the intrinsic layer, plus an
unwired pipeline:

1. **Intrinsics escape the effect row.** The strongest capabilities in
   the system (port I/O, foreign physical-memory writes, block device)
   are typed pure. Any function, regardless of signature, can use them.
   The effect discipline that would make I/O visible in the type is
   simply not applied to the operations that perform it. (Note for the
   fix, expanded in section 6: `peek-*` reading the program's own heap
   is not in this set — it is runtime plumbing, not a capability.)

2. **The manifest is not derived from anything.** It is hardcoded
   empty, so the verifier's capability and effect phases — which are
   correctly implemented — have no input. `granted-capabilities` being
   a grant-all list means even the compile-time check is a formality.

3. **The load-time rejection claim has no subject.** The verify ->
   loader -> registry path genuinely rejects a policy-denied manifest,
   but no compiler-produced binary carries a non-empty manifest, and
   the kernel grants all caps at boot regardless. The claim describes
   a design, not the shipped behavior.

## 5. What this is NOT

No change shipped. The probes pin the permissive behavior; each flips
to `errors/.failing` as enforcement lands. A fix campaign spans
compiler AND OS: (a) give the security-relevant intrinsics real effect
rows so a pure function cannot call them; (b) derive the CDX manifest
from the def's actual effect/intrinsic usage at emit time and write
real `cap-sz`/`eff-sz` bytes; (c) replace `granted-capabilities`
grant-all with the device trust policy; (d) wire the verified loader's
grant bitmask into the kernel process table and stop granting all caps
at boot. Step (a) is the type-system core; the rest is OS plumbing.

## 6. The fix is NOT "compiler-only clean" — reconnaissance (2026-07-03)

An earlier draft of this doc called step (a) "the highest-value single
fix ... purely a TypeEnv + effect-check change." That is wrong, and
correcting it is the honest first step before anyone starts the
campaign. Measuring the blast radius:

- The nine raw intrinsics (`port-in/out-byte/16/32`, `peek-byte`,
  `poke-byte`, `peek-32`/`poke-32`, `peek-qword`, `block-read-sector`,
  `block-write-sector`, and the `block-*` family) have **837 call
  sites across 79 files** — the kernel HAL (`Xhci` 23, `VirtioPci` 27,
  `DiskFacts` 47, `DriveManager` 22, `Ne2k` 11, `Vga` 13, ...),
  foreword (`Fat32` 74, `Gpt` 52, `GpuRender` 21, `Sha256` 5), the
  board drivers, and the dev tools.
- Because effect rows propagate (a body that calls a `[Device]`
  intrinsic gets `[Device]` inferred, and a declared signature that
  omits it errors at the def boundary — the enforcement EffectRows
  already ships), giving these intrinsics an effect **cascades that
  annotation through every one of those files and their transitive
  callers**, and the cascade reaches app code through the foreword
  modules (`Fat32`, `Sha256`, `GpuRender`) the apps depend on. This is
  the exact shape the BoundedSignatures campaign hit with bounded
  parameters, which is why it chose `__narrow`-at-store (zero cascade)
  over param bounds (cascading error). A naive effect on the
  intrinsics is the cascading choice.
- **It gates the seed.** The selfhost compiler calls these itself:
  `serial-byte (b) = port-out-byte 1016 b` (`opening.codex:191`) is the
  compiler's own serial output, so the effect would thread up through
  every diagnostic-emitting function to `opening`; and the emit-phase
  heap scanner (`pmap-walk` in `X86_64Compound.codex`) reads memory via
  `peek-qword`. The full battery and the fixed-point gate would be red
  until the compiler's own guts are annotated or exempted.

The sharper design insight the recon surfaces: **not all of these are
capabilities.** `peek-qword` / `peek-byte` reading the program's OWN
heap is how the GC-less runtime walks its structures — categorically
not an I/O capability, and the dominant share of the 837 sites. The
security-relevant set is narrow: `port-*` (hardware I/O),
`block-*-sector` (storage), and `poke-*` to a NON-heap (foreign)
address. Carving those from the runtime plumbing shrinks the blast
radius by a large factor and is also more correct — a capability
system that flags heap-structure reads as "I/O" is crying wolf.

**The ruling this needs before it starts** (Damian's call, analogous to
`__narrow`-vs-param-bounds):
1. **Granularity.** Which intrinsics carry an effect? Proposed: only
   `port-*`, `block-*-sector`, and foreign `poke-*` (the last needs a
   heap-vs-foreign distinction the type system does not currently
   make — likely a separate `poke-foreign` intrinsic, leaving
   `poke-byte`/`poke-32` for heap writes effect-free). `peek-*` for
   owned-heap reads stays pure.
2. **Effect name(s).** One `[Device]`, or split `[Port]` / `[Block]` /
   `[Mmio]`? Finer names give the manifest real granularity for
   step (b) but multiply the annotation surface.
3. **The trusted zone.** The kernel HAL and the compiler's own serial
   runtime genuinely perform hardware I/O — they cannot be "fixed,"
   only DECLARED. Do those modules annotate every function with
   `[Device]` (honest but heavy — hundreds of signatures), or is there
   a module-level trust boundary (`@doctrine trusted-hal` or similar)
   that lets a designated layer use the intrinsics without per-function
   declaration, so the effect discipline means "no UNDECLARED I/O in
   application code" rather than drowning the HAL? The whole point of
   the capability claim is the application/library boundary, not the
   HAL's internal plumbing.

That ruling is now made — section 7. The honest status: the pure-I/O
gap is real and pinned (`cap-launder-pure-io`, `cap-launder-pure-poke`),
the fix is a genuine cross-cutting campaign with a decided shape, and
it should not be started as a "quick TypeEnv change." The claim surface
(ClaimsCalibration) records the true scope.

## 7. Ruled design (Damian, 2026-07-03)

The guiding principle: **effect declaration carries information only at
the boundary where someone consumes code they did not write and cannot
personally review.** At a hardware boundary the declaration is theater
— a NIC driver's type saying `[Device.Port]` tells a reader nothing;
of course it does I/O, that is its whole job. The discipline earns its
keep at exactly one place: installing usermode / library code from an
external source and asking "what is this thing going to do." So the
trust boundary is drawn at the **quire** — which is already the unit of
provenance and trust — not per-function or per-chapter.

### 7.1 The exempt set (hardcoded, the owned hardware stack / TCB)

| Quire | Status | Why |
|---|---|---|
| `codex` (compiler runtime) | EXEMPT | `serial-byte`, the emit heap scanner — plumbing we wrote |
| `codex.kernel` | EXEMPT | raw drivers; I/O is the point |
| `codex.os.*` | EXEMPT | kernel/HAL; I/O is the point |
| `codex.boards` | EXEMPT | board HAL; "we know the boards need io" |
| `codex.plugs.*` | EXEMPT | trusted build tools / runtime emitters |
| `codex.foreword.*` | **ENFORCED** | the library surface external code consumes — the declarations are the value here, so we WANT them |
| apps / external usermode | **ENFORCED** | the actual threat surface: "what will this thing do" |

Foreword is deliberately NOT exempt. It is the code an outside author
pulls in and reads; `Fat32 : Path -> [Device.Block] Text` at the
library boundary is the manifest-in-miniature the consumer needs. The
handful of foreword modules that do real I/O (`Fat32` ~74 sites, `Gpt`
~52, `GpuRender` ~21, `KeyboardLayout`) declare their effects. This is
the demonstration surface for the whole mechanism.

### 7.2 The mechanism — route exempt defs through infer-and-propagate

Exemption must NOT be implemented as "suppress the mismatch error." A
declared `serial-byte : Integer -> Integer` whose error is merely
skipped keeps its pure declared type, so its callers see pure and the
effect is SWALLOWED — which silently breaks manifest correctness for
any external code downstream. Instead, an exempt-quire def is treated
like an **undeclared def**: its written row is ignored, a fresh row
variable is synthesized on the performing arrow, and the body's ambient
effects unify into it (the existing path at TypeChecker.codex:735). The
effect is therefore INFERRED and PROPAGATES to callers through
resolution — an external app calling exempt `Fat32`... (were foreword
exempt) or the enforced foreword calling exempt `block-read-sector`
still surfaces `Device.Block` in the caller's inferred row. Exemption
removes the declaration OBLIGATION on owned-stack code; it never
removes the effect from what callers see. Propagation-through-the-TCB
is the whole point.

Non-exempt quires use the normal declared-row check
(`check-effect-row-subset` / `cdx-effect-undeclared`): a written
signature that omits an inferred hardware effect is an error, so the
foreword and every app must state what they do.

### 7.3 Granularity (on the merits, independent of the quire ruling)

| Intrinsic | Classification | Effect |
|---|---|---|
| `port-in/out-byte/16/32` | hardware I/O | `Device.Port` |
| `block-read/write-sector`, `block-*` | storage | `Device.Block` |
| NEW `poke-mmio` / `read-mmio` | memory-mapped device I/O | `Device.Mmio` |
| `peek-byte/32/qword` | reads OWN heap | pure (a heap read is not I/O even in external code) |
| `poke-byte/32` | writes OWN heap | pure |
| `alloc-bytes` | allocator | pure |
| `flush-tlb` | CPU control | pure for now (privileged, not exfil; revisit) |

Dotted sub-effect names use the existing lattice (`effect-covered-by`,
as `Console.Read` / `Gpu.Compute` already do): `Device.Port`,
`Device.Block`, `Device.Mmio` are all `⊆ Device`, so the manifest gets
real granularity (a storage device requests `Device.Block`, not blanket
`Device`) and a policy can grant `Device` broadly or `Device.Block`
narrowly — all through shipping machinery.

**The MMIO trap:** `poke-byte addr val` is the same intrinsic whether
`addr` is heap or a device register, and the type system cannot tell —
the address is a runtime value. So MMIO I/O cannot be caught by typing
`poke-byte`; it needs a dedicated `poke-mmio` / `read-mmio` intrinsic
carrying `Device.Mmio`, with the real MMIO sites (VGA framebuffer,
device BARs) migrated to it. `poke-byte` stays pure for heap.

### 7.4 Build impact

- **Seed stays byte-identical.** Effects erase at codegen, and the
  compiler quire is exempt, so no error fires and no machine code
  changes — the fixed-point gate does not notice.
- **Battery-gating, bounded:** the foreword I/O modules (`Fat32`,
  `Gpt`, `GpuRender`, `KeyboardLayout`) must be annotated before their
  `-Apps`/`-FW` tests compile clean. That is the seed-adjacent work
  item and it is small — the compiler itself does not cite these
  (its I/O is `port-out-byte` + `peek` inside the exempt `codex`
  quire), so they are not in the seed compile set.
- The quire-exemption check keys on `def.chapter-slug` (the concat
  prefixes chapters with their quire dir), a hardcoded prefix list in
  the effect-check boundary.

### 7.5 Campaign stages (for whoever starts it)

1a. **Device.Port + quire exemption. SHIPPED (blu CL 6923).**
   `port-*` intrinsics carry `Device.Port` (TypeEnv). Exemption keys on
   the chapter-slug quire prefix (`slug-quire` / `quire-effect-exempt` /
   `def-effect-exempt` in TypeChecker) against a hardcoded owned-stack
   list (Opening + the compiler subdirs + Kernel/Net/Sched/Dev/Trust/
   Observe/Replay/Verify/Os/Boards + Riscv/Arm64/Pe/Elf/Img). AS-BUILT
   DISCOVERY: making an intrinsic effectful trips TWO checks, not one —
   the def-boundary row-subset check (CDX2031) AND the effectful-let
   check (CDX2033, `check-let-bind-row`, which fires on `let w =
   port-out-byte ...`, the pattern the kernel USB/xHCI drivers use). A
   def-level guard covers 2031 but 2033 fires inside `infer-expr` and
   does not know the quire. The fix: an `effect-exempt : Boolean` field
   on `UnificationState`, set per-def in `check-def-normal`, read by
   both checks. Flips `cap-launder-pure-io` to errors/.failing (2031 +
   2033); adds `cap-device-declared` positive guard. One-pass fixed
   point, battery 306/291/0/15, self-verify green. The compiler's own
   `serial-byte` (exempt `Opening`) proves the exemption; the kernel USB
   drivers (exempt `Kernel`) prove the CDX2033 arm.
1b. **Device.Block + Fat16 annotation + poke-mmio. SHIPPED (blu CL
   6932 + seed).** All six `block-*` intrinsics carry `Device.Block`
   (TypeEnv; nullary `block-sector-count` becomes an `EffectfulTy`
   value — infer-name proves a bare mention performs its effects, so
   the two `-Apps` callers declare `[Device.Block]` on `opening`). New
   `read-mmio` / `poke-mmio` intrinsics carry `Device.Mmio`: TypeEnv
   bindings, NameResolver known-name list, byte-width helper stubs
   cloned from peek-byte/poke-byte in X86_64Helpers — the complete
   wiring for a new intrinsic is exactly those three files.
   AS-BUILT DISCOVERY — "annotation" of an enforced module means
   ACT-CONVERSION, not a signature sweep. CDX2033 forbids an effectful
   let outside an act-bind REGARDLESS of the enclosing def's declared
   row (probe-verified: `let w = port-out-byte p b` inside a declared
   `[Device.Port]` def still errors; the declaration only satisfies
   CDX2031). The legal non-act positions for an effectful call are
   tail position, when-scrutinee, and argument position (all
   probe-verified against the 1a seed). Fat16's read path therefore
   took: 6 `act buf <- block-read-sector ...` conversions with 6 new
   step-helper defs (fat16-parse-bpb, fat16-scan-root-step,
   fat16-scan-cluster-step, fat16-read-cluster-step,
   fat16-list-root-step, plus inline int-mod in fat16-next-cluster),
   and 3 de-let rewrites (find-in-cluster-dir, walk-path,
   walk-path-sub move the effectful call into when-scrutinee /
   argument position). Bootstrap subtlety: the pre-1b seed types the
   intrinsics pure, so stage 0 flags only the declared-row USER-call
   lets while the new compiler flags the INTRINSIC lets — the act form
   is legal under both typings, which is what keeps the two-pass
   bootstrap green. Two-pass build (new helper stubs change every
   binary), converged one-pass on rebuild; seed CF0FF221 47473EF7...
   Probes: errors/cap-launder-pure-block + errors/cap-launder-pure-mmio
   (2031+2033); cap-launder-pure-poke RENAMED cap-heap-poke-pure (a
   positive guard for the ruled heap-is-pure classification, no longer
   a laundering probe); cap-device-declared extends to Device.Block +
   Device.Mmio declared functions.
   STAGE 2 ESTIMATE REVISED: Fat32 (~74 sites) and Gpt (~52) are
   act-conversion campaigns of the same shape as Fat16, not signature
   sweeps — plan accordingly.
2. **Foreword annotation. SHIPPED (blu CL 6958 + docs 6963).** The
   real module set differed from the probe estimate in both
   directions: KeyboardLayout uses ONLY heap peek/poke (pure by the
   7.3 ruling — needed nothing), while **InputSource** (foreword/ui,
   missing from the probe list) drives the mouse/keyboard ports and
   joined the scope, pulling **AppRunner** (bare-app-tick /
   bare-app-render) in transitively. Shipped set: Fat32 (22 intrinsic
   sites, ~30 defs in the [Device.Block] closure), Gpt (11 sites, ~12
   defs incl. `gpt-read` as an effectful VALUE `[Device.Block] Maybe
   GptDisk` — the nullary pattern), GpuRender (3 tail defs incl.
   `gr-clear-depth` as an effectful value), InputSource
   (raw-input-poll act-converted, 5 ordered port binds), AppRunner
   (tick/render act-converted). Conversion shapes: act binds for
   multiply-used sector buffers with step helpers for the multi-line
   pure continuations; when-scrutinee and argument-position de-lets
   where the value is used once; pure builder helpers extracted for
   the long poke chains (fat32-format-bpb/-fsinfo/-root,
   fat32-write-entry-fields, fat32-mkdir-dot-entries,
   gpt-build-protective-mbr, gpt-build-header).
   NEW LANGUAGE TRAP (CDX1070): inside an act block, a statement line
   whose first token is argument-like (a literal or `(`) after an
   application statement is rejected REGARDLESS of column — only
   name-headed or keyword-headed lines start new statements. So a
   trailing `True` statement is unwritable; the idiom is a
   `<chapter>-done : Integer -> Boolean` helper wrapping the last
   effectful call in argument position (fat32-done / gpt-done).
   Gating: foreword-fat32 + foreword-gpt (-FW) compile clean; NEW
   foreword-apprunner test added — the ui device closure previously
   had NO compile coverage anywhere in the battery, the exact
   dark-ship shape the plug gate closed. NO seed change: none of the
   five modules is in the seed compile set (verified against the
   concat).
3. **Manifest derivation. SHIPPED (blu CL 6994 + seed FC795D76...).**
   The emitter derives both sections from opening's registered type
   (X86_64Chapter, Capability Manifest section): the effects section
   lists every effect name on the performing spine (le16 count, then
   le16 len + CCE bytes per name — the verify-quire wire format); the
   capabilities section grants each effect's covering capability
   (dotted effects cover through their base: Device.Port -> Device;
   ids from the verify-quire table Console 0 / FileSystem 1 / Network
   2 / Concurrent 3 / Device 4 / Gpu.Compute 5 / Gpu.Memory 6) as
   direction=readwrite, empty-scope, unbounded-duration entries.
   PLACEMENT: the manifest sits AFTER rodata, INSIDE the hashed and
   signed content — text stays at file offset 224 because codex-vm
   skips a fixed 224-byte header, and the verifier reads both sections
   through the header offsets (cap-off@136/cap-sz@144 = real values
   now; the old proof-off/proof-sz slots @152/@160 are the effects
   section, as the verifier always read them). The sign step bakes the
   stored hash bytes into the generated sign program, so signature
   coverage of the manifest came for free.
   VERIFIER FIXES in the same CL: cdx-verify-content-hash previously
   recomputed over [cap-off .. EOF], which included the UNSIGNED MAP1
   debug tail — a pre-existing mismatch on every real binary that
   self-verify never noticed (it checks magic/signature/author-key
   only). The range is now [224 .. furthest section end] (text,
   rodata, caps, effs), which is behavior-identical for old binaries
   (cap-off was always 224, tail excluded now as the stored hash
   always intended) and covers the manifest on new ones.
   effect-has-capability gains dotted-prefix coverage (Device.Port is
   covered by a Device entry), mirroring the checker's
   effect-covered-by.
   TCB HONESTY BOUNDARY: the manifest reflects opening's REGISTERED
   type. For ENFORCED programs — apps, the actual threat surface —
   CDX2031 forces the declaration to be complete, so the manifest is
   checker-accurate. For exempt-quire programs (the compiler itself)
   it reflects the declaration only: the seed's manifest says
   Console+FileSystem though the TCB also drives ports — exactly the
   documented exempt-quire boundary ("a written -> T no longer proves
   purity inside the TCB"). Unmappable effect names (no cap id) get
   no capability entry, so phase 4 rejects rather than silently
   granting — the format extension point.
   Probe flip: cap-launder-manifest RENAMED cap-manifest-derived, a
   positive guard (nothing errors — the fix makes the manifest real,
   observable in the header of every emitted binary and consumed by
   VerifiedLoader's cdx-caps-to-bitmask, which is stage 4's input).
4. **OS wiring** — replace `granted-capabilities` grant-all with the
   device trust policy; wire the verified loader's grant bitmask into
   the kernel process table; stop granting all caps to proc 0 at boot.

Stages 1-2 are the type-system core (mine); 3-4 are OS plumbing that a
kernel-side agent can take once the effects exist to derive from.
Record the exempt-quire trust reduction in `TrustedComputingBase.md`
when stage 1 lands: inside an exempt quire a written `-> T` no longer
proves purity, by design — that is where the TCB boundary is drawn.
