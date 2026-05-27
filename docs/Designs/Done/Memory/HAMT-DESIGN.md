# HAMT: Hash Array Mapped Trie for Codex

**Date**: 2026-03-19 (verified via system clock)
**Status**: Design → Implementation
**Philosophy**: Own your foundations.

---

## Why

`Map<K,V>` and `Set<T>` are the two most-used data structures in the Codex compiler.
Every type environment, every symbol table, every substitution map, every lowering
context — they all flow through `Map<K,V>`. Today, both are thin wrappers around
`System.Collections.Immutable.ImmutableDictionary<K,V>`:

```csharp
public sealed class Map<TKey, TValue>
{
    readonly ImmutableDictionary<TKey, TValue> m_inner;
    // ... 40 lines of delegation
}
```

This means:

1. **Every project in the solution** depends on `System.Collections.Immutable` (31 files
   reference it directly).
2. **We don't control the data structure.** When we compile to JavaScript, Python, Rust,
   Go, Ada, Fortran, COBOL, or Babbage — there is no `ImmutableDictionary`. Each backend
   must provide its own equivalent, or we emit `Dictionary` and lose immutability.
3. **The self-hosted compiler** uses `List` with linear search for all its "maps" —
   O(n) lookup everywhere — because there's no immutable trie in Codex.

Replacing `ImmutableDictionary` with a **Hash Array Mapped Trie (HAMT)** written in
Codex solves all three problems. The HAMT is the same data structure that Clojure,
Scala, Haskell, and .NET's own `ImmutableDictionary` use internally — but we own it,
we can compile it to all 12 backends, and the self-hosted compiler gets O(log₃₂ n)
lookups instead of O(n).

---

## What is a HAMT?

A Hash Array Mapped Trie is a persistent (immutable, structure-sharing) hash table
that uses the bits of a key's hash code to navigate a tree of small arrays.

### The Core Idea

Given a 32-bit hash:

```
hash = 0b 01101 11010 00101 10011 01110 00001 10
         ───── ───── ───── ───── ───── ───── ──
         lvl6  lvl5  lvl4  lvl3  lvl2  lvl1  lvl0
```

Each 5-bit chunk (values 0–31) selects one of 32 possible children at each level.
The trie has at most 7 levels (5 × 7 = 35 > 32 bits).

### Nodes

Each internal node contains:

- A **bitmap** (32 bits): which of the 32 possible children are present.
- A **compact array**: only the present children, packed contiguously.

To find child at logical index `i`:
1. Check if bit `i` is set in the bitmap: `bitmap & (1 << i) != 0`
2. If yes, count the set bits below `i` to get the physical array index:
   `popcount(bitmap & ((1 << i) - 1))`
3. Index into the compact array.

This gives O(1) lookup per level, O(log₃₂ n) total — effectively O(1) for any
realistic collection size (log₃₂(1,000,000) ≈ 4).

### Structure Sharing

When inserting a key:
1. Walk down the trie using the hash bits.
2. At the leaf or empty slot, create the new entry.
3. Copy only the **path** from root to the changed node (O(log₃₂ n) nodes).
4. All other subtrees are shared with the old version.

This is what makes it persistent/immutable — the old map still exists, unchanged,
sharing almost all its structure with the new map.

---

## Design for Codex

### Types

```
HamtNode a =
  | HamtEmpty
  | HamtLeaf (Integer) (Text) (a)
  | HamtCollision (Integer) (List HamtEntry a)
  | HamtBranch (Integer) (List HamtNode a)

HamtEntry a = record {
  key : Text,
  value : a
}

HamtMap a = record {
  root : HamtNode a,
  size : Integer
}
```

- `HamtEmpty`: The empty trie.
- `HamtLeaf hash key value`: A single key-value pair.
- `HamtCollision hash entries`: Multiple keys with the same hash (rare).
- `HamtBranch bitmap children`: An internal node with a bitmap and packed children.

### Key Type Restriction

For Tier 0, keys are `Text` (strings). This is what the compiler needs — all our
maps are `Map<string, T>`. We use a simple FNV-1a or DJB2 hash, implementable in
pure Codex with integer arithmetic.

### Operations

```
hamt-empty : HamtMap a

hamt-get : HamtMap a -> Text -> Maybe a

hamt-set : HamtMap a -> Text -> a -> HamtMap a

hamt-remove : HamtMap a -> Text -> HamtMap a

hamt-contains : HamtMap a -> Text -> Boolean

hamt-size : HamtMap a -> Integer

hamt-fold : (b -> Text -> a -> b) -> b -> HamtMap a -> b

hamt-to-list : HamtMap a -> List HamtEntry a
```

### Hash Function

DJB2 is simple and good enough:

```
djb2-hash : Text -> Integer
djb2-hash (s) = djb2-loop s 0 5381

djb2-loop : Text -> Integer -> Integer -> Integer
djb2-loop (s) (i) (h) =
  if i == text-length s then h
  else djb2-loop s (i + 1) (h * 33 + char-code (char-at s i))
```

Pure integer arithmetic. Works on all 12 backends.

### Bit Operations

The HAMT needs three bit operations:
- `bit-and` (mask extraction)
- `bit-shift-right` (hash chunk extraction)
- `popcount` (array index computation)

These are the only primitives that need backend support. The reference compiler can
use C#'s built-in operators. The self-hosted compiler needs `bit-and`, `bit-shift-right`,
and `popcount` as built-in functions (or we implement `popcount` as a loop).

### Implementation Plan

**Tier 0** (this task): Implement the HAMT in pure Codex as a prelude module.
Use a simplified approach — if bit operations aren't available yet, use
integer division and modulo to extract hash chunks:
- `extract-chunk hash level` = `(hash / (32 ^ level)) mod 32`
- `popcount` via loop (count set bits)

This is slower than native bit ops but correct and portable.

**Tier 1** (next): Add `bit-and`, `bit-shift-right`, `popcount` as built-in
functions in the compiler, emitted as native operations on each backend.
Swap the HAMT internals to use them.

**Tier 2** (future): Replace `Map<K,V>` in `Codex.Core` with the HAMT.
All 31 files that reference `System.Collections.Immutable` switch to the
Codex-native implementation. Kill the dependency.

---

## Complexity

| Operation | HAMT | Current (List scan in .codex) | ImmutableDictionary |
|-----------|------|-------------------------------|---------------------|
| Lookup | O(log₃₂ n) ≈ O(1) | O(n) | O(log₃₂ n) |
| Insert | O(log₃₂ n) ≈ O(1) | O(n) | O(log₃₂ n) |
| Delete | O(log₃₂ n) ≈ O(1) | O(n) | O(log₃₂ n) |
| Memory | Shared subtrees | Full copy on every change | Shared subtrees |

For the self-hosted compiler's type environments (~50–200 bindings), this means
going from O(n²) to O(n log n) for type checking. Real speedup.

---

## What This Kills

Once Tier 2 is complete:

- ❌ `System.Collections.Immutable` — removed from all 31 files
- ❌ `ImmutableDictionary` — replaced by `HamtMap`
- ❌ `ImmutableArray` — replaced by Codex `List` (already happening)
- ❌ `ImmutableHashSet` — replaced by `HamtSet` (HAMT with unit values)
- ✅ All 12 backends get the same persistent map implementation
- ✅ Self-hosted compiler gets O(log₃₂ n) lookups
- ✅ One less BCL dependency in the entire solution

---

## Relationship to CCE

The HAMT's hash function operates on text. If text is stored as CCE bytes
internally (per CCE-DESIGN.md), the hash function operates on CCE byte
sequences — which are denser and more uniformly distributed than UTF-8 for
multilingual keys. This is a natural synergy.

---

## The Razor

.NET asked: how do we provide a general-purpose immutable dictionary?
They answered with `ImmutableDictionary<K,V>` — excellent, battle-tested.

We ask: how do we provide an immutable dictionary that compiles to 12 backends,
runs in the self-hosted compiler, and has zero external dependencies?

The answer is: own the trie.
