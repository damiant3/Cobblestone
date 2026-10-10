# UOAIX -- sub-init for agents

Read in full before your first edit under `apps/uoaix/` (init Step 4c). UOAIX is a persistent MMO world server for the
1998 Ultima Online client (1.25.32), written in Codex, booted on codex-vm. The design is `UOAIX.md`; open work is
`uoaix-backlog.md` and the lane rows in `docs/PM/CurrentPlan.md`.

## How work lands

- Code only, straight to main, one line to root per landing (Damian, 2026-10-09: "all changes go to main, root and
  damian do all testing. no ceremonials").
- Compile what you changed and every chapter that cites it (L-CITERS), then land. No proof runs, no BVT, no fan-out:
  "the proof is me testing it in game" (Damian, 2026-10-07). App work never runs beside the live shard in parallel.
- Compile at your merged head before you land (Damian, 2026-10-08: "never push to main before they confirm it builds"). A
  clean merge after that compile owes no recompile (the 2026-10-09 ruling below).
- A `bvt/*.codex` case compiles only inside a generated unit (`bvt.ps1` inlines every case under one entry): build a
  BvtCheck-plus-your-case unit the way `bvt.ps1` does and compile that. Grep `^  <name> :` across `apps/uoaix` before
  defining a name; a duplicate is CDX3001 for the whole unit. The generated unit carries the text of every chapter it cites as it stood when generated: regenerate it after any edit, or the run grades the old code (red, 2026-10-09).
- A chapter that compiles inside the server can still call names it does not cite, resolved through another chapter's
  cites; a smaller unit then fails CDX3002 (L-SUBSET). Cite the chapter that defines what you call.
- Say in the landing line whether the change needs a fresh world (any catalog item, codec or save-format change).
- A fix or feature adds its positive and negative `bvt/` case (written and compiled, run by root) and its CL names the
  live call site that runs it in the booted server (L-UNCALLED).
- Main moves every few minutes. When submit refuses out-of-date files, `p4 resolve -am` and submit again; no recompile
  is owed for a clean merge (Damian, 2026-10-09: "just submit it. we are operating under the assumption that root will
  do the proofs"). Sync the whole client (`p4 sync ...` from its root), never a subdirectory: a path-limited sync took
  one half of another lane's two-file CL and the merged tree named a type it no longer had (fester, 2026-10-09).
- Before working a register row, check its premise at head: rows go stale within hours here. Close a stale row with
  the CL that made it stale.
- Kill your own leftovers (guests, pwsh waiters, servers, AHK scripts) at every landing.

## Build and run

- The server is one entry chapter: `build/compile.ps1 -Src apps/uoaix/CompositeGameServer.codex -Out <cdx> -Log <log>
  -Kernel seed\Codex.cdx`.
- `apps/uoaix/bvt.ps1` compiles the server and the `bvt/*.codex` cases, installs the map cache and boots a trial. It
  is root's tool; read it before running it.
- A changed `premises.cfg` or `houses.cfg` needs the decoration re-imported (about 27 s; `bvt.ps1` builds it only when absent): `pwsh apps/uoaix/import-decoration.ps1 -ClientRoot C:/Users/Damian/uo1998-client -OutFile $env:LOCALAPPDATA/uoaix-bvt/britannia.dwd`, then pass that file to `switch-live.ps1 -DecorationFile` with a fresh world (a new DWD1 identity cannot decode an old WDS1).
- The live shard is root's: `switch-live.ps1` (detached, backs up the world, refuses a held disk) over
  `supervise-composite-game.ps1`. Never start, stop or copy the live world disk.
- Launch modes: `UOAIX TESTING` keeps the economy CLOSED until Damian plays British and opens it, so `ECONOMY ROWS
  hour=0` on an idle testing shard is by design (`CompositeGame.md`). A trial with no client launches `UOAIX TESTING
  OPEN`.

## Rulings that bind this project

- Worlds are disposable: a format change boots a fresh world; no migrations, no old-version readers, no durability
  tests (Damian, 2026-10-07: "we don't need save durability its wasted code and testing").
- A save never refuses: a check logs and the bytes are written; only a disk write failure fails a save (Damian,
  2026-10-07: "saves don't work for the dumbest possible reasons i have ever heard of"). Fix the class by a sweep of
  every codec.
- No fixed caps except deliberate, content or window caps (`ServerLimits.md`, Damian 2026-10-08). A table indexed by
  ids that can grow must grow; never answer a full table with a bigger constant.
- Healers and taverns never close (Damian, 2026-10-10: "healers and taverns should never close"). Root's reading: every city's healer, tavern keeper and innkeeper trades at every hour; other shops keep their hours.
- Smithing needs an anvil, not a workshop (Damian, 2026-10-10: "smith hammer should work near an anvil, not in a \"workshop\""). Root's reading: a smith hammer crafts within 2 tiles of any anvil item or static, placed by a GM or not, and nowhere else.
- Playable path first: work a player can reach in the real client this week beats a native core nobody can reach.
- Root runs every soak (Damian, 2026-10-09: "agents are running their own soaks, ... you do them so we can keep the box clean and combine efforts. if a soak likely fails, use reasoning instead of brute force"). A lane lands its soak family in `proofs/soak-bots.ps1` and sends root one line naming the families to run; root batches every lane's families into one run and returns each lane its lines. Where a soak would likely fail, reason it out from the code and the logs you have instead of spending another run.

## Read before you touch

| Subject | Read |
|---|---|
| Shops, premises, new trades | `BritainShops.md` ("Premises for new trades"), `EconomyCatalog.md`, `ItemOrigins.md`; every site a new shop touches is in the bowyer's and the cobbler's CLs (`p4 describe` main 41385, 41434) |
| Caps and table growth | `ServerLimits.md`, `docs/Designs/Done/Apps/UoaixDynamicTables.md` |
| Launch, supervisor, testing mode | `CompositeGame.md` |
| Wire packets | `ProtocolFacts.md`, `GameDispatch.md` |
| Saves and codecs | `WorldPersistence.md`, the codec's own `*Codec.md` |
| Town network, its corpus or weights | `TownNetworkHill.md`, `TownNetworkLive.md`: a change to the corpus (`TownNetworkOutcomes`), the frame or the EconomyClock fixture changes the training data, so it needs a retrain (`train-town-network.ps1`, GPU, about 20 s) and `proofs/TownNetworkHillModelProof` re-pinned to the receipt's scores |

## Pitfalls that cost sessions

- `&` and `|` short-circuit on x86-64, so a grow, add or push inside a guard runs only on some calls: keep side effects behind `if` (L-EAGER).
- A side buffer indexed by a table's ids overflows the day that table grows; grep every `ep-buffer` and stride
  (L-GROWINDEX).
- The server compacts (`sl-compact` restores to the serving mark): anything a compacting loop keeps must live below the
  mark (L-BELOWMARK). A record, list or Text built during play and stored into load-time state dangles after the next
  compaction: copy its fields into a record made at load (`tl-copy-spot`), keep a scalar (`faults-said` keeps a
  `chr-hash-text` fingerprint), or write it into a buffer allocated at boot (`ts-write-text`, the store latch).
- A load check must accept every state play can reach. MGM2 refused a keyed chest inside a bank, which play allows, and
  every restart of that world crash-looped (UOAIX-142). Before tightening a codec check, grep the actions that reach it.
- The load-fault line (`LOAD SECTION REFUSED`) prints at the first commit, about 60 s after boot: a boot stopped at
  `READY` cannot show it, so grade a restore after the first `WORLD COMMITTED`.
- `list-push`, `list-set-at` and `__record-set` write through the caller's pointer (L-ALIAS).
- Nothing runs `proofs/`, so a format or table change leaves them uncompilable or aimed at old offsets for days
  (2026-10-09: GameVendorCodecProof named a deleted table, TownGovernmentCodecProof corrupted v1 offsets of a v2
  image). Compile every proof that cites the chapter you change, and fix what no longer compiles in the same landing.
- UOAIX ids collide between lanes: check a new id is unused in CL descriptions (`p4 changes -l`) as well as the
  backlog; the second lander renumbers.
- Catalog item ids are appended in `bb-catalog` order and taken fast. A shelf older than head carries stale ids: port a
  shelf by hand at head, never unshelve it blind.
- A kind-2 purse owner is a label, not an identity: owners span person ids, shops `1000 + index`, residents
  `2000 + i`, workers 3001 on and spawns `5000000 + slot`. Resolve an actor's person through a key
  (`TwWorld.operator`, `cg-operator`), never by reading the purse owner as a person id.
- A proof or BVT that populates Britain on its own `world-new` binds growth first (`bb-fixture-grow`), or it fails the
  day the shops outgrow its world (UOAIX-144).
- The inn's 42 beds (`tl-home-region`) are full at founding: each founder takes the next free tile from its offset
  without wrapping, and the miners and the farmer abort the boot when none is left ("Britain shop has no clear tile:
  Shared lodging", BVT at main 41458). A new worker lodges in a building (`CgfProfile.lodge`, the hunter at the
  tannery), per the ruling that workers live in the town's buildings.
- A binary under a `build-output/` is whatever your workspace last built, not the depot's: blu's 2026-09-08
  `ptx-plug.cdx` regenerated a PTX CUDA refused and was reported as compiler drift (2026-10-09). Rebuild the tool from
  the depot before believing a regeneration diff (L-SAMEVER).
- Before blaming your change for a red proof, run the same proof on clean head (shelve, revert, run, unshelve). On
  2026-10-09 `TownNetworkLiveProof` and `EconomyStateProof` were already red at head.
- `EconomyClock` is a test economy whose emergent behaviour the proofs assert: a fixture change moves every proof and
  corpus that cites it, the town-network weights included (UOAIX-146: five smiths shared one tool by reselling it each
  hour at cost plus 25%, so its quote reached 99595 gold).
- An NPC that buys from a shop (a kitchen worker's `cgg-buy-raw`) opens the shop's buy window first (`v.window i 1`, `ss-window`) and stores it back after: a shop's goods lie in its floor box and crate, and `gv-checkout` refuses a lot not under the keeper (`gv-check-lots`, `gv-owned ... 65`), which reads as "keeper not near" until `missed=5` says otherwise (trial 42027).
- ServUO (and later references) name sound and gump-art ids the 1998 client does not hold: 0x44B, 0x307, 0x3C4, 0x299
  and 0x3BD are silent, and the craft gump's 5054 and 4005-4012 draw nothing. Read the id in the client's
  `SOUNDIDX.MUL` or `GUMPIDX.MUL` (12 bytes an entry; an offset of -1 is absent) before using it; UOX3's `spells.dfn`
  sounds fit the old client.
- A new `Strip` line in `premises.cfg` makes `flora-reference.json` stale, and `transform-flora.ps1` (so
  `switch-live.ps1 -Bake` and a fresh `bvt.ps1` flora install) then refuses. Re-record in the same landing: run
  `transform-flora.ps1 -WriteReference -Reference <a scratch copy>`, check the `original` hashes are unchanged, copy it
  over, and run the transform once without `-WriteReference`.
- `bb-buy-from` buys a seller's whole lot only while the buyer holds none of the item, so a recipe needing more than
  one lot holds (footwear: 4 to 10 leather from 2-leather lots) never fires; buy with `bb-buy-below` and a cap.
- An item's health, amount and hue are 0..65535 (world-fields-valid): a value packed past that fails world-update silently.
  Keep anything wider (serials, ticks) in an owner buffer allocated at boot, as the tavern seats are (CompositeGame.tavern).
- A player is live to combat only while its own session is the shard's active one (`cb-live`), and a refused `cb-select` calls
  `cb-stop` on the selector: an NPC that re-selects a player foe on every visit is stopped by every visit made under another
  session and never swings (UOAIX-195, main 42434). Keep a foe already engaged; select a player foe only while it is live.
- A soak scenario run (`proofs/soak-bots.ps1 -Only ... -Open`) wants `-ClockScale 2`: at 10 the back-to-back commits time out
  British's login and every reconnect. A live table, board or deck is a world item beside its scenery copy; decoration
  serials start at 0x7E000000.
- gs-handle refuses any opcode gp-packet-size does not list before any branch runs, and a refusal disconnects the player:
  a new client packet handled there needs its length in gp-packet-size (the client's lengths are GameOpcode go-lengths).
- A trial runs at `-ClockScale 30`: every `-us` timing in `WORLD COMMITTED` is in the sped-up guest clock, 30 times the
  wall time (an encode-us of 1.5 s is 50 ms real). Compare timings only between runs at the same scale.
- Grade a fresh-world trial at game hour 36 or later: new workers hold no coin until the first payroll (about hour 32),
  and shops are closed from hour 20 to 7, so a worker idle at hour 18 is not yet a fault.
- Copy a trial's `server.log` before stopping its guest: a log once read back 0 bytes after the guest was killed.
- The 1.25 `TILEDATA.MUL` item entry (37 bytes, after 512 land blocks of 4 + 32 x 26): flags 0-3, weight 4, wearable layer 5, animation 10-11 (little-endian), height 16, name 17-36. Byte 6 is not the layer; reading it closed UOAIX-183 on a false premise (2026-10-09).
- `WorldSpawnData`, `CreatureSoundData`, `CreatureCarveData` and `WorldSpawnIntData` are all written by `import-world-spawns.ps1`; change a spawn region, theme or rule in the importer (`$regionOverrides`, `$regionThemes`), regenerate, and read the diff of every output: a hand edit to a generated file is lost at the next import (42760 dropped the carve shearing rule this way).
- A packet told to another player (`cg-tell`, the event ring `ce-add-to`) is dropped without a word when it is longer than a ring row (`ce-bytes`, 16 KiB): a gump told to the other seat that outgrows it arrives as nothing, only its message line shows (UOAIX-230, the 512-byte rows dropped every card gump).
