# The Preemptive Scheduler

*Damian, 2026-08-28, on where a webserver belongs: "the desktop is merely the
admin app, the webserver is a system level concern right". Then: "yeah well we
definitely need preemptive". Then, on the rulings: the desk is privileged and
the quantum is 10 ms.*

Stages 1, 2 and 3 are done. Stage 4, the quantum, is open and is the only work
this page still asks for.

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

**The heap needs no work either.** The bump frontier is register `r10`
(`__heap-save` is `mov rd, r10`, `__heap-restore` is `mov r10, rd`), so it is
part of the CPU context and every process already has its own. The stack
collision guard is `cmp rsp, r10`, per context for the same reason, and
`emit-create-process` writes a per-process heap base.

## Open: `codex/os/sched` is a second, unrelated model of the same idea

A `Task` of `{id, name, priority, status}` with no body, a `try-dispatch` that
executes nothing, a `CoreHeap` that computes arena records nothing allocates
from, cited only by its own tests. It duplicates in records what the emitter
does in instructions. Whether it is deleted or kept as a bookkeeping view over
real processes is a decision for whoever next needs it, and nothing here waits
on it. `DeskScheduler.md` (cooperative pane rates, PARKED) is a different
subject and is not superseded by this page.

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
gets `proc-spawn-heap-size`, 1 MiB. `apps/works/DeskVm.codex`'s `opening`
carries `Concurrent, Capability, Network.Read, Network.Write` because
`gopweb-hold`'s row demands them, so dropping them is a compile error rather
than a silent loss of the bits.

**The order is a load-bearing invariant: spawn, then restrict.** After the
restrict, proc 0 cannot regain Network in this boot, and a service started after
the restrict inherits the restricted word and is refused.

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
yields once per `desk-loop` iteration and `web-mux-loop` on every empty poll,
which is what lets a child on the boot processor run at all. `web-mux-loop`
returns after fifty million consecutive empty polls, about five seconds on an
application processor, so `gopweb-service` re-enters `web-serve-concurrent` with
the heap restored between rounds and lives until it is killed.

**Acceptance, green** (val, 2026-09-08, smp 4, headless with `-portfwd
9100:9100`): a host `GET /` answers 200 in 0.2 s and again at 47 s, and the 60 s
frame shows the desk painted with its clock at 15,550 iterations a second. With
the service sharing core 0 the desk ran at 13,805 against 14,764 with no service
at all, so the yield costs it about six percent. Damian at a browser is
confirmation, not the gate (L-HUMAN).

**The admin pane is `desk-focus-web` in `GopDesk.codex`,** a window over the
block `gopweb-hold` allocates and shares with the service: the service pumps its
own mux loop, reads a command cell each round (stop drops frames, start resumes)
and writes every request its route answers into a sixteen-slot ring; the pane
shows Stop, Start, the state and the ring. **Control rides the shared block, not
`chan-kern-*`,** because the desk's row carries no `Concurrent` and a send would
widen `desk-loop`'s row through every step, and an integer channel cannot carry
a log line.

**Two facts a later reader needs.** The desk has launched no app from a keystroke
since 2026-08-26, so a bed recipe opens a pane by mouse. And `/api/health` is
answered by the web stack's `web-standard` before the route sees it, so it is
served and never logged (WORKS-48's residue).

**On metal** the service holds the network and serves nothing until Track B binds
the Intel NIC. `GopBoot.codex` calls `gopweb-hold` before its flow, the way
`DeskVm.codex` does, with the `boot-flow` row unchanged.

## Stage 4 -- the quantum, and it is the only open stage

`lapic-timer-count` is `1000000`, an arbitrary literal rather than a duration:
the period is `count * divisor / bus clock`, the divisor is 16, and the bus
clock is unknown at compile time, so on a 100 MHz bus that literal is 160 ms.
Boot must calibrate the LAPIC timer against the HPET, whose rate is already
known and used (`hpet-ticks-per-second`). That needs the current-count register
at 0x390, which is not declared, and it lands in
`codex/compiler/Emit/X86_64Boot.codex`, so it is SEED-AFFECTING and takes the
build token.

10 ms is not an ambitious number and that is the point (Damian: "windows does 17
right, been that way since processors were like 60mhz"). The risk is not the
number, it is that nothing has ever needed that timer to mean a time. Nothing in
stages 1 to 3 depends on it.

## What it must not break

- **The desk's capability row.** No `Network.*`, ever.
- **Proc 0 stays pinned to the BSP.** `smp-proc0-pinned` is the arm.
- **The desk never unwinds.** Section 1 of `works-desk-contract.md`.
- **The taskbar clock keeps advancing.**
