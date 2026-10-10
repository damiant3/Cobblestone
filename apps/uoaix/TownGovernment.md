# Town offices and public finances

`TownGovernment.codex` is the layer-1 civic unit: appointed mayors,
local dues, guard employment/payroll, funding requests, and town grants/loans.
It references an existing `TfWorld` for resident identity and an `EpWorld`
for authoritative money. It does not create townsfolk, guards, buildings,
coin or inventory. The supplied state must remain single-owned and alive.

`tg-new people economy` preallocates civic records and starts its day cursor
at the economy's current game day. It is initialization, not a recovery
fallback. [TownGovernmentCodec.md](TownGovernmentCodec.md) defines the TGC1
component checkpoint. Rebuilding government instead of decoding that state
would lose dues stamps and obligations.

For a denominated economy, use `tg-new-currency people coordinator royalActor`.
It returns a Result, retains the canonical coordinator/world and binds the
server-verified Lord British economy actor. An invalid actor or coordinator
refuses. The legacy constructor's mutation admission refuses the extended
metal catalog, preventing an accidental direct gold-ledger bypass. This is
fresh government construction, not migration of existing civic obligations.

Currency-bound government routes dues, payroll, arrears, grants, loans and
TownWorks payments through the coordinator. Existing civic amounts remain
gold coin counts; repricing does not silently convert dues, wages or rewards.
Copper and silver balances are untouched by those payments. The economy owner
advances `eu-hour`; government does not advance a separate money clock.

## Authority and admission

`tg-register state authorized town mayorNpc townPurse` admits the next existing
town in order. The purse must be kind 4 and owned by that town ID. The mayor
must be a living adult NPC resident of that town. `tg-appoint state authorized
town mayorNpc` replaces the incumbent and logs the appointment. Every mayor
operation rechecks the current office and resident state; replacement or loss
of eligibility removes the former mayor's powers immediately.

The `authorized` Boolean represents the server's authenticated Lord British
decision, never a client flag or a model's claim. The server must authenticate
the acting mayor's scheduled role before passing a mayor NPC ID. IDs are not
credentials. Townsfolk NPC IDs, economy actor IDs and purse IDs are distinct
namespaces; NPC purse kind 2 uses the NPC ID as its owner key. This component
does not grant GM authority and has no player-account action.

## Dues and employment

Ruling (Damian, 2026-10-07): "no, the kingdom taxes gold, silver, and copper ore
mining, and the king has a magic gold supply. that is plenty." The live
composite collects no town dues from anyone: Britain's office keeps its dues at
zero, and the mayor reports only the town purse. The dues operations below
remain the component's contract and are not called.

`tg-set-dues state mayor town amount` sets a flat daily gross due from zero
through 1000000 coin; zero disables collection. Rates are explicit policy,
not a production default. `tg-collect state mayor town payerPurse liable`
requires current mayor authority, an NPC or player purse, and authoritative
liability. NPC liability additionally requires a living adult resident of
the town. The server derives player liability from the actual world/property
rules. The Boolean does not establish residence or ownership by itself.

Successful collection stamps that town/purse/game-day pair. A second attempt
that day refuses, even after a dues-rate change or mayor replacement. Failed
payment does not stamp, move coin or append a civic success event. Royal tax
uses the ordinary payment rule; the office's `collected` counter records net
coin credited to the town, and the audit records the gross due.

`tg-hire state mayor town npc npcPurse wage` requires an eligible resident,
its matching NPC purse, no other active guard employment, and wage 1..1000000
gross coin per game day. It returns a lifetime guard ID. This records an
employment contract; it does not spawn a guard or implement movement/combat.
`tg-retire state mayor guardId` stops future wage accrual but preserves arrears.

`tg-close-day state` processes exactly the next completed economy game day.
A repeated call on the same day does nothing; a skipped day refuses and must
be handled by the owner, not silently discarded. Payroll accrues one day's
wage for each active eligible guard. It attempts to pay current wages plus
arrears from the town purse. Insufficient funds or coin-log capacity leave
the debt recorded; no coin is invented. A guard no longer eligible stops
accruing wages, with its old arrears preserved. `tg-settle state mayor guardId
amount` can pay all or part of active or retired guard arrears later.

Payroll pays gross wages under the ordinary tax rule. Office `paid` records
gross debits; `unpaid` equals the sum of guard arrears, including retired guards.
The enclosing owner advances the economy clock first and calls this day
processor in order. A payroll day contains separate money actions, not one
atomic day transaction. Civic log capacity is reserved before the day starts;
individual payments can still record arrears if the money ledger fills.
For a currency-bound government, a full coordinator event log is likewise a
payment refusal: payroll records arrears, and later settlement can pay them
after the owner supplies an admissible financial state. No payment falls back
to a direct gold-ledger mutation. Internal payment helpers are not client or
model admission endpoints.
Full append-only logs have no rotation or reclamation in this unit. The
capacity proof restores an artificial full marker; it does not establish a
production method for recovering log space or discarding old history.

## Gifts to the town

The mayor accepts money from anyone, player or NPC, and puts it in the town purse (Damian, 2026-10-08: "mayors should accept money from anyone and put it in town purse").
Every gift is one logged payment from the giver's purse (`cg-gift-pay`, `eu-pay`, currency kind 4); an NPC gives through
`cg-town-gift`. A player says "donate N gold" (or silver, copper) within 4 tiles of the mayor. A coin pile dropped on
the mayor bounces with that instruction, because a player's coin is purse coin and a pile in a player's pack is no share
of it.

## Royal funding and audit

`tg-request state mayor town kind neededBalance` records a positive shortfall
against the current purse: kind 1 grant, kind 2 loan. Requests move no coin.
`tg-grant state authorized town amount` transfers existing treasury coin;
`tg-loan state authorized town amount interest dueGameHour` records a real
treasury loan through the economy loan engine. Partial funding reduces the
request; full funding clears it. Lord British can also fund a town without a
prior request. No request itself conveys spending authority.

Town grants use money-ledger kind 13 and reference the receiving town ID.
The existing crown-business grant and UEI1 opcode retain their original
semantics, preserving replay of earlier refusal outcomes. Economy checkpoint
validation accepts and audits the new town-grant ledger rows.

Every successful civic action gets sequence, game hour, kind, town, acting
mayor NPC (zero for the royal/clock owner), subject, amount and related money
action ID. Kinds are registration 1, appointment 2, dues rate 3, collection 4,
hire 5, retirement 6, paid wages 7, wages owed 8, town grant 9, town loan 10,
day close 11 and funding request 12. The related money action always names
the gold ledger sequence, including with a currency coordinator; its currency
event links that same gold sequence. No audit rows are overwritten or cleared.
Refusals append no success row and must be reported by the calling adapter.

## Bounds and acceptance

Budgets are four town offices, 64 lifetime guard contracts, 8192 civic events (tg-event-limit)
and one last-paid-day cell per town/purse pair (4 * 256 cells). Native offices
have nine fields (72 bytes), guards six (48 bytes), events eight (64 bytes)
and state thirteen (104 bytes), plus lists, the optional binding and the stamp
array. Currency binding adds
one retained reference and royal actor ID, not a copy of the economy. Daily
dues and payroll retain no heap; financial routing adds constant work.

Office and collection operations use indexed access. Hiring scans at most
64 contracts for existing employment. Payroll preflight scans those contracts
for each registered town, then visits each contract once. The bound is
O(towns * guards), with at most 64 frames in the sum recursion. Money transfer
and audit row costs remain the bounded money module's costs. Full civic audit
or counter budgets refuse before mutation; a money refusal leaves the civic
payment uncommitted and records arrears only inside the day processor.

`proofs/TownGovernmentProof.codex` starts from the mined-gold economy fixture,
registers Britain and Minoc, obtains an actual grant and loan, and runs 30
game days of dues and wages with daily money/material checks. It grades
duplicate collection/day refusal, immediate mayor replacement, exact town
purse balances, unpaid wages, retirement without debt forgiveness, funded
settlement, audit exhaustion and economy codec support for town grants.
Compile normally and poisoned with an explicit depot kernel and compare the
complete output with `TownGovernmentProof.expected`.
`GovernmentCurrencyProof` grades coordinated funding, dues, payroll, arrears,
bounties, event-log backpressure and prevention of the legacy extended-catalog
bypass. TownWorks and TownDisputes proofs also cover the shared constructor and
payment interfaces.

[TownWorks.md](TownWorks.md) defines the native militia response, repair and
essential-workplace orders and bounties. [TitleRegistry.md](TitleRegistry.md)
defines registered ownership, provenance and transaction participation.
[TownDisputes.md](TownDisputes.md) defines evidence-gated case closure and
keeper escalation. [TownGovernmentInput.md](TownGovernmentInput.md) defines
ordered civic replay and component restart. Actual combat, composite restart and
panel/game bindings remain stage G work. This unit does not replace the
townsfolk/economy clock owner.
