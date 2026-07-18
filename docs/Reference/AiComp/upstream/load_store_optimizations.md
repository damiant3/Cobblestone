# Load/Store Optimizations for tree_hash Kernel

> Historical analysis. Its claims about unavoidable tree loads and missing
> store-to-load forwarding predate SROA promotion and read-only window
> promotion. See `docs/sroa.md` for the implemented pipeline.

This document analyzes the memory operation patterns in the tree_hash kernel and identifies optimization opportunities to reduce the load instruction count.

## Current Memory Operation Breakdown

| Operation | Count | Source |
|-----------|-------|--------|
| `load_offset` | 4096 | Tree node lookups (scattered) |
| `vload` | 1024 | Loading idx (512) + val (512) |
| `const` | 46 | Hash constants, loop bounds |
| `load` | 4 | Header values |
| `vstore` | 1024 | Storing idx (512) + val (512) |
| **Total loads** | **5170** | |

### How These Numbers Are Derived

- **Batch size**: 256 elements
- **VLEN**: 8 (vector width)
- **Vector batches**: 256 / 8 = 32
- **Rounds**: 16
- **Total vector iterations**: 32 × 16 = 512

Per vector iteration:
- 1 `vload` for idx (from `inp_indices`)
- 1 `vload` for val (from `inp_values`)
- 8 `load_offset` for tree node lookup (vgather pattern - scattered access)
- 1 `vstore` for idx (to `inp_indices`)
- 1 `vstore` for val (to `inp_values`)

---

## Why `load_offset` Cannot Be Reduced

The 4096 `load_offset` instructions come from line 95-96 in `kernels/tree_hash.py`:

```python
node_addr = b.add(forest_values_p, idx, "node_addr")
node_val = b.load(node_addr, "node_val")
```

This is a **scattered memory access** where:
- `node_addr = forest_values_p + idx`
- `idx` is **data-dependent** (computed from the hash result of the previous iteration)

### Why This Cannot Be Vectorized or Reduced

1. **Data-dependent addressing**: Each batch element accesses a different tree node based on its computed index
2. **Pseudo-random access pattern**: The tree path is determined by hash results
3. **No reuse opportunity**: Each round accesses a different tree level (children of previous node)
4. **Fundamental to algorithm**: The tree traversal IS the computation - we cannot skip any node lookups

**Conclusion**: The 4096 `load_offset` instructions are fundamental to the algorithm and cannot be reduced without changing the algorithm itself.

---

## What CAN Be Reduced: vload/vstore of idx and val

### Current Pattern (Inefficient)

Currently, **every iteration** loads and stores idx and val to/from memory:

```python
def batch_body(batch_i, batch_params):
    # Load from memory EVERY iteration
    idx = b.load(idx_addr, "idx")        # vload from inp_indices
    val = b.load(val_addr, "val")        # vload from inp_values

    # ... process (hash computation) ...

    # Store to memory EVERY iteration
    b.store(idx_store_addr, final_idx)   # vstore to inp_indices
    b.store(val_store_addr, val)         # vstore to inp_values
```

After full loop unrolling:
- Round r stores `final_idx` to `inp_indices[batch_i]`
- Round r+1 loads `idx` from `inp_indices[batch_i]` (same address!)

**The compiler does not optimize away this store-then-load pattern** because:
1. Memory operations are treated conservatively
2. SSA form creates fresh values each iteration
3. No store-to-load forwarding optimization exists in the current pipeline

---

## Proposed Optimization: Keep Values in Scratch Across Rounds

### Restructured Algorithm

```python
# 1. INITIAL: Load all batch elements into scratch vectors (ONCE)
idx_vectors = []  # 32 vectors, each holding 8 idx values
val_vectors = []  # 32 vectors, each holding 8 val values
for batch_i in range(0, batch_size, VLEN):
    idx_vectors.append(vload(inp_indices_p + batch_i))
    val_vectors.append(vload(inp_values_p + batch_i))

# 2. PROCESS: All rounds operate on scratch (NO loads/stores to inp_*)
for round in range(rounds):
    for vec_i in range(32):
        # Only the tree lookup needs memory access
        node_vals = vgather(forest_values_p, idx_vectors[vec_i])

        # Hash computation (all in registers/scratch)
        val_vectors[vec_i] = hash(val_vectors[vec_i] ^ node_vals)

        # Update idx in scratch (no memory store!)
        idx_vectors[vec_i] = compute_next_idx(idx_vectors[vec_i], val_vectors[vec_i])

# 3. FINAL: Store all results back to memory (ONCE)
for batch_i in range(0, batch_size, VLEN):
    vstore(inp_indices_p + batch_i, idx_vectors[batch_i // VLEN])
    vstore(inp_values_p + batch_i, val_vectors[batch_i // VLEN])
```

### Key Insight

The `idx` and `val` arrays are only read at the start of all rounds and written at the end. The intermediate stores/loads between rounds are **redundant** - we can keep these values in scratch (registers) throughout all 16 rounds.

---

## Projected Savings

| Operation | Current | Optimized | Savings |
|-----------|---------|-----------|---------|
| `vload` (idx) | 512 | 32 | **480 saved** |
| `vload` (val) | 512 | 32 | **480 saved** |
| `vstore` (idx) | 512 | 32 | **480 saved** |
| `vstore` (val) | 512 | 32 | **480 saved** |
| **Total memory ops** | **2048** | **128** | **1920 saved** |

---

## New Load Count Projection

| Operation | Current | Optimized |
|-----------|---------|-----------|
| `load_offset` | 4096 | 4096 (unchanged) |
| `vload` | 1024 | 64 |
| `const` | 46 | 46 (unchanged) |
| `load` | 4 | 4 (unchanged) |
| **Total loads** | **5170** | **4210** |

---

## Impact on Cycle Count

### Current State
- Total loads: 5170
- Load slot limit: 2 per bundle
- **Minimum bundles (load-limited): 5170 / 2 = 2585**
- Actual bundles: 2607 (within 0.8% of minimum)

### After Optimization
- Total loads: 4210
- **Minimum bundles (load-limited): 4210 / 2 = 2105**
- **Projected improvement: ~19% reduction**

### Can We Reach < 2000 Cycles?

Even with this optimization:
- 4210 loads / 2 slots = 2105 bundles minimum
- Still above 2000 target

To reach < 2000 would require reducing `load_offset` count, which requires algorithmic changes (e.g., different tree structure, caching tree nodes, etc.).

---

## Implementation Approach

### Option 1: HIR-Level Loop Restructuring

Modify `kernels/tree_hash.py` to use loop-carried values:

```python
def build_tree_hash_kernel(...):
    # Load all idx/val into scratch at start
    idx_vals = []
    for batch_i in range(0, batch_size, VLEN):
        idx = b.vload(b.add(inp_indices_p, b.const(batch_i)))
        val = b.vload(b.add(inp_values_p, b.const(batch_i)))
        idx_vals.append((idx, val))

    # Round loop with loop-carried values
    def round_body(round_i, carried_idx_vals):
        new_idx_vals = []
        for vec_i, (idx, val) in enumerate(carried_idx_vals):
            # Process (only tree lookup needs memory)
            node_val = b.vgather(forest_values_p, idx)
            val = hash(b.vxor(val, node_val))
            idx = compute_next_idx(idx, val)
            new_idx_vals.append((idx, val))
        return new_idx_vals

    final_idx_vals = b.for_loop(..., iter_args=idx_vals, body_fn=round_body)

    # Store all results at end
    for batch_i, (idx, val) in zip(range(0, batch_size, VLEN), final_idx_vals):
        b.vstore(b.add(inp_indices_p, b.const(batch_i)), idx)
        b.vstore(b.add(inp_values_p, b.const(batch_i)), val)
```

### Option 2: Compiler Optimization Pass

Add a "store-load forwarding" optimization pass that:
1. Detects store-then-load to same address patterns
2. Replaces the load with the stored value
3. Eliminates dead stores

This is more general but requires dataflow analysis.

---

## Scratch Space Requirements

To keep all idx and val values in scratch:

- 256 idx values + 256 val values = 512 scalar values
- With VLEN=8: 32 idx vectors + 32 val vectors = 64 vectors
- Scratch words needed: 64 × 8 = 512 words
- **SCRATCH_SIZE = 1536 words** (sufficient)

The optimization fits within available scratch space.

---

## Summary

| Metric | Current | Optimized | Improvement |
|--------|---------|-----------|-------------|
| Total loads | 5170 | 4210 | -19% |
| Total stores | 1024 | 64 | -94% |
| Min bundles | 2585 | 2105 | -19% |
| Actual cycles | 2607 | ~2105* | -19% |

*Projected, assuming scheduler achieves near-minimum

### Key Takeaways

1. **`load_offset` (4096) cannot be reduced** - fundamental to tree traversal algorithm
2. **`vload`/`vstore` (2048) can be reduced to 128** - by keeping values in scratch across rounds
3. **Implementation**: Restructure loops to use loop-carried values instead of memory stores/loads
4. **Limitation**: Even optimized, minimum is ~2105 bundles (still > 2000 target)
