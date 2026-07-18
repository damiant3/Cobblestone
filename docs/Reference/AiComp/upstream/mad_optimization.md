# Multiply-Add (MAD) Optimization

This document describes the complete path that creates target
`multiply_add` instructions. MAD optimization is not confined to
`MADSynthesisPass`: scalar expression shaping in `SimplifyPass` and SLP
vectorization are required before the final synthesis pass can match anything.

The relevant implementation is split across:

- `compiler/passes/simplify.py`: builds profitable scalar multiply-add shapes.
- `compiler/passes/slp.py`: packs independent scalar operations into `v*` and
  `v+` operations.
- `compiler/passes/mad_synthesis.py`: replaces a vector multiply and its single
  vector-add user with `multiply_add`.

See also the [VLIW ISA](VLIW_ISA.md), the
[SLP design](slp_vectorization_design.md), and the
[instruction scheduling design](instruction_scheduling_design.md).

## Target Motivation

The target provides an integer vector instruction:

```text
multiply_add(dst, a, b, c)
dst[i] = (a[i] * b[i] + c[i]) mod 2**32
```

A bundle has six VALU slots. A `multiply_add` is one VALU instruction and
occupies one slot, instead of the two VALU instructions needed by a `v*`
followed by a `v+`. It also removes one dependency level. This matters because
all slots in a bundle read the pre-bundle state: a dependent add cannot consume
a multiply result in the same bundle.

There is no scalar MAD instruction in this ISA. Scalar shaping is still useful
because SLP can turn repeated scalar `*`/`+` pairs into vector `v*`/`v+` pairs
that `MADSynthesisPass` can fuse.

## Pipeline

The current default pipeline contains the following relevant order:

```text
loop-unroll
  -> simplify
  -> dce / cse
  -> sroa (local promotion + read-only window promotion)
  -> load-elim / dse
  -> simplify
  -> slsr
  -> dce
  -> slp-vectorization
  -> dce / cse
  -> mad-synthesis
  -> dce
  -> lowering and scheduling
```

The stages have separate responsibilities:

| Stage | Responsibility |
| --- | --- |
| Simplify | Canonicalize shifts and construct scalar `multiply + add` shapes. |
| DCE | Remove old expressions made dead by rewrites. |
| SLP | Pack isomorphic scalar expressions into `v*` and `v+`. |
| CSE/DCE | Clean up vector IR without destroying the multiply-add shape. |
| MAD synthesis | Replace `v+(v*(a, b), c)` with `multiply_add(a, b, c)`. |
| Scheduler | Exploit the lower VALU demand and shorter dependency graph. |

## Simplify: Forming MAD Candidates

### Shift Canonicalization

For a valid constant shift amount `0 <= s < 32`, Simplify canonicalizes:

```text
x << s  ->  x * (2**s)
```

This is valid under the target's modulo-`2**32` arithmetic. It exposes a
multiplication that can participate in `add_mul_fold` and, after SLP, in MAD
synthesis. Variable shifts, right shifts, and invalid constant shift amounts
are not treated as multiplication.

### Add-Multiply Fold

The existing `add_mul_fold` option recognizes two expressions with the same
variable input:

```text
t_add = a + C
t_mul = a * K
result = t_add + t_mul
```

and rewrites the result as:

```text
t = a * (K + 1)
result = t + C
```

This is the basic MAD-producing shape. Once eight independent instances are
packed by SLP, the result becomes a `v*` with a `v+` user. The multiply-side
intermediate must have one use so the rewrite does not strand duplicated
multiply work. Any input expressions made obsolete are removed by the
following DCE.

A common source pattern is:

```text
(a + C) + (a << s)
  -> (a + C) + a * (2**s)
  -> a * (2**s + 1) + C
```

### Associative Constant Folding

The `assoc_fold` option reassociates constant chains:

```text
op(op(x, C1), C2)  ->  op(x, combine(C1, C2))
```

It supports `+`, `*`, `^`, `&`, and `|`; addition is the important case for
MAD formation:

```text
(x + C1) + C2  ->  x + (C1 + C2)
```

The rewrite only changes the outer expression. The inner expression remains
when it has other users and becomes dead-code when it does not, so no
single-use restriction is needed for correctness. Simplify records that the
outer use of the inner result was removed; this updated use count is important
for the following distribution step.

### Multiply Distribution

The `mul_dist` option recognizes multiplication, including constant left
shift, over an add-constant expression:

```text
(x + C) * K   ->  x * K + C * K
(x + C) << s  ->  x * (2**s) + C * (2**s)
```

It can also look through an existing constant multiply or left shift:

```text
x = a * K2
(x + C) * K  ->  a * (K2 * K) + C * K
```

This turns the output into another `multiply + add` candidate. The inner add
must have at most one effective remaining use. Otherwise distribution would
create new work while retaining the original add for another user.

For shifts, the SSA value must be the left operand and the shift amount must be
a constant in `[0, 31]`. Shifts are not commutative, so an expression such as
`Const(2) << variable` must never match this rule.

### Same-Run Def-Use Tracking

`UseDefContext` is built once when Simplify starts, but these transformations
need to consume definitions emitted earlier in the same pass invocation.
Simplify therefore maintains:

- `_local_defs`, which maps results to newly emitted operations and takes
  precedence over the original definition map.
- `_use_adjust`, which accounts for uses removed by rewrites such as
  `assoc_fold`.

Without `_local_defs`, a later expression would see the pre-rewrite definition
and miss a newly formed multiply-add pattern. Without the adjusted use count,
`mul_dist` would conservatively reject an expression after `assoc_fold` had
already removed one of its users.

## Tree-Hash Cross-Stage Example

The zero-based hash stages 2 and 3 demonstrate why the transformations are
used together. Let `a` be the input to stage 2 and let all constant arithmetic
be modulo `2**32`:

```text
C2 = 0x165667B1
C3 = 0xD3A2646C
```

```text
v   = (a + C2) + (a << 5)
t1  = v + C3
t2  = v << 9
out = t1 ^ t2
```

Shift canonicalization and `add_mul_fold` first produce:

```text
v = a * 33 + C2
```

`assoc_fold` rewrites the first stage-3 arm and removes one use of `v`:

```text
t1 = a * 33 + (C2 + C3)
```

Because `v` now has one effective use, `mul_dist` can rewrite the other arm:

```text
t2 = a * (33 * 512) + (C2 * 512)
   = a * 16896 + (C2 << 9)
```

The old `v` computation then becomes dead. After SLP and MAD synthesis, the
dataflow changes from:

```text
MAD(v) -> {vadd, vmul} -> vxor
```

to:

```text
{MAD(t1), MAD(t2)} -> vxor
```

The rewritten form has one fewer vector instruction and one fewer dependency
level per vector pack. The two MAD arms are independent and can be scheduled
in parallel.

## MAD Synthesis

`MADSynthesisPass` matches only vector HIR operations:

```text
v+(v*(a, b), c)  ->  multiply_add(a, b, c)
v+(c, v*(a, b))  ->  multiply_add(a, b, c)
```

The pass builds a use-def context, then processes each statement list in two
phases:

1. Find every `v+` with a `v*` operand whose result has exactly one use.
2. Omit the matched `v*` and replace the `v+` with `multiply_add`, preserving
   the add result SSA value.

The same transformation is applied recursively inside retained `ForLoop` and
`If` bodies. Scalar `*` and `+` operations are deliberately ignored because
the target has no scalar MAD instruction.

The multiply result must have exactly one user. Fusing a multiply with one add
while another operation still consumes the multiply result would remove a
required definition. The pass does not clone the multiply or use a cost model
for multi-use cases.

## Correctness Conditions

All arithmetic rewrites use the target's unsigned modulo-`2**32` semantics.
Associativity and distributivity of addition and multiplication hold in this
ring, and the vector `multiply_add` has exactly the same wrap behavior as a
separate vector multiply followed by vector add.

The implementation relies on the following guards:

- Constant values used by Simplify are normalized with `& 0xffffffff`.
- Constant additions and multiplications introduced by the rewrites are
  masked to 32 bits.
- Left-shift rewrites accept only a constant right operand in `[0, 31]`.
- `mul_dist` rejects an inner add with multiple effective users.
- MAD synthesis requires the vector multiply result to have exactly one use.
- SLP must prove that packed scalar operations are independent before creating
  the vector operations.

The Simplify rewrites may leave semantically equivalent, now-unused operations
in HIR. Their removal is the responsibility of the following DCE pass; it is
not a precondition for correctness.

## Profitability and Configuration

The relevant Simplify options are:

```json
{
  "canonicalization": true,
  "add_mul_fold": true,
  "assoc_fold": true,
  "mul_dist": true
}
```

`assoc_fold` and `mul_dist` are individually general algebraic transforms, but
their profitability in `tree_hash` is coupled. `assoc_fold` removes the first
use of the shared stage result; only then can `mul_dist` legally rewrite its
remaining use. Enabling `assoc_fold` without `mul_dist` can also make an
existing multiply multi-use and prevent a MAD that would otherwise form.

The pass implementation defaults the two newer options to disabled. The
default project configuration explicitly enables both, so experiments should
toggle them as a pair unless the interaction is being measured intentionally.

### Current Tree-Hash Ablation

The following development measurement used the default workload
`forest_height=10`, `rounds=16`, `batch_size=256`, with all unrelated settings
held constant:

| `assoc_fold` | `mul_dist` | VM cycles | Vector MADs | Scheduler bundles |
| --- | --- | ---: | ---: | ---: |
| on | on | 1141 | 2432 | 1142 |
| off | on | 1188 | 1920 | 1189 |
| off | off | 1182 | 1920 | 1183 |
| on | off | 1209 | 1408 | 1210 |

With both transforms enabled, the first Simplify run reported 4096
`assoc_folds` and 4096 `mul_dists`; the second reported another 512
`assoc_folds`. Compared with disabling both, the complete pipeline formed 512
additional vector MADs and reduced VM execution by 41 cycles, about 3.5
percent relative to the disabled case. Note that enabling only one of the
pair is WORSE than disabling both (`mul_dist` alone loses 6 cycles,
`assoc_fold` alone loses 27): each transform's profitability depends on the
other unlocking or consuming its output.

These numbers characterize the current pipeline (SROA window promotion, no
tree-level-cache pass) rather than defining a stable performance contract.
Changes to SLP, scheduling, window promotion, or the workload can change the
cycle impact even when the same number of Simplify patterns match.

## Diagnostics and Tests

Use pass metrics and IR dumps to follow the complete transformation:

```bash
python3 programs/tree_hash.py --print-metrics
python3 programs/tree_hash.py --print-after-all --print-metrics
```

Relevant metrics are:

- Simplify: `add_mul_folds`, `assoc_folds`, and `mul_dists`.
- MAD synthesis: `patterns_matched` and `ops_fused`.
- Scheduling: VALU instruction count, utilization, dependency stalls, and
  bundle count.

Focused coverage lives in:

- `compiler/tests/test_simplify.py`: add-multiply folding, associative folding,
  distribution, shift operand order, multi-use rejection, and u32 semantics.
- `compiler/tests/test_mad_synthesis.py`: both add operand orders, single-use
  legality, scalar rejection, nested control flow, metrics, and execution.
- `compiler/tests/test_programs.py`: end-to-end SLP and MAD-producing programs.

Run the focused tests with:

```bash
python3 -m pytest compiler/tests/test_simplify.py \
  compiler/tests/test_mad_synthesis.py -v
```

## Limitations and Future Work

- MAD synthesis only recognizes an immediate `v*` definition consumed by a
  `v+`; it does not reassociate arbitrary vector expression trees.
- There is no scalar MAD synthesis because the ISA has no scalar MAD.
- Multi-use multiply results are not cloned, even when cloning might be
  profitable.
- MAD operations are intentionally not devectorized by the scheduler. On a
  different workload with spare scalar ALU capacity and saturated VALU slots,
  fusion is therefore not guaranteed to be profitable without a target cost
  model.
- SLP is seed-driven. Scalar MAD candidates that cannot be reached from a
  legal vectorization seed remain scalar and are invisible to MAD synthesis.
- SLP may introduce broadcasts and register pressure that the current
  Simplify profitability checks do not model.
- CSE between SLP and MAD synthesis can make a vector multiply multi-use;
  synthesis then conservatively leaves it unfused rather than cloning it.
- Simplify has no general target cost model for `assoc_fold` and `mul_dist`.
  The current tree-hash configuration relies on enabling them together.
- A future combined profitability decision could evaluate the whole
  reassociation/distribution/MAD opportunity instead of treating the two
  Simplify options independently.
