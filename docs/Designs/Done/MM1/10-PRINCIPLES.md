# 10 — Engineering Principles

## The Rules We Follow

These principles govern every implementation decision. When in doubt, refer to this document. When two principles conflict, the one listed first wins.

---

### 1. Ship Working Software at Every Milestone

Every milestone produces a system that does something real. Not a library that will eventually be used. Not a type system that will eventually check programs. A program goes in, a result comes out. If a milestone doesn't end with a demo, the milestone is wrong.

### 2. Correctness Over Performance

The bootstrap compiler does not need to be fast. It needs to be correct. A type checker that takes 10 seconds but produces correct results is infinitely more valuable than one that takes 100ms but has edge cases. Performance optimization comes after correctness is proven by tests.

Exception: if performance makes development painful (> 30 seconds to type-check a small program), we fix it — but we fix it by profiling and targeted optimization, not by compromising correctness.

### 3. Types Are the Specification

The type system is the most important design artifact. Everything else — the syntax, the prose model, the backends — serves the type system. If a design decision weakens the type system, it is probably wrong. If it strengthens the type system, it is probably right.

### 4. Diagnostics Are a Feature

Error messages are not an afterthought. They are part of the user interface. Every diagnostic must:
- State what went wrong
- Show where it went wrong (with source location)
- Suggest how to fix it (where possible)
- Use language a programmer would understand (not type theory jargon)

A type error that says `cannot unify ?a with Integer` is a bug. A type error that says `Expected a Number here, but you gave an Integer. Try using to-number to convert.` is correct.

### 5. Immutability by Default

All data representations are immutable. AST nodes, IR nodes, types, facts — all immutable. Builders and accumulators are mutable during construction, then frozen. This eliminates entire categories of bugs and makes parallel processing safe.

### 6. Test What Matters

Every compiler phase has:
- **Positive tests**: programs that should succeed, verifying the output is correct
- **Negative tests**: programs that should fail, verifying the diagnostic is correct
- **Round-trip tests**: parse → print → parse produces the same result
- **Snapshot tests**: output is compared to a known-good baseline

We do not chase coverage numbers. We test the interesting cases: edge cases, error cases, and the examples from the vision document.

### 7. No Premature Abstraction

Do not create an interface until you have two implementations. Do not create a base class until you have three subclasses. Do not create a framework until you have built three things that need it.

The temptation in a compiler project is to over-abstract everything (visitor pattern on visitors on visitors). Resist. Write concrete code. Refactor when the pattern is clear.

### 8. The Vision Documents Are North Stars, Not Specifications

`NewRepository.txt` and `IntelligenceLayer.txt` describe the destination. These planning documents describe the route. When the vision says something that is impractical to implement in the current milestone, we defer it — we do not compromise the current milestone trying to reach the vision prematurely.

### 9. One Thing at a Time

Each file does one thing. Each class does one thing. Each method does one thing. Each commit does one thing. If you're changing two unrelated things, make two commits. If a class is doing two things, make two classes.

### 10. Read the Literature

This project builds on decades of programming language research. Before implementing a feature, read the paper. Before designing a subsystem, read how others have done it. We stand on shoulders:

- **Type checking**: "Bidirectional Typing" (Dunfield & Krishnaswami)
- **Dependent types**: "The Implementation of Functional Programming Languages" (Peyton Jones), Idris 2 implementation papers
- **Linear types**: "Linear Haskell" (Bernardy et al.)
- **Algebraic effects**: "An Introduction to Algebraic Effects and Handlers" (Pretnar), Koka papers
- **Proof checking**: "Certified Programming with Dependent Types" (Chlipala)
- **Parsing**: "Crafting Interpreters" (Nystrom) for practical parsing, "Parsing Techniques" (Grune & Jacobs) for theory
- **Content addressing**: IPFS papers, Unison language design documents

---

## Code Style

### C# Conventions

- Follow standard .NET naming conventions (PascalCase for public members, camelCase for locals)
- Use `record` types for immutable data (AST nodes, IR nodes, types, diagnostics)
- Use `sealed` on classes that are not designed for inheritance
- Use `ImmutableArray<T>` for collections in immutable types
- Use `readonly record struct` for small value types (Span, ContentHash)
- Pattern matching (`switch` expressions) over visitor pattern where possible
- `nullable` annotations enabled — no null surprises
- No `var` for complex types where the type is not obvious from context

### File Organization

- One primary type per file (matching the filename)
- Related small types (e.g., an enum used by one class) can share a file
- Test files mirror the structure of the source they test

### Project References

- Projects reference only what they need (no transitive dependency assumptions)
- `Codex.Core` is referenced by everything — keep it small
- Test projects reference their subject + `Codex.Core`

---

## Decision Log

Every significant design decision is recorded. Format:

```
## Decision: [Title]
**Date**: YYYY-MM-DD
**Context**: What situation prompted this decision?
**Options considered**: What alternatives were evaluated?
**Decision**: What did we choose?
**Rationale**: Why?
**Consequences**: What follows from this decision?
```

Decisions are appended to `docs/DECISIONS.md` as they are made.

---

## Definition of Done

A feature is done when:
1. It works (the demo passes)
2. It is tested (positive, negative, edge cases)
3. It has diagnostics (error messages for failure modes)
4. It builds (`dotnet build` succeeds with no warnings)
5. All existing tests still pass (`dotnet test` is green)
6. It matches the corresponding planning document (or the planning document is updated to reflect the decision)
