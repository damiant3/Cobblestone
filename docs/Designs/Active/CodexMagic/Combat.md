# Combat — Attack, Block, and Damage

## Combat Phase Flow

```
Begin Combat → Declare Attackers → Declare Blockers → Combat Damage → End Combat
                                                    ↗
                                   First Strike Damage (if applicable)
```

## Declare Attackers

Active player (the attacker) chooses which creatures attack:
- Must be untapped (unless vigilance)
- Must not have summoning sickness (unless haste)
- Must not have defender
- Taps as part of declaring (unless vigilance)
- Attacks target the opposing **General**, not the player directly

```
AttackDeclaration = record {
  attacker-id : Integer,
  target-general : Integer between 0 and 1
}
```

The General is always a valid attack target — it cannot be removed
from the battlefield. Creatures can also be directed to attack other
creatures if an ability permits, but the default target is always the
opposing General.

## Declare Blockers

Defending player assigns blockers:
- Must be untapped
- One blocker can block one attacker
- Multiple blockers can block the same attacker
- Flying creatures can only be blocked by creatures with flying or reach
- If multiple blockers, attacker orders them for damage assignment

```
BlockDeclaration = record {
  blocker-id : Integer,
  blocking : Integer
}
```

## Damage Assignment

### Defense Check

Before damage is dealt from any single combat source to a creature,
the engine checks the target's **defense** value. If the source's
power is less than or equal to the target's defense, the damage is
**fully absorbed** — zero damage is dealt. Defense is not subtracted
from damage; it is a threshold. A creature either pierces the defense
or bounces off entirely.

```
combat-damage-applies : Power -> Defense -> Boolean
combat-damage-applies power defense =
  power > defense
```

Defense applies per-source, not per-total. Ten 1/1 creatures
attacking into a 0/5/2 blocker each fail the defense check
independently — none of them deal any damage regardless of how many
there are. To kill a defense-2 creature in combat, you need at least
one attacker with power 3 or greater.

Defense does NOT apply to:
- Non-combat damage (spells, abilities, effects)
- Damage dealt to players (only creatures have defense)
- Self-inflicted damage (e.g., "deals damage to itself" effects)

### Standard Damage Assignment

For each attacker:
1. If unblocked → deals damage to the defending **General**. The
   General has its own defense value — if the attacker's power ≤ the
   General's defense, zero damage is dealt. Otherwise, the full power
   is dealt as damage to the General's life total.
2. If blocked by one creature:
   - Attacker deals damage to blocker IF attacker's power > blocker's
     defense. Otherwise zero.
   - Blocker deals damage to attacker IF blocker's power > attacker's
     defense. Otherwise zero.
3. If blocked by multiple:
   - Attacker assigns damage in order. For each blocker, the defense
     check applies: if the attacker's power > that blocker's defense,
     damage is assigned normally (must assign lethal before moving to
     next). If the attacker's power ≤ that blocker's defense, damage
     assigned to it is absorbed — it takes zero, but the attacker
     still must "assign lethal" (which is impossible) and cannot
     proceed to the next blocker.
   - Each blocker deals damage to attacker individually, each
     subject to the attacker's defense check.

**Trample and defense:** When a trampling attacker is blocked,
defense still applies to each blocker. If the attacker's power
exceeds the blocker's defense, damage assignment proceeds normally
(assign lethal, excess tramples through). If the attacker's power
does not exceed a blocker's defense, the attacker cannot assign
lethal to that blocker, and no damage tramples through — the defense
wall holds.

**First strike / Double strike:**
- Creatures with first strike deal damage in a separate step before
  normal damage. Defense checks apply in both steps.
- After first strike damage, state-based actions are checked
  (creatures with lethal damage die)
- Then normal combat damage happens (first strikers don't deal again;
  double strikers do)

### Defense and Keywords

| Keyword | Interaction with defense |
|---------|------------------------|
| Deathtouch | Pierces defense — deathtouch damage is always dealt regardless of defense value |
| Trample | Defense blocks trample-through (see above) |
| First strike | Defense applies in both damage steps |
| Lifelink | No life gained if defense absorbs the damage (zero damage dealt = zero life) |
| Protection | Protection prevents damage before defense is checked; both are independent shields |

Deathtouch is the primary answer to high-defense creatures. A 1/1
with deathtouch kills a 0/10/8 — deathtouch bypasses the defense
threshold entirely, and 1 point of deathtouch damage is lethal.

## Damage Record

```
DamageEvent = record {
  source : Integer,
  target-entity : Integer,
  target-player : Integer,
  amount : Integer,
  is-combat : Boolean,
  absorbed-by-defense : Boolean
}
```

Damage to creatures is marked (not subtracted from toughness). A
creature with damage >= toughness dies as a state-based action.
Absorbed damage events are recorded with `absorbed-by-defense = True`
and `amount = 0` for replay and analysis purposes.

## Lifelink

Damage dealt by a creature with lifelink also causes its controller to
gain that much life. This happens simultaneously with the damage.

## Deathtouch

Any amount of damage from a source with deathtouch is considered lethal
for the purpose of:
- State-based actions (creature dies)
- Damage assignment ordering (1 point is "lethal" for moving to next blocker)

## General in Combat

The General participates in combat as both attack target and potential
blocker:

- **As attack target** — unblocked creatures deal damage to the
  General's life total, subject to the General's defense value. The
  General's toughness limits how much damage it absorbs per combat
  step — damage beyond toughness still reduces life but the General
  remains on the battlefield.
- **As blocker** — the General can block like a creature. It uses its
  power to deal damage to the attacker (subject to the attacker's
  defense) and takes damage from the attacker (subject to its own
  defense). Damage to a blocking General reduces its life total.
- **The General cannot die from combat** — it stays on the battlefield
  regardless of damage. Only reaching 0 life ends the game.

The General's behavioral modifiers may influence whether the AI uses
it as a blocker. A General with `NeverOverextend` combat bias will
not block unless the incoming damage threatens a significant life
threshold.

## Combat Triggers

- "Whenever [this creature] attacks" — triggers on declare attackers
- "Whenever [this creature] blocks" — triggers on declare blockers
- "Whenever [this creature] deals combat damage to a player" — triggers on damage resolution

## Keyword Power and Costing

Simulation data from 8 tournaments (see [GameBalance.md](GameBalance.md)
sections 8.3-8.6) shows keyword power tiers:

- **Premium:** First Strike (most consistent winner), Trample (most
  explosive). Should cost +1 mana over vanilla.
- **Standard:** Flying, Haste. Standard costing.
- **Situational:** Deathtouch (trades up but stalls), Vigilance
  (strong vs vanilla, weak in diverse metas). +0 mana.
- **Synergy:** Lifelink. Needs cheap bodies or keyword pairing.

Defense as a stat creates natural color identity: Green gets highest
defense (3-5), White mid (2-3), Blue low (0-1), Red/Black zero.
Deathtouch (Black) is the universal answer to high defense.
