# Codex Syntax Quick Reference

For agents. Read this before writing test sources or samples.

## Definitions

```
square : Integer -> Integer
square (x) = x * x

add : Integer -> Integer -> Integer
add (x) (y) = x + y
```

## Records

```
Person = record {
  name : Text,
  age : Integer
}

get-name : Person -> Text
get-name (p) = p.name

main : Text
main = get-name (Person { name = "Alice", age = 30 })
```

## Sum Types (Variants)

```
Shape =
  | Circle (radius : Integer)
  | Rect (width : Integer) (height : Integer)
```

## Pattern Matching

```
describe : Shape -> Text
describe (s) =
  when s
    is Circle (r) -> "circle"
    is Rect (w) (h) -> "rect"
    is otherwise   -> "unknown"
```

`when` / `is` — not `match` / `case`. Wildcard: `is otherwise -> ...`
or `is _ -> ...`. Literal patterns work too:

```
classify : Integer -> Text
classify (n) =
  when n
    is 0 -> "zero"
    is 1 -> "one"
    is otherwise -> "other"
```

## Effects

```
main : [Console] Nothing
main = print-line "hello"

greet : Text -> [Console, FileSystem] Nothing
greet (name) = act
  contents <- read-file "template.txt"
  print-line (contents ++ name)
end
```

## Act Blocks (statement sequencing)

```
main : [Console] Nothing
main = act
  name <- read-line
  print-line ("Hello, " ++ name)
end
```

`act ... end` replaced the former `do` keyword. Inside an act block,
newlines separate statements. Outside, newlines are whitespace — multi-line
function applications work everywhere.

## If/Then/Else

```
abs : Integer -> Integer
abs (n) = if n < 0 then negate n else n
```

## Let Bindings

```
main : Integer
main = let x = 10 in let y = 20 in x + y
```

## Booleans

`True` / `False` — capital T/F.

## Identifiers

Names may contain hyphens: `my-function`, `elf-ident-32`, `patch-4-loop`.
A hyphen followed by a letter or digit continues the name. Subtraction
requires spaces: `x - 1` (expression), not `x-1` (identifier).

## Operators

Arithmetic: `+` `-` `*` `/` (spaces required around `-` for subtraction)
Comparison: `==` `/=` `<` `>` `<=` `>=`  (note: `/=`, not `!=`)
Boolean:    `&` (and) `|` (or)            (single-char, not `&&`/`||`)
Text concat / list append: `++`
List cons:  `::`

Unicode equivalents accepted by the lexer: `→` for `->`, `←` for `<-`,
`≡` for `===`, `≠` for `/=`, `≤` for `<=`, `≥` for `>=`, `⊢` for `|-`,
`⊗` for `(**)`, `∀` for `forall`, `∃` for `exists`.

## Effect Declarations

```
effect Console where
  print-line : Text -> [Console] Nothing
  read-line  : [Console] Text
```

## Subtypes — Bounded Integers

`Integer between L and H` is a refinement subtype of `Integer` with a
compile-checked range. It's the canonical way to express "byte", "u16",
"port number", "0..255", etc.

```
Byte = record {
  val : Integer between 0 and 255
}

Port = record {
  num : Integer between 0 and 65535
}
```

Plain `Integer` arithmetic produces a plain `Integer`, which won't fit
into a bounded slot. Use `__narrow` to assert the value is in range
(checked at runtime — out-of-range traps):

```
make-byte : Integer -> Byte
make-byte (n) = Byte { val = __narrow n }

bump : Byte -> Byte
bump (b) = make-byte (b.val + 1)
```

For record updates that keep a bounded field bounded, combine
`__record-set` (the immutable record-update builtin) with `__narrow`:

```
inc-counter : Counter -> Counter
inc-counter (c) = __record-set c "n" (__narrow (c.n + 1))
```

The old `Integer in L..H` syntax was retired. `between` is the only form.

## Linear Types

```
open-file : Text -> [FileSystem] linear FileHandle
close-file : linear FileHandle -> [FileSystem] Nothing
```

## Pitfalls

**Contextual keywords.** `act`, `end`, and `qed` are contextual: they
only act as keywords in record-scoped positions (inside an act block,
after a proof body, etc.). Elsewhere they're valid identifiers.

**Inline if/then/else in arithmetic.** The Codex emitter does not
preserve parentheses around if expressions. An expression like
`64 + (if w then 8 else 0) + (if r then 4 else 0)` will be
re-emitted without parens, changing the parse. Use let bindings:

```
let wv = if w then 8 else 0
in let rv = if r then 4 else 0
in 64 + wv + rv
```

**Long ++ chains.** A single expression with many ++ concatenations
creates a deep IR tree that increases type-checker time during
self-compilation. Break long chains into named helpers:

```
part-a : List Integer
part-a = [1, 2, 3, 4, 5]

part-b : List Integer
part-b = [6, 7, 8, 9, 10]

whole : List Integer
whole = part-a ++ part-b
```

**Deeply nested lets.** A function with 20+ chained let bindings
creates deep scope nesting for the type checker. Split into smaller
functions that each handle a coherent piece of work.
