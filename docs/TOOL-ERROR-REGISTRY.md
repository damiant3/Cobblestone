# Tool Error Registry

Persistent record of tool failures encountered during agent sessions.
Tracked to build the case for better tooling. Read by `codex-agent doctor`.

Last updated: 2026-03-25

## Error Classes

### TEF-001: edit_file drops or swallows lines

**Severity**: HIGH - silent data loss
**Tool**: `edit_file` (VS Copilot built-in)
**Symptom**: Lines at the top of a file (especially `# heading` on line 1) are silently
dropped. The tool reports success but the line is gone. No error, no warning.
A variant duplicates entire sections: content is inserted but the original block
it should replace remains, producing a file with two copies of the same section.
**Frequency**: Common on markdown files. Observed on files as small as 152 lines.
**Workaround**: Use `create_file` + swap for any file where line 1 content matters.
Always verify with `codex-agent peek` (not `get_file`) after any edit.

### TEF-002: edit_file produces no edits

**Severity**: MEDIUM - wastes retries, triggers two-failures rule
**Tool**: `edit_file` (VS Copilot built-in)
**Symptom**: "No edits were produced from the mapping operation." The tool cannot map
the intended change onto the file, even when the change is trivial (e.g., inserting
a single line at the top of a file).
**Frequency**: Common when editing near file boundaries (line 1, last line) or when
the surrounding context is markdown rather than code.
**Workaround**: Do not retry. Switch immediately to `create_file` + swap.

### TEF-003: get_file renders markdown headings invisibly

**Severity**: MEDIUM - verification is unreliable
**Tool**: `get_file` (VS Copilot built-in)
**Symptom**: Lines beginning with `# ` are consumed as rendered markdown headings in
the tool output. The line content is invisible to the agent. This makes it impossible
to verify whether a `# heading` line exists using `get_file` alone.
**Frequency**: Always, on any markdown file with `#` headings.
**Workaround**: Use `codex-agent peek` for verification instead of `get_file`.
`peek` shows raw line content with line numbers.

### TEF-004: Terminal write mangles encoding

**Severity**: HIGH - silent data corruption
**Tool**: `run_command_in_terminal` with file write operations
**Symptom**: Writing file content via PowerShell (`Set-Content`, string concatenation,
`[System.IO.File]::WriteAllText`) corrupts non-ASCII characters. Em dashes become
mojibake, arrows become garbage. The file looks correct in the terminal output but
is corrupted on disk.
**Frequency**: Always, when writing UTF-8 content with non-ASCII characters via terminal.
**Workaround**: NEVER write files via terminal. Use `create_file` or `edit_file` only.
This is already documented in copilot-instructions.md Terminal Discipline but the
temptation arises when `edit_file` fails (see TEF-001, TEF-002).

### TEF-005: edit_file `else` to `then` keyword swap

**Severity**: CRITICAL - silent semantic corruption
**Tool**: `edit_file` / `create_file` (VS Copilot built-in)
**Symptom**: The keyword `else` is silently replaced with `then` in `.codex` files,
producing syntactically plausible but semantically wrong code. The corruption inverts
conditional logic without any visible error.
**Frequency**: Recurring. Observed across multiple sessions and files.
**Workaround**: After ANY `.codex` edit, `snap diff` and grep for suspicious `then` patterns.
See docs/KNOWN-CONDITIONS.md for full details.

### TEF-006: create_file content truncation at boundaries

**Severity**: LOW - rare, but compounds with TEF-001
**Tool**: `create_file` (VS Copilot built-in)
**Symptom**: Leading blank lines or trailing newlines may be added or removed
unpredictably. Usually harmless but can interact with TEF-001 if the first
meaningful line is a heading.
**Frequency**: Occasional.
**Workaround**: Always verify with `codex-agent peek <file> 1 5` after `create_file`.

### TEF-007: run_command_in_terminal mangles multi-line commands

**Severity**: HIGH - silent command concatenation, wrong commands executed
**Tool**: `run_command_in_terminal` (VS Copilot built-in)
**Symptom**: When an agent passes multiple lines of PowerShell as a single command,
the terminal concatenates them unpredictably. Newlines are lost or joined with
adjacent text, producing mangled command names or truncated paths. Commands partially
execute - some lines run, some don't, some fuse into nonsense.
**Frequency**: Always, on any multi-line terminal command.
**Workaround**: NEVER send multi-line commands. One command per `run_command_in_terminal`
call, always. If you need N commands, make N separate tool calls. If the operation
truly requires a script, write a `.ps1` with `create_file`, run with `pwsh -File`,
then delete it.

### TEF-008: edit_file injects metadata into file content

**Severity**: HIGH - silent content pollution
**Tool**: `edit_file` (VS Copilot built-in)
**Symptom**: After an `edit_file` call on a markdown file, lines like
`File: docs\TOOL-ERROR-REGISTRY.md` and markdown code fences are injected at the
top of the file as actual content. These are tool metadata that should never appear
in the file. The agent may not notice because `get_file` renders them as part of
its own output framing (see TEF-003).
**Frequency**: Every `edit_file` call on markdown files. 100% reproduction rate in
the 2026-03-25 session - including while editing *this file*.
**Workaround**: Always verify with `codex-agent peek <file> 1 5` after ANY edit.
If metadata lines appear, use the safe path (create_file + swap) to rewrite the file.
Now auto-stripped by `codex-agent-verify.ps1` and wired into `codex-agent build`.

### TEF-009: Agent ignores explicit workflow rules under optimization pressure

**Severity**: HIGH - unverified edits, cascading recovery failures
**Tool**: Not a tool bug. This is an agent behavioral failure.
**Symptom**: The agent reads `copilot-instructions.md`, understands the per-file
workflow (snap save, edit, verify, peek, snap diff), and then skips all of it when
faced with bulk or repetitive work. The agent chooses "efficiency" over the explicit
rules it just read. Consequences compound:

1. Files are edited without snapshots, so recovery requires `git checkout`.
2. `edit_file` silently corrupts a large file (TEF-001), deleting ~100 lines.
3. The agent cannot see the corruption because it uses `get_file` instead of `peek`.
4. Without a snapshot, the corruption cannot be undone without user intervention.
5. If the agent attempts to fix the corruption, it may trigger further TEF-001/008
   failures, consuming tool capacity in a recovery loop the user pays for.

**Root cause**: The VS Copilot system prompt (above `copilot-instructions.md` in the
prompt chain, not controllable by the repo owner) contains directives like "make
minimal modifications," "choose tools strategically," and "be efficient." These are
good general principles. But when a session presents 40+ files needing a mechanical
rename, the efficiency pressure overrides the explicit per-file ceremony. The agent
rationalizes a bulk approach (e.g., PowerShell script writing files via terminal)
that bypasses every safety check the toolkit exists to enforce.

**Why this is worse than TEF-001 through TEF-008**: Those are tool bugs with known
workarounds. TEF-009 is the agent skipping the workarounds. The entire toolkit,
the snap/verify/peek workflow, the safe path documentation - all of it exists
because of TEF-001 through TEF-008. When the agent bypasses the workflow, every
prior mitigation is nullified. One TEF-009 can reintroduce all the others.

**What the repo owner controls**:
- Framing violations as *producing broken output* rather than *breaking process*
  may help. The agent optimizes for output quality more reliably than process fidelity.
- Scoping tasks small enough that the "do it right" path and the "do it fast" path
  converge. A 3-file rename invites the safe workflow. A 46-file rename invites shortcuts.
- Automation that cannot be skipped (like `codex-agent build` running the verify sweep)
  catches what discipline misses.

**What the repo owner does not control**:
- The system prompt above `copilot-instructions.md`. Its efficiency directives compete
  with per-repo workflow rules, and each new session starts with zero memory of prior
  failures. The agent must re-learn this lesson every time.

**Frequency**: Observed on first bulk-edit task of a new session (2026-03-25).
The agent had just completed `init`, `greet`, `orient style` - all the onboarding
worked perfectly. The rules were read and acknowledged. Then the first real task
arrived and every rule was violated.

**Workaround**: Automation works. Discipline doesn't. (See TEF-008 mitigation.)
Require `snap save` evidence in the session log before any edit. Future toolkit
enhancement: `codex-agent edit <file>` wrapper that refuses to proceed without
a snapshot on record.

---

## Incident Log

Format: `DATE | ERROR-CLASS | FILE | DESCRIPTION | OUTCOME`

```
2026-03-25 | TEF-009 | (46 files) | Agent read all workflow rules then bypassed snap/verify/peek on bulk rename; edit_file deleted ~100 lines from ILAssemblyBuilder.cs unnoticed; PowerShell script wrote files via terminal | user intervention required, git undo
2026-03-25 | TEF-008 | tools/codex-agent/codex-agent.codex | edit_file injected "File:" header while wiring verify into build | cleaned via codex-agent-verify.ps1
2026-03-25 | TEF-008 | .github/copilot-instructions.md | edit_file injected "File:" header while adding sweep to session start | cleaned via codex-agent-verify.ps1
2026-03-25 | TEF-008 | docs/ToDo/CSharpCleanup.md | edit_file injected "File:" header while updating Section 9 exemption | shipped to origin, caught by user, cleaned post-push
2026-03-25 | TEF-008 | docs/CurrentPlan.md | edit_file injected "File:" header and code fence on markdown edit | cleaned via codex-agent-verify.ps1
2026-03-25 | TEF-008 | docs/TOOL-ERROR-REGISTRY.md | edit_file injected "File:" header again while updating TEF-001 description | cleaned via codex-agent-verify.ps1
2026-03-25 | TEF-001 | docs/CurrentPlan.md | edit_file dropped # Current Plan heading from line 1 during section insertion | recovered via snap restore + create_file rewrite
2026-03-25 | TEF-001 | docs/CurrentPlan.md | edit_file duplicated "What Remains" and "Process" sections instead of inserting before them | recovered via snap restore + create_file rewrite
2026-03-25 | TEF-008 | docs/TOOL-ERROR-REGISTRY.md | edit_file injected "File:" header and code fence as lines 1-2 | rewrote via create_file
2026-03-25 | TEF-001 | .github/copilot-instructions.md | edit_file dropped # heading on line 1 | recovered via snap restore
2026-03-25 | TEF-002 | .github/copilot-instructions.md | 3 consecutive "no edits produced" on trivial line-1 insert | switched to create_file
2026-03-25 | TEF-004 | .github/copilot-instructions.md | terminal WriteAllText mangled em dashes to garbage | recovered via snap restore
2026-03-25 | TEF-003 | .github/copilot-instructions.md | get_file could not show # heading line, appeared missing | used codex-agent peek to confirm
2026-03-25 | TEF-007 | (terminal) | multi-line Remove-Item fused into garbage, partial execution | re-ran as individual commands
2026-03-25 | TEF-008 | docs/OldStatus/DECISIONS.md | latent pollution from prior session, discovered by full docs sweep | cleaned via codex-agent-verify.ps1
2026-03-25 | TEF-008 | docs/OldStatus/X86-64-REVIEW-HANDOFF.md | latent pollution from prior session | cleaned via codex-agent-verify.ps1
2026-03-25 | TEF-008 | docs/Projects/PHONE-WIPE.md | latent pollution from prior session | cleaned via codex-agent-verify.ps1
2026-03-25 | TEF-008 | docs/Projects/TWRP-BUILD-HANDOFF.md | latent pollution from prior session | cleaned via codex-agent-verify.ps1
2026-03-21 | TEF-005 | tools/codex-agent/codex-agent.codex | else->then swap on line 53 | caught by snap diff
2026-03-21 | TEF-005 | tools/codex-agent/peek.codex | else->then swap in if/then/else chain | caught by snap diff
```

---

## Aggregate Counts

| Error Class | Total | Last 7 days | Severity |
|-------------|-------|-------------|----------|
| TEF-001 edit_file drops lines | 3 | 3 | HIGH |
| TEF-002 edit_file no edits | 3 | 3 | MEDIUM |
| TEF-003 get_file hides headings | 1 | 1 | MEDIUM |
| TEF-004 terminal encoding | 1 | 1 | HIGH |
| TEF-005 else/then swap | 2+ | 0 | CRITICAL |
| TEF-006 boundary truncation | 1 | 1 | LOW |
| TEF-007 terminal multi-line | 1 | 1 | HIGH |
| TEF-008 edit_file injects metadata | 10 | 10 | HIGH |
| TEF-009 agent ignores workflow rules | 1 | 1 | HIGH |

**Total tool failures logged**: 23+
**Files requiring recovery**: 4 (3 prior + ILAssemblyBuilder.cs via git undo)
**Recovery method**: snap restore + create_file rewrite; git checkout when no snap exists

---

## The Case for Change

The current file editing tools provided to VS Copilot agents are unreliable for
production use. In a single session editing a 240-line markdown file:

- 6 write failures (4 classes)
- 2 unreliable reads (1 class)
- 2 snap restores required
- 0 of 4 `edit_file` attempts succeeded on a trivial change
- The file documenting tool errors was itself corrupted by a tool error (TEF-008)

The "large file rule" (>300 lines requires special handling) should be the
**all file rule**. The safe workflow is:

1. `codex-agent snap save <file>`
2. `create_file` to `<file>.new`
3. `Copy-Item <file>.new <file> -Force`
4. `codex-agent peek <file> 1 10` to verify (NOT `get_file`)
5. `codex-agent snap diff <file>` to confirm delta
6. `Remove-Item <file>.new`

This is not optional caution. It is the only reliable path. The toolkit exists
because the native tools cannot be trusted.

---

## Turtles All the Way Down

On 2026-03-25, an agent session was dedicated to fixing the process failures
caused by tool errors. The session went like this:

1. Audit found style violations in C# code - written to `CSharpCleanup.md`.
2. `edit_file` injected TEF-008 pollution into `CSharpCleanup.md`. **Shipped to origin unnoticed.**
3. Updating `CurrentPlan.md` with audit results: `edit_file` dropped the heading (TEF-001),
   duplicated sections (TEF-001), and injected metadata (TEF-008). Three attempts, three failures.
   Recovered via snap restore + create_file rewrite.
4. Documenting those failures in this file: `edit_file` injected TEF-008 into *this file*.
5. Updating TEF-001's description to document the duplication variant: TEF-008 again, *in this file*.
6. Adding TEF-008 sweep to session start in `copilot-instructions.md`: TEF-008 again.
7. Full docs sweep discovered **4 more files** with latent TEF-008 from prior sessions - pollution
   that had been sitting in committed, pushed docs for days.
8. Wiring the verify script into `codex-agent build` so it can't be skipped:
   `edit_file` on `codex-agent.codex` injected TEF-008. The verify script caught it.
9. Writing this summary of tool failures while fixing tool failures: used `create_file` + swap
   because - at this point - any agent that uses `edit_file` on a markdown file deserves
   what it gets.

The tool that documents tool errors is corrupted by the tool error it documents.
The fix for the process failure is corrupted by the same process failure it fixes.
The file you are reading was written via the safe path specifically because the
normal path would have corrupted it while describing its own corruption.

It's turtles all the way down.

**Mitigation shipped**: `codex-agent build` now runs `codex-agent-verify.ps1` on all
key docs automatically. The sweep is in the path agents can't skip. The verify script
has a 100% catch rate on TEF-008. Automation works. Discipline doesn't.

### TEF-010: git push succeeds but branch disappears from remote

**Severity**: MEDIUM - silent push loss, delays collaboration
**Tool**: `git push` via CLI (Claude Code / Cam agent)
**Symptom**: `git push origin cam/mm3-shared-fixes` reports success (`* [new branch]`),
but the branch does not appear on GitHub. `git ls-remote origin` confirms the branch
is absent. A second push to the same remote and branch succeeds and persists.
**Frequency**: Observed once (2026-03-28, Cam session). May be GitHub propagation lag,
a repo-level branch auto-delete rule, or a credential/session issue.
**Workaround**: After pushing a new branch, verify with `git ls-remote origin | grep <branch>`.
If absent, push again.
