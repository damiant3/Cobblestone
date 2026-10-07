# Vendor metadata checkpoint

`GameVendorCodec` supplies the composite owner's vendor payload:

```text
gvsc-bytes : Integer
gvsc-encode : GvWorld, buffer, capacity -> Result Integer Text
gvsc-decode : buffer, size, GameShard, EuCurrency -> Result GvWorld Text
```

GVS1 occupies `gvsc-bytes`. It binds the supplied game world and currency, which must be separately
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

A registered backpack stays on its dead owner. A live lot may be in a
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
`0x31535647`, version 6, length, vendor count, player count, lot count, checksum
at offset 48, sale count, vendor action sequence and controlled-NPC owner
count. FNV32 covers the payload except that checksum cell. Each version appends
after the last, so every earlier image keeps its offsets:

- 16 vendors: serial, actor, stock/extra/buys serials, then 129 price cells.
- Players 1-5: mobile serial, actor and backpack serial.
- Lots 1-64: seven `GvLot` fields in `GameVendorState` order.
- 1024 sales: twelve `GvSale` fields in `GameVendorState` order.
- 128 controlled-NPC owners: serial and actor (version 2).
- Lots 65-128 (version 3).
- Players 6 to `gv-player-limit` (version 4).
- Lots 129 to `gv-lot-limit` (version 5).
- The sale base, sales sealed before this segment (version 6).

The decoder accepts versions 1 to 6 at their exact lengths
(`GameVendorCodec.codex`); encoding writes version 6.

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
Composite disk recovery is the encompassing owner's grade.
