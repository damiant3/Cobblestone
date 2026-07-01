**Date**: 2026-03-22
**Branch**: `master`
**Commit**: `2f6392c` — fix: apply linux review — view name validation, existence guards, edge-case tests
**For**: Linux Agent (Phase 3 implementation)

---

## What's Done

### Phase 1: Named Views ✅

`src/Codex.Repository/FactStore.Views.cs` — all CRUD ops:
- `CreateView(name, copyFromCurrent)` — with `ValidateViewName` + canonical guard
- `ListViews()` — legacy bridge + `Map.Count`
- `SwitchView(name)` / `DeleteView(name)` / `GetCurrentViewName()`
- `GetNamedView(name)` / `UpdateNamedView(name, def, hash)` / `RemoveFromView(name, def)`
- `ViewExists(name)` — checks both `views/` dir and legacy `view.json`

Design decisions:
- Legacy `view.json` serves as canonical until explicitly migrated
- `ResolveViewFile` handles the two-path resolution
- All read/write methods guard on existence (the I/O APIs are forgiving — silent
  corruption is worse than an exception here)
- `ValidateViewName` trusts the non-nullable type contract, uses `Trim()` + length

27 tests in `ViewTests` (16 original + 11 edge cases from review).

### Phase 2: View Consistency ✅

`FactStore.CheckViewConsistency(viewName, IViewConsistencyChecker)`:
- Loads all facts from the named view
- Validates each is a Definition (not Trust, Supersession, etc.)
- Delegates to checker for semantic validation

`tools/Codex.Cli/ViewConsistencyChecker.cs` — full pipeline implementation:
- Parse (with prose detection) → Desugar → NameResolve → TypeCheck → LinearityCheck
- Combines all view definitions into a single `Module` for joint checking

5 tests in `ViewConsistencyTests`.

---

## What's Next: Phase 3 — View Composition

Three operations to add to `FactStore.Views.cs`:

### 1. Override: `base + override`

Create a new view from an existing one with specific definitions replaced.

```
OverrideView(baseViewName, targetViewName, overrides: ValueMap<string, ContentHash>)
```

- Creates `targetViewName` as a copy of `baseViewName`
- Applies all overrides (set operations)
- Target must not already exist; base must exist

### 2. Merge: `view-a ∪ view-b`

Combine two views. Fail on conflicting definitions (same name, different hash).

```
MergeViews(viewNameA, viewNameB, targetViewName)
```

- Target gets the union of both views
- If both define the same name with different hashes → error (return conflicts, don't throw)
- Same name + same hash → keep it (idempotent)

### 3. Filter: `view | filter`

Restrict a view to a subset of definition names.

```
FilterView(sourceViewName, targetViewName, keepNames: IReadOnlySet<string>)
```

- Target gets only entries whose keys are in `keepNames`

### Testing Strategy

Add to `ViewTests.cs`:
- Override: basic, override replaces existing, override adds new entry
- Merge: disjoint, overlapping-same-hash, conflicting-different-hash
- Filter: subset, empty filter, filter with names not in view (no-op)
- Edge cases: nonexistent source views, target already exists

### Key Files

| File | What |
|------|------|
| `src/Codex.Repository/FactStore.Views.cs` | Add the three methods here |
| `tests/Codex.Repository.Tests/ViewTests.cs` | Add Phase 3 tests here |
| `docs/CurrentPlan.md` | Update when done |

### Style Reminders

- `m_` prefix on private fields, PascalCase on methods/types
- No `var` when type isn't obvious from RHS
- No XML doc comments — code should be self-documenting
- Use `Map<K,V>` / `ValueMap<K,V>` from `Codex.Core`, not `ImmutableDictionary`
- Existence guards on source views (they'd silently return empty otherwise)
- `ValidateViewName` on any user-provided view name
- Run `dotnet build Codex.sln` + `dotnet test` before committing

---

## Process

Work on a branch (`linux/v1-views-phase3`), push for review. Don't merge to master
without Windows agent or user review.
