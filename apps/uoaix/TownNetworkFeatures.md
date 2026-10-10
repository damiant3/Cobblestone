# Live economy-v1 network inputs

`TownNetworkFrame` owns the economic input layout shared by the hill-training
corpus and `TownNetworkFeatures`. This removes separate training/runtime
normalization code. The layout remains the economy-v1 contract in
[TownNetworkHill.md](TownNetworkHill.md); the existing frozen weights retain
their meanings.

`tnf-frame economy actor producedItem role goalItem needed trade inputs base`
writes one 32-cell frame at the supplied base. It checks the actor, item IDs,
role, positive required quantity, culture range and destination bounds before
writing. Goal zero means no item goal. Invalid arguments return -1 without
changing the destination. Success returns zero, clears slots 0..29 and writes
the economic inputs. Slots 30 and 31 remain unchanged for recurrent happiness
and alarm; `tn-step` supplies them from its previous outputs.

The frame reads current hunger, gold purse balance, owned food, progress toward
the selected item goal, produced-item stock and current `ep-price` quote.
It writes the role indicators and the economy-v1 work/meal/market hour flags.
Role codes are 0 other, 1 smith, 2 miner/ingot producer, 3 farmer and 4 armourer.
`tnf-output` and `tnf-role` derive these from admitted resource/recipe catalog
IDs; their caller must validate those IDs before using the helpers.

This version keeps safety at 1000 and untrained slots at zero. It does not
invent a raid observation or feed live fatigue, ties and social memory into
weights trained with those slots zero. Those inputs require the expanded
simulation corpus and a separately graded model. A produced item with no stock
retains the original model's missing-quote encoding from `ep-price == -1`.

## NPC and culture binding

`tnl-culture people` creates four initially unconfigured town trade profiles
for the borrowed `TfWorld`. `tnl-town cultures verified town trade` sets one
admitted town's trade value in -1000..1000. The server supplies the verified
keeper decision; a Boolean passed by a model or player is not authentication.
The command endpoint owns its authorization, audit and durable commit.
The data represents the network's trade preference axis, not a complete town
dialect, jargon dictionary or customs record.

`tnl-bind economy cultures npc actor producedItem role history` admits a
binding to the current economic actor. Its purse must be NPC kind 2 with
legal owner equal to the townsfolk NPC ID. The person must be alive, the two
records must name the same place, and the economy and townsfolk clocks must
agree. The current town must have a configured profile. The produced item and
role are trusted server configuration, not network outputs.

History is exactly four nonnegative game-hour totals, indexed by town ID minus
one. Every nonzero entry requires a configured admitted town. Their sum must
not exceed `ep-limit`. Admission copies the values; later changes to the
supplied list cannot rewrite a binding. Initial history comes from the NPC's
admitted biography or restored record, not a guess based on current location.
With zero prior hours, the current town supplies the initial trade value.

The binding rejects a denominated-currency catalog: economy-v1's coin feature
uses gold counts, and must not silently reinterpret copper or silver wealth.
It does not attach or advance a currency coordinator.

`tnl-goal binding item needed` sets a validated item goal; zero item clears
the goal. The caller resolves the goal from the NPC's current needs and job.
This setter does not buy items, authorize an action or choose a counterparty.

`tnl-frame binding state` refreshes current economic inputs and culture slot
25. It refuses stale identity, death, mismatched place/clocks, clock reversal,
residence overflow, a short input vector or a pending action queue. No input
or residence count changes on those refusals. Binding fields and the admitted
catalog are private trusted state; do not replace fields through native record
writes or share the records across concurrent owners.

## Residence time and movement

Each successful sample credits elapsed game hours to the recorded current
town and advances the binding's last-sampled hour. Sampling again in the same
hour adds nothing. The trade input is the sum of each town's residence hours
times its current trade profile, divided by total hours with integer division.
Thus an NPC retains influence from previous towns after moving.

If the person's town changed, ordinary sampling refuses instead of guessing
when the move happened. The world owner must call `tnl-arrive binding verified`
at the actual admitted transition hour, with aligned clocks and locations.
That credits the elapsed interval to the old town and records the new town;
later hours accrue to the new town. This function does not move the person,
update population counts or establish a valid path. The world transition and
its residence update belong to one owner transaction.

Changing a town profile changes its contribution to every binding's weighted
input, including prior residence there. The retained history is duration by
town, not a historical sequence of old profile versions. This is an explicit
current-profile policy. It must not be presented as a reconstruction of what
a town believed at each historical date.

## Scheduling, persistence and evidence

The caller retains one binding and one `TnState` per NPC, and one shared model
and culture table. The normal order is: choose the current item goal, refresh
with `tnl-frame`, call `tn-step`, resolve proposals from current server state,
then drain through [TownNetworkActions](EconomyActions.md). A pending queue
must be drained or explicitly handled before refreshing. Network inputs
do not grant offer consent, actor authority or a right to move inventory.

The live binding is an in-memory native API. Server clock/session wiring,
autonomous goal/proposal resolution, culture administration, durable residence
and culture snapshots, language-model context and the full stage N run remain
open. Existing TSC1/UEC1 snapshots do not acquire these records automatically.
Persist binding configuration, residence hours, current town and sampled hour,
town profiles and the corresponding world/input-log position together before
claiming restart support. No raw heap pointers belong in that format.

A binding retains its copied four-town history and nothing per frame; the
network state is separate. Work is constant in population: fixed input slots and
four town terms, direct actor/item access and the existing quote calculation.
The full-population scheduling budget still needs an integrated measurement.
Compiler heap/time behavior is unchanged.

`TownNetworkFeaturesProof` grades owner identity, normalized current values,
copied history, elapsed time, movement, profile edits, refusal boundaries and
flat retained heap. Normal and poisoned outputs must match exactly.
`TownNetworkOutcomesProof` and `TownNetworkHillModelProof` grade the shared
training frame and require the recorded initial/final model scores and utility
to remain unchanged. These are compatibility and feature-admission proofs,
not claims of role/price, raid or multi-day policy acceptance.

The shared-frame CUDA reproduction with seed 104729, step 50 and 20000 proposals
produces the identical frozen model source and weights. Its model-source SHA-256
is `EDCD078DB60F1BF0555838FF6007497BFF3B4262237F1C7CEBF9AAD4BDD2DE0E`.
