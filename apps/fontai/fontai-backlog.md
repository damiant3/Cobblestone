# FontAI -- open capabilities

App-domain backlog. There is no platform-wide register:
`docs/PM/BACKLOG.md` must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|

## Test coverage

1 of 1 library chapters are reached by no computing test. `pwsh build/coverage-census.ps1 -Tree apps/fontai -List` names them (2026-10-26). Open: a computing test for each, or a deletion.
