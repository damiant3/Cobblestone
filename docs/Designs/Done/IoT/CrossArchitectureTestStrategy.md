# Cross-Architecture Test Strategy: Extending "The Build Is the Test"

> **Filed to Done 2026-07-15 (val):** the strategy is realized and cross-arch results are recorded; the remainder -- fix the 2 ARM64 Renode failures and decide gate-vs-out-of-band -- is tracked in BACKLOG 3.6. Moved out of Active to keep init light; reopen if that is picked up.

**Created**: 2026-06-12 (reek)
**Status**: Partially shipped -- cross-arch test results recorded
(docs/Test/*cross_results.md). **ARM64 is at 132 pass / 2 FAIL under
Renode, not full parity.** Pending: fix the 2 failures, and decide
whether the cross battery becomes a build gate or stays an out-of-band
tier. See "Where This Actually Stands" below.
**Upstream**: `docs/PM/IoT/AGENT-PROMPT.md` deliverable 6,
`docs/Designs/IoT/Active/BackendArchitecture.md` (phases B1-B3)

## Where This Actually Stands

The ladder below is the design. What is running today is narrower and
should not be described as parity:

- **ARM64 under Renode: 132 pass, 2 fail.** Two failures is two
  codegen or runtime divergences, and by the G4 definition below
  that is the highest-value signal the ladder produces -- a
  cross-architecture semantic difference. They are not triaged.
- **There is no cross-arch leg in `build/build.ps1`.** Nothing in the
  gate run compiles or executes an ARM64 or RISC-V binary. The cross
  results are produced out of band and recorded in `docs/Test/`.

So the two remaining decisions are concrete:

1. **Fix the 2 ARM64 failures.** Until they are green, "cross-arch
   parity" is a claim the tree does not support.
2. **Gate or tier?** Either the cross battery joins `build/build.ps1`
   as a real gate -- which means owning its wall-clock cost and its
   flake surface on every build -- or it stays a deliberate out-of-band
   tier run at milestones, in which case say so plainly and stop
   implying the gates cover it. The ladder below assumes the former;
   the practice today is the latter, and the gap between them is
   exactly the ambiguity that lets a "132/135" get written up as
   "135/135".

## The Problem

The project's acceptance regime is concrete: semantic-equivalence
of TEXT mode, byte-identical text round-trip, byte-identical CDX
fixed point, and the sample battery with `.expected` runtime
output matching -- all executed on the x86-64 host via codex-vm.
An ARM or RISC-V binary cannot run on that host, codex-vm is a
WHP x86 hypervisor, and self-hosting on target is explicitly
deferred. The gates must extend to binaries the host cannot
execute, without weakening anything that exists.

## Constraints

1. The x86-64 gates are unchanged and remain the root: new
   backends share the architecture-neutral pipeline (lex through
   lift), so any shared-IR change is already policed by the
   existing fixed point and battery. Zero failures before copy-up
   applies to the union of old and new gates.
2. Tooling rule: PowerShell + codex-vm is the environment. QEMU is
   the sanctioned fallback (`$env:USE_QEMU=1` exists today), and
   qemu-system-aarch64 / qemu-system-riscv64/32 on Windows is the
   only way to execute cross binaries without hardware -- accepted
   as the cross-arch analog of the existing fallback, not a new
   class of dependency. WSL stays GDB-only.
3. Determinism over coverage: gates must be byte-exact and
   scriptable; hardware-in-the-loop is a tier, not a gate.
4. Battery discipline from the known conditions applies: no other
   VM jobs during a battery run; QEMU cross batteries inherit the
   same contention rule.

## The Gate Ladder

Ordered by cost; each backend phase (B1/B2/B3) must climb the
ladder before its copy-up.

### G1 -- Encoder golden vectors (pure, runs in the existing battery)

Instruction encoders are pure functions: opcode fields in, 32-bit
words out. Each encoder chapter ships a sample
(`codex/test/arm64-encoder.codex` etc.) that encodes a curated
instruction inventory and prints hex; the `.expected` sidecar
carries the known-good encodings. Sources of truth for the
vectors: the frozen C# encoders in `old/` (transcribed outputs,
not executed -- the tree stays retired) cross-checked against the
ARM ARM / RISC-V spec encodings catalogued in
`docs/PM/IoT/Architecture/`. These tests run on x86 like any other
sample -- the cross encoders are exercised by every battery run
forever, at zero new infrastructure cost.

G1 covers: every instruction form the emitter uses, the `Li`
materialization ladders (the historical bug nests: RV64's 8-step
split, MOVZ/MOVK/MOVN chains, Thumb-2's 16/32 selection), branch
range edges, and the patch-application arithmetic (apply a known
patch table to a known buffer, expect bytes).

### G2 -- Cross-emit determinism (host-only)

For each target: compile the sample corpus twice from the same
source; the two binaries must be byte-identical. This is the
cross-arch analog of the text fixed point -- it cannot prove
correctness, but it catches nondeterminism (unstable iteration,
uninitialized state) which the x86 gates catch via fixed-point
divergence. Cheap: two compiles and a hash compare per sample,
batched in the existing REPL harness.

### G3 -- QEMU runtime battery (the cross `.expected` gate)

The existing two-phase harness generalizes: Phase 1 batch-compiles
on the x86 seed as today (mode `CDX` + target flag); Phase 2 boots
each output under the target's QEMU system emulator (`-M virt`
for AArch64/RV64/RV32; `-M netduinoplus2` or similar Cortex-M
machine for Thumb-2) instead of codex-vm, captures serial, and
compares byte-for-byte against the **same `.expected` sidecars**
the x86 battery uses. Same source, same expected bytes, any
architecture -- that identity is the whole point of the language,
and it makes G3 free of new test assets.

Harness work: a per-target stanza in `vm-config.ps1` (QEMU binary,
machine, UART chardev plumbing to the existing serial-capture
path, load address), and a `-Target` switch on `test.ps1`. Tests
whose behavior is legitimately architecture-dependent (none are
expected; HAL samples will be) get a `foo.<target>.expected`
override; absence of an override means the x86 sidecar governs.

### G4 -- Cross-architecture semantic equivalence (differential)

G3 already implies it, stated as its own gate so failures are
named correctly: for every sample, output(x86) == output(target).
A G4 failure with G1/G2 green means a codegen semantics bug --
the highest-value signal the ladder produces. The report
distinguishes G3-vs-expected from G4-vs-x86 so a wrong `.expected`
(test bug) is not confused with a divergence (compiler bug).

### G5 -- Hardware smoke tier (per release, not per CL)

Scripted but human-attended: flash the demo image to STM32 /
ESP32-C6 / Pi over their standard flash interfaces, capture UART
for N seconds, compare against a smoke `.expected` (boot banner,
LED-toggle trace lines, sensor read). Mirrors the existing
real-hardware bring-up practice on x86 (ASUS/Dell). A `.hw`
sidecar marks smoke samples; `test.ps1 -Hardware <target>` runs
the tier when a board is attached. QEMU cannot model the vendor
boot ROMs, clock trees, and flash controllers -- this tier is
where those live, and it is the only tier allowed to be flaky
without blocking copy-up (failures file as known conditions
instead).

### G6 -- Self-host pingpong on target (Phase 4 horizon)

When prospectus Phase 4 (self-hosting on ARM/RISC-V) begins, the
full existing gate set transplants: the target-native compiler
compiles the compiler source; stage1 == stage2 byte-identical
under QEMU, then on hardware. Nothing to design now beyond noting
that G1-G4 are exactly the preparation that makes G6 a milestone
rather than a debugging campaign -- and that the eventual
codex-vm-style ownership of the emulation substrate (a Codex ARM
interpreter, retiring QEMU) is a someday-item on the same axis as
retiring QEMU on x86, not a prerequisite for anything in this
program.

## What Each Backend Phase Must Pass

| Phase | Gates required green |
|---|---|
| B1 AArch64 | G1 + G2 + G3/G4 (QEMU virt) + existing x86 gates |
| B2 RV64 | same, qemu-system-riscv64 |
| B3 RV32IMC / Thumb-2 | same + G5 smoke on ESP32-C6 and STM32 |
| HAL / protocols / OTA | their samples enter the same ladder; OTA adds a scripted A/B + rollback rehearsal under QEMU (power-cut simulated by killing the VM at scripted points in the update flow) |

The OTA rehearsal deserves emphasis: every row of the failure
matrix in `OTAFirmwareUpdate.md` becomes a scripted QEMU test --
kill at download, kill at Gate B, kill mid-swap, corrupt a block,
present a wrong-key image, present a stale sequence number -- each
with a deterministic expected outcome (which bank booted, which
Update Result, which facts recorded). Security acceptance here is
rejection tests passing, byte-for-byte, forever in the battery.

## Memory and Time-Complexity Risk

The strategy adds compile work (each sample x N targets) and QEMU
boots. Mitigations: target batteries run as separate invocations
(not interleaved with the x86 battery -- contention rule), and the
per-CL requirement is only the targets the CL touches; the full
cross matrix runs pre-copy-up. G2 doubles compiles for touched
targets only. Harness changes are PowerShell-side; no compiler
memory impact. Verdict: wall-clock cost is real (estimate: one
additional battery-equivalent per affected target), heap risk nil.

## Open Questions

1. **QEMU provisioning.** Pin an exact QEMU version per target in
   `vm-config.ps1` (the x86 fallback already names QEMU-11.0.0 as
   a profile string); where do the binaries live -- `tools/`
   alongside codex-vm, or documented install? Recommendation:
   vendored under `tools/qemu/<ver>/` for determinism.
2. **Thumb-2 QEMU machine choice.** Which Cortex-M machine model
   best matches the STM32F4 memory map without vendor peripheral
   emulation; possibly emit a QEMU-profile variant (the profile
   mechanism exists: `ELF QEMU-11.0.0`).
3. **Timeout calibration.** Cross-QEMU boot times differ from
   codex-vm; the 30 s test timeout and the REPL no-output flake
   handling need per-target numbers, measured during B1.
4. **G5 cadence.** Per release, per seed rebuild, or weekly?
   Decide with Damian when hardware is on the desk.
