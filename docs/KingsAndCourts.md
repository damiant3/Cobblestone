# Kings and Courts — Hard Real-Time, EU Compliance, and IoT

How Codex meets regulatory requirements for safety-critical and IoT
deployments by construction, not by audit.

---

## The Claim

Most firmware vendors demonstrate compliance by producing paperwork
after the fact — SBOM spreadsheets, manual test reports, third-party
audits. The paperwork describes the system; it does not constrain it.
A compliant system and its evidence can drift apart the moment the
next commit lands.

Codex takes a different position: the compiler is the auditor. The
type system, the effect system, the capability model, and the
`punctual` keyword enforce the regulatory requirements as compile
errors. If the code compiles, the evidence is valid. If the evidence
would be invalid, the code does not compile.

This document consolidates the three regulatory frameworks Codex
targets, the language features that satisfy each requirement, and the
hard real-time story that ties them together.

---

## 1. Hard Real-Time — The `punctual` Keyword

### The Problem

Safety-critical systems (IEC 62443, EU CRA Article 6, IEC 62304)
require bounded worst-case execution time (WCET). Traditional
approaches rely on external tools (aiT, RapiTime for Ada Ravenscar)
or coding standards enforced by linters (MISRA-C). Neither is part of
the language — the guarantee lives outside the compiler and can be
violated silently.

### The Solution

The `punctual` keyword marks a function as having bounded execution.
The compiler enforces five structural restrictions at compile time:

| CDX Code | Restriction | Why |
|----------|------------|-----|
| CDX6001 | Cannot call non-punctual or non-safe-builtin functions | Transitivity — one unbounded callee breaks the guarantee |
| CDX6002 | Cannot use heap allocation | Bare-metal has no GC; heap allocation is unbounded in time |
| CDX6003 | Cannot use closures or lambdas | Unpredictable allocation from capture |
| CDX6004 | Must be effect-free (any effect rejected) | A handler's latency is unbounded from the caller's seat; I/O is only the worst case |
| CDX6005 | Cannot use self-recursion | Unbounded stack depth |

The emitter counts instructions per punctual function (CDX6010). An
optional budget warns when exceeded (CDX6011):

```
punctual 128 fast-handler : Integer -> Integer
fast-handler (n) = n + 1
```

Default budget: 256 instructions. The count is architecture-independent
(instruction count, not bytes or cycles). The compiler does not claim
wall-clock time — that depends on clock speed and pipeline, which is the
system integrator's responsibility.

### The Punctual Foreword

`codex.foreword.punctual` is an 8-chapter library where every function
is `punctual`. It provides the primitives for real-time code without
breaking the bounded-execution guarantee:

- **IntOps** — clamped add/sub/mul, abs, min, max
- **BitOps** — and, or, xor, shift, rotate, popcount
- **Saturate** — saturating arithmetic for fixed-point DSP
- **FastMath** — reciprocal, inverse sqrt, fast floor/ceil
- **Trig** — CORDIC-based sin, cos, atan2
- **ColorOps** — RGB/HSL conversion, blend, gamma
- **Kinematic** — velocity, acceleration, interpolation
- **Endian** — byte-swap, network-order conversion

### Prior Art Comparison

No production language has this combination of per-function, opt-in,
compile-time bounded-execution enforcement:

| Language | Mechanism | Scope | Compile-time? |
|----------|-----------|-------|:-------------:|
| **Codex** | `punctual` keyword | Per-function | Yes |
| Ada Ravenscar | Ravenscar profile | Global (entire partition) | Partial — needs aiT/RapiTime for WCET |
| Rust | None | — | No |
| MISRA-C | External linter rules | Coding standard | No — advisory, not enforced by compiler |
| Zig | `@setEvalBranch` | Comptime only | N/A |
| Erlang | `max_heap_size` | Per-process runtime limit | No |

---

## 2. EU Cyber Resilience Act (CRA)

Full mapping: `docs/Reference/CRA-Compliance-Matrix.md`

The CRA (Regulation 2024/2847) requires all products with digital
elements sold in the EU to meet essential cybersecurity requirements.
Codex maps each requirement to a language feature that enforces it by
construction.

### Evidence Classes

| Class | Meaning | Example |
|-------|---------|---------|
| BY-CONSTRUCTION | The language prevents the violation | Linear types prevent use-after-free |
| MECHANISM | A runtime mechanism enforces the property | OTA dual-gate verification |
| DEPLOYMENT | The provisioning process ensures the property | Per-device Ed25519 keypair |
| ORGANIZATIONAL | Process obligation, not a code artifact | Vulnerability disclosure policy |

### Key Requirements

| CRA Req | What | Codex Feature | Evidence Class |
|---------|------|---------------|:--------------:|
| 1(a) | No exploitable vulnerabilities | Linear types (no UAF/double-free), bounded integers (no overflow), effect types (no undeclared I/O) | BY-CONSTRUCTION |
| 1(b) | Secure by default | Empty capability tables, effect types enforce invariants | BY-CONSTRUCTION |
| 1(c) | Data protection | AES-GCM-256, ChaCha20-Poly1305, TLS 1.3, linear types prevent key aliasing | MECHANISM |
| 1(d) | Denial-of-service resilience | `punctual` instruction-count bounds, bounded integers, fuel-capped recursion, preemptive scheduler | BY-CONSTRUCTION |
| 1(e) | Attack surface minimization | No OS/libc/dynamic linker, effect types restrict capabilities, dead-code elimination | BY-CONSTRUCTION |
| 1(f) | Logging and monitoring | Effect-typed `[Audit]` channel, append-only FactStore with CRC framing | MECHANISM |
| 2(a) | Component identification | Content-addressed CDX, Ed25519 signatures, trust lattice provenance | MECHANISM |
| 2(b) | Vulnerability handling | OTA dual-gate verification, anti-rollback counter, capability lease revocation | MECHANISM |

### The Compile-Time Argument

Requirements 1(a), 1(b), 1(d), and 1(e) are satisfied BY-CONSTRUCTION.
This means no test, audit, or runtime check is needed — the compiler
rejects programs that would violate these requirements. The compliance
evidence is the fact that the code compiled.

For a traditional firmware vendor, demonstrating 1(a) (no exploitable
vulnerabilities) requires penetration testing, fuzzing, and manual
code review. For Codex, it requires showing that the type system
prevents the relevant vulnerability classes:

- **Buffer overflow**: No raw pointers. Bounded integers with compile-time
  range checking. `__narrow` for explicit narrowing with runtime trap.
- **Use-after-free**: `linear` types enforce exactly-once usage. Double-use
  is CDX2061. Leak is CDX2063.
- **Integer overflow**: `Integer between L and H` with `wrapping`,
  `clamping`, or `error` mode. No silent wrap-around.
- **Injection**: No eval, no shell, no string interpolation into queries.
  Effect types prevent undeclared I/O channels.
- **Unauthorized access**: Capability model with scoped permissions.
  Trust lattice with Ed25519 identity and direction markers.

---

## 3. ETSI EN 303 645

Full mapping: `docs/Reference/ETSI-303645-Mapping.md`

ETSI EN 303 645 is the European standard for consumer IoT security,
with 33 mandatory provisions across 13 categories. Codex satisfies or
provides mechanisms for all provisions where the manufacturer has
technical control.

### Coverage Summary

| Category | Provisions | Codex Coverage |
|----------|:----------:|:--------------:|
| 5.1 No default passwords | 3 | Satisfied — per-device Ed25519 identity, trust lattice |
| 5.2 Vulnerability disclosure | 1 | Organizational — FactStore audit trail supports process |
| 5.3 Software updates | 4 | Satisfied — OTA via LwM2M, signed CDX, anti-rollback |
| 5.4 Credential storage | 4 | Satisfied — linear types, bare-metal (no OS-level leaks) |
| 5.5 Secure communication | 3 | Satisfied — effect types enforce channels, TLS 1.3 |
| 5.6 Attack surface | 5 | Satisfied — capability whitelisting, no OS/libc/shell |
| 5.7 Software integrity | 2 | Satisfied — signed CDX, 5-phase verifier at boot |
| 5.8 Personal data | 2 | Satisfied — linear types, effect-typed data flows |
| 5.9 Resilience | 3 | Satisfied — `punctual` WCET, bounded integers, effect partitioning |
| 5.10-5.13 | 6 | Mixed — some organizational, some mechanism |

### The Bare-Metal Advantage

Most IoT firmware runs on an RTOS (FreeRTOS, Zephyr, ThreadX) with a
C/C++ toolchain. The attack surface includes the RTOS kernel, the C
runtime, the dynamic linker, and whatever debugging interfaces the
vendor forgot to disable.

Codex firmware is a single signed CDX binary. There is no OS beneath
it that you did not compile yourself. There is no C runtime. There is
no dynamic linker. There is no shell. The attack surface is the code
you wrote plus the code the compiler generated — and the compiler
itself is a fixed point of itself, so you can verify the toolchain too.

ETSI 5.6 (minimize attack surface) is not an aspiration for Codex.
It is the default state.

---

## 4. IEC 62443 (Industrial Automation)

Full mapping: `docs/Reference/IEC62443-Evidence.md`

IEC 62443 is the international standard for industrial automation
and control system (IACS) security. Codex maps to the component-level
requirements of IEC 62443-4-2 (SL-C levels) and the development
process requirements of IEC 62443-4-1.

### Security Levels

| SL | Meaning | Codex Posture |
|----|---------|---------------|
| SL 1 | Prevent casual or coincidental violation | Default — effect types, capability model |
| SL 2 | Prevent intentional violation using simple means | Signed CDX, trust lattice, linear resource discipline |
| SL 3 | Prevent intentional violation using sophisticated means | Full: WCET proofs, formal verification path (dependent types), content-addressed binaries |
| SL 4 | Prevent state-sponsored attack | Partial — requires hardware security module integration (future) |

---

## 5. The IoT Protocol Stack

Codex implements the standard IoT protocol suite, all as foreword
library modules:

| Protocol | Module | Purpose |
|----------|--------|---------|
| MQTT v5.0 | `codex/foreword/encode/Mqtt.codex` | Pub/sub telemetry and command |
| CoAP (RFC 7252) | `codex/foreword/encode/Coap.codex` | Constrained REST-like requests |
| LwM2M | `codex/foreword/encode/Lwm2m.codex` | Device management, OTA firmware |
| OTA Update | `codex/foreword/core/OtaUpdate.codex` | Dual-gate verification, A/B slots, anti-rollback |

### Board Support

Nine target boards with register-level drivers. Full details in
`docs/TinkersToolbox.md`. Summary:

| Board | MCU | Arch | Highlights |
|-------|-----|------|-----------|
| STM32F4 Discovery | Cortex-M4F 168 MHz | ARM | GPIO, UART, SPI, I2C |
| ESP32-C6 DevKit | RV32IMC 160 MHz | RISC-V | GPIO, UART, SPI, I2C |
| Raspberry Pi 4 | Cortex-A72 1.5 GHz | ARM | GPIO, UART, SPI, I2C |
| QEMU virt | AArch64 + RV | Both | PL011/16550 UART |
| nRF52840 DK | Cortex-M4F 64 MHz | ARM | BLE beacon PDU, SAADC, RADIO |
| RP2040 (Pico) | Dual M0+ 133 MHz | ARM | WS2812 NeoPixel via PIO, ADC |
| nRF9160 DK | Cortex-M33 64 MHz | ARM | Cellular modem IPC, AT parser |
| STM32L4 Nucleo | Cortex-M4F 80 MHz | ARM | Low-power modes, LPTIM |
| FE310 (HiFive1) | RV32IMAC 320 MHz | RISC-V | PWM, PLIC |

108 sub-tests total. All register addresses from official reference
manuals.

---

## 6. The Compliance Evidence Module

`codex/foreword/core/ComplianceEvidence.codex` maps 60 regulatory
requirements across CRA Annex I (8), ETSI EN 303 645 (40), NISTIR
8259A (5), and IEC 62443 (7) to the Codex features that satisfy each
one. The
`generate-evidence-report` function produces a text compliance summary
as a build artifact — not a separate document that can drift, but a
function that reads the actual compiler state and produces the actual
evidence.

---

## 7. GPU Compute for IoT Edge

PTX is the right target for the dev box (RTX 4060 Ti) and NVIDIA data
center GPUs. SPIR-V is the right target for the IoT edge:

- **ARM Mali GPUs** (IoT gateways, Raspberry Pi) — Vulkan compute
- **Qualcomm Adreno** (mobile/edge SoCs) — Vulkan compute
- **Intel integrated GPUs** (industrial PCs) — Vulkan/OpenCL
- **Imagination PowerVR** (automotive, embedded) — Vulkan compute

The dual-target approach means a single Codex source file produces
firmware that runs GPU compute on whatever hardware is available —
NVIDIA in the data center, ARM Mali on the gateway, CPU on the sensor
node. Same signed CDX, same trust chain, same effect-typed safety
guarantees.

---

## Cross-References

- `docs/Reference/CRA-Compliance-Matrix.md` — CRA requirement mapping
- `docs/Reference/ETSI-303645-Mapping.md` — ETSI provision mapping
- `docs/Reference/IEC62443-Evidence.md` — IEC 62443 evidence mapping
- `docs/PM/IoT/` — compliance summaries, protocol references, hardware specs
- `docs/PM/Stories/Vision/CodexIoTPlan.md` — IoT strategic prospectus
- `codex/foreword/punctual/` — the punctual library (8 chapters)
- `codex/foreword/core/ComplianceEvidence.codex` — automated evidence report
