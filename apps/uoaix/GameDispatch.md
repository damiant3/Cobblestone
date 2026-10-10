# Storage-capable game dispatch

`GameNet` exposes `gn-chunk-with`, `gn-connection-with` and `gn-loop-with`.
Each takes a handler before the existing arguments. The handler shape is:

```text
GameShard, GameInput, WalkMap, hostPort, packetBytes -> [e] Result GameReply Text
```

Framing, XOR state, partial packets and opcode budgets remain in GameNet.
The handler runs once for a complete decoded packet, including login/relay
packets. Accepted-packet logging, position observation and response sending
follow its Ok result. Err goes through the existing bounded refusal diagnostic
and closes the connection without sending a GameReply.

`gn-default-handler` retains the existing pure login/GameSession dispatch.
The original `gn-chunk`, `gn-connection`, `gn-loop` and `game-server` remain
pure-adapter entry points with their existing declared effects. A caller can
use the `-with` entry points with storage effects without forcing disk access
into replay callers. Declare `Device.Block` on the application opening when
the supplied handler uses it; the runtime grant comes from that declaration.

The handler owns admission, private mutation/undo, durable commit and error
recovery. Return Ok only when any promised mutation is committed. The hook
does not persist state or automatically roll it back. The callback itself
must not publish a response or world observation before its commit. Stop
admission and recover an ambiguous storage failure rather than retrying an
acknowledged action. Preserve the single-owner invariant of the world and
store while dispatch is running.

Allocate the handler and all captured persistent state before entering the
network loop's heap mark. Do not retain packet lists, temporary replies or
request-owned text without copying into the appropriate owned lifetime.
The complete-packet seam adds call and parameter forwarding without a world
copy. Opcode-registry costs are below. Storage-handler heap/time behavior
is a separate caller obligation.

## Opcode handlers and pulses

An owner needing a final socket-close flush uses
`gn-connection-hooks-with-disconnect`, `gn-loop-hooks-with-disconnect`, or
`game-server-with-disconnect`. Each adds a callback immediately after `pulse`:

```text
GameShard, GameInput -> [e] Result Integer Text
```

The existing entry points supply `gn-no-disconnect`. On every accepted
connection's exit (EOF, transport failure, handler/pulse close or refusal),
the transport closes, then this callback runs once before `gs-disconnect`
and before connection scratch is reclaimed. It also runs for login/relay
connections; the owner checks its own dirty state. Protocol logout may have
already cleared the session, so do not require phase2 to flush pending state.
The owner avoids a second commit when the packet handler already saved it.
Callback closures and retained state must precede the connection heap mark.
Do not retain GameInput or its packet/raw buffers after the callback returns.

Ok permits ordinary session cleanup and the next accept. Err logs the reason
and connection, returns nonzero through the connection and listener, and skips
session cleanup and scratch reclamation for fail-stop recovery. A caller using
the connection-level API must honor that nonzero result. The listener never
retries an ambiguous commit. A connection that never established has no callback.
This seam does not itself persist anything. `proofs/GameDisconnectReplay.codex`
checks the terminal-socket path, callback order, owner-error propagation and
legacy cleanup; disk/restart proof belongs to the composite owner. Both loops
remain self-tail recursive; the seam adds constant work and scratch per exit,
with no retained allocation beyond the owner's callback state.

`GameOpcodeSpec { opcode, size, minimum, maximum }` declares framing.
Fixed packets use size=minimum=maximum. Size zero means an opcode followed
by a big-endian two-byte total length; minimum is at least 3. Every maximum
is at most 256, matching GameInput's assembly buffer. `go-valid specs`
rejects invalid declarations, duplicate opcodes and changed framing for an
existing core opcode. At most 256 declarations are accepted. Undeclared
core packets retain their existing framing; undeclared unknown packets refuse.
Do not mutate the registry after validation.
The composition owner supplies one framing declaration per opcode even
when lane handlers share that opcode by subtype or target. Deduplicate
identical declarations; reject conflicting declarations.

The fixed/dynamic length convention is adapted from SphereServer Source-X,
`src/network/packet.cpp`, `Packet::checkLength` (Apache License 2.0), at
https://github.com/Sphereserver/Source-X/blob/master/src/network/packet.cpp.
The Codex port adds explicit lower/upper bounds and immutable core framing.
The license is retained in [Sphere-LICENSE.txt](Sphere-LICENSE.txt).

Each lane exports a callback from its own chapter:

```text
GameShard, GameInput, WalkMap, hostPort, packetBytes
  -> [e] Result (Maybe GameReply) Text
```

`Ok None` means unhandled and must leave state unchanged. `Ok (Just reply)`
means handled; Err refuses and closes the connection. Compose lane callbacks
on None, then wrap the combined callback with `gn-route`. That wrapper calls
extensions only for mode2/phase2, always retains core login, relay, creation
and selection dispatch, and falls back to the core handler on None. A callback
uses `input.session.serial` as its authenticated actor and still enforces its
own target, range and privilege rules. Callback return types do not grant
authority, durable commit or automatic rollback.

War-mode request 72 has fixed length five in the core profile. Before world
entry, the default handler consumes it without changing authentication,
relay keys, characters or combat state. Its empty reply carries the note
`pre-world war mode ignored` with stream mode and session phase; the owning
receive loop logs that note with opcode and connection. In-world requests
still route to the combat callback. `proofs/PreWorldWarReplay.codex` checks
encrypted framing, subsequent authentication and selection, and that the
callback remains gated until entry. The added guard has constant heap and
time cost, with no retained allocation. Real-client acceptance is separate.

The blocking `gn-send` path preserves a partially admitted packet across a
full retransmit queue. It resumes at the checked sender's absolute byte offset
for at most six drain calls per encoded packet. Each call retains NetIO's
ACK processing, receive buffering and retransmission clock. Closed or stale
transports and other send refusals stop immediately; exhausted backpressure
still closes with queue/closed/stale diagnostics. WAIT lines identify the
unsent offset. This is bounded waiting, not an unlimited delivery promise.
The shared nonblocking `ShardTx` path is unchanged. The native
`proofs/GameSendWaitReplay.codex` supplies controlled sender results to check
delayed progress, exact suffix retry and terminal/budget stops; it does not
simulate client ACK timing. The wrapper is self-tail recursive, retains no
new history and adds only a fixed number of drain calls and scratch records.

Pulses are separate from packets and receive an explicit tick:

```text
GameShard, GameInput, WalkMap, hostPort, tick
  -> [e] Result (Maybe GameReply) Text
```

`gn-pulse callback ... tick` applies the same mode2/phase2 gate. None means
no reply. Keep deadlines in caller-owned persistent state; do not treat a
pulse call as an elapsed fixed interval. No invented empty packet is routed.
The tick is raw `get-ticks`, not milliseconds. On the native guest, one tick
is `pit-count / pit-input-hz` seconds. Convert a positive millisecond delay
with ceiling division of `delay * pit-input-hz` by `1000 * pit-count`, using
bounded arithmetic. The PIT reload/input constants are in
`codex/compiler/Emit/X86_64Boot.codex`, PIT and PIC Initialization; the timer
interrupt increments the tick cell once. Shared owners use the same unit
when calling the hook. Do not silently pass a millisecond clock instead.

The standalone entry is
`game-server-with specs (gn-route combinedHandler) pulse hostPort windowSize`.
It validates the registry before preload and sends packet/pulse replies only
after the callback returns. Its receive pass is bounded to one calibrated
network poll tick, including transport-timer service, before an idle pulse.
This is not a wall-time deadline under host descheduling or a blocked send.
The connection loop remains self-tail-recursive; pulse helpers return before
the next iteration, so idle sessions do not accumulate call frames.
`gn-no-pulse` preserves the old behavior. The existing default entry points
and complete-packet callback seam remain available.

For the shared owner, validate `go-valid specs` once at startup and construct
each connection with `gn-input-with specs connectionNumber`, including slot
resets. `gn-byte` then uses that connection's framing registry. Call `gn-route`
for packets and `gn-pulse` on the shared owner's clock; enqueue returned
packets through the same transaction and outbound queue as packet replies.
Do not use the standalone blocking sender inside the shared loop. Pulses and
packet callbacks obey the same private-mutation/commit requirement above.

Allocate registry and captured callback state below the connection/owner
scratch marks. GameInput is now 104 native bytes; its packet and diagnostic
buffers remain 256 bytes each. Registry validation is O(N squared) at startup;
lookup is O(N) at opcode/prefix boundaries, not on every byte. A spec is 32
bytes, and default-spec temporaries are request scratch. Dispatch validates
the complete packet's declared extent and byte range in O(packet length).
The framework adds
no growing retained history or world copy; callback costs remain the lane's.

`proofs/GameOpcodeReplay.codex` supplies fixed05, variable12 and fixed72 test
handlers. Startup checks cover bounds, duplicates, incremental framing and
authentication/pulse gates. Run the normal socket replay with `-Hooks` to
grade fallback packets, registered replies and a delayed pulse without
additional client input through the live
sender. The 72 echo is a test handler, not the combat implementation. Real
client handler acceptance remains root's run; this proof is synthetic.

## Client packets in play (1.25.32)

The packets a player's 1.25.32 client sends after world entry, and what answers each. The set is ServUO
`Server/Network/PacketHandlers.cs`'s client registrations through 0xB3 (the client's length table, `go-lengths`, ends
there), plus 0x56, 0x66, 0x69, 0x71 and 0x93.

| Opcode | Client action | Answered by |
|---|---|---|
| 01 | logout | `gs-handle` (close) |
| 02 | walk | `gs-walk` |
| 03 | speech | `gs-speech` and the composite's speech handlers |
| 05 | attack | `GameCombat` |
| 06 | double-click | the composite chain (craft, harvest, vendors, doors, games, deeds) |
| 07, 08, 13 | lift, drop, equip | `GameSkillsItems` |
| 09 | single click (and All Names) | `GameSkillsItems` labels, `cg-creature-label` for creatures |
| 12 | skill, spell or action command | `ActiveSkillsHandler`, `MageryActions` |
| 22 | resync | `gs-resync` |
| 2C | death menu answer | `GameCombat` |
| 34 | status (type 4, any serial) and skills (type 5) | `GameCombat`; `ActiveSkillsHandler` and `GameHarvest` |
| 3B, 9F | buy, sell | `GameVendor` |
| 56, 66, 75, 93 | map pin, book page, pet rename, book title | skipped with a note (UOAIX-175 to 177) |
| 69 | text or emote colour change (5 bytes, sent twice, no colour carried; UOX3 CPTextEmoteColour) | skipped by `gn-default-handler` (correct: it carries nothing) |
| 6C | target | the cursor's owner |
| 6F | secure trade | the composite |
| 71 | bulletin board | `BulletinBoard` |
| 72 | war mode | `GameCombat` |
| 73 | ping | echoed |
| 7D | menu choice | craft, skills and paging menus |
| 95 | dye | `GameDye` |
| 9B | help | `CompositeHelpCommands` |
| A7 | tip or notice request | ignored |
| B1 | gump answer | craft, decorator, plays, battleship, cards |

Not counted as player packets: 04, 0A, 14, 47, 48, 58, 61, 79, 7E and 9D are god-client requests; 9A and AC answer a
prompt or a text entry the server never sends. To re-run the audit, count `PACKET <n> unhandled packet skipped` in the
server logs (`gn-default-handler` skips every packet with no fixed size, `gs-handle` the fixed ones it does not claim): across 137 trial and live logs (2026-10-06 to 2026-10-09) the only one was 0x69.

## Proof

```powershell
pwsh -File apps/uoaix/proofs/test-game-dispatch.ps1 -ClientRoot C:/Users/Damian/uo1998-client -CompressionSource <Compression.cs> -Kernel seed/Codex.cdx -OutDir <new directory>
```

The harness creates a synthetic block fixture, checks the exact native
dispatch oracle, then launches an owned private MUL adapter and guest on
free host ports. The marker server uses `gn-loop-with` and the default game
admission logic; each successful callback writes a disk counter before
returning its reply. The existing encrypted socket replay grades login,
creation, walking, speech, reconnect and refusal diagnostics through that
loop. The guest must exit through its graceful shutdown event before the
counter is compared with the complete accepted-packet log. The adapter reads
the original client files in place and the harness stops only its own processes.

The native proof checks that a partial frame does not call the handler,
that a completed frame executes actual block I/O once, and that handler
refusal preserves the no-reply path. `-Poison` applies poisoned allocation
to both proof entry points. `-StartupSeconds` bounds map preload, which
performs the existing sequential range transfers before listening. The proof
gives its private adapter a 30-second request timeout for instrumented guests;
the standalone adapter retains its five-second default.

The marker is a hook witness, not a world-state commit. These fixtures do
not establish live shard durability or unmodified-client acceptance.
`CompositeGame.md` owns the current composite integration and its separate
save/restart grades.

## Idle waits (NetIdle)

GameNet now opts into `NetIdle` for accept and connected receive waits. Empty
RX parks the native x86 core with `cpu-park`; the periodic boot interrupt is
the fallback when the NIC has no enabled wake interrupt. A per-connection HPET
epoch supplies elapsed100ms stack ticks and a100ms receive-return deadline for
pulses. The epoch also accounts for callback time; ticks already advanced by
legacy send drains are not credited twice. Elapsed time ages the current
retry/close countdown once. A retry emitted after a stalled callback receives
its full new interval starting at the current tick; missed ticks never replay
several retry intervals without giving the peer response time. Receive polling
also runs with buffered application data when buffer capacity permits. The old
countdowns age and the session clock advances before the received frame can
rearm a timer. Expired countdowns use a transient -1 marker. Expiry dispatch
runs after RX, allowing a queued final-retry ACK to retire or rearm the timer
before give-up. Dispatch never decrements a freshly rearmed timer.
Each parked receive pass drains at most 16 frames before expiry dispatch,
stopping earlier on an empty driver or insufficient application-buffer space.
Frames are compacted individually; no frame list is retained.
GameNet's timed sender polls RX after each handler/pulse returns, then stamps
the send time before admitting replies. GameLinks stamps peers, drains bounded
shared RX, then dispatches expiry before post-handler TX/close. New sends and
partial ACKs therefore start their intervals at the current clock.
GameNet concatenates a reply's independently Huffman-terminated packet streams
before the checked TCP send. The byte order and packet terminators remain
unchanged. The encoded reply budget is 65536 bytes; encoding and concatenation
are linear in the reply bytes, with bounded scratch and no new retained state.
Movement replies are sent during packet dispatch, before the subsequent view
pulse. PACKET, PULSE, LISTEN and REFUSE logs retain their prefixes and append
an HPET-derived `ms=` timestamp for correlating handler and send delays.
SEND records report reply byte count and separate encoding/send durations.

Input is fed as raw bytes, even
on the establishing ACK. Closed/stale transports and EOF retain their existing
handling. The old `gn-connection-step` polling helper remains available to its
direct callers; the connection/listener entry points use the parked path.

`NetIdle` requires native x86 HPET and the boot timer. It makes no cross-target
or hosted-runtime promise. Clock and RX storage precede its compaction mark;
wait loops are self-tail recursive, with fixed retained storage and bounded
transient compaction. Timer work is independent of missed-tick count, with at
most one bounded retransmission-queue traversal per advancement. Existing
NetIO polling APIs and send-drain policy are
unchanged.

The native timer proof is `proofs/NetIdleReplay.codex`. It checks elapsed ticks,
deadline return, legacy-drain credit, retransmission give-up and raw buffering.
The event-order checks invoke the production timed sender and receive-frame
path with an ACK that retires one of two real queued segments, then check the
remaining segment's rearmed interval immediately before and at its deadline.
Final-retry controls include both a full and partial ACK already queued at the
expiry deadline, plus no ACK, which must still close the connection.
`proofs/GameIdleServerProof.codex` uses a synthetic terrain window to grade the
network path without MUL startup.

Before NetIdle, `gn-accept` and NetIO's connected receive wait polled without
parking and the idle composite burned about one CPU core. Adding a sleep to a
poll-count clock instead would slow every timeout; the waits account elapsed time.
