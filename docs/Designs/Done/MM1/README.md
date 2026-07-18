# MM1 — Major Milestone 1 Archive

**Achieved**: 2026-03-19

This directory contains the original design documents and planning materials from
the bootstrap phase of the Codex compiler. These documents guided the project from
its inception through to self-hosting.

## What MM1 Represents

The Codex compiler, originally written in C# (.NET 8), successfully compiled itself.
The self-hosted `.codex` compiler achieved a proven fixed point, a proven quine
disproof, and zero type debt. The reference C# compiler was locked. All forward
development now happens in the Codex language itself.

## Contents

| Document | Description |
|----------|-------------|
| `00-OVERVIEW.md` | Original project overview (bootstrap-era) |
| `01-ARCHITECTURE.md` | System architecture, project structure, dependency graph |
| `02-LANGUAGE-DESIGN.md` | Formal language specification plan |
| `03-TYPE-SYSTEM.md` | Dependent types, linear types, effects |
| `04-COMPILER-PIPELINE.md` | Lexer → Parser → AST → TypeChecker → IR → Emitter |
| `05-REPOSITORY-MODEL.md` | Content-addressed store, facts, proposals, verdicts |
| `06-ENVIRONMENT.md` | The unified IDE — Reader, Writer, Verifier, Explorer |
| `07-TRANSPILATION.md` | IR design and target backends |
| `08-MILESTONES.md` | Original phased delivery plan |
| `09-RISKS.md` | Technical risks and mitigations |
| `10-PRINCIPLES.md` | Engineering principles (also preserved at `docs/10-PRINCIPLES.md`) |
| `GLOSSARY.md` | Term definitions |
| `CurrentPlan.md` | Final status snapshot at MM1 completion |

## Stats at MM1

| Metric | Value |
|--------|-------|
| Self-hosted compiler | 26 files, ~4,900 lines |
| Prelude library | 11 modules, ~1,200 lines |
| Backends | 12 (C#, JS, Python, Rust, C++, Go, Java, Ada, Fortran, COBOL, IL, Babbage) |
| Tests | 843 passing |
| Type debt | 0 |
| Fixed point | Proven |
| Reference compiler | Locked |
