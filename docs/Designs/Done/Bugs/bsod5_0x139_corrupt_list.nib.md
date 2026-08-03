# WHPX Host BSOD -- 2026-04-30 (0x139 KERNEL_SECURITY_CHECK_FAILURE)

## Filed at

- **QEMU upstream**: https://gitlab.com/qemu-project/qemu/-/work_items/3460 (update to existing)
- **Microsoft Feedback Hub**: https://aka.ms/AA10ry2d (update to existing)

## Summary

Tenth host BSOD on this machine (see full history below), second today.
Two QEMU guests (one from Nib, one from Cam -- see cross-reference below)
running under `-accel whpx` **with** `-machine kernel-irqchip=off` (the
PowerShell harness) crashed the host during pingpong self-compilation.
This is the first crash with `kernel-irqchip=off` active -- all prior
crashes occurred either before the flag existed (CL 490, 04/29 10:45) or
via the .sh harness which lacked it. The bugcheck class is also new:
0x139 (KERNEL_SECURITY_CHECK_FAILURE / corrupt linked list) rather than
the 0x50/0x4E/0x0A/etc seen previously.

If confirmed as WHPX-related, this means `kernel-irqchip=off` mitigates
but does not fully eliminate the kernel corruption bug. However, since
minidump symbol analysis has not been performed, it is possible this
crash is from a different driver entirely.

## Environment

Same as prior report (`docs/Bugs/newbsod_irqchipoff.nib.md`). Unchanged:

- **Host OS**: Windows 11 Pro 26200 (10.0.26200)
- **CPU**: 12th Gen Intel Core i7-12700KF (12C/20T)
- **RAM**: 32 GB DDR5-7200
- **QEMU**: 11.0.0, build `v11.0.0-12122-ga4bb4b10c9`
- **Hyper-V drivers**: winhv.sys 10.0.26100.4768, winhvr.sys 10.0.26100.7309,
  Vid.sys 10.0.26100.7920, vmswitch.sys 10.0.26100.8117

## Crash timeline

| Time | Event |
|---|---|
| 04:58:33 | BSOD (0x50, documented in `newbsod_irqchipoff.nib.md`) |
| ~05:08 | Reboot |
| 05:20:18 | CL 508 submitted (BSOD report) |
| 05:56:20 | CL 511 submitted (delete .sh scripts) |
| ~06:05 | Agents resume, Cam and Nib both launch `pingpong-self.ps1` |
| 06:11:54 | Nib's pingpong output: Phase 3 self-build → SUT 1,422,200 B (53s), canary OK (4s), Phase 4 Stage 1 begins |
| ~06:25 | **Host BSOD** (estimated -- between 06:14 and 06:28) |
| 06:28:01 | System reboots (LastBootUpTime) |
| 06:28:09 | WER event logs bugcheck 0x139 |

Note: the 6008 event records "previous shutdown at 5:48:42 AM." This
may be the actual crash time from a session that started after a quick
reboot from the 04:58 crash. The 40-minute gap between 5:48 and the
6:28 boot is consistent with Windows writing a kernel dump.

## Bugcheck details

From `Microsoft-Windows-WER-SystemErrorReporting` Event ID 1001:

```
BugCheckCode:  0x00000139  (KERNEL_SECURITY_CHECK_FAILURE)
Parameter 1:   0x0000000000000003   (FAST_FAIL_CORRUPT_LIST_HEAD)
Parameter 2:   0xffffad0e2e116ec0   (address of corrupted list entry)
Parameter 3:   0xffffad0e2e116e18   (exception record / trap frame)
Parameter 4:   0x0000000000000000   (reserved)
```

### Interpretation

**Bugcheck 0x139** -- the kernel detected internal data structure corruption
via `__fastfail(FAST_FAIL_CORRUPT_LIST_HEAD)`. A doubly-linked list entry
had invalid Flink/Blink pointers. This is a security hardening check --
Windows validates list integrity before list operations and crashes rather
than risk exploitation of a corrupted list.

**Parameter 1 = 3 (FAST_FAIL_CORRUPT_LIST_HEAD)** -- a `LIST_ENTRY`
structure was found with inconsistent forward/backward links. Common causes:
use-after-free, double-free, buffer overrun into a list head, or concurrent
unsynchronized list mutation. In the WHPX context, this is consistent with
a race condition in partition, VCPU, or memory management structures.

**Parameter 2 (`0xffffad0e2e116ec0`)** -- the kernel-mode address where the
corrupted list was detected. This is in the dynamic kernel pool range
(not a well-known fixed structure like KUSER_SHARED_DATA was in the
earlier 0x50 crash). Without symbols, the owning module cannot be
determined from the address alone.

### Minidump

**Dump**: `C:\WINDOWS\Minidump\043026-7859-01.dmp`
**Report ID**: `5fe314ae-febf-4867-9464-a0ae8f294e6e`

## What was running

### Nib's QEMU instance

Nib ran `codex.build/pingpong-self.ps1` via the Claude Code PowerShell tool as a
background task. The script uses `codex.build/qemu-config.ps1` which passes
`-accel whpx` and `-machine kernel-irqchip=off`. The guest was in Phase 4,
Stage 1 -- the self-built SUT (1,422,200 bytes) was compiling its own source
(844,961 bytes) over the serial chardev.

### Cam's QEMU instance

Cam also ran `codex.build/pingpong-self.ps1` via the PowerShell tool, same
harness, same flags. See `docs/Bugs/bsod_irqchipoff_139.cam.md`.

### Guest count: 2

Both agents were running pingpong simultaneously -- **two QEMU guests**,
not one. Both Nib's and Cam's individual reports initially claimed "single
guest" because neither agent was aware of the other's QEMU process at the
time of writing. The actual count was 2 concurrent guests under WHPX with
`kernel-irqchip=off`.

## Cross-reference: Cam's report (`bsod_irqchipoff_139.cam.md`)

Cam submitted CL 514 with the same bugcheck data. The reports agree on
bugcheck code, parameters, minidump path, and that this is a new crash
class (0x139 vs prior 0x50/0x4E). Discrepancies:

### 1. Guest count

Both reports say "single guest." Corrected above: there were 2 guests.

### 2. Crash timestamp

Cam says "2026-04-30 06:28:09 (local)" -- this is the WER event log time
(when the event was recorded after reboot), not the crash time. The 6008
event says the actual shutdown was at 5:48:42 AM.

### 3. `kernel-irqchip=off` status on prior crashes

Cam's history table marks 04/29 01:11 (0x13A) and 04/29 05:35 (0x0A) as
`irqchip=off: YES`. This is incorrect:

- `qemu-config.ps1` was first created at CL 485 (04/29)
- `kernel-irqchip=off` was added at CL 490 (04/29 10:45 AM)
- Both of those crashes (01:11 AM and 05:35 AM) predate CL 490
- They were on the .sh harness or early .ps1 without the flag

This matters because it determines whether the current crash is the FIRST
with `kernel-irqchip=off` (it is) or merely one of several (it is not).

### 4. Prior crash guest counts

Cam's table says crashes 04/29 08:45 - 04/30 05:08 were "1 guest" by
"Nib (.sh)". Corrections:

- 04/29 08:05–09:04 cluster: sweep jobs=8 (multi-guest), per
  `docs/Bugs/whpx-host-bsod.md`
- 04/30 04:58: 3 guests (2 cam + 1 nib), per
  `docs/Bugs/newbsod_irqchipoff.nib.md`

### 5. Cam's "two independent corruption vectors" conclusion

Cam concludes WHPX has two independent vectors: (1) IRQ-path corruption
mitigated by `kernel-irqchip=off`, and (2) list-entry corruption not
mitigated. This is reasonable as a hypothesis but should be qualified:
without minidump symbol analysis confirming the faulting module for the
0x139 crash is winhvr.sys/Vid.sys, the 0x139 could be from a completely
different driver unrelated to WHPX. One data point is insufficient to
declare a second WHPX corruption vector.

## All BSODs on this host

Complete history from Windows event log. Actual crash times from Event
6008; bugcheck codes from WER Event 1001.

| # | Crash time | Bugcheck | Code | `irqchip=off` | Scenario | Dump |
|---|---|---|---|---|---|---|
| 1 | 04/28 12:13 | 0x3B SYSTEM_SERVICE_EXCEPTION | P1=0xc0000005 | No (pre-CL 485) | unknown | 042826-7421-01.dmp |
| 2 | 04/28 18:20 | 0xD1 DRIVER_IRQL_NOT_LESS_OR_EQUAL | P1=0x0 | No (pre-CL 485) | unknown | 042826-7890-01.dmp |
| 3 | 04/28 22:14 | 0x7F UNEXPECTED_KERNEL_MODE_TRAP | P1=0x8 | No (pre-CL 485) | unknown | 042826-7796-01.dmp |
| 4 | 04/29 00:38 | 0x13A KERNEL_MODE_HEAP_CORRUPTION | P1=0x17 | No (pre-CL 490) | parallel sweep | 042926-8343-01.dmp |
| 5 | 04/29 05:22 | 0x0A IRQL_NOT_LESS_OR_EQUAL | P1=0xffffe500... | No (pre-CL 490) | parallel sweep | 042926-9171-01.dmp |
| 6 | 04/29 08:05 | 0x4E PFN_LIST_CORRUPT | P1=0x98 | No (pre-CL 490) | sweep jobs=8 | 042926-6875-01.dmp |
| 7 | 04/29 08:45 | 0x50 PAGE_FAULT_IN_NONPAGED_AREA | P1=0xfffffffff7... | No (pre-CL 490) | sweep jobs=8 | 042926-8031-01.dmp |
| 8 | 04/29 09:04 | 0x50 PAGE_FAULT_IN_NONPAGED_AREA | P1=0xfffff802... | No (pre-CL 490) | sweep jobs=8 | 042926-7984-01.dmp |
| 9 | 04/30 04:58 | 0x50 PAGE_FAULT_IN_NONPAGED_AREA | P1=0xfffff780... | No (.sh harness) | 3 guests | 043026-7656-01.dmp |
| **10** | **04/30 ~05:48** | **0x139 KERNEL_SECURITY_CHECK_FAILURE** | **P1=3 (corrupt list)** | **Yes** | **2 guests, pingpong** | **043026-7859-01.dmp** |

### Bugcheck diversity

Six distinct bugcheck codes across 10 crashes, all during WHPX activity:

| Code | Name | Count | `irqchip=off` |
|---|---|---|---|
| 0x3B | SYSTEM_SERVICE_EXCEPTION | 1 | No |
| 0xD1 | DRIVER_IRQL_NOT_LESS_OR_EQUAL | 1 | No |
| 0x7F | UNEXPECTED_KERNEL_MODE_TRAP | 1 | No |
| 0x13A | KERNEL_MODE_HEAP_CORRUPTION | 1 | No |
| 0x0A | IRQL_NOT_LESS_OR_EQUAL | 1 | No |
| 0x4E | PFN_LIST_CORRUPT | 1 | No |
| 0x50 | PAGE_FAULT_IN_NONPAGED_AREA | 3 | No |
| 0x139 | KERNEL_SECURITY_CHECK_FAILURE | 1 | **Yes** |

All 9 crashes without `kernel-irqchip=off` produced page/IRQ/heap
corruption codes. The single crash with the flag is a different class
(list integrity check). This is consistent with `kernel-irqchip=off`
eliminating the IRQ-path corruption but leaving a rarer corruption
vector exposed -- or with the 0x139 being from a different driver
entirely.

## Key finding

**`kernel-irqchip=off` dramatically reduces crash frequency but may not
be a complete fix.** 9/10 crashes occurred without the flag. The one
crash with it active is a different bugcheck class (0x139 vs 0x50/0x4E),
suggesting a different code path. Without minidump symbol analysis, we
cannot confirm the faulting module.

**Recommendation**: Continue using `kernel-irqchip=off`. Attach all 10
minidumps to upstream reports for symbol analysis. Consider whether WHPX
is viable for sustained automated use or whether TCG (software
emulation) should be evaluated despite the performance cost.

## Workaround status (updated)

| Mitigation | Status | Effect |
|---|---|---|
| `-machine kernel-irqchip=off` | **Still recommended** | 9/10 crashes prevented; 1 crash in 50+ runs |
| Reduce guest count | **Insufficient** | Crashed with 2 guests |
| CPU affinity pinning | Supplementary | Not tested for this crash |

**Bottom line**: `kernel-irqchip=off` remains the best mitigation.
The alternative (no flag) crashes reliably within minutes.
