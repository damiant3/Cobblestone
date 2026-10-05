# Instrument skills and profession kits

`Skills` implements the native stage K rules. `sk-new capacity` allocates
one retained state for up to 4096 characters and 128 placed NPC trainers.
The owner maps immutable character identities to returned local indices.
Allocate the component below request scratch and serialize access.

`sk-admit world verifiedNew identity profession place` admits a new identity
once. Each profession receives its lowest-tier blessed instrument, one jerky
and zero hunger (a full belly). The chosen parent starts at 200; every other
parent and every instrument proficiency starts at zero. No coin or reagent
grant exists. The verified
flag means the caller checked new-character authority and committed the
matching inventory creation. It is not a field clients can supply.

This component owns instrument proficiency and the equipped instrument set,
not the world's full inventory, purse or item serial table. It permits one
carried instrument of each kind. `held`, `blessed` and `wear` must be bound
to the corresponding server-owned inventory objects. Ordinary duplicates
remain in external inventory until selected. The current GameSession login,
economy aggregate stock, combat and checkpoint codecs do not yet call these
rules. No live-client or crash-recovery claim follows from the native proof.
An image owner must persist skill state and inventory changes together before
enabling these actions. No old checkpoint is silently reinterpreted.

## Catalog and tiers

`SkillCatalog` supplies these initial simulation defaults. Every named
instrument has a separate proficiency. Numeric job ids match the instrument
ids below, except the pickaxe also performs job 1 (common ore).

| Profession / parent id | Blessed starter (instrument id) | Crafted instruments (id, tier, distinct job) |
|---|---|---|
| Mining 1 | Shovel 1 | Pickaxe 2, tier 2: big/rich ore |
| Carpentry 2 | Saw 3: boards | Chair joinery 4, tier 2; table joinery 5, tier 2; lathe 6, tier 3: legs/bowls/spindles; plane 7, tier 2: finishing |
| Smithing 3 | Stone hammer 8: rough metal | Smith hammer 9, tier 2: fine metal |
| Leatherworking 4 | Leather knife 10: cutting | Awl 11, tier 2: stitching |
| Tailoring 5 | Needle 12: sewing | Shears 13, tier 2: shaped cutting |
| Farming 6 | Hoe 14: small plots | Plow 15, tier 2: fields |
| Fishing 7 | Rod 16: line fishing | Net 17, tier 2: net fishing |
| Lumbering 8 | Hand axe 18: small timber | Felling axe 19, tier 2: large trees |
| Cooking 9 | Cooking knife 20: preparation | Pan 21, tier 2: frying |
| Herbalism 10 | Trowel 22: ground herbs | Pruning knife 23, tier 2: woody growth |
| Swordsmanship 11 | Longsword 24 | Katana 25 and viking sword 26, tier 2: their own weapon techniques |
| Archery 12 | Bow 27 | Crossbow 28, tier 2 |
| Alchemy 13 | Mortar 29: grinding | Alembic 30, tier 2: distilling |
| Magic 14 | Wand 31 | Staff 32, tier 2 |

Tier 1 requires no parent proficiency, tier 2 requires 200 and tier 3
requires 500, on a 0..1000 scale. Starters are tier 1 and have no wear loss,
breakage roll or tool-failure roll. A blessed tool still cannot do another
tool's job or bypass resource, spell, combat or station admission.

`sk-receive-crafted` installs an ordinary tool with finite durability only
after a verified transfer. The caller must have verified a real crafted item,
its provenance, recipient ownership and atomic removal from the prior holder.
The flag is not proof of material production and this function does not craft
items itself. `sk-use` refuses an unavailable tool, wrong job or unmet tier;
an admitted use practices only that instrument and spends one ordinary wear.
At zero wear the instrument breaks. The caller derives job and admission from
the target and world rules, never from a claimed client capability.

`sk-death` clears ordinary carried instrument metadata and jerky while keeping
the blessed starter. The owner must move the ordinary objects to its corpse
or drop destination in the same transaction; clearing metadata does not
destroy world objects. `sk-revive` restores only the alive flag, never another
kit or meal. Dead characters cannot practice, use tools or teach.

## Practice and masters

`sk-parent-practice` records one separately admitted parent-skill practice.
`sk-practice` records one separately validated practice event for a held,
working instrument, capped
by both parent proficiency and the instrument's training ceiling. Do not call
it again after `sk-use`, which already applies the practice. It does not
validate a target job or tier and does not spend wear; ordinary tool work
must use `sk-use`, not call this lower-level recorder as an action endpoint.
Requests do not
receive arbitrary skill amounts. The owner verifies the actual event and
prevents replay. Parent practice never advances a different instrument.

Untrained instruments stop at 200. Master training unlocks a ceiling of
1000 but grants no proficiency; continued practice is still required.
`sk-effective` returns the lesser of instrument and parent proficiency.
Combat/production consumers must use this value for performance rather than
the parent alone. Defaults are simulation policy, not historical UO values.

The keeper edits NPC teacher instrument, location, mastery and enabled state
through `sk-trainer-set`. Mastery must be at least 900. `sk-train-npc` checks
the live placement and the learner's location. `sk-train-player` additionally
requires a living, distinct player with effective instrument mastery of at
least 900 and verified mutual consent. Payment and encounter admission belong
to the caller. The native model uses place ids; the live adapter must verify
distance, line of sight and the teacher's current presence before admission.

`rare-places` is keeper-owned world data. Viking sword training defaults to
place 9, Buccaneer's Den. Both NPC and player teaching obey the restriction.
`sk-rare-place` changes that policy under keeper authority; zero removes a
restriction. A keeper can move or disable an NPC trainer without changing
the learner's already-earned proficiency. Clients cannot provision mastery.

## Cost and verification

Storage is fixed per configured character plus a fixed trainer table.
Identity admission scans used characters; action lookup, practice, training
and use perform constant work. Death scans 32 instrument slots. No action
grows a history or retains request pointers. The owner reclaims transient
request wrappers at its scratch boundary. Compiler heap/time is unchanged.
On 2026-10-04, the normal and poison proofs each measured 57,568 retained
bytes for capacity 32 and zero additional retained bytes across 2,000 uses
with the caller's per-operation scratch boundary.

`proofs/SkillsProof.codex` grades every starting kit, duplicate refusal,
common/big ore, normal wear, repeated blessed use, death/revival, carpentry
jobs, isolated sword proficiency, parent and training ceilings, NPC/player
masters, rare placement, keeper authority and unadmitted actions. It prints
retained allocation and repeated-use measurements. Run normal and poisoned
allocation modes; both must print `UOAIX SKILLS failures=0`.
