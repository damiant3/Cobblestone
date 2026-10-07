# Complete currency checkpoint

`EconomyCurrencyCodec` encodes the shared gold/material world, copper and
silver ledgers, all denomination loans, mint counters and currency events in
one EUC1 payload. `CurrencyCheckpoint` contains `currency`, outer `tick` and
outer input `sequence`. `euc-encode` takes that record, an owned output buffer
and capacity. `euc-decode` returns a detached candidate; `euc-decode-record`
also requires exact enclosing tick/sequence equality.

The caller must supply disjoint output storage, keep the source stable under
single ownership and check the Result before using output. Do not overlap
the buffer with any source record, list, catalog text or material buffer.
After a refusal, reclaim caller scratch without publishing a partial candidate.
Keep an accepted candidate below later request scratch marks.

## EUC1 layout

The version 6 payload is 3414936 bytes. Cells are little-endian 64-bit
integers. The decoder refuses version 5. The decoder also accepts version 4 (3350728 bytes), version 1
(3288256 bytes), version 2 (3294400 bytes) and version 3 (3349696 bytes), which
embed UEC1 version 4, 1, 2 and 3, carry no tail and no event base, and decode
with a zero opening.

| Offset | Content |
|---:|---|
| 0 | Magic `0x31435545` |
| 8 | Version 6 (the embedded UEC1 version) |
| 16 | Total payload length |
| 24 | Embedded UEC1 length, 1432688 |
| 32 | Outer tick |
| 40 | Outer input sequence |
| 48 | FNV32 over the payload, skipping this cell |
| 56 | Reserved zero |
| 64 | Thirteen currency policy/counter cells |
| 168 | Event base (events sealed before this segment) |
| 176 | Segment count |
| 184 | Reserved zero through offset 191 |
| 192 | Complete UEC1 gold/material checkpoint |
| 1432880 | Copper money block, 472128 bytes |
| 1905008 | Silver money block, 472128 bytes |
| 2377136 | 8192 currency events, 120 bytes each |
| 3360176 | Copper and silver openings (906 cells each), three start tables of 13376 bytes, 17 start cells |

Policy cells are event count, copper-per-gold, silver-per-gold, copper owner,
silver owner, copper yield, silver yield, copper ingots, silver ingots,
copper sequence, silver sequence, gold sequence and coordinated purse count.

Each money block contains the existing eight money counters in 64 bytes,
256 purses at 24 bytes each, 128 loans at 56 bytes each and 8192 ledger rows
at 56 bytes each. Event fields follow `EuEvent` in the explicit
`EconomyCurrencyFields` serialization order. No native pointers or credentials
are persisted. Unused slots must match freshly initialized zero records.
The embedded UEC1 has its own checksum and must carry the same outer metadata.

## Reconstruction

`EconomyCurrencyValid.euv-valid` first validates the gold/material state and
the three-denomination census. It then constructs fresh money ledgers and
replays the currency events. Admission, rates, tax, transfers, grants, mint
policy, lending and repayment use the same currency admission functions.
Historical mint issuance reconstructs the coin/material ledger pair from the
recorded metal, actor, configured yield and ingot count. Historical hours
advance the money clocks/defaults without regrowing the final material world.

Every generated event must match its source row exactly, including the rates
and all three ledger sequence links. Reconstructed purse balances and owners,
loan terms/status/payment, complete ledgers, counters, mint policy and all spare
records must match the snapshot. Loan creation reads principal, interest and
due time from that loan's saved record, then checks its resulting history and
final state. A changed balance, detached loan payment, broken event link or
changed current rate refuses even after checksums are recomputed.

Valued-sale event kind 11 reconstructs its actual copper/silver/gold payment
and the zero-coin gold valuation marker (money kind 15). The corresponding
material trade must match payer, payee, hour, marker action and quoted gold
price. The marker is an audit record, not a gold transfer. Existing row sizes
and offsets are unchanged; older readers without these kinds reject such
snapshots. A gold/material UEC1 component alone cannot verify mixed tender;
the encompassing EUC1 validation supplies that check.

Reconstruction establishes internal historical consistency. It does not
reauthenticate old consent or prove historical mines, forge locations and
physical marks. Those facts belong to the trusted encompassing commit and its
world evidence. The material snapshot still requires conservation and enough
consumed ingots for each recorded mint. Reconstruction never modifies that
snapshot's shared actors, inventories or source nodes; its shadow world owns
only new financial state and clock/mint scalars.

Encoding validates the same reconstruction before writing. Decoding validates
the fixed header and checksum before allocation and nested parsing, then
reconstructs all currency history. Both reclaim reconstruction scratch. The
returned object retains no input-buffer pointers.

## Compatibility and integration

EUC1 is distinct from the older UEC1 format. The gold component keeps its old
units and is embedded intact; copper and silver are never relabeled gold or
copper equivalents on load. A UEC1-only checkpoint cannot restore a currency
world, and there is no implicit upgrade of an existing gold economy. New
worlds choose `EconomyMetals.ex-catalog` and `eu-new` explicitly.

This is a native checkpoint codec. [EconomyCurrencyInput.md](EconomyCurrencyInput.md)
defines ordered EUI1 replay and the separate-VM IDE restart witness.
[TreasuryPanel.md](TreasuryPanel.md) defines authenticated policy controls.
Composite shard integration, durable panel binding, shops and physical coin
stacks remain separate work. Never save only the embedded UEC1 as the complete
currency state.

## Cost and proof

Wire copy/hash is linear in the fixed payload. Currency event replay has
bounded purse-admission and loan-default scans; state comparison is linear
in all three fixed tables. Currency census rescans ledger rows per purse and
inventories per item. This adds O(purses * ledger rows + events * (purses +
loans) + item types * actors) to the inherited gold/material validation.
That inherited work also includes catalog closure, identity uniqueness checks,
per-loan history scans, trade-party checks and ledger lookups, and per-item
sales/turnover audits. All retain their existing fixed bounds. Reconstruction
uses one set of fresh financial tables, not a per-event whole-state clone.

Small and maximum-history decode retain the same heap, excluding caller
buffers, below the proof's 5 MiB bound. Per-event and reconstruction allocations do not accumulate.
`proofs/EconomyCurrencyCodecProof.codex` covers all three mined mint sources,
mixed payment/tax, repricing, default and partial repayment, canonical
roundtrip, detached candidates, rehashed corruption, reserved slots and the
maximum 8192-event history. Normal and poisoned output must match the oracle.
