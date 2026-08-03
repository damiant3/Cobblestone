# The Middle End

**Status: CLOSED, and as a measured negative.** This line read "Active --
decided 2026-07-14. No code written" until 2026-07-27, by which point the code
was written and the campaign was over. The LIR shipped (steps 1 through 7), the
linear-scan selector is live in the default pipeline, and the question this
campaign existed to answer -- whether named-binding register allocation could
beat the tree emitter *meaningfully* -- was answered NO on this register file.
The closing note is `docs/Designs/Active/Compiler/LIR.md` section 12, which
exists so nobody re-runs the investigation, and step 5 (the ARM64 allocator) went
with the retarget Damian dropped on 2026-07-23. Moved to `Done/` 2026-07-27
(blu).

**Status when it was current:** Active -- **decided 2026-07-14** (see §Decision). No code written.
**Filed:** 2026-07-14 (reek). Revised the same day, second session, after
verifying every load-bearing claim against the code; the corrections are in
§What the code actually says.
**Scope:** this campaign. The ARM64 allocator closes into step 5; the CDX2051 sweep closes in step 1.
**Evidence and full assessment:** `docs/Reference/AiComp/OPPORTUNITIES.md`.
**Literature base (2026-07-14, three commissioned surveys):**
`docs/Reference/MiddleEndLiterature.md` -- the design specifics below cite it
rather than re-arguing it.

---

## The one-paragraph version

Codex has no middle end. `Emit/` is 18,814 of the compiler's 36,389 lines
(**52%**); `IR/` is 2,191 lines and hosts exactly two hardcoded transforms.
Every other optimization is a peephole at the point of emission, and
`docs/Reference/CodegenAnalysis.md` records the ceiling: fib/fact/gcd/sum
plateaued at CL 3400 and twenty subsequent CLs moved them by zero. gcd is +156%
over the F# JIT. **The structural cause:** `IRExpr` (`IR/IRChapter.codex:23`) is
a name-based expression *tree* -- not SSA, not a CFG, no basic blocks, no
instruction list. Linear-scan register allocation and SLP vectorization are not
"add a pass" -- **they are "add an IR level."** This doc records the decision to
add it, and the sequence that gets there without betting the seed early.

---

## Decision (2026-07-14): the flat LIR is committed

**We build the linear IR and the register allocator on top of it. The tree
passes come first, but they are the on-ramp, not a fallback destination.**

The reasoning, stated so it can be attacked:

1. **Steps 1–3 do not touch the gap that motivates the campaign.** The
   benchmark gap is register allocation of named bindings --
   `ArchitectsSketchbook.md` says it in as many words ("the registers the JITs
   win through full linear-scan allocation of named bindings -- the next
   frontier"), and gcd at 17 instructions has no redundancy for CSE/DCE to
   find. Tree passes pay in seed density, deleted bounds checks, and the
   ability to measure. They will not move fib/fact/gcd/sum materially, and
   nobody should present them as codegen-quality progress. If we stop at
   step 3, the headline finding stays exactly as open as today and
   someone re-files this campaign in a year.

2. **"Stop at 3" is only coherent as "don't run the campaign."** The honest
   fork is: either codegen quality is a goal (the public perf page, thirty
   CLs of CodegenAnalysis investment, the JIT comparisons say it is), in which
   case the LIR is the only road that reaches it -- or it is not, in which case
   step 1 (which closes 2.11, a correctness item) is worth doing alone and the
   rest of the campaign should yield to safety-claim work. The middle position
   buys measurement and density but leaves the motivating gap; choosing it
   should be done knowingly, not arrived at by fatigue.

3. **The risk was overpriced and the payoff underpriced** -- both by facts now
   verified in the code (§What the code actually says): the migration surface
   is ~5,900 lines, not 18,814; the LIR needs no SSA and no phi nodes because
   the IR has no loops; the fixed point is a correctness oracle no test suite
   matches; and the same LIR + allocator, placed correctly, closes the ARM64 allocator
   (the ARM64 allocator hole) instead of leaving a third hand-rolled
   allocation scheme to be built later.

**Commitment is to scope, not schedule.** LIR work does not start until three
entry criteria hold:

- **E1.** The pass manager and ablation harness are live (steps 2–3), so LIR
  passes are born switchable and measurable.
- **E2.** The range analysis has shipped *with its soundness fuzz harness* --
  the discipline of "analysis output checked against runtime ground truth"
  proven once on a cheaper analysis before liveness bets the seed on it.
- **E3.** The LIR design doc (shape, placement, migration staging, register-file
  parametrization) is written and Damian has reviewed it.

Damian can veto the commitment by saying so; until then, agents plan against it.

---

## What the code actually says (verified 2026-07-14, second session)

Six corrections to the first draft of this plan. Each was checked against the
tree, not inferred.

**1. There are no loops in the IR -- the range analysis needs no fixpoint and
no widening.** `IRExpr` has no loop constructor (read the full variant list in
`IR/IRChapter.codex:23`); `for` desugars to `list-map`; iteration is
(tail-)recursion, which is *inter*-procedural. An intra-procedural range
analysis over `IRExpr` is structural induction over a finite tree -- it
terminates trivially. ai-comp needed the widening ladder because their HIR has
`ForLoop` with carried values; importing that requirement here was a category
error. Recursive-function *results* already flow through declared bounded
return types (the checker's stage-B door). Consequence: step 1 is
**substantially smaller than first planned** -- transfer functions, an
environment, and branch refinement. No fixpoint, no widening, no memoization
soundness trap. (The moment the LIR represents TCO self-calls as back-edges,
loop machinery becomes real -- that is a LIR-v2 concern, noted in step 5.)

**2. "No IR→IR pass" was false -- two exist.** `fold-constants-in-chapter`
(`IR/Lowering.codex:1178`) and `inline-leaf-calls-in-chapter`
(`IR/Lowering.codex:1570`), both `IRChapter -> IRChapter`, both hardcoded,
both always-on, neither switchable. The pass interface therefore already
exists de facto; step 2 is a *promotion*, not an invention, and it registers
two real passes on day one. It also means the ablation harness has its first
experiment waiting: the leaf inliner cannot currently be turned off, so its
interaction with everything downstream has never been measured.

**3. The emitter migration is ~5,900 lines, not 18,814.** Only **6 of 18**
`Emit/` files reference `IRExpr` at all. The tree-walking core is
`X86_64.codex` (2,380) + `X86_64Compound.codex` (1,777) + `X86_64Builtins.codex`
(1,701); `IRTextEmitter`/`CodexEmitter` are the TEXT/IR-mode emitters, which a
machine-code LIR never touches. The other ~12,900 lines -- Boot, Helpers,
TextHelpers, IPCHelpers, ListHelpers, ProcessHelpers, IO, Encoder, State,
Chapter, CdxWriter -- emit fixed runtime code or encode bytes, and every one of
them is reused unchanged beneath a LIR→x86 selector. The LIR is a serious
project; it is not a rewrite-the-emitter project.

**4. The plug boundary is the under-priced argument, in both directions.** The
wire format to all 53 plugs is the *tree* IR as S-expressions
(`codex/plugs/common/IRTextParser.codex` -- "the wire format the main compiler
emits in IR mode"). So: (a) every tree-level pass runs *before* serialization
and improves the input to **every plug and both native backends for free** --
no plug changes, no wire change; (b) the LIR must **not** live inside
`Emit/` -- placed there it fixes x86 only and leaves the ARM64 allocator to hand-roll a
third allocator. It belongs in the compiler's `IR/` quire as a citable chapter
(plugs already cite compiler chapters -- `IRTextParser` cites `Codex chapter
Text`), parametrized over a register-class table, so the ARM64 and RISC-V
plugs lower tree→LIR plug-side and reuse the same allocator. The wire format
does not change.

**5. The LIR needs no SSA and no phi nodes.** Because the tree has no loops
(correction 1), flattening it yields a **DAG** -- the only join points are
if/match arms, and those are handled destination-driven: both arms write the
same virtual register, exactly the philosophy the current emitter already
uses. Linear scan does not require SSA; it requires live intervals over a
linear order, and intervals that merge at joins are still intervals. ai-comp's
`phi-elimination` exists because they built SSA over loops; we skip the whole
tier. Recursion stays function calls in LIR v1 (TCO remains the emitter's
job); representing TCO as back-edges is v2 and only then does loop analysis
enter.

**6. Passes need a stated memory discipline (Rule 8).** Every tree pass is a
rewrite that allocates a new tree on a phase deck with no GC. N passes × a
~115 MB LOWER-phase tree is real address-space pressure even demand-paged, and
pass *order* interacts with heap (DCE first shrinks what every later pass
copies). The pass-manager design must state the pattern -- per-pass bivy
scratch with survivors deck-copied (the reservation-copy pattern), or in-place
rewrite where sound -- and `MEASURE` mode must report per-pass HWM so every
pass CL can carry its memory verdict honestly.

---

## The sequence

```
 1. range analysis        tree     low risk   closes 2.11, deletes traps    [A1 ∥ B]
 2. pass manager          tree     low risk   promotes the 2 existing passes [B ∥ A1]
 3. ablation harness      infra    no risk    seed+bench+time+fixed-point grid
 4. simplify / CSE / DCE  tree     low-med    seed density; proves the frame
 5. flat LIR + linear scan  new IR  high      DECIDED -- entry criteria E1-E3
 6. SLP vectorization     LIR      high       after 5; gives 3.3 (AVX) a reason
```

Steps 1 and 2 are **independent** -- the range analysis is not a pipeline pass
(it is an analysis with two consumer sites, checker and emitter), so it does
not wait for the pass manager. Two agents can run them concurrently.

### Step 1 -- range analysis (workstream A)

**A1 (checker, diagnostics-only).** The checker's `aexpr-proven-range`
(`Types/TypeCheckerInference.codex:742`) already threads an environment with
recorded local ranges. What it lacks: branch refinement (`AIfExpr` takes the
plain union of arms -- it cannot see `if i < n then …`), and depth in the
transfer functions (the apply-doors are shallow). Add refinement on
`<`/`<=`/`==` against known-range operands, deepen the doors. Closes
the CDX2051 false-positive sweep on the `-FW` surface.
Failure mode is a loud wrong diagnostic, not a miscompile. No seed semantics.
2–4 CLs.

**A1 mechanism (from the literature):** branch refinement is **scoped
environment narrowing** -- ABCD's π-nodes collapsed onto lexical scope. On
`if i < e`, push `env[i] ∩ [lo(i), hi(e)−1]` for the then-arm, the complement
for the else-arm, pop on exit; a fact never escapes the arm that justifies it,
and a fact derived from a trap never justifies removing that same trap. Plus
SCCP's two-liner: if the ranges already decide the condition, take only the
surviving arm. The kernel ships **with its exhaustive containment test**
(LLVM's ConstantRangeTest discipline): enumerate every interval pair at width
4, evaluate the concrete op over every member *under codex-vm* (so the oracle
is the runtime's own semantics), assert every concrete result is contained.
A transfer function without its exhaustive test does not merge.

**A2 (emitter, codegen -- gated).** The emitter's `ir-expr-proven-range`
(`X86_64Compound.codex:847`) threads **no environment**: `IrLet` discards the
bound value's range, `IrName` is unmatched. A named local has no provable
range, so `let x = 5 in rec.field <- x` pays a compare, branch, and trap on a
compile-time constant at every bounded narrow-store in the tree. The fix is
the shared kernel with an environment seeded from declared types, and it is a
**two-pass seed change whose hazard is trap removal**: an unsound interval
silently truncates a narrow-store. Three defenses ship with it, in order:
(1) the exhaustive kernel test already standing from A1; (2) a width-64
randomized containment layer with adversarial endpoints (lo±1, hi±1, 0, −1,
i64-min/max -- every documented real-world range bug lived at an endpoint);
(3) **shadow-trap mode** (V8's aborting-CheckBounds transplanted): a compile
flag under which every elided trap is emitted anyway as an aborting assert
tagged prover-unreachable, and the gate runs under the flag in test builds --
a wrong proof becomes a loud trap in the battery, not a silent truncation in
a seed. Measure the payoff as the count of elided checks (CDX2079) across the
seed and the seed byte delta.

**Out of scope, stated so it is not drifted into:** loop fixpoints and
widening (no loops -- correction 1); interprocedural inference beyond declared
bounded signatures; strides and known-bits (noted in the literature doc,
deferred until the interval kernel is boring).

### Step 2 -- pass manager (workstream B)

Promote `fold-constants-in-chapter` and `inline-leaf-calls-in-chapter` into a
registered-pass pipeline: an `IRChapter -> IRChapter` interface, a named
registry, an ordered pipeline as data, per-pass enable/options. Wire a
`passes=` knob through the compile mode line (precedent: `decks=N`) and a
`-Passes` flag on `compile.ps1`. First CL is a pure refactor -- byte-identical
fixed point required, no behavior change. Then the knobs. The memory pattern
(correction 6) is decided and documented here, and `MEASURE` grows per-pass HWM.

### Step 3 -- ablation harness -- **DONE** (`build/ablate.ps1`, log in `docs/Reference/AblationRuns.md`)

A script over the pass config that scores each configuration on seed size,
bench static counts (`bench/compare.ps1` mechanism), and self-compile wall
time. It does **not** run a per-config fixed point -- only the shipping
default is gate-verified, and the harness says so in its own header.
Multi-metric is the contract -- CL 3695 cut `sum` 26→14 while adding 2,373
seed bytes; score on one metric and the search wrecks the others.

**Run 1 answered the first experiment (inliner × const-fold), and the answer
was not the one the literature primed us for.** The two legacy passes are
**perfectly additive, not coupled**: `fold-constants` is −2,248 seed bytes
and moves no benchmark; `inline-leaf-calls` is +96 bytes (code duplication --
what inlining *is*) and is the *entire* benchmark win (gcd 19→11, collatz
25→14). Each contributes the same whether or not the other runs, exactly.
The predicted coupling -- that re-folding after the inliner would recover the
96 bytes as "uncollected residue," the MAD shape -- was **tested and
refuted**: byte-identical. **We have no coupled pass pair today.** The
methodological point stands regardless, and is now paid for rather than
borrowed: the grid is run, not reasoned, and the first thing it did was kill
a plausible theory. Every pass added below gets its row, alone and in
combination. Seed-size figure to beat: **2,294,622**.

### Step 4 -- tree passes (workstream D, after B)

Reshaped by the survey (GHC Core is the closest production analogue to our IR
that exists; its architecture transfers wholesale -- literature doc §2). Not
nanopass granularity, and **not e-graphs** (Cranelift's own retrospective:
e-class size 1.13, 7–8% compile-time cost, extraction NP-hard -- the wins were
hash-consing and eager rewriting, which we take without the graph). One CL
each, through the gate with a memory/time verdict:

1. **`ir-check`** -- a reusable IR validator (scope discipline under
   shadowing, type/effect agreement), interleavable anywhere in the pipeline,
   on in debug builds. Converts "silent corruption three stages later" into
   "pass N emitted malformed IR." Nanopass's one transferable idea.
2. **The occurrence analyser** -- a cheap pre-pass annotating every binder:
   dead / occurs-once (and where) / occurs-many, loop breakers for recursive
   groups. Makes DCE a non-pass (dead lets drop during simplification), makes
   occurs-once inlining unconditional and safe, and subsumes the leaf
   inliner's bookkeeping. Highest leverage-per-line in the campaign. **SHIPPED**
   (`IR/Occurrence.codex`, opt-in `occ-report` pass, driven and tested over
   real lowered IR): once-per-if-arm classifies **Many** (arms summed, so the
   inliner can never duplicate work into both arms), a shadowed name's inner
   and outer bindings classify independently. Filed on the way
   out -- CDX codegen has no dead-code elimination, so an unreachable helper is
   dead weight in the seed (the pruner exists but only for IR-text/plug output).
3. **The simplifier** -- ONE traversal carrying (substitution, in-scope set,
   occurrence info), applying all local rewrites at once: constant folding
   (absorbs the existing hardcoded pass), let-inlining per occurrence info,
   case-of-known-constructor, if/match-of-match commuting, algebraic
   reassociation, IR-level strength reduction. Shadowing by the rapier
   (rename only on in-scope collision). Termination by tick budget: iterate
   to a zero-tick pass, cap at 2–4 iterations, error loudly on a
   size-proportional global budget. **Heap-restore between iterations** --
   the iteration cap is also the peak-memory bound (Rule 8). Per-rule tick
   counts logged per gate run: the ablation instrument, and a fixed-point
   break diffs to "rule X fired 412 times, was 0."
4. **CSE, separate and late** -- scoped hash-cons over *pure, trap-free*
   subtrees, reusing a **dominating occurrence only, no hoisting** (commoning
   across arms forces work the taken path never wanted, and for us can change
   whether a trap fires). Scope-stack push/pop at binders and arms makes this
   dominator-based value numbering for free on a tree. Purity is visible in
   `CodexType` effect rows -- but resolve explicitly that "pure" does not yet
   imply trap-free (the safety-gates bug class) before enabling it on
   arithmetic. Never merge across `IrApply` of effectful type, `IrAct`,
   `IrHandle`, `IrTry`, or `IrFieldStore`; mutable records make store/load
   forwarding an alias problem -- defer forwarding entirely.

Shared by all of it: **smart constructors** (eager local simplification +
hash-cons lookup at node construction -- the one aegraph idea worth taking).
Expected benchmark movement: **near zero, and say so** -- the value is seed
density, deleted redundancy the emitter cannot see, and proving the pass
frame before step 5 bets the seed on it. All of it reaches the 53 plugs free
(correction 4).

### Step 5 -- the LIR (workstream C; DECIDED, entry-gated on E1-E3)

**The design doc is written: `docs/Designs/Active/Compiler/LIR.md` (2026-07-15).**
It expands the constraints below into implementable detail -- LIR data
structures, the tree→LIR lowering, the verifier-first algorithm, the Wimmer
allocator sweep, the migration ratchet, and a Limitations section -- and records
E1/E2 as MET and E3 as pending Damian's review. The summary below stays as the
plan-of-record; the doc is the specification.

Design doc first, and the design constraints are already known:

- **Shape:** per-function, basic blocks over virtual registers, **non-SSA,
  destination-driven** (correction 5). Flattening = number the values, resolve
  shadowed `Text` bindings to fresh vregs -- trivial without back-edges.
- **Placement:** a chapter in the compiler's `IR/` quire, citable by plugs;
  register file as data (x86-64 first, ARM64 second -- **the ARM64 allocator closes
  into this workstream**, not after it).
- **Migration staging:** dual emitters behind a per-function dispatch. v1
  handles bodies within a construct whitelist (literals, names, binary, if,
  let, apply -- the pure-arithmetic shapes, which are exactly the benchmark
  shapes); everything else falls back to the tree emitter. Coverage ratchets
  up construct by construct, one CL at a time, each through the gate, each
  two-pass. The fallback is removed only at total coverage plus soak.
- **The checker comes first -- before the allocator, not after** (regalloc2's
  discipline; verified ancestor Rideau & Leroy CC 2010; literature doc §3). A
  ~200-line symbolic validator: `Map<Location, VRegSet>` with **spill slots
  first-class alongside registers** (the property that would have caught the
  2026 spill-slot corruption at the guilty reload, same compile); inserted
  moves copy sets; each use checks `vreg ∈ state[location]` and errors naming
  function+instruction+operand; defs scrub stale copies; calls clobber
  caller-saved; joins intersect. On our loop-free DAG it is one pass in
  topological order -- no fixpoint. **Run on every allocation of every compile,
  unconditionally**: the self-compile is the fuzz corpus, the fixed point the
  backstop.
- **The allocator: Wimmer 2005 minus splitting.** Intervals as range-lists
  with holes, built in one backward reverse-topo liveness pass (acyclicity
  gives SSA's main gift for free); use positions with kinds; **fixed
  intervals** model everything special -- R8/R9 staging windows, caller-saved
  clobbers at calls, reserved roles (R10/R15/RBP/RSP) -- so the sweep has no
  special cases and ARM64 is a table entry. Belady/furthest-next-use
  eviction; lazy stores (store once at def, reload per use through the
  reserved staging registers, so allocation is strictly one-pass -- HiPE's
  no-iteration discipline); **one spill slot per vreg, no recycling in v1**
  (recycling returns only as checker-validated slot coloring). Splitting is
  v2, gated on measured spill counts in the 21KB lifted lambdas.
- **Determinism is a correctness requirement**: byte-identity is the
  acceptance test, so every internal ordering is sorted, ties broken by vreg
  number. A correct-but-nondeterministic allocator is a red gate.
- **The oracle stack:** the checker on every compile, fixed point
  (byte-identical, both directions), the poison build at milestones, and the
  LIR verifier asserting the destination-driven invariant (between two defs
  of a vreg, no path carries a live use of the old value -- the non-SSA
  replacement for the single-def guarantee).
- **v2, filed not built:** TCO self-calls as LIR back-edges (brings loop
  analysis in), interval splitting, stack-slot coloring, rematerialization of
  cheap constants, and instruction scheduling only if an in-order target
  (Thumb-2) ever demands it.

### Step 6 -- SLP (workstream E, after C)

Larsen-Amarasinghe over LIR straight-line code. Only after step 5 -- packs need
a linear order and a DDG, and AVX/AVX2 only earns its keep once
something generates lanes. Not designed further here.

---

## Coordination

- **Parallel now:** A1 ∥ B (independent -- correction on sequencing above).
  Then D (after B) ∥ A2 (after A1). The step-5 design doc can be written
  during D; step-5 implementation is **single-owner** (compiler invariants,
  one thing at a time), with a second agent joining for the ARM64 retarget
  after x86-64 v1 converges.
- **Every CL:** gate is `build/build.ps1` only (no battery -- it halts the
  agent); AgentGrid token for gate+submit; seed bundled in the same CL;
  codegen changes converge two-pass before install; memory + time verdict in
  the review; and every pass ships its doc with a **Limitations** section
  stating what it provably does not do (ai-comp's habit, worth stealing).
- **Clean-room:** `docs/Reference/AiComp/` is unlicensed. Read
  `PROVENANCE.md` before writing a line. Algorithms from the literature
  (Poletto-Sarkar linear scan, Larsen-Amarasinghe SLP); never the Python.

## Success criteria

- **Step 1:** 2.11 closed; CDX2079 elision count and seed delta reported for A2.
- **Steps 2–3:** the ablation table exists, is in a doc, and the two legacy
  passes are switchable.
- **Step 4:** per-pass seed delta reported; fixed point green throughout;
  benchmarks honestly reported as unmoved.
- **Step 5:** gcd/sum/locals gap to the JITs closed to the CodegenAnalysis
  target (~+55% or better on gcd); seed size reduced; 3.1 closed by the same
  allocator; self-compile time regression bounded and reported.

## What not to build

- **The instruction scheduler.** Their VLIW has no same-bundle RAW forwarding;
  x86-64's out-of-order core does it in hardware. File for Thumb-2 (3.5).
- **`mad-synthesis` as such.** No scalar MAD on x86-64; the reassociation that
  feeds it belongs in simplify, the fusion pass does not.
- **`assume_local_memory`.** A source-level UB contract. Codex does not ship
  trust-me escape hatches; the SROA win is earned with a proof or not at all.

## The license constraint

`docs/Reference/AiComp/` is **unlicensed** (no LICENSE file = all rights
reserved). We may read the design docs and reimplement the algorithms
clean-room -- every one is public, decades-old literature. We may **not** copy
the Python or transliterate it line-by-line into Codex. Full terms:
`docs/Reference/AiComp/PROVENANCE.md`. Read it before writing a line.
