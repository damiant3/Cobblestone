# IoT Protocol Stack: CoAP, MQTT, LwM2M

**Created**: 2026-06-12 (reek)
**Status**: Design — not yet started
**Upstream**: `docs/PM/IoT/AGENT-PROMPT.md` deliverable 3,
references in `docs/PM/IoT/Protocols/`

## The Problem

A Codex IoT device must speak the protocols brokers and device-
management servers actually use: CoAP (RFC 7252) for constrained
request/response and firmware block transfer, MQTT v5.0 for
telemetry into AWS IoT Core / Azure IoT Hub / Mosquitto, and LwM2M
v1.2 for the device-lifecycle management the CRA mandates
(bootstrap, registration, firmware update — Object 5). None exist
today. The network stack underneath them partially does.

## What Exists (codex/os/net, 28 modules)

- **UDP**: datagram build/parse, pure, working. CoAP's substrate.
- **TCP**: 10-state machine, functional event/action stepping
  (`tcp-step`), TcpConnection records. **No retransmission** — an
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
   sidecars on the x86 battery — no network, no VM peer. This is
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

The crypto floor is already in place and constant-time (Sha256,
Ed25519, AesGcm, ChaCha20, Hkdf, DiffieHellman) — the protocol
work composes primitives, it does not add any.

## The Design

### Layering (uniform across all three protocols)

```
foreword (pure, battery-tested)
  CoapCodec      message encode/decode, option delta coding, codes
  CoapMachine    CON/NON/ACK/RST exchange state, retransmit schedule
                 (pure: takes now-ticks, returns next-deadline)
  MqttCodec      15 packet types, varint remaining-length, v5 properties
  MqttMachine    session state, QoS 0/1/2 flows, keepalive schedule
  LwM2mModel     object/instance/resource tree as records; TLV +
                 SenML-CBOR codecs (Cbor chapter exists)
  LwM2mMachine   bootstrap/register/update lifecycles, Object 5 states
codex.os.net (I/O binding)
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
- Extensions, in priority order: Block (RFC 7959 — required by
  OTA), Observe (RFC 7641 — push telemetry), Resource Directory
  later. Block lands with the base, not after: OTA is the first
  real consumer (`OTAFirmwareUpdate.md`).
- Interop target: Eclipse Californium (host-side, for manual
  interop runs; the gated tests never depend on it).

### MQTT specifics

- v5.0 only (no 3.1.1 compatibility mode in the first cut —
  v5 reason codes and properties are strictly better and all
  major brokers speak it).
- `MqttConnection` is a linear resource: CONNECT acquires,
  DISCONNECT (or error) consumes. Forgetting to disconnect is
  CDX2063 — the keepalive/will story stays type-honest.
- QoS 2's four-step handshake is a pattern match over the pure
  session state; QoS 1 dup handling via packet-identifier table
  (bounded — 65535 ids — fixed-size allocation, no growth).
- Topic alias and session-expiry supported; shared subscriptions
  are server features we merely tolerate in CONNACK properties.

### LwM2M specifics

- Client only. Interfaces: Bootstrap, Registration, Device
  Management, Information Reporting — each a lifecycle in
  LwM2mMachine.
- Standard objects implemented as records over a generic
  object-tree: Security (0), Server (1), Device (3), Connectivity
  Monitoring (4), **Firmware Update (5)**, Location (6) optional.
- Object 5's state machine (Idle → Downloading → Downloaded →
  Updating → Idle, with Update Result codes) is shared with — and
  specified in — `OTAFirmwareUpdate.md`; this stack provides the
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
a configuration option — it cannot be weakened by a deployment
mistake.

**Replay protection**: agent protocol sequence numbers (per-peer,
monotonic, reject duplicates), DTLS/TLS implicit sequence numbers
in AEAD nonce (monotonic, connection-scoped), CoAP message ID
deduplication (§4.5, bounded cache within exchange lifetime).
Security-critical commands (device state mutation, actuation) must
use CON messages over DTLS — enforced by requiring both
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
pre-allocated — no attacker-triggered dynamic allocation.

**Nonce management**: TLS/DTLS record layers use implicit sequence
numbers as nonce components — nonce reuse is impossible within a
session. Application-layer use of `aesgcm-encrypt` outside a
TLS/DTLS context requires explicit nonce-management audit; the
evidence plug should flag such usage.

### What is deliberately absent

Matter/Thread (consumer smart-home; prospectus priority is
IIoT/medical — reference doc exists for later), LoRaWAN (no radio
hardware in the target set), MQTT-SN, and any broker/server-side
MQTT implementation.

## Test Strategy Hook

Per `CrossArchitectureTestStrategy.md`: codecs and machines run in
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
NSTART(=1) in-flight CON exchanges per endpoint — constant memory.
MQTT QoS-2 state is bounded by the receive-maximum property we
advertise. Verdict: low risk with buffer discipline; the DTLS
handshake transcripts are the one multi-KB transient and get a
heap-save/restore bracket.

## Open Questions

1. **Foreword Tls audit.** What does the existing chapter actually
   implement? Until read, the TLS line above is a work estimate,
   not a fact. First task of the MQTT phase.
2. **DTLS version.** 1.2 has universal server support; 1.3 is
   cleaner and shares more with TLS 1.3. Recommendation: implement
   the shared 1.3-style handshake core, expose 1.2 compatibility
   only if Leshan/cloud interop forces it.
3. **PSK vs raw-public-key vs certificates for DTLS.** Raw public
   key (RFC 7250) is the natural fit for Ed25519 device identity;
   cloud brokers often want X.509. Decide per design-partner
   requirements; the record layer is agnostic.
4. **Where the Cbor/SenML codec lives** — extend the existing Cbor
   foreword chapter or a new SenML chapter (recommendation: new
   chapter citing Cbor).
