# Backlog — Outstanding Work

**Updated**: 2026-05-16

## Active — Ongoing

### Compiler

| # | Item | Notes |
|---|------|-------|
| 1 | **Phase discipline rollout** | `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`. All 6 frontend phases have per-phase build + phase-measure + phase-compact (CLs 500, 552, 644). Emitter wall enforced (CL 644). Open: lower/emit isolation, escape invariant, TCO reset removal, survey tightening. |
| 2 | **Per-function emit reclaim hardening** | CL 463 added heap-save/restore around each emit-function. Working but not yet integrated with the phase-discipline deck model. |
| 3 | **O(n^2) scan cleanup** | CL 996 fixed 4 of 6 Tier 1 sites. CL 1514 fixed `pipe-unique` (sort+dedup). Remaining: Lowering type-param and lookup-subst (bounded at ~5 elements, not urgent). |
| 4 | **handler-basic / handler-crossfn GPF** | Effect handler tests GPF during compilation (`.fatal` sidecar, CL 1505). Codegen bug with simple handler bodies — investigate when bandwidth allows. |
| 5 | **list-fold-indexed 3-arg lambda** | `Iterate.codex` fold produces garbage when passed a 3-arg lambda. 1- and 2-arg lambdas work. Possible calling convention or type-checker issue. |
