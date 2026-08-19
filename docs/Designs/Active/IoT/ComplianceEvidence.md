# Compliance Evidence Architecture: Conformity as a Build Artifact

**Created**: 2026-06-12 (reek)
**Status**: Catalogs, report generator and **the evidence plug (2026-08-18,
root; the "Built 2026-08-18" section below) shipped.** Built:
`codex/foreword/core/ComplianceEvidence.codex` (requirement catalogs),
`codex/foreword/core/ComplianceBuild.codex`,
`codex/build/compliancereportScript.codex` (report generator), and the
`compliance-evidence` / `compliance-report` tests, and `codex/plugs/evidence/`
(`EvidencePackage.codex`, `EvidencePlug.codex`, `build.ps1`, `run.ps1`,
`test-evidence.ps1`). Not built: FactStore ingestion of the package, the
per-board residual checklist and the threat-model cross-references (the
section below lists them).
**Upstream**: `docs/Reference/IoT/AGENT-PROMPT.md` deliverable 4,
`docs/Reference/IoT/Compliance/` (EU CRA, ETSI EN 303 645, NISTIR 8259,
IEC 62443), prospectus Phase 3

## Note: This Doc Duplicates the Compliance Matrix

The per-requirement tables below now exist in a second place:
`docs/Designs/Active/IoT/CRA-Compliance-Matrix.md`. Two hand-maintained copies of
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
  -> evidence plug (codex/plugs/evidence, built 2026-08-18)
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

The evidence package cross-references `docs/Designs/Active/IoT/ThreatModel.md` for each
claim. Where a requirement maps to an attack class:

- The claim cites the specific attack (e.g., "CRA 2(a): memory
  corruption -- see docs/Designs/Active/IoT/ThreatModel.md §1.1") and the defense
  mechanism.
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

## Built 2026-08-18 (root): the evidence plug

`codex/plugs/evidence/` is the plug the Design section describes, in the
file-I/O plug shape (`html`): a bare-metal CDX (`build.ps1` bundles
`EvidencePackage` + `EvidencePlug` with the foreword catalogs through
`build/bundle-app.ps1`; the plug chapter cites the package chapter as
`Evidence chapter EvidencePackage`, satisfied from the unit) run by `run.ps1`
in codex-vm off the serial ring, one `key value` line per input, three
documents back between markers.

**Inputs, and what is host-computed.** The plug is fed TEXT: the CDX's first
224 bytes as hex (the header the claims read: content hash at 8, author key
at 40, signature at 72, capability section size at 144, proof section size
at 160, flags at 6), the CDX file's SHA-256, the compile log's SHA-256 and
its diagnostic lines (`<sev> CDX<n>:` and the CODEGEN/errors lines), the
source manifest (one line per `Chapter:` of the bundled source with that
chapter's SHA-256, plus the bundle's), and product/board. Every hash but
one is computed by `run.ps1` on the host; the package says which in two rows
(`hashes.host=` names them, `hashes.plug=` names the header hash the plug
computed itself and the content hash it read from the header), and
`Evidence.inputs.txt` beside the package is exactly the text the plug was
fed, so any hash that appears there was passed in.

**Claims.** One line per catalog entry (61 today), CLAIMED only against an
artifact of this build by the entry's `artifact` selector: `compile-log`
(log present; the note carries errors/warnings and the CDX4010/4020/2050/
2051/2061/2063/2031 counts), `effect-types` (log present AND errors=0; a red
log flips it to not-claimed naming the count), `cdx-binary` (a CDX header
with the magic; the note names size, flags and whether the bare-metal flag
is set), `cdx-header` (content hash non-zero; the note says whether the
signature is present, and an unsigned CDX says ABSENT), `capability-manifest`
(capability section size > 0), `punctual-report` (a report file given).
`crypto-suite`, `trust-lattice`, `ota-design`, `fact-store` are design or
library references and are NOT claimed by this plug ("the product's own test
evidence discharges it"); `none` is the manufacturer's. On the plug's own
build: 20 of 61 claimed. The reviewer page and the SBOM (CycloneDX 1.5
shape, one component per chapter keyed by hash, the binary as the metadata
component) are rendered from the same claims.

**Constraint 1 measured**: the same inputs give a byte-identical
`Evidence.cdxe` (the `stable` arm); no timestamp is inside the package,
`Evidence.sha256` names it, and `-SigningKey <seed>` produces `Evidence.sig`
(pub + Ed25519 signature over the package hash) through the same inline
signer shape `build.ps1` uses for the compiler; without a key the package is
unsigned and says nothing else. **Constraint 3 measured**: `no-log` and
`not-cdx` arms show every claim whose artifact is absent is not-claimed with
the reason, and `dirty-log` shows a red build cannot read as a clean one.

**FactStore ingestion, added 2026-08-18.** `run.ps1 -FactImage <disk>` records
the package as a fact in the disk's Codex fact-store partition (the one
`build/build-img.ps1` puts at the top of every image), through a second
bare-metal program `FactIngest.codex` (kind 50, the AppPersist register):
content one line naming the firmware header hash, the package hash, the
claim counts and whether the package was signed; timestamp the caller's (the
package body stays timestamp-free). It is idempotent by content: a second
run for an unchanged build finds the fact and does not append (`already
present`), and `Evidence.fact.txt` records what landed. `FactIngest` declares
`Device.Block` on `opening`, which a plug does not, because the runtime grant
mask is derived from `opening`'s effect row and a block read with no grant
answers zeros silently (DevelopersGuide pitfall, added the same day). It is a
`CapabilityFact`-shaped record in the log rather than a new FactKind: kind 50
is an AppPersist integer register, so no seed change was needed. The
`fact-ingest` and `no-store` arms of `test-evidence.ps1` measure both the
landing and the refusal on a partitioned disk with no fact partition.

**Per-board residual, added 2026-08-18.** The package now carries a per-board
appendix keyed by the `-Board` string (`ev-board-posture`): four public SoC
attributes (flash encryption, hardware crypto, secure boot, hardware RNG),
each with the requirement sections it moves from the manufacturer's checklist
to the SoC when the board provides it and leaves the manufacturer's when it
does not, and the vendor reference it comes from. ESP32-C6 anchors all four;
STM32F4 none; a board not in the table is `known=n` and answered "the
manufacturer states this SoC's posture", never assumed either way. The
CLAIMS do not change with the board (the `board` arm proves the claim block
is identical across ESP32-C6 and STM32F4); only the `board.*` facts and the
HTML appendix move. Boards named: ESP32-C6, nRF9160, nRF52840, STM32L4,
STM32F4, RP2040, FE310, Pi4 (`codex/boards/` has a chapter for each).

**Not built, in the order they matter.** (1) The catalog merge with
`CRA-Compliance-Matrix.md` recommended above is still pending. (2) The
ThreatModel cross-references (the R1-R10 residual-risk appendix) are not yet
attached per claim. (3) A wiring into `build.ps1` or a device build so the
package is emitted alongside every firmware CDX; today `run.ps1` is called by
hand or by a lane's own script.
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
   across power loss. **The evidence package's own persistence is now
   the fact store** (`-FactImage`), which is that durable log; what stays
   open is the forensic chain of runtime DECISIONS, not the build-time
   evidence record.
