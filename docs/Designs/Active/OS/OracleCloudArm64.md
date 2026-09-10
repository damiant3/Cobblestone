# Codex ARM64 Bare-Metal on Oracle Cloud

> **DEFERRED 2026-08-18 (Damian): "basically a dead project, you can defer
> that."** The local halves are closed. The open items below (the
> five-connection ceiling, the serve loop's heap growth, and the OCI-account
> work) are shelved with the design, not being worked. Left in `Active/` per
> the lifecycle rule (not shipped, not superseded); revive only on Damian's
> word.

## Goal

Boot Codex as the OS on Oracle Cloud Infrastructure (OCI) free-tier ARM
Ampere A1 instances using "Bring Your Own Image" (BYOI). The site serves as
a showcase: "this website is served by Codex on bare metal", game store,
code browser, everything running on the Codex kernel with zero Linux.

OCI ARM VMs boot via UEFI and require VirtIO drivers (virtio-blk for disk,
virtio-net for networking). The TCP/IP stack, HTTP parser, and web server
routing are pure Codex and architecture-independent; only the hardware
drivers need swapping.

## OCI free-tier target

- 4 ARM Ampere A1 cores (Neoverse N1, ARMv8.2)
- 24GB RAM
- 200GB block storage
- 10TB/month bandwidth
- KVM hypervisor, paravirtualized (VirtIO-PCI)
- UEFI boot, custom QCOW2 or raw images

## What exists

The ARM64 lane boots and serves under QEMU + edk2. `build/build-arm64-img.ps1`
builds the image (`-Qcow2` converts beside the raw one, 32 MB virtual and
640 KB on disk) and `build/boot-arm64.ps1` boots it; `boot-arm64.ps1` runs
QEMU in the foreground with no timeout, so it cannot serve as a test arm.

| capability | chapter |
|---|---|
| ARM64 PE32+ writer, UEFI entry stub, 4-level page tables | `codex/plugs/pe/Arm64PeWriter.codex` |
| GICv3, generic timer | `codex/os/kernel/Gic.codex`, `Arm64Timer.codex` |
| ECAM PCI, VirtIO-PCI transport | `codex/os/kernel/Arm64Pci.codex`, `VirtioPci.codex` |
| VirtIO-net, VirtIO-blk | `codex/os/kernel/VirtioNet.codex`, `VirtioBlk.codex` |
| ARM64 NetIO, drop-in for `NetIO.codex` | `codex/os/net/Arm64NetIO.codex` |

Probes: `codex/test/arm64-virtio-tx-probe.codex` (TX to the wire),
`codex/test/arm64-virtio-blk-probe.codex` (sector 0 read, graded on content
`0xEE`/`0x55`/`0xAA` from the GPT protective partition rather than on a
pointer), and `codex/test/arm64-web-server.codex` (the served site).

`docs/Probes/arm64-two-requests.ps1` is the connection harness: it builds
with `-NoBoot`, launches QEMU itself on a hostfwd port derived from the
workspace (a fixed port answers another agent's guest, L-SHARED), banks three
channels (guest serial, QEMU stderr, per-request client verdict) because a
crashed guest and a timed-out client are indistinguishable from the client
alone, and refuses to report request results unless the guest announced
`Listening on port 80`. Its `$Repo` is set to the workspace that wrote it;
set it to yours.

## Contracts

**The DMA floor is published at runtime, never fixed.** The stub writes
`align-up(stack-top, 2 MB)` to `a64pe-dma-floor-cell` (`#40004000`, low RAM
below the kernel image, mapped RW by the stub's own page tables) right before
the kernel jump (`a64pe-stub-publish-dma-floor`). `VirtioNet` and `VirtioBlk`
derive every DMA region from `peek-32 #40004000 0`. A fixed floor is what a
raised heap grant silently overruns: DMA regions landing inside the kernel's
own allocation corrupt whatever they hit, and the observed symptom was a
single CCE table entry, therefore one wrong character in a request path and
every other character correct. `build/build-arm64-img.ps1` carries the wiring
check: the stub publishes, both drivers read, the three cell addresses agree,
and an unmatched arm is exit 8.

**PCI MMIO must be mapped Device-nGnRnE, and PMD entry 0 must not be.**
`a64pe-stub-fill-pmd-device-range` maps PMD0 entries 128-503
(`0x10000000-0x3EFFFFFF`) with `AttrIndx=1`. Entry 0 stays Normal
deliberately: the page tables live at `0x70000` and a translation table walk
through Device memory is CONSTRAINED UNPREDICTABLE. QEMU under TCG models no
caches, so a cacheable MMIO mapping is invisible here and dead under KVM,
which is what OCI runs.

## Open items

- **A ceiling around five connections in one boot**, and the cause is
  localised to the TCP layer rather than to virtio or the heap. Connections
  0-4 answer `HTTP 200`; loop 5 sits in `SYN_RECEIVED` and loops 6+ in
  `LISTENING`. `arm64-net-io-accept` exhausts `arm64-net-io-max-polls` before
  the client's final ACK, `ws-serve-one` then calls `arm64-net-io-listen`
  unconditionally and `tcp-fresh-listener` throws the half-open back to
  `LISTENING`, whereupon the completing ACK (no SYN flag) falls to
  `tcp-step-listening`'s `else ActNone` (`Tcp.codex:262`) and is ignored
  forever. The server holds ONE `TcpConnection` as both listener and accepted
  connection. The repair is architectural and in `codex/os/net`: either accept
  must not give up while a connection is mid-handshake, or the server needs a
  listening socket distinct from the accepted connection. Routed to blu
  2026-08-18; shelved with the design.
- **The serve loop's heap frontier climbs about 73 KB per request** and
  reaches the DMA floor around request 31, because the loop takes no heap
  mark. The remedy is arm64 `deck-record` (`plugs-backlog.md` 1.33), which the
  arm64 and riscv plugs do not implement.
- **Phase 5b, upload to OCI Object Storage and import as a custom image**:
  needs Damian's OCI account.
- **Phase 5c, VCN security list (TCP 80/443) and addressing**: needs Damian's
  OCI account.
- **Phase 5d, smoke test via serial console and external curl**: needs 5b and
  5c.

## Licensing

Codex Fair Use License v1.0 (draft). Free for personal, education, research,
evaluation, auditing, non-commercial open-source. Commercial use free below
$100K aggregate revenue (rolling 12 months). Above threshold requires
commercial license.
