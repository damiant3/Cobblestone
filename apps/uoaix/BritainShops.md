# Britain shops

Ruling (Damian, 2026-10-07): "shopkeeps are not immune, and they should become
normal npcs with homes and stuff. there isn't a diff between a shop keep and a
worker, both are shop keeps, both can work the shop. their supply is not on
their body, its in the shop, preferably realized in a container, probably
locked if its small and valuable and easily stolen, while large pieces like
brest plates only need to be locked at night, when the whole shope should
close." Shopkeepers are mortal (`cgl-mortal-vendors`). A shop's goods for
sale lie in a strong box (small valuables) and a crate on its region's first
tile (`ShopStock`); the vendor's layer-26 container is the 1.25 buy window,
filled from them when a buyer opens it and emptied back after a purchase, a
restock, a session drop, the shopkeeper's death and every load. A shop
container opens and takes a lockpick by hand, but nothing inside it is lifted
(`ia-shop-box`): Stealing takes it. A killed shopkeeper leaves an empty corpse where it fell and rises on
its shop's first tile 5 minutes later (`cg-vendor-fall`, timer kind 11).
Goods bought in (the vendor's layer 27) move into the shop at the same
moments, and restock and smithing count a vendor's lots in its shop
(`bb-in-shop`). Open: a killed shopkeeper rising at home.

One role: vendor row i is shop i, not a person. Shop i's staff are the row's
shopkeeper and every resident whose work is that shop (Nell at Good Eats,
Jorin at The Hammer And Anvil, Mira at The Lords Clothiers). Any staff member
standing within 8 tiles of the shop's region (`cgl-counter-reach`, which
reaches the stations) sells from the row: a double-click on that member, or
"buy" and "sell" spoken within reach, opens the row's menu, spoken by that
member (`gv-staff-shop`, `gv-serving`; the composite binds `cgl-staff` and
`cgl-serves`). A shopkeeper keeps a home, its counter and a tavern tile like
any resident: open hours at the counter, game hours 20 to 22 at the Blue Boar,
then home (below; the tavern keeper's counter is at the Blue Boar); its station walk (`csn-smith-step`) runs only while the
shop is open.

## Hours and locks

`BritainShopHours` keeps the shops open from game hour 8 to 20 on the
town clock, except the healer (shop 5), the innkeeper (shop 6) and the tavern
keeper at the Blue Boar (shop 19), who trade at every hour in every city
(Damian, 2026-10-10: "healers and taverns should never close"; `bsh-never-closes`),
and the musician at the Conservatory (shop 20), who plays from game
hour 18 to midnight, a lute sound once a game minute at its post heard by every
player in event range (`bsh-shop-open`, `bsh-plays`). The musician sells nothing;
its income is tickets. Outside its hours `GvWorld.closed` marks a shop, and a buy
or sell request (speech, double-click, 3B, 9F) answers "The shop is closed."
At closing each keeper walks to its own tile at the Blue Boar
(`tl-tavern-region`), at game hour 22 to its own tile in the shared lodging
(`tl-home-region`) through the lodging entrance, and at opening walks back to
the tile it stood on. The innkeeper's home is its post. A keeper the lodging
cannot hold stays at its shop. Every ordinary door within `bsh-reach` tiles of
a shop's reference region bars once per night (`wd-bar`), when it is closed
and the keeper is outside that reach. Lockpicking a barred door opens it for
the rest of that night. All bars lift at opening. The inn's doors and the
lodging entrance never bar. Walk state and the per-door night marks rebuild
at boot (`bsh-new` in `cg-town-doors`); door bars persist in WDS1.

At each opening and closing (and at boot) every strong box is locked and every
crate is locked at closing and unlocked at opening, as Magery container locks
(`bsh-locks`), so a picked lock holds until the next turn of the hours. Closing
also empties every buy window (`gv-window-all`). The box opens gump 0x4B and
the crate 0x44 (ServUO `Data/containers.cfg`). Lockpicking works on both
(`glk-pick-at`). Stealing an item inside either takes it from the shop's
keeper's economy actor; the thief stands within one tile of the container,
and the keeper need not be present (`as-steal-keeper`).

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
shop is UOAIX-55. The vendor table holds one row per shop and no bank lineup
(Damian, 2026-10-08); the bank's real banker stays.

## Composite owner API

On a new installation, before publishing the candidate world:

```text
bb-economy 0 -> Result EuCurrency Text
gv-new game currency -> GvWorld
gsi-new game.world -> GsiState
bb-populate vendors items cachedMap -> Result BbTown Text
```

Use at least 128 world slots and a cache covering the reference regions. The
recommended window is x=1408, y=1536, width=128, height=224 (the fish market at the docks reaches y 1750). Installation builds
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
ordinary commit persists it. A world saved with any other vendor count is
refused at `bb-bind` and replaced by a fresh world.

## Production and funding

Treasury and vendor purses start at zero, and a new world strikes nothing
(EconomyCurrency.md, Damian 2026-10-07): the royal actor opens the Royal Mint
and sets each metal's yield (`bb-mint-policy`). Named placeholder yields are
1000 gold, 1000 copper and 100 silver per ingot. A shop with no open crown loan
borrows one gold on the books once the treasury holds it (`bb-borrow-shops`,
each town step) and repays from revenue (`bb-repay-shops`). These are named
placeholders, not rulings for loan size or mint terms. Tax is 10 percent.

Each vendor gathers and makes its containers, outfit and goods through the
ordinary harvest and craft functions at registered Britain workplaces (place
100); it is never assigned finished inventory or coin directly. The production
planner has a depth limit of 16 and 8192 work attempts per actor. Goods are
produced in dependency order so later work does not consume an already-bound
sale batch.

## Premises for new trades

Britain's sign names are in UOX3 `data/js/jsdata/worldtemplates/felucca_signs.jsdata` (name, then x and y in fields 5 and 6, under `D:/Projects/uo-reference`); ServUO's `Data/signs.cfg` places the same signs by cliloc id only. Signs in x 1400-1499, y 1540-1729 with no `bb-shops` shop: The Best Hides of Britain 1440,1611; Lord British's Conservatory of Music 1454,1561; Premier Gems 1458,1683 (walls x 1449-1457, y 1677-1693); Artists' Guild 1444,1670; The Cleaver 1449,1728; The Sorceror's Delight 1488,1572; The King's Men Theatre 1441,1584 and 1454,1600; Miners' Guild 1425,1584 (beside the paper mill); The First Bank Of Britain 1436,1693; The First Library of Britain 1493,1724. A sign stands outside its building's door; `STATICS0.MUL` gives the walls. Taken for new trades: Strength and Steel (herbalist, shop 13), Quality Fletching (bowyer, shop 14), The Best Hides of Britain (tanner, shop 15), the unsigned fish shop east of the provisioner (leatherworker, shop 16; walls x 1479-1487, y 1663-1675, open west to a hall whose door is 1475-1476,1669, floor z 0; Damian, 2026-10-09: "this fish monger station should change to become the leatherworkers station"), each root's pick 2026-10-09, Damian may override, and the west two rooms of the unsigned South Britain row houses, joined by `decor.cfg`'s cut of the wall at x 1453 (cobbler, shop and bedroom; walls x 1447 and 1459, y 1703-1711, doors 1450,1711 and 1456,1711, floor z 0; Damian, 2026-10-09: "made into a bedroom/shop for the cobbler. like the other shops with bedrooms attached nearby"). The row houses are four one-room lodgings with doors at x 1450, 1456, 1462 and 1468 on y 1711, each a bed, carpet and counters. The hunter is a worker and takes no premises (root, 2026-10-09). The tavern keeper (shop 19, UOAIX-222) keeps the north end of the Blue Boar's hall (walls x 1493 and 1501, y 1675-1697, a counter down x 1497, door 1501,1687; UOX3 sign 1502,1689), east of the counter; the hall's west room is where the other keepers drink (`tl-tavern-region`). It founds empty (`bb-participant`), makes wine from 4 grapes bought from the inn and sells bread bought from the baker and cooked food bought from the inn (`bb-tavern-supply`, `bb-brew`). The musician (shop 20) plays in Lord British's Conservatory of Music (sign 1454,1561), a hall at x 1448-1462, y 1554-1558 behind a pillared porch, door gap 1454-1456,1559; its post is x 1449-1453, y 1555-1557. The fishmonger (shop 18) is the eastern of Britain's two Oaken Oars (signs 1424,1747 and 1480,1746), walls x 1471-1483, y 1735-1751, door 1479,1747 onto the pier (Damian, 2026-10-09: "the eastern one, should become the fish market").
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
