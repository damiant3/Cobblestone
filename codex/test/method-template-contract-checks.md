# Method template contract controls

These controls target the first tranche in
`docs/Designs/Done/Compiler/MethodSpecialization.md`.
The checker consumes version 1 of the compiler's `method-templates` section.
The result separates structural checks, body reuse, inspection, input-derived
bounds, source offsets and recorded phase-reservation inequalities. Execute the
producer guards separately to establish refusal at the reservation boundary.
No seed acceptance follows from a structural checker pass.

## Fixtures

| Suffix | Required discriminator |
|---|---|
| zero | An unused polymorphic class/instance/method survives in checked metadata, without invented runtime layout. Printing `zero` alone cannot establish retention. |
| demands | Six direct uses require five unique method/type keys: two left slots plus three right slots. The repeated Boolean left use shares a key. Separate methods spelling `b` retain separate binder scopes. Runtime output also distinguishes left from right implementations. |
| lookalike | A user-defined record has dictionary-like type, field and constructor spellings, with two explicitly declared type parameters. The compiler must not invent trusted class/instance origins. |
| runtime | A generated polymorphic dictionary is bound by a `let` and projected. The desugarer rewrites the projection into a direct method use, so the class materializes with one slot and native behavior is unchanged. |
| escape | A helper receives the generated dictionary and returns a closure capturing the dictionary. The first tranche must retain the existing ineligible path and native behavior. |
| mono | Direct method dispatch and dictionary projection preserve monomorphic behavior. |
| bounds-small / bounds-large | Each input has one use and one unique key. Four nested Lists enlarge the latter key without increasing the use count. A count-only byte budget cannot distinguish the pair. |
| unicode | An unused method retains accented Latin and Greek literal text in its constructor recipe. Native output and metadata inspection must preserve the characters. |

## Runners

Run `method-template-contract-run.ps1` with explicit `-Kernel` and a new
`-OutputDirectory`. `-Only` selects suffixes from the table; the default selects
all nine. `-CaptureIr` also captures IR-CCE with the `text-plug` pass set.
The runner copies source/expected files into the output directory, records
input and kernel hashes, compiles/runs serially, and compares complete native
output after stripping CR only from the expected file. Read `exit.txt`,
`runs.json`, `provenance.json` and the subject diagnostics. A changed input or
kernel fails the run. This runner does not perform a compiler fixed point or BVT.

Run `method-template-contract-readers.ps1` with `-InputsDirectory` pointing to
those `.ir` and `.expected` files, a new `-OutputDirectory`, explicit `-OldPlug`,
`-NewPlug`, `-Zig` and `-Subjects`. Both frozen reader hashes must be distinct.
The runner emits Zig, compiles the emitted source and compares complete hosted
output, normalizing CRLF line endings but retaining bare CR. The frozen Zig
runtime prints normal output on stderr. The runner records the nonempty output
channel and refuses simultaneous stdout/stderr because cross-channel ordering
is ungraded. A 60-second hosted timeout fails.
`-Unsupported` names selected subjects whose known boundary is the
undeclared `b_` diagnostic, with no other Zig error. Those rows are `UNSUPPORTED_FREE_BINDER`, never
`EXACT_OUTPUT_PASS`. A successful process exit can therefore include unsupported
subjects; inspect `results.json` for the per-subject acceptance result.

Launch guest runs detached and record ownership, wrapper PID, one serial guest
and log in the lane status. Both runners remeasure RAM before each guest and
refuse at or below 1.5 GiB free. Frozen plugs use port 9145; the shared plug
runner refuses an occupied listener. Keep every red log and use a fresh output
directory after a correction.

`method-template-contract-source.ps1` is dot-sourced and exposes
`Get-MethodContractSource -Source <fixture>`. The bounded reader derives chapter,
class/instance/method declarations, source lines, original method schemas and
separate class/method binder names directly from the fixture. The reader accepts
only the fixture grammar: simple classes, Integer/Boolean instance heads,
single-letter type variables, atomic method types and one-line implementations.
Other class/instance syntax fails explicitly. This reader is not a Codex parser,
type checker, trusted-origin validator or metadata acceptance check.

## Metadata checker

Run `method-template-contract-check.ps1` with `-Source`, decoded UTF8 `-Ir`,
and `-ExpectedState` (`none`, `zero-demand`, `materialized`, `ineligible`).
For eligible fixtures supply the independent `-ExpectedUses` and
`-ExpectedSlots` census. `-Result` writes the structured grades.

The checker compares the complete source-derived declaration inventory,
original schemas, binder scopes, constructor recipes and declaration links.
Closed slot substitutions and structural keys must match the unique method
uses found independently in runtime IR. The complete runtime schema must agree
with the slots. Numeric source overflow tag 0 and runtime `ov-error` normalize
only for the full default Integer range; other bounded forms are ungraded.

Supply same-source, same-pass-set `-BaselineIr` to detect added, removed or
changed definitions independently of the producer's cloned-body flag. Build
the control with `method-template-contract-build-control.ps1 -Kernel <depot-seed>
-OutputDirectory <new-directory>`. The control bypasses exactly the text-IR
materialization call in a snapshot of current compiler source. Compare the
saved `producer.codex` hash with the actual candidate source before using the
control. An older compiler can change bodies through unrelated lowering rules
and is not a substitute for this control.
Definition comparison renames numeric type-variable/effect-row IDs consistently
within each definition. Without baseline IR, body reuse is explicitly UNRUN.
CHECK target variables are aligned with the declared source method binders;
optional quantifiers are allowed only around the complete declaration scheme.

`-RoundTrip` requires an IR chapter from the actual inspection executable and
compares the complete metadata tree. `method-template-contract-inspect.ps1`
produces those files: supply explicit `-Kernel`, `-Inspector`,
`-InputsDirectory`, new `-OutputDirectory` and `-Subjects`. The selected kernel
must match the input directory's producer provenance. The runner captures
IR-UNI and compares metadata with decoded IR-CCE, then sends CCE input through
the actual parser/emitter helper on port 9196 and decodes CCE output. Complete
IR equality is recorded separately because runtime parsing can normalize types.
Build the inspector from the current parser and emitter with
`method-template-contract-build-inspector.ps1 -Kernel <candidate>
-OutputDirectory <new-directory>`. The helper sends raw CCE, including retained
non-ASCII recipes. Both builders retain source and executable provenance and
require the same detached launch and RAM admission as the other guest runners.

`method-template-contract-selftest.ps1` uses an actual zero-demand artifact
and baseline to calibrate omitted/duplicated sections and declarations, forged
scopes/recipes/links, invalid target quantification, hidden clones, changed
bodies, bounds and malformed roundtrip output. Its same-file roundtrip positive
is a harness control, not an execution of the inspection parser.
`method-template-contract-slot-selftest.ps1` constructs an explicitly synthetic
materialized model over the demands fixture's baseline IR and tests slot
cross-products, duplicates, keys, arguments, scopes and runtime-field drift.
Synthetic model acceptance is not production materializer acceptance.

## Bound grades and producer witnesses

`-BoundProfile 8C145D24` recomputes the frozen v1 formula for the bounded ASCII
fixture grammar. `23DB8046-minimum` includes visible row-node charges but grades
only a lower bound: the producer follows row-tail chains absent from the wire.
Both profiles count every observed use, rather than multiplying by unique slots.
Negative/overflowed bounds and bounds smaller than the visible input minimum
fail. The old profile requires exact agreement; its use against a newer kernel
is not evidence of a production defect. Ineligible partial scans are ungraded.

Pass `-RequireWitness` to require the resources section and reconstruct exact
CCE unit offsets from the fixture and its resolved prelude. Every declaration's
span and identity must match that source inventory. The recorded source byte
count must match the assembled unit; planning bytes plus the total input bound
must fit strictly below phase capacity; content allocation must not exceed the
bound. The checker reports these as recorded inequalities, not an independent
measurement of allocator state. Non-ASCII costs use the shared CCE encoder.

Compile and run `method-template-producer-guards.codex` against the candidate.
The probe calls the real producer with poisoned phase compaction and separately
corrupts accounting, binder scope, slot count, arithmetic and phase capacity.
Each refusal must produce exactly one named diagnostic and no metadata wire.
The expected output pins all eleven controls. Run the metadata selftest with
`-RequireWitness` to calibrate source-offset and reservation corruptions too.
The same-source control grades body reuse; neither the producer's zero-clone
flag nor a synthetic slot model establishes that property alone.

Candidate runtime/escape typed failures remain unsupported, never accepted
typed programs. Required existing typeclass/superclass regressions belong to
the producer owner's combined proof and are not replaced by this fixture set.

Compiler heap/time behavior is unchanged by these test files. The source reader
uses O(S) expected host time and retained storage for source/inventory, with
hash-based class/method membership and linear signature construction. The metadata
checker repeats method searches, walks runtime IR per class and recursively
formats nested types; its host work can grow as O(C*R + M squared + R*D), where
C is class count, R is IR size, M is method count and D is nesting depth.
The checker has no production compiler performance claim. Native and
reader runners execute subjects serially. These fixtures do not establish the
production compiler's heap high-water mark or asymptotic materialization cost.
