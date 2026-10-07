# Shared shard connection ownership

`ShardLinks` holds a bounded set of game/admin TCP slots for one receive
loop. `sl-new ports mac ip gateway mask` accepts one to eight slots, each
bound to port 2593 or 2594. Repeated port entries give that port more slots.
It copies address lists, allocates each slot's receive buffer, transmit queue
and transport arenas, then records the scratch boundary. Allocate all other
persistent application state and the NIC receive buffer before calling it.

The owner dispatches a frame with `sl-feed owner frame validLength`. Invalid
checksums, other destination IPs, unconfigured ports and excess connections
return -1. Existing connections match both TCP ports and peer IP. A new SYN
claims only a free slot of the requested port. The session uses the incoming
link source MAC for its return hop; this does not populate a shared ARP cache.
ARP frames use slot zero's session. A zero subnet mask selects the gateway.

An accepted frame returns its slot index. Handshake payload and later data
remain raw in that slot's fixed 8192-byte receive buffer. Process/consume those
bytes through the selected protocol and retain the resulting transport.
The owner does not interpret UO packets or HTTP. If data exceeds available
receive capacity, the slot is marked `failed`; stop protocol admission on
that connection and close it. Do not continue after silently dropping data.
An unflushed outbox on the selected slot also marks failure.

Only the surrounding loop reads the NIC. Its order is receive/dispatch,
flush all outboxes, process admitted protocol input, enqueue responses, advance
each occupied slot once with `sl-send`, and flush again. `sl-send` uses
[ShardTx](ShardTx.md), which retains an unsent offset and never reads the NIC.
Service ACKs even while application output is blocked. Run `sl-tick owner 0`
at the stack's timer cadence, with empty outboxes, then flush any retransmits.
`sl-flush owner 0` writes frames but does not receive or wait for a peer.

`sl-close owner index False` begins graceful close only after the response
has entered TCP and the retransmit queue has room for FIN. A False result
means retry after more send/ACK progress. To abort a failed connection, use
`sl-close owner index True`: it discards unsent application output and buffered
input, then begins close when TCP has room. Failed slots continue processing
TCP control traffic while admitting no further application data. Flush and
continue servicing ACKs/timers until terminal. Do not call `net-io-close`:
this owner retains its arenas for reuse. Notify the application of disconnect
before `sl-release`; release requires a closed TCP state and empty outbox.
It clears occupancy and pending output. A subsequent admission increments
the slot's epoch and resets its receive/transmit cursors. Reset per-connection
protocol state on each epoch; never carry authentication or XOR state across
slot reuse. An exhausted epoch counter prevents further admission there.

Call `sl-compact` between application operations, after consuming every
scratch-owned reply, error or protocol object. It checks and copies every
transport into its own arena before restoring the common scratch boundary.
All slots, receive buffers and response buffers survive. Retain the slot's
current transport, not a previous transport reference. Do not release or
replace its arenas. A missing/stale arena or oversized copy refuses without
restoring the common boundary; stop the loop and report the failure. Earlier
slots may already have moved into their arenas when a later slot refuses.

Per-slot storage is fixed: an 8192-byte receive buffer, a 65,536-byte response
buffer and the network stack's three 131,072-byte arenas, plus fixed records.
Frame routing is linear in the bounded slot count. Compaction copies each
live transport graph and refuses a graph beyond the existing per-arena cap;
there is no unbounded fallback. Call it at a bounded scratch-growth threshold,
not once per incoming byte. Protocol admission and pending-request budgets
remain obligations of the shared loop.

`proofs/ShardLinksProof.codex` models two ports, raw handshake data, peer and
checksum refusal, slot exhaustion, independent output, repeated compaction,
ACK routing, receive overflow and terminal reuse. It does not exercise a live
NIC loop, DHCP, HTTP authentication or a game/world transaction. Those remain
the composed server's acceptance checks.
