# CLAUDE.md — Cam (Claude Code Agent)

> This file is read by Claude Code at session start. Keep it lean.

## What This Repository Is

Codex is a self-hosting programming language compiler (written in Codex, compiles itself).
C# bootstrap (.NET 8) is locked. Solution: `Codex.sln`. Pipeline:

```
Source (.codex) → Lexer → Parser → Desugarer → NameResolver → TypeChecker → Lowering → Emitter
```

12+ backends. 900+ tests. Self-hosting achieved. Content-addressed fact store.
Design docs: `docs/00-OVERVIEW.md`, `docs/10-PRINCIPLES.md`.
Current plan: `docs/CurrentPlan.md`. Route map: `docs/THE-ASCENT.md`.
Do not modify Vision docs (`docs/Vision/`) without explicit permission.

---

## Session Rules

1. **Read before you write.** Always read a file before editing it.
2. **Build before you commit.** `dotnet build Codex.sln` + `dotnet test Codex.sln`.
3. **Clean up temp files.** Delete `.bak`, `.new`, `.tmp`, `.snap`, scratch scripts before ending.
4. **One logical change per commit.** Don't bundle unrelated fixes.
5. **Leave a handoff.** After meaningful work, update `docs/CurrentPlan.md`.
6. **Ask the user** when unsure. A 10-second question beats a 10-minute wrong turn.
7. **Two-failures rule.** If the same approach fails twice, stop and switch strategies.

---

## The Climbing Party

- **Human**: Routes, picks the line, relays context between agents.
- **Agent Windows** (GitHub Copilot in VS): Builds features, reviews code, pushes to master.
- **Agent Linux** (Claude on sandbox): Pulls, tests on real hardware/emulators, finds bugs by tracing.
- **Cam** (Claude Code CLI): Local agent. Fast iteration, direct file access, parallel work.
- **Nut** (VS 2026 + Copilot, garage box): Hardware lab, OS dev, phone flash. Branch: `nut/`.

Git is the shared state. Push to master is the handoff. `dotnet test` is the acceptance criterion.

### Environment (Cam's workstation)

- WSL available — use for Linux tools, GDB, QEMU, strace.
- GDB available via WSL — use for native backend debugging (RISC-V, ARM64, x86-64).
- .NET 8.0 + 9.0 installed. SDK 9.0.312.
- Agent toolkit runs natively: `tools/codex-agent/codex-agent.exe <cmd>`
- Run `codex-agent orient` for a condensed project map.

---

## C# Code Style

- Private fields: `m_` prefix (e.g., `m_diagnostics`)
- Properties/types: PascalCase. Locals/parameters: camelCase.
- `sealed record` for immutable ref types. `readonly record struct` for small value types.
- **No XML doc comments** (`///`). **No `var`** when type is non-obvious.
- **No unused fields/variables/parameters.** `TreatWarningsAsErrors` is on.
- Prefer `Map<K,V>` (in `Codex.Core`) over `ImmutableDictionary`.
- Implicit usings: `System`, `System.Collections.Generic`, `System.Linq`, `System.IO`, `System.Threading`.
- 4 spaces (no tabs). UTF-8. Max 120 chars/line.

---

## Codex (.codex) Code Style

- Boolean literals: `True` / `False` (capital T/F).
- Function application is left-associative: `f x y` means `(f x) y`.
- Pattern matching uses `when`/`if`, not `match`/`case`.
- All functions are curried by default.

---

## Project Structure

Dependencies flow one way: `Core → Syntax → Ast → Semantics → Types → IR → Emit → Emit.* → Cli`

`Codex.Core` has zero project deps (root). `Codex.Cli` references everything (composition root).
Self-hosted source: 26+ `.codex` files in `Codex.Codex/`. Bootstrap runner: `tools/Codex.Bootstrap/`.

---

## Build and Verify

```bash
dotnet build Codex.sln
dotnet test Codex.sln
```

Every task touching source code must end with a green build (zero warnings, all tests pass).

---

## Git Workflow

Branch naming: `windows/<topic>`, `linux/<topic>`, `staging/<topic>`.
Use Conventional Commits: `feat:`, `fix:`, `docs:`, `test:`, `chore:`, `refactor:`.
No agent merges its own work to `master` without review.

---

## Scope of Authority

**Freely modifiable**: `src/`, `tests/`, `tools/`, `samples/`, `editors/`, `generated-output/`, `Codex.Codex/`
**With care**: `README.md`, `CLAUDE.md`, `.github/copilot-instructions.md`, `.gitignore`, `Codex.sln`
**Do not modify without permission**: `Directory.Build.props`, `docs/Vision/*`

---

## Reference Compiler Lock

The C# bootstrap compiler is **locked**. Do not modify `src/` files without explicit permission
from the user. All forward development happens in `.codex` source (`Codex.Codex/`).
See `docs/REFERENCE-COMPILER-LOCK.md` for details.
