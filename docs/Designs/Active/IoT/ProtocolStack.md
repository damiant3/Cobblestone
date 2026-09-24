# IoT Protocol Stack: CoAP, MQTT, LwM2M

**Status.** The DTLS 1.3 structural build (D0-D3) is done and peer
authentication ships (X.509 with Ed25519 certs, RFC 8410): two endpoints
complete an authenticated handshake in the battery, and an active
man-in-the-middle who flips a `key_share` byte is defeated. Application
traffic keys are derived and application data is sealed and opened at epoch
3 (`codex/os/net/DtlsEndpoint.codex`: `dtls-ep-derive-app`,
`dtls-ep-send-app`, `dtls-ep-recv-app`), and handshake flights after
ServerHello travel protected at epoch 2. Fragmentation is closed in both
directions. `coaps://` and `mqtts://` are composed and gated
(`CoapsEndpoint`, `Lwm2mCoaps`, `MqttsEndpoint`). **What gates `coaps://`
for compliance purposes is third-party interop, not any missing piece of
the stack.**

**STANDING RULING (Damian): no TLS or DTLS version uplifting.** That covers
the third-party DTLS 1.3 handshake oracle and the OpenSSL 3.5 requirement
recorded below. The reason is priority, not doubt about the gap: there is
no working web server and no working browser yet, and chasing a protocol
version ahead of them is cart before horse. **Do not pick this up, and do
not install a newer OpenSSL for it.** The finding is recorded so nobody
re-derives it.

**The LwM2M client** (`codex/os/net/Lwm2mClient.codex`) carries
Registration, Update, Deregister and the Device Management interface (Read,
Write, Execute) over a flat object tree, pure on DtlsEndpoint's precedent,
gated by `codex/test/apps/lwm2m-client`. **Bootstrap, Observe/Notify and
Access Control are NOT in it** and are each their own unit.

**A CoAP path is one option per SEGMENT.** RFC 7252 section 6.4 step 7 and
section 5.10.1 require one Uri-Path option per segment with no slash in the
value, therefore `coap-uri-path-options` and `coap-uri-query-options` are
the helpers to call; the singular `coap-uri-path-option` puts a whole path
in ONE option. A single-segment path is the one case where the two
spellings agree, which is why an interop harness asking only for
"temperature", "actuator" or "codex" passes against a real server and
proves nothing. An LwM2M path is `/3/0/1`. `coap-find-options` reads a
repeated option as the ordered sequence it is, which `coap-find-option`
cannot.

**`coap-204-deleted` is misnamed.** It is `coap-response-code 2 2`, which
is 2.02 Deleted. The value is right and the name is not; renaming it is its
own change.

**Upstream**: `docs/Reference/IoT/AGENT-PROMPT.md` deliverable 3,
references in `docs/Reference/IoT/Protocols/`

## The Problem

A Codex IoT device must speak the protocols brokers and device-
management servers actually use: CoAP (RFC 7252) for constrained
request/response and firmware block transfer, MQTT v5.0 for
telemetry into AWS IoT Core / Azure IoT Hub / Mosquitto, and LwM2M
v1.2 for the device-lifecycle management the CRA mandates
(bootstrap, registration, firmware update -- Object 5).

The codecs for all of these exist and are battery-tested, the bindings to a
socket exist, and DTLS 1.3 carries the secure form.

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

Modbus, Zigbee, LoRaWAN, BLE ATT and MQTT-SN are codecs only: none has an
endpoint machine or a binding.

## The Bindings

Every protocol in this design is bound to a socket. `tools/coap-client.codex`
cites `Net chapter NetDriver`, `Encode chapter CoapEndpoint` and
`Net chapter UdpIO`, which is the whole shape of a binding, and
`tools/coap-server.codex`, `tools/mqtt-client.codex`,
`tools/mqtts-client.codex` and `tools/lwm2m-client.codex` are the same.

**The LwM2M wire half is REACHED but unanswered, and that is the honest
limit.** Measured in the bed with `-nic`, the guest opens
`guest:5902 -> 10.0.2.2:5683` and pushes the registration datagram, so the
binding emits rather than merely compiling. There is no LwM2M server on this
box, therefore `registered=False` is a true statement about the socket and
no statement at all about the protocol. Pointed at Leshan it becomes an
interop arm, in the shape `build/coap-interop-test.ps1` takes.

## The Network Stack Underneath (`codex/os/net`, 42 chapters 2026-09-24)

- **UDP**: datagram build/parse, pure, working. CoAP's substrate.
- **TCP**: 10-state machine, functional event/action stepping
  (`tcp-step`), TcpConnection records, and RFC 793 serial sequence
  arithmetic (`tcp-seq-wrap`/`-diff`/`-lt`/`-le`/`-ge`). The counters wrap
  on store in `set-send-next`, `set-recv-next` and `tcp-new-connection`,
  therefore the twelve sites that increment a sequence number are each
  fixed once. `codex/test/tcp-seqwrap` pins it.

  **Retransmission is in `NetSession` (`NetworkStack.codex`), not in
  `tcp-step`**, which is a pure state machine with nowhere to put a timer.
  `net-send` arms `rexmit-frame` and `rexmit-ack`, `net-tick` counts down
  and re-sends on expiry, and `net-receive-segment` disarms on a covering
  ACK. `rexmit-queue` holds up to `net-rexmit-capacity` = 8 `RexmitSeg`
  entries with one timer per connection owned by the oldest unacked segment
  (RFC 6298 section 5): an expiry retransmits the head only, and a
  cumulative ACK retires every entry it covers and restarts the timer at the
  base interval with the retry count reset, because an ACK that retires
  anything is evidence the path is alive. The interval doubles (3, 6, 12,
  24, 48 ticks), the retry count is bounded at 5 and the connection is
  declared CLOSED rather than retransmitting forever, and
  `net-connect`/`net-close` arm the timer, therefore a lost SYN or FIN is
  retried. **The bound is refused, not grown:** a send past the capacity
  answers `send queue full` and does not send, because with no collector an
  unbounded queue would let a peer that has stopped acking choose our memory
  ceiling.

  **The RTO is RFC 6298, and the estimator needs no calibrated tick.** RFC
  6298 is a ratio between a measured round trip and a retransmit interval,
  and a ratio is right at any steady tick rate; only a value expressed in
  seconds would need calibration, and nothing here is. `net-rtt-update`
  carries `srtt` scaled by 8 and `rttvar` by 4 in the standard integer form,
  and **Karn's algorithm is honoured**: `rx-resent` is per segment,
  therefore an ack for anything retransmitted yields no sample. Pinned by
  `codex/test/tcp-reliability`: `rtt-rto=6` where a 2-tick round trip gives
  `srtt=16 rttvar=4`, against `karn-rto=3` where the same ack follows a
  retransmission and the estimator is correctly left untouched.

  **A safety bound placed over a wrong comparison converts a performance bug
  into a correctness one**, and the interaction is why the bound and the
  serial arithmetic must be read together: an unwrapped ACK failing the
  compare left the retransmit armed, and the retry ceiling then declared a
  healthy connection dead. The `wrapclose` arm answers ESTABLISHED.
- **Framing/TcpTransport**: length-prefixed LE32 message framing
  over TCP (used by plugs and TrustTransport).
- **DNS, DHCP, NTP, HTTP**: working client implementations.
- **TLS 1.3 client**: the handshake and record layer are the foreword's
  `TlsEndpoint`; `HttpFetch`'s https path drives it over NetIO, graded
  against OpenSSL by `build/https-interop-test.ps1`.
- Architecture pattern: pure protocol codecs + functional state
  machines, I/O confined to a NetIO boundary; `NetDriver` binds the
  NE2000 or the e1000 behind one seam.

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

### The receive path's heap cost

The path is measured by `codex/test/net-recv-heap`, and these are the figures
at head.

| 1,514-byte accepted TCP frame, parsed and checksum-valid | bytes |
|---|---|
| `net-process-frame`, whole | **392** |
| a frame the stack does not recognise | 40 |

392 is `tcp-parse`'s record 48, the checksum's 12-byte pseudo-header 304, and
one `NetResult` 40. Measured on kernel `EC179CDE95FA59DB`, 100 iterations.

**Nothing on the receive path copies the payload.** `TcpSegment` carries
`payload-buf`, `payload-off` and `payload-stop` instead of a `payload` list;
`ActData` and `ActDataAck` carry the same three; `NetResult` carries them
beside `data`, which is the frame rather than a copy of part of it; and
`transport-feed-range` writes the bytes from the frame into the receive
buffer with `__buf-write-byte`, allocating nothing. The range REPLACED the
list rather than joining it, because two representations of the same bytes
are an invariant nothing enforces.

Three properties hold that path down and each is a rule to keep:

- **`tcp-checksum-valid` does not concatenate.** `ip-sum` (`Ethernet.codex`)
  is `ip-checksum` without the fold, so the pseudo-header's unfolded sum is
  carried as the accumulator into a second pass over the segment IN PLACE.
  There is no explicit odd-length `& [0]` pad, because `ip-checksum`'s own
  odd-tail arm already adds a final byte as the high half of its word, which
  `ip-checksum-odd` pins.
- **The IPv4 branch parses at an OFFSET and copies nothing.** Every IP
  accessor, `tcp-parse` and `tcp-checksum-valid` have an `-at` form which IS
  the implementation, with the bare name a wrapper at offset 0, therefore the
  two forms cannot drift. **The segment's END is `off + ip-total-length-at`,
  not the end of the buffer**, which is the bound `ip-payload` applied and is
  why a padded Ethernet frame does not feed its padding to TCP. The ARP
  branch keeps its copy, because an ARP payload is 28 bytes.
- **The send path is cut the same way.** `tcp-with-checksum` carries the
  pseudo-header's unfolded sum into a pass over the segment in place and
  costs **304 regardless of segment length** (measured at 120, 1,480 and
  1,481 bytes). **One call, never a spin:** it writes the checksum back into
  its OWN argument with `list-set-at`, therefore a second call over the same
  segment sums the bytes the first wrote and answers something else.

**The transport records cost 40 bytes each and there are three per frame**,
120 in total: `net-process-frame`, `transport-feed-range` and
`transport-try-recv`. `transport-feed-range` takes the session as an argument
rather than reading it off a transport, and `transport-process-frame-within`,
`net-io-send-drain` and `arm64-net-io-send-drain` all call it directly,
therefore no caller builds a `TcpTransportState` purely to hand to another
builder.

`TcpTransportState` has no `recv-buf` field, and the field was provably
always the empty list: every write of it in the tree was `[]` or a copy of
one, no `__record-set` or `list-push` touched it, and its only read was
`transport-stats`, which is why every arm printing it printed `buf=0`.
`transport-stats` reports `recv-len`, the number of buffered bytes it always
claimed to be.

**`&` concatenation is 6.5x dearer to READ than a flat list** on the same
bytes, and the mechanism is unmeasured; the finding is carried on
`CostModel.md`. It bears on any measurement taken here, because a fixture
built with `&` prices its own assembly rather than the stack: the same
function over the same 1,514 bytes measured 107,435 on an `&`-built frame and
16,400 on a flat one. **A real frame is flat.** `e1000-read-bytes`
(`codex/os/kernel/E1000e.codex:1198`) is `list-push acc (peek-byte base i)`, one push per
byte.

#### Still open on this path

1. **Every x86 receive loop that keeps state across frames reuses one frame
   list; `Arm64NetIO` still builds a `List Integer` per frame, one
   `list-push` per byte** (1,514 bytes become 16,400). `Arm64NetIO` waits
   for two reasons: no bed runs its virtio receive except a QEMU UEFI boot
   (`build/boot-arm64.ps1`), and each of its polls also rebuilds three state
   records (`VirtqueueState`, `VirtioNetState`, `Arm64NetIOState`), which is
   item 2's problem and wants item 2's repair. The measurement is
   `e1000-rx-reuse`'s shape: drain the same N frames through the per-frame
   path and through a reused list and compare the two heap deltas; the arm64
   arm runs in the QEMU cross battery (`build/test-cross-batch.ps1`, QEMU is
   the default bed) with a `.qemudev` naming `virtio-net-pci`, and
   `arm64-web-server` (item 3) is the end-to-end check. The reusing loops are
   NetIO's seven, `web-mux-loop`, `gopweb-pump`, `http-recv` and
   `https-pull`. In the NetIO loops `net-driver-recv-into`
   copies the frame into a list allocated once per loop entry
   (`net-driver-frame-buffer`, 1,522 elements, before the first heap mark),
   and the parse reads it through `net-process-frame-within`, which bounds
   every validator by the frame's length and not the list's, so the tail of
   an earlier, longer frame cannot pass `ip-length-valid`
   (`tcp-checksum-refuse`, the stale arm). `e1000-rx-reuse` receives 20
   injected frames through the reused list at a heap delta of 0 beside 20
   through the list path, whose delta grows. `udp-io-recv`, `http-dns-await`
   and `net-io-poll-one` still take a fresh frame per poll and leak nothing:
   each restores its mark on every frame it does not return.
   `https-pull` hands TLS the range `data-off` to `data-stop` as a copy;
   handing it `r.data` whole passed Ethernet, IP and TCP headers into the
   record parser, and `build/https-interop-test.ps1` then completes no
   handshake at all. That script's rogue arm refuses with the same text a
   broken receive path produces, so the rogue arm alone cannot tell the two
   apart; the ECDSA and RSA arms are the discriminating ones.

   **The DMA ring cannot be handed out as the span directly.**
   `e1000-recycle-rx` returns the descriptor to the card immediately after
   the read, therefore a consumer reading lazily would race the next frame.
   The buffer the loop owns has to be a copy target, which is what makes it a
   fixed reused buffer rather than a view.

   **The conversion is three signatures, not fourteen.** Counting by "takes a
   `List Integer`" conflates an address with a payload (L-ADJECTIVE: a number
   standing in for a structure). Six of the fourteen carry a 4-byte IP or a
   6-byte MAC and never a frame (`net-arp-solicit`, `net-arp-known`,
   `arp-cache-lookup`, `arp-cache-search`, `arp-cache-index`,
   `arp-cache-add`), and a seventh, `tcp-pseudo-header`, BUILDS twelve bytes
   and receives no frame at all.

   | group | signatures | what it carries |
   |---|---|---|
   | INBOUND, the receive loop's own path | `net-process-frame`, `net-process-ip`, `net-process-arp` (each with a `-within` form taking the frame's length) | up to 1,514 bytes off the wire |
   | OUTBOUND and validation | `wrap-tcp-in-ip-eth`, `tcp-with-checksum`, `tcp-checksum-valid`, `net-outbox-frame` | bytes this stack built, or an inbound segment being checked |

   The slice helpers, the transmit path and `Arm64NetIO` are uncounted.

2. **A poll loop's frame branch recurses without restoring the heap.** What
   a bare ACK, an ARP or a FIN leaves behind is the session the parse and the
   outbox flush rebuild, and with no collector every byte of it is permanent.
   The repair is compaction, below (ruled a defect and taken, root,
   2026-09-23). **NetIO's seven loops, `http-recv`, `https-pull`, `web-mux-loop` and
   `gopweb-pump` compact; `Arm64NetIO` does not.** `http-recv`'s
   accumulator is reserved by its caller before the loop's mark and never
   pushed past that reservation, so compaction leaves it where it is.

   **THE STORAGE SHAPE: compaction into arenas the transport owns.** The
   session code stays functional; what changes is where the surviving state
   lives. Rewriting each session update to store in place is refused: it is
   `__record-set` across the whole stack, which writes through every caller's
   pointer (L-ALIAS).

   - `transport-arenas` (`NetCompact`) acquires a SET (a 16-byte header and
     THREE arenas of `net-arena-bytes` each) from the process's pool the
     first time a compacting loop is entered with a transport whose
     `arena-base` is 0, and never again for that transport. Acquiring at
     `transport-new` instead would cost 384 KiB for every transport, and a
     web-mux connection that never enters a NetIO loop costs 66 KB in all
     (`web-mux-heap`). The acquire happens BEFORE the loop takes its
     entry mark, so a freshly reserved set sits below the mark, as must the reused frame list. `TcpTransportState` gains
     `arena-base` and `gen`; all eight record literals in `TcpTransport`
     name both (the compiler refuses a literal that omits a field,
     CDX2006), and a transport built with `arena-base` 0 never compacts
     rather than being refused.
   - A poll loop compacts when `__heap-save` minus its entry mark passes
     `net-compact-bytes`, and every allocation the loop made since the mark
     counts, the tick branch's included. The copy runs TWICE, because the
     x86 `__heap-restore` is one unguarded `mov r10` and a `list-push` at
     the allocator frontier bumps without a bound, so a copy that overran
     an arena would have written past it before any check afterwards could
     look: first the live `TcpTransportState` is copied at the heap top and
     the two `__heap-save` readings around it give its exact size; if that
     exceeds `net-arena-bytes` the loop does not compact and says so; else
     the allocator is pointed at the target arena's base, the same copy
     runs again (same structure, same allocation sequence, same size), and
     the allocator returns to the loop's entry mark. Every frame's leftovers
     above the mark are gone and the loop continues on the arena copy.
   - THREE arenas and not two. The header records the arena written LAST,
     and that arena is never a target: every state derived since the last
     compaction references only it, the plain heap, and memory below the
     arenas, whatever arena it was entered from. The target is the next of
     the other two in turn.
   - The contract a caller takes on: a `TcpTransportState` passed to a poll
     loop is superseded by the one the loop returns, and one older than the
     last compaction can be overwritten by the next. That is the linear use
     every NetIO caller makes (audited: `WebServer`, `HttpFetch`, `GopWeb`
     and the plugs keep the state each call returns). It is GUARDED rather
     than trusted: the header holds a generation each compaction bumps, each
     state carries the generation it was copied at, and a loop entered with
     a state whose `gen` is below the header's refuses (returns its timeout
     result and says so) instead of reading an overwritten arena.
   - Two things this does NOT change: `list-push` still writes in place into
     spare capacity, so a caller's pre-call state can already see a loop's
     pushes (L-ALIAS, true today); and garbage an OUTER loop builds across
     requests (`WebServer`'s per-connection bookkeeping) is outside any poll
     loop's mark.
   - The copy is `nc-transport` in `codex/os/net/NetCompact.codex`. An
     omitted field is refused at compile time (CDX2006); a field filled from
     the wrong source is not, so `codex/test/net-compact-copy` fills every
     field with a distinct value and compares field by field;
     `TcpState` and the other nullary sums are copied through a `when`,
     because nothing here says how a nullary constructor is represented.
   - Sizing is measured, not assumed. The largest session this stack can
     hold is 8 retransmit segments (`net-rexmit-capacity`) of up to 1,514
     frame bytes as `List Integer` elements, 64 ARP entries
     (`arp-cache-max`) and a flushed outbox: about 150 KB by arithmetic, and
     up to about twice that where `list-push` doubled a list's capacity. The
     arm measures a full session's copy with `__heap-save` and sets
     `net-arena-bytes` with margin.
   - Cost: two copies of the session per `net-compact-bytes` of garbage,
     not per frame, and 3 arenas per transport, reserved once.

   Built: `nc-transport`, `transport-compact` and the pool
   (`codex/os/net/NetCompact.codex`, armed by `net-compact-copy`,
   `net-compact-arena` and `net-arena-pool`; a full session copies to
   106,584 bytes). `net-compact-bytes` is one arena, 128 KiB.

   **WHO PAYS FOR THE ARENAS: a per-process POOL** (root, 2026-09-23). A
   per-connection set that is never returned is still a leak, and one set
   shared by the process breaks concurrent transports, therefore: a pool of
   sets of three 128 KiB arenas each (the refusal path above bounds a copy
   larger than one arena), owned by the process. A transport ACQUIRES a set
   in `transport-arenas` and RETURNS it in `transport-release`, which
   `net-io-close` calls, so memory scales with live connections.
   `transport-release` copies the transport off the arenas onto the plain
   heap first, because the next acquire hands the set to another transport
   while the caller still holds the released state; it bumps the set's
   generation, so every state of the previous owner reads stale and a
   second release of the same set is refused rather than linked twice.
   A state older than the last compaction is stale too, so releasing one
   is refused and its set stays out of the pool: callers release the state
   the last loop returned.

   **The pool head is word 40 of the process's x86-64 process-table entry**
   (`proc-net-pool-offset`, table base 20480, 256 bytes per entry),
   indexed by `process-get-pid`, which IS the slot (`emit-load-current-proc`
   derives it from RSP). Every spawn helper zeroes the word, because a
   slot's region is reused by the next process spawned into it and a head
   left by the previous owner points into memory it no longer holds;
   `net-arena-pool` has a child leave a set in its pool, spawns a second
   child into the same slot, and requires that child's pool to start empty.
   **x86-64 only:** the address is not the arm64 runtime's process table and
   nothing zeroes it there, so `net-arena-pool`, `net-ack-leak` and `web-mux-long-run` carry a `.no-cross`, and no
   arm64 production path reaches the pool (`Arm64NetIO` does not compact).
   A plain `process-spawn` gets a 1 MB heap (Spawn Regions), of which one
   set is 384 KiB, so a server spawned that way holds two compacting
   connections at most and wants `process-spawn-with-heap`.
   `rebind-listen-transport` (`WebServer`) releases the transport it
   rebinds, because a web-mux connection acquires a set the first time
   `net-io-send-raw-checked` has to drain.

   **The wiring.** Each NetIO loop, on its first poll, refuses a
   `transport-stale` state, acquires the set, allocates its frame list and
   takes its entry mark after both; after a processed frame and after a
   tick it continues on `transport-compact-past` of its next state, which
   compacts once `__heap-save` minus the mark passes `net-compact-bytes`.
   Nothing a NetIO loop carries across iterations besides the transport,
   integers and caller-owned lists allocated before entry lives above the
   mark, and the branch that RETURNS a message does not compact, so the
   message it returns above the mark survives. `net-io-send-drain` acquires only
   when it actually has to drain.

   Armed by `net-ack-leak`: over 100 and 1,000 bare ACKs driven through the
   loop's own acquire, mark and `transport-compact-past`, the heap above the
   mark stays within `net-compact-bytes` plus one frame, at least one
   compaction fires, and the connection keeps its state; the uncompacted
   control grows ten times over the same run. A `transport-compact-past`
   that never compacts turns exactly the two compaction lines red. The
   real loop is armed end to end by a transpiler plug: the python plug
   receives 335,291 bytes of IR through `net-io-recv-loop` and emits the
   same 147,560 bytes as a plug built from the unwired NetIO, and a plug
   whose compaction zeroes `recv-len` produces no output, so compaction
   fires inside the real receive loop.

   **The mux leaks per request, measured** (`codex/test/web-mux-long-run`,
   seed 11ACE35C): one keep-alive connection served 20 request-and-ACK
   cycles and then 200 more through `web-mux-feed`, retaining 420,560 and
   4,205,600 bytes, 21,028 per request and exactly linear, because
   `web-mux-loop`'s frame branch restores nothing. **The repair is the
   same pool shape with the WebMux as the unit** (root, 2026-09-24):
   `web-mux-loop` acquires a set on its first poll and carries the set and
   its entry mark as loop parameters, so `WebMux` and its literals do not
   change; after a fed frame and after a sweep it deep-copies the whole
   mux (`nc-webmux`: listener, every `WebConn` with its pending bytes, the
   free list) into the next arena once `net-compact-bytes` has been spent,
   with the same two-pass size check and three-arena rotation as
   `transport-compact`; and it returns the set when the loop ends. A mux
   whose copy exceeds one arena is not compacted. Measured on seed
   11ACE35C: a mux copies to 1,472 bytes with one connection and 6,624 with
   eight, 736 per connection that has only completed its SYN, so an arena
   holds about 170 such connections; one connection holding a full
   retransmit queue copies to about 106,584 bytes by itself.
   `gopweb-pump` takes the same change, and the service is spawned with an
   8 MiB heap. Armed by `web-mux-long-run`: the uncompacted control
   still grows ten times over, and the same 220 requests with
   `web-mux-compact` after every feed serve every request, copy the mux
   at least twice, and keep the heap above the mark within one arena and
   one request; a `web-mux-compact` that never compacts turns exactly
   those two lines red.

   **The diagnostic signature if it bites in the field: time-to-death scaling
   with guest MEMORY is heap exhaustion, not a hang** (measured elsewhere at
   265 s on 3 GB against 615 s on 6 GB), and codex-vm prints nothing when a
   guest dies this way.

3. **`codex/test/arm64-web-server` serves under QEMU UEFI and is graded on no
   bed.** `build/boot-arm64.ps1 -Src codex\test\arm64-web-server.codex
   -TimeoutSec 120` boots it, the PCI scan finds the virtio-net card, and a
   request to the printed `hostfwd` port answers HTTP 200 with the 948-byte
   landing page (measured 2026-09-24, seed 9A323747). It carries no
   `.expected`, so no gate runs it (L-NOGATE), deliberately: the run needs a
   host peer. **A device read on arm64 needs `Device.Mmio` in the reader's
   row**: the runtime answers -1 at any non-RAM address to a program without
   the `Device` capability (`codex/plugs/arm64/Arm64Runtime.codex:1037`), so
   an `opening` declaring only `[Console]` read an all-ones ECAM and found no
   PCI function at all. With no virtio-net function the server refuses by
   name (`refused: no virtio-net device among N PCI functions`, QEMU
   `-nic none`) instead of indexing an empty list. Run on the x86-64 bed it
   page-faults at `CR2=0x4010000000`, an ARM64 ECAM address, which says
   nothing about the program.
**The arm asserts SHAPE, not the byte counts**, because an expectation
carrying the numbers would go red on any allocator or codegen change as well
as on a repair, and the next reader would update the figure without learning
why it moved. The counts live in the tables above with the date they were
taken. The instrument needs no new primitive: `__heap-save` returns an
Integer and the delta between two marks is the bytes allocated between them.

### The clock

`net-tick` (`NetworkStack.codex:678`) ages a connection: it fires the RTO,
counts `rexmit-tries`, and at `net-rto-max-tries` declares the peer dead,
clearing the retransmit queue and setting `TcpClosed`. `transport-tick`
(`TcpTransport.codex:170`) wraps it. Three places turn it: NetIO's waits
(`net-io-wait-established`, `net-io-send-drain`, `net-io-recv-wait`,
`net-io-recv-parked`, `net-io-recv-raw`) every `net-io-tick-interval` empty
polls; `Arm64NetIO`'s receive and drain loops every
`arm64-net-io-tick-interval`; and `web-mux-sweep`, which ages every mux
connection once per `web-sweep-interval` (`WebServer.codex:286`, 1,000,000)
polls. `net-io-accept` and `net-io-resolve` do not tick: a fresh listener has
nothing queued. Every ticking loop returns as soon as the connection reads
`TcpClosed`, which is what makes a RST end a drain on a full queue.

**The second argument of these loops is the try count to START AT, not a
limit** (they give up at `net-io-max-polls`), so passing `0` asks for the
longest possible wait; `mqtts-client.codex` starts high on purpose, so a read
that will fail fails cheaply.

**A tick is a count of empty polls, not a duration.** `net-io-tick-interval`
is `net-driver-poll-interval`, measured by the driver at bring-up (NetIO's
chapter head). The constraint to preserve if any of these constants moves:
give-up must land strictly inside the fuel cap, or a dead peer ends the loop
by fuel, which returns what an ordinary timeout returns and reports nothing.
Give-up takes at most 288 ticks against `net-io-max-ticks` 500
(`codex/test/net-io-clock`, `codex/test/net-drain-budget`). HPET is read only
by `net-io-recv-parked`, which falls back to the spinning wait when HPET
reports no rate, because NetIO is also compiled into the transpiled plug
lanes and an x86 MMIO read cannot be under all of them.

**The mux ages at two rates**: 1,000,000 polls a tick while a connection is
idle and `net-io-tick-interval` while its retransmit queue is full
(`web-mux-drain` -> `web-send-http` -> `net-io-send-raw-checked` ->
`net-io-send-drain`), so an `srtt` sampled at the sweep's rate is spent as an
RTO at the drain's, about ten times earlier in real time. Not fixed,
deliberately: it needs 8 segments unacked, which only happens once the peer
has stalled, when a fast timer is the harmless direction. Unifying the rate
means first separating `web-sweep-interval`'s two jobs, aging the clock and
pacing the idle reaper (`web-idle-max` counts sweeps).

**The ARM64 send has a checked form**, `arm64-net-io-send-raw-checked`
(`Arm64NetIO.codex`), with the x86 contract: the bytes sent, whether the send
completed, and a stop at the first chunk `net-send` refuses (FIN_WAIT_1,
FIN_WAIT_2, LAST_ACK: `Tcp.codex`). Armed on QEMU by
`codex/test/arm64-send-checked`; removing the outbox check turns its refusal
line red. `arm64-web-server` still sends through the unchecked
`arm64-net-io-send-raw`.
**Owner: blu** (`codex/os/net/**`).

## The Crypto Floor: Audited

Read against the tree function by function (2026-07-13), so that nothing
downstream is estimated against a chapter nobody opened. **The protocol work
adds primitives rather than only composing them**, which is why this audit
exists.

### Sound, and safe to build on

| Primitive | Chapter | Verdict |
|---|---|---|
| HKDF | `Hkdf.codex` | **Real RFC 5869.** `hkdf-expand` does proper counter-chained `T(n)` (line 39) -- it is not the fake one in `Tls.codex`. Use *this* one. |
| AES-128-GCM | `AesGcm.codex` | **Real NIST SP 800-38D.** `aesgcm-decrypt` verifies the tag before returning plaintext and yields `Maybe` -- the right shape. |
| AES block / key schedule | `Aes.codex`, `Aes256.codex` | `aes-encrypt-block` is exposed. This is exactly what DTLS 1.3 sequence-number masking needs. |
| SHA-256, HMAC | `Sha256.codex`, `Hmac.codex` | Sound. Transcript hash and HKDF rest on these. |
| X25519, Ed25519 | `DiffieHellman.codex`, `Ed25519.codex` | Sound. Key exchange and device identity. |

### Shipped in D0

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

**The TLS key schedule is real.** The RFC 8446 §7.1 ladder runs over the
foreword HKDF and is gated on the **RFC 8448 published trace**: early,
derived, handshake secret, both traffic secrets, server key and IV all match
the IETF's bytes. Three traps are closed and must not be reintroduced: a
`tls-hkdf-expand` that does not counter-chain, a one-byte zero IKM where the
RFC means Hash.length zeros, and **labels encoded as CCE instead of ASCII**,
which also sent SNI hostnames out as CCE. Use `to-unicode` for any ASCII
protocol constant, never bare `char-code`.

`ComplianceEvidence.codex` is reconciled against this floor: its crypto rows
say what they are tested against, and its transport rows (CRA Annex I 1(e),
ETSI 5.3-1, ETSI 5.3-6, IEC 62443 FR4) carry what the transport actually
supplies. The evidence generator is the one thing in this project that must
never lie.

## DTLS: The Build Plan

**Version: DTLS 1.3 (RFC 9147).** Confirmed rather than reopened. It
shares the TLS 1.3 handshake and key schedule (so §5.8's repair is spent
twice), its AEAD-only suite list makes the downgrade story true by
construction rather than by configuration, and mandatory
sequence-number encryption is a real privacy property on a UDP link.
DTLS 1.2 stays unimplemented unless a design partner's server forces it;
that is a deployment discovery, not a design decision, and the record
layer is where it would land.

**The record layer is agnostic to the auth mode**; the mode is X.509 with
Ed25519 certificates (D-auth below, Damian's ruling).

### Phases

Each phase is a CL and each is testable on the x86 battery as pure
samples -- no network, no VM peer, per the standing constraint.

**D0 -- Repair the floor. SHIPPED.** Poly1305,
ChaCha20-Poly1305, and AES-256-GCM, each gated on the published vectors
(RFC 8439 §2.5.2 and §2.8.2; OpenSSL for GCM), and the evidence table
reconciled so its crypto claims are true and its transport claims are
disclosed. The `tls-derive-keys` repair (§5.8) was **deliberately left
out of this CL** -- it is a separate concern from the AEAD floor, D1 does
not need it, and one thing at a time. It is the prerequisite for D2, not
for D1.

**D1 -- The record layer. SHIPPED.**
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

**D2a -- The handshake machine. SHIPPED.**
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

**D2b -- The handshake message layer. SHIPPED.**
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

**Fragmentation: CLOSED IN BOTH DIRECTIONS.**

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

  **The parser must only ever receive a body whose length equals its own
  declared length**, either by the fast path (offset 0, fragment length
  equal to length) or out of reassembly. Handing it a handshake fragment
  with a non-zero `fragment_offset` and a short body is a remotely
  triggerable fault on a pre-authentication datagram anyone can send, and it
  also draws a full HelloRetryRequest for a partial ClientHello (`half-hrr`
  is the census, and it reads 0). A short but SELF-CONSISTENT hello is a
  different input and is still reachable, therefore `runt-out` hands the
  server a self-declared 8-byte ClientHello and pins that it neither faults
  nor goes silent.

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
  **A Finished that verifies proves the peer holds the same handshake secret
  and says nothing about *who* the peer is.** Peer authentication is what
  answers that: `dtls-ep-with-anchors`, `x509-verify-peer` and
  `dtls-ep-authenticated`, gated by `codex/test/apps/dtls-auth-loopback`,
  which pins `client-authenticated=True` on the good path and False on all
  four bad ones (no anchor, MITM key_share, anonymous downgrade, wrong
  expected name).

**D2c -- The hello bodies. SHIPPED.**
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

**D3 -- The endpoint. SHIPPED.**
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

**D-auth -- Peer authentication. SHIPPED (X.509, Ed25519).** It
is the whole difference between "transport works" and "DTLS works."
Without it, everything above agrees an **anonymous** X25519 key: safe
against a passive eavesdropper, defeated by an active man-in-the-middle
who runs one handshake with each side. Certificate and CertificateVerify
now exist and the MITM is defeated in the battery -- see the A1-A5
sections below. **The anonymous mode still exists beside the
authenticated one, and it is still not secure.**

Application traffic keys are derived and app data is sealed at epoch 3
(`dtls-ep-derive-app`, `dtls-ep-send-app`); the handshake flights after
ServerHello travel protected at epoch 2, not as DTLSPlaintext; and
fragmentation is closed in both directions (D2b above). **What gates
`coaps://` is third-party interop, not any of these.**

The credential model is **decided (Damian): X.509.** PSK and
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

**A1 -- `Asn1.codex`, the DER decoder. SHIPPED.** Decoder only;
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

**A2 -- `X509.codex`. SHIPPED** (`codex/foreword/encode/X509.codex`, gated by
`codex/test/x509-parse`). Certificate and TBSCertificate parse; Ed25519
SubjectPublicKeyInfo; issuer and subject retained as **raw DER** (comparing
decoded names is how you get name-confusion bugs); validity; extensions.
The TBS is kept as a byte slice, never re-encoded.

**A3 -- Chain validation. SHIPPED** (`codex/foreword/encode/X509Chain.codex`, `x509-chain-verify`
and `x509-verify-peer`, gated by `codex/test/x509-chain`). Signature verify
against the issuer's key,
validity window against a caller-supplied `now` (this chapter has no
clock), `basicConstraints` CA + pathlen, key usage, SAN matching, trust
anchor set. Negatives: tampered body, expired, wrong issuer, a leaf
presented as its own CA.

**A4 -- The messages. SHIPPED** (`Certificate` and `CertificateVerify` are in
`codex/foreword/encode/DtlsMessage.codex`, exercised end to end by
`codex/test/apps/dtls-auth-loopback`). `Certificate` (RFC 8446 §4.4.2) and
`CertificateVerify` (§4.4.3), including the signature context -- 64 `0x20`
bytes, the context string, `0x00`, then the transcript hash. The context
string is ASCII, so it goes through `to-unicode`, **not** bare `char-code`
(§5.8's CCE trap, which shipped a whole key schedule in CCE). Plus the
`signature_algorithms` extension, which the ClientHello DOES send:
`tls-ext-sig-algs` is in the base extension list at
`codex/foreword/encode/DtlsHello.codex:89`, and the prose above that line
records what its absence cost.

**A5 -- Wired into `DtlsEndpoint`. SHIPPED.** Opt-in and
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

**The `Random` is the caller's entropy, not the endpoint's public key.**
`dtls-ep-new` takes `random` as its third parameter and stores the value as
`ep-random` (`DtlsEndpoint.codex:96`), and `dtls-ep-random` reads
`ep.ep-random` (`:124`).

**The entropy path is EXERCISED, by `codex/test/dtls-random`**, and it has to
be: a capability that sits in a signature with no caller has never been shown
to work (L-UNHEARD).

The arm builds two endpoints from ONE private key that differ only in the
entropy handed in, because a single endpoint proves nothing here: an
implementation deriving `Random` from the endpoint's public key answers a stable
32 bytes and looks correct until a second endpoint is asked. Measured: endpoint
a answers `32,31,30,29` and endpoint b answers `232,231,230,229`, each 32 bytes,
each carrying the caller's own first byte, and the two differ.

**And the standing limit on the whole X.509 stack:** it is
**Ed25519-only**, because Ed25519 is the only signature primitive the tree
owns. It authenticates a fleet whose CA *we* run. It **cannot validate a
commercial CA's chain** -- public CAs sign RSA or ECDSA, and AWS IoT and
Azure IoT both require one of them. That is a separate crypto campaign
(RSA modexp over a `BigInt` we do not have, or P-256 ECDSA), and it has
not started.

**The composition exists.** `codex/os/net/CoapsEndpoint.codex` carries
CoAP as DTLS application data (RFC 7252 §9.1), one message per record,
gated by `codex/test/apps/coaps-loopback`: an authenticated handshake
between two of our endpoints, then a confirmable GET sealed, opened,
parsed, answered in a piggybacked ACK and delivered, with the arms that
a plaintext pass-through would fail. The composition holds a `CoapEp`
and a `DtlsEp` and neither had to learn about the other.

**Sealing happens at send, and no sealed record is stored**, because the
two layers disagree about repetition on purpose: RFC 7252 §4.2 wants the
retransmission to be the SAME datagram, identified as a duplicate by
message id, and RFC 9147 §4.5.1 wants every record to carry a sequence
number the peer has not seen. A cached record satisfies the first and
violates the second, and the symptom is a request retransmitted four
times into a peer that discards each copy before CoAP sees it. The arm
asserts the two records differ and the message id inside them matches.

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

Both halves are built.

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
codex.os.net (I/O binding)                   -- BUILT
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
- **Message-id deduplication is IN** (§4.5, `ce-acked-mids`, capacity 8,
  cleared per exchange, and a full cache stops recording rather than
  growing). The half that was missing is the reply, not the suppression:
  a retransmitted Confirmable response met the completed interaction's
  state guard and drew NOTHING, so the peer went on retransmitting until
  it gave up. `codex/test/apps/coap-loopback`'s `dup-con ack` reports 1
  with the cache and **0 without**. `deliveries=1` is the control that
  the re-acknowledgement does not also deliver the body a second time,
  which is what routing the duplicate back through the response path
  would do. The cache is consulted for Confirmable messages only, because
  a message id is its sender's own counter and the ids we acknowledge can
  collide with the ids we chose for our own requests.
- Extensions, in priority order: Block (RFC 7959 -- required by
  OTA), Observe (RFC 7641 -- push telemetry), Resource Directory
  later. Block lands with the base, not after: OTA is the first
  real consumer (`OTAFirmwareUpdate.md`).
- Interop target: Eclipse Californium (host-side, for manual
  interop runs; the gated tests never depend on it).

### MQTT specifics

- **mqtts:// is composed and gated.** MQTT is a TCP protocol, so its
  secure form is MQTT over TLS, not over DTLS; MQTT-SN is the datagram
  one and has a codec (`MqttSn.codex`) but no endpoint machine.
  `codex/foreword/encode/MqttsEndpoint.codex` holds an `MqttEp` and a
  `TlsEp`, gated by `codex/test/apps/mqtts-loopback`: an authenticated
  handshake between two of our endpoints, then CONNECT, CONNACK and
  SUBACK with no network and no broker process. `tools/mqtts-client.codex`
  composed the same two chapters already, but it talks to a real broker
  and is therefore not a gate.
- **The composition's whole job is that the two layers disagree about
  boundaries.** MQTT is a self-delimiting byte stream, TLS is a record
  protocol, and nothing relates them, so one record can carry three
  packets and one packet can arrive across two records with its length
  field split. The read offset advances by what MQTT reported CONSUMED,
  never to the end of the accumulated buffer, so a trailing partial
  packet is presented again with the next record's bytes after it. The
  arm sends a SUBACK in two three-byte records: `split half=-1 whole=1`
  with the offset discipline, and `whole=-1` without, measured both ways.
  The other three arms pass in both versions, which is why this one has
  to exist.
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

- **coaps:// is composed and gated.** `codex/os/net/Lwm2mCoaps.codex`
  holds an `Lwm2mClient` and a `DtlsEp` and carries the registration
  lifecycle as DTLS application data, gated by
  `codex/test/apps/lwm2m-coaps-loopback`: authenticated handshake,
  Register sealed and recovered as CoAP addressing `/rd`, 2.01 Created
  with the location, then Update and Deregister. It is a separate chapter
  from `CoapsEndpoint` rather than a generalisation because the two wrap
  different machines; what they share is the carrier, and that is what to
  lift out if a third ever needs it.
- **The arm censuses the application epoch's record numbers**: `seq
  reg=0 upd=1 dereg=2`, exact values, the same property the handshake
  epoch's census pins after the flight-numbering repair. Equal numbers
  would be one AEAD key and one nonce across three management exchanges.
- **The registration handle is echoed exactly, because OMA LwM2M treats the
  location the server returns as opaque.** `lc-location` is a
  `List (List Integer)` holding the Location-Path options as the server sent
  them, and `lwm2m-loc-options` emits one Uri-Path option per stored
  segment, never joining them into a path and re-splitting it, because a
  handle containing a `/` would come back as two segments addressing
  something else. `lwm2m-client-deregister` must not clear the location
  before building the DELETE, or the DELETE carries no path at all. Both
  properties are measured by `lwm2m-coaps-loopback`: `update to-rd` and
  `deregister to-rd`.
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

**The window is WIRED on the application-data path.** `Dtls.codex`
implements RFC §4.5.1's 64-entry window as `dtls-replay-ok` and
`dtls-replay-accept`, split so a record that fails to authenticate cannot
advance it. `DtlsEp` carries `ep-app-window`, and `dtls-ep-recv-app`
deprotects, then tests the window, then advances it. That order is
forced: the sequence number travels encrypted (§4.2.3), so `dtls-open`
is what makes it readable, and advancing before deprotection would let
one forged datagram with a high sequence number shut the window on every
genuine record behind it.

Measured both ways, which is the only reason the arm is worth anything:
`codex/test/apps/dtls-app-loopback`'s `app replay len` reports 2 with
the window in place and **4 with the check disabled**, the replayed two
bytes appended a second time. The control is the first delivery, which
passes in both runs.

**The handshake epoch has its own window** (`ep-hs-window`, same order:
deprotect, test, advance). Epoch 0 is deliberately not windowed, because
a record there is a ClientHello and the defence is the stateless cookie;
a window would hold per-peer state before the peer has proven an address,
which is the exhaustion the cookie exists to prevent. A duplicate at the
handshake epoch is worse than wasteful: the transcript is a running hash
of what was received, so a Certificate folded in twice yields a hash no
peer computed and an honest CertificateVerify then fails against it.

**Every record in the authenticated server flight carries its own sequence
number, and the census that pins it must be exact values rather than a
verdict.** Certificate, CertificateVerify and Finished sealed at the same
record sequence would travel under one key with one AEAD nonce (§4.2.2
derives the nonce from that number), which is the one thing AES-GCM has no
margin for, and any conforming peer's replay window would discard the second
and third. **Do not count a flight by differencing accumulator lengths:**
`list-push` extends its accumulator in place and returns the same list, so
the flight's output names all alias and `list-length o1 - list-length o0` is
always zero (L-ALIAS). The count comes from the fragment lists themselves.
`app hs-seq` reads `cert=0 replay=0 cv=1 fin=2`.
`dtls-fragmented-flight` covers the multi-fragment case, where the
Certificate alone occupies two record numbers.
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

1. **Where the Cbor/SenML codec lives** -- extend the existing Cbor
   foreword chapter or a new SenML chapter (recommendation: new chapter
   citing Cbor).
2. **What `ComplianceEvidence.codex` may claim** (Damian's ruling):
   fix the code to make the claim true where it can be, soften only where it
   cannot. **No row asserts anything the tree does not contain**, and a row
   that credits the toolchain with supplying a secure channel it does not
   supply is DEPLOYMENT, not MECHANISM.
