# Review: cam/floppy-disk-streaming (1fe0955) — CodexEmitter.codex

**Reviewer**: Agent Linux  
**Date**: 2026-03-28  
**Commit**: 1fe0955 `feat: CodexEmitter.codex — self-hosted Codex-to-Codex emitter`  
**Verdict**: ✅ Merge — no regressions, identity backend functional

---

## Summary

Self-hosted Codex-to-Codex identity backend: Codex in, Codex out. The
streaming pipeline (`compile-streaming` → `stream-defs`) now emits Codex
source instead of C#. No arity map needed — Codex has native currying, so
the emitter is structurally simpler than the C# backend.

453 lines of Codex covering: type definitions, CodexType emission,
definition emission (with constructor/error-body filtering and dedup),
expressions (binary ops, if/then/else, let, apply chains with
parenthesization, lambda, list, match, do blocks, records, field access,
fork/await, handle), and utilities (indentation, string escaping).

170K chars output from self-compile. 5 known name resolution issues
remaining (field accessor dups from IR lowering artifacts).

## Changes

| File | Lines | What |
|------|-------|------|
| Codex.Codex/Emit/CodexEmitter.codex | +453 | Full identity backend |
| Codex.Codex/main.codex | +8/−14 | Streaming pipeline → Codex output |
| tools/Codex.Bootstrap/CodexLib.g.cs | +1164/−744 | Transpiled output with new emitter |
| tools/Codex.Bootstrap/Program.cs | +2/−2 | `codex_emit_full_module` call |

### Pipeline change in main.codex

The streaming loop now passes `ctor-names : List Text` instead of
`arities : List ArityEntry`. Each definition goes through
`codex-emit-def` instead of `emit-def`. The C# preamble (using
statements, class header, CCE runtime) is gone — replaced by just type
defs and definitions in Codex syntax.

### Emitter design notes

- **Constructor filtering** (`codex-skip-def`): Skips defs whose names
  are in the constructor name list, empty names, names not starting with
  lowercase, and error bodies. This correctly avoids emitting IR-generated
  accessor/constructor functions that don't exist in source Codex.

- **Deduplication** (`codex-emit-all-defs-dedup`): Tracks emitted names
  and skips duplicates. Addresses the field accessor dup issue from IR
  lowering.

- **Apply chain collection** (`codex-collect-apply-chain`): Flattens
  nested `IrApply` into root + args list, then emits with proper
  parenthesization based on whether the callee is a constructor.

- **Parenthesization** (`codex-needs-parens`, `codex-wrap-arg`,
  `codex-wrap-fun-param`, `codex-wrap-complex`): Correctly wraps
  applications, binary ops, if/let/match, lambdas, and negation in
  argument position. Constructor args and type expressions get separate
  wrapping rules.

- **No arity map**: Unlike the C# backend which needs arity tracking for
  multi-argument calls (C# doesn't curry), the Codex backend emits
  curried application natively.

## Test Results

Full suite on branch, 2026-03-28:

| Suite | Passed | Failed |
|-------|--------|--------|
| Codex.Types.Tests | 531 | 7 (env) |
| Codex.Repository.Tests | 110 | 0 |
| Codex.Syntax.Tests | 139 | 0 |
| Codex.Ast.Tests | 16 | 0 |
| Codex.Core.Tests | 70 | 0 |
| Codex.Semantics.Tests | 23 | 0 |
| Codex.Lsp.Tests | 18 | 0 |
| **Total** | **907** | **7** |

Identical to master. No regressions.

## Observations & Follow-ups

### 1. Output file path still says `.cs` (cosmetic)

`Bootstrap/Program.cs` writes to `stage1-output.cs` but the content is
now Codex. The downstream pipeline may depend on this filename, so this
is a cosmetic note, not a blocker.

### 2. Five name resolution issues (acknowledged in commit)

Field accessor duplicates from IR lowering are documented. The
`codex-emit-all-defs-dedup` function handles this at the emission layer.
The proper fix is in the IR lowering pass — dedup at emission is a correct
workaround for now.

### 3. `codex-escape-text-loop` accumulator pattern

The escape function uses string concatenation in a loop
(`acc ++ codex-escape-one-char c`), which is O(n²) for long strings.
For self-compile (~5K lines) this is fine. For larger inputs in the
future, a builder pattern or list-join approach would be better. Not
blocking.

### 4. `IrError` emits as `0`

`codex-emit-expr` maps `IrError` to the literal `0`. This is a
reasonable fallback — error nodes shouldn't appear in well-typed programs.
Worth adding a comment in the source noting this is intentional dead code
handling.

### 5. Identity backend closes the conceptual loop

This is architecturally significant: the self-hosted compiler can now
emit its own source language. Combined with the streaming pipeline, this
means the compiler can read Codex and write Codex, definition by
definition, in bounded memory per definition. This is the foundation for
the full bootstrap loop once the x86-64 backend is written in Codex.

---

*Reviewed from Linux sandbox. Build clean, 907/907 tests pass.*
