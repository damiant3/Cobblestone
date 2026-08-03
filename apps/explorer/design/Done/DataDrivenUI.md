# Data-Driven UI Architecture

## Problem

The explorer pages hardcode everything: data as Codex list literals,
CSS as concatenated strings, HTML structure as string templates, menu
hierarchies as ad-hoc grouping functions. Every page duplicates the
nav, the dropdown CSS, the state management pattern. Adding an item
type means editing prompt strings. Adding a dimension means touching
the controls, the state init, the pick callback, the prompt builder,
and the CSS -- in every page.

This is the same mistake as writing x86 by hand instead of going
through IR. We need an intermediate representation for UI that the
plug renders to HTML/CSS/JS, the same way the compiler IR renders
to machine code.

## Architecture

Three layers:

```
Layer 1: Data (relational, in the data quire)
   Tables: item_types, materials, rarities, enchantments, ...
   Relationships: item_type_modifiers, modifier_options, ...
   Queried at compile time by the page .codex

Layer 2: UI IR (widget tree + theme, in codex.foreword.ui)
   WidgetNode tree built from data
   Theme applied for colors/borders/spacing
   Layout computed by flex/grid engine
   No HTML, no CSS, no JS -- pure structure + style

Layer 3: Plug renderers (target-specific)
   HTML plug: WidgetNode -> DOM elements + CSS + event wiring
   Framebuffer plug: WidgetNode -> pixel rendering (existing)
   Future: React plug, Flutter plug, native plug
```

### Layer 1: Data Tables

All content data lives in relational tables using the data quire's
`TableDef`/`ColumnDef`/`Row` system. The explorer pages cite the
data quire and query tables at compile time (for static pages) or
at runtime (for dynamic content).

**Tables:**

```
item_categories
  id       : Integer (PK)
  name     : Text
  parent   : Integer (FK -> item_categories, nullable, for grouping)
  prompt   : Text
  sort     : Integer

item_modifiers
  id       : Integer (PK)
  item_id  : Integer (FK -> item_categories)
  name     : Text    ("Pommel", "Guard", "Blade")
  sort     : Integer

modifier_options
  id       : Integer (PK)
  mod_id   : Integer (FK -> item_modifiers)
  name     : Text
  prompt   : Text
  sort     : Integer

materials       (id, name, prompt, sort)
rarities        (id, name, color, prompt, sort)
conditions      (id, name, prompt, sort)
enchantments    (id, name, prompt, sort)
alignments      (id, name, prompt, sort)
sizes           (id, name, prompt, sort)
colors          (id, name, prompt, sort)

character_races
  id       : Integer (PK)
  name     : Text
  group    : Text    ("Human", "Elf", "Dwarf", "Other")
  traits   : Text
  sort     : Integer

character_classes   (id, name, gear, sort)
genders             (id, name, traits, sort)
personalities       (id, name, expression, sort)
portrait_modes      (id, name, framing, sort)

biomes          (id, name, traits, sort)
times_of_day    (id, name, lighting, sort)
weathers        (id, name, effects, sort)
moods           (id, name, tone, sort)
scales          (id, name, framing, ratio, sort)

sd_config
  key      : Text (PK)
  value    : Text

generation_history
  id       : Integer (PK)
  page     : Text
  prompt   : Text
  params   : Text (JSON)
  seed     : Integer
  url      : Text
  created  : Integer
```

The `parent` field on `item_categories` creates the grouping
hierarchy: Sword (parent=null) has children Longsword, Katana,
Saber, etc. The UI reads the hierarchy and renders the 2D menu
from it.

### Layer 2: UI IR

Pages describe their UI using the existing `WidgetNode` tree from
`codex.foreword.ui`. New widget kinds needed:

```
WidgetKind additions:
  | WkNavBar (List NavItem)           -- navigation with active state
  | WkDimPicker (Text) (DimConfig)    -- mega-menu dimension picker
  | WkHero (Text)                     -- hero image display
  | WkHistoryPanel                    -- scrolling history sidebar
  | WkPromptField (Text)             -- textarea with label
  | WkToolbar (List WidgetNode)      -- horizontal button strip
```

Or better: use `WkCustom` tags with a data payload record:

```
widget-dim-picker : Text, List DimOption, Integer -> WidgetNode
widget-dim-picker (id) (options) (selected) =
  widget-custom id "dim-picker" ...

widget-hero : Text -> WidgetNode
widget-hero (id) = widget-custom id "hero" ...
```

The page .codex becomes:

```
page-ui : RuntimeDb -> WidgetNode
page-ui (db) =
  let items = query db "SELECT * FROM item_categories ORDER BY sort"
  in let materials = query db "SELECT * FROM materials ORDER BY sort"
  in widget-panel "root" DirColumn 0 [
    widget-nav-bar "nav" [
      nav-item "Items" "/item" True,
      nav-item "Characters" "/character" False,
      nav-item "Settings" "/setting" False,
      nav-item "Cards" "/card" False
    ],
    widget-panel "main" DirRow 0 [
      widget-panel "left" DirColumn 0 [
        widget-dim-picker "type" (rows-to-dim-options items) 0,
        widget-dim-picker "material" (rows-to-dim-options materials) 0,
        widget-dim-picker "rarity" (rows-to-dim-options rarities) 0,
        widget-prompt-field "prompt" "Additional details...",
        widget-toolbar "tools" [
          widget-button "gen" "Generate",
          widget-button "seed" "Random Seed"
        ]
      ],
      widget-hero "hero",
      widget-history-panel "history"
    ]
  ]
```

No HTML strings. No CSS strings. No onclick strings. Pure typed
Codex data describing what the UI IS, not how it renders.

### Layer 3: HTML Plug Renderer

The HTML plug's `render_widget_html` JS runtime function already
walks a WidgetNode tree and emits DOM elements. It needs extensions
for the new widget kinds:

- `dim-picker`: renders the mega-menu pill with hover dropdown,
  reads options from the DimConfig, wires onclick to state_set
- `hero`: renders the hero image area with lightbox click
- `history-panel`: renders the scrolling sidebar, auto-populated
  by generate_image callback
- `nav-bar`: renders sticky nav with active highlighting
- `prompt-field`: renders labeled textarea
- `toolbar`: renders horizontal button bar

CSS is generated from the Theme, not hardcoded:
- `palette.pal-bg` -> `background: #0a0a0a`
- `palette.pal-primary` -> gold accent color
- Border, padding, margin from WidgetStyle
- Corner radius from CornerStyle

The plug walks the theme once, emits a `<style>` block with all
the rules derived from the palette and widget styles. No CSS strings
in any page file.

### Event Wiring

The plug's runtime handles events generically:
- WkButton with id "gen" -> onclick calls `on_click("gen")`
- WkDimPicker with id "type" -> selection calls `on_pick("type", idx)`
- The page .codex defines handler functions that the plug calls

```
on-pick : Text, Integer -> Integer
on-pick (dim-id) (idx) = ...rebuild controls...

on-click : Text -> Integer
on-click (btn-id) =
  if btn-id == "gen" then do-generate 0
  else if btn-id == "seed" then random-seed 0
  else 0
```

## Implementation Plan

1. **Data tables**: Create `apps/data/explorer-schema.codex` that
   defines all the tables using the data quire's TableDef system.
   Populate with seed data via INSERT rows.

2. **Widget extensions**: Add `WkNavBar`, `WkDimPicker`, `WkHero`,
   `WkHistoryPanel` to `codex.foreword.ui.Widget` (or use WkCustom
   tags to avoid modifying the foreword).

3. **Theme-to-CSS emitter**: In the HTML plug, replace hardcoded CSS
   with `theme-to-css : Theme -> Text` that walks the palette and
   widget styles and emits CSS custom properties and class rules.

4. **Widget-to-HTML renderer**: Extend `render_widget_html` in the
   HTML plug runtime to handle the new widget kinds, including
   mega-menu rendering, hero panel, history sidebar.

5. **Page rewrite**: Each page .codex builds a WidgetNode tree from
   DB data, picks a theme, returns the tree. No HTML/CSS/JS strings.
   The opening function is: `mount-widget (page-ui db) theme "app"`

6. **Shared query layer**: A small chapter that wraps DB queries
   into typed records the page functions consume: `load-items`,
   `load-materials`, `load-rarities`, etc.

## What Changes

| Before | After |
|--------|-------|
| CSS as string literals in each page | Theme-derived, emitted once by plug |
| HTML as string concatenation | WidgetNode tree, rendered by plug |
| onclick as string in HTML | Event handler functions, wired by plug |
| Data as Codex list literals | Relational tables, queried |
| State as ad-hoc state-get/set keys | Typed state record per page |
| Menu structure as grouping functions | Parent-child relationships in DB |
| Modifiers as per-item-type lists | Relational join: item -> modifiers -> options |
| One CSS per page, duplicated | One theme, shared across all pages |
