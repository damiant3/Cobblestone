# Classic moongates

Eight classic Britannia moongates (`GameMoongates.codex`): walk-in travel chosen by the Trammel and Felucca
moon phases, animated gate graphics and follower transfer, composed in
`CompositeGameRules` and graded by `proofs/GameMoongateDestinationProof.codex`.

## Reference facts

Source-X `CWorldGameTime.cpp` uses periods105 and840 game minutes and
`IMulDiv(remainder,8,period)`. `common.h::IMulDiv` adds half the divisor before
division for positive values. `CCharUse.cpp::Use_MoonGate` reduces each phase
modulo the gate count and selects `(source + Trammel - Felucca) mod8`.
Do not replace this with floor division. Time must come from the committed
GameClock and its game-minute fraction, not raw milliseconds or PIT ticks.

`CCharAct.cpp` invokes gates on movement rather than standing ticks.
`CCharSpell.cpp::Spell_Teleport` takes nearby actively following NPCs only
when they can reach the departure point; combat targets are not followers.
`CClientMsg.cpp::addPlayerUpdate` explicitly resets the server walk sequence
to0 because packet20 resets the client's sequence.

The gate order and coordinates are:

| Index | Place | x | y | z |
|---|---|---:|---:|---:|
| 0 | Moonglow | 4467 | 1283 | 5 |
| 1 | Britain | 1336 | 1997 | 5 |
| 2 | Jhelom | 1499 | 3771 | 5 |
| 3 | Yew | 771 | 752 | 5 |
| 4 | Minoc | 2701 | 692 | 5 |
| 5 | Trinsic | 1828 | 2948 | -20 |
| 6 | Skara Brae | 643 | 2067 | 5 |
| 7 | Magincia | 3563 | 2139 | 34 |

Coordinates and full animated blue graphic0F6C are in UOX3
`data/js/jsdata/worldtemplates/felucca_moongates.jsdata` and
`data/dfndata/items/gmmenu/moongates.dfn`. ServUO's PublicMoongate table agrees
on order/x/y; its newer Magincia z is map-derived. The server uses the classic 34.
Source-X `PacketEffect::writeBasicEffectLocation` supplies the28-byte70 XYZ
effect, default graphic3728; `PacketPlaySound` supplies12-byte54, sound01FE.
Retain Sphere-LICENSE.txt and ports/UOX3-LICENSE.txt attribution.

## Integration contract

At every boot `CompositeGameServer` calls `gmg-new game items combat map` after
magery and then `cg-gates-bind`, which finds each gate's 0x0F6C world item on
its tile or creates it, and registers it immovable; the gates are rebuilt each
boot rather than saved.

An admitted 02 walk calls `gmg-prepare cg-permit gates input map decoration
start seconds`, which answers an optional request-owned travel plan, then
`gmg-apply` with the plan and the walk's reply. The permit callback supplies each
traveller's current travel restrictions, and a party is one player plus at most
eight followers. Arrival cells are preflighted and a blocked party is refused
without relocation (a bounded adaptation of Sphere's per-pet calls). The
client-view pass calls `gmg-view` before publishing.

Followers: when the walk ends on a gate tile, `cg-gate-party` clears the
traveller's rows (`gmg-release`) and binds each live summon whose spawn slot
names the traveller as owner (`gmg-bind`); `gmg-collect` then takes the bound
followers within 18 tiles that can reach the traveller. An uncontrolled summon
has no owner and stays behind. The follower table is rebuilt for every trip and
the gates every boot, so moongate state is not saved.
`proofs/MoongateTravelProof.codex` grades a trip with a controlled daemon and
an uncontrolled blade spirit on one fresh disk.

Cost: fixed gate, follower and view records, one reused TlPath workspace and a
256-byte path buffer; follower path searches keep the 4096-expansion, 256-step
bound, occupancy uses tile chains, and arrival checks are bounded by the party
and table sizes.
