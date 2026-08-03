# IoT Protocol Stack: CoAP, MQTT, LwM2M

**Created**: 2026-06-12 (reek)
**Updated**: 2026-07-14 (reek) -- peer auth shipped; status corrected.
**Status**: The DTLS 1.3 structural build (D0-D3) is **done**, and
**peer authentication shipped 2026-07-13** (X.509 with Ed25519 certs,
RFC 8410): two real endpoints complete an *authenticated* handshake in
the battery, and an active man-in-the-middle who flips a `key_share` byte
is defeated. **It is still not a secure channel**, and the reasons are
now narrow and specific: no application traffic keys (the endpoint
derives handshake secrets only), no fragmentation reassembly, and the
handshake flights travel as DTLSPlaintext. **So `coaps://` still does not
exist and ETSI 5.5 / CRA 1(c) remain transport-gated**.
Also still missing across the wider stack: **TCP retransmit**, and the
CoAP/MQTT/LwM2M binding layers in `codex/os/net`.

**2026-07-13: Open Question 1 is now answered, the answer moved the
plan, and phase D0 has shipped.** The foreword `Tls` audit came back
worse than a work estimate -- and so did the cipher suite underneath it.
Two of the AEADs our compliance evidence attested to *did not exist* (no
Poly1305 anywhere; GCM was AES-128 only), and the `Tls` chapter ships a
broken key schedule under a correct-looking name. DTLS could not be
built on that.

**D0 is now done.** `Poly1305.codex` and `ChaCha20Poly1305.codex` are
new, `AesGcm` dispatches on key size and does real AES-256-GCM, and all
three are gated on the RFC 8439 / SP 800-38D published vectors rather
than on agreeing with themselves. The evidence table has been
reconciled: the crypto claims are now true, and the *transport* claims
it could not keep are disclosed instead of asserted. **D1 (the record
layer) is unblocked and is the next CL.** See **The Crypto Floor:
Audited** and **DTLS: The Build Plan** below.

**Upstream**: `docs/PM/IoT/AGENT-PROMPT.md` deliverable 3,
references in `docs/PM/IoT/Protocols/`

## The Problem

A Codex IoT device must speak the protocols brokers and device-
management servers actually use: CoAP (RFC 7252) for constrained
request/response and firmware block transfer, MQTT v5.0 for
telemetry into AWS IoT Core / Azure IoT Hub / Mosquitto, and LwM2M
v1.2 for the device-lifecycle management the CRA mandates
(bootstrap, registration, firmware update -- Object 5).

The codecs for all of these now exist and are battery-tested. What no
device can yet do is *talk to anything*: there is no code that binds a
codec to a socket, and there is no DTLS, so even a bound endpoint would
be cleartext-only and out of compliance.

## What Exists (codex/foreword/encode)

The pure wire codecs shipped, each with tests in the battery:

| Chapter | Protocol |
|---|---|
| `Coap.codex` | CoAP (RFC 7252) |
| `Mqtt.codex` | MQTT |
| `MqttSn.codex` | MQTT-SN |
| `Lwm2m.codex` | LwM2M |
| `Modbus.codex` | Modbus |
| `Zigbee.codex` | Zigbee |
| `Lorawan.codex` | LoRaWAN |
| `BleAtt.codex` | BLE ATT |

This is more coverage than this document originally scoped -- Modbus,
Zigbee, LoRaWAN, BLE and MQTT-SN were all written, and the "deliberately
absent" list below is now historical rather than descriptive.

## The Three Real Gaps

1. **DTLS: zero implementation.** Not a partial one -- searching the
   tree for `dtls` returns prose in an app page and a TLS test chapter
   name, and nothing else. LwM2M mandates DTLS; `coaps://` requires it;
   the compliance story requires it. This is the largest single hole in
   the IoT stack.
2. **TCP retransmission.** `tcp-step` is a working 10-state machine
   with no RTO timer and no segment queue: an unacked segment is lost
   forever on a lossy link. MQTT keepalive and QoS 1/2 assume a
   reliable stream, so MQTT over the real network is not trustworthy
   until this lands.
3. **No binding layer.** `codex/os/net` has 32 modules and not one of
   them is a `CoapEndpoint`, `MqttConnection`, or `LwM2mClient`. The
   codecs are pure functions nobody calls over a socket. Until the
   Action-list interpreters described under "Layering" below are
   written, the protocol work has no runtime existence.

## The Network Stack Underneath (codex/os/net, 32 modules)

- **UDP**: datagram build/parse, pure, working. CoAP's substrate.
- **TCP**: 10-state machine, functional event/action stepping
  (`tcp-step`), TcpConnection records. **No retransmission** -- an
  unacked segment is lost forever on a lossy link.
- **Framing/TcpTransport**: length-prefixed LE32 message framing
  over TCP (used by plugs and TrustTransport).
- **DNS, DHCP, NTP, HTTP**: working client implementations.
- **TLS: none in os/net.** Cleartext only. (A foreword `Tls`
  chapter exists in the catalog; its actual coverage must be
  audited before anything below leans on it.)
- Architecture pattern: pure protocol codecs + functional state
  machines, I/O confined to a NetIO boundary; NE2K is the only NIC.

## Constraints

1. Codecs are pure and battery-testable. Every packet
   encoder/decoder must run as ordinary samples with `.expected`
   sidecars on the x86 battery -- no network, no VM peer. This is
   how 90% of the protocol code gets tested (Rule: the build is
   the test).
2. Foreword layering: pure codecs may live in the foreword (the
   WebSocket/Smtp precedent in codex.foreword.encode); anything
   touching NetIO lives in codex.os.net.
3. Effect typing: a publish is `[Network]`; protocol state machines
   themselves are pure functions event → (state, actions), in the
   exact style of `tcp-step`.
4. Sequence: CoAP → DTLS → MQTT → LwM2M. CoAP is UDP-based and
   small (4-byte header); LwM2M rides CoAP; MQTT waits for TCP
   hardening.
5. Security is not optional: CRA/ETSI provisions on secure
   communication mean cleartext CoAP/MQTT are bring-up modes only,
   gated out of any compliance-evidence build (see
   `ComplianceEvidence.md`).

## Prerequisite Hardening (before/alongside, in os/net)

| Gap | Why it blocks | Work |
|---|---|---|
| TCP retransmission | MQTT keepalive + QoS assume a reliable stream | RTO timer + segment queue in tcp-step's functional style; TimingWheel foreword chapter drives timers |
| TLS 1.3 client | MQTT to any cloud broker | Audit foreword Tls; complete: X25519 (DiffieHellman chapter) + HKDF + AES-GCM/ChaCha20 exist, so the work is handshake + record layer, not primitives |
| DTLS 1.2/1.3 | CoAP security (coaps://), LwM2M mandates it | Record layer over UDP: retransmitting flights, cookie exchange, epoch/sequence in AEAD nonce; shares handshake core with TLS work |

> **Superseded 2026-07-13.** This section used to end: *"The crypto floor
> is already in place and constant-time (Sha256, Ed25519, AesGcm,
> ChaCha20, Hkdf, DiffieHellman) -- the protocol work composes
> primitives, it does not add any."* That was read as a fact and it is
> not one. The protocol work **does** add primitives. See below.

## The Crypto Floor: Audited

Read against the tree on 2026-07-13, function by function. This is what
is actually there, so that nothing downstream is estimated against a
chapter nobody opened.

### Sound, and safe to build on

| Primitive | Chapter | Verdict |
|---|---|---|
| HKDF | `Hkdf.codex` | **Real RFC 5869.** `hkdf-expand` does proper counter-chained `T(n)` (line 39) -- it is not the fake one in `Tls.codex`. Use *this* one. |
| AES-128-GCM | `AesGcm.codex` | **Real NIST SP 800-38D.** `aesgcm-decrypt` verifies the tag before returning plaintext and yields `Maybe` -- the right shape. |
| AES block / key schedule | `Aes.codex`, `Aes256.codex` | `aes-encrypt-block` is exposed. This is exactly what DTLS 1.3 sequence-number masking needs. |
| SHA-256, HMAC | `Sha256.codex`, `Hmac.codex` | Sound. Transcript hash and HKDF rest on these. |
| X25519, Ed25519 | `DiffieHellman.codex`, `Ed25519.codex` | Sound. Key exchange and device identity. |

### Was missing, shipped in D0 (2026-07-13)

| Primitive | Chapter | Gate |
|---|---|---|
| **Poly1305** | `Poly1305.codex` (new) | RFC 8439 §2.5.2 vector, plus the empty / exactly-one-block / partial-trailing-block seams. 130-bit field arithmetic in five 26-bit limbs; the final reduction is a branch-free mask select, because a comparison there is a timing oracle on the tag. |
| **ChaCha20-Poly1305** | `ChaCha20Poly1305.codex` (new) | RFC 8439 §2.8.2 AEAD vector, byte for byte, plus a ciphertext-tamper and an **AAD-tamper** case -- the second is what proves the additional data is authenticated and not merely carried alongside. |
| **AES-256-GCM** | `AesGcm.codex` (key-size dispatch) | OpenSSL vector at 32-byte key; the 16-byte path is pinned byte-identical as a regression guard. GCM only ever reached AES through `aes-encrypt-block`, so the whole fix was to dispatch `gcm-expand`/`gcm-block` on key length instead of hardcoding the 128-bit expansion. |

`ChaCha20.codex` remains a bare, unauthenticated stream cipher. Reach for
`ChaCha20Poly1305`, never `chacha20-encrypt`, for anything that goes on a
wire.

**Caught by the blast-radius check, worth knowing:** the only consumer
passing a 32-byte key to AES-GCM was `apps/secrets/VaultCrypto`, which
was therefore running a 256-bit key through the 128-bit expansion -- a
malformed, non-standard key schedule. It does not compile (and has no
test at all), so nothing shipping depended on it, but it is now
a recorded gap, and it is a fair warning about what silence in the test
battery buys you.

### Still missing, and load-bearing

| Gap | Reality | Status |
|---|---|---|
| ~~A usable TLS key schedule~~ | **FIXED 2026-07-13.** The RFC 8446 §7.1 ladder now runs over the real foreword HKDF and is gated on the **RFC 8448 published trace** -- early, derived, handshake secret, both traffic secrets, server key and IV all match the IETF's bytes. Three defects died: the fake `tls-hkdf-expand` (deleted), a one-byte zero IKM where the RFC means Hash.length zeros, and every label encoded as **CCE instead of ASCII** (which also had SNI hostnames going out as CCE). **D2 is unblocked.** | §5.8 |
| **The transport itself** | No TLS, no DTLS, no secure channel of any kind. The AEADs can now protect a payload; nothing forces a payload through them. | §5.3 |

`ComplianceEvidence.codex` has been reconciled to match. Its crypto rows
are now true and say what they are tested against; its *transport* rows
(CRA Annex I 1(e), ETSI 5.3-1, ETSI 5.3-6, IEC 62443 FR4) now disclose
that no TLS/DTLS ships, and the two that claimed the toolchain supplies
the channel were downgraded MECHANISM → DEPLOYMENT. The evidence
generator is the one thing in this project that must never lie; it no
longer does.

## DTLS: The Build Plan

**Version: DTLS 1.3 (RFC 9147).** Confirmed rather than reopened. It
shares the TLS 1.3 handshake and key schedule (so §5.8's repair is spent
twice), its AEAD-only suite list makes the downgrade story true by
construction rather than by configuration, and mandatory
sequence-number encryption is a real privacy property on a UDP link.
DTLS 1.2 stays unimplemented unless a design partner's server forces it;
that is a deployment discovery, not a design decision, and the record
layer is where it would land.

**Auth mode is deliberately NOT decided here.** The record layer is
agnostic to it (this is the one claim in the original design that the
audit confirmed), so PSK vs raw-public-key (RFC 7250, the natural fit
for our Ed25519 device identity) vs X.509 can wait for the handshake
phase and for a real interop target. Deciding it now would be guessing.

### Phases

Each phase is a CL and each is testable on the x86 battery as pure
samples -- no network, no VM peer, per the standing constraint.

**D0 -- Repair the floor. SHIPPED 2026-07-13 (fester).** Poly1305,
ChaCha20-Poly1305, and AES-256-GCM, each gated on the published vectors
(RFC 8439 §2.5.2 and §2.8.2; OpenSSL for GCM), and the evidence table
reconciled so its crypto claims are true and its transport claims are
disclosed. The `tls-derive-keys` repair (§5.8) was **deliberately left
out of this CL** -- it is a separate concern from the AEAD floor, D1 does
not need it, and one thing at a time. It is the prerequisite for D2, not
for D1.

**D1 -- The record layer. SHIPPED 2026-07-13 (fester).**
`codex/foreword/encode/Dtls.codex`, pure, with `codex/test/dtls-record.codex`
as its known-answer gate. Byte-identical to an independent implementation
of the spec written over the .NET BCL's crypto, and every failure path
fails closed: wrong sequence-number high bits, tampered ciphertext, and a
sub-16-byte record are all rejected.

Two findings worth keeping, because both are the kind that produce a
record layer which verifies beautifully against itself and interoperates
with nobody:

- **The AAD carries the *plaintext* sequence number.** RFC 9147 §4.2.3:
  the header *prior to record number encryption* is the additional data.
  The AEAD runs first; the sequence number is masked afterwards, using a
  mask drawn from the AEAD's own ciphertext. Mask-then-AEAD is the
  intuitive order and it is wrong.
- **The nonce excludes the epoch** (§4, contra DTLS 1.2) -- the epoch is
  keyed, not nonced. Each epoch derives its own key, which is what makes
  nonce reuse structurally impossible rather than merely improbable.

What D1 does not do, stated rather than implied: no Connection ID,
16-bit sequence numbers only, length always present. The parser rejects
the other shapes instead of mis-reading them.

Original scope, for the record:

- Unified record header (RFC 9147 §4): fixed `001` prefix, then the
  `C`/`S`/`L`/`EE` bits -- connection ID present, 8- vs 16-bit sequence
  number, explicit length present, low two bits of epoch.
- Per-record AEAD nonce (§4.2.2): the 64-bit record sequence number,
  network order, left-padded to the IV length and XORed with the static
  `write_iv`. Epoch is keyed, not nonced -- each epoch derives its own
  key, which is what makes nonce reuse structurally impossible.
- **Sequence-number encryption (§4.2.3), mandatory.** Mask from the
  ciphertext under a separate `sn_key` (`HKDF-Expand-Label(secret, "sn",
  "", key_len)`): AES-ECB of the first ciphertext block for the AES
  suites, a ChaCha20 block for the ChaCha suite. This is the one place
  DTLS needs a *raw block encrypt*, and it is why `aes-encrypt-block`
  being exposed matters.
- AAD is the record header exactly as it goes on the wire.
- Anti-replay sliding window (§4.5.1), 64 entries, fixed capacity.

**D2a -- The handshake machine. SHIPPED 2026-07-13 (fester).**
`codex/foreword/encode/DtlsHandshake.codex`. Pure `step : State, Event ->
(State, List Action)`, the `tcp-step` contract, so a datagram handshake --
whose whole difficulty is loss, reordering and duplication -- tests as an
action trace with no network, no peer and no clock
(`codex/test/dtls-handshake.codex`).

- **Flights and retransmission** (§5.8): 1000 ms initial timer, doubling,
  clamped at 60 s. A *new* flight resets the backoff; the backoff belongs
  to a flight, not to the connection. A stale timer after connect is
  ignored, and `failed` is terminal -- a machine an attacker could restart
  with a late packet is not a machine.
- **The stateless cookie** (§5.1): derived by HMAC from the client address
  under a server secret and recomputed on arrival, so the server holds
  **no table** between the two ClientHellos and there is nothing to
  exhaust. An invalid cookie MUST abort with `illegal_parameter` -- it is
  *not* the same as no cookie, and quietly re-issuing a HelloRetryRequest
  would hand an attacker an oracle and a free retry.
- **Anti-amplification** (§5.1): the 3× limit. The cookie stops state
  exhaustion; this stops bandwidth amplification. Different attacks, both
  need closing, and without the second DTLS is a reflector.
- **Our own bound, stated not buried**: the RFC declines to cap the retry
  count. Bare metal cannot -- an unbounded retry into a black hole is a
  hang, and a hang on a device nobody can reach is the failure this
  project exists to prevent. Retries stop at 10 and abort.

**D2b -- The handshake message layer. SHIPPED 2026-07-13 (fester).**
`codex/foreword/encode/DtlsMessage.codex`, gated by
`codex/test/dtls-message.codex`.

**The transcript rule is the whole reason this chapter exists.** RFC 9147
§5.2: the transcript is computed over the original *TLS 1.3-style*
Handshake message, **without** `message_seq`, `fragment_offset` and
`fragment_length`. So the same message has a 12-byte header on the wire
and a 4-byte one in the hash -- two different byte strings. An
implementation that hashes what it sent is perfectly self-consistent,
passes every round-trip test anyone would write, and **cannot complete a
handshake with any other implementation on earth**. The test asserts the
two forms *differ*, so nobody can "simplify" the transcript into reusing
the wire encoding without the diff going red.

Also here: the Finished (RFC 8446 §4.4.4, inherited whole) -- `finished_key
= HKDF-Expand-Label(secret, "finished", "", 32)`, an **empty** context,
with the transcript hash as the HMAC *message* and not the key's context;
transpose them and you get 32 bytes that look just as much like a key.
Gated on RFC 8448's published `finished_key`. And the §7 ACK: a vector of
`(epoch, sequence_number)` record numbers, which is what lets a flight
stop being retransmitted.

Malformed input fails closed. A truncated header and a header whose
`fragment_length` runs past the end of the datagram are both rejected
rather than read out of bounds -- a datagram protocol is handed rubbish
constantly, and trusting a length field is the attack.

**Still open inside D2b, stated rather than implied:**

- **Fragmentation reassembly.** The header carries the fragment fields
  and the parser reads them, but only unfragmented messages are emitted
  and a fragmented peer flight is not reassembled. A handshake message
  larger than the path MTU will fail against a peer that fragments.
- **Peer authentication of any kind.** A Finished that verifies proves
  the peer holds the same handshake secret. It says nothing about *who*
  the peer is. The credential question is still open (below).

**D2c -- The hello bodies. SHIPPED 2026-07-13 (fester).**
`codex/foreword/encode/DtlsHello.codex`, gated by
`codex/test/dtls-hello.codex`. ClientHello (with and without the cookie
extension), ServerHello, HelloRetryRequest, and the parsers that pull the
peer's key share and cookie back out.

**The HelloRetryRequest is not a message type**, and this is the trap that
catches everyone once. DTLS 1.2 had a distinct HelloVerifyRequest; DTLS
1.3 does not. An HRR is a **ServerHello whose random is a fixed magic
value** (RFC 8446 §4.1.3). A receiver that switches on message type will
never see one -- so the cookie exchange silently never happens, and D2a's
entire denial-of-service defence silently never engages. Nothing errors;
it just quietly stops protecting you. The test asserts both directions:
a real ServerHello is *not* taken for an HRR, and an HRR *is*.

Every hop through the extension list is length-prefixed, so every hop is
a chance for the peer to lie; each is bounds-checked against the buffer
we actually hold, not the length the peer claims. Asking a ServerHello
for a cookie, or an HRR for a key share, returns `None` rather than
garbage -- garbage there would become a shared secret.

**Still absent, and this is the loud one: peer authentication.** There is
no Certificate and no CertificateVerify. A handshake built from these
bodies is an **anonymous key agreement** -- it resists a passive
eavesdropper and it does **not** resist an active man-in-the-middle, who
just runs two handshakes and sits in the middle. A connection built on
this is not secure, and must not be described as such.

**D3 -- The endpoint. SHIPPED 2026-07-13 (fester).**
`codex/os/net/DtlsEndpoint.codex`, gated by
`codex/test/apps/dtls-loopback.codex`. Pure -- datagram in, datagrams out,
no socket and no clock -- on the `Udp` chapter's precedent, so a **full
client↔server handshake runs as a battery sample with no network and no
peer process**. The loopback drives the whole exchange: ClientHello →
HelloRetryRequest+cookie → ClientHello+cookie → ServerHello+Finished →
Finished. Both sides verify each other's Finished; both reach `done`;
both agree on the same handshake secrets.

Three things had to be right at once, and the loopback is what forced
each -- a self-consistent implementation would have hidden all three:

- **The server stays stateless across the cookie exchange.** RFC 8446
  §4.2.2: the ClientHello1 hash is carried *inside the cookie* (here, the
  hash followed by an HMAC over address-plus-hash), so the server keeps
  no table and still reconstructs the §4.4.1 `message_hash` transcript on
  the second flight. A cookie that were only a MAC of the address would
  verify fine and then fail at the Finished -- on the cookie path only.
- **The HelloRetryRequest `message_hash` rule.** On a retry, ClientHello1
  is *replaced* in the transcript by `message_hash(254) || len || H(CH1)`
  (RFC 8446 §4.4.1). Miss it and the Finished is right on the no-cookie
  path and wrong on the cookie path -- the bug hides exactly where the DoS
  defence lives.
- **A ClientHello is not a ServerHello.** It carries `cipher_suites` and
  `compression_methods` vectors the server's hello does not, so its
  extension block is at a different offset and its `key_share` is a
  *list*, not a bare entry. The first cut parsed the client's second
  flight with the server-hello offsets and found neither the cookie nor
  the key share -- the handshake stalled after the ServerHello with no
  error anywhere.

A forged-address cookie and a garbage datagram both produce nothing.

**D-auth -- Peer authentication. SHIPPED 2026-07-13 (X.509, Ed25519).** It
is the whole difference between "transport works" and "DTLS works."
Without it, everything above agrees an **anonymous** X25519 key: safe
against a passive eavesdropper, defeated by an active man-in-the-middle
who runs one handshake with each side. Certificate and CertificateVerify
now exist and the MITM is defeated in the battery -- see the A1-A5
sections below. **The anonymous mode still exists beside the
authenticated one, and it is still not secure.** Also still open:
application traffic keys (the endpoint derives handshake secrets only),
fragmentation reassembly, and record-protecting the handshake flights
(they travel as DTLSPlaintext
today). Until D-auth lands, `coaps://` does not exist and nothing built on
this is secure.

The credential model is **decided (Damian, 2026-07-13): X.509.** PSK and
raw public key were rejected as not good enough. D0-D3 are
authentication-agnostic by construction, so this slots in behind them
without reworking any of them.

#### The signature algorithm, and what it costs us

The certificates are **Ed25519** -- RFC 8410 (SPKI algorithm OID
1.3.101.112, parameters absent), carried by the TLS 1.3 signature scheme
`ed25519` (0x0807). Not a compromise for its own sake: **Ed25519 is the
only signature primitive this tree owns.** There is no RSA and no ECDSA,
and `BigInt` has no modular exponentiation -- `bigint-pow` takes a machine
Integer exponent, not the 2048-bit modexp RSA needs.

The consequence is stated here so nobody discovers it later: **an
Ed25519-only stack cannot validate a chain from a commercial CA**, because
essentially every public CA signs with RSA or ECDSA. This authenticates a
fleet whose CA *we* run. AWS IoT and Azure, as they ship today, stay out
of reach until somebody builds RSA (Montgomery modexp + PKCS#1 v1.5 /
RSASSA-PSS) or P-256 ECDSA. That is its own crypto campaign, and
constant-time modexp on bare metal is the sharpest work in it.

#### The vector, and why it is worth the whole build

RFC 8410 §10.2 publishes an example certificate. RFC 8410 §10.1 publishes
an example Ed25519 public key. **The key in §10.1 is the key that signed
the certificate in §10.2** -- confirmed by running our own `ed25519-verify`
over the sliced tbsCertificate.

That makes the RFC a complete, self-contained, *published* end-to-end
vector for X.509 signature verification, and it means we never have to
mint a certificate ourselves and check our parser against our own encoder.
This matters more than it sounds: three real bugs lived in the TLS key
schedule precisely because a round-trip test agrees with itself (§5.8). A
parser that is wrong by one byte -- one off-by-one in a length, one header
included where it should not be -- hands Ed25519 a different message, and
Ed25519 says False. The vector tests the parser, not itself.

#### Phases (each its own CL, each gated on a published vector)

**A1 -- `Asn1.codex`, the DER decoder. SHIPPED 2026-07-13.** Decoder only;
it does not build certificates. Offset-based: nothing is copied until a
caller asks, and `asn1-raw` returns a slice of the *original* bytes,
because a re-encoded TBSCertificate is a different byte string and
verifying a signature against it verifies nothing. DER is BER with the
ambiguity removed, and that property is the entire reason a signature over
a certificate means anything -- so the rules are enforced rather than
assumed, and each of these has cost somebody a CVE:

- indefinite length is refused (legal BER; a decoder that takes it can be
  walked past the end of the structure it thinks it is reading)
- lengths must be **minimally encoded** (`0x81 0x05` and `0x05` both say
  five; accepting both means one certificate has two encodings, so one can
  be signed and the other served)
- a length may not exceed the buffer -- refused, never clamped, because
  clamping hands the caller a short value it believes is complete
- the high-tag-number form is refused outright: X.509 does not need it,
  and a decoder that supports what it does not need has attack surface it
  does not need
- a BIT STRING's unused-bit count must be zero (every key and signature
  X.509 puts in one has zero)
- an INTEGER may not carry non-minimal padding (a serial number that
  decodes two ways is one that can be spoofed past a revocation list)

Gate: `codex/test/asn1-der.codex`. Parses the §10.2 certificate, slices
the TBS, and verifies the real signature with the §10.1 key
(`verify-published=True`); the six negatives above each decode to `None`.

**A2 -- `X509.codex`.** Certificate and TBSCertificate parse; Ed25519
SubjectPublicKeyInfo; issuer and subject retained as **raw DER** (comparing
decoded names is how you get name-confusion bugs); validity; extensions.
The TBS is kept as a byte slice, never re-encoded.

**A3 -- Chain validation.** Signature verify against the issuer's key,
validity window against a caller-supplied `now` (this chapter has no
clock), `basicConstraints` CA + pathlen, key usage, SAN matching, trust
anchor set. Negatives: tampered body, expired, wrong issuer, a leaf
presented as its own CA.

**A4 -- The messages.** `Certificate` (RFC 8446 §4.4.2) and
`CertificateVerify` (§4.4.3), including the signature context -- 64 `0x20`
bytes, the context string, `0x00`, then the transcript hash. The context
string is ASCII, so it goes through `to-unicode`, **not** bare `char-code`
(§5.8's CCE trap, which shipped a whole key schedule in CCE). Plus the
`signature_algorithms` extension, which the ClientHello does not send
today.

**A5 -- Wired into `DtlsEndpoint`. SHIPPED 2026-07-13.** Opt-in and
backward-compatible: `dtls-ep-with-cert` gives a server a chain and signing
key, `dtls-ep-with-anchors` gives a client its trust anchors and a calendar
time; an endpoint built by `dtls-ep-new` alone is still the anonymous
handshake. The server flight grows to ServerHello, Certificate,
CertificateVerify, Finished; the client walks the chain to an anchor,
verifies CertificateVerify over its own transcript, and -- if it was given
anchors -- refuses to finish unless that succeeded. One real subtlety worth
recording: the endpoint's transport `now` is an integer retransmit tick,
but certificate validity is a *calendar* instant, so `ep-cal-now` (14 ASCII
digits, caller-supplied) is a distinct field from the handshake clock;
conflating them was a live type error caught in the build.

**The test that closes §5.9 is the negative one, and it is green.**
`codex/test/apps/dtls-auth-loopback.codex` drives a full authenticated
handshake against a forged Ed25519 chain (`scratchpad/forge`), then an
active man-in-the-middle who flips one byte of the ServerHello key_share:
the client's transcript diverges from what the server's CertificateVerify
signed, the signature fails, and the handshake does **not** complete
(`client-done=False`, `authenticated=False`). A downgrade -- an anonymous
server against an anchored client -- is refused the same way. A passing
handshake proved nothing; this failing one is the proof.

**Still open, not bundled into A5:**
`dtls-ep-random` still derives the ClientHello/ServerHello `Random` from the
endpoint's own public key rather than taking caller entropy. It is not what
RFC 8446 means by `Random` and must not reach real hardware; it was left out
of the capstone deliberately to keep the authentication change clean.

**And the standing limit on the whole X.509 stack:** it is
**Ed25519-only**, because Ed25519 is the only signature primitive the tree
owns. It authenticates a fleet whose CA *we* run. It **cannot validate a
commercial CA's chain** -- public CAs sign RSA or ECDSA, and AWS IoT and
Azure IoT both require one of them. That is a separate crypto campaign
(RSA modexp over a `BigInt` we do not have, or P-256 ECDSA), and it has
not started.

Then, and only then, `CoapEndpoint` composes over it and `coaps://`
becomes real.

### Testing: do not trust the author of the encoder

The standing rule (`assert encoders against an independent reference`)
binds hardest here, because a wrong AEAD **still round-trips against
itself** -- encrypt/decrypt agreeing proves nothing. Every phase gate is
a *known-answer* test against vectors computed outside this tree, not a
self-consistency check:

- D0: RFC 8439 and NIST KAT vectors, transcribed from the standards.
- D1: RFC 9147 record vectors. Where the RFC does not supply one, ground
  truth is computed by an independent implementation and committed
  alongside the test -- **not** derived from our own encoder, and per
  house rule not from a PowerShell bit-loop (AMSI blocks them; use a
  Python file).
- Note for the implementer: the RFC section numbers above are the map,
  not the territory. Build each field against the actual RFC 9147 text
  in front of you. A design doc is not a specification and this one has
  already been wrong once about what was in the tree.

### Memory and time-complexity

Records are bounded and small; payloads stay as buffer slices, never
byte-`List`s (`buf-read-bytes` is the documented 8x blowup). The replay
window and the flight-retransmit queue are fixed-capacity
(`__list-with-capacity`), sized by spec maximums -- no attacker-triggered
allocation, which is the point of doing the cookie exchange before any
state is allocated. The handshake transcript is the one multi-KB
transient and takes a `heap-save`/`heap-restore` bracket. Verdict: low
risk, and unchanged from the original assessment.

## The Design

### Layering (uniform across all three protocols)

The foreword half is built. The `codex.os.net` half is the gap.

```
foreword (pure, battery-tested)              -- BUILT
  CoapCodec      message encode/decode, option delta coding, codes
  CoapMachine    CON/NON/ACK/RST exchange state, retransmit schedule
                 (pure: takes now-ticks, returns next-deadline)
  MqttCodec      15 packet types, varint remaining-length, v5 properties
  MqttMachine    session state, QoS 0/1/2 flows, keepalive schedule
  LwM2mModel     object/instance/resource tree as records; TLV +
                 SenML-CBOR codecs (Cbor chapter exists)
  LwM2mMachine   bootstrap/register/update lifecycles, Object 5 states
codex.os.net (I/O binding)                   -- NONE OF THIS EXISTS
  CoapEndpoint   CoapMachine x UDP (later x DTLS record layer)
  MqttConnection MqttMachine x TCP/TLS; linear connection handle
  LwM2mClient    LwM2mMachine x CoapEndpoint
```

Every `*Machine` follows the tcp-step contract:
`step : State, Event -> (State, List Action)` where Event is
arrival/timer/api-call and Action is send/deliver/set-timer. The
binding layers in os/net are thin interpreters of Action lists.
This is what makes the protocols testable as pure samples: a
`.codex` test feeds a scripted event sequence and asserts the
action trace.

### CoAP specifics

- Full RFC 7252 message layer: Ver/T/TKL/Code/MessageID, token,
  delta-encoded options, 0xFF payload marker. Methods GET/POST/
  PUT/DELETE; response classes 2.xx/4.xx/5.xx.
- Retransmission per spec constants (ACK_TIMEOUT 2 s, factor 1.5,
  MAX_RETRANSMIT 4) with deadlines computed in the pure machine.
- Extensions, in priority order: Block (RFC 7959 -- required by
  OTA), Observe (RFC 7641 -- push telemetry), Resource Directory
  later. Block lands with the base, not after: OTA is the first
  real consumer (`OTAFirmwareUpdate.md`).
- Interop target: Eclipse Californium (host-side, for manual
  interop runs; the gated tests never depend on it).

### MQTT specifics

- v5.0 only (no 3.1.1 compatibility mode in the first cut --
  v5 reason codes and properties are strictly better and all
  major brokers speak it).
- `MqttConnection` is a linear resource: CONNECT acquires,
  DISCONNECT (or error) consumes. Forgetting to disconnect is
  CDX2063 -- the keepalive/will story stays type-honest.
- QoS 2's four-step handshake is a pattern match over the pure
  session state; QoS 1 dup handling via packet-identifier table
  (bounded -- 65535 ids -- fixed-size allocation, no growth).
- Topic alias and session-expiry supported; shared subscriptions
  are server features we merely tolerate in CONNACK properties.

### LwM2M specifics

- Client only. Interfaces: Bootstrap, Registration, Device
  Management, Information Reporting -- each a lifecycle in
  LwM2mMachine.
- Standard objects implemented as records over a generic
  object-tree: Security (0), Server (1), Device (3), Connectivity
  Monitoring (4), **Firmware Update (5)**, Location (6) optional.
- Object 5's state machine (Idle → Downloading → Downloaded →
  Updating → Idle, with Update Result codes) is shared with -- and
  specified in -- `OTAFirmwareUpdate.md`; this stack provides the
  transport (CoAP Block on Package/Package URI) and the observable
  State/Result resources.
- Bootstrap provisions identity: the LwM2M security object carries
  the device's Ed25519-based identity material, bridging to the
  trust lattice rather than importing an X.509 worldview. Where a
  server insists on certificates, that sits behind the DTLS layer
  as deployment configuration, not in the object model.
- Interop targets: Eclipse Leshan (server), Wakaama (reference
  client to compare traces against).

### Security hardening (cross-ref: `ThreatModel.md` §2)

**Protocol downgrade prevention**: MQTT v5.0 only, TLS 1.3 only,
DTLS cipher suites restricted to AEAD (no CBC, no RC4, no MD5).
There is no negotiation path that leads to a weak cipher because
weak ciphers are not implemented. This is a design decision, not
a configuration option -- it cannot be weakened by a deployment
mistake.

**Replay protection**: agent protocol sequence numbers (per-peer,
monotonic, reject duplicates), DTLS/TLS implicit sequence numbers
in AEAD nonce (monotonic, connection-scoped), CoAP message ID
deduplication (§4.5, bounded cache within exchange lifetime).
Security-critical commands (device state mutation, actuation) must
use CON messages over DTLS -- enforced by requiring both
`[Network]` and `[Authenticated]` effects for mutating operations.

**Amplification mitigation (CoAP)**: cleartext CoAP responses to
unknown peers are rate-limited; resource discovery (/.well-known/
core) response size is bounded. Cleartext CoAP is gated out of
compliance-evidence builds (ETSI 5.5 degrades to
DEPLOYMENT(conditional) if present). DTLS-authenticated CoAP
eliminates spoofed-source amplification entirely because the peer
is verified before any response.

**Connection-level DoS**: the trust handshake requires proof-of-
work before session state allocation. MQTT's receive-maximum
property bounds the QoS-2 in-flight window. CoAP NSTART=1 bounds
the in-flight CON window. All protocol buffers are fixed-capacity
pre-allocated -- no attacker-triggered dynamic allocation.

**Nonce management**: TLS/DTLS record layers use implicit sequence
numbers as nonce components -- nonce reuse is impossible within a
session. Application-layer use of `aesgcm-encrypt` outside a
TLS/DTLS context requires explicit nonce-management audit; the
evidence plug should flag such usage.

### What is deliberately absent

Matter/Thread (consumer smart-home; prospectus priority is
IIoT/medical -- reference doc exists for later), and any broker/
server-side MQTT implementation.

(LoRaWAN and MQTT-SN were on this list when the design was written and
were subsequently built as foreword codecs anyway -- see the chapter
table above. They remain unbound, like everything else.)

## Test Strategy Hook

Codecs and machines run in
the standard battery (pure, `.expected`); end-to-end runs use the
codex-vm NE2K NAT path on x86 against host-side Californium/
Mosquitto/Leshan as *manual* interop checks, plus loopback
self-tests (Codex CoAP client against Codex CoAP server in one VM)
as gated runtime tests with no external dependency.

## Memory and Time-Complexity Risk

The wire formats are bounded and small; the codecs must use
buffer primitives, not byte-Lists, for payload bodies
(`buf-read-bytes` 8x blowup is the documented red flag; payloads
stay as buffer slices end to end). Packet-identifier and
retransmit tables are fixed-capacity (`__list-with-capacity`),
sized by spec maximums. The CoAP retransmit queue holds at most
NSTART(=1) in-flight CON exchanges per endpoint -- constant memory.
MQTT QoS-2 state is bounded by the receive-maximum property we
advertise. Verdict: low risk with buffer discipline; the DTLS
handshake transcripts are the one multi-KB transient and get a
heap-save/restore bracket.

## Open Questions

1. ~~**Foreword Tls audit.**~~ **ANSWERED 2026-07-13 (fester)** -- and it
   changed the plan. The chapter is a sketch with a broken key schedule,
   and the cipher suite beneath it is half-fictional. See *The Crypto
   Floor: Audited*. The old text of this question was right about one
   thing: "until read, the TLS line above is a work estimate, not a
   fact." It was read. It was not a fact.
2. ~~**DTLS version.**~~ **ANSWERED: DTLS 1.3 (RFC 9147).** Reasoning in
   *DTLS: The Build Plan*. 1.2 is not implemented and is not planned
   unless a design partner's server forces it.
3. **STILL OPEN -- PSK vs raw-public-key vs certificates for DTLS.** Raw
   public key (RFC 7250) is the natural fit for Ed25519 device identity;
   cloud brokers often want X.509. Deliberately deferred to the D2
   handshake phase: the record layer is agnostic (the audit confirmed
   this), and deciding it now, with no interop target in hand, would be
   guessing. Decide per design partner.
4. **STILL OPEN -- where the Cbor/SenML codec lives** -- extend the
   existing Cbor foreword chapter or a new SenML chapter
   (recommendation: new chapter citing Cbor).
5. ~~**What happens to `ComplianceEvidence.codex` in the meantime?**~~
   **ANSWERED AND DONE 2026-07-13.** Damian's call: fix the code to make
   the claim true where it can be, soften only where it cannot. Both
   happened. The crypto claims were made true (D0). The transport claims
   could not be -- that is the whole DTLS build -- so they were softened to
   disclose the gap, and the two that credited the *toolchain* with
   supplying a secure channel were downgraded MECHANISM → DEPLOYMENT.
   No row now asserts anything the tree does not contain.
