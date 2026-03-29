# Standard Library & Concurrency Design

**Date**: 2026-03-19 (verified via system clock)
**Author**: Claude (Opus 4.6, Linux, claude.ai)
**Status**: Proposal for review

---

## The Question

What should the Codex standard library look like, and how should Codex programs
use modern multi-core hardware?

These two questions are entangled. The standard library defines the vocabulary
that programmers use to express computation, and the concurrency model determines
whether that vocabulary can be parallelized safely. In a language with effect
tracking and pure functions, the answers reinforce each other.

---

## Part One: The Standard Library

### Design Principle: Small Core, Deep Foundations

The standard library should be **small** — but the things it includes should be
**complete within their scope**. This is the OCaml/Haskell model, not the Java/Python
model. The reasoning:

1. **Codex compiles to 12 backends.** Every stdlib module must have a sensible
   implementation on C#, JavaScript, Python, Rust, C++, Go, Java, Ada, Fortran,
   COBOL, IL, and Babbage. A large stdlib creates a 12x maintenance burden. A
   small stdlib with clean abstractions lets each backend map to its native
   equivalents.

2. **Codex has a repository model.** The vision (V1–V4) is that libraries are
   published as facts, searched by type signature, and verified by proof. A bloated
   stdlib competes with this model. The stdlib should provide the vocabulary for
   *writing* libraries, not *replacing* them.

3. **Codex is self-hosting.** The compiler is our first and most demanding user.
   The stdlib must include everything the compiler needs — and nothing the compiler
   doesn't. This is a natural size constraint.

4. **The prelude already exists.** We have 7 modules (821 lines). The question is
   what to add, not what to start.

### What the Compiler Needs (and Therefore What Ships)

The self-hosted compiler uses: text manipulation, list operations, pattern matching
on sum types, record construction, integer arithmetic, and console output. The
built-ins (`text-length`, `char-at`, `substring`, `text-replace`, `list-length`,
`list-at`, `integer-to-text`, `print-line`, etc.) cover this.

What the compiler does NOT need — and therefore what can wait — is: file I/O
beyond simple read/write, networking, date/time, floating-point math, regular
expressions, database access, GUI, and concurrency. These belong in the
repository, not the stdlib.

### The Layers

The stdlib is layered. Each layer depends only on the layers below it. Layers
are opt-in: importing `Maybe` does not pull in `Hamt`.

```
Layer 0: Built-ins (compiler-inlined, always available)
         text-length, char-at, substring, text-replace, list-length,
         list-at, integer-to-text, text-to-integer, char-code,
         code-to-char, print-line, show, open-file, read-all, close-file

Layer 1: Core Types (pure, no effects, no dependencies)
         Maybe, Result, Either, Pair
         — "What every function returns when it might fail or have options"

Layer 2: Collections (pure, depends on Layer 1)
         List, Hamt (persistent map), Set, Queue
         — "How you hold groups of things"

Layer 3: Text (pure, depends on Layers 1-2)
         CCE (character classification/encoding), StringBuilder, TextSearch
         — "How you work with text beyond concatenation"

Layer 4: Effects (effect definitions, depends on Layers 1-2)
         Console, FileSystem, State, Time, Random
         — "How you interact with the world"
```

That's it. Four layers above built-ins. Everything else goes in the repository.

### Why NOT a Big Standard Library

The temptation is to ship HTTP, JSON, CSV, regex, crypto, compression, and
argument parsing. Every modern language does this. Here is why Codex should not:

**Backend explosion.** An HTTP library on C# uses `HttpClient`. On JavaScript it
uses `fetch`. On Rust it uses `reqwest`. On COBOL it uses... nothing. Every
library that touches the platform creates 12 maintenance surfaces. The stdlib
should be the platform-independent core; platform-specific functionality belongs
in backend-specific packages published through the repository.

**Staleness.** Standard libraries calcify. Python's `urllib` vs `requests`.
Java's `java.util.Date` vs `java.time`. Go's `context`. Once something is in
the stdlib, removing it is a decade-long process. The repository model allows
evolution: a better implementation supersedes the old one, and the old one
remains for anyone who depends on it.

**Scope discipline.** The Codex vision includes proof-carrying packages (V4).
A function in the repository can carry a proof that it sorts correctly, terminates,
and runs in O(n log n). A function in the stdlib carries... a comment. The
repository is the right home for verified libraries.

### What Each Layer Provides

**Layer 1: Core Types** (existing, 97 lines total)

```
Maybe a     = Nothing | Just a
Result a    = Ok a | Err Text
Either a b  = Left a | Right b
Pair a b    = record { first : a, second : b }
```

These are complete. No changes needed.

**Layer 2: Collections** (existing: List 100 lines, Hamt 271 lines; add: Set, Queue)

The existing List module provides cons-list operations. The Hamt module provides
a persistent hash-array-mapped trie (the workhorse data structure for functional
languages — O(log32 n) lookup/insert/delete, effectively constant).

To add:

- `Set a` — built on Hamt (keys with unit values). Intersection, union, difference.
- `Queue a` — Okasaki-style two-list queue. O(1) amortized enqueue/dequeue.

Both are pure, ~100 lines each, and needed by the compiler for name resolution
(Set) and work-queue patterns (Queue). These can be written in Codex itself,
compiled through the self-hosted pipeline.

**Layer 3: Text** (existing: CCE 353 lines; add: StringBuilder, TextSearch)

CCE handles character classification (is-letter, is-digit, etc.) and encoding.

To add:

- `StringBuilder` — accumulate text efficiently. The compiler's emitter
  currently builds output via `++` (string concatenation), which is O(n²) in
  the worst case. A rope or buffer abstraction fixes this. ~150 lines.
- `TextSearch` — `contains`, `starts-with`, `ends-with`, `index-of`, `split`.
  These are needed for any real program and are currently missing. ~100 lines.

**Layer 4: Effects** (existing: Console, State, FileSystem built-in; formalize)

The built-in effects are currently hard-coded in the type environment. They should
be formalized as proper effect definitions in `.codex` source:

```codex
effect Console where
  print-line : Text -> Nothing
  read-line  : Text

effect FileSystem where
  open-file  : Text -> FileHandle
  read-all   : FileHandle -> Text
  close-file : FileHandle -> Nothing

effect Time where
  now : Integer

effect Random where
  random-integer : Integer -> Integer -> Integer
```

This is ~50 lines and makes the effect system self-documenting.

### Total Size Estimate

| Layer | Existing | To Add | Total |
|-------|----------|--------|-------|
| Layer 1: Core Types | 97 lines | 0 | 97 |
| Layer 2: Collections | 371 lines | ~200 (Set, Queue) | ~571 |
| Layer 3: Text | 353 lines | ~250 (StringBuilder, TextSearch) | ~603 |
| Layer 4: Effects | 0 (built-in) | ~50 (formalized) | ~50 |
| **Total** | **821** | **~500** | **~1,321** |

The entire standard library fits in ~1,300 lines of Codex. This is intentionally
small. For comparison: Haskell's `base` is ~30,000 lines. Go's stdlib is
~500,000 lines. Python's is over 600,000. We are not in that business.

---

## Part Two: Concurrency and Modern Hardware

### The Honest Assessment

Codex is currently single-threaded at the language level. The reference compiler
uses `Parallel.ForEach` for multi-file front-end parsing and multi-target
emission (visible in `Program.Incremental.cs`), but this is C# infrastructure
code, not Codex language features.

The question is: should Codex programmers think about threads?

**No.** Absolutely not. Not ever.

Thread management is organizational scaffolding — exactly the kind of complexity
the IntelligenceLayer.txt manifesto identifies as dissolving. The insight from
that document applies directly: *"Ask of every convention: Is this here because
the machine needs it, or because the team needed it?"* Manual threading exists
because languages couldn't figure out what was safe to parallelize. Codex can.

### Why Codex Is Uniquely Positioned

Most languages cannot automatically parallelize code because they cannot prove
that two computations don't interfere with each other. A function might read
a global variable. A method might mutate shared state. A closure might capture
a reference to a mutable object. The compiler must assume the worst.

Codex has three properties that change this equation:

1. **Purity by default.** A function with no effect annotation is pure. It
   cannot read or write state, perform I/O, or observe anything about the
   outside world. Pure functions can be evaluated in any order, on any core,
   with zero synchronization.

2. **Effect tracking.** When a function IS effectful, the type system says
   exactly which effects it uses. A function with `[Console]` only does I/O.
   A function with `[State Counter]` only touches one piece of mutable state.
   The runtime can use this information to determine which computations
   conflict and which are independent.

3. **Algebraic effect handlers.** The handler determines the execution
   strategy. The same code can be run single-threaded, multi-threaded, or
   distributed — by changing the handler, not the code. This is the key
   architectural insight.

### The Concurrency Model: Effects, Not Threads

Concurrency in Codex is expressed as an effect:

```codex
effect Async where
  fork   : (() -> [Async, e] a) -> Task a
  await  : Task a -> a
  yield  : Nothing
```

A program that uses `Async` declares that it performs concurrent operations.
The handler determines HOW:

```codex
-- Run with a thread pool (production)
run-parallel : (() -> [Async] a) -> a

-- Run sequentially (testing, debugging)
run-sequential : (() -> [Async] a) -> a

-- Run with a fixed number of workers
run-workers : Integer -> (() -> [Async] a) -> a
```

The programmer writes:

```codex
process-files : List Text -> [Async, FileSystem] List Result
process-files (paths) =
  let tasks = map (\path -> fork (\() -> parse-file path)) paths
  in map await tasks
```

This code says "fork a task for each file, then await all results." It does NOT
say "create 8 threads" or "use a semaphore" or "lock the output list." The
handler decides the execution strategy. In tests, `run-sequential` executes
everything on one thread. In production, `run-parallel` uses a work-stealing
pool. The code is identical.

### Implicit Parallelism for Pure Code

Beyond explicit `Async`, the runtime can parallelize pure code automatically.
Consider:

```codex
map : (a -> b) -> List a -> List b
```

When `f` is pure (no effects), `map f xs` can be executed in parallel with
zero risk. The runtime can:

- Below a threshold (say, 1000 elements): run sequentially (overhead not worth it)
- Above the threshold: split the list, map in parallel, concatenate results

This is the same optimization that Java's parallel streams, .NET's PLINQ, and
Haskell's `par`/`seq` provide — but in Codex it requires NO annotation from the
programmer. The compiler KNOWS `f` is pure because the type system PROVES it.

The emitter can target:

| Backend | Parallel strategy |
|---------|------------------|
| C# | `Parallel.ForEach`, `Task.WhenAll`, PLINQ |
| JavaScript | `Promise.all`, Web Workers |
| Rust | Rayon `par_iter` |
| Go | Goroutines |
| Python | `multiprocessing.Pool` (GIL workaround) |
| Java | `ForkJoinPool`, parallel streams |

Each backend uses its native parallelism primitive. The Codex source is unchanged.

### Structured Concurrency

Codex follows the structured concurrency model (as in Swift, Kotlin, and Java 21's
virtual threads): every concurrent scope has a clear lifetime, and child tasks
cannot outlive their parent.

```codex
effect Async where
  fork   : (() -> [Async, e] a) -> Task a
  await  : Task a -> a

-- When the handler scope ends, all forked tasks are awaited or cancelled.
-- No orphan tasks. No dangling futures. No fire-and-forget.
```

This is enforced by the handler. The `run-parallel` handler joins all tasks
before returning. If a task fails, all sibling tasks are cancelled and the
error propagates to the parent scope. This is the "nursery" pattern from
Trio (Python) and the task group pattern from Swift.

### What About Shared State?

The `State` effect provides mutable state, but it's scoped to a handler:

```codex
run-state : s -> (() -> [State s, e] a) -> [e] (a, s)
```

State is NOT shared between forked tasks by default. Each task gets its own
state scope. If you need shared state, you use a different effect:

```codex
effect SharedState s where
  read   : s
  write  : s -> Nothing
  modify : (s -> s) -> Nothing
```

The `SharedState` handler uses an atomic reference (lock-free CAS on most
backends). The type system ensures that only code within a `SharedState` handler
can access shared state, and the handler implementation ensures thread safety.

But the design pressure is AGAINST shared state. Codex is purely functional.
The natural pattern is to fork independent computations, await their results,
and combine them. This is the map-reduce pattern, and it needs no shared state.

### Is This Purely a Runtime Concern?

Almost, but not entirely. There are three layers:

**Compile-time (type system):** The effect system partitions code into pure
and effectful. This partition is the foundation — the compiler uses it to
determine what CAN be parallelized.

**IR-level (optimizer):** The IR can represent parallel map, parallel fold,
and structured fork/join as first-class constructs. This lets the optimizer
reason about parallelism without backend-specific knowledge.

**Runtime (backend):** The actual thread pool, work-stealing scheduler, or
async executor is backend-specific. C# uses the ThreadPool and Task system.
JavaScript uses the event loop and Promise machinery. Rust uses Tokio or Rayon.

The programmer touches NONE of these. They write pure functions and use effects.
The compiler decides what's safe. The runtime decides how to schedule it.

### When Does This Ship?

Not now. The concurrency model requires:

1. **Effect handlers that compile correctly** — P1 is resolved, but handlers
   need testing across backends.
2. **The `Async` effect definition** — straightforward to define, ~20 lines.
3. **Backend-specific emission for fork/await** — each of the 12 backends
   needs a mapping. C# and JavaScript are easiest (Task/Promise). COBOL and
   Fortran are hardest (may require a bundled runtime).
4. **The structured concurrency handler** — the `run-parallel` implementation
   that manages task lifetimes. ~200 lines per backend.

This is a V5+ feature (Intelligence layer timeline). For now, the sequential
execution model is correct, the effect system is in place, and the architecture
is ready for parallelism when the time comes.

### The Implicit Contract

The most important thing is what we DON'T do: we don't add `async`/`await`
keywords. We don't add a `Thread` type. We don't add `synchronized` blocks or
`Mutex` or `Arc<Mutex<T>>`. We don't add `channel` or `select` or `go`.

These are all mechanisms. Codex expresses intent. The intent is: "these
computations are independent" (purity) or "this computation forks work"
(`Async` effect). The mechanism is chosen by the handler and the backend.

A programmer who has spent years managing threads, debugging race conditions,
and reasoning about memory ordering should be able to write Codex and NEVER
think about any of that. Not because the problems don't exist, but because
the type system makes them structurally impossible. Pure functions can't race.
Scoped effects can't leak. Structured concurrency can't orphan. The problems
are solved by the language design, not by the programmer's vigilance.

---

## Summary of Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Stdlib size | Small (~1,300 lines) | 12 backends, repository model, scope discipline |
| Stdlib structure | 4 layers, opt-in imports | No dependency bloat, clean separation |
| What ships now | Set, Queue, StringBuilder, TextSearch, formalized effects | Compiler needs, real-program support |
| What doesn't ship | HTTP, JSON, regex, crypto, GUI | Repository packages, not stdlib |
| Concurrency model | Effect-based, not thread-based | Purity enables implicit parallelism |
| Programmer-facing API | `Async` effect + `fork`/`await` | No threads, no locks, no async/await keywords |
| Implicit parallelism | Pure `map`/`fold` auto-parallelized | Type system proves safety |
| Shared state | Discouraged; `SharedState` effect when needed | Atomic refs, handler-scoped |
| Timeline for concurrency | V5+ (after stdlib and FFI) | Needs handlers across backends |
| Thread management | Never exposed to programmer | Effects + handlers + runtime |

---

## Appendix: What to Build Next (R2 Completion)

In priority order:

1. `Set.codex` — built on Hamt, ~100 lines. Needed for name resolution.
2. `Queue.codex` — Okasaki two-list queue, ~80 lines. Needed for BFS patterns.
3. `TextSearch.codex` — contains, starts-with, ends-with, split, ~100 lines.
4. `StringBuilder.codex` — efficient text accumulation, ~150 lines.
5. Formalize effect definitions in `.codex` source, ~50 lines.

Total: ~480 lines of Codex to complete R2. All pure except the effect
definitions. All writable in the self-hosted pipeline. All testable through
the existing test infrastructure.
