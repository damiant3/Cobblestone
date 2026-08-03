# CRA Annex I Compliance Matrix

Maps each EU CRA Annex I essential cybersecurity requirement to the
Codex mechanism that satisfies it, the evidence the compiler produces,
and the manufacturer obligations beyond the toolchain.

**A row here is a claim, and the check is a test that fails when the claim
stops being true.** Nothing reads this document at session start, no gate
checks it, and no test cites it, which is precisely the condition under
which a claim rots. Every row was fired at the compiler or read at its
source on 2026-07-27 against seed `A5758E05`; where a claim did not
survive, it was **withdrawn and the measurement recorded**, never reworded
to fit. The precedent is ETSI provision 5.5, which read "Satisfied" while
nothing checked that a certificate belonged to the host the client had
dialled; it was withdrawn and re-earned the same day by fixing the code.
See `docs/KingsAndCourts.md`.

## Evidence Classes

- **BY-CONSTRUCTION**: The language makes the violation inexpressible.
  No runtime check needed -- the type system prevents it.
- **MECHANISM**: A runtime mechanism enforces it (signed binary,
  capability check, encryption).
- **DEPLOYMENT**: Satisfied by provisioning or operational configuration.
- **ORGANIZATIONAL**: Process obligation on the manufacturer.

---

## Annex I, Part I: Security Requirements for Products with Digital Elements

### 1(a) -- No Known Exploitable Vulnerabilities at Release

| Field | Value |
|-------|-------|
| Class | BY-CONSTRUCTION |
| Mechanism | Linear types eliminate use-after-free and double-free (CDX2061/CDX2063). Effect types prevent undeclared I/O (CDX2031/CDX2033) -- a compromised library cannot exfiltrate data. No OS, no libc, no POSIX surface: bare-metal execution removes the entire class of OS-level CVEs. **Overflow is prevented only where a bound is declared.** A `Integer between L and H` field rejects an out-of-range literal (CDX2050) and refuses a value whose range cannot be proven (CDX2051). Plain `Integer` arithmetic is unbounded 64-bit and **wraps silently**. |
| Evidence | Compile log: zero CDX2061/CDX2063 diagnostics = no use-after-free possible. CDX4010 info messages = integer bounds statically proven, runtime checks elided. Binary fixed-point (stage 1 = stage 2) proves deterministic compilation. |
| Withdrawn | **"Bounded integers prevent overflow" as a blanket statement.** Measured 2026-07-27: a chapter defining `i64-max = 9223372036854775807` and printing `i64-max + 1` compiles with zero diagnostics and prints `-9223372036854775808`. The guarantee is real but it is opt-in per declaration, and CDX4010 is an *info* recording that a check was **elided**, not a check performed. **"CDX4001 absence = every effect declared and granted."** CDX4001 (`check-opening-capabilities`, `Types/TypeChecker.codex`) inspects the effect row of the definition named `opening` alone, and asks only whether each name is one of the 24 in `Foreword chapter Capability`'s vocabulary. It examines no other function and it says nothing about whether any capability was granted. Its absence is not the evidence this row claimed. |
| Manufacturer | Run the compiler. If it compiles without CDX2xxx/CDX4xxx errors, the structural guarantees hold. Perform threat modelling for logic bugs that lie outside the type system's scope. |
| Cross-ref | ETSI EN 303 645 §5.1-1, NISTIR 8259A Capability 3, IEC 62443-4-1 SD-4 |

### 1(b) -- Secure Default Configuration

| Field | Value |
|-------|-------|
| Class | BY-CONSTRUCTION + DEPLOYMENT |
| Mechanism | A CDX capability table is **derived from the effect row the program declares**, not defaulted on: a binary that declares no effects carries an empty table, and one that declares `[Network.Write]` carries exactly that entry with its direction and scope. The table is a statement of what the binary *requests*; the grant is a separate decision made by the boot path or the verified loader against a policy. Effect types enforce the invariant underneath: code without a declared effect row cannot perform I/O, network, or storage operations regardless of configuration (CDX2031/CDX2033). |
| Evidence | The verifier's phase 3 evaluates every manifest entry against the deployment policy and rejects the binary on the first denial; phase 4 rejects any declared effect that no manifest entry covers. `codex/test/manifest-pin` reads a two-capability scoped manifest out of a freshly built binary and pins each cap-id, direction, scope length and scope; sabotaging `manifest-cap-direction` to answer readwrite unconditionally moves exactly those two lines. |
| Withdrawn | **"Compile log confirms no `cdx-capability-not-granted` suppressions."** There is no suppression mechanism for CDX4001 or for any other diagnostic, so there is nothing for a log to confirm. CDX4001 is a hard error and it fires on one condition only: the definition named `opening` declares an effect that is not one of the 24 names in the vocabulary. **"capability table dump shows zero default grants"** overstates the verifier's report, which states per-phase pass or fail and the denial reason, not a table dump. |
| Manufacturer | Do not pre-populate capability leases. Ship the CDX binary with an empty capability table and let the provisioning step grant only what the deployment requires. Document the minimum capability set. |
| Cross-ref | ETSI EN 303 645 §5.1-2, NISTIR 8259A Capability 1, IEC 62443-4-2 CR 7.7 |

### 1(c) -- Protection of Stored, Transmitted, and Processed Data

| Field | Value |
|-------|-------|
| Class | MECHANISM |
| Mechanism | AES-GCM-256 and ChaCha20-Poly1305 for data at rest. TLS 1.3 for data in transit (X25519 key exchange, Ed25519 authentication), with peer identity checked against the host dialled per RFC 6125. Linear types prevent key material aliasing -- a key declared `linear` cannot be copied, only moved, so it cannot leak through a second reference. Content-addressed FactStore ensures integrity of stored artifacts via SHA-256 hashes. |
| Evidence | Compile log: CDX2061 on a `linear` key type that is aliased = build failure. A function reaching the network must declare `[Network]` or a dotted refinement of it; an undeclared effect is CDX2031. FactStore CRC-frame verification on every read. `codex/test/apps/tls-fetch-loopback` refuses a genuine certificate issued for another host, and reports True when the matcher is sabotaged. |
| Withdrawn | **"TLS channel construction requires `[Network, Crypto]` effect row -- absence is a compile error."** There is no `Crypto` capability. The vocabulary is the 24 names derived in `Foreword chapter Capability`, and `Crypto` is not among them, so the row named an effect row that no program can write. Measured 2026-07-27: `opening : [Network, Crypto] Integer` gives `error CDX4001: Effect 'Crypto' has no capability the manifest can carry` -- the compile error is real but it is the opposite of the one this row described. Crypto in Codex is ordinary library code with no capability of its own; what is effect-gated is the channel it runs over. |
| Manufacturer | Provision TLS certificates and AES keys through the capability system. Enable encryption-at-rest for any persistent storage. Rotate keys per the trust lattice policy (documented in the deployment manifest). |
| Cross-ref | ETSI EN 303 645 §5.8-1/§5.8-2, NISTIR 8259A Capability 5, IEC 62443-4-2 CR 3.1/CR 4.1 |

### 1(d) -- Protection of Availability, Including Resilience Against DoS

| Field | Value |
|-------|-------|
| Class | BY-CONSTRUCTION + MECHANISM |
| Mechanism | The `punctual` keyword bounds a function structurally: no unbounded calls (CDX6001), no heap (CDX6002), no closures (CDX6003), no effects (CDX6004), no self-recursion (CDX6005). Those five are hard errors, and they are what makes the bound exist at all. The emitter then counts the finished instructions of each such function and reports them (CDX6010). Fuel-capped recursion eliminates unbounded descent elsewhere. The bare-metal scheduler is preemptive on a timer tick, per core, so no task monopolises a CPU. Dead-code elimination removes unreachable paths that could hide resource exhaustion. |
| Evidence | CDX6001-CDX6005 are errors: a `punctual` function that allocates, recurses, captures, or performs an effect does not compile. CDX6010 lists the per-function **instruction count** and its percentage of the declared budget. `build/wcet-validate.ps1` runs the binary under `codex-vm -wcet` and gates on observed <= budget per invocation, which is the step that turns the static count into a checked claim. |
| Manufacturer | Mark all externally-reachable entry points `punctual` with an instruction budget. **Run `build/wcet-validate.ps1`** -- the compiler alone will not fail a build for an exceeded budget. Convert instruction counts to wall-clock against your own clock speed and pipeline; the compiler does not do this and does not claim to. Size preemption intervals to match the deployment's real-time requirements. Load-test with sustained input at maximum rate. |
| Withdrawn | **"CDX6010 info messages list per-function WCET bounds in cycles."** They list instructions. The count is architecture-independent by design and the compiler explicitly declines to claim wall-clock time, which depends on clock speed and pipeline. **"Compile failure on any `punctual` function whose bound cannot be proven."** Measured 2026-07-27: a nine-parameter `punctual 1` function surviving the inliner compiles to a **86,632-byte binary at exit 0**, reporting `CDX6010: 106 instructions (10600% of budget 1)` and `warning CDX6011: exceeds budget: 106 / 1`. An undecodable body raises CDX6012, also a warning. Exceeding or failing to determine a budget does not fail the build; the five structural restrictions do. **"Scheduler log: preemption count and max-latency per task"** -- no such log exists. Per-core preemption evidence is a counter at cell 36216, read by `codex/test/smp-preempt`. |
| Cross-ref | ETSI EN 303 645 §5.7-1, NISTIR 8259A Capability 6, IEC 62443-4-2 CR 7.1/SAR 7.2 |

### 1(e) -- Minimisation of Attack Surface

| Field | Value |
|-------|-------|
| Class | BY-CONSTRUCTION |
| Mechanism | No OS kernel, no libc, no dynamic linker, no shell -- the entire POSIX/Win32 attack surface is absent. Effect types restrict each module to its declared capabilities; unreachable code is eliminated at compile time. The capability table enumerates the exact surface: every granted cap-id, direction, scope, and max-duration is visible in the CDX header. The 5-phase CDX verifier rejects binaries that request capabilities beyond their declared effect row. |
| Evidence | CDX header capability table: enumerable, auditable, diffable between releases, and pinned by `codex/test/manifest-pin`, which reads the header at offset 136 and compares every cap-id, direction, scope length and scope against a recorded expectation. The verifier's five phases are integrity, author, capabilities, effects and proofs (`codex/os/verify/CdxVerifier.codex`); phase 4 requires every effect in the binary's effect section to be covered by an entry in its capability manifest. |
| Withdrawn | **"Dead-code elimination metrics in compile log (bytes removed)."** No such diagnostic exists -- there is no DCE code in `CdxCodes.codex` and no emit site reports one. The elimination is real and was measured once by diffing binaries (the seed lost 412 definitions and 140,575 bytes), but that is a measurement someone performed, not evidence the build hands you. To obtain it, compare two `Sut.cdx` hashes and sizes yourself. |
| Manufacturer | Audit the capability table diff between releases. Any new cap-id entry must be justified in the release notes. Run the verifier against the deployment policy before shipping. |
| Cross-ref | ETSI EN 303 645 §5.6-1, NISTIR 8259A Capability 4, IEC 62443-4-1 SR-2/SD-3 |

### 1(f) -- Logging and Monitoring of Security-Relevant Events

| Field | Value |
|-------|-------|
| Class | MECHANISM + DEPLOYMENT |
| Mechanism | The FactStore provides an append-only event log, and append-only structurally: `DiskFacts` exposes no delete, truncate, erase or compact operation, so a written fact cannot be removed. Facts are content-addressed by `sdw-hash`. Capability lease grants and revocations are recorded as facts in the trust lattice with Ed25519 signatures. A function that writes such records over a channel must declare that channel's effect, so the *transport* is visible in the type signature even though the audit character of the write is not. |
| Evidence | FactStore integrity: the absence of any removing operation in `DiskFacts`. Lease history queryable via the repository protocol. |
| Withdrawn | **"CRC-framed" and "CRC verification on every read."** Measured 2026-07-28: there is no `crc` and no `checksum` anywhere in the fact-store chapters (`DiskFacts`, `FactLog`, `FactDisk`). CRC framing is an unshipped proposal in `Annotations.md` Addendum I and belongs to the ANNOTATION store, not this one. The read claim was false twice over, because `FactDisk`'s own prose says what it does **not** re-verify is the log entry's own content hash. Append-only survives on structure; the framing and the per-read verification do not exist. |
| Withdrawn | **The effect-typed `[Audit]` channel.** There is no `Audit` capability and no `effect Audit where` declaration anywhere in the tree. The 24-name vocabulary is derived in `Foreword chapter Capability` and `Audit` is not among the 18 rows or the six dotted refinements. Measured 2026-07-27: `opening : [Audit] Nothing` gives `error CDX4001: Effect 'Audit' has no capability the manifest can carry`. **No program can declare this effect**, so the row described a mechanism that is not merely unused but inexpressible, and "presence of `[Audit]` effect" was evidence no compile log could ever show. What the mechanism would take is a row in the capability table, a bit, and an `effect Audit where` declaration with its operations; that is a language change and is not made here. Until it exists, audit-channel separation is a manufacturer obligation, not a compiler guarantee. |
| Manufacturer | **Separate the audit channel yourself; the compiler will not do it for you.** Route audit records to a tamper-evident sink (append-only storage or remote SIEM) over a channel whose effect you declare, and keep that channel distinct from ordinary output by convention, since no capability distinguishes them. Define retention policy. Monitor for gaps in the hash chain, which indicate truncation or tampering. |
| Cross-ref | ETSI EN 303 645 §5.11-1, NISTIR 8259A Capability 2, IEC 62443-4-2 CR 6.1/CR 6.2 |

---

## Annex I, Part II: Vulnerability Handling Requirements

### 2(a) -- Unique Identification of Components and Vulnerabilities

| Field | Value |
|-------|-------|
| Class | BY-CONSTRUCTION + MECHANISM |
| Mechanism | Every CDX binary is content-addressed (SHA-256 over its own content); the Ed25519 signature is applied by the build pipeline, not to every CDX -- `build.ps1` signs the compiler under test and only when the signing key is present, and `compile.ps1` never signs, so an ordinarily compiled CDX is unsigned (measured 2026-07-28). The repository protocol records every artifact as a fact -- immutable, hash-identified, and traceable to the source proposal and verdict that produced it. The trust lattice binds each binary to its author key. |
| Evidence | CDX header, at fixed offsets in its 224 bytes: magic, format version, flags, **32-byte SHA-256 content hash, 32-byte author public key, 64-byte Ed25519 signature**, then the capability, effect, text and rodata section offsets and sizes. The verifier's phase 1 recomputes the content hash and checks the signature over it; phase 2 scores the author key against the trust lattice. FactStore query: provenance chain from binary back to source proposal. |
| Withdrawn | **"build timestamp, source fact-id" in the CDX header.** The 224-byte header (`cdx-build-header`, `Emit/X86_64Chapter.codex`) has neither field, and no reserved slot is populated with either. Provenance beyond the author key lives in the FactStore, not in the binary. **"signed with a per-device keypair."** The build signs with the project signing key; a per-device keypair is generated at first-boot provisioning and is what a *device* signs with, which is a different key at a different stage. Do not read the header's author key as a device identity. |
| Manufacturer | Maintain a software bill of materials (SBOM) derived from the FactStore provenance chain. Register the product in the manufacturer's vulnerability tracking system. Assign CVE-compatible identifiers to any discovered vulnerability and cross-reference to the originating fact-id. |
| Cross-ref | ETSI EN 303 645 §5.2-1, NISTIR 8259A Capability 7, IEC 62443-4-1 DM-1/DM-2 |

### 2(b) -- Effective and Regular Vulnerability Handling and Remediation

| Field | Value |
|-------|-------|
| Class | MECHANISM + ORGANIZATIONAL |
| Mechanism | OTA update with dual-gate verification: stage 1 verifies the Ed25519 signature and hash of the update payload; stage 2 verifies the installed image post-flash. Anti-rollback counter prevents downgrade attacks. Capability lease revocation propagates through the trust lattice -- a compromised component's leases can be revoked without reflashing the entire device. The repository protocol's proposal/verdict workflow provides an auditable patch-acceptance pipeline. |
| Evidence | OTA log: dual-gate pass/fail, anti-rollback counter value, old-hash to new-hash transition. Trust lattice: lease revocation record with timestamp and revoking authority. Proposal/verdict chain: full review history for every patch. |
| Manufacturer | Establish a coordinated vulnerability disclosure policy with 24-hour initial notification to ENISA/CSIRT per CRA Article 11. Maintain a security contact (email, web form). Provide free security updates for the support period (minimum 5 years per CRA Article 10). Document the OTA update procedure for end users. Publish advisories that reference the originating fact-id and the remediating fact-id. |
| Cross-ref | ETSI EN 303 645 §5.2-2/§5.3-1, NISTIR 8259A Capability 8, IEC 62443-4-1 DM-3/DM-4 |

---

## Summary Matrix

| Req. | Short Title | Primary Class | Key Codex Feature |
|------|-------------|---------------|-------------------|
| 1(a) | No known vulns | BY-CONSTRUCTION | Linear types, effect types, declared-bound integers |
| 1(b) | Secure defaults | BY-CONSTRUCTION | Manifest derived from declared effects, effect rows |
| 1(c) | Data protection | MECHANISM | AES-GCM-256, TLS 1.3 with peer-name checking, linear key types |
| 1(d) | Availability / DoS | BY-CONSTRUCTION | `punctual` structural restrictions (CDX6001-6005), fuel-capped recursion |
| 1(e) | Minimal surface | BY-CONSTRUCTION | No OS/libc, dead-code elim, capability manifest, 5-phase verifier |
| 1(f) | Logging | MECHANISM + DEPLOYMENT | Append-only FactStore (no removing operation exists) |
| 2(a) | Component ID | MECHANISM | Content-addressed hashes, Ed25519 signing |
| 2(b) | Vuln handling | ORGANIZATIONAL | OTA dual-gate, lease revocation, trust lattice |

**What the summary above deliberately no longer says.** 1(d) claimed
`punctual` WCET *proofs*: the instruction count is exact and the structural
restrictions are hard errors, but an exceeded budget is a warning and no
build fails for it, so the gate is `build/wcet-validate.ps1` and not the
compiler. 1(f) claimed an `[Audit]` effect, which does not exist. 1(a)
claimed overflow prevention in general, which holds only where a bound is
declared. Each row above carries the measurement.
