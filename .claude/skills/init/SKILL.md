---
description: >-
  Session initialization -- identity, memory, fleet state via agents,
  lesson index, on-demand reading contract, Perforce status. Run this at
  the start of every session.
shell: powershell
---

You are initializing a new session. Follow every step below in order.
Do not skip steps. Complete all steps before reporting status.

## Why this file reads the way it does (2026-07-28, Damian's direction)

Init keeps in DIRECT context only what changes behavior at session start
(memory, the lesson index, three agent summaries, Perforce state);
everything else is on-demand reading with an explicit trigger table. The
stories doctrine -- lessons live in the middle of post-mortems, and
summaries rot -- is preserved by a harder rule, not a longer read:
**when a lesson id becomes load-bearing for your work, you read its
story THEN, in full.** An unread story that never becomes load-bearing
costs nothing.

## Step 1 -- Identify yourself

Run `Get-Location`. Your agent name is everything to the RIGHT of the
first `-` in the directory name (e.g. `Cobblestone-val` -> **val**,
`Cobblestone-fester` -> **fester**). Split on the separator; do not
take a fixed number of characters. You are agent **XXX** for this
session.

## Step 2 -- Read memory

Read your memory index (`MEMORY.md` at the path in your system context)
and every memory file it lists. These carry handoff notes and project
state from prior sessions.

## Step 3 -- Launch THREE parallel agents (model: haiku)

All three run concurrently. Their reports come back small; the files
they read never enter your context.

**A compressed read of `CurrentPlan.md` flattens campaigns** (it merged
two campaigns' stage lists into one and produced a wrong assignment
within hours, red 2026-08-21). So: **a stage number carried out of this
summary is not addressed until it names its CAMPAIGN.** Require Agent A
to prefix every staged item with the campaign, and re-read the row in
`CurrentPlan.md` before acting on any stage number.

**Agent A -- the open work:**
- `docs/PM/CurrentPlan.md` **in full** -- return every open item in
  priority order, named concretely, with its owner and a word on which
  are unowned. Skip every entry marked `Deferred`. "The project is in
  good shape" is a failed report. Since 2026-08-08 this file is the
  fleet's ONLY cross-lane register of open work; there are no per-agent
  workplans, so a gap missing from this report is a gap nobody sees.

**Agent B -- Perforce process, and the workplans:**
- `docs/Agents/PerforceProcess.md` and
  `docs/Agents/CoordinationProtocol.md` -- return the gate-dance and
  build-token checklists.
- Glob `docs/Agents/*-workplan.md` (forward slashes, relative to the
  workspace root) and read every match. **Return the list of files the
  glob matched before anything else**: a glob that matched nothing
  reads exactly like five empty workplans, and on 2026-09-02 it did
  (the agent answered "no files found" over five files on disk). These
  are **EMPTY BY DESIGN since 2026-08-08** and hold only a session's
  in-flight lane state (what is shelved, what is mid-gate). Return
  anything actually in one, and say "all empty" when they are, which
  is the expected answer. **A workplan carrying work items, standing
  facts or messages to other lanes is a DEFECT to report**: that
  content belongs in CurrentPlan, a backlog, or the doc that owns the
  subject, and somebody's handoff did not finish.

**Agent C -- active designs (catalog only):**
- Glob `docs/Designs/Active/**/*`; read each match with `limit: 60`
  only. Return one line per doc: path, capability, in-progress or
  proposal. Skip `docs/Designs/Done/` and `docs/Reference/`.

## Step 4 -- Read the lesson index

Read `docs/PM/Active/Stories/LESSONS.md` directly (~1.4k tokens). It is
one row per hard-won lesson with a stable id and a pointer to the story
that earned it. The stories themselves are NOT read at init. The
binding rule: **before you lean on a lesson -- or are about to act in
a way a row warns against -- read its story in full, then act.** Cite
the ids (L-ORACLE, L-COUNT, ...) in CLs and reviews so the reasoning
stays reachable.

## Step 5 -- The on-demand reading contract

Nothing below is read at init. Each row is MANDATORY before work that
touches its subject; Grep for the section rather than reading whole
files where the doc is large. Do not start subject work on a stale
assumption a listed doc would have corrected.

| Before you touch | Read |
|---|---|
| Writing or reviewing `.codex` source | `docs/DevelopersGuide.md` (syntax, pitfalls); `docs/DevelopersRulebook.md` (quires, library rules, seed reachability) |
| Compiler memory, allocators, decks, registers, SMP, page tables | `docs/ArchitectsSketchbook.md` |
| Builds, `compile.ps1`, codex-vm flags, debugging, profiling, seed rebuild, release | `docs/OperatorsManual.md` |
| EDITING or REGENERATING a `build/*.ps1` script | `docs/Designs/Active/Build/Build.md` -- the shipped scripts are hand-maintained and the drift runs the OTHER way from what "generated from" advertises, so regenerating one hands back a script that prompts headless |
| Tests, sidecars, batteries, oracles, GUI tests, skip/diag semantics | `docs/ExaminersAssay.md` (~34k tokens -- Grep the section, never read whole) |
| A GOP desk pane, or taking a `ds` cell | `apps/works/works-desk-contract.md` -- the desk never unwinds, a pointer cell must be allocated in `desk-run`, and a pane-local heap mark does not reclaim the rebuilt root |
| Web output, the HTML plug, browser apps, widgets | `docs/TheShimmeringPortal.md` |
| IoT boards, board drivers, MMIO windows | `docs/TinkersToolbox.md` |
| Compliance claims, punctual/WCET, regulatory mappings | `docs/KingsAndCourts.md` |
| VS Code setup, USB stick build/flash, QEMU boot | `docs/UsersHandbook.md` |
| The app inventory | `docs/CuratorsCatalogue.md` |
| Ethos, virtues, definition of done | `docs/VisionAndVirtues.md` |
| The founding vision verbatim | `docs/PM/Stories/Vision/` |
| ANY Perforce operation beyond `p4 edit` / `p4 submit` | `docs/Agents/PerforceProcess.md` (standing rule, unchanged) |
| Your first gate run or token request of the session | `docs/Agents/CoordinationProtocol.md` (standing rule, unchanged) |
| A LESSONS id you are about to lean on or breach | Its story under `docs/PM/Active/Stories/` |
| Release history, "what landed in cycle N" | `docs/PM/Active/GitHubUpdates/` |

## Step 6 -- Check .p4config

If `.p4config` is missing from the workspace root, create it
(XXX lowercase):

```
P4PORT=localhost:1666
P4USER=damian
P4CLIENT=BigWhite_Codex_XXX
```

If it exists, verify the client name matches your agent name.

## Step 7 -- Check Perforce status

Run in one call:

1. `p4 changes -s pending -c BigWhite_Codex_XXX`
2. `p4 changes -s shelved -c BigWhite_Codex_XXX`
3. `p4 opened`
4. `p4 changes -s submitted -m 5 -c BigWhite_Codex_XXX`

## Step 8 -- Check coordination mailbox

Read `.agentgrid` in the workspace root (JSON: `{ agent,
coordinationDir }`). If it exists, list the coordination directory: a
stale `build-grant` means you may still hold the token (write
`build-complete` if you are not mid-gate); delete any stale
`build-request` you no longer intend. If `.agentgrid` does not exist,
AgentGrid is not managing this workspace -- skip.

**A `*-merge-down-directive-*.json` file in your inbox is an ORDER, and it
is the first thing you do after this step** (Damian, 2026-09-02):
`build/merge-down-all.ps1` ran while you were down and could not bring
your stream to main, so you are behind on rulings and registers by
everything since the `head` it names. Merge down (`p4 merge -S
//Codex/XXX -r; p4 resolve -am; submit`, or the shelve-first dance in
`PerforceProcess.md` if you hold open work), verify with `p4 diff2 -q`,
then delete the directive file. Do not read CurrentPlan or take an item
before that merge lands: the register you would read is the stale one.

**The dashboard is `status.json`, and you maintain it (Damian, 2026-09-02).**
Before Step 9, write `<coordinationDir>\status.json` with your live state:
`state` (`Idle`, `Working`, `Building`, `WaitingForBuild`, `Error`), `task`
(the unit you are on and its current step, or what you are waiting on),
and `claim` (the files or subsystems you hold). Then rewrite it at every
change of state for the rest of the session: taken, gating, waiting on
the box or the token, landed, handed off. A `status.json` still carrying
the previous session's handoff text is what the fleet dashboard showed
for four of six lanes at 12:00 on 2026-09-02, three hours into the day,
and that is the failure this paragraph exists to name. The contract is
`docs/Agents/CoordinationProtocol.md`, "status.json -- keep it fresh".

**Fleet messages are budgeted (Damian, 2026-08-17), and this binds from
your first message of the session:** at most 300 characters, ONE
addressee, one message per event (taken, landed, blocked, question,
correction), reply only when asked, pointers not contents. `to: fleet` is
the commander's alone (MAIN PINNED, MAIN OPEN, claim collision); every
fleet-wide message costs six agents' context. And never wait in the CLI
with a foreground loop: end your turn at the prompt so coordinator and
agent messages can arrive. The full rules are
`docs/Agents/CoordinationProtocol.md`, "The message budget" and "How to
wait".

## Step 9 -- Report

- **Agent name** and working directory
- **Perforce state:** pending, shelved, opened, one line each
- **Handoff notes:** anything relevant from memory
- **Open work:** the top 3-5 items from Agent A that are actionable now
  and in your lane, named concretely; omit `Deferred` entries; a green
  battery is not the absence of work
- **Anything the plan says only Damian can decide**, from CurrentPlan's
  rulings queue
- End with: "Ready for instructions."

Keep the report compact. Do not recite doc contents back to the reader
who wrote them.
