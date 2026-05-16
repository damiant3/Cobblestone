# Safe Mutation in a Functional Language

**Date**: 2026-03-25
**Status**: Implemented (list-snoc), principle established for future builtins

---

## The Principle

Codex is functional — values are immutable, functions are pure, the type system
enforces capabilities at the boundaries. But the runtime is C# `List<T>`, not
Haskell cons cells. Every `acc ++ [x]` copies the entire list. In a loop, that's
O(n²). The Lexer was 1,738ms because of this.

The fix: **mutation is safe when ownership is linear.**

`list-snoc acc x` calls `List<T>.Add(x)` in-place — O(1) amortized instead of
O(n). It's a mutable operation. It's safe because the accumulator in a tail-recursive
loop is consumed exactly once. Nobody else holds a reference to it. There's no
aliasing, no sharing, no observable side effect.

This is not a hack. It's the same insight behind Rust's ownership model, Clean's
uniqueness types, and linear logic. You don't need to forbid mutation — you need
to forbid *sharing* of mutable values.

## The Builtins

| Builtin | C# emission | Complexity | Safety requirement |
|---------|-------------|------------|-------------------|
| `list-snoc xs x` | `xs.Add(x); return xs` | O(1) amortized | `xs` must be linearly owned |
| `list-insert-at xs i x` | `new List(xs); Insert(i, x)` | O(n) | Pure — copies the list |
| `text-compare a b` | `string.CompareOrdinal(a, b)` | O(min(m,n)) | Pure |

`list-insert-at` is the safe (copying) version. `list-snoc` is the fast (mutating)
version. Use `list-insert-at` when you need to preserve the original list. Use
`list-snoc` when you're building a list in a loop and the old value is dead.

## Safe Patterns

```
-- SAFE: acc is consumed once in the tail call, never referenced again
tokenize-loop next (list-snoc acc tok)
map-list-loop f xs (i + 1) len (list-snoc acc (f (list-at xs i)))

-- SAFE: acc is consumed once in each branch
if i == len then acc
else loop (i + 1) (list-snoc acc (process (list-at xs i)))

-- UNSAFE: acc is used twice (passed to g AND continued in the loop)
let _ = g acc
in loop (i + 1) (list-snoc acc x)

-- UNSAFE: acc is stored in a record that outlives the loop
SomeRecord { items = list-snoc acc x, ... }
```

## Connection to the Bigger Picture

This is the smallest instance of a pattern that scales to the entire Codex.OS
architecture (see `docs/Designs/DistributedAgentOS.txt`):

- **Capabilities are controlled mutation.** `[Console]` grants the ability to
  mutate the terminal. `[FlightControl]` grants the ability to mutate actuators.
  `list-snoc` grants the ability to mutate a list. Same shape.

- **Safety comes from ownership, not prohibition.** The type system doesn't
  forbid writing to the console — it requires the caller to hold the capability.
  `list-snoc` doesn't forbid mutation — it requires the caller to own the list
  linearly.

- **The local agent must work without the cloud.** The offline agent compiles
  code on-device using `list-snoc` and sorted binary search — no network, no
  GC pressure, bounded memory. The same mutation discipline that makes a 24ms
  lexer makes a real-time flight control loop.

## Future Work

- **Compiler-enforced linearity.** Today, linear ownership of `list-snoc` targets
  is verified by the programmer. A future escape analysis pass (CDX2043) could
  verify it automatically — flag any `list-snoc` call where the list is aliased.

- **Array type.** `Array<T>` with O(1) indexed update would generalize this
  pattern. The HAMT failed because Codex has no O(1) array update — every
  "update" copies. A mutable array with linear ownership would enable efficient
  persistent data structures.

- **Region-scoped mutation.** Allocate a mutable builder inside a region, fill
  it, freeze it on region exit. Regions + mutation + linearity = safe,
  allocation-free, real-time-compatible computation. The pieces (regions, TCO,
  list-snoc) already exist separately.
