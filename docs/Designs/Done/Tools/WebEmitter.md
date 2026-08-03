# Web Emitter -- UI-to-Platform Rendering Layer

**Status:** Partially shipped (2026-05-24). UI-to-platform emitters are live -- HtmlEmitter (theme_to_css / render_widget_html), MauiEmitter, WinFormsEmitter. Pending: broader widget/feature coverage.

## The Problem

Codex has a 28-module UI foreword (`codex.foreword.ui`) with a complete
widget tree, flex layout engine, box model, theme system, event routing,
compositor, and window manager. Today this renders to a bare-metal
framebuffer (integer ARGB pixels via `Framebuf`). That's the only
backend.

We want to write applications in Codex using these same UI primitives
and emit them as web pages (HTML/CSS/JS), desktop apps (WPF, WinForms),
or framework-specific output (React, SwiftUI). The UI code should be
target-agnostic -- write once, emit to any platform.

## Architecture

```
Codex source (.codex)
    │
    ▼
Compiler frontend (lex → parse → ... → lower → resolve → lift)
    │
    ▼
IR text (S-expression) ─── existing plug protocol
    │
    ├──► html-plug.cdx    → index.html + style.css + app.js
    ├──► react-plug.cdx   → App.jsx + components/*.jsx
    ├──► wpf-plug.cdx     → MainWindow.xaml + *.xaml.cs
    ├──► winforms-plug.cdx → Form1.cs + Program.cs
    └──► swiftui-plug.cdx → ContentView.swift
```

Each plug is a standalone CDX binary (like the existing transpiler
plugs). It reads IR text on stdin, walks the IR tree, and emits
platform-specific output on stdout. No new compiler changes needed --
the existing `IR-CCE` compile mode + plug protocol is sufficient.

## UI IR Contract

The plug receives the full IR including type definitions. It recognizes
UI types by name -- these are the foreword types the plug knows how to
render:

### Widget Tree → DOM

| Codex UI Type | HTML Equivalent |
|---|---|
| `WidgetNode` with `WkPanel` | `<div>` |
| `WidgetNode` with `WkLabel text` | `<span>` or `<p>` |
| `WidgetNode` with `WkButton text` | `<button>` |
| `WidgetNode` with `WkInput text cursor` | `<input>` or `<textarea>` |
| `WidgetNode` with `WkGauge val max` | `<progress>` or custom |
| `WidgetNode` with `WkSeparator` | `<hr>` |
| `WidgetNode` with `WkCustom tag` | `<div data-custom="tag">` |

### Box Model → CSS

| Codex UI | CSS Property |
|---|---|
| `LayoutRect { lr-x, lr-y, lr-w, lr-h }` | `position`, `width`, `height` |
| `Edges { edge-top, edge-right, edge-bottom, edge-left }` | `margin` / `padding` |
| `Border { bdr-top, bdr-right, bdr-bottom, bdr-left }` | `border-width`, `border-color` |
| `CornerStyle` | `border-radius` |
| `LayoutDir DirRow` | `display: flex; flex-direction: row` |
| `LayoutDir DirColumn` | `display: flex; flex-direction: column` |
| `li-flex` | `flex: N` |
| `li-min-w`, `li-min-h` | `min-width`, `min-height` |

### Theme → CSS Variables

```css
:root {
  --pal-bg: #1a1a2e;
  --pal-fg: #e0e0e0;
  --pal-primary: #0f3460;
  --pal-secondary: #16213e;
  --pal-accent: #e94560;
  --pal-border: #333355;
}

.widget-button         { /* ss-normal styles */ }
.widget-button:hover   { /* ss-hover styles */ }
.widget-button:active  { /* ss-pressed styles */ }
.widget-button:focus   { /* ss-focused styles */ }
.widget-button:disabled { /* ss-disabled styles */ }
```

The `Theme` record maps directly to CSS custom properties. Each
`StateStyles` maps to CSS pseudo-classes. The plug emits a complete
CSS theme from a single `Theme` record.

### Layout → CSS Flexbox

The Codex flex layout engine (`flex-layout`, `flex-row`, `flex-col`)
maps directly to CSS flexbox:

```css
.panel { display: flex; gap: 8px; }
.panel.dir-row { flex-direction: row; }
.panel.dir-column { flex-direction: column; }
.widget[data-flex="0"] { flex: none; }
.widget[data-flex="1"] { flex: 1; }
```

`GridLayout` maps to CSS Grid. `SplitLayout` maps to a two-pane flex
with a fixed ratio. `stack-layout` maps to `position: absolute` within
a relative container.

### Events → DOM Events

| Codex Event | DOM Event |
|---|---|
| `EvKeyDown code mods` | `keydown` |
| `EvKeyUp code mods` | `keyup` |
| `EvMouseMove x y` | `mousemove` |
| `EvMouseDown btn x y` | `mousedown` |
| `EvMouseUp btn x y` | `mouseup` |
| `EvScroll dx dy btn` | `wheel` |
| `EvFocus id` | `focus` |
| `EvBlur id` | `blur` |
| `EvResize w h` | `resize` |
| `EvTimer tag` | `setTimeout` / `setInterval` |

The `HandlerTable` with event path capture/bubble maps to DOM event
propagation (capture phase + bubble phase are native to the DOM).

### Window Manager → Multi-page or Dialog

| Codex UI | HTML |
|---|---|
| `Window` | `<dialog>` or separate page |
| `wm-open` | `dialog.showModal()` or router navigation |
| `wm-close` | `dialog.close()` |
| `wm-focus` | `dialog.focus()` or `z-index` adjustment |
| `wm-tile` | CSS Grid tile layout |

### Specialized Components

| Codex UI | HTML |
|---|---|
| `TextFieldState` | `<input>` / `<textarea>` with cursor/selection |
| `DialogConfig` | `<dialog>` with OK/Cancel/Input |
| `Overlay` (Tooltip) | `<div role="tooltip">` with positioning |
| `Overlay` (ContextMenu) | `<menu>` with `popover` |
| `Overlay` (Modal) | `<dialog>` modal |
| `ScrollState` | `overflow: auto` with custom scrollbar CSS |
| `Observable` / `Binding` | Reactive JS (MutationObserver or custom) |
| `Throbber` | CSS `@keyframes` animation |
| `Transition` | CSS `transition` property |

## Plug Implementation

### Phase 1: Static HTML/CSS (html-plug)

The simplest emitter. Walks the IR, finds `WidgetNode` construction
sites, and emits static HTML + CSS. No JavaScript. Handles:

- Widget tree → DOM tree
- Theme → CSS custom properties + component classes
- Layout → flexbox/grid CSS
- Box model → margin/padding/border CSS

Output: `index.html` + `style.css`

### Phase 2: Interactive (html-plug + JS)

Add JavaScript for:

- Event handler wiring (onclick, onkeydown, etc.)
- TextField input handling
- Dialog open/close
- Overlay positioning
- Scroll state
- Animation (CSS keyframes from Throbber/Transition)
- Observable bindings (reactive updates)

Output: `index.html` + `style.css` + `app.js`

### Phase 3: React Plug (react-plug)

Separate plug that emits JSX components:

- Each `WidgetNode` → React functional component
- `Observable`/`Binding` → `useState` + `useEffect`
- `HandlerTable` → React event props
- Theme → CSS modules or styled-components
- Window → React Router routes or modal components

### Phase 4: Platform Plugs

Each is an independent plug CDX:

- **wpf-plug**: XAML + C# code-behind. WidgetNode → WPF controls,
  Theme → ResourceDictionary, Layout → Grid/StackPanel/DockPanel.
- **winforms-plug**: C# with programmatic UI construction.
- **swiftui-plug**: Swift with SwiftUI views. WidgetNode → VStack/HStack,
  Theme → ViewModifier.

## The UI Intermediate Layer

The key insight: the Codex UI foreword already IS the intermediate
layer. `WidgetNode`, `Theme`, `LayoutDir`, `BoxModel`, `Event` -- these
are platform-agnostic descriptions of UI. The framebuffer renderer
(`render-tree`) is just one backend. Each plug is another backend that
reads the same IR types and emits platform-specific output.

No new IR types are needed. The plug recognizes foreword UI types by
name in the IR text stream and translates them. A program that builds a
`WidgetNode` tree, applies a `Theme`, and handles `Event`s can be
emitted as HTML, React, WPF, or bare-metal framebuffer -- same source,
different plug.

## File Structure

```
codex/plugs/
  html/
    build.ps1           Build html-plug.cdx from .codex source
    run.ps1             Codex source → index.html + style.css + app.js
    HtmlEmitter.codex   IR walker → HTML generation
    CssEmitter.codex    Theme/BoxModel → CSS generation
    JsEmitter.codex     Event/Binding → JavaScript generation
    codex.project.json
  react/
    build.ps1
    run.ps1
    ReactEmitter.codex  IR walker → JSX components
    codex.project.json
```

## Open Questions

1. **Server-side rendering**: Should the HTML plug emit static HTML
   (SSR-friendly) or client-rendered SPA? Answer: static HTML first,
   SPA as Phase 2.

2. **CSS strategy**: Utility classes (Tailwind-like), BEM, CSS modules,
   or inline styles? Answer: semantic classes with CSS custom properties
   for theming -- matches the Theme record structure.

3. **Routing**: How do multi-window apps map to web navigation?
   Answer: each Window → `<dialog>` for modals, or hash-routing for
   page-level windows.

4. **Asset pipeline**: Images, fonts, icons from Codex `Icon` and
   `Font` modules. Answer: emit as data URIs or reference external
   files. Icon module already has bitmap data.

5. **Accessibility**: The `Accessibility` module in the UI foreword
   should emit ARIA attributes. The plug reads `Accessibility` records
   from IR and adds `role`, `aria-label`, `aria-live`, etc.
