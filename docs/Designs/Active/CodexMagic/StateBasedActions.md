# State-Based Actions

## What Are SBAs

State-based actions (SBAs) are game rules that are checked whenever a
player would receive priority. They don't use the stack. They just happen.
If any SBA applies, it is performed, then SBAs are checked again until
none apply.

## SBA List (implemented subset)

| # | Condition | Action |
|---|-----------|--------|
| 1 | A player's General has 0 or less life | That player loses the game |
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
5. Stop when no SBAs apply OR cycle detection triggers (see below)

After SBAs are done, triggered abilities that triggered during SBA
processing are put on the stack (APNAP order).

## Cycle Detection in SBA Processing

SBA processing can loop indefinitely when effects continuously
re-establish the conditions that SBAs address (e.g., a triggered
ability creates a legendary token every time one is removed by the
legend rule). The engine detects this:

```
check-state-based-actions : GameState -> GameState
```

The SBA loop maintains a **state fingerprint** — a hash of the
subset of game state that SBAs inspect (life totals, creature
toughness values, legendary permanent sets, aura attachments, token
zone positions). If the same fingerprint recurs within a single SBA
pass sequence, a cycle has been detected.

On cycle detection:
1. The loop clamps at the current state — no further SBA iterations
2. The cycle is logged in the match record with the repeating
   fingerprint and iteration count
3. Triggered abilities accumulated during the cycle are clamped to
   `max-cycle-iterations` copies (configurable, default 500)
4. Play continues from the clamped state

See [CycleDetection.md](CycleDetection.md) for the full cycle
detection system that covers SBAs, triggers, and replacement effects.

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
