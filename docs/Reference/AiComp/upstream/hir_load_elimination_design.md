# HIR Load Elimination Design

This document designs a new **HIR-level load-elimination pass** that forwards values from a dominating store to a later load of the same address when it is safe to do so.

The pass is intended to eliminate the common pattern:

```text
store(addr, new_idx)
...
x = load(addr)
```

If no intervening store may alias `addr`, then `x` is defined by `new_idx`. We can remove the load and replace all uses of `x` with `new_idx`.

---

## Goals and Non-Goals

Goals:

- Remove redundant loads by forwarding a known stored value.
- Use a conservative alias analysis based on **base pointer + constant offset**.
- Be correct in the presence of loops and branches (conservative when unsure).
- Run after CSE on HIR to leverage canonicalized address expressions.
- Optimization target: `python perf_takehome.py --print-metrics` reports **cycles < 2200**.

Non-goals:

- Full alias analysis across arbitrary pointer arithmetic.
- Store elimination or memory SSA construction.
- Cross-iteration forwarding in loops (initially).

---

## Where It Runs in the Pipeline

This is a **HIR → HIR** pass. It should run **after CSE** (the final CSE in the current pipeline), and before later HIR passes like `MADSynthesisPass` and the final `DCEPass` cleanup.

Reasoning:

- CSE makes address expressions more canonical and removes redundant computations, improving alias analysis hit rate.
- The pass replaces uses and removes loads; a later DCE can clean up now-unused ops.

---

## Alias Analysis Design (HIR Addresses)

### Address Normalization

We normalize HIR address expressions into a canonical form:

```text
Addr = Base + ConstOffset
```

Where:

- `Base` is an SSA value that represents a pointer-like root.
- `ConstOffset` is a compile-time integer.

Normalization rules:

1. If `addr` is a base pointer SSA value, treat it as `Base = addr`, `ConstOffset = 0`.
2. If `addr` is `+(base, #c)` or `+(#c, base)`, fold into `Base = base`, `ConstOffset = c`.
3. If `addr` is `+(addr1, #c)` and `addr1` is already `(Base + c1)`, fold to `(Base + c1 + c)`.
4. If `addr` is anything else (non-constant offset, unknown arithmetic), mark as **Unknown**.

This uses the HIR `Op` tree and `UseDefContext` to inspect definitions of address SSA values.

### Alias Categories

We return one of:

- `MustAlias`: guaranteed same location.
- `NoAlias`: guaranteed different locations.
- `MayAlias`: cannot prove either.

Rules:

- If either address is **Unknown**, result is `MayAlias`.
- If both are normalized, have the **same Base**, and offsets are equal, result is `MustAlias`.
- If both are normalized, have the **same Base**, and offsets are different, result is `NoAlias`.
- If Bases differ, default to `MayAlias` for safety.
- Refinement for pointer roots: if both Bases are derived from `load(#const)` of *different* constant header slots (memslot roots), treat as `NoAlias`.

This satisfies the requirement:

- `v5:inp_indices_p` and `+(v5:inp_indices_p, #1)` are `NoAlias`.
- `+(v5:inp_indices_p, #1)` and `+(v5:inp_indices_p, #1)` are `MustAlias`.

### Vector Ops

Initial scope is scalar `load`/`store`. If extended to vector ops:

- `vload` / `vstore` at `Base + C` access a **range** `[C, C + VLEN - 1]`.
- Two vector accesses are `NoAlias` when ranges do not overlap and Base matches.
- `vgather` is treated as **Unknown** (may alias everything) unless a lane-wise constant analysis exists.

---

## Memory Dependency Analysis (with Alias Caching)

### Purpose

Determine, for each `load`, the **nearest dominating store** that:

1. Must-aliases the load address, and
2. Is not invalidated by any intervening store that **may-aliases** the load address.

If such a store exists, the load can be replaced by the stored value.

### Caching Alias Results

Alias queries are expensive when repeated. We cache results in a map:

- Key: `(addr_key_a, addr_key_b)` where `addr_key` is the normalized address form.
- Value: `MustAlias | NoAlias | MayAlias`.

The memory dependency analysis owns this cache and consults it for every alias query.

### Memory State Tracking

We analyze a statement list in program order with a simple state:

- `last_store[addr_key] -> StoreInfo` for normalized addresses.
- `unknown_clobber` flag, set by any store with Unknown address.

On a `store`:

1. Compute normalized address key.
2. If Unknown, set `unknown_clobber = True`.
3. Else update `last_store[addr_key]`.

On a `load`:

1. Compute normalized address key.
2. If Unknown or `unknown_clobber` is set, we do not forward.
3. Else find `last_store[addr_key]`.
4. Ensure there was no intervening store that may-aliases the load address.

The last step requires tracking may-alias stores. Two safe options:

- Conservative option: treat any store to a different Base as `MayAlias`, which prevents forwarding across unrelated stores.
- Better option: use alias analysis and a **store history list** since the last `last_store[addr_key]`, scanning backward and consulting the alias cache. This is still efficient because alias queries are cached and the window is typically small.

### Structured Control Flow

HIR has `ForLoop` and `If` constructs. We keep correctness by being conservative.

For `If`:

- Analyze `then` and `else` with cloned memory states.
- After the `If`, reset memory state (no forwarding across merges). This is conservative but correct.

For `ForLoop`:

- Do not forward loads across loop boundaries.
- Start loop body with a fresh memory state.
- After the loop, reset memory state (no forwarding across iterations).

This mirrors the CSE pass’s conservative treatment of memory epochs.

---

## Load Elimination Transformation

### Detection

For each `load` in a statement list, the memory dependency analysis returns one of:

- `NoDef`: no dominating store proven.
- `Def(StoreInfo)`: a dominating store with a safe dependence.

`StoreInfo` includes:

- `store_stmt`: the `Op("store", ...)` that defines the memory.
- `stored_value`: the value operand of the store.

### Rewrite

If a load has `Def(StoreInfo)`:

1. Replace all uses of the load result SSA with `stored_value`.
2. Remove the load statement from the list.

This uses `UseDefContext.replace_all_uses(...)` with `auto_invalidate=False`, mirroring the CSE pass pattern.

### Safety Conditions

We only perform the rewrite when all of these hold:

- The store must-aliases the load address.
- No intervening store may-aliases the load address.
- The store is in the same linear region of control flow (no cross-branch or cross-loop forwarding unless proven by merge logic).

---

## Example

Original HIR pattern:

```text
v10 = +(v5:inp_indices_p, #7)
store(v10, v42:new_idx)
...
v11 = load(v10)
```

Alias analysis:

- `v10` normalizes to `(Base=v5:inp_indices_p, Offset=7)`.
- The store must-aliases the load.
- If no intervening may-alias store exists, `v11` is defined by `v42`.

Transformed HIR:

```text
v10 = +(v5:inp_indices_p, #7)
store(v10, v42:new_idx)
...
# load removed
# all uses of v11 replaced with v42
```

---

## Complexity

- Address normalization is O(1) amortized with memoization per SSA value.
- Alias checks are O(1) with cache hits.
- Memory dependency analysis is O(N * W) where W is the number of stores scanned in a small window; caching keeps it practical for unrolled HIR.

---

## Future Extensions

- Track range-based aliasing for `vload` / `vstore` precisely.
- Add store elimination when a store is overwritten before any load.
- Introduce memory SSA to enable cross-branch forwarding when safe.
- Prove non-aliasing across distinct base pointers loaded from known header slots.

---

## Pass Name Proposal

`load-elim` (HIR → HIR)

Metrics to report:

- `loads_analyzed`
- `loads_eliminated`
- `alias_queries`
- `alias_cache_hits`
