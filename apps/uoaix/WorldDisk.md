# Guest-disk record store

`cites Uoaix chapter WorldDisk` supplies an append-only store over an
explicit disk region. Use a dedicated
shard-state disk or an explicitly reserved region. Client MUL files are
never storage images. Do not change the selected device during a store's
lifetime or run concurrent writers. One caller owns the mutable UoStore.

`us-open start-sector sector-count` uses the currently selected builtin
block device (IDE in the native x86 proof). `us-open-on device start count`
accepts `UoIde` or `UoVirtio state` from `Uoaix chapter WorldBlock`, where
state is a successfully initialized VbxState. Keep that state and its DMA
buffers below every store scratch mark; all stores sharing the state must
operate serially. The format is identical on both backends. Virtio read
errors propagate as Err, and write errors fault the store rather than
allowing commit metadata to advance.
Failed scans/reads can retain the last sector and error wrappers; use an
outer scratch boundary when abandoning a failed operation. Reopening the
store does not reset a faulted virtio queue or authorize DMA reclamation.

Opening validates disk bounds and scans the
committed prefix. The region is at most 131072 sectors (64 MiB), each record
payload at most16777216 bytes. An all-zero initial sector opens an empty
store; a nonzero unrecognized or corrupt initial header is refused, never
reformatted. The returned store contains relative head, latest snapshot
offset (-1 when absent), sequence, tick, tail and fault fields.

Nothing compacts or rotates the log: once `us-append` finds no room it refuses
with "store disk budget exhausted" and the world stops saving. The composite
(`CompositeStore`) appends a full UCC1 snapshot of about 13 MB at every
version upgrade (`cs-upgrade-save`) and small deltas between; Damian's britain5
world held four snapshots (UCC1 9, 10, 12, 14) on 2026-10-06, so a fifth (the
version 15 upgrade, main 36274) does not fit. Compaction is open (UOAIX, blu).

`us-append store kind sequence tick buffer length` accepts kind 1 for a
checkpoint and kind 2 for a subsequent input record. An initial checkpoint
has sequence zero. A later checkpoint must equal the latest input sequence;
an input must advance sequence by exactly one. Tick never decreases. A full
region refuses without advancing head. There is no automatic log rotation,
compaction or disk growth in this unit.

The writer clears the pending commit sector, writes and reads back every
payload sector, writes a zero terminator after the new record when space
remains, and writes the checksummed commit header last. Success returns the
relative record offset. A write/readback failure sets fault; future appends
refuse until the caller reopens and recovers. A failed commit is potentially
ambiguous and must not be retried through stale in-memory world state.

UOS1 uses a 512-byte commit sector followed by the rounded payload extent.
Its first eight little-endian 64-bit cells are magic `31534F55` hex,
version 1, kind, sequence, tick, payload bytes, header FNV32 and payload
FNV32. Header hashing excludes only its own checksum cell. Hashes detect
accidental corruption, not deliberate forgery. Open checks every committed
payload hash and metadata sequence, including superseded checkpoints.

An empty header terminates the scan. A bad header checksum after a committed
prefix returns that prefix with `tail = 1`; callers must report the tail
before resuming appends. A complete committed payload checksum failure,
valid-checksum malformed header or sequence gap refuses open. Torn-header
recovery assumes a single append writer; arbitrary mid-log header corruption
cannot be distinguished from an interrupted tail and is not repaired here.

`us-read store record-offset buffer capacity` returns UoDiskRecord with
kind, sequence, tick, exact payload length and next relative record offset.
Start at store.snapshot, then follow record.next until store.head. A caller
must supply these discovered record boundaries, not arbitrary sectors.
Propagate every error and validate the payload codec before applying it.
The store treats payloads as opaque bytes: compare embedded checkpoint or
event sequence/tick with the returned record metadata. Append admitted
input before acknowledging the corresponding mutation; the game integration
owns admission and application. The WorldDisk proof grades storage and
codec behavior with WorldSnapshot and WorldJournal; its writer assertions
are test code, not the server's mutation/acknowledgement adapter.

Append and read take linear time in payload bytes, using a reusable sector
buffer and per-sector scratch restoration. Opening is linear in all committed
bytes, bounded by 64 MiB. Store metadata is fixed; no in-memory list grows
with log history. The supplied payload/output buffers remain caller-owned.
Daily scheduling, retention/rotation policy, and the combined world,
Townsfolk and TownMind commit remain server integration work.

Run `pwsh -File apps/uoaix/proofs/test-world-disk.ps1 -Kernel seed/Codex.cdx`
from the repository root. The harness creates a new scratch image, compiles
and runs each native entry serially, and reboots a fresh VM against the
same written image. Exact oracles cover the newest checkpoint, later input,
record count and object fields. The host asserts the pending payload bytes
and empty commit sector before reopening. Separate image mutations cover
torn header, committed corruption, foreign initial header and a rehashed
payload whose embedded clock disagrees with its disk metadata.
The receipt records compiler/artifact/disk hashes, PIDs, admission RAM and
normal VM exit evidence; output bodies and stderr remain beside result.json.
No client data enters a fixture.

Readback alone is not host durability: codex-vm's IDE path can retain a write
in RAM after refusing host writeback. The harness therefore requires a
writable scratch image, rejects lost-write diagnostics and restarts the VM.
The IDE proof establishes guest restart persistence under codex-vm.
`proofs/test-world-virtio.ps1` runs the same write/read fixture through
UoVirtio under QEMU, using a complete hashed compilation unit. Three fresh
guest processes grade write, restart, and read-only commit refusal; every
arm also checks propagation of an injected faulted queue. The host confirms
the pending payload bytes and an unchanged read-only image. These are
storage-component proofs. Physical power loss, host cache persistence,
the UEFI shard image, KVM and rented-host acceptance remain stage-D work.

On 2026-10-04, kernel `9752080A0276505E` passed all six required harness
runs in `build-output/uoaix/disk-proof-7/result.json`: write, reboot,
torn-header recovery, committed-corruption refusal, initial-header refusal,
and payload-metadata binding. The receipt preserves each guest PID and
image hash. No append was interrupted during execution; interrupted-tail
states were prepared explicitly and verified before the recovery guest.
