# Character stylist (UOAIX-54)

One rule dresses every server-made character by role: body (gender), skin
hue, hair and beard with hue, clothing with hues, and the held tool. The look
is a pure function of role and serial, so a restart redraws it; the worn
items are ordinary world objects and persist with the world.

## The rule

`CharacterStylist.codex`: `cst-look role serial` answers body 400/401, skin
and rows of graphic, hue and layer for townsfolk, shopkeeper, smith, banker,
fighter and miner; `cst-style items owner role` sets body and skin and wears
each row, replacing a different item already on that layer (`cst-wear`), so an
NPC in an older dress converts. `cst-styled` is the per-serial check. A worn
item is a kind-2 world object contained by the mobile and registered in
`GsiItem`; `gmb-rewrite` appends the 0x78 equipment list for every registered
layer, so a styled NPC is drawn with no packet work. Graded by
`proofs/CharacterStylistProof`.

## Who is styled, and where

- Shopkeepers and the demo vendor: `cg-vendor-dress` (shop 1, `bb-role`, is the
  smith). Their `bb-materialise` clothes are economy lots; a restyle changes the
  lot item's graphic, not its economy item.
- The banker: `cg-bank` (doublet, fancy shirt), keeping body 400 and skin 1002
  because `gmb-find-banker` recognises the banker by both.
- The fighter (`cst-fighter`), the gatherer and the four miners (`cst-miner`,
  pickaxe in hand), where made and remade (`cgf-spawn`, `cgf-revive`,
  `cgg-new-at`, `cgg-revive`) and at every boot (`cg-worker-dress` in
  `cg-install`). A respawn is a new serial and so a new look.
- TownLive residents (`tl-look`: Nell and Mira women, Jorin a man), restyled
  in place over their lot clothes so TLC1 keeps validating
  (`cg-resident-dress` at boot).
- Guards and the mayor (`cst-guard`, `cst-mayor`, over their `cv-outfit`
  lots); `CivicLiveCodec` validates the guards' exact layers.
  Both are re-dressed at every boot (`cg-civic-names`).
- The Royal Minter and Silas the gambler, recognised by body hue, take the
  stylist's clothes only and keep their body (`cg-dress-bare` through
  `cst-wear-all`, at every boot in `cg-install`).
- Sparring NPCs: `cb-fixture` gives the fighter look's body and skin before the
  first draw (`gmb-rewrite` keeps a 0x78's header); `cbd-dress` adds the
  garments, skipping a fixture that already wears hair.
- No spawn profile has a human body.

Restyling at boot is idempotent (one item per layer, as `gmb-clothe`).
`proofs/CompositeBritainWorld` grades every shopkeeper and resident by serial
after a restart; `CompositeGathererProof` and `CompositeFighterProof` grade the
workers.

## Lord British

The 1.25 client's Lord British piece is art 0x2042: unnamed in its
`tiledata.mul`, flags 0x08400000 (the costume family of the death shroud
0x204E and the GM robe 0x204F), and its animation id is 990 (0x3DE): an
equipment animation drawn over a human body, armor and crowned head together,
and its paperdoll gump 50990 is the whole figure. Body 990 used as a body has
no head and no paperdoll body gump (F47). British is body 400 wearing 0x2042:
`cg-british-dress` (in `cg-british-character`, every boot) wears it on layer 22
only when that layer is empty, so a robe he puts on is kept; `cb-form` treats
every body from 400 up as human (anim.mul's 175-entry layout). A resurrected
British sheds the death robe and takes his costume back from his corpse
(`cg-british-unshroud` through the resurrection dress hook;
`proofs/BritishCharacterProof`).

## Paperdolls and Incognito

Double-clicking a human NPC (`cb-form` 0, not a player character) opens its
paperdoll (0x88, flags 0), named by the viewer's label for it
(`cg-npc-label`, UOAIX-56), any other NPC unnamed (`cg-npc-paperdoll`). A
vendor still opens its buy menu and a banker the bank box.

Incognito (spell 35, `MageryIncognito`): a random skin, the stylist's hair and
beard art with random hues on the hair and beard the caster wears (none grown
or shaved), and a random name by gender from `NpcNames`, for 6 x Magery / 50 +
1 seconds, at most 144. The name replaces the caster's in click labels,
paperdolls and status (`mgp-disguise`); viewers redraw through
`GameClientView`'s 0x78 on a skin change. It ends on the caster's own pulse
after expiry and at death (the ghost keeps its hue); MGM2 persists it, so a
restart mid-spell restores the original look and name.

## Wear layers (client facts)

An item's wear layer is its `tiledata.mul` static entry's quality byte (byte 5
of the 37-byte entry after the 512 land blocks of 4 + 32 x 26 bytes). In the
1.25 client: shirt 0x1518 and fancy shirt 0x1EFE 5; long pants 0x1539 and
short pants 0x152F 4; shoes 0x170F, boots 0x170B, sandals 0x170E 3; hats (cap
0x1715, straw 0x1717, wizard's 0x1718, feathered 0x171A, bandana 0x1540) 6;
Long Hair 0x203C 11; beards (short 0x203F, long 0x203E, goatee 0x2040,
mustache 0x2041) 16; half apron 0x153B 12; full apron 0x153E, doublet 0x1F7C,
tunic 0x1FA1, surcoat 0x1FFD 17; cloak 0x1515 20; robe 0x1F04, plain dress
0x1F01, fancy dress 0x1F00 22; skirt 0x1531 and kilt 0x1537 23; smith's hammer
0x13E4, pickaxe 0x0E85, longsword 0x0F60 1; hatchet 0x0F44 and quarter staff
0x0E89 2.

## What the 1.25 client draws (measured)

Measured 2026-10-07 from the client Damian plays (`C:\Users\Damian\uo1998-client`,
byte-identical to `D:\Projects\uoaix-client`). Every wearable art id the server
names in `apps/uoaix` has its male paperdoll gump (50000 + animation id) and, for
every body-drawn layer, its body animation (ANIM.IDX entry 35000 + (animation -
400) x 175). Backpacks, the necklace 0x1085, the ring 0x108A and the platemail
gorget 0x1413 have no body animation and draw on the paperdoll only; weapons,
tools and shields have no female gump and the client uses the male one.

HUES.MUL holds 3000 hues. A hue is rough (drawn as coloured noise) when its
32-colour ramp changes brightness direction more than four times: 248 hues,
in ranges from 0x0423, just past the skin tones (among them 0x0423-0x044D,
0x0481-0x0485, 0x0487-0x048B and 0x0495-0x04AD, up to 0x06A4). Every hue the
stylist picks is smooth: dyes 2 to 1001, skins 1002 to 1058, hair 1102 to
1150, the guard hue 0x035F and the mayor hue 0x0016. The mayor's former 0x0481
and the minter's former body hue 0x0496 are rough and drew as noise (Damian,
2026-10-07); the minter's skin is now 1003.

## Ruling: every NPC is fully attributed

Damian, 2026-10-08: "we need the stylist to ensure npcs are properly attributed with name, profession, appropriate clothing, and mind that meets the character's roles in the world. too many npc show up with no name, no title, etc.", then "oh and items for their profession". Every server-made human NPC carries a name, a title naming its profession, clothing that fits its role and sex, the items of its profession (in hand or pack), and a mind (the behaviour and speech kind) that fits its role. A boot pass finds every human NPC missing one of the five and repairs it. Damian, the same day: "there is not single case where `"opponent`" is appropriate in game for a character or corpse or status bar"; no name shown in game falls back to a generic word: the combat actor, status bar, paperdoll and corpse take the NPC's own name.

## Open

A real-client walk past every Britain NPC, and British seen by another
character (root's TESTPLAN).
