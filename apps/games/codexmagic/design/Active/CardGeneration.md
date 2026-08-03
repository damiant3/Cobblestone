# Card Generation -- AI Content Pipeline

## Overview

Every card in CodexMagic is AI-generated. There are no human-authored
card sets. The generation pipeline produces card mechanics, stat lines,
effect code, art, and flavor text. A QA/approval gate validates
generated content before it enters the live card pool.

Cards are generated in batches aligned to seasonal releases, but the
pipeline can also produce one-off promotional or event cards.

## Generation Pipeline

```
┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│  Mechanic    │───▶│   Card       │───▶│   Effect     │
│  Invention   │    │   Assembly   │    │   Codegen    │
└──────────────┘    └──────────────┘    └──────────────┘
                                              │
┌──────────────┐    ┌──────────────┐          │
│  Art          │───▶│   QA Gate   │◀─────────┘
│  Generation   │    │   (approve) │
└──────────────┘    └──────────────┘
                          │
                    ┌──────────────┐
                    │  Live Card   │
                    │  Pool        │
                    └──────────────┘
```

### Stage 1: Mechanic Invention

The AI designs new card mechanics within constraints set by the current
season's theme and the existing mechanic space. Inputs:

- Season theme and color identity targets
- Existing keyword ability catalog
- Current metagame balance data (win rates by archetype)
- Complexity budget (how many new keywords this season can absorb)

Outputs:

- New keyword abilities with rules text templates
- Triggered/activated ability patterns
- Interaction rules with existing mechanics

Mechanic invention is the most constrained stage. New mechanics must be
expressible in the rules engine's effect system and must not create
unbounded loops, infinite combos, or degenerate game states. The QA gate
catches what slips through, but the generator is tuned to avoid these.

### Stage 2: Card Assembly

Given a mechanic palette, the AI produces individual card templates:

```
GeneratedCard = record {
  name : Text,
  flavor-text : Text,
  card-type : CardType,
  subtypes : List Text,
  is-legendary : Boolean,
  mana-cost : ManaCost,
  power : Integer,
  toughness : Integer,
  defense : Integer,
  abilities : List Ability,
  rules-text : Text,
  color : ColorSet,
  rarity : Rarity,
  art-prompt : Text
}
```

**Rarity tiers:**

```
Rarity =
  | Common          -- pack filler, simple effects
  | Uncommon        -- moderate complexity, useful in constructed
  | Rare            -- powerful or unique, deck-defining
  | Mythic          -- splashy, format-warping potential
  | Legendary-Mythic -- unique token, one copy ever minted
```

Card assembly balances the set mathematically:
- Mana curve distribution per color
- Creature-to-spell ratio
- Removal density
- Keyword ability saturation
- Cross-color synergy density
- P/T/D distribution -- defense values are scarce at common rarity
  and more available at rare+. High defense (4+) is a premium stat
  that commands higher mana costs. Most creatures have defense 0.
- Deathtouch density must be sufficient to answer high-defense
  creatures without making defense worthless

### Stage 3: Effect Codegen

Each card's abilities are compiled into executable effect code that the
rules engine can run. This is the critical safety boundary -- generated
code must be:

1. **Deterministic** -- same inputs produce same outputs
2. **Pure** -- no side effects outside the game state monad
3. **Type-safe** -- validated against the rules engine's effect type system

Infinite loops are **allowed by design**. Card interactions can and
will produce cycles -- a triggered ability fires an effect that triggers
another ability that fires the first again. This is a feature, not a
bug. The rules engine handles this via cycle detection and clamping
(see [CycleDetection.md](CycleDetection.md)). Effect code does not
need to be provably terminating; the engine enforces termination at
the execution level.

Effect code is generated as Codex source, compiled, and linked into the
card's runtime representation. The effect type system constrains what
generated code can express:

```
Effect =
  | DealDamage (target : Target) (amount : Integer)
  | GainLife (player : Player) (amount : Integer)
  | DrawCards (player : Player) (count : Integer)
  | DestroyPermanent (target : Target)
  | AddCounter (target : Target) (counter-type : CounterType) (count : Integer)
  | CreateToken (template : CardTemplate) (count : Integer)
  | ModifyPTD (target : Target) (power-mod : Integer) (toughness-mod : Integer) (defense-mod : Integer)
  | ReturnToHand (target : Target)
  | Exile (target : Target)
  | Search (zone : Zone) (filter : CardFilter) (action : Effect)
  | Conditional (condition : GamePredicate) (then-effect : Effect) (else-effect : Effect)
  | Sequence (effects : List Effect)
  | ForEach (targets : TargetSet) (effect : Effect)
  | PlayerChoice (options : List Effect)
```

New effect primitives are added only via the seasonal mechanic invention
process, never by individual card generation.

### Stage 4: Art Generation

Art is generated per-card using the `art-prompt` from card assembly.
Art generation produces:

- **Card art** -- the main illustration (fixed aspect ratio)
- **Full art** -- extended illustration for premium/foil versions
- **Token art** -- for creature tokens the card creates

Art style is consistent within a season but varies between seasons.
Each season establishes a visual identity: color palette, rendering
style, thematic motifs.

Art quality contributes to a card's market value. Cards whose art is
well-received by the community (measured by trade volume, collection
rates, and explicit ratings) gain a **reception score** that feeds back
into the card's prominence.

### Stage 5: QA Gate

Before any generated card enters the live pool, it passes through
automated and human-in-the-loop validation:

**Automated checks:**
- Effect code compiles and type-checks
- Cycle-producing interactions are identified and annotated with
  expected clamp behavior (does the loop converge? what does clamping
  produce -- a board full of tokens? lethal damage? draw?)
- Stat line falls within rarity-appropriate power budget
- Mana cost is consistent with color identity
- Rules text parses and matches the compiled effect
- Art meets technical requirements (resolution, aspect ratio, no artifacts)

**Simulation checks:**
- 1000-game Monte Carlo simulation against the current card pool
- Win rate within acceptable bounds (no single card dominates)
- Game length distribution stays healthy (no degenerate stalls or instant kills)
- Interaction count with existing mechanics stays within complexity budget

**Human review (for Rare and above):**
- Mechanic review: does this create interesting decisions?
- Art review: quality, appropriateness, distinctiveness
- Name and flavor text review
- Final approval or rejection with feedback to the generator

Rejected cards are fed back to the generator with rejection reasons,
improving future output. The pipeline is a flywheel -- each season's
QA feedback tunes the generator for the next.

## Card Identity

Every card that passes QA receives a permanent identity:

```
CardIdentity = record {
  card-id : Hash,           -- content hash of the template
  edition : SeasonId,       -- which season produced it
  mint-number : Integer,    -- order within the edition
  rarity : Rarity,
  first-mint-time : Timestamp,
  total-supply : Integer    -- how many copies exist on-chain
}
```

Card identity is immutable once minted. The template (stats, abilities,
art) is frozen at mint time. Balance adjustments happen through bans,
format restrictions, or new counter-cards -- never by editing existing
cards. What you pull from a pack is what it is forever.

## Legendary-Mythic Cards

The rarest tier. A Legendary-Mythic card has a `total-supply` of 1.
It is unique in the entire game world. These are the crown jewels --
their value comes from absolute scarcity, guaranteed uniqueness of
art, and their tournament history. Pulling one from a pack is a
headline event.

## Clan Custom Packs

Clans use this same pipeline to produce themed packs. The clan
provides a `ClanPackDef` with mechanic filters (ban/allow keywords,
complexity caps), content theme (flavor tone, creature types,
setting), art direction (style, palette, content restrictions), and
profanity level (enforced by age bracket). The pipeline generates
cards matching the clan's specifications and routes them through the
same QA gate.

Clan packs are sold for Mana Coin (30% platform, 40-60% clan
treasury). Cards from clan packs are standard tokens -- mechanically
identical to global cards, just themed differently. A "Shepherd of
the Valley" from a church clan plays the same as an "Iron Sentinel"
from the global pool if they share the same stats and keywords.

See [Clans.md](Clans.md) for full custom pack definitions, content
theming, and age-appropriate content enforcement.
