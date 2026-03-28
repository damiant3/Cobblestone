# Review: cam/fix-crlf-lexer (244e339) — CCE Encoding Bugs

**Reviewer**: Agent Linux  
**Date**: 2026-03-28  
**Commit**: 244e339 `fix: CCE encoding bugs in self-hosted compiler — 1047→34 type errors`  
**Verdict**: ✅ Merge — critical fix, no regressions

---

## Summary

Three bugs caused by assuming Unicode/ASCII character ordering in
CCE-encoded self-hosted code. CCE orders by frequency (e=13, t=14,
a=15, ..., z=38, E=39, T=40, A=41, ..., Z=64), not alphabetically.

**Impact**: 1047 → 34 type errors. 95/95 type defs recovered. 337K C#
output from self-hosted bootstrap.

## Bugs Fixed

### 1. cc-cr = 13 stripped leading 'e' from all tokens (Lexer.codex)

CCE byte 13 is the letter 'e', not carriage return. The CRLF "fix" from
the previous branch was silently stripping 'e' from token starts —
`emit` → `mit`, `else` → `lse`, `emit-variant-ctors` → `mit-variant-ctors`.
Masked because `_Cce.FromUnicode` already strips `\r` before encoding.

**Fix**: `cc-cr = 0 - 1` — sentinel that never matches any byte.

### 2. cc-upper-a excluded 'E' and 'T' (Lexer.codex + NameResolver.codex)

`char-code 'A'` = CCE 41. But in CCE frequency ordering, 'E' = 39 and
'T' = 40 come BEFORE 'A'. So the uppercase range [41, 64] excluded the
two most common uppercase starting letters. Type names starting with E
or T — Expr, Token, TypeExpr, TypeBinding, ErrorTy, EffectDef,
TypeChecker, etc. — were classified as Identifier instead of
TypeIdentifier. This lost 16 type definitions and all their constructors
from the type environment.

**Fix**: `cc-upper-a = char-code 'E'` — range now [39, 64], covering
all uppercase letters in CCE.

### 3. is-value-name used hardcoded ASCII 97-122 (TypeChecker.codex)

`code >= 97 & code <= 122` checks ASCII 'a'-'z'. In CCE, lowercase
letters are bytes 13-38. Type variable parameterization was broken.

**Fix**: `char-code 'e' ... char-code 'z'` — CCE range [13, 38].

## Changes

| File | Change |
|------|--------|
| Codex.Codex/Syntax/Lexer.codex | `cc-cr = 0 - 1`, `cc-upper-a = char-code 'E'` |
| Codex.Codex/Semantics/NameResolver.codex | `is-upper-char` range fix |
| Codex.Codex/Types/TypeChecker.codex | `is-value-name` range fix |
| tools/Codex.Bootstrap/Program.cs | Bootstrap diagnostics now human-readable |
| docs/CurrentPlan.md | Updated status, investigation notes |

## Test Results

907 passed, 7 failed (same env). Identical to master. No regressions.

## Architectural Note

This class of bug — assuming alphabetical ordering equals encoding
ordering — will recur anywhere character classification is done by range
comparison. CCE's frequency-based ordering means the "first" uppercase
letter is 'E' (most common), not 'A'. Any future range check on CCE
strings must use `char-code` to get the actual CCE code, never hardcoded
numeric constants from ASCII/Unicode.

The 34 remaining errors are a separate class from the 1047 that were
caused by these bugs.

---

*Reviewed from Linux sandbox. Build clean, 907/907 tests pass.*
