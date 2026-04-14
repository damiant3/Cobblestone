# Inline Const Syntax

## Problem

Simple constant declarations today require two lines with the name
repeated:

```
cdx-expected-token-kind : Integer
cdx-expected-token-kind = 1000
```

For a registry of 60+ constants, this is half noise. The type is trivial
(`Integer`, `Text`), the name is repeated for no reason, and the reader
has to scan two lines to learn one fact.

## Proposed surface syntax

Allow an inline form for value definitions that have no parameters:

```
cdx-expected-token-kind : Integer = 1000
```

Equivalent to the current two-line form. A function with parameters keeps
the two-line shape since the body often depends on multi-line layout:

```
make-error : Integer -> Text -> Diagnostic
make-error (code) (msg) = Diagnostic { ... }
```

The inline form is permitted only when there are no parameters between
the `:` and the `=`. Mixing parameters on one line (`f : T -> T (x) = ...`)
is not allowed — it reads badly and makes error recovery harder.

## Grammar

Current (approximate):

```
Definition := TypeAnnotation Newline+ Name Params? '=' Expr
            | Name Params? '=' Expr
```

Proposed:

```
Definition := TypeAnnotation '=' Expr              -- inline, zero-param only
            | TypeAnnotation Newline+ Name Params? '=' Expr
            | Name Params? '=' Expr
```

The inline branch binds the name from the annotation and requires no
parameter list. If a parameter list is attempted on the annotation line,
that's a parse error with a message pointing to the current two-line form.

## Implementation notes

### Reference compiler (`src/Codex.Syntax/Parser.cs:411` `TryParseDefinition`)

After `ParseTypeAnnotation()` succeeds, if `Current.Kind == Equals`,
treat this as the inline form: `nameToken = annotation.Name`, parameters
empty, consume `=`, parse body, emit `DefinitionNode`. Otherwise fall
through to the existing two-line path that expects a re-declared name.

Small edit: ~10-15 lines.

### Self-host parser (`Codex.Codex/Syntax/Parser.codex:113` `parse-definition`)

Mirror the same branch. Same size.

### Tests

- Positive: `c : Integer = 1000`, `greeting : Text = "hi"`, inside a
  Section, at chapter top level.
- Positive: mixed file with both inline and two-line forms in the same
  section.
- Negative: `c (x) : Integer = 1000` (params on annotation line) → error.
- Negative: `c : Integer = ` (missing body) → error.
- Round-trip: pretty-printer emits inline form for zero-param defs with
  simple-looking bodies (optional; printer can keep two-line form and
  still be correct).

## Compatibility

Strictly additive. All existing source continues to parse. The
pretty-printer / Codex text emitter can keep emitting the two-line form
until we decide on a style rule — no forced migration.

## Status

Designed. Not implemented. Land on its own feature branch
(`hex/inline-const-syntax` suggested) so review and rollback stay
clean. Sequenced after the CDX registry work.
