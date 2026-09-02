# Codex Games

A two-part games platform: a suite of 33 classic board and card games with AI
opponents, and CodexMagic, a full-featured collectible card game with economies,
clans, dungeons, and a universal multiverse registry.

Counts re-measured 2026-08-31 and they had been wrong. `games.json` holds 35
rows, and two of them are not classic games with an engine behind them: `chess`
is listed and NOT BUILT (GAME-10 in the backlog), and `magic` is the CodexMagic
platform below rather than a board game. Thirty-three ids have both a
`classic/<Name>.codex` engine and a `classic/web/<id>.html` shell, counted both
ways with neither side carrying an orphan. This file said 35 and the landing
page said 34; both were counting rows rather than games (L-COUNT).

## Classic Games (33 games)

### Abstract / Strategy
- **TicTacToe** -- 3x3 rules + full-depth minimax (battery-verified: never loses, self-play always draws)
- **Connect4** -- 6x7 board; the AI takes a win, blocks a loss, else favours the
  centre. `Minimax.codex` also holds a two-ply search for it (`c4-iterative-ai`)
  that nothing calls
- **Checkers** -- Full draughts rules with AI
- **Go** -- 9x9 board, territory scoring
- **Othello** -- 8x8 flip-disc game
- **Mancala** -- Kalah variant
- **Hex** -- 11x11 connection game
- **Game2048** -- Sliding-tile puzzle
- **DotsAndBoxes** -- Pen-and-paper line-claiming
- **Sudoku** -- Constraint-propagation solver
- **Life** -- Conway's Game of Life

### Card Games
- **Poker, PokerVariants** -- Texas Hold'em, Seven-Card Stud
- **Pinochle, Bridge** -- Trick-taking with melding/bidding
- **CrazyEights, GoFish, Blackjack, War, Spider** -- Classic card games
- **Yahtzee** -- Five-dice scoring
- **Mahjong** -- Tile-draw-and-discard with hand scoring
- **SetGame** -- Pattern-matching speed game
- **LiarsDice, RPS** -- Bluffing games

### Strategy / Simulation
- **Backgammon, Monopoly, Risk** -- Full rule implementations
- **HexWar** -- Avalon Hill-style wargame engine with Stalingrad/Normandy/Bulge scenarios
- **Mastermind, RoyalUr** -- Code-breaking and ancient Egyptian race game

### Infrastructure
- **Minimax** -- Search engines: full-depth TicTacToe minimax + iterative two-ply Connect4, **Rng** -- Deterministic LCG PRNG

## CodexMagic Platform (74 Codex files)

### Core Game Engine
- **Card, Mana, GameState, Engine, Combat, Action, Turn, Trigger, Stack** -- Complete card game engine with turn phases, combat, and AI
- **General** -- Commander/general identities
- **CardPool** -- 100+ card templates across six colors
- **Deck, Distribution, Crafting** -- Deck construction, pack distribution, wildcard crafting

### Economy and Tokens
- **Token, MintAuthority, ManaCoin, TransactionValidator** -- NFT-style card tokens, in-game currency
- **ChainCore** -- Hash-linked block structure for audit trail
- **PlanarExchange, CrossPlaneItems** -- Cross-game item trading

### Multiplayer
- **Auth, PlayerIdentity, Matchmaking, UniversalMatchmaking, MatchRecord** -- Accounts, ELO-based pairing, cross-game queues

### Clans
- **Clan, ClanAuthority, ClanEconomy, ClanLibrary, ClanFormat, ClanTournament, ClanChallenge, ClanPacks, ClanServer** -- Full clan system with treasury, tournaments, custom packs
- **Season, SeasonalContent** -- Quarterly season lifecycle

### RPG Layer
- **RPGEngine, CampaignWorld, DungeonRun, DungeonProgression, GMEconomy** -- Tabletop RPG: classes, stat blocks, dungeon traversal, loot

### Server
- **MagicServer** -- Bare-metal HTTP server dispatching 50+ API routes
- **Bridge, ServerUtil, EventBus** -- Transport and pub/sub infrastructure

### Web UI
- **MagicTheme, MagicCardRender** -- Theme and card rendering
- **GamePage, QueuePage, ProfilePage, CollectionPage, StorePage, MarketplacePage, WelcomePage, AdminPage, DeckTestPage** -- Full set of SPA views

## Completeness

Classic: 90% -- All 33 games are fully implemented with complete rule sets and
AI, and each has a web HTML shell. Chess is listed in `games.json` and is not
one of them (GAME-10).

## In the browser

The shells under `classic/web/` reach their engine through `server.ps1`, a
local PowerShell bridge to `GameServer.cdx` running in codex-vm. That needs a
machine running the server, so it is not how a visitor to the public site plays.

For the public site the engine is compiled to WebAssembly instead and the page
calls it directly:

    pwsh apps/games/build-wasm.ps1 -Game tictactoe

`build-wasm.ps1` holds one row per game -- the wasm-facing chapter and the
functions to export -- and runs source -> IR-CCE -> `codex/plugs/wasm` -> WAT ->
`wat2wasm`. `apps/landing/build.ps1` calls it while assembling the site, so the
module is build output rather than a tracked binary. Adding a game is a wasm
chapter plus a row plus a page under `apps/landing/web/games/`.

The wasm-facing chapter exists because the module keeps no state between calls:
`TicTacToeWasm.codex` packs the whole game into one integer the page holds, so
the page can reset the module's bump allocator before every call. There is no
collector in a wasm module either.

Two graders, and neither is optional:

    node apps/games/wasm-verify.mjs   # the module against the battery's answer key
    node apps/games/page-test.mjs     # the shipped page's own script against the module

`wasm-verify.mjs` replays `codex/test/ttt-perfect.expected` -- the exhaustive
walk, 92 games as X and 569 as O with zero losses -- through the wasm exports,
so "it assembled" is never read as "it computes". `page-test.mjs` reads the
script out of the shipped HTML rather than copying it, so it cannot drift from
what is served. Both carry a control that fires.

A game whose module will not build is a PARITY finding for the wasm plug lane,
not something to work around here: report the game and the failing step and
leave the game's Codex source alone.

CodexMagic: 75% -- Core card game engine, economy, auth, clan, season, and HTTP API layers are fully wired. RPG/dungeon layer is structurally complete but not yet integrated into the server dispatch table. PlanarExchange and GameRegistry are designed but not hooked into the server.

## Codex Conformance

Full -- All game logic, server, economy, and UI are written in Codex. The HTTP server is a CDX binary over the NE2K NIC. Web front-ends are emitted as HTML/JS bundles by the UI plug. One small non-Codex asset: `app.js` (104 lines) provides browser-side tab switching for the games portal index page.
