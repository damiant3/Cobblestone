# Assist: pretest a house placement

Owner: blu (the Assist, `apps/uoaix/assist`).

## The request

Damian, 2026-10-20: "what I do want is a map feature that lets me pretest house placement". Asked where: "Both", the
game client and the Assist's map, sharing one placement check. No deed is needed for a test.

## The two faces

- **In the game.** A command said in the client (`[house <type>`, the recorder's `[` family) makes the Assist hand the
  client the same multi-target cursor a house deed gives (packet 0x99 with the house's multi id), so the client draws
  the house where it would stand. The click's answer (0x6C with x, y, z) is kept by the Assist and never reaches the
  server; the Assist says in the client whether the house fits there and, if not, why, and pins the spot on its map.
- **On the map.** The Map tab gains a house picker. A click on the map draws that house's footprint tile by tile,
  green where the tile passes and red where it fails, with the reasons listed under the map.

## The check

One function judges a house type at a spot, for both faces:

- **The footprint** is the house's multi from the client's `MULTI.IDX`/`MULTI.MUL` (12-byte components: item, x, y,
  z, flags, around the centre; the format is noted above `UoMap` in `UoMap.cs`). A table maps each house
  deed type to its multi id.
- **The rules** are uo1998's own: it runs the 1998 UoDemo server, whose C translation is OUO
  ([github.com/draxinar/ouo](https://github.com/draxinar/ouo), MIT; `uoaix-init.md` names it the source for uo1998's
  behaviour), read at 814ea87 for this design. A multi is checked where it would stand by `CMultiSlave_MoveCheck`
  (`multi.c:926`, reached from `CItem_MoveMultiCheck`, `item.c:5137`); the deed's use and that check are the rules
  to transcribe, cited by function. RunUO's `Scripts/Multis/HousePlacement.cs` (runuo/runuo 71b2794, SHA-256
  6EE7EE71...ABA77D6) is a later reimplementation and only a cross-check: no impassable land, static or item
  touching the house, the foundation flat on passable land, no road land under it or around it, the border
  tiles clear, and five tiles before and behind the walls clear of other houses. Both are evaluated against the
  client's own map files (`MAP0`, `STAIDX0`, `STATICS0`, `TILEDATA`, read by `UoMap` and `UoRadar`, with `MultiBox`,
  `MultiAt` and `HousesIn` for the houses the Assist has seen) and the 1998 regions (`UoRegions`).
- **What only the server knows** (a house the Assist has never seen, uo1998's exact region table, any rule uo1998
  changed) is not guessed: a pass says "fits by the client's map and what the Assist has seen".

## Grading

uo1998's own build is not published, and OUO is the nearest source for its rules. Every real placement Damian makes, and every refusal
the server sends, is logged with the spot and the house type; each one becomes a case the check must agree with. A
case it gets wrong is a rule to correct.

## Stages

1. The check: `UoMap.HouseCheck` (`UoMap.cs`), the 1998 rule over every MULTI.MUL component and the client's
   map data, the reasons per failing tile; the deed table `UoMap.HouseTypes`/`HouseNames`/`HouseMulti` (15 deeds,
   0x64 to 0x7E and the large smithy 0x8C) and the front shift `UoMap.HouseFront`, each deed's front offset RunUO's.
2. The in-game face (`AssistFeatures.HoldHouse`): `[house <type>` (a type number or a word of the deed's name; bare,
   it lists them) sends the 0x99 cursor; the click's 0x6C is kept from the server, shifted north by the front offset,
   judged by `HouseCheck` plus the mobiles in view and the player (`areMobilesInMultiArea`), told in the client with up
   to 3 reasons, logged as a `House test` note, and pinned on the map as the one `House test` pin.
3. The map face (Map tab, `House` line): a deed picked, a left click on the map is the front row at the land's z
   (`UoMap.LandZ`), judged by the same `AssistFeatures.HouseTest`; `MapView.HouseTiles` fills each tile green or red
   and the reasons list under the map.
4. The placement log (`AssistFeatures.HouseWatchServer`/`HouseWatchClient`): a real deed's 0x99 and the client's
   answer to it log `House placement:` with the check's prediction; the server's answer (the new multi at the centre,
   a house-key line, or a house, housing or terrain line within 5 s) logs `House placement answer:` with agrees or
   DISAGREES, and appends a case to `%LOCALAPPDATA%\uoaix-assist\house-cases-<server>.txt`. The control port's
   `housegrade` judges every case again with the check as it stands and names up to 10 disagreements.

## OUO's placement check

- The rules `HouseCheck` transcribes (OUO 814ea87): a script's multi placement calls
  `CItem_MoveMultiCheck` (`wombat_exec.c:15498`; `multi.c:3331` calls it with flag 7), which runs
  `CMultiSlave_MoveCheck` (`multi.c:926`, boat wrapping aside), which runs `CMultiSlave_ValidateMove`
  (`multi.c:1012`), then `CMultiSlave_CanExistAt` (`multi.c:1051`): for each multi part of the slave (filtered by
  `CItem_HasMulti_Filter`), at the target plus `CMulti_GetOffset` and the part's height, the coordinate must be valid
  and `CTerrainManager_CheckMoveBlocked` (`terrain.c:536`) must return bit 4. That function, over
  `Terrain_BuildSurfaceList` (`terrain.c:441`: the land from its lowest corner to its average height, then dynamic
  items and statics at their own heights), fails on any impassable surface overlapping z .. z + height, and sets bit 4
  when any surface is present. Land's flags come from `GetLandTileFlags` (`entity.c:119`): always 0x40, with 0x600
  added when walkable; move type 8 adds nothing for road and furrow land (0x71..0x78, 0x09..0x15, 0x150..0x15C, the
  ranges RunUO's road list carries), so move type 8 looks like house placement's: confirm it. The parts are every
  component of the multi type's definition, each an item (`CMultiManager_BuildComponents`, multi.c near 1850), so the
  whole footprint is walked, each part at its own offset and height. The deed's script is readable at `D:\Projects\uo-reference\rundir\scripts.wombat\` (`housedeed.m`, `housestuff.m`;
  `scripts\` holds the same files encoded): the multi is 0x64 + 2 x the deed's `myhousetype`, set by the deed templates in
  `bank\templatestable.dat`; `isHousingOkay(place, 1)` refuses the area first ("Housing can not be created in this
  area."); the target's y moves back by the multi's maximum y extent (`getMultiExtents`) before the check;
  `areMobilesInMultiArea` refuses; then `makeMultiInstCheck(place, multi, 0x08, -3, ...)`: move type 8, and -3 is
  the land's z offset in the second pass (`CMultiSlave_CanExistAt`); the first pass (`CMultiManager_CanExistAt`,
  offset 0) is the stricter and is the one `HouseCheck` transcribes. Still not counted: the region refusal
  (`isHousingOkay`, the server's rule, graded by the placement log in stage 4) and items on the ground. OUO checks
  mobiles on every tile of the house, marked FIXED against the binary, which checked the centre tile only; the Assist
  checks every tile, and the log says which uo1998 does.
