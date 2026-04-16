# CLAUDE.md — Codex Project Instructions

## What This Is

Codex is a new programming language, compilers (reference and selfhost), tools, operating system, repository protocol, trust lattice, encoding, and more.  We take the best of type theory, language design, aesthetics, security research, and actual practice.  We leave everything else behind.  If we didn't build it, we don't trust it.  Codex is a new computational substrate intended to be impervious to all currently known attack vectors by-design.

The project was started 3/14/2026.

## Current State (MM4 — The Second Bootstrap)

**The current goal is MM4**: a Codex compiler compiled entirely by Codex, producing bare-metal
x86-64 binaries, achieving fixed-point self-compilation. No C# anywhere in the chain.

Design doc: `docs/Active/Compiler/SECOND-BOOTSTRAP.md`
Current plan: `docs/CurrentPlan.md`
Backlog: `docs/BACKLOG.md`

## The Rules

### 1. Compiler changes go to a feature branch, not master

**Do not push compiler code directly to master.** Anything that touches the
reference compiler (`src/`), the self-hosted compiler (`Codex.Codex/`), the
runtime, or codegen must land on a feature branch (`hex/description`). Another
agent or Damian reviews, builds, tests, and merges.

Non-compiler changes (docs, tooling under `tools/` that isn't the compiler,
agent scripts, CI, etc.) may be pushed directly to master.

### 2. Pingpong (bootstrap 2) is the acceptance test

There are **three separate bootstraps**. Do not confuse them. See
`docs/CodexBootstrap.png` and `docs/Test/BOOTSTRAP-REPORT.md`.

- **Bootstrap 1** — .NET self-host, emits C#. Fixed point: stage 1 === stage 3.
- **Bootstrap 1.1** — .NET self-host, emits Codex text. Fixed point: stage 1 === stage 2.
- **Bootstrap 2 (pingpong)** — bare-metal ELF under QEMU, emits Codex text.
  Fixed point requires **both** sem-equiv(source, stage 1) = PASS **and**
  stage 1 === stage 2 byte-identical.
- **Bootstrap 3** — bare-metal ELF emits machine code. Not yet green (MM4 Phase 8).

Bootstraps 1 and 1.1 run under `dotnet`. Pingpong is bootstrap 2, and **only**
bootstrap 2. A green "BOOTSTRAP 1" or "BOOTSTRAP 1.1" line from `codex bootstrap`
says nothing about pingpong. If you report pingpong green, it is because
`wsl bash tools/pingpong.sh` Phase 4 ran green: sem-equiv PASS followed by
stage1 === stage2 byte-identical.

Every change that touches codegen must pass bootstrap 2 (pingpong) before it
is considered done. `wsl bash tools/pingpong.sh` — if it's not green, back
it out.

### 3. Read before you write

Do not modify code you have not read. Do not guess at file contents. Do not assume
structure from names. The self-hosted compiler has subtle invariants — a wrong
assumption will cost hours.

### 4. One thing at a time

This is in the principles doc and it is the most violated rule. Do one thing. Test it.
Commit it. Then do the next thing. Do not batch. Do not "while I'm here." The compiler
is 12,000 lines of Codex and 7,000 lines of C# codegen. A wrong change in one place
surfaces as a silent corruption three pipeline stages later.

### 5. CCE is the internal encoding

Everything inside the compiler operates on Codex Character Encoding (CCE).
Unicode conversion happens ONLY at I/O boundaries. Do not introduce Unicode
assumptions in internal code.

### 6. No dates, no estimates

Every estimate has been wrong by orders of magnitude. The critical path is ordered.
That is all we need to know.

### 7. Never use python.

If you need to write a script, you can use bash (.sh), powershell (.ps1), Codex, or C#.  We don't need another dependency.

### 8. Parity is narrow

The reference (`src/`) is a baseline, not a mirror. The self-host (`Codex.Codex/`) is a strict superset — free to do more, do better, diverge on shape. The parity requirement is narrow: anything that affects the **compilation output** (lexer, parser, desugarer, type checker, lowering, codegen semantics) must mirror precisely; everything else (diagnostics wording, CLI output, debug dumps, profiler output, span precision, error formatting) is free to diverge. `pingpong.sh` is the acceptance test for the narrow invariant. Full treatment in principle 11 of `docs/10-PRINCIPLES.md` and applied in `docs/Active/Compiler/SELF-HOST-PARITY-AUDIT.md`.

### Key Tools

| Tool | What |
|------|------|
| `tools/pingpong.sh` | Self-compilation acceptance test (WSL) |
| `tools/codex-agent/codex-agent.exe` | Agent toolkit (orient, build, test) |
| `tools/Codex.Cli/` | Main CLI driver (`codex build`, `codex check`, etc.) |
| `tools/Codex.Bootstrap/` | Bootstrap2 driver (stage0 vs stage1 comparison) |

### Build and Test

```bash
dotnet build Codex.sln              # Builds everything
dotnet test Codex.sln               # Runs all tests
wsl bash tools/pingpong.sh          # Bare-metal self-compilation test with text output
wsl bash tools/binary-pingpong.sh   # Bare-metal self-compilation test with ELF output
```

## Agent Identity

Working directory: `D:\Projects\NewRepository-XXX`  Use pwd to find the actual XXX value
You are **Hex-XXX** where XXX is the last 3 characters of your working directory name.
Feature Branch prefix: `Hex-XXX/FeatureName`
Agent file: `docs/Agents/Hex.txt`

## What Not To Do

- Do not add features beyond what is asked
- Do not refactor unrelated code
- Do not add comments, docstrings, or type annotations to code unless a strong argument can be made that it prevents rediscovery
- Do not create abstractions for one-time operations
- Do not introduce Unicode handling inside the compiler

