# Codex Games

A two-part games platform: a suite of 35 classic board and card games with AI opponents, and CodexMagic, a full-featured collectible card game with economies, clans, dungeons, and a universal multiverse registry.

## Classic Games (35 games)

### Abstract / Strategy
- **TicTacToe** — 3x3 rules + full-depth minimax (battery-verified: never loses, self-play always draws)
- **Connect4** — 6x7 board, iterative two-ply minimax
- **Checkers** — Full draughts rules with AI
- **Go** — 9x9 board, territory scoring
- **Othello** — 8x8 flip-disc game
- **Mancala** — Kalah variant
- **Hex** — 11x11 connection game
- **Game2048** — Sliding-tile puzzle
- **DotsAndBoxes** — Pen-and-paper line-claiming
- **Sudoku** — Constraint-propagation solver
- **Life** — Conway's Game of Life

### Card Games
- **Poker, PokerVariants** — Texas Hold'em, Seven-Card Stud
- **Pinochle, Bridge** — Trick-taking with melding/bidding
- **CrazyEights, GoFish, Blackjack, War, Spider** — Classic card games
- **Yahtzee** — Five-dice scoring
- **Mahjong** — Tile-draw-and-discard with hand scoring
- **SetGame** — Pattern-matching speed game
- **LiarsDice, RPS** — Bluffing games

### Strategy / Simulation
- **Backgammon, Monopoly, Risk** — Full rule implementations
- **HexWar** — Avalon Hill-style wargame engine with Stalingrad/Normandy/Bulge scenarios
- **Mastermind, RoyalUr** — Code-breaking and ancient Egyptian race game

### Infrastructure
- **Minimax** — Search engines: full-depth TicTacToe minimax + iterative two-ply Connect4, **Rng** — Deterministic LCG PRNG

## CodexMagic Platform (74 Codex files)

### Core Game Engine
- **Card, Mana, GameState, Engine, Combat, Action, Turn, Trigger, Stack** — Complete card game engine with turn phases, combat, and AI
- **General** — Commander/general identities
- **CardPool** — 100+ card templates across six colors
- **Deck, Distribution, Crafting** — Deck construction, pack distribution, wildcard crafting

### Economy and Tokens
- **Token, MintAuthority, ManaCoin, TransactionValidator** — NFT-style card tokens, in-game currency
- **ChainCore** — Hash-linked block structure for audit trail
- **PlanarExchange, CrossPlaneItems** — Cross-game item trading

### Multiplayer
- **Auth, PlayerIdentity, Matchmaking, UniversalMatchmaking, MatchRecord** — Accounts, ELO-based pairing, cross-game queues

### Clans
- **Clan, ClanAuthority, ClanEconomy, ClanLibrary, ClanFormat, ClanTournament, ClanChallenge, ClanPacks, ClanServer** — Full clan system with treasury, tournaments, custom packs
- **Season, SeasonalContent** — Quarterly season lifecycle

### RPG Layer
- **RPGEngine, CampaignWorld, DungeonRun, DungeonProgression, GMEconomy** — Tabletop RPG: classes, stat blocks, dungeon traversal, loot

### Server
- **MagicServer** — Bare-metal HTTP server dispatching 50+ API routes
- **Bridge, ServerUtil, EventBus** — Transport and pub/sub infrastructure

### Web UI
- **MagicTheme, MagicCardRender** — Theme and card rendering
- **GamePage, QueuePage, ProfilePage, CollectionPage, StorePage, MarketplacePage, WelcomePage, AdminPage, DeckTestPage** — Full set of SPA views

## Completeness

Classic: 90% — All 35 games are fully implemented with complete rule sets and AI. Web HTML shells present for all games.

CodexMagic: 75% — Core card game engine, economy, auth, clan, season, and HTTP API layers are fully wired. RPG/dungeon layer is structurally complete but not yet integrated into the server dispatch table. PlanarExchange and GameRegistry are designed but not hooked into the server.

## Codex Conformance

Full — All game logic, server, economy, and UI are written in Codex. The HTTP server is a CDX binary over the NE2K NIC. Web front-ends are emitted as HTML/JS bundles by the UI plug. One small non-Codex asset: `app.js` (104 lines) provides browser-side tab switching for the games portal index page.
