# Shared-owner transmit queue

`ShardTx` copies one response batch into a caller-owned byte buffer, bounded
by 65,536 bytes. Allocate each queue below the shared receive loop's scratch
mark. `stx-enqueue` refuses a busy queue, oversized response or non-byte input
before altering its buffer or cursors. Convert text at the protocol boundary
before enqueueing. Packet framing and compression belong to the caller.

`stx-step queue transport` queues at most one MSS-sized segment through
`net-send`. It performs no network I/O, waits or receive calls. Retain the
returned transport and flush its outbox with `flush-transport-outbox` before
another step. An unflushed outbox refuses without advancing the cursor.
When the TCP retransmit queue is full, the result has zero sent bytes and
`complete = False`; return to the shared receive loop and dispatch incoming
traffic for every connection. Feed ACKs and service retransmit/close timers
there, then resume from the retained offset.

`complete` means all response bytes have entered TCP's retransmit queue,
not that the peer acknowledged them. For graceful closing, use
`transport-close`, flush its outbox and retain the transport while the shared
loop feeds ACKs and ticks its timers. Release its arenas only at terminal
teardown. Do not use `net-io-close` with outstanding data: that helper releases
the transport's arenas immediately. Its retransmit frames own their data,
so a completed response buffer can be reused. The shared owner must preserve
all connection transports during compaction; this module does not implement
that loop or make the existing game/admin servers concurrent.

One queue owns its configured byte capacity and a fixed-size record. Enqueue
takes linear validation/copy time and retains no input list. A step copies at
most one MSS of bytes plus the existing network frame/retransmit structures.
A blocked step allocates only its result; it does not copy the pending response
or poll. Bound connection count and reclaim transport scratch in the owner.
