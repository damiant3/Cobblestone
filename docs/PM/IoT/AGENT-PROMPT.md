# IoT Master Plan -- Agent Briefing

You are the planning agent for the Codex IoT initiative. Your job is
to produce the master plan-of-plans: the high-level engineering
documents that sequence the work from today's x86-64-only self-hosted
compiler to a multi-architecture IoT platform with regulatory
compliance as its primary market differentiator.

## Your Deliverables

1. **Architecture Design Document**: How the self-hosted compiler
   gains ARM Cortex-M (Thumb-2), ARM Cortex-A (AArch64), and RISC-V
   (RV32IMC) backends. This is a PORT, not greenfield -- both RISC-V
   and ARM64 backends were built and proven in the C# reference
   compiler (old/src/Codex.Emit.RiscV/, old/src/Codex.Emit.Arm64/).
   RISC-V was the project's first backend. Read the existing
   implementations before designing anything.

2. **HAL Design**: How GPIO, UART, SPI, I2C, ADC, and power
   management integrate with Codex's effect type system and linear
   resources. Each peripheral should be a linear resource that the
   type system tracks -- leaving an SPI bus open should be a compile
   error.

3. **Protocol Implementation Plan**: MQTT v5.0, CoAP (RFC 7252),
   and LwM2M v1.2 as Codex foreword chapters. These build on the
   existing TCP/UDP/TLS network stack in codex/os/net/. Sequence:
   CoAP first (simpler, UDP-based), then MQTT (TCP-based, broader
   ecosystem), then LwM2M on top of CoAP (device management).

4. **Compliance Evidence Architecture**: How the compiler generates
   CRA/ETSI/NIST compliance evidence as a build artifact. Map every
   regulatory requirement to the Codex feature that satisfies it.
   The compliance summaries in docs/PM/IoT/Compliance/ have the
   requirement-to-feature mappings started; complete them.

5. **OTA Firmware Update Design**: End-to-end secure update flow
   using CDX signed binaries, the LwM2M Firmware Update object
   (Object 5), and the existing CDX verifier. A firmware update
   that fails signature or capability verification must be rejected
   before any code runs.

6. **Test Strategy**: How each phase gets validated. The project's
   rule is "the build is the test" -- every change must pass gates
   (fixed-point compilation) and battery (runtime output matching).
   How does this extend to cross-compiled ARM/RISC-V binaries that
   can't run on the x86 host?

## What to Read First

Read ALL of these before writing anything (Rule 2: read before you
write):

### Mandatory (direct reads)
- `docs/PM/Stories/Vision/CodexIoTPlan.md` -- the strategic prospectus
  you are implementing
- `docs/VisionAndVirtues.md` -- non-negotiables that constrain every
  design decision
- `docs/DevelopersGuide.md` -- language syntax and type system features
- `docs/ArchitectsSketchbook.md` -- memory layout, register conventions
- `docs/OperatorsManual.md` -- build process, test harness, VM setup

### IoT Reference Material (in docs/PM/IoT/)
- `Compliance/` -- EU CRA, ETSI EN 303 645, NISTIR 8259, IEC 62443
- `Architecture/` -- RISC-V and ARM spec indexes
- `Protocols/` -- MQTT v5, CoAP, LwM2M, Matter/Thread
- `Hardware/` -- STM32, ESP32-C6, Raspberry Pi target specs
- `Reference/MarketData.md` -- verified market data

### Existing Implementations (read-only, do not edit)
- `old/src/Codex.Emit.RiscV/` -- the RISC-V backend (read all 3 .cs files)
- `old/src/Codex.Emit.Arm64/` -- the ARM64 backend (read all 4 .cs files)
- `codex/compiler/Emit/X86_64.codex` -- the current x86-64 emitter
  (this is the pattern to follow for new backends)
- `codex/compiler/Emit/X86_64Boot.codex` -- boot infrastructure
  (interrupt handling, process table, device I/O)
- `codex/compiler/Emit/X86_64State.codex` -- register allocation,
  temp rotation, code state management
- `codex/os/net/` -- existing network stack (TCP, UDP, TLS, DNS, etc.)
- `codex/foreword/core/` -- crypto primitives (Sha256, Ed25519, Aes, etc.)
- `codex/os/trust/` -- trust lattice, capability model
- `codex/os/verify/` -- CDX binary verifier

### Design Context
- `docs/PM/Stories/Vision/DistributedAgentOS.md` -- agent-centric OS

## Constraints

1. **No external dependencies.** Codex builds Codex. No linking to
   libc, no calling out to vendor SDKs. The HAL accesses hardware
   registers directly via memory-mapped I/O, same as the x86 drivers.

2. **The type system is the enforcement mechanism.** If a safety
   property can be checked at compile time, it must be. Runtime
   checks are the fallback, not the design.

3. **One thing at a time.** Each phase produces a working, testable
   deliverable. No "Phase 1 sets up infrastructure for Phase 2."
   Phase 1 blinks an LED on real hardware.

4. **Cross-compile first, self-host later.** The x86-64 compiler
   emits ARM/RISC-V binaries. The ARM/RISC-V binaries do not need
   to compile Codex in Phase 1.

5. **Compliance is a build artifact.** The compliance evidence must
   be generated automatically, not written by hand for each product.

6. **Read the existing backends.** The RISC-V encoder, the ARM64
   encoder, the ELF writer -- they exist. Do not redesign what is
   already proven. Port the designs.

## Output Location

Write your plans to `docs/Designs/IoT/Active/`. Create one document
per deliverable:
- `BackendArchitecture.md`
- `HardwareAbstractionLayer.md`
- `ProtocolStack.md`
- `ComplianceEvidence.md`
- `OTAFirmwareUpdate.md`

Each document should follow the project's existing design doc pattern:
state the problem, state the constraints, state the design, state the
open questions.
