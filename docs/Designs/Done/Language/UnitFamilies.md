# Unit Families — Domain-Polymorphic Physical Units

**Date**: 2026-06-13
**Status**: Design, implementation starting

## The Problem

Unit types (`Second = unit Integer`) prevent mixing unrelated domains
(seconds vs meters). But they're too specific for function signatures.
A geometry function shouldn't care whether you pass centimeters or
inches — it takes a LENGTH. A physics function shouldn't care about
Celsius vs Kelvin — it takes a TEMPERATURE.

Current approach requires explicit conversion at every call site:

```codex
perimeter : Centimeter, Centimeter -> Centimeter    -- too specific
perimeter (w) (h) = (w + h) * 2

opening = perimeter (Centimeter 20) (Inch-to-Centimeter (Inch 5))
                                      -- caller's burden
```

## The Vision

```codex
Length = unit family Millimeter
  Millimeter = 1
  Centimeter = 10
  Meter = 1000
  Kilometer = 1000000
  Inch = 25
  Foot = 305
  Mile = 1609344

perimeter : Length, Length -> Length
perimeter (w) (h) = (w + h) * 2

opening = perimeter (Centimeter 20) (Inch 5)
-- both auto-convert to base (millimeters), compute, result is Length
```

The function takes `Length` — the family name. Any member unit is
accepted. Values are stored internally in the BASE unit (first member,
declared factor = 1). Conversion happens at CONSTRUCTION time, not at
call sites.

## Design

### Declaration Syntax

```
Name = unit family BaseUnit
  Member1 = factor1
  Member2 = factor2
  ...
```

The base unit has factor 1 (one base-unit = one internal unit).
Each member's factor is: how many base units in one member unit.
`Centimeter = 10` means one centimeter = 10 millimeters internally.

### Semantics

1. `Length` is a type. Functions use it in signatures.
2. `Centimeter 20` constructs a Length value = 20 * 10 = 200 (base units).
3. `Inch 5` constructs a Length value = 5 * 25 = 125 (base units).
4. `Length + Length -> Length` — arithmetic works on base values.
5. `perimeter (Centimeter 20) (Inch 5)` — both are Length, no conversion needed.
6. Display: a Length value of 200 with no unit context displays as 200.
   To display as centimeters: `Length-to-Centimeter result` divides by 10.

### Implementation

**Parser**: `unit family` after `=` in type def position. Parse base
unit name, then indented member lines `Name = IntegerLiteral`.

**Desugarer**: For each family, synthesize:
- The family type as `unit Integer` (the base representation)
- Each member as a constructor function that multiplies by the factor
- Each member as a reverse function that divides by the factor
- e.g., `Centimeter : Integer -> Length` = `Length (n * 10)`
- e.g., `Length-to-Centimeter : Length -> Integer` = `n / 10`

**Type checker**: `Length` resolves to `UnitTy "Length" IntegerTy`.
All member constructors produce `Length`. The unifier handles `Length`
like any other unit type.

**Codegen**: Zero overhead. `Centimeter 20` compiles to `20 * 10 = 200`.
The multiplication is a compile-time constant fold when the argument
is a literal.

### Temperature (Non-Linear)

Temperature families need affine conversions (offset + scale), not
just multiplication. The factor-only approach works for most physical
units but not temperature.

For now: temperature stays as individual unit types with manual
conversion functions. A future extension could support
`Member = scale, offset` syntax for affine conversions.

### Interaction with Bounded Types

`Length between 0 and 100000` bounds the BASE unit value.
`Centimeter 20` → base value 200, which must be ≤ 100000.

### What This Replaces

The current `unit` + conversion declaration system still works.
Unit families are sugar over it. A `unit family` declaration is
equivalent to:

```codex
Length = unit Integer
Centimeter : Integer -> Length = \n -> Length (n * 10)
Length-to-Centimeter : Length -> Integer = \l -> l / 10
-- ... for each member
```

The family just makes this declarative and generates all the
boilerplate.
