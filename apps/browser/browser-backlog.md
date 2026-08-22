# Browser -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

| # | Capability | State of the gap |
|---|---|---|
| BROWSER-4 | **A named host is not reachable, and `resolve-and-load-remote` is uncalled** | Made explicit 2026-08-11 (blu) on Damian's instruction, and it is a narrowing of the DEFAULT path rather than a new limitation: remote loading has never been demonstrated to work on any runtime here, and `load-page-source` already answered `SourceAddress` with "Network loading requires Phase 2". `resolve-and-load` now handles `LocalAddress` and `HashAddress` and answers a `NamedAddress` with a PageFetchError naming the host. That is what lets the entire browser carry NO Network effect, which is what lets `GopDesk` embed it without the desktop's type claiming network reach (WORKS-10). **The wire path is kept by name, not deleted:** `resolve-and-load-remote` and `load-by-address-remote` route a `NamedAddress` to `load-remote-page` -> `fetch-page-tcp` exactly as before. **Nothing calls them**, so they are pruned from every binary and are UNCALLED CODE in the L-UNCALLED sense -- they compile and nothing executes them, so they can rot silently. Whoever lands Phase 2 should call them from a build that wants the wire and should not assume they still work. `codex/test/apps/browser-offline-load` pins both arms, and its real guard is that the chapter declares `[Console]` alone: rewiring the named arm makes `resolve-and-load` effectful again and the test STOPS COMPILING (CDX2031) rather than passing quietly. |
