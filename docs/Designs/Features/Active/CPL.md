# Codex Pattern Language (CPL)
*A structural, deterministic, human‑readable pattern system for the Codex ecosystem.*

## 1. Purpose
CPL replaces legacy regex‑style pattern matching with a **structural**, **typed**, and **deterministic** system aligned with Codex doctrine.  
It enables agents and humans to express intent over IR nodes without symbolic noise, backtracking, or hidden state.

CPL is not a string matcher.  
CPL is a **predicate language over structured data**.

## 2. Design Principles
### 2.1 Human‑Legibility
Patterns must be readable aloud.  
No symbolic compression. No lore. No regex‑style punctuation.

### 2.2 Structural Matching
Patterns operate on **typed IR nodes**, not raw text.  
Text is treated as a node type with explicit structure.

### 2.3 Determinism
CPL execution is single‑pass, with no backtracking or exponential traps.  
Every operator has a predictable cost.

### 2.4 Composability
Patterns compose like functions.  
Small, reusable predicates form larger structural recognizers.

### 2.5 Minimalism
Only essential operators are included.  
No feature exists without a clear architectural justification.

## 3. Pattern Shape
A pattern is a named predicate over a node:

```
pattern FooCall:
    kind == Call
    callee == "foo"
    args.len == 2
```

Patterns may be nested, composed, or reused.

## 4. Core Operators
CPL includes a minimal set of structural operators:

- **Equality**
  ```
  name == "foo"
  ```

- **Membership**
  ```
  op in {"+", "-", "*"}
  ```

- **Shape Constraints**
  ```
  args.len == 3
  ```

- **Nested Match**
  ```
  args[0] matches Identifier(name == "x")
  ```

- **Optional Fields**
  ```
  maybe docstring matches Text
  ```

- **Sequence Search**
  ```
  body contains Statement(kind == Return)
  ```

- **Quantifiers**
  ```
  all args match Identifier
  any args match Literal
  ```

- **Negation**
  ```
  not (callee == "debug")
  ```

These operators are intentionally limited to preserve determinism and readability.

## 5. Text Matching
Text is treated as a structured node:

```
pattern EmailAddress:
    kind == Text
    contains "@"
    contains "."
    not contains " "
```

More complex text structures are expressed explicitly, not symbolically.

## 6. Composition
Patterns can reference other patterns:

```
pattern AssignmentToX:
    kind == Assignment
    target matches Identifier(name == "x")

pattern SuspiciousWrite:
    AssignmentToX
    value matches Call(callee == "eval")
```

Composition encourages reuse and clarity.

## 7. Execution Model
CPL compiles to a deterministic matcher with:

- No backtracking  
- No recursion beyond IR structure  
- Predictable cost per operator  
- Full explainability for agents and humans  

This makes CPL suitable for ingestion, analysis, linting, transformation, and proof‑driven workflows.

## 8. Integration Points
CPL is intended to integrate with:

- **Codex IR** (primary target)
- **Annotation System** (patterns as triggers or filters)
- **Agent Reasoning** (patterns as predicates in workflows)
- **Static Analysis** (structural queries)
- **Transform Pipelines** (pattern‑driven rewrites)

Future work will define the exact interfaces.

## 9. Future Directions (Not in Scope Yet)
- Formal grammar for CPL
- Bytecode or VM representation
- Pattern optimizer
- Standard library of structural patterns
- Agent‑facing explanation protocol
- Integration with Codex annotation metadata

These will be developed once the IR stabilizes.

## 10. Open Questions
- Should patterns support limited user‑defined functions?
- Should patterns allow binding (e.g., `let x = args[0]`)?
- Should CPL support compile‑time evaluation?
- How should patterns interact with versioned IR schemas?

These questions are intentionally deferred.

---

CPL is the structural matcher for the new world:  
deterministic, legible, minimal, and aligned with Codex doctrine.
