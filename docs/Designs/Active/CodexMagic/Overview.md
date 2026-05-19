# CodexMagic — Design Overview

## Purpose

A collectible card game rules engine implemented in Codex, modeled on
early Magic: The Gathering (pre-Affinity era, roughly Revised through
Onslaught). The engine defines the game state machine, card data model,
turn structure, combat resolution, spell stack, and permanent management.

This is a proof of concept for a new game — fidelity to MTG's absolute
rules is not required, but the structural complexity is the point. We
want a real state machine driving a real game with targeting, priority,
stack resolution, zone transitions, and combat math.

## Scope

- Two-player duel (no multiplayer, no teams)
- Core card types: Land, Creature, Instant, Sorcery, Enchantment, Artifact
- No planeswalkers, no saga, no vehicles, no companions
- Mana system (5 colors + colorless)
- Turn phases and steps with priority passing
- The spell stack (LIFO resolution, responses, countering)
- Combat: attackers, blockers, damage assignment, first strike
- Zones: Library, Hand, Battlefield, Graveyard, Stack, Exile
- Basic keyword abilities (flying, trample, haste, vigilance, etc.)
- Legendary rule, summoning sickness, state-based actions
- Win/loss conditions (life total, deck-out, poison is out of scope)

## Non-Goals

- Network protocol or client UI (future workstream)
- AI opponent logic
- Deck construction rules or sideboarding
- Draft or sealed formats
- Any mechanics introduced after Onslaught block

## Architecture

The engine is a pure state machine. Game state is a record; actions are
events fired into the machine; resolution is deterministic given the
same inputs. Side effects (randomness for shuffle, player choice for
targeting/blocking) are injected as event parameters — the engine itself
is pure and testable.

Key foreword modules we build on:
- `StateMachine` — turn phase transitions
- `CardDeck` — shuffle, deal primitives
- `ECS` — entities for permanents on the battlefield

## Sub-Documents

| Document | Covers |
|----------|--------|
| [GameState.md](GameState.md) | Zone model, player state, game record |
| [Cards.md](Cards.md) | Card data model, types, abilities, costs |
| [Turns.md](Turns.md) | Phase/step structure, priority, active player |
| [SpellStack.md](SpellStack.md) | Stack mechanics, resolution, responses |
| [Combat.md](Combat.md) | Attack, block, damage assignment, keywords |
| [StateBasedActions.md](StateBasedActions.md) | SBAs, legend rule, zero-toughness |
| [Mana.md](Mana.md) | Mana pool, costs, paying, color identity |

## File Plan

```
codex.magic/
  codex.project.json
  GameState.codex       -- zones, players, game record
  Card.codex            -- card definitions, types, static data
  Mana.codex            -- mana pool, costs, color
  Turn.codex            -- phase state machine, priority
  Stack.codex           -- spell stack, resolution
  Combat.codex          -- combat phase logic
  Permanent.codex       -- battlefield objects, modifiers, abilities
  Action.codex          -- state-based actions
  Engine.codex          -- top-level game loop, event dispatch
  opening.codex         -- entry point (test harness)
```
