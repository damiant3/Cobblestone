# Economy proposal admission

`EconomyActions` is the shared in-memory admission boundary for a server-bound
actor's work, buy, sell and eat proposals. `TownNetworkActions` sends each
network queue item through it. It uses the existing production, harvest and
money rules on current `EpWorld` state. It has no fixture market or implicit
reserve buyer.

The server supplies `principal` from its authenticated session or controlled
NPC binding. Model output and player text cannot choose that binding.
`EaProposal.actor` must equal it. This is a domain API for trusted server code,
not an authentication protocol: callers must not expose the principal argument,
the offer-posting API or the underlying `ep-*` functions as unchecked model
tools. Serialize calls sharing a book and its economy. The book borrows that
economy and owns its fixed offer and event arrays.

## Consent and execution

`ea-post book principal kind target item quantity limit expires` records the
principal's consent to buy (kind 2) or sell (kind 3). Target zero permits any
other actor; a positive target permits only that actor. Quantity is a total
remaining allowance, not a quantity renewed for each request. Limit is a
maximum unit price for a buyer or a minimum unit price for a seller. Expiry is
an inclusive game hour. An offer must be funded or stocked when posted, but
does not reserve coin or stock. Existing rules recheck both at execution.
Admission returns a positive offer ID; invalid terms return -1, exhausted
offer/audit capacity returns -14 and a denominated-currency economy returns
-17. `ea-cancel book principal offer` permits only
the owner to revoke an active offer. Cancellation returns 0, -1 or -14.

`ea-submit book principal kind proposal` uses these action IDs and fields:

| Kind | Required proposal fields beyond actor/kind |
|---|---|
| 1, work | Positive node, or positive recipe and station; not both |
| 2, buy | Seller target, item, positive quantity, seller offer, maximum unit price |
| 3, sell | Buyer target, item, positive quantity, buyer offer, minimum unit price |
| 4, eat | Food item and quantity exactly one |

All unused fields must be zero. Work passes through `eh-harvest` or `ep-craft`;
eat passes through `ep-eat`. Minting and arbitrary inventory changes have no
proposal action. Kinds 5..10 (flee, guards, visit, haggle, celebrate, rest) are
currently unsupported and explicitly refused.

Buy and sell require a live offer from the other party for the opposite side,
the same item and enough remaining quantity. The current `ep-price` quote must
satisfy both parties' limits. Location, inventory, purse balance, tax, wear,
trade capacity and money capacity remain the existing layer 1 checks.
Only a successful transfer decrements the offer. A refusal never consumes
its allowance. Offers are immutable except remaining quantity; posting another
offer or revoking one does not rewrite a prior offer ID.

Prices and limits in this version are **gold coin counts**, matching `ep-price`
and `EpTrade`. A catalog recognized by `EconomyCurrency.eu-catalog` refuses
offer posting and buy/sell dispatch with -17. This prevents a raw `ep-buy`
from bypassing an attached currency coordinator. Work and eating do not move
money and remain admitted through their ordinary rules. Denominated trade
requires a coordinated material/payment operation and new explicit consent
terms; it must not reinterpret old gold limits or change UEI1 outcomes.

The proposing principal supplies consent for its own side through the admitted
request. A controlled NPC's policy may make that request; it cannot consent
for its counterparty. Offer creation must originate from that counterparty's
own authenticated action or server-controlled policy, never from the requesting
network's chosen target.

## Results and audit

| Result | Meaning |
|---:|---|
| Nonnegative | Admitted operation result; work returns produced quantity |
| -1 | Existing economy rule refused |
| -2 | Admitted skill failure; practice and tool wear can change |
| -10 | Principal/actor mismatch or invalid principal |
| -11 | Unsupported action |
| -12 | Missing, exhausted, revoked, expired or mismatched counterparty offer |
| -13 | Current quote violates either party's price limit |
| -14 | Audit, sequence or offer capacity exhausted |
| -15 | Resolved proposal kind differs from the queued kind |
| -16 | Noncanonical proposal or malformed queue binding |
| -17 | Denominated trade requires the currency coordinator |

Every `ea-submit` attempt with audit room records the bound principal, proposed
actor/kind, queued kind, target, item, quantity, offer, price limit, work inputs,
quoted unit price, result, game hour and resulting money sequence. Posts and
cancellations record event kinds 11 and 12. Offer records retain their complete
terms. Refused post/cancel calls return to their caller without an event; the
authenticated command endpoint owns those command refusals.

Audit capacity is 1024 events, with monotonic sequence bounded by `ep-limit`.
Capacity refusal occurs before mutation. `ea-ack` clears the event count after
the owner consumes the rows; it preserves sequence. Offer IDs are append-only
within a book, with 256 slots. Exhaustion is explicit; IDs are not recycled.
No event array grows with uptime.

`tna-run book principal state resolved` takes ten server-resolved proposals,
indexed by action ID minus one. It drains the existing `tn-step` priority queue
in order and checks the world anew for each item. A buy can therefore provide
food for a later eat; a refusal does not abort the remaining queue. Return 0
means every pending item has an audit row, not that every item was accepted.
The resolver chooses targets and work arguments from server state; network
scores supply only action priority. A kind mismatch is refused and logged.

The adapter checks capacity for the entire pending queue before consuming any
item. Insufficient capacity returns -14 with the queue intact. A malformed
binding returns -16 without consumption. A drained queue has no further effect.
Do not call `tn-step` again while a queue is awaiting capacity or its pending
items will be replaced. The caller owns this scheduling rule and audit draining.

## Scope and cost

These operations are live in-memory economy mutations. They are not yet wired
to game sessions, `TownMind`, the server clock or a durable shard transaction.
The caller must not publish them as crash-safe server actions. Offer state,
audit state and the relevant economy/world changes need one versioned durable
transaction and replay contract before that binding. UEI1 records alone do not
persist consent. Existing `EconomyInput` is a trusted recovery format, not this
untrusted proposal admission boundary.

Each book allocates fixed arrays once: 163920 native bytes measured under depot
compiler `4228CD5103DC4523` on 2026-10-04, excluding the borrowed economy.
Submission and queue draining allocate
no new retained guest heap. Direct offer lookup and admission are constant work
apart from existing economy primitives; a network queue contains at most ten
items. Posting/cancellation allocate a temporary request record for logging;
their caller may reclaim its scratch after the scalar result. The book and
economy must remain below that mark. No compiler heap/time behavior changes.

`proofs/EconomyActionsProof.codex` checks consent, principal binding, quantities,
price limits, expiry, revocation, live location and canonical economy bytes on
refusal. `proofs/TownNetworkActionsProof.codex` uses a synthetic priority model
to grade ordered buy/eat, work/sell, unsupported actions, queue capacity and
actor forgery. These prove admission and dispatch, not the frozen model's
policy quality or the full stage N acceptance. Run both normal and poisoned
builds with the explicit depot compiler.
