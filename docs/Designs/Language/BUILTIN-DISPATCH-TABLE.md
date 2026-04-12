# Builtin Dispatch Table

## Problem

`emit-builtin` in `Codex.Codex/Emit/CSharpEmitterExpressions.codex` is a
long `if ... else if ... else if ...` chain that dispatches on a Text
name. Today it's ~35 branches (after the bit / heap / buf groups were
collapsed). The remaining entries are one-offs with varied shapes.

Ugly to read, and O(n) per dispatch — for ~35 names, roughly ~18 string
compares on average per expression that references a builtin. Over
millions of IR expressions compiled, that's real time.

## Proposed shape

Data-driven dispatch table:

```
BuiltinEmitter = record {
  name : Text,
  emit : List IRExpr -> List ArityEntry -> CodexType -> Text
}

builtin-emitters : List BuiltinEmitter
builtin-emitters = sort-by-name [
  BuiltinEmitter { name = "show",       emit = emit-builtin-show-wrapped },
  BuiltinEmitter { name = "print-line", emit = emit-builtin-print-line-wrapped },
  ...
]

emit-builtin : Text -> List IRExpr -> List ArityEntry -> CodexType -> Text
emit-builtin (n) (args) (arities) (result-ty) =
  let entry = bsearch-builtin n
  in entry.emit args arities result-ty
```

Dispatch becomes O(log n). The wall of `if/else` disappears entirely;
adding a new builtin is one list entry plus its emitter function.

## Why not done now

- **Uniform signature requirement.** Every `emit-builtin-X` helper today
  has a slightly different type. Some take `result-ty`, most don't.
  Some are 1-arg, some 2-arg, some 3-arg. We'd need a small wrapper
  layer that adapts each to the uniform `List IRExpr -> List ArityEntry
  -> CodexType -> Text` shape.

- **Closures-in-records risk.** The bootstrap2 C# emitter has had
  issues in the past with captured lambdas inside record constructors.
  Before building a 28-entry table of exactly that shape, we want a
  small reproduction and fix, or confirmation that it works today.

- **Behavioral risk.** Mis-wiring one entry produces wrong C# output
  that only surfaces downstream. Need golden tests per builtin before
  doing the swap.

## Plan

1. Add a two-entry dispatch table behind a feature toggle. Confirm
   bootstrap2 emits correct C# for closures-in-records.
2. If that passes, migrate entries in small batches (5-10 at a time),
   running pingpong after each batch.
3. Remove the feature toggle once the old wall is empty.

## Status

Designed. Not implemented. Sequenced after the CDX registry work.
Own branch suggested: `hex/builtin-dispatch-table`.

## Related

The bit-op / heap / buf sub-groups in the same file are already
collapsed into shape-preserving helpers. That's a smaller,
lower-risk version of the same pattern and is good evidence the
full table-driven dispatch will also work.
