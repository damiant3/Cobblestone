# UOAIX merchant fleet (UOAIX-237)

Damian, 2026-10-10: "lets add a merchant fleet that buys and sells, and sails the high seas to capture profit. NuJelm
must have something special to make itself economically viable. right now a desert island with no food production
makes no sense economically. any suggestions?" Nujel'm is not the home port (Damian, 2026-10-10: "it has to be some
kind of tangible resource, something only found there"); its resource waits for his pick.

## End state

Merchant ships, each with a crew and a ledger purse, sail scheduled lanes between port cities, buy a port's surplus at
its shops' ask, sell where a shop bids more, and keep the margin in their purse. Their cargo is real lots moving between
shops, so economy rows show it. Players see the ships at sea and in harbour. The home port is Britain; Nujel'm is a
port of call whose own resource, once Damian picks it, is cargo the fleet carries.

## What the code already gives (2026-10-10)

- Every shop of every city is an economy actor at place 100 (`bb-populate-at`, `BritainShops.codex:230-232`; city shop
  k is vendor `bb-shop-count + k`, `:578`). `gv-can-pay` and `ep-buy` require buyer and seller at one place
  (`GameVendorTrade.codex:139`, `EconomyProduction.codex:724`), so a ship actor at place 100 trades with any shop. A
  voyage is therefore a rule of the fleet, not of the ledger.
- An NPC actor: `eu-open x 2 owner` opens its purse (`EconomyCurrency.codex:289-296`), `ep-admit w purse place`
  admits it (`EconomyProduction.codex:483-494`); `cg-wild-actor` (`CompositeGameRules.codex:772-779`) finds an owner's
  purse again with `em-find` before opening a new one, the pattern for an actor that survives a restart. Owner labels
  in use: shops 1000+, townsfolk 2000-2127, workers 3001+, city workers `3600 + 20 * city + k` (3899 at 15 cities,
  more with a 16th), spawns 5000000+. The fleet takes 4800 + k.
- Buying from a shop: `cgg-buy-raw` (`CompositeGatherer.codex:882-901`): ask from `ep-price`, the keeper near
  (`gv-near`), the shop's buy window opened (`v.window idx 1`, `ss-window`), `gv-checkout` into the buyer's container,
  window closed. Selling to a shop: `cgg-sell` (`:986-1005`): bid from `gvb-bid-for`
  (`GameVendorBuys.codex:111-114`, `base * (30 - min 20 stock) / 20`), `gv-checkout` with the shop as buyer.
- Prices differ by shop: `ep-price` (`EconomyProduction.codex:708-718`) is the seller's own inputs plus scarcity below
  10 held plus a demand term; the bid falls as the shop's own stock rises.
- Stepping: the timer wheel dispatches by kind in `cg-timers` (`CompositeGameRules.codex:3864-3872`: 8 miners, 12
  workers, 20 city workers); city workers arm at boot (`cg-arm-city-workers`, `:1488-1491`) and re-arm each step with
  `wtm-add`. The fleet takes its own kind and the same arm and re-arm pair; its cost shows in `TICK SLOW` as that kind.
- Nujel'm is city 8 (`Cities.codex:19`); its shops (`ct-shops-nujelm`, `:127-134`) are a blacksmith, tailor, innkeeper,
  bowyer, tanner and leatherworker, and the jeweler "Jewel of the Isle" (`:253`). It has no worker city and no food
  trade. `Ships.codex` draws one boat on one lane south of Britain per viewer (no world object, no ledger).

## Stages

- **F1, the ledger fleet (no visuals, no dock).** A `MerchantFleet` chapter: 3 ships, each an actor (owner 4800 + k,
  place 100) with a hold, a lane of port cities and 6 game hours a leg. A port is a city of `ct-table`, and its shops
  are that city's vendors (Britain 0 to 20; city shop k is vendor `bb-shop-count + k`), found by the city rectangle that
  holds the shop's centre (`mf-port-shops`). The lanes, all from and back to Britain (city 0): east, Britain, Magincia
  (7), Moonglow (2), Nujel'm (8); south, Britain, Trinsic (1), Jhelom (9); north, Britain, Vesper (12), Minoc (13),
  Cove (14). Built (F1a): `MerchantFleet.codex` holds the lanes, the ports' shops, the best bid at a port and the buy
  and sell rules; `bvt/BvtMerchantFleet`. F1b is the ships themselves: actors, hold, timer, trades and codec. On arrival, first sell each lot in the hold to the port's shop
  bidding the most (`gvb-bid-for`) above the lot's cost, then buy, within the purse and a hold of 8 lots, the lots
  whose ask here (`ep-price`) is at least 20% below the best bid at the lane's next port. Trades call `gv-checkout`
  directly (`GameVendorUndo.codex:164`): it has no distance check, only its callers `cgg-buy-raw` and `cgg-sell` test
  `gv-near`. The hold is a container item at a reserved tile off the playable map, as the stable pen is
  (`CityStables.codex` `csm-pen`), until F2 gives each ship a dock. The step is its own timer kind (8, 12, 13 and 20 are
  taken), armed at boot and re-armed with `wtm-add`. Saved as a CompositeCodec tail section of its own (actor, hold
  serial, lane, leg, next sailing tick a ship), with a version byte so a world without it loads with a fresh fleet.
  Logs `FLEET ship=k port=city bought|sold item=i n=q at=p margin=m` per trade. A bvt case on two ports grades the
  choice of goods: buys below the next port's bid by the margin, sells only above cost, never past the purse or the
  hold (template: `bvt/BvtCityStables.codex`, a `blh-owner` fixture with bit-per-check answers).
- **F2, ships at sea.** The ships are drawn on their lanes as `Ships.codex` draws the boat today, per viewer, at the
  position the voyage clock gives, and moored at the port's dock while trading.
- **F3, Nujel'm's resource.** Waits for Damian's pick of a tangible resource found only there; the east lane then
  carries it from Nujel'm to the cities that buy it.

## Open decisions

- Nujel'm's resource, something only found there (Damian).
- F2's dock tiles: no harbour spot exists in the code. Candidates are the shore boxes of the worker cities
  (`CityWorkers.codex`) and the docks named in `Cities.codex` (Skara Brae docks :98, Cove "Docks" :264); each port
  needs one measured on the 1.25 statics before F2.
