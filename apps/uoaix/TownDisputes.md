# Town disputes and keeper escalation

`TownDisputes` binds a town government, title registry and the existing keeper
report queue. It stores 128 lifetime cases and 256 append-only events. Parties
use `TrParty` identities: player 1, NPC 2, household 3, town 4 and crown 5.
These differ from `EmPurse` kinds; debt admission explicitly maps between them.
Case records copy scalar identities, so later changes to input party objects
cannot rewrite the case.

`td-new` takes retained `TgWorld`, `TrRegistry`, `AdminWorld` and optional
`EuCurrency`. The shard owner must bind the same canonical people/material
world instances used by government and admin, and the same economy instance
held by currency. It must preserve those objects below operation scratch.
No structural equality check can establish that ownership. With currency,
all three denominations are available and coordinator readiness is required;
without it, debt queries admit only gold. Government financial operations
themselves remain separate from this read-only debt observer.

## Filing and settlement

`td-file` requires the current living adult mayor of the named town, a verified
world-context decision, distinct valid parties and a positive source-log
event. NPC parties must be living residents of the town; they need not be
adults. The verified decision establishes identity, jurisdiction and the
authentic source event, rather than trusting a model's allegation. Reusing a
town/source-event pair refuses even after its earlier case closes.

| Case kind | Reference | Settlement requirement |
|---:|---|---|
| 1, debt | Loan ID plus metal | Actual recorded loan reaches fully paid status |
| 2, land | Authoritative plot reference | Verified lawful agreement and land-action log reference |
| 3, item/theft | Stable world item serial | Verified restitution, matching registered owner and consistent physical mark evidence |
| 4, player misconduct | Conduct-log reference | Keeper escalation only |

Debt filing checks that the recorded lender and borrower match the legal
parties and that the debt remains unpaid. Treasury and royal-business purses
map to the crown legal party, while the case retains the exact lender and
borrower purse IDs. `td-debt-settled` rechecks those IDs and requires paid
status with paid amount equal to principal plus interest. Default or partial
repayment does not settle a dispute. The operation closes the case; it does
not move coin, forgive debt or decide a contested lending contract.

`td-title-settled` requires caller-verified physical restitution and current
`TrEvidence`. Registry owner, true item owner, visible mark and revision must
agree with the claimant. Unregistered, forged, released, retired, absent or
prepared-registry states refuse. This observer neither transfers items nor
rewrites titles, marks or provenance. A failed provenance check is not itself
a criminal conviction.

`td-land-settled` records a verified lawful land agreement and its authoritative
land-event reference. This module has no plot allocator or land-title store;
the caller must validate that both required parties consented and that the
world's land rules and property operation were satisfied. A layer-2 proposal
cannot set the verification flag. The case record is not proof that a plot
operation occurred independently of that adapter.

Case status is open 1, settled 2 or escalated 3. Settlement resolution codes
are paid debt 1, verified item restitution 2 and admitted land agreement 3.
Current mayor authority is checked on every action, including after office
replacement or death. No local settlement function admits misconduct cases.
The low-level log, close and queue helpers are internal; no client or model
request may dispatch them instead of the admission functions.

## Keeper queue

`td-escalate` sends an open case through `AdminWorld.admin-report`, then records
the returned queue ID. It derives subject kind from the respondent: a player
uses an account, an NPC uses its NPC ID, and household/town/crown matters use
the world town ID with the object/plot reference in evidence. NPC and world
reports require an empty account argument and acquire no invented account.

The caller supplies `TdReportFacts`: verified binding, actual account for a
player respondent, what happened, location, wall time and evidence lines. The
verification covers the player-character-to-account mapping as well as the
log evidence. The module does not contain the account database and cannot
establish that mapping from an account string alone. Local limits are account
64, happened 256, location 64, wall time 64 and evidence 400 characters;
non-account fields must be nonempty. It appends immutable dispute, source-event
and object references before passing the existing report validator.

Game time comes from the retained economy clock. The actual wall-time string
comes from the shard owner. Report text is copied into the queue's preallocated
storage; temporary JSON/text is reclaimed before returning. Reports retain
their ordinary keeper acknowledgement flow. A case keeps its queue ID and
cannot enqueue twice, including after acknowledgement.

Full report queues leave the case open for later retry. Full case/event storage
refuses before effects; history is never overwritten. Queue admission and case
transition are single-owner operations, with local event capacity reserved
before touching the queue. The enclosing durable shard commit must include
both changes. None of these calls suspends, bans, edits or otherwise acts on an
account. Misconduct and unresolved judgement remain for the human keeper.

## Cost and evidence

Cases contain 16 scalar fields, events six. Construction retained 35064 bytes
under seed `4228CD5103DC4523` on 2026-10-04, excluding the existing government,
registry, economy and queue. Duplicate filing scans at most 128 cases. Debt
settlement is constant work; item settlement scans at most 128 titles. Report
work is linear in the bounded text/JSON size. Filing, settlement and escalation
retain no per-action heap; report/parser and detached title-query scratch is
reclaimed internally.

`proofs/TownDisputesProof.codex` checks actual denominated repayment, title and
restitution admission, prepared registry refusal, external land agreement
gating, player/NPC/world report subjects, detached case identities, queue/audit
backpressure, mayor replacement, existing UAC1 queue recovery and case capacity.
Normal and poisoned output must match the complete oracle. The fixture's
world-context, restitution, land and account bindings are explicit trusted test
facts; no live physical adjudication or authentication is claimed.

[TownGovernmentCodec.md](TownGovernmentCodec.md) preserves case/history records
with offices and public works in TGC1. Ordered dispute inputs, live plot/item
adapters, mind judgement proposals and the encompassing shard transaction
remain open. UAC1 preserves the keeper queue, not these case/history records.
Never restore the queue alone as a complete dispute system.
