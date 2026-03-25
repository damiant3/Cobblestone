# Tool Error Registry

Persistent record of tool failures encountered during agent sessions.
Tracked to build the case for better tooling. Read by `codex-agent doctor`.

Last updated: 2026-03-25

## Error Classes

### TEF-001: edit_file drops or swallows lines

**Severity**: HIGH — silent data loss
**Tool**: `edit_file` (VS Copilot built-in)
**Symptom**: Lines at the top of a file (especially `# heading` on line 1) are silently
dropped. The tool reports success but the line is gone. No error, no warning.
**Frequency**: Common on markdown files. Observed on files as small as 240 lines.
**Workaround**: Use `create_file` + swap for any file where line 1 content matters.
Always verify with `codex-agent peek` (not `get_file`) after any edit.

### TEF-002: edit_file produces no edits

**Severity**: MEDIUM — wastes retries, triggers two-failures rule
**Tool**: `edit_file` (VS Copilot built-in)
**Symptom**: "No edits were produced from the mapping operation." The tool cannot map
the intended change onto the file, even when the change is trivial (e.g., inserting
a single line at the top of a file).
**Frequency**: Common when editing near file boundaries (line 1, last line) or when
the surrounding context is markdown rather than code.
**Workaround**: Do not retry. Switch immediately to `create_file` + swap.

### TEF-003: get_file renders markdown headings invisibly

**Severity**: MEDIUM — verification is unreliable
**Tool**: `get_file` (VS Copilot built-in)
**Symptom**: Lines beginning with `# ` are consumed as rendered markdown headings in
the tool output. The line content is invisible to the agent. This makes it impossible
to verify whether a `# heading` line exists using `get_file` alone.
**Frequency**: Always, on any markdown file with `#` headings.
**Workaround**: Use `codex-agent peek` for verification instead of `get_file`.
`peek` shows raw line content with line numbers.

### TEF-004: Terminal write mangles encoding

**Severity**: HIGH — silent data corruption
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

**Severity**: CRITICAL — silent semantic corruption
**Tool**: `edit_file` / `create_file` (VS Copilot built-in)
**Symptom**: The keyword `else` is silently replaced with `then` in `.codex` files,
producing syntactically plausible but semantically wrong code. The corruption inverts
conditional logic without any visible error.
**Frequency**: Recurring. Observed across multiple sessions and files.
**Workaround**: After ANY `.codex` edit, `snap diff` and grep for suspicious `then` patterns.
See docs/KNOWN-CONDITIONS.md for full details.

### TEF-006: create_file content truncation at boundaries

**Severity**: LOW — rare, but compounds with TEF-001
**Tool**: `create_file` (VS Copilot built-in)
**Symptom**: Leading blank lines or trailing newlines may be added or removed
unpredictably. Usually harmless but can interact with TEF-001 if the first
meaningful line is a heading.
**Frequency**: Occasional.
**Workaround**: Always verify with `codex-agent peek <file> 1 5` after `create_file`.

### TEF-007: run_command_in_terminal mangles multi-line commands

**Severity**: HIGH — silent command concatenation, wrong commands executed
**Tool**: `run_command_in_terminal` (VS Copilot built-in)
**Symptom**: When an agent passes multiple lines of PowerShell as a single command,
the terminal concatenates them unpredictably. Newlines are lost or joined with
adjacent text, producing mangled command names or truncated paths. Commands partially
execute — some lines run, some don't, some fuse into nonsense.
**Frequency**: Always, on any multi-line terminal command.
**Workaround**: NEVER send multi-line commands. One command per `run_command_in_terminal`
call, always. If you need N commands, make N separate tool calls. If the operation
truly requires a script, write a `.ps1` with `create_file`, run with `pwsh -File`,
then delete it.

### TEF-008: edit_file injects metadata into file content

**Severity**: HIGH — silent content pollution
**Tool**: `edit_file` (VS Copilot built-in)
**Symptom**: After an `edit_file` call on a markdown file, lines like
`File: docs\TOOL-ERROR-REGISTRY.md` and markdown code fences are injected at the
top of the file as actual content. These are tool metadata that should never appear
in the file. The agent may not notice because `get_file` renders them as part of
its own output framing (see TEF-003).
**Frequency**: Observed on markdown files after `edit_file` edits.
**Workaround**: Always verify with `codex-agent peek <file> 1 5` after ANY edit.
If metadata lines appear, use the safe path (create_file + swap) to rewrite the file.

---

## Incident Log

Format: `DATE | ERROR-CLASS | FILE | DESCRIPTION | OUTCOME`

```
2026-03-25 | TEF-008 | docs/TOOL-ERROR-REGISTRY.md | edit_file injected "File:" header and code fence as lines 1-2 | rewrote via create_file
2026-03-25 | TEF-001 | .github/copilot-instructions.md | edit_file dropped # heading on line 1 | recovered via snap restore
2026-03-25 | TEF-002 | .github/copilot-instructions.md | 3 consecutive "no edits produced" on trivial line-1 insert | switched to create_file
2026-03-25 | TEF-004 | .github/copilot-instructions.md | terminal WriteAllText mangled em dashes to garbage | recovered via snap restore
2026-03-25 | TEF-003 | .github/copilot-instructions.md | get_file could not show # heading line, appeared missing | used codex-agent peek to confirm
2026-03-25 | TEF-007 | (terminal) | multi-line Remove-Item fused into garbage, partial execution | re-ran as individual commands
2026-03-21 | TEF-005 | tools/codex-agent/codex-agent.codex | else->then swap on line 53 | caught by snap diff
2026-03-21 | TEF-005 | tools/codex-agent/peek.codex | else->then swap in if/then/else chain | caught by snap diff
```

---

## Aggregate Counts

| Error Class | Total | Last 7 days | Severity |
|-------------|-------|-------------|----------|
| TEF-001 edit_file drops lines | 1 | 1 | HIGH |
| TEF-002 edit_file no edits | 3 | 3 | MEDIUM |
| TEF-003 get_file hides headings | 1 | 1 | MEDIUM |
| TEF-004 terminal encoding | 1 | 1 | HIGH |
| TEF-005 else/then swap | 2+ | 0 | CRITICAL |
| TEF-006 boundary truncation | 1 | 1 | LOW |
| TEF-007 terminal multi-line | 1 | 1 | HIGH |
| TEF-008 edit_file injects metadata | 1 | 1 | HIGH |

**Total tool failures this session**: 9
**Files requiring recovery**: 2
**Recovery method**: snap restore + create_file rewrite

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
