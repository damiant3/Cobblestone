# Civic checkpoint

`TownGovernmentCodec` preserves offices, guard obligations, civic audit,
public works, militia assignments and dispute cases/history in TGC1.
`GovernmentCheckpoint` carries the government, works and disputes plus outer
tick and input sequence. `tgc-encode` writes that state; `tgc-decode` returns
new civic records; `tgc-decode-record` also checks exact enclosing metadata.

## Shared bindings

This component does not duplicate people, material inventories, currency,
registered titles or the keeper queue. `GovernmentBindings` supplies their
already recovered canonical instances: people, economy, optional currency,
trusted royal economy actor, registry and admin world. Supply all components
from the same encompassing commit, with the admin world bound to those people.
For currency mode, the decoder takes the economy from the coordinator itself.
Legacy mode requires a gold-era economy and royal actor zero.

The wire records money mode and royal actor ID and requires them to match the
supplied binding. It does not recover authentication or establish Lord British
identity from a saved integer. The owner verifies that identity before making
the binding. Government still requires its normal current-mayor/royal admission
on later operations. Native references and credentials never enter TGC1.

Constructed works and disputes point to the same newly recovered government.
They retain the supplied shared objects, not copies. The encoder's caller must
likewise supply works/disputes belonging to its government and the canonical
bindings. Raw record rebinding is outside the ownership contract. Keep source
state stable under one owner and keep supplied bindings below recovery scratch.
The registry must have no prepared transaction. Publish only a successful
candidate; on error, consume the diagnostic and reclaim caller scratch.

Output must be disjoint owned storage, including separation from all native
records, lists and stamp arrays. The byte buffer is not retained by decode.
Mutating decoded civic records cannot alter the old civic records; shared
world/economy/queue bindings deliberately remain shared.

## TGC1 layout

The fixed payload is 198240 bytes. All cells are little-endian integers.

| Offset | Content |
|---:|---|
| 0 | Magic `0x31434754` |
| 8 | Version 2 (the decoder also reads version 1) |
| 16 | Total length |
| 24 | Gold mode 1 or currency mode 2 |
| 32 | Outer tick |
| 40 | Outer input sequence |
| 48 | FNV32, skipping its own cell |
| 56 | Reserved zero |
| 64 | Eleven scalar metadata cells |
| 152 | Reserved zero |
| 160 | Four offices, 72 bytes each |
| 448 | 64 guards, 48 bytes each |
| 3520 | Civic events, 64 bytes each: 8192 in version 2, 2048 in version 1; every later offset below is version 1's, and version 2 adds 393216 |
| 134592 | 1024 dues-day stamps |
| 142784 | Four public-works towns, 40 bytes each |
| 142944 | 64 site bindings, 16 bytes each |
| 143968 | 128 orders, 120 bytes each |
| 159328 | 128 bounties, 64 bytes each |
| 167520 | 128 militia cells |
| 168544 | 128 worker-hour stamps |
| 169568 | 128 dispute cases, 128 bytes each |
| 185952 | 256 dispute events, 48 bytes each |

Metadata cells are town count, guard count, civic event count, civic sequence,
completed payroll day, royal actor, order count, bounty count, case count,
dispute event count and dispute game hour. Field order is explicit in
`TownGovernmentFields`. Unused fixed records must be zero. Neither pointers
nor process-local callbacks are serialized. FNV detects corruption; it is
not a signature or authority to invent a consistent history.

## Validation and limits

The decoder checks length before reading fields, then header, bindings, counts
and checksum before allocating civic records. It validates the supplied
economy and admin queue, table dimensions, identity references, enum/counter
bounds and unused records. Dead or relocated former officials and workers may
remain historical references; decoding does not reapply current eligibility
to their past actions.

Current mayor and dues policy must match the civic audit. Guard contracts must
match their hire entries and retirement history. Office paid/collected totals,
remaining funding requests, payroll day and dues stamps are reconciled with
their recorded events. Guard arrears must sum to the office's unpaid total.
Each positive civic money reference must be a distinct increasing gold-ledger
action with matching parties, game hour, kind and gross amount. Currency mode
preserves the gold ledger reference convention.

Public-works validation checks site/station/worker references, order kinds and
progress, fee/payment state, bounty winner/proof state, raid identity and
militia counts. Paid order/bounty references must identify their own civic
payment event and matching ledger receipt. Dispute validation checks legal-party/loan bindings, unique
source events, status/resolution constraints, report sequence references and
the open-to-terminal history of each case. It refuses duplicated case closure,
missing history and unknown references within those checks.

This is not a replay of every historical government or world operation.
Repair material bills, former physical locations, lawful land agreements,
historical human consent and account mapping remain facts of the trusted
encompassing commit and owning world logs. Guard arrears allocated among
successive contracts with the same NPC are preserved, but the older civic
audit does not encode a guard ID for every payment, so that allocation is not
independently reconstructed. A successful decode does not authenticate such
facts or prove a physical action happened.

## Cost and evidence

Serialization and comparison are linear in the fixed payload. Validation
rescans bounded civic history for offices, guard contracts and dues stamps;
case validation scans bounded dispute history and checks duplicate source
events. Works records scan civic history for their paid references. Receipt
lookup uses the existing ordered money ledger. Shared economy
and queue validation retain their existing costs. Validation scratch is
reclaimed; no per-record wrapper is added to the returned state. The proof
bounds the populated currency-bound decode's retention, beyond shared
bindings, below 512 KiB; validation scratch and elapsed time are not bounded
by that check.

`proofs/TownGovernmentCodecProof.codex` covers funded offices, payroll, raid
damage and repair obligations, an essential-workplace order, a paid bounty,
active militia, debt/land/misconduct cases, canonical roundtrip, detached civic
records, duplicate-dues/report refusal and rehashed corruption. Run normal and
poisoned builds against the complete oracle.

[TownGovernmentInput.md](TownGovernmentInput.md) defines ordered TGI1 replay,
the TGB1 bundle of civic/money/queue state, and a separate-VM restart witness
with unchanged external anchors. Full composite shard inclusion remains open.
TGC1 alone cannot recover currency, registered titles, physical world state or
keeper report bodies. Restore their owning checkpoints from the same commit
before supplying bindings.
