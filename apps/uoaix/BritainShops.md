# Britain shops

`BritainShops` supplies `bb-shop-count` (six) permanent shop bindings for the
composite server. The first six `GvWorld.vendors` rows are provisioner,
blacksmith, tailor, baker, healer and innkeeper, in that order. Positions come from the reference spawn regions;
placement chooses a clear surface within each region using the supplied map.

| Shop | Reference region | Initial stock |
|---|---|---|
| Britains Premier Provisioner | 1465..1470, 1672..1674 | bag, dagger, bread |
| The Hammer And Anvil | 1417..1420, 1544..1550 | dagger, longsword, plate gorget |
| The Lords Clothiers | 1467..1471, 1684..1689 | shirt, short pants, folded cloth |
| Good Eats | 1449..1454, 1609..1618 | bread, pizza |
| Healer Of Britain | 1470..1477, 1608..1614 | clean bandages, scissors, folded cloth |
| Sweet Dreams Inn | 1496..1498, 1615..1617 | bread, pizza |

These are the admitted initial stock types, not the upstream shard's entire
catalog. `BritainCatalog` adds eight leaf types and recipes after the existing
metal catalog. Every type passes the harvestable/recipe closure check. Prices
come from `ep-price`; no upstream restock count or fixed price is imported.
The healer is a shop binding; this chapter does not add resurrection behavior.

## Testing-mode bank lineup

A composite booted in testing mode (`UOAIX TESTING`) on a Britain world stands
one of each townsfolk role in the row south of The First Bank of Britain's
front doors (1438-1439,1692): x 1434-1441 at y 1694, each tile moved one row
south when not walkable. Vendors 6-11 are trading copies of the six shops in
role order, built by `bb-populate-at` with their own actors, loans and
production after the royal actor mints one more gold ingot; then a banker and
a resident stand-in in the banker's dress, who answers with the first
resident's name and greets through that resident's persona. The copies are immune to combat. A testing boot
re-derives the same lineup until the first ordinary commit stores it; a world
that stored it keeps the copies in normal mode, where the banker and resident
copies are not bound. A lineup that cannot be built stops the boot before any
commit.

## Composite owner API

On a new installation, before publishing the candidate world:

```text
bb-economy 0 -> Result EuCurrency Text
gv-new game currency -> GvWorld
gsi-new game.world -> GsiState
bb-populate vendors items cachedMap -> Result BbTown Text
```

Use at least 128 world slots and a cache covering the six regions. The
recommended window is x=1408, y=1536, width=128, height=192. Installation builds
the local cache; server boots read it. This module performs no MUL reads and
ships no map or art data. A failed bootstrap may have changed its private
candidate; discard that candidate. It must not be retried on published state.
The initializer refuses an existing vendor registry or non-fresh economy.
It does not erase or migrate an existing shard.

Bind `bb-handler town` through `gn-route`, before generic item/mobile use and
name handlers. It claims only these vendors' 09 names and the existing vendor
family; unrelated traffic returns None. `bb-specs` includes the vendor bounds
and fixed 09. The owner deduplicates shared framing declarations as described
in `GameDispatch.md`. Call `bb-pulse town` for visibility. It draws equipped
NPCs when they enter the 18-tile view and removes them when they leave; it does
not resend every NPC every tick. Pulse visibility is transient, not a durable
world action. Replies still follow the encompassing owner's commit boundary.
If the composite uses `GameClientView` for all mobiles, use that single viewer
instead of also emitting `bb-pulse` draws for the same NPCs. Shop name/use and
trade handling still belong to `bb-handler`.

The owner binds an authenticated player's economy actor and existing GSI
backpack with `gv-add-player`. This module does not grant arbitrary players
coin or create a second backpack. The installation royal-business actor is
actor 1; subsequent player/purse authority belongs to the composite owner.

Save the existing EUC1, GVS1 and GSI1 components with the world and character
records. On restore, use their supplied shared world/currency bindings, then
`bb-bind vendors items` to rebuild `BbTown`. Quote and visibility state reset.
No additional persistent format or change to those payload sizes is required.
Do not rerun population on reload. The one exception is a townsfolk world
saved with five vendors: `cc-read-town` populates the innkeeper alone
(`bb-populate-one` from index 5) before `bb-bind`. The add is not committed at
load, so each boot re-derives the same innkeeper until the first ordinary
commit persists it. The role ordering is part of this installation contract
and must not be reordered independently.

## Production, funding and dress

Treasury and vendor purses start at zero. The royal actor gathers tool
materials, mines and smelts all three metals, and mints them through the
currency coordinator. Named placeholder yields are 1000 gold, 1000 copper and
100 silver per ingot. The installation royal-business grant is 100 gold,
40 silver and 200 copper; each vendor borrows 100 gold from the treasury with
10 interest due in 48 game hours. These are named installation placeholders,
not new rulings for general starting purses or mint terms. Tax is 10 percent.

Each vendor gathers and makes its containers, outfit and goods through the
ordinary harvest/craft functions at registered Britain workplaces (place 100).
Skills start at zero and rise through actual attempts; tools wear and are
replaced through recipes. The production planner follows inputs and tools,
with a depth limit of 16 and 8192 work attempts per actor. Successful work
scratch is reclaimed. It never assigns finished inventory or coin directly.
The batch receipt preserves exact new quantity, quality, wear and cost.

The three hidden vendor containers are recipe-produced bags. Their serials
are recorded in GVS1 and their material quantities remain in the actor pool.
Outfits and sale goods additionally receive exact `GvLot` bindings. Goods are
produced in dependency order so later work does not consume an already-bound
sale batch. There is no automatic restocking on boot or menu opening.

Dress uses blu's GSI metadata and `gmb-reply`/`gmb-mobile` rendering. Produced
garments are created as actual world items, registered at shirt, pants and
shoe layers, and made immovable on the NPC. Sale apparel carries its equip
layer and remains movable. Newly split purchase lots get GSI registration.

## Reference and limits

The local Source-X checkout has no spawn data. Placement regions, stock
graphics and item-name corroboration are adapted from UOX3 commit
`4560ae841bac898817143d7aa95ce59f47ab98e0`, under GPL-2.0-or-later:

- `data/dfndata/spawn/felucca/spawn_felucca_town_britain.dfn`, regions 1, 10,
  13, 23 and 27.
- `data/dfndata/items/shoplist.dfn`, the five named shop lists.
- `data/dfndata/items/food/foods.dfn`, gear definitions and tailoring/healing
  tools/resources definitions for the selected graphics and names.

[ports/UOX3-LICENSE.txt](ports/UOX3-LICENSE.txt) retains the upstream license
and copyright notice. Recipes are the Codex economy's rules, not copied NPC
restocking. Existing Source-X packet attribution remains in `GameVendor.md`.

`proofs/BritainShopsProof.codex` uses a synthetic walk map. It checks source
regions, registered dress, production/coin census, loans, each shop's actual
09/03/3B/9F handler path and EUC1/GVS1/GSI1 restoration. Actual MUL geometry
and client acceptance belong to root's complete-composite run.
The exact native oracle passes on seed `4228CD5103DC4523` (2026-10-05), including all six
shop purchases/resales and restore of currency, vendor and item metadata.

Bootstrap is bounded by the existing actor, node, station and lot tables.
Placement scans bounded reference regions and indexed tile occupants. Production retries
return before repeating, so work-attempt count does not grow the call stack.
Runtime visibility scans six vendors; equipment construction uses blu's
bounded renderer only on visibility transitions. Persistent state uses the
existing tables plus six transient viewer records. The full composite's
durable commit and recovery remain fester's responsibility.
