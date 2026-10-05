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
Every ledger and the currency log refuse when full; none overwrites history.

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
