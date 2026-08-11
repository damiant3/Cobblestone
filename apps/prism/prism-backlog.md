# Prism -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

Design: `apps/prism/design/Active/`.

| # | Capability | State of the gap |
|---|---|---|
| PRISM-1 | **Phase 3: plug fan-out is in-process** | `run.ps1` pre-bakes IR on the host, which is not the in-process compilation the design claims. |
