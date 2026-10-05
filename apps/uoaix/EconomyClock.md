# Thirty-day economy acceptance

`EconomyClock.codex` supplies a deterministic model-free scheduling fixture
over [EconomyProduction](EconomyProduction.md). `ec-fixture 0` creates 24
resource workers, 30 recipe workers and a reserve buyer. Every inventory and
purse starts empty, including the treasury. The reserve buyer is Lord British,
with a crown purse. Before lending, Lord British gathers stone and branches,
makes a pickaxe and hammer, mines two gold ore, smelts one ingot using fuel,
and strikes the first coin through [EconomyMint](EconomyMint.md). The fixture
explicitly configures a test yield of 6000000 coin per ingot, then lends
100000 to each actor at zero interest, due at hour 1000. The yield and loan
terms are acceptance parameters, not R8 rulings or production defaults. R8's
zero starting purse is enforced; the production mint rate still defaults to
disabled. The test artisans have skill 1000; the production proof
separately grades apprentices and skill gains.

All workers and owned workplaces share simulation place 100, avoiding an
unproved physical pathing claim. Wild source nodes start mature. Food sources
have eight nodes each; other sources have four. Plantable-resource workers
also own one prepared plot, plant their gathered seed, buy water, and tend.
There is no initial shop stock, tool, manufactured good or planting seed.
Workers bootstrap tools through hand-gathered stone, branches and clay.

`ec-hour clock removeHunters` advances one hour and schedules work during
hours 8..17, meals at 18 and reserve buying at 19. Resource workers gather
until their output stock reaches 16; recipe workers craft until stock reaches
4. Workers buy missing inputs and tools from the cheapest stocked worker,
and retain a partial purchase when more input is still needed. Each purchase
and craft is its own action; the work block is not one atomic transaction.
Prices, inventory, skill and tool rules are the production module's rules.

The reserve buyer buys one available unit of each item 39..49 each day and
keeps the goods. The buyer creates demand without deleting stock. Meals
consume bought bread, cooked food, vegetables or fruit; absent food leaves
the actor hungry. This fixture establishes chain execution and conservation,
not a self-financing equilibrium, adequate nutrition for every actor, market
price calibration or realistic reserve demand.

`ec-run clock hours removeHunters` stops on a failed hour, exhausted history
or failed daily census. A failed hour can follow successful actions; the
driver does not roll back the hour's committed simulation prefix. A live
adapter must persist each action and must not blindly repeat a failed hour.
The scheduling fixture remains separate from `TfWorld` needs/jobs and the
game server; no claim of live integration follows from these results.

## Acceptance

`proofs/EconomyClockProof.codex` creates two separate fixtures and runs each
for 720 hours. At every day boundary the material census checks held stock
against gathered plus crafted minus consumed, and the money census reconstructs
every purse from its ledger. The normal and shortage arms must each finish
30 checks with zero failures, unchanged retained heap, and 6000000 total coin.
The standard catalog's closure and the absence of direct inventory grants
establish the fixture's resource paths; no per-object serial genealogy is
claimed.

The chain grade requires weapons and metal armour, gold ingots and jewelry,
leather armour, clothing, bread and cooked food, furniture and bows, plus every
reagent's harvested output. A raw ore counter alone cannot pass a metal chain.
The shortage arm disables both hide and meat hunters at hour 240. Remaining
stock stays in the world and downstream workers can use it. The final grade
requires positive normal leather-armour shelf stock, zero shortage-arm shelf
stock, and less total leather-armour output in the shortage arm. The reserve
buyer's earlier purchases remain in its inventory and in the material census.

Under seed `BF984339C7BA4BAE` on 2026-10-04, the normal arm used 3279 purchases
and 4057 money rows and ended with 3 leather-armour units on sale, 31 produced.
The shortage arm used 2970 purchases and 3748 money rows and ended with none
on sale, 11 produced. The measured acceptance uses a bounded 4096-purchase
store and retains all history. Production construction retained 947864 bytes, excluding
money. No rolling-log truncation or archive deletion was introduced.

Compile normally and poisoned with an explicit depot kernel; compare the
entire runtime output to `proofs/EconomyClockProof.expected`. Also rerun
`EconomyProductionProof` when changing shared capacities or runtime behavior.

## Cost and remaining work

The fixture holds 55 jobs, each 8 fields (64 bytes); its clock holds 6 fields
(48 bytes), plus the preallocated production and money state. Work visits a
fixed job set. Seller selection scans 54 workers per missing input, and node
selection scans at most the bounded node set. Work costs
O(jobs * (actors + nodes)) per work hour. Daily census adds
O(items * actors + purses * ledger rows). All counters and collections stay
bounded; the two 720-hour runs check no retained heap growth.

Stage E still needs Damian's production mint terms, economy checkpoint and
ordered-input recovery on the current storage, `TfWorld`/world-item bindings,
and eventual database migration. Government, town cultures and housing
assignment remain their own units. This acceptance does not retire the
existing townsfolk or persistence paths.
