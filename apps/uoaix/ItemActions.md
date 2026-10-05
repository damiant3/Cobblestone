# Item action admission

`ItemActions.ia-plan checkpoint tick request admission` returns a
`Result WorldAction Text` without changing the world. The request names
the authenticated mobile, source item, amount and a ground, container or
stack destination. Execute the returned batch through [WorldAction](WorldAction.md).
Kinds 1, 2 and 3 mean relocation, split and merge respectively. Preserve
the source serial on relocation, preserve the original remainder on split,
and retire the consumed source serial on a full merge. No coin is created
or destroyed by these quantity transfers.

The caller derives `ItemAdmission` from trusted item/catalog and world
rules, never from client flags. `movable` means the actor is authorized to
move this source, including equipment, lock, house and vendor restrictions.
`stackable` means splitting is permitted and, for a merge, both objects
share the same type and every non-WorldObject attribute: title, hidden mark,
quality, wear, provenance and coin denomination. `destination` certifies
the specific destination and transfer are authorized. For container moves
and merges into contained stacks, that includes container type, access,
remaining weight/item capacity and every ancestor's capacity. Ground and
ground-stack destinations require the ground/region checks below.
The primitive additionally checks matching graphic, hue and health for merges.

The owner must keep the world stable from planning through prepare and
commit. Range is Chebyshev distance at most two and z difference at most
16 at the outer ground object. Containment leading to another mobile is
refused; containment leading to the acting mobile is allowed. Traversal
is bounded by the WorldRecords depth limit. The actor must be a living
mobile. Source amounts, destination coordinates, serial generations,
stack overflow, cycles and split capacity are checked. A split or merge
cannot involve a container with children. WorldRecords checks deeper
descendant limits during prepare; planning success alone is not admission
to publish the effect.

For a ground destination, the caller must first validate support height,
collision, house/porch rules and the per-player drop budget against map and
region state. These are not provided by the distance check. Marked-item
keeping, crime witnesses, dropped-item tracking, cursor ownership and
drag-and-drop packet handling remain server integration work. Associated
metadata, quantity ledgers and trade lines must join the same enclosing
transaction before acknowledgement. The primitive is not a player-facing
authorization endpoint or a separate item store.

Planning retains only bounded object lookups and at most two events in
request scratch. Parent walks are bounded by depth 64; occupied-container
checks scan the existing world capacity. Applying the batch retains the
WorldRecords costs described in WorldAction. There is no world-sized copy
or per-item persistent allocation in this layer. Compiler heap/time behavior
is unchanged.

`proofs/ItemActionsProof.codex` exercises quantity conservation, stable and
retired serials, split/merge rollback, container movement, inaccessible
inventory, range, cycle, amount, admission and world-capacity refusals.
These native checks do not prove durable commit or client rendering.
