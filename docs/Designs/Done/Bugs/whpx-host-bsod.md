# Host BSOD under sustained 8x parallel WHPX guest workload

## Filed at

- **QEMU upstream**: https://gitlab.com/qemu-project/qemu/-/work_items/3460
- **Microsoft Feedback Hub**: https://aka.ms/AA10ry2d

## Title (for the issue)
WHPX: host bluescreen (IRQL_NOT_LESS_OR_EQUAL + cascading heap corruption) after 4-7 minutes of 8 parallel small guests

## Summary
Running 8 short-lived QEMU guests in parallel under `-accel whpx` reliably bluescreens the Windows 11 host within 4-7 iterations of a sweep workload (≈5-10 minutes of sustained parallel activity). Earlier QEMU releases (9.1.0, 10.0.0) using the same harness on the same machine do not crash; only the v11.0.0 development snapshot does, indicating a regression.

## Environment

- **Host OS**: Windows 11 Pro 26200 (10.0.26200)
- **CPU**: x86_64 (host VM-capable)
- **QEMU (crashing)**: 11.0.0, build `v11.0.0-12122-ga4bb4b10c9` (Stefan Weil's 2026-04-22 Windows installer)
- **QEMU (stable, slower)**: 9.1.0 `v9.1.0-12064-gc658eebf44` and 10.0.0 `v10.0.0-12080-g252feb9469-dirty`
- **Driver Verifier**: Standard flags (0x1209BB) on `winhvr.sys`, `winhv.sys`, `Vid.sys`, `vmswitch.sys` — does NOT trip a verifier-specific bugcheck (0xC4)

## Reproducer

8 simultaneous QEMU instances launched in a tight harness loop. Each guest (this is the BSOD repro — without `kernel-irqchip=off`, which is now our canonical default but disabled here to make the bug observable):

```
qemu-system-x86_64.exe \
  -accel whpx \
  -kernel <small bare-metal ELF, ~1.3 MB> \
  -chardev socket,id=ch0,host=127.0.0.1,port=N+0,server=on,wait=on \
  -chardev socket,id=ch1,host=127.0.0.1,port=N+1,server=on,wait=on \
  -serial chardev:ch0 \
  -serial chardev:ch1 \
  -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
  -display none \
  -no-reboot \
  -m 1024
```

The harness:
1. Launches 8 of these in parallel (jobs=8 via PowerShell `ForEach-Object -Parallel`).
2. Each guest connects two TCP sockets, exchanges a small binary payload over the data channel, reads a result, exits.
3. Per-guest lifetime: ~3-10 seconds.
4. Throughput: ~14 sequential 8-wide batches per "sweep iteration" (105 guests/iter, ~62-68s total wall-clock with 11.0.0).
5. Loop the iteration. **Host BSODs within 4-7 iterations** = ~5 minutes of sustained activity.

QEMU 9.1.0 and 10.0.0 with the SAME harness on the SAME host: stable but ~6-9× slower per iteration. Suggests a regression on master between 10.x and 11.0.0 affecting WHPX stability.

## Crash signature (Win32 bugcheck)

Primary `Microsoft-Windows-Kernel-Power` event 41:

```
BugCheckCode (P1): 0xA  (IRQL_NOT_LESS_OR_EQUAL)
Param1 (P2):       0xFFFFE500184C9CF8   (referenced address — kernel range)
Param2 (P3):       0x2                  (IRQL = DISPATCH_LEVEL)
Param3 (P4):       0x0                  (read access)
Param4 (P5):       0xFFFFF805AA0F1B85   (faulting instruction address)
```

Cascading WER bluescreen entries from the same crash window (likely follow-on collapses as the kernel dies):

| P1 | Bugcheck | Notes |
|----|----------|-------|
| 0xA | IRQL_NOT_LESS_OR_EQUAL | Primary |
| 0x13A | KERNEL_MODE_HEAP_CORRUPTION | P2=0x17 internal pool corruption signature |
| 0x7F | UNEXPECTED_KERNEL_MODE_TRAP | P2=8 (double fault) |
| 0xD1 | DRIVER_IRQL_NOT_LESS_OR_EQUAL | |
| 0x3B | SYSTEM_SERVICE_EXCEPTION | P2=0xC0000005 access violation |
| 0x50 | PAGE_FAULT_IN_NONPAGED_AREA | |
| 0x141 | LIVE_KERNEL_DUMP | |

Each crash run produces a similar set; addresses change but the bugcheck mix is consistent.

## What's happening (interpretation)

The combination of `0xA` (IRQL violation reading kernel memory) followed by `0x13A` heap corruption and a `0x7F` double fault is a classic kernel-side use-after-free or buffer overrun — likely in WHPX kernel components (`winhvr.sys`/`Vid.sys`/`vmswitch.sys`) triggered by the QEMU 11.0 WHPX accelerator backend's API usage pattern under high create/destroy churn (8 partitions × ~100 creates/iter).

This is most likely a Microsoft kernel bug (guest workload should never BSOD the host) but is triggered by QEMU 11's WHPX backend specifically — 9.1.0 and 10.0.0 don't trip it.

## Diagnostics gap

- **No `Memory.dmp` written.** The heap-corruption + double-fault sequence kills the dump-write path before flush. Verifier didn't help here.
- **QEMU `-D <file> -d guest_errors,unimp,cpu_reset` log** captures only normal CPU resets; nothing anomalous before the host dies.
- Without a kernel dump, `0xFFFFF805AA0F1B85` cannot be resolved to driver+offset.

To resolve the faulting instruction:
- Attach kernel debugger via `bcdedit /debug on` + WinDbg before next repro.
- Or reduce verifier flags so the dump survives (current standard flags 0x1209BB don't trip but also don't tame the cascade).

## Workaround

**Canonical (since 2026-04-29):** pass `-machine kernel-irqchip=off` to QEMU. Probed at the request of QEMU upstream. With the in-userspace IRQ path, the host BSOD does not reproduce within the validation window (40+ iterations under jobs=7 + full pinning, host uptime 4+ hours stable; reproduced on a second independent machine with the same numbers). Throughput cost ~95% per iteration under pinning (185-195s/iter vs ~100s baseline). Without pinning the cost balloons to ~7× and the host CPU sits at ~20% — the slowdown is dominated by guest↔WHPX context-switch latency, not compute. Cam's harness now passes this flag unconditionally.

If `kernel-irqchip=off` is not viable (latency cost too high, or working with a different reproducer), the prior workarounds remain valid:

**A) Pin to QEMU 9.1.0 or 10.0.0** (Stefan Weil's signed installers from `https://qemu.weilnetz.de/w64/2024/` and `https://qemu.weilnetz.de/w64/2025/`). Both stable but 6-9× slower per iteration than 11.0.0 on the same WHPX host.

**B) QEMU 11.0.0, jobs=4, no pinning, kernel-irqchip default.** Indefinitely stable. ~195s/iter (measured 2026-04-29: 192s, 197s, 196s across the first three iterations on this host with Defender disabled). Roughly 2x slower than option C below.

**C) QEMU 11.0.0, jobs=7, with full mitigation stack, kernel-irqchip default.** 19+ iterations stable (~32 min) at ~100s/iter. Requires *all* of:

1. **Pin each QEMU process to a full physical P-core** (both HT siblings) via `ProcessorAffinity = 3 << coreId`. Logical-core (1-bit) pinning is too tight — starves QEMU's IO/monitor threads and silently corrupts stdin bytes mid-compile. Cores 0+1 (P-core 0) reserved; guests use P-cores 1-7 (mask anchors 2,4,6,8,10,12,14).
2. **Pin the harness orchestrator** (parent PowerShell + `ForEach-Object -Parallel` runspaces) to P-core 0 (mask = 3) so it doesn't compete with guest cores.
3. **Turn off Windows Defender real-time scanning** (auto-reverts on every reboot — re-disable after each BSOD repro). Defender's on-create/on-access scan of fresh ELFs across 7 parallel workers steals cycles from WHPX vCPU threads and produces sporadic guest-never-READY soft wedges.
4. **Generous serial-READY timeouts** in the harness (30s on the compile side, 30s + 60s wall budget on the runtime side) plus a `taskkill /F /T` fallback to clean up wedged QEMUs that survive `Stop-Process`. Without these, transient host-scheduler hiccups bucket as fail-runtime; with them, they recover (iter-10 of the validation run took 219s vs the steady 100s but completed clean).

Cam's harness ships with the hooks: `codex.build/sweep.ps1 -Pin -Jobs 7`, `codex.build/qemu-config.ps1` `-CoreId` parameter, `codex.build/stress-sweep.ps1` for repeated runs.

### Configurations tested (2026-04-29 on the validation host)

| jobs | guest pin | host pin | Defender | READY budgets | Result |
|---|---|---|---|---|---|
| 8 | none | none | on | default | BSOD iter 4-7 |
| 7 | none | none | on | default | BSOD iter 18 |
| 7 | 1-bit (logical) | none | off | default | compile-side stdin corruption iter 3-4 |
| 7 | 2-bit (full P-core) | none | on | 30s | soft wedge iter 5 |
| 7 | 2-bit | none | off | 30s | BSOD iter 9 |
| 6 | 2-bit | none | off | 30s | soft wedge iter 3 (no BSOD) |
| 5 | 2-bit | none | off | 30s | soft wedge iter 2 (no BSOD) |
| 5 | none | none | off | 30s | 3+ clean (~200s/iter) |
| **7** | **2-bit** | **P-core 0** | **off** | **30s** | **19 clean (~100s/iter)** |
| 4 | none | none | off | 30s | indefinitely stable, ~195s/iter (option B) |
| **7** | **2-bit** | **P-core 0** | **off** | **30s** + **`kernel-irqchip=off`** | **40+ clean (~190s/iter), no BSOD** |

The "host pin" line is what makes the difference between BSOD-at-iter-9 and 19-iter-stable: removing the harness orchestrator from the guest cores leaves WHPX kernel work room to schedule.

---

## Addendum: Host hardware

For reproducer correlation. Same machine repros all results above (jobs=8 BSOD, jobs=4 stable, slowdown on 9.1.0/10.0.0).

### CPU
- **Model**: 12th Gen Intel Core i7-12700KF
- **Topology**: 12 cores / 20 logical processors (8 P-cores hyperthreaded + 4 E-cores)
- **Base clock**: 3.6 GHz (turbo 5.0 GHz per Intel spec)
- **Cache**: 12 MB L2, 25 MB L3
- VT-x / EPT enabled in BIOS (a hypervisor is running per `systeminfo`; VBS active)

### Memory
- **Total**: 32 GB (2 × 16 GB)
- **DIMMs**: AITC KSD516G72C34VTR DDR5
- **Speed**: 7200 MT/s (configured 7200)

### Motherboard / firmware
- **Board**: MSI PRO Z790-VC WIFI (MS-7D33), revision 2.0
- **Chipset**: Intel Z790
- **BIOS**: AMI A.30, dated 2024-03-28

### Storage
- **System drive**: WD Blue SN5000 500GB NVMe (M.2)
- **Secondary**: Seagate ST4000DX001 4 TB SATA HDD

### OS
- **Edition**: Windows 11 Pro
- **Build**: 10.0.26200 (build 26200)
- **Architecture**: x64
- **Virtualization-Based Security**: Running (Base Virtualization Support, APIC Virtualization)

### Hyper-V / WHPX kernel drivers (file versions)

| Driver | Version |
|---|---|
| `winhv.sys` | 10.0.26100.4768 |
| `winhvr.sys` | 10.0.26100.7309 |
| `Vid.sys` | 10.0.26100.7920 |
| `vmswitch.sys` | 10.0.26100.8117 |

(All from the 26100 servicing branch despite OS reporting build 26200 — this is normal for cumulative-update split.)

### Crash dump configuration
- **CrashDumpEnabled**: 3 (Kernel Memory Dump)
- **DumpFile**: `C:\Windows\Memory.dmp`
- **AutoReboot**: 1
- **Driver Verifier** (during repro): Standard flags 0x1209BB on `winhv.sys`, `winhvr.sys`, `Vid.sys`, `vmswitch.sys` — did NOT trip a verifier-specific bugcheck (no 0xC4)
- **No `Memory.dmp` was written** despite the configuration; the heap-corruption + double-fault cascade kills the dump path before flush

### Thresholds observed on this machine

See the configurations-tested matrix in the **Workaround** section above for the full sweep across jobs ∈ {4..8}, pin variants, Defender on/off, and READY budgets. Headline summary:

- **jobs=8 unpinned**: BSOD within 4-7 iterations.
- **jobs=4 unpinned**: indefinitely stable.
- **jobs=7 unpinned**: BSOD by iter 18 (slower repro but still hits).
- **jobs=7 with full mitigation stack** (2-bit P-core pin + host orchestrator pinned off the guest cores + Defender off + 30s READY budgets): 19 iterations stable, no crash, no soft wedges.

Hardware coupling is plausible (cores, RAM bandwidth, NVMe queue depth). Other reporters with different hosts may see BSOD thresholds shifted up or down. The qualitative finding — that guest pinning alone is insufficient and the harness orchestrator must also be evacuated from guest cores — is likely portable.

---

## Where to file this

### 1. QEMU upstream (GitLab) — PRIMARY
**URL**: https://gitlab.com/qemu-project/qemu/-/issues/new
- Requires a GitLab account
- Assign label `accel/WHPX` if available (existing WHPX issues are tagged)
- Cross-reference: existing WHPX issues #2402, #2403, #2461 — none match this exactly (host BSOD under parallel load), so it's a fresh report

### 2. Microsoft Feedback Hub — SECONDARY (Hyper-V/WHPX is MS code)
- Open Feedback Hub (Win+F)
- Category: "Apps and Drivers" → "Hyper-V" (or "Virtualization")
- Title: "WHPX: host bluescreen under parallel small guest workload (QEMU 11.0)"
- Attach: this report text
- Reference: bugcheck codes 0xA → 0x13A → 0x7F cascade

Filing both paths is reasonable. The QEMU dev can confirm whether their WHPX backend changed something between 10.x and master that's now provoking a latent MS kernel bug; the MS team can fix the actual kernel-mode hole.

---

## Incident timeline updates (2026-04-30 / 2026-05-02)

This section records short observational reports from incidents
subsequent to the canonical writeup above. Each shifts the operating
theory of the bug. Per-incident detail (bugcheck codes, stack traces,
crash timeline) is in the dedicated `bsodN_*.md` files in this folder.

### 2026-04-30 — nib jobs=7 post-CL 552 (pinning removed)

**Trigger:** Nib running `sweep.ps1 -Jobs 7` on CL 553 head (post-CL
552: `whpx,hyperv=off`, no CPU pinning).

**Symptom:** Host BSOD. Same class as the CL 483/495 incidents.

**Context:** CL 552 removed guest `ProcessorAffinity` pinning and
hardcoded `-accel whpx,hyperv=off`. The rationale was that pinning
caused harness flakiness (socket read failures, READY timeouts, hung
sweeps). Unpinned sweeps at jobs=4 ran clean 3× on Cam's workspace.
Nib ran jobs=7 and triggered the BSOD.

**Prior mitigation (CL 495):** `-machine kernel-irqchip=off`. That flag
had been dropped from the harness at some point before CL 552. Pinning
was added in CL 490 as an additional layer; CL 552 removed pinning.
CL 555 restored `kernel-irqchip=off`.

**Theory at this point:** The BSOD is a WHPX hypervisor bug triggered
by high concurrent guest count. `kernel-irqchip=off` reduces the
surface but does not eliminate it. CPU pinning may have been masking
the race by serializing WHPX calls onto fewer cores. Removing it
widened the window.

**Open question:** Is the trigger guest count (7 vs 4), lack of
pinning, or both? Need to A/B: pinned at jobs=7 vs unpinned at jobs=4.

### 2026-04-30 — cross-agent aggregate (~9–10 guests)

**Trigger:** Cam pingpong + Nib sweep running concurrently. Config:
`-accel whpx,hyperv=off`, `-machine kernel-irqchip=off` (CL 555),
no CPU pinning. Cam had 2–3 QEMU guests (pingpong stages), Nib had
up to 7 (sweep). Combined ~9–10 concurrent WHPX guests.

**Symptom:** Host BSOD, same class.

**Context:** `kernel-irqchip=off` was restored in CL 555 after the
previous incident. Did not prevent a second BSOD when total
cross-agent guest count was high. This suggests the trigger is
aggregate WHPX guest count on the host, not per-agent job count alone.

**Updated theory:** Neither `kernel-irqchip=off` nor `hyperv=off`
eliminates the BSOD. The variable is total concurrent WHPX guests
across all processes on the host. Two agents each running jobs=4 can
still exceed the threshold. May need a system-wide semaphore or
coordination to cap total guests.

### 2026-05-02 — cam jobs=4 single agent

**Trigger:** Cam running `sweep.ps1 -Jobs 4`. Single agent, no peer
activity. Config: `-accel whpx,hyperv=off`, `-machine
kernel-irqchip=off`, no CPU pinning. Depot head at CL 647 + local
uncommitted changes in CL 633 (handler clause params plumbing — no
codegen or runtime changes to QEMU interaction).

**What was happening:** Iterating on CAMP-IIIC deferred items:
operations with arguments for effect handlers. Had just rebuilt the
SUT (`_rebuild-sut.ps1`, single QEMU guest), tested one sample
individually (`sample-compile-selfhost.ps1` + `run-for-sweep.ps1`,
1 guest each sequentially), then launched `sweep.ps1 -Jobs 4` (up to
4 concurrent guests for compile, then 4 for runtime). BSOD hit during
the sweep.

**Symptom:** Host BSOD, same class as prior incidents.

**Context:** First BSOD at jobs=4 with a single agent. Previous
incidents required either jobs=7 or cross-agent concurrency
(combined ~9–10 guests). This lowers the known threshold: 4 concurrent
WHPX guests on a single agent is sufficient to trigger the bug.

**Updated theory:** The threshold is lower than previously thought.
Jobs=4 is not safe. The BSOD correlates with any sustained parallel
WHPX activity, not just high guest counts. Possible contributing
factor: the host had been running multiple sequential QEMU sessions
(rebuild + individual sample compile/run) before the parallel sweep —
accumulated WHPX state or resource leaks may lower the bar for the
race condition.
