# AI Gameplay -- The General, Posture, and Auto-Play

## Runtime contract

`GamePage` selects either player's General before a game and edits each
player's stance, phase freezes and block policy during play.
The page acknowledges settings from the game API; rejected settings leave
the last acknowledged posture visible. Auto sends one step at a time and
stops at a freeze. Continue resumes the saved turn. Manual blocks select a
creature or the defending General, then an attacker, before Continue.

Starter generation returns `StarterDeck (DeckList)` only after Standard
validation, or `StarterRefused (Text)` naming the General, seed and number
of cards short. Selection targets 24 basic gems and 36 spells, caps nonbasic
copies at four, and bounds retries before scanning the remaining legal choices.
An exhausted pool is a refusal, not a short deck.

Starter gemstones use production metadata, separately from color identity.
Red, Yellow, Blue, Orange and Green select Ruby, Topaz, Sapphire, Carnelian
and Emerald respectively. Purple uses 12 Ruby and 12 Sapphire; Colorless
selects 24 Diamond. Token gems are excluded. A missing required family returns
a named refusal rather than substituting template zero. Selection scans the
pool at most twice and constructs the fixed 24-gem allocation. Regression
checks cover all seven primary-color cases and each generated deck's gem IDs
across the five-General corpus, independently of legality validation.

`/api/magic/deck/generate?general=N&seed=S` returns cards/count/general on
success or an error reason. `/api/magic/game/new` validates both generated
decks before replacing the game. Refusal preserves its input game, orders,
flags, pending decisions and deadlines; it does not undo any earlier explicit
cancellation. Standalone and simulation callers report refusals without
entering gameplay or presenting partial batch metrics.

`/api/magic/deck/validate?general=N&cards=I,J,...` returns `valid` and a
JSON-escaped `reason`. Card IDs are template indices; General IDs identify
entries in the General list. Both use canonical nonnegative decimal, at most
nine digits.
Missing or empty General selects zero; missing or empty cards means an empty
deck. Commas may be literal, `%2C` or `%2c`. Missing or empty format selects
Standard; the optional value accepts `standard` or `Standard`. Other formats
are refused. Standard requires at least
60 cards, at most four copies of each nonbasic template, and the General's color
identity. Basics have no copy limit. Invalid or unknown IDs, tokens and General
cards are refused before indexed validation. Validation does not change the game.
Actual API arms cover generated and basic-heavy decks, copy/size/color failures,
malformed inputs and forbidden types, including quote, backslash, LF and NUL
escaping. New parsed lists and escape fragments scale with input length. The
inherited query parser's growing text allocations and the existing copy-count
check can be quadratic. Compiler behavior is unchanged.

Native dynamic JSON string fields and error messages use
`Encode/Json.json-quote` through `Works/WebRoute.jstr`, including account
exports and disruption names. Quotes, backslashes and the native CCE control
characters NUL/LF are escaped; non-ASCII CCE text is preserved. The host
`web/server.ps1` delegates string quoting to `ConvertTo-Json`. Its isolated
`tests/json-host.ps1` proof uses strict JSON parsing and verifies all 32 Unicode
controls, non-ASCII text, numeric/boolean fields, nullable-key presence and
empty/singleton/multiple arrays without starting a server or reading live state.
Native quote/key goldens, the allocation-scaling guard, API round trips and
actual ClanServer/PlaneServer string responses pass. Escaping scans linearly
and copies plain spans; temporary storage scales with input and escapes.
The HTML mapping of Unicode controls absent from CCE is a separate open item
in `codex/plugs/plugs-backlog.md`; the native proof does not cover that boundary.

The starter regression covers all five server Generals across 600 distinct
seeds: 1-200, plus `base + 97*i + 1` and `base + 97*i + 2` for bases 12345
and 32345 with `i` from 0 through 99. On 2026-10-01 all 3000 decks passed,
with tested and violation counts printed per General. Small-pool refusal
arms and the actual API's early/second-deck refusal state checks also pass.

Hand and zone card IDs identify individual copies, including copies of the same
template. Manual play, commitment choices and held-card lists use those CardIds.
Card metadata carries the separate template index; battlefield actions still
use permanent IDs. [GameState](../Done/GameState.md) owns the registry, zone
identity contract and consumer boundaries.

Each card copy has one AI play order: Auto, DoNotPlay or PlayNext. DoNotPlay
excludes that copy from automatic gem drops, spell casts and counter-ray
reservation. It survives manual play, zone moves and cleanup until replaced.
PlayNext prioritizes that copy among eligible gems or spells and clears to
Auto after successful automatic or manual play. An unavailable Next stays
flagged while other eligible cards can play. Multiple Next spells use the
existing General/stance score; Next gems use hand order. Gems still precede
the spell pass, so these flags do not create a cross-action queue.

Flags do not bypass legality, payment, automatic benefit checks, reserves,
phase freezes or all-rays commitment prompts. Explicit manual play and an
approved commitment Cast override DoNotPlay. Setting a flag while a commitment
is pending preserves its nonce and deadline; Hold remains scoped to that main
phase, separately from persistent DoNotPlay.

`/api/magic/game/card-order?player=P&card=C&order=M` accepts a CardId in that
player's hand or an owned non-token battlefield copy. Modes are 0 (Auto),
1 (DoNotPlay), 2 (PlayNext). Hidden library cards, other owners, invalid IDs,
invalid modes and finished games are refused. The current local arena exposes
both players' orders; this endpoint is not an authenticated multiplayer boundary.
A successful `/game/new` clears all flags. Reload preserves acknowledged state.

GamePage shows the active hand's per-copy controls and battlefield controls
labelled `Recast by <General>`. Battlefield flags govern a later play after
the card returns to hand, not attacks or abilities of its current permanent.
They belong to the original owner even when another player controls the card.
Hand JSON exposes `play-order`; battlefield JSON adds `owner`, `play-order`
and `orderable`. Rejected edits leave the acknowledged controls unchanged.
The browser grader covers duplicate copies, rejection, reload, manual override,
consumption and both controls and captions fitting a 390-pixel viewport.

The eight freeze points precede upkeep (including untap and ray refresh),
draw, pre-combat main, attackers, blockers, combat damage, post-combat main
and end-step cleanup. Wire phase ids are 0 through 7 in that order; stage 8
is complete and cannot be frozen. The active player's settings govern every
phase except blockers, which uses the defender's settings. Continue approves
only the current phase; the next configured freeze stops again. Removing a
freeze setting does not release an existing pause. Victory ends the turn
without waiting for later freeze points.

`/game/freeze` changes one player's phase setting. The older `/game/posture`
`freeze` field changes only the attacker-phase setting and preserves all
others. Phase labels and saved settings come from the API. Reload recovers
an active paused game; a finished game offers setup for the next game.

Clicking New game from an active or finished board sends
`/api/magic/game/cancel` before entering setup. The server replaces the old
game with an inactive, unfinished placeholder: no cards, battlefield or pending
spell/combat decisions. It preserves accounts, the nonce sequence and player
settings except targets tied to old permanent IDs, without finalizing an
abandoned match. Cancellation is idempotent.
Both cancel and new-game creation take precedence over an expired decision,
so entering setup does not execute the abandoned plan first.

The page enters setup only after cancellation is acknowledged. That response
clears both decision nonces, target/payment drafts and the local blocker pick.
The timer callbacks then stop; reload remains in setup, including after a
finished game. Failed cancellation leaves the acknowledged game and its choices
visible. Cancel creates fixed-size empty game/turn state plus the standing-order
copies below. Creating the next game remains a separate request.

Successful game creation and cancellation remove both players' PriorityPermanent
orders because those IDs belong to the replaced match. General, type and threat
preferences retain their relative order, and all other posture fields survive.
A refused creation leaves the original posture intact, including its permanent
targets. Filtering T target orders takes O(T) time and replacement-list storage;
fresh posture records leave prior state unchanged. Compiler heap/time behavior
is unchanged.

At the attacker freeze, the engine prepares the General's recommendation once
without tapping. The saved `CombatState.attackers` is then the player's draft.
GamePage shows each legal creature as attacking or staying back; its button
changes that selection. Choosing none is valid. Repeated Step calls, reload,
and posture edits preserve the acknowledged draft rather than replanning it.
Continue revalidates the selected creatures, drops any now-illegal entries and
taps only those still selected. Without an attacker freeze, automatic planning
continues as before. Configured attacker-phase freezes remain untimed; the
optional combat-gamble pause below is separate.

`/api/magic/game/attack?attacker=P&enabled=E` takes a permanent ID and 0 or 1.
It requires an active unfinished game paused at attackers and a legal creature
controlled by the active player. Tapped creatures, summoning sickness without
Alacrity, Stalwart, opposing creatures, noncreatures and unknown IDs are refused.
Repeated equal settings are idempotent. Explicit selection overrides General
and stance preferences, including Defensive, while retaining attack legality.
The API's `attackers` array carries the draft and battlefield `attack-ready`
marks current legality; the page offers editing only during the attacker pause.
Card play flags govern casting and have no effect on this attack selection.

For B battlefield permanents and A selected attackers, editing takes O(B + A)
time and allocates O(A) replacement selection storage. Continue's revalidation
takes O(A*B) time and O(A) plan storage before existing tap/combat work.
The existing recommendation planner runs once when the freeze is first reached.
Compiler heap/time behavior is unchanged.

`Combat choice` is opt-in and off by default. At attackers, it pauses when the
opponent controls an untapped creature and has positive available prismatic
rays, after subtracting spent and committed rays. It uses visible state, not
the opponent's hand. The General's attack plan is prepared without tapping.
An explicit attacker-phase freeze configured when the pause begins takes
precedence and remains untimed.

The timed pause receives a fresh nonce and a 15-second deadline. Continue
accepts the shown plan immediately; untouched, the deadline accepts that same
plan, even if stance changes meanwhile. Attack legality is revalidated before
tapping, and an empty plan remains empty. A successful add/remove edit to the
attacker draft cancels the timer. The player then commits the edited selection
with ordinary Continue. Invalid edits and repeated equal settings change
nothing, including the timer. Settings edits do not replan or reset a pending
deadline. Reload restores its remaining time.

`/api/magic/game/combat-policy?player=P&enabled=E` sets `combat-pause` for player
0 or 1 and E equal to 0 or 1. State JSON has `combat-choice` with a nonce and
`remaining-ms`, or null. `/game/combat-choice?nonce=N&choice=continue` accepts
the plan; `choice=timeout` leaves an early request pending and accepts it once
due. Ordinary Step cannot bypass a pending combat decision. The server checks
expiry before other game requests, so a late attacker edit returns the expired
plan's result rather than changing that plan. A successful `/game/new` cancels the old decision.
An unavailable clock continues the shown plan on the next game request. As with
spell decisions, the browser supplies timer requests; there is no independent
server loop advancing a closed browser's game.

The trigger scans B battlefield entries in O(B) time and can allocate O(B)
evaluated tags. Combat decision metadata is fixed-size and shares the existing
attacker plan. Editing and accepting retain the attacker-selection costs above.
The browser uses one reusable Codex combat timer callback. Compiler heap/time
behavior is unchanged.

`Supervisor.effective-stance` applies General stance and low-life biases
when the player's stance is Balanced. An explicit Aggressive, Defensive,
Tempo or Control order wins. SpellTypePriority changes card ranking;
ResourceHoarding reserves the greatest declared ray amount. NeverOverextend
keeps at least half the attack-ready creatures back unless the effective
stance is Aggressive. Cautious requires favorable visible creature trades
and survival against a blocking General, or lethal damage to that General.
Each defender uses its own posture, and blockers must be legal.

Block policies are AutoBlock, ManualBlock, NoBlock and ProtectLife. NoBlock
adds no automatic assignments; explicit manual blocks remain authoritative.
ManualBlock pauses at blockers when attackers exist. ProtectLife uses the
current combat resolver to forecast defender life with existing assignments
and acts only when the result is strictly below its threshold. Forecasts copy
the mutable player list, replace damage records in temporary state, and release
all scratch without changing the live game or combat.

ProtectLife uses creature blockers with defensive ranking and does not trade
General life through an automatic General block. Explicit attacker priorities
win; otherwise larger incoming life threats go first. The policy skips attacks
that would not damage the General and stops adding blocks when the forecast
reaches the threshold. Existing manual blocks are preserved. The greedy
assignment can miss an attainable threshold or a globally best assignment.

ProtectLife forecasts once before assignment and after each added blocker.
With `K` added blocks, forecasting adds `K + 1` combat resolutions; each releases
its temporary state. Life-threat ranking uses the existing insertion sort.
The native large-combat arm covers 64 attackers and 64 creature blockers.

`/game/blocks` accepts policies 0 (auto), 1 (manual), 2 (none), 3 (protect life),
and thresholds 1 through 999. Threshold buttons select ProtectLife; other
policies acknowledge the default threshold 10. The legacy `/game/posture`
`blocks` field still maps 1 to AutoBlock and 0 to ManualBlock. Omitting that
field preserves the richer policy, which GamePage does for stance changes.
The legacy `auto-block` response is false only for ManualBlock; current clients
use `block-policy` and `block-threshold`. Rejection and reload retain the last
acknowledged setting.

General `LoyaltyCostAbility` activation is available during the General's own
main phases. Army loyalty is the current total nominal light cost of controlled
non-token creatures; the threshold is checked again at activation and is not
spent. One activation is allowed per General per own turn, as ruled in
[Cards](../Done/Cards.md). `GeneralState.loyalty-turn` survives damage, healing,
tapping and cleanup; a later own turn permits activation again.

`/game/loyalty?index=<ability index>` requires a paused main phase, an unused
activation, sufficient current loyalty and a legal target when required.
Target orders and protection rules govern damage/destruction choices.
The current supported effects are damage, life gain, opponent life loss,
draw, creature destruction, equipment destruction, bounce, exile and opponent
discard, friendly creature boosts and keyword grants. Automatic discard abilities
require a nonempty opposing hand.
General loyalty and ordinary activated abilities use the response contracts
below. Passive triggers retain their existing timing.
Lethal resolution finalizes the match once. The page shows army loyalty,
available abilities and the used-this-turn state, including after reload.

Automatic main phases first cast available spells, then use the first legal
beneficial loyalty ability in template order. Draw abilities must leave library
cards; newly drawn cards can be cast in the same phase. Manual spell or payment
mode leaves the ability choice to the player and Continue does not activate it.
A manually used ability also prevents automatic reuse later that turn.

Ordinary battlefield `ActivatedAbility` clauses can be used at an active
player's paused main phase with no pending commitment or response decision. The source must be
a controlled non-General permanent and the index must identify an activated
clause from the supported effect set listed for General loyalty above; NoEffect
is unavailable. General loyalty activation remains separate. All ray, tap, sacrifice
and life costs, plus target availability, are checked before anything is paid.
Ray costs use plain available prismatic rays; gemstone-chain drafting and
General reserve preferences do not constrain this explicit manual action.

Tap costs require an untapped source. Creature tap costs also require that
summoning sickness has ended or Alacrity permits activation; noncreatures do
not inherit that creature restriction. Life payment is not damage. Manual
payment may reach zero, which loses before the effect resolves; all declared
costs are paid together. The core's automatic-eligibility mode refuses a
payment reaching zero.

The supervised engine, including headless run-turn, considers ordinary permanent abilities after spells
and loyalty. Battlefield order, then ability index, determines the first useful
legal action. Each source/ability pair receives at most one automatic activation
or Hold per main phase. The record survives commitment responses and clears at
the next phase. Manual activations do not consume this automatic allowance.
After an activation, newly drawn cards can be cast before the next ability.
Manual spell/payment modes suppress the automatic pass. Ray reserves, sickness,
targets and the existing useful-effect restrictions apply. The AI never pays
life to zero. The shipped pool is unchanged; ability fixtures exercise the pass.

Sacrifice decisions follow the root ruling of 2026-10-01, subject to Damian's
override. Each General has a default-on `Ask before sacrificing` setting.
Disabling the setting means `Never sacrifice automatically`; manual activation
remains available. This differs deliberately from disabling the all-rays pause,
which permits automatic spending. `/game/sacrifice-policy?player=P&enabled=E`
acknowledges the setting without replacing an existing decision.

A sacrifice, enabled all-rays pause or enabled ambiguous-target pause presents
Activate/Hold before any ability cost. The shared commitment payload identifies
the source and ability index, effect, cost and target candidates. Approval
revalidates current costs, reserves, policy and targets. An invalid approval
preserves the decision for Hold. Every ability decision expires to Hold after
15 seconds, including target-only decisions; a missing clock also Holds.
Hold skips the pair for the current main phase. Reload and repeated Continue
preserve the nonce and deadline; New Game/cancel discards the decision. The AI
skips a sacrifice whose recommended target is the sacrificed source and rejects
approval targeting that source. Other candidates remain subject to ordinary
effect legality.

For N considered source/ability pairs, repeated ordered scans and binary used-pair
membership take O(N squared log N) worst-case time before target/payment work.
The sorted used-pair list occupies O(N) live entries and insertion copies
O(N squared) total entries across the phase. Readiness restores target scratch; a pending
choice retains O(B) candidate entries for B permanents. Effect resolution and
reconsidered spells retain their existing costs. Compiler heap/time behavior
is unchanged.

Sacrifice removes the source even with Indestructible. Real cards go to their
original owner's graveyard, retaining CardId and play flags; tokens disappear.
Targets are chosen before costs. If sacrificing the source removes its own
selected target, costs remain paid and that effect has no remaining target.
Activated boosts and keyword grants expire at cleanup, per Cards.md. There is
no added once-per-turn limit: an ability remains usable when its costs can
still be paid. Casting a card does not execute its activated clauses for free.

`/api/magic/game/ability-targets?permanent=P&index=I` previews legal candidates
without spending. `/game/activate?permanent=P&index=I&target=T` revalidates and
executes; omitted target uses current target orders. Source P is a permanent
ID, I is the template ability index, and T uses the shared spell-target IDs.
State JSON exposes `permanent-abilities` with labels, all costs and readiness.
The page shares the manual target picker, shows unavailable paid tap abilities
after reload, and keeps rejected selections available. Repeated use of a
tapped or sacrificed source is refused. Lethal payment or resolution finalizes
the match once. Tests supply activated templates; the shipped card pool is
unchanged. Responses to permanent abilities and generic leave/enter triggers
remain open; the implemented window below is for slow spells.

Readiness checks restore scratch. Listing K activated clauses performs K
readiness checks, each with a source lookup and, when targeted, the shared
candidate/order scan; no candidate collection is retained for every row.
Metadata and widgets add O(K) entries plus their text. Successful payment makes
a fixed number of battlefield traversals for tap/sacrifice before the existing
effect and state-based-action work. Compiler heap/time behavior is unchanged.

### Queued permanent abilities

Each player can keep one queued ordinary permanent ability. The order fixes
the controlled source permanent, ability index and target. Queuing replaces
that player's earlier order and spends nothing. Supported non-General
ActivatedAbility sources can be queued during any active phase, including
outside their controller's turn and when costs are currently unavailable.
The selected target must be legal when the order is created.

Execution waits for a strictly later own main phase: an order created in
pre-combat main first becomes eligible in post-combat main; one created in
post-combat main waits for the next own turn. Phase freezes and manual-mode
pauses still precede execution and require Continue. The queued action runs
before automatic gems, spells, loyalty and ordinary abilities. The source/ability
pair is reserved from ordinary automatic activation until the order clears or
is cancelled; other actions can still use rays or tap the source in combat.
A completed attempt consumes that pair's automatic allowance for the phase.
Direct activation preserves the queue and does not consume its automatic
allowance. The queued action can still execute later if legal and payable;
Cancel removes the queued order.

At an eligible main phase, success clears the order. Loss of source or control,
an unavailable ability definition, or an illegal original target also clears
the order, without substituting another target. Unavailable ray, life, tap or
summoning-sickness costs retain the order for a later main. Each main phase
attempts the order at most once. The root ruling of 2026-10-01, subject to
Damian's override, also retains a cost equal to remaining life with the visible
reason `Would pay last life`; direct manual activation still permits zero.
Explicit queued sacrifices need no second sacrifice prompt. Lethal resolution
stops subsequent main-phase actions.

The page exposes queueable abilities for both controllers, shares the target
picker, and shows the queued source/effect/cost and current waiting reason.
The retained order survives reload and has a cancellation button. An order
that has become affordable displays readiness for a later own main. An invalid
replacement or refused cancellation keeps the acknowledged order. New Game
and cancellation of the match clear both players' orders; refused creation
preserves both.

`/game/queue-targets?player=P&permanent=S&index=I` previews candidates without
spending. `/game/queue-ability?choice=queue&player=P&permanent=S&index=I&target=T`
creates the order; omitted target captures the current recommendation.
`choice=cancel&player=P` clears that player's slot. State JSON includes
`queued-abilities` and `queue-options`. Future queue edits can coexist with an
outstanding spell decision; its deadline and the late-request rules still apply.

Two fixed slots add one pointer to each GameState copy. Changed orders copy
the two-entry vector and allocate a fixed record plus label text. Admission,
status and attempted execution use linear source lookup and the existing target
validator, whose scratch is restored. Marking an attempted pair uses the existing
sorted activation-history insertion. Listing options takes O(B+A+L) work for B
permanents, A ability clauses and L emitted label text; widgets and JSON retain
O(K+L) for K queueable options. Compiler heap/time behavior is unchanged.

### Automatic spell policy

Each player's automatic spell policy governs both main phases. PlayOnCurve
uses the existing General/stance ranking. ConserveMana reserves the requested
number of prismatic rays, including rays already committed elsewhere in the
available-ray calculation. HoldCounters reserves the cost of the cheapest
Disruption in hand whose gemstone payment fits the current board and rays; a free
Disruption needs no reserve and an unaffordable one adds no reserve. The
greater of this policy reserve and ResourceHoarding applies, rather than
adding the two reserves. The reserve stays fixed across casts in one
uninterrupted casting pass. A new main phase, commitment response or casting
pass after a draw ability recomputes it from the current state.

Disruptions are excluded from ordinary main-phase casting. The single response
window below can spend those cards against an opposing slow spell.
`/game/spells` acknowledges
policy 0 (PlayOnCurve), 1 (HoldCounters), 2 (ConserveMana), or 3 (ManualSpells) and a reserve
from 0 to 999. GamePage's reserve buttons select ConserveMana. Other posture,
freeze and target edits preserve the policy; reload restores each player's
acknowledged policy.

Automatic main phases pause before a cast would spend every positive
unreserved ray. The quote includes optional Obsidian activation and the actual
selected gemstone/Diamond payment. The setting is on by default and appears
as `All-rays choice` in each General's orders. Manual spell/payment modes do
not add an automatic commitment prompt. Headless `run-turn`/`run-game` use
`posture-unattended`, explicitly disabling prompts.

The prompt appears above the battlefield with Cast and Hold. No part of the
pending payment happens before Cast. Cast revalidates the card, targets,
reserves and kept gemstones against current orders, then spends once. Hold
skips that card ID for the current main phase; cheaper alternatives may still
play and use rays. Held IDs clear at the next phase, so the card is eligible
again in the next main phase. Other phase freezes remain in force.

`/game/commitment-policy?player=0&enabled=0` changes the setting without
releasing an existing prompt. `/game/commitment?nonce=N&choice=cast` or
`choice=hold` answers the current prompt. Each prompt receives a fresh nonce
and a deadline 15000 milliseconds after the server's HPET-based clock reading.
Repeated Step/Continue requests preserve the nonce and deadline; stale nonce
requests cannot cast. Manual play, Fluorescence and loyalty activation are
unavailable while the choice is pending.

At or after the deadline, Cast resolves to Hold. An unavailable clock also
chooses Hold. The server checks expiry on game requests and returns the Hold
result instead of executing a late request; retry a late settings edit after
that response. `/game/new` instead replaces the game after both decks succeed;
a generation refusal leaves its input state unchanged.
The page sends Hold at expiry and uses the server's remaining milliseconds
after reload. It has one reusable timer, stops Auto while a choice is pending,
and retains a rejected Cast until the player chooses Hold or the deadline
expires. No independent server game loop advances an unattended browser game.

Pending all-rays state has a fixed header; an ambiguous-target decision also
retains O(C) candidate records for C choices. Filtering H hand entries against D declined IDs
costs O(H*D) per resumed scan, with O(H) candidate and O(D) held storage. Up to
H separate declines can therefore require O(H^3) membership comparisons in
the hand-only worst case, in addition to existing target/payment searches.
The Codex timer callback is reused.

Automatic `Target choice` is opt-in and off by default. With it enabled, a
payable automatic spell pauses before spending when it has at least two legal
targets and no acknowledged target order matches any candidate. Matching
permanent, General, type or threat orders already express a preference and
suppress this prompt. The shared manual-picker candidate builder supplies the
options, including targets enabled by a payable optional Fluorescence activation.
Loyalty abilities remain governed by their existing target orders.

The prompt saves the CardId, candidate list and recommendation. Clicking
`Cast at <target>` selects that candidate and casts; `Cast recommended` uses the
saved recommendation. Cast revalidates membership, current target legality,
payment, reserves and kept gemstones before spending. Invalid choices preserve
the prompt. Hold skips this CardId for the current main phase. Changing settings
or repeating Step does not regenerate candidates, release the pause or reset its
deadline. Reload uses the server's remaining time. No Continue bypass is exposed.

After 15 seconds, the server attempts the shown recommendation. It Holds if the
original quote spent all positive unreserved rays, or if current payment would
now do so, even when the separate all-rays-choice setting is off. A newly set
DoNotPlay or manual spell/payment mode also makes timeout Hold. If the saved
recommendation is no longer legal or payable, timeout Holds instead of choosing
a different target or retaining an expired prompt. Explicit timely Cast can
override a card hold. An unavailable clock Holds. Late requests return the
timeout result instead of executing their requested action.

`/api/magic/game/target-policy?player=P&enabled=E` sets `target-pause` for player
0 or 1, with E equal to 0 or 1. `/game/commitment` also accepts `target=T` on
Cast and `choice=timeout`; an early timeout request leaves the choice pending.
The commitment JSON adds `targets`, `recommended` and `all-rays`. Both decision
kinds use the existing fresh nonce, deadline and browser timer. Default and
headless unattended postures keep automatic target prompts off.

Ambiguity checks release scratch and add the existing candidate scan plus
O(P*C) target-order matching for P orders and C candidates. A prompt captures
its candidates once; subsequent state responses label that snapshot. Boolean
legality checks release their candidate scratch. The candidate builder and
label costs are described with the manual picker below. Compiler heap/time
behavior is unchanged.

ManualSpells pauses before both main phases. The active player's hand shows
playable cards and unavailable cards. `/game/play?card=<id>` plays one chosen
card and keeps the same phase paused. Gemstones use the one-per-turn gem drop;
spells spend their ray cost and use the selected target, falling back to the
acknowledged target queue when no explicit target is supplied. Manual
choices override General reserve preferences. Continue finishes that main
phase without automatic gem play or spell casting. Switching back to an
automatic policy still requires Continue to release an existing pause.

The play handler rejects absent cards, insufficient rays, unavailable targets,
Disruptions, General cards, repeated gem drops and calls outside a paused
manual main phase. Lethal resolution immediately ends the turn and finalizes
the match once. Reload restores the pending hand choices. Manual and automatic
casting share the payment planner below.

Choosing a manual spell fetches `/api/magic/game/targets?card=C`. This read-only
preview requires a playable card at a manual main-phase pause. It returns the
current legal candidates and the General/target-order recommendation. The shared
candidate builder applies effect domains, ownership, Shroud, enemy Hexproof,
Indestructible for destruction, and active Fluorescence. Friendly boosts can
target allied Hexproof creatures. A permanent's entry self-grants do not require
an external target. This is one shared target for a spell's effects, not a list
of simultaneous independent targets.

GamePage shows the candidate picker before spending. Cancel leaves the card in
hand; choosing a target either plays the spell or opens manual payment with
that target displayed. `/game/play?card=C&target=T` accepts permanent IDs or
General IDs -1 (player 0) and -2 (player 1), subject to the spell's current
legality. It rechecks the target before any payment. A stale, malformed or
illegal target spends nothing and leaves the card available. The selection
overrides standing target orders for that cast only. Manual `chains` and the
target travel in the same request. Rejection preserves the picker or payment
draft; reload discards an unsubmitted local draft. Spells with no target choices
continue directly to payment or play. Loyalty abilities still use target orders.

For B battlefield entries and A spell abilities, candidate filtering takes
O(B*A) time beyond existing score evaluation. Candidate lists and mapped
abilities occupy O(B+A); evaluated tags can add O(B*A) temporary allocations.
Preview label lookup adds
O(B*C) time for C candidates, at most B+1; JSON uses chunks rather than growing
prefix copies. Explicit legality queries release their temporary allocations.
The UI adds O(C) controls. Compiler heap/time behavior is unchanged.

Primary target orders form an ordered queue: enemy General, named opposing
permanent, card type, or high/medium/low threat. The first order with an
eligible target wins; missing or unusable targets fall through to the next
order and finally the default threat choice. Explicit orders override the
General's TargetPreference weights. Medium selects the score nearest the
integer midpoint estimate (`low / 2 + high / 2`) of the lowest and highest
eligible permanent scores; General
targets are excluded from threat tiers. The API rejects allied/missing
permanent selections, deduplicates orders and drops departed permanents when
appending. The page adds, removes and clears acknowledged target orders.

Direct damage, creature destruction, equipment destruction, bounce and exile select targets
before paying. Shroud, opposing Hexproof, effect domains and indestructibility
govern eligibility. Damage admits creatures and Generals; DestroyTarget admits
creatures and DestroyEquipment admits equipment. Indestructible excludes
destruction, not damage, bounce or exile. Bounce and exile admit opposing
non-General permanents. A shared target must satisfy every supported targeted
effect on the spell. An uncastable targeted spell stays in hand and the caster
can choose another spell. Resolved spell cards enter the caster's graveyard;
a destroyed permanent returns its card identity to its owner's graveyard,
and a token contributes no card. Automatic blockers address priority attackers
first without changing the declared attack/damage order or reusing a manual
blocker; acknowledged manual blocks remain assigned. Ordinary attacks
still target the enemy General; no effect currently permits attacking another
permanent. Target searches release temporary allocations; blocker ordering
retains one linear list and releases its ranking records.

ReturnToHand and ExileTarget remove the permanent and move its original card
identity to the owner's hand or exile, even when another player controlled it.
Tokens disappear without adding a card. Missing targets and Generals do not
move. Zone changes discard the permanent's accumulated modifications.
DiscardCards moves the opponent's first cards to the graveyard in hand order,
clamped to the available hand; nonpositive counts do nothing. Automatic
Obsidian activation for a discard bonus requires cards in the opponent's hand.
Bounce and exile bonuses use the ordinary legal-target and payment checks.

Zone movement makes linear battlefield traversals and retains a replacement
battlefield list plus destination-zone growth. Discard traverses the hand once
and retains the remaining hand and graveyard growth; oversized counts do not
add work beyond the available cards. Compiler heap and time behavior is unchanged.

BoostPTD and GainKeyword select one controlled creature. Allied Hexproof is
allowed; Shroud is not. Enemy target orders fall through to eligible friendly
creatures, ranked by the existing card score and applicable type/threat orders.
The named-permanent API queue still accepts only opposing permanents. A spell
mixing friendly grants with hostile targeted effects has no shared target and
stays in hand. Keyword grants exclude creatures already carrying that keyword.

Direct spell and loyalty grants last until cleanup. Temporary stat deltas and
keyword provenance live on each permanent and survive tap, damage and trigger
copies. Cleanup removes damage and temporary grants before checking state-based
actions. Innate keywords and persistent boosts survive; a later persistent grant
of a temporary keyword makes that keyword survive too. Grant helpers allocate
fresh permanent/expiry records rather than mutating records held by callers.

Automatic casting and loyalty activation hold temporary grants in the second
main phase. Boosts must be nonnegative with at least one positive component;
Stalwart and Flash grants are held. Manual casting can use those effects when
a legal target exists. The AI uses the existing target score; it does not predict
the best tactical use of each keyword. Expiry state has fixed size per permanent.
Grant application and expiry make linear battlefield traversals; target queries
inspect abilities per candidate and release scratch. Cleanup then uses the
existing state-based removal scans.

`Engine.SupervisedTurn` preserves game state, declared attackers, blocks
and the current stage across requests. A repeated unapproved step at a
pause does not repeat upkeep, drawing or casting. A General can block once
per combat when untapped and able to block that attacker. Its modified
power and defense apply; damage reduces life even past toughness, and
marked damage alone never removes the General. Zero life still loses.

`Payment` enforces ordinary gemstone payments for automatic and manual
main-phase casts. Only the caster's untapped, unattached Gemstone permanents
count. White uses no stone; Red, Yellow and Blue each use one matching stone
per unit. Purple uses Ruby plus Sapphire. Orange and Green first use their
direct stone, then Ruby plus Topaz for Orange or Topaz plus Sapphire for Green. The planner accounts for
all costs together, preventing a primary from serving two chains. Successful
casts spend rays and tap exactly the selected stones. Failed payments keep
cards and resources unchanged; automatic selection can try another card.
Readiness queries release their scratch allocations. The ordinary planner
uses linear battlefield scans and prefers direct secondary stones.

When ordinary payment is unavailable, Diamonds repeatedly double a chain's
output, including White and mixed-color chains. One chain spends one ray;
with `d` Diamonds, that chain supplies `2^d` units of one color. For example,
two Diamonds plus Ruby plus Sapphire supply four Purple units for one ray.
The rule is in [ManaRedesign](../Done/ManaRedesign.md). The planner
preserves ordinary payment when available; the fallback finds a feasible
payment rather than promising globally minimum ray or stone use. Diamonds
are shared across colors and tap with the other selected stones. Manual hand
buttons quote the actual ray payment, including amplification. Automatic
selection admits a spell whose nominal light cost exceeds available rays
when an amplified payment fits.

The search enumerates Diamond allocations per color, with at most seven
color levels. For a fixed color and Diamond count, concentrating the Diamonds
on one chain maximizes output; remaining same-color chains use no Diamonds.
Feasibility checks prune branches using the minimum remaining chain counts.
Search arrays are fixed-size scratch; readiness queries release the plan.
The native assignment oracle independently distributes a small Diamond supply
among unused, Red and Blue chains and compares feasibility with the planner.

`LightPayment.hardness` records the sum of the paid chains' hardness for
disruption contests. Each chain sums its non-Diamond gemstones,
then doubles that sum for each Diamond on that chain. Diamond's own template
hardness is not added, matching ManaRedesign's Sapphire/Diamond worked example.
Unfiltered White and Diamond-only chains contribute zero. Invalid plans have
zero hardness and a negative ray count. The paid spell snapshots the caster's
hardness; the opposing response uses its actual selected payment plan.

Automatic plans consume selected gemstones in battlefield order within each
family. Chains are assigned in Red, Yellow, Blue, Orange, Green, Purple order;
Orange/Green direct stones precede mixed chains. Diamonds amplify the first
chain of their color, as in the existing search. This is a deterministic
assignment, not a maximum-hardness optimizer. Manual plans use the submitted
chain groups, including paid overcasting chains; different groupings of
different-quality gemstones can therefore produce different hardness.

Automatic hardness accounting adds O(B+S+D) time for B battlefield entries,
S selected stones and D used Diamonds, beyond the existing payment search.
Its O(S) scratch lists are released before the final plan; the retained plan
adds one integer. Manual accounting adds O(C+B*S) time for C submitted chains
and constant scratch space beyond the existing chain validator. Readiness still releases the whole
plan. Compiler heap/time behavior is unchanged.

An untapped, unattached Obsidian owned by the active player can activate
Fluorescence for one ray. The stone taps; repeated activation during the same
turn is refused. `/game/fluorescence?stone=<id>` accepts the action only at a
paused main phase. The page shows the activation control, remaining ray usage
and active flag. Cleanup clears the flag without refreshing spent rays.
Obsidian never belongs to an ordinary color-payment chain.

The AI can activate before a permanent or nonpermanent spell when the extra ray and spell
payment fit its reserves, an Obsidian is available and not kept, and every
Fluorescence clause is a supported benefit: positive damage, life gain,
opponent life loss or draw, discard from a nonempty opposing hand,
creature destruction, equipment destruction,
bounce, exile, nonnegative boosts with a positive component, or keyword grants
other than Stalwart and Flash. Temporary grants also obey the main-phase limit
above and require an eligible friendly target.
Draw clauses must leave cards in the library after the spell's direct draws.
The same supported-benefit check applies when the flag is already active.
Only-Fluorescence nonpermanent spells stay in hand when the flag cannot be enabled.
An unusable optional bonus does not stop an otherwise castable base spell
while the flag is off.

Active Fluorescence clauses participate in shared-target eligibility,
including protection and effect domains; inactive clauses do not require or
restrict targets. Target previews release temporary ability/candidate lists.
Permanent-entry BoostPTD/GainKeyword clauses apply to the entering creature
itself and persist after cleanup, as specified by ManaRedesign's Obsidian Shade.
They are entry modifications, so the entrant's Shroud does not prevent them.
Other targeted entry clauses use the spell's shared hostile target and ordinary
direct effect resolution. Missing hostile targets skip those targeted effects;
untargeted life, draw and discard effects still resolve. Missing targets do not
prevent the permanent entering. Ordinary static, triggered and activated
abilities do not fire merely because the permanent enters through this path.

The AI activates only when the bonus and payment checks succeed. Entry grants
require a creature; keyword grants must add a keyword absent from its template.
Permanent-entry grants remain useful in the second main phase because they
persist. If activation cannot provide a legal hostile target, the AI can still
cast the unlit base permanent. With the flag already active, unsafe draw or
unsupported/unhelpful bonuses hold the spell. Entry draw safety counts only
Fluorescence draws, not unrelated printed abilities.

Benefit scans are linear in abilities. Entry target queries release scratch;
each entry grant traverses the battlefield and retains its replacement list.
For A clauses and B permanents, grant list work is O(A*B). The current base
CardPool has no Fluorescence clauses; native/API fixtures grade conditional
spells and entry effects without changing card designs.
HoldCounters reserves rays only; other spells can still tap stones needed
by a future response unless those gemstone families are explicitly kept.

Each player's `preserved-stones` mask excludes selected gemstone families
from both automatic main-phase payment searches. GamePage labels these
controls `AI Ruby: keep/use` and likewise for Topaz, Sapphire, Carnelian,
Emerald, Diamond and Obsidian. Every stone in a kept family is excluded; the planner
can use an alternative chain from other families. Keeping Ruby can still
allow Orange through Carnelian; keeping Diamond prevents amplification.
Purple has no stone of its own: keep Ruby and Sapphire to retain that chain.
White rays are governed by ConserveMana rather than a gemstone family.

`/game/mana` accepts player 0 or 1, stone 0 through 6 in the order above,
and keep 0 or 1. Duplicate settings are idempotent; rejected requests leave
the acknowledged setting intact. Other posture edits and reload preserve
both players' independent masks. A manual card choice or Obsidian activation
overrides the AI's gemstone preservation; manual card choices also override
General ray-reserve preferences.

Each player's `manual-payment` flag implements manual chain selection.
`/game/payment-mode` acknowledges player 0 or 1 and manual 0 or 1. Manual
payment pauses both main phases even with an automatic spell policy; card
choices are player-directed until Continue ends the phase without automatic
gem play or casting. Gem drops still need no payment and remain once per turn.

GamePage shows the required light cost when choosing a card and building its
payment. Players add eligible battlefield stones to a ray, finish that ray,
then add another. Finishing an empty ray creates White. Pay and play includes
an unfinished nonempty ray. Clear chains resets the local draft; Cancel
payment also clears the selected card. Successful play clears the draft;
a rejected request retains it. The displayed ray counter follows the active player.
Reload preserves the game and payment mode but discards an unsubmitted draft;
changing phase or active player also clears that draft.

`/game/play` carries `chains`: semicolon-separated rays containing
comma-separated permanent ids; `w` is an unfiltered White ray. An empty
value is no rays, suitable for a free spell. Every chain costs one ray.
The validator computes the chain's color and Diamond output, requires the
combined output to cover the card's full light cost, and rejects reused,
missing, opposing, tapped, attached or non-payment stones, incompatible
color mixes and excess ray spending. All validation finishes before any card,
ray or stone changes. Successful spells that draw cards are acknowledged even
when the final hand size equals the initial hand size.

Explicit payment validation and stone ordering take `O(B * (S + C))` time
for battlefield size `B`, selected stones `S` and chains `C`. Temporary
validation state is released; the returned plan retains the selected ids.

The native arm is `AIGameplayTest.codex` with its `.expected`. API checks
must exercise the actual handlers; browser checks cover acknowledged orders,
freeze/continue, serialized Auto requests and manual block submission.

Run the focused grader from PowerShell:

```powershell
pwsh apps/games/codexmagic/verify-ai.ps1
pwsh apps/games/codexmagic/verify-ai.ps1 -Scope Backend
```

The default All scope runs native goldens, an in-process bundle of actual
MagicServer handlers, and a newly generated GamePage against a mock HTTP API.
It does not start the live network server or prove a full browser/backend
session. Backend scope omits HTML/browser work and reports that omission.
Windows, PowerShell, Node with built-in WebSocket, and Edge are required for
All; `-BrowserPath` accepts another Chromium executable. `-OutDir` must name
a nonexistent directory; the default creates a timestamped build-output path.
Logs, the generated page, browser profile and screenshots remain there. The
grader copies the plug source, binary/build log after a successful plug build,
and page IR/log after rendering, into that directory. A failed plug build's
detailed log remains in the shared cache. Renderer temporary files are confined
to the run directory's html-temp child.

Every compiler invocation uses depot `seed/Codex.cdx`; the grader records its
hash and rejects a seed change during the run. All rebuilds the shared HTML
plug cache under `codex/plugs/html/build-output`, so run one All grader per
workspace. The tracked game page is not rewritten. Browser cleanup closes its
owned process tree; artifacts remain for inspection. The API fixture is
`tests/ai-api.codex.inc` with its golden; the browser fixture is
`tests/ai-browser.mjs`. The API include is bundled by the grader, not compiled
as a standalone chapter. Grader allocation is bounded by fixture/artifact size;
compiler heap and time behavior is unchanged.

### Manual Cantrips at combat freezes

Root's 2026-10-01 ruling, Damian may override: exactly one manual Cantrip
can be cast at each configured combat freeze. The attacker owns the attackers
and damage freezes; the defender owns the blockers freeze. The corresponding
owner's phase-freeze setting must be enabled. A manual-block pause or timed
combat-gamble choice alone does not grant this action. Other card types remain
unavailable outside the existing main-phase controls.

The owner's hand, target orders, fluorescence state and rays govern the cast.
The shared target picker and automatic or manual gemstone-payment controls
use that owner's resources, including at the defender's blockers freeze.
Existing explicit manual-play cost rules apply; this does not add automatic
Fluorescence activation. A successful cast consumes the freeze's allowance
before returning to the same combat pause. A second cast is refused, including
after reload, order edits or Pause now. Refused targeting or payment spends
nothing and leaves the allowance. Continue advances combat; a later configured
freeze has its own allowance. A fresh turn resets the marker.

Cantrips resolve without a response chain. Lethal resolution ends the match.
Removed attackers and blocker assignments are pruned after a combat cast.
Damage skips missing combatants; a previously blocked attacker stays blocked
when its last blocker disappears.
Pause now hides and blocks immediate play and target preview, retaining the
underlying freeze and its consumed marker. New Game and cancellation replace
the old turn with a fresh marker.

State JSON adds `manual-player`, `combat-cantrip` and `combat-cantrip-used`.
`manual-hand` contains only the freeze owner's Cantrips during an eligible
combat pause. The existing `/game/targets` and `/game/play` endpoints require
`player=P` during combat; a wrong or omitted player is refused by name.
Main-phase requests retain their existing form. The page explains the one-cast
allowance and shows when the allowance is spent.

Cost: one integer per SupervisedTurn; availability and ownership add constant
work. Existing hand listing, target and payment planning provide the scans
and temporary allocations. After a combat cast, participant refresh takes
O((A+K)*B) work for A attackers, K blockers and B battlefield permanents,
plus existing list-append costs; surviving lists retain O(A+K) entries.
Damage guards add a linear presence scan before existing lookups and allocate
no query records. The marker requires no history list, board copy
or polling loop. Compiler heap/time behavior is unchanged.

### Pause now

Root's 2026-10-01 ruling, subject to Damian's override: Pause takes effect at
the next request boundary, including a client request queued while another
request is in flight. It suspends any pending timed choice and stores its
remaining time server-side. Continue resumes that same choice with the saved
remaining time; no timeout action fires during the pause. Reload preserves
the suspension. This covers spell/ability commitments, combat choices and
disruption windows.

Pause blocks gameplay actions while allowing orders, New Game and Cancel.
Continue restores a phase pause that existed before Pause now; it does not
consume that pause. If the boundary was unpaused, Continue resumes normal
stepping. Standing phase-freeze settings remain unchanged. Required proof
includes pause mid-choice, reload while paused, resume into an existing
phase pause and rejection of a gameplay action while paused.

`/api/magic/game/pause` snapshots the remaining milliseconds once in
`MagicStore.pause-remaining` and sets `pause-active`. The underlying
SupervisedTurn and its phase pause stay intact. State JSON reports
`pause-active`; `paused` is true for either kind of pause. Each pending
choice reports the saved remaining time while suspended. Repeated Pause
and reload do not shorten or restart it. Pause takes priority over expiry
dispatch; a deadline already reached or an unavailable clock saves zero.

`/api/magic/game/resume` and Step with `resume=1` release Pause now.
They keep the same choice nonce and set its deadline from the resume clock
plus saved time. An existing choice or phase pause is shown without being
consumed; otherwise normal stepping resumes. Repeated `/game/resume` is inert;
Step with `resume=1` after suspension is cleared retains its ordinary meaning
and can consume the restored phase pause. Clients retry the dedicated resume
endpoint when an acknowledgement is lost.
Zero remaining time becomes eligible for the ordinary timeout after resume;
an unavailable resume clock retains the existing fail-safe timeout policy.
Successful New Game or Cancel clears suspension; a refused New Game retains it.

The page queues Pause through an in-flight request, stops Auto and suppresses
all three choice timers until Continue. Gameplay handlers reject immediate
casts, activations and combat edits while suspended. Standing orders, card
orders and future queued-ability orders remain editable. The native simulator
does not use this server-session pause.

Cost: two scalar fields per MagicStore; fixed-size record copies when pausing
or restamping a choice, with existing lists shared. No new battlefield or hand
scan is added. Existing state serialization and ordinary stepping keep their
costs. A queued browser Pause polls the busy flag every 25 ms only until the
current request completes.

### Cleanup hand limit

Cleanup reduces the active player's hand to seven before clearing damage,
expiring temporary effects and ending Fluorescence. It never trims the opposing
hand. Automatic play retains the seven highest cards under the existing
`score-card` and effective stance; equal scores retain earlier hand entries.
The retained hand keeps its original order. Automatic discards enter the existing
graveyard in hand order; manual discards enter in request order. Both preserve
CardId, template lookup and per-copy flags.
DoNotPlay and PlayNext are play orders, not protection from required discard.
Direct `do-cleanup` uses Balanced scoring; supervised play uses the General's
effective stance. Headless play enforces the same limit without prompting.

An end-step freeze exposes a discard picker for an oversized hand. Continue
lets the AI discard any remaining excess unless the active General uses
ManualSpells. ManualSpells always waits at the end step while more than seven
cards remain; Continue cannot bypass the selections. No timeout makes a
discard choice. The player chooses one exact CardId per request and uses
Continue after the hand reaches seven. Changing back to automatic spell
policy permits the next Continue to finish the excess automatically.

`/api/magic/game/discard?card=C` requires an active, paused end step and a card
in the active player's oversized hand. Foreign IDs, repeated IDs, other
phases and hands at or below seven are refused without mutation. Pause now
blocks this action while preserving the selections already made. Reload
retains the remaining count and candidates. State JSON adds `cleanup`, null
outside the choice or `{count, manual, cards:[{id,name}]}` at that pause.

Automatic selection keeps a scratch set of at most seven IDs, then partitions
the hand once. Its time is O(H*S + G), where H is hand size, S is the cost of
the existing score function and G is graveyard size; seven comparisons per
candidate are bounded. Storage is O(H+G) for replacement zones, plus the
bounded selection set. Manual discard copies the affected zones in O(H+G)
time/storage; it preserves the previous game snapshot.

### Automatic-work budget stops

Root's 2026-10-01 ruling, subject to Damian's override: a budget stop names
the budget, its count and the last action. It is not proof of a cycle and is
never a draw. Supervised play enters the existing phase pause, with manual
actions available where legal. Continue resets the work budget and resumes
from the stopped boundary. This is separate from Pause now, which blocks
gameplay actions and suspends choice timers.

`GameState.automatic-work` counts automatic main-phase spell selections and unprompted ordinary
permanent activations. Gem drops, once-per-turn loyalty, explicit choices and
the single opposing Disruption response keep their existing separate bounds.
After 500 counted actions, the next candidate stops before
spending. The count spans phase freezes and choice round trips within the turn;
a new supervised turn starts fresh. A budget stop persists through reload,
order changes and ordinary Step. Explicit Continue clears it. Manual actions
do not spend this automatic allowance or clear its reason. A stopped main
phase exposes manual cards even under an automatic spell policy.

The existing state-based-action loop limit of 500 passes now records a
`State-based action budget` stop instead of returning silently. The last-action
label is `State-based actions`. A winning state remains a win. The automatic
spell/ability budget reports the last card/source name. No paid action is
replayed when Continue refreshes the budget. New Game and Cancel start with
empty work state. State JSON exposes `budget-stop` as the named reason or an
empty string; GamePage displays it with the Continue/manual-play explanation.

Headless `run-game` returns an interrupted state without advancing the turn.
Standalone output prints the budget reason. Simulation metrics carry a
stop-reason, aggregation does not count the interrupted game as a win or draw,
and batch output returns a named refusal. Fixed-pool runs stop and report the
reason. Existing game turn caps are unchanged.

Each GameState adds one pointer to a fixed-size work record. An automatic
action allocates one work record and one game record, sharing existing lists
and the source-name text. Counter checks are O(1); the action allowance bounds
this extra allocation until the next fresh budget. Stop-message formatting is
linear in the last-action label length. No fingerprint history or new board
scan is introduced. Interactive exact-cycle detection/batching remains planned.

### Queued General abilities

Root's 2026-10-01 ruling, Damian may override: each player has one separate
queued General loyalty activation. It captures the General template, ability
index and one legal target, or no target for an untargeted effect. Only supported
`LoyaltyCostAbility` definitions qualify; passive Gain and Scale do not.
Creation or replacement spends nothing, can occur outside the player's turn,
and may reserve an ability whose loyalty threshold is not yet met.

The order reserves that General's once-per-turn activation against automatic
use, including other abilities. It first attempts in a strictly later own
main phase, after existing phase/manual freezes have been continued. Execution
order is queued card, queued General, queued ordinary permanent ability, then
ordinary automatic actions. A pending spell response or LoyaltyGain crossing
settles before proceeding. Insufficient army loyalty or an activation already
used this turn retains the order with a visible reason. Attempts occur at most
once per own main phase. Later legality never changes the locked target.

Successful execution, explicit queue cancellation, successful manual General
activation, New Game or match cancellation clears the order. Failed manual
activation and refused New Game retain it. A replaced General, missing or
unsupported ability, or illegal original target clears at the next supervised
or server state boundary without retargeting. General activation retains its
ordinary threshold and once-per-turn rules; there is no additional loyalty
payment. Direct manual activation remains available under its existing rules.

`/game/queue-loyalty-targets?player=P&index=I` previews legal targets and the
current recommendation. `/game/queue-loyalty?choice=queue&player=P&index=I`
accepts optional `target=T`; omission captures that recommendation.
`choice=cancel&player=P` clears the slot. Invalid replacements preserve the
existing order. These order edits remain available during Pause now and other
pending decisions. State exposes `queued-loyalty` and `queue-loyalty-options`
for both players. The page shares the target picker and shows the label,
retention reason and Cancel button through reload.

Cost: one pointer per GameState copy and two fixed queue slots. Changed orders
copy that two-entry list and retain one fixed record plus label/reason text.
Reservation checks are constant work; empty queue refresh allocates nothing.
Pending refresh uses the existing target validator; affordability scans the
army for nominal loyalty. Option rendering visits both General ability lists
and retains one row/widget per supported clause plus label text. No candidate
list is retained by the queue. Compiler heap/time behavior is unchanged.

### Queued card plays

Root's 2026-10-01 ruling, subject to Damian's override: each player can queue
one exact CardId and target for a strictly later own main phase. This slot is
separate from the permanent-ability and General queues. It runs before the
General queue, then the permanent-ability queue and ordinary automatic actions.
Existing phase/manual freezes
still require Continue first. Creation/replacement spends nothing; a refused
replacement preserves the previous order. The queued copy is excluded from
ordinary automatic gem/spell selection while reserved. Manual play remains
available and clears the queue when the card leaves hand.

Queued cards include gemstones and ordinary castable spells, not Generals or
Disruptions. The explicit command overrides DoNotPlay without clearing that
persistent flag; a successful PlayNext cast consumes Next normally. Execution
uses the saved target, with no second all-rays/target prompt or retargeting.
Announcement clears the queue even if the paid spell is later disrupted.
Illegal targets clear at the next supervised/server state boundary, without
payment. Leaving hand clears immediately through set-player, including manual
play and discard. New Game, Cancel and explicit queue cancellation clear the
order. An unavailable cost retains the order with a visible reason, with at
most one execution attempt per own main phase.

Payment uses the automatic chain planner, respects kept stones and bypasses
AI reserves. Manual-payment mode retains the order with `Manual payment
required`; after changing the policy, it can execute at a later own main.
A spent gem drop retains a queued gemstone for a later legal drop. Queuing
does not auto-activate Fluorescence. Its current state determines target
legality at selection and execution. Card LightCost has no life field, so the
last-life payment guard is moot for cards; adding life-cost cards reopens it.
The permanent-ability queue retains its existing last-life safeguard.

`/game/queue-card-targets?player=P&card=C` previews legal targets and a
recommendation without spending. `/game/queue-card?choice=queue&player=P&card=C`
accepts an optional `target=T`; omission captures the current recommendation.
`choice=cancel&player=P` clears that player's order. These order edits remain
available during Pause now and other pending decisions. State JSON exposes
`queued-cards` and `queue-card-options` for the active player's hand. The page
shares the existing target picker, retains waiting reasons through reload,
and offers Cancel. A Cantrip still bypasses disruption; slow spells retain
their existing single response window.

Cost: one pointer per GameState plus two fixed queue slots and order label
text. Reservation queries are O(1). Player updates with a queued card check
its hand membership in O(H); empty slots skip the scan. Target refresh checks
at most two queues and uses existing target-validation scratch. A due attempt
adds existing payment-planner work; serializing an already-attempted pending
spell also recomputes that payment reason, with planner scratch restored.
Option JSON is O(H) rows, plus label text;
no candidate list is retained in a queued order.

## LoyaltyGain crossings

Root's 2026-10-01 rulings, subject to Damian's override, define
`LoyaltyGainAbility threshold effect` on a General as one trigger per upward
crossing of a positive army-loyalty threshold. Starting observation is zero.
Staying at or above the threshold does not fire again; falling below rearms
it. Both players are observed. Triggers do not spend the General's one loyalty
activation. Ordinary spell casting does not execute this clause.

Observation occurs after state-based actions settle. An open slow-spell
response window defers observation until the original paid spell resolves or
fizzles. Simultaneous crossings enqueue the active player's triggers before
the opponent's, in ability-index order for each. Events capture their effect
and owner. Resolution follows that owner's current target orders; an effect
with no legal required target does nothing. Triggered creature boosts and
keyword grants persist through cleanup, following the existing trigger rule.
Game end stops further trigger resolution.

GameState carries the two observed totals and a pending-event list with a
cursor. Constructors preserve that state; New Game/cancel reset it, while a
refused New Game preserves it. Polling does not recapture crossings. A server
action's captured events settle before its response is emitted; Pause holds
pending events until resume. Native supervised play settles them between
automatic actions, including between casts in one main phase.

Each resolved event consumes the existing automatic-work budget and names
its player and ability index as the last action. Hitting the limit preserves
the remaining queue and enters the named budget stop; Continue resets that
budget and resumes the queue. Trigger effects can cause further settled
crossings. This is a budget stop, not proof of a cycle or a draw.

The shipped General templates are unchanged; fixtures exercise the new form.
This unit adds no browser control or response option for loyalty triggers.
No-Gain Generals take an allocation-free ability scan. Watched checkpoints
scan both armies and General ability lists; queued events add one record each
plus list-append copying. Settlement advances a cursor, avoiding a copied
tail per event, and drops the queue when drained. Target-query and effect costs
remain those of the existing effect engine. Each GameState adds one pointer.
Compiler heap/time behavior is unchanged.

## LoyaltyScale creature aura

Root's 2026-10-01 ruling, subject to Damian's override, scopes
`LoyaltyScaleAbility divisor power-unit toughness-unit defense-unit` to a
continuous friendly-creature aura. Each component contributes its unit value
times `floor(army-loyalty / divisor)`. Multiple clauses add. Divisors must be
positive; nonpositive clauses contribute zero without division. General and
noncreature permanents are excluded. Tokens receive the bonus but do not
contribute army loyalty; these exclusions and token behavior are explicit
root 2026-10-01 rulings, recorded in [Cards](../Done/Cards.md).

The aura follows current control and current army size. It is read with the
effective stats, never written into permanent or temporary modifiers, and
spends no loyalty activation. Combat damage, toughness-based state checks,
AI attack/block evaluations, target threat scores and battlefield JSON use
the same bonuses. General stats retain their existing calculation. Polling
and cleanup cannot accumulate an aura grant. Shipped General templates are
unchanged; fixtures exercise the new form. Scaling other effect types remains
separate design work.

Ordinary activations and ordinary ability responses settle full state-based
actions after paying costs, before their effect uses its saved target
(root 2026-10-01, Damian may override). Sacrificing one creature can reduce
the aura and kill another. A target lost at that checkpoint receives no grant;
the response result marks it fizzled, with costs spent. No retargeting occurs.

`ScaleBonuses` captures both players' PTD totals once per evaluation pass.
AI planning, target ranking, combat damage and JSON loops pass that cache
through their readers. State-based checks recompute it after a removal, since
that can reduce another creature's toughness. A cache is never stored in game
state or reused after a board change. Scalar query wrappers restore their
temporary cache; cached stat reads allocate no per-creature enum values.
Legacy list-only helpers have no opposing General context; live game-state
entry points use the cached variants. The uncited `find-biggest-threat`
raw-list utility remains outside those live paths.

Cache setup is O(B + A) for B battlefield entries and A General ability slots;
without a valid scale clause it skips army scans. Each stat read is then O(1).
The existing attacker/blocker algorithms retain their own nested work. Cache
storage is fixed-size per pass; no per-creature aura object is retained.
The post-cost checkpoint adds the existing state-based-action pipeline to
ordinary activation costs; the measurements below cover board queries, not
that mutation checkpoint.
Compiler heap/time behavior is unchanged.

### Measured evaluation cost

Measured 2026-10-01 on kernel `66AE634E870CA342`, using the same native VM
and HPET clock. Each sample times 2000 combined attack-plan, AutoBlock and
damage-target-score evaluations after warmup. Boards contain four or 32
identical creatures, split equally by controller; the defending General is
tapped. The table preserves all three samples in run order, in microseconds.

| Case | 4 creatures, microseconds | 32 creatures, microseconds | Maximum end delta per evaluation, bytes (4 / 32 creatures) |
|---|---|---|---|
| Before aura implementation | 1143, 1153, 1167 | 27570, 28682, 27995 | 736 / 6032 |
| After, no aura | 1426, 1584, 1445 | 32915, 32331, 36546 | 760 / 6056 |
| After, both Generals have the aura | 2049, 2644, 2099 | 33597, 34506, 34859 | 904 / 6200 |
| After, equivalent explicit stat modifiers | 1394, 1574, 1441 | 32096, 31133, 30345 | 760 / 6056 |

The active aura is `LoyaltyScaleAbility 5 1 1 0` on both Generals. The no-aura
control matches the prior checksum; the aura matches a fixture with equivalent
explicit creature modifiers. The retained-heap increase is fixed across these
sizes, rather than per stat read. Heap delta is the end-of-evaluation pointer
difference, not high-water; internal scratch restores can hide transient peaks.
Elapsed samples include checksum traversal and per-evaluation heap restoration.
This fixture does not establish whole-turn performance or asymptotic scaling.
Raw sources, binaries, samples and checks are under
`build-output/val-scale-bench/`; `landing/comparison.verdict` owns the final
comparison. Separate gameplay arms establish behavior.

## Remaining design work

The sections below describe the wider design. The current Posture contains
stance, primary target orders, all eight phase freezes, automatic spell
policies, manual main-phase card decisions, Obsidian activation and all four block policies.
Card-ranking skill profiles use the contract below; broader strategic
evaluation remains open. Counter, sacrifice and resource-commitment escalation
use the runtime contracts in this document. Unpaid commitment override uses
the contract below. Broader queued actions, arbitrary action
overrides, other priority windows and player-directed
cycle bailouts also remain open.
LoyaltyGain and the LoyaltyScale PTD aura use the contracts above; other
scaled effect forms remain open.
Loyalty is a threshold, not a permanently spent resource
(Cards.md, Army Loyalty).
All Effect constructors have direct handlers; permanent-entry Fluorescence
and useful automatic activation for a discard bonus are implemented.

## Intent

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

See [Cards.md](../Done/Cards.md) for the full General card type definition,
including `GeneralTemplate`, `BehavioralModifier`, and `CombatBias`.

## Posture

Posture is the player's standing orders to the AI. It defines the
general approach without specifying individual moves.

```
Posture = record {
  stance : Stance,
  primary-targets : List TargetOrder,
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

The General's `StanceOverride` applies when the player selects Balanced.
An explicit Aggressive, Defensive, Tempo or Control posture wins over
the General's stance override and low-life aggression threshold.

### Primary Targets

The player marks specific permanents or the opponent as priority targets.
Supported targeted spells follow those orders, and automatic blockers address
priority attackers first. Ordinary attackers still attack the enemy General.

```
TargetOrder =
  | PriorityGeneral
  | PriorityPermanent (Integer)
  | PriorityType (CardType)
  | PriorityThreat (Integer)  -- 0 high, 1 medium, 2 low
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
- **Every-phase review** -- freeze at every phase; Continue still delegates
  the phase's actions to the AI, except manually assigned blocks and
  main phases governed by ManualSpells or manual payment.

### Mana Policy

Automatic payment uses the shared prismatic planner. An empty
`preserved-stones` mask permits every eligible family; a nonempty mask keeps
the selected families out of automatic payments. Ray conservation is a
separate spell policy. The `manual-payment` flag implements ManualTap at the
two main-phase pauses with explicit card and chain choices.

### Block Policy

How the AI assigns blockers:

```
BlockPolicy =
  | AutoBlock     -- AI assigns legal blocks with its existing heuristic
  | NoBlock       -- no automatic assignments; manual assignments remain
  | ProtectLife (threshold : Integer) -- add creature blocks when forecast life is below threshold
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

Implemented ruling (root 2026-10-01, Damian may override): automatic target
choices and combat-gamble pauses are opt-in and off by default. Combat risk
means an untapped opposing creature and available rays. Its untouched plan
continues after 15 seconds; editing the draft cancels the timer and requires
Continue. The runtime contract and proof cover both outcomes.

Resource-commitment implementation ruling (root 2026-10-01, Damian may
override): pause before an automatic spell spends all unreserved rays. The
pause is on by default, with a toggle in General settings. Offer Cast and Hold;
after 15 seconds resolve to Hold, never Cast. Hold does not make that pending
payment; it skips the card for the current main phase while cheaper alternatives
may continue. The generic timeout recommendation below does not override this
implemented commitment policy.

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
- **Pause now**: implemented by the server-session pause contract above.
- **Override** -- cancel the AI's planned action and choose manually
- **Mark a card**: implemented by the per-copy runtime contract above.

### Unpaid commitment override

Root's 2026-10-01 ruling, subject to Damian's override, permits manual takeover
of a pending unpaid spell or ordinary-ability commitment. `/game/override?nonce=N`
requires the current nonce and a live main-phase commitment. It clears that
decision, spends nothing, and stays in the same main phase with manual controls
available. The skipped card joins the phase's held cards; a skipped ability
remains used for automatic scheduling that phase. Either can still be chosen
manually under the existing legality and payment rules. General settings do
not change.

State JSON exposes `manual-override`. Reload, order edits and Pause now preserve
it. Resuming Pause now restores this manual phase pause; only an explicit
Continue releases the override and resumes automatic play under the current
General policy. Normal manual plays and their response windows return to the
manual pause. New Game, cancel and phase completion clear it. A rejected New
Game leaves the existing game intact.

Stale nonces are refused. An expired commitment follows its existing timeout
policy, including Hold for an all-rays spend or an ability; expiry does not
open manual override. Pause now blocks override as a gameplay action. A paid
response window is refused with `cannot override a paid response window`:
override never refunds or rewinds an announced spell. Combat drafts keep their
existing editing controls. The UI labels the new button `Override` beside
Cast/Activate and Hold; Continue explains that automatic play resumes.

One Boolean is added to each SupervisedTurn. Override copies a held-card or
used-ability list, linear in that list's length; other added work and storage
are fixed-size. Existing manual target/payment costs are unchanged. No seed
or compiler behavior changes.

Broader intervention remains design work. Card flags affect future automatic
selection; neither flags nor override interrupt an in-flight server request.

## Skill Levels

### Locked ladder holdout

Root confirmed the following metric on 2026-10-01 and required the seed set
and size to land before tuning. This record is the lock. No skill heuristic
has changed for this tuning unit, and these holdout outcomes have not been
inspected. Do not replace seeds or enlarge the holdout after seeing results.

Compare adjacent profiles head to head: Intermediate versus Beginner, Expert
versus Intermediate, and Master versus Expert. Keep the named profiles and
all non-skill policies fixed. Use the existing full card pool, Warlord in
seat 0, Dawn Commander in seat 1, existing starter generation and simulator
rules. For rule index c=0 use baseline, c=1 proposed. For every integer
i=0..9999, use seed `90000000 + 97*i + c`. Starter seeds remain seed+1 and
seed+2. For each rule/seed, run two games, swapping only the skill profiles
between seats. The two rule sets have distinct residues modulo 97.
Root approved the shuffle correction before this lock: call the same
`Deck.shuffle-deck` function as live New Game, with seed+1 for Warlord's
generated deck and seed+2 for Dawn's, before loading libraries and drawing
seven cards. Each paired game receives the same two shuffled decks.
Non-skill setup follows `SkillComparison.skill-outcome` at MAIN34113 except
for this explicit shuffle and replacing its fixed Expert opponent with the
specified adjacent profile. Pool, General, generation and rule definitions
remain those of MAIN34113; do not tune them to change the ladder. Record the
frozen candidate CL and compiler hash with the holdout results.

Each comparison contains 20,000 matched seed pairs and 40,000 games; all
three contain 120,000 games. The last seed is 90,969,904. Counts and endpoints
were checked by the native Codex program under
`build-output/val-skill-plan-check.*` in DEV on kernel 0FD86AE43ACF3693.
No game in this holdout is a development or tuning game. Development uses
seeds below 10,000,000. Freeze candidate code before executing the holdout.
Report a failed holdout as failed; do not change its membership or relabel
profiles to obtain a pass. Any further tuning after inspecting it must be
identified as reuse, not represented as a fresh holdout.

For the higher profile, score each game as win=1, draw=0.5, loss=0. Average
the two games within each matched pair. The pair, not the game, is the
uncertainty unit. Pool the balanced rule/seed pairs for each comparison and
compute the mean score and sample standard error across pairs. Acceptance
requires mean score strictly above `0.5 + 2*SE` for every adjacent comparison,
with all 20,000 pairs and 40,000 games completed, and zero stopped or refused
games. Report attempted/completed game and pair
counts, W/L/D, mean score and SE for each comparison on this holdout only.
The seed-pair standard error describes this fixed simulator protocol; it is
not evidence about untested Generals, pools or human play.

An exact integer acceptance check avoids rounding the boundary. Give each
game 2 points for a win, 1 for a draw and 0 for a loss. For each pair let
D be its total points minus 2. With n completed pairs, S=sum(D), Q=sum(D*D),
the mean score is `0.5 + S/(4*n)` and
`SE = sqrt((n*Q-S*S)/(n*n*(n-1))) / 4`.
Require n>=2, S>0 and `S*S*(n+3) > 4*n*Q`, as well as complete admission.
Printed rounded scores/SE do not determine the verdict. The specified sizes
keep these products within signed 64-bit Integer range.

Instruction-only lock: compiler heap/time behavior is unchanged.

### Current profiles and prior measurement

Root's 2026-10-01 ruling, subject to Damian's override, scopes this unit to
automatic spell ordering and cleanup retention. `Posture.skill` uses these
wire values; the default is Expert and preserves the previous scoring paths.

| Value | Profile | Card score |
|---|---|---|
| 0 | Beginner | Equal scores, preserving hand order |
| 1 | Intermediate | Existing base score with Balanced stance |
| 2 | Expert | Existing score with effective stance |
| 3 | Master | Expert score plus current-board immediate-effect value |

Casting adds the General's spell-type priority except for Beginner. Cleanup
uses the card score without that extra priority, preserving Expert's previous
retention behavior. PlayNext precedes score during casting; DoNotPlay, queued
reservations, legality, usefulness, payment and safety checks retain their
existing rules. Skill changes affect future selections, not a saved commitment
or target. Combat planning, counter selection and automatic ability scheduling
retain their existing policies.

Master values immediate effects: 1000 points for legal lethal General damage
or life loss, otherwise 12 per point; healing is 12 per point below ten life
and 3 otherwise. Safe card draw is 20 per card, and discard is 15 per available
opposing card. Removal uses max(0, 20 plus the best candidate score) for that
individual effect; combined spell restrictions remain cast-time checks.
Friendly grants require a positive target valuation: stat boosts use 10/5/8
per positive power/toughness/defense and a keyword grant uses 20.
Only currently active Fluorescence receives
effect value; ordinary permanent static/activated abilities are not treated
as immediate spell effects. These are current-board ranking heuristics.

`/game/skill?player=P&level=L` accepts players 0/1 and levels 0..3, including
before New Game. Invalid input preserves the previous orders. Other order
edits and successful New Game preserve skill. State JSON adds `skill` to each
posture. GamePage labels the selector `Card choices` and waits for server
acknowledgement. Pending choices keep their saved nonce, deadline and card.

The MAIN33929 measurement used gemstone-first, unshuffled starter order and
does not describe shuffled play. `Deck.gen-starter-deck` concatenates 24
gemstones before spells; the old comparison loaded that order directly.
The native census under `build-output/val-skill-input-census.*` confirms both
initial hands contain seven gemstones before rule-specific mulligans.
Live New Game shuffles. The locked paired benchmark above uses the approved
seed-based shuffle correction; the legacy simulator output remains unchanged.

That prior measurement ran 2026-10-01 on kernel CC3FC5222D726096 with `SkillComparison.codex`:
each profile faced Expert over the SimRunner corpus, both seats, both rule
sets, 100 seeds per rule/seat. Warlord and Dawn Commander retained their seats;
only skill assignments swapped. Seeds are 12345 + i*97 for baseline and
32345 + i*97 for proposed, i=0..99. Expert self-play reproduced the existing
simulator output. There were no stopped or refused runs.

| Profile | Runs | Wins | Losses | Draws | Wins / completed runs |
|---|---:|---:|---:|---:|---:|
| Beginner | 400 | 34 | 49 | 317 | 8.50% |
| Intermediate | 400 | 40 | 41 | 319 | 10.00% |
| Expert | 400 | 41 | 41 | 318 | 10.25% |
| Master | 400 | 41 | 42 | 317 | 10.25% |

The ladder is not strictly ordered: Master tied Expert on wins and lost one
more game. Names remain unchanged. Draws include the simulator's turn-limit
adjudication; completed runs include those draws. Budget stops and generator
refusals have separate columns and are excluded from the win-rate denominator.
The tool prints integer basis points, with -1 when no run completed. Evidence:
`build-output/val-skill-measure/comparison.csv` and its verdict. Results measure
these ranking profiles with the other policies held fixed.

Ranking retains its existing hand scans. Master adds per-ability target
queries and restores the whole valuation scratch before returning a scalar.
Cleanup still uses a bounded seven-card selection set and replacement zones.
Each Posture adds one scalar. The comparison restores each simulated game's
heap and retains only integer outcomes/tallies; retained tally storage is
linear in attempted runs, while peak game storage is one game.

Broader tactical/strategic evaluation remains design work. Planned ranked
play requires Master for both players; enforcement is open in games-backlog
because there is no live ranked/casual selector.

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

## Disruption and Cycle Handling

The runtime disruption contract and remaining cycle work follow:

**Disruption window:** [ManaRedesign](../Done/ManaRedesign.md), Spells,
supersedes the historical spell stack. There is one response window for a
disruptable spell and at most one opposing response: a Disruption, a manual
Cantrip or a manual ordinary activated ability, with no chaining. Creature,
Incantation and Enchantment
casts use the window. A Cantrip itself never opens a response window.
The caster pays and removes the exact CardId from hand before
the window; the spell's effects and permanent entry wait. PlayNext is consumed
when the spell is announced, including a spell later disrupted. The paid
spell is held in `GameState.disruption.window`, with targets, caster, hardness
and whether manual casting must return to the existing main-phase pause.

If no affordable opposing Disruption, legally targetable Cantrip or usable
ordinary activated ability exists, the spell resolves directly. A Cantrip-only
or ability-only opportunity with response choice
off is passed automatically; automatic replies remain Disruption-only.
Otherwise the responder receives priority. Pass spends no opposing resources;
Disrupt revalidates card membership and payment, pays once and moves the reply
to its caster's graveyard. A successful contest sends the pending spell to its
caster's graveyard without effects. A failed contest resolves the spell.
An invalid original target fizzles a nonpermanent spell without retargeting.
There is no refund and neither reply opens another window. Priority then returns to
the active player. Manual casting returns to the same paused main phase;
automatic casting continues until another decision or configured freeze.

`Stack.contest-disruption` now takes caster focus/hardness, opposing
disruption power/hardness, seed and contest index. The pure helper uses
Random.mix-bits, masks the sign bit and maps the value to 1..100. Success
means the roll is at most the clamped disruption chance from ManaRedesign.
`contest-disruption-roll` accepts an explicit roll for exhaustive grading and
rejects values outside 1..100. The seed/index path is repeatable, not a source
of live entropy. Time and retained heap are O(1); compiler behavior is unchanged.

`GameState.disruption` owns the match seed and contest index. New games default
to seed 0; `/game/new?seed=N` accepts a canonical nonnegative decimal contest seed
of at most nine digits. The seed affects
contests only. Simulate and SimBaseline's seeded match runner use their game
seed. A valid paid Disruption consumes exactly one index; Cantrips, ordinary abilities, Pass,
refusal and reload consume none. The result records the actual chance, roll, reply and
whether the spell was disrupted or lost its target. New Game and cancel clear
the window and contest history; refused creation preserves both.

The root ruling of 2026-10-01, subject to Damian's override, makes response
choices opt-in and off by default. Enabled choices offer affordable
Disruptions, legally targetable Cantrips, usable ordinary abilities or Pass,
with a 15-second timeout to Pass.
The General setting is labelled `Response choice`; the stored field and API
remain `disruption-pause` and `/game/disruption-policy`. With the setting off,
including headless play, the AI considers replies with chance at least 50%.
PlayNext ranks first, then highest chance, then fewest rays; remaining ties
follow hand order. DoNotPlay is excluded from automatic replies. The actual
outcome uses the seeded percentile roll rather than that eligibility threshold.

The same ruling defines three policy interactions. First, an enabled choice
can select a DoNotPlay card as an explicit manual override; the hold remains
on that CardId. Second, all response payment uses the automatic planner with
the responder's kept-stone mask, including a prompted reply under manual
payment mode. With response choice off, ManualSpells or manual payment
instead means Pass. Third, automatic replies can spend the HoldCounters
reserve but preserve ConserveMana and General ResourceHoarding reserves.
A prompted reply uses available rays without those AI reserves.

`/game/disruption-policy?player=P&enabled=E` controls the setting.
`/game/disruption?nonce=N&choice=disrupt&card=C` selects an offered CardId;
`choice=pass` or `choice=timeout` passes. The window has a fresh nonce and
authoritative 15000-millisecond deadline. Reload, Step and Continue retain
that decision; settings edits do not release it. Stale nonces, unoffered cards,
malformed choices and unavailable payment spend nothing and retain Pass.
Early timeout requests preserve the window. At expiry, or without a clock,
even a late Disruption, Cantrip or ordinary ability becomes Pass. New Game and
cancel take precedence over expiry and abandon the old paid spell with the
match. A late unrelated game request receives the expiry result and must be
retried for its requested action. Immediate manual game actions are unavailable
during the window except the selected response. Future queue edits remain available.

`/game/response-targets?nonce=N&card=C` previews a Cantrip's legal targets and
recommendation through the shared picker, without spending or resetting the
deadline. `/game/disruption?nonce=N&choice=cantrip&card=C&target=T` submits it;
omission of target uses the current recommendation, or no target for an
untargeted effect. Payment and hand membership are revalidated first. After
payment the target is rechecked; an illegal resolution target fizzles the
Cantrip, consumes it and the one response, and refunds nothing. Otherwise its
effects resolve immediately. The original nonpermanent spell then rechecks
its saved target and resolves or fizzles normally, without retargeting.

Root's game-end ruling on 2026-10-01, subject to Damian's override: if the
Cantrip ends the game, nothing pending resolves. The original paid card moves
to its caster's graveyard without effects and is reported as abandoned at
game end. `ContestResult.reply-fizzled` distinguishes a failed Cantrip target
from `fizzled`, which describes the original spell; `abandoned` marks game end.
Result JSON adds these flags and `response-kind`. Cantrip results use chance
and roll zero. Internal response completion preserves any budget-stop reason;
it does not substitute for the player's Continue.

GamePage names the pending spell and responding General, displays each offered
card's ray cost and a contest chance for Disruptions, and sends the selected
CardId with the nonce. Cantrips open the shared target picker. Expiry clears
an open response picker, including when the original phase remains paused.
The reusable timer follows the server's remaining time after reload. Auto and
Continue are hidden during the choice. The result label reports Pass or the
actual contest chance/roll, paid Cantrip fizzle or game-end abandonment.
No independent server loop advances an idle
browser match; the next request enforces expiry.

For H opposing hand entries and payment-planning cost P, quoting or automatic
selection takes O(H squared + H*P) time; manual Cantrip offerings additionally
use existing target-validation work per candidate. There is O(H) candidate storage plus existing
planner scratch. Automatic candidate queries release their scratch; a prompted
window retains its O(H) options and original targets. Each game-state copy adds
one pointer; seed, index and result headers are fixed size. API chunks and page
controls are O(H). A hand with neither response type takes only a linear scan
before ordinary resolution. Cantrip-only hands can add a transient window and
payment/target query even with the prompt off; that path immediately Passes.
The two result flags add fixed-size storage. Compiler heap/time behavior is unchanged.

### Ordinary ability response

Root's 2026-10-01 ruling, subject to Damian's override, adds a manual ordinary
`ActivatedAbility` to the same single response slot. General loyalty abilities
are excluded. Response choice stays opt-in; automatic replies still consider
only Disruptions. Timeout is Pass. Selecting an ability consumes the window,
so no Disruption, Cantrip or second ability can follow.

Only the responder's controlled permanent and exact ability index qualify.
Existing cost, summoning-sickness, supported-effect and target guards apply.
This window is a narrow timing exception; ordinary main-phase activation
still requires the active player. Rays are paid directly, as for existing
ordinary abilities; tap, sacrifice and life costs also apply. Manual payment
to exactly zero life is legal and ends the game before the ability effect or
pending spell resolves. The original paid spell goes to its caster's graveyard.

`/game/response-ability-targets?nonce=N&permanent=P&index=I` previews the shared
target picker. `/game/response-ability` takes those identifiers and optional
`target`; omission uses the current target-order recommendation. The server
rechecks membership, ownership and payment. It then pays costs and rechecks
the selected target without retargeting. A target made illegal by sacrifice
fizzles the response with costs spent. The original spell rechecks its own
target afterward. Preview does not restart the deadline. Pause suspends the
same window and blocks execution; stale or repeated responses are refused.

Response option JSON adds `permanent`, `index`, `cost` and `kind: ability`.
Its card ID is -1; source and ability index identify it. Result JSON adds
`reply-source`, `reply-ability` and `response-kind: ability`; chance/roll are
zero and the contest sequence does not advance. Fizzle and game-ending
response flags retain their existing meanings.

The preliminary response scan now checks opposing ordinary ability slots.
Building manual options visits battlefield ability slots; each readiness
check can scan the battlefield and legal targets, with temporary query heap
restored. Retained options are linear in eligible responses, plus the existing
list-append allocation cost. Each option/result adds two integer fields.
No automatic ability reply is added. Compiler heap/time behavior is unchanged.

### Response to an ordinary activation

Root's 2026-10-01 ruling, Damian may override: ordinary activated abilities
offer the opponent one opt-in Cantrip or ordinary-ability response, or Pass.
Disruption is refused by name because an activation is not a slow spell.
Responses do not open another response window. Passive triggers and Cantrip
casts retain their existing timing; General loyalty follows the next contract.

The source, ability index, effect, label and selected targets are captured
before payment. Rays, life, tap and sacrifice costs are then paid and
state-based actions settle before any opponent choice appears. A player who
dies paying costs loses before the ability effect or response window.
The paid ability survives the source's sacrifice or later removal. A selected
target is rechecked at resolution; an invalid target fizzles the ability
without retargeting or refund. A game-ending response abandons the pending
ability. No synthetic card is added to a graveyard for an ability.

The existing Response choice setting controls the window, default off. Off,
or no legal payable response, resolves the ability without an automatic reply.
On, the same shared picker, nonce and 15-second deadline apply. Timeout is Pass.
Pause stores the remaining time; reload retains the suspension and Continue
resumes the saved deadline. A reply or Pass consumes the one window. Manual
activation returns to its existing main-phase pause; automatic and queued
activation continue under the existing General orders. The automatic phase
allowance and queued-order consumption precede the response, preventing reuse.

The existing response endpoints are shared. State and result JSON add
`pending-kind` (`ability`, `spell` or `loyalty`), `source` and `index`. The pending choice
uses `card=-1` for an ability; result JSON omits `card`. The retained label identifies a source even after sacrifice. The page
states that activation costs are paid and lists Cantrip, ordinary ability and
Pass rather than offering Disruption. Ability windows consume no contest roll
or sequence index. Cancellation and successful New Game drop the pending
ability with the match; refused New Game preserves it.

Cost: each ordinary activation captures a fixed record and label text, even
when no window remains visible. Window and result each add one record pointer.
Admission and prompted options reuse existing hand, payment and battlefield
ability scans, including their scratch restoration and list-append costs.
The pending record retains the effect and target list, not a copied board.
No extra simulation or polling loop is introduced. Compiler heap/time
behavior is unchanged.

### Response to General loyalty

Root's 2026-10-01 ruling, Damian may override: a General loyalty activation
offers the same single opt-in Cantrip or ordinary-ability response, or Pass.
Disruption is refused by name. A General loyalty ability is not offered as a
reply. No response chain is added; passive LoyaltyGain and LoyaltyScale retain
their existing semantics.

Before the window, the General's own-turn activation is marked used and any
queued loyalty order is cleared. The current army loyalty threshold is checked
at activation; no loyalty or rays are spent. The captured effect and target
then wait for the response. Losing army loyalty after announcement does not
undo the activation. A lost target fizzles without retargeting, and a reply
that ends the game abandons the effect. Neither outcome restores activation
use or the queued order. Unpaid Override cannot cancel this committed window.

The existing Response choice setting is default off. Off or no payable reply
means immediate Pass; on, timeout is Pass after 15 seconds. Pause suspends the
deadline and reload preserves the used marker and cleared queue. Manual use
returns to its main-phase pause. Automatic and queued use resume ordinary
actions only after the General window settles; later queued abilities cannot
run during that response.

The existing response endpoints and target picker are shared. Pending and
result JSON use `pending-kind: loyalty`; `source` identifies the General
template, and `index` its ability. The pending card is -1. The page calls the
activation committed for the turn, rather than claiming resource payment.
Both manual activation handlers prepare offered choices, or resolve the
default-off Pass, before returning a stored window to the client.

Cost: one Boolean in the existing pending-activation record, plus a captured
record/label per accepted General activation. Response admission, payment and
target scans reuse the ordinary activation path and its scratch restoration.
Reservation and used-turn checks remain constant work; no board copy or new
timer is introduced. Compiler heap/time behavior is unchanged.

The automatic-work budget-stop contract above is implemented. The wider
interactive cycle design below remains planned; the uncited CycleDetect
count-only fingerprint does not establish exact repeated state.

**Bailout cycles:** When infinite loops include player decision
points ([CycleDetection.md](../Done/CycleDetection.md)), the AI handles the
prompt. In single-player bailouts, the AI recommends an iteration
count based on board state. In contested cycles (both players have
decision nodes), the AI plays each player's side according to their
posture -- aggressive stance continues favorable cycles, defensive
stance bails early. Players can always override the AI's cycle
decisions.
