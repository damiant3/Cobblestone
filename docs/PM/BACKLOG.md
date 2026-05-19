# Backlog — Outstanding Work

**Updated**: 2026-05-17

## Active — Ongoing

### Compiler

| # | Item | Notes |
|---|------|-------|
| 1 | **Phase discipline rollout** | `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`. All 6 frontend phases have per-phase build + phase-measure + phase-compact (CLs 500, 552, 644). Emitter wall enforced (CL 644). Open: lower/emit isolation, escape invariant, TCO reset removal, survey tightening. |
| 2 | **Per-function emit reclaim hardening** | CL 463 added heap-save/restore around each emit-function. CL 1563 decked all emit-phase durable data (__deck-enter/exit around init, deck-record codegen-carry-forward). |
| 3 | **handler-basic / handler-crossfn batch GPF** | Effect handler tests GPF in batch-compile REPL mode only. Both compile and run correctly standalone (output 42). Issue is REPL heap-reset isolation, not codegen. |
