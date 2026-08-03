# Codex DB -- Librarian's Index

A how-to guide for the Codex database engine. 38 modules, one type
system, bare-metal. No OS, no libc, no dependencies outside the
Codex foreword library.

---

## What Is This

Codex DB is a multi-model database server written entirely in Codex.
It runs on codex-vm or real x86-64 hardware. It supports relational
(OLTP), columnar (OLAP), graph, spatial, time-series, full-text, and
document storage -- all under one transaction engine and one query
language.

The query language is native Codex. Queries are typed expressions
that the compiler checks. No SQL strings, no parsing at runtime for
the embedded path. The wire protocol serves external clients over
TCP with Ed25519 authentication.

---

## Quick Start

### 1. Create a Database and Tables

```codex
let cat = catalog-new
in let tdef = table-def-with-pk "employees" [
  col-def-not-null "id" ColInteger,
  col-def-not-null "name" ColText,
  col-def "department" ColText,
  col-def "salary" ColInteger
] ["id"]
in let result = catalog-create-table cat tdef
in result.cat
```

### 2. Insert Rows

```codex
let schema = table-schema tdef
in let row = row-new schema [
  ValInteger 1, ValText "Alice", ValText "Engineering", ValInteger 95000
]
in let ir = catalog-insert-row cat "employees" row
in ir.cir-cat
```

### 3. Query with the Pipe Operator

```codex
RelScan "employees"
  |> RelFilter (PredColCmp "department" CmpEq (ValText "Engineering"))
  |> RelProject (proj-columns ["name", "salary"])
  |> RelSort [SortSpec { sort-col = "salary", sort-dir = SortDesc }]
  |> RelLimit 10
  |> execute cat
```

### 4. Group and Aggregate

```codex
RelScan "employees"
  |> RelGroup ["department"] [
    AggSpec { agg-func = AggCount, agg-alias = "headcount" },
    AggSpec { agg-func = AggSum "salary", agg-alias = "total" },
    AggSpec { agg-func = AggAvg "salary", agg-alias = "avg" }
  ]
  |> execute cat
```

### 5. Join Two Tables

```codex
RelJoin (RelScan "employees") (RelScan "departments")
  (PredColCmp "dept-id" CmpEq (ValText "name"))
  |> execute cat
```

---

## Module Map

### Storage Engine

| Module | Purpose |
|--------|---------|
| `Page` | 8 KB slotted pages, read/write/delete slots |
| `Row` | Typed row encoding/decoding (Integer, Text, Boolean, Null) |
| `Heap` | Unordered row storage, sequential scan, insert/delete/update |
| `BufferPool` | LRU page cache with pin counting and dirty tracking |
| `BTreeIndex` | B+ tree on typed composite keys, point lookup, range scan, rebalance |
| `Wal` | Write-ahead log -- redo records, crash recovery, checkpoint, truncation |

### Schema and Catalog

| Module | Purpose |
|--------|---------|
| `Schema` | Column definitions, table definitions, constraints, DDL results |
| `Catalog` | System catalog -- create/drop table, create index, insert with auto index maintenance |

### Query Engine

| Module | Purpose |
|--------|---------|
| `RelAlgebra` | Relational algebra IR -- Scan, Filter, Project, Join, Group, Sort, Limit, Distinct, Union, Intersect, Except |
| `Executor` | Volcano-style pull iterator -- executes the RelOp tree against the catalog |
| `HashJoin` | Hash join, left hash join, semi join (EXISTS), anti join (NOT EXISTS) |
| `SortMerge` | Sort-merge join and left join for pre-sorted data |
| `Optimizer` | Cost model, selectivity estimation, predicate pushdown, join algorithm selection, EXPLAIN, plan cache |

### Transactions and Concurrency

| Module | Purpose |
|--------|---------|
| `Transaction` | Begin/commit/abort state machine, WAL integration, isolation levels |
| `LockManager` | 2PL with S/X/IS/IX modes, lock table, grant/wait, release + promotion |
| `Deadlock` | Wait-for graph cycle detection via DFS, victim selection |
| `Mvcc` | Multi-version concurrency -- versioned rows, snapshot scans, garbage collection, optimistic concurrency validation |
| `TwoPhaseCommit` | Distributed 2PC -- coordinator/participant, prepare/vote/decide, WAL-backed recovery |

### Analytics

| Module | Purpose |
|--------|---------|
| `ColumnStore` | Columnar storage -- per-column chunks, bulk load, sum/min/max/avg, filtered scans, RLE encoding |
| `MapReduce` | Map-reduce on columnar data -- map/shuffle/reduce, sum-by/count-by/avg-by |
| `StarSchema` | On-demand star/snowflake ETL -- auto-discovers fact/dimension tables, materializes denormalized columnar views at query time, invalidates on write |

### Multi-Model

| Module | Purpose |
|--------|---------|
| `GraphStore` | Property graph -- nodes, edges, adjacency, BFS shortest path, DFS traversal, pattern matching, PageRank, connected components |
| `SpatialIndex` | Dense grid (fixed cell), quadtree (adaptive 2D), octree (3D), range/radius/KNN queries |
| `TimeSeries` | Append-optimized time-ordered storage -- windowed aggregation, downsampling, rate of change, gap detection, retention, tag filtering |
| `FullText` | Inverted index, tokenizer, stop words, TF-IDF, BM25 ranking, boolean queries (AND/OR/NOT), phrase search, prefix search, document store |

### Server and Network

| Module | Purpose |
|--------|---------|
| `Protocol` | Binary wire protocol -- length-prefixed frames, auth challenge/response, query/result/error messages, 2PC coordination |
| `Session` | Per-connection state machine -- transaction tracking, query dispatch, result encoding |
| `Server` | Connection management, request routing, DDL operations, demo entry point |
| `DbBoot` | Bare-metal boot -- service state (running/paused/stopped), system catalog bootstrap, health status |
| `Proxy` | Forward + reverse proxy -- 5 load-balancing strategies, health checks, circuit breakers, sticky sessions, URL rewriting |

### Security

| Module | Purpose |
|--------|---------|
| `Security` | Users (Ed25519 auth, 4 levels), authorized views, row-level security, column encryption, time-window access control |

### Operations

| Module | Purpose |
|--------|---------|
| `BulkLoader` | Batch insert, CSV/TSV/pipe-delimited import, table export |
| `Backup` | Full backups, log backups, restore, roll-forward to LSN or transaction ID, backup chain validation |
| `Replication` | WAL shipping to followers, follower apply, lag tracking, sync health checks |
| `ChangeStream` | CDC -- WAL-tailed event streaming, filtered subscriptions, consumer groups, event replay |
| `SystemDb` | Cross-app system database -- centralized logging, adaptive firewall, anomaly detection, shared deny lists, full-text log search |
| `DbAdmin` | Web admin console -- dashboard, table browser, query runner, transaction/lock/index/plan/backup/security/replication pages |

---

## How-To Recipes

### Create an Index

```codex
let idef = index-def-unique "idx_emp_name" "employees" ["name"]
in catalog-create-index cat idef
```

### Bulk Load from CSV

```codex
let lines = ["id,name,dept,salary", "1,Alice,Eng,95000", "2,Bob,Sales,88000"]
in let mgr = txn-manager-new
in bulk-import-lines cat mgr "employees" lines default-import-config
```

### Run a Transaction

```codex
let mgr = txn-manager-new
in let br = txn-begin mgr Serializable
in let cat2 = ... do work with br.tbr-txn.txn-id ...
in txn-commit (br.tbr-mgr) (br.tbr-txn.txn-id)
```

### Use MVCC Snapshots

```codex
let vs = vs-new
in let vs2 = vs-insert vs row1 1
in let vs3 = vs-insert vs2 row2 2
in let vs4 = vs-delete vs3 0 3
in let snapshot = vs-scan vs4 2    -- sees both rows (txn 3 not visible yet)
```

### Use Optimistic Concurrency

```codex
let ws = occ-record-read occ-new 0 1    -- read row 0, version 1
in ... do work ...
in let validation = occ-validate ws vs  -- check nobody changed row 0
in when validation
  is OccValid -> commit
  is OccConflict (row) -> retry
```

### Hash Join Two Result Sets

```codex
let emps = (execute cat (RelScan "employees")).qr-rows
in let depts = (execute cat (RelScan "departments")).qr-rows
in hash-join depts emps "name" "dept"
```

### Column Store Analytics

```codex
let rows = (execute cat (RelScan "orders")).qr-rows
in let ct = ct-bulk-load (ct-new "orders" order-schema) rows
in let total = ct-sum ct "revenue"
in let by-region = mr-sum-by ct "region" "revenue"
```

### On-Demand Star Schema

```codex
let cache = star-cache-new
in let result = star-query cache cat "orders"    -- auto-discovers dimensions, materializes
in let new-cache = when result is (c, store) -> c
in let store = when result is (c, store) -> store
in mr-sum-by store "customer_name" "total"       -- runs on denormalized columnar data
```

### Graph Queries

```codex
let g = graph-new
in let g2 = graph-add-node g (gnode 1 "Person" |> gnode-set "name" (ValText "Alice"))
in let g3 = graph-add-node g2 (gnode 2 "Person" |> gnode-set "name" (ValText "Bob"))
in let g4 = graph-add-edge g3 1 2 "KNOWS"
in graph-match-pattern g4 "Person" "KNOWS" "Person"    -- finds Alice -> Bob
in graph-shortest-path g4 1 2                            -- BFS path
in graph-pagerank g4 10 85                               -- 10 iterations, 0.85 damping
```

### Spatial Queries

```codex
-- Dense grid
let grid = dg-new 100 (BoundingBox { bb-x-min = 0, bb-y-min = 0, bb-x-max = 10000, bb-y-max = 10000 })
in let grid2 = dg-insert grid (SpatialEntry { se-point = Point2D { px = 150, py = 200 }, se-row-id = rid, se-data = ValText "sensor-1" })
in dg-radius-query grid2 (Point2D { px = 150, py = 200 }) 500

-- Quadtree
let qt = qt-new bounds 16 8
in let qt2 = qt-bulk-load qt entries
in qt-knn qt2 center 5    -- 5 nearest neighbors
```

### Time Series

```codex
let series = ts-series-new "cpu-usage"
in let series2 = ts-append-value series 1000 45
in let series3 = ts-append-value series2 2000 67
in let series4 = ts-append-value series3 3000 23
in ts-window-aggregate series4 1000 4000 1000    -- 1-second windows
in ts-downsample series4 2000                     -- 2-second buckets
in ts-detect-gaps series4 1500                    -- gaps > 1.5s
```

### Full-Text Search

```codex
let store = ft-store-new
in let doc = ft-doc-set (ft-doc-set (ft-doc 1 "articles" 0) "title" "Codex DB Guide") "body" "A complete guide to the database engine"
in let store2 = ft-store-add store doc ["title", "body"]
in ft-store-search store2 "database guide"           -- BM25 ranked results
in ft-store-bool-search store2 (FtAnd (FtTerm "database") (FtNot (FtTerm "legacy")))
```

### Change Data Capture

```codex
let cs = cs-new
in let result = cs-subscribe cs "my-consumer" ["employees"] 0
in let stream = when result is (s, sub) -> s
in let events = wal-to-events wal-records table-names
in let delivery = cs-poll stream "my-consumer" events    -- get matching events
```

### System Database and Firewall

```codex
let db = sysdb-new
in let db2 = sysdb-register-app db "web-server"
in let db3 = sysdb-log-info db2 "web-server" "10.0.0.5" "request received"

-- Every request checks the firewall
in let result = sysdb-check-request db3 "10.0.0.5" now 1024 2048 False
in let db4 = when result is (d, decision) -> d
in when (when result is (d, decision) -> decision)
  is FwAllow -> handle-request
  is FwDeny (reason) -> reject
  is FwRateLimit (max) -> throttle
```

### Reverse Proxy

```codex
let rp = rp-new LbRoundRobin sysdb
in let rp2 = rp-add-backend rp (backend-new 1 "10.0.1.1" 5432)
in let rp3 = rp-add-backend rp2 (backend-new 2 "10.0.1.2" 5432)
in let rp4 = rp-add-rewrite rp3 "/api/v1" "/internal"
in let result = rp-route rp4 "client-addr" "/api/v1/query" now
in result.pr-backend    -- selected backend
```

### Backup and Restore

```codex
-- Full backup
let backup = create-full-backup cat wal 1000

-- Log backup (incremental)
let log-bak = create-log-backup wal last-backup-lsn 2000

-- Restore to a point in time
let restored = restore-full backup
in roll-forward (restored.rr-catalog) (log-bak.lbd-records) (RollToLsn 500)
```

### Security Setup

```codex
-- Create users
let store = user-store-add (user-store-add user-store-new (user-admin "root" pubkey-hash)) (user-reader "viewer" viewer-hash)

-- Row-level security
let policy = RowPolicy { rp-table = "salaries", rp-name = "hide-high", rp-predicate = PredColCmp "amount" CmpLt (ValInteger 100000), rp-applies-to = AuthReadOnly }

-- Time-window access
let tw = TimeWindow { tw-name = "business-hours", tw-table = "orders", tw-start-hour = 8, tw-end-hour = 18, tw-days = [1, 2, 3, 4, 5], tw-auth-level = AuthReadOnly }
```

---

## Boot as a Standalone Appliance

Codex DB boots directly on bare metal. No OS. Compile `DbBoot.codex`
as an EFI or ELF binary, flash to USB or run in codex-vm:

```
codex-vm.exe -kernel DbBoot.cdx -mem 2048
```

The server bootstraps system tables (`sys_tables`, `sys_columns`,
`sys_indexes`, `sys_sessions`, `sys_config`), prints the banner,
and listens for TCP connections with Ed25519 challenge-response
authentication.

Service states: **running** (accepts connections and queries),
**paused** (serves existing sessions, rejects new connections),
**stopped** (rejects everything). Controlled via the admin console.

---

## Admin Console

The web admin dashboard runs on the same port. Navigate to:

| Page | URL | What It Shows |
|------|-----|---------------|
| Dashboard | `/` | Service state, metrics grid, query log |
| Tables | `/tables` | All tables with row/column/index counts |
| Table Detail | `/table/{name}` | Schema + full data browser |
| Query | `/query` | Interactive query runner |
| Transactions | `/txn` | Active/committed/aborted, WAL summary |
| Kill TXN | `/kill-txn` | List active transactions with Kill button |
| Locks | `/locks` | Lock stats, live deadlock detection |
| Indexes | `/indexes` | Per-index stats, fill %, rebalance button |
| Plans | `/plans` | Plan cache hit rate, evict/purge/disable |
| Backup | `/backup` | Full/log backup buttons, restore form |
| Security | `/security` | Users, views, row policies, time windows |
| Replication | `/replication` | Node role, follower lag indicators |
| Config | `/config` | All settings, admin action buttons |

Login required. Token is SHA-256 of server name + node ID.

---

## Architecture Principles

1. **Everything is immutable.** Records are updated by producing new
   records. MVCC creates new versions. The WAL is append-only. The
   plan cache replaces entries, never mutates them.

2. **No GC.** This runs on bare metal. Every buffer has a fixed
   capacity. The buffer pool recycles frames via LRU. MVCC garbage
   collects old versions explicitly. The WAL truncates at checkpoints.

3. **Effects are explicit.** Pure queries are pure functions on the
   catalog. Mutations (insert, update, delete) carry a `[Database]`
   effect. The type system enforces the boundary.

4. **The type system checks queries.** If a column doesn't exist or
   has the wrong type, you get a compile error, not a runtime crash.

5. **On-demand materialization.** Star schemas, plan caches, and
   columnar stores materialize lazily when first queried and
   invalidate when source data changes. No batch ETL jobs.

6. **One deny list.** The SystemDb adaptive firewall is shared across
   all apps. One app detects a DDoS, every app blocks it. Rules
   auto-expire so false positives self-heal.

---

## File Inventory

```
apps/data/
  Page.codex            8 KB slotted pages
  Row.codex             typed row codec
  Heap.codex            unordered row storage
  BufferPool.codex      LRU page cache
  BTreeIndex.codex      B+ tree, rebalance, stats
  Wal.codex             write-ahead log
  Schema.codex          DDL types
  Catalog.codex         system catalog
  RelAlgebra.codex      relational algebra IR
  Executor.codex        volcano query executor
  HashJoin.codex        hash join family
  SortMerge.codex       sort-merge join
  Optimizer.codex       cost model, plan cache
  Transaction.codex     txn state machine
  LockManager.codex     2PL lock table
  Deadlock.codex        wait-for graph
  Mvcc.codex            MVCC + optimistic concurrency
  TwoPhaseCommit.codex  distributed 2PC
  ColumnStore.codex     columnar storage + RLE
  MapReduce.codex       map-reduce analytics
  StarSchema.codex      on-demand star/snowflake ETL
  GraphStore.codex      property graph + PageRank
  SpatialIndex.codex    grid, quadtree, octree, KNN
  TimeSeries.codex      time-series + downsampling
  FullText.codex        inverted index + BM25
  ChangeStream.codex    CDC event streaming
  Protocol.codex        wire protocol
  Session.codex         per-connection state
  Server.codex          connection manager
  DbBoot.codex          bare-metal boot
  DbAdmin.codex         web admin console
  BulkLoader.codex      batch import/export
  Backup.codex          full/log backup + restore
  Replication.codex     WAL shipping
  Security.codex        auth, views, RLS, encryption
  SystemDb.codex        system DB + adaptive firewall
  Proxy.codex           forward + reverse proxy
  Example.codex         showcase queries
  codex.project.json    quire config
```
