# NISTIR 8259 Series — IoT Device Cybersecurity

**Source**: NIST Applied Cybersecurity Division
**Status**: Active (Rev 1 April 2026)

## Documents in the Series

| Document | Title | Date | Status |
|---|---|---|---|
| NISTIR 8259 R1 | Foundational Activities for IoT Product Manufacturers | April 2026 | Final (supersedes original) |
| NISTIR 8259A | Core Device Cybersecurity Capability Baseline | May 2020 | Final |
| NISTIR 8259B | IoT Non-Technical Supporting Capability Core Baseline | August 2021 | Final |
| NISTIR 8259C | Creating a Profile Using IoT Core Baseline and Non-Technical Baseline | TBD | Draft |

## Core Device Capabilities (8259A)

Six capability categories defining what an IoT device should be able
to do from a cybersecurity perspective:

1. **Device Identification**: Unique logical and physical identifiers
2. **Device Configuration**: Authorized configuration changes only
3. **Data Protection**: Protect stored and transmitted data
4. **Logical Access to Interfaces**: Restrict access to local/network interfaces
5. **Software Update**: Secure, authorized update mechanisms
6. **Cybersecurity State Awareness**: Generate and report security events

## Lifecycle Coverage (8259 R1)

Covers manufacturer activities from conception through end-of-support:
conceive, design, develop, test, sell, and support.

## Codex Compliance Mapping

| NIST Capability | Codex Feature |
|---|---|
| Device Identification | Ed25519 public key IS the device identity |
| Device Configuration | Capability leases — permissions granted, scoped, and revocable |
| Data Protection | AES-GCM/ChaCha20 encryption, TLS, effect-typed I/O channels |
| Logical Access | Effect types — unauthorized interface access is a type error |
| Software Update | CDX signed binaries + verifier + trust lattice |
| State Awareness | Event bus, journal, health checks, metrics (codex/os/observe/) |

## PDF Downloads

- 8259A: https://nvlpubs.nist.gov/nistpubs/ir/2020/NIST.IR.8259A.pdf
- 8259B: https://doi.org/10.6028/NIST.IR.8259B
- 8259 R1: https://doi.org/10.6028/NIST.IR.8259r1
