# Data -- open capabilities

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
| DATA-1 | **`row-set` stops mutating the row it is given** | `apps/data/Row.codex` fills a fresh `RowData` with `list-set-at`, which writes IN PLACE, so the caller's row changes under them; pinned by `codex/test/db-row-update`. Every by-name setter inherits it. The choices are a copy on every set (O(n) per field write) or changing a builtin whose in-place semantics other code depends on. **A decision, not a repair** -- and the second option is a platform change, so it does not belong to this app alone. |
