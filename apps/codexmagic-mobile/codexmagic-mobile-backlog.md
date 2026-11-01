# CodexMagic mobile -- open capabilities

App-domain backlog. There is no platform-wide register:
`docs/PM/BACKLOG.md` must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

Design: `apps/codexmagic-mobile/Design.md`.

| # | Capability | State of the gap |
|---|---|---|
| MOB-1 | **Phase 2 economy pages** | Not built: no pack list, no mana coin purchase, no card browser. The files that once wore the planned names are now `CreationsPage.codex` / `GalleryPage.codex`; grep for those. Only `MobileApp.codex` defines `opening` today. |
| MOB-2 | **Test coverage: 6 of 6 library chapters are reached by no computing test** | `pwsh build/coverage-census.ps1 -Tree apps/codexmagic-mobile -List` names them (2026-10-26). Open: a computing test for each, or a deletion. |
