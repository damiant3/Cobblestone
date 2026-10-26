# UOAIX: player-made dungeons

Owner: fester. Status: proposal (UOAIX-243). No code until Damian rules Q1 to Q10.

Damian, 2026-10-12: "an idea to start writing down with me is the way to get players involved in content creation and
event planning. my idea is letting the decorators/playwrights earn "spawners" that let them decorate their own dungeon
in game, collect ticket sales to go in, let players join, run the dungeon, and then rate them for public guidance to the
proper/best crawls."

## Questions for Damian

Each lean is the design below; a ruling against a lean changes the stage the lean names.

| # | question | lean |
|---|---|---|
| Q1 | Who authors a dungeon: any player who holds a dungeon charter, or only a player the owner dubs decorator or playwright? | Any player holding a charter (bought from a vendor), therefore authoring is play, not staff work. The tools inside are the decorator's, confined to the author's own dungeon. |
| Q2 | How is a spawner earned? | The charter carries one tier-1 spawner. Every further spawner is earned: one for each 10 finished runs by distinct accounts with an average rating of 3 or better, and each 25 such runs raise the tier of the next spawner earned by one. |
| Q3 | Where does a dungeon stand: a fixed plot per dungeon, or an instance on a shared stage? | An instance (Stages, below): a dungeon is stored as data and stands on a free stage only as long as a run or a build session holds the stage. The measured void holds 14 stages of 48 by 48 tiles; 14 fixed plots cap the shard at 14 dungeons, and 14 stages cap the shard at 14 concurrent runs instead. |
| Q4 | What does a spawner raise? | A creature from the world's spawn catalog (`WorldSpawnData`) within the spawner's tier, and every dungeon holds a challenge budget: the sum of its spawners' live creature caps, at most 32 creatures alive at once. |
| Q5 | Loot: who pays for loot? | The author: loot is economy lots the author stocks in the dungeon's chests (Runs, Loot). A dungeon creature leaves no corpse and no goods, as a summoned creature does, and carries no gold (R8). The world creates nothing for a run. |
| Q6 | Death inside a dungeon? | The world's rules: a ghost, and a healer or a player resurrects. At the run's end every runner, living or ghost, stands at the entrance, and every runner's corpse moves to the entrance with every item in the corpse. |
| Q7 | Ratings: who rates, on what scale, and how does the board rank? | A runner whose account is not the author's account rates 1 to 5 at the run's end, one rating per account per dungeon, a later rating replacing the earlier. The board ranks by a confidence-weighted average: (sum + 3 x 5) / (count + 5). |
| Q8 | Does a play (`UoaixPlays.md`) run inside a dungeon? | Yes, in a later stage, with the play's marks and scenes relative to the stage's origin. |
| Q9 | Where do players find crawls? | A dungeon board in each city's inn: the top 16 dungeons by rank, each with name, author, ticket price, rating and run count. |
| Q10 | Where is the entrance? | A crawl door the author places inside a house the author holds (`UOAIX.md`, "Houses come from deeds"). A runner double-clicks the door, pays the ticket, and the run starts. |

## The loop

1. **Charter.** A player buys a dungeon charter. The charter names one new dungeon and holds one tier-1 spawner.
2. **Build.** The author opens a build session: the dungeon stands on a free stage, the author stands at the stage's
   entry mark, and the decorator's placement commands (`UoaixDecorator.md`) work inside the stage for the author. The
   author places walls, floors, light, a multi (`UoaixMultis.md`), spawners, loot chests and the exit. Leaving ends the
   session and takes the dungeon off the stage.
3. **Sell.** The author sets a ticket price in coin and places the crawl door in the author's house.
4. **Run.** A party of 1 to 6 players pays at the crawl door. The dungeon stands on a free stage, the spawners arm, and
   the party stands at the entry mark. The run ends when no living runner remains in the stage or at the run's time limit.
5. **Rate.** Each runner rates the run at the end; the inn boards rank the dungeons.

## Stages

A stage is a rectangle of void map east of x 5120 with no land art except void (0x244) and no statics, at least 24 tiles
from any other land, static or stage. The client's update range is 18 tiles, therefore no player outside a stage sees
anything inside the stage, and no player inside sees anything outside.

Measured 2026-10-18 on `D:/Games/UO1998com-Client1` (`MAP0.MUL`, `STAIDX0.MUL`): 8965 blocks of 8 by 8 tiles east of
x 5120 hold only void land and no statics. A greedy fit of square stages, each with 24 tiles of void around the stage:

| stage side | stages |
|---|---|
| 64 tiles | 8 |
| 48 tiles | 14 |
| 32 tiles | 32 |

The lean is 48 tiles. A stage's floor is baked once into `land.cfg` as a walkable dungeon-floor art at z 0, through the
decorator's land bake, therefore the client walks the stage before anything stands on the stage. Walls, doors and every
other piece of a dungeon are world items.

A dungeon is data, not a place: the dungeon's members are stored with x and y relative to a stage's origin. Standing a
dungeon on a stage places every member as a world item at the stage's origin plus the member's offset, as
`sty-switch` places a story's set (`PlayStories.codex`); taking the dungeon down removes every member. A dungeon on no
stage holds no world item, and a dungeon stands on at most one stage at a time: one run or one build session per
dungeon.

## Bounds

The ruling (`uoaix-init.md`, Damian 2026-10-18): every message and every world walk is bounded to a small region.

- **Messages.** A stage's speech, combat, item and mobile packets reach only the sessions inside that stage, by the
  24-tile isolation above and the existing view range. Nothing in a run is broadcast to every session.
- **Walks.** Each stage owns one walk map, allocated at boot below the serving mark, laid from the standing dungeon's
  colliding members when the dungeon stands. A player inside a stage moves on the stage's map, and `wmc-cover` does not
  page for that player; a stage's creatures walk on the same map. The RAM cost is one 64-square paged window per stage.
- **Ticks.** A stage holds one wheel timer kind (`WorldTimers`), keyed by stage, for the stage's spawners and the run
  clock. A stage with no run and no build session holds no timer and costs no CPU. A running stage costs, per deadline,
  one pass over the dungeon's spawners (at most 16) and nothing per session.
- **Creatures.** A dungeon creature is a summon in the world's one spawn controller (`cg-spawn-attach`, whose hooks sit
  on the shared `GameCombat`): `gws-summon` with owner 0 and the run's end as its expiry, counted against no region's
  population, leaving no corpse (`vanish-hook`) and leaving the world at the run's end. A stage holds at most 32 live
  creatures; the controller's slots grow by `gws-grow`. The controller walks every creature on one map today, therefore
  stage 4 picks the stage's map for a creature standing in a stage.
- **Storage.** A dungeon holds at most 2048 members, a deliberate content cap. The dungeon registry and the member
  table grow with the number of dungeons (`ServerLimits.md`); `PlayStories`'s fixed caps (64 stories, 256 sets, 4096
  members) do not hold dungeons.
- **Concurrency.** The stage count is a window cap: at most 14 runs and build sessions at once. A party at a crawl door
  with no free stage is told "Every crawl hall is in use." once, and the door does nothing more (L-FUTILITY).
- **Ratings.** Each dungeon keeps the rating sum and the rating count; a rating changes both in O(1), and the board's
  top 16 is a sorted list of 16 rows updated in O(16) on each rating.

## Spawners

A spawner is a member of kind spawner: a profile from `WorldSpawnData`, a live cap (1 to 8), and a birth interval in
game seconds. A dungeon holds at most 16 spawners. The spawner's tier bounds the profiles the spawner accepts (tier 1: the creatures of Britain's cemetery
and the orc fort; the tier table is Q4's ruling). The dungeon's challenge budget is the sum of the spawners' live caps,
at most 32. A spawner is an item in the author's pack until placed, and returns to the pack when removed.

At a run's start each spawner births up to its live cap; after the start a spawner births one creature per interval
as long as the spawner holds fewer than the live cap. A creature killed during the run stays dead for the run's remaining
time when the author marks the spawner `once`. A creature walks only inside the stage's map.

## Runs

- **Entry.** A runner double-clicks the crawl door: a gump names the dungeon, the ticket price, the run's time limit
  and the party (every player within 3 tiles who says "join"). Pay takes the ticket price from each runner's purse to the
  author's purse (the `cg-play-coin` path) and starts the run.
- **The run.** The dungeon stands on a free stage, the spawners arm, and each runner stands at the entry mark. The
  author sets the time limit in game minutes, at most 720 (one real hour at the default day of 2 real hours, where a
  game minute is 5 real seconds).
- **Goal.** The author marks one spawner as the goal. Killing the goal spawner's last creature marks the run finished.
- **End.** A runner who uses the exit leaves the run. The run ends when no living runner remains in the stage or when the
  time limit falls due. At the end every runner and every runner's corpse stands at the crawl door, the creatures leave,
  and the dungeon comes off the stage.
- **Loot.** A loot chest is a member whose contents are economy lots the author put in the chest during a build session.
  Between runs the contents are kept with the dungeon, not in the world. A runner who takes an item takes the lot from the
  author's stock.
- **Restart.** A server restart ends every run: each runner stands at the run's crawl door and is refunded the ticket.

## Persistence

One new UCC1 section, `DGN1`: per dungeon the author's account, the name, the ticket price, the time limit, the
members (art, hue, dx, dy, z, kind), the spawners, the loot chests' lots, the rating sum and count, the run count, and
the earned spawner count. Per running stage, one run row: the dungeon, the crawl door's position, and each runner's
serial and ticket paid, which a restart reads to refund the runners and stand them at the door. Creatures, timers and
the stage's walk map are not saved. Worlds are disposable: a format change
boots a fresh world.

## Build stages

| stage | built | grade |
|---|---|---|
| 0 | The stage table: 14 rectangles of 48 tiles from the measurement above, checked against spawn regions and teleporters; the floor lines in `land.cfg` | Damian walks a baked stage in the client |
| 1 | Dungeon registry and `DGN1`; the charter item; a build session on a stage; the decorator's place, move and remove confined to the author's stage, stored relative | BVT: a member placed in a build session stands at the same offset on another stage; a placement outside the author's stage is refused |
| 2 | The stage walk map: laid from colliding members, player walks inside a stage on the stage's map, no paging inside a stage | BVT: a wall member blocks a step; a step inside a stage leaves the paged window unmoved |
| 3 | Runs: crawl door, party, ticket payment, entry, exit, time limit, end, return to the door with corpses, restart refund | BVT: a run of one runner pays the author, ends at the time limit, and stands the runner at the door |
| 4 | Spawners: kinds, tiers, live caps, summons in the world controller walking on the stage's map, the goal | BVT: a spawner of cap 3 holds 3 creatures; the goal's last kill marks the run finished; the run's end leaves no creature |
| 5 | Loot chests: lots stocked in a build session, kept with the dungeon between runs, taken by runners | BVT: a lot taken by a runner leaves the author's stock and enters the runner's pack |
| 6 | Ratings and the inn boards | BVT: the author's own account cannot rate; a second rating from one account replaces the first |
| 7 | Earned spawners (Q2) | BVT: the tenth finished run by distinct accounts at an average rating of 3 or better grants one spawner |
| 8 | Plays inside a dungeon (Q8) | the sample play runs inside a stage at relative marks |
