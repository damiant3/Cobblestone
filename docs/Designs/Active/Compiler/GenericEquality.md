# Generic equality by dictionary passing

*Proposal, val 2026-09-25, for after Update 63 (Damian, via root). Docs only.
Every claim about the compiler cites the line it was read from on main 29009.*

## The problem, and what ships first

`==` on a value whose type is a type variable compares two addresses. With
`same : a, a -> Boolean` and `same (x) (y) = x == y`, `same` answers False for
two equal Texts built apart, two equal lists and two equal vectors, and True
for `same 7 7` (COMPILER-103). The cause is `lower-eq-dispatch`
(`IR/Lowering.codex:756-763`) and `lower-eq-dispatch-named` (`:773-786`): they name a helper from the operand's type,
`type-name-of` answers `""` for a type variable (`:809-820`), and lowering
falls back to `IrBinary IrEq`, which x86-64 emits as a register compare
(`Emit/X86_64.codex:3033-3043`).

The refusal ships first (blu; not yet on main at 29009, so CDX2100 is not in
`Core/CdxCodes.codex` in this tree): `==` and `/=` on a type variable of the
definition's signature are CDX2100. **This design is what replaces that
refusal for a signature that asks for equality**, and it leaves the refusal in
place for one that does not.

## The surface

```
same : Eq a => a, a -> Boolean
same (x) (y) = x == y
```

`Eq a =>` is the existing constraint syntax (`Syntax/Parser.codex:212-214`;
`desugar-constraint`, `Ast/Desugarer.codex:354-365`). Inside a definition
whose signature carries `Eq a`, `==` and `/=` on two values of type `a` are
legal and mean structural equality at whatever type `a` is instantiated to.
Without the constraint, CDX2100 stands, and its message names `Eq a =>` as the
fix. The constraint is explicit, as `Show a =>` and `Ord a =>` already are: no
inference of constraints is proposed.

## The mechanism: the derived-class path, with Eq added

Show and Ord already pass a function as their dictionary, and equality is the
same shape. The pieces that exist, and the change each needs:

1. **`is-derived-class`** (`Ast/Desugarer.codex:1142-1143`) accepts `"Show"`
   and `"Ord"`. Add `"Eq"`, and with it: a `__dderiv-Eq` builtin beside
   `__dderiv-Show` and `__dderiv-Ord` (`Types/Builtins.codex:99-100`);
   `derived-dict-type` (`Desugarer.codex:1160-1162`) answering `a -> a -> Boolean`
   for Eq (it sends every non-Show class to `a -> a -> Integer` today); and
   `derived-class-method` (`:1155`) answering, for Eq, a name no call can match,
   because Eq has no named method (its body rewrite is step 4) and it sends every
   non-Show class to `compare` today, which would reroute `compare` calls in an
   Eq-constrained body through the Eq dictionary.
2. **`rewrite-derived-constrained`** (`:1164-1182`) prepends a parameter
   `__d-<Cls>-<var>` typed by `derived-dict-type`. For Eq that type is
   `a -> a -> Boolean`, the type every `__eq_<T>` helper already has
   (`gen-eq-def`, `:883-896`). **Change:** it acts only when the FIRST
   parameter's type is the variable (`first-param-is-tyvar`, `:1166`); Eq
   needs any parameter that mentions it, because `count : Eq a => List a, a ->
   Integer` is the common shape. `collect-constrained-names` (`:1573-1576`) makes
   the same first-parameter check before it registers the function for
   `__dderiv-<Cls>` insertion, and must change with it, or the parameter is
   added and no call site passes it. Without both changes such a signature
   keeps CDX2100, which is safe.
3. **The call site.** `insert-dict-refs` (`:1600-1612`) inserts
   `__dderiv-<Cls>` as the first argument of a call to a constrained
   function, and `lower-derived-apply` (`IR/Lowering.codex:522-534`) replaces
   it with `<prefix><type-name-of arg-ty>`. For Eq the prefix is `__eq_`.
   **Two changes are needed there, and both are gaps the Show and Ord paths
   have today:**
   - **The argument whose type decides the dictionary.** `lower-derived-apply`
     reads the type of the call's FIRST argument. For `count xs y` the
     dictionary is for the element type, not `List`. The resolver must read
     the type the constrained variable is bound to at this call: match the
     callee's declared signature against `func-ty`, the callee's type at this
     site (`IR/Lowering.codex:525`), and take the image of the variable.
     **Verify first** that `func-ty` is the INSTANTIATED type and not the
     generalized one; if it is generalized, the checker must record each
     call's instantiation before this step can be written.
   - **No fallback.** `type-name-of` of a type variable is `""`, so the name
     built is `__eq_` and nothing checks that it exists. When the bound type
     is the CALLER's own constrained variable, the dictionary is the caller's
     `__d-Eq-<var>` parameter, passed on. `lower-constrained-apply`
     (`:499-520`) already does exactly this for user classes (its fallback at
     `:512-520`), and that is the code to copy. A bound type that is neither
     concrete nor the caller's constrained variable is CDX2100 at the call.
4. **The body.** Show and Ord rewrite a named method call in the desugarer
   (`rewrite-derived-method-calls-scoped`, called at `:1173`), before any type
   is known. `==` is an operator whose operand types exist only after
   inference, so its rewrite belongs in lowering: in `lower-eq-dispatch`, an
   operand whose resolved type is the constrained variable of the enclosing
   definition lowers to `__d-Eq-<var> l r`, and `/=` to its negation, the
   wrapping `lower-eq-dispatch-named` already does for `/=` (`:785-786`). A
   signature carries one constraint (`desugar-constraint` takes the first
   argument only), so the enclosing definition has at most one `__d-Eq-*`
   parameter and the choice is unambiguous.

## Which dictionary each type gets

The resolver in step 3 maps the bound type to a function value:

| bound type | dictionary |
|---|---|
| Integer, Boolean, Char, Text | a wrapper `__eq_<T>` (`__eq_Integer`, `__eq_Text`), added beside the Show and compare wrappers `prepend-prim-wrappers` already emits (`Ast/Desugarer.codex:1346-1354`); named so, the resolver's `prefix & type-name-of` (`IR/Lowering.codex:529`) finds it with no special case |
| a record or sum with a helper | `__eq_<T>`, or `__eq_<T>@<actuals>` for a generic type at concrete actuals (`Emit/X86_64.codex:3004-3007`); see minting below |
| `List T` | `__eq_List@<T>`, the helper `lower-eq-dispatch` already calls (`IR/Lowering.codex:762`); see minting below |

**Minting a helper passed as a value is NEW work.** Instantiated helpers are
minted in the x86-64 emitter by `eq-helper-defs` (`Emit/X86_64.codex:3688-3690`),
whose collector `eq-collect-expr` (`:3320`) records a helper name only at the
HEAD of an application (`:3334-3336`) and ignores a bare `IrName` in argument
position (`:3326`); `eq-close-reqs` (`:3389-3396`) closes over what was recorded.
A dictionary is exactly a bare name in argument position, so the collector
must also record an `__eq_`/`__eqd_` name there, or the call links to a helper
nobody minted. `eq-attach-helpers` (`opening.codex:1808-1812`) appends the minted
helpers to the plug wire as well, so the plugs receive them the same way.
| a withheld type (a Real, Vector or SizedVec field, `eq-withheld-set`, `Ast/Desugarer.codex:699-735`) | none: CDX2099 at the call, as for `==` on it directly |
| Real, Real approximate | none: CDX2085 at the call, as for `==` on it directly |
| a packed Vector | none: CDX2099 at the call. A vector's `==` is lane-wise and answers a mask, never a Boolean, so there is no `a -> a -> Boolean` to pass |
| the caller's constrained variable | the caller's `__d-Eq-<var>` (step 3) |

**Generic types inside generic code.** `__eq_Maybe@<actuals>` is minted per
concrete instantiation. Inside `same` at `Maybe a`, the actuals are a type
variable and no helper can be minted; this is the gap `Lowering.codex:745-748`
names for generic sums, and the helpers' own fields fall back to `plain`
`IrEq` there (`Emit/X86_64.codex:3425`, `:3446`). The design adds a
DICTIONARY-TAKING helper per generic type, `__eqd_<T> : (a -> a -> Boolean)
-> T a -> T a -> Boolean` (one dictionary per type parameter, in declaration
order), minted in the emitter beside `__eq_<T>@<actuals>` by the same
`eq-helper-defs` request mechanism (not by `gen-eq-def`, which builds only the
parameterless `__eq_<T>` in the desugarer, `Ast/Desugarer.codex:883-896`); it
compares a field of the parameter's type through the dictionary argument. A
site with concrete actuals keeps calling `__eq_<T>@<actuals>`, unchanged and
at no new cost; only a site whose actuals are constrained variables calls
`__eqd_<T>` with the dictionaries it holds. `List` gets `__eqd_List` the same
way.

## Cost (R-COST)

- A constrained call passes one extra argument, a function value that exists
  already (no allocation).
- Each `==` inside constrained code is an indirect call instead of an inline
  compare. Monomorphic code is unchanged: every site whose type is concrete
  lowers exactly as today.
- `__eqd_<T>` adds one helper per generic record or sum type that is compared
  inside constrained code; the worklist mints it only when requested.
- No heap growth per comparison beyond what the helpers already do.

## Targets

The dictionary is a function value, the same thing `__compare_<T>` is when
`Ord a =>` passes it today, so every plug that serves a constrained Ord call
serves this. ada, cobol, pascal and babbage cannot pass a function value and
already refuse that case by name (plugs-backlog 1.59).

## Acceptance

One `codex/test` chapter, every line printing the generic answer beside the
direct `==` on the same values (the direct operator is the oracle):

- `same` at Integer, Text built apart, `List Integer`, a user record and a
  user sum, each once equal and once unequal.
- `same` at a generic sum at concrete actuals (`Maybe Text`) and, through a
  second constrained function, at `Maybe a` (the `__eqd_` path).
- One constrained function called at two types in one program, which fails if
  a dictionary is resolved once per definition instead of once per call.
- A constrained function calling another with its own variable (step 3's
  fallback).
- `count : Eq a => List a, a -> Integer`, the non-first-parameter shape.

Refusals, one `codex/test/errors` chapter each: `same` at `Real` (CDX2085), at
a packed vector (CDX2099), at a record holding a Real (CDX2099), and `==` on a
type variable with no `Eq a =>` (CDX2100).

**Blast radius:** blu's census found 0 of 2,206 compiling programs reaching a
polymorphic `==` (2026-09-25), so no existing output changes; re-run that
census on the candidate before landing, because it is the only instrument that
says so.

## Not proposed

- Inferring `Eq a` from a body that compares `a`: every class here is
  explicit today, and an inferred constraint changes a signature the reader
  did not write.
- More than one constraint per signature, or a constraint on a second
  variable: `desugar-constraint` takes one, and lifting that is its own design.
- A user-declared `class Eq`: no chapter in the tree declares one (the tests
  use `Equatable`); once `Eq` is a derived class, a user class of that name is
  shadowed, and the parser should refuse the declaration rather than let it.
