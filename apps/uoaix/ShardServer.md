# Shared game and HTTP receive loop

`ShardServer` composes [ShardLinks](ShardLinks.md), game framing and HTTP
request handling. `ss-serve gameHandler httpHandler server` owns the NIC
receive call. Neither handler may read the NIC or invoke a standalone server
or synchronous network-send loop. Both handlers can carry storage effects;
declare their runtime capabilities on the application opening.

Create persistent world/admin state, map and Huffman table before `ss-new`.
It accepts those game/map references, the relay's
host port, listener-slot ports, local MAC/IP, gateway, subnet mask and current
runtime tick. Supply ports 2593 and 2594, repeated for the desired bounded
connection count. `ss-serve` sets the final scratch boundary after receiving
both handler function values, including their captured state. Enter through
that function, not the internal polling loop. The native fixture uses one of
each; the live fixture uses
six game and two admin slots. Addresses are configuration, not DHCP discovery.

Each slot has a fixed protocol-construction arena. Startup measures `gn-input`
and refuses if it would exceed that arena. A new connection epoch reconstructs
the framing/XOR/session state there. It retains no prior authentication or
partial packet. This avoids leaving protocol records above the common scratch
mark. Every handler must copy any retained request text or bytes into its own
persistent storage before returning; temporary replies are copied into the
slot's owned transmit queue before compaction.

A round drains at most sixteen NIC frames, then admits at most one complete
game packet or HTTP request per slot,
then queues at most one TCP segment per slot. Incomplete requests remain in
the bounded receive buffer. Incomplete HTTP is parsed again only after new
bytes arrive; EOF with an incomplete request aborts the connection. Game
framing uses `gn-byte` and invokes the game
handler only on a complete decoded packet. Successful replies are encoded
and queued before accepted-packet/position logging. Return Ok only after any
promised transaction has committed. The loop provides ordering, not world
rollback or storage recovery. A handler error aborts that connection; it must
also latch any ambiguous storage failure in the authoritative owner so later
connections cannot continue admission against uncertain state.

The HTTP handler receives one parsed request at a time and must authenticate
every request, including requests on an existing connection. The shared loop
does not retain an HTTP authentication decision. It supports further requests
after a response, matching the existing admin route's per-request contract.
The production handler binds `panel-route` or `admin-route` to the intended
auth/admin owner; it must not expose GM mutations backed only by GmMemory.

The loop flushes generated frames without receiving inside send, services TCP
timers at ten runtime ticks, and compacts all transports when scratch exceeds
1 MiB. On the current 100 Hz runtime clock, that timer cadence is 100 ms.
Idle connections abort after 30,000 runtime ticks without accepted TCP
traffic. End-of-stream with a partial game frame produces the existing
bounded refusal diagnostic. Closing disconnects the game session once;
subsequent FIN/ACK/timer passes cannot disconnect a newer session for the same
character. Terminal slots are released only after their outboxes are empty.

Costs are bounded by slot count and receive/transmit capacities. Game input
scans at most the receive buffer until one packet completes; consuming a
prefix shifts the remaining bounded bytes. A coalesced batch of small packets
can therefore take quadratic shifting work in its received byte count, capped
by the 8192-byte buffer. HTTP parsing and reply encoding
use request scratch. Game replies are capped at 64 packets and 65,536 encoded
bytes, then flattened into one exactly sized list in linear byte time.
Each protocol slot adds a 4096-byte construction arena
and fixed metadata to ShardLinks' storage. No per-poll world copy is made.
Empty outboxes are skipped. The handlers own their own heap/time budgets.

## Proofs and remaining integration

`ShardServerProof.codex` checks HTTP progress beside partial game input,
callback-before-output order, framing lifetime through compaction, and
once-only disconnect across reuse. The shared socket witness is:

```powershell
pwsh -File apps/uoaix/proofs/test-game-dispatch.ps1 -Shared -ClientRoot C:/Users/Damian/uo1998-client -CompressionSource <Compression.cs> -Kernel seed/Codex.cdx -OutDir <new directory>
```

It launches only owned processes and private loopback ports. The game replay
runs while a second client requires unauthenticated refusal and repeatedly
decrypts authenticated health replies. At least one authenticated health
reply must complete during the game replay interval. Both fixture handlers
write synthetic disk counters before returning; graceful shutdown must retain
complete matching game/admin logs. Fixture keys are random, temporary and
removed on cleanup. Client MUL files are read in place.
`-Vm` selects an explicit native VM candidate; the receipt records its hash.

The socket fixture has separate synthetic game and admin state. It is not a
persisted shared world, a deployable image, a browser-panel proof or unmodified
client acceptance. Bind the recovered composite state, trusted admin context,
durable action handler and clock advancement before claiming those stages.
