# Live Britain residents

`TownLive` binds three layer 1 residents to real world mobiles: Nell the baker,
Jorin the smith and Mira the tailor. Shopkeepers remain at their shops.
Residents follow the existing persona schedules between shared lodging,
workplaces and the Blue Boar. Nell works at Good Eats, Jorin at the Hammer
And Anvil, and Mira at the Lords Clothiers. A work tile is a free tile within two of the
resident's station (`wks-trade-spot`, `WorkStations.codex`): the Good Eats oven, the anvil beside
the forge, the Lords Clothiers loom; `tl-resettle` re-places a saved work tile that is not.

Sweet Dreams Inn supplies one shared building assignment, a common entrance
and three distinct sleeping tiles. The initializer accepts a keeper capacity
of 3 through 128; this bounded installation adds exactly three residents.
The initializer refuses insufficient capacity. Construction and expansion
of the resident set are outside this adapter.

## Composite entry

The composite populates on a fresh world only (`cg-britain`, CompositeGame.md)
and persists TLC1 in UCC1; `proofs/CompositeBritainWorld.codex` grades the
fresh build, walking, naming, greeting and the restart. Each composite pulse
with a player in game advances the composite town clock (12 game seconds per
real second, persisted beside TLC1), calls `tl-advance` with budget 8 over the
Britain map, then `tl-pulse`; `tl-handler` precedes the other 09 handlers.
Movement persists with the composite's timed save, like player walking.

After `bb-populate`, on a fresh private installation candidate:

```text
tl-populate vendors items map (Just decoration) gameSeconds homeCapacity
  -> Result TownLive Text
```

The cached map must cover the Britain shop window in `BritainShops.md`.
Each resident gets a zero-coin economy actor. Ordinary harvest and craft
operations produce the three outfit pieces; world serials and GSI equipment
metadata render the outfits. No loose stock or purse grants are added.
The initializer reserves home tiles, then places residents at work so the
first midnight schedule walks visibly toward home. Discard the entire
candidate on initialization failure. Do not populate again on restore.

Route `tl-handler state` before generic name handling. The handler claims
only these residents' fixed-five-byte 09 requests; reuse the owner's existing
09 framing declaration. No additional client opcode is introduced.

Call `tl-advance state map (Just decoration) gameSeconds budget` once per
owner simulation update, with budget 1 through 64. The owner supplies
monotonic game seconds from the authoritative committed clock, not raw PIT
ticks. `tl-game-seconds clock committedHour` converts the existing `GameClock`
fraction after hour draining; invalid clock fields return -1. Changing the
day-length setting rescales real-time movement through the same game clock.
Movement takes 12 game seconds per adjacent tile; blocked paths retry after
60 game seconds. Catch-up remains bounded by the caller's budget.

`tl-pulse state` is the per-viewer approach-speech callback with the standard
GameDispatch pulse signature. Bind the pulse after advancement. A living
player entering a resident's three-tile radius receives one short canned
line. The persona stub logs the model-unavailable fallback. The ten-game-minute
per-player cooldown survives reconnect and restore. Persona call and audit
budgets also apply; exhausting either suppresses further greetings.

GameClientView owns mobile and equipped-item visibility. Feed movement's
77 direction hints through the composite's usual view processing. Both
movement and speech can mutate durable state; commit before publishing
replies. The adapter performs no raw network sends.

## What a player sees (`TownLiveShow`)

The composite calls `tls-show` after `tl-pulse` (`cg-town-show`) and appends
`tls-status` to a resident's 09 reply (`cg-town-status`). For a viewer within
18 tiles:

- a click answers the resident's goal: busy at or on the way to work, at or
  off to the Blue Boar, resting at or heading home to the Sweet Dreams Inn;
- a resident standing on its work tile during work shows it once per 60 game
  seconds: Jorin swings (0x6E action 9) with the anvil sound 0x2A, Mira plays
  the tailoring sound 0x248, Nell calls out;
- completed purchases, sales and meals in the TownNetworkLive audit book are
  said overhead, at most 3 lines per pulse, read newest first by sequence
  because `cg-rotate` moves rows. A viewer starts at the book's current
  sequence, so the backlog is not replayed.

The 160-byte viewer table (`CompositeGame.shows`) is volatile and is not
persisted.

## Movement and persistence

The path search uses Chebyshev A*, a fixed heap, at most 8192 expansions and
256 saved directions (Jorin's forge-to-Blue Boar route takes 6549 on the real
map; a failed search costs about 100 ms of pulse; measured 2026-10-06). A step
costs 8 and a change of direction 1 more, so among equally short paths the
walk keeps its heading (a diagonal run, then a straight run) instead of a
staircase. Once the decoration overlay
is laid on the town map (`cg-town-doors`), any home, work or tavern spot and the
lodging entrance without a standing surface is re-placed in its region
(`tl-resettle`). Living-mobile collision uses the world tile index.
Each actual adjacent step rechecks terrain, decorations and occupancy.
An unreachable or occupied target causes a retry, never a teleport.

Shared routes (`TownRoutes.codex`) are a flyweight over the same planner: the town's `TlPath` holds
64 routes, each its endpoints (from x, y, z, to x, y, z), step count, generation and 256 step bytes,
and a walker holds only a `RteWalk` (route id, generation, step, the tile it expects to stand on, and
its back-off). `rte-toward` reuses the route whose endpoints match the walker's tile and target, plans
and stores one otherwise (a full table takes its oldest slot and bumps the generation, so a walker
holding the old id replans), retries a blocked step up to 8 times before replanning from where it stands,
and never stores a failed plan. Routes are not persisted. Every NPC walker uses them: residents, guards,
the blacksmith, the gatherer and miners on the town planner, the fighter and British on their own route-window
planners. TLC1 and the guards' record keep their 256-byte path slots and step counters, written as zeros and
ignored on load, so a saved world of any earlier version loads and its walkers replan.
The search retains one height per map cell and is intended for ground-floor
town routes, not stacked-floor routing.

Residents pass through the same selected unlocked lodging entrance.
An unlocked closed door may be opened through WorldDecoration. Search probes
temporarily rebuild the two door cells and restore all affected bytes before
returning. Run probes only within the single-owner simulation turn. Locked
doors refuse passage. `None` decoration is for a map without imported doors,
including the synthetic replay; the real composite supplies decoration state.

```text
tlc-encode state map buffer capacity -> Result Integer Text
tlc-decode buffer size vendors items map gameSeconds -> Result TownLive Text
tlc-bytes -> Integer
```

TLC1 contains the existing townsfolk/persona body, home capacity, entrance,
resident actor and garment bindings, route cursors, game-time deadlines and
speech cooldowns. Header version is 1; total size is `tlc-bytes`. The checksum
detects corruption; structure and shared-world bindings are validated too.
Decode allocates a fresh search workspace and resets connection/view masks.
The supplied game time must not precede the stored townsfolk hour.

Save TLC1 together with world, currency, GSI, vendor and WDS1 decoration
components in one encompassing transaction. Restore those shared components
before TLC1. Door opening must not be committed separately from movement.
A late error requires discarding the encompassing candidate. Cached paths
are hints and are checked again after restore. The existing layer 1 work and
market mutators are not invoked: the modern economy remains the sole owner
of stock and money. General needs, births and production scheduling are not
advanced by this movement adapter.

## Reference and verification

Movement and first-contact speech shape follow Source-X commit
`dd28a0ad53258adb55b548b1bd4df989065a1fe4`, `CPathFinder.cpp` and
`CCharNPCAct.cpp` (`NPC_WalkToPoint`, `NPC_OnHear`), Apache-2.0.
`Sphere-LICENSE.txt` retains attribution. Lodging and tavern regions follow
UOX3 commit `4560ae841bac898817143d7aa95ce59f47ab98e0`,
`spawn_felucca_town_britain.dfn`, regions 9 and 50; the retained license is
`ports/UOX3-LICENSE.txt`.

`proofs/TownLiveProof.codex` exercises production-backed outfits, home/work/
tavern schedules, step budgets, occupied destinations, speech suppression and
fallback logging, door probes, clock rescaling and TLC1 recovery/refusal.
The replay uses a synthetic walk map. Actual map reachability, visible dress,
speech and movement are graded by root in fester's complete composite.

Retained navigation memory is 48 bytes per cached map cell plus three
256-byte paths, fixed resident/viewer records and existing bounded Tf/Tm
tables. Search costs O(E log C) heap work for E <= 4096 and C map cells,
plus eight collision probes per expansion. Tile probes visit only indexed
occupants; door lookup uses the sorted decoration x range. A door probe
includes WorldDecoration's cell rebuild cost, which can scan the decoration
table. Search scratch is reclaimed per neighbor. The owner reclaims returned
packet and ordinary action scratch after commit. Codec validation includes
the existing world, persona and currency validators; no full validation is
added to each movement step. No compiler or seed behavior changes.
