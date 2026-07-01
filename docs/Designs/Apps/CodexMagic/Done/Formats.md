# Formats — Deck Construction Rules and Play Modes

## Design Principles

Formats are data-driven configurations, not hardcoded modes. Each
format is a record of parameters: deck size, copy limits, card pool
legality, General rules, and special constraints. New formats are
created by changing parameters, not by writing new code.

Format names are descriptive, not branded. No "Standard" (standard
compared to what?), no "Modern" (modern when?). Names say what the
format IS.

## Format Record

```
FormatDef = record {
  name : Text,
  deck-min : Integer,
  deck-max : Integer,
  copy-limit : CopyLimit,
  pool-rule : PoolRule,
  general-rule : GeneralRule,
  land-rule : LandRule,
  sideboard : SideboardRule,
  match-rules : RuleSet
}

CopyLimit =
  | Unlimited             -- any number of copies
  | MaxCopies (Integer)   -- up to N of each non-basic
  | Singleton             -- exactly 1 of each non-basic

PoolRule =
  | CurrentSeason          -- current season only
  | RecentSeasons (Integer) -- last N seasons
  | AllSeasons             -- every card ever printed
  | CustomPool (List Integer) -- specific card IDs

GeneralRule =
  | GeneralRequired        -- must have a General, color identity enforced
  | GeneralOptional        -- General optional, no color restriction
  | NoGeneral              -- no Generals allowed

LandRule =
  | BasicUnlimited         -- any number of basic lands
  | LandsCount (Integer)   -- exactly N lands required
  | LandsFree              -- lands count toward deck min but no constraint

SideboardRule =
  | NoSideboard
  | Sideboard (Integer)    -- N-card sideboard, swap between games
```

## Constructed Formats

### Modern

The flagship format. Current season + previous season. 4-of limit.
This is what "ranked ladder" defaults to.

```
format-modern = FormatDef {
  name = "Modern",
  deck-min = 60, deck-max = 60,
  copy-limit = MaxCopies 4,
  pool-rule = RecentSeasons 2,
  general-rule = GeneralRequired,
  land-rule = BasicUnlimited,
  sideboard = Sideboard 15,
  match-rules = rules-codexmagic-proposed
}
```

- **What rotates:** When a new season launches, the oldest of the
  two legal seasons rotates out. Cards from that season are no longer
  Modern-legal but remain playable in Legacy formats.
- **Bans:** Per-season ban list. Banned cards cannot be played but
  retain trade value. Bans are posted on-chain with justification.

### Legacy

All cards ever printed. Larger pool means more powerful decks and
more complex interactions. The "eternal" format.

```
format-legacy = FormatDef {
  name = "Legacy",
  deck-min = 60, deck-max = 60,
  copy-limit = MaxCopies 4,
  pool-rule = AllSeasons,
  general-rule = GeneralRequired,
  land-rule = BasicUnlimited,
  sideboard = Sideboard 15,
  match-rules = rules-codexmagic-proposed
}
```

- **Restrictions:** Some cards restricted to 1 copy instead of 4.
  Used for cards that are too strong at 4 copies but add interesting
  variance at 1.
- **No rotation.** Once a card is Legacy-legal, it stays legal
  forever (unless banned).

### Singleton

100-card decks, exactly 1 copy of each non-basic card. The
Commander-style format, emphasizing variety and General identity.

```
format-singleton = FormatDef {
  name = "Singleton",
  deck-min = 100, deck-max = 100,
  copy-limit = Singleton,
  pool-rule = AllSeasons,
  general-rule = GeneralRequired,
  land-rule = BasicUnlimited,
  sideboard = NoSideboard,
  match-rules = rules-singleton
}
```

- **General is central.** Color identity strictly enforced — every
  card in the deck must match the General's color identity.
- **Starting life: 30** (higher than Modern's 20 to accommodate
  longer games).
- **No sideboard.** The 100-card singleton constraint IS the
  deckbuilding challenge.
- **Multiplayer-ready.** Singleton is designed for 2-4 players.
  The format rules scale: 3+ player games use APNAP ordering, free-
  for-all attack targeting, and shared turn structure.

### Blitz

Small, fast, aggressive. 30-card decks, 2-of limit, tight turn cap.
The "quick game" format for mobile sessions.

```
format-blitz = FormatDef {
  name = "Blitz",
  deck-min = 30, deck-max = 30,
  copy-limit = MaxCopies 2,
  pool-rule = CurrentSeason,
  general-rule = GeneralRequired,
  land-rule = BasicUnlimited,
  sideboard = NoSideboard,
  match-rules = rules-blitz
}
```

- **Turn cap: 8.** Sudden death at turn 9 (5 + 2/turn). Games
  target 3-5 minutes.
- **Starting life: 15.** Lower life = faster kills.
- **General life: 20.** (reduced from 30).
- **2-of limit** means less consistency, more variance, more
  interesting draws.
- **Current season only.** Keeps the pool small and the meta fresh.

### Unlimited

No copy limits, no restrictions. The "anything goes" format for
testing, theory-crafting, and degenerate combos.

```
format-unlimited = FormatDef {
  name = "Unlimited",
  deck-min = 40, deck-max = 40,
  copy-limit = Unlimited,
  pool-rule = AllSeasons,
  general-rule = GeneralOptional,
  land-rule = LandsFree,
  sideboard = NoSideboard,
  match-rules = rules-dry
}
```

- **This is the test format.** Used for simulation, balance testing,
  and "what if" experiments.
- **No General required.** Decks can be mono-creature, mono-spell,
  or any mix.
- **No fixes, no cap.** Raw game mechanics only.
- **40 cards.** Smaller deck = faster iteration.

## Limited Formats

Limited formats use cards opened from packs, not pre-owned
collections. They equalize collection depth — a new player and a
veteran have the same card access within a limited event.

### Sealed: Base + Booster

The primary limited format. Every player receives:
1. A **base deck** (30 cards: 15 lands + 15 creatures/spells, color-
   balanced, all Common). The base deck is identical for every player
   in the event.
2. One **booster pack** (15 cards from the current season pool,
   standard rarity distribution).

The player builds a 40-card deck from their combined 45-card pool.
The base deck guarantees a functional starting point; the booster
provides customization.

```
format-sealed-base = FormatDef {
  name = "Sealed-Base",
  deck-min = 40, deck-max = 40,
  copy-limit = Unlimited,
  pool-rule = CurrentSeason,
  general-rule = GeneralRequired,
  land-rule = BasicUnlimited,
  sideboard = NoSideboard,
  match-rules = rules-sealed
}
```

**Why base + booster instead of 6 boosters:**
- **Lower skill floor.** Building a 40-card deck from 90 random
  cards requires deep format knowledge. Building from 30 known + 15
  random is approachable — you already have a deck, you're just
  upgrading it.
- **Faster setup.** No 30-minute deckbuilding phase. Crack the
  booster, swap in the good cards, play.
- **Lower cost.** 1 booster instead of 6. The base deck is provided
  free (it's a common card set, not a purchase).
- **Still has variance.** The booster gives you 15 cards, including
  rares and uncommons that can define your strategy. Two players with
  the same base deck but different boosters will play very different
  games.
- **Skill expression is in the swap decisions.** Which of your 15
  booster cards replace which base cards? This is a meaningful
  decision with clear tradeoffs, not an overwhelming sorting task.

**The base deck per color:**

Each General color has a matching base deck:

| Color | Lands | Creatures | Spells |
|-------|-------|-----------|--------|
| White | 15 Plains | 8 (mix of 1/1, 1/2, 2/2 vanilla) | 4 (heal, buff) + 3 land |
| Blue | 15 Islands | 8 (mix with flying) | 4 (draw, bounce) + 3 land |
| Black | 15 Swamps | 8 (mix with deathtouch) | 4 (removal, drain) + 3 land |
| Red | 15 Mountains | 8 (mix with haste) | 4 (damage, burn) + 3 land |
| Green | 15 Forests | 8 (mix with trample) | 4 (growth, ramp) + 3 land |

The base deck establishes the color identity. The booster can
contain any color — the player decides whether to splash or stay
mono.

### Sealed: Classic

Traditional sealed for experienced players. 6 booster packs, build
a 40-card deck from 90 cards. No base deck.

```
format-sealed-classic = FormatDef {
  name = "Sealed-Classic",
  deck-min = 40, deck-max = 40,
  copy-limit = Unlimited,
  pool-rule = CurrentSeason,
  general-rule = GeneralRequired,
  land-rule = BasicUnlimited,
  sideboard = NoSideboard,
  match-rules = rules-sealed
}
```

### Draft

8 players, 3 packs each, pick-pass drafting. Build a 40-card deck
from your drafted pool.

```
format-draft = FormatDef {
  name = "Draft",
  deck-min = 40, deck-max = 40,
  copy-limit = Unlimited,
  pool-rule = CurrentSeason,
  general-rule = GeneralRequired,
  land-rule = BasicUnlimited,
  sideboard = NoSideboard,
  match-rules = rules-sealed
}
```

Draft is the highest-skill limited format. Skill expression comes
from reading signals (what colors are open), evaluating cards in
context (a card's value depends on what you've already drafted), and
hate-drafting (taking a card to deny it to opponents).

**AI-assisted drafting:** The AI supervisor can recommend picks based
on the player's drafted pool, color commitments, and mana curve. The
player can accept recommendations or override. This makes draft
accessible to new players while preserving depth for experts.

### Chaos Sealed

Random format. Each player opens packs from random seasons — your
pool might span 3 different seasons with different mechanics. Build
a 40-card deck from the chaos.

```
format-chaos-sealed = FormatDef {
  name = "Chaos-Sealed",
  deck-min = 40, deck-max = 40,
  copy-limit = Unlimited,
  pool-rule = AllSeasons,
  general-rule = GeneralOptional,
  land-rule = BasicUnlimited,
  sideboard = NoSideboard,
  match-rules = rules-sealed
}
```

This is the "fun" format — unpredictable, chaotic, great for casual
events. Cross-season synergies emerge that never existed in any
single season's design.

## Tournament Structures

### Swiss

Round-robin-style with pairing by record. N rounds for 2^N players.
No elimination — every player plays every round.

- 8 players: 3 rounds
- 16 players: 4 rounds
- 32 players: 5 rounds

Final standings by record (W-L), tiebroken by opponent win
percentage.

### Single Elimination

Win or go home. Standard bracket with seedings. Best-of-3 matches.

### Double Elimination

Winners and losers brackets. Must lose twice to be eliminated.
Grand final: winners bracket champion vs losers bracket champion.
If the losers bracket winner wins the first grand final set, a
reset match is played.

Best-of-3 matches throughout. Grand final is best-of-3 with
optional reset.

### League

Ongoing over a season. Players play at their own pace. Record
accumulates over the league period. Minimum games required to
qualify for prizes.

- **Casual league:** Play 10+ games per week, earn seasonal rewards.
- **Competitive league:** Play 20+ games, top 16 qualify for end-of-
  season championship.
- **Draft league:** Pay entry fee, receive packs, build a sealed
  deck, play until 7 wins or 3 losses.

## Rotation Schedule

| Event | Timing | What happens |
|-------|--------|-------------|
| Season launch | Every 12 weeks | New card pool enters Modern |
| Rotation | Season launch | Oldest season leaves Modern, enters Legacy |
| Ban review | Mid-season (week 6) | Balance adjustments for Modern |
| Base deck update | Season launch | Sealed-Base decks updated for new season |
| Draft format update | Season launch | Draft packs use new season |

## Format-Specific Rules

### rules-sealed

```
rules-sealed = RuleSet {
  mulligan-style = MulliganLondon,
  screw-fix = ScrewFixDiscard,
  flood-fix = FloodFixPitch2Draw1,
  fairness = FairnessLifeBonus 3,
  turn-cap = 15,
  sudden-death = True,
  sudden-death-base = 2,
  sudden-death-escalation = 1,
  starting-hand-size = 7,
  starting-life = 20
}
```

Sealed gets a more generous turn cap (15 vs 12) because limited
pools are less consistent and games naturally take longer.

### rules-blitz

```
rules-blitz = RuleSet {
  mulligan-style = MulliganLondon,
  screw-fix = ScrewFixDiscard,
  flood-fix = FloodFixPitch2Draw1,
  fairness = FairnessLifeBonus 2,
  turn-cap = 8,
  sudden-death = True,
  sudden-death-base = 5,
  sudden-death-escalation = 2,
  starting-hand-size = 7,
  starting-life = 15
}
```

Blitz has aggressive sudden death (5 + 2/turn starting at turn 9)
to guarantee games end within 3-5 minutes.

### rules-singleton

```
rules-singleton = RuleSet {
  mulligan-style = MulliganLondon,
  screw-fix = ScrewFixDiscard,
  flood-fix = FloodFixPitch2Draw1,
  fairness = FairnessLifeBonus 3,
  turn-cap = 20,
  sudden-death = True,
  sudden-death-base = 3,
  sudden-death-escalation = 1,
  starting-hand-size = 7,
  starting-life = 30
}
```

Singleton has higher life (30) and a longer turn cap (20) because
100-card singleton decks are less consistent and games develop more
slowly.

## Simulation Testing per Format

Each format should be tested independently in the simulation
harness. Key metrics per format:

1. **Average game length** — must match format intent (Blitz: 4-6T,
   Modern: 10-14T, Singleton: 14-20T, Sealed: 12-16T)
2. **First-player win rate** — must be 45-55% with compensation
3. **Archetype diversity** — no single archetype > 55% win rate
4. **Mana screw rate** — must be under 5% with fixes
5. **Sudden death rate** — should be under 20% (games should end
   naturally most of the time)

## Open Questions

1. **Should Blitz have Generals?** At 30 cards and 8-turn cap, the
   General's abilities may not have time to matter. Consider a
   General-less Blitz variant.

2. **Draft pod size?** Classic MTG uses 8 players. With AI
   supervision, could we support 4-player pods for faster drafts?

3. **Sealed-Base booster size?** 15 cards is standard, but 10
   might be sufficient if the base deck is strong enough. Smaller
   boosters = cheaper entry = lower barrier.

4. **Cross-format ranked?** Should there be one unified rating or
   separate ratings per format? Proposal: separate ratings, with a
   "combined" rating for season rewards.

5. **Draft AI recommendations?** How aggressive should the AI be
   in draft? Options: suggest top 3 picks (player chooses), or
   auto-pick with player override on any pick.

## Cross-References

- **Turn cap rationale:** [GameBalance.md](GameBalance.md) section
  3.4 explains why 12 turns is the Modern default and how defender/
  deathtouch stalemates (21-36 turns) prove the cap is necessary.
- **First-player compensation:** [GameBalance.md](GameBalance.md)
  section 8.1 shows the life+2 to +3 sweet spot with fixes enabled.
  Per-format life bonuses are tuned from this data.
- **Clan custom formats:** [Clans.md](Clans.md) describes house
  rules that overlay format definitions. Clans can create any format
  by combining a FormatDef with house rules. The global formats
  listed here are the "default clan's" formats.
- **Keyword balance:** The format's card pool affects game balance.
  See [GameBalance.md](GameBalance.md) section 8.2 for how keyword
  diversity affects first-player advantage.
