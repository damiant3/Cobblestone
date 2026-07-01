# Prose Bracket Parse Bug

## Status: RESOLVED — CANNOT REPRODUCE (2026-06-11, reek)

Re-verified against the current compiler (seed at CL 3784). None of
these reproduce the documented failure; all compile clean:

- The doc's own minimal repro (bracket prose at column 2).
- Bracket prose at column 3+ (true code context; tokens are silently
  absorbed by parser resync, no error).
- Bracket prose directly adjacent to definition bodies and when-arms
  (no separating blank line).
- A 4,005-line synthetic file with 1,200 bracket prose lines.

Probable history: the original spark-webgpu symptoms ("the error
follows the function", "changing code above shifts the error line",
"compiled at 7800 lines, failed past a threshold") match the
diagnostic line-number drift fixed in CL 3783 (the pre-lex
newline-run collapse shifted every reported line after a blank run,
so the real error's reported position pointed at innocent code and
moved when lines were added or removed). The intervening parser-fix
campaign (CLs 3263-3308) may also have removed a real trigger.
Regression sample locked in: codex/test/prose-brackets.codex.

The original report follows for the record.

## Summary

Prose lines at column 2 containing `[` are misinterpreted as list
expressions by the parser. This causes cascading parse errors
(CDX1000) on subsequent definitions, often hundreds of lines later.

## Severity

Medium. Does not affect the compiler selfhost (no bracket prose in
compiler source). Affects application code that documents memory
layouts in prose comments using bracket notation like `[0] field-name`.

## Reproduction

Any chapter with prose at column 2 containing `[`:

```
Section: Example

 Memory layout at 0x100000:
 [0] field-a   i32
 [4] field-b   i32

  my-function : Integer
  my-function = 42
```

The parser reads ` [0] field-a   i32` as an attempted list expression
`[0]` followed by `field-a`, which doesn't parse. The error does not
surface immediately — it corrupts the parser's state and manifests as
CDX1000 ("Expected token kind mismatch, got ':'") on a later
definition's type annotation, often far from the actual bracket line.

## Diagnosis Pattern

- CDX1000 at a valid type annotation (e.g. `name : Integer`)
- The error persists regardless of renaming the function or
  simplifying its body
- The error follows the function — changing code above shifts
  the error line number
- Removing bracket prose lines from earlier in the file fixes it

## Root Cause

The lexer does not distinguish prose context (column 2, under a
`Section:` header) from code context when it encounters `[`. In code,
`[` starts a list literal. In prose, it should be ignored. The lexer
tokenizes `[0]` as `ListOpen IntLit(0) ListClose`, and the parser
tries to parse a list expression in definition position. This fails
and leaves the parser in a confused state, but the error is not
immediately fatal — subsequent definitions parse with accumulated
corruption until one triggers CDX1000.

## Workaround

Do not use `[` in prose lines. Replace bracket notation with
alternative formatting:

```
 Memory layout at 0x100000:
 offset 0: field-a (i32)
 offset 4: field-b (i32)
```

Or move memory layout documentation to a separate non-Codex file.

## Fix

The lexer (codex/compiler/Syntax/Lexer.codex) should skip `[` tokens
when the current column is 2 or less (prose position). Alternatively,
the prose scanner should consume entire prose lines without tokenizing
their content — prose at column 2 is human-readable commentary, not
code, per the language specification.

## Discovery

Found 2026-06-07 during Spark WebGPU buildout (agent: fester,
CL 3254). The spark-webgpu.codex file had 21 bracket prose lines
documenting memory-mapped regions. The file compiled at 7800 bundled
lines but failed with CDX1000 when new definitions pushed past the
corruption threshold. Removing all bracket prose lines eliminated
the errors entirely, confirming the root cause.

The compiler's own source (33K lines, 1.3MB) has no bracket prose
and is unaffected. The X86_64 chapter (10+ pages) compiles without
issue, proving the parser has no inherent size limit — the apparent
~300KB ceiling was bracket prose corruption, not a capacity constraint.
