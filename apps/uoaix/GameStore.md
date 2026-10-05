# Game checkpoint and world-action store

`GameStore` composes [GameCheckpoint](GameCheckpoint.md),
[WorldAction](WorldAction.md) and the existing [WorldDisk](WorldDisk.md).
One UGC1 checkpoint contains the character slots and shared world/town/mind
state. Each subsequent kind-2 disk record contains one complete UWA1 world
action. No new storage backend or parallel state store is introduced.

Construct `GameStoreState { checkpoint = savedGameCheckpoint, inputs = 0 }`
only for initial state, then call `game-store-save store owner`. The store
must already be opened on the intended dedicated disk region, with matching
sequence and tick. Initialization is explicit; a recovery failure never
creates a blank world. On restart, `game-store-restore store` returns a
private reconstructed owner. Publish that owner only on `Ok`.

`game-store-commit store owner action` encodes and privately applies the
entire world batch, validates character/equipment and live NPC references,
then appends one disk record. A refused append rolls back all world writes;
an accepted append closes the undo and advances the owner's sequence, tick
and suffix count. A broken reference likewise rolls back the whole batch
before disk mutation. Store metadata and owner metadata must agree before
every save or commit. These APIs require one trusted, exclusive owner with
valid module-owned state; IDs and action fields do not authenticate clients.

Keep the owner, disk device and action below the operation's scratch mark.
Successful save/commit calls reclaim their internal scratch; recovery
retains its candidate and input buffers. On `Err`, report the
diagnostic then reclaim caller scratch. An I/O error can leave `store.fault`
set: stop admitting writes and reopen/recover before another action. A
commit-header readback failure is ambiguous, so do not retry the action
blindly. Recovery determines whether that complete record committed.

`game-store-save` appends a new complete checkpoint and resets the suffix
count. At 4096 inputs, further commits refuse until a checkpoint succeeds.
Do not reset `owner.inputs` yourself or manufacture an owner around an
already-populated log. The existing disk-region capacity still bounds the
append-only log; this API does not rotate, overwrite or discard history.

Recovery binds both checkpoint and input metadata to their outer disk
records, selects the newest checkpoint and requires every following input
to be complete and contiguous. Per-action scratch is reclaimed during
replay. Character connections and pending relay keys reset as specified by
GameCheckpoint. A failed recovery candidate is discarded, never published.

This path commits **world-only actions**. Character-slot, relay-counter,
town, mind and economy mutations after the checkpoint are not UWA1 inputs.
TSI1 and UEC1 are not accepted as suffix frames. Mixed actions must gain one
encompassing input format before they can use this path; never acknowledge
a trade after committing only its physical items. The live GameSession,
economy, title/crime metadata and player-to-player trade log remain separate
integration work. The snapshot/journal path remains until the Codex DB
backend passes equivalent restart acceptance under [Database.md](Database.md).

Per commit, encoding and undo are bounded by 64 events; reference checks
scan five character slots and at most 128 NPC bindings. WorldRecords retains
its existing capacity/depth scans. There is no whole-world copy per action.
Checkpoint work is linear in encoded state size. Recovery retains one
maximum-sized UGC1 input buffer and one bounded action buffer alongside the
decoded state, with one final full-state validation. No compiler heap/time
behavior changes.

Run from the repository root, supplying a new output directory:

```powershell
pwsh -File apps/uoaix/proofs/test-game-store.ps1 -Kernel seed/Codex.cdx -OutDirectory build-output/uoaix/game-store-normal
pwsh -File apps/uoaix/proofs/test-game-store.ps1 -Kernel seed/Codex.cdx -OutDirectory build-output/uoaix/game-store-poison -Poison
```

The harness writes a fresh synthetic disk in one guest and restores it in
a separate guest. Exact oracles cover split/merge conservation, refusal
rollback, equipment and NPC references, checkpoint selection, suffix
replay, connection reset and an uncommitted payload with no commit header.
The reader must leave the disk hash unchanged. The incomplete payload is
a constructed interruption fixture, not a physical power-loss proof. This
unit does not establish live-client durability or a whole-shard transaction.
