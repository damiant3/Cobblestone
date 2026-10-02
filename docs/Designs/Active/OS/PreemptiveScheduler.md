# The Preemptive Scheduler

*Damian, 2026-08-28, on where a webserver belongs: "the desktop is merely the
admin app, the webserver is a system level concern right". Then: "yeah well we
definitely need preemptive". Then, on the rulings: the desk is privileged and
the quantum is 10 ms.*

Stages 1 to 4 are done: every core runs a 10 ms quantum (stage 4, below).

## What the kernel already provides, measured 2026-08-28 (re-measure, L-COUNT)

**A preemptive SMP process kernel, emitted by the compiler's boot layer.**

- **Processes are real.** `proc-table-base` 20480, `proc-entry-size` 256; each
  entry carries state, a CR3, and a per-process heap base.
  `emit-build-process-page-tables` gives each process its own page tables.
- **Callable from Codex under `[Concurrent]`:** `process-spawn` (it takes a
  CLOSURE and answers a pid), `process-spawn-on-core`, `process-spawn-priority`,
  `process-spawn-with-heap`, `process-wait`, `process-yield`, `process-exit`,
  `process-exit-self`, `process-kill`, `process-status`, `process-count`,
  `process-get-pid`.
- **Per-process capability restriction:** `process-restrict-cap`,
  `process-get-cap`, `process-set-scope`, `process-get-scope`,
  `process-set-network-scope`, `process-get-network-scope`.
- **Kernel channels:** `chan-kern-create`, `-send`, `-recv`, `-send-block`,
  `-recv-block`, plus `chan-text-send` / `chan-text-recv`.
- **Preemption is tested, not assumed.** `codex/test/smp-preempt.codex` spawns
  six children that spin a billion iterations with no yield, no channel and no
  wait, and counts every timer interrupt taken on a core whose id is not zero
  into cell 36216 with a locked add. Beside it: `smp-dispatch`, `smp-halt`,
  `smp-proc0-pinned`, `spawn-reuse`, `process-exit-status`, `supervisor-pattern`,
  `supervisor-kill-restart`, `chan-lost-wakeup`, `scheduler-integration`.

**Each process has its own heap frontier.** The bump frontier is register `r10`
(`__heap-save` is `mov rd, r10`, `__heap-restore` is `mov r10, rd`), so it is
part of the CPU context and every process already has its own. The stack
collision guard is `cmp rsp, r10`, per context for the same reason, and
`emit-create-process` writes a per-process heap base.
The backing pool's ownership is a separate contract; see
[UEFI-owned process pool](../../../ArchitectsSketchbook.md#uefi-owned-process-pool).

## Open: `codex/os/sched` is a second, unrelated model of the same idea

A `Task` of `{id, name, priority, status}` with no body, a `try-dispatch` that
executes nothing, a `CoreHeap` that computes arena records nothing allocates
from, cited only by its own tests. It duplicates in records what the emitter
does in instructions. Whether it is deleted or kept as a bookkeeping view over
real processes is a decision for whoever next needs it, and nothing here waits
on it. [DeskScheduler](DeskScheduler.md) owns pane rates, bounded presentation
and the production render-worker integration; this page does not supersede it.

## Each process keeps its own XMM state

Every context save (the timer handler, `process-yield`, the two blocking IPC
waits, and the process-wait block) calls `__fx_save`, and `__process_resume`
restores the incoming process, so a switch preserves XMM0-XMM15 and MXCSR.
Each slot reserves its top 4096-byte page for vector state. Save/restore uses
the enabled XSAVE/XRSTOR path or FXSAVE/FXRSTOR fallback. Native boot and legacy
UEFI use the fixed pool; the v4 UEFI handoff selects the firmware-owned pool.
PID lookup, child carving and vector-state save/init/restore use the same
active pool base (`emit-load-spawn-base`, `X86_64Boot.codex`). A spawn seeds
the area with the spawner's state through
`__fx_init`, so the first restore never loads a zero MXCSR. The arm is
`codex/test/xmm-preempt` (`-smp 2`): four children on one core and 12,000,000
vector passes, 1 to 4 wrong results a run before the fix, 0 after. A probe of
this kind needs `__heap-save`/`__heap-restore` around each pass: a spawned child
has a 1 MB heap and a vector pass keeps 64 bytes.

## Damian's two rulings

**THE DESK IS PRIVILEGED -- implemented.** Proc 0 is pinned to the boot
processor: `__idle_dispatch` starts each core's scan at its own id and wraps to
1, so an application processor never reaches slot 0, and both preempt scans skip
slot 0 outright when the claiming core is not the BSP. Pinned by
`codex/test/smp-proc0-pinned.codex`. What is NOT there is a no-preempt window
around a paint on the BSP's own clock; whether that is needed is a measurement
once the desk shares a core, not an assumption.

**THE QUANTUM IS 10 ms**, which is stage 4 below.

## The shape that shipped: THE SPAWNER IS THE HOLDER

**RULING (root, 2026-09-07, on Damian's do-not-wait).** The kernel enforces
parent-bounded capability and `process-restrict-cap` runs in that direction; a
spawn that granted the closure's row would let a child gain what its parent
lacks and is seed-affecting; the desk's row gaining `Network.*` is forbidden.

**The runtime gap this answers is the CAPABILITY WORD, not the scope.**
`process-spawn` copies the parent's `proc-cap-offset` word into the child, and
nothing carries the closure's declared row to the kernel: the row
`process-spawn` takes under its `ForAllEff` is a type-level fact with no runtime
twin. A child of a parent holding no `Network` bits is refused by `net-send-raw`
and `net-recv-raw`. The e1000 path is raw MMIO under `Device.Mmio` and checks
nothing; the default bed is the NE2000.

**The scope cell has three writers and its check is live.** The boot grant
writes proc 0's cell from the opening's scoped `Network.*` effect
(`X86_64Chapter.codex`, `manifest-opening-net-scope` into `emit-set-boot-scope`);
every spawn copies the parent's cell into the child
(`X86_64ProcessHelpers.codex`, beside the capability-word copy); and
`process-set-network-scope` is gated on the caller's admin bit. An empty cell is
an UNSCOPED effect and is admitted by the language's own meaning of one.

**The implementation is `gopweb-hold` in `apps/works/GopWeb.codex`:** proc 0
spawns the service, then restricts its OWN `cap-network-read` and
`cap-network-write`, then runs the desk. The desk stays proc 0; a spawned child
gets the explicit 8 MiB `gopweb-heap-bytes` grant through
`process-spawn-with-heap`. `apps/works/DeskVm.codex`'s `opening`
carries `Concurrent, Capability, Network.Read, Network.Write` because
`gopweb-hold`'s row demands them, so dropping them is a compile error rather
than a silent loss of the bits.

**The order is a load-bearing invariant: spawn, then restrict.** After the
restrict, proc 0 cannot regain Network in this boot. A replacement process
spawned afterward inherits the restricted word and is refused. Start and
Restart commands reuse the existing service process.

**The falsifier is `codex/test/apps/gopweb-hold`** (smp 4): the service's
capability word carries both Network bits, the holder's own `net-status` is -1
after the restrict, and a child spawned after the restrict reads -1 too. Deleting
the two restricts turns the second and third readings False while the first
stays True.

**`codex/test/apps/gopweb-spawn` measures the dispatch and nothing about the
network.** Its row declares no `Network` while the service it spawns runs under
both bits, so the chapter compiling IS the evidence that a child's effects do
not reach the spawner; widening that row still compiles and deletes the evidence
silently, which is why the file says not to.

**Where the service runs.** It lands wherever the scheduler puts it. The desk
collects input before deciding whether to yield; the pane policy bounds bursts
of queued input. `gopweb-pump` yields on empty polls. These cooperative points
let other processes on the boot processor run. `gopweb-pump`
has no round bound: it compacts its heap in place (`web-mux-compact`) and
lives until it is killed.

**An idle loop waits for the tick.** After `web-idle-polls` empty polls in a
row, `gopweb-pump` and `web-mux-run` wait for `get-ticks` to advance before the
next NIC read (`web-idle-wait`, `codex/os/net/WebServer.codex`), because each
empty poll is a VM exit; a frame that arrives during the wait is answered at
the next poll, at most one tick late.

**Acceptance, green** (val, 2026-09-08, smp 4, headless with `-portfwd
9100:9100`): a host `GET /` answers 200 in 0.2 s and again at 47 s, and the 60 s
frame shows the desk painted with its clock at 15,550 iterations a second. With
the service sharing core 0 the desk ran at 13,805 against 14,764 with no service
at all, so the yield costs it about six percent. Damian at a browser is
confirmation, not the gate (L-HUMAN).

**The admin pane is `desk-focus-web` in `GopDesk.codex`,** a window over the
block `gopweb-hold` allocates and shares with the service: the service pumps its
own mux loop and reads a command cell each round. Stop closes the listener
and connections; Start from a stopped state reacquires DHCP before listening.
The service records observed requests in a 64-slot ring owned by `GopWebAdmin`.
Start, Stop and Restart use sequenced command acknowledgements; the pane shows
the DHCP binding, service state and filtered request log. The detailed contract
is [Web Server administration](../../../../apps/works/works-desk-contract.md#web-server-administration).
**Control rides the shared block, not
`chan-kern-*`,** because `desk-loop`'s row carries no `Concurrent` and a send would
widen `desk-loop`'s row through every step, and an integer channel cannot carry
a log line.

The Windows key opens the Cobblestone menu; arrows select and Enter launches.
Mouse launch remains available. Request logging observes the service transport,
including `/api/health`; it no longer depends on the application route seeing
every request. Native/OVMF rehearsals do not establish physical input delivery.

`GopBoot` captures the handoff and initializes the runtime before calling
`gopweb-hold`; resetting the process table afterward would erase the service.
The service brings up its driver and acquires a DHCP lease before listening.
Physical input and LAN acceptance remain open in
[HardwareSitting](../../../Hardware/HardwareSitting.md#works-81-owned-pool-diagnostic-candidate).

## Stage 4 -- the quantum: every core is at 10 ms

`emit-lapic-calibrate` (`codex/compiler/Emit/X86_64Boot.codex`) runs on the boot
processor before the start-up IPIs: it times 10 ms of HPET against its own LAPIC
timer (masked, one-shot, divide-by-16) and leaves the count in cell 36352, which
`emit-ap-timer-init` programs on every AP. With no HPET, a period outside the
specification, or a frozen counter it keeps `lapic-timer-count`, the old
1,000,000. `codex/test/smp-quantum` pins it: under codex-vm the count measures
62,585 (10.01 ms at the modelled 100 MHz bus).

**Core 0 is the PIT at count 11,932** (`pit-reload-count`, 10.0002 ms, 99.998 Hz).
Vector 48 jumps over the tick increment, so cell 28672 is core 0's clock alone.
The builtins `pit-input-hz` (1,193,182) and `pit-count` carry the exact rate to
Codex code, and the compiler's own tick waits go through `pit-ms-ticks`:
`with-timeout` and the watchdog windows (the pet window 1,098 ms, the progress
window 302,088,000 ms, the 20 and 5,500,000 ticks they were at count 65,536).
`codex/test/pit-rate` pins the count and the rounding (a day is 8,639,869 ticks,
where a rounded 100 Hz gives 8,640,000).

**codex-vm paces core 0 from the programmed reload**, in the main loop's tick
tests, the SMP halt path and the kick thread, so an unprogrammed PIT still ticks
at 54.9 ms. `smp-quantum`'s "the tick count is core 0's clock" reads 60 to 130 a
second, and a codex-vm on a fixed 55 ms fails it at 18. The kick thread runs once
per PIT period and teardown joins it before deleting the partition: without the
join the 10 ms kick crashed codex-vm at exit in 2 of 11 runs, with it 0 of 8, and
the 55 ms codex-vm 0 of 15 (2026-09-23, small samples).

The readers of the tick count, by census of `28672` and `get-ticks` over `codex`
and `apps`:

- waits, from the builtins: `GopXhci` (164 and 54 ms), `IdeaServer`'s
  `current-day`, `MicInput`'s settle (219 ms);
- raw tick counts with no time meaning: the displays (the Monitor's
  `dk-mon-tick-cell`, `IdtInspector`, `PerfMonitor`, `DiagnosticShell`),
  `LoadTest`'s elapsed ticks, and entropy (`IdentityManager`, `GopText`,
  `GopWizard`);
- tests whose guard deadlines are tick counts, ended early by a kill or an exit:
  `process-kill-test`, `starvation-prevent`, `supervisor-kill-restart`.

The scheduler's own counts stay in ticks, the same unit on every core: a slice is
3 ticks (30 ms) at normal priority, and `starve-threshold` is 100 ticks.

**What the bed cannot show:** codex-vm answers an AP's HPET read with 0, so only
the boot processor can time anything. No metal boot starts an AP: only codex-vm
writes the core count (cell 4088, from GPA 0xFF8), so `emit-smp-init` finds 0 on
hardware and the AP timer runs only under codex-vm's `-smp`.
10 ms is not an ambitious number and that is the point (Damian: "windows does 17
right, been that way since processors were like 60mhz").

## On one core the boot process is never preempted

Proc 0 has a zero slice and must yield for a child to run on the same core.
A normal child can be preempted only when the slice table is initialized.
Both `emit-process-setup` and `emit-runtime-init-fn` call
`emit-slice-table-init` before enabling interrupts. The shared table supplies
kernel/system/normal/background slices 0/6/3/1. `process-yield` reloads the
selected child's slice; zero disables timer preemption. The UEFI wrapper enters
`opening`, so its `runtime-init` call establishes this state independently of
ordinary CDX startup.

COMPILER-111's initialization repair is landed. The
[single-core evidence](DeskScheduler.md#single-core-preemption-evidence)
records the poisoned-table regression and zero-normal-slice control in native
and OVMF beds. The fixture is `codex/test/apps/runtime-init-slices.codex`.
This enables child preemption; it does not make proc 0 preemptible or remove
the need for cooperative yields in long desktop operations. Physical latency
and multicore fairness remain separate claims.

## What it must not break

- **The desk's capability row.** No `Network.*`, ever.
- **Proc 0 stays pinned to the BSP.** `smp-proc0-pinned` is the arm.
- **The desk never unwinds.** Section 1 of `works-desk-contract.md`.
- **The taskbar clock keeps advancing.**
