# Fester Session Handoff — 2026-06-08

## What Was Done

### Spark Studio Buildout (CLs 3376–3435)

1. **WASM TCO infrastructure** (CL 3376) — Added tail-call optimization to
   WasmEmitter: `is-self-tail-call` detection + `emit-wat-expr-tco` emission.
   246 functions detected. **DISABLED** — causes infinite loop during init.
   Guard set to `False` in emit-wat-def. Code is architecturally correct but
   one function hangs. Next step: binary search which function.

2. **KvStore data layer** (CL 3387) — kv-delete, text-key helpers (kv-put-text,
   kv-get-text, kv-delete-text, kv-has-text), int-pair keys (kv-put-i32,
   kv-get-i32). 11 exports. JS bridge (kvPut/kvGet/kvDelete/kvCount).

3. **UV editor** (CL 3388) — State at 0x5D0000 (zoom/pan/toggle), wireframe
   emitter, gen-uv-panel. 10 exports.

4. **JS-to-Codex migration** (CL 3389) — Material library + light list panels
   moved from JS to Codex. color-to-hex helper. JS reduced 1070 to 1047 lines.

5. **Image editor redesign** (CL 3395) — Replaced 54-button flat list with
   tabbed layout (Adjust/Filter/FX/Generate/File). Tool strip, compact grids.
   New CSS classes (img-tabs, img-grid, img-tools).

6. **Export buffer fix** (CL 3419) — All gen-* functions now write text at
   offset 4 past the length field. Fixed first-4-char corruption in all panels.

7. **CAD workbench** (CLs 3430, 3435) — Part/Sketch/Measure tabbed panel.
   Precision numeric inputs with unit scaling (mm/cm/in). Ortho view buttons.
   Configurable grid. Object dimensions + volume + mass. PLY export, DXF export,
   STL binary import. Dimension annotations. Section view. Sketch entities
   (line/circle/rect). 23 CAD exports.

### Codex Designer (CLs 3441–3458)

Standalone WYSIWYG UI builder app. 12.5KB WASM, 27 exports.

- Widget tree in flat memory (0x200000, 128-byte stride, max 256)
- 11 widget types: Section, Button, Toggle, PropRow, TextInput, Slider,
  ColorPick, Grid, TabGroup, Panel, Spacer
- Container nesting (Grid/TabGroup/Panel contain children)
- **All rendering in Codex** (DesignerRender.codex) — JS is thin event layer
- Code generation (DesignerExport.codex) — outputs build-panel Codex functions
- Build: `pwsh codex/plugs/wasm/build-designer.ps1`
- Serve: same directory as Spark, `designer.html`

### Merge Operations

- Multiple merge-downs from main (CLs 3420, 3461, 3464, 3470)
- Copy-up to main: CL 3421 (Spark phase 3), CL 3471 (CAD + Designer)

## Current State

| App | WASM | Exports | Build Script |
|-----|------|---------|-------------|
| Spark Studio | 151KB | 400+ | build-spark.ps1 |
| Codex Designer | 12.5KB | 27 | build-designer.ps1 |
| WASM Plug | 241KB | — | build.ps1 |

All apps build clean. Tests: all 6 Spark tests pass. Non-Spark failures
(boot-init, datetime-test, channel-test, etc.) are pre-existing.

## Open Issues

1. **TCO infinite loop** — WasmEmitter.codex line ~824, guard is `False`.
   The implementation wraps self-tail-recursive function bodies in WASM
   `loop`+`br` instructions. Works for isolated tests but hangs during
   `wasm_init` → `init_scene`. Binary search needed to find the specific
   function. The TCO code (Section: Tail Call Optimization) is ~80 lines
   and architecturally sound — the bug is likely a single function with
   an edge case.

2. **Canvas init stack overflow** — Without TCO, canvas-clear-loop at
   512x512 overflows. Caught by try-catch. 3D works, canvas doesn't init.

3. **Designer polish** — Widget rendering works but could use: undo/redo,
   copy/paste, grid snapping, more property editors, import existing
   Codex panel code (DesignerImport.codex — not yet written).

## How to Build

```powershell
pwsh codex/plugs/wasm/build.ps1              # WASM plug (if WasmEmitter changed)
pwsh codex/plugs/wasm/build-spark.ps1        # Spark (28s)
pwsh codex/plugs/wasm/build-designer.ps1     # Designer (5s)
python -m http.server 8090 --directory codex/plugs/wasm/build-output
# Spark: http://localhost:8090/spark-webgpu.html
# Designer: http://localhost:8090/designer.html
```

## Key Patterns

- **Adding WASM exports:** (1) Codex source function, (2) name in
  WasmEmitter.codex `wasm-export-list`, (3) reference in opening chain
  (the long let-binding chain in spark-webgpu.codex that keeps functions
  reachable for the compiler).
- **Panel HTML generation:** gen-* functions call build-* which write
  HTML to project-buf-addr starting at offset 4. Length stored at offset 0.
  JS reads via `wasmHtml()` → `readExportText()`.
- **Designer rendering:** All in Codex. JS calls `render_canvas`/`render_preview`/
  `render_tree`/`render_props`, reads buffer, sets innerHTML.
