# Language Design Proposals

> **Filed to Done 2026-07-15 (val):** eight of the ten proposals shipped; the two that remain — P5 (tag equality) and P6 (string interpolation) — are tracked in BACKLOG 2.10. Moved out of Active to keep init light; reopen if P5/P6 are picked up.

Proposed enhancements to the Codex language. These are additive — they don't
break existing code.

**Eight of the original ten shipped.** What remains is P5 (tag equality)
and P6 (string interpolation). The shipped proposals are recorded below
with where they landed, so nobody re-proposes them; their full text is
gone.

## Shipped

| # | Proposal | Where it landed |
|---|---|---|
| P1 | Multi-pattern `\|` in branches | `Syntax/Parser.codex` (~451) + `collect-alt-patterns` / `after-alt-pattern` in `Syntax/ParserExpressions.codex`; alternatives carry an `alt-group` on `MatchArm` |
| P2 | Tuple patterns | `TuplePat` / `TupleType` in `Syntax/SyntaxNodes.codex`, parsed in `Syntax/Parser.codex` (~364, ~229), desugared to `MkTupN` constructor patterns in `Ast/Desugarer.codex` (~256, ~293) |
| P3 | Set type | `codex/foreword/core/Set.codex` |
| P4 | Guard clauses on patterns | `parse-match-branch-body` in `Syntax/ParserExpressions.codex` (~494). Shipped syntax is `is <pat> when <cond> -> <body>` (the sketch here said `if`; the language settled on `is`), and it composes with `\|` alternatives |
| P7 | Map type | `codex/foreword/core/Hamt.codex` + `KvStore.codex` |
| P8 | Exhaustiveness checking for `when` | `check-match-exhaustiveness`, diagnostic CDX2070 |
| P9 | Constant folding | `fold-constants-in-chapter` in `IR/Lowering.codex`, run in the LOWER phase |
| P10 | Tail call optimization | `should-tco` in `Emit/X86_64.codex`. (The proposal below was written against the retired C# emitter; TCO shipped natively in the self-hosted x86-64 emitter, and again in the ARM64 and RISC-V plugs.) |

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
"Expected " & show expected & " but found " & show actual
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

## Implementation Priority

Only two proposals are left, and neither is urgent — both are
convenience, not correctness or performance. The correctness (P8),
performance (P3, P7, P9, P10), and readability (P1, P2, P4) proposals
have all landed.

| Feature | Effort | Impact |
|---------|--------|--------|
| P5: Tag equality | 1 day | Convenience |
| P6: String interpolation | 2-3 days | Readability |

Note on P5: its motivating example (`types-equal` in `Unifier.codex`)
is now writable with the tuple patterns and `|` alternatives that
shipped as P2 and P1, which takes most of the pressure off. `tag-equal`
would still save the wildcard arm, but it is a smaller win than it was
when this list was written.
