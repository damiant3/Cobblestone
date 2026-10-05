# World object table

`WorldRecords.codex` stores mobiles (kind 1) and items (kind 2).
One caller owns a `WorldTable` from `world-new capacity`; aliases share the
same mutable storage. Do not run concurrent writers or treat an alias as a
snapshot. NPC persona and schedule state belongs to Townsfolk; the link is
the world mobile serial.

`WorldObject` fields are serial, kind, x, y, z, graphic, hue, container,
amount and health. World coordinates are bounded by the legacy map, z is
-128 through 127, graphics are below 16384, hue/health are 0 through 65535,
and amount is 1 through 65535. A mobile has amount 1 and no container.
Game rules above the table decide which items can serve as containers.
The table checks referential integrity, not map collision or combat rules.

Create a template with serial zero and call `world-create world template`.
Success yields the allocated serial. `world-get world serial` returns a
detached value record. Construct a replacement record with that serial and
call `world-update`; kind cannot change. `world-delete` refuses a target
record that still contains children. `world-at world slot` enumerates slots from
zero through capacity minus one and returns Err for empty slots.
Propagate every Result error; refusal leaves the table unchanged.

Mobile serials use the low 30 bits. Item serials also set bit 30. The low
bits identify both a slot and a generation; deleting and reusing a slot
increments its generation. An old serial therefore cannot address a new
object. Exhausting a slot's serial generations refuses creation; restarting
must preserve generations, including tombstones, rather than reset them.
Containers must exist, cannot form cycles and have at most 64 ancestors.
Calls outside these public operations, including direct raw writes or the
slot helpers, bypass invariants and are reserved for validated persistence.

`WorldTable` contains buffer, capacity and index. The canonical buffer still
uses80 bytes per slot and a16-byte header. Index bytes are never serialized.
Capacity remains bounded at16384 and never changes in place.

| Query | Result |
|---|---|
| `world-child-count world serial` | Direct child count; serial0 means ground/root objects |
| `world-first-child world serial` | First child serial, or0 |
| `world-next-sibling world serial` | Next serial in the same child list, or0 |
| `world-layer-serial world owner layer` | Registered serial for the layer, or0 |
| `world-tile-first world x y` | First uncontained object at the tile, or0 |
| `world-tile-next world serial` | Next uncontained object at that tile, or0 |

Save the next serial before moving or deleting a child during iteration.
Layers1 through25 have direct owner tables; layers26 through29 walk only the
owner's children. `world-set-layer world item layer` registers an authoritative
item definition; it returns0 or a negative refusal. GSI owns persistence of
those definitions and registers them after world recovery. Duplicate visible
layers select the lowest live slot, matching bounded slot enumeration.
`world-touch` marks other persisted metadata changes for the encompassing
owner. `world-revision` and `world-item-revision` are volatile change counters,
not durable journal sequence numbers.

Create/update/delete maintain child counts, sibling lists, subtree heights,
layer bindings and tile occupancy. Serial, child-count, visible-layer and
tile-head queries are O(1); iteration is O(children or occupants). Parent
admission checks at most64 ancestors plus the stored subtree height. Height
reduction may scan affected direct children; ordinary movement does not scan
capacity. Tile pages come from a reserved free pool and are recycled when
empty. There is no retained allocation per movement or lookup.

The runtime index reserves `128 + 1572864 + capacity * 576` bytes, including
the fixed block directory, per-slot arrays and bounded tile-page pool.
Construction is linear in that reserved storage. Rebuilding is O(capacity
times the64-ancestor bound), without a capacity-squared child search.
`world-reindex world` rebuilds derived indexes after validated raw restoration;
failure leaves the index unavailable for mutation. Read-only codec views use
index0. Snapshot, journal and undo readers rebuild before publishing a mutable
world. Existing layer registrations survive same-world rollback; decoded
worlds receive their authoritative registrations from GSI.

A read allocates one WorldObject plus a Result; mutations allocate only their
Result. Consume these inside a scratch mark and retain the world below it.

`proofs/WorldRecordsProof.codex` is a focused native proof. Compile with an
explicit depot kernel and compare its entire filtered output against
`proofs/WorldRecordsProof.expected`, removing CR only. The proof fills an
8192-slot table with no retained transient heap between inserts, and grades
capacity, namespaces, generation reuse, references, cycles and refusals.
The population is synthetic storage acceptance, not a populated Britain
or stage-2 gameplay acceptance. `WorldIndexProof.codex` adds layer, child and
tile maintenance, page recycling, snapshot rebuild and rollback checks.
The table alone is volatile; WorldDisk and the encompassing codecs own storage.
