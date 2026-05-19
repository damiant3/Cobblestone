# SpellStack — Stack Mechanics and Resolution

## The Stack

The stack is a LIFO structure holding spells and abilities waiting to resolve.

```
StackEntry = record {
  id : Integer,
  source-card : CardId,
  controller : Integer between 0 and 1,
  targets : List Target,
  entry-type : StackEntryType
}

StackEntryType =
  | SpellEntry (template : CardTemplate)
  | AbilityEntry (ability : Ability) (source-permanent : Integer)
```

## Casting a Spell

1. Announce the spell (move card from hand to stack)
2. Choose modes (if modal)
3. Choose targets
4. Determine total cost (mana cost + additional costs - reductions)
5. Activate mana abilities (these don't use the stack)
6. Pay costs
7. Spell is now "cast" — triggers "whenever a player casts a spell"

## Resolution

When both players pass priority with items on the stack:
1. Top entry resolves
2. If it's a spell:
   - Check targets still legal (if all illegal, spell is countered on resolution)
   - Apply effects
   - If permanent spell, move to battlefield
   - If instant/sorcery, move to graveyard
3. If it's an ability:
   - Check targets still legal
   - Apply effects
4. After resolution, active player gets priority again

## Responding

- Instants can be cast any time a player has priority
- Activated abilities can be activated any time a player has priority
- Sorceries, creatures, enchantments, artifacts, lands can only be played
  during a main phase when the stack is empty and you have priority

## Countering

A spell is "countered" when an effect says so (e.g., Counterspell).
A countered spell moves from the stack to its owner's graveyard without
resolving.

A spell is also countered on resolution if all its targets have become
illegal ("fizzles").

## Mana Abilities

Mana abilities do NOT use the stack. They resolve immediately:
- Activated ability that could produce mana, has no target
- Triggered ability that triggers from a mana ability

Example: Tapping a Forest for {G} resolves immediately.

## Split Second (out of scope for v1)

Not implemented in initial version.
