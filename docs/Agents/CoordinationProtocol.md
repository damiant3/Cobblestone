# AgentGrid Coordination Protocol

How fleet agents coordinate builds and submits through AgentGrid so
they stop clobbering each other racing to main.

This document is written for the AGENTS (Claude Code sessions running
in the fleet workspaces). AgentGrid implements the granting side; its
source lives in the `//AgentGrid/main` depot
(`AgentGrid/Services/BuildQueueService.cs`), which also holds the
canonical copy of this doc. Keep the two in sync when the protocol
changes.

## The Problem

Multiple agents build, test, and copy-up to main simultaneously. Agent
A and agent B both run gates against main@100. A submits CL 101. B's
gate run is now stale -- B submits a seed that is not a fixed point on
the new main, or hits resolve conflicts, and everyone downstream
inherits the mess. Hours of compute wasted.

## The Fix

AgentGrid owns a single **build token** per project. Holding the token
means: exclusive right to run gates and submit.

**Every grant tells you to merge down from main first.** This is not a
judgement the coordinator makes for you and it is not conditional on
anything -- a copy-up from an unmerged stream is refused by Perforce, so
merging down is a precondition of using the token for its purpose. Merge
down, resolve, re-shelve, and only then run gates: your gate run is against
the real head, not a memory of it.

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

### status.json -- keep it fresh

Write this whenever your activity changes. It drives the status dot and
task label in the UI, and it is how other agents see what you are doing.

```json
{ "state": "Working", "task": "fixing lexer fuel cap" }
```

Valid states: `Idle`, `Working`, `Building`, `WaitingForBuild`, `Error`.

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
- `note` -- one line, shown to the human.

An empty `build-request` file is also accepted (legacy form): you get
queued with no Perforce verification. Prefer the JSON form.

Do not poll-spam: write the file once and wait. If you need to cancel,
delete your `build-request` before it is granted, or write
`build-complete` after it is granted.

## What Happens Next

AgentGrid polls every second and answers **two ways at once**: it
writes files in your mailbox, and it types a `[AgentGrid coordinator]`
message directly into your terminal. You will see the message as user
input in your session. Obey it.

1. **DENIED** -- your CL has no shelved files. The request file is
   deleted. Shelve, then drop a new request.

2. **QUEUED** -- someone else holds the token. The message names the
   current holder and your position. Keep working on something else
   or wait. Do NOT run gates or submit while queued.

3. **GO with MERGE** -- `build-grant` appears in your mailbox and the
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

4. **CANCELLED / REVOKED** -- the human pulled your queued request
   (CANCELLED) or your held token (REVOKED) from the AgentGrid UI.
   Your mailbox files are cleared for you. Stop immediately: no gates,
   no submit. Shelve, address whatever prompted the human to step in,
   and drop a new `build-request` when the CL is ready.

5. **Release the token.** When your submit lands (or you abandon the
   attempt), create `build-complete` (empty file is fine) in your
   mailbox. AgentGrid clears your grant and hands the token to the
   next agent in line. There are exactly two ways out of a hold: you
   submitted and released, or you released. See rule 8.

## Rules

1. **Never run gates or submit without holding the token.** The token
   is the whole mechanism; going around it recreates the race.
2. **Shelve before you request.** The gate dance (shelve, revert,
   sync -f, clean, unshelve, build) already requires it; the protocol
   just checks you did it.
3. **Always release.** A crashed gate run still needs `build-complete`.
   If your session dies, the human can kill your slot in AgentGrid,
   which also releases the token.
4. **One request at a time.** A second `build-request` while queued or
   building is ignored.
5. **Merge down, every grant, no exceptions.** It is a precondition of
   the token, not a conditional step: the copy-up your token exists to
   perform is refused from an unmerged stream. If you cannot complete
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
p4 clean codex/... apps/... docs/...
p4 unshelve -s 4712 -c 4712
p4 opened                   # LOOK at it before you build
p4 diff -du                 # and look at the diff too

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
  the code they gate, not about every `p4 submit` in the depot.
- **Code that runs gates needs the token**, including a copy-up, because a
  copy-up is a submit of gated code to main and that is exactly the race.
- **Fixing broken code does not need the token -- and must not hold it.**
  Red gates, debugging, writing tests, "just one more thing": all of it
  happens outside the hold (rule 8). The token is for landing finished
  work, not for finishing work.

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
