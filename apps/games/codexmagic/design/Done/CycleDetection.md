# Cycle Detection — Infinite Loop Handling

## Philosophy

Infinite loops are a legitimate consequence of card interactions.
Two cards whose triggered abilities feed each other, a replacement
effect that undoes the condition it replaces, a state-based action
that creates the state it removes — these are not bugs. They emerge
naturally from a rich card interaction space, and banning them would
cripple card design.

Instead of preventing cycles, the engine **detects them early and
clamps them to a bounded result**. A cycle that would deal infinite
damage deals `max-cycle-iterations` damage. A cycle that would
create infinite tokens creates `max-cycle-iterations` tokens. The
game continues from the clamped state.

## Where Cycles Can Occur

Automatic game events — events that fire without player input — are
the only source of infinite loops. Player-driven actions are
inherently bounded (a player can only take one action per priority
window). Automatic events include:

1. **Triggered abilities** — "When X happens, do Y." If Y causes X,
   the trigger re-fires.
2. **State-based actions** — SBAs loop until stable. If an SBA's
   resolution recreates the condition, it loops.
3. **Replacement effects** — "If X would happen, do Y instead." If Y
   causes X, the replacement re-applies.
4. **Delayed triggers** — "At the beginning of the next end step, do
   X." If X sets up another delayed trigger, the chain continues.

## Detection Mechanism

### State Fingerprinting

The engine maintains a rolling fingerprint of game state at each
automatic event boundary. The fingerprint captures:

```
StateFingerprint = record {
  life-totals : List Integer,
  permanent-hashes : Hash,       -- hash of battlefield permanent set
  hand-sizes : List Integer,
  library-sizes : List Integer,
  graveyard-top-hashes : List Hash,
  stack-depth : Integer,
  counters-hash : Hash           -- hash of all counter values
}
```

The fingerprint is cheap to compute — it hashes the parts of game
state that automatic events can mutate, not the full state.

### Cycle Detection Algorithm

```
CycleDetector = record {
  history : List StateFingerprint,
  iteration-count : Integer,
  max-iterations : Integer,       -- hard clamp, default 500
  cycle-start : Integer | None    -- index of first repeated fingerprint
}
```

At each automatic event step:

1. Compute the current `StateFingerprint`
2. Check if it matches any fingerprint in `history`
3. If match found:
   - `cycle-start` = index of the matching fingerprint
   - Cycle length = `current index - cycle-start`
   - The engine now knows the exact repeating sequence
4. If no match, append to `history` and continue
5. If `iteration-count` exceeds `max-iterations`, force-clamp even
   without a fingerprint match (catches non-exact cycles that diverge
   slowly)

### Floyd's or Brent's?

For the common case (short cycles of 2-4 steps), linear history scan
is fast enough — the history rarely exceeds a few dozen entries before
detection. For adversarial cases where the cycle length is long, we
use Brent's algorithm (tortoise-and-hare variant) on the fingerprint
sequence to detect cycles in O(1) space without storing full history.

In practice both run: linear scan for fast detection of short cycles,
Brent's as a fallback for long cycles.

## Clamping

Once a cycle is detected, the engine must decide what the "result"
of the infinite loop is. The clamping rule:

**Execute the cycle body `remaining-iterations` times, where
`remaining-iterations = max-iterations - iterations-already-run`,
then stop.**

This produces a deterministic, bounded result that represents "what
would happen if this loop ran a very large but finite number of
times."

### Clamping Effects by Type

| Effect in cycle | Clamped result |
|----------------|----------------|
| Deal N damage per iteration | N × max-iterations total damage |
| Gain N life per iteration | N × max-iterations total life gain |
| Create a token per iteration | max-iterations tokens created |
| Draw a card per iteration | Draw until library empty (SBA: lose game) |
| Add counters per iteration | max-iterations × N counters |
| Mill cards per iteration | Mill until library empty |

Note: some clamped results will immediately trigger game-ending SBAs
(e.g., infinite damage kills the target, infinite draw empties the
library). This is correct — the cycle's natural consequence is that
the affected player loses, and the clamp produces the same outcome.

### Player-Choice Cycles

Some cycles include a point where a player can choose to continue or
stop — an optional activated ability, a "you may" trigger, or any
decision node inside the loop. These are **bailout cycles**: the
loop is infinite only if the player keeps choosing to continue.

When the engine detects a bailout cycle, it does NOT auto-clamp.
The behavior depends on how many players have decision points inside
the cycle.

#### Single-Player Bailout

If only one player has a choice point in the cycle, that player is
prompted once:

```
BailoutPrompt = record {
  situation : Text,            -- "Your combo deals 2 damage per loop"
  per-iteration-effect : Text, -- "Each iteration: deal 2 to opponent"
  max-allowed : Integer,       -- max-iterations clamp ceiling
  default : Integer            -- AI supervisor's recommendation
}
```

The player responds with the number of iterations they want (1 to
`max-allowed`). The engine executes that many iterations in a single
batch, applies the accumulated effects, and continues play.

If the player does not respond within the decision timeout, the AI
supervisor picks the recommended count based on the current game
state (e.g., "deal exactly enough damage to win" or "create enough
tokens to establish a lethal board").

#### Multi-Player Contested Cycles

When multiple players have decision points inside the same cycle,
the loop becomes a **contested cycle** — a back-and-forth where each
player's choice at their decision node determines whether the cycle
continues. Neither player can unilaterally set a count; the cycle
resolves through interactive negotiation.

```
ContestedCycle = record {
  cycle-body : List CycleStep,
  decision-points : List DecisionPoint,
  iteration-count : Integer,
  max-allowed : Integer
}

DecisionPoint = record {
  player : PlayerId,
  step-index : Integer,         -- where in the cycle body this falls
  choice-type : ChoiceType,     -- continue, stop, or choose-target
  description : Text
}

ChoiceType =
  | ContinueOrStop              -- "you may" — player decides to keep going
  | ChooseTarget (targets : List Target)  -- pick a target within the loop
  | ChooseMode (modes : List Mode)        -- modal choice inside the loop
```

**Resolution flow:**

1. The engine detects the cycle and identifies all decision points
   and which player controls each one.
2. The cycle runs step by step. At each decision point, the
   controlling player is prompted for their choice.
3. If any player with a `ContinueOrStop` decision chooses to stop,
   the cycle ends immediately at that point.
4. If all players choose to continue, the cycle advances one full
   iteration and prompts again at the next round of decision points.
5. The cycle continues until: a player stops it, the `max-allowed`
   clamp is reached, or a game-ending SBA fires mid-cycle.

**This is the strategic core of contested cycles.** Player A's combo
deals 3 damage per iteration, but Player B has a triggered "you may
gain 2 life" in the same loop. Each iteration, both are prompted:
A decides whether to keep looping (dealing 3 more), B decides whether
to keep responding (gaining 2 more). The net is -1 life per iteration
for B, so B will eventually stop responding — but exactly when depends
on board state, life totals, and what happens after the cycle resolves.

#### Shortcutting Contested Cycles

To avoid tedious click-per-iteration play when the outcome is
deterministic, the engine offers a **shortcut** when it detects that
both players' decisions are forced or obvious:

```
ShortcutOffer = record {
  situation : Text,           -- "You both continue: net -1 life to opponent per loop"
  net-effect-per-cycle : Text,
  projected-end : Text,       -- "Opponent stops at iteration 12 (life would reach 0)"
  both-players-agree : Boolean
}
```

If both players accept the shortcut, the engine batch-resolves the
projected iterations. If either player declines (because they plan
to change their choice at a specific iteration, or they have
information the engine doesn't model), the cycle runs step by step.

The AI supervisor can accept shortcuts on the player's behalf based
on posture — in `Aggressive` stance with a clear advantage, the AI
accepts. In `Control` stance, the AI may decline to keep options open
for a surprise play mid-cycle.

#### Decision Timeout in Contested Cycles

Each decision point has the standard intervention timeout. If a
player doesn't respond:

- Their AI supervisor makes the choice based on posture
- Aggressive stance: continue if the cycle is favorable
- Defensive stance: stop if the cycle is unfavorable
- The timeout applies per-decision, not per-cycle — a player can
  let the AI handle some iterations and intervene on others

#### More Than Two Players (Future)

The current design is two-player. If multiplayer is added later, the
contested cycle model extends naturally: each player's decision points
are processed in APNAP order within each iteration. More players
means more decision points per cycle, but the same stop-on-any-bail
and shortcut mechanics apply.

### Partial-Cycle Clamping

If the cycle has side effects that differ by iteration (e.g., odd
iterations deal damage, even iterations gain life), the clamp runs
complete cycles to maintain the ratio. The final clamped iteration
count is rounded down to the nearest multiple of the cycle length.

## Integration Points

### Triggered Ability Resolution

The trigger resolution loop in `Engine.codex` wraps the trigger
dispatch in the cycle detector:

```
resolve-triggers : GameState -> CycleDetector -> GameState
resolve-triggers state detector =
  let triggers = pending-triggers state
  if empty triggers then state
  else
    let state' = resolve-top-trigger state
    let fingerprint = compute-fingerprint state'
    let detector' = advance-detector detector fingerprint
    if cycle-detected detector' then
      clamp-remaining state' detector'
    else
      resolve-triggers state' detector'
```

### SBA Loop

The SBA loop (see [StateBasedActions.md](StateBasedActions.md))
already runs until stable. The cycle detector wraps this loop and
catches SBA-trigger-SBA cycles:

```
check-sbas-with-detection : GameState -> CycleDetector -> GameState
```

### Replacement Effects

Replacement effects are checked inline during event processing. The
engine tracks how many times a specific replacement effect has been
applied to the same event. If the same replacement fires more than
`max-replacement-depth` times (default: 50) on a single event, it
is suppressed for the remainder of that event's processing.

### Stack Resolution

Stack resolution itself cannot loop (the stack is finite and each
resolution removes one entry), but resolution can trigger abilities
that add to the stack. The cycle detector covers this: if resolving
stack entries produces a cycle of trigger → stack entry → resolve →
trigger, it is detected via the fingerprint mechanism.

## Configuration

```
CycleConfig = record {
  max-iterations : Integer,         -- hard clamp, default 500
  max-replacement-depth : Integer,  -- per-event replacement limit, default 50
  log-cycles : Boolean,             -- record cycle data in match log
  cycle-clamp-notification : Boolean -- notify players when a clamp fires
}
```

`max-iterations` is a global constant, not per-card or per-player.
It is set at the network level so all matches use the same value.
Changes to it are versioned in the season manifest.

## Match Record Integration

When a cycle clamp fires, the match record logs:

```
CycleEvent = record {
  turn : Integer,
  phase : Integer,
  trigger-source : CardId,       -- the card whose ability started the cycle
  cycle-length : Integer,        -- how many steps before repeat
  iterations-run : Integer,      -- how many iterations before clamp
  clamped-effects : List Effect, -- what the clamp produced
  fingerprints : List StateFingerprint  -- the repeating sequence
}
```

This data is available in replays. Viewers can see exactly where
the cycle occurred, what caused it, and what the clamped result was.
This is also valuable for card balance — if a specific card appears
in cycle events frequently, it may be a ban candidate.

## QA Interaction

The card generation QA gate (see [CardGeneration.md](CardGeneration.md))
does not reject cards that produce cycles. Instead it:

1. **Identifies** cycle-producing interactions with the existing pool
2. **Annotates** each cycle with its expected clamped outcome
3. **Flags for review** any cycle whose clamped outcome is
   game-ending (e.g., infinite damage → lethal) to ensure this is an
   intentional power-level decision, not an accident
4. **Measures** cycle frequency in Monte Carlo simulation — a card
   that produces cycles in >10% of games is flagged for balance review

Some cycles are desirable (a two-card combo that wins the game if
assembled is a valid deckbuilding reward). The QA gate ensures they
are intentional and appropriately costed.

## File Plan

```
codex.magic/
  CycleDetect.codex    -- fingerprint computation, cycle detection algorithm
  Clamp.codex          -- clamping logic, effect accumulation
```
