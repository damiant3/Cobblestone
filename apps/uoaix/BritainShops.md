# Britain shops

`BritainShops` supplies `bb-shop-count` permanent shop bindings for the
composite server. The `GvWorld.vendors` rows are provisioner, blacksmith,
tailor, baker, healer, innkeeper, tinker and carpenter, in that order; the role
ordering is part of the installation contract and must not be reordered
independently. Stock lists are `bb-goods` (`BritainCatalog.codex`). Positions
come from the reference spawn regions; placement chooses a clear surface
within each region using the supplied map.

| Shop | Reference region |
|---|---|
| Britains Premier Provisioner | 1465..1470, 1672..1674 |
| The Hammer And Anvil | 1417..1420, 1544..1550 |
| The Lords Clothiers | 1467..1471, 1684..1689 |
| Good Eats | 1449..1454, 1609..1618 |
| Healer Of Britain | 1470..1477, 1608..1614 |
| Sweet Dreams Inn | 1496..1498, 1615..1617 |
| The Tinkers Guild | 1422..1429, 1648..1663 |
| The Saw Horse | 1429..1432, 1593..1598 |

Prices come from `ep-price`; no upstream restock count or fixed price is
imported. The healer is a shop binding with no resurrection behavior.
Tinkering works only within The Tinkers Guild and carpentry within The Saw
Horse (`cpr-craft-lookup`); smithing keeps the forge and anvil of The Hammer
And Anvil.

A participant shop (`bb-participant`) starts empty and makes its stock from
inputs it bought; the blacksmith forges a longsword (recipe 70) only while
standing within two tiles of the anvil and the forge. Station work for every
shop is UOAIX-55. The bank lineup copies stay force-stocked as the testing
fixture and restock at every testing boot (`cgl-restock`).

## Testing-mode bank lineup

A composite booted in testing mode (`UOAIX TESTING`) on a Britain world stands
one of each townsfolk role in the row south of The First Bank of Britain's
front doors (1438-1439,1692): x 1434-1443 at y 1694, each tile moved one row
south when not walkable. The trading copies of the shops follow the shops in
role order, with their own actors, loans and production; then a banker and a
resident stand-in in the banker's dress, who answers with the first resident's
name and greets through that resident's persona. The copies are immune to
combat. A testing boot re-derives the same lineup until the first ordinary
commit stores it; a world that stored it keeps the copies in normal mode,
where the banker and resident copies are not bound. A lineup that cannot be
built stops the boot before any commit.

## Composite owner API

On a new installation, before publishing the candidate world:

```text
bb-economy 0 -> Result EuCurrency Text
gv-new game currency -> GvWorld
gsi-new game.world -> GsiState
bb-populate vendors items cachedMap -> Result BbTown Text
```

Use at least 128 world slots and a cache covering the reference regions. The
recommended window is x=1408, y=1536, width=128, height=192. Installation builds
the local cache; server boots read it. This module performs no MUL reads and
ships no map or art data. A failed bootstrap may have changed its private
candidate; discard that candidate. It must not be retried on published state.
The initializer refuses an existing vendor registry or non-fresh economy.

Bind `bb-handler town` through `gn-route`, before generic item/mobile use and
name handlers. It claims only these vendors' 09 names and the existing vendor
family; unrelated traffic returns None. `bb-specs` includes the vendor bounds
and fixed 09. The owner deduplicates shared framing declarations as described
in `GameDispatch.md`. If the composite uses `GameClientView` for all mobiles,
use that single viewer instead of also emitting `bb-pulse` draws for the same
NPCs.

The owner binds an authenticated player's economy actor and existing GSI
backpack with `gv-add-player`. This module does not grant arbitrary players
coin or create a second backpack. The installation royal-business actor is
actor 1.

Save the existing EUC1, GVS1 and GSI1 components with the world and character
records. On restore, use their supplied shared world/currency bindings, then
`bb-bind vendors items` to rebuild `BbTown`; do not rerun population. A world
saved with one shop fewer populates the last shop alone at load
(`cc-read-town`, `bb-populate-one`), re-derived each boot until the first
ordinary commit persists it. A world saved with an older testing lineup is
refused at `bb-bind` and replaced by a fresh world (Damian, 2026-10-06).

## Production and funding

Treasury and vendor purses start at zero. The royal actor gathers tool
materials, mines and smelts all three metals, and mints them through the
currency coordinator. Named placeholder yields are 1000 gold, 1000 copper and
100 silver per ingot. The installation royal-business grant is 100 gold,
40 silver and 200 copper; each vendor borrows 100 gold from the treasury with
10 interest due in 48 game hours. These are named installation placeholders,
not rulings for general starting purses or mint terms. Tax is 10 percent.

Each vendor gathers and makes its containers, outfit and goods through the
ordinary harvest and craft functions at registered Britain workplaces (place
100); it is never assigned finished inventory or coin directly. The production
planner has a depth limit of 16 and 8192 work attempts per actor. Goods are
produced in dependency order so later work does not consume an already-bound
sale batch.

## Reference

The local Source-X checkout has no spawn data. Placement regions, stock
graphics and item-name corroboration are adapted from UOX3 commit
`4560ae841bac898817143d7aa95ce59f47ab98e0`, under GPL-2.0-or-later:

- `data/dfndata/spawn/felucca/spawn_felucca_town_britain.dfn`, regions 1, 10,
  13, 15, 18, 23 and 27.
- `data/dfndata/items/shoplist.dfn`, the named shop lists.
- `data/dfndata/items/food/foods.dfn`, gear definitions and tailoring/healing
  tools/resources definitions for the selected graphics and names.

[ports/UOX3-LICENSE.txt](ports/UOX3-LICENSE.txt) retains the upstream license
and copyright notice. Recipes are the Codex economy's rules, not copied NPC
restocking. Source-X packet attribution is in `GameVendor.md`.

`proofs/BritainShopsProof.codex` grades placement, dress, production and coin
census, loans, each shop's 09/03/3B/9F handler path and EUC1/GVS1/GSI1
restoration on a synthetic walk map.

Bootstrap is bounded by the existing actor, node, station and lot tables.
Runtime visibility scans `bb-shop-count` vendors and keeps `bb-view-limit`
transient viewer records; equipment is rendered only on visibility transitions.
