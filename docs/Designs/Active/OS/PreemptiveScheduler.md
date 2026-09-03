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

### Stage 3 -- The pane as an admin console

WORKS-48's pane talks to the service over `chan-kern-*`: start, stop, and the
request log. `LockFreeChannel` in `codex/os/sched` is not that channel; the
kernel's is.

Acceptance: the log in the pane matches the requests the host actually made.

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
