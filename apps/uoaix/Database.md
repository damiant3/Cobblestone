# UOAIX database

All shard state lives in one database built on the data quire, Codex DB
(`apps/data`, `apps/data/README.md`), under one transaction engine
(Damian, 2026-10-04: "all player-player trades are logged server side ->
use the data quire and design a proper database for all this state").
`UOAIX.md` owns what the shard does; this document owns how its state is
stored.

## What Codex DB gives, and the one gap

Codex DB already has 8 KB slotted pages, typed rows, heaps, B+ tree
indexes, a write-ahead log with checkpoints and recovery, transactions
with two-phase locking and MVCC, a spatial index, a time-series store, an
audit log and change streams, all in Codex on bare metal.

**It has no storage backend**: `Wal.codex`,
`BufferPool.codex` and `Page.codex` contain no disk, block or flush path,
so pages and log records live only in memory. The shard's first database
work is a page store and WAL backend over the shard's disk
(`apps/uoaix/WorldDisk.md`, `apps/uoaix/VirtioX86.md`): pages read and
written by block, and a commit acknowledged only after its WAL record is
flushed to the device.

## The rule for every write

**One game action, one transaction.** A trade, a purchase, a craft, a loan,
a harvest or a death writes all its effects (items moved, coin moved, the
log row) in one transaction, so a crash leaves either the whole action or
none of it. The WAL replaces the bespoke event journal; checkpoints replace
the per-game-day snapshot.

## Tables

| group | tables | store |
|---|---|---|
| Accounts and characters | accounts, characters, skills, stats, notoriety | heap + B-tree |
| Items | item_types, items (serial, type, amount, hue, container or position), item_titles (serial, owner, mark) for engraved or stamped items only, title_registry (serial, registrar: a town or the crown, and every owner, transfer and re-marking with its time, never deleted), recipes, stations | heap + B-tree; positions in the spatial index |
| The world | regions (towns, guard zones, dungeons, safe zones), harvest_nodes and their regrowth, plots, construction_sites, houses, ships | heap + B-tree; spatial index |
| Townsfolk | npcs, personas, homes, workplaces, schedules, families, npc_memory (bounded per NPC) | heap + B-tree |
| Wilds and law | monster_homes, populations, crimes (deed, doer, witnesses, reported), sentences, jail_terms, bounties | heap + B-tree; spatial index |
| Money | purses (player, NPC, town, treasury), loans, the coin ledger | heap + B-tree; the ledger in the time-series store |
| Logs | trades, admin_actions, conduct_reports, connections, world_health | time-series store and the audit log |

**Positions use the spatial index**, because the rules ask spatial
questions every tick: who is within hearing distance of a call, is a guard
within three tiles, how many monsters are in a dungeon level.

## Trades and coin

- **Every player-to-player trade is logged on the server**, in the same
  transaction as the transfer: both parties, every item each way (serial,
  type, amount), the coin each way, the time and the place. Trade rows are
  never deleted and are readable from the admin panel.
- **The coin ledger is double-entry:** every coin movement is a row with a
  payer purse, a payee purse and an amount, including the treasury's tax,
  grants and loans and the gold that decays into the treasury. The coin
  census of `UOAIX.md` stage E becomes a query over this ledger.

## Cost

Each table states its row size and the row count it is budgeted for before
it lands (R-COST, L-PEROBJECT); hot pages stay in the buffer pool, and a
tick's writes batch into one transaction per action, not one per field.
The logs that grow without bound (trades, the coin ledger, admin actions)
are kept whole: their growth per active player per day is measured and
written here, dated, before the shard opens to a second player.

## Moving onto the database

The snapshot and journal codecs that exist today (`WorldPersistence.md`)
stay in service until the database passes the same restart proof on the
same disk; the old path is retired in a later change, never in the one
that adds the new path (L-FALLBACK).

## Owners

| part | lane |
|---|---|
| The page store and WAL backend over the shard disk, and the move off the snapshot/journal codecs | fester |
| The townsfolk, economy, mind and government tables | val |
| Accounts, characters, items, world, trades and combat state | reek |
| Admin panel queries over trades, the action log, reports and connections | blu |
