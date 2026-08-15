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
| DATA-BL1 | **`tab-import-config` and `pipe-import-config` are exported but cannot match anything** | `apps/data/BulkLoader.codex` compares `to-unicode ch == delim`, and its own prose already states the reason: a tab and a vertical bar have no CCE code point, so neither can appear in Codex Text at all. Both configs are still exported beside `default-import-config` as if they were usable. Either delete them or make TSV import work by some other route; the current state is two public constants that silently import every line as one field. Found 2026-08-14 while designing `apps/wademo`. |
| DATA-BL2 | **`BulkLoader` does not handle quoted fields** | No RFC-4180 quote handling anywhere in `split-delimited` / `parse-field-value`. A quoted Text field arrives with literal quote characters in the value, and a quoted numeric field reads as 0 because `text-to-int` stops at the first non-digit. Measured on a real extract whose seven context columns are all quoted. A quoted field containing the delimiter would also split wrongly, which is the case that corrupts rather than merely dirties. Found 2026-08-14. |
