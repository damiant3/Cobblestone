# WHPX BSOD #2 -- Cam session 2026-05-02

**Time**: ~21:30 local, second BSOD within ~15 minutes
**Host**: Windows 11 Pro, QEMU 11.0, WHPX accel, kernel-irqchip=off
**Trigger**: `pingpong-self.ps1` restart after first BSOD recovery.
Single QEMU guest, no parallel jobs. Fresh boot, first QEMU launch
since the previous BSOD.

**Activity at time of crash**: Same as first incident -- QEMU running
bare-metal selfhost compiler during self-build phase. This was the
FIRST QEMU launch after a clean reboot from the previous BSOD.

**Significance**: Two BSODs in rapid succession, both from single-guest
pingpong. The second occurred on a fresh boot with no accumulated WHPX
state -- contradicts the "cumulative state" theory from prior incidents.
The common factor is the selfhost compilation workload (large source,
long serial transfer, heavy computation in guest).

**Relation to known bug**: Escalation of `docs/Bugs/whpx-host-bsod.md`.
Prior incidents required parallel guests (jobs=4+). Now reproducible
with a single guest on fresh boot. The WHPX hypervisor may have a
deterministic trigger related to guest workload duration or memory
access patterns, not just concurrency.
