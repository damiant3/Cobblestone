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

AI2/11 select a nearby living player for shared combat; AI12 moves away from
the player; AI0/6 wander without initiating attacks. Walking stays within the
region rectangle and exclusions and uses the tile index for occupancy.
An existing valid combat target is retained, including neutral retaliation;
the scared-animal mode cancels combat and flees instead.
Casters currently share hostile melee targeting; spells, hunger and race
relations require their owning adapters. Other AI modes and profiles with
zero minimum DEX/damage/HP are refused rather than assigned invented stats.
Source NPC loot/equipment/script tags are not imported into this controller.

## Recovery

`gwsc-size state` is64 +8*regionCount +40*slotCount bytes.
`gwsc-encode state world now buffer capacity` and
`gwsc-decode state world now buffer size` return Result Integer Text.
UWS1 stores the generated catalog revision, dimensions, scheduling cursor,
remaining region/pulse timers and each live serial's region/profile/facing
and remaining movement timer. The generated revision derives from the table
contents. A changed catalog refuses recovery rather than rebinding indexes.

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
unweighted list expansion, stat/hue ranges and minute timers come from the
reference. Reversed random ranges normalize as UOX3 RandomNum does; reversed
DAMAGE uses the lower-field value as both bounds, matching ApplyNpcSection.
Duplicate region identifiers retain the first definition as LoadSpawnRegions
does. Unknown lists/NPCs and invalid geometry are reported by the importer;
unresolved NPC choices remain failed draws rather than replacement creatures.

Generation writes WorldSpawnData.codex and a build-output manifest containing
source SHA256 values and diagnostics. It checks every source hash again before
publishing output. Re-run the script after source changes, then run both proofs.

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

WorldSpawnCatalogProof checks table bounds, references, unique region IDs,
classic orc resolution and inherited Rat Valley geometry. GameWorldSpawnReplay
checks authenticated spawning, caps, exclusion/occupancy, type targeting,
timer replacement, no created loot and malformed/rebased recovery. These
native proofs do not establish installed-map placement or client acceptance.
The live terrain callback, composite checkpoint binding and complete-client
spawn/movement/aggression/restart grade remain Fester/root integration work.
