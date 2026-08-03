# Web Client & Server -- UI, Store, Game, Economy

## Overview

The CodexMagic web client follows the same architecture as the classic
games: a PowerShell HTTP server bridges the browser to a Codex VM
running the game server binary. The browser is a thin client -- all
game logic, AI decisions, economy operations, and matchmaking run
server-side. The client renders state and captures player input.

Unlike the classic games (which are spectator-only auto-play), the
CodexMagic client has interactive elements: posture controls, manual
overrides during AI decision points, store purchases, deck building,
and collection management. The step API still drives the game, but
the client can send player decisions back to the server.

## Architecture

```
Browser (HTML/JS/CSS)
  |
  |  fetch('/api/magic/...')
  v
PowerShell HTTP Server (server.ps1)
  |
  |  Serial bridge (same as classic games)
  v
CodexMagic VM (codexmagic.cdx)
  |-- Game engine (turns, combat, SBAs)
  |-- AI Supervisor (posture, auto-play)
  |-- Economy (tokens, ledger, packs)
  |-- Matchmaking (accounts, ratings, pairing)
  |-- Season (card pool, bans)
  '-- State persistence (in-VM memory)
```

## Pages

The client has six main pages, accessible from a top navigation bar:

### 1. Home / Dashboard (`index.html`)

The landing page. Shows:

- **Player profile** -- name, rank badge, rating, win/loss record,
  subscription tier
- **General portrait** -- current equipped General with P/T/D, life,
  and behavioral modifier summary
- **Daily rewards** -- claim button for first-win bonus, weekly pack
- **Season banner** -- current season name, days remaining, rank
  progress bar toward next tier
- **Quick play** -- buttons for "Play Ranked", "Play Casual",
  "Enter Tournament"
- **News feed** -- latest season notes, ban announcements, patch notes

Layout: centered column, dark theme consistent with the classic games
portal. General portrait is the visual centerpiece -- a large card
rendering with glow effect based on army loyalty.

### 2. Collection & Deck Builder (`collection.html`)

Two-panel layout:

**Left panel -- Collection browser:**
- Grid of owned cards, rendered as card images (art + stat overlay)
- Filter by: color, type, rarity, cost, keyword, season
- Sort by: name, cost, power, prominence, acquisition date
- Search bar for card name
- Each card shows: art, name, P/T/D (if creature), mana cost, rarity
  gem, prominence badge, copy count
- Click card to see full detail (rules text, provenance, match history)

**Right panel -- Deck builder:**
- Deck list with card counts, sorted by cost
- Mana curve bar chart (visual cost distribution)
- Deck stats: total cards, creature count, spell count, average CMC,
  color distribution
- General selector at the top (dropdown of owned Generals)
- Validation status (green check or red X with reason)
- Save/Load deck presets
- "Auto-fill" button: AI suggests remaining cards based on General
  and color identity

Deck validation runs on every change -- the server checks via
`/api/magic/deck/validate` and returns the result. Invalid states
show the reason inline.

**Card rendering:**
Cards are rendered as styled divs, not images (no asset generation
yet). Layout mimics a physical card:

```
┌──────────────────┐
│ Name        {2}{R}│  <- name + mana cost symbols
├──────────────────┤
│                  │
│   [Art Area]     │  <- placeholder gradient or color block
│                  │
├──────────────────┤
│ Creature - Goblin│  <- type line
├──────────────────┤
│ Haste            │  <- rules text / keywords
│                  │
├──────────────────┤
│             2/1/0│  <- P/T/D in bottom-right
└──────────────────┘
```

Color-coded border: white=W, blue=U, black=B, red=R, green=G,
gold=multicolor, grey=colorless. Rarity gem in top-right corner:
white=Common, blue=Uncommon, gold=Rare, orange=Mythic,
rainbow=Legendary Mythic.

### 3. Game View (`game.html`)

The main play screen. Split into zones:

```
┌─────────────────────────────────────────────┐
│  Opponent General  [Life: 28]  [Loyalty: 7] │
├─────────────────────────────────────────────┤
│  Opponent Battlefield (cards face-up)       │
│  ┌──┐ ┌──┐ ┌──┐ ┌──┐                       │
│  │  │ │  │ │  │ │  │                        │
│  └──┘ └──┘ └──┘ └──┘                       │
├─────────────────────────────────────────────┤
│          ← Combat Zone →                    │
│  Attackers → → →    ← ← ← Blockers         │
├─────────────────────────────────────────────┤
│  Your Battlefield                           │
│  ┌──┐ ┌──┐ ┌──┐ ┌──┐ ┌──┐                  │
│  │  │ │  │ │  │ │  │ │  │                   │
│  └──┘ └──┘ └──┘ └──┘ └──┘                  │
├─────────────────────────────────────────────┤
│  Your General  [Life: 30]  [Loyalty: 12]    │
├─────────────────────────────────────────────┤
│  Your Hand                                  │
│  ┌──┐ ┌──┐ ┌──┐ ┌──┐ ┌──┐ ┌──┐ ┌──┐       │
│  │  │ │  │ │  │ │  │ │  │ │  │ │  │        │
│  └──┘ └──┘ └──┘ └──┘ └──┘ └──┘ └──┘       │
├─────────────────────────────────────────────┤
│  [Posture: Balanced ▼]  [Phase: Main 1]     │
│  [▶ Auto] [⏸ Pause] [⏭ Step]  Speed: ████░│
└─────────────────────────────────────────────┘
```

**Posture controls** (left sidebar or bottom bar):
- Stance dropdown: Aggressive / Defensive / Balanced / Tempo / Control
- Phase freeze checkboxes (which phases to pause at)
- Primary target: click an opponent's permanent to mark it

**AI decision overlay:**
When the AI supervisor encounters an ambiguous decision, a modal
overlay appears:

```
┌─────────────────────────────────────┐
│  ⚔  Combat Decision                │
│                                     │
│  Opponent attacks with 3 creatures. │
│  Your General is at 22 life.        │
│                                     │
│  ○ Block the 4/4 with your 3/3     │
│    → You lose 3/3, they lose 4/4   │
│                                     │
│  ● Take 9 damage (AI recommended)  │
│    → General drops to 13 life      │
│                                     │
│  ○ Block all (spread blockers)     │
│    → Trade 2 creatures, stop all   │
│                                     │
│  [Accept AI Choice]  [Override]     │
│                          ⏱ 28s     │
└─────────────────────────────────────┘
```

Timer counts down -- if the player doesn't choose, the AI takes its
recommended action.

**Game flow:**
- Auto-play mode: game advances automatically, AI handles everything,
  pausing only at frozen phases and ambiguous decisions
- Step mode: player clicks "Step" to advance one game action at a time
- Speed slider controls auto-play delay (100ms to 2000ms)
- Phase indicator shows current phase with highlight

**Animations:**
- Cards slide from hand to battlefield (CSS transitions)
- Combat: attackers slide to center, blockers meet them
- Damage numbers float up from creatures/General
- Death: card fades out and slides to graveyard
- Life changes: General life counter animates up/down

### 4. Store (`store.html`)

The in-game store for spending Mana Coin:

**Pack Shop:**
- Card carousel of available pack types (Standard, Premium, Draft,
  Vintage)
- Each pack shows: price, contents summary, season label
- "Crack Pack" button opens the pack-opening experience (see below)
- Subscription badge shows upgrade bonus (+1 through +5)

**Pack Opening Experience:**
Full-screen modal with card reveal animation:
1. Pack appears centered, sealed
2. Click/tap to open -- pack tears open with animation
3. Cards fan out face-down
4. Click each card to flip it -- rarity determines flip animation:
   - Common: simple flip
   - Uncommon: flip with blue shimmer
   - Rare: flip with gold burst
   - Mythic: flip with orange fire effect
   - Legendary Mythic: full-screen rainbow explosion
5. After all cards revealed, "Add to Collection" button
6. If an Ubiquitous token was pulled, special callout with
   "Publish This Card" prompt

**Ubiquitous Market:**
- Grid of player-published cards available for copy purchase
- Each listing shows: card render, copy price, copies sold, publisher
  name
- Sort by: price, popularity, prominence, newest
- Buy button mints a copy and deducts Mana Coin

**Direct Sales:**
- Starter deck bundles (one per color)
- Season spotlight: rotating featured cards at fixed prices
- Cosmetics: card sleeves, board skins (future)

**Mana Coin Balance:**
- Always visible in the top-right corner of every page
- "Buy Mana Coin" button links to purchase flow (stub for now)

### 5. Matchmaking Queue (`queue.html`)

The match queue screen:

- **Format selector**: Standard, Vintage, Draft, Sealed
- **Competition tier**: Open (sub bonuses active) / Fair (no bonuses)
- **Deck selector**: dropdown of saved decks (validates on selection)
- **Queue button**: "Find Match" -- enters queue, shows spinner
- **Queue status**: "Searching... (12s)" with estimated wait time
- **Match found**: opponent preview (rank, General name) with
  "Accept" / "Decline" buttons

After match acceptance, redirects to `game.html` with the match ID.

**Casual vs Ranked:**
- Ranked: affects rating, uses competition tier
- Casual: no rating change, any deck, practice mode

### 6. Profile & History (`profile.html`)

- **Stats**: wins, losses, win rate, current streak, best streak
- **Rank history**: chart showing rating over time
- **Match history**: table of recent matches with opponent, result,
  turns, date
- **General collection**: all owned Generals with usage stats
- **Season progress**: reward track with claimed/unclaimed markers
- **Subscription management**: current tier, upgrade options

## API Endpoints

All endpoints prefixed with `/api/magic/`:

### Account & Profile

| Endpoint | Method | Request | Response |
|----------|--------|---------|----------|
| `/account/new` | GET | `?name=X&general=N` | `{account-id, name, rating, rank}` |
| `/account/profile` | GET | `?id=N` | Full account data |
| `/account/history` | GET | `?id=N&count=20` | Recent match list |

### Deck

| Endpoint | Method | Request | Response |
|----------|--------|---------|----------|
| `/deck/validate` | GET | `?general=N&cards=1,2,3,...&format=standard` | `{valid, reason}` |
| `/deck/generate` | GET | `?general=N&seed=S` | `{cards: [...]}` |

### Game

| Endpoint | Method | Request | Response |
|----------|--------|---------|----------|
| `/game/new` | GET | `?deck-a=...&deck-b=...&gen-a=N&gen-b=N&seed=S` | Initial game state JSON |
| `/game/step` | GET | `?match=N` | Updated game state JSON |
| `/game/posture` | GET | `?match=N&stance=aggressive` | Ack |
| `/game/override` | GET | `?match=N&choice=1` | Updated state after override |
| `/game/state` | GET | `?match=N` | Current full game state |

### Economy

| Endpoint | Method | Request | Response |
|----------|--------|---------|----------|
| `/store/packs` | GET | | List of available pack types + prices |
| `/store/crack` | GET | `?type=standard&account=N&seed=S` | `{cards: [...], upgraded: [...]}` |
| `/store/balance` | GET | `?account=N` | `{balance, sub-tier}` |
| `/store/buy-coin` | GET | `?account=N&amount=N` | `{new-balance}` |
| `/trade/list` | GET | `?page=N` | Market listings |
| `/trade/buy` | GET | `?listing=N&buyer=N` | Trade result |
| `/ubiq/list` | GET | | Ubiquitous market listings |
| `/ubiq/publish` | GET | `?token=N&price=P` | Listing created |
| `/ubiq/buy` | GET | `?listing=N&buyer=N` | Copy minted |

### Matchmaking

| Endpoint | Method | Request | Response |
|----------|--------|---------|----------|
| `/queue/join` | GET | `?account=N&format=standard&tier=fair` | `{queue-id, position}` |
| `/queue/status` | GET | `?queue=N` | `{status, wait-time, match-id}` |
| `/queue/cancel` | GET | `?queue=N` | Ack |

### Season

| Endpoint | Method | Request | Response |
|----------|--------|---------|----------|
| `/season/current` | GET | | Current season info |
| `/season/rewards` | GET | `?account=N` | Claimable rewards |
| `/season/claim` | GET | `?account=N&reward=N` | Reward claimed |

## Game State JSON

The game view needs a rich state snapshot per step:

```json
{
  "turn": 5,
  "phase": "combat",
  "step": "declare-attackers",
  "active-player": 0,

  "generals": [
    {
      "name": "Warlord of the Iron Banner",
      "power": 4, "toughness": 5, "defense": 1,
      "life": 28, "loyalty": 7,
      "stance-override": "aggressive",
      "tapped": false
    },
    {
      "name": "Dawn Commander of the Radiant Host",
      "power": 3, "toughness": 5, "defense": 2,
      "life": 30, "loyalty": 5,
      "stance-override": "balanced",
      "tapped": false
    }
  ],

  "battlefield": [
    {
      "id": 3, "name": "Goblin Raider", "controller": 0,
      "power": 2, "toughness": 1, "defense": 0,
      "tapped": false, "summoning-sick": false,
      "keywords": ["haste"], "damage": 0,
      "attacking": true
    }
  ],

  "hands": [
    {"size": 5, "cards": [...]},
    {"size": 6}
  ],

  "libraries": [13, 14],
  "graveyards": [[], [7]],
  "stack": [],
  "mana-pools": [
    {"W": 0, "U": 0, "B": 0, "R": 3, "G": 0, "C": 0},
    {"W": 2, "U": 0, "B": 0, "R": 0, "G": 0, "C": 0}
  ],

  "game-over": false,
  "winner": -1,

  "decision": null,
  "last-action": "Player 0 attacks with Goblin Raider"
}
```

When a decision is pending, the `decision` field contains the
`Decision` record from `AIGameplay.md`:

```json
{
  "decision": {
    "situation": "Opponent attacks with 3 creatures",
    "options": [
      {"description": "Block the 4/4 with your 3/3", "risk": "medium"},
      {"description": "Take 9 damage", "risk": "low"}
    ],
    "recommended": 1,
    "time-limit": 30
  }
}
```

## Visual Design

**Theme:** Dark, consistent with the classic games portal.

- Background: `#0d1117`
- Card background: `#161b22`
- Border: `#30363d`
- Text: `#c9d1d9`
- Accent blue: `#58a6ff`
- Accent green: `#3fb950` (life gain, valid)
- Accent red: `#f85149` (damage, invalid)
- Accent gold: `#d29922` (rare, Mana Coin)
- Accent orange: `#f0883e` (mythic)

**Card colors:** Color-coded borders and subtle background gradients
for each mana color. Multicolor cards get a gold border.

**Responsive:** Designed for desktop (1280px+), with tablet-friendly
layout for the game view (tap to select attackers/blockers).

## File Plan

```
tools/web/magic/
  index.html          -- dashboard / home
  collection.html     -- card collection + deck builder
  game.html           -- game view (battlefield, hand, controls)
  store.html          -- pack shop, ubiquitous market, direct sales
  queue.html          -- matchmaking queue
  profile.html        -- player stats, history, season progress
  magic.css           -- CodexMagic-specific styles
  magic.js            -- shared JS (card rendering, API helpers)
  card-render.js      -- card div builder (P/T/D, colors, rarity)

tools/web/
  server.ps1          -- extend with /api/magic/* routes
```

## Implementation Phases

**Phase 1 -- Playable game:**
- `game.html` with auto-play, step, posture controls
- Server API: `/game/new`, `/game/step`, `/game/state`
- Card rendering in JS
- Basic game state JSON serialization

**Phase 2 -- Collection and store:**
- `collection.html` with deck builder
- `store.html` with pack cracking
- Server API: `/store/crack`, `/deck/validate`, `/deck/generate`
- Pack opening animation

**Phase 3 -- Matchmaking and accounts:**
- `queue.html` with format/tier selection
- `profile.html` with stats and history
- `index.html` dashboard
- Server API: `/account/*`, `/queue/*`, `/season/*`

**Phase 4 -- Economy:**
- Trading UI in store
- Ubiquitous market
- Mana Coin balance integration
- Server API: `/trade/*`, `/ubiq/*`
