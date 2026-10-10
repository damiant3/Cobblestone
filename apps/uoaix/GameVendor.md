# Classic vendor packets

`GameVendor` exports `gv-specs` and `gv-handler vendorWorld` for
`GameDispatch.md`'s authenticated `gn-route` hook. It handles classic ASCII
`buy` / `vendor buy`, `sell` / `vendor sell`, double-click 06, buy cart 3B
and sell cart 9F. Other speech and unrelated double-clicks return None.
The owner composes callbacks on None. No handler sends raw network data.

The composite routes [NPC speech](NpcSpeech.md) before this handler for
case-insensitive keywords, named listeners and greetings. Vendor menus and
cart settlement admit living mobiles within four tiles and sixteen height
units, matching the selected pre-AOS vendor policy. Spoken `buy` and `sell`,
optionally after `vendor` or a role word (`blacksmith sell`, any case),
select exactly one vendor: the closest in range of that role by squared tile
distance, the lowest index on a tie. The answering vendor speaks overhead
("Take a look at my goods.", "Let me see what you have."), and a sell request
with nothing it buys answers "You have nothing I would be interested in."
with no menu.

The binding is explicit: `gv-new game currency`, `gv-add-vendor` with a
mobile, economy actor and stock/extra/buys containers, `gv-add-player`
with a mobile, economy actor and backpack, then `gv-add-lot` with a stable
physical serial and its exact per-unit quality/wear and total cost basis.
The owner admits unmarked, ordinary movable stock only. Client graphics
remain distinct from catalog IDs. Existing aggregate stocks must cover
every admitted physical lot; admission never creates economic inventory.
The binding owner must keep these mappings authoritative when other game
actions move, consume, merge or split items. Marked property is not admitted
until title/mark metadata can join this transaction.

Each menu copies server prices and quantities into fixed player quote rows.
The quote binds vendor, side, authenticated connection and a 60-second real
time deadline, converted to raw PIT ticks with ceiling division. Speech
passes the core C03 type, color, font and terminated-ASCII validation before
decoding or quote mutation. Checkout rechecks range, life, ownership, amounts, purse
balances and all bounded log capacities. Duplicate serial rows combine and
cannot exceed the quoted quantity. A consumed or expired quote cannot replay.
Prices never come from the cart. A vendor buys only what its own trade uses,
at the value to that trade (Damian, 2026-10-05: "a blacksmith would only
purchase items they planned to smelt, and would offer only the value of the
melt"). `GameVendorBuys` holds each role's materials and goods: a material
fetches its standard cost (1 per gathered unit; a crafted unit's first-recipe
inputs plus labor over the batch), a good fetches only its recipe inputs that
are the role's materials. Bids clamp to the classic 16-bit field. The sell
menu walks the backpack four containers deep; a loose item that is not a lot
is named by its art and, at checkout, joins the seller's pool through
`ep-find` and becomes a lot. A refused checkout keeps that admission.

Successful buy and sell settlement append one classic 54 sound packet to
the trading player's reply, at that player's position. The sound is
Source-X `SOUND_DROP_GOLD1` (0032), flags 1, volume 0, with 16-bit x/y/z.
The writer follows `send.cpp::PacketPlaySound`; the sound choice follows
`CClientEvent.cpp` vendor settlement and `uofiles_enums.h`. Menus, cancelled
or refused carts do not emit the sound. The composite sends the reply only
after the encompassing transaction commits. The packet adds constant scratch
work and no retained state; no codec format changes.

In a copper world prices, cost basis and `GvSale.gross` count copper
(EconomyCurrency.md, "Price scale"). Checkout first breaks the payer's coin
with the crown (`gv-change`), then selects exact tender, using gold, then
silver, then copper at the current royal rates (`gv-tender-plan`).
A rate edit invalidates outstanding quotes. The classic status field displays
the total purse value rounded down to whole gold; the reply and server log
state the actual denominations paid. The client's buy and sell windows show
one number a line, which counts copper, so each line's name carries its price
as `<item> at 6g5s` (`gvp-priced`): a quote of a gold or more is rounded to the
nearest silver and one under a gold is exact in silver and copper, never three
coins (`gvp-round`, applied where the quote is filled, so the charge is the
label; UOAIX-225). The vendor's opening line states the
rates, and a settled cart answers with a receipt gump (`gv-receipt`, 0xB0):
each line, the total, the coin by metal with tax, and the purse after. Sell receipts report net proceeds and
per-metal tax separately. `eu-pay` retains the old gold-only path;
mixed tender uses `eu-sale`, actual per-metal payment/tax and a zero-coin gold
valuation marker linking `EpTrade` to the currency event. No copper or silver
balance is relabeled as gold, and no fictitious gold payment is recorded.
`GvSale` additionally links source/delivery serials, exact quantities,
quality/wear, currency event and gold action. Full-stack transfers retain
the serial; partial transfers create a new serial and retain the remainder.
Sold goods move to the vendor extra container rather than disappearing.
Player sale quotes and checkout require the registered backpack subtree;
bank descendants and items deposited after quoting cannot be sold remotely.

Checkout preflights the entire cart, prepares one WorldAction and records
bounded before-images for money, aggregate inventory, lot metadata and log
tails. An unexpected payment refusal restores these and rolls back the
WorldAction before returning a reply. This is a single-owner memory
transaction. It does not commit a shard journal or promise restart recovery;
the shared game/storage owner must add one encompassing durable action,
including bindings, quote invalidation and serial metadata.
[GameVendorCodec.md](GameVendorCodec.md) defines the vendor metadata payload
for that owner, including corpse/ground lots and quote reset.

## Source and license

Adapted from SphereServer X contributors' Source-X, Apache License 2.0,
commit `dd28a0ad53258adb55b548b1bd4df989065a1fe4`, read from
`D:/Projects/uo-reference/Source-X`. The full license is retained in
[ports/Source-X-LICENSE.txt](ports/Source-X-LICENSE.txt).

- `src/network/receive.cpp`: PacketVendorBuyReq and PacketVendorSellReq.
- `src/network/send.cpp`: PacketCloseVendor, PacketItemContents,
  PacketItemContainer, PacketVendorBuyList and PacketVendorSellList.
- `src/game/clients/CClientMsg.cpp`: addShopMenuBuy / addShopMenuSell.
- `src/game/clients/CClientEvent.cpp`: Event_VendorBuy / Event_VendorSell.

The Codex translation uses the pre-grid client layouts, fixed budgets,
server-bound economy actors and conserved currency. It omits Sphere's NPC
restocking and deletion of bought-back goods. No GPL code was used.

## Acceptance entry

`GameVendorServer.codex` uses the existing game transport and MUL preload.
It is a volatile, single-character acceptance world. The first selected
character is explicitly the demo crown-business actor, not a general player
grant policy. Treasury and vendor purses start at zero. The crown harvests
materials, makes tools, mines gold, smelts it and mints it. Named demonstration
terms are 1000 gold per ingot, a 100-gold royal business grant, a 200-gold
vendor loan with 20 interest due at game hour 48, and 10 percent tax. The
vendor harvests four branches; its physical stack represents that stock.
The mixed-tender demo also mines and smelts copper and silver, using named
demonstration yields of 1000 copper and 100 silver per ingot. It grants the
crown-business player 200 copper and 40 silver, and returns 99 of its gold to
the treasury. The test purse therefore holds 1 gold, 40 silver and 200 copper,
worth 7 gold at the default rates. Buying one branch exercises all three
denominations; selling it back exercises the vendor-funded return path.

The provisioner stands at 1421,1698. The first world pulse equips a backpack,
draws the provisioner and reports purse gold. Say `buy`, buy part of the
branch stack, then say `sell` and return it. The acceptance fixture renders
the vendor naked. `proofs/GameVendorReplay.codex` grades buy/sell packet
layout, mixed tender and per-metal tax, net sell receipts, split/full serial
transfer, census, complete rollback, rate-change refusal and both checkpoint
formats. Marked property is not admitted.

## Cost

Vendor state preallocates 16 vendors, `gv-player-limit` player bindings with
32 quote rows each, `gv-lot-limit` physical lots and 1024 serial trade rows. No cart grows retained
state beyond those budgets. Request lists, packets and undo buffers belong
above the network loop's scratch mark. Retained callbacks and vendor state
must be allocated before entering that loop.

Buy menus scan at most 128 lots and bounded parent chains; a sell menu visits the backpack subtree four containers deep, linear in its items, and prices each candidate by a catalog walk bounded at depth 16. A cart has
at most 32 lines and 64 world events. Aggregate checks are O(lines squared);
child checks use maintained O(1) counts and ownership walks stop after 65
steps. Admission requires a ready world index. Undo space is linear in cart lines plus the fixed lot/pool
tables. No whole economy copy or growing request history is retained.
Exhausted budgets refuse before mutation.
