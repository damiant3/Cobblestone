# AI Gameplay -- The General, Posture, and Auto-Play

## Overview

Players in CodexMagic do not micromanage every game action. Each
player's AI supervisor is embodied by their **General** -- a card on
the battlefield that serves as the player's avatar, carries the life
total that determines win/loss, and shapes how the AI makes decisions
through behavioral modifiers. The player sets posture and priorities;
the General executes.

This design serves three goals: games resolve faster, strategic depth
shifts from mechanical execution to high-level command, and the
General card itself becomes a deckbuilding and collection axis -- your
General defines your playstyle at a fundamental level.

## The General Model

```
┌──────────────────────────────────┐
│          Player                   │
│  Sets posture, targets, freezes  │
├──────────────────────────────────┤
│       The General (card)         │
│  Behavioral modifiers shape AI,  │
│  abilities affect the board,     │
│  life total = win condition      │
├──────────────────────────────────┤
│       AI Supervisor (engine)     │
│  Reads General's modifiers +     │
│  player's posture, plays moves,  │
│  escalates ambiguous decisions   │
├──────────────────────────────────┤
│       Rules Engine               │
│  Executes actions, resolves      │
│  stack, advances game state      │
└──────────────────────────────────┘
```

The AI supervisor's behavior is the product of two inputs: the
player's posture (runtime orders) and the General's behavioral
modifiers (baked into the card). The General biases the AI's
decisions -- a General with `AlwaysAttack` combat bias will push the
AI toward aggressive plays even in a `Balanced` posture. The player
can always override, but fighting the General's tendencies costs
attention and intervention.

Choosing a General is the most important deckbuilding decision. It
determines your color identity (deck construction constraint), your
AI's behavioral baseline, your on-board abilities, and your starting
life total. Two players with identical decks but different Generals
will play very different games.

See [Cards.md](Cards.md) for the full General card type definition,
including `GeneralTemplate`, `BehavioralModifier`, and `CombatBias`.

## Posture

Posture is the player's standing orders to the AI. It defines the
general approach without specifying individual moves.

```
Posture = record {
  stance : Stance,
  primary-targets : List Target,
  phase-freezes : List Phase,
  mana-policy : ManaPolicy,
  block-policy : BlockPolicy,
  spell-policy : SpellPolicy
}
```

### Stance

The overall strategic direction:

```
Stance =
  | Aggressive    -- prioritize damage, attack with everything viable,
                  -- spend removal on blockers, play threats over answers
  | Defensive     -- prioritize survival, hold back blockers, save
                  -- removal for must-kill threats, play answers over threats
  | Balanced      -- evaluate each decision on board context, no bias
  | Tempo         -- prioritize board development, curve out efficiently,
                  -- use removal to maintain tempo advantage
  | Control       -- hold resources, counter key spells, play for late game,
                  -- minimize risk, maximize card advantage
```

Stance affects how the AI weighs competing options. In `Aggressive`
stance, the AI will attack with a creature that might trade down in
combat. In `Defensive`, it won't.

The General's `StanceOverride` behavioral modifier shifts the AI's
effective stance. If the player sets `Balanced` but the General has
`StanceOverride Aggressive`, the AI leans aggressive on close calls.
The player's explicit stance always wins on clear decisions, but the
General's bias breaks ties.

### Primary Targets

The player marks specific permanents or the opponent as priority
targets. The AI directs removal, combat, and targeting toward these.

```
Target =
  | EnemyGeneral                 -- attack the opposing General directly
  | Permanent (entity-id : Id)  -- focus removal/combat on this
  | CardType (card-type : CardType) -- prioritize targeting this type
  | Threat (threat-level : High | Medium | Low) -- target by assessed threat
```

When no primary targets are set, the AI evaluates threats
independently based on board state, stance, and the General's
`TargetPreference` modifier. The default target for unblocked
attackers is always the opposing General.

### Phase Freezes

The player can freeze the game at specific phases, forcing the AI to
pause and wait for input before proceeding. Without freezes, the AI
plays through all phases autonomously.

```
Phase =
  | Upkeep
  | Draw
  | PreCombatMain
  | DeclareAttackers
  | DeclareBlockers
  | CombatDamage
  | PostCombatMain
  | EndStep
```

Common freeze configurations:
- **Full auto** -- no freezes, AI plays everything
- **Combat check** -- freeze at DeclareAttackers to review attacks
- **Main phase check** -- freeze at PreCombatMain and PostCombatMain
- **Full manual** -- freeze at every phase (traditional play mode)

### Mana Policy

How the AI taps lands and spends mana:

```
ManaPolicy =
  | AutoTap       -- AI chooses optimal land tapping
  | PreserveMana (colors : List Color) -- keep these colors open
  | ManualTap     -- ask player for every mana decision
```

### Block Policy

How the AI assigns blockers:

```
BlockPolicy =
  | AutoBlock     -- AI assigns optimal blocks
  | NoBlock       -- never block (racing strategy)
  | ProtectLife (threshold : Integer) -- block only if life would drop below threshold
  | ManualBlock   -- ask player for every block decision
```

### Spell Policy

When the AI plays spells from hand:

```
SpellPolicy =
  | PlayOnCurve   -- play spells as soon as mana is available
  | HoldCounters  -- keep mana open for instant-speed responses
  | ConserveMana (amount : Integer) -- always leave this much mana open
  | ManualSpells  -- ask player for every spell decision
```

## AI Decision Engine

The supervisor evaluates each decision point using a scoring model:

### Obvious Moves

These are played automatically without asking the player:

- **Play a land** -- if the player has a land in hand and hasn't used
  their land drop, play the best land (color-fixing priority)
- **Mandatory triggers** -- triggered abilities that must resolve
- **Uncontested attacks** -- attacking when the opponent has no blockers
  and stance is Aggressive or Balanced
- **Lethal on board** -- if the AI detects lethal damage, it takes it
- **Forced blocks** -- blocking to prevent lethal damage to the General
  when life is critical
- **General blocks** -- using the General itself as a blocker when its
  defense value makes it the optimal choice

### Escalated Decisions

These are presented to the player as options:

- **Combat gambles** -- attacking or blocking where the outcome depends
  on whether the opponent has a combat trick
- **Sacrifice plays** -- trading a valuable permanent for a strategic
  advantage
- **Multi-target choices** -- removal with multiple valid targets where
  the best choice is contextual
- **Counter-or-not** -- whether to counter a spell when counter magic
  is limited
- **Resource commitment** -- spending most or all mana on a big play
  vs. holding back

### Decision Presentation

When the AI escalates, it presents the player with:

```
Decision = record {
  situation : Text,           -- "Opponent attacks with 3 creatures"
  options : List Option,
  recommended : Integer,      -- index of AI's preferred option
  time-limit : Seconds,       -- how long before AI auto-picks
  risk-assessment : RiskLevel  -- how much is at stake
}

Option = record {
  description : Text,         -- "Block the 4/4 with your 3/3, take 5"
  outcome-estimate : Text,    -- "You go to 8 life, remove their threat"
  risk : RiskLevel
}
```

If the player doesn't respond within the time limit, the AI takes its
recommended action. This keeps games moving even if a player is
briefly away.

## Intervention

Beyond posture settings, players can intervene at any time:

- **Queue an action** -- "play this land next," "cast this spell on
  your next main phase"
- **Freeze now** -- pause the game at the current priority point
- **Override** -- cancel the AI's planned action and choose manually
- **Mark a card** -- flag a card in hand or on board as "do not play"
  or "play next"

Interventions are processed immediately. If the AI was about to take
an action and the player overrides, the override wins.

## Skill Levels

The AI supervisor has adjustable skill levels, affecting how strong
its evaluations are:

```
SkillLevel =
  | Beginner    -- makes suboptimal plays, teaches through mistakes
  | Intermediate -- solid fundamentals, occasionally misreads complex boards
  | Expert      -- strong play, reads combat math accurately
  | Master      -- near-optimal play, deep strategic evaluation
```

In ranked play, both players' AIs run at Master level -- the
differentiator is the player's General choice, posture decisions,
intervention timing, and deck construction. In casual play, skill
level is adjustable for fun and learning.

## Game Flow Example

A typical turn with AI supervision (Balanced stance, combat-check
freeze):

1. **Upkeep** -- AI handles untap, resolves upkeep triggers automatically
2. **Draw** -- AI draws, evaluates hand (no player input needed)
3. **Pre-combat main** -- AI plays a land (obvious), casts a creature
   on curve (obvious per PlayOnCurve policy)
4. **Declare attackers** -- FREEZE. AI presents the board:
   "You have a 3/3/0 and a 2/2/0. Opponent's General is a 2/5/1 at
   22 life. Recommended: attack with 3/3/0 only (pierces General's
   defense, 2/2/0 would bounce off). Override?"
5. Player accepts or adjusts attackers
6. **Blockers through end** -- AI handles rest automatically, opponent's
   General may block based on its behavioral modifiers

Total player interaction: one decision point, ~5 seconds. The turn
resolves in ~10 seconds total instead of 60+ in traditional play.

## Stack and Cycle Handling

The AI supervisor manages two complex subsystems that players never
see directly:

**Stack resolution:** The spell stack ([SpellStack.md](SpellStack.md))
resolves behind the scenes. When instant-speed interaction occurs,
the AI presents decision points: "Opponent targets your creature.
Respond?" The player sees options, not the stack's LIFO mechanics.

**Bailout cycles:** When infinite loops include player decision
points ([CycleDetection.md](CycleDetection.md)), the AI handles the
prompt. In single-player bailouts, the AI recommends an iteration
count based on board state. In contested cycles (both players have
decision nodes), the AI plays each player's side according to their
posture -- aggressive stance continues favorable cycles, defensive
stance bails early. Players can always override the AI's cycle
decisions.
