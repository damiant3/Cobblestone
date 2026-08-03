# Planar Exchange -- Kickstarter Marketing Plan

## The Pitch (30 seconds)

**Planar Exchange** is an independent game development and distribution
platform built on a new programming language and operating system
designed for rapid AI development and secure deployment -- independent
of any cloud or host provider.

Game developers build games on the platform. Players play them, collect
items, and trade across every game in the ecosystem. A sword you win in
a dungeon crawler can become a creature card in a card game, a starship
component in a space sim, or whatever a game designer decides it means
in their world. Items live on a public blockchain -- not for mining or
speculation, but as a **database and public record** that establishes
ownership. The NFT is how games know the item is yours.

The first game on the platform is **CodexMagic**, a fantasy TCG where
AI-controlled Generals execute your strategy. But the platform is the
product. I build the substrate. The community builds the games.

---

## Part 1: The Platform

### What Planar Exchange Is

Planar Exchange is three things:

1. **A game economy** -- a cross-game marketplace where items flow
   between games with full provenance. Every item has a permanent
   identity, an owner, and a history. The blockchain is the public
   ledger -- it's a database, not a mining operation or a Ponzi scheme.
   No one "mines" ManaCoin. It's minted by the platform when value is
   created (items crafted, games played, content published) and burned
   when consumed (marketplace fees, service costs). I don't run the
   whole database -- it's a distributed public record that anyone can
   verify.

2. **A game construction kit** -- the platform ships a universal rules
   engine that clans and developers configure through data, not code.
   Formats, house rules, card pools, art themes, content restrictions,
   age brackets -- all parameterized. A church youth group runs
   stained-glass angel cards with EmotesOnly chat. A competitive clan
   runs full-complexity Modern with Deathtouch and ranked ladders. A
   sci-fi clan reskins everything as robots and lasers. Same engine.
   Different experience. Games also plug into the shared economy by
   defining **item translation tables** -- a +5 Holy Avenger becomes
   Excalibur in a medieval game, or a Plasma Saber in a sci-fi mod.

3. **A safe, age-appropriate community** -- real age brackets
   (Child/Teen/Adult) with enforced communication tiers
   (EmotesOnly/Moderated/Unmoderated), parental controls, guardian
   accounts, play-time limits, spending caps, and COPPA-level
   protections for kids. Clans span every game on the platform. Your
   reputation, your collection, and your clan travel with you -- but
   your 10-year-old never sees unmoderated chat.

### How Items Cross Game Boundaries

Every item on the platform has a **canonical identity**: a type, a
rarity, a set of base attributes, and a provenance chain. When a game
designer builds a new game, they write a translation table:

```
Dragon Creature Card (Legendary, ATK 5, DEF 3)
  → in DungeonCrawl:  Dragon Mount (fly speed 60, fire breath 3d6)
  → in SpaceFleet:    Dragon-class Carrier (shields 500, hangar 3)
  → in ChessWars:     Dragon Piece (moves like queen, captures 2 tiles)
  → unrecognized:     Legendary Trophy (display only, trade value preserved)
```

If a game doesn't define a translation for an item, it appears as a
generic trophy -- still ownable, still tradeable, still carrying its
full provenance and rarity. No item ever becomes worthless because a
game shuts down. The item exists on the chain independent of any single
game.

### The Blockchain Is a Database

The blockchain is not a cryptocurrency play. It's a **distributed
public record** with specific properties that matter for game items:

- **Ownership is verifiable** -- anyone can check who owns what without
  asking a central server
- **Provenance is permanent** -- the full history of an item (who
  created it, who owned it, what games it's been used in) is
  immutable and auditable
- **No single point of failure** -- if I get hit by a bus, the item
  ledger survives. No company shutdown wipes your collection.
- **Games are peers, not tenants** -- game developers don't need my
  permission to read the chain. They can build on the platform
  without a business relationship with me.

### Who Controls What

**We control the cards.** Official card and item creation is centralized.
We design the cards, we set the rarity tables, we sell the packs. This
is how the operation is funded. When a player opens a booster pack,
they're pulling from an RNG machine we built -- the items they "make"
are pulls, not creations. Every official card is minted by us as an NFT
on the main chain.

**Game makers interpret official cards.** When a third-party developer
builds a game on the platform, they write translation tables that map
our official cards and items into their game's mechanics. They don't
create official cards -- they create experiences where our cards gain
new meaning. This is the value proposition for players: buy a card once,
use it in every game that supports it.

**Game makers can publish their own items** -- but on their own sidechain,
not the main chain. Each game gets its own sidechain coin, which the
developer purchases from us. Their items trade on their sidechain and
can cross to the main chain through the Planar Exchange at a conversion
rate we set. This means:
- We control the main economy (ManaCoin)
- Game developers have autonomy within their games
- No one can inflate the main currency by minting on a sidechain
- Developers invest in the ecosystem when they buy sidechain coin

**ManaCoin is the platform currency.** It's not speculative -- it's the
unit of account. Items are priced in ManaCoin, marketplace rake is
collected in ManaCoin, and sidechain coins are purchased with ManaCoin.
We are the central bank. The blockchain is the public ledger that
proves it.

---

## Part 2: The Technology

### Built on Codex -- A New Language and OS

The platform runs on **Codex**, a new programming language and
bare-metal operating system built from first principles. Codex is:

- **A self-sustaining compiler** -- Codex compiles itself, on bare metal,
  with no OS, no runtime, no libc. The compiler is a fixed point of
  itself: the binary it produces is byte-identical to itself.
- **Designed for AI development** -- 19-module ML library (tensors,
  transformers, attention, sampling, optimizers), GPU proxy
  architecture, built-in tokenizer and inference pipeline.
- **Cloud-independent** -- runs on bare metal x86-64. No AWS, no Azure,
  no GCP dependency. Deploy on any hardware you control. Boot from USB,
  SSD, or network. The OS, compiler, and runtime are one artifact.
- **Security by design** -- linear types (use-after-free is a type
  error), capability-based effects, cryptographic identity (Ed25519),
  trust lattice for code provenance. No buffer overflows. No injection
  attacks. No uninitialized memory. These aren't runtime checks -- they're
  compile-time guarantees.

### Why This Matters for Game Developers

Building on Codex means:
- **No cloud bills** -- deploy on a $200 mini-PC or a rack server.
  Same binary. No containerization, no orchestration, no vendor lock-in.
- **No runtime surprises** -- if it compiles, the memory model is sound.
  No GC pauses. No mysterious OOMs. The compiler proves your resource
  usage.
- **AI-native** -- build games with embedded inference. NPCs that
  actually think. Procedural content that's actually procedural.
  The ML stack is in the standard library, not a third-party dependency.
- **Rapid iteration** -- the Codex build pipeline compiles, tests, and
  deploys in minutes. The language reads like prose. The type system
  catches design errors, not just syntax errors.

---

## Part 3: The Economy

### How Money Flows

```
Player buys pack ($2.99 real money)
  → We mint official cards as NFTs on the main chain
  → Player uses cards in CodexMagic (or any game that translates them)
  → Player wins dungeon run, pulls loot from our RNG tables (more NFTs)
  → Player lists items on the Planar Exchange
  → Another player buys them for ManaCoin
  → We take 10% rake
  → Seller spends ManaCoin on more packs, or in a third-party game

Game developer wants to build on the platform:
  → Developer buys sidechain coin from us (with ManaCoin or real money)
  → Developer mints their own game-specific items on their sidechain
  → Players in that game earn and trade sidechain items
  → Sidechain items can cross to the main chain at our conversion rate
  → We take a cut on every cross-chain transfer
```

Every transaction is on the public ledger. Every item has provenance.
We control the main economy. Game developers operate within it.

### Revenue Streams

| Stream | Model | Notes |
|--------|-------|-------|
| **ManaCoin Sales** | Players buy ManaCoin with real money | Primary revenue driver. All in-game spending is in ManaCoin. |
| **Official Card Packs** | Standard (15 cards), Premium (3x price, guaranteed Rare+), Draft (45 cards), Vintage (previous season) | We design, mint, and sell all official cards via AI pipeline. |
| **Subscriptions** | Bronze → Silver → Gold → Platinum | Rarity upgrade bonuses on pack opens (+1 to +5 card bumps per pack). Recurring revenue. |
| **Ubiquitous Rake** | 15-20% of every Ubiquitous copy sale | Players who pull a Ubiquitous token can publish copies of their cards. We take a cut of every sale. Revenue scales with player-driven publishing activity. |
| **Clan Pack Fees** | 30% platform fee on all clan pack sales | Clans design themed packs (art, mechanics, content restrictions). We take 30% of every pack cracked. |
| **Planar Exchange Rake** | 10% on all marketplace transactions | Every trade on the main chain pays us. A percentage is burned for deflationary pressure. |
| **Direct Card Sales** | Starter decks, season spotlights, event cards | Fixed-price cards we control. Onboarding + seasonal monetization. |
| **Tournament Entry Fees** | Portion of competitive entry fees | Rest goes to prize pools. |
| **Sidechain Coin Sales** | Per-developer pricing | Game developers buy their sidechain currency from us. |
| **Cross-Chain Transfer Fees** | % on sidechain → main chain transfers | We set the conversion rate and take a cut. |

### The Ubiquitous Card System

One of the most interesting economic mechanics: packs have a bonus
slot that can contain a **Ubiquitous token** (roughly 1 in 50 packs).
When a player pulls one, they can attach it to any card they own,
making that card **copiable**. The original becomes the master copy.
Other players buy freshly minted copies at a price the owner sets.
The owner earns passive income. We take 15-20% of every sale.

This turns players into card publishers. A player who pulls a
high-demand card and a Ubiquitous token sets the price, controls
supply (can delist anytime), and earns ManaCoin as copies sell. The
master copy itself becomes more valuable -- it's the origin, and its
provenance shows it as the source of all copies.

### Card Prominence

Every card has a **prominence score** -- a derived metric computed
from game utility (win-rate contribution), effect rarity (uniqueness
of mechanic combination), art reception (community ratings, trade
velocity), and tournament history (top-8 finishes, decisive plays).
High-prominence cards get visual treatments (glow effects, border
treatments, badges) and command premium market prices. Prominence is
computed from on-chain data -- anyone can verify it.

### What We Don't Do

- No mining -- ManaCoin is minted by us, not mined by speculators
- No gas fees for players -- transaction costs are absorbed by the rake
- No speculative token sales -- ManaCoin is a unit of account, not an investment
- No hidden odds -- pack rarity tables are published (1-in-8 Mythic, 1-in-1000 Legendary-Mythic)
- No pay-to-win -- purchased items play identically to earned items
- No item nerfs -- broken cards get flowers and exile from official play, never stealth-patched. Clans can still play anything.
- No unauthorized minting -- only we create official cards; game devs mint on sidechains
- No direct ManaCoin-to-cash-out from us -- secondary markets at player's own risk

---

## Part 4: The First Game -- CodexMagic

### What It Is

CodexMagic is a **card game construction kit** -- a universal engine
that clans and communities configure into their own game experiences.
The flagship format is a fantasy TCG where AI-controlled Generals
execute your strategy. But the same engine runs a 30-card speed game
with robots, a kids-only game with stained-glass angels, a
competitive 100-card singleton format, or whatever a clan designs.
The engine is one thing. The experiences are infinite.

### AI-Supervised Play

Each General carries behavioral modifiers -- stance, combat bias,
trigger thresholds, spell priorities, resource hoarding tendencies.
You build the deck, pick the General, set the strategy. The General
fights the battle. Games resolve in 5-8 minutes with 25-35
meaningful decisions. The spell stack is hidden -- the AI resolves it,
the player sees decision points.

Five launch Generals:

| General | P/T/D | Life | Style |
|---------|-------|------|-------|
| Warlord of the Iron Banner | 4/5/1 | 30 | Aggressive, Haste |
| High Sage of the Azure Spire | 2/4/3 | 30 | Control, resource hoarding |
| Verdant Keeper | 3/6/2 | 35 | Defensive, high toughness |
| Shadow Prince | 3/3/0 | 25 | Tempo, Deathtouch |
| Dawn Commander | 3/5/2 | 30 | Balanced, Vigilance |

### Defense Threshold

Damage at or below a creature's Defense stat deals zero. A knight
with Defense 2 shrugs off a 2-power attacker but takes full damage
from power 3+. Defense is scarce at common rarity and premium at
rare+. Deathtouch pierces defense, providing the counter.

### AI Content Pipeline

Every card is AI-generated through a 5-stage pipeline:

1. **Mechanic Invention** -- AI designs new keywords within the
   season's theme and complexity budget, tuned against metagame data
2. **Card Assembly** -- individual cards are generated with balanced
   mana curves, creature/spell ratios, P/T/D distributions, and
   cross-color synergies
3. **Effect Codegen** -- card abilities compiled into type-safe,
   deterministic effect code that the rules engine executes
4. **Art Generation** -- per-card art via LoRA pipeline, consistent
   style per season, clan-specific themes
5. **QA Gate** -- automated (compiles, type-checks, 1000-game Monte
   Carlo simulation) + human review for Rare and above

Cards are immutable once minted. Never edited, never nerfed. When a
card breaks the meta so hard it's ruining everyone's fun, we don't
quietly patch it away. We **give it flowers** -- a ceremony
acknowledging how absurdly powerful it was -- and then we **exile** it
from official play. The card gets a permanent "Exiled" badge on-chain,
its tournament highlight reel is preserved, and its prominence score
is frozen at peak. It's not a punishment. It's a retirement party
for a card that was too good for this world.

Exiled cards keep their identity, provenance, and trade value -- often
they become *more* valuable as collector pieces. They just can't
compete in centrally sponsored formats anymore.

But exile is only our call for official play. Clans run whatever they
want. Their ban list is their business. Your Exiled Legendary is
still legal in clan events, house-rule formats, and casual play. If
you think it's fun and the official format disagrees, find a clan
that agrees with you -- or found one.

### The Construction Kit

The game engine accepts a `FormatDef` (deck size, copy limit, pool
rules) and a `RuleSet` (mulligan, turn cap, sudden death). Clans
layer `HouseRule` overrides on top. Some configurations:

| Configuration | What you get |
|--------------|-------------|
| Modern + default rules | Flagship ranked experience |
| Blitz + 8-turn cap + 15 life | 3-minute mobile games |
| Singleton + 100 cards + 30 life | Commander-style format |
| Ban all keywords except Flying | Air-combat-only variant |
| Kids clan + Clean + StainedGlass art | Church youth group game |
| Adult + Unrestricted + CyberPunk art | Full-expression experience |

Every clan is an experiment in game design. Successful house rules
can be promoted to global formats. The metagame evolves from what
clans discover, not from top-down designer edits.

### Clan-Themed Packs

Clans design their own pack products: mechanics, art style, creature
types, flavor, profanity level. A church youth group sells "Shepherd's
Light" packs with stained-glass art and Angel creatures. A sci-fi clan
sells "Mech Assault" packs with circuit-board borders and robot
creatures. Cards are mechanically interchangeable -- same P/T/D, same
keywords -- just themed differently. We take 30% of every clan pack
sold. Clan treasury gets 40-60%.

### Cross-Game Items

Every card and item is an NFT on the Planar Exchange. Other games
translate them:

- A Legendary creature card → a boss monster in a dungeon crawler
- A rare enchantment → a passive buff in an RPG
- A mythic artifact → a unique ship module in a space game

Your collection isn't locked into one game. It's the seed of your
cross-platform inventory.

---

## Part 5: What's Already Built

This is not a pitch for something that might exist someday. The
platform and its first game are **14,400+ lines of working code across
56 source files**, with 20 major systems implemented and running on
a partial alpha server stack.

The whole thing is built on the Codex compiler -- itself a 29,000-line
self-sustaining bare-metal compiler across 53 files, with 235+ standard
library modules. The compiler is a hard fixed point of itself. The
language is real. The OS boots on real hardware.

### Platform Systems (working code)

| System | Files | What It Does |
|--------|-------|--------------|
| **Blockchain ledger** | ChainCore, TransactionValidator | Hash-linked blocks, merkle trees, transaction validation |
| **Economy** | GMEconomy, PlanarExchange, ClanEconomy | ManaCoin ledger, cross-game marketplace, clan currencies |
| **Token minting** | Token, MintAuthority | NFT creation, provenance chains, ownership records |
| **Game registry** | GameRegistry (424 LOC) | Multi-game registration, cross-game state management |
| **Cross-game items** | CrossPlaneItems | Item translation framework between games |
| **Server infrastructure** | MagicServer, PlaneServer, ClanServer | 3 server types, 2,300+ LOC combined |
| **Matchmaking** | Matchmaking, UniversalMatchmaking | Queue, ranking, cross-format matching |
| **Clan system** | 9 files, largest subsystem | Roles, governance, shared libraries, tournaments, economy |
| **Auth & identity** | PlayerIdentity, Auth | Cryptographic identity, authentication |
| **Leaderboards** | Leaderboard, MatchRecord | Ranking, match history |
| **Seasons** | Season, SeasonalContent | Content rotation, card pools, exile ceremonies |
| **Event system** | EventBus, Trigger, Action, Bridge | Cross-system event routing |

### CodexMagic Game Systems (working code)

| System | Files | What It Does |
|--------|-------|--------------|
| **Card engine** | Card, CardPool, Deck, Mana | Types, mana costs, abilities, deck validation |
| **Game engine** | Engine, GameRules, GameState, Turn | Turn loop, priority, phases, state-based actions |
| **AI supervisor** | Supervisor, General | Behavioral modifiers, posture system, decision logic |
| **Combat** | Combat | Attack/block, Defense Threshold, keywords, damage |
| **Spell stack** | Stack | LIFO resolution, targeting, mana abilities |
| **Dungeon engine** | DungeonRun, DungeonProgression | Floor generation, encounters, loot, boss fights |
| **RPG layer** | RPGEngine, CampaignWorld (372 LOC) | Classes, races, stats, skills, XP, equipment |
| **Crafting** | Crafting | 10 material categories, recipes, quality tiers |
| **Simulation** | Simulate, SimRunner, SimDb, SimBaseline | Automated game simulation, balance testing |

### The Codex Stack (the foundation under all of this)

| Layer | Scale | Status |
|-------|-------|--------|
| **Codex compiler** | 29,000 LOC, 53 files | Hard fixed point -- compiles itself byte-identically |
| **Standard library** | 235+ modules across 9 quires | Core types, crypto, ML, encoding, UI, game, signal, sim |
| **OS kernel** | 22 modules | Bare-metal drivers, scheduler, IPC, filesystem |
| **Operating system** | 51 modules | Networking, trust lattice, observability, replay |
| **Tools & apps** | 53 modules | Agents, build system, web server, test harness, UEFI boot |
| **codex-vm** | ~4,500 LOC (C) | Full x86-64 hypervisor with PCI, USB, NIC, audio, display |

### What the Kickstarter Funds

The hard part -- the engine, the AI, the economy, the blockchain, the
compiler, the OS -- is already written and working. What's needed to go
from partial alpha to public beta:

- **Web client UI** -- game logic is server-side; needs a front-end
- **Network play** -- state sync, reconnection, spectator mode
- **Art pipeline** -- LoRA training and batch generation for 200-card set
- **Polish** -- sound, music, animations, onboarding
- **Infrastructure** -- servers, scaling, monitoring

---

## Part 6: The Open Build Model

### Always Buildable, Always Forkable

The platform doesn't follow the traditional roadmap of milestones →
alpha → beta → launch → DLC. It's a **continuous open build**. At any
point, the code compiles, the game runs, and someone new can clone the
repo and start building.

This is a deliberate architecture decision. The platform layer -- the
economy, identity, item system, clan infrastructure -- is built as
clean abstractions that are stable, tested, and documented. Everything
above that is content and games. Games are data-driven. Content is
user-generated.

### What I Build vs. What the Community Builds

**I build the substrate:**
- The Codex language and compiler
- The bare-metal OS and runtime
- The Planar Exchange economy and blockchain
- The item identity and cross-game translation framework
- The clan, tournament, and social infrastructure
- The first game (CodexMagic) as proof of concept

**The community builds on top:**
- New games that plug into the Planar Exchange
- Item translation tables for their games
- Cards, creatures, items, dungeon templates, crafting recipes
- Art (LoRA-generated), sound, music
- Formats, house rules, tournament structures
- Entire new game genres that share the economy

### Why This Works

Most Kickstarters ask you to fund a finished product. If the developer
disappears, the money's gone. Planar Exchange is different:

- The code is always buildable -- anyone can pick it up and continue
- The items live on a public chain -- no company shutdown wipes collections
- The games are peers -- they don't depend on a central server I control
- The architecture is the product, not a release date

Backers aren't waiting for a "launch." They can play what exists today.
Every dollar funds the next abstraction, which unlocks the next layer
of community content and community games.

---

## Part 7: Kickstarter Campaign

### Campaign Goal: $50,000

This funds **me, full-time, building the platform** -- the abstractions
that make everything else possible. The game is already playable. The
Kickstarter buys dedicated time to harden the core, build the network
layer, and ship the first polished content set.

### What $50K Buys

- 6 months of full-time platform + engine development (my salary)
- Network play infrastructure (matchmaking, state sync, replay)
- AI art pipeline buildout (custom LoRAs, ComfyUI batch workflows)
- GPU compute for art generation (local + cloud burst)
- Server infrastructure for the Planar Exchange
- Sound design and music for the base CodexMagic content set

### Stretch Goals -- Building a Team

Stretch goals don't unlock features -- they unlock **people**. More
funding means I stop being a solo operation and start building a team.

| Amount | Hire | What This Changes |
|--------|------|-------------------|
| $75,000 | **Operations / Admin** -- Kickstarter fulfillment, backer comms, financials, legal, business registration, tax filings, vendor contracts. Frees me to write code instead of chasing invoices. | I stop losing 15+ hours/week to non-engineering work. |
| $125,000 | **PM / QA Lead** -- owns the backlog, triages community submissions, runs the test pipeline, manages seasonal content, coordinates community playtests. | Content quality goes up. Release cadence becomes predictable. |
| $200,000 | **Second Developer** -- another engineer on the platform. Network layer, mobile client, performance, or whatever the highest-priority gap is. | Development velocity doubles. Engine and network stack in parallel. |
| $300,000 | **Full team runway** -- extends all three hires from 6 to 12 months, plus server scaling, expanded GPU compute, and a real marketing push. | A full year of sustained development with a real team. |

### Reward Tiers

| Tier | Price | Reward |
|------|-------|--------|
| **Recruit** | $10 | Platform access, name in credits, Discord role |
| **Soldier** | $25 | + Kickstarter-exclusive General NFT (limited edition) |
| **Knight** | $50 | + 10 Kickstarter booster packs (exclusive card backs), early access Discord |
| **Commander** | $100 | + Design-a-card: submit a card concept, I build it (name, art, stats) |
| **General** | $250 | + Design-a-General: custom behavioral modifiers and LoRA-generated art |
| **Founder** | $500 | + Found a Clan (pre-registered name, custom sigil, Founder role), all lower tiers |
| **Patron** | $1,000 | + Name an in-game location, private design session, all lower tiers |

### Early Bird

First 500 backers at Knight tier or above get a **Founder's Edition card
back** (animated, non-tradeable NFT, permanent flex).

---

## Budget Allocation ($50K)

The biggest line item is the engineering work. I build the platform
substrate -- the economy, blockchain, item system, compiler, OS,
and the first game. That's what this funds.

All art is 100% AI-generated. The "artist" is the model builder and
LoRA designer -- curating training datasets, training custom models,
building ComfyUI batch pipelines. This scales to 200+ cards without
paying per-illustration.

| Category | Amount | % |
|----------|--------|---|
| Platform + engine development (my time -- 6 months) | $25,000 | 50% |
| AI art pipeline (LoRA training, GPU compute) | $3,000 | 6% |
| Server infrastructure (12 months) | $6,000 | 12% |
| Sound design and music | $3,000 | 6% |
| Marketing and PR | $4,000 | 8% |
| Kickstarter + payment processing fees (8-10%) | $5,000 | 10% |
| Contingency | $4,000 | 8% |

If stretch goals hit, the additional funds go to **hiring people** --
see the stretch goal table. The $50K base is me, solo, for 6 months.
Every stretch goal adds a person to the team.

---

## Campaign Timeline

### Pre-Launch (Weeks -6 to -1)

| Week | Action |
|------|--------|
| -6 | Landing page live, email list open, social accounts active |
| -5 | Dev diary #1: "What is Planar Exchange?" -- platform architecture |
| -4 | Dev diary #2: "Cross-Game Items" -- how item translation works |
| -3 | Dev diary #3: "CodexMagic" -- the first game, card reveals |
| -2 | Playable demo (single AI match, 3 preset decks) on itch.io |
| -1 | Press kit sent to gaming press, crypto/gaming outlets, indie devs |

### Launch Day

- Kickstarter goes live at 10 AM EST (Tuesday)
- Launch trailer (90 seconds): Platform overview → item crossing games →
  CodexMagic gameplay → pack opening → marketplace
- Reddit: r/games, r/gamedev, r/IndieGaming, r/cryptocurrency,
  r/magicTCG, r/digitaltcg
- Press embargo lifts

### During Campaign (30 days)

| Cadence | Content |
|---------|---------|
| Daily | Social post (card reveal, platform feature, backer milestone) |
| 3x/week | Short-form video: 15-30s gameplay or item translation demo |
| Weekly | Dev diary: deep dive (economy, Codex language, AI, clans) |
| Week 2 | "Build a game on Planar Exchange" stream for developers |
| Week 3 | Backer-exclusive livestream: live demo, Q&A |
| Final 48h | "Last chance" push |

### Post-Campaign

No "post-campaign silence." The build is continuous:
- Weekly commits visible in the public repo
- Monthly dev diary with video
- Community game submissions open immediately
- Backers at Knight+ get early access to new platform features

---

## Pitch Deck Slides

| Slide | Image | Content |
|-------|-------|---------|
| 1. Title | `11_combat_defense.png` | Planar Exchange logo, tagline: "Your Items. Every Game. Real Ownership." |
| 2. The Problem | (text only) | "Your game items are trapped. Shut down a game, lose everything. Your collection should outlive any single game." |
| 3. The Platform | (diagram) | Planar Exchange architecture -- games as peers, shared economy, public chain |
| 4. Cross-Game Items | (diagram) | Item translation: same NFT, different expressions per game |
| 5. The Technology | (diagram) | Codex language + OS -- bare metal, AI-native, cloud-independent |
| 6. The Economy | (diagram) | Money flow: player → packs → items → marketplace → developers |
| 7. CodexMagic | `07_card_warlord.png` | The first game -- card anatomy, P/T/D, Defense Threshold |
| 8. Gameplay | `10_general_select.png` | General select, AI-supervised play |
| 9. In Action | `08_battlefield_cards.png` | Cards on the table, game in progress |
| 10. Items & Loot | `09_loot_items.png` | Cross-game loot, rarity tiers |
| 11. What's Built | (text) | 14,400 LOC game + 29,000 LOC compiler + 235 library modules |
| 12. The Ask | `12_pack_opening.png` | $50K goal, stretch goals (hiring), reward tiers |
| 13. Team | (photo) | Your photo, background, why you're building this |

---

## Marketing Channels

### Owned

- **Landing page**: planarexchange.com -- email capture, trailer, FAQ
- **Discord**: community hub, dev announcements, game dev channels
- **YouTube**: dev diaries, gameplay, platform architecture walkthroughs
- **Twitter/X**: card reveals, platform features, milestone celebrations
- **TikTok/Reels**: short gameplay moments, item translation demos

### Earned

- **Gaming press**: Kickstarter-focused outlets, indie game coverage
- **Crypto/gaming press**: Decrypt, CoinDesk gaming, NFT-focused outlets
  (emphasis: "blockchain as DB, not speculation")
- **Gamedev communities**: r/gamedev, itch.io, indie dev discords --
  "build a game on a platform with a built-in economy"
- **TCG communities**: r/magicTCG, r/digitaltcg, auto-battler discords
- **Reddit**: organic dev engagement, not ads

### Paid (if budget allows)

- **Reddit ads**: r/gamedev, r/magicTCG, r/IndieGaming ($500-1000)
- **YouTube pre-roll**: on gamedev and TCG channels ($1000-2000)
- **Influencer sponsorship**: 1-2 mid-tier channels for launch ($500-1500)

---

## Key Metrics

| Metric | Target | Tool |
|--------|--------|------|
| Email signups (pre-launch) | 2,000+ | Mailchimp / landing page |
| Day 1 funding | 30% of goal ($15K) | Kickstarter |
| Backer count | 1,000+ total | Kickstarter |
| Average pledge | $40-50 | Kickstarter |
| Demo downloads | 5,000+ | itch.io |
| Discord members | 500+ during campaign | Discord |
| Social impressions | 100K+ total | Platform analytics |
| Repo forks (post-campaign) | 50+ in first 3 months | GitHub/GitLab |
| Games on platform (year 1) | 5+ community games | Internal |

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| **Underfunded** | $50K is lean -- my salary for 6 months. The platform already runs. Stretch goals are hires, not features. |
| **"Another NFT scam"** | Blockchain is a database, not a currency play. No mining, no gas fees, no token sale. Items have utility in multiple games. Marketing leads with gameplay, not crypto. |
| **"Too ambitious"** | 14,400 LOC game code + 29,000 LOC compiler already working. This is a partial alpha, not a whitepaper. Demo is playable today. |
| **"Open source = no revenue"** | Platform is open. Hosted services (matchmaking, marketplace, ranked play, seasonal content) are the product. Same model as Blender, Godot, Red Hat. |
| **Art quality** | 100% AI-generated with custom LoRAs. Style iteration is hours, not weeks. The model builder is the artist. |
| **Bus factor** | Code is always buildable. Items live on a public chain. Games are peers. If I disappear, the ecosystem continues. |
| **No games join the platform** | CodexMagic proves the platform works. The item translation API is simple. Game jam partnerships drive adoption. |

---

## Competitive Landscape

| Platform | What They Do | What We Do Differently |
|----------|-------------|----------------------|
| **Roblox** | User-generated game platform | Real item economy with provenance; items carry identity across games, not just cosmetic. Built on a real language, not Lua scripts. |
| **Steam** | Game distribution + Workshop | Games share an economy and item pool, not just a storefront. Community games are first-class citizens. |
| **Immutable X / Ronin** | NFT gaming chains | We're the games AND the chain. Not a chain looking for games to adopt it. |
| **Gods Unchained** | Blockchain TCG, single game | Multi-game platform. NFTs aren't just cards -- they translate across every game. |
| **MTG Arena / Hearthstone** | Walled-garden TCGs | Open platform. Your cards aren't trapped. AI-supervised play. Items outlive any single game. |
| **Epic / Unity** | Game engines | We're a platform with a live economy, not just a compiler. But yes, we also have our own language and compiler. |

---

## Image Assets

Generated concept art for the pitch deck lives in `docs/Marketing/images/`:

| File | Description | Slide Use |
|------|-------------|-----------|
| `07_card_warlord.png` | Warlord General card with P/T/D stats, mana cost, card frame | CodexMagic slide |
| `08_battlefield_cards.png` | Top-down card game in progress, cards on table | Gameplay slide |
| `09_loot_items.png` | Game item asset sheet -- weapons, gems, rarity colors | Items & Loot slide |
| `10_general_select.png` | Generals in portrait frames, character select layout | Gameplay slide |
| `11_combat_defense.png` | Creature cards in combat with game UI elements | Title slide |
| `12_pack_opening.png` | Booster pack opening, cards fanned on table | The Ask slide |

All game art is 100% AI-generated using custom Stable Diffusion LoRAs.
The art pipeline -- model selection, LoRA training, ComfyUI batch
workflows, prompt engineering -- is the art department. A trained LoRA
produces 200+ cards in a consistent style in days. Style iteration
costs GPU hours, not revision rounds.
