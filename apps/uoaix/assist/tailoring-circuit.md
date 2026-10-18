# The tailoring circuit (UOAIX Assist, stage 6)

An unattended loop on uo1998.com that buys shuttles of thread, weaves bolts, sews fancy shirts, sells them to tailors
and banks the gold, all inside guarded towns. Owner: blu. User: Damian. The stage's rulings, in brief, are stage 6 of
`assist-plan.md`; this document is the design: what was decided and why, what the wire showed, what each piece does,
and what is proven live.

## Decisions (Damian, 2026-10-17)

| decision | his words |
|---|---|
| Build it; he owns the account risk. uo1998 publishes no macro policy and has no support. | "as long as they don't tell me to stop, I think this is almost expected at this point lol. yeah lets build it out" |
| A simple, safe loop of tailoring only, in towns. | "i want a simple safe loop of just tailoring in all safe spots in town" |
| No round zero: the circuit picks its own kit, cloth and menus. | "no i need you to automate all this", "no round 0 here" |
| Packets, not emulated clicks; every window or cursor the assist causes stays off the client. | "lets just inject the right messages instead of user emulation of clicks unless absolutely necessary" |
| Walking is injected (an exception to the plan's rule 6); the client need not animate it. | "we don't need to make it animate if the server is responding the correct way" |
| Route around characters and players' piles; open doors. | "if you do walk through anothe char, you will take a stam penatly", "look for blocking items too ... like big ore piles, boxes", "tracy was behind a door, you have to learn to open those" |
| Address a vendor by name. | "its better that way, since otherwise other npc can intercept" |
| Buy a vendor's thread out in one purchase; restocks grow when a vendor sells out, every ~15 min, up to 999. | "maximizing the count in a single transaction is best", "if they have sold out of thread, the next respawn gets more", "respawns happen about every 15 mintues" |
| Sell what the sell list holds, each at its own lowest price, fancy shirts at 23 (setting `circuit.sell`; control port `sell-list`, `sell-item <arts> <gp> <name>`; what is sewn, `circuit.item.menu`, `make-menu`), 5 items a sale; sew up to a multiple of 5. | "lets make this price and even the item a configurable so no code change", "take a list of items and min prices", "sell as low as 23", "you can only sell 5 items at a time", "if you have like 14 shirts to sell, its better to make 1 more for the even 5" |
| Carry 2 sewing kits. | "i usually keep 2, in case for some reason that particular vendor doesn't have one" |
| Travel by Recall from the bank box's runes at any bank; runes never leave the bank; a recall is cast again only after a fizzle; 30 of each recall reagent, refilled at any bank ("30 of each recall reg, not 20"). | "all the destinations we need are right there in any bank", "never take runes out of the bank", "it sometimes fizzles but it works. be persistent", "wherever you go, there is a bank" |
| Never repeat anything that got no proper answer (L-FUTILITY, the plan's rule 1). | "if it didn't get a proper response the first time, DO NOT EVER REPEAT IT OVER AND OVER" |
| Bank the gold over the 300 purse at every recall's bank (the recall goes from it); a walk to a bank only for the deposit (after a sale, before a wait) only above 2000 gold (`BankOver`). In the city sold in, from the bank wall; never overflow a gold pile (60,000), prefer an existing pile. A bank that fails is passed over for 15 minutes and the gold carried; the circuit goes on. | "walk to the bank wall, say bank", "deposit the money carefully not to overflow the pile of gold there, but to use it if possible"; by hand on 2026-10-17 he banked at each recall's bank (2042, 1412, 970) at about 2 times the circuit's rate |
| Sell in Magincia first, then Moonglow's Brian (27 for 90 shirts, 2026-10-17). | "next time, try magincia first" |
| Stop only for staff, attack or death; ignore players. Staff: a "GM " name, a game master/counselor/seer/admin title, or the GM robe. Game master, not grandmaster. | "it should just ignore anyone who isn't clearly a GM", "players are annoying sometimes", "the custom was to name the char GM <whatever>" |
| Think two steps out: before leaving a place, do there what the step after next would come back for. The shop's work (sell, thread, weave, sew) before the bank; the gold banked at the bank a recall goes from, or before a restock wait; a sell trip buys thread in the shop before it banks. | "am I in the place i will come back to after the very next step, and maybe i could just kill 2 birds with one stone, and one less round trip", "it is mindblowingly stupid to run back to the bank to deposit gold, only to run back to tailorshop to buy 10 thread when you started in the tailor shop" |
| Scrap cloth goes on the floor, not into bandages. | "take the bit about making bandages out. just move the scrap pile the floor somewere" |

## What the wire showed on uo1998

- The server is ServUO or a fork: its bank line ("Bank container has N items, M stones") and full-container line match
  ServUO's word for word. The source is `D:\Projects\uo-reference\ServUO`; read it before guessing a rule.
- Speech (0x03): type 0, the player's hue and font, the text with its NUL. A vendor hears its name from 3 tiles, by
  range alone (ServUO `BaseVendor.cs:2497`); a banker hears "bank" from 12 (`Banker.cs:299`).
- The pack holds 125 items counting every bag's contents, not worn items (`Container.CheckHold`: TotalItems + 1 > 125,
  "That container cannot hold more items."). A drop that joins a stack is not counted, so reagents merged into the
  reagent bag's stacks cost no item.
- Buy: "<name> buy" opens 0x24 (gump 0x30), the stock 0x3C and the list 0x74. **The 0x74 list is in the reverse order of
  the 0x3C stock** (Jannelle: list last "Shuttle of thread @3" is stock first, 0x0FA0 x19). The answer is 0x3B: vendor,
  flag 2, then layer 0x1A, serial, amount. The vendor's line "The total of thy purchase is N gold." is what was charged;
  a second purchase from a depleted stock cost 4 gp against the listed 3.
- Sell: "<name> sell" opens 0x9E (serial, graphic, hue, amount, price, name per item); the answer is 0x9F (vendor,
  count, serial and amount per item), at most 5 items. A city's price for fancy shirts falls with the shirts sold there
  (Damian: "sales have pushed it down"; 2026-10-17 Michelle, Tracy and Lynn offered 24 while Jannelle in Nujel'm took 100
  at 27): under the floor, sell in another city. A vendor out of sight is where the atlas last saw it, so the walk to
  it is taken again from where it ends.
- Recall (spell 32): cast 0x12 type 0x56 "32"; the cursor comes after the cast time (longer than 3 s), so a cast claims
  its cursor 8 s; the player's spell words precede it; "The spell fizzles." is retried. Proven: two fizzles, then the move.
  Another mobile's speech during the cast does not end the wait for the cursor; a missed cursor leaves every recast
  "You are already casting a spell." Black pearl, blood moss and mandrake root each; Kwik keeps them in the small
  backpack inside the pack, and the bank box has more in a bag.
- Bank runes: Kwik's runes are in his bank box, in the pouch 459637C1 (Britain, Magincia, Moonglow, Vesper, Yew, Trinsic,
  Skara Brae, Serpent's Hold, Ocllo, Nujel'm, Minoc, Jhelom, Cove) and loose (two Vesper, Moonglow, north mines,
  Serpent's Hold, Skara Brae). "bank" opens the same box at every bank; a rune is labelled by a single click where it
  lies (`labels <container>`) and the recall is cast at it there. The pack keeps one rune, Skara Brae's.
- Shop runes: the bag 43DD0F1E in the bank box holds a rune marked at each tailor shop (Damian, 2026-10-17: "a set of
  tailor shop marked runes in that bag"): Skara Brae, both Vesper Spinning Wheels (41E89250 the south one, 44DE6EA5
  Morena's), Trinsic, Jhelom's north Needle, Nujel'm (outside), Magincia, Moonglow (outside, the shop is full of cats),
  Ocllo, Britain's Lord's Clothiers. A rune marked in a shop region is labelled for the shop. A rune is renamed by its
  double-click's text prompt (`rename <rune> <text>`). Mark is cast from scrolls (Kwik's Magery fizzles the spell
  otherwise); a Mark takes 20 mana and a Recall 11, of Kwik's 47, about 1 back every 5 s.
- Sewing: the kit's double-click asks "What cloth shall I use this sewing kit on?"; menus "Choose a category." (shirts)
  and "What kind of shirt?" ("Fancy shirt - takes 8 cloth."); a pick is 0x7D: dialog serial, menu id, index, model, hue.
  "You create the fancy shirt ..." or "Tailoring failed. Some of the cloth is ruined."
- Weaving: each loom use takes one thread; "You create some cloth and put it in your backpack." brings a new bolt
  (0x0F95) as its own item. A used stack is deleted and re-sent about 130 ms later.
- Walking: 0x02 direction and a step number the server takes in turn (0 to 255, then from 1; 0 again after 0x21 or a
  0x20). The 1.25 client ignores 0x97 (move player) and 0x77 for itself, so it cannot be made to animate assist steps.
  The answer (0x22) carries no height: the assist takes the player's z from the client's map (`UoMap.StandZ`: land at
  its corners' average, surfaces at their tops, bridges and stairs at half), or the client draws the player through
  walls on a street and in a hole on a slope. The redraw (0x20) goes out in the answer's place in the server stream: a
  redraw sent after it lets an item that comes into range with the step reach a client a tile behind, which drops it
  (the east tailor's door and sign). Steps walk by default, 400 ms on foot and 200 ms mounted (a mount on layer 0x19),
  and run (direction | 0x80) at 200 and 100 ms under `pace auto` (the default) while the mount's stamina estimate holds it
  rested, under `pace run`, and in a flee; the assist's own steps feed the estimate as the player's do; 2 go in flight before their answers as the client runs (sniffed mounted: 25 steps in 2.8 s, a median of
  94 ms, 2 outstanding); a refusal rubber-bands the client to the server's tile and the walk plans again from there.
  Measured: 66 tiles across east Britain, 86 steps at a median of 109 ms, 4 refusals, all two closed shop doors
  (opened, about 1.4 s each). The route is the fewest steps, and of those the fewest diagonals.
- The circuit's order: bank the gold, sell the shirts, buy thread, then weave and sew (Damian, 2026-10-17). The gold
  is banked in the city Kwik is in, its banks nearest first; a bank that does not answer is not asked again for 15
  minutes. Britain is one city among the rest: its shops are used and trips go there, and it is no centre. A tailor that offered under the floor is not asked for 45
  minutes (a price comes back: Tate 16 to 24 in 46 minutes, Ella 14 to 24 in 56), one that bought no more for 15
  minutes, a thread vendor sold out for 15 minutes; all three are kept on disk (underfloor-, circuit-<server>.txt).
  A sell trip asks every tailor in the shop that pays the floor, each by name. A sell trip goes to the tailor longest since a sale (the
  trades history). A refusal with "too fatigued to move" rests 10 s and walks on.
- A trip never goes to Wind (Damian, 2026-10-17: "do not go to wind").
- Guard rules differ from city to city (Damian, 2026-10-17: "the criminal justice rules are not consistent from city to
  city"): killing a cat is a crime in Skara Brae and allowed almost everywhere else ("killing cats in skara brea is not
  allowed, but it is allowed almost everywhere else"); a Magic Arrow at a cat in Shear Pleasure made Kwik a criminal and
  the guards killed him. Nothing
  the assist does attacks anything in a town; a cat on a landing or in a doorway is walked around.
- A trip picks Magincia, Nujel'm and Jhelom first ("great for solo"), then every other city alike, Britain included
  (Damian, 2026-10-17: "yes it should visit brit, but it should also not consider it the primary central location";
  he sold at the Lord's Clothiers and the east tailor at 24).
- Every tailor sells thread (Damian, 2026-10-17: "its always a tailor that is thread selling, and they all do"), so a
  thread trip goes to tailors whose buy list was never read, Magincia's first ("magincia is great for that").
- A vendor is home when the smallest region holding it, z included, is its shop's (the 1998 `human.m`, `myHomeShop`
  against `getSmallestArea`); anywhere else it says "Let us go back to my shop" and walks or teleports to its job spot.
  Cybil's job spot is the Magincia mage shop's roof (z 60), above `MAGEGUILD_MAGINCIA`'s z 20..59, so she never trades
  there (2026-10-17 02:56). The assist learns each vendor's home region from a positive answer and its away regions
  from "go back to my shop" (`vendor-homes-<server>.txt`), asks no vendor where it is known to be away, and passes it
  over (the survey goes on) when it stays away 60 s.
- Bankers carry no "the banker" title (Britain: Blythe, Francis, Elwood, Aida). Nujel'm's bank sign is at 3768,1313;
  a deposit from 3761,1306 was heard.
- Busy: a double-click or lift leaves the player busy about 1 s; a lift or use inside it gets "You must wait to perform
  another action."

## The pieces (all in `apps/uoaix/assist/`)

- `GameLink.cs`: injectable steps, speech, buy and sell answers; the relay numbers every step (client and assist) in one
  count and gives the client its own numbers back; assist step answers are kept from the client, and the player is
  redrawn (0x20) after each assist step (`ShowMode`, `walk-show`).
- `AssistFeatures.cs`:
  - `VendorBuy`, `VendorSell` (by name, shop windows held from the client, 5 per sale, one ask; after "Let us go back to my shop" or "thou must give it to me" (the vendor away from its post) its walk is watched on the server's mobile updates until it stands still 5 s, or 1 s after a jump of more than 3 tiles (a teleport home), then it is followed and asked again, up to 3 asks while it keeps giving that answer; each positive answer widens the vendor's home box (`vendor-homes-<server>.txt`), and a vendor with a known home sends the player there at once on its "let us go" (Damian, 2026-10-17: "about 1 second ticks on walks, then a 5 second stop is usually an indication they are home"; "sometimes they teleport when you ask"); or 2.5 s later after "hand it over" / "hand it to me" (Damian, 2026-10-17: "the hand it to me line is misleading"); NPC replies classified, an unknown one kicked up by `replies`;
    `NearVendor` walks to the named vendor first, up to 3 legs as the vendor comes into view);
  - `Recall` (the rune in sight, reagents, mana, fizzle retries); `Peek` (a bag read without a window); `RuneLabels`;
  - `WalkTo` with `PlanPath` (cheapest-first over walk states, a tile and the floor stood on there, so stacked floors
    are apart; a walk can be asked to end on a floor (`tz`); where no walk reaches, one Teleport (`TeleportHop`, spell
    22): the cast tile among those walked to, the landing among those the target is walked to from (steps whose reverse
    is open too, as a drop off a roof is not a climb), within 11 tiles, aimed at the landing's surface static as the
    client aims (the roof tile 0x0569 at z 60); a cast the server refuses is kept in `teleport-refused-<server>.txt` and
    never cast again; over statics, ground items, impassable land and characters, each tile at
    its own standing height; a diagonal only past two open sides; followed, replanned after a refusal, a character on
    the next tile or 8 steps; doors opened on a refusal; with no way round people and full stamina, through them, as
    ServUO allows a shove only at full stamina); `WalkToSpeakingRange` (within 2, in sight where a tile is: nothing at
    eye height and no door on the line; else within 2, since a vendor hears by range alone); `AskNear` (an unanswered
    shop asked again only when a re-measure shows a change since the ask: another vendor's window came instead, the
    vendor was out of hearing when asked, or it has moved; then the walk closes in first. Nothing changed, the silence is
    final);
  - `SewAll` (no round zero; the pack's bags opened first, books not, the next action after the busy time; bolts by
    their arts 0x0F95..0x0F9C, not by name, which also names the Energy Bolt scroll; stops one short of 125 items or on "cannot hold more
    items"; cloth piles first, then bolts; scraps dropped on the last tile a drop held, else a free tile);
  - `ToBanker`, every bank's approach (Damian, 2026-10-17: "find a spot in range of the banker but not go inside ... a
    human usually runs up right the edge of the building where their travel lands and they say bank", "move about 5-8
    tiles before you try again"): "bank" once from each new place, the box kept for the deposit, take or put that
    follows (`SayBank`): first the nearest free tile outside the bank's region (a tile of it beside, no one within a
    tile); then a free outside tile 5 to 8 tiles on, toward the door when one is in view; then a free outside tile
    within 8 of the banker and 5 from every try; then beside the banker inside;
  - `BankDeposit` (piles under 60,000);
    `BankTake` (from the box and the bags in it, onto the pack's stack of the same art or into the reagent bag, stopping
    on the first drop that does not land); `PackMerge` (each art's stacks joined onto the biggest);
  - `RunCircuit` (`CircuitGo`, `CircuitHalt`): buy from each thread vendor whose restock is due, weave at the nearest
    loom (remembered once seen), sew, sell at 27; shirts no tailor here takes go on a `SellTrip` (a city by its tailor
    not offering under the floor for 45 minutes, thread there first: sell, buy thread, bank, a `ThreadTrip` to the next
    city when none was found, bank there above 2000 gold); with nothing to do and no thread, a
    `ThreadTrip`; else wait for the next restock. `Travel` recalls from the nearest bank by the box's rune for the
    city (`BankRecall`; labels read in place and cached in `bank-runes-<server>.txt`). `CircuitWatch` stops it on staff, attack or death;
  - the circuit record (`circuit-<server>.txt`: each vendor's stock, buy-out time, restocks).
- The buy circuit, "Buy reagents" (`RunBuy`, `circuit buy`): the shopping list (`circuit.buy.list`, `buy-list`,
  `buy-item <arts> <count> <highest gp> <name>`; default 250 black pearl at 5 gp) bought at the picked shops in their
  order, lap after lap while a lap buys anything; at each city's bank (`BuyBank`) the goods go into the box's container
  already holding the most of them, 30 of each recall reagent stay in the pack, and the purse (`buy-purse`, 2000) is
  topped up from or banked onto the box's own gold pile; at the bank where the circuit ends everything bought goes in,
  none kept back, and the purse drops to 300. Gold is never taken from a bag in the box (Damian, 2026-10-17:
  "i have stashed 30k gold in the bank into a sub-bag so nothing happens accidentally to it"). Default shops: The
  Circles of Magic in Vesper and The Sorcerer's Delight in Britain. Open: returning to a bought-out mage shop when its
  restock is due ("someday the reagent buy circuit will need to follow the same "get more next respawn" logic").
- The survey, "Survey town" (`RunSurvey`, `circuit survey [town]`, `survey-town <town|here>`): every shop of the town
  (all trades, `UoRegions.AllShopsIn`; the ticked ones when any of the town's are ticked) nearest first, each keeper
  named by a click asked "buy" once by name so its stock and prices reach the atlas, nothing bought; a recall there by
  the bank's rune when elsewhere; the walk ends at the town's bank with nothing said. Report in
  `reports\survey-<server>-<time>.txt` (Damian, 2026-10-17: "no transactions, just data collection").
- The refresh, "Refresh" (`RunRefresh`, `circuit refresh`; Damian, 2026-10-17: "refresh loadout and feed horse and self,
  dump gold in bank when done ... never bails of hay or wheat ... get full basically"): in the town it starts in, the
  ticked food shops (provisioners, then farms and markets, then stables, each nearest first) give the mount 10 apples or
  pears and the player the stocked food of the most satiety (picked by art, the 1998 satiety table in `Satiety`), enough
  for 25; the player eats until "too full" or "stuffed" (eat.m), the mount is dismounted, handed the fruit ("Your pet
  looks happier.") and mounted again; at the bank the gold over the 300 purse goes in and the reagent loadout is refilled
  from its storage bag (the bags of the last loadout picked by hand, `loadout.bag` and `loadout.storage`).
- Each circuit's shops are picked by shop name (`circuit.shops.tailor`, `circuit.shops.buy`, `circuit-shops`); with none
  picked the tailor circuit uses every tailor shop.
- `AssistWindow.cs`: the Circuit tab (setup: the circuit, its lists, its shops ticked and ordered, its launch modes and
  Stop, each with a tip; then job, vendors, totals); the trades file reloads at start.
- `Assist.cs` control port (relay port + 1): `say`, `vendor-buy <arts> <most> [name]`, `vendor-sell <arts> <floor>
  [name]`, `recall <rune>`, `peek <bag>`, `ground [reach]`, `walk <x> <y> [reach]`, `walk-to <name> [reach]`,
  `walk-show 20|77|0`, `work`, `sew`, `bank-deposit [keep]`, `bank-take <arts> <count>`, `pack-merge <arts>`,
  `runes [reach]`, `items`, `stop`, `circuit go|stop|status`, `action <name>`, and the rest in `AssistRelay.Command`
  (`pace`, `flee`, `travel`, `names`, `labels`, `contents`, `put`, `move`, `bank-put`, `replies`).

## Proven live, and not

| piece | state |
|---|---|
| vendor buy by name, buy-out in one purchase | proven (Jannelle, Michelle, Tracy; 2 sewing kits from Michelle) |
| Recall with fizzle retries | proven (Britain bank rune; library runes to Skara Brae and Nujel'm) |
| weaving a 103-thread stack, bolts stacked | proven |
| walking with height from the map, bank to east tailor (75 steps) and across Britain | proven |
| running at 200 ms a step | proven (38 steps in 7.0 s, none refused) |
| vendor sell 5 at a time | proven (100 shirts to Jannelle at 27, 2700 gold) |
| sewing with no round zero (`sew`) | proven (98 made, 36 failed; scraps on the floor) |
| bank deposit (`bank-deposit`) | proven (Britain onto an existing pile; Nujel'm) |
| `vendor-sell` and `vendor-buy` walking to the vendor | proven (Michelle, Lynn, Jannelle; kits and 9 shirts at Nujel'm after the ask again by where she is seen) |
| `circuit go` buying thread | proven (Jannelle's 10 for 30); weaving 10 thread starts a bolt, more is needed |
| `pack-merge` | proven (7 stacks joined) |
| `runes` | proven (29 labels at the library) |
| `bank-take` with its stop on a refused drop, stacking onto the bag; sight at speaking range; the redraw in the answer's place; the cast wait that ignores others' speech; `sew` opening the pack's bags; the scrap tile kept | compiled, not run |
| the circuit's sell trip (`SellTrip`): Britain's tailors at 24, the library to Vesper, 28 sold to Morena for 756, her thread bought, home by the pack rune | proven |
| door opening | proven (two wooden doors in Britain, one in Vesper) |
| the Moonglow sell trip: 69 sold to Brian for 1863 | proven |
| banking in the selling city | NOT proven: Vesper's bank had no way from 2957,643 (its canals); Moonglow's did not answer "bank" from up to 8 tiles off its region's centre, and did answer Damian from 4474,1154 (2026-10-17); Magincia's had "no way from 3670,2234" and Damian walked it from Stitchin' Time to 3730,2160 and was answered. The gold is banked first, before any thread: the city's nearest BANK_ region walked to within 3 of its centre, "bank" said once; a bank that does not answer, or that the walk cannot reach, is not tried again for 15 minutes; a bank that answered before is tried first (`banks-heard-<server>.txt`; Nujel'm's sign at 3768,1313 went unheard twice and 3762,1316 answered); a deposit that fails leaves the gold carried and the circuit goes on, and a recall's "bank" unanswered ends the trip (L-FUTILITY). A city's signs stand in for banks only where the region file has none (Nujel'm). Compiled, not run |
| the mounted run, 2 steps in flight | proven (66 tiles, median 109 ms a step) |
| the nearest loom known (every loom in view noted on the walk; weave where the thread was bought, no trip home for a loom); the thread trip (`ThreadTrip`); the straight-line route | compiled, not run |
| the circuit's staff/attack/death stop | NOT RUN |
| `bank-take` from the bank's bag into the reagent bag's stacks | proven (4 pearl and 20 mandrake root) |
| a trip's reagent check (4 of each, else 30 from the bank), a failed recall ending the trip, the library's labels kept | compiled, not run |
| second sewing kit inside the circuit | not built |
| recall from a rune in the open bank box (`recall <serial>` after "bank") | proven (Moonglow, Vesper, Serpent's Hold, Skara Brae runes, 2026-10-17) |
| `travel <city>` (`BankRecall`) | NOT proven: walks to within 8 of the city's BANK_ region centre and says "bank" there (a sign stands outside the wall: Vesper's outside The Mint went unheard); a trip goes on only when the recall lands in the city named. Compiled, not run |
| the 90-shirt sale to Brian 5 at a time | proven (90 for 2430, 2026-10-17); `Say` now refuses only an unanswered repeat, so each answered "Brian sell" goes again. Compiled and the guard driven in a harness, not run live |
| a silent vendor re-measured and asked again only on a change (the survey's Francis at Ethereal Goods, 19:15:51, and Slade at 02:12:40 each ended a survey) | proven once (Edmund, Magincia, teleported home and answered) |
| a named ask from a tile nearer the vendor named than any other vendor in view (`NearVendor`, `NearerSpots`): Edmund answered "Cybil buy" three times from nearer than Cybil (Magincia, 02:37) | compiled, not run |
| a vendor hears only on the player's floor (within 16 z, `Hears`), and the walk to it ends on its floor (`WalkTo` tz): Edmund on the floor below, 1 and 2 tiles off, answered none of three asks (Magincia, 02:55). Cybil spawns on the roof of the mage shop (z 60), which no known walk reaches | compiled, not run |
| Map tab: "Teleport-only spots" (magenta marks: standing spots within 11 tiles no walk from the player reaches inside the plan box) | offline from the Magincia mage shop: from its second floor 294 spots, all roof (z 60, 61); from the street 360, the roof plus a walled building south of the street (3696..3710, 2240..2247) the planner finds no way into, 21 to 51 ms. Proven in the window (Damian, 2026-10-17: "the pink dots work great") |
| the refresh circuit | compiled, not run |
| the bank approach (`ToBanker`: edge, 5 to 8 on toward the door, the banker's hearing outside, then inside) | compiled, not run |
| walk states per floor and the Teleport hop | planned offline on the client map: street to street 7 steps; street to the mage shop's second floor (z 40) 48 steps by stairs outside the shop; street to the roof (z 60) no walk, one Teleport from 3707,2236 to 3704,2225 (11 tiles, roof art 0x055E) in 36 ms; reachable targets give no hop. Not cast live |
| a sell trip's order and asks | Magincia, then Moonglow, then by score, among cities the bank box has a rune for; a shop with no tailor known by name is walked into, its people named by a click and its shopkeeper asked by name, never "vendor sell". Compiled, not run |

## Next

1. Not yet seen live: the vendor-home watch (5 s still, teleport jump, up to 3 asks, the home
   box), the 2.5 s gap between shop asks (Bertram answered "Slade buy" said the instant his window came, 02:12:40), the
   buy circuit's final bank stop (all goods in, purse 300), the bank tried first that answered before.
2. Skara Brae's Lynn offered 23 and every other tailor asked offered 23-24 (2026-10-17); Moonglow's Brian paid 27 for 90.
3. Serpent's Hold and Buccaneer's Den have tailors but no shop region in the 1998 file: find the tailor by the mobiles
   in view. Vesper has two tailors, both "The Spinning Wheel": Morena's at 2952,616 and a second at 2832,880
   (CLOTHIER_VESPER_2).
4. Every hop goes from the nearest bank by the bank box's rune for the city (`BankRecall`, control port `travel
   <city>`; Damian, 2026-10-17: "all the destinations we need are right there in any bank"); no hop goes through
   the moat library, and runes never leave the bank.
5. Open faults: Slade did not answer from 3 tiles behind his closed door (now sight is required; not run); the walk to
   Jannelle had "no way" from her shop door; Vesper's bank had no way (canals). Jhelom is islands joined by
   teleporters (Damian, 2026-10-17): the bank and one Adventurer's Needle (1352,3776) are on the north island, the other
   (1448,4000) on the south island; teleporters are server-side items the client is never sent; the assist learns one
   from any step (the player's or its own) the server answers by drawing the player more than 2 tiles off with no spell
   cursor in 10 s, keeps it in `teleporters-<server>.txt` and routes through it, so the south island is reachable once
   someone has crossed with the relay up (planned offline: north Needle to Kimmie, no walk without it, 12 steps with it); the walk to Kimmie
   on the south island is tried twice a visit (selling, then thread), nothing said. The walk planner found "no way"
   from Stitchin' Time to Magincia's bank and inside Moonglow's bank to its banker; Damian walked the first.
6. Thread restocks: Stanley restocked inside 9 minutes, Ella and Jannelle had not after 18; the 15-minute restock
   (`RestockMinutes`) sent two dry trips. Two shop doors were opened 4 times in 3 s (Jannelle's, 3765,1255).
7. CLIENT.EXE crashed (access violation, 01:37:55 during the Vesper bank step of the buy circuit, and 05:17:09 one second
   after the server refused an assist pickup lift that the relay passed on). A refusal of an assist lift now stays in the
   relay (`GameLink.AssistLift`); 2 more refusals that reached the client did not crash it, so the cause is not proven.
   Kathleen (The Sorcerer's Delight) answered "go back to my shop" twice from inside her shop, not moving (a vendor's
   home is its shop region with z, `VendorAtHome`; her region and z were not read).
9. The planner steps a diagonal only past two open sides (ServUO's rule); uo1998 let Damian walk diagonally into a hollow
   past two blocked sides (2659,459). Loosen it for real walks only on refused or accepted steps seen in a log.
10. "thou must give it to me and I'll decide if I need it in my stock" is one of the 1998 shopkeeper convo's random sell
   lines (`rundir/convo/shopkeep.frg`), not a going-home answer; it still reads as GoingHome (Simon, Trinsic) until Damian
   says otherwise.
8. Pearl back stock: 250 wanted at 5 gp; 57 bought 2026-10-17, the bank bag 42C5AF80 held 71 and the pack 30.

Drive it from PowerShell with a TCP client on 127.0.0.1:2651, one command a line and one answer line each.
