# Identity & Authentication

**Date**: 2026-05-01
**Status**: Design
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

### Storage

The private key must never leave the device in plaintext. Storage
options, in order of preference:

1. **Hardware security module** (TPM 2.0, secure enclave): The private
   key is generated inside the HSM and never exported. Signing
   operations are delegated to the HSM. This is the strongest option.

2. **Encrypted file**: The private key is encrypted with a
   key-derivation function (e.g., HKDF from a passphrase or biometric
   hash). The encrypted key is stored on disk. Decryption happens in
   memory, and the plaintext key is wiped after signing.

3. **Memory-only**: The private key exists only in RAM during the
   session. Generated at boot, lost at shutdown. Suitable for
   ephemeral agents (test environments, CI runners).

The choice is a deployment decision, not a language decision. The
Identity effect provides the interface; the handler provides the
storage backend.

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

On a new device running Codex.OS for the first time, there is no
identity and no trust lattice. The boot sequence:

### Step 1: Kernel boots

The kernel is a known binary (verified by content hash against a
hardcoded root hash in the boot ROM or UEFI secure boot chain). The
kernel is trusted by construction — it is the Codex compiler's own
output, a verified fixed point.

### Step 2: Generate device keypair

The kernel generates an Ed25519 keypair using hardware entropy. This
is the **device identity** — it represents the hardware, not a person.

### Step 3: User setup

The kernel prompts (via the Shell, Face 3) for the first user to
establish their identity. Options:

- **Generate new**: Create a fresh Ed25519 keypair. The user provides
  a passphrase or biometric to protect the private key.
- **Import existing**: The user provides their public key and proves
  possession of the private key (by signing a challenge). This is for
  users who already have a Codex identity on another device.

### Step 4: Bootstrap trust lattice

The device identity vouches for the user identity (trust score = 1.0,
the maximum). The user identity becomes the root of the local trust
lattice. All subsequent identity and capability decisions flow from
this root.

### Step 5: Optional — connect to a peer

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

1. **RDRAND trust**: RDRAND is the only hardware entropy source on
   x86-64 bare metal. If the CPU's RNG is compromised, all generated
   keys are predictable. Should the first-boot ceremony mix in
   user-provided entropy (keyboard timing, mouse movement)?

2. **Key escrow**: If the user loses their device and their private
   key, they lose their identity. Social recovery (vouchers can
   authorize a new key) helps but requires at least one vouch
   relationship. Should the first-boot ceremony encourage the user to
   establish a vouch with a recovery contact?

3. **Multiple identities per person**: A person may want separate
   identities for work and personal use. The system supports this
   (each is a separate keypair) but the UX for managing multiple
   identities is undesigned.

4. **Anonymous agents**: Some use cases (whistleblowing, privacy)
   require agents that cannot be linked to a person. Codex's model
   (identity = public key) supports pseudonymity naturally, but the
   trust lattice makes de-anonymization possible via vouch graph
   analysis. Is this acceptable?

5. **Hardware binding**: Should the device keypair be derived from
   hardware-specific entropy (e.g., CPU serial number + RDRAND) to
   prevent key cloning between devices? Or should a device key be
   freely movable?
