# UOAIX Assist -- one harvest circuit, specialized by resource

Status: done (2026-10-23, the feather circuit's hunt, carve and ribs seen working by Damian). Owner: blu. Code:
`apps/uoaix/assist/`.

## The ask

Damian, 2026-10-23: "write up a plan in the docs for feather harvesting: kill a chicken or other non-tame bird, use a
blade, dump the food and feathers at a collection point with all the same harvest setup targeting, range controls etc.
abstract out the commonalities into common code, specialize for the differences."

## What the 1998 server says

- Birds that give feathers (rundir `bank/templatestable.dat`, `<resource production N 0 FEATHERS>`): forest bird (501),
  chicken (507), raven (509), eagle (512), tropical bird (525), magpie (570), crow (571), and the harpy (555, notoriety
  -125: a threat, never a target). A carve takes the corpse's whole bank of each resource: the chicken's template
  (`templatestable.dat` line 6890, MEAT 2, FEATHERS 25) gave 25 feathers and one raw ribs (stage 2).
- A bird with no notoriety in its template is not a threat (`Threat` in `AssistFeatures.Walking.cs`); the eagle is
  difficulty 2 and fights back.
- Corpses are 0x2006 (`CorpseArt` in `AssistFeatures.Trips.cs`); a corpse decays on the server's corpse timer (OUO
  `corpse.c`), not the 3-hour ground decay of `res.seen`.
- Our own shard's carve (`apps/uoaix/GameCarve.codex`, a Sphere port) puts the parts inside the corpse and answers with
  Sphere's DEFMSG_CARVE lines. That is a model, not uo1998: uo1998 runs rundir `scripts.wombat/bladed.m`, below.
- Feathers are 0x1BD1 and 0x1BD2 (`ResKind` kind 2); a carve's meat is raw ribs 0x09F1. Read every weight from the
  client's TILEDATA, never assume one.

## The carve on uo1998 (stage 2, Damian by hand, 2026-10-23, `record-2650-20261023-153050.log`)

Kwik killed a chicken (body 0xD0) with the battle axe in hand and carved it with the same axe.

| step | client | server |
|---|---|---|
| war mode | 0x72 on | 0x72 echo |
| attack | 0x05 the bird's serial | 0xAA the serial; one swing killed it (0xA1 hits 0 of 3), 2.1 s after the attack |
| death | | 0xAF bird serial, corpse serial; 0x1A the corpse (art 0x2006, amount the body id, the bird's tile); 0x89 and 0x3C with 0 items (the corpse is empty); 0x1D the bird; 0xAA 0 (the attack ended) |
| carve | 0x06 the blade | 0x1C system line "What do you want to use battle axe on?", then 0x6C a target cursor |
| target | 0x6C object, the corpse serial, its tile, art 0x2006 | 200 ms later: a blood splash (0x122A to 0x122F) on the corpse's tile; per resource an item made on the corpse's tile, then 0x25 into the pack (meat 0x09F1 amount 1, feathers 0x1BD1 amount 25), each followed by its system line; the corpse stays, empty |

The parts go straight into the pack (`giveItem`), never into the corpse, so nothing is lifted from the corpse. Every
answer `bladed.m` can give, in its order (system lines unless marked bark, a bark is spoken by the target):

- the use: "What do you want to use <blade name> on?" and a target cursor (line 157);
- the target farther than 3 tiles: "That is too far away."; out of sight: "You can't see that." (lines 167 to 173);
- a corpse with a `nameVar` (a person's corpse) is dismembered with a notoriety and karma loss (`Q4JH`, line 178):
  the carve never targets a corpse whose label is a person's name, only one whose label is a creature's;
- a live mobile: bark "But that's not dead!" (a sheep: its shearing lines) (line 188);
- not a corpse: bark "Use this on corpses to carve away meat and hide." (line 319);
- per resource with a bank above 0, in the order meat, leather, fur, feathers, cloth: "You carve away some meat.",
  "You skin the corpse and get the hide.", "You skin the corpse and get the fur.", "You pluck the bird and get the
  feathers.", "You shear the corpse and get the wool."; a full pack instead answers "You don't have anywhere to carry
  the <meat, hides, furs, feathers, wool>." and the item is put at the player's feet (lines 329 to 403);
- a corpse with no bank left: bark "You see nothing useful to carve from the corpse." (line 405).

## Common to every harvest (today inside the logger, to be lifted out)

Everything below is in `AssistFeatures.LumberjackCircuit.cs` and is resource-blind in substance:

| piece | today | becomes |
|---|---|---|
| the tool | `LumberAxe`, `LumberAxeNow`, `LumberEquip` | `HarvestTool`: a target cursor, an art test, equip into the hand it needs |
| the place | `LumberLogsTo` (tile, bag, pile, pack animal), `CollectPlace`, Reset collection point | `Gather.PlaceSpec`, `Gather.CollectPlace` (built); one per harvest, same buttons |
| at feet and the collection | `LumberAtFeet`, `LumberPiles`, `LumberCollect`, `PileTree`, `Bunnyhop`, `CarryPile`, `HopPile`, `LumberCollectAt` | `Gather.AtFeet`, `CollectAt` and the pile record (built); one collector over it per harvest, the art test passed in (logs, or feathers and meat) |
| the order | `LumberOval`, `LumberTree` (two foci, Range, bands of 8, walk plus 8 a band) | `HarvestOrder` over candidate spots, the spot source passed in |
| the starts | New chop (dump all but the tool), Resume (keep all, collect first when due) | New and Resume on every harvest |
| the threat watch | `Threats`, `Flee`, the tree choice skipping threats | unchanged, shared |
| the map | `LumberSpots`, `res.seen`, `ResourceSpots`, the show types | unchanged; a harvest adds its own spot kinds |
| the window rows | the Lumberjacking rows in the Circuit tab | one row set per harvest from one builder |

Each piece is generic once three things are parameters: the art the harvest drops and collects, the spot source, and the
act at a spot.

`AssistFeatures.Harvest.cs` already exists and is a different thing: the walk-past pickup of crops and the shearing of
sheep, whose blade-from-the-pack targeted on a mobile is the feather harvest's carve in miniature (reuse its blade finder
rather than write another). The new family is therefore named `Gather` (`Gather`, `LumberGather`, `FeatherGather`), not
`Harvest`, so the two do not collide.

## What differs

| | logs | feathers |
|---|---|---|
| spot source | tree statics in the oval (`LumberStatics`) | birds in view (mobiles, by body or name, never a pet of ours, never at notoriety -120 or lower), then their corpses |
| the act | chop the tree (`LumberChop`, `ChopSaid`) | attack (0x05) until the death (0xAF), then the blade on the corpse (0x06 the blade, target the corpse); the server puts the parts in the pack |
| tool | an axe | a blade (dagger, knife, cleaver, sword, axe: the TILEDATA name, as `IsAxe` reads names); the battle axe carved in stage 2 |
| the answers | "You put some logs", "not enough wood", ... | `bladed.m`'s lines (above) |
| what goes to feet | logs | feathers and meat; the meat dumped ("dump the food") at the place or at feet, as setup says |
| emptiness | 8x8 chunks found empty, the sentinel | none: a bird is a spawn; a region with no bird in view for a while is passed, not marked |
| respawn | the sentinel probe | on uo1998 a region revisited after a wait; `WorldSpawnData` only on our own shard |

## Stages

1. Lift the common pieces out of the logger into the `Gather` class family (`Gather`, `LumberGather`), no behaviour
   change: every existing Lumber and Collection case stays green, and the window keeps its rows. `AssistStructure.md` and
   `assist-csharp.md` rules apply. Built: the place, At my feet, Collect at and the pile record (`Gather.cs`,
   `LumberGather.cs`, `tests/GatherTests.cs`), under the logger's own setting keys; `AssistFeatures` reaches them through
   members of the old names (`LumberLogsTo`, `LumberAtFeet`, `LumberCollectAt`, `LumberPiles` and the pile edits,
   `CollectPlace`). The store and the collector take a `Gather` (`GatherStore`, `GatherCollect`, `HopPile`,
   `CarryPile`, `PileAt`; the gather names the art, the goods, the place's button and the weight), still in
   `AssistFeatures` because they reach the link through it until `AssistStructure.md` stage 4 gives them `ITravel`;
   `LumberStore` and `LumberCollect` call them with the `LumberGather`. The tool: `Gather.Tool`, `ToolName`, `IsTool`
   (the axe ranges in `LumberGather`), `GatherToolNow` and `GatherEquip`. The order: `GatherOrder` (`Oval` over a spot
   source, `Pick` by walk plus `Band` a band with a skip; `tests/GatherOrderTests.cs`), the logger's tree statics its
   spot source and its empty, unreached, refused and threatened trees its skip. The starts: `Gather.CollectDue` (Resume
   collects first when due) and `Gather.NewStart` (the piles forgotten, the place the player's tile); the sentinel, the
   chunks found empty, Chop from and the old drops stay the logger's. The window's row builder is stage 4's: it has one
   user until the Feathers rows exist, and the suites do not build the window.
2. Done: Damian's chicken, "The carve on uo1998" above.
3. Built (main 48259), not yet run live: `FeatherGather` (`FeatherGather.cs`) and the circuit
   (`AssistFeatures.FeatherCircuit.cs`, Feathers in the Circuit tab: Resume, New hunt, Blade, Feathers to, At my feet,
   Range, Leave a fight under, Collect at; control port `circuit feathers`, `feathers-setup`). The nearest bird in range
   of body 5, 6 or 0xD0 (the shard's spawn data: eagle; bird, canary, crow, falcon, hawk, raven, tropical bird; chicken),
   never a pet, a threat, an invulnerable one or one with a name not starting "a "/"an "; walked to, attacked once
   (0x05, `GameLink.Attack`), walked after when it moves off, its death read from 0xAF; a bird unreached, unanswered,
   gone from view or not dead by `kill-wait` is left for the run (L-FUTILITY); the player's hits under the setting end
   the circuit. The corpse carved with the blade (`bladed.m`'s arts, 24 ranges), every answer above read by `CarveSaid`.
   The feathers go to Feathers to, or at feet with the collection. Every cut of ribs in the pack goes back on the corpse
   after its carve (Damian, 2026-10-23: "have it dump those ribs back on the corpse"; uo1998 gives each cut its own
   serial, never stacked). A "You don't have anywhere to carry the ..." line ends the circuit only when the part named
   did not reach the pack: `bladed.m` asks `canHold` of a part its merge into the pack's stack deleted, so the line
   comes with the feathers in the pack (Damian, 2026-10-23: "it does go in pack, no actual error"). No recall on its walks.
   `tests/FeatherTests.cs`, 17 cases; coverage 87.2% of the circuit's lines (2026-10-23), the unhit lines its failure
   arms (a refused link send, a failed store or collection, the flee, a stop mid-fight).
4. Built (main 48278): `GatherSet` in `AssistWindow.cs` builds a gathering circuit's Set line (tool, place, At my feet,
   the place's words) for Lumberjacking and Feathers; the map shows feather piles (kind 2).

## Someone's pet

A pet's single-click label carries "(tame)" (Damian, 2026-10-23: "there is typically a (Tame) in the name for a
currently owned pet"), and a trained pet is often renamed so it can be released ("saying "a horse release" doesn't work,
whereas "horsey release" does"), and a release leaves the renamed bird wild beside the one that respawned ("the release
makes 2 exist where 1 used to"; "eventually you get a buildup of excess fauna"). The circuit looks at each bird once
before its fight and never attacks one labelled "(tame)" (`AssistState.Tame`); a renamed bird without it is hunted.
