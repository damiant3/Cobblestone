# WHPX BSOD — 2026-05-03 during ATA bring-up CL 704 validation

**Reported:** 2026-05-03 ~07:12 local
**Reporter:** Nib (during VM profiles CL 704 validation)
**Bug class:** WHPX host crash (BSOD)

## Trigger

Running `codex.build/run-with-disk.ps1` (single QEMU guest, WHPX accel,
`kernel-irqchip=off`, 1024 MB, IDE disk attached) during the block-identify
sample validation. Possibly the second or third QEMU launch in quick
succession after a sweep + disk-persistence run.

## Context

- QEMU 11.0, `-accel whpx,hyperv=off`, `-machine kernel-irqchip=off`.
- Host: Windows 11 Pro 10.0.26200.
- Guest: bare-metal Codex ELF (1.8 MB), single vCPU.
- Disk: 512 KB raw IDE image.
- Preceding workload: full sweep (4 parallel guests), then
  `test-disk-persistence.ps1` (sequential guests with retries),
  then single-guest block-identify runs.

## Likely cause

Same class as all prior WHPX BSODs (docs/Bugs/whpx-host-bsod.md).
The host hypervisor crashes when QEMU guests accumulate residual state
across rapid start/stop cycles. `kernel-irqchip=off` mitigates but
does not fully eliminate the bug.

## Mitigation applied

None beyond existing `kernel-irqchip=off`. Session terminated by BSOD.
Work was shelved in CL 704 before the crash (Perforce state intact).

## State of CL 704

All gates were green immediately before the crash:
- BS2: stage1 === stage2 (890,627 bytes).
- BS3: stage1 === stage2 (1,828,760 bytes, byte-identical fixed point).
- Sweep: 138 pass / 0 fail / 13 skipped.
- Disk persistence: all 5 phases PASS (attempts 1-4).

The block-identify sample had just returned 0 (correct under
QEMU-11.0.0 profile). The BSOD occurred during or after that run.

## Related

- docs/Bugs/whpx-host-bsod.md (master tracker)
- QEMU upstream: gitlab.com/qemu-project/qemu/-/work_items/3460
