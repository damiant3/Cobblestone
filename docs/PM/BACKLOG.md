# Backlog — Outstanding Work

**Updated**: 2026-05-25

## Active — Ongoing

### USB Install (Gap 4)

| # | Item | Notes |
|---|------|-------|
| 1 | **End-to-end USB validation** | All driver/integration layers done (MSC, DriveManager, DevConsole, XHCI). Needs physical USB stick test on Asus + Dell. |

### Compiler

| # | Item | Notes |
|---|------|-------|
| 2 | **Phase discipline rollout** | `docs/Designs/Active/Compiler/PHASE-ARCHITECTURE.md`. All 8 frontend phases have per-phase build + phase-measure + phase-compact. RESOLVE and LIFT phases split out (CLs 2135, 2169). Open: deck-record toggle ratchet, escape invariant, TCO reset removal, survey tightening (lex 40x done CL 2306). |
| 3 | **Per-function emit reclaim hardening** | CL 463 added heap-save/restore around each emit-function. CL 1563 decked all emit-phase durable data. |

### Profiler

| # | Item | Notes |
|---|------|-------|
| 4 | **Sampling profiler completion** | Builtins and ISR landed (CLs 2287–2301). Needs host-side histogram reader to aggregate samples against symbol map. |
