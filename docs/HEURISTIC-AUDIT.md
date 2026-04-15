# Heuristic Audit — Codex Compiler

**Date:** 2026-04-10 (reconciled 2026-04-15)
**Auditor:** Agent Linux
**Status:** OPEN — remaining items must be resolved before MM4 binary self-compilation

At 2.537 million light years, no patch is possible. Every heuristic is a time bomb
with a fuse proportional to input diversity. This document catalogs every place the
compiler guesses instead of knowing.

Originally 9 items; H-001 and H-004 have been fully fixed since the audit landed.

---

## H-002: `lookup-func-offset` silently misresolves missing functions

**Severity:** CRITICAL — wrong code, no diagnostic
**Status:** PARTIALLY FIXED — no longer returns 0
**File:** `Codex.Codex/Core/OffsetTable.codex:62-74`, `Codex.Codex/Emit/X86_64.codex:355`

`OffsetTable` now returns `-1` for missing functions (was `0`, which hit the multiboot
header). But `collect-call-patches` uses the value unchecked in
`rel32 = target-offset - (p.patch-offset + 5)`, so a missing function becomes a garbage
relative offset rather than a multiboot crash. Still no trap.

**Fix:** Check for `-1` in `collect-call-patches` and halt compilation with a diagnostic.

---

## H-003: `emit-field-access` logs but still uses offset 0 on unknown types

**Severity:** HIGH — silent wrong field access despite diagnostic
**Status:** PARTIALLY FIXED — diagnostic added, execution continues
**File:** `Codex.Codex/Emit/X86_64.codex:1947-1961`

The function now calls `resolve-constructed-ty` and records an error via `st-add-error`
when the resolved type isn't `RecordTy`. But the fallback arm still sets `field-idx = 0`
and emission continues, producing wrong code alongside the diagnostic.

**Fix:** Halt emission (or emit a trap) when the resolved type is not `RecordTy`.

---

## H-005: `ErrorTy` as silent failure propagation

**Severity:** HIGH — type errors become wrong code
**Status:** STILL PRESENT
**Files:** `Codex.Codex/IR/LoweringTypes.codex:13,21,25`, `Codex.Codex/IR/Lowering.codex:37,47,58,82`, `Codex.Codex/Emit/X86_64.codex:519+`

Every type lookup failure still returns `ErrorTy`. It flows through lowering and reaches
the emitter, where default `_ ->` arms treat it as a valid type and produce wrong code.
No pipeline-boundary check halts on ErrorTy.

**Fix:** ErrorTy must be checked at a pipeline boundary (end of lowering or start of
emission). Any ErrorTy reaching the emitter should halt compilation.

---

## H-006: `ConstructedTy` as unresolved type wrapper

**Severity:** HIGH — requires every consumer to resolve
**Status:** PARTIALLY FIXED — `emit-field-access` resolves, but not centralized
**Files:** `Codex.Codex/Types/TypeChecker.codex` (lookup-type-def), `Codex.Codex/IR/LoweringTypes.codex`, `Codex.Codex/Emit/X86_64.codex:1949`

Call-site resolution has been added in `emit-field-access`. But ConstructedTy is still
produced by `lookup-type-def` and not resolved during lowering — every new consumer
still has to remember to call `resolve-constructed-ty`. This is the systemic fragility
the audit named; only one site has been patched.

**Fix:** Resolve ConstructedTy during lowering so emitters never see it.

---

## H-007: Flat rename map — last citation wins

**Severity:** MEDIUM — silent wrong function call
**Status:** STILL PRESENT
**File:** `Codex.Codex/Semantics/ChapterScoper.codex:477-488` (apply-cite-selected)

`remove-rename` + `list-snoc` still overwrite prior cites of the same name without
warning. Two chapters citing the same function name silently collapse to the last one.

**Fix:** Short term: warn on duplicate cite names. Long term: qualified names.

---

## H-008: R8/R9 toggle assumes max 2 concurrent spilled loads

**Severity:** HIGH — silent register clobbering
**Status:** STILL PRESENT
**File:** `Codex.Codex/Emit/X86_64.codex:266-278` (load-local)

`if int-mod (st.load-local-toggle) 2 == 0 then reg-r8 else reg-r9` — still a mod-2
toggle with no bounds check. Three simultaneous spilled loads silently clobber.

**Fix:** Expand scratch pool, push/pop instead of toggle, or statically verify
max-2 simultaneous spills.

---

## H-009: `slug-matches-cite` uses fuzzy matching

**Severity:** LOW — ambiguous chapter resolution
**Status:** STILL PRESENT
**File:** `Codex.Codex/Semantics/ChapterScoper.codex:463-465`

`strip-hyphens (slugify slug) == strip-hyphens (slugify cite-name)` — "Type-Checker",
"TypeChecker", "Type Checker" all match the same chapter.

**Fix:** Exact match after a single canonical normalization, or assign unique IDs.

---

## Resolution Priority

1. **H-002** — Check for `-1` at the call-patch site.
2. **H-005** + **H-006** — Fix together: resolve ConstructedTy in lowering; halt on ErrorTy at pipeline boundaries.
3. **H-003** — Halt on non-RecordTy in `emit-field-access`.
4. **H-008** — Audit spill paths; expand pool or use push/pop.
5. **H-007** — Warn on duplicate cites (module system is the long-term fix).
6. **H-009** — Low priority; current naming avoids ambiguity.
