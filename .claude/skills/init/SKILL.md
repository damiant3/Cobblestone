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

- `docs/CuratorsCatalogue.md`
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

**Agent A — PM docs (the open work):**
- `docs/PM/CurrentPlan.md` — the shape and priority order
- `docs/PM/BACKLOG.md` — **the register of every open capability**

Agent A must return the open work, not a summary of the project's
health. Specifically: the top gaps from CurrentPlan in priority order,
and from BACKLOG the items that are actionable now and (a) in this
agent's lane, or (b) unowned. Name them concretely. "The project is
in good shape" is a failed report.

**Skip every entry marked `Deferred`.** The decision to wait has already
been made and re-reporting it each session wastes the reader's time. Do
not list them, do not summarize them, do not argue for them. Report a
deferred entry only if you found direct evidence its text is now false —
which is a correction, not a status update.

**Agent B — Perforce process and agent workplans:**
- `docs/Agents/PerforceProcess.md`
- `docs/Agents/CoordinationProtocol.md` — the AgentGrid build-token
  protocol; you must hold the token before any gate run or submit
- Glob for `docs/Agents/*-workplan.md` and read every match.
  These are inter-agent communication — other agents' current plans,
  streams, and status. Note anything relevant to your own work.

**Agent C — Active designs (catalog only, do NOT read every doc in full):**
- Glob for `docs/Designs/Active/**/*` to get the file list.
- For EACH match, read only its opening with the Read tool's `limit`
  parameter (`limit: 60`) — the title, status, and summary live at the
  top. Do **not** read whole design docs; there are ~25 of them and
  reading them in full overflows your context before you can report.
  A bounded prefix per file is enough for the one-line catalog below.
- Return a catalog: one line per design doc — path, the capability it
  designs, and whether it is in-progress or a not-yet-started proposal.
  Group by subdirectory. This is a map of where the live work is, not a
  digest of each design.
- Skip `docs/Designs/Done/` — it is the archive (shipped and superseded
  designs, folded together), kept for reference but NOT read at init.
- `docs/Reference/` is also NOT read at init (surveys and position docs).
- App designs now live under `apps/<app>/design/` and are intentionally
  NOT read at init (read them when you work that app).

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


## Step 6 — Check coordination mailbox

Read `.agentgrid` in the working directory root (JSON:
`{ agent, coordinationDir }`). If it exists, list the coordination
directory and check for leftovers from a prior session: a stale
`build-grant` means you may still HOLD the build token — if you are not
mid-gate, write `build-complete` there to release it; a stale
`build-request` you no longer intend should be deleted. If `.agentgrid`
does not exist, AgentGrid is not managing this workspace — skip.

## Step 7 — Check memory

Read your memory index at the path shown in your system context
(typically `~/.claude/projects/<project-key>/memory/MEMORY.md`). Read
every memory file listed there. These contain handoff notes,
feedback, and project state from prior sessions.

## Step 8 — Report

After ALL steps complete, report to the user:

- **Agent name** and working directory
- **Perforce state:** pending CLs, shelved CLs, opened files,
  merge-down/copy-up status (one line each)
- **Handoff notes:** anything relevant from memory
- **Open work:** the top 3-5 items from `docs/PM/BACKLOG.md` and
  `docs/PM/CurrentPlan.md` that are **actionable now and in your lane**,
  named concretely, with a word on which are unowned. **Omit every entry
  marked `Deferred`** — those are known, decided, and not this session's
  work. **This section is not optional and must not be replaced by a
  battery count** — a green battery is not the absence of work, it is the
  reason the work is visible.

  Keep it to what the reader does not already know. The register is read
  every session by every agent; reciting the same standing gaps back to
  the person who wrote them is noise, not diligence.
- End with: "Ready for instructions."

Keep the report compact — no headers larger than bold text, no
repeating doc contents. Just the status and the open work.
