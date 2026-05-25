# Review: cam/cce-native-text

**Date**: 2026-03-26
**Reviewer**: Agent Linux
**Branch**: `cam/cce-native-text` (7 commits)
**Verdict**: LGTM — merged to master

---

## What It Does

Replaces Unicode with CCE (Codex Character Encoding) as the internal text
representation. Unicode exists only at I/O boundaries. String literals are
CCE-encoded at compile time. Character classification becomes simple range
checks.

## Verification

- Build: clean (0 warnings, 0 errors)
- Tests: 829/829 passing
- Fixed point: proven (Stage 1 = Stage 3 at 298,328 chars)

## What Was Reviewed

### CCE Encoding Table (revised Tier 0)

The encoding was revised from the original CCE-DESIGN.md ranges. Old encoding
had 8 whitespace slots (0-7); new encoding trims to 3 (NUL, LF, space) since
source code only needs those. This shifts everything down by 5, giving tighter
ranges:

| Category | Old range | New range |
|----------|-----------|-----------|
| Whitespace | 0-7 | 0-2 |
| Digits | 8-17 | 3-12 |
| Lowercase | 18-43 | 13-38 |
| Uppercase | 44-69 | 39-64 |
| Punctuation | 70-89 | 65-93 |
| Accented | 90-111 | 94-112 |
| Cyrillic | 112-127 | 113-127 |

Property preserved: all categories are contiguous, so classification is still
one or two comparisons.

### I/O Boundary Wrapping

Every builtin that crosses the Unicode/CCE boundary was updated consistently
in both the reference and self-hosted emitters:

| Builtin | Boundary direction |
|---------|--------------------|
| `read-file` | Unicode → CCE (path: CCE → Unicode) |
| `write-file` | CCE → Unicode (both path and content) |
| `print-line` | CCE → Unicode |
| `read-line` | Unicode → CCE |
| `show` | .NET `ToString()` → CCE |
| `integer-to-text` | .NET `ToString()` → CCE |
| `text-to-integer` | CCE → Unicode → `long.Parse` |
| `run-process` | CCE → Unicode (args), Unicode → CCE (result) |
| `get-args` | Unicode → CCE (each arg) |
| `get-env` | CCE → Unicode (key), Unicode → CCE (result) |
| `current-dir` | Unicode → CCE |
| `file-exists` | CCE → Unicode (path) |
| `list-files` | CCE → Unicode (path, pattern), Unicode → CCE (results) |

### Lexer Migration

All magic numbers replaced with `char-code 'X'` expressions. The Lexer is now
encoding-agnostic — works with whatever values the compiler resolves char
literals to. `is-letter-code` and `is-digit-code` delegate to the builtin
char-level functions.

### Self-Hosted Emitter

`escape-text` rewritten from `text-replace` chains to per-character loop with
`\uXXXX` escaping. Necessary because CCE byte values (0-127) are mostly
non-printable in ASCII, so the old replace-based approach won't work.

`emit-cce-runtime` added — emits the `_Cce` static class with lookup tables
and conversion functions. Identical logic to the reference emitter version.

### Bootstrap

`Program.cs` updated to convert Unicode → CCE on source load, CCE → Unicode
on emitted output write. Module name strings also converted at the boundary.
Debug token dump converts back to Unicode for readability.

## Open Concern: Triple-Copy CCE Table

The CCE-to-Unicode lookup table exists in three places:

1. `src/Codex.Emit.CSharp/CSharpEmitter.cs` — `EmitCceRuntime()` (emits table
   as C# source into runtime preamble)
2. `src/Codex.Emit.CSharp/CSharpEmitter.Utilities.cs` — `s_cceToUnicode`
   (compile-time table for string literal encoding)
3. `Codex.Codex/Emit/CSharpEmitter.codex` — `emit-cce-runtime` (self-hosted
   emitter's copy, emitted as string concatenation)

The commit history shows this was already a bug source: commit `c180d8c` is
"fix: update third copy of CCE table in EmitCceRuntime" — the self-hosted copy
got out of sync during development.

**Recommendation**: Extract the table to a single source of truth. Options:

- A shared `.json` or `.csv` file that all three locations read at build time.
- Generate the emitter string constants from the reference array in a build step.
- Or accept the triple-copy and add a build-time assertion that all three are
  identical (e.g., a test that parses all three and compares).

This isn't a blocker — the fixed point proves the self-hosted copy is
self-consistent with the reference copy right now. But the next person who
changes the encoding will hit the same sync bug.

## Open Item: Encoding Integration

Files written by the Codex compiler contain CCE-encoded text. External tools
(text editors, `cat`, `grep`, `diff`, debuggers) will display garbage unless
they know about CCE. This needs an integration story for both platforms.

See `docs/Designs/CCE-ENCODING-INTEGRATION.md` for the full analysis.

## Files Changed

47 files. Key changes in: reference emitter (3 files), self-hosted emitter (2),
Lexer (1), CCE prelude (1), bootstrap (1). The rest are regenerated output.
