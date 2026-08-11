# The Shimmering Portal

A web developer's guide to the Codex UI-to-browser pipeline.

---

## Chapter 1: The Three Paths

Codex produces web output three ways. Understanding which one you are
on determines everything that follows. All three end in a browser; only
the first two go through the compiler.

### Path A: The Compiled App Page (the main road)

Write a `.codex` chapter named `<Name>Page.codex` that builds
`WidgetNode` trees, defines a `Theme`, and handles events. The compiler
emits IR; the HTML plug turns that IR into a single self-contained HTML
file with the CSS and JavaScript inlined.

```
apps/<name>/<Name>Page.codex
  --> build/bundle-app.ps1      (inlines every cited chapter)
  --> build/compile.ps1 -IrCce  (IR text, CCE)
  --> codex/plugs/html/build-output/html-plug.cdx  (in codex-vm)
  --> apps/<name>/web/<name>.html
```

One command does all of it for every app at once:

```powershell
pwsh build\build-apps.ps1            # all apps
pwsh build\build-apps.ps1 -Only notes
```

`build-apps.ps1` discovers apps by convention, not by a list: every
artifact `apps/<dir>/web/<name>.html` pairs with the chapter
`apps/<dir>/<Name>Page.codex` (or `<Name>WebPage.codex`) whose base
name, minus the suffix and lowercased, equals `<name>`. **74 pages are
built this way today** -- mail, notes, maps, music, markets, globe,
fishtank, cvmm, the 40-odd gpushow demos, and the rest. Adding an app is
adding a Page chapter and an (initially empty) `web/<name>.html` under
Perforce. No script edit.

The output is a real page: `<div id="app">`, a `<style>` block, and one
`<script>` block holding the transpiled chapter plus the plug's JS
runtime. No external files, no bundler, no npm.

To compile a single source by hand:

```powershell
pwsh codex\plugs\html\run.ps1 -Src apps\notes\NotesPage.codex -Out build-output\notes.html
```

`compile.ps1` resolves `cites` itself (via `build/quire-map.ps1`), so
`run.ps1` takes a chapter directly. `build-apps.ps1` bundles first
anyway -- `bundle-app.ps1` inlines the transitive cites into one file, and
the compiler then warns if it had to resolve a chapter the bundle
missed. That check is the reason for the extra step; it is not required
to get a page out.

To build the plug itself: `pwsh codex\plugs\html\build.ps1`.

### Path B: The Compiled Page Plus a CDX Server (the explorer)

Same compiled front end as Path A, but the data lives in a bare-metal
Codex server instead of in the page. `apps/explorer/` is the worked
example, and it is **pure Codex on both sides**:

```
apps/explorer/CharDesignerApp.codex   -- browser, via the HTML plug
apps/explorer/ExplorerServer.codex    -- CDX binary, serves /api/* over TCP
apps/explorer/ExplorerData.codex      -- the single source of content
apps/explorer/build-explorer-db.ps1   -- content -> explorer.db.img (disk)
apps/explorer/run-designers-demo.ps1  -- boots the CDX, bridges HTTP -> TCP
```

The page fetches its dimensions at load (`/api/d/races`,
`/api/d/classes`, ...); the guest CDX reads them off the attached disk
image and answers JSON. Nothing about the content is compiled into the
page. Chapter 4 walks the full sequence.

### Path C: The Hand-Built, Served Page (the games portal)

`apps/games/` is hand-written HTML/CSS/JS served by a PowerShell HTTP
server that proxies its API to a Codex game engine.

```
apps/games/
  index.html      Portal template with {{VERSION}}, {{STATUS}}, {{GAMES}} markers
  style.css       Dark-theme stylesheet (GitHub aesthetic)
  app.js          Tab switching, filtering, hover audio (hand-written)
  games.json      34-entry game catalog
  server.ps1      HTTP server: dashboard, catalog, static files, /api/* bridge
  GameServer.codex  The engine -- compiled to build/output/GameServer.cdx and
                    booted by server.ps1; every /api/* request is forwarded
                    to it over the serial link
  classic/web/    34 game pages (33 named + rungame.html fallback)
  codexmagic/web/ CodexMagic SPA -- a mix (see below)
  magic/          Magic engine sources (no web assets)
```

```powershell
pwsh apps\games\server.ps1 -Port 8080
```

The chrome is hand-written; the *game logic* is not. `server.ps1` boots
`GameServer.cdx` in codex-vm (compiling it first if missing) and every
`/api/*` request is answered by that CDX binary.

CodexMagic straddles Paths A and C: `apps/games/codexmagic/*Page.codex`
chapters are compiled to `apps/games/codexmagic/web/*.html` through the
HTML plug, then post-processed to inject `magic.css`, `magic.js`, and
`card-render.js` (still hand-written).

```powershell
pwsh build\build-magic-pages.ps1                      # all *Page.codex
pwsh build\build-magic-pages.ps1 -Pages CollectionPage
pwsh apps\games\codexmagic\web\build-pages.ps1        # admin + marketplace only
```

> There is no `tools/web/`. It was deleted. Its contents became
> `apps/games/` (portal, catalog, classic pages) and `apps/explorer/`
> (the designers, now compiled from Codex).

---

## Chapter 2: The Widget Kingdom

The **50-module** UI foreword (`codex/foreword/ui`, quire `UI`) defines
a platform-agnostic widget system. These types are the contract between
application code and any rendering backend -- browser, framebuffer, GPU.

### The Core Types

`Widget.codex`:

```
WidgetKind =
  | WkPanel                  -- container (div)
  | WkLabel (Text)           -- read-only text (span)
  | WkButton (Text)          -- clickable (button)
  | WkGauge (val) (max)      -- progress bar (progress)
  | WkSeparator              -- horizontal rule (hr)
  | WkInput (text) (cursor)  -- text input (input)
  | WkCustom (tag)           -- extensible (div with data-custom)

WidgetNode = record {
  wn-kind, wn-state, wn-id,
  wn-children, wn-child-count,
  wn-layout-dir, wn-gap, wn-flex,
  wn-min-w, wn-min-h, wn-bounds
}
```

Build trees with the constructors: `widget-panel`, `widget-label`,
`widget-button`, `widget-gauge`, `widget-separator`, `widget-input`,
`widget-custom`. Extended catalog (built on top of the core seven):
`widget-checkbox`, `widget-radio`, `widget-slider`, `widget-progress`,
`widget-dropdown`, `widget-toolbar`, `widget-menu`, `widget-tabs`,
`widget-list-view`, `widget-number-field`, `widget-text-area`.

`WkCustom` is the escape hatch, and the browser apps lean on it. The
`WebWidgets` chapter (Path A, see Chapter 3) wraps it: `widget-box id
cls children` emits a `WkCustom` tagged `box:<cls>`, which the plug
renders as a `<div class="<cls>">`; `widget-box-click` emits `boxc:` --
the same div, wired to the click handler.

### The Box Model

`BoxModel.codex` owns `LayoutRect { lr-x, lr-y, lr-w, lr-h }` and the
integer-pixel decomposition (`box-margin-rect`, `box-border-rect`,
`box-content-rect`). The *style* records it decomposes --
`Edges`, `BorderSide`, `Border`, `CornerStyle` -- live in `Theme.codex`.

```
Outer rect
  +-- Margin    (Edges: edge-top/right/bottom/left)
    +-- Border  (Border: four BorderSides + CornerStyle)
      +-- Padding (Edges)
        +-- Content
```

CSS mapping, as actually emitted by `HtmlEmitter`:
- `Edges` maps to the `margin` / `padding` shorthand
- `BorderSide { brd-width, brd-color }` maps to `border: Npx solid #rrggbb`
  (the emitter reads `bdr-top` and applies it to all four sides)
- `CornerStyle` maps to `border-radius`: `CornerRound n` gives `npx`,
  `CornerBevel` gives `0`, `CornerSharp` is omitted
- `ws-min-width` / `ws-min-height` map to `min-width` / `min-height`

`Shadow`, `Gradient`, and `AccentBorder` exist in `WidgetStyle` but the
HTML emitter does not lower them yet (Chapter 7).

### Layout

`Layout.codex`: `LayoutDir = DirRow | DirColumn` maps to
`flex-direction`. `LayoutItem { li-min-w, li-min-h, li-flex, li-margin }`
maps to `min-width`, `min-height`, `flex`, `margin`. `flex-layout`
distributes space exactly like CSS flexbox: fixed items take their min
size, flex items split the remainder proportionally. Gap maps to `gap`.

Also present: `GridLayout { gl-columns, gl-row-gap, gl-col-gap }`,
`SplitLayout { sl-ratio, sl-direction, sl-gap }`, and `stack-layout`.
These compute rects for the framebuffer backend; the browser path uses
the `.wk-row` / `.wk-col` flex classes and whatever CSS the app supplies.

### Theming

`Theme.codex`. A `Theme` is pure data: a name, a `Palette` of ten
colors, and `StateStyles` for six widget families.

```
Theme
  th-name    : Text
  th-palette : Palette
    pal-bg, pal-fg, pal-primary, pal-secondary, pal-accent,
    pal-muted, pal-error, pal-success, pal-warning, pal-border
  th-panel   : StateStyles   (ss-normal/hover/pressed/disabled/focused)
  th-button  : StateStyles
  th-label   : StateStyles
  th-input   : StateStyles
  th-gauge   : StateStyles
  th-separator : StateStyles
```

CSS mapping: palette colors become `:root` custom properties
(`--bg`, `--fg`, `--primary`, ... `--border`). Each `StateStyles`
becomes a class plus its pseudo-class variants (`:hover`, `:active`,
`:disabled`, `:focus`). See Chapter 5 for the class names.

Three built-in themes ship in the foreword: `theme-terminal`,
`theme-lcars`, `theme-minimal`. Apps generally define their own
(`notes-theme`, `dark-gold` in `apps/explorer/ExplorerTheme.codex`).

Colors are integer-packed RGB (`0..16777215`); the literal `#D8D8D8` is
accepted. The JS runtime function `_c(n)` converts to CSS `#rrggbb`.

---

## Chapter 3: The Browser Runtime Contract

This is the chapter that tells you what you are allowed to call.

The HTML plug transpiles your chapter to JavaScript. Most Codex
functions become JS functions. But a handful of names are **not**
transpiled -- they are *bound to the plug's JS runtime*. The list lives
in one function: `is-html-builtin` in
`codex/plugs/html/HtmlEmitter.codex`. If a name is in that list, the
plug skips your definition and calls its own JavaScript instead.

Every such name has a Codex-typed stub so the type checker is happy.
The stubs live in the **WebApp quire**, `apps/webapp/`:

| Chapter | Holds |
|---------|-------|
| `WebRuntime.codex` | The whole JS-bridge stub contract: `dom-*`, `state-*`, `fetch-*`, `register-handlers`, `register-input-handler`, `set-render`, `mount-widget-themed`, `inject-theme-css`, `show-alert/confirm/prompt`, `play-tone`, `set-timeout`, `local-storage-*`, `download-text` |
| `WebTheme.codex` | The eight theme builders (`bsn`, `bn`, `ez`, `bdr`, `eu`, `exy`, `ws`, `ss-flat`) and `inject-app-style theme css` |
| `WebWidgets.codex` | `wk-attach`, `widget-box`, `widget-box-click`, `id-num`, `no-pick` |

So a browser app's cite block is, almost always, exactly this:

```codex
Chapter: NotesPage
  cites UI chapter Theme
  cites UI chapter Widget
  cites WebApp chapter WebRuntime
  cites WebApp chapter WebTheme
  cites WebApp chapter WebWidgets
```

The app keeps only: its palette and `Theme`, its CSS text, its widget
tree, its data helpers, and its click/input handlers.

### What the runtime gives you

- **DOM** -- `dom-get`, `dom-create`, `dom-set-text`, `dom-set-html`,
  `dom-set-attr`, `dom-get-value`, `dom-set-value`, `dom-append`,
  `dom-prepend`, `dom-add-class`, `dom-remove-class`, `dom-set-style`
- **Widget mount** -- `mount-widget`, `mount-widget-themed theme tree`
  (styles from the `Theme`, appends into `#app`), `mount-widget-into id tree`
  (re-mounts a subtree in place -- this is how a designer rebuilds one row)
- **Events** -- `register-handlers pick click`, `register-input-handler`,
  `dom-on-click`, `dom-on-input`, `dom-on-key`. `WkButton` nodes wire
  their own click to the registered click handler, keyed by `wn-id`;
  `WkInput` nodes wire `input` the same way.
- **State + render loop** -- `state-get/set`, `state-get-text/set-text`,
  `set-render f`, `request-render`, `state-set-render` (set a key and
  schedule a repaint on the next animation frame)
- **AJAX** -- `fetch-json`, `fetch-then`, `fetch-get-then url cb`,
  `json-parse-obj`, `json-obj-field`, `json-stringify`, `url-encode`
- **Dialogs** -- `show-alert`, `show-confirm`, `show-prompt`,
  `close-dialog` (real `<dialog>` elements, `showModal()`)
- **A11y** -- `dom-set-aria`, `dom-set-role`
- **Animation** -- `css-animate-spin/pulse/bounce`, `css-transition`
  (`@keyframes wk-spin`, `wk-pulse`, `wk-bounce` are injected)
- **Persistence** -- `local-storage-get/set`, `save-theme`, `load-theme`,
  `save-layout`, `load-layout`, `download-text`
- **Misc** -- `random-int`, `play-tone`, `set-timeout`, `next-card-id`,
  `generate-image`, `check-sd-status`

### Adding a runtime primitive

One change, every app gets it. In order:

1. Add the JavaScript to the right `emit-dom-*` function in
   `codex/plugs/html/HtmlEmitter.codex`.
2. Add the name to `is-html-builtin` in the same file.
3. Rebuild the plug: `pwsh codex\plugs\html\build.ps1`.
4. Add the typed stub to `apps/webapp/WebRuntime.codex` (a body that
   type-checks; it is never emitted).
5. Regenerate the pages: `pwsh build\build-apps.ps1`.

Never copy a stub into an app chapter. The names in `WebRuntime` *are*
the contract.

---

## Chapter 4: Adding a Page

Two recipes, one per compiled path.

### Recipe A: A standalone app page

Everything ships in the HTML file; no server.

**Step 1 -- the chapter.** Create `apps/pomodoro/PomodoroPage.codex`
(any app dir; `apps/notes/NotesPage.codex` is the reference to copy):

```codex
Chapter: PomodoroPage
  cites UI chapter Theme
  cites UI chapter Widget
  cites WebApp chapter WebRuntime
  cites WebApp chapter WebTheme
  cites WebApp chapter WebWidgets

 We say:

Section: Theme
  pomo-theme : Theme
  pomo-theme = ...          -- Palette + ws/ss-flat builders from WebTheme

Section: CSS
  pomo-css : Text
  pomo-css = "*{margin:0;box-sizing:border-box}" & ...

Section: Tree
  page-tree : Integer -> WidgetNode
  page-tree (x) = widget-box "root" "wrap" [ ... ]

Section: Entry
  render-pomo : Integer -> Integer
  render-pomo (x) = mount-widget-themed pomo-theme (page-tree 0)

  opening : [Console] Integer
  opening = act
    let t = inject-app-style pomo-theme pomo-css
    in let s = state-set "seconds" 1500
    in let r = set-render render-pomo
    in let h = register-handlers no-pick pomo-click
    in render-pomo 0
  end
```

**Step 2 -- the artifact.** Create the (empty) output file so the build
discovers the app, and put it under Perforce:

```powershell
New-Item -ItemType Directory -Force apps\pomodoro\web | Out-Null
New-Item -ItemType File apps\pomodoro\web\pomodoro.html
p4 add apps\pomodoro\PomodoroPage.codex apps\pomodoro\web\pomodoro.html
```

The pairing is by name: `PomodoroPage` minus `Page`, lowercased, equals
`pomodoro`, so `web/pomodoro.html` is its artifact.

**Step 3 -- build.**

```powershell
pwsh build\build-apps.ps1 -Only pomodoro
```

It bundles the cites, compiles to IR, runs the plug in codex-vm, and
writes the HTML (p4-editing the artifact first, normalizing to CRLF).
Open the file in a browser. That is the whole loop.

### Recipe B: A DB-backed designer page (explorer)

The page is compiled; the content and the API come from a CDX server.

**Step 1 -- content.** Add your dimension lists to
`apps/explorer/ExplorerData.codex` (the single source of truth), then
add a table row to the `$spec` table in
`apps/explorer/build-explorer-db.ps1` naming the table and the list.

**Step 2 -- the disk image.**

```powershell
pwsh apps\explorer\build-explorer-db.ps1
# -> build-output\explorer.db.img  (1 MB, 31 tables today)
```

**Step 3 -- the page chapter.** Create `apps/explorer/MyDesignerApp.codex`
modelled on `CharDesignerApp.codex`. It cites `Explorer chapter
ExplorerTheme` (the `dark-gold` theme, the layout CSS, the shared
`designer-page-tree`, `widget-dimsel`, `widget-dim-flat`,
`add-history-card`, `gen-body`) plus whichever UI chapters it needs. The
shape is fixed:

- `opening` calls `init-explorer`, seeds `state-set` keys,
  `register-handlers on-pick on-click`, `mount-widget-themed dark-gold
  (page-tree 0)`, then one `fetch-get-then "/api/d/<table>" cb-<table>`
  per dimension.
- Each `cb-*` does `state-set-text "<table>" s` and bumps a counter;
  when every fetch has landed, it calls `mount-widget-into "dimrow" ...`
  to build the picker rows from the fetched tables (`tbl-names`,
  `tbl-field`, `parse-table`).
- `on-pick group idx` updates the selection and re-mounts the row.
- `on-click "gen"` builds the prompt and calls `generate-image`.

**Step 4 -- compile the page.**

```powershell
pwsh codex\plugs\html\run.ps1 -Src apps\explorer\MyDesignerApp.codex `
    -Out build-output\mydesigner.html
```

(Explorer pages go through `run.ps1` directly -- `compile.ps1` resolves
the `Explorer` and `UI` cites from the quire map. No bundle step.)

**Step 5 -- the server.** Compile the CDX once:

```powershell
pwsh build\compile.ps1 -Src apps\explorer\ExplorerServer.codex `
    -Out build-output\explorer-server.cdx -Log build-output\explorer-server.log
```

`-Log` is mandatory; without it the compile hangs on a parameter prompt.

**Step 6 -- route and run.** Add your page to the `$pageFiles` and
`$pageSrc` maps at the top of `apps/explorer/run-designers-demo.ps1`,
then:

```powershell
pwsh apps\explorer\run-designers-demo.ps1        # -HttpPort 8888 by default
```

It boots `explorer-server.cdx` in codex-vm with `explorer.db.img`
attached, waits for the guest to dial home on TCP 9100 (framed protocol,
hardcoded in `WebServer`), fronts it with an `HttpListener` on 8888, and
proxies `/api/*` to the guest. Today it serves `/` and `/setting`,
`/character`, `/item`, `/excalibur`, `/mine`.

---

## Chapter 5: The CSS Contract

When the plug mounts a themed widget tree it applies the theme two ways:
`theme-to-css` writes a stylesheet keyed by class, and `_wkStyle` also
sets the same properties inline on each element as it is created.

| Class | Widget | Source |
|-------|--------|--------|
| `.wk-panel` | `WkPanel` | `th-panel` StateStyles |
| `.wk-btn` | `WkButton` | `th-button` StateStyles |
| `.wk-label` | `WkLabel` | `th-label` StateStyles |
| `.wk-input` | `WkInput` | `th-input` StateStyles |
| `.wk-gauge` | `WkGauge` | `th-gauge` StateStyles |
| `.wk-sep` | `WkSeparator` | `th-separator` StateStyles |
| `.wk-row` | Row layout | `display:flex; flex-direction:row` |
| `.wk-col` | Column layout | `display:flex; flex-direction:column` |
| `.wk-dialog` | `show-*` dialogs | fixed in the runtime |

Each StateStyles emits five rules: the base selector plus `:hover`,
`:active`, `:disabled`, `:focus`.

`WkCustom "box:card"` becomes `<div class="card">` -- so an app's own CSS
text (the `<name>-css` chapter constant, injected by
`inject-app-style theme css`) styles its own class names, and the `wk-*`
classes only cover the six themed widget families. That is the division:
**theme records own the widget chrome, the app's CSS string owns the
page layout.**

`apps/games/style.css` is independent of all of this -- it is the
hand-written stylesheet for the games portal and the classic game pages
(Path C), which never touch the widget system.

---

## Chapter 6: Image Generation Pipeline

The explorer designer pages are built around Stable Diffusion prompt
construction:

1. The Codex chapter defines the prompt template and the dimension axes
   (race x class x gender x personality x portrait, or item x material x
   rarity x ...). The dimensions come from the DB over `/api/d/<table>`.
2. `build-<kind>-prompt` composes the positive prompt from the selected
   rows' prompt fragments; `<kind>-negative` is a constant.
3. `gen-body prompt negative seed width height` (in `ExplorerTheme`)
   builds the JSON body, `add-history-card` inserts a spinner card, and
   `generate-image "/api/generate" body card-id` POSTs it and fills the
   card when the image comes back.
4. `check-sd-status` pings `/api/config` and flips a status dot.

**The back half of this is not wired in-repo.** `/api/generate` and
`/api/config` have no implementation in the depot today:
`ExplorerServer.codex` serves the DB tables, the creations API
(`/api/save`, `/api/mine`, `/api/delete`, `/api/remix`, `/api/export`,
`/api/export-workflow`) and auth -- not generation.
`apps/explorer/server.ps1` *defines* `Invoke-SdGenerate` and
`Feed-SdConfig` against a local SD WebUI on port 7860, but never
dispatches to them, and it serves its pages from a directory outside the
depot. Chapter 7 tracks both. The prompt-building half -- the part that
is Codex -- works and is testable without SD: the prompt string is
visible in the page.

---

## Chapter 7: Missing Rooms (Known Gaps)

Verified against the tree. We do not walk back a capability we want; a
gap that is still open stays on this list until it is closed.

### Foreword capability vs. web emission

| Module | Foreword | Web emission | Verdict |
|--------|----------|--------------|---------|
| Widget tree to DOM | `Widget.codex` | `mount-widget-themed` / `mount-widget-into` build real `div`/`span`/`button`/`input`/`progress`/`hr` | **CLOSED** |
| Dialog | `Dialog.codex` (DialogConfig/Result) | `show-alert/confirm/prompt`, `close-dialog` emit `<dialog>` + `showModal()` | **CLOSED** -- the foreword's `DialogConfig` record is still not the input type; the runtime takes plain text |
| Event routing | `Event.codex` (HandlerTable, EventPath) | Plug has its own two-callback registry (`register-handlers`, `register-input-handler`, `dom-on-*`) | **OPEN** -- the foreword event model is not what gets emitted. Bind `HandlerTable`/`EventPath` to `addEventListener` |
| Binding | `Binding.codex` (Observable, BindingTable) | `set-render` / `request-render` / `state-set-render` rAF dirty-loop | **OPEN** -- a render loop exists, but `Observable`/`BindingTable` are not lowered |
| Accessibility | `Accessibility.codex` (Role/Label/LiveRegion) | `dom-set-aria`, `dom-set-role` builtins | **OPEN** -- nothing is emitted automatically from the widget tree; a11y is opt-in, per call |
| Animation | `Animation.codex` (Throbber/Transition/KeyframeSeq) | `css-animate-spin/pulse/bounce`, `css-transition`, three `@keyframes` | **OPEN** -- fixed set only; `KeyframeSeq` does not lower to `@keyframes` |
| Overlay | `Overlay.codex` (Tooltip/Popup/ContextMenu/Modal) | none (grep: no tooltip/popup/context-menu in `HtmlEmitter`) | **OPEN** -- modal is reachable only via the dialog builtins |
| Scroll | `Scroll.codex` (ScrollState) | none | **OPEN** -- no `overflow` CSS; apps hand-write it in their CSS string |
| Charts | `Charts.codex` (Bar/Line/Pie/Area) | none | **OPEN** -- no SVG and no canvas emission at all |
| Vector | `Vector.codex` (paths) | none | **OPEN** -- no `<svg>` emission |
| Shadow / Gradient / AccentBorder | `Theme.codex` `WidgetStyle` fields | none | **OPEN (new)** -- `theme-to-css` and `_wkStyle` lower bg, fg, padding, margin, border, radius, min-w/h only |
| Grid / Split layouts | `Layout.codex` | none | **OPEN (new)** -- only `.wk-row` / `.wk-col` are emitted; grid and split compute rects for the framebuffer backend only |

### Explorer

| Item | Verdict |
|------|---------|
| Character explorer | **CLOSED** -- `apps/explorer/CharDesignerApp.codex`, compiled, routed at `/character` by `run-designers-demo.ps1` |
| Setting explorer | **CLOSED** -- `apps/explorer/SettingDesignerApp.codex`, routed at `/` and `/setting` |
| Item explorer | **CLOSED** -- `apps/explorer/ItemDesignerApp.codex`, routed at `/item` |
| Card explorer | **OPEN (new)** -- `CardDesignerApp.codex` + `CardEmitter.codex` exist and compile, but no server routes them: `run-designers-demo.ps1`'s page map has no `card` key |
| `/api/generate`, `/api/config` | **OPEN (new)** -- every designer page calls them; no server in the depot answers them (see Chapter 6) |
| `apps/explorer/server.ps1` | **OPEN (new)** -- serves pages from `D:\Projects\CodexMagic\explorer\pages`, a path outside the depot, so it cannot work from a fresh sync. `run-designers-demo.ps1` is the working server; fold the SD generation code into it and retire the out-of-repo path |
| `build/build-explorer-pages.ps1` | **OPEN (new)** -- stale. It runs `build-output\{carddesigner,characterdesigner,settingdesigner,voicestudio}.cdx`, which nothing produces, and writes outside the depot. Delete it or rewrite it over `codex\plugs\html\run.ps1` |
| "Save to My Creations" bar | **OPEN (new)** -- injected as hand-written JS by `run-designers-demo.ps1` (`$inject`) into the three designer pages. It should be an `AuthClient` widget in the page chapters, the way `CreationsApp` already does it |
| VoiceStudio, WorkflowExporter, StoryGraph, WorldForge, NameForge | Chapters exist and compile; no page is routed by `run-designers-demo.ps1`. **OPEN** |

### Games portal

| Item | Verdict |
|------|---------|
| Portal chrome (`apps/games/app.js`, `index.html`, `style.css`) | **OPEN** -- hand-written; not compiled from Codex. The 34 classic game pages are hand-written too (their engines are not) |
| CodexMagic web (`magic.js`, `card-render.js`) | **OPEN** -- the `*Page.codex` chapters are compiled, but the pages are post-processed to inject two hand-written JS files |

Design references: `docs/Reference/UICapabilityMap.md`
(cross-platform coverage matrix), `apps/webapp/design/Done/BaseTemplate.md`
(why the WebApp quire exists).

---

## Appendix A: File Map

```
codex/plugs/html/            The compiled path
  HtmlPlug.codex             Entry: read IR from serial, emit HTML to serial
  HtmlEmitter.codex          IR walker; JS/CSS/HTML generation; is-html-builtin
  build.ps1                  Bundles the plug + deps, compiles html-plug.cdx
  run.ps1                    Source -> IR-CCE -> plug CDX in codex-vm -> HTML
  runtime-extra.js           Extra runtime snippet
  codex.project.json         Plug metadata
  build-output/html-plug.cdx The plug binary (~195 KB)

apps/webapp/                 The browser runtime contract (quire: WebApp)
  WebRuntime.codex           Typed stubs for every JS-bridge builtin
  WebTheme.codex             Theme builders: bsn bn ez bdr eu exy ws ss-flat
  WebWidgets.codex           wk-attach, widget-box, widget-box-click, id-num
  README.md

build/
  build-apps.ps1             Builds all 74 apps/<x>/web/<x>.html from Page chapters
  bundle-app.ps1             Inlines transitive cites into one .codex
  compile.ps1                Source -> CDX or IR (-IrCce). -Log is mandatory
  build-magic-pages.ps1      CodexMagic *Page.codex -> apps/games/codexmagic/web/*.html
  quire-map.ps1              Quire name -> directory (UI, WebApp, Explorer, Games, ...)
  build-explorer-pages.ps1   STALE -- see Chapter 7

codex/foreword/ui/           50 modules, quire UI (the source of truth)
  Widget.codex               WidgetNode, WidgetKind, constructors, queries
  Theme.codex                Palette, Edges, Border, CornerStyle, WidgetStyle,
                             StateStyles, Theme; theme-terminal/lcars/minimal
  BoxModel.codex             LayoutRect and box decomposition
  Layout.codex               Flex, Grid, Split, Stack
  Event.codex Focus.codex Scroll.codex Overlay.codex Dialog.codex
  Binding.codex Accessibility.codex Animation.codex Selection.codex
  Cursor.codex Drag.codex Touch.codex Clipboard.codex Sound.codex
  TextField.codex Editor.codex RichText.codex Markdown.codex Validation.codex
  Dropdown.codex DataTable.codex TreeView.codex FilterableList.codex
  SearchBar.codex SettingsPanel.codex DetailPane.codex StatusBadge.codex
  CommandPalette.codex Charts.codex Vector.codex Canvas.codex Icon.codex
  Font.codex FontAtlas.codex TrueTypeFont.codex GlyphRasterizer.codex
  Render.codex GpuRender.codex Surface.codex Window.codex Orchestrator.codex
  AppRunner.codex InputSource.codex

apps/explorer/               Compiled pages + CDX server (pure Codex)
  ExplorerData.codex         All content: items, materials, rarities, races, ...
  ExplorerTheme.codex        dark-gold theme, layout CSS, shared page kit
  ExplorerStore.codex        Paged binary store (4 KB pages, CCE-native)
  ExplorerDb.codex           Relational schema (Data quire)
  ExplorerServer.codex       Bare-metal HTTP/JSON server (CDX)
  ItemDesignerApp.codex CharDesignerApp.codex SettingDesignerApp.codex
  CardDesignerApp.codex CardEmitter.codex CreationsApp.codex AuthClient.codex
  ExcaliburSlice.codex WorldModel.codex Emitters.codex NameForge.codex
  StoryGraph.codex WorldForge.codex VoiceStudio.codex WorkflowExporter.codex
  build-explorer-db.ps1      ExplorerData -> build-output/explorer.db.img
  run-designers-demo.ps1     Boots the CDX + HTTP bridge on :8888  <- use this
  server.ps1                 SD-oriented server; out-of-repo pages dir (Ch. 7)
  README.md

apps/games/                  Hand-built portal + Codex engines
  index.html style.css app.js games.json   Portal chrome (hand-written)
  server.ps1                 HTTP server; boots GameServer.cdx; proxies /api/*
  GameServer.codex           The engine (CDX)
  classic/                   36 Codex chapters: the game engines + Minimax/Rng
  classic/web/               34 HTML pages + game-common.css
  codexmagic/                CodexMagic engine, economy, clans, server
  codexmagic/web/            Compiled *Page pages + magic.js, card-render.js,
                             magic.css, build-pages.ps1, server.ps1
  magic/                     Magic engine sources (no web assets)

docs/
  Reference/UICapabilityMap.md       Cross-platform coverage matrix
  apps/webapp/design/Done/BaseTemplate.md   Why the WebApp quire exists
```

---

## Appendix B: Quick Reference

### Build the HTML plug
```powershell
pwsh codex\plugs\html\build.ps1
```

### Build every app page (74 of them)
```powershell
pwsh build\build-apps.ps1
pwsh build\build-apps.ps1 -Only notes,mail
```

### Compile one bundled source to HTML
```powershell
pwsh build\bundle-app.ps1 -Src apps\notes\NotesPage.codex -Out build-output\notes.codex
pwsh codex\plugs\html\run.ps1 -Src build-output\notes.codex -Out build-output\notes.html
```

### Run the explorer designers (compiled pages + CDX server)
```powershell
pwsh apps\explorer\build-explorer-db.ps1
pwsh build\compile.ps1 -Src apps\explorer\ExplorerServer.codex `
    -Out build-output\explorer-server.cdx -Log build-output\explorer-server.log
pwsh codex\plugs\html\run.ps1 -Src apps\explorer\CharDesignerApp.codex `
    -Out build-output\character.html
pwsh apps\explorer\run-designers-demo.ps1
# http://localhost:8888/character
```

### Run the games portal
```powershell
pwsh apps\games\server.ps1 -Port 8080
# http://localhost:8080/  -- Status and Games tabs; /games/<id> per game
```

### Build the CodexMagic pages
```powershell
pwsh build\build-magic-pages.ps1
```
