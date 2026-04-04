# Glossary

Terms used throughout the Codex project documentation.

---

| Term | Definition |
|------|-----------|
| **ANF** | A-Normal Form. An intermediate representation where all intermediate values are named. No nested expressions. |
| **AST** | Abstract Syntax Tree. The simplified, desugared tree representation of a program's structure. |
| **Algebraic Effects** | A system where side effects are declared in types and interpreted by handlers. Subsumes monads. |
| **Bidirectional Type Checking** | A type checking strategy with two modes: inferring a type from an expression, and checking an expression against an expected type. |
| **Bootstrap** | The process of writing a compiler for a language in another language, then using that compiler to compile the language's own compiler written in itself. |
| **CST** | Concrete Syntax Tree. A lossless tree that preserves all tokens, whitespace, and trivia from the source. |
| **Canonical View** | The view of the repository that all stakeholders have agreed to. The "main branch" equivalent. |
| **Chapter** | The top-level organizational unit in Codex source. Corresponds to a module. |
| **Content-Addressed** | A storage scheme where data is identified by the hash of its content, not by a name or location. |
| **Dependent Type** | A type that depends on a value. Example: `Vector 5 Integer` — a vector whose type includes its length. |
| **Diagnostic** | An error, warning, or suggestion produced by the compiler, with source location and optional fix suggestion. |
| **Effect Row** | The set of effects declared in a function's type. Example: `[FileSystem, Network]`. |
| **Elaboration** | The process of filling in implicit arguments, inserting coercions, and resolving proof obligations during type checking. |
| **Fact** | The fundamental unit of the Codex repository. Immutable, content-addressed, attributed, typed. |
| **Fuel** | A budget (number of reduction steps) given to the type-level normalizer to prevent divergence. |
| **IR** | Intermediate Representation. The typed, optimized, target-agnostic representation between the AST and code generation. |
| **Linear Type** | A type whose values must be used exactly once. Used for resource safety (file handles, connections). |
| **Literate Programming** | Knuth's concept of programs as documents written for human reading, from which machine-executable code is extracted. |
| **Monomorphization** | Specializing a generic function for each concrete type it is called with. Required by some backends. |
| **Normalizer** | The component that evaluates type-level expressions to their normal form during type checking. |
| **Notation Mode** | The lexer mode for formal code (identifiers, operators, expressions). Entered via indentation. |
| **Proof Obligation** | A proof that the programmer must provide for a dependent type constraint. Example: proving that an index is in bounds. |
| **Proposal** | A formal suggestion to add or change a definition in the canonical view. Replaces pull requests. |
| **Prose Mode** | The lexer mode for natural language text. The default mode. |
| **Prose Template** | A recognized English sentence pattern that the parser maps to a formal construct. |
| **Section** | A sub-division of a Chapter in Codex source. Corresponds to a sub-module. |
| **Self-Hosting** | When a compiler can compile its own source code. The ultimate test of a language's expressiveness. |
| **Span** | A range of characters in source text, identified by start and end positions. |
| **Stage 0** | The C# bootstrap compiler. |
| **Stage 1** | The Codex compiler compiled by Stage 0. |
| **Stage 2** | The Codex compiler compiled by Stage 1. Should be identical to Stage 1's output. |
| **Supersession** | A repository fact indicating that one definition replaces another. The old definition remains. |
| **Trust Lattice** | The multi-dimensional trust profile of a definition: proof coverage, test coverage, vouchers, etc. |
| **Unification** | The process of finding a substitution that makes two types equal. Core algorithm of type inference. |
| **Universe** | A level in the type hierarchy. `Type₀` contains ordinary types, `Type₁` contains `Type₀`, etc. |
| **Verdict** | A stakeholder's response to a Proposal: Accept, Reject, Amend, or Abstain. |
| **View** | A consistent selection of facts from the repository. Replaces branches. |
| **Vouching** | A trust fact where an author stakes their reputation on a definition's correctness. |
