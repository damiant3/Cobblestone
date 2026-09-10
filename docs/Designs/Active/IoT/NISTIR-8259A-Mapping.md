# NISTIR 8259A Compliance Mapping to Codex

Maps the NISTIR 8259A Core Device Cybersecurity Capability Baseline to
Codex language and platform features.

**Legend -- Gap column:**
- **Satisfied** -- Codex provides the mechanism, and it was fired
- **Partial** -- Codex provides part; the rest is manufacturer or absent
- **Manufacturer** -- process obligation outside the compiler/runtime
- **Withdrawn** -- the row claimed a mechanism that does not exist or does
  not do what the capability asks

**A row here is a claim, and one thing re-reads it.**
`codex/test/compliance-report` validates every row in
`codex/foreword/core/ComplianceEvidence.codex`: unknown artifact, unknown
regulation, empty section or title, or a mechanism under forty characters
is a fault, and the test asserts zero faults across all 61 rows. It cannot
check that a claim is TRUE -- no test can -- but it cannot be green while a
row is a placeholder.

---

## The six capabilities

| Capability | Title | Class | Gap |
|---|---|---|---|
| 8259A-1 | Device identification | Mechanism | **Partial** |
| 8259A-2 | Device configuration | Mechanism | Satisfied |
| 8259A-3 | Data protection | Mechanism | Satisfied |
| 8259A-4 | Logical access to interfaces | ByConstruction | Satisfied |
| 8259A-5 | Software update | Mechanism | Satisfied |
| 8259A-6 | Cybersecurity state awareness | Mechanism | **Partial** |

---

## Why two capabilities are Partial

**8259A-1, device identification.** The logical identifier is real:
`IdentityManager` generates an Ed25519 keypair and stores it encrypted,
covered by `codex/test/idm-key-tests`. No physical identifier ships. Object
3 in `codex/foreword/encode/Lwm2m.codex` is a block of resource-ID constants
and nothing else (`lwm2m-res-serial : Integer = 2` is the OMA registry
number naming where a serial would go), nothing populates the resource, and
the one serial field in the tree, `sbc-boot-drive-serial`, is initialised to
`""`. The provisioning path is unproven end to end:
`codex/test/apps/first-boot-ceremony` carries a `.skip` reading *"stub: test
body not yet written. The body prints `first-boot-ceremony:ok` and runs no
ceremony."*

**8259A-6, cybersecurity state awareness.** The surfaces are real
(`CapabilityAudit`, `Forensics`, `NotificationLog`, the content-addressed
fact store) but nothing establishes the universal: there is no check that a
state change cannot occur without a fact being written, and the surfaces are
separate mechanisms rather than one enforced chokepoint.

---

## Data protection rests on the cipher, not on the type system

8259A-3 is Satisfied on the cryptography. At rest: AES-256-GCM (SP 800-38D)
and ChaCha20-Poly1305 (RFC 8439), known-answer tested against the published
vectors. In transit: TLS 1.3, measured below.

**Linear types do not satisfy this capability and must not be cited for
it.** A linear type prevents a resource being consumed twice, which is a
use-after-move property, and a linear value can be printed, written to disk
or put on a socket exactly once with no diagnostic. Effect types govern what
a function is permitted to DO, not what data can be disclosed. The same
category error retired ETSI 5.4-2.

---

## TLS 1.3 is interop-proven; DTLS is loopback only

CRA `Annex-I-1(e)`, ETSI `5.3-6` and IEC 62443 `FR4` rest on this
measurement. Measured 2026-07-28 with `build/tls-interop-test.ps1`, which
drives the Codex TLS 1.3 server against Python/OpenSSL rather than against
another Codex endpoint:

```
TLSv1.3 / TLS_AES_128_GCM_SHA256 against OpenSSL 3.0.13
server certificate accepted: 377 bytes, chain walked to the fixture CA
application data echoed: 'GET'
control refused an unrelated CA: CERTIFICATE_VERIFY_FAILED
```

Both directions fired: the positive case completes a handshake with a
foreign peer, and the negative control refuses an unrelated anchor.

**DTLS is not covered by that measurement.** `dtls-loopback`,
`dtls-auth-loopback` and `dtls-app-loopback` are Codex endpoints talking to
each other, which cannot distinguish a correct implementation from two
consistently wrong ones. No DTLS foreign-peer oracle exists on this box. The
rows say loopback-tested only.

---

## The limits of this mapping

The baseline's six capability titles were read from
`docs/Reference/IoT/Compliance/NISTIR-8259-Summary.md` and NIST's published
titles, **not from the 8259A PDF**, which is not in the tree. The
sub-elements NIST lists under each capability are not walked one by one,
therefore "Satisfied" means the stated mechanism was fired and holds, not
that every sub-element of that capability is covered.
`ETSI-303645-Mapping.md` records the same limit at its head.

**Every row in the module carrying the `ByConstruction` class has been
fired** (13 rows, 2026-07-28). That class asserts the language makes
violation inexpressible, which is the strongest claim available and the one
worth attacking first. What the firing established, stated row by row in the
module's own mechanism text: an undeclared effect is `CDX2031`, an
out-of-range literal `CDX2050`, an unproven range `CDX2051`, and a heap
allocation in a punctual path `CDX6002`, all **errors that halt codegen and
emit no binary**. Dead-code elimination was measured with a positive
control. `CDX6011` (WCET budget exceeded) is only a **warning**, which is
why FR6 carries the weaker claim and FR7 does not. `CDX4010` and `CDX4001`
must not be cited: neither produces the behaviour once claimed for it.

**Not audited:** the roughly 45 rows classified `Mechanism`, `Deployment`
or `Organizational`. Those make weaker claims, therefore a wrong one costs
less, but no row among them has been fired.
