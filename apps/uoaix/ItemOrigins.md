# UOAIX item origins

The rule (CompositeGame.md, Damian 2026-10-08): an item comes only from
harvest, transform, consume; every item has an NPC maker and every
harvested input an NPC gatherer. This file is the reconciliation of the
code against that rule, measured on main 40289 (2026-10-08). A row leaves
this file when its path follows the rule.

## World objects created from nothing

Each `world-create` of an item (kind 2) in `apps/uoaix`, classified.
"Backed" means the object materialises economy stock that already exists;
"from nothing" means no lot or harvest is consumed.

| site | what | origin |
|---|---|---|
| GameMobileBank.codex:35 (`gmb-clothe`) | every NPC's clothing, hair, beard and stylist tool (CharacterStylist rows), guard and vendor outfits | founding equipment, exempt: a character comes equipped for its job (Damian, 2026-10-08) |
| GameSession.codex:149 | a new character's starting shirt, pants and hair (client creation packet) | from nothing |
| StarterKit.codex:27 (`pk-make`) | starter kits (skill tools, reagents, food) | from nothing |
| GameLocks.codex:191 (`glk-cut-door`) | shop door keys cut for shopkeepers | founding equipment, exempt: a character comes equipped for its job (Damian, 2026-10-08) |
| GameLocks.codex:219 (`glk-give-master`) | Lord British's master key | from nothing (admin) |
| GameLocks.codex:247 (`glk-throne-chests`) | Lord British's two kit chests behind the throne, testing worlds only | from nothing (admin) |
| CompositeGameRules.codex:2921 | a raider's stolen good: the shop lot is consumed (`ep-add w.consumed`), then a new object is made in the raider's pack | from nothing (the consumed good reappears) |
| CompositeGameRules.codex:316 (`cg-drop-ammo`) | a missed arrow or bolt on the ground | from nothing (the ammo spent is not the object dropped) |
| MageryActions.codex:120 | Create Food spell | from nothing (magic) |
| BulletinBoard.codex:109 | a posted message | from nothing |
| CompositeTestStock.codex:30 | testing-mode stock | from nothing (testing only) |
| BritainShops.codex:56 (`bb-materialise`) | shop and outfit lots | backed |
| TownLiveState.codex:233 (`tl-coin-make`) | coin shown in a resident's pack | backed by purse coin |
| GameCraftProduction.codex:47, Cartography.codex:31, Construction.codex:632, Campfires.codex:76, GameAlchemy.codex:52 | crafted goods, maps, deeds, fires, potions | transform (inputs consumed) |
| GameHarvestProduction.codex:79, GameCarve.codex:56, GameCarve.codex:161 | ore, logs, meat, hides, kindling | harvest |
| GameCombat.codex:472, CompositeGameRules.codex:1350 | corpses | not goods |
| GameSkillsItems.codex:101, GameLocks.codex:185, GameMobileBank.codex:149, CompositeTestStock.codex:40, Construction.codex:144, CompositeGameRules.codex:1541 and 1543, ShopStock.codex:35 | backpacks, bank boxes, crown bags, trade boxes, shop stock boxes | containers |
| BulletinBoard.codex:49, TavernBoard.codex:31, 76 and 309, CompositePlanting.codex:472, CompositeProduction.codex:121 and 148, GameResurrection.codex:46 and 50, CompositeGameRules.codex:1815, MageryActions.codex:513 and 578 | boards, game pieces, farm and mill furniture, the royal gate, shrines, moongates, spell fields and gates | fixtures and effects |
| ActiveSkillsStealing.codex:93, WorldJournal.codex:55, GameAlchemy.codex (`gal-split`), ActiveSkillsPoisoning.codex (`as-poison-split`) | a stack split, a journal replay, the bottle of one potion split from a stack | not creation |

A drunk or spent potion becomes its own empty bottle (`gal-empty`, `as-poison-spend`); brewing consumes the bottle
(`gal-take`).

## Every catalog item: maker and gatherer

The server installs `bb-catalog` (BritainProduction.codex:183): ids 1-62
from `eg-standard` (EconomyCatalog.codex:27-91), 63-68 from
EconomyMetals.codex:7-18, 69-103 from BritainCatalog.codex:39-63. H is
harvest, C craft (station st). "Self" means the producer harvests the
input itself: `bb-produce`/`bb-make` work back through the whole chain and
`bb-node` creates a node when none exists (BritainProduction.codex:19-23,
52-70, 100-110). Residents' jobs (`tf-work`, Townsfolk.codex:211-228) move
only the town's abstract counters and make no catalog item.

| id | item | origin and inputs | NPC maker | NPC gatherer of harvested inputs |
|---|---|---|---|---|
| 1 | stone | H, by hand | the quarrier (CompositeGatherer.codex, place 403), sold to the provisioner; founding and workers' own tools still harvest it inside other chains | the quarrier |
| 2 | fallen branches | no resource (Damian 2026-10-08): no recipe, buyer, carving or harvest path uses it; the catalog row keeps its id | - | - |
| 4, 14 | water, cabbages | H | self, inside other chains | NONE (each producer gathers its own) |
| 7 | hides | H, carved from a corpse | the hunter (`cgh-profile`), skinning the tameable animals it kills in the Britain graveyard forest (`cg-hunter-carve`); a player's carve; self inside other chains | the hunter |
| 3 | clay | H, by hand | the clay gatherer (CompositeGatherer.codex), for the potter; self inside other chains | the clay gatherer |
| 5 | iron ore | H, pickaxe | the gatherer and 4 miners (CompositeGatherer.codex:65) | the same |
| 6 | gold ore | H, pickaxe | Lord British at the Royal Mine (CompositeBritishErrand.codex:127) | Lord British |
| 8 | meat | H, knife | NONE | NONE |
| 9 | fish | H, rod | fisherman (CompositeGatherer.codex:53) | fisherman |
| 10, 11, 12 | cotton, flax, wool | H | cotton picker, flax picker, shepherd (CompositeGatherer.codex:57-61) | the same |
| 13 | wheat | H, pitchfork (item 120) only | farmer (`cgg-trade-of`, place 300) | farmer |
| 15 | logs | H, axe | lumberjack (CompositeGatherer.codex:63) | lumberjack |
| 16-24 | apples and the eight reagents | H (EconomyCatalog.codex:56-60) | NONE | NONE |
| 25-29, 62 | stone pickaxe, stone axe, knife, trowel, fishing rod, stone hammer | C st1: the stone axe from 3 stone, the others from stone and a board (the rod a board and thread) (EconomyCatalog.codex) | Provisioner, workers' tools, the crew, the crown | NONE (self) |
| 30 | bucket | C st10, clay and a board | self | NONE (self) |
| 31 | iron ingots | C st3, 2 iron ore | miners (CompositeGatherer.codex:338) | miners |
| 32 | gold ingots | C st3, 2 gold ore | the crown (BritainProduction.codex:143-171) | Lord British |
| 33 | flour | C st2, 2 wheat | farmer | farmer |
| 34 | bread | C st6, 2 flour, water, a log burned | baker station | farmer, lumberjack; water NONE (self) |
| 35 | leather | C st7, 2 hides, water | the tanner (shop 15), at its hide rack in The Best Hides of Britain from bought hides (`csn-tanner`); self inside other chains | the hunter; water self |
| 36 | thread | C st4, 2 cotton, flax or wool | flax picker, shepherd | pickers, shepherd |
| 37 | cloth | C st5, 2 thread | cotton picker | cotton picker |
| 38 | boards | C st8, 2 logs | lumberjack, carpenter restock, the crew | lumberjack |
| 39 | nails | C st9, 1 iron ingot | tinker restock (BritainShops.codex:86-89), the crew | miners |
| 40, 41 | weapon (broadsword 0x0F5E), metal armour (platemail 0x1415) | C st9: 2 ingots and a board; 4 ingots | the smith at its anvil whenever its shop holds none (`bb-forge`) | miners, lumberjack |
| 43, 49 | jewelry, arrows | C (EconomyCatalog.codex:78-89) | NONE | inputs as their recipes |
| 44 | leather armour | C st11, 2 leather, thread | the leatherworker (shop 16), at its bench in Premier Gems from leather bought from the tanner (`bb-supply`, `csn-leatherworker`) | through the tanner |
| 48 | bow | C st8, 2 boards and 1 thread | the bowyer (shop 14), at its bench in Quality Fletching from boards bought from the carpenter (`bb-supply`, `csn-bowyer`); thread self | lumberjack |
| 47 | furniture | C st8, 4 boards and 2 nails | the carpenter, from boards and the tinker's nails it bought (`bb-supply`, restocked at its bench) | lumberjack, miners |
| 50 | cooked food | C st6, fish, water, a log burned (the meat recipe waits for row 26's hunters) | the innkeeper, a batch of fish steaks whenever its shelf holds none (`bb-cook`) | fisherman, lumberjack; water NONE (self) |
| 42 | metal tool | C st9, 2 ingots, 1 board | shops 0, 2, 4-7 and restock | miners, lumberjack |
| 45 | bag | C st11, leather, thread | the leatherworker (shop 16) for sale, from bought leather; every shop, resident and worker pack self | NONE (self) |
| 46 | clothing | C st11, 2 cloth, thread | shopkeeper, resident, mayor and guard outfits | cotton picker |
| 51, 67, 68 | gold, copper, silver coin | struck by the mint from consumed gold, copper and silver ingots (`eu-strike`); craft refused | the mint operator (the crown) | miners, Lord British |
| 52-56 | wheat, cabbage, cotton, flax, timber seed | harvest by-product | farmer, pickers, lumberjack | the same |
| 57-61 | apple cutting, reagent seeds | harvest by-product | NONE | NONE |
| 63, 64 | copper ore, silver ore | H on rolled veins | the 4 miners | miners |
| 65, 66 | copper ingots, silver ingots | C st3 | miners (`bb-coin`) | miners |
| 69 | dagger | C st9, 1 ingot, 1 board | Provisioner founding stock | miners, lumberjack |
| 70 | longsword | C st9, 2 ingots, 1 board | smith station (`bb-forge`), guard outfits | miners, lumberjack |
| 71 | plate gorget | C st9, 3 ingots (BritainCatalog.codex:57) | smith station (`bb-forge`), whenever its shop holds none | miners |
| 72, 73 | shirt, short pants | C st11 | tailor founding and restock | cotton picker |
| 74 | scissors | C st9, 1 ingot | healer founding, tinker restock | miners |
| 75 | clean bandages | C st11, 1 cloth | healer, founding only | cotton picker |
| 76 | pizza | C st6, flour, water, cabbages | baker and innkeeper, founding only | farmer; cabbages NONE (self) |
| 77 | shepherd's crook | C st8, 3 boards | Provisioner, founding only | lumberjack |
| 78-103 | grapes, carrots, onions, lettuce, turnips, corn, pumpkins, squash, melons, gourds, hops, pears, their seeds and cuttings | H (BritainCatalog.codex:49-53) | the edible crops: a crop picker each (places 409-420, `cgg-crop-items`), sold to the inn and eaten by its residents; grapes also to the tavern keeper (wine, 152); hops, seeds and cuttings: NONE (hops wait for a craft that uses them) | the crop pickers |
| 104 | sand | H, shovel (item 115) only | the sand gatherer (`cgg-new-sander`) | the sand gatherer |
| 115 | shovel | C st9, 1 iron ingot and 1 board, Tinkering | the tinker (restock, from a bought ingot); the sand gatherer makes its own at founding | miners, lumberjack |
| 109, 110 | glass, empty bottle | C st16, 2 sand and 1 log; 1 glass to 2 bottles | the glassblower (shop 11), restocking bottles at its furnace in the Merchants' Guild from bought sand (`csn-glassblower`) | the sand gatherer; logs self |
| 111-114 | ceramic mug, plate, vase, large vase | C st10, 1, 1, 2 and 4 clay | the potter (shop 12), firing at its kiln in the Heavy Metal Armorer from bought clay (`csn-potter`) | the clay gatherer |
| 117 | blank map | C st14, 1 wood pulp makes 2 | the paper miller (shop 9), from a bought log or pulp; Cartography spends it (`gal-take`) | lumberjack |
| 118 | mapmaker's pen | C st9, 1 iron ingot | the tinker (restock, from a bought ingot); Cartography's tool | miners |
| 121-124 | sandals, shoes, boots, thigh boots | C st11, 4, 6, 8 and 10 leather | the cobbler (shop 17), at its counter in the South Britain row house at door 1450,1711 from leather bought from the tanner (`bb-buy-below`, `csn-cobbler`) | through the tanner |
| 125, 126 | leather scraps, parchment | C st11, 1 leather cut into 4 scraps; C st14, 2 scraps | scraps: the leatherworker (shop 16) from bought leather; parchment: the paper miller (shop 9) from scraps bought from the leatherworker (`bb-supply`) | through the tanner |
| 127 | raw fish steak | C st6, 1 fish cut into 4 with a knife | the fishmonger's cutter (worker place 423), at the cutting table in the eastern Oaken Oar from fish it bought from the fishmonger, who sells the steaks; the fishmonger buys fish from the fisherman and players, the innkeeper and the castle cook buy fish from it (`bb-supply`, `cgg-raw-seller`) | the fisherman |
| 152 | wine | C st6, 4 grapes | the tavern keeper (shop 19), whenever its shelf holds none, from grapes it bought from the inn, leaving the inn 4 (`bb-brew`, `bb-tavern-supply`); it also sells bread bought from the baker and cooked food bought from the inn | the crop pickers |
| 153-161 | amber, amethyst, citrine, diamond, emerald, ruby, sapphire, star sapphire, tourmaline | H, 1 in 20 ore swings (`gh-gem-roll`) | miners (`cgg-gem-yield`), sold to the jeweler two at a time (`cgg-gem-trade`); players mining ore | the jeweler, who sells them and makes jewelry |
| 162-165 | gold ring, gold bracelet, necklace, earrings | C st9, 1 gold ingot and 1 ruby, sapphire, emerald or amethyst | the jeweler (shop 21, restock at its counter from gold ingots and gems it bought, `bb-restock-jeweler`; also sold) | players |
| 119 | kindling | C st8, 1 board makes 4; also H, a blade on a tree (`GameCarve`, no lot) | the carpenter (shop 8), from a bought board; a campfire spends it (`Campfires`) | lumberjack |
| 120 | pitchfork | C st9, 1 iron ingot and 1 board, Tinkering | the tinker (restock, from a bought ingot); the farmer and the flax picker make their own at founding | miners, lumberjack |
| 148 | hinge | C st9, 2 iron ingots, Tinkering | the tinker (restock, from bought ingots; also sold) | building sites: 2 for either house (Construction `cn-bill-item` line 3) |
| 149 | stone block | C st1, 1 stone and a stone hammer, Masonry (economy skill 11) | the provisioner (restock, from stone it bought from the quarrier; also sold) | building sites: 120 for the field stone house (Construction `cn-bill-item` line 2) |

No NPC maker: 8, 16-24, 43, 44, 49, 57-61, 78-103.
A restock (bb-restock) makes a good only from stone, logs and boards the shop holds (`bb-restock-input`).
No NPC gatherer of its own: water,
cabbages (each producer harvests them itself), and meat (no harvester at
all).

## Economy stock created from nothing

`ep-find` adds stock to an actor and counts it in `w.found`, with no harvest
or input consumed.

| site | what |
|---|---|
| BritainProduction.codex:180 | the crown's founding copper and silver ingots (`bb-founding-stock`; only proofs call it) |
| CompositePaging.codex:325 | Lord British's `[add` of a catalog item (`cp-add-lot`) |
| CompositeProduction.codex:213 | testing-kit goods in a player's pack or bank (`cpr-kit-each`) |
| GameVendorBuys.codex (`gvb-admit`) | an untracked commodity a player sells (a marked item, a weapon or armour, is refused: `gvb-marked`) |
