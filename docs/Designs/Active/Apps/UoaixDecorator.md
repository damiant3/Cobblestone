# UOAIX decorator GM

The order (Damian, 2026-10-09): "we need an admin gump in the client that lets us decorate the world. putting items
in static from the client. it should trigger a rebuild of the maps somehow on an administrative schedule, like after
a downtime and comeback. the idea will be i can split up the duties of GMs into various aspects, one being a
decorator. that decorator can go around and, say, add forges to mining camps, or block off buggy areas of the client
or make a shop look more prettier. first they add normal items, then mark them for incorporation into the shard
statics."

## End state

1. A decorator is a GM aspect: a power bit in the GM grant (`GmContract.md`), dubbed by the owner like the other
   powers, and logged in the GM action log.
2. In the 1998 client, a decorator opens a decorator gump and places ordinary items (any art, hue, x, y, z), moves
   and removes them. Placed items are normal world items until marked.
3. The decorator marks a placed item, or every item in an area, for incorporation. Marked items are listed in the
   gump and in the admin page, and can be unmarked until the bake.
4. The bake runs on an administrative schedule, at a downtime and comeback: the server writes the marked items to the
   shard's own placement file (beside `premises.cfg`), the stopped-server install (`install-map-cache.ps1`) folds them
   into the server's collision and decoration, the client statics are patched the way `transform-flora.ps1` patches
   `STATICS0.MUL`, and the marked world items are removed so nothing stands twice.
5. A decorator can block a buggy area: a placed blocking item becomes a collider after the bake. A decorator also
   removes a client static (a wall, a fixture): `[decor unstatic` gives a location cursor, and the picked
   static gets a red-hued copy (hue 0x26) that is a decorator row of mark 2 (`dc-place-removal`); the row persists with
   the decorator table, refuses a mark, a move and a story set, and `[decor remove` on the copy cancels it.
   `live.decor.removed` lists those rows, `bake-decor.ps1` writes each as `Remove 0xART X Y Z` in `decor.cfg` and its serial
   in the pending file, `install-map-cache.ps1` drops the matching client static from the server's colliders,
   `decor-statics.ps1` drops it from the players' statics, and `decor-baked` deletes the copy. A `Remove` naming no
   static removes nothing and both scripts warn with the matched count.

## World-building tools

The widening (Damian, 2026-10-09): "i want even tools for modifying tiles, so i can build roads, raise and lower
mountains, cut rivers, even build buildings. i want to see boundaries for guard zones, and get spawn information."

6. Land editing: a decorator sets a land tile's art and height (roads, raised and lowered ground, river beds) and
   places multi-tile building pieces, with an area brush. Land edits bake like statics: the shard's land patch,
   folded into the server's map by the stopped-server install, and the client's `MAP0.MUL` patched to match.
   Tile tools built (`GmLand.codex`, gated on `?decorate` and British): `[land <name|hex>` paints, `[raise n`/`[lower n`
   move the ground, `[flatten` levels to the picked tile, `[landclear` drops pending edits, `[brush 0-8` sizes the square,
   `[landshow` shows pending edits as runes at their new height; blu's menu calls `gld-menu`. The server holds no land
   art, so a height-only edit carries art -1, which the bake keeps. The client cannot redraw land live: an edit shows
   only as a rune until the bake. Pending edits are held in memory: a restart before the export drops them.
   Building pieces built (red, `CompositePaging.codex`): `[decor run <art>` lays one art in a line from the GM's tile to
   the picked tile (at most 64), `[decor fill <art>` covers the rectangle between them (at most 256), both at the GM's
   height and all or nothing against the table's room; `[decor multi <id>` places a MULTI.MUL building as one item of art
   0x4000 + id, drawn whole by the client and expanded into statics by the bake. Pieces are ordinary decorations,
   marked for the bake with `[decor mark` or `[decor markarea`.
   `[decor gump` (val, `cp-decor-gump`) shows the decorator menu's entries as a gump with 247/248 buttons on the 2600
   backing, art the 1998 GUMPIDX holds; a press is read as that menu choice, so `[decor` alone still opens the menu.
   Its Land entry opens the land page (`cp-land-gump`): a button per preset tool command (`[land` grass, dirt,
   cobblestones, sand, water; `[raise`/`[lower` 1 and 5; `[flatten`; `[brush` 0, 2, 4; `[landclear`; `[landshow`), each
   read as that command spoken, so the tile is picked after the press and the land gate applies.
7. GM overlays: a GM sees guard-zone boundaries (`Law.md`, `CivicLive.md`) and spawn regions with their spawn
   information (`WorldSpawns.md`: region, creature, count, respawn) drawn in the client. Built (`GmOverlay.codex`):
   `[zones` and `[spawns` toggle hued runes on the edge tiles of each zone within the GM's 37-tile view, sent to that
   GM's connection only. A guard zone is every tile within `cv-earshot` (36, per axis) of a living guard's patrol
   route; Britain has no fixed guard rectangle, because a guard answers a call only in earshot. A click on a rune names
   the patrol, or the spawn region's id, name, living count against its maximum, primals, birth interval and
   weighted creature table.

## Split

| Part | Owner |
|---|---|
| Decorator power, gump, place/move/remove, mark and unmark, admin listing | blu |
| Bake: placement file, install fold, client statics patch, schedule at downtime, removal of baked world items | val |
| Land editing tools in the client (6) | red |
| Land bake: the land patch, install fold, client `MAP0.MUL` patch (6) | val, after the statics bake |
| GM overlays: guard zones and spawn regions with spawn information (7) | red |

The two halves meet at one record: a marked item's art, hue, x, y, z and the decorator's account, written by the
server and read by the bake.

## The bake

The record travels over the admin protocol, not the world disk: the server lists marked items as
`live.decor.marked` = `[{serial, art, hue, x, y, z, account}]` and takes the admin write `decor-baked <serial,...>`,
which removes those world items, drops their marks and logs it (blu, agreed 2026-10-09).

A downtime bake, in order:

1. Before the stop, `bake-decor.ps1 -Port -KeyFile` reads `live.decor.marked`, appends each item to
   `apps/uoaix/decor.cfg` (the shard's placement file, `premises.cfg` syntax) and writes the serials to
   `build-output/uoaix/decor-pending.json`; it refuses while a bake is pending. Items marked after the read wait for
   the next bake.
2. With the server stopped, `install-map-cache.ps1` reads `decor.cfg`'s placements as statics of the shard's own: each
   collides as a client static does, on the existing world disk (the install keeps the world store). Baked items are
   statics, not server-drawn scenery, so `import-decoration.ps1` does not read them and nothing stands twice.
3. `decor-statics.ps1 -StrippedDir -OutDir` adds `decor.cfg`'s items to the player's `STAIDX0.MUL`/`STATICS0.MUL`,
   starting from `transform-flora.ps1`'s reference-checked output, so the result depends only on `decor.cfg` and an
   item leaves the client at the next run after it leaves `decor.cfg`.
4. Once serving again, `bake-confirm.ps1` sends the panel action `decor-baked` with the pending serials (blu, main
   41764) and deletes the pending file; `switch-live.ps1 -Bake` runs steps 1 to 4.
5. The schedule is the downtime switch: `switch-live.ps1 -Bake` runs steps 1 to 4 between stop and relaunch, and
   gives both player clients (`uoaix-client-flora`, `uoaix-client2`) the same files, each backed up first. Not yet
   run; root runs the first bake at a switch.

Land edits (item 6) bake the same way through `apps/uoaix/land.cfg`, one line a cell, `x y 0xART z`, a later line for
the same cell winning: `install-map-cache.ps1` replaces those cells' land art and height before it builds the server's
map, and `decor-land.ps1 -ClientRoot -OutDir` writes the client's `MAP0.MUL` with them. The record is `live.decor.land` =
`[{x, y, art, z, account}]`, last edit per tile winning, and the admin write `decor-land-baked <x,y;...>` drops the
baked edits (blu, agreed 2026-10-09); `bake-decor.ps1` exports it with the marked items and lists the tiles in the
pending file. `live.decor.land` and the owner command `decor-land-baked` with field `tiles` = `"x,y;x,y"` are built
(red); the reply is `{"ok":true,"dropped":N,"waiting":M}`. An admin request body holds at most 2048 bytes, so the
comeback sends the tiles in batches.