# Trust Network — Replacing the Internet's Trust Architecture

**Date**: 2026-03-31
**Status**: Design
**Depends on**: Agent Contract (RuntimeTrust.txt), Capability Refinement, Crypto Primitives (TBD)
**Prior art**: `docs/Codex.OS/DistributedAgentOS.txt`, `docs/Codex.OS/RuntimeTrust.txt`

---

## The Problem

TCP/IP was designed in 1974 for a network of universities that trusted each
other. Identity, authentication, and authorization were not part of the
design. They have never been part of the design. Every secure thing on the
internet today is a patch:

| Layer | Original design | Patch | Patch quality |
|-------|----------------|-------|---------------|
| Transport | TCP: anyone can connect | TLS: encrypt + authenticate | Good, but CA model is fragile |
| Naming | DNS: unsigned name→address | DNSSEC: signed responses | Complex, ~30% adoption after 20 years |
| Email | SMTP: anyone can claim any sender | SPF/DKIM/DMARC: sender verification | Partial, still spoofable |
| Identity | IP addresses: no identity | OAuth/SAML/JWTs: bolted-on identity | Fragmented, token-based, leak-prone |
| Authorization | None | API keys, cookies, RBAC | Per-application, no universal model |

The patches don't compose. TLS authenticates the server but not the
application. DNSSEC authenticates the name but not the content. OAuth
authenticates the user but not the message. Every layer has its own trust
model, its own failure modes, its own configuration surface. The result is
a stack where security is a property of the configuration, not the protocol.

Codex's model inverts this: **distrust by default, earn trust through the
lattice.** This is not a philosophical preference — it's a protocol design
decision that eliminates entire attack classes by construction.

---

## Design Principles

1. **The trust lattice is the only trust authority.** No CAs, no DNS
   registrars, no OAuth providers, no API key issuers. Identity is a
   public key. Trust is a score in the lattice. Authentication is a
   signature check. Authorization is a capability check.

2. **Every message is a fact.** Signed, content-addressed, append-only.
   The network protocol IS the fact protocol operating in real time.

3. **TCP is a dumb pipe.** We use TCP (or raw IP, or Ethernet, or serial,
   or BLE) as a reliable byte transport. Nothing more. All trust semantics
   live above the transport. The transport is interchangeable.

4. **Stateless until proven real.** No state is allocated for an
   unauthenticated sender. The first message must be self-authenticating.
   If verification fails, drop it — zero cost to the receiver.

5. **Silence is safe.** An agent that receives no messages, or receives
   only invalid messages, defaults to its compiled-in safe set. The
   network cannot force an agent into an unsafe state by sending it
   garbage.

---

## Attack Class Analysis

### SYN Flood

**TCP's problem**: The three-way handshake (SYN → SYN-ACK → ACK) requires
the server to allocate state (a half-open connection entry) on the first
packet. An attacker sends millions of SYNs from spoofed addresses. The
server's connection table fills up. Legitimate connections are refused.

**Why it doesn't apply**: Codex agents don't use TCP's handshake for
trust establishment. The first message in a Codex exchange is a signed
`Propose` (from the Agent Contract). The receiver's first operation is
signature verification — a pure CPU operation with no state allocation.

```
Attacker sends:  Propose { from: X, signature: garbage, ... }
Receiver does:   verify_signature(X.public_key, message, signature)
                 → fails → drop. Zero state allocated.
```

The cost of attack becomes: forge a valid Ed25519 signature. The cost of
defense is: one signature verification per incoming message (~70 microseconds
on modern hardware). This is rate-limitable by CPU budget, not by connection
table size.

For transport-level connection management, SYN cookies (stateless TCP) can
be used at the TCP layer. But the Codex protocol doesn't care — TCP is just
the byte pipe. An attacker who exhausts the TCP layer gets a TCP-level
rejection, not a Codex-level state corruption.

### DNS Poisoning

**DNS's problem**: A DNS resolver asks "what is the address of example.com?"
Any device on the network path can race the real answer with a forged one.
The resolver accepts whichever arrives first. If the forgery wins, all
traffic to "example.com" goes to the attacker. DNSSEC signs responses,
but adoption is ~30% after two decades because the incentive structure
doesn't reward deployment.

**Why it doesn't apply**: Codex doesn't use DNS. Agents identify each
other by public key, not by name. Content is identified by hash, not by
URL. There is no name → address mapping to poison.

```
Traditional:  "Connect to api.example.com" → DNS → 192.168.1.100 (maybe poisoned)
Codex:        "Connect to agent Ed25519:a1b2c3..." → trust lattice → peer address
              First message must be signed by a1b2c3...
              Attacker at that address can't forge the signature.
```

Peer discovery works through the trust lattice: agents you trust vouch
for other agents, with addresses attached to the vouch. An attacker can
forge an address, but cannot forge the vouch signature. If you connect to
a fraudulent address, the first message exchange fails signature verification
and you disconnect — no state leaked, no data sent.

For interoperability with the existing internet (reaching non-Codex
services), a Codex agent uses DNS as a hint, not as truth. The agent
connects, then verifies identity through the Codex protocol. If the
identity doesn't match the trust lattice entry, the connection is dropped
regardless of what DNS said.

### Email Spoofing / Sender Forgery

**SMTP's problem**: The MAIL FROM field is self-declared. Anyone can claim
to be anyone. SPF checks the sending server's IP. DKIM signs the message
body. DMARC combines both. Adoption is incomplete, verification is optional,
and the user sees "From: CEO" regardless of whether DKIM passed.

**Why it doesn't apply**: Every Codex message carries a signature over the
content, signed by the sender's private key. The sender's identity is a
public key in the trust lattice. Verification is not optional — it's the
first thing the receiver does.

```
Codex message:  { from: Ed25519:a1b2c3, body: ..., signature: ... }
Receiver:       verify(a1b2c3, body, signature) → valid or drop
                No "From:" field to spoof. The signature IS the identity.
```

There is no equivalent of "display name spoofing" because identity is not
a display name — it's a cryptographic key. The trust lattice maps keys to
human-readable names, but the mapping is signed and auditable:

```
Trust(key: a1b2c3, display_name: "Alice", vouched_by: [Bob, Carol], score: 0.9)
```

An attacker can create a key and claim any display name. But without vouches
from agents the receiver trusts, the trust score is zero, and the message
is rejected by the trust threshold.

### Man-in-the-Middle

**TCP's problem**: TCP carries no proof of who sent the packet. Any device
on the network path can read and modify traffic. TLS adds encryption and
server authentication (via CA-signed certificates), but the CA model has
single points of failure — a compromised CA can issue fraudulent
certificates for any domain (DigiNotar, 2011; Symantec, 2015-2017).

**Why it doesn't apply**: Codex messages are signed end-to-end by the
sender's private key. A man in the middle can observe ciphertext (if the
transport is encrypted) or signed messages (if the transport is plain),
but cannot:

- **Forge messages**: Can't sign with the sender's key.
- **Modify messages**: Modification invalidates the signature.
- **Replay messages**: Each message has a unique `FactHash` (content-
  addressed). The receiver's fact store deduplicates by hash. Replaying
  a message is a no-op.

For confidentiality (preventing observation), the transport layer can use
TLS or a Codex-native encryption layer. But the integrity guarantee doesn't
depend on the transport — it's in the message signatures. A MITM who strips
TLS still can't forge or modify Codex messages.

The CA model is replaced by the trust lattice. There is no single authority
whose compromise breaks everything. Trust is distributed, scored, and
decays with distance. Compromising one agent's key affects only the trust
relationships that flow through that agent — not the entire network.

### DDoS (Distributed Denial of Service)

**The fundamental problem**: DDoS is about resource exhaustion. An attacker
sends more traffic than the target can process. This is the hardest attack
to eliminate because it's not a protocol flaw — it's a physics problem.
Any system that accepts input from the network can be overwhelmed by
sufficient input.

**How Codex mitigates it** (does not eliminate):

1. **Trust-gated acceptance.** An agent doesn't process messages from
   unknown senders. The first message must carry a valid signature from
   a key that has non-zero trust in the lattice. Messages from zero-trust
   keys are dropped after signature check (cheap) without processing the
   payload (expensive).

2. **Capability-budgeted processing.** The `[Network]` capability has
   scope and quota (from capability refinement). An agent can limit:
   how many connections per second it accepts, how much bandwidth it
   allocates to untrusted peers, and how much CPU it spends on signature
   verification.

3. **Proof-of-work for first contact.** An agent with no existing trust
   relationship must present a proof-of-work token with its first
   `Propose`. This makes first-contact messages expensive for the sender
   and cheap to verify for the receiver.

4. **Peer reputation.** Agents that send malformed messages, fail
   signature checks, or exhibit flood patterns get their trust score
   decremented. Below a threshold, their messages are dropped without
   verification. This is automated immune response — the trust lattice
   learns to reject bad actors.

DDoS at the raw transport level (IP packet floods, amplification attacks)
is a network infrastructure problem, not a Codex protocol problem. Codex.OS
nodes behind hostile networks need the same infrastructure-level defenses
(rate limiting, blackholing, traffic scrubbing) that any internet-connected
system needs. The difference is that Codex's application layer doesn't
amplify the damage — a flood of invalid messages wastes bandwidth but
doesn't corrupt state or trick the agent into unsafe behavior.

### Replay Attacks

**The problem**: An attacker captures a valid signed message and resends
it later. If the message grants a capability, the replay re-grants it.

**Why it doesn't apply**: Every message is a fact with a unique `FactHash`
(content-addressed from its fields, including a timestamp). The receiver's
fact store records every processed fact by hash. A replayed message has the
same hash → already in the store → ignored.

Additionally, capability grants have an `expires` field (lease model).
Even if a replay somehow bypassed deduplication, the grant would carry
the original expiry timestamp, which has already passed.

### Session Hijacking

**The problem**: An attacker steals a session token (cookie, JWT, bearer
token) and uses it to impersonate the victim.

**Why it doesn't apply**: There are no session tokens. Every message is
independently signed. There is no "session" that persists between messages
— each message is self-authenticating. Stealing one message gives you one
message. It doesn't give you the ability to send future messages as that
agent, because you don't have the private key.

---

## The Protocol Stack

```
┌─────────────────────────────────────────┐
│  Agent Protocol (RuntimeTrust.txt)      │  Propose/Grant/Deny/Explain/
│  7 message types, fact-based            │  Narrate/Interrupt/Handoff
├─────────────────────────────────────────┤
│  Trust Layer                            │  Signature verify, trust score
│  Identity = public key                  │  check, replay dedup, rate limit
│  Trust = lattice score                  │
├─────────────────────────────────────────┤
│  Framing Layer                          │  Length-prefixed fact serialization
│  Content-addressed messages             │  Hash, sign, encode, decode
├─────────────────────────────────────────┤
│  Transport (interchangeable)            │  TCP, raw IP, serial, BLE,
│  Dumb byte pipe, no trust semantics     │  Ethernet, USB, carrier pigeon
└─────────────────────────────────────────┘
```

The trust layer is the new thing. It sits between the agent protocol and
the transport. It handles:

- **Signature verification** on every inbound message
- **Trust score lookup** in the local lattice cache
- **Replay deduplication** via fact hash store
- **Rate limiting** per sender key and per trust tier
- **Proof-of-work verification** for first-contact messages
- **Encryption** (optional, for confidentiality — integrity is already
  guaranteed by signatures)

### Framing

Messages on the wire are length-prefixed serialized facts:

```
[4 bytes: message length][N bytes: serialized fact]
```

The serialized fact is the canonical byte encoding of the message record
(deterministic field ordering, no padding, integer encoding TBD). The
`FactHash` is `SHA-256(serialized fact body, excluding the signature field)`.
The signature is `Ed25519(sender_private_key, FactHash)`.

### First Contact

When two agents have no prior trust relationship:

```
Initiator                           Receiver
    │                                   │
    │──── Hello { key, proof_of_work }──→│
    │                                   │ verify proof_of_work
    │                                   │ if valid: allocate state
    │←── Challenge { nonce } ───────────│
    │                                   │
    │──── Prove { sign(nonce) } ────────→│
    │                                   │ verify signature
    │                                   │ trust_score = lattice_lookup(key)
    │←── Accept { trust_score } ────────│ or Reject { reason }
    │                                   │
    │  ... Propose/Grant/Deny ... ──────→│
```

The `Hello` message carries a proof-of-work token (e.g., partial hash
collision of configurable difficulty). This gates state allocation on the
receiver side — no proof, no state. The `Challenge`/`Prove` exchange
verifies that the initiator actually holds the private key (not just
replaying someone else's Hello).

After first contact, the receiver caches the key → trust score mapping.
Subsequent connections from the same key skip proof-of-work and go
straight to signed message exchange.

### Peer Discovery

How does an agent find other agents?

1. **Direct address**: The agent knows a peer's transport address (IP:port,
   serial device, BLE address) from prior interaction or manual
   configuration.

2. **Vouch-based discovery**: Agent A trusts Agent B. Agent B publishes
   a fact: `PeerAddress(agent: C, transport: "tcp:192.168.1.50:9000",
   vouched_by: B, signature: ...)`. Agent A retrieves this fact from B
   and contacts C, verifying C's identity via the trust lattice.

3. **Registry query**: A well-known registry agent (itself identified by
   public key, not DNS) maintains an index of agent keys → transport
   addresses. The registry is a convenience — it accelerates discovery
   but is not a trust authority. The registry can lie about addresses
   (say C is at a different IP), but it can't forge C's identity — the
   first message exchange with the false address fails signature
   verification.

4. **Broadcast/multicast on local network**: For device discovery (phone
   finding a nearby Codex.OS kiosk), agents broadcast signed discovery
   beacons on the local network. The beacon carries the agent's public
   key and capabilities. Receivers verify the signature and check the
   trust lattice before responding.

---

## What We Use From TCP/IP

TCP/IP is not the enemy. It's a reliable byte transport that works on
every network on earth. We use it as such:

| TCP/IP component | How Codex uses it |
|-----------------|-------------------|
| IP | Packet routing (unchanged) |
| TCP | Reliable byte stream (unchanged) |
| UDP | Unreliable datagrams for discovery beacons |
| TLS | Optional encryption layer for confidentiality |
| DNS | Hint for reaching non-Codex services (verified by Codex protocol) |
| HTTP | Not used. The agent protocol replaces REST APIs |

What we **don't use**:
- DNS as a trust authority
- CAs as identity providers
- Cookies/JWTs/API keys for authentication
- OAuth/SAML for authorization
- SMTP/IMAP for messaging
- HTTP as a semantic layer

These are all replaced by the trust lattice + agent protocol + capability
system. One mechanism instead of seven.

---

## Connection to Existing Systems

| Existing component | How Trust Network uses it |
|-------------------|--------------------------|
| **Agent protocol** | The message types ARE the network protocol |
| **Trust lattice** | Identity, authentication, authorization, peer reputation |
| **Capability system** | Network operations are capability-gated effects |
| **Fact store** | Replay deduplication, message persistence, forensic record |
| **Forensics layer** | Every network exchange is a forensic chain |
| **Policy contract** | Network access policies compiled from prose |
| **Crypto primitives** | Ed25519 signatures, SHA-256 hashing (design TBD) |

---

## Implementation Order

| Step | What | Effort | Depends on |
|------|------|--------|------------|
| 1 | Define framing format (length-prefixed, deterministic serialization) | Small | Core types |
| 2 | Signature verification + trust score check (the trust layer) | Medium | Crypto primitives |
| 3 | Replay deduplication (fact hash store) | Small | Step 1 |
| 4 | First-contact handshake (proof-of-work + challenge/prove) | Medium | Steps 1-2 |
| 5 | TCP transport binding (connect, send, receive, disconnect) | Medium | Step 1 + bare metal networking |
| 6 | Rate limiting (per-key, per-trust-tier) | Small | Steps 2, 5 |
| 7 | Peer discovery (vouch-based, registry, local broadcast) | Medium | Steps 4-5 |
| 8 | Peer reputation (trust score decrement on bad behavior) | Small | Steps 2, 6 |

Steps 1-4 can be built and tested in-process with no networking — two
agents in the same process exchanging serialized byte arrays through
function calls. The trust layer works the same whether the bytes come
from TCP or from a test harness. Step 5 is where bare-metal networking
enters the picture.

---

## What This Does NOT Do

- **It does not make the internet secure.** Codex.OS nodes talking to
  non-Codex services still face all the old attacks. The trust layer
  applies only to Codex-to-Codex communication.

- **It does not prevent traffic analysis.** An observer can see that
  two agents are communicating, how often, and how much data flows.
  Preventing this requires an anonymity layer (Tor-like routing), which
  is a separate design problem.

- **It does not solve key management.** Private keys must be stored
  securely. If an agent's key is stolen, the attacker can impersonate
  that agent until the key is revoked in the trust lattice. Key storage,
  rotation, and revocation need their own design.

- **It does not eliminate all DDoS.** Transport-level floods require
  infrastructure-level defenses. The trust layer prevents application-
  level amplification but cannot prevent a raw packet flood.
