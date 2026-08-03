# GitHub Update 8 -- CL 976 to CL 985 (2026-05-05)

Previous update: CL 803+ (GitHubUpdate7).
This update: CL 985.

## UI Foreword Quire (codex.foreword.ui/) -- 18 chapters

New library quire for themeable GUI primitives. The design separates
structure from presentation: widgets describe intent ("I am a button,
I am pressed"), themes describe appearance ("a pressed button has these
colors, this shape, this border"). Swap one Theme record and the entire
interface transforms -- LCARS, cockpit dashboard, terminal, anything.

### Foundation (CLs 976-979)

- **Theme** -- Palette, BorderSide, Border, CornerStyle (Sharp/Round/Bevel),
  WidgetStyle, StateStyles (normal/hover/pressed/disabled/focused), Theme.
  Three built-in themes: `theme-terminal`, `theme-lcars`, `theme-minimal`.
- **BoxModel** -- CSS-style LayoutRect, Edges. Margin/border/padding/content
  decomposition. Hit test, intersection, union, inset.
- **Layout** -- Flex row/column engine with gap. Fixed items get min-size,
  flex items split remaining space proportionally.
- **Widget** -- WidgetKind variant (Panel/Label/Button/Gauge/Separator/Input/Custom).
  Semantic tree with children, state, flex. Zero visual opinion.
- **Render** -- Walks laid-out widget tree, resolves Theme style per kind+state,
  draws to Framebuf via Rasterizer. Filled rects, borders, gauge fill,
  rounded rects.
- **Surface** -- Compositor. Z-ordered surfaces each owning a framebuffer.
  Composite back-to-front with per-surface alpha blending. Move, toggle
  visibility, z-reorder, hit test.

### Interaction (CL 977)

- **Event** -- EventKind variant: key, mouse, scroll, focus, resize, timer,
  network, custom. Handler table with widget-id + event-name lookup.
  Hit-test routing through widget tree. Event path building for
  capture/bubble phases.
- **Binding** -- Observable values (Integer + Text) with dirty tracking.
  Binding table maps sources to widget targets (label, gauge, visibility,
  state). Collect pending updates per frame, clean after apply.
- **Animation** -- Throbbers (spin/pulse/bounce/bar), property transitions
  with easing, keyframe sequences with looping. AnimSet ticks all entries
  in batch.
- **Icon** -- Multi-size bitmap icons: 8x8 (17 icons), 12x12 (6), 16x16 (10),
  24x24 (4), 32x32 (4 scaled). Nearest-neighbor scaler. Size-aware lookup
  with fallback. Standard set: close, check, arrows (4 dirs), warning,
  info, menu, lock, gear, plus, minus, search, home, star, power.
  `icon-set-all` merges all 41 icons.
- **Overlay** -- Stack of tooltips, popups, context menus, notifications,
  modals. Auto-dismiss timers. Modal input capture.
- **Sound** -- Waveform descriptors (sine/square/triangle/noise/silence),
  effect queue with priority and max size, sound sequences with per-step
  delay scheduling. Built-in effects: click, beep, error, success,
  warning, notify, tick, whoosh.
- **Orchestrator** -- `AppState` record + `app-tick`: polls timers, dispatches
  events (with mouse hit-test targeting and keyboard focus routing),
  ticks animations and overlays, applies bindings, re-layouts dirty
  widgets, advances frame counter. One call per frame.

### Phase 3 (CL 981)

- **Font** -- 5x7 bitmap font, 97 CCE-indexed glyphs (printable ASCII mapped
  to CCE positions). Text measurement + framebuffer rendering. Complements
  the kernel's 8x16 BitmapFont.codex for bare-metal use.
- **Cursor** -- 8 cursor styles (arrow/hand/beam/crosshair/resize-h/resize-v/
  move/wait/none) with bitmap icons and per-style hotspot offsets.
- **Scroll** -- Scrollable viewport. Clamped offsets, scrollbar thumb
  position/size geometry, page up/down, percentage queries, visibility test.
- **Focus** -- Tab-order focus ring. Next/prev/set/clear navigation.
  `focus-apply` propagates focused state to widget tree nodes.
- **Dialog** -- Builder functions for alert, confirm, prompt, and custom
  dialogs. Produces themed widget trees wrapped in modal overlays.

### Dedup (CL 979)

Consolidated duplicate helpers across UI files:
- `rnd-min`/`rnd-isqrt` in Render removed (uses `box-min`/`rast-isqrt`)
- `ico-abs` in Icon removed (uses `math-abs` from MathLib)
- `show-bool`/`orch-show-bool` in Surface/Orchestrator removed (uses `fmt-bool` from Theme)
- Added `math-abs`, `math-min`, `math-max`, `math-clamp` to `codex.foreword/MathLib.codex`

## UEFI Boot Path (CL 980, Nib)

- **efi-diag.c** -- Standalone C UEFI diagnostic shell compiled with MSVC.
  Commands: help, peek, poke, in, out, echo, reboot.
- **DiagnosticShell.codex** -- Codex-native diagnostic shell in kernel quire.
  CCE-aware hex parser, bare-metal builtins (peek/poke/port-in/port-out/
  get-ticks), REPL via Console effect.
- **make-efi.ps1** -- PE32+ builder. Parses CDX header, builds UEFI stub
  that AllocatePages at 0x100000, copies code+rodata, patches HLT to NOP,
  disables LAPIC, jumps to __start.
- **X86_64IO.codex** -- Trampoline now disables LAPIC via MSR 0x1B before
  long mode. Serial busy-wait loops have 10000-iteration timeout (prevents
  hang when hardware absent).

## Boot Gate (CL 985)

- **codex-boot.codex** -- New boot entry point. Polls `kb-has-key` 50,000
  times during startup. Key held = diagnostic shell. No key = VGA welcome
  screen with centered logo, four green [OK] status checks (compiler fixed
  point, trust lattice, verifier, scheduler), prompt, and footer hint.

## Numbers

- **Foreword count**: 176+ chapters across 14 quires (up from 103+)
- **UI quire**: 18 chapters, 41 icons across 5 sizes, 3 built-in themes
- **Kernel**: 14 modules (up from 3 -- added Keyboard, BitmapFont, VgaGraphics, DiagnosticShell, etc.)
- **Compiler**: ~21,000 lines of Codex across 51 files
- **Seed**: 1,743,984 bytes (CDX), hard fixed point proven
