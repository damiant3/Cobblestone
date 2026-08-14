# IoT Protocol Stack: CoAP, MQTT, LwM2M

**Created**: 2026-06-12 (reek)
**Updated**: 2026-08-09 (blu) -- DTLS send-side fragmentation shipped;
three stale "still open" claims corrected in place.
**Status**: The DTLS 1.3 structural build (D0-D3) is **done**, and
**peer authentication shipped 2026-07-13** (X.509 with Ed25519 certs,
RFC 8410): two real endpoints complete an *authenticated* handshake in
the battery, and an active man-in-the-middle who flips a `key_share` byte
is defeated. **It is still not a secure channel**, and the reasons are
now narrow and specific: no application traffic keys (the endpoint
derives handshake secrets only), no fragmentation reassembly, and the
handshake flights travel as DTLSPlaintext. **So `coaps://` still does not
exist and ETSI 5.5 / CRA 1(c) remain transport-gated**.
Also still missing across the wider stack: the CoAP/MQTT/LwM2M binding
layers in `codex/os/net`. (This line said **TCP retransmit** as well;
that was wrong when written -- see the 2026-08-08 correction below.)

**CORRECTION, 2026-08-05: the tree has moved past the status above, and
the body below reads stale in the done direction.** What the tree now
shows, with evidence:

- Application traffic keys ARE derived and app data is sealed/opened at
  epoch 3 (`codex/os/net/DtlsEndpoint.codex`: `dtls-ep-derive-app`,
  `dtls-ep-send-app`, `dtls-ep-recv-app`).
- Handshake flights after ServerHello travel protected at epoch 2
  (`DtlsEndpoint.codex:133-147`).
- `CoapEndpoint` and `MqttEndpoint` exist (`codex/foreword/encode/`)
  with wire halves `tools/coap-client.codex`, `tools/coap-server.codex`,
  `tools/mqtt-client.codex`, plus `coap-loopback` and
  `build/coap-interop-test.ps1`. (This line said a general LwM2M client is
  still absent. It landed 2026-08-09 as `codex/os/net/Lwm2mClient.codex`;
  see the 2026-08-09 entry above.)
- RSA verification exists (`codex/foreword/core/Rsa.codex`:
  `rsa-verify-pkcs1-sha256`, `rsa-verify-pss-sha256` over `CryptoBig`),
  with `X509Chain`, `TlsEndpoint` and `TrustAnchors` in
  `codex/foreword/encode/`.
- `dtls-ep-random` now takes caller entropy via `dtls-ep-new`'s random
  argument.

**BACKLOGGED, Damian's ruling 2026-08-09: any TLS or DTLS version
uplifting.** That covers the third-party DTLS 1.3 handshake oracle and the
OpenSSL 3.5 requirement below. The reason is priority, not doubt about the
gap: there is no working web server and no working browser yet, and
chasing a protocol version ahead of them is cart before horse. **Do not
pick this up, and do not install a newer OpenSSL for it.** The finding
stays recorded below so nobody re-derives it; it is simply not next.

Still recorded but NOT open work: the third-party DTLS handshake oracle,
blocked on a tool version (below). **The general LwM2M client landed 2026-08-09**
(`codex/os/net/Lwm2mClient.codex`): Registration, Update, Deregister and
the Device Management interface (Read, Write, Execute) over a flat object
tree, pure on DtlsEndpoint's precedent, gated by
`codex/test/apps/lwm2m-client`. Bootstrap, Observe/Notify and Access
Control are NOT in it and are each their own unit.

**Building it found a defect in CoAP that had never been reachable.**
`coap-uri-path-option` puts a whole path in ONE Uri-Path option, and RFC
7252 section 6.4 step 7 requires one option per segment with no slash in
the value (section 5.10.1). Every path this tree had ever asked for was a
single segment -- "temperature", "actuator", and "codex" in
`coap-interop-test.ps1` -- which is the one case where the two spellings
agree, so the interop harness passed against a real server and proved
nothing about multi-segment paths. An LwM2M path is `/3/0/1`.
`coap-uri-path-options` and `coap-uri-query-options` split correctly;
`coap-find-options` reads a repeated option as the ordered sequence it is,
which `coap-find-option` cannot. Also added: `coap-opt-location-path` (8)
and `coap-204-changed`. **`coap-204-deleted` is misnamed** -- it is
`coap-response-code 2 2`, which is 2.02 Deleted; the value is right and the
name is not, left alone rather than renamed under an unrelated change.

**The RTT-derived retransmit interval landed 2026-08-09, and the reason it
sat open was wrong.** It was recorded as needing a calibrated tick, since
no caller drives `net-tick` at a known rate. The estimator never needed
one. RFC 6298 is a ratio between a measured round trip and a retransmit
interval, and a ratio is right at any steady tick rate; only a value
expressed in seconds would need calibration, and nothing here is. The
fixed 3 ticks it replaces was wrong in both directions at once, too eager
on a slow link and too patient on a fast one. `net-rtt-update` carries
`srtt` scaled by 8 and `rttvar` by 4 in the standard integer form, and
**Karn's algorithm is honoured**: `rx-resent` is per segment, so an ack for
anything retransmitted yields no sample. Pinned by `codex/test/tcp-reliability`:
`rtt-rto=6` where a 2-tick round trip gives `srtt=16 rttvar=4`, against
`karn-rto=3` where the same ack follows a retransmission and the estimator
is correctly left untouched.

**"Third-party DTLS interop" was one item and is now two, one of them
done.** The fragmentation layer HAS a third-party oracle as of 2026-08-09:
`codex/test/dtls-openssl-fragments` reassembles real OpenSSL records, and
`build/dtls-fragment-interop.ps1` regenerates them. The full handshake does
not, and **what blocks it is a tool version rather than a decision**:
OpenSSL gained DTLS 1.3 in 3.5, and 3.2.4 is what is installed (measured
2026-08-09 against both the mingw64 and usr/bin builds; Python's `ssl`
exposes no DTLS at any version). This was previously recorded as needing a
ruling on outside dependencies. That was wrong: six interop harnesses
already shell out to OpenSSL and Python (`tls-interop-test.ps1` and five
others), so the precedent was settled long ago and nobody needed to decide
anything.

**Fragmentation is closed in BOTH directions as of 2026-08-09.** Receive
landed 2026-08-08; send landed the following day once the MTU question
was decided rather than deferred, at 1200 bytes of UDP payload
(`dtls-ep-mtu`) with a 1024-byte fragment body. See section D2b below,
which carries the derivation of both numbers and the pre-authentication
fault the receive wiring closed.

**TCP retransmit is NOT one of them, and the "no RTO timer" claim below
was already false when this correction was written** (measured
2026-08-08 against `codex/os/net/NetworkStack.codex`). It is not in
`tcp-step`, which is why a reader looking there found nothing: the timer
lives one layer up in `NetSession`, which is the layer that owns frames
and has somewhere to put one. `net-send` arms `rexmit-frame` with the
built frame and `rexmit-ack` with the sequence number that would retire
it, `net-tick` counts down and re-sends on expiry, and
`net-receive-segment` disarms on a covering ACK.

What 2026-08-08 added, gated by `codex/test/tcp-reliability`: the
interval now doubles (3, 6, 12, 24, 48 ticks), the retry count is
bounded at 5 and the connection is declared CLOSED rather than
retransmitting forever, and `net-connect`/`net-close` arm the timer so a
lost SYN or FIN is retried -- previously only data segments were, which
left connection setup, the least reliable moment on a lossy link, with
no retransmission at all.

**What is still missing, stated so nobody reads the above as done:**

- ~~**One segment deep.**~~ **FIXED.** `rexmit-queue` holds up to
  `net-rexmit-capacity` = 8 `RexmitSeg` entries, one timer per
  connection owned by the oldest unacked segment (RFC 6298 section 5).
  An expiry retransmits the head only; a cumulative ACK retires every
  entry it covers and restarts the timer at the base interval with the
  retry count reset, because an ACK that retires anything is evidence
  the path is alive.

  **The bound is refused, not grown.** A send that would exceed the
  capacity answers `send queue full` and does not send. There is no
  collector, so an unbounded queue would let a peer that has stopped
  acking choose our memory ceiling.

  Measured against the pre-change chapter on the two-send case: the old
  code retransmits sequence 5003, the SECOND send, because the second
  `net-send` had overwritten `rexmit-frame` and segment 5001 was
  unrecoverable. The new code retransmits 5001. `codex/test/tcp-reliability`
  carries `qoldest` for exactly that, reading the sequence number back out
  of the frame that reached the outbox rather than trusting the queue's own
  bookkeeping.
- **No RTT measurement.** The RTO is a tick count, not RFC 6298
  SRTT/RTTVAR, and a tick is whatever period the caller drives the
  session at. The backoff is real; the base interval is a constant
  nobody derived from a measured path.
- ~~**The ACK comparison does not handle sequence wraparound.**~~
  **FIXED, and it was worse than this entry said.** There were two
  defects, not one: the comparisons were ordinary integer compares, AND
  the in-memory counters were never wrapped at all, so `send-next` grew
  past 2^32 unbounded while everything arriving from the wire came back
  truncated by `read-be32`. `Tcp.codex` now carries `tcp-seq-wrap`,
  `tcp-seq-diff` and the three RFC 793 serial comparisons; the counters
  wrap on store in `set-send-next`, `set-recv-next` and
  `tcp-new-connection`, so the twelve sites that increment a sequence
  number are each fixed once.

  **Recorded because the interaction is the part worth knowing:** on its
  own this produced spurious retransmits past 4 GB. Combined with the
  bounded-retry give-up added the same day, it produced a *closed
  connection* -- the wrapped ACK failed the compare, the retransmit was
  never disarmed, and the retry ceiling then declared a healthy
  connection dead. A safety bound placed over a wrong comparison converts
  a performance bug into a correctness one. `codex/test/tcp-seqwrap`
  pins it: the `wrapclose` arm answers CLOSED against the pre-change
  chapters and ESTABLISHED now.

**Ruling 2026-08-05 (Damian): RESURFACED as the base of the IoT chain.** The remainder is live work in the ProtocolStack -> OTAFirmwareUpdate -> ComplianceEvidence chain. The TCP segment queue is closed 2026-08-08, and DTLS fragmentation is closed in BOTH directions as of 2026-08-09: receive through `ep-reasm`, send at a 1200-byte MTU with a 1024-byte fragment body, derived in D2b below. **That list is now empty of open work.** The RTT-derived retransmit interval and the general LwM2M client both landed 2026-08-09, and the third-party DTLS handshake oracle was BACKLOGGED the same day (Damian: no TLS or DTLS version uplifting; there is no working web server or browser yet, so it is cart before horse).

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

**Upstream**: `docs/Reference/IoT/AGENT-PROMPT.md` deliverable 3,
references in `docs/Reference/IoT/Protocols/`

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
2. ~~**TCP retransmission.**~~ **PARTLY CLOSED -- see the 2026-08-08
   correction at the top, which has the measurement and the three
   remaining holes.** `tcp-step` does indeed have no RTO timer and never
   will: it is a pure state machine and the timer lives in `NetSession`.
   The single-segment depth and the wraparound-unsafe compare are both
   closed. **What survives of this gap is the absent RTT measurement, and
   nothing else.** MQTT keepalive and QoS 1/2 assume a reliable stream;
   they now have a bounded, multi-segment, oldest-first retransmit over a
   correct sequence space, driven by a tick count nobody has calibrated
   against a real path.
3. **No binding layer.** `codex/os/net` has 32 modules and not one of
   them is a `CoapEndpoint`, `MqttConnection`, or `LwM2mClient`. The
   codecs are pure functions nobody calls over a socket. Until the
   Action-list interpreters described under "Layering" below are
   written, the protocol work has no runtime existence.

## The Network Stack Underneath (codex/os/net, 32 modules)

- **UDP**: datagram build/parse, pure, working. CoAP's substrate.
- **TCP**: 10-state machine, functional event/action stepping
  (`tcp-step`), TcpConnection records, and RFC 793 serial sequence
  arithmetic (`tcp-seq-wrap`/`-diff`/`-lt`/`-le`/`-ge`). Retransmission is
  in `NetSession` (`NetworkStack.codex`), not here: an 8-deep
  oldest-first queue, exponential backoff, bounded at 5 retries, covering
  SYN, data and FIN, refusing a send past the bound.
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
| TCP retransmission (RTT estimate only) | MQTT keepalive + QoS assume a reliable stream | Timer, backoff, bounded retry, an 8-deep oldest-first queue and serial sequence arithmetic all exist in `NetSession`; what is left is an RTT-derived interval, which needs a clock the session does not have |
| TLS 1.3 client | MQTT to any cloud broker | Audit foreword Tls; complete: X25519 (DiffieHellman chapter) + HKDF + AES-GCM/ChaCha20 exist, so the work is handshake + record layer, not primitives |
| DTLS 1.2/1.3 | CoAP security (coaps://), LwM2M mandates it | Record layer over UDP: retransmitting flights, cookie exchange, epoch/sequence in AEAD nonce; shares handshake core with TLS work |

### The clock exists. One production caller drives it. (measured 2026-08-09, val)

The row above says the retransmission gap needs "a clock the session does
not have". Measured, the position is worse and more specific than that:
**the clock exists and almost nothing turns it.**

`net-tick` (`NetworkStack.codex:459`) is what ages a connection: it fires
the RTO, counts `rexmit-tries`, and at `net-rto-max-tries` declares the peer
dead, clearing the retransmit queue and setting `TcpClosed` (line 464).
`transport-tick` (`TcpTransport.codex:115`) wraps it. **Census of every
mention in the tree, excluding definitions: `WebServer.codex:270`, and two
tests (`tcp-reliability.codex`, `tcp-seqwrap.codex`). That is the whole
list.** L-UNCALLED.

So in every program except `WebServer` -- all 60 language plugs, `HttpFetch`,
`TrustTransport`, `Arm64NetIO`, the other servers -- **no retransmission is
ever sent, the RTT estimator never runs, and a connection is never declared
dead.** The RTT-derived interval added at 14272 is unreachable from those
paths.

**The failure this produces is a HANG, and it was previously recorded as a
truncation.** `net-io-send-drain` (`NetIO.codex:113`) loops while
`net-rexmit-full`, polling for a frame. Its only other exit is
`tries > 50000000`. Nothing in that loop advances the clock, so a segment
that is lost is never retransmitted and the ACK that would drain the queue
can never be provoked. Two measurements pin it:

- **The 50,000,000 cap is not reachable in practice.** The sibling loop
  with the identical cap (`net-io-accept`) was still polling after **180
  seconds** with no peer. So "the drain gives up at its fuel cap" is not an
  outcome any caller sees.
- **A peer that ANSWERS does not help either.** Nothing outside `net-tick`
  clears `rexmit-queue` except `net-rexmit-prune` on an ACK, so a RST or FIN
  closes the connection while leaving the queue full, and the drain keeps
  spinning on a connection that is already dead.

**What this is not.** It is not a missing failure channel on
`TcpTransportState`, which is how it was carried in `CurrentPlan` until this
measurement. Once the clock runs, `net-tick` already sets `TcpClosed` on
give-up and the caller can read `(ts.session).conn.state`; no new record
field and no 23-site construction sweep is needed.

**DECIDED AND LANDED 2026-08-09 (blu): the I/O loops tick on a poll count,
not on a wall clock.** `NetIO` now carries `net-io-tick-interval = 100000`
and `net-io-max-polls = 50000000`, and `net-io-send-drain`,
`net-io-recv-wait` and `net-io-recv-raw` each spend one `transport-tick`
plus an outbox flush every `net-io-tick-interval` polls. All three also
return as soon as the connection reads `TcpClosed`, which is what makes a
RST end the loop instead of leaving it spinning on a full queue.

**Why a poll count and not HPET.** The only wall clock in the tree is
`Hpet`, an x86 MMIO read at `#FED00000`, and `NetIO` is compiled into the
transpiled plug lanes and the ARM64 path as well, so citing it would put an
x86 device read under all of them. The estimator asks only that the rate be
steady (the section above), which a poll count is.

**Why 100000, and this is the constraint to preserve if either constant
moves.** Give-up must be reachable strictly inside the fuel cap, or a dead
peer ends the loop by fuel, which returns exactly what an ordinary timeout
returns and reports nothing. Measured by `codex/test/net-io-clock`, which
counts the ladder rather than asserting it: 141 ticks with no RTT sample,
288 with the RTO clamped at `net-rto-max-ticks`. 288 * 100000 = 28,800,000
polls against a 50,000,000 cap.

**Measured end to end, not only by inspection** (2026-08-09, seed
`B3C1BAA8F961D247`, codex-vm, no peer on the wire). A session with one
unacked segment handed to `net-io-recv-raw` came back **CLOSED, queue
empty, `now-ticks` 141** after 263 s: the full unmeasured ladder ran
inside the poll loop and ended by give-up. The same program before this
change returns ESTABLISHED with `now-ticks` 0. 141 ticks at 100000 polls
is 14.1M polls in 263 s, so a poll cost about 18.6 us in that bed --
**that is the emulated NIC's number and not a property of the design; a
tick is a count and its duration is whatever the caller's poll loop
costs.** The battery arm (`codex/test/net-io-clock`) pre-ages the session
to one tick short of give-up so it proves the same chain in one interval
and 2 s. Sabotaged by pushing `net-io-tick-interval` past the fuel cap, it
fails.

`net-io-accept` deliberately does NOT tick. It is entered on a fresh
listening transport with nothing queued and no close timer, so a tick
there advances `now-ticks` and allocates a session for it and buys
nothing. This is the loop val measured at 180 s without reaching its cap;
that measurement stands and is about the cap, not about the clock.

**The stack now has TWO tick rates, and this change is what made that
true.** `net-io-tick-interval` is 100000 polls; `web-sweep-interval`
(`WebServer.codex:263`) is 1000000, and it is what ages every connection
in the concurrent mux, because `web-mux-loop` polls
`net-driver-recv-frame` itself rather than going through a NetIO wait. The
two meet in one place, verified by reading the chain rather than assumed:
`web-mux-drain` -> `web-send-http` -> `net-io-send-raw` ->
`net-io-send-chunk` -> `net-io-send-drain`, so a mux connection is aged at
1000000 while idle and at 100000 while its retransmit queue is full.

The estimator's premise is that the rate is STEADY, and across that
boundary it is not: an `srtt` sampled under the sweep's rate is spent as
an RTO under the drain's, which makes the retransmit fire about ten times
earlier in real time than the sample implied. **Not fixed, deliberately.**
The condition needs 8 segments unacked, it only arises when the peer has
already stalled, which is when a timer running fast is the harmless
direction, and there is no web server in service to measure a better
number against. The clean repair is not to copy the constant across:
`web-sweep-interval` does two jobs, aging the clock and pacing the idle
reaper (`web-idle-max` counts sweeps), so unifying the rate means
separating those two first.

**Two parts of the census this did NOT close, both measured 2026-08-09.**

- `net-io-wait-established` has a **10000**-poll cap, not fifty million, so
  it already terminates and no tick can fire inside it at any sane
  interval. The consequence is that a lost SYN is never retransmitted:
  `net-connect` queues it, `transport-connect` sends it once, and a connect
  that gets no SYN-ACK fails after 10000 polls. That is a capability gap,
  not a hang, and it is separate work.
- **The ARM64 path is worse than untimed and it is the 14317 defect, still
  live.** `arm64-net-io-send-chunk` (`Arm64NetIO.codex:102`) calls
  `net-send` and advances by `arm64-net-mss` without reading the result's
  refusal and without any `net-rexmit-full` check at all, so past
  `net-rexmit-capacity * net-mss` = 11,200 bytes it silently drops
  everything it thinks it sent. The x86 path was fixed at main 14317; this
  copy never was. Its loops do not tick either. Not fixed here because
  there is no ARM64 bed on this box to run the change against.

**Owner: blu** (`codex/os/net/**`). Raised by val out of C2, which is where
the symptom surfaced; C2 does not own the fix.

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

**Fragmentation: CLOSED IN BOTH DIRECTIONS.** Receive landed 2026-08-08,
send 2026-08-09.

  The endpoint reassembles a fragmented peer flight through `ep-reasm`
  and `dtls-ep-on-frag`, and it now cuts its own oversized messages
  through `dtls-ep-wrap-into` / `dtls-ep-seal-into`, each fragment
  becoming its own record with its own record sequence number.

  **The MTU was a decision and it has been made: 1200 bytes of UDP
  payload (`dtls-ep-mtu`), 1024 of fragment body (`dtls-ep-frag-body`).**
  1200 is not a measurement -- an endpoint with no clock, no ICMP and no
  socket cannot make one, and RFC 9147 §4.4 leaves the value to the
  implementation. It is the number chosen so that no path has to be
  measured: 1200 + 40 (IPv6) + 8 (UDP) = 1248, inside the 1280 every IPv6
  link must carry (RFC 8200 §5) and far inside Ethernet's 1500. QUIC
  fixed on 1200 for the same reason (RFC 9000 §14.1). The 1024 body limit
  is the MTU less the worst framing we emit -- 5 bytes of unified header,
  1 inner content type, a 16-byte tag, 12 bytes of message header, 34 in
  all -- with the remaining 142 bytes of slack there so an outer tunnel
  does not force the constant to be re-derived. Measured: the largest
  record in a fragmented flight is 1058, which is 1024 + 34 exactly.

  **The control run is the only arm that can see this change.** Gated by
  `codex/test/apps/dtls-fragmented-flight`, whose server carries a
  four-certificate chain (a 1368-byte Certificate message). Against
  `DtlsEndpoint.codex#13` the flight is 4 records with a largest of
  **1402** and `within-mtu=False`; after, 5 records, largest **1058**,
  `within-mtu=True`. Every other arm -- `client-done`, `server-done`,
  `agree` -- passes on BOTH revisions, because both endpoints are in one
  process and no path is ever involved. A test without the size arm would
  have certified the broken build.

  **Wiring the receive half fixed a REMOTELY TRIGGERABLE FAULT, and that
  was not what the work set out to do.** Before it, a handshake fragment
  with a non-zero `fragment_offset` went straight to the hello parser
  carrying a body shorter than its own declared length, and the guest
  died: `!EXC=06` at RIP 0x120a7b, CR2 0x1a00000, reproduced twice at the
  same address on `DtlsEndpoint.codex#12`. Worse than the crash, the
  fragment BEFORE it was answered: a partial ClientHello drew a full
  HelloRetryRequest (`half-hrr` measured 1, now 0). Both are
  pre-authentication, on a datagram anyone can send.

  It was the CONTROL run that found it, not the feature work. The arms
  were written to show reassembly working and the pre-change arm was
  expected to answer 0; it faulted instead.

  The class is closed rather than the instance: after the change the
  parser only ever receives a body whose length equals its declared
  length, either by the fast path (offset 0, fragment length equal to
  length) or out of reassembly. A short but SELF-CONSISTENT hello is a
  different input and is still reachable, so `runt-out` hands the server
  a self-declared 8-byte ClientHello and pins that it neither faults nor
  goes silent.

  Send: `dtls-msg-fragments` splits at a caller-given body
  size, each fragment carrying the WHOLE message length with its own
  offset and fragment length. A body that fits emits one fragment
  byte-identical to `dtls-msg-encode`, so nothing that did not need
  fragmenting changed -- which is why `dtls-loopback`,
  `dtls-auth-loopback`, `dtls-app-loopback` and `dtls-fragmented-hello`
  all pass unmodified across the send-side change. Receive: `DtlsReasm` accumulates fragments for one
  `message_seq`, out of order, tolerating duplicates and overlaps,
  because a retransmitted flight is not obliged to be cut where the first
  copy was. Bounded at 16 fragments and 16384 bytes, refusing rather than
  allocating, since the peer choosing those numbers is not authenticated
  yet. Gated by `codex/test/dtls-fragment`, which pins the negatives too:
  a gap reports incomplete rather than assembling short, and an over-long
  declared length or a fragment running past the end is refused.

  **And gated against an implementation we did not write**, which
  `dtls-fragment` cannot be: it cuts with `dtls-msg-fragments` and rejoins
  with `DtlsReasm`, so it cannot separate a correct implementation from two
  consistently wrong ones. `codex/test/dtls-openssl-fragments` carries real
  DTLS records captured from OpenSSL 3.2.4 through a recording UDP proxy.
  The capture gave us more than a fragmented message: **OpenSSL
  retransmitted the flight and refragmented it at different boundaries**
  (0+261, 261+347, 608+179, then 0+347, 347+347, 694+93). Our own generator
  cuts the same way twice and can never produce that case, which RFC 9147
  section 5.2 nonetheless requires a receiver to tolerate. Four arms: each
  transmission alone, a mixed delivery interleaving fragments from BOTH cuts
  out of order and overlapping, and a gapped negative. The first three
  reassemble to the same SHA-256 that OpenSSL and Python independently
  compute over the same message; the fourth reports incomplete. Regenerate
  with `build/dtls-fragment-interop.ps1 -Regenerate`, which mints a fresh
  chain, so a rerun confirms the property over different bytes rather than
  replaying the frozen ones.
  **Peer authentication was listed here as open and is not.** It shipped
  2026-07-13 and the header of this file has said so since; this bullet
  simply outlived it. The observation it made is still true -- a Finished
  that verifies proves the peer holds the same handshake secret and says
  nothing about *who* the peer is -- but the answer exists:
  `dtls-ep-with-anchors`, `x509-verify-peer` and `dtls-ep-authenticated`,
  gated by `codex/test/apps/dtls-auth-loopback`, which pins
  `client-authenticated=True` on the good path and False on all four bad
  ones (no anchor, MITM key_share, anonymous downgrade, wrong expected
  name). Corrected 2026-08-09 (blu).

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
authenticated one, and it is still not secure.**

**The three items this paragraph listed as open are all closed, and the
sentence that followed them outlived its own heading.** It read "Until
D-auth lands, `coaps://` does not exist" in a paragraph whose first line
says D-auth SHIPPED. Corrected 2026-08-09 (blu): application traffic keys
are derived and app data is sealed at epoch 3 (`dtls-ep-derive-app`,
`dtls-ep-send-app`); the handshake flights after ServerHello travel
protected at epoch 2, not as DTLSPlaintext; fragmentation is closed in
both directions (D2b above). What still gates `coaps://` is third-party
interop, not any of these.

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
