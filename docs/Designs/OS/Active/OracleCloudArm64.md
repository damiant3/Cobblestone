# Codex ARM64 Bare-Metal on Oracle Cloud

## Goal

Boot Codex as the OS on Oracle Cloud Infrastructure (OCI) free-tier
ARM Ampere A1 instances using "Bring Your Own Image" (BYOI). The site
serves as a showcase: "this website is served by Codex on bare metal"
-- game store, code browser, everything running on the Codex kernel
with zero Linux.

OCI ARM VMs boot via UEFI and require VirtIO drivers (virtio-blk for
disk, virtio-net for networking). The existing TCP/IP stack, HTTP
parser, and web server routing are pure Codex and architecture-
independent -- only the hardware drivers need swapping.

## OCI Free Tier Specs

- 4 ARM Ampere A1 cores (Neoverse N1, ARMv8.2)
- 24GB RAM
- 200GB block storage
- 10TB/month bandwidth
- KVM hypervisor, paravirtualized (VirtIO-PCI)
- UEFI boot, custom QCOW2 or raw images

## Phase 0: ARM64 MMIO and Buffer Builtins

Add to `codex/plugs/arm64/Arm64Runtime.codex`:
- peek-byte, poke-byte (LDRB/STRB)
- peek-32, poke-32 (LDR/STR 32-bit)
- peek-qword, poke-qword (LDR/STR 64-bit)
- __buf-write-byte, __buf-read-bytes, __buf-write-bytes
- __heap-save, __heap-restore, __heap-advance

Add to ARM64 encoder (codex.foreword Arm64Encoder):
- DSB, DMB, ISB barrier instructions
- MRS/MSR for system register access

Reference: x86-64 equivalents in X86_64Helpers.codex lines 2920-2958.

Test: poke-32 to PL011 UART on QEMU virt.

## Phase 1: ARM64 UEFI Boot

1a. ARM64 PE Writer -- new codex/plugs/pe/Arm64PeWriter.codex
- Machine type 0xAA64
- UEFI entry stub: save SystemTable, AllocatePages, ExitBootServices
- 4-level page tables (4KB granule), MAIR, TCR, TTBR0, SCTLR
- Set SP, x28 (heap), branch to opening

1b. FAT boot filename -- BOOTAA64EFI in Fat16Writer/Fat32Writer

1c. Exception vector table -- codex/os/kernel/Arm64Boot.codex
- 16 entries, 2KB-aligned, VBAR_EL1

1d. Build script -- build/build-arm64-img.ps1
- Source -> IR -> ARM64 codegen -> PE32+ -> GPT+FAT16 -> qemu-img QCOW2

Test: QEMU aarch64 with AAVMF UEFI firmware boots to UART output.

## Phase 2: GIC and Timer

2a. GICv3 driver -- codex/os/kernel/Gic.codex
- GICD, GICR, ICC system registers (MRS/MSR)

2b. Generic Timer -- codex/os/kernel/Arm64Timer.codex
- CNTP_TVAL_EL0, CNTP_CTL_EL0, PPI 30

Test: timer tick visible on UART.

## Phase 3: VirtIO-PCI Networking

3a. ECAM PCI -- codex/os/kernel/Arm64Pci.codex
- ECAM at 0x4010000000 (QEMU virt)
- Reuse Pci.codex scanning/BAR logic

3b. VirtIO-PCI transport -- codex/os/kernel/VirtioPci.codex
- VirtIO 1.0 modern, capability-list discovery
- Virtqueue setup, feature negotiation, DSB/DMB barriers

3c. VirtIO-net -- codex/os/kernel/VirtioNet.codex
- RX/TX virtqueues, MAC from device config
- Same interface as ne2k-send-frame/ne2k-recv-frame

3d. ARM64 NetIO -- codex/os/net/Arm64NetIO.codex
- Drop-in for NetIO.codex, calls VirtIO instead of NE2K

Test: curl HTTP response via QEMU TAP bridge.

## Phase 4: VirtIO-blk and Static Content

4a. VirtIO-blk -- codex/os/kernel/VirtioBlk.codex
4b. FAT reader -- codex/os/kernel/FatReader.codex
4c. Static file server -- extend web server routing

Test: serve HTML file from disk partition.

## Phase 5: OCI Deployment

5a. Raw -> QCOW2 via qemu-img
5b. Upload to OCI Object Storage, import as custom image
5c. VCN security list (TCP 80/443), DHCP or static IP
5d. Smoke test via serial console and external curl

## Build Order

Phase 0 -> Phase 1 -> Phase 2 -> Phase 3 -> Phase 4 -> Phase 5.
Full build before deployment. Each phase has a QEMU test gate.

## Licensing

Codex Fair Use License v1.0 (draft). Free for personal, education,
research, evaluation, auditing, non-commercial open-source. Commercial
use free below $100K aggregate revenue (rolling 12 months). Above
threshold requires commercial license.
