# Codex StarMap

An interactive 3D star map rendering 80+ named celestial objects (stars, Messier catalog, notable galaxies) in galactic XYZ space, with a free-fly camera, constellation stick figures, spatial database, and a WASM/WebGPU export interface for browser rendering.

## Modules

- **CelestialTypes** -- 14 CelestialKinds, 12 SpectralClasses, B-V color index to RGB conversion, CelestialObj with full astronomical data, screen-size-from-magnitude, distance formatting
- **StarCatalog** -- 50 named bright stars with real galactic coordinates, 22 Messier objects, 8 notable galaxies; text search
- **Constellations** -- 13 IAU constellations with stick-figure line segments referencing catalog star IDs
- **StarDb** -- OctreeNode type, StarDatabase with spatial index; insert, box-range query, nearest-neighbor search, magnitude/kind filters
- **StarMapScene** -- StarCamera with yaw/pitch/orbit/zoom/goto; VisibleObj projection; StarScene state with catalog, selection, hover, label/grid/constellation toggles, search
- **StarMapWasm** -- WASM shared-memory layout, export functions, write-cam-state, tick() writing star vertex and label data to shared buffers
- **TestStarMap** -- 7 test sections covering types, B-V colors, catalog, scene, camera, database, constellations

## Completeness

65% -- Data layer (catalog, types, scene state, DB queries) and WASM memory-write path are implemented. Missing: 3D-to-2D projection delegated to JS/WebGPU harness; camera control WASM exports return 0 (stubs); octree spatial partitioning typed but not built; constellation lines sparse; catalog-all only includes stars (messier/galaxies not concatenated).

## Codex Conformance

Full -- All logic is Codex. Rendering correctly split: Codex owns scene state and writes vertex data to WASM shared memory; a WebGPU JS harness handles GPU projection and rasterization (correct plug model for browser-side 3D).
