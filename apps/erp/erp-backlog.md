# ERP -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

Design: `apps/erp/design/Active/`.

| # | Capability | State of the gap |
|---|---|---|
| ERP-1 | **The dashboard page shows no RAG status and no aging buckets** | `ErpPage.codex` (`web/erp.html`) renders a snapshot of a native `run-month`, written by `build-snapshot.ps1` and naming the compiler and depot change that produced it: seven KPI tiles, open payables and receivables, and the trial balance with its totals. `codex/test/apps/erp-snapshot-fresh` fails when the snapshot and the scenario disagree. Missing from the design's Phase 5: a RAG status per tile (`BwAnalytics` `eval-kpi-status`) and the AP/AR aging buckets (`calc-ap-aging`, `calc-ar-aging`). The HTML runtime has `alloc-bytes` and a Data quire page runs in the browser (`codex/plugs/html/arms/data-page.mjs`); the page running `run-month` itself is untried. |
