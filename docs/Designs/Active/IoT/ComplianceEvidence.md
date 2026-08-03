# Compliance Evidence Architecture: Conformity as a Build Artifact

**Created**: 2026-06-12 (reek)
**Status**: Catalogs and report generator shipped; **the evidence plug
does not exist.** Built:
`codex/foreword/core/ComplianceEvidence.codex` (requirement catalogs),
`codex/foreword/core/ComplianceBuild.codex`,
`codex/build/compliancereportScript.codex` (report generator), and the
`compliance-evidence` / `compliance-report` tests. Not built: the
`EvidencePlug` that Constraint 2 below makes the architectural centre
of this design -- nothing emits a signed evidence package alongside a
firmware build today. That plug is the remaining work.
**Upstream**: `docs/PM/IoT/AGENT-PROMPT.md` deliverable 4,
`docs/PM/IoT/Compliance/` (EU CRA, ETSI EN 303 645, NISTIR 8259,
IEC 62443), prospectus Phase 3

## Note: This Doc Duplicates the Compliance Matrix

The per-requirement tables below now exist in a second place:
`docs/Reference/CRA-Compliance-Matrix.md`. Two hand-maintained copies of
the same requirement mapping is precisely the staleness problem this
design was written to abolish, and the copies will diverge -- one of them
already will have by the time this is read.

**Recommendation: merge.** `CRA-Compliance-Matrix.md` becomes the single
requirement mapping (it is a Reference doc, which is what a requirement
table is), and this design keeps only the architecture: the honesty
principle, the classification scheme, the plug, and the fact-store
integration. Whoever next touches either file should do the merge rather
than update both.

## The Problem

By 2027-12-11 any product with digital elements sold in the EU must
demonstrate CRA compliance or exit the market; vulnerability-
reporting obligations begin 2026-09-11. Today compliance evidence
is consultancy work-product: hand-written, per-product, stale the
moment firmware changes. Codex's position -- the entire reason the
IoT initiative exists -- is that most technical requirements are
discharged *by construction*, so the evidence can be generated
mechanically from what the compiler already knows and signs.

The deliverable is a build artifact: every firmware build emits,
alongside the CDX binary, a signed, content-addressed evidence
package that maps each regulatory requirement to the mechanism
satisfying it and the proof artifact demonstrating it.

## The Honesty Principle

A compiler cannot make a vulnerability-disclosure policy exist, and
an evidence generator that claims otherwise would be rejected by
the first competent notified body and poison the entire pitch.
Every requirement is classified, and the classification is printed
in the evidence:

| Class | Meaning | Evidence form |
|---|---|---|
| **BY-CONSTRUCTION** | The language/toolchain makes violation inexpressible | Compiler diagnostics + binary properties, machine-checked |
| **MECHANISM** | A runtime mechanism enforces it; correct config required | Capability manifest, verifier policy, signed config facts |
| **DEPLOYMENT** | Satisfied only by how the product is provisioned/operated | Template + checklist the manufacturer completes; their inputs become signed facts |
| **ORGANIZATIONAL** | Process obligation on the manufacturer (reporting, support period) | Named as out of toolchain scope; pointer to manufacturer process record |

The generated package therefore never says "compliant." It says:
of the N applicable requirements, K are discharged by construction
with these proofs, M by mechanism with these manifests, and here is
the residual checklist that is the manufacturer's to own. That
residual list IS the product's compliance workplan -- which is the
honest version of the "$200K consultancy replacement" claim.

## Constraints

1. Generated, never hand-written per product. Regeneration on every
   build; byte-stable given identical inputs (no timestamps inside
   the signed body; build time is a separate fact).
2. The compiler stays lean. Evidence generation is a **plug**
   (EmitterExodus pattern), not a compiler phase. The compiler's
   only obligations are: keep diagnostics machine-readable (they
   are: numbered CDX codes), and keep the CDX header truthful.
3. Every claim traces to an artifact hash. A claim without a
   content-addressed artifact is not emitted as a claim.
4. The evidence package is itself a fact: content-addressed,
   Ed25519-signed, stored in the FactStore -- the audit trail the
   CRA's technical-documentation duty wants is then the storage
   layer's native behavior.

## Inputs the Generator Consumes

All already exist or are produced by in-flight designs:

| Input | Source | Feeds |
|---|---|---|
| CDX header: content hash, author key, signature | CdxWriter | Integrity/authenticity claims |
| Capability manifest (ids, direction, scope, duration) | CDX header | Least-privilege / access-control claims |
| Effects table + effect-capability coverage | CDX header, verifier phase 4 | "No undeclared I/O" claim |
| Diagnostic stream: CDX4010 (bounds proven), CDX4020 (proofs erased), zero-error status | compile log | Memory-safety / integer-safety claims |
| Linearity discipline (CDX2061/2063 enforced) | compiler identity + version fact | Resource-lifecycle claims |
| fact_hashes (dependency content hashes) | CDX header | SBOM |
| Source manifest: chapter list + per-chapter hashes | concat step | SBOM, reproducibility |
| Verifier policy + trust lattice snapshot | codex/os/verify, os/trust | Update-verification and identity claims |
| HAL capability grants | `HardwareAbstractionLayer.md` | Attack-surface claims |
| OTA design conformance | `OTAFirmwareUpdate.md` | Secure-update claims |

## The Design

### Pipeline

```
build.ps1 / device build
  -> CDX binary (signed)
  -> compile log (diagnostics, phase metrics)
  -> evidence plug (codex/plugs/EvidencePlug)
       inputs:  CDX bytes + log + source manifest + requirement
                catalogs (data files, one per regulation)
       outputs: Evidence.cdxe   (canonical, machine-readable)
                Evidence.html   (human/reviewer rendering)
                SBOM.cdx.json   (CycloneDX-shaped export)
       then:    evidence hashed, signed, stored as ComplianceFact
                referencing the firmware's content hash
```

The requirement catalogs are checked-in data (one chapter per
regulation under the plug), each entry: requirement id, normative
text reference (not the text itself -- licensing), classification,
mechanism pointer, artifact selector. Regulation updates are
catalog edits, not code changes.

### Claim format (per requirement)

```
  Claim = record {
   requirement : Text,          -- e.g. "ETSI 303645 5.1-1"
   class       : ClaimClass,    -- ByConstruction | Mechanism | Deployment | Organizational
   mechanism   : Text,          -- prose: what discharges it
   artifacts   : List Text,     -- content hashes (diag log, manifest slice, fact ids)
   residual    : Maybe Text     -- what remains for the manufacturer, if anything
  }
```

### Requirement mappings

The catalogs below are the design-level mapping; the catalog files
carry one entry per *individual* mandatory provision (ETSI's 33,
CRA Annex I items, 8259A capabilities, 62443-4-2 CRs).

**ETSI EN 303 645 -- the 13 provisions:**

| Provision | Class | Mechanism |
|---|---|---|
| 5.1 No universal default passwords | MECHANISM + DEPLOYMENT | Identity is a per-device Ed25519 keypair generated at first boot (Identity design); there is no password scheme to default. Residual: manufacturer provisioning flow must not inject shared keys |
| 5.2 Vulnerability disclosure | ORGANIZATIONAL | Out of toolchain scope. FactStore gives the tracking substrate; policy is the manufacturer's |
| 5.3 Keep software updated | MECHANISM | OTA design: signed CDX, verifier-gated activation, LwM2M Object 5. Residual: support-period commitment |
| 5.4 Securely store security parameters | MECHANISM | Key storage per Identity design (encrypted at rest, kernel-pinned, zeroed). Hardware-anchored storage where the SoC provides it (ESP32-C6) is DEPLOYMENT |
| 5.5 Communicate securely | MECHANISM | TLS/DTLS with constant-time primitives (ProtocolStack); effect typing proves no bypass path exists in firmware code -- that sub-claim is BY-CONSTRUCTION |
| 5.6 Minimize attack surfaces | BY-CONSTRUCTION | No OS, no libc, no dynamic linking; binary contains exactly compiler-emitted code; capability manifest enumerates every reachable effect; unused services are unrepresentable rather than disabled |
| 5.7 Software integrity | BY-CONSTRUCTION + MECHANISM | SHA-256 content hash + Ed25519 signature verified before any code runs (verifier phases 1-2) |
| 5.8 Personal data protection | MECHANISM + DEPLOYMENT | AEAD primitives + effect-typed data paths; what counts as personal data is the product's call |
| 5.9 Resilient to outages | MECHANISM | Capability leases expire to safe-mode; OTA A/B rollback. Partial -- watchdog/persistence patterns are per-product |
| 5.10 Examine telemetry | DEPLOYMENT | Observability modules (codex.os.observe) exist; using them is product scope |
| 5.11 Easy personal-data deletion | DEPLOYMENT | Product scope |
| 5.12 Easy installation/maintenance | DEPLOYMENT | Product scope |
| 5.13 Validate input data | BY-CONSTRUCTION (partial) | Bounded integers + static prover (CDX4010), exhaustive pattern matching, no silent truncation (CDX2071), typed codecs for every parser the toolchain ships. Residual: application-level semantic validation |

**EU CRA Annex I Part I (essential requirements), condensed:**

| Requirement | Class | Mechanism |
|---|---|---|
| (1) Designed/produced to ensure appropriate cybersecurity | BY-CONSTRUCTION umbrella | The language guarantees: memory safety via linearity, effect discipline, bounded integers -- cited per sub-claim |
| (2)(a) No known exploitable vulnerabilities at delivery | MECHANISM + ORGANIZATIONAL | Toolchain eliminates the memory-safety CVE class (~70% of critical CVEs per the market research); residual known-vuln review of design-level logic is process |
| (2)(b) Secure-by-default configuration | MECHANISM | Capability manifests default-deny: an effect without a granted capability fails verification (phase 4) |
| (2)(c) Security updates, automatic where appropriate | MECHANISM | OTA design end to end |
| (2)(d) Access control / unauthorized-access protection | MECHANISM | Trust lattice + capability leases; load-time verifier |
| (2)(e) Confidentiality of stored/transmitted data | MECHANISM | AES-GCM/ChaCha20 + TLS/DTLS; effect typing scopes data paths |
| (2)(f) Integrity of data, commands, configuration | BY-CONSTRUCTION + MECHANISM | Signed binaries; content-addressed facts; signed agent-protocol messages |
| (2)(g) Data minimization | DEPLOYMENT | Product scope |
| (2)(h) Availability of essential functions / resilience | MECHANISM (partial) | Lease expiry to safe mode; bounded memory by construction (no GC, survey allocations); DoS-resilience of network endpoints is per-product analysis |
| (2)(i) Minimize own attack surface | BY-CONSTRUCTION | As ETSI 5.6 |
| (2)(j) Reduce incident impact | MECHANISM | Capability scoping bounds blast radius; forensics chain records decisions |
| (2)(k) Security-relevant logging | MECHANISM | FactStore/forensics layer (note gap: durable persistence of the forensic chain is open work) |
| (2)(l) Secure data deletion | DEPLOYMENT | Product scope |
| Part II vulnerability handling (SBOM, disclosure, 24h/72h reporting, 5y support) | ORGANIZATIONAL + toolchain SBOM | SBOM generated (below); reporting clocks and support commitments are the manufacturer's. The evidence package prints the Part II checklist explicitly |

**NISTIR 8259A core baseline:**

| Capability | Class | Mechanism |
|---|---|---|
| Device identification | MECHANISM | Ed25519 public key is the device identity |
| Device configuration | MECHANISM | Capability leases; signed config facts; policy engine |
| Data protection | MECHANISM | Crypto floor + effect-typed I/O |
| Logical access to interfaces | BY-CONSTRUCTION + MECHANISM | Effects gate every interface; capabilities gate every effect |
| Software update | MECHANISM | OTA design |
| Cybersecurity state awareness | MECHANISM (partial) | Observe/forensics modules; durable audit persistence is open work |

**IEC 62443-4-2 (component requirements), by foundational
requirement:** FR1 identification/authn → trust handshake +
Ed25519; FR2 use control → capability leases/policy engine; FR3
system integrity → signed CDX + verifier; FR4 confidentiality →
AEAD/TLS; FR5 restricted data flow → effect rows + capability
scoping (zones/conduits map onto trust-lattice domains); FR6
timely response → forensics/anomaly facts (partial); FR7 resource
availability → bounded memory by construction, lease expiry,
collision-checked stack. Target capability level: **SL-C 2 claims
plausible from the mechanisms above; SL 3-4 claims require the
62443-4-1 process evidence (secure development lifecycle), which
is organizational and partially satisfied by this very
architecture (gated builds, signed facts, mandatory review
verdicts) -- catalog as MECHANISM(partial) + ORGANIZATIONAL.**

### SBOM

The CDX header's fact_hashes plus the source manifest are already
a content-addressed SBOM -- stronger than name/version coordinates
because identity is the hash. The plug exports a CycloneDX-shaped
JSON view (Json foreword chapter) for ecosystem tooling, with each
component keyed by content hash; the canonical SBOM remains the
fact-graph itself. CRA Part II's SBOM duty is satisfied by this
export plus the rendering in Evidence.html.

### Threat model integration

The evidence package cross-references `ThreatModel.md` for each
claim. Where a requirement maps to an attack class:

- The claim cites the specific attack (e.g., "CRA 2(a): memory
  corruption -- see ThreatModel §1.1") and the defense mechanism.
- The residual risk register entries (R1-R10) are included in the
  evidence as an appendix. Each residual states: what is not
  covered, why, and what the manufacturer must do to close the
  gap for their target SL.
- The per-board security posture differs (ESP32-C6 has flash
  encryption and hardware crypto; STM32F4 does not). The evidence
  plug parameterizes by target board and adjusts the residual
  checklist accordingly.

This prevents the evidence from being a flat checklist of "we
have linear types, therefore safe." It is a structured argument:
here is the attack, here is the defense, here is the proof
artifact, and here is what remains.

### Reproducibility claim

Because the build is a fixed point and signing covers the content
hash, the evidence can state: rebuilding this source with this
seed yields this exact binary. That is a claim no mainstream
toolchain can sign. It anchors the whole package: every other
artifact hash hangs off a binary the reviewer can re-derive.

## Memory and Time-Complexity Risk

The plug processes inputs linear in binary + log size, well under
existing plug workloads (HTML plug renders 1.37 MB IR in ~2.8 s).
Catalogs are static data. No risk flags. The one rule: the plug
must stream the log rather than `buf-read-bytes` it into lists.

## Open Questions

1. **Notified-body acceptance.** Will compile-time evidence be
   accepted under the EN 18031 harmonised-standard route, or only
   as supporting technical documentation? Partnership question
   (TUV SUD / Bureau Veritas per the prospectus); the format
   should anticipate "supporting documentation" status first.
2. **Catalog licensing.** ETSI/IEC normative text cannot be
   reproduced; catalogs carry ids + paraphrase. Confirm paraphrase
   depth with counsel before anything ships externally.
3. **Evidence for the toolchain itself.** Reviewers will ask "who
   verifies the compiler?" The fixed point + self-verifying seed
   is the answer; decide how much of the bootstrap story
   (BS2/BS3) the package includes by default.
4. **PDF rendering.** HTML now (plug exists as a pattern); PDF via
   a future plug or print-from-HTML guidance. Not load-bearing.
5. **Durable forensic persistence** (gap noted twice above) -- the
   audit-trail claims for CRA (2)(k) and 8259A state-awareness are
   capped at MECHANISM(partial) until the forensic chain persists
   across power loss.
