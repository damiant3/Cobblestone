# UOAIX coroners: a death in town becomes a quest

Owner: red. Register row: `apps/uoaix/uoaix-backlog.md` UOAIX-275.

## The order

Damian, 2026-10-20, after a mine elemental killed NPC 2182 at 1449,1533 in game hour 1 (live-46874):

> we need coroners, associated with the healer shop, to go haul off the corpse of this poor miner and give him a
> burial.

> it will require some adventurers to help clear the graveyard. make this a quest sort of thing for player who
> encounters the death. the mayor should be called to witness the corpse and offer the quest, if the player waits long
> enough. the first miner on the scene should decide to run and get the mayor. the mayor should hang out at the corpse
> until a player finds the scene and accepts the mission

## End state

1. A town NPC of Britain killed by a wild creature leaves a corpse that does not decay while its burial is open (an
   incident).
2. The first live Britain worker that sees the corpse (`cg-worker-witness`, `cg-dead-sight` 8 tiles) stops work, walks
   to the mayor and says so; the mayor walks to the corpse and stays beside it.
3. A player who comes within cdr-offer-reach of the corpse while the mayor stands there is offered the burial quest,
   once (L-FUTILITY); the player accepts by saying "accept" there. The mayor then returns to his day.
4. The quest is complete when no hostile creature stands in Britain's cemetery and the quest's holder stands in it.
   The cemetery is the two WorldSpawnData regions 5505 (1338,1445 to 1374,1509) and 5506 (1368,1444 to 1389,1491).
5. Two coroners of Britain's healer shop then walk to the corpse, take it (the corpse leaves the world), carry it to
   the cemetery and bury it: a grave mound stands on a free cemetery tile. The mayor pays the holder from the town
   purse.
6. The coroners never enter the cemetery while a hostile creature stands in it. A burial not done waits at the corpse.

## The model

The dead ring becomes the incident table: each row gains the corpse serial, a stage, the witness, the quest's holder
and the two coroners; a row is made only for a death that leaves a corpse (Q3 names which); the table grows with the
world instead of overwriting at 16 rows (L-SCALE, L-GROWINDEX), and it is saved with the world (a new
`CompositeCodec` section). The witness is picked whether or not the killer still lives. The guard's report and hunt of
a live killer stay as they are; `cg-dead-bury` no longer removes a corpse whose row is open. The mayor gains a walk
state of his own (an `RteWalk` on `CivicLive`). A row is a stage and its actors:

| stage | meaning | leaves when |
|---|---|---|
| 1 fallen | corpse held, no witness yet | a worker sees the corpse (`cg-worker-witness`): it yells as today and becomes the witness |
| 2 running | the witness walks to the mayor | the witness stands within 2 tiles of the mayor; it is let go then |
| 3 called | the mayor walks to the corpse, one incident at a time (a later one queues) | the mayor stands within 2 tiles of the corpse |
| 4 waiting | the mayor waits; a player in reach is offered once | a player says "accept" in reach |
| 5 quest | the holder clears the cemetery | the cemetery holds no hostile creature and the holder stands in it |
| 6 coroners | two coroners are made at the healer shop and walk to the corpse | both beside it and the cemetery clear: they take it |
| 7 carrying | the coroners walk to a free grave tile | both beside it: the mound is placed |
| 8 buried | the holder is paid, the coroners walk back to the shop | both in the shop: they are dismissed, the row is freed |

A stage-1 corpse no Britain worker sees is let go after `cg-witness-wait` decay periods and decays as before.

The witness, mayor and coroners walk on the Britain planner over the whole map (`rte-toward-in`), as workers do: the
mine to the mayor's post is about 165 tiles, a search that can pass a guard's 16,384-cell budget. A stage whose walk fails waits and tries again on the walker's failed-plan pace (`rte-pace`); nothing asks twice for an answer
(L-FUTILITY). An actor taken by an incident is released by it at stage 7 or when its own death or a server restart
frees the row. The incident table is saved with the world, so a restart keeps the corpse held and the quest open; the
actors start their walks again from where they stand.

A corpse whose row exists is skipped by the decay wheel (`cg-corpse-ms`). The grave is two world items the coroners
place on a free cemetery tile, a dirt patch (TILEDATA 0x0913) and a gravestone (0x0ED5), immovable, and they stand
until a GM removes them.

The incident is not a play (`UoaixPlays.md`): a play casts its own bots, and this one moves town NPCs that already
live, so it is a state machine over existing actors with the Play's rules on minor steps (a step proceeds without a
player) and major steps (the offer and the cemetery wait on a player).

## Stages of work

All three are BUILT and on (`CompositeGame.incidents`); BVT `bvt/BvtIncident.codex` (code 17179869183).

1. The dead ring is a SlotArray of `cg-dead-bytes` rows grown in `cs-grow` (`cg-dead-due`, WORLD GROWN bit 34) and
   saved as the composite's 25th section (`cg-incident-write-tail`/`-read-tail`, open incidents only). Row qwords: 0 x,
   8 y, 16 body, 24 the guard who heard, 32 the killer cried, 40 the live threat, 48 its spawn region, 56 corpse, 64
   stage, 72 victim (set only for an innocent, `cg-innocent`), 80 witness, 88 holder, 96 and 104 the coroners, 112 the
   mayor's post x * 65536 + y, 120 its z, 128 the house design named at "accept", 136 the grave x * 65536 + y, 144 its
   z (before stage 7, the game second the mayor first arrived plus 1, Q5), 152 paid (1) or told the purse cannot pay (2), 160 decay periods held unseen at stage 1 and the game second the
   witness ran plus 1 after (Q5), 168 the stage last logged (`INCIDENT`, CompositeStore `cs-incident-rows`) and 176
   the reports since. Stages 1 to 4 run in
   `cg-incident-step` from `cg-dead-step`; the witness is the gatherer, the farmer or a miner (`cg-britain-witness`),
   whose `cg-worker-step` walks it to the mayor (`cg-witness-walk`); the mayor walks on `CivicLive.mayor-walk`
   (`cg-mayor-walk`) and, with no incident holding him, back to his post (`cg-mayor-home`). A player's corpse is held
   too (Q3); its contents go with it at the take, as they would at decay.
2. Stage 4 (`cg-incident-offer`) offers each player within `cg-offer-reach` of the corpse
   once while the mayor stands by it, told to that player alone through the event ring (`cg-incident-tell`; every other incident line reaches each player in range the same way, `cg-incident-say`) (kind-1 rows of `CompositeGame.quests`, in memory), skipping an account that took
   a quest; "accept" there (`cg-incident-accept`, chained before `cg-monster-call`) makes the player the holder (row
   88), marks the account (kind-2 rows, saved after the incident rows in the tail) and frees the mayor. Stage 5
   (`cg-incident-quest`) moves to 6 when the holder stands in the cemetery (`cg-in-cemetery`) and no hunting creature
   (ai 2 or 11, owner 0) does (`cg-cemetery-clear`).
3. Stages 6 to 8 (`cg-incident-fetch`, `cg-incident-late`): coroners made by `cg-coroner-make` at the healer shop
   (named "<name> the coroner", robe 0x1F03), walking on `CivicLive.coroners`; the take is `cg-rot`; graves every second
   tile of region 5505 from y 1490, the rows Britain's walk window holds (`BritainWindow` starts at y 1488); the
   mound is `cg-grave-lay`. Q2: "accept" names the house (`cn-design-said`: wooden, cottage or stone; no keep, castle
   or tower exists among the deeds), and `cg-incident-pay` buys that deed from the town purse as the carpenter sells
   one (`cn-tender`, `cn-make-deed` into the holder's pack); the crown first grants the purse any shortfall, as for every town expense (`cg-fund-town`), so a purse that still cannot pay (a short silver or copper part at changed rates) is said once and paid later. The coroners walk
   home to the healer's first free floor tile (`bb-placement`, where they were made); a pair whose home has no
   approach tile or whose walk fails once is dismissed where it stands and the row is freed (L-FUTILITY; trials 47481
   and 47635: the shop centre at z 0 had no approach on the live map, and the stranded pair held every later burial).
   An accepted burial is served before an unaccepted one (`cg-accepted-waiting`).
4. BUILT, not yet passed (trials 47111, 47148, 47174: 275-offer fail, fail, unreached; state and next step in CurrentPlan's red row): soak family `coroner` (`proofs/soak-bots.ps1 -Only coroner -Bots 4`, alone, `-Minutes` 60 or
   more; root runs it): grades `275-offer`, `275-accept`, `275-refuse`, `275-burial` and `275-paid` (see its header).

## Open questions for Damian

- Q1. RULED (Damian, 2026-10-20: "1: default is fine"): a buried NPC rises as today (a guard at once, a keeper after
  5 minutes, a worker at home); the burial is of its corpse.
- Q2. RULED (Damian, 2026-10-20: "2 is a house of their choice. not a keep, castle, or tower. a house."): the quest's
  holder is paid a house of the holder's choice; a keep, a castle and a tower are refused.
- Q3. RULED (Damian, 2026-10-20: "all innocent characters, players, npcs, shopkeeps, all innocents. guards too."): the
  death of every innocent character makes an incident: players, town NPCs, shopkeepers and guards.
- Q4. RULED (Damian, 2026-10-20: "each death is an incident, but a player can only do the quest once per account."):
  every death is its own incident; an account accepts the quest once, ever.
- Q5. RULED by root (2026-10-21; trial-47285 filled 5 rows at stage 3 or 4 with holder=0, unbounded, R-COST; trial-47730
  closed c's incident as the mayor arrived, after a queue of five): an incident the mayor waits at closes a game day
  (`cg-unaccepted-close`) after he first arrived, and one still queued for him (stages 2 and 3) three game days
  (`cg-queued-close`) after its witness ran (`cg-incident-close`). With the cemetery clear the coroners bury the corpse, with no holder and no payment;
  otherwise the incident is let go as an unseen corpse is and the corpse decays, because coroners sent into a hunted
  cemetery die and their deaths are new incidents. Either way the row frees. Row 160 holds the game second the witness
  ran plus 1 from stage 2 on (`INCIDENT ... ran=`), row 144 the game second the mayor first arrived plus 1 until stage 7.

## Constants

- cdr-offer-reach 6 tiles: the mayor's offer goes to a player standing at the scene, not to one passing at the edge of
  the client's 18-tile view.
