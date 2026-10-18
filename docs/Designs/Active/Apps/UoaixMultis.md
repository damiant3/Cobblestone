# UOAIX buildable multis

The order (Damian, 2026-10-17): "extract houses, buildings, other features from the main map and put them into multis
we can build in the world. so say like i want a castle with a shrine in it, i can do that. or that cool house style i
like with the flat roof you can stand on." Owner: reek. Houses come from deeds (`uoaix-init.md`); val restores the
deeds and a deed names a design from this table.

## End state

1. A **piece** is a rectangle of the main map cut out with its statics (a building, a shrine, a roof style), named in
   `apps/uoaix/multis.cfg`.
2. A **design** is a multi id and a list of pieces at offsets, with optional clears, so pieces combine (a castle with
   a shrine in its yard).
3. The 1.25 client draws a design whole as one world item of art 0x4000 + id, from its own `MULTI.IDX`/`MULTI.MUL`.
   Those two files are patched from `multis.cfg` and shipped to the player clients with the statics at a downtime
   switch.
4. The server collides a placed design from the same components (walls, floors, stairs and a roof you can stand on).
5. A design is placed by the decorator (`[decor multi <id or name>`) and by a house deed.

## Files

| file | what it holds |
|---|---|
| `apps/uoaix/multis.cfg` | the pieces and designs (grammar below) |
| `apps/uoaix/build-multis.ps1` | reads the unstripped client and `multis.cfg`; writes patched `MULTI.IDX`, `MULTI.MUL` and a receipt to `-OutDir` (outside the repo) |
| `apps/uoaix/MultiDesigns.codex` | the server's multi colliders: `md-lay` (lay or lift one placed multi; answers the colliders without room), `md-fits`, `md-sync`, `md-lift` |

A placed multi collides only where a window is loaded: the paged 64 by 64 window and Britain's. A cell's room is the
window's stride (the busiest cell plus 8, `wmc-room`); the cut designs' busiest cell holds 7 colliders.

## multis.cfg

```
Piece <name> <x0> <y0> <x1> <y1> [<zlo> <zhi>]   statics in the rectangle (and z range) on the main map
  origin <x> <y> <z>                             optional; default the rectangle's middle tile at its land z
  land 0x<lo> 0x<hi> 0x<art>                     land tiles of art lo..hi in the rectangle become static art at land z
  except 0x<art> [0x<art>]                       art (or an art range) left out
  door <x> <y> <z> [0x<closed> 0x<open> <dx> <dy>]  a map door that becomes the front door (arts from the decoration)
Design 0x<id> <name>                             a multi id from the free range, and a name
  <piece> <dx> <dy> <dz>                         a piece with its origin at (dx, dy, dz) of the design
  clear <dx0> <dy0> <dx1> <dy1> [<zlo> <zhi>]    drops the components the lines above put in the rectangle
  multi 0x<id> <dx> <dy> <dz>                    an earlier design (with its doors) or a client multi's drawn components
  item 0x<art> <dx> <dy> <dz>                    one static
```

A design's components are its pieces' statics relative to the design origin, in line order; `clear` removes what
earlier lines placed. The map's land is not part of a piece unless a `land` line converts it (a shrine's floor is land
0x43A..0x43D, not statics).

## Multi ids

`MULTI.IDX` has 4096 slots. The 1.25 client uses 0-23 (boats), 100-127 (houses, towers, keeps, castles), 1000-1003 and
2000-2007; `VERDATA.MUL` patches 140 and 141. `build-multis.ps1` refuses a design id the client or VERDATA already
holds, so a design never replaces a client multi. A design keeps its id for life: a placed item and a deed store
0x4000 + id.

## Components

A `MULTI.MUL` component is 12 bytes: art (u16), x, y, z (i16) and flags (u32). Flags 1 draws the component; the
client's own houses carry flags 0 for parts the server places (doors, signs). Every cut component is written with
flags 1. A `MULTI.IDX` entry is offset, length and extra (i32 each); new designs are appended after the client's last
component.

## Slices

| slice | what | state |
|---|---|---|
| 1 | `multis.cfg`, `build-multis.ps1`, the first pieces and designs: 0x200 the Shrine of Justice (209 components), 0x201 Castle British's keep and south court (11267), 0x202 its throne room (2041), 0x203 the castle with the shrine on the court's barracks roof (11376) | built |
| 2 | ship: `switch-live.ps1 -Bake` runs `build-multis.ps1` before the stop and gives every player client the patched `MULTI.IDX`/`MULTI.MUL` with the statics; `bake-decor.ps1 -MultiDir` expands a marked design from them | built, not run |
| 3 | server collision: `install-map-cache.ps1 -MultiDir` writes MCP1 (every multi's colliding components) after the world journal, header cells 336, 344, 352; the server loads it at boot (`MULTIS installed rows=`); `MultiDesigns.md-sync` lays every decorator item of art 0x4000 + id into the paged and Britain windows when a window moves or the decorator changes (`cp-cover`), `[decor remove` and `nudge` lift it first, and `[decor multi` refuses where the GM's window lacks room (`md-fits`); `bvt/BvtMultiDesigns` | built, not run |
| 4a | `[decor multi <id hex or name>`: `build-multis.ps1` writes `MULTI.NAMES`, the installer puts the names after MCP1's rows (head cell 32 counts them), `md-find-name`; `[decor multi` alone lists the installed designs | built, not run |
| 4b | a deed places a design: a deed's health names a design id of 0x200 or more (`cn-design-ok`); `cn-place-design` stands it with its origin on the targeted tile, its plot the rectangle its colliders cover (`md-bounds`), refused over an item, plant, wall or another plot and where a cell lacks room (`md-fits`); the client is sent the multi item alone; `cp-designs` lays deed sites beside decorator multis (revision: decor and `CnState`), and a forfeit keeps a torn design's id in site field 15 until `cp-lift-torn` lifts it | built, not run |
| 5 | cut in game: `[decor cut <name>` then a picked corner records the map rectangle between the GM's tile and the pick (at most 128 a side) in `MdState.cuts` (32 between bakes, memory only: a restart before the bake drops them); `live.decor.cuts` exports them; `bake-decor.ps1` appends a `Piece` (with the stone, cobblestone and marble `land` rules) and a `Design` of it alone at the next free id to `multis.cfg` (a name `multis.cfg` holds is skipped); `switch-live.ps1 -Bake` builds the multis again after the export | built, not run |
| 6 | doors and locks: a piece's `door x y z` names a map door (its arts and open offset from the decoration DWD1, `build-multis.ps1 -DecorationFile`); a design takes its pieces' doors, at most two (a single or a double door); `MULTI.DOORS` and MCP1's doors section (head cell 40, 16 bytes each) carry them; a deed house of the design draws them as item parts 7 and 8, each solid while shut (field 13 bit n), opened and locked with the house key as the 7 by 7 house's door; `cn-house-at` answers the design's plot, so lock-down and the ban work inside it | built, not run |
| 7 | compose in game: `[decor compose <name>` then a picked corner makes a design of every decoration between the GM's tile and the pick, its origin the GM's tile (a cut row of kind 1); `live.decor.cuts` lists its members; `bake-decor.ps1` writes a `Design` of `multi` (a placed design or client multi) and `item` lines, leaving out a member multi neither the client nor a design holds and skipping a compose left empty; a design with more than two doors keeps the first two | built, not run |

## Gaps

- Only the front doors open: a design's other map doors are decoration, not statics, so its inner doorways stand open.
- A placement check reads only the cells inside the GM's or the player's 64 by 64 window; a larger design's far cells
  are laid when a window covers them, and a cell without room is logged (`MULTI colliders without room=`), not refused.

## Open questions

- The flat-roof house Damian likes: its place on the map (asked through root, 2026-10-17).
- The largest multi the 1.25 client has drawn is 3461 components, 31 by 31 tiles (id 126). A larger cut is untested
  until Damian's client draws one.
