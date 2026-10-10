# The Desk Scheduler

Status: Buffered desktop composition and scene-progress corrections are on
main at **CL 32619** (fester CL 32615, with scene-owner eviction from 32449).
Focused desk, policy, scene, fish and raster suites pass. Production-loop
positive and dispatch-bypass controls pass in codex-vm and OVMF.
Damian accepted GUI usability and repaint behavior on 2026-10-01 with image
`90B8CEFA`, which uses seed `AF9057E8`, including timer selection from main CL 33111 and
runtime-init slice initialization from main CL 33130.
Native and USB-only OVMF rehearsal evidence is in
`D:/Projects/sitting-images/desk-scheduler-33130/rehearsal`.
The image carries the standard test identity. Native captures show completed
Aquarium and 3D frames. OVMF shows Clock progress, Aquarium shadows and
minimize/restore, and continuing 3D frames. Frame captures do not certify
input latency or flash-free scanout.
Damian's report closes WORKS-78: the image boots, GUIOS works much better,
screens no longer flash, apps work as expected, and the whole-screen repaint
problem is gone. The verbatim report and image hash are in
`docs/Hardware/HardwareSitting.md`. Web pane port 9100 was not reported and
is unrunnable on metal until DHCP and a displayed leased IP (WORKS-81).
The report gives no measured latency/frame-rate bound
and does not enumerate every gesture in the retained acceptance sequence.
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
| Keyboard collection advances the arm/check/drain state machine through bounded rounds. The scene uses the desk's delivered scancode when a collector exists. | `apps/works/GopInput.codex`, `di-kbd-rounds`; `apps/works/GopScene.codex`, `gsc-step`, `gsc-take` | Pane-local draining remains only for callers without the desktop collector. Detailed physical input cases remain unreported. |
| The PIT reload is 11932 at input rate 1193182 Hz; normal/system slices are 3/6 ticks. Kernel-priority slice is zero. | `codex/compiler/Emit/X86_64Boot.codex:828`, `:853`, `:3005` | About 10 ms is the timer period, not every process's execution slice. Normal/system slices are about 30/60 ms if uninterrupted. |
| Timer preemption bypasses a zero remaining slice. Proc 0 starts with zeroed scheduler fields and is pinned to the BSP. | `X86_64Boot.codex:1699`, `emit-common-interrupt-handler`; `:3005`, `emit-process-setup` | The desktop is not an ordinary freely preemptible application. Do not infer bounded desk latency from the existence of preemption elsewhere. |
| Yield advances from the current slot. Timer expiry preserves priority preference and rotates equal-priority ties from the current slot; periodic relief rotates from the last relieved slot. | `X86_64ProcessHelpers.codex`, `emit-process-yield-helper`; `X86_64Boot.codex`, `emit-common-interrupt-handler` | The direct selection tests below grade sampled progress, loop-work balance, wake delay and low-priority service in both single-core beds. No multicore fairness or physical latency bound follows. |
| AP startup reads the boot core-count cell before attempting startup. | `X86_64Boot.codex:2773`, `emit-smp-init` | The design must work on one core. Existing documentation says metal does not populate that cell; hardware SMP remains unproven here. |

Ordinary startup and `runtime-init` share slice-table initialization:
kernel/system/normal/background receive 0/6/3/1. The controlled comparison
below grades restoration from poisoned memory and timer return in both
single-core beds. Render workers still require ownership, lifecycle and
physical-acceptance proofs.

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
can still tear. GUI/repaint acceptance is recorded above; these work bounds do not
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

The cooperative software job borrows its caller-owned scene for its lifetime. Geometry,
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
eight engine units. The scheduled desktop batches at most 32 such steps,
checking input and an elapsed 2 ms service target between steps. A pending
input event or a handled key ends the batch. Each step restores its transient
heap. Callers without a scheduling table retain single-step service.
Owned rendering does not install another collector inside the engine units.
Presentation retains its collection hook.
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
an active render, injects a queued key and requires render cancellation before
any frame completes. This proves dispatch and cooperative observer progress;
it deliberately does not complete a frame. Pair this fixture with the renderer
pixel-parity suite. The three runner units above do not replace this production
loop check. Run `desk-present-proof.ps1 -Unit loop -Bed both -Mode all` for
the production-loop positive and dispatch-bypass control. Both use explicit
runtime initialization and mask the PIC interrupts after killing the child,
before returning to the PE wrapper's interrupt-disabled halt loop
(`build/cdx-to-pe.ps1`). The fixture proves cooperative background progress,
not acceptance of preemption against a non-yielding child.

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

The desktop declarations give 3D View and Aquarium a 16 ms release period
with run-late behavior: an unfinished frame continues through bounded service
batches. Periodic polling panes use a 50 ms
period; static panes are event-only. Only the focused pane is selected by the
current desktop. Keyboard, pointer, click, active chrome gestures and pending
chrome repaint bypass cosmetic admission. A budget-blocked scene processes
input without advancing rendering. Empty-queue passes amortize process yields
over 2 ms rather than yielding after every tiny work unit. A queued input
event is delivered before a timed yield; the burst limit still forces a yield
after 32 passes. A handled scene key ends its batch and yields after handling.

The desktop composes into a retained surface. `GopPresent` publishes changed
pixels between handlers, targeting approximately 60 Hz with a working HPET.
No scan occurs without a pending request; polling panes can request a scan
whose comparison finds no changed pixels. A cached
published surface avoids framebuffer reads. A scene rectangle undergoing a
phased copy is excluded until completion, including during clock and pointer
updates. Shadow/GPU option changes roll a partial copy back to the published
cache before cancelling the scene job. This prevents intermediate clears and partial scene copies from
reaching the display; CPU publication is not a synchronized scanout swap.

The taskbar RTC poll has a separate 100 ms service interval. An unavailable
HPET rate sets a fault counter, permits pane work without a timing guarantee,
and polls the RTC once per 64 desk passes. `pane-policy.codex` grades budget,
rate, skip, counter wrap, burst fairness and capacity. The proof runner's
`-Unit policy` arms disable budget enforcement, skip or burst yielding and
require `budget False`, `skip False` or `fairness False`, respectively.

## Render-worker lifetime prerequisite

`apps/works/proofs/desk-worker-lifetime.codex` is a single-core handoff
prototype. The prototype grades mailbox guards and result ownership with
real spawned processes. The production desktop uses the worker contract below.
No renderer, pane registry or window-close path calls the prototype.

The desk owns the mailbox, destination surface and worker closure below its
transient frame mark. A worker owns the result buffer in its process region
and retains the region during its bounded acknowledgement wait. After filling
the buffer, the worker publishes its result pointer and request token, then marks the result
ready and yields until acknowledgement or fuel expiry. Successful setup
requires the worker to remain alive before publication; the single-core desk
does not yield between that check and acknowledgement. The desk checks
ready, unconsumed, live and matching-token state before copying any result bytes. A successful
copy consumes the completion once. A rejected completion changes neither
the destination nor the publication count. Only after accepting or rejecting
the completion does the desk acknowledge the worker and wait for exit.

The retained mailbox models one in-flight job and one pane incarnation.
Close is modeled by clearing the live flag; reopen by changing the token.
The mailbox outlives both events. A reused PID is not an incarnation token:
process slots and their allocation addresses are reused after exit. A
published result must therefore reside in desk-owned storage before the
worker region is released. Keeping the worker pointer is insufficient.

The positive fixture restores the parent's heap mark and overwrites the
same temporary allocation after the worker reports ready. Every result
pixel must remain intact. After copying and acknowledging, the fixture
spawns another child, requires the same PID and allocation address, and
poisons that address. The published desk copy must still match all 16
pixels. Separate cases attempt publication after changing the modeled
incarnation token or clearing the live flag; both require no publication
and an unchanged sentinel surface. Replaying an accepted completion must
leave the count at one.

Run the explicit diagnostic from the repository root:

```powershell
pwsh -NoProfile -File apps/works/proofs/desk-worker-lifetime.ps1 -Bed both -Mode all -Kernel seed/Codex.cdx
```

The runner uses fresh output directories and snapshots the source closure,
compiler and runner. Each arm compiles once and runs the same CDX in native
codex-vm and QEMU/OVMF, serially with one core. Native abnormal exit, missing
completion, any unexpected graded trace or an ineffective control fails the run.
All arms require `setup=True`, including confirmed slot/address reuse in
the ownership case. A refusal without completed setup is not a passing
control. `results.json` records the compiler, source, CDX and image hashes.

| Mode | Removed protection | Required false field |
|---|---|---|
| `transient` | Worker writes into the parent's reclaimed temporary allocation | `transient`, `owned-copy` |
| `generation` | Accept a completion with the wrong incarnation token | `stale` |
| `closed` | Accept a completion when the pane is not live | `closed` |
| `borrowed` | Retain the worker pointer instead of making the desk copy | `owned-copy` |
| `duplicate` | Accept a completion already consumed | `duplicate` |

Other fields must match the arm's exact expected trace. Measured 2026-10-01:
the positive and every designated control passed in both beds. Evidence is under
`D:/Projects/Cobblestone-fester/build-output/desk-worker-20261001/matrix/`.
The measured compiler is
`D2C01E16DD7E342F08DE462D5B7A842A4F5EF802CA8D5F9F791A42ED3D6456EA`.

The prototype uses a fixed 128-byte mailbox and 64-byte destination per
case, plus bounded closure/result bookkeeping and a 64-byte worker result.
Copy and pixel comparison visit 16 cells. Polling has fuel bounds of 10000
parent yields and 1000000 worker yields per wait; host deadlines bound a
stalled guest. No loop allocates a collection. Full-frame copying remains
linear in pixels and requires the existing bounded-presentation units.
Compiler and production desktop heap/time behavior are unchanged.

The prototype does not free/recycle a real pane, race multicore publication,
or establish an input-latency bound. Physical display acceptance remains separate.

## Production render worker

`GopDesk` starts one persistent `GopRenderWorker` below the base heap mark.
Desk cell 308 points to its 256-byte `GopRenderMailbox`. The closure and
controller survive closing, replacing and reopening 3D View or Aquarium.
The worker constructs a fixed scene from numeric request fields in its own
process heap. It never borrows a pane record, scene or parent scratch pointer.
The cooperative `GopScene` path remains available to callers without a worker.

Pane incarnations and requests share a monotonically increasing token counter.
Exhaustion at 2147483647 refuses instead of wrapping. Submission invalidates
the previous request before changing fields and commits the new token last.
The worker validates owner and token around the snapshot and between engine
units. It publishes reply fields before marking the result ready, then keeps
the result until consumption or cancellation. It restores the job arena before
acknowledging cancellation. Close and replacement invalidate the controller
before pane reclamation; they need not wait because the worker has no pointer
into the reclaimed pane. Hide cancels the request without changing ownership.

Every parent copy unit checks worker liveness, owner, matching request/reply
and unconsumed ready state. Each unit copies at most 1024 pixels. Completion
requires the full requested size and revalidates the result before swapping
the parent-owned front/back color surfaces. Duplicate completions cannot
publish again. Presentation keeps the existing bounded framebuffer-copy path.
An unavailable worker, exhausted token counter or oversized image produces
a visible refusal. There is no automatic restart using an old PID.

This integration uses the BSP. Startup pins a free process slot before the
custom-heap spawn publishes READY, under the runtime's single spawning writer
contract. A different returned slot is stopped gracefully and retried, with
at most 16 attempts. Explicit cleanup invalidates the controller, requests
natural exit and waits. Kernel kill/wait is not a process-slot reclamation
protocol. The two-core affinity diagnostic tests reuse of an AP-affine slot;
it does not certify arbitrary concurrent process creation or SMP rendering.

The worker yields after at most 32 engine units. The parent yields when waiting
for a result unless input is already queued. The pane-policy allowance is
propagated before service; denial pauses the worker before starting or between
units. A denied pane still handles input. The production declaration remains
16 ms rate service with run-late behavior. Budget accounting charges elapsed
parent callback time, including yields within that callback; it does not
separately meter worker CPU consumed outside the callback. No physical input
latency or strict worker CPU-budget bound is claimed.

Heap/time verdict: one 30 MiB worker heap plus the kernel's 1 MiB stack fits
the 32 MiB process slot with FX storage. Admission requires positive dimensions
no larger than 8192 and `width * height * 8 <= 30 MiB - 4 MiB`. The reserve
covers fixed scene geometry, shadow storage and temporary render records.
The worker checks the post-construction frontier and measures its peak;
per-unit scratch and the whole per-frame scene are reclaimed. Parent surfaces
retain `12 * width * height` bytes plus pane metadata; two new pane fields add
16 bytes. Rendering cost follows the existing bounded engine; the additional
parent copy is linear in pixels, allocation-free and bounded per unit.

Run the integrated diagnostics from the repository root:

```powershell
pwsh -NoProfile -File apps/works/proofs/desk-render-worker.ps1 -Unit render -Bed both -Mode all -Kernel seed/Codex.cdx
pwsh -NoProfile -File apps/works/proofs/desk-render-worker.ps1 -Unit lifecycle -Bed both -Mode all -Kernel seed/Codex.cdx
pwsh -NoProfile -File apps/works/desk-present-proof.ps1 -Unit worker-loop -Bed both -Mode all -Kernel seed/Codex.cdx
```

The default OVMF commands exercise the legacy process pool. For the
firmware-owned pool introduced in main 34159, select OVMF explicitly:

```powershell
pwsh -NoProfile -File apps/works/proofs/desk-render-worker.ps1 -Unit render -Bed ovmf -OwnedProcessPool -Mode all -Kernel seed/Codex.cdx
pwsh -NoProfile -File apps/works/proofs/desk-render-worker.ps1 -Unit lifecycle -Bed ovmf -OwnedProcessPool -Mode all -Kernel seed/Codex.cdx
pwsh -NoProfile -File apps/works/desk-present-proof.ps1 -Unit worker-loop -Bed ovmf -OwnedProcessPool -Mode all -Kernel seed/Codex.cdx
```

These runs require a guest-confirmed v4 handoff with the owned pool span
before grading the unchanged worker verdicts. Each result identifies its
process-pool mode. The switch refuses native or mixed-bed use; the presentation
runner accepts it only for `worker-loop`. These fixtures add one bounded
diagnostic print to the root process; production heap and time behavior is
unchanged. Physical HID and latency still require the sitting.

Measured 2026-10-01 with seed
`342D64BAC2A39ADA01A4F831BF2DE8EB6A7DD8CD634E34217904D23669F18056`:
owned-pool OVMF render, lifecycle and real-loop positives and controls pass
in `build-output/render-workers/owned342D-b`. Native and legacy-OVMF lifecycle
positives also pass. The marker establishes the selected handoff, while the
worker fixtures grade their existing behavior; allocation ownership and child
placement have the separate proof in the Architect's Sketchbook. The frozen
`F0863CAF` input diagnostic does not include this production worker integration.
The omitted-converter-flag control in `owned342D-control` prints a false
handoff marker and is refused before a worker PASS can be recorded.

The render runner grades exact color/depth parity, independent 1024-pixel copy
progress and untouched-tail canaries, stale owner/request refusal, close,
duplicate consumption and retained-frame survival after worker-slot poisoning.
Controls remove each named protection and must change only the designated
verdict. The lifecycle runner directly invokes production close, replacement,
hide and policy helpers around pane reclaim/poisoning. Its controls cover those
helpers, not every desktop call site. The worker-loop fixture invokes the real
desk loop with a synthetic queued key during active rendering and requires
cancellation before a completed frame. Its dispatch control requires the exact
expected false fields. Physical HID and latency remain outside these fixtures.

`apps/works/proofs/render-worker-admission.codex` renders a shadowed Aquarium
at the admitted 2048x1664 boundary and checks that refusing the next row
preserves the held request token, ready state and every color/depth pixel.
It snapshots both complete buffers before refusal, yields to the worker,
then compares every word and both original pointers before stopping it.
Independent controls flip the last color or depth pixel and must fail only
the corresponding comparison. Snapshot storage is `8 * width * height`
parent bytes, bounded by admission; copying and comparison are linear in
pixels with constant stack use. Production allocation and rendering costs
are unchanged.

```powershell
pwsh -NoProfile -File apps/works/proofs/desk-render-worker.ps1 -Unit admission -Bed both -Mode all -Kernel seed/Codex.cdx
pwsh -NoProfile -File apps/works/proofs/desk-render-worker.ps1 -Unit admission -Bed ovmf -OwnedProcessPool -Mode all -Kernel seed/Codex.cdx
```

Measured 2026-10-01 on seed `342D64BA`: the positive and both tail controls
pass in native, legacy OVMF and owned-pool OVMF. All retain the measured
27995912-byte worker peak. Evidence is in
`build-output/render-workers/admission-held`, including exact expected traces,
source and kernel hashes. This grades the completed held Aquarium frame
after one yield, not every resize timing or every scene.
After main 34185, compiler `267B6C83` reproduced every default/owned control
CDX byte-for-byte; `head-compat.exit` and `head-kernel.json` bind that check.

The affinity diagnostic relies on the native
startup path to initialize APs. Run serially, measuring memory admission before
each compile and guest as required by the project run rules:

```powershell
pwsh -NoProfile -File build/compile.ps1 -Src apps/works/proofs/render-worker-admission.codex -Out build-output/render-worker-admission.cdx -Log build-output/render-worker-admission.compile.log -Kernel seed/Codex.cdx
pwsh -NoProfile -File build/test-run.ps1 -Kernel build-output/render-worker-admission.cdx -OutFile build-output/render-worker-admission.actual
pwsh -NoProfile -File build/compile.ps1 -Src apps/works/proofs/render-worker-affinity.codex -Out build-output/render-worker-affinity.cdx -Log build-output/render-worker-affinity.compile.log -Kernel seed/Codex.cdx
pwsh -NoProfile -File build/test-run.ps1 -Kernel build-output/render-worker-affinity.cdx -OutFile build-output/render-worker-affinity.actual -Smp 2
```

Check each command's exit status before continuing. Every printed Boolean must
be True, with `render-admission-end` or `render-affinity-end`, respectively.

Measured 2026-10-01 with compiler
`0FD86AE43ACF36939E72BC968C3A98533C8FB80DA527E05BEEC72921D551806A`:
render and lifecycle positives/controls passed in native and OVMF, as did the
real-loop dispatch positive/control. The admission render peaked at 27995912
worker bytes; AP-affine slot reuse rendered on the BSP. Focused scene/input
regressions and the complete desk build passed. Evidence is in
`build-output/render-workers/final-render`, `final-lifecycle`, `final-loop`,
`final-resources` and `final-regress` in the fester workspace.
After merging through main 34046, the only changed chapter in the complete
GUI closure was `GopComposite`'s focus-ring change. Its native fixture, desk
build and worker-loop positives in both beds passed in `postmerge` and
`postmerge-loop`; the renderer, mailbox and pane integration were unchanged.

The complete OVMF GUI rehearsal in `build-output/render-workers/gui-final`
exercised keyboard launch, shadow toggle, mouse minimize/restore, maximize,
close/reopen and replacement of Aquarium by 3D View. Completed scene frames
resumed after these operations; `/` and `/api/health` returned 200 during
Aquarium. These observations supplement the direct-helper lifecycle fixture;
they do not turn it into exhaustive call-site coverage.

A same-dependency native GUI comparison at compiler `66AE634E` captured
248 ms/frame for the cooperative baseline and 171 ms/frame for the revised
worker at 1024x768. Both served HTTP during Aquarium. Each number is one HUD
capture on a shared host, not a general speedup or latency guarantee. Evidence
is `build-output/render-workers/gui/baseline/native` and
`build-output/render-workers/gui-v2/worker/native`.

## Direct scheduler selection tests

The ordinary regression is `codex/test/apps/desk-scheduler-selection.codex`
with its `.expected` and x86-64 sidecars. The default case requires three
normal-priority workers to receive bounded service and balanced loop progress
under timer expiry. The diagnostic runner is:

```powershell
pwsh -NoProfile -File apps/works/proofs/desk-scheduler-selection.ps1 -Kernel seed/Codex.cdx -OutDir <fresh-directory>
```

The runner selects both beds, both dispatch paths, every case and its
sabotage by default. Each guest has one core and 2048 MB RAM; each arm uses
the same compiled CDX in codex-vm and QEMU/OVMF. Compilation and execution
are serial. The configured host supplies QEMU and edk2 firmware under
`D:/Program Files/qemu`. `results.json` retains source, compiler, CDX and image hashes
with each trace. A failed positive is `PROPERTY-FAIL`; sabotage earns
`CONTROL-PASS` only for rejection with the designated symptom. Either a
failed property or an ineffective control makes the runner exit 1.

The blocked parent creates the workers and waits for the designated worker's
window to finish. The parent does not compete during measurement. Yield
cases set all child slices to zero and explicitly yield; timer cases seed
the normal scheduling slices and never yield in a worker. This separates
selection mechanisms without relying on a PIC mask.
The explicit slice setup isolates selection from runtime initialization;
the separate slice-initialization fixture below grades that contract.

| Case | Required observation | Sabotage |
|---|---|---|
| Equal-priority pair and triple | Every worker samples at least two distinct ticks; initial, internal and final gaps are at most 12 ticks over an 80-tick window. Every pair of workers has a loop-work ratio within 2:1. | Omit worker yields on the yield path; zero normal slices on the timer path. Worker 2 must receive no samples. |
| Newly runnable channel receiver | Receiver is observed BLOCKED before sending 77, receives 77, and resumes within both 4 PIT ticks and 50000 us of the pre-send timestamp. | Publish the message with the receiver assigned to unavailable core 1, then restore core 0 after at least 8 ticks. Delivery must still occur and exceed a latency bound. |
| One low-priority worker beside two system workers | Repeated low-priority samples; all gaps at most 606 ticks over 1250 ticks on the timer path. | Reset the relief counter from system-worker iterations. Worker 3 must receive no samples. |
| Two low-priority workers beside two system workers | Both receive repeated samples; all gaps at most 1206 ticks over 2500 ticks on the timer path. | Reset the relief counter. Worker 3 must receive no samples. |

Yield variants of the background cases use the 80-tick window and 12-tick
gap limit; their sabotage removes yields. The timer background allowances
cover one relief opportunity per 100 expirations at system slices of 6 ticks,
one or two low-priority contenders, and a slice of margin. They are diagnostic
acceptance thresholds, not universal scheduler guarantees. Clock or fuel
exhaustion before the tick window completes is a fixture failure, not a
passing negative control. Wake timing is a conservative pre-send/post-receive
envelope, not the exact READY-to-RUNNING interval or a latency distribution.

Measured 2026-09-30 with depot seed `561EEBFC3ED59D96`: timer selection gave
the third equal-priority worker zero samples over 80 ticks. Periodic relief
gave the second low-priority worker zero samples even over 2500 ticks.
Both failures occurred in both beds; the corresponding yield cases passed.
The corrected two-contender allowance was measured against the old seed
before the repair, and retained the zero-service failure.

The repair scans cyclically after the current slot for equal-priority ties.
Relief retains an independent last-selected cursor and scans every slot
cyclically. Priority comparisons, affinity checks and the AP restriction
against claiming proc 0 remain in both paths. The counter and cursor use
separate 32-bit halves of the existing eight-byte starvation slot; ordinary
startup and runtime initialization clear both halves. The periodic relief
state remains shared across cores, so the single-core measurements do not
assert a multicore starvation bound.

The candidate matrix passed every positive and designated control in both
beds. With three equal-priority workers, each bed recorded sample counts
27, 27 and 26, with maximum gaps of 7 ticks. With two low-priority workers,
both beds recorded first samples at ticks 600 and 1195 and subsequent samples
at 1790 and 2385. The largest gaps were 1190 and 1195 ticks.

| Wake path | codex-vm envelope, us | OVMF envelope, us |
|---|---:|---:|
| Yield | 28 | 300 |
| Timer expiry | 30002 | 30568 |

Microseconds are guest HPET readings converted with the guest's advertised
rate and rounded down. The host was shared; these are individual observations,
not physical input latency promises. Candidate evidence is
`D:/Projects/Cobblestone-fester/build-output/desk-selection-20260930/candidate-matrix/`.
Old-seed evidence is under `matrix/` and `two-round-baseline/` in the same
parent directory. The unsigned compiler used for the candidate matrix has
SHA-256 `FB29A4EE01BE4B68AB3BE75803FA47DCF74B213E554A7F9254CDE61704C40B50`.

The merged signed candidate has SHA-256
`8C4B42085FE2A036D76E92DC6FA973FA54A5D915E7F5001ECBA0AE3BDB19F874`.
Every saved diagnostic source was recompiled with that candidate and produced
a byte-identical CDX, recorded in `seed-proof3/matrix-compatibility.json` under
the evidence root. The retained runtime traces therefore grade the same
test artifacts. The merged depot seed `3C9A3DFD` prints `timer rotation: False`
for the ordinary regression; the merged candidate prints `timer rotation: True`.
The fresh fixed point, signature check, BVT and scheduler regression logs are
under `seed-proof3/`. The source snapshot hash is
`FB4A9970F7D8E440DE661C354008F4AC35E1726A838317C246CA95920DAA3D32`;
unsigned stage 2 and stage 3 both hash to
`42BB3DBABDB94AA6FB55CD190403B553441A94BCA9324987FC60788B613C591A`.

The scheduler adds no heap allocation or per-process storage. Both selection
scans remain bounded by the process-table size. The emitted instruction
sequences add fixed compiler work per generated runtime. The diagnostic has
fixed per-worker cells, no per-iteration collections, fuel-bounded tail loops
and bounded reporting after worker termination. Loop-work balance measures
useful loop progress, not exact CPU-time shares.

## Single-core preemption evidence

`emit-slice-table-init` supplies kernel/system/normal/background slices
0/6/3/1 for both ordinary startup and `runtime-init`. Initialization occurs
before interrupts are enabled. Process 0 keeps its zero slice; a child
selected by yield reloads its slice from this table.

`codex/test/apps/runtime-init-slices.codex` first reads the entry table,
poisons all four qwords with nonzero bytes, then calls `runtime-init`.
The exact table predicate therefore tests every store, including kernel
zero. A spawned child performs no yield or blocking operation. The parent
holds cooperatively, then yields and requires child progress with completion
still false and timer ticks advancing. The `slice-control = 1` variant zeros
only the normal slice after initialization; it must instead observe child
completion, with valid PID, clock rate, progress and advancing ticks.

Measured 2026-09-30, one core in each bed. The depot compiler was
`8C4B42085FE2A036`; the candidate fixed point was `BD4CE1D6F8E03AF5`,
installed with signature as `AF9057E86BDB4FD2`. The same CDX for each arm
ran in codex-vm and OVMF. All six traces were checked against their exact
predicates and completion marker.

| Arm | Runtime table restored | Timer return before completion | Child completion |
|---|---|---|---|
| Depot compiler, poisoned table | False | False | True |
| Candidate, poisoned table | True | True | False |
| Candidate, zero-normal control | True | False | True |

Both beds produced the rows above and preserved cooperative kernel behavior.
Before poisoning, ordinary startup had the expected table; direct OVMF
entry did not. The native `.expected` pins the ordinary startup result.
Raw sources, kernels, logs and control outputs are in
`D:/Projects/Cobblestone-reek/build-output/compiler111/`.

Compile the fixture with `build/compile.ps1 -Src <source> -Out <cdx>`
plus mandatory `-Log <log> -Kernel <candidate>`. Make a separate scratch
copy changing only `slice-control` to 1 for the control. Run serially with
`build/test-run.ps1 -Kernel <cdx> -OutFile <out>` (3072 MB, one core).
For OVMF, wrap that CDX using `build/cdx-to-pe.ps1 -CdxInput <cdx>`
with `-Out <efi> -HeapPages 32768 -ExitBootServices`, then
`build/build-img.ps1 -PeInput <efi> -Out <image>`. Use QEMU q35/TCG,
2048 MB, one core, private edk2 code/vars copies and a private serial log.
Require `slice-proof-end` within 60 seconds; stop only the owned guest.
Grade both `timer-return` and `control-completed`; a False timer result
alone does not identify the control's failure mechanism.

The initializer has fixed emitter cost and no runtime heap allocation.
The fixture retains its shared cell through child cleanup, uses fixed
allocations and performs no per-iteration allocation. Polling work is linear
in iterations executed. Fuel limits and host timeouts bound faulty-clock
runs; child completion is not a measurement of one second elapsed.
These tests establish initialization and single-core timer return, not
multicore fairness, physical latency or safe render-worker ownership.

codex-vm holds a masked IRQ0 as the 8259 does (`codex/test/ops/pic-mask-pit`), so
the separate PIC-mask control can now grade native preemption in both beds; it
has not been re-run on that codex-vm. Its earlier evidence is in
`D:/Projects/Cobblestone-fester/build-output/desk-cpu-20260930/`.

## Cost and arithmetic evidence

The physical-acceptance correction has focused fixtures under
`apps/works/proofs/`: `desk-buffered-present.codex` for publication and
partial-copy rollback, `scene-animation-time.codex` for animation phase
delivery with a fixed camera, and `renderer-weight-range.codex` for the
software-raster arithmetic fault exposed during Aquarium animation.

Scene construction and owned-pose updates receive the HPET-derived animation
phase independently of camera yaw. Fish headings use normalized CORDIC
quaternions across a full turn. The software rasterizer uses signed edge
determinants for barycentric weights, avoiding the fourth-degree products
in the old formulation. Publication
and rollback allocate no memory; raster weights remain scalar per-pixel work.

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
