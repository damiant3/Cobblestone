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
- Must attack the opposing player (no planeswalkers in this version)

```
AttackDeclaration = record {
  attacker-id : Integer,
  target-player : Integer between 0 and 1
}
```

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

For each attacker:
1. If unblocked → deals damage equal to power to defending player
2. If blocked by one creature → attacker and blocker deal damage to each other
3. If blocked by multiple → attacker assigns damage in order (must assign lethal before moving to next), blockers all deal damage to attacker

**Trample:** If blocked, must assign at least lethal damage to each blocker; excess goes to defending player.

**First strike / Double strike:**
- Creatures with first strike deal damage in a separate step before normal damage
- After first strike damage, state-based actions are checked (creatures with lethal damage die)
- Then normal combat damage happens (first strikers don't deal again; double strikers do)

## Damage Record

```
DamageEvent = record {
  source : Integer,
  target-entity : Integer,
  target-player : Integer,
  amount : Integer,
  is-combat : Boolean
}
```

Damage to creatures is marked (not subtracted from toughness). A creature
with damage >= toughness dies as a state-based action.

## Lifelink

Damage dealt by a creature with lifelink also causes its controller to
gain that much life. This happens simultaneously with the damage.

## Deathtouch

Any amount of damage from a source with deathtouch is considered lethal
for the purpose of:
- State-based actions (creature dies)
- Damage assignment ordering (1 point is "lethal" for moving to next blocker)

## Combat Triggers

- "Whenever [this creature] attacks" — triggers on declare attackers
- "Whenever [this creature] blocks" — triggers on declare blockers
- "Whenever [this creature] deals combat damage to a player" — triggers on damage resolution
