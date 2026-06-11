# Blu Handoff — 2026-06-10

## Session Summary

Five fishtank pipeline improvements plus a Spark build attempt.

## What Was Done

### 1. Vertex Colors in Shader (fishtank.js)

SHADER_FISH3D updated: added `@location(6) vcol:vec3f` vertex input,
passes color through VOut to fragment shader. Fragment now uses
`col * (0.5 + facing * 0.5)` instead of hardcoded `vec3f(1.0, 0.5, 0.15)`.

Vertex layout expanded: stride 36 → 48 bytes (12 floats). New attribute
at shaderLocation 6, offset 36, format float32x3.

### 2. Mesh Scale Normalization (fishtank.js, uploadGLBMesh)

GLB meshes now centered at origin and divided by `span * 3` (where span
is the longest bounding box axis). This matches the visual presence of
the procedural fish meshes. Without it, TripoSR meshes rendered ~10x
too big.

### 3. SpineT Axis Fix (fishtank.js, uploadGLBMesh)

SpineT and UV now map along the X axis (the fish spine) instead of Z.
The swim animation mask was applying to the wrong axis.

### 4. Mesh Regeneration — All 8 Species

Regenerated all 8 fish species from proper Forge reference photos
(not old 2D sprites). Pipeline: Forge txt2img → TripoSR GLB.

All 8 completed:
- fish-clownfish, fish-angelfish, fish-neontetra, fish-tang
- fish-discus, fish-guppy, creature-seahorse, creature-shrimp

Reference images in `apps/fishtank/creature-db/<species>/reference/`.
Full-res GLB backups in `web/models/*-full.glb`.

Creature DB records created for all 7 new species (Clownfish already
existed). Index.json was p4 edited.

### 5. Mesh Decimation — validate-and-decimate.py

New pipeline script: `apps/fishtank/pipeline/validate-and-decimate.py`

Sanity checks: vertex count, face count, bounding box (no collapsed
axes), aspect ratio (>1.3 for fish), vertex color presence.

Decimation: fast_simplification quadric edge collapse, vertex colors
preserved via scipy cKDTree nearest-vertex mapping.

Results: all 8 species decimated from 23K-63K verts to ~1000 verts
(~2000 faces). GLBs went from 1-2.5MB to ~41KB each.

Dependency installed: `fast_simplification` in D:\AI\TripoSR\venv.

### 6. generate-mesh.ps1 — OBJ → GLB

Changed `--model-save-format obj` to `glb` and updated all variable
names/file patterns from `.obj` to `.glb`.

### 7. build-spark.ps1 — $ordered fix

Fixed line 29: removed reference to `$ordered.Count` (variable internal
to Resolve-PlugForewords, not accessible from the caller).

### 8. WASM Plug Build — Succeeded

`codex/plugs/wasm/build.ps1` ran successfully. Produced:
`codex/plugs/wasm/build-output/wasm-plug.cdx` (229,688 bytes).

### 9. Spark Build — IN PROGRESS / FAILING

The full Spark build (build-spark.ps1) gets through IR compile (2.4MB
IR-CCE from 562KB bundled source) but the plug phase (IR → WAT via
wasm-plug.cdx in codex-vm) runs for 600+ seconds and produces no output.

A manual retry is currently running in background with `-mem 8192`
(doubled from 4096) and stderr captured to
`codex/plugs/wasm/build-output/spark-plug-err.txt`.

**Background task ID: biv560o3r** — check its output file for results.

The plug works for small inputs (demo.wasm at 2.4KB, viewport.wasm at
4.2KB exist from prior builds). The 2.4MB Spark IR may be hitting a
memory or time limit in the emitter.

## Perforce State

Files opened for edit on Mountain stream:
- `apps/fishtank/web/fishtank.js` — shader, upload, layout changes
- `apps/fishtank/creature-db/index.json`
- `apps/fishtank/pipeline/generate-mesh.ps1` — OBJ→GLB
- `codex/plugs/wasm/build-spark.ps1` — $ordered fix

New files (not yet added):
- `apps/fishtank/pipeline/validate-and-decimate.py`
- `apps/fishtank/creature-db/*.json` (7 new species records)
- `apps/fishtank/creature-db/*/reference/` and `mesh/` directories
- `apps/fishtank/web/models/*-full.glb` (full-res backups)

## Services Running

- HTTP server on port 8090 (python, PID 14000) — serves fishtank
- Stable Diffusion Forge on port 7860 (python, PID 10928)
- codex-vm (manual plug run) — may still be running

## What's Next

1. **Spark plug debugging** — check stderr output from the manual run.
   If OOM, the WASM emitter may need chunked processing or the IR may
   need splitting. If a bug, check the WasmEmitter.codex / WasmPlug.codex
   for issues with large inputs.

2. **Fish visual polish** — the fish are properly colored and scaled but
   still have TripoSR single-view artifacts (front/back symmetry). Could
   improve with multi-view reconstruction or manual mesh editing in Spark
   once it builds.

3. **Swim animation direction** — spineT maps X 0→1 from minX to maxX.
   Depending on the Forge reference image orientation, the tail flex may
   be at the wrong end. Check visually and flip if needed.

4. **Perforce submit** — the fishtank changes are ready to submit once
   verified. Bundle as one CL: shader + upload + layout + pipeline.
