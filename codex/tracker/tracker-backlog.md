# Tracker -- open capabilities

App-domain backlog. `docs/PM/BACKLOG.md` is the register for the
platform -- compiler, forewords, OS, plugs, backends. Anything that is
this quire's own behaviour lives here instead, so the platform register
stays readable. Tracker is an application that happens to live under
`codex/` rather than `apps/`; the backlog sits beside the code either
way.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|
| TRK-1 | **`codex/tracker` compiles** | Two reasons, both its own rather than a missing dependency. **`end` used as a parameter name** -- it is a reserved keyword -- in `IssueTypes.codex` `cycle-new` and `SprintEngine.codex` `sprint-create`. And a `TableDef` built with an entire `tbl-*` field vocabulary that the only reachable record, `Data chapter Schema`'s `TableDef`, does not have: **a whole-record mismatch, not one missing field**, so it wants a decision about which schema model tracker targets, not a rename. (A third reason was once recorded as "an application broken across a newline"; it could not be located by inspection, so re-derive it from a compile sweep before chasing it.) |
