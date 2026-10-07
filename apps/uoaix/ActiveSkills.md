# Classic active skills

The shard serves the installed 1.25 client with its 46-skill profile (Damian).
Meditation and Stealth are absent from that client's SKILLS.IDX/MUL and are
not implemented, and no server command replaces them. Mana regenerates
passively.

## What exists

| Chapter | Contents |
|---|---|
| ClassicSkills | 46 fixed-point levels per actor, the Source-X bell/S success curve, separate deterministic success and gain rolls, an attempt counter |
| ActiveSkillsState | five actor records keyed by character slot, the composite-supplied `AsServices` record, a concealment byte per world slot, a calm deadline per world slot, cursor contexts `#4153xxxx` |
| ActiveSkillsLore | Anatomy, Animal Lore, Item Identification, Arms Lore, Evaluating Intelligence |
| ActiveSkillsHiding | Hiding, Detect Hidden, reveal on admitted actions (`as-after`) |
| ActiveSkillsHealing | bandage Healing, and Veterinary on an animal: cure, heal, resurrect |
| ActiveSkillsTown | Snooping, Peacemaking, Provocation (two cursors; a spawned creature's TOPROV from `ProvokeData`, through `AsServices.provoke`), Enticement (two cursors; `AsServices.lure` sets a spawn slot's `follow`, which `GameWorldSpawn` walks) |
| ActiveSkillsPoisoning | Poisoning: a poison potion (`GameAlchemy` nightshade, 0x0F0A) coats a sword or fencing weapon (item health strength * 256 + charges); `GameCombat` spends the charges on landed blows through `AsServices.envenom` |
| ActiveSkillsStealing | Stealing from an NPC's backpack, random or targeted (weight against skill, worn armor; no thieves' guild or witnesses); a stolen economy lot passes to the thief by the theft transfer (`EconomyProduction.md`), and a stolen coin pile moves purse to purse (`eu-steal`, `EconomyCurrency.md`). Britain's residents carry backpacks of their trade's goods (`tl-pack`); shopkeepers and bankers are immune |
| ActiveSkillsForensics | Forensic Evaluation: a corpse's killer and death tick (`GameCombat.slain`), its last looter (`GsiState.looted`), a container's last picker (`ActiveSkills.picks`, written by `GameLocks`); Spirit Speak: a window (`ActiveSkills.spirit`) in which `CompositeEvents` delivers ghost speech clear |
| ActiveSkillsTracking | Tracking menus (`0x7C`/`0x7D`, menu ids `#9000..#9FFF`) and the quest arrow (`0xBA`) |
| ActiveSkillsTaming | Animal Taming: the cursor, three timed attempts, the master binding through `AsServices` |
| ActiveSkillsHandler | packet routing and the pulse |
| ActiveSkillsCodec | ASC1 version 2: seeds, counters and cooldowns (levels live in the skill table) |

`proofs/ActiveSkillsReplay.codex` has 118 arms (2026-10-06). Compile it with an explicit
`-Kernel seed/Codex.cdx` and a mandatory `-Log`, run it through
`build/test-run.ps1`, then compare the complete output to
`proofs/ActiveSkillsReplay.expected`.

Every admitted use rolls success and gain through `skc-use`. Each of the
following refuses before any roll, and none of them practices, paces or
spends a resource:

- an invalid target
- a cancelled, replayed or expired cursor
- a missing tool or instrument
- an item held on the cursor

Anatomy and Evaluating Intelligence answer in qualitative tiers, with an
error that shrinks as skill rises (ServUO margins: 25 minus skill/4, and 20
minus skill/5). Animal Lore on a player refuses with "People are capable of
more than animal behavior. Usually." Arms Lore admits catalog items 40, 41,
44, 48, 69, 70 and 71.

Gain: 1000 per skill and 7000 in total (blu, 2026-10-06). The chance per use
follows Source-X Skill_Experience: 10, 200 and 800 uses per 0.1 at 0, 50.0
and 100.0 skill, interpolated, and no gain when the difficulty plus 200 is
below the level. At the 7000 total a gain lowers a random other skill above
0 by 0.1, as the 1998 shard did (root, 2026-10-06): the client has no lock
arrows.

Gain curve (Damian, 2026-10-06): "skill gains need to be logarithmic. big
gains early, slow gains late. also early, you gain on both success and fail,
whereas by 50 skill, it should be mostly you successes on "harder things". it
should not be skills econonmy where player population affects skill gain
rates. every character has the same bar." Built in `SkillCurve.codex`: skill is the
log of uses, so the uses per 0.1 are 10 * 80^(level/1000), 10 at 0.0 and 800 at
100.0; below 50.0 a failure gains a normal gain; from 50.0 a failure gains a
tenth of one, and a success gains fully on a difficulty within 10.0 below the
level or above it, falling to nothing 20.0 below. The chance depends only on the
character's level, the difficulty and the outcome, never on a shard-wide count.
Scope: every character, player and NPC, on every practice path: ClassicSkills'
active skills, the fighter's swings (the hit is the success) and the economy's
`ep-practice` (harvest and craft, the level as the difficulty).
`proofs/SkillCurveProof` is the per-level arm.

Success bonus (Damian, 2026-10-06): "we should give big bumps for success here. catching a fish should be like 10x a normal skill gain, tapering off after 50 is achieved. 0-50 should be doable in a 8 hour gaming session starting from 0 and doing all the work at the water". Root's reading: below 50.0 a successful use gains ten times a normal gain, the bonus tapers to 1x between 50.0 and 100.0, and the curve is tuned so that one player using one gathering skill continuously, at the pace the server allows, goes from 0.0 to 50.0 within 4 hours of play (Damian, same day: "actually lets make it 4 hours. after work, first session, you should be able to catch fish pretty good. bigger hauls (kracken) will be for the GMs"); the same rule covers every skill (one bar for every character). Built as `skc-bonus` in `SkillCurve.codex`, applied in `skc-gain-at` on every practice path: a success gains 10x a normal gain below 50.0, tapering linearly to 1x at 100.0 (5.5x at 75.0). `proofs/SkillCurveProof` replays 4,800 casts through `ep-practice` (4 hours at the harvest adapter's 3 s cooldown) and asserts the fisher reaches 50.0; it does at cast 4,215 (2026-10-06), and at 1x the same casts reach 25.7. The biggest catches (sea serpents, the kraken) are reserved for grandmaster fishers.

Farming replaces Begging (Damian, 2026-10-06: "yeah lets just replace begging skill. that isn't the actions our players need to get good at. make it farming."). Client skill 6 is Farming: the client's SKILLS.MUL entry is renamed in the shard's client install, sowing, tending and reaping crops roll and gain skill 6 in the one skill store, and the begging action is removed.

## One skill store

Every character's 46 levels live in one row of the shard's skill table (`CharacterSkills`, `GameShard.skills`, 320 rows), keyed by serial, claimed and seeded from the creation skills on first use, and persisted as UCC1 version 23 (an older world is refused: worlds are disposable). `gs-skill` is the one read and `gs-skill-set` the one write (`[skill`). A session's ClassicSkills levels are the row; combat (`cb-level`), Magery (`mgs-skill`), the 0x3A list (`gs-skills`) and name labels read it; player swings (`cb-practise`) and casts (`mgr-cast-skill`) gain in it. A player's economy actor is bound to its row (`gh-row`) and practises the mapped client skill there (`ep-classic`); an NPC economy actor (which rolls and gains every category as before), a monster and the town fighter keep their own single store.

Economy categories and the client skill they practise: mining and gold mining Mining (45), fishing Fishing (18), farming Farming (6, which replaces Begging, F55: sow, tend and reap roll it), lumber Lumberjacking (44) when harvested, Carpentry (11) when crafted and Bowcraft (8) for bows and arrows, smelting Mining (45), smithing Blacksmithy (7), cooking Cooking (13), textile and leather Tailoring (34). No client skill, so a player's use always succeeds and gains nothing (root, 2026-10-06, for Damian to map later): hunting (carving), herbalism (reagent picking), pottery and minting. `proofs/SkillStoreProof` and the Magery arms of `proofs/MageryReplay` grade it.

## Animal Taming

UOX3 `taming.js` rules. Skill 35 opens a "Tame which animal?" cursor. The target must be a wild world spawn whose
UOX3 `TOTAME` (tenths of a skill point, `WsProfile.taming`; a horse 291, a bear 459, a sheep 111) the tamer's level
meets, within 3 tiles; a profile with no `TOTAME` cannot be tamed, and one above 1000 can never be. Every 3 seconds the
tamer says one of four lines and rolls 35 against `TOTAME`, three times at most; the attempt stops when the creature
is more than 8 tiles away, the tamer dies or another tamer takes it. On success the creature says "It seems to accept
you as master.", drops its fight, leaves its region's population and follows.

A pet is a spawn slot with an owner and an order (`gws-pet`). Its owner saying "all follow", "all guard" or "all stay"
(or "all stop") orders every pet it owns (`cg-pet-orders`, heard before NPC speech, which still hears the line).
Follow walks to within one tile at 0.3 s a step; guard follows and fights the owner's combat target or whoever is
fighting the owner; stay holds the tile. A pet keeps a target it is already fighting and never targets its owner.
UWS1 version 2 persists owner and order. `proofs/CompositeTamingProof` grades taming, the three orders and the
restart (two boots on one disk), red with pets acting wild or the owner unsaved.

## Veterinary

UOX3 `healing.js`: a bandage applied to an animal (the `animal` service: a mobile that is no character)
rolls and gains Veterinary (39) and Animal Lore (2) where a person takes Healing (17) and Anatomy (1), at the same
thresholds (cure 60.0/60.0, resurrect 80.0/80.0), amounts and delays. `proofs/ActiveSkillsReplay` grades it at Healing
100.0 and Veterinary 0 (the cure fails) and at Veterinary 100.0 (it cures); the first arm turns red with the skill
choice removed.

## Camping

Kindling is made with a blade (UOAIX-70; ServUO `BladedItemTarget.cs`, `GameCarve.codex`): a knife or dagger's
cursor on a tree within 2 tiles (a tree static on the map or a tree in the world) puts one kindling in the backpack,
joining a kindling stack that has no economy lot; on the carver's own fallen branches, logs or boards it spends one
unit through the lot and gives one kindling. Anything else answers "You can't use a bladed item on that!". ServUO
spends 5 wood from the tree's harvest bank; a tree here is not spent, and each cut paces the blade 1 s.
`proofs/GameCarveReplay` grades the tree, the stack, reach, a rock and a log.

ServUO `Kindling.cs` and `Campfire.cs` (`Campfires.codex`). Double-clicking kindling (0x0DE1) lying on the ground
within 2 tiles rolls Camping (10) at an even chance per skill point; success spends one (through its economy lot when it
has one) and lights a non-movable campfire (0x0DE3) on the kindling's own tile. Refused: kindling in a container ("Set
the kindling down on the ground to light it.", Damian 2026-10-07), out of reach, a dungeon (x 5120 and east), 16 fires
already lit. The fire
smoulders (0x0DE9) at 60 s, goes out (0x0DEA) at 90 s and is removed at 100 s (timer kind 9). A player within 7 tiles
of a lit fire is told the camp is securing, and 30 s later that it is secure; a logout from a secure camp leaves the
world at once instead of lingering 300 s (`cg-depart`). Fires and camps are not saved: the first pulse after a boot
removes every campfire an earlier run saved. `proofs/CompositeCampingProof` grades it (two boots), red with the
secure logout unbound.

## Cartography

ServUO `LocalMap.cs`, `MapItem.cs` and `DefCartography.cs` (`Cartography.codex`). Double-clicking a blank map (0x14EC)
in the backpack with a mapmaker's pen (0x0FBF or 0x0FC0, the scribe's pen art) also carried rolls Cartography (12)
against a difficulty between 10.0 and 70.0. Success spends the blank map and draws a local map (0x14EB) of the square
64 + 2 x skill tiles each way around the cartographer (264 at 100.0); a failure spends nothing. Double-clicking the
drawn map sends MapDetails (0x90): gump 0x139D, the bounds clamped to the map, 200 by 200. The map's centre and reach
live in Magery's item definitions (`mgs-chart`: x, y, and the reach in z), saved by MGC1 as flag 16, so drawing needs
Magery attached, as the server attaches it at boot. The skills window's Cartography button (skill 12) draws with the
first blank map in the backpack as a double-click on it would, and without a blank map and a pen answers "You need a
blank map and a mapmaker's pen in your backpack to draw a map." (UOAIX-71). `proofs/CompositeCartographyProof` grades
it (two boots), red across a restart with the flag unsaved.

## Taste Identification

ServUO `TasteID.cs` (`ActiveSkillsTaste.codex`). Skill 36 asks "What would you like to taste?" and targets within 2
tiles (or the taster's own pack). Food, an item whose economy lot is a food (`ep-food`), rolls 36 at an even chance per
skill point: success finds nothing unusual, because no food here carries poison, and failure discerns nothing. A potion
(bottle art 0x0F06 to 0x0F0E) is already known and named (`item-name`) with no roll. A mobile is inappropriate; anything
else cannot be tasted. `proofs/ActiveSkillsReplay` grades it, red on the food arm with food recognition removed.

## Herding

UOX3 `herding.js` (`ActiveSkillsHerding.codex`; the crook is fester's slice A). Double-clicking a shepherd's crook
(0x0E81, 0x0E82) or crook (0x13F4, 0x13F5) in the pack asks for an untamed creature with a TOTAME within 7 tiles,
then a place or the herder. Herding (20) below the creature's TOTAME cannot persuade it; otherwise a roll of 20
against TOTAME sends it walking to the place at chase pace, 20 steps at most (`gws-herd`, the slot's goal), or
following the herder (`gws-lure`). `proofs/ActiveSkillsReplay` grades the cursor flow and `proofs/GameWorldSpawnReplay`
the walk (a herded deer reaches its place; red with the goal branch removed).

## Resource rules

- A bandage is a Britain-catalog item 75 lot carried in the healer's own
  backpack, never in bank custody. Finishing an application deletes or
  reduces the world stack and debits exactly one unit's stock, quality, wear
  and basis. It also counts the unit in `consumed`. A bandage with no lot is
  not claimed.
- A successful snoop sends the container gump and its contents and nothing
  more. Lifting from or dropping into the pack is still refused by
  `ia-access`. The snoop never touches bank custody.
- Peacemaking requires a carried or held instrument with graphic `#0EB1`,
  `#0EB2`, `#0EB3`, `#0EB4`, `#0E9C`, `#0E9D` or `#0E9E`. The catalog holds no
  instrument item.

## Composite binding (fester)

All eight steps are bound in `CompositeGameRules` (`cg-as-services`,
`cg-admit`) and graded by `proofs/CompositeActiveSkills.codex`. `resurrect`
and `snoop` answer False there: no patient-side redraw exists, and a
single-player composite has no client to notify.

1. Construct the state with `as-new owner.vendor owner.banks owner.combat`.
2. Route `as-handler services state` before `gsi-handle` and before the
   harvest handler. The handler claims these packets:
   - `0x12`/`0x24` for skills 1, 2, 3, 4, 9, 14, 15, 16, 19, 21, 22, 28, 35, 36 and 38, and 0x06 on a crook
   - `0x34` skill queries
   - `0x06` on an item-75 lot
   - `0x6C` contexts `#4153xxxx`
   - `0x7D` menu ids `#9000..#9FFF`

   Every other packet answers `Ok None`. Merge `as-specs 0` into `cg-specs`: `0x7D` has no frame in the composite's spec lists today.
3. Call `as-pulse` from `cg-pulse`. It finishes bandage timers and moves the
   tracking arrow.
4. Pass every routed reply through `as-after`. A hidden player is revealed
   by an admitted step (`0x22`), an attack (`0xAA`) or a lift.
5. Call `gcv-conceal owner.view state.concealed` once.
6. `GameCombat` `struck-hook` calls `as-reveal-serial` when combat or spell
   damage lands, and `admit-hook` (`cg-admit`) refuses, in `cb-select` and
   Magery's harmful aim, a target the attacker cannot see and any pair in
   which either side is calm.
7. ASC1 persists in UCC1 (`CompositeCodec` `cc-write-skills`,
   `cc-read-skills`). An actor row is valid when its serial is in that slot of the bound
   characters or of its own account record (`asc-bound`), so another account's login commits.
8. Bind `AsServices` as follows:

   | Service | Binding |
   |---|---|
   | `light` | light-source graphics |
   | `animal` | animal bodies |
   | `item-name` | `gsi-name` |
   | `intelligence` | the character or NPC intelligence, -1 when unknown |
   | `poison` | Magery `MgActor` poison, -1 when none |
   | `cure` | Magery `MgActor` poison |
   | `resurrect` | GameResurrection |
   | `snoop` | the owner notice and notoriety |
   | `taming`, `master`, `tame`, `pet-name` | the spawn controller: `gws-taming`, `gws-owner-of`, `gws-tame`, `gws-name` |

   Cure and resurrect bind the current private action candidate and never
   mutate a separately published state. `gr-raise` answers a `GameReply`
   addressed to the resurrector's connection, so `resurrect` needs a
   patient-side redraw before it can wrap `gr-raise`.

Hiding and tracking reach other viewers only through their own connections.
A detected or revealed player whose client is elsewhere sees the change on
that client's next view pass.

## Open

- Blu has no active-gain seam in Magery: `mgr-cast-skill` checks success only.
  Bind `skc-use` there together with blu.
- Red's `GameLiveActions*`/`TownNetworkLive*` live-stage review is assigned to
  val. `TownLive.md` and `GameVendor.md` own those contracts.

## References

Source-X commit `dd28a0ad53258adb55b548b1bd4df989065a1fe4`, Apache-2.0,
under `D:/Projects/uo-reference/Source-X`: `src/common/CExpression.cpp`
(Calc_GetBellCurve/Calc_GetSCurve), `src/game/chars/CCharSkill.cpp` and
`src/game/clients/CClientTarg.cpp`. ServUO `Scripts/Skills/*.cs` and
`Items/Skill Items/Misc/Bandage.cs` supply the skill-scaled error shapes,
ranges, delays and thresholds.
Ports target the old ASCII client, not cliloc or gumps.
