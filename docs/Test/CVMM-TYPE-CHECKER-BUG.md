# CVMM App Type Checker Bug — RESOLVED, DO NOT RE-INVESTIGATE

**Date**: 2026-06-22
**Status**: Resolved (CL 5734, copied up CL 5735)
**Agent**: reek
**Resolution**: Not a compiler bug. Build script was incomplete.

## The Trap (Read This First)

The CVMM app build has a TWO-STAGE pipeline:

1. `build.ps1` bundles app chapters + foreword/OS deps into one
   `.codex` file. It uses `ExcludeQuires @('Cvmm')` to skip
   intra-Cvmm cites (because Cvmm chapters are bundled explicitly).

2. `compile.ps1` reads that bundle, re-resolves ALL cites WITHOUT
   exclusions, and prepends any chapters the bundle missed.

If `build.ps1`'s chapter list is incomplete — i.e., a bundled
chapter cites another Cvmm chapter that isn't in `$AppChapters` —
then `compile.ps1` silently finds and prepends those missing
chapters (plus their transitive deps). The compiler sees a LARGER
source than what `build.ps1` produced.

**The investigation trap**: if you only inspect the bundled source
(`build-output/cvmm-source.codex`), you will see fewer chapters and
fewer lines than what the compiler actually receives. Type names,
line numbers, and error messages will reference code that doesn't
appear to exist — making it look like memory corruption, sort bugs,
or phantom types. It is none of those things.

**How to check**: run this in PowerShell from the repo root:

```powershell
. build\quire-map.ps1
$src = [System.IO.File]::ReadAllLines('apps\cvmm\build-output\cvmm-source.codex')
$seen = @{}; $src | Where-Object { $_ -match '^Chapter:\s*(\w+)--(.+)' } | ForEach-Object { $seen["$($matches[1])::$($matches[2])"] = $true }
$extra = Resolve-CiteOrder -RootLines $src -Repo '.' -SeedSeen $seen
Write-Host "$($extra.Count) extra chapter(s)"; $extra | ForEach-Object { Write-Host "  $($_.Quire)::$($_.Name)" }
```

If the count is > 0, the fix is adding those chapters to
`build.ps1`'s `$AppChapters` list (and fixing any type errors in
them). As of CL 5734, `compile.ps1` emits a WARNING when it finds
extra chapters, so this class of silent failure should not recur.

## What the Errors Were

The original 14 errors were genuine type errors in the 14 silently-
prepended chapters, not type checker corruption:

| Error | Cause | Fix |
|---|---|---|
| 10x `Rec:RowSchema vs List` | `ProductivityDb` called `row-new` without the required `RowSchema` first argument | Added `(table-schema X-table)` |
| `List vs Rec:WidgetNode` (x2) | `Calendar` wrapped `time-gutter` (`List WidgetNode`) in `[]` → `List (List WidgetNode)` | Wrapped in a column panel |
| `Sum:AppRole vs Sum:MemberRole` | `CvmmShell` called `role-label` on `MemberRole` but only the `AppRole` overload was in scope (name collision between `DefaultApps` and `GroupMembership`) | Renamed to `member-role-label` |
| `Con:None vs Nothing` | `CvmmServer` opening had type `[Console] None` instead of `[Console] Nothing` | Typo fix |

## Why the Sort/Bsearch Hypothesis Was Wrong

The original investigation hypothesised that `sort-by` / `bsearch-text-pos`
in the type environment produced wrong results at ~2929 bindings. This
was investigated thoroughly and found to be correct:

- `sort-by` (foreword/core/Sort.codex): Standard Lomuto quicksort with
  median-of-three. Algorithmically correct. `list-set-at` is in-place
  (no allocation per swap).
- `bsearch-text-pos` (compiler/Core/Collections.codex): Standard lower-
  bound binary search. Correct.
- `text-compare` / `__str_eq` (compiler/Emit/X86_64TextHelpers.codex):
  Byte-by-byte lexicographic comparison. Correct. Both use the same
  byte comparison, so sort order and equality checks are consistent.
- `env-lookup` / `env-lookup-in`: Check `b.name == name` after bsearch.
  A wrong-position result returns `ErrorTy`, not a phantom type.

The sort, search, and comparison code are all correct. The compiler
successfully self-compiles (28K lines, ~200 types, 543 tests) using
the same code paths.

## The Cyrillic Red Herring

CDX2051 warnings showed garbled text like `-х х0 ҿ҃Ҁѿ0҅.922...807`.
This is a SEPARATE, REAL bug in `__itoa` (integer-to-text): negating
INT64_MIN wraps back to INT64_MIN, producing negative digit
remainders → garbled CCE bytes. This is display-only and does not
affect compilation. See `KNOWN-CONDITIONS.md` for details.

This garbled output made the phantom-type hypothesis more plausible
(looked like memory corruption) but was unrelated to the actual
build-script discrepancy.

## Files Changed (CL 5734)

| File | Change |
|---|---|
| `build/compile.ps1` | Added warning when extra chapters found |
| `apps/cvmm/build.ps1` | Added 5 missing chapters to `$AppChapters` |
| `apps/cvmm/ProductivityDb.codex` | 10x `row-new` schema args |
| `apps/cvmm/Calendar.codex` | `time-gutter` panel wrap |
| `codex/os/net/GroupMembership.codex` | `role-label` → `member-role-label` |
| `apps/cvmm/CvmmShell.codex` | Updated `member-role-label` call |
| `apps/cvmm/CvmmServer.codex` | `None` → `Nothing` in type annotation |
| `docs/Test/KNOWN-CONDITIONS.md` | INT64_MIN itoa bug documented |
| `docs/Agents/reek-workplan.md` | Priority 1 marked resolved |
