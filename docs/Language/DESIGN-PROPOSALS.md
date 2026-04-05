# Language Design Proposals

Proposed enhancements to the Codex language. These are additive — they don't
break existing code. Prioritized by impact on readability, correctness, and
bare-metal performance.

---

## P1: Multi-Pattern Matching (`|` in branches)

**Problem:** Repeated identical branches in `when` expressions.

```
when op
  if OpAdd -> infer-arithmetic st lt rt
  if OpSub -> infer-arithmetic st lt rt
  if OpMul -> infer-arithmetic st lt rt
  if OpDiv -> infer-arithmetic st lt rt
  if OpPow -> infer-arithmetic st lt rt
```

**Proposed:**

```
when op
  if OpAdd | OpSub | OpMul | OpDiv | OpPow -> infer-arithmetic st lt rt
```

**Impact:** High readability, moderate implementation. Parser recognizes `|`
between patterns in a branch. Desugarer can expand to individual branches
(simplest) or keep grouped (emitter optimizes to jump table). Affects ~20
functions across the compiler.

**Implementation:**
- Parser: recognize `|` after a pattern, before `->`, collect alternatives
- Desugarer: expand `P1 | P2 -> body` into separate AMatchArms
- No emitter changes needed (expansion handles it)

---

## P2: Tuple Patterns

**Problem:** Nested `when`-inside-`when` for matching two values simultaneously.

```
types-equal (a) (b) =
  when a
    if IntegerTy -> when b
      if IntegerTy -> True
      if _ -> False
    if NumberTy -> when b
      if NumberTy -> True
      if _ -> False
    ...
```

**Proposed:**

```
types-equal (a) (b) =
  when (a, b)
    if (IntegerTy, IntegerTy) -> True
    if (NumberTy, NumberTy) -> True
    if (TypeVar id-a, TypeVar id-b) -> id-a == id-b
    if _ -> False
```

**Impact:** High readability for type equality, unification, and similar
two-argument dispatch. Requires tuple type support.

**Implementation:**
- Parser: recognize `(expr, expr)` as tuple construction
- Type system: add TupleTy or use anonymous records
- Pattern matching: desugar tuple patterns to nested matches (simplest)
- Alternative: pair-based, `when pair a b` without a full tuple type

---

## P3: Set Type with O(1) Lookup

**Problem:** Membership tests use if-else chains (50 comparisons for
`is-cs-keyword`) or list-contains (O(n) linear scan).

```
is-cs-keyword (n) =
  if n == "class" then True
  else if n == "static" then True
  ... 50 more lines ...
  else False
```

**Proposed:**

```
cs-keywords : Set Text
cs-keywords = set-from-list ["class", "static", "void", "return", ...]

is-cs-keyword : Text -> Boolean
is-cs-keyword (n) = set-contains cs-keywords n
```

**Impact:** Performance on bare metal. A hash set reduces 50 string comparisons
to 1. Critical for any function called per-identifier during compilation.

**Implementation options (in order of complexity):**
1. Sorted list + binary search (no new type, use existing bsearch-text-pos)
2. Hash set built-in (new type, hash function, bucket array)
3. Perfect hash for compile-time-constant sets (zero collisions)

**Short-term fix:** Sort the keyword list and use binary search. The
infrastructure already exists (bsearch-text-pos). This can be done today
without language changes.

---

## P4: Guard Clauses on Patterns

**Problem:** Some branches need both a pattern match AND a boolean condition.
Currently requires nested if-then-else inside the branch body.

```
when expr
  if SomeConstructor (x) ->
    if x > 0 then handle-positive x
    else handle-other x
```

**Proposed:**

```
when expr
  if SomeConstructor (x) when x > 0 -> handle-positive x
  if SomeConstructor (x) -> handle-other x
```

**Impact:** Moderate readability. Common in functional languages (Haskell,
OCaml, F#). Useful in the type checker and lowering where pattern + condition
combinations are frequent.

**Implementation:**
- Parser: recognize `when condition` after pattern, before `->`
- Desugarer: expand to `if condition then body else next-branch`
- No emitter changes needed

---

## P5: Tag Equality Built-in

**Problem:** Checking if two values of the same variant type have the same
constructor, without inspecting fields. Currently requires exhaustive
nested `when` (see `types-equal` in Unifier.codex — 30 lines).

**Proposed:**

```
types-equal (a) (b) =
  when (a, b)
    if (TypeVar id-a, TypeVar id-b) -> id-a == id-b
    if _ -> tag-equal a b
```

Where `tag-equal` returns True if both values have the same constructor tag.

**Impact:** Moderate. Useful in unification, equality checks, and any
variant type dispatch. On bare metal, this is a single integer comparison
on the tag word.

**Implementation:**
- Built-in function `tag-equal : a -> a -> Boolean`
- Bare-metal: compare first word (constructor tag) of both values
- C#: compare GetType() or use pattern matching

---

## P6: String Interpolation

**Problem:** String construction uses verbose concatenation chains.

```
"Expected " ++ show expected ++ " but found " ++ show actual
```

**Proposed:**

```
"Expected {show expected} but found {show actual}"
```

**Impact:** Readability in emitters and diagnostic messages. The CSharpEmitter
and CodexEmitter have dozens of multi-part string concatenations.

**Implementation:**
- Lexer: recognize `{` inside string literals as interpolation start
- Parser: parse interpolated segments as expressions
- Desugarer: expand to `++` concatenation (simplest) or dedicated IR node
- Note: must not conflict with record literal syntax `Name { field = ... }`

---

## P7: Map Type (Key-Value)

**Problem:** No associative data structure. The compiler uses linear list
scans for lookups (rename maps, type environments, arity maps). On bare
metal, these are O(n) per lookup.

**Proposed:**

```
env : Map Text CodexType
env = map-from-list [("x", IntegerTy), ("y", BooleanTy)]

lookup : Map Text CodexType -> Text -> CodexType
lookup (m) (key) = map-get m key
```

**Impact:** High performance on bare metal. Type environment lookups, rename
map lookups, and arity lookups are the hottest paths in the compiler. A hash
map would reduce these from O(n) to O(1).

**Implementation:**
- Hash map with open addressing (simple, cache-friendly)
- Hash function on CCE strings (multiply-accumulate on byte values)
- Bare-metal: allocate bucket array on heap, inline hash + probe
- Linear types ensure single-owner semantics (no aliasing issues)

---

## P8: Exhaustiveness Checking for `when`

**Problem:** Missing branches in `when` expressions are caught at runtime
(`Non-exhaustive match` exception) rather than at compile time. The compiler
can't guarantee total functions.

**Proposed:** The type checker verifies that `when` covers all constructors
of the scrutinee type. Missing constructors produce a compile-time warning
(or error, by policy).

**Impact:** Correctness. A self-hosting compiler on bare metal cannot afford
runtime match failures. This is the type-theoretic equivalent of "no patch
is possible at that distance."

**Implementation:**
- Collect all constructors for the scrutinee type
- Subtract the matched constructors from `when` branches
- Report unmatched constructors as diagnostics
- Handle wildcard `_` as covering all remaining constructors
- Handle nested patterns (constructor within constructor)

---

## P9: Constant Folding and Dead Code Elimination

**Problem:** Expressions like `char-code-at "a" 0` are evaluated at runtime
even though both arguments are constants. On bare metal, this means a
function call where a single integer would suffice.

**Proposed:** The lowering pass evaluates constant expressions at compile
time and replaces them with literal values.

**Impact:** Performance on bare metal. The lexer and parser have many
`char-code-at` calls on constant strings. Folding these to integer
literals eliminates function calls and string allocations.

**Implementation:**
- Detect IrApply where function and all arguments are literals
- Evaluate built-in functions on constants (char-code-at, text-length, etc.)
- Replace with IrIntLit/IrTextLit result
- Conservative: only fold pure built-ins, never user functions

---

## P10: Tail Call Optimization in Self-Hosted Compiler

**Problem:** The reference C# compiler detects tail calls and emits while
loops. The self-hosted Codex emitter does not — recursive functions on bare
metal grow the stack. With 1161 defs to process, deep recursion during
compilation risks stack overflow.

**Proposed:** The self-hosted C# emitter (`CSharpEmitterExpressions.codex`)
detects self-recursive tail calls and emits while loops with variable
reassignment, matching the reference compiler's TCO behavior.

**Impact:** Required for large compilations on bare metal. The current 2MB
stack HWM is near the limit. TCO would reduce this to constant stack for
recursive processing loops.

**Implementation:**
- Detect `should-tco` in the self-hosted emitter (already exists in C#)
- Emit `while (true) { ... }` with variable reassignment for tail calls
- Emit normal function call for non-tail positions
- Already implemented in reference C# emitter — port the logic

---

## Implementation Priority

| Phase | Feature | Effort | Impact |
|-------|---------|--------|--------|
| Now | Sorted keywords + bsearch (no language change) | 1 hour | Perf |
| Soon | P1: Multi-pattern `\|` | 1-2 days | Readability |
| Soon | P8: Exhaustiveness checking | 2-3 days | Correctness |
| Soon | P10: Self-hosted TCO | 2-3 days | Bare-metal safety |
| Next | P2: Tuple patterns | 2-3 days | Readability |
| Next | P9: Constant folding | 1-2 days | Perf |
| Later | P3: Set type | 3-5 days | Perf |
| Later | P7: Map type | 1-2 weeks | Perf |
| Later | P4: Guard clauses | 1-2 days | Readability |
| Later | P5: Tag equality | 1 day | Convenience |
| Later | P6: String interpolation | 2-3 days | Readability |
