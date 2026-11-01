# Lens -- open capabilities

App-domain register. Cross-lane priorities belong in `docs/PM/CurrentPlan.md`.

| Source | Gap |
|---|---|
| `LensService.codex:170` | `svc-cell` replaces quote/backslash only. LF and NUL remain raw in cell, row and diagnostic JSON arrays. Use the shared CCE string encoder and prove decoded cell/diagnostic preservation. Source-inspected 2026-10-01 at main 33668. |

## Test coverage

1 of 6 library chapters are reached by no computing test. `pwsh build/coverage-census.ps1 -Tree apps/lens -List` names them (2026-10-26). Open: a computing test for each, or a deletion.
