# Ordered currency inputs and restart

`EconomyCurrencyInput` records admitted material and three-denomination money
operations in EUI1. It applies them to `CurrencyCheckpoint`, which carries the
complete `EuCurrency` plus outer tick and input sequence. The currency's own
financial event count is separate: a refused action or a material operation
can advance input sequence without appending a currency event.

These are trusted server commit records, not player commands. Admission must
authenticate the caller, current royal/payer/borrower authority, intended
world target and any required consent before recording an input. Permission
flags carry that completed decision for deterministic replay. Decoding a flag
does not confer authority. No raw issuance, inventory grant or skill-setting
operation is present.

## EUI1

Each frame is 128 bytes: magic `0x31495545` at 0, version 1 at 8, length at 16,
operation at 24, tick at 32, sequence at 40, FNV32 at 48, recorded integer
outcome at 56, then eight little-endian argument cells. FNV32 skips its own
cell. Spare arguments must be zero. Permission/watering Booleans must be
exactly 0 or 1, and operation, sequence and tick must satisfy their bounds.
EUI1 is distinct from the older gold-only UEI1 magic and operation table.

| Op | Operation | Arguments in order |
|---:|---|---|
| 1 | Open aligned purses | kind, legal owner |
| 2 | Admit material actor | purse, place |
| 3 | Admit workplace | actor, kind, place |
| 4 | Admit harvest source | resource, place |
| 5 | Admit plantable ground | actor, place, ground |
| 6 | Change actor place | actor, place |
| 7 | Royal exchange policy | verified Lord British, actor, copper/gold, silver/gold |
| 8 | Royal tax policy | verified Lord British, actor, basis points |
| 9 | Royal mint policy | verified Lord British, actor, metal, yield |
| 10 | Strike coin | verified mint, actor, station, metal, ingots |
| 11 | Pay mixed tender | verified payer, payer, payee, copper, silver, gold |
| 12 | Royal grant | verified Lord British, actor, recipient, copper, silver, gold |
| 13 | Lend one denomination | verified lender, lender, borrower, metal, principal, interest, due hour |
| 14 | Repay one denomination | verified borrower, borrower, metal, loan ID, amount |
| 15 | Harvest | actor, node |
| 16 | Craft ordinary recipe | actor, recipe, station |
| 17 | Eat held food | actor, item |
| 18 | Plant | actor, ground node, resource |
| 19 | Care for plant | actor, node, watered |
| 20 | Record verified crop loss | node |
| 21 | Advance one game hour | none |

The owning modules supply operation-specific admission. Mint recipes remain
unavailable through ordinary crafting. Metal codes are copper 1, silver 2,
gold 3. Loans keep their metal with the ID. Mixed-price shop purchases and
physical item/trade admission remain outside this table.

## Candidate ownership

`eui-apply` requires exactly the next sequence and a nondecreasing tick. It
executes once and compares the result with the recorded outcome. Refusals and
skill failures can be recorded outcomes. An outcome mismatch may already have
mutated its private candidate: discard it, never publish or retry it as live
state. Input shape/ordering errors refuse before execution.

`eui-replay` accepts at most 4096 complete frames, clones the complete source
checkpoint once and applies the batch to that private candidate. Success also
requires complete currency/material validation. The source stays unchanged on
success and failure. Per-frame records and argument lists are scratch, reclaimed
after application; no argument pointer enters retained currency/world state.
On error, consume the diagnostic and reclaim the caller's outer scratch mark.
The successful candidate retains its encoding buffer as well as decoded state.

## CurrencyStore

`currency-store-restore` restores the latest EUC1 checkpoint and up to 4096
following EUI1 inputs from the existing `WorldDisk` backend. It checks each
record's kind, extent and outer metadata, exact input ordering/outcome, complete
final state and equality with the committed store head. Blank/faulted stores,
a backend-marked torn tail, unknown formats or any bad suffix refuse; there
is no fallback to an empty world or to an older checkpoint that silently skips
committed actions. A marked tail requires explicit owner investigation before
currency admission can resume.

Use a dedicated bounded disk region. This helper does not make a second live
economy journal safe: mixed item, trade, account, title, admin and currency
changes still require the one encompassing shard commit and recovery format.
An append/readback failure can leave commit status ambiguous; stop admission
and recover instead of acknowledging or blindly retrying. The standalone
writer is a synthetic persistence witness, not the live transaction owner.

## Proof and cost

`EconomyCurrencyInputProof` builds its 149-entry fixture through public input
operations, with zero starting coins, no stock/skill grants and novice mining.
Gathered materials make three picks and a hammer; skill failures and tool wear
remain recorded. Each metal is mined, smelted and minted. The fixture then
grants, lends, pays mixed tender/tax, changes exchange policy, advances 24 game
hours and partially repays defaulted gold debt. The checkpoint boundary is
input 114, after all three mints; the suffix has 35 inputs.

The proof checks complete equality with direct execution, source isolation,
late sequence failure, outcome mismatch, rehashed shape/Boolean corruption,
old-format refusal and a 4096-hour batch. Under seed `4228CD5103DC4523` on
2026-10-04, both 2- and 4096-input replay retained 7191663 bytes, below 8 MiB.
Replay does one checkpoint clone, then operation-specific work plus final
validation; retained allocation does not grow per input. It is not the live
per-action path. The checkpoint validator's full cost is in
[EconomyCurrencyCodec.md](EconomyCurrencyCodec.md).

```powershell
pwsh -File apps/uoaix/proofs/test-currency-restart.ps1 -Kernel seed/Codex.cdx -OutDir build-output/uoaix/currency-restart-proof
```

The harness requires a new output directory and creates an 8 MiB synthetic
disk, sufficient for two complete checkpoints plus the input log. Separate
native IDE writer and reader VMs must exit normally and match exact oracles.
The reader recovers the checkpoint plus suffix, all denomination balances,
rates, defaults, partial repayment and mined-ingot provenance. It also refuses
cached head disagreement and a marked torn-tail state. Its disk hash
must equal the writer's. The receipt records guest PIDs, free RAM, kernel and
artifact hashes, disk hashes and results. Owned guests are stopped on failure.

This establishes normal writer-exit/reboot recovery on the native IDE backend.
It does not establish interrupted-sector writes, physical power loss, a killed
writer, mixed live action acknowledgement, authenticated panel dispatch or
Vultr acceptance. Run the in-memory proof normally and poisoned against its
complete oracle; those builds do not turn the disk witness into a poison run.
