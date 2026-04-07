# Closure Escape Analysis

**Status**: Shipped (Steps 1-4 complete)
**Date**: 2026-03-26

---

## Problem

CDX2043 currently warns when a linear variable is captured by a closure:

```
capture : linear FileHandle -> (Integer -> [FileSystem] Nothing)
capture (h) = \x -> close-file h
```

This warns because the closure might be called zero times (leaking `h`) or
multiple times (double-close). But it's only a warning — the programmer can
ignore it. The goal is to **make this safe by construction**, not by hope.

---

## What Makes Closure Capture Dangerous

A linear variable must be consumed exactly once. When a closure captures one,
the consumption shifts from the definition site to the call site:

```
-- Safe: closure called exactly once
let f = \x -> close-file h
in f 42

-- Unsafe: closure never called (h leaked)
let f = \x -> close-file h
in 42

-- Unsafe: closure called twice (h double-closed)
let f = \x -> close-file h
in (f 1, f 2)

-- Unsafe: closure given to something that calls it many times
map (\x -> close-file h) some-list
```

The problem reduces to: **how many times is the closure invoked?**

---

## Key Insight: Linear Closures

A closure that captures a linear variable is itself linear — it must be used
exactly once. This is the same insight as Rust's `FnOnce`: if you move an
owned value into a closure, the closure can only fire once.

We don't need full escape analysis. We need **linear propagation through
closures**:

1. A lambda that captures a linear variable becomes a linear value.
2. The linearity checker tracks it like any other linear binding.
3. Calling the closure counts as consumption (exactly once).
4. CDX2040 (unused) and CDX2041 (used more than once) enforce correctness.

---

## Current State

The `LinearityChecker` already has the machinery:

| Diagnostic | What it checks | Severity |
|-----------|----------------|----------|
| CDX2040 | Linear variable never used | Error |
| CDX2041 | Linear variable used more than once | Error |
| CDX2042 | Inconsistent usage across branches | Error |
| CDX2043 | Linear variable captured by closure | **Warning** |

CDX2040-2042 are errors. CDX2043 is the gap — it warns but doesn't enforce.

---

## Proposed Changes

### Step 1: Promote CDX2043 to error

Change `m_diagnostics.Warning("CDX2043", ...)` to `m_diagnostics.Error(...)`.
Any closure that captures a linear variable is rejected unless the compiler
can prove safety.

### Step 2: Track closures as linear bindings

When a lambda captures a linear variable, the lambda itself becomes a linear
value. If it's bound in a `let`:

```
let f = \x -> close-file h    -- f is now linear (captures linear h)
in f 42                        -- f consumed once: OK
```

Implementation: in `CheckLambdaExpr`, after detecting CDX2043, mark the
lambda's binding (if in a `let` context) as linear in `m_linearBindings`.
The existing CDX2040/CDX2041 checks then enforce exactly-once usage of `f`.

### Step 3: Allow direct application (no binding)

The common safe pattern is applying the closure immediately:

```
(\x -> close-file h) 42       -- OK: closure created and consumed in one step
```

This is already safe — the closure never escapes. No special handling needed
because CDX2043 only fires when the closure is a lambda expression, and direct
application means the linear variable `h` is consumed in the body.

Actually, in the current checker, direct application like `(\x -> close-file h) 42`
would check the lambda body and see `h` used. The `h` usage is inside the
lambda scope, and the lambda is immediately applied — but the checker doesn't
see the application, it sees the lambda. So CDX2043 would still fire.

Fix: in `CheckExpr` for `ApplyExpr`, if the function is a `LambdaExpr`, check
the body directly without the closure-capture check. The lambda is never stored
— it's consumed at the call site.

### Step 4: Higher-order functions with single-use guarantees

The hard case:

```
with-file "log.txt" (\h -> write h "hello")
```

The closure captures `h` (linear), so it's linear. But `with-file` calls it
exactly once. How does the compiler know?

Option A: **Effect-annotated callbacks.** `with-file` declares its callback
parameter as `linear (FileHandle -> [FileSystem] Nothing)`. The type system
enforces that `with-file` uses the callback exactly once.

Option B: **Trust `with-file` as a builtin.** The compiler knows certain
higher-order functions guarantee single-use. Pragmatic but not general.

Option C: **Defer.** Make the programmer use direct application for now.
Most use cases are `let f = ... in f x` or immediate application. The
higher-order case can wait.

**Recommendation**: Option C now, Option A later. ~~Direct application and
let-binding cover the practical cases. Higher-order linear callbacks are
a language feature unto themselves.~~

**Update**: Option A shipped (2026-03-26). `linear` function types enforce
exactly-once consumption. The `LinearityChecker` resolves parameter types
at call sites and allows linear-capturing lambdas when the target parameter
is `LinearType`. 6 tests validate the behavior. See `LinearityChecker.cs`
`TryResolveExprType` and the `ApplyExpr` case for lambda arguments.

---

## Implementation Plan

| Step | What | Effort | Risk |
|------|------|--------|------|
| 1 | CDX2043 warning → error | One line | Low — may break existing code | **Shipped** |
| 2 | Linear closure bindings in `let` | ~20 lines in LinearityChecker | Low | **Shipped** |
| 3 | Direct application bypass | ~10 lines in CheckExpr | Low | **Shipped** |
| 4 | Higher-order linear callbacks | ~40 lines (TryResolveExprType + ApplyExpr case) | Low | **Shipped** |

All four steps shipped 2026-03-26. Step 4 implemented via `TryResolveExprType`
which resolves function parameter types through curried application chains.

---

## Verification

### Existing tests

```
Linear_captured_by_closure_warns     → becomes: Linear_captured_by_closure_errors
Linear_not_captured_no_warning       → unchanged
```

### New tests needed

```
Linear_closure_let_used_once_ok      → let f = \x -> close-file h in f 42
Linear_closure_let_used_twice_error  → let f = \x -> close-file h in (f 1, f 2)
Linear_closure_let_unused_error      → let f = \x -> close-file h in 42
Linear_closure_direct_apply_ok       → (\x -> close-file h) 42
Linear_closure_passed_to_map_error   → map (\x -> close-file h) xs
```

---

## Connection to Other Systems

**Regions** (CAMP-IIIA): Closure escape analysis determines whether a closure
can be safely allocated in a region. A linear closure (used once) can live in
the current region — it won't outlive it. A non-linear closure might escape
and must be heap-allocated. The two analyses compose.

**Safe mutation** (SAFE-MUTATION.md): `list-snoc` linearity is currently
programmer-verified. Once closure escape analysis is in place, the compiler
can verify that the accumulator list isn't aliased — any closure capturing
it would be flagged.

**Codex.OS**: On bare metal with region-based allocation, closure linearity
determines memory safety. A linear closure in a region can be stack-allocated.
This is the path to zero-allocation higher-order programming.
