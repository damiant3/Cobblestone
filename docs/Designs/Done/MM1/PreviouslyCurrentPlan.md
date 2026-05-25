# Current Plan

**Date**: 2026-03-19 (verified via system clock)

---

## Where We Are

The Codex compiler is self-hosting with a proven fixed point, a proven quine disproof,
and zero type debt. The reference C# compiler is **locked** (`REFERENCE-COMPILER-LOCK.md`).
All language development now happens in `.codex` source.

The L1–L4 feature port to the self-hosted pipeline is **complete**: effects, modules,
prelude, and do-notation are all wired from parser through C# emission in `.codex` source.

**P1 (built-in expansion) is RESOLVED.** The root cause was a bootstrap prose-extraction
bug: `indent >= 4` in `ExtractCodeBlocks` silently dropped all type definitions and
function signatures after the project moved to 2-space indentation. The fix
(`indent >= 2`) restored 91 type definitions, eliminated 1,014 unification errors,
and recovered ~30 missing functions in Stage 1 output including `is_builtin_name`,
`emit_builtin`, and the entire effects/handler subsystem. Fixed point re-verified.

### Snapshot

| Metric | Value |
|--------|-------|
| .codex source (compiler) | 26 files, ~4,800 lines, 181K chars |
| .codex source (total) | 97 files (compiler + prelude + samples + tests) |
| Tests | **836** (836 passing + 2 skipped) |
| Backends | 12 (C#, JS, Python, Rust, C++, Go, Java, Ada, Fortran, COBOL, IL, Babbage) |
| Bootstrap | Stage 1: 247,997 chars, 0 unification errors, 0 ErrorTy |
| Type debt | **0** (filtered), 0 ErrorTy bindings |
| Prelude | 11 modules: Maybe, Result, Either, Pair, CCE, Hamt, List, StringBuilder, Set, Queue, TextSearch |
| Quine disproof | `samples/expr-calculator.codex` — 10/10 PASS |
| Ref compiler | 🔒 Locked at `6d8bb2c` on 2026-03-19 |

### Self-Hosted Pipeline Coverage

The `.codex` compiler now handles:

| Stage | Features ported |
|-------|----------------|
| Lexer | `effect`, `with`, `import` keywords |
| Parser | Effect declarations, handler expressions, multi-param handler clauses, **import declarations** (`import ModuleName`) |
| Desugarer | `HandleExpr`, `HandleClause`, `EffectDef`, `EffectOpDef`, effect-defs in Document/AModule, **import threading** (CST→AST) |
| NameResolver | Single-module resolution, **cross-module name resolution** (`resolve-module-with-imports`) |
| Types | `EffectfulTy` in CodexType |
| IR | `IrHandle`, `IRHandleClause` |
| Lowering | `lower-handle`, `lower-handle-clauses` |
| Emitter | `emit-handle`, `emit-handle-clauses`, `EffectfulTy` case in `cs-type`, built-in inlining, typed do-blocks |
| Main | `compile-with-imports` accepts pre-resolved import results |

---

## What's Next

### Completed (This Session)

| # | Task | Status | What |
|---|------|--------|------|
| P1 | **Self-hosted built-in expansion** | ✅ Done | Root cause: bootstrap `ExtractCodeBlocks` required `indent >= 4` but source uses 2-space indent. Fix: `indent >= 2`. Stage 1 now inlines all built-ins. Fixed point verified. |
| L7 | **Better error messages** | ✅ Done | "Did you mean X?" via `StringDistance.FindClosest` ✅. Readable type formatting ✅. Related spans ✅. Error cap at 20 ✅. ErrorType cascade suppression ✅. Parser error recovery with 18 tests (CDX1021–CDX1032) ✅. |
| R5 | **Build system** | ✅ Done | `codex build <dir>` compiles multi-file projects via `codex.project.json`. Dependency resolution ✅. Multi-target (`--targets cs,js,rust`) ✅. Incremental builds (`--incremental`) ✅. Verified: `codex build Codex.Codex` compiles 26 files successfully. |
| M1 | **Self-hosted import/module system** | ✅ Done | CST `ImportDecl`, parser `parse-imports`, AST `AImportDecl`, desugarer threading, `resolve-module-with-imports` for cross-file name resolution, `compile-with-imports` pipeline entry point. Bootstrap verified: 458 defs, 0 errors. |
| R2 | **Standard library** | ✅ Done | 11 prelude modules (1,208 lines): Maybe, Result, Either, Pair, List, CCE (character encoding), Hamt (persistent map), StringBuilder, Set, Queue, TextSearch. Auto-discovered by `PackageResolver`. |
| R4 | **Package management** | ✅ Done | `PackageResolver` with local cache, version constraints (`*`, exact, prefix, `>=`). CLI: `codex add/remove/pack/packages`. Lock files. Auto-prelude for non-prelude projects. |
| E1 | **Exit criterion: real programs** | ✅ Done | `samples/word-freq/` — 3-file word frequency counter (139 lines). Multi-file build, cross-file defs, records, variants, pattern matching, effects, do-notation. Compiles and runs correctly via `codex build`. |

### Near Term: Make the Language Practical

| # | Task | Status | What |
|---|------|--------|------|
| E1 | **Exit criterion: real programs** | ✅ Done | `samples/word-freq/` — 3-file word frequency counter (139 lines). Multi-file build, cross-file defs, records, variants, pattern matching, effects, do-notation. Compiles and runs correctly via `codex build`. |
| Perf-P1 | **Lexer zero-alloc optimization** | ✅ Done | Added `char-code-at` builtin (reference compiler lock override, user-authorized). Rewrote self-hosted lexer: `peek-char` (Text, alloc) → `peek-code` (Integer, zero-alloc). All character comparisons now integer ==. Projected: lexer 800× → ~5×, overall 28× → ~4×. |

### Medium Term: Library & Runtime

| # | Task | Status | What |
|---|------|--------|------|
| R2 | **Standard library** | ✅ Done | 11 modules (1,208 lines): Maybe, Result, Either, Pair, List, CCE, Hamt, StringBuilder, Set, Queue, TextSearch. Covers option types, error handling, persistent maps, string building, text search, functional queues and sets. |
| R3 | **FFI / host interop** | ⏭️ Scratched | Deferred — exercise for the user. Per-backend with common interface. |
| R4 | **Package management** | ✅ Done | `PackageResolver` with local cache (`~/.codex/packages/`), version matching (`*`, exact, prefix, `>=`). CLI: `codex add/remove/pack/packages`. Lock files. Auto-prelude discovery for all non-prelude projects. |
| R6 | **Native executable bootstrap** | ⬜ Unblocked | Compile Codex compiler to native `.exe` via IL emitter. P1 resolved; depends on R2. |

**Exit criterion**: `codex build myproject/ --target il` produces a runnable `.exe`
with standard library support, no C# toolchain needed.

### Long Term: The Vision

| # | Task | What |
|---|------|------|
| V1 | **Views** | First-class consistent selections of facts from the repository. |
| V2 | **The Narration layer** | Prose-aware compilation where English text is load-bearing. |
| V3 | **Repository federation** | Multi-repository sync, cross-repo trust and identity. |
| V4 | **Proof-carrying packages** | Every published fact carries its proofs. Verified on import. |
| V5 | **Intelligence layer** | AI agents as first-class participants. Dashboard is step one. |
| V6 | **Trust lattice** | Vouching with degrees, trust-ranked search across contributors. |
| V7 | **Type-level function reduction** | Proof steps that unfold function definitions. |

---

## Recommended Sequence

```
Now ──→ E1 (real program)
            │
            └──→ R6 (native bootstrap via IL)
                    │
                    └──→ V1-V7 (the vision)

Scratched: R3 (FFI) — exercise for the user
```

**R6 is now unblocked.** P1, L7, R2, R4, R5, and M1 are resolved. The remaining
critical path is R6 (native executable bootstrap) which depends on the IL emitter
producing a standalone `.exe` using the standard library.

---

## Recent Session: P1/L7/R5 Resolution (2026-03-19)

**Agent**: Claude (Opus 4.6, Linux, claude.ai)
**Branch**: `linux/p1-builtin-expansion` → merged to `master`

### Investigation Trail

1. Diagnosed that Stage 1 output was missing ~30 functions (`is_builtin_name`, `emit_builtin`, entire effects subsystem, type parameter handling).
2. Found 1,014 unification errors in the self-hosted type checker, 976 of which were "Unknown name" for constructors and local variables.
3. Discovered the self-hosted parser found **0 type definitions** out of 91 expected.
4. Traced to the bootstrap's `ExtractCodeBlocks` function: `indent >= 4` threshold rejected all 2-space-indented code blocks, silently dropping every type-definition-only file (SyntaxNodes, TokenKind, CodexType, AstNodes, Token, Name, etc.).
5. One-line fix: `indent >= 4` → `indent >= 2` in `tools/Codex.Bootstrap/Program.cs`.

### Cherry-picked from `l1l4_partial`

Reviewed the other agent's branch. Accepted orthogonal fixes, rejected regressions:

- ✅ Unifier: ErrorType cascading suppression
- ✅ DiagnosticBag: 20-error cap
- ✅ NothingTy → "object" in cs-type
- ❌ Removal of `is-builtin-name`/`emit-builtin` (needed for P1)
- ❌ Removal of effect parsing (regression)
- ❌ Removal of `IrFieldAccess` (regression)

---

## Process Notes

- **Reference compiler is LOCKED.** New features go in `.codex` source only. See `REFERENCE-COMPILER-LOCK.md`.
- **Agent toolkit**: `tools/agent/` — PowerShell + Bash: `peek`, `fstat`, `sdiff`, `trun`, `gstat`.
- **Cognitive dashboard**: `tools/codex-dashboard.ps1` (Windows) or `tools/codexdashboard.sh` (Linux).
- **Session init**: `bash tools/linux-session-init.sh` for fresh Linux sessions.
- **Dates**: Always `checkdate()` before writing dates. Never trust training data.
- **Commits**: Working branches (`windows/<topic>`, `linux/<topic>`), review before merge.
- **Archived history**: `docs/OldStatus/` contains M0–M13 iteration handoffs.
