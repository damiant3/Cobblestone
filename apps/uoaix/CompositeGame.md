# Composite client acceptance server

`CompositeGameServer.codex` composes mixed vendors, decoration, bank,
skills/items, combat, monsters, death/ankh and client-view reconciliation
through GameNet's authenticated hook. It listens on guest2593.
Root owns the real client and launches the image on host2593 in turn. This
entry does not open an admin port.

## World disks are disposable (Damian, 2026-10-05)

"we are in primary development, there are no old worlds except as convienient
to us. start us fresh everytime if it helps"

A format change does not need a migration, an upgrade path or a refusal of old
disks. Prefer a fresh world disk whenever a migration would cost work.

## Resting state and next grade

Implementation landed at MAIN35648 from DEV35643. Fester wrapped on root's
order; context telemetry was unavailable in the Codex harness. The lane
workplan is empty and no owned VM/listener remains. Shelves35378 and35450
hold only deferred prototypes/catalog entries and cold-load diagnostics;
their current-head gates are NOT RUN. Inspect individual shelf paths before
resuming them, never broadly overwrite this landed composite. There is no
remaining unshelve step for the current server.

The current candidate is
`D:/Projects/Cobblestone-fester/build-output/uoaix/landing-final/server.cdx`,
SHA256 `6F0627368CD33F261A28BE245E531912608FA178E1089BDC7C0334A4D7FBA751`.
Use shipped `tools/codex-vm.exe`, SHA256
`CF9841F8B2BC635D33EC6A1953F318CB2539253308C6DBC7CA6D572F881EC5FE`.
Kernel: `4228CD5103DC4523`. The four-boot1GiB proof is green in
`build-output/uoaix/landing-final/result.json`. Stock count/parent/gump
coordinates and restart repair passed in `build-output/uoaix/stairs-stock`;
the installed dock stair control/fix is in `build-output/uoaix/stairs`.
The current-head installed-stair pass is in
`build-output/uoaix/landing-final/stairs/result.json`.
Full battery and poison were NOT RUN under the UOAIX efficiency ruling.
This candidate still needs root's real-client grade: dock stairs, visible
testing bag/bank contents, death/ankh, and persisted characters/items/positions.

For an existing installed world, reuse the same disk and run:

```powershell
pwsh apps/uoaix/start-composite-game.ps1 -Artifact D:/Projects/Cobblestone-fester/build-output/uoaix/landing-final/server.cdx -StateFile <local-world.disk> -Testing
```

For a fresh installation, run `import-decoration.ps1 -ClientRoot <client>
-ReferenceRoot D:/Projects/uo-reference -OutFile <local-britannia.dwd>`, then
`install-map-cache.ps1 -ClientRoot <client> -WorldDisk <local-world.disk>
-DecorationFile <local-britannia.dwd>`, then the launcher. Never commit or
publish the client, decoded cache, DWD or world disk. Do not replace DWD on
an existing world until WDS1 identity/door-bit migration is implemented;
the newer35626 door dataset is not installed in the current grade disk.

Resume by reading root's inbox and grading this frozen artifact. Transport
and GCV35629, decoration35613, the35574 own-death/corpse fix, gump-position
repair and Source-X stair admission are included. The earlier live trace
fix reduced the same40-move workload from1402ms maximum reply latency to79ms;
this does not substitute for the new client grade. Magery35510 stays shelved,
and second-build shops/harvest/crafting/TownLive/speech/civic/world-spawn
adapters are not composed here. Multi-session integration follows single-player
acceptance. No seed was changed for this landing, and no guest is left running.
Reek's framing35638 is unverified and must not be integrated as part of this
grade. Magery's future kit binding needs its trusted `mgs-book`/`mgs-rune`
registration; the current physical spellbook/rune does not claim spell effects.

Run the separate [map installation](MapCache.md) step first, then launch with
`start-composite-game.ps1 -Artifact <server.cdx> -StateFile <composite-world.disk>`.
The launcher reads no client directory and starts no MUL bridge. The server
pages the entire installed map from the local world disk. Use the same file
on each restart; an existing UCC1 world is preserved by cache installation.

`-Testing` supplies a local launch record and logs `MODE composite TESTING
local-only` at boot; `-Dev` logs `MODE composite DEV`. With neither, the
guest logs `MODE composite PUBLIC` and the launcher first prompts for account
British's new password (GameServer.md, "Accounts").
The mode is transient and defaults off on every boot; network packets and
saved world state cannot enable it. Public or hosted launches use `-Hosted`,
which refuses `-Testing`. Never supply the testing record to a hosted image.
The launcher verifies the guest's mode log before reporting ready.
`-Vm <path>` selects a diagnostic VM explicitly; `run.json` records its hash.
The default remains the workspace's `tools/codex-vm.exe`.
Testing entry adds one tool bag with trade tools, a spellbook, recall rune,
blank scrolls and100 of each classic reagent. The bank receives1000 of each
material stack and10000 physical gold coins. These are explicit testing
items, not production lots or vendor-purse credit. Magery and production
adapters remain separate integration work. A durable grant serial for each
character slot prevents restocking after use, deletion, death or restart;
the record is per slot, not per account (UOAIX-10).
Creation and its grant marker commit together before entry replies.

`CompositeGameRules` combines framing declarations once and refuses conflicting
definitions. Vendor use precedes general item use. Combat status selector4
and skills selector5 share opcode34. A successful entry attaches one backpack
and binds that same serial to the vendor account. The demonstration economy
and terms are those in `GameVendor.md`; the first character is the demo
business actor. The composite spawns no sparring fixtures; every boot deletes
The demonstration provisioner is protected from combat. Vendor lot stacks
move whole through the item handler; vendor checkout owns splitting them.
General item splitting cannot silently invalidate the vendor lot ledger.
Three monsters use the shared combat state and one combined pulse, with home
1440,1720 and radius6, outside the new-player entry area. Startup relocates
surviving monsters from the old1426,1700 home onto distinct walkable tiles
and commits the migration before listening. Their serials and health survive.
The shrine
restores life without reclaiming corpse loot. Resurrection creates/rebinds an
empty backpack while preserving bank ownership. Bank permissions and client
visibility are transient. SeasonBC stays disabled pending the exact-client grade.

The shared owner persists game/world state, currency and lane metadata in one
WorldDisk log. The snapshot is a composite of bounded codecs. CPD1 suffix rows
carry ordered qword offsets and exact before/after values in that canonical
snapshot. Recovery checks record order, before images and the final composite
before creating the live owner. Quotes, drag/target cursors and combat
connection/target state reset on restart. Monster movement/replacement delays
are stored as remaining time and rebased on the new boot clock.

A fresh world disk builds the Britain economy (`bb-economy`, `bb-populate`)
and val's three TownLive residents (`tl-populate`) over a fixed 96x160 Britain
`WalkMap` filled from the cache at boot (about 22 MB); `cgs-load` supplies
that map. A world created before this keeps the demo economy and boots with
`TOWNSFOLK off`; a Britain world logs `TOWNSFOLK on`. The residents' map has
Britain worlds route `bb-handler` (shopkeeper name labels, then the shared
vendor 3B/9F flow) after the townsfolk 09 handler; `BbTown` is rebuilt by
`bb-bind` at load. The residents' map has
its own WdState (`cg-town-doors`, about 22 MB more) sharing the player
state's door bits; a door toggled on either map rebuilds the other map's two
cells, and a door a resident opens arms the 20 s autoclose.

UCC1 version6 appends a townsfolk section: one length qword (0 or
`tlc-bytes`), the town clock in game seconds, then TLC1, decoded against the
Britain map; a townsfolk world loaded without that map refuses. Version5
(no clock qword) decodes with clock 0. UCC1 version4 combines UGC1/TSC1, full EUC1, GVS1, UCB1, GSI1, monsterUMC1,
shrineUSR1, doorWDS1, a48-byte CGT1 testing-grant section and a magery
section: one length qword (0 or `576 + 304 * capacity`) then MGM1. Decode
holds the MGM1 bytes and every commit before magery attaches writes them back
unchanged; `cgs-start` builds Magery, then `cc-magic-restore` reads them with
the boot PIT clock, and a refusal stops the server. UCC1 version9 carries
EUC1 version 3 (256 stations, 1024 nodes); version8 carries EUC1 version 2 and versions
1-7 EUC1 version 1; header cell
64 records which, and every later section offset follows that length. At
16384 objects the version9 snapshot is 13441360 bytes with UGC1 version 2 (`cc-size`, 2026-10-05). The immutable DWD1 dataset is install data, not part
of every journal record. A world written before UGC1 version 2 (accounts) is
refused at decode; start a fresh world disk. Capacity grows to16384 without renaming
live serials or reusing an old stale serial; character and equipment IDs stay
stable. Derived world indexes are rebuilt and authoritative layers rebound.

Door autoclose runs on the composite's fixed-capacity timer wheel, drained
in every pulse; [WorldTimers.md](WorldTimers.md) lists the timed state not
yet on the wheel.

Movement stays in memory. A60-second HPET timer, protocol logout and the
socket-disconnect callback flush pending movement. Item/economic mutations
commit before their replies. Paperdoll and other reads do not force a movement
save. A crash can lose movement since the most recent save; committed item
and economic actions retain their encompassing world state.

Commit logs include HPET milliseconds and separate encode, delta, disk and
total microseconds. Route and nonempty pulse logs report handler and total
owner time before transport sends replies. Compare these timestamps with
GameNet packet/pulse logs to distinguish persistence work from send delays.

Packet and pulse handlers return replies to the encompassing owner. A
handler failure after a durable mutation or a commit failure latches the owner
closed until restart; the partially mutated in-memory candidate cannot answer
further requests. A failed commit
does not grant permission to retry with that candidate.

The first64 MiB of the disk is the append-only journal. Whole-map and
decoration install data follow it. Reuse that exact disk for restart grading.
Earlier USC1 prototype journals are refused rather than converted silently.
The journal has no rotation; a checkpoint payload is bounded at16 MiB.
This is a local acceptance image, not the stage D ISO or hosted deployment.

Retained storage comprises the bounded lane states, two fixed canonical buffers,
a bounded delta buffer, world revision counters,800 character bytes and at
most eight monster-slot comparisons. Packet/pulse dirty checks do not scan
world capacity or encode a checkpoint for a walk. Durable saves encode
and compare the complete bounded snapshot, O(snapshot bytes), before writing
changed qwords. This prioritizes an atomic acceptance build; it does not claim
production throughput. Existing lane action costs still apply, including
combat's bounded child/tile traversals. Compiler heap/time behavior is unchanged.

`proofs/CompositeReplay.codex` uses one synthetic journal across four boots:
the first grades deferred walks, timer/logout/EOF flushes and item/economic
commits, the second restores and commits death,
the third checks ghost/corpse inventory and resurrection, and the fourth
checks the resurrected state. It reports FAIL on an unsuccessful assertion.
The final read does not change the journal.

`proofs/CompositeGrowProof.codex` fails two assertions at main 35671, with
and without the timer wheel: "legacy character serial position and death
survive upgrade" and "legacy vendor ledger and inventory remain bound".

Acceptance remains one composed replay and root's real-client run. Compilation
alone does not establish packet rendering, disk restart correctness or client
acceptance. Lane source attribution and licenses remain with their port chapters.
