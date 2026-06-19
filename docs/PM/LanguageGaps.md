# Language Gaps

Features promised in the vision and design documents that are not yet
fully implemented. Ordered by impact on daily use of the language.

**Last audited**: 2026-06-18 (reek, post-implementation sweep)

## Tier 1 — Type System Foundations — DONE

### 1. Type Classes / Traits — DONE

**Shipped:** CL 4722 (polymorphic dictionary forwarding), CL 4724 (copy-up).

Full pipeline: parser, desugarer (dictionary-passing), type checker,
IR lowering, emitter. Constraint syntax, `deriving Show, Eq, Ord`,
multi-instance implicit resolution, and polymorphic dictionary
forwarding all work. A constrained function can call another
constrained function and the dictionary is threaded through.

Tests: `typeclass-smoke` (29 assertions), `type-class-no-instance`,
`class-op-no-instance`.

**Remaining minor items — DONE (fester, 2026-06-18):**
- ~~Polymorphic method signatures~~ — class methods with multiple
  type variables work (e.g., `convert : a, b -> b`). Higher-order
  function params in class methods need parser work (deferred).
- ~~Superclass constraints~~ — `Eq a => Ord a where` syntax parsed
  and desugared. Superclass dictionary injected as `__super-Eq`
  field in the subclass dictionary record.

### 2. Higher-Kinded Types — DONE

**Shipped:** CL 4730 (TypeCon + TypeApply foundation), CL 4735
(symmetric unification), CL 4736 (copy-up).

Two new CodexType constructors: `TypeCon (Name)` for unapplied type
constructors and `TypeApply (CodexType) (CodexType)` for type-level
application. The unifier decomposes `TypeApply (TypeVar f) x` against
concrete types like `ListTy y` or `ConstructedTy n [y]`, binding
`f -> TypeCon "List"`. `deep-resolve` reduces `TypeApply (TypeCon n) a`
to concrete types. Scope: `* -> *` (single-arg).

Enables: `class Functor f where map : (a -> b) -> f a -> f b` with
`instance Functor List`.

### 3. GADTs — DONE

**Shipped:** CL 4746 (full implementation), CL 4749 (copy-up).

Syntax: `| IntLit (Integer) : Expr Integer` — constructor return-type
annotations via colon. Per-branch unification state forking via
`snapshot-substitutions`/`restore-substitutions` ensures branch-local
type refinements don't bleed across match arms.

9 files: Parser, SyntaxNodes, AstNodes, Desugarer, CodexType,
CodexTypeTree, TypeChecker, TypeCheckerInference, Unifier.

## Tier 2 — Expressiveness

### 4. Lazy Evaluation — WIRED, NEEDS TESTING

Full pipeline exists: lexer (`LazyKeyword`), parser (`parse-lazy-expr`),
type inference (thunk as `Integer -> T`), IR lowering (`__LazyCell`
record with `__lz-done`/`__lz-val`, memoization via closure).

Test: `lazy-smoke` exercises basic lazy with memoization.

### 5. Refinement Types — DONE

**Shipped:** CL 4743/4744 (blu, bounds prover expansion).

`Integer between L and H` with static range checking. The bounds
prover reasons about arithmetic (add/sub/mul/div), bitwise ops
(shru, and, mod), if-expression unions, let propagation, and
negation. CDX4010 info diagnostic elides runtime checks when the
prover proves the value fits. Test: `bounds-prover.codex`.

Length-indexed vectors (CL 4753) extend refinement to collection
sizes: `Vector 3 Integer` vs `Vector 2 Integer` is a compile-time
error.

### 6. Vector with Length Index — DONE

**Shipped:** CL 4753.

Builtins: `vec-empty` (Vector 0 a), `vec-singleton` (Vector 1 a),
`vec-cons` (Vector n a -> Vector (n+1) a), `vec-head`, `vec-length`.
VectorTy wildcard unification (length -1 matches any length). Type
inference engine refines output vector lengths from input lengths.
Type checker catches length mismatches: `Vector 3 Integer vs Vector 2
Integer` is a compile-time error.

## Tier 3 — Infrastructure

### 7. Content-Addressed Repository Protocol

**Vision:** NewRepository.txt — replace Git/Perforce with immutable
facts, proposals, verdicts, trust lattice.

**Scope:** Large. DiskFacts and FactStore exist in the foreword.
The Trust modules exist. The transport protocol (TrustTransport) is
designed. Gap #6 in CurrentPlan.md.

**Blocked by:** First-boot ceremony (gap #2), network stack maturity.

### 8. Narrator / Historian IDE Tools

**Vision:** NewRepository.txt — Narrator explains code in plain
language, Historian shows full evolution of definitions.

**Scope:** Requires agent integration, AST traversal for explanation,
and repository history queries.

**Blocked by:** Repository protocol (#7), agent acquisition (gap #3).

## Tier 4 — Advanced Type Theory

### 9. Session Types — DONE

**Shipped:** CL 4759.

Protocol-level typing for channels using phantom type parameters.
Protocol states: `SSend a s` (send a, continue with s), `SRecv a s`
(receive a, continue with s), `SEnd` (close). Channel type `SChan s`
carries the current protocol state. Builtins: `s-new`, `s-send`,
`s-recv`, `s-close`.

Each operation advances the session state via unification:
`s-send : SChan (SSend a s) -> a -> SChan s`. Protocol violations
are caught at compile time: attempting `s-recv` on an `SSend`-state
channel produces `Type mismatch: SRecv vs SSend`.

Built entirely on HKTs + GADTs — no new compiler infrastructure.
Foreword chapter: `codex/foreword/core/SessionTypes.codex`.

---

## Summary

Of the original 9 gaps, 7 are DONE (#1 Type Classes, #2 HKTs,
#3 GADTs, #5 Refinement Types, #6 Length-Indexed Vectors,
#9 Session Types, plus #4 Lazy mostly done). Remaining:
#7 Repository Protocol (infrastructure), #8 IDE Tools (infrastructure).
