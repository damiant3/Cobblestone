# Backlog — Outstanding Work

**Updated**: 2026-06-15

## Active — Ongoing

### USB Install (Gap 4)

| # | Item | Notes |
|---|------|-------|
| 1 | **End-to-end USB validation** | All driver/integration layers done (MSC, DriveManager, DevConsole, XHCI). Needs physical USB stick test on Asus + Dell. **New since CL 3742: the 3GB seed needs 3GB of contiguous RAM below the PCI MMIO hole — confirm on both machines.** |

### Memory

| # | Item | Notes |
|---|------|-------|
| 1 | **Non-contiguous physical memory (the real 8GB+)** | `bare-metal-ram-size` is now 3GB (CLs 3736/3742), the hard maximum for the contiguous design: the top 1GB of 32-bit space is PCI MMIO, and RAM above 4GB relocates to addresses the kernel never maps. Going past 3GB needs: boot path reads the firmware memory map instead of a baked constant; page tables skip the MMIO hole and map above 4GB; allocator and stack-top handle a non-contiguous arena. This is the item that kills the memory ceiling for good. Design home: `docs/Designs/Memory/Active/`. |

### Compiler

| # | Item | Notes |
|---|------|-------|
| 1 | **Phase discipline — remaining items** | `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`. Per-phase build/measure/compact and the RESOLVE/LIFT split are done. Open: deck-record toggle ratchet, escape-invariant enforcement, TCO-reset removal, per-phase survey tightening (lex 40x done CL 2306). |

### Apps — Never-Compiled Code Inventory (2026-06-11 sweep)

Found while root-causing the crypto vector failures: large bodies of app
code that have never compiled, written against APIs or syntax that do
not exist. They pass no gate because nothing collects them. Each class
needs either a compiler feature, a mechanical rewrite, or deletion.

| # | Item | Notes |
|---|------|-------|
| 1 | **Hex sites — mostly done** | Original 178 app `.codex` sites are cleaned up. Remaining `0x` in `.codex` files are inside string literals (embedded JS in SparkBridge) or `NxM` dimension strings, not hex integer literals. The JS/HTML/PS1 files use JavaScript's native `0x` notation which is correct for those languages. Consider closed for Codex source. |
| 2 | **Bare `list-map` callers** | Not in the foreword; per-file sweep overcounts because cross-chapter defs resolve via cites — needs a compile-based count, not grep. |

### Encoding

| # | Item | Notes |
|---|------|-------|
| 1 | **CCE Tiers 2+ (CJK, rare scripts, emoji)** | Tier 1 (2048 two-byte codes, all EU languages) shipped CL 4248. Tier 2 (3 bytes) covers CJK, emoji, rare scripts. Same pattern: foreword encode/decode, lexer acceptance, I/O converters, block assignment table. Not needed for EU IoT deployment; needed for global reach. |

### GPU Compute

| # | Item | Notes |
|---|------|-------|
| 1 | **Dual-target GPU compilation (PTX + SPIR-V)** | Design complete (CL 4424). PTX and SPIR-V plugs built and compiling (CL 4432, 157KB/152KB CDX). Full IR expression coverage. Next: K0-K2 from GpuKernels.md (`[Device]`/`[Gpu]` effects, type-checker post-pass, IR partition) then end-to-end kernel test. |

### Tooling — Host Stability

| # | Item | Notes |
|---|------|-------|
| 1 | **build.ps1 -mem 3072 matches bare-metal-ram-size** | `bare-metal-ram-size` is 3GB (3221225472), not 2GB — 3072 is already the minimum. Cannot reduce without changing the kernel memory layout. Reducing host commit pressure requires the non-contiguous memory work (Memory #3) or deriving -mem from the seed at build time. |
