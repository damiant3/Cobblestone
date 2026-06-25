# IRISA Research Harvest

**Date:** 2026-06-23
**Source:** https://www.irisa.fr/en/departments/networks-telecommunication-services
**Surveyed:** All 7 IRISA departments (35 teams)

IRISA (Institut de Recherche en Informatique et Systemes Aleatoires) is a
joint research lab in Rennes, France, spanning Inria, CNRS, Universite de
Rennes, and several engineering schools. This document captures research
findings relevant to Codex from a full survey of their teams.

## Directly Harvestable

### 1. Squirrel Prover (SPICY team, D1)

A protocol verification proof assistant operating in the **computational
model**, not just symbolic. Uses a "higher-order indistinguishability
logic" that bridges symbolic reasoning with computational soundness --
verified properties hold under computational hardness assumptions, not
just symbolic equivalence.

Key evolution: meta-logic proof assistant (2021) -> mutable protocol
state (2022) -> self-contained higher-order logic (2023) -> probabilistic
reasoning for concrete security (2024).

**Codex relevance (trust lattice, Gap 6):** Instead of proving only
symbolic properties of trust delegation chains (sym/trans/cong), we
could model computational indistinguishability -- proving that an
attacker observing the protocol transcript cannot distinguish between
two trust states. Squirrel's handling of mutable protocol state maps
directly to our append-only mutation log. Their probabilistic reasoning
for concrete security could inform trust degradation models.

- Site: https://squirrel-prover.github.io/
- Team: SPICY (Security & Privacy), D1
- Lead: Stephanie Delaune (CNRS)

### 2. GnuZero (SPICY team, D1)

A **compiler-based static detection tool for zeroization weaknesses**.
Won Distinguished Best Paper at DSN'25. Detects when compilers optimize
away security-critical memory clearing (e.g., memset of keys/passwords
getting dead-store-eliminated).

**Codex relevance (security, compiler):** Our __alloc now zeroes via
rep stosb (calloc semantics). Since we control the compiler, we can make
zeroization a semantic guarantee -- immune to dead-store elimination
because we own the emitter. Could add a zero-on-free or secure-erase
annotation that the emitter enforces at the language level.

### 3. EPICURE (D4) -- Verified Compilation + Side-Channel Analysis

Semantics-based methods for secure software development. Three areas
directly relevant:

- **Information flow control through verified compilation:** Verify that
  high-level security properties (constant-time execution, no information
  leakage) survive compilation to machine code. Built on CompCert-style
  verified compilation.
- **RIOT OS verification:** Static analysis of an IoT OS kernel --
  relevant to our bare-metal kernel verification.
- **Tezos blockchain verification:** Formal verification of OCaml smart
  contract execution -- their approach of verifying a VM implementation
  could inform our CDX verifier.

**Codex relevance (punctual, compiler):** Their approach of proving that
compilation preserves security properties maps to our punctual functions.
A punctual function has bounded execution, no heap, no I/O -- but we
only check this at the IR level. EPICURE's approach would verify that
x86-64 codegen doesn't introduce timing side-channels (e.g.,
data-dependent branches in emitted code). Also: RIOT OS parallels our
bare-metal kernel -- proving properties about code running with no OS
safety net.

- Team: EPICURE (evolved from CELTIQUE), D4
- Lead: Thomas Jensen (Inria)
- Site: https://team.inria.fr/epicure

### 4. SUSHI (D3) -- Hardware/Software Interface Security

Security at the software/hardware boundary:

- **Microarchitectural attacks:** Spectre/Meltdown-class vulnerabilities
- **Compiler support for security:** Using compilation to mitigate
  hardware vulnerabilities
- **Binary analysis and instrumentation:** Analyzing compiled code
- **Formal models for low-level security mechanisms**

**Codex relevance (Gap 5, VMX hypervisor):** We run bare-metal with no
OS mitigations (no ASLR, no KPTI). SUSHI's work on architectural
support for security could inform DevHypervisor design. Their formal
models for low-level security could help prove that our page table setup
and VMXON isolation prevent guest-to-host escapes.

- Team: SUSHI (SecUrity at Software/Hardware Interface), D3
- Lead: Guillaume Hiet (CentraleSupelec)
- Organizes SILM Workshop (co-located with IEEE Euro S&P)

### 5. CAPSULE (D1) -- Applied Cryptography

- **Lightweight block ciphers:** Relevant to IoT/embedded targets
  (STM32F4, ESP32-C6) where Ed25519 may be too heavy
- **Post-quantum lattice-based cryptography:** Our Ed25519 signing will
  eventually need a post-quantum upgrade path
- **Side-channel attack analysis of crypto implementations**

**Codex relevance (signing, future):** Our Ed25519 is "hardcoded and
always works" but we haven't verified it's constant-time at machine code
level. CAPSULE's implementation security work could inform a verification
pass on our crypto builtins.

- Team: CAPSULE (Applied Cryptography), D1
- Lead: Pierre-Alain Fouque (Universite de Rennes)

### 6. PACAP (D3) -- Compilation and Architecture

- **WCET analysis (Worst-Case Execution Time):** Formal bounds on
  execution time for real-time systems
- **Branch prediction and value prediction**
- **Non-volatile RAM compilation strategies**

**Codex relevance (punctual, compiler):** WCET analysis directly maps to
punctual functions. CDX6010 instruction count is a proxy for execution
time, but PACAP's techniques could give actual cycle-accurate bounds on
specific hardware. This would make punctual not just "bounded
instructions" but "bounded wall-clock time on target X."

- Team: PACAP (Processor Architecture and Compilation), D3
- Lead: Erven Rohou (Inria)

## Thematic Ideas

### 7. SOTERN (D2) -- Self-Protecting Networks

"Intent-based security" -- expressing security policies as high-level
intents that the system enforces automatically. Self-monitoring,
self-healing distributed security.

**Codex relevance (trust lattice, effects):** Instead of writing
firewall rules, write intent: only signed code runs and the system
derives enforcement. Could inform our effect system: [Secure] Nothing as
an effect the verifier proves is maintained.

- Team: SOTERN (Self-prOtecting The futurE inteRNet), D2
- Lead: Guillaume Doyen (IMT Atlantique)

### 8. INZU (D2) -- Opportunistic Networking

Infrastructure-free mobile networks, censorship-resistant communication.
Protocols for intermittent connectivity and delay tolerance.

**Codex relevance (Gap 6, federation):** When two Codex nodes connect
via TrustTransport, they may have been disconnected for days. INZU's
delay-tolerant networking protocols could inform how we reconcile
divergent fact stores.

- Team: INZU (Opportunistic Computing and Networking), D2
- Lead: Yves Maheo (Universite Bretagne Sud)

### 9. E4SE (D2) -- Edge Computing Without Cloud

Explicitly rejects centralized cloud in favor of distributing
intelligence to edge nodes. Data sovereignty and privacy through local
processing.

**Codex relevance (vision alignment):** This is the Codex vision: "Hand
someone a USB stick, they boot it." E4SE's architectural patterns for
decentralized edge systems could inform how multiple Codex nodes
federate without central authority.

- Team: E4SE (Enabling Affordable Smarter Environment), D2
- Lead: Jean-Marie Bonnin (IMT Atlantique)

## Lower Priority

- **TARAN (D3):** Domain-specific accelerators, approximate computing --
  future GPU/FPGA backends
- **WIDE (D1):** Large-scale distributed systems, scalability vs
  coordination -- general background for federation
- **LogicA (D4):** Attack trees, multi-agent strategic reasoning --
  adversarial modeling against trust lattice
- **ERMINE (D2):** Network economics -- not directly relevant
- **ADOPNET (D2):** Content delivery, radio access -- not relevant
- **OCIF (D2):** Not surveyed in detail

## Actionable Summary

| Priority | Source | Idea | Codex Gap |
|----------|--------|------|-----------|
| High | Squirrel (SPICY) | Computational-model protocol verification for trust lattice | Gap 6 |
| High | EPICURE | Verify punctual properties survive codegen | Compiler |
| High | GnuZero (SPICY) | Guarantee zeroization can't be elided -- language-level semantic | Security |
| Medium | PACAP | WCET analysis for cycle-accurate punctual bounds | Compiler |
| Medium | SUSHI | Formal models for VMX isolation proofs | Gap 5 |
| Medium | INZU | Delay-tolerant reconciliation for federated fact stores | Gap 6 |
| Low | CAPSULE | Post-quantum crypto upgrade path for signing | Future |
| Low | E4SE | Decentralized edge architecture patterns | Gap 6 |
