# GameState -- Zone Model and Game Record

## Runtime card identity

Every real copy allocated into a match receives a distinct, nonnegative CardId,
unique across both players for that match. CardIds start at zero and are not
reused. `GameState.card-templates[cardId]` stores the index into that game's
template array; `next-instance-id` tracks the allocation count. A CardId is not
a template index, even when the two numbers happen to coincide. Runtime records
represent CardId with Integer; the record sketches below name its role.

`game-load-library` validates a batch of deck template indices before allocating
copies. Invalid batches leave the prior state unchanged. The four entry paths
(MagicServer, standalone Opening, Simulate and SimBaseline) use this allocator.
Draw, discard, mulligan, bounce, exile, destruction and recasting preserve the
allocated CardId. Entering the battlefield does not allocate another card.

| Identifier | Meaning and consumers |
|---|---|
| CardId | One allocated copy. Player zones, Permanent.card-id, manual play, held cards and commitment choices use it. |
| Template index | An index into GameState.templates. The registry, Permanent.template-id, deck construction and format validation use it. `get-template` remains a template-index lookup. |
| Permanent.id | A battlefield incarnation. Target orders, blockers and gemstone payment chains use it. Re-entry assigns a new permanent ID while keeping the CardId. |
| CardToken.token-id | A minted collectible outside the match state. Minting, account collections and clan libraries keep this separate identity. |

Use `game-card-valid` at card-input boundaries, then `get-card-template` or
`card-template-id`. Registry lookup is O(1) and retained registry space is O(C)
for C allocated copies. Loading N copies copies the previous registry and the
destination library once, O(C + L + N) for existing library length L. Ordinary
casts and zone moves do not grow the registry. State copies used by combat,
payment, triggers, cleanup and turn advancement retain the same identity table.

Manual-hand JSON exposes `id` as CardId and `template-id` as its resolved template
index. `/game/play?card=` takes that CardId. Battlefield JSON keeps `id` as the
permanent ID and adds `card-id` alongside `template-id`. The page carries card
IDs unchanged through selection and payment drafts. Negative card-id token
permanents, and token templates, contribute no card when they leave play.

## Per-copy play orders

`card-orders` is a lazily allocated Integer vector indexed by CardId. A missing
entry means Auto (0); stored modes are Auto (0), DoNotPlay (1) and PlayNext (2).
`set-card-play-order` rejects invalid IDs/modes and returns unchanged state for
an equal order. A change copies the existing prefix and extends it only as far
as the requested CardId. It never mutates the previous state's vector.
Every game-state reconstruction preserves the vector; new copies default to
Auto. `consume-play-next` clears only mode 2, leaving a persistent hold intact.
[AIGameplay](../Active/AIGameplay.md) owns the selection and manual override rules.

Reads are O(1) and allocate no heap. Each changed setting or consumed Next
allocates O(C) prefix storage and takes O(C) time, bounded by the match's
allocated card count C. With no garbage collector, repeated changes retain
their old allocations until a covering heap restore or process exit. New hand
metadata and controls take O(H) work for H hand entries; battlefield controls
add O(B) work for B permanents. Existing AI search complexity and compiler
heap/time behavior are unchanged.

## Automatic work

`automatic-work` is a fixed-size record with the automatic action count, last
card/source name, stop kind and count at the stop. Every state copy preserves
it. New matches and fresh supervised turns reset it; a stopped turn retains it
until explicit Continue. It reports an interrupted calculation, not a winner
or draw. AIGameplay owns the budget and manual-play contracts.

## Queued card plays

`queued-cards` is a separate two-slot vector storing an exact CardId, saved
target, creation/attempt turn and stage, label and waiting reason. Every state
copy preserves it. `set-player` clears the corresponding slot if its card
leaves that player's hand. AIGameplay owns execution, payment and target
revalidation. Card and permanent-ability queues can coexist.

## Queued permanent ability orders

`queued-abilities` has one slot per player. A populated slot records the
source permanent ID, ability index, fixed target, creation turn/stage, last
attempt turn/stage, label and waiting reason. Creation and replacement spend
nothing. Each eligible later own main attempts the order once. Source/control
or target loss clears the order at that attempt; unavailable costs retain it.
The page exposes the current waiting reason and cancellation.

`game-set-queued-ability` copies the two-entry vector before replacing a slot,
preserving prior queue records and the other player's order. All game-state
reconstructions preserve the vector. New Game/cancel create empty slots;
refused creation preserves the existing slots. The field adds one pointer per
GameState copy and fixed queue storage plus label text. [AIGameplay](../Active/AIGameplay.md)
owns scheduling, the last-life rule and UI/API behavior.

## Paid spell and contest state


`GameState.disruption` contains the match seed, next contest index, a single
paid-spell window and the latest result. The pending CardId has left hand but
has not entered battlefield or graveyard. The window retains its caster,
original targets and paid chain hardness. Its choices refer to opposing hand
CardIds, not template indices. There is no stack and no nested response.

Combat, payment, triggers, zone changes, cleanup and turn advancement preserve
this state. New Game/cancel replace it; failed creation and refused replies
preserve it. A valid paid Disruption consumes one contest index. Cantrip
responses, Pass and reload consume none. The result retains the actual
chance/roll and distinguishes disruption from loss of the original target.
`reply-fizzled` records a paid Cantrip's illegal target; `abandoned` records
an original spell discarded without effects because the response ended the
game. One Cantrip or Disruption consumes the window, with no second reply.
The state header has fixed size;
a prompted window retains O(H) offered replies. [AIGameplay](../Active/AIGameplay.md)
owns policy, timeout, seed entry points and resolution behavior.


## Consumer boundaries

The pre-change census was checked independently before implementation. These
are the current boundaries that a later identity change must preserve:

| Source | Identity use |
|---|---|
| GameState | Allocates copies; owns lookup, PlayerState zone movers and Permanent identity fields. Removal-by-value selects one nonnegative CardId. |
| MagicServer, opening, Simulate, SimBaseline | Convert template-valued deck inputs into allocated library copies. |
| Supervisor | Resolves hand IDs for gem choice, spell ranking, counter reserves and casting; passes distinct card/template IDs into resolution. Zone effects preserve the original card. |
| Engine | Checks membership and registry validity for manual and commitment choices; carries exact held IDs. |
| GameRules, Simulate | Resolve instance templates for gemstone counts and mulligan scoring; shuffle, bottom and discard move IDs unchanged. |
| Action, Combat, Payment, Trigger, Turn | Preserve registry state. Permanent scans use template-id; target and gemstone IDs stay permanent IDs. |
| MagicServer, GamePage | Resolve card labels and costs on the server; the page submits metadata IDs without treating them as template indices. |
| Deck, CardPool, Token, ClanFormat, ClanPacks | Template construction, pack contents and format copy limits remain template-valued. Collectible token and clan-library IDs are outside live match zones. |
| CycleDetect, MatchRecord, EventBus | Zone counts, opaque action IDs or cross-game event payloads; no hand-ID-to-template decoding. |

`AIGameplayTest` and the API/browser fixtures in `verify-ai.ps1` grade two copies
diverging through play, bounce, discard, replay and exile, IDs beyond template
count, both players, mulligans and state copies. `magic-sim-fixes` grades first
instance discard/pitch. Starter callers inspect `StarterDeckResult` before
loading libraries. A refusal does not enter gameplay or become a partial
simulation aggregate. `magic-starter-deck` grades the five-General seed
corpus and named pool-exhaustion refusals; API arms check that refused game
creation preserves the prior state and pending deadlines.

## Zones

The zone model includes the following domains. The pending spell lives in its
single response window rather than a player zone. Cleanup reduces the active
player's hand to seven; AIGameplay owns automatic retention and manual choices.

| Zone | Ordered? | Visible? | Notes |
|------|----------|----------|-------|
| Library | Yes (top = index 0) | Hidden | Shuffle on certain effects |
| Hand | Yes | Owner only | May exceed seven during the turn; cleanup discards the active player's excess |
| Battlefield | No | Public | Permanents live here |
| Graveyard | Yes (LIFO) | Public | Ordered by arrival |
| Pending spell | One card | Public | Paid slow spell awaiting its single disruption response |
| Exile | No | Public | Removed from game |

## Player State

```
PlayerState = record {
  general : GeneralState,
  prismatic : PrismaticState,
  library : List CardId,
  hand : List CardId,
  graveyard : List CardId,
  exile : List CardId,
  gem-drop-used : Boolean
}
```

Default: 1 gemstone drop per turn. Life total is on the General, not the
player (see below).

## General State

The General is the player's avatar and carries the life total that determines
win/loss. Its runtime state is separate from the permanent list and from the
card-copy registry; template-id identifies the selected General definition.

```
GeneralState = record {
  template-id : Integer,
  life : Integer,              -- default 30, set by GeneralTemplate
  damage-marked : Integer,     -- combat/effect damage this turn
  counters : Integer,
  is-tapped : Boolean,
  power-mod : Integer,
  toughness-mod : Integer,
  defense-mod : Integer,
  loyalty-turn : Integer
}
```

The General is always on the battlefield -- it cannot be exiled,
bounced to hand, or destroyed. Effects that would remove the General
from the battlefield are negated. The General can be tapped and
untaps normally during the untap step.

Damage dealt to the General reduces its `life`, not its toughness.
The General's toughness determines how much damage it can absorb in
a single combat step before excess carries over (see
[Combat.md](Combat.md)), but the General is never destroyed by
damage -- it stays on the battlefield until `life` reaches 0.

**Army Loyalty** is derived, not stored -- it is the sum of CMC of all
non-token creatures the player controls. It changes as creatures enter
and leave the battlefield. See [Cards.md](Cards.md) for how the
General's abilities consume and scale with army loyalty.

## Game Record

```
GameState = record {
  players : List PlayerState,
  generals : List GeneralTemplate,
  battlefield : List Permanent,
  active-player : Integer between 0 and 1,
  priority-player : Integer between 0 and 1,
  turn-number : Integer,
  phase : Integer,
  step : Integer,
  next-permanent-id : Integer,
  next-instance-id : Integer,
  game-over : Boolean,
  winner : Integer,
  templates : List CardTemplate,
  card-templates : List Integer,
  card-orders : List Integer
}
```

## Zone Transitions

Current direct movement helpers preserve CardId. Cards entering the battlefield
become fresh Permanents; cards leaving lose that incarnation's modifications.
A shared `zone-move : GameState, CardId, Zone, Zone -> GameState` dispatcher
and automatic leave/enter trigger events remain design work.

## Card Identity

Cards have a static definition (template) and a runtime identity (entity ID).
A CardId uniquely identifies a specific card instance throughout the game,
regardless of which zone it is in. The template provides base stats. Its current
Permanent incarnation carries battlefield state such as damage, counters and
modifiers; that state does not follow the CardId out of the battlefield.
