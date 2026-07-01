# The Shimmering Portal

A web developer's guide to the Codex UI-to-browser pipeline.

---

## Chapter 1: The Two Paths

Codex produces web output through two paths. Understanding which
one you are on determines everything that follows.

### Path A: The Compiled Path (HTML Plug)

Write a `.codex` program that builds `WidgetNode` trees, applies a
`Theme`, handles `Event`s. Compile it through the HTML plug. Out
comes a self-contained HTML page with CSS and JavaScript.

```
source.codex --> compiler (IR mode) --> html-plug.cdx --> index.html
```

The pipeline:

1. `build/compile.ps1 -Src source.codex -Out source.ir -IrCce`
   produces IR text in CCE encoding.
2. `codex/plugs/html/run.ps1 -Src source.codex -Out output.html`
   wraps both steps: compile to IR, boot the plug CDX in codex-vm,
   feed it the IR, capture the HTML output.

The HTML plug (`codex/plugs/html/HtmlEmitter.codex`) walks the IR
tree and emits:
- Type definitions as JS constructor functions
- Function definitions as JS functions
- Expressions as JS expressions (ternary for if, IIFE for let, etc.)
- A runtime preamble with DOM builtins, text helpers, and theme CSS

To build the plug itself: `pwsh codex/plugs/html/build.ps1`.

### Path B: The Hand-Built Path (tools/web)

Write HTML, CSS, and JavaScript directly. The web server
(`tools/web/server.ps1`) serves these files alongside the
dashboard and game catalog.

```
tools/web/
  index.html          Template with {{STATUS}} and {{GAMES}} markers
  style.css           Dark-theme stylesheet (GitHub aesthetic)
  app.js              Tab switching, filtering, audio system
  server.ps1          HTTP server with data gathering + templating
  backgammon.html     34 standalone game pages
  ...
  magic/              Magic: The Gathering client
  explorer/           Card and item design tools (hybrid path)
```

### Path C: The Hybrid Path (explorer)

Data and logic live in Codex source, transpiled to JavaScript via
the HTML plug. A thin hand-written JS file (`card-ui.js`,
`item-ui.js`) wires the transpiled functions to the DOM.

The Codex source exports pure functions:
- `dimensions()` returns the exploration axes
- `build_item_prompt(item, material, rarity, extra)` constructs
  an image generation prompt

The JS glue calls these functions and renders the results.

---

## Chapter 2: The Widget Kingdom

The 28-module UI foreword (`codex.foreword.ui`) defines a
platform-agnostic widget system. These types are the contract
between application code and any rendering backend.

### The Core Types

```
WidgetKind =
  | WkPanel            -- container (div)
  | WkLabel (Text)     -- read-only text (span)
  | WkButton (Text)    -- clickable (button)
  | WkGauge (val) (max) -- progress bar (progress)
  | WkSeparator        -- horizontal rule (hr)
  | WkInput (text) (cursor) -- text input (input/textarea)
  | WkCustom (tag)     -- extensible (div with data attribute)

WidgetNode = record {
  wn-kind, wn-state, wn-id,
  wn-children, wn-child-count,
  wn-layout-dir, wn-gap, wn-flex,
  wn-min-w, wn-min-h, wn-bounds
}
```

Build trees with the constructors: `widget-panel`, `widget-label`,
`widget-button`, `widget-gauge`, `widget-separator`, `widget-input`,
`widget-custom`.

Extended catalog (built on top of core): `widget-checkbox`,
`widget-radio`, `widget-slider`, `widget-progress`,
`widget-dropdown`, `widget-toolbar`, `widget-menu`, `widget-tabs`,
`widget-list-view`, `widget-number-field`, `widget-text-area`.

### The Box Model

Every widget occupies a `LayoutRect { lr-x, lr-y, lr-w, lr-h }`.
The box model decomposes this into four layers:

```
Outer rect
  +-- Margin    (Edges: top, right, bottom, left)
    +-- Border  (Border: per-side width + color, CornerStyle)
      +-- Padding (Edges)
        +-- Content
```

CSS mapping:
- `Edges` maps to `margin` or `padding` shorthand
- `BorderSide { brd-width, brd-color }` maps to `border-width`, `border-color`
- `CornerStyle` maps to `border-radius` (CornerRound) or 0 (CornerSharp)
- `LayoutRect` maps to `width`, `height`, and positioning

### Layout

`LayoutDir = DirRow | DirColumn` maps to `flex-direction`.
`LayoutItem { li-min-w, li-min-h, li-flex, li-margin }` maps to
`min-width`, `min-height`, `flex`, and `margin`.

The flex layout engine (`flex-layout`) distributes space exactly
like CSS flexbox: fixed items get their min size, flex items split
the remainder proportionally. Gap maps to CSS `gap`.

Additional layouts:
- `GridLayout { gl-columns, gl-row-gap, gl-col-gap }` maps to CSS Grid
- `SplitLayout { sl-ratio, sl-direction, sl-gap }` maps to a two-pane flex
- `stack-layout` maps to `position: absolute` overlay stack

### Theming

A `Theme` record contains a `Palette` (10 named colors) and
`StateStyles` for each widget type (panel, button, label, input,
gauge, separator). Each `StateStyles` has five `WidgetStyle`
variants: normal, hover, pressed, disabled, focused.

```
Theme
  th-palette : Palette
    pal-bg, pal-fg, pal-primary, pal-secondary, pal-accent,
    pal-muted, pal-error, pal-success, pal-warning, pal-border
  th-panel   : StateStyles (5 WidgetStyle records)
  th-button  : StateStyles
  th-label   : StateStyles
  th-input   : StateStyles
  th-gauge   : StateStyles
  th-separator : StateStyles
```

CSS mapping: Palette colors become `:root` CSS custom properties.
StateStyles become pseudo-class selectors (`:hover`, `:active`,
`:disabled`, `:focus`). Widget classes: `.wk-panel`, `.wk-btn`,
`.wk-label`, `.wk-input`, `.wk-gauge`, `.wk-sep`.

Three built-in themes: `theme-terminal` (green-on-black, sharp
corners), `theme-lcars` (orange/purple, rounded), `theme-minimal`
(white, thin borders).

Colors are integer-packed ARGB: `0xFF0000` = red. The JS runtime
function `_c(n)` converts to CSS hex `#rrggbb`.

---

## Chapter 3: The Data Layer

Applications that need dynamic content (card designers, item
explorers, character builders) follow the hybrid pattern:

### Data File (Codex)

Define your data as pure functions returning records and lists.
Place the source in the application directory (e.g.,
`apps/games/codexmagic/CardDesigner.codex`).

```codex
Chapter: CardDesigner

Section: Categories
  item-categories : List ItemCategory
  item-categories = [
    ItemCategory { name = "Sword", icon = "blade", ... },
    ItemCategory { name = "Shield", icon = "guard", ... }
  ]

Section: Prompts
  build-item-prompt : ItemCategory, Material, Rarity, Text -> Text
  build-item-prompt (item) (mat) (rarity) (extra) =
    mat.prefix & " " & item.name & ", " & rarity.quality & ...
```

### Transpile to JavaScript

```powershell
codex/plugs/html/run.ps1 -Src apps/.../CardDesigner.codex `
    -Out tools/web/explorer/card-app.js
```

This produces a JS file where each Codex function is a JS function.

### DOM Glue (JavaScript)

Write a thin `card-ui.js` that:
1. Calls the exported data functions (`item_categories()`, etc.)
2. Renders the DOM (filter pills, image grids, lightboxes)
3. Calls the prompt-builder for image generation

The DOM glue handles browser-specific concerns (event listeners,
fetch API, DOM manipulation). The Codex source handles domain
logic (what categories exist, how prompts are composed, what
rarities mean).

### Server API

`tools/web/server.ps1` serves static files and provides API
endpoints:
- `GET /api/config` returns model/sampler/LoRA configuration
- `POST /api/generate` dispatches image generation requests

---

## Chapter 4: Adding a New Explorer Page

To add a character designer, setting builder, or any new explorer:

### Step 1: Codex Data Source

Create `apps/games/codexmagic/CharacterDesigner.codex`:

```codex
Chapter: CharacterDesigner

Section: Archetypes
  character-archetypes : List Archetype
  character-archetypes = [ ... ]

Section: Prompt Builder
  build-character-prompt : Archetype, Race, Class, Text -> Text
  build-character-prompt (arch) (race) (cls) (extra) = ...
```

### Step 2: Transpile

```powershell
codex/plugs/html/run.ps1 -Src apps/games/codexmagic/CharacterDesigner.codex `
    -Out tools/web/explorer/character-app.js
```

### Step 3: DOM Glue

Create `tools/web/explorer/character-ui.js` following the
`item-ui.js` pattern: read exported functions, render filter
pills, wire generate button.

### Step 4: HTML Page

Create the HTML page or add a route to the server. Reference
both scripts:

```html
<script src="/explorer/character-app.js"></script>
<script src="/explorer/character-ui.js"></script>
```

### Step 5: Server Route

Add the page to `server.ps1`'s routing table so it is served
at the desired URL.

---

## Chapter 5: The CSS Contract

When the HTML plug emits themed widgets, it generates CSS classes
following this convention:

| Class | Widget | Source |
|-------|--------|--------|
| `.wk-panel` | Panel | `th-panel` StateStyles |
| `.wk-btn` | Button | `th-button` StateStyles |
| `.wk-label` | Label | `th-label` StateStyles |
| `.wk-input` | Input | `th-input` StateStyles |
| `.wk-gauge` | Gauge | `th-gauge` StateStyles |
| `.wk-sep` | Separator | `th-separator` StateStyles |
| `.wk-row` | Row layout | `display: flex; flex-direction: row` |
| `.wk-col` | Column layout | `display: flex; flex-direction: column` |

Hand-written pages that want to match the compiled theme should
use these same classes and include the theme CSS (generated by
`inject_theme_css(theme)` in JavaScript).

The `tools/web/style.css` file is independent of the widget
system — it styles the dashboard and game catalog. Games
that want to use both can load `style.css` for chrome and
theme CSS for widget content.

---

## Chapter 6: Image Generation Pipeline

The explorer pages use Stable Diffusion for card art. The pipeline:

1. Codex source defines prompt templates with dimension axes
   (model, sampler, steps, CFG, LoRA).
2. The JS glue renders a pin-and-cascade UI: pin a dimension
   to fix its value, then generate images across remaining axes.
3. `POST /api/generate` sends the prompt to the SD backend.
4. Results are displayed in a grid with lightbox preview.

Status checking: `check_sd_status()` pings `/api/config` and
updates a status dot (green = online, red = offline).

---

## Chapter 7: Missing Rooms (Known Gaps)

These capabilities exist in the UI foreword but have no web
emission path yet:

| Module | Foreword Status | Web Status | Priority |
|--------|----------------|------------|----------|
| Widget tree to DOM | 28 modules, complete | HtmlEmitter emits JS, not semantic HTML | High |
| Event routing | HandlerTable, EventPath | No addEventListener gen | High |
| Dialog | DialogConfig/Result | No dialog emission | Medium |
| Overlay | Tooltip/Popup/ContextMenu/Modal | Not started | Medium |
| Scroll | ScrollState | No overflow CSS | Low |
| Animation | Throbber/Transition/KeyframeSeq | No @keyframes | Low |
| Accessibility | Role/Label/LiveRegion | No ARIA attributes | Medium |
| Binding | Observable/ObsText/BindingTable | No reactive loop | High |
| Charts | Bar/Line/Pie/Area | No SVG/Canvas | Low |
| Vector | SVG-like path rendering | No SVG emission | Low |
| Character explorer | Not started | Not started | New |
| Setting explorer | Not started | Not started | New |

The WebEmitter.md design doc has the full mapping tables for
each of these. The UICapabilityMap.md has the cross-platform
coverage matrix.

---

## Appendix A: File Map

```
codex/plugs/html/
  HtmlPlug.codex         Entry point (reads IR, dispatches)
  HtmlEmitter.codex      IR walker, JS/CSS/HTML generation
  build.ps1              Bundles foreword deps, compiles plug CDX
  run.ps1                Source -> IR -> plug CDX -> HTML
  codex.project.json     Plug metadata

codex/foreword/ui/       28 modules (the source of truth)
  BoxModel.codex         LayoutRect, Edges, Border, box decomposition
  Widget.codex           WidgetNode, WidgetKind, construction/queries
  Layout.codex           Flex, Grid, Split, Stack layouts
  Theme.codex            Palette, WidgetStyle, StateStyles, Theme
  Render.codex           Framebuffer rendering (bare-metal backend)
  Surface.codex          Compositor with z-order and dirty rects
  Window.codex           Window manager
  Event.codex            Event routing with HandlerTable
  Focus.codex            Tab order and focus ring
  Scroll.codex           Viewport offset tracking
  Overlay.codex          Tooltip, Popup, ContextMenu, Modal
  TextField.codex        Text input with cursor/selection
  Animation.codex        Throbber, Transition, KeyframeSeq
  Binding.codex          Observable values with dirty tracking
  Dialog.codex           Dialog configuration and results
  Orchestrator.codex     Central UI orchestration
  Accessibility.codex    ARIA-like a11y metadata
  Charts.codex           Bar/Line/Pie/Area chart data
  Font.codex             Bitmap font with glyph metrics
  Icon.codex             Multi-size bitmap icons
  Selection.codex        Range selection
  Cursor.codex           Cursor shape management
  Drag.codex             Drag and drop
  Touch.codex            Touch points and gestures
  Clipboard.codex        Copy/paste
  Sound.codex            UI sound effects
  Vector.codex           SVG-like vector paths
  RichText.codex         Rich text formatting

tools/web/
  index.html             Dashboard template
  style.css              Dark-theme CSS
  app.js                 Tab/filter/audio logic
  server.ps1             HTTP server + data gathering
  explorer/
    card-ui.js           Card dimension explorer (DOM glue)
    item-ui.js           Item designer (DOM glue)
  magic/                 Magic: The Gathering client
  *.html                 34 standalone game pages

docs/Designs/Active/Tools/
  WebEmitter.md          Full design: widget-to-DOM mapping spec
  UICapabilityMap.md     Cross-platform coverage matrix
```

---

## Appendix B: Quick Reference

### Build the HTML plug
```powershell
pwsh codex/plugs/html/build.ps1
```

### Compile a Codex program to HTML
```powershell
pwsh codex/plugs/html/run.ps1 -Src my-app.codex -Out my-app.html
```

### Start the web server
```powershell
pwsh tools/web/server.ps1 -Port 8080
```

### Run all games locally
Navigate to `http://localhost:8080/#games` after starting the server.
