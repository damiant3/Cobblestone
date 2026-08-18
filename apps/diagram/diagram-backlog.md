# Diagram -- open capabilities

App-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register any more.
Anything that is this application's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## Open

- **The layer TreeView is not wired to anything.** `diagram-layer-tree`
  (`Toolbar.codex`, Section: Layer TreeView) has no caller, so the layer tree
  is never shown. It type-checked wrongly until 2026-08-17 and carried TWO
  defects, the second hidden by the first: it was annotated `DiagramModel`,
  which is a CHAPTER name rather than a type, and it read `dm-nodes` and
  `dm-edges` where the `Diagram` record (`DiagramModel.codex:129`) has
  `dg-nodes` and `dg-edges`. Field access on the opaque type the compiler
  fabricated for the unknown name checked against nothing, so the wrong field
  prefix was invisible for as long as the type name was wrong. Both are
  fixed; what is missing is the call from the editor. Found by CDX3008.

- **`Toolbar.codex:13-14` cites `Diagram chapter DiagramTheme` twice.** A
  duplicate cite is CDX3003 territory and the later one silently shadows the
  earlier. Unrelated to the fix above and left for this app's owner.