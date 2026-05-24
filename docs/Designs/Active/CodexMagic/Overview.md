# CodexMagic — Design Overview

## Purpose

CodexMagic is a distributed collectible card game platform built on
Codex. Players crack packs to obtain randomly generated cards, build
decks, and compete in AI-supervised duels across phone, tablet, and PC.
Cards are blockchain-backed fungible tokens with value driven by game
utility, effect rarity, art quality, and tournament history. The game
network is resilient, auditable, and fully replayable.

The rules engine is modeled on early Magic: The Gathering (Revised
through Onslaught era) for structural complexity — targeting, priority,
stack resolution, zone transitions, combat math — but CodexMagic is its
own game. AI generates all content: card mechanics, art, flavor text,
and effect code. Players supervise play like a general in war, not
micromanaging every action.

## Pillars

1. **AI-Generated Content** — Cards, art, effects, and mechanics are
   produced by AI on a seasonal schedule, with a QA/approval gate before
   release. No human-authored card sets.

2. **AI-Supervised Gameplay** — Players define posture and priorities.
   The play AI resolves obvious moves automatically; players intervene
   on ambiguous decisions, gambles, or sacrifices. Games flow fast.

3. **Blockchain Economy** — Every card is a token on-chain. Game stats,
   match outcomes, and tournament results are posted to the ledger.
   Players trade cards for an in-game cryptocurrency. We sell tokens —
   that's the monetization.

4. **Distributed Network** — Servers resolve game state in a distributed
   fashion. No single point of failure. Match history is auditable and
   replayable from the chain.

5. **Cross-Platform** — Phone, tablet, PC. Same account, same
   collection, same matches.

## Scope

### Rules Engine (existing docs)

- Core card types: Land, Creature, Instant, Sorcery, Enchantment, Artifact, General
- Mana system (5 colors + colorless)
- Turn phases and steps with priority passing
- The spell stack (LIFO resolution, responses, countering)
- Combat: attackers, blockers, damage assignment, first strike
- Zones: Library, Hand, Battlefield, Graveyard, Stack, Exile
- Keyword abilities, legendary rule, state-based actions
- Win/loss conditions (General life total, deck-out)
- General as player avatar, AI supervisor embodiment, and attack target

### Platform Systems (new docs)

- Card generation pipeline (AI mechanics + art + effect code)
- QA and approval workflow for generated content
- Seasonal mechanics schedule
- Pack-cracking and collection management
- Blockchain token system and trading
- In-game cryptocurrency and monetization
- Distributed game server network
- AI gameplay supervisor (posture, targeting, auto-play)
- Cross-platform client architecture
- Tournament system and rankings

## Architecture

The system is layered:

```
┌─────────────────────────────────────┐
│          Client (UI Layer)          │
│   Phone / Tablet / PC — same app   │
├─────────────────────────────────────┤
│       AI Gameplay Supervisor        │
│  Posture → decisions → auto-play   │
├─────────────────────────────────────┤
│         Rules Engine (pure)         │
│  State machine, deterministic,     │
│  side-effect-free game logic       │
├─────────────────────────────────────┤
│      Distributed Game Network       │
│  State resolution, matchmaking,    │
│  replay storage, audit trail       │
├─────────────────────────────────────┤
│         Blockchain Layer            │
│  Card tokens, match records,       │
│  currency, trading, tournaments    │
├─────────────────────────────────────┤
│      Content Generation Pipeline    │
│  Card design, art gen, effect code │
│  QA gates, seasonal releases       │
└─────────────────────────────────────┘
```

The rules engine remains a pure state machine — game state is a record,
actions are events, resolution is deterministic given the same inputs.
Side effects (randomness, player choice, AI decisions) are injected as
event parameters. Everything above and below the engine is new.

Key foreword modules we build on:
- `StateMachine` — turn phase transitions
- `CardDeck` — shuffle, deal primitives
- `ECS` — entities for permanents on the battlefield

## Sub-Documents

### Rules Engine

| Document | Covers |
|----------|--------|
| [GameState.md](GameState.md) | Zone model, player state, game record |
| [Cards.md](Cards.md) | Card data model, types, abilities, costs |
| [Turns.md](Turns.md) | Phase/step structure, priority, active player |
| [SpellStack.md](SpellStack.md) | Stack mechanics, resolution, responses |
| [Combat.md](Combat.md) | Attack, block, damage assignment, keywords |
| [StateBasedActions.md](StateBasedActions.md) | SBAs, legend rule, zero-toughness |
| [Mana.md](Mana.md) | Mana pool, costs, paying, color identity |
| [CycleDetection.md](CycleDetection.md) | Infinite loop detection, clamping, player-choice bailouts |

### Platform Systems

| Document | Covers |
|----------|--------|
| [CardGeneration.md](CardGeneration.md) | AI card design, art generation, effect codegen, QA pipeline |
| [Economy.md](Economy.md) | Packs, tokens, trading, cryptocurrency, monetization |
| [Network.md](Network.md) | Distributed state resolution, server topology, replay |
| [AIGameplay.md](AIGameplay.md) | Play supervisor, posture system, auto-play, intervention |
| [Seasons.md](Seasons.md) | Seasonal content schedule, mechanic invention, approval |
| [WebClient.md](WebClient.md) | Web client/server architecture, UI pages, API, visual design |
| [GameBalance.md](GameBalance.md) | Critical analysis of MTG problems, competitor survey, design responses |

## File Plan

```
codex.magic/
  codex.project.json

  # Rules engine
  GameState.codex       -- zones, players, game record
  Card.codex            -- card definitions, types, static data
  Mana.codex            -- mana pool, costs, color
  Turn.codex            -- phase state machine, priority
  Stack.codex           -- spell stack, resolution
  Combat.codex          -- combat phase logic
  Permanent.codex       -- battlefield objects, modifiers, abilities
  Action.codex          -- state-based actions
  CycleDetect.codex     -- cycle detection, fingerprinting
  Clamp.codex           -- clamping logic, bailout prompts
  Engine.codex          -- top-level game loop, event dispatch

  # AI gameplay
  Supervisor.codex      -- AI play supervisor, posture evaluation
  Posture.codex         -- posture definitions, target priority
  AutoPlay.codex        -- obvious-move detection, auto-resolution

  # Card generation
  CardGen.codex         -- card template generation, stat balancing
  EffectGen.codex       -- runtime effect code generation
  ArtGen.codex          -- art generation pipeline interface
  QAGate.codex          -- approval workflow, mechanic validation

  # Economy
  Token.codex           -- blockchain token interface, card NFTs
  Trading.codex         -- peer-to-peer card trading
  Currency.codex        -- in-game crypto, purchase flow
  PackCrack.codex       -- pack opening, rarity distribution

  # Network
  GameServer.codex      -- distributed game state resolution
  Matchmaking.codex     -- player matching, ranked queues
  Replay.codex          -- match recording, chain posting
  Audit.codex           -- state verification, dispute resolution

  # Client
  Client.codex          -- cross-platform UI shell
  Collection.codex      -- deck building, card browsing
  opening.codex         -- entry point
```
