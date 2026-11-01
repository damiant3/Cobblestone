# Globe -- open capabilities

App-domain backlog. `docs/PM/CurrentPlan.md` carries the shape and the
priority order for the platform. Anything that is this application's own
behaviour lives here. An entry says what is still missing and nothing else;
a closed entry is DELETED rather than annotated.

| # | Capability | State of the gap |
|---|---|---|
| GLOBE-1 | **`tests/TestGlobe` does not compile: two `GeoPoint` types** | `GlobeTypes.codex:30` defines `GeoPoint` and `Math` `Geodesic`, cited by `MapNetwork` and `MapSearch`, defines it too (CDX3001, seed 9E2000181819DC49, 2026-10-26). `GlobeDemo`, the app's entry, cites none of `GlobeScene`, `MapMode` or `MapNetwork`, so TestGlobe is their only compiler. Open: one `GeoPoint` (cite `Geodesic`'s or rename the app's), then run TestGlobe. |

## Test coverage

11 of 13 library chapters are reached by no computing test. `pwsh build/coverage-census.ps1 -Tree apps/globe -List` names them (2026-10-26). Open: a computing test for each, or a deletion.
