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
| DATA-2 | **A primary key and a unique index accept a duplicate row.** Measured by reek on seed EF9466BE (2026-10-04): two inserts with the same key into a table with a primary key and a unique index both answer True, and the table holds 2 rows. UOAIX stage B (`apps/uoaix/Database.md`) depends on key uniqueness. Owner: fester, with the Codex DB backend. | open |
| DATA-3 | **No storage backend.** `Wal.codex`, `BufferPool.codex` and `Page.codex` have no disk, block or flush path; pages and log records live only in memory (`apps/uoaix/Database.md`). Owner: fester, UOAIX stage B. | open |
