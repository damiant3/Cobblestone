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
    if Circle (r) -> "circle"
    if Rect (w) (h) -> "rect"
```

`when`/`if` — not `match`/`case`. Wildcard: `if _ -> ...`

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
Comparison: `==` `!=` `<` `>` `<=` `>=`
Boolean: `&&` `||`
Text concat: `++`

## Effect Declarations

```
effect Console where
  print-line : Text -> [Console] Nothing
  read-line  : [Console] Text
```

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
