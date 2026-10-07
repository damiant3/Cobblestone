# Militia, public works and bounties

`TownWorks.codex` extends [TownGovernment](TownGovernment.md) with civic
responses to admitted world events. It references the same townsfolk,
economy, purses and civic audit. It does not spawn guards, buildings, goods
or coin. `tw-new government` preallocates its bounded state.
[TownGovernmentCodec.md](TownGovernmentCodec.md) includes these records in
TGC1; the encompassing durable shard commit remains separate.
Repair fees and bounty payments use the government's payment route. With
`tg-new-currency`, they append the existing currency coordinator's history;
they never fall back to direct money mutations. Fee/reward amounts and stored
money-action references keep their gold coin/gold ledger meanings.

## Admission boundary

The server supplies authoritative raid events, actual damage and repair bills,
property consent, eligible threats and verified clearing results. Boolean
parameters represent those prior checks; they are not client flags or a
model's asserted authority. The current mayor is rechecked on every mayor
operation. The world adapter must bind real locations, ownership and character
identity before dispatch. Native simulation IDs are not credentials.

`tw-admit-site state mayor town station permitted` registers a workplace for
civic management after region/ownership/consent admission. It remembers the
current owner; an owner change invalidates the old site binding. Registration
does not transfer title or create a building. `tw-revoke-site` removes the
binding. Repair delivery/work and staffing recheck the binding before effects.

## Raids and repairs

`tw-raid state authorized town eventId` accepts a new positive, increasing
raid ID when the town has no active raid. It marks living adult resident NPCs
as militia, excluding active paid guards. The roster is an assignment snapshot,
not proof of later life, range or combat readiness. The combat owner must
recheck those facts before using a member. No movement or combat is executed.

`tw-stand-down state authorized town eventId` closes an undamaged raid.
`tw-end-raid state authorized town eventId damage` closes a matching raid,
demobilizes its roster, disables the admitted damaged workplace and creates
a repair order. A `TwDamage` holds station, boards, nails, work hours and fee.
The adapter verifies the actual damage and correct bill from world/design
rules; this core checks bounded quantities and site admission. The current
bill supports boards and nails, not every construction material. A repeated
raid-end event cannot create another repair order.

`tw-assign-repair state mayor order actor` assigns a living adult local NPC
with Carpentry skill. A still-eligible assigned worker cannot be displaced.
If that worker becomes ineligible, a successor can take over without losing
delivered material or progress. The fee is a completion fee, paid to the
finishing worker; hourly wage splitting is not implemented.

`tw-deliver state actor order boards nails` consumes actual assigned-worker
stock at the site and counts the materials as installed in the repair. It
refuses over-delivery and cannot be used by an ineligible or unassigned worker.
The material census records consumption. The live world adapter must retire
the corresponding physical item quantities in the same transaction.

`tw-work state actor order` requires the complete bill, the assigned worker
at the site and a hammer with charges. Each admitted attempt practices
Carpentry and wears the hammer. Failed skill returns -2; admission refusal
returns -1 without effects. Per-worker and per-order game-hour stamps prevent
double-counting work, including across separate repair orders. Success advances
one work hour. Completion enables the workplace and attempts the fee payment
from the town purse. If payment fails, the repair remains complete and its fee
remains owed; no coin is invented. `tw-pay-repair state mayor order` settles
that recorded fee after funding and refuses repeat payment.

Repair status is open 1, assigned/in progress 2, complete and settled 3,
complete with unpaid fee 4. Zero-fee volunteer work completes in status 3.
The completion action is in-process state mutation, not a crash-atomic commit.

## Essential workplaces

`tw-essential state mayor town kind` checks mill 2, forge 9 or bakery 6.
An admitted enabled workplace with a qualified operator meets the need. NPC
operators must be living adults; qualified player/company workplaces also
count. World admission supplies the validity of non-NPC operators.

If no workplace meets the need, the operation creates one staffing request
per town/kind. It returns the new order ID, zero when no new request is needed
(including an existing pending request), or -1 on refusal. Zero alone is not
a readiness assertion; inspect the site or pending orders.

`tw-candidate state town skill firstActor` finds a qualified resident NPC,
skipping the current mayor and active paid guards. Explicit assignment still
requires a qualified resident; candidate search is not authority to dispatch.
`tw-staff state mayor order actor station` closes a request only for an
admitted enabled workplace of the required kind, owned by that qualified
actor. The world owner must provision/admit the actual workplace first; the
call cannot create a free mill or seize another owner's building. The order
records the worker and resolved station for the job controller. The proof
then runs the mill recipe through ordinary economy crafting.

## Bounties

`tw-post state mayor town home threatEpoch reward eligible` requires the
server's eligible overflow/raiding-home determination and a positive reward
up to 1000000 coin. A town cannot repost the same home/epoch, even after payment.
A new threat epoch permits a new offer. Offers do not escrow coin; the town
must fund a verified obligation before payment.

`tw-claim state verified bounty home threatEpoch clearingEvent winnerPurse`
requires a matching, authoritative clearing result and an admitted NPC/player
purse. The first accepted claim fixes the winner and proof event. A second
claim refuses. This core does not infer that a dungeon is clear from client
text or manufacture a stage-M combat result.

`tw-pay-bounty state mayor bounty` pays the recorded winner from existing
town coin under the ordinary gross-payment/tax rule. Insufficient funds keep
the verified claim pending; funding can then enable payment. Statuses are
offered 1, verified/unpaid 2 and paid 3. Payment cannot be redirected or repeated.

## Records, audit and cost

Budgets are four town raid records, 64 site bindings, 128 lifetime orders,
128 lifetime bounties, and 128 cells each for militia assignment and worker
hour stamps. Raid records have five fields (40 bytes), sites two (16 bytes),
orders fifteen (120 bytes), bounties eight (64 bytes), and the state nine
(72 bytes), plus lists and buffers. `TwDamage` is a five-field caller value,
not retained by the order. Construction retained 32264 bytes under seed
`4228CD5103DC4523` on 2026-10-04. Actions use preallocated storage.

Civic audit kinds extend the government log: site admission 13, revocation
14, raid start 15, raid end 16, repair order 17, repair assignment 18,
materials delivered 19, work attempt 20, repair completion/payment 21,
essential request 22, staffing 23, bounty offer 24, verified claim 25 and
bounty payment 26. These are civic kinds, distinct from money-ledger kinds.
Raid/clearing rows name actor zero; worker actions name the worker NPC;
mayor actions name the current mayor. Money action IDs link successful fees
and rewards to the coin ledger. Capacity is checked before commitment.

Muster and candidate search cost O(people/actors * guards) within fixed
limits. Site scans cost O(stations), and order/bounty lookup O(their bounded
counts). Indexed delivery, work and payment are O(1). No per-action collection
growth or whole-world copies occur. The shared civic audit's lifetime limit
still applies; no history is discarded to keep a clock running.

## Acceptance and limits

`proofs/TownWorksProof.codex` starts with goods produced by the 30-day economy
fixture. It grades a scripted raid's militia and repair order, real material
delivery, work-time admission, resumed bakery output, missing-mill staffing
and actual flour output, qualified player workplaces, eligible/verified bounty
claims, unpaid obligations, later funding, worker replacement and duplicate
refusal. Another 30 government days preserve money/material validity and retain
no daily heap. The proof compares complete normal and poisoned output with
`TownWorksProof.expected`.

Raid, damage, property and clearing inputs are scripted stand-ins for their
authoritative world adapters. This is not the full stage-G combat grade:
actual stage-M raids, movement, physical serial changes, native government
recovery and panel integration remain open. Disputes, keeper reports and
title registries are separate government units. A live action must put civic,
economy, material and world changes in one encompassing durable transaction.
