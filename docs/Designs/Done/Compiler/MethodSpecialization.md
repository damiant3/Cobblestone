# Method Specialization

The first production tranche materializes finite closed dictionary schemas and
retains checked declarations in version 1 `method-templates` metadata.
The implementation is `codex/compiler/IR/MethodSpecialization.codex`.
General body cloning and field-local forall are outside this contract.
Independently polymorphic uses through one local dictionary are
`docs/Designs/Done/Compiler/MethodLocalPolymorphism.md` (COMPILER-83): a
projection of a generated dictionary reaches this pass as a direct method use.

## Production boundary

Desugaring retains class schemas, method-local binders, instance heads,
constructor declarations and recipes, and implementation target identities.
CHECK validates these origins against the generated declarations.
`method-capture-inputs` retains origins and checked target types through
the existing frontend arena transitions.

Materialization runs after lowering, type resolution, required equality
helpers and normal reachability pruning, before native or IR text emission.
Native export roots remain part of reachability. IR-UNI and IR-CCE use the
same preparation path. A fresh reservation precedes metadata allocation,
including the no-lift path used by quotation inspection.

A class can materialize only with one resolved closed instance head, closed
observed uses, representable slot types and no runtime demand for the original
dictionary layout. The scan examines signatures, expressions, closures and
other type declarations. Runtime construction, projection, escape, reflection
and unresolved uses preserve the original path and record an ineligible
reason. Dictionary-like names do not establish trusted origins.

Existing checked method definitions and calls are reused. Generated method-body
count is zero. Structural keys include complete closed use types, nominal names
and arguments, numeric representation, effects and scopes. Methods have separate
keys. Deterministic sorting compares complete keys and deduplicates equal keys.
Slots sum independent method demands rather than forming a cross-product.
Method-local variables never become shared dictionary parameters or defaults.

Zero demand retains the checked schema and constructor recipe in metadata
without inventing a runtime field type or executable constructor. Normal
unreachable-definition pruning still applies. Monomorphic dictionaries retain
their existing schemas. Ineligible typed output remains unsupported.

## Version 1 carrier

`IRTextMeta.method-templates` is compiler-produced CCE text. No classes means
an empty value and no section. The top-level section contains:

- `version`: exactly 1.
- `resources`: unit source bytes, available phase capacity, measured
  planning/content allocation and total input-derived byte bound.
- `logical`: one identity per class, method, instance and implementation.
- `templates`: original schemas, separate binder scopes, checked constructor
  recipes, implementation identities and checked target signatures, lifecycle
  state, observed-use count, zero cloned bodies, byte bound and generated slots.

IDs combine declaration kind, file ID, source offset and name. IDs belong to
one artifact, not a stable ABI. Every positional declaration occurs once in the
logical inventory. References and materializations link to those declarations.
Class, method and instance binders have separate scopes. Recipes retain semantic
expression fields and binders; declaration spans retain original unit offsets.

Schema, declared-signature and slot fields use the source AST wire grammar.
Checked targets and slot arguments use the IR type grammar. The existing
`a-forall` proposition form is not a runtime field quantifier.

`IRTextParser.ir-method-template-section` and
`IRTextEmitter.emit-ir-chapter-prefix` preserve the section through inspection.
The common parser preserves metadata but does not authenticate it. The
independent checker rejects duplicate sections, unsupported versions, forged
scopes and incomplete inventories. Frozen runtime readers ignore the section;
their execution proves runtime compatibility separately from metadata validity.

## Bounds and costs

The pass scans a fixed checked graph and discovers no new bodies. For N
observed eligible uses and U unique method/type keys, U is at most N.
Each slot copies its source signature with closed substitutions. Bounds charge
actual text, schema, recipe, retained checked-type and observed-use sizes with
checked arithmetic. Planning bytes plus the bound must fit below phase
capacity. Measured content allocation and serialized length must fit within
the bound; an additional 8 KiB margin remains after publication.
The 2 GiB compiler memory contract remains binding.

Heap/time verdict: metadata has an input-dependent compiler cost. IR is scanned
once per eligible class. Sorting uses O(N log N) full-key comparisons, whose
cost depends on key length. Origin validation and name/binder lookup include
repeated linear searches; no whole-pass linear-time claim is made. Retained
storage includes origins, observed use types/keys, concrete slot signatures
and serialized metadata. Slot storage scales with the sum of signature sizes,
not just slot count. Eligible dictionaries have no reachable runtime layout
use, so materialization adds no runtime dictionary allocations. Existing
method bodies and monomorphic runtime dictionaries are unchanged.

Measured 2026-09-19: the small and nested-type fixtures each have one use and
one slot, but bounds differ: 160,000 and 176,384 bytes per template. The
independent checker reproduces both estimates. Whole-compiler CDX stages
complete with a 2,048 MiB guest cap; a cap is not a measured heap high-water
value. TEXT output is byte-identical under depot and candidate compilers on
the same final source. Both report a phase peak of 1,174,346,355 bytes; elapsed
VM times are 5.536 and 5.573 seconds respectively. The semantic comparison
passes. These measurements establish no performance improvement.

## Acceptance and reproduction

The maintained run sheet is `codex/test/method-template-contract-checks.md`.
All guest runs require fresh RAM admission and run serially for this proof.

- Nine fixtures distinguish zero demand, six uses/five unique slots, lookalike
  names, runtime dictionaries, escaping dictionaries, monomorphic dictionaries,
  small/nested type keys and retained Unicode recipes.
- Source-derived inventories grade exact CCE offsets, scopes, recipes,
  slot/use accounting and input-derived bounds.
- A same-source compiler control bypasses exactly the materialization call.
  Comparing complete runtime definitions grades body reuse independently of
  the producer's zero-clone flag.
- Actual IR-UNI/IR-CCE and parser/emitter round trips use production sources
  and a raw CCE transport.
- Host sabotage controls corrupt declaration accounting, scopes, recipes,
  targets, keys, slots, bodies, source offsets and bounds.
- Eleven executable producer controls include real phase-capacity refusal
  with poisoned compaction, one exact diagnostic and no wire on rejection.
- Native and both frozen Zig-reader outputs grade supported fixtures and
  the typeclass/equality regressions. The escape fixture preserves native
  output and retains the known free-binder typed refusal; the runtime
  fixture's projection is a direct method use and materializes.

Required regressions are `typeclass-poly`, `typeclass-smoke`,
`typeclass-compound-head`, `typeclass-instance-types` (the direct mixed-type
specimen), `typeclass-instance-nested`, `typeclass-superclass-types` and
`eq-generic-fields`. `errors/typeclass-instance-mismatch` pins rejection of
a concrete instance mismatch. These proofs do not close COMPILER-83.

Proof artifacts measured 2026-09-19 are under
`D:/Projects/Cobblestone-root/build-output/method-resume/`.
The unsigned candidate is
`CE23CA1C82DD00CAAC84811A1451AE39E5EDD40F4BC68D8B3392240313FEB356`;
the signed candidate is
`BD66718CBE24F589E8AD8E9D7AAC47289081B00A8824ECD22DBDE77321279541`.
Stages 1, 2 and 3 agree byte-for-byte. The candidate verifies its signature.
The BVT reports 79 compile passes, 64 runtime passes, zero failures and a
passing two-member batch control. This focused proof does not claim a full
release battery or public publication.
