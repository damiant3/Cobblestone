# Mana — Pool, Costs, and Color

## The Five Colors

```
ManaColor =
  | White
  | Blue
  | Black
  | Red
  | Green
  | Colorless
```

## Mana Pool

Each player has a mana pool that accumulates mana and empties at phase
boundaries:

```
ManaPool = record {
  white : Integer between 0 and 99,
  blue : Integer between 0 and 99,
  black : Integer between 0 and 99,
  red : Integer between 0 and 99,
  green : Integer between 0 and 99,
  colorless : Integer between 0 and 99
}
```

Mana pools empty at the end of each step/phase (mana burn was removed
in the rules era we're targeting, so no life loss — it just vanishes).

## Mana Cost

```
ManaCost = record {
  white : Integer between 0 and 15,
  blue : Integer between 0 and 15,
  black : Integer between 0 and 15,
  red : Integer between 0 and 15,
  green : Integer between 0 and 15,
  generic : Integer between 0 and 15
}
```

**Generic mana** can be paid with any color. Colored requirements must
be paid with that specific color.

## Paying Costs

```
can-pay : ManaPool, ManaCost -> Boolean
pay-cost : ManaPool, ManaCost -> ManaPool
```

Payment algorithm:
1. Subtract required colored mana (if pool has insufficient, can't pay)
2. Subtract generic from remaining pool (player chooses which colors; for
   the engine, we use a deterministic priority: colorless first, then
   the color with the highest surplus)

## Converted Mana Cost (CMC)

```
cmc : ManaCost -> Integer
cmc (cost) = cost.white + cost.blue + cost.black + cost.red + cost.green + cost.generic
```

## Lands and Mana Production

Basic lands have an intrinsic mana ability:
- Plains → {W}
- Island → {U}
- Swamp → {B}
- Mountain → {R}
- Forest → {G}

Tapping a land for mana is a mana ability (doesn't use the stack).
Non-basic lands may produce multiple colors or have other abilities.

## Color Identity (for card definitions)

A card's color identity includes:
- Colors in its mana cost
- Any color indicators
- Colors of mana symbols in rules text (not relevant for our subset)

For the engine, color identity is precomputed on the CardTemplate as
a `ColorSet` record.

## Resource Conversion (Optional Mechanics)

Two optional mechanics address mana screw and flood. These are
configurable per format via the RuleSet and are marked [EXPLORE]
pending simulation validation.

- **Screw fix:** Discard any card for 1 colorless mana. Unlimited
  per turn. Self-balancing because cards are worth far more than
  1 mana.
- **Flood fix:** Discard 2 land cards from hand to draw 1 card.
  Once per turn. 2-for-1 rate ensures mitigation, not advantage.

See [GameBalance.md](GameBalance.md) section 3.1 for full analysis,
degenerate case testing, and simulation questions.

## Mana Constraints

In the early rules subset we're implementing:
- No hybrid mana (no {W/U} costs)
- No phyrexian mana (no {W/P} costs)
- No snow mana
- No X costs in v1 (stretch goal)
