# Character stylist (UOAIX-54)

One rule dresses every server-made character by role: body (gender), skin
hue, hair and beard with hue, clothing with hues, and the held tool. The look
is a pure function of role and serial, so a restart redraws it; the worn
items are ordinary world objects and persist with the world.

## What exists (measured 2026-10-06)

- Every human NPC is made by `gs-mobile 0 x y z 400 1002` (`GameSession.codex:105`):
  shopkeepers `bb-populate-at` (`BritainShops.codex:111`, called from
  `CompositeLineup.codex:50`), the banker (`GameMobileBank.codex:102`),
  townsfolk `cgl-plain` (`CompositeLineup.codex:56`), guards
  `cv-populate-guards` (`CivicLiveState.codex:66,88`), the fighter `cgf-spawn`
  (`CompositeFighter.codex:93`, respawn `:410`), the gatherer and the four
  miners `cgg-new-at` (`CompositeGatherer.codex:55`, respawn `:255`; miners
  via `cg-found-miners`, `CompositeGameRules.codex:393-401`), and the sparring
  NPCs (`GameCombat.codex:623`).
- Lord British is an account character, not a spawned NPC (`cg-british-serial`,
  `CompositeGameRules.codex:431`).
- A worn item is a kind-2 world object whose container is the mobile, with its
  layer in the world layer index and `GsiItem` (`gsi-register`,
  `GameSkillsItems.codex:36-48`). `gmb-clothe owner graphic hue layer`
  (`GameMobileBank.codex:23-30`) equips one item idempotently per layer
  (layers 1-25, registered non-movable).
- The 0x78 equipment list is already appended for every registered layer by
  `gmb-rewrite` (`GameMobileBank.codex:71-77`) on every reply that passes
  `gmb-reply`. A registered item is drawn with no packet work.
- Today's dress is one fixed set in four copies: `gmb-dress`
  (`GameMobileBank.codex:31`), `cg-vendor-dress` (`CompositeGameRules.codex:201-207`),
  `cbd-dress` (`GameCombatDress.codex:9-29`), `bb-materialise`
  (`BritainShops.codex:70`), and the guards' `CvDress` rows
  (`CivicLiveState.codex:34-45`).

## Constraints the build must keep

- `gmb-find-banker` finds the banker by graphic 400 and hue 1002
  (`GameMobileBank.codex:~95`): a female or tinted banker breaks it. Find the
  banker by its registered serial instead before restyling it.
- `CivicLiveCodec.codex:62` validates the guards' exact layers; restyled guards
  change that codec's check with them.
- A respawn makes a new serial (`CompositeFighter.codex:410`,
  `CompositeGatherer.codex:255`), so a respawned NPC gets a new look. That
  matches "a new character"; keep it unless Damian rules otherwise.
- Restyling on boot must stay idempotent: reuse `gmb-clothe`'s per-layer rule.
- An NPC has no paperdoll today: double-clicking one answers "You cannot use
  that item." (`GameSkillsItems.codex:274`). The paperdoll branch needs a name
  source other than a player character slot.

## Lord British

The 1.25 client's Lord British piece is art 0x2042: unnamed in its
`tiledata.mul`, flags 0x08400000 (the costume family of the death shroud
0x204E and the GM robe 0x204F), and its animation id is 990 (0x3DE): an equipment animation drawn over a
human body, armor and crowned head together, and its paperdoll gump 50990 is the whole figure. Body 990
used as a body has no head and no paperdoll body gump (F47). British is body 400 wearing 0x2042.

## Choices left to the build

An item's wear layer is its `tiledata.mul` static entry's quality byte (byte 5 of the
37-byte entry after the 512 land blocks of 4 + 32 x 26 bytes). Measured in the 1.25
client (2026-10-06), art and layer: shirt 0x1518 and fancy shirt 0x1EFE 5; long pants
0x1539 and short pants 0x152F 4; shoes 0x170F, boots 0x170B, sandals 0x170E 3; hats
(cap 0x1715, straw 0x1717, wizard's 0x1718, feathered 0x171A, bandana 0x1540) 6; Long
Hair 0x203C 11; beards (short 0x203F, long 0x203E, goatee 0x2040, mustache 0x2041) 16;
half apron 0x153B 12; full apron 0x153E, doublet 0x1F7C, tunic 0x1FA1, surcoat 0x1FFD 17;
cloak 0x1515 20; robe 0x1F04, plain dress 0x1F01, fancy dress 0x1F00 22; skirt 0x1531
and kilt 0x1537 23; smith's hammer 0x13E4, pickaxe 0x0E85, longsword 0x0F60 1;
hatchet 0x0F44 and quarter staff 0x0E89 2.
Where Incognito keeps the original look and name for
the restore are open design choices for the build.

## Plan

1. Built: `CharacterStylist.codex`. `cst-look role serial` answers body 400/401,
   skin and rows of graphic, hue and layer for the roles townsfolk, shopkeeper,
   smith, banker, fighter and miner; `cst-style items owner role` sets body and
   skin and wears each row, replacing a different item already on that layer
   (`cst-wear`), so an NPC in the old fixed dress converts. Graded by
   `proofs/CharacterStylistProof` (5 arms; a layer-keeping sabotage turns the
   restyle arm red).
2. Shop vendors are wired: `cg-vendor-dress` styles shop 1 (`bb-role`) as the smith and the
   rest as shopkeepers (`proofs/CompositeHelpProof`, `CompositeBritainWorld` 41/41). Their
   `bb-materialise` clothes are economy lots; a restyle changes the lot item's graphic,
   not its economy item. The banker is wired in `cg-bank` (doublet, fancy shirt); it keeps
   body 400 and skin 1002 because `gmb-find-banker` recognises it by both. The bank
   lineup's two plain NPCs (`cgl-plain`) are styled as banker and townsfolk, and `cgl-find-tile`
   finds body 400 or 401. The fighter (`cst-fighter`), the gatherer and the four miners
   (`cst-miner`, pickaxe in hand) are styled where they are made and remade (`cgf-spawn`,
   `cgf-revive`, `cgg-new-at`, `cgg-revive`) and again at every boot (`cg-worker-dress` in
   `cg-install`), so a world founded before the stylist converts; `cst-styled` is the
   per-serial check (`proofs/CompositeGathererProof` every boot, `CompositeFighterProof`
   the revived fighter and the raised gatherer).
   Creation sites of human NPCs on the live server, and whether the stylist dresses them
   (F35): shopkeepers and the demo vendor yes (`cg-vendor-dress`); banker yes (`cg-bank`);
   lineup yes (`cgl-plain`); fighter, gatherer and miners yes; TownLive residents yes (`tl-look`: Nell and Mira women, Jorin a man; their lot clothes are restyled in place, so TLC1 keeps validating; `cg-resident-dress` at boot); guards and the mayor yes (`cst-guard`, `cst-mayor`, over their `cv-outfit` lots;
   `cv-populate` has no caller on the live server, only `proofs/CivicLiveReplay`); sparring NPCs yes (`cb-fixture` gives the fighter look's body and skin before the first draw, because `gmb-rewrite` keeps a 0x78's header; `cbd-dress` adds the garments, skipping a fixture that already wears hair).
   No spawn profile has a human body. British yes: `cg-british-dress` (in `cg-british-character`, every boot)
   sets body 400 and wears 0x2042 (tiledata layer 22) only when layer 22 is empty, so a robe he puts on is
   kept; `cb-form` treats every body from 400 up as human (anim.mul's 175-entry layout). Known gap: a dead
   British leaves the robe in his corpse and resurrects with the body his combat actor recorded, until the next
   boot redresses him (`proofs/BritishCharacterProof`).
3. Built: double-clicking a human NPC (`cb-form` 0, not a player character) opens its paperdoll (0x88,
   flags 0), named by the viewer's label for it (`cg-npc-label`: a shopkeeper, banker, resident, miner or
   the farmer by trade until asked, UOAIX-56), any other NPC unnamed (`cg-npc-paperdoll`). A vendor still opens
   its buy menu and a banker the bank box on double-click.
4. Built: Incognito (spell 35, `MageryIncognito`): a random skin, the stylist's hair and beard art with
   random hues on the hair and beard the caster wears (none grown or shaved), and a random name by gender
   from `NpcNames`, for 6 x Magery / 50 + 1 seconds, at most 144. The name replaces the caster's in click
   labels, paperdolls and status (`mgp-disguise`); viewers redraw through `GameClientView`'s 0x78 on a skin
   change. It ends on the caster's own pulse after expiry, and at death (hair and beard back, the ghost
   keeps its hue); MGM2 persists it, so a restart mid-spell restores the original look and name
5. Grade: a proof that the same serial yields the same look across a restart;
   a real-client walk past every Britain NPC and British seen by another
   character (root's TESTPLAN).
