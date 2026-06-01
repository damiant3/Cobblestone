# Source Formatting Workflow

## Overview

The `codex format` command normalizes indentation in `.codex` source files to
match the canonical prose format. It preserves Chapter/Section headers and prose
text while ensuring code indentation follows the standard rules:

- Chapter and Section headers at column 1
- Prose text at 1-space indent
- Type definitions and function annotations at 2-space indent
- Function bodies and continuation lines at 3+ space indent
- Variant constructors at 3-space indent
- Record fields at 3-space indent

## Usage

Preview formatted output (prints to stdout):
```bash
dotnet run --project tools/Codex.Cli -- format Codex.Codex/Syntax/Parser.codex
```

Overwrite file in place:
```bash
dotnet run --project tools/Codex.Cli -- format Codex.Codex/Syntax/Parser.codex --write
```

## Full Reformat Process

This process reformats all `.codex` source files. It should be done on a
dedicated branch and reviewed before merging.

### Step 1: Create a branch

```bash
git checkout -b reformat-source
```

### Step 2: Format all files

```bash
for f in $(find Codex.Codex -name '*.codex' | sort); do
    dotnet run --project tools/Codex.Cli -- format "$f" --write
done
```

### Step 3: Verify the compiler still works

```bash
dotnet build Codex.sln
dotnet test Codex.sln
dotnet run --project tools/Codex.Cli -- bootstrap Codex.Codex
```

The bootstrap MUST still pass. If it doesn't, the formatter changed semantics
(likely a misclassified line). Fix the formatter, revert, and retry.

### Step 4: Review the diffs

```bash
git diff Codex.Codex/
```

The formatter only changes whitespace. If you see content changes, the
formatter has a bug. The most common edge cases:

- Lines that look like code but are prose (e.g., English sentences that
  start with a name followed by a colon)
- Lines that look like prose but are code (e.g., continuation lines at
  low indentation)
- Record closing braces `}` indentation
- Variant constructor `|` lines

### Step 5: Manual prose review

After formatting, review each file's prose sections for accuracy. The prose
describes what the code does. If the code has changed since the prose was
written, the prose may be wrong. Common issues:

- Function names that changed during refactoring
- Pipeline descriptions that don't match the current implementation
- Parameter descriptions that are outdated

This is a manual review task — read the prose, read the code below it,
verify they agree. Fix any prose that doesn't match.

### Step 6: Commit and verify

```bash
git add Codex.Codex/
git commit -m "style: normalize source formatting"
dotnet run --project tools/Codex.Cli -- bootstrap Codex.Codex
```

Run the bootstrap one more time after commit to confirm everything is clean.

## What the Formatter Does NOT Do

- It does not parse or validate Codex code. It works at the line level.
- It does not rewrite prose. Prose text is preserved as-is.
- It does not add or remove Chapter/Section headers.
- It does not change the order of definitions.
- It does not handle name mangling or scoping. Those are compiler concerns.
- It does not add the `Page N` markers.

## Edge Cases

The formatter uses heuristics to classify lines as code vs prose. If a prose
line happens to match the code pattern (starts with a word followed by `:` or
`=`), the formatter may indent it as code. Review the diff and adjust the
heuristic in `Program.Format.cs` if needed.
