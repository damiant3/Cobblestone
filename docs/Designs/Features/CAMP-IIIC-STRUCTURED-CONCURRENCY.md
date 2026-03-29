# Camp III-C — Structured Concurrency

**Date**: 2026-03-24
**Status**: Design
**Depends on**: Effect system (done), Capability system (done), Native backends (done)
**Prior art**: `docs/Designs/STDLIB-AND-CONCURRENCY.md` (the "what" — this doc is the "how")

---

## The Goal

A Codex program that says "do these things in parallel" and the runtime
does them in parallel. No threads. No locks. No data races. The type
system proves safety. The effect system tracks concurrency. The handler
chooses the strategy.

---

## What Already Exists

1. **Effect system**: Functions declare effects in their types. `[Console]`,
   `[FileSystem]`, etc. Pure functions have no effects.
2. **Effect handlers**: `with` handlers intercept effects and provide
   implementations. Already in the IR and all backends.
3. **Linear types**: Values consumed exactly once. Prevents aliasing.
4. **Capability enforcement**: `CDX4001` rejects code that uses effects
   without capability grants.

What's missing: the `Concurrent` effect, the `par`/`race` primitives,
and the runtime scheduler.

---

## Design

### The Effect

```codex
effect Concurrent where
  fork  : (() -> a) -> Task a
  await : Task a -> a
```

`fork` takes a thunk (a zero-argument function) and returns a `Task`
handle. `await` blocks until the task completes and returns its value.

`Task` is an opaque type — the programmer cannot inspect it, cancel it,
or share it across scopes. It exists only to connect `fork` to `await`.

### The Primitives

```codex
par : (a -> b) -> List a -> [Concurrent] List b
par (f) (xs) =
  let tasks = map (\x -> fork (\() -> f x)) xs
  in map await tasks

race : List (() -> a) -> [Concurrent] a
race (thunks) =
  let tasks = map (\t -> fork t) thunks
  in await-first tasks
```

`par` is parallel map. `race` is first-to-finish. Both are expressible
in terms of `fork` and `await`. The handler determines whether `fork`
creates OS threads, green threads, or runs sequentially.

### The Handlers

```codex
-- Sequential (testing, debugging — no real parallelism)
run-sequential : (() -> [Concurrent] a) -> a

-- Parallel with work-stealing (production)
run-parallel : (() -> [Concurrent] a) -> a

-- Fixed worker count
run-workers : Integer -> (() -> [Concurrent] a) -> a
```

The handler is the ONLY place that knows about threads. The programmer
writes `par f xs` and the handler decides whether that means "8 OS
threads with work-stealing" or "sequential loop for debugging."

### Safety Guarantee

A function passed to `fork` must be **pure or have only `Concurrent`
effects**. The type system enforces this:

```codex
fork : (() -> [Concurrent] a) -> [Concurrent] Task a
```

A function with `[FileSystem]` cannot be forked — it would need the
capability grant, and the handler doesn't provide it. To do parallel I/O,
you use a different handler that grants both:

```codex
run-parallel-io : (() -> [Concurrent, FileSystem] a) -> [FileSystem] a
```

This makes the capability grant explicit: "this parallel scope can do I/O."

---

## Implementation Plan

### Phase 1: IR Nodes (Lowering.cs)

Add two IR nodes:

```csharp
public sealed record IRFork(IRExpr Body, CodexType ResultType) : IRExpr(TaskType(ResultType));
public sealed record IRAwait(IRExpr Task, CodexType ResultType) : IRExpr(ResultType);
```

The lowering pass converts `fork` and `await` effect operations into
these IR nodes. The handler wrapping determines the runtime strategy.

### Phase 2: Sequential Handler (all backends)

The simplest correct implementation: `fork` evaluates the thunk immediately
and stores the result. `await` returns the stored result. No parallelism.
This gets the semantics right and all tests passing.

- `EmitFork`: evaluate body, wrap result in a Task struct `[value: 8B]`
- `EmitAwait`: unwrap Task, return value
- ~20 lines per backend

### Phase 3: Native Thread Pool (RISC-V, x86-64)

For native backends, implement a minimal work-stealing scheduler:

- Thread pool initialized at program start (N = number of cores)
- `fork` pushes a thunk + continuation onto a work queue
- Worker threads pop from the queue and execute
- `await` spins or blocks until the Task's result slot is filled
- Structured: when the handler scope exits, all tasks are joined

This requires:
- `clone` syscall (Linux) or `pthread_create` (with libc)
- Atomic compare-and-swap for the work queue
- A futex or spin-lock for `await`

~500 lines per native backend. The hardest part is the atomic operations
in pure machine code (no libc).

### Phase 4: Backend-Specific Emission (transpilation targets)

For C#, JavaScript, etc., emit to native concurrency primitives:

| Backend | `fork` | `await` |
|---------|--------|---------|
| C# | `Task.Run(() => ...)` | `.Result` or `await` |
| JavaScript | `Promise.resolve().then(() => ...)` | `await` |
| Rust | `tokio::spawn(async { ... })` | `.await` |
| Go | `go func() { ... }()` + channel | `<-ch` |
| Python | `executor.submit(lambda: ...)` | `.result()` |

~50 lines per transpilation backend (wrapping in the target's idiom).

---

## What NOT to Build

- **Channels**: Go-style channels are a communication primitive. Codex
  uses return values, not channels. `fork` returns a result via `await`.
- **Mutexes/locks**: Linear types prevent shared mutable state. The
  `SharedState` effect (if needed) uses atomic refs, not locks.
- **Async/await keywords**: Codex uses effects, not colored functions.
  There is no `async` keyword. Any function can be forked.
- **Cancellation tokens**: Structured concurrency handles this — when
  the scope exits, all tasks complete or are abandoned.

---

## Testing Strategy

1. `par identity [1,2,3,4,5]` = `[1,2,3,4,5]` (sequential handler)
2. `par (\x -> x * 2) [1,2,3]` = `[2,4,6]`
3. `race [\() -> 42, \() -> 99]` = either 42 or 99
4. Effect tracking: `par` requires `[Concurrent]` in the type
5. Capability: `CDX4001` if `Concurrent` not granted
6. Nested `par`: `par (\xs -> par f xs) [[1,2],[3,4]]` works correctly
7. Self-hosted compiler: `par` over source files in the build pipeline

---

## Sequencing

| Phase | What | Effort | Blocks |
|-------|------|--------|--------|
| 1 | IR nodes + lowering | Small | Nothing |
| 2 | Sequential handler | Small | Phase 1 |
| 3 | Native thread pool | Large | Phase 2 |
| 4 | Transpilation targets | Medium | Phase 2 |

Phases 1+2 can ship together. Phase 3 is the real work. Phase 4 is
parallelizable across backends.
