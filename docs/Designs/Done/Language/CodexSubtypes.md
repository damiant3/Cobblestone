# Codex Subtypes — Bounded Ranges and Unit Domains

**Date:** 2026-04-16 (initial), updated 2026-07-13.
**Status:** **Both axes shipped.** Axis 1 (bounds) through CL 410 +
`__narrow`. Axis 2 (units) shipped as **unit families** — the earlier
"deferred, not started" line was stale.

Units as built: `unit family <Base>` with a scale table per member.
`codex/foreword/core/Units.codex` defines Duration, LongDuration,
Length, and friends; `UnitTy` lives in
`codex/compiler/Types/CodexType.codex` and threads through the
unifier, lowering, and every emitter. Functions take the *family* name
(`Length`, `Duration`) and accept any member unit; conversion happens
at construction, so call sites are zero-cost. Tests:
`codex/test/unit-family.codex` and `codex/test/unit-family-mixed.codex`.
The design below describes units as standalone `unit Integer`
declarations with pairwise `1 Minute = 60 Second` facts; the shipped
form generalizes that to a family with a base and a scale per member.
Read the unit sections as rationale, not as a spec of the syntax.

**The real residual is on Axis 1, not Axis 2:** arithmetic result-type
derivation. `Integer between 0 and 255` plus anything still yields an
unbounded `Integer` — the bound does not propagate through `+`. That
is exactly why `__narrow` exists and why CDX2051 fires on `field + 1`.
See "Still open (Axis 1)" below; it is the one item standing between
bounded types and the ergonomics they were designed for.

## Shipped (Axis 1)

| CL | What |
|---|---|
| 358 | `Integer in lo..hi` syntax, parser, bidirectional unifier |
| 372 | Width-aware emit primitives, narrow-store / narrow-load, Boolean → 1 byte |
| 374 | Width-sort record layout |
| 376–381 | Token / SourcePosition / Diagnostic / DiagnosticBag and the CL 381 sweep migrated to bounded fields |
| 384 | Revert ExprTypeEntry.key bound (CL 381 over-narrowed a packed hash) |
| 403 | Lint pass: CDX2050 (literal out-of-bound, error) + CDX2051 (wider type, warning); `bag-errors` filter so warnings don't pollute TEXT-mode stdout |
| 404 | `OverflowMode` AST + parser plumbing — `wrapping` / `clamping` / `error` keyword (default `error`) |
| 409 | Error-mode codegen at narrow-store; `cmp+jcc+ud2`. Bounds in i32-signed range use `cmp-ri`; u32 bounds use a `push-r9 / mov-ri64 / cmp-rr / pop-r9` sandwich. `AdvanceResult.pos` annotated `wrapping` because skip-list span arithmetic is genuinely modular |
| 410 | Clamping codegen at narrow-store (`cmp + jcc + mov-ri32` saturating). Lint mode-aware: CDX2050/CDX2051 fire only under `error` mode |
| 411 | `__narrow` builtin — explicit-narrow primitive that suppresses CDX2050/CDX2051 at a specific call site. Codegen pass-through; downstream narrow-store enforces the field's mode |
| this CL | `between L and H` syntax (canonical, prose-form) accepted alongside `in L..H`. Closed-inclusive only. Spec rewritten throughout to use the new form. Selfhost source migration is the follow-on CL. |

Default overflow mode is **`error`** (Damian, 2026-04-26). Unannotated bounded fields trap on out-of-range writes.

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
Integer                                 -- full machine word (64-bit)
Integer between 0 and 255               -- 8 bits sufficient
Integer between 0 and 16777215          -- 24 bits sufficient
Integer between 0 and 1048576           -- 20 bits sufficient
```

The compiler picks the tightest power-of-two-aligned representation
that covers the declared range. The user never writes "Int32" — they
write the domain constraint. The width follows.

Bounds are closed-inclusive. `Integer between 0 and 255` includes
255; the storage size (256 = 2^8) is the compiler's derivation,
not a number the author writes. There is no half-open form;
mathematical interval pedantry stays out of the source.

Named types carry their bounds:

```
ByteOffset = Integer between 0 and 16777215
LineNumber = Integer between 1 and 1048576
FileId     = Integer between 0 and 65535
```

Record fields with bounded types pack tighter: sub-qword loads/stores,
proper alignment. A record with three 32-bit fields occupies 12 bytes
+ padding, not 3 × 8 = 24 bytes.

#### Overflow semantics

Bounds raise the question: what happens when a value exceeds its range?

```
Byte       = Integer between 0 and 255 wrapping     -- modular arithmetic
Percentage = Integer between 0 and 100 clamping     -- saturates at bounds
SafeIndex  = Integer between 0 and N error          -- runtime error on overflow
```

Default mode (no keyword) is **`error`**. Unannotated bounded fields
trap on out-of-range writes. This is the safest default and forces
authors to be explicit when truncation or saturation is intended.

The `__narrow` builtin is the explicit-narrow primitive: writing
`__narrow expr` at an assignment site suppresses CDX2050 and CDX2051
for that site. Codegen is pass-through; the downstream narrow-store
still enforces the field's mode (so `__narrow` under `error` mode
will trap if the value really doesn't fit at runtime — it's an
intent annotation, not an unchecked cast).

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

| Aspect | Ada | F# | Codex (as built) |
|--------|-----|-----|-----------------|
| Range bounds | `range 0..23` | no | `Integer between 0 and 23` |
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

### Resolved

5. **Overflow mode default.** Resolved: `error`. Unannotated bounded
   fields trap on out-of-range writes. Implemented in CL 404
   (parser/AST default) and CL 409 (codegen).

6. **Migration path.** Resolved in practice: CL-by-CL field
   migrations driven by the heap-profiling motivation. CL 376–381
   landed Token / SourcePosition / Diagnostic / DiagnosticBag /
   ParseState etc. The cascade lives in `project_bounded_integer_
   cascade.md` (memory). One field reverted (ExprTypeEntry.key, CL
   384) when its semantic was a packed hash, not a counter. One
   field annotated `wrapping` (AdvanceResult.pos, CL 409) when the
   semantic was genuinely modular.

### Still open (Axis 1) — THE remaining work

2. **Arithmetic result types.** `Integer between 0 and 255 + Integer
   between 0 and 255 → Integer between 0 and 510` is not derived. `+`
   returns unbounded `Integer` regardless of operand bounds, which is why
   CDX2051 fires on `field + 1` patterns. Refinement-typed
   arithmetic is the proper fix; for now, `__narrow` is the
   manual escape hatch — and its existence in the source is the
   standing receipt that this is unfinished. Every `__narrow` in the
   tree is a place the compiler should have derived the bound and
   didn't.

   Concretely: the derivation rules are the easy part (`+` adds the
   bounds, `*` multiplies them, `-` subtracts low-from-high, `/` by a
   positive constant divides). The work is threading the derived bound
   through the unifier so it reaches the narrow-store, and deciding
   what happens when a derived bound exceeds the destination's — which
   is the same question the overflow modes already answer.

7. **`__narrow` runtime semantics.** Currently codegen pass-through.
   Could optionally emit a runtime check at the narrow point itself
   (catch the violation earlier in the trace) when the destination
   type is in `error` mode. Deferred — downstream narrow-store
   already catches it.

8. **Layout details.** Width-sort + alignment rules from CL 374 are
   codegen decisions with no spec guidance. Spec should formalize
   so future backends can match.

### Still open (Axis 2 — units shipped; these questions survive it)

1. **Interaction between bounds and units.** Can you write
   `ShortDuration = Second between 0 and 3600`? Bounded AND unit-tagged?
   If so, conversion from `Hour between 0 and 1` to `ShortDuration` must
   range-check after converting.

3. **Conversion ambiguity.** If there are two paths from A to B in the
   unit graph (e.g., via C and via D), which conversion applies? Error?
   Shortest path? We need a disambiguation rule or require unique paths.

4. **Cost of runtime tags.** If every `Integer` value carries a unit
   tag, that's +8 bytes per value (or +1 byte if we use a compact tag
   scheme). For register-held values, the tag might be implicit from
   static type context. For heap-stored values, it's an extra field.
   Need to quantify the cost.

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
