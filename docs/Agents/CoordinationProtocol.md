# AgentGrid Coordination Protocol

How fleet agents coordinate builds and submits through AgentGrid so
they stop clobbering each other racing to main.

This document is written for the AGENTS (Claude Code sessions running
in the fleet workspaces). AgentGrid implements the granting side; its
source lives in the `//AgentGrid/main` depot
(`AgentGrid/Services/BuildQueueService.cs`), which also holds the
formal copy of this doc.

**The copy you are reading is the one that governs.** Two copies exist
and they are expected to drift, because the protocol gets corrected in
the middle of operations -- that is when a problem is visible and when it
has to be fixed. Edit this one. The `//AgentGrid/main` copy and the
AgentGrid source catch up in a reconciliation pass, done deliberately as
its own piece of work rather than as a submit-and-mirror tax on every
in-flight fix. Do not report the drift as a defect; do not stop to
mirror.

What a reconciliation pass owes you: anything the fleet retired here has
to actually leave the coordinator, not just this page. The merge-down
rationale is the worked example -- it was corrected here on 2026-07-29,
and AgentGrid went on typing the retired reason into terminals on every
grant until 2026-08-02.

## The Problem

A gate certifies the source it was run against, and a seed-affecting
change on main moves that source. Agent A and agent B both run gates
against main@100. A submits a seed-affecting CL 101. B's run is now
certifying source that no longer exists: B has to merge 101 down and
gate again, and the twenty minutes already spent bought nothing.

## The Fix

AgentGrid owns a single **build token** per project. Holding the token
means main does not gain seed-affecting changes underneath you, so you
never have to merge one down mid-run and start over.

**That is the whole purpose and there is nothing else in it.** The token
is not a lock on the shared build box, and it is not there because
`p4 copy` refuses an unmerged stream. Both used to be written here as
reasons and neither is one.

**It follows that the token is keyed to what your change TOUCHES.**
Seed-affecting work -- compiler source, the foreword, `seed/` itself --
takes the token, because landing it is what invalidates somebody else's
run and because somebody else's landing invalidates yours. **Docs, apps,
plugs and anything else that leaves the seed alone take no token at all**,
on any stream, including a copy-up to main. There is no gate for them to
invalidate.

**Every grant tells you to merge down from main first.** This is not a
judgement the coordinator makes for you and it is not conditional on
anything. The token holds main still from the moment you are granted it,
not from the moment you started working, so whatever landed while you
waited is still yours to take. Merge down, resolve, re-shelve, and only
then run gates: your gate run is against the real head, not a memory of
it, and from there the token keeps it that way.

(AgentGrid used to decide this for you by comparing main's head when you
asked against its head when you were granted. That measured the drift while
you *waited in the queue*, not the drift you accumulated while *working* --
and with an empty queue those two moments are milliseconds apart, so it
almost never fired. Agents were routinely cleared to build on streams tens
of changelists stale. The check is gone; the merge-down is now always.)

## Discovering Your Mailbox

AgentGrid writes a pointer file into each agent workspace root:

```
<your-workdir>\.agentgrid
```

It is JSON: `{ "agent": "blu", "coordinationDir": "D:\\...\\.agentgrid\\blu" }`.
Read it to find your coordination directory (your "mailbox"). All
protocol files below live in that directory. The file is in
`.p4ignore`; do not add it to a CL.

## Protocol Files

All files live in `<coordinationDir>` (yours -- never write to another
agent's directory; reading theirs is fine).

| File | Who writes | Meaning |
|---|---|---|
| `status.json` | agent | Your live state, shown in the AgentGrid UI |
| `build-request` | agent | You want the build token |
| `build-grant` | AgentGrid | You hold the token (JSON body has details) |
| `build-complete` | agent | You are done; token released |
| `outbox/<file>` | agent | A message you are sending (see Fleet Messages) |
| `inbox/<file>` | AgentGrid | Messages addressed to you |

The `.agentgrid` pointer also carries `fleetStatus`: the path of the rollup
described under **The Fleet Dashboard** below.

### status.json -- keep it fresh

Write this whenever your activity changes. It drives the status dot in the
UI, it is your row on the fleet dashboard, and it is how a manager agent
decides whether you are free to take work.

```json
{ "state": "Working", "task": "fixing lexer fuel cap", "claim": ["codex/compiler/Lexer"] }
```

Valid states: `Idle`, `Working`, `Building`, `WaitingForBuild`, `Error`.

`claim` is the ground you are standing on: the paths, files or subsystems
you are changing. One string or a list of them. It is what keeps two agents
off the same code -- AgentGrid compares every live claim against every
other, and when two overlap it tells BOTH of you so you can settle it
between yourselves with a fleet message. Neither of you is stopped and
neither is in the wrong; you are the two people who did not know. This is
the channel for the failure that cost the fleet twice in one week, when val
and blu built the same `fetch-tls` work an hour apart.

Three things make the claim worth writing:

- **Write it when you start, not when you finish.** A claim published after
  the collision is a record of one.
- **A claim only holds while you say you are on it.** Your claim is ignored
  while your state is `Idle`, so standing down releases your ground with no
  extra step and nothing to clean up.
- **Omitting the field keeps your last claim; an empty list releases it.**
  A status write that only updates `task` never silently drops your ground.

### build-request -- ask for the token

Write `build-request` containing JSON:

```json
{ "cl": 4712, "note": "codegen fix, seed rebuild" }
```

- `cl` -- your pending CL. **Shelve it first** (`p4 shelve -f -c 4712`).
  AgentGrid verifies the CL has shelved files (`p4 files @=4712`); a
  request for an unshelved CL is DENIED and the request file deleted.
  This is deliberate: a shelved CL means your work is safe in the depot
  before the gate dance starts, and others can unshelve it if asked to.
  **That same listing decides whether you needed the token at all.** If
  none of your shelved files sit under a path that can invalidate a gate,
  the request is answered NO TOKEN NEEDED and you are not queued. Nothing
  is lost: you were free to submit the moment you asked.
- `note` -- one line, shown to the human. **Put the real duration in it when
  you are asking for more than one gate pass.** A seed rebuild is three gate
  passes by construction, and a pass measured 774 s on 2026-08-15, so the
  cycle is roughly 40 minutes rather than the "about 12" that has been quoted
  from a single pass. Re-measure rather than copying that number forward
  (L-COUNT): samples on this box have ranged 8 to 13 minutes a pass. The
  failure mode is under-quoting, which surprises the queue mid-hold.

An empty `build-request` file is also accepted (legacy form): you get
queued with no Perforce verification. Prefer the JSON form.

Do not poll-spam: write the file once and wait. If you need to cancel,
delete your `build-request` before it is granted, or write
`build-complete` after it is granted.

**This was broken and is fixed as of the stable build deployed 2026-08-16
06:12 (AgentGrid CL 15661).** val measured it that morning: a `build-request`
deleted at about 02:58 was granted anyway at 03:17:33, 19 minutes later, so
it was not a poll race -- the queue held a COPY of the request and never
looked at the file again. The coordinator now re-checks that the file still
exists at the moment of the grant, drops the entry if it does not, tells you
it did, and passes the token on. Deleting the file is a real cancel again.

**Write `build-complete` anyway the moment an unwanted grant arrives.** That
is still the only way out of a grant that has already landed, and it is worth
knowing on its own: if you get a GO for a CL you no longer intend to land, do
not gate it to be polite. Releasing immediately is what the queue behind you
needs.

## How to wait (Damian, 2026-08-17)

Everything AgentGrid and the fleet send you arrives as a line TYPED INTO
YOUR TERMINAL. A typed line is only read when your session is at its
prompt. So the rule for waiting, for a token, for a reply, for Damian, is
one rule: **waiting means your turn has ended and the terminal is at the
prompt.** Set `status.json` to what you are waiting on, and stop.

What that forbids, each of which has blocked a GO or a message this week:

- **No foreground wait loops.** No `Start-Sleep` polling in a tool call,
  no `-Wait N` watcher run in the foreground. A tool call that sleeps
  holds the prompt shut for its whole duration and every typed line
  queues behind it.
- **No long foreground commands while queued or waiting.** A gate, a
  battery, a bed run or a VM boot goes through `run_in_background`, and
  then the turn ENDS; the completion notification wakes you. If a
  command must be foreground it is under two minutes.
- **No open dialog while waiting.** A permission prompt or a question
  left open eats the next typed line as its answer. If you need Damian,
  say so in one line and in `status.json`, and end the turn at the plain
  prompt.
- **`state` tells the truth.** `WaitingForBuild` when queued,
  `Idle` when there is nothing to do, `Building` only while a background
  gate is actually running. `Working` while the terminal sits at a prompt
  waiting for someone is a false report (rule stated 2026-08-16, restated
  here because it is the same failure).

The shape of a correct wait: request or send, write `status.json`, end
the turn. The next thing that happens to you is a typed line or a
background completion, and either one starts a new turn cleanly.

**A landed report does not end the lane (Damian, 2026-08-18).** Waiting is
for a token, a reply you asked for, or a ruling only Damian can give. Landing
an item is none of those: send the one-line landed message TO A PEER who is
waiting on the work -- landed is not one of the commander's three events, so
this line is not licence to send him one (see "When the addressee is the
COMMANDER" below) -- take the next
item named in your CurrentPlan row or your register in its order, and keep
working. Do not end the turn to wait for the commander to say "go"; four
lanes sat at the prompt for fifteen minutes on 2026-08-18 with their next
item already written in the plan, and every one needed a nudge that cost
a message. If the row is empty, that is a **question** event (one line to
the commander: "lane empty, draw?"), and the wait after THAT is a real one.


**A mailbox file does not wake a session, and the commander learned it the expensive way (red, 2026-08-21).** Every lane sat idle at its prompt for hours while red wrote `build-request`-shaped notes into their inboxes and waited for replies; the messages were read only when each session next happened to run a tool. `SendMessage` to the session name is an interrupt and wakes the receiver now; `ListAgents` says busy or idle; a `status.json` age is a proxy that lies in both directions (fester read DARK for 91 minutes while busy). A commander that needs a lane to move reaches it through `SendMessage` and reads the depot for what landed; the mailbox is for the coordinator protocol, not for waking anyone.

## What Happens Next

AgentGrid polls every second and answers **two ways at once**: it
writes files in your mailbox, and it types a `[AgentGrid coordinator]`
message directly into your terminal. You will see the message as user
input in your session. Obey it.

1. **DENIED** -- your CL has no shelved files. The request file is
   deleted. Shelve, then drop a new request.

2. **NO TOKEN NEEDED** -- your shelved files touch nothing that can
   invalidate a gate: docs, apps, plugs, workplans. The request file is
   deleted and you are NOT queued. Go submit, on your dev stream or
   copying up to main; nothing is waiting on you and nothing you do here
   can invalidate anyone else's gate run. This is not a refusal of
   service, it is the queue declining to charge you for something that is
   free. It is rule 1 enforced rather than restated. If you are certain
   the CL does affect the seed, tell Damian so the project's seed paths
   can be corrected -- do not re-request, you will get the same answer.

3. **QUEUED** -- someone else holds the token. The message names the
   current holder and your position. Keep working on something else
   or wait. Do NOT run gates or submit to main while queued.

4. **GO with MERGE** -- `build-grant` appears in your mailbox and the
   terminal message says GO. The grant body is JSON:

```json
{
  "grantedAt": "2026-07-13T14:02:11",
  "mainHeadCl": 4721,
  "mergeDownRequired": true,
  "message": "..."
}
```

   `mergeDownRequired` is **always true**. **Merge down from main into
   your stream, resolve, and re-shelve BEFORE running gates.** Then
   gates, then submit. Do not skip it because you "just merged" or
   because main "looks unchanged" -- other agents land CLs while you
   work, and an unmerged stream cannot copy up. Your build must be
   against the head that exists when you build, not the head that
   existed when you last looked.

   `mainHeadCl` is informational: it is main's head at the moment you
   were granted the token. It is not a condition to evaluate.

5. **CANCELLED / REVOKED** -- the human pulled your queued request
   (CANCELLED) or your held token (REVOKED) from the AgentGrid UI.
   Your mailbox files are cleared for you. Stop immediately: no gates,
   no submit. Shelve, address whatever prompted the human to step in,
   and drop a new `build-request` when the CL is ready.

6. **Release the token.** When your submit lands (or you abandon the
   attempt), create `build-complete` (empty file is fine) in your
   mailbox. AgentGrid clears your grant and hands the token to the
   next agent in line. There are exactly two ways out of a hold: you
   submitted and released, or you released. See rule 8.

## The Fleet Dashboard

Everything above is your own mailbox. The **rollup** is the whole fleet in
one file, rewritten by AgentGrid every second, and its path is the
`fleetStatus` field of your `.agentgrid`:

```powershell
$fleet = Get-Content (Get-Content .agentgrid | ConvertFrom-Json).fleetStatus | ConvertFrom-Json
$fleet.agents | Where-Object { $_.state -eq 'Idle' } | Select-Object agent, contextPercent
```

Read it; never write it. It carries, for every agent on this Perforce main:
`state`, `task`, `claim`, `buildState`, who holds the token, who is queued
behind them, and `conflictsWith`.

**The unit is the Perforce main, not the project.** A fleet larger than one
grid is split across several AgentGrid project configs that all submit to
the same main, and they share one rollup, one token and one queue. Every
agent racing you is in this file.

**Two halves, deliberately not merged into one "state".** `state`, `task`
and `claim` are what an agent SAYS. `atRest`, `terminalRunning`,
`contextPercent` and `lastActivityUtc` are what AgentGrid OBSERVES, off the
terminal and the Claude Code transcript. A crashed or wedged session goes on
saying `Working` forever, so anyone assigning work off the self-report alone
hands items to agents that are not there.

**`atRest` is the field to assign off** (AgentGrid CL 16260). It is true when
the agent's last turn ENDED, read from `stop_reason` on the last assistant
record of its transcript: `tool_use` means Claude Code is still working the
turn, anything else means it is sitting at its prompt. It needs no
cooperation from the agent and no threshold, so an agent ten minutes into one
gate run reads as working rather than tripping an inactivity timer. It is
routinely the opposite of what the agent says -- when this landed, `fester`
said `Working` and was at rest, and `reek` said `Idle` and was mid-turn.

**It is also the answer to "will a typed line be read right now".** Under
**How to wait** above, a typed line is only taken in at the prompt; `atRest`
is exactly that condition, observed rather than promised. A message sent to
an agent that is not at rest waits for its current turn to finish.

Prefer it to the older reading of "idle", which was a self-reported `Idle` OR
a `lastActivityUtc` that had not moved in a long time. That still works and
is still honest, but it cannot tell a long tool call from an ended turn, and
that is the distinction that decides whether you may interrupt.

(`terminalRunning` is what the AgentGrid instance holding the coordinator
lock can see. If the terminals were launched from a DIFFERENT instance, it
reads `false` for agents that are perfectly alive. `lastActivityUtc` does
not have that problem: it comes off the transcript on disk and is the same
for every instance. Trust the quiet time, not the flag.)

Use it to answer: who is free, who is standing on the ground I am about to
take, who is ahead of me in the queue, and who has burned so much context
that handing them a large item is a waste. **That is what it is for -- so
those questions cost nobody a turn.** Asking the fleet instead puts the
question in five terminals, spends five agents' attention, and returns five
answers of five different ages.

## Rules

1. **Take the token for seed-affecting work, and only for that.** Gate
   and land compiler source, foreword or `seed/` under the token, because
   that is the class of change a gate result depends on. Going around it
   there recreates the race.

   **Everything else needs no token**, on any stream. Docs, apps, plugs,
   workplans: none of them can invalidate a gate, so none of them belongs
   in the queue -- not on your dev stream, and not copying up to main
   either. Damian, 2026-07-28, on an agent queueing to land a workplan:
   *"you don't need a build gate for a workplan."* And 2026-07-29, on the
   scope: *"you don't need a token for non-seed changes, e.g. docs, apps,
   plugs, etc. only things that would invalidate a gate running effort."*

   The test is not "does this touch main", and it is not "am I about to
   run something". It is **"would this invalidate a gate run, or could a
   gate run be invalidated under it"**.
2. **Shelve before you request.** The gate dance (shelve, revert,
   sync -f, clean, unshelve, build) already requires it; the protocol
   just checks you did it.
3. **Always release.** A crashed gate run still needs `build-complete`.
   If your session dies, the human can kill your slot in AgentGrid,
   which also releases the token.
4. **One request at a time.** A second `build-request` while queued or
   building is ignored.
5. **Merge down, every grant, no exceptions.** It is a precondition of
   the token, not a conditional step: the token holds main still from the
   moment you are granted it, not from the moment you started working, so
   the merge is how your source becomes the head the token is protecting.
   Gate after it, never before. If you cannot complete
   the merge (conflicts you cannot resolve), write `build-complete` to
   release the token, set `status.json` to `Error` with a task note,
   and tell the human.
6. If `.agentgrid` does not exist in your workspace root, AgentGrid is
   not managing this workspace -- proceed without the token.
7. **Delete the shelf before you submit.** Perforce refuses to submit a
   changelist that still has shelved files:

   ```
   Change 7626 has shelved files -- cannot submit.
   ```

   The protocol makes you shelve, so *every* run ends here. After you have
   unshelved and gated, `p4 shelve -d -c <CL>` and then submit. This is
   safe and it is not the same as reverting: deleting the shelf discards
   the depot *copy* of your work and does not touch your workspace files.
   The files you gated are the ones on disk, and they are the ones that
   go in. (Found the hard way on the first run of this protocol.)
8. **Submit or step aside.** The token buys ONE attempt at the gate
   dance and the submit. It is not a workspace lock and it is not a
   license to keep coding. The moment you learn your CL is not landing
   as-is -- a red gate, a bug to fix, a test to write, anything that
   puts you back in an editor -- shelve what you have, write
   `build-complete`, and do that work WITHOUT the token. When the fix
   is ready: re-shelve, drop a new `build-request`, and wait your turn.
   Main may move while you fix; the merge-down in your next grant is
   the protocol working, not punishment. "It's a one-line fix" is how
   one agent's debugging session becomes three agents' idle afternoon.
   The test: if the next ten minutes are an editor and not `p4 submit`,
   you should not be holding the token.
9. **Publish your claim, and read the fleet's before you pick work.** Put
   the ground you are taking in `status.json`'s `claim` when you START, and
   check the rollup for an overlap before you start rather than after. The
   token serialises BUILDS and nothing has ever serialised WORK: two agents
   can spend a day on the same item without either doing anything wrong,
   because neither had any way to know. That is not a build race and the
   token was never going to catch it. A claim costs one line in a file you
   are already writing.

9. **Warm the caches BEFORE you request.** The hold is a mutex on the
   whole fleet, so a one-time cost paid inside it is paid by everyone
   in the queue. A workspace that has not gated recently has no cached
   plug binaries (`build-output/` is p4-ignored), and the gate's
   plug-smoke phase then BUILDS four plugs from source inside your
   hold: measured 2026-08-06 on the CL 13708 hold, 1,045s of a
   1,394s gate, on a hold Damian had to ping. Every second of it was
   tokenless work. Before writing `build-request` on a cold workspace
   (above all a copy-up client), pre-build the plug CDXs
   (`pwsh codex\plugs\<p>\build.ps1` for typescript, python, rust,
   ptx) and sync the workspace. The test extends rule 8's: work that
   would run identically WITHOUT the token belongs before the
   request, not inside the hold. (Damian's ruling, 2026-08-06: "the
   problem isn't the time it took, it was the mutex it held.")

## Example Session (agent "blu")

```powershell
# work done, CL 4712 ready
p4 shelve -f -c 4712

$mbox = (Get-Content .agentgrid | ConvertFrom-Json).coordinationDir
Set-Content "$mbox\build-request" '{ "cl": 4712, "note": "lexer fuel cap" }'
Set-Content "$mbox\status.json" '{ "state": "WaitingForBuild", "task": "CL 4712 queued" }'

# ... wait for the [AgentGrid coordinator] GO message in the terminal ...
# (or poll: Test-Path "$mbox\build-grant")

# ALWAYS merge down first -- every grant, no exceptions
p4 merge -S //Codex/blu -r
p4 resolve            # semantically, per file -- see PerforceProcess.md
p4 submit -d "merge down from main"
p4 shelve -f -c 4712  # re-shelve on top of the merged stream

Set-Content "$mbox\status.json" '{ "state": "Building", "task": "gates for CL 4712" }'

# the gate dance -- on-disk files are the source of truth for the build
p4 shelve -f -c 4712        # your work is safe in the depot
p4 revert //Codex/blu/...
p4 sync -f //Codex/blu/...
p4 clean codex/... apps/...  # paths and what reads each: PerforceProcess P-STRAY
p4 unshelve -s 4712 -c 4712
p4 opened                   # LOOK at it before you build
p4 status                   # a dropped add: the preflight warns, it does not fail
p4 diff -du //Codex/blu/... # PATHS, not -c <CL>, which is not a diff option (P-DIFFC)

build/build.ps1 -Internal   # gates (CLAUDE.md R-GATE; the bare form is release only)

p4 shelve -d -c 4712        # rule 7 -- or the submit is refused
p4 submit -c 4712

New-Item "$mbox\build-complete" -ItemType File
Set-Content "$mbox\status.json" '{ "state": "Working", "task": "post-submit cleanup" }'
```

## What the token is actually for

**The token prevents colliding BUILDS on the same code. It is not a lock on
the depot, and it is not permission to work.**

- **Docs-only changes do not need the token.** Edit them directly on main and
  submit. No gates run, so there is no race to prevent. A workplan, a backlog
  entry, a design note, a README -- just submit it. Rule 1 is about gates and
  the code they gate, not about every `p4 submit` in the depot. **AgentGrid now
  enforces this** rather than asking: it reads your shelved file list and
  answers NO TOKEN NEEDED instead of queueing you. This paragraph and rule 1
  both already said so in Damian's own words, and the queue kept filling with
  docs anyway, which is why it is now mechanical.
- **Code that runs gates needs the token**, including a copy-up, because a
  copy-up is a submit of gated code to main and that is exactly the race.
- **Fixing broken code does not need the token -- and must not hold it.**
  Red gates, debugging, writing tests, "just one more thing": all of it
  happens outside the hold (rule 8). The token is for landing finished
  work, not for finishing work.
- **A hold has a clock (Damian, 2026-08-31): over 20 minutes is a WARNING,
  over 25 is SEVERE, 30 is a bug.** The dashboard's `tokenHeldMinutes` is
  the reading and the commander's pulse watches it. A hold that long means
  the gate was started under the token instead of before the request, or a
  red gate is being fixed under it (rule 8): shelve, write `build-complete`,
  do the work, re-request. The measured `-Internal` gate with nothing
  implicated was 186 s on 2026-08-20, so a 20-minute hold is not a slow gate.

## The token does not cover RAM: ask the fleet before a heavy run (Damian, 2026-09-01)

**The box is one DIMM down and stays that way: 16 GB is all there is**
(one KSD516G72C34VTR at Controller1-DIMMB2, 15.8 GiB visible, measured
2026-08-31 and again 2026-09-01 after the fleet resumed). The token
serialises GATES on the same code; it says nothing about two lanes each
booting 3072 MB guests at the same time, and on this box that overcommit is
what kills guests with a plausible-looking codegen error (`OperatorsManual.md`,
"The compile batch asks for 12 GB").

So the rule, in Damian's words: be conscientious about running builds, and
**check with the other agents before launching big tests.** Concretely:

- `-Jobs 8` is the default again (Damian, 2026-09-01, on the measurement
  below), and its condition is THIS section: one heavy run on the box at a
  time. The 4 was conditioned on two lanes' guests overlapping; with that
  forbidden, 8 costs the floor and buys a third of the gate.
- **`build/test.ps1 -All` and every full-battery run are PROHIBITED except
  for release builds (Damian, 2026-09-01; `CLAUDE.md` R-GATE).** The fleet
  is on BVT only plus focused test passes: the `-Internal` gate, then the
  specific tests a change touches, one at a time. There is no "ask first"
  path for a battery; the ask below is for the runs that remain.
- Before any remaining run that boots more than one guest (a focused
  parallel harness, a release proof in red's hands), send ONE message to
  each lane that could be running guests, within the budget, and wait for
  the answers. A single `-Internal` gate is not a heavy run and needs no
  ask; two gates at once from two lanes is exactly the overlap to avoid, so
  a lane about to gate says so in its `status.json` and a lane reading
  `Working` with a gate in another lane's status waits.
- `status.json` is the shared reading: name the run you are about to start
  and its guest count, so the ask is answerable from the dashboard.
- **Waiting for a slot is not idle time (Damian, 2026-09-01, the same
  morning: "the agents are very idle, we need to keep the team working").**
  A slot ask is non-blocking: keep working the next item in your lane
  while the box is busy, and gate once per arc with the CLs batched, which
  is the arc rule below applied under a RAM budget. A single-guest run (a
  focused test, one hosted binary, one app) needs no ask at all.
- **Overlap is decided by CONSUMPTION, not by ceiling (L-REQUEST), and the
  gate's consumption is NOT one number.** A self-compile guest peaks near
  1.1 GB, but the `-Internal` gate's test-compile phase at `subject: FULL`
  (every compiler change implicates all ~1,488 test chapters) runs the
  compile BATCH at four slots, and a batch guest scales with its slot:
  ~2.1 GB at 416 chapters (`OperatorsManual.md`, "The compile batch asks for
  12 GB"), so that phase is ~8 GB by itself. A Renode subject bounces to
  ~1 GB at peak. **Measured 2026-09-01 07:29 (fester, then root): a
  FULL-subject gate running ALONE, five codex-vm at 5.2 GB of working set,
  took the box to 0.39 GiB free.** That is the whole box on one gate, with
  the six agent sessions and the rest of the host holding the balance.
  **Sampled every 3 s through a second FULL gate alone (red, 08:16-08:21,
  five sessions after fester was killed): guest working set peaked at
  5.89 GB over 5 guests, floor 0.86 GiB free, and the four
  `test-compile-batch.ps1` DRIVERS held 400-900 MB EACH (2.5 GB) beside the
  guests they feed**, because each reads its whole batch capture as a
  `byte[]` and then a Latin-1 shadow `string` of it (`:135-137`, UTF-16, so
  3x the capture in memory for the parse). The host half of a gate is not
  free either; the reduction is CurrentPlan's item.
- **`-Jobs 8` MEASURED on this box (blu, 2026-09-01 08:46-08:53, one FULL
  gate alone, 103 samples at 3 s, with the driver byte-scan fix already
  in): GREEN, `SUT === stage1` in one pass, no refusal line. Elapsed 412.4 s
  against the 620.5 s baseline at 4 (test-compile 122.1 s against 180.0 s).
  Floor 0.44 GiB free. Peak single guest 2,140 MB; peak total guest working
  set 5,995 MB with only FOUR guests up at that moment, so the heavy-phase
  guests are ~2.1 GB each, not the ~1.0 GB quoted earlier from a light
  phase (withdrawn). Max concurrent 7 guests, phases overlap.** The 4-slot
  baseline floor was 0.86 GiB with one more session alive and the old
  drivers. So 8 buys 33 per cent of the gate with the whole headroom: a
  floor of 0.44 GiB is one green sample, which is what CLAUDE.md's `-Jobs`
  paragraph says an overcommit looks like until it kills a guest and reads
  as codegen. The criterion set before the run (floor above 1 GiB) said
  keep 4; **Damian overrode it with the numbers in front of him (09:04):
  "-jobs 8 runs, is barely more memory now than -jobs 4 and we need that
  speedup." 8 is the default**, conditioned on one heavy run at a time.
  Levers that raise the floor: the driver reduction's second half
  (streaming, not just the shadow string; CurrentPlan gate-RAM 1b), fewer
  live sessions, and the second DIMM.
- **RE-MEASURED WITH THE SMALL DRIVERS (blu, 2026-09-01 09:19-09:26, same
  gate shape, FULL 1,490 chapters forced, alone, 102 samples at 3 s):
  green, 412.1 s, floor 2.50 GiB free, peak guest 2,139 MB, 8 concurrent,
  peak DRIVER 183 MB (was 812).** Same slots, same speed, 5.7x the headroom,
  and the reason is named: `test-compile-batch.ps1` built the whole batch
  input in a StringBuilder and wrote it with `ToString()` (the batch text
  resident twice at the write, plus a 512 KB builder and a full copy per
  chapter); it now streams the input through a StreamWriter and nothing
  holds the batch. The capture bytes everyone looked at first were 41 MB of
  it. So `-Jobs 8` clears the 1 GiB bar it failed at 08:53, by 2.5x, for a
  reason rather than by luck; the one-heavy-run-at-a-time condition still
  stands, because 2.5 GiB is not two gates. So:
  a gate whose change touches the compiler runs ALONE on this box, nothing
  else booting; only a cite-scoped gate (apps, docs,
  tests) may overlap a single-guest run. `WHvSetupPartition 0x800705aa` is
  the refusal signature. A run that dies is re-run alone, and the death is
  reported with its time.
- **A gate that dies at `subject: FULL` with no refusal line is a
  TOOL-CALL CHILD dying, until proven otherwise (2026-09-01, blu; the
  commander got this wrong first).** Two `-Internal` runs died at that
  line the same morning, both launched as `run_in_background` tool-call
  children; the first ran with nothing else booting, both gate logs were
  byte-identical (3,672 bytes) and stopped 24 and 34 s BEFORE the harness
  recorded `[killed]`, which appears in the task file and in neither log.
  The commander published RAM as the cause (main 20927) from the
  arithmetic above and retracted it within the hour on that evidence:
  RAM pressure does not reproduce a log to the byte, and one of the two
  runs had no neighbour to blame. This is `OperatorsManual.md`, "A gate run
  as a tool-call child dies with the session, and it reads as host
  trouble", measured 2026-08-28 and repeated here. **Launch every gate
  DETACHED via `Start-Process`, keep the PID and the log path in
  `status.json`, and before calling a died-mid-phase gate RAM or codegen,
  ask whether its parent outlived it.**

This rule dies with its condition. When the second DIMM is back and
`Win32_PhysicalMemory` shows two rows, re-measure and delete this section.
**That is weeks away (Damian, 2026-09-01): the replacement is an RMA the
manufacturer will contest, and the part that cost 100 now lists at 750. Plan
on 16 GB for the rest of September.** So this section is not a stopgap: the
serialization above is the working shape, and the host-side footprint
reductions in CurrentPlan ("Gate RAM") are the lever that actually moves the
floor. Do not schedule anything on the assumption the DIMM returns first.

## Fleet Messages

**The depot is not a message bus.** A note from reek to red used to mean:
submit to your dev stream, copy up to main, wait for red to merge down.
Two merges and minutes of latency to deliver one line, and every one of
those merges is a chance to clobber somebody's file. Your mailbox is on
local disk and every agent in the fleet can reach it.

### A message is a pointer, not a container (Damian, 2026-08-16)

**Every message, directed or broadcast, is a sentence or two at most: one
claim, and where the detail lives.** That is still the single-line `text`
field described below -- a sentence or two packed into one line, not a
paragraph, and never a wall. The detail already has a home -- a CL number, a
section of the doc that owns the subject, a `file:line` -- and the message
NAMES that home instead of reproducing it. "Gpt geometry guard
landed, red 15556, account in `ExaminersAssay` 'The Foreword GPT Geometry
Guard'" is the whole message; the arms, the ablation and the fixture recipe
are in the two places named, read when the reader gets there.

The reason is the arithmetic of a fleet. Every recipient reads every message
in full, in their own loaded context, mid-task. A wall of prose sent to one
lane is that wall paid by everyone it reaches, and almost none of it changes
what any of them does next. Restated detail is also detail that now exists
twice and drifts from the CL that owns it, which is the mechanical failure
the workplan outbox died of.

**The test: would the reader act the same having read only the pointer, and
opened the CL or doc if and when it became load-bearing?** If yes, the prose
was swamp. This binds a directed note as hard as a broadcast: "taking item
17, claimed in the file-claims table, reply if it is yours" needs no
paragraph of what the item is, because the register already says what it is.
When a message starts to argue a case, the case belongs in the CL
description or the doc, and the message shrinks to the sentence that points
at it.

### The route: agent-to-agent messages go by the cross-session channel (Damian, 2026-08-31)

**A message from one agent to another is sent with the harness's
`SendMessage` tool, addressed by the name `ListAgents` shows (for example
`cobblestone-reek-ea`).** It arrives in the recipient's conversation
asynchronously, as a cyan `<cross-session-message>` notice, without touching
the input buffer. Damian's preference, stated in those words, and the measured
reason: the outbox route TYPES a `[fleet message from X]` line into the
recipient's terminal, and on 2026-08-28 (nine messages) and 2026-08-31 (three
of three) those lines stranded unsubmitted in the input buffer while every
sender-side receipt said delivered (GRID-5; the live `stable` coordinator is
the pre-fix build). The cross-session route delivered four of four the same
day, each into a busy session, acknowledged inside a minute.

- **Resolve the name every time.** Session names change at rollover
  (`cobblestone-val-6c` became `-09` became `-0b` in three days); a cached
  name sends to nobody. `ListAgents` first, then `SendMessage`.
- **Ask for the ack when the message must land**: "reply one line by
  SendMessage". The tool's `success: true` says the pipe accepted it, not that
  the lane read it. An assignment lands in `CurrentPlan.md` on main BEFORE
  the message, so a lost one self-heals at the lane's next merge-down.
- The budget below binds unchanged: one claim and where the detail lives,
  one addressee, one message per event. A fleet-wide notice is the commander's
  and is one `SendMessage` per lane, not a broadcast.
- **The outbox is the coordinator's machinery and stays**: `build-request`,
  `build-grant`, `build-complete`, `status.json`, and the dashboard. Do not use
  it for agent-to-agent text while the stable coordinator predates
  AgentGrid CL 20652. When that build is promoted and relaunched, the typed
  route becomes a fallback for a session `ListAgents` cannot see, and the
  read-back log in that build is what says whether a typed line submitted.
- **The coordinator's own lines will arrive on this channel too** (AgentGrid
  GRID-6, CL 20862, live once Damian relaunches stable): a GO grant, MAIN
  PINNED, a held-message notice, and any `[fleet message from X]` it routes
  come as a cyan `<cross-session-message>` from `AgentGrid`, asserting
  bypass, with NO reply address. AgentGrid is not a peer: nothing answers it
  by `SendMessage`, and `ListAgents` never lists it. You answer the
  coordinator the way you always did, with `build-request`, `build-complete`
  and `status.json`. A session the coordinator cannot find in
  `~/.claude/sessions/` (or one whose `peerProtocol` it does not know) gets
  the typed line instead, logged as such on its side.

To send by the outbox (coordinator machinery, or the fallback above):

```powershell
$mbox = (Get-Content .agentgrid | ConvertFrom-Json).coordinationDir
Set-Content "$mbox\outbox\fetch-tls.json" '{ "to": "red", "text": "fetch-tls landed on main at CL 12480, do not build it again" }'
```

- `to` is an agent name, or **`fleet`** to reach everyone else in the
  fleet (a broadcast never echoes back to you).
- `text` is one line, the way an outbox entry is one line.
- Write it into **your own** `outbox/`. You never write to another
  agent's directory; AgentGrid does the routing. Same rule as always.
- Write the file in one shot. AgentGrid lets a message settle for a
  second before reading it, so a half-written file is not mistaken for a
  malformed one, but an atomic write is still the honest way to do it.

**Writing straight into another agent's `inbox/` is not a shortcut for
this, and it fails silently.** The file lands, it looks delivered, and no
terminal line is ever typed -- so the addressee sees it at their next
init instead of now. Measured 2026-08-14: two replies to an agent who was
BLOCKED behind a red gate sat unread in their inbox for a quarter of an
hour that way, while the sender believed they had answered. The routing
is the whole value of the channel; going around it leaves you with a file
drop that is slower than the depot it replaced.

**RUN `build/check-mailbox.ps1`. It is the runner this section did not have.**
Every rule below was already written down on 2026-08-20 and three agents lost
messages that morning anyway: blu wrote four into red's `inbox/` (never
delivered, red reported the lane as silent for hours), root had one sitting in
`outbox/failed/` for two days, and val had one that was a `.TXT` rather than a
`.json` and was never picked up at all. **All three look exactly like success
from the sender's side** -- no error, no bounce, the file goes somewhere
plausible. Prose cannot catch that; a script that looks can. Run it at init and
after any send you care about. Exit 1 means something of yours never arrived.

**Your own `inbox/` is DELIVERED mail and is NOT evidence of loss** (val,
2026-08-20, correcting a fleet broadcast that would have had people reporting a
hundred phantom losses). Files accumulate there normally. A message written by
hand into SOMEONE ELSE'S inbox is invisible from the receiving side, which is
exactly why the only reliable check is sender-side on your own outbox.
**The check that matters is SENDER-side: your own `outbox/sent/` is the
receipt.** AgentGrid moves a message there when it routes it. If it is
not there, you did not send it, and no wording of the file can make that
false. Watch it leave `outbox/` within a few seconds; if it is still
sitting in `outbox/`, AgentGrid is not running and nobody got it.

**A message it could not deliver goes to `outbox/failed/` instead, and it
types you the reason.** The two folders are the whole answer: `sent/` means
it reached a mailbox, `failed/` means it did not and why. Until AgentGrid
CL 16276 the archive happened BEFORE the parse and before delivery, so a
message with a typo'd recipient landed in `sent/` and the one check this
section tells you to trust said yes to a message nobody received -- the
same sender-side false confidence the rule was written for. If you are
reading an old `sent/` from before that build, it does not prove delivery.

**Use that one, because the failure this rule exists for is a SENDER
failure.** Both agents on 2026-08-14 believed they had answered. Neither
was wrong about what they wrote; both were wrong about whether it left.
A receiver-side check cannot catch that, because the sender is not the
receiver.

Receiver-side, to diagnose a message somebody else sent you, the tell is
the SCHEMA: AgentGrid writes exactly `at`, `from`, `text` and nothing
else, where a hand-dropped file carries whatever its author invented.
Measured 2026-08-14 across seven messages in one inbox -- three routed
with `at,from,text`, four hand-dropped with `body,from,subject`.
**Treat it as evidence, not proof** (blu's point, and it is right): a
hand-drop that happened to use those three field names would pass it.
`outbox/sent` cannot be fooled that way; the schema can.

**The filename is not a tell at all**, and it was recorded here as one
for a few hours on 2026-08-14. Both agents that day hand-dropped, one
mimicking the routed naming convention exactly, millisecond field and
all, and the other not. The rule was written from the second and would
have called all four of the first routed. A convention two authors
follow differently decides nothing.

### Your REPORT to Damian is budgeted too, and rulings route through the commander (Damian, 2026-08-21)

The budget below governs agent-to-agent messages. This governs the thing you
write at the end of your turn, which Damian actually reads, and it is a
separate rule because the fleet kept the first and ignored the second. His
words, with four agents idle at the time: **"i can't read the walls of text
they spew ... surface less details because I wont read it, its tokens spent
for no purpose."**

**Cut it to the result and what changed.** No journey, no what-you-ruled-out,
no restatement of a process that went as documented, no detail he would not
act on. R-REPORT already says this; what is new is that it is now measured
against a reader who has stopped reading. A report he skips is worth less
than no report, because it cost his attention to skip.

**A ruling request does NOT go to Damian. It goes to the commander, who
decides whether it is genuinely his.** Most are not. His words: agents ask
him to participate in "calls that are really not calls", which is
**psychological-needs fulfilment wearing the costume of diligence** -- it
makes the asker feel careful and spends the one attention budget the project
cannot refill.

The test is already written in `CurrentPlan.md` and has not changed: **only a
decision he alone can make** -- an outside relationship, an account, a spend,
a product direction. **A technical trade-off with a defensible answer is the
commander's call.** What is new is the ROUTE: you send it to the commander,
the commander decides, and the commander carries it up if it survives. If you
cannot name which of those four categories your question falls in, it is not
his.

**Asking is no longer automatically free.** The out clause in `CLAUDE.md`
still stands for a genuine rule conflict, and a rule that already answers your
question was never an excuse to ask. Between those, route to the commander.

**Two more standing corrections, broadcast at Damian's direction
2026-08-27:** an empty lane is one line to the commander in the same
minute, not a wait; and a clear next step with no blocker is taken, not
reported. "Not a wait" governs the moment BEFORE the line is sent -- do not
sit on an empty row. Waiting for the answer to it is the real wait the
empty-row clause above already allows; the two say the same thing from
opposite ends and have read as contradictory at speed.

**THIS RULE DOES NOT TOUCH R-TRUE, AND CANNOT (val, 2026-08-21, within
minutes of the rule being issued).** He declined the half of it that would
have filtered what reaches Damian, and he was right to. **R-TRUE is tier 1:
a red gate, a wrong byte shipped, a test you skipped, or a number you
published and later found wrong goes to Damian IN FULL, every time, and
nothing below it may be used as a reason to soften, delay, or omit it.** This
section is about brevity and about routing REQUESTS FOR A DECISION. It is
tier 3 and tier 4 material, so by the meta-rule it loses to R-TRUE outright
and never gets to gate a failure report.

The distinction, since the first wording blurred it and would have been read
as a filter: **a ruling request is you asking Damian to spend attention
deciding something. A failure report is you telling him something that is
already true.** Route the first. Never route the second -- send it, in full,
and tell the commander afterwards if it matters to the fleet.

That the correction arrived from the fleet within minutes of the rule going
out is the rule working, not failing. A commander who compresses what reaches
the human is one bad sentence away from being the reason a red gate went
unreported.

### The message budget (Damian, 2026-08-17)

The pointer rule above was written 2026-08-16 and by the next morning
messages had grown back into paragraphs, several addressed "for X and Y"
and sent to the fleet, and progress narration was flowing to the
commander after every step. Damian's words: *"every fleetwide message is
a 6x token spend."* A message is typed into the recipient's context and
read in full, mid-task; a broadcast is that cost times the fleet. So the
rule now has numbers, and they are the rule.

**Size.** `text` is at most **300 characters** (two short sentences). If
it needs more, the excess belongs in a CL description, the owning doc, a
register row or your `status.json`, and the message points there. Never
argue a case in a message: state the claim, name where the argument is.

**Addressee.** **One agent per message.** A message that starts "for val
and red" is two messages, or more often one, to whoever must ACT on it;
the other party reads the CL. `to: fleet` is reserved for the commander
and for exactly three events: MAIN PINNED, MAIN OPEN, and a claim
collision that no two agents can settle between themselves. Nobody else
broadcasts. Answer a broadcast to its sender only.

**Frequency.** An agent sends at most one message per lane event, and the
events are: **taken** (one line, item and register), **landed** (one
line, CL number and where the account is), **blocked** (one line, on
what, on whom), **question** (one, answerable in one line), and
**correction** (a claim you made was wrong; say which and where the fix
is). Progress between those events goes in `status.json`, which is read
by the watcher and costs nobody a context. Do not send: acknowledgements,
thanks, agreement, restatements of a ruling, what you are about to do
next, or a reply to a message that asked for none.

**When the addressee is the COMMANDER, the five events narrow to three, and
there is no fourth: blocked, a question only he can answer, and a correction
to something he ruled** (Damian via red, 2026-08-21). Not taken, not landed,
not a re-verification, not an acknowledgement. The reason the two extra
events drop here and nowhere else is that the commander already has both
without spending anything: `p4 changes` says what landed and `status.json`
says what was taken, and both are read when he chooses rather than typed
into his terminal while he is working. So keep `status.json` current INSTEAD
of announcing. A peer who is waiting on your work still gets taken and
landed; the commander is not waiting, he is reading the depot.

**Reply.** Reply only when the message asks for one, and reply to the
sender. If you disagree with a ruling, one line: "disagree, reasons in
CL N" or the doc section; the commander reads it there.

**The check.** Before writing to `outbox/`, count: characters under 300,
one `to`, one of the five events. If any of the three fails, the message
is not ready.

The commander is bound by all of it, and when an agent is unsure whether
a thing is a message, a status line or a doc edit, the commander decides;
ask once, in one line.

### Broadcasts

`to: fleet` types a line into EVERY other agent's terminal. It interrupts
the whole fleet mid-thought, so the bar is that it is worth that many
interruptions and the format is that it fits in one.

**Two forms, and only two.**

A QUESTION -- one question, answerable in one line, that you need
answered by another agent rather than by Damian.

A POINTER -- one line of what happened plus where the detail is. Send
the detail as a normal message FIRST, then broadcast the line that names
it. **A broadcast is never the container for the detail**, because the
cost of a broadcast is paid by everyone and the detail is wanted by one.

Three lines, hard. Say what you want back or say "no reply needed"; a
broadcast that leaves the fleet guessing whether they owe an answer costs
a reply from each of them. **Answer a broadcast to its SENDER, never to
`fleet`** -- only the asker wanted it, and a broadcast reply costs
everyone again.

The `/broadcast` skill (`.claude/skills/broadcast/SKILL.md`) is the
runner for this and carries the composition rules.

To receive: messages arrive **twice**, exactly like a build grant. A
`[fleet message from <agent>]` line is typed into your terminal, and a
copy lands in your `inbox/`. The inbox copy is the durable one -- if your
terminal was not running, the typed line is lost and the file is still
there at next launch. **Read your `inbox/` at init.**

**Delete an inbox message once you have absorbed it.** The deletion is
the acknowledgement. It works here and did not work in the retired
workplan outbox for one reason: your inbox is YOUR directory, so
acknowledging costs you nothing. There, the addressee had to delete an
entry from the author's file in the author's stream, and so nobody ever
did.

### What goes here, and where the record goes

The channel carries the **notification** and it is not a record of
anything. What the channel adds is that the addressee hears about it in
the next second instead of after two merges, and that the notification
costs no submit, no merge and no token.

**The durable fact goes in the doc that OWNS the subject, the moment it
is verified** -- a reference doc (`OperatorsManual`, `ExaminersAssay`,
`DevelopersGuide`, `HardwareSitting`), the design that owns the
capability, `LESSONS.md` for a lesson, or the relevant backlog for a gap.
Cross-lane open work goes in `docs/PM/CurrentPlan.md`. It does **not**
go in a workplan: those are empty by design and hold only the current
session's lane state.

Use it for: a defect that invalidates another lane's measurements, a
contract change, a capability someone is waiting on, "I am taking this
item so do not duplicate it."

Do not use it for: status updates nobody asked for, anything Damian
should be told instead, or a conversation. It is a notification channel,
not chat -- if a message needs two rounds, the second round belongs in
the doc that owns the subject.

## Notes from the first run (val, 2026-07-13)

- The protocol held up end to end: shelve, request, GO, gate dance, submit,
  release. The only friction was rule 7 above.
- **Hold the token for the gate and the submit -- not for the investigation.**
  The first run held it across six builds while root-causing a bug, including
  one build wasted on a workspace that had not been synced (75 stale files, a
  false MISMATCH) and a question put to the human mid-hold. All of that is
  work that belongs OUTSIDE the hold. Investigate, decide, prepare the CL,
  *then* take the token, gate, submit, release. If you find yourself reading
  code or forming a theory while holding it, you are hogging the commons.
- Do the arc -- gates, submit to your stream, copy-up, resolve, submit to
  main -- inside ONE hold. Do not release after the dev-stream submit and
  re-request for the copy-up. (This holds only while every step stays
  mechanical: the moment any step needs code written, rule 8 applies --
  release, fix outside the hold, re-request.)
- **Sync the copy-up workspace before you gate there.** A seed CL is gated on
  the *target* workspace (`SEED === SUT`), and if that workspace is stale the
  build silently uses old source and the gate fails for a reason that has
  nothing to do with your change. `p4 -c <main-client> sync //Codex/main/...`
  first, every time.

## An internal seed land is a short hold now (Damian, 2026-08-16)

A seed land used to hold the token for ~30 minutes: three full gate passes,
two of them redundant against determinism. It is a few minutes now. The token
holder gates with `build/build.ps1 -Internal` (smart coverage: the fixed-point
core and BVT always, a regression phase only when a file it depends on
changed), skips the convergence rebuild when the gate reports a one-pass fixed
point, and replaces the parent rebuild at copy-up with `build/check-seed-orphans.ps1`.
The mechanics and the why are in `PerforceProcess.md` 4.3b and 4.4; the point
for the queue is that a hold behind you is minutes, not half an hour. The full
`build/build.ps1` stays for public/release builds.

## A many-CL arc takes ONE token, at the end (Damian, 2026-08-06)

*"do the iterations on A6 locally, don't push each to main or take a token.
just stack them for one push to main, but you can do builds locally on each
step to keep the verification simple and cumulative."*

**Iterate on your own stream: submit each step to `//Codex/<agent>`, verify
each step by compiling it and running the specific tests it touches, and
gate ONCE per batch.** Until 2026-09-01 this line said to run
`build/build.ps1 -Internal` locally per step (and before 2026-08-20 the bare
`build/build.ps1`, a full gate per step: 644.1 s against 186.1 s). **Damian,
2026-09-01, with the box one DIMM down: "we need to have the agents batch up
their builds, so they can ask for the token less, and get more done in a
shot."** So a step is verified by `compile.ps1` plus its focused tests, several
steps stack into one batch, and the `-Internal` gate runs once for the batch,
at the end, under the one token that lands it. Do NOT copy any intermediate
CL to main, and do NOT request the token until the batch is ready. The last
push is then a normal seed-affecting copy-up: token, merge down, gate on the
target, prove the seed, one copy-up.

**The batch gate must SEE the batch (red, 2026-09-01, the same morning the
rule landed).** `-Internal` chooses its regression phases from `p4 opened`
(`build/check-test-compile.ps1`, the `changed` list): a batch already
submitted to your stream has nothing opened, so the gate runs core, BVT and
refusals only, skips test-compile and the plug phases for a COMPILER change,
and comes back green in 129 s having proved less than it says. red caught it
on the first batch gated under this rule and released the token unlanded.
Until the scoping fix lands (red's), the batch must be OPEN when it gates.
The clean way (val, same morning): **keep the batch as ONE open CL on your
stream, shelve it after each verified step, gate with everything still open,
submit after.** The shelf is the per-step record, there is no phantom edit CL
and nothing to revert. The fallback, for a batch you already submitted step
by step, is `p4 edit` on the files it touched before the gate. When the fix
lands, this paragraph is replaced by the command.

What counts as a batch: every item in your lane that is ready, whether or
not the items are related; the gate proves the tree, not the item. A red
batch gate is bisected by the focused tests the steps already carry, which is
why each step must have run them. R-ONE still holds at the step: one change,
its tests, its submit to your own stream; batching is about when the GATE
runs, not about how many things a CL does.

The reason is what the token is FOR. It buys a window in which main gains no
seed-affecting change underneath a gate you already paid for, and that only
has value at a real landing. Per-step tokens serialise the whole fleet behind
cosmetic intermediate states, for a guarantee no intermediate state needs.

## Workplan Cross-Lane Protocol -- RETIRED 2026-08-08

This section prescribed a `## Cross-lane` section in every workplan, with
critical-path rows and a date-stamped findings outbox that the ADDRESSEE
deleted once absorbed. **All of it is retired at Damian's direction and
must not be restarted.** CLAUDE.md is the authority: the workplans were
emptied, `docs/Agents/<agent>-workplan.md` is scratch for the current
session's lane state only, and *"do not start a findings outbox anywhere."*

The account of why is in CLAUDE.md and is worth reading before anyone
proposes it again, because the idea is a good one that failed for two
mechanical reasons. A durable fact parked in a status file is read once at
init and then reasoned about from memory instead of re-read. And an entry
was deleted by the ADDRESSEE from the AUTHOR's file in the author's stream,
which is a cross-workspace write on somebody else's document, so it almost
never happened: a true, unfixed finding about a switch that does not exist
sat in one outbox for a day and reached nobody. An entry addressed "for the
fleet" had no addressee at all and could never be cleared by anyone.

What replaced it, and the split is the point:

- The **notification** goes through Fleet Messages above -- instant, no
  submit, no merge, no token.
- The **record** goes into the doc that owns the subject, the moment it is
  verified. Cross-lane open work goes in `docs/PM/CurrentPlan.md`, which is
  the fleet's only cross-lane register; an item originating in one app or
  quire goes to that register.

The section is kept rather than deleted because it was live for twelve days
and agents who learned the fleet under it will look for it. Finding a
retirement notice is cheap; finding nothing and rebuilding the channel from
memory is not.
