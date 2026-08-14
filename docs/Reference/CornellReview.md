# Codex in Light of Cornell CS 6120: Advanced Compilers

**Course**: CS 6120 -- Advanced Compilers (Self-Guided), Cornell University
**Instructor**: Adrian Sampson
**URL**: https://www.cs.cornell.edu/courses/cs6120/2025fa/self-guided/
**Reviewed**: 2026-06-25

---

## Abstract

Cornell CS 6120 is a graduate-level advanced compilers course covering
fourteen lessons from program representation through dynamic compilation,
concurrency, and compiler performance. This document reviews the course
syllabus against the Codex compiler -- a self-sustaining, bare-metal
compiler that achieves a hard fixed point of itself on x86-64 with no
operating system, runtime, or libc. The review proceeds lesson-by-lesson,
identifying where Codex aligns with textbook technique, where it
deliberately diverges, and where the course's lessons illuminate future
work. The central finding is that Codex's design validates the course's
theoretical framework while making aggressive pragmatic choices that a
classroom setting would not: no SSA, no GC, a fixed register assignment,
phase-scoped arena allocation, and a self-hosting fixed-point test that
replaces the usual unit-test methodology for compiler correctness. The
course's strongest relevance to Codex lies in its treatments of data flow
analysis, loop optimization, alias analysis, and fast-compiler data
structure design -- areas where Codex has room to grow. Its weakest
relevance is in LLVM infrastructure (Codex owns its entire toolchain) and
garbage collection (bare-metal Codex has no GC by design).

---

## Lesson-by-Lesson Analysis

### Lesson 1: Welcome & Overview

**Course content.** Proebsting's Law (compiler optimizations double
performance every 18 years, vs. Moore's Law's doubling every 18 months).
The framing question: what are compilers really for?

**Codex position.** Codex answers this question differently than the
course expects. The course frames compilers as optimization engines for
existing languages on existing hardware. Codex frames its compiler as a
correctness engine for a new language on bare metal. The compiler is its
own specification -- the acceptance test for any codegen change is that
the self-host remains a byte-identical fixed point of itself. This is a
stronger correctness criterion than any test suite: the compiler must
produce a binary that, when used to compile the same source, produces
the identical binary. Proebsting's Law is acknowledged but subordinated:
Codex Virtue #2 is "Correctness Over Performance."

The codegen quality campaign (CL 3091 → present) demonstrates that
Codex takes optimization seriously once correctness is established. The
`fib` benchmark dropped from 107 to 21 instructions -- competitive with
MSVC `/O2` and the .NET JIT. But the campaign came after the fixed point
was proven, not before.

### Lesson 2: Representing Programs

**Course content.** Concrete syntax, ASTs, instruction-based IRs (Bril).
Basic blocks, control flow graphs, terminators.

**Codex position.** The Codex compiler uses a layered representation
that maps closely to the textbook taxonomy:

1. **Concrete syntax** -- `.codex` source files with whitespace-
   significant indentation, prose at column 2, code at column 3+.
2. **AST** -- The parser produces `Document` and `SxDef` records.
   The desugar phase transforms these into `ADef` (annotated
   definitions). The type checker produces `CheckedChapter`.
3. **IR** -- The LOWER phase produces a flat IR (`IrDef`, `IrExpr`)
   that is closer to Bril's instruction-based model: explicit
   `IrLet` bindings, `IrIf`/`IrWhen` for control flow, `IrCall`
   for function application. No implicit control flow.
4. **Machine code** -- The EMIT phase walks IR directly to x86-64
   instructions. There is no separate "instruction selection" pass;
   the emitter pattern-matches IR nodes to x86-64 instruction
   sequences.

Codex does not use explicit basic blocks or a CFG data structure in its
IR. The IR is a tree of nested expressions with `IrLet` as the
sequencing construct. Control flow is represented structurally
(`IrIf`, `IrWhen`, `IrFor`) rather than as a graph with terminators
and jumps. This is a deliberate choice: the language has no `goto`,
no unstructured control flow, and no `break`/`continue`. Every CFG
the compiler would construct is reducible by construction (Lesson 5
calls this out -- languages with only structured control flow produce
only reducible CFGs). The tree representation is simpler, allocates
less memory, and is sufficient for all current optimization passes.

The Bril educational IR uses JSON as its canonical representation and
emphasizes language-neutrality. Codex's IR is language-specific and
exists only transiently in memory -- it is never serialized to disk
except in `IR` compile mode (used by transpiler plugs). The IR text
format uses S-expressions over serial for plug communication, a
pragmatic choice for a system where the compiler runs bare-metal and
communicates via serial port.

### Lesson 3: Local Analysis & Optimization

**Course content.** Dead code elimination (DCE). Local value numbering
(LVN) -- a unified framework for constant folding, copy propagation,
and common subexpression elimination within a basic block.

**Codex position.** The Codex compiler performs several optimizations
that correspond to the course's local analysis techniques, though they
are implemented as pattern-matching rules in the emitter rather than
as separate analysis passes:

- **Constant folding.** The emitter folds `IrIntLit` arithmetic at
  compile time. Negation of literals (`-(5)`) is folded into
  `IrIntLit -5` during IR construction.
- **Dead code elimination.** Proof definitions (return type `Proof`
  or `PropEqTy`) are erased entirely during emit -- they produce no
  machine code (CDX4020). The static bounds prover elides runtime
  bounds checks when it can prove a value is in range (CDX4010).
- **Copy propagation.** The destination-driven emission strategy
  (CL 3091 campaign) avoids many redundant copies by emitting
  results directly into the target register rather than into a
  temporary followed by a move.
- **Common subexpression elimination.** Not implemented as a general
  pass. The emitter relies on the IR being in a form where common
  subexpressions are already named via `let` bindings -- the
  language encourages this style.

The course's LVN framework is more general than what Codex currently
implements. A full LVN pass over the IR could catch opportunities
that the pattern-matching approach misses, particularly in
compiler-generated code where the same expression appears in
multiple branches of a `when` dispatch. This is a potential future
optimization, though it must be weighed against the fixed-point
stability requirement (any optimization that changes codegen must
reconverge to a byte-identical self-host).

### Lesson 4: Data Flow

**Course content.** The data flow framework: domain, transfer
functions, merge operators, worklist algorithm. Instantiations:
reaching definitions, live variables, constant propagation,
available expressions.

**Codex position.** The Codex compiler does not implement a general
data flow framework. Its analyses are bespoke:

- **Live variable analysis** is implicit in the register allocator's
  local rotation scheme. Temp registers cycle through
  [RAX, RCX, RDX, RSI, RDI, R11] (mod 6); local variables are
  assigned in order [RBX, R12, R13, R14] with stack spill beyond 4.
  There is no explicit liveness analysis -- the fixed assignment
  means every variable is "live" for its entire scope.
- **Reaching definitions** are not computed. The type checker
  resolves names during scope analysis; the emitter trusts these
  resolutions.
- **Constant propagation** is limited to literal folding.
- **The `punctual` checker** (CDX6001-CDX6005) is the closest
  thing to a data flow analysis in the compiler: it walks the call
  graph to verify that punctual functions only call other punctual
  or safe-builtin functions, do not allocate, do not recurse, and
  do not perform I/O. This is a forward analysis over the call
  graph -- not a classic data flow analysis, but structurally similar.

The course's data flow framework would be most valuable for Codex in
implementing a proper liveness analysis for register allocation. The
current fixed-assignment scheme leaves R8 and R9 unused and spills
to the stack when more than 4 locals are needed. A liveness-based
allocator could use all available registers and reduce spill traffic.
The codegen analysis (`docs/Designs/Done/Compiler/CodegenAnalysis.md`)
identifies register pressure as the next frontier -- the gap between
Codex and the .NET JITs on `gcd` and `sum` is primarily due to the
JITs' linear-scan allocation of named bindings.

### Lesson 5: Global Analysis -- Dominance

**Course content.** Dominance, strict dominance, immediate dominance,
dominance frontier, post-dominance. Natural loops (strongly connected
components with a single entry, identified via back edges).
Reducibility.

**Codex position.** Codex does not compute dominance trees,
dominance frontiers, or natural loops. It does not need to because:

1. **All CFGs are reducible by construction.** The language has no
   `goto`. Every control flow construct (`if`/`when`/`for`) produces
   structured, reducible control flow. The course notes that
   "languages with only structured control flow generate only
   reducible CFGs" -- Codex is an existence proof of this claim.

2. **Loop optimization is minimal.** The compiler does not perform
   LICM, strength reduction, or loop fusion. Loops in Codex are
   expressed as recursion (including tail-call-optimized recursion)
   or `for` expressions (sugar for `list-map`). The emitter handles
   TCO by converting `IrTailCall` into a parallel-move argument
   shuffle followed by a jump to the function entry -- this is
   strength reduction on the loop variable by another name.

3. **The tree IR makes dominance trivial.** In a tree-structured IR
   without explicit joins, every definition dominates all its uses
   by construction. Dominance analysis is a tool for flat CFGs; in
   a tree IR, the dominance relation is the tree's ancestor relation.

If Codex ever introduces a graph-based IR for advanced optimization
(global value numbering, partial redundancy elimination), dominance
computation would become necessary. For now, the tree IR's structural
properties make it unnecessary overhead.

### Lesson 6: Static Single Assignment (SSA)

**Course content.** SSA form (each variable assigned exactly once),
phi nodes, the dominance-frontier algorithm for SSA construction,
conversion out of SSA. Block arguments as an alternative to phi nodes.
The upsilon/phi variant.

**Codex position.** The Codex IR is not in SSA form, and this is a
deliberate architectural choice. The IR uses `IrLet` bindings which
are inherently single-assignment within their scope (functional-style
let bindings are already SSA in the single-definition sense). However,
the IR does not have phi nodes, block arguments, or any explicit merge
construct because the tree structure eliminates the need:

```
IrIf (cond)
  (then-branch producing value A)
  (else-branch producing value B)
```

This is semantically equivalent to a phi node that selects between A
and B based on which branch was taken, but it requires no explicit
SSA machinery -- the tree structure encodes the merge implicitly.

The course notes that "definitions == variables" and "instructions ==
values" in SSA. Codex's IR already satisfies this by construction:
every `IrLet` introduces a new name bound to a new value. There is no
mutable variable assignment in the IR (mutable records use
`__record-set` which produces a new record value at the IR level even
though it mutates in place at the machine level).

The absence of explicit SSA means Codex cannot directly use SSA-based
optimizations (sparse conditional constant propagation, global value
numbering via SSA, SSA-based register allocation). If these become
desirable, the functional structure of the IR means SSA construction
would be trivial -- the IR is already "almost SSA" by virtue of being
a tree of let-bindings.

### Lesson 7: LLVM

**Course content.** LLVM as compiler infrastructure. Writing custom
LLVM passes. The SSA-form LLVM IR with its API for manipulation.

**Codex position.** Codex does not use LLVM. This is foundational to
the project's identity: "If we didn't build it, we don't trust it."
The compiler emits x86-64 machine code directly, ARM64 and RISC-V
via plug CDX binaries, and WASM/PTX/SPIR-V via additional plugs.
There is no dependency on any external compiler infrastructure.

The trade-off is significant. LLVM provides hundreds of optimization
passes, battle-tested register allocation, instruction scheduling,
and target-independent optimization. Codex gives all of this up in
exchange for:

- **Total control.** Every byte of the output binary was produced by
  code the project wrote. The compiler is auditable end-to-end.
- **Self-sustainability.** The compiler compiles itself on bare metal
  with no OS. An LLVM dependency would break this -- LLVM requires
  a hosted environment.
- **Fixed-point verification.** The compiler's output is verified
  byte-identical to itself. An external optimization pass that is
  not deterministic (LLVM's pass ordering can vary) would break
  this property.
- **Bare-metal operation.** The compiler boots from a CDX binary on
  bare metal, communicates via serial, and produces output binaries.
  LLVM cannot operate in this environment.

The cost is visible in the codegen quality comparison:
`sum-to-N` beats C at both optimization levels, but `gcd` is 3
instructions behind MSVC `/O2` and 8 behind the F# JIT. The gap is
almost entirely register allocation -- LLVM's linear-scan allocator
would close it. The project accepts this trade-off because the
compiler can always be improved incrementally while maintaining the
fixed-point property, whereas an LLVM dependency could never be
removed once introduced.

### Lesson 8: Loop Optimization

**Course content.** Loop-invariant code motion (LICM), induction
variable elimination / strength reduction, loop unswitching, loop
fusion / fission, loop unrolling.

**Codex position.** Loop optimization is the area where the gap
between the course's textbook approach and Codex's current
implementation is widest.

Codex loops are primarily expressed as:
- **Tail-call-optimized recursion.** The TCO implementation
  (CL 3091 campaign) converts `IrTailCall` into a parallel-move
  argument shuffle + `jmp`. This is equivalent to a compiled
  `while` loop and produces tight code: the `sum-to-N` benchmark
  compiles to `add`/`lea`/`jmp` -- matching F# JIT density.
- **`for` expressions.** Sugar for `list-map`, producing a function
  call per element.
- **`fuel`-bounded iteration.** Loops with explicit termination
  fuel, used in board drivers and I/O polling.

None of these forms receive loop-specific optimization:

- **No LICM.** Invariant expressions in recursive functions are
  recomputed on every iteration. The functional style mitigates
  this somewhat -- `let` bindings outside the recursive call are
  naturally loop-invariant -- but the compiler does not hoist
  computations out of recursion.
- **No strength reduction.** Multiplication by a loop counter is
  not converted to incremental addition. The induction variable
  pattern from the course applies directly to Codex's TCO loops
  but is not implemented.
- **No loop fusion.** Consecutive `for` expressions over the same
  list are not fused. Each produces a separate `list-map` call
  with a fresh list allocation. This is a significant memory cost
  on bare metal where there is no GC -- fusing consecutive maps
  would eliminate intermediate list allocations entirely.
- **No loop unrolling.** Not implemented.

The course's loop optimization techniques are directly applicable
to Codex's TCO loops. LICM and strength reduction would benefit the
compiler's own hot paths (the type checker and emitter both contain
recursive walks that recompute invariant expressions). Loop fusion
on `for`/`list-map` chains would reduce heap pressure -- a critical
concern on bare metal.

### Lesson 9: Interprocedural Analysis

**Course content.** Call graphs, open vs. closed world assumptions,
inlining, devirtualization, context sensitivity.

**Codex position.** The Codex compiler operates in a closed-world
context: it sees all the code at once (the source is concatenated
into a single file before compilation). This enables whole-program
analysis.

**Inlining.** The compiler has a leaf inliner (CL campaign) that
inlines small functions at call sites. The `IrRemInt` pass inlines
`math-mod` calls as `idiv`/RDX sequences. The INLINE phase (CDX
mode only) performs broader inlining. Inlining decisions are
conservative -- overly aggressive inlining would increase code size
beyond the 4 MB code buffer, and every inline expansion must
reconverge to a byte-identical fixed point.

**Devirtualization.** Type class dispatch in Codex is resolved at
compile time via dictionary passing -- there are no virtual calls to
devirtualize. Pattern matching (`when`/`is`) compiles to tag-dispatch
sequences (compare-and-branch on the variant tag), which is already
direct.

**Call graph analysis.** The `punctual` checker traverses the call
graph to enforce bounded-execution constraints. This is a
whole-program interprocedural analysis: it must verify that the
transitive closure of callees from a `punctual` function contains
only other `punctual` or safe-builtin functions.

The course's treatment of context sensitivity is not directly relevant
to Codex today because the compiler does not perform interprocedural
data flow analysis. If constant propagation or range analysis were
extended across function boundaries, context sensitivity would
determine precision.

### Lesson 10: Alias Analysis

**Course content.** Pointer aliasing, the undecidability of alias
analysis, may-alias vs. must-alias, heap models, context-sensitive
alias analysis.

**Codex position.** Codex has no raw pointers in user code. Memory
is managed through:

- **Immutable records** -- no aliasing concern; values are
  semantically copied (though the compiler may share representations).
- **Mutable records** -- unique ownership enforced by the type checker
  (CDX2062). A mutable record cannot be aliased: handing it to two
  owners is a compile error.
- **Linear types** -- exactly-once usage enforced by the type checker
  (CDX2061, CDX2063). A linear value cannot be aliased by definition.
- **Bump allocation** -- all heap allocation goes through R10 (the
  bump pointer). The allocator does not reuse freed memory within a
  phase. There is no pointer arithmetic, no `free`, no realloc.

The type system provides the guarantees that alias analysis tries to
compute: mutable values have unique owners, linear values are used
exactly once, and immutable values are freely shareable but never
mutated. The compiler does not need an alias analysis pass because
the language eliminates aliasing hazards at the type level.

This is one of Codex's strongest validations of the course's material:
the course presents alias analysis as necessary because mainstream
languages permit arbitrary aliasing. Codex's type system demonstrates
that a language designed from first principles can make alias analysis
unnecessary by construction -- the same way that structured control
flow makes reducibility analysis unnecessary.

### Lesson 11: Memory Management

**Course content.** Garbage collection: reference counting,
mark/sweep, semispace, generational. Conservative GC. The
collector/mutator model.

**Codex position.** Codex has no garbage collector. This is not an
omission -- it is a design principle. Bare metal has no GC. Every
allocation is permanent until the producing function returns (or
until the phase allocator reclaims it).

Memory management in Codex uses three mechanisms:

1. **Bivy (bump allocator).** `pitch`/`strike` on R10. O(1)
   allocation (one add instruction). No selective free. Arena-scoped:
   everything in a phase is freed together at phase end.

2. **Deck (structured allocator).** Built on bivy. Deck regions
   survive phase compaction; bivy scratch is reclaimed. The
   reservation-copy pattern (CLs 3805/3849/3894) reclaims dead
   decks at phase boundaries -- the heap does NOT monotonically
   stack.

3. **Per-function heap save/restore.** In the emit phase,
   `__heap-save`/`__heap-restore` brackets per-function codegen.
   Each function's scratch is reclaimed after its code is emitted.

The course's GC lesson is valuable context for understanding what
Codex avoids and why. The `calloc` semantics on `__alloc`
(CL 1927) -- zeroing every allocation via `rep stosb` -- is a safety
net against uninitialized fields, not a GC mechanism. The poison
build (fill with 0xCD instead of zero) proves the compiler has no
uninitialized-field dependencies.

The survey-before-you-allocate discipline (Virtue #12) is Codex's
alternative to GC: each phase computes how much memory it needs
before allocating, and the phase allocator provides exactly that
amount. This is closer to region-based memory management (Tofte and
Talpin) than to any of the GC strategies the course discusses.

### Lesson 12: Dynamic Compilers

**Course content.** JIT compilation, profiling, trace-based
specialization, deoptimization, on-stack replacement, tiered
compilation.

**Codex position.** Codex is an ahead-of-time (AOT) compiler. There
is no JIT, no profiling, no runtime code generation. The compiler
runs once (on bare metal), produces a CDX binary, and that binary
executes without further compilation.

However, the course's discussion of trace-based specialization is
intellectually relevant to Codex's codegen campaign. The
destination-driven emission strategy -- where the emitter knows
*where* a value is needed and emits code to place it there directly
-- is analogous to the trace compiler's ability to specialize code
for a particular execution path. The difference is that Codex's
specialization is static (based on IR structure) rather than dynamic
(based on runtime profiles).

The `codex-vm` hypervisor includes an execution trace facility
(`-trace-file`) that records instruction-level execution. This could
be used for profile-guided optimization (PGO) in the future -- the
compiler could read a trace from a previous self-compilation and use
it to guide inlining, branch prediction hints, and code layout. This
is the bridge between the course's dynamic compilation lesson and
Codex's AOT model.

The REPL batch compile mode (the compiler runs in a loop, compiling
multiple programs sequentially) has some JIT-like characteristics:
the compiler itself is "warm" from previous compilations, and the
heap is reset between iterations. But this is operational reuse of a
running compiler, not JIT compilation of user code.

### Lesson 13: Concurrency & Parallelism

**Course content.** Memory models, sequential consistency, the
happens-before relation, data races, DRF⇒SC, the impossibility of
implementing threads as a library.

**Codex position.** Codex has a complete SMP story:

- **Per-core stacks and heaps.** Each AP gets an independent stack
  (spaced 64 KB apart) and a separate heap slice (R10 per core).
  No contention on the bump allocator.
- **Atomics.** Six builtins: `atomic-load`, `atomic-store`,
  `atomic-cas`, `atomic-add`, `atomic-exchange`, `memory-fence`.
  Codegen: `LOCK CMPXCHG`, `LOCK XADD`, `LOCK XCHG`, `MFENCE`.
- **Lock-free channels.** MPSC channels for inter-core message
  passing.
- **Effect types for concurrency.** Concurrent operations are
  tracked in the effect system. A function that uses shared mutable
  state declares this in its type signature.

The course's key insight -- that threads cannot be implemented as a
library because optimizations valid in single-threaded contexts
become incorrect in multithreaded ones -- is addressed by Codex's
approach: the compiler knows about concurrency at the language level
(via effects and atomics) rather than discovering it from library
calls. The effect system ensures that sequential optimizations are
not applied to concurrent code, because concurrent code has a
different type.

The memory model question (sequential consistency vs. relaxed
ordering) is relevant to Codex's atomics. Currently, all atomic
operations use x86-64's naturally strong ordering (TSO -- total store
order), which provides near-sequential-consistency for free. On
ARM64 and RISC-V (weaker memory models), the compiler would need to
emit explicit barriers. The ARM64 and RISC-V plugs do not yet handle
concurrent code, so this is future work.

### Lesson 14: Fast Compilers

**Course content.** Why compiler speed matters (edit-compile-run
cycle, large-project build times). Data structure flattening as a
cross-cutting optimization. The connection between flat ASTs and
bytecode interpreters.

**Codex position.** Compiler speed is directly load-bearing for
Codex. The self-host compilation takes approximately 22 seconds for
a CDX build. Every change must be tested through a full build
(text round-trip + CDX fixed-point + test battery), and a build
takes approximately 10 minutes end-to-end. Faster compilation
directly improves developer velocity.

The course's key technique -- flattening ASTs into contiguous arrays
-- maps directly to Codex's data structure strategy:

- **Lists as contiguous buffers.** Codex `LinkedList` is backed by
  a contiguous buffer with `__list-with-capacity` pre-allocation.
  The 11 accumulator lists in the emit phase are pre-allocated to
  32,768 elements each, and `list-push` writes in-place with no
  allocation while within capacity.
- **Records as flat structures.** Codex records are laid out as
  flat sequences of 8-byte fields in memory. Field access is a
  constant-offset load from the record base address.
- **Phase deck layout.** The compiler's phase allocator produces
  a flat, contiguous memory layout within each phase. The
  reservation-copy pattern compacts survivors into a contiguous
  region, improving locality.

The course's observation that "compilers exhibit flat profiles with
no single bottleneck" matches Codex's experience: the codegen
campaign (CL 3091 → present) made dozens of small improvements
rather than finding one hot function to optimize. The campaign
tracked per-benchmark instruction counts, and each optimization
(destination-driven emission, immediate operands, minimal frame
elision, commutative shortcuts) contributed a modest reduction.

The connection between flat ASTs and bytecode interpreters is
relevant to the REPL batch mode: the compiler's IR is consumed by
the emitter in a single forward pass, function by function, with
per-function heap save/restore. This streaming pattern avoids
holding the entire IR in memory simultaneously during emit (CL 3793).

---

## Thematic Synthesis

### Where Codex Validates the Textbook

1. **Structured control flow ⇒ reducible CFGs.** The course's
   Lesson 5 notes that languages without `goto` produce only
   reducible CFGs. Codex is a 28,000-line proof of this claim --
   no dominance analysis, no loop detection, no reducibility check
   needed.

2. **Type systems as alias analysis.** The course's Lesson 10
   presents alias analysis as a difficult, undecidable problem.
   Codex's linear and mutable-ownership types solve the same
   problem at the language level, demonstrating that the right type
   system makes alias analysis unnecessary.

3. **Arena allocation as GC alternative.** The course's Lesson 11
   treats GC as the primary memory management strategy. Codex's
   phase allocator (bivy + deck + reservation-copy) demonstrates
   that arena/region-based allocation is sufficient for a
   production compiler of non-trivial size, provided the programmer
   plans phase lifetimes explicitly.

4. **Self-hosting as a correctness criterion.** The course uses
   test suites (Turnt, Brench) and formal proofs (data flow
   convergence). Codex uses a stronger criterion: the compiler is a
   fixed point of itself. Any correctness bug that affects codegen
   will be caught by the fixed-point check, because the bug will
   cause the compiler's output to differ from itself. This is a
   whole-program integration test that no unit test can match.

### Where Codex Diverges from the Textbook

1. **No SSA form.** The course's Lesson 6 presents SSA as the
   canonical intermediate representation enabling modern
   optimizations. Codex uses a tree-structured IR with let-bindings
   that is semantically close to SSA but lacks phi nodes, block
   arguments, and the explicit join-point machinery. This limits
   the optimizations available but simplifies the compiler and
   reduces memory consumption.

2. **No general data flow framework.** The course's Lesson 4
   presents the worklist algorithm as a reusable framework. Codex
   implements each analysis (scope resolution, type checking,
   punctual verification) as a bespoke recursive walk. This is
   pragmatic -- each analysis needs different information -- but
   means new analyses must be built from scratch rather than
   instantiated from a framework.

3. **Fixed register assignment.** Standard compilers use graph
   coloring or linear scan to assign registers. Codex uses a
   fixed assignment: 6 rotating temps, 4 callee-saved locals,
   R10 for the bump pointer, R15 for closure environments.
   This leaves R8 and R9 unused and spills aggressively. The
   approach is simple and deterministic (essential for the
   fixed-point property) but leaves performance on the table.

4. **No LLVM, no external toolchain.** The course's Lesson 7
   treats LLVM as essential infrastructure. Codex rejects this
   entirely -- the compiler owns every byte from source to machine
   code. The trade-off (less optimization, more control) is
   accepted as foundational to the project's identity.

### Where the Course Points to Codex's Future

1. **Register allocation.** The codegen analysis identifies
   register pressure as the primary remaining gap. The course's
   treatment of liveness analysis (Lesson 4) and SSA-based
   allocation (Lesson 6) provides the theoretical foundation for
   a linear-scan or graph-coloring allocator that would close the
   gap with the JITs.

2. **Loop optimization.** The course's Lesson 8 techniques (LICM,
   strength reduction, loop fusion) are directly applicable to
   Codex's TCO loops and `for`/`list-map` chains. Loop fusion in
   particular would reduce heap pressure -- a critical concern on
   bare metal.

3. **Data structure flattening.** The course's Lesson 14 connects
   flat ASTs to compiler performance. Codex already uses flat
   records and pre-allocated lists, but the phase allocator's
   reservation-copy pattern could be further optimized by reducing
   the number of deep-copy walks required at phase boundaries.

4. **Profile-guided optimization.** The course's Lesson 12 on
   dynamic compilers and the `codex-vm` trace facility together
   suggest a PGO path: instrument a self-compilation, use the
   trace to guide code layout and inlining in subsequent builds.
   This would be an AOT analog to the JIT's runtime profiling.

---

## Conclusion

Cornell CS 6120 provides a rigorous framework for understanding
compiler design. Codex's self-sustaining compiler validates the
framework's theoretical claims while making engineering choices that
a textbook would not recommend: no SSA, no GC, no LLVM, a fixed
register assignment, and a fixed-point self-host test as the primary
correctness criterion. These choices are not naïve -- they are
consequences of the project's commitments to bare-metal operation,
total auditability, and deterministic reproducibility. The course's
strongest lessons for Codex's future are in data flow analysis (for
register allocation), loop optimization (for heap pressure reduction),
and compiler performance engineering (for build speed). Its weakest
lessons are in areas Codex has deliberately rejected (LLVM
infrastructure) or made unnecessary (garbage collection, alias
analysis). The course is recommended reading for anyone working on
the Codex compiler -- not as a blueprint to follow, but as a map of
the territory the compiler inhabits.
