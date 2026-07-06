# gpushow -- Design

A WebGPU technique showcase whose GPU code is **generated from Codex
`[Device]` AST** through a new WGSL plug -- the browser-graphics analogue of
what the PTX and SPIR-V plugs already do for GPU compute. It ports the spirit
of the Sascha Willems WebGPU sample gallery
(https://pooyaeimandar.github.io/webgpu/): one page per rendering technique,
but each demo's shader comes from `.codex`, not hand-authored WGSL.

Naming note: working name `gpushow`; rename is free until code references it.

## Why this exists

fishtank/starmap/spark all reach WebGPU by storing WGSL as Codex `Text`
constants or hand-written `.js` -- none of it is generated from the AST. The
project thesis (dual-target GPU: "adding a GPU target is adding a plug, not a
compiler change") says the honest path is a WGSL emitter that walks the same
IR the PTX/SPIR-V plugs walk. gpushow is the app that exercises and shows off
that plug.

## Architecture

```
apps/gpushow/kernels/*.codex          [Device]-effected compute in Codex
        |  build/compile.ps1 -Src K.codex -Out K.ir -IrCce
        v
      IR-CCE  --> ptx-plug.cdx    --> K.ptx      (exists)
              --> spirv-plug.cdx  --> K.spvasm   (exists, but hollow -- see below)
              --> wgsl-plug.cdx   --> K.wgsl     <== codex/plugs/wgsl/ (WE BUILD)
        v
  WebGPU host page (apps/gpushow/web/)
     compute pass: dispatch K.wgsl -> storage buffer of agent state
     render pass:  draw agents (instanced points/billboards)
        v
  Chrome/Edge WebGPU  (validator + runtime + screenshot proof)
```

## The plug (codex/plugs/wgsl/)

Structure follows every transpiler plug: `WgslPlug.codex` (entry `opening`
-> `read-line-cce` mode -> `read-file` -> `parse-ir-chapter` ->
`emit-wgsl-chapter` -> `print-line-uni`) + `WgslEmitter.codex` (codegen) +
`build.ps1` (`Build-TranspilerPlug -PlugName wgsl -Chapters WgslEmitter,WgslPlug`)
+ `run.ps1` (compile `-IrCce`, feed `IR-CCE\n` + CCE IR + null to
`codex-vm -kernel wgsl-plug.cdx`, strip `HEAP|WD|STACK|PM:` log lines +
leading control bytes).

### Critical finding (2026-07-05 code read)

- The **SPIR-V plug is a hollow syntactic walker**: it declares
  `OpTypePointer StorageBuffer` but emits no StorageBuffer access, no `gid`
  entry point, and no math intrinsics. `device-load`/`device-store`/`gid`/
  `real-sqrt` all fall through the generic call path to bogus
  `OpFunctionCall`s. So `.spvasm` output is NOT a working shader.
- The **real GPU lowering lives in the PTX plug** (`codex/plugs/ptx/PtxEmitter.codex`).
  WGSL must port that design, not SPIR-V's.

### What to reuse vs rewrite vs port

- **Reuse from SPIR-V skeleton** (rename `spv-`->`wgsl-`, swap the
  `SpvResult`/`SpvCtx` records): the `emit-expr-at` dispatch skeleton + depth
  guard, `collect-apply-chain`, act/args loops, name sanitize, per-def emit
  loop, chapter assembly shape.
- **Rewrite for WGSL syntax** (WGSL is high-level -- drop SSA/phi entirely):
  `if`/`else` (replace OpSelectionMerge/OpBranchConditional/OpPhi with
  structured `if (c) {..} else {..}`), `match` (real `if`-chain/`switch`),
  literals (inline `123`/`1.0`, not OpConstant temps), comparisons (native
  `<`/`==`, not OpSelect booleanize), calls (`name(args)`), function
  signatures (`fn name(p: i32) -> i32 { return .. }`), header (`@group/@binding
  var<storage, read_write>` + `@compute @workgroup_size`), delete the
  OpTypeFunction table.
- **Port from PTX** (the GPU semantics SPIR-V lacks): the intrinsic-dispatch
  branch in the apply handler; kernel detection (trailing param named `gid`,
  `>=2` params) -> `@compute` entry deriving `gid` from
  `global_invocation_id.x` with a `pixel_count` bound; `device-load/store` ->
  `buf[i]` on a storage binding; math intrinsics (`real-sqrt`->`sqrt`,
  `cordic-sin`->`sin`, `real-floor`->`floor`, `__int-to-real`->`f32`, ...);
  special regs (`thread-idx-x`->`local_invocation_id.x`,
  `block-idx-*`->`workgroup_id`, `grid-dim-*`->`num_workgroups`).

Caveat: `IRDef` exposes no effect annotation at emit time, so "is this a
kernel" is inferred structurally from the trailing `gid` param (same heuristic
PTX uses).

## Host harness

Template = **starmap pattern**: Codex owns scene/agent state (in-WASM or as a
data module); a thin JS/WebGPU harness in `apps/gpushow/web/` creates the
`GPUDevice`, builds a compute pipeline from the generated `.wgsl`, dispatches
it to update a storage buffer, and a render pass draws the result. v1 host JS
is a small static harness (honest: hand-written); a later pass can emit it via
the html plug (CurrentPlan item p wants the ~1100 lines of harness JS moved
into Codex).

## Validation

No standalone WGSL validator on this box (naga/tint/wgslc absent, no cargo).
Chrome + Edge are installed -> **Chrome headless WebGPU is the validator and
the runtime**: a node (v24) driver launches `chrome --headless=new
--enable-unsafe-webgpu`, loads a page that calls
`device.createShaderModule({code})` + `shaderModule.getCompilationInfo()`
(real WGSL diagnostics), runs the passes, and screenshots the canvas. This
doubles as the showcase runtime. wat2wasm 1.0.39 + python 3.11 available.

## Honesty flags

1. Compute-only demos first. The **compute** is genuinely from-AST; the tiny
   **render** shader that draws points is hand-WGSL at first. Render-stage
   emission (`@vertex`/`@fragment` I/O, textures, bindings) is a later plug
   extension -- required for the reference site's PBR/deferred/shadow demos.
2. Integer fixed-point first (simplest WGSL, matches FireworksKernel). Float
   `sqrt`/`sin` builtins come when a demo needs them (the PTX math-intrinsic
   table is the map).

## Reference material in-tree

- Fork base: `codex/plugs/spirv/{SpirvPlug,SpirvEmitter}.codex`, `build.ps1`, `run.ps1`.
- GPU-semantics reference: `codex/plugs/ptx/PtxEmitter.codex` (intrinsic
  dispatch ~273-566; device-load/store + math ~634-698; gid entry wrappers +
  chapter assembly ~1043-1117).
- IR vocabulary: `codex/plugs/common/PlugTypes.codex:226-368`
  (`IRExpr`/`IRBinaryOp`/`IRPat`/`IRActStmt`/`IRDef`, `collect-apply-chain`).
- IR parse: `codex/plugs/common/IRTextParser.codex` (`parse-ir-chapter`, `ParsedIR`).
- Build lib: `codex/plugs/common/plug-build-lib.ps1:126-156` (`Build-TranspilerPlug`).
- Kernel seed: `apps/fireworks/FireworksKernel.codex` (`fw-burst-spark`,
  `fw-integrate` -- compute-only `[Device]`, integer fixed-point trig).
- Host templates: `apps/starmap/StarMapWasm.codex`, `apps/fishtank/build-wasm.ps1`.
- Existing GPU marketing page (Site-theme showcase pattern for M4):
  `apps/gpu/GpuPage.codex`.

## `[Device]` effect surface

`codex/foreword/gpu/DeviceEffect.codex` -- `effect Device`: `thread-idx-{x,y,z}`,
`block-idx/dim`, `grid-dim`, `sync-threads`, `device-load/store` (+f64). A
function with `Device` in its effect row is a kernel; kernels take a trailing
`gid`. `codex/foreword/gpu/GpuEffect.codex` is the host side (not used in the
browser path).
