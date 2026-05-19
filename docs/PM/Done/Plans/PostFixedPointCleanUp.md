# Post-Fixed-Point Cleanup

**Date:** March 17, 2025
**Context:** After achieving bootstrap fixed-point (Opus.md), several sessions
improved the self-hosted lowerer. This document captures the current state and
remaining cleanup tasks.

---

## Current State

### Test Suite

| Project | Tests | Status |
|---------|------:|--------|
| Codex.Core.Tests | 16 | ✅ |
| Codex.Syntax.Tests | 88 | ✅ |
| Codex.Ast.Tests | 11 | ✅ |
| Codex.Semantics.Tests | 15 | ✅ |
| Codex.Types.Tests | 529 | ✅ |
| Codex.Lsp.Tests | 18 | ✅ |
| Codex.Repository.Tests | 23 | ✅ |
| **Total** | **700** | **All pass** |

Build: zero errors, 2 warnings (both `CS8603` in `Codex.Codex.cs` — null
return in `main()`, expected from effect syntax gap).

### Bootstrap Pipeline

```
.codex source (21 files, ~143K chars)
  → Stage 0 (Codex.Cli, C# reference compiler)
  → out/Codex.Codex.cs (Stage 1 output, ~258K chars, 4,925 lines)
  → Codex.Bootstrap (uses Stage 1 as compiler)
  → stage1-output.cs (Stage 2 output, ~158K chars, 1,203 lines)
```

| Metric | Stage 0 output | Stage 2 output |
|--------|---------------|----------------|
| `object` references | 5 (all legitimate) | 3 (all legitimate) |
| `_p0_` proxy params | 23 | 17 |
| Unification errors | 0 | 0 |
| ErrorTy bindings | 0 | 0 |
| Has-object bindings | 0 | 1 (`main` only) |

The 5 Stage 0 `object` references are: 1 string literal in `is_cs_keyword`,
2 in `cs_type` (`NothingTy`/`ErrorTy` → `"object"`), 2 in `main()` return.

The 3 Stage 2 `object` references are the same pattern minus the 2 `main()`
return variants (Stage 2 emits single-line bodies).

### Lowerer Fixes Applied (This Session)

1. **`binary-result-type` helper** — Binary ops now use left operand type
   (or `BooleanTy` for comparisons) instead of blindly propagating expected
   type. Fixes `(object remaining = ar - list_length(args))` patterns.

2. **If-expression type inference** — When expected type is `ErrorTy`,
   infers result from then-branch. Matches `Lowering.cs` lines 95–97.

3. **Match expression type inference** — `infer-match-type` scans branches
   for first non-`ErrorTy` body. Matches `Lowering.cs` line 351.

4. **ConstructedTy field access** — Resolves through constructor type to
   find underlying `RecordTy` for field type lookup.

5. **Per-field record lowering** — `lower-record-fields-typed` looks up
   each field's expected type from the `RecordTy` instead of passing the
   whole record type to every field.

### What Opus.md Listed vs Current State

| Opus Item | Was | Now | Status |
|-----------|-----|-----|--------|
| 90 `object` lines | 90 | **3** (Stage 2), **5** (Stage 0) | ✅ Fixed — all remaining are legitimate |
| 17 `_p0_` proxy lines | 17 | **17** (Stage 2), **23** (Stage 0) | ⚠️ Cosmetic — partial application wrappers, C# infers types |
| Byte-for-byte convergence | No | No | ⚠️ Stage 0 (4,925 lines) vs Stage 2 (1,205 lines) — different formatting |
| Effect annotations | Missing | **Fixed** | ✅ Parser skips `[Console]` effect syntax, 0 unification errors |

---

## Remaining Tasks

### Tier 1: Correctness

**1. ~~Effect annotation parsing~~** ✅ — Fixed. `parse-effect-type` in
`Parser.codex` skips `[...]` effect brackets and parses the return type.
Self-hosted compiler now has 0 unification errors (was 1).

**2. ~~Verify Stage 2 compiles as C#~~** ✅ — **Stage 2 compiles with 0 errors.**
Fixed 10 categories of emitter bugs in `CSharpEmitter.codex`:
1. `emit-let`: `(T x = val) is var _` → `((Func<T,R>)((x) => body))(val)`
2. `emit-do`: bare `{ }` blocks → `((Func<object>)(() => { ... return null; }))()`
3. `is-cs-member-name`: `Equals` → `Equals_` (avoids record/method name collision)
4. `IrName` for constructors: `IntLit` → `new IntLit()`
5. `IrName` for zero-arg functions: `f` → `f()` when arity = 0
6. `IrName` for method groups: wrap multi-arg functions in partial application lambdas
7. `emit-binary`: list `++` → `Enumerable.Concat(...).ToList()`
8. `emit-match`: conditional `_ => throw` wildcard for non-exhaustive switches
9. `emit-match-arms`: stop after first catch-all (prevents duplicate `_` patterns)
10. `emit-full-module`: top-level `Codex_Name.main();` before type declarations
11. `escape-text`: re-escape newline/CR characters back to `\n`/`\r` for C# strings
`CodexRuntime.cs` prelude provides 12 builtin functions (`list-length`, `list-at`, etc.)
Additional fixes:
- **Parser**: column-aware `when` parsing prevents nested `when` from stealing outer arms
- **Lexer**: `process-escapes` handles `\n \t \r \\ \"` at lex time (matches Stage 0)
- **Unifier**: restored original nested `when` structure (parser fix made flattening unnecessary)
Progress: 3,793 errors → 0 errors. Stage 2 self-hosting proven.

**2b. Standalone `codex.exe`** ✅ — `codex-standalone/` project builds and runs.
`codex version` and `codex build <file.codex>` work. Compiles `.codex` source to C#
output with proper newlines and escaping. Known limitation: simple programs like
`hello.codex` produce incorrect output (type checker doesn't resolve all bindings
for standalone programs without type annotations on every def). The self-hosted
compiler works correctly when compiling *itself* (full type annotations).

### Tier 2: Convergence

**3. ~~Fixed point / Stage 3~~** ✅ — **FIXED POINT ACHIEVED.**
Stage 2 (227,097 chars) compiles itself, producing Stage 3 (227,097 chars).
Stage 3 == Stage 2, byte-for-byte identical. The compiler produces itself.
Key enabler: TCO in the self-hosted emitter — tail-recursive functions are
emitted as `while (true) { ... continue; }` loops instead of expression-bodied
recursion. Without TCO, the ~153K char source caused stack overflow.
`.stage3-verify/` project independently verifies the fixed point.

**4. ~~Emission format alignment~~** ✅ — Moot. Stage 2 ↔ Stage 3 are identical.
Stage 0 ↔ Stage 2 differ in style (statement bodies vs expression bodies + TCO loops)
but produce semantically equivalent compilers. Per Damian's directive: semantic
equivalence is sufficient.

**5. Type declaration ordering** — The reference compiler emits record types
before sum types; the self-hosted compiler may emit them in definition order.
The `generated-output/*/mini-bootstrap.*` files show this ordering difference
across all 10 non-IL backends. Harmless but prevents exact diff.

````````

## Remaining Tasks (continued)

### Tier 3: Polish

**6. `_p0_` proxy parameter names** — 17–23 lines use `_p0_`, `_p1_` as
lambda parameter names from partial application wrapping. These work because
C# infers the types from context, but they're ugly. Fix: in the emitter,
when generating partial application wrappers, look up the target function's
parameter types and emit typed lambdas like `(Expr _p0_) => ...`.

**7. Generated output corpus refresh** — The `generated-output/` directory
has 10 changed `mini-bootstrap.*` files (type declaration ordering). These
should be regenerated and committed to match the current compiler output:
```
codex build samples/mini-bootstrap.codex --targets cs,js,rust,py,cpp,go,java,ada,fortran,cobol
```

### Tier 4: Ecosystem

**8. VS 2022 extension (VSIX)** — `tools/Codex.VsExtension/` has:
- ✅ TextMate grammar (`codex.tmLanguage.json`) with all keywords
- ✅ Language configuration (brackets, auto-close, indentation)
- ✅ `.pkgdef` registration
- ✅ `build-vsix.ps1` and `install-vs.ps1` scripts
- ✅ `codex.project.json` schema
- ⚠️ Not verified against current VS 2022 version
- ⚠️ `Codex.VsExtension.csproj.Backup*.tmp` files should be cleaned

**9. VS Code extension** — `editors/vscode/` has:
- ✅ TextMate grammar (synced with VS 2022 version)
- ✅ Language configuration
- ✅ LSP client (`src/extension.ts`)
- ✅ `node_modules` present (dependencies installed)
- ⚠️ Not verified against current VS Code version
- ⚠️ `VSCODE-SETUP.md` in `docs/` — verify instructions still work

**10. LSP server** — `src/Codex.Lsp/` provides:
- ✅ Diagnostics (errors + warnings in editor)
- ✅ Hover (type information)
- ✅ Completion
- ✅ Go-to-definition
- ✅ Document symbols
- ✅ Semantic tokens
- 18 tests all passing
- ⚠️ Not tested with the new lowerer changes (shouldn't affect LSP since
  LSP uses the reference compiler pipeline, not the self-hosted one)

**11. IL emitter** — `src/Codex.Emit.IL/` handles integers, text, booleans,
numbers, static methods, if/else, let bindings, binary ops, function calls,
records, sum types, field access, pattern matching. Produces runnable `.exe`.
- ⚠️ Missing: generics, TCO, full bootstrap
- ⚠️ No integration tests in the test suite (tests were in a separate run)

**12. Babbage emitter** — `src/Codex.Emit.Babbage/` is the Analytical Engine
backend. Intentionally limited. No action needed.

### Tier 5: Documentation

**13. Update `08-MILESTONES.md`** — M13 still shows `[ ] Stage 1 output =
Stage 2 output (full bootstrap fixed-point verification)` as unchecked.
The functional fixed-point is achieved (Stage 2 compiles, types resolve
correctly). Whether to check this off depends on whether "=" means
byte-for-byte or semantic equivalence.

**14. Update `FORWARD-PLAN.md`** — The "Bootstrap Status" section (lines
109–142) has stale numbers (328 `object`, 1,863 unification errors). Update
to reflect current state: 3 `object`, 1 error.

**15. Opus.md addendum** — Consider appending a section on the post-fixed-
point work: lowerer type inference (binary, if, match), `object` count
reduction from 90 → 3.

---

## Backends Audit (from Emitter Status commit `fbe227c`)

All 12 backends were audited in a prior session. Summary:

| Backend | Records | Sum Types | Match | Recursion | Effects | Status |
|---------|---------|-----------|-------|-----------|---------|--------|
| C# | ✅ | ✅ | ✅ | ✅ | ✅ | Clean |
| JavaScript | ✅ | ✅ | ✅ | ✅ | ✅ | Clean |
| Python | ✅ | ✅ | ✅ | ✅ | ✅ | Clean |
| Rust | ✅ | ✅ | ✅ | ✅ | ✅ | Clean |
| C++ | ✅ | ✅ | ✅ | ✅ | ✅ | Clean |
| Go | ✅ | ✅ | ✅ | ✅ | ✅ | Clean |
| Java | ✅ | ✅ | ✅ | ✅ | ✅ | Fixed: class name collision, `.apply()` |
| Ada | ✅ | ✅ | ✅ | ✅ | ✅ | Fixed: procedure name collision |
| Fortran | ✅ | ✅ | ✅ | ✅ | ✅ | Fixed: missing IRMatch, pattern vars |
| COBOL | ✅ | ✅ | ✅ | ✅ | ✅ | Fixed: missing IRMatch, var names |
| IL | ✅ | — | — | ✅ | — | Produces runnable `.exe`, limited |
| Babbage | — | — | — | — | — | Analytical Engine, intentionally limited |

The lowerer changes (binary-result-type, if/match inference) affect only the
self-hosted pipeline. The reference compiler's `Lowering.cs` already had
these behaviors, so the 10 mainstream backends are unaffected.

**One thing to verify:** Regenerate the `generated-output/` corpus and diff
against the committed versions. The only expected differences are the type
declaration ordering in `mini-bootstrap.*` (already visible in `git diff`).

---

## Files Changed (Uncommitted)

| File | Change |
|------|--------|
| `Codex.Codex/IR/Lowering.codex` | +64 lines: binary-result-type, if inference, match inference, ConstructedTy field access, per-field record lowering |
| `Codex.Codex/stage1-output.cs` | Regenerated from bootstrap (Stage 2 output) |
| `tools/Codex.Bootstrap/CodexLib.g.cs` | Synced with `out/Codex.Codex.cs` (Stage 1 output) |
| `Codex.Codex/type-diag.txt` | Regenerated diagnostic dump |
| `generated-output/*/mini-bootstrap.*` | Pre-existing ordering change (10 files) |

---

## Recommended Commit Strategy

1. **Commit current changes** — Lowerer fixes + Stage 1/2 regeneration
2. **Separate commit** — Generated output corpus refresh
3. **Separate commit** — Doc updates (milestones, forward plan)
4. **Future session** — Effect annotation parsing (Tier 1, item 1)
