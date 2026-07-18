# Identity & Authentication

> **Filed to Done 2026-07-15 (val):** the core shipped (Ed25519 identity, IdentityManager, trust lattice); the pending features — USB hotplug / stick-removal / configurable timeout — and the runtime test-coverage gap are tracked in BACKLOG 7.14. Moved out of Active to keep init light; reopen if those are picked up.

**Date**: 2026-05-01
**Status**: Core shipped -- Ed25519 identity, IdentityManager, trust lattice live. Pending: USB hotplug / stick-removal / configurable timeout.
**Depends on**: Crypto primitives (done CL 541), Trust lattice
foreword (done CL 544), Agent protocol (done CL 543)
**Unblocks**: Trust lattice runtime, Verifier Phase 2 (author check),
Agent protocol runtime, first-boot sequence

---

## The Core Claim

Identity in Codex is a 32-byte Ed25519 public key. Nothing else.

Not a username. Not a biometric. Not a device serial number. An agent
IS its public key. Authentication IS signature verification.
Authorization IS capability checking against the trust lattice.

This is the simplest possible identity model: if you can sign with the
private key, you are the identity. Everything else — biometrics, PINs,
hardware tokens — is a mechanism for protecting the private key, not a
mechanism for defining identity.

---

## Where Identity Is Used

| System | Identity Field | How It's Used |
|--------|---------------|---------------|
| CDX binary | `author_key` (0x28) | Verifier checks author trust score |
| Agent protocol | `from` on all 7 message types | Receiver verifies signature |
| Trust lattice | Node key | Vouches point from one key to another |
| Capability grants | Grantee key | Capabilities are granted to a key |
| Fact store | Author key | Facts are signed by their author |

---

## Key Lifecycle

### Generation

An Ed25519 keypair is generated from 32 bytes of entropy:

1. Read 32 bytes from the hardware entropy source (RDRAND on x86-64,
   or from an external seed if RDRAND is unavailable).
2. Clamp the private key per the Ed25519 spec (clear low 3 bits, set
   bit 254, clear bit 255).
3. Derive the public key by scalar multiplication of the base point.
4. The public key IS the identity. The private key is stored securely.

The Ed25519 foreword (`foreword/Ed25519.codex`, CL 541) implements
key generation, signing, and verification. The primitives are
constant-time and run on bare metal.

**Post-quantum note (IRISA, 2026-06-23):** The CAPSULE team (IRISA
D1) researches post-quantum lattice-based cryptography. Ed25519 is
not quantum-resistant. A future upgrade path should support hybrid
signatures (Ed25519 + a lattice-based scheme) so that the trust
lattice can transition without invalidating existing vouches. The
RotationFact mechanism (below) supports this — a key rotation from
an Ed25519 identity to a hybrid identity preserves trust chains.
CAPSULE also studies side-channel attacks on crypto implementations;
their work could verify that our Ed25519 emitted code is
constant-time at the machine instruction level, not just at the
source level. See `docs/Reference/IRISA_Research_Harvest.md`.

### Storage

The private key must never exist on disk in plaintext. The concrete
storage architecture has three layers:

#### Layer 1: Cold Store (DiskFacts on USB stick)

The USB stick is the root of trust. At first boot, the encrypted
private key is written to the stick's DiskFacts partition as an
`IdentityFact`:

```
IdentityFact = record {
  id-version        : Integer       -- 1
  id-salt           : List Integer  -- 16 bytes, RDRAND, generated once
  id-iv             : List Integer  -- 16 bytes, RDRAND, fresh per encryption
  id-encrypted-key  : List Integer  -- 48 bytes (32-byte key + PKCS7 padding)
  id-public-key     : List Integer  -- 32 bytes
  id-created-ticks  : Integer
}
```

Encryption scheme:
- `passphrase` -> `HKDF-SHA256(passphrase, salt, info="codex-identity")` -> 256-bit derived key
- `private-key` -> `AES-256-CBC(derived-key, iv, PKCS7-pad(private-key))` -> encrypted blob

The stick carries the canonical copy. DiskFacts provides crash
safety (dual superblock, SHA-256 per fact) and pre-OS accessibility
(raw sector I/O, no filesystem engine required).

#### Layer 2: Installed Copy (DiskFacts on system drive)

When Codex is installed to a drive, the encrypted `IdentityFact` is
copied from the stick to the system drive's DiskFacts partition. Same
encryption, same passphrase, byte-identical blob. The system drive
now has its own copy that survives stick removal.

Two copies, same passphrase, same ciphertext. The stick is the
master; the system drive is the replica. On passphrase change, both
copies are re-encrypted with a fresh IV.

#### Layer 3: Runtime (pinned kernel memory)

After unlock (passphrase entry), the plaintext private key is held
in a pinned memory region inside the kernel address space:

- The region is allocated at a fixed address in the kernel's memory map
- It is never mapped into any user-mode process's page tables
- The `sign` syscall reads from this region — user processes invoke
  signing but never see the key bytes
- On timeout (configurable, default 30 minutes of inactivity), the
  region is zeroed and the system drops to locked state
- On shutdown, the region is zeroed
- On `lock` command from the console, the region is zeroed immediately

The RuntimeDb (apps/data/RuntimeDb.codex) receives the **public key**
and identity metadata (fingerprint, creation timestamp) in
`sys_config` at boot. It never receives the private key. All runtime
subsystems — trust lattice, capability grants, agent protocol, fact
store — reference identity by public key. Signing is a kernel
operation.

#### Storage Decision Matrix

| Scenario | Key source | Passphrase prompt |
|----------|-----------|-------------------|
| First boot (fresh stick) | Generate + encrypt to stick | Create passphrase |
| Boot with stick | Decrypt from stick | Enter passphrase |
| Boot without stick (installed) | Decrypt from system drive | Enter passphrase |
| Runtime signing | Read from pinned kernel memory | None (already unlocked) |
| Timeout / lock | Key zeroed | Re-enter passphrase to unlock |
| Passphrase change | Re-encrypt both copies, fresh IV | Enter old + new passphrase |

#### Future: Hardware Security Module

TPM 2.0 support is deferred but the architecture accommodates it:
the TPM would replace the passphrase-derived AES key with a
hardware-sealed key. The `IdentityFact` format carries a version
field — version 1 is passphrase-encrypted, version 2 would be
TPM-sealed. The unlock flow checks the version and dispatches to
the appropriate decryption path. The pinned-memory runtime layer
is unchanged — the key arrives in RAM the same way regardless of
how it was decrypted.

### Rotation

A key is replaced by publishing a **rotation fact** in the trust
lattice:

```
RotationFact = record {
  old-key    : Bytes32
  new-key    : Bytes32
  timestamp  : Integer
  signature  : Bytes64     -- signed by old-key
}
```

The rotation fact is signed by the OLD private key (proving the holder
of the old key authorized the rotation). After publication, the trust
lattice updates the node: the old key is marked `retired`, the new key
inherits the old key's trust score and vouch relationships.

A key that is compromised (private key leaked) is revoked by
publishing a **revocation fact** signed by any key that vouched for
the compromised key. This is a social recovery mechanism — your
vouchers can revoke you.

### Destruction

When a device is decommissioned, the private key is wiped. If the key
was in an HSM, the HSM's secure erase is invoked. If the key was on
disk, the encrypted file is overwritten and the storage reclaimed.

The public key remains in the trust lattice (with a `retired` or
`revoked` marker) so that historical signatures can still be verified.

---

## First-Boot Ceremony

On a fresh USB stick (no IdentityFact in DiskFacts), the kernel
enters the first-boot ceremony. See also `DriveManager.md` for the
full boot flow from stick power-on to UEFI console.

### Step 1: Kernel boots from stick

UEFI firmware reads GPT on the USB stick, loads `EFI/BOOT/BOOTX64.EFI`
from the ESP. The PE32+ stub enters the Codex kernel. Hardware init
(Stage 0) runs: long mode, PIC, IDT, serial. Block driver init
(Stage 1) detects the stick's ATA device.

### Step 2: Check DiskFacts on stick

The kernel reads the DiskFacts superblock from the stick. If no
superblock exists or no IdentityFact is found, the stick is fresh.
Branch to the ceremony.

### Step 3: Generate identity

The kernel generates an Ed25519 keypair:
1. Read 32 bytes from RDRAND
2. Mix in user-provided entropy: prompt the user to type a random
   sentence (keyboard timing provides additional entropy bits)
3. SHA-256(RDRAND bytes || keyboard timing bytes) -> 32-byte seed
4. Clamp seed per Ed25519 spec, derive public key
5. Display fingerprint: "Your identity: [hex fingerprint]"

### Step 4: Encrypt and store

1. Prompt: "Create a passphrase to protect your identity:"
2. Prompt: "Confirm passphrase:"
3. Generate 16-byte salt from RDRAND
4. Generate 16-byte IV from RDRAND
5. Derive encryption key: HKDF-SHA256(passphrase, salt, "codex-identity")
6. Encrypt: AES-256-CBC(derived-key, iv, PKCS7-pad(private-key))
7. Write IdentityFact to stick's DiskFacts
8. Write BootConfigFact (boot-mode = console, no boot drive)
9. Checkpoint superblock

The plaintext private key remains in the pinned kernel memory region.
The passphrase is zeroed from memory immediately after key derivation.

### Step 5: Bootstrap trust lattice

The stick's identity becomes the root of the local trust lattice.
The kernel writes a self-vouch fact (the identity vouches for itself
at trust score 1.0). This is the seed — all future trust relationships
grow from this root.

### Step 6: Enter UEFI console

The kernel drops into the UEFI console (DevConsoleBoot). From here
the user can:
- Enter the Drive Manager to install Codex to a drive
- Stay in the console for development
- Configure upstream agent connectivity

### Subsequent boots

On a stick that already has an IdentityFact:
1. Read IdentityFact from DiskFacts
2. Prompt: "Passphrase:"
3. Derive key, decrypt private key
4. Verify: derive public key from decrypted private key, compare
   against stored public key. Mismatch = wrong passphrase, retry.
5. Load BootConfigFact. If a boot drive is configured, offer to
   boot it (5-second timeout). Otherwise, enter console.

### Import existing identity

For users who already have a Codex identity on another stick:
1. Insert both sticks (if hardware supports two USB ports)
2. From the UEFI console: "Import identity from another stick"
3. Select source stick, enter its passphrase
4. Decrypt private key from source, re-encrypt with new passphrase
   (or same passphrase), write to destination stick
5. The identity (public key) is the same — the user's trust lattice
   relationships carry over. Only the physical storage moves.

### Step 7: Optional — connect to a peer

If the device has network access, it can connect to a trusted peer
(another Codex.OS device or a repository server) and synchronize
trust lattice state. The peer's identity must be provided by the user
(e.g., by scanning a QR code or entering a public key fingerprint).

---

## Authentication Flows

### Local (same device)

A process authenticates to the kernel by virtue of its process table
entry. The kernel knows which identity launched each process (the
identity is stored in the process table at process creation). No
signature verification is needed for local authentication — the
kernel mediates.

### Remote (network)

A remote agent authenticates via the Trust Network handshake:

1. Agent A sends a `Propose` message signed with its private key.
2. Agent B verifies the signature using A's claimed public key.
3. Agent B looks up A's trust score in the local trust lattice.
4. If the score exceeds the threshold for the requested capability,
   Agent B sends a `Grant`.
5. If the score is too low, Agent B sends `Deny` with an explanation.

Signature verification happens BEFORE any state is allocated (no
SYN-flood equivalent). An invalid signature is dropped silently.

### Offline

A device with no network access authenticates locally only. The
local trust lattice (populated at first-boot and during prior network
sessions) is sufficient. Capabilities granted to local identities work
without network. Capabilities that require remote verification (e.g.,
checking a revocation list) fail-closed: access is denied until
connectivity is restored.

---

## The Identity Effect

```codex
effect Identity where
  whoami       : AgentIdentity
  sign         : Bytes -> Signature
  verify       : AgentIdentity -> Bytes -> Signature -> Boolean
  trust-score  : AgentIdentity -> Integer
```

`whoami` returns the current process's identity. `sign` uses the
current process's private key. `verify` checks a signature against
any public key. `trust-score` queries the local trust lattice.

The `Identity` effect requires the `Identity` capability, which is
granted at process creation based on the launching identity's
permissions.

---

## Connection to Other Designs

| Design | How Identity Connects |
|--------|----------------------|
| **DriveManager.md** | First-boot ceremony lives here. Install-to-drive copies the encrypted IdentityFact. Drive Manager requires `IdentityAdmin` capability for key operations |
| **Kernel.md** | Boot Stage 5 is identity establishment. The pinned key region is a kernel memory map entry. The `sign` syscall (new) reads from the pinned region |
| **RuntimeDb** (`apps/data/RuntimeDb.codex`) | Receives public key + fingerprint in `sys_config` at boot. Capability grants, policy evaluation, and audit trails reference identity by public key. Never holds private key |
| **SystemDb** (`apps/data/SystemDb.codex`) | Logs identity-related events (unlock, lock, timeout). Firewall rules reference agent identity for trust decisions |
| **DiskFacts** (`codex/os/kernel/DiskFacts.codex`) | Stores the encrypted IdentityFact on both stick and system drive. Provides crash-safe, pre-OS persistence via dual superblock |
| **CryptoPrimitives.md** | Ed25519 for key generation/signing, AES-256-CBC for key encryption at rest, HKDF-SHA256 for passphrase key derivation |
| **TrustAndRuntime.md** | Identity = public key is the foundation of the trust lattice. Every message signature, every vouch, every capability grant references an Ed25519 public key |

---

## What NOT to Build

- **Usernames or passwords**: Identity is a keypair. Authentication
  is signature verification. There are no passwords in Codex.
- **Certificate authorities**: The trust lattice IS the certificate
  infrastructure. There is no external CA.
- **OAuth, SAML, OpenID**: These are delegation protocols for systems
  without cryptographic identity. Codex has cryptographic identity.
- **Biometric storage**: The OS does not store biometrics. A biometric
  sensor provides entropy for key derivation. The biometric data is
  processed locally and immediately discarded.
- **Multi-factor authentication**: The private key is the single
  factor. Protecting the private key (HSM, passphrase, biometric) is
  a storage concern, not an authentication concern.

---

## Open Questions

1. **RDRAND trust** — RESOLVED: Yes, mix in user entropy. The
   first-boot ceremony prompts the user to type a random sentence.
   SHA-256(RDRAND || keyboard timing) provides the seed. This
   defends against a compromised CPU RNG while still using RDRAND
   as the primary source when it's trustworthy.

2. **Key escrow**: If the user loses their stick and the system drive,
   they lose their identity. Social recovery (vouchers can authorize
   a new key via RotationFact) helps but requires at least one vouch
   relationship. The first-boot ceremony should encourage establishing
   a recovery vouch, but cannot require it (the first device has no
   peers yet). A second stick with a copy of the encrypted key is the
   simplest backup — the import-identity flow supports this.

3. **Multiple identities per person**: A person may want separate
   identities for work and personal use. The system supports this
   (each is a separate keypair on a separate stick, or multiple
   IdentityFacts on the same stick with a selection menu at boot).
   The UX for identity switching is deferred to V2.

4. **Anonymous agents**: Some use cases (whistleblowing, privacy)
   require agents that cannot be linked to a person. Codex's model
   (identity = public key) supports pseudonymity naturally, but the
   trust lattice makes de-anonymization possible via vouch graph
   analysis. This is acceptable — anonymity is a spectrum, and
   pseudonymous keys with no vouch history are unlinkable until the
   user chooses to establish trust relationships.

5. **Hardware binding** — RESOLVED: No. The identity lives on the
   USB stick, not the hardware. The stick is portable — plug it into
   any machine and your identity travels with you. Device-specific
   entropy (CPU serial) is mixed into key generation for additional
   randomness, but the key is not derived from it. A key generated
   on machine A works on machine B.

6. **Pinned memory region size and location**: The kernel needs a
   fixed address for the 32-byte private key that is excluded from
   all user-mode page tables. The current memory map
   (X86_64Boot.codex) has reserved regions — one needs to be
   designated as the identity key slot. This is a small change to
   the boot codegen.

7. **Timeout policy**: The default lock timeout (30 minutes) is a
   compile-time constant in V1. In V2, it should be configurable
   via policy prose ("lock the system after 10 minutes of
   inactivity") and stored in RuntimeDb's sys_config.

8. **Stick removal detection**: If the stick is removed while the
   system is running, should the system lock immediately? This
   requires USB hotplug detection, which is not yet implemented.
   V1 behavior: the system continues running with the key in pinned
   memory. The stick can be safely removed after unlock.
