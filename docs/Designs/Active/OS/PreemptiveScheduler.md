# The Preemptive Scheduler

*Damian, 2026-08-28, on where a webserver belongs: "the desktop is merely the
admin app, the webserver is a system level concern right". Then: "yeah well we
definitely need preemptive". Then, on the rulings: the desk is privileged and
the quantum is 10 ms.*

Status: CORRECTED DESIGN. Opened by val 2026-08-28 out of WORKS-48.

## THIS DOCUMENT'S FIRST VERSION WAS WRONG, AND IT IS ON MAIN AT 20716 AND 20717

It said the tree has no scheduler, that "task context switching exists
nowhere", and that we would be "building one, not wiring one". **All of that is
false.** The kernel has preemptive SMP multiprocessing, it is callable from
Codex today, and it is tested. The error was mine and it was the same one four
times in one day: a search for `context-switch|task-switch|save-regs` found
nothing, and I published the absence rather than the search. The machinery is
there under other names -- `__idle_dispatch`, the proc table, the preempt scans
-- and it was two greps away.

Anyone who read the first version: discard its premise. The only finding that
survives is `lapic-timer-count`, at the foot of this file.

## What is actually there, measured 2026-08-28

**A preemptive SMP process kernel, emitted by the compiler's boot layer.**

- **Processes are real.** `proc-table-base` 20480, `proc-entry-size` 256; each
  entry carries state, a CR3, and a per-process heap base.
  `emit-build-process-page-tables` gives each process its own page tables.
- **Callable from Codex under `[Concurrent]`:** `process-spawn` (it takes a
  CLOSURE and answers a pid), `process-spawn-on-core`, `process-spawn-priority`,
  `process-spawn-with-heap`, `process-wait`, `process-yield`, `process-exit`,
  `process-exit-self`, `process-kill`, `process-status`, `process-count`,
  `process-get-pid`.
- **Per-process capability restriction already exists:** `process-restrict-cap`,
  `process-get-cap`, `process-set-scope`, `process-get-scope`,
  `process-set-network-scope`, `process-get-network-scope`. This is exactly the
  mechanism the webserver problem needs.
- **Kernel channels exist:** `chan-kern-create`, `-send`, `-recv`, `-send-block`,
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
collision guard is `cmp rsp, r10`, per context for the same reason. The first
version proposed per-task arenas as stage 1; they exist by construction, and
`emit-create-process` already writes a per-process heap base.

## So what is actually missing

**Nothing in `apps/`, `codex/os/` or `codex/foreword/` has ever called any of
it.** Measured: zero callers of `process-spawn`, `chan-kern-create` or
`process-restrict-cap` outside `codex/test`. The desk owns the CPU because
nobody ever spawned anything beside it, not because it cannot be done.

**And `codex/os/sched` is a second, unrelated model of the same idea** -- a
`Task` of `{id, name, priority, status}` with no body, a `try-dispatch` that
executes nothing, a `CoreHeap` that computes arena records nothing allocates
from, cited only by its own tests. It duplicates in records what the emitter
does in instructions. Whether it is deleted or kept as a bookkeeping view over
real processes is a decision for whoever next needs it, and nothing below waits
on it.

`DeskScheduler.md` (cooperative pane rates, PARKED) is unaffected and is not
superseded.

## Damian's two rulings, against what is actually there

**THE DESK IS PRIVILEGED -- already implemented.** Proc 0 is pinned to the boot
processor: `__idle_dispatch` starts each core's scan at its own id and wraps to
1, so an application processor never reaches slot 0, and both preempt scans skip
slot 0 outright when the claiming core is not the BSP. Pinned by
`codex/test/smp-proc0-pinned.codex`. If the desk is proc 0 it has the privilege
the ruling asks for. What is NOT there is a no-preempt window around a paint on
the BSP's own clock; whether that is needed is a measurement once the desk
shares a core, not an assumption.

**THE QUANTUM IS 10 ms, and this half of the original finding stands.**
`lapic-timer-count` is `1000000`, an arbitrary literal rather than a duration:
the period is `count * divisor / bus clock`, the divisor is 16, and the bus
clock is unknown at compile time -- on a 100 MHz bus that literal is 160 ms.
Boot must calibrate the LAPIC timer against the HPET, whose rate is already
known and used (`hpet-ticks-per-second`). That needs the current-count register
at 0x390, which is not declared, and it lands in
`codex/compiler/Emit/X86_64Boot.codex`, so it is SEED-AFFECTING and takes the
build token.

10 ms is not an ambitious number and that is the point (Damian: "windows does 17
right, been that way since processors were like 60mhz"). Windows has run a
~15.6 ms tick since the NT era; some older HALs used 10 ms. The risk is not the
number, it is that nothing has ever needed that timer to mean a time.

## What stage 1 found: THE FIRST PROCESS A PROGRAM SPAWNS IS PINNED TO THE BOOT PROCESSOR

Found 2026-09-02 (val) by being the first shipped caller, which is exactly
what this stage was for. The row is `compiler-backlog.md` COMPILER-49 and it
is seed-affecting; what follows is why this design's stage 1 could not go green
until it was fixed, which it was at main 21975.

**The mechanism, measured rather than read.** `process-spawn` takes the new
process's core affinity from cell 36224 `spawn-affinity-addr` and then resets
that cell to -1
(`codex/compiler/Emit/X86_64ProcessHelpers.codex:376-380`, the `aff0`..`aff4`
block; `process-spawn-priority` does the same at `:485-489`). The cell is
supposed to hold -1 at rest, meaning "any core": `X86_64Boot.codex:2647-2649`
stores -1 into it at boot, and the chapter's own prose at `:348` says "holds
-1 at rest, so an ordinary process-spawn pins to no core". **It reads 0.**
A guest that prints `atomic-load 36224` before spawning anything prints 0,
and 0 is the boot processor, not a wildcard.

So the first spawn of a program's life consumes a 0 and pins its child to the
BSP; the reset then leaves -1, and every later spawn is unpinned. A parent
that keeps running therefore starves its own first child on the core it is
itself holding.

**Every observation follows from that one fact.** With the parent spinning,
the first child sits in `proc-state-ready` (1) indefinitely while
`ap-dispatch-count-addr` stays 0. The moment the parent blocks in
`process-wait`, that same child runs to completion and goes to
`proc-state-free` (0) with the AP dispatch count STILL 0, which states the
pinning twice: it ran, and it ran on core 0. A second child spawned afterwards
is claimed by an application processor. Three children spawned in one run with
the parent spinning throughout give statuses `1 2 2`: the first never runs and
the other two do, whatever their bodies are.

**Why no test has ever seen it.** Every spawning test in `codex/test` waits on
its children almost immediately, and a `process-wait` frees the boot processor,
which is the one condition under which a BSP-pinned child runs. The defect is
invisible to a corpus that always waits and appears the moment a parent carries
on, which is what a service host does (L-CONSTRUCT).

**Stage 1's arm is green at main 22042**, under COMPILER-49's fix (main 21975,
red). It was never softened to pass, and that is the point of the entry: while
the defect stood, the arm printed the same line with the spawn present and the
spawn removed, which was the arm correctly reporting that its subject never
ran. Re-run on seed F134E3E7 at smp 4 it prints `an ap ran the service`, and
the sabotage the arm's own prose calls for -- remove the `gopweb-start` line,
keep the instrument -- prints `NO AP RAN THE SERVICE`. So the two colours now
differ, which is what makes the green mean something.

The bound was checked too, because a spin whose value nothing reads is a spin
the compiler may drop, and a wait that does not wait would make the arm flaky
rather than wrong: at 20 tries the sabotage takes 1.6s against 0.7s at one try,
so the wait scales with its budget. `codex/test/apps/gopweb-spawn.codex` and
`apps/works/GopWeb.codex` landed with it.

## The work, as it actually stands

### Stage 1 -- A shipped caller, not a test

Something outside `codex/test` spawns a process and survives a gate. The
smallest honest version is the webserver itself, so stages 1 and 2 may be one
CL.

Acceptance: a non-test chapter spawns, the child runs, the parent keeps running.
**Sabotage: remove the spawn and the arm must lose the child's effect** -- an arm
that passes without the spawn is measuring the parent.

### Stage 2 -- The webserver as a process

`web-serve-concurrent` runs under `process-spawn`, with network scope granted to
that process and to nothing else. The desk keeps
`[Device.Port, Gpu.Compute, Gpu.Memory, Identity, Audio]` and never gains
`Network.*` -- the whole point, and now free rather than a 42-signature
widening.

Acceptance: a browser on the host loads a page served by the guest while the
desk is driven by the mouse, and the desk's effect row is unchanged.

**THE SCOPE CELL HAS THREE WRITERS AND THE CHECK IS LIVE** (val, 2026-09-07,
at the source). The boot grant writes proc 0's cell from the opening's scoped
`Network.*` effect (`codex/compiler/Emit/X86_64Chapter.codex`,
`manifest-opening-net-scope` into `emit-set-boot-scope`); every spawn copies
the parent's cell into the child (`X86_64ProcessHelpers.codex`, the
`proc-net-scope-offset` load and store beside the capability-word copy); and
`process-set-network-scope`, gated on the caller's admin bit, whose only Codex
caller is `apply-load-decision`. The three `network-scope-*` tests never call
the setter: they declare `Network.Write "ok.host"` on `opening` and the boot
grant writes the cell. An empty cell is an UNSCOPED effect and is admitted by
the language's own meaning of one; "fails open" was the wrong reading.

**THE RUNTIME GAP IS THE CAPABILITY WORD, NOT THE SCOPE.** `process-spawn`
copies the parent's `proc-cap-offset` word into the child, and nothing carries
the closure's declared row to the kernel: the row `process-spawn` takes under
its `ForAllEff` is a type-level fact with no runtime twin. The desk's
`opening` declares no `Network`, so a child it spawns holds no Network bits,
whatever `gopweb-service`'s row says. The NE2000 path is `net-send-raw` and
`net-recv-raw`, both capability-checked, and refuses such a child; the e1000
path is raw MMIO under `Device.Mmio`, which the desk holds, and checks
nothing. The default bed is the NE2000.

Measured on seed E103FF07, two arms, one file apart: an `opening :
[Concurrent, Console]` spawns `\x -> poke-byte 28000 0 (if net-status < 0 then
1 else 2)` after storing 9, yields, and prints the byte. It prints `1`
(refused). The control, `opening : [Concurrent, Console, Network.Read]`,
prints `2`. 9 would have meant the child never ran. Re-run both before
believing any fix; the fix is arm one printing `2`.

So a production caller of the scope setter was not the unit: the desk cannot
call it without `Capability` on its row and the admin bit, and a scope on a
child with no Network bits scopes nothing. The question is which side of the
spawn holds the network.

**RULING (root, 2026-09-07, on Damian's do-not-wait): THE SPAWNER IS THE
HOLDER.** The kernel enforces parent-bounded capability and
`process-restrict-cap` runs in that direction; a spawn that granted the
closure's row would let a child gain what its parent lacks and is
seed-affecting; the desk's row gaining `Network.*` is forbidden below.

**The shape, landed in `apps/works/GopWeb.codex` as `gopweb-hold`:** proc 0
spawns the service, then restricts its OWN `cap-network-read` and
`cap-network-write`, then runs the desk. The desk stays proc 0 (a spawned
child gets `proc-spawn-heap-size`, 1 MiB, and is not the pinned boot
process). `desk-run`'s row is unchanged; the `opening` in
`apps/works/DeskVm.codex` carries `Concurrent, Capability, Network.Read,
Network.Write` because `gopweb-hold`'s row demands them, so dropping them is
a compile error rather than a silent loss of the bits.

**The order is a load-bearing invariant, not a convenience: spawn, then
restrict.** After the restrict, proc 0 cannot regain Network in this boot;
nothing grants a bit at runtime. A service started after the restrict
inherits the restricted word and is refused.

**The falsifier is `codex/test/apps/gopweb-hold`** (smp 4), three readings
from three processes: the service's capability word carries both Network
bits; the holder's own `net-status` is -1 after the restrict; a child
spawned after the restrict reads -1 too. Sabotage measured on seed
E103FF07: delete the two restricts and the second and third lines turn
False while the first stays True.

`codex/test/apps/gopweb-spawn` measures the dispatch and nothing about the
network; its prose that "the child has the network" is a claim its
instrument cannot see.

**The spawner's row is `[Concurrent]` and that half is DONE** (main 23076).
`process-spawn` is `ForAllEff 0 (FunTy (FunTy Integer (row-var 0) Integer)
(concrete-row "Concurrent") Integer)` in
`codex/compiler/Types/Builtins.codex`, so a child's effects do not reach the
spawner. `gopweb-start` had declared
`[Console, Concurrent, Network.Read, Network.Write]`, which would have handed
the desk `Network.*` through the effect row rather than through any code a
reader would look at.

**`codex/test/apps/gopweb-spawn`'s own row is the evidence.** It spawns a
service running under `Network.Read` and `Network.Write` and declares neither,
so the chapter compiling is the demonstration rather than the assertion; the
signature alone would only have been a declaration edited to agree with a
claim about it. A note at that line says the row must not be widened, because
adding `Network.*` back still compiles and deletes the evidence silently.

**THE ACCEPTANCE IS RED, and the two blockers are measured and registered**
(val, 2026-09-07, headless per root: host `Invoke-WebRequest` through
`-portfwd 9100:9100` while `-keys-file` drives the desk, DeskVm at
`BB98735D` on seed `E103FF07`). The desk paints and its clock runs with the
service spawned beside it at smp 1 and smp 4; the fetch never gets an answer.
Narrowed by five single-guest arms, each named in its register:

- NAT delivery is not the subject: proc 0 polling the NIC sees the host's
  SYNs (15 frames to port 9100), and `web-serve-concurrent` in proc 0
  answers the page at 200 in 0.2 s.
- FIXED at main 23163: every transport reserved a 32 MiB receive buffer
  and a spawn slot is 32 MiB, so the service died of `OUT OF MEMORY` at its
  first allocation in any child. The listen transport now takes
  `serve-recv-buf-cap` (64 KiB); `codex/test/apps/gopweb-child-heap` is the
  runner, and a child pinned to core 0 under a yielding parent serves a
  host `GET` at 200 in 0.2 s.
- `plugs-backlog.md` 2.45, THE BED'S GAP, **CLOSED (reek, 2026-09-07)**:
  codex-vm answered 0 to every device port read from an application
  processor, so a child on an AP received NO frames (0 against the boot
  processor's 15, same poll, same host). `handle_io` now takes its VP as a
  parameter instead of writing VP 0's registers by constant, and serialises
  on a lock, so an AP gets the same device models the boot processor gets;
  `codex/test/smp-ap-port-io` grades it and goes red on the pre-fix binary.
  **The pin is lifted (val, 2026-09-08):** `gopweb-start` is `process-spawn`
  again and the service lands wherever the scheduler puts it. The desk still
  yields once per `desk-loop` iteration and `web-mux-loop` on every empty
  poll, which is what lets a child that lands on the boot processor run at
  all. Two things the pin had hidden. `web-mux-loop` returns after fifty
  million consecutive empty polls, which an application processor reaches in
  about five seconds, so `gopweb-service` re-enters `web-serve-concurrent`
  with the heap restored between rounds and lives until it is killed;
  `codex/test/apps/gopweb-spawn` at smp 4 is the arm, and it read False
  before the re-entry. And `plugs-backlog.md` 2.46: an application processor
  polling the NE2000 takes codex-vm's I/O lock so often that the boot
  processor's disk load crawls behind it (the frame is black at 25 s and
  painted at 60 s), the bed's cost and not the design's.

**THE ACCEPTANCE IS GREEN ON THE ACCOMMODATION** (val, 2026-09-07, DeskVm
at `9B007324` on seed `076181B2`, headless, `-keys-file` driving the desk,
`-portfwd 9100:9100`, screenshot at 30 s): a host `GET /` answers 200 in
0.2 s, `/api/health` and a second `GET` answer, and the frame at 30 s shows
the desk painted with its clock running at 13,805 iterations a second
against 14,764 without the service, so the yield costs the desk about six
percent. Damian at a browser is confirmation, not the gate (L-HUMAN).

**AND GREEN WITH THE PIN LIFTED** (val, 2026-09-08, DeskVm at `87B724B7` on
seed `076181B2`, smp 4, the same headless recipe): the service on an
application processor answers `GET /` at 200 in 0.2 s and again at 47 s,
and the 60 s frame shows the desk painted with its clock running at 15,550
iterations a second, above the 13,805 measured with the service sharing
core 0.

**The metal entry is wired (main 23205):** `GopBoot.codex` calls
`gopweb-hold` before its flow, the way `DeskVm.codex` does, with the
`boot-flow` row unchanged. Bed-proven on seed `076181B2`: the boot image
built to scratch and booted under OVMF to its first screen. On metal the service holds the network and serves nothing until
Track B binds the Intel NIC. THIS STAGE HAS NO OPEN ITEM ON THE BED. Beside
it: codex-vm exited with a host heap-corruption code (0xC0000374) after the
smp 4 desk arm's screenshot, once, unreproduced.

### Stage 3 -- The pane as an admin console

DONE (val, 2026-09-08, val 23390). WORKS-48's pane is `desk-focus-web` in
`GopDesk.codex`, a window over the block `gopweb-hold` allocates and shares
with the service (`GopWeb.codex`, "The Block the Desk and the Service
Share"): the service pumps its own mux loop, reads a command cell each round
(stop drops frames, start resumes) and writes every request its route
answers into a sixteen-slot ring; the pane shows Stop, Start, the state and
the ring, repainting when the count or the paused flag moves.

**Control rides the shared block, not `chan-kern-*`.** The desk's row
carries no `Concurrent`, so a send would have widened `desk-loop`'s row
through every step, and an integer channel cannot carry a log line. The
block sits in the flat address space both processes share, which the stage
2 probes already relied on (a child reporting through cells the parent
prints).

**Acceptance, bed-proven headless** (DeskVm `24257374` on seed `EEFABF6C`,
`-portfwd 9100:9100`, a `-mouse-file` through the start menu): the pane
lists `GET / 200 OK` and `GET /nothing 404 Not Found` as the host made them,
a click on Stop turns the state to Stopped, and the next host GET times out.
Two facts the run fixed. The desk has launched no app from a keystroke since
2026-08-26, so a bed recipe opens a pane by mouse (the Settings row at
(140,694), then the Web Server row at the same point once the group is
open), and the stage 2 acceptance's `-keys-file` was inert. And
`/api/health` is answered by the web stack's `web-standard` before the route
sees it, so it is served and not logged (WORKS-48's residue). The desk idles
at 11,795 iterations a second with the pane focused.

### Stage 4 -- The quantum, if it is still wanted

The LAPIC calibration above. Seed-affecting, takes the token, and independent of
everything else: nothing in stages 1 to 3 needs the quantum to be 10 ms rather
than whatever it is now.

## What it must not break

- **The desk's capability row.** No `Network.*`, ever.
- **Proc 0 stays pinned to the BSP.** `smp-proc0-pinned` is the arm.
- **The desk never unwinds.** Section 1 of `works-desk-contract.md`.
- **The taskbar clock keeps advancing.** 4 s and 18 s frozen before the step
  conversion, 0 after.
