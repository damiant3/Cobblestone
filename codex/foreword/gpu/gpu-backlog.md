# Foreword Gpu -- open capabilities

Quire-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register any more.
Anything that is this quire's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## Test coverage

1 of 18 library chapters is reached by no computing test (2026-10-31).
`GpuEffect` declares only an effect, so there is nothing to compute. **Open:**
a definition to test or a deletion. `Atomic` is reached through
`ReduceKernelsPtx`, which `codex/test/gpu-histogram` launches on the GPU.

**Open:** the PTX plug refuses a record literal (`record_literal`), so a
`[Device]` kernel cannot take or build a record; scope is a function name
(`atom-add`, `atom-block-add`), not a type.

`BrowserKernels` is graded only by `codex/plugs/wgsl/arms/browser-kernels.mjs`
(WebGPU against host references), and no gate runs it: the release gate has no
browser phase (Damian, main 51669). Run it by hand after a change to the
chapter or the WGSL plug; `coverage-census.ps1` reports the chapter reached by
nothing.

`GemmKernels` and `LayerKernels` are reached through `GemmPtx` and
`LayerKernelsPtx`, which the `codex/test/gpu-*` tests launch on the GPU against
a host reference. Both are true only while every source or plug change
regenerates the `*Ptx` chapter (`codex/plugs/ptx/run.ps1 -Chapter`);
`build/checks/ptx-fresh.ps1` regenerates all 8 generated PTX chapters and reports
each FRESH or STALE (8 fresh, 2026-10-31). **Open:** no gate runs it; run it after
touching a `[Device]` kernel chapter or the PTX plug.
