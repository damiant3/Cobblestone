# UOAIX dynamic tables

**Status:** built, owner reek. Stages 1 to 7 and the world ceiling are written; every landing from main 41076
on was written under Damian's code-only ruling (2026-10-08) and is graded by root's builds and BVT, not by its author.
`apps/uoaix/ServerLimits.md` lists only deliberate caps, content bounds and windows.
Requested by Damian, 2026-10-08: "why isn't it dynamic, and where necessary, generous? can we use some kind of mutable
collections here instead of fixed arrays?"
## Goal

The UOAIX server holds about 150 fixed caps (`apps/uoaix/ServerLimits.md`). Most of them size a table that is
allocated once at boot and saved at a fixed byte offset: world objects (16384), economy actors (128), lots (1024),
players (64), vendors, law cases, town rows. The campaign replaces those tables with tables that grow on demand, so
the only limits left are memory and the object serial space, and keeps a fixed, generous cap only where an attacker
or a quadratic walk sets the bound.

Done means: every table in the "moves" list below grows at runtime and saves at its live size, a fresh world and a
restored world both pass the BVT, and `ServerLimits.md` lists only the deliberate caps.

Damian, 2026-10-08: "the dynamic paging should have been in from the start instead of a capped experience, as we were building a MMO persistent world server after all, and our testbed is literally one portion of the capital city, let alone the full map" (L-SCALE).

## Why the tables are fixed today

1. **No garbage collector.** On bare metal an allocation is permanent until the producing function returns
   (heap marks). A list that grows by reallocating leaves every older copy behind as dead heap.
2. **Fixed save layout.** Each table is written at a fixed offset and size inside the composite checkpoint, so a
   save is hashed, delta-compared (`CompositeDelta`) and validated region by region.
3. **Validation by range.** The save checks walk every row up to the cap (`ev-actors`, `gvsc-valid`, ...).

## Design

### The paged table

A table is a directory of fixed-size pages. A page is allocated when the table first needs a row past its last
page, from the boot-time arena that already holds every table, and is never copied and never freed; an emptied row
is reused through a free list, as `world-delete` already reuses world slots. Growth therefore costs no dead heap:
the only allocation is one new page.

- **Row address:** `page = row / rows-per-page`, `slot = row mod rows-per-page`. One extra indirection per access.
- **Page size:** a power of two rows, chosen per table so a page is a few KiB (world objects: 256 rows of 80 bytes).
- **Directory:** a fixed array of page pointers sized for the table's ceiling. A table whose ceiling is small keeps
  a small directory.
- **World object serials (root for Damian, 2026-10-08):** the live ceiling is 2^24 objects (1.3 GiB of 80-byte
  rows; the directory is 2^24 / 256 = 65536 pointers, 512 KiB). A serial's low 30 bits are
  `slot + 1 + generation * 2^24`, so a serial names its slot without a lookup and a reused slot takes a new serial.
  A slot is reused 64 times (generations 0 to 63) and then retired, never handed out again, which spends the 30-bit
  serial space (about 1.07 billion creations over a world's life, Damian's ruling 1). Near exhaustion of slots
  the server logs loudly; a save never refuses. `uint.MaxValue` objects would need wider serials and a client
  protocol change, out of scope.

### The save format

The composite checkpoint stays one image sized from the world's capacity, written by the existing codecs. `cc-layout`
lists it as segments of fixed bytes and per-slot rows, each segment's rows at its end in slot order, so a capacity
change is a relayout: segments move in memory and appended rows are zero. A commit across a growth relayouts the saved
image before the delta and replay relayouts before applying it, so the store writes only new rows and changed cells.
A new per-slot codec region must join `cc-layout`; `BvtCheckpointCeiling` checks the segments cover `cc-size`.

The store is sized at install (`install-map-cache.ps1 -StoreMiB`, default 1024) and a checkpoint is bounded by the store it is written to; a world at 1 million objects saves about 595 MiB of per-slot rows (624 bytes a slot).

### The census after every merge

Code merged from main is written for the contiguous layout, so after every merge-down the stream is searched by shape
(L-CENSUS), proofs included: `* 80`, a `world.buffer` offset of 16 or more, `int-mod (... serial - 1)`, and a raw
`peek`/`poke` on a per-slot buffer (`positions`, `active`, `slain`, `looted`, `concealed`, `calm`, `picks`, `spirit`,
`manifest`, `decay`, `visible`, magery `active`/`due`). Every hit goes through `wi-object` or `sa-at`; a missed one reads
a page directory as a row and fails far from the cause.

### Validation

Validation walks the live row count, not the cap. A row past the count must not exist; the directory past the last
page must be empty.

### Where a page comes from

The receive loop restores a common scratch boundary, `links.mark`, at every `sl-compact` (`ShardLinks.md`; `gl-round`
compacts after a round once scratch passes 1 MiB), and all persistent state sits below that mark. A page or list row
allocated during a round lands above it and is overwritten after the next compaction (L-BELOWMARK). On bare metal
there is no virtual memory to reserve without committing, so growth happens at one safe point: when a table is due to
grow, `gl-round` compacts first (scratch is then empty), the composite grows the world and every per-slot side table
with ordinary allocation (pages and list rows alike), and `links.mark` is raised to the new heap top, so the grown
state is permanent like boot state.

**Ruling (root, 2026-10-08, "if the box has room, let it grow"):** growth is bounded by a budget, a launch setting in
MiB beside the connection slots, defaulting to `bare-metal-ram-size` less the heap at `sl-new` and a scratch reserve.
The server logs the budget and its use at boot and on every commit, and a budget past 90% logs loudly, as the world's
slots do; a save never refuses.
## What moves and what stays

**Ruling (Damian, 2026-10-08): "we keep caps that are either externally fixed, or for safety of the server like
player malicious prevention. for operational issues, dynamic is the way to go."** A cap stays only when a protocol,
file format or other outside definition fixes it, or when it bounds what one player can make the server do. Every
other limit is operational and becomes dynamic.

**Moves to paged tables:** world objects and the combat actor per slot; economy actors, purses, loans, stations,
nodes, trades, lots, sales, owners; players, accounts, characters per account; vendors; townsfolk, personas, law
cases and witnesses, disputes, town works, civic rows; harvest sites, house plots and building sites; timers;
spawn slots and regions; bulletin posts, help pages; the catalog's items, resources and recipes.

**Stays fixed, because an outside definition or server safety sets the bound:**

| Cap | Value | Reason |
|---|---|---|
| Container depth | 8 | Every parent walk is bounded by it (Damian) |
| Incoming packet | 65535 bytes | Wire protocol field width |
| Socket slots | per box, from a launch setting | One slot is one connection's buffers |
| Reply packets, transmit bytes | as today | Bound the work one request can cause |
| Per-link viewer tables | as today | Small by nature; bound the redraw work per link |
| Help pages, reports, posts per player | per player | Spam control, not storage |

## Stages

Each stage lands alone, with BVT cases, and keeps the server playable.

1. **The paged table chapter** (`PagedTable.codex`, `BvtPagedTable`).
2. **World objects** (built): every row access goes through `wi-object`; a serial's low 30 bits are
   `slot + 1 + generation * 2^24`, so growth moves no row; rows, index rows, tile pages and announce cells live on
   pages behind two-level directories (`wi-dir-at`, a 2 KiB top of 256 pointers to 256-entry blocks), so the ceiling
   is the serial scheme's 2^24 slots (`wi-pages` 65536) and a table pays only for the blocks its pages use. Every
   per-slot side table is a `SlotArray` on the same directories and grows with the world through `cg-grow`; goods
   rows grow with the world and goods heads with the actor table. A world is founded on one page, grows through a
   founding-only hook while founding, installing and booting, and afterwards only at the round's growth point
   (`gl-round` compacts, `cs-grow`, `links.mark` raised; L-BELOWMARK), within the RAM budget (`cs-grow-limit`).
3. **The save format** (built): `cc-layout` lists the checkpoint as segments of fixed bytes and per-slot rows, so a
   commit or replay across a capacity change relayouts in memory and deltas only new rows and changed cells. Tables
   past their first block save in tails closing the composite after the currency (vendor with lots, account, law,
   government, town, harvest, spawn, construction, help, board).
4. **The economy tables** (built): actors (sparse rows), purses and loans (all three currencies grow together),
   stations, nodes, the trade window (ES version 11 closes the record with the live trades), goods lots (GVS1 version
   9 carries lots past its fixed block in its tail); the store and the checkpoint have no fixed cap (4.4).
5. **Players, accounts, vendors** (built), and 32 game links with every per-link viewer table.
6. **Town, law, civic and works tables** (built): townsfolk, personas, law cases and witnesses, disputes, titles,
   works orders and bounties, government events, guards and dues stamps.
7. **The remaining operational caps** (built): trades, lots, known names
   (with an index), GM memory, mutes, duels and campfires per link, speaking rows, departed vendors, economy action
   offers, and the game links: a first launch line `LINKS n` sets 1 to 256 slots (32 without one), every per-link
   table is fitted to the slot count at boot (`cs-fit-links`), and `LINKS 0` takes 256 slots and admits an account
   login only while one more link's bytes keep the heap under the growth limit and the decaying maximum round time is
   under 100 ms (root for Damian, 2026-10-08), refusing with the check that failed. Speaking rows past 40 save in a
   speech tail; the admin panel's action log and open-report ring grow at the growth point. The catalog's items,
   resources and recipes are as many as the world's catalog holds (below).

### The catalog (stage 7)

The catalog is fixed per world: it is built in code at founding (`ex-catalog`, `bb-economy`) and saved in ES, so the
item arrays need no growth point. Every per-item array is `eg-cells` wide (items + 1) and is allocated from the
catalog it serves; `eg-validate` bounds no count. ES format 12 keeps `es-bytes` fixed (header, catalog counts, gold
table, world, ledger, gold opening, turnovers) and closes with the catalog-wide block (item, resource and recipe rows,
six item arrays, the trade opening) before the actor count. SEG1 version 3 records the trade opening width at cell
120. GVS1 version 10 keeps vendor prices in its tail (width, vendor count, rows), so its fixed image does not depend
on the catalog. Older ES, SEG1 and GVS1 images are refused: a fresh world.
## Cost (R-COST)

- **Time:** one extra indirection per row access; a page allocation on growth only. The hot loops (world tile
  walks, lot scans) are linear today and stay linear; the per-item and per-lot scans that are O(rows) grow with the
  live row count, so a large world makes them slower in proportion, which today's caps hid. Stage 4 lists each
  O(actors x items x lots) walk and replaces it with an index where the live count makes it costly.
- **Memory:** the directory per table, plus at most one partly used page per table. No dead heap from growth.
- **Saves:** proportional to live rows, smaller than today for a small world, larger for a large one.

## Rulings (Damian, 2026-10-08)

1. **Object ceiling:** 30-bit serials stay ("fine with me, if we have a problem because we need > 1b objects, we will fix it then").. **Store disk:** unlimited ("unlimited, harddrive is cheap").. **Connection slots:** a launch setting, default 32; 0 means dynamic, decided at each login attempt from the current population and the box's live load ("connections at 32 default, 0 means dynamic and should be determined at login attempt based on current server population and live-load analysis (perf). e.g. if the box has room, let it grow. later, if/when we need sharded server deploys (haha!) we can discuss a heath-reporting protocol to feed this decision. for now, its simply box load").
