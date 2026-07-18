# CRA Annex I Compliance Matrix

Maps each EU CRA Annex I essential cybersecurity requirement to the
Codex mechanism that satisfies it, the evidence the compiler produces,
and the manufacturer obligations beyond the toolchain.

## Evidence Classes

- **BY-CONSTRUCTION**: The language makes the violation inexpressible.
  No runtime check needed — the type system prevents it.
- **MECHANISM**: A runtime mechanism enforces it (signed binary,
  capability check, encryption).
- **DEPLOYMENT**: Satisfied by provisioning or operational configuration.
- **ORGANIZATIONAL**: Process obligation on the manufacturer.

---

## Annex I, Part I: Security Requirements for Products with Digital Elements

### 1(a) — No Known Exploitable Vulnerabilities at Release

| Field | Value |
|-------|-------|
| Class | BY-CONSTRUCTION |
| Mechanism | Linear types eliminate use-after-free and double-free (CDX2061/CDX2063). Bounded integers prevent overflow (CDX4010, CDX2050). Effect types (CDX4001) prevent undeclared I/O — a compromised library cannot exfiltrate data. No OS, no libc, no POSIX surface: bare-metal execution removes the entire class of OS-level CVEs. |
| Evidence | Compile log: zero CDX2061/CDX2063 diagnostics = no use-after-free possible. CDX4010 info messages = integer bounds statically proven, runtime checks elided. CDX4001 absence = every effect declared and granted. Binary fixed-point (stage 1 = stage 2) proves deterministic compilation. |
| Manufacturer | Run the compiler. If it compiles without CDX2xxx/CDX4xxx errors, the structural guarantees hold. Perform threat modelling for logic bugs that lie outside the type system's scope. |
| Cross-ref | ETSI EN 303 645 §5.1-1, NISTIR 8259A Capability 3, IEC 62443-4-1 SD-4 |

### 1(b) — Secure Default Configuration

| Field | Value |
|-------|-------|
| Class | BY-CONSTRUCTION + DEPLOYMENT |
| Mechanism | CDX capability tables default to empty — a binary ships with zero granted capabilities until the deployer explicitly provisions them. Effect types enforce the invariant: code without a declared effect row cannot perform I/O, network, or storage operations regardless of configuration. |
| Evidence | CDX verifier 5-phase report: capability table dump shows zero default grants. Compile log confirms no `cdx-capability-not-granted` suppressions. |
| Manufacturer | Do not pre-populate capability leases. Ship the CDX binary with an empty capability table and let the provisioning step grant only what the deployment requires. Document the minimum capability set. |
| Cross-ref | ETSI EN 303 645 §5.1-2, NISTIR 8259A Capability 1, IEC 62443-4-2 CR 7.7 |

### 1(c) — Protection of Stored, Transmitted, and Processed Data

| Field | Value |
|-------|-------|
| Class | MECHANISM |
| Mechanism | AES-GCM-256 and ChaCha20-Poly1305 for data at rest. TLS 1.3 via effect-typed channels (X25519 key exchange, Ed25519 authentication) for data in transit. Linear types prevent key material aliasing — a key cannot be copied, only moved, so it cannot leak through a second reference. Content-addressed FactStore ensures integrity of stored artifacts via SHA-256 hashes. |
| Evidence | Compile log: CDX2061 on any crypto key type = build failure (key aliased). TLS channel construction requires `[Network, Crypto]` effect row — absence is a compile error. FactStore CRC-frame verification on every read. |
| Manufacturer | Provision TLS certificates and AES keys through the capability system. Enable encryption-at-rest for any persistent storage. Rotate keys per the trust lattice policy (documented in the deployment manifest). |
| Cross-ref | ETSI EN 303 645 §5.8-1/§5.8-2, NISTIR 8259A Capability 5, IEC 62443-4-2 CR 3.1/CR 4.1 |

### 1(d) — Protection of Availability, Including Resilience Against DoS

| Field | Value |
|-------|-------|
| Class | BY-CONSTRUCTION + MECHANISM |
| Mechanism | Compile-time WCET proofs via the `punctual` keyword (CDX6010): the compiler statically bounds worst-case execution time per function, preventing unbounded computation. Bounded integers and fuel-capped recursion eliminate infinite loops. The bare-metal scheduler uses HPET-based preemption — no task can monopolise the CPU beyond its declared time budget. Dead-code elimination removes unreachable paths that could hide resource exhaustion. |
| Evidence | CDX6010 info messages list per-function WCET bounds in cycles. Compile failure on any `punctual` function whose bound cannot be proven. Scheduler log: preemption count and max-latency per task. |
| Manufacturer | Mark all externally-reachable entry points `punctual` with appropriate cycle budgets. Size HPET preemption intervals to match the deployment's real-time requirements. Load-test with sustained input at maximum rate. |
| Cross-ref | ETSI EN 303 645 §5.7-1, NISTIR 8259A Capability 6, IEC 62443-4-2 CR 7.1/SAR 7.2 |

### 1(e) — Minimisation of Attack Surface

| Field | Value |
|-------|-------|
| Class | BY-CONSTRUCTION |
| Mechanism | No OS kernel, no libc, no dynamic linker, no shell — the entire POSIX/Win32 attack surface is absent. Effect types restrict each module to its declared capabilities; unreachable code is eliminated at compile time. The capability table enumerates the exact surface: every granted cap-id, direction, scope, and max-duration is visible in the CDX header. The 5-phase CDX verifier rejects binaries that request capabilities beyond their declared effect row. |
| Evidence | CDX header capability table: enumerable, auditable, diffable between releases. Dead-code elimination metrics in compile log (bytes removed). Verifier pass/fail report per phase. |
| Manufacturer | Audit the capability table diff between releases. Any new cap-id entry must be justified in the release notes. Run the verifier against the deployment policy before shipping. |
| Cross-ref | ETSI EN 303 645 §5.6-1, NISTIR 8259A Capability 4, IEC 62443-4-1 SR-2/SD-3 |

### 1(f) — Logging and Monitoring of Security-Relevant Events

| Field | Value |
|-------|-------|
| Class | MECHANISM + DEPLOYMENT |
| Mechanism | Effect-typed `[Audit]` channel: any function that writes audit records must declare the `Audit` effect, making log generation visible in the type signature. The FactStore provides an append-only, CRC-framed, power-loss-safe event log — entries cannot be silently modified or truncated. Capability lease grants and revocations are recorded as facts in the trust lattice with Ed25519 signatures. |
| Evidence | Compile log: presence of `[Audit]` effect on security-critical functions. FactStore integrity: CRC verification on every read, hash-chain validation on sync. Lease history queryable via the repository protocol. |
| Manufacturer | Route the `[Audit]` channel to a tamper-evident sink (append-only storage or remote SIEM). Define retention policy. Monitor for gaps in the hash chain, which indicate truncation or tampering. |
| Cross-ref | ETSI EN 303 645 §5.11-1, NISTIR 8259A Capability 2, IEC 62443-4-2 CR 6.1/CR 6.2 |

---

## Annex I, Part II: Vulnerability Handling Requirements

### 2(a) — Unique Identification of Components and Vulnerabilities

| Field | Value |
|-------|-------|
| Class | BY-CONSTRUCTION + MECHANISM |
| Mechanism | Every CDX binary is content-addressed (SHA-256 hash) and Ed25519 signed with a per-device keypair. The repository protocol records every artifact as a fact — immutable, hash-identified, and traceable to the source proposal and verdict that produced it. The trust lattice binds each binary to its author identity and build provenance. Component versions are encoded in the CDX header metadata, not in mutable filenames. |
| Evidence | CDX header: SHA-256 hash, Ed25519 signature, author identity, build timestamp, source fact-id. FactStore query: full provenance chain from binary back to source proposal. `p4 filelog` equivalent via repository protocol for every artifact. |
| Manufacturer | Maintain a software bill of materials (SBOM) derived from the FactStore provenance chain. Register the product in the manufacturer's vulnerability tracking system. Assign CVE-compatible identifiers to any discovered vulnerability and cross-reference to the originating fact-id. |
| Cross-ref | ETSI EN 303 645 §5.2-1, NISTIR 8259A Capability 7, IEC 62443-4-1 DM-1/DM-2 |

### 2(b) — Effective and Regular Vulnerability Handling and Remediation

| Field | Value |
|-------|-------|
| Class | MECHANISM + ORGANIZATIONAL |
| Mechanism | OTA update with dual-gate verification: stage 1 verifies the Ed25519 signature and hash of the update payload; stage 2 verifies the installed image post-flash. Anti-rollback counter prevents downgrade attacks. Capability lease revocation propagates through the trust lattice — a compromised component's leases can be revoked without reflashing the entire device. The repository protocol's proposal/verdict workflow provides an auditable patch-acceptance pipeline. |
| Evidence | OTA log: dual-gate pass/fail, anti-rollback counter value, old-hash to new-hash transition. Trust lattice: lease revocation record with timestamp and revoking authority. Proposal/verdict chain: full review history for every patch. |
| Manufacturer | Establish a coordinated vulnerability disclosure policy with 24-hour initial notification to ENISA/CSIRT per CRA Article 11. Maintain a security contact (email, web form). Provide free security updates for the support period (minimum 5 years per CRA Article 10). Document the OTA update procedure for end users. Publish advisories that reference the originating fact-id and the remediating fact-id. |
| Cross-ref | ETSI EN 303 645 §5.2-2/§5.3-1, NISTIR 8259A Capability 8, IEC 62443-4-1 DM-3/DM-4 |

---

## Summary Matrix

| Req. | Short Title | Primary Class | Key Codex Feature |
|------|-------------|---------------|-------------------|
| 1(a) | No known vulns | BY-CONSTRUCTION | Linear types, bounded ints, effect types |
| 1(b) | Secure defaults | BY-CONSTRUCTION | Empty capability table, effect rows |
| 1(c) | Data protection | MECHANISM | AES-GCM-256, TLS 1.3, linear key types |
| 1(d) | Availability / DoS | BY-CONSTRUCTION | `punctual` WCET proofs, fuel-capped recursion |
| 1(e) | Minimal surface | BY-CONSTRUCTION | No OS/libc, dead-code elim, capability table |
| 1(f) | Logging | MECHANISM | `[Audit]` effect, append-only FactStore |
| 2(a) | Component ID | MECHANISM | Content-addressed hashes, Ed25519 signing |
| 2(b) | Vuln handling | ORGANIZATIONAL | OTA dual-gate, lease revocation, trust lattice |
