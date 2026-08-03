# What ai-comp Has That We Don't -- and What To Do About It

Assessed 2026-07-14 against Codex at seed `40B4BAA3` (2,278,398 bytes),
compiler 36,389 lines across 55 files.

Source archive and license terms: `PROVENANCE.md`. **We may reimplement the
algorithms; we may not copy the code.** Everything below assumes clean-room.

---

## 1. The finding

**Codex has no middle end.** Not a weak one -- none.

Measured, this session:

| Layer | Lines | What it does |
|---|---:|---|
| `Emit/` | 18,814 | Instruction selection, and *all* optimization |
| `Types/` | 6,279 | Type check, inference, unification |
| `Syntax/` | 4,040 | Lex, parse |
| `IR/` | **2,191** | Lower AST → IR. **No optimization. No analysis.** |
| `Ast/`, `Semantics/`, `Core/` | 3,632 | Desugar, resolve, plumbing |
| **Total** | **36,389** | |

**52% of the compiler is the emitter.** There is no pass manager, no IR→IR
pass, and no analysis layer -- no use-def chains, no dependency graph, no
liveness, no alias analysis. `IR/Lowering.codex` lowers and hands off.

And `IR/` is not a compiler IR in the sense the word usually carries. `IRExpr`
(`IR/IRChapter.codex:23`) is a **name-based expression tree**:

```
IrLet   (Text) (CodexType) (IRExpr) (IRExpr)     -- nested, with a body
IrIf    (IRExpr) (IRExpr) (IRExpr) (CodexType)   -- arms are subexpressions
IrBinary (IRBinaryOp) (IRExpr) (IRExpr) (CodexType)
```

No basic blocks. No instruction list. No SSA -- bindings are `Text`, and they
shadow. It is a typed, desugared AST, and it is the *only* IR between the type
checker and 18,814 lines of emitter. **That single fact governs everything
below**, and it is why §3 is ordered the way it is rather than by payoff.

Every optimization we have is a peephole *at the point of emission*:
destination-driven emit, staged operands through R8/R9, reg-left/reg-right
folds, `cmp`-immediate fusion, NOP elision, power-of-two strength reduction.
`docs/Reference/CodegenAnalysis.md` records thirty-odd CLs of this work, and
it is genuinely good work -- but read its own table and the ceiling is visible:

> *"fib/fact/gcd/sum function-body instruction counts plateaued at CL 3400."*

Twenty CLs after that plateau moved those four benchmarks by zero. They moved
seed bytes, because a peephole that fires 2,600 times across the compiler's own
source is worth real density -- but the *shape* of the generated code stopped
improving, because a peephole cannot see past the instruction it is rewriting.
gcd still sits **+156%** over the F# JIT. That gap is not a missing peephole. It
is a missing pass.

ai-comp is the other half of a compiler: **16 passes over 3 IRs, with 5
analyses underneath.** It is precisely, almost eerily, the half we lack. That is
why it is worth the read -- not because a VLIW toy VM resembles x86-64 (it does
not), but because the *structure* it demonstrates is the structure we're missing.

### The sharpest single observation

Our bounds prover, `aexpr-proven-range` (`Types/TypeCheckerInference.codex:742`),
is a *degenerate value-range analysis*: a structural walk over an expression
returning an `(lo, hi)` interval, defaulting to full `i64` on anything it does
not recognize. It has no loop handling, no fixpoint, no widening, and -- critically --
no branch refinement (`AIfExpr` takes the plain *union* of both arms; it never
narrows `x` inside `if x < C`).

ai-comp's `range_analysis.md` is **the same analysis, done properly**: a sound
interval domain with a stated soundness contract, a widening ladder chosen to
land on mask-shaped bounds, a narrowing phase to recover overshoot, and branch
*and select* refinement -- which the author calls out as *"load-bearing, not an
optimization,"* because without it his wrap recurrence diverges to full range.

We even have this analysis **twice**: the comment at
`TypeCheckerInference.codex:731` describes itself as *"the checker-side twin of
the emit prover's `ir-expr-proven-range`."* Two hand-written interval walks that
must agree, in the same tradition as BACKLOG 2.14's two builtin lists.

This is the sleeper. See opportunity **A**.

---

## 2. What they have, mapped to what we're missing

Their default pipeline (`upstream/compiler-pass_config.json`), in order:

```
dce → loop-unroll → simplify → dce → cse → sroa → strip-assume → dce
  → load-elim → dse → simplify → slsr → dce → slp-vectorization → dce → cse
  → mad-synthesis → dce → hir-to-lir → copy-propagation → lir-dce
  → simplify-cfg → phi-elimination → inst-scheduling
  → mir-reg-pressure-profiler → mir-register-allocation → mir-to-vliw
```

| Their pass | Do we have it? | The gap it would close |
|---|---|---|
| `mir-register-allocation` (linear scan) | **No.** `alloc-local` is monotonic with ad-hoc scope recycling | CodegenAnalysis Root Cause 3 names linear-scan *"Option A… the full solution… largest reward."* BACKLOG 3.1 is the ARM64 half of the same hole |
| `range-analysis` | **Degenerate twin** (see above) | BACKLOG 2.11 (CDX2051 sweep), plus every elided runtime bounds check (CDX2079 `BoundsProven`) |
| `inst-scheduling` | **No.** We emit in source order | Unmeasured for us -- see §5, this is the one I can't price |
| `slp-vectorization` | **No.** SSE2 emission exists; nothing *generates* vector ops | BACKLOG 3.3 -- we can encode vectors but no pass ever forms one |
| `cse`, `dce` (at IR) | **No** | Whole classes of redundancy the emitter cannot see |
| `load-elim` / `dse` | **No** | Store-to-load forwarding does not exist for us |
| `sroa` | **No** | Record fields never get promoted to registers |
| `slsr` | **No** | -- |
| `simplify` (const-fold, assoc, `mul_dist`) | **Partial, in the emitter** | pow2 div/rem strength reduction is emit-side, not IR-side, so it fires only where the emitter looks |
| `loop-unroll` | **No** | -- |
| `copy-propagation`, `simplify-cfg` | **No** | We do have "dead jump elimination after TCO branches" -- a peephole doing one case of `simplify-cfg`'s job |

Two of their passes have no meaning for us and should be ignored:
`phi-elimination` (their LIR has phis; our IR does not) and `mad-synthesis`
(a VLIW `multiply_add` slot we have no analogue for -- though see the
*honest caveat* in §4).

---

## 3. The opportunities, ranked

Ranked by (payoff × confidence) ÷ risk. Risk is dominated by one fact: **every
codegen change costs a two-pass seed rebuild and must reach a byte-identical
fixed point.** A middle-end pass that miscompiles does not fail a test -- it
silently corrupts the compiler that compiles the compiler. That is the tax on
everything below, and it is why the order starts where it does.

### A. Promote the bounds prover to a real range analysis -- *start here*

**What.** Replace the two structural `*-proven-range` walks with one sound
interval analysis: transfer functions per operator, branch refinement on `<`/`==`,
a loop fixpoint with a widening ladder, and a narrowing phase.

**Why first.** Four reasons, and I want to be explicit that this is not the
"exciting" pick:

1. **It is not codegen.** It changes what the *prover* can prove, not what the
   emitter emits. A bug here makes a compile fail loudly with a CDX2051, not
   silently miscompile. This is the lowest-risk way to build our first real
   dataflow analysis, and we need one before any of B-E is safe.
2. **It closes real backlog.** BACKLOG 2.11 (*"a stray unproven narrowing fails
   that compile"*) is a false-positive problem -- the prover rejects code that is
   actually in range. Branch refinement alone kills the common shape
   (`if i < n then arr[i]`), which today the prover cannot see through at all.
3. **It pays at runtime.** CDX2079 `BoundsProven` *elides a runtime bounds check*.
   Every interval we can newly prove is a check deleted from the hot path, with
   no codegen change at all.
4. **It de-duplicates.** One analysis, consumed by both the checker and the
   emitter, instead of two walks that must agree.

**Evidence it's tractable.** Their doc is unusually candid about the two bugs
that matter, and both are ones we would otherwise hit: (a) the interval lattice
is ~2³³ tall, so *naive iteration cannot terminate* -- you need the widening
ladder or you hang; (b) they tried memoizing loop fixpoints by signature and it
was **unsound**, because an inner body can capture a changing outer SSA the
signature never saw. They replaced it with a warm start that re-proves the
post-fixpoint property every time. That is a bug we would have shipped.

**Risk:** low. **Cost:** medium. **Gate exposure:** type-checker only, no seed
semantics change *if* we keep the emitter's prover as-is initially and swap it
second.

### B. A pass manager, and the pipeline as data

**What.** `IR/Lowering.codex` currently *is* the pipeline, hardcoded. Introduce
an IR→IR pass interface, a named registry, and an ordered pipeline read from
config -- their `pass_config.json` shape: an ordered list of pass names (passes
**repeat**: `dce` appears five times, `simplify` three), plus per-pass `enabled`
and `options`.

**Why this is the keystone.** Not because a pass manager is interesting, but
because **without it, "optimizing runs" are not possible at all.** Our
optimizations are welded into emission; you cannot turn one off, cannot A/B two
orders, cannot bisect a regression to a pass. Their `--pass-config
/path/to/my.json` lets N compiler instances run concurrently under different
configs. That is the substrate for §5.

**The evidence that makes this non-optional** is their MAD ablation
(`upstream/mad_optimization.md`):

| `assoc_fold` | `mul_dist` | cycles |
|---|---|---:|
| on | on | **1141** |
| off | on | 1188 |
| off | off | 1182 |
| on | off | 1209 |

**Either transform alone is worse than neither.** `assoc_fold` alone costs 27
cycles; `mul_dist` alone costs 6; together they win 41. Pass profitability is
*non-monotone and coupled* -- `assoc_fold` must remove a use before `mul_dist` is
legal on the remaining one. You cannot derive that by reasoning. You find it by
running the grid, and you can only run the grid if the pipeline is data.

**Risk:** low (pure refactor, no semantics). **Cost:** medium.

### C. Linear-scan register allocation

**What.** The pass CodegenAnalysis has been calling "Option A" for thirty CLs.

**The prerequisite nobody has priced.** A linear-scan allocator assigns registers
to *live intervals over a linear instruction sequence*. **We have no linear
instruction sequence.** Our IR is a tree (§1); the linear sequence first exists
as raw machine bytes, by which point it is far too late to allocate. So C is not
"add a pass" -- it is **"add an IR level,"** the equivalent of their `hir-to-lir`:
flatten the tree to a block-structured instruction list, number the values, and
resolve shadowed `Text` names into distinct definitions. That flattening *is* the
bulk of the work; the allocator on top of it is the well-understood part.

This is the single most important correction in this document. Read casually,
ai-comp says "you're missing a register allocator." Read against our actual IR,
it says **"you're missing the IR the register allocator runs on."**

**Why not first.** Because it is the highest-payoff *and* the highest-risk item,
and our own history says so in blood. From CodegenAnalysis's attempt log:
`emit-binary-simple-right` diverged `next-local` and corrupted callee-saved
registers during stage1→stage2 self-compile. Spill-slot recycling in
`emit-if-generic` *"corrupted a callee-saved local in a 21KB lifted lambda (R12
garbage, crash in the self-compiled TEXT emitter)."* The hard-won rule --
**register recycling is safe, spill-slot recycling is not** -- was paid for.

A real allocator subsumes all of that. It is the right destination. But it wants
a liveness analysis underneath it, and liveness is the thing we've never built --
which is exactly what A and B put in place.

**Payoff:** CodegenAnalysis: *"would close most of the remaining gap to C /O2."*
Concretely, gcd +156% → target ~+55%; every intermediate stops round-tripping
through `alloc-local`; and the seed shrinks, because the compiler's own 2,600+
functions are full of `a + b` over locals.

**Risk:** high. **Cost:** high. **Do not start it before B.**

### D. IR-level DCE + CSE + copy propagation

**What.** The boring passes. They are boring because they work.

**Why they matter here specifically.** Their pipeline runs `dce` **five times** --
after unroll, after simplify, after SLSR, after SLP, after MAD. That is not
sloppiness; it is the design. Every rewrite pass *leaves husks*, and the next
pass's cost model reads op counts. Their SLSR doc is explicit that its whole
profitability model is a **dead-op liveness fixpoint** -- it only rewrites when it
can *prove* a net op-count reduction, and two earlier heuristic generations
leaked through CSE-shared ops and were wrong.

For us: CSE at the IR level would catch redundancy across the whole function
body, which no emission-site peephole can. It is also the cheapest possible
consumer of the use-def infrastructure that A and B introduce -- the pass that
proves the infrastructure works before C bets the seed on it.

**Risk:** low-medium. **Cost:** low, *given B*.

### E. SLP vectorization

**What.** Auto-form vector ops from isomorphic straight-line scalar code
(Larsen & Amarasinghe, PLDI 2000). Seeds from consecutive memory accesses,
extend up the use-def chain, check legality, cost-model, emit.

**Why last, despite being the flashiest.** We shipped SSE2 emission (BACKLOG 3.3
Phase 1) -- we can *encode* `paddd`. But **nothing in the compiler ever decides to
form a vector operation.** The lanes exist and nothing fills them. SLP is the pass
that fills them, and it is the single most valuable thing in their repo *in
principle*.

In practice it is last, because SLP needs everything else first: a DDG, alias
analysis, a cost model, and CSE/DCE around it to clean up. Their own pipeline
puts `slp-vectorization` fourteenth of twenty-seven, after unroll, SROA,
load-elim, and SLSR have all shaped the code for it. Attempting SLP without that
scaffolding produces a pass that fires almost never -- and each time it does, bets
the seed.

**And it inherits C's prerequisite.** SLP packs *isomorphic instructions in
straight-line code*, seeded from consecutive memory accesses. On a tree IR there
is no straight-line code to scan and no notion of "consecutive." It needs the
same flat instruction list C needs. **C and E share one prerequisite, and that
prerequisite is the actual project.**

**Risk:** high. **Cost:** high. **Payoff:** potentially the largest of any item
here, and it also gives BACKLOG 3.3 (AVX/AVX2) a *reason* -- doubling lane width
is worth little while nothing generates lanes.

---

## 4. What NOT to take

Being clear about this matters as much as the list above.

- **The Python.** Unlicensed (`PROVENANCE.md`) and, more to the point, written
  for a machine that isn't ours.
- **The VLIW model.** Their scheduler exists because their target has explicit
  bundles with per-engine slot limits and *no same-bundle RAW forwarding* -- the
  hardware cannot forward a result within a cycle, so the compiler must schedule
  around it. **x86-64 has an out-of-order core that does this in hardware, far
  better than we would.** A static scheduler for x86-64 is mostly wasted effort;
  the exception is instruction *selection* order affecting register pressure,
  which is a register-allocation concern (C), not a scheduling one. **Do not port
  `inst-scheduling`.** It is the best-engineered document in their repo and it is
  the one we should skip. (It *would* matter for a future in-order MCU target --
  Thumb-2, BACKLOG 3.5 -- and for VLIW-ish GPU backends. File it, don't build it.)
- **`mad-synthesis`, as such.** No scalar MAD on x86-64. *Honest caveat:* the
  underlying idea -- fusing a multiply and its single-use add -- maps onto `lea`,
  which we already exploit as a peephole, and onto FMA (`vfmadd*`) if we ever do
  float SIMD. The *pass* is not portable; the *shaping that feeds it* (their
  `simplify` doing `assoc_fold` + `mul_dist` to create fusable shapes) is a
  general algebraic simplifier and very much is.
- **`assume_local_memory`.** A source-level UB contract -- *"violating the contract
  is undefined behavior."* Codex's entire premise is that we do not ship
  trust-me-it's-fine escape hatches. If we want the SROA win we earn it with a
  proof (their read-only-window path does exactly that: object-size analysis plus
  alias refutation), not with an annotation the programmer can lie with.

---

## 5. The optimizing runs

This is what the pass-manager work buys, and it is the part I'd most want to
actually run.

Once the pipeline is data (**B**), an "optimizing run" is a **search over pass
orders and option vectors**, scored by a metric. Their harness already does the
enabling half of this: `--pass-config`, `--print-metrics` per pass,
`--print-after-all`, a register-pressure HTML chart, a Perfetto trace viewer.
Ours is `Emit/X86_64InsnCount.codex` -- 160 lines.

**Our objective function is better than theirs, and this is the genuinely
exciting part.** They score against a simulated VM's cycle count on one kernel.
We have something no synthetic benchmark can match: **the compiler compiles
itself.** So the score of a pass configuration is:

1. **Seed size** -- 2,278,398 bytes today, over ~2,600 functions of real,
   varied, non-synthetic code. A pass that shrinks the seed shrank real code.
2. **Self-compile wall time** -- we already know self-compile is *output-bound*
   (~78% in `__write_binary`, per the current-state memory), so this metric is
   noisy for codegen quality but sharp for compile-time cost of the pass itself.
3. **The four benchmarks** (fib/fact/gcd/sum) plus the extended set (ack, tak,
   collatz, locals) -- static instruction count, already harnessed in `bench/`.
4. **The fixed point still holds.** Non-negotiable, and it is a *correctness
   oracle no benchmark suite can give you*: if a pass miscompiles, the compiler
   it produces compiles a different compiler, and the byte-identical fixed point
   breaks. **Our gate is a stronger correctness signal for optimizer work than
   any test suite they have.** That cuts the risk of C and E substantially -- it
   just does not cut the *cost* of hitting it.

A concrete first run, once B lands: hold the pipeline fixed, sweep each pass's
`enabled` flag and its options across the seed + bench metrics, and print the
ablation table. If our passes are as coupled as theirs, that table will contain
at least one pair where either alone is worse than neither -- and we will only
know because we ran it.

**Non-obvious warning from their experience:** their SLSR doc notes the cost model
*deliberately does not model scheduling effects* and relies on cycle-count
regression tests to catch rewrites that improve the op count but hurt the
schedule. Our analogue: a pass can shrink static instruction count and *grow*
the seed, or shrink the seed and slow self-compile. **Score on more than one
metric or the search will happily optimize the one we chose and wreck the rest.**
CodegenAnalysis already has one instance of this -- CL 3695 cut `sum` from 26 to
14 instructions while *adding* 2,373 bytes to the seed.

---

## 6. Recommended order

**A → B → D → | flat LIR | → C → E.**

The bar in the middle is the real boundary in this document, and it is not where
I expected to draw it when I started reading.

**A, B and D need nothing new.** They all operate on the structured tree IR we
already have -- and, importantly, *so do their counterparts upstream*. ai-comp's
HIR is **also** a structured tree (`ForLoop`/`If` with carried values, not a CFG);
their `range_analysis.md` says so outright -- *"the HIR is structured, so the
analysis is structural induction rather than a CFG worklist."* Their `simplify`,
`cse`, `dce`, `load-elim`, `sroa`, `slsr` and `mad-synthesis` are **all HIR
passes**. Nine of their sixteen passes run on a tree shaped much like ours. Those
are portable to the IR we have today, and the range analysis (A) is portable
*almost verbatim in structure*, because structural induction over a typed tree is
exactly what our `aexpr-proven-range` already tries to do.

**C and E are on the far side of an IR level we have never built.** They are
LIR/MIR passes upstream, and they run after `hir-to-lir` + `phi-elimination` --
the flattening step we do not have and cannot fake. A linear-scan allocator wants
live intervals over a linear instruction list; SLP wants isomorphic instructions
in straight-line code. Our tree offers neither. Building that flattening is a
substantial project in its own right, and it is the honest price of the two items
CodegenAnalysis has been calling "the full solution" for thirty CLs.

**So the shape of the answer is:** we can have a real middle end for the passes
that fit a tree -- starting immediately, at low risk, closing real backlog -- and
the moment we want the *big* wins (registers, vectors) we must first admit that
`IR/` needs a second level beneath it. That is a decision worth taking
deliberately, not one to back into.

### The open question, now answered

*Does the emitter's `ir-expr-proven-range` have consumers the checker's twin does
not?* **Yes -- checked 2026-07-14, and the answer changes A's risk.**

The two provers are **not** merely duplicated. Different reach, different
consumers:

- The **checker's** `aexpr-proven-range` (`TypeCheckerInference.codex:742`) has an
  environment with recorded local ranges, and drives **diagnostics only** --
  CDX2051 error, CDX2053 info.
- The **emitter's** `ir-expr-proven-range` (`X86_64Compound.codex:847`) drives
  **codegen**: it decides whether to emit the runtime overflow check around a
  narrow-store -- a `cmp`/`jcc`/`ud2` trap for `OvError`, or the saturating clamp
  for `OvClamping`.

So A is **not** a diagnostics-only change. It needs the gate and a two-pass seed
rebuild, and its hazard is the inverse of what I first assumed: **this analysis
removes traps.** An unsound interval deletes an overflow check that should have
fired, and the narrow-store silently truncates. Soundness is the contract, and it
must be *stated and fuzzed* as one -- which is exactly the discipline
`upstream/range_analysis.md` models.

And the emitter's prover is the weaker of the two, in the way that matters most:
it threads **no environment at all**, so
`is IrLet (name) (lty) (value) (body) (sp) -> ir-expr-proven-range body` discards
the bound value's range outright, and `IrName` is not matched (falling to full
i64). **A named local has no provable range in the emitter.** So
`let x = 5 in rec.field <- x` emits a compare, a branch and a trap on a
compile-time constant -- at every narrow-store to a bounded field in the tree.
That is the first thing a real analysis deletes, and today it is 100% unprovable.

Plan and sequencing: `docs/Designs/Active/Compiler/MiddleEnd.md`.

---

## 7. The other thing worth noticing

The repo has a `CLAUDE.md`, an `AGENTS.md`, a `.claude/` and a `.codex/`. It was
built the way we build -- by an expert directing agents against a written spec.
Archived as `upstream/aicomp-CLAUDE.md`.

Its instrument is structurally ours: one mandatory-reading doc (*"Before
implementing any optimization, always re-read the relevant sections of
`docs/VLIW_ISA.md`"*), a fenced-off don't-touch directory (the upstream
challenge code), and a hard rule that every bug fix ships a regression test
that would have failed before it.

One difference worth stealing outright: **every one of his design docs has a
"Limitations" section that states what the pass provably does not do.** `slsr.md`:
*"Op count is the objective; the schedule is validated empirically, not modeled."*
`range_analysis.md`: *"The domain is non-relational: it cannot express `i != j`."*
That is the same instinct as our BACKLOG rule -- a known gap is stated louder, not
deleted -- but applied at the level of the individual pass, in the doc that
describes it. Worth adopting when we write ours.
