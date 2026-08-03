# SMP -- Symmetric Multi-Processing for Codex

**Date**: 2026-06-17
**Author**: fester
**Status**: Atomics live on all three backends (CL 4626). x86-64 AP bring-up is real (BACKLOG 4.3), an AP fault is diagnosable (4.4), and **an AP claims processes out of the table, runs them, is preempted, halts when idle, honours affinity, and cannot strand proc 0 -- 4.1 and all of BACKLOG 4.11(a)-(f) are CLOSED (last two 2026-07-15, blu).** The **x86-64 process scheduler is SMP-complete**: work stealing was never a separate gap (the single global 16-slot process table already IS the shared run queue). **Phases 3, 4 and 5 below remain a pure model, not the running scheduler** -- the live scheduler is in the emitter (see Remaining Work).

**DONE 2026-07-15 (blu). BACKLOG 4.2 is closed on both backends, so this doc
moves to `Done/`.** AP boot now exists on x86-64 (INIT/SIPI), RISC-V
(park-the-secondaries) and ARM64 (PSCI). **RISC-V:** the plug's `__start` reads
`mhartid`, hart 0 boots normally, a non-zero hart marks a DRAM cell and parks;
under QEMU `virt -smp N -bios none` every hart enters `__start` at reset, so the
CLINT/park model is all that is needed (no SBI). **ARM64:** QEMU holds the
secondaries in PSCI, so core 0's `__start` issues PSCI `CPU_ON` (conduit HVC,
function id `0xC4000003`) to start core 1 at an AP stub that marks a cell and
parks. Proven by `codex/test/smp-riscv-boot.codex` and `smp-arm64-boot.codex`
(each reads a cell only a non-zero core writes; non-zero at `-smp 2`/`4`, "AP
DID NOT RUN" at `-smp 1`), run by `build/test-cross-smp.ps1` under QEMU. What is
NOT built on RISC-V/ARM64 (and is not part of 4.2): a secondary joining a
scheduler -- these backends have no process table or scheduler; the live
scheduler is x86-64-only, in the emitter. (The channel-block lost-wakeup,
formerly 4.11(g), is CLOSED as of 2026-07-15 -- `chan-send-block`/
`chan-recv-block` now do the same block-then-recheck-and-retry as `process-wait`,
proven with a yield-injection mutant; see `docs/Test/YieldInjectionMutant.md`.)
**Depends on**: preemptive scheduler (done), interrupt model (done),
punctual keyword (done), ARM64/RISC-V backends (GCC-O0 parity)

---

## The Problem

Codex runs on a single core. The scheduler is cooperative (yield points
in function prologues), the heap allocator is a single-threaded bump
pointer (R10/bivy), and all data structures assume exclusive access.
Every multi-core chip we target -- x86-64 (2-128 cores), Cortex-A72
(4 cores), ESP32-C6 (2 cores), RP2040 (2 cores) -- wastes all but one
core.

The web server, the agent runtime, the game engine, and the compiler
itself would all benefit from parallel execution. But SMP is not free:
it requires hardware-specific boot protocols, memory ordering
guarantees, lock-free data structures, and per-core isolation of
mutable state.

## What We Have

| Component | Single-core status | SMP gap |
|-----------|-------------------|---------|
| Scheduler (TaskQueue) | 4-level priority, cooperative yield | Single run queue, no per-core dispatch |
| Heap (bivy) | Bump pointer in R10 | Single arena, no per-core allocation |
| Interrupt model | IOAPIC + HPET on x86-64 | No IPI, no per-core LAPIC init |
| Process table | 16 slots, capability bits | No core affinity, no migration |
| Emitter (x86-64) | Full scalar + SSE2 + SIMD | Atomics done (LOCK/XCHG/CMPXCHG) |
| Emitter (ARM64) | GCC-O0 parity | Atomics done (LDAR/STLR/LDXR/STXR/DMB) |
| Emitter (RISC-V) | GCC-O0 parity | Atomics done (fence + LR/SC) |
| AP boot | x86-64 INIT/SIPI trampoline | No PSCI (ARM64), no SBI/CLINT (RISC-V) |
| `codex/os/sched/*` | CoreState, CoreHeap, IPI, LockFreeChannel, TaskQueue all written | **A pure model, not a scheduler.** Immutable records, tasks as `Text`, no process table, no context switch, no atomics. It cannot be "called from" the kernel loop -- see Remaining Work |
| Live scheduler | Process table + yield/exit/wait + channel blocking + timer preempt, all in the **emitter** | **An AP runs processes, is preempted, and halts when idle (4.1 + 4.11(a)-(f) closed).** The running process is no longer a global (step 1), the claim is a `lock cmpxchg` and it claims before it publishes (step 2), an AP dispatches out of the table instead of halting (step 3), a per-core LAPIC timer preempts it (4.11a), it honours affinity (4.11b), it `hlt`s when idle with an IPI wake (4.11d), and a non-zero core cannot claim slot 0 (4.11e). The channel-block lost-wakeup (4.11g) is closed too, so the process scheduler has no open residual |

## Architecture

### Phase 1 -- Atomic Primitives (language + emitter) -- DONE (all three backends)

Six atomic builtins. x86-64 landed in CL 4626; the ARM64 and RISC-V
plugs carry the same six (ARM64: `a64-emit-memory-fence` / `-atomic-load`
/ `-atomic-store` / `-atomic-cas` / `-atomic-add` / `-atomic-exchange` in
`codex/plugs/arm64/Arm64CodeGen.codex`, dispatched from
`Arm64CodeGen2.codex`; RISC-V mirrors it in
`codex/plugs/riscv/RiscVCodeGen.codex`).

The operations, in the language and all three backends:

```
  atomic-load     : Ptr a -> [Atomic] a
  atomic-store    : Ptr a -> a -> [Atomic] Nothing
  atomic-cas      : Ptr a -> a -> a -> [Atomic] Boolean
  atomic-add      : Ptr Integer -> Integer -> [Atomic] Integer
  atomic-exchange : Ptr a -> a -> [Atomic] a
  memory-fence    : -> [Atomic] Nothing
```

These are builtins, not library functions. The emitter maps them to:

| Builtin | x86-64 | ARM64 | RISC-V |
|---------|--------|-------|--------|
| atomic-load | MOV (x86 loads are acquire on aligned) | LDAR | LW + FENCE R,RW |
| atomic-store | MOV + MFENCE (or XCHG) | STLR | FENCE RW,W + SW |
| atomic-cas | LOCK CMPXCHG | LDXR/STXR loop | LR/SC loop |
| atomic-add | LOCK XADD | LDXR/ADD/STXR loop | AMOADD |
| memory-fence | MFENCE | DMB ISH | FENCE RW,RW |

The `[Atomic]` effect prevents accidental use in non-concurrent
contexts. `punctual` functions cannot use `[Atomic]` (atomics have
variable latency from contention).

### Phase 2 -- Per-Core Bootstrap -- DONE (boot gate + VM flag)

SMP is opt-in. The boot sequence reads the core count from
`ap-core-count-addr` (GPA 0xFF8); if <= 1, SMP init is skipped
entirely and the existing single-core path runs unchanged. The VM
flag `-smp N` (default: single-core) writes the requested count
to guest memory before boot and creates N vCPUs + MADT entries.

#### x86-64 (codex-vm + bare metal)

1. BSP (core 0) boots normally, initializes IOAPIC, HPET, memory map.
2. BSP writes AP boot trampoline to low memory (real-mode stub at
   0x8000 that transitions to long mode and jumps to `ap-entry`).
3. BSP sends INIT IPI + STARTUP IPI to each AP via LAPIC ICR.
4. Each AP executes trampoline, sets up its own GDT/IDT/TSS/CR3
   (shared page tables), stack (per-core stack from a pre-allocated
   pool), and calls `ap-entry`.
5. `ap-entry` initializes the AP's LAPIC, enables interrupts, and
   enters the per-core scheduler loop.

#### ARM64 (PSCI)

1. Primary core boots, discovers secondaries via MPIDR.
2. Calls PSCI `CPU_ON` (SMC/HVC) for each secondary with entry
   point = `ap-entry-arm64` and per-core stack pointer.
3. Each secondary initializes its GIC redistributor (GICR), enables
   interrupts, enters scheduler loop.

#### RISC-V (SBI)

1. Hart 0 boots, discovers harts via device tree.
2. Calls SBI `sbi_hart_start` for each secondary hart.
3. Each hart initializes its PLIC context, enters scheduler loop.

#### RP2040 (dual Cortex-M0+)

1. Core 0 boots normally.
2. Core 0 writes entry point to SIO FIFO, core 1 reads it from
   its boot ROM spin loop.
3. Core 1 sets up its own stack (from linker-defined region) and
   vector table, enters scheduler loop.

### Phase 3 -- Per-Core Scheduler

The single TaskQueue becomes N per-core run queues plus a global
overflow queue:

```
  CoreState = record {
    core-id : Integer between 0 and 255,
    run-queue : TaskQueue,
    current-task : Maybe TaskId,
    idle-ticks : Integer between 0 and 4294967295,
    stack-base : Integer,
    stack-top : Integer
  }
```

**Work stealing**: when a core's run queue is empty, it steals from
the longest queue. The steal operation uses `atomic-cas` on the
victim's queue tail. Lock-free Michael-Scott queue or Chase-Lev
work-stealing deque.

**Core affinity**: tasks can be pinned to a core (`pin-to-core N`)
for cache locality or hardware-bound work (interrupt handlers,
DMA channels).

**Load balancing**: periodic rebalancing (every 1000 ticks) migrates
tasks from overloaded cores to idle ones. Migration is cooperative:
the task is dequeued from source and enqueued on target, both via
atomic operations.

### Phase 4 -- Per-Core Heap

The single bivy bump pointer becomes per-core arenas:

```
  CoreHeap = record {
    base : Integer,
    top : Integer,
    hwm : Integer,
    core-id : Integer between 0 and 255
  }
```

Each core allocates from its own arena (no contention on the fast
path). Cross-core references are safe because all arenas share the
same address space and page tables. The only synchronization point
is arena creation (at boot, from the global pool).

For the x86-64 3GB arena: divide equally among cores. 4 cores =
768MB each. Enough for compilation (the compiler uses ~600MB peak)
but not for 4 simultaneous compilers. Larger memory (Phase:
non-contiguous physical memory) would help.

### Phase 5 -- IPI and Cross-Core Communication

**Inter-Processor Interrupts** for:
- Scheduler wake-up (idle core receives work)
- TLB shootdown (page table changes)
- Panic broadcast (one core faults, all cores halt)

**Lock-free channels**: the existing Channel module becomes lock-free
using atomic-cas on head/tail pointers. MPSC (multiple producer,
single consumer) is the natural fit for work distribution.

## What NOT To Do

- **No kernel-level locks.** The bare-metal runtime has no mutex. SMP
  must use lock-free algorithms or per-core isolation. Adding locks
  would introduce priority inversion and unbounded blocking --
  incompatible with `punctual`.

- **No shared mutable state by default.** Tasks on different cores
  share nothing. Communication is via channels (lock-free MPSC) or
  atomic variables. The effect system enforces this: `[Atomic]` is
  required for cross-core access.

- **No NUMA awareness (yet).** All cores see the same memory with
  uniform latency. NUMA is a Phase 6+ concern for server-class
  hardware.

## Phasing vs Dependencies

| Phase | Depends on | Blocks | Status |
|-------|-----------|--------|--------|
| 1 (atomics) | Emitter changes only | Phase 3, 4, 5 | DONE -- x86-64, ARM64, RISC-V |
| 2 (boot) | Phase 1, LAPIC/GIC driver | Phase 3 | DONE for x86-64 (opt-in `-smp N`); ARM64/RISC-V open |
| 3 (scheduler) | Phase 1, 2 | Phase 5 | Module DONE (CoreState + work stealing); not wired |
| 4 (heap) | Phase 1 | Nothing (can parallelize with 3) | Module DONE (CoreHeap per-core arenas); not wired |
| 5 (IPI/channels) | Phase 3 | Web server, agent runtime | Module DONE (IPI + LockFreeChannel); not wired |

## Remaining Work

**Status, 2026-07-15 (blu). The x86-64 process scheduler is SMP-complete.**
BACKLOG 4.11(a)-(f) are all closed: an AP is preempted by a per-core LAPIC
timer (a), honours core affinity (b), halts when idle and is woken by IPI
(d), never strands proc 0 (e); the mid-exit preempt deadlock (f) and the
wait/exit double-run race are fixed. Work stealing (Phase 3) is not a gap --
the single global process table is the shared run queue -- and CoreHeap
(Phase 4) is unused off the critical path. The channel-block lost-wakeup
(4.11g) is now fixed -- `chan-send-block`/`chan-recv-block` got the same
block-then-recheck-and-retry as `process-wait`. **One item remains and is why
this doc is still Active: AP boot on ARM64/RISC-V (BACKLOG 4.2)** is unbuilt.
When it lands, move this doc to `Done/`.

**Correction, 2026-07-13 (blu). "Wire the sched modules into the live
kernel loop" was the wrong instruction and it sat here for weeks.**

`codex/os/sched/*` is a **pure functional model**, not a scheduler.
`CoreTable` is an immutable record over a `List CoreInfo`; a task is a
`Text` name; `core-schedule` returns a new table; `ipi-send` appends to
a list. It never touches the process table, never context-switches, and
never calls one of the six atomics. **There is no call site that turns
it on**, and looking for one is a dead end. What it is good for is
what it already does: an executable specification of work-stealing and
load-balancing policy, tested in isolation, that a real scheduler can
later be checked against. Phases 3, 4 and 5 above describe that model,
not the machine.

The **live scheduler is in the emitter**, and it is what an AP must
join:

| Piece | Where |
|---|---|
| Process table, 16 slots x 256 bytes | `proc-table-base` (0x5000) |
| Cooperative switch | `process-yield`, `process-exit`, `process-wait` (`X86_64ProcessHelpers.codex`) |
| Blocking switch | channel send/recv, `emit-find-next-ready` (`X86_64IPCHelpers.codex`) |
| Preemptive switch | timer ISR, `__interrupt_common` (`X86_64Boot.codex`) |
| Context restore | `__process_resume` -- takes the table entry in RSI |

### Step 1 -- the running process is not a global (DONE)

`current-proc-addr` (cell 28688) named the running process and was read
at 38 sites. It is correct for exactly one core and for no more than
one: two cores each running a process would each overwrite that cell,
and each would then resume the other's saved context. **That one cell
was the whole blocker.**

It is deleted, and **nothing per-core replaces it**, because nothing
needs to be stored. A spawned process's stack already lives in its own
slot-indexed region (`spawn-pool-base + slot * spawn-slot-region-size`),
so the running slot is a pure function of RSP:

```
    mov  dest, rsp
    sub  dest, spawn-pool-base
    cmp  dest, spawn-pool-span
    jae  .proc0                  ; outside the pool (also catches RSP < base)
    shr  dest, spawn-slot-region-shift
    jmp  .done
.proc0:
    xor  dest, dest              ; the booted program
.done:
```

`emit-load-current-proc` / `emit-slot-from-addr` in `X86_64Boot.codex`.
No memory read, no per-core cell, no MSR, no LAPIC access, and it is
cheaper than the load it replaced. The timer ISR must use the
**interrupted** RSP off the interrupt frame (`[rsp+64]`), not the one it
is handling on.

### Step 2 -- claim first, publish second (DONE)

Every scan-and-claim was `cmp state, READY` followed by `mov state,
RUNNING`. That is a TOCTOU: safe on one core, unsafe on two. All five --
`process-yield`, `process-exit`, `process-wait`, `emit-find-next-ready`
and the timer ISR -- now claim with `lock cmpxchg`. Losing the race is
not an error; another core took the slot, so keep scanning.

**Atomicity alone was not the fix.** `process-yield` marked *itself*
READY **before** it scanned for a replacement. On one core that is
harmless -- nobody else is looking. With an AP dispatching it advertises
a process as claimable *while its own core is still executing it*: the
AP claims it, the BSP finds no replacement, takes its `no-switch` path,
and carries on running the process the AP has now also resumed.

So the order is inverted: **secure a replacement with a successful
cmpxchg first, and only then mark yourself READY.** A core standing
inside a process never advertises it. The no-switch path has nothing to
undo, because it never published. The timer ISR's preempt path had the
same shape and got the same treatment -- with one extra wrinkle: its
publish is *itself* a cmpxchg (RUNNING → READY), because the max-ticks
check above it may have already marked the process a ZOMBIE and a plain
store would resurrect it.

**New invariant, and the protocol depends on it:** the running process's
state word is RUNNING. Proc 0's table entry is zeroed at boot, so it is
now explicitly initialised to RUNNING; otherwise the ISR's RUNNING →
READY publish fails against it and the BSP can never be scheduled back
onto proc 0.

**A trap that cost a hang, written down so it is not re-found:** in the
timer ISR, RCX holds `&proc[current]` for the context save -- and the
**starve check clobbers it** (`sv1` loads the starve counter into RCX).
Anything below that point which wants `&proc[current]` must rebuild it
from RDI, which does survive. The original code reloaded it for exactly
this reason.

### Step 3 -- the AP dispatch loop (DONE)

An application processor no longer halts. It goes to **`__idle_dispatch`**
-- the same place the boot processor goes when it runs out of work -- with
its core id in R15, claims a READY process with a `lock cmpxchg`, and runs
it. Six children at `-smp 4` execute across four cores; the same binary
works at 2 and 8. `codex/test/smp-dispatch.codex` pins it.

**The thing that made it hard, and that a first attempt got wrong:**

> **A core that parks is still standing on the stack of the process it was
> last running.**

`process-wait` marks itself BLOCKED and spins. The moment the child it
waits on exits, the wake loop marks it READY, another core claims it, and
resumes it **on that stack**. Two cores, one stack -- and the parked core's
next interrupt or fault pushes a frame straight through the other core's
process. It showed up as `!EXC=08` with **RSP = 0** and a core executing
inside the process table.

So a parked core **leaves the stack first and scans afterwards**, on its
own per-core idle stack in always-present low memory. The AP idle stacks
already live there and are handed out from index 1, so slot 0 of that
region was free and is now the boot processor's idle stack:
`idle_top(core) = ap-stacks-base + (core + 1) * ap-stack-size`.

To know which idle stack is its own, a core has to know which core it is.
**It reads that out of the process it is standing in.** `proc-core-offset`
(entry offset 8, which was free) records the core that claimed the slot,
stamped by whoever won the CMPXCHG; the AP's bring-up seeds its own id
from R15. That is the entire per-core identity mechanism -- **no MSR, no
LAPIC read, no GS base.** A core asks the process it is running.

Core 0 may claim slot 0; an AP may not. Slot 0 is the program the machine
booted and it owns the boot stack and the main heap.

`process-exit`, `process-wait` and the channel block path all now end in
`jmp __idle_dispatch` rather than carrying their own scan. `process-yield`
keeps its own scan, and safely -- it never publishes itself before it has
won a claim, so it is never standing on a stack anyone can take.

### What step 3 looked like before it worked (kept: the diagnosis is the useful part)

An AP still ends bring-up at `hlt; jmp hlt` (`emit-smp-init`). The loop
that replaces it: sentinel and cursor both start at slot 0 so the walk
covers 1..15 and stops on wrap (**never claim slot 0** -- proc 0 is the
booted program and owns the BSP's stack and main heap); claim a READY
slot with `lock cmpxchg`; load the slice from `slice-table-addr`; `jmp
__process_resume` with RSI = the table entry; on no work, `pause` and
walk again.

**This was built and it worked** -- an AP claimed a spawned process,
executed it, and its side effect landed. A one-child probe at `-smp 4`
printed `val: 9` and `ap claims: 1`, where the claim counter is a cell
only the AP dispatch loop writes. **So the capability is real.** It is
not landed because a six-child probe crashes, and the cause is worth
having written down before anyone tries again:

**A core parked with no work is still standing on the stack of the
process it was last running.** `process-exit` and `process-wait` spin
their idle scan *on the exiting/blocking process's own stack*. That
process is about to be made READY by the wake loop -- at which point
another core claims it and resumes it **on the very stack the parked
core is still executing on**. Two cores, one stack. The crash signature
is a `!EXC=08` with **RSP = 0** and a `!EXC=06` executing inside the
process table at `RIP≈0x502a`: a core resumed a context that was being
rewritten underneath it.

The AP's *initial* dispatch loop gets this right by accident -- it spins
on its own idle stack, which nothing else owns. The shared idle paths do
not. **The fix is that a core with no work must leave the process's
stack before it parks** (switch RSP to a per-core idle stack, then
scan). That needs a per-core identity, which is the one thing the
RSP-derivation of step 1 deliberately does not give you -- an AP knows
its core id in R15 at bring-up, but a process resumed on it does not.
The cleanest route is probably to carry the owning core in the process
table entry, written by whoever claims the slot, so a core can always
ask "which core am I" by reading the process it is running.

Two further things the probe surfaced:
- **Proc 0 migrates -- now FORBIDDEN (BACKLOG 4.11e, CLOSED 2026-07-15).**
  Once the wake loop marked proc 0 READY, an AP's preempt scan could claim
  it and strand it RUNNING-but-frozen on a core that is not its own. Both
  preempt-ISR candidate scans now skip slot 0 when the claiming core id is
  non-zero, mirroring `__idle_dispatch` (which already refused slot 0 to an
  AP by starting each core's scan at its own id). Proc 0 stays the boot
  processor's to schedule; genuinely *moving* it would need a hand-off, not
  a claim, and remains unbuilt by choice.
- Interrupts are **not** the cause: the crash reproduces with
  `CODEX_VM_NO_TIMER=1` and with the AP resuming at IF=0.

### The test that proves it

Of the `smp-cores` shape, and for the same reason: **find a counter only
an AP can write, and make sure nothing else can write it.** The probe
used a cell (36200) bumped only inside the AP dispatch loop's claim, and
read it back from the guest. A test showing that N processes completed
proves nothing -- the BSP alone can do that, and a test that cannot fail
is how SMP came to be faked here in the first place.

**Cell hygiene, learned the hard way:** 36152 is a permanent booby trap
(a legacy codex-vm output-ring position) and 36160 / 36168 / 36176 are
codex-vm's blit cells. **Grep `tools/codex-vm.c` before claiming any
cell in this band.** 36200 is free.

### AP boot on RISC-V -- CLOSED 2026-07-15 (blu)

A secondary hart executes guest code. The insight is that RISC-V needs
no SBI here: under QEMU `virt -smp N -bios none` **every hart enters the
kernel at the reset entry** -- there is no OpenSBI to park the
secondaries -- so the bring-up is the park-the-secondaries model, not
`sbi_hart_start`. The plug's `__start` (`rv-emit-runtime`,
`RiscVRuntime.codex`) reads `mhartid` (`csrr`) as its first act; hart 0
takes the existing boot path (stack, FP, heap, CCE tables, trap vector,
`opening`); a hart whose id is non-zero branches to a four-instruction AP
path that stores 1 to a fixed DRAM cell (`0x80090000`) and parks in
`wfi; j .`. No new instruction encoder was needed. The marker is written
only by a core that ran our guest code -- the `smp-cores` discipline
(a counter only an AP can write). `codex/test/smp-riscv-boot.codex` reads
it back: "an ap executed guest code" at `-smp 2`/`4`, "AP DID NOT RUN" at
`-smp 1`, so **the test can fail**. Run by `build/test-cross-smp.ps1`
under QEMU (a `.smp` sidecar carries the core count; the single-core
cross batteries skip `.smp` tests). Plug-only; no seed.

Not yet built on RISC-V, and not needed for the proof: CLINT software
IPIs (MSIP at `0x02000000`) between harts, and any real per-core work --
the AP path only parks. A secondary joining a scheduler is moot here
anyway, because these backends have no process table or scheduler (that
lives only in the x86-64 emitter).

### AP boot on ARM64 -- CLOSED 2026-07-15 (blu)

A secondary ARM64 core executes guest code. Unlike RISC-V, QEMU `virt` does
NOT auto-enter the secondaries -- a spike (`qemu-system-aarch64 -M virt
-smp 2`) produced a single core's output, so the secondaries are held in
QEMU's PSCI implementation. Core 0's `__start` (`a64-emit-runtime`,
`Arm64Runtime.codex`) therefore issues **PSCI `CPU_ON`** to start core 1:
`x0` = `0xC4000003` (CPU_ON, SMC64 id), `x1` = target core (1), `x2` = the AP
stub's address (PC-relative `adr`, so it is position-independent), `x3` = 0
(context), then `hvc #0`. **The conduit is HVC on QEMU virt** (proven by
spike -- a second spike issuing `CPU_ON` over HVC ran the secondary), so no
EL3/secure firmware is needed and no spin-table. The AP stub stores 1 to
`0x60000000` (mid-RAM, clear of the heap and stack) and parks in `wfi; b .`.
No `mpidr` read is needed: the secondary enters the AP stub directly, and
core 0 is the only core at `__start`. No new instruction encoder -- `hvc` is a
literal word and `adr` is a small byte-computing helper (`a64-adr-bytes`).
QEMU boots the ELF (honouring its `0x40100880` entry); ARM64 emits no flat
`.bin`. Plug-only; no seed. `codex/test/smp-arm64-boot.codex` pins it.

The per-core GIC redistributor is not initialised and is not needed for the
proof (the AP stub takes no interrupts). Atomics are no longer a gap on any
backend.

## Effort Estimate

| Phase | Scope | Rough size |
|-------|-------|-----------|
| 1 | 6 builtins x 3 backends + effect | ~500 lines emitter + ~100 lines type checker |
| 2 | AP boot trampoline + per-core init | ~300 lines x86-64, ~200 lines ARM64, ~100 RISC-V |
| 3 | Lock-free scheduler + work stealing | ~800 lines |
| 4 | Per-core heap split | ~200 lines |
| 5 | IPI + lock-free channels | ~400 lines |
| Total | | ~2500 lines across 5 phases |

## References

- `codex/os/sched/TaskQueue.codex` -- current single-core scheduler
- `codex/os/sched/OsScheduler.codex` -- OS tick loop
- HardRealtime design (punctual, Phase 4 defers multi-core)
- Intel SDM Vol. 3 Ch. 8 -- INIT/STARTUP IPI, LAPIC ICR
- ARM PSCI specification (DEN0022D)
- RISC-V SBI specification (v2.0)
- RP2040 datasheet Section 2.8 -- SIO, multicore launch
