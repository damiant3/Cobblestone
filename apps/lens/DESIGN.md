# Lens -- Governed Analytics for Codex

**A Google Looker replacement, by construction.**

- **Status**: Design -- research + architecture, no code yet. Rev 2
  (2026-07-09): dual-target split -- Target N (Codex native) / Target B
  (barbarian C# emit, MSSQL)
- **Author**: val
- **Date**: 2026-07-09
- **Location**: `apps/lens/` (this document); implementation will live here
- **Depends on**: `apps/data` (Codex DB), `codex.foreword.ui` (widgets + charts),
  HTML plug (`codex/plugs/html/`), trust lattice (`codex.os.trust`), FactStore,
  C# plug (`codex/plugs/csharp/`, Target B emission), `codex.os.net`
  (Target N service)

---

## 1. What This Is

Lens is a business-intelligence platform: a **semantic modeling layer** that
maps raw tables to governed business concepts (dimensions, measures, joins),
a **query service** that compiles model-level questions into grain-checked
plans, and a **dashboard layer** that renders the results.

One Codex codebase, two deliverables (§4.4):

- **Target N -- Codex native.** Bare metal, Codex DB as the only engine,
  GOP + HTML-plug dashboards, codex.os.net service. No barbarian
  databases -- a native shop's data lives in Codex DB and the trust story
  stays end-to-end.
- **Target B -- barbarian cloud, for immediate consumption.** The engine,
  web service, and dashboard are **emitted as C# from the Codex AST**
  (`codex/plugs/csharp/`), deployed to cloud services, with Microsoft SQL
  Server as the warehouse. Nothing on this target is hand-written C# --
  every line is generated from the same `.codex` source.

The one-sentence pitch, in the house style: *Looker's semantic model is a
proprietary YAML dialect that only Looker can check. A Lens model is Codex
source -- the type system checks it, units are types, join cardinality is a
type, access is an effect, metric certification is a trust-lattice verdict,
and the audit trail is a build artifact.*

---

## 2. Research: What Looker Actually Is

Looker (acquired by Google, 2019; now part of Google Cloud) is the
reference point for "governed BI." Its architecture, distilled from the
official documentation and field experience:

### 2.1 LookML -- the semantic layer

LookML is a declarative modeling language. The core objects:

| LookML object | Meaning |
|---|---|
| **view** | Maps to a database table (or derived table). Declares its fields. |
| **dimension** | An attribute column -- segments the data (customer city, order date). |
| **measure** | An aggregate -- summarizes the data (`sum`, `count`, `average`, percentile). |
| **explore** | A curated join graph rooted at one view. The unit users query. |
| **join** | Declares relationship + cardinality (`many_to_one`, etc.) between views. |
| **model** | Groups explores, binds them to a database connection. |
| **derived table** | A view defined by a SQL query instead of a physical table. |

Users never write SQL. They pick dimensions and measures in an Explore; Looker
generates the SQL, runs it against the customer's warehouse (BigQuery,
Snowflake, etc. -- Looker stores no data itself), and renders the result.
Connectivity is a first-class product surface: 50+ SQL dialects over JDBC,
in two tiers (fully supported vs. integration-only, the latter with no fix
commitments). "Bring the warehouse you already have" is the enterprise
pitch; any replacement without it is not a replacement.
This "trusted metric definitions live in one governed place" property is the
entire value proposition, and it is why Google now markets LookML as the
grounding layer for AI agents (ungrounded NL-to-SQL hallucinates; NL-to-
semantic-model does not).

### 2.2 The clever machinery

Three mechanisms distinguish Looker from a naive query builder:

**Symmetric aggregates.** Joining a `many_to_one` fan-out then summing
double-counts rows. Looker solves this at SQL-generation time with a hash
trick: `SUM(DISTINCT FLOOR(value * 1e10) + ABS(HASH(pk))) - SUM(DISTINCT
ABS(HASH(pk)))` -- each row's value is made unique by its primary key, so
`SUM DISTINCT` counts it exactly once. Correct, but the generated SQL is
famously unreadable, and it silently depends on the modeler declaring
primary keys and join cardinality correctly.

**Persistent derived tables (PDTs) + aggregate awareness.** Expensive
rollups are materialized into the warehouse on a schedule or trigger. At
query time, "aggregate awareness" rewrites the query's FROM clause to hit
the smallest rollup that can still answer it correctly (a `sum` of `sum`s
is valid; a `sum` of `average`s is not). PDT build failures are a known
operational sore point -- a failed build silently cascades into broken
dashboards.

**Caching.** Query results cache keyed on the generated SQL + connection,
invalidated by datagroup policies (typically "when this table changed").

### 2.3 The rest of the product

Dashboards and Looks (saved queries), scheduled delivery (email/webhook),
alerts on thresholds, a REST API, embedded analytics (signed iframe embeds
of dashboards inside customer products), a marketplace of "Blocks"
(prebuilt models), and -- the 2025/2026 push -- Gemini-powered Conversational
Analytics: chat-with-your-data grounded in the LookML model, dashboard
agents, and an agent API, all GA or in preview as of Google Cloud Next '26.

### 2.4 Why customers leave (the pain research)

Consistent themes across Gartner/G2/Capterra reviews and the 2025-2026
migration literature:

1. **The LookML specialist bottleneck.** LookML requires dedicated expertise;
   every model change is edit → validate → commit → deploy through Looker's
   git integration. Business questions queue behind data engineers. This is
   the #1 cited reason for migration.
2. **Cost.** ~$35-60k/year entry, ~$150k/year average enterprise contract,
   per-user add-ons up to ~$1,665/user/year for developers -- plus the
   warehouse compute bill, which scales unpredictably with query volume,
   plus the Fivetran + dbt + Looker multi-tool stack each taking a margin.
3. **Debuggability.** When an Explore is wrong or slow, the operator must
   reverse-engineer machine-generated, symmetric-aggregate-laden SQL.
4. **PDT operations.** Silent cascade failures; scheduling opacity.
5. **Dated visualization layer** relative to competitors.
6. **Lock-in.** Years of LookML investment is captive; the Open Semantic
   Interchange initiative (60+ vendors, first spec Jan 2026) exists
   precisely because of this resentment.

### 2.5 The replacement landscape

| Product | Position | Weakness Lens should note |
|---|---|---|
| **Omni** | Ex-Looker founders; LookML-compatible-ish governed layer + AI | Proprietary; same captive-model risk |
| **Cube** | Headless semantic layer (YAML/JS), API-first, embedded/AI focus | No native viz; you still assemble a stack |
| **dbt Semantic Layer** | Metrics in the dbt repo, PR-reviewed, JDBC to BI tools | Metrics only; no query UI; YAML again |
| **Lightdash** | Open-source BI reading dbt YAML directly | Bound to dbt; YAML semantics unchecked beyond dbt |
| **Metabase / Superset** | Open-source dashboards, weak/no semantic layer | Governance is the gap Looker exists to fill |

Every one of them models semantics in **YAML or a DSL that no type system
checks**. The strongest idea in the market right now -- "the semantic layer
is the contract that grounds both humans and AI agents" -- is being built on
stringly-typed foundations. That is the opening.

---

## 3. The Codex Thesis

Codex was built for exactly this shape of problem. Point-by-point:

| Looker mechanism | Lens equivalent | Why it's stronger |
|---|---|---|
| LookML view/explore files | Codex chapters (`Lens` model values) | Type-checked source; prose is load-bearing documentation for the analyst AND the regulator |
| `value_format` / units by convention | **Unit types** (`Revenue = unit Integer`, unit families) | Mixing dollars and cents, or seconds and ms, is a compile error -- the Mars-Climate-Orbiter class of metric bug dies |
| Declared join cardinality (trusted, unchecked) | Cardinality carried in the join's **type**; grain-correct aggregation enforced by the planner with a diagnostic | Fan-out double-counting becomes a static error, not a hash trick in generated SQL |
| Symmetric aggregates (SQL hash trick) | Planner aggregates each measure **at its own grain** before joining | Correct by plan shape; EXPLAIN output stays readable |
| PDT + aggregate awareness | **Materialization facts**: content-addressed rollups, staleness tracked via WAL/ChangeStream, failures loud | A stale or failed rollup is a visible fact with provenance, never a silent cascade |
| Result cache + datagroups | Content-addressed result store keyed on (model hash, plan hash, data version) | Invalidation is exact, not policy-approximate |
| "Certified content" flag | **Trust lattice verdict** on a metric definition (Ed25519-signed vouch) | Certification is cryptographic provenance, not a UI badge |
| Git-integrated model deploy | Repository protocol: models are content-addressed facts; a change is a proposal + verdict | No deploy step; views are consistent selections of facts |
| Connections: 50+ dialects via JDBC | Target split (§4.4): Target N speaks only Codex DB; Target B emits T-SQL and uses the platform's SqlClient | The immediate product runs where the customer already is; the native product owes nothing to barbarian substrates |
| Looker is SaaS-only (Google-hosted) | One source, two deliverables: signed bare-metal CDX or emitted-C# cloud service | The customer chooses the substrate; the model and its guarantees are identical on both |
| Row/column security config | apps/data row-level security + **effect rows and capability manifests** | An embedded dashboard binary physically cannot request data outside its capability manifest |
| Audit logs (product feature) | FactStore append-only audit trail | The CRA/compliance story Codex already ships (KingsAndCourts.md) applies to analytics verbatim |
| Gemini grounding on LookML | Future: conversational layer grounded on the typed model | Same grounding argument, but the ground truth is machine-checked |

The literate angle deserves emphasis: a Lens model chapter -- prose
explaining what "Active Customer" means, directly above the typed
definition the engine executes -- is readable by the finance controller who
must sign off on the metric. Looker's docs generator approximates this;
Codex was designed for it.

---

## 4. Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Delivery                                                    │
│   Bare-metal dashboards (GOP + ui foreword)                   │
│   Browser dashboards (HTML plug transpile)                    │
│   Embedded panels (capability-scoped CDX or iframe)           │
│   Scheduled delivery + alerts (Schedule/TimingWheel + Smtp)   │
├─────────────────────────────────────────────────────────────┤
│  Lens API service                                             │
│   HTTP+JSON and binary wire protocol; Ed25519 sessions        │
│   query / model-introspection / dashboard CRUD endpoints      │
├─────────────────────────────────────────────────────────────┤
│  Query service                                                │
│   LensQuery (dims, measures, filters, sorts, limit)           │
│    -> grain checker (fan-out correctness, static)             │
│    -> planner (rollup selection over Materialization facts)   │
│    -> RelAlgebra (Target N) or T-SQL text (Target B, §4.4)   │
│   Content-addressed result cache; EXPLAIN                     │
├─────────────────────────────────────────────────────────────┤
│  Semantic model (the product)                                 │
│   Lens model chapters: tables, dimensions, measures,          │
│   joins-with-cardinality, access rules, certifications        │
│   Model registry: content-addressed versions, trust verdicts  │
├──────────────────────────────┬──────────────────────────────┤
│  Target N: apps/data engine  │  Target B: barbarian cloud   │
│   Heap/BTree/ColumnStore,    │   emitted C# service         │
│   RelAlgebra/Optimizer/MVCC, │   (csharp plug output),      │
│   WAL, ChangeStream,         │   MSSQL via SqlClient,       │
│   Security (RLS), Protocol   │   platform TLS/auth (§4.4)   │
└──────────────────────────────┴──────────────────────────────┘
```

Lens is a **separate app**. On Target N, Codex DB is both engine and
system store, spoken to over the existing wire protocol (or linked
directly for the single-box deployment -- decided in Phase 1 by measuring,
not guessing), and Lens state (models, dashboards, schedules, audit)
lives in Codex DB itself. On Target B, every layer of this diagram is
emitted C#, and the engine + system store is Microsoft SQL Server (§4.4).

### 4.1 The model surface: plain Codex values, no new syntax

**Deliberate v1 decision:** a Lens model is ordinary Codex data -- records
and lists interpreted by the Lens engine -- following the proven hybrid
pattern (`apps/games/codexmagic/CardDesigner.codex`, ShimmeringPortal Path
C). Zero compiler changes, zero new keywords, and the model still gets the
full type system. Prose sugar (`We say:` CPL forms for metric definitions)
is a later phase, if ever.

Sketch of the modeling vocabulary (types live in `LensModel.codex`):

```
Chapter: SalesModel

 Revenue is stored in cents. The controller-approved definition of
 net revenue excludes refunds and internal test accounts; both
 exclusions are visible below as typed filters, not tribal knowledge.

Section: Units

  Cents = unit Integer

Section: Tables

  orders-table : LensTable
  orders-table = LensTable {
   name = "orders",
   primary-key = "order_id",
   grain = "one row per order"
  }

  users-table : LensTable
  users-table = LensTable {
   name = "users",
   primary-key = "user_id",
   grain = "one row per registered user"
  }

Section: Joins

  orders-to-users : LensJoin
  orders-to-users = LensJoin {
   from-table = "orders",
   to-table = "users",
   on-left = "user_id",
   on-right = "user_id",
   cardinality = ManyToOne
  }

Section: Measures

  net-revenue : LensMeasure
  net-revenue = LensMeasure {
   name = "net_revenue",
   table = "orders",
   column = "amount_cents",
   agg = AggSum,
   filters = [not-refunded, not-test-account],
   description = "Controller-approved. Sum of order amounts in cents."
  }
```

`cardinality = ManyToOne` is not documentation -- the grain checker consumes
it (§4.2). `Cents` is not documentation -- a dashboard that formats a
`Cents` value as dollars must go through the declared conversion.

### 4.2 Fan-out correctness without symmetric aggregates

The Looker problem: query `users ⟕ orders` (one-to-many), measure
`sum(users.lifetime_value)` -- the join fans out one user row per order and
a naive SUM double-counts. Looker patches the SQL with the hash trick.

The Lens answer is structural: **a measure is always aggregated at the
grain of its own table.** The planner compiles each measure to a
sub-aggregation on its home table (grouped by the join keys + requested
dimensions), then joins the *already-aggregated* results. Fan-out never
reaches an aggregate. This is the pattern warehouse-native hand-writers use;
Lens makes the planner do it always.

Where the model's declared cardinality makes a fan-out impossible, the
planner may fuse the sub-aggregation away (an optimization with a
correctness proof obligation -- Phase 3 gate). A query whose measures cannot
be grained correctly (e.g. a measure over a table reachable only through a
many-join with no primary key declared) is a **static error with a numbered
diagnostic** (house style, Virtue 4): state what fanned out, show the join
path, suggest the fix. Negative tests in the battery pin every diagnostic.

### 4.3 Materializations (the PDT replacement)

A `Materialization` is a declared rollup: (source lens, dimensions kept,
measures kept, refresh policy). The engine builds it into a physical table
via the ColumnStore bulk path and records a **fact**: content hash of the
model version + plan + the WAL position it was built at.

- **Freshness** is checked against ChangeStream/WAL position, not wall
  clock -- "stale" is a computable predicate.
- **Aggregate awareness**: the planner answers a query from the smallest
  fresh materialization whose kept dimensions ⊇ requested dimensions and
  whose measures re-aggregate losslessly (`sum` of `sum` yes, `sum` of
  `avg` no -- the measure type carries re-aggregability).
- **Failures are loud**: a failed build marks the materialization fact
  broken; dependent dashboards render a visible staleness banner sourced
  from the fact, never silently wrong numbers. This is a direct answer to
  Looker's worst operational complaint.
- **Realization is per-target** (§4.4): on Target N a materialization is a
  ColumnStore table; on Target B it is a real MSSQL table built by emitted
  T-SQL (`SELECT INTO`) -- exactly Looker's PDT model, minus the silent
  failures, because the facts layer travels with the shared core.

### 4.4 Two targets, one source (the platform seam)

Lens ships to two environments from a single Codex codebase. Everything --
engine, web service, dashboards -- is written as `.codex`; the barbarian
deliverable is *generated from our AST*, never hand-written. ("Barbarian"
here means any non-Codex substrate: foreign OS, runtime, or database.)

**Target N -- Codex native.** The pure environment. Codex DB is the only
engine and the system store; dashboards render to GOP or transpile through
the HTML plug; the service speaks codex.os.net. **There are no barbarian
databases on this target** -- a Codex-native shop's data lives in Codex DB,
and the trust story stays end-to-end: signed CDX, capability manifests,
verifier at boot, trust-lattice certification.

**Target B -- barbarian cloud, for immediate consumption.** The same Lens
source is emitted as C# through `codex/plugs/csharp/` (IR → CsAst →
rendered C# source), compiled with the platform toolchain, and deployed to
a cloud service. The warehouse is **Microsoft SQL Server**, reached through
the platform's own SqlClient in the emitted runtime -- there is no wire
protocol to implement, because on this target the barbarian substrate *is
the point*. Dashboards ship as HTML/JS assets (HTML plug) served by the
emitted service.

**The seam is effects.** The shared core -- model vocabulary, validation,
grain checker, planner, SQL emission, dashboard definitions -- is pure
Codex and target-independent. Everything platform-shaped goes through a
small set of declared effects, each with one handler per target:

| Effect | Purpose | Target N binding | Target B binding (C# runtime preamble) |
|---|---|---|---|
| `[LensStore]` | system state: models, dashboards, audit | apps/data (wire protocol or direct link) | MSSQL system schema via SqlClient |
| `[LensSql]` | execute a query, stream rows | RelAlgebra plan → apps/data Executor | `SqlCommand`/`SqlDataReader` over T-SQL text |
| `[LensHttp]` | accept requests, send responses | codex.os.net listen + portfwd | `HttpListener` minimal host |
| (serialization) | JSON in/out | Json foreword | Json foreword -- transpiles as-is, no binding needed |

The C# plug already works exactly this way for core builtins: `read-line`
emits `Console.ReadLine()`, `current-dir` emits
`Directory.GetCurrentDirectory()` (Raw Reference Builtins,
`CSharpEmitterExpressions.codex`). The Lens platform bindings are new
entries in that table plus their implementations in the runtime preamble --
`lens-sql-exec` becomes a preamble method over SqlClient, `lens-http-accept`
a preamble accept loop. The pattern is proven; the work is keeping the
binding surface small and enumerated.

**Emit pipeline (Target B), concretely:**

```
lens/*.codex ──compile.ps1 -IrCce──▶ IR text
  ──csharp plug (run.ps1, TCP)──▶ Lens.cs + runtime preamble
  ──barbarian toolchain (dotnet build)──▶ one deployable service
  ──container / app service──▶ cloud, next to the customer's MSSQL
```

The HTML plug runs the same way over the dashboard modules to produce the
static web assets the service serves. The `.csproj` shell is generated by
the emit script -- the only barbarian-authored artifact is the build
invocation itself.

**Dialect SQL emission** (rescoped from Rev 1): the planner targets
whichever engine sits behind `[LensSql]`. v1 backends: **RelAlgebra plan
trees** (Target N -- no text at all, straight into the local executor) and
**T-SQL text** (Target B). The ANSI-core-plus-adapters emitter structure
stays, so later dialects are additive. The grain rule (§4.2) carries over
unchanged on both: emitted T-SQL uses aggregate-at-home-grain subqueries --
correct under fan-out and *readable*, no symmetric-aggregate hash
incantations for the operator to reverse-engineer.

**Credentials and auth.** Target N authenticates users with Ed25519
identities as elsewhere in the platform. Target B inherits the barbarian
platform's conventions -- connection strings from environment / managed
identity, TLS from the platform stack. We do not re-implement barbarian
security on the barbarian target; we inherit it and say so plainly.

**What happened to native wire connectors.** Rev 1 of this design
specified native PG/TDS/MySQL wire-protocol connectors so bare-metal Lens
could query barbarian databases directly. The target split supersedes
them: Target N does not talk to barbarian databases at all, and Target B
gets its database client from the platform for free. If a native-connector
need returns, Rev 1's analysis (public specs for PG v3 / MS-TDS / MySQL;
Oracle TNS proprietary, reachable only via ORDS or a gateway) is in this
file's Perforce history (CL 7393).

### 4.5 Governance and security

- **Identity**: Ed25519 users/sessions via apps/data Security + Identity
  foreword. No passwords, matching the platform posture.
- **Row/column security**: apps/data authorized views + RLS enforce at the
  engine; the model layer adds *access rules* (which lenses/fields a role
  may query) checked before planning.
- **Embedded analytics**: an embedded dashboard is a capability-scoped
  artifact -- its manifest names the lenses it may query. The load-time
  verifier rejects anything broader (the CDX capability story, applied).
- **Certification**: a metric definition can carry trust-lattice vouches.
  Dashboards display certification state from the lattice; a "certified"
  badge is a verifiable signature chain, not a database flag.
- **Audit**: every query (who, which model version, which plan, row counts)
  appends to the FactStore trail. Compliance evidence is a report over
  facts (ComplianceEvidence pattern).

### 4.6 Dashboards and delivery

Three render paths, all from one dashboard definition (a widget tree):

1. **Bare metal**: ui foreword → GOP framebuffer. The appliance/kiosk story.
2. **Browser**: HTML plug transpile (ShimmeringPortal Path A/C) served by
   the Lens service over NE2K + portfwd, like the ideas app.
3. **Embedded**: a dashboard compiled as a capability-scoped panel.

`Charts.codex` today: bar, line, scatter, pie → `DrawCmd` lists. Known gaps
for BI (build in Phase 4): stacked/grouped bars, time-series axes with date
bucketing, tables with conditional formatting, single-value tiles with
comparison deltas, and an HTML/SVG emission path for charts (currently
listed as a gap in ShimmeringPortal §7).

Scheduled delivery reuses Schedule/TimingWheel forewords + Smtp for email
and webhooks over the net stack. Alerts are threshold predicates evaluated
on a schedule against a lens query -- same machinery.

### 4.7 Conversational analytics (explicitly deferred)

Looker's 2026 differentiator is Gemini grounded on LookML. The Lens
equivalent -- an agent grounded on the typed model, using the same query API
any client uses -- is architecturally cheap *later* because the model is
already machine-readable and the API already exists. Nothing in v1 blocks
it; nothing in v1 builds it. (Vision doc alignment: IntelligenceLayer.txt --
specification languages as the primary programming surface.)

---

## 5. What Exists vs. What Must Be Built

### 5.1 Reused as-is (verified in-repo)

| Asset | Role |
|---|---|
| apps/data: Heap/BTree/ColumnStore/RelAlgebra/Executor/Optimizer | Physical query engine |
| apps/data: Mvcc/Wal/ChangeStream | Consistency + staleness signal |
| apps/data: Security (RLS, authorized views), Protocol (Ed25519 wire) | Enforcement + transport |
| apps/data: StarSchema, MapReduce | ETL + columnar aggregation |
| foreword: ui (43 modules), Charts, Theme | Dashboard rendering |
| foreword: Csv, Json, Smtp, Schedule, TimingWheel, Statistics | Import/export, delivery, analytics |
| codex/plugs/html | Browser delivery |
| codex.os.trust, FactStore, Identity | Certification + audit |
| codex/plugs/csharp (CsAst, emitter, raw-builtin table, TCO) | Target B: full C# emission from the Codex AST |
| codex.os.net (TCP) | Target N service transport |
| ideas/helm app patterns | HTTP service on bare metal, portfwd serving |

### 5.2 Built new (the actual work)

1. **LensModel.codex** -- the modeling vocabulary (types in §4.1) + model
   validation (dangling columns, missing PKs, unreachable joins).
2. **Grain checker + planner** -- LensQuery → grain-correct RelAlgebra plan;
   diagnostics; EXPLAIN.
3. **Materialization manager** -- build/refresh/staleness over ColumnStore +
   ChangeStream; aggregate-aware plan rewrite.
4. **Result cache** -- content-addressed, bounded (see §7).
5. **Lens service** -- sessions, API endpoints, dashboard/model registry
   persistence in Codex DB.
6. **Dashboard editor + viewer** -- widget-tree composition, both render
   paths; chart gaps from §4.6.
7. **Delivery** -- schedules, alerts, email/webhook.
8. **Platform effect seam** (§4.4) -- `[LensStore]`/`[LensSql]`/`[LensHttp]`
   effects; Target N handlers over apps/data + codex.os.net; csharp-plug
   raw-builtin entries + runtime-preamble implementations (SqlClient,
   HttpListener, JSON) for Target B, plus the generated `.csproj` shell.
9. **SQL backends** -- RelAlgebra plan trees for Target N; T-SQL text
   emitter (ANSI core + adapter) with grain-correct subquery generation
   for Target B.

### 5.3 Honest dependencies and gaps

- The executor **materializes full result sets** (README: the stated 10%
  gap). Fine for dashboard-sized results; a `LIMIT`-pushdown or streaming
  iterator becomes necessary for large exports. Phase 1 measures where the
  wall is; escalate only if hit.
- Date/time dimension bucketing (year/quarter/month/week grains) needs
  audit -- DateTime foreword exists, but the RelAlgebra Group path has to
  bucket efficiently. Phase 2 item.
- The csharp plug's coverage must be proven at Lens scale early -- the
  Phase 0 barbarian spike exists for exactly this. `run.ps1` already
  streams ~10 MB of IR through the plug, and the raw-builtin table is the
  designed extension point; the unknowns are effect/handler emission
  coverage at app scale and the generated `.csproj` shell (today the plug
  emits a single `.cs` file; the project wrapper is new, small, generated).

---

## 6. Roadmap

Per Virtue 1, every phase ends with a demo. Gates in the house style.

| Phase | Deliverable | Gate |
|---|---|---|
| **0. Twin spikes** | (a) Native: star schema (CSV via BulkLoader) into Codex DB, hand-written RelAlgebra query, chart on GOP. (b) Barbarian: transpile a toy Lens module through the csharp plug, `dotnet build`, run one query against SQL Server (LocalDB/Express) via a first `lens-sql-exec` preamble binding. No Lens product code -- proves both substrates. | Native: screenshot + known-answer test. Barbarian: emitted C# compiles and returns the correct row |
| **1. Model + query core** | (shared, target-independent) LensModel types, validation, grain checker, planner for single-table + many-to-one joins. `lens-query` known-answer tests incl. fan-out negative tests (`.failing`). | Battery green; fan-out double-count provably rejected |
| **2. Real analytics** | (shared) Filters, sorts, date bucketing, top-N, multi-join graphs; EXPLAIN; result cache. **Dual-target fixtures begin here**: every known-answer fixture runs natively and transpiled. | Dual-target battery green vs. hand-computed fixtures; cache-hit test |
| **3. Barbarian target (the immediate product)** | Platform effect seam (`[LensStore]`/`[LensSql]`/`[LensHttp]`); csharp-plug binding extensions + runtime preamble (SqlClient, HttpListener, JSON); T-SQL emitter; emitted web service serving the query API + HTML-plug dashboard assets; generated `.csproj`; cloud deploy recipe (one service + customer MSSQL). | Dual-target battery green; live MSSQL smoke (skip-gated); a deployed demo answering queries over HTTP |
| **4. Materializations** | Build/refresh/staleness; aggregate-aware planning; loud-failure banner path. Per-target realization: ColumnStore tables (N) / `SELECT INTO` tables (B). Fusion optimization only with its correctness argument written. | Staleness + failure-visibility tests on both targets; plan-selects-rollup test |
| **5. Dashboards** | Dashboard definition, viewer on GOP + HTML plug, chart gap work, editor MVP. | Both render paths from one definition; screenshot fixtures |
| **6. Service + governance** | Sessions, RLS + access rules, capability-scoped embed, audit facts, certification vouches (Target N full trust story; Target B inherits platform auth, stated). | Denied-access negative tests; audit-trail fixture; embed cap test |
| **7. Delivery + polish** | Schedules, alerts, email/webhook, model registry UX. | End-to-end demo on Target B: MSSQL → certified metric → dashboard → alert |

Phases 1-3 are the spine: typed grain correctness is the defensible
novelty, and the barbarian emit (Phase 3) is the immediate product --
customers consume Lens on the substrate they already run while the native
target remains the destination. Dashboards before full service on
purpose: a demo you can see beats an API you can curl.

---

## 7. Memory and Time-Complexity Posture (Rule 8)

Rule 8 binds **Target N**: bare metal, no GC -- analytics is the
adversarial workload for a bump allocator, so this is stated up front.
(Target B runs under the platform GC; emitted code inherits .NET memory
management. The shared core still avoids unbounded accumulation, so both
targets stay bounded by design.)

- **Per-query arena discipline**: each query executes inside a
  `__heap-save`/`__heap-restore` bracket (the emit-loop pattern); a query's
  scratch dies with it. Cross-query state (models, cache index) lives on
  long-lived decks.
- **Result cache is bounded**: fixed entry count + byte budget, LRU
  eviction (LruCache foreword). Content-addressed keys; no unbounded
  growth by construction.
- **Materialization builds** stream through ColumnStore bulk-load in
  bounded chunks; peak = chunk, not table.
- **Row streams are chunked** on both targets: Target N decodes into the
  per-query arena in bounded buffers; Target B's preamble reads
  `SqlDataReader` row-by-row. Lens never buffers a whole result set it
  didn't ask for.
- **Fixed-size accumulators** for plan nodes per the accum-capacity
  pattern; a query exceeding plan-complexity caps gets a diagnostic, not
  an OOM.
- **Complexity notes required per CL** as usual; the planner is
  small-input (model graphs, not data), the executor inherits apps/data's
  costs -- the review burden concentrates on join-order and group-by
  memory in the executor path.

---

## 8. Risks

| Risk | Mitigation |
|---|---|
| apps/data executor perf/completeness under real BI load | Phase 0/1 measure first (profiler is live); gaps become scoped apps/data CLs, not Lens workarounds |
| Grain-checker complexity creep toward full query-optimizer research | v1 rule is conservative: always pre-aggregate at home grain; fusion only with a written correctness argument |
| Chart/dashboard scope explosion | Phase 5 builds only the §4.6 gap list; everything else is backlog |
| "Plain Codex values" modeling feels verbose vs. LookML | Acceptable v1 cost; prose sugar is a later, separable phase; the type-safety payoff is the product |
| Conversational-analytics expectation (market table stakes by 2026) | Deferred consciously (§4.7); the typed model is the grounding asset, so no rework when it comes |
| csharp plug coverage gaps at app scale (effects, handlers, closures) | Phase 0 barbarian spike proves the pipeline before any product code exists; gaps become scoped plug CLs -- the raw-builtin table is the designed extension point |
| Semantic drift between targets (same query, different answer) | The dual-target battery is the gate: every known-answer fixture must pass natively and transpiled; a divergence is a red build, not a support ticket |
| T-SQL correctness (NULL semantics, collation, date functions) | Generated-SQL text fixtures + live MSSQL smoke (LocalDB/Express on the dev box), skip-gated like hardware tests |
| Barbarian ops surface (deploy, config, monitoring) | Keep it boring: one emitted service, connection string from environment/managed identity, platform logging; no bespoke infrastructure |

---

## 9. Sources

Research references (accessed 2026-07-09):

- [Introduction to LookML -- Google Cloud docs](https://docs.cloud.google.com/looker/docs/what-is-lookml)
- [Looker's open semantic layer -- Google Cloud](https://cloud.google.com/looker-modeling)
- [Understanding symmetric aggregates -- Google Cloud docs](https://cloud.google.com/looker/docs/best-practices/understanding-symmetric-aggregates)
- [Aggregate awareness -- Google Cloud docs](https://docs.cloud.google.com/looker/docs/aggregate_awareness)
- [Derived tables in Looker -- Google Cloud docs](https://docs.cloud.google.com/looker/docs/derived-tables)
- [Conversational Analytics in Looker -- Google Cloud docs](https://docs.cloud.google.com/looker/docs/conversational-analytics-overview)
- [Looker updates for agentic BI at Next '26 -- Google Cloud blog](https://cloud.google.com/blog/products/business-intelligence/looker-updates-for-agentic-bi-at-next26)
- [Google Next 2026: What's New for Looker -- Rittman Analytics](https://blog.rittmananalytics.com/google-next-2026-whats-new-for-looker-bigquery-data-platforms-and-agentic-analytics-732cb3c1aa1b)
- [Looker reviews -- Gartner Peer Insights](https://www.gartner.com/reviews/product/looker-1264314839)
- [Looker reviews -- G2](https://www.g2.com/products/looker/reviews)
- [Looker pricing 2026 -- Luzmo](https://www.luzmo.com/blog/looker-pricing)
- [Best Looker alternatives 2026 -- Holistics](https://www.holistics.io/blog/best-looker-alternatives/)
- [8 Looker alternatives without the LookML lock-in -- Colrows](https://colrows.com/blogs/looker-alternatives/)
- [Best Looker alternatives for AI analytics -- Cube](https://cube.dev/articles/best-looker-alternatives-2026)
- [Best Looker alternatives for AI analytics -- Omni](https://omni.co/articles/best-looker-alternatives-for-ai-analytics-2026)
- [Best semantic layer tools 2026 -- Bruin](https://getbruin.com/blog/semantic-layer-tools/)
- [LookML vs. dbt Semantic Layer -- Devoteam](https://www.devoteam.com/expert-view/lookml-vs-dbt-semantic-layer-which-one-is-better/)
- [Looker dialects (supported databases) -- Google Cloud docs](https://docs.cloud.google.com/looker/docs/dialects)
- [Connecting Looker to your database -- Google Cloud docs](https://docs.cloud.google.com/looker/docs/connecting-to-your-db)
- [MS-TDS: Tabular Data Stream protocol -- Microsoft Open Specifications](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-tds/b46a581a-39de-4745-b076-ec4dbb7d13ec)
- [PostgreSQL frontend/backend protocol](https://www.postgresql.org/docs/current/protocol.html)
- [MySQL client/server protocol](https://dev.mysql.com/doc/dev/mysql-server/latest/PAGE_PROTOCOL.html)
