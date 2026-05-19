# State-Based Actions

## What Are SBAs

State-based actions (SBAs) are game rules that are checked whenever a
player would receive priority. They don't use the stack. They just happen.
If any SBA applies, it is performed, then SBAs are checked again until
none apply.

## SBA List (implemented subset)

| # | Condition | Action |
|---|-----------|--------|
| 1 | A player has 0 or less life | That player loses the game |
| 2 | A player attempts to draw from an empty library | That player loses the game |
| 3 | A creature has toughness 0 or less | It is put into its owner's graveyard |
| 4 | A creature has damage >= its toughness | It is destroyed (graveyard) |
| 5 | A creature has been dealt damage by a deathtouch source | It is destroyed |
| 6 | An Aura is not attached to a legal object | It is put into its owner's graveyard |
| 7 | Two or more legendary permanents with the same name are controlled by the same player | That player chooses one, the rest are put into their owners' graveyards |
| 8 | A token is in any zone other than the battlefield | It ceases to exist |

## Implementation

```
check-state-based-actions : GameState -> GameState
```

This function loops:
1. Check all SBAs against current state
2. Collect all that apply
3. Apply them simultaneously
4. If any were applied, check again
5. Stop when no SBAs apply

After SBAs are done, triggered abilities that triggered during SBA
processing are put on the stack (APNAP order).

## The Legend Rule (#7)

When a player controls two or more legendary permanents with the same
name, they choose one to keep. The rest go to the graveyard. This is
not destruction (doesn't trigger "when destroyed" abilities).

## Damage vs. Destruction

- Damage is marked on creatures (accumulates within a turn)
- Damage is removed during the cleanup step
- Destruction moves a permanent to the graveyard
- "Indestructible" prevents destruction but NOT "put into graveyard" effects
- Damage marked on an indestructible creature doesn't cause SBA #4

## Zero Toughness

If a creature's toughness is reduced to 0 by an effect (not damage),
it goes to the graveyard via SBA #3. This is not destruction.
Indestructible does not save it.
