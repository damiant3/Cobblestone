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
resource9 for fishable water, or0 to refuse. Check installed map/static facts,
target height, mining/wood line of sight and restricted/multi regions.
The supplied graphic is a client hint, never authority. The original WalkMap
height/flags representation cannot distinguish all rocks and trees. Fester's
map/cache adapter must supply these facts before claiming live acceptance.
The native replay uses explicitly configured server terrain facts.

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
the player's own feet. The adapter uses a three-second action cooldown.
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
iron ore, logs and timber seed, or fish. No coin is created. The existing
gathered/consumed census remains authoritative. Physical lots are movable
but do not split/merge through generic item handling; vendor lot-aware
transactions retain their existing behavior. This preserves serial
provenance until generic inventory operations also update GvLots.

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
player-serial/remaining-tick pairs and x/y/z/resource/node rows.
Decode checks all bindings before mutation, rebases cooldowns, and clears
tool targets, expiry and connection authority. The enclosing checkpoint
supplies integrity. Source regrowth, skill levels and tool/material stock
are already owned by the economy codec, not duplicated here.

Retained metadata is fixed by the five-player/256-source budgets. Lookup is
O(source count); tool/lot lookup is bounded by64. Candidate allocation and
copy cost are bounded by fixed economy table sizes, with no retained
request allocation. Codec duplicate checks are O(source count squared)
inside the256 limit. No compiler or seed change.
