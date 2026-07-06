# gpushow -- Project Plan

Goal: a WebGPU technique gallery whose shaders are generated from Codex
`[Device]` AST via a new WGSL plug. First flagship demo: **boids / N-body
flocking**. See DESIGN.md for architecture.

Ship-working-software discipline: every milestone ends with something that
runs and is verifiable (Virtue 1). Proof of each shader step = Chrome headless
WebGPU (`getCompilationInfo` for validity, canvas screenshot for render).

## Milestones

### M0 -- Get the seed onto fester. DONE (2026-07-05, fester CL 7126)
Merge-down main->fester brought in val's `apps/fireworks/` (the compute-only
`[Device]` kernel seed) + blu's compiler work + seed #336. One-pass fixed
point + BVT green. `apps/fireworks/FireworksKernel.codex` is the starting
kernel shape.

### M1 -- The WGSL plug, proven on the existing fireworks kernel. DONE (2026-07-05)
Built `codex/plugs/wgsl/`:
- [x] `WgslPlug.codex` -- copy of `SpirvPlug.codex`, `emit-wgsl-chapter`.
- [x] `WgslEmitter.codex` -- structured WGSL (no SSA): `if`/`else`, `select`
      for value-position if, native operators, `fn`/`return`. GPU semantics:
      kernel = trailing `gid` param; buffer params (device-load/store bases) ->
      `@group(0) @binding(k) var<storage, read_write> <kernel>_<p>_buf`; scalar
      params -> a `U_<kernel>` uniform struct; `gid` from
      `@builtin(global_invocation_id)`. Reachability filter drops unused
      foreword glue. device-load -> `buf[i]`, device-store -> `buf[i] = v;`.
- [x] `build.ps1` + `run.ps1` -- copies of spirv's, `spirv`->`wgsl`.
- [x] Built the plug CDX; emits `apps/gpushow/kernels/FireworksKernel.wgsl`
      (both `fw_burst_spark_main` + `fw_integrate_main` compute entries).
- [x] Validator `apps/gpushow/tools/validate.mjs` -- drives installed Chrome
      via CDP (node built-ins only), `createShaderModule` +
      `getCompilationInfo()`. Must load a `file://` page (secure context) so
      `navigator.gpu` is exposed; `about:blank` is not secure.
**PROVEN:** `adapter: nvidia lovelace`, WGSL VALID: 0 errors on the real
RTX 4060 Ti. Codex [Device] AST -> valid WGSL, confirmed by a real WGSL
compiler.

### M2 -- First browser pixels from a Codex-generated shader. DONE (2026-07-05)
`apps/gpushow/web/fireworks.html`:
- [x] fetches the generated `/kernels/FireworksKernel.wgsl`, builds a compute
      pipeline on `fw_burst_spark_main` (checks getCompilationInfo).
- [x] compute pass (N=2600 sparks, one thread each) packs each spark's
      (bright,x,y) into out_buf[gid]; uniform = {cx,cy,frame,nspark}.
- [x] render pass unpacks out_buf (bitcast u32 bitfield: py=bits0-11,
      px=12-23, bright=24-31) and draws additive quads (hand-WGSL render
      shader -- render-stage emission is a later plug extension).
- [x] `apps/gpushow/tools/shoot.mjs` serves the app on localhost (secure
      context + lets the page fetch the wgsl), runs headless Chrome, waits for
      a mid-burst frame, and Page.captureScreenshot -> PNG.
**PROVEN:** `screenshots/fireworks-m2.png` -- a radial dome of sparks at
frame 98, the ballistic burst computed entirely by the Codex-generated
compute shader running in WebGPU.

### M3 -- Swarm (flow-field), the flagship's first form. DONE (2026-07-05)
True neighbour-based boids needs a per-thread loop over the state buffer --
which is recursion in Codex, requiring recursion-to-loop lowering + storage-
pointer helper params in the plug (see M3-next). So the flagship lands first
as a flow-field swarm, which IS emitter-tractable and reads as a murmuration:
- [x] `apps/gpushow/kernels/SwarmKernel.codex`: one thread per particle,
      double-buffered (read inb, write outb). Velocity eases toward a curl-y
      procedural flow field (sine-lobe angle field, fixed-point trig); position
      integrates and wraps toroidally.
- [x] Grew the WgslEmitter for what this needs: nullary defs -> WGSL `const`;
      **topological ordering** (WGSL has no forward decls); reachability over
      calls AND value-refs, intersected with real def names; **multiple
      device-stores per act block** (4 state words). Fixpoint reachability fixed
      (iterate to depth, not early-stop).
- [x] Host `web/swarm.html`: ping-pong two storage buffers, 8000 particles,
      instanced quads colored by speed, additive blend.
**PROVEN:** `screenshots/swarm-m3.png` -- 8000 particles organized into
swirling vortices/ridges by the Codex-generated flow-field compute shader.
Validated 0-errors on NVIDIA; fireworks output byte-identical under the new
emitter (no regression).

### M3-next -- Iteration + true neighbour boids (emitter features)
- [x] **Recursion-to-WGSL-`loop` lowering** (2026-07-05). Tail self-recursion
      -> `loop` with params as `var`s, base cases `return`, self-call ->
      parallel-move (`_mv` temps) + `continue`. PROVEN by the Mandelbrot demo
      (mb_iter). `wgsl-is-self-recursive` + `wgsl-emit-loop-helper` in the plug.
- [ ] Storage-pointer helper parameters (`ptr<storage, array<i32>, read>`) so a
      neighbour-reduction helper can take the buffer (or per-kernel inlining).
      Probed: Chrome accepts storage-ptr params + loops. This is the remaining
      piece for boids.
- [ ] BoidsKernel.codex: separation/alignment/cohesion over neighbours;
      `var<workgroup>` tiling.
**Proof:** thousands of agents flocking at interactive frame rate.

## Emitter feature: Float (f32) support -- DONE (2026-07-05)
Codex Real -> WGSL f32. `wgsl-is-real` (mirrors ptx-is-real-type); IrNumLit
(f64 bit pattern) -> exact f32 via bit conversion + `bitcast<f32>(<u32>u)`;
type-directed emission with `wgsl-fexpr`/`wgsl-iexpr` coercion; typed params/
returns/consts; device-load/store coerce at the i32 buffer boundary; math
intrinsics (real-sqrt->sqrt, cordic-sin->sin, __int-to-real->f32, ...) via
float-coerced args; intrinsic-named stub defs are not emitted. Buffers stay
i32 -- only in-kernel math is float. GPU-kernel convention: define the math
intrinsics as local stubs (like apps/globe/kernels/EarthKernel.codex).

## Demo catalog (live in the gallery)
1. Fireworks (particles) -- M2
2. Swarm (flow field, double-buffered) -- M3
3. Plasma (per-pixel) -- integer
4. Mandelbrot (per-pixel escape-time) -- recursion-to-loop
5. Raymarch (3D SDF sphere + floor, diffuse light) -- float + march loop
6. Julia (animated escape-time, float) -- float + recursion-to-loop
Plus: gallery cards have a "</> Source" popup (VSCode Dark+ highlighting,
ports tools/vscode grammar). Next candidates: boids (storage-ptr helpers),
reaction-diffusion / fluid (float grid), render-stage WGSL emission (3D mesh
demos: instancing, deferred, PBR).

### M4 -- Showcase site (the gallery). DONE (2026-07-05)
- [x] `web/index.html` -- a polished dark gallery: title, the pipeline
      explainer (kernel.codex -> IR -> wgsl-plug -> kernel.wgsl -> WebGPU),
      and a card per demo (live-screenshot thumbnail, description, kernel source
      path, "Run live" link to the full page).
- [x] `README.md` (build/validate/run instructions). Skipped
      `codex.project.json` -- gpushow is not a compiled Codex app; its kernels
      are compiled individually through the WGSL plug.
**PROVEN:** `screenshots/gallery-m4.png` -- the served gallery listing both
demos, each linking to its live generated-WGSL page.

### Beyond -- breadth toward the reference gallery
Each additional Sascha-Willems technique = a new kernel (+ render shader) +
its host-page variant. Render-stage WGSL emission (`@vertex`/`@fragment`,
textures, multiple bindings) becomes a plug extension when the first
render-heavy demo (PBR/deferred/shadow) needs it. Track demos as a checklist
here as they land.

## Validation tooling (this box)
- Chrome + Edge installed (WebGPU); no naga/tint/cargo. Use
  `chrome --headless=new --enable-unsafe-webgpu`.
- node v24 (host driver / screenshot), wat2wasm 1.0.39, python 3.11.

## Open questions / risks
- WGSL storage-buffer binding layout: how many bindings, read vs read_write,
  and how the host binds them -- pin down in M1/M2.
- `@workgroup_size` + dispatch dims mapping from the `gid` heuristic.
- Render-stage emission is deferred; the first render-heavy demo forces it.
- Float path (sqrt/sin builtins) deferred; boids may want it -- decide in M3
  (integer fixed-point may suffice, matching fireworks).

## Status log
- 2026-07-05: M0 done (CL 7126). Plug design captured (SPIR-V hollow, PTX is
  the real semantics reference).
- 2026-07-05: M1 DONE. WGSL plug built + emits valid WGSL for both fireworks
  kernels, validated 0-errors on NVIDIA via Chrome WebGPU.
- 2026-07-05: M2 DONE. WebGPU host page runs the generated compute shader;
  screenshots/fireworks-m2.png shows the radial burst.
- 2026-07-05: M3 DONE. Flow-field swarm (SwarmKernel.codex) + emitter grown
  (const, topo order, multi-store, reachability fix); screenshots/swarm-m3.png.
- 2026-07-05: M4 DONE. Gallery site web/index.html + README;
  screenshots/gallery-m4.png. M0-M4 complete in one session.
- 2026-07-05: Showcase build-out (CL 7134+): Plasma demo (per-pixel);
  recursion-to-loop lowering shipped + Mandelbrot demo (mb_iter -> WGSL loop).
- 2026-07-05: FLOAT (f32) support shipped + Raymarch demo (SDF sphere-trace,
  float math + march loop, both from Codex). Gallery at 5 demos. Emitter now
  covers const/topo/multi-store/recursion-to-loop/float. Next: boids
  (storage-ptr), render-stage WGSL (3D mesh demos).
