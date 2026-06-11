# Codex DB

A multi-model database server written entirely in Codex that runs on bare metal with no OS, no libc, and no external dependencies — supporting relational (OLTP), columnar (OLAP), graph, spatial, time-series, and full-text storage under one ACID transaction engine.

## Modules

### Storage Engine
- **Page** — 8 KB slotted pages with read/write/delete slot operations
- **Row** — Typed row encoding/decoding (Integer, Text, Boolean, Null)
- **Heap** — Unordered row storage, sequential scan, insert/delete/update
- **BufferPool** — LRU page cache with pin counting and dirty tracking
- **BTreeIndex** — B+ tree on typed composite keys, point lookup, range scan, rebalance
- **Wal** — Write-ahead log: redo records, crash recovery, checkpoint, truncation

### Schema and Catalog
- **Schema** — Column definitions, table definitions, constraints, DDL result types
- **Catalog** — System catalog: create/drop table, create index, insert with auto index maintenance

### Query Engine
- **RelAlgebra** — Relational algebra IR: Scan, Filter, Project, Join, Group, Sort, Limit, Distinct, Union, Intersect, Except
- **Executor** — Volcano-style pull iterator executing the RelOp tree
- **HashJoin** — Hash join, left hash join, semi join, anti join
- **SortMerge** — Sort-merge join and left join for pre-sorted data
- **Optimizer** — Cost model, selectivity estimation, predicate pushdown, join algorithm selection, EXPLAIN, plan cache

### Transactions and Concurrency
- **Transaction** — Begin/commit/abort state machine with WAL integration and isolation levels
- **LockManager** — Two-phase locking with S/X/IS/IX modes
- **Deadlock** — Wait-for graph cycle detection via DFS with victim selection
- **Mvcc** — Multi-version concurrency: versioned rows, snapshot scans, GC, optimistic validation
- **TwoPhaseCommit** — Distributed 2PC: coordinator/participant, prepare/vote/decide, WAL-backed recovery

### Analytics
- **ColumnStore** — Per-column chunks, bulk load, aggregation, RLE encoding
- **MapReduce** — Map-reduce on columnar data: shuffle/reduce, sum-by/count-by/avg-by
- **StarSchema** — On-demand star/snowflake ETL: auto-discovers fact/dimension tables

### Multi-Model
- **GraphStore** — Property graph: nodes, edges, BFS shortest path, DFS, pattern matching, PageRank
- **SpatialIndex** — Dense grid, quadtree, octree, range/radius/KNN queries
- **TimeSeries** — Append-optimized time-ordered storage: windowed aggregation, downsampling, retention
- **FullText** — Inverted index, tokenizer, TF-IDF, BM25 ranking, boolean/phrase/prefix queries

### Server and Network
- **Protocol** — Binary wire protocol: Ed25519 auth challenge/response, query/result/error messages
- **Session** — Per-connection state machine
- **Server** — Connection management, request routing, DDL operations
- **DbBoot** — Bare-metal boot entry point: service state machine, system catalog bootstrap
- **Proxy** — Forward and reverse proxy with five load-balancing strategies, circuit breakers

### Security and Operations
- **Security** — Users with Ed25519 auth, authorized views, row-level security, column encryption
- **BulkLoader** — Batch insert, CSV/TSV import, table export
- **Backup** — Full backups, log backups, restore, roll-forward
- **Replication** — WAL shipping to followers, lag tracking
- **ChangeStream** — CDC: WAL-tailed event streaming, consumer groups
- **SystemDb** — Cross-app system database: centralized logging, adaptive firewall, anomaly detection
- **DbAdmin** — Web admin console with dashboard, table browser, query runner

## Completeness

90% — All 38 functional modules are fully implemented with real logic. The storage engine, query engine, transaction stack, multi-model stores, server, and operations modules are substantive and internally consistent. The primary gap is that the executor materializes full result sets rather than streaming.

## Codex Conformance

Full — Every module is written in idiomatic Codex. The server emits its wire protocol through the Protocol module and the DbAdmin module produces HTML responses. No foreign-language fragments appear anywhere.
