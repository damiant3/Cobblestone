# Building a Web App with Codex

The Explorer is a reference implementation for building data-driven
web applications on the Codex platform. This document describes the
architecture and how the pieces fit together.

## Architecture

```
                    ┌─────────────────────────────────────────┐
                    │           Browser                       │
                    │  HTML page (from plug pipeline)         │
                    │  Theme CSS (from inject-theme-css)      │
                    │  App logic (from Codex → IR → JS/HTML)  │
                    └──────────────┬──────────────────────────┘
                                   │ HTTP
                    ┌──────────────▼──────────────────────────┐
                    │        server.ps1 (HTTP bridge)         │
                    │  Accepts browser requests on port 8888  │
                    │  Proxies to CDX server over serial      │
                    │  Proxies SD API calls to port 7860      │
                    │  Serves static HTML/JS from pages dir   │
                    └──────────────┬──────────────────────────┘
                                   │ serial (file-mapped I/O)
                    ┌──────────────▼──────────────────────────┐
                    │     ExplorerServer.codex (CDX binary)   │
                    │  Boots bare-metal in codex-vm            │
                    │  Initializes Catalog with 7 tables      │
                    │  Serves JSON API: /api/items, etc.      │
                    │  Reads/writes IDE disk for persistence  │
                    └──────────────┬──────────────────────────┘
                                   │ cites
            ┌──────────────────────┼──────────────────────┐
            ▼                      ▼                      ▼
    ExplorerDb.codex      ExplorerData.codex     ExplorerTheme.codex
    (DB schema,           (content data,          (theme, DOM stubs,
     table defs,           items, races,           CSS components,
     bootstrap,            biomes, etc.)           shared nav/UI)
     row builders)
```

## File Inventory

### Shared Chapters (cited by pages and server)

| File | Purpose |
|------|---------|
| `ExplorerTheme.codex` | Theme types (Palette, WidgetStyle, Border, Edges), dark-gold theme, DOM stub declarations, CSS component functions (css-nav, css-dim-picker, css-hero, etc.), shared UI builders (explorer-nav, dim-pill, build-dropdown-items), shared callbacks (random-seed-cb, close-lb-cb, add-history-card) |
| `ExplorerData.codex` | All content data — item categories, materials, rarity tiers, conditions, enchantments, alignments, sizes, colors, modifier options, character races/classes/genders/personalities/portraits, setting biomes/times/weathers/moods/scales. Type definitions. Name extraction helpers. Negative prompt defaults. |
| `ExplorerDb.codex` | Database schema using the Codex data quire (cites `Data chapter Schema`, `Row`, `Catalog`). 7 table definitions (items, materials, rarities, prefs, history, sd_config, themes). Bootstrap function creates all tables in a Catalog. Seed row builders for populating defaults. |

### Page Files (compiled through the HTML plug)

| File | Lines | What it defines |
|------|-------|-----------------|
| `ItemDesignerApp.codex` | 210 | Item prompt builder, 8-dimension mega-menu, modifier logic, item-specific controls |
| `CharDesignerApp.codex` | 158 | Character prompt builder, race grouping, 5-dimension controls |
| `SettingDesignerApp.codex` | 131 | Setting prompt builder, 5-dimension controls |

Each page cites `ExplorerTheme` and `ExplorerData`, defines only its
page-specific logic, and produces a self-contained HTML page via the
HTML plug.

### Server

| File | Purpose |
|------|---------|
| `ExplorerServer.codex` | Bare-metal CDX server. Boots in codex-vm, initializes DB from ExplorerDb schema, seeds data from ExplorerData, serves JSON API over serial. |
| `server.ps1` | PowerShell HTTP bridge. Accepts browser requests, proxies to CDX server, serves static pages, proxies SD API. |

### Legacy (from original CL 2422, pre-plug-pipeline)

| File | Status |
|------|--------|
| `CardDesigner.codex` | Old inline-JS approach, superseded by CardDesignerApp |
| `ItemDesigner.codex` | Old inline-JS approach, superseded by ItemDesignerApp |
| `CharacterDesigner.codex` | Old inline-JS approach, superseded by CharDesignerApp |
| `SettingDesigner.codex` | Old inline-JS approach, superseded by SettingDesignerApp |
| `VoiceStudio.codex` | Placeholder, TTS backend not connected |
| `WorkflowExporter.codex` | ComfyUI workflow builder |
| `CharMini.codex` | Minimal test file |

## How to Build a Page

### 1. Define your data in ExplorerData

Add records and lists for your content. Use existing types where
possible (`NamedPrompt` pattern: `{name : Text, prompt : Text}`).
Add a name extraction helper for your type.

### 2. Create a page .codex file

```codex
Chapter: MyPage
  cites Explorer chapter ExplorerTheme
  cites Explorer chapter ExplorerData

 My page description.

 We say:

Section: Prompt Builder
  build-my-prompt : ... -> Text
  build-my-prompt (...) = ...

Section: Controls
  render-controls : ... -> Text
  render-controls (...) = ...

Section: Page
  build-page : ... -> Text
  build-page (...) =
    explorer-nav "my-page"
    & "<div class=\"page\">..."
    & "</div>"

Section: Callbacks
  pick : Text, Integer -> Integer
  pick (group) (idx) = ...

Section: Entry
  opening : Integer -> [Console] Nothing
  opening (x) = act
    let st = init-state 0
    in let ex = init-explorer 0
    in let app = dom-get "app"
    in let a2 = dom-set-html app (build-page ...)
    in print-line-uni ""
  end
```

### 3. Build through the HTML plug

```powershell
# Build the HTML plug CDX (once)
codex/plugs/html/build.ps1

# Build your page
codex/plugs/html/run.ps1 -Src apps/explorer/MyPage.codex -Out pages/my-page.html
```

The plug pipeline:
1. `compile.ps1 -IrCce` compiles your .codex to IR (includes
   foreword resolution for all cited chapters)
2. `run.ps1` converts CCE IR to UTF-8 (avoids byte-4 EOT collision)
3. Plug CDX parses the IR S-expressions, emits HTML + CSS + JS
4. Output is a self-contained .html file

### 4. Add a route in server.ps1

```powershell
elseif ($path -eq '/my-page') {
    Send-StaticFile -Response $resp -FilePath (Join-Path $PagesDir 'my-page.html')
}
```

### 5. Add an API endpoint in ExplorerServer.codex

If your page needs dynamic data, add a handler:

```codex
handle-my-data : ServerState -> [Console] ServerState
handle-my-data (state) =
  let data = my-data-list
  in act
    r <- respond-json (my-data-to-json data)
    state
  end
```

## Key Design Decisions

### CSS from Theme, not strings

Colors come from `inject-theme-css dark-gold` which generates CSS
custom properties (`:root { --primary: #ffd700; --bg: #0a0a0a; ... }`)
from the Codex `Theme` record at runtime. Layout CSS references these
variables (`var(--primary)`, `var(--border)`) — never hardcoded hex.
Changing the theme changes every page.

### Data in shared chapters, not per-page

All content data lives in `ExplorerData.codex`. Pages cite it and use
the data. Adding an item type or rarity tier in ExplorerData makes it
available on every page. No duplication.

### DOM stubs for browser builtins

The Codex compiler needs type signatures for browser functions
(`dom-get`, `dom-create`, `state-get`, etc.). These are declared as
stub functions in `ExplorerTheme.codex` with trivial bodies. The HTML
plug's JavaScript runtime provides the real implementations. The
`is-html-builtin` filter in the plug emitter strips the stubs from
the output so the runtime versions take precedence.

### CCE-to-UTF8 IR conversion

The Codex IR emitter produces CCE-encoded output. CCE maps digit '1'
to byte value 4, which collides with the EOT sentinel. The plug
run script converts CCE bytes to UTF-8 on the host side before feeding
to the plug CDX, eliminating the collision. The plug reads via
`read-file-uni` which handles the encoding.

### Server CDX with data quire

The ExplorerServer boots bare-metal, initializes a Catalog from the
data quire, and serves JSON over serial. The PowerShell bridge
translates HTTP to serial. This means the data layer runs in Codex
on Codex hardware — no external database, no FFI, fully owned stack.
