# Code Review Nits — master @ 2109fe7

**Reviewer**: linux agent (claude.ai sandbox)
**Date**: 2026-03-18
**Scope**: Hot-path compiler files on master

These are minor issues — nothing blocking. Fix at convenience.

---

## 1. Builtin name lists are duplicated and must be kept in sync

`NameResolver.s_builtins` (hardcoded set) and `TypeEnvironment.WithBuiltins()` (binding loop)
define the same names independently. Adding a new builtin requires updating both, and there is
no compile-time check that they agree.

**Suggestion**: Have `TypeEnvironment` expose its builtin names as a static set, and have
`NameResolver` consume it instead of maintaining its own copy.

**Files**: `src/Codex.Semantics/NameResolver.cs:22`, `src/Codex.Types/TypeEnvironment.cs:30`

---

## 2. Magic type variable IDs in WithBuiltins

`TypeEnvironment.WithBuiltins()` uses hardcoded IDs (0, 100, 101, 102, 200, 201, 202) for
type variables in builtin signatures. The `Unifier.FreshVar()` counter starts at some value
and increments — if it ever reaches these IDs, unification will silently confuse builtin
type parameters with user-inferred variables.

**Suggestion**: Reserve a negative range or a high-offset range for builtins, or allocate
them through the unifier with an explicit "builtin reservation" phase.

**Files**: `src/Codex.Types/TypeEnvironment.cs`

---

## 3. CSharpEmitter is not reuse-safe

`m_constructorNames`, `m_definitionArity`, and `m_matchCounter` are set inside `Emit()` but
`m_matchCounter` is never reset. If `Emit()` is called twice on the same instance, match
variable names will continue incrementing from the previous run. The other two fields are
reassigned so they're fine, but the inconsistency is a latent bug.

**Suggestion**: Reset `m_matchCounter = 0` at the top of `Emit()`, or make these locals
passed through the emit methods.

**Files**: `src/Codex.Emit.CSharp/CSharpEmitter.cs:12-13`

---

## 4. TypeChecker save/restore pattern is fragile

`CheckModule` saves and restores `m_typeParamEnv` around each definition. This manual
save/restore is easy to break if an early return or exception is added later. Same pattern
appears in `RegisterRecord` and `RegisterVariant`.

**Suggestion**: Consider a `using`-based scope guard, or a method that takes a lambda and
handles save/restore internally: `WithTypeParamScope(env => { ... })`.

**Files**: `src/Codex.Types/TypeChecker.cs:30-33`

---

## 5. `n` type parameter in Desugarer

`DesugarTypeDefinition` uses `List<n>` for type parameters — `n` is `Name` but the
single-letter type alias is jarring and inconsistent with the rest of the codebase which
spells out `Name`.

**Suggestion**: Spell out `Name` instead of the single-letter alias.

**Files**: `src/Codex.Ast/Desugarer.cs` (DesugarTypeDefinition method)

---

## 6. Lowering primary constructor field duplication

`Lowering` uses a primary constructor `(Map<string, CodexType> typeMap, ...)` but then
manually copies each parameter into an `m_` field. The primary constructor parameters
are still in scope and could be accidentally used instead of the `m_` fields. This is a
style nit but a real hazard — the C# compiler won't warn if you use `typeMap` instead
of `m_typeMap`.

**Suggestion**: Either drop the primary constructor and use a regular constructor, or
drop the `m_` fields and use the primary constructor parameters directly (accepting the
style deviation). Pick one pattern and be consistent.

**Files**: `src/Codex.IR/Lowering.cs:1-18`

---

## Summary

| # | Severity | Effort |
|---|----------|--------|
| 1 | Medium — correctness risk on next builtin addition | Small |
| 2 | Low — theoretical collision, hasn't happened yet | Small |
| 3 | Low — only matters if emitter is reused | Trivial |
| 4 | Low — fragility, not a current bug | Small-Medium |
| 5 | Trivial — readability | Trivial |
| 6 | Low — style/hazard | Small |

None of these are blocking. They should be addressed incrementally as the relevant files
are touched for other reasons.
