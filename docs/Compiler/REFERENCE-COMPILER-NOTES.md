# Reference Compiler Notes

> Companion to [REFERENCE-COMPILER-LOCK.md](REFERENCE-COMPILER-LOCK.md).
> This file records detailed context for seal-break overrides that are too
> verbose for the lock file's summary format.

---

## Override 6: Safe `text-to-integer` in IL emitter + legacy emitter cleanup

**Date**: 2026-03-21
**Authorized by**: User (project owner)
**Agent**: Copilot (Claude Sonnet 4, VS 2022, Windows)

### Background

While building black-box tests for the `.codex`-authored agent toolkit
(`tools/codex-agent/`), we discovered that any Codex program calling
`text-to-integer` on non-numeric input (e.g. `"abc"`, `""`, `"3.14"`,
or overflow values like `"99999999999999999999"`) would crash with an
unhandled `FormatException` or `OverflowException`.

The root cause was in the IL emitter: the `text-to-integer` builtin emitted
a raw `System.Int64.Parse(string)` call with no error handling. `Int64.Parse`
throws on any input that isn't a valid 64-bit integer string.

This affected every `.exe` compiled from `.codex` source via the IL backend —
including `codex-agent.exe`, `peek.exe`, `fstat.exe`, and `sdiff.exe`.

### The Fix

Replaced `Int64.Parse` with `Int64.TryParse` in the emitted IL. The new
pattern emits:

```
// string on stack
ldloca __tti_result     // address of local for out param
call   Int64.TryParse   // bool on stack; result in local
brtrue success
ldc.i8 0                // failure → push 0
br     end
success:
ldloc  __tti_result     // success → push parsed value
end:
```

This required:
1. A new `MemberReferenceHandle` for `Int64.TryParse(string, out long) : bool`
   with a correctly encoded by-ref parameter (ECMA-335 `ELEMENT_TYPE_BYREF`
   before `ELEMENT_TYPE_I8`).
2. A scratch local (`__tti_result`) for the `out` parameter, using the
   existing `LocalsBuilder` pattern already used by `integer-to-text`.
3. Branch IL (`brtrue_s` / `br_s`) using the existing `DefineLabel`/`MarkLabel`
   pattern from `EmitIf`.

The fix is minimal and surgical — only the `text-to-integer` case in
`TryEmitBuiltinCore` changed. All existing IL emission paths are untouched.

### Files Modified

| File | Change |
|------|--------|
| `src/Codex.Emit.IL/ILAssemblyBuilder.cs` | Added `m_int64TryParseRef` field, built `TryParse(string, out long)` reference in `BuildCorlibReferences()`, replaced `text-to-integer` case with TryParse+branch pattern |

### Concurrent Cleanup: Legacy Emitter Build Errors

The build had pre-existing errors because `CompileToJS` and `CompileToPython`
references in test code and the CLI were outside `#if LEGACY_EMITTERS` guards,
but the JavaScript/Python emitter projects were no longer in the solution.

| File | Change |
|------|--------|
| `tools/Codex.Cli/Codex.Cli.csproj` | Flipped `LEGACY_EMITTERS` default to opt-in (`== 'true'`) |
| `tests/Codex.Types.Tests/Codex.Types.Tests.csproj` | Same flip |
| `tools/Codex.Cli/Program.Build.cs` | Moved JS/Python into `#if LEGACY_EMITTERS` |
| `tests/Codex.Types.Tests/Helpers.cs` | Same, plus `#else` stubs returning `null` |
| `tests/Codex.Types.Tests/CorpusEmissionTests.cs` | Moved JS/Python into guard |

### New Tests

**IL emitter integration tests** (in `ILEmitterIntegrationTests.cs`):

| Test | Asserts |
|------|---------|
| `Text_to_integer_on_valid_input` | `"42"` → `42` |
| `Text_to_integer_on_non_numeric_returns_zero` | `"abc"` → `0` |
| `Text_to_integer_on_empty_string_returns_zero` | `""` → `0` |
| `Text_to_integer_on_negative_number` | `"-7"` → `-7` |
| `Text_to_integer_on_float_string_returns_zero` | `"3.14"` → `0` |
| `Text_to_integer_on_overflow_returns_zero` | `"99999999999999999999"` → `0` |

These compile a `.codex` snippet via the full pipeline (Lexer → Parser →
Desugarer → NameResolver → TypeChecker → Lowering → IL Emitter), write
the bytes to a temp `.dll`, run with `dotnet`, and assert stdout.

**Agent toolkit black-box tests** (new project `Codex.AgentToolkit.Tests`):

57 tests exercising the compiled `.exe` tools as external processes.
Covers `codex-agent.exe` (peek, stat, snap, status, plan, check),
`peek.exe`, `fstat.exe`, and `sdiff.exe`. Tests are sequenced via
`[Collection("AgentToolkit")]` to avoid directory-cleanup races.

### Verification

| Check | Result |
|-------|--------|
| `dotnet build Codex.sln` | ✅ 0 errors (excluding pre-existing CS5001 in Codex.Codex) |
| IL emitter tests | ✅ 63 passed (57 existing + 6 new) |
| Agent toolkit tests | ✅ 57 passed |
| Types.Tests (non-legacy) | ✅ 266 passed, 2 skipped |
| Existing IL compilation | ✅ All `CompileAndRun` tests green |

### Design Note

The `text-to-integer` semantics are now: return the parsed `Int64` on
success, return `0` on any failure (non-numeric, empty, overflow, etc.).
This matches the behavior of `parse-int-or` in the `.codex` agent tools,
which was trying to implement this same fallback in user code but couldn't
because the underlying builtin crashed before the fallback could execute.

The C# emitter (`CSharpEmitter.Expressions.cs`) was not changed because
it emits `long.Parse(...)` which is wrapped by user-level try/catch in
the generated C# code. The IL emitter has no try/catch support yet, so
the fix must be at the builtin level.
