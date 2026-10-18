# Economy production runtime

`EconomyProduction.codex` and `EconomyHarvest.codex` execute stage E's
gathering, crafting, purchases, consumption and plant growth against real
bounded inventories. `ep-new catalog money` validates the catalog and returns
a new production state linked to an existing `EmMoney`. Core water, food and
coin IDs must retain the standard catalog names and positions; extensions can
append types within the catalog budget. Accepted catalogs remain immutable.
Retain catalog, money and production allocations together for the world's
lifetime; a scratch restore must not invalidate any referenced record or buffer.

Actors start with empty inventory. `ep-admit world purse place` binds an
existing non-treasury purse exactly once. NPC and player purse kinds use the
same operations. Actors, places, resource nodes and stations are simulation
IDs; no current function creates a WorldObject or moves a client mobile.

## Trusted admission boundary

The calls are server simulation primitives. Actor IDs and purse IDs are not
credentials. The eventual server adapter must authenticate the actor, verify
actual position, map/source suitability, plot rights and building ownership,
and dispatch only allowed actions. `ep-place` records a verified arrival; it
is not a teleport command exposed to players. `ep-station` admits a building's
owned workplace; it does not create a free building. `eh-source` admits a
world-created harvestable from a configured source, not player-created stock.
`eh-ground` admits owned prepared farmland or tree soil at the actor's place;
map tilling and plot permission are adapter prerequisites. `eh-eaten` records
an authoritative wildlife event. None of these admission calls is a client
protocol handler or an authorization wrapper.

## Items, workmanship and tool use

Each actor stores quantity, total quality points, total input/acquisition cost
and remaining use charges per item type. Quantities are capped at 1000000.
This is an aggregate inventory, not a serial-numbered item store. A future
world binding must preserve individual object identity and complete trade
records when converting between representations.

A harvest adds the exact quality (up to 1000 a unit) while its lot records only
the grade floor. `ep-settle actor item` caps quality at 1000 and wear at 32 a
unit held and clears cost at zero stock; without it `ev-actor-items` refuses the
next save (UOAIX-110). `ep-take`'s lot path (`ep-take-lot`) settles itself;
its no-lot path (`ep-take-rest`) and `ep-remove` do not, and a hand debit (eating,
reagents, cooking) ends with `ep-settle` itself, as `gco-cook` does.

`eh-harvest world actor node` requires a mature source at the actor's place,
the source's tool, and ownership when the node is planted. On success the
source becomes depleted, the actor receives actual output and any seed, and
the source's regrowth deadline is set. Seed and output enter the gathered
census. No loose reagent inventory is spawned. The world adapter must bind
the reagent sources described in [EconomyCatalog.md](EconomyCatalog.md).

`ep-craft world actor recipeId stationId` requires the recipe's owned,
enabled station at the actor's current place, all inputs and the tool.
Inputs are consumed and output enters the crafted census. Tools that are also
recipe inputs require an extra unit for use. Ordinary crafting refuses the
royal-mint station and coin output. [EconomyMint.md](EconomyMint.md) owns the
separate configured, resource-consuming issuance action; this runtime cannot
craft coin as an inventory item or harvest coin from a catalog resource.

Recipe and node IDs are one-based. Successful harvest/craft returns the
positive output quantity. Admission refusal is -1 with no mutation. A failed
skill attempt returns -2, advances practice and wears the tool but consumes
no recipe materials and produces nothing. Callers must distinguish those
outcomes before retrying.

Skills range 0..1000. Each admitted trained attempt gains one point up to the
cap; untrained hand operations use category 0 and always succeed. The
deterministic roll uses the actor's attempt counter. Success chance rises
from 10% to 100%; the maximum gather yield and output quality rise with skill.
The proof explicitly sets fixture masters' skill levels, never their stock.
No public skill-grant entry is provided by the production module.

New inventory carries 32 use charges per unit. A tool attempt spends one.
Use charges are pooled within an item-type stack; when charges fall below
the remaining stack's full-unit threshold, one unit is consumed. This bounded
aggregate model does not retain each tool's individual wear. Purchases move
the proportional remaining charges and quality, so resale cannot reset wear.
Crafting makes new output charges only after consuming real materials.

## Planting and the clock

`eh-plant world actor node resourceId` consumes that resource's seed/cutting,
requires the correct prepared ground and an owned empty or failed plot, and
starts growth. `eh-care world actor node True` consumes one unit of water;
the False variant records tending. Care requires the owner at the plot.
Repeated watering consumes another unit; server retries require action-ID
handling before dispatch rather than assuming every operation is idempotent.

Node stages are depleted/just planted 0, growing/sapling 1, mature 2, failed
or eaten 3. Growth enters stage 1 halfway to the configured deadline. At the
deadline wild sources regrow; whether a neglected planted source fails
depends on its fragility (`eh-fragility`), per Damian's ruling of 2026-10-07:
"no, they shouldn't. trees are hardy. we use that mechanic for tender crops,
fragile ones. mandrake might be very difficult for example, to keep alive,
while wheat is fairly resilient in comparison."

| fragility | resources | fails at the deadline when |
|---|---|---|
| 0 hardy | timber 15, fruit 16 | never |
| 1 resilient | cotton 10, flax 11, wheat 13, carrots 28, onions 29, turnips 31, corn 32, gourds 37 | neither watered nor tended |
| 2 moderate | every other crop | not both watered and tended |
| 3 tender | mandrake 19, nightshade 20, honeydew 35, hops 38 | as 2, and both flags clear when it turns half grown, so it is cared for in each half |

Harvest clears both care flags. Failed or eaten plants require fresh seed to
replant. A felled timber source
becomes a stump, then a sapling, then a mature tree; with the flora layer
(`WorldFlora.codex`, UOAIX-47) a stripped client sees those stages as art.

`eh-step world` advances one game hour, together with the money clock and
loan-default events. A money-ledger refusal leaves the production clock and
nodes unchanged. Actor hunger rises by three up to 100. Demand counters halve
each game day. `ep-eat world actor item` consumes an owned food (`ep-food`):
cabbages, apples, bread, cooked food, grapes and the field crops carrots,
onions, lettuce, turnips, corn, pumpkins, squash, honeydew melons, watermelons
and gourds (hops are not food), and reduces hunger by 30 down to zero. A
player eats any of them from the pack (`gh-fill`, ServUO Food.cs fill factors).
An item is named by its own kind, never its category (Damian, 2026-10-07:
"'vegetable' is not a vegetable. its a category."). An actor lacking
food cannot eat and remains hungry. Purchasing, work and meals are explicit
operations; the next clock harness must schedule those actions.

## Purchases, logs and census

`ep-price world seller item` quotes integer coin per unit from average stored
input/acquisition cost, stock scarcity and recent sales. `ep-buy world buyer
seller item quantity` requires both actors at the same place, real seller
stock, buyer capacity and the gross price in the buyer's purse. Money and tax
move through [EconomyMoney](EconomyMoney.md). The buyer's cost basis becomes
the price actually paid. Prices cannot exceed 1000000 per unit.

Each purchase appends action ID, hour, buyer/seller purse IDs, item type,
quantity, gross coin, quality and remaining charges. The action ID matches
the corresponding money-ledger action. Trade and ledger capacity are checked
before either coin or goods move. Logs are never overwritten or cleared.

**Theft transfer** (`as-steal-lot`, `ActiveSkillsStealing.codex`): a successful
steal of an item held as an economy lot moves its units from the victim's actor
to the thief's with no payment: stock, quality and wear at the lot's own
per-unit grade, and cost by the lot's share of basis. A whole stack keeps its
lot under the thief; part of a pile reduces the victim's lot and opens a new
one for the thief. Every census holds because no unit is made or destroyed.
The thief must be a player with an economy actor and room for the units, or
the steal fails before anything moves. It writes no trade-log row (that log is
tied to money actions); the record is the steal reply's note in the server log,
`skills:stolen; theft item=<id> quantity=<n> from-actor=<a> to-actor=<b>`.
Stealing a lot-less item (summoned or testing stock) moves only the world item.
A coin pile in an economy actor's pack is purse coin, not material: its theft is
the currency theft in [EconomyCurrency.md](EconomyCurrency.md) (`eu-steal`).
These records describe one item-type sale, not a multi-item barter or a
complete server log of serial-numbered player trades.

Per-chain turnover records gross sales. Crown turnover counts purchases
directly made by a crown-business purse; it does not trace the downstream
origin of fungible coin or yet report the full bootstrap-subsidy share.

`ep-census world 1` checks every item type: total held equals gathered plus
found plus crafted minus consumed. `ep-find` admits goods that entered from
outside the economy (testing kits, loot) at a vendor sale: stock at cost basis
0, quality 1000 and wear 32 per unit, counted in `found` and never in
`gathered` (root ruling, 2026-10-05). Inputs, food, seed and broken tools enter consumption;
sales only move stock. Accepted catalog closure establishes material paths,
and the runtime admits no direct stock grant besides `ep-find`. The census detects unlogged
stock, but is not a per-object provenance tree or an untrusted-state validator.

## Bounds, costs and integration

The runtime preallocates 128 actors, 512 stations (`ep-station-limit`), 2048 resource/plot nodes (`ep-node-limit`) and
4096 lifetime purchases. Actor records are 9 fields (72 bytes), stations 4
(32 bytes), nodes and purchase rows 9 (72 bytes each), and the world 20
(160 bytes), plus lists and fixed inventory/counter arrays. Ordinary actions
and ticks allocate no heap. Records refuse past capacity.

Harvest, crafting, pricing and purchases are O(1) with indexed records. Actor
admission scans at most 128 purse bindings. A tick costs O(nodes + actors +
loans), plus item-count demand decay at day boundaries. The material census
costs O(items * actors); the money census retains its separate ledger-scan
cost. Counters and prices are bounded before arithmetic that can grow stock,
cost or turnover. The runtime assumes valid module-owned state and one writer.

The proof `proofs/EconomyProductionProof.codex` checks bootstrap without stock
grants, station/owner refusal, novice/master behavior, tool wear and resale,
prices, planted crop care/failure, the mill-to-bread chain, eating, tree
regrowth, purchase logs, capacity refusal and both censuses. Normal and poisoned
builds must match `proofs/EconomyProductionProof.expected` exactly.

The runtime provides in-process refusal atomicity, not crash atomicity; the
composite's commit owns durability. [EconomyClock.md](EconomyClock.md) owns the
30-day scheduling/census acceptance. The database path replaces the current
codecs only after equivalent restart acceptance, per [Database.md](Database.md).
