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
`p4 copy` refuses an unmerged stream.

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

**`context` is mandatory on every write** (Damian, 2026-09-07): the lane's
context used, a whole-number percent, e.g. `"context": 62`. The commander
reads it on every pulse and ORDERS `/handoff` at 70; a lane that reads 70 or
more hands off on its own without waiting to be told. A lane whose `context`
has not moved across two pulses while its state says `Working` is checked
for exhaustion first, before its terminal or its run.

**MEASURE IT, DO NOT ESTIMATE IT.** The one formula is
`build/measure-context.ps1 -Lane <lane> -Percent`, read from the transcript
files. A feeling about how long the session has run is not this number, and
AgentGrid's display and a lane's own estimate are both wrong after a
`/compact`. Write what you measured, and if you have not measured since your
last write, measure before you write.

```json
{ "state": "Working", "task": "fixing lexer fuel cap", "claim": ["codex/compiler/Lexer"], "context": 62 }
```

**The commander's pulse wakes a lane only on an EVENT**: a run of its
exited, a grant it asked for, a ruling it waits on, a collision. "You are
idle" is not an event. Every message to an idle lane after its cache has
expired is a full re-read of its context. Between events an idle lane costs
nothing; leave it idle. The pulse itself runs no oftener than every 15
minutes, and every 30 when Damian is away.

`claim` is the ground you are standing on: the paths, files or subsystems
you are changing. One string or a list of them. It is what keeps two agents
off the same code -- AgentGrid compares every live claim against every
other, and when two overlap it tells BOTH of you so you can settle it
between yourselves with a fleet message. Neither of you is stopped and
neither is in the wrong; you are the two people who did not know.

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
- `note` -- one line, shown to the human. Put the real duration in it when
  you are asking for more than a 90-second hold; re-measure rather than copy
  a number forward (L-COUNT).

An empty `build-request` file is also accepted (legacy form): you get
queued with no Perforce verification. Prefer the JSON form.

Do not poll-spam: write the file once and wait. If you need to cancel,
delete your `build-request` before it is granted, or write
`build-complete` after it is granted.

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

What that forbids:

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
  waiting for someone is a false report.

The shape of a correct wait: request or send, write `status.json`, end
the turn. The next thing that happens to you is a typed line or a
background completion, and either one starts a new turn cleanly.

**A landed report does not end the lane (Damian, 2026-08-18).** Waiting is
for a token, a reply you asked for, or a ruling only Damian can give. Landing
an item is none of those: send the one-line landed message TO A PEER who is
waiting on the work (landed is not one of the commander's three events; see
"When the addressee is the COMMANDER" below), take the next item named in
your CurrentPlan row or your register in its order, and keep working. If the
row is empty, that is a **question** event (one line to the commander: "lane
empty, draw?"), and the wait after THAT is a real one.

**A mailbox file does not wake a session.** `SendMessage` to the session
name is an interrupt and wakes the receiver; `ListAgents` says busy or idle;
a `status.json` age is a proxy that lies in both directions. A commander that
needs a lane to move reaches it through `SendMessage` and reads the depot for
what landed; the mailbox is for the coordinator protocol, not for waking
anyone.

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

**`atRest` is the field to assign off.** It is true when the agent's last
turn ENDED, read from `stop_reason` on the last assistant record of its
transcript: `tool_use` means Claude Code is still working the turn, anything
else means it is sitting at its prompt. It needs no cooperation from the
agent and no threshold, so an agent ten minutes into one gate run reads as
working rather than tripping an inactivity timer, and it is routinely the
opposite of what the agent says.

**It is also the answer to "will a typed line be read right now".** Under
**How to wait** above, a typed line is only taken in at the prompt; `atRest`
is exactly that condition, observed rather than promised. A message sent to
an agent that is not at rest waits for its current turn to finish.

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
   go in.
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

10. **Warm the caches BEFORE you request.** The hold is a mutex on the
   whole fleet, so a one-time cost paid inside it is paid by everyone
   in the queue. A workspace that has not gated recently has no cached
   plug binaries (`build-output/` is p4-ignored), and a plug-smoke phase
   then BUILDS plugs from source inside your hold. Before writing
   `build-request` on a cold workspace (above all a copy-up client),
   pre-build the plug CDXs (`pwsh codex\plugs\<p>\build.ps1`) and sync the
   workspace. The test extends rule 8's: work that would run identically
   WITHOUT the token belongs before the request, not inside the hold
   (Damian, 2026-08-06: "the problem isn't the time it took, it was the
   mutex it held.")

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

# NO GATE RUNS UNDER THE TOKEN (CLAUDE.md R-GATE), and `-Internal` is BANNED.
# The proof happened BEFORE the request, each run granted by the commander:
#   build/compile.ps1 -Src <each touched test> -Out <o> -Log <l> -Kernel seed\Codex.cdx
#   the scratch fixed point: stage 2 == stage 3, built from the DEPOT seed
#   build/bvt.ps1 -CodexCdx <candidate> -Jobs 4
#   the signer compiled and run over the candidate, then test-self-verify
#     printing that the seed verifies itself; a seed lands signed and
#     self-verified or not at all
# Under the token: re-check head, submit, copy up, release. About 90 seconds.

p4 shelve -d -c 4712        # rule 7 -- or the submit is refused
p4 submit -c 4712

New-Item "$mbox\build-complete" -ItemType File
Set-Content "$mbox\status.json" '{ "state": "Working", "task": "post-submit cleanup" }'
```

## What the token is actually for

**The token prevents colliding BUILDS on the same code. It is not a lock on
the depot, and it is not permission to work.**

**RULED AGAIN, sharper (Damian, 2026-09-02): "the point of the token is to
freeze main for a quick proof build because you already know your code is
good and you just need to sync, freeze, merge, quickbuild, bvt, promote."**
So the shape of a seed-affecting landing is:

1. **Before the request, WITHOUT the token:** merge down to head, run the
   proof named in step 3, and fix until green. Every red you find here costs nobody else anything.
   A lane that requests the token for a CL it has not seen green is
   requesting it to debug, which is rule 8's violation before the fact.
2. **The request names the main head you merged to.** A request behind head
   is answered "merge down first", not queued; since main 21620 the gate
   itself refuses a workspace behind main, so this only moves that check to
   before the queue.
3. **Under the token:** sync, merge whatever landed since (usually nothing),
   re-check head, promote, `build-complete`. **No gate runs here**, and
   `-Internal` is banned everywhere: the proof is step 1's, taken before the
   request, and is the touched tests compiled and run one at a time, the
   scratch fixed point, the BVT on the candidate, and the signed,
   self-verified seed.

   **The merge is not optional; RE-PROVING after it is, and the test is
   whether the merge touched YOUR SUBJECT.** What the proof certifies is the
   source you are submitting, so a merge that carried no file your change is
   built on leaves it certifying exactly what it did before. Read the merge's
   own file list (`p4 describe -s <merge CL>`) and decide from that: a seed
   CL cares about `codex/compiler`, `codex/foreword` and `seed`; an apps CL
   cares about what it compiles. If your subject moved, release the token
   and re-prove outside the hold (P-REMERGE).
4. **Two reds under one grant end it.** Write `build-complete`, shelve,
   and re-request BEHIND everyone already queued.

- **Docs-only changes do not need the token.** Edit them directly on main and
  submit. No gates run, so there is no race to prevent. A workplan, a backlog
  entry, a design note, a README -- just submit it. AgentGrid enforces this:
  it reads your shelved file list and answers NO TOKEN NEEDED instead of
  queueing you.
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
  do the work, re-request.
- **A grant can be CANCELLED while your run is still in flight** (blu,
  2026-09-02). The coordinator, or Damian through it, may clear
  `build-grant` and pass the token on without your run ending; the files
  simply vanish from your coordination dir. Check for `build-grant` before
  assuming you still hold it, and never infer from "I never wrote
  `build-complete`" that the token is still yours; only the mailbox shows it.
  A run that was in flight when the grant went keeps its VERDICT (a green is
  still a green on your stream) but not its landing: the copy-up waits for
  a new grant, taken the 4.4 way.

## The token does not cover RAM (Damian, 2026-09-01)

**The box is one DIMM down and stays that way until the RMA lands: 16 GB
is all there is** (15.8 GiB visible, measured 2026-09-01). The token
serialises GATES on the same code; it says nothing about two lanes each
booting guests at the same time, and on this box that overcommit is what
kills guests with a plausible-looking codegen error (`OperatorsManual.md`,
"The compile batch asks for 12 GB").

**THE BOX IS NOT GATED PER RUN (Damian, 2026-09-07 19:35: "the box is
chronically under utilized"). The standing rule:**
- **A serial single-guest run is launched WITHOUT asking.** Measure free
  memory first (`(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory`);
  above 1.5 GiB, launch. Write the run, its PID and its log in `status.json`.
- **Watch your own run.** Launch it detached and wait on it with the Monitor
  tool and a bounded `Wait-Process`, then continue. Do not end the turn and
  sit deaf until the commander bumps you; that latency, multiplied by six
  lanes and a 15-minute pulse, is what left the box idle.
- **A fan-out is launched on a RUNTIME MEASUREMENT, by the lane** (Damian,
  2026-09-08 15:50: "that can be easily measured at runtime by the agent to
  see if it fits, then go"). Measure free memory at the moment of launch and
  count the guests the run will boot (`-Jobs N` is N guests, and a batch
  compile is one guest). **The bar is the measured figure, ruled by Damian
  2026-09-08 (`docs/Agents/box-release-2026-09-08.csv`, 573 samples over the
  Update 57 release): 1 GiB per RUN guest (a test run or a BVT slot; four ran
  at about 965 MB each, one lone battery guest peaked at 2.2 GiB) and 0.25 GiB
  per COMPILE guest (18 ran at about 145 MB each).** If free GiB is at least
  the sum for the guests the run boots, launch, and write the run, its guest
  count, its PID and its log in `status.json`; if not, wait and say so in
  `status.json`, then re-measure. The 3072 MB a runner passes as `-MemMB` is
  a ceiling, not a consumption (L-REQUEST).
  Check `Get-Process msedge` first: Edge idles at about 1.7 GB and Damian
  kills it on request, so a fan-out that misses by that much is a message to
  root, not a wait. A gate's compiler stages take the box whole: no fan-out
  launches beside a running gate. The commander no longer grants fan-outs;
  root arbitrates a collision (two fan-outs measured against the same free
  memory in the same minute) and holds the box for a release gate's compiler
  stages, which is where a hold still comes from. The per-guest figures above
  are re-measured at each release (`build/box-sample.ps1` writes the profile)
  and replaced here, dated (L-COUNT).
- **A dead guest is reported, never retried.** The report names free memory
  at launch and what else was running; that is the measurement the next
  ruling on this section is made from.
- The commander still measures the box on every pulse and can order a lane
  off it; a lane that sees free memory under 1.5 GiB waits, and says so.
- **Every HOLD and every GO the commander decides is logged with the box as
  measured at that moment** (Damian, 2026-09-08: "too many times I see an
  agent held up for the box with 20% cpu and 65% memory utilized only").
  `build/box-hold-log.ps1 -Lane <lane> -Decision HOLD|GO -Ask "<what>" -Reason
  "<why>"` appends a row to `docs/Agents/box-holds.csv`: cpu%, free and total
  GiB, mem%, guest count and their owning workspaces, Renode and QEMU counts.
  The log is the evidence the threshold is tuned from; a hold with no row is
  a hold nobody can audit. The commander's own bar, until the log says
  otherwise: a serial single guest is held only under 2.5 GiB free (one run
  guest's measured peak, 2.2 GiB, plus margin) or beside a fan-out; a fan-out is held beside a rehearsal or under
  its own guests' need. Memory percent alone never justifies a hold.

- **`-Jobs 8` is the default** (Damian, 2026-09-01), conditioned on one
  heavy run at a time: a gate whose change touches the compiler runs ALONE
  on this box, nothing else booting; only a cite-scoped gate (apps, docs,
  tests) may overlap a single-guest run. **Renode arms run ALONE.**
  `WHvSetupPartition 0x800705aa` is the refusal signature. A run that dies
  is re-run alone, and the death is reported with its time.
- **`build/test.ps1 -All` and every full-battery run are PROHIBITED except
  for release builds (Damian, 2026-09-01; `CLAUDE.md` R-GATE), and
  `-Internal` is BANNED (Damian, 2026-09-02 15:52).** The fleet is on
  focused test passes: the specific tests a change touches, one at a time,
  and the BVT only on a seed candidate.
- **Waiting for a slot is not idle time (Damian, 2026-09-01).** Keep working
  the next item in your lane while the box is busy, and gate once per arc
  with the CLs batched. A hosted binary that starts no guest needs no
  measurement; anything that boots a guest does.
- **Overlap is decided by CONSUMPTION, not by ceiling (L-REQUEST).** A
  runner's `-MemMB` is a ceiling; the per-guest figures above are what a
  guest uses. The evidence behind them is the release profile the figures
  cite, and `ExaminersAssay.md` "The parallelism default" carries the
  history of the default.
- **A gate that dies mid-phase with no refusal line is a TOOL-CALL CHILD
  dying, until proven otherwise.** Launch every gate DETACHED via
  `Start-Process`, keep the PID and the log path in `status.json`, and
  before calling a died-mid-phase gate RAM or codegen, ask whether its
  parent outlived it (`OperatorsManual.md`, "A gate run as a tool-call child
  dies with the session").

This section dies with its condition: when `Win32_PhysicalMemory` shows two
rows, re-measure and rewrite it.

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
the input buffer. The outbox route TYPES a `[fleet message from X]` line into
the recipient's terminal, and a typed line reaches an IDLE lane only when
something else opens a turn, so it strands (GRID-5, GRID-7); never bump a
quiet lane by the outbox alone.

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
terminal line is ever typed, so the addressee sees it at their next init
instead of now. The routing is the whole value of the channel.

**RUN `build/check-mailbox.ps1`** at init and after any send you care about.
Exit 1 means something of yours never arrived. A message lost this way looks
exactly like success from the sender's side: no error, no bounce, the file
goes somewhere plausible.

**The check that matters is SENDER-side: your own `outbox/sent/` is the
receipt.** AgentGrid moves a message there when it routes it; a message it
could not deliver goes to `outbox/failed/` with the reason typed to you. If
a file is still sitting in `outbox/`, AgentGrid is not running and nobody got
it. Your own `inbox/` is DELIVERED mail and is not evidence of loss.
Receiver-side, the tell of a routed message is the SCHEMA (exactly `at`,
`from`, `text`), evidence rather than proof; the filename is not a tell.

### Your REPORT to Damian is budgeted too, and rulings route through the commander (Damian, 2026-08-21)

The budget below governs agent-to-agent messages. This governs the thing you
write at the end of your turn, which Damian actually reads. His words:
**"i can't read the walls of text they spew ... surface less details because
I wont read it, its tokens spent for no purpose."**

**Cut it to the result and what changed.** No journey, no what-you-ruled-out,
no restatement of a process that went as documented, no detail he would not
act on. R-REPORT carries the cadence; a report he skips is worth less than
no report, because it cost his attention to skip.

**A ruling request does NOT go to Damian. It goes to the commander, who
decides whether it is genuinely his.** Most are not: agents ask him to
participate in "calls that are really not calls", which is
**psychological-needs fulfilment wearing the costume of diligence**. The
test is `CurrentPlan.md`'s: **only a decision he alone can make** -- an
outside relationship, an account, a spend, a product direction. **A technical
trade-off with a defensible answer is the commander's call.** If you cannot
name which of those four categories your question falls in, it is not his.
The out clause in `CLAUDE.md` still stands for a genuine rule conflict, and
a rule that already answers your question was never an excuse to ask.

**Two standing corrections (Damian, 2026-08-27):** an empty lane is one line
to the commander in the same minute, not a wait; and a clear next step with
no blocker is taken, not reported. "Not a wait" governs the moment BEFORE
the line is sent; waiting for the answer to it is the real wait the
empty-row clause above already allows.

**THIS RULE DOES NOT TOUCH R-TRUE, AND CANNOT.** R-TRUE is tier 1: a red
gate, a wrong byte shipped, a test you skipped, or a number you published and
later found wrong goes to Damian IN FULL, every time. A ruling request is you
asking Damian to spend attention deciding something; a failure report is you
telling him something that is already true. Route the first. Never route the
second: send it, in full, and tell the commander afterwards if it matters to
the fleet.

### The message budget (Damian, 2026-08-17)

A message is typed into the recipient's context and read in full, mid-task;
a broadcast is that cost times the fleet (*"every fleetwide message is a 6x
token spend"*). The numbers below are the rule.

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
the acknowledgement; your inbox is YOUR directory, so acknowledging costs
you nothing.

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

## Holding the token

- **Hold the token for the submit, not for the investigation.** Investigate,
  decide, prepare the CL, *then* take the token, submit, release. If you find
  yourself reading code or forming a theory while holding it, you are hogging
  the commons.
- Do the arc -- submit to your stream, copy-up, resolve, submit to main --
  inside ONE hold. Do not release after the dev-stream submit and re-request
  for the copy-up. The moment any step needs code written, rule 8 applies:
  release, fix outside the hold, re-request.
- **Sync the copy-up workspace before you gate there.** A stale target
  workspace silently uses old source and the gate fails for a reason that
  has nothing to do with your change: `p4 -c <main-client> sync
  //Codex/main/...` first, every time.

## An internal seed land is a short hold now (Damian, 2026-08-16)

A seed land holds the token for about 90 seconds, because **the token holder
runs no gate at all**. The proof is taken before the request (the recipe is
in "What the token is actually for", step 3), and the copy-up replaces the
parent rebuild with `build/check-seed-orphans.ps1`. The mechanics are in
`PerforceProcess.md` 4.3b and 4.4; the point for the queue is that a hold
behind you is seconds, not half an hour. `build/build.ps1` is the release
gate and is Damian's; `-Internal` is banned (Damian, 2026-09-02 15:52).

## A many-CL arc takes ONE token, at the end (Damian, 2026-08-06)

*"do the iterations on A6 locally, don't push each to main or take a token.
just stack them for one push to main, but you can do builds locally on each
step to keep the verification simple and cumulative."*

**Iterate on your own stream: submit each step to `//Codex/<agent>`, verify
each step by compiling it and running the specific tests it touches, and
prove ONCE per batch.** **Damian, 2026-09-01, with the box one DIMM down:
"we need to have the agents batch up their builds, so they can ask for the
token less, and get more done in a shot."** A step is verified by
`compile.ps1` plus its focused tests, several steps stack into one batch, and
the batch's proof runs once, at the end, BEFORE the token is requested: the
scratch fixed point, the BVT on the candidate, and the signed, self-verified
seed. Do NOT copy any intermediate CL to main, and do NOT request the token
until the batch is proven. The last push is then a normal seed-affecting
copy-up: token, merge down, re-check head, one copy-up.

**The batch gate SEES the batch.** `build/build.ps1` (Damian's) chooses its
regression phases from a `changed` list that is `p4 opened` UNIONED with
`p4 diff2 -q //Codex/main/... <your stream>/...`, so a batch already
submitted to your stream is seen and a COMPILER batch runs test-compile and
the plug phases whether or not anything is opened. Nothing needs `p4 edit`
before a gate. A stream BEHIND main reads as changed too, which
over-triggers and never under-triggers, and the merge-down every grant
requires removes it.

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

Retired at Damian's direction and not to be restarted: there is no findings
outbox anywhere, and `docs/Agents/<agent>-workplan.md` is scratch for the
current session's lane state only (`CLAUDE.md`). The **notification** goes
through Fleet Messages above; the **record** goes into the doc that owns
the subject the moment it is verified, and cross-lane open work into
`docs/PM/CurrentPlan.md`.

