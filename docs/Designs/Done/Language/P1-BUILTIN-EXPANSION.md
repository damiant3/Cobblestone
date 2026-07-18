# P1: Self-Hosted Built-in Expansion

**Date**: 2026-03-20 (verified via system clock)
**Author**: Claude (Opus 4.6, claude.ai, Linux)
**Status**: ✅ Complete — all 21 builtins inlined, bootstrap fixed-point proven

---

## Summary

P1 was listed as ⬜ Critical in CurrentPlan.md with the description: "The self-hosted
emitter calls built-ins by name instead of inlining them." **This is no longer accurate.**
The self-hosted emitter (`CSharpEmitterExpressions.codex`) already contains inline
expansions for 20 of 21 built-in functions. Only `read-line` is missing.

This document captures the actual current state and the remaining work to close P1,
unblock dogfooding, and enable standalone execution of self-hosted compiler output.

---

## Current State: Built-in Coverage

### Self-hosted `emit-builtin` (CSharpEmitterExpressions.codex, lines 185–210)

| Built-in | C# Expansion | Status |
|----------|-------------|--------|
| `show` | `Convert.ToString(...)` | ✅ |
| `negate` | `-(...)` | ✅ |
| `print-line` | `Console.WriteLine(...)` | ✅ |
| `text-length` | `((long)...Length)` | ✅ |
| `is-letter` | `char.IsLetter(...[0])` | ✅ |
| `is-digit` | `char.IsDigit(...[0])` | ✅ |
| `is-whitespace` | `char.IsWhiteSpace(...[0])` | ✅ |
| `text-to-integer` | `long.Parse(...)` | ✅ |
| `integer-to-text` | `(...).ToString()` | ✅ |
| `char-code` | `((long)...[0])` | ✅ |
| `char-code-at` | `((long)...[(int)i])` | ✅ |
| `code-to-char` | `((char)...).ToString()` | ✅ |
| `list-length` | `((long)...Count)` | ✅ |
| `char-at` | `...[(int)i].ToString()` | ✅ |
| `substring` | `...Substring((int)i, (int)n)` | ✅ |
| `list-at` | `...[(int)i]` | ✅ |
| `text-replace` | `...Replace(a, b)` | ✅ |
| `open-file` | `File.OpenRead(...)` | ✅ |
| `read-all` | `new StreamReader(...).ReadToEnd()` | ✅ |
| `close-file` | `...Dispose()` | ✅ |
| `read-line` | `Console.ReadLine()` | ❌ Missing |

The self-hosted `is-builtin-name` recognizes all 20 implemented builtins. Both
functions have survived the bootstrap — they are present and correct in
`stage1-output.cs` (the compiled self-hosted compiler).

### Comparison with Reference Compiler

The reference C# emitter (`CSharpEmitter.Expressions.cs`) handles builtins in two
groups: single-arg (direct `if` chain) and multi-arg (`s_multiArgBuiltins` set with
switch/case). The self-hosted emitter handles both groups uniformly in `emit-builtin`
via the `args` list and `list-at`. Functionally equivalent.

The self-hosted emitter additionally covers `char-code-at` which the reference emitter
also handles but routes through the multi-arg path.

---

## Remaining Work

### Task 1: Add `read-line` to self-hosted emitter (trivial)

Add to `is-builtin-name`:
```codex
else if n == "read-line" then True
```

Add to `emit-builtin`:
```codex
else if n == "read-line" then "Console.ReadLine()"
```

Note: `read-line` is a zero-arg builtin (no arguments). The reference compiler handles
it as a special case in the name emission path (line 32 of `CSharpEmitter.Expressions.cs`),
not in the application path. The self-hosted emitter should handle it similarly — when
`read-line` appears as a bare name (not in an application), emit `Console.ReadLine()`
directly. Check how the current emit path dispatches bare names vs applications to ensure
`read-line` works in both positions.

**Estimated effort**: ~10 minutes + rebuild + fixed-point verification.

### Task 2: Verify standalone execution

The compiler's `main.codex` currently compiles a hardcoded test string:

```codex
main : [Console] Nothing
main = do
  print-line (compile test-source "test")
```

To prove P1 is closed, we need to verify:

1. Build the solution (`dotnet build Forward.sln`)
2. Stage 1 output (`stage1-output.cs`) compiles standalone as a C# program
3. The resulting binary, when executed, produces valid C# output from `.codex` input
4. The fixed point holds: Stage 2 = Stage 3

### Task 3: Re-verify fixed point after `read-line` addition

Current state on disk: `stage1-output.cs` (250,332 chars) ≠ `stage3-output.cs`
(227,301 chars). These files may be stale from different bootstrap runs. After
adding `read-line`, run the full bootstrap verification:

```bash
bash tools/linux-bootstrap.sh
```

Expected: Stage 2 == Stage 3, byte-for-byte.

### Task 4: Update CurrentPlan.md

Change P1 from ⬜ Critical to ✅ Complete once Tasks 1–3 pass.

---

## What P1 Completion Enables

With builtins fully inlined, Stage 1 output is standalone valid C# that depends only
on `System`, `System.Collections.Generic`, `System.Linq`, and `System.IO`. No runtime
shim needed.

This unblocks:

| Item | What it enables |
|------|----------------|
| **Dogfooding** | Write tools in .codex, compile with self-hosted compiler, run with `dotnet` |
| **R6 (native bootstrap)** | IL emitter can target the same builtins → standalone .exe |
| **E1 (real programs)** | User programs with console I/O, file I/O, string ops all work |
| **New-era dashboard** | Rewrite cognitive meter in .codex instead of bash/PowerShell |

---

## Broader Dogfooding Strategy (Post-P1)

Once P1 is closed, the priority shifts to making the compiler usable as a real CLI tool:

1. **File-based compilation** — `main.codex` currently compiles a hardcoded string.
   Change it to read from file arguments or stdin using `open-file` + `read-all`.

2. **First dogfood target** — rewrite the simplest shell script (`codexdashboard.sh`)
   in `.codex`. It's mostly string formatting and file reading. Proves the language
   works for real tasks.

3. **New cognitive meter** — dashboard counters shift from bootstrap-era metrics
   (type debt, fixed point, context budget) to dogfood-era metrics (dogfood ratio,
   external dependency count, practical program complexity, MCP tool coverage).

4. **Tool replacement sequence** — ordered by complexity, each one exercises more
   language features:
   - Dashboard (string formatting, file reading)
   - Session init (process execution, conditional logic)
   - Bootstrap verifier (file comparison, multi-stage orchestration)
   - MCP server (JSON parsing, TCP/stdio, the full pipeline)

---

## Implementation Plan

**Next prompt**: Add `read-line`, rebuild, verify fixed point. ~30 minutes total.

**Following prompt**: Update `main.codex` to accept file input, compile a real
sample program through Stage 1, verify output runs correctly. Update CurrentPlan.md.
