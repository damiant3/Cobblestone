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

build/build.ps1             # gates

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

To send:

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

**Iterate on your own stream: submit each step to `//Codex/<agent>`, run
`build/build.ps1` locally per step, and keep the workspace seed current with
the local Sut so verification stays cumulative.** Do NOT copy any
intermediate CL to main, and do NOT request the token until the final push.
The last push is then a normal seed-affecting copy-up: token, merge down,
gate on the target, prove the seed, one copy-up.

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
