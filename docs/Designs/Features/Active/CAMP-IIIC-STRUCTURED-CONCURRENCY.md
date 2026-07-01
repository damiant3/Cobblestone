# Camp III-C — Structured Concurrency

**Original**: 2026-03-24
**Revised**: 2026-05-01
**Status**: Partially shipped -- Phase 1 (fork/await x86 emit) and most of Phase 2 (green-thread scheduling, par-map, reclaim) live with tests. Pending: Phase 3 (IrHandle codegen), Phase 4 (OS processes).
**Depends on**: Effect type tracking (done), Capability enforcement (done),
x86-64 bare-metal backend (done)

---

## Context: What Changed Since March

The original design assumed 12 transpilation backends with native
concurrency primitives (C# Task, JS Promise, Go goroutines). Since then:

- The reference compiler (`src/`) is permanently retired.
- All transpilation backends are frozen. The only live backend is
  **x86-64 bare metal**.
- The self-host compiles itself end-to-end on bare metal (MM4 proven,
  CL 340). The cord is cut.
- Effect handlers (`IrHandle`) have no x86-64 codegen — they fall
  through silently.
- The bare-metal runtime has a process table (16 slots, page tables,
  capability bits) but no scheduler.
- Capability enforcement is live: direction, scope, time-boxing
  (CLs 539-580).
- The `Concurrent` capability is bit 3 in the process table. Granted
  to process 0 but not checked at emit time.

The concurrency design must now target a single environment: a
freestanding ELF binary running on bare x86-64 hardware, with no OS,
no libc, and no thread library.

---

## The Goal

A Codex program that says "do these things concurrently" and the
runtime does them safely. No threads. No locks. No data races.

- The **type system** proves safety (pure or `[Concurrent]`-only
  functions may be forked).
- The **capability system** gates access (`Concurrent` bit 3 in the
  process table).
- The **effect system** tracks concurrency (`EffectfulTy` carries
  `[Concurrent]` through the type checker).
- The **runtime** provides cooperative green threads on bare metal.

---

## What Already Exists

### Language Level

| What | Where | Status |
|------|-------|--------|
| `EffectfulTy` with effect lists | `Types/CodexType.codex:23` | Done |
| `CDX2031` rejects undeclared effects | `Types/TypeChecker.codex` | Done |
| `CDX4001` rejects ungrated capabilities | `Types/TypeChecker.codex:675` | Done |
| Capability directions (R/W) | CLs 539, 573, 575 | Done |
| Capability time-boxing (`with-timeout`) | CLs 579, 580 | Done |
| `IrFork(body, type, span)` IR node | `IR/IRChapter.codex:36` | Done |
| `IrAwait(task, type, span)` IR node | `IR/IRChapter.codex:37` | Done |
| Lowering: `fork`→`IrFork`, `await`→`IrAwait` | `IR/Lowering.codex:183-184` | Done |
| Lambda lifting handles both | `IR/LambdaLifting.codex:158-163` | Done |
| `fork`, `await`, `par`, `race` as builtins | `Semantics/NameResolver.codex:93` | Done |
| `fork : forall a. (() -> a) -> Task a` | `Types/TypeEnv.codex:136` | Done |
| `await : forall a. Task a -> a` | `Types/TypeEnv.codex:137` | Done |

### Emitters

| Backend | IrFork | IrAwait | Status |
|---------|--------|---------|--------|
| Codex text | `fork (...)` | `await (...)` | Correct (pingpong-safe) |
| C# | `Task.Run(() => ...)` | `(...).Result` | Dead code (REF retired) |
| x86-64 | Falls through to `otherwise` → 0 | Same | **Broken** |

### Bare-Metal Runtime

| What | Where | Status |
|------|-------|--------|
| PIT timer interrupt at ~18 Hz | `X86_64Boot.codex` | Done |
| Tick counter at 0x7000 | `X86_64Boot.codex:49` | Done |
| Process table: 16 slots × 256 bytes at 0x5000 | `X86_64Boot.codex:490` | Done |
| Per-process capability bits (64-bit mask) | `X86_64Boot.codex:553` | Done |
| `concurrent` capability = bit 3 | `X86_64Boot.codex:556` | Done |
| SYSCALL/SYSRET with capability check | `X86_64Boot.codex:623` | Done |
| R10 bump allocator (1 GB heap) | `X86_64Builtins.codex:581` | Done |
| `heap-save` / `heap-restore` | `X86_64Builtins.codex:581-607` | Done |
| Deck allocator (secondary stack) | `X86_64Builtins.codex:609-643` | Done |
| IDT with 256 vectors | `X86_64Boot.codex:388` | Done |
| Watchdog (stall detection) | `X86_64Boot.codex:220` | Done |
| Capability expiry check per syscall | `X86_64Boot.codex:660` | Done |
| Stack overflow detection per function | `X86_64.codex:22-36` | Done |
| Serial I/O (COM1 data, COM2 control) | `X86_64IO.codex` | Done |

### Foreword

`foreword/Concurrent.codex`: Sequential stubs. `seq-fork`, `seq-await`,
`par-map`, `par-reduce`, `par-filter`, `par-zip-with`, `race`. All
single-threaded. Correct API shape, no actual parallelism.

### Sample

`samples/MM4-Deferred/fork-basic.codex`: Minimal test (`fork compute`,
`await t`, `r`). Skipped — "NOT IMPLEMENTED on bare metal."

---

## Design

### The Effect (unchanged from original)

```codex
effect Concurrent where
  fork  : (() -> a) -> Task a
  await : Task a -> a
```

`Task` is opaque. `fork` takes a thunk, returns a handle. `await`
blocks until the result is ready. The handler scope joins all tasks
before returning — no orphans, no fire-and-forget.

### The Primitives (unchanged)

```codex
par : (a -> b) -> List a -> [Concurrent] List b
par (f) (xs) =
  let tasks = map (\x -> fork (\() -> f x)) xs
  in map await tasks

race : List (() -> a) -> [Concurrent] a
```

Both are expressible in terms of `fork` and `await`.

### Safety Guarantee (unchanged)

A function passed to `fork` must be pure or have only `Concurrent`
effects. The type system enforces this via `EffectfulTy`. The
capability system gates `Concurrent` at the process level (bit 3).

```codex
opening : [Concurrent] Integer = act
  t <- fork compute
  r <- await t
  r
end
```

The `[Concurrent]` annotation is checked against the capability grant.
If process 0 doesn't have bit 3 set, `CDX4001` fires at compile time.

### Task Representation

On x86-64 bare metal, a `Task a` is a heap-allocated 16-byte record:

```
offset 0: state  (8 bytes)  0 = pending, 1 = complete
offset 8: value  (8 bytes)  result (valid when state = 1)
```

Phase 1 (sequential): state is always 1 — immediate evaluation.
Phase 2 (green threads): state transitions 0 → 1 when the task's
green thread completes. `await` on a pending task yields the current
thread.

---

## Implementation Plan

### Phase 1: Sequential Fork/Await on x86-64

**Goal**: Get `fork-basic` green. Establish the Task ABI. Pass
pingpong and sweep.

**What to build**:

In `Codex.Codex/Emit/X86_64.codex`, add two cases to
`emit-expr-dispatch`:

**`emit-fork`**: The body of `IrFork` is a thunk `(() -> a)`. Emit an
apply with a unit argument (0) to evaluate it. Allocate 16 bytes on
the heap. Store 1 (complete) at offset 0, store the result at
offset 8. Return a pointer to the Task in the result register.

```
; evaluate thunk: apply body to ()
; result in <reg>
; allocate Task on heap
mov  [R10+0], 1         ; state = complete
mov  [R10+8], <reg>     ; value = result
lea  <out>, [R10]       ; pointer to Task
add  R10, 16            ; advance heap
```

**`emit-await`**: Emit the task expression (produces a Task pointer).
Load the value at offset 8. Return it.

```
; emit task expr -> pointer in <reg>
mov  <out>, [<reg>+8]   ; load value
```

Phase 1 ignores the state field — sequential evaluation means every
Task is complete at construction time.

**Sample changes**:
- Move `samples/MM4-Deferred/fork-basic.codex` to `samples/`
- Create `samples/fork-basic.expected` with the expected output
- Delete `samples/MM4-Deferred/fork-basic.skip`

**Complexity**: ~30 lines of emit code. O(1) per fork/await. 16 bytes
of heap per fork. No change to Codex text emitter (already correct).

**Pingpong safety**: The selfhost compiler source does not use
`fork`/`await`. The new emit code compiles correctly but is never
reached during self-compilation, so stage 1 = stage 2 holds. The
seed must be rebuilt to include the new emit logic.

**Gate**: Rebuild seed, full sweep green, pingpong byte-identical.

### Phase 2: Cooperative Green Threads

**Goal**: Real concurrency on bare metal. Multiple tasks make progress.

**Prerequisites**: Phase 1 (establishes ABI).

**What to build**:

**Task descriptor** (heap-allocated, 64 bytes):

```
offset  0: state       (8B)  0=ready, 1=running, 2=blocked, 3=complete
offset  8: stack-ptr   (8B)  saved RSP
offset 16: heap-snap   (8B)  saved R10 (per-task heap region)
offset 24: cap-bits    (8B)  inherited from parent
offset 32: result-ptr  (8B)  address to write result when complete
offset 40: parent-ptr  (8B)  parent task (for structured join)
offset 48: next-ptr    (8B)  linked list pointer (ready queue)
offset 56: deck-snap   (8B)  saved deck-pos
```

**Per-task stack**: Allocated from the heap at fork time. 64 KB default.
Stack overflow detection adapts: each task checks RSP against its own
stack base, not the global minimum.

**Memory model**: At `fork` time, the parent allocates a heap arena for
the child (a contiguous region starting at the current R10). The child
bumps R10 within its arena. On context switch, R10 is saved/restored
per task. On join, the parent reclaims the child's arena after copying
the result. This is the same transactional model as `heap-save`/
`heap-restore` — each task is a "phase" from the allocator's
perspective.

**Scheduler** (round-robin, ~100 lines of emit helpers):
- Ready queue: singly-linked list of task descriptors via `next-ptr`.
- `fork`: allocate descriptor + stack, set entry point to thunk body,
  set state=ready, push to ready queue. Return Task handle (pointer to
  descriptor's result slot).
- `await`: if target task state=complete, load result, return. Otherwise
  set current task state=blocked (waiting on target), save context,
  switch to next ready task.
- `yield`: save context, rotate to next ready task (explicit yield point).
- Timer tick: set a "yield-requested" flag in kernel state. The next
  function prologue checks the flag and yields if set. This gives
  cooperative preemption without interrupt-in-the-middle complexity.
  Cost: ~3 instructions per function call when flag is clear.
- Join: when a scope with forked children exits, block until all child
  tasks are complete. Walk `parent-ptr` links.

**Context switch** (~40 lines of assembly):
- Save: push callee-saved registers (RBX, RBP, R12-R15), store RSP
  and R10 to current task descriptor.
- Restore: load RSP and R10 from next task descriptor, pop callee-saved
  registers, ret into the resumed task.
- No page-table switch — all tasks share the address space. This is
  in-process concurrency, not OS process isolation.

**Structured scoping**: A forked task's `parent-ptr` points to the
task that forked it. When the parent's handler scope exits, the
scheduler refuses to return until all children with that parent are
complete. Orphan tasks are structurally impossible.

**Complexity**: ~300 lines of emit code. Per-fork overhead: 64-byte
descriptor + 64 KB stack. Context switch: ~20 instructions. Timer
check: ~3 instructions per function call.

**Gate**: `par-map` sample green, nested `par` sample green, full
sweep, pingpong byte-identical.

### Phase 3: Effect Handler Codegen on x86-64

**Goal**: Make `IrHandle` emit real code. Enables handler-based
strategy selection — the programmer writes `with Concurrent` and the
handler determines sequential vs. parallel.

**Prerequisites**: Phase 1 (ABI established). Independent of Phase 2.

**Approach**: Inline-only (same as IL emitter Phase 2, documented in
`docs/Designs/Backends/IL-EFFECT-HANDLERS.md`). When the emitter
encounters `IrHandle(eff, body, clauses, ty, sp)`:

1. Emit each handler clause as a local callable (address on the stack).
2. Emit the body. When an effect operation name matches an active
   handler clause, emit a call to the clause instead of the normal
   name resolution.
3. One-shot `resume`: the resume parameter is a local holding the
   value to return. `resume x` stores `x` and returns from the clause.

**Limitations** (acceptable, same as IL):
- Named computations with parameters are not inlined (the handler
  context does not cross function boundaries).
- Multi-shot continuations are not supported (no CPS).
- Higher-order effect passing is not supported.

These limitations are acceptable because the primary use case —
`with Concurrent { fork ... await ... }` — uses inline do-blocks.

**Complexity**: ~200 lines. No new runtime primitives.

**Gate**: Handler samples green (new samples needed), full sweep,
pingpong byte-identical.

### Phase 4: OS-Level Processes

**Not in scope for this document.** The kernel scheduler, process
isolation, and IPC are a separate design (`docs/Designs/Codex.OS/
Scheduler.md`, not yet written). Phase 4 uses the process table
infrastructure that already exists (16 slots, page tables, CR3
field, capability bits) and adds:

- Preemptive scheduling via timer interrupt + full context save
- Page-table switch per process (CR3 reload)
- IPC via capability-gated message passing
- Process lifecycle (spawn, exit, signal)

In-process green threads (Phase 2) inform but do not determine the
OS scheduler design. Green threads are in-process, same address space,
type-safe. OS processes are hardware-isolated, capability-gated,
mutually untrusted.

---

## Known Limitations — Revisit

### Fork pool is a bump allocator with no reclaim

The fork pool (`fork-pool-cursor-addr`, 4 MB reservation) is a one-way
bump allocator. Completed tasks' stacks are never returned to the pool.
For long-running programs with many sequential forks, this leaks. For
the current use case (batch parallelism like `par-map` / `par-reduce`),
it's fine. Reclaim requires either a free-list, a generation-based
reset (strike the pool after a structured join), or per-scope arenas.

---

## What NOT to Build

- **Channels**: Communication is via return values (`fork` -> `await`),
  not Go-style channels. If needed later, channels are a separate
  effect.
- **Mutexes / locks**: Linear types prevent shared mutable state. The
  `SharedState` effect (if ever needed) uses atomic refs, not locks.
  Deferred.
- **Async/await keywords**: Codex uses effects, not colored functions.
  There is no `async` keyword. Any pure function can be forked.
- **Work-stealing**: Round-robin until profiling proves it's the
  bottleneck. Work-stealing adds complexity (per-core deques, stealing
  protocol, cache-line contention) for a benefit that only shows at
  high core counts under unbalanced workloads.
- **Multi-shot continuations**: Require CPS transform. Not needed for
  fork/await. Deferred indefinitely.
- **Transpilation backend mappings**: C# Task, JS Promise, Rust Tokio,
  Go goroutines — all dead code. If transpilation backends revive,
  they get their own design.

---

## Testing Strategy

| Phase | Test | Gate |
|-------|------|------|
| 1 | `fork-basic`: fork a thunk, await result, print it | Sweep green |
| 1 | Pingpong byte-identical (selfhost doesn't use fork) | BS2 green |
| 1 | `fork-nested`: fork inside fork, both awaited | Sweep green |
| 2 | `par-map`: parallel map over a list, verify result order | Sweep green |
| 2 | `par-nested`: `par (\xs -> par f xs) [[1,2],[3,4]]` | Sweep green |
| 2 | `fork-many`: fork 100 tasks, await all, verify results | Sweep green |
| 2 | Structured join: parent waits for all children | Sweep green |
| 2 | Timer yield: long-running forked task doesn't starve | Manual verify |
| 3 | `handle-concurrent`: handler determines sequential | Sweep green |
| 3 | Handler nesting: `with A { with B { ... } }` | Sweep green |

---

## Sequencing

| Phase | What | Effort | Blocks On |
|-------|------|--------|-----------|
| 1 | Sequential fork/await (x86-64 emit) | Small (~30 lines) | Nothing |
| 2 | Cooperative green threads | Large (~300 lines) | Phase 1 |
| 3 | Effect handler codegen (x86-64) | Medium (~200 lines) | Phase 1 |
| 4 | OS-level processes | Large (separate doc) | Phase 2 |

Phases 2 and 3 are independent of each other — both depend on Phase 1
but not on each other. Phase 3 lets the programmer choose a handler;
Phase 2 lets the runtime actually schedule. They compose when both land.

Phase 1 is ready to implement now.

---

## Relationship to Other Work

| Area | How It Connects |
|------|----------------|
| Phase discipline (PHASE-ARCHITECTURE.md) | Green threads need per-task heap regions. The deck/bivy/strike model provides this — each forked task gets its own build/strike cycle. |
| Capability refinement (CAPABILITY-REFINEMENT.md) | `Concurrent` is bit 3 in the process capability mask. Direction (R/W) and scope don't apply — `Concurrent` is a binary grant. Time-boxing applies: `with-timeout 10 [Concurrent]` limits fork lifetime. |
| OS scheduler (needs design doc) | Phase 4. In-process green threads inform but don't constrain the OS scheduler. |
| Agent protocol (TrustAndRuntime.md) | Agents are OS-level processes that communicate via the 7-message protocol. In-process concurrency is below this layer — an agent's internal parallelism is invisible to other agents. |
| Compiler self-use | The selfhost compiler is the first candidate for `par`-based parallelism: parallel file parsing, parallel type checking of independent chapters. This motivates Phase 2. |

---

## What Was Removed From the Original Design

| Original Section | Why Removed |
|-----------------|-------------|
| Phase 4: Backend-Specific Emission (C#/JS/Rust/Go/Python) | All transpilation backends are frozen. |
| "12 backends" sizing assumption | One backend: x86-64 bare metal. |
| "V5+ feature" timeline | No dates. Phase 1 is ready now. |
| `run-sequential` / `run-parallel` / `run-workers` handler variants | Handler-based strategy selection requires Phase 3 (effect handler codegen). Until then, the emitter hardcodes the strategy. |
| SharedState effect | Not needed for fork/await. Deferred. |
| Implicit parallelism for pure `map`/`fold` | Explicit `par` first. |
