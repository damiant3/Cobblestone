# Cards — Data Model, Types, Abilities

## Card Types

```
CardType =
  | Land
  | Creature
  | Instant
  | Sorcery
  | Enchantment
  | Artifact
  | General
```

Subtypes are text tags (e.g., "Goblin", "Warrior", "Aura", "Equipment")
stored as a list. Supertypes: Legendary, Basic.

## The General

The General is a special card type that embodies the AI supervisor
and serves as the player's avatar on the battlefield. Each player
selects a General during deck construction — it is not part of the
deck but starts the game on the battlefield in the **command zone**.

```
GeneralTemplate = record {
  name : Text,
  mana-cost : ManaCost,         -- determines color identity for deck building
  card-type : General,
  subtypes : List Text,
  is-legendary : Boolean,       -- always True for Generals
  power : Integer,
  toughness : Integer,
  defense : Integer,
  life : Integer,               -- the General's life total (default 30)
  abilities : List Ability,
  behavioral-modifiers : List BehavioralModifier,
  rules-text : Text,
  color : ColorSet
}
```

The General's `life` total is the win condition — when a General
reaches 0 life, that player loses the game. Players do not have
their own life total. The General IS the player for the purposes
of damage, life gain, and life loss.

The General has P/T/D like a creature and can be attacked in combat.
Combat damage dealt to the General reduces the General's life total.
The General can also block. However, the General cannot be destroyed
by conventional means — if it would receive lethal damage (damage >=
toughness), it takes the damage to its life total but remains on the
battlefield. The General is removed from the game only when its life
reaches 0 (game over).

### Behavioral Modifiers

Generals carry **behavioral modifiers** that shape how the AI
supervisor plays. These are abilities unique to the General card type
— they don't produce game effects directly but modify the AI's
decision-making:

```
BehavioralModifier =
  | StanceOverride (stance : Stance)
    -- forces the AI to favor a specific stance when evaluating options
  | CombatBias (bias : CombatBias)
    -- tilts combat decisions (e.g., always attack if able)
  | SpellPriority (card-type : CardType) (weight : Integer)
    -- AI prioritizes casting certain card types
  | TriggerAggression (threshold : Integer)
    -- AI becomes more aggressive when General's life drops below threshold
  | ResourceHoarding (resource : Resource) (threshold : Integer)
    -- AI hoards a resource until threshold is reached
  | TargetPreference (target-type : TargetType) (weight : Integer)
    -- AI prefers certain targets for removal/combat

CombatBias =
  | AlwaysAttack       -- attack with all viable creatures every turn
  | NeverOverextend    -- keep at least half creatures back
  | SuicidalCharge     -- attack even at unfavorable trades
  | Cautious           -- only attack when guaranteed favorable
```

Behavioral modifiers are visible to both players — you can read your
opponent's General and understand how their AI will tend to behave.
This creates a mind-game layer: the player can override the AI's
behavioral tendencies at any time, but the opponent doesn't know
whether the player is following or fighting their General's instincts.

### General Abilities

Beyond behavioral modifiers, Generals have standard abilities
(static, triggered, activated) that function like creature abilities.
These are game-mechanical effects:

- A General might give all creatures of a certain type +1/+1
- A General might have a triggered ability that draws a card when a
  creature enters the battlefield
- A General might have an activated ability that costs mana

General abilities are part of the game rules. Behavioral modifiers
are part of the AI layer. Both are printed on the card.

### Army Loyalty

The General's power scales with its army. **Army Loyalty** is a
dynamic value computed from the combined converted mana cost (CMC)
of all non-token creatures the General's controller has on the
battlefield. Token creatures do not contribute — only creatures that
were cast from hand and paid for with real mana count toward loyalty.

```
army-loyalty : GameState -> PlayerId -> Integer
army-loyalty state player =
  sum (map cmc (non-token-creatures state player))
```

Army Loyalty functions like planeswalker loyalty in MTG — it is the
resource that powers the General's activated abilities. Generals have
abilities that cost loyalty to activate:

```
GeneralAbility =
  | LoyaltyCost (cost : Integer) (effect : Effect)
    -- spend N loyalty to produce an effect
  | LoyaltyGain (threshold : Integer) (effect : Effect)
    -- triggers when loyalty reaches or exceeds threshold
  | LoyaltyScale (effect : Effect)
    -- effect's magnitude scales with current loyalty
```

**Examples:**
- "[-3]: Deal damage equal to your army loyalty to target creature."
  (Costs 3 loyalty = you need at least 3 CMC of non-token creatures)
- "[+0, threshold 10]: When your army loyalty reaches 10, draw 2 cards."
- "[Scale]: All creatures you control get +X/+0 where X is your army
  loyalty divided by 5."

Loyalty is not spent permanently — it is recalculated each time it's
checked, based on current board state. "Spending" loyalty on an
ability means the ability checks that you have enough, then the
effect resolves. If creatures die before the ability resolves (in
response), the loyalty check may fail and the ability fizzles.

This creates a natural army-building incentive: casting real creatures
(not just spawning tokens) powers up your General. It also creates
interesting tension — do you cast a big creature to boost loyalty, or
hold mana for removal? Opponents can weaken your General by killing
your army, reducing your loyalty and locking out your best abilities.

Token creatures are excluded to prevent degenerate loyalty-pumping
via cheap token generation. A card that creates ten 1/1 tokens
shouldn't give the General 10 loyalty worth of free abilities. Only
mana investment counts.

### General Leveling

Generals have tiered abilities that unlock at army loyalty
thresholds. Each General card prints three ability tiers:

| Loyalty Threshold | Unlock |
|-------------------|--------|
| 5 | First loyalty ability |
| 10 | Second loyalty ability |
| 20 | Ultimate ability |

Abilities are locked until the threshold is reached. If creatures
die and loyalty drops below a threshold, the ability locks again.
This creates a natural dramatic arc: early game is creature
development, mid-game is General activation, late-game is
loyalty-powered finishers. The opponent can de-level your General
by killing your army.

See [GameBalance.md](GameBalance.md) section 3.5 for design
rationale and simulation data on General leveling impact.

## Card Template

The static definition shared by all copies of a card:

```
CardTemplate = record {
  name : Text,
  mana-cost : ManaCost,
  card-type : CardType,
  subtypes : List Text,
  is-legendary : Boolean,
  is-basic : Boolean,
  power : Integer,
  toughness : Integer,
  defense : Integer,
  abilities : List Ability,
  rules-text : Text,
  color : ColorSet
}
```

Power/toughness/defense only meaningful for creatures (0 for non-creatures).
Stats are displayed as P/T/D (e.g., a 3/4/2 creature has 3 power,
4 toughness, and 2 defense).

**Defense** is the damage absorption threshold per attacking source.
Any single source of combat damage with power at or below the
creature's defense value deals zero damage to it. Defense does not
reduce damage — it either blocks it entirely or has no effect. See
[Combat.md](Combat.md) for full rules.

## Mana Cost

See [Mana.md](Mana.md) for full model. A cost is a record of color
requirements plus generic:

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

Converted mana cost (CMC) = sum of all fields.

## Abilities

Abilities come in three kinds:

1. **Static** — continuous effect while on battlefield (e.g., "Other creatures you control get +1/+1")
2. **Triggered** — fires when a condition is met (e.g., "When this enters the battlefield...")
3. **Activated** — player pays a cost to use (e.g., "{T}: Add {G}")

```
Ability =
  | StaticAbility (effect : Effect)
  | TriggeredAbility (trigger : TriggerCondition) (effect : Effect)
  | ActivatedAbility (cost : ActivationCost) (effect : Effect)
```

## Keyword Abilities

Implemented as flags on creatures (simple case) or as ability instances
(complex case with parameters):

| Keyword | Effect |
|---------|--------|
| Flying | Can only be blocked by creatures with flying or reach |
| Reach | Can block creatures with flying |
| First strike | Deals combat damage in the first strike step |
| Trample | Excess combat damage carries over to defending player |
| Haste | Not affected by summoning sickness |
| Vigilance | Attacking doesn't cause it to tap |
| Lifelink | Damage dealt also gains that much life |
| Deathtouch | Any damage is lethal |
| Defender | Cannot attack |
| Protection (color) | Can't be blocked, targeted, dealt damage, or enchanted by that color |
| Intimidate | Can only be blocked by artifact creatures or creatures sharing a color |

## Color Identity

```
ColorSet = record {
  white : Boolean,
  blue : Boolean,
  black : Boolean,
  red : Boolean,
  green : Boolean
}
```

A card's color is determined by its mana cost symbols plus any color
indicators. Colorless cards have all fields False.
