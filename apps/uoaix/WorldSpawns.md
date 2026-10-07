# Live region spawns

GameWorldSpawn adapts UOX3 region populations and NPC tables to the shared
GameCombat world. Sphere Source-X supplies bounded movement and obstacle
turning. This unit creates real world mobiles with persistent combat profiles;
it creates no inventory, equipment or gold. The larger living-wilds population,
breeding, food, overflow and raids design remains in UOAIX.md.

## Composite contract

Construct `wsd-catalog 0` once, then `gws-new catalog combat slotBudget` before
the request arena. The budget is1..256 and must be smaller than combat/world
capacity. Keep the same shared GameCombat used by the other combat wrappers.
The generated catalog is trusted server configuration, never client input.

Call `gws-after terrain state shard input map tick prior` after the composite's
single combat pulse, before commit and view reconciliation. `prior` is that
pulse's Result/Maybe GameReply. The adapter adds movement, spawn and removal
packets, preserving a prior reply's close flag. It does not call combat again
or send raw packets. Existing GameCombat handlers own attack and status input;
there is no new client opcode. Admission requires the active authenticated
session and an indexed WorldTable. The composite owns rollback, commit,
publication and reclamation of request scratch.

`terrain map region profile x y -> Result Integer Text` is a trusted placement
predicate. Return a valid signed-byte z only after checking installed terrain,
clearance, region fixed/preferred height and ONLYOUTSIDE/building rules. An Err
consumes a bounded placement attempt. Coordinates are chosen by the server
within the intersection of the region and loaded WalkMap, excluding the final
row/column required for four-corner land height. Region exclusions and indexed
mobile occupancy are checked before the predicate. The supplied `gws-ground`
handles walking land and fixed/preferred height; it refuses ONLYOUTSIDE because
WalkMap alone does not establish building membership. Fester owns the live
installed-world predicate. Water-only profiles are not admitted by this land
controller; amphibious profiles may use land.

The pulse checks16 region records per second with a persistent round-robin
cursor. A due intersecting region attempts at most min(CALL,16) spawns, eight
placement tries each, bounded by MAXNPCS, global slots and available world
capacity. Regions outside the loaded window retain their deadlines without
spawning. Newly created or restored regions can wait for the cursor to reach
them. Intervals use the source MINTIME/MAXTIME minutes, choosing uniformly in
seconds and converting to raw PIT ticks; a zero interval has a one-second
floor. Dead/deleted serials release population before the next spawn pass.
No distant actor is deleted merely to make space for a newly visited region.

AI2/11 attack a living player within 10 tiles (UOX3 MAXNPCAGGRORANGE) on the
first pulse after the monster has been drawn to that player, ahead of the wander
timer, and play the creature's start-attack sound; a hidden British is never
chosen. AI12 moves away from a player within 6 tiles; AI0/6 wander without
initiating attacks. A monster with a target chases it at 0.3 s a step (UOX3
NPCRUNNINGSPEED) wherever the walk map allows, and drops it beyond 18 tiles.
Wandering stays within the region rectangle and exclusions; a monster outside
its region walks back toward the region's centre. All walking uses the tile
index for occupancy, and a saved monster can stand outside its region.
An existing valid combat target is retained, including neutral retaliation;
the scared-animal mode cancels combat and flees instead.
Casters currently share hostile melee targeting; spells, hunger and race
relations require their owning adapters. Other AI modes and profiles with
zero minimum DEX/damage/HP are refused rather than assigned invented stats.
Source NPC loot/equipment/script tags are not imported into this controller.

## Recovery

`gwsc-size state` is 64 + 8*regionCount + 64*slotCount bytes.
`gwsc-encode state world now buffer capacity` and
`gwsc-decode state world now buffer size` return Result Integer Text.
UWS1 version 3 stores the generated catalog revision, dimensions, scheduling
cursor, remaining region/pulse timers and each live serial's region/profile/facing,
remaining movement timer, tamed owner and order (56-byte rows), then each slot's
remaining summoning time (8 bytes, 0 for a creature not summoned). A version 2
frame, the same without that tail, still loads with nothing summoned. A tamed or
summoned slot counts against no region's population. A version 1 frame (40-byte
rows) is read only by `gwsc-retire`. The generated revision derives from the table
contents. A changed catalog refuses recovery rather than rebinding indexes.

Summons (Magery, UOAIX-81): the composite attaches `gws-with-summons catalog`, which
appends ServUO's pre-SE blade spirit, energy vortex (drawn as Body.def's body 13 at
hue 20) and black bear after the generated profiles, so the revision and every saved
index stay valid. `gws-summon state shard profile x y z owner until` places a creature
with region -1, the owner it follows (0 for one that answers to nobody) and the tick
its summoning ends; the spawn pass dismisses it then (`gws-dismiss`), and a summoned
creature that dies leaves no corpse (`vanish-hook` on GameCombat, bound to
`gws-summoned`).

Restore WorldRecords and UCB1 first, then UWS1, before serving a connection.
UCB1 owns names, stats and RNG and clears combat targets/deadlines. UWS1
validates all rows, world serial/body/hue bindings, duplicate serials, region
population caps and timer ranges before mutating state. It reconstructs
population counts, rebases deadlines and clears visibility/connection state.
The enclosing checkpoint owns integrity and atomic component restoration.
Encode after the complete spawn pulse has removed stale serial bindings.

## Source and generation

`import-world-spawns.ps1` reads the local UOX3 checkout at
`D:/Projects/uo-reference/UOX3`, revision
`4560ae841bac898817143d7aa95ce59f47ab98e0`. Source-X has no matching region
script assets in the reference checkout, so the region/NPC data uses the
authorized UOX3 fallback. The retained [UOX3 license](ports/UOX3-LICENSE.txt)
is GPL-2.0-or-later. Movement retains the
[Sphere Apache license](Sphere-LICENSE.txt).

The importer reads Felucca dungeon, world lands/lostlands, forts and graveyard
tables. It selects UO-era regions and GETUO profiles. Region GET inherits
geometry/settings/exclusions but clears inherited spawn entries, following
CSpawnRegion::Load. NPC GET/GETUO and ID alternatives become weighted variants;
nested alternatives preserve their relative probabilities. NPC-list weights,
unweighted list expansion, stat/hue/GOLD ranges and minute timers come from the
reference. Reversed random ranges normalize as UOX3 RandomNum does; reversed
DAMAGE uses the lower-field value as both bounds, matching ApplyNpcSection.
Duplicate region identifiers retain the first definition as LoadSpawnRegions
does. Unknown lists/NPCs and invalid geometry are reported by the importer;
unresolved NPC choices remain failed draws rather than replacement creatures.
One override departs from the source (Damian, 2026-10-06): the Britain
Cemetery regions 5505 and 5506, north of the forge, draw low-end undead only
(`location_25_light` without its lich: skeleton 3, zombie 4, bone axeman 2,
ghoul 2).

The client is 1.25 and animates fewer bodies than UOX3 uses; a body with no
`ANIM.IDX` row (offset -1) is drawn as nothing while it still fights. The
importer reads the installed client's `ANIM.IDX` (`-AnimIndex`) and the later
official client's `Body.def` (`-BodyDef`, OSI's own substitution table), and
replaces each such body with the first animated body `Body.def` names, keeping
name and stats; the substitute's hue applies only to an unhued profile. A body
with no animated substitute drops its profile (only the fire gargoyle, 130, on
2026-10-06), and the import refuses any emitted body the client cannot
animate. The manifest lists every remap.

A regenerated catalog changes its revision. At boot, `cg-spawn-attach` hands a
UWS1 saved under another revision to `gwsc-retire`, which checks the frame
against its own header counts, deletes each slot's serial that is still a
live NPC mobile bound in combat, and attaches the controller fresh; every
other record is kept. The server prints "UWS1 from a changed catalog retired
N spawned mobiles".

The importer also writes `CreatureSoundData.codex`: each emitted profile's
`creatures.dfn` SOUND_ATTACK, SOUND_DEFEND and SOUND_STARTATTACK, keyed by the
body the client draws, one 64-bit row per body (attack, get-hit, start-attack,
16 bits each from the low end, 0 silent). Where two
profiles drawn with one body disagree, the first profile's sounds are kept and
the manifest names the others. `cg-context` hands the table to GameCombat's
`sound-hook`; human sounds stay Source-X's fixed sets in GameCombat. The sound
chapter carries no catalog revision, so regenerating it retires no spawns.
`CreatureCarveData.codex` is written the same way from each profile's `CARVE`
table in `carve/carve.dfn`: meat (raw ribs, raw bird, leg of lamb, chicken
leg) low and hides (hides and the spined, barbed and horned hides) high.
Feathers, scales, fish steaks and body parts are not imported. The importer
refuses an output path that is one of its own sources.

Each profile carries its NPC section's `TOTAME` as `taming` (0 when absent); Animal
Taming reads it (ActiveSkills.md).

Generation writes WorldSpawnData.codex and a build-output manifest containing
source SHA256 values and diagnostics. It checks every source hash again before
publishing output. Re-run the script after source changes, then run both proofs.
The shipped `CreatureCarveData.codex` carries a shorn-sheep line (0xDF carves as
0xCF) the importer does not emit: pass `-CarveOutput` a scratch path or the
regeneration deletes it.

## Cost and acceptance

Retained controller storage is O(regions + slotBudget), plus the one-time
catalog and existing shared combat metadata. Region counts/timers occupy two
fixed qword arrays; slot records are reused. A pulse visits the bounded slots,
checks at most16 regions when due, and uses indexed tile chains. Placement
also searches the bounded free-slot list; it never scans world capacity.
Packets, temporary world objects and Results stay in request scratch.
Codec validation uses O(regions*slots + slots squared) time for population
and duplicate checks with no retained decode scratch. Both dimensions are
bounded at construction. Compiler heap/time and seed behavior are unchanged.

An all-digit UOX3 `NAME` (`NAME=5123//a bone axeman`) is a key into
`data/dictionaries/dictionary.ENG`; the importer resolves it there and refuses
a key the dictionary lacks.

WorldSpawnCatalogProof checks table bounds, references, unique region IDs,
classic orc resolution, inherited Rat Valley geometry and that no profile name
is all digits. GameWorldSpawnReplay
checks authenticated spawning, caps, exclusion/occupancy, type targeting,
timer replacement, no created loot and malformed/rebased recovery.
WorldSpawnAggroProof checks aggro at 8 tiles, a hidden British left alone, the
start-attack sound once, the 0.3 s chase out of the region, pursuit after the
player walks off, the 18-tile drop and the walk home. These
native proofs do not establish installed-map placement or client acceptance.

The composite binds the controller at server boot (`cg-spawn-attach`, 128
slots, `CompositeGameServer` `cgs-ready`) and calls `gws-after` after its
combat pulse in `cg-monsters`. Its terrain predicate `cg-spawn-terrain` is
`gws-ground` plus ONLYOUTSIDE: a tile is outside when no collider in the walk
map starts at or above standing height + 16. UWS1 persists as UCC1 version12,
held at decode and decoded when the controller attaches.
`gws-world` is the session-free core of `gws-after`; with no client connected,
`cg-world-tick owner game map watcher tick` runs it around an NPC watcher over
a caller-loaded walk window and discards the packets.
`proofs/CompositeWorldSpawns.codex` grades spawning, the headless tick, drawing within 18 tiles,
the outside predicate both ways and restart restoration; the real-client grade
remains root's.
