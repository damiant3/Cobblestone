# x86 modern virtio substrate

`Kernel chapter VirtioPciX86` provides the PCI transport shared by block
and NIC drivers. `Kernel chapter VirtioBlkX86` supplies synchronous
512-byte block reads, writes and negotiated flushes. The ARM64 chapters
remain separate. The wire layouts follow [VIRTIO 1.2](https://docs.oasis-open.org/virtio/virtio/v1.2/cs01/virtio-v1.2-cs01.html),
sections 4.1, 2.7 and 5.2.

The [x86 network driver](VirtioNetX86.md) uses the same transport and owns
its NIC admission, DMA buffers, NetDriver binding and QEMU network proof.

The x86 transport admits firmware-assigned MMIO BARs at or above 3 GiB,
including 64-bit BARs, within the CPU physical-address limit and lower
canonical address range. `MmioMapX86` requires existing writable identity
mappings with an effective uncacheable memory type. The check reads PAT
and, when needed, variable MTRRs; the check changes no page tables or
cache settings. Each capability window is bounded to 1 MiB. The supported
runtime/OVMF envelope uses four-level paging and identity-accessible page
tables below 3 GiB. Other paging environments are outside the contract.
Both BAR words and the command register are restored after
sizing. Capability traversal is bounded; capability extents must fit their
BAR. Legacy-only interfaces, packed rings, EVENT_IDX and feature words
beyond VERSION_1 are outside this transport's contract.

## Transport API for device drivers

First, select the device ID required by the device driver; vpx-open checks
the virtio vendor and capabilities, not whether the device is a NIC or disk.
Use `vpx-open pci-device` to obtain VpxDevice or a named error, and check
config-size before every device-specific configuration layout access.
Then, `vpx-reset device` resets and returns VpxFeatures (low/high offered
words). `vpx-accept device wanted-low 1` negotiates the selected features;
the high word 1 requests VERSION_1. Do not use a device after any refusal.
Call `vpx-queue device queue-index queue-size` for each required queue,
using a power-of-two size from 1 through 256 no larger than the offered
size. Finally, call `vpx-ready device` after all queues are configured.

VpxQueue exposes desc, avail, used, size and notify addresses, index,
avail-index, last-used and fault. The transport owns queue metadata;
the device driver fills descriptors and owns every DMA buffer. A queue is
single-owner, with no concurrent writers. Allocate the device, queues and
DMA buffers below all request scratch marks and keep them alive throughout
device use. This unit provides no hot-unplug or reclaim operation.

`vpx-publish queue head` publishes a prepared chain and notifies the device;
0 succeeds and -1 refuses a bad head, faulted or full queue. Each descriptor
must be owned by exactly one outstanding chain. `vpx-take queue output8`
returns 0 for no completion, 1 after writing a little-endian descriptor ID
and used length to output8, or -1 on a fault. Counts and ID range are
checked. The caller must check that the ID belongs to an outstanding chain
and that the used length fits the buffers advertised by that chain.
The transport does not maintain a descriptor ownership bitmap.

Publication and completion use explicit x86 memory fences. Polling and
publication allocate no heap. A queue reserves 8192 bytes plus alignment
slack at sizes up to 128, or 12288 plus alignment slack at size 256; the
available and used regions remain disjoint. Queue wrappers are fixed-size.
Initialization probes at most 48 capability links per requested region.
MMIO admission walks at most 257 pages per capability and at most 32
variable MTRRs per page. The walks allocate no per-page heap; admission
returns a fixed Result wrapper. BARs of 4 GiB or larger are refused.

## Block API

`vbx-find pci-scan-all` discovers a modern or transitional PCI block device
with modern capabilities; `vbx-init pci-device` admits an explicit device.
The VbxState contains capacity in 512-byte sectors, readonly and flush
feature flags, one eight-entry queue and fixed request/data buffers.
`vbx-read state sector destination512` returns Result Integer Text and
copies data only after a successful complete response. `vbx-write state
sector source512` writes and issues FLUSH when offered and negotiated.
Without FLUSH or CONFIG_WCE negotiation, the virtio contract supplies a
writethrough cache; CONFIG_WCE is never negotiated by this driver.

The block state is single-owner with one synchronous request at a time.
Bounds and read-only refusal precede publication. Completion ID, used
length and status are checked. Polling has a 10000000-iteration fuel cap,
not a claimed wall-clock timeout. A timeout or malformed completion faults
the queue; do not reclaim or reuse its DMA memory after a fault. Results
allocate fixed wrappers; reclaim request scratch only after results have
been consumed, preserving the driver and caller buffers below the mark.
Transfers use one reusable 512-byte data buffer and constant work per sector.

## Proof and deployment boundary

`pwsh -File apps/uoaix/proofs/test-virtio-block.ps1 -Kernel seed/Codex.cdx`
runs the native queue oracle and five real QEMU device boots: write,
fresh reboot, read-only, absent device and legacy-only refusal. A direct
host check compares every written byte; the reboot reads the pattern before
writing again. The read-only arm requires an unchanged image hash.
The proof creates synthetic data only and retains hashes, PIDs and exits.

Measured 2026-10-04 with kernel `C1D0003E5F380465`, QEMU 11.0.0 under TCG:
seven native queue checks and all five device boots passed, including
negotiated FLUSH and persistence into the next VM process. Receipt:
`build-output/uoaix/virtio-proof-2/result.json`.
WorldDisk's virtio binding and storage-component proof are described in
[WorldDisk.md](WorldDisk.md). Booting the actual UEFI shard image,
NIC integration, port/authentication acceptance,
KVM and the rented-host acceptance remain stage-D work. No generic claim
that every cloud's BAR placement or device feature set is supported follows
from the QEMU configuration used here.

[BootVolume.md](BootVolume.md) owns the UEFI storage-image proof, including
OVMF's high MMIO window. The storage payload does not compose game/admin
listeners and does not establish full stage-D acceptance.
