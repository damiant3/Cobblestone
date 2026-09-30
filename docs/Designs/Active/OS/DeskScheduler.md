# The Desk Scheduler

Status: Stage 1 input collection and stage 2 bounded software rendering,
presentation and pane policy are implemented and verified in both beds.
Stage 2 code is on main at **CL 32370** (fester CL 32361).
The cited gate passed 72 compile and 72 runtime subjects with no failures.
Physical acceptance remains unrun; the queued desk sequence is in
`docs/Hardware/HardwareSitting.md`, "Queued desk acceptance".
Panes declare a rate or a budget and independently choose skip or run late on
a miss. Cooperative checks report overruns; enforced caps require bounded
work units or preemption.


## Recommendation

Build a desktop scheduling policy with protected input service, bounded
rendering work and fair progress for background work. Keep display-state
ownership explicit. Establish the boot and timer behavior in both VM beds
before moving rendering into preemptible workers.

Borrow Apple's work classification and bounded interactive preference,
NT's prompt treatment of newly runnable interactive work, and Linux's
fairness between user activities. Port none of the complete schedulers.
Cobblestone's present bottlenecks include work performed inside one process
and the mouse collection path; changing the process selection algorithm
alone cannot remove those delays.

User-visible success means predictable pointer and keyboard response during
expensive work, timely completed frames, and continuing progress for
background tasks. A higher idle loop count is insufficient evidence.

## What the comparison establishes

### Apple: classify user work, then protect short interactive bursts

Apple's QoS API distinguishes user-interactive, user-initiated, utility and
background work. QoS influences CPU scheduling, I/O and timer behavior, and
supported operation dependencies can promote lower-QoS work needed by a
higher-QoS operation. That is a useful declaration model for a desktop whose
rendering, file loading and indexing serve different user expectations.
[Apple QoS guide](https://developer.apple.com/library/archive/documentation/Performance/Conceptual/EnergyGuide-iOS/PrioritizeWorkWithQoS.html).

The current published XNU Clutch design schedules at QoS-bucket, thread-group
and thread levels. Bucket deadlines, bounded early-service windows and
starvation-avoidance windows balance interactive bursts against lower-priority
progress. Group accounting prevents a workload from purchasing extra service
merely by creating threads. Edge adds placement across heterogeneous clusters.
Those mechanisms are design references, not guarantees for arbitrary desktop
callbacks or constants to copy onto the ASUS.
[Apple XNU Clutch/Edge design](https://github.com/apple-oss-distributions/xnu/blob/main/doc/scheduler/sched_clutch_edge.md).

Cocoa run-loop modes also select which event sources and timers receive
service during activities such as dragging. A timer outside the current mode
waits. The corresponding Cobblestone idea is a declared gesture policy,
rather than a clock-specific conditional scattered through the desk loop.
[Apple run-loop modes](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/Multithreading/RunLoopManagement/RunLoopManagement.html).

Mach's time-constraint interface separately names period, computation and
constraint. That separation explains why a request for a few milliseconds
alone is incomplete: the scheduler also needs a replenishment interval and
a completion expectation. Historical Mach documentation is an interface
reference, not proof of modern hardware timing.
[Mach scheduling interface](https://developer.apple.com/library/archive/documentation/Darwin/Conceptual/KernelProgramming/scheduler/scheduler.html).

**Local conclusion:** distinguish a pointer update from a complete 3D frame.
Both are visible work; only the pointer update belongs in the shortest
nonblocking service class. A continuously animating pane must not inherit
unlimited interactive privilege.

### Windows NT: preempt by urgency, preserve progress elsewhere

Windows selects runnable threads by priority and rotates equal-priority
threads. A newly ready higher-priority thread can displace lower-priority
execution. Dynamic boosts help window-input and I/O-completion work and
decay afterward; the base priority does not become a permanent entitlement
to dominate the CPU.
[Scheduling priorities](https://learn.microsoft.com/en-us/windows/win32/procthread/scheduling-priorities),
[priority boosts](https://learn.microsoft.com/en-us/windows/win32/procthread/priority-boosts).

MMCSS provides preferential CPU access for declared time-sensitive multimedia
work while retaining resources for lower-priority applications. Multimedia
service is a controlled class, rather than a reason to mark every visible
operation real-time.
[Microsoft MMCSS](https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service).

Driver execution can defeat thread policy. Microsoft requires DPC work to
be brief because ordinary threads cannot run on the same processor during
a DPC. The transferable rule is to bound nonpreemptible work throughout the
input and device path, not merely to raise the UI thread's priority.
[Microsoft DPC guidance](https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/guidelines-for-writing-dpc-routines).

**Local conclusion:** input becoming available needs an effective route back
to the desk. An unconditional yield before input collection, followed by a
long worker slice, can undermine a well-designed pane policy. A blanket
highest-priority desktop process can instead starve the workers and services
the desktop needs. Dependency handling and service limits matter together.

### Linux: fairness, grouping and latency are separate concerns

CFS arrived in Linux 2.6.23, replacing the previous scheduler's interactivity
machinery. Weighted virtual runtime approximates fair CPU sharing. Fair
long-term shares do not by themselves specify an input-to-display latency.
[Linux CFS design](https://docs.kernel.org/scheduler/sched-design-CFS.html).

Per-session CPU accounting (Linux autogroup) prevents a parallel workload's
process count from multiplying its CPU entitlement.
[Linux scheduling manual](https://man7.org/linux/man-pages/man7/sched.7.html).

BFS provides a desktop-latency-oriented alternative. Kolivas's description
favors a global view of runnable work and distinguishes desktop latency
goals from specialist real-time workloads.
[BFS author's explanation](https://ck-hack.blogspot.com/2010/10/bfs-in-real-time.html?m=0).

Newer Linux fair scheduling uses EEVDF concepts: eligibility limits service
to tasks owed CPU, then virtual deadlines influence which eligible task runs.
A virtual deadline in fair scheduling is not a hard wall-clock completion
guarantee.
[Linux EEVDF documentation](https://docs.kernel.org/scheduler/sched-eevdf.html).

Linux SCHED_DEADLINE instead combines runtime, deadline and period with
budget enforcement and admission control. Deadline guarantees depend on
appropriate execution-time assumptions and the admitted workload. General
desktop panes with unknown rendering costs cannot acquire those guarantees
by declaring a deadline.
[Linux deadline scheduling](https://docs.kernel.org/scheduler/sched-deadline.html).

**Local conclusion:** account by user activity when parallel workers arrive.
Opening helper workers must not multiply an application's CPU entitlement.
Start with a small policy appropriate to the current process table; do not
import a scalable runqueue architecture before measurements justify the cost.

## Cobblestone source audit, 2026-09-29

| Observation | Evidence | Consequence |
|---|---|---|
| Input dispatch, desk work and the focused pane share one synchronous path. The desk collects input before yielding and skips that yield when the queue is nonempty. | `apps/works/GopDesk.codex`, `desk-loop` | Collection checkpoints protect reports during software rendering; rate limiting alone cannot interrupt other pane calls. |
| Desktop software rendering returns after at most eight engine quanta; presentation returns after one bounded copy. The completed-frame HUD advances only after publication finishes. | `apps/works/GopScene.codex`, `gsc-step`, `gsc-render-units`; `codex/foreword/engine/SceneWork.codex` | The desktop dispatches between groups. Host-GPU calls and opaque pane callbacks remain cooperative. |
| Mouse report consumption marks the endpoint idle; the desktop collector rearms the endpoint before returning. Legacy `mouse-pump-one` callers retain their phased behavior. | `apps/works/GopUsbMouse.codex`, `mouse-consume`, `mouse-pump-one`; `apps/works/GopInput.codex`, `di-mouse-one` | The collection proof must detect suppressed rearming through missing expected input, independently of collector-call counts. |
| Keyboard collection advances the arm/check/drain state machine through bounded rounds. The scene uses the desk's delivered scancode when a collector exists. | `apps/works/GopInput.codex`, `di-kbd-rounds`; `apps/works/GopScene.codex`, `gsc-step`, `gsc-take` | Pane-local draining remains only for callers without the desktop collector. Physical input acceptance remains unrun. |
| The PIT reload is 11932 at input rate 1193182 Hz; normal/system slices are 3/6 ticks. Kernel-priority slice is zero. | `codex/compiler/Emit/X86_64Boot.codex:828`, `:853`, `:3005` | About 10 ms is the timer period, not every process's execution slice. Normal/system slices are about 30/60 ms if uninterrupted. |
| Timer preemption bypasses a zero remaining slice. Proc 0 starts with zeroed scheduler fields and is pinned to the BSP. | `X86_64Boot.codex:1699`, `emit-common-interrupt-handler`; `:3005`, `emit-process-setup` | The desktop is not an ordinary freely preemptible application. Do not infer bounded desk latency from the existence of preemption elsewhere. |
| Yield/idle selection and timer-expiry selection differ. Yield scans for the next eligible ready slot; timer expiry scans priorities and has a periodic starvation fallback. | `X86_64ProcessHelpers.codex:50`, `:139`; `X86_64Boot.codex:1755` onward | Equal-priority fairness, wake latency and starvation limits require direct tests. The fixed scan order is a risk to investigate, not a newly measured failure. |
| AP startup reads the boot core-count cell before attempting startup. | `X86_64Boot.codex:2773`, `emit-smp-init` | The design must work on one core. Existing documentation says metal does not populate that cell; hardware SMP remains unproven here. |

`PreemptiveScheduler.md`, "On one core the boot process is never preempted",
also records a consequential disagreement: codex-vm resumed a yielding parent
against a spinning child, but the OVMF Dev Console path required the child
to yield. Resolve that disagreement
before treating a worker-process render path as portable across the beds.

The current `ds` layout and typed pane lifetime rules belong to
`apps/works/works-desk-contract.md`. A suspended render cannot retain
temporary pointers above a heap mark that the desktop later restores.
Completion from a closed or replaced pane must not publish into recycled
memory. Workers therefore require owned arenas, explicit result lifetime
and stale-completion rejection; adding process-spawn alone is unsafe.

Worker admission also needs an explicit memory calculation. The default spawn
heap is 1 MiB and a spawn slot occupies a fixed 32 MiB region, including its
stack (`X86_64Boot.codex:682` onward). The desk contract's "A pane that runs
as its own process" records refusal of an oversized request. Size the heap
for scratch, scene state and owned surfaces, check the returned pid, and
handle refusal without losing input service. Borrowed desk surfaces still
require a lifetime protocol; borrowing does not solve ownership.

## Proposed desktop contract

Retain the approved rate-or-budget and skip-or-late pane interface. Normalize
the declarations into explicit internal scheduling facts:

- release source: event, period or available background opportunity;
- urgency: input/interaction, visible work, user-requested long work, background;
- budget amount and replenishment interval, with CPU time versus elapsed
  service time named explicitly;
- deadline or maximum acceptable age;
- miss behavior and whether intermediate results are replaceable;
- the largest non-yielding work unit and persistent-state ownership.

A rate declaration is not an execution-cost bound. A budget declaration
needs a replenishment interval. An elapsed-time check after a callback
measures an overrun but cannot enforce the budget during that callback.
A first cooperative implementation must report budget overruns honestly;
hard caps require preemption or a proven bound on every work unit.

For a clock, run-late means display the current time once after a delay,
not replay a queue of obsolete repaint requests. For animation, discard
superseded presentation work and retain the newest coherent result.
Simulation state, file writes, keyboard events and button transitions
must not inherit animation's discard policy.

A gesture policy can defer cosmetic repaints during dragging without
suspending device collection, key/button handling, essential timers or the
delivery of a completed operation. Temporary interaction preference needs
an expiry and a background progress rule.

## Architecture options and recommended order

| Approach | Benefit | Limitation | Disposition |
|---|---|---|---|
| Due-time checks around existing whole-pane steps | Small change; removes duplicate RTC gates and unnecessary paints | A started frame or long callback still blocks input | Useful policy foundation, not completion of the responsiveness work |
| Resumable bounded work inside the desk | Preserves one display owner and can work on one core | Every long renderer, blit and handler needs safe yield boundaries and persistent state | Preferred first enforceable prototype |
| Render workers with completed-frame publication | Separates heavy computation from input service | Requires proven wake/preemption behavior, arenas, cancellation and presentation ownership | Next step when those contracts are demonstrated |
| Replace the entire process scheduler first | Can address fairness and wake behavior globally | High compiler/runtime risk; cannot alone split a synchronous UI callback or fix mouse rearming | Avoid as the initial implementation |

First, establish input collection and timing evidence. Inspect endpoint
rearming and bounded draining. Coalesce absolute pointer positions, or sum
relative deltas between button transitions; retaining only the latest raw
relative report loses motion. Preserve key/button order and expose queue
overflow instead of silently treating missing input as success.

Then separate event handling, state advance, rendering and presentation.
Pump input between bounded units. Render into owned offscreen storage and
publish completed work through the desk; a worker must not race the cursor
or window chrome while writing the live framebuffer. Bound the final copy
too: chunking triangle work alone leaves a full-frame blit in the input path.
A partial copy can tear, so presentation coherence requires its own test.

Finally add urgency and fair progress using a bounded burst allowance,
per-activity accounting and measured elapsed service. Consider worker
processes only after single-core yield and timer-preemption tests agree
between codex-vm and OVMF. Tune quantum or add deadline wakeups only when
traces identify process scheduling as the remaining bottleneck.

No process-per-pane requirement is proposed. Fixed small tables and a bounded
worker pool fit the current kernel better than independent threads and timers
for every clock or status pane. Measure before replacing the scan with a heap
or tree. Audio deadlines and filesystem durability remain separate service
contracts; neither follows from the focused window's repaint policy.

Keep schedule decisions pure and testable. Codex's effect rows and
punctual/allocation bounds can help constrain small service helpers, but
those declarations do not turn device waits or GPU execution into proven
wall-clock bounds.

## Verification and the missing GUIOS hardware baseline

Steps 1 to 3 below run in the beds today and do not wait for a sitting or
reliable GUIOS boot on Damian's machine. Pure policy checks, single-core
codex-vm/OVMF checks and integrated desk traces can proceed independently.
Only step 4, physical acceptance, depends on reliable GUIOS boot on the ASUS.

The fleet must deliver a known image, deterministic route to the desktop,
working physical keyboard/mouse and recorded artifact identity before a
subjective scheduler comparison is meaningful. Fold the scheduler experiment
into the coordinated sitting; do not use repeated manual tests as the
inner development loop.

Build the evidence in this order:

1. Pure policy tests: due times, wrap handling, missing clock, budget refill,
   missed periods, skip/late coalescing, burst expiry and background progress.
2. Single-core CPU tests: a non-yielding child, a yielding child, equal-priority
   competitors, mixed priorities and a newly runnable input-service task.
   Compare codex-vm with OVMF; retain disagreement as failure.
3. Integrated desk traces under software 3D, host-rendered 3D, large editor
   operations and background service load. Mouse-script the actual pane open
   and confirm pane identity before taking readings.
4. Physical GUIOS acceptance with the same artifact and cases, including
   sustained drag, button press/release during a long frame, typing and
   background completion.

Use fixed-capacity trace storage with no per-event heap allocation. Record
device completion observed, input queued, input serviced, cursor update,
render start/end, present submission and missed deadlines. Preserve event
identity across stages; do not subtract unrelated host and guest clocks.
Track tail latency, longest input-service gap, input loss, frame age and
background progress alongside throughput. Loop iterations/second is only
supporting evidence.

A timestamp after a framebuffer write is not input-to-photon latency.
Physical display/camera evidence is needed for that final claim; VM
screenshots and render-completion timestamps establish different boundaries.
No fixed latency promise is selected before the baseline exists.

Sabotage the actual protection: allow a long unyielding render unit, suppress
a required rearm, discard a button edge, disable background replenishment,
or publish a stale completion. The corresponding instrument must fail.
Test that a slow pane can miss its own frame without breaking input.

## Stage 1: input collection

Deliver one allocation-free input collector with an ordered fixed-capacity
queue, integrated at boundaries between bounded desk/render work units.
The integration must exercise a long render split into bounded units; a
collector tested only against an idle desktop is insufficient. The minimum
render checkpoints needed for that trace belong to stage 1. General pane
rate/budget policy and render workers remain later units.

The collector must:

- consume completed reports and rearm the mouse endpoint before the next
  bounded work unit, rather than leaving rearming for another full frame;
- sum relative deltas between button transitions, flushing pending motion
  before an intervening key or button event; never coalesce across an edge;
- preserve key and button order with monotonically assigned collection
  sequence numbers; order means collection order, not unknowable physical
  ordering between separate devices;
- advance keyboard collection through its required phases before removing
  the existing pane-local drain;
- report overflow through a sticky flag and a cumulative loss counter.
  A full queue refuses a new non-coalescible event without overwriting older
  entries. Any loss invalidates a lossless-input acceptance claim.

Acceptance runs in the beds. Fix an input-collection-gap limit in the run
configuration before the positive and sabotage arms. The render must last
longer than that limit overall, with bounded units shorter than the limit.
Inject a known sequence of motion, press/release and keyboard events during
the render. Record the longest collector-pass gap, the longest interval
without collecting expected input, rearm events, delivered sequence and
overflow counters. Include an outstanding input interval at capture end,
so a collector that stops receiving reports cannot pass with an empty sample.

The positive arm must stay within the declared input-collection-gap limit,
preserve the expected motion and key/button sequence, and report no overflow.
The matching control suppresses rearming after a consumed report; expected
input then fails collection or sequence acceptance. Merely counting collector
calls is not a valid control: calls can continue without receiving reports.
Additional controls discard a button edge or overfill the event queue; the
sequence check or overflow check must fail. A saturated queue must report
loss rather than silently manufacture a passing trace.

Report the trace, configured limit, artifact identity and both arm outcomes.
The limit is a bed acceptance parameter, not a physical input-to-photon claim.
Stage 1 does not wait for physical acceptance step 4.

### Stage 1 implementation and proof procedure

`apps/works/GopInput.codex` collects into `GopInputQueue.codex`. The desk
allocates the queue below its base heap mark and keeps the pointer in `ds`
cell 276. A pass visits the mouse endpoints and advances each keyboard
through eight pump rounds. Each transfer poll consumes at most four xHCI
event records and retains other endpoints' completion latches. Successful
mouse consumption rearms the endpoint in the same pass. Transfer errors
increment the loss and transfer-error counters and retain the completion
code. They report `INPUT TRANSFER ERROR` with that code; only queue
exhaustion sets the overflow flag and reports `INPUT QUEUE OVERFLOW`.
Both notice causes have separate notification flags.

The queue stores collection order, summed adjacent relative motion, and the
position at each event. A key or button edge ends a motion group. A click
forwarded by menu dismissal retains its position and precedes later queued
input. The desk delivers one queued event per iteration and consumes pending events before
starting another 3D frame. Software colour/depth clearing and presentation
checkpoint every 16 rows. Each triangle's raster loop checkpoints at
16-scanline boundaries; triangle traversal also polls every eight triangles,
including clipped and culled triangles. These are cooperative checkpoints,
not a general wall-clock bound for arbitrary scenes. The renderer
callback is removed before the desk restores the frame heap mark.
A pending chrome repaint does not discard a delivered key or click. Input
also preserves the repaint request until an empty chrome step answers
`desk-wnd-ev-stay`. The desk takes its frame heap mark before calling
`desk-input-step`, which owns collection, yield selection, delivery and
loss notification.

These checkpoints collect reports; they do not re-enter pane event handlers.
Stage 1 bounds the measured collection gaps, not pane dispatch latency or
input-to-display latency. Whole host-GPU submissions and other panes' long
operations still need the later bounded-work policy. Process scheduling,
pane rate/budget declarations and render workers remain separate units.

Run the positive arm and all controls serially from the repository root:

```powershell
pwsh -NoProfile -File apps/works/desk-input-proof.ps1 -Bed both -Mode all -Kernel seed/Codex.cdx
```

The runner snapshots the compiler and complete source unit before compiling
the arms. Each arm uses the same CDX in codex-vm and OVMF. A fresh output
directory holds compile logs, full traces and `results.json` with compiler,
source, CDX and boot-image hashes. `-Bed codex-vm` or `-Bed ovmf` selects one
bed; `-Mode positive`, `rearm`, `edge` or `overflow` selects one arm.
OVMF requires QEMU and its edk2 firmware under `D:\Program Files\qemu`.
The helper uses workspace-derived local ports and cleans its own QEMU process.

`DeskInputProof.codex` fixes a 1024x768 target and a 100 ms collection-gap
limit before either arm. F1 establishes the guest-clock input anchor. The OVMF driver waits for
the guest's anchor acknowledgement before injecting the sequence. Read-only
monitor queries then confirm collected pointer state before each next action
and key release before further motion. Missing acknowledgements fail the
positive arm; sabotage arms continue the stimulus after bounded waits.
The diagnostic prints its queue layout before the anchor. The OVMF case is
an acknowledgement-paced ordered stimulus, not an unsynchronized burst
across devices; monitor polling can affect timing. Codex-vm
uses the supplied key/mouse timeline. The timing claim starts at the received
anchor and makes no claim about host-to-guest delivery of F1. All trace
timestamps use the guest HPET, whose rate is printed in the trace.

The positive grade requires a frame longer than the limit with input
collected during that frame, bounded collection gaps, the motion and
position at every key/button edge, monotonic collection sequence spans,
zero loss and zero retained frame allocation. Missing expected input keeps
an outstanding interval open through capture end. The controls suppress
rearming, discard button edges and overfill the actual queue. A control
passes only when the grader sees the intended refusal: `accepted=NO` alone
is insufficient evidence of overflow detection.

Trace record kinds are 1 queued, 2 dequeued, 3 rearmed, 4 input anchor,
5 frame start, 6 scene ready, 7 clear complete, 8 render complete and
9 presentation copy complete. Queue sequence spans remain in the queue
records; trace timestamps are collection/dequeue boundaries, not photons.
The focused `desk-input-queue`, `desk-input-poll` and `desk-input-delivery` tests cover ring wrap,
coalescing boundaries, overflow refusal, bounded event-ring draining,
cross-endpoint latches, forwarded-click ordering and zero hot-path allocation through the existing
cite gate. Queue wrap coverage advances the head before a full-capacity
fill and checks motion coalescing with a nonzero head. `desk-input-path`
executes the desk's shared collect-and-deliver step and the real `gsc-step`:
input injected by the scene builder must be collected during rendering,
and calling the target callback after the step must no longer collect.
Transfer-error fixtures exercise both device paths and the distinct notice.
`desk-input-loop` runs the enclosing desktop loop in a child process and
requires collection and delivery before terminating that child.
Calibration uses bundled source copies. Removing either queue index modulo
must fail the wrap assertion. Removing `gsc-step`'s hook or unhook must fail
the corresponding `desk-input-path` assertion. Omitting the production
loop's `desk-input-step` call must fail `desk-input-loop`; that control keeps
a yield so the parent can observe missing input without testing starvation.
The timed bed proof remains an explicit diagnostic invocation.

Measured 2026-09-29 with compiler SHA-256
`B3256BF8B4CC8327EAFB42E53997C3A5FD0385BBD59E257013663E3618072E82`:
the positive arm and the rearm, edge and overflow controls passed their
required grades in both beds. Both positive arms reported zero queue/trace
loss and zero retained frame bytes.

| Bed | Longest collector gap, us | Longest expected-stream interval, us | Longest frame, us |
|---|---:|---:|---:|
| codex-vm | 92062 | 31343 | 182803 |
| QEMU/OVMF | 46623 | 32958 | 2139523 |

The limit was 100000 us. Values are guest-clock measurements converted to
integer microseconds, rounded down; they are not hardware latency promises.
The positive CDX SHA-256 was
`AEAC0FC02D125752A8BEC43C17E4B354B341E9D6C8E657FDAE6AEE67E4EEAD01`;
the OVMF boot-image SHA-256 was
`F6897D09E3C16BEB92C4528A20A8B9E9902339052EF84CCA42631E2949655311`.
The codex-vm executable SHA-256 was
`70C36D351799B3D97C68310D0593951270DDF6763EB3B298D39FB897FF210141`.
Conversion arithmetic was verified with `codex_run`, status `ok`, library
`none`, fact
`ed240144a60aa220baf55840396771ab729e3b3700465b309204d6c9889a349a`.

## Stage 2: bounded resumable work

The first unit establishes stable scene metadata and a fixed-size
presentation-copy primitive. `gsc-place` updates the pane-owned target and
viewport records in place; neither record may be allocated above a transient
desk frame mark. Buffers and metadata retain the pane's existing lifetime.
Placement preserves the existing callback. `gsc-step` saves and restores
that callback rather than allocating a replacement function value inside
the transient frame.

`gsc-blit-unit` copies at most 1024 pixels, including partial scanlines,
pumps the target's input hook before nonempty work, and returns the next
pixel offset. The completed offset equals width times height. Nonpositive
dimensions, pitches below width and offsets outside zero through completion
return -1. The caller owns adequately sized live buffers, uses representable
address arithmetic, and
keeps source pixels, geometry and destination unchanged across a copy;
window movement or frame replacement requires cancelling that continuation.
The offset is the only continuation state. The service callback and its
captures must remain live for each call, perform bounded nonallocating work,
and neither mutate copy inputs nor re-enter pane dispatch. A frame-local
callback must be removed before a heap restore and reinstalled when needed.
A step allocates nothing and total copy work remains linear in pixels.

The compatibility `gsc-blit-serviced` drains those units synchronously.
The focused desktop uses the phased presentation described below.
The renderer and pane-policy contracts below govern the remaining phases.
The copy primitive alone makes no dispatch-latency or atomic-presentation claim.

Acceptance must prove the copy bound, input pumping, correct padded pitches,
and survival of metadata across a heap restore followed by overwrite.
Each protection needs a failing bundled-source control in codex-vm and
OVMF. Restored offsets must continue the same copy without per-step heap
growth. The pixel oracle includes untouched destination padding.

From the repository root:

```powershell
pwsh -NoProfile -File apps/works/desk-present-proof.ps1 -Bed both -Mode all -Kernel seed/Codex.cdx
```

The fixture is
`codex/test/apps/scene-present-unit.codex`. The runner snapshots compiler
and bundled source, uses each CDX in both beds, and writes trace paths and
source/CDX/image hashes to `results.json` in a fresh output directory.
Positive output must match the full expected verdict. Each control must
report its specific failed field, not merely `accepted=NO`.

`scene-place` installs a callback returning 73 before placement and requires
the same result afterward. `desk-input-path` requires that result after
the real scene step and after frame restore plus overwrite. Replacing the
callback with a stable no-op therefore fails independently of allocation.

The controls widen the unit, copy beyond a bounded returned offset, remove
or delay input service, make each metadata record transient independently,
and substitute each pitch independently. A sentinel immediately past each
returned prefix detects contiguous over-copy even when progress is reported
as bounded. The final oracle checks every destination pixel and padding.

Measured 2026-09-30: the positive presentation arm and all named controls
passed in codex-vm and QEMU/OVMF. The positive arm reported every field
`yes`, including exact pixels, bounded progress and zero allocation.
Compiler SHA-256:
`B3256BF8B4CC8327EAFB42E53997C3A5FD0385BBD59E257013663E3618072E82`.
Positive CDX SHA-256:
`1A19F3CAE4D87386A382BEDEAC34C4F3871E231FCE1C8D4BAEF245350AFE0606`.
OVMF image SHA-256:
`4E3F3CE621873A3209784308D70DF639BAD15270394E914139B98763F1F1B8F6`.
Evidence: `build-output/desk-stage2/unit1-land-acceptance/results.json`.
The cited gate passed all 66 compile and runtime subjects. The subject lists
were counted with `codex_run`, status `ok`, fact
`b236fcccd00b0573c1a00039a9de13628ed5ede79cd455f51f3621e49badac7c`.
The timed input positive, rearm, edge and overflow grades also passed in
both beds, using the acknowledgement-paced OVMF driver. Evidence lives in
`build-output/desk-stage2/unit1-input-ack-{positive,rearm,edge,overflow}/results.json`.
The unit bounds the copy to 4096 bytes. The 1537 by 37 fixture contains
56869 pixels and takes 56 units. Arithmetic was verified by `codex_run`,
status `ok`, library `none`, fact
`2eb162cd106ace1f0571866df969487c285954789464aac55853a25d470b6f7d`.

### Stage 2 unit 2: phased software presentation

The unit-2 measurements below describe the presentation-only baseline at
main CL 32141. Current allocation and timing results are under "Stage 2
completion evidence" below.

The focused software scene step separates rendering from presentation.
Rendering completes into the pane-owned work surface, then swaps that color
surface into the completed front frame before publishing an offset of zero.
A subsequent step copies one `gsc-blit-unit` and returns
to the desktop loop. Input dispatch therefore occurs between copy units.
The source frame remains unchanged until the last unit completes or the
continuation is invalidated. The completed-frame counter and HUD advance
only at completion, not once per unit.

`ScenePane` carries an offset, destination pitch and completed-source flag.
The offset is -1 when no copy is pending. Geometry or framebuffer changes
invalidate the source flag and cancel the offset before metadata changes.
Unchanged placement preserves progress. Rendering-option changes invalidate
the source; hide, restore and chrome repaint cancel pending publication.
The continuation lives only in its owning pane record, so closing that pane
leaves no asynchronous completion to publish into recycled memory.

Each step restores the prior callback before the desktop reclaims its frame.
No scene graph or frame-local closure is retained for presentation. The new
state adds 24 bytes per scene record on x86-64, with no additional surfaces.
Opening allocations rise from 512216 to 512240 bytes at
320x200 and from 768216 to 768240 bytes at 320x300. Arithmetic was verified
with `codex_run`, status `ok`, library `none`, fact
`146c5b7318f1a34b52a999476d6025160f7b62e76088ecca2afc7e16733e58c6`;
`scene-open-cost` confirmed those predictions. Copy work remains linear
in pixels, with constant continuation storage. Compiler heap/time behavior
is unchanged.

`scene-present-step` exercises real scene steps and the desktop's
collect-and-deliver function between units. The fixture checks an untouched
destination before publication, a bounded prefix per step, a single source
frame across copies, completion accounting, frame restore plus overwrite,
geometry/pitch invalidation, hide, rendering options and cached redraw.
The same fixture calls the production chrome path for repaint, minimize
and close, and changes the framebuffer base independently of geometry.
`desk-scene-present-loop` runs the actual desk loop in a child process under
codex-vm, observes a partial copy, injects input and requires that input to
cancel publication before the next copy unit. The existing pane-lifetime
tests `desk-hole`, `desk-reuse` and `desk-root-guard` remain the allocator
close/reclaim coverage; the step fixture does not simulate
reopening recycled pane storage.
The explicit control suite is:

```powershell
pwsh -NoProfile -File apps/works/desk-present-proof.ps1 -Unit step -Bed both -Mode all -Kernel seed/Codex.cdx
```

Controls drain the whole frame, rebuild a pending frame, omit geometry,
pitch or framebuffer invalidation, and omit cancellation for hide, each
rendering option, cached redraw or each chrome event. Every control must
change its designated verdict field;
the ordinary cite gate grades the changed desktop callers as well.

Desktop-owned software rendering follows the bounded contract below.
The host-GPU path and cached compatibility redraw remain synchronous.
Redraw cancels pending work and shows only a completed cache for an owned
software pane; an empty cache waits for scheduled rendering. Unowned legacy
callers retain the synchronous fresh-frame adapter. A live framebuffer copy
can still tear. Physical acceptance remains open; these work bounds do not
establish a general desk latency or atomic display bound.

Measured 2026-09-30: the optimized step positive and every named control
passed in codex-vm and QEMU/OVMF. The primitive suite also passed in both
beds. The cited gate passed 68 compile and 68 runtime subjects, including
the real desk-loop fixture. Count verification used `codex_run`, status
`ok`, fact
`b22d46df02aa1acf3342836bbd44ff689767a6038fbf23ef62d4b3d4ba8b455f`.
Evidence roots are `build-output/desk-stage2/unit2-opt-step`,
`unit2-opt-primitive` and `unit2-opt-validation.stdout`.
The compiler and VM hashes are the unit 1 hashes above. Positive step CDX:
`507529C181E5C98ABC68BD95FCB6BA8CC307A3E5AD252DEEC49D62638F4ABA15`.
OVMF image:
`0FAC51936F8D4FB3F8780C7D6859663F1149D6ED97F40E838E93DDAA75C52505`.
After incoming tracker formatting, the whole desk-loop proof CDX remained
`253132472396663015E9C2246E4091D36674F19D46A550D22AD72000D3043810`.

The scene-step cost probe is
`apps/works/proofs/desk-scene-step-cost.codex`. A before/after pair uses the
same bundled dependencies and compiler, with only `GopScene` replaced by
main revision `@=32007` for the before arm. Each run warms one completed
frame, clears the destination and times the next 16 completed frames at
640x480 with a fixed camera. Incomplete work refuses. Final checks require
nonblank source pixels, a matching source fingerprint across arms, and exact
source/destination equality above the label band. Those checks occur outside
timing and do not certify every intermediate displayed frame.

Measured 2026-09-30 under codex-vm, three pairs with alternating order:
mean elapsed time was 76358 us per frame before and 76187 us after, rounded
down. Per-run means ranged 74034-77877 us before and 75097-76755 us after.
The ranges overlap; these samples do not establish a speedup. Copy-only
steps do not read the RTC. Both arms retained zero frame bytes after the
caller's heap restores and passed the final visible-pixel comparison.
The test measures aggregate scene-step time, including HUD work. It excludes
the enclosing desk loop, individual input latency, peak heap and physical
display timing. The host was shared.

Evidence: `build-output/desk-stage2/unit2-opt-cost-results.json`, including
source/CDX hashes. Timing arithmetic was verified with `codex_run`, status
`ok`, library `none`, fact
`cbd5b2f5fb4bee21ad90898da8fc778fffb9271fff155de98d857c8f7279d1c0`.

## Bounded renderer implementation contract

The software job borrows a pane-owned scene for its lifetime. Geometry,
materials, textures and lights remain stable until completion or cancellation.
The pane updates animation poses before beginning a job. Job storage and
surfaces are allocated below the desktop frame mark; steps copy numeric
results into that storage and retain no transient matrix, vertex or light
records. Every step permits restoring and overwriting the transient heap.
The caller restores its frame mark after each desktop group.

The initial job admits at most 128 nodes and 16 lights, and shininess at most
128. Parent indices must be -1 or refer to an earlier node. Unsupported
inputs refuse explicitly. Node transforms and bounds are cached; one bounds
unit visits at most 128 vertices. Triangle preparation clips one triangle
against the near plane. Fan preparation handles one triangle. Clear and
raster units visit at most 256 pixels or bounding-box positions, respectively.
Culled nodes and triangles still return to the caller. Shadow rendering uses
the same bounded phases and owned surfaces. A desktop call groups at most
eight engine units, then returns for input dispatch. Input collection belongs
to the desktop between groups; owned rendering does not install another
collector inside those units. Presentation retains its collection hook.
These are work bounds, not a hardware-independent elapsed-time guarantee.

The completed frame remains available in a separate color surface during
the next render. Cancellation on geometry, rendering options, hide or close
prevents publication from an obsolete job. The synchronous renderer remains
the pixel oracle. Proof must cover exact color/depth parity, clipping, all
shading modes, textures, shadows, interrupted heap reclamation and explicit
refusal. Desktop proof must demonstrate dispatch between render units and
continued background progress. Physical acceptance remains a separate gate.

Move, resize, framebuffer or pitch replacement cancels rendering and
publication and invalidates the cached frame. Hide, close, chrome repaint,
cached redraw and pane reentry cancel both kinds of progress. An unchanged
placement preserves progress. Rendering-option changes invalidate the cache.
The software desktop redraw uses only a completed cache; an empty cache waits
for scheduled rendering. The host-GPU path remains synchronous.

The renderer fixture is `codex/test/apps/scene-render-work.codex`; the owned
desktop-step fixture is `scene-bounded-step.codex` in the same directory.
`apps/works/desk-present-proof.ps1 -Unit render -Bed both -Mode all` grades
raster-bound, vertex-lifetime and shadow-pass controls against `done`,
`color` and `color`, respectively. `-Unit bounded` grades render-cancel,
front-publish and render-budget against `cancel`, `front` and `budget`.
The render-quanta control enlarges the group and must fail `quanta`.
The runner requires the designated false verdict and a complete output shape.
All named positives and controls passed in both beds on the submitted code.

`codex/test/apps/desk-scene-render-loop.codex` separately invokes the actual
desktop loop in a child process in both beds. The observing process sees
partial clearing, injects a queued key and requires render cancellation before
any frame completes. This proves dispatch and cooperative observer progress;
it deliberately does not complete a frame. Pair this fixture with the renderer
pixel-parity suite. The three runner units above do not replace this production
loop check. Run `desk-present-proof.ps1 -Unit loop -Bed both -Mode all` for
the production-loop positive and dispatch-bypass control. Both use explicit
runtime initialization and mask the PIC interrupts after killing the child,
before returning to Option A's single-halt epilog. The generic return hazard
is recorded in the plugs backlog, 2.108. This is cooperative background
progress, not acceptance of preemption against a non-yielding child.

## Pane policy implementation contract

`GopPanePolicy` owns a fixed table with 15 records of 64 bytes and a 64-byte
header. The table lives below the base heap mark, referenced by desk cell
296. A declaration names event-only, rate or elapsed-time-budget service,
the period, the budget when applicable, and independently skip or run late.
Invalid declarations and exhausted registration capacity increment an explicit
refusal counter. The desktop also displays a policy-refusal notice.

Periods use low-counter modular subtraction and must be shorter than half
the counter range. Rate service starts immediately, then admits a new job
at the next period. An active run-late job continues. A skip declaration
cancels an active job at a missed period. Budget service replenishes at the
first visit after the period expires; elapsed callback time is charged after
return. A bounded render unit can cross the budget edge once. The overrun
counter records calls ending beyond the allowance. Opaque callbacks remain
cooperative and cannot acquire an enforced elapsed-time cap.

The initial desktop declarations give 3D View and Aquarium an 8 ms allowance
per 16 ms period with run-late behavior. Periodic polling panes use a 50 ms
period; static panes are event-only. Only the focused pane is selected by the
current desktop. Keyboard, pointer, click, active chrome gestures and pending
chrome repaint bypass cosmetic admission. A budget-blocked scene processes
input without advancing rendering. Background processes receive a yield when
the input queue is empty and at least once per 32 queued-event passes.

The taskbar RTC poll has a separate 100 ms service interval. An unavailable
HPET rate sets a fault counter, permits pane work without a timing guarantee,
and polls the RTC once per 64 desk passes. `pane-policy.codex` grades budget,
rate, skip, counter wrap, burst fairness and capacity. The proof runner's
`-Unit policy` arms disable budget enforcement, skip or burst yielding and
require `budget False`, `skip False` or `fairness False`, respectively.

## Cost and arithmetic evidence

### Stage 2 completion evidence, 2026-09-30

Main CL 32370 contains the complete code change. The final grouped-work
runner records are `build-output/desk-stage2/unit3-batched-{render,bounded,
loop,step,unit}/results.json` in the fester workspace. Policy records are
`unit3b-policy/results.json`; its final bundled source remained byte-identical,
SHA-256 `823091F8627AA62E8F39B28C32B737BB91B665D7780A0899966FB68BE0BEDB8E`.
The cited gate record is `unit3-batched-validation.stdout`, exit 0: 72 compile
and 72 runtime passes, zero failures. These are focused checks, not the full
release battery. Every published code file was read back from main and matched
its proven SHA-256 value.

The reusable `apps/works/proofs/desk-scene-step-cost.codex` now compares the
current synchronous renderer with the grouped renderer, both using bounded
presentation and outer input collection. The camera is fixed, devices are
disabled, one completed frame warms the path, and the next 16 completed frames
are timed. Three pairs alternate ordering. Final source fingerprints and
visible presentation match, with zero retained transient bytes in both arms.

| Sample statistic | Synchronous render | Grouped render |
|---|---:|---:|
| Mean time per completed frame, us | 106709 | 144794 |
| Largest sampled scene call, us | 110511 | 1449 |
| Largest sampled transient allocation before step restore, bytes | 1983448 | 5168 |

The sampled frame-time increase is 35.69 percent. Shorter calls trade against
that throughput cost. Times use the guest HPET rate and integer microseconds,
rounded down; the shared-host samples are not a physical latency guarantee.
The probe excludes persistent initialization, the enclosing desk loop and
pane-policy delays. Its allocation sample includes the whole group and outer
collection, but is not a general allocator high-water instrument. Evidence:
`unit3-cost-results.json` and `unit3-cost-batched.stdout`.

The initial ungrouped version had a substantial frame-time regression.
The current group amortizes dispatch overhead and removes duplicate collection
inside owned rendering. The current desktop call visits at most 2048 raster
positions across eight engine quanta, or a bounded mixture of geometry and
raster phases. A new pane adds 104 bytes of metadata relative to the preceding
presentation-only version. The policy allocation advances the allocator by
1088 bytes, including alignment allowance.

Each owned software pane uses two color surfaces and one depth surface,
plus fixed shadow surfaces, scene geometry and continuation records. The hole
reservation is 12 bytes per capacity pixel plus a fixed 2 MiB allowance.
Both 3D View and Aquarium fit that reservation at both tested geometries in
`scene-open-cost`. Total work remains linear in geometry and raster attempts,
with the admitted light and shininess limits bounding per-attempt work.
Persistent state is reused across frames; compiler implementation and seed
are unchanged.

The MCP arithmetic attempt returned `Transport closed` and supplied no fact
hash. The same input-derived integer calculations and selected-test count were
compiled and run with the depot seed as `unit3-cost-arithmetic.codex`; output
is `unit3-cost-arithmetic.out`, CDX SHA-256
`6BED3FD06853696ECB3D7744E44673CBE8BC35219EB33C0E2A3708D90F1FEF64`.

The complete GopBoot source bundle remained identical after the final merge,
SHA-256 `A3C2697A53D5EA11D2218D41CC98B157AA940574D75028D41FEAD0BFE66A7537`.
The compiled desk component is
`D:/Projects/Cobblestone-fester/build-output/desk-stage2/desk-stage2-boot.efi`,
SHA-256 `A0849D5EF3E03198249450677265B1FAEE9247F42538443BF4E448CCDD7AE068`.
This is a component for the coordinated sitting, not a rehearsed composed
image or evidence of physical acceptance.

The first implementation supports at most **15 simultaneously registered
panes**, matching `dk-wr-max` in `apps/works/GopDesk.codex:1722`, and
**256 queued input events across the whole desk**, not per pane. Pane type
IDs do not consume slots unless registered. The taskbar clock and fixed desk
services are outside the pane registry. A scheduler registration beyond the
pane limit must refuse explicitly, without silently leaving a pane unserviced.
These are implementation limits; increasing either limit requires a cost and
acceptance review.

Reserve at most 64 bytes of scheduler state per pane and 64 bytes per queued
event, including per-slot headers and padding. A registered pane's admission
lookup scans at most 15 pane records. Cold registration additionally prunes
15 policy slots against the window registry with a bounded nested scan;
that path is O(panes squared), rather than the O(panes) hot lookup.
Dequeue at most
32 events per bounded service pass. Queue append/removal and adjacent-motion
coalescing are O(1); no growing accumulator or per-event allocation is allowed.
Use a separate fixed trace ring of 4096 records, at most 32 bytes each.
Trace overflow is explicit and fails a trace-based acceptance run. Ring
indices, sequence counters, gap maxima and overflow metadata use a fixed
block of at most 256 bytes outside those records.

Record storage is bounded by 960 bytes for pane state, 16384 bytes for input
events and 131072 bytes for tracing: 148416 bytes before the bounded metadata
block. Existing device rings, framebuffers and continuation storage are
separate costs and must be reported rather than charged to that record total.
The total state is O(panes + queued events + trace capacity), with all three
populations explicitly bounded. Rendering continuation storage must outlive the
desk's transient frame mark without retaining every prior frame. A pair of
1600 by 900, 32-bit pixel buffers occupies 11,520,000 bytes, about 10.99 MiB,
before depth buffers or scene state; allocating such a pair per open pane
multiplies that cost. Prefer bounded reusable surfaces where ownership permits.

Arithmetic was computed from source constants and stated example dimensions
with `codex_run`, status `ok`, library `none`:
`11932 * 1000000 / 1193182 = 10000` microseconds, rounded down;
three and six ticks round down to 30000 and 60000 microseconds.
Those are source-derived nominal intervals, not measured dispatch latencies.
The illustrative 60 Hz frame interval rounds down to 16666 microseconds and
a 125 Hz mouse report interval is 8000 microseconds; neither rate is asserted
for Damian's actual display or device.

Fact: `36a4ce7c22b16567298e3bc381e51a66fdbcff3cac556dd1ff743ce0f6096625`.
The computed framebuffer size is `2 * 1600 * 900 * 4` bytes.

Capacity arithmetic: `15 * 64 = 960`, `256 * 64 = 16384`,
`4096 * 32 = 131072`, sum `148416` bytes. Verified with `codex_run`,
status `ok`, library `none`, fact
`9dcdc18c18edf6ebdd0a462becbc69e4b39dcdf15644aef5da3cd1667dde3fcd`.

Stage 1 reserves 147712 bytes for the queue, trace and metadata. The existing
`alloc-zeroed` allocator advances by another 64 bytes for alignment, giving
147776 bytes reserved once per desktop. No pane-policy table is allocated
by stage 1. The diagnostic's separate presentation buffer is test storage;
the desktop reuses its existing scene buffers. Queue append, adjacent-motion
coalescing and removal are O(1). Collection has fixed endpoint, phase and
event-drain bounds. Rasterization retains its pixel/triangle complexity with
bounded service work per row/triangle group. Compiler heap/time behavior is unchanged.
Transfer-error fields fit the existing metadata block. Recording an error
allocates nothing; formatting its first desktop notice occurs under the
desk frame mark and is reclaimed with that iteration.

A 640x480 fixed-camera fixture measured 16 frames after one warm-up frame
on 2026-09-30, with the collector enabled in both arms. The control uses the
former 16-row presentation checkpoints; the candidate uses 1024-pixel
units. The target and presentation surface were cleared after warm-up.
Both arms produced the same nonblank pixel hash and retained zero frame
bytes. Mean elapsed time was 69031 us for the control and 74932 us for the
candidate, rounded down, on the shared codex-vm host. More frequent input
service adds measured elapsed work; the fixed storage bound is not a zero
time-cost claim. Placement now updates pane-owned records without allocation;
the allocation probe excludes its own formatted output.

The timed fixture exercises rendering and presentation directly; placement
and the enclosing scene step are outside that timing measurement.

Timing conversion verified with `codex_run`, status `ok`, library `none`,
fact `c564c8da20facb7fafefd73609c17171b10198e89e34822b6e7d95e893bb5dbd`.

Storage arithmetic verified by `codex_run`, status `ok`, library `none`:
fact `06b8a56ae7bdb9b576a17187c4a851aafbae751d47b514568215ff871bfd60d9`
for the block and keyboard phase bound; fact
`8596264eada6aeb7eb9c29177355870822017f411f8b26c5d5f706d3334517d2`
for the allocator's additional alignment allowance.
