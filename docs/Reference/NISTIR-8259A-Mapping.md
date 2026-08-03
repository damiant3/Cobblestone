# NISTIR 8259A Compliance Mapping to Codex

Maps the NISTIR 8259A Core Device Cybersecurity Capability Baseline to
Codex language and platform features.

**Legend -- Gap column:**
- **Satisfied** -- Codex provides the mechanism, and it was fired
- **Partial** -- Codex provides part; the rest is manufacturer or absent
- **Manufacturer** -- process obligation outside the compiler/runtime
- **Withdrawn** -- the row claimed a mechanism that does not exist or does
  not do what the capability asks

**A row here is a claim and nothing re-reads it** -- except, now, one thing
that does. `codex/test/compliance-report` validates every row in
`codex/foreword/core/ComplianceEvidence.codex`: unknown artifact, unknown
regulation, empty section or title, or a mechanism under forty characters
is a fault, and the test asserts zero faults across all 61 rows. It cannot
check that a claim is TRUE -- no test can -- but it can no longer be green
while a row is a placeholder, which is what it was before.

Audited 2026-07-28 against main 11421. All six capabilities are now
claimed, measured, and stated at the strength the measurement supports.

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

## What the first audit found, and what was wrong with it

The module claimed **five** capabilities. The baseline has six. The missing
one was capability 4, Logical Access to Interfaces -- and the numbering hid
it: the old rows labelled Software Update as `8259A-4` and State Awareness
as `8259A-5`, so every section id from 4 onward pointed at the wrong
capability in the standard. The list was not merely one row short, it was
mislabelled from the fourth row down. Both are fixed; the ids now follow
the baseline's own order.

Two of the five did not survive as written.

**8259A-1 claimed "device serial in LwM2M Object 3".** Object 3 in
`codex/foreword/encode/Lwm2m.codex` is a block of resource-ID constants and
nothing else: `lwm2m-res-serial : Integer = 2` is the OMA registry number
naming where a serial would go. Nothing populates it, and the one serial
field in the tree, `sbc-boot-drive-serial`, is initialised to `""`. The
clause is gone. The logical identifier is real -- `IdentityManager` generates
an Ed25519 keypair and stores it encrypted, covered by
`codex/test/idm-key-tests` -- so the capability is **Partial** and says so.
The provisioning path is also unproven end to end:
`codex/test/apps/first-boot-ceremony` carries a `.skip` reading *"stub: test
body not yet written. The body prints `first-boot-ceremony:ok` and runs no
ceremony."*

**8259A-3 claimed linear types as data protection**, classified
ByConstruction. Linear types prevent a resource being consumed twice; that
is a use-after-move property, and a linear value can be printed, written to
disk or put on a socket exactly once with no diagnostic. Effect types govern
what a function may **do**, not what data may be disclosed. Same category
error as the ETSI 5.4-2 withdrawal.

**It has been re-earned on the mechanism that actually does the work.** At
rest: AES-256-GCM (SP 800-38D) and ChaCha20-Poly1305 (RFC 8439), known-answer
tested against the published vectors. In transit: TLS 1.3, and that is
measured rather than asserted -- see below.

**8259A-6 (state awareness) claimed "all state changes ... recorded".** The
surfaces are real (`CapabilityAudit`, `Forensics`, `NotificationLog`, the
content-addressed fact store) but nothing establishes the universal: there
is no check that a state change cannot occur without a fact being written,
and these are separate mechanisms rather than one enforced chokepoint. The
row now says Partial and names the limit.

---

## The three rows that understated what ships

CRA `Annex-I-1(e)`, ETSI `5.3-6` and IEC 62443 `FR4` each asserted that **no
TLS/DTLS transport ships.** That was true when written and is not true now,
which makes it the rarer failure: a claim that decayed in the *under*-claiming
direction while the audit was looking for the opposite.

Measured 2026-07-28 with `build/tls-interop-test.ps1`, which drives our TLS
1.3 server against Python/OpenSSL rather than against ourselves:

```
TLSv1.3 / TLS_AES_128_GCM_SHA256 against OpenSSL 3.0.13
server certificate accepted: 377 bytes, chain walked to the fixture CA
application data echoed: 'GET'
control refused an unrelated CA: CERTIFICATE_VERIFY_FAILED
```

Both directions fired: the positive case completes a handshake with a
foreign peer, and the negative control refuses an unrelated anchor. All
three rows now say TLS 1.3 ships and is interop-proven.

**DTLS is not covered by that measurement and is no longer lumped in with
it.** `dtls-loopback`, `dtls-auth-loopback` and `dtls-app-loopback` are our
endpoints talking to each other, which cannot distinguish a correct
implementation from two consistently wrong ones. No DTLS foreign-peer oracle
exists on this box. The rows say loopback-tested only.

---

## What this audit did not do

It read the baseline's six capability titles from
`docs/PM/IoT/Compliance/NISTIR-8259-Summary.md` and NIST's published titles,
**not from the 8259A PDF**, which is not in the tree. The sub-elements NIST
lists under each capability were not walked one by one, so "Satisfied" means
the stated mechanism was fired and holds, not that every sub-element of that
capability is covered. `ETSI-303645-Mapping.md` records the same limit at
its head.

The other three mappings (CRA, ETSI, IEC 62443) have had their three TLS
rows re-measured, and **every row in the module carrying the
`ByConstruction` class has now been fired** -- that class asserts the
language makes violation inexpressible, which is the strongest claim
available and the one worth attacking first. Twenty rows carried it at the
start of 2026-07-28; thirteen do now.

What the firing established, and it is in the module's own mechanism text
row by row: an undeclared effect is `CDX2031`, an out-of-range literal
`CDX2050`, an unproven range `CDX2051`, and a heap allocation in a punctual
path `CDX6002` -- all **errors that halt codegen and emit no binary**.
Dead-code elimination was measured with a positive control. Against that,
`CDX6011` (WCET budget exceeded) is only a **warning**, which is why FR6
was corrected and FR7 was not, and `CDX4010`/`CDX4001` turned out to be
cited for behaviour they do not produce.

**Still not audited:** the roughly 45 rows classified `Mechanism`,
`Deployment` or `Organizational`. Those make weaker claims, so a wrong one
costs less, but none of them has been fired.
