# Atomic world actions

`WorldAction.codex` is the stage 2 consumer boundary for an ordered set of
WorldRecords mutations. A `WorldAction` carries one sequence, tick, actor,
action kind, target and 1..64 `WorldEvent` values. All nested events carry
the outer sequence and tick. Create events carry the allocator's expected
serial, as in WorldJournal; update and delete carry existing serials.
Actor zero denotes a server action. Kind is a server-owned positive ID.
These fields are audit context, not client authentication or authorization.

`wa-prepare checkpoint action` checks the next sequence and monotonic tick,
then applies the ordered events privately. Failure restores every touched
slot and both allocator header cells, including generations and free-list
links. Success returns a `WorldUndo`. The single world owner must either
call `wa-rollback undo` or, after the encompassing durable commit succeeds,
`wa-accept undo`. Both close the undo object and refuse a second call.
Accept does not advance the checkpoint. After successful durable commit
and accept, the owner must publish a new checkpoint with the action's
sequence and tick and the same world before admitting the next action.

No world read, broadcast, competing write, nested prepare or heap restore
may occur between prepare and close. Keep the world below the request's
scratch mark; keep the action and undo alive until close. The checkpoint
must describe the current authoritative world and sequence. Helpers and
raw records require module-owned valid state. A caller cannot use a stale
checkpoint or hand-built undo as a concurrency or rollback mechanism.

`wa-encode action buffer capacity` writes UWA1. `wa-decode buffer size`
requires the exact complete frame. The frame has eight little-endian cells:
magic, sequence, tick, actor, kind, count, FNV32, target. The checksum skips
its cell at byte 48. The header is followed by one 128-byte UWJ1 per event,
each with its own checksum. Length is `64 + count * 128`, at most 8256.
Checksums detect corruption; they do not authenticate input.

`wa-replay checkpoint buffer size` validates and applies one complete
action, returning the advanced checkpoint on success. A failure leaves
the supplied world byte-for-byte unchanged. Replay mutates that world on
success; restore into a private recovery candidate and publish only after
the complete enclosing log passes. Reclaim per-frame decode and undo
scratch after copying out the returned sequence and tick.

The enclosing shard owner must put the action and every associated change
to character data, economy, coin ledger and trade log in one durable input
record before acknowledging the action. UWA1 is a component of that record,
not a separate database or an independently committed economy journal.
The existing WorldDisk path stays until the Codex DB restart proof passes,
as required by [Database.md](Database.md). This unit does not integrate the
live GameSession, TownInput, character checkpoint or economy transaction.

Undo storage is 16 bytes plus 88 bytes per attempted mutation, with a
40-byte wrapper. Repeated writes to one slot retain successive before
images and restore in reverse order. Codec and rollback work are linear
in the event count. WorldRecords retains its existing parent-depth and
capacity scans; no whole-world copy is added per action. Result, decoded
event and list allocations are request scratch. Counts are bounded before
traversal. No compiler heap or time behavior changes.

`proofs/WorldActionProof.codex` checks a late failure after earlier writes,
repeated slot reuse, allocator restoration, complete-frame admission,
duplicate sequence refusal and the maximum batch on small and maximum
capacity worlds. The proof does not establish a disk commit, physical
power-loss recovery or client acceptance.
