# BSOD — 2026-05-02 ~21:25 PM — Nib sweep -Jobs 4 (second in 15 min)

**Date:** 2026-05-02 ~21:25 PM (estimated)
**Reporter:** Nib
**Bugcheck:** Unknown (system rebooted before code captured)
**Host:** BigWhite, Windows 11 Pro 10.0.26200

## What Was Running

- **Nib**: `sweep.ps1 -Jobs 4` — 4 parallel QEMU guests. This was the
  post-reboot retry of the sweep that was running when the previous
  BSOD hit (~21:10 PM). The sweep completed (136 pass / 1 fail / 14
  skip) and Nib was inspecting the single failure (`test-if` runtime
  error — likely QEMU transport flake, not a regression). The BSOD
  occurred during or immediately after the inspection.
- **Cam**: unknown.
- Config: `-accel whpx,hyperv=off`, `-machine kernel-irqchip=off`,
  no CPU pinning.

## Context

Two BSODs within ~15 minutes on the same agent running sweep -Jobs 4.
No concurrent peer activity known. The sweep completed once between
the two BSODs (the first BSOD killed the first attempt; the retry
completed). The second BSOD hit while Nib was reading the failure log
— possibly stale QEMU guests from the just-completed sweep were still
winding down.

## Prior BSODs This Session

1. ~21:10 PM — sweep -Jobs 4, first attempt (documented in
   `bsod-2026-05-02-nib-sweep.md`)
2. ~21:25 PM — this incident

Combined with cam's earlier -Jobs 4 BSOD today, this is the third
BSOD at -Jobs 4 with a single agent in a single day.

## Observation

The host may be accumulating WHPX state across reboots that lowers
the BSOD threshold. Each reboot-and-retry may be starting from a
worse baseline than the prior session. See `whpx-host-bsod.md`
incident timeline.
