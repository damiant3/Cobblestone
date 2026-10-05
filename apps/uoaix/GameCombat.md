# Live combat port

`GameCombat` and `GameCombatPackets` are modified Codex ports of SphereServer
Source-X, copyright 2026 SphereServer development team, Apache License 2.0.
[Sphere-LICENSE.txt](Sphere-LICENSE.txt) carries the license. The upstream
[source](https://github.com/Sphereserver/Source-X) is retained locally at
`D:/Projects/uo-reference/Source-X`.

Ported paths are `src/network/receive.cpp` (attack, combat-mode and status
requests), `src/game/clients/CClientEvent.cpp` (combat mode and attack),
`src/network/send.cpp` (combat/status/death packets), `src/game/CResourceCalc.cpp`
(pre-AOS swing speed and hit chance), and the physical damage/death paths in
`src/game/chars/CCharFight.cpp` and `CCharAct.cpp`. Codex records replace Sphere
objects and event dispatch; registered callbacks return packets through
[GameNet](GameDispatch.md), never raw socket writes. No GPL implementation is
included in this port.

## Wire and flow

Multi-byte fields are big-endian. `cb-specs` declares fixed client packets:

| Opcode | Size | Handler |
|---|---:|---|
| 05 | 5 | Target serial32; accept a live non-self target or acknowledge zero |
| 72 | 5 | War Boolean and three skipped legacy bytes; reply `72 mode 00 32 00` |
| 34 | 10 | Skip marker32, request type8, target serial32; type4 is status, other types leave fallback state untouched |
| 2C | 2 | Death-menu response; nonzero resumes ghost play and never grants resurrection |

Mode changes clear the actor's combat target. Attack requests bind the active
session actor, enter war mode when needed and return AA plus the accepted
target serial (five bytes). A repeated request for the same target does not
reset or bypass its swing deadline. Missing/non-mobile targets follow Sphere's
no-response path. Invalid lengths, non-byte input and unauthenticated calls
return diagnostic errors instead of indexed access.

Pulses use the owner's explicit tick and recheck live identity, protection,
adjacent reach and terrain before every strike. Replies use 2F swing (10 bytes),
6E animation (14), A1 health (9), AF death (13), 1A corpse and 1D removal (5).
A1 sends max then current; the owning player gets actual HP, other viewers get
percentages out of 100. Foreign 11 status is the 43-byte Sphere legacy form
with current/max percentages, rename false and version1. Own status retains
the existing 66-byte character packet. Incoming mobiles use 78 (23 bytes with
empty equipment), and ghost player updates use the existing 20 layout.

The server uses Sphere's pre-AOS formulas with wrestling speed50 and scale15000,
plus the caller's one-second animation floor. Current native stamina is the
character's initial DEX value. Hit chance uses both wrestling skills plus500.
Damage uses the pre-AOS tactics, anatomy and strength bonus; NPC bonus damage
is enabled. Starting skill/stat values come from the admitted character record.
Sphere tenths are converted upward to raw PIT ticks using `pit-input-hz` and
`pit-count`; `get-ticks` is not a millisecond counter. The provided owner tick
and every stored deadline use that same unit.
The current profile is unarmed physical combat with no armor/magic modifiers;
weapon and live skill/stamina adapters must supply their actual records before
those systems can claim integration. The server's seeded mixer supplies draws.

## Death and retained state

Health and corpses mutate the authoritative `WorldTable`. A corpse is graphic
2006 hex; its amount field carries the former body ID, matching Sphere's corpse
wire convention rather than a stack quantity. Treat corpses as non-stackable
and non-movable containers. Direct child items move into the corpse; the four
recorded starting equipment serials stay with a player. No new loot or coin is
generated. NPC mobiles are removed after the death packet. Players retain their
serial with zero HP, ghost body402/403 and hue0. The 2C response cannot heal them.
AF is sent only to observers, never to the dying player's connection, matching
Source-X `CCharAct.cpp`'s `UpdateCanSee(PacketDeath, m_pClient)` exclusion.
The owning player receives the corpse, 2C death menu and ghost update.
GameResurrection registers corpses from either the observer AF or the 1A
world-item packet, checking the authoritative item's corpse graphic.

Combat metadata is retained per world slot: copied fixed name bytes, stats,
war/target/deadline, template body and connection identity. Packet lists, reply
text and temporary world records remain request scratch. No packet-owned name
or list is retained. Reconnection clears that player's old target and attackers'
references. Corpse/health world records fit the existing world delta; combat
metadata has the bounded [GameCombatCodec](GameCombatCodec.md) component.
Complete action commit/recovery still needs the shared persistence owner.
`GameTransaction`'s stage-1 footprint alone does not cover this family.

Lookup is constant-time by world slot. Combat keeps a bounded active-attacker
index; ordinary pulses visit O(active attackers), and packet batches flatten
once in O(reply packets). The index stays in world-slot order so simultaneous
strikes preserve their order. Admission/removal shifts O(active attackers);
death can also remove attackers targeting the victim. UCB1 restore clears
the transient index along with targets. Use cb-select/cb-stop for target changes.
Connection-entry views traverse the indexed tiles within18 squares, clipped
to map bounds, and their uncontained occupants. Cost is O(visible tiles plus
occupants), independent of distant world capacity; iteration adds no retained
allocation. Eligible live NPCs and ground corpses retain their packet formats.
Repeated war packets on that connection do not resend or rescan the scene.
Corpse transfer walks indexed direct children, saving the next sibling before
reparenting. Each move retains WorldRecords' parent/depth validation costs.
There is no per-pulse world clone. Persistent memory is one fixed metadata row
and a 30-byte name buffer per world slot, plus two fixed qword index arrays;
active snapshots and packet batches use bounded request scratch.
Compiler heap/time and seed behavior are unchanged.

## Entry and acceptance

Compose `cb-handler combat` on `None` with other lane handlers, then use
`gn-route`. Compose `cb-pulse combat` through the same owned pulse/reply path.
Both require an active authenticated game session. Construct `cb-state` below
the connection arena with capacity matching the shard world; `cb-new shard seed`
is the convenience constructor when the shard already exists. `cb-profile`
configures retained NPC data, not client-granted capabilities.

`proofs/GameCombatServer.codex` targets host/guest port2593. Root launches one
lane image there at a time for the real client's existing LOGIN.CFG. Its
fixture creates a weak sparring partner and a
strong veteran on two reachable neighboring tiles after world entry. They are
passive until attacked. The entry uses `GameCombatDress.cbd-server`: body400,
skin hue1002, and registered shirt, trousers and shoes through
`GameMobileBank`. `cbd-handler` and `cbd-pulse` rewrite 78 replies with
`gmb-reply`; the composite supplies its shared GsiState. Clothing transfers
to the corpse with existing inventory custody. Repeated pulses reuse it.
The dressed pulse captures registered layer29 bank ownership before combat
and restores those banks to their player before returning replies. Nested
bank contents remain in that same box. The encompassing transaction must
commit the world and item metadata together; a refused pulse requires rollback.
The custody scan visits the five player owners' direct children; saved bank references use O(bank count)
request scratch. Restoring changed parents uses existing bounded world
validation and adds no retained state.
The standalone allocates item metadata before connection scratch and binds
its world on the first authenticated pulse. Retained cost is O(capacity);
the fixture clothing scan visits root objects and rewrites returned packets.
This fixture is enabled only by the combat server entry;
ordinary `cb-state` starts without it.

`proofs/GameCombatReplay.codex` replays admission, mode changes, target/health
bytes, timer enforcement, NPC death/corpse, player death and ghost response
against a flat synthetic map. Reek reviews the handler seam. The real 1.25.32
client run is driven by root. On 2026-10-04 root graded war mode, attack,
partner death and the visible corpse PASS on server SHA256
`99F5283F225B777BD5F6DECDBC21D9C311D144CE25DE0006B3B1BD4AD747095F`.
Root's 2026-10-05 complete-client grade on image `0A4A8453` crashed at player
death with `c0000005` at `CLIENT.EXE+0x44649`. Self-directed AF violated the
reference recipient contract. MAIN35648 contains the reviewed correction and
Fester's composite four-boot proof passed. The complete-client death retest
remains pending; native replay alone does not establish that the crash is fixed.
Root also observed a pre-world-entry 72 closing the connection;
MAIN35439 supplies the dispatcher's log-and-ignore correction.
Root's 2026-10-04 real-client grade accepted the dressed fixture image
`AED09768B882E0CDA4F8C4C6F5C719186C81FC0F65CF04A81BB36A8C9BDE814C`:
both NPCs display normal skin, green shirts, trousers and shoes.
No poison or reader-subagent pass is part of this UOAIX packet-family grade.
