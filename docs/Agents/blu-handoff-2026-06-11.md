# Blu Handoff -- 2026-06-11

## Session Summary

Housekeeping session: submitted pending fishtank work, merged down from
main, copied up to main. Workspace is clean.

## What Was Done

### 1. Submit FishTank 3D Pipeline (CL 3756, Mountain)

56 files. Everything from the prior session's fishtank work:
- Vertex color shader (SHADER_FISH3D, stride 36->48 bytes)
- Mesh normalization (center + scale by span*3)
- SpineT axis fix (X not Z)
- 8 species regenerated from Forge reference photos via TripoSR GLB
- Mesh decimation pipeline (validate-and-decimate.py, ~1K verts each)
- generate-mesh.ps1 OBJ->GLB conversion
- build-spark.ps1 $ordered variable fix
- WASM plug CDX (229KB)
- Handoff doc

Excluded build-output/ directories (reverted before submit).

### 2. Merge Down from Main (CL 3757, Mountain)

66 files. Accepted theirs for all (no overlap with fishtank changes):
- Compiler: CodexEmitter, IRTextEmitter, X86_64, X86_64Compound,
  X86_64State, IRChapter, Lowering, opening
- ERP: ErpConfig (new), ErpScenario (new), ErpTypes, FinApAr,
  FinControlling, FinGL, FinTreasury, HrCore, IsBanking, IsInsurance,
  MmProcurement, PsProject, SdSales
- Build scripts: compile.ps1, compile-legacy.ps1, gdb-watchpoint.ps1,
  quire-map.ps1, run-with-disk.ps1, test-compile-batch.ps1,
  test-disk-boot.ps1, test-disk-compile.ps1, test-exception-handler.ps1,
  test-run.ps1, test-uefi-heap.ps1, vm-config.ps1
- Apps: Server.codex, Session.codex, Http.codex
- Tests: 20 new ERP test files, web-server-test updates
- Docs: ErpBuildout.md (new), CodegenAnalysis.md, BACKLOG.md,
  KNOWN-CONDITIONS.md
- Seed, codex-vm.c, codex-vm.exe, plug-build-lib.ps1

### 3. Copy Up to Main (CL 3758, main)

76 files. All Mountain content (fishtank + prior work) now on main.
Includes the full creature-db, pipeline scripts, Codex source files,
web models (decimated + full-res backups), WASM plug fix, roadmap, and
all fishtank infrastructure that was previously only on Mountain.

## Perforce State

Both workspaces clean. No files opened on either client.
- BigWhite_Codex_blu (Mountain) -- clean
- BigWhite_Codex_blu_main (main) -- clean

Mountain is fully synced with main in both directions.

## Known Issues

### Spark WASM Build Blocked

The full Spark build (build-spark.ps1) compiles IR fine (2.4MB from
562KB source) but wasm-plug.cdx hangs 600+ seconds on the large IR
with no output. The plug works for small inputs (demo.wasm 2.4KB,
viewport.wasm 4.2KB). Likely an emitter scaling issue -- check
WasmEmitter.codex / WasmPlug.codex for large-input behavior.
Stderr from a manual run may be in
codex/plugs/wasm/build-output/spark-plug-err.txt.

## What's Next

1. **Spark plug debugging** -- the main blocker for getting fishtank
   running as a native WASM app instead of just the JS viewer.

2. **Fish visual polish** -- TripoSR single-view artifacts remain
   (front/back symmetry). Multi-view reconstruction or mesh editing
   in Spark (once it builds) would improve quality.

3. **Swim animation verification** -- spineT maps X 0->1 from minX
   to maxX. Depending on reference image orientation, tail flex may
   be at the wrong end. Check visually and flip if needed.

4. **Fishtank roadmap** -- see apps/fishtank/docs/ROADMAP.md for the
   full plan.
