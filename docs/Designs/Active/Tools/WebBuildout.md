# Web Buildout — val

**Status:** Design. Agent: val. Date: 2026-05-27.

This document covers the gap analysis and buildout plan for the
Codex web stack: the HTML plug emitter, the explorer pages, the
foreword UI-to-web pipeline, and the `tools/web/` server layer.

## Goal

Move from hardcoded inline CSS/JS/HTML to a fully data-driven
architecture where **the theme belongs to the user, not to us**.
Users define their own layouts, color preferences, and widget
arrangements via the DB. We provide the layout widgets (from
`codex.foreword.ui`) and users can arrange them. The web output
must have AJAX, eventing, and reactive binding built in as
first-class capabilities — not bolted on after the fact.

---

## 1. Current State

### Three Generations of Web Output

**Gen 1 — Legacy inline-JS** (`apps/explorer/ItemDesigner.codex`,
`CharacterDesigner.codex`, `SettingDesigner.codex`). These files
emit full HTML pages by printing `<!DOCTYPE html>` through serial
output. CSS and JS are embedded as Codex text literals. Each page
duplicates its own `explorer-css` string, nav HTML, pill renderer,
lightbox, generate function, and per-type JSON serializers. Working
but high duplication, fragile escaping, and all logic is
string-templated JS rather than compiled Codex.

**Gen 2 — Plug pipeline** (`apps/explorer/ItemDesignerApp.codex`,
`CharDesignerApp.codex`, `SettingDesignerApp.codex`). These cite
shared chapters (`ExplorerTheme`, `ExplorerData`) and use DOM stub
functions (`dom-get`, `dom-set-html`, `inject-theme-css`, etc.).
The HTML plug compiles them to JS functions; theme CSS is generated
at runtime from the `dark-gold` Theme record via `theme_to_css`.
Shared nav, dropdown builder, history panel, hero image, and SD
status check live in ExplorerTheme. Data lives in ExplorerData.
This is the active path.

**Gen 3 — Semantic widget emission** (designed, not built). The
WebEmitter.md design describes recognizing `WidgetNode` construction
sites in IR and emitting semantic HTML (`<button>`, `<progress>`,
etc.) with CSS from `Theme`/`BoxModel`/`Layout`. No code exists.

### File Inventory

| Layer | Files | Status |
|-------|-------|--------|
| HTML plug CDX | `codex/plugs/html/HtmlEmitter.codex` (477 lines) | Working, Gen 2 |
| HTML plug entry | `codex/plugs/html/HtmlPlug.codex` (24 lines) | Working |
| Shared theme | `apps/explorer/ExplorerTheme.codex` (308 lines) | Working, Gen 2 |
| Shared data | `apps/explorer/ExplorerData.codex` (430 lines) | Working, Gen 2 |
| Shared DB | `apps/explorer/ExplorerDb.codex` | Working, Gen 2 |
| Server (CDX) | `apps/explorer/ExplorerServer.codex` | Working, Gen 2 |
| Server (bridge) | `apps/explorer/server.ps1` | Working |
| Item page | `apps/explorer/ItemDesignerApp.codex` (210 lines) | Working, Gen 2 |
| Character page | `apps/explorer/CharDesignerApp.codex` (158 lines) | Working, Gen 2 |
| Setting page | `apps/explorer/SettingDesignerApp.codex` (131 lines) | Working, Gen 2 |
| Card page | `apps/explorer/CardDesignerPage.codex` | Working, Gen 2 |
| Dashboard server | `tools/web/server.ps1` (800+ lines) | Working |
| Dashboard CSS | `tools/web/style.css` (89 lines) | Working |
| Dashboard JS | `tools/web/app.js` (101 lines) | Working |
| Game HTML files | `tools/web/*.html` (34 files) | Working |
| Legacy card glue | `tools/web/explorer/card-ui.js` (129 lines) | Legacy Gen 1 hybrid |
| Legacy item glue | `tools/web/explorer/item-ui.js` (89 lines) | Legacy Gen 1 hybrid |
| WebEmitter design | `docs/Designs/Active/Tools/WebEmitter.md` | Design only |
| UICapabilityMap | `docs/Designs/Active/Tools/UICapabilityMap.md` | Design only |

---

## 2. Gap Analysis

### 2.1 HtmlEmitter Issues

| # | Issue | Severity | Fix |
|---|-------|----------|-----|
| E1 | `emit-dom-builtins` is a 70-line string blob | Medium | Extract into named helpers, one per builtin |
| E2 | `is-html-builtin` is a 19-branch if/else chain | Low | Sorted list + binary search |
| E3 | `emit-js-let` wraps every let in an IIFE | Medium | Emit sequential `const` when body is another let |
| E4 | `mount-widget` declared in `is-html-builtin` but undefined in builtins | Bug | Add implementation or remove from list |
| E5 | `IrHandle` and `IrWithTimeout` silently emit body only | Medium | Emit a console.warn at minimum; implement handler dispatch for Phase 2 |
| E6 | No source mapping from generated JS to Codex source | Low | Add `//# sourceURL` comments per function |

### 2.2 Explorer Gaps

| # | Gap | Description |
|---|-----|-------------|
| X1 | Voice Studio page | `VoiceStudio.codex` exists as placeholder, TTS backend not connected |
| X2 | Workflow Exporter | `WorkflowExporter.codex` exists, ComfyUI integration incomplete |
| X3 | Gen 1 / Gen 2 coexistence | Legacy `*Designer.codex` files duplicate data that now lives in `ExplorerData`. Should be deleted or marked frozen. |
| X4 | `tools/web/explorer/` orphaned | `card-ui.js` and `item-ui.js` reference `card-app.js` and `item-app.js` that aren't checked in. These are Gen 1 hybrid remnants. |

### 2.3 UI Foreword → Web Gaps

These modules exist in `codex.foreword.ui` but have no web
emission path:

| Module | Web Status | Work Required |
|--------|-----------|---------------|
| Widget → semantic DOM | Not built | Recognize `WidgetNode` construction in IR, emit `<div>`, `<button>`, `<span>`, `<input>`, `<progress>`, `<hr>` |
| Event → addEventListener | Not built | Map `HandlerTable` entries to `addEventListener` calls |
| Binding → reactive updates | Not built | Generate JS reactive loop from `Observable`/`BindingTable` |
| Dialog → `<dialog>` | Not built | Emit `dialog.showModal()` / `dialog.close()` from `DialogConfig` |
| Overlay → positioned divs | Not built | Tooltip, popup, context menu, modal overlay emission |
| Scroll → overflow CSS | Not built | Emit `overflow: auto` from `ScrollState` |
| Animation → CSS keyframes | Not built | Emit `@keyframes` from `Throbber`/`Transition`/`KeyframeSeq` |
| Accessibility → ARIA | Not built | Emit `role`, `aria-label`, `aria-live` from `Accessibility` records |
| Charts → SVG/Canvas | Not built | Emit `<svg>` chart elements from `Charts` data |
| Vector → SVG | Not built | Emit `<svg><path>` from vector path data |
| Window → routing/dialog | Not built | Multi-window → hash routing or `<dialog>` |

### 2.4 Dashboard Gaps

| # | Gap | Description |
|---|-----|-------------|
| D1 | `location.reload()` for status refresh | Kills client state; should use `fetch` + DOM update |
| D2 | Game catalog hardcoded in PS1 | Should be a JSON data file or generated from Codex source |
| D3 | No shared component library across 34 game files | Each game reimplements board, controls, status, log |

---

## 3. Buildout Plan

### Phase 1: Emitter Hardening (no new features)

Fix bugs and clean up the existing HtmlEmitter before extending it.

1. **E4: Implement `mount-widget`.** Add a JS function that takes
   a WidgetNode-shaped JS object and renders it into `#app` as DOM
   elements. This is the bridge between Gen 2 (DOM stubs) and
   Gen 3 (semantic emission).

   ```javascript
   function mount_widget(node) {
     var el = _wk(node);
     document.getElementById('app').innerHTML = '';
     document.getElementById('app').appendChild(el);
     return 0;
   }
   function _wk(n) {
     var k = n.wn_kind;
     var el;
     if (k._tag === 'WkPanel') el = document.createElement('div');
     else if (k._tag === 'WkLabel') { el = document.createElement('span'); el.textContent = k._0; }
     else if (k._tag === 'WkButton') { el = document.createElement('button'); el.textContent = k._0; }
     else if (k._tag === 'WkInput') { el = document.createElement('input'); el.value = k._0; }
     else if (k._tag === 'WkGauge') { el = document.createElement('progress'); el.value = k._0; el.max = k._1; }
     else if (k._tag === 'WkSeparator') el = document.createElement('hr');
     else { el = document.createElement('div'); el.dataset.custom = k._0; }
     el.id = n.wn_id;
     el.className = 'wk-' + (k._tag === 'WkPanel' ? 'panel' : k._tag.substring(2).toLowerCase());
     if (n.wn_layout_dir && n.wn_layout_dir._tag === 'DirRow') el.classList.add('wk-row');
     if (n.wn_layout_dir && n.wn_layout_dir._tag === 'DirColumn') el.classList.add('wk-col');
     if (n.wn_gap > 0) el.style.gap = n.wn_gap + 'px';
     for (var i = 0; i < (n.wn_children || []).length; i++) {
       el.appendChild(_wk(n.wn_children[i]));
     }
     return el;
   }
   ```

2. **E3: Flatten let chains.** When `emit-js-let` sees a body that
   is also `IrLet`, collect the full chain and emit a single IIFE
   with sequential `const` declarations. Reduces nesting from O(n)
   to O(1).

3. **E1: Extract DOM builtins.** Break `emit-dom-builtins` into
   named text constants, one per logical group (DOM manipulation,
   state management, theme CSS, image generation).

4. **E5: Warn on dropped handlers.** Emit
   `console.warn('effect handler not supported in web target')`
   for `IrHandle` and `IrWithTimeout` instead of silent passthrough.

### Phase 2: Explorer Cleanup

1. **X3: Freeze Gen 1 files.** Add `.skip` or a comment marking
   `ItemDesigner.codex`, `CharacterDesigner.codex`,
   `SettingDesigner.codex` as superseded by `*App.codex`. Don't
   delete — they document the evolution.

2. **X4: Remove orphaned JS glue.** Delete `tools/web/explorer/card-ui.js`
   and `item-ui.js` if `card-app.js` / `item-app.js` are not
   generated. Or: add a build step that generates them.

3. **Verify Gen 2 pages build.** Run the HTML plug pipeline for
   each `*App.codex` and confirm the output HTML works in a
   browser. This requires a working seed on the CodexMagic stream
   (currently blocked by constants mismatch — see build failure).

### Phase 3: Theme-Driven Widget Rendering

Bridge the gap between the UI foreword's `WidgetNode` type system
and the web target. This is the Gen 3 work from WebEmitter.md.

1. **`mount-widget` runtime** (Phase 1 deliverable above) handles
   the JS side. Programs that build a `WidgetNode` tree and call
   `mount-widget` will get DOM output.

2. **`inject-theme-css` enhancement.** The existing implementation
   emits CSS variables and widget-class styles. Extend it to also
   emit flex layout classes (`.wk-row`, `.wk-col`) with gap, and
   state pseudo-class styles for all widget types.

3. **Box model CSS.** When a program sets margins, padding, or
   borders on widgets via `WidgetStyle`, the `mount-widget`
   runtime should apply them as inline styles (or data attributes
   that CSS rules target).

### Phase 4: Interactive Web Features

These extend the emitter with JS codegen for interactive behavior.

| Feature | IR Pattern | JS Output |
|---------|-----------|-----------|
| Event handlers | `IrApply dom-on el "click" handler` | `el.addEventListener("click", handler)` |
| Binding updates | `Observable` construction + `BindingTable` | Reactive setter that calls `_wk` re-render on dirty |
| Dialog | `DialogConfig` construction | `<dialog>` element + `showModal()` call |
| Animation | `Throbber`/`Transition` | CSS `@keyframes` + `animation` property |
| Accessibility | `Accessibility` record fields | `role`, `aria-label`, `tabindex` attributes on DOM elements |

### Phase 5: Dashboard Modernization

1. **D1: Replace `location.reload()`.** Fetch `/api/status` as
   JSON, update the status panel DOM in-place.

2. **D2: Extract game catalog.** Move `$GameCatalog` from
   `server.ps1` into a JSON file (`tools/web/games.json`) or a
   Codex source file.

3. **D3: Shared game components.** Factor the common game chrome
   (controls bar, status line, game log, speed slider, engine tag)
   into a shared CSS class set and a small JS library. The 34
   individual game files would shrink significantly.

---

## 4. Data Architecture

### Where Data Lives

| Data Kind | File | Format |
|-----------|------|--------|
| Item types, materials, rarities, conditions, enchantments, mods | `ExplorerData.codex` | Codex records + lists |
| Character races, classes, genders, personalities, portraits | `ExplorerData.codex` | Codex records + lists |
| Setting biomes, times, weathers, moods, scales | `ExplorerData.codex` | Codex records + lists |
| Negative prompt defaults | `ExplorerData.codex` | Codex text constants |
| Name extraction helpers | `ExplorerData.codex` | Codex functions |
| Theme definition (dark-gold) | `ExplorerTheme.codex` | Codex `Theme` record |
| DOM stub signatures | `ExplorerTheme.codex` | Codex function stubs |
| CSS component strings | `ExplorerTheme.codex` | Codex text constants |
| Shared nav/dropdown/history builders | `ExplorerTheme.codex` | Codex functions |
| DB schema (7 tables) | `ExplorerDb.codex` | Codex Catalog tables |
| Game catalog (34 games) | `tools/web/server.ps1` | PowerShell hash tables |

### Data Flow

```
ExplorerData.codex ──cites──> *App.codex ──plug──> HTML page
                                  │
ExplorerTheme.codex ─cites──┘
                                  │
                        inject-theme-css at runtime
                                  │
                        ┌─────────▼─────────┐
                        │  Browser DOM       │
                        │  CSS variables set │
                        │  DOM stubs active  │
                        └─────────┬─────────┘
                                  │ fetch
                        ┌─────────▼─────────┐
                        │  server.ps1        │
                        │  proxies to CDX    │
                        │  proxies to SD     │
                        └─────────┬─────────┘
                                  │ serial
                        ┌─────────▼─────────┐
                        │ ExplorerServer.cdx │
                        │ ExplorerDb schema  │
                        │ JSON API           │
                        └───────────────────┘
```

### Adding New Data

To add a new item type, race, biome, or any content:

1. Add the record to `ExplorerData.codex` in the appropriate section.
2. If a new name-extraction helper is needed, add it there.
3. Rebuild the relevant `*App.codex` page through the HTML plug.
4. The shared dropdown builder and dim-pill system automatically
   pick up the new entry.

To add a new dimension axis to a page:

1. Define the data type and list in `ExplorerData.codex`.
2. Add a name extractor for it.
3. In the `*App.codex` file, add a state slot, a dropdown, and
   wire it into the prompt builder.
4. Rebuild through the plug.

---

## 5. Resolved Questions

1. **Gen 1 disposition.** The legacy inline-JS files
   (`ItemDesigner.codex`, `CharacterDesigner.codex`,
   `SettingDesigner.codex`) are reference material only. Once all
   lessons have been extracted into the Gen 2 pattern, delete them.

2. **Seed mismatch on CodexMagic stream.** Resolved — CL 2573
   build files applied, full build passes (120/120, all gates green).

3. **IR size blocker for foreword cites.** No longer a hard
   constraint. Survey constants are now tunable and the compiler
   auto-retries. ExplorerTheme can migrate from inlined types to
   direct `cites UI chapter ...` when ready.

4. **VoiceStudio.** Deferred. Sample audio code exists in spark.
   Ignore for this buildout.

## 6. Open Questions

1. **`tools/web/explorer/` cleanup.** The `card-ui.js` and
   `item-ui.js` files reference transpiled JS files not checked in.
   Dead code from Gen 1 hybrid? If so, delete with the Gen 1 files.

---

## 7. Priority Order

The buildout is oriented around the goal: users own their theme
and layout. We provide widgets and data; they arrange them.

| # | Work Item | Phase | Status |
|---|-----------|-------|--------|
| 1 | `mount-widget` runtime — WidgetNode tree to DOM | 1 | **DONE** CL 2574 |
| 2 | Flatten let-chain IIFEs (E3) | 1 | **DONE** CL 2574 |
| 3 | Warn on dropped handlers (E5) | 1 | **DONE** CL 2574 |
| 4 | Extract dom builtins into named sections (E1) | 1 | **DONE** CL 2574 |
| 5 | Theme-driven widget rendering — `_wkStyle`, `_wkSS` | 2 | **DONE** CL 2574 |
| 6 | `mount-widget-themed` — user's theme flows to all widgets | 2 | **DONE** CL 2574 |
| 7 | AJAX callbacks — `fetch-then`, `fetch-get-then` | 3 | **DONE** CL 2574 |
| 8 | Event wiring — `dom-on-click`, `dom-on-input`, `dom-on-key` | 3 | **DONE** CL 2574 |
| 9 | Reactive render loop — `set-render`, `state-set-render`, `request-render` | 3 | **DONE** CL 2574 |
| 10 | Dialog — `show-alert`, `show-confirm`, `show-prompt`, `close-dialog` | 4 | **DONE** CL 2574 |
| 11 | Widget types inlined in ExplorerTheme (WidgetNode, WidgetKind, etc.) | 2 | **DONE** CL 2574 |
| 12 | Migrate ExplorerTheme to `cites UI chapter ...` | — | **DEFERRED** plug IR size limit |
| 13 | Delete Gen 1 inline-JS files + orphaned JS glue | 4 | **DONE** CL 2574 |
| 14 | Animation CSS: spin/pulse/bounce @keyframes + CSS transition | 4 | **DONE** CL 2574 |
| 15 | Accessibility: `dom-set-aria`, `dom-set-role` builtins | 4 | **DONE** CL 2574 |
| 16 | Dashboard `fetch` refresh (D1) — `/api/status` endpoint | 5 | **DONE** CL 2574 |
| 17 | User theme persistence via localStorage | — | **BLOCKED** plug compile ceiling (see below) |
| 18 | Game catalog → `tools/web/games.json` + server.ps1 loader | 5 | **DONE** CL 2574 |
| 19 | Shared game component library (D3) | — | Not started |

### IR Size Constraint

The HTML plug source compiles at ~74KB. At ~74KB the compiler
crashes with 2048MB RAM and succeeds with 4096MB retry. Each new
`&`-chain function adds IR depth. Mitigation: split long
concatenations into multiple named functions (e.g.,
`emit-dom-dialog-a`, `emit-dom-dialog-b`, `emit-dom-dialog-c`).
The foreword-cite migration is blocked by this — adding 37KB of
foreword UI chapters would push the source to ~111KB, well past
the limit.

The root cause is `parameterize-walk-children` in TypeChecker.codex
recursing too deeply on nested type trees from long `&`
concatenation chains. The stack overflows at ~75KB source. See
`docs/Test/PLUG-PARAMETERIZE-CRASH.md` for full investigation
and fix proposals. Theme persistence (`save-theme`/`load-theme`)
is ready to add — the JS runtime code is in
`codex/plugs/html/runtime-extra.js` — but cannot be compiled into
the plug until the stack depth issue is resolved.
