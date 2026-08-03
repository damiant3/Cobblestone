# Hale: A Sibling Project, and the Five Things Worth Taking

*Survey written 2026-07-25 by reek, at Damian's direction, after reading
`github.com/hale-lang/hale`. A position doc, not a plan. Nothing here is
committed work.*

## Why this project and not another

There are a great many new languages and almost none of them are evidence
about Codex. Hale is, because it arrives at four of our conclusions from a
different starting point and one different premise, which makes the places it
agrees with us informative and the places it goes further worth reading.

What it is: a concurrent systems language, Apache-2.0, self-hosted compiler
written in Hale targeting LLVM 18, native on Linux and Apple Silicon plus
WebAssembly from the same source. No garbage collector. No borrow checker. No
exceptions. No `async`/`await`. Region-based memory with one arena per
component. A runtime whose concurrent primitives are model-checked
exhaustively in CI.

The agreements, stated plainly because they are the cheapest evidence this
survey contains: **no GC and no tracing**, **region-based ownership with
wholesale free**, **failures declared in the type signature rather than thrown**,
and **a self-hosted compiler written in its own language**. Four independent
arrivals at four of our load-bearing decisions. That is not proof any of them
is right, and it is the closest thing to an outside opinion we have.

The difference in premise: their unit of decomposition is the **locus**, a
concurrent component that is simultaneously their module, class, service and
fleet element, and their programs are descriptions of message topology. Ours
is the **chapter**, and our programs are literature. Both projects then claim
the same prize, that the description *is* the program. Two different bets on
what a description is.

## What this survey is evidence of, and what it is not

**Everything below about Hale comes from their README, their mdBook and their
`AGENTS.md`. I have not read their compiler, run their toolchain, or verified
one claim.** The project whose post-mortem sits in
`docs/PM/Active/Stories/BrotliBeatsOpus.md` is the project that learned what a
well-written claim is worth, and this document is a reading of well-written
claims. Where a number appears below with "measured", it is a measurement of
*our* tree taken while writing this, and the command is given.

No local copy was archived. Under the convention in
`docs/Reference/README.md` that makes every citation here a hyperlink waiting
to break. Their licence is Apache-2.0, so archiving is permitted and cheap if
that is wanted; it was not done because it was not asked for.

## 1. Negative controls on the concurrency models

Their runtime is C. Its lock-free hashmap protocol, mailbox monitor hand-off,
bus queue conditionals and arena subregion locks are transcribed into models
and checked exhaustively under every legal thread interleaving with **GenMC**,
on every CI run. **Each model ships a paired negative control with the
synchronisation deliberately removed, so the check is proven able to fail.**

That last sentence is our own doctrine, arrived at independently, pointed
somewhere we have never pointed it. `TheSilentKeyboard` puts it as
"validation that cannot fail is not validation; it is reassurance".
`ExaminersAssay` puts it as "a function that always answers the same thing
looks exactly like one that works", and says twice that an unfired guard is
worth exactly what no guard is worth. Blu's proposed lesson index calls it
`L-FALSIF`.

What we have: six atomics builtins lowering to `LOCK CMPXCHG`, `LOCK XADD`,
`LOCK XCHG` and `MFENCE`; a process table claimed across cores by CMPXCHG on
the state word; a channel table; per-core LAPIC timers preempting into a
shared scheduling path.

What we can prove about any of it: that an application processor executed
guest code (cell 36200), that one was preempted (cell 36216), that slot 0
never migrates, that an idle core halts and that affinity is honoured. Those
are good tests and they were hard won. **Not one of them can see a lost
update.** Every SMP test asserts that something happened, and a race is a
thing that happens correctly almost always. Our instrument for concurrency is
existence proofs, and the failure mode is a wrong answer under an interleaving
that a single run does not schedule.

This is the largest by-construction hole I can identify in our stack that has
a known, off-the-shelf instrument sitting next to it. It costs no seed
rebuild, touches no codegen, and the negative control per model is the entire
design.

The honest objection: GenMC is a tool we did not build, on a substrate
(C models of our primitives) that is not the thing that ships, and Rule 6 and
the no-borrowed-substrate doctrine both have a claim here. The counter is
`TheSilentKeyboard` recommendation R-6, which settled this exact question in
the general case: *the doctrine governs what ships, not what may be observed
during development. Telescopes are not contamination.*

## 2. Topology checked as a typed graph at compile time

Their compiler walks the message-bus graph at build time and reports, as
errors: intra-locus re-entrant self-publish (unbounded recursion) and subject
type mismatches. As warnings: orphan topics, cross-locus cycles, unbounded
backpressure.

We have nothing of this shape, and the gap is precise: our effect rows type
each *edge* of a communication and nothing types the *shape* of the graph. A
`process-spawn` tree, the 16-slot channel table, the handler dispatch table at
cell 28968 -- there is no phase that asks whether the resulting topology is
well formed. An orphaned channel, a cycle between two spawned processes, or a
producer that can outrun its consumer are all legal today and all diagnosable
statically.

It is also the cheapest kind of check we know how to write: a new pass over
data the frontend already has, no runtime cost, no seed implication beyond the
pass itself, and it fails a build rather than a boot.

## 3. `@budget(alloc_per_call = N)`, and why `punctual` should carry a number

Their escape and loop dataflow flags, by default, allocations that escape a
per-message handler or an unbounded loop and accumulate until the component
dissolves. `@budget(alloc_per_call = N)` turns the advisory into a hard error,
and **`N = 0` certifies a zero-allocation handler**. Resource budgets do the
same for file descriptors, OS threads and pool sizes against ceilings checked
in CI.

We have the identical problem with more teeth, because we have no collector at
all, and we have most of the machinery already:

- `punctual` rejects heap allocation outright (CDX6002).
- The emitter counts instructions per punctual function and reports CDX6010,
  with an optional budget warned at CDX6011. **So we already accept a number
  after a keyword and check it.**
- `heap hwm` is measured, by hand, per compile.

**`punctual` is all-or-nothing where theirs is a number, and the all-or-nothing
form is why almost nothing in the tree can wear it.** A function that allocates
once is not punctual and gets no accounting at all, which is the common case
and the one where a budget would pay. `punctual 128 f` already parses; an
allocation budget on an ordinary function is the same grammar and the same
emit-time counting against a different quantity.

The connection worth making: CurrentPlan gap 8 is precise escape roots for
CHECK and LOWER. **Escape roots are exactly what an allocation budget needs,
and an allocation budget is exactly what would make them checkable rather than
re-measured by hand each time.** Today `ArchitectsSketchbook`'s deck table
carries a warning that it is already going stale and must be re-measured
before quoting. A budget is the instrument that ends that sentence.

## 4. Their arena model is ours, with two properties we lack

Per-component arena; child regions are subregions of their parent, mirroring
the component tree; a component's dissolution frees its whole subtree
wholesale, with no tracing and no aliasing analysis. Sound because of two
stated invariants: **no pointer crosses sideways**, and **a payload crossing a
boundary is copied into the receiver's arena**.

Read our own account beside it. Phase decks with `phase-compact` reclaiming
bivy scratch; slot-indexed spawn regions where the process table *is* the
allocator; the reservation-copy pattern where survivors are deep-copied into a
reservation below the scratch. Same destination, different road.

Two differences, both in their favour, and both about form rather than
substance:

**Their soundness argument is an invariant the compiler enforces; ours is a
discipline the documentation describes.** `ArchitectsSketchbook` explains at
length which guards read R10 and which read the deck cell, why CHECK is the
one you cannot infer from the phase's shape, and how to tell a guard that
holds from a guard that does nothing. Every word of that is true and hard won,
and all of it is prose about invariants rather than invariants. Their
equivalent is two sentences because their compiler is holding the other end.

**Their declared sizes are hints.** An arena that out-allocates its budget
adds another chunk. Ours raises CDX9002 when a floor is exceeded, and, worse,
under-reservation does not raise it at all -- the parse keep-deck copy writes
past the floor into the scratch it is still reading and the compile dies in a
`#GP` with no diagnostic. For the same class of programmer mistake their
behaviour is strictly better than ours. We have the demand-paged address space
that would make growing-a-chunk nearly free, and we spend it on generous flat
floors plus a hard stop.

## 5. Removing choices, and citable rule ids

Their `AGENTS.md` removes parametric generics (`@form(vec)` rather than
`Vec<T>`), visibility modifiers, closures as values, and function colouring,
and justifies each in part on the grounds that a narrower solution space is
harder for a model to get wrong. It publishes seven canonical program shapes
with the instruction that every well-formed program matches one, and a formal
model whose nodes, hyperedges and invariants carry ids you are told to quote
(`H8`, `I4`) rather than paraphrase.

Two of those transfer and one does not.

**Citable ids transfer, and we already do it in one place.** Our diagnostics
are numbered CDX1xxx through CDX9xxx, `CdxCodes.codex` is the registry, and
`check-cdx-registry.ps1` fails the build on a code raised but not registered
or registered but never raised. That is precisely the machinery Hale applies
to its *design rules*, and we apply to nothing but diagnostics. Our doctrine
lives in eleven root docs, twenty stories and a `CLAUDE.md`, addressed by
prose. Blu's proposed `docs/PM/Active/Stories/LESSONS.md` is the same idea and
should be read as such; its argument that `CLAUDE.md` is a test suite with no
runner is the sharpest sentence written about this repository in some time.

**Formatter as enforcement transfers.** Theirs is zero-config and canonical,
so a diff touches only lines that changed. Our text emitter is already a
canonicaliser proven idempotent by the gate's text fixed point, and we do not
use it as one.

**Narrowing the language to suit a model does not transfer, and should not.**
Our premise is that prose is load-bearing and the notation serves the
intention. Deleting expressive power because a generator finds it hard is the
notation serving the generator, which is the failure `VisionAndVirtues`
forbids in its first paragraph. Their constraints are defensible on their own
premise; the argument from model convenience is one we should decline even
where we happen to want the same restriction.

## What not to take

**`reperspective`, their hot swap.** A handle is atomically redirected to a
new implementation and every caller sees V2 on its next call, with no restart
and no state loss. It is a genuinely attractive operational property and it is
in direct tension with content addressing: our whole position is that a name
resolves through a digest to exactly one text, checked against a signature and
a trust floor before it is admitted. An atomic redirect is a mutable binding
wearing an operations costume.

**Their benchmark grid, as evidence.** They publish 2.4x faster message
dispatch, 2.6x JSON parsing and 4.8x vector push against Go, alongside 2.5x
slower function calls and 8.2x slower component instantiation, and they explain
the losses by design rather than hiding them. Publishing the losses is
creditable. It is still self-published, single-competitor, and against a
garbage-collected language, and `ClaimsCalibration.md` exists for exactly this
kind of number.

## The order I would do these in

1. **The concurrency models with negative controls.** Off-the-shelf
   instrument, small surface, no seed, no codegen, and it is pointed at the
   part of our stack with the weakest evidence.
2. **Bus and spawn topology as a compile-time graph check.** A new pass over
   data we already have; fails a build rather than a boot.
3. **An allocation budget, as the instrument for CurrentPlan gap 8** rather
   than as a feature of its own. Do not build it before the escape roots; it
   is what makes them worth having.

Items 4 and 5 are readings rather than work items. The arena comparison argues
for making one existing invariant machine-checked instead of documented, and
for reconsidering the hard stop on a deck floor now that the address space is
demand paged. The rule-id comparison argues for blu's `LESSONS.md`, which is
already proposed and does not need this doc's support.

## The caution that governs all of it

Every claim in sections 1 through 5 about Hale is unverified prose from an
author with an interest in it reading well. The first move on any of these is
the one `BrotliBeatsOpus` ends with: **build the instrument, then find out.**
Reading a better-written version of your own doctrine is pleasant and is not
evidence. The reason item 1 is first is not that their document is convincing.
It is that we cannot currently see a lost update, and that is true whatever
they wrote.

## Sources

- `https://github.com/hale-lang/hale` -- README, `AGENTS.md`
- `https://github.com/hale-lang/hale/tree/main/docs/src` -- the mdBook, in
  particular `verification.md`, `systems/memory.md`, `the-design.md`
- `https://github.com/hale-lang/bench` -- the benchmark grid (not read)

Measurements of our own tree, taken 2026-07-25 on `//Codex/reek` at CL 10484:
`codex/test/errors` holds 162 `.codex`, each with a `.failing` sidecar
(162 of 162); `codex/compiler` is 63 files and 55,879 lines.
