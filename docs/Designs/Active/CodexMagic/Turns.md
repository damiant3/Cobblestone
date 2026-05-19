# Turns — Phase Structure and Priority

## Turn Structure

A turn proceeds through these phases and steps in order:

```
1. Beginning Phase
   a. Untap step       — untap all permanents, no priority
   b. Upkeep step      — triggered abilities, priority passes
   c. Draw step        — active player draws, priority passes

2. Pre-combat Main Phase
   — play lands, cast sorceries/creatures/enchantments/artifacts
   — priority passes

3. Combat Phase
   a. Beginning of combat step — priority
   b. Declare attackers step   — active player declares, priority
   c. Declare blockers step    — defending player declares, priority
   d. Combat damage step       — damage dealt, priority
   e. End of combat step       — priority

4. Post-combat Main Phase
   — same as pre-combat main

5. Ending Phase
   a. End step         — "at end of turn" triggers, priority
   b. Cleanup step     — discard to hand size, remove damage, no priority (usually)
```

## Phase/Step Encoding

Phases and steps are integer constants for the state machine:

```
phase-beginning  = 0
phase-main-1     = 1
phase-combat     = 2
phase-main-2     = 3
phase-ending     = 4

step-untap       = 0
step-upkeep      = 1
step-draw        = 2
step-begin-combat = 3
step-attackers   = 4
step-blockers    = 5
step-damage      = 6
step-end-combat  = 7
step-end         = 8
step-cleanup     = 9
```

## Priority

- After each game action, the active player receives priority
- A player with priority may: cast a spell, activate an ability, or pass
- When both players pass priority in succession with an empty stack, the phase/step advances
- When both players pass with items on the stack, the top item resolves
- Some steps (untap, cleanup) do not grant priority unless a trigger fires

## State Machine Integration

The turn structure maps onto a `StateMachine` from the foreword:
- States = phases/steps
- Events = priority-pass, advance, combat-declare, etc.
- The FSM enforces legal phase transitions
- Illegal actions (e.g., casting a sorcery during combat) are rejected by checking current state

## Active Player / Non-Active Player (APNAP)

When multiple triggered abilities trigger simultaneously:
1. Active player puts their triggers on the stack first (in any order)
2. Non-active player puts theirs on top
3. Stack resolves LIFO, so non-active player's triggers resolve first
