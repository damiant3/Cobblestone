# Assist circuit brain: expert and neural

The UOAIX Assist's circuits (`apps/uoaix/assist/tailoring-circuit.md`) choose their next move by a fixed priority
order written as rules. This design adds a second brain: a planner that looks several moves ahead over small networks
written in Codex, compiled to C# and loaded by the Assist, trained from Damian's recorded play and from the runs'
results. A toggle picks the brain. Owner: blu, solo (Damian, 2026-10-24: "reek needs be told nothing, you can work
solo").

## Rulings

- Damian, 2026-10-19: "a neural network ai for these circuits. the expert system isn't cutting it and its too hard to
  fine tune. we need something that watches me play with the record button, and then helps train a network to optimize
  the runs i make in the the circuit decision making."
- "basically we need a toggle in the game between the expert and neural modes".
- "keep the current code, use it as hands"; "but the high level decisions need a better adaptive brain".
- Codex, transpiled and put in the Assist ("using codex but transpiled and put in this assist").

## Brain and hands

The free pass this design was written over (`RunCircuit`'s tailoring loop before `RunShops`; Open, below) runs
its steps in a fixed order, each step's body guarded by its own condition: bank the gold over `BankOver`; sell here
(nearest tailor first, thread bought from each tailor that answered); the sell trip; sew; weave; buy thread here; then,
only when no step did anything this pass (`did`), the thread trip, the restock wait or the end. That order is the expert
brain.

The split keeps every step body where it is and puts a decision in front of each:

1. **Decision point.** At each step of the pass, before its guard, the brain is asked: run this step now, or not.
2. **Legal.** The step's own guard as the code writes it today (the code is the definition; the table below summarizes
   and is not a second copy). A step whose guard is false is never run by either brain.
3. **Decide.** The expert brain answers "run" for every legal step, which is today's behaviour exactly. The neural
   brain answers from its plan (Planning, below): a legal step that is not the plan's next move is passed over, and the
   pass goes on to the next step. A pass in which the neural brain ran nothing goes to its plan's move among the
   end-of-pass steps (thread trip, wait, end).
4. **Act.** The step body runs unchanged. Every guard the hands hold today stays in the hands: L-FUTILITY
   (`GameLink.Say`, `Silent`, `SkipVendor`, `AvoidTailor`), rule 2's client-legal packets, the fail-safe, and every
   way a step ends the circuit today (a weave that took no thread, a failed sell trip, a failed thread trip, `Silent`).
   Neither brain retries a refused move (L-FUTILITY).

Walking routes are the walk planner's, not the brain's: a wrong path is a walk-planner defect and goes to
`apps/uoaix/uoaix-backlog.md`. The brain chooses where to go; the hands choose how to walk there.

The trips choose their own city inside the hands (`SellTrip`, `ThreadTrip`); until stage 6 the brain decides whether to
take a trip, not where. A city argument for the trip hands is a later change with its own stage.

### Tailoring steps (first circuit)

In the loop's order. Scope: the Tailoring circuit's loop including its Sell-only branch; Tour and Buy thread
(`RunTour`, `RunThreadOnly`) are separate runners and out of scope.

| step | the hands | legal when (summary; the code's guard decides) |
|---|---|---|
| Bank | `BankHere` in this city | gold over `BankOver`, the bank not passed over |
| SellHere | the local sale loop; thread bought from each tailor that answered | shirts in the pack |
| SellTrip | `SellTrip` | shirts at least `SellBatch` |
| Sew | `SewAll` (kits from the toolbag or a tailor first) | cloth or bolts in the pack |
| Weave | nearest loom, `WorkToggle` | thread in the pack, not held for `ThreadMin` while more is coming (`weaveHeld`) |
| BuyThreadHere | `VendorBuy` from the due vendors, nearest first | shirts under `SellBatch`, no work on hand, a vendor here due |
| ThreadTrip | `ThreadTrip` | nothing done this pass |
| Wait | the restock wait (gold banked first) | nothing done this pass, a restock coming |
| End | the circuit ends and says why | nothing done this pass, no restock coming, no thread held |

### State

The input nodes (Damian, 2026-10-19: "the vendors restocking behavior, timing, weight of thread and gold, etc ... all
input nodes"). Stage 1 logs them as named raw values, one column each (`DecisionHeader` in `AssistFeatures.cs`): the
city and tile; shirts, thread, cloth, bolts, gold and sewing kits in the pack; pack weight and the carry limit
(strength x 4 + 30); the player's stamina and the mount's estimated stamina owed (mobility); the recall reagents
(pearl, mandrake, moss); the survey window and the best shirt offer seen; the thread vendors due here and by city, and
the minutes to the next restock of one bought out; tiles to the nearest loom; minutes since the bank box was last
open; the gold in the bank box (its piles, once it has been opened); the vendors passed over so far. Stage 3 fixes the network's normalized vector (caps, one-hot cities) in the Codex chapter that owns it,
with a version, built from these columns; a log of an older column set is mapped or not trained on.

## The toggle

- The Circuits tab: "Brain: Expert / Neural" beside the circuit picker, kept per circuit in the settings.
- The control port: `brain expert|neural|status`.
- In the client: `[brain expert` and `[brain neural` (the recorder's `[` command family).
- Neural mode with no weights loaded refuses to start the circuit and says so; it never falls back silently.

## Data

**Decision log.** Every decision point writes one row to `%LOCALAPPDATA%\uoaix-assist\decisions-<server>.csv`: time,
circuit, pass number, the step, legal or not, run or passed over, who decided (expert, neural, or Damian), the other
brain's answer (empty until stage 4 has two brains), and the state columns. A step's outcome is the next row's state
and the time between the two rows; an "Ended" row closes each run.

**Damian's play.** With Record on and no circuit running, `HandMove` labels his moves from what his client sends, as
rows by "damian": a sell reply (0x9F) holding shirts is SellHere, or SellTrip when his city differs from his last
labelled move's; a buy reply (0x3B) holding thread is BuyThreadHere or ThreadTrip the same way; a double-click on a loom
is Weave and on a sewing kit Sew; "bank" said (0x03) is Bank. A move repeating the last within 2 minutes is the same
move (a sale in batches of 5, a sewing session's menu picks). Each row carries the state 5 s after his previous move,
when he chose this one, not the state where the move happened.

**Training mode** (Damian, 2026-10-19: "a toggle in the ui that says turns on training. i run the circuit, and when
something wonkey happens of I don't like the decision, thats when i break the circuit and comment about the decision and
what was wrong or what could be more optimal"). The Circuits tab's Training box. While ticked, a Tailoring run he stops
himself (a step of his own, Stop, `circuit stop`; never an alarm) writes a row for the step it was running, by
"reject", and starts Record: each `[` note is a Comment row (its words in the note column), and his moves by hand are
the better choice in that state. Go ends the critique and the recording it started. In training a rejected step is a
strong negative reward for that state and step, and the hand moves after it are positive examples; the comments are
read by the engineer, who turns them into input nodes, rules for the move model, or a correction to a hand.

**Suggest mode** (Damian: "lets make that feed the neural network"). The Circuits tab's Suggest box:
before each step the circuit would run, it pauses for Do it or Skip; the answer is a row by "approve" or "reject". In
training a reject weighs as a Training stop does and an approve as a positive example of the expert's step in that
state.

**Score of a run.** Default: net gold per hour (gold sold minus thread and kits bought), with a deduction for each
circuit stop that was not asked for (Damian, 2026-10-19: "yeah, that is a good fitness").

**Reward of a move** (Damian, 2026-10-19: "per move is good"). Each move is scored on its own, from its row and the next
one, so a bad step is punished at the step that made it and not spread over the run: the gold the move gained (pack
gold plus bank gold, after less before, with gold carried in thread, cloth, bolts and shirts at their recent prices),
less the minutes the move took at the run's gold per hour, less a penalty for each vendor passed over during it (the
skips column) and for a trip that sold or bought nothing. A walk back to a tailor without the gold to buy thread costs
its minutes and gains nothing, and the reward says so at that move. The value network learns the sum of the rewards
that followed a state; the move prior weights each of Damian's moves by its reward. The penalty sizes are set in the
trainer, stated beside it, and tuned on the logged runs.

## Planning

Damian, 2026-10-19: "we need something that can anticipate a multi step process and work backwards to figure out
critical path to make better decisions early". A chooser that scores the next move alone repeats the myopia of the
rules, so the neural brain plans:

- **Move model.** Each move's effect on the state, by its rule (a sale turns shirts into gold, sewing turns cloth into
  shirts, weaving turns thread into bolts, a thread buy turns gold into thread, a trip changes the city, a wait moves
  the clock and the restocks), with its duration and its yield (shirts sold, offer paid, thread got) predicted by a
  learned network from the decision log. The rules give the shape; the network gives the numbers, which is what the
  hand-tuned constants got wrong.
- **Goal regression.** Damian, 2026-10-19: "final goal is i want gold in the bank, for that we sell shirts, for that we
  need bolts and sew kit, for that we need thread and loom, for that we need (small) gold and mobility". From that goal
  each needed resource is traced back to the move that makes it and that move's own needs: gold in the bank needs a
  sale, a sale needs shirts, shirts need bolts and a sewing kit, bolts need thread and a loom, thread needs a due
  vendor, a little gold and the mobility to reach it (stamina, the mount, recall reagents and runes). The chain with its predicted durations is the critical
  path; a move off it that ends before the path needs it fills the wait (thread bought on the way to a sale city, a
  restock that lands while sewing). Every move's needs are checked at the place it will run, before the walk there
  starts. Damian's case, 2026-10-19: "not 'go to tailor shop and forget gold to buy thread, so back to back, then back
  to tailor'. it would say, when i get there, what will i need, and recurse that from the end goal": a thread buy
  planned at a tailor needs gold at the tailor, so the bank comes before the walk, not after it. The planner's gate
  probe carries that case as a fixture.
- **Search.** Each pass searches move sequences up to a horizon (8 moves to start) from the current state through the
  move model, ordered by the goal regression, and scores each sequence by the gold it predicts per hour plus a learned
  value of the state it ends in (the gold per hour runs from such states went on to make). The first move of the best
  sequence is taken; the next pass plans again from what actually happened.
- **The bound** (R-COST): legal moves per pass are at most about 30 (9 moves, the city ones one per city), the search
  is a beam of fixed width over the horizon, and the whole plan is bounded in time per pass (100 ms to start, measured).

## Network and training

Three small dense networks in Codex, written as the town network is (`apps/uoaix/TownNetworkHill.md`: frozen weights in
a generated Codex chapter, a gate probe, hash receipts):

- **Move outcomes:** state and move in, duration and yield out; trained on every logged move, the expert's, the
  network's and Damian's alike (a move's outcome does not depend on who chose it).
- **State value:** state in, the run's gold per hour from that point on out; trained on the run score of each logged
  run, from every row.
- **Move prior:** state in, a score per move out; trained by imitation on Damian's labelled rows (the expert's weighted
  lower), used only to order the search so the beam tries his kind of move first.

Training runs offline on this box (CPU in codex-vm, or the town network's GPU trainer); the Assist only infers, and
never explores on uo1998: new plans are tried only through the move model, never by trying a move to see. Inference
(the networks and the search) is a Codex chapter compiled through the C# plug (`codex/plugs/csharp`, `emit-app.ps1`)
into a C# file the Assist's `Add-Type` line loads beside its own sources; the weights are a data file read at start.

## Grading

1. **Shadow.** In Expert mode the neural pick is computed and logged beside the expert's; a disagreement report per run
   names each pass where the two differ, with the state.
2. **Held-out prediction.** On logged runs not trained on, the outcome network's durations and yields against what
   happened, and the planner's predicted gold per hour against the run's; and agreement with Damian's moves against the
   expert's on the same rows. Neural mode is not offered until the planner's predictions hold and it agrees with him at
   least as often as the expert does.
3. **Live.** Neural mode with Damian watching, one circuit, scored by the run score against the expert's recent runs.

## Stages

1. A decision point in front of each step of `RunCircuit`'s tailoring loop with the expert brain deciding, and the
   decision log. Behaviour unchanged: the step bodies and their guards are moved by no line, which the CL's diff shows
   (only the decision call and the log row are added); then blu or Damian runs one circuit live, and the relay log's
   circuit lines read as before. No harness replays a circuit offline.
2. The recorder's labels from Damian's play.
3. The move model with its outcome network, the state value, the search, the trainer, the C# build into the Assist,
   and the toggle (Neural refuses without weights). The move prior follows once stage 2 has Damian's rows.
4. Shadow mode and the disagreement report.
5. Neural mode live on tailoring.
6. The trips' city as a brain decision (a city argument for `SellTrip` and `ThreadTrip`), then the other circuits, one
   at a time, each with its own step table.

## Open

- **Deferred before stage 3 until Damian raises it** (Damian, 2026-10-24: "drop the assistcircuitbrain till i bring it
  up again"). Built: stages 1 and 2 and Suggest mode. Tailoring is now `RunShops`: Damian's fixed
  order of 12 shops with sell, restock and make phases, and the steps `ToShop`, `Unburden`, `Fetch`, `OverflowTake`,
  `OverflowStore` and `Rest`. The step table, the trips and the planner above describe the free pass it replaced.
- On resumption, the brain decides only what the shop loop leaves open: when to switch between the sell, restock and
  make phases, whether to work a shop or pass it, and when to bank or use the overflow pile.
- How many recorded hours stage 3's training needs: measured, not guessed. The first count is the number of decision rows per hour
  of Damian's play once stage 2 runs.
