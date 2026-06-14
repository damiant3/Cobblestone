# CODEX — Project Overview

## What This Is

Codex is a programming language, a repository protocol, and a unified development environment. It is being built in .NET 8 (C#) as the bootstrap implementation. The vision documents (`NewRepository.txt` and `IntelligenceLayer.txt`) define the philosophical and technical north star. This document and its siblings define the engineering plan to get there.

## The Three Pillars

| Pillar | What It Does | Why It Matters |
|--------|-------------|----------------|
| **The Language** | A literate, dependently-typed, effect-tracked programming language whose source reads like prose | Programs become literature; the compiler verifies what you meant |
| **The Repository** | A content-addressed, append-only fact store that replaces Git, GitHub, and package managers | Code is knowledge; knowledge accumulates; nothing is lost |
| **The Environment** | A unified reader/writer/verifier/explorer that presents code as formatted chapters | The tooling is not bolted on — it is the medium |

## Bootstrap Strategy

We build Codex *in C#* first. This is the bootstrap compiler, the way GCC was first compiled by another C compiler. Once Codex can compile itself, the C# implementation becomes historical artifact. Until then, it is the workhorse.

The .NET 8 solution (`NewRepository.sln` / `NewRepository.csproj`) is our home. We will restructure it into a multi-project solution as the architecture demands.

## Planning Documents

| Document | Contents |
|----------|----------|
| `01-ARCHITECTURE.md` | System architecture, project structure, dependency graph |
| `02-LANGUAGE-DESIGN.md` | Formal language specification plan — syntax, semantics, type system |
| `03-TYPE-SYSTEM.md` | Deep dive on the type system — dependent types, linear types, effects |
| `04-COMPILER-PIPELINE.md` | Lexer → Parser → AST → Type Checker → IR → Code Generation |
| `05-REPOSITORY-MODEL.md` | Content-addressed store, facts, proposals, verdicts, views, trust |
| `06-ENVIRONMENT.md` | The unified IDE — Reader, Writer, Verifier, Explorer, Narrator |
| `07-TRANSPILATION.md` | IR design and target backends (Rust, C#, JS, Python, WASM, LLVM) |
| `08-MILESTONES.md` | Phased delivery plan with concrete milestones |
| `09-RISKS.md` | Technical risks, mitigations, open questions |
| `10-PRINCIPLES.md` | Engineering principles that govern all implementation decisions |

## Relationship to Vision Documents

- **`NewRepository.txt`** — The "book." Defines the language syntax, type system, repository model, and social contract. This is the *what*.
- **`IntelligenceLayer.txt`** — The manifesto. Defines *why* now, *why* this matters, and the industry forces that make Codex necessary and possible.
- **`docs/`** — The engineering plan. Defines *how* we build it, in what order, with what tradeoffs.

The vision documents are not specifications. They are aspirations. The planning documents translate aspiration into executable engineering work.
