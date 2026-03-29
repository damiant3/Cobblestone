# Windows Agent Review — Handoff Summary

**Date**: 2026-03-18 (verified via `Get-Date`)
**Agent**: Copilot (VS 2022, Windows)
**Branch**: `windows/review-and-dates`
**Remote**: https://github.com/damiant3/NewRepository
**Commit**: `bfdc6ad`

---

## What Was Done

### Task 1: Review of Linux Agent's Rule Decomposition

All 9 files in `.github/agent-rules/` were read and reviewed. **Overall: excellent work.**

Findings:
- **No important content was lost** in the decomposition from monolithic instructions.
- The `checkdate()` rule is the single most impactful addition.
- The dual-agent review workflow is clear and workable.
- Minor issues found and fixed (see below).
- Full review documented in `09-WINDOWS-NOTES.md`.

### Task 2: Windows Agent Notes (`09-WINDOWS-NOTES.md`)

Created `.github/agent-rules/09-WINDOWS-NOTES.md` documenting:
- Review findings for each of the 9 rule files
- `get_file` first-line-drop bug (tool frequently omits line 1 of files)
- `edit_file` reliability guidance (when it works well vs. when it struggles)
- Terminal output truncation and encoding quirks
- PowerShell pitfalls (semicolons, `Select-String` vs `grep`, path separators)
- Correction: `pwsh` → `powershell` for Windows PowerShell 5.1 default

### Task 3: Date Fixes

Fixed all hallucinated dates using git commit timestamps:

| File | Original | Corrected | Source |
|------|----------|-----------|--------|
| `ITERATION-3-HANDOFF.md` | `2025-06-21` | `2026-03-14` | `git log 05a40ea` |
| `ITERATION-4-HANDOFF.md` | `2025-06-22` | `2026-03-14` | `git log 4c4b52e` |
| `DECISIONS.md` (8 entries) | `2025-06-20`, `2025-07`, `2025-08`, `2025-09` | `2026-03-14` | First commit: `2026-03-14 05:54:59` |
| `DECISIONS.md` (3 entries) | `2026-06` | `2026-03-15`, `2026-03-16` | Feature commits |

**Additional finding**: The Linux agent's DATE-AUDIT.md missed three `2026-06` dates
in DECISIONS.md (future hallucinations). These were also corrected. The audit file
has been updated with the full list.

### Minor Fixes

- `02-TERMINAL.md`: `pwsh -File` → `powershell -File` (PS 5.1 is default in VS)
- `05-BUILD-VERIFY.md`: test count `654+` → `722+` (actual current count)
- `README.md`: added `09-WINDOWS-NOTES.md` to the file index

### Task 4: Commit and Push

- Branch `windows/review-and-dates` created, committed, pushed.
- 8 files changed, 248 insertions, 21 deletions.

---

## For the Linux Agent (Review Checklist)

When you review this branch:

1. **Verify the date corrections** — run `git log --format="%ai" 05a40ea -1` and
   `git log --format="%ai" 4c4b52e -1` to confirm the timestamps match.
2. **Read `09-WINDOWS-NOTES.md`** — check if the tool quirks I documented match
   your understanding. The `get_file` first-line-drop bug is particularly important.
3. **Check the `2026-06` date fixes** in DECISIONS.md — I found 3 additional
   hallucinated dates your audit didn't catch (Parser Error Recovery, Emitter
   Generics, Column-Based `when` Branch Scoping).
4. **The `pwsh` → `powershell` change in `02-TERMINAL.md`** — verify this is
   acceptable or if you'd prefer to keep `pwsh` with a note.

---

## Build Status

- `dotnet build Codex.sln`: 1 error (pre-existing CDX3002 in `Codex.Codex` — same on master)
- `dotnet test Codex.sln`: PASS — 722 tests (16+23+88+11+15+18+551)

The `Codex.Codex` CDX3002 error (`extract-ctor-type-args` undefined) exists on master
and is unrelated to this branch's changes.

---

## Known Issue: get_file Tool Bug

During this session, the `get_file` tool repeatedly showed line 1 of
`ITERATION-3-HANDOFF.md` as blank, when the terminal confirmed the heading
`# Iteration 3 — Handoff Summary` was present. This caused an initial edit to
appear to drop the heading. The workaround is documented in `09-WINDOWS-NOTES.md`:
always verify line 1 via `Get-Content -TotalCount 5` when it matters.
