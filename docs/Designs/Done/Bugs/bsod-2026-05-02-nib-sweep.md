# BSOD -- 2026-05-02 ~21:10 PM -- Nib sweep -Jobs 4

**Date:** 2026-05-02 ~21:10 PM (estimated from last successful output)
**Reporter:** Nib
**Bugcheck:** Unknown (system rebooted before code captured)
**Host:** BigWhite, Windows 11 Pro 10.0.26200

## What Was Running

- **Nib**: `sweep.ps1 -Jobs 4` -- 4 parallel QEMU guests compiling/running
  samples. This was the second attempt; the first was transport-killed
  mid-run (socket write error) and retried.
- **Cam**: unknown -- may have been preparing an integration push.
- Config: `-accel whpx,hyperv=off`, `-machine kernel-irqchip=off`,
  no CPU pinning. Standard qemu-config.ps1 defaults.

## Context

Nib had just completed a successful pingpong run (~9:02 PM) with
shelved CL 676 (right-size record fields + prose grooming). The first
sweep attempt failed immediately with a transport error ("Unable to
write data to the transport connection: An existing connection was
forcibly closed by the remote host"). Second sweep attempt was
launched at ~9:10 PM and the BSOD occurred shortly after.

Prior session history: multiple pingpong runs (5+) and one completed
sweep earlier in the session (~7:25 PM). All at -Jobs 4.

## Prior BSODs

See `whpx-host-bsod.md` for the canonical writeup and full incident
timeline. This is the second known BSOD at -Jobs 4 with a single
agent (first: cam, also 2026-05-02, documented in the timeline
appendix of whpx-host-bsod.md).
