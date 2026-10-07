# Composite checkpoint storage

`ShardStore` saves and restores USC1 through the existing WorldDisk backend.
Open the intended bounded region first. Keep the store, device/DMA allocations,
live state and supplied GM backend below operation/recovery scratch marks.

`shard-store-save store state` requires equal owner/store tick and sequence,
encodes the complete [ShardCheckpoint](ShardCheckpoint.md), then appends one
kind-1 record. Initialization is explicit and uses the blank store's initial
commit position. A successful save reclaims internal encoding scratch.
On error, consume the diagnostic before reclaiming caller scratch. An I/O
error may make the commit outcome ambiguous: stop admission and recover;
never acknowledge or blindly retry after a failed commit/readback.

`shard-store-restore store newRuntimeTick trustedActor gmBackend` returns a
private complete candidate. The latest checkpoint must end at the committed
store head and match its metadata. **A following committed input is refused**
until the encompassing mixed-input replay path exists. Recovery does not
silently publish the checkpoint while ignoring later state. A blank/faulted
store also refuses. Publish only Ok; report and discard all failed recovery
allocations. Successful recovery retains its maximum input buffer alongside
the decoded state. Reads do not write to the disk.

This checkpoint-only API is not the live action-commit path. GameStore,
TownStore and EconomyStore are component examples; one shard owner must
compose all affected state in one transaction and replay format before
acknowledging mixed game/economy/admin actions. Existing bounded log capacity
and audit backpressure remain unchanged.

## Kill and restart proof

```powershell
pwsh -File apps/uoaix/proofs/test-shard-restart.ps1 -Kernel seed/Codex.cdx -Nasm C:/Tools/nasm/nasm.exe -OutDir build-output/uoaix/shard-kill-proof
```

Use a new output directory. The harness expands and hashes complete BIOS/UEFI
compilation units, builds the GPT image and dual-boot ISO, then tests at 1 GiB:

- codex-vm UEFI with IDE;
- QEMU TCG UEFI disk boot with modern virtio;
- QEMU TCG BIOS ISO boot with modern virtio;
- QEMU TCG UEFI ISO boot with modern virtio.

Each writer first proves owner-metadata and blank-recovery refusals, commits
one synthetic USC1 checkpoint, prints its commit marker and remains alive.
The host requires that live state, kills the owned writer process, then boots
a fresh reader. Exact oracles check game records, catalog/economy, report/job
queues, audit cancellation and clock rebasing. The ISO modes have their data
disk ESP cleared, and OVMF must identify DVD boot. All backends must produce
the same committed data-partition hash; readers must leave their disk hashes
unchanged and all writes must remain inside the data partition.

A host-built, checksummed kind-2 suffix advances the committed sequence.
The final negative reader must refuse that suffix and leave its disk unchanged.
The receipt records compiler/VM/source/image hashes, each PID, free RAM,
exit and kill evidence. `run.json` identifies the current owned guest. The
harness stops its own guests on failure and retains artifacts and logs.

The kill follows a completed commit. This does not simulate interrupted
sector writes, physical power loss or an unacknowledged mixed action. No EA
files, protocol keys or real player data enter the fixture. The images still
contain a storage proof, not the composed game/admin server. KVM, Vultr and
network acceptance remain separate requirements.

Save/recovery costs are linear in checkpoint bytes plus existing component
validation. There is no per-object wrapper or per-action full-state clone.
The host harness retains whole-image arrays and performs linear confinement
and hash checks; the BIOS loader uses its existing bounded scratch.
