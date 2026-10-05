# GSI1 item metadata recovery

`GsiCodec.codex` exposes `gic-size state`, `gic-write state buffer length`
and `gic-read restoredWorld buffer length`. Length is exactly
16 + 40 * world capacity. GSI1 is little endian: qword magic31495347,
qword capacity, then one row per world slot: serial, layer, gump, movable,
stackable (five qwords). Boolean fields are 0 or1. Dead/unregistered rows
are all zero. Decode validates the complete image against the restored
world before allocating state; serials, item kind, field bounds and
stackable constraints must agree. The trusted disk envelope supplies integrity.

Decode and reindex the mutable world first, then metadata, below the receive
scratch mark. Index0 codec views are refused; decoded definitions populate
the world layer index through idempotent GSI registration.
Cursors, target nonces and connection identifiers are not serialized.
The codec owns no disk I/O or commit. The encompassing transaction captures
the world, metadata and character equipment cells before publication.
Skill values remain in the existing character record; this adapter does not
create a second skill store.

`gsi-pack` finds an existing 0E75 backpack under the player before creating
one. Run `gsi-after-entry` once, then give its actor.pack serial to
`gv-add-player`; do not invoke the standalone vendor demo's second pack
creation. Vendor06 precedes general item06 in callback composition.

`proofs/GsiCodecReplay.codex` grades roundtrip and authority reset, truncated
extent, invalid layer, stale serial generation and stackable-equipment refusal.
Durable image save/reload remains the encompassing owner's grade.

Encode/decode are O(world capacity). Encoding uses caller-owned storage
without allocating row copies. Decoding allocates the existing GSI retained
metadata rows and five empty cursor records after validation. Disk storage
is 40 bytes per slot plus16. Compiler heap/time behavior is unchanged.
