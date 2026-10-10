# Live monster base

`GameMonster` ports Sphere Source-X `CCSpawn::GenerateChar`,
`OnTickComponent`, `NPC_Act_Wander`, `NPC_WalkToPoint` and melee
pursuit in `NPC_Act_Fight`. Apache attribution is in the chapter.

`gm-new combat count x y z radius` creates one home with one to eight
monster slots. Allocate it before the connection scratch mark.
`gm-pulse monsters` has the GameDispatch pulse signature and includes
`cb-pulse`; the composite must not invoke the combat pulse a second time.
Register `cb-specs 0` and route `cb-handler combat`. No additional client
opcode or raw send is introduced.

The home fills vacant slots with orcs on reachable, unoccupied neighbour
tiles. Each orc enters WorldRecords and gets a combat profile. A dead
serial waits sixty seconds before replacement; slot reuse cannot revive
the stale serial. No gold or equipment is created.

Wandering keeps Sphere's stop probability and adjacent heading turns.
Blocked steps use its weighted turn choice. Every accepted step uses
WalkMap and remains inside the home's radius. Movement delay follows
Sphere's walking formula at movement rate 100, clamped to 100 through
5000 milliseconds and converted to raw PIT ticks. A nearby living active
player inside the home territory becomes a combat target; melee pursuit
stops at reach. This single-player base does not implement full pathfinding,
breeding, food, dungeon overflow, raids or NPC victim selection.

Replies carry 0x78 appearance, 0x77 movement and 0x1D removal. Reconnect
refreshes visibility. The standalone server configures three orcs at
(1426,1700,0), radius six, using the real MUL map window.

The encompassing owner commits world, combat and monster state (UMC1,
GameMonsterCodec.md) before publishing replies.

Cost: fixed retained slot records, O(monsters * world capacity) occupancy
checks, with at most eight monsters and two movement attempts each.
Packet and WorldObject allocations belong to the caller's scratch arena.

`proofs/GameMonsterAggressionReplay.codex` grades pursuit and melee damage
on the standalone three-orc home.
