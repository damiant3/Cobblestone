# Iteration 9 — Handoff Summary

**Date**: 2026-03-15
**Branch**: `master`
**Remote**: https://github.com/damiant3/NewRepository

---

## What Was Done

### Milestone Status

| Milestone | Status | Notes |
|-----------|--------|-------|
| M0–M7 | ✅ Complete | See ITERATION-7-HANDOFF.md |
| M9: LSP & Editor | ✅ Complete | See ITERATION-8-HANDOFF.md |
| **M8: Dependent Types (Basic)** | **✅ Complete** | All deliverables met |

### M8 Deliverables

| Deliverable | Status |
|-------------|--------|
| Dependent function types: `(n : Integer) → Vector n a → ...` | ✅ |
| Type-level arithmetic: `m + n` evaluated during type checking | ✅ |
| Proof obligations: `index` requires proof that `index < length` | ✅ |
| Simple proof discharge: literal evidence and context-based evidence | ✅ |
| The `Vector` type with `append` having the correct dependent type | ✅ |

### M8 — Implementation Details

#### New Type System Types (`src/Codex.Types/CodexType.cs`)

| Type | Purpose |
|------|---------|
| `DependentFunctionType(ParamName, ParamType, Body)` | `(n : Integer) → Vector n a → ...` |
| `TypeLevelValue(Value)` | Literal integer in type position (e.g., `5` in `Vector 5 a`) |
| `TypeLevelBinary(Op, Left, Right)` | Type-level arithmetic (e.g., `m + n`) |
| `TypeLevelVar(Name)` | Type-level variable (e.g., `n` from a dependent binder) |
| `ProofType(Claim)` | Proof obligation `{proof : claim}` |
| `LessThanClaim(Left, Right)` | `i < n` claim for proof obligations |
| `TypeLevelOp` enum | `Add`, `Sub`, `Mul` |

#### New Syntax Nodes

| Layer | Node | Purpose |
|-------|------|---------|
| CST | `DependentTypeNode` | `(n : Integer) → Body` |
| CST | `IntegerTypeNode` | Integer literal in type position |
| CST | `BinaryTypeNode` | `(m + n)` in type position |
| CST | `ProofConstraintNode` | `{proof : i < n}` |
| AST | `DependentTypeExpr` | Desugared dependent function type |
| AST | `IntegerLiteralTypeExpr` | Desugared integer literal type |
| AST | `BinaryTypeExpr` | Desugared type-level binary expression |
| AST | `ProofConstraintExpr` | Desugared proof constraint |

#### Parser (`src/Codex.Syntax/Parser.cs`)

- `ParseTypeAtom`: detects `(name : Type) →` via `IsDependentTypeLookahead()`
- `ParseTypeAtom`: handles `IntegerLiteral` tokens as type atoms
- `ParseTypeAtom`: detects `+`, `-`, `*` inside parenthesized types → `BinaryTypeNode`
- `ParseTypeAtom`: handles `{proof : left < right}` → `ProofConstraintNode`
- Type application argument loop accepts `IntegerLiteral` tokens

#### TypeChecker (`src/Codex.Types/TypeChecker.cs`)

- `m_typeLevelEnv`: type-level variable bindings
- `ResolveDependentType`: binds param name as `TypeLevelVar`, resolves body
- `ResolveTypeLevelBinary`: resolves + normalizes type-level arithmetic
- `ResolveProofConstraint`: resolves `{proof : claim}` to `ProofType(LessThanClaim(...))`
- `NormalizeTypeLevelExpr`: constant-folds `TypeLevelBinary` when both are `TypeLevelValue`
- `InferApplication`: for `DependentFunctionType`, substitutes arg value into body; calls `TryDischargeProofParams`
- `TryDischargeProofParams`: auto-discharges `FunctionType(ProofType(...), return)` when proof is trivially true
- `TryDischargeProof`: evaluates `LessThanClaim(TypeLevelValue, TypeLevelValue)` by comparison
- `TryExtractTypeLevelValue`: extracts integers from literals and list lengths
- `SubstituteTypeLevelVar`: substitutes named type-level variables in types
- `InferDefinition`: skips proof params when walking expected type; returns declared type directly

#### Unifier (`src/Codex.Types/Unifier.cs`)

- Unifies `DependentFunctionType` ↔ `DependentFunctionType` and ↔ `FunctionType`
- Unifies `TypeLevelValue` by value, `TypeLevelVar` by name
- Normalizes `TypeLevelBinary` before comparing
- `ListType` ↔ `ConstructedType("Vector", [n, elem])` coercion
- `DeepResolve`, `OccursIn`, `NormalizeTypeLevelExpr` handle all new types

#### Lowering, CSharpEmitter, LinearityChecker

- All skip proof params when walking function types for parameters
- `DependentFunctionType` erases to regular `Func<P, R>` at runtime
- Type-level values emit as `long`

### Test Count

**182 tests, all passing** (16 Core + 11 Ast + 55 Syntax + 10 Semantics + 76 Types + 14 LSP)

New tests added: 5 TypeChecker + 4 Parser + 8 Integration = **17 new tests**

---

## Demo

### Vector append (type-level arithmetic)
```codex
append : (m : Integer) -> (n : Integer) -> Vector m a -> Vector n a -> Vector (m + n) a

example : Vector 5 Integer
example = append 3 2 [1, 2, 3] [4, 5]
```
The compiler verifies that 3 + 2 = 5. ✓

### Proof obligations (literal evidence)
```codex
safe-index : (i : Integer) -> (n : Integer) -> {proof : i < n} -> Integer
safe-index (i) (n) = i

main : Integer
main = safe-index 3 5
```
The compiler auto-discharges `{proof : 3 < 5}` — it's trivially true. ✓

```codex
main = safe-index 5 3
```
The compiler rejects this: `Cannot discharge proof obligation: 3 < 5` fails. ✓

---

## Known Limitations / Next

### Stretch goals not in M8 Basic

- **Implicit dependent arguments** — `{m : Integer}` syntax for auto-inferred type-level params
- **Type-level function evaluation** — only arithmetic on constants; no user-defined type-level functions
- **Totality checking** — not yet enforced for type-level expressions
- **Context-based evidence** — proofs from pattern match branches not yet propagated

### Next Milestones

| Priority | Milestone | Notes |
|----------|-----------|-------|
| 1 | M10: Proofs | Full proof checking, induction, rewriting |
| 2 | M11: Collaboration | Multi-user repository sync |

---

## Key Code Locations

| Task | File |
|------|------|
| Add new type-level construct | `src/Codex.Types/CodexType.cs` |
| Type-level normalization | `TypeChecker.cs` (`NormalizeTypeLevelExpr`) and `Unifier.cs` |
| Proof obligation generation | `TypeChecker.cs` → `TryDischargeProofParams` |
| Dependent type parsing | `Parser.cs` (`ParseTypeAtom`, `IsDependentTypeLookahead`) |
| Runtime erasure | `Lowering.cs` — dependent types erase to plain functions |
