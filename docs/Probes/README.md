# Probes

*Proposed 2026-07-25 by blu, for review.*

A probe asks whether a lesson from `../PM/Active/Stories/LESSONS.md` **fires
for a model on a case that does not announce itself, and stays quiet on a
case that only resembles one.**

That sentence used to read "whether a model has a lesson", and "has" was
doing three jobs at once. Being able to state a rule is recall, and it is
free for anything that has read this repository. Assenting to a rule when it
is put to you is worse than free, because assent is what a model produces
when it wants to agree. Neither is what anyone needs to know. Every failure
in `../PM/Active/Stories/` was committed by an agent that could have recited
the relevant rule at the moment it broke it: `AgentCommunication.md` is an
autopsy of violating rule 10, written inside a document about rule 10.

So the thing being measured is a disposition, not a possession. This is not a
capability benchmark. Everything here is a question a capable model can
answer wrongly while writing fluent, plausible, well-structured prose, which
is the failure mode this project has paid for repeatedly.

## Why

Two uses, and the second is the one that pays the rent.

**Deciding whether a model is good enough to run unattended here.** The
efficiency curve is going to keep delivering cheaper models at similar
capability, and each one needs a go/no-go. Capability evals will not answer
it: every failure in `../Stories/` was committed by a model that could write
the code fine.

**Checking whether the lesson is still true.** A rule nothing evaluates rots.
That is the whole argument in `LESSONS.md`.

## The rules for writing one

**1. Every probe needs a paired control where the correct answer is the
opposite.** This is not a fairness check and it is not there to catch
overtriggering. **The control is what makes the measurement dispositional
rather than propositional.** With the positive case alone you cannot separate
a model that reasoned from one that pattern-matched on a keyword, so a
single-case probe measures recall however carefully it is scored. A model
that answers "the harness only validates the half it points at" to *every*
harness question has not passed `L-ORACLE`; it has memorised it. The pair is
the unit of measurement, not the case.

**2. Do not use the story's own case.** `BrotliBeatsOpus` is in the repo and
in the training data of anything that has seen the repo. Build a fresh
scenario with the same shape.

**3. State what a failing answer looks like, not only a passing one.** A
grader who has only seen the right answer marks generously.

**4. The corpus is public; the instrument is not.** Publish the stories, they
are the argument. Keep a held-out probe set unpublished, because a published
probe is a trained-on probe. This project regenerates the corpus every time it
fails at something new, which makes the held-out set renewable in a way most
benchmarks are not.

**5. Record whether the run was cold or warm, because they are different
claims.** *Cold* means `LESSONS.md` was not in the candidate's context.
*Warm* means it was. Cold answers "does this model bring the disposition
already", which is the go/no-go on a candidate model and the reason this
directory exists. Warm answers "can it apply the rule once told", which is
the question of whether `LESSONS.md` earns the context it costs every
session. Both are worth running and neither substitutes for the other. An
unlabelled pass defaults to the weaker reading: a warm pass says a model can
use a file it was handed thirty seconds ago, and says nothing about what it
would have done without it.

This is why `docs/Probes/` sits outside `docs/PM/Active/`. The init read
path runs through `Active/` (since 2026-07-28 via `LESSONS.md` and the
agent summaries rather than wholesale, but the ids and their one-line
lessons still land in every session), so a probe filed there would put the
answer key in front of the agent being tested and make a cold run
impossible to obtain.

**6. A good must be reachable.** If no candidate can pass, the probe
discriminates nothing and the work stops. Every probe states its passing run
explicitly, and if a capable candidate cannot achieve it the fault is in the
probe.

**7. Probes age out, and the retirement is part of the design.** Models
improve; a question that separated good from bad last year becomes one that
everything answers. A test that cannot distinguish is not evidence, which is
`L-FALSIF` pointed at the instrument. **When the last three distinct
candidates all pass, the probe is saturated: move it to `Retired/` with its
log.** It stops being run and stays readable, because a probe that used to
discriminate is itself a measurement of the state of the art.

Combined with rule 4 this closes: new failures in `../PM/Active/Stories/`
generate new probes, saturated probes retire, the set stays the same size
without anyone curating it, and what survives is whatever still separates.

## Two kinds of probe

A **question probe** presents a scenario and grades the answer. `L-ORACLE.md`
is one. Cheap, and it measures the lesson at the level where the lesson is
the topic.

A **micro-test** gives real work on a real tree and grades the artifact.
`M-COUNT.md` is the first. It costs more and it measures the level that
actually goes wrong: whether the rule fires when the prompt is about
something else. Prefer it where the lesson is mechanically scorable.

A micro-test should hit **the minimal surface that still separates good from
bad**. `M-COUNT` is one document, one number, one directory, no build, and
the answer is an integer. Anything larger buys noise, not discrimination.

## Format

One file per lesson id. Each holds: the setup given verbatim to the
candidate, the question, what a passing answer must contain, what failing
answers look like, and the control with its own expected answer.

Grading is human or model-assisted. There is no runner yet, and pretending
otherwise would be the exact defect `L-FALSIF` names.

## Index

| probe | kind | lesson |
|---|---|---|
| `L-ORACLE.md` | question | A harness validates only the half it points at |
| `M-COUNT.md` | micro-test | Never carry a count forward |

`DOCTRINE-GA.md` and `ABLATION.md` are not probes. The first is the argument for
selecting doctrine and the account of what blocks it; the second is the harness
for the ablation that argument says to do first, and `build/ablate-doctrine.ps1`
is its code.

## On arms, selection, and the genetic algorithm

The point of making doctrine optional per workspace (`.doc-counts` for the
count check; the same shape generalises) is that arms become comparable: run
the same micro-test against workspaces that differ only in what doctrine they
carry, and the difference is in the artifact rather than in anyone's opinion.

Selection over that is the obvious next thought, and there is more GA in this
tree than a search for the foreword chapter finds. There are two independent
implementations and only one of them has ever run against a real landscape.

`codex/foreword/ai/GeneticAlgorithm.codex` holds the general primitives
(tournament select, crossover, mutate, evolve). It is cited by exactly two
things, both tests: its own per-chapter compile test and
`foreword-all-compile`, which cites every chapter in the foreword. No product
code uses it, so it is built, compiled, and has never met a fitness function.

`apps/games/magic/Personality.codex` is the one that works.
`gen-random-population` seeds a population, `evolve-generation` sorts by
fitness, keeps the top half, breeds and mutates it, and `sort-by-fitness` is a
selection sort pairing population index with score index. It is driven by
`FormatSolver.codex`, which co-evolves deck genomes against a field
(`mutate-genome`, `mutate-splash`, `mutate-field`, `viable-field`), and pinned
by `FitnessSortDemo`, `GaSplashDemo`, `FormatSolverDemo` and `FormatRigorDemo`
inside a suite of roughly 130 demos.

**And it has already produced the exact failure this directory exists for.**
`sort-by-fitness` used to be a stub that returned the population unchanged.
The GA still ran. It still built generations, still bred, still mutated, still
reported a best-of-generation. It never selected on fitness at all. Everything
about it looked like evolution and nothing was being optimised.
`FitnessSortDemo.codex` exists because somebody caught that and pinned it, and
its prose says so in as many words.

That is `L-FALSIF` inside an optimiser, and it is the specific thing to carry
into any doctrine GA: **a selection step that silently does nothing is
invisible from outside, because a GA without selection produces output shaped
exactly like a GA with selection.** Whatever fitness function gets built, the
first test is not that it converges. It is that the ranking actually reorders.

The cost model is the other caution, and it is inverted from the one a GA
assumes: fitness here is an agent run, the most expensive thing in the system,
while a GA wants fitness to be nearly free. Population 20 over 30 generations
is 600 runs, and fitness is stochastic, so each individual needs repeats.

**Start with ablation, not evolution.** With twenty lessons, ablation asks
which rows change behaviour at all in about twenty runs and answers the
question actually on the table: is `LESSONS.md` carrying its weight, and which
rows are dead. Evolution earns its cost later, when the search is over
phrasings and interactions rather than presence and absence.

The harness for that is built: `build/ablate-doctrine.ps1`, described in
`ABLATION.md`. It enumerates the arms out of the index, materialises one, stands
up an isolated tree, applies the `M-COUNT` case and scores the artifact, and its
own controls are fired before it scores anything. It does not run the agent, and
that hole is the subject of `ABLATION.md`'s middle section rather than a gap to
be closed by convenience. One correction to the paragraphs above, which were
written before GAME-5: there are no longer two independent GA implementations.
`Personality` and `FormatSolver` are both instantiations of the foreword's
`ga-elitist-step` as of CL 10506, so the loop that had run against a real
landscape and the one that had never met a fitness function are now the same
code.

And whatever the optimiser, the fitness function has to be one that cannot be
satisfied by producing compliant-looking output, or the result is a machine
optimised for the appearance of diligence, which is the failure the whole
corpus is about. The honest form is a **seeded defect**: a task with a planted
fault and a known answer, scored on whether the fault was found. The stories
are a task bank for exactly that, because each one is a real defect with a
recorded cause.
