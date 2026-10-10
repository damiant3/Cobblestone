# UOAIX server hard caps

Every fixed limit the UOAIX game server still enforces on a count of things. Paths are under `apps/uoaix/`. Values
were read from the source at main 41130 (2026-10-08); rows marked † were read at main 40507 the same day and not
re-measured. Re-read the constant before quoting one in a decision (L-COUNT).

The ruling (Damian, 2026-10-08): a cap stays only when a protocol, file format or other outside definition fixes it, or
when it bounds what one player can make the server do. Every other limit is operational and grows
(`docs/Designs/Done/Apps/UoaixDynamicTables.md`). The tables that grow are not listed here; every cap left is deliberate, set by the authored content, or a window that a seal, snapshot or commit rolls.

## Deliberate: an outside definition or server safety sets the bound

| Cap | Value | Bounds | Where |
|---|---|---|---|
| `world-max-objects` | 2^24 | World slots: the serial scheme's slot field (ruling 1) | WorldRecords, WorldIndex |
| `world-max-container-depth` | 8 | Container nesting; every parent walk is bounded by it | WorldRecords |
| `go-wire-limit` | 65535 bytes | One incoming packet (wire field width) | GameOpcode |
| `stx-max-bytes` / `gl-reply-packets` | 65536 bytes / 4096 | Transmit and packets per response | ShardTx, GameLinks |
| `stx-admin-bytes` | 2097152 bytes | Transmit per admin-panel response: the sealed panel page, about 550 KB (2026-10-09); `bvt/BvtAdminTx` fails when the page outgrows it | ShardTx, ShardLinks |
| `ss-protocol-bytes` / `sl-recv-bytes` | 69632 / 8192 bytes | One connection's construction arena and receive buffer | ShardServer, ShardLinks |
| `cl-limit` | 400 bytes | The launch record | CompositeLaunch |
| game links | 1 to 256 | The `LINKS` launch line (32 without one; 0 admits logins by heap and round time); every per-link table is fitted at boot | CompositeLaunch, GameLinks, ShardLinks |
| characters per account † | 5 | The 1.25 client's character list | GameSession |
| `gt-max-slots` | 5 | Slot images per game transaction | GameTransaction |
| `wa-max-events` / `wa-max-bytes` | 64 / 8256 | Events and bytes per world action | WorldAction |
| `gcv-item-limit` | 32 | Items drawn per reply | GameClientView |
| `cf-limit`, duel slots | 32 / 32 | Campfires and campers, duels: one per game link | Campfires, CompositeGameRules |
| `wf-pending-limit` / `wf-shown-limit` | 64 / 4096 | Flora redraws and shown statics per viewer | WorldFlora |
| `gv-offers` † | 32 | Quote lines per player (one cart) | GameVendorState |
| `cb-event-rows` | 32 | Damage and death events per pulse | GameCombat |
| `cgf-hunt-limit` | 128 | Steps in one NPC fighter's hunt | CompositeFighter |
| summons, moongate followers, walls † | 5 / 32 / 32 | Per caster | GameMagery, GameMoongates, MageryActions |
| `ep-stock-limit` | 10^6 | One item's stock per actor, quantity per trade | EconomyProduction |
| `ep-limit`, `em-limit`, `tr-limit` | 10^12 | Any economy value (overflow) | EconomyProduction, EconomyMoney, TitleRegistry |

## Content: the authored data sets the bound

| Cap | Value | Bounds | Where |
|---|---|---|---|
| `eg-station-kinds` | 24 | Station kinds | EconomyCatalog |
| `bb-shop-count` | 20 | Britain shops | BritainCatalog |
| spawn regions † | 1024 | The spawn catalog's regions | GameWorldSpawn |
| `cst-most-rows` | 7 | Clothing rows per styled NPC | CharacterStylist |
| `cgm-limit` / `cgw-sheep` / `cv-guard-limit` | 4 / 4 / 4 | Miners, the flock, foot guards on patrol | CompositeGameRules, CivicLiveState |
| `shp-haven-max`, `gm-new` † | 8 / 8 | Pirate ships, monsters at the monster home | Ships, GameMonster |
| `cgg-raw-cap` | 16 | A worker's raw stockpile (a gameplay rule) | CompositeGatherer |
| live residents † | 3 | The authored townsfolk who walk (`TownLive` checks the count is 3) | TownLiveState |
| `pls-max-*` | 64 KiB script; 256 marks, 64 scenes, 64 cast, 256 set pieces, 512 states, 4096 actions, 2048 cues, 4096 heard words | One play script and its tables (2026-10-09) | PlayScript |
| `plr-slots` / `sty-max-*` | 4 plays; 64 stories, 256 sets, 4096 set members | Plays in one world, and the story registry (2026-10-09) | PlayRun, PlayStories |

## Windows: rolled by a seal, a snapshot or a commit

| Cap | Value | Rolls | Where |
|---|---|---|---|
| `em-rows` | 8192 | Money ledger rows, emptied by the currency seal | EconomyMoney |
| `ep-trade-limit` | 4096 | The trade window's first block; it grows and the seal empties it | EconomyProduction |
| sale log † | 1024 | Vendor sales, rolled before every cart (`gv-seal-sales`) | GameVendorState |
| `ep-lot-limit` | 1024 | GVS1's fixed lot block; further lots travel in its tail | EconomyProduction |
| `ce-limit` | 1024 | Composite event ring (one pulse's events for every link) | CompositeEvents |
| `wj-max-events` | 16384 | World journal events between snapshots | WorldJournal |
| `cd-max-changes` / `gd-entries` | 524288 / 1712 | Changes per composite commit / game delta | CompositeDelta, GameDelta |
| NPC speech audit, town events † | 1024 / 1024 | Rolling audit rows | GameNpcSpeech, Townsfolk, TownMind |
| `cg-dead-rows` | 16 | Bodies awaiting a guard; a full ring overwrites | CompositeGameRules |
| `cg-alarm-rows` / `cg-rounds-limit` | 64 / 64 | NPC alarm memory and tavern rounds, hashed by serial when full | CompositeGameRules |
| `tl-route-limit` | 64 | Shared walk routes, a cache replaced by cursor | TownLivePath |
| walk map † | 256 colliders | The walk window's colliders, rebuilt as the window pages | WalkMap |
