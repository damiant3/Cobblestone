# GameState -- Zone Model and Game Record

## Zones

Six zones, each a list of card references (entity IDs):

| Zone | Ordered? | Visible? | Notes |
|------|----------|----------|-------|
| Library | Yes (top = index 0) | Hidden | Shuffle on certain effects |
| Hand | Yes | Owner only | Max hand size 7 (discard in cleanup) |
| Battlefield | No | Public | Permanents live here |
| Graveyard | Yes (LIFO) | Public | Ordered by arrival |
| Stack | Yes (LIFO) | Public | Spells/abilities waiting to resolve |
| Exile | No | Public | Removed from game |

## Player State

```
PlayerState = record {
  general : GeneralState,
  mana-pool : ManaPool,
  library : List CardId,
  hand : List CardId,
  graveyard : List CardId,
  exile : List CardId,
  land-drop-used : Boolean,
  land-drops-remaining : Integer between 0 and 3
}
```

Default: 1 land drop per turn. Life total is on the General, not the
player (see below).

## General State

The General is the player's avatar. It starts on the battlefield in
the command zone and carries the life total that determines win/loss.

```
GeneralState = record {
  card-id : CardId,
  life : Integer,              -- default 30, set by GeneralTemplate
  damage-marked : Integer,     -- combat/effect damage this turn
  counters : List Counter,
  is-tapped : Boolean,
  modifiers : List Modifier    -- temporary P/T/D changes, auras, etc.
}
```

The General is always on the battlefield -- it cannot be exiled,
bounced to hand, or destroyed. Effects that would remove the General
from the battlefield are negated. The General can be tapped and
untaps normally during the untap step.

Damage dealt to the General reduces its `life`, not its toughness.
The General's toughness determines how much damage it can absorb in
a single combat step before excess carries over (see
[Combat.md](Combat.md)), but the General is never destroyed by
damage -- it stays on the battlefield until `life` reaches 0.

**Army Loyalty** is derived, not stored -- it is the sum of CMC of all
non-token creatures the player controls. It changes as creatures enter
and leave the battlefield. See [Cards.md](Cards.md) for how the
General's abilities consume and scale with army loyalty.

## Game Record

```
GameState = record {
  players : List PlayerState,
  battlefield : List Permanent,
  stack : List StackEntry,
  active-player : Integer between 0 and 1,
  priority-player : Integer between 0 and 1,
  turn-number : Integer,
  phase : Integer,
  step : Integer,
  game-over : Boolean,
  winner : Integer
}
```

## Zone Transitions

Every card movement is a zone transition event:
- `zone-move : GameState, CardId, Zone, Zone -> GameState`
- Triggers "leaves the battlefield" and "enters the battlefield" events
- Cards entering the battlefield become Permanents
- Cards leaving the battlefield lose all modifications

## Card Identity

Cards have a static definition (template) and a runtime identity (entity ID).
A CardId uniquely identifies a specific card instance throughout the game,
regardless of which zone it is in. The template provides base stats; the
entity carries current state (damage, counters, attached auras, etc.).
