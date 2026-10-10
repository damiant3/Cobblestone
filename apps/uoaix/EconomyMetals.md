# Copper and silver production

`EconomyMetals.ex-catalog 0` extends the gold-era catalog with copper and silver
ores, ingots and coin definitions. Existing item, resource and recipe IDs stay
unchanged. `eg-standard` remains the gold-era fixture; a caller explicitly
chooses the extended catalog for a new economy. Existing UEC1 checkpoints
already carry their complete catalog and retain their saved meanings.

| Metal | Ore item | Resource | Ingot item | Smelting recipe | Coin item | Mint recipe |
|---|---:|---:|---:|---:|---:|---:|
| Gold | 6 | 6 | 32 | 8 | 51 | 30 |
| Copper | 63 | 25 | 65 | 32 | 67 | 34 |
| Silver | 64 | 26 | 66 | 33 | 68 | 35 |

Copper and silver use the existing mining skill and stone pickaxe. The copper
source yields up to four ore and regrows in 48 game hours; silver yields up to
two and regrows in 72 game hours. These are simulation defaults. Each smelting
recipe lists two matching ore and one fallen branch at an owned smelter; `ep-craft`
spends no branch on a smelt and rolls Mining (UOAIX-108, Damian 2026-10-07),
using metalworking practice. No stock or trained skill is granted at admission.

All three coin edges require the royal mint and stone hammer. Their unit
quantity proves material closure only. Generic production refuses station 12;
this component cannot issue currency. Royal mint policy must set each actual
yield and consume the matching ingot atomically with issuance. The exchange
rate (EconomyCurrency.md, "Price scale") is not a mint yield.
Denominated balances, tax, exchange-rate panel actions and physical coin
graphics belong to the currency/world adapters and are not implemented here.

The extended catalog has 68 items, 26 resource definitions and 35 recipes,
within the existing 128/64/128 bounds. Construction is linear in the table
size, retained below the proof's 16 KiB bound. Closure remains
O(items * (resources + recipes)) with reclaimed scratch. Runtime harvest and
smelting retain no per-action heap; material census is O(items * actors).

`proofs/EconomyMetalsProof.codex` checks preserved identities, closure, actual
tool bootstrap, novice mining and smelting, ordinary mint refusal, material
conservation and the existing checkpoint's recovery of the extended catalog.
No claim of coin circulation or live client integration follows from this proof.
