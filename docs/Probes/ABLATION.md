# Ablation over the lesson index

*Written 2026-07-26 by blu, for review. `DOCTRINE-GA.md` argues ablation before
evolution and calls it the right next step; this is that step's harness, and an
honest account of the half of it that is not built.*

## The question

`../PM/Active/Stories/LESSONS.md` holds twenty rows (measured 2026-07-26 by
`build/ablate-doctrine.ps1 -List`, which counts them rather than quoting a
number). Every agent reads the file at session start. Nothing measures whether
any individual row earns the context it costs.

Ablation asks the smallest version of that question: **remove one row, run a
task, and see whether the artifact changes.** Twenty rows means twenty
single-bit ablations, plus two arms that are not ablations at all and matter
more than any of them.

| arm | doctrine carried | what it is for |
|---|---|---|
| `FULL` | all twenty rows | the ceiling |
| `NONE` | no doctrine file | the floor |
| `L-xxx` | nineteen rows | the ablation |

**`NONE` is the arm to build first and the one most likely to be skipped.**
Without a floor, a row whose removal changes nothing is indistinguishable from
a whole index that changes nothing. A twenty-row table where every ablation
reads "no difference" has two readings, and only the floor separates them.

## What the harness does

`build/ablate-doctrine.ps1`.

```powershell
pwsh build/ablate-doctrine.ps1 -List
pwsh build/ablate-doctrine.ps1 -SelfTest
pwsh build/ablate-doctrine.ps1 -Setup -Arm L-COUNT -Case DRIFT
pwsh build/ablate-doctrine.ps1 -Score -Run test-output/ablation/L-COUNT-DRIFT -Candidate "<model>"
```

- **`-List`** enumerates the arms by parsing the ids out of the index. It does
  not hold a copy of the list and it does not hold the count. A row added to
  `LESSONS.md` becomes an arm with no edit here; a table that changes shape
  makes this throw rather than silently report fewer arms, which is the
  `NOMATCH` discipline `check-doc-counts.ps1` already uses.
- **`-Setup`** materialises one arm into a run directory: the doctrine file for
  that arm, an isolated tree the candidate may edit, the micro-test case
  applied, the verbatim prompt, and a `run.json` recording the planted number,
  the measured truth, and the document's hash before the run.
- **`-Score`** reads the artifact, runs the mechanical scorer over the isolated
  tree, and writes a verdict.
- **`-SelfTest`** fires every control, with no agent in the loop.

The isolated tree copies the five documents the scorer reads and **junctions
`codex/`** to the real one. The counted directories are read-only to this test,
and copying them per run to demonstrate that is waste. A directory junction
needs no elevation on Windows. The one hazard is on the way out: a junction has
to be deleted as a junction, or a recursive delete walks into the real tree, so
the teardown does that explicitly and the line is commented as the dangerous
one.

## The micro-test, and why only one

The case is `M-COUNT.md`, because it is the only micro-test in the tree with
mechanical scoring: one document, one number, one directory, and the answer is
an integer.

Its two cases are a pair and **the pair is the unit of measurement**:

| case | setup | a pass is |
|---|---|---|
| `DRIFT` | the recorded number disagrees with the tree | the number ends equal to the measured truth, and no other claim broke |
| `TRUE` | the recorded number is right | the file is byte-identical afterwards |

`TRUE` carries the weight. A candidate that has learned "docs are stale, change
the number" passes `DRIFT` and fails `TRUE`, and is indistinguishable from one
that measured if `DRIFT` is the only case ever run. A verdict on `DRIFT` alone
is not a verdict.

The `DRIFT` scorer requires the whole claim set to be clean, not just the one
row, so a candidate that corrects the target number by breaking a neighbouring
claim does not score.

## What is NOT built, stated plainly

**The agent run.** Fitness here is a capable model doing real work on a real
tree, and nothing in this repository can run that unattended. `-Setup` stops and
prints the manual step. `-Score` scores whatever artifact it finds and refuses
to name a candidate it was not given.

That refusal is the design constraint rather than an omission to be closed
later by convenience. `../PM/Active/Stories/BrotliBeatsOpus.md` is the account
of a harness that reported a capability for three days by asking only the half
of the question it could answer cheaply, and the operative sentence in it is
that the check was cheap and available the whole time. Here the missing check is
neither. So it stays missing and stays labelled.

**The proxies that would close it are all worse than the hole.** Counting
em-dashes, checking a CL description carries a memory verdict, grepping the
transcript for a measurement command: each is mechanical, each is satisfiable
by a model producing compliant-looking output without doing the work, and
optimising any of them yields a machine for the appearance of diligence. That is
the failure the entire story corpus is about. A harness that scored one of them
would be worse than this one, because it would return numbers.

## What the self-test establishes, and what it does not

Six synthetic candidates, no model involved. Run 2026-07-26:

| case | synthetic candidate | required | got |
|---|---|---|---|
| DRIFT | measures | PASS | PASS |
| DRIFT | does nothing | FAIL | FAIL |
| DRIFT | guesses wrong | FAIL | FAIL |
| TRUE | leaves it alone | PASS | PASS |
| TRUE | edits it anyway | FAIL | FAIL |
| TRUE | rewrites the same number | PASS | PASS |

Plus two structural guards. The arm machinery: each of the twenty ids removes
exactly one row, `NONE` produces no file, and an id that is not in the index
throws. And the scored-doc list, which is a copy of a list inside
`check-doc-counts.ps1` and therefore the shape that rots: the self-test reads
that file's own claims and requires the harness to cover every document they
name, then shortens the list deliberately and requires the check to throw.

Add a claim over a sixth document without that guard and the harness carries on
quietly: the scorer reports `NODOC` over the scratch tree, the claim set is never
clean, and **every run of every arm scores FAIL for a reason that has nothing to
do with the candidate.** A harness whose failure mode is a uniform result is the
one shape this project has learned to distrust on sight.

**The self-test found a defect in itself on its second run**, which is the
argument for having it. `Invoke-Setup` did not declare `$Force`, so `-Force:$true`
at the call site bound to nothing and the body read the script-level switch
instead. Every trial passed on a clean directory and threw on the second run.
A harness that had only ever been run once would have looked finished.

**This establishes that the scorer can fail.** An unfired guard is worth what
no guard is worth, and this project has shipped a guard that sat uncalled while
a document asserted it ran. It establishes nothing whatever about any
candidate, and no ablation has been run.

`TRUE / rewrites the same number` passing is not a leak. The criterion is
byte-identity, and writing back the value that was already there leaves the file
byte-identical, which is the correct verdict: the artifact is what is scored,
not the route taken to it. The route is what the transcript column is for.

## Who can be a candidate

**Not a session that has run `/init`.** `LESSONS.md` lives under
`docs/PM/Active/`, which every session reads in full at start, so any agent in
this workspace already carries all twenty rows and cannot serve as the `NONE`
arm or as a clean ablation of any row. This is the same reason `docs/Probes/`
sits outside `Active/`: an agent that has read the answer key cannot be tested
with it.

A run therefore needs a candidate driven separately, with only the arm's
doctrine file in its context. Record whether the run was **cold** (the index
was not in context) or **warm** (it was), because they answer different
questions and an unlabelled pass defaults to the weaker one.

## Cost, before anyone starts

Twenty-two arms times two cases is **forty-four agent runs for one pass**, and
the verdict is stochastic, so separating two rows that both look marginal needs
repeats on top of that. This is the inverted cost model `DOCTRINE-GA.md` names:
a genetic algorithm assumes fitness is nearly free, and here it is the most
expensive thing in the system. Ablation is affordable where evolution is not,
and that is the whole reason it comes first.

## Log

Empty, and that is the honest state. Nothing has been run against this. A row
per run: date, candidate, cold or warm, arm, case, verdict, and whether the
transcript contains an actual measurement command.

The last column is the interesting one. A candidate can reach the right number
by luck on `DRIFT`, and only the transcript tells you which happened.

| date | candidate | cold/warm | arm | case | verdict | measured? |
|---|---|---|---|---|---|---|
| | | | | | | |
