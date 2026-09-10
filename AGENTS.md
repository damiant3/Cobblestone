# Codex / GPT-6 Astra project instructions

## Project and harness

The project named Codex is a language, self-hosted compiler, bare-metal
runtime, repository protocol and tools. The coding agent named Codex is
the OpenAI harness. Keep those meanings distinct.

The canonical compiler artifact is `seed/Codex.cdx`, booted by
`tools/codex-vm.exe` or QEMU multiboot. The compiler reproduces itself on
bare metal; verify the particular candidate before claiming a fixed point.
The compiler emits CDX or text. Plugs in `codex/plugs/` produce container
formats. The founding vision is `docs/PM/Stories/Vision/NewRepository.txt`.
Do not edit, invoke or rebuild anything under `old/`; the C# reference
compiler is retired.

These are the Codex/Astra instructions. `CLAUDE.md` and `.claude/skills/`
serve the Claude harness; do not load those files as another startup layer.
Use the tools and model settings actually supplied by the current harness.
A model name in a document does not select a model or create a capability.

## Session start and roles

The fleet consists of one commander and five coding agents. The commander
reads and executes `.agents/skills/commander-init/SKILL.md`; each coder
reads and executes `.agents/skills/init/SKILL.md`. In Codex, `$init` and
`$commander-init` refer to those skills. Reading and following the file is
sufficient when no skill-invocation tool exists.

Keep workspace lane, conversation role and fleet role separate. Derive the
lane from everything after the first `-` in `Cobblestone-<lane>`, and verify
`.p4config` and `.agentgrid`. Codex's conversation identifier `/root` means
the lead of a tool delegation tree; that identifier alone does not make a
coder the fleet commander. Use the user's assignment and matching fleet
identity to establish fleet command. Subagents inherit the assigned task,
not a new fleet seat, mailbox or full session-init obligation.

Init reads current state directly and routes subject-specific reading on
demand. No routine init subagents, whole-fleet design scan or archive read.
After compaction, preserve the active request and completed work; refresh
volatile state before acting, rather than repeating cold init.

## Working with Damian

Carry an authorized task through implementation, relevant verification and
its project landing procedure. Resolve routine choices from the request,
source and rules. Ask only when a missing answer changes scope, correctness
or authorization and cannot be resolved from available evidence. Continue
independent work while an answer is pending. An assessment request ends
with the assessment; do not turn the assessment into an unrequested fix.

The request defines the deliverable. Do not widen, narrow or substitute the
scope, refactor adjacent code or add a feature because the feature is easy.
Put a verified unrelated gap in the owning register. Avoid abstractions,
comments, docstrings and type annotations without a concrete need.

Use brief, outcome-first prose. Give an opening line and meaningful progress
updates at the harness-required cadence; omit routine process narration.
Report the changed behavior, landing, validation and material limitations.
A real failure takes the space needed to explain the failure accurately.
For messages to Damian, use the CPL axioms and banned-word table in
`docs/DevelopersGuide.md`, section `Codex Prose Language (CPL)` through
`CPL Sentence Forms`. Keep uncertainty explicit with named facts and gaps.

## Rule priority

System and harness instructions apply above project instructions. Damian's
direct request overrides project defaults. Within project rules, the tiers
below settle conflicts; a lower tier still binds where no conflict exists.
If applicable rules of equal priority disagree and evidence cannot settle
the conflict, ask. A coder routes fleet questions to the assigned commander
when a verified channel exists. Do not block Damian's direct task merely
because that channel is absent. Cite stable rule IDs, never bare numbers.

| Tier | Protection | Rules |
|---|---|---|
| 1 | Truth | R-TRUE, R-GATE |
| 2 | Artifact | R-READ, R-COST, R-CCE, R-OPENING, R-SIGN |
| 3 | Process | R-DIAG, R-ONE, R-SHELL, R-NAIVE |
| 4 | Form | R-REPORT, R-DASH, R-PROSE, R-HISTORY |

## Truth and verification

**R-TRUE:** Report only claims supported by the current session's evidence.
Distinguish observed results, inference and unverified work. Report a red
proof, wrong shipped byte, omitted required test or consequential correction
explicitly, with the failure output. A pre-existing failure remains a failure.
Do not turn missing tools, truncated output or an empty filtered error into
success. Check command completion and read diagnostics before filtering.

**R-GATE:** Run the tests the change can break, compiled and run one at a
time. No failures before copy-up. Documentation-only changes need document
validation, no compiler gate. Do not broaden a passing check without a new
change, failure or unresolved concern. Batch a coherent multi-CL arc's proof
at the landing.

- `build/build.ps1 -Internal` is banned. Bare `build/build.ps1` is Damian's
  release gate. `build/test.ps1` is Damian's full battery; `-All` is reserved
  for authorized releases. Read the release skill only for a public release
  request. Never claim a focused pass proves the full battery.
- Read every build-process script before executing the script. For a single
  compile, supply `-Src`, `-Out`, mandatory `-Log`, and explicit `-Kernel`
  to `build/compile.ps1`. Record the printed `kernel:` digest. The default
  build-output kernel is not evidence of the depot compiler's identity.
- A seed-affecting change also requires a scratch fixed point from the depot
  seed (stage 2 equals stage 3), `build/bvt.ps1` over the candidate, signing,
  and `test-self-verify` confirming the candidate verifies itself before
  installation. Compare the candidate's whole-file hash with the depot
  seed separately; a fixed point and equality with the depot are different
  claims. The content digest excludes the signature.
- Prove before requesting the AgentGrid build token. The token serializes
  seed-affecting landings on main, not box use. Under the token: recheck head,
  inspect incoming changes, submit, copy up, release. No gate runs under the
  token. If the proof's subject moved or code needs fixing, release and
  re-prove outside the hold. Two reds end the grant. Work leaving the seed
  unchanged needs no token. Read the coordination protocol for the mechanism.
- Measure available RAM immediately before a run. A serial guest launches
  unasked above 1.5 GiB free; fan-out uses the per-guest budget in
  `docs/Agents/CoordinationProtocol.md`, `The token does not cover RAM`.
  Read that section before the first run; do not reuse an old free-RAM value.
  Launch long VM runs detached, record guest count, PID, log and ownership,
  and monitor through available harness tools. Audit and remove your own
  orphaned watchers, guests and servers after cancellation and at handoff.

## Artifact and implementation

**R-READ:** Read the target and relevant callers before editing. Inspect
existing edits first, preserve other work, and prefer a targeted patch.

**R-DIAG:** Read the failure site and form a source-grounded hypothesis
before building to test that hypothesis.

**R-ONE:** Finish one coherent change, validate and submit, then proceed.

**R-COST:** Every code review gives an explicit heap and time-complexity
verdict. Inspect new loops, recursion, accumulators and per-object allocation
cost multiplied by object count. Watch `buf-read-bytes`, buffer/List round
trips, retained AST/IR and missed `heap-save`/`heap-restore` boundaries. There
is no GC. Inspection is enough when no risk is introduced; if uncertain,
measure relevant heap high-water and elapsed time before and after. For
instruction-only work, state that compiler heap/time behavior is unchanged.

**R-CCE:** Internal compiler operations use Codex Character Encoding.
Unicode conversion belongs at I/O boundaries.

**R-OPENING:** The program entry point is `opening`.

**R-SIGN:** Signing is automatic. Never print or document the key path.
Repair signing failures in the build scripts.

**R-SHELL:** Use PowerShell for shell work, never Git Bash. Prefer `rg` and
`rg --files`; if unavailable use bounded PowerShell searches and explicit
text decoding. Use the available patch tool for edits and preserve encoding
and CRLF. Python is banned for ordinary tool use, scripting and automation.
Use PowerShell or Codex. Use Python only when required for the task or when
Damian explicitly authorizes an exception; convenience or an existing Python
helper is not necessity. A skill's preferred Python workflow alone does not
establish an exception. Run any required interpreter from PowerShell.
No Unix dependency on the build path. The existing verification-only
exceptions are live WSL GDB and
Prism stage-5a user-mode ELF runs, following their subject documentation.

**R-NAIVE:** A reusable instruction, design, run sheet, handoff, memory file,
probe or diagnostic whose value depends on a reader lacking your context
gets one independent reader pass. A routed brief and a claim of clarity or
discoverability also trigger the pass. Give a fresh subagent the task or
symptom, let the agent find the artifact, and require file:line evidence,
confidence and a falsifier. Do not leak the answer. This authorizes bounded
reader validation, not routine verification of your own code by subagent.
If delegation is unavailable, state that the reader check remains unrun.

Other delegation requires a user request or an applicable skill's explicit
instruction, an independent bounded task and useful local work in parallel.
Use the available collaboration tools and inherit the model by default.
Local subagents do not replace the five persistent fleet coding agents.

## Documents and durable state

**R-REPORT:** Report what Damian can act on. The CL holds routine detail.
**R-DASH:** No em-dash, or en-dash outside numeric ranges, in authored text.
Use ASCII in CL descriptions.
**R-PROSE:** Source prose is justified only for external formats/hardware,
magic numbers, or performance/crackability constraints. Remove other prose
in source files already being changed; do not sweep unrelated files.
**R-HISTORY:** Documents state current contracts and open work. Replace
changed rows; put chronology in CL descriptions. Remove obsolete narrative
in documents already being changed without discarding unique active facts.

- `docs/PM/CurrentPlan.md` owns cross-lane priorities; lane rows carry NOW,
  NEXT and standing. Read the owning app/quire backlog before declaring
  work complete. Do not recreate `docs/PM/BACKLOG.md`.
- `docs/Agents/<lane>-workplan.md` is temporary in-flight scratch, emptied
  at completed handoff. Durable facts belong in the reference/design that
  owns the subject, gaps in the owning backlog, lessons in `LESSONS.md`.
  Do not create a second findings outbox or shadow priority register.
- Read `docs/PM/Active/Stories/LESSONS.md` at init. Before relying on a
  lesson, read the linked evidence in full. Do not load `docs/Probes/` at
  init; those files contain answer keys.
- `docs/Designs/Active/` contains live designs. Finished campaigns belong
  in `docs/Designs/Done/`. Archives and surveys are on-demand reading.
- Verify a claim that a document is wrong against the source before editing.
  Date measured counts; remeasure before using a count to make a decision.
  Correct drift during relevant edits rather than a standalone count sweep.

## Perforce and coordination

Read `docs/Agents/PerforceProcess.md` before Perforce operations beyond
`p4 edit` and `p4 submit`; use the documented procedure for the operation.
Validate the workspace/client first. Preserve existing shelves and edits.
Create a numbered CL headlessly with an ASCII description, open explicit
paths, inspect diffs and opened files, and report the final submitted number.
Docs-only work goes directly through the lane's MAIN client, without a gate.
GitHub and GitLab are downstream release mirrors, not the source repository.

Before a proof, follow the documented isolation procedure: shelve, verify
shelf coverage, revert, sync -f, unshelve, resolve and inspect on-disk state.
Never revert or clean unknown work merely to make init look clean. A merge
must preserve both sides semantically; a clean auto-merge is not that proof.

Read `docs/Agents/CoordinationProtocol.md` before fleet mutation or the first
VM run/token request. Write only your verified mailbox. A leftover pointer
or status does not establish that this Codex session owns a Claude seat.
Do not release another session's grant or overwrite another session's run.
Fleet messages require an authorized verified channel: one addressee, one
message per event, at most 300 characters, pointers rather than copied docs.
An idle lane is not an event. External mail and public posting need explicit
user authorization; startup does not implicitly authorize sending messages.

For Codex context measurement, resumption and capability gaps, follow the
init skill's `Context and continuation` section. That section governs Codex
when a copied skill assumes Claude transcripts or unavailable tools.
