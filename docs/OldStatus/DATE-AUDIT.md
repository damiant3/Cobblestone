# Date Audit — Hallucinated Dates in Agent-Generated Files

**Audit date**: 2026-03-18 (verified via system clock)
**Auditor**: Claude (Opus 4.6, Linux/claude.ai)

---

## Background

The Codex project began on or around **2026-03-13**. All iterations (1 through 13+)
occurred within a ~5-day window from 2026-03-13 through 2026-03-18. Previous agents
hallucinated dates from training data, placing some iterations in June 2025 — nearly
9 months before the project existed.

This audit catalogs every incorrect date found in agent-generated documents and
recommends corrections.

---

## Incorrect Dates Found

### Iteration Handoff Documents

| File | Current Date | Problem | Suggested Fix |
|------|-------------|---------|---------------|
| `docs/OldStatus/ITERATION-3-HANDOFF.md` | `2025-06-21` | 9 months before project existed | `2026-03-13` (estimated — check git log for commit `05a40ea`) |
| `docs/OldStatus/ITERATION-4-HANDOFF.md` | `2025-06-22` | 9 months before project existed | `2026-03-13` (estimated — check git log for commit `4c4b52e`) |

### Correct (or Plausible) Dates

These dates fall within the actual project timeline and appear correct:

| File | Date | Status |
|------|------|--------|
| `docs/OldStatus/ITERATION-5-HANDOFF.md` | `2026-03-14 Pi Day` | ✅ Plausible |
| `docs/OldStatus/ITERATION-7-HANDOFF.md` | `2026-03-14 Pi Day` | ✅ Plausible (multiple iterations per day) |
| `docs/OldStatus/ITERATION-9-HANDOFF.md` | `2026-03-15` | ✅ Plausible |
| `docs/OldStatus/ITERATION-11-HANDOFF.md` | `2026-03-15` | ✅ Plausible |
| `Opus.md` | `March 2026` | ✅ Correct (intentionally vague) |

### Decision Log (`docs/OldStatus/DECISIONS.md`)

| Decision | Current Date | Problem | Suggested Fix |
|----------|-------------|---------|---------------|
| "Bootstrap Language is C#" | `2025-06-20` | Pre-dates project | `2026-03-14` ✅ Fixed |
| "Hand-Written Lexer and Parser" | `2025-06-20` | Pre-dates project | `2026-03-14` ✅ Fixed |
| "Solution Structure" | `2025-06-20` | Pre-dates project | `2026-03-14` ✅ Fixed |
| "IR is Not A-Normal Form" | `2025-07 (M3)` | Pre-dates project | `2026-03-14` ✅ Fixed |
| "Curried Application in IR" | `2025-07 (M3)` | Pre-dates project | `2026-03-14` ✅ Fixed |
| "No Separate Runtime Library" | `2025-08 (M3–M5)` | Pre-dates project | `2026-03-14` ✅ Fixed |
| "Direct I/O for Effects" | `2025-09 (M5)` | Pre-dates project | `2026-03-14` ✅ Fixed |
| "long/double Instead of BigInteger" | `2025-08 (M3)` | Pre-dates project | `2026-03-14` ✅ Fixed |
| "Parser Error Recovery" | `2026-06` | Future date (hallucinated) | `2026-03-15` ✅ Fixed |
| "Codex-Side Emitter Generics" | `2026-06` | Future date (hallucinated) | `2026-03-16` ✅ Fixed |
| "Column-Based `when` Branch Scoping" | `2026-06` | Future date (hallucinated) | `2026-03-16` ✅ Fixed |
| Decisions marked `2026-03` | Various | ✅ Correct | No change needed |

### Missing Handoff Documents

The following iterations are referenced in git history but no handoff document was
found in the project knowledge. They may exist on disk or may not have been created:

- Iteration 1 (initial foundation)
- Iteration 2 (type checking)
- Iteration 6 (effects + linear types)
- Iteration 8 (LSP + editor)
- Iteration 10 (proofs)
- Iteration 12 (additional backends)
- Iteration 13 (self-hosting / bootstrap)

---

## How to Fix

### Option A: Correct in Place (Recommended)

For each wrong date, look up the actual commit timestamp from git:

```bash
# Find the real date for Iteration 3's commit
git log --format="%ai" 05a40ea -1

# Find the real date for Iteration 4's commit
git log --format="%ai" 4c4b52e -1
```

Then update the handoff documents with the real dates.

### Option B: Add Correction Notes

If preserving the original text is preferred, add a correction note at the top:

```markdown
> **Date correction**: The original date `2025-06-21` was hallucinated by the agent.
> The actual date of this work was `2026-03-13` based on git commit timestamps.
```

### For DECISIONS.md

The decision dates that say `2025-08` or `2025-09` should be updated to their actual
dates. The milestone numbers (M3, M5) are correct — only the year/month is wrong.

---

## Prevention

The `checkdate()` rule in `.github/agent-rules/00-META.md` prevents this from
recurring. All agents must query the system clock before writing dates.

---

## Git Log Reference

Relevant commits (from `git log --oneline`):

```
84826be Iteration 9, M8 - Dependant Types (basic)
5aeb162 vscode syntax editor
466029b Iteration 8 Handoff date.  Also, VSCODE-Setup, and clarify copilot.
b21ee67 Resolve conflicting rules and apply.  Iteration 8 Handoff updates.
7aed4a5 No dictionaries anymore, Lsp
962f5c1 Interation 8 Phase 1
f31835c Handoffs 5 6 7
e024e68 Cleanup.
d31cc65 Iteration 7 Handoff
f90dae2 iteration 7
cdfbb9e iteration 6
ac8ce55 Add effectful types and do-expressions to Codex
1f5e1c7 docs: Iteration 5 handoff summary
e403956 feat: Iteration 5 — type params, exhaustiveness checking
3d21ed2 Add user-defined record and sum types to Codex
4c4b52e iteration 4
f06c3dc Remove all XML doc comments; update code style rules
d4d479c feat: add prose-mode document support to Codex
81655bf Iteration 3 handoff
05a40ea feat: Milestone 3 — IR, C# emitter, codex build/run commands
27e4a99 Bootstrap Codex language: core, syntax, CLI, tests
9e9c7d1 Add Codex project engineering and planning docs
ef62c5c rules and configs and a vision
cf22328 Add project files.
d6452cf Add .gitattributes, .gitignore, and README.md.
```

The actual git timestamps on these commits are the ground truth for when work happened.

---

## Fixes Applied

**Date**: 2026-03-18
**Agent**: Copilot (VS 2022, Windows)
**Branch**: `windows/review-and-dates`

All dates listed above have been corrected using Option B (correction notes at top
of each file + in-place date updates). Additional `2026-06` hallucinations were
discovered in DECISIONS.md that the original audit missed — these have also been
fixed. See the expanded table in the "Decision Log" section above.
