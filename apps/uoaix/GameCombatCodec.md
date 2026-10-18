# Combat checkpoint component

`cbc-size capacity` gives the exact byte count.
`cbc-encode combat buffer capacity` returns the written count.
`cbc-decode combat buffer size` restores into an already allocated
`cb-state` of the same capacity. Both return `Result Integer Text`.

UCB1 has six qwords: magic, version, actor capacity, RNG seed, draw cursor,
scene state. Each actor row has thirteen qwords in this order: serial,
maximum, strength, dexterity, skill, tactics, anatomy, low, high, body,
corpse, NPC flag, immune flag; then thirty ASCII name bytes and two zero
bytes. Integers use the existing little-endian buffer primitives.

Decode checks the complete format and every row before mutation. It copies
names into retained buffers and resets war, target, due, connection, tick
and visibility cursor. RNG, profiles and corpse references survive.
The encompassing owner supplies checksum integrity and atomic restoration
with the matching world. It must validate serial/world associations before
publishing a restored checkpoint. This component does not commit storage.

Cost: O(actor capacity) encode and validation plus restore, fixed caller
buffer, no new retained allocation.
