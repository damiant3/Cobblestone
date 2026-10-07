# Live classic Magery

## Resting state

Magery is on main (35663) in the composite server, and Damian graded it in
the real client. `mgs-skill` reads `cb-skill`, so a live skill provider
other than combat's must be bound there before Magery claims skill gains.

The handler implements circles 1-3, Recall32 (a fourth-circle spell), Incognito35 (fifth circle) and Mark45 (sixth circle):
spellbook opening, carried spell definitions, pack reagent consumption,
mana, casting delay, owned target cursors, effects and interruption fizzle. A spell scroll (`mgd-scroll` art) casts from its user's pack with no reagents, rolled two circles easier, and one is spent only when the cast lands (ServUO SpellScroll, MagerySpell.GetCastSkills); dropped on a carried book that lacks its spell, it writes the spell in and is spent (Spellbook.OnDragDrop).

## Source and modifications

Modified Codex ports of SphereServer Source-X, copyright 2026 SphereServer
development team, Apache-2.0, and ServUO, GPL-2.0, copyright its contributors.
References are under D:/Projects/uo-reference. Licenses:
[Sphere-LICENSE.txt](Sphere-LICENSE.txt),
[ports/ServUO-LICENSE.txt](ports/ServUO-LICENSE.txt).

Source-X src/network/receive.cpp and src/game/clients/CClientEvent.cpp
supply EXTCMD_CAST_BOOK, CAST_MACRO, OPEN_SPELLBOOK and target dispatch.
src/network/send.cpp supplies legacy container/spellbook, mana, effect and
sound packet layouts. ServUO Scripts/Spells/First, Second, Third and
Fourth/Recall.cs and Fifth/Incognito.cs supply effects and reagent lists; Spells/Base/Spell.cs,
MagerySpell.cs and SpellHelper.cs supply classic sequence/timing, skill
checks and modifiers. Poison and the pre-AOS BaseMeleeWeapon reactive-armor
path supply periodic damage and absorption. No upstream code is executed.

Codex records replace upstream objects. Indexed custody replaces Backpack
traversal. PIT conversion uses pit-input-hz and pit-count, never an assumed
1kHz clock. Damage delegates corpse/death handling to the combat owner.
The owner supplies map coverage and commits before publishing replies.

## Composite integration

Allocate `mgs-new items bank combat decoration vendor cover` before the
owner's request heap mark. All services must refer to the same indexed
WorldTable. Supply `Just vendor` for the real economy; `None` is only for
an explicitly isolated fixture. The cover callback has signature
`Integer, Integer, Integer -> [Device.Block] Result WalkMap Text` and must
load the destination and call wd-rebind before returning. Refused travel
restores source coverage. Codec recovery requires a bound collision window.

Call mge-bind after construction/recovery to install the combat damage hook.
It interrupts casting on damage, applies reactive armor and clears effects
on death. The default hook preserves melee behavior without Magery.

Compose `mg-handler magic` before general GSI use/target handling, inside
the bank authorization boundary. Existing06,12 and6C framing suffices;
unclaimed12 selectors and other target namespaces return None. Call
`mg-after-command magic shard input packet reply` after successful commands
to handle movement/equipment/lift/skill interruption and status presentation.
Combine `mg-pulse magic` with pulses; its final argument is raw PIT time.
The client view drops handler 0x11/A1-A3 self updates and emits its own self
status, so the composite passes that status through `mgp-rewrite`
(`cg-status`); `proofs/CompositeSpellStatus.codex` grades the shown stats.
Apply mg-entry after inventory entry and mg-disconnect during disconnect.
All output is GameReply packets; these modules perform no raw sends.

Trusted creation calls `mgs-book magic serial mask` for a real0EFA item and
`mgs-rune magic serial x y z` for a real1F14 rune with an admitted destination.
The supported testing mask is hexadecimal100480FFFFFF. The composite marks the
testing kit rune once at grant, at the new-character tile 1420,1698
(`cg-kit-rune`); Mark45 marks any recall rune in the caster's backpack at the
caster's place (`mga-mark`), and Recall then takes the caster there. Unregistered books open
empty, so Fester must register the testing kit explicitly. Do not refill or
re-mark restored objects at every entry. Register lock/trap-capable ordinary
containers with mgs-container; bank boxes refuse. Reserve fake book-entry
serials41000001..41000040 and target context prefix4D47.

Val's skill effects share `mgs-ensure magic shard serial -> Result MgActor
Text`: actor.mana is authoritative; poison=-1 is healthy. Timer changes call
mgs-track, and mgs-touch marks persistent state dirty. mgp-mana emits A2.
Do not add a second mana pool. Hidden-mobile visibility belongs to GameClientView.

## Resources and persistence

Preflight the complete reagent set before consuming any item. Carried book
and reagent searches follow pack descendants; bank/foreign custody cannot
satisfy them. Completion rechecks requirements. A successful sequence uses
one unit of each reagent and classic4/6/9/11 mana. Skill fizzle consumes
reagents; movement/damage interruption before completion does not. Owned
target contexts cannot be replayed for another debit. Item movement and
spell costs belong to one encompassing owner transaction.

For tracked reagents, the lot actor must match the player and physical
quantity must match the lot. Consumption reduces stock, quality, wear and
proportional basis, increases consumed quantity and invalidates vendor
quotes. The final unit retires lot and item. Testing items without lots
remain explicitly untracked fixture stock.

The composite persists MGM2 as UCC1's magery section (CompositeGame.md);
`proofs/CompositeMageryRestart.codex` grades a spellbook mask, a rune
destination and mana across three boots, including a commit before attach.
MGM2 has64 header bytes,304 actor bytes and64 item bytes per world slot,
then32 wall rows of16 bytes: `mgc-size magic = 576 +368*capacity`. Actor bytes 240-295 hold an active Incognito: remaining time, name, and the original skin, hair and beard art and hues; a restart mid-spell restores them at expiry. A disk holding MGM1 refuses to load (a fresh world is required).
Use mgc-write/read with exact extent and raw PIT time. Restore world/index,
GSI, combat and decoration first. Header binds version, capacity, PRNG seed
and decoration identity. Actor rows preserve base stats, mana, modifiers,
poison, defenses and remaining durations. Item rows preserve book masks, a lockable container's key id (bytes 56-63, zero for none),
marked runes and enchantments; walls bind real0082 items. Recovery rebases
durations, clears casts/connections, rebuilds active actors/collision and
reinstalls the combat hook. The owner supplies callbacks; they are never
decoded. An error aborts the encompassing load; never serve partial recovery.

## Evidence, cost and limits

proofs/MageryReplay.codex checks every supported effect and exact costs,
legacy book/mana bytes, bank refusal, forged/repeated targets, movement,
damage/disconnect interruption, canonical MGM2 rebasing and invalid-mana
refusal. The tracked-lot fixture checks partial basis, final retirement and
the economy census. Evidence: build-output/uoaix/magery-ledger.stdout,
seed4228CD5103DC4523. Combat default-hook regressions are under
build-output/uoaix/magery-combat. Full battery and poison are excluded by
UOAIX section9, not claimed as passes.

Retained state is O(world capacity): actor/item records per slot,96-byte
modifier and200-byte equipment arrays per actor, an8-byte active slot array
per capacity, and32 wall records. Allocate once before scratch compaction.
Indexed lookups are O(1); pack searches are O(pack descendants), depth<=64.
Pulses scan active actors and32 walls; equipment checks examine25 layers.
Expired fields rebuild the bounded map overlay. Codec work is O(capacity +
map cells + decoration rows). Packets/lists are scratch; there is no GC.

LOS uses an integer half-cell ray. Wall placement requires all three cells
to be available. Mana regeneration lacks armor penalties. Generic containers
are not automatically enchanted, created food lacks a use handler, and
spells beyond this profile refuse. Native proof is not a client grade.
The complete image must bind the kit, codec and callbacks before grading.
