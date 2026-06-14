# 09 — Risks & Open Questions

## Technical Risks

### Risk 1: Dependent Type Checking Performance
**Severity**: High
**Likelihood**: High

Dependent type checking requires evaluating expressions at compile time. If type-level computation is unrestricted, the type checker may diverge or take exponential time.

**Mitigation**:
- Require all type-level functions to be provably total (structurally recursive)
- Impose a fuel limit on the normalizer (N reduction steps, then give up with an error)
- Profile type checking performance early and often
- Provide escape hatches: `@trust-me` to skip a proof obligation (with a loud warning)

### Risk 2: Prose Parsing Ambiguity
**Severity**: Medium
**Likelihood**: High

The prose/notation mode switching depends on indentation and transition phrases. Natural language is inherently ambiguous. Users will write prose that the parser doesn't understand.

**Mitigation**:
- Start with a very small set of recognized prose templates
- Unrecognized prose is preserved as documentation, not rejected
- Provide clear error messages when a prose pattern is *almost* recognized
- Expand the template catalog based on user feedback, not speculation
- The notation blocks are always the source of truth — prose is sugar

### Risk 3: Effect System Complexity
**Severity**: Medium
**Likelihood**: Medium

Algebraic effects with handlers are powerful but complex. The interaction between effects, dependent types, and linear types is not well-explored in existing literature.

**Mitigation**:
- Implement effects as a separate phase from dependent types initially
- Start with a simple effect system (just tracking, no handlers) and add handlers later
- Study existing implementations: Koka, Eff, Multicore OCaml, Unison
- Be willing to simplify if the full vision is too complex for bootstrap

### Risk 4: Self-Hosting Feasibility
**Severity**: Medium
**Likelihood**: Medium

Writing a compiler in a language that the compiler is still being designed in is a chicken-and-egg problem. Language design decisions made early may be regretted when writing the compiler in the language.

**Mitigation**:
- Write small programs in Codex as soon as the language is usable (Milestone 3)
- Use the experience of writing in Codex to inform language design decisions
- Accept that the language will evolve during self-hosting and plan for iteration
- The C# bootstrap doesn't need to be perfect — it needs to be correct

### Risk 5: Adoption & Ecosystem Bootstrap
**Severity**: High
**Likelihood**: High

A new language with no ecosystem, no libraries, no community faces a cold-start problem. People won't use it without libraries; libraries won't exist without users.

**Mitigation**:
- Transpilation is the bridge: Codex can call C#/.NET libraries via FFI from day one
- The C# backend means the entire .NET ecosystem is available
- Focus initial standard library on compiler needs (we are our own first user)
- The repository model is the long-term answer — but it needs content first

### Risk 6: Scope Creep
**Severity**: High
**Likelihood**: Very High

The vision is enormous: a language, a type system, a proof engine, a repository, an IDE, six transpilation backends, and a social protocol. The temptation to add features at every stage will be overwhelming.

**Mitigation**:
- Milestones are non-negotiable: each one delivers a working system
- Features not in the current milestone are written down and deferred
- "No" is a complete sentence when evaluating feature requests before the current milestone is done
- The planning documents (these documents) are the scope — work not described here doesn't happen until it is

### Risk 7: C# Backend Limitations
**Severity**: Low-Medium
**Likelihood**: Medium

C# cannot natively express linear types, dependent types, or algebraic effects. The encoding (runtime checks, interface patterns) may be awkward or have significant runtime overhead.

**Mitigation**:
- Accept that the C# backend is a bootstrap target, not the final target
- Runtime checks are fine for correctness — performance is a later concern
- The Rust backend (Milestone 12) will be the high-fidelity target
- If C# encoding is too painful for a specific feature, defer that feature to after bootstrap

---

## Open Design Questions

These are questions without answers yet. They will be resolved during implementation, documented as decisions, and may be revisited.

### Q1: Type Class Mechanism
**Options**:
- Haskell-style type classes with global instances
- Scala-style implicits / given instances
- Rust-style traits with explicit impl blocks
- Modular type classes (ML-style)
- Something novel that fits the prose model

**Decision deadline**: Before Milestone 2 (needed for basic polymorphism like `show`, `==`).

### Q2: Universe Hierarchy
**Options**:
- Explicit universes: `Type₀`, `Type₁`, `Type₂`
- Implicit universes with universe polymorphism (like Agda)
- `Type : Type` with consistency check (like Idris 2's `--type-in-type` escape hatch)
- Cumulative universes

**Decision deadline**: Before Milestone 8 (dependent types need this).

### Q3: Totality Checking
**Options**:
- All functions total by default, `[Diverge]` effect for general recursion
- Functions total only when used at the type level or in proofs
- No totality checking (rely on fuel limits in the normalizer)

**Decision deadline**: Before Milestone 8.

### Q4: Recursion Syntax
**Options**:
- Implicit recursion (function can call itself by name — like Haskell)
- Explicit `rec` keyword (like OCaml's `let rec`)
- Structural recursion only (no explicit recursion — patterns like fold/unfold)

**Decision deadline**: Before Milestone 1 (affects basic function definitions).

### Q5: Foreign Function Interface
**Options**:
- Direct FFI to the target language (different per backend)
- Universal FFI through the IR (one FFI mechanism, backends adapt)
- No FFI — all external interaction through effects

**Decision deadline**: Before Milestone 3 (we need to call .NET libraries).

### Q6: Error Messages Quality
This is not a question with options — it's a commitment. Error messages in Codex must be *excellent*. They must:
- Explain what went wrong in plain English
- Show the relevant source location
- Suggest a fix where possible
- Never use jargon without explanation

This is an ongoing quality requirement for every milestone.

### Q7: Standard Library Scope
**Question**: How big is the standard library at bootstrap? Do we provide collections, string manipulation, math, file I/O? Or is the standard library minimal and we rely on .NET interop?

**Tentative answer**: Minimal standard library for bootstrap. Core types (List, Maybe, Result, Vector), basic operations (arithmetic, string concatenation, comparison), and effect definitions. Everything else via .NET interop until the language is mature enough to write a rich standard library in Codex itself.

### Q8: Numeric Tower
**Question**: How do we handle the relationship between Integer and Number? Is Integer a subtype of Number? Are conversions explicit?

**Tentative answer**: Explicit conversions. `Integer` and `Number` are separate types. `to-number : Integer → Number` and `to-integer : Number → Maybe Integer` (because not all numbers are integers). No implicit numeric coercion.

---

## Dependencies & External Risks

### .NET 8 Longevity
.NET 8 is an LTS release (supported until November 2026). We should plan to move to .NET 10 (the next LTS) when available. This should be straightforward — we avoid platform-specific APIs.

### Library Dependencies
We minimize external dependencies (see Architecture doc). The ones we plan to use:
- `System.Collections.Immutable` — ships with .NET, no risk
- `System.CommandLine` — Microsoft-maintained, stable
- `xUnit` + `FluentAssertions` — standard testing, stable
- Any LSP library — need to evaluate options

### Tooling
We develop in Visual Studio / VS Code with C# dev kit. Standard .NET tooling. No exotic build systems.

---

## What Could Kill This Project

Let's be honest about existential risks:

1. **Scope overwhelm**: The vision is so large that no meaningful progress is made on any single front. **Counter**: The milestone plan is deliberately incremental. Each milestone is independently valuable.

2. **Type system quicksand**: Getting stuck in type theory research instead of building a working system. **Counter**: Phase the type system. Simple types first. Dependent types later. Ship something that works at every stage.

3. **The prose parsing tar pit**: Spending months on NLP-adjacent problems trying to make the prose parser understand natural language. **Counter**: The prose parser recognizes templates, not natural language. Unrecognized prose is documentation. Keep it simple.

4. **Loss of focus**: Getting distracted by the repository, the IDE, the transpilation backends before the core language works. **Counter**: The critical path is clear: Language → Type Checker → C# Backend → Everything Else.

5. **Perfectionism**: Refusing to ship anything because it doesn't match the vision. **Counter**: The vision is the north star, not the minimum viable product. Ship early, iterate, converge.
