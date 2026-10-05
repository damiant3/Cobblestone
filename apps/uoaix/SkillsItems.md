# Live skills and items, 1.25.32

Codex port of SphereServer Source-X commit
`dd28a0ad53258adb55b548b1bd4df989065a1fe4`: `src/network/receive.cpp`,
`src/network/send.cpp` and `src/game/clients/CClientEvent.cpp`.
Copyright belongs to the SphereServer Source-X contributors. Apache-2.0;
[license](Sphere-LICENSE.txt). The port adapts packet construction,
ownership and drag state to the current shard records. No upstream C++ is
compiled into the image. Reference checkout: `D:/Projects/uo-reference/Source-X`.

## Wire profile

Fields are big-endian. Input offsets include the opcode. Source-X handlers
provide pickup/drop/equip identity checks, bounce behavior, container ordering
and skill targeting. [RunUO](https://github.com/runuo/runuo/blob/master/Server/Network/PacketHandlers.cs)
corroborates the legacy request sizes; its newer skill writer is not this
client's layout.

| Request | Size | Fields / ownership |
|---|---:|---|
| 06 use | 5 | serial at 1; high bit requests own paperdoll |
| 07 lift | 7 | serial at 1, amount16 at 5 |
| 08 drop | 14 | held serial at 1, x16 at 5, y16 at 7, signed z8 at 9, destination serial at 10; no grid byte |
| 09 look | 5 | serial at 1 |
| 12 text command | variable, 5..256 | length16 at 1, selector8 at 3, NUL-terminated ASCII at 4; this family claims selector24 hex with a 64-byte command bound, decimal skill id first |
| 13 equip | 10 | held serial at 1, requested layer8 at 5, mobile serial at 6; server metadata decides actual layer |
| 34 query | 10 | marker32 at 1, type8 at 5, serial at 6; skill list is type5, own mobile only |
| 6C target | 19 | type8 at 1, context32 at 2, flags8 at 6, serial at 7, x/y at 11/13, reserved15, z8 at16, graphic16 at17; only this family's pending contexts |

[Sphere 0.52's writer](https://github.com/Sphereserver/Source-Archive/blob/main/0.52/GraySvr/CClientMsg.cpp)
explicitly switches at 1.26.2. The earlier
[packet structure](https://github.com/Sphereserver/Source-Archive/blob/main/0.52/Common/grayproto.h)
contains only skill id16 and value16 in tenths. For 1.25.32, a full 3A reply
is 190 bytes: length, type0, 46 one-based id/value pairs, then zero id16.
Base/lock/cap fields from later clients are omitted. This is a compatibility
fact, not a port of the archived implementation.

Replies ported from [Source-X](https://github.com/Sphereserver/Source-X/blob/master/src/network/send.cpp):
24 container-open is 7 bytes, followed by 3C contents (5 + 19 per item).
25 single contained item is 20 bytes. 2E equipment is 15 bytes. 1D removal
is 5 bytes. 27 bounce is 2 bytes; reasons0..5 follow Source-X. 29 is a KR
drop acknowledgement and is not sent here. Paperdoll 88 is 66 bytes.
Ground item 1A uses optional amount/hue flags and a computed length.
The Codex adaptation sends reason5 before re-emitting the original placement
when the atomic drop planner refuses; an unloaded ground cell uses reason1.

## Owner interface

The family supplies opcode bounds and a retained context captured by its
handler. Reek owns registry, authentication and framing. The handler returns
`Result (Maybe GameReply) Text`, never sends raw bytes, and returns None for
unclaimed shared selectors. Its records and registered item metadata must be
allocated below the receive loop scratch boundary. Packet/reply objects are
request scratch.
`gsi-handle-at` takes raw PIT ticks. The Source-X 333 ms pickup interval is
rounded up with `pit-input-hz` and `pit-count`; no millisecond tick assumption
is made.
Construct `gsi-new shard.world` once and bind `gsi-handle context` to the
registered specs. After successful create/select entry, call
`gsi-after-entry context shard input reply` before publishing the reply; it
attaches/reuses the backpack and re-emits nearby registered ground items.
The local `WorldAction` admission frame is used only for atomic in-memory
item changes, not as a durable journal sequence. The encompassing owner
transaction owns the durable sequence and commit.
`gsi-route context` wraps `gn-route` and the after-entry call for the standalone
`GameSkillsItemsServer` entry. A composed server can instead chain the family
callback and apply the entry hook once. Entry can create one backpack object;
compose the vendor 06 handler before this general items handler.
item moves can change the four existing character equipment cells. Include
that footprint in the surrounding transaction capture. Re-register item
metadata from authoritative definitions after recovery; cursors are volatile.

Item metadata is server registered; clients cannot choose movability,
stackability, equipment layer or container gump. The initial clothing and
backpack are registered from the existing character/world records. Unknown
item types refuse until the world owner registers their authoritative metadata.
Registration binds the WorldRecords layer index. Repeating identical metadata
does not touch the world revision; a changed definition touches it once.
Skills initially read the persisted character-creation values. Targeted
inspection uses the server cursor and real object data; unsupported skill
effects return a message instead of inventing gameplay effects.

Drag state records the admitted serial, amount and source snapshot. The
adaptation leaves the world item at its source until a valid drop/equip commits,
so disconnect cannot strand an item in a transient dragging container. A new
connection clears that character's cursor. Source changes while dragging cause
a bounce. World item batches use the existing prepare/rollback primitive;
the live owner must include family metadata and character equipment changes
in its encompassing durable transaction before publication.

The packet-family grade is one replay and root's real 1.25.32 client run.
Source agreement is not client acceptance.

Retained storage is one metadata row per world slot and five cursor records;
no packet or reply list is retained. Contents and unregistered backpack lookup
walk direct children; registered backpack lookup uses layer21. Ground entry
queries only the25 tiles within the existing two-tile access range and their
occupants. Only emitted objects allocate value records. Access walks are
bounded by container depth, and item batches contain at most
two world events. Transient lookup, undo and reply allocations use the caller's
request scratch boundary. Compiler heap/time behavior is unchanged.
