# Decision Log

> **Date correction (2026-03-18)**: Several dates in this file were hallucinated by
> the agent from training data. Dates originally showing `2025-06`, `2025-07`,
> `2025-08`, `2025-09`, and `2026-06` have been corrected based on git commit
> timestamps. The Codex project began on 2026-03-14; all pre-2026 dates were errors,
> and `2026-06` dates were future-hallucinated (today is 2026-03-18).
> Milestone references (M3, M5, etc.) were correct — only the dates were wrong.

Significant design and engineering decisions are recorded here in chronological order.

---

## Decision: Bootstrap Language is C# on .NET 8
**Date**: 2026-03-14
**Context**: We need a language to write the first Codex compiler in. The compiler must be correct, maintainable, and productive to develop in.
**Options considered**: C#, Rust, Haskell, TypeScript, F#
**Decision**: C# 12 on .NET 8
**Rationale**: 
- Strong type system with nullable reference types catches many bugs at compile time
- Excellent tooling (Visual Studio, Rider, VS Code with C# Dev Kit)
- .NET ecosystem provides everything we need (immutable collections, cryptography, JSON, CLI)
- The first transpilation target is C# — writing the compiler in C# means we deeply understand the target
- F# was considered (its algebraic types are closer to what we're building) but C# has broader familiarity and better tooling for large projects
- Rust was considered but the development velocity for a compiler is lower than C#
**Consequences**: 
- The C# backend is the first and most important backend
- We use C# idioms (records, pattern matching, nullable annotations) pervasively
- The self-hosting path requires the C# backend to be complete before the Codex compiler can compile itself

---

## Decision: Hand-Written Lexer and Parser
**Date**: 2026-03-14
**Context**: We need to tokenize and parse Codex source, which has an unusual structure (prose/notation mode switching, indentation sensitivity).
**Options considered**: Hand-written, ANTLR, parser combinators (Sprache/Pidgin), tree-sitter
**Decision**: Hand-written lexer and recursive descent parser
**Rationale**:
- The prose/notation mode switching is too context-sensitive for most generators
- We need perfect error recovery for IDE support — hand-written parsers give full control
- We need full trivia tracking for round-trip fidelity
- Hand-written parsers are easier to debug and modify during language evolution
- Roslyn (C# compiler) uses the same approach and it works exceptionally well
**Consequences**:
- More initial development work than using a generator
- Full control over error messages and recovery
- No external dependency for parsing

---

## Decision: Solution Structure — Multi-Project with Layer Separation
**Date**: 2026-03-14
**Context**: The compiler has many distinct phases that should be testable independently.
**Options considered**: Monolithic project, multi-project layered, microservice (rejected immediately)
**Decision**: Multi-project solution with one project per compiler phase
**Rationale**:
- Each phase is independently testable
- Dependency direction is enforced by project references
- The compiler-as-library pattern enables CLI, LSP, and environment to share the same core
- Standard practice for large .NET solutions
**Consequences**:
- More projects to manage
- Must be disciplined about not creating circular dependencies
- Build time may increase (mitigated by incremental builds)

---

## Decision: IR is Not A-Normal Form
**Date**: 2026-03-14 (M3)
**Context**: The original spec called for A-Normal Form IR where all intermediate values are named. During implementation, this added complexity with no benefit — each backend emits differently, and ANF forced premature linearization.
**Decision**: Let IR expressions nest freely. `IRApply(IRApply(f, x), y)` is valid.
**Rationale**: Simpler lowering pass, each backend handles linearization if needed. C# and JS both naturally nest expressions. Rust handles it with blocks.
**Consequences**: No optimization passes rely on ANF. If we add optimization later, we may introduce a local normalization step.

---

## Decision: Curried Application in IR
**Date**: 2026-03-14 (M3)
**Context**: Codex functions are curried: `f a b` is `(f a) b`. The IR could flatten this to multi-arg calls or keep it curried.
**Decision**: IR uses single-arg `IRApply`. Multi-arg calls are nested: `IRApply(IRApply(f, a), b)`.
**Rationale**: Matches the language semantics. Each backend decides how to collapse curried calls — C# and JS emit multi-arg calls for known-arity functions, Rust does the same. The emitter has `CollectApplyArgs` to flatten when useful.
**Consequences**: Backends need arity tracking to emit efficient multi-arg calls.

---

## Decision: No Separate Runtime Library
**Date**: 2026-03-14 (M3–M5)
**Context**: The original design called for a hand-written runtime library per backend (Unit.cs, Maybe.cs, etc.).
**Decision**: No separate runtime. Built-in functions are emitted inline. Type definitions come from user code, not a library.
**Rationale**: The Codex type system is expressive enough that users define their own `Maybe`, `Result`, etc. Built-ins like `print-line`, `char-at`, `text-length` are simple enough to emit inline in each backend.
**Consequences**: No runtime dependency for generated code. Each backend is self-contained. New built-ins require changes to all three emitters.

---

## Decision: Direct I/O for Effects (No Monadic Encoding)
**Date**: 2026-03-14 (M5)
**Context**: Effects could be encoded as monads (Reader/Writer), interface injection, or direct I/O.
**Decision**: Direct I/O. `print-line` emits `Console.WriteLine` (C#), `console.log` (JS), `println!` (Rust). The effect type annotation is checked but not reified at runtime.
**Rationale**: Simple, working, and sufficient for the bootstrap. The effect type system prevents pure functions from calling effectful ones — that's the safety guarantee. Full algebraic effect handlers can be added later without changing existing code.
**Consequences**: No effect handlers yet. `run-state` and user-defined effects are deferred.

---

## Decision: long/double Instead of BigInteger/decimal
**Date**: 2026-03-14 (M3)
**Context**: The original design used `BigInteger` for Integer and `decimal` for Number.
**Decision**: Use `long` for Integer and `double` for Number.
**Rationale**: 64-bit primitives are fast and sufficient for the bootstrap. The compiler itself doesn't need arbitrary precision. Upgrading to BigInteger/BigRational later is a backend change, not a language change.
**Consequences**: Integer overflow is possible but not checked. Number precision is IEEE 754 double.

---

## Decision: Self-Hosting Without Type Checker in Codex
**Date**: 2026-03 (M13, early)
**Superseded by**: The Codex-side type checker was subsequently implemented (see M13 deliverables in [08-MILESTONES.md](08-MILESTONES.md)).
**Original decision**: Declare structural parity as the M13 success criterion. The Stage 0 type checker handles all type checking. Stage 1 emits `object` types.
**What actually happened**: A full Codex-side type checker (Unifier, TypeEnvironment, TypeChecker, NameResolver) was written. Additionally, `object` erasure was replaced by C# generics (see "C# Generics for Polymorphic Functions" below). Stage 1 now compiles with zero C# errors and produces valid output.

---

## Decision: Codex Main Renamed to codex_main in Rust Backend
**Date**: 2026-03 (M12)
**Context**: Rust reserves `fn main()` as the entry point. The Codex `main` function collided.
**Decision**: Emit the Codex `main` as `codex_main`, and generate a Rust `fn main()` that calls it.
**Rationale**: Simple, no namespace pollution, clear separation between Codex semantics and Rust entry point convention.
**Consequences**: Rust output always has a `codex_main` function.

---

## Decision: Tail Call Optimization via Loop Conversion
**Date**: 2026-03
**Context**: Codex uses recursion for all iteration. Deep recursion causes `StackOverflowException` on .NET and Node.js. Rust may or may not optimize tail calls.
**Decision**: All three emitters detect self-recursive tail calls and convert them to `while(true)` (C#/JS) or `loop {}` (Rust) with parameter reassignment.
**Rationale**: Simple, reliable, no runtime dependency. The detection is conservative — only self-calls in tail position of if/let/match branches are converted. Non-tail calls remain as recursion. This eliminates the most common SO risk (accumulator-style recursion).
**Implementation**: `HasSelfTailCall` walks the IR expression tree. `EmitTailCallDefinition` emits the loop. Tested with 1,000,000-deep recursion (`tco-stress.codex`).
**Consequences**: Recursive functions with tail calls are now stack-safe in all three backends.

---

## Decision: ~~TypeVariable Emits as `object` in C#, Not Generic `T<N>`~~ — SUPERSEDED
**Date**: 2026-03
**Superseded by**: "C# Generics for Polymorphic Functions" (below)
**Context**: Polymorphic sum types (e.g., `Result (a) = Success (a) | Failure Text`) emitted `T0` as a C# type name, but no generic parameter was declared. This caused `CS0246: T0 not found`.
**Original decision**: Emit `TypeVariable` as `object` in C#. Use casts at usage sites.
**Why superseded**: The `object` approach caused 105+ CS1503 type mismatch errors during Stage 1 compilation. Switching to C# generics eliminated them entirely. See the later decision.

---

## Decision: No String Interpolation Syntax
**Date**: 2026-03 (Damian's answer to Q4)
**Context**: The lexer was designed with `${ }` interpolation in mind.
**Decision**: No string interpolation. Use `++` for concatenation and named functions like `integer-to-text` for conversion.
**Rationale**: `${}` is visual noise that reduces readability — contrary to Codex's prose-first philosophy. Named functions are clearer: "convert this integer to text" vs "embed this integer in a string template." The language should not add special syntax to make string building easier when function composition already works.
**Consequences**: No lexer/parser changes needed. The `InterpolationMode` from the original lexer design is permanently deferred.

---

## Decision: Inductive Hypothesis Registration in Proof Checker
**Date**: 2026-03 (M10)
**Context**: Proof by induction requires the inductive hypothesis (IH) to be available in the step case. The checker already had `CheckInduction` with case splitting but did not register the IH.
**Decision**: For each variable sub-pattern in a constructor case, register the IH (the claim substituted with that variable) in the claim map. It's available both as `__ih_{variable}` and via the enclosing claim name with the variable as an argument.
**Rationale**: This is the standard approach in proof assistants (Coq, Lean, Agda). The IH is scoped to the case — saved and restored after checking.
**Consequences**: Induction proofs can now reference the IH. Structural induction on lists works. Arithmetic induction requires Peano encoding (deferred). The `assume` escape hatch remains for steps that require function reduction.

---

## Decision: Cong Goal Decomposition
**Date**: 2026-03 (M10)
**Context**: `cong f proof` tried to infer the inner proof's type, which fails for `Refl` (no type to infer). `cong List Refl` for the goal `List Nil ≡ List Nil` failed.
**Decision**: When inference fails, decompose the goal: extract `A` from `f(A) ≡ f(B)` and check the inner proof against `A ≡ B`.
**Rationale**: Bidirectional — try inference first, fall back to checking. Matches the bidirectional pattern used in the type checker.
**Consequences**: `cong List Refl` now works. `cong f (lemma args)` works when inference succeeds or when the goal is decomposable.

---

## Decision: Variant Type Syntax Without Leading Pipe
**Date**: 2026-03
**Context**: `Shape = Circle (Integer) | Rectangle (Integer)` didn't parse — the parser required a leading `|` before the first constructor: `Shape = | Circle (Integer) | Rectangle (Integer)`.
**Decision**: Accept both forms. If the token after `=` is a `TypeIdentifier` and a `|` appears later on the same line, parse as a variant type.
**Rationale**: The no-leading-pipe form is more natural and matches Haskell/ML convention. The leading-pipe form remains valid.
**Consequences**: Both `Shape = Circle | Rectangle` and `Shape = | Circle | Rectangle` now parse correctly.

---

## Decision: Codex-Side Type Checker — Threaded UnificationState
**Date**: 2026-03 (M13)
**Context**: The type checker requires mutable state for type variable substitutions (the unifier). Codex is purely functional — no mutable references.
**Decision**: Model the unifier as a `UnificationState` record threaded through all functions. Every inference function takes state as input and returns updated state as output. Named `UnificationState` (not `MutableRef`) per the named-purpose mutability principle.
**Rationale**: This is the standard purely functional approach (used in Haskell, ML). The name `UnificationState` answers "why is this changing?" — because we're accumulating unification knowledge. The alternative (effect system with State effect) requires the Codex effect system to be more mature.
**Implementation**: `UnificationState` holds `substitutions : List SubstEntry`, `next-id : Integer`, `errors : List Diagnostic`. All type checking functions return `CheckResult { inferred-type, state }` or `UnifyResult { success, state }`.
**Consequences**: Every function signature is longer (extra state parameter). But the data flow is explicit, debuggable, and the code is self-documenting about what mutates and why.

---

## Decision: Codex-Side Name Resolver — Error-Collecting Scope Walk
**Date**: 2026-03 (M13)
**Context**: Name resolution must validate that all referenced names exist in scope. The C# NameResolver uses `Set<string>` and mutates a `DiagnosticBag`. In Codex, there's no mutable set.
**Decision**: Use a `Scope` record containing `List Text`. Walk all expressions, collecting `List Diagnostic` as errors. No mutation — errors are concatenated via `++`. Scope is extended by prepending names.
**Rationale**: Lists are inefficient for lookup (O(n) per check), but correctness is the priority. Codex programs are small enough that linear scan is fine. If performance matters later, a balanced tree or hash set can be added to `Core/Collections.codex`.
**Consequences**: The name resolver is purely functional. `resolve-module` returns `ResolveResult { errors, top-level-names, type-names, ctor-names }`. The `compile-checked` pipeline gates on zero errors before proceeding to type checking.

---

## Decision: C# Generics for Polymorphic Functions
**Date**: 2026-03 (M13)
**Context**: The C# emitter used `object` for all type variables, causing `Func<LetBind, ALetBind>` vs `Func<object, object>` mismatches, `List<object>` to `List<string>` errors, and required coercion hacks at every callsite.
**Decision**: Emit `TypeVariable(id)` as `T{id}`. Polymorphic functions become C# generic methods: `public static T1 map_list<T0, T1>(Func<T0, T1> f, List<T0> xs)`. C# infers type arguments at callsites.
**Rationale**: The boxing argument against generics is a false economy — nobody building a compiler is CPU-bound on type dispatch. Generics eliminate an entire class of emitter bugs and make the generated C# natural.
**Consequences**: Zero `object` erasure. All 105 CS1503 type mismatch errors eliminated. Stage 1 compiles with zero errors. The emitter is simpler (no coercion logic needed).

---

## Decision: Parser Error Recovery — Commit-Then-Recover
**Date**: 2026-03-15
**Context**: The parser stopped at the first error in most cases. `TryParseTypeDefinition` backtracked to `savedPos` on any failure after `=`, losing the type name entirely. `TryParseDefinition` similarly lost partial results. The LSP got nothing for definitions with syntax errors — no hover, no completion, no go-to-def.
**Decision**: Once the parser has consumed enough tokens to commit (e.g., `TypeId =` for type defs, `name (params) =` for defs), it must produce a partial node rather than backtrack. New node: `ErrorTypeBody` for type definitions. Definitions with missing `=` produce `DefinitionNode` with `ErrorExpressionNode` body. Record/variant bodies skip bad fields/constructors and continue.
**Rationale**: The Roslyn approach — the parser always produces a tree, even for broken code. Every definition gets a node. The desugarer maps `ErrorTypeBody` to an empty `RecordTypeDef` so the type name is visible to downstream passes. The key insight: once we've seen `TypeId =`, that IS a type definition — the question is only what the body looks like.
**Implementation**: Three new diagnostic codes: CDX1050 (bad type body after `=`), CDX1051 (bad field in record body), CDX1052 (bad constructor in variant body). 11 new tests. `SkipToNextDefinition` used as the recovery strategy at the definition level.
**Consequences**: LSP can now show type names, definition names, and partial type annotations even in files with syntax errors. Multiple definitions after an error are all parsed. The parser never backtracks after committing to a definition shape.

---

## Decision: run-state Effect Handler — Built-in with Mutable-Cell C# Emission
**Date**: 2026-03
**Context**: The effect system had effect types, checking, and polymorphism, but no effect handlers. `run-state` was the first handler needed — it eliminates `State s` from the effect row.
**Decision**: Implement `run-state`, `get-state`, `set-state` as built-in functions (same pattern as `print-line`, `read-line`). The type checker detects when a function parameter has effectful type and temporarily allows those effects while checking the argument (the handler's computation). The return type is unwrapped if the handler eliminates all effects. The C# emitter emits a closure with a mutable `__state` variable.
**Rationale**: This is the direct I/O approach extended to state — no algebraic effect runtime, no continuation-passing transform, no monadic encoding. The language semantics are purely functional (run-state returns a value), but the C# backend uses mutation under the hood. The `do` keyword was added to `IsApplicationStart()` so `run-state init do ...` parses correctly.
**Consequences**: `run-state 0 do ... get-state ... set-state x ...` works end-to-end. The State effect is eliminated from the return type, so `main : Integer` + `run-state` is valid. User-defined effect handlers deferred — they require a more general mechanism (continuations or CPS transform).

---

## Decision: Codex-Side Emitter Generics — Closing the Stage 1 Gap
**Date**: 2026-03-16
**Context**: Stage 0 (C# compiler) emitted polymorphic functions as C# generic methods (`map_list<T0, T1>`). Stage 1 (Codex-in-Codex compiler) emitted `object` for all type variables, causing type degradation — Stage 1 output couldn't compile itself because generic type info was lost.
**Decision**: Update the Codex-side C# emitter (`CSharpEmitter.codex`) to emit generics matching Stage 0: `TypeVar(id)` → `T{id}`, `generic-suffix` collects type variable IDs from a definition's type, `emit-def` appends `<T0, T1, ...>` after method names, type definitions thread `tparams` through to emit generic type parameter suffixes, `emit-type-expr-tp` maps type parameter names to `T{index}` via `find-tparam-index`.
**Rationale**: The bootstrap requires Stage 1 output to have the same type fidelity as Stage 0. Without generics, `List<T0>` degrades to `List<object>`, `Func<T0, T1>` to `Func<object, object>`, and the Stage 2 output can't compile. The Codex-side emitter is purely functional — type variable collection uses index-based loops and list accumulation, following the project's coding patterns.
**Consequences**: Stage 0 and Stage 1 now emit identical generic signatures (e.g., `map_list<T0, T1>(Func<T0, T1> f, List<T0> xs)`). `Codex.Codex/out/Codex.Codex.cs` has only 4 `object` references (all correct: `NothingTy` emission, effectful `main` return, `do` block wrapper). The remaining gap to full bootstrap is the Stage 1 type checker resolving concrete types — the emitter is no longer the bottleneck.

---

## Decision: Column-Based `when` Branch Scoping
**Date**: 2026-03-16
**Context**: Nested `when` expressions (e.g., `when a if X -> when b if Y -> …`) had the inner match greedily consuming branches belonging to the outer match. This produced incorrect C# where only the first outer branch was emitted — a critical bug for self-hosting since the Codex source uses nested matches heavily (e.g., `unify_structural`).
**Decision**: The parser now records the column of the first `if` keyword in a `when` expression and only accepts subsequent `if` keywords at the same column (or on the same line for inline matches). This distinguishes inner vs outer branches without requiring explicit delimiters.
**Rationale**: Column-based scoping is consistent with Codex's existing indentation sensitivity. Alternatives considered: (1) explicit `end` keyword — verbose and un-Codex-like; (2) parenthesization — adds noise. The column rule is invisible to the programmer when code is properly formatted, which is always the case in practice.
**Consequences**: Nested `when` expressions now emit all branches correctly. This required a companion fix: unique pattern binding names in the C# emitter (via `m_matchCounter`) to avoid C# variable name clashes between outer and inner matches in the same expression scope. A third fix in `Lowering.cs` was also needed: `LowerMatch` now infers its result type from the first non-error branch body when `expectedType` is `ErrorType`, matching how `LowerIf` already works.

---

## Decision: x86-64 Frame Layout — Callee-Saved Pushes Before Sub RSP
**Date**: 2026-03-23
**Context**: The x86-64 backend's prologue emitted `push rbp; mov rbp,rsp; sub rsp,frameSize; push callee-saved`. Spill offsets in `StoreLocal`/`LoadLocal` were computed as `rbp - (slot+1)*8 - calleeSavedCount*8`, assuming callee saves sat immediately below rbp. But because `sub rsp` came first, callee saves were below the frame space. When a function used >5 locals (triggering spills), spill slot 0 at `[rbp-48]` overwrote saved R14 — causing silent corruption in recursive functions like factorial.
**Decision**: Reorder prologue to `push rbp; mov rbp,rsp; push callee-saved; sub rsp,spillFrame`. Epilogue uses `lea rsp, [rbp - 40]` to skip spill space before popping callee-saved regs.
**Rationale**: This matches the standard System V AMD64 convention where callee-saved registers are at fixed offsets from rbp. The spill offset formula `rbp - (slot+1)*8 - 5*8` now correctly places spills below the 5 callee-saved registers (rbx, r12-r15) without collision.
**Consequences**: Stack layout is clean. Spill slot 0 at `[rbp-48]`, safely below saved r15 at `[rbp-40]`. No changes to `StoreLocal`/`LoadLocal` offset math needed — they already computed the right offsets, the prologue was just putting the saved registers in the wrong place.
