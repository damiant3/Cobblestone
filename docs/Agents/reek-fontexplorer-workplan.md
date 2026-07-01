# Font Explorer Workplan — reek agent

**Updated:** 2026-06-24 (session 5)
**Stream:** //Codex/MutableRecords
**Latest CLs:** 6038-6055

## Session 5 Summary

Major session: WinForms emitter AST rewrite, 108 runtime builtins,
native C# Font Explorer app, GPU training pipeline end-to-end.

### Completed

- **WinForms emitter rewrite** (CL 6038): WidgetCase records, named
  builtins, clean assembly. No more monolithic string concatenation.
- **108 WinForms builtins** (CL 6038): text/list/char/bitwise/math/
  DOM/state/dialog/widget/theme/render/JSON/utility + CUDA driver API.
- **Font Explorer native app** (CL 6040): dark theme, model management,
  multi-size TrueType preview, Training tab with live log, style
  checkboxes, sliders with numeric feedback.
- **PTX emitter fixes** (CLs 6049-6052): three bugs fixed to produce
  valid PTX accepted by NVIDIA JIT:
  1. Param loads via numbered registers then mov to named locals
  2. Call convention with .param variables for arg passing
  3. Forward declarations for .func before .entry kernels
  4. %ru registers for u32 thread-idx reads
  5. Unique labels per function (i*1000 offset)
  6. %lv_ prefix for locals (no conflict with %rd range)
- **MLP kernels in Codex** (CL 6049): 22 GPU kernels (10 .entry +
  12 .func) written in Codex, compiled to PTX via the plug pipeline.
  PTX loads successfully on RTX 4060 Ti.
- **CUDA builtins in WinForms plug** (CL 6049): gpu_init, gpu_load_ptx,
  gpu_alloc/free, gpu_copy_to/from, gpu_launch, gpu_sync via P/Invoke
  to nvcuda.dll.
- **FontAiTrainer.codex** (CL 6053): MLP training orchestration written
  in Codex with [Gpu] effect, compiles through WinForms plug to 41KB C#.
- **GPU training live** (CL 6055): Training tab runs MLP forward/backward/
  Adam entirely on RTX 4060 Ti. 20x faster than 8-thread CPU baseline.
- **Copy-up to main** (CL 6044): all WinForms + FontExplorer work.

### Known Deficiencies

#### 1. GPU kernels use Integer (s64) not Real (f64) — BLOCKING

The MLP kernels in `MlpKernels.codex` declare all values as `Integer`,
so the PTX plug emits `mul.lo.s64`, `add.s64`, `div.s64` (integer
arithmetic). Neural net training requires floating-point: the Adam
optimizer computes `m * 0.9 + g * 0.1` which in integer division
truncates small gradients to 0. Loss reads as 0.00 because the
integer math kills the learning signal.

**Fix:** Change kernel types from `Integer` to `Real` so the PTX
plug emits `mul.f64`, `add.f64`, `div.rn.f64`. The `device-load`
and `device-store` intrinsics need `.f64` variants (already added
as `device-load-f64` / `device-store-f64` in DeviceEffect). The
host-side `gpu_copy_to` / `gpu_copy_from` must send `double[]` not
`long[]`. The weight initialization and training data must use
`BitConverter.DoubleToInt64Bits` / `Int64BitsToDouble` for the
host-device transfer (CUDA sees raw 64-bit values).

**Files to change:**
- `apps/fontai/kernels/MlpKernels.codex` — Integer -> Real
- `apps/fontexplorer/FontExplorerApp.cs` — double[] for GPU buffers
- Possibly `codex/plugs/ptx/PtxEmitter.codex` — verify .f64 codegen

#### 2. Weights saved to build-output (gets blown away on rebuild)

GPU training saves weights to `build-output/app/trained/` which is
inside the build output directory. A rebuild wipes this. Should save
to a user-chosen location or to the fontai directory.

#### 3. TTF parsing still in PowerShell

The Font Explorer's Generate button calls `generate.ps1` for font
generation. The training data extraction (TTF parsing, contour
resampling) is also in PowerShell (`train.ps1`). Both should be
ported to C# in the app for a fully self-contained experience.

#### 4. Training uses synthetic data

The GPU training loop currently trains on 95 random target vectors
(one per ASCII character) to prove the pipeline works. Real training
needs actual TTF contour data fed through `BuildGlyphInput`.

### Next Priorities

1. Fix f64 kernel arithmetic (unblocks meaningful GPU training)
2. Save weights to user-chosen location
3. Port TTF parsing to C# in the app
4. Wire real training data into GPU training loop
5. Font preview with generated TTF after training

## Build Commands

```powershell
# Build WinForms plug CDX
pwsh codex/plugs/winforms/build.ps1

# Build Font Explorer (native C# mode, default)
pwsh apps/fontexplorer/build.ps1

# Build Font Explorer (Codex transpilation mode)
pwsh apps/fontexplorer/build.ps1 -Transpile

# Compile MLP kernels to PTX
pwsh codex/plugs/ptx/run.ps1 -Src apps/fontai/kernels/MlpKernels.codex -Out apps/fontai/kernels/mlp.ptx

# Train font AI (PowerShell, standalone)
pwsh apps/fontai/train.ps1 -Epochs 2000

# Generate fonts (PowerShell)
pwsh apps/fontai/generate.ps1 -Upem 1024
```

## Key Files

| File | Purpose |
|------|---------|
| apps/fontexplorer/FontExplorerApp.cs | Native C# app (preview + training + GPU) |
| apps/fontexplorer/FontAiTrainer.codex | MLP training in Codex (transpiles to C#) |
| apps/fontexplorer/build.ps1 | Build script (native or transpile mode) |
| apps/fontai/kernels/MlpKernels.codex | GPU kernels in Codex |
| apps/fontai/kernels/mlp.ptx | Compiled PTX for RTX 4060 Ti |
| apps/fontai/train.ps1 | CPU training pipeline (PowerShell) |
| apps/fontai/generate.ps1 | TTF generation from weights |
| codex/plugs/winforms/WinFormsEmitter.codex | C# emission (AST model + 108 builtins + CUDA) |
| codex/plugs/ptx/PtxEmitter.codex | PTX emission (sm_89, .func/.entry, intrinsics) |
| codex/foreword/gpu/DeviceEffect.codex | GPU device intrinsics |
| codex/foreword/gpu/GpuEffect.codex | GPU host operations |
