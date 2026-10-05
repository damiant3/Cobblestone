# World checkpoints and journal replay

The codecs supply bounded binary snapshots and in-memory replay.
[WorldDisk.md](WorldDisk.md) owns the append-only guest-disk store and
separate VM restart proof. [TownStateCodec.md](TownStateCodec.md) supplies
combined world/townsfolk/mind snapshot serialization. Daily scheduling,
coordinated snapshot commit and ordered simulation-input replay remain
integration work.
Codec tests alone do not establish durable-save or crash-recovery behavior.

`cites Uoaix chapter WorldSnapshot` provides `WorldCheckpoint { world,
tick, sequence }`. Tick is nonnegative game time; sequence is the last
committed input-log position represented in the snapshot. The table is
owned under `WorldRecords.md`'s single-writer rules.

Allocate `ws-size world.capacity` bytes and call
`ws-encode checkpoint output-buffer output-capacity`. The output buffer
must not overlap the table. Propagate Err; success yields the exact byte
count. `ws-decode buffer actual-length` validates before returning a detached
WorldCheckpoint. Preserve capacity, generations, tombstones and free-list
order; reconstructing only live objects changes later serial allocation.

UWS1 is explicitly little-endian for the x86-64 guest. The 64-byte header
contains eight 64-bit cells: magic `31535755` hex, version 1, capacity,
tick, sequence, table byte count, FNV32 checksum and reserved zero. The
table follows with its 16-byte header and 80-byte slots. The checksum skips
only its own eight bytes. FNV32 detects accidental corruption and is not
an authentication claim. The decoder also checks identity namespaces,
slot/generation correspondence, field bounds, containers, live count and
complete acyclic free-list coverage, including recomputed-checksum inputs.

`cites Uoaix chapter WorldJournal` provides `WorldEvent { sequence, tick,
operation, object }`. Operations are 1 create, 2 replace, 3 delete. Create
carries the actual allocated serial; replay checks allocator agreement.
Delete uses the object's serial. The remaining delete fields are ignored.
`wj-encode event buffer capacity` writes exactly 128 bytes. Each UWJ1 frame
contains magic `314A5755` hex, sequence, tick, operation, ten WorldObject
cells, FNV32 and reserved zero. The frame checksum excludes its own cell.

`wj-replay checkpoint buffer actual-length` accepts only events *after*
the supplied checkpoint. Sequence must increase by exactly one and ticks
must not decrease. Replay applies to a detached table and leaves the
original unchanged on success and failure. Success returns
`WorldReplay { checkpoint, applied, tail-bytes }`; tail-bytes reports an
incomplete trailing frame, which is not applied. A complete corrupt frame,
gap, duplicate or invalid operation refuses the replay. Caller recovery
policy must account for any nonzero tail before appending again.
The bounded replay accepts at most 16384 frames (2097152 bytes).

The public boundaries are ws-encode, ws-decode, wj-encode and wj-replay.
Other helpers require validated capacities, record counts and addresses;
they are not untrusted byte entry points. Caller buffers remain owned by
the caller. Failed decoding/replay can allocate transient wrappers and a
replay table; reclaim the caller's scratch mark on failure. Retain a
successful restored table below the next scratch mark.

Checkpoint size is `80 + capacity*80`, at most 1310800 bytes. Encoding and
hashing are linear in bytes. Validation visits capacity slots and at most
64 ancestors per live object; free-list traversal is bounded by capacity.
Decode allocates a detached table plus fixed wrappers. Replay copies the
table once, then restores its per-event scratch mark; retained heap does
not grow per event. Operation costs remain those in WorldRecords.md,
including capacity scans for deletion and containment changes. No byte-list
conversion or growing history collection occurs.

Focused proofs are `proofs/WorldSnapshotProof.codex` and
`proofs/WorldJournalProof.codex`. Compile each separately with an explicit
depot kernel and compare the entire filtered runtime output with the
matching `.expected`, removing CR only. The snapshot proof includes
rehashing structurally invalid data; the journal proof includes a failure
after a valid prefix and checks that the original world stays unchanged.
The large cases use fabricated records and events, never client data.

Use `build/compile.ps1 -Src <proof.codex> -Out <proof.cdx> -Log <compile.log>
-Kernel seed/Codex.cdx`, followed by `build/test-run.ps1 -Kernel <proof.cdx>
-OutFile <proof.actual>`. The canonical runner removes VM HEAP/WD/STACK
telemetry and normalizes the final newline. Compare `.actual` with
`[IO.File]::ReadAllText(<absolute expected path>) -replace "\r", ''` using
case-sensitive exact equality; a zero runner exit alone is insufficient.
On 2026-10-04, kernel `9752080A0276505E` passed the 15-line snapshot oracle
and 13-line journal oracle, including the 8192-object/event cases.

The WorldDisk adapter commits payload before commit metadata. Server
integration must acknowledge mutations only after logging. A restart must restore
the snapshot with its input-log position and replay later inputs in order.
TownStateCodec preserves all people/town fields, schedules, memory rings,
pending events and mind audit state beside UWS1. Its record-aware decoder
compares the embedded tick/sequence with WorldDisk metadata. Lifecycle
notifications alone are not sufficient replay inputs.
[TownInput.md](TownInput.md) defines the ordered simulation inputs and proves
combined checkpoint plus suffix recovery across separate IDE VMs. Live server
commit admission, log rotation and combined virtio recovery remain unfinished.
The [database migration](Database.md) retains the existing path until equivalent
restart acceptance passes.
