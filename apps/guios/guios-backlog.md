# GuiOS -- open capabilities

**This app is retiring into `apps/works/GopDesk.codex`** (ruled
2026-08-03, `docs/Designs/Active/OS/GuiOsBringup.md`). The four menu-item
chapters are deleted; `CalcApp`, `CalendarApp`, `DiffusionApp`, `TrackerApp`
and `TrackerDb` carry real logic and are being merged into the GopDesk stack
and wired to panes. Do not start new work here.

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
| GUIOS-1 | **Chapters in this app stop sharing definition names** | Measured 2026-07-21 by `build/sweep-app-classes.ps1 -Jobs 6` (2.6 min, 266 entry units; parse `test-output/clssweep/*.log` for `warning CDX3006`). **Private copies of a platform name** (cite it instead of defining it): `fat32-cluster-sector` (FontLoad vs Foreword--Fat32, 2u). CDX3006 is a warning, not an error: each chapter sees its own definition, so this compiles and runs. What it costs is that a mention in a chapter defining NEITHER resolves by the order the build globs files, and moving a body between chapters silently changes what it means. **Compare the bodies before merging a pair -- same name does not mean same function**, and a pair whose arities differ cannot be merged at all.  |
