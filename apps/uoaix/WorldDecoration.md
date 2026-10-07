# Legacy Britannia decoration

The host importer ports ServUO `Scripts/Commands/Decorate.cs`,
`Scripts/Items/Functional/BaseDoor.cs`, `Doors.cs`, `SecretDoors.cs`, and
the literal component constructors under `Scripts/Items/Addons`. It reads
all CFG files in `Data/Decoration/Old/Britannia`, then `Data/Decoration/Britannia`,
including `patches.cfg`. Identical art/hue/position placements collapse into
one row; current definitions replace old ones. Named signs come from literal
CFG Name properties and UOX3's `felucca_signs.jsdata`; this old client has no
modern cliloc dictionary. The runtime is a Codex adaptation of door range,
open art/offset and blocked-close behavior. Crafting, traps and teleporters
remain immovable scenery in this decoration layer, not new gameplay services.

The CFG sets do not contain most ordinary shop doors: ServUO installs those
separately through `DoorGenerator`. The importer supplements missing door
positions from UOX3's literal `felucca_doors.jsdata` version3 table. Closed art
selects the corresponding ServUO door constructor and facing; opened art and
offset follow that typed definition. Existing typed CFG doors win at the same
x/y/z, even if the supplemental table has another art or facing there. Template
type12 is an unlocked door and type13 is locked, as in UOX3's item types.
This reads literal placements, not upstream JavaScript execution or guessed
doors at arbitrary gaps. Unmapped supplemental door art is reported as skipped.
A door from either source with no wall (TILEDATA 0x10) or impassable (0x40)
static within one tile at its height is skipped as unframed: 133 of 1,768 on the
1.25 client (2026-10-06), among them two template gates standing in the open
at 1421,1563-1564 beside the Britain forge.

ServUO sources and data are GPL-2.0; UOX3 uses GPL-2.0-or-later. Copyright
belongs to their contributors; licenses are in
[ServUO-LICENSE.txt](ports/ServUO-LICENSE.txt) and
[UOX3-LICENSE.txt](ports/UOX3-LICENSE.txt). The legacy ground-item packet's
light field follows Source-X `src/network/send.cpp` PacketItemWorld under
Apache-2.0; [license](Sphere-LICENSE.txt). The import manifest records SHA256
of every consumed reference file. No upstream C# or JavaScript is executed.

## Install and composition

Run the importer once for Britannia; a smaller fixed region can be selected
with X/Y/Width/Height:

```powershell
pwsh apps/uoaix/import-decoration.ps1 -ClientRoot D:/Projects/uoaix-client -ReferenceRoot D:/Projects/uo-reference -OutFile <local-install>/britannia.dwd
```

This reads the installed 1.25.34 legacy TILEDATA in place. Missing/empty or
out-of-range art is skipped, including a door when its opened art is absent.
Door swings outside the dataset region are skipped. Literal multi-component
add-ons are expanded from their constructors. A nonliteral addon or unknown
door definition refuses the import instead of silently losing components.
Malformed placement rows are reported and skipped; the reference currently
contains `cove.cfg`'s `2331 1219 0s`. The JSON sidecar names rejected rows,
input identities, window and output hash. Both the DWD1 file and its sidecar
contain locally derived client properties: keep them on the install/world
disk, never in the depot, public image or release artifacts.
The manifest also reports total door rows and supplemental door rows. Eight
classic facings, including case-insensitive Facing properties, are checked by
the host fixture against BaseDoor offsets and closed/open art pairs.

`serve-mul.ps1 -DecorationFile <local-install>/britannia.dwd` adds bounded
`/decoration/<offset>/<count>` reads, retaining existing MUL routes. Initial
composition calls `wd-read buffer length map map.stride` when the install
cache supplies bytes directly. The optional network loader is
`wdl-load map map.stride`; the complete composite uses disk-only boot.
Legacy callers without the new stride field supply their fixed200-byte cell
extent. DWD1 storage, WdState
and its buffers must be below receive scratch. The map must contain only
its base terrain/statics when `wd-read` is called; it saves that collision
base before applying decoration. Keep this base distinct from a cache that
already includes decoration, so restart cannot apply the overlay twice.
The immutable dataset covers the walk window and remains unchanged as that
window moves. After each map-cache refill, call `wd-rebind state map map.stride` before
dispatching or publishing another gameplay reply. This recaptures the new
base collision cells and applies the same saved door bits to the new window.
`wd-rebind` copies the map's CURRENT solids as the new base, so the map must
be undecorated when it is called: rebinding a map that `wd-read` already
decorated bakes every closed door into the base, and no later toggle can open
it. Restore the map from `state.base` first (`cg-town-doors` does).
Window dimensions and stride must stay fixed; resizing needs a fresh retained context.
The state stores origin, dimensions and buffer identities separately from
the mutable WalkMap. Handler, pulse and codec restoration reject a rebased
map until rebind, including a same-pointer rebase. A failed refill/rebind
must close the encompassing owner rather than serve an undecorated window.

Capture WdState in the composite. Put `wd-handler state` before the other
item handlers and combine `wd-pulse state` with the other family pulses.
It consumes existing 06/07/09 framing and needs no new opcode declarations.
Both return replies through the owner, never raw sends. Pulse batches at
most32 nearby additions; repeated pulses finish a large view. Leaving range
forgets the object in the server view without a removal packet: client1.25
culls it locally. Reentry sends it again. Only objects within18 tiles are sent.
The composite's single active character owns this volatile visibility set.

Reserve serials `7E000001..7E010000` for decoration; inventory/world allocators
must not enter that interval. A handler collision with an existing world
object refuses. Decoration is stored independently of WorldTable, so it
does not consume the game's inventory/NPC slots. Double-click toggles a
nearby unlocked door, updates art and position, and rebuilds collision in
its affected cells. Closing onto a mobile or ground item refuses. Locked
doors refuse until a future key service authorizes them. All other imported
objects remain immovable. Clicking a sign returns its text. Light patterns
use the legacy 1A direction field. This layer has no automatic door timer.

Persist the WDS1 codec in the same encompassing transaction as game state.
`wdc-size state`, `wdc-write state buffer length` and `wdc-read state buffer
length` own the door-state section. Include that section in dirty detection
and before/after comparison; a door action changes no WorldTable cell.
Commit before publishing packets. On recovery load the immutable DWD1, then
apply WDS1 before accepting a connection. The decoder verifies the dataset
identity, rebuilds collision, and clears connection/visibility state. The
descriptor file is install data, not repeated in each action journal row.
Adding missing doors changes DWD1 identity and row numbering. Reinstall the
descriptor through the composite owner; an old WDS1 cannot be decoded against
the new dataset. The owner must migrate saved door bits by placement identity
or initialize the new decoration section as part of its test-world install.
Do not overwrite a running world's descriptor independently of its checkpoint.

## Formats

DWD1 is little endian. Its 64-byte header contains qwords: magic31445744,
row count, x, y, width, height, dataset identity, reserved zero. Identity is
the positive low63 bits of SHA256 over the file with the identity field zero.
At most65536 rows are admitted, sorted by x for binary range lookup.
Each 176-byte row has fourteen qwords:
x, y, z, closed art, hue, flags, height, kind, opened art, dx, dy, light,
opened flags, opened height; followed by a NUL-terminated ASCII name in64
bytes. Kind0 is scenery,1 door,2 sign,3 light,4 locked door. Light -1 means
absent;0..255 supplies the packet direction byte. Non-door offsets are zero.

WDS1 is 24 + row-count bytes: qword magic31534457, dataset identity, count,
then one byte per row. Zero is closed;1 is open and is valid only for doors.
Views, connections and rendering cursors are never persisted. Extents and
all row/state fields are validated before admission.

## Validation and cost

`proofs/DecorationReplay.codex` checks collision before and after opening,
occupied close refusal, legacy light and named-sign replies, immovability,
client culling, bounded reentry, codec recovery and malformed state rejection. Root grades the
real client only in Fester's complete composite; this unit makes no separate
client-acceptance claim. Import and adapter checks use local install data.

Retained storage is the descriptor buffer (64 +176N), two N-byte arrays,
two4N-byte row arrays, and a stride-sized base collision snapshot per loaded
map cell (capped at64 MiB). Decoration reuses WorldIndex's sparse8x8 tile
pages and directory, with capacity P equal to occupied blocks: wi-bytes(P).
Its immutable row chains use closed coordinates; a19-tile lookup margin
covers the one-tile door offset, then filters actual positions to18 tiles.
The temporary block-count bitmap is393216 bytes and is freed before the
retained index allocation. Index construction is O(N + P + directory).
A stationary, fully emitted view takes O(1) per pulse. A changed view prunes
only its V cached rows, then visits1521 nearby tiles and their L rows:
O(V +1521 + L), independent of distant decoration rows. At most32 new
packets are emitted per call. Movement restarts an interrupted scan while
preserving already shown nearby rows. Door changes scan O(N) for
each affected cell plus the bounded world table for close obstruction.
Codec work is O(N + map cells); import sorting is O(N log N). All request
packets and text use the owner's scratch boundary; no per-row records are
retained. The caller's collider capacity is enforced, with rollback
on overflow. Compiler heap/time behavior is unchanged.
