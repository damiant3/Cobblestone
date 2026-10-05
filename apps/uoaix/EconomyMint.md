# Royal mint and zero-gold bootstrap

R8 fixes the starting treasury at zero: Lord British mines the first gold.
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
- `emi-strike world authorized actor station ingots` consumes 1..1000
  actual gold ingots from the configured operator at its owned, enabled mint
  station. It requires the actor's presence and a hammer with remaining use
  charges. All coin is credited to the treasury. Refusal returns -1 without
  changing inventory or money; success returns 0 and wears the hammer once.

Each successful strike reserves two money-ledger rows before mutation: the
source-2 issuance row records coin, and kind 12 records ingot consumption with
the same action ID. The kind-12 reference is the ingot count and its payer is
the operator's purse. Its amount is zero, preserving the coin ledger's debit
and credit meaning. Kind 11 configuration records the operator's purse and
rate in a zero-coin row. `world.mint-ingots` counts consumed ingots. Gold is
removed through the material census; minted currency lives in purses, never
as ordinary item-51 inventory. The material and coin censuses remain separate.

R8's production mint terms remain **unsettled**: who can sell gold to the mint,
the buying price, and the conversion yield must be configured from Damian's
ruling. No public gold seller or mint-buyer transaction is implemented here.
The current operator only strikes gold already in its own inventory. A future
ingot purchase must be a logged transfer of existing goods and coin, followed
by a separately admitted strike. The 30-day fixture deliberately chooses
6000000 coin per ingot solely to fund its acceptance loans. That value is not
a server default or an economic calibration.

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
growth and disabled/repeated minting. Compile normally and poisoned with an
explicit depot kernel and compare the full output to its `.expected` file.
