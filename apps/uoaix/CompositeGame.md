# Composite client acceptance server

`CompositeGameServer.codex` composes mixed vendors, decoration, bank,
skills/items, combat, monsters, death/ankh and client-view reconciliation
through GameNet's authenticated hook. It listens on guest2593.
Root owns the real client and launches the image on host 2593.

## World disks are disposable (Damian, 2026-10-05)

"we are in primary development, there are no old worlds except as convienient
to us. start us fresh everytime if it helps"

A format change does not need a migration, an upgrade path or a refusal of old
disks. Prefer a fresh world disk whenever a migration would cost work.

## Install and launch

For a fresh installation, run `import-decoration.ps1 -ClientRoot <client>
-ReferenceRoot D:/Projects/uo-reference -OutFile <local-britannia.dwd>`, then
`install-map-cache.ps1 -ClientRoot <client> -WorldDisk <local-world.disk>
-DecorationFile <local-britannia.dwd>` ([MapCache.md](MapCache.md)), then
`start-composite-game.ps1 -Artifact <server.cdx> -StateFile <local-world.disk>`
(add `-Testing` for a testing world). Reuse the same disk on every restart.
Never commit or publish the client, decoded cache, DWD or world disk.

NPC speech is composed: `cgs-ready` binds it after the lineup (`cg-speech-bind`,
logging `SPEECH on: N listeners`), `ns-handler` precedes every other C03
handler, and `ns-pulse` follows `tl-pulse`; NSC1 greeting cooldowns persist as
UCC1 version 13 (held at decode, applied by `cg-speech-bind`). Civic adapters
are not composed here.

A live shard runs under `supervise-composite-game.ps1` with the same arguments plus a
new `-OutDir` (UOAIX-48): each guest exit, a latch included, appends the exit code,
uptime, cause lines and log tail to `<OutDir>/crashes.log` and relaunches on the same
disk into `run-N`; a run that dies within 60 s doubles the 5 s delay up to 300 s, and
more than 5 launches in 10 minutes stop the supervisor. `supervisor.json` holds the
current run and guest PID; creating `<OutDir>/stop` shuts the guest down and ends the
supervisor. Relaunches skip British's password prompt (`-NoPrompt`); `-Port` and
`-AdminPort` move the host ports.
`switch-live.ps1 -Artifact <cdx> -World <disk> -OutDir <new> -Running <old OutDir>` switches the testing shard:
it stops the old supervisor through its stop file, refuses any disk a codex-vm names (L-HOTCOPY), copies the
outgoing and reused disks to `-BackupDir` (default `world-backups` beside `-World`) with a hash check, then
starts `supervise-composite-game.ps1 -Testing -Admin` detached and returns once it is serving; `-Fresh` installs
the map cache on a new `-World` (`-ClientRoot`, `-DecorationFile`, `-FloraFile`). `-StartupSeconds` (default 120, the supervisor's maximum) is each launch's deadline: a loaded box boots past the supervisor's 60 s default.
Once a run has logged its first `WATERMARK` line, a run silent for `-HeartbeatSeconds`
(180) is logged `WEDGED`, stopped and relaunched.
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
Testing entry adds a spellbook and recall rune loose in the backpack, a bag
of trade tools, and a bag of blank scrolls and100 of each classic reagent,
each laid out on its own grid position. The bank receives1000 of each
material stack and10000 physical gold coins. These are explicit testing
items, not vendor-purse credit. The harvesting tools among them (pickaxe and
shovel mine, hatchet chops, fishing pole fishes) become economy lots at each
testing entry (`cpr-kit-tools`): the player's economy actor produces each one
and the kit item takes the lot, so harvesting accepts it. A tool whose
production the actor cannot complete stays a plain item and never fails the
entry; the fishing pole is that case today (`proofs/TestingToolsProof`). A durable grant serial for each
character slot prevents restocking after use, deletion, death or restart;
the record is per slot, not per account (UOAIX-10).
Creation and its grant marker commit together before entry replies.

`CompositeGameRules` combines framing declarations once and refuses conflicting
definitions. Vendor use precedes general item use. Combat status selector4
and skills selector5 share opcode34. A successful entry attaches one backpack
and binds that same serial to the vendor account. The demonstration economy
and terms are those in `GameVendor.md`; the first character is the demo
business actor. The composite spawns no sparring fixtures; every boot deletes
a surviving Sparring partner or Veteran opponent (`cgl-remove-fixtures`).
The demonstration provisioner is protected from combat. Vendor lot stacks
move whole through the item handler; vendor checkout owns splitting them.
General item splitting cannot silently invalidate the vendor lot ledger.
Three monsters use the shared combat state and one combined pulse, with home
1440,1720 and radius6, outside the new-player entry area. Startup relocates
surviving monsters from the old1426,1700 home onto distinct walkable tiles
and commits the migration before listening. Their serials and health survive.
The shrine
restores life without reclaiming corpse loot; the ghost keeps its emptied
backpack and its bank box. A ghost (`cg-ghost-may`) walks, speaks, clicks, asks
its status, toggles war mode, answers the death menu, opens its own paperdoll
and uses the ankh; every other action (attack, lift, drop, equip, 0x12 skills
and spells, target, menu, buy, sell, double-click of a door, NPC or item) answers
"You cannot do that while dead." Its speech reaches no NPC handler, and other
players hear it as o and O (`ce-ghostly`). A town healer (Britain's shop 4 and
its bank-row copy) raises a ghost that comes within 4 tiles on the next pulse,
with ServUO BaseHealer's words and sound 0x1F2 (`cg-healed`). At every boot a bank box found inside its owner's
corpse (written before that rule) returns to the owner (`cg-bank-rescue`). Bank permissions and client
visibility are transient. SeasonBC stays disabled pending the exact-client grade.

The shared owner persists game/world state, currency and lane metadata in one
WorldDisk log. The snapshot is a composite of bounded codecs. CPD1 suffix rows
carry ordered qword offsets and exact before/after values in that canonical
snapshot. Recovery checks record order, before images and the final composite
before creating the live owner. Quotes, drag/target cursors and combat
connection/target state reset on restart. Monster movement/replacement delays
are stored as remaining time and rebased on the new boot clock.

A fresh world disk builds the Britain economy (`bb-economy`, `bb-populate`)
and val's three TownLive residents (`tl-populate`) over a fixed 96x192 Britain
`WalkMap` (x 1408-1503, y 1536-1727, so the Lords Clothiers' door at 1471,1695
and the street south of it load) filled from the cache at boot; `cgs-load` supplies
that map. A world created before this keeps the demo economy and boots with
`TOWNSFOLK off`; a Britain world logs `TOWNSFOLK on`.
Britain worlds route `bb-handler` (shopkeeper name labels, then the shared
vendor 3B/9F flow) after the townsfolk 09 handler; `BbTown` is rebuilt by
`bb-bind` at load. The residents' map has
its own WdState (`cg-town-doors`, about 22 MB more) sharing the player
state's door bits; a door toggled on either map rebuilds the other map's two
cells, and a door a resident opens arms the 20 s autoclose.

UCC1 version6 appends a townsfolk section: one length qword (0 or
`tlc-bytes`), the town clock in game seconds, then TLC1, decoded against the
Britain map; a townsfolk world loaded without that map refuses. Version5
(no clock qword) decodes with clock 0. The last 64 bytes of the GVS1 budget
hold the removed townspeople (`GvWorld.gone`, `[remove` on a vendor or banker):
a count of at most 7, then that many serials, the rest zero (`cc-gone-valid`);
a world saved before them reads zero there, none removed. UCC1 version4 combines UGC1/TSC1, full EUC1, GVS1, UCB1, GSI1, monsterUMC1,
shrineUSR1, doorWDS1, a48-byte CGT1 testing-grant section and a magery
section: one length qword (0 or `576 + 368 * capacity`) then MGM2. Decode
holds the MGM2 bytes and every commit before magery attaches writes them back
unchanged; `cgs-start` builds Magery, then `cc-magic-restore` reads them with
the boot PIT clock, and a refusal stops the server. UCC1 version10 appends
one length qword and ASC1 (`asc-size`, active-skill levels, seeds, attempts and
cooldowns) after the town network section; a version9 world loads with
baseline skill levels and is rewritten as version10. UCC1 version12 appends
one length qword and UWS1 (world spawns, `cc-spawn-budget` bytes reserved);
decode holds the bytes until `cg-spawn-attach` binds the controller. UCC1
version14 appends one length qword (0 or `cgg-bytes`, 192) and CGG1, the
economic gatherer (`CompositeGatherer`): its mobile serial, economy actor, ore
node, place, ore gathered, home and node tiles, leg, entrance flag, visit
attempts, trips, backpack, unsold ingot lot, sale tile, ingots made and sold;
it walks on shared routes (`TownRoutes.codex`, TownLive.md) and replans after restore. Version15 appends
one length qword (0 or `cgf-bytes`, 96) and CGF1, the fighter (`CompositeFighter`):
mobile serial, economy actor, loot kept and taxed, kills, gold, home tile, and
the errand word (qword 88: leg, 16 when the hunt has killed, 32 per hunt step,
65536 per tenth of gained combat skill); a zero low 16 bits, which every disk
written before the errand holds, is a fighter at home with no errand, and a
zero skill is the Warrior template's 30.0. A dead fighter keeps its skill. Version16 appends one length qword (0 or `cbe-bytes`, 88)
and CBE1, Lord British's errand (`CompositeBritishErrand`): mobile serial (0
when the mobile is gone), leg, mining tries, gold ore mined, blocked steps,
home tile and the stand tile's height. Version22 appends one length qword (0 or `cgg-bytes`, 192) and CGG1,
the farmer (`CompositeGatherer` at place 300, `cgg-trade-of`): wheat from the strip x 1410..1415, y 1660..1670,
milled at the Good Eats mill, sold to the baker. Version30 appends one length qword (`cn-codec-bytes`) and CNS1,
the plots and building sites (`Construction`); a version29 world loads with none and is rewritten as version30.
Versions 9 and later carry
EUC1 version 4 (found counters), so a disk written before main 35832 is
refused; version8 carries EUC1 version 2 and versions
1-7 EUC1 version 1; header cell
64 records which, and every later section offset follows that length. The immutable DWD1 dataset is install data, not part
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

## Log levels (UOAIX-27)

Every composite line ends with ` level=<level> sys=<subsystem>`, placed before
GameNet's trailing ` ms=`. Levels are trace, debug, info, warn and error;
subsystems are server, network, accounts, world, world-store, combat, magery,
spawns, speech, economy, items, vendors, town, admin and help (`ServerLog`).
`GameShard.log` holds one level for every subsystem and an optional override
per subsystem; a line is written when its level is at or above its
subsystem's setting. `slg-set-all` and `slg-set` change the setting in place
on the running server. The default is trace, which writes every line.
`start-composite-game.ps1 -Admin` serves the owner panel (AdminPanel.md) on
port 2594 of the running server, bound to `GameShard.log`, so the Log levels
card reads and changes these settings live: the launcher generates four role
keys and an epoch per launch, passes them in the launch record's `ADMIN` line
(CompositeLaunch), writes the owner key to `admin-owner.key` in the run
directory, and the server gives the panel three link slots beside the eight
game slots. `test-panel-links.ps1` runs the full browser acceptance against a
panel served through game links (`proofs/GameLinksPanelProof`).

| Lines | Level |
|---|---|
| SEND, POSITION, WORLD ROUTE, WORLD PULSE | trace |
| PACKET, PULSE, WAIT, WORLD COMMIT BEGIN | debug |
| boot lines (MODE, ACCOUNTS, ITEMS, TOWNSFOLK, LINEUP, SPEECH, SPAWNS, READY), LISTEN, WORLD NEW/RESTORED/COMMITTED, KILL, HELP | info |
| REFUSE, KILL lost, HELP refused | warn |
| FAIL, FAULT, REFUSE disconnect flush | error |

Pulses run only for a connected session. The world advances through the
server's world tick once per second, clients or none (EconomyCurrency.md, "The
headless world tick"): each `cg-world-tick` call first advances the town clock
from its tick (`cg-town-headless`: residents, town network and economy hours),
then the gatherer, then arms the fighter's and Lord British's errand timers once
per boot (`cg-errand-arm`, `cg-british-arm`), then drains the timer wheel
(`cg-drain`), then the spawn pass. The server binds Lord British's route window
at boot; nothing binds the fighter's (`cgf-route`), so the fighter stays home. The residents' hourly shift timers (wheel kind 2,
`CompositeLabor`) are not persisted; the first tick or pulse after a boot
schedules each resident's next hour (`cg-labor`).

**The NPC economy waits for the king's gold (UOAIX-86).** Until Lord British's
errand has struck the gold he mined (`cg-seeded`: the saved errand is at
`cbe-done`, set by `cbe-bank` or by British striking gold ingots at the Royal
Minter, `cg-minter-or-plant`), the tick skips `cg-town-headless` and
`cg-errand-arm`, and the pulse skips `cg-labor` and the town move. Harvesters
work from the start: the gatherer, the miners and the farmer step, and their
goods wait for a buyer. In testing mode the errand never runs (`cg-british-arm`): Damian plays British
and opens the economy himself. A new world strikes nothing (`bb-economy`). A world with no Lord British character never
opens. Proofs of the NPC economy open it with `cg-seed-gold`. A trial with no client launches `UOAIX TESTING OPEN`, which
opens it at boot the same way; every boot logs `ECONOMY opened by the TESTING OPEN launch`, `ECONOMY open` or
`ECONOMY closed until British strikes gold at the Royal Minter`. At the default day (7200 real seconds) an economy hour
is 300 real seconds.

**Ruling: workers find their own resources (Damian, 2026-10-08: "there are more miners than that spot will serve for a whole workday. we need to have a "find resource" capability for the npcs at runtime, so when areas get mined or logged out (forestry logging), they keep searching. and farmers need to reap and sow, increasing their bounty each until the farm plot is full.").** A miner or lumberjack whose known spots are spent searches outward at runtime for the nearest unspent resource of its kind on the map (minable rock, trees) and works there, so a workday never ends idle on a spent strip. A farmer reaps what is ripe and sows the empty rows, each season's planting larger than the last, until the plot is full.

**Ruling: every item comes from harvest, transform, consume (Damian, 2026-10-08: "this is the mechanism by which all items must be created. harvest->transform->consume").** No item enters the world from nothing: raw goods are harvested from the map, goods are made only by transforming harvested or made inputs, and goods leave the world only by being consumed. Every item the world uses (a nail for a building included) is made by some NPC trade that consumes harvested or transformed inputs (Damian, the same day: "if there are items, like nails, needed for buildings, then the nail must be made by some npc class, it must consume some harvestable or transformed resource"). Every harvestable input has an NPC gatherer that harvests it (Damian: "and there must be a gatherer for that resource"). Every existing feature is reconciled against these rules, and every future feature ships with its whole chain (gatherer, transform, consumer) complete (Damian: "any feature that exists now must be reconciled against those rules, and any future feature must ship with all that complete"). A vendor refuses only a marked item (a sword, a piece of armour: an item tracked to its owner); a commodity (ore, an ingot, an apple) is fungible and is bought whatever its origin (Damian: "a vendor only refuses marked items. commodities cannot be tracked to owners like that. a pile of ore or an ingot is fungible. the guards can't tell who's apple it is."). The one exemption: every character, player or NPC, starts equipped with the clothing and basic tools of its job or template (Damian: "on clothing and basic tools: yes all characters should come equipped for their basic job/template"). Monsters and brigands follow the same rule: they start with what they need to function, have inventories, pick things up, harvest, rummage through the corpses they make or find, and carry it all until it is stolen or they die; their corpse holds what they carried. NPCs carry no weight limit (Damian: "monsters and brigand npcs the same: they have what they need to be functional in the world. the have inventories, and pick stuff up and harvest things. they rummage through the corpses they create or find, and then they carry that till stolen or dead. npcs don't have to follow encumberance rules. it would be silly for an ogre to get stuck with too much gold in their pockets.").

**Ruling: workers live in the town's empty buildings, not the inn (Damian, 2026-10-08: "there are tons of empty buildings in the north part of town there. my idea was to use them, not the inn. and each character could spread out to fill the town, and we could double up if need be").** Each founded worker gets a home in an empty Britain building, spread across the town, with a second worker in a building only when every empty building already holds one. A worker leaves and returns through its own building's door; the inn is not a worker home. Candidate buildings: the free-sign list in `BritainShops.md`, "Premises for new trades", north part first.

**Ruling: new trades (Damian, 2026-10-08: "we need herbalists and alchemists that buy and make potions and tinctures, scribes that make spellbooks and scrolls, paper millers who make blank books and scrolls from wood pulp and parchment from leather scraps").** Herbalists and alchemists buy reagents and make potions and tinctures; scribes make spellbooks and scrolls; paper millers make blank books and blank scrolls from wood pulp, and parchment from leather scraps. Each trade ships with its whole chain under the rule above (the wood and leather gatherers included). Root's rulings for the herbalist (2026-10-08): the herbalist gathers the five plant reagents (garlic, ginseng, mandrake root, nightshade, blood moss) from wild plants on the town's walk map, one gatherer per reagent where the code allows one raw and one tool per trade, and sells them from its own shop; the alchemist buys reagents for potions and the scribe for scrolls; the healer keeps buying as today. Black pearl, spiders' silk and sulfurous ash get their own gatherers later. Glassblowers make glass (bottles for the alchemists among it) from sand that a sand gatherer harvests (Damian: "we need glassblowers and sand harvesting"). Potters make pottery from clay that a clay gatherer harvests (Damian: "we need potters and clay gathering"). Gatherers sell their raw goods to players too: the farmer sells wheat and the miner sells ingots; a player with Tinkering makes nails; fallen branches are no resource and are removed, and every recipe that used them takes logs, boards or coal instead (Damian: "a farmer can sell wheat, a miner can sell ingots. nails should also be makable with the tinker skill. fallen branches is not a resource, remove that. its either logs or boards or coal."). Fallen branches are no item at all; a log or a board serves as fuel, and logs are the primary source of every derived wood good used in crafting (boards, pulp, hafts) (Damian: "replace fallen branches with logs or boards. you can burn boards too. fallen branches shouldn't even be a thing as far as I care at this point"; "logs are primary source of all `"derived wood`" stuff needed for crafting"). A miner with plenty of money holds its ingots instead of selling them to NPC buyers while the ingot price is low (Damian: "a miner should choose to not sell ingots to npcs when they have plenty of money and the price of ingots is low"). The miner holds out until a player pays more, the price rises, or the miner needs coin, and then sells (Damian: "and instead hold out for a player to pay more, or the price to go up, or for them to become needful of coin"). Hunters take hides from hunted animals and tanners make leather from them; tailors make cloth items only; a leatherworker makes leather armor and leather goods, and a cobbler, a separate specialist trade, makes footwear (Damian: "we need tanners and leather from hunted animals, tailoring should be cloth items only. leather is cobbler or whatever the appropriate title is for one who makes leather armor. we don't have those people anymore."). Buildings that new trades may take over (Damian: "add strength and steel to a list of repurposable buidings for professions I am creating that don't already exist, like glassblowing"): Strength and Steel. Already taken: The Miner's Guild (paper mill), the Britain Public Library (scribe). (Damian, the same day: "leatherworks do not make shoes though, a cobbler does, it is a speciality").

A fresh Britain world adds one economic gatherer (`cgg-new`, after the
residents): a world mobile lodged on a free Sweet Dreams Inn tile that is no
resident's bed, an economy actor with a pickaxe and a backpack it made,
registered as a vendor-path owner (`gv-add-owner`), and an iron-ore node at
place 200 on a free tile of the strip 1408-1415,1620-1659 at the west edge of
the Britain map. The actor's place follows the walk: 200 at the node, 100 (the
shops' and residents' place) from its return home. Each `cg-world-tick` takes
one gatherer step on the residents' map with their doors (`cg-gather`). At
home it first carries an unsold ingot to the blacksmith, else walks to a free tile within two of the
Hammer And Anvil's forge (`WorkStations.codex`), smelts 2 ore there into an ingot lot in its
backpack (`bb-produce`, `bb-materialise`) and carries it straight to the blacksmith, else leaves for the node when it has regrown and it holds a
pickaxe. It walks through the lodging entrance, mines once per step until it
holds two of its raw material (one smelt) or 64 tries, moving on to another ready node of the
same resource at its place when a harvest spends one (`cgg-renode`, never making a node), and walks back the same way; a miner's
try at its vein shows the player within 18 tiles the pickaxe swing (action 11) and the mining
sound (`cg-miner-show`). Each worker logs `WORKER <gatherer|farmer|miner-N> leg L` at every leg
change and every 32 blocked steps (`cs-workers-report`; legs as in `cgg-step`). A harvester whose
tool has worn out walks to the provisioner and buys a pickaxe (legs 8 and 9, `cgg-buy-tool`) once
the treasury holds gold and it can pay; otherwise, or when the purchase fails, it makes its own.
An ingot goes to whichever of the blacksmith and the tinker can pay and bids more (`cgg-buyer-for`),
the blacksmith on a tie; the tinker's restock uses ingots it holds before making any.

Three fibre workers follow the four miners in `owner.miners` (`cg-found-workers`, UCC1 v31 saves them
after the miners): a flax picker and a cotton picker in the wheat field (places 301 and 302, nine
nodes each, `cpr-fibre-install`) and a shepherd shearing wool with a knife in the pasture
1408-1415,1640-1659 (place 303, six nodes). The pasture is a spawn region (`cg-pasture`, id 9303) holding
four sheep of the catalog's sheep profile, which are named, wander inside it and return as spawns do. Flax and wool spin into thread at the Lords Clothiers' wheel and cotton into cloth
at its loom (`cgg-craft` by the recipe whose first input is the raw), and each sells to the tailor.
A worker the inn cannot house is not founded and the server starts without it. They log as
`WORKER flax-picker`, `cotton-picker` and `shepherd`, and bend (action 32) at their node.

A lumberjack follows them (`WORKER lumberjack`): it fells Britain's map trees inside the residents'
map at the town's place, so its trees are the ones players chop. At home, when its tree is not
ready, `cg-lumber-pick` takes a registered tree that has grown back, else registers the map tree
nearest home that has an open neighbour cell and no site (a timber site and node, as a player's chop
makes, at most 16 and never past site 240), and walks to a free tile beside the trunk. A chop
swings the axe (action 13, sound 0x13E); the felled tree draws as a stump and regrows through
`cg-flora-sync`. It saws 2 logs into 4 boards at The Saw Horse and sells them to the carpenter. At the blacksmith it stands on a
free tile within two of the shopkeeper and sells the ingot through vendor
checkout (`gv-checkout`) at the blacksmith's own bid (`gvb-bid`), then walks
home. A refused checkout (the buyer cannot pay) keeps the lot and backs off: no buyer leg until a game day passes or the buyer's coin count rises (`cgg-waiting`); meanwhile the gatherer smelts nothing more, stockpiles its raw material to sixteen, leaves a node of it at that cap (`cgg-capped`) and then rests at home instead of walking out, and keeps vein-mining copper and silver for the crown mint. The crown is the town's reserve buyer: at its counter the blacksmith sells it one longsword a game day while it holds two or more (`csn-crown-buy`), so the coin the smith pays for ingots comes back. An NPC's death deletes its body, so the gatherer's and the fighter's deaths settle at the blow (the composite's death hook, `cg-deaths`, before `cb-die`): their vendor lots leave the ledger as consumed goods into the corpse, which takes an NPC's backpack whole, their owner rows go, and each is recorded dead (serial 0, written so in CGG1 and CGF1); the next step raises it at home for the same economy actor, the gatherer with a new backpack and owner row. A tick with no living watcher still steps the gatherer (`cg-tick`). `proofs/CompositeGathererProof.codex` grades the trip, the smelt, the
sale, the census and three restarts on one disk across four boots. A restart proof runs one boot per codex-vm process against the same disk: compile it once, create a blank disk of at least 64 MiB (`us-open 0 131072`), then run `tools/codex-vm.exe -kernel <proof cdx> -disk <that disk> -output <log> -headless -mem 3072` once per boot; each boot reads the store and takes the branch its saved state selects (`proofs/CompositeFighterProof.codex`: fresh, hunting, dead, revived), so the boots must run in order on one disk and never in parallel against it. After the
gatherer's step, the same tick steps the blacksmith (`csn-smith-step`, `CompositeStation.codex`):
holding two bought ingots, it walks from its counter to a free tile within two of the anvil and the
forge, forges there (`bb-forge`, BritainShops.md), refuses anywhere else, and walks back to its counter.

Packet and pulse handlers return replies to the encompassing owner (UOAIX-48, fault
isolation). A handler failure drops only its session; after a world change it also logs
`FAULT isolated connection=`.

The server always saves, and a failed save never stops serving (Damian, 2026-10-07: "the
server should always save, and failure to save should not shut things down. this isn't
good behavior in a service app"). Every commit runs the binding checks and logs, never
enforces, them (`cs-check`): the vendor binding (`gvsc-valid`) and the townsfolk binding
(`tlc-valid`). Currency is not replayed on save or load (`StateUnification.md`, step 4).
A failing check logs `SAVE CHECK FAILED, saving anyway: <clause>` as an error when the
failure first appears or changes, the commit writes, boot loads that state the same way
(`euc-write`, `gvsc-write`, `tlc-write` and their `-decode-any` readers; the strict
codecs keep refusing for their own proofs), and the next clean commit logs `SAVE CHECK
CLEARED`. A commit a codec cannot lay out (an extent or a structural binding) logs `SAVE
FAILED` (`cs-save-refused`), keeps the last good commit on disk and answers `Ok`, so the
route, pulse or tick is served; the next valid commit logs `SAVE RESUMED`. A store write
failure is the same: the journal still ends at its head (`us-append` zeroes the header
first and advances only after readback), so the owner clears the store fault, logs `SAVE
FAILED ... store write failed, retrying at the next commit`, stays dirty and rewrites the
same place at its next commit (`cs-store-failed`); a compaction whose snapshot committed
before the retired half's wipe failed counts as landed (`cs-landed`). The owner latches
only for an exhausted sequence, or a map window or decoration rebind failure; the latch records its reason (`cs-latched`), the world tick
answers `Ok -1`, and GameLinks stops serving so the guest exits for the host supervisor.
`proofs/CompositeFaultProof.codex` grades the policy on a blank 64 MiB disk.

The first64 MiB of the disk is the append-only journal. Whole-map and
decoration install data follow it. Reuse that exact disk for restart grading.
Earlier USC1 prototype journals are refused rather than converted silently.
The server opens the journal as two 32 MiB halves (`us-open-pair`), and a
checkpoint payload is bounded at16 MiB.
This is a local acceptance image, not the stage D ISO or hosted deployment.

A full snapshot is 13,475,752 bytes (2026-10-06) and is written at boot for a
new world or a version or capacity upgrade; a running server appends deltas
to the live half. When a delta or a boot snapshot does not fit, the server
compacts online (`us-switch`): first, it wipes the other half's first sector;
then, it commits the full snapshot it has just encoded into that half, at the
next sequence; finally, it wipes the old half's first sector and logs `WORLD
COMPACTED`. The switching commit measured io-us=5637042 under codex-vm
(2026-10-06, every sector written and read back), a pause in that one commit
once per half; a delta commit measures about 1 ms. A crash at any step leaves one half that replays to the last
committed sequence: at boot the half with the higher sequence wins, a tie
keeps the first half, and the losing half's first sector is wiped. Boot
refuses a journal whose records cross the half boundary (a journal written
before the halves) and a blank first half beside an unreadable second half.
`proofs/WorldDiskPair` grades each crash state on a 64-sector pair (14 arms,
a pick-and-straddle sabotage turns two red); `proofs/CompositeCompactOnline`
grades a live switch through `cs-commit` and its restart over two boots on
one 64 MiB disk. Compact a stopped world offline:

```powershell
pwsh apps/uoaix/compact-world.ps1 -Source <world.disk> -Target <new.disk>
```

`CompositeCompact` reads drive 0 (the source, never written), picks the
highest-sequence journal among the whole 64 MiB span and the two halves,
replays its latest snapshot and deltas with `cs-replay`, and writes drive 1 (a
copy of the source) as one snapshot in the first half at the source's last
sequence and tick, with the second half wiped; map and decoration install data
stay as copied. A pre-halves journal that crosses the half boundary is
converted this way. The script refuses a source that a VM has open, checks
that the source is unchanged, and requires one record in the new journal. A
journal may begin with a snapshot at any sequence (`us-ordered`), which a
build before this rule refuses.

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
the third checks the ghost's login body, death robe and paperdoll, ghost/corpse inventory
and resurrection, and the fourth
checks the resurrected state. It reports FAIL on an unsuccessful assertion.
The final read does not change the journal.

`proofs/CompositeSpellDeath.codex` uses two boots on one fresh disk: the
first kills a testing-stocked caster with a self-cast Magic Arrow through
`cs-route` and checks the bank box stays on the dead mobile, the corpse is
registered and drawn, and the ghost redraw shows it; the second checks the
bank box and its contents after restart.

`proofs/CompositeGrowProof.codex` grades the growth path on its own disk: boot 1
writes a current-format composite at capacity 4096 with one dead character,
and boot 2 loads it through `cs-upgrade` and `cu-grow` to 16384. A world
written before accounts is refused at decode and never reaches growth.

Acceptance remains one composed replay and root's real-client run. Compilation
alone does not establish packet rendering, disk restart correctness or client
acceptance. Lane source attribution and licenses remain with their port chapters.
