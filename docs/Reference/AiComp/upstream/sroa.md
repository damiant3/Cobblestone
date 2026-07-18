# SROA: Scalar Replacement of Aggregates

`SROAPass` (compiler/passes/sroa.py) promotes qualified memory regions to
SSA values. It is one optimization organized as a two-axis matrix:

|                          | constant offset          | dynamic bounded offset            |
| ------------------------ | ------------------------ | --------------------------------- |
| **load** (local region)  | tracked state value      | select tree over state snapshot   |
| **load** (read-only)     | shared preload           | select tree over preloads         |
| **store** (local region) | state update, store gone | not implemented (select scatter)  |

**Region qualification** decides when a region's values are trackable:

- **Local by contract** (`assume_local_memory`, below): private and
  unobservable, so the pass owns every read AND write.
- **Read-only by proof**: object-size analysis first proves every speculative
  preload in bounds; alias analysis then uses the program's declared restrict
  contract to refute every store against the window.

**Access rewriting** materializes each access per the matrix. Dynamic
bounded offsets share one select engine: bits of `offset - lo` are
recovered from the offset's affine form (one boolean atom per power-of-two
coefficient; provably-constant selects and multipliers are folded through
by range refinement) with an explicit shift/mask fallback. The pass does
not inspect header slots, variable names, tree shapes, or benchmark
constants.

## The local-memory contract

`assume_local_memory(base, length)` is a source-level optimization contract.
`length` is measured in 32-bit words. At the annotation point the region
`[base, base + length)` is valid, non-wrapping, and logically zero-initialized.
Until function exit it is private to the current invocation, cannot escape,
and cannot be observed by another function or thread. Every access to the
region in the current invocation must use `base` or an address derived from
it; access through an independently rooted alias violates the contract. Its
final contents are unobservable. Violating the contract is undefined behavior.
The region is also unobservable at `Pause` and debug/host-inspection
boundaries; observing it there violates the same contract.

The annotation does not allocate or clear memory and lowers to no target
instruction. Ignoring it is always valid. Contract parsing lives in
compiler/local_memory.py; the exact-base address classification it relies
on is the general `PointerProvenance` in compiler/pointer_provenance.py.

## Local regions (contract-qualified, read/write)

The first implementation requires an SSA base and a positive constant
length, and flat HIR at the accesses. A region is proven legal as a whole,
then rewritten atomically:

```text
state[offset] = 0
load(base + offset)       -> state[offset]
store(base + offset, val) -> state[offset] = val
load(base + i), i bounded -> select tree over the state snapshot
```

No final stores are emitted because the contract makes the region
unobservable on return. The dynamic-read row is the newest quadrant: a
scalar load whose base-relative offset interval provably lies INSIDE the
region (width <= max_window) selects over the current state values -- the
leaves are the state at that program point, so promoting it costs selects
but zero memory traffic and never blocks the region.

Everything else falls back conservatively and atomically: pointer escape,
tainted control flow, retained-control-flow accesses, vector accesses that
overlap the region, dynamic STORES (select scatter is not implemented),
and dynamic reads that are unbounded or wider than max_window. Dynamic
offsets provably OUTSIDE the region (non-wrapping, footprint included) are
ordinary accesses to other memory and are preserved without rejecting the
region; statically out-of-range accesses likewise.

## Read-only windows (proof-qualified, table promotion)

The dynamic-index counterpart for shared memory: a load `load(p + i)`
whose index is range-proven to a small window [lo, lo+W) of a provably
read-only region is replaced by W preloaded values and a select tree over
the bits of (i - lo). Immutability comes from alias analysis under the
program's declared restrict contract (an unrefutable store conservatively
disables the window, never the program). Wide windows are re-preloaded per
use cluster to keep live ranges short.

Three additional guards qualify a window:

- **Object bounds**: preloading touches every window slot, including ones an
  execution never reads. A width-one window does not widen the source access;
  every wider window must be contained in a trusted `object_extent` emitted by
  the frontend, allocation model, or ABI. `HIRBuilder.memory_view(base, size)`
  creates this zero-runtime-cost metadata while returning the same address
  value. It is object/type information, not a programmer optimization hint;
  ordinary source code must not be able to forge it. Loads under control flow
  are still never speculated.
- **Pause epochs**: a Pause hands control to the host, which may rewrite
  memory. Windows and their preloads are confined to one pause-delimited
  epoch; a snapshot is never reused across a barrier.
- **Engine-bound cost gate**: promotion trades load work for flow (select)
  and alu (preload address, bit extraction) work; the select trees land on
  the single-slot flow engine, so small blocks are easily made flow-bound.
  A window is accepted only if the block's per-engine lower bound does not
  grow beyond a small proportional slack (floored, so tiny blocks stay
  strict); select cost is charged at 1/VLEN when the window has enough
  isomorphic uses to vectorize. The amortization rule (uses > W x
  clusters) additionally requires a strict load-traffic reduction.
  Per-reason rejection counts are reported in the pass metrics.

## Pipeline placement

The pass runs after full unrolling, Simplify, and CSE, but before generic
LoadElim/DSE. Promoting the complete local region first avoids making those
memory passes rediscover the same forwarding chains, and read-only window
discovery depends on the ranges the local promotion exposes. A mandatory
`strip-assume` pass then removes any marker left behind when promotion is
disabled, followed immediately by DCE. This placement is a pipeline
heuristic for the current kernel; the legality rules are
program-independent. If the cleanup pass is disabled or omitted by a custom
pipeline, lowering still erases the assumption marker defensively; the
difference is only that dead HIR may survive longer.

## Tree Hash

The tree-hash frontend records the forest's real ABI object and declares its
index state with:

```python
b.memory_view(forest_values_p, n_nodes)
b.assume_local_memory(inp_indices_p, b.const(batch_size))
```

The memory view comes from `build_mem_image`'s forest allocation and provides
only its extent; it is neither a runtime bounds check nor a programmer
optimization assumption. The local-memory marker is a separate kernel ABI
contract: every input index must be zero at the first pause, and the index
array is internal traversal state whose later contents are not an output. Only
the value array is externally observable.

This replaces two former benchmark-specific actions (no pass substitutes
initial index loads with zero anymore; default DSE no longer discards the
final index stores by header slot), and the read-only window promotion
subsumes the former tree-level-cache pass: the per-round index windows ARE
the tree levels, the wrap periodicity falls out of range analysis folding
the wrap checks, and the branch bits are recovered from the affine chain --
with no knowledge of trees, header slots, or rounds.
