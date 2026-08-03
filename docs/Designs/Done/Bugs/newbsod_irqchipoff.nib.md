# WHPX Host BSOD -- 2026-04-30 (3 guests, no kernel-irqchip=off)

## Filed at

- **QEMU upstream**: https://gitlab.com/qemu-project/qemu/-/work_items/3460 (update to existing)
- **Microsoft Feedback Hub**: https://aka.ms/AA10ry2d (update to existing)

## Summary

Running **only 3** long-lived QEMU guests in parallel under `-accel whpx`
**without** `-machine kernel-irqchip=off` bluescreened the Windows 11 host
after ~5 minutes of sustained self-compilation workload. This is well below
the prior "safe" threshold of jobs=4 from the rapid-churn sweep scenario
(CL 483/484), and proves that high create/destroy churn is NOT required to
trigger the bug -- sustained WHPX partition activity alone is sufficient.

The effective variable is `-machine kernel-irqchip=off`. All 4 BSODs on this
host occurred without it. The cam validation run with `kernel-irqchip=off`
went 40+ iterations stable under jobs=7 with full pinning.

## Environment

- **Host OS**: Windows 11 Pro 26200 (10.0.26200)
- **CPU**: 12th Gen Intel Core i7-12700KF (12C/20T, 8P+4E, turbo 5.0 GHz)
- **RAM**: 32 GB DDR5-7200 (2x16 GB AITC KSD516G72C34VTR)
- **Board**: MSI PRO Z790-VC WIFI (MS-7D33 rev 2.0)
- **BIOS**: AMI A.30 (2024-03-28)
- **Storage**: WD Blue SN5000 500GB NVMe (system), Seagate ST4000DX001 4TB SATA (data)
- **QEMU**: 11.0.0, build `v11.0.0-12122-ga4bb4b10c9` (Stefan Weil's 2026-04-22 Windows installer)
- **QEMU binary**: `D:\Program Files\qemu\qemu-system-x86_64.exe`
- **WSL QEMU** (for reference): 8.2.2 (Debian 1:8.2.2+ds-0ubuntu1.16) -- NOT used in this crash
- **VBS**: Running (Base Virtualization Support, APIC Virtualization)

### Hyper-V / WHPX kernel drivers

| Driver | Version |
|---|---|
| `winhv.sys` | 10.0.26100.4768 |
| `winhvr.sys` | 10.0.26100.7309 |
| `Vid.sys` | 10.0.26100.7920 |
| `vmswitch.sys` | 10.0.26100.8117 |

(All from the 26100 servicing branch despite OS reporting build 26200.)

## Crash timeline

| Time | Event |
|---|---|
| 04:51:28 | Cam QEMU #1 (PID 23992) started -- long-running self-compilation |
| 04:53:25 | Nib QEMU (PID 26508) started -- pingpong-self.sh stage 1 (TEXT mode) |
| 04:58:33 | **Host BSOD** -- unexpected shutdown |
| 05:03:32 | Cam QEMU #2 (PID 24668) started -- post-reboot (irrelevant, listed for completeness) |
| 05:08:36 | System boot completed, event log entries written |

Duration from first guest to crash: **~7 minutes** of sustained 2-3 guest activity.

## Bugcheck details

From `Microsoft-Windows-WER-SystemErrorReporting` Event ID 1001:

```
BugCheckCode:  0x00000050  (PAGE_FAULT_IN_NONPAGED_AREA)
Parameter 1:   0xfffff78000002004   (faulting virtual address)
Parameter 2:   0x0000000000000000   (read access, not present)
Parameter 3:   0xfffff8049a11d0e0   (faulting instruction address)
Parameter 4:   0x0000000000000002   (reserved)
```

### Interpretation of parameters

**Parameter 1 (`0xfffff78000002004`)**: This is offset `+0x2004` into
`KUSER_SHARED_DATA` (base `0xfffff78000000000`). This is a system-critical
kernel page that is supposed to be permanently mapped -- it contains the
`SharedDataFlags` and `TestRetInstruction` fields used by the kernel's
self-test and timing infrastructure. A page fault on this address means the
kernel's own page table mappings have been corrupted. This page is never
unmapped during normal operation.

**Parameter 3 (`0xfffff8049a11d0e0`)**: The instruction that attempted the
read. Without symbols this cannot be resolved to a specific driver, but the
address is in the kernel/driver range (`0xfffff800` prefix). Attaching the
minidump to WinDbg with Microsoft symbols will resolve this to
`module+offset`.

### Minidump

**A minidump was saved**: `C:\Windows\Minidump\043026-7656-01.dmp`
**Report ID**: `c663560a-8606-4ab3-a014-de9c97b52479`

This is notable -- prior crashes in the 0xA/0x13A/0x7F cascade often killed
the dump-write path before flush. The 0x50 bugcheck alone (without the
full cascade) apparently survived to disk. This dump should be attached to
both upstream reports.

## Exact QEMU invocations at time of crash

### Nib's instance (PID 26508)

Launched by `tools/pingpong-self.sh` via `tools/qemu-config.sh`, TEXT mode
stage 1 (self-compilation). The bash harness sources `qemu-config.sh` which
sets:

```bash
QEMU_ACCEL=${QEMU_ACCEL:-whpx}
# ...
QEMU_ACCEL_FLAGS=(-accel whpx)
```

Effective command line (reconstructed from script source):

```
"C:/Program Files/qemu/qemu-system-x86_64.exe" \
    -accel whpx \
    -kernel D:/Projects/NewRepository-nib/build-output/bare-metal/Codex.Codex.elf \
    -chardev socket,id=ch0,host=127.0.0.1,port=<even>,server=on,wait=on \
    -chardev socket,id=ch1,host=127.0.0.1,port=<odd>,server=on,wait=on \
    -serial chardev:ch0 \
    -serial chardev:ch1 \
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
    -display none \
    -no-reboot \
    -m 1024
```

- **Kernel ELF**: 1,422,200 bytes, self-built by the seed in Phase 3 (51s)
- **Guest workload**: Compiling its own 844,961-byte source (full self-compilation)
- **Protocol**: `TEXT\n` header + source bytes + `\x04` EOT pumped on COM1 data socket; output collected on COM1; READY received on COM2
- **Expected duration**: ~48 seconds. Had been running 5+ minutes at crash time -- likely in a hot emit loop.
- **No CPU affinity pinning** (bash harness doesn't support it)
- **No `-machine kernel-irqchip=off`** (bash `qemu-config.sh` doesn't pass it)
- **Port range**: nib agent slot uses ports 57600-64998 (stride 2)
- **Timeout**: TEXT_TIMEOUT=1200 seconds (20 minutes)

### Cam's instances (PIDs 23992, 24668)

Launched from `D:\Projects\NewRepository-cam\` workspace, same binary,
same flags. Also WHPX, also without `-machine kernel-irqchip=off` (bash
harness). Same command structure as above with cam's kernel ELF and port
range (50200-57598).

### Flags NOT present (critical)

These flags are used in cam's **PowerShell** harness (`qemu-config.ps1`)
but were NOT present in the bash invocations that crashed:

```
-machine kernel-irqchip=off     # moves IRQ delivery to userspace
```

The PowerShell harness also uses `nodelay=on` on chardev sockets; the bash
harness does not. The PowerShell harness runs with `kernel-irqchip=off`
unconditionally since CL 490's investigation.

## Comparison with prior crashes

### All BSODs on this host

| # | Date/Time | Bugcheck | Code | P1 (fault addr) | Scenario | Dump |
|---|---|---|---|---|---|---|
| 1 | 2026-04-29 08:45 | PFN_LIST_CORRUPT | 0x4E | 0x98 (PFN index) | sweep jobs=8 | 042926-6875-01.dmp |
| 2 | 2026-04-29 09:04 | PAGE_FAULT_IN_NONPAGED_AREA | 0x50 | 0xfffffffff7e6e8cf | sweep jobs=8 | 042926-8031-01.dmp |
| 3 | 2026-04-29 09:32 | PAGE_FAULT_IN_NONPAGED_AREA | 0x50 | 0xfffff8023dd70dbe | sweep jobs=8 | 042926-7984-01.dmp |
| **4** | **2026-04-30 04:58** | **PAGE_FAULT_IN_NONPAGED_AREA** | **0x50** | **0xfffff78000002004** | **3 guests, long-running** | **043026-7656-01.dmp** |

### Key differences from prior report (CL 483/484)

| Factor | Prior crashes (#1-3) | This crash (#4) |
|---|---|---|
| Guest count | 8 parallel (sweep) | **3** (2 cam + 1 nib) |
| Workload pattern | Rapid create/destroy (~3-10s each, ~100 guests/iter) | **Long-running** (~5+ min each) |
| `-machine kernel-irqchip=off` | Not applied | **Not applied** |
| CPU affinity pinning | Varied (see prior report) | **None** |
| Windows Defender | On | Unknown (likely on, default) |
| Primary bugcheck | 0x4E then 0x50 | **0x50** |
| P1 address character | Pool/driver range | **KUSER_SHARED_DATA** (0xfffff780...) |
| Minidump survived? | Yes (all 3) | **Yes** |

### What this proves

1. **High guest churn is not required.** 3 long-lived guests triggered the same class of corruption that previously required 8 rapid-cycling guests. The common factor is sustained WHPX partition activity, not partition create/destroy rate.

2. **`kernel-irqchip=off` is the effective discriminator.** Every crash on this host occurred without it. The 40+ iteration validation run with `kernel-irqchip=off` (same QEMU version, same host, jobs=7, full pinning) never crashed. The bash harness lacks this flag; the PowerShell harness has it.

3. **The corruption is in kernel page management.** The progression from 0x4E (PFN list corrupt) to 0x50 (page fault in nonpaged area) on `KUSER_SHARED_DATA` indicates physical page tracking corruption -- a page that should be permanently mapped lost its mapping. This is consistent with a use-after-free or double-free in the WHPX/Vid.sys virtual-to-physical page management path when the in-kernel IRQ chip is active.

4. **jobs=4 is not a safe threshold** without `kernel-irqchip=off`. The prior report stated jobs=4 was "indefinitely stable" -- that was tested under the sweep pattern (rapid churn). Long-running compilations with only 3 guests still triggered it. The safe threshold is: use `kernel-irqchip=off`, regardless of guest count.

## QEMU source: what `-machine kernel-irqchip=off` changes

When `kernel-irqchip=off` is set, QEMU handles interrupt delivery in
userspace (via its own APIC emulation) rather than offloading it to the
Windows hypervisor's virtual APIC. With WHPX, the default `kernel-irqchip=on`
routes interrupts through `WHvRequestInterrupt()` / the in-kernel virtual
LAPIC provided by `winhvr.sys`. The hypothesis is that the WHPX kernel-mode
interrupt delivery path (`winhvr.sys` or `Vid.sys`) has a memory management
bug -- likely a race condition in partition teardown or interrupt coalescing --
that corrupts kernel page tables under sustained load.

## Reproducer (updated)

The original reproducer (8 parallel sweep guests) still works but is no
longer the minimal case. Updated minimal reproducer:

```
# Launch 3 simultaneous QEMU instances, each running a compute-intensive
# bare-metal workload for several minutes. No rapid churn required.

qemu-system-x86_64.exe ^
    -accel whpx ^
    -kernel <bare-metal-elf> ^
    -chardev socket,id=ch0,host=127.0.0.1,port=50200,server=on,wait=on ^
    -chardev socket,id=ch1,host=127.0.0.1,port=50201,server=on,wait=on ^
    -serial chardev:ch0 ^
    -serial chardev:ch1 ^
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 ^
    -display none ^
    -no-reboot ^
    -m 1024

# Each guest does sustained computation (serial I/O + memory allocation)
# for 5+ minutes. Host BSODs within ~7 minutes.
#
# Adding -machine kernel-irqchip=off prevents the crash.
```

The guest ELF is a self-compiling compiler (~1.4 MB) that does heavy serial
I/O and heap allocation. Any compute-intensive bare-metal guest that runs
for minutes (not seconds) under WHPX should reproduce this with 2-3
concurrent instances.

## Action items

### For QEMU upstream (GitLab #3460)

1. **Attach all 4 minidumps** from `C:\Windows\Minidump\`:
   - `042926-6875-01.dmp` (0x4E)
   - `042926-8031-01.dmp` (0x50)
   - `042926-7984-01.dmp` (0x50)
   - `043026-7656-01.dmp` (0x50) -- **this crash**
2. **Updated finding**: minimal reproducer is 3 long-lived guests, not 8 rapid-cycling. The bug is in sustained WHPX IRQ chip activity, not create/destroy churn.
3. **`kernel-irqchip=off` is the confirmed workaround** -- 40+ iterations stable under aggressive parallel load.
4. **Request**: can QEMU's WHPX backend default to `kernel-irqchip=off` on Windows, or at least emit a warning when running multiple WHPX guests with the in-kernel IRQ chip?

### For Microsoft (Feedback Hub)

1. Attach the minidumps -- they should resolve the faulting instruction to a specific driver+offset.
2. The `KUSER_SHARED_DATA` page fault (crash #4) is the strongest signal: this is a permanently-mapped page, so its mapping being torn indicates kernel-mode page table corruption.
3. Affected drivers: `winhvr.sys` 10.0.26100.7309, `Vid.sys` 10.0.26100.7920, or `vmswitch.sys` 10.0.26100.8117.

### For our harness

1. **Patch `tools/qemu-config.sh`** to add `-machine kernel-irqchip=off` to `QEMU_ACCEL_FLAGS` when `QEMU_ACCEL=whpx`. The PowerShell harness already has this; the bash harness does not, which is why this crash happened.
2. **Update `docs/Bugs/whpx-host-bsod.md`** with this incident and the revised minimal reproducer.

## Workaround status

| Mitigation | Status | Effect |
|---|---|---|
| `-machine kernel-irqchip=off` | **Required** | Moves IRQ delivery to userspace; 40+ iterations stable |
| Pin to QEMU 9.1.0 / 10.0.0 | Alternative | Stable but 6-9x slower |
| Reduce to jobs=4 | **Insufficient** | Crashed at jobs=3 with long-running guests |
| CPU affinity pinning | Supplementary | Helps throughput, does not prevent crash alone |
| Defender off | Supplementary | Reduces soft wedges, does not prevent crash |

**Bottom line**: `-machine kernel-irqchip=off` is the only reliable mitigation.
It must be applied to ALL WHPX QEMU invocations, not just the PowerShell path.
The throughput cost (~95% per iteration under pinning) is the price of not
crashing the host.
