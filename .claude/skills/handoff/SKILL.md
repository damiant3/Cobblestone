---
name: handoff
description: Session wrap-or-continue decision point. Assess remaining context against remaining work, then either commit to a bounded next unit of work or execute the formal handoff so the next session resumes losslessly. Run when the user invokes /handoff, when context passes ~70% used, or at any natural milestone near the end of a work arc.
---

You are at a decision point: continue working or wrap the session.
Follow every step. The output of this skill is either a bounded
work commitment or a completed handoff — never an unstated middle.

## Step 1 — Inventory

Gather, with tools (do not answer from memory):

1. `p4 opened` — files open on disk right now.
2. `p4 changes -s pending -c <client>` and `-s shelved` — pending and
   shelved CLs.
3. Background tasks still running (builds, batteries, agents).
4. The current campaign/task list state (TaskList if in use).
5. Free context: use the most recent /context output from THIS
   session. If none exists, ask the user to run `/context` before
   deciding — a WRAP decision must never rest on a length-based
   guess. Only a CONTINUE decision may proceed on an estimate (the
   error is self-correcting: re-entry at the next milestone gets
   real numbers). If you must estimate anyway (user absent),
   calibrate against the actual window size — on a 1M-token window
   a session is rarely past 60% before several hundred tool calls —
   and state the estimate with its basis.

## Step 2 — Decide

Size the NEXT natural unit of work (one campaign stage, one CL, one
investigation — not the whole backlog). Estimate its context need
including gates and their outputs, then apply the rule:

    continue only if: (estimated need x 1.5) + 40k wrap reserve < free

The 1.5 covers debugging surprises; the 40k reserve guarantees the
handoff itself is never squeezed. If the next unit does not fit,
WRAP. Do not start work you cannot both finish and hand off.

Calibration warning (2026-07-08): a session wrapped at 55% used —
449k free — on a length-based guess. The multipliers above already
carry the safety margin; do not stack a conservative context
estimate on top of them. With real /context numbers, trust the
formula: a typical campaign leg (reads + edits + two gate builds +
battery) fits in 100-150k, so wrapping is only justified under
~250k free.

State the decision and the reasoning in one short paragraph to the
user. If continuing, also state the wrap trigger ("after X submits I
wrap regardless") and honor it.

## Step 3a — If continuing

Proceed with the bounded unit. When it completes (or the wrap trigger
fires), re-enter this skill at Step 1.

## Step 3b — If wrapping: the formal handoff

Order matters; shelve before anything else touches the tree.

1. **Shelve open work.** `p4 shelve -f -c <CL>` for every CL with
   open files (`-f` — a plain shelve on a previously-shelved CL
   silently does nothing). Never revert before shelving.
2. **Record gate truth.** For each shelved/pending CL, state exactly
   which gates ran and their results (build one-pass or two-pass,
   battery tally vs baseline, self-verify). A gate that did not run
   is recorded as NOT RUN, not assumed green.
3. **Verify seeds.** If any seed was installed or submitted this
   session, print the DEPOT digest (`p4 print -q -o tmp <seed path>`
   then hash) and record it. Workspace hashes do not count.
4. **Update the campaign memory file** (or create one): a RESTING
   STATE section at the top with — state of the world in two
   sentences; CL numbers submitted/shelved with one-line contents;
   seed digests; battery baseline; the exact next action with its
   resume recipe (unshelve which CL, run which command first); every
   trap hit this session worth not rediscovering. Update the
   MEMORY.md index hook if the description changed.
5. **Update `docs/Agents/<agent>-workplan.md`** top section — the
   fleet-visible version of the same, plus any OTHER-AGENTS warnings.
6. **Sweep for orphans.** Kill stray VMs/background tasks this
   session started; note any deliberately left running.
7. **Final message.** Resting-state summary the user can skim in
   thirty seconds, ending with: what the next session should do
   first, and that it is now safe to /clear or close.

## Rules

- The handoff is complete only when a fresh session could resume
  from MEMORY.md + the workplan alone, with zero conversation
  context. Test your writeup against that standard before finishing.
- Wrapping with work shelved and documented is success, not failure.
  A half-finished unit pushed past the context wall is failure.
- If the session is in the middle of a red gate or an undiagnosed
  failure, the handoff must say so in the first line of both the
  memory file and the workplan — never bury a red gate.
