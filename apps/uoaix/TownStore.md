# Combined state recovery for the shard owner

`TownStore.town-store-restore store` returns `Result TownRecovery Text`.
`TownRecovery.state` is the detached `TownState` for the game/admin owner;
`inputs` is the number of suffix frames actually replayed. The caller opens
the bounded disk region first and keeps the device, DMA buffers and store
below the recovery allocation mark. A blank store is refused: initialization
is an explicit caller operation, never a fallback after recovery failure.

Recovery reads the newest kind-1 checkpoint, verifies its metadata against
the outer record, then follows returned record boundaries through kind-2
inputs. Each input must be contiguous and ordered under `TownReplay`.
Successful recovery reaches the committed head with equal tick and sequence
and passes the combined state invariant check. At most 4096 suffix frames
are accepted; checkpoint before that limit. The helper performs no writes.

Publish the returned state only on Ok. On Err, report the diagnostic before
restoring the caller's mark and discard the entire recovery candidate.
An input outcome mismatch may occur after candidate mutation. Existing live
state is never supplied to the helper and remains unchanged on failure.
Keep returned state below subsequent request scratch marks. The internal
suffix helper requires a private decoded candidate and validated store.

Successful text admissions retain their decoded allocations as required by
[TownInput.md](TownInput.md); other per-input scratch is reclaimed. Recovery
retains one maximum-size snapshot buffer and one input buffer alongside the
decoded state. Those buffers remain allocated for the recovered owner's
lifetime. Work is linear in bytes read plus each replayed operation's cost;
there is no per-input whole-state copy. The store is single-owner throughout
recovery, with no concurrent append or device reassignment.

Run `pwsh -File apps/uoaix/proofs/test-uefi-storage.ps1 -Kernel seed/Codex.cdx -CombinedTown`.
The identical initial UEFI image is saved and rebooted under QEMU/OVMF
virtio and codex-vm IDE. Recovery selects checkpoint 9 and replays the
37-input suffix through sequence 46. The oracle covers family/birth data,
post-checkpoint names and personas, world invariants, counters and pending
audit. Blank recovery and head-metadata disagreement must refuse. The host
checks all writes stayed in the data partition.

The proof does not cover interrupted commits or full shard composition.
Game character-slot metadata and admin report/panel state are not part of
TownState. Their durable ownership must be integrated before claiming the
complete server survives restart. Database migration remains governed by
[Database.md](Database.md).
