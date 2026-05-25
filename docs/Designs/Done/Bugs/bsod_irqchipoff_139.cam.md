# WHPX: host BSOD 0x139 during `pingpong-self.ps1` — WITH `kernel-irqchip=off`

## Summary

Host BSOD on 2026-04-30 06:28 while agent **Cam** ran `codex.build/pingpong-self.ps1`
via the canonical PowerShell harness. This is the first BSOD observed **with**
`-machine kernel-irqchip=off` active. All prior BSODs in the 04/28–04/30 window
were attributed to the `.sh` path which lacked the workaround.

**Root cause**: unknown. The `kernel-irqchip=off` remediation **did not prevent
this crash**. The bugcheck code (0x139 KERNEL_SECURITY_CHECK_FAILURE) is new —
not seen in any prior incident in this cluster.

**Caveat**: without minidump symbol analysis, we cannot confirm the faulting
module is `winhvr.sys` / `Vid.sys`. The 0x139 could be from an unrelated
driver. One data point does not disprove the 40+ stable iterations with
`kernel-irqchip=off` — this could be a rare secondary race, or a different
bug entirely.

**Significance**: `kernel-irqchip=off` likely reduces BSOD frequency but may
not eliminate it completely. Continue using the flag — the alternative (no flag)
crashes reliably within minutes.

## Crash details

**Timestamp**: 2026-04-30 06:28:09 (local)

**Bugcheck**:
```
0x00000139 (KERNEL_SECURITY_CHECK_FAILURE)
  P1: 0x0000000000000003   (LIST_ENTRY corruption detected)
  P2: 0xffffad0e2e116ec0   (address of the corrupted structure)
  P3: 0xffffad0e2e116e18   (expected value / near pointer)
  P4: 0x0000000000000000
```

P1=3 means the kernel detected a corrupted doubly-linked list (`LIST_ENTRY`
validation failed). This is a kernel-internal data structure integrity check,
not a page fault or IRQ routing issue.

**Minidump**: `C:\WINDOWS\Minidump\043026-7859-01.dmp`

## What was running

Agent Cam ran `pingpong-self.ps1` via the PowerShell tool in Claude Code.
The user backgrounded it. The script:

1. Cleaned `build-output/`
2. Staged seed ELF from `seed/Codex.Codex.elf`
3. Dumped source via `concat-codex-self.ps1`
4. Launched QEMU via `Start-QemuRun` (from `qemu-config.ps1`)

The QEMU command line included `-machine kernel-irqchip=off` (line 183 of
`qemu-config.ps1`) and `-accel whpx`. This is the canonical, mitigated path.

**Correction (cross-referencing Nib's report, CL 515):** Agent Nib also
launched `pingpong-self.ps1` at approximately the same time (~06:11:54 per
Nib's logs). Both agents used the PowerShell harness with `kernel-irqchip=off`.
The actual guest count at crash time was likely **two** (one per agent), not
one as originally reported. The previous BSODs in this cluster involved
either multiple guests or the unmitigated `.sh` path.

**Timeline ambiguity (from Nib's report):** The 6008 event records a previous
shutdown at 5:48:42 AM. This may indicate the crash occurred earlier than the
06:28 WER timestamp suggests. The bugcheck data is authoritative regardless
of the exact crash moment.

## Comparison with prior BSODs

| Time | Bugcheck | Code | irqchip=off? | Guests | Agent |
|------|----------|------|:---:|:---:|-------|
| **04/30 06:28** | **0x139** KERNEL_SECURITY_CHECK_FAILURE | **LIST_ENTRY corrupt** | **YES** | **2 (Cam+Nib)** | **Cam+Nib** |
| 04/30 05:08 | 0x50 PAGE_FAULT_IN_NONPAGED_AREA | KUSER_SHARED_DATA | NO | 3 | Cam+Nib (.sh) |
| 04/29 09:31 | 0x50 PAGE_FAULT_IN_NONPAGED_AREA | | NO | multi | Nib (.sh) |
| 04/29 09:04 | 0x50 PAGE_FAULT_IN_NONPAGED_AREA | | NO | multi | Nib (.sh) |
| 04/29 08:45 | 0x4E PFN_LIST_CORRUPT | | NO | multi | Nib (.sh) |
| 04/29 05:35 | 0x0A IRQL_NOT_LESS_OR_EQUAL | | YES | multi | sweep |
| 04/29 01:11 | 0x13A KERNEL_MODE_HEAP_CORRUPTION | | YES | multi | sweep |
| 04/28 22:31 | 0x7F UNEXPECTED_KERNEL_MODE_TRAP | | ? | ? | |
| 04/28 18:36 | 0xD1 DRIVER_IRQL_NOT_LESS_OR_EQUAL | | ? | ? | |

**New pattern**: 0x139 with P1=3 (LIST_ENTRY corruption) is a memory-safety
violation inside the kernel, distinct from the IRQ-routing page faults (0x50)
that `kernel-irqchip=off` was designed to mitigate. This may indicate a
second corruption vector in WHPX, but without minidump symbol analysis we
cannot confirm the faulting module is WHPX-related:

1. **IRQ-path corruption** — mitigated by `kernel-irqchip=off` (the 0x50 cluster)
2. **List-entry corruption** — possibly not mitigated (this crash, 0x139), pending confirmation

## Environment

| Component | Value |
|-----------|-------|
| **Host OS** | Windows 11 Pro 10.0.26200, 64-bit |
| **CPU** | Intel Core i7-12700KF (12C/20T, 8P+4E, Alder Lake) |
| **RAM** | 32 GB DDR5-7200 |
| **QEMU** | 11.0.0 (`v11.0.0-12122-ga4bb4b10c9`) |
| **WHPX drivers** | `winhv.sys` 10.0.26100.4768, `winhvr.sys` 10.0.26100.7309, `Vid.sys` 10.0.26100.7920 |
| **Workaround** | `-machine kernel-irqchip=off` — ACTIVE |
| **Parallel guests** | 2 (Cam + Nib both running pingpong-self.ps1) |

## Implications for upstream reports

This crash should be appended to QEMU upstream #3460 and/or the Microsoft
Feedback Hub report. Key points for upstream:

- `kernel-irqchip=off` may not fully prevent the bug (first crash with it active)
- Two-guest reproduction (Cam + Nib concurrent pingpong) with the workaround active
- Different bugcheck family (0x139 LIST_ENTRY vs 0x50 page fault)
- Minidump symbol analysis needed to confirm faulting module is WHPX-related

## Action items

1. Update upstream QEMU #3460 with this new bugcheck code and single-guest repro
2. Consider whether WHPX is viable for sustained automated use on this hardware
3. No change to the `.ps1` harness — `kernel-irqchip=off` remains correct but insufficient
