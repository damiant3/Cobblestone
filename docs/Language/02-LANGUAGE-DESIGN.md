# 02 — Language Design

## Overview

Codex is a literate, statically-typed, purely functional programming language with dependent types, linear types, and algebraic effects. Its distinguishing feature is that prose and notation are co-equal parts of the source — the English text is load-bearing, not decorative.

This document defines the language design at the level needed to begin implementation. It is not a formal specification (that comes later, written in Codex itself). It is an engineering blueprint.

---

## Source Structure

A Codex source file is a **document**. A document contains **chapters**. Chapters contain **sections**. Sections contain **definitions**, **claims**, **proofs**, and **prose**.

```
Document
  └── Chapter
        ├── Prose (introductory text)
        ├── Section
        │     ├── Prose
        │     ├── Definition
        │     │     ├── Prose (explanation)
        │     │     ├── Type signature
        │     │     └── Implementation
        │     ├── Claim
        │     │     ├── Prose (statement)
        │     │     └── Formal statement
        │     └── Proof
        │           ├── Prose (explanation)
        │           └── Formal proof
        └── Section
              └── ...
```

### File Extension
`.codex`

### Encoding
UTF-8. Unicode operators (→, ∀, ∃, ≡, ≠, ⊢, ⊗) are first-class. ASCII alternatives exist for every Unicode operator.

---

## Lexical Structure

### Prose Mode vs. Notation Mode

The lexer operates in two modes:

1. **Prose mode** (default) — text is treated as natural language. The lexer recognizes:
   - Chapter/section headers
   - Transition phrases that introduce notation ("We say:", "This is written:", "We define:")
   - Inline type references in parentheses
   - Inline code references in backticks

2. **Notation mode** (entered via indentation under a prose block) — text is treated as formal notation. Standard lexical analysis applies: identifiers, operators, literals, keywords.

The transition between modes is governed by **indentation**. Prose is at the base indentation level. Notation is indented beneath the prose that introduces it.

```
  This is prose. It introduces a definition.          ← Prose mode

    this-is-notation : Number → Number                 ← Notation mode
    this-is-notation (x) = x + 1                       ← Notation mode

  This is prose again.                                 ← Prose mode
```

### Identifiers

- Value identifiers: `lowercase-hyphenated-words` (e.g., `compute-monthly-payment`)
- Type identifiers: `Capitalized-Words` (e.g., `Account`, `List`, `Result`)
- No underscores in identifiers (stylistic choice for readability)
- Unicode letters permitted in identifiers

### Keywords

```
where, such that, let, in, when, if, then, else,
for all, there exists, by, assume, therefore,
claim, proof, definition, chapter, section,
record, containing, gives, may fail, otherwise,
succeed, fail, linear, effect, type, module,
import, export, as, is, are, an, a, the, of,
carrying, carries, either, or
```

Note: many "keywords" are English words used in prose-mode transitions. They are only keywords when they appear in specific syntactic positions. `the` is not reserved — it is recognized as part of the pattern `the sum of`.

### Operators

| Symbol | ASCII Alt | Meaning |
|--------|-----------|---------|
| `→` | `->` | Function type / implication |
| `←` | `<-` | Bind in do-notation |
| `∀` | `forall` | Universal quantifier |
| `∃` | `exists` | Existential quantifier |
| `≡` | `===` | Definitional equality |
| `≠` | `/=` | Inequality |
| `⊢` | `\|-` | Entails / proves |
| `⊗` | `(**)` | Linear pair |
| `::` | `::` | Cons |
| `++` | `++` | Append |
| `\|` | `\|` | Pattern branch / union |
| `&` | `&` | Intersection |
| `=` | `=` | Definition / binding |
| `:` | `:` | Type annotation |
| `_` | `_` | Wildcard / discard |

### Literals

- **Numbers**: `42`, `3.14`, `-17`, `1_000_000` (underscores for readability)
- **Text**: `"hello"`, `"multi\nline"`, `"""raw text blocks"""`
- **Boolean**: `True`, `False`
- **List**: `[1, 2, 3]`
- **Nothing**: `()`

### Comments

Codex has no comments in the traditional sense. Prose *is* the commentary. If you need to say something that isn't part of the program's meaning, you write it as prose — and you think carefully about whether it should be there at all.

For compiler directives and annotations that are not prose, we use:

```
  @annotation-name (parameters)
```

---

## Type System Summary

(Full details in `03-TYPE-SYSTEM.md`)

### Primitive Types

| Type | Description |
|------|-------------|
| `Text` | Unicode string |
| `Number` | Arbitrary-precision rational |
| `Integer` | Arbitrary-precision integer |
| `Boolean` | `True` or `False` |
| `Nothing` | Unit type, single value `()` |
| `Void` | Empty type, no values |

### Composite Types

| Form | Example |
|------|---------|
| Function | `Number → Number` |
| Effectful function | `Path → [FileSystem] Result Text` |
| Record | `record { owner : Person, balance : Amount }` |
| Sum / variant | `Success (a) \| Failure Text` |
| List | `List (a)` |
| Maybe | `Maybe (a)` |
| Result | `Result (a)` |
| Vector | `Vector (n : Integer) (a : Type)` |
| Linear pair | `a ⊗ b` |
| Tuple | `(a, b, c)` |

### Dependent Types

Types may depend on values. The canonical example:

```
Vector (n : Integer) (a : Type) where n ≥ 0

append : Vector (m) (a) → Vector (n) (a) → Vector (m + n) (a)
```

The type checker evaluates type-level arithmetic (`m + n`) during compilation.

### Linear Types

Values marked `Linear` must be used exactly once. This is enforced by the type checker. Resource handles (files, network connections, locks) are linear by default.

### Effects

Function types include an effect row:

```
read-file : Path → [FileSystem] Result Text
pure-fn   : Number → Number              -- empty effect row (pure)
```

Effect rows are sets. `[FileSystem, Network]` means the function may do both.

---

## Definition Forms

### Value Definition

```
Prose introducing the definition.

  name : TypeSignature
  name (params) = body
```

### Type Definition — Record

```
A Person is a record containing:
  - name    : Text
  - age     : Integer
  - email   : Text

  Person = record {
    name  : Text
    age   : Integer
    email : Text
  }
```

### Type Definition — Sum

```
A Shape is either:
  - a Circle with a radius
  - a Rectangle with width and height

  Shape =
    | Circle (radius : Number)
    | Rectangle (width : Number) (height : Number)
```

### Type Definition — With Constraint

```
An Account is a record containing:
  - owner   : Person
  - balance : Amount
  - history : List of Transaction

such that balance equals the sum of all amounts in history.

  Account = record {
    owner   : Person
    balance : Amount
    history : List Transaction
  } where balance ≡ sum (map amount history)
```

The `where` clause is a dependent type constraint verified at construction time.

### Claim and Proof

```
Claim: reversing a list twice returns the original list.

  reverse-reverse : ∀ (xs : List a) → reverse (reverse xs) ≡ xs

Proof:
  by induction on xs.
  
  Base case: xs = []
    ...

  Inductive step: xs = (head :: tail)
    ...
```

### Pattern Matching

```
when (result : Result (a))
  if Success (value) → ... use value ...
  if Failure (reason) → ... handle reason ...
```

Exhaustiveness is checked by the compiler.

### Let Bindings

```
let monthly-rate = annual-rate / 12
    compound     = (1 + monthly-rate) ^ (-months)
in  principal * monthly-rate / (1 - compound)
```

### Do-Notation (for effects)

```
read-and-process (path : Path) : [FileSystem] Result Data =
  do
    contents ← read-file path
    parsed   ← parse-data contents
    succeed parsed
```

---

## Module System

### Chapters as Modules

Each chapter is a module. The chapter title becomes the module name.

```
Chapter: Sorting

  ... this is the Sorting module ...
```

### Sections as Sub-modules

```
Chapter: Sorting

  Section: Insertion Sort
    ... this is Sorting.Insertion-Sort ...
  
  Section: Merge Sort
    ... this is Sorting.Merge-Sort ...
```

### Imports

```
Chapter: Reports

  This chapter uses definitions from the Account module
  and the Sorting module.

  import Account
  import Sorting.Merge-Sort (merge-sort)
```

### Exports

By default, all definitions in a chapter are exported. To restrict:

```
  export (open-account, deposit, withdraw)
```

---

## Prose-Notation Integration

The key design challenge: how does the compiler understand prose?

### Approach: Structured Prose Templates

We do NOT attempt natural language understanding. Instead, we define a set of **prose templates** — English sentence structures that the parser recognizes and maps to formal constructs.

Examples of recognized templates:

| Prose Pattern | Maps To |
|--------------|---------|
| `An X is a record containing:` | Record type definition header |
| `X is either:` | Sum type definition header |
| `such that P` | Dependent type constraint |
| `To V ... :` | Function definition header |
| `V-ing (x : T) gives us Y` | Function signature in prose |
| `may fail if P, otherwise gives us Y` | Result-returning function |
| `Claim: P` | Proof obligation |
| `Proof: by induction on X` | Proof strategy |
| `We say:` | Transition to notation |
| `This is written:` | Transition to notation |

The parser maintains a catalog of these templates. They are extensible — part of the language evolution is adding new recognized prose patterns.

### What Prose Does NOT Do

- Prose does not define operational semantics. The notation block beneath a prose block is the executable definition.
- Prose does not override the type checker. If the prose says "this never fails" but the type says `Result`, the type wins.
- Prose is not free-form. Unrecognized prose is preserved as documentation but has no formal effect.

### The Prose-Notation Contract

Every definition has:
1. **Prose** — human explanation, optionally containing structured templates
2. **Signature** — formal type
3. **Body** — formal implementation

The prose and the signature must be consistent (the compiler checks this where prose templates are recognized). The body and the signature must be consistent (standard type checking). The prose and the body are not directly checked against each other — the signature is the bridge.

---

## Error Model

There are no exceptions. All recoverable errors are values of type `Result (a)`.

For truly unrecoverable situations (out of memory, stack overflow), the runtime aborts. These are not part of the language's type system — they are failures of the machine, not the program.

The `fail` keyword produces a `Failure` value. The `succeed` keyword produces a `Success` value. Pattern matching on `Result` is exhaustive — you must handle both cases.

---

## Evaluation Strategy

Codex is **strict by default** with **opt-in laziness**.

- Function arguments are evaluated before the function body executes
- `lazy` annotation defers evaluation: `lazy (expensive-computation)`
- Lazy values are memoized — evaluated at most once

Strict-by-default was chosen for predictability of effects and resource usage. Lazy evaluation is available where it provides clear benefit (infinite data structures, short-circuit evaluation).

---

## Open Design Questions

These are questions we will resolve during implementation, not before:

1. **How much prose parsing is enough?** We start minimal (a few templates) and expand based on what feels natural when writing real Codex programs.

2. **Record syntax** — do we use braces `{ }` or indentation-based? The vision doc uses braces. We may want both.

3. **Operator sections** — do we support `(+ 1)` as a shorthand for `\x → x + 1`?

4. **Type classes vs. traits vs. something else** — the vision doc mentions type classes (from Haskell). We need to decide on the exact ad-hoc polymorphism mechanism.

5. **Universe hierarchy** — `Type : Type` is inconsistent (Girard's paradox). We need a universe hierarchy (`Type₀ : Type₁ : Type₂ : ...`) but how explicit should it be?

6. **Proof automation** — how much automated proof search do we include in the bootstrap? Start minimal.

7. **Recursion** — do we require termination proofs for all recursive functions? Probably yes for proofs, no for general computation (with `[Diverge]` effect).
