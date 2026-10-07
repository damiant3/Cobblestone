# Economy purses and coin ledger

`EconomyMoney.codex` is the stage E money foundation. A new state has an
empty treasury; every admitted NPC, player, town and royal-business purse
also starts empty. The state is currently an in-memory simulation component.
The existing townsfolk `coin` fields have not migrated, and the combined
checkpoint does not yet carry these records. No live server uses these calls.

## Rules and entry points

Purse 1 is the treasury. `em-open state kind owner` admits a unique pair:
kind 2 NPC/vendor, 3 player, 4 town, 5 crown-owned business. IDs are positive;
admission returns -1 on refusal. Open purses never receive a seed balance.

`em-pay state payer payee gross` debits the gross amount, credits the
recipient with gross minus tax, and credits the treasury with the tax. The
rate is integer basis points, initially zero; fractional coin rounds down.
Treasury payments and receipts do not tax themselves. A successful payment
returns 0; refusal returns -1 and changes no purse, ledger or sequence.
Payer and payee must differ. Ordinary payments cannot debit the treasury;
treasury spending uses the loan or authorized grant paths. Negative and zero payment requests refuse.
At a 100% tax rate the recipient receives zero and the treasury receives all.

`em-lend state lender borrower principal interest dueHour` creates a loan
and transfers the principal from the lender's purse. The loan ID links both
purses; principal and interest are fixed coin amounts, with no compounding.
Loan principal and repayments are gross amounts under the same tax rule as
other payments. The borrower owes principal plus interest; tax is withheld
from each recipient, rather than added to the debt. A treasury loan is
untaxed. Lenders can be NPCs, players, towns or the crown. Loan creation
returns a positive loan ID, or -1 without mutation.

`em-repay state borrower loanId amount` accepts partial payments up to the
remaining debt and requires the recorded borrower. Status 1 is open, 2 paid,
3 defaulted. `em-advance state hour` refuses time reversal, marks every unpaid
loan due at that hour defaulted, and records each default once. Default does
not erase debt; later repayment can settle the loan. The cumulative default
counter does not fall after settlement. The ledger event carries both purses
and the loan ID for later townsfolk reactions.

`em-set-tax state authorized basisPoints` and
`em-grant state authorized businessPurse amount` require a trusted server
authorization result. A grant can only pay a crown-owned business. Both
operations write administrative ledger entries. The Boolean is not an
authentication mechanism: a future server adapter must derive authorization
from its authenticated admin session, never from player-supplied fields.

`em-reclaim state purse amount` transfers decayed or removed coin to the
treasury. No operation destroys coin. The eventual world-removal path must
pair removal with the transfer in the same game-action transaction.

The starting treasury is zero under R8 (Damian, 2026-10-04); Lord British
mines the first gold. `em-issue state authorized amount source` is an internal
primitive accepting only source 2, a royal mint issue. Source 1, the former
starting-grant path, is refused. [EconomyMint.md](EconomyMint.md) consumes real
ingots and records their linkage to issuance. Server code must use that
adapter, never dispatch the internal issuance primitive from a request.
The primitive alone does not establish backing. Monster gold and other
unsettled R8 sources have no entry point.

## Server admission and atomicity boundary

Every mutator is a trusted simulation primitive, not a player command handler.
Purse IDs, owner labels and loan IDs confer no authority. Before calling pay
or repay, the server must authenticate control of the debited purse. Lending
requires both parties' consent and treasury authority when the crown lends.
Opening a purse requires verified identity and role; a player cannot label
their own purse as a crown business. Reclaim requires an actual authorized
world-removal event; advance is server-clock-only. Vendor startup funding
must be admitted as a loan, not disguised as a player payment. No actor-aware
server wrapper is supplied here, and these calls must never be dispatched
directly from untrusted request fields.

Every accepted ledger action receives a monotonically increasing action ID.
Purse admission is separate: it creates zero coin and no ledger entry; the
future durable adapter must persist purse identity as part of its transaction.
An entry contains action, hour, payer, payee, amount, kind and reference.
Tax and payment rows share an action ID. Each positive transfer is a debit
from one purse and an equal credit to another. Issuance debits external source
0 and credits treasury 1. Tax configuration, clock and default entries move
zero coin. Entry kinds are payment 1, tax 2, tax-rate change 3, issuance 4,
grant 5, reclaim 6, loan 7, repayment 8, default 9, clock 10, mint policy 11,
mint ingot consumption 12. The two mint metadata kinds move zero coin.
Tax rows retain
the parent loan reference when applicable. Kind 13 is an authorized town grant;
its reference is the town owner ID of the receiving kind-4 purse.
Kind 15 is the currency coordinator's zero-coin sale valuation marker; its
reference is the quoted gold price, not gold paid. Actual tender and tax stay
in their denomination ledgers. [EconomyCurrency.md](EconomyCurrency.md) owns
the matching currency event and material-trade validation.
Kind 19 is a theft (`em-steal`): coin moves between two private purses with no
tax and reference 0.

`em-town-grant state authorized townPurse amount` transfers treasury coin to
an admitted town purse. [TownGovernment.md](TownGovernment.md) supplies town
and mayor admission. The older `em-grant` remains crown-business-only, so
existing UEI1 operation 13 keeps its recorded-outcome behavior.

The implementation validates every affected purse and reserves all ledger
rows before mutation. Ledger exhaustion refuses the entire action, including
an hour advance with multiple defaults. No ledger records are overwritten or
acknowledged away. Native calls assume one writer and valid module-owned state;
private mutation helpers are implementation details, not admission APIs.

These guarantees cover refusal before mutation in one process. They do not
provide disk commit, crash rollback or atomic item-plus-coin trading. The
[database migration](Database.md) must enclose each game action and its ledger
rows in one durable transaction. Player-to-player item trade logs belong to
the game adapter, not this money-only ledger.

`em-balanced state 1` reconstructs each purse from the ledger and compares
total coin with cumulative issuance. The census detects unlogged purse changes.
The ledger is trusted server data; this query is not an untrusted-file decoder.

## Budgets and proof

The state preallocates 256 purses, 128 lifetime loans and 8192 lifetime ledger
rows. Purse records have 3 integer fields (24 bytes), loans 7 (56 bytes),
ledger entries 7 (56 bytes), and the state 11 fields (88 bytes). Lists add
their normal runtime storage. Construction retained 609720 bytes under depot
kernel `EF9466BEF7CB5FDA` on 2026-10-04. Payment, loan and clock operations
allocate no further heap. Coin, cumulative issuance, action IDs and hours
are capped at 1000000000000; the tax multiply fits signed 64-bit arithmetic.

Payment and repayment are O(1). Purse admission scans at most 256 entries.
Clock advancement scans at most 128 loans twice. Census is
O(purse-count * ledger-count), for offline/health checks rather than every
payment. The proof checks no allocation across a ledger-filling payment run.
The bounded ledger refuses until a future database-backed implementation can
retain further history; this unit does not establish production log capacity.

`proofs/EconomyMoneyProof.codex` grades loan funding, tax, partial repayment,
default and recovery, administrative refusal, decay transfer, source bounds,
ledger/action identity, deliberate unlogged-coin detection, capacity refusal,
and flat heap. Compile normally and poisoned with an explicit depot kernel,
then compare the entire runtime output with `EconomyMoneyProof.expected`.
Stage E's resource chains, prices, consumption and 30-day census remain open.
