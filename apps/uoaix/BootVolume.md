# UOAIX UEFI storage volume

`Uoaix chapter BootVolume` locates the unique Codex-facts GPT partition
on a `UoDevice` and returns `UvRegion { start, sectors }`. Pass the region
to `us-open-on`. The reader validates the protective MBR, primary header
CRC, entry-array CRC, selected extent, read-only attribute and overlap
with every other occupied partition. A damaged primary table is refused;
the reader performs no recovery, repair or formatting. GPT fields follow
[UEFI 2.10 chapter 5](https://uefi.org/specs/UEFI/2.10/05_GUID_Partition_Table_Format.html).

The entry array has 1 through 1024 entries with a supported stride of
128, 256 or 512 bytes and at least 16 KiB total. Allocation is bounded
to 512 KiB plus sector/result scratch. CRC work is linear in array bytes;
partition selection and overlap checks are linear in entry count.
Callers may reclaim the boot-reader scratch after copying the region,
keeping the device and DMA buffers below the scratch mark. Failed reads
return errors and require the same caller-owned scratch cleanup.

## Reproduce the component proof

Run `pwsh -File apps/uoaix/proofs/test-uefi-storage.ps1 -Kernel seed/Codex.cdx`.
The harness creates a new evidence directory, expands the complete cite
closure, compiles the storage payload, emits an ExitBootServices PE with
an owned process pool, and builds a 32 MiB GPT/FAT16 image. The blank data
partition receives a synthetic world checkpoint. No EA client files are
read or copied. Supply `-Qemu` for a different QEMU installation; firmware
is taken from that installation's `share` directory.
Guests default to the selected 1 GiB target; `-GuestMemMB` records an
explicit alternative in the receipt. Compilation retains its separate
compiler guest memory budget. A storage-only pass does not establish that
the eventual full shard fits the target RAM.
codex-vm exposes GOP only when the entire advertised framebuffer fits guest
RAM. The 1 GiB path boots without GOP; the PE stub supports that firmware
path. This is headless storage acceptance, not a small-memory GUI claim.

Copies of the same pristine bytes boot under QEMU/OVMF with modern virtio
block and under codex-vm with IDE. Each backend saves, exits, then restores
in a fresh process. The QEMU arm requires the admitted MMIO address above
4 GiB. Exact serial oracles, normal process exits, image hashes and a
host byte comparison enforce completion, restore and writes confined to
the data partition. Each QEMU boot gets fresh firmware variables.
`BootVolumeProof.codex` adds malformed GPT and MMIO range/identity controls.

Add `-CombinedTown` to exercise [TownStore](TownStore.md) recovery of the
combined world/townsfolk/mind checkpoint and ordered input suffix. The
harness bundles the existing restart fixtures into the complete compilation
unit and selects their exact oracles. The image still contains synthetic
data and a terminating proof payload.

The receipt records the compiler and complete source hashes, initial image
hash, guest PIDs, free RAM, exits and final disk hashes. The harness keeps
all images and logs in its new output directory and stops its own timed-out
guests. Host fixture comparison retains two image-sized arrays and runs in
linear time; the guest does not retain those arrays.

## Deployment boundary

The image contains a storage probe, not the game/admin server. The current
proof covers QEMU TCG with the installed OVMF and codex-vm's UEFI model.
KVM, a rented host, network/authentication acceptance, real shard data,
live game state integration and interruption during a commit remain separate
stage-D obligations. The mapping envelope and driver lifetime rules are
in [VirtioX86.md](VirtioX86.md). A successful storage probe does not admit
arbitrary firmware paging layouts or every cloud device configuration.
