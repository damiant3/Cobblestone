# Backlog — Outstanding Work

**Updated**: 2026-05-19

## Active — Ongoing

### Compiler

| # | Item | Notes |
|---|------|-------|
| 1 | **Phase discipline rollout** | `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`. All 6 frontend phases have per-phase build + phase-measure + phase-compact (CLs 500, 552, 644). Emitter wall enforced (CL 644). Open: lower/emit isolation, escape invariant, TCO reset removal, survey tightening. |
| 2 | **Per-function emit reclaim hardening** | CL 463 added heap-save/restore around each emit-function. CL 1563 decked all emit-phase durable data (__deck-enter/exit around init, deck-record codegen-carry-forward). |

## Recently Closed

| # | Item | Resolution |
|---|------|-----------|
| 3 | ~~handler-nested batch GPF~~ | **Fixed CL 1845.** Root cause: `lookup-expr-type` in Unifier used non-short-circuit `&` to guard a `list-at` access after binary search. When the key was not found (`pos == len`), `list-at entries len` read one element past the list into stale heap data from the previous REPL compilation. Fix: split into nested `if` so the access is only reached when `pos < len`. Also likely root cause of the plug crash (PLUG-CRASH-INVESTIGATION.md). Sweep: 105/105 pass, 0 fail. |
