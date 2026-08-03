# OTA Firmware Update: Signed CDX over LwM2M Object 5

**Created**: 2026-06-12 (reek)
**Status**: Partially shipped -- OtaUpdate foreword + ota-state-machine /
ota-update tests live; the gates, anti-rollback, and manifest
verification described below are all present. **The `Flash` blocker is
gone (blu, 2026-07-16):** `Foreword chapter Board` now has the linear
bank this design asked for -- `flash-open-bank` → `flash-write-page`* →
`flash-seal-bank`, typed `[Flash, Device.Mmio]`, with `Flash` a
capability in its own right (cap id 15) so the manifest can say which
firmware may rewrite the boot image. See `HardwareAbstractionLayer.md`.

**Steps 1-4 of The Flow are built (blu, 2026-07-16):** `Net chapter
Lwm2mFirmware` is the binding layer. A Write of the Package URI starts a
download; each CoAP response is parsed and its Block2 payload staged
straight into the bank via `flash-write-page`; Gate A hashes the BANK
with `sha256-buf` in constant heap, so what is verified is what landed
rather than what the client believes it sent; Gate B's verdict is
computed by the caller against the real verifier and handed in, because a
firmware client that carried the trust lattice is a client that could be
talked into changing it. `flash-seal-bank` returns the count of page
writes whose busy-wait ran out of fuel, and a staging fault outranks the
hash -- Result 2 (flash failed) and Result 5 (corrupt in transit) send an
operator to different places.

Proven by `codex/test/apps/ota-lwm2m-loopback`: a real signed CDX,
chunked into real CoAP Block2 responses, staged 254/254 bytes, approved
by the real five-phase verifier, activated to the inactive slot -- and
the Adversarial Scenario below (tampered image in transit) refused at
Gate A with Result 5, still booting the old slot.

**Steps 5-7 are built (blu, 2026-07-16):** `Foreword chapter OtaBoot` is
the boot selector. The record lives in one flash page and the candidate
flag is a single aligned word (`flash-write-word`), so the device wakes
having either fully accepted the candidate or never heard of it; the
digest and length are written first and the flag last, in its own cycle.
Every boot counts its attempt and persists it BEFORE hashing anything --
an image that hangs has still spent a chance -- then re-hashes the bank
against the digest Gate A recorded, and boots the primary on any doubt.
Past `boot-attempt-limit` the candidate is abandoned. The commit is
explicit (`boot-commit`): nothing adopts an image on its behalf, because
an image that boots and then wedges is exactly what the window catches.

The Failure Matrix below is no longer a description of a machine that
does not exist. `codex/test/apps/ota-boot-rollback` walks its rows
against real flash, simulating a reboot by re-reading the record rather
than threading state.

The selector re-checks the **hash** and not the signature. The signature
was checked at Gate A and again by Gate B before the bank was ever
marked, and re-parsing a CDX header would put a parser in every boot
path, which is what "the boot selector must be small" forbids. What the
re-check catches is the bank changing after Gate B blessed it.

**Pending, in the order it bites:**

1. **The socket.** CoAP is UDP; codex-vm serves only port 53 and drops
   the rest, so no guest completes a live CoAP exchange. The transport is
   injected into `fw-feed-response`, plus a VM capability.
2. **Gate B costs a List.** `evaluate-load` takes `List Integer`, so the
   caller reads the staged image back out of the bank -- the constraint 5
   memory limit this design names, unfixed. A buffer-taking verifier is
   the fix.
3. **The watchdog window is the caller's.** `boot-commit` exists and
   nothing schedules it. A device that never calls it rolls back by
   attempt count, which is the safe direction, but the timed window step
   7 describes is not implemented here.

One defect found while wiring this and **fixed** (2026-07-16):
`OtaUpdate.gate-a-verify-block` hashed with `sha256` -- eight 32-bit
WORDS -- and compared the result against a byte digest over 32 elements,
so it read a word against a byte eight times and then walked twenty-four
elements off the end of an eight-element list. It could not have passed
for a real digest, and nothing called it, which is the only reason it
never halted. It now converts with `hkdf-words-to-bytes` and
`ota-hash-eq` measures both lists instead of trusting a caller's length.
Pinned by `codex/test/ota-gate-block`, which fails on the old code at the
first check.
**Upstream**: `docs/PM/IoT/AGENT-PROMPT.md` deliverable 5,
`docs/Designs/Active/IoT/ProtocolStack.md` (LwM2M/CoAP Block),
`docs/Designs/Active/IoT/HardwareAbstractionLayer.md` (Flash effect),
`codex/os/verify/` (5-phase verifier)

## The Problem

The CRA mandates secure update mechanisms; ETSI 5.3 and NISTIR
8259A "software update" say the same. The pieces exist -- signed
CDX binaries, a 5-phase verifier, a network stack, LwM2M Object 5
specified in the protocol design -- but no end-to-end flow connects
them, and the existing verifier assumes the whole binary sits in
memory, which a 192 KB-SRAM device cannot do for a multi-hundred-KB
image.

The hard requirement, stated in the agent prompt and restated here
as the design's invariant: **a firmware image that fails signature
or capability verification is rejected before any byte of it
executes.** Not quarantined after boot -- never booted.

## Constraints

1. The unit of update is a signed CDX. No delta/patch formats in
   the first design (deltas mutate an image before verification --
   exactly the wrong place for cleverness; revisit only with a
   verify-after-reconstruct design and a real bandwidth need).
2. Verification uses the existing verifier phases unmodified:
   integrity (magic, SHA-256, Ed25519), author trust, capability
   policy, effect coverage, optional proofs. The OTA flow decides
   *when* each phase runs, not *what* they check.
3. Power loss at any instant must leave the device bootable into
   exactly one of: the old firmware, or the fully-verified new
   firmware. Nothing in between.
4. Every state transition is recorded as a fact -- the update
   history is compliance evidence (`ComplianceEvidence.md`).
5. Memory honesty: download and verification must work in
   streaming fashion on the smallest target (STM32F4, 192 KB SRAM,
   1 MB dual-bank flash).

## The Design

### Roles and trust

- **Publisher**: signs the firmware CDX (author key) and a release
  fact binding (firmware content hash, target model, monotonic
  firmware sequence number).
- **Fleet server**: LwM2M server distributing the release; its
  authority is its place in the device's trust lattice, not its
  network position.
- **Device**: LwM2M client + verifier + boot selector.

A device accepts an image only if the *author* key meets the trust
threshold for the firmware-update capability -- the transport
(server) merely carries bytes. Compromising the fleet server gains
distribution, not execution.

### Storage layout (per board)

| Board | Staging | Mechanism |
|---|---|---|
| STM32F4/H7 | Flash bank B (dual-bank) | Bank swap at boot via option bytes |
| ESP32-C6 | OTA partition (app1) | Espressif bootloader chain-loads; otadata selects |
| Pi 4/5 | Second image file on FAT boot partition | config.txt/tryboot selection |

The HAL grows a `Flash` effect (linear bank handle: open-bank →
write-page* → seal-bank) used only by the updater; the capability
manifest of ordinary firmware need not include it -- firmware that
cannot rewrite flash *by type* is itself an evidence claim.

### The flow

```
1  Server sets Object 5 Package URI (or pushes via Package)
2  Device: state Idle -> Downloading
     CoAP Block transfer (RFC 7959), each block written
     straight to the staging bank via the Flash effect;
     running SHA-256 via sha256-buf (constant-heap streaming)
3  Download complete: state -> Downloaded
     GATE A (on-stream): header sanity, content hash equals
     running hash, Ed25519 signature over content hash, author
     key, machine field matches this device's architecture
4  Server executes Resource 5/0/2 (Update): state -> Updating
     GATE B (full policy): author trust threshold, capability
     manifest vs device policy, effect coverage, proofs if
     flagged, firmware sequence number > current (anti-rollback),
     target-model match from release fact
     All five verifier phases run here, reading from staging
     flash. Any failure: Update Result <- failure code, fact
     recorded, staging bank invalidated, state -> Idle. The old
     firmware never stopped running.
5  Mark staging bank boot-candidate with boot-attempts = 0;
   reboot
6  Boot selector (minimal, in the boot path):
     re-verify GATE A on the candidate bank (cheap: hash + sig);
     increment boot-attempts; if attempts exceeds limit or
     verification fails, boot the old bank (automatic rollback)
7  New firmware runs a self-check (LwM2M re-registration
   succeeds, application health probe passes), then commits:
     candidate -> primary, Update Result <- success(1),
     UpdateFact recorded (old hash, new hash, sequence,
     verdicts, timestamps)
   No commit within the watchdog window -> next reboot returns
   to the old bank (step 6)
```

Gate A on-stream plus Gate B from staging satisfies the invariant
with bounded RAM: at no point does the device hold the image in
memory, and no instruction from the new image executes before
both gates have passed -- the boot selector's re-check closes the
TOCTOU window between Gate B and the bank swap.

### Anti-rollback

The release fact carries a monotonic sequence number per device
model, signed by the publisher. The device persists the highest
*committed* sequence (a fact, surviving updates) and refuses lower
ones at Gate B. This is deliberately a fact-layer rule, not a CDX
header field -- the header stays architecture-truth, the release
fact carries deployment-truth. Rollback to a known-good *older*
image after a failed boot is not a violation: the candidate never
committed, so the sequence never advanced.

### Key compromise and revocation

The verifier consults the trust lattice at Gate B, so rotating or
distrusting a publisher key is a lattice update delivered as a
signed fact from a higher-trust authority. Two known gaps (also
listed in the trust digest) become real requirements here:
FactStore has no supersession/revocation primitive, and lease
revocation has no API. The OTA design needs both; they are scoped
to the trust layer, and until they land, the honest
evidence claim is "key rotation by full trust-store reprovision."

### Update Result codes (Object 5 standard values, mapped)

| Result | Trigger in this design |
|---|---|
| 0 Initial | post-commit reset |
| 1 Success | step 7 commit |
| 2 Not enough flash | staging open/write failure |
| 3 Out of RAM | streaming buffers unavailable |
| 4 Connection lost | CoAP Block exchange exhausted retransmits |
| 5 Integrity check failure | Gate A or boot-selector re-check |
| 6 Unsupported package type | bad magic / wrong machine field |
| 7 Invalid URI | URI parse/connect failure |
| 8 Update failed | Gate B policy/trust/capability/sequence refusal, or health-check rollback (sub-code in UpdateFact) |

## Failure Matrix (power loss / crash at each step)

| Failure point | Outcome |
|---|---|
| During download (2) | Staging partial, never a boot candidate; old firmware untouched; download resumable via Block offsets |
| During Gate B (4) | Candidate flag never set; old firmware boots |
| After flag, before/during reboot (5) | Selector verifies candidate; on pass boots it un-committed (health gate still pending), on fail boots old |
| During first boot of new image (6-7) | boot-attempts exceeds limit -> automatic rollback to old bank |
| After commit (7) | New firmware is primary; old bank becomes next staging area |

## Memory and Time-Complexity Risk

Streaming hash is constant-heap (`sha256-buf`); block writes are
page-buffer sized (the one persistent buffer, flash-page bytes).
Gate B reads header + capability/effect/proof tables from staging
-- KBs, not the image. Ed25519 verify ~1M cycles: sub-second at
MCU clocks, twice per update (Gate A, selector re-check) plus once
at Gate B if re-hashed -- acceptable; re-use Gate A's stored hash
where the staging bank is write-sealed. The boot selector must be
small (it lives in every boot path): hash + signature + bank
select only, no policy engine. Verdict: low risk; the selector's
code-size budget is the one number to watch in implementation.

## Adversarial Scenarios (cross-ref: `ThreatModel.md` §1.5, §4.1)

These are the specific attack scenarios the OTA design must
survive. Each is a testable assertion, exercised by scripted
QEMU tests.

**Tampered image in transit**: attacker modifies bytes during
CoAP Block transfer. The streaming SHA-256 hash diverges from the
header's content hash at Gate A. Result: image rejected, staging
bank invalidated, UpdateFact records the hash mismatch, state
returns to Idle. The old firmware was never at risk.

**Valid signature, escalated capabilities**: attacker (or
compromised publisher) signs a firmware that requests capabilities
the device's policy does not grant (e.g., Network + Flash on an
air-gapped sensor that only grants Gpio + Uart). Gate B's
capability policy check (verifier phase 3) rejects the image.
The capability manifest in the CDX header is inside the signed
content range -- the attacker cannot strip capabilities without
invalidating the signature.

**Replay of older firmware** (rollback attack): attacker captures
a known-vulnerable firmware version and presents it as an update.
The signature is valid (it was legitimately signed). Gate B's
anti-rollback check compares the release fact's sequence number
against the device's committed sequence. The older firmware's
sequence is lower; the update is rejected. The committed sequence
is a fact persisted across updates, not a volatile counter.

**Compromised fleet server**: the fleet server distributes images
but does not sign them -- the publisher (with the author key)
signs. A compromised fleet server can distribute any image, but
the device only accepts images whose author key meets the trust
threshold in the device's own trust lattice. The attacker must
compromise the publisher's signing key to produce an image the
device will accept. The fleet server's network position grants
distribution, not execution authority.

**Power loss during bank swap** (step 5): the bank-candidate
flag is written atomically (single option-byte write on STM32,
single otadata entry on ESP32-C6) before the reboot. If power is
lost during the write, the flag is either fully written (candidate
marked, boot selector will verify it) or not written (no
candidate, old firmware boots). There is no intermediate state
where the device has a partially-marked candidate.

**Fault injection during signature verification**: an attacker
with physical access and a voltage glitcher attempts to skip the
Ed25519 verification in the boot selector (step 6). The boot
selector re-runs Gate A (hash + signature) -- a single glitch must
corrupt both the hash comparison and the signature verification
to succeed. Countermeasure: the boot selector should verify the
signature, then verify it again with a different register
allocation path (see `ThreatModel.md` open question 3). This is
not yet designed but is noted as a Phase B3 hardening item.

## Open Questions

1. **Boot selector placement per board.** STM32 option-byte bank
   swap vs a tiny Codex first-stage; ESP32-C6 sits behind
   Espressif's ROM+bootloader (their secure boot verifies *their*
   format -- our selector runs inside the app slot); Pi tryboot.
   Per-board appendices needed during implementation.
2. **Health-check definition.** Re-registration is necessary;
   what application-level probe is sufficient, and who declares
   it (product code via a `commit-update` API the runtime
   exposes)? Proposal: explicit `ota-commit` builtin; no implicit
   auto-commit.
3. **Sequence-number authority.** Per publisher key or per model
   line with multiple authorized publishers? Affects release-fact
   schema; defer to first design partner's key-management reality.
4. **Resume across reboot during download.** Block offset persists
   as a fact, or restart download? First cut: restart (simpler,
   correct); revisit for cellular-billed links.
