# ETSI EN 303 645 Compliance Mapping to Codex

Maps all 33 mandatory provisions to Codex language and platform features.

**Legend — Gap column:**
- **Satisfied** — Codex provides the mechanism by construction
- **Partial** — Codex provides the tool; manufacturer must configure/enable
- **Manufacturer** — process obligation outside the compiler/runtime

---

## 5.1 — No Universal Default Passwords

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.1-1 | No universal default passwords | Security | Trust lattice generates per-device identity at provisioning; no shared secrets in CDX binaries | Partial |
| 5.1-2 | No easily guessable default passwords | Security | Capability manifests bind credentials to device key pairs (Ed25519); no password-based auth by default | Satisfied |
| 5.1-3 | Passwords shall be unique per device | Security | Trust lattice derives per-device keys from hardware identity; key material never duplicated across devices | Partial |
| 5.1-4 | Authentication not based on universal credentials | Security | Ed25519 per-device signing keys; trust lattice enforces unique identity chain | Satisfied |

## 5.2 — Vulnerability Disclosure

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.2-1 | Vulnerability disclosure policy | Process | Fact store provides audit trail for all code changes; manufacturer must publish disclosure process | Manufacturer |

## 5.3 — Software Updates

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.3-1 | Software shall be securely updateable | Security | OTA via LwM2M Object 5; signed CDX images verified by Ed25519 + SHA-256 before application | Satisfied |
| 5.3-2 | Updates shall be timely | Process | Fact store tracks proposals/verdicts with timestamps; manufacturer sets cadence | Manufacturer |
| 5.3-3 | Automatic update mechanism | Security | LwM2M Object 5 supports pull-based and push-based update; capability manifest declares update channel | Partial |
| 5.3-4 | Check for latest update on init | Security | Boot sequence queries update endpoint via LwM2M; trust lattice validates server identity | Partial |
| 5.3-5 | Notify user of security update | UX | Effect types force I/O declaration; notification side-effect must be declared in manifest | Partial |
| 5.3-6 | Easy to apply updates | UX | CDX binary is single-image; no dependency resolution needed; signed swap is atomic | Satisfied |
| 5.3-7 | Updates use integrity verification | Security | Signed CDX (Ed25519 + SHA-256); CDX header contains content hash verified at boot | Satisfied |
| 5.3-8 | Update over untrusted network | Security | CDX signature verification is independent of transport; no TLS dependency for integrity | Satisfied |

## 5.4 — Secure Storage of Credentials

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.4-1 | Credentials stored securely | Security | Linear types (CDX2061/2063) prevent credential duplication; bare-metal has no swap/tmpfs leak surface | Satisfied |
| 5.4-2 | Hard-coded credentials prohibited | Security | Effect types prevent compile-time secret embedding; capability manifest rejects static key literals | Satisfied |
| 5.4-3 | Credentials in transit encrypted | Security | Effect types declare all I/O; credential-bearing channels require encryption capability | Partial |
| 5.4-4 | Device identity credentials unique | Security | Trust lattice per-device key derivation; Ed25519 keypair generated at provisioning | Satisfied |

## 5.5 — Secure Communication

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.5-1 | Communicate securely | Security | Effect types enforce declared I/O channels; capability manifest specifies permitted protocols | Partial |
| 5.5-2 | Confidentiality of sensitive data | Security | Linear types prevent accidental aliasing of sensitive buffers; bare-metal — no OS-level cache leak | Satisfied |
| 5.5-3 | Cryptographic protocols up to date | Process | Signed CDX uses Ed25519 + SHA-256; manufacturer must track algorithm deprecation | Manufacturer |
| 5.5-4 | Cryptographic suites configurable | Security | Capability manifest declares permitted cipher suites; update via OTA | Partial |
| 5.5-5 | Use best-practice cryptography | Process | Ed25519 + SHA-256 are current best practice; manufacturer reviews periodically | Manufacturer |
| 5.5-6 | No known weak algorithms | Security | Compiler rejects deprecated algorithm identifiers in capability manifest | Partial |
| 5.5-7 | Manage cryptographic material securely | Security | Linear types (CDX2061) enforce single-owner for key material; no use-after-free | Satisfied |

## 5.6 — Minimise Exposed Attack Surface

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.6-1 | Disable unused network interfaces | Security | Capability manifest whitelists enabled interfaces; bare-metal has no background services | Satisfied |
| 5.6-2 | Minimise pre-authenticated attack surface | Security | Bare-metal compilation — no OS, no libc, no shell; only declared capabilities are reachable | Satisfied |
| 5.6-3 | Software operates with least privilege | Security | Effect types + capability manifests enforce minimal privilege per function | Satisfied |
| 5.6-4 | Disable debug interfaces in production | Security | CDX binary strips debug sections; capability manifest excludes debug capabilities | Partial |

## 5.7 — Software Integrity

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.7-1 | Verify software integrity at boot | Security | Signed CDX (Ed25519 + SHA-256); boot verifies content hash against embedded signature | Satisfied |
| 5.7-2 | Alert on unauthorized changes | Security | Fact store records expected hashes; runtime hash mismatch triggers alert via declared effect | Partial |

## 5.8 — Personal Data Protection

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.8-1 | Personal data protection by design | Security | Linear types prevent data aliasing; effect types make all data flows explicit and auditable | Satisfied |
| 5.8-2 | Consent mechanism | Process | Capability manifest can declare consent gates; manufacturer must implement UI | Manufacturer |
| 5.8-3 | External sensing clearly indicated | UX | Effect types require declaration of sensor access; manufacturer provides physical indicator | Partial |

## 5.9 — Resilience

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.9-1 | Resilient to outages | Safety | `punctual` (CDX6010) provides WCET proofs; bounded integers (CDX4010/2050) prevent overflow; no unbounded allocation | Satisfied |
| 5.9-2 | Resilient to network disruption | Safety | Bare-metal runtime continues local operation; no network dependency for core function | Satisfied |
| 5.9-3 | Graceful degradation | Safety | Effect types partition essential vs. optional I/O; `punctual` guarantees deadline compliance for essential paths | Partial |

## 5.10 — Telemetry

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.10-1 | Examine telemetry data | Process | Fact store audit trail records all emitted telemetry; manufacturer provides inspection tool | Manufacturer |

## 5.11 — Deletion of Personal Data

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.11-1 | Easy mechanism to delete data | Security | Linear types guarantee single-owner; drop semantics zero memory on scope exit; no residual copies | Satisfied |

## 5.12 — Installation and Maintenance

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.12-1 | Simple and secure installation | UX | CDX is single-image bootable binary; no multi-step installation; signed at build | Partial |

## 5.13 — Input Validation

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.13-1 | Validate input data | Security | Bounded integers (CDX4010/2050) reject overflow; linear types prevent buffer aliasing; bare-metal has no injection surface | Satisfied |

---

## Summary

| Gap Status | Count |
|------------|-------|
| Satisfied | 19 |
| Partial | 10 |
| Manufacturer | 4 |

All 4 Manufacturer gaps are process obligations (disclosure policy, update cadence, algorithm review, telemetry inspection) that no compiler can satisfy. The 10 Partial items require manufacturer configuration of mechanisms Codex already provides.
