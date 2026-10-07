# Live resource harvesting

GameHarvest adapts Sphere Source-X CClientUse/CClientTarg tool targeting and
CCharSkill mining, lumberjacking and fishing admission. Apache-2.0 attribution
is in GameHarvestState. Local reference paths are CClientUse.cpp,
CClientTarg.cpp and CCharSkill.cpp. Item and skill IDs follow the Source-X
enums; the seed-pile graphic0DCF is a format fact also used by ServUO Seed.
No ServUO implementation is copied.

## Composite contract

Allocate `gh-state vendor items` with the shared GvWorld and GsiState before
request scratch. Route `gh-handler lookup gh-produce state` before general
item handling. The existing GSI registry already declares C06 fixed5,
C6C fixed19 and C34 fixed10; do not add duplicate opcode declarations.
C34/type5 returns classic skill rows with production mining, lumberjacking
and fishing values. Other skill values retain the character's admitted
starting values.

`lookup map mobile x y z graphic -> Integer` is the owner's trusted map
adapter. Return resource5 for mineable rock/mountain, resource15 for a tree,
resource9 for fishable water, or0 to refuse. The composite's adapter is
`cpr-lookup` (CompositeProduction): a land target (graphic 0) is judged by its
x,y alone, as ServUO reads `GetLandTile(x, y)`, and answers 9 when the cell is
wet and 5 when it carries the mountain mark; its source binding is keyed on
the map's land z, never the client's. A static target answers only when a collider at that cell matches
the claimed graphic's installed tiledata flags, height and z; trees are 15,
cave rock 5, wet statics 9. Line of sight and multi regions are not checked.

The player must already have a production actor and owned backpack in
GvPlayer. Tools must be real single-unit GvLots, GSI-registered, owned by the
active living player, and usable: catalog25 pickaxe,26 axe,29 fishing rod.
At both C06 selection and C6C completion, tools must be inside the bound
backpack or directly equipped on a visible layer. Bank descendants are
refused even while the bank is open; depositing a tool invalidates its
pending harvest target.
Produce tools through the existing economy/recipes and materialize their
real inventory into the pack; the handler never grants starter tools.

C06 returns a ground-enabled cursor with a separate HA context namespace.
C6C is single-use, bound to character, connection and tool, and expires after
ten seconds. Mining/wood use range2; fishing uses range4, with no target under
the player's own feet; reach is two-dimensional, as ServUO `InRange`. Every
refused target logs its x,y,z and tile id in the reply note. A swing plays
ServUO's effect sound (0x13E chop, 0x125 or 0x126 mine; fishing none).
The adapter uses a three-second action cooldown.
Cooldown survives reconnect/restart; an old target cannot complete after
either. Client coordinates cannot create a source unless lookup validates it.

## Production and custody

Validated locations bind to shared EconomyHarvest nodes. Depletion and
regrowth remain in the production state and use its game-hour clock.
The handler maintains at most256 source bindings and five request slots.
Resource-node budget refusal creates no stock.

`gh-produce` clones only the actor inventory/skills, target node, relevant
counters and actor/node reference tables. It isolates the selected one-unit
tool, calls eh-harvest, then recombines the other tools. A failed skill attempt
still applies practice and wear. A depleted or invalid source does neither.
The selected physical tool loses one charge or is retired; other tools keep
their charges. Retiring a lot compacts the bounded lot table and invalidates
vendor quotes. All harvesting attempts that change inventory invalidate
quotes before publication.

Output enters real WorldObjects in the player's backpack and exact GvLots:
iron ore, logs or fish. A chop yields logs and the economy's timber sapling
(item 56) as a sapling-art (0x0CE9) backpack item; other seeds are taken back
out of the candidate. No coin is created. The existing
gathered/consumed census remains authoritative. Physical lots are movable
but do not split/merge through generic item handling; vendor lot-aware
transactions retain their existing behavior. This preserves serial
provenance until generic inventory operations also update GvLots. A yield
merges into a backpack stack of its grade (the rule is in `GameCraft.md`);
an unmerged yield lands at a random point of the backpack gump's
`containers.cfg` rectangle, as ServUO `Container.DropItem` places it.

## Carving

`GameCarve` ports Source-X `Use_CarveCorpse`: a knife (item 27, skinning or
butcher knife art), a dagger (item 69) or the hatchet (item 26, art 0x0F43
only) used on a corpse within two tiles carves it once; swords, the lumber
axe and other bladed weapons do not (Damian 2026-10-06). A knife or dagger
opens its own cursor; the hatchet carves through the harvest cursor when its
target is a corpse. The testing kit binds its knife and dagger as lots.

`GameScissors` ports UOX3 `scissors.js`: scissors (item 74) open a
cursor, and the player's own hides lot in the backpack becomes cut leather
(item 35, art 0x1081) one for one, consumed and crafted in the economy so the
census holds, with sound 0x248 and UOX3's 6029, 6030, 6035 and 6036 lines.
Cloth (item 37) becomes clean bandages (item 75, art 0x0E21, 6034) the same way. Scissors on a wooly sheep (body 0xCF) within 3 tiles port `sheepshearing.js`: the body becomes 0xDF and the shearer gets 2 wool (item 12, art 0x0DF8) as found goods (`ep-find`, no cost basis) through `gh-place`, with 1773, 1774 and 461. The spawn tick regrows a shorn sheep with one roll in 60 per action (`gws-regrow`), the spawn codec accepts 0xDF under a 0xCF profile, and `cvd-carve` carves 0xDF as 0xCF. The testing kit's materials are bank items, not lots, so its cloth cannot be cut. The corpse's body selects the
parts from `CreatureCarveData` (UOX3 carve tables, meat and hides only),
which the composite passes in as the `parts` hook. The parts enter the
economy as gathered meat (8) and hides (7) with hunting (skill 3) practice
and tool wear, exactly as a harvest does, and land inside the corpse as
graded lots owned by the carver. The corpse then holds health 1, and carving
it again yields nothing. `proofs/GameCarveReplay` grades the goat, the axe,
a human corpse, reach and a non-corpse target.

A blade on fish (art 0x09CC-0x09CF) in the carver's own backpack ports UOX3
`sword.js` MakeFishSteaks: each fish becomes 4 raw fish steaks (art 0x097A)
with 9338, and fish anywhere else answers 775. The economy has no steak item,
so the steaks stay fish (item 9) at the lot's grade and the 3 new units per fish
are crafted. A harvest yield merges only into a stack of its own output art
(`gh-stack-art`), so a new catch never joins the steaks.

World, production, vendor lots, GSI metadata and harvest state form one owner
transaction. Do not publish replies before commit. A late Err requires
rollback of that encompassing candidate; the helper is not a storage commit.
Preflight checks output world/lot capacity before publishing production.

## Recovery

UHC1 persists source/node bindings, next target nonce and remaining player
cooldowns. Restore world, economy, GSI and vendor/player bindings first,
then `ghc-decode state now buffer size`. Encoding is
`ghc-encode state now buffer capacity`; `ghc-size state.count` supplies
the required size. Both return Result Integer Text.

The header has magic/version/count/nonce qwords, followed by five
player-serial/remaining-tick pairs and x/y/z/resource/node rows. A pair whose
serial no longer holds its character slot (another account is bound) is
written as 0/0, so an account switch never refuses the encode.
Decode checks all bindings before mutation, rebases cooldowns, and clears
tool targets, expiry and connection authority. The enclosing checkpoint
supplies integrity. Source regrowth, skill levels and tool/material stock
are already owned by the economy codec, not duplicated here.

Retained metadata is fixed by the five-player/256-source budgets. Lookup is
O(source count); tool/lot lookup is bounded by64. Candidate allocation and
copy cost are bounded by fixed economy table sizes, with no retained
request allocation. Codec duplicate checks are O(source count squared)
inside the256 limit. No compiler or seed change.
