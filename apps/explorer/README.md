# Codex Explorer

Data-driven web applications on the Codex platform. Users own
their theme and layout — we provide the widgets, they arrange them.

## Architecture

```
Codex source (.codex)
    |
    v
Compiler (IR-CCE mode)
    |
    v
HTML plug CDX (codex/plugs/html/)
    |
    v
Self-contained HTML page
    |  - Theme CSS from inject-theme-css
    |  - Widget tree from mount-widget-themed
    |  - AJAX via fetch-then / fetch-get-then
    |  - Events via dom-on-click / dom-on-input / dom-on-key
    |  - Reactive rendering via set-render / state-set-render
    |  - Dialogs via show-alert / show-confirm / show-prompt
    |  - Animation via css-animate-spin / pulse / bounce
    |  - Accessibility via dom-set-role / dom-set-aria
    v
Browser
```

## File Inventory

### Shared Chapters

| File | Purpose |
|------|---------|
| `ExplorerTheme.codex` | Theme types (inlined from foreword UI), dark-gold theme, DOM stubs, CSS components, shared nav/dropdown builders |
| `ExplorerData.codex` | All content data — items, materials, rarities, races, classes, biomes, etc. |
| `ExplorerDb.codex` | Database schema (7 tables), bootstrap, seed rows |

### Page Files (compiled through the HTML plug)

| File | Purpose |
|------|---------|
| `ItemDesignerApp.codex` | Item prompt builder, 8-dimension mega-menu |
| `CharDesignerApp.codex` | Character prompt builder, 5-dimension controls |
| `SettingDesignerApp.codex` | Setting prompt builder, 5-dimension controls |
| `CardDesignerPage.codex` | Card dimension explorer |
| `WidgetDemo.codex` | End-to-end demo of the widget stack |

### Server

| File | Purpose |
|------|---------|
| `ExplorerServer.codex` | Bare-metal CDX server, JSON API over serial |
| `server.ps1` | PowerShell HTTP bridge, SD API proxy |

## How to Build a Page

### 1. Create your .codex file

```codex
Chapter: MyPage
  cites Explorer chapter ExplorerTheme
  cites Explorer chapter ExplorerData
```

Cite `ExplorerTheme` for the theme, DOM stubs, widget constructors,
and CSS components. Cite `ExplorerData` for content data.

### 2. Build through the HTML plug

```powershell
# Build the plug CDX (once)
pwsh codex/plugs/html/build.ps1

# Build your page
pwsh codex/plugs/html/run.ps1 -Src apps/explorer/MyPage.codex -Out pages/my-page.html
```

### 3. Use the widget stack

```codex
  opening : [Console] Nothing = act
    let theme = inject-theme-css dark-gold
    in let s = set-render (my-render)
    in let r = my-render 0
    in print-line ""
  end

  my-render : Integer -> Integer
  my-render (x) =
    let tree = widget-panel "root" DirColumn 8 [
      widget-label "title" "My App",
      widget-button "go" "Click Me"
    ]
    in let m = mount-widget-themed dark-gold tree
    in let btn = dom-get "go"
    in let b2 = dom-on-click btn (on-go)
    in 0

  on-go : Text -> Integer
  on-go (id) = show-alert "Hi" "Button clicked!" (done)

  done : Text -> Integer
  done (r) = 0
```

## Available Builtins

### DOM
`dom-get`, `dom-create`, `dom-set-attr`, `dom-set-text`,
`dom-set-html`, `dom-get-value`, `dom-set-value`, `dom-append`,
`dom-prepend`, `dom-remove`, `dom-add-class`, `dom-remove-class`,
`dom-set-style`, `dom-on`, `dom-query`, `dom-body`

### Widget Rendering
`mount-widget`, `mount-widget-themed`, `widget-panel`,
`widget-label`, `widget-button`, `widget-input`, `widget-gauge`,
`widget-separator`

### Events
`dom-on-click` (callback receives element ID),
`dom-on-input` (callback receives current value),
`dom-on-key` (callback receives key name)

### AJAX
`fetch-json` (returns Promise), `fetch-then` (callback-based),
`fetch-get-then`, `json-stringify`, `json-parse`

### State + Reactive Rendering
`state-get`, `state-set`, `state-get-text`, `state-set-text`,
`set-render` (register render function),
`state-set-render` (set + trigger render),
`state-set-text-render`, `request-render`

### Dialogs
`show-alert` (title, message, callback),
`show-confirm` (title, message, callback — result "ok" or "cancel"),
`show-prompt` (title, message, placeholder, callback — result is input text or ""),
`close-dialog`

### Animation
`css-animate-spin` (element, period-ms),
`css-animate-pulse`, `css-animate-bounce`,
`css-transition` (element, property, duration-ms)

### Accessibility
`dom-set-role` (element, role string),
`dom-set-aria` (element, attribute name, value)

### Theme
`inject-theme-css` (Theme record — generates CSS custom properties),
`theme-to-css` (Theme → CSS text), `int-to-css-color` (Integer → hex)

### Other
`random-int`, `set-timeout`, `local-storage-get`, `local-storage-set`,
`next-card-id`, `generate-image`, `check-sd-status`
