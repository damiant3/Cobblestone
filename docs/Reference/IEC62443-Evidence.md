# IEC 62443 Compliance Evidence — Codex

Maps Foundational Requirements FR1-FR7 to Codex mechanisms with concrete evidence.

---

## FR1 — Identification and Authentication Control

**Requirement:** All users, processes, and devices must be identified and authenticated before access.

**Codex mechanism:** Trust lattice assigns Ed25519 keypairs per device at provisioning. Capability manifests declare required identity before any I/O capability is granted. Effect types make unauthenticated code paths a compile-time error.

**Evidence:**
- CDX binary header contains device public key (Ed25519, 32 bytes)
- Trust lattice key derivation: see signed CDX verification in boot sequence
- Capability manifest syntax enforces `identity` declaration (CDX2061)

---

## FR2 — Use Control / Authorization

**Requirement:** Enforce assigned privileges for authenticated entities; least privilege.

**Codex mechanism:** Capability manifests whitelist permitted operations per function. Effect types partition I/O capabilities — a function cannot perform undeclared effects. Linear types (CDX2061/2063) prevent capability duplication or escalation.

**Evidence:**
- Effect type annotations are compiler-enforced; undeclared I/O is a compile error
- Linear types prevent aliasing of capability tokens — single-owner semantics
- CDX2063 (linear resource) ensures capability handles cannot be copied
- Bare-metal binary contains no shell, no syscall interface — only declared capabilities exist

---

## FR3 — System Integrity

**Requirement:** Ensure integrity of components and protect against unauthorized modification.

**Codex mechanism:** Signed CDX binaries (Ed25519 + SHA-256). Boot verifies content hash against embedded signature before execution. Fact store maintains audit trail of all changes (proposals, verdicts).

**Evidence:**
- CDX header: SHA-256 content hash + Ed25519 signature (64 bytes)
- Self-compile fixed point: stage 1 CDX = stage 2 CDX (byte-identical)
- Bounded integers (CDX4010/2050) prevent arithmetic corruption
- No use-after-free by construction (linear types CDX2061)

---

## FR4 — Data Confidentiality

**Requirement:** Protect confidentiality of data at rest and in transit.

**Codex mechanism:** Linear types enforce single-owner semantics — sensitive data cannot be aliased or leaked through dangling references. Drop semantics zero memory on scope exit. Bare-metal runtime has no swap file, no OS-level cache, no tmpfs.

**Evidence:**
- CDX2061 (linear types): compiler rejects programs that alias secret-bearing buffers
- Bare-metal compilation: no OS memory management surfaces (no page file, no core dumps)
- Effect types require explicit declaration of any channel carrying sensitive data

---

## FR5 — Restricted Data Flow

**Requirement:** Segment and control data flow between zones.

**Codex mechanism:** Effect types create static data-flow boundaries. Each function declares its I/O surface; the compiler enforces that data does not cross undeclared zone boundaries. Capability manifests define permitted communication partners.

**Evidence:**
- Effect type system: compiler traces all data paths; undeclared flows are compile errors
- Capability manifest: per-function whitelist of permitted I/O endpoints
- No ambient authority — bare-metal binary has no implicit network stack or file system

---

## FR6 — Timely Response to Events

**Requirement:** Respond to security-relevant events within defined time bounds; audit logging.

**Codex mechanism:** `punctual` (CDX6010) provides compile-time WCET (worst-case execution time) proofs. The compiler statically guarantees that security-critical event handlers complete within their declared deadline. Fact store provides audit logging.

**Evidence:**
- `punctual` annotation (CDX6010): compiler computes instruction-level WCET bound
- `codex/test/examples/missile-warning.codex`: demonstrates `punctual` on a safety-critical response path — the compiler proves the handler meets its deadline or rejects the program
- Bounded integers (CDX4010) prevent unbounded loops in event handlers
- No dynamic allocation in `punctual` functions — heap-save/heap-restore scoping guarantees no GC pause
- Fact store records timestamped audit entries for all security-relevant events

**How `punctual` satisfies FR6:** FR6 requires "timely response" — a vague term in most implementations, satisfied by best-effort testing. Codex makes it a compile-time proof. A function annotated `punctual` with a deadline has its worst-case instruction count computed statically. If the compiler cannot prove the deadline is met, compilation fails. This transforms FR6 from a runtime hope into a build-time guarantee. The missile-warning example shows a sensor-to-alert pipeline where each stage carries a `punctual` bound, and the compiler verifies the end-to-end latency.

---

## FR7 — Resource Availability

**Requirement:** Ensure availability of the system under degraded conditions; prevent denial of service.

**Codex mechanism:** `punctual` (CDX6010) guarantees bounded execution time, preventing CPU exhaustion. Bounded integers (CDX4010/2050) prevent memory exhaustion through overflow. Bare-metal runtime has no OS scheduler to starve. Linear types prevent resource leaks.

**Evidence:**
- `punctual` (CDX6010): no function can consume unbounded CPU — WCET is proved at compile time
- Bounded integers (CDX4010/2050): all arithmetic is range-checked; no silent overflow leading to infinite loops or allocation spirals
- Linear types (CDX2061/2063): every resource has exactly one owner; no leaks, no double-free
- Bare-metal: no competing processes, no scheduler preemption, no OS-level DoS surface
- `codex/test/examples/missile-warning.codex`: demonstrates that the critical path is immune to CPU starvation — the compiler has proved its WCET

**How `punctual` satisfies FR7:** FR7's availability requirement is fundamentally about bounded resource consumption. `punctual` converts this from an operational concern (load testing, capacity planning) into a mathematical property of the binary. A `punctual`-annotated system has a provable upper bound on CPU time per invocation. Combined with bounded integers (no allocation spiral) and linear types (no resource leak), the system cannot be driven into resource exhaustion by any input. The missile-warning example is the canonical demonstration: the compiler proves that sensor-read through alert-dispatch completes within the stated bound regardless of input, making availability a compile-time invariant rather than a runtime aspiration.

---

## Summary

| FR | Status | Key Mechanism |
|----|--------|---------------|
| FR1 | Satisfied | Trust lattice + Ed25519 identity |
| FR2 | Satisfied | Capability manifests + effect types + linear types |
| FR3 | Satisfied | Signed CDX + fixed-point self-compile + bounded integers |
| FR4 | Satisfied | Linear types + bare-metal (no OS leak surface) |
| FR5 | Satisfied | Effect types + capability manifests (static data-flow) |
| FR6 | Satisfied | `punctual` WCET proofs + fact store audit trail |
| FR7 | Satisfied | `punctual` + bounded integers + linear types (no exhaustion) |
