# One instance body, checked once (COMPILER-90)

Owner: red. COMPILER-90, closed by option 2: `ProvInstanceCopy1` and `ProvInstanceCopy2` (`Core/SourceText.codex`) mark the dictionary-lambda and bare-`m` copies (`stamp-copy-spans`, `Ast/Desugarer.codex`), and `expr-type-key` (`Types/Unifier.codex`) keys each copy apart. `ir-fidelity -Disagree` reads 0 sites over 678 clean programs, against 7 over 6 before.

## The defect, measured 2026-09-24 on seed 9A323747

`ir-fidelity -Disagree` over 678 clean programs reads 7 sites over 6, every
one an instance method `m (x) (y) = y` of `a, b -> b` whose def declares `y`
as `(tvar N)` and whose body uses `y` at `int-default`. A plain def of the
same signature reads clean.

The desugarer builds each instance method body up to three times from one
source expression: the dictionary field lambda (`synth-instance-fields`), the
specialized def `m-T` (`synth-specialized-methods`), and, for a class with
one instance, the bare def `m` (`synth-bare-methods`). The copies share every
span. The checker's type table is keyed by span alone (`expr-type-key`,
`Types/Unifier.codex`: file-id, start offset, capped length), and lowering
prefers the recorded type at a use's span over the binder's type
(`lower-name-normal`, `IR/Lowering.codex`). So a copy's `y` is typed with a
sibling copy's variable, which is foreign to the copy's def and reaches the
wire as `int-default`. The step that turns the foreign variable into
`int-default` is not located, and nor is which copy's entry wins the lookup
(`lookup-expr-type` binary-searches a sorted list; equal keys sort by check
order).

**The mechanism moves the symptom (L-MECHANISM), two experiments on a
scratch unit, not landed.** Each edit is one expression in
`Ast/Desugarer.codex`, so either is re-made in minutes:

- In `synth-instance-fields`, `field-val` for a method with parameters is
  `ANameExpr (m & "-" & head)` instead of the `ALambdaExpr` copy: 7 sites
  become 4 (`first-method-Integer` in two programs and `bound-value` clear).
- In addition, `synth-bare-methods` builds bare `m`'s body as `m-T` applied
  to its parameters instead of a third copy: 0 sites. The run named 8
  programs (the 6 flagged ones, `typeclass-poly`, `typeclass-smoke`); 7
  compiled and 1 was refused because the scratch edit spelled the head as a
  literal `-Integer`, which a non-Integer instance does not have.

Native output is unaffected by the defect (a word is a word), and both frozen
Zig readers already pass the COMPILER-83 fixtures, so the harm is to any
typed consumer that reads a name's type off the wire.

## The options

1. **One copy: the dictionary field and bare `m` refer to `m-T`.** The
   experiments' shape. It changes the dictionary constructor recipe
   MethodSpecialization retains (`(lambda (params ...) ...)` becomes a name),
   which the version 1 metadata and `method-template-contract-check.ps1`
   grade against the fixture source, and a bare `m` whose body calls `m-T`
   at an open `b` is an `open-method-use` that makes the class ineligible,
   so bare `m` would have to disappear in favour of rewriting its references
   to `m-T`, the way COMPILER-83 stage 2 rewrites projections.
2. **Distinct keys for the copies.** The desugarer stamps every span of the
   second and third copy with a copy index, and `expr-type-key` folds the
   index in. The key is `file-id * 2^48 + offset * 2^16 + capped length`, so
   its only room is above an offset bound: `copy * 2^44` needs offsets below
   2^28 (268 MB; the whole compiler unit is 3.3 MB), and a larger offset must
   refuse rather than wrap. The index needs a home in `SourceSpan` (a
   `Provenance` variant carrying it, which every span copier and every `when`
   over `Provenance` then learns). No contract outside the compiler moves.
3. **The consumer prefers the binder.** `lower-name-normal` takes a
   parameter's own type when the recorded type carries a variable foreign to
   the def. Smallest, but it repairs names only. The table is filled for
   names and record expressions (`Types/Unifier.codex`), and lowering reads it
   at more sites than `lower-name-normal`, so a copied body keeps reading a
   sibling's entry wherever those meet.

**Recommendation: option 2.** Option 1 is the cleaner end state, but it
changes the version 1 method-template contract, not only the compiler:
`method-template-contract-check.ps1` requires exactly two targets per
implementation (`:303`, the specialized and the bare name) and rebuilds every
recipe field as a lambda from the fixture source (`:278`), and the contract's
own acceptance (run sheet, selftests, frozen reader runs) re-proves against
that shape. Option 2 fixes the defect inside the compiler with the contract
untouched; it keeps the two or three checked copies, whose CHECK cost exists
today. Option 1 stays the right follow-up if the contract is revised for
another reason.

## What option 1 touches, read at head 2026-09-24

- **Bare `m` cannot forward to `m-T`.** MethodSpecialization's use scan
  records a use of a target name at an open type as `open-method-use` and
  makes the class ineligible (`method-observe-name`,
  `IR/MethodSpecialization.codex:782-796`), and a bare `m` whose body
  applies `m-T` at the declared, still-generic `b` is exactly that. So bare
  `m` is removed, every reference to it is rewritten to `m-T` (scoped, as
  `rewrite-dict-projections` does), and `desugar-instance-defs` lists only
  `m-T` in `method-targets`, because origin validation requires every listed
  target to exist as a def with the declared type and parameter names
  (`method-target-contracts`, `:166-175`).
- **The dictionary field becomes a name.** `method-instance-valid` compares
  the constructor def's body with the retained `constructor-body` (`:146`);
  both come from the same desugared def, so they stay equal. The version 1
  metadata recipe changes from `(lambda (params ...) ...)` to a name, and
  `codex/test/method-template-contract-check.ps1` rebuilds the lambda shape
  from source (`:278`) and requires two targets (`:303`), so the checker, its
  selftests and the run sheet change with it.
- A method with no parameters has its field value as the bare body today, so
  that branch becomes the same name.

## Acceptance

`ir-fidelity -Disagree` reads 0 sites over the whole corpus; the
typeclass regressions, the COMPILER-83 tests and the method-template
contract fixtures stay green natively; both frozen Zig readers keep their
results; the metadata checker is re-run over the fixtures whose recipes
change, and the run sheet says what changed. Two constraints the change must
keep: CDX2087 still fires on an instance body that fixes `b`, because the
soundness of COMPILER-83 rests on `m-T` being checked with `b` rigid
(`errors/typeclass-method-local-fixed-binder`); and a method with no
parameters, whose field value is the bare body rather than a lambda, still
builds.
