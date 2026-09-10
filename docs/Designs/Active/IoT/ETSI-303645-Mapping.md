# ETSI EN 303 645 Compliance Mapping to Codex

Maps 40 EN 303 645 provisions to Codex language and platform features.

The standard carries 33 mandatory provisions and 35 recommendations. The 40
rows here are drawn from both, and no check has ever established that all
33 mandatory provisions are among the 40. **Treat coverage as unverified**
until someone walks the standard against this table.

**Legend -- Gap column:**
- **Satisfied** -- Codex provides the mechanism by construction
- **Partial** -- Codex provides the tool; manufacturer must configure/enable
- **Manufacturer** -- process obligation outside the compiler/runtime
- **Withdrawn** -- the row claimed a mechanism that does not exist. The
  measurement is recorded beside it and the provision is **not met**.

**A row here is a claim and nothing re-reads it.** The rows below were fired
at the compiler on 2026-07-27 against seed `A5758E05`. A claim that does not
survive firing is withdrawn, never softened into something the same
mechanism can satisfy, and is re-earned by fixing the code rather than the
wording. Provision 5.5 is the worked precedent (`docs/KingsAndCourts.md`).

---

## 5.1 -- No Universal Default Passwords

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.1-1 | No universal default passwords | Security | Trust lattice generates per-device identity at provisioning; no shared secrets in CDX binaries | Partial |
| 5.1-2 | No easily guessable default passwords | Security | Capability manifests bind credentials to device key pairs (Ed25519); no password-based auth by default | Satisfied |
| 5.1-3 | Passwords shall be unique per device | Security | Trust lattice derives per-device keys from hardware identity; key material never duplicated across devices | Partial |
| 5.1-4 | Authentication not based on universal credentials | Security | Ed25519 per-device signing keys; trust lattice enforces unique identity chain | Satisfied |

## 5.2 -- Vulnerability Disclosure

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.2-1 | Vulnerability disclosure policy | Process | Fact store provides audit trail for all code changes; manufacturer must publish disclosure process | Manufacturer |

## 5.3 -- Software Updates

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

## 5.4 -- Secure Storage of Credentials

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.4-1 | Credentials stored securely | Security | Linear types (CDX2061/2063) prevent credential duplication; bare-metal has no swap/tmpfs leak surface | Satisfied |
| 5.4-2 | Hard-coded credentials prohibited | Security | **Nothing in the compiler rejects a hard-coded credential.** A `Text` or `List Integer` constant is an ordinary literal | **Withdrawn** |
| 5.4-3 | Credentials in transit encrypted | Security | Effect types declare all I/O; credential-bearing channels require encryption capability | Partial |
| 5.4-4 | Device identity credentials unique | Security | Trust lattice per-device key derivation; Ed25519 keypair generated at provisioning | Satisfied |

**THIS DOCUMENT AND THE CODE DO NOT SHARE A NUMBERING SCHEME.** The
provisions here follow the standard's clause numbering (5.3 keep software
updated, 5.4 securely store sensitive security parameters).
`codex/foreword/core/ComplianceEvidence.codex` does not: the module's
`5.3-*` block holds the cryptography and credential rows and the module's
`5.4-*` block holds the software-update rows. The row withdrawn here as
**5.4-2** is the row the module calls **5.3-5**. A withdrawal on one side
therefore does not reach the twin row on the other side, and the two cannot
be cross-referenced by id at all.

**Do not renumber either side on a guess.** Neither the 8259A PDF nor EN 303
645 is in the tree; aligning the ids needs the standard's own clause list,
not an inference from the titles. Until the clause list is in hand,
cross-reference these tables by TITLE, never by section id.

**5.4-2 is withdrawn, and the module's twin row (`Organizational`, artifact
`none`) with it.** Measured 2026-07-27 and again 2026-07-28: a chapter
defining `api-key = "AKIAIOSFODNN7EXAMPLE"`, `device-password = "admin"` and
an eight-byte `signing-key` list compiles with **zero diagnostics**, the
only `CDX` line in the log being an informational `CDX4030: PIPELINE`, and
prints all three values at runtime. The capability manifest carries cap-id,
direction, scope length, scope and eight zero bytes: the manifest has no
notion of a key literal and cannot reject one. Effect types govern what a
function is permitted to *do*, not what a constant can *contain*. This
provision is a manufacturer obligation with no compiler support; a secret
scanner over source would be the instrument, and no such scanner exists.

## 5.5 -- Secure Communication

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.5-1 | Communicate securely | Security | Effect types enforce declared I/O channels; capability manifest specifies permitted protocols | Partial |
| 5.5-2 | Confidentiality of sensitive data | Security | Linear types prevent accidental aliasing of sensitive buffers; bare-metal -- no OS-level cache leak | Satisfied |
| 5.5-3 | Cryptographic protocols up to date | Process | Signed CDX uses Ed25519 + SHA-256; manufacturer must track algorithm deprecation | Manufacturer |
| 5.5-4 | Cryptographic suites configurable | Security | **The capability manifest has no cipher-suite field.** Suite selection is ordinary code in the TLS chapter, changeable only by rebuilding and shipping a new signed CDX | **Withdrawn** |
| 5.5-5 | Use best-practice cryptography | Process | Ed25519 + SHA-256 are current best practice; manufacturer reviews periodically | Manufacturer |
| 5.5-6 | No known weak algorithms | Security | **The compiler has no notion of an algorithm identifier** and cannot reject one. What holds instead: the tree implements no weak algorithm, so there is nothing deprecated to select | **Withdrawn** |
| 5.5-7 | Manage cryptographic material securely | Security | Linear types (CDX2061) enforce single-owner for key material; no use-after-free | Satisfied |

**5.5-4 and 5.5-6 are withdrawn: algorithm policy is not in the capability
manifest.** A manifest entry is a little-endian cap-id, a direction, a scope
length, the scope bytes and eight zero bytes (`manifest-cap-bytes`,
`Emit/X86_64Chapter.codex`): there is no algorithm field, no cipher-suite
field, and no reserved slot carrying either. A grep of `codex/compiler` for
cipher, algorithm-identifier or deprecated-algorithm handling returns
nothing (2026-07-27). Configurability at deployment time is therefore **not
available**: changing a suite means rebuilding. The withdrawal does not
weaken 5.5-1 through 5.5-3, 5.5-5 or 5.5-7, which rest on mechanisms that do
exist, nor 5.5's peer authentication, which carries a negative test.

## 5.6 -- Minimise Exposed Attack Surface

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.6-1 | Disable unused network interfaces | Security | Capability manifest whitelists enabled interfaces; bare-metal has no background services | Satisfied |
| 5.6-2 | Minimise pre-authenticated attack surface | Security | Bare-metal compilation -- no OS, no libc, no shell; only declared capabilities are reachable | Satisfied |
| 5.6-3 | Software operates with least privilege | Security | Effect types + capability manifests enforce minimal privilege per function | Satisfied |
| 5.6-4 | Disable debug interfaces in production | Security | **The CDX binary does not strip debug sections -- it adds one.** Every binary with at least one function carries a symbol map naming every function and sets the has-debug-info flag | **Withdrawn** |

**5.6-4 is withdrawn: the binary adds a debug section rather than stripping
one.** `cdx-build-header` sets `debug-off = if func-count > 0 then ... else
0` and flag value 19 rather than 3, which is bare-metal plus needs-heap plus
**has-debug-info**. The embedded MAP1 symbol map runs to roughly 79 KB and
2,600 functions in the seed (2026-07-27), and the crash reporter depends on
the map. The map is a deliberate and useful property, and a production
binary shipping a full symbol table is exactly what this provision asks
about. No strip flag exists. What holds instead: there is no debug
*interface* to disable, because there is no shell, no JTAG stub and no
dynamic loader. The exposure is the symbol table, not a live channel.

## 5.7 -- Software Integrity

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.7-1 | Verify software integrity at boot | Security | Signed CDX (Ed25519 + SHA-256); boot verifies content hash against embedded signature | Satisfied |
| 5.7-2 | Alert on unauthorized changes | Security | Fact store records expected hashes; runtime hash mismatch triggers alert via declared effect | Partial |

## 5.8 -- Personal Data Protection

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.8-1 | Personal data protection by design | Security | Linear types prevent data aliasing; effect types make all data flows explicit and auditable | Satisfied |
| 5.8-2 | Consent mechanism | Process | Capability manifest can declare consent gates; manufacturer must implement UI | Manufacturer |
| 5.8-3 | External sensing clearly indicated | UX | Effect types require declaration of sensor access; manufacturer provides physical indicator | Partial |

## 5.9 -- Resilience

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.9-1 | Resilient to outages | Safety | `punctual` forbids heap, closures, effects, self-recursion and unbounded callees as hard errors (CDX6001-6005), and the emitter reports an exact instruction count (CDX6010). A declared bound rejects an out-of-range literal (CDX2050) and refuses an unprovable range (CDX2051). **An exceeded budget is a warning, not a build failure**, so `build/wcet-validate.ps1` is the gate. Plain `Integer` arithmetic wraps silently | Partial |
| 5.9-2 | Resilient to network disruption | Safety | Bare-metal runtime continues local operation; no network dependency for core function | Satisfied |
| 5.9-3 | Graceful degradation | Safety | Effect types partition essential vs. optional I/O; `punctual` makes an essential path structurally bounded and exactly counted. It does not establish a deadline: the count is instructions, and converting it to time is the integrator's step | Partial |

## 5.10 -- Telemetry

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.10-1 | Examine telemetry data | Process | Fact store audit trail records all emitted telemetry; manufacturer provides inspection tool | Manufacturer |

## 5.11 -- Deletion of Personal Data

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.11-1 | Easy mechanism to delete data | Security | **There are no drop semantics and nothing zeroes memory on scope exit.** Linear types do guarantee single-owner, so there is no second reference to miss, but erasure is the programmer's job | **Withdrawn** |

**5.11-1 is withdrawn: Codex has no drop semantics at all.** Allocation is a
bump of R10 and there is no collector, therefore an allocation persists
until the producing function returns, at which point the region can be
reused by a later `pitch`, un-zeroed. `__alloc` zero-fills a block **when
the block is handed out**, not when the block is released, therefore a
secret's bytes stay in RAM until something else is allocated over them. The
poison build exists precisely because that zero-fill-on-allocation is
load-bearing.

What the language gives this provision is narrower: `linear` guarantees a
single owner, therefore there is no aliased copy hiding somewhere a delete
would miss. Erasing the bytes is the program's responsibility and no
compiler mechanism performs the erasure. `IEC62443-Evidence.md` FR4 carries
the same withdrawal.

## 5.12 -- Installation and Maintenance

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.12-1 | Simple and secure installation | UX | CDX is single-image bootable binary; no multi-step installation; signed at build | Partial |

## 5.13 -- Input Validation

| Provision | Title | Class | Mechanism | Gap |
|-----------|-------|-------|-----------|-----|
| 5.13-1 | Validate input data | Security | Bounded integers (CDX4010/2050) reject overflow; linear types prevent buffer aliasing; bare-metal has no injection surface | Satisfied |

---

## Summary

Counted 2026-07-27 by tallying the Gap column of the tables above.
**Do not carry these figures forward** -- recount them, which is one pass
over the file.

| Gap Status | Count |
|------------|-------|
| Satisfied | 17 |
| Partial | 12 |
| Manufacturer | 6 |
| **Withdrawn** | **5** |
| **Total rows** | **40** |

**A figure in this summary is a tally of the tables above, and nothing
enforces that.** Recount before quoting any of the five (L-COUNT). The
standard's own 33 mandatory provisions is a different number from this
document's row count and must not be substituted for it.

The 6 Manufacturer gaps are process obligations (disclosure policy, update
cadence, algorithm review, telemetry inspection and two more) that no
compiler can satisfy. The 12 Partial items require manufacturer
configuration of mechanisms Codex already provides.

**The 5 withdrawn provisions are not met**, and each says why in place:

| Provision | The mechanism the row named, and what is there instead |
|---|---|
| 5.4-2 | a manifest that rejects static key literals; hard-coded credentials compile clean |
| 5.5-4 | a cipher-suite field in the capability manifest; there is none |
| 5.5-6 | a compiler that rejects deprecated algorithm identifiers; it has no such notion |
| 5.6-4 | debug-section stripping; every CDX with a function adds a symbol map instead |
| 5.11-1 | drop semantics zeroing memory on scope exit; there are no drop semantics |

**No gate reads this file, no test cites it, and nothing re-evaluates a row
once the row is written down.** A row is worth exactly the firing behind it,
and the firing has a date.
