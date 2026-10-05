# x86 virtio network driver

`Kernel chapter VirtioNetX86` uses the [shared modern PCI transport](VirtioX86.md).
`vnx-find pci-scan-all` selects a virtio network device; `vnx-init device`
returns a control-block address or a named error. Modern interfaces on both
modern and transitional network PCI IDs are admitted. Legacy-only interfaces
and devices without the MAC feature or sufficient queues are refused.

The wire header follows [VIRTIO 1.2, section 5.1](https://docs.oasis-open.org/virtio/virtio/v1.2/cs01/virtio-v1.2-cs01.html):
the modern header includes `num_buffers`, even without merged receive buffers.
The driver negotiates MAC and VERSION_1 only. Outgoing offload fields are zero;
incoming offload requests and multi-buffer frames are refused.
In the MAC-only profile, a receive count of zero or one is accepted as the
single completed descriptor. Buffer length and ownership checks still apply;
a count above one is refused.

## Ownership and bounds

The driver is single-owner and polled. Keep the initialized control block,
queue memory and DMA buffers alive below every request scratch mark. No
hot-unplug or reclamation API exists. The 256-byte control block flattens the
two VpxQueue records, plus DMA pointers, MAC, fault and packet/drop counters.
The RX queue has 32 descriptors and the TX queue eight; each RX descriptor
owns one 2048-byte buffer. TX uses one 2048-byte buffer and descriptor zero,
waiting for its completion before reuse. One eight-byte completion buffer is
shared by the serialized operations.

`vnx-send base frame` accepts Ethernet frames of 14 through 1522 octets and
returns 1 only after completion, otherwise 0. Every octet is validated before
publication. TX polling has a ten-million-iteration fuel bound, not a claimed
wall-clock deadline. Timeout, an unowned completion or a malformed used length
faults the driver and prevents further DMA-buffer reuse.

`vnx-recv-into base destination` returns a complete frame's length, or 0 for
no usable frame. A destination too small receives no prefix. Frames beyond the
driver's frame cap, short headers and unsupported offload headers are dropped
and recycled. A completion exceeding its advertised buffer or queue ownership
bounds faults the driver. `vnx-valid base` exposes the fault state; callers
must not reclaim DMA memory after a fault. Reinitialization requires device
reset and fresh ownership, not resetting a software flag.

Empty polls read the used index without allocating. A nonempty receive and a
send construct a temporary queue wrapper, save its updated integer metadata,
and restore the internal scratch mark. Caller buffers predate that mark.
`vnx-recv-frame` allocates a returned frame; hot loops use `vnx-recv-into` with
a retained destination instead. Work per packet is linear in its bounded
length; descriptor/completion work is constant. Initialization allocates fixed
DMA and queue regions. No compiler heap or time behavior changes: the compiler
concatenation excludes NetDriver and these OS chapters.

## NetDriver binding

`NetDriver` selects virtio before e1000 and the NE2000 fallback. Card id 2
means `virtio-net`; the existing MMIO-address cell instead holds the opaque
virtio control-block address for that card. Card id 3 means `unavailable`:
a discovered virtio device failed admission, so no unrelated card is silently
used. Send/receive return 0 and MAC is empty in that state. Callers must check
bring-up's returned card id before advertising a listening service.
For the specific admission reason, use `vnx-find` and `vnx-init` directly,
as `VirtioNetInitProof.codex` does; the generic seam returns only the card id.

The existing e1000 and NE2000 paths retain their behavior. The driver inherits
the shared PCI transport's below-4-GiB MMIO mapping and split-ring limits.
The deployment image, KVM and a rented host require their own exact-artifact
acceptance; QEMU TCG coverage does not establish those outcomes.

## Focused proof

```powershell
pwsh -NoProfile -File apps/uoaix/proofs/test-virtio-net.ps1 -Kernel seed/Codex.cdx
```

The harness runs synthetic queue guards in a serial guest, boots modern,
transitional, legacy-only and absent NIC configurations under QEMU TCG, then
runs the encrypted admin-port conversation through an actual virtio NIC.
The host needs `D:/Program Files/qemu/qemu-system-x86_64.exe` by default;
`-Qemu` chooses another executable. Each run uses a new evidence directory.

`apps/uoaix/test-admin.ps1 -Qemu <executable>` selects that real NIC for the
network leg, supplies ephemeral keys over the guest UART, and retains the
same independent .NET cryptographic and queue oracle. Default runs still use
codex-vm and NE2000. The QEMU child and serial connection are owned by the run
and closed in cleanup.

Measured 2026-10-04 with depot kernel `EF9466BEF7CB5FDA`: queue guards,
modern and transitional PCI-ID assertions, legacy/absent controls and the
encrypted admin conversation passed under QEMU TCG. Evidence is in
`build-output/uoaix/virtio-net-head/`. The e1000 binding/no-address controls,
poll calibration/clamping, receive reuse and NE2000 admin regression passed
under the same compiler; receipts are in `virtio-net-head-regress/`.
The guard measured zero retained heap over 100000 empty polls and zero
retained scratch growth for a nonempty receive and a completed send.
The contract and harness received an independent reader pass. Serial capture
failure cannot skip child cleanup, and transitional admission asserts the
observed PCI ID rather than relying only on a QEMU default.
