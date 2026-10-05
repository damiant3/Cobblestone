# Vendor metadata checkpoint

`GameVendorCodec` supplies the composite owner's vendor payload:

```text
gvsc-bytes : Integer
gvsc-encode : GvWorld, buffer, capacity -> Result Integer Text
gvsc-decode : buffer, size, GameShard, EuCurrency -> Result GvWorld Text
```

GVS1 occupies 119240 bytes. It binds the supplied game world and currency, which must be separately
decoded components of the same encompassing checkpoint. EUC1 owns all currency
and material economy state. GVS1 owns copies of bindings, lots, sale history
and price buffers; it retains no input-buffer pointers. Allocate it below the
owner's request heap mark. Failed decode allocations are request scratch.
The supplied world must have a ready maintained index. Read-only raw codec
views with index zero are refused; restore/reindex the world before GVS1.

Validation checks current serials, actor/purse bindings, lot quantities and
aggregate inventory coverage, server price bounds, and each sale's currency
event and material-trade links. Historical source/delivery serials may no
longer exist. These checks establish consistency, not the original caller's
authority or historical custody; the trusted outer action log owns those facts.

A registered backpack may have moved into a corpse. A live lot may be in a
bank, corpse or on the ground. Its actor names the material ledger accounting
for that stock; it does not grant custody or permission to sell. These states
remain saveable. Deleting, consuming, splitting, merging or transferring a lot
between actors requires matching metadata and material-account updates in the
same transaction. Missing serials and quantity mismatches refuse. Vendor
stock, extra and buys containers must still belong to their vendor.

`GameVendor` requires player sale items inside the registered backpack at both
quote and checkout, and that backpack must belong to the living player. A bank
deposit after quoting refuses. Decode clears quotes, connection, side, count
and deadline; a pre-restart cart cannot be reused.

## Format

All cells are little-endian 64-bit integers. The 80-byte header contains magic
`0x31535647`, version 3, length, vendor count, player count, lot count, checksum
at offset 48, sale count, vendor action sequence and reserved zero. FNV32 covers
the payload except that checksum cell. Fixed tables follow in this order:

- 16 vendors: serial, actor, stock/extra/buys serials, then 129 price cells.
- 5 players: mobile serial, actor and backpack serial.
- Lots 1-64: seven `GvLot` fields in `GameVendorState` order.
- 1024 sales: twelve `GvSale` fields in `GameVendorState` order.
- 128 controlled-NPC owners: serial and actor (versions 2 and 3).
- Lots 65-128 (`gv-lot-limit`), version 3 only.

Version 3 is 124872 bytes. The decoder also accepts version 2 (121288 bytes,
64 lots) and version 1 (no owner table); each is a prefix of the next.

Unused wire rows must be zero. Transient quotes are omitted. The vendor action
sequence counts accepted carts; it is not the store's global input sequence
or game clock. The outer checkpoint/journal must validate those separately.

## Commit and cost

Encode GVS1 with the same private world/currency candidate as the outer commit.
Publish replies only after that encompassing commit succeeds. This codec alone
does not write disk, roll back other components or establish crash recovery.

Decode allocates the fixed `gv-new` tables once; validation scratch is reclaimed.
It does not clone the world or economy. Lot coverage is O(lots squared), child
queries O(lots) through maintained counts, sale linkage O(sales * log trades),
and ownership walks stop at 65 steps. Currency
reconstruction retains its existing bounded costs. Excess counts refuse.
`proofs/GameVendorCodecProof.codex` checks a handler purchase, bank custody,
quote reset, corpse/ground persistence and recomputed-checksum corruptions.
Reek approved the codec and custody review. Its exact oracle and the existing vendor replay pass on seed
`4228CD5103DC4523`. Composite disk recovery remains the encompassing owner's
grade; this landing establishes the metadata codec and sale-custody boundary.
