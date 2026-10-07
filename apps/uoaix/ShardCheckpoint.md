# One shard checkpoint

`ShardCheckpoint` contains a GameCheckpoint, EconomyCheckpoint and AdminPanel.
USC1 combines UGC1, UEC1, UAC1, UPA1 and the shared GameClock state into one
payload suitable for one WorldDisk kind-1 record. It adds no storage backend.
The current maximum world capacity still fits the store's 4 MiB payload
budget; the native proof checks that relation.

`shc-encode state buffer capacity` requires one exclusive, stable owner.
Game/town and economy tick/sequence must agree, and townsfolk/economy game
hours must agree. The panel's AdminWorld must reference that same townsfolk
and mind, and the panel must hold the one scheduler clock. These native
reference/provenance requirements belong to the caller; matching NPC IDs
alone does not establish shared ownership. Supply output disjoint from
every source record, list, text and buffer. Failure can leave partial output;
append nothing unless the complete encode returns Ok.

`shc-decode buffer actualLength newRuntimeTick trustedActor gmBackend`
returns a private reconstructed owner. `shc-decode-record` takes outer tick
and sequence before the last three arguments. All embedded component
metadata must match the envelope and outer record. Every component runs its
own validation, and game hours must still agree. AdminWorld is explicitly
bound to the decoded townsfolk/mind; panel audit is bound to that AdminWorld
and the reconstructed clock. No source buffer pointer is retained.

The clock retains day-seconds, phase and due hours. Its last-tick becomes
the supplied new runtime tick. No offline elapsed time is inferred. The
committed game tick and sequence retain their original values; the live
owner must continue their monotonic ordering after reboot. Persist the
prospective post-consumption due count when committing a game hour, as
[GameClock.md](GameClock.md) requires.

Game connections and pending relay keys reset. Panel sessions/connections
reset, and a queued panel action becomes cancelled in its retained audit.
The GM backend and current actor are trusted boot bindings, not checkpoint
content. Protocol keys and epochs are excluded. Deployed GM mutations remain
disabled until the authoritative database backend exists.

Publish only a successful whole candidate. On failure, report its error then
reclaim the caller's entire recovery scratch; already decoded components are
not independently publishable. Retain successful state and supplied backend
below later request scratch. Encoding/decoding are linear in the component
bytes plus their bounded validators. The wrapper adds no whole-state copies
beyond component decode allocations and no per-object data structure.

## USC1 layout

| Offset | Content |
|---:|---|
| 0..63 | Magic `0x31435355`, version 1, size, UGC1 size, tick, sequence, FNV32, reserved zero |
| 64 | Game-day seconds |
| 72 | Fractional hour credit |
| 80 | Due game hours |
| 88 | Reserved zero |
| 96 | UGC1, then fixed UEC1, UAC1 and UPA1 payloads |

Sizes come from the owning codecs. Integer cells are little-endian; nested
text retains CCE. FNV32 skips only the outer checksum cell at 48; nested
checksums remain inside its coverage. Format versions, lengths, padding,
clock bounds and component positions are checked before publication.

`ShardFixture.codex` creates synthetic proof state, not production shard
initialization. `proofs/ShardCheckpointProof.codex` covers a nonempty NPC
world, standard economy catalog, reports/jobs, queued audit cancellation,
clock rebasing, shared references, complete input reuse, guard bytes,
corruption and rehashed component/clock disagreement. Compare complete
normal and poisoned outputs with `ShardCheckpointProof.expected`.

The codec does not commit actions, replay a mixed input log, drain bounded
audit/storage capacity or join listeners. Component TownStore, GameStore and
EconomyStore restore their own checkpoint/input formats and cannot directly
read USC1. The encompassing shard owner must use one compatible input format
and one commit before acknowledging a mixed action. Disk kill/restart,
network admission and chosen-host acceptance remain integration work.
