# Codex Developer's Guide

## Source Structure

```
Chapter: Name
  cites Quire chapter ChapterName

 Prose goes at column 2 (one space indent).

Section: SubName

  code-at-column-3-or-deeper : Type -> Type
  code-at-column-3-or-deeper (x) = x
```

Chapters = modules. Sections = sub-modules. `cites` = imports.
Entry point: `opening` (not `main`).

**Prose does not reach the CDX.** Two sources differing only in a prose
block compile to a byte-identical CDX, at chapter level and at section
level; changing a printed string literal in the same file does change it,
which is the control that says the comparison is not blind. So a
prose-only edit cannot move the seed, however many chapters it touches.
Measured 2026-07-28 against seed `C28DA27668475BAD`, after the opposite
was assumed from pingpong's byte-identical text round-trip and written
into a changelist description.

### Quire Name Resolution for `cites`

The quire name in `cites <QuireName> chapter <Chapter>` is the **last
segment** of the directory name, capitalized:

| Directory | Quire Name |
|-----------|-----------|
| `codex.foreword` | `Foreword` |
| `codex.foreword.game` | `Game` |
| `codex.os.net` | `Net` |
| `codex.os.replay` | `Replay` |
| `codex.os.dev` | `Dev` |
| `codex.os.sched` | `Sched` |
| `codex.os.kernel` | `Kernel` |
| `apps/games/magic` | `Magic` |

For intra-quire references (chapter A citing chapter B in the same
quire), use the quire's own name:

```
Chapter: ReplayVerifier
  cites Replay chapter ReplayLog    -- same quire (codex.os.replay)
  cites Net chapter MessageFraming  -- cross-quire (codex.os.net)
  cites Foreword chapter Sha256     -- foreword library
```

## Identifiers

- Values: `kebab-case` (e.g., `compute-balance`, `list-push`)
- Types: `PascalCase` (e.g., `Account`, `LinkedList`)
- No underscores in user code. `__double-underscore` = compiler intrinsic.

Names may contain hyphens: `my-function`, `elf-ident-32`, `patch-4-loop`.
A hyphen followed by a letter or digit continues the name. Subtraction
requires spaces: `x - 1` (expression), not `x-1` (identifier).

### A name may be written in any language

A letter is a letter whatever its script. `café`, `año` and `дом` are
identifiers, and so are names built from Greek, Arabic, Hebrew, Devanagari and
Latin Extended letters. This is not a courtesy: the founding document asks for
a language that exists for human reading, and a name a reader cannot write in
their own alphabet fails that on the first line.

**This was half true until 2026-07-27 and the half that was missing was the
cheap one.** The lexer already accepted multi-byte characters through their own
path, so Greek and Arabic names worked. What it refused were the **31 letters
CCE carries in a single byte** -- sixteen accented Latin (`é è ê ë á à â ä ó ô
ö ú ü ñ ç í`) and fifteen Cyrillic -- because `is-letter` tested one band,
13..64, and those letters sit at 97..127. So `дом` compiled and `café` did not.

The reason it is two bands and not one wider band is worth stating, because the
obvious fix is wrong: **CCE's punctuation sits between the letters.** 13..38 is
the ASCII lowercase, 39..64 the ASCII uppercase, **65..96 the punctuation and
symbols**, then the accented and Cyrillic letters. Extending the top to 127
would make `.` and `(` letters.

`codex/test/ident-letters.codex` pins it and `build/oracle-cce.ps1` adjudicates
the predicates against the host.

**Case conversion does not follow.** `to-upper` is the identity on those 31
letters, deliberately: the uppercase of `é` is a Tier 1 code point and
`to-upper` answers a `Char`, which carries a Tier 0 byte, so the answer does not
fit in the return type. It answers the letter unchanged rather than a wrong one.

## Types

| Form | Example |
|------|---------|
| Function (1 param) | `Integer -> Text` |
| Function (multi) | `Integer, Integer -> Integer` |
| Effectful | `Path -> [FileSystem] Result Text` |
| Record | `record { name : Text, age : Integer }` |
| Variant | `| Ok (Integer) \| Err (Text)` |
| List | `List Integer` |
| Maybe | `Maybe Text` |
| Bounded integer | `Integer between 0 and 255` |
| Bounded + overflow | `Integer between 0 and 255 wrapping` |
| Unit type | `Second = unit Integer` |
| Bounded + unit | `Second between 0 and 3600` |
| Linear | `linear FileHandle` |
| Vector | `Vector 4 Real` |
| Vector (bounded) | `Vector 16 (Integer between 0 and 255)` |
| Vector mask | `VectorMask 4` |
| Real (f64) | `Real` |
| Real approx (f32) | `Real approximate` |
| Real + safety | `Real trapping`, `Real saturating` |

## Definitions

```
  name : Type -> Type
  name (param) = body
```

Type annotation and definition on separate lines. Parameters
parenthesized individually: `f (a) (b)` not `f (a, b)`.

Multi-parameter functions use commas in the type signature:

```
  add : Integer, Integer -> Integer
  add (x) (y) = x + y
```

The last `->` separates parameters from return type. Higher-order
parameters use parens: `map : (a -> b), List a -> List b`.

## Records

```
  Person = record {
   name : Text,
   age : Integer
  }

  p = Person { name = "Alice", age = 30 }
  p.name                           -- field access
  __record-set p "name" "Bob"      -- in-place field store; returns p
```

No `{ record | field = val }` sugar. Use `__record-set` to update a field.

**`__record-set` is not a functional update.** It stores into the field
and returns *the same record* (`emit-record-set-builtin`), so every
holder of that record sees the change. This doc called it "functional"
until 2026-07-16, and the type checker's environment was written against
that reading: `env-bind-local` did `__record-set env "locals" ...`
believing it produced a new env, and instead wrote each binding into its
*caller's* environment. Locals then outlived their scope, and the
checker rejected valid programs.

It is a controlled concession, and the condition on it is real: it is
sound only while a single owner is threaded linearly through the value
(`VisionAndVirtues.md`, virtue 5). **A callee does not own a record its
caller still holds.** When the caller keeps using the value afterwards,
build a new record with the constructor and copy the fields -- and copy
any list you carry over, because `list-push` / `list-set-at` /
`list-insert-at` are in-place under capacity and would be shared by both
records (see Lists, below).

### Mutable Records

Prefix `mutable` on a record type to allow in-place field assignment:

```
  mutable Counter = record {
   value : Integer
  }

  c = Counter { value = 0 }
  c.value = c.value + 1              -- in-place field assignment
```

Field assignment (`r.field = expr`) is valid anywhere, not just in
act blocks; a field-assign may also precede the result in a let/def
body and the statements sequence. The type checker rejects field
assignment on immutable records (CDX2060). Mutable records use
`__record-set` under the hood, and the compiler enforces **unique
ownership**: a `mutable` record is read-free but may be passed on,
aliased, or returned at most once per path -- handing it to two owners
is an error (CDX2062). Borrow-vs-move is inferred from callee
signatures (a function that only reads it borrows; one that threads it
onward consumes). See the Linear Types section: `linear` is for
resources (exactly-once), `mutable` is for data (no aliasing); they
are orthogonal disciplines.

`freeze : linear a -> a` converts a uniquely-owned value to an ordinary
immutable one, consuming the source. Because the source is unique and
spent, no copy is needed -- `freeze` is the identity at runtime.

## Variants (Sum Types)

A constructor's fields are **positional types, not named bindings**. The
parentheses hold a type; the name comes from the pattern that takes it
apart.

```
  Shape =
   | Circle (Integer)
   | Rect (Integer) (Integer)

  area : Shape -> Integer
  area (s) = when s
   is Circle (r) -> r * r * 3
   is Rect (w) (h) -> w * h
```

**`Circle (radius : Integer)` does not compile** -- it is CDX1000 at the
colon, because the parser is reading a type there and a colon is not one.
This page carried exactly that example until 2026-07-16 and nothing caught
it: the compiler is the only reader that would have, and no chapter in the
tree writes a variant that way, so there was nothing to contradict. If you
want the fields named, that is what a record is for -- give the constructor
one as its payload (`| Circle (CircleDims)`), or name the variables at each
`when`. Records are where names live.

## Pattern Matching

```
  when expr
   is Pattern1 -> body1
   is Pattern2 -> body2
   is otherwise -> default
```

`when` / `is` -- not `match` / `case`. Wildcard: `is otherwise -> ...`
or `is _ -> ...`. Exhaustiveness checked. Patterns: `VarPat`, `LitPat`,
`CtorPat`, `_` (wildcard). Literal patterns work too:

```
  classify : Integer -> Text
  classify (n) =
    when n
      is 0 -> "zero"
      is 1 -> "one"
      is otherwise -> "other"
```

### Adding a variant: `is otherwise` absorbs it and no checker will say so

Exhaustiveness is checked, so adding a constructor to a variant type raises
CDX2070 at every `when` that ENUMERATES its arms. **A `when` ending in
`is otherwise` is exhaustive by construction, so it takes the new variant
silently down the default path** and the compiler has nothing to report.

The cost is that the failure surfaces far from the cause. Adding
`KeyEcdsaP384` to `X509KeyAlg` raised CDX2070 in four tests that listed their
variants and nothing at all in `x509-key-params-ok`, whose EC arm ends in a
catch-all: every P-384 certificate then stopped parsing three stages
downstream. The same shape cost the tree its `^` operator, where
`emit-binary-op` had no `IrPowInt` arm and the catch-all returned RAX
untouched, so `^` compiled clean and answered garbage.

**When you add a variant, grep every `when` on that type and read the
catch-alls by hand.** The compiler covers only the enumerated ones.

## Let Bindings

```
  let x = expr1
  in let y = expr2
  in x + y
```

## If/Then/Else

```
  if condition then true-branch else false-branch
```

Always requires `else`. No dangling if.

## For Expressions

```
  for x in xs -> f x
```

Sugar for `map-list`. The body is a function applied to each element.
Desugars to `map-list (lambda (x) -> f x) xs`.

The separator is an arrow, not `do`. `parse-for-expr` reads the variable
and `in`, then `finish-for-list` requires `is-arrow` -- there is no `do`
branch, so `for x in xs do f x` is a parse error (CDX1000 at the `do`).

## Effects and Act Blocks

```
  greet : Text -> [Console] Nothing
  greet (name) = act
   print-line ("Hello, " & name)
  end
```

`act`/`end` delimits effectful blocks. `<-` binds effectful results:

```
  act
   line <- read-line
   print-line line
  end
```

Inside an act block, newlines separate statements. Outside, newlines
are whitespace -- multi-line function applications work everywhere.

Effect declarations:

```
  effect Console where
    print-line : Text -> [Console] Nothing
    read-line  : [Console] Text
```

## Operators

| Op | Meaning |
|----|---------|
| `+` `-` `*` `/` `^` | Arithmetic |
| `==` `/=` `<` `>` `<=` `>=` | Comparison (note: `/=`, not `!=`). `==`/`/=` are errors on Real types (CDX2085). |
| `~` `~0` | Approximate equality (4 ULP default), bitwise exact (`~0`). For Real and Vector types. |
| `&` | Text/list append |
| `\|` | Boolean or |
| `::` | List cons |
| `->` | Function type arrow |
| `<-` | Effect bind |

Unicode equivalents accepted by the lexer: `→` for `->`, `←` for `<-`,
`≡` for `===`, `≠` for `/=`, `≤` for `<=`, `≥` for `>=`.

## Division and the Two Remainders

Integer `/` **truncates toward zero**, which is what `idiv` does and what
C, Java, Go, Rust and Zig mean by `/`. `-7 / 2` is `-3`, not `-4`.

There are two remainders and picking the wrong one is silent.

| | Rounding | Sign of result | Pairs with `/` |
|---|---|---|---|
| `int-rem a b` | truncating | sign of the **dividend** | **yes** |
| `int-mod a b` | Euclidean | never negative | no |

```
  int-rem (-7) 3   -- -1        int-mod (-7) 3   -- 2
  int-rem 7 (-3)   --  1        int-mod 7 (-3)   -- 1
  int-rem (-7) (-3) -- -1       int-mod (-7) (-3) -- 2
```

`int-rem` is the one that satisfies the division identity, for every
pair including negatives:

```
  a == (a / b) * b + int-rem a b
```

`int-mod` does not satisfy it and is not meant to: it is the Euclidean
remainder, always in `[0, |b|)`, which is what you want for indexing a
ring buffer or a colour wheel, where a negative answer would be a bug.
This is the same split Rust draws between `%` and `rem_euclid`.

Reach for `int-mod` when the answer indexes something. Reach for
`int-rem` when the answer has to agree with `/`. Pinned by
`codex/test/int-rem` and `codex/test/div-negative-pow2`.

## Negation

**The minus binds to whatever it abuts.** One symbol carries two meanings, so
the spaces around it are not optional -- they are what says which meaning you
want, and they say it the way a reader already reads it.

| written | the minus abuts | means |
|---|---|---|
| `a - 2` | neither side | subtraction |
| `a -2` | the right | `a` applied to `-2` |
| `a- 2` | the left | `a-` applied to `2` |
| `a-2` | both sides | the single identifier `a-2` |

So a negative works in argument position like any other literal, and needs no
parentheses:

```
  list-push acc -1         -- correct: acc and -1 are two arguments
  list-push acc (-1)       -- also correct, parens are never wrong
  list-push acc - 1        -- subtraction: (list-push acc) - 1
  -5                       -- literal negative
  -x                       -- negate a variable, in argument position too
  -(x + 1)                 -- negate a compound expression
  f a -b c                 -- three arguments; the negation takes ONE atom
```

`a-` is a legal name (a hyphen touching the name before it belongs to it), so
`a- 2` is an application. If you did not define `a-`, that is CDX3002
`Undefined name` and it names the thing you actually typed.

The one exception is `->`. A hyphen before `>` is the arrow, so `a->b` is
`a` `->` `b` and never the name `a-`.

**This page said the opposite until 2026-07-27** -- "negative literals in
argument position must be parenthesized", with `list-push acc -1` marked
WRONG. It was an accurate description of the parser and an inaccurate
description of the language: `is-signed-literal-atom` had implemented the
abutment rule for TYPES since long before, so `Integer between -1 and 255`
worked while the same literal in an expression did not.

The compiler folds `-(literal)` into `IrIntLit` at IR level.
There is also a `negate` builtin but the unary operator is preferred.

## Hex Literals

`#` followed by hex digits is an integer literal denoting a raw 64-bit
bit pattern. Underscores group digits. Case-insensitive.

```
  #FF                  -- 255
  #C8E6FF              -- a color, CSS notation verbatim
  #DEAD_BEEF           -- 3735928559
  #FFFFFFFFFFFFFFFF    -- all ones = -1 (bit-pattern semantics)
```

Up to 16 significant hex digits; more is CDX2071 (same code as a
decimal literal beyond the 64-bit range -- the compiler never silently
truncates a literal). Hash literals work anywhere an integer literal
does: expressions, pattern arms, `between` bounds. Use them for bit
masks, magic numbers, and colors -- domains whose references are
written in hex. Plain quantities stay decimal.

## Bounded Integers

```
  age : Integer between 0 and 150
  byte : Integer between 0 and 255 wrapping
  offset : Integer between -128 and 127 clamping
```

Overflow modes: `wrapping` (mod), `clamping` (saturate), `error`
(default, compile-time check on literals).

**A `wrapping` band must be exactly its hardware width** (u8, i8, u16,
i16, u32 or i32): `0 and 255`, `-128 and 127`, `0 and 4294967295`, and so
on. Anything narrower is CDX1073. `wrapping` is the mode that asks for
the modular arithmetic the machine already does, and the machine does it
at the width of the store -- a one-byte store wraps mod 256 whatever the
band says. Until 2026-07-28 a narrower band was accepted and quietly
meant that instead, so `between 0 and 100 wrapping` held 200 and read
`-1` back as 255. The static bounds prover reads the declared range off
the declaration and elides checks on the strength of it, so an
out-of-band value there puts a false range under every elision
downstream. `clamping` is unaffected and IS band-relative: `between 0 and
100 clamping` saturates at 100.

Plain `Integer` arithmetic produces a plain `Integer`, which won't fit
into a bounded slot. Use `__narrow` to assert the value is in range
(checked at runtime -- out-of-range traps):

```
  make-byte : Integer -> Byte
  make-byte (n) = Byte { val = __narrow n }
```

Bounds are enforced at the **function boundary**, not just at record
construction (BoundedSignatures, 2026-07-03). A bounded parameter or
return type is a real contract: a literal argument outside the range is
a static error (CDX2050), and a value whose range cannot be proven
raises **CDX2051** and the compile stops. Previously these were cosmetic
-- `inc-byte` declared `Integer between 0 and 255` could silently return
301. The static bounds prover elides the check when it can prove the
value fits (CDX2053).

**The compiler does not insert a runtime guard behind your back, and this
paragraph said it did until 2026-07-27.** It read "where the value cannot
be proven at compile time the callee inserts a precondition/postcondition
guard that traps at runtime (Eiffel-style design-by-contract)". Measured:
it refuses instead. `feed (x) = inc-byte x`, with `feed` taking a plain
`Integer`, does not compile:

```
error CDX2051: bounded parameter has bound 0..255 but the value's proven
range is -9223372036854775808..9223372036854775807; prove the value's
range or assert it with __narrow, which traps at runtime if violated
```

The runtime trap is real, and `__narrow` is how you ask for it: write
`inc-byte (__narrow x)` and an out-of-range value dies on a `UD2` at the
callee's entry guard, with a postcondition guard on the return in the
mirror case. So the design is refuse-by-default with an explicit opt-in,
not an implicit contract. Two tests written against the old reading
(`codex/test/bounded-param-trap`, `bounded-return-trap`) stopped
compiling when CDX2051 landed and nobody noticed, because both are
`.fatal` and nothing runs those.

## Unit Types

A `unit` declaration creates a distinct type wrapping another type.
The compiler erases the wrapper at codegen -- zero runtime overhead.

```
  Second = unit Integer
  Meter = unit Integer
```

Construction: `Second 42` creates a Second value.
Arithmetic preserves units: `Second 42 + Second 8 = 50`.
Scalar multiplication: `Second 42 * 3 = 126`.
Cross-unit is a type error: `Second + Meter` does not compile.

Unit types are transparent to their inner type at assignment
boundaries: a `Second` can be passed where `Integer` is expected.
But different unit types do not mix.

Bounded + unit composition works: `Second between 0 and 3600`
creates a bounded unit type.

Conversion declarations are parsed but not yet auto-applied:

```
  1 Minute = 60 Second
```

Write conversion functions manually:

```
  minute-to-second : Minute -> Second
  minute-to-second (m) = Second (m * 60)
```

## Unit Families

A `unit family` declaration creates a set of related units that share
a common base and convert automatically at construction time. The
family name is the type; member constructors multiply by their factor.

```
  Duration = unit family Nanosecond
    Nanosecond = 1
    Microsecond = 1000
    Millisecond = 1000000
    Second = 1000000000
```

Each member becomes a constructor function: `Second 5` produces
`5000000000` (5 * 1,000,000,000 nanoseconds). The family type
(`Duration`) is a `unit Integer` at runtime -- zero overhead.

```
  timeout : Duration
  timeout = Second 5

  precise : Duration
  precise = Microsecond 250
```

Extraction functions are synthesized automatically:
`Duration-to-Second : Duration -> Integer` divides by the factor.

All members share the same underlying type, so arithmetic works
across units: `Second 1 + Millisecond 500` = `1500000000` nanoseconds.

Families are organized by scale to avoid 64-bit overflow. Human-scale
durations (nanoseconds through hours) share `Duration`. Calendar-scale
durations (seconds through centuries) share `LongDuration`. The
standard families are defined in `codex/foreword/core/Units.codex`.

The desugarer erases `unit family` into a standard `unit Integer`
type plus constructor and extractor functions. Downstream phases
(type checker, IR, codegen) see only the erased form.

## Punctual Functions

A function marked `punctual` is proven to have bounded execution
at compile time. The compiler enforces five structural restrictions:

| CDX Code | Restriction |
|----------|------------|
| CDX6001 | Cannot call non-punctual or non-safe-builtin functions |
| CDX6002 | Cannot use heap allocation |
| CDX6003 | Cannot use closures or lambdas |
| CDX6004 | Must be effect-free (any effect in the signature is rejected) |
| CDX6005 | Cannot use self-recursion |

```
  punctual classify-threat : SensorReading -> ThreatLevel
  classify-threat (s) = ...
```

The emitter counts instructions per punctual function and reports
the count as CDX6010. An optional instruction budget warns when
exceeded (CDX6011):

```
  punctual 128 fast-handler : Integer -> Integer
  fast-handler (n) = n + 1
```

Default budget is 256 instructions. Budget is architecture-independent
(instruction count, not bytes or cycles). The compiler does not claim
to know wall-clock time -- that depends on clock speed and pipeline,
which is the system integrator's responsibility.

See `codex/test/examples/missile-warning.codex` for
a real-world example with Ada/Ravenscar side-by-side comparison.

## Proofs and Dependent Types

Codex has dependent types: types that carry values. The `===` operator
in type position creates a propositional equality type. Proof terms
inhabit these types; the unifier verifies them at compile time; the
emitter erases them (zero machine code).

### Propositional Equality

```
  nil-eq : Nil === Nil
  nil-eq = Refl
```

`Refl` proves `a === a` for any `a`. The unifier instantiates the type
variable and checks both sides are equal. An invalid proof is a type error:

```
  bad : Nil === Cons
  bad = Refl                 -- CDX2001: Type mismatch
```

### Proof Terms

| Term | Type | Meaning |
|------|------|---------|
| `Refl` | `forall a. a === a` | Reflexivity |
| `sym` | `forall a b. (a === b) -> (b === a)` | Symmetry |
| `trans` | `forall a b c. (a === b) -> (b === c) -> (a === c)` | Transitivity |
| `assume` | `Proof` | Axiom (unverified) |
| `cong` | `Proof -> Proof` | Congruence (degenerate) |

### Claim and Proof Syntax

`claim` declares a proposition. `proof` provides the evidence. `qed`
marks the end of the proof block. Claims at column 2 are parsed; the
proposition is stored as an annotation. Proofs compile as real
definitions and are erased at emit time.

```
  claim id-nil : Nil === Nil
  proof id-nil = Refl
  qed

  claim chain : 5 === 5
  proof chain = trans Refl Refl
  qed
```

Non-parametric claims (`claim name : prop`) create type annotations
via `PropEqTy`. The proof's body is checked against the annotation.

### The Proof Type

`Proof` is a first-class type name. Functions can take or return proofs:

```
  my-proof : Proof
  my-proof = assume

  parametric : Integer -> Proof
  parametric (x) = assume
```

### Proof Erasure

All definitions whose return type is `Proof` or `PropEqTy` are erased
during emit -- they produce no machine code. The compiler reports each
erasure with CDX4020.

### Static Bounds Prover

The compiler statically proves bounded-integer range safety. When it can
prove a value fits within a field's declared bounds, the runtime bounds
check (`cmp`/`jcc`/`ud2`) is elided and CDX4010 is emitted.

The prover recognizes these expression patterns:

| Pattern | Proven range |
|---------|-------------|
| Literal `n` | `[n, n]` |
| Field access `r.f` with `IntegerTy(lo, hi)` | `[lo, hi]` |
| `__narrow expr` | Propagates inner range |
| `a + b` (non-negative) | `[a.lo+b.lo, a.hi+b.hi]` |
| `a - b` (non-negative) | `[a.lo-b.hi, a.hi-b.lo]` |
| `a * b` (non-negative, overflow guard) | `[a.lo*b.lo, a.hi*b.hi]` |
| `a / b` (non-negative, b > 0) | `[a.lo/b.hi, a.hi/b.lo]` |
| `negate x` | `[-x.hi, -x.lo]` |
| `int-mod x n` (n > 0) | `[0, n-1]` |
| `bit-and x y` (non-negative) | `[0, min(x.hi, y.hi)]` |
| `bit-shru x n` (non-negative) | `[x.lo>>n.hi, x.hi>>n.lo]` |
| `if c then a else b` | Union of branch ranges |
| `let x = v in body` | Carries `v`'s range for `x` in body |

### Diagnostics

| Code | Severity | Meaning |
|------|----------|---------|
| CDX4010 | info | Bounds proven, runtime check elided |
| CDX4020 | info | Proof definition erased (compile-time only) |

## Linear Types

A `linear` value must be **used exactly once** on every path -- not
dropped (leak, CDX2063) and not reused (CDX2061). It is the discipline
for resources with a lifecycle: file handles, sockets, capabilities.

```
  open-file  : Text -> [FileSystem] linear FileHandle
  close-file : linear FileHandle -> [FileSystem] Nothing

  consume : linear Integer -> Integer
  consume (n) = n * 2          -- OK: used exactly once
                               -- `0`     -> CDX2063 (never used)
                               -- `n + n` -> CDX2061 (used twice)
```

`linear` and `mutable` are orthogonal uniqueness disciplines: `linear`
is exactly-once (resources -- every mention counts); `mutable` is
no-aliasing-with-free-reads (data -- see Mutable Records). `freeze :
linear a -> a` bridges them, consuming a uniquely-owned value and
returning a shareable immutable one (the identity at runtime).

Diagnostics: CDX2061 (linear used more than once / inconsistent across
branches / mentioned after a move), CDX2063 (linear never used -- leak),
CDX2062 (mutable record aliased), CDX2065 (linear passed to a plain
parameter), CDX2066 (linear returned with a plain return type),
CDX2067 (linear captured by a handler clause or escaping closure).

**Current enforcement scope (2026-07-03, LinearOwnership complete).**
The checker follows ownership through let-bound locals, across call
boundaries, and into closures and containers. `let h = n` on a
linear or mutable parameter is a *move* -- `h` inherits the
exactly-once obligation, and any later mention of `n` is an error
("the original name is dead"). A linear value moves into a callee
only through a parameter declared `linear` (CDX2065 -- `freeze`'s
own `linear a` parameter is what makes it the sanctioned exit); a
bare linear return requires a `linear`-declared return type
(CDX2066). A let-bound closure that captures a linear owns it and
is call-once; a partial application through a linear parameter is
the same discipline; a list or record literal stashing the bare
value makes the container the owner (consume it whole -- positional
re-reads are double-uses or plain-boundary errors). A handler
clause or an argument-escaping closure may not capture a linear at
all (CDX2067 -- clauses may run zero or many times). All nine
adversarial laundering probes are enforced (`codex/test/errors/
linear-launder-*`, `linear-capture-*`). Locals minted by a
linear-returning call and locals minted by a call returning a
`mutable` record are both tracked (the minted-owner walks; the
mutable half is fixed, pinned by
`codex/test/errors/stringbuilder-alias-local`). Known un-tracked
edge: container literals in argument or tail position.

## Vector Types (SIMD)

`Vector N T` is a fixed-width SIMD vector with `N` lanes of element
type `T`. The lane count is a compile-time integer (power of two,
1–64). The element type is restricted to numeric primitives.

```
  v : Vector 2 Real
  v = vec-splat 3.14

  w : Vector 4 (Integer between 0 and 255)
```

Arithmetic operators (`+`, `-`, `*`, `/`) are overloaded for vectors
and operate element-wise. Both operands must have matching `N` and `T`.

```
  result = v + v              -- element-wise add
  dot = vec-reduce-add (a * b)   -- dot product
```

Scalar broadcast is explicit via `vec-splat`, not implicit.

### Construction and Access

```
  vec-splat : a -> Vector N a           -- fill all lanes
  vec-extract : Vector N a, Integer -> a  -- extract one lane
  vec-load-at : Integer -> Vector 2 a     -- load 16 bytes at an address (movupd, unaligned OK)
  vec-store-at : Integer, Vector 2 a -> Integer  -- store 16 bytes at an address
```

`vec-load-at`/`vec-store-at` move whole vectors at computed
addresses -- the primitives under `Math chapter VecArray`, a
contiguous vector array over one flat 16·N buffer (va-alloc /
va-get / va-set / va-map2 / va-sum). `va-set` mutates in place and
returns the same array, like `list-set-at`.

### Reduction

```
  vec-reduce-add : Vector N a -> a      -- horizontal sum
```

### Approximate Equality

`==` and `/=` are compile errors on Real types (CDX2085). Use `~`:

```
  x ~ y        -- approximately equal (4 ULP tolerance)
  x ~0 y       -- bitwise exact (zero tolerance)
```

The `~` operator works on both scalar Real and `Vector N Real` values.
On vectors it produces a `VectorMask N`.

### Real Type

`Real` is the floating-point type (f64). `Real approximate` is f32.
`trapping` and `saturating` choose what happens when a result has no
representable value:

```
  Real                        -- f64, IEEE 754 default
  Real approximate            -- f32
  Real trapping               -- traps on NaN/Inf
  Real saturating             -- clamps to +-MAX
```

Precision and safety are meant to compose on both widths, so
`Real approximate trapping` should be legal. **It does not parse today**:
the type carries one qualifier, so of the six combinations only these four
can be spelled, and the two missing are both f32.

There is no `Real checked`. Recovering from a bad result is a job for an
operation you call where you care, not a property of every number you
declare, because a type that returns a maybe-a-number makes `a + b + c`
three unwrappings.

### Codegen

On x86-64, `Vector 2 Real` maps to SSE2 packed instructions (ADDPD,
SUBPD, MULPD, DIVPD). Vector values live in XMM registers. Alignment
is natural (`N * sizeof(T)` rounded to next power of two, minimum 16).

## Type Classes

`class` declares an interface; `instance` provides an implementation.
Dispatch is resolved at compile time by dictionary passing -- no runtime
cost.

```
  class Showable where
    to-text : Integer -> Text

  instance Showable Integer where
    to-text (x) = show x
```

Multiple instances, return-type polymorphism (the result type selects
the instance), generic functions constrained by a class, and instances
over parametric types are supported. A missing instance is a static
error (CDX2040).

## Tuples

`(a, b)` builds a pair, `(a, b, c)` a triple, up to five. Take one apart
with a tuple pattern in `when`:

```
  swap : Tup2 a b -> Tup2 b a
  swap (p) = when p
    is (x, y) -> (y, x)
```

Tuples desugar to the foreword `Tup2`..`Tup5` variants -- cite
`Foreword chapter Tuple`. Both `Tup2 A B` and `(A, B)` work in type
signatures. `let (x, y) = expr in body` destructures in let-bindings.

## Lists

```
  xs = [1, 2, 3]
  list-length xs          -- 3
  list-at xs 0            -- 1
  list-push xs 4          -- [1, 2, 3, 4]
  list-insert-at xs 1 99  -- [1, 99, 2, 3]
```

`[]` sugar for `LinkedList` in notation.

**`list-set-at` mutates in place.** It writes the slot and returns the
SAME list; `list-push` also writes in place while under capacity.
Arguments evaluate left to right (pinned by
`codex/test/wavelet-sort-aliasing.codex`). Code that needs value
semantics -- search that applies candidate moves, undo history, any
caller that re-reads the old list -- must copy first (a
`list-push`-loop over `list-at`; see `ttt-copy-squares` in
`apps/games/classic/TicTacToe.codex` for the idiom and the aliasing
bug it fixed).

## Text

```
  s = "hello"
  text-length s           -- 5
  s & " world"            -- "hello world"
  show 42                 -- "42"
  text-split s " "        -- ["hello"]
  text-contains s "ell"   -- True
  text-starts-with s "he" -- True
```

Internal encoding: CCE (Codex Character Encoding). Unicode at I/O
boundaries only. No `\t` or `\r` escapes -- use spaces and `\n`.

## Booleans

`True` / `False` -- capital T/F.

## Comments

Codex has no comments. No `//`, `--`, or `/* */`. Prose at column 2
under `Section:` headers is the commentary layer. For machine-readable
metadata, use the annotation sidecars in `annotations/` (see below); the
inline `@` form was removed from the language on 2026-07-27.

### What prose must not contain

**Never reference a document from source prose.** Not a path, not a section
number, not a register entry. Nothing re-reads such a reference, so it rots
silently, and the rot is fast. Measured across all 2,114 source chapters on
2026-07-27: **24 document references, of which 24 are dead.**

- `RiscVLir.codex` pointed at `docs/Designs/Active/Compiler/LirRetarget.md`,
  which had been moved to `Done/` less than an hour earlier.
- **22 point at `BACKLOG.md`, which was deleted on 2026-07-23** -- sixteen
  naming a numbered entry (`BACKLOG 3.20`, `7.17`, `4.17`) and six saying "the
  backlog" generically, across sixteen files. (`codex/tracker` and
  `StatusBadge` are excluded: there "Backlog" is a domain word in an issue
  tracker, not a reference.)
- one cited `CLAUDE.md` by rule number.

**The count in this paragraph was itself wrong when it was written**, and the
way it was wrong is the lesson. It said "exactly two", because the sweep
searched for `docs/` paths and `CL` numbers and did not search for the word
`BACKLOG`. The single BACKLOG reference it did catch surfaced only because that
line happened to also name an agent. **A survey is a claim too, and it is bounded
by the pattern you gave it** -- so state the pattern beside the count, and expect
the shape you forgot to look for.

State the substance instead. If the reason a constant is 2032 matters, say why
it is 2032; a reader who has the file does not need to be sent somewhere else,
and a reader who follows the pointer may find nothing.

**A changelist reference is different and is fine.** A CL is immutable and
Perforce keeps it forever, so `(CL 6430)` beside a regression pin still resolves
in a year. A document path is a location, and locations move.

**Leave attribution and dates to Perforce.** "All three are gone now" carries
everything the next reader needs; "(fester, 2026-07-16)" adds a name and a date
that `p4 annotate` already answers precisely. A *measurement* date is the
exception and worth keeping, because a number without a date cannot be judged
stale, which is what `CLAUDE.md` means by never carrying a count forward.

## Reserved Keywords

These words cannot be used as identifiers. The compiler rejects them
with CDX1060.

```
let  in  if  then  else  when  is  otherwise  act  end
record  mutable  punctual  unit  cites  quotes  trusting  above  grounds
claim  proof  qed  forall  exists  induction
linear  effect  where  with  between  and  such  that
class  instance  lazy
True  False
```

`as` is deliberately **not** reserved. It is the idiomatic name for a
list (`as`, `bs`, `cs`) and is used as an ordinary identifier in the
compiler, in the foreword, and in the `reverse-reverse` proof. In
`quotes "sha256:..." as Name` it is an ordinary word in a position that
`quotes` has already made unambiguous.

## Citing and Quoting

`cites` names a work. `quotes` reproduces one.

```
Chapter: PaymentGateway
  cites Foreword chapter Json
  quotes "sha256:a1b2c3d4..." as JsonParser
  trusting above 5000
```

A citation resolves through its quire to whatever file is currently on
disk under that name. A quotation resolves through its digest to exactly
one text -- the text that hashes to it, or nothing at all. That is the
whole difference, and it is why a quotation can be trusted and a
citation cannot.

`trusting above N` declares the chapter's trust floor. Trust scores are
fixed-point, `0`-`10000` (0.0–1.0), so `trusting above 5000` admits no
definition the author trusts less than half. The floor applies to every
quotation in the chapter and may be written before or after them. **A
chapter that quotes a work must declare a floor** -- an absent `trusting
above` is a compile error (CDX3026), because a floor defaulted by
omission is not a choice. A declared `trusting above 0` remains legal.

## Grounding Hardware Effects

`grounds` declares that a chapter is the SOURCE of specific hardware
effects -- the layer where the abstraction meets the metal. It is a
chapter-level declaration, a sibling of `cites`/`quotes`/`trusting`:

```
Chapter: Ne2k
  cites Kernel chapter Pci
  grounds Device.Port, Device.Mmio
```

The functions in a grounding chapter may perform the named effects
without declaring them in their signatures -- the internal exemption, so
a driver talking to the metal pays no per-function bookkeeping. A
function that *does* declare the effect (`f : ... -> [Device.Mmio] ...`)
publishes it to callers outside the chapter, who must then declare it in
turn: that function is a **root** of the capability graph, the boundary
where the effect becomes visible to ordinary code.

The exemption is **scoped to the named effects**. A chapter that
`grounds Device.Port` but performs `Device.Mmio` is rejected with
CDX2031 -- a chapter cannot launder some other hardware effect through an
exemption it took for a different one.

`grounds` replaces a hardcoded quire-exemption list that lived inside the
compiler: the module now declares its own status, in the file, three
lines above the code that touches the hardware, rather than the compiler
asserting it from afar. Effects erase at codegen, so `grounds` changes
only what type-checks, never the emitted binary. See
`docs/Designs/Done/Language/GroundsBoundary.md` for the full rationale.
The migration it describes is COMPLETE: `quire-effect-exempt` holds only
the compiler's own quires and the plug backends, no `codex/os/` quire is
exempt, and fourteen driver chapters carry `grounds`. Delete the `grounds`
line from `Pci.codex` and it fails CDX2031 and CDX2033, which is the check
that the declaration is load-bearing rather than decorative.

## Compile Modes

Sent as first line on stdin:

| Mode | Output |
|------|--------|
| `TEXT` | Codex source text |
| `CDX` | CDX binary |
| `ELF` | ELF x86-64 bare-metal |
| `EFI` | PE32+ UEFI application |
| `UEFI` | PE32+ UEFI app (ConOut) |
| `IMG` | GPT disk image |
| `MEASURE` | Phase metrics |

Append profile: `ELF QEMU-11.0.0`
Append flags: `TEXT prose`

### What TEXT mode actually emits

**Without the `prose` flag, the text emitter reproduces the chapter header
and the definitions, and drops the prose and the `cites` lines.** With
`prose`, the prose is emitted and round-trips. Measured 2026-07-25 by
compiling a small chapter both ways against the depot seed.

That is by design and it is consistent with the Comments section above:
outside `prose` mode, column-2 prose is the commentary layer and the
compiler is not reading it. The consequence worth knowing is what it means
for the gates. **The text fixed point compares emitter output against
emitter output** (stage1 against stage2), and semantic equivalence compares
definition bodies, so **no gate in `build/build.ps1` observes prose at
all.** Prose cannot break the build and the build cannot vouch for the
prose. If prose is load-bearing for a chapter, that chapter needs the
`prose` flag and a test that runs it; nothing else will notice.

## Codex Prose Language (CPL)

CPL is a proper subset of English from which all implicit binding is
removed. Activate with the `prose` compile flag (`TEXT prose`, `CDX prose`).

### The Three Axioms

1. **No implicit referent.** No `it`, `this`, `they`. Name the thing.
2. **No implicit quantity.** No `some`, `many`, `few`. Use a quantifier.
3. **No implicit order.** Use `first,`/`then,`/`finally,` for sequences.

### Banned Words

These are lexical errors inside `We say:` blocks (CDX1110):

| Banned | Substitute |
|--------|-----------|
| `it` | the named value |
| `this` | the named value |
| `they` | the named collection |
| `some` | a quantifier (all, every, exactly one, ...) |
| `many` | a quantifier or explicit bound |
| `few` | a quantifier or explicit bound |
| `etc.` / `etc` | exhaust the list or use a type |
| `so` | `therefore` or `in order to` |
| `since` | `because` (causal) or `after` (temporal) |
| `while` | `during` or `at the same time as` |
| `may` / `might` | `can` (possibility) or `is permitted to` (permission) |
| `should` | `must` (obligation) or `is recommended to` (recommendation) |

### Transition Markers

`We say:` opens a CPL block. Everything inside is parsed as CPL.
Everything outside is prose commentary -- human-readable, machine-ignored.
`This is written:` is an alternative marker.

### CPL Sentence Forms

1. **Type declaration:** `A Transaction is a record containing: ...`
2. **Function declaration:** `To deposit (amount : Amount) into (account : Account) gives the updated Account, failing if amount is less than zero.`
3. **Constraint:** `such that the balance is positive.`
4. **Proof assertion:** `claim: reversing a list twice gives the original.`
5. **Procedure step:** `first, let updated-balance be the balance plus amount.`
6. **Quantified statement:** `for every transaction in the history, the amount is positive.`

### Annotations live in sidecars, not in the source

**`@` is not Codex syntax and has not been since 2026-07-27.** This section
documented an inline form:

```
 @rationale opening "Why this function exists"
 @invariant balance "Always non-negative after deposit"
```

It is removed from the language. `AtSign` is gone from the token set, the
lexer no longer produces it, and `parse-annotation-line` is deleted. An `@`
at column 2 is now ordinary prose; an `@` in code is a stray character and
takes the same path as any other, which is `ErrorToken` and a failed compile.
Measured: `@bad` and `$bad` in the same position produce byte-identical
diagnostics.

Maintainer commentary belongs in `annotations/`, one JSON file per source
chapter, mirroring the source path. See `annotations/README.md` for the
format and `apps/works/AnnotationsQuery.codex` for reading them, which is an
optional side query and deliberately not a build step.

Note that `claim`, `punctual`'s budget and the `1 Minute = 60 Second`
conversion declaration are carried internally by a record named
`AnnotationNode`. They are directives, not annotations, and they are
unaffected.

### Prose-Notation Consistency (Warnings)

| Code | Check |
|------|-------|
| CDX1101 | Prose function name != notation definition name |
| CDX1102 | Prose parameter != notation parameter |
| CDX1103 | Prose record field != notation field |
| CDX1104 | Prose variant constructor != notation constructor |

### CPL Scope Rules

1. Names scope from introduction to end of enclosing `We say:` block.
2. Chapter-level types are public. Value bindings are private.
3. Parameters shadow chapter scope (only permitted shadowing).
4. No forward use of value bindings in procedures.
5. Constraints scope follows their attachment point.

## Pitfalls

**Lines cannot start with `.`** A `.field` continuation on its own line
is rejected with CDX1071 (it was previously silently dropped, surfacing
as a misleading downstream type error). Keep field access on the
receiver's line (`r.field`), or bind the receiver with a `let`.

**Multi-line function application needs parens.** Outside `act` blocks,
newlines are whitespace. A bare multi-line application can misparse.

**`show` vs `integer-to-text`.** Both convert Integer to Text. `show`
is the standard builtin. Do not write `toString`, `str`, `to-string`.

**Inline if/then/else in arithmetic.** A parenthesized if works inline:
`64 + (if w then 8 else 0) + (if r then 4 else 0)` evaluates correctly.
(An earlier emitter dropped these parens; that is fixed.) For deeply
nested conditionals, let bindings are still clearer:

```
  let wv = if w then 8 else 0
  in let rv = if r then 4 else 0
  in 64 + wv + rv
```

**`alloc-bytes` does NOT zero the memory it returns.** It is three
instructions -- `mov rax, r10; add r10, rdi; ret` -- so it hands back the
current heap pointer and bumps it. That is a different primitive from
`__alloc`, which does zero-fill and which the poison build is written
about; the two are easy to conflate because the documentation for the
zeroing one is prominent and there was none for this one.

The consequence: **write every byte you intend to read.** A buffer is only
as clean as whatever the heap pointer last passed over, so a partially
filled buffer returns the residue of earlier allocations. Measured
2026-07-28: a 64-byte buffer with only its top byte written read back the
decimal text of the previous statement's `show`, and a 512-byte buffer
taken after a discarded string held the bytes of that string
(`100 105 114 116 45 108 101 110`, which is `dirt-len`).

It is also invisible to the obvious probe. Allocating at the top of a
program and finding zeros proves nothing, because fresh heap has never
been written and looks identical to a zeroing allocator; the dirt has to
be created, discarded, and only then allocated over. `zero-bytes` is the
explicit fill, and `DiskFacts` calls it before packing a superblock for
exactly this reason.

The bump semantics are load-bearing elsewhere and must not be "fixed"
casually: `PerfMonitor` allocates **zero** bytes purely to read the heap
position, and `VecArray`'s prose already calls it a raw bump.

**A list CONSTANT is rebuilt at every mention, and it is invisible in the
source.** `xs : List Integer = [...]` is a definition, not storage: each
reference re-materialises the whole list. Measured on a 121-element list, 100000
reads of one element cost **98.4 MB of heap** through the constant and **1 KB**
through the same list passed in as a parameter -- a 91000-fold difference, and
both sites read `list-at xs i`.

This is survivable where a table is touched once per call and fatal inside a
search loop. A dictionary matcher reading three such tables per candidate, per
chain slot, per input byte ran the heap into the stack and double-faulted; the
fix was to load them once into a record and thread it. **If a table is read in a
loop, hoist it into a parameter or a record field.** There is no collector, so
every rebuild is permanent until the producing function returns.

The same applies to a large data constant read per element: build it once and
thread it, never re-reference it per position.
**Long & chains.** A single expression with many `&` concatenations
creates a deep IR tree. Break long chains into named helpers.

**Deeply nested lets.** A function with 20+ chained let bindings
creates deep scope nesting. Split into smaller functions.

**Nested `let ... in` inside an `else` (historical EXC=06 trap).**
Earlier compilers could mis-scope an `in` after `else let X = val`,
binding it to an outer `let` and silently putting `X` out of scope
(runtime EXC=06). Current compilers scope this correctly in the tested
patterns (see `codex/test/let-else-scope.codex`), but keeping if/else as
a single-line dispatch with each branch in its own named function
remains the clearest style:

```
  let reuse = find-slot st name
  in if reuse >= 0 then emit-let-reuse st name value body reuse
  else emit-let-fresh st name value body
```

**`&` and function application.** Function application binds tighter than
`&`, so `"text" & show x` parses correctly as `"text" & (show x)` -- no
parens needed. Add parens only when you want the other grouping.

**No multi-line `&` chains.** A line starting with `& "more"` is a
new expression, not continuation. Keep all `&` on one line or use
`let` bindings to break it up.

**Escapes in string literals.** `\"` (double quote) and `\n` (newline)
are supported: `"she said \"hi\""` prints `she said "hi"`. `\t` and `\r`
are rejected (CDX5/CDX6) -- use spaces and `\n`.

**`None` vs `Nothing`.** `None` is the empty constructor of `Maybe`
(a value). `Nothing` is a type (`NothingTy`) used as the return type
of effectful procedures: `[Console] Nothing`. They are not
interchangeable. Haskell uses `Nothing` for both; Codex splits them.
Using `Nothing` as a value is rejected with CDX2086 (hint: use `None`);
using `None`/`Some`/`Just` as a type adds a hint to the CDX2001 mismatch.

**`Cons`/`Nil` pattern matching on the builtin `List` works, with a
v1 shape.** `when xs is Nil -> ... is Cons (h) (t) -> ...` matches
structurally on a `List` scrutinee: the match desugars to length
tests over O(1) tail views, so structural recursion is linear, not
quadratic (`codex/test/list-pattern`). The v1 restrictions, each
rejected with CDX2088 rather than mislowered: a `Cons` field must be
a name or `_` (bind the tail and match it in a nested `when` instead
of nesting a pattern), and a `Nil`/`Cons` arm cannot carry a `when`
guard (test inside the arm body). A `Cons`-only match without a
catchall is non-exhaustive (CDX2070). `LinkedList` scrutinees are
not matchable this way. The tail is a VIEW sharing the backing
elements: `list-set-at` through it writes the backing, the same
aliasing contract `list-set-at` already has; copying operations
(`&`, `::`, `list-push`, `list-insert-at`) produce plain lists.
Cross-arch note: the desugar targets the `__list-len`/`__list-head`/
`__list-tail` intrinsics, which the ARM64/RISC-V plug lanes do not
implement yet -- list matches are x86-64-only until they do. Both plugs
**refuse** such a program rather than emitting one: the compile fails
with `[UNSUPPORTED] __list-len` and the two others. Until 2026-07-28
they compiled it clean and the call fell through to an undefined
symbol, so a list match on those lanes produced a binary that printed
nothing (riscv64) or faulted on every line (arm64), with no diagnostic
anywhere.
Proof arms were already structural: Stage 5 checks builtin-`List`
induction against a synthesized `Nil`/`Cons` view, and the proof
normalizer reduces `list-length`, `list-push`/`list-snoc`, and
literal-index `list-at` over those spines
(`codex/test/list-induction`).

**`real-from-int` / `real-to-int` for type conversion.** `n * 1.0`
is a type error (Integer * Real). `__narrow` does not convert Real
to Integer. Use the explicit conversion builtins.

**`sha256` returns WORDS, not bytes.** `sha256 : List Integer -> List
Integer` gives the compressor's internal state -- eight 32-bit words, not
thirty-two bytes. `sha256-to-hex` renders it with `words-to-hex`, and
`Hkdf` wraps every hash it touches in `hkdf-words-to-bytes` for exactly
this reason. Feed the word list somewhere a byte string is wanted and you
get plausible output that is silently wrong. Convert with
`hkdf-words-to-bytes` at the boundary. (This is what made the TLS 1.3 key
schedule wrong below its first rung; see `Tls.codex`.)

**`char-code` gives CCE, not ASCII.** Codex Text is CCE internally, so
`char-code (char-at s i)` returns the CCE code point. For `"derived"`
that is `16 0d 15 11 21 0d 16`, not the `64 65 72 69 76 65 64` that any
wire protocol means. To emit Text as bytes on a wire, convert at the I/O
boundary: `to-unicode (char-code (char-at s i))` (cite `Foreword chapter
CCE`). This bug is invisible in a round-trip test -- both sides agree --
and it shipped a TLS SNI hostname and every key-schedule label in CCE
until 2026-07-13.

**`end` is a reserved keyword.** Cannot be used as a parameter name
or identifier.

**A new foreword chapter silently SHADOWS an existing foreword name, and
the error lands nowhere near the cause.** Foreword names are globally in
scope, so a helper whose name already exists in another foreword chapter
is a shadow, not a duplicate-definition error. Inside the new chapter the
local definition wins and it compiles clean; in a test that cites it, the
name resolves to the OTHER chapter's version, and if the two differ in
arity the call becomes a partial application, surfacing as `CDX2001: Type
mismatch: List vs Fun` at the CALL SITE with no mention of the collision.
Before adding a chapter, grep the foreword for every helper name it
defines, and prefix private helpers with the chapter's own short tag
(`tlsc-`, `asn1-`, `x509-`) rather than a generic quire prefix.

**Arithmetic on CCE code points is meaningless.** CCE orders letters by
English frequency rather than alphabetically, so the usual hex-digit trick
`10 + c - 'a'` is nonsense: it read `0xff` as 391. Digits 3..12 are
contiguous and letters are not. Any letter-to-value conversion has to be a
comparison table, which is the form `hex-char-val` in `Dev chapter
HexFormat` already has.

**`poll-key` answers CCE, and is not source-compatible with a scan-code
reading.** A character is its CCE code point; every editing key is a named
constant at or above `key-named-base` (1048576), three orders of magnitude
above any character. Letters moved: `a` was 97 and is 15. Comparing a key
against a literal number is wrong now, and the failure is silent -- code
that collects `poll-key` output straight into bytes (`idm-rlb-loop` does)
produces different bytes than it used to, so stored identities stop
unlocking.

**`sha256-compress` writes into the hash list it is handed and returns that
same list**, because `list-set-at` is in place. That is deliberate and it is
what makes a scan-and-rewind digest possible: allocate the eight words below
a `__heap-save` mark and every block's schedule and buffers can be reclaimed
while the words survive. The consequence is that `sha256-h0` must never be
passed to it by anything expecting a constant to stay constant. `sha256`
gets away with it only because a top-level list constant is rebuilt at every
mention.

## Seed Rebuild Procedure

The canonical seed is `seed/Codex.cdx` -- the signed, self-sustaining CDX
binary, bootable via codex-vm or QEMU multiboot.

### Pre-conditions

- All source changes are submitted (no pending CLs touching `codex/` or any library quire directory)
- The change justifies a seed rebuild -- see below, and note the test is
  reachability, not which directory you edited

### When a change actually needs a new seed

**The question is not "did I touch a chapter the compiler cites". It is
"does the compiler REACH the code I changed".** Whole-program dead-code
elimination prunes every definition the compiler never calls, so code can
sit in a cited chapter and contribute nothing to the binary.

| Change | New seed? |
|--------|-----------|
| Codegen, or the body of any definition the compiler calls | Yes |
| A new builtin | Yes |
| Adding or removing a `cites` | Yes -- it moves the transitive closure |
| Adding or removing a module from a foreword quire | Yes |
| A new definition in an already-cited chapter that the compiler never calls | **No** |
| A registry arm on a live dispatch path (e.g. `run-ir-pass`) | Yes -- DCE cannot prune it |

The surprising row is measured, not reasoned. **CL 9432 added 155 lines to
`Fat16` -- a chapter the compiler does cite -- and the SUT came out
byte-identical to the depot seed**, because nothing in the compiler reaches
`fat16-create-file`. The prediction going in was that it would need a seed;
it did not.

The `cites` row is mechanical rather than measured here: a citation moves the
transitive closure, so the concat feeds the compiler a chapter it was not
being fed before. Do not go looking for a clean byte count attached to a
changelist for it -- CL 9400 is the one usually cited and it changed
`opening.codex` and three foreword chapters in the same breath, so its seed
growth cannot be pinned on the citation.

**So do not predict it. Measure it, every time, after the gate:**

```powershell
(Get-FileHash -Algorithm SHA256 build/output/Sut.cdx).Hash
(Get-FileHash -Algorithm SHA256 seed/Codex.cdx).Hash
```

Equal means the depot seed still reproduces from depot source and this CL
carries no seed. Different means it must.

**`build/build.ps1` does not do this for you and cannot.** It proves the SUT
is a fixed point of *itself*; it says nothing about whether the seed in the
depot is that SUT. That gap is the whole reason a seed can silently stop
reproducing.

### Steps

1. **Run full build** -- `build/build.ps1`. All phases must PASS (text round-trip + CDX fixed-point + test battery).
2. **Install new seed** -- `Copy-Item build/output/NewSeed.cdx seed\Codex.cdx -Force`

   **Use `NewSeed.cdx`, not `Sut.cdx`.** This step said `Sut.cdx` until
   2026-07-28 and that is correct only when the build printed
   `hard fixed point in one pass`, because in that case they are the
   same bytes. **When your change alters what the
   compiler EMITS they are not**, and installing `Sut.cdx` installs a
   compiler that is not the fixed point of itself.

   The reason is what the two artifacts are. `Sut.cdx` is the OLD seed
   compiling the new source, so its own binary was emitted by the old
   codegen; `stage1.cdx` is the Sut compiling the new source, so it is
   the first binary that both contains and produces the new codegen.
   `build.ps1` copies `stage1` to `NewSeed.cdx` at the fixed-point step
   for exactly this purpose, and the fixed point it proves is
   `stage1 === stage2` -- a property of stage1, which is why stage1 is
   the seed.

   Measured 2026-07-28 adding six builtins that emit new helper
   functions: `Sut.cdx` was 2701354 bytes and `stage1 === stage2` was
   2701564. Installing the Sut would have shipped a seed missing the
   helpers from its own body.

   **`NewSeed.cdx` IS NOT SIGNED, so it is a step and not the destination.**
   The `sign` phase signs the SUT that the build produced from the seed;
   `stage1` is emitted later and nothing signs it. Install `NewSeed.cdx` and
   run `build/test-self-verify.ps1` and it answers `SIGNATURE: False`,
   `AUTHOR-KEY-PRESENT: False`, `SIGNATURE INVALID`. Measured 2026-07-28.

   This page said "use NewSeed.cdx" and `red-workplan.md` said "the
   installed seed is the SIGNED Sut.cdx", and **both were half right**. The
   full procedure when the bytes move is three steps, not one:

   1. `Copy-Item build/output/NewSeed.cdx seed\Codex.cdx -Force` -- the
      fixed point, unsigned.
   2. Run `build/build.ps1` again. It now prints `hard fixed point in one
      pass`, and the `Sut.cdx` it just built has the same content AND
      carries the signature.
   3. `Copy-Item build/output/Sut.cdx seed\Codex.cdx -Force` -- same bytes
      of program, now signed. Sizes match; only the signature differs.

   **Then prove it**: run `build/build.ps1` once more and it must still say
   `hard fixed point in one pass` with `Sut == seed`, and
   `build/test-self-verify.ps1` must print `THE SEED VERIFIES ITSELF`. A
   content-hash match cannot tell you the signature is there, which is
   exactly why self-verify is a separate step and not a formality.

   Do NOT use `build-output/bare-metal/Codex.cdx` -- that is the unsigned
   boot kernel, and neither of the two above.
3. **Self-verify** -- `build/test-self-verify.ps1`. Must print "THE SEED VERIFIES ITSELF".
4. **Capture digest** -- `Get-FileHash -Algorithm SHA256 seed\Codex.cdx`
5. **Submit to Perforce** -- `p4 submit -d "seed: rebuild for CL <N>"`

### Rules

- Never skip pingpong. Never skip self-verify.
- One seed per CL. CDX is primary.
- Signing is automatic.
- The bootable image (`seed/Codex.img`) is a separate distribution
  artifact built by `build/build-boot-img.ps1`. It is NOT part of the
  seed rebuild. Do not run it during seed rebuilds.
