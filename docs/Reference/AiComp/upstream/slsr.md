# Straight-Line Strength Reduction (SLSR)

`compiler/passes/slsr.py` strength-reduces chain recurrences of the form

```text
y = A * x + s*v + C        (const A >= 2, s in {+1, -1}, v optional)
```

on flat (fully unrolled) SSA bodies by a change of variable that makes the
constant addend disappear from every link. It is built on a reusable
recurrence analysis (`compiler/recurrence.py`) and is deliberately
straight-line only: in this compiler everything is unrolled before it runs,
so no loop-carried form is needed.

The canonical target is the unrolled tree-hash index chain
`idx' = 2*idx + bit + 1`: tracking `j = idx + 1` instead turns every update
into `j' = 2*j + bit`, saving the `+1` in each of the thousands of unrolled
copies. SLSR discovers and performs exactly this class of rewrite without
knowing anything about the benchmark.

## Recurrence analysis (compiler/recurrence.py)

### Affine evaluation

`RecurrenceAnalysis.affine_of` computes a symbolic affine form

```text
expr = sum(coeff_i * atom_i) + const        (all arithmetic mod 2**32)
```

by expanding `+`, `-`, `* const`, and `<< const` definitions. Atoms are SSA
values the evaluator does not look through: loads, selects, non-affine ops,
values in an explicit stop set, or anything past the expansion budgets
(`max_terms = 6`, `max_depth = 8`; exceeding them degrades the value to an
atom, never to an error). Each expression carries the `id`s of the ops the
expansion walked through -- its *interior* -- which the cost model later
uses to reason about which old computations die.

Working on affine forms instead of syntax matters: the chain may be spelled
as `2*idx + (bit + 1)`, `(idx << 1) + offset`, or whatever earlier rewrites
(select-to-mul, mul distribution) produced. The analysis sees through all
spellings uniformly.

### Link matching and chain discovery

`match_link` views an op as a link `y = A*x + s*v + C` when its affine form
has exactly one atom with |coeff| >= 2 (the chain variable `x`), at most
one atom with coeff +-1 (the step `v`), and a constant. Two deliberate
asymmetries:

- **Only `+`/`-` tops can be links.** Pure scalings (`*`, `<<`) must remain
  expandable interiors: matching them as degenerate `C == 0` links would
  put them in the discovery stop set and cut the real chain's expansion --
  and a scaling-only link saves nothing anyway.
- **Discovery walks in program order with a growing stop set** containing
  all previously discovered link results. Expansion stops at those
  boundaries, so a chain is represented link by link; without the stop set,
  single-use intermediate links would be flattened into one big affine
  expression with coefficient `A^n` and the chain structure would be lost.

One merge rule handles association order: when `y = t + C` and `t` is an
already-discovered `C == 0` link (a greedily frozen partial like
`t = 2*x + v`), the two merge into the full link `y = A*x + s*v + C`, and
the covered partial is retired from the link set (it stays in the stop set
as an expansion boundary). Keeping both would give one chain a mixed
zero-C/full-C member list, which breaks fixpoint root solving below.

## The transformation

The rewrite tracks `xt = x + k` with per-link compensation constants

```text
k(y) = A*k(x) - C   (mod 2**32),      k(root) chosen per policy
```

so each transformed link becomes `xt' = A*xt + s*v`: one `slsr_mul` op,
plus one `slsr_val` add/sub when a step variable exists -- the constant
addend is gone. All arithmetic is exact in Z/2**32, so the change of
variable is correct without any overflow reasoning.

Uses of a chain value are rewired with the inverse compensation:
`+(o, y)` becomes `+(o - k, xt)`, and the `-` orientations analogously.
A use is *compensable* when it is a two-operand `+`/`-` whose other operand
is a distinct value; uses inside any candidate link's interior belong to
old chain computations and are never rewired; dead husks (results with no
uses, left by earlier passes) neither block a rewrite nor cost anything.
A link is rewritable only if every live non-interior use is compensable and
its `x` is either the chain root or itself rewritable.

Compensation values fold into the constant when `o` is a Const; for SSA
`o` they become hoisted `slsr_comp` ops cached globally by
`(operand, signed delta)`, so the 256 structurally identical unrolled lanes
share one op per distinct pair (on tree_hash: 7 comp ops total for 3328
rewritten links). Two placement rules are load-bearing:

- A hoisted op is placed at `max(link_pos, def_pos(o) + 1)` -- after its
  operand's definition, but never earlier than the link. In particular it
  never lands inside the leading load/const prefix of the body, whose shape
  SLP's entry-broadcast placement relies on.
- The root's transformed value is NOT cached by the bare root SSA: two
  chains may share a root value while needing DIFFERENT `k_root`s (fixpoint
  policy), which was a confirmed miscompile in an earlier version. Nonzero
  root offsets go through the same `(operand, delta)` comp cache, which
  both provides the correct sharing and prices the root materialization
  exactly like any other compensation op.

## Root-offset policies

The root's `k` is a degree of freedom. Two policies are evaluated globally
(whole-body cost under each), and the better total wins:

- **`k_root = 0`**: roots are reused as-is (free), per-link `k`s differ
  along the chain. Each compensated use position costs one shared
  `(operand, k)` op. Wins when many parallel chains share operands and `k`
  sequences -- tree_hash's 512 parallel lane chains pick this.
- **Fixpoint `k`**: when `k = A*k - C` has a common solution for every
  member, all links share one `k`, collapsing all compensations to a single
  op per operand, at the price of one root add per distinct root. Each link
  contributes the congruence `(A-1)*k = C (mod 2**32)`; solution sets
  `k = k_l (mod 2**32/gcd(A_l - 1, 2**32))` are intersected with CRT for
  non-coprime moduli. The intersection matters: taking one link's smallest
  representative and checking it against the others misses solutions (a
  bug in an earlier version). A safety pass re-verifies the final `k`
  against every congruence. Wins for long single chains.

## Cost model: dead-op liveness fixpoint

The pass only rewrites when it can prove a net op-count reduction, and the
accounting is done by liveness, not by heuristic counting. Two earlier
generations of heuristics ("savings per link", then "survivor bookkeeping")
both leaked through op-level sharing -- e.g. a mul CSE-shared between an
approved link and a non-approved candidate was counted as dying while the
other consumer kept it alive.

`compute_dying_ops(approved)` runs the honest version: death candidates are
the approved chains' interior ops; an op survives if its result is
referenced by any surviving consumer -- a non-dying op, a compensation op
(which reads the rewired use's other operand), the emitted new chain
(which reads root and step values), or a non-approved candidate's
computation. This iterates to a fixpoint because de-listing one op can keep
its operands alive.

Approval is itself a fixpoint around that: every chain whose
`|interior ops that die| - |ops emitted|` falls below `min_savings` is
dropped, liveness is recomputed (rejected chains now keep their values
alive), and rejections cascade until stable. The final decision compares

```text
total_net = |dying ops| - |emitted ops| - |hoisted comp pairs|
```

per policy. Scheduling effects are deliberately NOT modeled -- op count is
the proxy -- and cycle-count regression tests guard against rewrites that
help the count but hurt the schedule.

## Pipeline position and results

SLSR runs after read-only window promotion (in SROA) and the
second `simplify` (which exposes folded, canonical chains) and immediately
before a DCE that deletes the old chain computations, followed by SLP
vectorization of the new chains. On the
default tree_hash build:

```text
links_found 19712, chains 512, links_rewritten 3328, policy zero,
comp_ops 7, net_savings 3833 (model);
following DCE: 99127 -> 85815 statements
```

worth about -31 cycles at introduction (1208 -> 1177), plus -2 from
free root reuse under the zero policy (1145 -> 1143).

## Testing

- `compiler/tests/test_recurrence.py`: affine evaluation (budgets, stop
  sets, mod-2**32 combination), link matching (tops-only, ambiguity
  rejection), program-order discovery, and the C0-partial merge/retire
  rule.
- `compiler/tests/test_slsr.py`: end-to-end rewrites with execution checks,
  both root policies and the CRT solver, the dead-op cost model on
  CSE-shared interiors, compensation caching/sharing across lanes, hoisted
  placement, non-compensable-use rejection, and metrics.
- The kernel correctness suites plus the cycle-count regression tests cover
  the scheduling side the cost model does not.

## Limitations

- Straight-line only by design: ForLoop/If bodies are skipped whole. Loop
  recurrences would need carried-value reasoning this compiler does not
  require (everything unrolls first).
- Only `A >= 2` positive chain coefficients; a single optional step
  variable; compensable uses limited to two-operand `+`/`-`.
- Op count is the objective; the schedule is validated empirically, not
  modeled.
