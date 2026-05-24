# Seasons — Content Schedule, Mechanic Invention, Approval

## Overview

CodexMagic content is released on a seasonal schedule. Each season
introduces new mechanics, a new card pool, a visual theme, and a
competitive cycle. Seasons are the heartbeat of the game — they keep
the metagame fresh, drive pack sales, and give players a reason to
return.

AI generates all seasonal content. A structured approval process
ensures quality and balance before anything hits the live environment.

## Season Structure

A season runs for approximately 12 weeks:

```
Season = record {
  season-id : SeasonId,
  name : Text,               -- "Age of Embers," "Tide of Shadows," etc.
  theme : Text,              -- creative and mechanical theme
  start-date : Timestamp,
  end-date : Timestamp,
  card-pool-size : Integer,  -- typically 200-300 cards
  new-keywords : List Keyword,
  mechanics-document : Hash, -- link to the season's mechanic spec
  visual-theme : ArtDirection
}
```

### Season Timeline (12 weeks)

| Week | Phase | Activity |
|------|-------|----------|
| -4 to -2 | **Invention** | AI generates new mechanics, playtests internally |
| -2 to 0 | **Assembly** | AI generates full card pool, art, effect code |
| 0 | **QA Gate** | Automated + human review of all content |
| 0 | **Launch** | New packs available, new cards enter circulation |
| 1-2 | **Discovery** | Players explore new cards, meta is undefined |
| 3-6 | **Development** | Meta stabilizes, archetypes emerge |
| 6 | **Mid-season patch** | Balance adjustments if needed (bans, not edits) |
| 7-10 | **Tournament** | Ranked season, qualifiers, championship |
| 11 | **Championship** | Season-ending tournament, prizes |
| 12 | **Transition** | Rank reset, next season preview |

Seasons overlap: the next season's invention phase begins while the
current season is in tournament phase. There is always a season in
play and a season in production.

## Mechanic Invention

Each season introduces 2-4 new keyword abilities and 1-2 new mechanic
themes. Mechanic invention is the most creative and most constrained
part of the pipeline.

### Invention Constraints

New mechanics must satisfy all of:

1. **Expressible** — implementable in the rules engine's effect type
   system without new primitives (unless a new primitive is approved
   as a platform upgrade)
2. **Bounded** — no unbounded loops, no infinite combos without
   specific counterplay
3. **Interactive** — creates meaningful decisions for both players,
   not just the controller
4. **Composable** — plays well with existing mechanics without creating
   degenerate interactions
5. **Learnable** — a new player can understand the mechanic from one
   game of seeing it in action

### Invention Process

1. **Theme selection** — the creative AI proposes 3-5 season themes
   based on what hasn't been explored recently, color pie gaps, and
   player engagement data. Human selects one.

2. **Mechanic generation** — the AI proposes 8-12 new keyword abilities
   and mechanic patterns within the chosen theme. Each proposal includes:
   - Rules text template
   - 3 example cards using the mechanic
   - Interaction analysis with the top 20 current-meta cards
   - Complexity rating (1-5 scale)

3. **Mechanic playtest** — the AI simulates 10,000 games using the
   proposed mechanics against the existing card pool. Metrics:
   - Game length distribution
   - Win rate by archetype
   - Decision density (how often the mechanic creates a choice)
   - Feel score (does the mechanic lead to interesting board states?)

4. **Mechanic selection** — from the 8-12 proposals, 2-4 are selected
   for the season based on playtest results and thematic fit. Human
   approval required.

5. **Mechanic finalization** — selected mechanics get their effect
   code templates locked and documented. These become the palette
   for card assembly.

### Example Mechanic Proposals

**Season: "Age of Embers"**

- **Smolder** (keyword) — "At the beginning of your end step, if this
  creature dealt damage this turn, it deals 1 damage to each opponent."
  *Theme: sustained pressure, aggressive decks.*

- **Forge** (activated ability pattern) — "Exile a card from your hand:
  Put a +1/+1 counter on this creature." *Theme: converting cards to
  power, hand-size tradeoffs.*

- **Eruption** (triggered ability pattern) — "When the third land
  enters the battlefield this turn, [effect]." *Theme: land-drop
  counting, ramp payoffs.*

## Balance Philosophy

Cards are never edited after minting. Balance is maintained through:

1. **Bans** — a card is banned from a format (cannot be played, but
   retains trade value and can be played in Vintage)
2. **Restrictions** — a card is restricted to 1 copy per deck instead
   of the usual 4
3. **Counter-printing** — next season's card pool includes specific
   answers to dominant strategies
4. **Format rotation** — Standard format rotates out older seasons,
   naturally pruning the meta

Ban decisions are posted on-chain with justification. The community
can see the game data that motivated the ban.

## Season Rewards

Each season offers rewards for participation:

- **Daily rewards** — small Mana Coin for first win, first 3 games
- **Weekly rewards** — bonus pack for 7 wins in a week
- **Season pass** — tiered rewards for cumulative play (free track
  and premium track)
- **Rank rewards** — Mana Coin and exclusive card variants at season
  end based on final rank
- **Tournament prizes** — Mana Coin pool, exclusive cards, Signature
  variants

Season-exclusive card variants (alternate art, borders) are only
obtainable during that season. They become tradeable after the season
ends, driving collector demand.

## Content Versioning

All seasonal content is versioned and immutable once released:

```
SeasonManifest = record {
  season-id : SeasonId,
  card-pool : List CardIdentity,
  mechanics : List MechanicSpec,
  art-direction : ArtDirection,
  balance-notes : List BalanceAction,
  manifest-hash : Hash
}
```

The manifest hash is posted on-chain at season launch. Anyone can
verify that the card pool they're playing with matches the committed
manifest. No stealth nerfs, no hidden changes. What shipped is what
shipped.

## Year Structure

Four seasons per year, each with a distinct theme. The year culminates
in a World Championship combining all four seasonal metas:

| Quarter | Season | Example Theme |
|---------|--------|---------------|
| Q1 | Spring | Growth, nature, ramp mechanics |
| Q2 | Summer | Aggression, fire, combat mechanics |
| Q3 | Fall | Decay, death, graveyard mechanics |
| Q4 | Winter | Control, ice, counter/lockdown mechanics |
| Year-end | World Championship | All-season Vintage format |

Thematic alignment is a guideline, not a constraint. The AI may propose
themes that subvert expectations — a spring season about predatory
growth, a winter season about volcanic activity under ice.
