---
name: mindmeld
description: Fleet-wide memory consolidation. Every agent cleans its own memory of war stories and tombstones, lifts what the fleet needs into the doc that owns it, then the fleet reviews the result one agent at a time. Two serial phases around a ring, pure docs work, no gate and no build token. Run when Damian invokes /mindmeld from any agent; that agent goes first.
shell: powershell
---

Agent memory rots in a specific way. It accumulates war stories that were
evidence once, tombstones for decisions already executed, and facts that
belong to the whole fleet but live in one agent's head. None of it is visible
to anyone else, so the same trap gets rediscovered four more times and the
same declined work gets re-proposed by whoever never heard the ruling.

A mindmeld fixes that by moving the shared parts out of five private memories
and into the docs, then having all five read what came out.

## The two rules that make it work

**1. Serial, never concurrent.** One agent at a time, all the way around the
ring, in both phases. This is not caution, it is the whole design: the last
run of this procedure had three agents fold findings into
`PerforceProcess.md` at once and it took three changelists plus a rewrite to
settle. Concurrency here manufactures exactly the conflicts the procedure
exists to prevent.

**2. Main stream only.** Every agent does this work through
`p4 -c BigWhite_Codex_<agent>_main` and submits straight to `//Codex/main`,
and **syncs immediately before each step**. It is pure docs work, so there is
no reason to route it through a dev stream and then merge. Syncing before
each step is what makes duplicate work show up as "somebody already wrote
this" instead of as a file conflict later.

```powershell
p4 -c BigWhite_Codex_<you>_main sync //Codex/main/docs/...
p4 -c BigWhite_Codex_<you>_main opened     # must be empty before you start
```

Consequences, all of them good: no build token (docs affect no seed, per
`CoordinationProtocol.md` rule 1), no gate (nothing in `build/build.ps1`
reads these files), and no dev-stream merges at all until the merge-down-all
at the end of each phase.

## Vocabulary, because one word is already taken

The thing passed from agent to agent here is the **baton**. It is not the
AgentGrid **build token**, which is a different object with a different
purpose, and this procedure never takes one. Say baton.

## Step 0 -- Work out the ring

The roster is the agents with BOTH a dev client and a main client. Derive it,
do not hardcode it:

```powershell
$agents = (p4 clients -u Damian) |
  ForEach-Object { if ($_ -match 'Client BigWhite_Codex_([a-z]+)_main ') { $Matches[1] } } |
  Sort-Object -Unique
```

Rotate so the agent running `/mindmeld` is first; the order is otherwise
arbitrary and only has to be deterministic, complete, and known to everyone:

```powershell
$me = ((Split-Path -Leaf (Get-Location)) -replace '^[^-]+-','') -replace '-main$',''
$i = [Array]::IndexOf($agents, $me)
if ($i -lt 0) { throw "not on the roster: $me" }
$ring = @($agents[$i..($agents.Count-1)]) + @($agents | Select-Object -First $i)
```

Two things in that snippet are load-bearing and were both wrong on the first
draft. The trailing `-main` strip is because the agent-name rule takes
everything right of the first `-`, which answers `blu-main` in a copy-up
workspace and then matches nothing. And the rotation must not be written as
`$agents[0..($i-1)]`: when `$i` is 0 that is the range `0..-1`, which
PowerShell reads as first-and-last rather than empty, so the ring silently
gains a duplicate. Verified over all five start points.

**Your session's working directory stays your DEV workspace.** You target the
main client with `-c BigWhite_Codex_<you>_main` on every p4 command rather
than moving there.

**The last agent in the ring does the consolidating edits in phase 2.** Say
who that is in every baton message so nobody has to recompute it.

---

# Phase 1 -- Clean your own memory, lift what is shared

Each agent does all of Step 1 and Step 2, submits, then passes the baton.

## Step 1 -- Clean your own memory

Sync first (`p4 -c BigWhite_Codex_<you>_main sync`). Then read your memory
index and every file it lists, and cut:

- **War stories.** The narrative that was evidence for a rule. Keep the rule,
  delete the account. "I once ran the gate on a CL nothing in the build reads
  and was asked why we were building the compiler" becomes "ask what step of
  the gate could observe this change."
- **Tombstones.** Records of your own corrected mistakes, notes that you
  first wrote something down wrongly, markers left where a decision was
  already executed. A rejection that has been carried out leaves nothing
  behind.
- **Anything a doc read at init already says.** `CLAUDE.md` loads every
  session; so do `LESSONS.md`, `CurrentPlan.md`, `PerforceProcess.md` and
  `CoordinationProtocol.md` through the init skill. A second copy is only a
  version that can drift.
- **Duplicates across your own files**, and merge files that turned out to be
  one fact.

Do not cut a fact merely because it is old. Cut it because it is narrative,
already written down elsewhere, or no longer true.

## Step 2 -- Lift the shared parts into the doc that owns them

For everything that survived, ask **would another agent act differently if
they knew this**. If yes, it is not yours to keep.

| what it is | where it goes |
|---|---|
| A Perforce trap | `docs/Agents/PerforceProcess.md`, section 3, one row |
| Coordination or mailbox behaviour | `docs/Agents/CoordinationProtocol.md` |
| A build, VM, or debugging fact | `docs/OperatorsManual.md` |
| A test, sidecar, or battery fact | `docs/ExaminersAssay.md` |
| A hard-won judgement lesson | `docs/PM/Active/Stories/LESSONS.md`, one row |
| A ruling that closes off work | `docs/PM/CurrentPlan.md`, standing rules |
| Something only true at release | `docs/PM/SomethingSeenDuringRelease.md` |

Three things decide whether a lift is done properly:

- **Check the doc first.** Most of what feels like a private fact is already
  written down better than you would write it. If it is there, delete yours
  and keep at most a one-line pointer.
- **If the doc is NOT read at init, say so in the pointer you keep.** A fact
  fully covered by `OperatorsManual.md` still needs a trigger somewhere, or
  you only rediscover it after you already opened the doc, which is backwards.
- **Do not lift into a doc nobody reads at the moment it matters.** A
  standing obligation with no runner loses to an interrupt fired at the right
  time (L-INTERRUPT). If the fact only bites during a specific procedure, put
  it in that procedure's skill or run sheet, not in a reference doc.

Submit to main. Docs only, so: numbered CL, ASCII description, no gate, no
token. Watch line endings on any file you create; an editor writing bare LF
into a CRLF tree turns the next agent's edit into a whole-file conflict
(`PerforceProcess.md` P-EOL).

## Step 3 -- Pass the baton

```powershell
$mbox = (Get-Content .agentgrid -Raw | ConvertFrom-Json).coordinationDir
$body = @{ to = '<next agent>'; text = '<the message>' } | ConvertTo-Json -Compress
Set-Content "$mbox\outbox\mindmeld-p1.json" -Value $body -Encoding utf8
```

The message carries: the phase, the full ring with who is last, what you
lifted and to which doc with CL numbers, what you deliberately did NOT lift
and why, and the instruction to do the same and pass on.

**Confirm it left.** The file moves from `outbox/` to `outbox/sent/` within a
few seconds. Still sitting in `outbox/` means AgentGrid is not running and
nobody got it. That is the only check worth making, because the failure it
catches is a sender failure and a receiver-side check cannot see one.

## Step 4 -- Last agent in the ring closes phase 1

Sync, then distribute the accumulated docs to every dev stream so phase 2
reviews the same bytes everywhere:

```powershell
pwsh build/merge-down-all.ps1 -DryRun             # look first
pwsh build/merge-down-all.ps1 -ApprovedBy damian
```

**The `-ApprovedBy` flag needs Damian to have asked in this session.**
Invoking `/mindmeld` IS that ask, for the two merge-down-alls this procedure
prescribes and for nothing else. It skips a workspace with files open and
that skip is correct; verify with `p4 diff2 -q`, not the summary it prints.

Then tell Damian phase 1 is closed and hand the baton back to the first agent
for phase 2.

---

# Phase 2 -- Everybody reads what came out

The point of phase 2 is that five agents read the consolidated docs with five
different sets of scars, and the ones who did not write a given section are
the only people who can tell whether it lands.

## Step 5 -- Review, and do not edit

Sync first. Read the docs phase 1 changed, in full.

**Nobody edits during their review pass except the last agent.** Findings
accumulate in the baton message. This is what keeps five agents out of the
same file.

**Verify mechanically before you report.** Every claim in these docs is an
assertion with no runner, which is exactly how they rot. A review that only
reads produces agreeable findings. So:

- Run the commands the doc gives. A row that says a command is silent when it
  actually prints a usage error and exits 1 is worse than no row.
- Check that a doc's claim about a script matches the script. Claims of the
  form "X is wired into build.ps1" and "X is not" have both been false in
  this tree while sitting in a file everyone read.
- Follow cross-references to the section they name and confirm what is there
  is what was promised.
- Report what you verified as verified, and what you only read as read.

Findings worth having are usually one of: a claim that is false, a pointer
that dangles, a recipe that does something other than what the row wanted, or
a structural problem with how the document is organised.

## Step 6 -- Pass the baton with the accumulated findings

Same mechanics as Step 3. Carry forward **everyone's** findings, not just
yours, so the last agent gets one complete set and nobody has to reconstruct
the chain. Ask the next agent to review the doc independently rather than
only checking the findings already in the message, or the last three passes
are worth nothing.

## Step 7 -- Last agent edits, then closes

The last agent in the ring:

1. Applies the accumulated findings to the docs. Where two findings disagree,
   verify against the source and say which won.
2. Submits to main, docs only, ASCII description.
3. Runs `pwsh build/merge-down-all.ps1 -ApprovedBy damian` so every dev
   stream carries the final docs.
4. Reports to Damian: what changed, what was rejected and why, and anything
   the fleet disagreed about that he should settle.

Damian relaunches the fleet clean on the new docs.

---

## When it is done right

Memory holds only what is genuinely private to one agent: how the work is
done and reported, and the preferences of the person you work for. Everything
that another agent could act on is in a doc, once, in the place that owns it.
No war stories, no tombstones, no counts carried forward.

## What this is not

- Not a backlog pass. Open work stays in `CurrentPlan.md` and the app or
  quire registers.
- Not a licence to grow the docs. A lift that duplicates what a doc already
  says makes the tree worse. Most of the value is in deletion.
- Not a gate. Nothing here compiles. If you find yourself running
  `build/build.ps1` during a mindmeld, ask what step of it could observe a
  markdown file.
