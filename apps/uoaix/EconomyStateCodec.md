# Economy checkpoint codec

`EconomyStateCodec` serializes the complete aggregate economy state: the
catalog, purses, loans, coin ledger, actors and inventory metadata, workplaces,
resource/plant nodes, purchase history, census/demand counters and mint policy.
`EconomyCheckpoint` contains an `EpWorld`, an outer tick and an input sequence.
The outer sequence is not the money ledger's action counter. All game-hour
rates and deadlines remain in game time; a real-time day-length setting does
not rewrite these values.

`es-encode checkpoint buffer capacity` returns the fixed encoded length or
an error. `es-decode buffer actualLength` returns a detached checkpoint.
Use `es-decode-record buffer actualLength outerTick outerSequence` when the
payload comes from WorldDisk; both metadata values must agree. The version 4
payload is 1423240 bytes and fits the existing store's payload budget. The
decoder also accepts version 1 (1360768 bytes, 64 stations, 256 nodes),
version 2 (1366912 bytes, 256 stations, 256 nodes) and version 3 (1422208
bytes, no found counters): each is a prefix of the next, and re-encoding
writes version 4.

The caller supplies owned, disjoint output storage and stable source state.
Encoding explicitly rejects overlap with inventory/counter buffers. Native
record/list/text allocations must also be disjoint; the caller owns that
requirement. Decoding copies all fields and CCE text into new allocations.
After successful decoding, the input buffer can be reused. Retain the returned
catalog, economy and money state together below later request scratch marks.
Failure can allocate a partial candidate: report the error and reclaim the
caller's scratch without publishing or continuing that candidate.

## UEC1 layout

All integer cells are little-endian. The fixed body writes scalar fields and
buffer contents, never native addresses. The catalog is embedded, so recovery
does not silently substitute different recipe or regrowth settings from a
later build. Names use length-prefixed, zero-padded CCE text.

| Offset | Content |
|---:|---|
| 0 | Magic `0x31434555` |
| 8 | Version 4 (1, 2 or 3 for the shorter payloads) |
| 16 | Total bytes |
| 24 | Catalog bytes, 25120 |
| 32 | Outer tick |
| 40 | Outer input sequence |
| 48 | FNV32, skipping this cell |
| 56 | Reserved zero |
| 64 | Item/resource/recipe counts and a reserved zero cell; 128 item slots, 64 resource slots, 128 recipe slots |
| 25184 | Eight money scalars |
| 25248 | Eight production scalars |
| 25312 | 256 purse records, 24 bytes each |
| 31456 | 128 loan records, 56 bytes each |
| 38624 | 8192 coin/audit entries, 56 bytes each |
| 497376 | 128 actor blocks, 4248 bytes each |
| 1041120 | Stations 1-64, 32 bytes each |
| 1043168 | Resource/plot nodes 1-256, 72 bytes each |
| 1061600 | 4096 purchase records, 72 bytes each |
| 1356512 | Gathered, crafted, consumed and recent-sales arrays (129 cells each), then turnover and crown-turnover arrays (8 cells each) |
| 1360768 | Versions 2 and 3: stations 65-256, 32 bytes each |
| 1366912 | Versions 3 and 4: resource/plot nodes 257-1024, 72 bytes each |
| 1422208 | Version 4 only: found array (129 cells) |

An item slot has chain, text length and 64 CCE bytes (80 bytes total).
Resource and recipe slots carry their seven and eleven scalar fields.
An actor block has purse, place, hunger and attempts, then four 129-cell
inventory buffers (quantity, quality, cost, charges) and eleven skill cells.
Scalar field order is explicit in `EconomyStateFields.codex`. Unused catalog
slots, records and inventory indices must be zero; active text padding must
also be zero. FNV32 detects corruption, not authorization or forgery.

## Validation

`EconomyStateValid.ev-valid` checks catalog closure, dimensions and scalar
bounds before following references. It checks unique purse ownership labels
and actor/purse bindings, loan terms/status, inventory metadata bounds,
workplace ownership, plant/resource relationships, deadlines and spare slots.

The money audit reconstructs balances from zero in ledger order, rejects
overdraft and over-capacity credits, and checks contiguous action IDs and
monotonic game-hour records. Payment/tax rows, mint issuance/material rows and
clock/default rows must have their required action relationships. Mint
issuance must match the logged policy rate and ingot count. Reconstructed
policy, issuance, ingots and defaults must match their stored counters.
Loan principal and repayments are checked against their linked ledger rows;
default references are unique per loan, debt must still be outstanding at the
default's position in history, and final status must agree.

Purchase rows must reference matching payment actions and existing actor
purses. The validator reconstructs turnover, crown purchases and demand decay
from purchase history. It then checks both purse totals and the material
census. Validation establishes bounded internal consistency, not authenticated
history or each object's physical provenance. An authorized snapshot writer
and the later world-serial transaction adapter remain separate requirements.

## Cost and proof

Encoding/decoding copies and hashes a fixed payload. Structural checks scan
the fixed record capacities. The history checks cost
O(loans * ledger + items * purchases + purchases * (actors + log ledger)),
plus catalog closure, the material census, and bounded quadratic purse/actor
uniqueness checks. History recursion uses bounded
loops/accumulators; no per-entry snapshot or growing audit collection is made.
Validation's temporary balance array and audit record are reclaimed before
returning. The raw-field helpers require validated owned storage; callers use
the top-level codec rather than treating those helpers as admission APIs.

Under seed `4228CD5103DC4523` on 2026-10-05, decoding the populated 30-day
fixture retained 1643258 bytes. The maximum-dimension catalog fixture retained
1723671 bytes; all money/production record capacities are preallocated by the
decoder in both cases. Caller-owned input/output buffers are excluded. The
proof refuses retained decoding above 3 MiB.

`proofs/EconomyStateProof.codex` grades the 30-day state plus taxed loans,
partial repayment, default and changed mint policy. It checks canonical
byte equality after decode/re-encode, detached ownership, input-buffer reuse,
identical continuation, outer metadata, overlap refusal and maximum catalog
dimensions. Version 1 and version 2 payloads must decode and re-encode to the
version 3 bytes; a version 3 header over either shorter length must refuse. Rehashed corruptions target purses, loans, mint linkage, trades,
stock, unused actor slots, text dimensions and reserved data. A fully repaid
loan followed by a forged default must refuse after rehashing. Compile normally
and poisoned with an explicit depot kernel and compare the complete output
with `proofs/EconomyStateProof.expected`.

UEC1 is a native economy codec. [EconomyInput.md](EconomyInput.md) defines
ordered UEI1 inputs, explicit EconomyStore recovery and the separate-VM IDE
restart proof. `TownStore` reads TSC1/TSI1; `GameCheckpoint` owns UGC1. Neither
accepts UEC1 implicitly. Complete shard-envelope composition and serial-preserving
world binding remain integration work. The deterministic fixture's `EcClock` jobs are
test-driver configuration and are not part of this production-state format.
Database migration remains governed by [Database.md](Database.md).
