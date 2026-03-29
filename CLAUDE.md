# CLAUDE.md — Cam (Claude Code Agent)

## Session Start

Run the agent toolkit orient command at the beginning of every session:

```bash
tools/codex-agent/codex-agent.exe orient
```

This provides project structure, style, syntax, pipeline, agents, git workflow,
and known conditions. Use `orient <topic>` for detail on any area.

**Clean stale intermediates before doing anything else:**

```powershell
git clean -fd samples/
Remove-Item -Force Codex.Codex/out/*.cs, Codex.Codex/out/*.elf, Codex.Codex/out/*.dll -ErrorAction SilentlyContinue
```

Stale `.elf`, `.dll`, `.cs` outputs from previous sessions cause false test
results — you test yesterday's codegen against today's type system. Same class
as QEMU tests silently skipping. Always rebuild from current source.

## Session Rules

1. **Read before you write.** Always read a file before editing it.
2. **Build before you commit.** `dotnet build Codex.sln` + `dotnet test Codex.sln`.
3. **Clean intermediates.** Clean stale build outputs at session start (see above). Delete `.bak`, `.new`, `.tmp`, `.snap`, scratch scripts before ending.
4. **One logical change per commit.** Don't bundle unrelated fixes.
5. **Track workstream status.** When starting or completing a workstream, update its status:
   ```bash
   tools/codex-agent/codex-status.exe update <id> --status <active|done|fixed|blocked> --summary "what changed" --agent cam
   ```
   Check current status with `tools/codex-agent/codex-status.exe list`.
   Create new workstreams with `tools/codex-agent/codex-status.exe create <id> --title "..." --status active`.
6. **Ask the user** when unsure. A 10-second question beats a 10-minute wrong turn.
7. **Two-failures rule.** If the same approach fails twice, stop and switch strategies.

## Scope of Authority

**Freely modifiable**: `src/`, `tests/`, `tools/`, `samples/`, `editors/`, `generated-output/`, `Codex.Codex/`
**With care**: `README.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, `.gitignore`, `Codex.sln`
**Do not modify without permission**: `Directory.Build.props`, `docs/Vision/*`
