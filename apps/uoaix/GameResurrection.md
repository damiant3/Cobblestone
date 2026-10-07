# Player death and ankh resurrection

GameResurrection adapts Sphere Source-X CClientUse's IT_SHRINE case and
CChar::Spell_Resurrection, under Apache-2.0. The source restores body/hue
and at least one HP using HitPointPercentOnRez, whose default is10.
This shard uses that ten-percent rule. Corpse loot remains in the corpse
for the living player to recover; resurrection does not reclaim or mint it.

Allocate `gr-new combat items` with the composite's shared GameCombat and
GsiState before connection scratch. During trusted world setup call
`gr-install state x y z` once to create the immovable south/north ankh
halves (graphics2/3). The owner chooses map-admitted coordinates. Each half
is a registered world object. Failure requires the encompassing transaction
to roll back world and metadata together.

Route `gr-handler state` before other C06 handlers. Add fixed opcode06,
size5, once to the composite registry; it already exists in gsi-specs.
Unknown targets return None without mutation. A shrine double-click requires
the active authenticated player, a still-valid registered shrine, a dead
player, and the existing two-tile/sixteen-height touch range. A living player
cannot repeatedly heal. Restoration clears combat target, war and attackers'
references, restores the saved human body and character skin, and sends
player redraw, equipment, status and HP synchronization.

`gr-pulse state` wraps cbd-pulse. Use it instead of a separate combat pulse.
For a composite already composing monster/combat pulses, call `gr-after
state shard input result` on that combined result once; do not execute
combat twice.

The post-pulse adapter registers corpse containers with gump09, so the
existing C06/C07/C08 item handlers can open and recover real corpse loot.
A death outside that pulse (a spell or poison tick in the composite) passes
its reply through `gr-corpse-packets` for the same registration.
Ghosts cannot use those item handlers. Banks keep their player ownership.
It emits full own status11 plus A1 when HP or connection identity changes;
the existing combat A1 stream still covers individual hits. The pulse log
includes current HP. Apply GameClientView after commit with the owner's
authoritative `gv-status-packet`, replacing the bare character status so
the displayed coin value remains correct. That adapter also reconciles
death/resurrection season and light. Shrine appearance tracks the player's
view range and reappears when returning. The real-client bar rendering is
graded in Fester's composite by root.

USR1 is a32-byte component: little-endian qwords magic, version1, first
ankh serial and second ankh serial. `gr-encode state buffer capacity` and
`gr-decode state buffer size` return Result Integer Text. Decode requires
the world and GSI1 already restored, validates both live shrine identities
before mutation, and resets the transient health/connection cursor.
Combat state still uses UCB1; this component adds no duplicate combat or
inventory snapshot. The encompassing codec supplies checksum and commit
atomicity. Install the shrine before the first checkpoint.

Retained state is one fixed record. Corpse registration scans O(world
capacity) on connection entry, then only the returned death packets.
Appearance uses the existing bounded equipment scan. Requests
retain no packet lists. No compiler or seed change.

The native GameResurrectionReplay covers lethal combat, ghost continuation,
corpse custody, range refusal, ankh recovery, repeated-heal refusal, real
item-packet looting, HP/status updates and shrine codec recovery. Real-client
acceptance and encompassing restart grading remain with the composite owner.
