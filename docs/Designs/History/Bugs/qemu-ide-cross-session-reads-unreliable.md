# QEMU IDE: cross-session reads unreliable under WHPX — 2026-05-02

**Reported:** 2026-05-02
**Reporter:** Cam (during DiskFacts V0 bring-up, CL 665)
**Bug class:** QEMU IDE controller emulation (cross-session boot path)

## Trigger

ATA PIO read of a sector written by a *previous* QEMU session. Config:
QEMU 11.0, `-drive file=test.img,format=raw,if=ide,index=0`, WHPX
accel, bare-metal kernel.

## Symptom

~40% of boots read correctly. The remaining ~60% return all zeros for
every sector in the session, even though the host disk image file
contains the correct data (verified via host-side read). The failure
is per-boot — if the first read in a session fails, all reads in that
session fail. If it succeeds, all reads succeed.

## Additional findings

- `cache=none` breaks ALL reads (Windows O_DIRECT alignment issue).
- `cache=writethrough` does not improve reliability.
- 3-second delay before QEMU kill ensures writes flush to the backing
  file (without it, late writes in a session may not persist).
- In-program retry (re-issuing the SYSCALL) does not help — the IDE
  controller returns the same stale data for the entire boot.

## Impact

Disk persistence tests (`codex.build/test-disk-persistence.ps1`) use
session-level retries (reboot QEMU). Single-sector tests pass reliably
within ~5 attempts. Multi-sector tests (reading 4+ sectors correctly
in one boot) are flaky — need many attempts or get lucky.

## Theory

The WHPX-accelerated IDE controller sometimes fails to initialize its
sector buffer from the backing file on boot. The buffer starts zeroed
and stays that way for the session.

## Workaround

Retry at the session level (reboot QEMU). The test script supports
`-MaxRetries N`. For CI, use `-MaxRetries 10`.

## Related

See `qemu-ide-pio-write-read-stale.md` for the adjacent same-session
write-then-read variant.
