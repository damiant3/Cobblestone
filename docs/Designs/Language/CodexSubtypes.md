# Codex Subtypes — Bounded Ranges and Unit Domains

**Date:** 2026-04-16
**Status:** Early exploration. Ideas from a design conversation, not a proposal.

---

## Problem

Every `Integer` in Codex is 64-bit on bare-metal. A `file-id` that will
never exceed 40 occupies 8 bytes. A `line` number that will never exceed
20,000 occupies 8 bytes. A `Boolean` occupies 8 bytes. The self-host
compiler's heap HWM is 192 MB to compile 600 KB of source — most of
those bytes are zeros in the high bits of fields that could be far
smaller.

Separately, there is no way to express that a value means "seconds" vs
"meters." Assigning a distance to a duration is a silent bug.

These are two orthogonal problems with a shared solution space.

---

## Two Orthogonal Axes

### Axis 1: Bounds (representation)

How large can the value be? The answer determines storage width.

```
Integer                         -- full machine word (64-bit)
Integer in 0..255               -- 8 bits sufficient
Integer in 0..16777215          -- 24 bits sufficient
Integer in 0..1048576           -- 20 bits sufficient
```

The compiler picks the tightest power-of-two-aligned representation
that covers the declared range. The user never writes "Int32" — they
write the domain constraint. The width follows.

Named types carry their bounds:

```
ByteOffset = Integer in 0..16777215
LineNumber = Integer in 1..1048576
FileId     = Integer in 0..65535
```

Record fields with bounded types pack tighter: sub-qword loads/stores,
proper alignment. A record with three 32-bit fields occupies 12 bytes
+ padding, not 3 × 8 = 24 bytes.

#### Overflow semantics

Bounds raise the question: what happens when a value exceeds its range?

```
Byte       = Integer in 0..255 wrapping     -- modular arithmetic
Percentage = Integer in 0..100 clamping     -- saturates at bounds
SafeIndex  = Integer in 0..N error          -- runtime error on overflow
```

Default behavior is TBD. `error` is safest. `wrapping` is standard for
byte-level work. `clamping` is useful for signal processing. The
overflow mode could be part of the type declaration.

### Axis 2: Units (meaning)

What does the number represent? Two values with the same range but
different domains should not be silently mixed.

```
Second = unit Integer
Meter  = unit Integer
Hour   = unit Integer
```

A `unit` declaration creates a distinct type. Arithmetic between
unrelated units is a type error:

```
let s : Second = 5 Meter    -- ERROR: no conversion path
```

Units are runtime-real, not compile-time erasures. The domain tag
travels with the value. A `Second` arriving over a trust boundary can
be verified as actually being a `Second`, not a raw integer someone
relabeled.

---

## Declared Conversions

The relationship between related units is a fact, declared once in the
foreword. Not annotated per-constant. Not per-callsite. A citable,
auditable, versioned fact.

```
Chapter: Time

  Second = unit Integer
  Minute = unit Integer
  Hour   = unit Integer

  1 Minute = 60 Second
  1 Hour   = 60 Minute
```

The compiler derives the transitive closure: `1 Hour = 3600 Second`.

### Implicit application

When a function expects `Second` and receives `Hour`, the compiler
inserts the conversion automatically:

```
  cites Foreword chapter Time

  countdown : Second -> [Console] Nothing
  countdown (remaining) = ...

  main = countdown (2 Hour)
  -- compiler inserts: 2 * 3600 → 7200 Second
```

No annotation at the call site. The conversion is applied because the
relationship is declared and cited.

### Safety from absence

If no conversion path exists between two unit types, assignment or
passing between them is a type error. The absence of a declared
relationship IS the safety mechanism.

```
  cites Foreword chapter Time
  cites Foreword chapter Length

  let s : Second = 5 Meter
  -- ERROR: no conversion path between Meter and Second
```

Nobody wrote a fact connecting time and length. That's not an oversight
— it's the type system working correctly.

### Non-multiplicative conversions

Not all unit relationships are linear scaling factors. Temperature
conversion is affine:

```
  Celsius    = unit Number
  Fahrenheit = unit Number

  convert Celsius -> Fahrenheit = \c -> c * 9 / 5 + 32
  convert Fahrenheit -> Celsius = \f -> (f - 32) * 5 / 9
```

Explicit conversion functions for non-linear relationships. Still
declared in the foreword. Still applied implicitly at assignment and
argument sites.

---

## Normalization vs Conversion

Conversion changes unit representation: `2 Hour → 7200 Second`.

Normalization changes structure: `7261 Second → TimeSpan { 2 Hour, 1 Minute, 1 Second }`.

These are different operations. Conversion is implicit (declared in
foreword, applied by compiler). Normalization is explicit (a function
the user calls when they want a structured breakdown):

```
  TimeSpan = record {
    hours   : Hour,
    minutes : Minute,
    seconds : Second
  }

  normalize : Second -> TimeSpan
  normalize (total) = TimeSpan {
    hours   = total / 3600,
    minutes = (total / 60) % 60,
    seconds = total % 60
  }
```

`1000 Second` is a valid `Second` — it's just large. Only when you
want a human-readable decomposition do you normalize.

---

## Foreword as Unit Ontology

The conversion facts live in foreword chapters. They are:

- **Citable.** A source file declares `cites Foreword chapter Time` to
  gain access to time units and their conversions.
- **Auditable.** The conversion factors are visible, inspectable text.
  You can read the foreword and verify `1 Hour = 3600 Second`.
- **Versioned.** A different foreword can define different conversion
  facts (useful for domain-specific unit systems).
- **Composable.** Citing multiple foreword chapters (Time + Length)
  gives you both unit families without them interfering.

The compiler does not have built-in knowledge of meters or seconds.
ALL unit relationships come from cited foreword chapters. The trust
chain extends to units.

---

## Comparison to Prior Art

| Aspect | Ada | F# | Codex (proposed) |
|--------|-----|-----|-----------------|
| Range bounds | `range 0..23` | no | `Integer in 0..23` |
| Distinct types | yes (strong) | yes (measures) | yes (unit) |
| Conversion | explicit function call | explicit annotated constant | implicit from declared fact |
| Syntax | `Hour_Type`, `for T'Size use 8` | `float<meter/second^2>` | `Hour`, `Meter` — plain words |
| Runtime presence | yes (tagged) | no (erased) | yes (domain tag travels) |
| Where defined | package spec | inline attributes | foreword chapters (citable) |
| Non-linear convert | manual | doesn't fit | `convert X -> Y = \v -> expr` |
| Overflow | Constraint_Error | n/a | `wrapping` / `clamping` / `error` per type |

Key differentiators:
- **No techno-jargon.** No `Int32`, no `<meter/second^2>`, no `'Size use 8`. Domain words only.
- **Conversions are facts, not code.** `1 Minute = 60 Second` is a declaration, not a function body. The compiler derives and applies conversions.
- **Runtime-real.** Unit tags are not erased. Data is self-describing across trust boundaries.
- **Foreword-sourced.** Unit ontologies are cited chapters, not compiler built-ins.

---

## Open Questions

1. **Interaction between bounds and units.** Can you write
   `ShortDuration = Second in 0..3600`? Bounded AND unit-tagged? If so,
   conversion from `Hour in 0..1` to `ShortDuration` must range-check
   after converting.

2. **Arithmetic result types.** `Second + Second → Second` (same unit,
   range widens). `Second * Integer → Second` (scaling). What about
   `Second * Second`? Is that `Second^2`? Or just `Integer`? Algebraic
   dimensions vs flat units — how far do we go?

3. **Conversion ambiguity.** If there are two paths from A to B in the
   unit graph (e.g., via C and via D), which conversion applies? Error?
   Shortest path? We need a disambiguation rule or require unique paths.

4. **Cost of runtime tags.** If every `Integer` value carries a unit
   tag, that's +8 bytes per value (or +1 byte if we use a compact tag
   scheme). For register-held values, the tag might be implicit from
   static type context. For heap-stored values, it's an extra field.
   Need to quantify the cost.

5. **Overflow mode default.** `error` is safest but adds a branch per
   arithmetic op. `wrapping` is cheapest but hides bugs. Could default
   to `error` in debug, `wrapping` in release — but that means debug
   and release have different semantics. Codex principles probably
   demand one consistent behavior.

6. **Migration path.** Existing self-host code uses `Integer` everywhere.
   How do we gradually adopt bounded/unit types without rewriting
   everything at once?

---

## Motivation

From the 2026-04-16 bare-metal heap profiling session: the self-host
compiler allocates 192 MB to compile 600 KB of source. Most record
fields (line numbers, column numbers, offsets, file IDs, token kinds)
use fewer than 24 bits of their 64-bit slots. The remaining bits are
zeros — carried through memory, cached, bus-transferred, and never
read.

Bounded-range types would let the compiler pack these fields without
the user writing bit-manipulation code. The SourceSpan record — today
80+ bytes with nested SourcePosition records — could become 12 bytes
with offset-range fields.

Unit types would prevent a class of bugs the language currently cannot
catch: confusing a byte offset with a line number, a token index with
a character offset, a file ID with a definition count.

Both features serve the same principle: the type system should carry
meaning, and the compiler should do the work.
