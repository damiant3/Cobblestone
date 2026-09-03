# Fireworks -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

An entry says what is still missing and nothing else, a closed entry is
DELETED rather than annotated, and a gap that is still real is never
quietly dropped.

The app is two Codex chapters and a page that owns neither the physics nor
the city. `kernels/FireworksSimKernel.codex` goes through the wgsl plug to the
GPU particle integration; `FireworksShow.codex` goes through the wasm plug to
the skyline mesh and the rooftops, written into linear memory the page uploads
verbatim. `build-wasm.ps1` builds the second. `p4 edit` the generated `.wgsl`
before regenerating it: it is tracked, and the plug otherwise fails with an
access-denied that reads like a plug fault (P-NOEDIT).

| # | Capability | State of the gap |
|---|---|---|
| FW-1 | **Bright rectangular artifacts at the top of the frame** | Above the tallest towers, on every city. Believed to be tower crowns out of `emit-roof` in `FireworksShow.codex`, but the colours those vertices are given are dark and the artifacts are bright, so that explanation does not survive contact with the data. **Cause NOT isolated.** Three rounds of adjusting constants and looking at screenshots produced two hypotheses and both were wrong when tested; the next step is to dump the vertex buffer out of WASM linear memory and read the coordinates and colours of the crown triangles directly, rather than tune anything else. |
| FW-2 | **Palm and willow shells read pale rather than coloured** | The other burst types (ring, peony, crackle, chrysanthemum) show their colour. Removing pure white from the palette, weighting the shell mix away from the willow types, widening the palm arms and shortening the ascent trail each helped a little and not one of them explained it. Same instruction as FW-1: read the packed colour and intensity words the kernel writes for one identified burst, instead of reasoning from the picture. |
| FW-3 | **The shell scheduler and the burst shapes are still JavaScript** | The physics and the city are Codex; the launch cadence, the shell integration and the six burst shapes are not, which is a hole in the same claim that moved the skyline out of the page (main 22278). They want a third export on `FireworksShow.codex` writing spawn records into linear memory in the GPU's 8-word particle layout, so the page copies them straight into the state buffer. Not asked for yet. |
| FW-4 | **The page's render and post shaders are hand-written WGSL** | Sky, city, particles, water, bright pass, blur and composite. The wgsl plug lowers COMPUTE stages only, so this is the plug's gap rather than the app's, and it is what the gpushow README already says about render-stage emission. Nothing here can close without that. |
| FW-5 | **The bare-metal app and the web app share a name and nothing else** | `Fireworks.codex` draws the USA 250 scene on the host GPU triangle rasterizer through the codex-vm command buffer; the web app rebuilt the same idea for WebGPU. Two skylines, two particle systems, two sets of landmarks. Neither is wrong and the duplication is real. |
