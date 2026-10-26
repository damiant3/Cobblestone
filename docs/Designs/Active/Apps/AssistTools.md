# Assist tools: a toolbox and a toolbag per profession

The UOAIX Assist's tools come from two containers per profession: a toolbox, the big store, and a toolbag, the tools
carried on the road. The bank's toolbag goes. Owner: blu. It is built as `ITools` beside `IInventory` in stage 2 of
`AssistStructure.md`, under `apps/uoaix/assist/assist-csharp.md`.

## Rulings (Damian, 2026-10-21)

- "we need a toolbag for each skill, at least for mining and for tailor. we need a shop toolbox and a bag toolbag".
- "the bank toolbag should be disolved. we don't actually use it, and its not good for the bank anyways".
- "the toolbox is any container near the work by practice. by code, it could be anywhere."
- "the toolbag i imagine is the same bag you carry always for tools. they could be different for each profession, but
  could be shared. same technically with toolbox."
- "the idea of the box is it is the big store, the bag the ones you take on the road."
- "in the case of mining, there really is no tools needed at home, its all on the road. for tailor, its mainly at home,
  but you should take a few kits on the sweeps."
- "for the tailor the box is likewise anywhere, but if in the house, where the kit is needed, then you can use it
  directly instead of the ceremony of putting into your bag first."

## Design

- **Professions:** mining and tailoring first; each other production loop or circuit joins by naming its tools.
- **Toolbox:** a container picked by target cursor, one per profession; two professions may pick the same one. Its
  place is wherever it lies, any city, any house; nothing assumes the bank.
- **Box and bag stay apart** (Damian, 2026-10-22: "box and bag, seperate, bag stays with you, box stays put. box
  usually in house, though we can move it to field. bag stays in pack, though we can move it to the field too";
  "a box holds the bulk supplies, the bag for the doing the work away from the shop (or just out of reach of the
  box)"). The player moves either one; no circuit lifts or drops a box or a bag.
- **Toolbag:** a container picked by target cursor, one per profession, shared allowed, carried in the pack.
- **A box in the field:** the mining toolbox put down in the cave is the cave's shovel store; down to 1 shovel, the
  circuit walks to it and tops the bag up.
- **Road counts:** each profession sets how many of each tool its bag carries (mining shovels and pickaxes, 10 as
  Damian's example; tailoring sewing kits, 4, `KitsWant`). A refill tops the bag up from the box to those counts, at the box,
  only when the box is the trip's next stop or within reach; no trip is made to the box for a tool the bag still holds.
- **Direct use:** a tool needed where its box lies within reach (the tailor's box in the house that sews) is used from
  the box itself, with no move into the bag first.
- **Bank toolbag removed:** the bank box's upper-right toolbag (`Toolbag`, `ToolsFromBank`, `banktoolbag`, the reset's
  entry) is deleted; each caller of `ToolsFromBank` takes from its profession's toolbox instead, and a task short of a
  tool and away from its box keeps whatever other source it has today (a purchase, a tinker) or stops and says which
  tool and which box.
- **Window:** each profession's section shows its toolbox and toolbag (picked, or not), their tool counts, and the road
  counts to set.

## Built

`Tools.cs` (`ITools`, `ToolPlaces`, `Profession`) over `Inventory.cs` (`IInventory`); the professions are mining,
tailoring, smithing (the hammer) and tinkering (the tinker's tools), each picked on the Bags tab with its road count.
The bank toolbag and `ToolsFromBank` are gone. At a circuit's start only its own professions' bags are topped up, each when its box is within reach (mining: shovels;
smithing: the hammer and the tinker's tools; the tailoring circuits: kits; Damian, 2026-10-22: "the tongs are for
smithy, and useless here");
the tailoring loop and the homespun home stop use a kit straight from a box within reach, and the home stop walks to a
tailoring box lying in the house.

## Open

- A refill trip to a box out of reach (the mining box in another city) is not built: today a refill happens only where
  the box is already within reach.
- Whether a tool short at the road's end is bought or fetched from the box is the circuit's call today per tool; the
  refill rule above keeps that call and only adds the box as a source.
