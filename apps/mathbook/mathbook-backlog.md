# Mathbook -- open capabilities

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

## TestParser: matrix literals and `and`/`or`, 5 of 56 arms

`apps/mathbook/tests/TestParser` (run by no battery until it passes) fails 5 arms at
seed 9E2000181819DC49 (2026-10-26): `matrix(2, 2, 1, 2, 3, 4)` and the 1x3 and empty
matrices answer `expected ',' or ')' in matrix` (`Parser.codex` `ParseErr` in the matrix
argument loop), and `x > 0 and x < 10` and `x < 0 or x > 10` keep only the left
relation; `ps-parse-logic-rest` reads correctly, so the right side is lost before it.
Open: fix both, then move the suite to `codex/test/apps/mathbook-parser-suite`.

## Test coverage

5 of 12 library chapters are reached by no computing test. `pwsh build/coverage-census.ps1 -Tree apps/mathbook -List` names them (2026-10-26). Open: a computing test for each, or a deletion.
