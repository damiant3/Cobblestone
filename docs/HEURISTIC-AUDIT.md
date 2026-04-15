# Heuristic Audit — Codex Compiler

**Date:** 2026-04-10 (reconciled 2026-04-15)
**Auditor:** Agent Linux
**Status:** OPEN — one remaining item

Originally 9 items. H-001 through H-008 have been fixed. H-009 remains as a
low-priority design-smell item; current naming conventions avoid the ambiguity.

---

## H-009: `slug-matches-cite` uses fuzzy matching

**Severity:** LOW — ambiguous chapter resolution
**Status:** STILL PRESENT
**File:** `Codex.Codex/Semantics/ChapterScoper.codex:463-465`

`strip-hyphens (slugify slug) == strip-hyphens (slugify cite-name)` — "Type-Checker",
"TypeChecker", "Type Checker" all match the same chapter.

**Fix:** Exact match after a single canonical normalization, or assign unique IDs.
