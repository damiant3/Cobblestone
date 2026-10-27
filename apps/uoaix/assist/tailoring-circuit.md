# The circuits (UOAIX Assist)

The Assist's circuits on uo1998.com, the tailoring circuit first: an unattended loop that buys shuttles of thread,
weaves bolts, sews fancy shirts, sells them to tailors and banks the gold, all inside guarded towns; then Keep thread,
the buy, mining, smithing and the other circuits. Owner: blu. User: Damian. This document is their design: what was
decided and why, what the wire and the 1998 server's source show, and what each piece does. What is live and not yet
seen working is the blu row of `docs/PM/CurrentPlan.md`.

## Decisions (Damian, from 2026-10-17)

| decision | his words |
|---|---|
| Build it; he owns the account risk. uo1998 publishes no macro policy and has no support. | "as long as they don't tell me to stop, I think this is almost expected at this point lol. yeah lets build it out" |
| A simple, safe loop of tailoring only, in towns. | "i want a simple safe loop of just tailoring in all safe spots in town" |
| No round zero: the circuit picks its own kit, cloth and menus. | "no i need you to automate all this", "no round 0 here" |
| Packets, not emulated clicks; every window or cursor the assist causes stays off the client. | "lets just inject the right messages instead of user emulation of clicks unless absolutely necessary" |
| Walking is injected (the plan's rule 6); the client need not animate it. | "we don't need to make it animate if the server is responding the correct way" |
| Route around characters and players' piles; open doors. | "if you do walk through anothe char, you will take a stam penatly", "look for blocking items too ... like big ore piles, boxes", "tracy was behind a door, you have to learn to open those" |
| Address a vendor by name. | "its better that way, since otherwise other npc can intercept" |
| Buy a vendor's thread out in one purchase; a vendor bought out comes back with more at its next restock ("How a 1998 vendor restocks", below). | "maximizing the count in a single transaction is best", "if they have sold out of thread, the next respawn gets more" |
| A bought-out thread vendor is due at its own restock, learned by probing ("How a 1998 vendor restocks", below); every circuit asks `ThreadDueAt`. Nothing waits for one vendor: the round goes on to the others, and the circuit waits only when every vendor refuses to buy and sell and the house has no work; never with shirts to sell. The buy circuit still passes a bought-out shop over for `RestockMinutes` (17). | "if and when we gleen the exact timings of restocks at each vendor, then we can backoff till then. no arbitrary backoffs" (2026-10-21); "no reason to wait for others ... the whole circuit should only wait if all vendors refuse to buy anymore, sell anymore, and there is no work to be done at the Astronomical Society"; "selling shirts is always more important than waiting for anything, especially thread" (2026-10-24) |
| Sell what the sell list holds, each at its own lowest price, fancy shirts at 23 (setting `circuit.sell`; control port `sell-list`, `sell-item <arts> <gp> <name>`; what is sewn, `circuit.item.menu`, `make-menu`), 5 items a sale; sew up to a multiple of 5. | "lets make this price and even the item a configurable so no code change", "take a list of items and min prices", "sell as low as 23", "you can only sell 5 items at a time", "if you have like 14 shirts to sell, its better to make 1 more for the even 5" |
| Carry 4 sewing kits (`KitsWant`, the tailoring toolbag's road count). | "increase the sewing kit buys to 4 kits" (2026-10-19) |
| Travel by Recall from the carried shop runes bag, else the bank box's runes at any bank; only the shop runes bags leave the bank (2026-10-18); a recall is cast again only after a fizzle; 30 of each recall reagent, refilled at any bank ("30 of each recall reg, not 20"). | "all the destinations we need are right there in any bank", "never take runes out of the bank", "it sometimes fizzles but it works. be persistent", "wherever you go, there is a bank" |
| Never repeat anything that got no proper answer (L-FUTILITY, the plan's rule 1). | "if it didn't get a proper response the first time, DO NOT EVER REPEAT IT OVER AND OVER" |
| A trip by the bank box's runes banks the gold over the 300 purse at that bank first (`Travel`); a walk to a bank only for the deposit only above `BankOver` (5000 unset; `RunShops` banks as its visit step 2 says). From the bank wall; never overflow a gold pile (60,000), prefer an existing pile. A bank that cannot be reached is passed over by `FailSpan` (below) and the gold carried; "bank" said and unanswered ends the trip. | "walk to the bank wall, say bank", "deposit the money carefully not to overflow the pile of gold there, but to use it if possible", "make the banking interupt threshold 5k" (2026-10-19) |
| Sell to the tailors holding 27 first, each until it refuses, then at 23 anywhere; every hop pays for itself (a recall costs 15 gp of reagents). | Damian's policy, 2026-10-20 (it replaced "next time, try magincia first" of 2026-10-17) |
| Every pause has a reset condition, never a timer alone: a refused top tailor rests until every other tailor the runes reach has been asked since (`Resting`); a tailor offering under the floor or buying no more is passed over until the survey cycle ends (`EndCycle`). A failure (a walk, a bank, a vendor out of reach) passes its target over for this pass, a second pauses it `FailPauseMinutes` (5), a third marks it BROKEN until `broken clear` (Damian, 2026-10-20: "the first time ... make a alternate plan ... a second time, add the timer"). The timer still alone is Next, 0. | "there always must be a reset condition for all pauses" (2026-10-20) |
| Stop only for staff, attack or death; ignore players. Staff: a "GM " name, a game master/counselor/seer/admin title, or the GM robe. Game master, not grandmaster. | "it should just ignore anyone who isn't clearly a GM", "players are annoying sometimes", "the custom was to name the char GM <whatever>" |
| Think two steps out: before leaving a place, do there what the step after next would come back for. The shop's work (sell, thread, weave, sew) before the bank; the gold banked at the bank a recall goes from, or before a restock wait; a sell trip buys thread in the shop before it banks. | "am I in the place i will come back to after the very next step, and maybe i could just kill 2 birds with one stone, and one less round trip", "it is mindblowingly stupid to run back to the bank to deposit gold, only to run back to tailorshop to buy 10 thread when you started in the tailor shop" |
| Shirts and thread are decided apart, in one order: sell the shirts in the pack, then sew the cloth, then weave the thread bought, then buy thread. A tailor's shirt hold runs from her refusal to her next restock, never moved by a thread visit; homespun goes to a shop for thread only with no shirts carried. | "we need seperate the logic of shirts and threads, obviously. buying thread is secondary to selling shirts in pack, secondary to making shirts with cloth, secondary to making cloth from already purchased thread" (2026-10-25) |
| The house's town (the CITY_ region holding the house, else the city a known teleporter within 50 tiles of the house lands in: Moonglow, by the pad at 4663,1134; the house's own guard zone, "telescope in Moonglow", is no town) is walked to from home, its shop first when it has work and then the order where it left off, and its bank for a bank stop from home; within it every shop and bank is walked to, and home is walked to from it; a rune only when the walk fails. | "brian can be reached by foot from the house in about the same time it takes to wait for that mana ... you could save mana and let it regenerate while you run" (2026-10-25); "the walk to the bank and tailor shop in moonglow is always worth it either to let mana regen or to prevent spending it ... run home from brian, if brian is planned to be last on the current outing" (2026-10-26) |
| Every bank stop banks the gold over the purse, whatever it was made for; bank-over decides only whether the gold alone makes a trip. Every bank stop that deposits gold tops the recall reagents off to the loadout. | "when at the bank for reagents, you should bank any gold in excess of the 200 pocket change. and if you are at the bank to deposit gold, you should top off your recall reagents" (2026-10-25); "do all your business pending at the bank... we don't need to cart the gold around, the threshold purpose is prevent too much going to the bank" (2026-10-26) |
| The rest before the top ends when the soonest held tailor comes due, if sooner than its two minutes (10 s at least). | "better than sitting in the hut waiting for nothing" (2026-10-25) |
| A held tailor is checked at her restock's best guess only where the player already is (her town); a trip to her waits until she has surely restocked (`KeepRules.SureAfter`: the window's last phase, a whole period with nothing known). A walk from home into the house's town is never traded for a recall. | "not go out of our way for the check. if we are already there, its nearly free. if we have to travel, it costs at least 15gp of pain" (2026-10-25) |
| Scrap cloth goes on the floor, not into bandages. | "take the bit about making bandages out. just move the scrap pile the floor somewere" |
| Homespun's bag goes down the sell order (`circuit.sell.order`, control port `sell-order <names>`, tailors by first name): one tailor a price region first, the smallest regions first, then each region's next; each bag to the first tailor of it that pays the floor, is not passed over and is here or surely restocked, sold down until she quits. Empty: the shop order. | "sell down 1 vendor per region to start. so brian first, hes the one we want to respawn the fastest. then slade, hes the closest to a bank and at the next smallest region. then take your pick for trinsic ... then for vesper ... then the tracy->secondary in brit, then trinsic region secondary, then vesper region secondary, then brit tertiary, then on until you've sold shirts to all the vendors" (2026-10-26) |
| Under a sell order a tailor sold down rests `sell-rest` restocks (2) surely past (`SureAfter`, then whole periods). | "if you sell them down hard enough they don't respawn with great demand"; "2 restocks might be the correct answer to brian and a test circuit with that logic will confirm or deny the assertion" (2026-10-26) |
| Homespun asks every thread vendor of a shop it stands in, due or not, once: bought out or "has none", she is not asked for thread for a restock, her shirts untouched. | "thread should be bought always at every stop for sure, or checked for to at least confirm the zero when there, its basically free sanity check" (2026-10-26) |
| Under a sell order the reagent warning makes no trip while the reagents (the least of the three) cover the recalls to the plan's next shop a walk from a bank, two a stop and one spare; that shop, left, refills them when they would not reach the one after. | "there is no need to detour for regs warning at 8 when in the next 2 normal bounces we will be near a bank ... if it fits normal cycle don't make special trip" (2026-10-26) |
| An outing that ends outside the house's town goes home by it when it has a reason (short of mana for two recalls, shirts its tailor may take, its thread due, gold over bank-over, the recall reagents low): its tailor's shop (sell, thread, kits), its mage shops for the recall reagents (up to `RegsBuyUp`, 200, each), its bank (the reagents stored, 30 kept, the gold over the purse in), then home on foot. Its tailor is then rested and not first next outing. | "You bounce in to brian at the end, sell 5 or 10 shirts, pickup any thread that spawned, hit the bank and bank gold reload reagents, then run home"; "not every time you return home is brian, mostly not, but once in a while it makes sense, you are low on mana, you have multiple things to do, or brian has thread, or you have shirts that he might take"; "the stop at the magic store on the way home too, the alchemy shop just south of the tailor shop. buy up the recall regs there. then hit the bank, stock the recall reg storage, then home with the pile of thread through the teleporter" (2026-10-26) |
| Under a sell order, home with no tailor of it ready weaves the pile into bolts and sews nothing; with nothing to weave, it sews as ever. | "if the vendors are really beaten down this might be a good time for making bolts before shirts" (2026-10-26) |
| A town's tailors are one a first name, the one seen last: a sale is asked by name, so a stale atlas entry under another serial would be a second tailor with no rest of her own. | (the sell order's world case, 2026-10-26) |

## What the wire showed on uo1998

- uo1998 is based on the 1998 UoDemo server, whose C translation is OUO (`D:\Projects\uo-reference\ouo`, its scripts
  in `D:\Projects\uo-reference\rundir`; `uoaix-init.md`): read OUO first for a rule. Its bank line ("Bank container has
  N items, M stones") and full-container line match ServUO's word for word, so ServUO
  (`D:\Projects\uo-reference\ServUO`) is the cross-check where OUO says nothing, and each rule below names its source.
- Speech (0x03): type 0, the player's hue and font, the text with its NUL. A vendor hears its name from 3 tiles, by
  range alone (ServUO `BaseVendor.cs:2497`); a banker hears "bank" from 12 (`Banker.cs:299`).
- The pack holds 125 items counting every bag's contents, not worn items (`Container.CheckHold`: TotalItems + 1 > 125,
  "That container cannot hold more items."). A drop that joins a stack is not counted, so reagents merged into the
  reagent bag's stacks cost no item.
- Buy: "<name> buy" opens 0x24 (gump 0x30), the stock 0x3C and the list 0x74. **The 0x74 list is in the reverse order of
  the 0x3C stock**. The answer is 0x3B: vendor, flag 2, then layer 0x1A, serial, amount. The vendor's line "The total
  of thy purchase is N gold." is what was charged, which can be over the listed price (4 gp against 3 from a depleted
  stock).
- Sell: "<name> sell" opens 0x9E (serial, graphic, hue, amount, price, name per item); the answer is 0x9F (vendor,
  count, serial and amount per item), at most 5 items. The offer is "How a 1998 vendor prices what it buys" (below). A
  vendor out of sight is where the atlas last saw it, so the walk to it is taken again from where it ends.
- Recall (spell 32): cast 0x12 type 0x56 "32"; the cursor comes after the cast time (longer than 3 s), so a cast claims
  its cursor 8 s; the player's spell words precede it; "The spell fizzles." is retried.
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
  Ocllo, Britain's Lord's Clothiers. A rune marked in a shop region is labelled for the shop. A shop rune whose landing is
  blocked (most often its tailor standing there) is reported in red ("RUNE BLOCKED", `blocked-runes-<server>.txt`) for
  a better mark, and the trip goes by the city's rune from the bank box and walks in. Runes Damian keeps for himself are
  never picked by a circuit (`<server>runes.reserved`, hex serials by commas; the Moonglow bank roof rune 4424FD7B,
  "no walk to anything"). A rune is renamed by its
  double-click's text prompt (`rename <rune> <text>`). Mark is cast from scrolls (Kwik's Magery fizzles the spell
  otherwise); a Mark takes 20 mana and a Recall 11, of Kwik's 47, about 1 back every 5 s.
- Sewing: the kit's double-click asks "What cloth shall I use this sewing kit on?"; menus "Choose a category." (shirts)
  and "What kind of shirt?" ("Fancy shirt - takes 8 cloth."); a pick is 0x7D: dialog serial, menu id, index, model, hue.
  "You create the fancy shirt ..." or "Tailoring failed. Some of the cloth is ruined." A bolt cuts to 50 cloth
  (Damian, 2026-10-21: "28 bolts is 1400 cloth"), so a bolt is 6 shirts and 2 cloth over.
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
  The route is the fewest steps, and of those the fewest diagonals.
- The circuit by shops (Damian, 2026-10-20, `RunShops`): the tailor shops in one fixed order, Moonglow, Magincia,
  Trinsic, Skara Brae, Ocllo, Nujel'm, Jhelom south then north, Vesper north then south, Britain east (The Right Fit)
  then west (The Lord's Clothiers), and round again; it starts at the shop the player stands in, else this town's first
  of the order, else where it last left off (`<server>circuit.shop.next`). Each visit, in order:
  1. Too heavy to walk (past strength * 4 + 30): thread, then bolts, then cloth, largest pile first, put down under the
     player's feet; gold is never put down, and with nothing else left to put down the circuit stops.
  2. The bank only when critical (something was put down and the pack holds gold over "Bank gold over", or the pack
     holds no kits and nothing to sew and the purse is short), in whatever town the player is in; else the gold waits
     for Jhelom north or Britain west, banks a walk from their shops, after their shop's work. At every bank stop the
     reagent loadout is refilled, and only when the pack holds no kits and nothing to sew is gold taken, from the box's
     top-level piles only: what the town's due thread vendors are expected to sell (`ThreadScore`, or 20) at
     3 gold a thread and a kit a 22 thread at 3 gold, 400 at most.
  3. What was put down is lifted back, split where the whole pile would make the pack too heavy again.
  4. A shop with nothing to do is passed over with no recall (its known tailors all passed over this cycle or under the
     floor, no thread due there, nothing to weave or sew with room in the pack); a shop with no tailor known yet is
     visited. A tailor that left shirts unbought (full, or under the floor) is passed over until her next restock by
     her strike window (`RestockAfter`, `KeepRules.StrikeAfter`), and her offer stays fresh until then: an item sold
     to a vendor goes at its restock (OUO), so she buys again then. The recall checks the reagents only: with shirts in the pack the purse is never topped up for a trip
     (Damian, 2026-10-20: "we have the rules with shirts in bag, we don't need seed captial").
  5. To the shop: a walk within the town (by rune when the walk fails, as between Jhelom's islands), else the carried
     rune whose recorded landing lies in the shop, else one labelled for it, else `Travel`.
  6. The shop's work, round after round while one does something: the shirts sold to its tailors that pay the floor,
     its thread bought, kits bought when none is left, the thread woven at its loom, the bolts and cloth sewn.
  7. On to the next shop. A whole round of the order that sold, sewed and bought nothing (shops passed over count)
     ends the circuit; there is no wait for a restock.
- The phases (Damian, 2026-10-20: "march through the tailors one at a time trying to sell the pile of shirts ... go back
  to the node where we first had to skip thread and buy it ... until full of thread"; "in the sell phase we reset all that
  stuff and sell to the first offer everything we can"; "dump means put on floor ... when it means the vendors are offering
  more, it means get it to the house"):
  - Sell, while the pack is full of shirts: every pause cleared, every tailor in order offered the shirts at the floor
    whatever it offered before, no thread bought; the first shop with thread for sale is kept. A lap with no sale is
    stuck (above).
  - Restock, once the shirts are sold: the thread held stored at the house pile (the gold banked on the way, the purse
    topped up to "Keep in pack" from the box's top-level gold), else the gold banked; then thread bought in order from
    the kept shop; a pack full of thread goes to the house and the buying goes on; a tailor there that buys shirts
    reached with thread in the pack starts Make; a lap that buys nothing starts Make.
  - Make: the visits above (sell, buy, weave, sew); a pack full of shirts starts Sell. A round with nothing to do in
    any shop is the rest ("rest means we have time to do other things, like get your reagent bag loaded out again,
    deposit the gold, prep for respawn"): the thread held stored at the house by way of a bank stop, else a bank stop in
    the town; the gold deposited, the loadout refilled, the purse topped up; then the circuit ends "resting".
- A bank stop more than 20 tiles' walk from the town's nearest bank (the overflow trip, a critical bank, the rest) goes
  by the carried rune whose recorded landing lies nearest a known bank, within 15 tiles (Damian, 2026-10-20: "it should
  have recalled to a bank instead of the long march in trinsic"); any town's bank serves. The recall goes only when the
  town has no bank or it saves more than `RecallTiles` (100) of walking (Damian, 2026-10-26: "the choice was bad", a
  recall out of Magincia to save 48 of 83 tiles).
- The thread overflow (Damian, 2026-10-20: the pile on his house floor, "the house rune should only be in the bank box,
  and never taken out"; "the thread is vulnerable to theft. it is cheap to replace"): set once in the tailoring setup,
  "Set house rune" (target it in the open bank box) and "Set pile" (target the pile or its tile); only thread on that one
  tile is taken; a pile absent from its tile is started again there by the next store (Damian, 2026-10-24: "if the
  thread pile is absent, start a new one"). No recall goes anywhere without a task there (Damian, 2026-10-24: "these
  circuits must NOT recall somewhere without a task to perform"): a house rune carried in the pack recalls home from
  where the player stands; a bank comes first only when the house rune is in the box, the gold is over bank-over, the
  recall reagents are low or the purse needs topping up. A homespun Go with shirts in the pack sells them first from
  the shop where the player stands, never home first. From a bank: the gold over the purse deposited and the loadout
  refilled first, as at every bank stop, then the rune recalled where it lies.
  - Stuck (a whole round with nothing sold, sewn or bought, and thread held): every thread stack put on the pile, and
    the circuit ends; "THREAD OVERFLOW" in the log and in red in the client only with shirts carried (no shirts, and the
    thread is only waiting for home).
  - The vendor ramp-up (Damian, 2026-10-20: "after checking all 12 and not getting much, you should just sew what you
    got, then go fetch thread from the storage to keep working while the restock timer runs"): a round of all the shops
    (passed-over ones counted) that bought fewer than "Overflow below" thread (120, a fresh shard's 12 shops), with
    nothing left to sew: up to "Overflow take" (200) thread lifted from the pile, as far as the carry limit allows,
    before a round with nothing done is the rest; an empty pile triggers nothing. The vendors' surplus
    refills the pile over later rounds (stuck, Restock).
- Sell only keeps the older order: bank the gold, sell the shirts here, then a sell trip (Damian, 2026-10-17). The gold
  is banked in the city Kwik is in, its banks nearest first; a bank that cannot be reached is passed over by `FailSpan`.
  A tailor that offered under the floor or bought no more is passed over until the survey cycle ends, a thread vendor
  sold out for `RestockMinutes` (17); both are kept on disk (underfloor-, circuit-<server>.txt). A sell trip asks every
  tailor in the shop that pays the floor, each by name. A refusal with "too fatigued to move" rests `fatigue-rest` (10
  s), doubled for each one more in the same walk, 60 s at most, and walks on.
- A sell trip's cities (`SellTrip`): every city with a tailor shop open, a rune in the bank box and not Wind; a city whose
  every buyer offered under the floor within `SurveyFreshMinutes` (30) is not travelled to. Best first by the expected
  price (`CityPrice`), then Magincia and Moonglow (`SellFirst`; "next time, try magincia first"), then by the hours since
  a sale at its best shop, Magincia, Nujel'm and Jhelom weighed 3 times ("great for solo", `QuietCities`); after a survey,
  the city of the best fresh offer first. Britain is one city among the rest (Damian, 2026-10-17: "yes it should visit
  brit, but it should also not consider it the primary central location").
- A trip never goes to Wind (Damian, 2026-10-17: "do not go to wind").
- Guard rules differ from city to city (Damian, 2026-10-17: "the criminal justice rules are not consistent from city to
  city"): killing a cat is a crime in Skara Brae ("killing cats in skara brea is not allowed, but it is allowed almost
  everywhere else") and in Moonglow (Damian's test, 2026-10-24 14:41:51: a Magic Arrow at a cat by the bank, "Guards can
  now be called on you!", and the guard Elmer killed Kwik in the same second). Nothing the assist does attacks anything
  in a town; a cat on a landing or in a doorway is walked around.
- Every tailor sells thread (Damian, 2026-10-17: "its always a tailor that is thread selling, and they all do"), so a
  thread trip goes to tailors whose buy list was never read, Magincia's first ("magincia is great for that").
- A vendor is home when the smallest region holding it, z included, is its shop's (the 1998 `human.m`, `myHomeShop`
  against `getSmallestArea`); anywhere else it says "Let us go back to my shop" and walks or teleports to its job spot.
  Cybil's job spot is the Magincia mage shop's roof (z 60), above `MAGEGUILD_MAGINCIA`'s z 20..59, so she never trades
  there. The assist learns each vendor's home region from a positive answer and its away regions
  from "go back to my shop" (`vendor-homes-<server>.txt`), asks no vendor where it is known to be away, and passes it
  over (the survey goes on) when it stays away 60 s.
- Bankers carry no "the banker" title (Britain: Blythe, Francis, Elwood, Aida). Nujel'm's bank sign is at 3768,1313;
  "bank" is heard from 3761,1306.
- Busy: a double-click or lift leaves the player busy about 1 s; a lift or use inside it gets "You must wait to perform
  another action."
- A stack dropped on the ground comes back under a new serial on its tile (2026-10-25 10:39:58: 476C218C lifted,
  476C45A0 sent with its 82), so a put-down pile is found by its art on its tile (`DumpedPile`).
- Ore piles of two arts never merge (rundir `ore.m`): large ore (19B9) dropped onto small (19B7) is put back where it
  lay. A drop goes onto a pile of its own art and hue only (`SamePileAt`), and a hop moves every stack on its tile.
- A smelt makes metal / 2 ingots (`ore.m`) and says "There is not enough metal-bearing ore in this pile to make an
  ingot." under 1, before any skill check: the smallest ore (19B7) is smelted two at a time (`SmeltTake`).
- Skill checks and strength (OUO `skill.c`, `mobile.c`; rundir `skills.txt`): a dig, a chop, a smelt and a sew are each
  one `testSkill` (DirectUse), its stat roll (100 - stat) x the skill's stat weight x 50 (25 on a failure) / 1000 against
  20000; Mining, Lumberjacking and Blacksmithy weigh strength 100 and nothing else, Tailoring 15 / 65 dex / 20 int. A
  dig that answers "no metal" or "Someone has gotten to the metal before you" makes no check (`mining.m`). The stat
  total is capped at 225; every stat gain rolls to take a point from one of the other two, 50/50, at 0.1% under a
  total of 170 rising to 96% at 224, so tailoring's dex and int gains cost strength. Blacksmithy checks once an item,
  after a hammer loop of 1 to 5 short ticks a swing that ends 1 in 6 (`blacksmith.m`), through `testAndLearnSkill`,
  whose stat value follows the shard's own usage.

## The pieces (all in `apps/uoaix/assist/`)

- `GameLink.cs`: injectable steps, speech, buy and sell answers; the relay numbers every step (client and assist) in one
  count and gives the client its own numbers back; assist step answers are kept from the client, and the player is
  redrawn (0x20) after each assist step (`ShowMode`, `walk-show`).
- `AssistFeatures.cs`:
  - `VendorBuy`, `VendorSell` (by name, shop windows held from the client, 5 per sale, one ask; after "Let us go back to my shop" or "thou must give it to me" (the vendor away from its post) its walk is watched on the server's mobile updates until it stands still 5 s, or 1 s after a jump of more than 3 tiles (a teleport home), then it is followed and asked again, up to 3 asks while it keeps giving that answer; each positive answer widens the vendor's home box (`vendor-homes-<server>.txt`), and a vendor with a known home sends the player there at once on its "let us go" (Damian, 2026-10-17: "about 1 second ticks on walks, then a 5 second stop is usually an indication they are home"; "sometimes they teleport when you ask"); or 2.5 s later after "hand it over" / "hand it to me" (Damian, 2026-10-17: "the hand it to me line is misleading"); NPC replies classified, an unknown one kicked up by `replies`;
    `NearVendor` walks to the named vendor first, up to 3 legs as the vendor comes into view);
  - `Recall` (the rune in sight, reagents, mana, fizzle retries); `Peek` (a bag read without a window); `RuneLabels`;
  - Cave mining (Damian, 2026-10-18): the bank rune and the cave's rune are picked by target cursors, each label read and
  kept; after the recall back the circuit walks into the cave from where the rune lands; a run counts its ingots
  smelted and stored, in its status and its end line (a circuit's end line carries thread, shirts and sales only for
  tailoring); the storage trip goes at the "store at" ingots (`mine.store-at`, 500 unset); two shovels pace 1250 ms use to
  use; two shovels keep two chunks by the cave plan (below). Each 8x8 chunk keeps its last dig, last "no metal" line, and its regrowth evidence (the shortest empty to
  ore again, the longest seen still empty); a chunk rests until the cave's estimate from that evidence (30 minutes with
  none) and the Circuits tab's grid shows every chunk and when it is likely back.
- Houses: each multi the server sends (0x1A, graphic 0x4000 | id) stands on the map by its MULTI.MUL components
    (walls, floors, stairs) until deleted (`UoMap.PutMulti`, merged into `UoMap.StaticsAt`), so every walk, standing
    height and tile search sees player houses (Damian, 2026-10-18: "it doesn't navigate the player house here"); the
    mining circuit's standing tile is tested as a walk step is, ground items included.
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
  - `RunCircuit` (`CircuitGo`, `CircuitHalt`): the tailoring circuit is `RunShops` (above); sell only sells here, then
    on a `SellTrip` (its cities above). `Travel` recalls by a carried shop rune for the city, else from the nearest bank
    by the box's rune for it (`BankRecall`: a shop rune landing in one of the city's shops, else the city's rune, by
    landing; labels read in place and cached in `bank-runes-<server>.txt` only order the runes never recalled by).
    `CircuitWatch` stops it on staff, attack or death;
  - the circuit record (`circuit-<server>.txt`: each vendor's stock, buy-out time, restocks).
  - Suggest (the Circuits tab's Suggest row, control port `suggest on|off|do|skip`; Damian, 2026-10-19): each step the
    tailoring circuit would run (`Decide`) waits, named with the pack's counts, for Do it or Skip; Skip offers the next
    legal step; each answer is an "approve" or "reject" row in `decisions-<server>.csv` for the brain
    (`docs/Designs/Active/Apps/AssistCircuitBrain.md`).
- The buy circuit, "Buy reagents" (`RunBuy`, `circuit buy`): the shopping list (`circuit.buy.list`, `buy-list`,
  `buy-item <arts> <count> <highest gp> <name>`; default 250 black pearl at 5 gp) bought at the picked shops in their
  order, lap after lap while a lap buys anything; each city reached by a pack rune first, every bag in the pack scanned
  (`CarriedShopRune` over `RuneChoice.Pick`: a rune landing in the shop, then one landing in the city; a rune never
  recalled by is tried in its label's order; `AssistIdentity.md`; the tailoring trips too), the bank's runes after; a rune's kept label follows the server's labels and the client's
  renames (`RuneRelabel`); at each city's bank (`BuyBank`) the goods go into the box's container
  already holding the most of them, 30 of each recall reagent stay in the pack, and the purse (`buy-purse`, 2000) is
  topped up from or banked onto the box's own gold pile; at the bank where the circuit ends everything bought goes in,
  none kept back, and the purse drops to 300. Gold is never taken from a bag in the box (Damian, 2026-10-17:
  "i have stashed 30k gold in the bank into a sub-bag so nothing happens accidentally to it"). Default shops: The
  Circles of Magic in Vesper and The Sorcerer's Delight in Britain. The restock ("someday the reagent buy circuit will
  need to follow the same "get more next respawn" logic"): a shop whose keeper was bought out of an item still wanted is
  passed over for `RestockMinutes` (17); when a lap buys nothing and a bought-out shop's restock is still to come, the goods go
  into the bank there and the circuit waits for the soonest restock, then laps again; a lap after a wait that buys
  nothing ends it.
- The ingot-buying circuit, "Buy ingots" (`RunBuy` over `BuyListFor("ingots")`, `circuit ingots`; settings `ingots
  [count] [highest gp]`, 500 at 10 gp unset, twice ServUO's 5): iron ingots bought at the picked smith shops
  (`circuit-shops ingots`; region kinds BLACKSMITH, ARMORER, WEAPONRY, MINERSGUILD), else at the smith shops of the town
  it starts in, nearest first; banked, the purse and the restock as the buy circuit's.
- The advertiser, "Advertise" (`RunAdvert`, `circuit advert`, setup `advert title|text|author|per|price|places|here`;
  Damian, 2026-10-18: "an advertiser circuit that buys empty books, writes an ad, recalls to major banks and drops a book
  on the ground there so people see it"): started in a town, it buys the blank books short (arts 0FEF..0FF2, at the
  town's provisioners and scribes, 30 gp at most unset, twice ServUO's 15), writes each new one by the packets the client
  sends after writing (0x93 title and author, 0x66 pages, 8 lines a page of at most 24 characters, 20 pages; ServUO
  `BaseBook` takes them for any writable book within a tile, the pack's included) and reads its title back by a click; a
  book that did not take the ad ends it. A book of the player's own is never written: only the books bought on this run
  and books already titled with the ad are used. Then each place in turn (`bank:<town>`, `shop:<shop key>`,
  `spot:<x>,<y>`; default the banks of Britain, Vesper, Minoc, Trinsic and Moonglow), reached by the bank's rune for its
  town and a walk, gets its books per place (1 unset) put down beside the player; a book this session put down that
  still lies there counts. ServUO decays a floor item after 60 minutes unless the shard sets otherwise
  (`Item.DefaultDecayTime`). ServUO's Inscription copies a written book onto a blank one for free (`Inscribe.cs`: no
  materials, a check of 0 to 50.0, a failure loses nothing); the packets write every book, so the circuit uses no skill.
  The books bought are picked on the Circuits tab by their art from the client's files (`advert.books`; brown 0FEF, tan
  0FF0, red 0FF1, blue 0FF2, the ones the provisioners and scribes sell), red and blue unset: 1998 refuses to write into
  brown and tan (`inscribe.m`). The places are a grid of every town's bank and shops and the spots added, filtered by
  type and town, ticked in visiting order; the message is a wrapping box under it, a line break there a line break in
  the book.
- The rune marker, "Mark runes" (`RunMark`, `circuit mark`, `mark-setup class|per|price`; Damian, 2026-10-18: "mark a
  rune to a spot at each of a class like banks, tailors, etc. and we use smart randomizing selecting the spots so our
  customers dont end up blocking each other all buying the same mark"): every place of the class (every bank, or every
  shop of a kind) in every town but Wind, the town here first, `mark.per` runes each (1 unset). Blank runes are the
  pack's runes a click reads as unmarked; those short are bought at the town's mage shops (30 gp at most unset). Each
  rune is marked by a Mark scroll in the pack when one is there, else by the spell (45, 20 mana, the recall reagents);
  a fizzle is cast again, any other answer ends it. The spot: a standable, unoccupied tile inside the bank's or shop's
  region, the farthest from every spot marked there before (this run and earlier, `marks-<server>.txt`), chosen at
  random among the best quarter. Each rune is renamed "<place> <n>".
- In a shop, a keeper the walk did not reach is asked once more after the others (a held door can open meanwhile). A Teleport has landed only when the player moved off the cast tile.
- The general settings (the bank's gold amounts) sit above the circuit picker. The bank's bags are each picked on the
  Bags tab by a target cursor in the open bank box (Damian, 2026-10-18: "we should do a general setting like tool bag
  and bank tool bag, and bank store, for the bags of recall runes to the cities and shop types"): the bank store the buy
  circuits and the spellbook maker bank into (`<server>bank.store`), the city runes bag searched first for a city's rune
  (`<server>runes.city`) and the shop runes bags searched first for a shop's (`<server>runes.shop`, a list; a bag
  targeted again is removed).
- The spellbook maker, "Spellbooks" (`RunSpellbooks`, `circuit spellbook`, setup `spellbook-setup
  count|chance|inscribe|price|scroll-price`; Damian, 2026-10-18: "it buys spellbooks and scrolls from the vendors and
  stocks them. it should buy what it can and if inscription is adequate, fill up the books to the scribes skill level,
  if it wont fail too much without giving gains"): started in a town, it reads every spellbook in the pack (the scribe's
  own is the one of the most spells; the rest are stock), buys spellbooks up to the count (1 unset, 36 gp at most) and
  every scroll a stock book lacks that the town's mage shops sell (200 gp at most), and gives each book its scrolls. Then
  it inscribes what is still lacking, by the 1998 rules (`scripts.wombat/inscribe.m`, `spelskil.m`): only spells in the
  scribe's own book, of a circle the scribe's Magery can cast and mana holds, and only while the circle's Inscription
  chance, (skill - (circle - 1) * 1000 / 7) * 2 + 500 tenths of a percent, is at least the floor (75% unset), since a
  failure ruins the scroll and gains nothing. The blank scrolls and reagents for the attempts that chance expects are
  bought first. Finally the stock books go into the bank box's container already holding the most spellbooks. The skill
  value is the client's; 1998 weighs stats into the checked value, which the client does not show.
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
  from its storage bag (the bags of the last loadout picked by hand, `loadout.bag` and `loadout.storage`). With no
  storage bag set (the Circuit tab's "No storage bag"; Damian, 2026-10-18), the loadout comes from every bag in the bank
  box; what the bank lacks is bought in the city's mage shops (the buy circuit's picked ones there, else all), exactly
  the shortfall at the shopping list's price or twice RunUO's, with gold from the box's own piles; the change is banked
  and the reagents go into the loadout bag.
- The smithing circuit, "Smithing" (`RunSmithing`, `circuit smith`; Damian, 2026-10-18): started anywhere in a town
  (Minoc's bank), it first readies the ingots (from the bank when that is their source and the pack holds none) and a
  smith's tool (`SmithHammer`: the smithing toolbox when within reach, else a sledge hammer tinkered with the pack's
  tinker's tools, else one bought at the town's TINKER, BLACKSMITH, ARMORER or WEAPONRY shops with the purse taken at
  the city's bank when the pack holds under the keep; a failed tinker
  is tried again, 30 times at most, and a menu entry with no text is named by its art), then smiths at the nearest anvil with a forge within 6 tiles of it, from the tile between them (both
  within 3, `ToSmithy`). Anvils and forges seen as world items are kept in the atlas (`places-<server>.txt`, kinds Anvil
  and Forge, on the map); the map's statics are searched too (Minoc's have none: its smithies are server items). With
  none known, it walks into each of the town's smith shops and miners' guilds (region kinds BLACKSMITH, ARMORER,
  WEAPONRY, MINERSGUILD) the atlas holds no anvil or forge for, nearest first, so the atlas records what stands there,
  and looks again. There: the ladder's item for the skill (`SmithNow`: `smith-ladder`, his steps or the formula's guess) built with
  the smith's hammer, the menus walked by their text from the 1998 `blacksmith.m` table (Build Armor, then Ring, Chain or
  Plate Armor, Helmets under Plate; a category with one buildable entry is flattened, an item past the ingots or skill
  is not listed); what was made sold once the pack is full (the next piece would pass the carry limit, or 124 items) or
  the ingots run short, to each smith in reach whose stock names a made item (else any item the hammer builds), nearest by the
  planned walk, at any price; gold over `BankOver` banked to
  300; a hammer worn out readied the same way (the sledge's menus `Tools|Sledge`, settable); ingots (`smith-ingots`) the Circuits tab's "at a time" (100 unset) at a
  time from the city's bank box, or bought from a smith in reach; until Blacksmithing reads 100.0. Ended on its own,
  it sells what it made, then banks the gold over 300 and the ingots left. No skill lock is sent. "Build for"
  (`<server>smith.goal`; Damian, 2026-10-18): skill (the above), profit (the item of the most gold per ingot in the
  smithing book, sold to NPCs until the ingots run out), or order (`smith.order`, item@count; presets ring, chain and
  plate suits, five katanas, five spears): each piece built `smith.pick` times (2 unset), then the nearest smith's sell
  list read and answered with nothing, the best-paid pieces kept for people (the smith's price stands for the hidden
  quality) and the rest sold to it. The hammer's menu is the 1998 table by art (48 items: armour, helmets, shields,
  blades, axes, pole arms, bludgeons). Ingots "from the mine's storage" (`smith-ingots mine`): where
  the mining circuit stores them (`mine.store`): an ingot stash in the house, within 150 tiles of the anvil, walked to
  and up to "at a time" lifted from its largest pile; a stash in the bank box, the city's bank box as above; the
  backpack and vendor storage, the pack's ingots only. Ingots by "run mining circuit" (`smith-ingots harvest`; Damian, 2026-10-18: "add "run mining
  circuit" option to go harvest ingots first"): out of ingots, the mining circuit runs at the picked cave with its
  ingots kept in the pack until the pack is full, then the pack's bank rune recalls to the smithy's town and the walk
  goes back to the anvil; it needs a picked cave with its rune and the bank rune in the pack.
- A shop is surveyed while a circuit is in it (`SurveyHere`; Damian, 2026-10-18: "this store hasn't been properly
  surveyed", "that should have happenend"): after a buy there (reagents, the buy circuit's shops, sewing kits), each keeper
  whose stock the atlas never read is asked "buy" once, nothing bought, once a session.
- Heights (`UoMap.StepZ`; Damian, 2026-10-18: "pathfinding needs to be z-block aware enough to find the stairs leading up
  to the stage, and down to the door on the first floor"): every step, planned, walked and tracked, follows ServUO's
  `Movement.Check`: the climb is measured from the top of the surface stood on plus 2 (a stair counts by its base, a
  platform by its top, so a stage edge is a wall), the floor taken is the one nearest the walker's height, and a drop
  needs its whole column clear up to the old head height (so a balcony is no way down to the door under it). A door
  is opened only from its own floor (within 16 of it).
- A crowded door (Damian, 2026-10-18: "recall if stuck, give up that local target if you cannot make ingress"): a door
  with someone beside it is left alone and walked round; with no way round, the walk waits once ("Held door wait", 5 s)
  for nobody to stand beside it and takes it. Still crowded: from inside a shop the target is not in, a recall out by a
  pack rune naming the town, and none is no recall; from outside, the target is given up. A Teleport never lands on a
  door tile. A vendor walk that fails inside the vendor's shop looks again from where the vendor stands now, up to three
  legs, before asking from where the player is (Damian, 2026-10-20: "the circuit should have reacquired the location of
  the target"; "my shop sells many things means the circuit wasn't close enough to get the buy list").
  A target farther than 400 tiles is refused before any plan. The shop surveyed while there (`SurveyHere`) is the
  smallest shop region holding the player, and HOUSING regions are no shop (HOUSING_NO_JHELOM held all of Jhelom).
  The walk planner reads ground items and people from a snapshot by tile (250 ms at most old), and logs a plan over 1 s.
- People in the way (Damian, 2026-10-18: "push past, wait for stamina, or ... recall to bank"): with no way round them,
  the walk pushes through at full stamina, else rests until full (90 s at most) and plans again; a bank's approach with
  no walk to the bank recalls by the pack's rune for the bank's town (any bag in the pack) when the reagents are held,
  landing within 40 tiles of the bank.
- The bank's gold amounts, for every circuit, on the Circuits tab (Damian, 2026-10-18: "need a configurable bank gold
  amount control in ui"): a walk to a bank only for a deposit starts over `bank.over` gold (5000 unset), and every
  deposit leaves `bank.keep` (300 unset) in the pack.
- A bank's approach starts with "bank" where the player stands when a banker in view is within 12 tiles, any floor (a
  roof landing; Damian, 2026-10-19: "bank would have opened from roof too"), then the tile a banker answered from last
  time (`<server>bankspot.<bank>`), then the edge, the
  door, the hearing and beside the banker as below. A container window from the pack is never the bank's answer (the
  player's own double-click on the pack can open one just after "bank" is said). Tinkering's "Tinkering failed." comes in place of the menu and is a failed try, tried again.
- Keep thread (`RunKeepThread`, `circuit keep`, the Circuits tab's "Keep thread"; Damian, 2026-10-21: "keeps these
  vendors bought out, never assumes, always checks, runs in jhelom and brit between shops to save mana"): until
  stopped, the thread vendors of the approved shops: the vendor due soonest (`ThreadDueAt`, by `KeepRules.ProbeAt`
  below) is waited for until it is due or a restock of it is seen, its city's purse is filled, the player goes to its
  shop by its own rune, and it is asked first, then every other vendor of that city, nearest first, whatever its
  forecast; "the vendor has none" is an answer, counted and asked again next lap. Inside Jhelom and Britain the shops
  are walked between, never recalled (`KeepWalkCities`). The purse is sized for every vendor of the city bought whole
  (`KeepRules.PurseFor` over `ThreadExpected` and each vendor's price) and filled from the bank box's top-level gold at
  the bank of the town the player is in before the trip, and every bank stop leaves it in the pack (`KeepRules.PackKeeps`); a vendor whose answer asks
  for more gold has the purse raised to its whole price and is asked once more. The recall reagents are fetched from
  the bank (`TripReady`, the loadout's storage) before each trip to another city. The thread past the carry limit is stashed in the shop where it was bought, on its
  most hidden floor tile (`StashSpots` in `Stash.cs`: a tile the client draws behind a wall or tall static south-east of
  it, else half behind one east or south, else beside furniture; roofs count for nothing), kept in `stash-<server>.txt`
  across restarts, and lifted back when the cycle is next in that city with room; a stash gone from its tile is
  noted with its age and forgotten. Once the pack is nine tenths full its thread goes on the house pile (the thread
  overflow, `OverflowStore`).
- How a 1998 vendor restocks (OUO, `D:\Projects\uo-reference\ouo`; read 2026-10-24):
  - The clock: a game tick is 250 ms (`main.c` `TICK_INTERVAL_MS`); every 8th tick beats 8 of the 64 NPC hash
    buckets (`time.c` `CTimeManager_Update`, `npc.c` `CNPCHash_HeartbeatTick`), so an NPC beats every 16 s. A
    vendor's `restockCounter` drops by one a beat and restocks below 0, then is set to 200 (`npc.c` `CNPC_Heartbeat`,
    `shopkeeper.c` `CMobile_VendorRestockTick`): one restock every 201 beats, 3216 s (53.6 minutes). A wake from
    frozen (a player coming in range) queues one more beat (`CNPC_SetSleeping`). The counter runs from the vendor's
    creation and is saved with it (`dynamic.c`); a purchase does not touch it, so the restock falls at the vendor's own
    phase, 0 to 53.6 minutes after a buy-out.
  - The amount (`mobile.c` `CMobile_ComputeVendorFee`): each stock item keeps `stockTarget`, its amount after the
    last restock. At a restock, k units bought since then leave T - k; the item is brought to k + 10, restocked up or
    sold down (a ware bought out first restocks 1 in the first pass, so it comes back at T + 9). A ware nobody bought
    falls to 10 at the next restock.
  - The money: each unit restocked costs the vendor its value from its own bank gold, and stops when the gold runs
    out; with no bank gold nothing restocks. A purchase puts the price in the vendor's bank (`shopkeeper.c`); a new
    vendor starts with its stock's value, 9 times for wares of 1000 gp or less (`template.c`
    `CTemplateManager_ValidateVendorStock`). Each item sold to the vendor goes at its restock with chance 3 in 4, its
    value into the vendor's bank (`VendorStock_ComputeSoldItemsFee`).
  - Measured on uo1998 (the attempts record, 2026-10-20 to 23; every rise of a thread stock between two visits 10
    minutes or more apart): a period of 3280 to 3296 s, so about 2% over the 3216 the source gives (the server's tick
    runs late). One period and one phase fit every rise before 2026-10-21 11:00 and after 13:00, not across: the phase
    moves, so it is learned from recent visits. Each wake from frozen (a player coming in range) adds a beat, 16 s.
  - Every restock seen as it happens (within 18 tiles of the vendor: a ware of its stock box destroyed and back with
    more, `AssistState.Restocks`) is said ("Keep: <vendor> restocked at ...") and kept in `restocks-<server>.txt`.
  - The strike window (`KeepRules.ProbeAt`, from the vendor's thread buys in the attempts record and its restocks
    seen; the period refined from the gaps seen): the phases its restock can fall at, as arcs of the period. A visit
    finding it restocked since it was last empty keeps the arc between; a dry visit drops it; a restock seen keeps the
    16 s before it. A bought-out vendor is next due at the median of the times left within one period, so each visit
    halves the window whatever it finds (Damian, 2026-10-24: "you check a few like bisecting is fine ... then you
    should develop better and better estimates of when, while still maintaining the money flow"); once the window is
    2 minutes or under, at its end plus 60 s. With nothing known that is 27.4 minutes after the buy-out; a hidden
    phase is found in about five visits and each later visit lands 1 to 3 minutes after the strike. Evidence that
    leaves no phase: a restock found keeps only itself, a dry visit starts the window again, as does a dry visit just
    past a period after the buy-out (the phase moved later); a dry visit long after is another player's buy and moves
    nothing. Evidence older than 24 hours is dropped. Keep thread waits for the vendor due soonest (a restock of it seen
    ends the wait); the other circuits only pass a vendor not yet due over.
- How a 1998 vendor prices what it buys (OUO `shopkeeper.c`): the sell window offers half of `GetBuyPrice`, at most 5
  items an offer, every item of an offer at the price before it. `GetBuyPrice` of an item of value b is b + b/4 x
  (1 + m / (m + c)), where m is the vendor's own stock of that item plus 1 (its stock box and the items bought from
  players not yet gone at a restock) and c the stock of every other vendor in its resource-bank region
  (`CShopkeeper_VendorAdjustment`); then marked up by the share it lacks of its own stock target, up to double, for an
  item it also sells. A tailor sells no fancy shirt, so its offer for one (b 36) runs from 23 (its share of the
  region's shirts near 0.1) to 27 (all of them). The other vendors' stock c is cached per region and item type: every
  4096 ticks, about 17 minutes (`resbank.c` `CResBankManager_RespawnTimerCheck`), the region is marked for a recount,
  and the first price asked there recounts only its own item type; the cache starts at 0. The vendor's own m is read
  live. So every tailor of a region pays 27 while the cached c is 0, and the region drops together at a recount that
  finds other vendors holding shirts; each tailor's own shirts go at its restocks (3 in 4 each), so a tailor's offer
  falls overnight and climbs again as it is sold to. A vendor pays only from its bank ("I can't afford any more of that
  right now"). Measured on the trades record: within a run of sales to one vendor, 335 rises, 11 falls and 17,827
  unchanged (2026-10-15 to 24); Brian, Moonglow's one tailor, at 27 in every sale (1182 of 1182 to 2026-10-20); a
  tailor left at 27 opened at 27 again 9 times in 9 within 1.3 hours and 3 in 15 past 2 hours. The regions are OUO's
  `resregions.txt` (hand-authored by OUO, so uo1998's own file is not known), nine rectangles: Britain (Britain alone);
  Vesper (Vesper, Nujel'm, Magincia, Ocllo, Serpent's Hold); Trinsic (Trinsic, Skara Brae, Jhelom, Buccaneer's Den);
  Minoc (Minoc, Cove); Yew; Moonglow; Dungeons; Green Acres; Lost Lands (Delucia, Papua).
- The bank's gold (Damian, 2026-10-24: "any gold pile in the top level of the bank is fine, just dont go digging in
  bags for gold ever. gold in subcontainers in the bank is savings, for a major project"): every circuit takes its
  purse from the box's top-level piles and deposits onto them; gold in any bank bag is savings and never touched. A
  purse the box cannot fill ends the circuit, saying so.
- The tools, a toolbox and a toolbag per profession with its road count: `docs/Designs/Active/Apps/AssistTools.md`
  (`Tools.cs`). The homespun circuit tops its kits up to 4 at the thread shops.
- Mounted, the mining circuit dismounts where it starts (the back of the cave), tells the mount "<name> stop" ("all
  stop" with no name known), and when the circuit ends on its own walks back and mounts again; stopped by Stop, the
  player's step or an alarm, it leaves the mount and says where.
- Cave mining (`RunMineOn`; Damian, 2026-10-18: "need a drop down for 1 or 2 shovel digging, a tool bag selection,
  automatic selection and walking to targets, using the 8x8 resource zone"): no recorded dig. Targets come from the 1998
  `mining.m`/`itemmanip.m` rules: land ids Q4ZJ and static ids Q4ZI within 4 tiles, the metal pooled per 8x8 chunk. A
  chunk answering "no metal" is passed over for 30 minutes; a refused or unanswered tile is never dug again, and 3
  unanswered digs in a row end the circuit. 2 shovels (`mine.mode`): two digs in flight in two chunks by the cave plan
  (`MinePlanner.cs`; Damian, 2026-10-22: "digging with one shovel should only happen when only 1 shovel is available
  or there is no available adjacent spawn 8x8 chunks"). After the sentinel, the cave's chunks are ordered once, each
  next the neighbour with the fewest unvisited neighbours (dead ends first, none stranded). The free shovel takes a
  chunk of its own in reach, a started one before a fresh one, then the earliest in the order. With none, it waits for
  the dig in flight and the miner walks to the stand reaching the chunk still being dug and another, else two others
  (more first, up to 3, then the earliest in the order, then the nearest); only with no such stand do both dig two spots
  of one chunk. A shovel is never used while its own dig is in flight; results are matched to digs in order. With
  nothing in reach it walks to the standing tile reaching the most live chunks. The cave respawns as a whole, never chunk
  by chunk. A circuit's start trusts nothing: the sentinel (the chunk nearest the cave's mark, its doorway) is emptied
  first, then every chunk is probed. A chunk answering "no metal" rests until the next strike: metal in any chunk
  emptied since the last strike (the circuit's dig or the player's) means the cave respawned, every chunk is fresh, and
  the sentinel is emptied first again. With every chunk resting the sentinel is dug every 60 s, every 30 s within 3
  minutes of the respawn the measured cycle predicts (and with none measured), for 2 hours at most. A mining toolbox the player put down in the cave
  (`AssistTools.md`) is the cave's shovel store: down to 1 shovel, the circuit walks to it and tops the toolbag up to
  its road count; the circuit never lifts or drops a toolbox or a toolbag. Storage (`mine.store`; Damian, 2026-10-22): backpack (local
  while mining; ends when the pack is full after smelting), the house vendor (secure, small, for sale; the stash takes
  the rest), or the ingot stash (`house.stash`, a target: the floor, an ingot pile or a container in the house, big and
  unsecured, or an ingot pile or a container in the bank box, secure and small, reached only by the pack rune
  `mine.bank-rune`; a full bank stash sends the rest to the house floor). Out of
  shovels, or with 2 shovels set and fewer than 2 in the pack, the bag and the box together (Damian, 2026-10-22; the
  box is fetched from first, its spot remembered out of view; a trip that brings none is not made again that run):
  first the ore smelted and the ingots stored (vendor or house stash; Damian, 2026-10-22), then the house vendor's at 14 gp, else a recall to the bank town, 14 gp a shovel short taken from the bank box,
  shovels bought at the town's tinker shops up to `mine.shovels` (10), or with a mining toolbox in the cave the bag's
  road count plus 20 for the box, every one past the road count put in the box on the way back (Damian, 2026-10-22:
  "rebuy should fill the box first with 10-20 if available"), and the pack's weight (strength * 4 + 30 or `pack.limit`, the lower), into the tool bag
  (the pack's tool bag, below), and the cave's rune straight back. Caves (`caves-<server>.txt`, `mine.cave`): marked by name, each with its rune
  back, its chunks' ore digs and emptyings (the seam depth), and the people and creatures seen near.
- The tailoring circuits' shops are the tailor runes bag's (Damian, 2026-10-20: "lets just use the runes in the bag as the
  list of player approved shops"): every tailor shop holding a rune of the bag, by the rune's recorded landing, else its
  label (`ApprovedShops`), visited in the fixed order; the tailor circuit has no shop picker. With no bag runes seen, the
  fixed order's 12 shops. The other circuits' shops are picked by shop name (`circuit.shops.buy`, `circuit-shops`).
- The Map tab's Find (`MapSearch`, `ShowRoute`, `MapGo`; Damian, 2026-10-18: "in map add search box for name and type
  of shop or feature, that shows shortest path, and a walk button there, and a run button too"): every shop of the
  region file by name and kind, every bank, every place the atlas keeps and every named shopkeeper, banker and healer,
  matched by every word typed, nearest first (60 at most); the one picked has its shortest walk drawn in orange (to the
  first teleporter on it, when it takes one), and Walk or Run (the pace for that walk) takes the player there; Stop or a
  step of the player's own ends it.
- `AssistWindow.Circuits.cs`: the Circuit tab (setup: the circuit, its lists, its shops ticked and ordered, its launch modes and
  Stop, each with a tip; then job, vendors, totals); the trades file reloads at start.
- `Assist.cs` control port (relay port + 1): `say`, `vendor-buy <arts> <most> [name]`, `vendor-sell <arts> <floor>
  [name]`, `recall <rune>`, `peek <bag>`, `ground [reach]`, `walk <x> <y> [reach]`, `walk-to <name> [reach]`,
  `walk-show 20|77|0`, `work`, `sew`, `bank-deposit [keep]`, `bank-take <arts> <count>`, `pack-merge <arts>`,
  `runes [reach]`, `items`, `stop`, `circuit go|stop|status`, `action <name>`, `timing [key [ms]]` (every timing, one,
  or one set as the Timings panel sets it, logged), and the rest in `AssistRelay.Command`
  (`pace`, `flee`, `travel`, `names`, `labels`, `contents`, `put`, `move`, `bank-put`, `replies`).
- Homespun (Damian, 2026-10-21, `AssistFeatures.Homespun`, the Circuits tab's Tailoring checkbox): in the registered
  private house (`AssistHouses.md`), `HomespunHome` puts the carried thread on the pile and `HomeSew` sews where the
  floor's stock lies, never lifting it, the most finished first (`SewSource`): the largest cut cloth pile, then the
  largest bolt stack; the loom weaves the thread pile only when neither is left. The tailoring steps (Damian,
  2026-10-24: "remove night and day, instead replacing with which steps of the tailor process we want to enable"),
  each ticked unless unticked and never run unticked: Buy thread, Weave, Sew from bolts, Sew from cut cloth
  (`circuit.step.buy|weave|bolts|cloth`); "if there is lots of cut cloth we shouldnt be looming it (but we should buy
  thread and store it)". The left-over cloth goes down where the player
  stands (onto the cloth pile within 2) whenever the pack is nine tenths full by items or weight, until it is nine
  tenths full of shirts. Out by `HomeLeave`: the door shut and locked from the doorstep, the key back on the vendor
  from the key spot (`HomeKeySpot`: the doorstep when guarded, else the guarded open tile beside it, east first; the
  Moonglow Astronomical Society's doorstep 4699,1116 is outside JUSTICE_MOONGLOW_3, which starts at x 4700). Then
  `RunShops` from the order's first shop (Moonglow's) by rune: shirts sold, thread bought, the bank as due, never
  sewing or weaving at a shop. Home (the bank stop and the house rune, in by `HomeEnter`, the key taken at the same
  spot) when the shirts are gone or every shop is visited, and again; the circuit ends when a home stop sews no
  shirt.

## Next

0. The buy circuit (reagents) passes a bought-out shop over for `RestockMinutes` (17), under the measured period: its
   vendors move to the strike window (`KeepRules.ProbeAt`) once their buys are recorded as visits. `ThreadExpected` (a
   buy-out's amount plus 9 for 62 minutes after it, else 10) stands in for the 1998 rule: the amount bought since the
   vendor's last restock plus 10 (plus 9 after a buy-out) from its next restock until the one after, then 10.
1. Selling by the offer's rule, on Damian's word: every shirt of a region to its tailor paying the most; all of a
   region's tailors within one 17-minute cache window; a tailor shortly before its restock rather than after.
2. Travel: a recall's first step waits until no 0x20 has come for 400 ms (uo1998 sends two).
3. Serpent's Hold and Buccaneer's Den have tailors but no shop region in the 1998 file: find the tailor by the mobiles
   in view. Vesper has two tailors, both "The Spinning Wheel": Morena's at 2952,616 and a second at 2832,880
   (CLOTHIER_VESPER_2).
4. Every hop goes from the nearest bank by the bank box's rune for the city (`BankRecall`, control port `travel
   <city>`; Damian, 2026-10-17: "all the destinations we need are right there in any bank"); no hop goes through
   the moat library. A carried shop runes bag's rune for the city goes first, from where the player stands; the shop
   runes bags are taken into the pack at the first recall from a bank (Damian, 2026-10-18: "take the whole bag of
   vendor runes out of the bank and use it directly rather than go back and forth to bank"); every other rune stays.
   The tailoring circuit and sell-only begin by carrying it (`CarryShopRunes`; "look for a bag full of tailor shop runes
   in the backpack or bank, and put it in backpack if it fits", "if it wont fit stop"): none in the pack, the town's bank
   box is opened and the picked shop runes bag, else the bag holding the most runes labelled for a tailor shop (a TAILOR
   shop's name in any town, or "tailor"), is lifted into the pack and becomes the picked one; past 124 pack items or the
   carry limit the circuit stops and says so; no such bag is noted and the bank's runes serve.
   A walk recalls first where that is cheaper (`RecallNearer`; Damian, 2026-10-18: "walking from the two shops in vesper
   is probably worth the recall. also brit too"): each recall learns its rune's landing (`rune-landings-<server>.txt`),
   and a walk recalls by the pack rune landing nearest the target, then walks on, when the recall costs less gold
   ("add in time to walk it, in comparison to opportunity to be making the shirts instead"): the straight-line walk's
   time at the making rate, against the recall's measured cast time plus the walk from its landing at that rate plus
   its reagents at the lowest atlas price. The making rate is the circuit's sales over its time not spent walking once it
   has run 5 minutes and sold, else `walk.goldrate` (gold a minute, 180 unset, Circuit general settings; 0 never).
5. Jhelom is islands joined by teleporters (Damian, 2026-10-17): the bank and one Adventurer's Needle (1352,3776) are on
   the north island, the other (1448,4000) on the south island; teleporters are server-side items the client is never
   sent; the assist learns one from any step (the player's or its own) the server answers by drawing the player more
   than 2 tiles off with no spell cursor in 10 s, keeps it in `teleporters-<server>.txt` and routes through it.
   Teleporters from public data load first (`teleporters-public.txt` beside the sources: ServUO's Felucca table inside
   the 1998 map, 392 one-way links, Jhelom's middle island 1419,3832 to the south island 1466,4015 among them; Damian,
   2026-10-18: "teleporters you can guess from public data, and ones that are found by in game discovery"), each on
   the client map's standing height nearest its listed z ("be careful of the z too"). Any teleporter, guessed or
   learned, that does not move the player has its 8 neighbours stepped on, on its floor ("add 1 tile in all
   directions"), and is then forgotten (`dead-teleporters-<server>.txt`, and out of the learned file) and the walk plans
   again; a jump seen in game always wins. A jump is learned from the tile of the oldest step still unanswered when the
   far redraw comes, else where the player stood. The walk to Kimmie on the south island is tried twice a visit
   (selling, then thread), nothing said.
6. Open faults: the walk to Jannelle had "no way" from her shop door; Vesper's bank had no way (canals); the walk
   planner found "no way" from Stitchin' Time to Magincia's bank and inside Moonglow's bank to its banker (Damian walked
   the first); two shop doors were opened 4 times in 3 s (Jannelle's, 3765,1255); Kathleen (The Sorcerer's Delight)
   answered "go back to my shop" twice from inside her shop, not moving (a vendor's home is its shop region with z,
   `VendorAtHome`; her region and z were not read). CLIENT.EXE crashes are `client-crashes.md`.
7. The planner steps a diagonal only past two open sides (ServUO's rule); uo1998 let Damian walk diagonally into a hollow
   past two blocked sides (2659,459). Loosen it for real walks only on refused or accepted steps seen in a log.
8. "thou must give it to me and I'll decide if I need it in my stock" is one of the 1998 shopkeeper convo's random sell
   lines (`rundir/convo/shopkeep.frg`), not a going-home answer; it still reads as GoingHome (Simon, Trinsic) until Damian
   says otherwise.
9. A future pass on the overweight dump (Damian, 2026-10-20): the dump goes under the player's own feet, because an
   overweight player can neither walk nor recall. The better spot is one hidden from most players, behind the west or
   north side of a building or another obstruction ("i don't think that is necessary optimization for the cycle").

Drive it from PowerShell with a TCP client on 127.0.0.1:2651, one command a line and one answer line each.
