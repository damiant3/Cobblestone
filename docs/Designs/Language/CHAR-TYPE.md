# Char Primitive Type

**Date**: 2026-03-25
**Status**: Approved, implementing on `cam/char-type`
**Agent**: Cam (Claude Code CLI)

---

## Why

The self-hosted Codex compiler is **28x slower** than the C# bootstrap. 85% of the
gap is in the lexer, where `char-at` returns `Text` — allocating a new string for
every character examined. On 163K chars of input, this produces billions of bytes
of garbage (192x more memory than the reference compiler).

The language has no `Char` type. Characters are represented as `Text` (strings),
which means every character operation allocates on the heap. The self-hosted lexer
works around this via `char-code-at` (returns `Integer`), but this is a hack: the
type system doesn't know about characters, and the workaround leaks integer codes
where character semantics belong.

The CCE design (`docs/Designs/CCE-DESIGN.md`) defines characters as codepoints with
computational structure — classification, case conversion, and script identification
encoded in bit patterns. A `Char` type is the natural home for these operations.

---

## Design

### Char as a Primitive Type

```
Char : Type0    -- CCE code point (integer with character semantics)
```

`Char` is a **distinct type** from `Integer` and `Text`. The type system prevents
implicit mixing. At runtime, `Char` has the same representation as `Integer`:

| Backend | Runtime representation |
|---------|-----------------------|
| C# | `long` |
| IL | `int64` |
| Wasm | `i64` |
| x86-64, ARM64, RISC-V | machine word (register) |

### Relationship to Text

`Text` is **not** `Array<Char>`. Text is a variable-width encoded byte stream
(CCE-encoded, self-synchronizing). `Char` is the decoded form — what you get after
resolving the framing.

For Tier 0 CCE (the vast majority of Codex source), `Char` and byte are the same
thing. For Tier 1+, a `Char` is the multi-byte decoded value.

Conversion between `Char` and `Text` is explicit:

```
char-at     : Text -> Integer -> Char      -- O(1) byte index, zero allocation
char-to-text : Char -> Text                -- allocates a 1-char string
```

### Updated Builtin Signatures

| Builtin | Old signature | New signature |
|---------|---------------|---------------|
| `char-at` | `Text -> Integer -> Text` | `Text -> Integer -> Char` |
| `char-code` | `Text -> Integer` | `Char -> Integer` |
| `code-to-char` | `Integer -> Text` | `Integer -> Char` |
| `char-code-at` | `Text -> Integer -> Integer` | *(unchanged, deprecated)* |
| `is-letter` | `Text -> Boolean` | `Char -> Boolean` |
| `is-digit` | `Text -> Boolean` | `Char -> Boolean` |
| `is-whitespace` | `Text -> Boolean` | `Char -> Boolean` |
| `char-to-text` | *(new)* | `Char -> Text` |

`char-code-at` is kept for backward compatibility but is semantically equivalent
to `char-code (char-at text idx)`. It will be deprecated in a future release.

### Character Literals

No character literal syntax (e.g., `'a'`) in this phase. Characters are created via:
- `char-at "a" 0` — extract from a string
- `code-to-char 65` — from an integer code

Character literal syntax is a follow-up.

### Show on Char

`show (char-at "hello" 0)` produces `"h"`, not `"104"`. Char has character identity,
not numeric identity.

---

## Performance Impact

### Before (char-at returns Text)

| Backend | char-at cost |
|---------|-------------|
| C# | `text[(int)idx].ToString()` — heap alloc per call |
| IL | `String.get_Chars()` + `Char.ToString()` — heap alloc |
| Wasm | ~20 ops: bump-alloc 5 bytes, copy byte, return ptr |
| x86-64 | 16-byte heap alloc + store length + store byte |
| ARM64 | heap alloc + `strb` |
| RISC-V | heap alloc + `sb` |

### After (char-at returns Char)

| Backend | char-at cost |
|---------|-------------|
| C# | `(long)text[(int)idx]` — zero alloc, one array index |
| IL | `String.get_Chars()` + `Conv_i8` — zero alloc |
| Wasm | ~3 ops: load byte, extend to i64 |
| x86-64 | `movzx` byte into register — one instruction |
| ARM64 | `ldrb` into register — one instruction |
| RISC-V | `lbu` into register — one instruction |

Projected lexer improvement: **800x -> ~5x** of reference (from the performance
report's analysis, the per-character string allocation is the dominant cost).

---

## Implementation Plan

### Commit 1: Core type system + C# and IL emitters + tests

Add `CharType` to the type hierarchy. Update `char-at`, `char-code`, `code-to-char`,
`is-letter`, `is-digit`, `is-whitespace` signatures. Add `char-to-text`. Update the
C# and IL emitters (the two emitters exercised by the test suite). Fix all affected
tests.

**Files:** `CodexType.cs`, `TypeChecker.Resolution.cs`, `TypeEnvironment.cs`,
`Lowering.cs`, `NameResolver.cs`, `CSharpEmitter.Expressions.cs`, `CSharpEmitter.cs`,
`ILAssemblyBuilder.cs`, `IntegrationTests3.cs`

### Commit 2: Transpiler emitters

Update JS, Python, Go, Java, Rust, C++, Ada, Cobol, Fortran emitters. Each: `char-at`
returns integer code instead of string. Add `char-to-text` case.

### Commit 3: Native emitters (the performance win)

Update Wasm, x86-64, ARM64, RISC-V. `char-at` returns byte value in register —
no heap allocation.

### Commit 4: Self-hosted compiler

Add `CharTy` to `CodexType.codex`. Update type environment, emitter, type checker,
and name resolver in the self-hosted compiler.

---

## Relationship to CCE

This change adds the **type** that CCE operations live on. The CCE prelude
(`prelude/CCE.codex`) currently operates on `Integer` — every function takes and
returns `Integer`. With `Char`, these functions gain their proper types:

```
is-cce-letter : Char -> Boolean      -- was Integer -> Boolean
cce-to-lower  : Char -> Char         -- was Integer -> Integer
cce-classify  : Char -> CCEClass     -- was Integer -> CCEClass
```

This migration is not part of this changeset but is enabled by it.
