# Denominated currency

`EconomyCurrency` coordinates three `EconomyMoney` ledgers against one material
economy. Metal codes are copper 1, silver 2, gold 3. Every purse ID denotes the
same legal owner in all three ledgers. `world.money` retains its existing gold
meaning; copper and silver use separate ledgers. Balances and loan terms are
counts of the named coin, never an implicitly converted gold balance.

## Construction and ownership

Construct an `EpWorld` with `EconomyMetals.ex-catalog`, then call `eu-new` before
opening any purses or admitting actors. Construction requires a fresh, empty
economy; attaching to an existing gold economy is refused. `eu-open` opens the
same kind/owner in every ledger and returns the common purse ID. The existing
`ep-admit`, workplace, harvest and ordinary crafting functions operate on the
shared material world. They do not issue coin.

After attachment, all money changes, minting and game-hour advancement must
use this coordinator. Direct `em-*`, `emi-*`, `eh-step` or `ep-buy` monetary
changes bypass its event linkage. Sequence/hour/purse-count checks detect
such a bypass and refuse subsequent coordinated actions. These checks do not
make raw records safe to mutate or authenticate a caller. Do not construct,
alias-edit or partially replace state through native record access.

The coordinator is single-owner. Each operation preflights every affected
ledger and its own event capacity before any mutation. No other reader or
writer may interleave the operation. An ordinary refusal leaves state
unchanged. This is in-memory admission, not durable transaction rollback.
The enclosing shard owner must retain a private candidate or supply a complete
undo transaction before publishing a money/material action.

## Price scale (Damian, 2026-10-05)

"we need to adjust prices to be realistic for a medeval world. what was a roman
soldier salary? 1 silver a week? so make that 1 silver a day for a regular
shopkeeper to buy food and pay expenses + 1 copper savings and 1 copper tithing.
so 8 copper has to cover their needs. a basic sword should be like 1 gold, 10
weeks of work to make it. a full suit like 10 gold."

Root's reading, binding until Damian corrects it: 10 copper = 1 silver; 70 silver
= 1 gold (10 weeks of 7 days at 1 silver a day), so 700 copper = 1 gold. A
shopkeeper earns 1 silver a day: 8 copper of needs (food and expenses), 1 copper
saved, 1 copper tithed. A basic sword sells near 1 gold, a full armor suit near
10 gold, and every other good is priced by the labor and material days in it.

A world whose catalog carries the metals (68 items or more, the same marker
`ev-shape` uses) is a copper world; a gold-only economy keeps its coin-unit
prices and pays no labor.

- Rates: a new Britain world logs `eu-rates` 700 copper and 70 silver per gold
  through the royal actor at founding. Fresh world disks are the default; there
  is no migration.
- Unit: in a copper world `ep-price`, cost basis, `EpTrade.gross` and
  `GvSale.gross` count copper. `eu-sale` requires the tender's copper value to
  equal the price; every vendor sale settles through it, and `gv-tender-plan`
  spends gold in `copper-rate` steps, then silver, then copper.
- Labor: `ep-labor` adds copper per batch at each craft (`ep-craft`), and each
  gathered unit costs 1 copper (`eh-add-yield`, harvest lots carry it as basis).
  Scarcity and demand are proportional to unit cost in a copper world.
- Theft: `eu-steal` (currency event kind 14; money rows kind 19) moves each metal
  from the victim's purse to the thief's untaxed, all metals checked before any
  moves. A resident's pack shows a quarter of its purse as coin piles (`tl-coins`);
  stealing a pile is this event (`as-steal-coin`), so no unbacked coin enters
  the world.
- Change: `eu-exchange` (currency event kind 12; money rows kind 6 in, 20
  retired, 21 recoined, 17 out) breaks a purse's coin with the crown at the
  current rates, at any distance (Damian, 2026-10-05). When the treasury lacks
  the smaller coin, it recoins whole coins of the larger, which the purse has
  just handed in, into the smaller at the rate (`em-retire`, `em-recoin`), so
  the crown can always make change and no value is created (root, 2026-10-09,
  UOAIX-213). The event's party cell records a decree, which no exchange makes
  now; the composite's FAULT line for one stays as the alarm. Vendor checkout
  breaks the payer's coin first (`gv-change`), so a gold-only buyer can pay a
  copper price. Smaller coin trades up through the same exchange: the treasury
  takes the smaller coin and pays the larger from its reserve and pays only a
  whole coin; a gatherer home from a trip and every shop each town step trade
  every whole gold of their silver up, then every whole silver of their copper
  as far as the treasury's silver pays (`bb-trade-up`; root, 2026-10-07).
- Residents' market: funds and bids compare in copper. The frozen town network
  reads prices in gold through `ep-price-gold`, which keeps its trained range.

| Labor per batch (copper) | Recipe outputs (item ids) |
|---:|---|
| 5000 | metal armour 41 (full suit) |
| 700 | leather armour 44 |
| 500 | weapon 40, longsword 70, plate gorget 71 |
| 200 | jewelry 43 |
| 150 | metal tool 42, furniture 47, bow 48, dagger 69 |
| 70 | clothing 46 |
| 35 | scissors 74 |
| 20 | shirt 72, short pants 73 |
| 15 | bag 45 |
| 10 | fishing rod 29, bucket 30, ingots 31 32 65 66, cloth 37, arrows 49, bandages 75 |
| 5 | stone tools 25-28 62, leather 35, thread 36, boards 38, nails 39 |
| 2 | flour 33, bread 34, cooked food 50, pizza 76 |
| 1 | each gathered resource unit |

## The economic loop (Damian, 2026-10-06)

"the economy needs laborers and shops need a loan from the crown."

"so the world needs to supply harvestables. those harvestables then need
transport, refining, then transformation into usables. e.g. iron ore (miner) ->
iron ingots (smelter) -> long sword (smith) -> gold (fighter). those
harvestables need to be gathered by independant NPCs who go mining, return with
the ore, smelt it, sell it to the smith, who then makes the long sword and sells
that to the fighter who then goes and adventures and returns with loot of
somekind making the loop worthwhile. Here, the gatherer has a tool to always get
low-grade ores, so at the bottom of the economy is a self-bootstrap. But the
smith starts broke. They need money to buy the ingots. They take a loan from
another npc, usually Lord British or a Mayor or Banker for that purpose. The
idea was that Lord British, on first login, must go and mine up gold to prime
the pump, or NPC miners can get gold and it gets auto-taxed. so we need to
provide faucets and drains in the economy where necessary. One drain for gold,
should always be Lord British's bank."

"along with the fully NPC sustained economy, a human driven client can insert
themselves into that economy by doing the mining themselves, the smithing
themselves, or the fighting themselves. or all at once, if they want. but the
world lives without players driving the action"

"now for testing purposes, we can force the lineup outside the bank to have
certain items for sale so we can test and iterate, but the ones in the actual
shops should be participants in the world fully"

"copper, silver, and gold should be mineable as well, and gold is always mined
by appointment of the King. the economy is supposed to be bootstrapped at server
initialize by lord british. he is supposed to go gold mining as a first economic
activity."

Root's reading, binding until Damian corrects it:

- The chain is harvest, transport, refine, transform, use: a world node yields
  the raw good, an independent NPC gatherer walks out, gathers, walks home,
  refines (or sells to a refiner), and sells to the maker; the maker sells the
  usable to the user; a fighter NPC adventures and returns with loot whose sale
  closes the loop.
- The bottom bootstraps itself: a gatherer's tool always yields low-grade ore,
  so gathering needs no coin.
- Makers start broke and borrow from a lender NPC (Lord British, a mayor or a
  banker) to buy their first inputs.
- Copper, silver and gold ore are all mineable, and the Crown takes 10% of each
  in kind at harvest (R8, UOAIX.md section 5); iron and other ore are untaxed.
  Copper and silver ore any gatherer may mine; gold ore only a miner holding
  the King's appointment may mine, and a gold node refuses anyone else.
- The economy bootstraps at server initialization: on a new world, Lord
  British's first economic activity is to go gold mining, as an NPC when no
  human drives him, and that gold primes the treasury before any loan is
  made. A human logged in as British can take over the same errand.
- The crown always makes change by recoining the coin handed in (UOAIX-213);
  the copper and silver faucets set how much small coin circulates, not
  whether change can be made.
- Damian, 2026-10-07: "the treasury should start empty. lord british should
  get to investigate the starting world state, and see what happens when he
  puts the gold in the treasury. having it automatically start hides the magic
  from the admin. So new world means the npcs are all in a standby mode except
  the harvestors, who can start getting ore and ingots ready, crops harvested
  etc. but nobody can buy until there is a loan from the king to kick start it
  all." A new world mints no founding ingots and makes no automatic grant or
  loan; harvesters mine, smelt and harvest from the start; every purchase and
  sale waits until British's own strike puts gold in the treasury. Damian,
  2026-10-07: "the loan is entirely on the books, not a process a player can
  take. only an npc shop keeper who isn't a harvestor. they need ingots to make
  stuff, or wheat to make flour to make bread. then they pay back the loan from
  revenues collected by sales of the items they sell." Each Britain shop with
  no open crown loan borrows from the treasury on the books once it holds gold,
  buys its inputs from the harvesters, makes and sells, and repays from revenue
  (`bb-repay`).
- Every faucet has a matching drain; Lord British's bank is always one gold
  drain.
- The loop runs with zero players connected. A player takes any role (gatherer,
  refiner, maker, fighter, or several at once) through the same buy, sell and
  craft paths the NPCs use, competing with them; no step waits on a player.
- Every shopkeeper in an actual shop is a full participant: buys inputs, borrows,
  makes or resells, and sells, with no forced stock. Each actual shop stops
  force-stocking in the CL that lands its input chain, so residents never lose
  bread (root, 2026-10-06).

A loop step is proven headless in the composite, on a fresh disk and again
after a restart from the same disk, by a census: coin issued, coin in each
purse, goods by holder, and the audit book's completed trades by kind;
conservation must hold.

| Step | Where it lives | Proof |
|---|---|---|
| Gatherer: walk out, mine low-grade iron with a self-made pickaxe, walk home, smelt at the forge, sell ingots to the blacksmith through vendor checkout | `EconomyGatherer.codex`, `CompositeGatherer.codex` | `EconomyGathererProof`, `CompositeGathererProof` |
| Participant shops | BritainShops.md, UOAIX-55 | `BritainShopsProof`, `CompositeGathererProof`, `CompositeFarmerProof` |
| Fighter: buy a sword on a crown loan, hunt, loot, sell loot, repay | `EconomyFighter.codex`, `CompositeFighter.codex` (below) | `CompositeFighterProof`, `CompositeWorldSpawns` |
| Gold faucets and drains | below | `GoldFindTaxProof`, `CrownMintProof`, `GoldLedgerProof`, `BritishErrandProof` |
| Laborers: residents paid hourly by their employer shop, then buy food and eat | `CompositeLabor.codex`, TownNetworkLive.md | `CompositeLaborProof` (UOAIX-79) |
| Player parity: a scripted session (no client, packets replayed) trading through the same paths | `CompositeScript.codex` | `CompositeScriptProof` |

Open: the provisioner, healer and innkeeper have no input chain of their own
(founding stock only; the others restock at their stations, UOAIX-55); the
fighter has made no kill in the live hunt; a scripted player does not yet
compete with the NPCs' trades.

`CompositeScript` does not replay the 0x80/0x91 login handshake: `csp-login`
marks the bound account logged in, then creates the character.

### The headless world tick

The server's links loop calls the world tick once per second whether or not a
client is in the world (`gl-tick` in `GameLinks`, `cp-tick` in
`CompositePaging`, `cs-tick` in `CompositeStore`): it pages the shared walk
window around the gatherer and calls `cg-world-tick owner game map watcher tick`
(`CompositeGameRules`) with the gatherer as `watcher`, or advances only the town
and the wheel when there is no gatherer, then commits as a pulse does. A refused
world tick is logged and world ticks stop until a restart. A stage proof drives
the same call once per step, where `watcher` is the NPC's serial, `map` a walk
window the caller loaded around it and `tick` the PIT clock
(`proofs/WorldTickProof`). Each call takes one gatherer step (`cg-gather`), then
one world-spawn pass around the watcher (`gws-world`), then one round of combat
swings (`cb-swings`); it answers 1 when the spawn controller is attached and 0
when it is not.

### The fighter

Rulings that bind `CompositeFighter` (root, 2026-10-06): the fighter's route is
a window of its own (`cgf-window`, 56x88 tiles from 1368,1472, loaded by
`cgs-route` and bound by `cs-load-town`), and the route never widens the
Britain window; it hunts in UOX3's Britain Cemetary (region 5506), whose undead
all carry gold. It takes its prey one at a time: the weakest gold-carrying
spawn within 12 tiles with no other live spawn within 8 tiles of it; it fights
back a sole attacker and retreats when two or more spawns are on it. A crown
loan for a sword is rounded up to whole gold, carries no interest and is due
at the ledger's last hour so it never defaults; at most one loan is open, so a
fighter that died in debt and cannot pay a sword's quote hunts unarmed. The
fighter fights as a new ServUO Warrior (profession 1: strength 45, dexterity
35, Swordsmanship, Tactics and Anatomy 30). An NPC's death deletes its mobile,
so the fighter's death settles at the blow: its lots leave the vendor ledger
as consumed goods and lie in the corpse as plain items, and it is raised at
home, unarmed, for the same economy actor. Its loot is the lots it takes from its kill's corpse
(`cg-fighter-loot`); a kill makes nothing from nothing.

### Crown loans (Damian, 2026-10-06)

"the crown's loans are to be paid back as soon as the business can afford it.
this isn't late stage capitalism here." Reading, binding until Damian corrects
it: a borrower repays from its purse whenever it holds more than it needs to
keep trading; a due hour forces no default and no penalty. A debt is a value in
copper units at the current rates (root, 2026-10-06): a repayment in any mix
of copper, silver and gold counts toward it at value with no exchange step,
and the lender receives the coins as paid. A loan keeps whole units of its
metal, so a repayment settles whole units (`eu-repay-value`, currency event
13). The loan is credited by a zero-coin ledger row of kind 18 (`em-credit`,
amount the loan units, no purse), which the loan audit counts as repaid. Shop
loans fall due at `em-limit`; a shop keeps working capital worth its loan's
principal and repays whole gold units from the rest (`bb-repay`, every town
step).

### Gold faucets and drains (root's readings, 2026-10-06, open to Damian's correction)

1. Gold coin is what is counted; it enters only by `eu-strike` from gold
   ingots into the treasury. Gold ore is a material.
2. A find is taxed in kind at harvest: `eh-find-tax` rolls each gold, silver
   or copper ore a non-mint actor finds against `eh-mining-tax` (10%,
   independent of the trade tax rate; a per-unit roll seeded by the finder's
   attempt counter, so a 2-ore find still pays on average) and moves the taxed
   units, with their quality, wear and cost, to the mint actor, counted per
   metal in the world's `taxed` table (UEC1 version 6).
3. Lord British's errand primes the pump (`CompositeBritishErrand`, wheel kind
   5): at boot the server creates British on his throne (1323,1624 z 55) and
   binds two route windows, Britain's (136 by 104 from 1312,1608) and Wind's
   (24 by 24 from 5352,64, the Royal Mine). With no human driving him he walks
   into the curtained alcove behind the throne, mines the Royal Mine's four
   gold veins as the crown actor, returns through the gate, and at the tile
   south of the Britain banker the crown smelts his ore and strikes it into the
   treasury. The errand runs once per world; any pulse from British's own
   session pauses it for 3 s, and it resumes from where the human left him. No
   NPC economic action runs until the errand has struck his gold. The server
   logs each leg ("BRITISH errand leg N"); a format 1 errand that finished stays
   finished and any other restarts at leg 0.

   Damian, 2026-10-07: "lets start a fresh new world, and let me play the task
   of lord british stocking the treasury and not let the bot do it for me." In
   testing mode the errand never runs (`cg-british-arm`, `cg-british`). The
   economy opens when British's own session strikes gold ingots (item 32) at
   the Royal Minter (`cg-minter-or-plant`), in any mode.
4. The drain is coin that reaches Lord British's bank (the treasury purse:
   taxes, loan interest, repayments) and never leaves except by grant or loan.
   `proofs/GoldLedgerProof` checks every treasury-touching gold ledger row
   against the currency-log event whose `gold-seq` is that row's action.

A warrant is an economy station of kind 13 (`eh-warrant`) owned by a miner at a
place, made only by `eh-appoint` with the King's verified word. `eh-harvest`
refuses gold ore to an actor who is neither the mint owner nor warranted at the
node's place; before a crown exists (mint owner 0) gold is open, so the
founding King mines first.

Copper and silver ore (Damian's ruling, root's reading 2026-10-06): any miner
with a pickaxe may mine them. A new pickaxe site on a mountain tile draws its
vein from the tile (`cpr-vein`): copper ore at ServUO's copper vein chance,
8.4%; silver ore at 11.2% (UO has no silver ore); iron ore otherwise. Every
player mining attempt can also turn up a seam (`gh-seam-begin`): gold 1%,
silver 2%, copper 3% a roll, a temporary node the site mines until one yield
works it out. A gold seam waives the appointment and is never royal gold; a
Royal Mine site takes no seam. Before every composite commit the crown smelts
its copper and silver ore two at a time and strikes each ingot at the founding
yield (`bb-crown-mint`, at most 16 ingots per metal per commit; a single ore
waits for a second). The mint buys nothing (R8): an owner has its own ingots
struck at the crown's mint and the Crown keeps 10% as the minting fee
(EconomyMint.md). The copper and silver gatherer smelts its own ore, spending no
fuel (UOAIX-80), and has each ingot struck into its own
purse (`bb-coin`), so the faucet scales with mining.

The Royal Mine's four gold veins (`cpr-royal-veins`, within 2 tiles of the arrival
5360,76) never deplete (`eh-royal-gold`) and carry no marker (Damian, 2026-10-07:
"keep them unidentified. they should look like normal surface ore spots, the
same as iron. nothing special except the secret we share about it.").

Britain's mine: `cg-install` installs a wild gold-ore node at the crown's place
and a pickaxe harvest site at the rock tile 1451,1528 z 40 (`cpr-mine-install`),
on a new world and on the first boot of an older one.

## Authority, rates and tax

The default is 100 copper = 10 silver = 1 gold. `eu-rates` accepts the number
of copper and silver coins per gold coin. Both are positive, copper is at most
1000000, and copper must be divisible by silver. This native policy represents
each denomination as a whole number of copper units. `eu-value` returns the
current gross value in copper units, including zero for an empty bundle.
The rate changes valuation only. It moves no coin and rewrites no debt,
issuance count or historical event. Historical rows retain the rates then in
force.

`eu-rates`, `eu-tax`, `eu-mint-policy` and `eu-grant` require an actor backed
by a crown-business purse and a caller-verified Lord British identity. The
Boolean argument means the server has authenticated Lord British and bound
that actor to him. A crown purse alone does not confer this authority; another
royal business or a GM cannot supply the flag from client input. The panel
adapter must derive it from its current authenticated role and grants.

`eu-tax` sets one basis-point rate for all three ledgers. Payments floor tax
separately to whole coins of each denomination. Fractions are not minted,
converted or carried. Treasury transfers are tax-free. Splitting a payment can
affect whole-coin rounding; no stronger fractional-tax invariant is claimed.

## Minting, transfers and loans

`eu-mint-policy` sets the named metal's coin yield per ingot and records the
authenticated actor. Each yield starts disabled at zero and is bounded by
1000000000. `eu-strike` requires verified mint admission, that configured
actor, his enabled mint at his current place, a charged stone hammer and
1..1000 matching ingots. It consumes copper 65, silver 66 or gold 32 and credits
only the corresponding treasury. Coin and material ledger rows share one
denomination action; the currency event binds that action to the other ledgers.
Yield and exchange rate are independent policies.

`eu-pay` takes verified payer authority, payer/payee purse IDs and explicit
copper/silver/gold counts. Nonnegative counts through 1000000000000 are allowed,
with at least one positive. It preflights all components before debiting any,
then moves the actual tender and whole-coin tax. It refuses treasury-origin
ordinary payments. `eu-grant` moves a specified bundle from treasury to a town
or royal business, without issuing coin. Player support uses admitted loans.

`eu-sale` binds an explicit bundle to an integer sale price. Both purses must
belong to admitted economy actors. The bundle's value at the current rates must
equal the price exactly. It pays the actual coins and per-metal tax, then
writes a zero-coin gold-ledger valuation row (kind 15, reference = quoted
price) and currency event kind 11. The marker does not issue, transfer or claim
payment of gold. Material `EpTrade.action` links to that marker; the enclosing
transaction must append the matching material trade before exposing or saving
state. Currency validation matches each sale event to its trade and
reconstructs the actual tender.

`eu-lend` and `eu-repay` name one metal and otherwise use the existing lender,
borrower, principal, interest, due-hour and partial-repayment rules. Loan IDs
are local to a denomination; callers must retain the metal with the ID.
Verified lender/borrower arguments represent checked authority over the named
purse and any required loan consent. Treasury lending requires royal authority.
The native Boolean does not itself authenticate that consent.

`eu-hour` advances all three ledgers and the shared material world by one game
hour, reserving room for all due defaults first. Live wall-time conversion is
the clock owner's job. The sequence checks prohibit independently advancing the
gold-era clock after attaching this coordinator.

## History, census and persistence boundary

The append-only currency log binds each operation to the post-operation
copper/silver/gold money sequences. Rows contain sequence, game hour, kind,
actor, party, metal, three original tender counts, reference and both rates.
Kinds are purse admission 1, rates 2, tax 3, payment 4, grant 5, mint policy 6,
mint strike 7, loan 8, repayment 9, hour 10, valued sale 11, exchange 12,
repayment by value 13 and theft 14. Actor means an `EpActor` for royal
policy/mint/grant rows, a purse for payment/loan/repayment/sale rows, a legal owner
for admission, and zero for clock rows. Party and reference are interpreted
by kind; loan details remain in the denomination's existing loan record.
The event log, the three ledgers and the trade log roll over together at an
economy-hour boundary (`eu-seal`), and vendor sales roll over at checkout;
validators then start from a carried opening.
[UoaixSealedSegments.md](../../docs/Designs/Active/Apps/UoaixSealedSegments.md)
owns the rollover, its formats and what a seal does with the sealed rows.

`eu-census` checks identity alignment, every purse against its own ledger,
each metal's purse total against its issued count, shared tax policy and the
material census. Consumed-ingot totals must cover each mint's recorded ingots.

UEC1 stores only the gold/material world. Saving it alone after attaching
currency would lose copper, silver and policy history.
[EconomyCurrencyCodec.md](EconomyCurrencyCodec.md) defines EUC1, preserving
all three ledgers, loans, mint counters and the currency log together.
[EconomyCurrencyInput.md](EconomyCurrencyInput.md) defines ordered EUI1 replay
and standalone restart recovery. [TreasuryPanel.md](TreasuryPanel.md) owns the
authenticated tax, grant and exchange-rate adapter. Never silently treat an old
gold balance as copper or reinterpret an old UEI1 outcome.
[GameVendor.md](GameVendor.md) defines exact mixed-metal shop tender.

## Cost

Each ledger keeps 256 purses, 128 loans and 8192 rows; the currency log adds
8192 fixed 15-field records. No action or hourly update retains heap. Ordinary
payment, grant, policy and mint work is O(1); a valued sale also scans up to
128 admitted actors, and its checkpoint linkage uses a binary search over
material trades. Admission scans up to 256 purses; hourly processing scans up
to 128 loans per metal plus the material world's sources, actors and item
types. Census costs O(purses * ledger rows + item types * actors), with fixed
bounds.

`proofs/EconomyCurrencyProof.codex` starts with empty inventories and obtains
all mint inputs through gathering, tools, mining and smelting. It grades
disabled and unauthorized minting, mixed tender and tax, preflight failure in a
later metal, event capacity, repricing without coin or debt conversion,
repayment, exchange, theft, 30 daily censuses and flat hourly heap against its
complete oracle.
