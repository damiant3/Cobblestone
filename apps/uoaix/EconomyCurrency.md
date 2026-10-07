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

Implementation. A world whose catalog carries the metals (68 items or more, the
same marker `ev-shape` uses) is a copper world; a gold-only economy keeps its
coin-unit prices and pays no labor.

- Rates: a new Britain world logs `eu-rates` 700 copper and 70 silver per gold
  through the royal actor at founding (`bb-coin-copper`, `bb-coin-silver`).
  Fresh world disks are the default; there is no migration.
- Unit: in a copper world `ep-price`, cost basis, `EpTrade.gross` and
  `GvSale.gross` count copper. `eu-sale` requires the tender's copper value to
  equal the price; every vendor sale settles through it, and `gv-tender-plan`
  spends gold in `copper-rate` steps, then silver, then copper.
- Labor: `ep-labor` adds copper per batch at each craft (`ep-craft`), and each
  gathered unit costs 1 copper (`eh-add-yield`, harvest lots carry it as basis).
  Scarcity and demand are proportional to unit cost in a copper world.
  Measured in `BritainShopsProof` (seed `4228CD5103DC4523`, 2026-10-05): a
  longsword quotes 708 copper, bread 3 and a pizza 4.
- Change: `eu-exchange` (currency event kind 12; money rows kind 6 in, 16
  decree, 17 out) breaks a purse's coin with the crown at the current rates,
  at any distance (Damian, 2026-10-05). When the treasury lacks the smaller
  coin, the crown decrees it into being and the event's party cell records the
  amount; the composite then logs a FAULT line asking why the kingdom has gold
  but no copper or silver. Vendor checkout breaks the payer's coin first
  (`gv-change`), so a gold-only buyer can pay a copper price.
- Residents' market: funds and bids compare in copper. The frozen town network
  reads prices in gold through `ep-price-gold`, which keeps its trained range.
- Money supply: vendor placeholder loans are 1 gold without interest.

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
- Copper, silver and gold ore are all mineable, and each coin metal reaches the
  treasury the same way: taxed in kind at harvest, smelted and minted by the
  crown. Copper and silver ore any gatherer may mine; gold ore only a miner
  holding the King's appointment may mine, and a gold node refuses anyone else.
- The economy bootstraps at server initialization: on a new world, Lord
  British's first economic activity is to go gold mining, as an NPC when no
  human drives him, and that gold primes the treasury before any loan is
  made. A human logged in as British can take over the same errand.
- The crown's change decree stays as the alarm: its FAULT means the copper or
  silver faucets fell behind demand (UOAIX-30).
- A fresh composite world's crown starts with a one-time founding stock of 2
  copper and 4 silver ingots, minted at founding (`bb-founding-stock`, root
  2026-10-06), so wages break gold into small coin before any ore is mined. It
  is material, never a coin float; after founding the mint takes only mined ore.
- Every faucet has a matching drain; Lord British's bank is always one gold
  drain.
- The loop runs with zero players connected. A player takes any role (gatherer,
  refiner, maker, fighter, or several at once) through the same buy, sell and
  craft paths the NPCs use, competing with them; no step waits on a player.
- The bank lineup (the shopkeepers at 1434-1443,1694) is a testing fixture: its
  stock is forced to fixed items so a feature can be tested and iterated, and
  it stays outside the loop. Every shopkeeper in an actual shop is a full
  participant: buys inputs, borrows, makes or resells, and sells, with no
  forced stock.

### Stages (UOAIX-20, red, 2026-10-06)

Measured before the plan (`proofs/CompositeBritainWorld.codex` boot 1, seed
F2F7B731, 68 economy hours, no client): the three residents hold 0 coin
throughout, their goal is food (item 34) and every live merchant sell of it is
unaffordable, so all buys log -16 (`gla-execute`, an empty proposal); the
offer book holds 42 live offers and is not the limit; the 4 sells the network
proposes fail because a resident owns no goods. Actual Britain shops other
than the blacksmith are force-stocked at creation (`bb-populate-at`,
`bb-stock`); every shop is funded by the crown's 1-gold placeholder loan.

Every stage proof runs headless in the composite (no client connected), on a
fresh disk and again after a restart from the same disk, and prints a census:
coin issued, coin in each purse, goods by holder, and the audit book's
completed trades by kind. A stage is done when its loop step completes without
a player and conservation holds.

| Stage | Builds | Proof (no client) |
|---|---|---|
| 1 | One independent gatherer NPC with a self-bootstrapping tool: walks from home to an ore node outside town, gathers low-grade iron ore (`eh-harvest`), walks home. No coin needed. Built: the economy side (`EconomyGatherer.codex`) and the composite binding (`CompositeGatherer.codex`, CompositeGame.md): a world mobile lodged at the Sweet Dreams Inn walks to its node on the west edge of the Britain map in `cg-world-tick`, mines, walks home, and persists in UCC1 version 14. Open: the economy clock does not advance with no client, so a mined-out node regrows only when a player's pulses move the hours. | `proofs/EconomyGathererProof`: 2 iron ore in 96 economy hours from a self-made pickaxe and no coin. `proofs/CompositeGathererProof`: in the composite with no client, one trip home to node and back with 1 ore, census valid; after a restart the gatherer is home with its ore, waits while the node is mined out and sets out again once it regrows. |
| 2 | Refining and the first sale: the gatherer smelts ore to ingots at a station (`ep-craft`) and sells them; the smith shop, funded by its crown loan, buys them. Built (`CompositeGatherer.codex`): 2 ore smelt into an ingot lot with the gatherer standing within two tiles of the Hammer And Anvil's forge (UOAIX-55), and the gatherer walks to the blacksmith and sells it through vendor checkout at the blacksmith's bid, the path a player's sale takes. | `proofs/CompositeGathererProof` boot 2: one ingot moves gatherer to smith, the gatherer gains 12 copper and the smith pays 13 (1 copper tax), census valid; boot 3 keeps the sale. The smith's first purchase breaks its loaned gold and the crown decrees 10 silver for change (the composite's treasury FAULT line). |
| 3 | Shops become participants: actual Britain shops stop being force-stocked (`bb-stock` stays only for the bank lineup fixture at 1434-1443,1694); the smith crafts a longsword from bought ingots and offers it for sale. Order (root, 2026-10-06): each actual shop stops force-stocking in the CL that lands its input chain, so residents never lose bread; the end state stays every actual shop a full participant. Built: the blacksmith (`bb-participant`, `bb-forge` in BritainShops.md). Open: provisioner, tailor, baker (after a farmer and a miller), healer, innkeeper. | `proofs/CompositeGathererProof` boot 3: after the gatherer's second sale the blacksmith forges one longsword into its stock and holds no ingot; boot 4 keeps it. `proofs/BritainShopsProof`: the actual blacksmith starts empty, forges from a player's ingots and the sword quotes 706 copper; `proofs/CompositeBritainWorld`: the lineup blacksmith keeps its stock. |
| 4 | The fighter: an NPC buys the weapon, adventures against spawned monsters, returns with loot (gold and items), and sells the loot. Built: the headless world tick with spawns and combat (below), and the economy side (`EconomyFighter.codex`: `ef-loot` admits loot gold as found gold ore, item 6, through `ep-find` and pays the crown's share through `eh-find-tax-at` with no cost basis; `proofs/EconomyFighterProof`); spawn profiles carry UOX3 GOLD ranges, and a kill by the fighter loots the victim's roll through the combat `death-hook` (`CompositeFighter`, `cgf-bind`; `proofs/CompositeWorldSpawns`); the Britain world founds the fighter as a world mobile at its own home with an economy actor, persisted as UCC1 version 15 (`proofs/CompositeGathererProof`). The errand runs on the WorldTimers wheel (kind 4, `CompositeFighter`): every game hour at home the fighter walks the Britain window through the lodging entrance to a trailhead at its north-west corner (1408-1415 by 1536-1551), then a route window of its own (`cgf-window`, 56x88 tiles from 1368,1472, loaded from the map cache by the server (`cgs-route`) and bound by the load (`cs-load-town`); no spawn region meets the Britain window, and root ruled 2026-10-06 that the route never widens it) to the south-east corner of UOX3's Britain Cemetary (region 5506), whose undead all carry gold. At the lodging entrance an unarmed fighter first walks to the blacksmith while it offers a longsword, borrows what it lacks from the crown (gold rounded up, no interest, due at the ledger's last hour so it never defaults; root 2026-10-06 under Damian's loan ruling, at most one open loan), buys the sword at the smith's quote through vendor checkout onto itself, wields it on layer 1 and goes on to hunt. It takes its prey one at a time (root's ruling 2026-10-06): the weakest gold-carrying spawn within 12 tiles with no other live spawn within 8 tiles of it, keeping a live target; it leaves at its first kill, after 128 steps, or below half health, walks home the same way, and regains 7 hits per idle hour at home (one per 40 s), setting out only at full health. Each swing practises its combat skill by the classic gain rule players use (`cgf-swing` on the combat `swing-hook`), which CGF1 keeps across death and restart. An NPC's death deletes its mobile, so the fighter's death settles at the blow (its death hook, before `cb-die`): its lots, the longsword among them, leave the vendor ledger as consumed goods and lie in the corpse as plain items, its vendor owner row goes, and it is recorded dead; the next errand step raises it at home, unarmed, for the same economy actor. At most one open loan: a fighter that died in debt and cannot pay a sword's quote hunts unarmed. CGF1's qword 88 carries the errand; zero is home with no errand. The fighter fights as a new Warrior character (ServUO profession 1: strength 45, dexterity 35, Swordsmanship, Tactics and Anatomy 30) with the longsword's UOX3 damage (GameCombat.md). Measured (`proofs/CompositeFighterProof` boot 2, its BLOWS line, 2026-10-06): walking straight to the hunt spot, ten undead converged on the fighter, nothing was lone and it never swung; so it now fights back a sole attacker, retreats when two or more spawns are on it, and starts hunting where a lone prey or a sole attacker first comes into sight. Starting at its full 45 hits it then landed 1 blow for 27 and took 6 for 30, retreating at 15 before a kill; 2000 swings against a 90.0 defender raise 30.0 to 35.3 under the gain curve (SkillCurve) (boot 4, PRACTICE line). Home from a hunt with loot, the fighter turns at the lodging entrance to the blacksmith, sells all its gold ore as one lot at the smith's bid (`gvb-material` role 1, `cgf-sell`) and repays the crown's loan at value from whatever coin it holds (`cgf-repay`); measured (`proofs/CompositeFighterProof` boot 4, LOOT line, 2026-10-06): 120 ore looted, 108 kept after the crown's share, sold at 1, coin value 28 -> 126. At the measured 700 copper to the gold, 126 copper does not yet cover the 1-gold loan, so it stays open until more hunts pay. Open: a kill in the live hunt (the boot 2 hunt still retreats before one). | Loot gold enters through a taxed faucet; the fighter's purchase and the loot sale both complete. |
| 5 | Gold faucets and drains: NPC gold miners taxed automatically; Lord British mining gold on first login primes the pump; Lord British's bank absorbs gold as the standing drain. | Gold issued minus gold drained equals gold held, every game day. |
| 6 | Laborers: the residents (Nell, Jorin, Mira) work for the shops and are paid from the shops' loaned purses, then buy food and eat. Closes UOAIX-20. Built (`CompositeLabor.codex`): an hourly shift timer per resident on the WorldTimers wheel (kind 2) pays 1 copper from the employer shop (Good Eats, the Hammer and Anvil, the Lords Clothiers) when the resident stands at work, and in a scheduled work hour directs the resident to work for the next hour; the town network decides only off-shift (`gla-shift`, root 2026-10-06). The headless world tick advances the town clock, so residents, the network and economy hours run with no client. A survival guard puts a food buy first for a resident who is hungry, holds no food and can pay for an offer (`tnv-guard`, TownNetworkLive.md). Food is restocked: the farmer harvests a nine-node wheat field (`cpr-field-nodes`), mills flour and sells it to the baker, who bakes bread at its oven, and the merchants' market posts again as each economy day begins. | `proofs/CompositeLaborProof`, three game days across a restart: every resident is paid 9 copper a day (7 on the restarted day) and buys and eats on every day that opens with at least one food unit offered per resident; a day that opens with fewer prints the shortfall; census valid. |
| 7 | Player parity: a scripted session (no client, packets replayed) mines, smiths or fights through the same buy, sell and craft paths and competes with the NPCs. Built: the harness, `CompositeScript.codex` (`csp-login`, `csp-resume`, `csp-send` through `cs-route`, `csp-pulse`, `csp-walk-to`, `csp-wait` (a bounded real-time wait for cooldowns), packet builders; a target cursor's context is taken from the last reply). It does not replay the 0x80/0x91 login handshake: `csp-login` marks the bound account logged in, then creates the character. `proofs/CompositeScriptProof`: Lord British walks to the lineup smith, buys a pickaxe (the sale is in the audit book), mines ore and commits; after a restart he resumes holding it. Open: competing with the NPCs' trades. | The scripted player's trades appear in the same audit book with the NPCs' and conservation holds. |

The headless world tick: `tf-step` and `tl-advance` advance the town
simulation's hours, not the composite world's mobiles, spawns and combat.
The server's links loop calls the world tick once per second whether or not a
client is in the world (`gl-tick` in `GameLinks`, `cp-tick` in
`CompositePaging`, `cs-tick` in `CompositeStore`): it pages the shared walk
window around the gatherer and calls `cg-world-tick owner game map watcher tick`
(`CompositeGameRules`) with the gatherer as `watcher`, or advances only the town
and the wheel when there is no gatherer, then commits as a pulse does. A refused
world tick is logged and world ticks stop until a restart. A stage proof drives
the same call once per step, where `watcher` is the NPC's serial, `map` a walk
window the caller loaded around it and `tick` the PIT clock;
`proofs/WorldTickProof` runs one game day of world ticks beside a connected
scripted client and then with none, across heap compactions. Each call runs one world-spawn pass around the watcher
(`gws-world`): it spawns the intersecting regions' monsters, moves them, and
lets AI 2/11 monsters within 6 tiles select the watcher as their target, then runs one round of combat swings (`cb-swings`), in which an NPC defender retaliates. It
answers 1 when the spawn controller is attached and 0 when it is not. Before
the spawn pass, each call takes one step of the gatherer (`cg-gather`). Every
other NPC action is not in the tick yet; each stage adds its own step there. `proofs/CompositeWorldSpawns.codex` grades
the spawn pass and an NPC's fight with no session.

Crown loans (Damian, 2026-10-06): "the crown's loans are to be paid back as soon
as the business can afford it. this isn't late stage capitalism here." Reading,
binding until Damian corrects it: a borrower repays from its purse whenever it holds more than it needs to keep trading; a due
hour forces no default and no penalty. A debt is a value in copper units at the
current rates (root's ruling, 2026-10-06): a repayment in any mix of copper,
silver and gold counts toward it at value with no exchange step, and the lender
receives the coins as paid, which also feeds the crown's change float (UOAIX-30).
A loan keeps whole units of its metal, so a repayment settles whole units
(`eu-repay-value`, currency event 13; `proofs/EconomyCurrencyProof`).

Stage 5 readings (root, 2026-10-06, under Damian's words; open to his correction):

1. Which gold is counted? Ruled: gold coin, which enters only by `eu-strike`
   from gold ingots into the treasury; gold ore is a material.
2. Where is an NPC gold find taxed? Ruled: in kind at harvest, so each gold
   ore an NPC gathers moves the tax-rate share of that ore to the crown's stock,
   which the crown smelts and mints.
3. How does Lord British's first-login mining run with no client? Ruled: on
   his first login a gold node at Britain's mine becomes his; he mines it as a
   player, and the stage proof drives that with replayed packets (stage 7's
   harness).
4. What is "drained"? Ruled: coin that reaches Lord British's bank (the
   treasury purse: taxes, loan interest, repayments) and never leaves except by
   grant or loan. Conservation alone makes minted minus treasury equal to
   circulating, so the stage's own check is that every faucet and drain event
   appears in the currency log with the matching amount.

Reading 2 is built: `eh-find-tax` (EconomyHarvest) rolls each gold ore a non-mint
actor finds against the tax rate (a per-unit roll seeded by the finder's attempt
counter, so a 2-ore find still pays on average) and moves the taxed units, with
their quality, wear and cost, to the mint actor. `proofs/GoldFindTaxProof`.

The King's appointment is built: a warrant is an economy station of kind 13
(`eh-warrant`) owned by the miner at a place, made only by `eh-appoint` with the
King's verified word. `eh-harvest` refuses gold ore to an actor who is neither
the mint owner nor warranted at the node's place; before a crown exists (mint
owner 0) gold is open, so the founding King mines first. `proofs/GoldFindTaxProof`.

Copper and silver ore (Damian's ruling, root's reading 2026-10-06): any
miner with a pickaxe may mine them, no appointment needed. A new pickaxe site
on a mountain tile draws its vein from the tile (`cpr-vein`): copper ore at
ServUO's copper vein chance, 8.4%; silver ore at 2.8%, a first value (UO has
no silver ore); iron ore otherwise. Gold ore stays at appointed nodes only.
`eh-find-tax` taxes copper and silver ore in kind exactly as gold ore, so the
crown's share reaches its stock at harvest. `proofs/GoldFindTaxProof`,
`proofs/CompositeProductionReplay`. Before every composite commit the crown smelts
its copper and silver ore two at a time and strikes each ingot at its mint at
the founding yield (`bb-crown-mint`, at most 16 ingots per metal per commit;
a single ore waits for a second). `proofs/CrownMintProof`. The change decree
stays as the alarm: it fires while no miner works copper or silver. The mint buys copper and silver ore from any seller, gatherer NPCs and players, at a posted price paid in coin the treasury holds, gold first, then smelts and mints it, so the faucet scales with mining (root's ruling 2026-10-06, open to Damian's correction).

Britain's mine is built: `cg-install` installs a wild gold-ore node at the crown's
place and a pickaxe harvest site at the rock tile 1451,1528 z 40
(`cpr-mine-install`), on a new world and on the first boot of an older one. A
pickaxe site can point at any node the pickaxe mines, gold ore included.
`proofs/BritishFirstMineProof`: Lord British buys a pickaxe, walks to the mine and
mines gold ore; after a restart nothing is installed twice.

Lord British's errand is built (`CompositeBritishErrand`, wheel kind 5): at boot
the server creates British on his throne (1323,1624 z 55), founds the errand
there and binds two route windows from the map cache before serving: Britain's,
136 by 104 tiles from 1312,1608 (the alcoves, the throne, the bank and the
entry point), and Wind's, 24 by 24 tiles from 5352,64 (the Royal Mine,
UOAIX-60). With no human driving him (F48) he walks into the curtained alcove
behind the throne, which moves him to the Royal Mine's arrival, mines its four
gold veins as the crown actor (the mint owner, the actor his own session binds;
a mined-out vein passes to the next, and a worn pickaxe is remade), walks to the
return gate, which moves him to the throne room, walks to the tile south of
the Britain banker, and there the crown smelts the gold ore he mined, two ore
to the ingot, and strikes it into the treasury. The errand then ends; it runs
once per world. Any pulse from British's own session pauses it for 3 s, and it
resumes from where the human left him, including on the far side of either
teleporter. The server logs each leg ("BRITISH errand leg N"). Save format 2;
a format 1 errand (Britain's mine) that finished stays finished and any other
restarts at leg 0. `proofs/BritishErrandProof`: the creation on the throne,
the walk to the alcove, a takeover by British's session, the arrival in Wind,
a restart mid-mining, the walk back through the gate to the bank, the struck
gold in the treasury and the finished errand surviving a restart.

Reading 4 is built as `proofs/GoldLedgerProof`: each day prints the census
(minted, treasury, circulating) and checks every gold ledger row that touches
the treasury (payer 0 mints, payer 1 leaves it, payee 1 drains into it) against
the currency-log event whose `gold-seq` is that row's action: a payment's gold
equals the sum of its bound rows, and any other event's gold equals the row.

## Authority, rates and tax

The default is 100 copper = 10 silver = 1 gold. `eu-rates` accepts the number
of copper and silver coins per gold coin. Both are positive, copper is at most
1000000, and copper must be divisible by silver. This native policy represents
each denomination as a whole number of copper units. `eu-value` returns the
current gross value in copper units, including zero for an empty bundle.
The rate changes valuation only. It moves no coin and rewrites no debt,
issuance count or historical event. Historical rows retain the rates then in
force. There is no automatic change-making or bank exchange operation here.

`eu-rates`, `eu-tax`, `eu-mint-policy` and `eu-grant` require an actor backed
by a crown-business purse and a caller-verified Lord British identity. The
Boolean argument means the server has authenticated Lord British and bound
that actor to him. A crown purse alone does not confer this authority; another
royal business or a GM cannot supply the flag from client input. The panel
adapter must derive it from its current authenticated role and grants.

`eu-tax` sets one basis-point rate for all three ledgers. Payments floor tax
separately to whole coins of each denomination, as the existing gold ledger
does. Fractions are not minted, converted or carried. Treasury transfers are
tax-free. Splitting a payment can affect whole-coin rounding; no stronger
fractional-tax invariant is claimed.

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

`eu-sale` binds an explicit bundle to an integer gold-denominated sale price.
Both purses must belong to admitted economy actors. The bundle's value at the
current rates must equal the price exactly. It pays the actual coins and
per-metal tax, then writes a zero-coin gold-ledger valuation row (kind 15,
reference = quoted gold price) and currency event kind 11. The marker does
not issue, transfer or claim payment of gold. Material `EpTrade.action` links
to that marker; the enclosing transaction must append the matching material
trade before exposing or saving state. Currency validation matches each sale
event to its trade and reconstructs the actual tender. Gold-only vendor
purchases retain the existing payment/trade representation.

`eu-lend` and `eu-repay` name one metal and otherwise use the existing lender,
borrower, principal, interest, due-hour and partial-repayment rules. Loan IDs
are local to a denomination; callers must retain the metal with the ID.
Verified lender/borrower arguments represent checked authority over the named
purse and any required loan consent. Treasury lending requires royal authority.
The native Boolean does not itself authenticate that consent.

`eu-hour` advances all three ledgers and the shared material world by one game
hour, reserving room for all due defaults first. It preserves the existing
growth, demand and hunger behavior. Live wall-time conversion remains the clock
owner's job. The sequence checks prohibit independently advancing the gold-era
clock after attaching this coordinator.

## History, census and persistence boundary

The append-only currency log binds each operation to the post-operation
copper/silver/gold money sequences. Rows contain sequence, game hour, kind,
actor, party, metal, three original tender counts, reference and both rates.
Kinds are purse admission 1, rates 2, tax 3, payment 4, grant 5, mint policy 6,
mint strike 7, loan 8, repayment 9, hour 10 and valued sale 11. Actor means an `EpActor` for royal
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
Current balances can be revalued under a new rate while all three coin-count
conservation equations remain unchanged.

UEC1 stores only the gold/material world. Saving it alone after attaching
currency would lose copper, silver and policy history.
[EconomyCurrencyCodec.md](EconomyCurrencyCodec.md) defines EUC1, preserving
all three ledgers, loans, mint counters and the currency log together.
[EconomyCurrencyInput.md](EconomyCurrencyInput.md) defines ordered EUI1 replay
and standalone restart recovery. Existing-state migration, durable composite
commit, mixed-price shops and physical coin graphics remain integration work.
[TreasuryPanel.md](TreasuryPanel.md) owns the authenticated tax, grant and
exchange-rate adapter; its durable binding remains the image owner's job.
Never silently treat an old gold balance as copper or reinterpret an old UEI1
outcome. [GameVendor.md](GameVendor.md) defines exact mixed-metal shop tender
while retaining gold-denominated prices and material cost/turnover accounts.

## Cost and proof

Each ledger keeps its existing 256 purses, 128 loans and 8192 rows. The currency
log adds 8192 fixed 15-field records. The coordinator and its two additional
ledgers retained 2333880 bytes beyond the existing economy under seed
`4228CD5103DC4523` on 2026-10-04. No action or hourly update retains heap.
Ordinary payment, grant, policy and mint work is O(1); a valued sale also scans
up to 128 admitted actors, and its checkpoint linkage uses a binary search
over material trades. Admission scans up to 256 purses;
hourly processing scans up to 128 loans per metal plus the existing material
world's sources, actors and item types. Census costs
O(purses * ledger rows + item types * actors), with fixed bounds.

`proofs/EconomyCurrencyProof.codex` uses explicitly trained fixture skills,
but starts with empty inventories and obtains all mint inputs through actual
gathering, tools, mining and smelting. It grades disabled/unauthorized minting,
mixed tender and tax, preflight failure in a later metal, event capacity,
repricing without coin/debt conversion, repayment, 30 daily censuses and flat
hourly heap. Normal and poisoned output must match its complete oracle.
