# Royal mint and zero-gold bootstrap

R8 fixes the starting treasury at zero: Lord British mines the first gold.
The Crown taxes gold at the moment it is mined, by magic (Damian, 2026-10-06: "the Crown taxes gold at the moment of its mining, magically."):
the treasury's share of every gold ore harvest goes to the treasury at the
harvest. The mint buys nothing: an owner pays to have the owner's own ingots
struck into coin. Rates (Damian, 2026-10-06: "mining should have a 10% tax right off the top. another 10% to coin it."):
10% of the mined gold at the harvest, then 10% of the gold struck as the
minting fee. Every fee a player pays (a guild fee, for one) is a
transfer to the treasury, never a sink (Damian, 2026-10-06: "fees that take coin out should go to the crown").
Repairs are not a fee (Damian, 2026-10-06: "whoa, not repairs"; root's reading: a repair pays whoever does the repair).
All purses created by `EconomyMoney` remain empty and starting-grant issuance
is refused. The only defined new-coin source is gold ore, smelted to gold
ingots and struck at the royal mint. Decayed coin remains a treasury transfer,
not a new source or a sink.

`EconomyMint.codex` adds two trusted server operations over `EpWorld`:

- `emi-configure world authorized ownerActor coinPerIngot` logs and sets
  a crown-business actor as mint operator, with rate 0..1000000000. Zero
  disables minting. New worlds have rate zero and no operator. The Boolean
  must come from an authenticated royal administration decision; neither
  actor IDs nor purse kind establish the caller's authority.
- `emi-strike world authorized actor station ingots` consumes 1..1000 of the
  actor's own gold ingots at the operator's enabled mint station, with the actor
  present at the station's place. The operator striking its own ingots needs a
  hammer with remaining use charges, wears it once and credits all coin to the
  treasury. Any other owner needs no hammer (the fee pays the Crown's mint for
  the work): the coin is issued to the treasury and the owner is paid the coin
  less `emi-fee`, 10%, as the next ledger action. `eu-strike`
  applies the same rule to copper and silver. Refusal returns -1 without
  changing inventory or money; success returns 0.

Each successful strike reserves its money-ledger rows before mutation: the
source-2 issuance row records coin, and kind 12 records ingot consumption with
the same action ID. The kind-12 reference is the ingot count and its payer is
the striker's purse. An owner's strike adds a kind-17 payout from the treasury
to that purse as the next action; the validator requires it whenever the
payer is not the operator. Its amount is zero, preserving the coin ledger's debit
and credit meaning. Kind 11 configuration records the operator's purse and
rate in a zero-coin row. `world.mint-ingots` counts consumed ingots. Gold is
removed through the material census; minted currency lives in purses, never
as ordinary item-51 inventory. The material and coin censuses remain separate.

The mint buys nothing and sells nothing (R8): it only strikes ingots their owner
brings. The coin yield per ingot is each metal's logged mint policy
(`eu-mint-policy`); the 30-day fixture deliberately chooses 6000000 coin per
ingot solely to fund its acceptance loans, which is not a server default or an
economic calibration. A player has ingots struck by the Royal Minter, an NPC in the Royal Mint room of
Castle British at 1334,1603,72 (Damian, 2026-10-07: "the mint is in the castle
brit." and "there should be a minter NPC here that does the minting"): the
player drops a stack of their own copper, silver or gold ingots on him within two
tiles and the whole stack is struck (`CompositeMinter`,
`proofs/CompositeProductionReplay`).

The first-gold proof starts all purses at zero, admits owned workplaces and
wild source nodes, then uses ordinary harvest/craft operations for stone,
branches, pickaxe, hammer, gold ore and smelted ingot. Only after that path
does the fixture configure its explicit test yield and strike coin. No item
or gold balance is assigned directly. Bootstrap houses/workplaces are map
fixtures, not free inventory granted to vendors.

The mint adds three integer fields to `EpWorld` (operator, rate and cumulative
ingots). Calls are O(1), allocate no heap and refuse before mutation when the
treasury, cumulative issuance or ledger lacks capacity. The rate and ingot
bounds keep multiplication within the money limit. Native calls assume one
writer and valid module-owned state. The future durable adapter must commit
ingot removal, tool wear, issuance and both ledger rows in one transaction;
the current in-process operation is not a crash-atomic disk commit.

`proofs/EconomyMintProof.codex` grades first-gold provenance, starting-grant
refusal, policy authorization, station admission, generic-craft refusal,
two-row reservation, issuance/material linkage, census balance, no heap
growth, an owner's strike (refused away from the mint; struck with no hammer,
90% paid out, validator-accepted) and disabled/repeated minting.
`proofs/EconomyCurrencyCodecProof` replays owners' copper and gold strikes from
history, and `proofs/CrownMintProof` has an owner smelt its own ore and keep 90%. Compile normally and poisoned with an
explicit depot kernel and compare the full output to its `.expected` file.
