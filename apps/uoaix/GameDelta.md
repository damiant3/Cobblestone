# Ordered game delta

`gd-encode undo sequence tick destination capacity` serializes a pending
[GameTransaction](GameTransaction.md) with a persistent change. The caller
retains exclusive ownership and supplies disjoint output storage. It refuses
finalized undo, invalid capture indices, unchanged persistent state or invalid
post-state. A session-only action needs no game delta; its caller still checks
store health before admission and finalizes its undo.

UGD1 is a little-endian before/after format. Its 64-byte header contains
magic `0x31444755`, version 1, length, world capacity, sequence, tick, FNV32
and captured-slot count. The checksum skips its own cell at offset 48.

| Offset | Contents |
|---:|---|
| 64 | Before next relay counter, 16-byte world header and 800 character bytes |
| 888 | After next relay counter, world header and character bytes |
| 1712 | Zero to five entries, each an index, 80 before bytes and 80 after bytes |

The size is `1712 + count * 168`, bounded by `gd-max-bytes`. No native pointers,
pending relay key, active connection or GameSession state are serialized.
FNV detects corruption, not hostile rewriting. The record represents a
trusted prepared action; structural validation does not establish client
consent or authenticate an operator who can rewrite storage and its checksum.

`gd-apply checkpoint game bytes length` is a private-recovery operation.
The checkpoint and game must share the same world. The input buffer must
remain stable and disjoint from world/character storage. The sequence must be
exactly the checkpoint's successor and the tick cannot go backwards.
`gd-apply-record` additionally checks enclosing store-record metadata.

Before writing, replay checks the entire before metadata and every captured
slot against the current owner. Duplicate/out-of-range indices, empty changes,
checksum/extent errors and mismatches refuse without mutation. After copying
the candidate bytes, validation permits only free-prefix allocation or updates
that preserve a live object's serial, kind and container. New serials must be
the exact next generation, counts and free head must match the allocations,
and all character references and captured object fields must remain valid.
A post-state refusal restores the complete before images and leaves commit
metadata unchanged. Only a valid result advances checkpoint sequence/tick.

The initial world must already be fully validated. These constrained
transitions preserve that invariant without a full-table scan per action;
recovery should still validate the complete composite at publication. Callers
must update other composite commit-position holders after a successful game
record, and must not publish any recovered state until the entire mixed suffix
has succeeded. This module does not implement that dispatcher or write disk.

Encoding/replay copy and compare fixed character metadata plus at most five
slots. Object-parent checks retain the existing bounded depth; character
validation is bounded by the five character slots and their equipment.
No whole-world clone, free-list scan or retained per-object wrapper is added.
Error restoration uses the verified before bytes in the input buffer.
Successful replay retains no input-buffer references.

`proofs/GameDeltaProof.codex` covers creation, movement and relay replay,
exact state reproduction, duplicate/sequence/tick refusal, changed checksummed
preconditions, rehashed invalid generations/free head/character padding,
overlap refusal and full input overwrite. It proves the codec and memory
replay, not commit flush, crash recovery or the encompassing shard transaction.
