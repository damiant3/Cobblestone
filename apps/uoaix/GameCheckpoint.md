# Stage-1 game checkpoint envelope

`GameCheckpoint` wraps the combined TownState checkpoint and all 800 bytes
of GameShard character slots. The stage-D owner uses the envelope while the
database backend is pending. Reek owns later gameplay format changes; the
database may replace the envelope only after equivalent restart acceptance.

`gc-encode town game buffer capacity` requires the game and town to share
one world table and returns the encoded length. `gc-size worldCapacity`
returns the required destination size. The caller supplies disjoint owned
output storage and keeps the source stable during encoding. Character-slot
and world-table overlap is explicitly refused. Other TownState allocations
must also remain outside the output buffer.

`gc-decode buffer actualLength` returns `GameCheckpoint { state, game }`.
The decoded game and town share the same detached WorldTable. Character
slots are copied; the input buffer can be reused. Failed decoding may
allocate a partial candidate: report the error then reclaim the caller's
scratch, without publishing the candidate. Use `gc-decode-record` with the
outer disk record's tick and sequence; both must match the envelope and
embedded TSC1 metadata.

The next relay-key counter survives. Pending relay keys, live sessions and
the active mobile do not survive a boot. Authentication configuration is
outside the format. Character names, creation packets and equipment serials
survive through the fixed slots. The stored creation record is 104 bytes: the 100-byte 1.25.32
wire packet plus four server-owned clothing-hue bytes. Legacy wire requests
normalize those hues to zero; existing normalized records retain their hues.
This leaves the UGC1 layout unchanged. Validation rejects duplicate character
mobile references, missing/wrong-kind mobiles, invalid creation packets,
equipment outside the owning mobile, nonzero unused slots and reserved
padding. Slot byte 145 is stamina spent, at most the dexterity at byte 80
(0 = full). UOX3 1998 rules: a point returns every 2 s, running spends a point per 15 steps,
a swing 2, and an overloaded step 5 (carried weight over strength x 3.5 + 40 stones,
`cp-overload`; about 97 transient heap bytes per carried item per step, reclaimed by
`sl-compact`); at 0 a step is refused (1382, or 1783 when overloaded). Slot byte 144 is the testing-supplies grant flag (0 or 1, set by
`CompositeTestStock`); byte 146 counts running steps (under 15), bytes 152..159 hold the stamina tick, and bytes 147..151 must be zero. The existing stage-1
single-account contract remains unchanged.

## UGC1 layout

| Offset | Content |
|---:|---|
| 0 | Magic `0x31434755` |
| 8 | Version 1 |
| 16 | Total payload bytes |
| 24 | Embedded TSC1 bytes |
| 32 | Tick |
| 40 | Input sequence |
| 48 | FNV32 excluding this cell |
| 56 | Next relay-key counter |
| 64 | Five 160-byte character slots |
| 864 | Complete TSC1 checkpoint |

Integer cells are little-endian. Creation-packet bytes keep their wire
encoding; TownState text remains CCE. FNV32 detects corruption and is not
authentication. The entire envelope fits WorldDisk's payload budget.

Slot validation has fixed work over five slots and four equipment references
per slot. Temporary packet lists and lookup results are reclaimed per slot.
Encoding adds one linear copy/hash around the existing TSC1 work. Decoding
retains TSC1's detached state, 800 copied slot bytes and fixed wrappers.
No per-object allocation beyond the underlying codec is introduced.

`proofs/GameCheckpointProof.codex` checks shared world identity, detached
slots, reset connection/relay state, nonempty world fields, outer metadata,
truncation, corrupted/rehashed references, reserved padding and overlap.
Compare its complete serial output with `GameCheckpointProof.expected`.
The proof is a native codec proof. Disk append ordering, a complete live
game/admin server, admin/panel durability and kill/reboot acceptance remain
stage-D integration work. TownStore reads TSC1 plus TSI1; UGC1 requires an
explicit envelope-aware caller and must not be passed to TownStore.
