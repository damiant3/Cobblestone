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
```

Subtypes are text tags (e.g., "Goblin", "Warrior", "Aura", "Equipment")
stored as a list. Supertypes: Legendary, Basic.

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
  abilities : List Ability,
  rules-text : Text,
  color : ColorSet
}
```

Power/toughness only meaningful for creatures (0 for non-creatures).

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
