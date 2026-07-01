---
description: >-
  Session initialization — read all live docs, set up .p4config, check
  Perforce status, check memory. Run this at the start of every session.
shell: powershell
---

You are initializing a new session. Follow every step below in order.
Do not skip steps. Do not summarize early. Complete all steps before
reporting status to the user.

## Step 1 — Identify yourself

Run `Get-Location` to find your working directory. Your agent name is
the last 3 characters of the directory name (e.g. `NewRepository-val`
→ **val**). Remember this — you are agent **XXX** for this session.

## Step 2 — Read mandatory docs (directly, not via agent)

Read ALL files in the root of `docs/` using the Read tool. Do them in
parallel. These are non-negotiable — every session must read them:

- `docs/Apps.md`
- `docs/ArchitectsSketchbook.md`
- `docs/DevelopersGuide.md`
- `docs/DevelopersRulebook.md`
- `docs/ExaminersAssay.md`
- `docs/KingsAndCourts.md`
- `docs/OperatorsManual.md`
- `docs/TheShimmeringPortal.md`
- `docs/TinkersToolbox.md`
- `docs/UsersHandbook.md`
- `docs/VisionAndVirtues.md`

## Step 3 — Read remaining docs (via parallel agents)

Read these directly as well (parallel with Step 2 is fine):

- `docs/PM/Stories/Vision/NewRepository.txt`
- `docs/PM/Stories/Vision/IntelligenceLayer.txt`
- `docs/PM/Stories/Vision/CodexIoTPlan.md`

Then launch THREE parallel agents (use the Agent tool, model: haiku)
to read the rest. Each agent reads its assigned files and returns a
short summary. All three must run concurrently:

**Agent A — PM docs:**
- `docs/PM/CurrentPlan.md`
- `docs/PM/BACKLOG.md`

**Agent B — Perforce process and agent workplans:**
- `docs/Agents/PerforceProcess.md`
- Glob for `docs/Agents/*-workplan.md` and read every match.
  These are inter-agent communication — other agents' current plans,
  streams, and status. Note anything relevant to your own work.

**Agent C — Active designs:**
- Glob for `docs/Designs/*/Active/**/*` and `docs/Designs/Apps/*/Active/**/*`
- Read every match
- Skip `Done/` and `History/`

## Step 4 — Check .p4config

Check if `.p4config` exists in the working directory root. If it does
not exist, create one with this content (replace XXX with your agent
name, lowercase):

```
P4PORT=localhost:1666
P4USER=damian
P4CLIENT=BigWhite_Codex_XXX
```

If it already exists, read it and verify the client name matches your
agent name.

## Step 5 — Check Perforce status

Run these commands in parallel (use the PowerShell tool):

1. `p4 changes -s pending -c BigWhite_Codex_XXX` — pending CLs
2. `p4 changes -s shelved -c BigWhite_Codex_XXX` — shelved CLs
3. `p4 opened` — files opened on disk
4. `p4 changes -s submitted -m 5 -c BigWhite_Codex_XXX` — recent submissions
5. `p4 depots` — find depot paths (needed for next commands)


## Step 6 — Check memory

Read your memory index at the path shown in your system context
(typically `~/.claude/projects/<project-key>/memory/MEMORY.md`). Read
every memory file listed there. These contain handoff notes,
feedback, and project state from prior sessions.

## Step 7 — Report

After ALL steps complete, report to the user:

- **Agent name** and working directory
- **Perforce state:** pending CLs, shelved CLs, opened files,
  merge-down/copy-up status (one line each)
- **Handoff notes:** anything relevant from memory
- **Scope reminder:** what this agent works on (from memory, if recorded)
- End with: "Ready for instructions."

Keep the report compact — no headers larger than bold text, no
repeating doc contents. Just the status.
