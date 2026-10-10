# Mobile equipment and bank access

`GameMobileBank.codex` ports SphereServer Source-X commit
`dd28a0ad53258adb55b548b1bd4df989065a1fe4`, Apache-2.0:
`src/network/send.cpp` PacketCharacter, `src/game/clients/CClientMsg.cpp`
addBankOpen, `src/game/chars/CCharStatus.cpp` GetBank/CanTouch and
`src/game/items/CItemContainer.cpp` OnOpenEvent. Copyright belongs to the
Source-X contributors; [license](Sphere-LICENSE.txt).

The legacy 78 mobile packet contains real equipped world items. A nonzero
hue sets graphic bit 8000 and appends hue16; zero hue omits both. Each
visible layer is emitted once. Bank and vendor containers are excluded.
`gmb-mobile items mobile` supplies this writer; `gmb-reply items reply`
replaces equipment in 78 packets, preserving the mobile header and reply close flag.

## Composite binding

Allocate `gsi-new shard.world`, then `gmb-new items bankGump`, below receive
scratch. The bank gump is an owner-supplied container definition. The demo
uses 004A, the classic metal chest gump named in Source-X uofiles_enums.h.
The box graphic 09B2 and layer29 come from Source-X GetBank and item enums.
The demo's shirt, pants, shoes and colors are explicit shard fixture choices.

Register trusted banker mobile serials with `gmb-add-banker` (maximum16).
`gmb-dress items npc` creates and registers real immovable shirt, pants and
shoes; call once during world setup, within the owner transaction. On restore,
retain those world objects and recover their metadata instead of creating
new garments. Apply `gmb-reply` to the vendor pulse so its existing 78 draw
contains the registered clothing. This does not modify the vendor family.

Order handlers: `gmb-handle banks`, vendor handler, `gsi-handle items`.
Continue only on `Ok None`. Register `gsi-specs 0` plus vendor-only specs;
C03 speech already belongs to the core framing registry. Preserve the core
login/entry route. Run `gsi-after-entry` once after create/select, then bind
the vendor player to the resulting actor.pack; do not create a second pack.

Vendor composition requires the pack-only sale admission at both quote and
checkout. Owner-chain-only vendor admission is insufficient.
The replay quotes a purchased lot, deposits it, moves one tile while staying
near the vendor, and requires both stale checkout and fresh quote refusal.

Say `bank` or `banker bank` within two tiles and sixteen height units of a
registered banker. The range and speech bindings are this shard's policy.
The port lazily creates/reuses an immovable layer29 box, emits 2E before
24/3C, and records the opening position. Every direct or nested item-family bank access
checks owner, connection and that position before general item handling.
Source-X likewise checks the recorded opening position. Returning to that
same position on the same connection meets that rule; this is not a timed
bank session. Another connection and recovery have no authority. Deposits
and withdrawals reuse the existing atomic item handler. The surrounding
owner transaction must cover world objects and metadata before publication.
No coin minting, account balance service or interest is introduced.

## Item metadata recovery

[GSI1](GsiCodec.md) owns metadata encoding and recovery. Restore world, then
GSI1, then create a fresh bank context and register trusted bankers.
Bank permissions are volatile. The standalone GameMobileBankServer draws
a dressed banker at 1422,1698 for the real-client grade.

## Validation and cost

`proofs/MobileBankReplay.codex` grades legacy equipment fields, bank packet
ordering, nested deposits/withdrawals, movement and connection refusal,
foreign ownership and metadata recovery. Deposit/withdraw and
drag/drop/equip are ungraded in the real client. Bank coin accounting and
durable composite replay belong to the encompassing image owner.

Mobile packets walk the mobile's equipment and emit at most 25 equipment rows.
A request-local list of at most25 serials preserves the established slot
order using bounded insertion sorting, O(E squared) with E at most25.
Backpack and bank lookup traverse owner children rather than world capacity.
Bank ancestry walks at most65 objects; banker search is capped at16.
Metadata encode/decode are O(world capacity), with 40 bytes per slot on
disk and the existing GSI retained rows on decode. Five bank permissions
and sixteen banker slots are allocated once; requests retain no new lists.
