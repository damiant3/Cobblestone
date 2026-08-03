# WHPX: host BSOD from `pingpong-self.sh` -- missing `kernel-irqchip=off` remediation

## Summary

Host BSOD on 2026-04-30 05:08 was caused by agent **Nib** running
`tools/pingpong-self.sh` (the bash script) instead of `codex.build/pingpong-self.ps1`
(the PowerShell harness). The `.sh` scripts do **not** pass `-machine
kernel-irqchip=off`, which is the validated BSOD workaround baked into
`qemu-config.ps1` since CL 485. The `.ps1` path is canonical; the `.sh` path
is legacy and lacks the remediation.

**Root cause**: operator error (wrong script), not a new QEMU/WHPX bug.
The `kernel-irqchip=off` workaround remains valid.

**Remediation**: Nib is preparing a CL to delete the `.sh` scripts entirely,
eliminating the footgun.

## Crash details

**Timestamp**: 2026-04-30 05:08:36 (local)

**Bugcheck**:
```
0x00000050 (PAGE_FAULT_IN_NONPAGED_AREA)
  P1: 0xfffff78000002004   (faulting address -- KUSER_SHARED_DATA region)
  P2: 0x0000000000000000   (read access)
  P3: 0xfffff8049a11d0e0   (faulting instruction)
  P4: 0x0000000000000002   (page not present)
```

**Minidump**: `C:\WINDOWS\Minidump\043026-7656-01.dmp`

## What actually ran (reconstructed)

Nib ran `pingpong-self.sh`, which invokes QEMU via the bash helpers. The
bash path constructs a QEMU command line like:

```
qemu-system-x86_64 \
  -accel whpx \
  -kernel <elf> \
  -chardev socket,id=ch0,host=127.0.0.1,port=N,server=on,wait=on \
  -chardev socket,id=ch1,host=127.0.0.1,port=N+1,server=on,wait=on \
  -serial chardev:ch0 \
  -serial chardev:ch1 \
  -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
  -display none \
  -no-reboot \
  -m 1024
```

**Missing**: `-machine kernel-irqchip=off`

The `.ps1` harness (`qemu-config.ps1:183`) unconditionally appends
`-machine kernel-irqchip=off`. The `.sh` scripts were never updated with
this flag because `.ps1` became canonical before the workaround was
discovered.

## What should have run

```
"D:\Program Files\qemu\qemu-system-x86_64.exe"
  -accel whpx
  -kernel <elf>
  -chardev socket,id=ch0,host=127.0.0.1,port=N,server=on,wait=on,nodelay=on
  -chardev socket,id=ch1,host=127.0.0.1,port=N+1,server=on,wait=on,nodelay=on
  -serial chardev:ch0
  -serial chardev:ch1
  -device isa-debug-exit,iobase=0xf4,iosize=0x04
  -display none
  -no-reboot
  -m 1024
  -machine kernel-irqchip=off
```

This is what `codex.build/pingpong-self.ps1` produces via `Start-QemuRun` in
`codex.build/qemu-config.ps1`.

## Recent BSOD history on this machine

All within the last 48 hours:

| Time | Bugcheck | Code | Likely cause |
|------|----------|------|--------------|
| 04/30 05:08 | **0x50** PAGE_FAULT_IN_NONPAGED_AREA | `0xfffff78000002004` | **This crash** -- Nib ran `.sh` (no irqchip=off) |
| 04/29 09:31 | 0x50 PAGE_FAULT_IN_NONPAGED_AREA | `0xfffff8023dd70dbe` | Nib ran `.sh` |
| 04/29 09:04 | 0x50 PAGE_FAULT_IN_NONPAGED_AREA | `0xfffffffff7e6e8cf` | Nib ran `.sh` |
| 04/29 08:45 | 0x4E PFN_LIST_CORRUPT | | Nib ran `.sh` |
| 04/29 05:35 | 0x0A IRQL_NOT_LESS_OR_EQUAL | | Parallel sweep (known #3460 pattern) |
| 04/29 01:11 | 0x13A KERNEL_MODE_HEAP_CORRUPTION | | Parallel sweep |
| 04/28 22:31 | 0x7F UNEXPECTED_KERNEL_MODE_TRAP | | |
| 04/28 18:36 | 0xD1 DRIVER_IRQL_NOT_LESS_OR_EQUAL | | |
| 04/28 12:20 | 0x3B SYSTEM_SERVICE_EXCEPTION | | |

The 04/29-04/30 `0x50` cluster is consistent with repeated `.sh` invocations
without the irqchip workaround. These do **not** indicate that
`kernel-irqchip=off` is insufficient.

## Environment

| Component | Value |
|-----------|-------|
| **Host OS** | Windows 11 Pro 10.0.26200, 64-bit |
| **CPU** | Intel Core i7-12700KF (12C/20T, 8P+4E, Alder Lake) |
| **RAM** | 32 GB DDR5-7200 |
| **QEMU** | 11.0.0 (`v11.0.0-12122-ga4bb4b10c9`) |
| **WHPX drivers** | `winhv.sys` 10.0.26100.4768, `winhvr.sys` 10.0.26100.7309, `Vid.sys` 10.0.26100.7920 |

## Status of upstream reports

No update needed to QEMU upstream #3460 or the Microsoft Feedback Hub report.
The `kernel-irqchip=off` workaround is not invalidated by this incident.

## Action items

1. **Nib**: delete `.sh` scripts (`pingpong-self.sh`, `pingpong.sh`, and any
   other QEMU-invoking `.sh` files) so the unmitigated path cannot be used.
2. **Standing rule**: QEMU is only invoked via the `.ps1` harness. The `.sh`
   path is a BSOD footgun. (Already in memory: `feedback_prefer_ps1_over_sh`,
   `feedback_qemu_is_shared_resource`.)
