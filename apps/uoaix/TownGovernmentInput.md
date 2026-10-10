# Ordered civic replay and restart

`TownGovernmentInput` applies TGI1 inputs to `GovernmentCheckpoint`.
`TownGovernmentBundle` keeps the civic state, its mutable economy/currency and
keeper queue together in TGB1. `TownGovernmentStore` restores that bundle and
its following civic inputs through the existing opaque `WorldDisk` backend.

These are trusted commit records, not player or model commands. The producer
must complete the government's current-mayor/royal checks and all required
world, physical, land, account and counterparty admission. Boolean cells carry
that prior decision; decoding one does not grant authority. No account action,
raw coin issue, stock grant or identity creation is exposed by this table.

## TGI1

Frames are 1088 bytes. Header cells are magic `0x31494754`, version 1, frame
length, operation, tick, sequence, FNV32 and recorded integer outcome. They
occupy offsets 0 through 56 in that order. FNV skips its own cell at 48.
Sixteen integer arguments start at 64; unused arguments are zero.

Five padded, length-prefixed CCE text slots follow: account at 192 (64 cells),
happened at 264 (256), location at 528 (64), wall time at 600 (64), and evidence
at 672 (400). Offset 1080 is reserved zero. Only escalation uses text; all
other operations require empty text. Decoder bounds and padding checks precede
text allocation. Permission/presence/mark flags must be exactly zero or one.

| Op | Operation | Arguments |
|---:|---|---|
| 1 | Register town | verified royal, town, mayor NPC, town purse |
| 2 | Appoint mayor | verified royal, town, mayor NPC |
| 3 | Set dues | mayor, town, gold amount |
| 4 | Collect dues | mayor, town, payer purse, verified liability |
| 5 | Hire guard | mayor, town, NPC, purse, gold wage |
| 6 | Retire guard | mayor, guard ID |
| 7 | Request funding | mayor, town, grant/loan kind, needed gold balance |
| 8 | Settle arrears | mayor, guard ID, gold amount |
| 9 | Royal town grant | verified royal, town, gold amount |
| 10 | Royal town loan | verified royal, town, gold amount, interest, due hour |
| 11 | Close completed payroll day | none |
| 12 | Admit managed site | mayor, town, station, verified consent |
| 13 | Revoke site | mayor, town, station |
| 14 | Begin raid response | verified world event, town, event ID |
| 15 | Stand down | verified world event, town, event ID |
| 16 | End raid with damage | verified world event, town, event ID, station, boards, nails, work hours, gold fee |
| 17 | Assign repair | mayor, order, economy actor |
| 18 | Deliver repair materials | actor, order, boards, nails |
| 19 | Work on repair | actor, order |
| 20 | Pay completed repair | mayor, order |
| 21 | Request essential workplace | mayor, town, station kind |
| 22 | Staff workplace | mayor, order, actor, station |
| 23 | Post bounty | mayor, town, home, threat epoch, gold reward, verified eligibility |
| 24 | Claim bounty | verified clearing, bounty, home, threat, event, winner purse |
| 25 | Pay bounty | mayor, bounty |
| 26 | File dispute | verified context, mayor, town, kind, claimant kind/id, respondent kind/id, reference, metal, source event |
| 27 | Close paid debt case | mayor, case |
| 28 | Close item case | mayor, case, verified restitution, present, marked, serial, true owner kind/id, mark kind/id, revision |
| 29 | Close land case | mayor, case, verified agreement, land event |
| 30 | Escalate case | mayor, case, verified report facts; five text fields |
| 31 | Advance economic game hour | none |

The economic-hour operation dispatches `eu-hour` for currency and `eh-step`
for legacy gold. It does not advance the borrowed townsfolk population or
alter registered titles. The encompassing clock/input owner must compose their
separate changes where required. Unsupported mixed-input families refuse;
they are not silently skipped.

## Private candidates

`tgi-apply` requires exactly the next sequence and a nondecreasing tick. It
executes once and compares the result with the recorded outcome. Refusals and
skill failures may be recorded. An outcome mismatch can already have changed
the candidate, including money or queue state: discard it without publishing.

`tgi-replay` clones all mutable state reachable by these operations once per
batch: the economy/currency, keeper queue and civic records. It accepts at most
512 complete frames and validates the final state. Every frame's arguments,
party/evidence objects and report text are reclaimed after application; no
input-buffer pointer enters retained state. The source remains unchanged on
success or failure. Failed allocations are reclaimed by the caller after
consuming the diagnostic.

People, mind and registry are borrowed read-only anchors during this replay.
Hold them stable and below recovery scratch. They must come from the same
encompassing checkpoint. Recovered admin runtime callbacks are reset by UAC1;
rebind callbacks and all users of the recovered economy/queue before publishing
the entire returned object graph. Publishing only the government while keeping
old money or queue references would be incorrect. This is a recovery/batch API,
not a per-action whole-world clone or a live transaction undo mechanism.

## TGB1 bundle and disk recovery

The bundle contains all state these inputs mutate. Header cells at 0 through
56 are magic `0x31424754`, version 1, total size, economy payload length, tick,
sequence, FNV32 and reserved zero. TGC1 starts at 64, UAC1 at 198304, and the
UEC1/EUC1 economy at 268400. Every embedded checkpoint must match outer metadata.
The currency bundle is 3556656 bytes, within WorldDisk's 4194304-byte record
limit. Legacy gold uses the smaller UEC1 payload; its units are unchanged.

`GovernmentAnchors` supplies canonical people, mind, registry and the trusted
royal economy actor. The bundle recovers its own money, inventory, stations,
queue and civic records. It does not reconstruct the supplied anchors or
authenticate the actor from the saved ID. TGC1 requires the supplied royal
actor to match its saved binding. All output buffers must be disjoint from
the source graph, and the source must remain stable during encoding.

`government-store-restore` reads the latest TGB1 and up to 512 following TGI1
records. It checks record kinds/extents, exact metadata and outcomes, final
civic validation and equality with the committed head. Blank/faulted stores,
marked torn tails, unknown formats and invalid suffixes refuse. There is no
empty-world or older-checkpoint fallback. Ambiguous append/readback failure
requires stopping admission and recovering, not blind retry or acknowledgement.

This helper does not authorize a second live civic journal. The shard owner
must compose item/world, account, title, population, consent, model and admin
effects in its one durable game-action transaction. A component-only bundle
does not make a mixed action durable.

## Evidence and cost

The 47-input fixture registers/funds a town, hires and pays a guard, collects
dues, records raid damage and repair/essential-workplace orders, pays a bounty,
files debt/land/player cases and queues evidence exactly once. The in-memory
proof compares complete civic, economy and queue encodings with direct execution.
A late outcome mismatch after payments and escalation must leave every source
component byte-identical. Rehashed malformed input and bounded-batch controls
also run normally and poisoned.

Under seed `4228CD5103DC4523` on 2026-10-04, both 2- and 512-input replay retained
8262015 bytes, below 12 MiB. Bundle decode retained 4705023 bytes beyond anchors,
below 6 MiB. These are retained allocations, not peak scratch measurements.
Replay makes one complete clone, then operation-specific work plus final
validation. It does not retain a fresh clone per input. Bundle copy/hash is
linear in bytes and inherits the component validators' documented costs.

```powershell
pwsh -File apps/uoaix/proofs/test-government-restart.ps1 -Kernel seed/Codex.cdx -OutDir build-output/uoaix/government-restart-proof
```

The host creates a new 8 MiB synthetic disk and runs separate native IDE writer
and reader guests. The writer commits an initial bundle, 47 inputs and a bundle
after input 7. The reader restores that checkpoint plus its 40-input suffix,
including payments, obligations and report body, without changing the disk hash.
Both guests must exit normally and match exact oracles. The receipt retains
kernel/artifact/disk hashes, PIDs and RAM admission measurements; the harness
stops its own guests on failure.

This fixture recreates identical, unchanged people/mind/registry anchors in
the reader. It does not prove their persistence here, physical power loss,
interrupted writes, a killed writer, live acknowledgement or a complete shard
restart. Those anchors and mixed input families belong to their owning codecs
and the encompassing persistence path. Runtime callbacks/credentials require
fresh binding after recovery.
