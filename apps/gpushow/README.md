# gpushow

A WebGPU technique showcase whose GPU compute shaders are **generated from
Codex `[Device]` source** by a self-hosted transpiler plug -- the browser-
graphics analogue of what the PTX and SPIR-V plugs do for GPU compute.

See `DESIGN.md` for architecture and `PLAN.md` for milestones/status.

## The pipeline

```
apps/gpushow/kernels/<K>.codex        [Device]-effected compute in Codex
  --> build/compile.ps1 -IrCce  -->   IR (CCE)
  --> codex/plugs/wgsl (wgsl-plug.cdx) -->  <K>.wgsl   (WGSL compute shader)
  --> apps/gpushow/web/<demo>.html  -->  WebGPU compute + render  -->  pixels
```

The compute pass is lowered from the same typed AST the Codex compiler uses;
only the render shader and host wiring are hand-written (render-stage WGSL
emission is a later plug feature).

The rules the plug is held to are `docs/Reference/WgslSpec.md`. A construct it
cannot lower is refused by name and no `.wgsl` is written, so a failed run
leaves the previous shader in place rather than a half-lowered one; the paired
arms are `codex/plugs/wgsl/arms/`.

## Demos

| Demo | Kernel | What it shows |
|------|--------|---------------|
| Fireworks | `kernels/FireworksKernel.codex` | one thread per spark, ballistic burst, packed storage buffer, additive quads |
| Swarm | `kernels/SwarmKernel.codex` | 8000 particles, double-buffered, curl-y procedural flow field |

## Build a kernel to WGSL

```powershell
# build the plug once
pwsh codex/plugs/wgsl/build.ps1
# compile a kernel to WGSL
pwsh codex/plugs/wgsl/run.ps1 -Src apps/gpushow/kernels/SwarmKernel.codex `
    -Out apps/gpushow/kernels/SwarmKernel.wgsl
```

## Validate / run headless

Chrome (installed) is the WGSL validator and the runtime -- WebGPU needs a
secure context, so the tools load a `file://` or `localhost` page, never
`about:blank`.

```powershell
# validate a shader against Chrome's real WGSL compiler (getCompilationInfo)
node apps/gpushow/tools/validate.mjs apps/gpushow/kernels/SwarmKernel.wgsl
# serve the app on localhost, run a demo headless, screenshot the canvas
node apps/gpushow/tools/shoot.mjs /web/swarm.html shot.png
```

Open `web/index.html` (served from the app root, e.g. via `shoot.mjs`'s server
or any static server) for the gallery. Screenshots of each demo are in
`screenshots/`.
