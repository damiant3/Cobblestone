# SMP -- Symmetric Multi-Processing for Codex

**Date**: 2026-06-17
**Author**: fester
**Status**: Design proposal
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
| Emitter (x86-64) | Full scalar + SSE2 + SIMD | No LOCK prefix, no XCHG, no CMPXCHG |
| Emitter (ARM64) | GCC-O0 parity | No LDXR/STXR, no DMB/DSB |
| Emitter (RISC-V) | GCC-O0 parity | No LR/SC, no FENCE |

## Architecture

### Phase 1 -- Atomic Primitives (language + emitter) — DONE (CL 4626)

Six atomic builtins on x86-64. ARM64/RISC-V backends not yet done.

Add atomic operations to the language and all three backends:

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

### Phase 2 -- Per-Core Bootstrap — DONE (boot gate + VM flag)

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
| 1 (atomics) | Emitter changes only | Phase 3, 4, 5 | DONE (CL 4626, x86-64 only) |
| 2 (boot) | Phase 1, LAPIC/GIC driver | Phase 3 | DONE (opt-in `-smp N`, x86-64) |
| 3 (scheduler) | Phase 1, 2 | Phase 5 | DONE — CoreState + work stealing |
| 4 (heap) | Phase 1 | Nothing (can parallelize with 3) | DONE — CoreHeap per-core arenas |
| 5 (IPI/channels) | Phase 3 | Web server, agent runtime | DONE — IPI + LockFreeChannel |

All 5 phases are done for x86-64. The boot gate is opt-in: without
`-smp`, the single-core path is unchanged. ARM64/RISC-V atomics
and boot are not yet done. The SMP infrastructure is complete at
the OS module level; wiring into the actual kernel scheduler loop
and adding hardware LAPIC ICR writes are integration tasks.

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
- `docs/Designs/OS/Active/HardRealtime.md` -- punctual, Phase 4 defers multi-core
- `docs/Designs/Features/Active/CAMP-IIIC-STRUCTURED-CONCURRENCY.md` -- green threads
- `docs/Designs/Features/Active/STDLIB-AND-CONCURRENCY.md` -- concurrency vision
- Intel SDM Vol. 3 Ch. 8 -- INIT/STARTUP IPI, LAPIC ICR
- ARM PSCI specification (DEN0022D)
- RISC-V SBI specification (v2.0)
- RP2040 datasheet Section 2.8 -- SIO, multicore launch
