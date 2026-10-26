# Assist identity: objects by place or by target, never by name

Owner: blu. Campaign over `apps/uoaix/assist`, under `apps/uoaix/assist/assist-csharp.md`.

## Ruling

Damian, 2026-10-22: "make all identification of ingame objects either tied to map/region knowns or by targeting in
the client. matching on names is ambiguous when both shops are the spinning wheel in vesper. spinning wheel vesper
north/south works for me." Earlier the same day: "shops have more than name... it is not a unique identity of a shop
anymore than walmart is".

## The rule

An in-game object is identified by one of:

- **A map or region known.** A shop is its `UoRegions.Shop` rectangle (`Key`, `Holds`); a city is its region; a cave
  is its mark and radius; a rune is where it lands (`rune-landings-<server>.txt`, written at every recall that moved
  the player, `RuneLanded`); a house is its registered rectangle.
- **A target in the client.** A serial the player picked with a cursor (toolbox, stash, bank bags, house vendor).
- **A serial the server sent** while the object stood at a known place (a vendor seen inside a shop's rectangle).

A name (a rune's label, an NPC's name, a shop's title, a tile's name) is for display and for speech only: a vendor is
still addressed "Brian buy", and a label may suggest which unknown rune to try first, after which its landing decides.
Display names that collide are told apart by place: two shops of one title in one city read "Spinning Wheel, Vesper
north" and "Spinning Wheel, Vesper south".

A census of what still matches by name: `Select-String apps/uoaix/assist/AssistFeatures*.cs -Pattern
'PlaceKey\(|RunePlace\(|PackRuneFor\(|bankRunes\.TryGetValue|shop\.Text|x\.Text\)|s\.Text\)'`, grouped by the
enclosing method.

## Stages

1. **Runes by landing.** Every rune choice takes a rune whose recorded landing lies in the place wanted (a shop's
   rectangle, a city's region, within a cave's radius, beside a bank); a label only orders the unknown runes to try,
   and the first recall records the landing. Settings that store a label (`mine.bank-rune`, cave runes, the overflow
   rune) store the rune's serial, picked by target, with its landing.
2. **Shops by rectangle.** Every shop comparison by `Key` or `Holds`; a display name with a compass suffix when its
   city holds another shop of the same title (`UoRegions.Shop`, the window's lists, notes).
3. **Vendors by serial and shop.** Records and lists (trades, vendor homes, the thread report, thread-off) keyed by
   serial, shown with the shop's display name.
4. **Item kinds by graphic.** The `UoTiles.Name` compares reviewed against the graphic sets they stand for.

Each stage lands with `test-assist.ps1` green, the coverage of the files it touched measured, and test cases for the
matching it replaces (a label naming one shop and landing in another; two shops of one title in one city).

## Built

Stage 2's display half: `UoRegions.Display` (the title, else the title, the city and a compass point among the shops
of that title, cached per shop) names shops in the window's shop lists and in every circuit status and note that named
a shop by its title (`ShopDisplayTests`).

Stage 1's rule: `RuneChoice.Pick` (`RuneChoice.cs`, `RuneChoiceTests`) takes a rune landing in the place, never one
landing elsewhere, and orders the runes never recalled by by their labels. `CarriedShopRune` uses it, the shop's
rectangle first and then the city's region, and its callers pass the shop, not its title. `PackRuneFor` takes the
place: a town's rune lands in its region (`PackTownRune`), the mining bank rune within `BankRuneTiles` of a bank
(`PackBankRune`), a cave's rune within its radius and `CaveRuneSlack`. `BankRecall` takes a shop rune landing in a shop
of the city (`InShop`: not a `NONE_`, `BANK_`, `HOUSING_` or `PLAINS` area, where the Magincia, Nujel'm and Moonglow
runes land) and the city's rune landing anywhere in the city.

A rune picked by target keeps its serial: the mining bank rune (`mine.bank-rune-serial`), a cave's rune back (the caves
file's 13th column) and the overflow rune (`overflow.rune`); `PackRuneFor` takes that serial first while it is in the
pack and lands in the place or has not landed, and a label set by name holds no serial (`RunePinTests`). The two rune
cursors know a rune by its graphics (`IsRuneArt`), not its tile name.

Stage 2's comparisons: no shop is found by its title. The buy circuit's default list names titles, so it takes every
shop of each title (`ShopDisplayTests`); the survey and thread reports name shops by `UoRegions.Display`. A rune's
label still suggests a shop or a landing for a rune never recalled by (`ReadRuneLabels`, `ApprovedShops`,
`GuessLanding`), as the rule allows.

Stage 3: every vendor record is keyed by serial (thread-off, vendor homes, the circuit record, attempts, offers,
broken marks); `thread-off`/`thread-on` take a serial (8 hex digits) as well as a first name, which takes every
tailor of that name. A vendor is still addressed by first name in speech, and `NearVendor` finds the nearest of the
name, as the server's own hearing does.

Stage 4: a forge (`IsForge`, the blacksmith.m set), a shovel (`DigTool`, rundir mining.m), a rune on the ground and the
house sign are their graphics (`KindsByGraphic`). A door is an art the 1.25 TILEDATA flags a door (`IsDoor`: the 302
arts of `DoorSets`, gates among them; the window's house doors and `Sight` use it, as the stash spots do); a pack bag
is a container that is no book (`PackBag`: the bookcases 0x0A97-0x0A9C and the spellbook 0x0EFA are not), one rule for
the pack's three bag walks; the house scan takes its loom and spinning wheel from `Looms` and `Wheels` (`KindTests`).
Open: the typed kind of a resource count.
