# Diagram -- open capabilities

App-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register.
Anything that is this application's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## Open

- **The layer TreeView is not wired to anything.** `diagram-layer-tree`
  (`Toolbar.codex`, Section: Layer TreeView) has no caller, so the layer tree
  is never shown; what is missing is the call from the editor.

## Test coverage

7 of 9 library chapters are reached by no computing test. `pwsh build/coverage-census.ps1 -Tree apps/diagram -List` names them (2026-10-26). Open: a computing test for each, or a deletion.
