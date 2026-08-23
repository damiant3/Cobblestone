# Desk Visual Lift

Status: Active design direction for Codex Desk chrome and list surfaces.
Date: 2026-08-23
Scope: Visual language only. No architecture, heap, or binding changes.

Related code:

- `apps/works/GopDesk.codex`
- `apps/works/GopStyleKit.codex`
- `apps/works/GopPrograms.codex` (and Appearance / Files / Scene panes)
- `codex/foreword/ui/Theme.codex`
- `apps/works/works-desk-contract.md`

Screens reviewed: Programs launcher, Appearance, Files (`ESP:/`), 3D View.

---

## 1. Diagnosis

The desk is functional and keyboard-complete. The primitives for a modern
look already exist in Theme and DesignLanguage (bevel, gradient, shadow,
corner, relief, accent border, state styles, `theme-ink-on`).

What reads as primitive is not missing capability. It is **one loud default
control style applied to every interactive row**: full-width green vertical
gradient capsules with uniform bevel, used for navigation, settings, and
actions alike.

Files content is already closer to a serious product (title bar, columns,
dark list, selection row). Appearance and Programs still wear the demo skin.
The lift is unification: make chrome and lists match the calmer Files content
language, and spend accent colour only on state.

### Why agent campaigns stall

Anthropic (and similar) models optimise toward the lowest common visual
pattern visible in the tree. When every row is a green lozenge, "improve the
UI" reproduces green lozenges with slightly different padding. Progress
requires a **strict visual contract** (tokens + acceptance screenshots),
not adjectives like "make it modern."

---

## 2. Critique by surface

### Programs launcher

What works:

- Clear column grouping (CODEX / Accessories+Productivity / Graphics+Settings)
- Shortcuts visible
- Dark ground, single accent family
- Taskbar present

What hurts:

- Every row is the same green gradient pill (primary weight on idle chrome)
- Heavy late-90s gloss competes with hierarchy
- Icons uneven in optical size and weight
- Section headers too quiet relative to bars
- Selection (e.g. Clock) reads as a white inset strip, not a designed state
- Uniform density without section rhythm
- Taskbar thin; status counters compete with the clock
- Duplicate entries (Files, Console) in sidebar and catalog add noise

### Appearance

What works:

- Settings discoverable; scheme name visible; space shortcuts documented
- Flags exist for Borders, Gradients, Shadows, Rounded corners

What hurts:

- Settings styled as primary CTAs (same capsules as Programs)
- Label and value fused (`Colour scheme: seahawk`, `Sounds: off`) with no
  left/right structure
- Flags claim depth is on, but rows still read as glossy bars (weak mapping
  or list rows not using full WidgetStyle)

### Files

What works:

- Title bar (`Files  ESP:/`) is the right chrome pattern
- Column headers; list geometry; dark content region
- Keyboard hints

What hurts:

- Selection is pure white full row (max contrast, low design)
- Sidebar green pills fight blue title bar and dark list
- Path presentation is terminal-raw
- Icons are placeholders
- Large empty region can feel unfinished without tighter top alignment

### 3D View

What works:

- Scene proves the graphics path (host rasterizer, shadows, etc.)

What hurts:

- Same sidebar candy
- Status line is neon-green terminal noise under the viewport
- No soft frame separating scene from desk

---

## 3. Target visual language: "Codex Quiet"

Everyday default (not Flight Deck / Chevy / LCARS modes). Quiet industrial
modern: dense enough for work, calm enough to live in, one accent, measurable
elevation.

### Layers

| Layer | Role | Rule |
|-------|------|------|
| Ground | Desktop / pane body | Soft vertical gradient or flat mid-dark; no border noise |
| Panel | Content / list surface | Slight raise or soft shadow; 1px low-contrast border or none |
| Control idle | Nav and settings rows | Near-flat; icon + label; no vertical green fill |
| Control state | Hover / selected / focus | Soft accent wash; optional 3-4px left accent bar |
| Chrome | Title bar, taskbar | Shared bar treatment; muted status; readable clock |
| Destructive | Shutdown | Muted or error-tinted; not primary green |

### Tokens (agent-directable)

- Type: body 13, label/shortcut 11 muted, pane title 15-18
- Spacing: 4-base; row gap 6-8; section gap 16; pane padding 12-16
- Row height: ~28-32 logical for comfortable hit targets
- Corners: 6-8 default; sharp for terminal mode; 12 only for LCARS
- Elevation: 0 ground, 1 panel/list, 2 floating menu
- Borders: 1px at low contrast when on; never pure white/black outlines
- Accent: one hue for selection, focus, and primary actions only
- Ink: always `theme-ink-on` (or equivalent) on filled bands

### DesignLanguage mapping

Prefer for default desk:

- `dl-relief` 0 or 1 (not strong bevel on every idle row)
- `dl-gradient` off for list/nav rows; optional light gradient on taskbar only
- Idle row approximates `sk-well` / flat panel, not `sk-control` primary fill
- Selected/hover = accent-tinted well
- Shutdown = separate tone (error or muted), not `pal-primary`

Respect Appearance flags (Borders, Gradients, Shadows, Rounded) as global
language, not as text on green bars.

---

## 4. Per-surface targets

### Sidebar and Programs

- Idle: flat/near-flat row, icon column aligned, label + muted shortcut
- Selected: accent wash + left accent bar (no white inset strip)
- Hover: slight surface lighten only
- Section headers: muted, stronger weight, 16+ top margin
- Shutdown: distinct secondary/danger style, bottom-aligned

### Appearance

- Row = label left, value/toggle right
- Not styled as primary buttons
- Selected row matches Programs/Files selection language

### Files

- Keep title bar, columns, path, hints
- Selection: soft accent wash (not pure white); keep name/size legible
- Sidebar must match the quiet row language
- Optional: light rule under column header row

### 3D View

- Do not change renderer
- Mute status line (smaller, muted ink)
- Optional cheap viewport edge / recessed well if low cost

### Taskbar

- Darker band; clock primary; debug counters dim or secondary
- Shutdown not primary green

---

## 5. Campaign order

Smallest jumps that read as a generation change:

1. **Remove gradient primary pills from idle nav and settings**
   (sidebar, Programs, Appearance).
2. **Unify selection** (Files white bar and Programs strip both become
   accent wash + optional left bar).
3. **Shared pane title bar** (Files pattern on Appearance; light frame cue
   on 3D if cheap).
4. **Mute helper and status text** (Appearance hint, 3D status, taskbar noise).
5. **Icons** only after the above (fixed optical size, mono or single accent).

One surface (or one concern) per CL. No drive-by refactors.

---

## 6. Agent brief (copy into a work lane)

```text
Visual unification pass only: sidebar rows, Appearance list, Files selection,
pane title bars.

Must:
- Default interactive row = flat/near-flat (no vertical green gradient fill).
- Selected = soft accent wash + optional 3px left accent; never pure white
  full row.
- Appearance: label left, value/toggle right; do not style settings as
  primary CTAs.
- Files: keep columns and title bar; restyle selection only.
- 3D: do not change renderer; mute status line; optional viewport edge only
  if cheap.
- Theme / DesignLanguage roles only; respect Borders/Gradients/Shadows/Rounded.
- No binding changes, no new settings, no ds layout changes.

Acceptance screenshots: Appearance, Files with selection, Programs, 3D View.
```

Programs-only variant remains valid for a first CL if preferred.

---

## 7. Non-goals (near term)

- Backdrop blur / acrylic
- Heavy continuous animation
- Full multi-weight icon system before row language is fixed
- Redesigning pane lifecycle, mark stack, or desk contract rules

Realistic "current" on this substrate: hierarchy, type, spacing, selection,
and restrained accent. Files already shows that path.

---

## 8. Success criteria

- Idle Programs/Appearance/sidebar no longer read as a wall of green capsules
- Selection is one language across Files, Programs, and Appearance
- Title bar treatment is recognisably shared
- Status/helper text is subordinate to content
- Seahawk (or successor Quiet preset) maps flags to visible surface change
- Goldens or paired screenshots for the four surfaces above

---

## 9. Origin

Compiled from review of live desk screenshots (Programs, Appearance, Files,
3D View) and the existing Theme / GopStyleKit / desk-contract machinery,
2026-08-23, for the CodexOS design track.
