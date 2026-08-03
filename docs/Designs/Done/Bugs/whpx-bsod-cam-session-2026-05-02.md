# WHPX BSOD -- Cam session 2026-05-02

**Time**: ~21:15 local, during pingpong-self.ps1 rebuild
**Host**: Windows 11 Pro, QEMU 11.0, WHPX accel, kernel-irqchip=off
**Trigger**: `pingpong-self.ps1` running a self-build (seed → SUT).
Single QEMU guest, no parallel jobs. This was the ~12th pingpong
rebuild in the session (debugging process-spawn child OOM). Previous
11 rebuilds completed without incident.

**Activity at time of crash**: QEMU running bare-metal selfhost
compiler (seed ELF compiling compiler source over serial). No
concurrent sweep or parallel QEMU instances. Host was otherwise idle
(Claude Code CLI + terminal).

**Prior session context**: Heavy QEMU usage -- ~20+ QEMU boots total
(pingpong rebuilds, gdb debug sessions via WSL KVM, sample compiles,
run-for-sweep probes). Mix of WHPX (Windows) and KVM (WSL) guests
across the session.

**Recovery**: Hard reboot. Workspace intact (Perforce + git clean).
Pingpong needs to be restarted.

**Relation to known bug**: Matches pattern from `docs/Bugs/whpx-host-bsod.md`.
Cumulative WHPX state may lower the threshold. This is the first
single-guest BSOD observed -- all prior incidents involved parallel
guests (jobs=4+).
