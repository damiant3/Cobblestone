# Selecting doctrine with the lifted GA

*Written 2026-07-25 by blu, for review. This is the destination of the arc
that lifted a generic GA core into `codex/foreword/ai/GeneticAlgorithm.codex`
(CL 10490) and gave the foreword a real mixer (CL 10493). It states what is
proven, what is not, and the one thing that blocks the rest.*

## The idea

`../PM/Active/Stories/LESSONS.md` holds twenty lessons. Which of them should
an agent carry? Today the answer is all of them, chosen by whoever wrote the
file, and nothing measures whether any individual row earns the context it
costs every session.

Represent a doctrine set as a bit vector, one bit per lesson: include or
exclude. Score a candidate by how an agent carrying exactly that subset
performs. Then select.

## What is already proven

**The genome is a shape the core already handles.** A doctrine set is
`GaChromosome { genes = <20 bits>, gene-count = 20 }` with `gene-min = 0` and
`gene-max = 1`. That is precisely the shape `codex/test/ga-core.codex` runs:
binary genome, twenty-four genes, population thirty, `ga-elitist-step` with
`ga-crossover` and `ga-mutate` supplied as the two operators. It solves
OneMax at generation seven. Nothing about the doctrine case needs new
machinery, and the reason is that OneMax was chosen for this test precisely
because it is the doctrine genome wearing a different name.

**The operators are right for this genome.** A uniform reroll over `[0, 1]`
is a bit flip, which is the correct mutation for a set-membership genome.
Single-point crossover of two doctrine sets is a coherent recombination.
Crossover rate is live (CL 10496), so exploration against exploitation is a
knob rather than a decoration.

**Arms are switchable per workspace.** `build.ps1` reads a `.doc-counts`
marker (CL 10488), which is the mechanism for varying what an agent carries
without varying anything else.

## What blocks it, stated plainly

**There is no automatable fitness function, and building the GA without one
would be the exact error `../PM/Active/Stories/BrotliBeatsOpus.md` records.**

Fitness here means: give an agent this doctrine subset, give it a task, and
score the artifact. Every part of that is available except the automation.
`M-COUNT.md` is a real micro-test with mechanical scoring, and it still needs
an agent to sit in the middle and do the work. Nothing in this repository can
run that unattended today.

Three consequences follow, and none of them is solved by more code:

**The cost model is inverted.** A GA assumes fitness is nearly free. Here it
is the most expensive thing in the system. Population twenty over thirty
generations is six hundred agent runs, and fitness is stochastic, so each
individual needs repeats to be ranked at all. That is not a tuning problem,
it is a different order of magnitude.

**A cheap proxy would be gamed.** The obvious shortcut is to score against
the probe set itself. That optimises doctrine for passing probes, which is
Goodhart, and it would produce a doctrine tuned to look diligent. The corpus
this whole directory is built from is a record of exactly that failure.

**A compliance-shaped fitness is worse than none.** Counting em-dashes,
checking a CL description has a memory verdict, checking a test was added:
all mechanical, all satisfiable by an agent that produces compliant-looking
output without doing the work. Optimising that yields a machine for the
appearance of diligence.

## The honest fitness, when someone builds it

**A seeded defect with a known answer.** Take a tree state with a planted
fault, give the agent a task that requires finding it, and score on whether
the fault was found and how much was spent finding it. That cannot be
satisfied by producing plausible prose, because the fault either surfaced or
it did not.

This project has an unusually good task bank for that, and it is the reason
this is worth doing at all: **every story in `../PM/Active/Stories/` is a real
defect with a recorded cause.** The Brotli one-directional harness, the
`sort-by-fitness` stub that left a GA selecting on nothing, the `0x01`
sidecar, the mixer with no downward fold. Each is a seeded-defect scenario
that actually happened, with an answer key written by the person who found
it.

## Do ablation before evolution

Twenty lessons means twenty single-bit ablations, and twenty runs answers the
question actually on the table: **is `LESSONS.md` carrying its weight, and
which rows are dead?** A GA searches a space of combinations, which is only
worth its cost once you know the individual rows matter and the interactions
between them are what remain unknown.

Evolution earns its keep later, when the search is over phrasings and
orderings rather than presence and absence, and when fitness has become cheap
enough to run hundreds of times.

## The first test of any fitness function

Before trusting a single generation of output, check that **ranking actually
reorders.** `sort-by-fitness` in `apps/games/magic/Personality.codex` was once
a stub returning the population unchanged: the GA still ran, still bred, still
mutated, still reported a best-of-generation, and selected on nothing at all.
A GA without selection produces output shaped exactly like a GA with it.

`codex/test/ga-core.codex` asserts this directly (`rank descends`), and its
sabotage run is recorded in CL 10492. Any doctrine fitness gets the same
treatment: run it against two deliberately different candidates and require
different scores, before running it against six hundred.

## Status

| piece | state |
|---|---|
| generic GA core, operators as parameters | built, CL 10490 |
| binary genome proven end to end | built, CL 10492, solves at generation 7 |
| crossover rate live and asserted | built, CL 10496 |
| mixer with usable low bits | built, CL 10493 |
| per-workspace doctrine arms | built, CL 10488 |
| lesson index with stable ids | built, CL 10483 |
| a micro-test with mechanical scoring | built, CL 10488 (`M-COUNT.md`) |
| ablation harness, everything except the agent run | built, `build/ablate-doctrine.ps1`, see `ABLATION.md` |
| **an automatable fitness function** | **absent, and it is the whole blocker** |
| a single ablation actually run | not started, and it needs a candidate that has not read the index |
| doctrine GA | premature until the two rows above exist |

The harness row is worth reading precisely. What is built is the arm
enumeration, the isolated tree, the case setup, the mechanical scorer and its
controls: everything that can be built without an agent. The agent run is
untouched and deliberately so, and `ABLATION.md` says why at length. A harness
that closed that hole with a proxy would return numbers, and returning numbers
it had not earned is what the whole corpus in `../PM/Active/Stories/` is about.

**No session in this workspace can be the candidate.** `LESSONS.md` is under
`docs/PM/Active/` and every session reads all of it at start, so every agent
here already carries all twenty rows and cannot serve as the `NONE` arm or as a
clean ablation of any row.
