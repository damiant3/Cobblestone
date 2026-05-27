# Language Gaps

Features promised in the vision and design documents that are not yet
implemented. Ordered by impact on daily use of the language.

## Tier 1 — Type System Foundations

These block generic programming patterns and limit expressiveness.

### 1. Type Classes / Traits — IN PROGRESS

**Status (CL 2491):** `class`/`instance` syntax parses. Desugarer
generates dictionary record types, instance values, dispatch functions
(arity-aware), and per-instance specialized methods. Constraint syntax
(`Showable a =>`) parses and is stored in AST. Tests pass.

**What works now:**
- `class Showable where to-text : Integer -> Text`
- `instance Showable Integer where to-text (x) = show x`
- `to-text 42` — implicit dispatch (single-instance classes)
- `to-text-Integer 42` — explicit specialized call
- `to-text Showable-dict-Integer 42` — explicit dict (multi-instance)
- `is-equal 3 3` — multi-param methods work
- `Showable a =>` constraint syntax parses

**Remaining:**
- Multi-instance implicit resolution (type checker picks instance
  from concrete argument type when >1 instance exists)
- Polymorphic method signatures (class methods with type variables)
- Superclass constraints, deriving

**Blocked by:** Type checker work for multi-instance resolution.

### 2. Higher-Kinded Types

**Vision:** Implied by type class design (e.g., `Functor` needs
`* -> *` kinds).

**Scope:** Kind inference, kind checking, `Type -> Type` parameters
in type class definitions.

**Blocked by:** Type classes (#1).

### 3. GADTs (Generalized Algebraic Data Types)

**Vision:** Implied by dependent types. Currently only `PropEqTy`
exists as a special case.

**Scope:** User-defined indexed type families. Constructor return
types may refine the index. The unifier already handles PropEqTy;
generalize.

**Blocked by:** Nothing directly, but HKTs (#2) make them more useful.

### 4. Linear Type Enforcement

**Vision:** Language Design doc describes `linear T`. Syntax parses.
Tests exist but are `.skip`.

**Scope:** CDX2061 (use after consume) and CDX2062 (aliasing) in
the type checker. Phase 3 linearity tracking as described in
DevelopersGuide.

**Blocked by:** Nothing — the syntax and type representation exist.

### 5. Freeze

**Vision:** DevelopersGuide says `freeze` converts `mutable T` to `T`,
consuming the mutable reference.

**Scope:** Single keyword, type rule in the checker, trivial codegen
(copy or identity depending on representation).

**Blocked by:** Linear type enforcement (#4) for soundness — without
linearity, freeze can alias the mutable reference.

## Tier 2 — Expressiveness

Not blockers, but the language is less pleasant without them.

### 6. Lazy Evaluation

**Vision:** Language Design doc describes `lazy` annotation with
memoization.

**Scope:** Thunk allocation, force-on-access, memoization cell.
Requires heap allocation strategy (thunk = closure + flag + cached
value).

**No longer blocked.** The compiler crash (GPF in `name-value` when
a mutable record had a function-typed field) is resolved in the
current seed (CL 2490). Test `mutable-fn-field.codex` exercises
construction, function field calls, integer and function field
mutation — all pass. Ready for thunk/lazy implementation.

### 7. Refinement Types (beyond bounded integers)

**Vision:** Language Design doc. Currently only `Integer between L and H`
has static range checking.

**Scope:** Arbitrary predicates on types, lifted into the type checker.
The static bounds prover is a template — generalize its range lattice
to other domains.

**Blocked by:** Nothing, but the prover complexity grows quickly.

### 8. Intersection and Union Types

**Vision:** Language Design doc describes `a & b` and `a | b`.

**Scope:** Subtyping rules, type narrowing in pattern match arms,
width subtyping for records.

**Blocked by:** Complicates unification significantly.

### 9. Tuple Types

**Vision:** Language Design doc describes `(a, b, c)`.

**Scope:** Sugar for anonymous records with positional fields.
Could desugar to `record { _0 : a, _1 : b, _2 : c }`.

**Blocked by:** Nothing — purely syntactic.

### 10. Vector with Length Index

**Vision:** Language Design doc describes `Vector (n : Integer) (a : Type)`.

**Scope:** Dependent type over List with compile-time length tracking.
The bounds prover can reason about lengths.

**Blocked by:** GADTs (#3) for the clean formulation.

## Tier 3 — Module System

### 11. ~~Export Lists~~ — REMOVED

Removed from the gap list. Export lists hide definitions, which
conflicts with the literate programming model — books don't have
hidden pages. All definitions remain public.

### 12. Selective Imports — DONE

Already implemented. `cites Quire chapter Name (x, y)` syntax is
parsed by `parse-selected-names` in Parser.codex. The ChapterScoper
handles selective name resolution via `cite-selects-name`. Was
undocumented; discovered during gap analysis.

## Tier 4 — Infrastructure

### 13. Content-Addressed Repository Protocol

**Vision:** NewRepository.txt — replace Git/Perforce with immutable
facts, proposals, verdicts, trust lattice.

**Scope:** Large. DiskFacts and FactStore exist in the foreword.
The Trust modules exist. The transport protocol (TrustTransport) is
designed. Gap #6 in CurrentPlan.md.

**Blocked by:** First-boot ceremony (gap #2), network stack maturity.

### 14. Narrator / Historian IDE Tools

**Vision:** NewRepository.txt — Narrator explains code in plain
language, Historian shows full evolution of definitions.

**Scope:** Requires agent integration, AST traversal for explanation,
and repository history queries.

**Blocked by:** Repository protocol (#13), agent acquisition (gap #3).

## Tier 5 — Advanced Type Theory

### 15. Phantom Types

**Scope:** Types with unused type parameters for tagging. Trivial
to add — the type checker just needs to not reject unused type vars.

### 16. Session Types

**Vision:** Implied by capability model and channel types.

**Scope:** Protocol-level typing for channels. Each send/receive step
refines the channel type. Deep feature — intersects with linearity (#4)
and effect rows.

**Blocked by:** Linear types (#4).

---

## What NOT to do

This list is ordered. Do not jump to Tier 3+ before Tier 1 is solid.
Type classes (#1) are the single highest-impact gap — they unlock
generic programming, which makes the foreword libraries composable
instead of monomorphic. Start there.
