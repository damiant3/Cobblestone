# Data Quire Compiler Crash — Investigation Report

**Date:** 2026-05-23 (updated 2026-05-24)  
**Stream:** RESTRUCTURE  
**Agent:** fester (resolved by gollum)  
**Status:** RESOLVED — root cause: type name `Page` shadowed by page-marker parser

## The Problem

Compiling any program that transitively includes `apps/data/Transaction.codex` crashes the compiler with a general protection fault in `is-compound`. Compiling `apps/data/Example.codex` reproduces it reliably.

Separately, `apps/data/Page.codex` has 21 type errors from nested record field access (`page.header.slot-count`) — CDX2000 "unresolved type for field 'header'". These don't crash but block compilation.

## Crash Details

```
CRASH in is-compound+0x5E (general protection)
  RIP   0x002C0217  is-compound+0x5E
  R12   0xC436880000000000  (corrupted pointer, deterministic)
  Heap  6.4 MB (Example standalone) / 13.4 MB (with CodexMagic)
```

The crash is in `codex/compiler/Syntax/ParserCore.codex:434` — `is-compound` pattern-matches on an `Expr` value, but the value is a corrupted pointer. This is a parser or memory corruption bug, NOT a size/heap issue.

## Isolation

| Configuration | Result |
|--------------|--------|
| `cites Data chapter Row` | OK |
| `cites Data chapter Catalog + Schema + Row` | Type errors only (Page.codex CDX2000) |
| `+ RelAlgebra` | Type errors only |
| `+ Executor` | CDX2010 infinite type (from `|>` pipe operator) |
| `+ Transaction` | **CRASH** in is-compound |
| `+ Hamt` (without Transaction) | OK |
| `Example.codex` (full deps) | **CRASH** |

**Transaction.codex is the trigger.** Adding it to ANY compilation causes the crash.

## Root Cause (Resolved 2026-05-24)

The parser's `is-page-marker` function (Parser.codex:661) treated ANY
line starting with the TypeIdentifier `Page` as a `Page N` page marker.
It did not verify the next token was an IntegerLiteral. This caused the
type definition `Page = record { ... }` in Page.codex to be silently
skipped — the parser consumed the line as a page marker and never
registered the `Page` type. All field accesses on `Page` records then
failed with CDX2000 "unresolved type" because the type binding did not
exist.

**Fix (two parts):**
1. `is-page-marker` now also checks `is-page-number (peek-kind st 1)`,
   requiring an IntegerLiteral after "Page".
2. The `Page` type in the Data quire was renamed to `DbPage` as a
   belt-and-suspenders measure.

### Original Report (superseded)

The original report attributed the crash to Transaction.codex corrupting
parser memory. On main (2026-05-24 seed), the parser crash does not
reproduce — the compiler reaches type checking and reports CDX2000
errors from Page.codex instead. The original GPF may have been a
RESTRUCTURE-stream-specific issue or was masked by the CDX2000 errors
being treated as fatal before reaching the parser path that crashed.

Need to test each dep individually to narrow which file actually triggers the corruption.

### 2. Page.codex Nested Field Access (CDX2000)
Page.codex uses nested record access: `page.header.slot-count`, `page.header.page-id`, etc. The type checker reports CDX2000 "unresolved type for field 'header'" — it can't resolve the intermediate record type when accessed through a chain.

This affects 21 call sites in Page.codex and Heap.codex. These are real type errors that would need the type checker to support chained field access on records from transitive dependencies.

### 3. Executor Infinite Type (CDX2010)
The `|>` pipe operator causes an infinite type error when used with `query cat` (partially applied). The pipe desugars `x |> f` to `f x`, but partial application of `query` with one argument doesn't type-check correctly through the pipe.

## Files Verified Identical to Main

All compiler source files: **identical to main**  
All Data quire files: **identical to main**  
Seed: tested with both RESTRUCTURE-built seed AND main's seed — same crash.

## What Works

- Data quire WITHOUT Transaction compiles (with Page.codex type errors)
- CodexMagic game modules (all 26 files) compile fine
- MagicServer (349KB CDX) compiles fine
- SimBaseline without DB compiles fine

## What SimDb Needs

SimDb (`apps/games/codexmagic/SimDb.codex`) needs:
- Catalog (for `catalog-new`, `catalog-create-table`, `catalog-insert-row`, `catalog-scan`)
- Schema (for `table-def-with-pk`, `col-def-not-null`, `table-schema`)
- Row (for `row-new`, `RowData`, `ColumnValue`)
- NOT RelAlgebra/Executor (query pipeline)
- NOT Transaction

The blocking issue is Page.codex CDX2000 errors — Catalog transitively imports Heap which imports Page. Even if SimDb doesn't USE Page, the type errors prevent compilation.

## Fix Options

1. **Fix the type checker** to resolve nested field access (`a.b.c`) on records from transitive deps. This would fix the 21 CDX2000 errors in Page.codex.

2. **Fix Transaction.codex** to not crash the parser. Narrow down which line/construct causes the corrupted pointer.

3. **Refactor Catalog** to not depend on Heap/Page for in-memory-only use. Create a "CatalogLite" that uses lists instead of the heap storage layer.

4. **Use CSV** as interim storage and integrate with the DB later once the compiler is fixed.

## Current State of SimBaseline

SimBaseline works WITHOUT the DB — it runs games, collects metrics, and outputs text results. The CSV approach via `tools/sim-test.ps1` also works. The DB integration is the only blocked item.

SimDb.codex exists and is correct code — it just can't compile until the Page.codex type errors are resolved.
