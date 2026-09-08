# Network -- Distributed Game State and Infrastructure

## Overview

CodexMagic runs on a distributed server network with no single point of
failure. Game state is resolved by server clusters, match results are
posted to the blockchain, and the full game history is auditable and
replayable by anyone. The network supports phone, tablet, and PC
clients connecting from anywhere.

## Architecture

```
┌─────────┐  ┌─────────┐  ┌─────────┐
│ Client  │  │ Client  │  │ Client  │
│ (phone) │  │(tablet) │  │  (PC)   │
└────┬────┘  └────┬────┘  └────┬────┘
     │            │            │
     └────────┬───┴────────────┘
              │
        ┌─────▼──────┐
        │  Gateway    │  Edge routing, auth, session
        │  Layer      │
        ├────────────┤
        │  Match      │  Game state resolution,
        │  Servers    │  rules engine execution
        ├────────────┤
        │  State      │  Distributed state store,
        │  Layer      │  consensus, replication
        ├────────────┤
        │  Chain      │  Blockchain posting,
        │  Bridge     │  token operations
        └────────────┘
```

## Game State Resolution

The rules engine is deterministic -- given the same game state and the
same action sequence, it always produces the same result. This property
is the foundation of the distributed model.

### Match Lifecycle

1. **Matchmaking** -- the matchmaking service pairs players by rank,
   format, and latency. Assigns the match to a server region.

2. **Match initialization** -- a match server loads both players' decks
   (verified from on-chain collection), initializes the game state,
   generates the shuffle seed from a commit-reveal scheme.

3. **Play** -- the match server runs the rules engine. Player inputs
   (posture changes, manual interventions, land selections) arrive as
   signed messages. The AI supervisor produces actions on each player's
   behalf within their posture constraints. Every state transition is
   logged.

4. **Resolution** -- when the game ends, the match server produces a
   match record: final state, action log, winner, game stats.

5. **Chain posting** -- the match record hash is posted to the blockchain.
   The full action log is stored in the distributed state layer and is
   retrievable by hash. Anyone can replay the match by feeding the
   action log into the rules engine and verifying the outcome.

### Deterministic Replay

Because the engine is pure, any match can be replayed:

```
replay : ActionLog -> GameState
replay log =
  fold apply-action initial-state log.actions
```

Replay verification is the dispute resolution mechanism. If a player
claims the server produced an incorrect result, anyone can replay the
action log and check. The match record hash on-chain commits the server
to a specific outcome -- if replay produces a different result, the
server is provably wrong.

### Server Redundancy

Match state is replicated to multiple servers during play:

- **Primary** -- runs the rules engine, processes inputs
- **Witness (2+)** -- receives state snapshots, validates transitions,
  can take over if the primary fails
- **Handoff** -- if the primary goes down, the first witness that
  confirms state consistency promotes itself. The match continues
  with minimal interruption.

Players never interact with a single server. The gateway layer routes
to the best available server and handles failover transparently.

## Shuffle Seed Generation

Shuffle randomness must be verifiably fair -- neither player nor server
can manipulate it.

**Commit-reveal protocol:**
1. Each player generates a random nonce and sends its hash (commit)
2. Both commits are collected
3. Each player reveals their nonce
4. The shuffle seed = hash(nonce_A || nonce_B || match_id)

Neither player can influence the seed without the other's cooperation.
The server cannot manipulate it because it doesn't contribute to the
seed. Both nonces are stored in the match record for verification.

## Matchmaking

```
MatchRequest = record {
  player : AccountId,
  format : Format,
  deck-hash : Hash,
  rank : RankBracket,
  region-preference : Region,
  timestamp : Timestamp
}
```

**Formats:**
- **Constructed** -- bring a deck from your collection (60 cards min)
- **Draft** -- open 3 packs, pick cards, build a deck (40 cards min)
- **Sealed** -- open 6 packs, build a deck (40 cards min)
- **Standard** -- current season + previous season cards only
- **Vintage** -- all cards ever printed
- **Tournament** -- ranked competitive, entry fee, prizes

**Rank brackets:**
Ranked play uses an Elo-like rating system. Brackets exist to keep
matchmaking fair: Bronze, Silver, Gold, Platinum, Diamond, Master,
Grandmaster. Rank resets partially at season boundaries.

## Cross-Platform

All clients connect to the same network. Account state is on-chain --
log in from any device and your full collection, decks, rank, and
match history are there.

**Client responsibilities:**
- Render game state from server snapshots
- Capture player inputs (posture, interventions)
- Display AI supervisor decisions and offer intervention points
- Animate card play, combat, effects

**Client does NOT:**
- Run the rules engine (server-authoritative)
- Store game state locally (all from server)
- Generate randomness (server + commit-reveal)

The client is a view. This prevents cheating, ensures consistency,
and means a lost phone doesn't lose a match in progress.

## Latency and Responsiveness

The AI supervisor model helps with latency. Because the AI plays
obvious moves without waiting for player input, the game progresses
even on high-latency connections. Player interventions are
asynchronous -- the AI pauses at decision points and waits, but routine
play flows without round-trips per action.

**Target latencies:**
- Action acknowledgment: < 200ms
- State sync: < 500ms
- AI decision + animation: 1-3 seconds per action
- Player intervention timeout: 30 seconds (configurable)

## Replay Storage

Full action logs are stored in a content-addressed distributed store.
The chain stores only the match record hash -- the full log is
retrievable by hash from any node in the network.

**Retention:**
- Current season: all replays available
- Previous seasons: top-1000 ranked matches, tournament matches
- All time: tournament finals, record-breaking matches
- Players can pin their own match replays (costs Mana Coin for storage)

Replays are viewable in the client with full animation, pause,
rewind, and speed controls. Tournament replays are public -- anyone
can watch and learn.

## Clan and Tournament Integration

Clan tournaments and inter-clan challenges use the same network
infrastructure. Clan matches are recorded on-chain with clan
provenance metadata. See [Clans.md](Clans.md) for clan tournament
and challenge definitions, and [Formats.md](Formats.md) for the
format configurations that matchmaking supports.
