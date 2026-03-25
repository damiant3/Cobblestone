# Copilot Instructions for Codex

> Single file — VS Copilot only reads `.github/copilot-instructions.md`.
> Keep this lean. Every char here is burned on every prompt.

## Repository Identity

| | |
|---|---|
| **Repo** | `https://github.com/damiant3/NewRepository` |
| **Local** | `D:\Projects\NewRepository` (Windows workstation) |
| **Solution** | `Codex.sln` |
| **Agent** | Agent Windows (GitHub Copilot in Visual Studio) |
| **Git identity** | `Agent Windows` / `agent-windows@codex.dev` |

## What This Repository Is

Codex is a self-hosting programming language compiler (written in Codex, compiles itself).
C# bootstrap (.NET 8) is locked. Solution: `Codex.sln`. Pipeline:

```
Source (.codex) → Lexer → Parser → Desugarer → NameResolver → TypeChecker → Lowering → Emitter
```

15 backends (12 transpilation + IL + RISC-V + ARM64 + x86-64 + WASM + bare metal).
890+ tests. Self-hosting achieved. Content-addressed fact store with network sync.

Design docs: `docs/00-OVERVIEW.md`, `docs/10-PRINCIPLES.md`.
Do not modify Vision docs (`docs/Vision/`) ever.

### Agents

| Agent | Environment | Branch prefix | Identity |
|-------|-------------|---------------|----------|
| Windows | VS + Copilot, this workstation | `windows/` | `agent-windows@codex.dev` |
| Linux | Claude sandbox (remote) | `linux/` | `agent-linux@codex.dev` |
| Cam | CLI worktree `D:\Projects\NewRepository-cam` | `cam/` | Cam's git config |
| Nut | VS 2026 + Copilot, garage box | `nut/` | `agent-nut@codex.dev` |

No agent merges its own work to `master` without review (by another agent or user).

---

## Session Rules

1. **Read before you write.** Always read a file before editing it.
2. **Build before you commit.** Use `codex-agent build` + `codex-agent test`.
3. **Clean up temp files.** Delete `.bak`, `.new`, `.tmp`, `.snap`, scratch scripts before ending.
4. **One logical change per commit.** Don't bundle unrelated fixes.
5. **Leave a handoff.** After meaningful work, update `docs/OldStatus/` handoff docs.
6. **Ask the user** when unsure. A 10-second question beats a 10-minute wrong turn.
7. **Two-failures rule.** If the same approach fails twice, stop and switch strategies.

### checkdate()

Never trust training data for dates. Before writing any date, run:
```powershell
Get-Date -Format "yyyy-MM-dd"
```
The Codex project began ~**2026-03-13**. Any earlier date in project docs is wrong.

---

## Agent Toolkit — codex-agent

**Use this for all file ops.** It's a Codex-compiled .exe in `tools/codex-agent/`.

```
dotnet tools/codex-agent/codex-agent.exe <command> [args]
```

| Command | What it does |
|---------|-------------|
| `peek <file> [start] [end]` | Read file lines with line numbers (use `0 0` for whole file) |
| `stat <file> [file2] ...` | File stats: lines, chars, size category |
| `snap save <file>` | Snapshot before editing. **Required for files >100 lines.** |
| `snap diff <file>` | Compare current file to snapshot |
| `snap restore <file>` | Restore from snapshot if edit goes wrong |
| `status` | Project health check |
| `plan add <task>` | Stash a task (survives context loss) |
| `plan` / `plan show` | Show current task list |
| `plan clear` | Clear task list |
| `check` | Cognitive load estimate — hot-path sizes vs budget |
| `doctor` | Known conditions & diagnostics briefing (run at session start) |
| `handoff [show]` | Show current handoff state machine status |
| `handoff push <summary>` | Create/update handoff, auto-commits `.handoff`, requests review |
| `handoff review` | Pick up handoff for review (transitions to under-review) |
| `handoff approve` | Approve reviewed handoff |
| `handoff request-changes <reason>` | Request changes with reason |
| `handoff merge` | Merge approved branch to master (auto `git merge --no-ff`) |
| `handoff abandon` | Abandon handoff |
| `log <message>` | Append to persistent session log (decisions, errors, notes) |
| `recall [n]` | Show last N session log entries (default 10) |
| `build` | `dotnet build Codex.sln` — stores full log, prints compact summary |
| `test [filter]` | `dotnet test Codex.sln` — stores full log, prints compact summary |

### Session Start

```powershell
dotnet tools/codex-agent/codex-agent.exe doctor
dotnet tools/codex-agent/codex-agent.exe check
dotnet tools/codex-agent/codex-agent.exe status
dotnet tools/codex-agent/codex-agent.exe handoff show
pwsh -File tools/codex-agent-verify.ps1 docs/**/*.md   # sweep for TEF-008 pollution
```

Key doc locations (memorize, don't search):
- `docs/TOOL-ERROR-REGISTRY.md` — tool failure log
- `docs/KNOWN-CONDITIONS.md` — pre-existing issues
- `docs/CurrentPlan.md` — current plan and status
- `docs/ToDo/CSharpCleanup.md` — style audit tracker

On first session or after context loss, also orient:
```powershell
git -C D:\Projects\NewRepository log --oneline -5   # recent history
git -C D:\Projects\NewRepository branch -r           # outstanding branches
```

### Before Editing Any File

```powershell
dotnet tools/codex-agent/codex-agent.exe stat <file>
dotnet tools/codex-agent/codex-agent.exe peek <file> 1 30
dotnet tools/codex-agent/codex-agent.exe snap save <file>
# ... make edit ...
dotnet tools/codex-agent/codex-agent.exe snap diff <file>
```

---

## C# Code Style

### Naming

| Element | Convention | Example |
|---------|-----------|---------|
| Private instance fields | `m_` prefix | `m_diagnostics` |
| Private static fields | `s_` prefix | `s_instance` |
| Private readonly fields | `m_` prefix + `readonly` | `readonly List<Token> m_tokens` |
| Properties, types | PascalCase | `SourceSpan`, `TokenKind` |
| Locals, parameters | camelCase | `localEnv`, `tokenIndex` |
| Constants | PascalCase | `MaxLineLength` |

### Key Rules

- `sealed record` for immutable ref types. `readonly record struct` for small value types.
- Use `new()` when the target type is on the left side.
- **Omit default modifiers.** Don't write `private` on members or `internal` on top-level types.
- **No XML doc comments** (`///`). Code should be self-documenting.
- **No `var`.** Always use explicit types. Agents and humans both benefit from seeing the type on each line.
- **No unused fields/variables/parameters.** `TreatWarningsAsErrors` catches these.
- Prefer `Map<K,V>` (in `Codex.Core`) over `ImmutableDictionary`.  There is an equivalent ValueMap<K,V> for value types.
- `Nullable` is enabled project-wide. `LangVersion` is `12`.
- Pattern matching (`switch` expressions) over visitor pattern where possible.
- File-scoped namespace declarations. One primary type per file.
- Use xUnit for tests. Match the style of existing test files.

### Implicit Usings

These are implicit (don't add them): `System`, `System.Collections.Generic`, `System.Linq`,
`System.IO`, `System.Net.Http`, `System.Threading`, `System.Threading.Tasks`.

`System.Collections.Immutable` is **NOT** implicit — add only when needed.

### Formatting

- 4 spaces (no tabs). UTF-8. Max 120 chars/line. End files with single newline.

---

## Codex (.codex) Code Style

- Boolean literals: `True` / `False` (capital T/F).
- Function application is left-associative: `f x y` means `(f x) y`.
- Pattern matching uses `when`/`if`, not `match`/`case`.
- All functions are curried by default.

---

## File Editing Rules

**All files, any size.** `edit_file` has documented failure modes on files of every
size (see `docs/TOOL-ERROR-REGISTRY.md`). Required workflow:

1. `codex-agent snap save <file>`
2. Make the edit (try `edit_file` for small surgical changes to `.cs` files only).
3. `pwsh -File tools/codex-agent-verify.ps1 <file>` — auto-strips tool pollution.
4. `codex-agent peek <file> 1 10` to verify (NOT `get_file` — it hides headings).
5. `codex-agent snap diff <file>` to confirm delta.
6. If anything looks wrong: `codex-agent snap restore <file>` and switch to the safe path.

**Safe path** (use when `edit_file` fails, or for any markdown/`.codex`/config file):

1. `codex-agent snap save <file>`
2. `create_file` to `<filename>.new` with complete content.
3. `Copy-Item <filename>.new <filename> -Force`
4. `pwsh -File tools/codex-agent-verify.ps1 <filename>` — auto-strips tool pollution.
5. `codex-agent peek <file> 1 10` to verify.
6. `codex-agent snap diff <file>` to confirm only intended changes.
7. If build fails: `snap restore`, inspect, retry.
8. `Remove-Item <filename>.new`

**Alternative: Partial class.** For large `.cs` files needing new methods, create a second
file (e.g., `Foo.Bar.cs`) with `partial class`. Merge when stable.

### Never Do

- Never print a full file as a code block for the user to paste.
- Never use terminal redirects (`>`, `Set-Content`) when file tools are available.
- Never retry a failed edit approach — switch strategies.

---

## Build and Verify

Every task touching source code must end with a green build.

```powershell
dotnet tools/codex-agent/codex-agent.exe build
dotnet tools/codex-agent/codex-agent.exe test
```

`TreatWarningsAsErrors` is on — unused fields, variables, parameters, and redundant
usings are all build failures.

### Quick Checklist

- [ ] Build passes (zero warnings)
- [ ] Tests pass
- [ ] `m_` prefix on private instance fields, `s_` prefix on private static fields
- [ ] No `var` — always explicit types
- [ ] No XML doc comments
- [ ] No dead code

---

## Scope of Authority

### Freely Modifiable
`src/`, `tests/`, `tools/`, `samples/`, `editors/`, `generated-output/`, `Codex.Codex/`

### Modifiable With Care
`README.md`, `CONTRIBUTING.md`, `.github/copilot-instructions.md`, `.gitignore`, `Codex.sln`

### Do Not Modify Without Permission
`Directory.Build.props`, `docs/Vision/NewRepository.txt`, `docs/Vision/IntelligenceLayer.txt`,
`docs/00-OVERVIEW.md`, `docs/10-PRINCIPLES.md`

---

## Project Structure (quick ref)

Dependencies flow one way: `Core → Syntax → Ast → Semantics → Types → IR → Emit → Emit.* → Cli`

`Codex.Core` has zero project deps (root). `Codex.Cli` references everything (composition root).

Tests: `Codex.Core.Tests`, `Codex.Syntax.Tests`, `Codex.Ast.Tests`, `Codex.Semantics.Tests`,
`Codex.Types.Tests`, `Codex.Repository.Tests`, `Codex.Lsp.Tests`

Self-hosted source: 26 `.codex` files in `Codex.Codex/`. Bootstrap runner: `tools/Codex.Bootstrap/`.

---

## Git Workflow

Branch naming: `windows/<topic>`, `linux/<topic>`, `cam/<topic>`, `nut/<topic>`.
No agent merges its own work to `master` without review (by another agent or user).
Use Conventional Commits: `feat:`, `fix:`, `docs:`, `test:`, `chore:`, `refactor:`.

When reviewing other agents' branches:
1. `git fetch --all`
2. `git branch -r --merged origin/master` — delete these (cleanup)
3. `git branch -r --no-merged origin/master` — review these
4. `git diff --stat origin/master..origin/<branch>` — scope the change
5. Build + test on master after merge

---

## Terminal Discipline

- **ONE command per terminal call.** Multi-line input is silently mangled (TEF-007).
  Never send two commands in one call. If you need N commands, make N calls.
- **Never write files via terminal.** No `>`, `Set-Content`, `WriteAllText`. Encoding
  corruption is guaranteed on non-ASCII content (TEF-004). Use `create_file` only.
- Terminal is for **read-only queries and builds**. Use `edit_file`/`create_file` for mutations.
- If a command hangs, kill it and switch to a file-based approach.
- If you truly need a multi-command script, write a `.ps1` with `create_file`,
  run with `pwsh -File <script>`, then `Remove-Item <script>`.

---

## Handoffs

After meaningful sessions, create/update `docs/OldStatus/ITERATION-N-HANDOFF.md`.
Log architecture decisions in `docs/OldStatus/DECISIONS.md` (append-only).
Use `checkdate()` for all dates. See existing handoffs for format.
