# FontExplorer -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|
| FONTEXP-1 | **The app is written against a UI and list API that does not exist** | `opening` is RED, 14 CDX3002 errors, measured 2026-08-08 by `build/sweep-app-classes.ps1 -Jobs 8`. Six names are undefined and **none of them is defined anywhere in the tree** (checked outside `build-output`), so this is not a missing cite: the chapters are cited and simply do not publish these names. From `apps/fontexplorer/opening.codex`, which cites `UI chapter Layout` and `UI chapter Widget`: `layout-vertical`, `layout-horizontal`, `layout-fixed-width`, `layout-spacer`, `widget-text-field`. From `apps/fontexplorer/FontModel.codex:53,72`, which cites `Foreword chapter List`: `list-concat`. The app was written against an intended API rather than the one that shipped. Closing it means either building those five layout/widget primitives in the `UI` quire and `list-concat` in the foreword (both are platform additions, and `list-concat` in the foreword is seed-affecting), or rewriting the two chapters against what `UI--Layout`, `UI--Widget` and `Foreword--List` actually publish. Read what those three chapters export before choosing; the app's own model and preview chapters (`FontModel`, `FontPreview`, `FontAiTrainer`) resolve their other cites fine, so the gap is confined to layout, one widget, and one list helper. |
