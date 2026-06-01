# BSOD #6 — Sweep parallel QEMU load

**Date**: 2026-04-30 ~21:10 PM
**Reporter**: Nib
**Bugcheck**: Unknown (system rebooted before code captured)
**Host**: BigWhite, Windows 11 Pro 10.0.26200

## What Was Running

- **Nib** (this agent): `sweep.ps1 -Jobs 4` — 4 parallel QEMU guests compiling/running samples
- **Cam**: Unknown — may have had builds or sweeps running concurrently
- Both agents share the same physical host

## Context

This was the third sweep attempt in this session. Previous attempts:
1. `-Jobs 7` — BSOD (earlier in session)
2. `-Jobs 4` — completed with exit code 1 (sample failures, not BSOD)
3. `-Jobs 4` — BSOD (this incident)

## Mitigations In Place

- `kernel-irqchip=off` — unconditional in qemu-config.ps1
- P-core CPU pinning — **NOT active**. The `ProcessorAffinity` assignment
  line is missing from the current qemu-config.ps1 (parameter `$PCore`
  exists but the actual affinity set is absent from the code). Guests
  ran unpinned.
- WHPX acceleration

## Analysis

Parallel QEMU guests under WHPX with kernel-irqchip=off but WITHOUT
CPU pinning triggered host BSODs. The crash pattern correlates with
guest count: 7 jobs crashed immediately, 4 jobs survived one run but
crashed on the second. Single-guest runs (pingpong) have been stable
throughout this session (~10 successful pingpong runs, 0 crashes).

The missing CPU pinning is likely the primary factor — prior sessions
never crashed with pinning active.

## Recommendation

Reduce default sweep parallelism to 2 or run sweep sequentially until
the upstream QEMU/WHPX issue is resolved. Single-guest workloads
(pingpong, single sample probes) remain safe.

## Prior BSODs

See bsod5_0x139_corrupt_list.nib.md, bsod_irqchipoff_139.cam.md for
earlier incidents and full crash history.
