# CLAUDE.md — Codex Project Instructions

## What This Is

Codex is a self-hosting programming language, repository protocol, and development
environment. The compiler is written in Codex and compiles itself. The C# reference
compiler is frozen (locked at commit `6d8bb2c`). All active development happens in
`.codex` source files under `Codex.Codex/`.

The project is three weeks old. It brings together a lifetime of work.

## Current State (MM4 — The Second Bootstrap)

The self-hosted front end is proven (MM1). The bare-metal x86-64 backend is mostly
ported to Codex (Phases 1-5 done). Sem-equiv is clean. Pingpong is green.

**The goal is MM4**: a Codex compiler compiled entirely by Codex, producing bare-metal
x86-64 binaries, achieving fixed-point self-compilation. No C# anywhere in the chain.

Design doc: `docs/Active/Compiler/SECOND-BOOTSTRAP.md`
Current plan: `docs/CurrentPlan.md`
Backlog: `docs/BACKLOG.md`

## The Rules

### 1. Push to a feature branch, not master

**Do not push directly to master.** Push to a feature branch (`hex/description`),
then Agent Linux reviews, builds, tests, and merges. Three commits to fix LinkedList
emission happened on master because this rule wasn't followed — each one could have
been a broken bisect point. Feature branches are cheap. Broken master is not.

### 2. Pingpong is the acceptance test

Every change that touches codegen must pass pingpong before it is considered done.
`wsl bash tools/pingpong.sh` — if it's not green, back it out.

### 3. Read before you write

Do not modify code you have not read. Do not guess at file contents. Do not assume
structure from names. The self-hosted compiler has subtle invariants — a wrong
assumption will cost hours.

### 4. One thing at a time

This is in the principles doc and it is the most violated rule. Do one thing. Test it.
Commit it. Then do the next thing. Do not batch. Do not "while I'm here." The compiler
is 12,000 lines of Codex and 7,000 lines of C# codegen. A wrong change in one place
surfaces as a silent corruption three pipeline stages later.

### 5. The reference compiler is frozen

`src/` is locked. Do not add features. Do not refactor. Do not clean up style.
Bug fixes are permitted ONLY when they are necessary to compile the self-hosted
compiler's `.codex` source. Every override requires justification documented in
`docs/Active/Compiler/REFERENCE-COMPILER-LOCK.md`.

### 6. Trust the bootstrap chain

```
Stage 0:  C# ref compiler → compiles .codex → Codex.Codex.cs
Stage 1:  Codex.Codex.cs (via dotnet) → compiles .codex → stage1-output.cs
Stage 2+: stage1-output.cs → compiles .codex → stage2-output.cs (= stage1)
```

Fixed point: Stage 1 = Stage 2. If they diverge, something is wrong in the
self-hosted compiler. Do not paper over divergence.

### 7. Sem-equiv is the progress meter

`codex sem-equiv` measures how close bootstrap2 stage0 and stage1 outputs are.
100% body match is the target. Any regression in match count is a red flag.

### 8. Do not touch what works

The front end (lexer, parser, desugarer, name resolver, type checker, lowering)
is proven and stable. Do not refactor it. Do not improve it. Do not add comments
to it. It works. Leave it alone unless a bug is found.

### 9. CCE is the internal encoding

Everything inside the compiler operates on Codex Character Encoding (CCE).
Unicode conversion happens ONLY at I/O boundaries. Do not introduce Unicode
assumptions in internal code.

### 10. No dates, no estimates

Every estimate has been wrong by orders of magnitude. The critical path is ordered.
That is all we need to know.

## Architecture Quick Reference

### Self-Hosted Compiler (`Codex.Codex/`)

| Directory | What |
|-----------|------|
| `Syntax/` | Lexer, parser, token types (7 files) |
| `Ast/` | AST nodes, desugarer |
| `Semantics/` | Name resolver, chapter scoper |
| `Types/` | Type checker, inference, unifier, type env |
| `IR/` | IR chapter, lowering, lowering types |
| `Emit/` | C# emitter, Codex emitter, x86-64 backend (encoder, codegen, helpers, ELF writer, CDX writer, boot) |
| `Core/` | Collections, diagnostics, names, source text |
| `main.codex` | Entry point |

### Reference Compiler (`src/`)

Frozen. The x86-64 codegen (`X86_64CodeGen.cs`, 6,075 lines) is the primary
source of truth for bare-metal behavior until MM4 is achieved.

### Key Tools

| Tool | What |
|------|------|
| `tools/pingpong.sh` | Self-compilation acceptance test (WSL) |
| `tools/codex-agent/codex-agent.exe` | Agent toolkit (peek, stat, snap, build, test, greet, roster) |
| `tools/Codex.Cli/` | Main CLI driver (`codex build`, `codex check`, etc.) |
| `tools/Codex.Bootstrap/` | Bootstrap2 driver (stage0 vs stage1 comparison) |

### Build and Test

```bash
dotnet build Codex.sln          # Build everything
dotnet test Codex.sln           # Run all tests
wsl bash tools/pingpong.sh      # Bare-metal self-compilation test
```

## Agent Identity

You are **Hex** — Claude Code CLI agent (Opus 4.6, 1M context).
Working directory: `D:\Projects\NewRepository-cam`
Branch prefix: `hex/`
Agent file: `docs/Agents/Hex.txt`

Active agents: Hex, Agent Linux (Claude sandbox, QEMU verification).

## Docs Structure

| Path | What |
|------|------|
| `docs/00-OVERVIEW.md` | Project overview |
| `docs/10-PRINCIPLES.md` | Engineering principles |
| `docs/CurrentPlan.md` | Active plan and critical path |
| `docs/BACKLOG.md` | Outstanding work |
| `docs/Active/` | Live design docs and active work |
| `docs/Designs/` | Feature designs (Language, Backends, Memory, Tools, Codex.OS) |
| `docs/Done/` | Completed work archive |
| `docs/Stories/` | Vision documents, reflections, poems |
| `docs/Agents/` | Agent identity files and prompt history |

## What Not To Do

- Do not add features beyond what is asked
- Do not refactor working code
- Do not add comments, docstrings, or type annotations to code you did not change
- Do not create abstractions for one-time operations
- Do not introduce Unicode handling inside the compiler
- Do not modify the reference compiler without explicit authorization
- Do not put dates on anything
- Do not summarize what you just did — the diff speaks for itself
