# CodexMagic -- Design Overview

## What This Is

CodexMagic is a **card game construction kit** built on Codex. The
platform provides a universal game engine, a blockchain economy, an
AI content pipeline, and a social infrastructure. On top of this,
players and clans configure their own game experiences -- formats,
rules, card pools, art themes, and private economies.

Magic: The Gathering is one configuration in the space. So is a
30-card speed game with robots and lasers. So is a kids-only game
with biblical art and no deathtouch. The engine is the same. The
experience is defined by data -- format records, house rules, content
themes, and clan policies. No code changes required.

## Pillars

1. **Universal Engine** -- One deterministic rules engine handles all
   configurations. P/T/D combat, stack resolution, zone transitions,
   triggered abilities, and state-based actions are constants. Turn
   caps, life totals, deck sizes, copy limits, keyword legality, and
   content themes are parameters.

2. **AI-Supervised Gameplay** -- Players set posture and priorities.
   The AI General executes. Games resolve in 5-8 minutes with 25-35
   meaningful decisions. The stack is hidden -- the AI resolves it,
   the player sees decision points.

3. **AI-Generated Content** -- Cards, art, mechanics, and flavor text
   are produced by AI on seasonal schedules, with QA gates before
   release. Clans can theme their own packs with custom art styles,
   creature types, and profanity levels.

4. **Blockchain Economy** -- Every card is a token on-chain. Match
   results, tournament outcomes, and card provenance are auditable
   and replayable. Clans mint private currencies alongside the global
   Mana Coin.

5. **Safe Social Platform** -- Age brackets (Child/Teen/Adult) with
   enforced communication tiers (EmotesOnly/Moderated/Unmoderated),
   parental controls, and clan-based content curation. Kids play in
   walled gardens. Adults get the experience they choose.

## Architecture

```
+---------------------------------------------+
|              Client (UI Layer)              |
|      Phone / Tablet / PC -- same app       |
+---------------------------------------------+
|           AI Gameplay Supervisor            |
|     Posture -> decisions -> auto-play      |
+---------------------------------------------+
|       Rules Engine (pure, universal)        |
|    Deterministic state machine. All         |
|    formats share this engine.              |
+---------------------------------------------+
|          Clan & Social Layer               |
|  Custom formats, house rules, economies,   |
|  age safety, tournaments, challenges       |
+---------------------------------------------+
|      Distributed Game Network              |
|  State resolution, matchmaking, replay     |
+---------------------------------------------+
|          Blockchain Layer                  |
|  Card tokens, match records, Mana Coin,   |
|  clan coin, trading, provenance           |
+---------------------------------------------+
|       Content Generation Pipeline          |
|  Card design, art gen, effect code,        |
|  clan theming, QA gates, seasons           |
+---------------------------------------------+
```

## Sub-Documents

### Game Engine

The universal rules engine. These docs define the constants --
mechanics that are the same in every format and every clan.

| Document | Covers |
|----------|--------|
| [Cards.md](Cards.md) | Card types (7 including General), P/T/D stats, keyword abilities, behavioral modifiers, army loyalty, General leveling |
| [Combat.md](Combat.md) | Attack, block, defense threshold, damage assignment, deathtouch piercing, General in combat |
| [GameState.md](GameState.md) | Zones (6), General state with life total, player state, army loyalty computation |
| [Mana.md](Mana.md) | 5 colors + colorless, mana pool, costs, payment. Optional: discard-for-mana and pitch-lands-for-draw (see GameBalance) |
| [Turns.md](Turns.md) | 5 phases, 10 steps, priority passing, APNAP ordering. Turn caps vary by format (see Formats) |
| [SpellStack.md](SpellStack.md) | Stack (LIFO), casting flow, resolution, countering, mana abilities. Player-facing: hidden by AI (see AIGameplay) |
| [StateBasedActions.md](StateBasedActions.md) | 8 SBAs: General life check, deck-out, lethal damage, zero toughness, deathtouch, aura, legend rule, tokens |
| [CycleDetection.md](CycleDetection.md) | Infinite loop detection via state fingerprinting, clamping, player-choice bailouts (single and contested), integration with SBAs and triggers |
| [GameBalance.md](GameBalance.md) | MTG problem analysis, competitor survey, 9 design responses, simulation framework, keyword tournament results, recommended parameters |

### Economy

How cards, currency, and value flow through the system.

| Document | Covers |
|----------|--------|
| [Economy.md](Economy.md) | Pack cracking, subscription tiers, card tokens with provenance and prominence, Ubiquitous publishing, Mana Coin, trading with rake, direct sales, monetization. Clan coin (see Clans) |

### Social

Players, clans, safety, and community.

| Document | Covers |
|----------|--------|
| [Clans.md](Clans.md) | Clan organizations, roles, private economies (clan coin + treasury + exchange), shared card library, house rules, custom formats, inter-clan challenges, custom packs with content theming, age brackets (Child/Teen/Adult), chat tiers, parental controls, trust system |
| [Formats.md](Formats.md) | Data-driven format definitions (Modern, Legacy, Singleton, Blitz, Unlimited), limited formats (Sealed-Base, Sealed-Classic, Draft, Chaos), tournament structures (Swiss, Single/Double Elim, League), rotation schedule |

### Content

How cards, art, and mechanics are created.

| Document | Covers |
|----------|--------|
| [CardGeneration.md](CardGeneration.md) | 5-stage pipeline: mechanic invention, card assembly, effect codegen, art generation, QA gate. Clan packs use this pipeline with custom themes (see Clans) |
| [Seasons.md](Seasons.md) | 12-week seasons, mechanic invention process, balance philosophy (bans not edits), rewards, content versioning (immutable manifests on-chain) |

### Infrastructure

Servers, networking, and the blockchain.

| Document | Covers |
|----------|--------|
| [Network.md](Network.md) | Distributed match servers (primary + witness), deterministic replay, commit-reveal shuffle, matchmaking, cross-platform thin clients, latency targets |

### Client

The player-facing application.

| Document | Covers |
|----------|--------|
| [AIGameplay.md](AIGameplay.md) | General-embodied AI supervisor, posture system (stance, targets, freezes, policies), obvious-move auto-play, escalated decisions with options, skill levels. Stack hidden from player |
| [WebClient.md](WebClient.md) | 5 web pages (Game, Collection, Store, Queue, Profile), HTTP server bridging to VM, card rendering, game state JSON, 22 API endpoints |

## The Configuration Space

The game engine accepts a `FormatDef` record (deck size, copy limit,
pool rule, general rule, etc.) and a `RuleSet` record (mulligan,
screw/flood fix, fairness compensation, turn cap, sudden death).
Clans layer `HouseRule` overrides on top (ban/require keywords,
adjust parameters, enable/disable mechanics).

Any combination of these parameters defines a playable game. Some
examples:

| Configuration | What you get |
|--------------|-------------|
| Modern + default rules | The flagship ranked experience |
| Blitz + turn cap 8 + life 15 | 3-minute mobile games |
| Singleton + 100 cards + life 30 | Commander-style with General identity |
| All keywords banned except Flying | Air-combat-only variant |
| Unlimited + no cap + 40 cards | Simulation test format |
| Kids clan + Clean + StainedGlass art | Church youth group game |
| Adult clan + Unrestricted + CyberPunk art | Full-expression experience |
| MTG-like: 60 cards, 4-of, 20 life, no General | Classic card game (General optional) |

The platform does not hardcode any of these. They are all data.

## File Plan

```
codex.magic/
  codex.project.json

  # Game engine (universal)
  Card.codex, Mana.codex, GameState.codex, Turn.codex, Stack.codex,
  Combat.codex, Action.codex, CycleDetect.codex, Clamp.codex,
  Engine.codex, Trigger.codex, General.codex, GameRules.codex

  # AI supervisor
  Supervisor.codex, Posture.codex, AutoPlay.codex

  # Content
  CardGen.codex, EffectGen.codex, ArtGen.codex, QAGate.codex,
  CardPool.codex, Season.codex

  # Economy
  Token.codex, Trading.codex, Currency.codex, PackCrack.codex

  # Social
  Clan.codex, ClanEconomy.codex, ClanLibrary.codex, ClanFormat.codex,
  ClanTournament.codex, ClanChallenge.codex, ClanPacks.codex,
  Safety.codex, Trust.codex

  # Infrastructure
  GameServer.codex, MagicServer.codex, Matchmaking.codex,
  Replay.codex, Audit.codex, Auth.codex, MatchRecord.codex

  # Formats & simulation
  Deck.codex, Simulate.codex, SimBaseline.codex, SimRunner.codex,
  SimDb.codex

  # Client
  Client.codex, Collection.codex, opening.codex
```
