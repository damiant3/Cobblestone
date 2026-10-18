# GitHub Update 70

Covers main 38906 through 45584. UOAIX grows from one town into a populated
Britannia: every city of the 1.25 map has its shops, workers, guards and jail,
the economy runs from harvest to transform to consumption with an empty
treasury that only Lord British's strike opens, players learn and craft under
the 1998 skill system, and every operational table of the server grows at run
time instead of refusing at a fixed cap. UOAIX Assist arrives as a relay and
companion window for the player's client. The compiler grows its decks on
demand, lowers bitwise work through LIR, and the shard boots from an ISO under
BIOS and UEFI. The release seed is `292381CB` (signed and
self-verified); full digests are recorded in `TechnicalDetails.md` at release.
Numbers in parentheses are main changelists.

## Compiler

- **Decks grow on demand**: every phase deck that overflows (LEX, PARSE, SCOPE, CHECK, LOWER, RESOLVE, LIFT and their keeps) reruns with that deck doubled while it fits under the guard page, reporting CDX9014, instead of halting with CDX9002; RESOLVE in the IR path, which truncated with no check, now grows too. Output is byte-identical to a compile at larger fixed decks (red 44804, 45004).
- **LIR lowers bitwise work (CORE-11)**: the bitwise builtins, `list-at`, zero-parameter constants, `list-set-at` and `list-push` lower through LIR; the leaf inliner takes plain Integer constants and bitwise helpers with a bisection lookup, and `inline-single-caller` searches a sorted list. Self-compile went from 8.66 to 7.52 s and PBKDF2 from 18.3 to 1.45 us per iteration across the campaign (red 38983, 44726, 44895, 45032, 45048, 45058).
- **NX boundary follows the code end**: the executable region is cleared of NX up to the image's actual code end, rounded to 2 MB, so a program whose code passes 6 MiB no longer faults on its first fetch above the old fixed boundary (reek 41864).
- **COMPILER-130, UEFI payloads**: UEFI payloads link at 32 MiB with `pe-base`, the stub refuses a load range the memory map does not show free, sizes the heap at boot from the memory map with a floor, and `runtime-init` programs COM1 receive on the UEFI entry so the serial ring fills (red 45022, 45127, 45145).
- **CCE Dingbats**: the Dingbats block (U+2700..U+27BF) is appended to tier 2 in every table copy (red 44775).
- **COMPILER-129, `fail` and `try` on more plugs**: Zig lowers `try`/`fail` as error unions and Perl lowers them natively; the row's census is updated (fester 39039, 39110, 39116).
- **COMPILER-122 and COMPILER-125**: phase metrics print the heap cursor at phase close as `bivy-close` rather than as a high-water mark, and the trace dump prints before the `HEAP:` control line (reek 38940; red 38966).
- **Leaf inliner and polymorphic arguments**: a non-simple argument bound ahead of an inlined leaf takes its own type, not the leaf's declared parameter type; a polymorphic parameter's declared type had defaulted to Integer, so `show (keep-value 9 True)` printed 1 from 44895 until this fix, found by this release's full test-run (root 45554).
- **COMPILER-109 closed**: the last compiler lines over 128 columns, including the CdxCodes registry rows and Utf8Cce block tables, are rewritten with `let` bindings; program bytes unchanged (red 38951, 38968, 39011, 44701).

## Core libraries

- **CORE-10 closed**: the OTA LwM2M loopback trusts only the pinned key and refuses an image signed by a foreign key, and `tools/ota-fetch` compiles again (red 38977, 38979).
- **HttpClient**: `status-text` holds the reason phrase (red 38990).

## Kernel and OS

- **codex-vm masked PIT tick**: codex-vm holds a PIT tick that the guest has masked at the 8259 (OCW1); arm `pic-mask-pit` (reek 41462).
- **Shard boot media (UOAIX stage D)**: the world lives on a virtio disk when one is present (D1); the shard takes a static address from its launch record (D2); `install-map-cache.ps1 -WorldDisk` writes the map cache, archive region and world journal to that disk from the user's own client files (D3); `build-shard-image.ps1` packs the server as a BIOS CDX and a UEFI application into one El Torito ISO (D4), whose BIOS loader is now written in Codex and NASM is retired; the shard reaches READY under BIOS and under OVMF on pc and q35, with 25 forwarded ports and restore-then-commit checked (fester 39971, 44760, 44785, 44794, 45071, 45136).

## Plugs

- **HTML plug**: WebCrypto primitives (HMAC-SHA256, AES-GCM, random hex) emitted only into pages that call them, and framing primitives (sandboxed iframe, postMessage requests); arms `crypto.mjs` and `frame.mjs` (red 42053, 42062).
- **Perl plug**: a plain Integer traps on overflow and the band wraps, graded under perl 5.38; constructor values, Boolean literal patterns and `list-push`/`list-snoc` lower, taking `plug-oracle-arith` from 0 to 47 of 59 rows (red 44831, 44846).
- **Zig plug**: Real arithmetic traps and saturates as on x86-64 (red 44914).
- **C# plug (CSHARP-UNICODE)**: no longer shadows the Foreword's CCE `from-unicode`/`to-unicode`; `plug-oracle csharp` 59/59 (red 44778).
- **PTX and WGSL**: `int-mod` lowers on both, `run.ps1` refuses an undefined callee, and the WGSL runner refuses a module that calls a function it does not declare; arm `int-mod.mjs` (reek 41488, 41496; red 45089).

## UOAIX

### Economy and currency

- **One source per fact (state unification)**: the goods lot table moves into the economy world, one `ep-take`/`ep-put` pair writes lot, pile and totals, every engine debit, craft, harvest and carve names its lot, a lot stores no quantity of its own, and finally a lot lives at its world item's slot chained per actor; replays leave save and load, and the duplicate currency and character copies in GVS1, TLC1, TNL1 and TRC1 go (red 39197, 39229, 39303, 41089; fester 39395, 39469).
- **An empty treasury**: a new world strikes nothing and makes no grant or loan until Lord British strikes gold at the Royal Minter; the TESTING OPEN launch performs that strike and opens the economy at boot, striking copper and silver beside the gold so change can be made; the Minter refuses with a true reason and pays into the royal treasury (val 39219, 39255, 39399, 39729; reek 41079, 41725).
- **The crown always makes change (UOAIX-213)**: copper trades up to silver at the treasury, and a down exchange recoins the larger coin handed in instead of decreeing (val 42549, 42562).
- **Prices in coins (UOAIX-225)**: vendor labels read `6g5s`, quotes round to two adjacent coins and the charge equals the label; `transform-trade-gump.ps1` makes the 1.25 client's buy and sell windows coin aware; players pay with carried coin through a coin deposit at checkout, and a refused checkout names its failing term (val 42910; blu 44196, 44201; red 44168, 44180).
- **Iron is the only manufacturing ore (UOAIX-240)**: no coloured ores; gold, silver and copper are coin metals; makers work by unmet demand and the town network is retrained on the new score (val 43627, 44841, 44869, 44878; reek 44919, 44970).
- **Shop money**: shop bids move with the shop's own stock (1.5x with none, 1x at 10, 0.5x at 20 or more), shops re-borrow only below working capital, loan ids are stable and repaid loans retire at a seal, and every shop borrows from the crown (red 39787, 44013; fester 44109; blu 44103).
- **Banks are buildings (D-17)**: one bank box per bank building per character; crafts draw what the pack lacks from the nearest bank's box and a short purchase takes the rest from the box at the shop's nearest bank (blu 44261, 44269).
- **Harvest, transform, consume**: every catalog item has a producing NPC trade and every harvested input a gatherer (`ItemOrigins.md`); fallen branches are gone (stone axe from stone, hafts from boards, ovens burn logs or boards); a vendor refuses only marked items; coin is minted from consumed ingots (val 40373; blu 40680; fester 40646, 40671).
- **Ore and ingots**: harvested ore names its metal and hue, smelting yields each metal's own ingot art and stacks by metal, harvested lots of one actor merge whatever their grade, and the Royal Mine veins keep a fixed economic place and never deplete (blu 39054, 39077, 39283; val 39035, 39048).

### Shops, keepers and townsfolk

- **Shopkeeper redesign**: goods for sale live in the shop's strong box and crate and the vendor container is the buy window; shops open game hours 8 to 20 with keepers walking to lodging at closing and doors barred at night; any staff member near the shop sells from it; a killed shopkeeper leaves an empty corpse and rises on its shop's first tile five minutes later (blu 39385, 39420, 39440; red 39431; val 39460).
- **Hours and greetings**: healers, innkeepers and taverns never close; a locked shop door says when the shop opens; a closed keeper greets with its closed note in every city; keepers greet only at their own shop by hour and state, announce a state once, and wander their shops (red 40159, 40831, 42935, 43390, 43392; reek 43038).
- **Leisure (UOAIX-222)**: the Blue Boar tavern keeper serves wine and food from noon to midnight, the Conservatory musician plays in the evening, and keepers and workers of every city spend evenings at their city's inn, paying from their wage purse (blu 42894, 42907, 42945, 43077, 43095, 43612).
- **NPCs live in houses (UOAIX-239)**: Britain lodgers and city workers move out of the inns into the town's houses before any house goes on sale; families live together; ambitions move residents and keepers between houses; no NPC lives in an inn while a house is for sale (red 43555, 43557, 43675, 43768, 43800, 43807).
- **NPC attribution**: no NPC is named Opponent; workers dress, carry tools and take titles by trade; civic NPCs, the Minter and Silas are dressed and named; creatures answer All Names with their combat name (val 39755, 40174, 40182, 41867; root 39760).
- **Drink and duels**: townsfolk drink, merry and drunk names show, drunk residents issue duels and drunk witnesses do not tell; Silas accepts a duel after two rounds of ale; workers and off-duty keepers of every city drink and duel (fester 39948, 39950, 39952; val 39708, 44944, 44949).

### Trades and production chains

- **New Britain trades**: the paper mill (scrolls, books, parchment), the scribe (spellbooks and scrolls of every reagent spell), the glassblower in the Merchants' Guild (bottles from sand), the potter and clay gatherer, the quarrier, the herbalist and its reagent pickers, the bowyer, the tanner, the leatherworker, the cobbler (sandals to thigh boots) and the jeweler (gems from ore swings) (reek 40427, 40441; val 40468; fester 40550, 41434, 43011).
- **Fibre, fish and sea**: flax and cotton pickers and a shepherd sell thread and cloth to the tailor; a fisherman and a fishing boat land the catch for the innkeeper, pirates take it one day in four and a royal navy, paid 20 gold a patrol day, turns them back; the fishmonger and fish cutter work the docks (red 39833; reek 39848, 39878, 39920, 39988, 41955).
- **Farmers by tool**: herb, root, vegetable, grain and fiber farmers replace the 17 pickers; the shovel digs carrots, onions and turnips, the trowel the root reagents and small herbs, the pitchfork wheat and flax (UOAIX-120); the farmer resows reaped rows until the plot is full (red 39597, 41422, 41943, 41951; reek 40321).
- **Reagent gatherers (UOAIX-248)**: a pearl diver, silk gatherer and ash miner supply black pearl, spiders' silk and sulfurous ash to the herbalist (reek 43981).
- **The castle household (UOAIX-157)**: Castle Britain gets a kitchen oven and dressing, a baker who buys flour from Good Eats and a cook who buys fish at the docks, and two castle guards on the main floor (blu 41884, 41901, 41936, 42010, 42032, 42502).
- **Merchant fleet and Nujel'm (UOAIX-237, UOAIX-238)**: merchant ships sail lanes from Britain to port shops by best bid and are drawn at sea; Nujel'm's powder of restoration is dug there and traded by blacksmiths, and repairs items under a new durability table with combat wear and smith repair (red 43449; val 43527, 43529; fester 43483, 43487, 43491).
- **Smiths and guards' gear**: the smith forges gorgets, weapons and armour when its shop holds none; guards buy armour with their wages; each town's smith services its own guards; NPC smiths sell the 1998 smith stock (val 40367; fester 40573, 44331; reek 44085).
- **Testing stock**: the testing bank box holds one bag per craft profession, gathering, alchemy, inscription and cartography; British carries a pickaxe and master key and enters with every skill at 100 (fester 40893, 40897, 42599; red 41317, 41807).

### Workers

- **Miners at work**: a miner swings visibly and audibly, searches outward over every rock face, holds its ingots while the bid is below base, and three Britain miners run a rock, forge, smith loop with route-weighted face choice (red 39766, 44205, 44211; blu 40437, 40589).
- **Routing**: closed doors are probed open, walkers pass in one-tile doors, routes are sized from the map, beds must route both ways, a worker whose bed tile is unstandable moves its bed, and NPCs walk through each other while only players block (red 40473, 41794; val 41549, 41580, 41614, 41882).
- **Timing wheel**: workers, shop stations and trades, the townsfolk walk, spawns, combat rounds, the save timer, spawn slots and corpse decay step on one timing wheel; idle workers back off (blu 41063, 41073, 41075).
- **Threats to workers (UOAIX-262)**: a worker's corpse is reported, a patrol hunts the killer, a guard-cleared spawn stays cleared, and hostile spawns spare town workers (fester 44399; reek 41753).

### Cities, wilds and dungeons

- **Every city populated (UOAIX-217)**: each city gets a walk window, its shops founded at boot from UOX3 (Trinsic, Moonglow, Yew, Wind, Skara Brae, Ocllo, Magincia, Nujel'm, Jhelom, Serpent's Hold, Buccaneer's Den, Vesper, Minoc, Cove), shop hours and counter restocking, and its own workers on per-city route planners (red 42708, 42776, 43005; val 42893; fester 42848, 43211).
- **City guards and law**: guard patrols and posts per city from UOX3, city crimes and a jail cell per guarded city, a law guard table that grows past 32, and a lumberjacking swing inside a city booked as a misdemeanor (red 42988, 43008, 43015, 43092, 43116, 43128).
- **Forests and east Britain**: city lumberjacks fell in a forest outside their city; ten keepers populate Britain east of the river and the East Britain Bank gets a banker (reek 43135, 43603, 43640; fester 44722).
- **Wilds and dungeons (UOAIX-218)**: themed wild regions (lizardmen, polar bears, daemons and dragons by the Shrine of Humility), undead prisoners in Wrong, spawn homes that breed and retry, and 326 ServUO teleporters into all ten dungeons, pets included (val 42722, 43074; reek 42766, 43080, 43159; red 43411).
- **Monsters**: Britain's patrol guards attack hostile spawns and townsfolk fight raiders as militia; orc camps raid and wreck shops and carry off goods; monsters hunt street NPCs, keep their pace, leash to their home region and spill at most 8 tiles past it (reek 39674, 39680, 39788, 40088, 41891, 43304).

### Law and government

- **Stage L, the law**: Britain's mayor and foot patrols; witnessed theft, blows and kills are Law deeds with classic notoriety; arrest on submit or a fight; shops shun witnessed offenders; the jail has walls and an indoor cell; consensual duels; NPC testimony (val 39525, 39557, 39570, 39573, 39625, 39634).
- **Guards as mortal NPCs**: guards hold trained skill, run to a call on foot, answer only within 36 tiles, fight to the death, focus fire on the first striker, and a killed guard rises on its patrol's next waypoint; struck guards turn on the striker and fallen guards die on screen (val 40276; fester 43895, 44284).
- **Town government (UOAIX-117)**: Britain's government is founded at boot with a town purse, guard payroll, carpenter crew on repair orders, essential workplaces staffed by the mayor, and gifts accepted beside the mayor (val 39731, 39835, 40003, 41453, 41507; red 41392).

### Construction and housing

- **Stage C construction**: plots by `[plot`, deeds, building sites, bills delivered by builders paid from the holder's bank, house doors, signs, keys and a thatched cottage design; a deleted character's land goes to the crown (fester 39778, 39794, 39880, 39891, 39903, 39910).
- **Masonry and building parts (UOAIX-122)**: hinges, Masonry as a skill with stone blocks and a field stone house design, the mallet-and-chisel menu, deeds sold by the Saw Horse carpenter, and hired builders who buy the missing bill from the shops (red 42407, 42499, 42517, 42574, 42622, 42638).
- **Player housing (UOAIX-236, UOAIX-252, UOAIX-253)**: scenery houses become player houses with signs and keys, runes marked inside belong to the holder, lock-down and secure containers, house ban and eject; deed houses stand at once when placed (fester 43463, 43469, 44055, 44063, 44710; val 45440).
- **Buildable multis**: `multis.cfg` designs ship to the client as MULTI.IDX/MUL, placed multis collide from the map cache, `[decor cut` and `[decor compose` turn a map rectangle or a set of decorations into a new design with front doors and locks, and deed houses build from those designs (reek 45422, 45432, 45448, 45451, 45460, 45463).

### Skills and crafting

- **The 1998 skill system (UOAIX-242)**: the 1998 skill check and gain on every player practice path (combat, active skills, crafts, harvests, casts, repair), watchers and the divisor-4 share, no skill cap or decay, the 1998 stat gain, and craft failure losses (fester 43638, 43665, 43745, 44087; reek 43667, 43689).
- **The 1998 smith**: a 46-row smith menu with quality bands on damage, shield AR, armour defence and max hits, anvil and forge within 3 tiles, and UOX3 axes (blu 43721, 43887, 43908, 43920; red 43418).
- **Carpentry by tool (UOAIX-174)**: each carpentry tool opens its own menu, with furniture, instruments, a dress form, spinning wheel, training dummy and the loom deed at ServUO skill ranges (blu 42086, 42186, 42206, 42242).
- **Craft gump (UOAIX-185)**: categories on the left, items on the right, Make Last, for every craft tool including Alchemy and Inscription, fitted to the 640 by 480 window, with a REPAIR button at the smith (val 42084, 42098, 42995; blu 44248; fester 44424).
- **Crops and trees**: crops 28 to 38 and the pear and peach trees, every food crop edible, planted and wild trees grow from stump to sapling to tree, grapevines are picked by hand, and Castle Britain's orchard is picked by double-click (fester 39373, 39549; red 39522; val 39344; blu 42528, 42544).
- **Skills with a use**: Tinkering, Tactics and Musicianship gain a player use, every skill tool has a shop source, Camping, Cartography and Taste Identification run whole chains, snooping rolls for players, and the smith's hammer and tinker's tool need their anvil and workbench (reek 39389, 39404, 41211, 41213; fester 40960; blu 44241).

### Magic and combat

- **Spell ladder (UOAIX-201, UOAIX-202)**: damage per mana and per second rises with circle, Earthquake one-shots orcs and ratmen, Mana Drain and Vampire slow regeneration, and mana regenerates three times faster (reek 42445, 42456; val 41887).
- **Spells in the client (UOAIX-170, UOAIX-210, UOAIX-220)**: spell sounds and visuals the 1998 client lacks are replaced, fields place the tiles that fit across a street, Dispel Field takes a ground cursor, Energy Vortex summons, a refused summon fizzles, and Alt-B opens the spellbook (val 42039, 42123, 42772; fester 42473, 42532; reek 43184).
- **Combat**: players fight each other (UOAIX-247), monsters poison by UOX3 strength, karma and fame on kills with paperdoll titles, a struck monster fights back past its leash, and bandaging an uninjured patient is refused (fester 43977; reek 42044, 42192, 42236, 42486).
- **Pets and mounts**: pet names, `<name> attack/defend/follow/guard/stay` and all follow me, pets that defend themselves, pack animals that carry a pack, mounting horses and llamas and a ridden nightmare, a royal stable for British, and stablemasters in nine cities selling animals at ServUO prices (val 42165, 42461, 42836; reek 42831; red 43398, 43409).

### Tavern games

- **Backgammon (UOAIX-118, UOAIX-178)**: the inn board with dice, refereed moves, bar and bear-off, a house patron, wagers and gammons, Silas the gambler, then every backgammon set a live two-player table (reek 39505, 39606, 39653, 39666; fester 42101, 42117).
- **More games**: chess and checkers tables, Crazy Eights, Go Fish, Poker and Blackjack at the tavern card tables with watchers, and battleship at the shipwright's (fester 42252; reek 42260, 42287; blu 42264; val 42985).

### Plays, stories and the decorator

- **Plays**: a play script compiler, a runtime with cast, cues and a line ring, a story and set registry that refuses overlapping areas, a saved PLY1 section, coin, item, give and take cues, an admin Plays tab, and four GM play aspects including Director (fester 41665, 41691, 41699, 41708, 41727, 41728).
- **Decorator**: GMs with the decorate power place, mark and nudge decorations from a menu or gump, paint and raise land, lay runs, fills and whole multis, toggle guard-zone and spawn overlays, and tag decorations into story sets; `switch-live -Bake` bakes marked items and land edits into both player clients (blu 41764, 41800; red 41704, 41740, 41799; val 41676).

### Client and world presentation

- **Client packets**: packets up to 65535 bytes no longer drop the player, `0x22` resync is answered and unclaimed packets are skipped, map pins and writable books are saved, bookcases shelve readable books, and Britain's bulletin board works (reek 42355; fester 42068; val 39726, 42202, 42226, 43098).
- **Weather, day and light**: the 1998 weather with snow regions and seasonal rain, day and night, `[light`, light shapes for fires, gates, lamp posts and candles (blu 43739, 43758; val 43749; root 44140, 44142).
- **Sounds and drawing**: every good drop thunks and every refused one swooshes, ground items are drawn on arrival and at any z, corpses are drawn dressed and keep their facing, doors and lifts redraw for every viewer, and a single click names any item in sight (red 41822; reek 39333; val 42509; blu 40137, 40150, 40803).
- **Game gumps (UOAIX-230)**: gumps wrap, keep margins and sections, use 1998 GUMPIDX art, and update live for both seats (val 41919, 42966).

### Admin panel and Game Masters

- **Codex DOM admin panel (UOAIX-173)**: the panel is built in Codex DOM through the HTML plug end to end, with tabs for Overview, Treasury and the Crown ledger, Accounts, World, Plays, Game Masters and Activity, a market history ring, and a Houses card (red 41303, 41310, 42051, 42096, 42321; fester 43476).
- **Stage A, GM powers**: Lord British dubs GMs; dubbed GMs go to, inspect, kick, mute, jail, ban, unstick and answer help pages in game, every action audited; panel actions apply on British's next pulse and also while he is logged out (blu 39820, 39845, 39955, 40376, 40527; reek 44080).
- **`admin-read.ps1`**: reads the live admin state without a browser (red 41616).

### Server scale and storage

- **Dynamic tables**: a paged world table with a 2^24 slot ceiling and two-level page directories; the item catalog, economy actors, purses, loans, stations, nodes, vendors, accounts, law cases, government rows, townsfolk, harvest sites, spawns, plots, pages, posts and 32 game links all grow at the round's growth point; `ServerLimits.md` lists only the deliberate caps (reek 40615, 40856, 41016, 41076, 41126, 41130).
- **Checkpoint and store**: a growth commit relayouts the checkpoint and writes only new rows (38 KB in 53 ms instead of a 9 s snapshot); the store has no fixed record cap, writes and reads 64 sectors per call, restores in bounded time, and the 60 s save no longer drains a compaction snapshot in one call (reek 41022, 41088, 41763; red 43988, 44068, 44266).
- **Tick and encode time (UOAIX-196, UOAIX-216)**: one HPET read per round, government audit and economy validation once a game hour, byte loops eight bytes a step, the save written one 32 KiB bulk per tick, keeper searches bounded and sliced, and TICK SLOW and WORLD ENCODE DETAIL lines that name the slow part (blu 42383, 42862, 43002, 43811; fester 43362; reek 44904).
- **Stability**: a garbage walk collider refuses the step instead of trapping, dangling values after compaction are fixed, a dead worker no longer fails the production tick, and boot heap fell by 157 MB with decoration colliders down from 99 to 9 MB (fester 41593, 42303, 42342; reek 41447; red 41285, 41626).

### Town network

- **Stage N**: every miner, the farmer and every townsperson has a town-network state framed from hunger, coin, goods and the hour; a raid in sight raises alarm; the smith's trade opinion falls as the ingot price rises and the miners' rises with demand (val 40029; blu 41420, 41430, 41463, 41611, 41625).

### Live shard tools

- **`switch-live.ps1`**: switches the live testing shard with a stop file, a hash-checked backup, a fresh map cache and a detached supervisor, and bakes the decorator's work into the player clients (reek 39014; val 41676).
- **`bvt.ps1`**: the UOAIX build verification test compiles the server, boots a fresh world, logs in, restarts and restores, and runs unit cases in 41 s (fester 40163, 40199).
- **Soak bots (UOAIX-49)**: scenario families for crafts, spells, tavern games, cities, dungeons, GM actions, plays and trade, with GRADE lines (val 42313, 42386, 42605; fester 42351; reek 42375; blu 42616).
- **Other tools**: `packet-proxy.ps1`, `play-live.ps1`, `export-archive.ps1` and `PlayerClient.md`, the player client setup (fester 40287, 45122; blu 42013, 43939).

### UOAIX Assist

- **The relay**: a relay with the 0x8C redirect, framing, state, packet injection and a control port, in a window docked to the client (blu 44297, 44312, 44324, 44344, 44359).
- **Panels**: status, skills, chat, sounds, a shaded and textured map with pins, kills and creature pictures, an NPC atlas with shop stock and price reports, session trips and per-pet history, and a combat row with DPS and a bestiary (root 44519, 44539, 44603, 44640; blu 45500, 45504).
- **Loops**: mining with one or two tools, blacksmithing, harvesting crops and wool, milling, spinning and weaving, smelting, magery training and tailoring, with fatigue-aware fail-safes, reagent autopickup and a timings panel that auto-tunes delays (root 44448, 44555, 44571, 44613; blu 44828, 44863).
- **Circuits**: tailoring, reagent buying, town survey, refresh and mining circuits that walk a floor-aware path with teleport hops, travel by rune library, bank, sell and buy across cities, and `fill <container> <count>` (blu 45110, 45142, 45314, 45326, 45354, 45408).

## Apps

- **ClipBpe**: a word-final capital sigma lowers to U+03C2, and the letter, digit and lowercase tables agree with Forge on every code point CCE carries (red 39009, 45070).
- **Website**: the site gains a UOAIX landing section, feature list, get-started guide, screenshots and the keeper's desk page (red 43498; val 42016, 43510, 43867).

## Tools and release documents

- **COMPILER-131**: the cite-gate runs both LIR dump pins on every `codex/compiler` change and grades census and pins whatever the BVT answers; both harnesses refuse a stale log (red 45062).
- **GPU arm wall budgets**: `.wall` sidecars for `flux-condition`, `sdxl-guidance-extras`, `sdxl-pag-euler` and `clip-sdxl-encode`, sized from loaded runs (red 45076, 45083).
- **`cdx-to-pe` (UOAIX-113)**: `Find-FuncOffset` matches a whole debug-map entry only, so `opening` no longer matches the tail of another name (reek 39246).
- **Release record**: the Update 69 release documents and artifacts (red 38924).
- **Gate upkeep**: `plug-smoke` grades ptx on its own device-kernel probes, because ptx refuses a program that calls a function PTX does not define; the `cdxtopeScript` raw-shell record is re-baselined for the UEFI stub work (root 45516, 45567).

## Release proofs

- **Proven for this release (seed 292381CB)**: the full gate, including the CDX hard fixed point in one pass with the gate's compiler byte-identical to the depot seed, the text fixed point, the full test-compile (2,016 chapters) and test-run, the plug, generator (0 drift), deck-headroom, wasm and browser phases; the full app sweep (561 units, 558 clean, 3 known-dirty, 0 regressions); IR fidelity (0 unexpected); the diverse double-compiling witness (the Roslyn arm and the Codex arm both 3,828,773 bytes, 0 differing bytes outside the signature region); the battery `-Tier all` (2,284 subjects: 2,214 pass, 63 skipped, 7 failed); the poison build (2,284 subjects: 2,214 pass, 7 failed, no uninitialized-field read); `diag.img` rehearsed on all 60 arms on both beds; `seed/Codex.map` matches the seed's embedded MAP1 on all 5,843 rows.
- **Graded alone**: every battery, poison and test-run failure is a GPU diffusion subject over its 60 s wall budget at 16 ways; all seven pass run alone on the release compiler.
- **Box profile** (`docs/Agents/box-release-2026-10-18-u70.csv`): free memory bottomed at 13.65 GiB during the gate's test-run; the peak was 17 guests during the gate's later phases, the live UOAIX shard among them.
- **Fixed during the release**: the leaf inliner miscompile above (root 45554, a new seed), and the two gate upkeep items under Tools.
