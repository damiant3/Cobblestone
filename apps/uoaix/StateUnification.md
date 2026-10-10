# UOAIX state unification

Status: steps 1 to 6 built except C2c; C2 has remaining second writers
(see "Order of work"). Owners: red (C1, C2, C3), fester (C4, C5).

Ruling (Damian, 2026-10-07): "we need unification of this state data, and it
was stupid to put it in 2 places"; "so don't have busted caches... use one."

## The rule

1. Every fact has exactly one owning store. Every other store holds a key
   (serial, actor, index, sequence), not a copy of the value.
2. Where a value is derivable (from an action code, a list, another section),
   it is not stored: the writer omits it and the reader recomputes it.
3. Where two stores must both hold a value (a journal and the state it
   records; a vendor lot, its world pile and its owner's economy totals), one
   function writes all of them in the same call, and nothing else writes any of
   them. The save writes both and compares neither; equivalence is graded by a
   proof, not by the save.
4. A save or load check that remains is a shape check on the section's own
   bytes (ranges, counts, magic, references that must resolve). A check that
   compares two copies of one value is a defect under this rule.

The pattern already exists: the spawn writer maps a slot whose mobile is gone
to an empty slot at write time (`GameWorldSpawnCodec.codex:40`), the monster
codec does the same (`GameMonsterCodec.codex:29`), and GSI1 writes a row only
when the world still holds the serial (`GsiCodec.codex:11`). None of the three
refuses a save.

## Census

Measured at red head, 2026-10-07 (main 39149). "Save" is encode, "load" is
decode; a load refusal loses the world exactly as a save refusal loses the
session.

| # | check | the copies it compares | the one source | the other becomes |
|---|---|---|---|---|
| C1a | EUC1 save `EconomyCurrencyCodec.codex:110` and load `:153`, both `euv-valid` (`EconomyCurrencyValid.codex:133`); GVS1 save `GameVendorCodec.codex:198` and load `:254` reach it again through `gvsc-valid-body` `:130`; TLC1 save `TownLiveCodec.codex:59` a third time | the currency journal `x.events` against the copper, silver and gold money tables and the policy cells, by full replay | the money tables and policy cells (gameplay reads them; the journal is a window of `em-rows` rows over a sealed start, therefore cannot rebuild state alone) | rule 3: the journal is appended by the same `eu-*` call that changes the tables, and `euv-step` (`:85-104`) already replays through those live functions, so the replay is a self-test; it leaves save and load and becomes a proof. The GVS1 and TLC1 calls are deleted outright |
| C1b | the same replay, `euv-shadow` (`EconomyCurrencyValid.codex:28-34`) | start-time money against save-time actors, stations, nodes and trades: the replay borrows the current world | (removed with C1a) | (removed with C1a) |
| C1c | `eu-census` (`EconomyCurrency.codex:508`) | one tax rate stored three times: `copper.tax`, `silver.tax`, `world.money.tax` | `world.money.tax` | copper and silver read it; their `tax` field goes |
| C2a | GVS1 `gvsc-lots-valid` (`GameVendorCodec.codex:89`) | a lot's `quantity` against its world pile's `amount` | the pile | rule 3, through the mutator pair of C2b; the check leaves the save and becomes a proof assertion |
| C2b | `gvsc-lots-valid` `:91-94`, the UOAIX-110 grape breaker | the sum over an actor's lots (`gv-represented`) against the economy actor's `stock`, `quality`, `wear`, `cost` totals, as an upper bound | the lot and its pile | rule 3: see "C2 in detail" |
| C2c | `gvsc-sales-valid` `:101-121`, with `euv-sale-events` (`EconomyCurrencyValid.codex:124-132`) | one sale recorded three times: the GVS1 sale row, the EUC1 payment event, the economy trade row | goods (item, quantity, quality, wear, hour, buyer, seller) in the trade row; money (gross) in the EUC1 event | the GVS1 sale row keeps only its keys (`gold-action`, `currency-event`) and the two world serials the trade lacks (`source`, `delivered`) |
| C3 | TRC1 load `TitleRegistryCodec.codex:150` and `:143`; save `:175` (encode decodes its own bytes, `:172-174`) | the 128-row title table and its `used`, `count`, `sequence`, `hour` scalars against the 4096-command history replayed through `tr-execute`; and `sequence` against `count` (`:143` on the bytes, `:156` in memory) | the history: the registry starts empty (`tr-new`, `:146`), the history is complete from zero, and `tr-execute` is the live path | TRC1 stops writing the table and the scalars; load keeps the replay it already runs (`:149`) and takes the table from it, so `:150` and `:175` have nothing to compare; `sequence` is deleted |
| C4a | TNL1 save `TownNetworkLiveCodec.codex:288`, load `:300`, `tnlc-agents-valid` `:182` | each agent's `person` and `actor` (`:16-17`) against the TownLive resident | the resident | TNL1 drops both; load binds them from `t.residents` (`tnv-bind` already receives `t`, `:304`) |
| C4b | `tnlc-control-valid` `:157-161` | for control actions 0, 1 and 4, the stored target cell, `place` and `goal` against the resident's home, work or tavern spot, `tf-place` and the action code | the action code and the resident | the target, place and goal of actions 0, 1 and 4 are recomputed (rule 2). Action 3 (approach a vendor, `:162-168`) keeps its target: the approach cell is chosen state, and the check against the vendor is a reference check that stays |
| C4c | `tnlc-agents-valid` `:206-208` | the pairing of a resident's mobile to the resident's actor, held in TownLive residents and in the GVS1 owner table | the resident | the GVS1 owner rows for residents are derived at bind; see question 2 |
| C4d | `tnlc-history` `:130`, `tnlc-positive` `:203` | row `total` (at 112) against the sum of the four `hours`; row `count` (at 128) against the number of positive outputs | the `hours` list; the outputs | both stored scalars are deleted and computed |
| C4e | `tnlc-proposal-valid` `:171`, `:176` | the proposal's `actor` against the agent's `actor`; a prepared agent's proposal `kind` against its `prepared` | the agent row | the proposal's `actor` is dropped; `prepared` is the proposal kind or 0, one field |
| C5 | no save check; the UOAIX-82 spell breaker | the bound account's five character slots exist twice: the working slots `game.characters` (800 bytes) and the account table's slots. `ga-bind` (`GameAccounts.codex:206-211`) copies working to account and account to working; `ga-sync` (`:204`) copies working to account before every encode (`:222`) and at `CompositeGameRules.codex:127`; load copies account to working (`GameCheckpoint.codex:105`); `GameAccounts.codex:175` states the account copy is stale while bound. `chs-record` (`CharacterSkills.codex:28-30`) reads the working slots first and the account slots otherwise, and a read of the wrong copy is the recorded breaker | the account table's slots | `game.characters` becomes the address `ga-slots table (ga-bound table)`; see "C5 in detail" |
| C7 | TGC1 load `TownGovernmentCodec.codex:220` | the stored `royal-actor` (at 104) and mode (at 24) against the bindings the loader passes | the bindings | TGC1 stops writing both; load takes them from the bindings |
| C8 | town input save `TownInputCodec.codex:20` | a world entry's `world-event.sequence` and `tick` against the entry's own | the entry | the world event's copies are dropped from the frame |

Stays, by rule 3: every section's tick and sequence against its record's
metadata (`*-decode-record`, `TownStateCodec.codex:148`) is written by one
encode call and detects a torn or misplaced record. Not two-store checks: the
fruit breaker (`EconomyStateValid.codex:72`) is a time invariant inside one
store; `gc-slot`, `gvsc-mobile`, `tl-controlled` and `tnlc-events-valid` test
that a key resolves.

## C2 in detail

One unit taken from a lot edits three stores by hand. `gal-take`
(`GameAlchemy.codex:37-49`) subtracts from the actor's `stock`, `quality`,
`wear` and `cost`, adds to the world's `consumed`, subtracts from the lot's
`basis` and `quantity`, then updates or deletes the world pile. Lot fields are
written by hand at 52 sites in 16 chapters (counted 2026-10-07 with the
pattern `__record-set (l|lot) "(quantity|quality|wear|basis)"` over
`apps/uoaix/*.codex`). A site that moves two of the three stores and not the
third is a save-breaker, and today only the save sees it.

The economy engine debits through the pair: `ep-consume` is `ep-take` oldest
first, `ep-forget-lot` takes a lot out of the ledger at its own grade and
leaves its pile as a plain item, and `ep-wear` wears the oldest one-unit lot
of the tool, else the unrepresented units, else lifts one unit off the oldest
multi-unit lot into the remainder and wears that. A player's craft, harvest
and carve run the engine on the live actor and name their lots:
`ep-craft-picks` consumes the picked input lots and wears the named tool,
`eh-harvest-with` and `ep-wear-serial` wear the named tool. The crown's
mining tax is split from the yield before it is credited (`eh-tax-share`),
so nothing is debited back.

Ruling A (root, 2026-10-07): the lot is the source. A player's item keeps its
own grade. The engine debits named lots: the lot the action names, otherwise
the oldest lot of that actor and item first, then the actor's unrepresented
remainder at its average.

The engine cannot reach lots where they live today: `EconomyProduction` cites
Catalog, Money and SkillCurve only, and lots sit above it in `GameVendorState`.
`WorldRecords` cites only Result and WorldIndex, so the economy layer can
cite it. Therefore:

- **2a. The lot table moves into the economy.** `EpLot` (serial, actor, item,
  quantity, quality, wear, basis) and `lots` / `lot-count` live in `EpWorld`;
  `GvWorld` reads them through `currency.world`. Retirement keeps order (the
  shift of `cg-lot-remove`, not the swap of `gh-retire-lot` and
  `mgr-vendor-debit`), so the lowest index is the oldest lot. GVS1 still
  carries the bytes. Replay arm: a lot retired from the middle leaves the
  later lots in creation order.
- **2b. One goods pair in the economy.** `ep-take w actor item n lot` and
  `ep-put`, citing `WorldRecords`, each write the lot, its world pile and the
  actor's totals in one call (rule 3). `lot` 0 means oldest first. Replay
  arms: a take across two lots of different grade debits the older at its own
  grade; a take larger than the lots falls through to the remainder at the
  average.
- **2c. Every writer routes through the pair.** The engine's debits and the 52
  lot sites call `ep-take` or `ep-put`; the BritainShops trim has no job left
  (every debit names its lot, so lots never exceed the totals) and is deleted;
  C2a and C2b leave the save and become assertions in the arms. Proofs whose
  numbers came from average consumption re-pin, and each re-pin names its
  cause.

The actor's totals stay stored and are written only by the pair. Stage 2
(ruling 3, totals computed, nothing stored) follows after the read count of
`stock`, `quality`, `wear` and `cost` is measured on a soak.

Cost (R-COST), per debit: finding the oldest lot of an (actor, item) scans at
most `lot-count` rows, bounded by `gv-lot-limit` = 1024
(`GameVendorState.codex:42`); `ep-craft` debits up to three inputs and a tool,
therefore up to four scans per craft. An order-keeping retirement shifts at
most 1023 rows. Removed: the save's `gvsc-lots-valid`, which calls
`gv-represented` (a walk of every lot) four times per lot, O(1024 x 4 x 1024)
per save at the limit; and the shop trim, which walks the lots after every
shop step. Heap: no new allocation per debit.

## C5 in detail

`ga-bind` is called by four production sites (`CompositeGameRules.codex:122`,
`:128`, `GameLinks.codex:63`, `GameSession.codex:268`) and 7 proofs. Making
`game.characters` an address into the account table requires:

1. `ga-bind` re-points the `characters` field of the shard it is given,
   instead of copying 800 bytes each way; every holder of a copy of the
   address re-reads it from the shard. The step begins with the census of
   holders (`.characters` is read in 22 chapters and 38 proofs, 2026-10-07).
2. `ga-sync` and its three callers, and the load copy at
   `GameCheckpoint.codex:105`, are deleted.
3. The overlap guard at `GameCheckpoint.codex:74` is restated against the
   account table, which then holds the working slots.
4. `chs-record` reads one place: the account slots.
5. The undo and delta snapshots (`GameTransaction.codex:79`, `:100`,
   `GameDelta.codex:111`, `:128`, `CompositeStore.codex:67`, `:90`) copy 800
   bytes from the address, unchanged.

## Order of work

Each step is one CL, graded by the proofs that cite the changed chapters.
Each section format change bumps that section's version and refuses older
worlds; root boots a fresh world (Damian, 2026-10-07: "we don't need save
durability its wasted code and testing").

1. Built (main 39197): GVS1 and TLC1 no longer replay currency.
2. C2, ruling A. Built: 2a, the lot table in `EpWorld.goods`, retirement
   keeps creation order (main 39229); 2b, `ep-take`/`ep-put` (main 39263);
   2c reduced (root), the hand-written debits through the pair: bandages,
   kindling, alchemy, cooking, smelting, whittling, fish steaks, reagents,
   ammunition and eating (main 39296); the engine's debits through the pair
   and the BritainShops trim deleted, the player craft, harvest and carve
   candidate worlds and the tax debit replaced, and every lot move routed
   through the economy (fester): `ep-list` (gv-add-lot), `ep-relist`
   (harvest stacking), `ep-hand` (sale, theft), `ep-join` and `ep-split`
   (stack drag), `ep-unlist` (released player, vanished pile). No lot field
   or actor total is written outside `EconomyProduction` except by the codec
   load and the undo restore. C2b left the save. C2a by rule 2: a lot stores
   no quantity; every reader takes its pile's amount (`ep-amount`), GVS1
   writes those bytes from the pile and its decode ignores them (no format
   change). A world operation that moves a pile before its `ep-*` call
   (vendor delivery, theft, drag and drop) is read after the move. Open: C2c,
   held for Damian's next fresh world. Stage 2 is not taken.
3. Built by fester (main 39205): one character copy.
4. Built (main 39303): no save or load replays currency; TRC1 v2 stores the
   history only.
5. Built by fester (main 39226, 39277): TNL1 copies dropped; resident owner
   rows bound at TNL1 load.
6. Built by fester (main 39277): C1c, C7, C8. Open: C2c (sale rows reduced to
   keys), held because it changes the GVS1 format and would cost Damian his
   world mid-play (root, 2026-10-07).

## Cost (R-COST)

Removed from every composite save, and again from every load: three full
currency replays over up to `em-rows` events (C1a), each inside a heap mark;
the GVS1 sale scan, one binary search per sale (C2c). Removed wherever TRC1 is
saved or loaded: one title replay over up to 4096 commands per save (load
keeps one). Added in steps 1-6: nothing per save; step 2 adds a lot scan of
at most 1024 rows per goods debit (see "C2 in detail"), and step 3 replaces two 800-byte copies per bind
with one field write. Heap: no new allocation. Stage 2 of C2 is the one item
that adds a per-read walk, and its read count is measured before it lands.

## Relation to the save policy

fester's save policy (always save, log a failed check, keep serving) stays as
the net. After the order above, every remaining logged check is a shape
check, so a logged failure names a defect in one store, not a disagreement
between two.

## Rulings (root, 2026-10-07)

1. C1: the money tables are the source; the journal is an audit trail.
2. C4c: root ruled "load TLC1 first, if TLC1 reads nothing from GVS1". It
   does: `tlc-decode` binds the town to the GvWorld (`tl-bind v`,
   `TownLiveCodec.codex:121`), reads `v.game.world` (`:120`), and
   `tlc-valid` (`:126`) looks up GVS1's vendor and slot tables. Therefore GVS1
   loads first without resident owner rows, TLC1 loads next, and the owner
   rows are derived from the residents after it. GVS1's load check that every
   lot's actor has an owner (`gvsc-lots-valid`, `GameVendorCodec.codex:84`)
   then runs after the derivation, in the composite, not inside
   `gvsc-decode`. Step 5 carries this.
3. C2 stage 2 is in scope ("use one" covers it): measure the read count of
   the actor totals, and take stage 2 unless the walk costs real time. It
   follows step 2.
