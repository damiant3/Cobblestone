# Assist houses: many houses, public and private

Status: in progress (blu). Built: registration and the per-house ring (items 1 and 3 below;
`AssistFeatures.HouseRegister`, `houses-<server>.txt`, the Houses tab (the door's protections beside it; the Vendor tab picks its vendor by house), the control port's `houses`,
`house-register <vendor> <name>`, `house-mode <public|private> <name>`, `house-forget <name>`). Weaving inside a
registered private house takes thread from its floor once the pack has none and puts each bolt on the floor's one bolt
stack (`SafeHouse`, `OnFloor`, `FloorBolt`). While no house is registered the one-house settings below still decide
the door. Not built: the role table (2), stores by kind (4), the
sorting job (6), and item 1's castle recall spot.

## Damian's direction (2026-10-21)

- "i discovered tonight we can create as many houses as we like" (uo1998: Moonshot placed a clothier's shop,
  multi 0x406C, at 4698,1112 in Moonglow; Kwik's first house is at 2479,73).
- "circuits could become multi house aware. i just manually moved all that stuff for example. would be nice if I had
  a big house with lots of junk to automate that."
- "we can put these houses into "public" and "private" modes. whereby we leave the door unlocked and keep nothing of
  value, or private where the door remains locked and the various protocols are used and the advanced ones available."
- "we will store all our ingots and thread and reagents here. anything of bulk"
- "the forge will remain open to the public": the forge house (Kwik's first, 2479,73, the mining forge) is public, so
  the mining circuit smelts there with no key routine and stores its ingots in a private house.
- "the player may have many so for each circuit they need to decide which one serves in what role for each thing. so
  the player has to register their house as a step, the code should scan the house for the features that make it
  stand out in a role like forge or loom. those are the only ones that matter afaik. the rest of the structures
  available are either just storage or maybe with optional useless devices."
- A castle's interior is a recall spot "you can be certain has no players in it, at least unless and until someone
  has/gets a mark on this inside": a capability, not paramount. Monsters trapped inside a house are another, "rarely
  useful".
- "every house should be setup with vendor and key on its own ring stored on the vendor and used carefully." and "a
  keyring with all the houses keys is a recipe for disaster.": no ring ever holds a second house's key, and the Assist
  never carries two rings at once. A ring stored in the bank (bank, walk through, return to the bank, from a secure
  banking spot such as a roof in Nujel'm, Trinsic or Britain) works but is "not a necessary or advisable solution":
  not built. The threat the per-house ring answers is a stalker waiting for a disconnect "right with the key in your
  hand"; "losing the key + the stuff in the house is worse than just losing the stuff", so the key routine keeps the
  key in the pack for the shortest time it can.

## The one-house settings (while no house is registered)

One home, found by `HomeNow` (`AssistFeatures.cs`) from the mining forge setting, the house vendor (`house.vendor`),
or the last seen position (`house.at`). One lock setting (`house.locked`), one key routine (`HomeEnter`/`HomeLeave`,
key on the vendor at `KeyX`,`KeyY`), one ingot stash (`house.stash`, `house.stash.boxes`), one thread overflow (`overflow.rune`, `overflow.pile`). Every circuit's walk crosses the door
through `HomeCross` in `WalkTo`; every recall checks the key first (`HomeKeyBeforeTravel`).

## What has to change

1. Registration: the player registers a house as a step. Registering scans the house (`UoMap.HouseAround`/`HouseDoors`
   for footprint and door, then the items and statics inside the footprint) for its role devices, a forge or a loom;
   every other device counts as nothing, and a house with neither is storage. A castle's enclosed interior is recorded
   as a safe recall spot. The record per house: footprint, door, vendor, mode, devices found, stores. Kept per server
   like the caves file; the single settings above become the first house's record.
2. Roles per circuit: each circuit names, for each role it needs (smelt at a forge, weave at a loom, store ingots,
   store thread, store reagents), which registered house serves it, chosen by the player from the houses whose scan
   qualifies them. The Assist never picks a house for a role on its own.
3. Every house has its own vendor and its own key on its own ring, stored on that vendor; registration refuses a house
   with no vendor or no ring on the vendor. Mode per house. Public: the door left unlocked, the ring stays on the
   vendor, nothing of value stored (the circuits refuse to store in it). Private: locked, the key routine (sweep, wait
   for nobody near, that house's ring off and back on that house's vendor), the ring of searches and other advanced
   protocols available per house. A ring never travels and never leaves its own house. A ring found in the pack before
   a recall is an emergency (Damian, 2026-10-22: "the circuit can never travel with the house key and is an emergency
   situation if discovered and the key should be put on the vendor to the house not for sale immediatly. if that is
   delayed by movement, or refused or not possible, then it should be put in the bank. the circuit can continue at that
   point."): on the vendor from inside, the doorstep or the key spot; anywhere else, or refused there, into the bank
   box, and the recall goes on (`HomeKeyBeforeTravel`, `HomeKeyBank`).
4. Stores per house by kind: ingots, thread, reagents, any bulk item; each a pile or container target as the ingot
   stash is now. A circuit storing a kind goes to the house its role table names for that kind; a full store (the
   container weight limit, "That container cannot hold more weight.") is skipped for the run.
5. `HomeCross`, `HomeKeyBeforeTravel` and the door's place in the walk planner apply to the house whose footprint the
   walk crosses, by that house's mode.
6. The sorting job: a circuit that empties a house's junk (piles and containers on its floor) into the stores by kind,
   so moving a house's junk into its stores is one button.

## Open questions for Damian

- Which item ids count as a forge and as a loom: the 1998 forge and loom statics and deeded items only, or more?
