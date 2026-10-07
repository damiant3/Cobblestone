# UOAIX sealed segments (UOAIX-50)

Status: stages 1 and 2 built (rollover, persistence and the disk archive);
stage 3 (a live world on the new format) open. Approved by root 2026-10-06.
Owner: blu.

A disk installed before the archive region (header cell 304 is zero), and every
proof that boots a bare journal, discards a seal's rows and logs `ARCHIVE
ABSENT`; rerunning `install-map-cache.ps1` adds the region.

## Problem

The UOAIX economy keeps five lifetime logs of fixed capacity, and each one
refuses when full:

| log | rows | holder |
|---|---:|---|
| currency events | 8192 | `EuCurrency.events` |
| copper, silver, gold money ledgers | 8192 each | `EmMoney.ledger` |
| material trades | 4096 (`ep-trade-limit`) | `EpWorld.trades` |
| vendor sales | 1024 | `GvWorld.sales` |

`eu-hour` writes one clock row into the currency log and one into each ledger
every economy hour, so before stage 1 an idle world stopped its clock after
about 8192 hours (measured: 8,184 events at economy hour 7,665,
`CompositeVeinTripProof`).

Ruled (root, 2026-10-06): a full log is sealed as a read-only archive segment
with a checksum and carried-forward state, and continues in a fresh segment.
Nothing is overwritten, skipped or compacted.

## The seal

**`eu-seal` rolls the event log, the three ledgers and the trade log over
together, at an economy-hour boundary.** A trade names its gold-ledger action and
a valued-sale event names its trade, so these four move as one. `eu-hour` calls
`eu-seal` first when the event log or any ledger holds more than 7168 rows, or
the trade log more than 3584 (`eu-seal-due`). An operation that finds a log full
inside one hour refuses as before; the next hour seals.

**Vendor sales roll over on their own** (`gv-seal-sales`, called by
`gv-checkout`): when more than 992 rows are used, so a 32-line cart still fits.

**References keep absolute numbers.** Ledger actions (`EmMoney.sequence`), event
sequence numbers (`EuCurrency.base` plus the row) and sale numbers
(`GvWorld.sale-base` plus the row) keep counting across seals. A reference that
points below its log's window points into a sealed segment. It was validated
before the seal, so a validator accepts it on order alone:

- a vendor sale whose `currency-event` is at or below `EuCurrency.base`;
- a town-government event or receipt whose `money-action` is at or below the
  gold opening action (`tgv-payment`, `tgv-collected`).

## The opening

Validators that replay or sum a log start from the state at the last seal.

| holder | carried | cells |
|---|---|---:|
| `EmMoney.opening`, each ledger | purse balances (256); per loan: lent, paid, defaulted (128 x 3); action, hour, tax, issued, defaults, mint purse, mint rate, mint ingots; kind-1 coin received per purse (257) | 906 |
| `EpWorld.opening` | turnover and crown turnover per chain (8 + 8), demand per item (129), the demand day | 146 |
| `EuCurrency.start` | three money tables at the seal (counters, purses, loans; no rows), the 13 policy cells, world hour and the three mint fields, the segment count | 3 tables + 17 |

The received column carries town dues: `o.collected` equals the town purse's
sealed kind-1 receipts plus the dues rows in the window. A kind-1 payment into a
town purse that is not dues would break that equality; none exists today.

`euv-resume` builds the replay shadow from `start` once a world has sealed and
from `eu-new` before; `em-balanced`, `ev-audit`, `ev-loan-histories`,
`ev-turnovers` and `ev-all-sales` read the opening. An unsealed world's opening
is all zero, so its validation is unchanged.

## Formats

| format | version | change |
|---|---:|---|
| UEC1 | 5 | gold opening (7,248 bytes) and trade opening (1,168 bytes) appended: 1,431,656 bytes |
| EUC1 | 5 | event base at 168, segment count at 176, then a tail of the copper and silver openings, the three start tables and the start cells: 3,413,904 bytes |
| GVS1 | 6 | sale base appended: 176,472 bytes |
| UCC1 | 24 | carries EUC1 version 5; version 23 still decodes |

Every earlier version decodes with a zero opening. Retained bytes measured
2026-10-06: EUC1 decode 4,064,631 (+82,368), UEC1 decode 1,653,754 (+8,432),
a currency world 2,407,816 (+73,936).

## The archive (stage 2)

Ruled (root, 2026-10-06): a region of its own on `world.disk`, not the journal,
because every journal switch wipes the other half.

- **Where.** The region sits after the installed map cache. The map-cache header
  names it: start sector at 304, capacity in sectors at 312 (installer-owned,
  inside the header FNV). Zero means a disk installed before the region.
- **Size.** 256 MiB (524,288 sectors), about 105 segments at 2.4 MB, about 7 real
  years of an idle world at the default clock.
- **Its own header.** The region's first sector is ARC1: magic, version, segment
  count, next free sector, FNV32 of the last segment, header FNV32. The server
  owns it and rewrites it after each segment's payload is on disk.
- **A segment.** SEG1: segment number, the bases, the opening and start state
  that began the segment, every sealed row, and the previous segment's FNV32,
  so a segment verifies on its own. The seal captures the rows into a buffer
  allocated at the first seal; the next commit writes the segment, then the
  journal record.
- **Full.** A seal that would not fit refuses: the hour refuses, the world logs
  `ARCHIVE FULL` at every refused hour, and the economy clock stops. It never
  wraps.
- **Absent** (an old disk, or a proof's bare journal). The world loads; each seal
  logs `ARCHIVE ABSENT: segment N rows discarded` and proceeds.
- **Carried.** `install-map-cache.ps1` reads the region from the old header,
  preallocates the new one after the new cache and copies it across before it
  commits the header. `compact-world.ps1` copies the whole file and its guest
  writes only the journal. An arm runs a sealed world through compact, then
  install, then boots and reads every segment back.

Bytes per segment at the seal thresholds: events 7,168 x 120 = 860,160, ledgers
3 x 7,168 x 56 = 1,204,224, trades up to 3,584 x 72 = 258,048, opening 63,176:
**about 2.4 MB**, with sales archived separately (at most 1,024 x 96 = 98,304
per rollover).

At the default 7200 s game day (288 economy hours per real day) a world with
no trading seals every 7168 / 288 = 24.9 real days; the gold ledger, which also
takes tax, valuation and default rows, seals first in a trading world. A live
shard's rate is measured in stage 2 and recorded here. A 64 MiB UOS1 store holds
about 20 segments beside its snapshots, so archive export is open work before a
world runs about 16 real months.

## Stages

1. Built: `eu-seal`, `gv-seal-sales`, the openings, the validators and the
   formats above. Arms: `proofs/EconomySealProof` (loans across a seal, a
   default after it, payments and tax after it, a restart across it, tamper
   refusals, and two seals driven by economy hours alone),
   `GovernmentCurrencyProof` (dues before and after a seal) and
   `GameVendorCodecProof` (a quote before a seal and a sale rollover is bought
   after them, and the world roundtrips).
2. Built: `EconomyArchive` (`ea-probe`, `ea-flush`, called from `cs-commit`),
   the installer's region, and `proofs/test-archive-carry.ps1 -ClientRoot <client>`:
   install, four `CompositeVeinTripProof` boots (the fourth seals and archives
   segment 1, 2,105,960 bytes: 7,224 events and 6,867 gold rows), the
   `EconomyArchiveRead` walk, `compact-world.ps1`, `install-map-cache.ps1`, and
   the same walk again, byte for byte. `EconomySealProof` grades the capture
   and the refusal of a seal that would not fit. Open: a live shard's seal rate,
   read from the `ECONOMY ROWS` line each commit logs (economy hour; event,
   copper, silver, gold and trade rows).
3. A fresh live world on the new format (UCC1 24 and a reinstalled cache).
