# Codex DB — A Relational Database Server in Codex

## Vision

A database server written entirely in Codex, running bare-metal on
codex-vm or real hardware. It is to databases what Codex is to
compilers: built from first principles, self-contained, correct by
construction, readable as a book.

The founding vision (NewRepository.txt, Chapter 3) says: "From SQL we
take: the declaration of what you want rather than how to get it. The
relational model remains one of the most powerful abstractions ever
discovered. We extend it." And: "From Codd we take: the relational
model, and the insight that data has structure independent of its
representation."

This is that extension.

## What It Is

**Codex DB** is:

1. A **relational database server** — tables, indexes, joins, ACID
   transactions, query plans, an optimizer. The RDBMS fundamentals done
   right.
2. A **query language dialect** of Codex — set-based operations embedded
   in `.codex` source as first-class expressions. Not SQL with Codex
   syntax; Codex with relational algebra as native operations.
3. A **multi-model engine** — relational (OLTP), columnar (OLAP),
   key-value, document, and map-reduce, unified under one type system
   and one transaction engine.
4. A **distributed transaction coordinator** — two-phase commit,
   multi-server messaging, partition-aware routing.
5. A **network server** — RFC-compliant TCP protocol with Ed25519
   public-key authentication and TLS 1.3 encryption.

## Where It Lives

```
apps/
  data/                          -- new quire: codex.data
    codex.project.json
    -- Storage Engine
    Page.codex                   -- page format (8KB pages, slotted)
    BufferPool.codex             -- buffer pool manager (LRU, dirty tracking)
    Wal.codex                    -- write-ahead log (LSN, redo/undo records)
    BTreeIndex.codex             -- B+ tree on pages (generalized from foreword)
    ColumnStore.codex            -- columnar page layout, run-length/dict encoding
    Heap.codex                   -- heap file (unordered row storage)
    -- Catalog & Schema
    Catalog.codex                -- system catalog (tables, columns, indexes, types)
    Schema.codex                 -- schema types, DDL operations
    Row.codex                    -- row format (typed tuple, null bitmap)
    -- Query Engine
    RelAlgebra.codex             -- relational algebra IR (select, project, join, etc.)
    Parser.codex                 -- query dialect parser
    Planner.codex                -- logical plan from parse tree
    Optimizer.codex              -- cost-based optimizer (selectivity, join ordering)
    Executor.codex               -- volcano-style pull iterator executor
    Aggregate.codex              -- aggregation operators (sum, count, avg, group-by)
    -- Transaction Engine
    Transaction.codex            -- transaction state, isolation levels
    LockManager.codex            -- 2PL lock manager, lock table
    Deadlock.codex               -- wait-for graph, deadlock detection + victim selection
    Mvcc.codex                   -- multi-version concurrency (read snapshots)
    TwoPhaseCommit.codex         -- 2PC coordinator and participant protocols
    -- Server
    Server.codex                 -- connection accept loop, session state
    Protocol.codex               -- wire protocol (framed messages, auth handshake)
    Session.codex                -- per-connection session (parse → plan → exec → result)
    Replication.codex            -- log shipping, follower apply
    -- MapReduce & Analytics
    MapReduce.codex              -- map-reduce job execution on column stores
    HashJoin.codex               -- hash join for OLAP workloads
    SortMerge.codex              -- sort-merge join
```

Dependency order: `codex.foreword` -> `codex.data` (peer to
`codex.works`; does not depend on compiler or OS internals beyond
what foreword exposes).

## The Query Language — Codex Relational Expressions

Not a string DSL. Not embedded SQL. Native Codex syntax for
set-based operations, using new operators that compose with the
existing type system.

### Design Principles

1. **Relations are typed values.** A table is a `Relation r` where `r`
   is a record type. The type system knows the schema.
2. **Queries are expressions.** A query is a Codex expression that
   produces a `Relation`. It composes with let bindings, functions,
   pattern matching — everything.
3. **Set operators are first-class.** Select, project, join, aggregate,
   group — these are functions with types that the compiler checks.
4. **Effects mark mutation.** Pure queries are pure. Inserts, updates,
   deletes carry a `[Database]` effect. Read-only queries can be
   `[ReadOnly Database]`.

### Core Types

```codex
  Relation (r) = a set of records of type r

  Column (r) (t) = a typed accessor into record type r producing t

  Predicate (r) = r -> Boolean

  Ordering (r) = r -> r -> Integer

  AggOp (r) (t) = an aggregation over Relation r producing t
```

### Query Operators

```codex
  where : Relation r, Predicate r -> Relation r

  select : Relation r, (r -> s) -> Relation s

  join : Relation a, Relation b, (a, b -> Boolean) -> Relation (a, b)

  left-join : Relation a, Relation b, (a, b -> Boolean) -> Relation (a, Maybe b)

  group-by : Relation r, (r -> k), List (AggOp r t) -> Relation (k, t)

  order-by : Relation r, Ordering r -> Relation r

  limit : Relation r, Integer -> Relation r

  distinct : Relation r -> Relation r

  union : Relation r, Relation r -> Relation r

  intersect : Relation r, Relation r -> Relation r

  except : Relation r, Relation r -> Relation r
```

### New Operator: Pipe (`|>`)

Queries chain naturally with pipe:

```codex
  employees
    |> where (fun e -> e.department == "Engineering")
    |> select (fun e -> { name = e.name, salary = e.salary })
    |> order-by (fun a b -> b.salary - a.salary)
    |> limit 10
```

### Aggregation

```codex
  sum-of : Column r Integer -> AggOp r Integer
  count-of : AggOp r Integer
  avg-of : Column r Integer -> AggOp r Integer
  max-of : Column r Integer -> AggOp r Integer
  min-of : Column r Integer -> AggOp r Integer

  orders
    |> group-by (fun o -> o.customer-id) [sum-of .total, count-of]
```

### Mutations (Effectful)

```codex
  insert-into : Relation r, r -> [Database] Nothing
  update-where : Relation r, Predicate r, (r -> r) -> [Database] Integer
  delete-where : Relation r, Predicate r -> [Database] Integer

  transact : [Database] a -> [Database] a
```

### Example — Complete Query

```codex
Chapter: SalesReport

  top-customers : Integer -> [ReadOnly Database] Relation CustomerSummary
  top-customers (n) =
    orders
      |> join customers (fun o c -> o.customer-id == c.id)
      |> where (fun (o, c) -> o.date >= date 2026 1 1)
      |> group-by (fun (o, c) -> c.name) [sum-of (fun (o, c) -> o.total), count-of]
      |> select (fun (name, total, n) -> CustomerSummary { name = name, revenue = total, order-count = n })
      |> order-by (fun a b -> b.revenue - a.revenue)
      |> limit n
```

The compiler type-checks this end to end. If `orders` doesn't have a
`customer-id` field, or if `total` isn't an Integer, you get a compile
error with a source location.

## Storage Engine

### Page Format

8 KB pages. Slotted-page layout for row store, columnar layout for
OLAP.

```
Page Header (64 bytes):
  page-id     : 8 bytes
  page-type   : 1 byte (Data, Index, Overflow, Column, Free)
  flags       : 1 byte
  free-space  : 2 bytes
  slot-count  : 2 bytes
  lsn         : 8 bytes (log sequence number for WAL)
  checksum    : 4 bytes (CRC32)
  reserved    : 38 bytes

Slot Directory (grows from end of header):
  [offset : 2, length : 2] per slot

Row Data (grows from end of page toward slot directory):
  [null-bitmap, col1, col2, ..., colN]
```

### Buffer Pool

Fixed-size pool of page frames. LRU eviction with dirty-page
tracking. Pin counting for pages under active use.

```codex
  BufferPool = record {
    frames : List PageFrame,
    page-table : HamtMap PageFrame,
    capacity : Integer,
    hit-count : Integer,
    miss-count : Integer
  }

  bp-fetch : BufferPool, PageId -> (BufferPool, PageFrame)
  bp-flush : BufferPool, PageId -> [DiskIO] BufferPool
  bp-evict : BufferPool -> BufferPool
```

### Write-Ahead Log (WAL)

Append-only log. Every mutation writes a redo record before modifying
the page. On crash recovery, replay the log from the last checkpoint.

```codex
  WalRecord =
    | WalInsert (table-id : Integer) (page-id : Integer) (slot : Integer) (data : List Integer)
    | WalUpdate (table-id : Integer) (page-id : Integer) (slot : Integer) (before : List Integer) (after : List Integer)
    | WalDelete (table-id : Integer) (page-id : Integer) (slot : Integer) (data : List Integer)
    | WalCommit (txn-id : Integer)
    | WalAbort (txn-id : Integer)
    | WalCheckpoint (lsn : Integer)
    | WalPrepare (txn-id : Integer)
```

### B+ Tree Index

Generalize the existing foreword `BPlusTree` from Integer-only to
typed keys. Page-resident nodes (not in-memory list). Support:
- Point lookup
- Range scan (forward and reverse via leaf sibling pointers)
- Prefix scan (for text keys)
- Composite keys (tuple comparison)

### Column Store

For OLAP workloads. Pages store a single column's values contiguously
with encoding:
- **Run-length encoding** for low-cardinality columns
- **Dictionary encoding** for text columns
- **Delta encoding** for sorted integer columns
- **Null bitmap** separate from values

Scans read only needed columns. Aggregations operate directly on
encoded data where possible (e.g., summing RLE runs).

## Transaction Engine

### Isolation Levels

| Level | Read Behavior | Write Behavior |
|-------|--------------|----------------|
| Read Uncommitted | No locks on read | Write locks held to commit |
| Read Committed | Lock per statement | Write locks held to commit |
| Repeatable Read | Shared locks held to commit | Write locks held to commit |
| Serializable | MVCC snapshot + validation | Write locks + conflict detection |

Default: **Serializable** (correctness over performance, per the
virtues).

### Lock Manager

Hierarchical locking: database -> table -> page -> row. Lock modes:
S (shared), X (exclusive), IS (intent shared), IX (intent exclusive).
Lock escalation when row-lock count exceeds threshold.

```codex
  LockMode = | Shared | Exclusive | IntentShared | IntentExclusive

  LockEntry = record {
    resource : LockResource,
    mode : LockMode,
    txn-id : Integer,
    granted : Boolean
  }
```

### Deadlock Detection

Wait-for graph. Background cycle detection via DFS. Victim selection
by youngest transaction (lowest cost to abort).

```codex
  WaitForGraph = record {
    edges : List WaitEdge,
    count : Integer
  }

  WaitEdge = record {
    waiter : Integer,
    holder : Integer
  }

  detect-deadlock : WaitForGraph -> Maybe (List Integer)
```

### Two-Phase Commit (2PC)

For distributed transactions across multiple Codex DB servers.

```
Phase 1 — Prepare:
  Coordinator sends PREPARE to all participants.
  Each participant writes WalPrepare, replies VOTE-COMMIT or VOTE-ABORT.

Phase 2 — Commit/Abort:
  If all vote COMMIT: coordinator sends GLOBAL-COMMIT, participants commit.
  If any votes ABORT: coordinator sends GLOBAL-ABORT, participants abort.
  Coordinator writes decision to its own WAL before sending phase 2.
```

Recovery: if coordinator crashes after prepare but before decision,
participants hold locks until coordinator recovers and replays its
WAL to determine the outcome.

## Query Engine

### Pipeline

```
Source Text
  |
  v
[Parser] -- tokenize + parse query expressions
  |
  v
Logical Plan (RelAlgebra tree)
  |
  v
[Optimizer] -- push predicates down, choose join order, pick indexes
  |
  v
Physical Plan (with access methods + join algorithms)
  |
  v
[Executor] -- volcano iterator: open/next/close
  |
  v
Result Set (streamed rows)
```

### Optimizer

Cost-based. Statistics per table:
- Row count
- Distinct values per column
- Min/max per column
- Histogram (equi-depth, 64 buckets)

Join ordering: dynamic programming for <= 8 tables, greedy heuristic
above. Index selection: compare sequential scan cost vs. index scan
cost using selectivity estimates.

### Join Algorithms

| Algorithm | Best When |
|-----------|-----------|
| Nested loop | Small inner, indexed |
| Hash join | Large unsorted, equi-join |
| Sort-merge join | Pre-sorted or merge-friendly |

## Network Protocol

### Wire Format

Binary, length-prefixed frames over TCP+TLS.

```
Frame:
  [length : 4 bytes, big-endian]
  [type : 1 byte]
  [payload : length - 1 bytes]

Types:
  0x01 AUTH_CHALLENGE   -- server -> client: nonce (32 bytes)
  0x02 AUTH_RESPONSE    -- client -> server: Ed25519 sig of nonce
  0x03 AUTH_OK          -- server -> client: session token
  0x04 AUTH_FAIL        -- server -> client: reason text
  0x10 QUERY            -- client -> server: query text
  0x11 RESULT_HEADER    -- server -> client: column names + types
  0x12 RESULT_ROW       -- server -> client: row data
  0x13 RESULT_COMPLETE  -- server -> client: row count + timing
  0x14 ERROR            -- server -> client: error code + message
  0x20 PREPARE          -- client -> server: parameterized query
  0x21 EXECUTE          -- client -> server: param values
  0x22 CLOSE_STMT       -- client -> server: deallocate
  0x30 BEGIN            -- client -> server: start transaction
  0x31 COMMIT           -- client -> server: commit
  0x32 ROLLBACK         -- client -> server: rollback
  0x40 PING             -- keepalive
  0x41 PONG             -- keepalive response
  0xF0 2PC_PREPARE      -- coordinator -> participant
  0xF1 2PC_VOTE         -- participant -> coordinator
  0xF2 2PC_COMMIT       -- coordinator -> participant
  0xF3 2PC_ABORT        -- coordinator -> participant
```

### Authentication

1. Client connects over TLS (Ed25519 + X25519 key exchange).
2. Server sends AUTH_CHALLENGE with random 32-byte nonce.
3. Client signs nonce with its Ed25519 private key, sends AUTH_RESPONSE.
4. Server verifies signature against its trust store.
5. On success: AUTH_OK with session token. On failure: AUTH_FAIL.

No passwords. Identity is key-based, consistent with Codex's trust
lattice model.

## Implementation Order

### Phase 1 — Storage Foundation
1. `Page.codex` — page format, read/write primitives
2. `Row.codex` — row serialization/deserialization
3. `Heap.codex` — heap file (sequential scan)
4. `Wal.codex` — append WAL records, replay
5. `BufferPool.codex` — page cache with LRU
6. `BTreeIndex.codex` — generalized B+ tree on pages
7. `Catalog.codex` — system tables (pg_class, pg_attribute style)
8. `Schema.codex` — DDL: create table, create index

### Phase 2 — Query Engine
9. `RelAlgebra.codex` — logical algebra nodes
10. `Parser.codex` — parse Codex relational expressions
11. `Planner.codex` — logical plan builder
12. `Executor.codex` — volcano iterator
13. `Aggregate.codex` — group-by, sum, count, avg
14. `Optimizer.codex` — predicate pushdown, index selection

### Phase 3 — Transactions
15. `Transaction.codex` — begin/commit/abort state machine
16. `LockManager.codex` — lock table, grant/wait
17. `Deadlock.codex` — wait-for graph, cycle detection
18. `Mvcc.codex` — snapshot isolation

### Phase 4 — Server
19. `Protocol.codex` — wire format encode/decode
20. `Session.codex` — per-connection state machine
21. `Server.codex` — accept loop, dispatch

### Phase 5 — Analytics & Distribution
22. `ColumnStore.codex` — columnar pages, encoding
23. `HashJoin.codex` — hash join operator
24. `SortMerge.codex` — sort-merge join operator
25. `MapReduce.codex` — map-reduce on column data
26. `TwoPhaseCommit.codex` — 2PC coordinator/participant
27. `Replication.codex` — WAL shipping

## Testing Strategy

Each phase adds test programs in `codex.test/`:
- `db-page-test` — page read/write/slot management
- `db-btree-test` — insert, lookup, range scan, split
- `db-heap-test` — sequential scan, insert, delete
- `db-wal-test` — write records, crash, replay, verify
- `db-query-test` — parse, plan, execute sample queries
- `db-txn-test` — concurrent transactions, deadlock, isolation
- `db-protocol-test` — wire format round-trip
- `db-join-test` — nested loop, hash, sort-merge correctness

## Memory and Time Complexity

**Page buffer pool**: Fixed at startup. Default 256 pages = 2 MB.
Bounded. No GC needed — frames are recycled via LRU eviction.

**B+ tree operations**: O(log_B n) where B = page fan-out (~200 for
8 KB pages with 40-byte keys). 10M rows = ~4 levels.

**WAL**: Append-only, sequential write. Checkpoints truncate the log.
Bounded by checkpoint interval.

**Lock table**: O(1) hash lookup. Lock entries freed on txn commit/abort.

**Query execution**: Volcano iterators — O(1) memory per operator
(except hash join which buffers the build side, and sort which
materializes). Streaming results, not materializing.

**Deadlock detection**: O(V + E) DFS on the wait-for graph. V = active
transactions, E = lock waits. Runs periodically, not per-lock.

No unbounded allocations. Every buffer has a capacity. This runs on
bare-metal with no GC.

## Existing Building Blocks

| Need | Foreword Module | Status |
|------|----------------|--------|
| B+ tree skeleton | BPlusTree | Exists, needs page-based generalization |
| Hash maps | Hamt | Exists, production quality |
| Key-value store | KvStore | Exists, HAMT-backed |
| Sorting | Sort | Exists, quicksort |
| Bloom filters | BloomFilter | Exists |
| Crypto (auth) | Ed25519, Sha256, DiffieHellman | Exists |
| TLS | Tls | Exists, TLS 1.3 |
| TCP transport | TcpTransport | Exists |
| HTTP (admin UI) | Http, WebServer | Exists |
| Channels (IPC) | Channel | Exists |
| CRC checksums | Crc32 | Exists |
| Content addressing | FactStore | Exists |
| Append-only log | MutationLog | Exists, pattern to follow |

## Design Decisions

1. **Pipe operator `|>`**: New compiler operator. Lexer token + parser
   infix rule + desugar to reversed function application (`x |> f`
   becomes `f x`). Benefits all Codex code, not just DB queries.
   Precedence: lower than function application, higher than `<-`.

2. **Column accessor syntax**: `.field-name` as a first-class function
   (Haskell-style record selectors). `.salary` desugars to
   `fun r -> r.salary`. Enables cleaner query expressions:
   `employees |> select .name` instead of
   `employees |> select (fun e -> e.name)`.

3. **On-disk format**: FAT16-backed via the existing `Fat16` foreword
   module. Database files are regular files on the FAT16 volume.
   Gives us file-level tooling compatibility and works with the
   existing disk image pipeline.

4. **Query language surface**: Pure Codex expressions. Queries are
   ordinary typed expressions that compose with let, if, functions,
   and pattern matching. No special `query ... end` block syntax.
