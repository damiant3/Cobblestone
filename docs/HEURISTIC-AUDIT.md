# Heuristic Audit — Codex Compiler

**Date:** 2026-04-10
**Auditor:** Agent Linux
**Status:** OPEN — all items must be resolved before MM4 binary self-compilation

At 2.537 million light years, no patch is possible. Every heuristic is a time bomb
with a fuse proportional to input diversity. This document catalogs every place the
compiler guesses instead of knowing.

---

## H-001: `cc-upper-a` range check misses E and T

**Severity:** CRITICAL — likely root cause of binary pingpong failure
**Files:** `Syntax/Lexer.codex` (classify-word), `Semantics/ChapterScoper.codex` (slugify)

CCE encodes uppercase letters by frequency: E(39), T(40), A(41), O(42), ..., Z(64).
The range check `first-code >= cc-upper-a` uses `cc-upper-a = char-code 'A' = 41`.
This misses E (39) and T (40) — the two most common uppercase letters.

**Impact in lexer:** Any type name starting with E or T is classified as `Identifier`
instead of `TypeIdentifier`. Affected types include: `Token`, `TypeExpr`, `EmitResult`,
`ErrorTy`, `EffectfulTy`, `TypeBinding`, `TcoState`, `ElfWriter`, `TypeEnv`, `TypeChecker`,
`TypeCheckerInference`, `Text` (the type), and many more.

Stage 0 is unaffected (C# lexer uses `char.IsUpper`). Stage 1's self-hosted lexer
would misclassify these, causing parse failures or wrong ASTs for the compiler source.

**Impact in slugify:** Chapter names starting with E or T are not lowercased. "Type Checker"
slugifies to "Type-checker" (capital T preserved). This doesn't break slug-matches-cite
(both sides use the same slugify) but produces inconsistent mangled names.

**Fix:** Replace `cc-upper-a` with `cc-upper-e`:
```
cc-upper-e : Integer
cc-upper-e = char-code 'E'
```
And use `first-code >= cc-upper-e` in both classify-word and slugify. Or better:
define `is-upper-code : Integer -> Boolean` that checks `c >= 39 & c <= 64`.

---

## H-002: `lookup-func-offset` returns 0 for missing functions

**Severity:** CRITICAL — silent crash at runtime
**File:** `Emit/X86_64.codex` line 219

When a function name is not found in the offset table, the lookup returns 0. The call
instruction gets patched with a relative offset to position 0 — the multiboot header.
The CPU executes `0x1BADB002` (multiboot magic) as an instruction and crashes.

The C# reference at least prints `X86_64 WARNING: unresolved call to '...'`.

**Fix:** Either halt compilation with an error (correct) or emit a jump to a known
trap handler (defensive). Returning 0 is never correct.

---

## H-003: `emit-field-access` defaults to offset 0 for unknown types

**Severity:** HIGH — silent wrong field access
**File:** `Emit/X86_64.codex` lines 1249, 1670, 1753

Three instances of `if _ -> 0` in field index resolution. If the type is not `RecordTy`,
the field offset defaults to 0. This means ALL fields resolve to the first field.

The ConstructedTy fix addressed the most common case, but the `_ -> 0` fallback
remains. Any new type representation that reaches the emitter without being resolved
will silently access the wrong field.

**Fix:** Replace `if _ -> 0` with a trap or diagnostic. Field index 0 is a valid
value — it cannot be distinguished from a genuine first-field access.

---

## H-004: `expect` does not verify token kind

**Severity:** MEDIUM — parser silently skips wrong tokens
**File:** `Syntax/ParserCore.codex`

```codex
expect (kind) (st) =
 if is-done st then st
 else advance st
```

The `kind` parameter is completely ignored. The parser advances past whatever token
is present, whether it matches or not. Malformed input propagates silently through
the parser.

**Fix:** Check `current-kind st == kind` before advancing. On mismatch, either
produce a diagnostic or enter error recovery.

---

## H-005: `ErrorTy` as silent failure propagation

**Severity:** HIGH — type errors become wrong code
**Files:** 20+ instances across Lowering, TypeChecker, TypeEnv, Unifier, CSharpEmitter

Every type lookup failure returns `ErrorTy`. This value flows through the entire
pipeline — lowering, type checking, emission — as if it were a valid type. Functions
that receive `ErrorTy` as input produce `ErrorTy` as output, creating cascading
silent failures.

In the emitter, `ErrorTy` falls through to `_ ->` default branches, producing
wrong code (offset 0, missing calls, etc.).

**Fix:** ErrorTy should be checked at pipeline boundaries. Any ErrorTy reaching
the emitter should halt compilation. Alternatively, use a Result type that forces
callers to handle the error.

---

## H-006: `ConstructedTy` as unresolved type wrapper

**Severity:** HIGH — requires every consumer to resolve
**Files:** `Types/TypeChecker.codex` (lookup-type-def), `IR/LoweringTypes.codex`

When `lookup-type-def` cannot find a type name, it creates `ConstructedTy name []`
instead of reporting an error. This opaque wrapper flows through the pipeline,
requiring every consumer (emitter, pattern match, field access, equality) to call
`resolve-constructed-ty`.

We have already fixed 4+ consumers that forgot to resolve. Each new code path that
touches types needs the same resolution. This is a systemic fragility.

**Fix:** Resolve ConstructedTy at the IR level (during lowering) so emitters never
see it. The lowering phase has access to the type-def map and can resolve all
ConstructedTy to their underlying RecordTy or SumTy. If resolution fails, produce
ErrorTy (which is then caught by H-005's fix).

---

## H-007: Flat rename map — last citation wins

**Severity:** MEDIUM — silent wrong function call
**File:** `Semantics/ChapterScoper.codex` (apply-cite-selected)

The scoper uses a flat list of rename entries. When a chapter cites two different
chapters that define the same function name, `remove-rename` deletes the first
mapping and adds the second. The first chapter's version becomes inaccessible.

No warning is produced. The code compiles and runs — calling the wrong function.

**Fix:** Short term: detect and warn when two citations target the same name.
Long term: implement a proper module system with qualified names
(`TypeChecker.resolve-type-expr` vs `LoweringTypes.resolve-type-expr`).

---

## H-008: R8/R9 toggle assumes max 2 concurrent spilled loads

**Severity:** HIGH — silent register clobbering
**File:** `Emit/X86_64.codex` (load-local)

The register allocator uses a toggle between R8 and R9 for loading spilled locals.
If 3 or more spilled values need to be live simultaneously, the third load reuses
R8, silently overwriting the first value.

No detection mechanism exists. The toggle wraps modulo 2 without checking whether
the previous value has been consumed.

**Fix:** Either expand the scratch pool (R8, R9, R11 — but R11 is used as temp),
or push/pop instead of toggle for spilled loads, or statically verify that no code
path produces 3+ simultaneous spilled loads.

---

## H-009: `slug-matches-cite` uses fuzzy matching

**Severity:** LOW — ambiguous chapter resolution
**File:** `Semantics/ChapterScoper.codex`

Chapter citation matching strips hyphens and slugifies both the chapter slug and
the cite name, then compares. This means "Type-Checker", "TypeChecker", and
"Type Checker" all match the same chapter. Two chapters with names that differ
only by hyphenation or spacing would be ambiguous.

**Fix:** Use exact matching after a single canonical normalization. Or assign
each chapter a unique identifier that doesn't depend on name normalization.

---

## Resolution Priority

1. **H-001** — Fix immediately. Likely root cause of binary pingpong failure.
2. **H-002** — Fix immediately. Silent crash is unacceptable.
3. **H-003** — Fix with H-006. Resolve types before they reach the emitter.
4. **H-006** — Fix in lowering. Eliminate ConstructedTy before emission.
5. **H-005** — Fix at pipeline boundaries. ErrorTy must not reach the emitter.
6. **H-008** — Audit all code paths. Expand scratch pool or change mechanism.
7. **H-004** — Fix in parser. Verify token kind before advancing.
8. **H-007** — Warn on duplicate citations. Module system is on the horizon.
9. **H-009** — Low priority. Current naming conventions avoid ambiguity.
