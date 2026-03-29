# 03 — Type System

## Overview

The type system is the heart of Codex. Every design decision in the language traces back to what the type system can express. This document defines the type system in enough detail to implement it.

We are building a type system that combines:
- **Hindley-Milner inference** (for the common case — you don't annotate everything)
- **Bidirectional type checking** (for dependent types — some annotations are required)
- **Linear type checking** (for resource safety — tracked via a separate pass or integrated)
- **Effect row typing** (for purity tracking — effects are part of function types)
- **Dependent types** (for precision — types can depend on values)

This is not a toy. This is approximately the type system of Idris 2, with linear types from Linear Haskell, effect rows from Koka/Eff, and a novel prose integration layer on top.

---

## Type Universe

### Base Types

```
Type₀ : Type₁ : Type₂ : ...

Text     : Type₀
Number   : Type₀
Integer  : Type₀
Boolean  : Type₀
Nothing  : Type₀    -- unit type, one inhabitant: ()
Void     : Type₀    -- empty type, no inhabitants
```

In practice, universe levels are inferred. The user writes `Type` and the compiler figures out the level. Universe polymorphism is supported but rarely needed.

### Function Types

```
(a : A) → B           -- dependent function (B may mention a)
A → B                  -- non-dependent function (sugar for (a : A) → B where B doesn't use a)
(a : A) → [E] B       -- effectful dependent function with effect row E
A → [E] B             -- effectful non-dependent function
```

### Quantification

```
∀ (a : Type) → List a → List a           -- explicit universal
{a : Type} → List a → List a             -- implicit universal (inferred)
∃ (n : Integer) → Vector n Boolean        -- existential
```

Implicit arguments are enclosed in `{ }` and are solved by unification. The user almost never writes them.

---

## Algebraic Types

### Sum Types

```
Result (a) =
  | Success (a)
  | Failure Text

Maybe (a) =
  | Just (a)
  | None

List (a) =
  | Nil
  | Cons (head : a) (tail : List a)
```

Sum types are tagged unions. Each constructor carries a payload. Pattern matching must be exhaustive.

### Record Types

```
Person = record {
  name  : Text
  age   : Integer
  email : Text
}
```

Records are product types with named fields. Field access is `person.name`. Records support:
- **Spread/update**: `person with { age = person.age + 1 }`
- **Destructuring**: `let { name, age, email } = person`
- **Row polymorphism** (stretch goal): functions that work on any record with at least the specified fields

### Newtypes

```
Amount = newtype Number
  where value ≥ 0
```

A newtype wraps an existing type with a new identity and optional constraints. `Amount` is not `Number` — you cannot pass an `Amount` where a `Number` is expected without explicit conversion. The `where` clause adds a runtime-checked (or proof-discharged) constraint.

---

## Dependent Types

### The Core Idea

Types can contain values. Values can appear in types. The type checker evaluates expressions at the type level.

```
Vector : (n : Integer) → Type → Type

empty  : Vector 0 a
single : a → Vector 1 a
append : Vector m a → Vector n a → Vector (m + n) a
index  : (i : Integer) → Vector n a → {proof : i < n} → a
```

The type of `append`'s result contains `m + n` — an arithmetic expression over values. The type checker must evaluate this expression during compilation.

### Type-Level Computation

The type checker includes a normalizer that can evaluate:
- Arithmetic: `+`, `-`, `*`, `/`, `^` on `Integer` and `Number`
- Boolean logic: `&&`, `||`, `not`
- Comparison: `<`, `>`, `≤`, `≥`, `≡`, `≠`
- List operations: `length`, `++`, `map` (when arguments are known)
- Conditional: `if ... then ... else ...` at the type level
- User-defined functions marked `@total` (guaranteed to terminate)

The normalizer runs during type checking. It must terminate. Functions used at the type level must be provably total (no infinite loops).

### Proof Obligations

Dependent types generate **proof obligations** — things the programmer must prove for the program to type-check.

```
index (i : Integer) (v : Vector n a) {i < n} → a
```

When you call `index 3 my-vector`, the type checker needs a proof that `3 < length my-vector`. This proof can come from:

1. **Literal evidence**: if `my-vector` has known length 5, then `3 < 5` is trivially true
2. **Context**: if a prior pattern match established `length my-vector > 3`
3. **Explicit proof**: the programmer provides a proof term
4. **Automatic search**: the compiler searches for a proof (within limits)

---

## Linear Types

### Motivation

Linear types track resource usage. A linear value must be used **exactly once**: not zero times (leak), not two or more times (double-use).

### Usage Annotations

Every binding has a **usage annotation** (usually inferred):

| Annotation | Meaning |
|------------|---------|
| `1` (linear) | Must be used exactly once |
| `ω` (unrestricted) | May be used any number of times |
| `0` (erased) | Used only at the type level, erased at runtime |

```
FileHandle : Linear Type    -- shorthand for usage = 1

open-file  : Path → [FileSystem] FileHandle
read-all   : FileHandle → [FileSystem] (Text, FileHandle)  -- returns the handle back
close-file : FileHandle → [FileSystem] Nothing              -- consumes the handle
```

Note: `read-all` returns the handle as part of its result — this is the standard linear pattern. You "thread" the resource through operations.

### Linearity Checking

The linearity checker tracks, for every variable in scope, how many times it has been used. At the end of each scope:
- Linear variables must have been used exactly once
- Unrestricted variables can have any count
- Erased variables must have count zero (used only in type positions)

Branching: in a pattern match, every branch must use linear variables the same number of times (or the variable must be used before the branch).

### Integration with Effects

Linear types and effects are complementary:
- Effects tell you *what kind* of side effect a function has
- Linear types tell you that *resources are properly managed*

A function with signature `FileHandle → [FileSystem] Text` reads from a file (effect) and does not return the handle (consumes it — linearity).

---

## Effect System

### Effect Rows

An effect row is a set of effect labels:

```
[FileSystem]                    -- single effect
[FileSystem, Network]           -- multiple effects
[]                              -- pure (no effects) — written by omitting the brackets
[FileSystem, State Counter]     -- effects can be parameterized
```

### Effect Polymorphism

Functions can be polymorphic over effects:

```
map : (a → [e] b) → List a → [e] List b
```

`map` propagates whatever effects the mapped function has. If you pass a pure function, `map` is pure. If you pass an effectful function, `map` has those effects.

### Built-in Effects

| Effect | Description |
|--------|-------------|
| `FileSystem` | Read/write files |
| `Network` | Network communication |
| `State (s)` | Mutable state of type `s` |
| `Random` | Non-deterministic random values |
| `Time` | Read current time |
| `Console` | Read/write console |
| `Diverge` | May not terminate |
| `Unsafe` | Platform-specific behavior, no guarantees |
| `IO` | Alias for all external effects |

### Effect Handlers

Effects are interpreted by handlers. A handler eliminates an effect from the row:

```
run-state : s → (Unit → [State s, e] a) → [e] (a, s)
```

`run-state` takes an initial state and a computation that uses `State s` (plus other effects `e`), and returns the result plus final state, with `State s` removed from the effect row.

This is the algebraic effects model from Koka/Eff/Multicore OCaml. It subsumes monads, monad transformers, and free monads.

---

## Type Inference

### Bidirectional Type Checking

We use bidirectional type checking, which means:

- **Check mode**: we know the expected type and check the expression against it
- **Infer mode**: we don't know the type and infer it from the expression

Rules:
- Literals infer their types: `42` infers `Integer`, `"hello"` infers `Text`
- Variables infer based on their binding
- Function application: if the function's type is known, check arguments; if not, infer arguments and solve
- Lambdas: check mode required (we need to know the parameter types) unless annotated
- Let bindings: infer the right-hand side, then check the body
- Type annotations: switch to check mode

### Unification

Type inference generates constraints (e.g., `?a ~ Integer`, `?b ~ List ?a`). Unification solves these constraints. Standard first-order unification with:
- Occurs check (prevents infinite types)
- Constraint ordering (dependent types may require solving in a specific order)
- Deferred constraints (when not enough info is available yet)

### Elaboration

The output of type checking is an **elaborated AST** where:
- All implicit arguments are filled in
- All types are explicit
- All proof obligations are either discharged or recorded as holes
- All usage annotations are computed

---

## Subtyping

Codex has **structural subtyping** for records (stretch goal) and **effect subtyping** for effect rows:

- A function with effects `[FileSystem]` can be used where `[FileSystem, Network]` is expected (adding effects is safe)
- A pure function can be used where any effect row is expected

There is NO subtyping between named types. `Amount` is not a subtype of `Number` even if it wraps one.

---

## Implementation Strategy

### Phase 1: Simple Types
- Primitives, functions, records, sums
- Hindley-Milner inference
- No dependent types, no linear types, no effects
- This gets us a working language fast

### Phase 2: Effects
- Add effect rows to function types
- Effect inference
- Basic built-in effects
- Effect handlers

### Phase 3: Linear Types
- Add linearity annotations
- Linearity checker
- FileHandle and similar resource types

### Phase 4: Dependent Types
- Value-dependent types
- Type-level computation / normalizer
- Proof obligations
- Basic proof terms

### Phase 5: Full Integration
- All features working together
- Dependent types with effects
- Linear dependent types
- The full type system as described in this document

Each phase produces a usable language. Phase 1 alone is roughly ML or a simple Haskell. Phase 2 adds Koka-like effects. Phase 3 adds Rust-like resource safety (at the type level, not ownership). Phase 4 adds Idris-like precision. Phase 5 is the full vision.

---

## Open Questions

1. **Instance resolution** — Type classes need instance resolution. Do we use Haskell-style global instances, Scala-style implicit search, Rust-style explicit impl blocks, or something new? This affects the entire feel of ad-hoc polymorphism.

2. **Totality checking** — How aggressive? Idris requires totality for everything by default. We probably want totality for type-level functions and proofs, with general recursion (via `[Diverge]`) for runtime code.

3. **Type-level computation limits** — The normalizer must terminate. How do we bound it? Fuel-based? (Give it N reduction steps and give up?) Or require all type-level functions to be structurally recursive?

4. **Higher-kinded types** — Do we support `Functor`, `Monad`, etc. as type classes over type constructors? Almost certainly yes, but the implementation is non-trivial with dependent types.

5. **Row polymorphism for records** — Desired for expressiveness but complex to implement with dependent types. May be a Phase 5+ feature.
