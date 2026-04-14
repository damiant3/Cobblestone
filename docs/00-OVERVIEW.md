# CODEX — Project Overview

## What This Is

Codex is a programming language, a repository protocol, and a unified development
environment. The compiler is **self-hosting**: it is written in Codex and compiles
itself. The C# bootstrap implementation that brought the project to this point is
locked and preserved as historical artifact.

The vision documents (`docs/Vision/NewRepository.txt` and `docs/Vision/IntelligenceLayer.txt`)
define the philosophical and technical north star. This document describes where the
project stands and where it is going.

## Major Milestone 1 — Self-Hosting (Achieved 2026-03-19)

The Codex compiler crossed the self-hosting threshold. The original design documents
(01–09, Glossary) that guided the bootstrap phase are archived in `docs/Done/MM1/`.

### What Was Proven

- **Fixed point**: the compiler compiles itself and produces identical output.
- **Quine disproof**: non-trivial programs compile correctly, not just the compiler itself.
- **Zero type debt**: no unresolved types, no `ErrorTy` bindings in self-hosted output.
- **12 backends**: C#, JavaScript, Python, Rust, C++, Go, Java, Ada, Fortran, COBOL, IL, Babbage.

### The Self-Hosted Compiler

| Metric | Value |
|--------|-------|
| Source files | 26 `.codex` files |
| Lines | ~4,900 |
| Pipeline | Lexer → Parser → Desugarer → NameResolver → TypeChecker → Lowering → Emitter |
| Prelude | 11 modules (Maybe, Result, Either, Pair, List, CCE, Hamt, StringBuilder, Set, Queue, TextSearch) |
| Tests | 843 passing |

### The C# Bootstrap

The reference C# compiler in `src/` bootstrapped the language and now serves as
Stage 0 for the self-hosted compiler. Parity between the two is narrow by design —
the reference is a baseline, not a mirror; the self-host is expected to be a
strict superset. See principle 11 in `docs/10-PRINCIPLES.md` and the running
parity inventory in `docs/Active/Compiler/SELF-HOST-PARITY-AUDIT.md`.

## The Three Pillars

| Pillar | What It Does | Status |
|--------|-------------|--------|
| **The Language** | A literate, dependently-typed, effect-tracked programming language whose source reads like prose | Self-hosting. Core features complete. |
| **The Repository** | A content-addressed, append-only fact store that replaces Git, GitHub, and package managers | Foundation built. Federation and trust ahead. |
| **The Environment** | A unified reader/writer/verifier/explorer that presents code as formatted chapters | LSP server operational. Narration layer ahead. |

## What's Ahead

The compiler is free of its C# cradle. The road forward is written in Codex:

- **V1 — Views**: first-class consistent selections of facts from the repository.
- **V2 — Narration layer**: prose-aware compilation where English text is load-bearing.
- **V3 — Repository federation**: multi-repo sync, cross-repo trust and identity.
- **V4 — Proof-carrying packages**: every published fact carries its proofs.
- **V5 — Intelligence layer**: AI agents as first-class participants.
- **V6 — Trust lattice**: vouching with degrees, trust-ranked search.
- **V7 — Type-level function reduction**: proof steps that unfold function definitions.

## Key Documents

| Document | Contents |
|----------|----------|
| `00-OVERVIEW.md` | This file — project overview and status |
| `10-PRINCIPLES.md` | Engineering principles that govern all decisions |
| `CurrentPlan.md` | Active plan and near-term direction |
| `Active/Compiler/SELF-HOST-PARITY-AUDIT.md` | Running inventory of where the self-host and reference stand on the output-affecting axis |
| `Milestones/MM1/` | Archive of bootstrap-era design documents (01–09, Glossary) |
| `Vision/NewRepository.txt` | The original vision document |
| `Vision/IntelligenceLayer.txt` | The intelligence layer manifesto |
| `Designs/` | Feature design documents (Language, Features, Backends, Memory, Tools) |
| `History/` | Iteration handoffs and decision log |
| `Codex.OS/` | Codex operating system vision |
