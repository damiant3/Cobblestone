# The Flat LIR and Linear-Scan Register Allocation

**Status:** The codegen-quality campaign is **closed** -- see §12 for the closing
note and the measurement that closes it. LIR-1..7 shipped (representation,
lowering through constructor-pattern match, structural + allocation verifiers,
liveness, and the linear-scan allocator; §10), and **the x86 selector is live in
the default pipeline** and verified on every compile. This header said the
selector was "the next and first codegen-changing step" until 2026-07-23, which
had been false for weeks. The design remains Active because §12 closes the
*quality* frontier and not the three open coverage and verification items it
names. Satisfies entry criterion **E3**
of the middle-end campaign (`docs/Designs/Active/Compiler/MiddleEnd.md` §Decision):
the LIR design doc that Damian reviews before step-5 implementation begins.
**Filed:** 2026-07-15 (reek). **Scope:** this campaign, step 5; **the ARM64 allocator
closes into this workstream** (the ARM64 allocator is a register-file table
entry here, not a separate build).
**Literature base:** `docs/Reference/MiddleEndLiterature.md` §3 (register
allocation). The algorithms are decades-old and public; the clean-room
constraint of `docs/Reference/AiComp/PROVENANCE.md` applies -- reimplement, never
transliterate.

---

## 1. Why this exists, in one paragraph

The benchmark gap is register allocation of named bindings. `docs/Reference/
CodegenAnalysis.md` records the ceiling -- fib/fact/gcd/sum plateaued at CL 3400
and twenty subsequent peephole CLs moved them by zero -- and `docs/Architects
Sketchbook.md` names the frontier in as many words: "the registers the JITs win
through full linear-scan allocation of named bindings." The tree passes (WS-D,
slices 1-3) bought seed density and one 15% dent in the `locals` benchmark from
copy propagation, but they cannot reach the gap: gcd at 11 instructions has no
redundancy left to fold, and its cost is entirely which values live in which
registers across the recursion. That is an allocation decision, and the current
emitter does not make it globally -- it makes it greedily, one expression at a
time. This document specifies the intermediate representation and the allocator
that close the gap, and the staging that gets there without betting the seed on
day one.

---

## 2. What the emitter does today (and why it caps out)

The x86-64 emitter (`codex/compiler/Emit/X86_64.codex` and its siblings) is a
recursive tree walker over `IRExpr`. Register assignment is local and greedy,
with no liveness analysis across a function body:

- **Temps** rotate through `alloc-temp`: a six-deep cycle over RAX, RCX, RDX,
  RSI, RDI, R11 (`docs/ArchitectsSketchbook.md`, Register Convention). A temp is
  chosen by position in the rotation, not by what is live.
- **Named locals** (`let` bindings, parameters) are handed the callee-saved
  registers RBX, R12, R13, R14 in first-come order by a monotonic `next-local`
  counter; the fifth and later locals **spill to the stack** at `[RBP - (32 +
  8n)]`. There is no notion that two locals with disjoint live ranges could
  share a register, nor that a heavily-used local should out-rank a
  rarely-used one for a register.
- **Binary operands** stage through R8/R9/R15 to dodge the RAX/RDX that
  idiv/imul clobber (`X86_64.codex:640` region).
- `peak-local` and `peak-spill` are tracked to size the frame, and
  `restore-locals` scopes the name→register table lexically (the WS-2.22 fix),
  but none of this is *allocation* in the global sense -- it is a deterministic
  assignment order.

The consequence is exactly the CodegenAnalysis finding. A function with more
live values than callee-saved registers spills in `next-local` order, not in
value-of-keeping order; a value used once and a value used ten times compete on
arrival order. Linear scan replaces arrival order with live-interval order and
furthest-next-use eviction -- the JITs' win, and the thing a tree cannot express
because it has no linear order and no basic blocks to compute liveness over.

**What does NOT change.** Only 6 of 18 `Emit/` files walk `IRExpr` (X86_64 2,380
+ X86_64Compound 1,777 + X86_64Builtins 1,701 lines -- `docs/Designs/Compiler/
Active/MiddleEnd.md` correction 3). The other ~12,900 lines -- Boot, Helpers,
Text/List/IPC/Process helpers, IO, Encoder, State, Chapter, CdxWriter -- emit
fixed runtime code or encode bytes and are reused unchanged beneath a LIR→x86
instruction selector. This is a new IR level, not a rewrite of the emitter.

---

## 3. The LIR shape

Per-function, a list of **basic blocks** over **virtual registers** (vregs).
**Non-SSA, no phi nodes, destination-driven.** The justification is structural
and load-bearing (`MiddleEnd.md` correction 5): the tree IR has **no loop
construct** -- `for` desugars to `list-map`, iteration is tail recursion, which
is inter-procedural -- so flattening a function body yields a **DAG**, never a
cyclic CFG. The only join points are the arms of `if`/`match`, and those are
handled the way the current emitter already handles them: both arms write the
**same destination vreg**. Linear scan needs live intervals over a linear order,
not SSA; intervals that merge at a join are still intervals. We skip the entire
SSA/phi-elimination tier that ai-comp's HIR needs because it built SSA over
loops and we have none.

### 3.1 Data structures (sketch, to be refined in code review)

```
LirValue =                          -- an operand
 | LvReg (VReg)                     -- a virtual register
 | LvImm (Integer)                  -- an immediate
 | LvFixed (PhysReg)                -- a pinned physical register (see fixed intervals)

LirInsn =
 | LiMove (dst : VReg) (src : LirValue)
 | LiBin (op : LirOp) (dst : VReg) (l : LirValue) (r : LirValue)
 | LiCall (dst : VReg) (target : Text) (args : List LirValue)   -- args pre-placed by the selector
 | LiCmp (l : LirValue) (r : LirValue)
 | LiBranch (cond : LirCond) (then-block : BlockId) (else-block : BlockId)
 | LiJump (block : BlockId)
 | LiRet (src : LirValue)
 | LiLoad / LiStore / ...           -- memory, record field access, etc.

LirBlock = record { id : BlockId, insns : List LirInsn, span : SourceSpan }
LirFunc  = record { name : Text, params : List VReg, blocks : List LirBlock,
                    n_vregs : Integer, span : SourceSpan }
```

Placement: a chapter in the compiler's **`IR/` quire** (`codex/compiler/IR/
Lir.codex` or similar), NOT under `Emit/`. This is the decision that closes
the ARM64 allocator rather than leaving it: the plug wire format is the tree IR as
S-expressions (`codex/plugs/common/IRTextParser.codex`), plugs already cite
compiler chapters, and if the LIR and its allocator live in `IR/` the ARM64 and
RISC-V plugs can lower tree→LIR plug-side and reuse the same allocator over a
different register-file table. Put it under `Emit/` and it fixes x86 only and
leaves 3.1 to hand-roll a third allocation scheme. **The register file is
data** -- a table of allocatable registers, caller/callee-saved sets, and
reserved roles -- so a new target is a table entry, not a port.

### 3.2 Lowering tree → LIR

A structural walk of `IRExpr`, allocating a fresh vreg per intermediate value
and threading a destination:

- **`IrIntLit` / literals** → `LvImm`, or `LiMove dst (LvImm v)` when a
  destination is forced.
- **`IrName`** → the vreg the binding was lowered to (a name→vreg map threaded
  lexically, the same discipline `restore-locals` already enforces; shadowed
  `Text` bindings resolve to distinct fresh vregs -- trivial without back-edges).
- **`IrBinary`** → lower both operands to vregs/imms, emit `LiBin`.
- **`IrIf` / `IrMatch`** → destination-driven: allocate a result vreg, lower the
  condition, emit `LiBranch`, lower each arm into its own block writing the
  **same** result vreg, both arms `LiJump` to a join block. No phi -- the result
  vreg is defined on both paths, which is exactly the invariant the verifier
  checks (§4).
- **`IrLet`** → lower the value into a vreg, bind name→vreg, lower the body.
- **`IrApply`** (calls) → the selector pre-places arguments per the ABI
  (Codex's fixed convention, `docs/ArchitectsSketchbook.md`) as `LvFixed`
  operands, emits `LiCall`, result in a fresh vreg. **TCO stays in the emitter
  for v1** (it is not a LIR back-edge yet -- that is v2, §9).

Everything the current tree emitter does for effects, records, handlers, and the
runtime-helper calls is unchanged; those lower to `LiCall`s and loads/stores. The
LIR is about the *scalar/integer register-pressure* core -- the benchmark shapes
(literals, names, binary, if, let, apply) -- and everything outside the migration
whitelist (§8) falls back to the tree emitter until its lowering is written.

---

## 4. The verifier comes first -- before the allocator, not after

This is the single highest-priority instruction from the register-allocation
survey (`MiddleEndLiterature.md` §3; regalloc2's discipline; the verified
ancestor is Rideau & Leroy, CC 2010). **No allocator code lands before the
verifier does.**

The verifier is a symbolic checker over the *allocated* LIR: given the
allocator's output (each vreg assigned a physical register or a spill slot, with
inserted moves), it re-derives what each location holds and asserts every use
reads the right vreg. State is `Map<Location, VRegSet>` where a **Location is a
physical register OR a spill slot** -- spill slots are first-class, which is the
property that would have caught the 2026 regalloc2 spill-slot-recycling
corruption at the guilty reload in the same compile. On our loop-free DAG:

- Walk blocks in topological order; **no fixpoint** (acyclicity).
- An allocator-inserted **move** copies the source location's vreg-set to the
  destination location.
- An original instruction's **use** at location L must have `vreg ∈ state[L]`,
  else the verifier errors naming **function + instruction + operand**.
- A **def** scrubs the defined vreg from every location, then sets the def
  location to exactly `{vreg}`.
- A **call** clobbers every caller-saved location (removes their vreg-sets).
- A **join** intersects the incoming states pointwise.

~200 lines, and it **runs on every allocation of every compile, unconditionally**
in the first implementation. The self-compile is a 36K-line fuzz corpus -- every
build exercises it -- and the byte-identical fixed point is the end-to-end
backstop behind it. It is cheap enough to leave on: one topological pass, set
operations over a handful of locations per instruction.

The **LIR-level invariant** the verifier also asserts, the non-SSA replacement
for SSA's single-def guarantee: between two defs of the same vreg, no path
carries a live use of the old value. True by construction of destination-driven
lowering; stated so a lowering bug that violates it is caught here, not three
stages later in the bytes.

---

## 5. The allocator: Wimmer 2005 minus splitting, on a DAG

Formulation from Wimmer & Mössenböck (HotSpot client compiler, VEE 2005),
reduced by our acyclicity (`MiddleEndLiterature.md` §3):

- **Liveness** is one backward pass over blocks in **reverse topological order** --
  no iterative dataflow, because there are no back-edges (Wimmer & Franz, CGO
  2010: SSA's main gift to linear scan is liveness-without-fixpoint, and
  acyclicity confers that independently).
- **Intervals** are range-lists with holes; **use positions** carry a kind
  (must-register / memory-ok).
- **Fixed intervals** model every special constraint so the sweep has no special
  cases: the R8/R9 binary-staging windows, the caller-saved clobbers at each
  `LiCall`, and the permanently-reserved roles -- **R10** (the bump allocator),
  **R15** (closure env), **RBP**, **RSP** -- are pre-occupied intervals the sweep
  routes around. This is also what makes ARM64 a table swap: its reserved roles
  and caller/callee sets are a different table, same sweep.
- **Eviction is Belady / furthest-next-use** (per-path optimal on straight-line
  code; every linearized path through a loop-free DAG is straight-line; at a
  branch, take the minimum next-use distance). Wimmer's `allocateBlockedReg` is
  already this.
- **Spill discipline v1**: no interval splitting. Whole-interval spill with
  **lazy stores** (Traub/Holloway/Smith 1998; store once at the def, reload per
  use through the reserved staging registers), so **allocation is strictly
  one-pass** -- spill code never re-enters the allocator (HiPE's no-iteration
  discipline, Sagonas & Stenman 2003). **One dedicated slot per spilled vreg, no
  recycling** -- frame waste is noise at ~870 bytes/function, and recycling
  returns only later as checker-validated stack-slot coloring.
- **Rematerialization** of cheap constants instead of reloading: later (Briggs).

Graph coloring is rejected on the merits (`MiddleEndLiterature.md` §3): the
interference-graph build is ~72% of allocation time and quadratic, and coloring's
precision premium lives in cyclic liveness our IR cannot express. HiPE measured
linear scan at near-coloring quality several times faster and defaulted to it.
Their one caveat -- linear scan is mediocre at ~5-6 registers -- is the reason we
take Belady eviction seriously: at ~10 allocatable integer registers we sit
between HiPE's good and bad data points.

---

## 6. Determinism is a correctness requirement

The acceptance test is byte-identity (the fixed point). Therefore **every
internal ordering in the allocator is fully sorted, ties broken by vreg number**:
interval start positions, eviction candidates at equal next-use distance, spill-
slot assignment order. A correct-but-nondeterministic allocator is a **red gate**,
not a passing one -- it would break the fixed point even while producing valid
code. This is stated up front because it is the easiest invariant to violate by
accident (iterating a set in hash order) and the most expensive to debug after
the fact (an intermittent fixed-point break).

---

## 7. The oracle stack

Four independent checks, weakest-to-strongest, because the whole campaign's
method is *validate the result, not the transformer* (`MiddleEndLiterature.md`,
the cross-cutting theme):

1. **The verifier** (§4) on every allocation of every compile -- catches a wrong
   allocation at the guilty instruction.
2. **The LIR structural invariant** (§4, destination-driven single-live-def) --
   catches a wrong *lowering*.
3. **The byte-identical fixed point** (both directions) -- the end-to-end backstop:
   if the allocated code miscompiles the compiler, the self-compile diverges.
4. **The poison build** at milestones -- uninitialized-field dependencies.

The fixed point's known blind spot (a self-consistent miscompile passes it --
the WS-2.22 and the WS-D3-capture lesson) is why 1 and 2 exist: they check
properties the fixed point cannot see.

---

## 8. Migration staging -- the ratchet

Single-owner workstream (compiler invariants, one thing at a time); a second
agent joins for the ARM64 retarget after x86-64 v1 converges.

- **Dual emitters behind a per-function dispatch.** A function whose body lies
  entirely within a **construct whitelist** -- literals, names, binary, if, let,
  apply (the pure-arithmetic shapes, which are exactly the benchmark shapes) --
  is lowered tree→LIR→allocated→x86. Everything else falls back to the existing
  tree emitter, unchanged.
- **The whitelist ratchets up one construct per CL**, each through the gate,
  each two-pass (a codegen change is two-pass: SUT ≠ stage1 on the first build;
  install NewSeed, rebuild, converge). The dispatch predicate is the ratchet: it
  starts tiny and grows only as each construct's lowering + selection is proven.
- **The fallback is removed only at total coverage plus a soak** -- the tree
  emitter stays as the safety net until every construct lowers and the fixed
  point has held across many builds.

The value shows up incrementally: the first ratchet step that covers gcd's shape
is the first CL that can move the gcd benchmark, and every step reports its
per-bench instruction-count delta against the CodegenAnalysis tables via the
ablation harness.

---

## 9. v2 -- filed, not built

- **TCO self-calls as LIR back-edges.** This is what brings loop analysis into
  the LIR (and with it, the widening/fixpoint machinery the tree range analysis
  correctly did *not* need). v1 keeps TCO in the emitter.
- **Interval splitting** -- gated on measured spill counts in the 21KB lifted
  lambdas; whole-interval spill until the data demands it. **It was argued on
  2026-07-19 that the data demanded it early, for a reason other than spills.
  Measurement said no**, and the argument is recorded here rather than in the
  a register because what came out of it is a decision not to do something, which
  is a design note and not an open capability. The argument was that intervals having
  no *holes* blocks `gcd`: a loop-carried value spans the whole body, so
  `lir-scan-avail` masks `diva`/`divb` out of it even where the value is dead
  across the division. That much is true, and the local hole was built and
  works -- `v0` moves into RAX and the allocation verifier accepts it. It buys
  nothing: nine benches byte-identical, and `my-gcd` measured with
  `codex-vm -wcet` goes **158 dynamic instructions to 159**. The dividend is a
  *parameter*, so placing it in RAX costs an entry move and displaces the other
  parameter into a second one, buying back exactly what the division and the
  return saved. **What binds here is the entry moves, not the interval model.**
  Two attempts from the interval side were reverted after measurement; a third
  should start somewhere else.
- **Stack-slot coloring** (spill-slot recycling) -- returns only as a
  checker-validated pass.
- **Rematerialization** of cheap constants.
- **Instruction scheduling** -- only if an in-order target (Thumb-2)
  ever demands it; x86-64's out-of-order core schedules in hardware, so building
  a scheduler for it is `MiddleEnd.md`'s explicit "what not to build."

---

## 10. Entry criteria status (the E-gates)

- **E1** -- pass manager + ablation harness live. **MET** (WS-B1 @7911, WS-B2
  @7948, WS-B3 @7950).
- **E2** -- range analysis shipped *with its soundness fuzz harness*. **MET** by
  WS-A1a (@7921): the interval kernel shipped with a width-4 exhaustive
  containment test, 4,887,840 checks with the concrete oracle being codex-vm's
  own runtime semantics -- the "analysis output checked against runtime ground
  truth" discipline proven once on a cheaper analysis, exactly as E2 requires.
- **E3** -- this document, reviewed by Damian. **MET** -- Damian read and
  approved it on 2026-07-15 ("carry on with the LIR as you suggest"). All
  three entry criteria are now satisfied; step-5 implementation has begun.

**Implementation status.** LIR-1 (this doc's §3 representation + §3.2 lowering
for the straight-line whitelist + §4 structural verifier) shipped as
`codex/compiler/IR/Lir.codex` @8078, opt-in `lir-dump` pass, dead to the
default pipeline. **LIR-2 shipped @8092**: basic blocks + branches, so `if`
lowers (§3.2) -- a result vreg is allocated, the condition lowers into the
current block ending in a fused compare-and-branch, each arm lowers into its
own block that moves its value into the shared result vreg and jumps to a
join, and lowering continues in the join (destination-driven, phi-free, DAG).
`LirFunc` now holds a list of `LirBlock`; the structural verifier (§4)
generalised from a straight line to a topological block walk that intersects
predecessor exit-sets at each join -- proven to fire on a corrupted lowering
(an else-arm move to the wrong vreg surfaced as `use before def` at the join,
the exact cross-arm case a union checker would miss). **LIR-3 shipped
@8108**: `match` over a scalar scrutinee with literal, variable, and wildcard
patterns (§3.2) lowers to a decision chain -- the scrutinee becomes an
operand, each literal arm is `LiCmp sv #lit` ending in a conditional branch
to its body block or the next test, and a variable/wildcard arm (or the last
arm, which an exhaustive match reaches only when it must match) is
unconditional; every arm moves its value into one result vreg and jumps to
the join, so the verifier sees the result defined on every path. It reuses
LIR-2's blocks with no new instruction, verifier, or dump code; the literal
value comes from `lit-text-to-integer`, exactly as the tree emitter's
`emit-pattern` derives it. Or-patterns lower (the parser expands them to
one same-body branch per alternative); constructor patterns (a tag load),
vector patterns, and real guards decline to the tree emitter. The verifier
was re-proven to fire on a corrupted match lowering (an out-of-range result
vreg surfaced as `dst vreg out of range` plus an undefined result). All opt-in
`lir-dump`, byte-identical default, one-pass. **LIR-4 shipped @8138**:
`match` on constructor patterns, the first LIR memory instruction
(`LiLoad`). A tested constructor arm loads the variant tag from offset 0 of
the scrutinee pointer, compares it against the constructor's declaration
index, and branches; the arm's body binds each variable field with a
`LiLoad` at its packed offset and width before lowering the body. Only
variable and wildcard sub-patterns lower; a nested constructor, literal, or
vector sub-pattern, a real guard, or a scrutinee type that did not resolve
to a `SumTy` declines the whole function to the tree emitter. The variant
layout (tag walk, field width/signed, packed offset) is reimplemented
target-independently in `Lir.codex` over the shared Types-quire width table
(`bounds-to-hw-width`), so the LIR drags in no x86 chapter and a plug can
reuse it. This is also the increment that moved the `lir-dump` report from
the pre-RESOLVE pass pipeline to a post-RESOLVE hook in `opening.codex` over
the final `cdx-chapter`: a constructor pattern needs its scrutinee's
resolved `SumTy`, and every existing scalar dump is byte-identical after the
move (verified against the real compiler; seed 89BF910D). **LIR-5 shipped
@8158**: backward liveness over the block DAG (§5's "one backward pass over
blocks in reverse topological order") -- the input the linear-scan allocator
needs. A single sweep with no fixpoint (the DAG has no loops): the transfer
over an instruction kills the def then adds the uses, a block's live-out is
the union of its successors' live-in, and the one exit block seeds its
live-out with the function result so the return value stays live to the end.
Live-in is stored by block id and the sweep runs blocks in reverse list
order (topological), so successors are always already computed. It is
analysis only -- nothing is allocated -- and `-Passes lir-dump` now prints
one `LIVE <def>: bN in {..}` line per lowerable function beside the `LIR`
dump; the whitelist's live sets are recorded in `codex/test/lir-check.lir-expected`
(e.g. `if-operand` keeps `v0` live across both arms into the join, exactly
the interference the allocator must see) -- **recorded, not pinned: no
harness reads that file, so nothing fails when the dump changes.** See
That gap is closed by `build/lir-dump-test.ps1`. **LIR-6 shipped @8172**: the
**allocation verifier** (§4), the checker that stands before the allocator so
the allocator is validated from its first line. Given each vreg an assigned
`Location` (physical register or spill slot -- slots first-class from the
start), it re-derives what each location holds along every path
(`Map<Location,VRegSet>`, here specialised to a single occupant per location
since the LIR is single-assignment per path and this increment inserts no
moves; a join intersects to "same vreg on both edges, or unknown") and asserts
every use reads its vreg from its assigned location. A trivial bijective
register assignment (vreg i -> register i, declines over a register budget)
drives it over the real lowered whitelist, one `ALLOC <def>: v0->r0 ...
[alloc-check: ok]` line per function. The verifier was **proven to fire, and
precisely**: corrupting the allocator to map every vreg to r0 produced
violations exactly where live ranges overlap (`sl-add`, `sl-nest` with four,
`if-operand` where `v0` is clobbered before its join use) while correctly
ACCEPTING `sl-mix`/`if-min`/`if-nest` where all-to-r0 is a valid sequential
reuse -- a checker that distinguishes safe reuse from unsafe, not one that
rejects everything. **LIR-7 SHIPPED @8184 (main), seed D9143476**: the Wimmer
linear-scan allocator itself (`Section: Allocator`). `lir-build-intervals`
computes each vreg's live range as a single `[start, fin]` over a flat
instruction numbering (blocks in list order = topological) -- a conservative
interval (no holes; over-states liveness, never under; the allocation verifier
backstops it), `start = -1` for parameters and `fin` extended to one past the
last instruction for the result. `lir-scan-step` is the sweep: expire the active
intervals whose `fin` is strictly before the new `start` (touching intervals get
distinct registers -- conservative, no read-then-overwrite assumed), assign the
lowest-numbered free register, else spill by furthest-next-use (Belady) -- the
active interval of greatest `fin`, ties by greatest vreg; if that victim reaches
further than the new interval it is evicted to a fresh slot and the new interval
takes its register, otherwise the new interval spills itself. Whole-interval
spill, one dedicated slot per spilled vreg, no recycling; `lir-alloc-nregs = 4`
(the callee-saved local count). It replaced the trivial bijective placeholder and
fixed a latent bug the placeholder masked -- the allocation verifier sized its
occupant map to `nregs` alone, so a spill-slot index overflowed; it is now
`nregs + lir-assign-max-slot`. Because the allocation is whole-interval and
inserts NO moves, each vreg holds one location for its whole life and the
verifier's single-occupant state (LIR-6) remains correct; a per-location set
becomes necessary only with interval splitting or the selector's reloads (v2,
section 9). Checked once by hand, and only once: `.lir-expected` was an exact
match to the real `-Passes lir-dump` run at the time it was written --
register reuse (`sl-mix v2->r0`, `sl-nest v4->r0`, ...) and, for `sl-wide`
(five parameters over four registers), spill slots (`v3->s1 v4->s0`, both an
eviction-spill and a self-spill), every function `[alloc-check: ok]`. **That
was a measurement, not a guarantee, and for a long time nothing re-ran it.**
`build/lir-dump-test.ps1` does now, and `build/lir-a64-test.ps1` runs the same
corpus under two non-x86 register files. Neither is in `build.ps1`, so both are
diagnostics rather than gates: read a divergence, do not reflexively re-record.
Determinism is enforced as the design requires (intervals
sorted by start then vreg, lowest-free register, furthest-then-highest-vreg
victim). Still opt-in `lir-dump`, byte-identical default, ONE-PASS (no `Emit/`
file touched). **The x86 selector then shipped behind the per-function dispatch
(§8)** -- fixed intervals (reserved R10/R15/RBP/RSP, call-clobbers, R8/R9
staging) and physical register mapping turned `lir-alloc-nregs` into a real
register-file table, and the selector is live in the default pipeline with both
verifiers standing on the emission path. That was the last step of the campaign;
§12 is where it ends.

---

## 11. Limitations of this design (stated so they are not assumed away)

- **v1 does not touch loops** because the IR has none; the moment TCO becomes a
  back-edge (v2), every "no fixpoint" claim here is void and the loop machinery
  the tree analysis skipped becomes real. This doc's simplicity is *bought* by
  the loop-free property and does not survive without it.
- **The register file is x86-64-shaped first.** *Half-answered 2026-07-19
  (`LirRetarget.md` steps 1-2, CLs 9234, 9246).* The file is data now, and the
  allocator and both verifiers have been run over the `lir-check` corpus under
  two deliberately non-x86 descriptors -- a wide ARM64 shape and a narrow one,
  neither with a division-fixed pair -- clean on all 28 definitions. For the
  allocator and the verifiers, "a table entry" is measured. **The suspects
  named here are not:** flag-setting comparisons and two-operand forms are
  emitted by the *selector*, which that probe never runs, so they remain open
  and land in step 4. Read the green as scoped to allocation, not to codegen.
  One caution the probe earned: a wider register file exercises *less* of the
  allocator than x86-64's nine, because nothing spills -- a narrow descriptor
  is what reaches the crossing arm, eviction and the spill path.
- **The selector's coverage and its margin, measured 2026-07-19.** It takes
  **all nine** `bench/codex` functions -- "instruction-neutral" does not mean it
  declines them -- and against the tree emitter it is neutral on seven and one
  instruction ahead on `ack` and `collatz`. Two apparent gaps in the
  `ArchitectsSketchbook.md` table are **not codegen gaps**: `fib`'s +2 over
  C /O2 is a source-shape difference (`if (n<=1) return n` is one compare; the
  Codex source tests `n == 0` then `n == 1`, so it is a different program), and
  `tak`'s spill is genuinely required -- five values each cross a call against a
  callee pool of four. Adding R15 to that pool is **unsound**, not merely
  unattempted: `X86_64Builtins.codex:281` writes it unsaved (`mov r15, r11`) at
  closure-call sites, so it is not preserved across a call.
- **~10 allocatable integer registers is the hard case for linear scan** (HiPE's
  x86 caveat). Belady eviction is the mitigation, but this design does not
  *prove* it reaches coloring quality at this register count -- that is an
  empirical question the ablation answers after the fact, and if the answer is
  "not close enough," interval splitting (v2) moves up the schedule.
- **The verifier checks allocation, not selection.** A wrong instruction
  *selection* (emitting the wrong opcode for a LIR op) is caught by the fixed
  point and the tests, not by the verifier -- the verifier assumes the selected
  instructions are the program and checks only that vregs live where uses read
  them.

---

## 12. Closing note: the campaign ends here, and why

*Written 2026-07-23 (reek), so that nobody re-runs this.*

§1 opened this document with a specific claim: the benchmark gap is register
allocation of named bindings, and a global allocator is what closes it. The
allocator was built, it is live, and it is verified on every compile. **The
remaining gap it was supposed to close does not exist to be closed.** That is a
negative result, it was reached in a bounded investigation rather than a grind,
and it has two independent legs.

### 12.1 The spills that remain are the register file, not the allocator

`bench/codex/regstress.codex` is the register-pressure shape, and on x86-64 it
spills. The question the campaign has to answer is whether that is the allocator
being weak or the machine being small. It is the machine.

The evidence is one program, one allocator, one lowering, three register-file
descriptors, measured 2026-07-23 on the additive pipeline
(`-Passes +lir-dump` / `+lir-dump-a64`; all three runs report 39 vregs, so they
are the same program):

| descriptor | callee-saved (`ncallee`) | allocatable (`nregs`) | spill slots |
|---|---:|---:|---:|
| narrow probe | 2 | 5 | **13** |
| **x86-64** | **4** | **9** | **6** |
| a64 shape | 10 | 19 | **0** |

Nothing varies across those rows except the table. The spill count is a monotone
function of the callee-saved count and of nothing else the campaign could
improve.

The mechanism is exact rather than statistical. A value live across a call
cannot sit in a caller-saved register, so it is restricted to the callee-saved
pool. Reading the LIR dump for `regstress`, **peak simultaneous call-crossers is
6** -- at the last `mix4`, `v8`/`v17`/`v26` (the earlier results, consumed only at
the end) plus `v28`/`v30`/`v32` (the staged arguments) are all live across
`v34 = call step v33`. x86-64 in Codex's convention has **4** callee-saved
registers (RBX/R12/R13/R14; R10 is the bump allocator and R15 the closure
environment, and §11 records that adding R15 is *unsound*, not merely
unattempted). Six values need a pool of four, so at least two must live in
memory at that instant regardless of how good the allocator is. At `ncallee` 10
the same six fit, and the spill count is zero -- which is what the table shows.

**No allocator can remove a spill the register file genuinely requires.**

### 12.2 There are no holes to exploit in v1 anyway

The standing hypothesis for a further win was holed live intervals: `lir-build-
intervals` computes one conservative `[start, fin]` per vreg (§10), which
over-states liveness, so a value that is dead in the middle of its range still
occupies its register there. Sharpening that looks like free precision.

It is not available, and the reason is structural rather than empirical. Holes
require a value that is live, then dead, then live again. v1's LIR is a
**loop-free DAG with destination-driven, single-def-per-path vregs** (§3) -- the
whole simplification this design is built on. Every interval is therefore
contiguous `[def, last-use]` by construction. **The conservative interval is not
conservative here; it is exact.**

Holes only appear once TCO self-calls become LIR back-edges, which is v2 (§9) --
and §9 already records that the one v2-shaped hole was built, measured, and made
`my-gcd` **worse**, 158 dynamic instructions to 159, because what binds there is
the entry moves and not the interval model. Two attempts from the interval side
were reverted after measurement. This is the third reading of the same result
and it agrees: the lever is not here.

### 12.3 What this closes, and what it explicitly does not

**Closed: the middle-end codegen-quality frontier.** The selector takes all nine
`bench/codex` functions, beats C /O2 on `fact`, `gcd` and `sum`, matches the C#
JIT on `gcd`, and is neutral-to-ahead of the tree emitter (§11). The residual
gap to the F# JIT on `gcd`/`sum` is entry-move and prologue cost entangled with
TCO loops -- that is v2, which §9 files as unproven and which has now been
reverted twice on measurement. Chasing it is an unscoped grind, and the scoping
lesson of this project is that a campaign without a done-line does not get one
later.

**Not closed, and not to be read as closed by this note.** A closing note that
quietly absorbed open gaps would be the one unrecoverable mistake in doc work:

- **The rest of the prologue** (`lir-push-saved`,
  `lir-stack-guard`, the frame adjust) is raw x86 emitted outside the LIR, so no
  verifier sees it. The `col-hue` miscompile survived ten sessions of green
  benches behind exactly that kind of blind spot.
- **`list-map` used to lower and no longer does. The cause is established
  (2026-07-26) and it is not a defect in the selector.** `inline-single-caller`
  joined the default IR pipeline in CL 9461, and `map-list` has exactly one
  caller, so the pass substitutes its body into `list-map`. What the selector
  then sees is no longer the single `call map-list v0 v1` the pin recorded; it
  is the inlined loop set-up, and the whitelist declines it.

  Measured rather than reasoned. Against the current seed, `-Passes
  '+lir-dump'` (the shipping pipeline
  `fold-constants,inline-leaf-calls,inline-single-caller,lir-dump`) declines it,
  while a bare `-Passes 'lir-dump'`, which ablates the defaults, restores
  `LIR list-map(2) nvregs=3: b0: v2 = call map-list v0 v1 => v2 [check: ok]` --
  the pin-revision-11 line exactly. Run one default pass at a time,
  `inline-single-caller` alone reproduces the decline and `fold-constants` and
  `inline-leaf-calls` alone do not. Seed bisection agrees independently: the
  transition sits between `seed@9450` (lowers) and `seed@9500` (declines), and
  CL 9461 is the only seed revision in that window.

  **The pin revision is not the causing changelist, and reading it as one sent
  this investigation to the wrong CL first.** The line changed at pin revision
  12 (CL 10144, LirRetarget step 4d), which is merely the next time anyone
  re-recorded the pin; the behaviour had already changed some 680 changelists
  earlier. A snapshot dates when it was last taken, not when the thing it
  snapshots moved.

  What is NOT established is which gate in `lir-emit-try` rejects the inlined
  body -- `lower-def-to-lir` returning not-ok and `lir-calls-ok` refusing a
  builtin call target are both live candidates and neither has been isolated.
  That would need an instrumented compiler, and it is only worth building if
  someone intends to widen the whitelist. **Recovering this coverage is not
  recommended:** it means admitting calls with call arguments, which is real
  miscompile risk for one definition, and the codegen-quality frontier this
  section closes is exactly the argument against paying it.
- **Both LIR verifiers are pinned only in the affirmative;** the
  rejection paths run under no harness, blocked on compiler chapters having no
  quire.

None of the three is a codegen-quality item, which is why the frontier can close
while they stay open. All three are honest gaps and stay in the register until
someone closes them by fixing them.

### 12.4 The instrument that settled it

Worth keeping, because it is reusable and because it nearly went wrong here.
`bench/` is the only instrument that can see a change which keeps a program
correct and makes it worse; a correctness battery is blind to a redundant
instruction by construction. The three-descriptor sweep above is `bench/` used as
an *ablation over the machine model* rather than over the code, and it converts
"is the allocator good enough" from a matter of opinion into one table.

The trap it walked into first, recorded because the register already warned about
it and the warning still had to be re-learned: **a bare `-Passes lir-dump`
REPLACES the default pass list**, so the first x86 run was measuring un-inlined
IR and was not comparable to the a64 run that used the additive `+` form. The
`PIPELINE ...` line in the compile log is what tells you which one you ran. Read
it before you compare two dumps.
