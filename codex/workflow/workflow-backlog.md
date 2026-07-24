# Workflow -- open capabilities

App-domain backlog. `docs/PM/BACKLOG.md` is the register for the
platform -- compiler, forewords, OS, plugs, backends. Anything that is
this quire's own behaviour lives here instead, so the platform register
stays readable. This is `codex/workflow` (`ProcessEngine`,
`ProcessTemplates`, `ProcessTypes`) and is a different quire from
`apps/workflow`, which compiles.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|
| WF-1 | **`codex/workflow` compiles** | It does not, for its own reasons rather than a missing dependency: a `TableDef` built with an entire `tbl-*` field vocabulary that the only reachable record, `Data chapter Schema`'s `TableDef`, does not have. **A whole-record mismatch, not one missing field**, so it wants a decision about which schema model this quire targets, not a rename. Shares the shape with `codex/tracker` (`codex/tracker/tracker-backlog.md`); settle both at once. |
