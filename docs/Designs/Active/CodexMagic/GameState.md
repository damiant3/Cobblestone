# GameState — Zone Model and Game Record

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
  life : Integer,
  mana-pool : ManaPool,
  library : List CardId,
  hand : List CardId,
  graveyard : List CardId,
  exile : List CardId,
  land-drop-used : Boolean,
  land-drops-remaining : Integer between 0 and 3
}
```

Default: 20 life, 1 land drop per turn.

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
