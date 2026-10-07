# Classic moongates: resting state

Shelf35637 is an uncompiled draft. Native replay, review, composite integration
and real-client grading are NOT RUN. Root ordered wrap-up on2026-10-05; do not
land this draft as green. On resumed GO, moongates precede the live stage N seam.

The requested unit is eight classic Britannia moongates, walk-in travel chosen
by the Trammel/Felucca moon phases, animated gate graphics and correct follower
transfer. Only fester's complete composite is graded by root.

## Preserved draft and resume

The shelf adds `GameMoongates.codex` and `GameMoongatesCodec.codex`. A second
copy is under `D:/Projects/Cobblestone-red/build-output/uoaix/moongates/paused-source`.
The red workspace has merged through the canonical composite/death landing
MAIN35648. Refresh main and read the inbox before resuming; the active client
is `BigWhite_Codex_red`, stream `//Codex/red`.

The death-code delta is MAIN35648; its updated replay assertions/documentation
are MAIN35656, with combat and resurrection replays PASS against current main.
The client crash retest is still pending. Depot seed whole-file SHA256 verified
at this wrap is
`4228CD5103DC45232EB40C1FC9DE655BE2A805F7CAA5019E3EC76B28FF9216A8`.
No new full-battery/BVT claim is made. There are no owned guests or listeners.

After protecting any intervening edits, follow PerforceProcess.md's merge and
isolation procedure. Unshelve35637 into35637, inspect the result against the
current shared APIs, then create `proofs/GameMoongatesReplay.codex`. No replay
source exists yet. Read build scripts before running them; compile with
explicit `-Src`, `-Out`, `-Log` and `-Kernel seed/Codex.cdx` and run serially
after a fresh RAM check. No poison or reader subagent is required for this
UOAIX unit. No new unit should start during the wrap.

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
on order/x/y; its newer Magincia z is map-derived. The draft uses classic34.
Source-X `PacketEffect::writeBasicEffectLocation` supplies the28-byte70 XYZ
effect, default graphic3728; `PacketPlaySound` supplies12-byte54, sound01FE.
Retain Sphere-LICENSE.txt and ports/UOX3-LICENSE.txt attribution.

## Draft integration contract to finish

Allocate `gmg-new game items combat map` below request scratch, and install
once with `gmg-install` on a private candidate. Restore the codec instead of
installing again. The draft creates eight immutable GSI/world gate objects.

Capture the authoritative mobile before an admitted02 walk. After the walk,
call `gmg-prepare permit state input sourceMap decoration before gameSeconds`.
It returns an optional request-owned travel plan. Prepare follower reachability
before changing the map window. Only server-verified active follow bindings
may be added with `gmg-bind`; there is no player-facing pet-command endpoint.
The required permit callback supplies current travel restrictions for each
traveller. The draft bounds a party at one player plus eight followers.

The composite must load the destination with `wmc-cover`, rebind decoration,
then call `gmg-apply` before its encompassing commit. The draft preflights
distinct arrival cells and refuses a blocked party without relocation.
That atomic-party policy is a bounded adaptation of Sphere's per-pet calls
and needs review. Reconcile the cache to the actual player position on either
outcome. Force immediate commit when the gate sequence changes; do not leave
teleport on the ordinary02 walking save timer. Then run GameClientView and
`gmg-view` before publishing. Preserve the current client-safe mobile culling.

UMG1 records gate IDs, follower bindings and a sequence; GameClock, world,
GSI and combat remain separately owned components. Restore them first.
The draft decoder validates all records before mutation and resets views.
Call `gmg-tidy` after deaths/deletions before encoding. Check this lifecycle
and the cache/commit coupling in the composite rather than assuming it works.

Required replay: eight bindings and visibility, rounded phase boundaries and
full-period permutations, standing/turn/auth refusal, follower reachability
and unrelated-NPC exclusion, blocked arrival without partial movement,
inventory/serial preservation,20/70/54 packet fields, walk-sequence reset,
stale-plan refusal and malformed/restart codec cases. Then root grades all
eight destinations and visible travel in the real1.25 client.

Draft cost inspection: fixed gate/follower/view records, one reused TlPath
workspace and256-byte path buffer; follower path searches have the existing
4096-expansion/256-step bound. Occupancy uses tile chains. Arrival and binding
checks are bounded by the party/table sizes; combat target cleanup retains
the shared active-attacker cost. No timing or heap measurement has run.
