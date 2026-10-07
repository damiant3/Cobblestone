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

Resisting Spells is armour against every harmful spell (UOAIX-77, Damian's design, not ServUO's): attack is
the caster's Magery and defence the target's Resisting Spells, each scaled by its owner's Evaluating Intelligence
(half at 0.0, whole at GM). First a full-resist roll (`mge-resisted`: 55% between equals, rising 4% per 10.0
points of defence lead to 95%, falling with the attack's lead to 0 with no defence) answers "You feel yourself
resisting magical energy." and nothing lands; otherwise the damage, a curse's duration and poison's ticks are
scaled by the landed share (`mge-share`: attack minus defence over attack, at least a tenth). Between GM equals a
cast lands on 45% of tries at a tenth, about 1/20 of an unresisted cast. The roll practises the target's
Resisting Spells. Creatures have no skill row, so their defence is 0 and nothing changes for them.

Paralyze (38, UOAIX-81) freezes its target for ServUO's pre-AOS (7 + 20% of Magery) seconds scaled by the landed
share: `CombatActor.frozen` holds the thaw tick on the combat clock. A frozen actor does not swing (`cb-batches`), a
frozen creature does not step (`gws-frozen`), and a frozen player's walk is refused and a cast from a book, macro or
scroll answers "You are frozen and cannot cast." (`mg-casting`). Any damage thaws (`mge-thaw`, in `mge-interrupt`).
The freeze is volatile: GameCombatCodec does not store it, so a restart thaws everyone.

Curse (27) lowers strength, dexterity and intelligence as Clumsy, Feeblemind and Weaken lower one each; Mass Curse (46)
curses every other admissible mobile within 2 tiles of its point, each rolling its own resist (`mga-mass-curse`). Mana
Drain (31) takes 1 to 100 of the target's mana, Mana Vampire (53) all of it, moving only what fits under the caster's
intelligence (`mga-drain`); the landed share scales each. Mana costs follow ServUO's circle table (4, 6, 9, 11, 14,
20, 40, 50).

Damage spells take ServUO's pre-AOS bases (`mge-spell-damage`): Magic Arrow 4-7, Harm 1-15, Fireball 10-16, Lightning
12-20, Energy Bolt 24-41, Explosion 23-44, Flame Strike 27-48, and Mind Blast half the gap between the caster's
highest and lowest stat (at most 45), each scaled by Eval Int, Magery and the landed share. Explosion's cast lights a
fuse on the target (`MgActor.fuse`, `fuser`) and the blast lands 2.5 s later from the pulse (`mgt-fuse`), so an Energy Bolt
begun as the Explosion's recovery ends lands on the same tick. The fuse is volatile: a restart drops it.

Greater Heal (29) heals 40% of Magery plus 1 to 10. The area spells walk the tile chains around their point
(`mga-area`): Arch Cure (25, 2 tiles) cures at Cure's chance less 1%; Arch Protection (26, 3 tiles, caster included)
gives this shard's Protection, not pre-AOS virtual armour, which combat does not model; Chain Lightning (49) and
Meteor Swarm (55) split 27 to 48 among the harmful targets within 2 tiles; Earthquake (57) strikes within 1 + Magery/15
tiles of the caster for 60% of each target's hits (at least 10 on a creature, at most 75), unscaled by Eval Int.

Energy Field (50) is Wall of Stone's placement (`mga-walls`, `mga-wall-last`) five tiles long in art 0x3946 (east-west) or
0x3956, impassable, for 28% of Magery plus 2 seconds (ServUO pre-AOS), sound 0x20B. Fire Field (28), Poison Field (39) and
Paralyze Field (47) use the same five-tile placement but stay passable and may be cast over mobiles: each pulse,
whoever stands in one is burnt for 2 once a second, poisoned at the caster's level, or frozen as by Paralyze
(`mgt-fields`), each through the resist roll and the landed share. A field's kind is read from its art
(`mga-field-kind`), so a restored world keeps it passable; its caster (`MgWall.caster`) is volatile, and a restored
field acts as from a caster with no skill. A paralyze field re-freezes whoever is still standing in it once they thaw. Dispel Field (34) removes the one field or wall tile it targets (ServUO Fifth/DispelField): it expires that tile and the next pulse's wall pass removes it.

Magic Reflection (36) stores 8 circles of reflection, 15 when Magery plus Inscription reach 200.0; a harmful spell
aimed at its holder spends circle + 1 and returns to its caster while the store stays at 0 or above (`mga-reflects`,
ServUO SpellHelper.CheckReflect). The store is volatile.

Invisibility (44) hides its target for Magery x 120 ms (ServUO pre-AOS 1.2 x Magery.Fixed / 10 s) by the same concealed
byte Hiding sets, so acting or being struck reveals it, and the caster's own body is drawn hidden; at expiry the holder
is unhidden (a player's in its own pulse). Reveal (48) uncovers every hidden player within 1 + Magery/20 tiles of its
point (pre-AOS always succeeds; a player caster reveals players only). Magery reaches the concealed byte through
`hide-hook` and `hidden-hook`, which the composite binds to ActiveSkills (`as-set-hidden`, `as-concealed-hidden`). The
Invisibility timer is volatile. A revealed player other than the caster is redrawn for others at once and for itself
on its next self update.

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
