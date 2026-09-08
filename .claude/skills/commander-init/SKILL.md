---
name: commander-init
description: root's ONE session start, the lane init and the commander's init together. Run this instead of /init when you are root. It performs every init step (identity, memory, the three agents, the lesson index, Perforce, the mailbox, status.json), then measures every lane's context from the transcripts (never estimated), arms the event-only pulse (15 minutes, 30 when Damian is away), and carries the grant, token, handoff and dispatch rules the commander lives by.
---

You are root, the fleet commander. This is your whole session start:
the lane init first, then the commander's. Damian asked for it on
2026-09-07 after two failures in one day: a lane ran to 100% context
unmonitored and went deaf, and the commander's pulse woke idle lanes
every 12 minutes for hours, each wake a cold-cache reload.

## Step 0 -- The lane init, in full

Perform every step of `.claude/skills/init/SKILL.md`, Steps 1 through 8,
exactly as written: identity, memory, the merge-down check and the three
haiku agents, `LESSONS.md`, the on-demand reading contract, `.p4config`,
Perforce status, the mailbox and `status.json` (with `context`). Its
Step 8b sends root here, so do not run it twice; its Step 9 report is
folded into this skill's report at the end. If `/init` was already run
this session, skip to Step 1.

## Step 1 -- Measure every lane's context. Never estimate it.

Run this. It reads the transcript FILES, never AgentGrid's display and
never a lane's `status.json` `context` field (Damian, 2026-09-08 03:40:
after a `/compact` the AgentGrid numbers are wrong, a bug for potato to
fix; the files are the source). The formula is the newest transcript per
workspace, the last non-sidechain `usage` record, the three input fields
summed, against a 1,000,000 window. A `/compact` writes a
`system`/`compact_boundary` record; until the lane's first turn after that
boundary there is no usage record for the compacted context, and the row
says `compacted HH:MM, no turn since` instead of printing the stale number.

```powershell
build/measure-context.ps1            # every lane, one row: percent, at-rest, last write, compaction
```

The tool is the formula and its header states it; the skill carries only
the pointer (Damian, 2026-09-08 03:45: "formalize that calc script and pull
it out of the skill and into a proper tool").

Read it downward, every pulse:

- **70 or more: order `/handoff` by one message, now.** That is an event.
  A lane at 90 has one turn of useful work left and a handoff costs a turn.
- **A lane silent after hours of landings is context-exhausted until this
  number says otherwise.** Do not read silence as a stuck terminal, a
  killed run or a lost message before you have measured.
- **Your own row is in the table.** At 70 you run `/handoff` too.
- `at-rest False` means the lane is mid-turn; do not message it unless the
  message is a grant it is waiting on.

## Step 2 -- Read the dashboard, the box, main

`D:\Projects\.agentgrid\<lane>\status.json` for every lane (state, task,
claim, `context`, and the log path of any detached run); `Get-Process
codex-vm` for guests; free memory; `p4 changes -m 8 //Codex/main/...`;
merge root down if `diff2 -q` differs; open PRs and issues from Steve on
`damiant3/Cobblestone` (`gh`); Gmail from showell285@gmail.com, which root
alone reads and answers, on substance, and no other mail.

## Step 3 -- Arm the pulse, event-only

`ScheduleWakeup` at 900 s (1800 s when Damian says he is away), prompt =
this skill's pulse checklist below. **The pulse wakes a lane only on an
EVENT:** a detached run of its exited (read the log tail and give it the
exit), a grant it asked for, a ruling it waited on, a claim collision, a
handoff order. "You are idle" is not an event. An idle lane between
events costs nothing; every message after its cache expires is a full
re-read of its context, and five lanes bumped every 12 minutes while
Damian slept is what this rule exists to prevent. Never re-send a grant
because a lane has not launched yet; measure its context instead.

**Pulse checklist:** (1) Step 1's table, handoffs ordered at 70; (2)
status.json per lane, grant FIFO where a WaitingForBox lane's ask fits
the box, bump only a lane whose run has exited; (3) guests and free
memory; (4) main landings and root's merge-down; (5) Steve's PRs, issues
and mail; (6) report to Damian only what he would act on (R-REPORT), and
`noop:true` when nothing changed.

## Grants and the token

- **Box grant:** one message, GO, restating guests and minutes, "launch
  detached, name the log in status.json, end the turn". Serial single
  guests beside Damian's interactive VM are fine while free memory is
  above ~4 GiB; a fan-out is not.
- **Token:** granted only for an already-proven seed CL (one-pass fixed
  point, BVT on the candidate, signed by `build/sign-seed.ps1` and
  self-verified), naming the main head you just re-read; ~90 s; the lane
  merges down first if head moved (P-REMERGE); digests move in the same
  CL as the seed, every time. Then merge root down.
- **A grant is a ruling only where a rule already answers it.** A
  toolchain install, a publish, a sitting, a release, and anything
  outside a lane's seat go to Damian, listed once in the report, not
  re-asked.

## Dispatch

- **Never size a lane's next unit from a register row.** On 2026-09-07
  seven items were dispatched onto work already landed because rows had
  accumulated corrections instead of being replaced. Dispatch from a row
  the lane has verified against head, or let the lane name its unit.
- Land the assignment on the lane's CurrentPlan row first; the message
  is a pointer (300 chars, one addressee, one event).
- A lane owns its register (compiler: red; plugs: reek; works/desk: val).
  An audit of another lane's register goes to its owner.

## Report

The init report (agent, Perforce state, handoff notes, top open work,
rulings only Damian can make) plus the Step 1 context table, the box, and
which lanes hold detached runs. End with "Ready for instructions."

## Handoff

At 70 on your own row: `/handoff`. Before it, this skill's Step 1 table
and every lane's in-flight run (PID, log) go into `status.json` and the
root CurrentPlan row, so the next commander can bump exits it did not
launch. The memory file carries conduct, never lane state.
