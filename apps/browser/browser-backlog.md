# Browser -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

**BROWSER-3 was deleted 2026-08-11 because it was never real.** It claimed
no test exercised a repaint after the first, on the evidence that four
injected arrow keys changed nothing while codex-vm logged
`HID: first key event sc=50` for an injected 80. **That trace prints the
scancode in HEX**: `0x50` IS 80, so the key was delivered correctly and the
entry's own suspect was innocent. The page simply has nothing to scroll --
the other branch of the disjunction the entry left open, which one look at
the log's base would have closed. Measured instead with Ctrl+T
(`-keys 29,20,157`, and identically through a `-keys-file` timeline): the
tab bar gains a second tab and the repaint is correct, so keyboard input
reaches the browser AND a frame rendered after a `__heap-restore` is sound.
Recorded here rather than dropped silently, because the lesson is about
reading an instrument, not about the browser.

| # | Capability | State of the gap |
|---|---|---|
| BROWSER-4 | **A named host is not reachable, and `resolve-and-load-remote` is uncalled** | Made explicit 2026-08-11 (blu) on Damian's instruction, and it is a narrowing of the DEFAULT path rather than a new limitation: remote loading has never been demonstrated to work on any runtime here, and `load-page-source` already answered `SourceAddress` with "Network loading requires Phase 2". `resolve-and-load` now handles `LocalAddress` and `HashAddress` and answers a `NamedAddress` with a PageFetchError naming the host. That is what lets the entire browser carry NO Network effect, which is what lets `GopDesk` embed it without the desktop's type claiming network reach (WORKS-10). **The wire path is kept by name, not deleted:** `resolve-and-load-remote` and `load-by-address-remote` route a `NamedAddress` to `load-remote-page` -> `fetch-page-tcp` exactly as before. **Nothing calls them**, so they are pruned from every binary and are UNCALLED CODE in the L-UNCALLED sense -- they compile and nothing executes them, so they can rot silently. Whoever lands Phase 2 should call them from a build that wants the wire and should not assume they still work. `codex/test/apps/browser-offline-load` pins both arms, and its real guard is that the chapter declares `[Console]` alone: rewiring the named arm makes `resolve-and-load` effectful again and the test STOPS COMPILING (CDX2031) rather than passing quietly. |
| BROWSER-2 | **A fixed-height row cannot be expressed with `widget-set-min` alone** | The browser itself is CLOSED (blu, 2026-08-10 and 2026-08-11): display, glyphs, out-of-memory, serial and chrome layout. It boots, paints a real UI, prints `Codex Browser v0.1`, and runs indefinitely. Kept because the layout cause is a PLATFORM trap that will catch the next caller. **`widget-panel` defaults `wn-flex` to 1** (`UI--Widget`), where `widget-label`, `widget-button` and `widget-separator` default it to 0. The chrome asked for a 32-pixel tab bar and a 28-pixel address bar with `widget-set-min`, which sets a MINIMUM and preserves flex, so both bars stayed flexible, 32 and 28 never bound, and the three rows split the window evenly at 247 each. Every button then stretched to its row height, because `flex-row-place` sizes a child's cross axis to `box-max(min, container)` -- that is why they drew as full-height bars, and it is a consequence rather than a second defect. The fix is `widget-set-flex ... 0` on each fixed row. **The tempting fix is to default `widget-panel` to flex 0, and it should not be done casually** -- every panel in `apps/` and `codex/foreword/ui/` currently relies on the flex-1 default, so that is a tree-wide change and Damian's call, not a browser change. `codex/test/apps/browser-chrome-layout` pins the three rows by id and rect through `format-widget`, so it needs no display; falsified at 247/247/247 with the fix reverted. **Serial, for the record:** `print-line` IS a registered builtin (`Types/Builtins.codex`), not an undefined name -- it lowers through `emit-print-line-raw-builtin` and writes raw CCE with no boundary conversion. Ten call sites now use `print-line-uni`. The trap came from `DevelopersGuide.md`, whose examples all taught `print-line`; that is fixed, and the 192 remaining sites tree-wide are filed in `docs/PM/CurrentPlan.md`. |
| BROWSER-1 | **Chapters in this app stop sharing definition names** | Measured 2026-07-21 by `build/sweep-app-classes.ps1 -Jobs 6` (2.6 min, 266 entry units; parse `test-output/clssweep/*.log` for `warning CDX3006`). **Private copies of a platform name** (cite it instead of defining it): `mod-alt` (BrowserEvent vs UI--Event, 1u); `mod-ctrl` (BrowserEvent vs UI--Event, 1u); `mod-shift` (BrowserEvent vs UI--Event, 1u); `text-index-of` (ContentAddress vs Foreword--StringUtils, 1u); `text-index-of` (DataChannel vs Foreword--StringUtils, 1u); `text-trim` (PageCompiler vs Foreword--TextSearch, 1u). **Duplicated between this app's own chapters** (prefix one, or extract a shared chapter): `clamp-vol` (AudioOutput vs Browser--MediaPlayer, 1u); `clamp-vol` (MediaPlayer vs Browser--AudioOutput, 1u); `text-index-of-at` (ContentAddress vs Browser--DataChannel, 1u); `text-index-of-at` (DataChannel vs Browser--ContentAddress, 1u). CDX3006 is a warning, not an error: each chapter sees its own definition, so this compiles and runs. What it costs is that a mention in a chapter defining NEITHER resolves by the order the build globs files, and moving a body between chapters silently changes what it means. **Compare the bodies before merging a pair -- same name does not mean same function**, and a pair whose arities differ cannot be merged at all.  |
