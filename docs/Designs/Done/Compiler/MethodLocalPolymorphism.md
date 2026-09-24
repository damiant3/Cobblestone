# Method-local polymorphism in class dictionaries (COMPILER-83)

Owner: red. COMPILER-83, closed; the standing boundary is `codex/plugs/plugs-backlog.md` 2.66.

## The defect

`class Converter where convert : a, b -> b` binds `a` at the instance and
must bind `b` anew at every use of `convert`. The dictionary the desugarer
synthesizes cannot say that. `synth-class-type-defs` (`Ast/Desugarer.codex`,
near line 1020) builds `ConverterDict` with the single parameter `a`, and
`synth-class-fields` copies the method's type expression into the field
unchanged, so `b` is one free variable of the record type, shared by every
projection of that field and by every method that happens to spell `b`.

Measured by fester (2026-09-10, candidate-v2): both controls stop before IR
with CDX2001 Integer vs Text on the second use.

- `same-dictionary-two-types`: one method of one dictionary used at Integer,
  then at Text.
- `independent-method-variables`: two methods each spelling `b`, used at
  Integer and Text.

The typed backend fails for the same root reason: the Zig plug meets a
record field whose type has a free variable, and a struct field cannot hold
a generic function (plugs 2.66, the refusal in `emit-zig-record-fields`).

## Rejected

- **Promote `b` to a dictionary parameter.** It fixes `b` per dictionary
  value, which is exactly the coupling the two controls show.
- **Default `b`, or delete the declaration.** Neither is a semantics.
- **Erase the field in Zig to `anytype`.** Only a comptime-only struct can
  hold one, and a dictionary is a runtime value.
- **Reuse `AForallType`.** It is the proposition and value-binder form, and
  `resolve-type-expr` maps it to `ProofTy`; it is not a runtime quantifier
  (`MethodSpecialization.md`, "Version 1 carrier").

## Stage 1: fresh method-local variables at each projection (checker, landed)

**The coupling.** `register-one-type-def` (`Types/TypeChecker.codex`)
parameterizes a record's whole CONSTRUCTOR type once, so `b` in a dictionary
field is one `TypeVar` under the constructor's `ForAllTy` chain, and
`strip-fun-args` hands that same id to every projection. The `ConstructedTy`
arm of field access (`Types/TypeCheckerInference.codex`) substituted only the
record's own parameters (`a`), so the first use of `b` fixed it for every
later one. A direct call to a class method already instantiates per use
(`register-cm-ops` binds the method's own parameterized type), so only
projection needed the change.

1. **Origin.** `desugar-class-defs` already gives each `AClassDef` its
   `dict-schema` and its per-method `method-binders` (free type-variable
   names other than `a`). `register-class-methods` turns them into one env
   entry, `__method-local`: per dictionary record with a binder-bearing
   method, the `-impl` fields that carry binders. The origin is the class
   definition, never the `Dict` name suffix.
2. **Projection.** In the `ConstructedTy` arm, a field listed there has every
   constructor `ForAllTy` id that is not a record parameter replaced by a
   fresh variable (`freshen-method-local`). A field with no binders, and every
   record of a chapter without such a class, takes no new path beyond one
   failed `env-has`.
3. **Construction.** Freshening is sound only because the dictionary's
   methods are checked polymorphic: each instance method is also synthesized
   as `m-T` with declared type `a := T` and `b` rigid, and a body that fixes
   `b` is refused with CDX2087 (`errors/typeclass-method-local-fixed-binder`).
   A record expression written in source that builds such a dictionary would
   bypass that check, so it is refused with CDX2098 (`ClassDictByHand`,
   `errors/typeclass-dict-by-hand`); the desugarer's own construction has a
   synthetic span and is exempt.
4. Nothing changes in lowering or the native backends: the field type after
   projection is an ordinary type, and a field is one word at run time.

**Acceptance.** `codex/test/typeclass-method-local-two-types` and
`codex/test/typeclass-method-local-independent` (fester's controls) fail with
CDX2001 on the pre-change seed and print both types after it.
`errors/typeclass-instance-mismatch` still refuses (the class parameter `a`
still couples). The typeclass regressions `MethodSpecialization.md` lists
(`typeclass-poly`, `typeclass-smoke`, `typeclass-compound-head`,
`typeclass-instance-types`, `typeclass-instance-nested`,
`typeclass-superclass-types`, `eq-generic-fields`) stay green.

**Cost (R-COST).** One fresh variable per binder per projection of a listed
field, and no allocation on any other field access or record expression: the
env key is a constant. The compiler's own source declares no class, so its
CHECK phase takes none of the new paths.

## Stage 2: a projection of a generated dictionary is a direct method use (landed)

**What the typed path meets, measured on seed EDC80C7F (2026-09-24).** Both
controls lower to `let d = IndependentMethods-dict-Integer` followed by
`d.first-method-impl` applications. MethodSpecialization records the class
`ineligible "runtime-type-use"` because the `let` carries the dictionary type,
so no slots exist. Independently of that, lowering does not carry stage 1's
per-projection type into IR: the `AFieldAccess` arm (`IR/Lowering.codex`)
recomputes the field type from the record constructor, so the Text use is
typed `fn int (fn int int)`. The native backend is indifferent (a field is
one word); a typed backend is not. The instance's own implementations,
`first-method-Integer` and `second-method-Integer`, are checked with `b`
rigid and are MethodSpecialization targets already, but nothing references
them, so reachability prunes them.

**The change.** After `synth-instance-defs`, the desugarer rewrites every
projection of a generated dictionary value onto the implementation it holds:

1. `C-dict-T.m-impl`, and `d.m-impl` where `d` is bound by a `let` to
   `C-dict-T` and not shadowed, become the name `m-T`, for every method `m`
   of `C` that has method-local binders. The map from dictionary name to its
   fields comes from the instance definitions the desugarer itself turned
   into those defs, never from the name's spelling.
2. A `let` whose bound name no longer occurs after the rewrite is dropped.
3. Monomorphic methods, superclass fields, and every other use of the
   dictionary (passed, returned, stored, captured) are left alone.

After the rewrite each use is an ordinary reference to a def whose declared
type quantifies `b`, so the checker instantiates it per use, lowering types
each use from that instantiation, and MethodSpecialization sees direct
target uses. The slot machinery is unchanged: one slot per method and closed
use type, summed, which is what the `demands` fixture already carries through
both frozen Zig plugs. Behaviour is unchanged natively: the field value and
`m-T` are the same instance body.

**The fixture that moved.** `method-template-contract-runtime` has the same
shape as the controls, so it is materialized with one slot. The ineligible
path is shown by `method-template-contract-escape` (a helper receives the
dictionary) and `typeclass-method-local-shadow` (a lambda parameter shadows
`d`), both of which keep one `-impl` projection in IR and the
`UNSUPPORTED_FREE_BINDER` result.

**Acceptance.** Both controls and the runtime fixture are exact through both
frozen Zig plugs against their native `.expected`; escape and shadow keep
the free-binder refusal; the typeclass regressions and the stage 1 tests
stay green natively. In IR both controls carry no `-impl` projection and a
`materialized` template with two slots.

## Stage 3: the boundary that stays

A dictionary that is constructed at run time, escapes, or is used at open
types is ineligible for materialization today, and it stays ineligible here.
On typed backends it keeps the by-name refusal plugs 2.66 records. Lifting
that needs a boxed representation of a generic field, which is a separate
design and is not proposed.
