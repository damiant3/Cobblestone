# CLAUDE.md — Cam (Claude Code Agent)

## Session Start

Run the agent toolkit orient command at the beginning of every session:

```bash
tools/codex-agent/codex-agent.exe orient
```

This provides project structure, style, syntax, pipeline, agents, git workflow,
and known conditions. Use `orient <topic>` for detail on any area.

**Clean stale intermediates before doing anything else:**

```bash
git clean -fd samples/
rm -f Codex.Codex/out/*.cs Codex.Codex/out/*.elf Codex.Codex/out/*.dll
rm -rf generated-output/* .vs
find . -type d \( -name bin -o -name obj \) -not -path './.git/*' -exec rm -rf {} + 2>/dev/null
```

Stale `.elf`, `.dll`, `.cs` outputs from previous sessions cause false test
results — you test yesterday's codegen against today's type system. Same class
as QEMU tests silently skipping. Always rebuild from current source.

**Run a directory listing to confirm the workspace is clean:**

```bash
find . -type f -not -path './.git/*' -not -path '*/node_modules/*' -not -path './.codex-agent/snapshots/*' -not -path './.codex-agent/history/*' -not -path './.handoff/*' -not -path './phone/flash/*' | sort
```

## Session Rules

1. **Read before you write.** Always read a file before editing it.
2. **Build before you commit.** `dotnet build Codex.sln` + `dotnet test Codex.sln`.
3. **Ping-pong before you push backend changes.** Any change to `Emit.X86_64`
   or `IR` MUST pass the bare-metal ping-pong test before pushing. No exceptions.
   Run it via:
   ```bash
   wsl bash tools/pingpong.sh
   ```
   If it fails, your change is broken. Fix it before pushing.
   **ARM64 and RISC-V are abandoned. Do not work on them.**
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

This is the proof that the compiler works. Codex code is compiled by the Codex
compiler, producing new Codex code. That output is compiled again by the Codex
compiler, producing a third copy. All three must match.

**What it is:** codex code (a) → codex compiler → codex code (b) → codex compiler → codex code (c) → a == b == c.

**How to run it:** `wsl bash tools/pingpong.sh`

**When to run it:** Before pushing ANY change to native backend code.

**What it uses:**
- `dotnet run --project tools/Codex.Cli -- build Codex.Codex --target x86-64-bare` to build the ELF
- `dotnet run --project tools/Codex.Bootstrap -- --dump-source` for the source (Unicode, converted to CCE by the serial reader)
- `qemu-system-x86_64` via WSL for bare-metal execution
- Source fed via serial pipe with `\x04` EOT terminator
- QEMU available in WSL at `/usr/bin/qemu-system-x86_64`

**Instrumentation (performance summary table):**

The script reports a summary table after both stages:

```
═══ Performance Summary ═══
Stage       Output    Time     Stack HWM      Heap HWM    QEMU RSS
Stage 1     261654     28s      32768 B       1048576 B    98304 kB
Stage 2     261654     29s      32768 B       1048576 B    97280 kB
```

- **Time:** Wall-clock per stage (1s grain, `$SECONDS`).
- **Stack HWM:** Parsed from `STACK:nnnnn` emitted by the bare-metal kernel after each compilation. Already implemented.
- **Heap HWM:** Parsed from `HEAP:nnnnn` on serial. **Cam: the allocator must emit this.** Emit `HEAP:<decimal-bytes>\n` on the serial port after compilation completes, same pattern as `STACK:`. The shell script already parses `^HEAP:\K[0-9]+` and feeds it into the table — it shows "—" until emission is wired up.
- **QEMU RSS:** Host-side peak resident set size of the `qemu-system-x86_64` process, captured via `/usr/bin/time -v`. Proxy for total guest memory pressure.

The `STACK:` and `HEAP:` lines are stripped from stage output before the fixed-point comparison, so they don't affect the diff.

**Do NOT:**
- Confuse this with `dotnet run -- bootstrap` (that's the C# fixed-point check, different thing)
- Try to hand-roll the QEMU invocation from scratch — use the script
- Push backend changes without running this
- Claim "pre-existing issue" without verifying on the commit before your change

## Scope of Authority

**Freely modifiable**: `src/`, `tests/`, `tools/`, `samples/`, `editors/`, `generated-output/`, `Codex.Codex/`
**With care**: `README.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, `.gitignore`, `Codex.sln`
**Do not modify without permission**: `Directory.Build.props`, `docs/Vision/*`
