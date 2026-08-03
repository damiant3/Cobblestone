# ERP Buildout: from schema to application

Status: **Phases 0 through 4 are shipped.** The quire builds, the
scenario runs, the API answers, and 11 tests guard it. Phase 5 (the
browser dashboard) is the remaining work. Written 2026-06-10 by reek;
the plan below is retained because Phases 5 and 6 still execute against
it -- one CL at a time, after a fresh session init.

## Where it stands

It is an app now. What shipped:

- **The quire builds.** Registered in `build/quire-map.ps1` and the plug
  build lib's overrides.
- **11 tests in the battery**, all under `codex/test/apps/`: `erp-gl-test`,
  `erp-posting-test`, `erp-payroll-test`, `erp-amortization-test`,
  `erp-bom-test`, `erp-spc-test`, `erp-ev-test`, `erp-pricing-test`,
  `erp-scenario-test`, `erp-db-test`, `erp-server-test` -- each with an
  `.expected` sidecar.
- **`ErpConfig.codex`** -- the AccountMap that killed the hardcoded GL
  accounts (Phase 1).
- **`ErpScenario.codex`** -- the integrated month-in-the-life with the
  balancing invariant (Phase 2).
- **`ErpServer.codex`** -- the JSON API on the MarketWeb route pattern
  (Phase 4).
- Persistence through Codex DB (Phase 3), guarded by `erp-db-test`.

**What remains: Phase 5.** There is no `ErpPage.codex` and no
`apps/erp/web/erp.html`. The executive dashboard -- KPI tiles, trial
balance, AP/AR aging, cash position -- is the last piece between this and
the "omg awesome" description at the foot of this document. Everything
it needs (the scenario data, the KPI engine, the WebApp quire, the page
gates) is already in place.

### For the historical record -- what it was

The comment "it's just a schema, no app there" was half right. The 21
chapters held 194 records + 108 variants and ~154 functions, of which
~65 were real engines, but: the quire could not be built (no resolver
entry, `ERP`/`Erp` case mismatch), there were zero tests, there was no
entry point / API / UI, `GlState.gl-db` was constructed and never read,
and no test ran a subledger flow end-to-end into the GL. Phases 0-4 each
closed one of those.

### Cross-cutting flaws to fix while building (verified by read)

- **Hardcoded GL accounts** in every posting module (AP: 1300/2000/2100,
  AR: 1100/4000/2200, payroll: 6000/6100/2200/2300, inventory:
  1200/5000, allocations: 6900). Needs an account-mapping config.
- **Inconsistent numeric scaling**: bps (x10000), factors x1000,
  indices x100, all undocumented; PsProject compares an x100-scaled SPI
  against unscaled 95/80 thresholds (live bug). One convention, stated
  in prose, everywhere.
- **Known stubs returning wrong answers**: SumOfYears depreciation
  reuses StraightLine; UnitsOfProduction returns 0; treasury
  `reconcile` hardcodes unmatched-book = 0; IsInsurance StopLoss /
  CatastropheXL cede 0; IsRealEstate escalation is simple not compound;
  SdSales COGS hardcodes 60% of unit price instead of material cost.
- **`check-period-open` returns True when the period is unknown** --
  postings into never-opened periods succeed. Flip to closed-by-
  default; scenario code must open periods explicitly.
- **Tax hardcoded at 8%** in AP and Utilities; payroll state tax at 5%,
  health at 25000, retirement at 6% -- config, not constants.
- **Hot-path scans**: 3-way match is O(n^3) (find-gr-qty /
  find-po-line-price re-scan per line); treasury forecast is
  O(periods x flows); warehouse bin search is linear. Index with Hamt
  where a test shows it matters; otherwise note and move on.
- **Every chapter re-implements the accumulator loop** (~30 copies of
  sum-*-loop). A small ErpLib fold/sum-by helper kills the bloat --
  do it opportunistically while touching each chapter, not as a big
  bang.

### Infrastructure that is proven TODAY (use it, don't rebuild)

- **HTTP**: `apps/works/Http.codex` (http-parse-request, http-encode) +
  `codex/os/net/WebServer.codex` (web-dispatch, web-standard) -- pass
  http-test / web-server-test in the battery. The route-handler shape
  to copy is `apps/market/MarketWeb.codex`:
  `market-route : MarketState, HttpRequest -> (MarketState, HttpResponse)`.
- **JSON**: `codex/foreword/encode/Json.codex` -- complete emit + parse,
  tested. API transport is solved.
- **Codex DB**: `apps/data` -- in-memory DbServer with schema, rows,
  filters, group-by; db-test / db-full-test pass. No disk persistence
  yet (WAL exists, recovery not wired) -- design write-through now, get
  durability free when Data grows it.
- **Browser UI**: the WebApp quire (apps/webapp -- WebRuntime, WebTheme,
  WebWidgets) + HTML plug; build-apps.ps1 auto-discovers
  `apps/<dir>/web/<name>.html` paired with `<Name>Page.codex`;
  check-apps.ps1 asserts page invariants. See
  apps/webapp/design/Done/BaseTemplate.md.
- **Browser-to-server**: explorer's pattern (TCP bridge serving
  /api/<table> JSON) works; chat shows fetch stubs. Live wiring is a
  late phase -- everything before it is testable without sockets by
  feeding HttpRequest records to the route function.
- **Test harness**: codex/test/apps/*.codex with .expected sidecars;
  `build/test.ps1 -Apps`. Bare-metal printing must use `print-line-uni`
  (plain print-line emits raw CCE bytes).

## The plan

Sequencing rule: one CL, one concern, battery green, submit. Every
phase ends with a named gate. Estimated sizes are CL counts, not days.

**Phases 0-4 are DONE.** They are kept below as the record of what was
built and why. Phase 5 is the live work; Phase 6 is the backlog.

### Phase 0 -- Make it buildable and prove one engine -- DONE

1. Register the quire: `'ERP' = 'apps\erp'` in build/quire-map.ps1;
   add `'apps\erp' = 'ERP'` to plug-build-lib's $QuireOverrides (its
   auto-derivation would say "Erp" and the cites say "ERP").
2. First test ever: `codex/test/apps/erp-gl-test.codex` (+ .expected)
   -- gl-init, load-standard-coa, open a period, post a balanced JE,
   verify rejection of an unbalanced JE and of a closed-period JE,
   trial balance debits == credits, net-income arithmetic.
   GATE: battery green with the new test passing.

### Phase 1 -- Foundation hardening -- DONE (`ErpConfig.codex`)

1. **ErpConfig chapter**: `AccountMap` record (ap-control, ar-control,
   inventory, gr-ir, cogs, salaries, benefits, tax-payable, wages,
   cash, input-tax, allocation, revenue, retained-earnings...) with a
   `standard-account-map` default; `TaxCode` list; thread AccountMap
   through FinApAr, HrCore, SdSales, MmProcurement, FinControlling
   posting builders (signature change: each takes the map). One CL per
   module if diffs get large.
2. **Scaling convention**: prose section in ErpTypes declaring: money
   in cents, rates in bps (10000 = 100%), ratios x100 where SAP-like
   (CPI/SPI); fix PsProject health thresholds to the declared scale;
   fix convert-currency rounding (round-half-up via +5000 before
   /10000).
3. **Stub fixes**, one CL: SumOfYears + UnitsOfProduction depreciation,
   treasury unmatched-book, compound rent escalation, StopLoss/CatXL
   ceding, SdSales COGS from material standard cost (MmProcurement
   already has the material master), closed-by-default period check.
4. **Engine KATs**: one test file per engine with hand-computed
   .expected -- erp-payroll-test (bracket math vs hand calc),
   erp-amortization-test (known loan table), erp-bom-test (explosion
   with scrap), erp-spc-test (sigma limits), erp-ev-test (earned
   value), erp-pricing-test (condition sequence). These are the "test
   the engine once" guarantee for everything later.
   GATE: -Apps battery green including all new KATs.

### Phase 2 -- The integrated scenario: this is what makes it an app -- DONE (`ErpScenario.codex`)

New chapter `ErpScenario` + `opening`: one month in the life of a demo
company. Create company, COA, open period; onboard vendors, customers,
materials, employees; run procure-to-pay (PR -> PO -> GR with GR/IR ->
vendor invoice -> 3-way match -> payment run); make-to-stock (BOM ->
production order -> confirmation -> goods movement); order-to-cash
(quote -> SO -> credit check -> delivery with COGS -> billing -> cash
receipt); payroll run posting to GL; cost-center allocation; month-end
close (depreciation JE, accruals, close the period); print trial
balance, P&L, balance sheet over serial via print-line-uni.

The golden .expected asserts the one invariant that proves the whole
suite coheres: **assets == liabilities + equity, and every subledger
total ties to its GL control account.** Expect this phase to flush out
real integration bugs (that is its job). Add `erp-scenario-test` to
the battery (likely a .slow sidecar if it exceeds a few seconds).
GATE: scenario runs on codex-vm, statements balance, battery green.

### Phase 3 -- Persistence through Codex DB -- DONE (`erp-db-test`)

Wire the dead `gl-db`: table-defs for accounts, journals, journal
lines, balances via Data Schema; post-journal writes through; gl-init
gains a rebuild-from-db path. ERP becomes Codex DB's first real
customer -- exactly the dogfooding the Data quire needs. In-memory
today is fine; durability arrives when Data's WAL recovery lands.
Drop gl-log dead weight or persist log lines as an audit table (the
audit table is the better story -- GRC reads it later).
GATE: scenario unchanged output with persistence on; a kill-and-rebuild
test reloads state from the db tables and reproduces the trial balance.

### Phase 4 -- API surface -- DONE (`ErpServer.codex`, `erp-server-test`)

New chapter `ErpServer`: `erp-route : ErpState, HttpRequest ->
(ErpState, HttpResponse)` on the MarketWeb pattern, JSON bodies via
the foreword codec. Routes: GET /api/gl/accounts, /api/gl/trial-balance,
/api/gl/journal (POST = post a JE), /api/ap/invoices + aging,
/api/ar/aging, /api/kpi/dashboard, /api/hr/paystub/<id>,
plus web-standard health/status. Tested exactly like web-server-test:
construct HttpRequest records, assert status/body -- no sockets needed.
GATE: erp-server-test green in battery.

### Phase 5 -- Browser dashboard (2 CLs) -- THE REMAINING WORK

`ErpPage.codex` on the WebApp quire (cites WebApp chapter WebRuntime /
WebTheme / WebWidgets -- see the BaseTemplate port recipe), output
apps/erp/web/erp.html so build-apps discovery and check-apps pick it
up automatically. Executive dashboard: KPI tiles with RAG status (the
BwAnalytics eval-kpi-status engine), trial balance table, AP/AR aging
buckets, cash position. First cut renders the Phase-2 scenario data
embedded (pure widget tree -- fully verifiable by the existing page
gates); live fetch against Phase 4 comes later with the explorer-style
bridge and is OPTIONAL for "awesome".
GATE: erp.html builds in build-apps, check-apps 21+1 green, page renders
the scenario numbers.

### Phase 6 -- Module deepening backlog (pick by appetite, 1 CL each)

Priority order by payoff:
1. FIFO inventory valuation done properly (layers, not standard-cost
   fallback) + KAT.
2. MRP multi-level explosion (recurse subassembly BOMs, lead-time
   offset on planned orders).
3. GRC SoD evaluation engine -- evaluate standard-sod-rules against
   HrCore role assignments, emit violations (turns GRC from data into
   behavior; reads the Phase-3 audit table).
4. MDM rule evaluation + duplicate detection (Foreword EditDistance
   exists -- fuzzy match is nearly free).
5. BW fact-table population from GlState/SdSales (turns the dashboard
   from snapshot to drill-down).
6. WmWarehouse picking strategies (FIFO/FEFO at minimum) + wave
   completion flow.
7. Treasury reconciliation done honestly (match by amount+date window,
   report both unmatched sides).
8. 3-way match O(n^3) -> Hamt-indexed O(n).
9. IS-* depth (healthcare DRG grouping, insurance IBNR, utilities TOU
   billing) -- only after the core sings.

## Conventions for the executing session

- Read docs/Agents/PerforceProcess.md before any p4 beyond edit/submit.
  Shelve -> sync -f -> clean -> unshelve -> hash-check before submits.
  p4 clean deletes codex/plugs/html/build-output/html-plug.cdx -- rebuild
  before regenerating pages. Never run VM jobs concurrently with the
  battery.
- Money/time-complexity verdict in every CL (rule 8). The scenario
  test is the heap canary: watch `heap hwm` if a chapter grows lists
  in a loop.
- The -Apps battery has 15 PRE-EXISTING failures unrelated to ERP
  (crypto-vector and disk-facts tests; see memory + KNOWN-CONDITIONS).
  Do not chase them; do not let them mask new ERP reds -- compare
  failure sets, not exit codes.
- ErpTypes is cited by everything: a type change there recompiles the
  whole quire -- batch type changes deliberately.

## What "omg awesome" looks like when done

Boot a CDX on codex-vm, watch a company run a month and print balanced
books; open erp.html and see the executive dashboard of that same
month; curl the API for the trial balance; every engine pinned by a
hand-computed KAT in the battery; the GL living in Codex DB tables.
An ERP that is at once a demo, a test of the whole substrate (DB +
HTTP + JSON + UI + compiler), and the most complete business app in
the repository.
