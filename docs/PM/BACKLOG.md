# Backlog — Outstanding Work

**Updated**: 2026-06-10

## Active — Ongoing

### USB Install (Gap 4)

| # | Item | Notes |
|---|------|-------|
| 1 | **End-to-end USB validation** | All driver/integration layers done (MSC, DriveManager, DevConsole, XHCI). Needs physical USB stick test on Asus + Dell. **New since CL 3742: the 3GB seed needs 3GB of contiguous RAM below the PCI MMIO hole — confirm on both machines.** |

### Memory

| # | Item | Notes |
|---|------|-------|
| 3 | **Non-contiguous physical memory (the real 8GB+)** | `bare-metal-ram-size` is now 3GB (CLs 3736/3742), the hard maximum for the contiguous design: the top 1GB of 32-bit space is PCI MMIO, and RAM above 4GB relocates to addresses the kernel never maps. Going past 3GB needs: boot path reads the firmware memory map instead of a baked constant; page tables skip the MMIO hole and map above 4GB; allocator and stack-top handle a non-contiguous arena. This is the item that kills the memory ceiling for good. Design home: `docs/Designs/Memory/Active/`. |

### Compiler

| # | Item | Notes |
|---|------|-------|
| 2 | **Phase discipline — remaining items** | `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`. Per-phase build/measure/compact and the RESOLVE/LIFT split are done. Open: deck-record toggle ratchet, escape-invariant enforcement, TCO-reset removal, per-phase survey tightening (lex 40x done CL 2306). |
