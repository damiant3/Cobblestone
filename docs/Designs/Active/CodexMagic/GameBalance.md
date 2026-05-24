# Game Balance — Critical Analysis and Design Responses

## Purpose

This document audits the known problems with collectible card game
design (primarily Magic: The Gathering), catalogs the community
response to each, surveys how competing games address them, and
proposes CodexMagic's specific design responses. Every proposal is
evaluated against our core constraints: AI-supervised play, fast
pace, meaningful player decisions, and blockchain-auditable state.

## Part 1: The Problems

### 1.1 Mana Screw and Flood

**The problem:** Approximately 20% of MTG games are meaningfully
affected by drawing too many lands (flood) or too few (screw). The
mana system forces a deckbuilding tension — adding lands reduces
screw but increases flood — that no ratio fully resolves. Games
decided by resource variance feel like coin flips.

**Community defense:** The mana system creates meaningful deckbuilding
decisions (how many lands, what colors, which fixing) and produces
game-to-game variance that keeps matches fresh. Richard Garfield has
argued the resource system is what makes MTG strategically rich.

**What competitors do:**
- Hearthstone: one mana crystal per turn automatically, eliminating
  screw/flood entirely. Tradeoff: no deckbuilding tension around
  resources, all decks curve the same way.
- Force of Will: any card can be pitched as a mana source. Keeps
  resource decisions without dead draws.
- Flesh and Blood: pitch any card for resources. Resource quality
  varies by card, creating nuance.
- Pokemon TCG: Energy cards exist but can be searched freely.
- Legends of Runeterra: automatic mana gain plus spell-mana banking.

**Statistical reality:** In a 60-card deck with 24 lands, the
probability of drawing 0 lands in an opening 7 is ~2.5%. Drawing
1 or fewer is ~12%. After a mulligan to 6, the chance of a keepable
hand improves but at the cost of card advantage. Over a tournament
(15+ games), most players experience at least one game decided by
mana variance.

### 1.2 First-Player Advantage

**The problem:** The player going first wins approximately 53-55% of
the time in constructed formats, with the gap widening in aggressive
metas to 57%+. Going first means deploying a threat before the
opponent can answer it.

**Community defense:** MTG mitigates this by denying the first player
their initial draw step. The advantage is smaller than in many other
games and varies by format and archetype. Control decks sometimes
prefer drawing second for the extra card.

**What competitors do:**
- Hearthstone: second player gets "The Coin" (a free temporary mana).
- Pokemon TCG: first player cannot attack on turn one.
- Legends of Runeterra: alternates attack tokens each round, largely
  neutralizing first-turn advantage.
- Marvel Snap: simultaneous play eliminates the concept entirely.

### 1.3 Non-Games

**The problem:** Games where one player never meaningfully
participates — kept a marginal hand, drew poorly, or faced an
unanswerable opening. Community estimates: 10-15% of competitive MTG
games are effectively decided before either player makes a meaningful
choice.

**Community defense:** Variance is a feature. It allows weaker players
to occasionally beat stronger ones, keeping the game accessible and
tournaments exciting. Mark Rosewater has written extensively about
variance being essential to MTG's appeal.

**Counter-argument:** Professional players express frustration that at
the highest levels, too many matches hinge on draw quality rather
than decision-making. Frank Karsten's statistical analysis shows top
players maintain 60-65% win rates — well above chance, but well below
what a purely skill-based game would produce.

### 1.4 Game Length and Pace

**The problem:** Aggressive decks end games by turn 4-5, producing
too few decision points. Control mirrors stretch to 40+ minutes,
causing tournament time pressure and tedium. Power creep pushes the
game faster overall.

**The sweet spot:** Research on player engagement identifies
3-8 minutes with 15-30 meaningful decisions as optimal for digital
card games. Traditional CCGs (Hearthstone, MTG) average 2-4
meaningful decisions per minute across 10-20 minute games. Marvel
Snap compresses to 4-6 decisions per minute across 3-6 minutes.

**What competitors do:**
- Marvel Snap: fixed 6-turn structure with escalating energy. Every
  game ends in 3-6 minutes.
- Clash Royale: 3-minute real-time matches.
- Hearthstone: typical games 8-15 minutes.

### 1.5 Complexity Creep

**The problem:** Mark Rosewater has called complexity creep "the
biggest danger to the game." Average word count per card has steadily
increased. Modern sets feature 5+ new keywords. The barrier for new
players rises continuously.

**Community defense:** Wizards uses "New World Order" to keep commons
simple. Complexity at higher rarities serves enfranchised players.

**Design insight:** Digital games can hide complexity — the system
handles triggers and interactions automatically. The AI supervisor
model is uniquely positioned here: the AI resolves the complex
interactions, the player sees the outcome.

### 1.6 The Stack and Priority

**The problem:** The stack (LIFO resolution) and priority system are
powerful but intimidating. Casual players frequently misunderstand
interaction timing. Digital implementations (Arena, MTGO) handle it
imperfectly — auto-pass telegraphs information, manual control is
clunky.

**Community defense:** The stack is what makes MTG's interaction model
uniquely deep. No other card game has the same depth of responsive
play.

**Design insight:** The stack's depth can be preserved while hiding
its complexity. If the AI handles stack resolution and the player
only sees decision points ("do you want to respond?"), the strategic
depth is maintained without the cognitive overhead.

### 1.7 Color Pie Balance

**The problem:** Blue has historically been the strongest color
because counterspells and card draw are the most powerful generic
effects. Blue's access to "permission" (unconditionally denying
spells) is fundamentally asymmetric. Eternal formats are
blue-dominated.

**Community defense:** Wizards has deliberately weakened counterspells
in Standard. Creature quality has risen, benefiting green and white.
The color pie is better balanced now than ever.

**Design opportunity:** In a new game, the color pie can be designed
from scratch without 30 years of baggage. Defense as a stat creates
natural color differentiation.

### 1.8 Skill vs. Luck

**Statistical reality:** MTG is roughly 60% skill over a sufficient
sample size. The best players maintain 60-65% win rates over hundreds
of matches. The smaller the skill gap, the more luck dominates.

**Design goal:** Increase the skill component by making decisions
more impactful and reducing the number of games decided by draw
variance. The AI supervisor helps by ensuring the "execution" layer
(tapping mana, ordering triggers) is always optimal — the remaining
variance is strategic, not mechanical.

## Part 2: Design Lessons from Competitors

### 2.1 Marvel Snap's Breakthroughs

- **Fixed turn count** (6 turns) eliminates grind-out games.
- **Simultaneous play** eliminates downtime and first-player advantage.
- **The Snap mechanic** (doubling cube from backgammon) creates a
  meta-game of reading confidence and bluffing. Skilled players climb
  the ladder even with sub-50% win rates by snapping when ahead and
  retreating when behind.
- **Three locations** add spatial strategy to card play.
- **3-6 minute games** enable "one more game" on mobile.

### 2.2 Auto-Battler Patterns

- Separate the "thinking phase" from the "resolution phase."
- Let players feel clever during setup.
- Make resolution readable — players must understand *why* they won
  or lost.
- Continuous reinvention through meta rotation keeps engagement.

### 2.3 Commander Design

- A single identity card defines your deck and strategy.
- The identity card must remain accessible — taxing players out of
  their own identity is punishing (commander tax problem).
- Color identity restrictions feel natural when the identity card
  earns them.

### 2.4 Decision Density

- 3-5 meaningful decisions per minute is the engagement sweet spot.
- Under 10 total decisions per game feels like a coin flip.
- Over 50 total decisions creates decision fatigue.
- "Time value" (density of enjoyment per minute) matters more than
  raw clock time.

### 2.5 Input vs. Output Randomness

- **Input randomness** (what options you receive — drawing cards) is
  satisfying because the player has agency over what to do with them.
- **Output randomness** (whether your action succeeds — coin flips)
  creates "I did everything right and still lost" frustration.
- CodexMagic should maximize input randomness (card draw, pack
  cracking) and minimize output randomness (combat resolution is
  deterministic given board state).

### 2.6 Spectator Design

Card games are watchable when:
- The audience sees both hands and anticipates collisions.
- Expert commentary explains non-obvious lines.
- Clear dramatic peaks: lethal setups, bluff calls, comebacks.
- Short game length enables complete games in broadcasts.
- Variable stakes (snap/fold) create natural narrative arcs.

## Part 3: CodexMagic Design Responses

### 3.1 Resource Conversion (Mana Screw / Flood Fix)

**Problem addressed:** Mana screw and mana flood.

**Status: EXPLORE — requires simulation testing before adoption.**

Two complementary mechanics, one for each side of the problem:

#### 3.1a Screw Fix: Discard for Mana

Any card in hand can be discarded to produce 1 colorless mana. No
limit on how many cards per turn. The mana goes to your pool for
the current turn and empties at cleanup like normal.

**How it works:**
- During your main phase, discard any card from your hand.
- Add 1 colorless mana to your pool.
- The card goes to your graveyard. It is gone.
- You can do this as many times as you want per turn.
- Colorless mana can pay generic costs but not colored costs.

**Why unlimited is self-balancing:**

The cost is the card itself. Cards are worth far more than 1 mana.
A card is an entire action — a creature, a removal spell, a draw
engine. Trading a card for 1 colorless mana is a terrible rate that
nobody does voluntarily. You only do it because the alternative
(doing nothing with a hand full of uncastable spells) is worse.

The more cards you discard, the worse it gets exponentially:
- Discard 1: slight cost, reasonable emergency play.
- Discard 2-3: significant card disadvantage, aggressive tempo play.
- Discard 5: catastrophic. You are in topdeck mode permanently.

**The degenerate case tests clean:**

Opening hand, 0 lands. Discard 5 cards for 5 colorless mana. Play
your one remaining spell (if colorless-castable). You now have a
tempo burst (5-mana creature on turn 1) but 0 cards in hand. Your
opponent with a normal hand has 6 cards and will grind you out. The
mechanic punishes itself — the player who "goes all-in" must win
fast or die to card advantage.

**Interaction with Generals:**

The discard-for-mana decision maps to General posture. An Aggressive
general might discard 2-3 cards for an explosive early turn. A
Control general would never discard. This creates a real strategic
axis tied to the General's behavioral modifiers.

**Archetype implications:**

A hyper-aggro deck could be built to exploit this — run fewer lands,
plan to discard early for a huge opener, try to kill the opposing
General before card advantage matters. This is a valid glass-cannon
archetype. The counter is high-defense creatures (which bounce cheap
attackers), healing, and any deck that can survive the burst. The
defense stat naturally polices this strategy.

**Open question:** Should the mana be colorless only, or should
discarding a card with colored mana cost produce 1 mana of that
color? Colorless-only is simpler and means real lands are still
needed for color. Color-matching would make the mechanic stronger
but might reduce the importance of lands too much. **Test both in
simulation.**

#### 3.1b Flood Fix: Pitch Lands for Draw

Discard 2 land cards from your hand to draw 1 card. Once per turn.

**How it works:**
- During your main phase, if you have 2+ lands in hand, you may
  discard 2 of them to your graveyard.
- Draw 1 card from your library.
- This is a 2-for-1 trade — you are behind on raw card count.
- Once per turn to prevent cycling through the entire deck.

**Why 2-for-1:** You are converting dead draws (lands you can't use)
into live draws (anything else). The 2-for-1 rate ensures this is
a *mitigation*, not an advantage. A player who floods is still
behind — they just aren't locked out.

**Interaction with deckbuilding:** This slightly reduces the cost
of running more lands. A deck with 28 lands (normally a flood risk)
can convert excess lands into draws. But the conversion rate is bad
enough that you'd still prefer to draw spells naturally. **Test
different land counts (20, 24, 28) in simulation to verify this
doesn't make high-land-count decks dominant.**

**Interaction with graveyard mechanics:** Pitched lands go to the
graveyard, which could matter for future graveyard-matters cards.
This is intentional — it creates a secondary resource pathway.

#### 3.1c Combined Effect

Together, these two mechanics ensure:
- A hand with 0 lands is playable (discard spells for colorless mana)
- A hand with 5+ lands is playable (pitch 2 for a draw)
- Neither mechanic is *good* — both cost significant resources
- Players with normal draws (2-4 lands in 7) never use either
- The AI supervisor detects screw/flood states and offers the
  conversion, or auto-applies it based on posture

**Critical simulation questions:**
1. What is the optimal land count when both mechanics exist? Does it
   shift from ~24 to something else?
2. Does the discard-for-mana mechanic make aggro too strong? Run 1000
   games with aggro-heavy decks (12-16 lands) vs. standard (24 lands).
3. Does the flood fix make control too consistent? Run 1000 games
   with 28-land control decks vs. 24-land midrange.
4. At what discard rate does the AI supervisor use these? Track how
   often each mechanic fires per game across different deck types.
5. Does the interaction between the two create any degeneracy? (e.g.,
   pitch 2 lands for a draw, then discard the drawn card for mana)

### 3.2 Second-Player Compensation

**Problem addressed:** First-player advantage.

The second player's General starts with **+5 life** and **+1 starting
army loyalty** (a phantom 1-CMC creature worth of loyalty, not an
actual creature on board).

**Why this works:**
- Life and loyalty are existing resources, not new mechanics.
- The numbers are tunable per season based on win-rate data from
  on-chain match records.
- The AI factors the advantage into its strategy — a second-player
  General with 35 life plays more patiently.
- Combined with our defense stat, +5 life is significant — it may
  absorb an entire extra attack from a mid-size creature.

**Tuning knob:** If data shows 55% first-player win rate, increase
the compensation to +7/+2. If it drops below 50%, reduce to +3/+0.
Season balance patches adjust this number, not card text.

### 3.3 Free Early Concession

**Problem addressed:** Non-games, wasted time.

Conceding in the first 3 turns costs **no rating and no ManaCoin**.
The match is voided — it doesn't appear in either player's history.

**AI detection:** The supervisor detects "non-game" states and offers
a free restart prompt:
- No land or playable card by turn 2
- Opponent has lethal on board by turn 2
- Player's General has lost 50%+ life by turn 3 with no board
  presence

**Why this works:** It doesn't eliminate variance — it eliminates
wasted *time* from variance. Over a session, players play more real
games. The blockchain records only completed matches, so match
history reflects genuine contests.

### 3.4 Turn Cap and Sudden Death

**Problem addressed:** Game length, control-mirror stalls.

**12-turn cap.** After turn 12, the game enters **Sudden Death**:
- Both Generals take 3 damage at the start of each subsequent turn.
- The damage escalates by 1 per turn (3, 4, 5, 6...).
- This guarantees every game ends within ~15-16 turns maximum.

**Target game length:** With AI auto-play at 5-10 seconds per turn,
plus 2-3 intervention points per turn at ~5 seconds each, a 12-turn
game runs 5-8 minutes. Sudden Death adds 1-3 minutes if triggered.

**Decision density target:** 3-5 meaningful decisions per minute,
25-35 total decisions per game.

**Why 12 turns:** It allows:
- Aggro to deploy and attack (turns 1-6)
- Midrange to stabilize and turn the corner (turns 5-9)
- Control to reach its payoffs (turns 7-12)
- But not control to stall indefinitely

### 3.5 General Leveling

**Problem addressed:** Game pacing, dramatic arc, visible goals.

When army loyalty reaches thresholds, the General **unlocks additional
abilities** printed on the card:

| Loyalty Threshold | Unlock |
|-------------------|--------|
| 5 | First loyalty ability unlocked |
| 10 | Second loyalty ability unlocked |
| 20 | Ultimate ability unlocked |

**Example (Warlord of the Iron Banner):**
- Base: 4/5/1, Haste, StanceOverride Aggressive
- Loyalty 5: "All creatures you control get +1/+0"
- Loyalty 10: "Deal 3 damage to target creature"
- Loyalty 20: "All creatures you control gain First Strike and
  Trample until end of turn"

**Why this works:**
- Creates a natural dramatic arc: early development, mid-game
  activation, late-game finisher.
- Gives the player a visible goal (build your army to unlock the
  General's power).
- Makes army loyalty feel consequential every turn, not just for
  occasional activated abilities.
- The opponent can weaken your General by killing your creatures,
  creating meaningful interaction.

### 3.6 The War Cry Mechanic

**Problem addressed:** Ranked play engagement, spectator appeal.

Inspired by Marvel Snap's Snap mechanic (itself from backgammon's
doubling cube). At any point during a game, either player can
**War Cry** to double the rating stakes.

**How it works:**
- Default match stakes: +/- 20 rating points.
- Player A War Cries: stakes become +/- 40.
- Player B can Accept (continue at 40) or Retreat (concede at 20).
- If Player B accepts, either player can War Cry again to 80.
- Maximum: 3 War Cries per game (20 → 40 → 80 → 160).

**Strategic depth:**
- War Cry is a read on confidence. You signal "I think I'm winning."
- The opponent must evaluate: is this a bluff or does their board
  actually justify confidence?
- Retreat is not a full loss — you lose at the current stakes, which
  is less than you'd lose if you played it out and lost at doubled
  stakes.
- The AI supervisor can auto-War-Cry when it detects a dominant
  board state, or the player can do it manually.

**Why this works for CodexMagic:**
- Fast games (5-8 min) mean War Cry decisions come quickly and
  frequently — one per game on average.
- Retreat-and-requeue is painless in short games.
- Spectators love the drama: "Will they snap back or fold?"
- Rating climbers can gain ELO efficiently by War Crying when ahead
  and retreating when behind, rewarding game-reading skill.

### 3.7 Hidden Complexity, Visible Decisions

**Problem addressed:** Complexity creep, stack confusion.

**Design rule:** The player never interacts with the stack directly.
The AI resolves all stack interactions. The player sees decision
points:

- "Opponent cast Lightning Strike targeting your General. Respond?"
  → Options: [Counter with your Counterspell] [Let it resolve]
- "Three triggers fire simultaneously. Order?"
  → Options shown with outcomes, AI recommends optimal ordering

The stack, priority, APNAP ordering — all of it runs in the engine.
The player experience is: "Here's what's happening. Do you want to
intervene?" This preserves the strategic depth of instant-speed
interaction without requiring the player to understand LIFO
resolution or priority windows.

### 3.8 Color Pie with Defense

**Problem addressed:** Color balance, blue dominance.

Defense as a stat creates natural color differentiation:

| Color | Identity | Defense Profile |
|-------|----------|----------------|
| White | Protection, order | Mid defense (2-3), vigilance, lifelink |
| Blue | Knowledge, control | Low defense (0-1), hexproof, card draw |
| Black | Power, sacrifice | Zero defense, deathtouch (pierces defense), life drain |
| Red | Aggression, speed | Zero defense, high power, haste, first strike |
| Green | Growth, strength | Highest defense (3-5), trample, large bodies |

**Key balance:** Black's deathtouch is the universal answer to
defense. A 1/1 deathtouch kills any creature regardless of defense.
This keeps green's high-defense creatures honest and gives black a
clear role in the metagame. Red's first strike + high power can
sometimes pierce defense through raw damage. White's mid-defense
creatures are resilient but not unkillable.

**Blue without dominant counterspells:** In CodexMagic, counterspells
exist but are expensive (2-3 blue mana) and limited. Blue's identity
is card advantage (draw) and evasion (flying, hexproof), not
permission. The AI handles counter-play decisions, so the "counter
or not?" decision is presented as a choice, not a timing puzzle.

## Part 4: What We Keep from MTG

1. **The stack** — but only for the AI. Players see decision points.
2. **Color pie** — with defense-based differentiation.
3. **Mana as a resource** — lands in deck, tap for colored mana.
4. **Deckbuilding** — 60-card constructed with 4-copy limit.
5. **Draft and Sealed** — limited formats with 40-card minimums.
6. **Combat math** — attacking, blocking, damage assignment.
7. **Triggered abilities** — ETB, death, attack, combat damage.

## Part 5: What We Change

1. **Resource conversion** — discard any card for 1 colorless mana
   (screw fix); pitch 2 lands for 1 draw (flood fix). [EXPLORE]
2. **First-player compensation** — +5 life, +1 loyalty for second.
3. **Turn cap** — 12 turns, then Sudden Death.
4. **Free early concession** — first 3 turns cost nothing.
5. **General leveling** — loyalty thresholds unlock abilities.
6. **War Cry** — doubling cube for ranked stakes.
7. **No direct stack interaction** — AI resolves, player decides.
8. **Defense stat** — per-source damage threshold.
9. **Army loyalty** — CMC-based, tokens excluded.

Items marked [EXPLORE] require simulation testing before adoption.

## Part 6: Open Questions

1. **Simultaneous play?** Marvel Snap's simultaneous model eliminates
   first-player advantage entirely. Should CodexMagic adopt
   simultaneous turns? This conflicts with the stack and instant-speed
   interaction — but since the AI handles the stack, maybe the
   "turns" are really just planning phases followed by simultaneous
   resolution?

2. **Location-based play?** Marvel Snap's three-location model adds
   spatial strategy. Should CodexMagic have board zones that matter?

3. **Draft vs. Constructed balance?** Limited formats produce the
   healthiest game lengths in MTG. Should CodexMagic's ranked ladder
   default to draft rather than constructed?

4. **Optimal General count?** How many Generals should exist per
   season? Too few and the meta is stale. Too many and none feel
   special. Proposal: 10-15 per season, with 5 in the starter set.

5. **Should War Cry be AI-assisted?** The AI could recommend when to
   War Cry and when to retreat. But this removes a key player-skill
   axis. Proposal: the AI can suggest, but War Cry/Retreat is always
   a manual decision.

## Part 7: Simulation Testing Framework

All mechanics marked [EXPLORE] must be validated through automated
simulation before they are adopted into the game. Simulation uses
the existing AI Supervisor to play both sides of thousands of games,
with metrics collected per game and aggregated for analysis.

### 7.1 Test Methodology

Each mechanic is tested as a toggle: run N games with the mechanic
enabled, N games without it, compare metrics. N should be at least
1000 per configuration to get statistically meaningful results.

The simulation harness:
1. Constructs two decks (either generated or preset archetypes).
2. Assigns Generals with appropriate behavioral modifiers.
3. Runs the game to completion with the AI supervisor on both sides.
4. Records per-game metrics (see below).
5. Aggregates across all N games for analysis.

### 7.2 Metrics Per Game

| Metric | What it measures |
|--------|-----------------|
| Winner (0 or 1) | Who won |
| Turns played | Game length |
| First-player win | Did the first player win? |
| Mana-screw events | Turns where a player had 0 playable cards |
| Mana-flood events | Turns where a player had 3+ lands in hand |
| Discards-for-mana | How many times the screw fix was used |
| Lands-pitched | How many times the flood fix was used |
| Cards in hand at game end | Card advantage proxy |
| General life at game end | How close the game was |
| Army loyalty at game end | Board development proxy |
| Total creatures played | Tempo proxy |
| Total spells cast | Activity proxy |
| Decisions made | Player intervention count |
| War Cries issued | Snap/retreat frequency |

### 7.3 Test Configurations

**Resource conversion tests:**
- Config A: No conversion mechanics (baseline MTG-style).
- Config B: Discard-for-colorless-mana only, unlimited.
- Config C: Pitch-2-lands-for-draw only, once per turn.
- Config D: Both mechanics enabled.
- Config E: Discard-for-colored-mana (matching card color).
- Vary land counts: 16, 20, 24, 28 lands per deck.
- Archetypes: aggro (low curve, few lands), midrange (standard),
  control (high curve, many lands).

**First-player compensation tests:**
- Config A: No compensation (baseline).
- Config B: +5 life to second player.
- Config C: +5 life and +1 starting loyalty.
- Config D: +3 life only.
- Config E: +7 life only.
- Measure first-player win rate across 1000+ games each.

**Turn cap tests:**
- Config A: No turn cap (games run to natural conclusion).
- Config B: 12-turn cap with Sudden Death (3 + 1/turn).
- Config C: 10-turn cap with steeper Sudden Death (5 + 2/turn).
- Config D: 15-turn cap with mild Sudden Death (2 + 1/turn).
- Measure: average game length, percentage of games reaching
  Sudden Death, archetype win rates under each cap.

**General leveling tests:**
- Config A: No leveling (static abilities as currently implemented).
- Config B: 5/10/20 thresholds with increasing ability power.
- Measure: average loyalty at game end, how often each threshold
  is reached, impact on army-building strategy.

### 7.4 Analysis Targets

A mechanic is considered **healthy** if:
- First-player win rate is between 48% and 52%.
- Mana-screw non-games are under 5% (down from ~15%).
- Average game length is 8-12 turns (5-8 minutes).
- No single archetype wins more than 55% against the field.
- The mechanic fires in 10-30% of games (not never, not always).

A mechanic is **degenerate** if:
- One archetype wins 60%+ against the field.
- The mechanic fires in 80%+ of games (it's not an escape valve,
  it's the default strategy).
- Average cards-in-hand-at-game-end drops below 1 (everyone is
  discarding everything — the mechanic is too strong).
- Game length drops below 5 turns consistently (the mechanic
  enables too-fast kills).

### 7.5 Implementation

The simulation harness is built as a Codex module (`Simulate.codex`)
that imports the Engine, Supervisor, and relevant mechanic modules.
It runs headless (no Console output during games, only aggregate
stats at the end). The module uses the existing RNG infrastructure
for repeatable tests (same seed = same game).

```
codex.magic/
  Simulate.codex    -- simulation harness, metric collection
  SimConfig.codex   -- test configuration records
  SimReport.codex   -- aggregation, analysis, reporting
```

Each test run produces a report:
```
=== Resource Conversion Test (Config D, N=1000) ===
First-player win rate: 51.2%
Avg turns: 9.4
Mana-screw events/game: 0.3 (baseline: 1.8)
Mana-flood events/game: 0.5 (baseline: 2.1)
Discard-for-mana fires: 14% of games
Lands-pitched fires: 11% of games
Aggro win rate: 48%, Midrange: 52%, Control: 50%
Cards in hand at end: 2.1 (baseline: 1.8)
Verdict: HEALTHY
```
