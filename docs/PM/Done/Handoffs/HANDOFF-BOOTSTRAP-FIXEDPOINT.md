**Goal:** Achieve bootstrap fixed-point — Stage 1 (compiled Codex compiler)
produces identical output to Stage 0 (C# compiler) when given the same
Codex source. This is the last unchecked item in Milestone 13.

---

## The One Sentence Problem

Stage 0 and Stage 1 now both produce correctly-typed C# output, but
nobody has run Stage 1 on the full Codex source to produce Stage 2
output and diff'd it against Stage 0's output. There will be cosmetic
differences (e.g., curried vs multi-arg calls, extra `_loop` helpers)
that must be catalogued, understood, and either accepted or fixed.

---

## Context from Previous Session

The typed-lowering handoff (`HANDOFF-TYPED-LOWERING.md`) is complete.
`Codex.Codex/out/Codex.Codex.cs` now has concrete types everywhere —
`string`, `long`, `List<Token>`, `Func<…>` instead of `object`.
Only 5 `object` references remain (all correct).

---

## Files You Must Read (in this order)

| # | File | Why |
|---|------|-----|
| 1 | `codex-src/output.cs` | The OLD Stage 1 output. `lower_module(AModule m)` with no type args. Must be replaced with `Codex.Codex/out/Codex.Codex.cs`. |
| 2 | `Codex.Codex/out/Codex.Codex.cs` | The NEW Stage 0 output (with typed lowering). This is what Stage 1 should now be. |
| 3 | `docs/M13-BOOTSTRAP-PLAN.md` lines 160–200 | Lists known cosmetic differences between Stage 0 and Stage 1 output (curried calls, `_loop` helpers). |
| 4 | `docs/08-MILESTONES.md` Milestone 13 section | The unchecked item: "Stage 1 output = Stage 2 output (full bootstrap fixed-point verification)" |
| 5 | `docs/FORWARD-PLAN.md` "What's Next" section | Priority list after fixed-point. |

---

## Files You Do NOT Need to Read

- `Codex.Codex/IR/Lowering.codex` — already fixed, don't touch
- `Codex.Codex/Types/TypeChecker.codex` — working, don't touch
- `Codex.Codex/Types/TypeEnv.codex` — already has all builtins
- `Codex.Codex/main.codex` — already wired, don't touch
- `src/Codex.IR/Lowering.cs` — Stage 0 reference, read-only
- Any emitter other than `Codex.Emit.CSharp`
- `src/Codex.Lsp/`, `src/Codex.Proofs/`, `src/Codex.Repository/`
- `docs/00-OVERVIEW.md` through `docs/10-PRINCIPLES.md`

```
dotnet run --project tools/Codex.Bootstrap -- build Codex.Codex
```

This produces `Codex.Codex/stage1-output.cs` — the Stage 2 output.

### Step 3: Diff Stage 0 vs Stage 2

Compare `Codex.Codex/out/Codex.Codex.cs` (Stage 0 output) against
`Codex.Codex/stage1-output.cs` (Stage 2 output).

**Expected differences (from M13-BOOTSTRAP-PLAN.md):**
- Curried calls: Stage 1 emits `f(a)(b)`, Stage 0 emits `f(a, b)` — Stage 1 is Codex-native curried application
- Extra `_loop` helpers: Stage 0 inlines some recursion as lambdas, Stage 1 may emit named helpers
- Whitespace/formatting differences

**If the diff is zero or only cosmetic:** Fixed-point achieved. Check
off the M13 item. Update `08-MILESTONES.md` and `FORWARD-PLAN.md`.

**If there are semantic differences:** Each one is a bug in either
Stage 0 or Stage 1 that must be diagnosed and fixed. The most likely
sources:
- Type resolution differences (Stage 0 has `ConstructorMap`/`TypeDefMap`
  that Stage 1 doesn't use — sum type and record types may emit as
  `object` instead of their named types)
- Missing type information for `when`/match expressions
- Lambda parameter types not flowing through

### Step 4: Update docs

- Check off `Stage 1 output = Stage 2 output` in `08-MILESTONES.md` M13
- Update `FORWARD-PLAN.md` status
- Update M13-BOOTSTRAP-PLAN.md with final parity results

---

## Known Risks

2. **Stack overflow** — The Bootstrap project already allocates a 256 MB
   stack (`new Thread(() => ..., 256 * 1024 * 1024)`). If Stage 1 with
   typed lowering is more recursive, it might still overflow. Monitor.

3. **`when` expressions emitting `object` casts** — The Codex `when`
   keyword compiles to C# `switch` expressions. When branches return
   different types (e.g., `CodexType` subtypes), C# infers `object`.
   This is a known Codex→C# limitation, not a bug in typed lowering.

---

## How to Verify

1. `dotnet build Codex.sln` — zero warnings
2. `dotnet test Codex.sln` — all tests pass
3. Diff `Codex.Codex/out/Codex.Codex.cs` vs `Codex.Codex/stage1-output.cs` — differences are cosmetic only

---

## What Success Looks Like

The M13 milestone is fully checked off. The Codex compiler written in
Codex, when compiled and run, produces output functionally identical to
what the C# bootstrap compiler produces. This is the definition of a
self-hosting compiler reaching fixed-point.
