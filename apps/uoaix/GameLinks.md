# Concurrent game sessions

`GameLinks` uses the existing ShardLinks transport slots and ShardTx queues.
It is a single-owner event loop, not concurrent world mutation. The callback
shapes remain GameNet's packet, pulse and disconnect shapes:

```text
gl-new specs game map huffman hostPort ports mac ip gateway mask
  -> Result GameLinks Text
gl-serve packetHandler pulseHandler disconnectHandler links
  -> [Console, Network.Read, Network.Write, e] Integer
```

Ports are repeated2593 entries, at most eight. Reserve spare slots for login
and relay connections in addition to active players. Existing ShardServer's
HTTP/game API is unchanged; this entry serves game links only. It does not
start another NIC reader or call a blocking send loop. Allocate captured
owner/presentation state before gl-serve establishes the final scratch mark.

Every admitted TCP epoch reconstructs its own GameInput, including XOR state,
partial frame, diagnostics and character session. Global connection numbers
identify epochs to callbacks. Registry declarations are shared and immutable.
Relay tickets are separately retained after their login socket closes, with
bounded capacity and120-second expiry; attempting game login consumes only
that ticket. A later relay cannot invalidate an earlier pending game login.
The shared next-key counter still advances inside the packet callback, where
the composite can persist it. Tickets and sessions are intentionally volatile.

The existing test-account credential policy and five character slots remain.
Different characters can be active concurrently; selecting or recreating a
character already active in another session refuses. This is not a new account
service. The loop temporarily binds GameShard.active to the slot's authenticated
actor while calling its packet, pulse or disconnect handler, then restores the
prior value. The pending-key field is similarly scoped for legacy game login.
These are compatibility fields, never client-selected authority. Callbacks must
not interleave another handler or retain a borrowed actor binding after return.

Each round drains at most16 NIC frames, handles at most one complete packet
per slot, and attempts one TCP segment per slot. Pulses are at most once per
100ms per active session and wait behind that session's pending reply. Each
transport has its own HPET epoch and retransmission clock. An idle round parks
the core. A full send queue blocks only its own session. Replies are capped at
256 packets and65536 encoded bytes. Slot receive buffers remain8192 bytes.

EOF, refusal and logout call the disconnect hook once. Failure of that hook
latches an owner fault and stops the loop. Other packet refusals close only
their connection; ambiguous storage errors must also latch the composite's
existing fault before it returns Err. Graceful close retains queued output;
terminal slots are reused only after the outbox is flushed. Idle-link expiry
retains ShardServer's30000-PIT-tick traffic bound.

## Composite work

Fester owns the binding after the single-player composite grade. Supply the
existing committed route/pulse/flush callbacks, but make their presentation
state per connection: GameClientView, decoration visibility and family scene
markers cannot be a single shared last-connection cache. Shared immutable
decoration and durable door state may remain common. A map-cache cover/rebind
must precede each player's callback when players occupy different windows.

Replace cg-bind's one-business-character admission with an explicit per-player
economy binding before grading the second composite player. Do not share the
first player's purse or mint starter money as a networking workaround.
GameLinks adds no durable codec fields; shared world/character/economy state
keeps its existing owner codecs. Recovery starts with empty sessions/tickets.

Costs are bounded by eight links, receive/transmit limits and registry size.
Active-character and relay lookup scan at most eight records. Packet framing
retains the existing bounded receive-prefix shift. Reply encoding is linear
in bytes. Each timer catch-up is bounded by NetIdle's500 ticks. Fixed protocol
arenas, transport arenas, TX/RX buffers and clocks survive compaction; temporary
packets and callback results do not. No per-poll world copy is introduced.

`proofs/GameLinksReplay.codex` checks relay expiry, actor ownership, restoration
after a callback error, compaction lifetime, once-only disconnect and owner
fail-stop. `proofs/GameLinksServerProof.codex` plus `test-game-links.ps1` checks
two outstanding relays, interleaved partial game logins, two simultaneous
characters, independent movement/status/pulses, duplicate-character refusal,
consumed-ticket refusal and one player remaining responsive after the other's
logout. These are native/synthetic protocol grades, not complete-composite
or real-client acceptance.
