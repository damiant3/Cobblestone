# Instruction Scheduling & Bundling Design (LIR → MIR)

This document describes an instruction scheduling pass for this compiler’s VLIW SIMD target. It is written to match the **current codebase** and its IR pipeline:

`HIR -> LIR -> (LIR scheduling + lowering) -> MIR (bundles) -> MIR regalloc -> VLIW bundles`

The scheduler is specified as a **LIR → MIR lowering pass**: it runs on LIR basic blocks and produces MIR bundles. When this pass is enabled, the existing `lir-to-mir` lowering pass must be disabled (mutual exclusion) so only one LIR→MIR pass runs.

---

## Background: Target Semantics That Drive Scheduling

The ISA model in `docs/VLIW_ISA.md` and the simulator in `problem.py` impose the key constraint:

- **All slots in a bundle read pre-bundle state; writes commit at bundle end.**
  - Therefore, **same-bundle RAW forwarding does not exist**: if instruction B uses a value defined by A, A and B **must not** be placed in the same bundle.

Also:

- Each bundle has **per-engine slot limits** (mirrored in `compiler/mir.py` as `MBundle.SLOT_LIMITS`).
- Control-flow ops do **not cancel** other slots in the same bundle; they affect the next fetch only.
- Store visibility: loads in the same bundle do **not** see stores from that bundle (stores commit after bundle).

We treat one `MBundle` as one “cycle” from the scheduler’s perspective.

---

## Goals / Non-Goals

### Goals

- Produce correct MIR bundles (respecting the “pre-bundle read / post-bundle write” model).
- Maximize instruction-level parallelism under slot limits.
- Remain **deterministic** (same input IR → same bundles).
- Run per basic block (no cross-block code motion).
- Run **before** MIR register allocation.

### Non-Goals (for the first implementation)

- Global scheduling across blocks / traces.
- Sophisticated memory alias analysis (we keep conservative ordering).
- Explicit modeling of multi-cycle latencies: in this ISA, every instruction is effectively “1 bundle” long; the only timing constraint we need is that **RAW cannot be satisfied within the same bundle** (no same-bundle forwarding).

---

## Where This Pass Lives in the Pipeline

### New pass: LIR instruction scheduling + MIR construction

Add a new lowering pass (name: `inst-scheduling`) that:

- **Input:** `LIRFunction`
- **Output:** `MachineFunction` (MIR)
- **Responsibility:** convert LIR to `MachineInst`, schedule into `MBundle`s, and construct `MachineBasicBlock`s and CFG metadata.

This is a `LIRToMIRLoweringPass` in the current pass manager type system.

### Mutual exclusion with `lir-to-mir`

Both `inst-scheduling` and `lir-to-mir` are LIR→MIR lowering passes. The pipeline must ensure **exactly one** is enabled:

- If `inst-scheduling` is **enabled**, then `lir-to-mir` must be **disabled**.
- If `inst-scheduling` is **disabled**, then `lir-to-mir` remains enabled (baseline lowering behavior).

To make disabling `lir-to-mir` workable, the pipeline must include `inst-scheduling` as an alternative LIR→MIR step (otherwise the pipeline would be stuck in LIR and fail type checking before MIR regalloc).

---

## Dependencies: Use/Def, Memory, and Barriers

### Instruction representation

Although the pass runs on LIR, it should schedule a `MachineInst` view of each instruction:

- Convert each `LIRInst` to `MachineInst` (preserving opcode/dest/operands/engine).
- Schedule the `MachineInst` list, using:
  - `MachineInst.get_defs()` and `MachineInst.get_uses()` for scratch use/def,
  - opcode/engine for slot constraints (`MBundle.SLOT_LIMITS`).

This keeps dependency modeling aligned with MIR semantics (e.g., `LOAD_OFFSET` use/def interpretation) and produces MIR bundles directly.

### Why “one global block DAG” (not per-root DDGs)

The repo has a generic DDG builder in `compiler/ddg.py` for HIR/LIR analysis, but MIR scheduling needs:

- conservative memory ordering and barriers,
- edge *delays* (same-bundle allowed vs forbidden),
- fast incremental “ready” updates.

Therefore we build one dependency graph for the whole block (a DAG over instruction indices) and schedule from a single ready queue. This naturally handles “multiple DDGs” (multiple roots) without a separate “scan DDGs and steal from subsequent DDGs” mechanism.

---

## Dependencies and “Delay” (Bundle Distance)

The scheduler models constraints as edges `u -> v` with a minimal separation in **bundles**:

- `delay = 1`: `v` cannot be scheduled in the same bundle as `u`; it must be in a later bundle.
- `delay = 0`: `v` may be scheduled in the same bundle as `u` (subject to slot availability and internal bundle safety rules).

### RAW (the only *latency* constraint)

Because every instruction’s latency is 1 bundle and there is no same-bundle forwarding, the only *timing* constraint the scheduler needs is:

- **RAW (def -> use)**: `delay = 1`
  - Justification: same-bundle reads cannot see same-bundle writes (writes commit after the bundle).

### Memory and other side effects (correctness, not latency)

If **LIR scratch is SSA** (each scratch is defined at most once per basic block), then scratch redefinition hazards (WAW/WAR) do not arise within a block, and **RAW is sufficient for scratch correctness**.

However, memory operations are still side-effecting, and a scheduler that reorders them can change program behavior unless it proves non-aliasing. This design keeps memory correctness separate from “latency”:

- **Minimum safe default:** keep program order among memory ops by adding conservative edges.
- **Performance option:** relax those edges only when the compiler can prove non-aliasing (future work).

Conservative memory edges:

- **store -> later load**: `delay = 1` (must not co-issue; loads cannot observe same-bundle stores).
- **store -> later store**: `delay = 0` (may co-issue if slots allow, but keep original order within the store engine’s slot list for determinism).
- **load -> later store**: `delay = 0` (may co-issue; loads read pre-bundle memory).

### Barriers and terminators

- `HALT`/`PAUSE` behave as barriers: they must remain after all previous instructions and before all following instructions.
- Basic block terminators (`JUMP`, `COND_JUMP`, `HALT`, `PAUSE`) must remain last in the block.

Implementation simplification (consistent with current behavior): schedule the terminator as the final bundle in the block.

---

## Bundle Internal Safety Rules

In addition to edge delays, we enforce per-bundle safety:

- No same-bundle RAW by construction (delay=1 for RAW).
- Because LIR scratch is SSA, we do not need WAW/WAR edges for scratch within a block.
- For memory correctness, we still:
  - avoid same-bundle “store then load” (store→load delay=1),
  - keep store slot order deterministic when co-issuing multiple stores.

---

## Scheduling Algorithm: Delay-Aware List Scheduling with Packing

### State

For a basic block with N instructions (excluding terminator):

- `preds[v]`: predecessor indices.
- `succs[v]`: successor indices with per-edge delay.
- `remaining_preds[v]`: count of predecessors not yet scheduled (topological readiness).
- `earliest_bundle[v]`: earliest bundle index in which `v` may be scheduled due to delay-1 constraints.
- `scheduled[v]`: whether `v` has been scheduled.

We maintain a deterministic ready structure:

- `ready`: all nodes with `remaining_preds==0` and `earliest_bundle <= current_bundle`.
- tie-breaking by priority key (see below), then by original index for stability.

### Priorities

Use a lexicographic key, descending:

1. **critical path height** (longest distance to any root under edge delays; precomputed once per block),
2. **engine pressure** (prefer scarce engines first: flow > load/store > valu > alu),
3. **original instruction index** (smaller first) to ensure determinism.

### Bundle construction (incremental)

Unlike a one-pass scan of the ready set, we fill a bundle incrementally:

1. Start new bundle `b`.
2. While there exists a ready instruction that fits `b`’s remaining slots:
   - pick best-priority ready instruction that fits,
   - emit it into bundle,
   - mark it scheduled,
   - update successors:
     - decrement `remaining_preds`,
     - update `earliest_bundle[succ] = max(earliest_bundle[succ], current_bundle + delay(u->succ))`,
     - if succ now becomes eligible, add to `ready`.
3. If no further instruction fits, finalize bundle and advance `current_bundle += 1`.

This immediate-update loop is important because `delay=0` edges can unlock more same-bundle candidates.

### Pseudocode

```text
build_graph(insts):
  add RAW edges with delay=1
  add memory edges (store->load delay=1, load->store delay=0, store->store delay=0)
  add barrier edges as needed

schedule_block(insts):
  init remaining_preds, earliest_bundle=0
  ready = {v | remaining_preds[v]==0 and earliest_bundle[v]==0}
  current_bundle = 0
  out = []

  while not all scheduled:
    b = empty bundle
    refresh ready = {v | remaining_preds[v]==0 and earliest_bundle[v] <= current_bundle and not scheduled[v]}
    while exists v in ready that fits b:
      v = argmax(priority(v)) among fit candidates
      emit v into b
      scheduled[v]=true; remove v from ready
      for (v -> s, delay) in succs[v]:
        remaining_preds[s]--
        earliest_bundle[s] = max(earliest_bundle[s], current_bundle + delay)
        if remaining_preds[s]==0 and earliest_bundle[s] <= current_bundle:
          ready.add(s)
    out.append(b)
    current_bundle++

  return out + [terminator bundle]
```

---

## Determinism Guarantees

To ensure stable output across runs:

- Use a deterministic priority key with the original instruction index as the final, unique tie-breaker. The ready set may therefore be iterated directly without changing the selected node.
- For same-bundle store ordering (if `delay=0` permits co-issuing stores), preserve original order within the engine’s slot list.

---

## Automatic Stream Staggering

`stream_stagger` accepts either a non-negative integer for a manually selected
height offset or `"auto"` for candidate search. Auto mode always includes the
unstaggered schedule. It searches only when the baseline is sufficiently far
from its resource/critical-path lower bound or approaches the scratch-pressure
guard, then selects a pressure-safe candidate with the fewest bundles.

The heuristic controls are explicit `inst-scheduling` options in
`compiler/pass_config.json`:

| Option | Default | Meaning |
| --- | ---: | --- |
| `stream_stagger_threshold` | `12` | Dependence-graph degree above which a node is treated as shared infrastructure. |
| `stream_stagger_auto_min_gap_pct` | `25` | Minimum baseline gap above the block lower bound that triggers search. |
| `stream_stagger_auto_pressure_headroom` | `64` | Scratch words reserved when deciding whether a candidate is pressure-safe. |
| `stream_stagger_auto_candidate_start` | `1` | First stagger strength to evaluate. |
| `stream_stagger_auto_candidate_multiplier` | `2` | Geometric multiplier for subsequent strengths. |
| `stream_stagger_auto_candidate_max` | `null` | Maximum strength; `null` uses the block critical-path height. |
| `stream_stagger_auto_direction` | `"auto"` | `auto`, `unidirectional`, `bidirectional`, or `both`. |

When `stream_stagger` is an integer, `stream_stagger_bidirectional` retains its
manual-mode meaning and the `stream_stagger_auto_*` options are ignored.

---

## Complexity

Per block:

- Graph building is roughly `O(N * U)` where `U` is number of defs/uses tracked (using maps for last-def and use lists).
- Delayed-ready activation uses a heap. Candidate selection scans the current ready set, so worst-case scheduling remains quadratic in block size.

---

## Metrics and Debuggability

Reuse/extend existing LIR→MIR metrics:

- total bundles
- total instructions
- packing ratio (insts / bundles)
- bundle size histogram

Optional additional metrics:

- average slot utilization per engine
- number of delay-0 edges exploited (co-issue opportunities)
- critical path length estimate

For debugging, a “no scheduling” mode should remain available, but it should be expressed as **pass selection**:

- run baseline `lir-to-mir` (no scheduling / 1-inst bundles), or
- run `inst-scheduling` (scheduling enabled).

Avoid putting multiple independent scheduling toggles in different passes; the pipeline should select exactly one LIR→MIR lowering strategy.

---

## Test Plan (must not touch `tests/`)

Add regression tests under `compiler/tests/` that:

1. **No same-bundle RAW**: construct a tiny LIR block where `b = a + 1` uses `a`, and assert the `const a` and `add` do not appear in the same MIR bundle.
2. **Memory store->load separation**: ensure a store followed by a load cannot be co-issued in one bundle.
3. **Allow load->store co-issue**: two independent instructions `load x` and `store y` can share a bundle when slots permit.
4. **Determinism**: compile the same LIR twice and assert identical bundle layouts.

These tests can compile LIR→MIR directly via the enabled LIR→MIR lowering pass (baseline `lir-to-mir` or `inst-scheduling`), similar to how existing MIR tests compile via `LIRToMIRPass` in `compiler/tests/test_mir_codegen.py`.

---

## Future Work

- Light alias analysis / memory disambiguation (tighten conservative memory edges).
- Optional “pack with terminator” optimization (co-issue independent ops with the final branch/jump).
- Better heuristics: register pressure-aware scheduling (once vreg pressure estimates exist pre-regalloc).
- Cross-block scheduling (trace scheduling) for hot paths (more complex, likely unnecessary initially).
