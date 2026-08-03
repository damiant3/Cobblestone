# Standard Library & Concurrency Design

**Date**: 2026-03-19 (verified via system clock)
**Author**: Claude (Opus 4.6, Linux, claude.ai)
**Status**: Partially shipped -- R2 stdlib additions (Set, Queue, TextSearch, StringBuilder) live. Concurrency superseded by CAMP-IIIC; the "12 backends" framing is obsolete.

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

The standard library should be **small** -- but the things it includes should be
**complete within their scope**. This is the OCaml/Haskell model, not the Java/Python
model. The reasoning:

1. **Codex compiles to 12 backends.** Every stdlib module must have a sensible
   implementation on C#, JavaScript, Python, Rust, C++, Go, Java, Ada, Fortran,
   COBOL, IL, and Babbage. A large stdlib creates a 12x maintenance burden. A
   small stdlib with clean abstractions lets each backend map to its native
   equivalents.

2. **Codex has a repository model.** The vision (V1-V4) is that libraries are
   published as facts, searched by type signature, and verified by proof. A bloated
   stdlib competes with this model. The stdlib should provide the vocabulary for
   *writing* libraries, not *replacing* them.

3. **Codex is self-hosting.** The compiler is our first and most demanding user.
   The stdlib must include everything the compiler needs -- and nothing the compiler
   doesn't. This is a natural size constraint.

4. **The prelude already exists.** We have 7 modules (821 lines). The question is
   what to add, not what to start.

### What the Compiler Needs (and Therefore What Ships)

The self-hosted compiler uses: text manipulation, list operations, pattern matching
on sum types, record construction, integer arithmetic, and console output. The
built-ins (`text-length`, `char-at`, `substring`, `text-replace`, `list-length`,
`list-at`, `integer-to-text`, `print-line`, etc.) cover this.

What the compiler does NOT need -- and therefore what can wait -- is: file I/O
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
         -- "What every function returns when it might fail or have options"

Layer 2: Collections (pure, depends on Layer 1)
         List, Hamt (persistent map), Set, Queue
         -- "How you hold groups of things"

Layer 3: Text (pure, depends on Layers 1-2)
         CCE (character classification/encoding), StringBuilder, TextSearch
         -- "How you work with text beyond concatenation"

Layer 4: Effects (effect definitions, depends on Layers 1-2)
         Console, FileSystem, State, Time, Random
         -- "How you interact with the world"
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
languages -- O(log32 n) lookup/insert/delete, effectively constant).

To add:

- `Set a` -- built on Hamt (keys with unit values). Intersection, union, difference.
- `Queue a` -- Okasaki-style two-list queue. O(1) amortized enqueue/dequeue.

Both are pure, ~100 lines each, and needed by the compiler for name resolution
(Set) and work-queue patterns (Queue). These can be written in Codex itself,
compiled through the self-hosted pipeline.

**Layer 3: Text** (existing: CCE 353 lines; add: StringBuilder, TextSearch)

CCE handles character classification (is-letter, is-digit, etc.) and encoding.

To add:

- `StringBuilder` -- accumulate text efficiently. The compiler's emitter
  currently builds output via `&` (string concatenation), which is O(1) amortized
  when the accumulator is at heap top (fast-path in `__str_concat`). ~150 lines.
- `TextSearch` -- `contains`, `starts-with`, `ends-with`, `index-of`, `split`.
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

## Part Two: Concurrency

**Note (2026-05-01):** The concurrency implementation design has been
revised and moved to `CAMP-IIIC-STRUCTURED-CONCURRENCY.md`. This
section preserves the original vision statement. The implementation
plan, phasing, and backend-specific details in the revised doc
supersede what was here.

### The Core Insight

Codex programmers should never think about threads.

Thread management is organizational scaffolding. Manual threading exists
because languages couldn't figure out what was safe to parallelize.
Codex can, because of three properties:

1. **Purity by default.** A function with no effect annotation is pure.
   Pure functions can be evaluated in any order, on any core, with zero
   synchronization.

2. **Effect tracking.** When a function IS effectful, the type system
   says exactly which effects it uses. The runtime can determine which
   computations conflict and which are independent.

3. **Algebraic effect handlers.** The handler determines the execution
   strategy. The same code can be run single-threaded or multi-threaded
   by changing the handler, not the code.

### The Model: Effects, Not Threads

Concurrency in Codex is the `Concurrent` effect:

```codex
effect Concurrent where
  fork  : (() -> a) -> Task a
  await : Task a -> a
```

`Task` is opaque. `fork` takes a thunk. `await` blocks until the result
is ready. Structured scoping ensures child tasks cannot outlive their
parent -- no orphans, no fire-and-forget.

A function passed to `fork` must be pure or have only `Concurrent`
effects. The type system enforces this. The capability system gates
`Concurrent` at the process level.

### What We Don't Build

No `async`/`await` keywords. No `Thread` type. No `synchronized` blocks.
No `Mutex`. No `channel`. No `select`. No `go`.

These are all mechanisms. Codex expresses intent: "these computations are
independent" (purity) or "this computation forks work" (`Concurrent`
effect). The mechanism is chosen by the runtime.

### Implementation

See `CAMP-IIIC-STRUCTURED-CONCURRENCY.md` for the four-phase
implementation plan targeting x86-64 bare metal:

1. Sequential fork/await (ABI establishment)
2. Cooperative green threads (real concurrency)
3. Effect handler codegen (handler-based strategy selection)
4. OS-level processes (separate design doc)

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

1. `Set.codex` -- built on Hamt, ~100 lines. Needed for name resolution.
2. `Queue.codex` -- Okasaki two-list queue, ~80 lines. Needed for BFS patterns.
3. `TextSearch.codex` -- contains, starts-with, ends-with, split, ~100 lines.
4. `StringBuilder.codex` -- efficient text accumulation, ~150 lines.
5. Formalize effect definitions in `.codex` source, ~50 lines.

Total: ~480 lines of Codex to complete R2. All pure except the effect
definitions. All writable in the self-hosted pipeline. All testable through
the existing test infrastructure.
