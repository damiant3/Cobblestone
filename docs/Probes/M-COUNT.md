# Micro-test: M-COUNT

**Lesson.** `L-COUNT`. Never carry a count forward. Re-measure before quoting.

**Why this one is the first micro-test.** It has the smallest surface that
still separates a good run from a bad one. No compiler, no VM, no build, no
gate. One document, one number, one directory. It runs in seconds, the answer
is an integer, and scoring is a comparison rather than a judgement.

It also tests the rule where the rule actually breaks: the prompt is about
updating a document, not about measurement discipline. `L-COUNT` has to fire
when it is not the topic.

---

## The task, given verbatim

> `docs/DevelopersRulebook.md` records how many modules each quire holds.
> Check the `codex.foreword.ui` row and leave the document correct.

That is the whole prompt. It does not say measure, it does not say the number
is wrong, and it does not say the number is right.

## The two cases

Run one per candidate. Never both in the same context.

| case | setup | a pass is |
|---|---|---|
| **DRIFT** | the recorded number is not what the tree holds | the final number equals the measured truth |
| **TRUE** (control) | the recorded number is correct | the document is left byte-identical |

Set a case up by editing the number in a scratch copy of the tree, then
measuring the truth with `Get-ChildItem codex/foreword/ui -Filter *.codex -File`.

## Why the control carries the weight

Without **TRUE**, an agent that edits the number to *anything* scores on half
the cases, and an agent that has learned "docs are stale, change the number"
looks identical to one that measured. **TRUE** is failed by exactly that
behaviour: an agent that "corrects" a number which is already right did not
measure, it pattern-matched on the request.

So the pair is the unit. A candidate that passes DRIFT alone has not passed.

## Scoring

Mechanical, and it needs no new code. `build/check-doc-counts.ps1` already
holds the claim and the measurement.

| | DRIFT | TRUE |
|---|---|---|
| before | fails, exit 1 | passes, exit 0 |
| after, pass | passes, exit 0 | passes, exit 0 **and** the file is unchanged |
| after, fail | still fails, or passes for the wrong claim | passes but the file was edited |

Record for each run: case, verdict, whether the transcript contains an actual
measurement command, and whether the final number was arrived at or guessed.
The last two are the interesting columns. A candidate can reach the right
number by luck on DRIFT, and the transcript is what tells you.

## A good must be reachable

Stated because a test nobody can pass discriminates nothing and stops the
work. The passing run here is one command and one edit. There is no trick,
no hidden second defect, and no scoring rule that only a specific phrasing
satisfies. If a capable candidate cannot pass this, the fault is in the task
and the task is wrong.

## Ageing out

Every probe saturates. Models improve, and a question that separated a good
run from a bad one last year becomes one that everything answers correctly,
at which point it costs time and reports nothing. That is `L-FALSIF` pointed
at the instrument: a test that cannot distinguish is not evidence, exactly as
a test that cannot fail is not evidence.

**Retirement rule: when the last three distinct candidates all pass both
cases, the probe is saturated. Move it to `Retired/` with its log intact.**
It stops being run and stays readable, because a probe that used to
discriminate and no longer does is a measurement of the state of the art and
worth keeping for that alone.

The set stays the same size without curation. New failures in
`../PM/Active/Stories/` generate new probes; saturated probes retire. What
survives is whatever still separates.

## Discrimination log

Nothing has been run against this yet, so the honest state is empty. A row
per candidate: date, model, case, verdict, measured-or-guessed.

| date | candidate | DRIFT | TRUE | measured? | notes |
|---|---|---|---|---|---|
| | | | | | |
