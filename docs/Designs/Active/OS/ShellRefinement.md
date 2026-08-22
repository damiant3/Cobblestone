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
`seed img bytes` and `seed img sha256` claims in `README.md`, which
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
| Overlapping windows | `codex/foreword/ui/Window.codex`, a real window manager: `wm-open`, `wm-focus`, `wm-move`, `wm-resize`, `wm-tile`, z-order, 167 lines | written, cited by nothing |
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
2. **An antialiased stroke.** `Vector.codex` rasterizes with Bresenham, so
   wiring vector icons before this would draw jagged artwork through a smooth
   compositor.
3. **The eight pixel cell** (see `desk-label-metrics`): the widget layer sizes
   text by `text-length * 8` while the renderer draws proportionally, and that
   has to be honest before any width constraint lands, because the full-bleed
   stretch is currently hiding every under-reservation.
4. **Small type.** The face loads at `comp-glyph-h * ui-wscale`, a 41 pixel
   cell. His reference is legible at a third of that and ours has never been
   asked to be.
5. **Windows.** `Window.codex` and `Surface.codex` together are overlapping
   windows, and the desk's own model is one full-screen pane at a time. That
   is a shell-model decision, not a wiring job, and it is the biggest item
   here.

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
