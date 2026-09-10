---
name: init
description: Initialize a Codex/Astra coding lane in Cobblestone. Load identity, current work and relevant memory, lessons, Perforce state and coordination capabilities. The fleet commander uses commander-init, which includes this flow once.
---

# Coding lane init

Run at a new session's start, or when Damian requests init. The fleet
commander enters through `../commander-init/SKILL.md`, which runs this flow
once and adds fleet duties. A task-scoped subagent follows the parent's
brief and reads applicable instructions, without initializing a fleet seat.

The startup outcome is a reliable working set: the active request, workspace
identity, preserved work, current assignment, relevant lessons, and the
capabilities needed for the next action. Read directly by default. Do not
launch summary agents, scan every design or reread files already supplied
in full. This is a deliberate Codex/Astra default, not a model benchmark.

Resolve repository-relative paths from the workspace root. Read the root
`AGENTS.md` and any applicable nearer instructions before subject work.

## 1. Establish identity and capabilities

Run `Get-Location`; obtain the lane from the folder suffix after the first
`-`. Read `.p4config` before any Perforce command. If absent, create:

```text
P4PORT=localhost:1666
P4USER=damian
P4CLIENT=BigWhite_Codex_<lane>
```

Lowercase the lane. Verify an existing client rather than replacing the
configuration blindly. A `-main` workspace is the lane's MAIN client, not
a new lane. After reading `docs/Agents/PerforceProcess.md`, use `p4 info`
to confirm client root and stream. A mismatch blocks Perforce mutation,
not unrelated read-only work.

Read `.agentgrid` when present. Check the named lane, mailbox and session
ownership against the current harness or explicit user assignment. Distinguish
conversation `/root` from fleet commander. Never adopt a mailbox solely
because an old `.agentgrid` pointer exists. No pointer means standalone
operation; do not manufacture an AgentGrid identity.

Use only tools exposed in this session. The collaboration tree is not a
fleet roster. Claude `ListAgents`, `SendMessage`, `ScheduleWakeup`, transcript
formats and memory paths are not assumed Codex capabilities. Check the
available tool catalog only for capabilities the current role needs.

## 2. Inspect current work before changing state

Read the Perforce process before these commands. Substitute the verified
client and stream; capture each result and exit status without hiding errors:

```powershell
p4 changes -s pending -c <client>
p4 changes -s shelved -c <client>
p4 opened
p4 changes -s submitted -m 5 -c <client>
p4 changes -s submitted -m 1 //Codex/main/...
p4 diff2 -q -S //Codex/<lane>
```

The comparison reports depot-stream content, not all local edits. Inspect
relevant local diffs and untracked target files before editing. Preserve
pending CLs and shelves; a prior shelf is not a new assignment. Read the
lane's `docs/Agents/<lane>-workplan.md` if present for in-flight state.

If a verified mailbox exists, read status, active request/grant files and
unhandled inbox events. Inspect filenames and relevant new entries rather
than dumping the mailbox archive. Read `CoordinationProtocol.md` before
interpreting or mutating protocol state. Correlate a claimed run with its
PID, command and log before treating the run as live or cleaning leftovers.
Age alone does not make a grant stale.

A merge-down directive for the verified seat is handled by the documented
Perforce procedure before taking backlog work. First protect local edits
and untracked requested files; then merge and inspect the result. Never
use a broad revert, clean or sync to erase unowned work. When merely
assessing a file or configuring the harness, inspect main's relevant files
read-only if a merge would disturb unrelated work. Do not claim the lane
is synchronized from a path-limited comparison.

## 3. Load the working set

- Read memory or handoff explicitly supplied by the harness, and task-relevant
  files reached from that index. If no memory index is supplied, proceed
  with the conversation, lane scratch and owning registers. Do not crawl
  private Claude histories or create a second project-state database.
- Read `docs/PM/Active/Stories/LESSONS.md` directly. Read a lesson's full
  linked evidence when the lesson becomes relevant to an action. The index
  is discovery, not proof. Keep probe answer keys out of startup context.
- Read the CPL section of `docs/DevelopersGuide.md`, from `Codex Prose
  Language (CPL)` through `CPL Sentence Forms`, for messages to Damian.
- In `docs/PM/CurrentPlan.md`, read the current global restrictions, your
  complete lane row, and rulings affecting the request. Retrieve complete
  Markdown rows/sections, not truncated previews. Read the named owning
  backlog/design when taking the item. A stage number always carries the
  campaign name; verify the source before acting on a summarized stage.
- Damian's explicit task is the assignment. When no task is assigned, use
  the verified commander's assignment; otherwise report readiness and wait.
  Do not pull a new campaign from the register merely because init finished.

A register and a shelf can disagree. Verify only the part affecting the
current task, preserve the other work, and do not turn init into a cleanup
campaign. An active design catalog is read only when discovering relevant
work requires the catalog.

## 4. Read by subject before acting

Search headings and read the complete relevant section of large manuals.

| Before touching | Read |
|---|---|
| `.codex` source | `docs/DevelopersGuide.md`; `docs/DevelopersRulebook.md` |
| Compiler memory, allocators, decks, registers, SMP, page tables | `docs/ArchitectsSketchbook.md` |
| Build, VM, debug, profile, seed, release | `docs/OperatorsManual.md`; the exact scripts to execute |
| Editing or regenerating `build/*.ps1` | `docs/Designs/Active/Build/Build.md` |
| Tests, sidecars, oracles, GUI, skip/diag semantics | `docs/ExaminersAssay.md` |
| GOP desk panes or `ds` cells | `apps/works/works-desk-contract.md` |
| Web output, HTML plug, browser apps, widgets | `docs/TheShimmeringPortal.md` |
| IoT, board drivers, MMIO | `docs/TinkersToolbox.md` |
| Compliance, punctual/WCET, regulatory claims | `docs/KingsAndCourts.md` |
| VS Code, USB build/flash, QEMU boot | `docs/UsersHandbook.md` |
| App inventory | `docs/CuratorsCatalogue.md` |
| Ethos and definition of done | `docs/VisionAndVirtues.md` |
| Founding vision | `docs/PM/Stories/Vision/` |
| Perforce beyond edit/submit | `docs/Agents/PerforceProcess.md` |
| Coordination mutation, first VM run or token request | `docs/Agents/CoordinationProtocol.md` |
| A load-bearing lesson | Full evidence linked by `docs/PM/Active/Stories/LESSONS.md` |
| Release history | Relevant entry in `docs/PM/Active/GitHubUpdates/` |

## Context and continuation

Use current-session context telemetry only when the harness exposes a
verified measurement with its effective window and compaction state.
Report source and freshness. Cumulative token spend, remaining goal budget,
file bytes divided by four, and another session's usage are not context
occupancy. Do not hardcode Astra's window or claim a percentage from them.

`build/measure-context.ps1` reads Claude transcripts and a Claude window.
Use that script for a Claude lane only after reading the script; do not use
the script as Codex telemetry. A copied handoff skill's claim otherwise does
not change the script's input format. If Codex telemetry is unavailable,
say `context usage unavailable` once when operationally relevant.

At a verified 70 percent used, or an explicit handoff request, checkpoint
at a safe boundary. Do not infer exhaustion from elapsed time or force an
early handoff from an estimate. Automatic harness compaction is continuation:
preserve the request, accepted decisions, evidence, uncommitted edits and
next action. After compaction refresh opened files, head and owned runs
before the next mutation; do not restart finished work.

A checkpoint names the task, workspace/client, exact files and CLs, proof
results and unrun checks, owned PIDs/logs, blockers and the next concrete
action. Use the harness continuation channel and established lane scratch;
put durable facts in owning docs. A completed handoff leaves no stale scratch
or orphaned owned process. Keep live-run recovery details until another
session can locate the run. Apply R-NAIVE to a durable handoff artifact.
Do not use Claude-only TaskList or scheduler calls as mandatory steps.

## 5. Publish only supported state, then work

For a verified AgentGrid session, update only the owned `status.json` at
state changes using the protocol's accepted schema and fresh measurements.
Keep `claim` explicit and name an active run's guests, PID and log. If the
consumer requires numeric context and no compatible Codex measurement
exists, do not fabricate a value, write null into a numeric contract or
carry forward Claude's number. Leave that shared file intact, record the
current task and capability gap in lane scratch if the seat is owned, and
report that the dashboard is not publishing this Codex session. Continue
work that does not depend on the missing integration.

Send a coder check-in only through an available, authorized fleet channel.
Do not treat a local subagent message as a fleet check-in. A missing channel
or scheduler does not block Damian's already-assigned local task; a required
build token without a usable coordinator does block seed promotion.

When work was requested, continue the work after init. When only init was
requested, give a compact readiness result: lane, relevant pending/open
state, assignment or blocker, and material capability gaps. No routine
recital of the files read. Do not finish with readiness in place of an
outstanding deliverable.
