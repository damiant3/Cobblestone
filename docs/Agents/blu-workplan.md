# Blu Agent Workplan — 2026-06-26

## Current Stream: GPU Globe App

### Completed This Session
- **Cornell CS 6120 Review** — `docs/Reference/CornellReview.md`, pushed to main (CL 6069/6070)
- **Globe WinForms app** — CPU raycast renderer with NASA January texture, ice caps, moon, atmosphere, multi-scene (earth/blackhole/sosaria)
- **PTX plug f64 type system** — 8 bugs fixed across CLs 6152, 6156, 6163: hex literals, typed locals/params/returns/if-else/comparisons, f32 sincos, int-to-real/real-to-int intrinsics, FunTy return extraction
- **GPU earth kernel** — `EarthKernel.codex` compiles to valid PTX, loads on RTX 4060 Ti, renders textured sphere
- **CORDIC atan2** — 16-iteration vectoring mode in Codex, compiles to GPU PTX

### Shelved Work
**CL 6166** — Globe app files (GlobeApp.cs, EarthKernel.codex, GlobeKernels.codex, run.ps1)

### Next Steps (Priority Order)

1. **Fix PTX function call ABI** — Local variables are corrupted after PTX `.func` calls return. The `%lv_` registers lose their values across call boundaries. This is the ONLY bug blocking textured earth with proper UV mapping. The inline texture path works; the function-call path doesn't. Root cause is in the PTX plug's call emission — likely register space overlap between caller and callee.

2. **Fix dead code elimination** — The Codex compiler eliminates `kernel-` prefixed functions in IR mode because they have no callers. Current workaround: name functions without `kernel-` prefix and post-process `.func` → `.entry` in C#. Real fix: compiler should preserve all chapter-level defs in IR mode.

3. **Fix stub emission** — Math stub functions (cordic_sin etc.) compile to invalid PTX because their stub bodies use s64 registers for f64 operations. Current workaround: strip stubs in PowerShell post-processing. Real fix: PTX plug should not emit function bodies for names it recognizes as intrinsics.

4. **Black hole scene** — The ray march kernel is written in `GlobeKernels.codex` but needs the function call fix before it can run (200-step recursive march).

5. **Sosaria texture** — Need to source a UO Sosaria equirectangular map image and convert to raw format.

### PTX Plug Status
The plug compiles valid PTX for f64 Real types including:
- `sin.approx.f32` / `cos.approx.f32` (via f64→f32 conversion)
- `sqrt.rn.f64`, `abs.f64`, `min.f64`, `max.f64`
- `cvt.rn.f64.s64` (int-to-real), `cvt.rzi.s64.f64` (real-to-int)
- Typed function params (`.param .f64` vs `.param .s64`)
- Typed local registers (`%lv_f__xxx` as `.reg .f64`)
- Typed if-else merge registers
- Typed comparison instructions (`setp.gt.f64`)

The MLP kernels (fontexplorer) still work — they only use integers.
