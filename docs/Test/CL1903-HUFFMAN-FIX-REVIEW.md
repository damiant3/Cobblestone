# CL 1903 — Huffman Tree + Graph DFS Fixes

**Author**: reek
**Date**: 2026-05-20
**Status**: NOT COMPILED — requires seed rebuild (foreword module changes)

## Summary

Three bugs across two foreword modules:
1. Two bugs in `codex/foreword/compress/Huffman.codex` made the tree
   builder and code generator non-functional for any input with more
   than one unique byte.
2. One bug in `codex/foreword/core/Graph.codex` where DFS visited state
   was not propagated across neighbor traversals, causing duplicate visits.

## Bug 1: `HuffBranch` stored stale indices

**Before**: `HuffBranch (Integer) (Integer) (Integer)` — stored the
frequency and two indices into the flat `nodes` list.

**Problem**: `huff-merge-loop` called `huff-remove-and-add` which used
`huff-compact` to remove an element from the list, shifting all
subsequent elements. Indices stored in previously-created `HuffBranch`
nodes became stale after compaction. A tree with 3+ unique bytes would
have branches pointing to wrong nodes or out of bounds.

**Fix**: `HuffBranch (Integer) (HuffNode) (HuffNode)` — stores child
nodes directly as a recursive variant. No index invalidation possible.

## Bug 2: `huff-walk-tree` never recursed

**Before**:
```
huff-walk-tree (nodes) (bits) (depth) (acc) =
    ...
    is HuffBranch (freq) (left) (right) -> acc
```

On encountering a branch, returned `acc` unchanged. The code table was
always empty for trees with branches (i.e., any input with 2+ unique
bytes).

**Fix**: Renamed to `huff-walk-node`, takes a single `HuffNode` instead
of a list. Recurses into both children:
```
is HuffBranch (freq) (left) (right) ->
    let acc2 = huff-walk-node left (bits * 2) (depth + 1) acc
    in huff-walk-node right (bits * 2 + 1) (depth + 1) acc2
```

Left child gets `bits * 2` (append 0), right child gets `bits * 2 + 1`
(append 1). Standard Huffman code assignment.

## Changes

| Function | Change |
|----------|--------|
| `HuffBranch` | `(Integer) (Integer) (Integer)` → `(Integer) (HuffNode) (HuffNode)` |
| `huff-merge-loop` | Extract min nodes by value, build recursive tree, no index tracking |
| `huff-find-min` | Unchanged (still finds index of minimum-frequency node) |
| `huff-find-min2` | **Removed** — no longer needed; second min found after removing first |
| `huff-remove-and-add` | **Removed** — replaced by `huff-remove-at` |
| `huff-compact` / `huff-compact-loop` | **Removed** — index-based compaction no longer needed |
| `huff-remove-at` / `huff-remove-at-loop` | **Added** — remove element at index, return new list |
| `huff-walk-tree` | **Removed** |
| `huff-walk-node` | **Added** — recursive tree walk, assigns bit codes |
| `huff-build-codes` | Updated to call `huff-walk-node` on root node |

## Risk Assessment

**Memory**: `huff-merge-loop` creates a new list per iteration via
`huff-remove-at` + `list-push`. For n unique bytes (max 256), that's
n-1 iterations with lists of decreasing size. Worst case: ~65K list
cells total. No blow-up.

**Time**: O(n^2) from repeated find-min + remove (n scans of
decreasing-length list). n <= 256, so ~32K comparisons worst case.
Acceptable. A priority queue would give O(n log n) but is unnecessary
for this input size.

**Recursive variant risk**: `HuffBranch` now contains `HuffNode`
values, making the type recursive. The compiler handles recursive
variants (e.g., `LinkedList`), but if there are edge cases in variant
lowering for deeply nested trees, this could surface them. Maximum tree
depth is 255 (degenerate case with 256 unique bytes at exponentially
increasing frequencies). Typical depth is 8-12.

## Seed Rebuild

This is a foreword module change. The compiled Huffman code is embedded
in the seed. A seed rebuild is required to incorporate this fix, but the
existing seed will still compile correctly — the compiler does not call
any Huffman functions.

## Bug 3: Graph DFS visited state lost across neighbors

**File**: `codex/foreword/core/Graph.codex`

**Before**: `graph-dfs-visit` returned `List Integer` (just the result).
`graph-dfs-neighbors` passed the original `visited` list to each
neighbor instead of the updated one:
```
let updated = graph-dfs-visit g (e.target) visited result
in graph-dfs-neighbors g edges (i + 1) visited updated
                                       ^^^^^^^ stale!
```

When visiting neighbor B after neighbor A, the DFS into A may have
visited vertices C, D, E. But B's DFS starts with the pre-A visited
state, causing C, D, E to be visited again.

**Fix**: Added `DfsState` record (matching the existing `TopoState`
pattern used by `graph-topo-visit`). Both `graph-dfs-visit` and
`graph-dfs-neighbors` now return `DfsState`, propagating the updated
visited list:
```
let state = graph-dfs-visit g (e.target) visited result
in graph-dfs-neighbors g edges (i + 1) (state.dfs-visited) (state.dfs-result)
```

`graph-dfs` unwraps the final `DfsState` to return `List Integer`,
preserving the public API.

**Memory**: One extra record allocation per DFS call. For a graph with
V vertices and E edges, that's O(V + E) records of 2 fields each.
No blow-up.

## Bug 4: Bresenham line dead branch

**File**: `codex/foreword/game/Bresenham.codex`

`bres-line-loop` lines 27-33 and 34-39 had identical code in both
branches of `if x == x1`. The outer condition was dead — both sides
performed the same error-stepping and recursive call.

**Fix**: Collapsed to `if x == x1 & y == y1 then acc2 else <step>`.
Removes 6 duplicate lines.

## Bug 5: HexFormat hex-dump spacing

**File**: `codex/os/dev/HexFormat.codex`

`hex-dump-byte` had `if i == 8` branch identical to `else` — both
used single-space separator. Standard hex dumps use double space at
byte 8 to visually separate the two halves of a 16-byte line.

**Fix**: Changed `i == 8` branch from `" "` to `"  "` (double space).

## Bug 6: Convolution window div-by-zero

**File**: `codex/foreword/signal/Convolution.codex`

`window-hamming-loop` and `window-hanning-loop` both computed
`i * 6283 / (n - 1)`. When `n = 1`, this divides by zero.

**Fix**: `let denom = if n <= 1 then 1 else n - 1`. For n=1, the
single window coefficient gets cos(0) = 1000, yielding w=1000 for
Hanning and w=1000 for Hamming — correct single-sample behavior.

## Not Fixed

`huff-tree-depth` (line 109) returns `tree.count` (node count, always 1
after merging), not the actual tree depth. Misleading name but not a
correctness bug. Left as-is per the one-thing-at-a-time rule.
