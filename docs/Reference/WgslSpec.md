# WGSL: what the spec requires of anything we emit

Source: W3C **Candidate Recommendation Draft, 31 August 2026**,
`https://www.w3.org/TR/2026/CRD-WGSL-20260831/`. Section numbers below are
that document's. This is a distillation of the rules that bind
`codex/plugs/wgsl`, not a summary of the language: a rule is here because
the emitter can violate it, and each one names the section to re-read.

WGSL is not ours, so its details are written down rather than rediscovered
(R-PROSE's first exemption). Re-fetch and re-check before leaning on a rule
that decides a change; the CRD moves.

## The three error times (2.2)

The spec sorts every "must" into when it is caught, and the distinction is
load-bearing for a code generator because two of the three are things we
can hand the browser and never hear about:

| kind | when | what it means for us |
|---|---|---|
| **shader-creation error** | `createShaderModule` | The module is rejected. `getCompilationInfo` names it. This is the good case: it is visible. |
| **pipeline-creation error** | `createComputePipeline` | Rejected later, only for code reachable from the entry point. |
| **indeterminate / dynamic** | at execution | No diagnostic anywhere. Wrong pixels. |

Several rules below are errors ONLY when the operand is a const-expression
(8.1.1: an expression the implementation can evaluate at module-creation
time, built from literals, `const` declarations and `@const` functions).
The same expression written with a runtime value is not an error and has
defined behaviour instead. So a construct can be legal in every shader we
have shipped and be a shader-creation error the first time a constant
reaches it.

## Identifiers (3.7)

An identifier is `XID_Start`/`XID_Continue` (non-ASCII letters are fine:
`Δέλτα`, `朝焼け`), **with three exceptions that are shader-creation errors**:

- it must not have the same spelling as a **keyword or a reserved word**;
- it must not be `_` alone;
- it must not **start with `__`** (two underscores).

Two identifiers are the same iff they are the same sequence of code points
(3.7.1). There is no case folding and no `-`: a Codex name is not a WGSL
name until it has been through a real sanitizer.

### Keywords (16.1)

```
alias break case const const_assert continue continuing default diagnostic
discard else enable false fn for if let loop override requires return
struct switch true var while
```

### Reserved words (16.2), all 146

A module **must not contain** one, even as a variable name.

```
NULL Self abstract active alignas alignof as asm asm_fragment async
attribute auto await become cast catch class co_await co_return co_yield
coherent column_major common compile compile_fragment concept const_cast
consteval constexpr constinit crate debugger decltype delete demote
demote_to_helper do dynamic_cast enum explicit export extends extern
external fallthrough filter final finally friend from fxgroup get goto
groupshared highp impl implements import inline instanceof interface
layout lowp macro macro_rules match mediump meta mod module move mut
mutable namespace new nil noexcept noinline nointerpolation non_coherent
noncoherent noperspective null nullptr of operator package packoffset
partition pass patch pixelfragment precise precision premerge priv
protected pub public readonly ref regardless register reinterpret_cast
require resource restrict self set shared sizeof smooth snorm static
static_assert static_cast std subroutine super target template this
thread_local throw trait try type typedef typeid typename typeof union
unless unorm unsafe unsized use using varying virtual volatile wgsl where
with writeonly yield
```

The ones a Codex chapter can plausibly produce by accident are worth
naming: `match`, `type`, `mod`, `set`, `get`, `use`, `with`, `where`,
`self`, `new`, `class`, `enum`, `filter`, `final`, `common`, `shared`,
`precise`, `static`, `import`, `export`, `module`, `package`, `pass`,
`target`, `template`, `partition`, `of`, `from`, `do`.

### Predeclared names are shadowable, which is the hazard (3.6)

Built-in function names are **predeclared, not reserved**, so a module-scope
declaration of `fn min(...)` is legal and **hides the built-in over the
entire source**. The spec gives exactly that as its example. A generator
that lowers one Codex builtin to `min` and also emits a user helper named
`min` produces a module that compiles and computes something else.

## Operators

### Logical (8.7)

| form | operands | note |
|---|---|---|
| `!e` | `bool`, `vecN<bool>` | |
| `e1 \|\| e2` | `bool` only | **short-circuits**; evaluates `e2` only if `e1` is false |
| `e1 && e2` | `bool` only | **short-circuits**; evaluates `e2` only if `e1` is true |
| `e1 \| e2` | `bool` or `vecN<bool>` | **evaluates both** |
| `e1 & e2` | `bool` or `vecN<bool>` | **evaluates both** |

`&` and `|` on booleans are legal WGSL, so lowering a short-circuiting
source `and` to `&` is accepted by every validator and quietly evaluates
the right operand. `&&`/`||` are the spelling that preserves the meaning.
There is no `^^`.

### Arithmetic (8.8)

`+ - * / %` over `AbstractInt`, `AbstractFloat`, `i32`, `u32`, `f32`,
`f16`, and vectors of those. Unary `-` takes `AbstractInt`,
`AbstractFloat`, `i32`, `f32`, `f16` and their vectors: **not `u32`**.
Negating the most negative integer yields itself.

There is **no exponentiation operator**. `pow` is a built-in and is
float-only (17.5).

Integer division and remainder do not trap:

- `e2 == 0`: shader-creation error if `e2` is a const-expression,
  pipeline-creation error if it is an override-expression, otherwise the
  result is `e1` for `/` and `0` for `%`.
- signed `INT_MIN / -1` and `INT_MIN % -1`: same three-way rule, otherwise
  `e1` and `0`.

Signed `/` truncates toward zero and `%` takes the sign of `e1`.

### Comparison (8.9)

`== != < <= > >=` yield `bool` (or `vecN<bool>`), operands of matching
scalar or vector type.

### Bit expressions (8.10), the section a generator gets wrong

`~e`, `e1 | e2`, `e1 & e2`, `e1 ^ e2`: operands and result all the same
`T`, where `T` is `AbstractInt`, `i32`, `u32`, or a vector of those. These
are straightforward.

**The shifts are not.** For `e1 << e2` and `e1 >> e2` with concrete `e1`:

- `e1` is `i32` or `u32` (or `vecN` of those);
- **`e2` must be `u32`** (or `vecN<u32>`). An `i32` shift count does not
  compile. This is the single rule most likely to be missed, because every
  other binary operator wants matching operand types.
- The shift amount is **the value of `e2` modulo the bit width of `e1`**.
  If `e2 >= 32` it is a shader-creation error only when `e2` is a
  const-expression, and a pipeline-creation error when it is an
  override-expression; at runtime the modulo is what happens.
- `>>` on a **signed** `e1` is **arithmetic**: "If `e1` is negative, each
  inserted bit is 1, and so the result is also negative." A **logical**
  right shift of an `i32` therefore has to go through `u32` and back.
- `<<` on a signed `e1` where the discarded bits differ from the sign bit
  is an overflow, and again an error only for const- and
  override-expressions.

Masking a shift count with `& 31u` is exactly the modulo the spec already
performs at runtime, and it converts the const-expression error case into
the same defined answer. It is a widening of what compiles, not a change
to what any legal program means.

### Precedence (8.20)

`&`, `|`, `^`, `<<`, `>>` have no precedence relationship with each other
or with the arithmetic operators in WGSL's grammar: the language requires
parentheses rather than ranking them. Fully parenthesising every emitted
binary expression sidesteps the whole table.

## Literals (3.4, 6.1)

- Unsuffixed integer literal is `AbstractInt`; `123i` is `i32`, `123u` is
  `u32`. Unsuffixed float is `AbstractFloat`; `1.0f` is `f32`.
- **"A shader-creation error results if an integer literal with an `i` or
  `u` suffix cannot be represented by the target type."** An `AbstractInt`
  that must materialise as `i32` and does not fit is likewise rejected:
  `4278190080` is refused with "value 4278190080 cannot be represented as
  i32", which is why an opaque-alpha constant has to be written
  `bitcast<i32>(4278190080u)`.
- WGSL has almost no implicit conversion (6.1.2): only from abstract types
  and buffer pointers at call boundaries. `i32` to `f32` needs `f32(x)`.

## bitcast (17.2.1)

`bitcast<T>(e)` where `S` and `T` are each one of `i32`, `u32`, `f32`
reinterprets the bits; same-type is the identity. So
`bitcast<i32>(bitcast<u32>(x) >> c)` and `bitcast<f32>(1065353216u)` are
both well-formed.

## select (17.3.3)

```
@const @must_use fn select(f : T, t : T, cond : bool) -> T
```

**The false value comes first.** `select(f, t, cond)` returns `t` when
`cond` is true. Both arms are ordinary arguments and both are evaluated,
so lowering a Codex `if` to `select` is only sound when neither arm can
fault or diverge.

## Built-in functions we lower onto (17.3, 17.5)

Float-only, so an integer argument must be converted first: `acos acosh
asin asinh atan atanh atan2 ceil cos cosh degrees exp exp2 faceForward
floor fma fract frexp inverseSqrt ldexp length log log2 mix modf normalize
pow quantizeToF16 radians reflect refract round saturate sign sin sinh
smoothstep sqrt step tan tanh trunc`.

Defined for integers as well as floats: `abs`, `min`, `max`, `clamp`,
`sign` (`sign` is float-only in the CRD; check 17.5 before using it on an
integer), `dot`.

Integer-only: `countLeadingZeros countOneBits countTrailingZeros
extractBits firstLeadingBit firstTrailingBit insertBits reverseBits
dot4I8Packed dot4U8Packed`.

`f32(x)`, `i32(x)`, `u32(x)` are value constructors, not built-in
functions, and are the way to convert between numeric types.

## What this means for the emitter

The rules above sort into two piles, and the second is the one that costs
us. Some violations are **loud**: a bad identifier, an `i32` shift count,
a literal out of range, a call to a function that does not exist. Chrome
refuses the module and `getCompilationInfo` says why, which is a runner.

The rest are **silent**: `&` where the source said short-circuiting `and`,
an arithmetic `>>` where the source said logical, a `select` whose arms
both evaluate, a predeclared name shadowed by a helper. Every one of those
produces a module that validates and computes the wrong thing, and no
sweep over "does it compile" can see any of them. That asymmetry is why
the plug's rule is that anything it cannot lower **refuses by name** rather
than emitting a plausible value (L-BAILVALUE), and why
`apps/gpushow/tools/validate-all.mjs` grading every shader clean is
evidence about the first pile only.
