# TownNetwork hill-climbing contract

Stage N training follows Damian's hill-climbing ruling in [UOAIX.md](UOAIX.md).
One shared integer weight vector serves every NPC. A candidate changes one
parameter by a signed step, clipped to the CPU model's bounds. Evaluate the
candidate on CUDA and retain it only when its score is strictly greater.
Ties and regressions retain the previous weights. No gradient update or
SGD-trained initial model belongs in this path.

The run must record its initial weight generator, PRNG seed, step size,
iteration count, score definition, accepted-candidate count, initial/final
scores, source/model hashes and CUDA execution evidence. The CPU image loads
only frozen weights and the [CPU core](TownNetwork.md).

## Score version 1

The corpus records outcomes of bounded layer 1 action trials from
stable simulation snapshots. Each action starts from the same snapshot;
an action's inventory, coin and need changes come from simulation operations.
Refused operations receive a refusal penalty. Configured preference weights,
including residence-weighted town culture, determine the utility of those
changes. Record that utility definition with the corpus. Do not substitute
hand-authored action labels or an unrecorded supervisory policy.

For each case, outputs 0..9 predict the measured action utilities. Select the
highest strictly positive output, preserving the lower action ID on a tie;
no positive output selects no action, with utility zero. The GPU score is:

`1000 * measured utility of the selected action - sum((output - outcome)^2 / 16)`

The calibration sum covers all 14 outputs, with integer division per term.
Candidate score is the unweighted sum of every case's score.
The four affect/opinion outcomes must be derived from the trial observations
and declared preferences as well. Every outcome lies in -1000..1000;
happiness and alarm outcomes are nonnegative. This combines achieved
one-step utility with calibration to measured counterfactual outcomes.
The scorer cannot establish the provenance of an arbitrary supplied table;
the corpus builder and its conservation/refusal proofs establish it.

Caching these deterministic one-step outcomes avoids cloning a world for
every GPU candidate. It is not a closed-loop, multi-day training rollout.
The full stage N economy/population acceptance remains a separate required
run with the learned network deciding actions, and requires the relevant
stage E/M adapters. Unimplemented actions and missing event outcomes remain
explicit gaps; a placeholder or teacher label is not their simulation grade.
An action outside the trial adapter's supported set is refused without world
mutation and penalized. That grades the adapter boundary, not the missing
action's future behavior. Sequential execution of a learned priority queue
belongs to the integration grade.

## Device and lifetime contract

The CUDA evaluator uses signed 64-bit integer cells and the same scale,
row order, division and clipping as the CPU core. It must agree exactly
with CPU inference and score calculations on diagnostic cases before use.
`TownNetworkHillKernels.codex` generates `TownNetworkHillPtx.codex`; modify
the source and regenerate the PTX through the project plug.

`tnh-open` borrows host lists only while uploading, then owns eight device
buffers and fixed launch arguments. Sample count is 1..1024; parameters,
inputs and outcomes are bounded before upload. `tnh-try` returns 1 for an
improvement, 0 for a retained incumbent, and -1 for execution refusal.
No failed device operation supplies a score. `tnh-export` copies the best
weights into independent CPU lists. `tnh-close` requests release of owned
buffers, closes the object and rejects further use. CUDA module caching
lasts until the owning VM exits. The backend's live-buffer census must be
zero at successful diagnostic/training exit; this is not physical-VRAM
accounting.

GPU storage is linear in case count and fixed model size. Candidate work is
linear in cases times dense-layer work, plus one parameter-vector copy.
The host reuses launch arguments; it must not allocate a new state per
iteration. A 20000-proposal run retains no new guest heap. The eight device
buffers reserve `8 * (1533 + 77 * cases)` bytes; module/context storage is
additional. Corpus construction keeps serial trial clones in scratch, so
retained storage does not grow with the number of trials. Encoding and
validation scan bounded state/history for each trial. The returned 16-case
corpus retains less than 10000 bytes; that is not a transient-peak measure.

## Recorded corpus and utility

`TownNetworkOutcomes` builds the standard `EconomyClock` fixture and runs
96 game hours. Four existing economic roles are sampled at hour 96 and
hour 108, each under two trade preferences: 16 cases. Setup and trial
purchases use real stock, purses and the existing fixture's consenting
market. Coin/stock provenance is the resource bootstrap in `EconomyClock`;
its test mint terms are not production mint policy.

Each case encodes one `EconomyCheckpoint`. Every trial decodes a fresh
private copy. Work uses the actor's fixture resource/node or recipe/station.
Buy resolves one unit of a missing food/tool/input goal from the fixture's
cheapest stocked seller. Sell uses the actor's produced item and the
fixture's consenting reserve buyer. Eat consumes owned food. Action 10 is
economy idle, with no immediate mutation; fatigue/rest integration is not
graded. Actions 5..9 are refused by this adapter. These fixture rules are
not a live actor-authentication or trade-consent service.

For an admitted trial, utility is the following sum, clipped to -1000..1000:

- Coin change times 10, weighted by `(1000 + tradePreference / 2) / 1000`.
- Hunger reduction times starting hunger, divided by 3.
- Food inventory change times 100, including acquisitions, sales and consumption.
- One fifth of progress toward the current food/tool/input goal; progress
  is the owned fraction of its required quantity, capped at 1000.
- Skill gain plus tool-charge inventory change times five, including acquired or spent charges.
- 250 per produced unit for work.

All differences come from executed simulation operations. Return -1 is
refusal and receives -1000 only after canonical before/after bytes agree.
Work return -2 is an admitted failed attempt: practice and tool wear are
scored, not treated as unchanged refusal. Every trial requires material
census, balanced coin and full state validity. All ten actions are repeated
in reverse order, and every utility must agree. The original snapshot must
remain byte-identical.

The affect targets describe this limited economy opportunity model:
happiness is `clamp(500 + best nonnegative action utility / 2, 0, 1000)`;
alarm is `clamp(starting hunger * 10 - positive eat utility, 0, 1000)`;
opinion 0 averages buy/sell utility; opinion 1 is work utility. They do not
grade raid response, general emotional realism or social-history effects.

The two preferences are duration-weighted mixtures of fixture cultures
with trade values +800 and -800, using residence ratios 12:4 and 4:12.
They produce inputs +400 and -400. `TownNetworkFrame` now supplies the shared
economic normalization; [TownNetworkFeatures](TownNetworkFeatures.md) binds
that layout to live records and residence hours. This training corpus still
uses its declared fixture histories. Active input slots are:

| Slots | Meaning |
|---|---|
| 0, 2, 3 | Hunger times 10; coin times 10 capped at 1000; fixture safety 1000 |
| 4, 5, 6 | Owned meal availability; goal fraction; produced-item stock times 250 |
| 9 | Produced-item quote centred at 10 coins, times 20 |
| 10..13 | Miner/producer, smith, farmer and armourer indicators |
| 21..23 | Fixture work, meal and market time indicators |
| 25 | Residence-weighted trade preference |
| 29, 30, 31 | Produced stock below four; initial happiness 500; initial alarm zero |

Other slots are zero. Inputs are clipped to the CPU range. This small
training set does not establish behavior across all NPCs or unseen events.

## Reproduce and grade

Run from the repository with a new output directory and the explicit
compiler chosen for the proof:

```powershell
pwsh -NoProfile -File apps/uoaix/train-town-network.ps1 -Kernel seed/Codex.cdx -OutDir build-output/uoaix/hill-new -Seed 104729 -Step 50 -Iterations 20000
```

Coordinate GPU use and launch the run detached under the lane protocol.
The script regenerates PTX into scratch, requires agreement with the tracked
PTX, pins resolved source hashes, compiles the trainer and checks CUDA launch
counts, monotonic checkpoints, improvement, export bounds and zero live
buffers. It emits candidate `TownNetworkWeights.codex` and hash receipts
without modifying the tracked model. A run with no improvement is refused
as an export candidate. Module generation needs the existing PTX plug CDX.

Initialization and mutation use the recorded 32-bit LCG in
`TownNetworkHillSeed`; coordinate and direction selection use higher bits.
The recorded run uses seed 104729, step 50 and 20000 proposals. Score rises
from -26209708 to 9594886 with 2477 accepted proposals. The CUDA census records
102482 kernel launches and zero live buffers at exit. The observed host run
took 20.993 seconds including corpus construction and VM overhead; it is not
a latency or scaling guarantee. `proofs/TownNetworkHillModelProof.codex`
recomputes initial/final scores on CPU and compares selected measured utility.

`TownNetworkHillProof` grades exact GPU/CPU integer parity, aggregate score,
strict acceptance, tie/regression refusal and ownership. `TownNetworkOutcomesProof`
grades corpus conservation, refusal bytes, order independence and scratch
reclamation in normal and poisoned builds. Frozen-model acceptance and the
multi-day learned-policy integration remain distinct checks.
