# Townsfolk network CPU core

`TownNetwork.codex` is the inference and proposal-ordering core for UOAIX
stage N. `TownNetworkWeights.codex` supplies a hill-trained one-step economy
prototype; [TownNetworkHill.md](TownNetworkHill.md) owns its score, corpus,
reproduction and acceptance limits. The synthetic weights in
`proofs/TownNetworkProof.codex` test core arithmetic and ownership, not policy.
`TownNetworkActions` drains proposals through the shared consent-bound
[economy validator](EconomyActions.md). [TownNetworkFeatures](TownNetworkFeatures.md)
binds economy-v1 inputs to current economic records and residence-weighted
town trade profiles. Expanded social/event inputs, live-shard integration and
the full stage N economy/population acceptance remain open.

The existing `AI.NeuralNet` builds intermediate tensors on each forward pass.
This core instead gives each NPC fixed buffers and shares one admitted model.
The architecture has 32 inputs, 16 hidden units and 14 outputs. Changing the
shape requires retraining, a new model contract and a new population budget.

## Ownership and arithmetic

`tn-model hiddenWeights hiddenBias outputWeights outputBias` admits four
lists of lengths 512, 16, 224 and 14. Matrices use row-major order. Every
weight and bias lies in -4000..4000. An incorrect length or value returns
`Err`; `Ok` borrows the supplied lists for the model's lifetime. Treat those
lists and the returned model as immutable after admission. There is no model
copy per NPC and no inference-time allocation of weight storage.

`tn-new 0` constructs one NPC's mutable buffers. The owning simulation thread
serializes access. Different NPCs must use different state records. Keep the
model and NPC allocations below any temporary arena mark used by a caller.

All values use integer scale 1000. A dense row sums integer products, divides
once by 1000 with Codex integer division, then adds its bias. Hidden activation
clips to 0..4000. The admitted bounds keep every accumulator within a signed
64-bit integer. This is quantized arithmetic, not floating-point equivalence.
The offline trainer must export this scale, layout and activation contract.

`tn-input state slot value` accepts slots 0..29 and values -1000..1000.
Refusal returns -1 without mutation; success returns 0. `TownNetworkFrame`
owns economy-v1 meanings and normalization for training and live bindings.
Slots 30 and 31 are
reserved for the previous happiness and alarm. Initial happiness is 500 and
alarm is zero. `tn-step model state` copies the previous emotional outputs
into those slots, computes both layers and replaces the pending proposal queue.

## Outputs and authority

Output slots 0..9 score action IDs 1..10: work, buy, sell, eat, flee, call the
guards, visit, haggle, celebrate and rest. Only strictly positive scores enter
the queue. Higher scores come first; equal scores keep the lower action ID
first. `tn-pop state` returns the next action ID, or zero when exhausted.
A new step replaces unconsumed proposals. The economy queue adapter requires
enough audit capacity before consuming any pending item. Preserve a queue
waiting on capacity; do not replace it with a new step.

Slots 10 and 11 are happiness and alarm, clipped to 0..1000 and exposed by
`tn-happiness` and `tn-alarm`. Slots 12 and 13 hold two signed opinion axes,
clipped to -1000..1000 and read by `tn-opinion state axis` for axis 0 or 1.
The chosen model defines those axes; the hill prototype documents them in
`TownNetworkHill.md`. An invalid axis
returns zero. No output supplies an actor identity, target, amount, location
or authorization. An action ID is a proposal, not a world operation. Every
adapter must bind the live NPC and pass resolved proposals through
the layer 1 rules; this module performs no world mutation.

## Cost and proof

The shared model holds 766 integer parameters. Each state holds 32 input,
16 hidden, 14 output and 10 queue cells, plus the four list headers and the
six-field state record. `tn-step` performs 736 multiply-accumulates, 30 row
divisions, bounded clipping and ordering of at most ten proposals. Work is
O(inputs * hidden + hidden * outputs + actions squared); state is
O(inputs + hidden + outputs + actions). Inference and queue draining contain
no record, list or text constructors. Construction is separate.

The core budgets are 1024 bytes per state and 1024 multiply-accumulates per
step. The shared model and feature/world storage are separate.

`TownNetworkBench.codex` reads a decimal round count (0..65536) from stdin,
constructs and warms 128 states, then runs that many population rounds.
Its inputs exercise dense layers, activation saturation and positive action
scores ordered by insertion shifts. Time the same compiled image from the
host for zero rounds and for 8192 rounds. The zero-round arm estimates VM
startup, construction, warmup and harness overhead. Both arms retain zero
additional heap. The result is a host-wall throughput observation, not a
worst-case execution bound or a server tick guarantee, and excludes feature
extraction, queue draining, action validation, world mutation and training.
Do not substitute `get-ticks` for elapsed time: codex-vm can miss PIT periods
when host scheduling delays interrupt delivery.

`TownNetworkProof.codex` checks model and input refusals, dense arithmetic,
activation/output saturation, ordering and ties, omitted actions, recurrent
emotional inputs, distinct NPC state, one 128-NPC round and repeated inference.
The proof reads the heap cursor around construction and around inference.
These differences measure retained allocation, not transient high-water usage.
Compile and run with an explicit depot seed in normal and poisoned allocation
modes; compare the complete output to the proof's expected fixture.

The core and limited hill prototype do not close stage N. An integrated
full-population budget, scripted role/price and raid behaviours, live
culture-history controls, durable live-shard action binding and the 30-day
stage E/M integration grade remain required. The current queue proof grades
in-memory consent and dispatch with a synthetic priority model.
