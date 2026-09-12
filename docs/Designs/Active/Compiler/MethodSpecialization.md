# Method Specialization

Status: the first production tranche below is authorized by RED. Root owns
implementation and critical integration; original shelves25558,25601,25599
remain preserved. General recursive specialization and field-local forall
are not authorized. The broader design describes future work, not additional
permission. PR135's hold remains until the required checks pass.

## First production tranche

Implement the finite schema materialization demonstrated by the original-IR
probe, with trusted declaration origins, structural scope validation and the
checked-template carrier below. Reuse existing method definitions unchanged.
Do not clone bodies, discover demands by recursively instantiating bodies,
or propagate open demands through arbitrary runtime dictionary values.

Eligibility is structural, never a fixture or generated-name allowlist:
generated polymorphic dictionary schemas with a resolved single instance,
closed observed method-use types and no reachable construction, projection,
escape, export or other runtime layout use of the affected dictionary.
Check references from signatures, expressions, closures and other type
declarations, not only calls. Zero observed method uses are permitted when
the same analysis proves no runtime layout demand and no unresolved use.
Ordinary monomorphic dictionaries retain their existing path.

Outside that proven surface, leave the existing family/path unchanged and
record the unhandled reason in the proof accounting. Do not introduce a new
rejection of accepted native source or silently select a global instance.
An unchanged unsupported typed-backend case is not a passing result. If a
required accepted fixture falls outside eligibility, report the boundary
instead of adding a fixture-specific exception.

**Zero-demand carrier.** Retain checked templates in compiler-owned typed-IR
metadata, serialized in a top-level `method-templates` section with schema
version 1 in both IR-UNI and IR-CCE. The owning boundary is completed lowering
with required derived helpers present, before native or text emission. Retain
origins from desugaring, validate them during checking, and preserve the metadata
through existing arena transitions until serialization. Each entry carries its trusted origin,
complete original dictionary schema, class/instance/method binder scopes,
checked constructor recipe and method declaration identities, plus its
zero-demand or materialized state and links to generated slots. Metadata is
created from checked compiler state, not accepted from user annotations as
proof. Every original logical declaration must have one accounting identity;
materializations link to that identity rather than masquerading as new source.

A zero-demand declaration has no invented runtime record or arbitrary field
type. Its declaration survives as a checked template, not as executable
constructor code. Normal reachability pruning and native export rules still
apply; the table records that lifecycle instead of claiming unused bodies
survive CDX. This is not a separate-compilation ABI or permission to materialize
an exported/open template. No field-local runtime forall or AForallType-as-type
encoding is introduced.

The materialization boundary must validate template scopes and total
declaration accounting before runtime IR is published. The IR metadata must
survive its supported compiler serialization/inspection path. Runtime-only
backends may ignore the explicitly non-runtime section; demonstrate that both
frozen Zig readers still consume all runtime sections correctly. A manifest
containing only names or an unchecked checked=true flag is insufficient.

**Resource bound.** The first tranche scans a fixed, already-checked IR graph.
Let N be eligible use sites and U be unique closed keys: U must be at most N.
Generated method-body count is zero. Slot counts are sums of each method's
unique demands, never a cross-product. Derive allocation/serialization bounds
from those counts and the actual input signature/key/schema sizes, with checked
arithmetic; charge them to the existing phase reservation and 2 GB compiler
heap contract before materialization. Do not replace a byte bound with a bare
use count or add an arbitrary fixed specialization-count ceiling.

Use collision-checked structural keys, share immutable types where their
lifetime permits, and include sorting/comparison costs in the verdict.
If the finite bound cannot be established, stop at the boundary rather than
falling back to an unbounded work list. General body-cloning specialization
needs a separate measured budget and ruling; no numeric cap is chosen for
that unimplemented expansion here.

First extend the scratch proof with zero-demand carriage, omitted-entry and
unbound-binder sabotage controls, runtime/escape ineligibility, unchanged
monomorphic dictionaries and input-bound accounting. Then implement this
tranche in a new numbered shelf based on combined25599, retaining every donor
and frozen plug. Validate the real producer/serializer path, not only a host
rewrite. Required typed/native fixtures, broader relevant regressions and full
R-GATE on the reconciled combined source precede seed promotion. No gate waiver
or COMPILER-83 completion follows from this ruling.

## Problem and existing boundary

`codex/test/typeclass-poly.codex` declares `Converter.convert : a, b -> b`.
The instance fixes a to Integer; a method invocation fixes b. A dictionary
type declaring only a cannot contain an unbound b in its callable field.
The accepted direct mixed-call specimen uses KeepValue at Boolean and Text.
Choosing one b for that dictionary would couple independent method calls.

Fester's boundary account is
`D:/Projects/Cobblestone-fester/build-output/instance-typing/method-polymorphism-boundary.md`.
The account distinguishes accepted direct calls from two checker-refused cases:
one local dictionary method called at Integer and Text, and two methods spelling
b independently called at different types. Both rejected cases produce CDX2001
before IR. COMPILER-83 owns that possible language extension; the malformed
declaration blocking accepted programs remains plugs-backlog2.54.

Relevant source at main25609:

| Mechanism | Source and contract |
|---|---|
| Dictionary schema | `Ast/Desugarer.codex:1055`, synth-class-type-defs/synth-class-fields, copies the method signature into a field and declares only a. |
| Existing method definitions | `Ast/Desugarer.codex:1549`, synth-specialized-methods, creates one named definition per instance method. That specialization chooses the instance head, not every method-local type argument. |
| Direct dispatch | `IR/Lowering.codex:511`, lower-method-apply/method-spec-rn, chooses the named instance method from the first argument type. |
| Constrained calls | `Ast/Desugarer.codex:1092`, rewrite-if-constrained; `IR/Lowering.codex:465`, lower-constrained-apply, pass the selected dictionary. |
| Definition instantiation | `Types/TypeCheckerInference.codex:214`, instantiate-type, freshens definition-level ForAllTy variables; subst-type-var preserves binder scope. |
| No field quantifier | `Types/TypeChecker.codex:26` resolves AForallType to ProofTy. That AST form cannot encode a generic callable field. |
| Typed backend | `codex/plugs/zig/ZigEmitter.codex`, emit-zig-type and function-field CxFn emission require the field's argument/result types to be in scope. Stripping ForAllTy is not field-local binding. |

Compiler paths in the table are relative to codex/compiler unless fully named.
Fester's candidate-v2 includes the instance/superclass annotation repair; the
design builds on that candidate, rather than replacing the repair.

## Proposed bounded representation

Retain each class, instance and method declaration as a checked template with
its original binder scopes. Materialize finite, closed method specializations
using ordinary definitions and ordinary record fields. The first implementation
unit is closed application compilation for the two accepted specimens and the
existing monomorphic dictionary regressions. No original declaration is deleted
to make a backend pass, and no unresolved variable is replaced by a default.

For example, KeepValue/Integer has one logical method with local binder b.
Boolean and Text demands produce two ordinary definitions and two fields in
the instance's concrete dictionary family:

```text
KeepValueDictInteger {
  keep-value-impl-boolean : Integer, Boolean -> Boolean
  keep-value-impl-text    : Integer, Text -> Text
}
```

The suffixes illustrate readable output only. Structural keys below determine
identity. The family is one dictionary layout containing independent callable
slots; b is never promoted to a shared dictionary type parameter. A direct call
can invoke its specialized definition directly. A dictionary projection chooses
the slot matching the checked method-use type and keeps the runtime receiver.
Existing record, closure and call IR forms can express those materializations.

The original method scheme remains in a specialization manifest with its source
declaration identity, binders, generated definitions, generated fields and uses.
Every emitted dictionary declaration must be structurally well scoped, including
declarations whose constructors are not runtime-reachable. A method with no
closed demand remains an explicitly checked template in the manifest; an empty
demand set is not permission to delete the declaration or invent a runtime field
type. Exporting or materializing such a method requires a further demand or a
separately approved representation. Template retention is new machinery,
authorized only within the first tranche above.

The first-tranche carrier above resolves representation policy; implementation
and zero-demand proof remain required. Existing closed-demand scratch cases
retain runtime declarations and do not establish the new carrier's correctness.
The general pass below remains outside the authorized tranche.

The narrowest observed repair reuses the existing generalized method definitions
and direct-call types unchanged. Only the malformed dictionary schema becomes a
family of concrete method slots. A scratch IR materializer demonstrates that
narrow case without cloning method bodies, removing any type/definition, or
rewriting existing monomorphic dictionary accesses. A general definition cloner
is needed only when concrete demands cannot be obtained from the existing typed
uses; the successful specimens do not prove that larger mechanism necessary.

## Identity and variable scopes

The method key is a tuple, not concatenated user spellings:

1. Resolved class declaration identity: quire, chapter and declaration ID.
2. Resolved instance declaration ID and its fully substituted structural head.
3. Method declaration ID within that class.
4. Ordered substitution for that method's locally bound type variables.
5. Resolved required evidence and effect-row identity.

Type identity includes nominal declaration identity and ordered arguments;
integer bounds and overflow mode; real width/mode; collection kind and element;
function argument/result/effect structure; vector widths; units; and linear
wrappers. Canonical effect labels include scopes. Hashes index keys, with full
structural equality on collisions. `type-key-of`'s display strings are not a
sufficient key for that contract. Generated names are deterministic IDs derived
from the sorted structural keys, with collision verification.

Class binders, instance-head binders, method-local binders and enclosing-definition
binders are distinct namespaces. Binder b in method1 is not binder b in method2.
Freshening occurs per ordinary definition instantiation, using existing
substitution machinery where applicable. The specialization pass does not change
the checker's present local-dictionary generalization rule. An unresolved
enclosing binder causes propagation to its owning definition's concrete demands,
not substitution with Integer, anyopaque, VoidTy or a free backend name.

## Demand collection and rewriting

First, check all source declarations and method bodies under their declared
scopes. Retain origin metadata through desugaring and lowering; synthesized text
names alone cannot recover class/instance/method identity reliably.

Then, seed demands from fully checked applications and concrete dictionary
projections. Reuse the existing instance-resolution result. Substitute the entire
method type, including nested arguments/results, and enqueue each structural key
once. Materialize a normal definition for each demanded method. Discover further
calls in each instantiated body until the work list closes. This is demand
specialization, not repeated syntactic copying at every call site.

Constrained functions preserve the explicit dictionary parameter. A concrete
call specializes the wrapper's type and the dictionary layout together; nested
constraints and superclass projections propagate the same instance substitutions.
The mono-superclass field must carry the applied superclass type, as in fester's
repair. Never remove a constraint because a method name looks globally unique.

Dictionary materialization uses a deterministic union of demanded slots for each
concrete class/instance family. Compute that union once after demand closure;
rewrite constructor fields, receiver types, field indices, closure types and call
types together. Do not cross-product unrelated methods' local type variables.
Every source method has a manifest entry even when no executable slot is demanded.
The layout identity includes the sorted slot-key set. Separately compiled
producers and consumers cannot exchange a family merely because class and instance
names agree; such exchange requires the same layout identity or an explicit adapter.
No separate-compilation dictionary ABI is established by this closed-unit probe.

An arbitrary runtime dictionary value is not interchangeable with the generated
global instance. Custom method closures, captured environments, evaluation order
and receiver identity must survive. A projection retains and evaluates its actual
receiver once. Method extraction as a monomorphic function retains the selected
closure/environment. Unknown dictionary provenance or a receiver requiring an
open family is outside the demonstrated tranche; record the unsupported demand
before implementation rather than silently selecting the global instance.

The pass must establish a well-scoped materialization boundary before typed IR
is serialized and before either native or hosted code emission. Existing checked
method schemes remain separately available; the pass must not reinterpret a bad
runtime field as a valid type merely by deleting its declaration.

## Supported surface versus new capability

| Surface | Disposition |
|---|---|
| typeclass-poly direct convert, sort-key and monomorphic superclass dictionary access | Required accepted-program repair. |
| Direct mixed Boolean/Text method calls plus Integer/Boolean/List Integer instances and constrained describe calls | Required accepted-program repair, represented by extra-method-variable.codex. |
| typeclass-smoke, instance-nested, superclass Integer/Boolean controls, eq-generic-fields | Existing typed regressions remain required; specialization does not waive their proofs. |
| Closed monomorphic method projections through generated dictionaries | Preserve; scratch model exercises ordinary fields and superclass access. |
| One local dictionary's method called at two types; two independently spelled b methods at different types | Checker currently rejects. Supporting those original source forms is COMPILER-83, not demonstrated or authorized here. |
| Open exported dictionaries, polymorphic recursion, arbitrary escaping/custom dictionaries needing open method families | Not established by this probe. Require separate acceptance and representation work before claiming support. |

For the future general pass, NEW required capabilities are origin/binder metadata retention, a structural
specialization cache/work list, typed demand propagation through constrained
definitions and dictionary values, a checked-template manifest, and a coordinated
record-layout/type rewrite. Existing per-instance definition synthesis and
ForAllTy instantiation are building blocks, not an implementation of that pass.
The first-tranche ruling authorizes only its stated subset of those mechanisms;
field-forall and general runtime dictionary rewriting remain outside scope.

## Termination, code size and heap costs

The first tranche uses the input-derived bound above and clones no bodies.
The following recursive planning costs describe future general specialization.

Cache entries have pending and complete states. Insert pending before visiting a
body, allowing same-key recursion to reference the pending definition without
cloning it again. Ordinary monomorphic recursion closes on the same finite keys.
Do not assume polymorphic recursion has finite demand closure. A cycle that grows
type structure requires a named diagnostic before materialization; a measured
specialization/code-size budget remains a second refusal boundary. No partial
program is emitted after either refusal. The exact budget and diagnostic need
RED's implementation ruling and measurements, not an arbitrary default in a plug.

Let B be reachable source-body size, U the number of unique specialization keys,
T total canonical type/key size, and S the sum of specialized body sizes. With
hashed structural lookup, expected planning time is O(T + S), plus existing type
checking and equality costs, plus O(U log U) structural-key comparisons for
deterministic ordering. The cost of each comparison depends on key size unless
canonical interned identities settle the comparison. Retained planning storage
is O(T + U), and retained
materialized code/IR is O(S). A naive linear cache would add O(U squared) key
search and is excluded. The worst U is not bounded by call count in source under
polymorphic recursion; the work-list and budget checks are mandatory.

Dictionary families retain one slot per demanded method specialization, summed
over methods rather than multiplied. Runtime allocation changes with slots per
dictionary times dictionary instances created, plus closure environments. Measure
that product on real consumers before landing. Do not append to shared inherited
lists in the planning cache. Allocate per-family accumulators and finalize once.
Use the owning phase's arena lifetime, release traversal temporaries at established
boundaries and retain only materializations/origin metadata needed downstream.
The compiler's 2 GB self-compile heap contract remains binding.

Production compiler heap/time behavior is unchanged by this document and the
scratch probes. No whole-compiler performance improvement is claimed.

## Scratch viability and acceptance

Artifacts: D:/Projects/Cobblestone-root/build-output/method-specialization/.
Inputs original-poly/original-mixed are unchanged copies of the two accepted
specimens. specialized-poly/specialized-mixed are MANUAL elaborations into
ordinary records, closures and definitions. The probe does not implement demand
discovery, automatic rewriting or template retention. Class/instance/method
declaration accounting must accompany the models; deleting the bad typedef from
the original IR is not a permitted control.

The stronger, bounded `materialize-ir.ps1` probe reads the original compiler's
typed IR. For Converter/convert and KeepValue/keep-value, it collects every
observed closed method-use type, verifies the exact original dictionary schema,
and replaces only that schema's method field with its concrete slot family.
It refuses any runtime construction/projection of the affected polymorphic
dictionary, any unrecognized call type, or any declaration shape outside the
two specimens. Existing method definitions and every other IR byte are preserved.
`materialization-manifest.json` retains both complete schema expressions, all
call-site types, and identical before/after lists of type and definition names.
This is a fixture-bounded viability instrument, not a production parser or pass.
Both affected classes have one instance. The prototype retains the dictionary's
existing class parameter a and materializes only b; multiple-instance family
selection and runtime polymorphic dictionary rewriting remain unexercised.

The manual mixed model's unused constructors are pruned by the existing compiler;
its output alone is not evidence that every constructor survives. Its record
schemas do survive. The IR materializer starts after that pre-existing pruning
and preserves every declaration present at its input; it does not claim to have
solved the zero-demand source-template problem.

viability.ps1 uses frozen compiler candidate-v2
C56FAC728A7F3BAD1777C41B9028DECDB6D695977E3C9EB73A47199881D882D3,
old Zig plug E71F15A2 and cumulative Zig plug065EE99F. It requires both original
programs to pass CDX output and both original typed emissions to refuse the free
b_ declaration. Each manual model must pass unchanged expected output through
both frozen plugs and CDX. The two rejected dictionary controls must remain
CDX2001 Integer/Text before IR. No seed gate is implied.

The first manual model used chained arrow syntax and was rejected with CDX1072;
initial-model-syntax.diag preserves the failure. Field signatures were corrected
to the existing comma-parameter grammar before model grading.

Measured 2026-09-10: both original programs pass their three/seven CDX rows and
both frozen Zig plugs refuse b_. Both manual models pass those same outputs on
CDX and both frozen plugs. Both local-dictionary boundary controls remain CDX2001
Integer/Text. Results are in results.json. The materialized IR passes the same
three/seven output rows through both frozen plugs, four exact passes recorded in
materialized-results.json. Type/definition inventories remain17 and16 respectively;
the inverse schema replacement reproduces every original IR byte. No declaration
was removed by that transformation. The original class/method schema is retained
verbatim in materialization-manifest.json, alongside the generated slot family.

Reader review found the zero-demand carrier gap, distinguished normal pruning in
the manual models from declaration preservation, and required ordering costs in
the complexity statement. Those limits are incorporated above. Reader review is
not RED's implementation ruling and does not lift the PR135 hold.

Before first-tranche landing: independent reader review of the implementation
contract; automated declaration accounting and structural scope
validation; discriminating same-key reuse/distinct-method/distinct-type controls;
runtime-receiver ineligibility and constrained/superclass controls; finite-input
bound and zero-cloned-body controls; exact CDX and frozen typed-backend output for
every supported case; relevant compiler proof under R-GATE. Scope growth or any
newly rejected accepted source is reported before landing, never hidden by defaults.

An output-equivalent hand elaboration establishes that the target representation
can carry these closed demands. It does not establish that the automatic pass is
implemented, that every accepted dictionary program is covered, or that PR135's
hold can be lifted.
