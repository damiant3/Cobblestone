# Codex UI Designer

A WYSIWYG visual layout tool for building Codex UI widget trees. Runs in the browser with all layout computation, rendering, and code generation performed by a Codex-compiled WASM binary; only a thin JavaScript event/DOM layer is retained in the browser.

## Modules

- **DesignerWidgets** — Memory layout constants and accessors: widgets stored at 0x200000, 128-byte stride, max 256 widgets; 64 KB string pool; 11 widget types with full read/write field accessors
- **DesignerRender** — Four rendering views as HTML strings: canvas view (absolute-positioned drag targets with resize handles), live preview (flow layout), widget tree sidebar, properties panel
- **DesignerExport** — Codex source code generation: walks widget array and emits a `build-my-panel` function
- **DesignerApp** — Application core: widget store management, string pool, export buffer, query API. The `opening` function exercises the full API as a smoke test.
- **designer-page.template** — HTML shell with CSS/JS slots, three-column layout
- **designer-app.js** — JavaScript bridge (~160 lines): loads WASM, delegates all rendering to WASM exports, handles drag-and-drop and pointer events

## Completeness

80% — The core designer loop (place widget, drag, resize, reparent, edit, see live preview, copy generated Codex code) is fully implemented. The WASM pipeline is in place. Gaps: no save/load of designer state; no undo/redo; grid column editing not exposed in properties panel; CSS placeholder must be filled by a build step not present here.

## Codex Conformance

Full — All widget management, layout computation, HTML rendering, and code generation are written in Codex and compiled to WASM. The JS layer is explicitly a thin event bridge. This is the correct plug pattern for a browser-hosted tool.
