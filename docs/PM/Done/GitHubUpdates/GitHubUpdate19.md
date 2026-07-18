# GitHub Update 19 -- 2026-05-31

## Language

- **Tuples.** `(A, B)` sugar in type position desugars to foreword `Tup2`..`Tup5`. `let (x, y) = e` destructuring via single-arm match over `TuplePat`. All 15 transpiler plugs emit idiomatic tuple syntax for their target language (C# `(a, b)`, Rust `(a, b)`, Python `(a, b)`, JS `[a, b]`, etc.).
- **Scoped constraint dispatch.** `show`/`compare` inside `Show a =>` / `Ord a =>` functions now dispatches through the dictionary only when the argument is a parameter name. Let-bound locals (`let n = 42 in show n`) use normal direct dispatch. Test: `constraint-concrete`.
- **Parametric same-name types.** `Box (a) = | Box (a)` now compiles and runs (was miscompile then CDX3001 rejection). Emit type table merges a type-only map ahead of value env; `lookup-type-binding` prefers Sum/Record type over same-named ctor.

## Compiler

- **Lambda-def parse fix.** `LambdaExpr` added to `is-compound` in `parse-app-loop`. A bare lambda in function position (`\y -> x + y`) no longer greedily consumes the next definition's identifier as an argument. Fixes the "Bug 1" lambda-def miscompile that lost type annotations on definitions following lambda-bodied defs.
- **IRBranch.guard fix.** `rename-ir-branches` in ChapterScoper now preserves the `guard` field (was constructing IRBranch with only 3 of 4 fields).
- **SCOPE phase discipline.** Precise `pmap-walk` escape check wired into SCOPE; `phase-compact` reclaims scope bivy scratch (peak heap -102 MB on selfhost).
- **CHECK survey overhaul.** Reserve type-check deck by (records+ctors) count, not source bytes. Peak heap selfhost 897->405 MB (-55%).
- **Self-type-table shrink.** Nested named types emitted as `ConstructedTy` by-name refs instead of inline-expanding. Data section -58%, seed 2637->2499 KB.
- **Text-append alias fix.** `__str_concat` fast in-place path was corrupting its left operand when it ended at heap-top. Fix: always fresh-alloc.
- **C# plug full-compiler emit.** The emitted full Codex compiler (2376 defs) now compiles under `dotnet build` with 0 errors (was 1334). IRTextEmitter stream-defs-sexp with per-def heap-restore.

## Tooling

- **Interactive debugger.** codex-vm `-debug -break <fn> -map <file>` with command shell, guest `!EXC=03` serial interception, symbol resolution, conditional breakpoints, backtrace, register dump.
- **Durable disk writes.** codex-vm IDE WRITE SECTORS (0x30) + REP OUTSW data phase + flush to host image file. Accounts and SystemDb persist across restarts.
- **Tuple dogfooding.** 11 Parser result records, 2 Unifier records, 2 TypeChecker records, 6 foreword records, 8 app records replaced with tuples. Demonstrates the feature end-to-end in the compiler itself.

## Apps

- **CreationsApp SPA** (CodexMagic): replaces hand-JS with a single-page app. AuthClient reusable auth, WorldForge integration, NameForge, StoryGraph, CardEmitter, WorldModel.
- **Explorer DB-backed designers.** Multi-table ExplorerStore + generic ExplorerServer; Setting/Character/Item pages fetch from DB and build pip-trees.
- **Accounts persistence.** Durable save/load of account table to disk sector; survives restart.

## README

- Milestone table trimmed to highlights; full detail moved to `docs/PM/Milestones.md`.
- Seed digest updated (`3C62496D...`, 2,653,313 bytes).
- Stats: 237 foreword modules, 54 compiler files, 208 test samples, 157 pass.

## Stats

- Seed: 2,653,313 bytes (SHA256 3C62496D...)
- CDX hard fixed point on bare metal (SUT === stage1, one pass); 208 total / 157 pass / 0 fail / 51 skip
- 237 foreword modules, 54 compiler files, 107 plug files, 319 app modules
