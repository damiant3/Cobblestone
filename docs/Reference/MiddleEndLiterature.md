# Middle-End Literature Synthesis

**Written:** 2026-07-14 (reek), from three commissioned surveys (range analysis /
bounds-check elimination; pass architecture on tree IRs; register allocation).
**Consumes into:** `docs/Designs/Active/Compiler/MiddleEnd.md` — this doc is the
evidence and citation base; the plan carries only the decisions.
**Standing constraint on all of it:** everything here is public literature to be
reimplemented clean-room. Nothing below is from `docs/Reference/AiComp/upstream/`
(which is unlicensed); these are the decades-old published sources.

The three questions the surveys answered, and the answers:

1. **How do we make the range analysis sound enough to remove traps?**
   Exhaustive small-width testing of every transfer function (LLVM's
   discipline), plus a shadow-trap mode (V8's hardening), plus scoped
   branch refinement (ABCD's π-nodes collapsed onto lexical scope).
2. **What shape should the tree passes take?** One GHC-style simplifier
   with an occurrence-analysis pre-pass and a separate late CSE — not
   nanopass granularity, and **not e-graphs** (Cranelift's own numbers
   reject them).
3. **What makes the allocator safe to build?** A regalloc2-style symbolic
   checker that validates every allocation of every compile, built
   *before* the allocator.

---

## 1. Range analysis (feeds WS-A1/A2)

### The testing discipline — LLVM ConstantRange (ADOPT, the #1 takeaway)

LLVM's interval domain is validated by **exhaustive enumeration at small
bitwidths** (`llvm/unittests/IR/ConstantRangeTest.cpp`): enumerate every
(lo, hi) pair at 1 and 4 bits, evaluate the *concrete* integer op over every
member of both input ranges, and assert (a) **containment** — every concrete
result lies inside the returned interval (soundness; a failure here is exactly
a would-be silent truncation for us) — and (b) **tightness** where the op
promises exactness (both endpoints witnessed). The whole test for an op is two
lambdas. Even with this, LLVM shipped endpoint off-by-ones (UIToFP/SIToFP
ConstantRange bug, fixed 2024) — **the bugs are always at the endpoints.**

Our version is cheaper than theirs: no wrapped ranges, so width 4 gives 136
intervals / 18,496 pairs / ~5M member evaluations — seconds. And our concrete
oracle is better: the test runs *under codex-vm*, so the concrete semantics are
by construction the runtime's own. Ship the harness in the same CL as the
kernel; a transfer function without its exhaustive test does not merge.

Layer 2 for A2: randomized width-64 containment with adversarial endpoints
(lo, hi, lo±1, hi±1, 0, −1, i64-min, i64-max) — every documented real-world
range bug lived at an endpoint this probes by construction.

### The hardening — V8's aborting CheckBounds (ADOPT as "shadow-trap mode")

After repeated typer-range bugs were weaponized through bounds-check
elimination (CVE-2019-5782: a `4 < 4` folded true by an off-by-one in
`MachineOperatorReducer`; the `String#lastIndexOf` range fact short by one),
V8 **stopped deleting the checks**: analysis-"redundant" checks became
aborting asserts, so a wrong proof yields a trap, not a silent OOB. Our
transplant: a compile flag under which every trap the emitter prover elides is
emitted anyway as an aborting assert tagged prover-unreachable. **Run the gate
and targeted tests under this flag in test builds**; any firing assert is a
prover soundness bug caught before it ships as silent truncation. Zero cost to
real seeds (flag off).

### The refinement mechanism — ABCD's π-nodes (ADAPT, collapsed onto scope)

ABCD (Bodík/Gupta/Sarkar, PLDI 2000) attaches branch facts to a *fresh name*
per branch arm (e-SSA π-assignments), because a fact must never outlive or
escape the region its branch guards — and must never justify eliminating **the
generating check itself**. On a lexically scoped tree this collapses to:
analyzing `IrIf (i < e) then-arm else-arm`, push `env[i] := env[i] ∩
[lo(i), hi(e)−1]` for the then-arm, the complement for the else-arm, pop on
exit. No renaming, no inequality graph, no cycle machinery (those exist for
loops and we have none). Graal (PiNode anchored to its guard) and RyuJIT
(assertions valid only where dominated) reach the same anchoring discipline
independently. Corollary rule: a fact derived from a trap must never justify
removing that same trap.

SCCP's contribution (Wegman–Zadeck): **fold the condition first** — if the
ranges already decide `i < n`, take only the surviving arm and skip the dead
one. Two lines, adopt.

Patterson's VRP (PLDI 1995): the domain can carry a stride almost free; SKIP
for v1, note for later. Verasco/CompCert: SKIP the mechanized proof, ADOPT the
invariant statement — every kernel op carries the one-line contract "for every
concrete value in the input intervals, the concrete result is inside the
returned interval," and the exhaustive test is the mechanical check of exactly
that sentence.

### Sources

ABCD PLDI 2000 (dl.acm.org/doi/10.1145/349299.349342) · Patterson PLDI 1995
(lighterra.com/papers/valuerangeprop/) · Wegman–Zadeck TOPLAS 1991 ·
LLVM ConstantRangeTest.cpp (github.com/llvm/llvm-project, unittests/IR) ·
LLVM UIToFP fix PR #86041 · Alive2 (github.com/AliveToolkit/alive2) · Verasco
POPL 2015 (inria.hal.science/hal-01078386v1) · Graal IntegerStamp · RyuJIT
ryujit-overview.md + dotnet/runtime PRs #100777, #40180 · gts3.org TurboFan BCE
exploit writeup · doar-e.github.io typer-bug hardening writeup · ZDI
CVE-2021-21220 analysis.

---

## 2. Tree passes (feeds WS-D, reshapes it)

### The architecture answer: one GHC-style simplifier in a pass-manager skin

GHC Core is the closest production analogue to our IR that exists — typed
lambda-plus-let tree, shadowing allowed, no loops, purity known — and its
middle end is **one Simplifier pass** applying dozens of *local* rewrites in a
single traversal (beta, let-inlining, case-of-known-constructor, case-of-case,
constant folding), iterated to a cheap fixpoint, with a handful of separate
global passes around it. Adopt that architecture wholesale; our strictness and
explicit effect rows make it *stronger* here than in GHC (their two known
weaknesses — laziness-driven CSE timidity and syntactic purity guessing — are
both places our types answer precisely).

The load-bearing pieces:

- **The occurrence analyser comes first** (separate cheap pre-pass): per
  binder, dead / occurs-once (where) / occurs-many, plus loop-breaker
  selection for recursive groups. One linear pass makes three things trivial:
  DCE (drop dead lets during simplification — **DCE stops being a pass**),
  unconditional safe inlining of occurs-once-not-under-lambda bindings, and
  size-heuristic inlining for the rest (subsumes the leaf-inliner's
  bookkeeping). Highest leverage-per-line in the whole survey.
- **Termination by tick budget**: every applied rewrite increments a counter;
  iterate the traversal to a max (GHC default 4; most code fixpoints in 2),
  stop early on a zero-tick iteration, and **error loudly** on a
  program-size-proportional global budget (never hang). The per-rule tick log
  doubles as the ablation instrument — a fixed-point break diffs to "rule X
  fired 412 times, was 0."
- **Shadowing by the rapier** (Secrets of the GHC Inliner): carry a
  substitution + in-scope set; rename only on actual collision. No global
  unique-names pass, ever.
- **Memory (Rule 8)**: each simplifier iteration rebuilds the tree, so the
  iteration cap is also the peak-memory bound; heap-restore between iterations
  keeping only the newest tree. This is why the arena economics favor **few
  fat passes over nanopass granularity**: Chez pays traversal time and lets
  the GC reclaim; we pay peak deck.
- **CSE is separate, late, and hoisting-free**: scoped hash-consed CSE over
  *pure, trap-free* subtrees, reusing only a **dominating occurrence** (scope
  stack push/pop at binders and arms = dominator-based value numbering for
  free on a tree). Run after the simplifier (GHC's ordering lesson: CSE blocks
  inlining if run early). The strict-language hazard is **work creation by
  hoisting** — commoning across arms forces evaluation the taken path never
  wanted, and for us can change *whether a trap fires* — hence: no hoisting in
  v1, and resolve explicitly that "pure" in our effect rows does not yet imply
  trap-free (the safety-gates bug class) before enabling CSE on arithmetic.
- **From nanopass, take exactly one thing**: a reusable `ir-check` pass
  (scope discipline under shadowing, type/effect agreement) interleavable
  anywhere in the pipeline, on in debug builds. It converts "silent corruption
  three stages later" into "pass N emitted malformed IR."
- **Smart constructors**: apply eager local simplification + hash-cons lookup
  at node construction (the one idea worth stealing from Cranelift's
  aegraphs — see below), shared by every pass.

### Why not e-graphs (SKIP, with the receipts)

Equality saturation (Tate et al. 2009; egg POPL 2021) solves pass-ordering by
never destroying alternatives; extraction picks the best at the end. The only
production deployment is Cranelift's **aegraph** — and their own retrospective
is the strongest published argument against adopting it here: full egg-style
e-graphs cost **23% compile time with zero rewrites applied**; the shipped
aegraph nets 7–8% compile-time cost; measured **average e-class size was 1.13**
with ~2 cases in 4M nodes where eager rewriting missed anything saturation
would catch; extraction with sharing is NP-hard; rule-ordering heuristics
remain. Translation: the wins came from hash-consing + eager rewriting + GVN
done well, not from the multi-representation machinery. A monotone-growth
graph is also the worst structure for our no-GC arenas, and with no loops we
don't even get their LICM payoff. Re-evaluate only if ablation later proves
ordering-dependent misses a fixpoint loop cannot close.

### Sources

Peyton Jones & Santos, "A Transformation-Based Optimiser for Haskell" (SCP
1998) · Peyton Jones & Marlow, "Secrets of the GHC Inliner" (JFP 2002) · GHC
User's Guide (max-simplifier-iterations, simpl-tick-factor) · Chitil, CSE in a
Lazy Functional Language (IFL 1997) · Keep & Dybvig, nanopass ICFP 2013 ·
Briggs/Cooper/Simpson value numbering (SP&E 1997) · Tate et al. eqsat POPL
2009 · Willsey et al. egg POPL 2021 · Cranelift aegraph RFC + Fallin's
EGRAPHS 2023 slides + 2026 retrospective (cfallin.org) · Necula translation
validation PLDI 2000 · Csmith PLDI 2011 / YARPGen OOPSLA 2020.

---

## 3. Register allocation (feeds step 5's design doc, E3)

### The checker comes first (ADOPT — highest priority in the survey)

regalloc2 (Cranelift) treats the allocator as untrusted and validates **every
allocation result** with a symbolic checker (`src/checker.rs`; Fallin's
"Correctness in Register Allocation" post; the verified ancestor is Rideau &
Leroy, CC 2010). Design, reduced to our DAG case: state is
`Map<Location, VRegSet>` where Location is a physical register **or a spill
slot** (slots are first-class — this is the property that would have caught
the 2026 spill-slot-recycling corruption at the guilty reload, same compile);
allocator-inserted moves copy sets; an original instruction checks each use
(`vreg ∈ state[location]`, else error naming function+instruction+operand),
then scrubs the def's vreg from every location and sets the def location to
exactly {vreg}; calls clobber caller-saved entries; join-in is pointwise set
intersection. On a loop-free DAG there is **no fixpoint** — one pass in
topological block order. ~200 lines. Run it on every allocation of every
compile unconditionally: our self-compile is the fuzz corpus (every build is a
36K-line fuzz campaign), and the byte-identical fixed point is the end-to-end
backstop behind it. Nothing in step 5 lands before the checker does.

### The allocator: Wimmer 2005 minus splitting, on a DAG

- **Formulation** (HotSpot client compiler, VEE 2005): intervals as
  range-lists with holes; use positions with kinds (must-register /
  memory-ok); **fixed intervals** model every constraint — R8/R9 staging
  windows, caller-saved clobbers at each call, permanently-reserved roles
  (R10 allocator, R15 closure env, RBP, RSP) — so the sweep has no special
  cases and the ARM64 retarget is a table entry, not a port.
- **Non-SSA costs us almost nothing** (Wimmer & Franz CGO 2010 assessed):
  SSA's main gift to linear scan is liveness without iterative dataflow — but
  **acyclicity confers that independently**: one backward pass over blocks in
  reverse topological order, no fixpoint. What we must assert instead of
  SSA's single-def invariant: between two defs of the same vreg no path
  carries a live use of the old value (true by construction of
  destination-driven form; the LIR verifier states it).
- **v1 simplifications**: no interval splitting (whole-interval spill with
  second-chance reloads at uses — Traub's lazy stores: store once at def,
  reload per use); splitting returns in v2 only if measured spill counts in
  the 21KB lifted lambdas demand it.
- **Eviction is Belady/furthest-next-use** (per-path optimal in replacements
  on straight-line code, and every linearized path through a loop-free DAG is
  straight-line; on branch divergence take the minimum next-use distance).
  Wimmer's `allocateBlockedReg` is already this.
- **Spill discipline**: one dedicated slot per spilled vreg, **no recycling
  in v1** (frame waste is noise at ~870 bytes/function); recycling returns
  only as checker-validated stack-slot coloring. Spill code goes through the
  reserved staging registers (HiPE's trick — Sagonas & Stenman, SP&E 2003) so
  allocation is strictly one-pass: spill code never re-enters the allocator.
  Rematerialize cheap constants instead of reloading (Briggs), later.
- **Determinism is a correctness requirement here**: the acceptance test is
  byte-identity, so every internal ordering is sorted with ties broken by
  vreg number. A correct-but-nondeterministic allocator is a red gate.
- **Graph coloring rejected** on the merits: the interference-graph build is
  ~72% of allocation time and quadratic; coloring's precision premium is in
  cyclic liveness, which our IR cannot express; HiPE measured linear scan at
  near-coloring quality several times faster and made it their default.
  HiPE's x86 caveat (linear scan mediocre at ~5–6 registers) is the one
  warning: at ~10 allocatable registers we sit between their good and bad
  data points — that is the empirical argument for Belady eviction and
  (eventually) holes.

### Sources

Poletto & Sarkar TOPLAS 1999 · Traub/Holloway/Smith PLDI 1998 · Wimmer &
Mössenböck VEE 2005 · Wimmer & Franz CGO 2010 · Sagonas & Stenman SP&E 2003 +
IFL 2001 (HiPE) · regalloc2 (github.com/bytecodealliance/regalloc2, checker
docs on docs.rs) · Fallin cfallin.org posts 2021-03-15 and 2022-06-09 ·
Rideau & Leroy CC 2010 (xavierleroy.org/publi/validation-regalloc.pdf) ·
Guo/Garzarán/Padua LCPC 2003 (Belady on long blocks) · Braun & Hack CC 2009 ·
Cooper et al. LCPC 2005 (coloring cost crossover) · regalloc2 PR #56
(spillslot reuse bug, caught only as a perf regression).

---

## The cross-cutting theme

All three surveys converged on the same shape without being asked: **validate
the result, not the transformer.** Exhaustive containment tests validate every
interval the prover returns; shadow-trap mode validates every trap it elides;
`ir-check` validates every tree a pass emits; the symbolic checker validates
every allocation the allocator produces — and behind all four, the
byte-identical self-compile validates the whole pipeline. That is the
discipline that lets a no-customers project build an optimizing middle end
without shipping the V8 story.
