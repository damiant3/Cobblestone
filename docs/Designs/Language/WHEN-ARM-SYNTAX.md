# When-Arm Syntax: `is` Patterns and `otherwise` Wildcard

## Problem

Inside a `when` expression, each arm today reads:

```
when k
 if LeftBrace -> True
 if _ -> False
```

Two issues:

1. **`if` is overloaded.** The same keyword is used for boolean
   conditionals (`if cond then X else Y`) and for pattern-match arms
   (`if pattern -> X`). To a reader — especially one coming from
   procedural languages — these look like the same construct but
   behave differently: one takes a Boolean, the other takes a
   pattern. The ambiguity is real and costs reading time.

2. **`_` is cryptic.** The wildcard pattern inherits from academic
   pattern-matching tradition (ML, Haskell, Rust, Scala). It's terse
   and doesn't bind a name, but it's meaningless to someone who
   hasn't internalized the convention. "Why is there an underscore
   there? Is that a variable?"

## Proposed surface syntax

Replace both:

```
when k
 is LeftBrace -> True
 is otherwise -> False
```

- `is` introduces a pattern-match arm. Reads aloud correctly: "when k
  is LeftBrace, return True; when k is otherwise, return False."
- `otherwise` is the wildcard pattern. Self-documenting, matches any
  remaining case. The word itself tells the reader "all other cases."

Longer pattern examples stay natural:

```
when expr
 is LitExpr (tok) -> emit-lit tok
 is NameExpr (tok) -> emit-name tok
 is AppExpr (f) (a) -> emit-app f a
 is otherwise -> emit-error expr
```

## Grammar

Current:

```
WhenArm := 'if' Pattern '->' Expr
Pattern := ... | '_'
```

Proposed:

```
WhenArm := 'is' Pattern '->' Expr
Pattern := ... | 'otherwise'
```

`if` inside `when` becomes a parse error pointing the user at `is`.
`_` in pattern position becomes a parse error pointing the user at
`otherwise`. Both old forms can be retained as deprecated-but-working
for one migration cycle if we want a gentle rollout.

## Implementation notes

### Reference compiler

- `src/Codex.Syntax/Parser.Expressions.cs` — the `ParseMatch` /
  `ParseWhen` path accepts `IfKeyword` today. Add `IsKeyword` (new
  token) as an accepted alternative, or replace outright.
- `src/Codex.Syntax/Lexer.cs` — lex `is` as `IsKeyword`. Already lexed
  as `Identifier`? Then keyword-promote in a late pass.
- `src/Codex.Syntax/Parser.Expressions.cs` — the wildcard pattern parse
  accepts `Underscore` today. Add `OtherwiseKeyword` as an accepted
  alternative.

### Self-host parser

Mirror edits in `Codex.Codex/Syntax/Parser.codex` and
`Codex.Codex/Syntax/ParserExpressions.codex`. Add the matching token
kind in `Codex.Codex/Syntax/Token.codex`.

### Migration sweep

Every existing `.codex` file uses `if Pat -> E` inside `when`. This is
a mechanical `sed`-style sweep over all `.codex` sources. Reference
compiler's own C# source isn't affected.

### Tests

- Positive: `when x is Ctor -> ...`, `is otherwise -> ...`, nested
  `when` inside a `when` arm, all parse.
- Negative (once old forms are dropped): `if Ctor ->` inside `when`
  errors with a helpful "use `is` inside `when`" message.
- Round-trip: pretty-printer emits the new form.

## Compatibility

Two-phase rollout recommended:

1. **Additive phase.** Accept both `if` / `is` and both `_` /
   `otherwise`. Emit a deprecation warning on the old forms. Do the
   sweep across all `.codex` sources to the new forms.
2. **Cleanup phase.** Remove the old forms from the grammar. Now
   `if` means exactly one thing (boolean conditional), and `_` is no
   longer a pattern.

## Reference-compiler lock

Feature addition to the frozen reference compiler. Requires an entry
in `docs/Active/Compiler/REFERENCE-COMPILER-LOCK.md` when implemented,
justifying why the ref compiler is being modified after freeze
(user-requested readability improvement; low-risk keyword-swap;
valuable before MM4 so the self-host is cleaner from the start).

## Status

Designed. Not implemented. Own branch suggested:
`hex/when-arm-syntax`. Sequenced after CDX registry and diagnostics
Phase 1 land.
