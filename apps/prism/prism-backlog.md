# Prism -- open capabilities

App-domain backlog. `docs/PM/BACKLOG.md` is the register for the
platform -- compiler, forewords, OS, plugs, backends. Anything that is
this application's own behaviour lives here instead, so the platform
register stays readable.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

Design: `apps/prism/design/Active/`.

| # | Capability | State of the gap |
|---|---|---|
| PRISM-1 | **Phase 3: plug fan-out is in-process** | `run.ps1` pre-bakes IR on the host, which is not the in-process compilation the design claims. |
