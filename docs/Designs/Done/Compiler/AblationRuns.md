# Ablation Runs -- IR Pass Pipeline

Dated ablation runs of the compiler's IR pass pipeline, produced by
`build/ablate.ps1` (Middle End campaign step 3 --
`docs/Designs/Active/Compiler/MiddleEnd.md`). This file is append-only:
each run gets a `## Run N` section recording the kernel digest, the
configs, the result table verbatim, and a short reading. Never rewrite an
old run's numbers -- a stale row under a stated kernel digest is history; a
corrected row is a new run.

## Why a grid, not an argument

Pass profitability is **non-monotone and coupled**. The canonical datum
(`docs/Reference/AiComp/OPPORTUNITIES.md` §B, from their MAD ablation):
`assoc_fold` alone cost 27 cycles, `mul_dist` alone cost 6, and together
they won 41 -- either transform alone was worse than neither, because one
must remove a use before the other becomes legal on what remains. You
cannot derive that by reasoning about the passes; you find it by running
the grid, and you can only run the grid because the pipeline is data (the
WS-B2 `passes=` knob). So: every pass added in this campaign earns its
place in a run recorded here, alone and in combination, never by argument
alone.

## What a run does and does not establish

Each run scores configurations on compiler self-compile size and wall
time plus per-bench static instruction counts (the `bench/compare.ps1`
counting mechanism, comparable to the tables in
`docs/ArchitectsSketchbook.md`). It does **not** run a per-config fixed
point -- only the shipping default pipeline is gate-verified -- and wall
time is a single noisy run. A config that wins a row here still has to
survive `build/build.ps1` before it can become the default.

## Run 1 -- 2026-07-14, inliner × const-fold

The first ablation ever run on this compiler. Kernel: `seed/Codex.cdx`
[`19E3F8444E12B009`] (the WS-B2 seed, main CL 7948). Command:
`build/ablate.ps1 -Kernel seed\Codex.cdx`. Bench columns are static
instruction counts of the benchmark function body. Skipped: `vecstride`
(no benchmark-function mapping in `bench/compare.ps1`'s table).

| Config | Compiler bytes | Compiler s | fib | fact | gcd | sum | ack | tak | collatz | locals | regright | regstress |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| default | 2294622 | 9 | 21 | 13 | 11 | 8 | 24 | 42 | 14 | 54 | 14 | 97 |
| none | 2296774 | 8.7 | 21 | 13 | 19 | 8 | 24 | 42 | 25 | 54 | 14 | 97 |
| fold-constants | 2294526 | 8.4 | 21 | 13 | 19 | 8 | 24 | 42 | 25 | 54 | 14 | 97 |
| inline-leaf-calls | 2296870 | 8.5 | 21 | 13 | 11 | 8 | 24 | 42 | 14 | 54 | 14 | 97 |

### The reading

**The two legacy passes are perfectly additive and independent on seed
size.** Against the `none` baseline of 2,296,774 bytes:

| Pass | Effect on compiler bytes | Effect on benchmarks |
|---|---:|---|
| `fold-constants` | **−2,248** | none -- moves no benchmark at all |
| `inline-leaf-calls` | **+96** | **the entire win**: gcd 19→11, collatz 25→14 |

And the effects compose exactly: `2,296,774 − 2,248 + 96 = 2,294,622`,
which is the default to the byte. Each pass's contribution is the same
whether or not the other runs (const-fold saves 2,248 either way; the
inliner costs 96 either way).

So: **the inliner buys the codegen win and pays 96 bytes of code
duplication for it, which is simply what inlining is.** Const-fold shrinks
the compiler by 2.2 KB and buys nothing on the benchmarks. Both earn their
place, for different reasons, and neither interferes with the other. Every
other bench is unmoved by either pass -- fib/fact/ack/tak are
self-recursive, and a self-recursive body is not a leaf, so the inliner
cannot fire; sum, locals, regright and regstress present it nothing to
fire on.

**A refuted hypothesis, recorded because the refutation is the finding.**
The first draft of this reading called the +96 "the inliner's uncollected
constant residue" and predicted that re-running the folder *after* the
inliner would recover it -- the coupled, non-monotone shape of ai-comp's
MAD ablation (quoted above), which is what one *expects* to find and
therefore what one is most likely to see whether it is there or not. The
experiment says no:

| Config | Compiler bytes |
|---|---:|
| `fold-constants,inline-leaf-calls,fold-constants` | 2294622 |
| `fold-constants,inline-leaf-calls` | 2294622 |

Byte-identical. There is no unfolded residue; the 96 bytes are duplicated
*code*, not uncollected constants, and no amount of re-folding touches
them. **We have no coupled pass pair in this compiler today.** The grid
was run, the prediction was wrong, and the number is what stands.

That is the argument for the campaign in miniature, and it is a better one
than the coupling story would have been: **twenty CLs of peephole work
never measured any of this, because the passes could not be switched off --
and the first thing measurement did was kill a plausible theory.**

### What it says to do next

1. **Do not reorder or drop either pass.** Both are justified, on
   different axes, and the pipeline order is not costing anything.
2. **The coupling question is open, not answered.** Two passes that
   happen to be independent do not tell us the next five will be. Every
   pass WS-D adds gets its row here, alone and in combination.
3. **It sharpens the WS-D plan.** The GHC-style simplifier's one-traversal
   fixpoint is still the right architecture, but this run says its value
   here is *not* "collecting residue the previous pass left" (there is
   none to collect). It is the rewrites that do not exist yet -- DCE,
   CSE, case-of-known-constructor -- that will create work for each other.
4. Wall time is flat across configs (8.4–9.0 s, single noisy runs) -- the
   passes cost nothing measurable at compile time, so pass *cost* is not
   a reason to drop either.
5. The seed-size figure to beat, for anything WS-D adds, is **2,294,622**
   at kernel `19E3F8444E12B009`.

## Run 2 -- 2026-07-14, the simplifier (WS-D3 slice 1)

First measurement of the GHC-style simplifier's opt-in first slice
(`IR/Simplify.codex`: constant folding, SCCP if-of-known-bool, literal
propagation). Kernel: the WS-D3 SUT -- this CL's compiler, built from the
current main seed, **not yet gate-rebuilt or signed** -- [`D6D7430AC845E86D`].
Its `default` size (2,363,294) is larger than Run 1's because the SUT
carries the new Simplify chapter itself (~69 KB of compiled-in pass code,
reachable through the pipeline-name dispatch exactly as `ir-check` and the
occurrence analyser are). Command: `build/ablate.ps1 -Kernel
build\output\simplify-sut.cdx -Configs @('default','fold-constants,inline-leaf-calls,simplify')`.

| Config | Compiler bytes | Compiler s | fib | fact | gcd | sum | ack | tak | collatz | locals | regright | regstress |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| default | 2363294 | 9.3 | 21 | 13 | 11 | 8 | 24 | 42 | 14 | 54 | 14 | 97 |
| fold-constants,inline-leaf-calls,simplify | 2361502 | 9.9 | 21 | 13 | 11 | 8 | 24 | 42 | 14 | 54 | 14 | 97 |

### The reading

**Adding the simplifier on top of the two legacy passes shrinks the
compiler's own code by 1,792 bytes and moves no benchmark.** Both numbers
are the ones the plan predicted, and both are worth stating plainly.

- **−1,792 compiler bytes** (2,363,294 → 2,361,502). Byte counts are a
  deterministic function of source and pipeline, so this is exact and
  reproducible, not wall-time noise. It is the value of the simplifier's
  *new* rewrites -- literal propagation, integer/boolean folding, and
  taking the decided arm of an `if` -- beyond what `fold-constants`
  already did, applied to the compiler's own source. This is seed density,
  the thing `MiddleEnd.md` step 4 says the tree passes buy, measured for
  the first time on a real body of code rather than asserted.
- **Every benchmark unmoved** -- fib/fact/gcd/sum/ack/tak/collatz/locals/
  regright/regstress identical across both configs. The plan says to
  expect this and say so: the benchmarks are arithmetic over their
  parameters with no literal-bound `let` and no constant subexpression to
  fold, so a constant-folding, literal-propagating simplifier finds
  nothing to do in them. Codegen quality is register allocation of named
  bindings (the LIR, step 5); it is not what a tree simplifier reaches.

**What this run does not establish.** It is one incremental slice: no dead
let elimination, no occurs-once inlining, no CSE -- the rewrites that create
work for each other, and where Run 1 predicted the real coupling would live,
are not in it yet. The −1,792 is the floor of the simplifier's density win,
not the ceiling. And this is not a gate: the `+simplify` config has not been
shown to self-host to a fixed point (only the shipping default is
gate-verified), and it never will be until the simplifier replaces
`fold-constants` in the default pipeline as its own CL. This slice ships the
pass opt-in; the default output is byte-identical.

## Run 3 -- 2026-07-14, the simplifier with dead-let elimination (WS-D3 slice 2)

The simplifier's second slice adds dead-let elimination: a non-literal
`let x = v in b` whose x is dead in b and whose v is pure and trap-free is
dropped. Kernel: the WS-D3 slice-2 SUT [`518A4F9FC7E70118`], built from the
main slice-1 seed. Same two configs as Run 2.

| Config | Compiler bytes | Compiler s | fib | fact | gcd | sum | ack | tak | collatz | locals | regright | regstress |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| default | 2374316 | 11.3 | 21 | 13 | 11 | 8 | 24 | 42 | 14 | 54 | 14 | 97 |
| fold-constants,inline-leaf-calls,simplify | 2372180 | 12.3 | 21 | 13 | 11 | 8 | 24 | 42 | 14 | 54 | 14 | 97 |

### The reading

**Dead-let elimination adds another 344 bytes of compiler density over
slice 1.** The `+simplify` config now saves 2,136 bytes against the default
(2,374,316 → 2,372,180), where slice 1 saved 1,792; the difference, 344
bytes, is what dropping dead pure-trap-free bindings from the compiler's own
source buys. The absolute numbers are larger than Run 2 because both configs
run on the slice-2 SUT, which carries the slice-2 pass code itself (the
`default` column grew from Run 2's 2,363,294 to 2,374,316 for that reason --
the pass is compiled in either way; only the `+simplify` column exercises
it). As always the byte counts are deterministic and exact.

**Benchmarks unmoved, again** -- the benches have no dead bindings any more
than they had foldable constants, so a dead-let pass finds nothing in them.
The −344 is entirely seed density on real code, which is the point of the
tree passes and not a codegen-quality claim.

The next slice (occurs-once inlining with the rapier) is where a rewrite
first *moves* a value rather than dropping or folding one, and it is the
first that needs adversarial capture fixtures because the fixed point cannot
see a self-consistent miscompile.

## Run 4 -- 2026-07-14, occurs-once inlining and copy propagation (WS-D3 slice 3)

The simplifier's third slice adds copy propagation (a `let` bound to a bare
name) and occurs-once inlining (a `let` bound to a pure trap-free value used
once), with capture refused rather than renamed. Kernel: the WS-D3 slice-3
SUT [`EBAF138AFA308550`]. Same two configs.

| Config | Compiler bytes | Compiler s | fib | fact | gcd | sum | ack | tak | collatz | locals | regright | regstress |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| default | 2381767 | 9.8 | 21 | 13 | 11 | 8 | 24 | 42 | 14 | 54 | 14 | 97 |
| fold-constants,inline-leaf-calls,simplify | 2364727 | 11 | 21 | 13 | 11 | 8 | 24 | 42 | 14 | 46 | 14 | 97 |

### The reading

**This is the big one, and it moved a benchmark.** Adding copy propagation
and occurs-once inlining saves **17,040 compiler bytes** (2,381,767 →
2,364,727) -- an order of magnitude more than slices 1 and 2 combined (−1,792
and −344). The reason is structural: lowering emits a great many
`let x = <name or simple value> in ...` bindings, and copy propagation
collapses the redundant ones across the whole 36K-line compiler. This is the
seed-density payoff the tree-pass campaign was for, and it is real code, not
a contrived case.

**And `locals` dropped 54 → 46 -- the first benchmark any tree pass has
moved.** Every earlier slice left all ten benches untouched, exactly as
predicted, because folding and DCE find nothing in constant-free arithmetic.
Inlining is different: the `compute` benchmark (the `locals` column) is the
one written to stress named local bindings, and copy-propagating its
redundant locals cut eight instructions of register traffic (−15%). It is
modest and it is not the register-allocation win the LIR is for -- the other
nine benches, which do not lean on named-local redundancy, are unmoved -- but
it is the first evidence on the benchmark suite that inlining reaches codegen
at all, and it is worth recording as the exception to "the tree passes move
nothing."

**The capture guard is the load-bearing safety property here**, and it is
*not* checked by the fixed point: the simplifier is opt-in, so the
self-compile never runs it, and a wrongly-captured program compiles to a
self-consistent wrong answer the byte-identity check cannot distinguish from
a correct one. The guard is checked instead by two adversarial fixtures that
compile the same source with the pass off and on and diff the runtime output
-- `sc-cap` (capture through a `let` binder) and `sc-lamcap` (through a
lambda parameter). Both produce identical output with the pass on and off,
which is the whole proof that the inline was correctly refused.
