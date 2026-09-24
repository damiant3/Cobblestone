# Shell Refinement -- from dev surface to a first-rate OS shell

**Opened 2026-08-20 (val) at Damian's direction.** The desk works and is a
good dev surface. It does not look or behave like something you would hand a
user: the word he used was fischer-price. This design is the campaign that
fixes that, and it covers visual refinement, space efficiency,
responsiveness, sound, a real settings system, fonts, backgrounds, icons,
accessibility and parental controls, with the stated goal of matching what
Windows, macOS and Android give a user on day one.

## Layout stacks and constrained allocation

`widget-stack` constructs a typed `WkStack`: a row or column that measures,
arranges and contains children without a background, border, padding or margin
of its own. Child styles still apply. `widget-panel` remains the decorated
surface. Grouping controls no longer requires adding a styled surface.
GopComposite, UI Render/GpuRender and the active HTML path implement stacks.
Other native-widget exporters require an explicit mapping when adopted.

Flex weights describe shares of available main-axis space. Children whose
minimum exceeds that share freeze at the minimum; remaining children divide
the remaining space. The allocator repeats until every flexible share satisfies
its floor. Cumulative integer shares consume the full distributable extent,
including remainders. Fixed children, margins and between-child gaps reserve
space before distribution. Cross-axis stretch retains declared minima.

Minimums remain hard floors. For direct `flex-layout` callers,
`LayoutResult.lr-used-w` and `lr-used-h` report
allocated extents including margins and gaps; `layout-overflow-w/h` report
positive excess over a viewport. The report concerns layout allocation, not
shadows or text ink. An undersized viewport needs reflow, more space or scrolling
from its caller; overflow reporting does not supply scrolling automatically.
`widget-layout` returns the arranged tree and does not retain those extent
fields. A scroll viewport (`widget-scroll-view`, `WkScroll`, S4 below) stops
child minima from enlarging the viewport and scrolls its content natively; the
HTML adapter does not yet install scrolling (S4h).

Calculator uses layout stacks for its display and keypad groups. The display
keeps its measured height and the keypad receives the remaining space. Tests in
`codex/test/ui/flex-minima.codex`, `codex/test/ui/layout-stack.codex` and
`codex/test/apps/calc-layout-fit.codex` cover floor redistribution, shortage,
rounding, undecorated containers and all fifteen keys at both desktop scales.

HTML uses a bare flex container for WkStack and preserves standard widget nodes' declared
minimum dimensions. Flexible nodes use a zero basis and weighted growth; fixed
nodes retain their basis. Native browser intrinsic text sizing can exceed an
explicit minimum. Browser pixel equality is not promised by the shared model.
Custom widget adapters retain their own sizing rules and bypass the common
typed-widget minimum/flex mapping.

Cost: layout placement stays linear after fitting. Fitting takes at most one
minimum-freezing pass per child, quadratic comparisons in the worst case and
linear temporary budget records. Scans use scalar accumulators, not per-child
temporary records. Stack construction allocates one node; the new result extent
adds two integer fields per layout result. Existing frame reclamation applies.

Remaining model work includes explicit intrinsic/preferred/maximum sizes,
consistent margin/border-box measurement, alignment and text baselines,
integrated overflow/scroll policy, semantic input states and theme reflow.
The sizing and viewport half is staged below and is val's (from root,
2026-09-23).

## Sizing and viewports (val)

Each stage lands alone, with the arm named for it. No stage takes the build
token: `codex/foreword/ui` is not in the compiler's unit (the standing fact
below, re-measured 2026-09-23: 22 `Foreword--` chapters and no `Ui--` one), so
a UI stage is proved by its arm and the cite-gate over what it changed.

**S1, maximum sizes.** `LayoutItem` and `WidgetNode` gain a maximum per axis,
0 meaning unbounded. The allocator freezes an item at its maximum the way it
freezes one at its minimum today (`flex-locked`, `flex-fit`) and hands the
excess to the others. The cross-axis stretch, which `flex-row-place` and
`flex-col-place` do by raising each child to the container (`box-max` of the
minimum and the container extent), stops at the maximum. A maximum below the
minimum yields to the minimum, which stays the hard floor. The arm,
`codex/test/ui/flex-max`: a row of two flex children, one capped, where the
capped child stops at its cap and the other takes the rest, and the same row
with the cap removed as the control. S1 is native only.

**S1h, the maximum in the HTML lowering.** `HtmlEmitter.codex`'s widget
mount (beside `el.style.minWidth`) emits `max-width`/`max-height` for a
nonzero `wn_max_w`/`wn_max_h`, never below the minimum. It is proved by
rebuilding the html plug, regenerating one app page whose tree sets a maximum
(`build/build-apps.ps1 -Only`), and loading it with
`build/check-app-pages.ps1 -Only`; the regenerated page is the only artifact
it moves.

**S2, natural sizes are a separate fact from explicit ones.** A label or
button's minimum is a guess (`text-length * 8`) that `comp-fit-node` replaces
only while the node still holds the guessed number (`GopComposite.codex`
`comp-fit-guess`), so an author's explicit width that happens to equal the
guess is overwritten and a pinned zero defeats the fit. A node carries whether
its minimum is measured-natural or author-set, and the fit replaces only the
natural one. The arm, `codex/test/ui/natural-size`: an explicit width equal to
the guess survives the fit, and a natural one is still replaced.

**S3, preferred sizes.** A preferred size is a soft minimum: the allocator
satisfies every minimum, then every preferred size while space allows, then
distributes flex growth. Under shortage the preferred sizes shrink toward the
minimums in proportion. The arm, `codex/test/ui/flex-preferred`: one row at
three widths, above, between and below the preferred sum.

**S4, a typed scroll viewport (landed).** `widget-scroll-view` builds
`WkScroll (Integer)`, the offset along its layout direction (an Integer, not a
`ScrollState`, because `Scroll.codex` cites `Widget`). A pane sets it with
`widget-set-scroll` and reads it back with `widget-scroll-offset`.
`widget-arrange` lays the children at their natural main extent (each item's
minimum or flex basis plus margin, plus gaps, never less than the viewport)
shifted back by the offset, and clamps the offset to that extent in the laid
node. The laid bounds are therefore absolute, so `ev-hit-widget` follows the
scroll unchanged and its `rect-contains` gate on the viewport keeps a
scrolled-off child from answering. `comp-walk` and `comp-walk-except` narrow
the clip to the viewport's box for its children (`comp-kids-clip`).
`Scroll.codex` builds the `ScrollState` from a laid viewport
(`scroll-view-state`) and answers the next offset for a key
(`scroll-view-handle-key`: the main-axis arrows, page up and down, home, end)
or a wheel delta (`scroll-view-handle-wheel`), at `scroll-line` 24 pixels per
step. The Browser's hand-built pane (`gbr-paint`, `comp-translate-kids`) is
not migrated. The HTML and MAUI plugs render `WkScroll` as they rendered the
old `scroll-view` tag; the HTML lowering to `overflow: auto` is S4h, proved
like S1h. The arm is `codex/test/ui/scroll-viewport`.

**S5, a windowed Files listing, and this is what sizes the Files hole.**

S5a (landed). The pane keeps the directory's COUNT (`fs-count`), not its
entries. `gfat-dir-count` and `gfat-dir-window` (`GopFat16.codex`, "Windowed
Listing") read every sector and FAT link outside the requested rows inside a
heap bracket that answers integers into a cursor allocated before it, so a
listing allocates the sectors holding the window's rows and the rows
themselves. Every paint (`gfl-paint-cur`) reads the visible rows in its own
frame; a pick reads the one row it opens. `files-open-cost` reads the open's
`want` as 128 bytes at the root and 128 in the subdirectory, where the whole
listing beside it still costs 2,520 and 2,864 (2026-09-23).
`codex/test/apps/files-window` grades the windows against the whole listing
across a sector, a cluster hop and the directory's end, and two four-row
windows at the head and tail of a three-cluster directory at the same cost, on
a fixture built host-side by `build/make-fat16-bigdir.ps1`.

S5b (landed). `desk-files-open` asks `desk-hole-enter` for
`desk-files-want` (8,192 bytes) and pushes a mark only when no hole is
found, the way `desk-scene-open` is wired. The GPT lookup (`gpt-esp-start`)
runs first in a bracket at the frontier, because it reads as many sectors as
the medium's partition array holds (58,528 bytes on the fixture); inside the
hole run only `desk-files-mount` and `desk-files-build`, 1,992 bytes
(`files-open-cost`, 2026-09-23). `codex/test/apps/files-hole` runs that
sequence in a real hole: the allocation lands inside with 6,200 bytes to
spare, the frontier does not move, the entry is claimed for Files, and the
listing reads back through the state. The step allocates at the frontier as
before (D.5's first sizing rule), and a directory change keeps its two
navigation lists there. The desk reading is in D.5, beside the 3D pane's. The generic widget list viewport
(a row count and a row builder) is not built: no pane asks for it yet, and
Files paints its rows directly. Edit's list mode does the same: `EditState`
keeps `ed-count`, and `ged-list-paint-cur` reads a screenful of rows from
`top` for each paint.

**S4h waits on a consumer.** No app page's tree holds a scroll viewport
(`apps/*/*Page.codex`, 2026-09-23): the Browser and Review build theirs on
the native desk only. An `overflow: auto` lowering that no page exercises
cannot be proved the way S1h was, so S4h is built with the first HTML page
that adopts `widget-scroll-view`.

**S6, cross-axis alignment (landed).** `LayoutItem.li-align` and
`WidgetNode.wn-align` are an Integer (`align-stretch` 0, the default, then
`align-start`, `align-center`, `align-end`), set with `layout-item-align` and
`widget-set-align`. A stretch item fills its line as before; any other takes
its own cross size (minimum or preferred, under its maximum) and is placed
by `flex-cross-off`, never before the line's start. The arm is
`codex/test/ui/flex-align`. Native only: the HTML lowering (`align-self`) is
S6h and waits on a page that sets an alignment, as S4h does.

**S7, text baselines (landed as a guard).** On the native desk a label and
a button in one row already share a baseline under stretch and centre: the
desk draws in one face, every edge of both theme families is symmetric top
to bottom (`edges-uniform`, `edges-xy`), and `comp-text-top` centres the
cap band in each content box. `codex/test/apps/desk-baseline` pins that
through `comp-text-top` (which `comp-text` draws with) under the palette
theme, face and bitmap, scales 1 and 2, with a start-aligned row as the
control that differs. A baseline ALIGNMENT mode is not built: it is needed
only once a second face size or an asymmetric edge exists, and that arm is
what will go red when one does.

**S8, independent control states (landed).** A node's control state is a
flag field (`wn-flags`: focused, hovered, pressed, checked, disabled,
invalid, busy), so a focused button can also show hover. The theme resolves
flags to one `StateStyles` slot by a stated precedence (disabled, pressed,
hovered, focused), and focus, checked, invalid and busy paint as overlays
(ring, mark, error edge, indicator) on top of that slot, so every combination
has a defined picture. A custom widget's Integer payload (a selected index, a
seed) is `wn-data` (`widget-set-data`), never a style state.

S8a: `wn-flags`, `widget-set-flags` and `widget-add-flag`; the `flag-*`
constants and `theme-state-of-flags` in `Theme.codex`; `widget-state-of` is
what `widget-resolve-style`, `Render` and `GpuRender` resolve by.
`comp-focus-ring` (`GopComposite.codex`) draws the focus a stronger state
hides. The arm is `codex/test/gop-composite-focus`, whose controls are a
focused-alone button changing pixels off the ring (it resolves to the
focused slot, not a ring) and a flagless node resolving to normal.
`theme-flags-of-state` converts a chapter's own local state Integer to a
flag, for the five writers that compute one (CommandPalette, FilterableList,
SearchBar, cvmm Monitor and LogViewer).

S8d: `comp-state-marks` draws invalid as a bottom edge two scale
steps high in the error colour, checked as a four-step square in the top
right in the style's foreground, busy as a one-step bar along the top's
left third in the warning colour; `codex/test/gop-composite-marks` holds
each mark to its own region and marks all three over hover and focus.

S8h: the HTML plug's style state is `_wkStateOf(n)`, the precedence of
`theme-state-of-flags` over `wn_flags` (converted with `Number`, because the
field arrives as a BigInt); it styles the widget (`_wkSS`) and the button
bar's primary button, and the custom widgets' payload reads (`opt`, `optc`,
`_wkCustom`'s `st`) take `wn_data`. The MAUI plug resolves its style state
from `wn-flags` by the same precedence. The checked-in pages under
`apps/*/web/` are `build/build-apps.ps1` output from before S8a and still
carry `wn_state` until their next regeneration.

Cost: S1 and S3 add at most one more freezing pass per child to the existing
fit, so time stays quadratic in the worst case and the budget records stay
scalar. S4 adds one linear pass over a viewport's children per arrange, and
the key and wheel handlers one list of layout items per call. S5a trades
heap for disk reads: a Files paint re-reads the directory's sectors up to
the last visible row, and an open or directory change reads all of them to
count, each sector's buffer freed by its bracket. S5
bounds a list pane's heap by its visible rows, which is the point.

## Approved font preview delivery, 2026-09-19

Damian authorized importing the shortlisted open fonts, adding the pack to
the boot image, and applying fonts across the desktop and linked apps for
visual approval. Inter is the initial default. Preview choices include Source
Sans 3, Atkinson Hyperlegible Next, IBM Plex and Noto Sans; IBM Plex Mono supplies
the code-text role. Regular/semibold faces and Inter Bold are included with
their original OFL notices and pinned asset hashes.

The first delivery covers the active GopDesk/GUI and HTML/CSS/JavaScript paths.
Unused plugs remain incomplete until an actual project needs them. The widget
transpiler and broader visual redesign follow font approval; the earlier survey's
cross-platform roadmap is not permission to build every backend now.

Font selection for the first preview happens before launch or image construction,
preserving one shared face across a desktop session without reallocating live
application state. `fonts/font-pack.json` owns the pack. `UIFONT.CFG` is a
16-byte zero-padded ASCII FAT filename; legacy images without that record retain
the CMUNSS.TTF fallback. The default image must carry all selected font assets
and license notices. HTML exports must carry usable local/embedded fonts and
their notices without requiring a network font service.

Preview and image-build commands are in [the font pack guide](../../../../fonts/README.md).
The pack contains twelve static TTF faces totaling 3,958,976 bytes. The native
desktop shares one selected face across chrome and linked panes. HTML exports
embed the selected family's weights and Plex Mono with OFL notices. The native
desktop carries explicit UI and code font roles for custom-painted menu apps.
Editor source, line numbers and file source/hex previews use Plex Mono with
measured character advances. File listings use clipped name and size columns.
The first-boot wizard retains its early bitmap font.

The compound decoder bounds nesting at 16 levels and work at 64 glyph visits.
Simple outlines are capped at 256 contours and 4096 points. Transform work and
allocation scale with visited outline points and nesting; font parsing retains
the existing full byte-list copy. Desktop font loading occurs once per session,
before app heap marks. The coverage diagnostic restores scratch per glyph and
per face. Web packaging costs linear font-byte work plus base64 expansion;
image choice edits read and write one complete image on the host.

At 16 ppem, the 2026-09-19 native measurement retained 13,780,997 bytes for
Inter loading and 10,860,386 bytes for Plex Mono loading. Both costs occur once
per desktop session. The shared font reference adds eight bytes to a FilesState;
the existing root/subdirectory cost fixture measures 40,072 and 67,464 bytes.
Plex Mono's measured cell advance is nine pixels at that size. Reopening the
editor reuses the cached font set rather than loading another atlas.

The native coverage check passed 94 visible ASCII glyphs for each of twelve
faces. Coverage does not establish general OpenType conformance: hint bytecode,
phantom-point attachment, variable/CFF/color fonts, complex shaping and Unicode
layout remain outside the current renderer. Browser automation was unavailable
for the Notes export; browser appearance awaits visual review.

## 2026-09-19 UX and typography survey

Assessment requested by Damian; implementation remains a separate commitment.
GopDesk needs a shared semantic control and text contract before broader skinning.
The existing box model, widget tree and compositor are useful foundations.
Adding more palettes alone cannot repair clipping, missing glyphs, exclusive
interaction states or backend-specific custom-widget behavior.

### Evidence and limits

Source reviewed at main25869, merged to root25872. A fresh DeskVm build used
depot seed #800, SHA-256
`BD66718CBE24F589E8AD8E9D7AAC47289081B00A8824ECD22DBDE77321279541`.
DeskVm SHA-256:
`40FD0E3C6D90F567B9D91875212E7BFA6FC5BCEF583B73B4706036914273B572`.
Captures and the font audit are in
`D:/Projects/Cobblestone-root/build-output/ux-survey-20260919/`.
The eight BMPs cover CMU Sans at 1280x800 and 1600x900, the launcher,
Calculator, Appearance, Chevy, Inter and Source Sans 3. Mouse/key timelines,
compiler/VM logs, candidate fonts, licenses and `font-audit.json` are adjacent.

The auxiliary FAT images carry the selected font as CMUNSS.TTF; GopDesk
selects by that filename. The image's boot PE was not executed: DeskVm boots
directly and mounts the image for font/settings access. Seed artifacts were
not changed. The first capture used an insufficient 2048 MiB VM mapping and
failed with `Unmapped MMIO GPA=0xbf000000 (WRITE)` before painting. The source
requires the GOP address inside mapped RAM; subsequent captures used the
desktop runner's 3072 MiB setting and completed. That failure is a capture
configuration error, not a desktop rendering result.

HTML, MAUI, SwiftUI, Compose and Flutter findings below are source inspection,
not device conformance results. No physical run or full battery ran. The
interactive `ux-directions.html` is a browser-rendered proposal, not GopDesk
output. The exact diffusion-font entry point remains unidentified; the inspected
font-generation paths are MLP-based.

### Current rendering findings

| Finding | Evidence and consequence |
|---|---|
| Compound glyphs become blank | `apps/guios/FontLoad.codex:503` returns empty contours for compound glyphs. Fresh Inter and Source Sans 3 captures visibly lose `i` and punctuation. The binary audit finds seven compound printable-ASCII glyphs in Inter and eight in Source Sans 3, including `i`, `j`, colon and semicolon in both; the captured welcome text does not exercise `j`. CMU Sans has no compound printable-ASCII glyphs. Font validity and font aesthetics cannot be judged through the current decoder alone. |
| Typography is not a style contract | `GopFont.codex:55` builds one face; GopDesk loads the fixed CMUNSS.TTF filename and chooses 14 or 16 device ppem by display width. `Theme.codex:142-176` has no family/weight/size/line-height roles. The captured label reports a filename even when the font bytes are Inter. |
| Measurement still begins with bitmap assumptions | `Widget.codex:94` sizes text as character count times eight. `GopComposite.codex:808` repairs selected label/button minima only when the numeric minimum matches that guessed value. Explicit sizing and natural sizing need separate types; numeric equality cannot express author intent. |
| Overflow breaks real controls | The fresh Calculator capture shows rows 7/8/9, 4/5/6 and 1/2/3, but not the source's final 0/equals/plus row. `GopCalc.codex`, `gcalc-tree`, supplies that row. `Layout.codex:48-85` distributes available space then raises each share to a minimum, without redistributing shortage. Minimum sums can exceed the container. |
| Theme shape information is lost | `GopStyleKit.codex:98` returns `border-none` when outline width is zero. `Theme`'s border-none has sharp corners. Chevy requests round corners and zero outline at `GopStyleKit.codex:189`; the fresh Chevy capture shows square controls despite Appearance reporting rounded corners on. Shape must exist independently of border ink. |
| LCARS is still selected as a palette path | `GopDesk.codex:254-271,471` routes indices 4 and 5 through DesignLanguage, but index 2 through the palette theme. The existing `sk-lcars` language is not the selected desktop LCARS path. No LCARS runtime capture was taken for the survey. |
| Theme changes need full layout invalidation | The Chevy capture clips taskbar labels to fragments after changing padding/style. The screenshot establishes clipping; the exact cache invalidation repair needs a targeted trace. Theme changes affecting metrics must remeasure layout, hit regions and text together. |
| Presentation lacks hierarchy | Launcher entries and Appearance toggles are all large glossy action buttons. Appearance lacks distinct toggles/choices; Calculator labels remain small inside very wide buttons. The welcome page exposes runtime details as product copy. These are visual/interaction design findings, separate from the rendering defects above. |
| The named golden-pane driver is stale | `build/desk-goldens.ps1:100` still uses scancodes to open named panes. `GopDesk.codex:6682` ignores non-F12 keys on an unfocused desktop; `build/desk.ps1:33` documents that constraint. Repair the driver and assert the expected pane before accepting its screenshot. The survey used mouse timelines instead. |

The current UI directory has 49 chapters. The transitive GopDesk cite closure
contains 26, including TextField, Selection, RichText, Surface and Overlay.
Focus, Accessibility, Touch and Animation are absent from that closure.
Reachability alone does not prove exercised behavior. The earlier campaign's
50-chapter/14-direct-cite measurements below describe an older tree.

### One UX model, explicit backend contracts

`WidgetKind` currently has seven constructors: panel, label, button, gauge,
separator, input and a string-tagged custom node (`Widget.codex:8`). Richer
libraries compose those nodes. `WidgetNode` carries one integer state, minimum
sizes and an arranged rectangle. A control cannot express focused-and-hovered,
checked-and-disabled, invalid-and-focused or busy-and-selected as independent
facts. `A11yInfo` exists separately; the common node does not carry a semantic
role/name/value/state binding.

| Backend | Present | Gap relevant to uplift |
|---|---|---|
| GopDesk software compositor | Theme/state resolution, rounded fills, gradients, bevels, shadows, text and explicit clipping; custom icons/status dots | Custom tags fall back to a styled box (`GopComposite.codex`, `comp-custom`). `comp-walk` forwards one clip to children; scroll clipping is a separate special walk. No shared integrated focus/accessibility/touch contract in the desk closure. |
| HTML/CSS | Real button/input/progress elements, widget-to-DOM lowering, CSS state selectors and theme colors | `HtmlEmitter.codex:746-844` omits shadow/gradient/bevel fields in common style lowering, uses the top border as a uniform border, and guards zero colors by truthiness. Round corners depend on nonzero border width. Black is conflated with absent styling. Native HTML controls help, but custom tags still need semantic keyboard/accessibility contracts. |
| MAUI phone/desktop | Real Button, Entry, ProgressBar and stack layouts; themed widget mounting and input callbacks (`MauiEmitter.codex:547-709`) | Common style lowering covers colors, margins, layout padding and minima, not the whole Theme surface contract. Custom tags have special cases and a label/stack fallback. AutomationId is present; a full mapping from common accessibility metadata is not shown. The mobile README's claim that the plug is absent is stale. |
| SwiftUI / Compose / Flutter | Language emitters and application wrappers | The inspected wrappers display collected console output. Console wrappers do not establish WidgetNode control parity. Treat each as a separate lowering task until real control/interaction proofs exist. |

Recommended model boundaries:

1. **Semantic controls:** stable IDs, role, accessible name, value, commands,
   enabled/read-only/checked/selected/invalid/busy state and focus policy.
   Action semantics do not contain scancodes, CSS names or device dimensions.
2. **Layout:** measure then arrange, with min/preferred/max constraints,
   grow/shrink, alignment/baselines, wrapping, grids/tracks, padding/gaps,
   explicit overflow and scroll viewports. Keep paint overflow separate from
   layout size and hit size. Use start/end edges, direction and safe insets.
3. **Presentation:** typography roles, semantic color tokens, component parts,
   shapes, elevation/material, icon family, density and bounded motion.
4. **Platform capabilities:** available space and scale; keyboard/IME, fine/coarse
   pointer, hover, touch/pen; text shaping, alpha/clip/vector support; accessibility
   bridge, audio and haptics. Lower to native controls or the software renderer.
   Unsupported functionality returns an explicit capability result or a declared
   equivalent interaction. A silently empty rectangle is not a fallback.

Keep the semantic core compact. Add reusable control compositions and typed
contracts rather than another growing collection of magic custom-tag strings
or one giant platform-specific union. App window management belongs to the
desktop shell. A list/detail application becomes two panes with space and a
navigable single pane without space; a phone is not a scaled desktop window.
Preserve selection, drafts and focus identity across layout transitions.

The next shared interaction set needs buttons with quiet/primary/destructive
variants, checkbox/toggle/radio, choice/select, range/stepper, tabs/navigation,
list/tree/table, text editing, scrollbars, splitter, menus/popovers, dialogs,
tooltips and status/progress. Each needs a behavior contract: Tab/Shift-Tab,
arrows, Enter/Space, Escape, focus restoration/trapping, pointer capture/cancel,
touch targets and text composition. Reuse existing UI chapters after validating
real functions, rather than assuming a chapter's name proves the behavior.

These boundaries agree with current platform guidance on
[Windows focus navigation](https://learn.microsoft.com/en-us/windows/apps/develop/input/focus-navigation),
[Apple's foundations](https://developer.apple.com/design/human-interface-guidelines/foundations),
[Android adaptive layout](https://developer.android.com/design/ui/mobile/guides/layout-and-content/adapt-layout)
and [WAI-ARIA interaction patterns](https://www.w3.org/WAI/ARIA/apg/about/introduction/).
Platform conventions are inputs to the adapters, not identical pixel targets.

### Theme families and customization

Theme packages need more than palettes: role tokens, component-part recipes,
typographic roles, icon/illustration assets and motion/sound preferences.
Keep task meaning, accessibility state and focus/hit geometry independent of
decoration. Metric changes trigger remeasurement; paint-only changes repaint.
Package version and asset hashes belong in the theme identity, with a safe
default and a preview/revert path. Corporate defaults and allowed overrides
are policy; accessibility preferences take precedence over decorative choices.

| Family | Visual language | Shared usability contract |
|---|---|---|
| Refined default | Quiet neutral surfaces, restrained accent, coherent spacing, visible focus, clear type hierarchy | Controls read as distinct controls; primary actions are scarce and obvious. |
| Company standard | Brand palette/logo, chosen body/display families, density and icon set | Administrators distribute one package without forking app code. |
| Dinosaurs | Warm ground, botanical colors, rounded shapes, fossil/footprint illustrations and optional sounds | Illustration frames content; reading text stays legible and controls stay predictable. |
| LCARS | Asymmetric rounded frame parts, segmented bands, black ground, restrained orange/lavender hierarchy | Preserve labels, focus, keyboard access and sufficiently large targets. Do not rely on hover. |
| 57 Chevy | Two-tone teal/cream lacquer, thin chrome rails, selective embossing and red accents | Keep reflective decoration on frames/selected controls; body text and data remain quiet. |

A renderer can simplify a costly shadow to a border, or a material to a solid
fill, without changing the control's role. A radial control needs an equivalent
keyboard/value-entry path. Do not turn every primary action into a decorative
gauge merely because the theme contains gauges.

### Fonts: intake, renderer repair and custom design

There are 14 TTF files in the current fonts tree. GopDesk exposes one fixed
font path; the older GUI catalog has 13 entries. New files alone do not provide
font selection, role fallback, weight selection or international text.

The fresh candidate trial used official static TrueType releases:

| Family | Intended role | Current GopDesk result |
|---|---|---|
| [Inter](https://rsms.me/inter/) | Neutral UI text and controls | Loads, but compound ASCII letters/punctuation disappear. Regular TTF SHA-256 `40D692FCE188E4471E2B3CBA937BE967878F631AD3EBBBDCD587687C7EBE0C82`, release 4.1. |
| [Source Sans 3](https://github.com/adobe-fonts/source-sans) | Humanist UI/body alternative | Same compound-glyph defect. Regular TTF SHA-256 `4644C81B86EC9CAAA76B634889968ED3C4F4F52F054855933ACC7C2B21E53B0F`. |
| [Atkinson Hyperlegible Next / Mono](https://www.brailleinstitute.org/freefont/) | Legibility-oriented option | Candidate only; no GopDesk trial in the survey. |
| [IBM Plex](https://www.ibm.com/design/language/typography/typeface/) | Cohesive sans/serif/mono corporate family | Candidate only; no GopDesk trial in the survey. |
| [Noto](https://github.com/notofonts/get-noto) | Script-specific fallback coverage | Candidate only; requires shaping, fallback and bounded lazy caches, not eager loading of every glyph. |

Inter and Source Sans licenses were downloaded with the trial fonts. Both use
OFL 1.1. Noto and IBM document OFL distribution. Preserve each exact package's
license and modification/name terms in a font manifest. The existing `fonts/cc0`
label is not accurate for every asset: the shipped CMU Sans name table explicitly
declares OFL 1.1, matching the [CMU package listing](https://ctan.org/pkg/cm-unicode).
Record license by asset rather than inferring permission from the directory.

Repair order:

1. Decode compound outlines, including nested components and transforms, with
   depth/count/range limits. Distinguish missing glyphs from valid empty glyphs.
   The active buffer reader and the general TrueType path need one conformance
   corpus. Static glyf/loca TTF is the first supported intake format; CFF,
   variable axes and color fonts need explicit support or build-time conversion.
2. Give text runs real metrics: advances with fractional accumulation, bearings,
   ascent/descent, cap/x-height and line gap. `fb-render-glyph` floors advances,
   places from the font-wide bounding box and uses 4x coverage supersampling;
   no TrueType instruction interpreter is visible on that path. Raster quality
   and line placement must be evaluated separately from glyph design.
3. Introduce font roles and family/weight/style fallback. The 95-entry printable
   ASCII cache is not a Unicode shaping engine. Add coverage reporting,
   grapheme-aware editing, combining marks, direction and shaping incrementally.
4. Build an actual-GOP specimen gallery at 14/16/18/20/24 device ppem, both scale
   regimes, light/dark grounds, regular/semibold, digits, `Il1 O0 rn m`, punctuation,
   accents and selected non-Latin scripts. Compare outlines/metrics with an
   independent mature engine; different antialiasing need not be byte-identical.

Custom fonts are feasible, but a polished text family is a coordinated design
system. Begin with shared stem widths, cap/x-height, overshoot, curves, counters,
terminal shapes, sidebearings and word spacing; derive related glyphs from shared
parts. Review words and paragraphs before expanding weights and coverage.
[FontForge's metrics guidance](https://fontforge.org/docs/tutorial/editexample5.html)
explains the separation between spacing and pair kerning. A display face for
LCARS or retro headings is a smaller first target than replacing all body text.

Use AI for controlled variations and repair proposals, with family-level
constraints and specimen review. Per-glyph image plausibility alone does not
enforce a baseline, consistent stems, spacing or interpolation across weights.
The inspected `FontAi.codex` predicts contour rectangles; `FontAiTrainer.codex`
predicts contour coordinates through an MLP. Those representations and older
backlog gaps do not establish the behavior of Damian's unidentified diffusion
model. Retraining or replacement requires evidence from the actual model.

### Proposed implementation sequence and acceptance

| Unit | Deliverable | Acceptance |
|---|---|---|
| U0: render/interaction gallery | One representative shared tree plus actual GopDesk, browser and MAUI runners; explicit support matrix | Displayed control count, labels, states and keyboard/pointer outcomes agree. Unknown controls cannot silently disappear. Native-phone execution remains required evidence. |
| U1: text and font contract | Compound glyphs, usable font roles, metrics and licensed intake manifest | Inter and Source Sans render every printable ASCII glyph, baseline/advance controls pass, no regression in CMU, bounded cache/scratch use. |
| U2: box and viewport contract | Measure/arrange, shortage handling, alignment and overflow; atomic theme reflow | Calculator's last row stays reachable; launcher and dialogs fit/scroll; paint/hit/focus agree across 1280/1600, resizes and larger text. |
| U3: semantic control states | Shared focus/actions, editing, toggles/choices, menus/dialogs and accessible metadata | Keyboard-only task completion, focus restoration, cancellation, IME contracts and equivalent touch operation. |
| U4: refined default and skin packages | Default, company, dinosaur, LCARS and Chevy recipes using the same gallery | Theme switching preserves application state and accessibility; no clipped labels or lost controls; documented cheaper-renderer fallbacks. |
| U5: backend/device conformance | HTML/MAUI parity, then genuine SwiftUI/Compose/Flutter widget lowering as selected | Real target runtime proofs, adaptive list/detail transitions and device-input tests; console-wrapper output does not count as UI parity. |

The recommended first implementation commitment is U0 plus a bounded U1
compound-glyph/metrics slice, followed by U2. Develop the refined default in
parallel as a design reference; ship broader decorative skins after those
geometry and text contracts hold. The assessment supplies no duration estimate.

Heap/time verdict: the survey changes no runtime code. New themes must preserve
the existing no-allocation idle loop and frame/retained-state lifetime rules in
`apps/works/works-desk-contract.md`. FontLoad currently materializes a byte List
in `fb-parse-ttf` in addition to its raw buffer and builds 4x supersampling scratch.
Avoid multiplying that cost by every font, weight and glyph. Use bounded glyph/run
caches, byte buffers, per-glyph scratch reclamation, viewport virtualization,
dirty-region painting and explicit budgets for blur/shadows. Measure theme
switches, resize/reflow and repeated open/close cycles before claiming a cost
improvement. Desktop screenshots do not establish worst-case frame time.

## The measurement this campaign starts from

**The UI library is far ahead of the shell.** Measured 2026-08-20 over the
cite graph: 36 of the 50 chapters in `codex/foreword/ui/` were never cited by
`apps/works/` at all, and the fourteen the desk did cite were the structural
quarter with none of the presentation. The unused list was not a list of
stubs: `Icon.codex` at 1,366 lines, `Sound.codex`, `Accessibility.codex`,
`Animation.codex`, `Dialog.codex`, `TrueTypeFont.codex`, `FontAtlas.codex`,
`GlyphRasterizer.codex`, `Vector.codex`, `Canvas.codex`, `RichText.codex`,
`TreeView.codex`, `Surface.codex` and `Focus.codex`.

**So this campaign is substantially a WIRING campaign**, and that is what
decides the work: less inventing, more connecting, and the connecting is
where the measurements go.

**The risk is integration, not correctness.** Each unused chapter was proved
against its own expectations in isolation; none was proved against the desk's
real constraints, which are a 128 MB arena with no collector, `ui-wscale`'s
step at 1600, the compositor's clip bound, and a frame budget shared with a
taskbar that repaints every second. **Each stage arms the chapter it wires
AGAINST THOSE**, which is what the chapter's own test could not do.

Two cautions on that survey, both earned:

- **A test named for a chapter is not a test of it** (L-NAMED). The 28 files
  in `codex/test/forewords/ui-*` are seven lines each: every one cites a
  chapter, calls nothing from it, prints `UI/X OK`, and has a one-line
  `.expected` containing no digit. `apps/foreword-all-compile` is the same
  claim in one file. Discount those and 11 of the 50 chapters had no asserting
  test at all when this was measured 2026-08-20.
- **`codex/foreword/ui/**` is NOT seed-affecting**, and that is measured
  rather than assumed. A seed rebuild is decided by REACHABILITY and not by
  directory (`DevelopersRulebook.md` 7); `concat-codex-self.ps1` walks `cites`
  transitively from `codex/compiler`, and the prefix `Ui--` does not occur in
  that unit. The foreword-core chapters that ARE in it are the positive
  control: the instrument can see foreword chapters and sees no UI one.
  Re-measure with a positive control before quoting this (L-COUNT); measured
  again 2026-09-08, the unit is 103 chapters and Widget, Theme, Layout, Render
  and GpuRender are none of them. What UI work DOES move is `seed/Codex.img`,
  which carries the desk and is under its own hold below.

## Standing facts about the type path

**`seed/Codex.img` carries no typeface and is deliberately NOT rebuilt.**
Refreshing that image is a RELEASE step: it moves the `seed img bytes` and
`seed img sha256` claims in `TechnicalDetails.md`, which
`build/check-doc-counts.ps1` checks at zero tolerance and which are public.
`.claude/skills/release/SKILL.md` owns it. Until it is rebuilt, `desk.ps1`
defaults to it and every dev boot shows the bitmap face, which is why a type
sweep with no `-Disk` reports fourteen identical panes and reads as "the
change did nothing".

**`fl-write-yoffsets` is a STUB.** The per-glyph y-offset table it would
write is uniformly zero and `gbf-put-text-loop` reads it and always gets zero
(L-UNCALLED). It is harmless only because every glyph is rasterized into one
common cell. A placement account built on that table's existence is built on
nothing writing it.

**`gf-gh` is the rasterized MAX glyph height**, the tallest ascender to the
deepest descender across the whole face, 126 per cent of the em for cmunss.
Almost no line contains both, so the ink a reader sees is far shorter than
the cell, and the gap they perceive is the step minus the INK rather than the
step minus the cell.

**`desk-chrome-with content w ds` IS `desk-chrome-face content w ds
gfont-none`**, so the chrome discards any face its caller holds. Lay through
`desk-chrome-face` with the face, or a measurement taken through the chrome
answers for the bitmap path whatever face the caller loaded.

**`ui-scale` steps at 1024 and `ui-wscale` at 1600, so they AGREE at 1600 and
disagree from 1024 to 1599.** Every capture in this campaign is taken at
1600, so an arm run at one width passes with a wrong scale still in place.
Run pane geometry arms at 1280 AND 1600.

### The leading question is OPEN

The complaint is that paragraph text is set with too much leading. **The
guess that the step is computed from `comp-glyph-h` rather than from the
face's metrics is REFUTED** (fester, 2026-08-21), and acting on it would have
made the leading worse. Measured at 1600 wide, `ui-wscale` 2, CMUNSS at 32
ppem, in device pixels:

| | device px |
|---|---|
| face cell `gf-gh` | 41 |
| face ascent `gf-asc` | 29 |
| face cap `gf-cap` | 22 |
| step then in force | 36 |
| **step minus face cell** | **-5** |

Read off the glass the same day, baseline to baseline was 36 in both blocks,
ink band 23 to 32, and the single 47 step is the paragraph break
(`dk-para-gap` 6 logical at scale 2). No jitter to chase.

**So the open question is not "does the step ignore the face" but "what
should baseline-to-baseline be".** That needs a target defined against the
INK and a capture to judge it on. It is not answerable from `gf-gh` and it is
not a one-line change.

Reproducing the table needs a font-carrying image, because `seed/Codex.img`
deliberately carries none:

```powershell
pwsh build/build-boot-img.ps1 -Out <font>.img -Kernel seed/Codex.cdx   # -Font defaults to cmunss
pwsh build/compile.ps1 -Src codex/test/apps/desk-leading-metrics.codex `
  -Out <out>.cdx -Log <log> -Kernel seed/Codex.cdx
pwsh build/test-run.ps1 -Kernel <out>.cdx -OutFile <out>.txt -DiskFile <font>.img
```

## Stages

Each stage names what it wires, what proves it, and what it must not break.
**Ordering is by dependency and by visual return, not by ease.**

**The section numbers here are CITED and must not be renumbered.** Source
chapters name them (`desk-pill-edge.codex` cites 6.7.1, `desk-pill-pinned.codex`
cites the task frame's stage 3) and so does `CurrentPlan.md`. A number is how a
citation outlives the paragraph it points at, so a section that loses its
number takes its citers with it.

| stage | what it is | state |
|---|---|---|
| 1 | Typography: the UI quire's glyph stack, proportional metrics, an antialiased raster path, a type and spacing scale in `Theme` | CLOSED |
| 2 | Iconography: `Icon.codex` in the launcher, taskbar, Files type column, dialog severity, status | one item open |
| 3 | Surfaces and depth: layered surfaces, elevation, shadow, rounded corners, background images | background images open |
| 4 | Sound: `Sound.codex` on the Intel HDA device, off by default until stage 5 can configure it | CLOSED |
| 5 | Settings: a typed schema, persistence through the fact store, a Settings GUI on `SettingsPanel.codex` | OPEN, whole stage |
| 6 | Accessibility: roles, focus ring, keyboard traversal, high-contrast palette, independent text scaling, an announce channel | chapter armed, nothing wired |
| 7 | Accounts and parental controls | OPEN, needs a ruling before it starts |
| 8 | Responsiveness: `Animation.codex`, transitions, a throbber, a stated frame budget with an instrument | OPEN |
| 9 | Crisp: partial coverage, antialiased stroke, honest text metrics, small type, windows, docking, the task frame | one item open, plus D.5 |

### Stage 2, what is left

**Dialog severity has no surface.** `badge-severity` answers a `BsPill` and
has exactly one caller in the tree, a test. `Dialog.codex` is unused and the
desk has no confirm or alert surface at all, so changing severity to carry an
icon would move nothing on any screen. It waits on a dialog surface, which is
a stage 3 or 5 item.

**Rules the icon work established, which the later stages take as given:**

- **`Icon.codex` cannot draw on this surface, and the reason is cost.**
  `icon-blit` paints into `Framebuf`, whose `fb-set` returns a record per
  pixel at 24 bytes each; the desk's buffer is linear XRGB behind `gop-put`
  at 0 bytes per pixel. The artwork and the lookup are reusable and the
  renderer is not. `GopIcon` is the blit, with the same column bound
  `gop-draw-text` carries, and every stage that paints an icon goes through
  it.
- **Every icon is a module-level `List Integer`, so a mention re-materialises
  the whole bitmap.** Mentioning `icon-set-standard` in a paint would rebuild
  all 63. The kit is built once per paint and threaded, which is why
  `IconKit` exists rather than a name-to-icon lookup taking a `Text`.
- **Icons ride the LAID tree, keyed on `wn-id`, painted after the walk.** It
  cannot move a hit test, needs no widget kind and no signature change, and
  is the road `desk-taskbar-clock` already takes. Reach for it before
  reaching for a new widget kind. What it costs is that an icon is keyed on
  a STRING an id has to match, so **renaming a button removes its icon in
  silence**; `desk-chrome-icons` asserts that, control included.
- **`ev-hit-widget` answers the DEEPEST node under the pointer**, so an icon
  node inside a button is what a click on the icon resolves to.
- A gutter exactly as wide as the icon puts every label flush against it, so
  the gutter is one cell wider; the icon centres on the CONTENT BOX rather
  than on the glyph height, because the glyph height depends on which face
  loaded and the box does not.
- **A picture that fills its node takes the BORDER box, not the content
  box.** Padding insets text; a picture has nothing to inset. Measured off
  the content box the status dots drew zero pixels at 10 by 10.

### Stage 3, what is left

**Background IMAGES alone**, and the dependency stands: a decode path plus
somewhere to keep the image, which is stage 5's store.

Elevation, shadow, rounded corners and gradients are not work: the desk has
an adornment system (`dk-adorn-border`, `dk-adorn-gradient`,
`dk-adorn-shadow`, `dk-adorn-round`), resolved by `dk-grad` and `dk-shade`,
painted by `comp-shadow`, `comp-bevel`, `comp-accent` and
`comp-corner-inset`, and toggleable in the Appearance pane.
`GopStyleKit`'s `DesignLanguage` carries `dl-relief`, `dl-bevel-w`,
`dl-corner`, `dl-outline` and `dl-gradient` per language.

- **Must not break: the idle repaint budget.** A wallpaper that repaints per
  frame is a regression against WORKS-37's leak fix.
- **The root panel already carries the theme's own vertical gradient**, so a
  desktop drawn as a gradient off the palette is invisible and costs an
  interpolation per scanline per repaint. Take the control capture before
  building one (L-SUSPECT).

### Stage 5: Settings

The one users see and the one the rest depends on. A settings SCHEMA (typed
keys, defaults, ranges), a persistence path through the existing fact store,
and a Settings GUI built on `SettingsPanel.codex` with categories: Appearance
(theme, wallpaper, type scale), Sound, Accessibility, Accounts and Parental
Controls, System.

- Proves: a setting survives a reboot; an out-of-range value is refused
  rather than clamped silently; the default is restored by an explicit action
  and not by deleting a file.
- It makes stages 3, 4, 6 and 7 configurable rather than hardcoded, so it
  cannot come last.
- Persistence goes through `GopFacts.codex`'s published interface. That
  chapter is red's and is not part of this campaign.

### Stage 6: Accessibility

Wire `Accessibility.codex`. Roles on widgets, a visible focus ring, keyboard
traversal of every pane, a high-contrast palette, text scaling independent of
`ui-wscale`, and an announce channel a speech path can later consume.

- Proves: every interactive widget answers a role; tab order reaches every
  control in a pane and returns; the high-contrast palette meets a stated
  contrast ratio, computed rather than eyeballed.
- **This is a correctness area, not a decoration area.** A focus ring that is
  present but invisible against one palette is a defect, and `Theme.codex`
  already warns that 36 `Palette` literals exist across 32 files.
- **The chapter is ARMED** (2026-08-21): `codex/test/forewords/ui-accessibility`
  censuses all sixteen role constructors against both `a11y-role-name` and
  `a11y-is-interactive` and pins both branches of `a11y-announce`. The census
  shape is the point, because `a11y-is-interactive` answers False through an
  `otherwise` catch-all, so a role that drifts into it is indistinguishable
  from one that was always inert unless every constructor is named.
- **Nothing wires it.** Roles, the focus ring, traversal, the palette and
  text scaling are all still open, and they are the stage.

### Stage 7: Accounts and parental controls

Needs a user model, which the shell does not have. It ties to `Identity.codex`
and the trust lattice rather than inventing a parallel notion of a user, and
that is a design question before it is a code one.

- Depends on red's identity reconciliation work.
- Deliberately last, and the stage most likely to need a ruling before it
  starts.

### Stage 8: Responsiveness

`Animation.codex` wiring, transitions on open and close, a throbber for slow
work, and a stated frame budget with an instrument that reports when a pane
misses it. `Animation.codex` is still cited by nothing: the dock and the
restore are instant.

- Proves: the budget is measured, not asserted. The desk already has a
  frame-timing readout in the 3D pane; this generalises it.

## Stage 9: crisp

Damian's direction, 2026-08-21, with a reference: his own Windows 11 desktop,
naming antialiasing, smooth corners, a clear font at SMALL size, colourful
full icons, overlapping windows, a taskbar showing common and active apps,
windows with a status bar along the bottom, ribbons and toolbars along the
top, clear menus with simple icons. He was open on how icons are made and
floated a drawing tool for authoring them. The instruction that decides the
work: **get the primitives we do not have written.**

### Drawing rules this stage established

- **`vec-coverage` CROPS a path into the grid, it does not scale it into
  it.** A drawing authored in a 32 unit box and rendered at 24 comes out as
  the top-left 24 units of itself, complete and wrong. `vec-coverage-in`
  takes the box and maps it. The cropping form is kept for a caller already
  working in pixel coordinates.
- **`gr-fill-scanline` pairs its intersections, which is even-odd, so two
  overlapping quads CANCEL.** Handing one edge list of stroke quads to
  `gr-render-edges` guts the ink at every join, every meeting cap and every
  self-crossing. Each quad is rendered on its own and merged by MAXIMUM.
  Measured on an X of two segments at width 2 in a 16 by 16 grid: per quad
  the crossing pixel reads 255, the single edge list reads 47, and a point on
  one arm away from the crossing reads 255 under both, which is what says the
  damage is local to the crossing. The rejected construction is kept in
  `codex/test/ui/vector-raster` as a row rather than described, so it is a
  runner and not a claim.
- **The same rule decides authorship.** A close mark is authored as a single
  twelve-vertex outline, because two overlapping bars cancel and an X comes
  out holed where the arms meet. A box inside a box is NESTED and is allowed,
  which is the rule that also makes a ring.
- Segments are square-capped by half a width along their own axis, which
  covers a join without a miter calculation. Width is in PATH UNITS;
  `vs-stroke-width` is not read, because it is capped at 1000 and its scale
  was never settled.
- **`gr-render-edges` allocates `width * height` on every call and there is
  no collector**, so each quad is rendered over only the rows it spans and
  the buffer is the band. **A caller rasterizes a stroke ONCE at load**, the
  way the icons already do for fills, rather than per paint.
- **A name with no drawing still draws its eight pixel mask** (L-FALLBACK),
  so the icon set grows one drawing at a time and no surface stops working
  while it does.
- `comp-corner-ins256` answers a corner's inset in 256ths and `comp-blend-px`
  mixes by that coverage, so a row with no corner takes the old path exactly
  and a radius of 6 at scale 2 costs twenty-four blended pixels for a whole
  box however tall it is.

### The type metrics, and the one item still open

**The text fit lives in the app that holds the face, not in the foreword.**
`Widget.codex` may not cite the app quire that owns the face
(`DevelopersRulebook.md`, dependency direction `codex.foreword -> codex ->
codex.os -> apps/`), and `widget-measure` returns a leaf UNCHANGED, so
nothing in the foreword ever revisits a label's intrinsic width.
`comp-fit-text` (`GopComposite.codex`) walks the tree and rewrites `wn-min-w`
for every `WkLabel` and `WkButton` from `gfont-text-w`, rounded UP to logical
units, and answers the tree unchanged when `gf-ok` is false.

**It is applied to the CHROME only, and that is a correctness bound rather
than a scoping preference.** A pane that hit-tests its own subtree does so
with plain `widget-layout`, so fitting a pane's tree on the paint side and
not on that side puts a click on the wrong widget. The fit lives in
`desk-chrome-face`, which wraps the sidebar and the taskbar and hands
`content` through untouched.

**A pinned width is an intent and a constructor's guess is not, and they land
in the same field**, so `comp-fit-node` replaces `wn-min-w` only while it
still equals what the constructor guessed. `task-clock` is pinned to 220 over
an EMPTY string, and an unguarded fit measures that empty string and reserves
nothing.

**`widget-set-min` sets BOTH fields**, so removing a pin also removes its
`min-h` of 0 and the band grows to the button's own default height of 24.
Read the guess back off the node rather than restating a constant at the call
site.

**STILL OPEN, item 3: the eight pixel cell on the PANE path.** The widget
layer sizes text by `text-length * 8` while the renderer draws
proportionally. `comp-fit-text` makes the chrome honest; every pane still
carries its constructor's guess, and the full-bleed stretch hides every
under-reservation. This has to be honest before any width constraint lands.

**The ppem sweep, kept because it is what a size argument is settled
against** (`codex/test/apps/desk-leading-metrics`, device pixels, CMUNSS):

| ppem | cell | cap | `CODEX` |
|---|---|---|---|
| 10 | 13 | 6 | 31 |
| 12 | 16 | 8 | 38 |
| 14 | 18 | 9 | 45 |
| 16 | 21 | 11 | 51 |
| 20 | 26 | 13 | 64 |
| 32 | 41 | 22 | 106 |

**A ppem is already a DEVICE size**, so multiplying it by `ui-wscale` is what
made the type exactly twice what it should be at 1600. `dk-ui-ppem` is 16 at
1600 and above and 14 below; the CBF fallback keeps its own 16 rows, because
`comp-glyph-h` is ALSO that fallback's cell height in
`gop-buf-cbf-rows-scaled`.

**Three constants that assumed the old cell, and the reasoning behind each
replacement:**

- **`dk-line-step` is five quarters of `gf-gh`** when there is a face, and
  keeps the old constant for the bitmap fallback whose cell really is
  `comp-glyph-h * s`.
- **`dk-icon-px` is four fifths of the gutter**, the remaining fifth being
  air between the icon and the first letter of the label. The old quantiser
  of `avail / 8` came from an 8 by 8 stencil block-magnified by an integer; a
  DRAWING is rasterized at any size, so the quantiser threw away up to seven
  of the gutter's pixels. The stencil fallback keeps its integer scale.
- **`dk-task-h` is the band's FLOOR, not its height.** `desk-run` lays the
  taskbar once through `dk-task-init` and caches the extent; `dk-task-px`
  answers that cell and falls back to `28 * ui-wscale` only when nobody wrote
  it. **No padding constant is written down anywhere**, which is the whole
  reason to measure rather than compute: padding is a THEME's choice
  (`edges-uniform 4` in one scheme, `edges-xy 16 8` in another), so an
  arithmetic agreeing with the engine under the default wraps at the wrong
  column under `lcars`. Measured on the glass 2026-09-08, the laid band is 88
  device pixels at 1600x900 where `28 * s` is 56. **Anything placing near the
  band asks `dk-task-px`, never the constant**; `GopScene` is passed the
  extent because it cannot cite `GopDesk`.

**A copied expression measures the test, not the code** (L-INSTRUMENT). Both
metric arms once carried their own copy of a constant the paint path had
stopped using, and each reported a disagreement that did not exist.

### One rectangle, one answer

**`ui-sidebar-px` was the pattern and the sweep is the rule.** Four chapters
once restated where the sidebar ended and no two agreed, and every wrong
version was right at some width. `GopFiles`' four screen-derived geometry
functions went the same way: every drawing and hit function in `GopFiles` and
`GopEdit` takes a `LayoutRect`, and the desk says which one it is,
`dk-pane-box` for a pane that owns the desktop and `dk-wnd-content` for one
that is a window. Two functions in each chapter carry the screen as well as
the box, because `cursor-update` clamps the pointer to the glass.

**`dk-wnd-content` is the content box, once.** Four copies of that arithmetic
were written before it existed.

### Windows: the rules a pane author acts on

All fourteen windowed panes are done. What survives is the set of rules the
conversion established, because every one of them was paid for and the next
pane pays again without them.

- **A pane becomes windowed by taking five things**: a state block with the
  window slot free; an arm in `desk-wnd-tree` returning a `WidgetNode`; its
  step routed through `desk-wnd-chrome-step`; its paint through
  `desk-wnd-paint-all`; and **its own hit test laid out at the window's
  content box rather than at the screen corner**.
- **The hit test is the one that fails plausibly.** Sabotaged back to the
  corner layout, a click on the launcher's Calculator row opened the
  AQUARIUM: a real application, no error, and exactly the wrong-but-plausible
  answer a screenshot cannot show. A click on the colour-scheme row answered
  ROUNDED CORNERS: the pane repaints, a setting changes, and it looks like a
  click that worked. **That is one measured defect per pane, not a sweep.**
- **THE BIGGER SCREEN IS THE TIGHTER ONE, BY NEARLY HALF.** A tree is laid
  out in logical pixels and `ui-wscale` is 1 below 1600 and 2 at or above it,
  so the content box goes from 1120 by 728 logical at 1280x800 to 640 by 400
  at 1600x900. Every instinct says to check the small size; panes lose their
  last line at the LARGE one. **Look at every pane at both sizes and expect
  1600 to be where it breaks.**
- **`widget-layout` places a child past its container rather than clipping
  it**, so no hit test and no count can see a row that is off the glass. The
  question to ask of a pane is not whether its tree lays out, it is whether
  its tree FITS, and **the answer is a picture, at 1280 and at 1600**.
- **A pane that PAGES rather than clips hides a bad fit from every arm and
  from the eye looking for a clipped line.** `data-table-fit` pages: at
  1600x900 in a three-quarter window the Issues pane read `Page 1 of 7` with
  two rows and every cell truncated, and with the whole box `Page 1 of 4`
  with four rows. Read what the picture SAYS, not just its edges.
- **A full-box window is still one titlebar short of the pane it replaces.**
  `dk-wnd-wants-box` gives a pane the content box and the window then spends
  `dk-wnd-bar-h` of it, so a tree that exactly filled the region overflows by
  about a line. Expect that trade on every pane whose tree was written to
  fill the region; deleting a line is often the right repair.
- **A converted pane must not keep its own title bar.** Under the desk's
  titlebar that is two bars saying the same word and two `x`es at different
  heights meaning different things. A pane's own bar stays as a LOCATION: the
  window says which application, the bar says which directory or which file,
  which is the one thing the window title cannot know.
- **A drawing pane's tree costs exactly what its draw could reach.** Absolute
  `gop-draw-text` reads whatever its caller had; a tree is built by the
  painter, which holds the screen and `ds` and nothing else. Sort the rows
  three ways: what the tree can compute for itself stays in the arm; a fact
  about the MACHINE that will not fit in 32 bits joins `desk-wnd-tree`'s
  arguments (a framebuffer address is 64 bits and there is no `poke-qword`);
  what is settled before the base mark goes in the block. **The last kind
  fails quietly**, because an unwritten block cell reads zero and zero is
  indistinguishable from a machine that genuinely lacks the hardware.
- **A heavy pane must repaint itself when the chrome answers `stay`.** A
  raise, a Tab cycle or a maximise repaints every window and draws a heavy
  pane's empty body panel. A 3D pane recovers on its next frame; a pane that
  only redraws on an event has none, so it sits blank looking exactly like
  one that crashed.
- **A repaint under a shown cursor is undone by the cursor's own restore.**
  Hide the cursor first.
- **The key is Tab, not Alt-Tab.** The desk tracks no modifier state:
  `kbd-take` answers a scancode and nothing accumulates a shift or an alt
  across calls. Alt-Tab is a keyboard-layer item, not a shell item.
- **THE REVIEW PANE IS NOT WINDOWED AND CANNOT CHEAPLY BE.** `grv-tree`
  takes `GrvData`, which `grv-load` builds by mounting the volume and
  scanning every proposal, verdict and supersession on it, plus a signer from
  `loaded-fingerprint`. So `desk-wnd-tree` would gain `Identity` and widen
  through the painter and every step; and the painter builds the tree of
  EVERY open window on every repaint, so a Clock ticking beside a Review
  window would scan the medium once a second. **Its tree is a function of the
  MEDIUM, not of a block**, and making it work means caching the `GrvData`
  where the painter can reach it, which is a `DeskApps` field and a signature
  change through the window machinery.
- **`dk-wr-max` is 15, and the 4 it replaced was not a choice about how many
  windows a person may have.** The default rect leaves an eighth of the
  content box as margin, and at 1280x800 that is 91 device pixels vertically
  against a step of 24, so the fifth window's offset of 96 left the box: the
  cap was that quotient. The cascade wraps each axis in its OWN room, because
  wrapping both together puts the window after the wrap exactly under the
  first; separate spans, 6 and 4 at 1280x800, repeat only after twelve. 15 is
  the registry block's own bound, 64 bytes at four for the count and four an
  entry.
- **The window registry is an array of focus ids in a block, and THE ARRAY
  ORDER IS THE Z ORDER.** That deletes `win-z` outright along with every
  question about two windows sharing one, and makes a raise into
  `dk-wr-raise`: slide the entries above one index down, write the id at the
  end, six lines and no allocation. **`Window.codex` was deleted** (red's
  ruling, 2026-08-25) because it is a functional record and durable desk
  state may not live in one: `desk-app-close` restores to the base mark and
  rebuilds `apps` fresh, so a manager threaded through `desk-loop` is a
  dangling pointer from the first pane exit. A future heap-owning caller
  refetches it from history.
- **The 3D panes resize without reallocating.** `r3d-target-at` models a
  target whose row stride is independent of its width, so the colour and
  depth buffers are allocated ONCE at the content box, the largest rectangle
  a window can take, and a smaller window renders the top-left of them.
  `gsc-blit-rows` takes the source stride. A target reallocated per resize
  strands about 4 MB above the pane's own heap mark until it closes.
- **A pill's icon is a SECOND table keyed by focus id, not a join.** Every
  window title is a launcher label, so a join against `gpr-entries` would
  work, but each mention of that list rebuilds all its entries (696 bytes,
  measured 2026-09-23) per pill per repaint, where the table costs nothing.
  `codex/test/apps/desk-chrome-icons` holds the two to one answer: each pill's
  icon must be the launcher's icon for the row of the same name.

**UNMEASURED, and named rather than assumed: what the drag repaint RATE costs
ON METAL.** A window move calls `desk-wnd-repaint`, the same full repaint the
raise and maximise paths already call, so the per-event cost is not new; the
rate is, because it is now per mouse sample rather than per click. This file
records a desk paint as near a second on metal against about 16 ms in the
bed. If it is too slow there, the answer is the outline drag every
pre-compositing desktop used, and that is a second unit rather than a repair.

**UNMEASURED, and worth one run.** `desk-wnd-walk` paints every
non-minimised window through `desk-wnd-one`, and `dk-wnd-frame` fills the
body with `pal-bg` before rendering the tree. For a heavy pane that tree is
empty, so a full repaint while a heavy window is open and NOT focused should
leave it a blank framed rectangle until its own step runs again. Open two
heavy panes, focus the second, force a repaint and photograph the first.
**Do not report this as a defect before that run**: it is a prediction from
reading, which is the thing L-MECHANISM says to distrust.

### The start menu shows at most six rows of a group

**The open group is a window of `gpr-open-max` (6) rows that follows the
selection, and its heading says which rows (`- Productivity  3-6 of 7`).** Six
is what the tightest glass fits: 1600x900 lays the chrome out in 800x450, where
six rows put the band at 406..442 and a seventh pushed it to 454, off the glass.
With the window, a seventh Productivity entry leaves the band at 406..442
(measured 2026-09-23 with a temporary entry), so the group an app joins is a
naming decision again. `codex/test/apps/desk-menu-groups` pins the window at a
cap of four, at both ends of Productivity. A row outside the window is absent
from the tree, so a count, an id lookup and the hit test agree that it cannot
be clicked; up and down still reach it through the selection.

What the window does not cover: the number of groups. Every heading is one
row, so enough groups overflow the same glass, and `flex-col-place` still
places a child past its container rather than refusing (the layout layer).

**Two traps in this area, both paid for.** An empty flex-1 `widget-panel`
PAINTS its own background, so an anchoring spacer must be a label or a
`widget-spacer`. And **an over-tall menu PUSHES the taskbar down rather than
overlapping it**, so an arm comparing the menu against the band agrees on the
broken build; the row that discriminates compares the band against the GLASS.
`works-desk-contract.md` carries both where a pane author meets them.

### The task frame

Damian, 2026-08-27: *"the task bar, as it was, is now more of a task frame.
the whole edge of the OS should be dockable like that, with the bottom being
the default placement."*

| | stage | state |
|---|---|---|
| 1 | the band docks to any edge | DONE |
| 2 | the flick | DONE |
| 3 | hot-launch pills | **mechanism landed.** A pinned app has a pill with no window; `dk-pill-live` is the union the four walks ask. **NOT closed: there is no pin gesture, and `dk-pill-hit` / `desk-pill-icons` are covered by no arm** |
| 4 | the Cobblestone button's position | **OPEN. It is five variables, not one** |

**6.7, per-edge pill strips: DONE.** A pill's edge is a per-app FACT (6.7.1)
in a block
of `dk-focus-max + 1` entries keyed by focus id, written at dock from
`dk-flick-dir-cell` and read by the band; `dk-strip-px` answers whether an
edge takes glass, and `desk-chrome-face` is one construction with an optional
node per edge. **The fact is keyed by FOCUS ID and not by registry index**,
because the registry's order is the z order and changes when a window is
raised: an edge keyed by index moves a pill out from under the pointer that
just clicked it.

**A strip is the SAME depth as the band on both axes**, since the Cobblestone
button and the clock add nothing beyond the floors and a pill's own height,
so there is no thinner constant to invent. Four edges each carrying a strip
is four bands' worth of content box gone: at 1600 the band is 56 device
pixels deep, so strips on all four edges take 112 from each axis, and a
window is three quarters of the content box.

**What still bites the moment many are open is the pill row.** It is built
from the registry into a flex row beside `tasks`, so a taskbar holding a
dozen pills needs the band to answer what happens when they do not fit, which
is the same question the start menu's overflow asks.

### Stage 4: five variables, and the ruling is Damian's

Damian, 2026-09-07, at the running desk: *"we have the docking for the start
menu, then the orientation of the open tasks versus all tasks is a variable
too. so like in this example maybe i want the cobblestone button default, but
open tasks to go up the page and clicking cobblestone menu opens its list in
the horizontals instead of like in windows going only vertical and up."*
Then: *"we want it to also have 'float over' the existing layout when you
open the cobblestone menu, or 'embed in' the layout, which causes the
shifting of everything now. in that mode, there should be a scroll bar and
virtual space to keep the layout of the existing window for the apps opened
already from having to relayout. but then the one that doesn't float can be
opened permanent or only when clicked, and dismisses when something is
launched or the cobblestone menu dismissed (esc)."*

**Today there is ONE variable and three things are welded to it.**
`dk-task-edge` decides `DirRow` against `DirColumn` in `desk-taskbar`, so the
pills flow whichever way the band runs; the Cobblestone button is the band's
first child, so its position falls out of the same choice; and the menu reads
the edge not at all. The stage name reads as one setting because the three
agree by accident.

| | variable | today | Damian's example |
|---|---|---|---|
| 1 | the Cobblestone button's dock | first child of the band | default, bottom left |
| 2 | the open-task pill flow | the band's own axis | up the page |
| 3 | the all-tasks list orientation | vertical, always | horizontal |
| 4 | menu presentation | in the layout, painted over the windows | float over, or embed in |
| 5 | menu persistence, embed only | transient | permanent, or dismissed on launch or Esc |

Axis 2 is half built: WHICH edge a pill lands on is already a per-app fact.
What is missing is the flow direction within a strip.

**The menu anchors at the button and the button has height on the side
edges**, both pinned by `codex/test/apps/desk-menu-anchor`. That arm's claim
is the GAP TO THE BAND, not equality of left edges: the theme pads the band's
button by 14 and the menu column by 16, so an equality test reads NO where
the placement is right. **An equality test whose honest answer is a near miss
is not an assertion.**

**`codex/foreword/ui/Scroll.codex` is 12 KB of working scroll and the desk
has never used it.** A viewport and content `ScrollState`, thumb geometry on
both axes, `scroll-visible-rect`, page and row helpers, and
`scroll-slice`/`scroll-take`, which answer only the visible children of a
list. `Browser`, `Tab`, `FilterableList` and `GopReview` cite it.
**`desk-menu-groups.codex` says "nothing in the widget layer clips or
scrolls" and that is half wrong**: there is no clipping, `Widget.codex`
contains none, but there is scrolling. Anyone costing axis 4 from that
sentence would build an engine that exists.

Scroll here works by SLICING A CHILD LIST rather than by clipping a viewport.
That is the right primitive for the menu's own list and the wrong one for
keeping open apps still:

- **Opening the menu does not reflow open windows today and cannot.** They
  are absolute rects (`dk-wnd-rx/ry/rw/rh`, device pixels) painted by
  `desk-wnd-paint-all`, not laid children. What shifts is the chrome, because
  the menu is passed as the CONTENT to `desk-chrome-face`.
- **So "virtual space so the apps do not relayout" is not a relayout problem.
  It is a VIEWPORT ORIGIN for the window layer**: one offset added at paint
  and at hit test. That does not exist and is the only genuinely new
  mechanism in stage 4.
- It needs no clipping. The desk already tolerates a window hanging off the
  glass and keeps the band on top by paint ORDER (`works-desk-contract.md`
  section 2).
- The scrollbar itself is nearly free: `scroll-thumb-x/y/w/h` is the geometry.

**The cost shape to expect**: the per-edge work moved `dk-cbox-*` to take
`ds` at 40 call sites in `GopDesk` and 43 across seven test chapters. A
viewport origin read by every paint and every hit test is that shape again,
which is why this stage is sized in call sites rather than in functions.

#### OPEN, AND DAMIAN'S ALONE: which combinations are supported

Five independent settings is a combinatorial surface, and every combination
is a layout that has to be right on the glass: a horizontal menu list docked
to a vertical band with pills flowing up is a real arrangement someone can
select. Storing them is nothing, the `ds` block has two free cells (248 and
252) and each variable is a few bits. The cost is entirely in which
combinations we commit to. **Name the supported set deliberately rather than
claiming all of them and finding the bad ones on the glass.**

### 6.4: WHAT IS STILL OPEN

**This list is the only one. Do not restate it beside the code it
describes.** A "what remains" list written beside the work is a summary of a
register, and one of those went stale twice and misrouted the commander into
dispatching a lane to build a gesture that had already shipped.

| item | state |
|---|---|
| a virtual desktop space to move into (Damian, 2026-09-07) | **WAITS ON HIS WORDING.** Two readings, several virtual desktops with a switcher, or a desk larger than the screen. Root carries the question; neither is built until he answers |

**The stranding ruling, Damian 2026-08-27 evening.** Buried heap marks become
reclaimable and close-from-a-pill stops lying. The three cheaper options,
accept the stranding, refuse to close anything but the top pill, or tombstone
the pane and sweep it when its mark surfaces, are all declined; the
recommendation carried upward was the third.

**The flick's direction IS the selection criterion**, in his words *"yes on
the flick direction is the selection criterion"*, and the per-edge strips
that ruling forced are built. **The hover preview is the app's decision with
a default**, in his words *"the hover preview should be decided by the app,
and in default should be a mini-render of the whole floating there by the
pill"*, and that is built: `dk-prev-own` names the panes that supply their
own, every other pane gets a snapshot taken at the moment it stops being
visible. **A snapshot default is content-blind, so it is equally right for
all twelve windowed panes**, which is what stops a preview that works for the
Calculator and is blank for Files from shipping as the item.

## Option D: the allocator, and D.5 is what is left

D.1 through D.4 are landed. What a reader still needs from them:

- **The root leak is fixed and the cycle table is FLAT IN N.**
  `desk-root-reclaim` frees the root a rebuild replaces only when no LIVE
  mark-stack entry sits at or above where that root ended. **The obvious
  formulation, comparing the frontier to `root-end`, is provably INERT**,
  because every site allocates after the root and `desk-loop`'s per-iteration
  mark sits above `root-end`. It failed closed, which is the direction to
  fail in, and only the measurement said so (L-FALSIF).
- **The span accessors ship**: `desk-marks-extent`,
  `desk-marks-extent-sum`, `desk-span-holds-root` and `desk-span-reusable`,
  pinned by `codex/test/desk-span`. **The sum is `top` minus ENTRY 0's mark,
  not frontier minus BASE**: the desk's own blocks and its first root sit
  below entry 0 and belong to no span. **And a span is not a hole**: the live
  root can be buried in one, so `desk-span-reusable` is the dead test AND the
  root test, never the dead test alone.
- **The guard already ships and costs 3.9 per cent of code size.**
  `heap-bump-reg` and `heap-bump-imm` append `deck-guard-code` after EVERY
  heap bump, behind `deck-guard-enabled` in `Core/BuildSettings.codex`, and
  that flag is 1: four instructions, a disarmed-cell test that skips, a
  `cmp r10, ceiling` and a `UD2`. Ablated at the flag 2026-09-08 on seed
  27D2386F7AF76F0C, two compilers from the same source by the same seed, each
  compiling the 3,154,973-byte self-concat: **code size +120,864 bytes on
  3,102,429, exact. Time is BELOW THE NOISE**, ten runs each, both arms
  sharing a 5.08 s minimum, medians 5.23 against 5.20, against a within-arm
  spread of 5.08 to 5.88. **A difference smaller than the spread of repeats of
  the same point is not a reading.** A scripted ablation here fails unless the
  pattern hit count is 1: the first attempt wrote the flag to a depot
  read-only file, the write was silently denied, and both arms compiled
  identical bytes.
- **The guard's fire is a FAULT (`!EXC=06`), not a branch a caller can
  take.** A battery arm cannot express it, its pass condition being a fault,
  and it needs no new firing arm because `PhaseAllocator.codex` already names
  the sabotage that produces one.

### D.5: reuse a hole, and the extent is the STEP

A pane open picks a dead buried entry whose hole fits instead of pushing a
new mark.

**The bound cannot be armed on the hole alone.** The ceiling the shipping
guard compares against is a statement about the DECK CURSOR, not about the
frontier. `PhaseAllocator.codex` records the defect measured 2026-08-27,
where arming it at the reservation made the guard fire on the first ordinary
allocation after every deck and the guarded compiler could not compile
itself, `!EXC=06` at R10 0x14ADAECC: **outside an extent R10 is the bivy
frontier, which lives ABOVE the reservation by construction**, so a ceiling
set to a hole's top condemns every later allocation that legitimately sits
past it. **Therefore reusing a hole means ENTERING it**, moving R10 into the
hole for as long as the pane owns it, which is what `__deck-enter` and
`__deck-exit` already do on the nesting counter's zero crossings.

**The extent is the STEP, not the pane's life.** A deck extent is lexical and
LIFO while a pane's life is neither, and that is true of the LIFE and is the
wrong thing to bracket. **A pane only ALLOCATES inside regions that are
already lexical, and there are exactly two: its `-open`, and its step.** The
desk calls the step and the step returns, once per iteration, from one site
(`desk-step-of`). So **ownership of a hole is DATA**, recorded in the mark
stack entry that already names the pane's focus id, and the extent is entered
and exited around the step each iteration. Enter and exit then nest at depth
at most one, in the order the counter assumes.

**Ownership needs no new field**: a dead entry carries `desk-focus-none`, so
claiming a hole is writing the opening pane's focus id into the entry, which
is what makes `desk-span-reusable` refuse it afterwards.

**What this does not cover.** `works-desk-contract.md` section 0 lets a step
store durable state and answer NEGATIVE to keep the iteration's frame, which
is how Files carries a directory change. Under the shape above that
allocation happens while R10 sits in the hole, so it is exactly the case that
can overrun, and the guard's fire is a fault rather than a branch. **The
first sizing rule is the one to build:**

- **The hole holds only what `-open` allocates**, and a step that wants
  durable growth is allocated at the frontier as it is today. Simple,
  provably safe, and R6 falls by less for a pane that grows, which the
  acceptance delta tolerates because the control sequence does not grow one.
- The alternative, sizing the hole with headroom at `-open` from a per-pane
  figure and excluding an unbounded pane (Files, the Browser) by name, is
  compacter and buys a number that has to be MEASURED per pane. It is
  declined for now because a headroom figure is exactly the per-object cost
  that means nothing until multiplied by a count (L-PEROBJECT), and no count
  exists.

**IT IS NOT SEED-AFFECTING.** Measured 2026-09-08 by
`codex/test/apps/deck-hole-enter`: an ordinary program enters a block it
already owns with `__deck-set` then `__deck-enter`, allocates from it, and
leaves with `__deck-exit`. All three are builtins the compiler already has,
and the ceiling `__deck-enter` arms is read from
`deck-reservation-top-cell`, a FIXED PHYSICAL ADDRESS (36344) that any
bare-metal program writes with eight `poke-byte` stores. `build` in
`Core/PhaseAllocator.codex` is the RESERVATION primitive and is not wanted
here, so nothing cites the compiler and no compiler source changes. **So D.5's
allocator half wants no token, no scratch fixed point and no BVT** unless the
desk change itself grows to touch compiler source. The arm carries its own
control, because an allocation taken BEFORE the extent must not land in the
block or entering would prove nothing.

**What is built.** `desk-span-pick ms top r want i` answers the first reusable
span that fits or -1, **first fit by design**, because a best fit's answer
moves when an unrelated pane opens and the acceptance delta would move with
it. `desk-hole-enter ms top r want` picks a span, arms the ceiling from its
top and enters, answering the index or -1; `desk-hole-exit ms i fid` leaves
and claims the entry. `codex/test/desk-hole` grades both on REAL memory, with
the control that an allocation taken before the hole lands outside it.

**THE 3D PANE IS WIRED AND THE ACCEPTANCE IS MET.** `desk-scene-open` asks
`desk-hole-enter` for a span sized by `desk-scene-want`, pushes a mark only
when no hole is found, and claims the entry on the way out. Its `want` is
`bw * bh * 8 + 200`, measured by `codex/test/apps/scene-open-cost` at two
sizes; the wiring asks for a page more than that, because undersizing traps
rather than refusing.

**EVERYTHING A PANE KEEPS FROM ITS OPEN IS BUILT BEFORE `desk-hole-exit`,
its `DeskApps` record and the `Just` box in it included.** A reused entry sits
BELOW the live panes' marks, so anything the open allocates after the exit
lands at the frontier, above every mark, and the next close of a pane above
restores under it: measured 2026-09-24 on the desk, a Files or Edit reopened
into its hole vanished when the pane above it closed, until the three opens
built the record inside the hole.

**The first pane wanted a bounded `-open` and a step that does not grow
durably**, and only a heavy pane has a mark to bury at all, which is what
chose the 3D pane: `r3d-target-at` allocates its buffers ONCE at open and a
smaller window renders a sub-rect, so nothing grows afterwards. Files is the
subject of the ORIGINAL table and reads as the obvious candidate, and it is
the wrong one: its `want` is data-dependent, which is the open item above.

**Measured on the desk under codex-vm at 1600x900**, both builds driven by the
SAME mouse timeline, read through the Monitor's own rows with the Monitor open
in every arm so its allocations cancel in the differences. R5 is the buried
close, the 3D View closed with the Text Editor alive above it; R6 is the
reopen after it:

| build | R5 | R6 | R6-R5 | the mark stack at R6 |
|---|---|---|---:|---|
| depot, unwired | `0x2aa4a8b` | `0x3283b9b` | **+8,253,712** | depth 3, dead `0@0x1362eb7` STRANDED under a fresh `12@0x28a23c3` |
| wired | `0x2aab5a3` | `0x28f188b` | **-1,809,688** | depth 2, `12@0x1362eb7`, the dead entry REUSED at its own address |

**Files and Edit, read the same way on 2026-09-24** (seed 11ACE35C, 1600x900, no `-rtc`, the Monitor opened LAST in every arm so its own open is common to R5 and R6). The subject pane is opened first, the other one above it, the subject closed while its mark is buried, and R6 reopens it. The control builds are GopDesk#180 for Files (before S5b) and CL 26741 for Edit (before the hole):

| pane, build | R5 | R6 | R6-R5 | the mark stack at R6 |
|---|---|---|---:|---|
| Files, unwired | `0x2799665` | `0x27d37a5` | **+237,888** | depth 3, dead `0@0x1e17081` STRANDED under a fresh `10@0x277f565` |
| Files, wired | `0x277d155` | `0x27800e5` | **+12,176** | depth 2, `10@0x1e17081`, the dead entry REUSED |
| Edit, unwired | `0x2787415` | `0x30dc3e9` | **+9,785,300** | depth 3, dead `0@0x1e17081` STRANDED under a fresh `14@0x276d315` |
| Edit, wired | `0x277d135` | `0x277f775` | **+9,792** | depth 2, `14@0x1e17081`, the dead entry REUSED |

Both reopens push no mark, and the residual is attributed. Above the root, R6 is byte for byte the never-closed state: with Edit and Files alive and no close in the history, the frontier stands 141,600 bytes above the root, as it does after the close and reopen, so that part is the live pane's window and pill. Under the root, a close that restores nothing used to strand its `DeskApps` record, 120 bytes a close-and-reopen cycle; the next close now reclaims it (`desk-apps-reclaim`), and the root stays at one address over one, two and three cycles. The root of the never-closed control sits 137,072 bytes higher than the reopened one, which is the reuse.

**R5 moved 27,416 bytes between the two builds**, which is the row this design
predicted would stay put, and it did. **The mark stack is the part to read
rather than the total**: a reopen that pushes NO NEW MARK is what the fix had
to produce, and a frontier figure alone could not have said so (L-GAP, which
is why R6 rather than R5 was made the acceptance row in the first place).

**`r` in `desk-hole-enter ms top r want` is the live ROOT address**, which
`desk-span-reusable` needs for its root test, because a span holding the live
root is not a hole. `codex/test/desk-hole` passes 0, which is the fixture's
"no root in range"; a live caller passes `desk-root-cell`.

**THE FILES OPEN'S `want` IS A CONSTANT since S5a**: 128 bytes at the root
and in a subdirectory (`codex/test/apps/files-open-cost`), because the pane
keeps a count and the count walk is bracketed. **An allocation past an armed
ceiling TRAPS rather than refusing**, so what Files' hole must also hold is
its step's peak, which S5a bounds by one screenful of rows and not by the
directory. `codex/test/apps/files-prepass-cost` measures an UNBRACKETED
metadata walk (2,320 bytes against 2,544 for the listing), which is why a
walk whose bytes stay at the frontier was refused as a sizing pre-pass; the
bracketed count keeps none.

**THE DESK-SIDE CEILING IS BUILT.** `desk-hole-top ms i top` answers span
`i`'s ceiling, `desk-hole-room ms i top` the bytes between the cursor and
that ceiling, and `desk-hole-fits ms i top want` whether `want` bytes fit.
A caller asks before it allocates, therefore an overrun is a Boolean the
pane can branch on rather than the `!EXC=06` the armed guard raises. The
probe is `alloc-bytes 0`, which answers the cursor without advancing it;
OUTSIDE an extent that cursor is the bivy frontier, which lives above any
hole by construction, so a caller holding no hole reads zero room and is
refused on the same branch as `-1`.

`codex/test/desk-hole` grades all three on real memory and carries the two
controls that make the refusal mean something: the room is PRINTED beside
each verdict, so a refusal is visibly a want past a known room rather than a
constant False (L-VACUOUS), and the probe is read twice with nothing between
it, because a probe that moved the cursor would make every later room wrong
in the direction that looks correct. Measured: room 8192 on entry to an
8192-byte hole, 8128 after a 64-byte allocation, 0 once the extent is left,
and the allocation a refused pane then takes lands outside the hole.

Files is sized and wired by S5b of "Sizing and viewports".

**Making the guard SPILL instead of trap was proposed and is refused**, so
that it is not re-proposed: the ceiling is the COMPILER's, armed after every
heap bump, and turning its fault into a silent continue removes a check
nothing currently measures, which is a change indistinguishable from a fix
by any aggregate (L-CAPABILITY-LOST). It is also seed-affecting for a desk
problem.

**One further question this section does not answer, recorded rather than
assumed away:**

- **What residual passes.** "R6 falls toward zero" is a direction, not a
  threshold. If wiring one pane takes `2,465,912` to `400,000`, nothing here
  says whether that is acceptance. Decide it before the run, or the run
  decides it afterwards.
**THE MARK STACK UNDER REUSE IS NOW MEASURED, and one of the four answers is
a defect.** `codex/test/desk-reuse` drives the shipped `desk-hole-enter` and
`desk-hole-exit` on real memory into the arrangement this design could not
otherwise produce: a pane named by a BURIED entry, under a live one, having
pushed no mark of its own.

Sound, and pinned so a repair has a control: `desk-marks-find` locates the
reusing pane at the buried entry; the reuse pushes no mark, so the depth is
unchanged; the marks still ascend and the extent stays positive; a live
entry above stops `desk-marks-reclaim` outright; killing the live entry pops
that entry alone; and killing the reusing pane restores the SPAN BASE, which
hands back the hole exactly rather than the address the reusing pane's own
bytes began at. A released span is picked again, which is the control line.

**`desk-marks-remark` was the defect, and the corruption half is now
guarded.** An entry a pane reused carries the span base, and remark used to
write the caller's current frontier over it: with 4,096 bytes allocated
since the open, the extent went from 8,224 to -4,096, and the entry then
named a range that was not the pane's, so a later reclaim of that entry
would restore the heap to the remarked address and never return the bytes
below it. Remark now refuses a mark at or above the next entry's or below
the previous entry's, and answers which branch ran: **1 wrote, 0 no live
entry, -1 refused**, because both refusals write nothing and a caller cannot
otherwise tell a refusal from a write (L-BAILVALUE).

**The residue is open and is WORKS-67.** A refused remark leaves the entry
naming an address the pane's state has left, which is WORKS-59's failure in
the other direction, so the guard makes the combination safe to discover
rather than correct. A reusing pane that moves its state has nowhere
legitimate to record the move, and giving it one needs a bit the 8-byte
entry does not have. Nothing reaches this at head: the only remarking pane
is the Browser, the Browser is never given a hole, and its live entry is
therefore always the top one, which is the shape `codex/test/desk-reuse`
carries as its control.

**A Files directory change retains the two navigation lists and the entered
name, not a listing** (S5a): the count walk is bracketed and the paint after
it is bracketed.

### 6.4's frontier table: the acceptance arm

**A frontier reading after the close CANNOT see the fix option D actually
is.** `__alloc` is a bump pointer, so reclaiming a buried mark cannot LOWER
the frontier; it can only make the hole available to a later allocation. An
arm accepting on "the stranded row must drop" asks a bump allocator for
something it will not do even when the fix is perfect, and would report a
correct fix as a failure (L-GAP). **R6 is the row that can express it**:
after the buried close, reopen the pane. **Under option D R6 must fall toward
zero and R5 is expected to stay where it is.** Both rows are acceptance, and
they say opposite things about the same fix, which is why neither alone is
enough.

**Measured 2026-08-28 at main 20522 on seed 8769F31E**, through the Monitor
pane's own heap readout with the Monitor open in every arm, so its frame
allocations appear in all of them and cancel in the differences:

| state | heap frontier | against baseline |
|---|---:|---:|
| R1 baseline, desktop with the Monitor only | `0x13bead3` | -- |
| R2 Files opened then CLOSED at the top of the stack (**the control**) | `0x13bead3` | **0** |
| R3 Files DOCKED | `0x1520f4b` | +1,451,128 |
| R4 Files docked, Edit opened OVER it | `0x208f6a7` | +13,437,908 |
| R4b Files RESTORED from its pill, Edit still alive | `0x1fb00bf` | +12,522,988 |
| R5 Files CLOSED with Edit alive above it (**the subject**) | `0x1f86a17` | +12,353,348 |
| R6 Files REOPENED after that close | `0x21e0a8f` | +14,819,260 |

| quantity | value |
|---|---:|
| the control, a close at the TOP of the stack (R2-R3) | -1,451,128 |
| restore from the pill (R4b-R4) | -914,920 |
| the buried close (R5-R4b) | -169,640 |
| **the reopen after it (R6-R5), D.5's acceptance number** | **+2,465,912** |

**ACCEPTANCE IS A DELTA, `R6-R5 = 2,465,912`, NEVER AN ABSOLUTE FRONTIER.**
The absolute column has since moved: D.3 added a `marks` row to the Monitor
and the preview work added a `preview` row and 626,688 bytes of boot
allocation. Nothing that matters moved with it, because the boot block and
the Monitor are present in every arm and cancel in every difference.
**Re-take the absolute column before quoting a frontier; do not re-take it
before quoting a delta.**

**R4b is why the close and the restore are separable and it is not
decoration.** R5 minus R4 alone conflates them, because the subject arm
restores Files from its pill before it can reach a close button.

**Against which envelope, because the bed is generous and the artifact is not
(L-ARENA).** These readings come from codex-vm with about 3 GB; the flying
boot image runs heap and stack in ONE 128 MB region. At about 2 MB each the
mark stack's eight slots are about 16 MB, or 12.5 per cent of that region,
before any stranding.

**AND THE INSTRUMENT ITSELF MUST NOT MOVE UNDER THE TABLE.** The Monitor is
what every reading is taken through, and its own frame allocations are what
cancel in the differences. Adding a row to it stops them cancelling against
anything recorded earlier: every number shifts, and the shift looks exactly
like a result. Take the table with the Monitor exactly as it is, record it,
and only then give the Monitor anything new. If the two ever happen the other
way round, the table is re-taken twice, once on each Monitor, and the pair
compared before anything is concluded (L-COUNT).

### Whether a buried live root actually arises is OPEN

The guard ships regardless, because it is one comparison and
`codex/test/desk-span` proves it discriminates. But **the mechanism is from
reading and the live arm did NOT confirm it**, and the difference is the
whole of L-MECHANISM.

The argued sequence: Files opens, so `mark_F` is the frontier and the current
root sits below it; Files is minimised, and `desk-app-hide` builds a new root
above `mark_F`; the Editor opens, so `mark_E` is above that root. The live
root is then inside `[mark_F, mark_E)`, and allocating into that span would
free the root the desk is painting from. **A pane open does not rebuild the
root** (`desk-edit-open`, `desk-files-open`, `desk-browser-open` and
`desk-scene-open` each push a mark and pass the `root` they were handed
through); a root is rebuilt only by a close, a hide, a `-reenter` or a pill
restore.

Taken live 2026-08-28 at main 20522, the `marks` row read `depth 2
root 0x1d8cc07   10@0x121c50b 14@0x1364cfb` with the frontier at `0x20cd49f`:
**the root is NOT inside `[mark_F, mark_E)`, it is above `mark_E` in the TOP
span.** What the arm did confirm is the accessor's arithmetic on live data:
span F is 1,345,520 and span E is 14,059,428, summing to 15,404,948, which is
exactly frontier minus `mark_F`.

**Why an arm shaped like that cannot settle it.** Reading the marks costs a
Monitor, and opening the Monitor is itself desk activity: between the
Editor's open and the reading, one of the eight `desk-draw` sites rebuilt the
root above `mark_E`. **The `marks` row prints only the CURRENT root and not
which rebuild placed it**, so this arm cannot name the site and neither will
another shaped like it. The probe perturbs the state it observes.

**The instrument that would settle it is a LATCH** (L-BANK): at the moment a
pane's `-open` pushes its mark, record the then-current `desk-root-cell` and
the then-top mark into two spare cells, and let the Monitor print the latched
pair afterwards. That is D.5's to build if D.5 needs it, and it is not a
blocker.

### How to drive the desk for a reading

The arm is `tools/codex-vm.exe` invoked directly with `desk.ps1`'s own
arguments plus `-mouse-file`, `-headless -screenshot <bmp> -screenshot-delay
<ms>`, and `-rtc` to freeze the clock so the Monitor paints once. Cost
several boots to work out.

- **The `x,y` in a mouse timeline are NOT screen coordinates.** The host
  tracks a position from `0,0`, each event SETS it, and the guest receives
  the DELTA clamped to +-127 per sample (`OperatorsManual.md`, `-mouse`). The
  guest pointer starts CENTRED, so a move is a run of samples whose numbers
  are a running total, not a destination. A single event naming the target
  moves 127 pixels and stops.
- **A 32000 ms screenshot delay captures** (2026-09-23, seed
  BD66718CBE24F589, 1600x900 with `-disk`); fit the timeline inside the delay.
- **A pane cannot be opened by keystroke.** Click the Cobblestone pill, then
  the group, then the row.
- **THE MENU IS BOTTOM-ANCHORED, WHICH MAKES EVERY ROW COMPUTABLE AND EVERY
  RECORDED COORDINATE PERISHABLE.** Measured off the glass at 1600x900,
  2026-09-09: rows are at device x 144, Shutdown is always at y 762, the last
  row above it is always at 694, and rows stack upward at a pitch of 56. So a
  row's y is `694 - 56 * (rows above it)`, and it MOVES when a group expands,
  because expanding one collapses the last and the whole list re-lays. Read a
  row's position from the state the arm has actually reached, never from a
  number written down for a different state.
- **The walk to the pill, which is the start of every arm.** The pill centre
  is device `90,848`. From the centred start that is six samples, each inside
  the +-127 clamp, with the host's tracked position running
  `-118,66` `-237,133` `-355,199` `-473,265` `-592,332` `-710,398`.
- **The initial menu has Accessories expanded**: Accessories 246, Clock 302,
  Calculator 358, Calendar 414, Aquarium 470, Sheets 526, Productivity 582,
  Graphics 638, Settings 694, Shutdown 762. Clicking Settings at `144,694`
  expands it, and the list becomes Accessories 302, Productivity 358,
  Graphics 414, Settings 470, Appearance 526, System Info 582, Web Server
  638, Program Runner 694.
- **The recorded "Settings and System Info are both at `144,711`, so the
  Monitor is two clicks at ONE point" is STALE and is wrong at head.** Two
  clicks at 711 open the PROGRAM RUNNER, because 711 is Program Runner's row
  once Settings is expanded. That was measured rather than reasoned: the
  capture shows the launcher pane, not the Monitor.
- **KEEP EVERY MOVE SAMPLE SMALL, and this is the trap that cost the most.**
  A move of 112 device pixels written as ONE sample is inside the +-127 clamp
  and is still not reliably delivered: on one boot it arrived and on the next
  it did not, leaving the pointer two rows down, so a click aimed at System
  Info opened the Program Runner. Split a move into samples of about 28 and
  leave a dwell before the click. Four small samples reach the Monitor every
  time where one large one did not, and **the failure looks exactly like a
  desk defect**: a real pane opens, no error is printed, and the run exits 0.
- **A FRAME FROM ANOTHER BOOT IS NOT A CONTROL.** The evidence that made the
  above look like a hit-test fault was a capture at 7450 ms showing the
  pointer ON the correct row, taken from a SEPARATE run of the same timeline.
  Mouse delivery differs between boots, so that frame said nothing about the
  boot that failed. If a capture is to explain a click, it has to come from
  the SAME boot as the click, which means capturing once and reading the
  whole frame rather than running twice at two delays.
- **The hit path itself is not the suspect and has a runner**:
  `codex/test/apps/desk-menu-click` clicks every visible row at the centre of
  its own laid rectangle and asserts the answer against that entry's
  `ge-scan`, faceless and with a synthetic face, and all of them agree.
- **The Monitor is Settings then System Info**; it is not in `gpr-entries`
  under its own name.
- **A heavy `-open` evicts the Browser and a light pane does not**, so an arm
  needing a second live pane beside a docked Browser uses a light one.
- **Window positions CASCADE.** The first window's title bar sits at y=160
  and the second at y=208, so a close or minimise button is not at a fixed
  point across arms. Take a frame and read the geometry rather than carrying
  coordinates between arms.
- **A restored window's close button is at its ORIGINAL cascade slot**, not
  the raising one. Take a frame between the restore and the close.
- **The pill row is not in open order.** Opening Files, then Edit, then the
  Monitor lays the pills out Monitor, Files, Edit. Read the row from a frame
  before clicking a pill.
- **Dock with the MINIMIZE button, not the flick.** The gesture is a speed
  against an HPET deadline and so is unverifiable by scripted capture by
  construction; minimize reaches the same docked-pill state and is a fixed
  point on the title bar.
- **`-rtc` MAKES EVERY SECOND CLICK ON ONE PILL A DOUBLE-CLICK, and a
  double-click minimises.** `dk-dclick` compares an elapsed that is
  identically zero while the clock is stopped, so the second click on the
  same pill reaches `desk-pill-minimise` instead of `desk-pill-restore`. An
  arm that clicks one pill twice under `-rtc` is measuring the double-click
  path whatever it believes it is measuring, and it will report a no-op for
  anything the restore path does, including a crash. Drop `-rtc` for any arm
  that clicks a pill more than once, or alternate pills, and accept that the
  clock and the it/s counter then differ between frames.
- **A hover dwell needs a LIVE clock** for the same reason.
- **Read the frontier off the Monitor's `memory` row** in the captured frame.
  It prints the heap frontier and the desk mark together.
- **Re-measure the baseline rather than reusing a recorded absolute**
  (L-COUNT).

## Constraints on the surface that this campaign does not get to change

**The Browser may only ever be the topmost heavy pane.** It rebuilds its
state at the current heap frontier on every event, so anything opened over
it, or any pane that takes a frame mark above it, breaks it (`BROWSER-5`,
`apps/browser/browser-backlog.md`). Stage 3's layered surfaces and any later
window or z-order work inherit this: **a general window manager is not
available while that holds**, and a design that assumes arbitrary stacking
will be wrong at the Browser. Told to this campaign by red, 2026-08-20, as a
property of the surface.

**The taskbar band repaints every second** and is contested by any pane that
renders `desk-chrome-with` (`WORKS-37`). Every stage that paints near it has
to leave `dk-chrome-paint`'s clock repaint intact. Anything drawn into the
band from OUTSIDE the tree is erased within a second of appearing, which is
why a pill is a button in the taskbar tree rather than a painting.

**`dk-task-h` is the band's FLOOR rather than its height**, and anything
placing near the band asks `dk-task-px`. The measurement is above, under the
type metrics.

## What this collides with

**SETTLED 2026-08-20: the desk is val's for this campaign.** red released
`GopDesk.codex` the day the campaign opened; `GopComposite` was never
claimed. The claims table carries both.

**`GopFacts.codex` remains red's** and is not part of this. Stage 5's
settings persistence goes through the fact store's published interface rather
than by editing that chapter.

**The announce-before-you-start rule on the desk is not suspended by the
claim**, and neither is checking which `ds` cells are already spoken for
before taking one. `works-desk-contract.md` carries the block layout and the
rule that **a `ds` shorter than 128 bytes cannot hold the cells above 63**:
the arena is bump-allocated and 64-aligned, so an over-read lands on padding,
answers zero, and is indistinguishable from a legitimately empty cell.

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

It does not claim the unused chapters work IN THE DESK. It claims they exist,
are not stubs, and most carry a test of their own, which is a different and
weaker statement than working under the shell's constraints. Each stage arms
the ones it wires against those constraints.

It does not claim feature parity with Windows, macOS or Android is reached by
finishing stage 8. It claims these nine stages are the skeleton such parity
would hang on, which is what was asked for.
