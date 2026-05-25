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
| `codex.magic` | `Magic` |

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
| Linear | `linear FileHandle` |

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
  __record-set p "name" "Bob"      -- functional update
```

No `{ record | field = val }` sugar. Use `__record-set` for functional updates.

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
act blocks. The type checker rejects field assignment on immutable
records (CDX2060). Mutable records use `__record-set` under the hood
but the compiler enforces ownership: CDX2061 (use after consume) and
CDX2062 (aliasing) are planned for Phase 3 (linearity tracking).

`freeze` (planned): converts `mutable T` to `T`, consuming the
mutable reference and producing an immutable copy.

## Variants (Sum Types)

```
  Shape =
   | Circle (radius : Integer)
   | Rect (width : Integer) (height : Integer)

  area : Shape -> Integer
  area (s) = when s
   is Circle (r) -> r * r * 3
   is Rect (w) (h) -> w * h
```

## Pattern Matching

```
  when expr
   is Pattern1 -> body1
   is Pattern2 -> body2
   is otherwise -> default
```

`when` / `is` — not `match` / `case`. Wildcard: `is otherwise -> ...`
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
are whitespace — multi-line function applications work everywhere.

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
| `==` `/=` `<` `>` `<=` `>=` | Comparison (note: `/=`, not `!=`) |
| `&` | Text/list append |
| `\|` | Boolean or |
| `++` | Text/list append (deprecated, use `&`) |
| `::` | List cons |
| `->` | Function type arrow |
| `<-` | Effect bind |
| `\|-` | Turnstile (proofs) |

Unicode equivalents accepted by the lexer: `→` for `->`, `←` for `<-`,
`≡` for `===`, `≠` for `/=`, `≤` for `<=`, `≥` for `>=`, `⊢` for `|-`,
`⊗` for `(**)`, `∀` for `forall`, `∃` for `exists`.

## Negation

Negative literals in argument position must be parenthesized:

```
  list-push acc (-1)       -- correct
  list-push acc -1         -- WRONG: parsed as subtraction
  -5                       -- literal negative
  -x                       -- negate a variable
  -(x + 1)                 -- negate a compound expression
```

The compiler folds `-(literal)` into `IrIntLit` at IR level.
There is also a `negate` builtin but the unary operator is preferred.

## Bounded Integers

```
  age : Integer between 0 and 150
  byte : Integer between 0 and 255 wrapping
  offset : Integer between -128 and 127 clamping
```

Overflow modes: `wrapping` (mod), `clamping` (saturate), `error`
(default, compile-time check on literals).

Plain `Integer` arithmetic produces a plain `Integer`, which won't fit
into a bounded slot. Use `__narrow` to assert the value is in range
(checked at runtime — out-of-range traps):

```
  make-byte : Integer -> Byte
  make-byte (n) = Byte { val = __narrow n }
```

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
during emit — they produce no machine code. The compiler reports each
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

```
  open-file : Text -> [FileSystem] linear FileHandle
  close-file : linear FileHandle -> [FileSystem] Nothing
```

## Lists

```
  xs = [1, 2, 3]
  list-length xs          -- 3
  list-at xs 0            -- 1
  list-push xs 4          -- [1, 2, 3, 4]
  list-insert-at xs 1 99  -- [1, 99, 2, 3]
```

`[]` sugar for `LinkedList` in notation.

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
boundaries only. No `\t` or `\r` escapes — use spaces and `\n`.

## Booleans

`True` / `False` — capital T/F.

## Comments

Codex has no comments. No `//`, `--`, or `/* */`. Prose at column 2
under `Section:` headers is the commentary layer. For machine-readable
metadata, use `@annotations` (prose flag).

## Reserved Keywords

These words cannot be used as identifiers. The compiler rejects them
with CDX3014.

```
let  in  if  then  else  when  is  otherwise  act  end
record  cites  claim  proof  qed  forall  exists
linear  effect  where  with  between  and  such  that
True  False
```

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
Everything outside is prose commentary — human-readable, machine-ignored.
`This is written:` is an alternative marker.

### CPL Sentence Forms

1. **Type declaration:** `A Transaction is a record containing: ...`
2. **Function declaration:** `To deposit (amount : Amount) into (account : Account) gives the updated Account, failing if amount is less than zero.`
3. **Constraint:** `such that the balance is positive.`
4. **Proof assertion:** `claim: reversing a list twice gives the original.`
5. **Procedure step:** `first, let updated-balance be the balance plus amount.`
6. **Quantified statement:** `for every transaction in the history, the amount is positive.`

### Annotations

At column 2, `@` introduces an annotation:

```
 @rationale opening "Why this function exists"
 @invariant balance "Always non-negative after deposit"
 @warning compute-hash "O(n) in key length, hot path"
```

Format: `@kind target body`

Kinds: `rationale`, `invariant`, `warning`, `discovery`, `doctrine`, `todo`

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

**Lines cannot start with `.`** The parser bans `.`-prefixed lines.
Use let bindings to break up field-access chains.

**Multi-line function application needs parens.** Outside `act` blocks,
newlines are whitespace. A bare multi-line application can misparse.

**`show` vs `integer-to-text`.** Both convert Integer to Text. `show`
is the standard builtin. Do not write `toString`, `str`, `to-string`.

**Inline if/then/else in arithmetic.** The emitter does not preserve
parentheses around if expressions. Use let bindings:

```
  let wv = if w then 8 else 0
  in let rv = if r then 4 else 0
  in 64 + wv + rv
```

**Long & chains.** A single expression with many `&` concatenations
creates a deep IR tree. Break long chains into named helpers.

**Deeply nested lets.** A function with 20+ chained let bindings
creates deep scope nesting. Split into smaller functions.

## Seed Rebuild Procedure

The canonical seed is `seed/Codex.cdx` (CDX binary, bootable via
codex-vm or QEMU multiboot). `seed/Codex.img` is a UEFI-bootable GPT disk image
containing the PE32+ compiler, CDX seed, all source, and docs.

### Pre-conditions

- All source changes are submitted (no pending CLs touching `codex/` or any library quire directory)
- The change justifies a seed rebuild (codegen change, new builtin, foreword change that affects compilation)

### Steps

1. **Run full build** — `build/build.ps1`. All phases must PASS (text round-trip + CDX fixed-point + test battery).
2. **Install new seed** — `Copy-Item build/output/Sut.cdx seed\Codex.cdx -Force`
3. **Build bootable image** — `build/build-boot-img.ps1`
4. **Verify sweep** — `build/test.ps1 -Jobs 4`. Must match or exceed prior pass count.
5. **Self-verify** — `build/test-self-verify.ps1`. Must print "THE SEED VERIFIES ITSELF".
6. **Capture digests** — `Get-FileHash -Algorithm SHA256 seed\Codex.cdx`, same for `.img`.
7. **Update files** — README.md (digest tables, sample counts), `docs/PM/CurrentPlan.md` (seed size, sweep counts), `docs/PM/BACKLOG.md` (sweep counts).
8. **Submit to Perforce** — `p4 submit -d "seed: rebuild for CL <N>"`
9. **Push to GitHub + GitLab** — stage specific files, merge with `-X ours`, push.

### Rules

- Never skip pingpong. Never skip self-verify. Never skip sweep.
- One seed per CL. CDX is primary; ELF is derived.
- IMG is the distribution artifact.
- Signing is automatic if `D:\Projects\signing.key` exists (lives OUTSIDE the repo).
- Never `git add -A`. Never force-push.
