# Collab -- open capabilities

App-domain backlog. The shape and priority order for the platform live in
`docs/PM/CurrentPlan.md`; there is no platform-wide register any more.
Anything that is this application's own behaviour lives here.

The rules are the standing ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a gap that
is still real is never quietly dropped.

## Open

- **The meeting agenda TreeView is not wired to anything.** `agenda-tree`
  (`CallPage.codex`, Section: Meeting Agenda TreeView) has no caller, so no
  view ever shows a meeting's agenda. It type-checked wrongly until
  2026-08-17, annotated `MeetingRecord` where the record is `MeetingRoom`
  (`CollabTypes.codex:147`); the same file already used `List MeetingRoom`
  correctly at 117 and 123. The annotation is fixed; what is missing is the
  call from a view. Found by CDX3008.