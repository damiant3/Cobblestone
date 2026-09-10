---
name: commander-init
description: Initialize the Codex/Astra fleet commander for one commander and five coding lanes. Run the shared init once, then inspect fleet assignments, live runs and coordination capabilities and arm event-only monitoring when supported.
---

# Fleet commander init

Use for the user-assigned fleet commander. The five coders use plain `init`.
A conversation named `/root` is insufficient evidence of fleet command.
Verify the fleet role and workspace/mailbox identity before fleet mutation.

## 1. Shared startup, once

Execute `../init/SKILL.md` through the working-set and capability checks.
If already completed in this session, reuse the evidence and refresh only
volatile state. Do not send a coder check-in or stop at the coder readiness
report. The shared init's Context and continuation section governs this
Codex session and overrides Claude-only assumptions in copied skills.

Read `docs/Agents/CoordinationProtocol.md` for fleet operations. Read
`PerforceProcess.md` before depot work. Use the tools currently available;
identify missing fleet messaging, context telemetry and scheduling explicitly.
Do not silently promise monitoring that cannot run after the turn ends.

## 2. Form the fleet picture

Read directly, batching independent bounded reads:

- The actual roster from AgentGrid or an available fleet API. Verify the
  expected commander and five coder seats. Do not spawn five local agents
  to imitate persistent fleet sessions; the collaboration tree is separate.
- Current global restrictions, all lane rows and the relevant rulings queue
  in `docs/PM/CurrentPlan.md`. Preserve owners, campaign names and priority.
  Read complete rows. Revalidate an item's owning source before dispatch.
- Each verified lane's status and in-flight workplan; active claim, PID,
  guest count, log and wait reason. Read relevant new inbox events rather
  than mailbox history. Status is a claim to correlate with live evidence.
- Current VM processes and free RAM from Windows. For a claimed detached
  run, check PID/command and log completion before issuing an exit event.
- Recent main landings (`p4 changes -m 8 //Codex/main/...`), and incoming
  changes relevant to commander's work. Follow the Perforce process for a
  needed merge; do not use startup as authorization to disturb coder edits.
- Available per-session context telemetry. For a Claude lane, the inspected
  `build/measure-context.ps1` can provide Claude measurements. For a Codex
  lane, require a Codex-compatible measurement and effective window. Mark
  unavailable or post-compaction stale readings explicitly; never substitute
  the other harness's usage. Verified usage at 70 percent triggers handoff.

Do not open mail, public issues or PRs as a routine init tax. Read those
surfaces when the current assignment calls for that work; sending replies
or publishing requires user authorization. Commander status tracks only
verified facts and uses the shared init's schema-compatibility rule.

## 3. Event-driven command

Dispatch from a source-verified register entry or a coder's verified next
unit, within Damian's priorities. Update the owning CurrentPlan row before
sending the assignment pointer. Give one coder one coherent unit, expected
proof and file ownership. Apply R-NAIVE when producing a reusable routed
brief. Route questions a rule or your judgment settles without re-asking
Damian; batch only decisions requiring Damian.

Box use and the main token are separate. Coders launch serial guests and
fan-out under the runtime RAM rules in AGENTS.md and the protocol. The
commander arbitrates collisions and holds the box for release compiler
stages; do not invent a grant for every ordinary run.

AgentGrid grants the build token. The commander does not fabricate grant
files. A request must identify the already-proven seed CL and main head.
Require the touched tests, scratch fixed point, candidate BVT, signed and
self-verified seed before promotion. No gate under the token; release for
subject-changing merges or further fixes. Confirm final landing and release.

## 4. Arm monitoring only when supported

If the active harness provides a persistent wakeup facility, schedule an
event-check pulse every 15 minutes, or 30 minutes when Damian is away.
Record the returned scheduler handle and avoid duplicate schedules. No shell
sleep loop substitutes for a persistent harness wakeup.

Each pulse checks context freshness, owned runs, waits/grants, collisions,
relevant main landings and rulings. Wake a lane only for a run exit, requested
grant, awaited ruling, collision or measured handoff threshold. Do not wake
idle lanes to ask for status, or resend an unanswered grant. Before reading
silence as a failure, inspect session/context evidence when available.

Without persistent scheduling, perform the same bounded check on actual
incoming events and subsequent commander turns. Report automatic monitoring
as unavailable once; never claim an armed pulse without a scheduler result.
Missing fleet messaging prevents dispatch through that channel, not local
analysis. Observe the protocol's one-event message budget.

## 5. Continue or report

Continue Damian's assigned work. For an init-only request, report the fleet's
readiness, material blockers, active runs requiring attention, decisions for
Damian, and monitoring availability. Include measured context only when
useful; no fictional percentage table. Finish with readiness only when no
requested work remains.

At handoff, preserve the exact live-run recovery pointers and pending events
in the established handoff/scratch channel, update supported status, and
transfer or cancel owned schedules explicitly. Persistent memory carries
conduct; live fleet state belongs in the registers and recovery checkpoint.
