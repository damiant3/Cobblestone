# Contributing Guidelines for Codex

Conventions for working in the Codex tree. **For current project state, gates, and workflow, see [CLAUDE.md](CLAUDE.md).**

---

## Purpose

Codex is a self-hosting compiler, language, OS, and trust system. The compiler is a hard fixed point of itself on bare metal: `seed/Codex.Codex.elf` compiles `codex/*.codex` and the result is byte-identical to itself (BS3, proven 2026-04-24).

The .NET reference compiler and its CLI/bootstrap helpers are **permanently retired**, and the C# test projects are abandoned. They live under `old/` (`old/src/`, `old/tests/`, `old/Codex.sln`, `old/generated-output/`) as historical record only. New work goes through the selfhost.

---

## General Principles

1. **Ship working software at every milestone.** A program goes in, a result comes out. If a milestone doesn't end with a demo, the milestone is wrong.
2. **Correctness over performance.** The bootstrap compiler does not need to be fast; it needs to be right. Optimization comes after correctness is proven by tests.
3. **Immutability by default.** AST nodes, IR nodes, types, and facts are immutable. Builders are mutable during construction, then frozen.
4. **No premature abstraction.** Do not create an interface until you have two implementations. Do not create a base class until three subclasses exist.
5. **One thing at a time.** Each file does one thing. Each method does one thing. Each commit does one thing.

See [docs/10-PRINCIPLES.md](docs/10-PRINCIPLES.md) for the full set of governing principles.

---

## Branching and Commits

- Source of truth is the local Perforce server (`localhost:1666`, depot `//Codex/main`). GitHub receives ad-hoc pushes at meaningful milestones.
- Conventional Commits style: `feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`, `perf:`.
- Single logical change per CL.
- Shelve numbered CLs for review; the author submits after sign-off.

---

## Code Style

The selfhost is the canonical source. See [docs/DevelopersGuide.md](docs/DevelopersGuide.md) for syntax details.

- Booleans: `True` / `False` (capital T/F).
- Application is left-associative: `f x y = (f x) y`. All functions are curried.
- Pattern matching uses `when`/`if`, not `match`/`case`.
- Effects appear in brackets: `[Console] Nothing`.
- **Do not add comments.** Prose at section/function scope is the structural element; if a fact deserves explanation, it belongs in the surrounding narrative, not as a sigil-prefixed line.
- **Do not introduce Unicode handling inside the compiler.** CCE is the internal encoding; conversion happens only at I/O boundaries.

---

## Testing Requirements

Every change must include tests. Tests must be deterministic and headless.

For compiler changes (codegen, semantics, syntax), all three gates must stay green:

1. **Sample battery** — `codex.build/sweep.ps1`. Hand-verified `.expected` snapshots for every sample.
2. **BS2 (pingpong)** — `codex.build/pingpong-self.ps1`. Stage 1 === stage 2 Codex-text byte-identical.
3. **BS3 (bootstrap3)** — `codex.build/bootstrap3.ps1`. Stage 1 ELF === stage 2 ELF byte-identical (hard fixed point).

When adding behavior, ship a sample that exercises it: a `.codex` source under `codex.test/` with a hand-verified `.expected` (success path) or `.failing` CDX-code sidecar (compile failure path).

---

## Build and Test Commands

```powershell
codex.build/sweep.ps1                     # Sample battery (canonical correctness gate)
codex.build/pingpong-self.ps1             # BS2: Codex-text byte-identical
codex.build/bootstrap3.ps1                # BS3: ELF byte-identical (hard fixed point)
codex.build/sample-compile-selfhost.ps1   # One-shot: seed ELF + sample → compiled ELF
```

---

## How AI Agents Should Operate

- **Always read a file before editing it.** No guessing at structure.
- **Run the gates** (`sweep.ps1`, `pingpong-self.ps1`, `bootstrap3.ps1`) before concluding a codegen-touching task.
- **Produce minimal diffs** — do not reformat unrelated code, rename symbols, or restructure files unless that is the explicit task.
- **Do not modify `docs/Vision/`** — ever.
- **Do not modify `docs/00-OVERVIEW.md` or `docs/10-PRINCIPLES.md`** unless explicitly asked. They are the north-star specification.
- **Do not edit, invoke, or rebuild anything under `old/`** — the retired reference compiler, sln, tests, and generated artifacts live there as historical record only.

---

## Quick Checklist for Changes

- [ ] `codex.build/sweep.ps1` passes (or no codegen change).
- [ ] `codex.build/pingpong-self.ps1` passes (or no codegen change).
- [ ] `codex.build/bootstrap3.ps1` passes (or no codegen change).
- [ ] New behavior has a `codex.test/` entry with a hand-verified `.expected` or `.failing` sidecar.
- [ ] No comments in Codex source.
- [ ] Temp files cleaned up (`.bak`, `.new`, `.tmp`, `.snap`).
