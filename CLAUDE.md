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
3. **Ping-pong before you push backend changes.** Any change to `Emit.X86_64`,
   `Emit.RiscV`, `Emit.Arm64`, or `IR` MUST pass the bare-metal ping-pong test
   before pushing. No exceptions. Run it via:
   ```bash
   wsl bash tools/pingpong.sh
   ```
   If it fails, your change is broken. Fix it before pushing.
4. **Clean intermediates.** Clean stale build outputs at session start (see above). Delete `.bak`, `.new`, `.tmp`, `.snap`, scratch scripts before ending.
5. **One logical change per commit.** Don't bundle unrelated fixes.
6. **Track workstream status.** When starting or completing a workstream, update it:
   ```bash
   tools/codex-agent/codex-status.exe update <id> --status <active|done|fixed|blocked> --summary "what changed" --agent cam
   ```
   Check current status with `tools/codex-agent/codex-status.exe list`.
   Create new workstreams with `tools/codex-agent/codex-status.exe create <id> --title "..." --status active`.
   **File bugs immediately.** If you discover an unreported bug — even if you aren't fixing it now — create a status entry so it doesn't get lost:
   ```bash
   tools/codex-agent/codex-status.exe create <bug-id> --title "Brief description" --status active --summary "what you observed" --agent cam
   ```
   **Cross-agent bug workflow:**
   1. Discover bug → `codex-status create <bug-id> ...`
   2. Commit as `docs:` → push to master (no branch needed — it's a coordination artifact)
   3. Other agents pull master → see it via `codex-status list` or `git log`
   4. Assignee picks it up → updates status to `investigating`, works on a feature branch, updates to `fixed` when done
7. **Ask the user** when unsure. A 10-second question beats a 10-minute wrong turn.
8. **Two-failures rule.** If the same approach fails twice, stop and switch strategies.

## Ping-Pong (Bare-Metal Self-Compile Smoke Test)

This is the proof that the compiler works. It compiles itself on bare metal,
then compiles itself again with that output, and the two must match.

**What it is:** codex source → ELF kernel → C# output (stage 1) → compile
stage 1 back to ELF → feed source again → C# output (stage 2) → stage 1 == stage 2.

**How to run it:** `wsl bash tools/pingpong.sh`

**When to run it:** Before pushing ANY change to native backend code.

**What it uses:**
- `dotnet run --project tools/Codex.Cli -- build Codex.Codex --target x86-64-bare` to build the ELF
- `dotnet run --project tools/Codex.Bootstrap -- --dump-source` for the source (Unicode, converted to CCE by the serial reader)
- `qemu-system-x86_64` via WSL for bare-metal execution
- Source fed via serial pipe with `\x04` EOT terminator
- QEMU available in WSL at `/usr/bin/qemu-system-x86_64`

**Do NOT:**
- Confuse this with `dotnet run -- bootstrap` (that's the C# fixed-point check, different thing)
- Try to hand-roll the QEMU invocation from scratch — use the script
- Push backend changes without running this
- Claim "pre-existing issue" without verifying on the commit before your change

## Scope of Authority

**Freely modifiable**: `src/`, `tests/`, `tools/`, `samples/`, `editors/`, `generated-output/`, `Codex.Codex/`
**With care**: `README.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, `.gitignore`, `Codex.sln`
**Do not modify without permission**: `Directory.Build.props`, `docs/Vision/*`
