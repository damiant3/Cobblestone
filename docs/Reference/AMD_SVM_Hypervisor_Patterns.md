# AMD SVM Hypervisor Patterns

Source: [Type2-AMD-HV](https://github.com/whosstyler/Type2-AMD-HV) -- a
hosted (Type-2) hypervisor for AMD Secure Virtual Machine (SVM) on
Windows. Written in C + x86-64 assembly. Reviewed 2026-06-13.

This is a reference capture of techniques that may be useful if codex-vm
ever moves past WHP to a direct AMD-V backend or targets real hardware.

## Dual Nested Page Table (NPT) Trick

AMD's NPT hardware lacks execute-only page permissions (unlike Intel EPT).
This project works around it by maintaining two parallel page table trees:

- **npt_main**: all pages RWX, normal execution path.
- **npt_hooks**: globally NX on all pages.

When the CPU executes code on a page that needs interception, a #NPF
fires in npt_hooks. The handler swaps the VMCB's NCR3 back to npt_main
and re-executes. This ping-pong gives fine-grained per-page execute
control without hardware support for XO permissions.

Optimization: when a 2 MB PDE is split into 512 4 KB PTEs, surrounding
PTEs get their NX bits pre-cleared to reduce cross-page faults on
sequential execution.

Relevance: if codex-vm targets AMD-V directly, this is the canonical
pattern for execute-control on NPT hardware.

## Firmware Wasted Allocator (FWA)

Discovers unmapped DRAM gaps between firmware-reported physical memory
ranges. These gaps exist on real hardware between E820/UEFI memory map
entries -- physical RAM that no OS memory manager knows about.

Algorithm:
1. Scan gaps between known memory ranges; skip first two (legacy/main).
2. Map candidate regions via MmMapIoSpace, verify pages are zeroed.
3. Skip the 3-4 GB PCI MMIO aperture (reads have device side effects).
4. Skip past PE images (EFI runtime regions) rather than aborting.
5. Bump-allocate sequentially from discovered regions.

Used for VMCBs, host page tables, VMM stacks -- all invisible to the OS.

Relevance: if Codex OS ever needs to reserve control structures that the
guest cannot discover via its own memory map, this is the pattern. Also
relevant for any bare-metal firmware work where E820 gaps are usable
DRAM.

## Per-CPU Parallel Virtualization

Virtualizes all processors simultaneously using DPC (Deferred Procedure
Call) broadcast:

1. Queue a high-importance DPC targeted to each logical processor.
2. Each DPC increments an atomic counter and spins with `_mm_pause()`.
3. When all cores are spinning, each executes the identical VMRUN setup.
4. Atomic decrement on completion; last core signals done.

Near-instantaneous full-system virtualization with no context switches
during bringup.

Relevance: pattern for any multi-core bare-metal bringup where all cores
must reach a synchronization point before proceeding.

## 512 GB Identity-Mapped Host Page Tables

Single PML4 entry covers the entire physical address space using 512
PDPT entries with 2 MB large pages. Minimal TLB pressure, simple to
construct, and sufficient for any machine with less than 512 GB RAM.

Construction integrates MTRR (Memory Type Range Register) queries --
both fixed-range and variable-range -- to assign correct memory types
(WB, UC, WC) per physical address during page table construction.
Uncacheable takes priority when ranges overlap.

Relevance: codex-vm currently doesn't model MTRRs. If targeting real
hardware, MTRR-aware page table construction prevents subtle caching
bugs (especially around MMIO regions and framebuffers).

## VMRUN Loop Structure

The core loop in assembly:

    VMLOAD  -- restore guest hidden segment state from VMCB
    VMRUN   -- execute guest until exit
    VMSAVE  -- preserve guest segment state to VMCB
    VMLOAD  -- restore host hidden segment state

Context save/restore: 16 GPRs (0x80 bytes) + 16 XMM registers (0x100
bytes) pushed to stack before calling the C exit handler. NRIP_SAVE
feature provides hardware-saved next-RIP, eliminating manual instruction
length decoding for most exits.

Five exit types handled: VMMCALL (hypercalls), MSR (protected register
access), NPF (page faults / hook dispatch), VMRUN (nested virtualization
attempts), INVALID (devirtualization trigger).

## SEH-Aware Exception Handling in VMM

The host IDT is a copy of Windows' IDT with vectors 0-20 overridden
(except NMI #2 and reserved #15). IST (Interrupt Stack Table) entries
are preserved from the originals to maintain Windows' dedicated stacks
for double-fault and machine-check.

Exception handlers walk PE runtime unwind tables for structured
exception recovery. If a fault occurs inside a `__try` block, control
transfers to the `__except` handler. Otherwise, KeBugCheckEx.

Relevance: pattern for graceful fault recovery inside a hypervisor or
VMM without crashing the entire system.
