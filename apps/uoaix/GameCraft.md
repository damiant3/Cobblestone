# Classic client crafting

GameCraft adapts Sphere Source-X PacketDisplayMenu, PacketMenuChoice and
Skill_Menu, under Apache-2.0. The7C writer and13-byte7D reader follow those
legacy packet layouts. Tool graphic facts also agree with the ServUO
SmithHammer, SewingKit, Saw and TinkerTools constructors; no implementation
from those classes is copied.

## Composite entry

Allocate `gcr-state harvest` before request scratch, using the composite's
shared GameHarvest/GvWorld/GsiState. Route `gcr-handler lookup state`
before general C06 use handling. Register `gcr-spec` for C7D fixed13;
C06 already belongs to the item registry. Keep bank-access filtering first.

`lookup map mobile recipeId category -> Integer` is the trusted workshop
adapter. It returns an authorized EpStation ID or0. Validate real workshop
position and permission, not client-supplied graphics. Smith finishing
requires nearby forge and anvil. Other recipes
require their declared workshop. Bind the station to the verified player's
production actor/place. ep-craft rechecks station kind, owner and enablement.
Lookup runs at menu opening and selection. The composite's adapter is
`cpr-craft-lookup` (CompositeProduction): smithing and tinkering stations
(kinds 3 and 9) bind inside the Blacksmith premises, textiles (4, 5, 11)
inside the Tailor premises, field recipes (kind 1) anywhere; carpentry has no
Britain workshop. Britain shops sell the openers and harvesting tools.

Tool openers are a produced stone hammer (item62, graphic13E3/13E4), or a
produced metal-tool variant (item42): sewing kit0F9D, saw1034/1035,
tinker tools1EB8/1EB9. Tools must be carried in the backpack or equipped,
and bank ancestry is refused at both phases. Recipes also require their
declared working tool; the catalog currently uses the hammer, knife or axe.
No menu grants a starter tool.

Blacksmithing exposes metal goods; tailoring exposes clothing;
carpentry exposes boards, furniture, bows and arrows; tinkering exposes
toolkits and jewelry/scissors. Recipe20 produces the tinkering-tool,
sewing-kit and saw variants with the same catalog material accounting.
The unchanged standard prefix is supported. With BritainCatalog's verified
extension, the menus use its dagger, longsword, gorget, shirt, pants and
scissors recipes. World items receive their applicable equipment layer;
actual equip support remains the item handler's contract.

## Smelting (Damian, 2026-10-07)

"refining iron ore should happen at a forge by using the ore on the forge";
"refining ore->ingots should not require fallen branches"; "it should not be a
smithy skill, it is mining"; "for now, a forge is a forge."

`GameSmelt`: double-clicking a pile of ore of any metal in the backpack asks
for a forge (ServUO Ore.cs, cliloc 501971). A target whose art is a forge
(0x0FB1 or 0x197A to 0x19A9) standing at the targeted tile within 2 tiles
smelts the whole pile on one Mining roll, two ore to the ingot, spending no
fuel; a failure burns away half the ore (501990). Any other target refuses.
The smith's hammer menu offers no smelting. `proofs/CompositeProductionReplay`
grades the smelt, the census, the Mining roll and gain, and the refusals.

## The craft gump

A tool opens the craft gump (`CraftGump.codex`, after ServUO Engines/Craft
CraftGump) instead of the 7C icon menu: categories from ServUO's Def files
down the left, one gump page each, the chosen category's items down the right
with what each makes and needs, a status line, Make Last and Exit. A pressed
button arrives as 0xB1 and `crg-answer` (in `cp-route`) rewrites it as the 7D
choice below, Make Last as choice 9999 (the request's `last`), Exit as 0. Each
showing takes a fresh menu context and lives two minutes; an attempt, a
cooldown or an unreachable workshop shows the gump again with that status.

## Admission and production

7D binds tool serial, connection and a single-use menu context. Selection0
cancels. Expired, repeated, out-of-range or changed-tool responses refuse
before production. Client model/hue fields never select the output.
A menu context expires after two minutes; accepted attempts have a
three-second cooldown. Working inputs must be real lot-backed items in the backpack.

The adapter selects exact quantities and quality/cost from those lots,
isolates them and the working tool in a private Ep candidate, then calls
ep-craft. Unselected stock is recombined unchanged. On success, selected
physical inputs shrink or retire and the output receives an exact GvLot.
Failures practice and wear the working tool according to ep-craft, without
consuming recipe materials. Overlapping input/tool/output types refuse
until explicitly supported; current exposed recipes have distinct types.
Output-capacity checks precede publication. Lot compaction invalidates
vendor quotes.

Classic skill rows reflect production skill groups: metalworking supplies
blacksmithing/tinkering, woodwork supplies carpentry/lumberjacking, and
tailoring uses the textile group. Skill gain, item quality and acquisition
cost come from production, not the menu packet. Quality is retained in the
lot and reported to the player. Generic splitting/merging of lot-backed
stacks remains refused, matching the harvesting/vendor contract.

Harvest output is graded, and grade is the only merge rule a lot-backed stack
has. Harvest quality (100 plus nine tenths of skill) falls into low (below
400), common (below 700) or fine (from 700), and the lot records its grade's
floor: 100, 400 or 700. A new yield merges into the player's backpack stack of
the same item, grade and wear, adding to the stack's amount and its lot's
quantity and basis, and only when the economy holds the units the merged lot
claims. Otherwise the yield starts its own stack. Quantity-weighted averaging
is refused: it would erase the grade a buyer pays for. The harvest message
names the grade.

World objects, GSI metadata, production, vendor lots and craft state are
one encompassing transaction. A late Err requires rollback of that whole
candidate; no reply may publish before commit. The adapter is not a store.

## Recovery and cost

UCC1 is112 bytes: magic/version/next-menu/reserved qwords, then five
player-serial/remaining-cooldown pairs. `gcrc-encode state now buffer
capacity` and `gcrc-decode state now buffer size` return Result Integer
Text. Restore world, production, vendor/player and harvest bindings first.
Decode validates before mutation, rebases cooldowns and clears all menu,
tool, connection and expiry authority. Integrity belongs to the owner codec.
A pair whose serial no longer holds its slot is written as 0/0, as in UHC1.

Retained state is five fixed requests. Material and tool search is bounded
by 128 lots (`gv-lot-limit`); candidate buffers and actor-reference copies use fixed economy
bounds. Parent checks use the existing depth bound. No request-owned buffer
is retained. Codec work is fixed-size.

GameCraftReplay covers all four menus, ore smelting, toolkit variants,
workshop departure refusal, exact material use, unselected thread quality,
failed attempts, skill/quality output, census/lot validity, stale menus and
cooldown recovery.
