# Native tavern backgammon

`TavernBackgammon` cites `Games.Backgammon`; it does not change that engine.
The engine decides legality, maximum dice use, bar entry, bearing off,
doubling, winners and single/gammon/backgammon points. This adapter owns
seats, server dice, bounded table history and settlement through the existing
`EuCurrency`. It adds no purse, mint, balance copy or alternative game rules.

## Binding and consent

`tb-new currency challenger invitee metal stake serverSeed verifiedChallenge`
constructs a pending table. Both seats carry immutable character identity,
purse id, NPC classification, persona skill, drink, appetite and wager limit.
Identity and purse namespaces differ: the verified binding must establish
that each character controls the stated purse. Native admission checks purse
existence and player/NPC kind, not that external mapping. Treasury, business
and town purses cannot occupy a seat. Identities and purses must differ.
The adapter copies patron fields, so changing an input record cannot rewrite
an admitted seat. The currency reference is retained, never copied.

The trusted server chooses the table location and admits current presence,
life, session identity, ownership and the challenger's consent to the stake
and scoring multipliers. No client supplies a verified flag or a seed.
The caller also prevents a purse from occupying conflicting active tables.
`tb-accept` requires the invitee's authenticated choice. An NPC additionally
requires an affirmative mind decision and the appetite check. A decline ends
that offer and moves no coin. A fresh challenge creates a fresh table.

Stake is 1..1000000 coins of one selected denomination: copper 1, silver 2
or gold 3. It cannot exceed either patron's preconfigured limit. Each party
agrees to `stake * enginePoints`, including gammon/backgammon multipliers.
Acceptance and each cube increase require both purses to cover the maximum
three-times-cube exposure. Cube offers/takes still follow the engine's owner
and turn rules; an unaffordable increase is refused rather than granted credit.

## Dice, moves and NPC play

State is offered 0, awaiting roll/cube choice 1, playing dice 2,
result awaiting payment 3, settled 4 or declined 5. Only the current seated
principal can roll, move, pass or offer a double. The opponent answers a
pending double. `tb-decline` lets either a human or NPC invitee explicitly
close a challenge without requiring funds or moving coin. Opening rolls assign the higher die's side; equal opening
dice are announced but rerolled. Later doubles give four uses of the die.

`tb-roll` generates both dice on the server. The native seeded generator
mixes high bits before range reduction, avoiding the raw generator's
alternating-low-bit defect. Rejection removes modulo-six bias; a bounded
rejection failure refuses the roll after consuming random state. This is a
deterministic simulation generator, not production entropy. The image must
provide an unpredictable server-owned source before public wagering and
persist its state with the table. Never expose the seed through a client view.

`tb-move` asks `bg-legal` and applies `bg-play`; a refused move leaves board,
remaining dice and history unchanged. The client adapter must restore a
dragged checker from this authoritative position. Turns advance only when no
remaining die can be played. `tb-pass` refuses while any legal move remains.
The persistent board and four dice slots are copied in place from transient
engine results before scratch restoration; no new board graph is retained
per move. The owner can announce the recorded roll and current player, then
publish legal moves from the same engine and remaining dice.

Persona skill, drink and appetite are integers from 0 through 1000. NPC
acceptance uses a server choice below `appetite + drink/2`, with the wager
limit still enforced and the mind retaining its veto. These are simulation
policy defaults. Effective play strength is `max(0, skill - drink)`: a server
choice below strength uses `bg-ai-pick`; otherwise the NPC selects the first
engine-legal move. Both branches obey the engine's complete dice-use rules.
NPC cube decisions use `bg-ai-double` and `bg-ai-take`, within purse exposure.
An NPC drops a pending offer when taking it has become unaffordable.

`tb-drink` requires a verified served drink and increases drink by 200,
saturating at 1000. `tb-hour` advances monotonically and removes 25 per game
hour, never below zero. The live layer-1 adapter must bind those decisions to
actual beverage consumption and the shared clock. Persona data is trusted
server configuration; it is not a model-authored skill override.

## Wagers and transaction boundary

`tb-settle` uses the engine's winner and points, transferring the gross wager
from loser to winner with `eu-pay`. Existing tax rules apply: the loser pays
gross, the winner receives net and the treasury receives the tax. Rates do
not convert the nominated denomination. The table stores the coordinator
event id and gross amount on success; a settled table cannot pay again.

No escrow or funds reservation is implemented. A live owner must reserve the
agreed exposure through its authoritative transaction/admission system or
handle a pending result when other activity spends the funds. Insufficient
funds, recipient capacity, ledger space or coordinator state leaves the
finished result pending. Retrying does not reroll or replay the game, and no
partial transfer is admitted by `eu-pay`.

The owner serializes access and retains the table/currency below operation
scratch. These functions mutate an in-memory candidate. Commit board, random
state, consent, table history, payment event and all money rows together under
[Database.md](Database.md) before acknowledgement. Use a private candidate or
complete undo; the component supplies no durable rollback. One failed
multi-call operation does not undo earlier successful calls.

Table history has 2048 fixed 48-byte rows: sequence, operation, principal,
and three integer arguments. Operations are accept 1, roll 2, move 3, pass 4,
cube offer 5, take 6, drop 7, settlement 8, decline 9, drink 10 and time 11.
Roll arguments are the two announced dice and acting side; move arguments
are source point, die and side; settlement arguments are gross, denomination
and coordinator event id. Rows through `event-count` alone are initialized
and readable. Full history refuses further mutations; it never drops old rows.
The DB owner must retain/drain the history atomically before resuming play.

## Cost, verification and remaining integration

Each table retains one engine board, four dice, two copied patrons, scalar
state and a fixed 98304-byte history buffer. The owner must budget the number
of simultaneously admitted tables before enabling a live venue. Selection
search uses the existing engine's at-most-four-dice search, whose recursive
board copies are reclaimed; the adapter reclaims its enclosing operation
scratch as well. Weaker selection checks at most four dice and 25 sources.
Settlement is constant work in the currency coordinator. A complete game
retains no request-heap growth.

`proofs/TavernBackgammonProof.codex` plays a complete scripted-human versus
NPC game, compares accepted moves against the cited engine, validates each
server roll, checks purse/tax conservation and rejects repeated settlement.
It also grades actual sober refusal versus drunk acceptance with the same
server draw, drink decay and weaker strength, cube drop, payment retry,
empty-purse human decline and an NPC dropping an unaffordable take.
Normal and poisoned runs must print `UOAIX TAVERN failures=0`.

The client board/window, drag restoration, announcements, live persona/mind
and drink bindings, venue/session admission, secure entropy, funds reservation
and Codex DB restart recovery remain explicit integration gaps. No GameNet
or backgammon engine edits, client session grade or public-wager readiness
are claimed by this native component.
