# Combined world, townsfolk and mind checkpoint

`TownStateCodec.codex` serializes `TownState { checkpoint, townsfolk, mind }`.
The embedded `WorldCheckpoint` retains the world object table, allocator
generations/free-list, tick and input-log sequence. `TfWorld` retains every
town/person field, unused slots, schedules, memory arrays and the entire event
buffer. `TmMind` retains personas, budgets, replay guards, counters and the
entire audit buffer. No native pointer is written to the payload.

## API and ownership

```text
ts-size worldCapacity
ts-encode state outputBuffer outputCapacity
ts-decode inputBuffer actualLength
ts-decode-record inputBuffer actualLength outerTick outerSequence
```

The public encode/decode functions return `Result`, with a diagnostic on
refusal. `ts-size` requires a world capacity in 1 through 16384. The encoder
validates state before writing and returns the exact encoded length. The
destination must be caller-owned storage of the stated capacity and must not
overlap the world table; overlap is refused. Keep source state stable during
encoding. Concurrent writers are unsupported.

Decoding returns detached state, including owned copies of text and arrays.
The input buffer can be released or reused after successful decoding. A failed
decode does not mutate any existing live state, but can allocate transient
state; restore the caller's scratch mark on failure. A successful state must
remain below subsequent request-arena marks. Helpers in `TownStateFields` and
`TownStateValid` require validated buffers/record shapes and are not wire entry
points.

Use `ts-decode-record` with the tick/sequence returned by `WorldDisk.us-read`.
The embedded UWS1 metadata, combined header and outer record must agree.
`WorldCheckpoint.tick` is the server's nonnegative tick value. `TfWorld.hour`
is separately preserved game-hour time; the codec does not assume equal units.
The outer sequence is the committed input-log position, distinct from the
townsfolk event sequence and mind audit sequence.

The output is suitable for a WorldDisk kind-1 checkpoint payload. The codec
does not append, acknowledge, schedule or rotate disk records.
[TownInput.md](TownInput.md) defines ordered simulation-input serialization,
replay and the separate-VM combined IDE restart proof. Live server admission,
log rotation and combined virtio recovery remain integration work.
Keep the snapshot/input-log/notification acknowledgement
checkpoint rules in [Townsfolk.md](Townsfolk.md) and [TownMind.md](TownMind.md).

## TSC1 layout

All integers are signed 64-bit little-endian cells. Text remains Codex Character
Encoding: length cell, that many CCE bytes, and zero padding through the fixed
text capacity. No UTF-8 conversion occurs inside this format. A future
cross-language adapter must implement the declared encoding at its boundary.

| Header offset | Meaning |
|---:|---|
| 0 | Magic `0x31435354` (`TSC1`) |
| 8 | Version 1 |
| 16 | Total payload bytes |
| 24 | Embedded UWS1 byte length |
| 32 | World tick |
| 40 | Committed input sequence |
| 48 | FNV32 over the complete payload, excluding this cell |
| 56 | Reserved zero |
| 64 | Complete UWS1 checkpoint |

The body begins immediately after UWS1:

| Body offset | Extent |
|---:|---|
| 0 | TfWorld used, town-count, hour, event-count, sequence: 40 bytes |
| 40 | Four town slots of 256 bytes |
| 1064 | 128 person slots of 592 bytes |
| 76840 | All 4096 event integers |
| 109608 | TmMind audit-count, sequence, fallbacks, refusals, accepted: 40 bytes |
| 109648 | 128 persona slots of 712 bytes |
| 200784 | All 6144 audit integers |

`TownStateFields.codex` states each slot's scalar order. Towns use 23 integer
cells plus a 64-byte name capacity; people use 25 integer cells, a 64-byte
name, 24 schedule cells and 16 memory cells. Personas use seven integer cells,
512 bytes for description and 128 for fallback. Each text capacity has its
own eight-byte length cell. The fixed body is 249936 bytes. Total payload size
is `250080 + 80 * worldCapacity`, at most 1560800 bytes, below WorldDisk's
4194304-byte payload ceiling. New state fields require a versioned format
change; version 1 does not silently skip unknown fields or versions.

## Validation

Before text decoding, the codec checks complete lengths, format/version,
reserved data, checksum, text lengths and padding. Embedded world validation
is delegated to UWS1. State validation checks slot identities, field ranges,
town population, live vendor references, reciprocal adult spouses, family
cycles, schedules, memory indexes, pending events, persona budgets and audit
counters/outcomes. Living NPCs with nonzero mobile bindings must reference
distinct existing mobile objects. Dead NPCs can retain historical mobile
serials. Unused person/town slots retain default scalar state; unused schedule
cells are range checked because admission overwrites the schedule.

The parent-graph walk uses visited/visiting marks and a depth bound. Mobile
uniqueness uses a world-slot bitmap; no pairwise population scan occurs.
FNV32 detects accidental corruption, not deliberate forgery or authority.
Structural checks still run when a modified payload has a recomputed checksum.
Within-range values are not authenticated by this codec.

## Cost and proof

Validation/encoding/decoding are linear in the fixed state body and world
capacity, plus the existing world's bounded container validation. Text decoding
uses byte lists only for the bounded strings, not for the checkpoint payload.
Caller-owned output storage is excluded from decoder allocation. The three
`TownState` references occupy 24 native bytes. The proof refuses a
maximum-capacity/maximum-text decode that retains more than 8 MiB.

`proofs/TownStateProof.codex` grades every declared TfWorld/TmMind field,
independent wire cells, detached ownership, allocator generations, request
replay guards and pending-event behavior. Recomputed-checksum cases corrupt
clock, lengths, populations, family links, bindings, budgets and audit data.
The maximum fixture covers 16384 world slots and 128 full-size personas.
Use an explicit depot kernel with `build/compile.ps1`, then `build/test-run.ps1`
and compare the complete output to `proofs/TownStateProof.expected`, removing
CR only. The proof is standalone; no full-battery registration is added.
