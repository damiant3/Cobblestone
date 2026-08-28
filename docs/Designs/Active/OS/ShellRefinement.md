# Shell Refinement -- from dev surface to a first-rate OS shell

**Opened 2026-08-20 (val) at Damian's direction.** The desk works and is a
good dev surface. It does not look or behave like something you would hand a
user: the word he used was fischer-price. This design is the campaign that
fixes that, and it covers visual refinement, space efficiency,
responsiveness, sound, a real settings system, fonts, backgrounds, icons,
accessibility and parental controls, with the stated goal of matching what
Windows, macOS and Android give a user on day one.

## The measurement this design starts from, and it changes the whole shape

**The UI library is far ahead of the shell. 36 of the 50 chapters in
`codex/foreword/ui/` are never cited by `apps/works/` at all.** Measured
2026-08-20 over the cite graph, with a control: `widget-` appears 198 times
in the works app, `ico-`/`Icon` once, anything audio once, and `a11y` zero
times.

The unused list is not a list of stubs. It includes **`Icon.codex` at 1,366
lines**, `Sound.codex`, `Accessibility.codex`, `Animation.codex`,
`Dialog.codex`, `TrueTypeFont.codex`, `FontAtlas.codex`,
`GlyphRasterizer.codex`, `Vector.codex`, `Canvas.codex`, `RichText.codex`,
`TreeView.codex`, `Window.codex`, `Surface.codex` and `Focus.codex`.

The fourteen the desk does cite are `Widget`, `Theme`, `Layout`, `BoxModel`,
`Event`, `KeyInput`, `Scroll`, `Overlay`, `Dropdown`, `DataTable`,
`Validation`, `StatusBadge`, `SettingsPanel` and `CommandPalette`.

**So the shell is built on roughly a quarter of its own toolkit, and the
quarter it picked is the structural quarter with none of the presentation.**
That is exactly what produces a competent-but-plain surface, and it means
this campaign is substantially a WIRING campaign rather than a build-from-
nothing one. That is the good news and it should be stated before any stage
list, because it changes what the work is: less inventing, more connecting,
and the connecting is where the measurements go.

**And the risk is smaller than that paragraph first claimed, but the first
count of it was wrong and is corrected here.** The obvious worry is that an
unused chapter is an untested chapter, so "it exists" would not be "it
works". This section said "all 36 carry tests, one to six each, not one is
untested". That counted test FILES citing each chapter, and **a test named
for a chapter is not a test of it** (L-NAMED).

Re-measured 2026-08-20 by asking what a test ASSERTS rather than what it
cites. **The 28 files in `codex/test/forewords/ui-*` are seven lines each.
Every one cites a chapter, calls nothing from it, prints "UI/X OK", and has
a one-line `.expected` containing no digit.** They prove the chapter
compiles and links, which is worth something and is not correctness.
`apps/foreword-all-compile` is the same claim in one file.

Discount those two and **11 of the 50 chapters had no asserting test at
all** when this was measured 2026-08-20: `Accessibility`, `Charts`,
`Clipboard`, `Cursor`, `Drag`, `Editor`, `SearchBar`, `Selection`, `Touch`,
`Vector`, `Window`. **`Accessibility` came off that list 2026-08-21** and the
rest of the list has not been re-measured since, so it is ten by one known
change rather than by a fresh count. The other 39 do, and
some are proved hard: `Icon` has twelve assertions in
`codex/test/ui-icon-test`, which is why stage 2 could lean on the artwork
without arming it first.

That list is not evenly distributed over this campaign. It costs **stage 2's
vector half** (`Vector`), **stage 6 entire** (`Accessibility`), and parts of
stage 3 (`Window`, `Cursor`). Those stages arm the chapter before they wire
it; the rest wire first.

**So the risk is integration, not correctness, for 39 of the 50**, and that
is a different and more tractable thing. Each of those chapters was proved against its own
expectations in isolation. None was proved against the desk's real
constraints: a 128 MB arena with no collector, `ui-wscale`'s step at 1600,
the compositor's clip bound, and a frame budget shared with a taskbar that
repaints every second. Each stage below must arm the chapter it wires
AGAINST THOSE, which is what its own test could not do.

## Stage 1 is closed. What survives it

Stage 1 threaded the loaded TrueType face through the widget compositor and
fixed the two placement faults that came with it. The changelists carry the
account; four things it established are still load-bearing and are here
because nothing else records them.

**`seed/Codex.img` carries no typeface and is deliberately NOT rebuilt.** It
was held until the metrics were right, they landed 2026-08-20, and it is
still held because refreshing that image is a RELEASE step: it moves the
`seed img bytes` and `seed img sha256` claims in `TechnicalDetails.md`, which
`build/check-doc-counts.ps1` checks at zero tolerance and which are public.
`.claude/skills/release/SKILL.md` owns it. Until it is rebuilt, `desk.ps1`
defaults to it and every dev boot shows the bitmap face, which is also why a
type sweep with no `-Disk` reports fourteen identical panes and reads as "the
change did nothing".

**`fl-write-yoffsets` is a STUB.** The per-glyph y-offset table it would write
is uniformly zero and `gbf-put-text-loop` reads it and always gets zero
(L-UNCALLED). It is harmless only because every glyph is rasterized into one
common cell. A placement account built on that table's existence is built on
nothing writing it, and one was.

**`gf-gh` is the rasterized MAX glyph height**, the tallest ascender to the
deepest descender across the whole face -- 126 per cent of the em for cmunss.
Almost no line contains both, so the ink a reader sees is far shorter than the
cell, and the gap they perceive is the step minus the INK rather than the step
minus the cell. Centring the cell instead of the cap band is the same error
one axis over, and it is what `comp-text` had to learn.

**`comp-cols` and `comp-fit` still bound text by `comp-cell-w`, a fixed 8**,
which is wrong in both directions: it truncates a narrow string early and
would let a wide one overflow. `gfont-text-clip` converts the overflow into a
clip, which bounds the defect rather than fixing it. Stage 9 carries the
measurement and the repair.

### The leading question is OPEN, and it is not the one-line change it looks like

The complaint is that paragraph text is set with too much leading. The cause
was guessed as a line step computed from `comp-glyph-h` rather than from the
face's metrics. **Measured 2026-08-21 (fester), that guess is REFUTED, and
acting on it as written would have made the leading worse.** The step is
`dk-line-h`, a fixed 18 scaled by the desk scale, and it consults no face --
that half was right. At the desk's default, 1600 wide, `ui-wscale` 2, CMUNSS
at 32 ppem:

| | device px |
|---|---|
| face cell `gf-gh` | 41 |
| face ascent `gf-asc` | 29 |
| face cap `gf-cap` | 22 |
| current step `dk-line-h * s` | 36 |
| **step minus face cell** | **-5** |

The step is already five pixels TIGHTER than the cell. "Compute it from the
face's own metrics" reads as `gf-gh`, 41, and would open the lines up in the
direction the complaint is about.

Read off the glass the same day, ink bands counted per row across the Welcome
window's body: baseline to baseline is **36, every step, in both blocks**, ink
band 23 to 32, gap 4 to 13, and the single 47 step is the paragraph break
(`dk-para-gap` 6 logical at scale 2). No jitter to chase.

**So the open question is not "does the step ignore the face" -- it does, and
that is not the defect -- but "what should baseline-to-baseline be".** That
needs a target defined against the ink and a capture to judge it on. It is not
answerable from `gf-gh` and it is not a one-line change.

Reproducing the table needs a font-carrying image, because `seed/Codex.img`
deliberately carries no face:

```powershell
pwsh build/build-boot-img.ps1 -Out <font>.img -Kernel seed/Codex.cdx   # -Font defaults to cmunss
pwsh build/compile.ps1 -Src codex/test/apps/desk-leading-metrics.codex `
  -Out <out>.cdx -Log <log> -Kernel seed/Codex.cdx
pwsh build/test-run.ps1 -Kernel <out>.cdx -OutFile <out>.txt -DiskFile <font>.img
```

## The one structural obstacle

**`gop-draw-text` paints every glyph as sixteen scaled rows**
(`comp-glyph-h : Integer = 16`, `GopComposite.codex`). The desk's font path
is `Works chapter GopFont`, a works-local bitmap face, and the UI quire's
`Font` / `FontAtlas` / `GlyphRasterizer` / `TrueTypeFont` stack is not in the
picture at all.

Everything visual is downstream of that. A fixed 16-row cell forces monospace
metrics, forbids hinting and antialiasing, and pins the whole type scale to
integer multiples of 16. **No amount of palette work fixes a shell whose text
is a bitmap grid**, and conversely, proportional antialiased text is the
single change that most moves the surface toward looking like a real OS. It
is therefore stage 1 and not a later polish item.

The compositor guarantee that clause exists to protect is real and must
survive: `gop-draw-text` is a BOUNDED primitive, which is what lets
`comp-render` clip safely (fester, main 17846). A proportional replacement
has to keep a bound, which means the glyph rasterizer must report its extents
before it draws rather than after.

## Stages

Each stage names what it wires, what proves it, and what it must not break.
**Ordering is by dependency and by visual return, not by ease.**

### Stage 1: Typography

Replace the fixed-cell text primitive with the UI quire's glyph stack.
Proportional metrics, an antialiased raster path, a real type scale (display,
title, body, caption) in `Theme`, and a spacing scale beside it so padding
stops being ad hoc integers.

- Wires: `Font`, `FontAtlas`, `GlyphRasterizer`, `TrueTypeFont`, `Encode.TrueType`.
- Proves: text extents match the rasterizer's reported advance within one
  pixel over a known string; the clip bound still holds at a pane edge; a
  before-and-after capture at 1024, 1600 and 1920.
- Must not break: the bounded-primitive guarantee, and `ui-wscale`'s
  behaviour at the 1600 step.
- Risk: this is the deepest change in the campaign and every pane's layout
  shifts. It goes first precisely so the rest is measured against the new
  metrics rather than remeasured later.

### Stage 2: Iconography

Wire `Icon.codex`, which is 1,366 lines and unused. Icons in the launcher,
the taskbar, the Files pane's type column, dialog severity, and status.
Vector where `Vector.codex` can serve, so icons scale with `ui-wscale`
instead of pixelating.

- Proves: an icon renders identically at scale 1 and 2 modulo size; the
  Files pane's type column distinguishes at least directory, source, image
  and binary.

**The type column landed 2026-08-20** (`apps/works/GopIcon.codex` new,
`GopFiles.codex` wired, `codex/test/apps/files-type-icons`). Three things
came out of it that the stage list did not know.

`Icon.codex` cannot draw on this surface and the reason is cost, not
plumbing. `icon-blit` paints into `Framebuf`, whose `fb-set` returns a
record per pixel at 24 bytes each. The desk's buffer is linear XRGB behind
`gop-put` at 0 bytes per pixel. So the artwork and the lookup are reusable
and the renderer is not: `GopIcon` is a blit against `base`/`stride` with
the same column bound `gop-draw-text` carries, and every later stage that
paints an icon goes through it rather than through the UI quire's.

**Every icon is a module-level `List Integer`, so a mention re-materialises
the whole bitmap** and mentioning `icon-set-standard` in a paint would
rebuild all 63. The kit is built once per paint and threaded, which is why
`IconKit` exists at all rather than a name-to-icon lookup taking a `Text`.
That shape is the one to copy in stages 3 and 5.

**The medium cannot exercise the image class.** `seed/Codex.img` holds
directories, one `.SRC`, one `.CDX` and a `SRC/` tree of `.COD` and
`INDEX.TXT`, so a capture shows directory, source and binary and never
image. The classification is asserted in the test, where an entry can be
constructed; the capture is evidence for three of the four and says so.

**The launcher and the taskbar landed the same day** (`desk-chrome-icons` on
the six chrome ids, `desk-gpr-icons` on the fourteen launcher rows,
`codex/test/apps/desk-chrome-icons`). They are widget trees rather than
hand-drawn panes, and the plan above assumed that meant a `WkCustom` tag arm
in `comp-custom`. **It does not, and the reason is the hit test.**

`ev-hit-widget` answers the DEEPEST node under the pointer, so an icon node
inside a button is what a click on the icon resolves to, and the desk's
dispatch has never heard of its id. Painting after the walk, keyed on
`wn-id` over the LAID tree, cannot move a hit test at all, needs no widget
kind, no signature change, and no edit to `GopComposite`. It is the road
`desk-taskbar-clock` already takes over the band it repaints. Stages 3 and 5
should reach for it before they reach for a new widget kind.

What it costs is that an icon is now keyed on a STRING an id has to match,
so renaming a button removes its icon in silence. That is what
`desk-chrome-icons` asserts, control included: an id the tree does not carry
answers 0, so a walk that found nothing cannot report the same shape as one
that found everything.

Two numbers came off the glass rather than out of the design. A gutter
exactly as wide as the icon puts every label flush against it, so the gutter
is one cell wider; and the icon centres on the CONTENT BOX rather than on
the glyph height, because the glyph height depends on which face loaded and
the box does not.

**This campaign needs no build token, and that is measured rather than
assumed.** A seed rebuild is decided by REACHABILITY and not by directory
(`DevelopersRulebook.md` 7), and `concat-codex-self.ps1` walks `cites`
transitively from `codex/compiler`. That unit is 86 chapters. Twenty-one of
them are foreword chapters and every one is from `codex/foreword/core`
(`CCE`, `Fat16`, `Maybe`, `MathLib`, `Sha256`, `Sort`, ...); the prefix
`Ui--` does not occur in it at all. The twenty-one are the positive control:
the instrument can see foreword chapters and sees no UI one. So work in
`codex/foreword/ui/**` is not seed-affecting and does not queue for the
token.

What it DOES move is `seed/Codex.img`, which carries the desk, and that is a
separate artifact under a separate hold. Do not read "no token" as "no
consequence".

**The status half, surveyed 2026-08-20, and it is the pattern again.**
`StatusBadge.codex` already has everything this stage wanted and none of it
is switched on:

- **`badge-widget` has no production caller.** Its only callers anywhere are
  three tests (`lib/badge-tone`, `lib/detail-pane`, `lib/status-badge-priority`).
  The chapter is proved and unused, which is this campaign's thesis in one
  chapter.
- **`BsIcon (icon) (color) (tone)` renders the icon's NAME as text**:
  `widget-label id icon` inside a 16 by 16 box. At eight pixels a cell that
  shows two letters of the word. Nothing constructs a `BsIcon` anywhere in
  `codex/` or `apps/`, so nobody has ever seen it do that.
- **`status-dot` and `status-dot-filled` are indistinguishable.**
  `comp-custom` knows one tag, `event-dot`; both dot tags fall to its
  `comp-box` branch and paint the same rectangle. Filled and hollow are the
  same picture.

So the status half is two edits, not a feature: a `status-dot` and
`status-dot-filled` arm in `comp-custom`, and `BsIcon` routed through the
icon path instead of a text label. `badge-severity` answers `BsPill` today,
so severity gets its icon by moving to `BsIcon` once that renders.

**STATUS IS DONE, 2026-08-20** (main 18299). `comp-custom` answers all three
of `StatusBadge`'s tags: the two dots were the SAME rectangle before, and
`BsIcon` was a label carrying the icon's name, so a badge asking for a
warning sign drew "wa". The arm that proves it is
`codex/test/apps/badge-tags`, measured against the depot compositor, where
every comparison flips.

Writing that arm at the size `badge-widget` really emits found a second
defect: measured off the CONTENT box the dots drew zero pixels at 10 by 10,
because the style's padding leaves nothing in a box that small. Padding
insets text; a picture that fills its node has nothing to inset, so these
take the border box. An arm written at a convenient 20 would have passed and
shipped a badge that draws nothing.

**SEVERITY HAS NO SURFACE, and that is the finding rather than a deferral.**
`badge-severity` answers a `BsPill` and has exactly one caller in the tree, a
test. `Dialog.codex` is one of the unused chapters and the desk has no
confirm or alert surface at all: `warning` appears in `GopDesk` once, as a
palette colour. So changing severity to carry an icon would move nothing on
any screen. It is waiting on a dialog surface, which is a stage 3 or 5 item,
not a stage 2 one.

**THE VECTOR HALF IS DONE and it went to stage 9.** It was scoped here as
three things -- a rasterizer that grows curves and a fill, artwork that did
not exist anywhere, and a cache, because `vec-rasterize` allocates a record
per pixel and cannot run per frame. All three landed 2026-08-21: the fill at
main 18916 by pointing `Vector` at `GlyphRasterizer`'s edge scanline rather
than writing a second rasterizer, the artwork at 18975 and 19008 as nineteen
paths, and the cache as a coverage blob rasterized once at boot rather than a
per-frame call. Stage 9 carries the account.

**One defect found while looking and routed rather than fixed here.** The
taskbar renders the shell's own name as "Code". `comp-draw-node` bounds a
label with `comp-fit t (comp-cols c s)` BEFORE drawing, and `comp-cols` is a
cell count: the menu button's content box is 116 device pixels, so 7
columns, and `   Codex` is 8 characters. At the loaded face that label
renders in 110 pixels, so the pixels fit and a cell COUNT drops the `x`.
`comp-fit` is fester's half. No min-width workaround was added, because once
`comp-fit` measures, the existing minimum fits with room to spare and a
constant added now would be dead weight.

Still open in this stage: dialog severity, waiting on a surface.

### Stage 3: Surfaces and depth

Background images, layered surfaces, elevation and shadow, rounded corners
where the theme asks. `Surface.codex`, `Canvas.codex`, `PixelBuf.codex`.
Wallpaper needs a decode path and a place to keep the image, which ties to
stage 5's settings store.

- Must not break: the idle repaint budget. A wallpaper that repaints per
  frame is a regression against WORKS-37's leak fix, which cost real work.

**MOST OF THIS STAGE IS ALREADY BUILT, surveyed 2026-08-20, and the stage
list did not know it.** The desk has an ADORNMENT system: `dk-adorn-border`,
`dk-adorn-gradient`, `dk-adorn-shadow` and `dk-adorn-round`, resolved into
real `Theme` values by `dk-grad` and `dk-shade`, painted by the compositor's
`comp-shadow`, `comp-bevel`, `comp-accent` and `comp-corner-inset`, and
TOGGLEABLE BY THE USER in the Appearance pane, which already lists all four
with a key each. `GopStyleKit`'s `DesignLanguage` carries `dl-relief`,
`dl-bevel-w`, `dl-corner`, `dl-outline` and `dl-gradient` per language.

So elevation, shadow and rounded corners are not work. What was wrong is one
bit: `dk-adorn-default` was 11, which is border, gradient and round with
SHADOW off, so the one adornment that makes a control look like it sits
above the ground was the one nobody saw. It is 15 now (main, this CL) and
`s` in the Appearance pane puts it back.

What remains of this stage is therefore background IMAGES alone, and the
dependency stands: a decode path plus somewhere to keep the image, which is
stage 5's store.

**A gradient desktop was built here and REVERTED rather than shipped**, and
the reason is worth more than the change was. `dk-wall` is one flat colour,
so painting the desktop as a vertical gradient off the palette looked like
the cheap half of "background images" with no decode path needed. Written,
compiled, captured. The sampled column ran #001327 at the top to #000E1C at
the bottom, a clean gradient. It was already doing that BEFORE the change:
the pre-change capture holds the identical three values, because the root
panel covers the screen and carries the theme's own vertical gradient. The
change was invisible and cost an interpolation per scanline per repaint, so
it went back. Identical where you expected different is the tell (L-SUSPECT),
and only the control caught it.

**One defect of mine found by the same capture.** The sidebar icons appeared
on every PANE screen and not on the bare desktop, from main 18268 until this
CL. `desk-draw` renders the chrome through `comp-render` directly while
every pane goes through `dk-chrome-paint`, and the icon pass hung off the
latter only. Every capture I read after adding those icons was of a pane; I
took the bare-desktop control shot in the same session and never looked at
it. Both chrome renders carry the pass now, and they are the only two.

### Stage 4: Sound

Wire `Sound.codex` to the Intel HDA device codex-vm already emulates with
host waveOut. Click, focus change, error, notification. Every one of them
off by default until stage 5 can configure them, because an OS that beeps
and cannot be silenced is worse than a silent one.

- Proves: a click produces a measurable buffer submission, and the mute
  setting produces none. The negative arm is the point.
- Must not break: frame pacing. Audio submission on the draw path is how a
  desk starts stuttering.

**The device half landed at main 18559** (`hda-play-pcm-at`, a caller-owned
descriptor list, arm `hda-play-gated`) and **the click landed at main 18696**.
`snd-click` is rendered once in `desk-run`, above the base mark, into
`dk-sound-cell`: the controller reads the list and the samples by DMA after the
call returns, and `desk-loop` restores the heap on every poll, so a click that
built its own buffer would be a DMA read of freed memory. The click itself is
seven register writes and no allocation, which is what keeps it off the frame
budget. The setting is `dk-set-sound`, a third key beside scheme and
adornments, default OFF, and the Appearance pane toggles it on `a`. Its arm is
`codex/test/apps/desk-click-sound`: muted submits nothing, unmuted submits one
buffer per click, and a third arm holds the setting on and passes no click, so
a pass cannot come from a gate reading only one of the two. Both discriminating
sabotages were run and each turned the arm they aimed at red.

**What the stage still owes: focus change, error and notification.** Those are
three more effects in `Sound.codex` and three call sites that do not exist yet;
the click is the one the desk already had an event for.

### Stage 5: Settings

The one users see and the one the rest depends on. A settings SCHEMA
(typed keys, defaults, ranges), a persistence path through the existing fact
store, and a Settings GUI built on `SettingsPanel.codex` with categories:
Appearance (theme, wallpaper, type scale), Sound, Accessibility, Accounts
and Parental Controls, System.

- Proves: a setting survives a reboot; an out-of-range value is refused
  rather than clamped silently; the default is restored by an explicit
  action and not by deleting a file.
- This is the stage that makes stages 3, 4, 6 and 7 configurable rather than
  hardcoded, so it cannot come last.

### Stage 6: Accessibility

Wire `Accessibility.codex`. Roles on widgets, a visible focus ring, keyboard
traversal of every pane, a high-contrast palette, text scaling independent
of `ui-wscale`, and an announce channel a speech path can later consume.

- Proves: every interactive widget answers a role; tab order reaches every
  control in a pane and returns; the high-contrast palette meets a stated
  contrast ratio, computed rather than eyeballed.
- **This is a correctness area, not a decoration area.** A focus ring that
  is present but invisible against one palette is a defect, and the palette
  census in `Theme.codex` already warns that 36 `Palette` literals exist
  across 32 files.

**The chapter is ARMED as of 2026-08-21, which is what this stage owed before
it could wire anything.** `codex/test/forewords/ui-accessibility` was one of
the twenty-eight seven-line files that cite a chapter, call nothing and assert
nothing (L-NAMED); it now censuses all sixteen role constructors against both
`a11y-role-name` and `a11y-is-interactive`, and pins the builders, the
defaults, and both branches of `a11y-announce`. Nine lines, every value
predicted before the run and every one matched. The census shape is the point:
`a11y-is-interactive` answers False through an `otherwise` catch-all, so a role
that drifts into it is indistinguishable from one that was always inert unless
every constructor is named. Two sabotages, each moving only the row it aimed
at: dropping `RoleMenuItem` from the interactive list moved one cell of one row
(`menuitem/y` to `menuitem/n`), and swapping the alert's `LiveAssertive` for
`LivePolite` moved one row. The `empty` row is bound before the builders run
and printed after them on purpose, because `__record-set` stores in place and a
shared `a11y-empty` would show as mutated defaults.

**Nothing wires it yet.** Roles on widgets, a focus ring, tab traversal, the
high-contrast palette and text scaling are all still open, and they are the
stage.

### Stage 7: Accounts and parental controls

Needs a user model, which the shell does not have. It ties to
`Identity.codex` and the trust lattice rather than inventing a parallel
notion of a user, and that is a design question before it is a code one.

- Depends on: red's identity reconciliation work, which is open.
- Deliberately last, and deliberately flagged as the stage most likely to
  need a ruling before it starts.

### Stage 8: Responsiveness

`Animation.codex` wiring, transitions on open and close, a throbber for slow
work, and a stated frame budget with an instrument that reports when a pane
misses it.

- Proves: the budget is measured, not asserted. The desk already has a
  frame-timing readout in the 3D pane; this generalises it.

## Stage 9: crisp. Damian's direction, 2026-08-21, with a reference

He sent a photograph of his own Windows 11 desktop and asked for that
standard, naming what he meant: antialiasing, smooth corners, a clear font at
SMALL size, colourful full icons, overlapping windows, a taskbar showing
common and active apps, windows with a status bar along the bottom, ribbons
and toolbars along the top, clear menus with simple icons. He was open on how
icons are made -- an icon font or simple images, "whatever makes sense" -- and
floated a drawing tool for authoring them, Freehand or Illustrator shaped.
The instruction that decides the work: **get the primitives we do not have
written.**

### The inventory, measured before building anything

Surveying first paid for the fifth time. Most of that list is already written
and switched off, and the honest gap is much narrower than the request sounds.

| He asked for | What exists | State |
|---|---|---|
| Overlapping windows | **DELETED 2026-08-25 (red's ruling, WORKS-49).** `codex/foreword/ui/Window.codex` was a window manager on paper: `wm-open`, `wm-focus`, `wm-move`, `wm-resize`, `wm-tile`, z-order, 167 lines, cited by nothing. 6.3c built overlapping windows and settled that the desk could not be its caller -- it is a functional record and the desk's durable state must sit below a bump-allocator base mark that every pane exit restores. The desk's registry is a block where the array order IS the z order. | shipped, from a block |
| Compositing them | `Surface.codex`, a z-ordered compositor with per-surface framebuffers and `comp-blend-pixel`, 294 lines | written, cited by nothing |
| Vector artwork for icons | `Vector.codex`: paths, beziers, arcs, circles, fill and stroke, `vec-rasterize`, 191 lines | written, cited by nothing, and its rasterizer is Bresenham, so it is HARD EDGED |
| The drawing tool | `Canvas.codex`: viewport pan, zoom, zoom-to-fit, snap-to-grid, world/screen transforms | written, cited by nothing |
| Motion | `Animation.codex`: transitions, keyframes, easing, throbbers | written, cited by nothing |
| Icons | `Icon.codex`, 1,366 lines, `icon-new` takes pixel data as a list of integers and `icon-blit-keyed` composites on a colour key | written and USED; the desk's icons are two-colour by authorship, not by limit |
| Antialiasing | nothing | **genuinely missing** |

So the campaign is again mostly WIRING, with one real hole. `GopDraw` is 242
lines and its whole vocabulary is `gop-put`, `gop-fill-rect` and bitmap text:
one pixel, one rectangle, no line, no curve, no blend. Everything that reads
as crisp in his photograph is coverage, and coverage needs a read of what is
already on the glass.

### The first primitive: partial coverage

`gop-get` reads a pixel back. The GOP framebuffer is ordinary RAM at
0xBF000000 with no MMIO trap, so a read costs what any load costs, which is
what makes this affordable at all.

`comp-corner-ins256` answers the inset a corner row wants in 256ths instead of
whole pixels, and `comp-blend-px` mixes a pixel by that coverage through the
`comp-lerp` the gradient path already had. `comp-rows` then draws the solid
span as before and blends the two boundary pixels. A row with no corner takes
the old path exactly, so a radius of 6 at scale 2 costs twenty-four blended
pixels for a whole box however tall it is.

**Landed main 18827.** The golden sweep over all fifteen captures moved every
one, which is normally the reading that says a change went where it had no
business; here it is the intent, and the census is what proves it went only
where it meant to. On the desktop: **414 differing pixels of 1,440,000**, in
bands of about fifteen rows that line up with the four sidebar buttons and
Shutdown, two to six pixels per row, at symmetric x pairs (39 and 312, 37 and
314) which are the two ends of the same row. The colours move from the
wallpaper `#001327` toward the button fill `#509729` rather than to it. No
straight edge moved and no rectangle shifted.

### The chrome's icons are drawings now

**9.2 landed main 18916, 9.4a main 18943, 9.4b main 18975, and 9.4c main
19008 -- every icon the desk names is a path.** `Vector.codex`
could only stroke straight segments and dropped every curve silently; it is
flattened into `GlyphRasterizer`'s `Edge` list and filled with coverage now,
which is why no rasterizer was written. `gicon-render` rasterizes a path into
a coverage blob, `gicon-blit-cov` blends it, and the desk rasterizes its six
chrome drawings ONCE in `desk-run` above the base mark into `dk-icons-cell`.

**A name with no drawing still draws its eight pixel mask** (L-FALLBACK), so
the set grows one drawing at a time and no surface stops working while it
does.

**`vec-coverage` CROPS a path into the grid, it does not scale it into it**,
and that is worth knowing before using it: a drawing authored in a 32 unit box
and rendered at 24 came out as the top-left 24 units of itself, complete and
wrong. `vec-coverage-in` takes the box and maps it. The cropping version is
kept because a caller already working in pixel coordinates wants exactly that.

**The order the defect was found in is the reusable part.** The picture did
not change, twice, and both readings of why were wrong: the guard was rewritten
on a guess and moved exactly one icon of six, and the second capture was read
as "squashed" when the squashing was a non-uniform crop in the comparison
script. `codex/test/apps/desk-icon-size` exists because of that -- it prints
the size `dk-icons-init` settles at boot beside the one `desk-icon-in`
recomputes per paint, and it said they agreed, which is what ruled out the
guard and left the geometry.

### What is still missing, in the order it is worth doing

1. **The shadow is still hard edged.** `comp-shadow` draws its own offset
   shape; it should blur, and a blur is the same coverage primitive applied
   over a kernel rather than a chord.
2. **An antialiased stroke. DONE.** `vec-stroke-coverage` and its em-mapped
   form answer a 0 to 255 grid the way `vec-coverage` does, so outline artwork
   is no longer a Bresenham staircase through a smooth compositor.
   `vec-rasterize` is left alone: it is the pixel-list stroke its own test
   asserts, and nothing on the desk calls it.

   **The construction is the finding, and it is forced by the fill's rule.**
   `gr-fill-scanline` pairs its intersections, which is even-odd, so two
   overlapping quads CANCEL. Handing one edge list of stroke quads to
   `gr-render-edges` therefore guts the ink at every join, every meeting cap,
   and every self-crossing. Each quad is rendered on its own and merged by
   MAXIMUM instead. Measured on an X of two segments at width 2 in a 16 by 16
   grid: per quad the crossing pixel reads **255**, the single edge list reads
   **47**, and a point on one arm away from the crossing reads 255 under both,
   which is what says the damage is local to the crossing rather than to the
   stroke. The rejected construction is kept in `codex/test/ui/vector-raster`
   as a row rather than described, so it is a runner and not a claim. It is 47
   and not 0 because the centre pixel straddles the cancelled lozenge and a
   few of its sixteen supersamples fall outside it.

   Segments are square-capped by half a width along their own axis, which
   covers a join without a miter calculation. Width is in PATH UNITS;
   `vs-stroke-width` is not read, because it is capped at 1000 and its scale
   was never settled, and the arms confirm the units by arithmetic rather than
   by eye: a 12 unit run at width 2 has area 28, which is 2 by 14 with the two
   caps, and at width 4 it is 64, which is 4 by 16.

   **Cost, and it is the reason the bands exist.** `gr-render-edges` allocates
   `width * height` on every call and there is no collector, so a per-quad
   full-grid render would leave one copy of the supersampled surface resident
   per segment for the whole call. Each quad is rendered over only the rows it
   spans, so the buffer is the band. A caller should still rasterize a stroke
   ONCE at load, the way the icons already do for fills, rather than per paint.
3. **The eight pixel cell** (see `desk-label-metrics`): the widget layer sizes
   text by `text-length * 8` while the renderer draws proportionally, and that
   has to be honest before any width constraint lands, because the full-bleed
   stretch is currently hiding every under-reservation.

   **Where the measurement arrives: settled, and the two obvious routes were
   both wrong.** `Widget.codex` is a foreword chapter and may not cite the app
   quire that owns the face (`DevelopersRulebook.md`, dependency direction
   `codex.foreword -> codex -> codex.os -> apps/`), so the design question was
   whether the metric rides on `Theme` or arrives as a parameter. Measured
   rather than argued: `widget-label` and `widget-button` have **2,846 call
   sites in 116 files**, `widget-layout`/`widget-measure` have **67 in 32
   files**, and there are **36 `Theme` literals in 34 files**. Both routes
   therefore churn thirty-odd files across other lanes' quires, and both are
   ways of smuggling an app-owned measurement into the foreword.

   What the code already says settles it instead. `widget-measure` returns a
   leaf UNCHANGED (`Widget.codex`, the `wn-child-count == 0` arm), so nothing
   in the foreword ever revisits a label's intrinsic width; and the desk
   already measures through the face in `desk-text-w`. So the fit is a WALK
   over the tree in the app that holds the face: `comp-fit-text`
   (`GopComposite.codex`) rewrites `wn-min-w` for every `WkLabel` and
   `WkButton` from `gfont-text-w`, rounded UP to logical units, and answers
   the tree unchanged when `gf-ok` is false. The foreword gains one additive
   helper, `widget-set-children`, and no signature in it changes.

   **It is applied to the CHROME only, and that is a correctness bound rather
   than a scoping preference.** A pane that hit-tests its own subtree does so
   with plain `widget-layout` (`GopBrowser.codex`), so fitting a pane's tree on
   the paint side and not on that side puts a click on the wrong widget. The
   fit therefore lives in `desk-chrome-face`, which wraps the sidebar and the
   taskbar and hands `content` through untouched; `desk-chrome-with` stays as
   the faceless caller for the test chapters that have no volume mounted.

   **A pinned width is an intent and the guess is not, and they land in the
   same field.** `comp-fit-node` therefore replaces `wn-min-w` only while it
   still equals what the constructor guessed. The taskbar carries one case of
   each: the menu button is pinned to 72 and `task-clock` to 220 over an EMPTY
   string, and an unguarded fit measures that empty string and reserves
   nothing. Sabotage measured 2026-08-24, guard forced true: menu 72 -> 71 and
   clock 220 -> 0, both rows moving, so the guard has an arm rather than an
   assertion.

   **Amended 2026-08-25 by the brand rename: the menu button is no longer a
   pinned case and the clock is the only one left.** The taskbar entry carries
   the brand word, so its label went from `   Codex` at 8 characters to
   `   Cobblestone` at 14.

   **The cost of the pin is on the FALLBACK path, and the first version of
   this paragraph got that wrong.** It said the 72 had become narrower than
   its own string, which is the reading a longer word invites and which the
   arm refutes: measured 2026-08-25 with CMUNSS at 1600, `desk-label-metrics`
   answers `fitted menu button : 63` logical, so with a face the pin would
   have added nine logical pixels of slack and clipped nothing. `comp-fit-text`
   answers the tree UNCHANGED when there is no face, so it is the bitmap path
   where a pin is the whole reservation: fourteen characters at the eight
   pixel cell want 112 against the pin's 72. `desk-taskbar-hit` runs faceless
   and is the arm that path has.

   So the pin was removed rather than raised to a second guess, and the fit
   answers on one path while the constructor's own guess answers on the other.
   Taking the LARGER of pin and measurement is the obvious repair and is
   wrong: it keeps the clock's 220 correctly and also keeps every constructor
   guess that exceeds what the face needs, which is the slack this item exists
   to remove.

   **Removing the pin also removes its `min-h` of 0, and that is a regression
   the arm caught.** `widget-set-min` sets both fields, the button's own
   default height is 24, and the taskbar band grew from `dk-task-h` to 36 the
   moment the call went. `desk-taskbar-hit` went red on `band height is
   dk-task-h` in the same run that proved the width. The shipped form keeps
   the call and reads the constructor's guess back off the node
   (`widget-set-min mb (mb.wn-min-w) 0`), which restores the guess exactly so
   the fit applies and holds the height at 0 without restating the eight pixel
   cell at a call site.

   The sabotage above keeps its menu row as the record of what was measured
   that day; the arm's own row is renamed, because a row labelled `pinned menu
   button 72` over an unpinned button is an instrument describing code it no
   longer measures (L-INSTRUMENT).

   **Measured 2026-08-25, CMUNSS at 1600, `s` = 2, the fitted table** (the
   brand label is the row the rename moves; device pixels). It does not
   contradict item 3's table above it: that one was read at ppem 32, item 4
   halved the ppem to 16, and every `drawn` here is about half its counterpart
   for that reason and not because anything shrank twice.

   | row | chars | reserved | drawn | slack |
   |---|---|---|---|---|
   | brand `COBBLESTONE` | 11 | 108 | 107 | 1 |
   | programs | 11 | 108 | 76 | 32 |
   | files | 8 | 76 | 43 | 33 |
   | edit | 7 | 72 | 40 | 32 |
   | console | 10 | 98 | 65 | 33 |
   | shutdown | 11 | 110 | 78 | 32 |

   The sidebar lays out to 320 either way, so the brand word fits with the
   whole column to spare, and it was looked at at 1024 as well as 1600 rather
   than assumed.

   **Expect no picture change from this alone.** `flex-col-place` still hands
   every child the container's full width and the sidebar is still pinned to
   160 logical, so an honest cell is invisible until a maximum width lands. The
   evidence is `desk-label-metrics`, which now prints the same table twice, the
   second time through `comp-fit-text`; a golden sweep that moves nothing is
   the expected result here and not a pass to be quoted as one.

   **The table, and the command that makes it.** The battery runs this arm
   with no volume, so the recorded `.expected` is the no-volume line and proves
   only that the chapter still compiles and reaches the mount (L-NAMED). The
   numbers below need a disk:

   ```powershell
   build/build-boot-img.ps1 -Out $env:TEMP\face.img -Kernel seed\Codex.cdx
   build/compile.ps1 -Src codex/test/apps/desk-label-metrics.codex `
                     -Out dlm.cdx -Log dlm.log -Kernel seed\Codex.cdx
   build/test-run.ps1 -Kernel dlm.cdx -OutFile dlm.out -DiskFile $env:TEMP\face.img
   ```

   Measured 2026-08-24 with CMUNSS at 1600 wide, `s` = 2, device pixels:

   | row | guessed | drawn | slack | fitted | slack |
   |---|---|---|---|---|---|
   | brand `CODEX` | 80 | 106 | **-26** | 106 | **0** |
   | programs | 208 | 154 | +54 | 186 | +32 |
   | files | 160 | 88 | +72 | 120 | +32 |
   | edit | 144 | 83 | +61 | 116 | +33 |
   | console | 192 | 131 | +61 | 164 | +33 |
   | shutdown | 208 | 159 | +49 | 192 | +33 |

   The residual 32 or 33 on every button is `widget-button`'s own 16 logical
   pixels of padding at `s` = 2, so what was a slack varying from 49 to 72 by
   string is now a constant the theme owns. The label goes to exactly zero,
   which is the direction that was under-reserving. Rounding is UP: sabotaged
   to round down, `edit`, `console` and `shutdown` each lose 2 device pixels
   and the other three do not move, because 106, 154 and 88 are even and 83,
   131 and 159 are odd (L-SABOTAGE: the three that cannot move are the control,
   and they are named rather than counted as a miss).
4. **Small type. DONE.** The face loaded at `comp-glyph-h * ui-wscale`, which
   is ppem 32 at 1600: a 41 row cell with 22 pixel capitals against the 9 to
   10 of the reference. `comp-glyph-h` could not simply be lowered, because it
   is ALSO the CBF fallback's cell height, hardwired to 16 rows in
   `gop-buf-cbf-rows-scaled`. The desk now owns `dk-ui-ppem`, 16 at 1600 and
   above and 14 below, and the fallback keeps its 16.

   **The multiplier was the bug, not the constant.** A ppem is already a
   DEVICE size, so multiplying it by `ui-wscale` is what made the type exactly
   twice what it should be at 1600. The two values are read off a sweep now
   kept in `codex/test/apps/desk-leading-metrics`, which asks the face what
   each ppem costs rather than arguing about the word small (L-ADJECTIVE):

   | ppem | cell | cap | `CODEX` |
   |---|---|---|---|
   | 10 | 13 | 6 | 31 |
   | 12 | 16 | 8 | 38 |
   | 14 | 18 | 9 | 45 |
   | 16 | 21 | 11 | 51 |
   | 20 | 26 | 13 | 64 |
   | 32 (old) | 41 | 22 | 106 |

   **Two constants assumed the old cell and both were visibly wrong the moment
   the type shrank. The pictures found them; no number did.**

   - **The line step.** `dk-line-h * s` = 36 never consulted the face: 36
     against a 41 row cell was too TIGHT, and against a 21 row cell it is half
     again too loose, so the paragraph read double spaced. `dk-line-step` is
     five quarters of `gf-gh` when there is a face, 26 here, and keeps the old
     constant for the bitmap fallback whose cell really is `comp-glyph-h * s`.
     `dk-wrap-draw` had a second hardcoded `18 * s` that would not have moved
     with `dk-line-h` at all.
   - **The icon size.** `desk-icon-in` and `dk-icons-init` both quantised the
     icon to `avail / 8` because the artwork used to be an 8 by 8 stencil
     block-magnified by an integer. A DRAWING is rasterized at any size, so
     the quantiser threw away up to seven of the gutter's pixels: at ppem 16
     the gutter measures 15 and the icons came out at 8, a third smaller than
     the type beside them. `dk-icon-px` is four fifths of the gutter, 12 here,
     the four fifths being air between the icon and the first letter of the
     label. The stencil fallback keeps its integer scale.

   **Both metric arms were instruments measuring code they no longer describe,
   and both were repaired rather than re-baselined** (L-INSTRUMENT).
   `desk-leading-metrics` printed `dk-line-h * s`, a constant the paint path
   had stopped using. `desk-icon-size` carried its OWN copy of `8 * scale` and
   so reported paint 8 against boot 12, which reads as exactly the
   disagreement that chapter exists to catch, while the two real sites agreed.
   A copied expression measures the test.

   **The golden sweep moves all fifteen panes and that is the intended
   reading here**, which is the one case the sweep's own header warns about:
   every pane draws text, so a change to the size of all text reaches all of
   them. What the sweep is good for at this stage is the pictures, and they
   are what found both constants above.

   ### There is a THIRD constant assuming the old cell, found 2026-08-25: `dk-task-h`. DONE.

   **What landed, and it is not what this section first proposed.** The band
   is MEASURED, not computed: `desk-taskbar`'s menu entry asks for one line of
   the face through `dk-task-btn-h`, the layout engine adds the theme's own
   padding, and `dk-task-init` lays the real taskbar out once in `desk-run`
   and caches the device height in `dk-task-band-cell`. No padding constant is
   written down anywhere, which is what the analysis below said was required
   and what a formula could not have given. `dk-task-px` is the one reader,
   and every consumer goes through it: `dk-cbox-h` and the `dk-wnd-*`
   rectangles under it, `desk-mon-draw`, `desk-content-h`, `desk-shot`.

   **Measured 2026-08-25 by `desk-taskbar-hit`**, logical units, synthetic
   face with an 18 pixel cell:

   | | band | entry | entry asks |
   |---|---|---|---|
   | 1024 bitmap | 28 | 0 | 0 |
   | 1024 face | **34** | **22** | 22 |
   | 1600 bitmap | 28 | 0 | 0 |
   | 1600 face | 28 | 11 | 11 |

   **The floor wins at 1600 without a `math-max` anywhere**, because 11 plus
   the padding is under 28 and the engine takes the larger by itself. That is
   the whole reason to measure rather than compute: the behaviour the analysis
   wanted from an explicit maximum falls out of asking the engine.

   **The bitmap rows are the control and they do not move.** That path is not
   broken -- its cell is `comp-glyph-h * ws` and the 16 the entry gets holds it
   -- so `dk-task-btn-h` asks for nothing when `gf-ok` is false. A change that
   moved both would have been a change nobody needed.

   **The face used by the arm is SYNTHETIC and that is what makes this a gated
   assertion rather than a no-volume line.** `gfont-load` needs a volume and
   the battery attaches none. A `GopFont` is a plain record, `gf-ok` true with
   a `gf-gh` of 18 is all `dk-task-btn-h` reads, and the tree is measured
   directly rather than through `desk-chrome-face`, which would run
   `comp-fit-text` and measure text through a glyph base of zero.

   **Both halves of `dk-task-px` are asserted**, because the cache is written
   only by `desk-run` and no gated arm boots the desk: without a row for it the
   reading half would be a path nothing calls (L-UNCALLED). The arm reads 28
   from an unwritten cell and 34 from a written one.

   **A `ds` shorter than 128 bytes cannot hold the cells above 63, and five
   test chapters were allocating 64.** This predates cell 76 -- `dk-icons-cell`
   is 72 and has been read off a 64-byte block since it was added. It never
   showed because the arena is bump-allocated and 64-aligned, so the over-read
   landed on padding, answered zero, and every reader's absent-is-zero branch
   took over, which is indistinguishable from a legitimately empty cell. All
   five allocate 128 now and `works-desk-contract.md` carries the rule.

   **Not changed, deliberately: the legacy `dk-win-*` welcome window.** Its
   band term is `dk-task-h * ui-scale w`, a different scale from the taskbar's
   own `ui-wscale`, so it was already inconsistent before this; it is the
   surface 6.2 superseded and threading half of it through would leave a worse
   mixture than leaving it alone.

   The analysis that produced this follows, kept because the two rejected
   routes are the part worth reading.

   **It was OPEN, and it is the taskbar band.** Measured with a probe that
   lays `desk-chrome-face` out and reads the rects back, CMUNSS on the ESP:

   | | band | menu entry | glyph cell | sidebar button |
   |---|---|---|---|---|
   | 1024, no face | 28 | 16 | -- | 24 |
   | 1024, face | 28 | **16** | **18** | 24 |

   The entry is 16 logical tall and one line of the face is 18, so it clips
   its own descenders at every width below 1600. `dk-task-h` is 28 and the
   root panel's padding takes 12 of it, which is where the 16 comes from; the
   entry carries `min-h` 0 and so shrinks to whatever the band leaves.

   **It is PRE-EXISTING and the rename did not cause it.** The same crop off
   a build of the DEPOT chapter clips `Codex` exactly as the renamed build
   clips `Cobblestone` (L-CONTROL: the arm was built from the depot, not from
   the shelf). What the longer word did was make it easy to see.

   **Why it survived item 4, and this is the part worth keeping.** The band
   is CORRECT at 1600, and 1600 is the width every capture in this campaign
   is taken at. `ui-wscale` is 2 there, so the 28 logical band is 56 device
   against a 21 device cell and there is room to spare; at 1024 the scale is
   1, the band is 28 device, and the cell is 18. Every arm and every
   screenshot agreed because they all shared one width (L-GAP, and the same
   shape as `ui-scale` and `ui-wscale` agreeing at 1600 that hid the sidebar
   edge in item 5).

   **One logical constant cannot serve both scales, which is what makes this
   more than raising 28.** The cell comes from `dk-ui-ppem`, 14 below 1600
   and 16 at or above it, and a ppem is a DEVICE size that does not halve
   when `ui-wscale` doubles. In logical units the cell is therefore about 18
   at `s` = 1 and about 11 at `s` = 2. A single constant large enough for the
   first is half again too tall for the second, which is exactly the
   too-tight-then-too-loose that `dk-line-h` was retired for. So the band has
   to follow the face the way `dk-line-step` and `dk-icon-px` now do, and
   `dk-task-h` becomes its floor rather than its value:

   ```
   dk-task-band tf ws = math-max (dk-task-h * ws) (dk-line-step tf ws + pad)
   ```

   Taking the larger keeps 1600 EXACTLY where it is and moves only the widths
   that are broken, which is what holds the golden sweep to the panes that
   actually change.

   **The cost is twelve call sites, and `tf` is the reason.** `dk-task-h` is
   read by `desk-taskbar`, `dk-cbox-h` and through it every `dk-wnd-*`
   rectangle, `dk-win-fits`, `dk-win-y`, `desk-mon-draw`, `desk-content-h`,
   `desk-shot`'s toast, `desk-bro-h`'s fallback, and two test chapters.
   `dk-win-fits`, `dk-win-y` and `desk-mon-draw` already carry a `GopFont`;
   `desk-taskbar`, `dk-cbox-h`, `desk-content-h` and `desk-shot` do not and
   would each grow a parameter.

   **`pad` must not be written as a number, and the tree already says so.**
   12 is what the DEFAULT scheme's root panel takes; `desk-content-h`'s own
   prose records that padding is a THEME's choice, `edges-uniform 4` in one
   scheme and `edges-xy 16 8` in another, and that an arithmetic agreeing
   with the engine under the default wraps at the wrong column under `lcars`.
   `desk-bro-h` reached the same conclusion from the other side and MEASURES
   the band off the laid tree, keeping `dk-task-h` only for a tree that has
   no band at all. Whatever lands here has to take the padding from the
   theme or from a laid rect, or it buys a correct default and a wrong
   `lcars`.

   **The arm to change, and it is an instrument pinned to the wrong thing.**
   `desk-taskbar-hit` asserts `band height is dk-task-h`, which is a
   statement about a CONSTANT and goes red for the fix rather than for a
   defect: when the entry's `min-h` was lifted during the rename the band
   laid out to 36 and that row went red, and 36 was the correct answer. It
   should assert the contract instead -- that the entry is at least as tall
   as a line of the face, and that the band did not shrink below its floor --
   which is measuring the artifact against what it promises rather than
   against a number (L-BOTHARMS).
5. **Panes stop slicing the sidebar. DONE.** WORKS-35's class. Four chapters
   restated where the sidebar ends and no two agreed: `dk-sidebar-w *
   ui-scale` in the welcome window, a bare `dk-mon-x = 160` in the Monitor
   pane, `90 * s` in the Files pane (and through it the Editor and the desk's
   own diagnostic row), and `gsc-sidebar-w * ui-wscale` in the 3D pane, which
   was the only one that matched what the widget tree lays out. That chapter's
   prose already recorded the bug it had been bitten by; nothing carried the
   fix to the others.

   **`ui-scale` steps at 1024 and `ui-wscale` at 1600, so they AGREE at 1600
   and disagree from 1024 to 1599, and that is why this survived**: every
   wrong version was right at some width. At 1600 the sidebar is 320 device
   pixels and the Files pane began at 180, painting over its last 140 and
   cutting every sidebar button in half, on the width every capture is taken
   at.

   `ui-sidebar-px` in `GopDraw` answers it once, in DEVICE pixels. It lives
   there rather than in `GopDesk` because `GopDesk` cites the panes and not
   the other way round, and that chapter already owns both scales.
   `gsc-sidebar-w` and `dk-mon-x` are gone; `gfl-px` and `dk-sidebar-w` are
   defined in terms of it; `desk-mon-row` grew the `w` it needs to ask.

   **The arm is `codex/test/apps/desk-pane-origin`, and both halves of it
   matter.** It compares each pane's origin against `ui-sidebar-px` AND
   `ui-sidebar-px` against the width `widget-measure` actually lays the
   sidebar out to, because the first comparison alone is the instrument built
   from its subject once every pane calls the same function. It runs at 1280
   and 1600 for the reason above: a single-width arm passes with any one of
   the three wrong versions still in place. Sabotage measured 2026-08-24,
   `gfl-px` restored to `90 * s`: both rows go to `agree NO`, 180 against 160
   at 1280 and 180 against 320 at 1600. It needs no disk, so unlike the other
   desk metric arms it carries a real expectation rather than a no-volume
   line.

6. **Windows. IN PROGRESS.** Damian's ask, 2026-08-24, in his words:
   overlapping windows with focus control and alt-tab, standard
   close/minimize/maximize, and **edge docking** -- flick a window by its
   titlebar and it sticks to the edge of the desktop in the direction
   flicked, as a PILL carrying the app name, which pops the window back open
   on click. Hovering the pill shows a mini-preview of the page in a bubble.

   `Window.codex` already carries the bookkeeping: open, close, focus with a
   z bump, move, resize, tile, find. `WsMinimized` and `WsMaximized` are
   DECLARED and never set. `Surface.codex` is the z-sorted compositor with
   the hit test `Window` lacks. So the seventh instance of the campaign's
   pattern: mostly written, never switched on.

   ### 6.1 An offset render path. DONE.

   `comp-render` laid out at a hard-coded `0, 0`, so no pane could paint into
   a rectangle that is not anchored at the screen corner. `comp-render-at`
   takes an origin and `comp-render` is the `0, 0` case, so no existing
   caller moves.

   **The origin goes into the LAYOUT RECT and not into the paint, and that is
   what keeps the hit path working.** The bounds that come back are then
   absolute logical coordinates, so `comp-hit-x` and `comp-hit-y` need no
   origin of their own. This design said the opposite before it was measured
   ("the hit path needs the matching change or every hit test against an
   offset tree breaks silently"); it does not, and the arm says so rather
   than the sentence.

   The arm is `codex/test/apps/desk-window-origin`: geometry (every child
   moves by exactly the origin), containment (nothing paints outside the
   granted rectangle), and hit (a device point inside the offset window finds
   the offset child, and the SAME point against the same tree laid at the
   corner finds nothing, which is what says the hit is origin-sensitive at
   all rather than agreeing by luck).

   **The containment row read 0 twice before it meant anything, and both
   readings looked like a pass** (L-FALSIF). With a tree that FITS its
   window, widening the clip to the whole buffer changes nothing, because
   layout was keeping the paint in and the clip was never consulted. With a
   tree overflowing past the END of the buffer, the widened clip faults
   rather than reporting. Only a tree whose overflow lands inside the buffer
   and outside the grant can move the number: 0 with the clip, 3,708 pixels
   without it.

   ### 6.2 The first window. DONE.

   The CLOCK, which pushes no mark and keeps its state in a `ds` block
   allocated in `desk-run` below the base mark, so no restore can reach it.
   `dk-wnd-paint` draws the chrome with an EMPTY content slot, then the
   window body, border, titlebar and buttons, then the pane's own tree
   through `comp-render-at`. Focus does not move and the desk still steps
   exactly one pane.

   Everything in the frame is DEVICE pixels, because a window rectangle is a
   screen fact and the desk's two scales disagree below 1600. The window is
   three quarters of what the chrome leaves, centred, so the desktop shows
   around it. The titlebar is the FACE's cell plus padding rather than
   `dk-title-h`, so it follows the type rather than staying at the 40 device
   pixels that constant gives at this scale.

   **The three buttons are drawings, and the close mark is the one shape in
   the icon set whose form is a crossing.** Two overlapping bars would cancel
   under the even-odd fill and an X would come out holed exactly where the
   arms meet, so it is authored as a single twelve-vertex outline. The
   maximise frame is the opposite case and is allowed, because a box inside a
   box is NESTED, which is the same rule that makes a ring. `gicon-vector`
   asserts the pixel that distinguishes them: the close centre reads 255 and
   the maximise centre reads 0. The area census alone could not have said so,
   because a cancelled centre is a smaller number in a range where every
   number is plausible.

   The buttons carry the icon set's ONE rasterized size centred in their
   square, rather than a second blob at the titlebar's size.

   `dk-wnd-hit` answers none, min, max, close, bar or body, and the buttons
   are resolved BEFORE the bar, or a click on close would read as a drag of
   the window it is closing. Close is wired; min and max are 6.3.
   `codex/test/apps/desk-window-frame` asserts the six codes are distinct and
   the button order is min, max, close from the left, at 1280 AND 1600, since
   the rectangle derives from `ui-sidebar-px` and inherits its scale steps.

   **Not in 6.2:** two panes stepping in one `desk-loop` iteration, a heavy
   pane floating, or the Browser as anything but topmost.

   ### 6.3 Focus, alt-tab, and the titlebar buttons

   `wm-focus` already bumps z and unfocuses the rest, so alt-tab is a cycle
   over `wm-windows` in z order calling it. Close, minimize and maximize are
   three hit targets in the titlebar plus the two `WindowState` arms nothing
   sets today. Maximize keeps the restore rectangle so it can be undone;
   minimize is the same state a docked pill shows, which is why 6.4 follows
   rather than being independent.

   **6.3 IS THREE ITEMS AND ONLY ONE OF THEM IS BUILDABLE TODAY**, which the
   paragraph above hides by listing them in one breath. Sorted 2026-08-25
   against what the desk actually has:

   - **6.3a, maximize and restore. DONE.** Needs one window and nothing else.
   - **6.3b, minimize. DONE**, with 6.4's first unit, because it was blocked
     on exactly one thing: a minimised window has to go somewhere and the pill
     is where. The diagnosis here was right and so was the warning. The
     taskbar's `tasks` slot is NOT it: it reads the mark stack, so only the
     five HEAVY panes can ever appear there and the Clock pushes no mark by
     design. The pill is a SECOND register beside it, read from the window
     registry, and the two answer different questions -- alive-as-an-app
     against minimised-as-a-window -- so both are shown.

     `dk-wnd-st-min` is the third window state. A minimised window stays in
     the registry and keeps its rect; it is simply not drawn, and focus skips
     it (`dk-wr-visible`, which is `dk-wr-top` filtered). Restoring clears the
     state and raises, and needs no kept rectangle because 6.3c made the
     normal rect a fact in the pane's own block -- the window returns to where
     it was, cascade offset included.

     **The pill is a BUTTON IN THE TASKBAR TREE, not something painted into
     the band**, and that is forced rather than chosen: `desk-taskbar-clock`
     re-renders the whole taskbar subtree once a second to repaint the clock,
     so anything drawn into the band from outside the tree is erased within a
     second of appearing. Being in the tree also gives the pill its hit test
     for free, so click-to-restore cannot drift from where the pill is drawn.
     Its id carries the focus id because a label cannot: two panes could
     reasonably share a title and a restore has to name a pane.

     **Minimise leaves through `desk-step-hide`.** That is the shipped path
     that rebuilds the chrome tree (so the pill exists at all) and leaves the
     pane ALIVE. `desk-app-hide` gained one branch: after redrawing the
     desktop it hands focus back to the top still-visible window and paints
     the windows over the fresh chrome, so minimising one window does not put
     the others away.

     **A repaint under a shown cursor is undone by the cursor's own restore**,
     and leaving `cursor-hide` out of the restore path left a fragment of the
     pill on the band. Caught in a capture, not by an arm.
   - **6.3c, focus and alt-tab. DONE.** It was blocked on a second window;
     what follows is the account of what that turned out to mean. 6.2's own
     "not in" list says two panes stepping in one `desk-loop` iteration is not
     built, and alt-tab over one window is an instrument that cannot fail
     (L-FALSIF). ONLY THE FOCUSED WINDOW STEPS, which is what let the second
     window arrive without that item: an unfocused window is PAINTED and does
     not run, so `desk-focus-cell` still names exactly one stepping pane and
     the registry names what is drawn. This was also supposed to be where
     `WindowManager` finally gets a caller:
     measured 2026-08-25, `wm-new`, `wm-open`, `wm-close`, `wm-focus`,
     `wm-move`, `wm-resize`, `wm-tile`, `wm-find` and `wm-focused` have ZERO
     call sites outside `codex/test/forewords/ui-window` (L-UNCALLED). The
     desk does not use the manager at all; it paints one window from
     `dk-wnd-*` geometry. So 6.3c is "wire the manager in", not "add a cycle".

     **THE RECTANGLE HAD TO BECOME A FACT FIRST, AND THAT IS DONE.** Every
     one of `dk-wnd-x`, `dk-wnd-y`, `dk-wnd-w` and `dk-wnd-h` derived the rect
     from the screen width and the window state alone, so a second window
     landed on exactly the same pixels as the first and no hit test could tell
     them apart. A window's normal rect is now four cells in the pane's own
     block (`works-desk-contract.md` names the slot), read through
     `dk-wnd-rx`/`ry`/`rw`/`rh`, with width zero as the never-placed sentinel
     so an untouched block still answers the old arithmetic. That is the
     obligation 6.3a recorded coming due: the first caller that moves a window
     makes the normal rect a fact somebody has to keep.

     `dk-wnd-hit`, `dk-wnd-btn-x` and `dk-wnd-paint` now take the rectangle
     instead of deriving it, which is what lets one screen carry two of them.
     `desk-window-frame`'s first ten rows are byte-identical across the change
     and are the calibration.

     **WHAT `WindowManager` CAN AND CANNOT BE, because "wire the manager in"
     hides a constraint the desk has already settled everywhere else.** It is
     a functional record: every `wm-focus` and `wm-open` returns a new one.
     Durable desk state may not live in a value like that. Section 2 of the
     contract is explicit -- a cell holding a pointer is allocated in
     `desk-run` before the base mark -- because `desk-app-close` restores to
     that mark and rebuilds `apps` fresh afterwards, and a manager threaded
     through `desk-loop` would be a dangling pointer from the first pane exit.

     So the manager cannot BE the registry. The registry is a block below the
     base mark.

     **6.3c IS DONE. And the second half of that paragraph was wrong, so it is
     corrected here rather than quietly dropped.** It proposed materializing a
     `WindowManager` from the block per frame, using it for the focus and z
     queries, and writing it back, on the reasoning that this item was
     supposed to give `Window.codex` its first caller. Building it settled the
     question the other way, and the reason is the one L-LESS names: the
     machinery would exceed the cost it manages.

     The registry is an array of focus ids in a block, and THE ARRAY ORDER IS
     THE Z ORDER -- entry 0 is the bottom, the last entry is the top, the top
     is focused. That representation deletes `win-z` outright, along with every
     question about two windows holding the same z, and it makes `wm-focus`'s
     whole job -- bump the z, unfocus the rest -- into `dk-wr-raise`, which
     slides the entries above one index down and writes the id at the end. Six
     lines, no allocation. Materializing a `WindowManager` to do the same would
     allocate a `List Window` of records inside the frame bracket and then need
     a second walk to write the answer back.

     **So `Window.codex` had no caller and could not get one, and it is now
     DELETED** (red's ruling, 2026-08-25, with `codex/test/forewords/ui-window`
     and the three cites that never used it -- `Browser`, `fontexplorer`'s
     opening, and `foreword-all-compile`). It was a heap-shaped design in a
     bump-allocated desk. L-UNCALLED plus this item settling that the only
     designed caller cannot exist is the whole case; a future heap-owning
     caller refetches it from history. Reachability measured before deleting:
     no chapter under `codex/compiler` cites it, and `concat-codex-self`
     preloads only CITED foreword chapters and only from `codex/foreword/core`,
     so it is not in the seed's compilation unit at all. Confirmed by the gate
     rather than left as an argument -- `build/output/Sut.cdx` came out
     byte-identical to the workspace seed, so the deletion carries no seed and
     took no build token. **Compare Sut against YOUR OWN workspace's seed for
     that question, not against main's**: main took a seed-affecting change
     from another lane during the run, so the comparison against main said
     "moved" while the honest answer was that nothing of mine had moved it.

     What 6.3c actually shipped: the registry, the Calculator as the second
     windowed pane, painting every open window bottom to top with the focused
     one drawn last and its titlebar in `pal-primary`, Tab to cycle,
     click-to-focus resolved from the top down, close handing focus to the
     window underneath instead of leaving for the desktop, and the cap
     refusing a fifth window rather than evicting one.

     **The key is Tab, not Alt-Tab, and the design should stop saying alt-tab
     without saying so.** The desk tracks no modifier state: `kbd-take` answers
     a scancode and nothing accumulates a shift or an alt across calls, so an
     Alt-Tab today would be a claim the keyboard layer cannot support. Making
     it Alt-Tab is a keyboard-layer item, not a shell item.

     `codex/test/apps/desk-window-registry` is the arm. It is pure arithmetic
     over one block, so the whole of focus, stacking, opening, closing and the
     cap is decidable without a screen, a face or a volume, and every row
     prints THE WHOLE ARRAY rather than the count and the top: a row printing
     only the top would agree with an implementation that dropped every window
     beneath it, and one printing only the count would agree with one that
     stacked them backwards.

   ### 6.3a Maximize and restore. DONE.

   `dk-wnd-st-normal` and `dk-wnd-st-max` are the state and the four rect
   functions take it. Maximised is the content box exactly, so `dk-wnd-x` and
   `dk-wnd-y` need no arm of their own: they centre whatever size comes back
   and at the full size that offset is zero. The state lives at offset 44 of
   the clock pane's own block rather than in a `ds` cell, because it is a fact
   about this pane's window and the next pane to get one keeps its own;
   `works-desk-contract.md` carries the block's layout.

   **Nothing stores a restore rectangle and that is correct only while the
   normal rect is COMPUTED.** Restoring recomputes three quarters of the
   content box. `wm-move` and `wm-resize` exist and are called by nothing; the
   first caller makes the normal rect a fact about what the operator did
   rather than arithmetic, and from that moment restoring has to read a
   rectangle somebody kept.

   **The arm's discriminating row is one point in SCREEN coordinates**, and
   the reason is that every other row in `desk-window-frame` reads the rect
   and then asks about a point derived FROM that rect, so it travels with the
   window and would answer the same thing if maximising did nothing at all.
   The fixed point sits just inside the content box's top-left, on the
   titlebar row a maximised window would have there: maximised it answers
   `bar` (4); normal the window is inset by an eighth of the box, the same
   point is outside it, and it answers `none` (0). Sabotage measured
   2026-08-25 with both size functions made to ignore the state: `maximised is
   the content box` and `the two rects differ` both go to `no` and the point
   goes 4 to 0, at 1280 AND 1600. `above the taskbar` stays `yes` under that
   same sabotage and is kept as the row the defect cannot move.

   ### 6.5 Every pane a window. Damian, 2026-08-25, on seeing 6.3b run

   *"i like the minimize and maximize work. but only for a handfull of the
   apps. i think they all should do that."*

   Two panes are windowed today, the Clock and the Calculator, and they were
   chosen because they were the cheapest two rather than because the rest are
   different in kind. Most are not. **INVENTORIED BEFORE ESTIMATING, because
   "fourteen panes" is a count standing in for a shape (L-ADJECTIVE) and the
   groups below need very different work.**

   A pane becomes windowed by taking five things, and item 2 is what sorts
   them: a state block with the window slot (44 through 60) free; an arm in
   `desk-wnd-tree` returning a `WidgetNode`; its step routed through
   `desk-wnd-chrome-step`; its paint through `desk-wnd-paint-all`; and its own
   hit test laid out at the window's content box rather than at the screen
   corner, which is the defect `dk-calc-hit` carried in 6.3c.

   **THE CONSOLE IS DONE and it cost four of the five, not five.** Its block
   leaves 44..60 free as the table says, its tree is a pure function of that
   block, and it has no hit test of its own -- it reads keys and never a
   click -- so the fifth thing, the hit test moved to the content box, was
   not there to move. The step gained the `clicked` parameter anyway, because
   `desk-wnd-chrome-step` needs it to resolve a click on the titlebar. Tab
   was free: `gcon-key` already answered nothing for it, since `apply-mods`
   gives scancode 15 a character below 32 and the type arm refuses those.

   **THE LAUNCHER IS DONE TOO, and it cost SIX, which is the correction this
   table needed.** Two of them are not in the five and neither was foreseen.
   `desk-wnd-over` is a fourth per-pane fact: the launcher's icons ride the
   LAID tree, the painter now renders every window and hands the laid trees to
   nobody, so a pane that paints over its own tree needs a hook at the moment
   its window is drawn. And `dk-wnd-wants-box` is a first placement of the
   whole content box, because **the launcher's tree does not fit three
   quarters of it: fourteen entries in the tree, twelve on the screen, the
   Review pane and the Browser clipped away with nothing saying so.**

   **NO HIT TEST COULD HAVE CAUGHT THAT, and that is the part to carry
   forward.** `widget-layout` places a child past its container rather than
   clipping it, so `codex/test/apps/desk-prog-click` reports all fourteen
   reachable under either rect. The layout and the paint disagree and only the
   paint is what a person can click. **So the question to ask of every pane
   below is not whether its tree lays out, it is whether its tree FITS three
   quarters of the content box** -- and the answer is a picture, at 1280 and
   at 1600.

   The hit test was the other half and it does fail loudly enough to arm.
   Sabotaged back to the corner layout, a click on the launcher's Calculator
   row opened the AQUARIUM: a real application, no error, and exactly the
   wrong-but-plausible answer a screenshot cannot show.

   **REVIEW IS NOT A GROUP A PANE AND THE TABLE WAS WRONG TO SAY SO.** The
   group's test is a tree that is a pure function of a block. `grv-tree` takes
   `GrvData`, which `grv-load` builds by mounting the volume and scanning every
   proposal, verdict and supersession on it, plus a signer from
   `loaded-fingerprint`. Two consequences and each is disqualifying on its own.
   `desk-wnd-tree` would gain `Identity` and the row would have to widen through
   the painter and every step that calls it. And the painter builds the tree of
   EVERY open window on every repaint, so a Clock ticking beside a Review window
   would scan the medium once a second and allocate three lists inside the frame
   bracket to do it. **The tree is a function of the MEDIUM, not of a block, and
   that is a third shape this table does not have a group for.** Making it work
   means caching the `GrvData` where the painter can reach it, which is a
   `DeskApps` field and a signature change through the window machinery, not
   five lines.

   **GROUP B's FIRST TWO ARE DONE, and the block was the cheap half.** The
   Calendar and the Diffusion pane took `ds` 84 and 88 and a 64-byte block
   each, which is exactly what this table predicted. What it did not predict is
   the two findings below, and both of them are about SIZE rather than state.

   **THE BIGGER SCREEN IS THE TIGHTER ONE, BY NEARLY HALF.** A tree is laid out
   in logical pixels and `ui-wscale` is 1 below 1600 and 2 at or above it, so
   the whole content box goes from 1120 by 728 logical at 1280x800 to 640 by
   400 at 1600x900, and three quarters of it goes from 840 by 546 to 480 by
   300. Every instinct says to check the small size; both panes lost their last
   line at the large one and showed it at the small one. **Look at every
   remaining pane at BOTH sizes and expect 1600 to be where it breaks.**

   **AND A FULL-BOX WINDOW IS STILL ONE TITLEBAR SHORT OF THE PANE IT
   REPLACES.** `dk-wnd-wants-box` gives a pane the content box, and the window
   then spends `dk-wnd-bar-h` of it on its own titlebar, so a tree that exactly
   filled the region overflows by about a line the moment it is windowed. A
   depot-built control is what separated that from something I had done: the
   Diffusion pane painted its last line cleanly before the change and clipped
   it after, with the box. The repair was to DELETE the line rather than find
   room for it -- `Esc returns.` is a hint about the only way out, and a
   windowed pane has a close button, a menu and Esc. Expect that trade on every
   pane whose tree was written to fill the region.

   **APPEARANCE IS DONE and it fits a three-quarter window at both sizes**, so
   it is not in `dk-wnd-wants-box` -- the first converted pane that does not
   need the whole box. What it needed instead was an arm: `dk-style-hit` had no
   test at all, and its wrong answer is the quietest of any pane so far.
   Sabotaged back to the corner layout, a click on the colour-scheme row
   answers ROUNDED CORNERS: the pane repaints, a setting changes, and it looks
   exactly like a click that worked. `codex/test/apps/desk-style-click` moves
   two rows under that sabotage and its `reachable` sweep moves neither.

   **ISSUES CLOSES GROUP B, AND IT IS THE FIRST PANE WHOSE TREE IS A FUNCTION
   OF THE ROOM IT WAS GIVEN.** The table is built at the height the layout
   engine measured for it (WORKS-23), so `desk-wnd-tree` had to learn `w`, `h`
   and `tf` to find the WINDOW's content box instead of the pane's.
   `desk-wnd-one` is its only caller and had all three, so the widening was
   three lines; that is the honest reason Review is disqualified and this is
   not, rather than anything about how much state either keeps. A table
   measured against the whole chrome pages for a height nothing draws at.

   **AND IT WANTS THE WHOLE BOX, MEASURED AT BOTH SIZES RATHER THAN
   PREDICTED.** Nothing was ever clipped: `data-table-fit` pages instead, so
   the failure is not a lost line but a lost table. At 1600x900 in a
   three-quarter window the pane read `Page 1 of 7` with two rows on it and
   every cell truncated to `CD...` and `In Pro...`; with `dk-wnd-wants-box` it
   reads `Page 1 of 4` with four rows and full ids. At 1280x800 the same
   change goes from eight rows to thirteen. **A pane that pages rather than
   clips hides a bad fit from every arm AND from the eye that is looking for a
   clipped line** -- the picture answers it only if you read what it says
   rather than checking the edges.

   | group | panes | what it needs |
   |---|---|---|
   | **A. ready** | ~~Console~~, ~~Programs~~, **Review is NOT in this group** | tree-painted, light, and their blocks already leave 44..60 free. The five lines and nothing else |
   | **B. needs a block** | ~~Calendar~~, ~~Diffusion~~, ~~Appearance~~, ~~Issues~~ | tree-painted and light, but no state block at all: the quiet panes keep no state and Issues uses two BARE `ds` cells (32 and 40) rather than a block. `ds` 96..124 is free, which is eight cells |
   **THE MONITOR IS DONE, AND GIVING A DRAWING PANE A TREE COSTS EXACTLY WHAT
   ITS DRAW COULD REACH.** Absolute `gop-draw-text` reads whatever its caller
   had; a tree is built by the painter, which holds the screen and `ds` and
   nothing else. Its eleven rows sorted into three kinds and the sorting is
   the rule for the two panes left. What the tree can compute for itself stays
   in the arm: the RTC, the ACPI walk, the heap frontier, the stack cells.
   What is a fact about the MACHINE that will not shrink into 32 bits joins
   `desk-wnd-tree`'s arguments, which is why it now takes `base` and `stride`:
   a framebuffer address is 64 bits, there is no `poke-qword`, and a truncated
   address in the one pane whose job is to report the address is a lie exactly
   where it costs most. What is settled before the base mark and cannot change
   afterwards goes in the block, which is `kbd` and `mouse`, two booleans not
   worth threading through nine call sites.

   **THAT LAST KIND IS THE ONE THAT FAILS QUIETLY, and it is why the arm is a
   DIFFERENCE.** An unwritten block cell reads zero and zero here is `mailbox`
   and `no`, which is precisely what a machine with no USB HID keyboard and no
   mouse reports: the pane says the hardware is absent, confidently, and no
   picture can tell you which it is. `codex/test/apps/desk-mon-block` builds
   the same tree over a written block and an unwritten one and asks them to
   disagree; sabotaged to answer from literals, both read `USB HID` and the
   row goes to no. It needs no window and no hit test, because this pane has
   never had one -- it reads keys, like the Console, so the fifth thing was
   not there to move.

   It wants the whole box: at 1600x900 a three-quarter window drew nine of the
   eleven rows and cut `memory` and `stack` off, which is the heap instrument
   this campaign has been taking every reading with.

   **GROUP C IS CLOSED. THE TWO 3D PANES TOOK THE OTHER ANSWER THE TABLE
   OFFERED: a windowed variant of the draw, taking an origin and a size.**
   They already had one and nobody had noticed. `ScenePane` carried a
   `GpuView` with an x, a y, a width and a height, a framebuffer row pointer
   and a content height, all four computed once in `gsc-new` from the desk's
   chrome constants -- a hand-synced copy of the desk's own sidebar, top bar
   and taskbar sizes. Replacing that derivation with the window's rect is the
   whole of the change, and `gsc-place` is the four writes.

   **THE RESIZE QUESTION IS ANSWERED WITHOUT SPENDING ANYTHING, and the
   answer was in `Renderer3D` already.** `r3d-target-at` models a target whose
   row stride is independent of its width, so the colour and depth buffers are
   allocated ONCE at the content box, the largest rectangle a window here can
   take, and a smaller window renders the top-left of them. `gsc-blit-rows`
   gained the source stride that makes that a copy of a sub-rect rather than
   of a buffer. So maximise and restore rebuild two small records and touch no
   buffer: the alternative, a target reallocated per resize, strands about 4 MB
   above the pane's own heap mark until it closes, which is 6.4's LIFO tail
   reached by pressing a button rather than by docking.

   **The arm nearly shipped an overclaim.** `codex/test/apps/scene-place` first
   asserted that placing allocates NOTHING; it allocates 1,392 bytes for two
   placements, because a `GpuView` and an `R3dTriState` are records and records
   are heap. Per frame that is inside `desk-loop`'s bracket and gone. The
   assertion that survives is the comparison that matters -- 1,392 against
   256,000 for one buffer at the test's size -- and the sabotage that moves it
   is instructive on its own: forcing the target's width back to the buffer's
   moves the SMALL row and leaves the full-box row agreeing, because at the
   full box those two numbers are equal. The row that catches it is the one at
   a size nothing else is measured at.

   | **C. no tree to give** | ~~Monitor~~, ~~3D View~~, ~~Aquarium~~ | they DRAW rather than build a tree -- Monitor writes rows through `gop-draw-text`, the two 3D panes render a framebuffer. `desk-wnd-tree` has nothing to return. Each needs either a tree or a windowed variant of its draw that takes an origin and a size |
   **GROUP D IS STARTED: THE BROWSER IS A WINDOW, and the stranding decision
   did NOT have to be made first.** Windowing a heavy pane
   changes where it paints, not how long it lives: the mark discipline,
   `desk-marks-push` at open and the restore at close, is untouched, and the
   Browser's own rule that it may only ever be the topmost heavy pane still
   holds. So the 3 MB tail 6.4 measured is a question about DOCKING and about
   `desk-marks-reclaim` popping from the top only, and it blocks the pill and
   the flick rather than this.

   Two things came out of it. **`dk-wnd-content` is the content box, once**,
   after four copies of that arithmetic had been written out and the Browser
   wanted a fifth; and **a heavy pane must repaint itself when the chrome
   answers `stay`**, because a raise, a Tab cycle or a maximise repaints every
   window and draws this one's empty body panel. A 3D pane recovers on its next
   frame. A pane that only redraws on an event has no next frame, so it would
   sit blank looking exactly like one that had crashed.

   **`desk-bro-h` is deleted and `browser-pane-fit` now measures the window
   content box.** That function computed the pane's bottom from the taskbar
   band and nothing but the arm called it any more; re-pointing the arm rather
   than softening it is the L-INSTRUMENT repair, and the numbers move
   accordingly -- the box is 960x552 at 480,192, ending 84 device pixels above
   the band, and the tree reaches 430 past it, which is the scrolled page and
   what `gbr-paint`'s clip is for.

   **THE EDITOR'S BLOCK COLLISION IS CLEARED, and it was bigger than this
   table said.** The row named 56, 60 and 64. Grepping the block rather than
   reading `ged-init` found a fourth: **the filename buffer at bytes 44 to
   55**, written by `poke-byte es (44 + n)` in the naming mode, which is
   `dk-wnd-st-cell` and the first two words of the rectangle. Nothing had
   recorded it because nothing except that mode reads those bytes and the mode
   had no test -- `ged-name-push` and `ged-name-of` had never been exercised by
   anything (L-UNCALLED).

   All four moved above 108 and the block is 192 bytes rather than 128, in
   `desk-run` and in `annot-write`, which builds one. `codex/test/apps/edit-block`
   is the arm and the shape of it is worth keeping: **the assertion is a
   CENSUS of bytes 44 to 67 and the two round trips beside it are the
   control.** Sabotaged by putting the filename buffer back at 44, the census
   reads 5 and both round trips are unmoved, because a buffer works perfectly
   at either offset and only the span can see that one of them is sitting on
   the desk's rectangle.

   **THE PANEL GEOMETRY TAKES A RECT, WHICH IS THE SWEEP THIS CAMPAIGN HAD
   LEFT.** `GopFiles` answered where the Files panel was in four functions of
   the SCREEN and `GopEdit` called them, so three chapters agreed about one
   rectangle by each doing the same arithmetic -- the shape WORKS-35 already
   paid for once with the sidebar constant. The four are deleted; every
   drawing and hit function in both chapters takes a `LayoutRect`, and the
   desk says which one it is: `dk-pane-box` for a pane that owns the desktop,
   `dk-wnd-content` for one that is a window.

   **Neither pane is a window yet and that is deliberate.** This lands the
   parameter and hands both panes exactly the rect they had, so the change is
   provably inert: `codex/test/files-parse` and `codex/test/apps/desk-pane-origin`
   are UNCHANGED across it, with the rect spelled out in the first as exactly
   what the four deleted functions computed at 1280x800, and the Files and
   Edit panes photograph identically. Windowing them is now handing them the
   other rect.

   Two functions in each chapter carry the screen as well as the box, because
   `cursor-update` clamps the pointer to the glass. That is the honest reason
   and it is written beside them rather than left to look like a leftover.

   **6.5 IS DONE. EVERY PANE IS A WINDOW, all fourteen.** Files and the
   Editor were the last two and they took the rect the sweep above had already
   made a parameter, plus a `ds` cell for Files. The Editor needed no cell:
   its slot is 44 through 60 of the block that grew to 192 bytes for exactly
   this.

   **THE LAST FINDING IS ONE THE OTHER TWELVE COULD NOT HAVE SHOWN: a
   converted pane must not keep its own title bar.** Both of these drew one
   through `gfl-draw-frame`, with a caption and a close box, and under the
   desk's titlebar that is two bars saying the same word and two `x`es at
   different heights meaning different things. The bar stays and is a LOCATION
   now -- the window says which application, the bar says which directory or
   which file, which is the one thing the window title cannot know.
   `gfl-close-hit` is deleted rather than left unreachable, and the capability
   that went with it is written down rather than left to be noticed: that `x`
   returned from a preview to the listing and dismissed three of the editor's
   modes. Esc does all four, Enter does the first, and the window's close
   button closes the pane. `codex/test/files-parse` loses exactly one row for
   it and every other row is unchanged.

   | **D. heavy, and the stranding decision** | ~~Files~~, ~~Editor~~, ~~Browser~~, ~~3D View~~, ~~Aquarium~~ | these push a heap mark. 6.4's measurement applies: a docked heavy pane costs 1.6 to 2.1 MB and closing one beneath a live one strands about 3 MB. Files and the Editor also draw directly (zero `comp-render` sites), and **the Editor's block collides with the window slot** -- it already uses 56, 60 and 64 |

   So the honest order is A, then B, then C and D together, because C and D
   overlap on the two 3D panes and both need a decision rather than typing.

   **`dk-wr-max` HAS RISEN, to 15, and finding out why it was 4 was the work.**
   It was never a choice about how many windows a person may have: the default
   rect leaves an eighth of the content box as margin, and at 1280x800 that is
   91 device pixels vertically against a step of 24, so the fifth window's
   offset of 96 left the box. The cap was that quotient. The cascade now wraps,
   each axis in its OWN room, and 15 is the registry block's own bound -- 64
   bytes, four for the count and four an entry. Wrapping both axes together
   would have been worse than not wrapping at all, because the window after the
   wrap lands exactly under the first; separate spans, 6 and 4 at 1280x800,
   repeat only after twelve. Measured at 1280x800 and at 1600x900: every one of
   the fifteen offsets is inside both margins, and twelve of the fifteen
   positions are distinct at both sizes.

   **What still bites the moment many are open is the pill row.** It is built
   from the registry into a flex row beside `tasks`, so a taskbar holding a
   dozen pills needs the band to answer what happens when they do not fit --
   which is the same question `dk-task-h` is already open on.

   **What must NOT be assumed: that windowing a pane is free once it has a
   tree.** Every pane that hit-tests its own subtree does so with its own
   layout call, and fitting the paint side alone puts clicks on the wrong
   widget, silently. That is one measured defect per pane, not a sweep.

   ### 6.6 The sidebar is gone and the pill is a start menu. DONE

   Damian, 2026-08-25, on testing 6.5: *"lets remove the whole left pane, put
   the shutdown option inside the cobblestone menu pill on the bottom so it
   acts more like the start menu than a full screen app. the cobblestone
   button should expando the program groups and apps."*

   **The sidebar cost almost nothing to delete, which is the measure of how
   much the launcher had already absorbed.** Four of its five buttons were
   already rows in `gpr-entries`; only Shutdown had nowhere else to be. What
   moved with it is `ui-sidebar-px`, deleted from `GopDraw`, and therefore
   `dk-cbox-x`, which every window's left edge and every pane's origin derive
   from. It answers zero now and stays a function for that reason.

   **The menu is the launcher COLLAPSED, and that is a measurement rather than
   a preference.** The first build put `gpr-tree` whole into a box anchored
   above the pill. Captured at 1600x900 the Shutdown row was past the bottom
   of the glass and the taskbar was behind it, and the box was 1000 device
   pixels wide because a panel minimum is a floor and `gpr-tree`'s own was
   larger than the 500 asked for. At 1280x800 the same tree fitted. So the
   menu shows one row per GROUP and expands the group holding the selection,
   which is 8 rows against Accessories and 10 against Productivity.

   **The open group is a function of `gpr-sel` and not state**, so the two
   presentations stay one model and arrow keys crossing a group boundary open
   the next group with no code that does it.

   **Three findings, and a capture is what produced each of them.** A flex-1
   `widget-panel` paints its own background, so the anchoring spacer drew as
   an empty box until it became a label, which is the shape the deleted
   sidebar had already proven. The prose that said a panel does not size to
   its children was false and had been believed for a campaign;
   `widget-measure` propagates minima and the real trap was `widget-set-min`
   preserving flex. And the arm written for the overflow could not fail: an
   over-tall menu PUSHES the taskbar down rather than overlapping it, so
   comparing the two agreed on the broken build, and the row that discriminates
   compares the band against the glass. `works-desk-contract.md` carries all
   three where a pane author will meet them.

   `codex/test/apps/desk-menu-groups` is the arm, proven by sabotage: pointed
   back at `gpr-tree` it reports `band on the glass NO` at 1600 and stays green
   at 1280, which is the control.

   **Shutdown has no key and is reached only by clicking the row**, which is
   what it was in the sidebar. `dk-menu-hit` answers `dk-sc-bye`, 65536, which
   no PS/2 set can produce; `desk-app-close-to` hands it to `desk-dispatch`,
   it falls past every `sc ==` in the table and is caught at the bottom. That
   route is the one thing here an arm cannot reach, so it was driven at the
   bed with a three-way discrimination at 1600x900, one pointer path each and
   a 15 s screenshot delay:

   | click at | elapsed | screenshot |
   |---|---:|---|
   | Shutdown (delta -510,+328) | 11.5 s | **none** |
   | the Productivity heading (-510,+148) | 15.1 s | menu up, Productivity expanded |
   | empty wall (-510,-300) | 15.1 s | menu up, unchanged |

   The Shutdown arm exits within 100 ms of the click and never reaches the
   capture, which is `acpi-poweroff` taking the machine down. The other two are
   what make that mean something rather than reading as a crash: the same path
   generator, the same build, and one of them proves the click reached the desk
   at all. The Productivity capture is also the only proof that a heading click
   works, since the keyboard reaches the same state by a different road.

   **A keyboard walk covers the other half.** Ten downs from Clock lands on 3D
   View, and the capture shows Graphics expanded, the other three collapsed,
   and the menu SHRUNK from ten rows to six with no stale rows and its foot
   still pinned above the pill. Growing was already covered by the Productivity
   click; shrinking is the direction that would have left residue.

   ### 6.4 Edge docking

   A flick is a titlebar drag whose release carries velocity: direction from
   the dominant axis, magnitude past a threshold. The window docks to that
   edge as a pill with the app name, and a click restores it to the rectangle
   it left. Hovering the pill shows a mini-preview in a bubble, and
   `comp-render-at` is what makes that cheap: a bubble is the window's own
   tree rendered into a small rect at scale 1, live rather than a snapshot,
   paid for only while the pointer is over the pill.

   `Animation.codex` exists and is cited by nothing; the dock and restore
   transitions are its first real use.

   **The open question 6.4 must answer before it is built: what a docked
   window costs.** A pill is cheap, but a minimised window whose state lives
   above the heap frontier cannot be restored by redrawing it, and that is
   the same LIFO constraint that puts windows last. A docked LIGHT pane is
   free; a docked heavy one is the thing to prove or refuse.

   ### ANSWERED, 2026-08-25, and the mechanism already exists

   **Docking a heavy pane is already built and shipped: it is
   `desk-step-hide`.** A step answering 1,000,000 reaches `desk-app-hide`,
   which clears the focus, redraws the desk and does NOT restore the heap, so
   the pane stays alive with its state and its mark. Files and the Browser
   both bind it to Tab today. The taskbar's `tasks` slot already reads the
   mark stack and NAMES the live panes, which is the pill's precursor: a
   capture with Files and the Browser hidden reads `Files  Web` in the band.
   So 6.4 is not "invent docking for heavy panes", it is the flick gesture,
   the pill as its own drawing, click-to-restore and the hover preview.

   **What it costs, measured on the desk under codex-vm at 1600x900 through
   the Monitor pane's own heap readout.** Every reading is taken with the
   Monitor open, so its frame allocations are in all of them and cancel in
   the differences.

   | state | heap frontier | against baseline |
   |---|---:|---:|
   | desktop, nothing opened | `0x12db783` | -- |
   | Files opened then CLOSED (the control) | `0x12dbfeb` | +2,152 |
   | Files DOCKED | `0x144f73b` | +1,655,224 |
   | Files and Browser both docked | `0x16711c3` | +3,759,680 |
   | Files then CLOSED under a live Browser | `0x17bbff3` | +5,114,480 |

   So **a docked heavy pane costs 1.6 to 2.1 MB** -- Files 1,655,224 bytes,
   the Browser 2,104,456 on top of it. The control is what makes those
   numbers mean anything: opening and CLOSING Files returns the frontier to
   within 2,152 bytes of baseline, so the retention is docking's and not the
   cost of having opened a pane at all.

   **The LIFO tail is the real finding, and it is worse than "not
   reclaimed".** Closing a docked pane that sits BENEATH a live one does not
   give its memory back -- `desk-marks-reclaim` pops dead entries from the
   top only, so Files' mark stays buried under the Browser's. Measured, the
   frontier does not merely fail to drop, it RISES: reopening Files to close
   it allocates again, and the desk ends holding 5,114,480 bytes above
   baseline with ONE pane docked, where that pane alone costs 2,104,456.
   Roughly 3 MB stranded, from an operation a person would read as tidying
   up.

   **Against which envelope, because the bed is generous and the artifact is
   not (L-ARENA).** These readings come from codex-vm with about 3 GB; the
   flying boot image runs heap and stack in ONE 128 MB region. At ~2 MB each
   the mark stack's eight slots are ~16 MB, or 12.5 per cent of that region,
   before any stranding. That is affordable and it is not free, and the
   number that matters is the stranded one rather than the resident one.

   **THE PILL AS ITS OWN DRAWING IS DONE (main 19742): it carries its app's
   icon.** The label takes the same leading gutter every other chrome button
   has and `desk-pill-icons` paints into it after the layout, keyed on the id
   the button already carries.

   **`dk-pill-icon` is a SECOND table keyed by focus id, and the reason is a
   finding rather than a shortcut.** The one-register construction joins a
   pill's title against `gpr-entries` and takes that row's `ge-icon`. It fails
   silently on four of fifteen, because the window title and the launcher
   label diverge (`Edit`/`Editor`, `Web`/`Browser`, `Monitor`/`System Info`,
   and `Programs` is not an entry), and `gicon-named` answers the `file` icon
   for an unknown name rather than refusing. Four pills would have worn the
   wrong picture with every count agreeing. **That divergence is real, visible
   to a person, and left open as WORKS-50**; fixing it would make the join
   sound and let the second table go.

   The arm is one row in `codex/test/apps/desk-chrome-icons` and it covers a
   missing arm and a misspelled name together, because both land on that same
   fallback: for every id `desk-wnd-title` names, the icon must be non-empty
   AND resolve to itself. Sabotage-proven in both directions at 13 of 14, with
   `titled=14` unmoved as the control, and the row prints what an untitled id
   falls back to so the sentinel is visible rather than assumed.

   ### WINDOWS ARE MOVABLE. DONE. Damian, 2026-08-26, through red

   A press on a titlebar grabs the window, holding the button and moving the
   pointer carries it, releasing drops it. It is one arm in
   `desk-wnd-chrome-step`, so every one of the fourteen windowed panes got it
   at once, and clicking an unfocused window's bar raises AND grabs in the one
   gesture rather than costing a second press.

   **This is the flick's own primitive**, which is why it came before the
   remaining 6.4 items: a flick is a titlebar drag whose release carries
   velocity, so what is left for the flick is reading the release rather than
   inventing the gesture.

   **THE LEVEL AND THE EDGE ARE DIFFERENT CELLS AND A DRAG NEEDS BOTH.**
   `mouse-clicked` is `mouse-click-edge`, true only on the report where the
   button went down; `mouse-buttons` is the level. The desk had only ever
   consumed the edge, because every interaction before this one was a click. A
   drag written against the edge alone moves the window exactly one sample.

   **The rectangle needed no new home**: 6.3c already made it a fact in the
   pane's own block, and this is the caller that paragraph said would come due.
   The two `ds` cells the gesture needs are the last two the block has, and the
   contract now says the block is full.

   **THE CLAMP WAS WRONG IN A WAY ONLY A PICTURE COULD SHOW.** It first kept
   the TITLEBAR inside the content box and let the body hang off the bottom, on
   the reasoning that the bar is what a person steers. `desk-wnd-paint-all`
   paints the chrome first and the windows after, so the hanging body drew over
   the taskbar and buried the Cobblestone pill. Both axes clamp the whole
   window now. The arithmetic arm agreed with both versions and could not have
   caught it, which is the sixth finding across these campaigns invisible to
   green arms: ask what a photograph would show that an arm cannot.

   **What it costs.** No allocation: two `ds` cells, and the step's own work is
   arithmetic. The move calls `desk-wnd-repaint`, the same full repaint the
   shipped raise and maximise paths already call, so the per-event cost is not
   new; the RATE is, because it is now per mouse sample rather than per click.
   It repaints only when the rectangle actually changed, so a held button with
   a still pointer is free. **Unmeasured and named rather than assumed: what
   that rate costs ON METAL**, where this file already records a desk paint as
   near a second against about 16 ms in the bed. If it is too slow there, the
   answer is the outline drag every pre-compositing desktop used, and that is a
   second unit rather than a repair of this one.

   `codex/test/apps/desk-window-drag` is the arm: the offset round trip, the
   grab point held across ten samples, the four edges with a middle control
   that must NOT move, and the maximised refusal asked through the reason it
   exists rather than through the guard. Sabotage-proven in both directions --
   collapsing `dk-drag-dy` onto `dk-drag-dx` takes `offset held` to 0 of 10,
   and deleting the clamp's lower bound moves exactly the two edge rows and
   leaves the control alone.

   **FIRST UNIT LANDED: minimise to a pill, click the pill to restore.** That
   is 6.3b closed and 6.4's core, and it is deliberately the light-pane half
   -- the Clock and the Calculator, which push no mark and cost nothing to
   leave docked.

   **This sentence used to list what remained and it went stale twice, and the
   second time it misrouted the commander** (2026-08-27): it still named the
   pill's own drawing, which landed at 19742 two paragraphs up, and the flick,
   which landed at 20078 in the section below. red read it and dispatched this
   lane to build a shipped gesture. **A "what remains" list written beside the
   work it describes is a summary of a register, and it rots the way every
   summary in this project has rotted.** What remains lives in ONE place now:
   the task-frame stage table below for the campaign, and "WHAT IS STILL OPEN
   IN 6.4" at the end of the flick section for this stage. This paragraph
   records only what it was for: the light-pane half of docking, closed.

   **So 6.4 is not refused, and it carries an obligation.** Docking heavy
   panes is affordable at the depth the desk allows. What 6.4 must not do is
   present the pill as a cheap place to leave things: either restoring is the
   normal exit from a pill, which keeps the LIFO order intact, or closing
   from the pill has to be honest that it reclaims nothing until the panes
   above it go. The arm for whichever is chosen is this table re-run, because
   the control and the stranded row are what a "docking is cheap" claim would
   have to move.

   ### THE TASK FRAME, and its four stages, which were written down nowhere

   Damian, 2026-08-27: *"the task bar, as it was, is now more of a task frame.
   the whole edge of the OS should be dockable like that, with the bottom
   being the default placement."* Four stages:

   | | stage | state |
   |---|---|---|
   | 1 | the band docks to any edge | landed, main 20024 |
   | 2 | the flick | this section |
   | 3 | hot-launch pills | not started |
   | 4 | the Cobblestone button's position | not started |

   **That list existed only in one session's head until now, and recovering
   it after that session was evicted cost a transcript dig.** A CL description
   saying "stage 1 of four" names a denominator no register carries, which is
   L-ADJECTIVE's second half wearing a number: the count was accurate and told
   nobody what the other three were. A stage list goes in the design before
   the first stage ships.

   ### 6.4 THE FLICK: DONE, val 2026-08-27

   **The discriminator is the follow-through, not the release velocity, and
   that is a correction to what this section said above.** 6.4 was written as
   "a titlebar drag whose release carries velocity: direction from the
   dominant axis, magnitude past a threshold". Damian's own statement of it is
   narrower and better: *"a drag for moving purposes usually stops after the
   mouse up but a flick follows through in the direction of the edge to which
   the windowpill should be attached."* Release speed alone throws away a
   window somebody was placing briskly. Follow-through alone fires on the
   commonest thing a hand does after dropping a window. Both halves are asked.

   The drag primitive was already shipped (2026-08-26), so what this stage
   built is the reading of the release and the watch after it: a two-sample
   velocity baseline rolled every 40 ms, a release test at 700 device pixels a
   second, and a 300 ms window in which the pointer must travel 24 logical
   pixels further along the dominant axis. The watch answers three states and
   not two (L-STATES): the hand is still moving, the hand reached, the window
   closed and this was a move.

   **It ends as `desk-wnd-ev-min`, which is the dock the desk already had.**
   All twelve windowed panes handle that event, so every one of them got the
   gesture at once, and the pill, its icon and its click-to-restore are the
   shipped ones. The window returns to the rectangle it was flicked from,
   because the flick never moved it: the drag did and the release committed
   that.

   **THE DIRECTION IS MEASURED AND CURRENTLY UNSPENT, and that is the open
   product question this stage hands back.** Every edge answers the same dock,
   because a pill lives in the task band and the band is on one edge at a
   time. Damian's phrase is "the edge to which the window pill should be
   attached", which reads as though flicking left should leave the pill on the
   left. That is a placement question -- per-edge pill strips, their hit
   testing and their painting -- and not a re-measurement of the gesture, so
   the edge is recorded rather than discarded.

   `codex/test/apps/desk-window-flick` is the arm. It drives
   `dk-flick-verdict` and its parts, which are the functions the step itself
   branches on, so it is not a shadow of the step's arithmetic. Sabotage-
   proven: collapsing the baseline to one sample takes `still moving` red and
   leaves the `stopped` control alone. **The predicted failure was the
   opposite one** -- a one-deep baseline was expected to call a stopped hand
   fast, and what it actually does is report a 0 ms interval that
   `dk-flick-fast` refuses, killing the gesture rather than making it
   trigger-happy (L-SABOTAGE). A suite with only the negative arm would have
   scored a deleted gesture as a pass.

   `Animation.codex` is still cited by nothing: the dock is instant.

   ### WHAT IS STILL OPEN IN 6.4

   **This list is the only one. Do not restate it beside the code it
   describes** -- the paragraph that used to do that is the reason red
   dispatched this lane to build the flick after the flick had shipped.

   | item | state |
   |---|---|
   | the flick gesture | **DONE, main 20078** |
   | the pill as its own drawing | **DONE, main 19742** |
   | click-to-restore | **DONE**, and double-click to toggle at main 20105 |
   | the hover mini-preview | **RULED, see below.** The app decides; the default is a mini-render of the whole window, floating by the pill |
   | whether a flick's DIRECTION picks the edge the pill attaches to | **RULED: YES. The direction IS the selection criterion.** Needs per-edge pill strips |
   | the heavy-pane stranding decision | **RULED: option D, FIX THE ALLOCATOR.** See below. This lane's, after 6.7 |

   ### THE STRANDING IS RULED: FIX THE ALLOCATOR (Damian, 2026-08-27 evening)

   **Option D of the four this design put to him, and it is the one that was
   described here as a project rather than a decision.** Buried heap marks
   become reclaimable; close-from-a-pill stops lying. The three cheaper
   options -- accept the stranding, refuse to close anything but the top pill,
   or tombstone the pane and sweep it when its mark surfaces -- are all
   declined, and the recommendation carried upward was the third. Recorded via
   red at main 20231; this lane's after 6.7.

   **THE ACCEPTANCE ARM ALREADY EXISTS AND IT IS THE FRONTIER TABLE ABOVE.**
   The `+5,114,480` row is the subject: Files closed under a live Browser,
   holding about 3 MB more than the one pane still open costs. It must drop.
   **What makes that table an arm rather than a set of numbers is its
   CONTROL** -- the open-then-close row that returns to within 2,152 bytes of
   baseline. Without it, a change that moved every reading could be read as a
   fix; with it, the question is whether the buried row joins the control's
   behaviour, which is a different and falsifiable claim.

   **Take the readings the way the table was taken**: through the Monitor
   pane's own heap readout with the Monitor open in every arm, so its frame
   allocations appear in all of them and cancel in the differences. And
   re-measure the baseline rather than reusing the numbers above (L-COUNT) --
   they were taken at 1600x900 on 2026-08-25 and the desk has gained the
   window registry, the pill icons and a per-app edge block since.

   ### DAMIAN RULED TWO OF THESE, 2026-08-27, and both change the shape

   **THE FLICK'S DIRECTION IS THE SELECTION CRITERION.** In his words: *"yes
   on the flick direction is the selection criterion."* So a flick left docks
   the pill to the left edge, and the band is no longer one strip on one edge
   with every pill in it. That is the per-edge pill strip this table has been
   naming as a cost, and it is now the work rather than a caveat. What it
   forces, stated so the next session does not re-derive it: a docked window's
   edge becomes a per-window fact rather than a property of the band, so it
   belongs in the window's own registry entry beside its rectangle, and
   `dk-task-edge` stops being the only answer to "where does a pill live".

   #### 6.7 Per-edge pill strips. THE STAGE LIST, written before stage 1 ships

   A stage list goes in the design BEFORE the first stage lands. The task
   frame's did not, three of its four stages existed only in one session, and
   recovering them cost a dig through an evicted transcript. Not again.

   **What is already built and must not be rebuilt.** `dk-flick-edge` already
   computes the direction from the release vector and `dk-flick-arm` already
   stores it in `dk-flick-dir-cell`. **The direction is measured and unspent**
   -- every flick today reaches `desk-wnd-ev-min` and lands in the one band,
   because a pill lives in the task band and the band is on one edge at a
   time. So this is not "measure the direction", it is "spend it".

   | stage | what it is |
   |---|---|
   | 6.7.1 | **DONE, main 20210.** A pill's edge is a per-app FACT. One `ds` cell holds a block of `dk-focus-max + 1` entries keyed by focus id, written at dock from `dk-flick-dir-cell`, read by the band. Nothing visible changes yet: every entry defaults to `dk-task-edge ds` and the picture is identical. **That is the point** -- it is provably inert and the arm asserts it |
   | 6.7.2 | **DONE.** The band FILTERS by edge. `dk-pills` takes an edge and yields only the pills docked to it; the existing band asks for its own edge. Still one band, so still no picture change unless something writes a different edge |
   | 6.7.3a | **DONE. The RESERVATION.** `dk-cbox-*` ask every edge through one `dk-strip-px` instead of four `dk-task-edge ds ==` tests: an edge takes glass if it carries the band, or if it carries pills of its own. Inert until something writes an edge |
   | 6.7.3b | **DONE. The PAINT.** `desk-chrome-face`'s four edge arms become ONE construction with an optional node per edge, so any combination of band and strips is legal. Clicks and icons needed nothing: `dk-pill-hit` and `desk-pill-icons` search the laid root by widget id. **And the depth question is answered: a strip is the SAME depth as the band on both axes**, since the Cobblestone button and the clock add nothing beyond the floors and a pill's own height, so there is no thinner constant to invent |
   | 6.7.4 | **DONE. The flick spends the direction**, so a flick left docks the pill to the left edge. `dk-flick-dock-at` is the dock on its own because everything above it in the gesture is a speed and a deadline off the HPET, which `desk.ps1 -Rtc` pins -- the gesture is unverifiable by scripted capture BY CONSTRUCTION and wants a hand on the mouse, so the arm drives the state change the reading leads to instead |

   **Why the fact is keyed by FOCUS ID and not by registry index.** The
   registry's order is the z order and it changes when a window is raised;
   `dk-pills` already walks ids 0 to `dk-focus-max` rather than the registry
   for exactly that reason, because walking the registry moved a pill out from
   under the pointer that had just clicked it. An edge keyed by index would
   inherit that bug; keyed by id it cannot.

   **The `ds` cell is 212.** Re-measured 2026-08-27 against the definitions
   rather than taken from the contract: cells 0 through 208 are in use and the
   block is 256 bytes, so 212 through 252 are free, eleven cells. It is a
   pointer, so it is allocated in `desk-run` BEFORE the base mark, for the
   reason every other pointer cell is.

   **The cost 6.7.3 has to answer, and it is not obvious.** Four edges each
   carrying a strip is four bands' worth of content box gone. The band is
   `dk-task-px` deep, which at 1600 is 56 device pixels; strips on all four
   edges would take 112 from each axis. A window is three quarters of the
   content box, so that is visible. Whether a pill-only strip should be
   thinner than the full band -- it carries no Cobblestone button and no clock
   -- is a question 6.7.3 must answer with a measurement rather than a
   preference, and the arm is `desk-pane-origin`, which already asserts the
   laid content slot against the box at all four edges.

   **THE HOVER PREVIEW IS THE APP'S DECISION, WITH A DEFAULT.** In his words:
   *"the hover preview should be decided by the app, and in default should be
   a mini-render of the whole floating there by the pill."* This unblocks the
   item outright and it dissolves the premise failure below rather than
   working around it: the reason a preview could not be built was that
   `desk-wnd-tree` answers an empty panel for the five heavy panes, and the
   answer is that the preview is not the desk's to compute from a tree. A pane
   supplies its own; the default, for a pane that supplies nothing, is a
   mini-render of the whole window floating beside the pill. **The account
   below of why the tree-based mechanism cannot work stands as the reason this
   ruling was needed, not as a live blocker.**

   ### THE HOVER PREVIEW'S PREMISE IS FALSE FOR THE FIVE PANES IT IS FOR

   Established by reading, val 2026-08-27, when this was picked up as the
   unblocked tail of 6.4. **6.4 says a bubble is "the window's own tree
   rendered into a small rect at scale 1, live rather than a snapshot", and
   `comp-render-at` is what makes it cheap. The five heavy panes have no
   tree.** `desk-wnd-tree`'s first arm is

       if fid == desk-focus-scene | fid == desk-focus-fish
        | fid == desk-focus-bro | fid == desk-focus-files
        | fid == desk-focus-edit then widget-panel "scene-body" DirColumn 0 []

   an EMPTY panel, and `desk-wnd-over` is the only other thing the shared
   painter calls, which answers 0 for everything except the launcher's icons.
   So Files, the Browser, the 3D view, the Aquarium and the Editor paint their
   content from their own step while focused, and nothing the desk can call
   reproduces it. A preview built as designed renders an empty box for exactly
   the five panes a preview is worth having, and a correct-looking one for the
   Calculator, the Clock and the Console.

   This is not a new fact about those panes -- this file already says Files and
   the Editor draw directly with zero `comp-render` sites, one section up. What
   is new is that 6.4's preview mechanism was specified without it, so the item
   as written cannot be built. **It is recorded rather than half-built**: a
   bubble that works for the Calculator and is blank for Files is a demo, and
   it would have been shipped as the item and closed.

   **What a real preview would need, so the next attempt starts from here.**
   Either the heavy panes gain a tree, which is a per-pane rewrite and much
   larger than a preview; or the desk keeps a SNAPSHOT of each heavy window's
   pixels taken when it was last painted, which contradicts 6.4's "live rather
   than a snapshot" and costs a buffer per pane; or the preview is defined as
   chrome plus a title and an icon and does not claim to show content. The
   third is honest, cheap and probably right, and it is a product decision
   rather than an implementation one.

   **UNMEASURED AND WORTH ONE RUN, because it falls out of the same reading.**
   `desk-wnd-walk` paints every non-minimised window through `desk-wnd-one`,
   and `dk-wnd-frame` fills the body with `pal-bg` before rendering the tree.
   For a heavy pane that tree is empty, so a full repaint while a heavy window
   is open and NOT focused should leave it a blank framed rectangle until its
   own step runs again. Whether that is reachable in practice is not
   established here: heavy panes bind Tab to hide, and the Browser may only
   ever be the topmost heavy pane, so open-but-unfocused may be rare. Open two
   heavy panes, focus the second, force a repaint and photograph the first.
   **Do not report this as a defect before that run** -- it is a prediction
   from reading, which is the thing L-MECHANISM says to distrust.

## Constraints on the surface that this campaign does not get to change

**The Browser may only ever be the topmost heavy pane.** It rebuilds its
state at the current heap frontier on every event, so anything opened over
it, or any pane that takes a frame mark above it, breaks it (`BROWSER-5`,
`apps/browser/browser-backlog.md`; the scroll remainder closed at fester's
main 17968). Stage 3's layered surfaces and any later window or z-order work
inherit this: **a general window manager is not available while that holds**,
and a design that assumes arbitrary stacking will be wrong at the Browser.
Told to this campaign by red, 2026-08-20, as a property of the surface.

**The taskbar band repaints every second** and is contested by any pane that
renders `desk-chrome-with` (`WORKS-37`). Every stage that paints near it has
to leave `dk-chrome-paint`'s clock repaint intact.

**`dk-task-h` is 28 logical at every resolution and cannot host a themed
button** (`WORKS-34`): a themed button needs 36, so the band's contents are
labels. Raising it moves `desk-mon-draw`, `dk-win-y` and `GopScene`'s
hand-synced copy together. Stage 2 wants icons in that band, so this is
stage 2's first problem and not a surprise it should discover.

## What this collides with, stated plainly

**SETTLED 2026-08-20: the desk is val's for this campaign.** red released
`GopDesk.codex` the day the campaign opened, and gave the reason, which is
worth keeping because it is the sort of thing that otherwise gets
re-litigated: the desk was claimed alongside `GopBoot` and `GopWizard`
because the identity ceremony reached into it, and all four identity stages
have since landed, so nothing of red's is in flight there. `GopComposite` was
never claimed. The claims table carries both.

**`GopFacts.codex` remains red's** and is not part of this. Stage 5's
settings persistence therefore goes through the fact store's published
interface rather than by editing that chapter.

**The announce-before-you-start rule on the desk is not suspended by the
claim**, and neither is checking which `ds` cells are already spoken for
before taking one (the cell 48 collision, 2026-08-11). A campaign that takes
a file does not get to skip the protocol that keeps the file usable.

## Memory and time (R-COST)

The two stages with real exposure are 1 and 3.

**A glyph atlas is a cache and caches grow.** A proportional face at several
sizes with antialiasing is materially more memory than a 16-row bitmap, and
bare metal has no collector. The atlas must be allocated once above the
desk's base heap mark, like the 3D pane's target, and never per frame. The
bound to state and measure is atlas bytes per face per size.

**A wallpaper is the largest single allocation the shell would ever hold**:
1920x1080x4 is 8.3 MB against a 128 MB arena. It is loaded once, kept, and
must not be re-decoded on a theme change.

Stage 4's exposure is time rather than memory: audio submission must not sit
on the draw path.

Every stage states its own verdict before it lands, per R-COST.

## What this design does NOT claim

It does not claim the 36 unused chapters work IN THE DESK. It claims they
exist, are not stubs, and each carries a test of its own, which is a
different and weaker statement than working under the shell's constraints.
Each stage arms the ones it wires against those constraints.

It does not claim feature parity with Windows, macOS or Android is reached
by finishing stage 8. It claims these eight stages are the skeleton such
parity would hang on, which is what was asked for.
