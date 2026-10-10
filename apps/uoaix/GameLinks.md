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

Ports are 2593 and 2594 entries, at most eleven. Reserve spare slots for login
and relay connections in addition to active players. A 2594 slot serves HTTP
through the handler `gl-bind-web` installs (no handler refuses every request),
closes after each response, and never reaches the game's disconnect callback. It does not
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
4096 packets and65536 encoded bytes. Slot receive buffers remain8192 bytes.

EOF, refusal and logout call the disconnect hook once. Failure of that hook
latches an owner fault and stops the loop. A world tick answering a negative
value does the same; a tick answering Err logs `FAIL world tick failures=` (the
first three, then every 60th) and ticking continues. Each minute the tick logs
`WATERMARK heap-used= heap-peak= heap-ceiling= heap-peak-pct=` (warn level at 80%; the
peak is sampled once a second, the ceiling is RAM less the demand-stack reserve) and
`stack-used= stack-peak= stack-reserve= stack-peak-pct=` (`__stack-save` sampled once a second
at the loop's depth, against the 64 MiB demand-stack reserve; warn at 80%, either line). Other packet refusals close
only their connection. Graceful close retains queued output;
terminal slots are reused only after the outbox is flushed. Idle-link expiry
retains ShardServer's30000-PIT-tick traffic bound.

## Composite work

Each connection borrows its own
`GameClientView` into `state.view` for one callback (`cs-borrow`, pool reserved
by `cs-views` before serving, least recently used reused), graded by
`proofs/CompositeViewsProof.codex`. Each session also lends its own account
and game-login flag for its callback (`gl-lend`, recorded by `gl-adopt` after a
game login), because `GameShard.characters` holds one account's slots; graded
by `proofs/GameLinksAccountsProof.codex`. `CompositeGameServer` serves through
`gl-serve` with `cs-views` 8.

Every per-viewer cache is per connection, eight rows, and a new connection takes
the row with the lowest connection number; a reassigned row resets, so choosing a
live row costs a redraw and nothing else. Accepts number connections from 0 up
and `cg-rebind` (the death answer's re-entry) from -1 down, so an empty row that
a rebind id can match must still reset on its serial or actor check, or hold a
value no id takes (`cb-seen-none`):
`WorldDecoration` `WdViewer` (each row owns its visible and shown buffers; a door
toggle invalidates that door in every row), `BritainShops` `BbView`,
`GameMoongates` `GmgViewer`, `GameNpcSpeech` `rooms` (the five slot rows keep the
persisted greeting identity), `TownLiveState` `TlViewer`, `TownLiveShow` rows,
`GameResurrection` `GrView`, `GameCombat` `seen`, and one bit per row in the
`seen` mask of `GameMonster` and `GameWorldSpawn` slots. `gvd-pulse` uses the
session's own vendor player row. Player speech reaches every other connection
within 18 tiles once, never the speaker (`CompositeEvents`, a 64-event ring with
buffers below the serving mark). Shared immutable decoration and durable door
state stay common.

Graded natively by `CompositeViewsProof` (two boots) and the `DecorationReplay`
two-connection arm, and over sockets by `proofs/test-composite-links.ps1`: two
accounts on one guest (host port derived from the workspace, never 2593; a fresh
world disk, since a world saved under another spawn catalog refuses to boot; a
copy of an installed disk with its first 64 MiB, the world journal, zeroed is
byte-identical to `install-map-cache.ps1` run on a new file with the same client
and DWD),
each draws the other, no packet repeats in 1.5 s while neither player acts, each
sees the other walk, speak and enter war mode. A fight reaches the other player too
(`GameCombat.md`, "Swings seen by every player in range"), graded natively.
`-Corpus <file>` (`proofs/refusal-corpus.txt`, UOAIX-48) replays each refused live stream after
the two-player checks, one line `name|login or game|account|password|plaintext hex`, and
requires alpha's ping answered and the guest up after every entry.
Open: the same over sockets, which needs a monster (the server refuses an attack
on another player).

`cg-bind` gives the first character installation actor 1 and every later
character its own kind-3 purse (owner = mobile serial) and economy actor, with
no starter money. The player table holds 64 rows (`gv-player-limit`), a deleted character releases its row, and a creation that cannot
bind is refused before any world change (UOAIX-15).
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
