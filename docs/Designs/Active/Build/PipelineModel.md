# The build pipeline as a model: stages of input and transformation, emitted into any language

A build is a graph of stages. A stage declares its inputs, its transformation,
its outputs, its resources (guests, memory, ports, a kernel) and its verdict
(what each exit means), and the model lowers to the shell AST that the shipped
`build/*.ps1` scripts are generated from. The model's type is
`codex/foreword/shell/Pipeline.codex`; the record shape is approved (Damian,
2026-09-08).

**Every generator with a live target is on the model.** `check-generated-scripts`
bare reads 57 generators, 0 drifted, 0 broken, byte arm level with its record.
One generator is target-less by design, `testrunBashScript`, which emits
`build/test-run.sh`; it is KEPT rather than retired because it is the only bash
generator in the tree, so the unhandled-node scan reaches `BashEmit` through it
and through nothing else.

The first bash subject is still owed. The claim that `pipeline-to-shell` is
target-neutral rests on `emit-powershell` alone so far, and a bash generator
with a live target is what would test it.

## The shape

```
PipeRepeat =
  | PrWhile (ShellExpr)
  | PrWhileLabeled (Text) (ShellExpr)
  | PrForEach (Text) (ShellExpr)

PipeStep =
  | PsStage (PipeStage)
  | PsGroup (Maybe PipeRepeat)   -- repeat: while, labeled while, foreach
              (Maybe ShellExpr)    -- guard: if
              (Maybe PipeStage)    -- handler: try/finally
              (List PipeStep)
```

`pl-stages` is `List PipeStep`, `pipe-stage-cmds` walks the tree, and
`pipeline-doc` renders nesting. **The repeat is a vocabulary, not a condition:**
`foreach` binds a NAME and needs a collection, and a bare condition would leave
every `foreach` script unmigrable.

**THE THREE FIELDS NEST IN A FIXED ORDER: guard outermost, then repeat, then
handler around the body.** A guarded group that does not run therefore costs no
pass of its loop, and the handler runs once per pass rather than once per group.
The order is not derivable from the record, which is why `Pipeline.codex` states
it. The handler lowers as `ScTry body [] (handler.ps-body)`, an empty catch
beside a non-empty finally, which is exactly what the five `try` scripts are.

**Each stage lowers as its own `ScSequence` rather than being concatenated
flat**, because `emit-ps-cmds` gives a nested sequence a trailing newline that a
flat list does not. Blank-line placement therefore stays inside the stage bodies
where the generators already carry it. That is a migration constraint, not a
preference, and it is the first thing to revisit once a generator exists with no
shipped script to match.

**A STATEMENT ABUTTING A GROUP COSTS ONE BLANK LINE, and root ruled that the
residue absorbs it rather than a fourth field.** A stage lowers as its own
sequence and `emit-ps-cmds` terminates every command, so a sequence boundary
always emits one blank; where the shipped script has a statement touching its
loop with no blank between them, the tree cannot reproduce that. One blank line
is whitespace rather than structure, and a preamble field on the group would
bend the ruled shape to reproduce a cosmetic. The affected rows carry their
cause in `build/generated-scripts-bytes.txt`, and a row leaves the residue when
the shipped script's owner next touches it.

**The test that decides every subject: what the ASSEMBLY puts inside the
construct, not whether a construct exists.** A loop whose body is a helper list
rather than numbered sections of the script's own assembly does not need a
group. Nor does file SIZE predict anything: `cdx-to-pe` is 102 KB and four
stages, `build-magic-pages` is 4.6 KB and needed the tree.

**AN ASSEMBLY IS NOT UNIFORMLY BLANK-SEPARATED, and assuming so drifts the
output.** The rule is not "every body after the first takes a leading `ScBlank`"
but "each body takes exactly the separator the flat assembly held before it";
`plug-build-lib` has continuation stages that take none and `build-img` has no
separators at all.

## The vocabulary, and what each constructor exists for

| gap | constructor |
|---|---|
| a stage whose output is a FUNCTION a later stage calls | `PkFunction` |
| an exit code that is a COUNT | `PoComputed (Text)` |
| a refusal spelled `throw`, reaching no exit code | `PoThrow (Text)` |
| a resource driven by a PARAMETER | `PrDriven (PipeResourceKind) (ShellExpr)` |

**`PrDriven` carries a KIND rather than three new arms**, so the parameter case
is one constructor and stays typed: `PipeResourceKind` is `RkGuests`,
`RkMemoryMb`, `RkPort`, and a scheduler reads the same three quantities whether
they are constant or given. The Integer arms are untouched, because a constant
guest count is still the honest spelling for a stage that boots exactly one.
Writing `PrGuests 8` for a stage whose count comes from `-Jobs` would publish
the DEFAULT as the run, which is L-REQUEST's shape.

`PoPassthrough (Text)` spells an exit that is the code of the child stage that
failed, which no literal can describe.

**The test for whether a constructor earned its place is whether it DELETES
PROSE.** `check-cdx-registry` carried a column-2 block explaining that its
PascalCase converter could declare no artifact, and `run-plug`'s `ps-why`
carried a sentence explaining that its port could not be declared. Both are
deleted, because the code now says it.

**A VERDICT IS DECLARED ON THE STAGE THAT EXITS, NOT THE ONE THAT DEFINES THE
EXIT.** A verdict says what an exit MEANS for the stage a caller is standing in,
so a definition stage carries none and every stage that can reach the definition
carries its own.

**One verdict per stage rather than one per `exit`.** `build` spells 48 exits
and 47 are the same code; 47 identical `PoExit 1` entries would be a longer
declaration saying less, so each stage carries the one sentence separating its
refusal from its neighbours'.

**A verdict is WRITTEN by an author who has read the body, not derived from
it**, so an exit inside a raw payload can still be declared. What the raw
spelling costs is a CHECK: nothing walking the statement tree can find it.

## What is load-bearing, and what is documentation

**Artifact references are load-bearing.** A stage body does not spell
`SeVar "srcPath"`; it asks `pipe-ref` for the artifact named `srcPath`, and an
undeclared name resolves to `UNRESOLVED:<name>`, which changes the emitted
script. Provoked and observed: dropping one artifact from the table takes
`bundle-app` from `match / 0` to `DRIFTED / 4`.

**An artifact has two expressions and conflating them is wrong.** `pa-expr` is
the reference and `pa-from` is the production, and `pa-from` is a `Maybe`
because some artifacts come from a call statement rather than an expression
assignment.

**Resolution against the assembled pipeline is circular.** `pipe-ref` first took
the `Pipeline`, so a stage body asked the pipeline for a name and the pipeline
was built from the stages: the generator COMPILED CLEAN and faulted at run with
`!EXC=08`. Resolution is against a declared artifact table.

**An artifact DERIVED from another artifact still cannot say so**, and this is
open. A `pa-from` resolving through the table would be that same circular
definition, so each derived artifact spells its parent's `SeVar` directly and
the dependency between them is invisible: deleting the parent's declaration
leaves the productions emitting a correct script that references an artifact no
longer declared. The `pipe-ref` guard covers a reference from a stage BODY and
not one from a production. The repair is to resolve a production against the
artifacts declared BEFORE it, which is well founded because an artifact can only
derive from an earlier one, and which needs the table built in order rather than
as one literal list.

**A declared output with no consumer, and a declared input with no producer, are
both decidable by a future check where prose about them is not.** That check
does not exist.

## The gates

**`check-generated-scripts` decides STATEMENTS, not bytes.** It trims each line
and drops the empty ones on both sides before comparing, so every `match / 0
drift` proves identity modulo blank lines and indentation. Measured on the
gate's own logic: a shipped script against itself plus one extra blank line
gives `delta 0`; one changed statement gives `delta 2` (L-GAP).

**THE BYTE ARM is inside the same script**, because that script already holds
the emitted text and a separate one would boot every guest again. Its record is
`build/generated-scripts-bytes.txt`, one row per generator as
`<name> <shipped-lines> <emitted-lines> <indent-diff-lines>  # cause`, the cause
derived rather than typed so a row cannot arrive without a reason. It fails
three ways, each provoked and observed: a generator not in the record, a
recorded difference whose numbers move, and a recorded generator that has become
byte-identical while the record still lists it. That last one is the ratchet,
the same shape as `check-shell-raw`. **No row is a content difference**: every
one is blank lines, indentation, or both.

**`-UpdateBytes` writes only the residue**, because `-Update` also rewrites the
drift baseline and the hand-written inventory, and the inventory carries other
lanes' scripts. A lane repairing its own byte difference with `-Update` would
record those as decided.

**The drift gate REFUSES an undeclared missing target.** A generator with no
target grades nothing, and the gate used to report that and exit 0, which is
L-ACCEPTED in the gate's own lane. `$NoTargetByDesign` carries one entry with
its reason and the run FAILS on any other missing target, naming the three
repairs: the target moved, so map it in `$AltTarget`; it is gone, so delete the
generator; it is absent on purpose, so declare it.

**`build/check-pipe-verdicts.ps1` checks that a declared verdict is a code the
script can actually exit.** Two generators shipped a `PoExit 1` their scripts
never exit, written from the fail-helper prose instead of from the `ScExit`
values. **A verdict that states the wrong number is worse than no verdict,
because a reader trusts it and no run contradicts it.**

**What is decidable, and it took three wrong instruments to settle (L-CENSUS).**
A census of `exit N` over the emitted PowerShell is not decidable by text:
`exit` appears inside prose and strings, and a real code can reach `exit`
through a variable. A census over the generator with string literals stripped is
also wrong, because it hides every `exit` inside a `ScRaw` payload. What IS
exact is `ScExit (SeInt N)` in a code position, so the check reads that and
treats a raw-payload exit as **unreadable to the model: counted, reported, never
failed.** The arm therefore covers a subset of generators and that subset grows
as `check-shell-raw` shrinks the payloads: the two checks pull the same way, and
every raw payload removed makes a stage's verdict checkable.

Two arms, and only the first fails outright:

- **INVENTED**, a `PoExit N` no stage spells, in a generator with no unreadable
  exit. No baseline: there is nothing to record about a false claim.
- **UNDECLARED**, an `ScExit (SeInt N)` no stage explains. Filling one is
  per-stage judgement, so it is a record that shrinks, and the record is EMPTY.

**A migrated generator can SILENTLY LEAVE the drift gate.** Discovery matched
`sh-script "([^"]+)"`, and a generator that does not match is not reported as
broken, it is not reported at all. Discovery matches `pl-name` too now.
`check-generated-scripts.ps1` is fester's.

**The full pass is not a fan-out and costs about 60 seconds, peak ONE guest.**
`check-generated-scripts.ps1` declares no `-Jobs` at all, so the run is serial by
construction: one batch compile boot for the whole set, then one guest per
generator in sequence. It was once planned as a bounded `-Jobs 4` fan-out
needing a grant, which was L-AMORTISED: a per-item cost taken from a whole
invocation prices the setup N times. **Merge down to the seed at head before the
proof**, because a proof under a superseded seed is void.

## The runner

`apps/workflow/PipelineRun.codex` executes a pipeline as a `ProcessInstance`, so
`pi-history` is the build log with the MEANING of each exit beside it. Graded by
`codex/test/apps/pipeline-run` and `codex/test/apps/pipeline-group`.

**`apps/workflow/WorkflowTypes.codex` IS THE VOCABULARY.** A build is a process
and a stage is a step; inventing `PipeRun`, `PipeStageStatus` and `PipeEvent`
beside them would be a second copy of a concept this tree already keeps once.

| the pipeline model | the workflow vocabulary |
|---|---|
| a `Pipeline` | `ProcessDef` |
| one run of a pipeline | `ProcessInstance` |
| a `PipeStage` | `StepDef` |
| where a stage stands | `StepStatus` |
| what happened to a stage | `StepEvent` + `EventType` |
| an edge between stages | `TransitionDef` |

**`StepSkipped` is the state that pays.** `test-cross` reports six different
skips as exit `0`, indistinguishable by a caller from a pass; a runner carrying
`StepStatus` cannot make that mistake, so a tally reports what RAN as well as
what passed. That is L-DENOM answered by the type rather than by a convention.

**What does NOT carry over.** `StepType`'s arms are about people; a build stage
is an `AutomatedAction`, with `SubProcess` for a stage that shells out and
`ParallelSplit`/`ParallelJoin` for a fan-out. `sd-form`, `sd-assignment`,
`sd-required-docs` and `AssignmentRule` are left UNSET rather than given
invented values, because a field filled with a plausible default is L-BEDTRUE.
`AssignmentRule` has `Unassigned`, so that is satisfiable exactly.
`pd-sla-defaults` is the one field with no such value: it is zeroed and nothing
reads it, and a zero deadline would read as an immediate breach to `check-sla`,
so a pass that calls it must revisit rather than inherit.

**The one thing the workflow vocabulary lacks is `PipeOutcome`.** A `StepStatus`
says a step finished; it does not say what the exit code MEANT. The runner keeps
`ps-verdict` on the stage and records the matched outcome in `se-details`.

**A verdict is a transition.** A stage's `ps-verdict` list becomes its outgoing
edges, one `TransitionDef` per `PipeOutcome`, whose condition tests the exit code
and whose label is the outcome's own sentence. A stage with an empty
`ps-verdict` has one unconditional `Always` edge, which is the right reading of a
stage that cannot fail.

**A group's three optional fields are three edges.** The repeat is an edge from
the group's last interior step back to its first; the guard is the condition on
the edge INTO the group; the handler is an edge from EVERY interior step to the
handler stage.

**THE GROUP HEADER IS A ROUTING NODE, not a label, and it has to be.** The guard
is an edge out of the header, so pointing the previous stage straight at the
interior makes the guard unreachable and every guarded group runs
unconditionally. `pipe-entry-name` answers the header for a group.

**THE DRIVER NAMES WHAT IT OBSERVED.** A guard is a `ShellExpr` and a repeat is
a `PipeRepeat`; neither is a `ConditionOp`, and a runner that evaluated a shell
expression would be inventing an answer. Each group edge tests a key the driver
sets from the real run, and **the key names the group**, so two groups in one
pipeline cannot be confused.

**ONLY AN OUTCOME CAN REFUSE, AND CONTROL FLOW IS NOT AN OUTCOME.** The exit and
outcome keys are the ones that can refuse; guard, repeat and handler are the run
saying which way it went. A guard-skip is recorded as `EvStepSkipped` on the
group AND on every interior step, which is "StepSkipped rather than absent" in
full: the guard edge alone leaves them merely never entered, which tells a reader
nothing.

**Group membership comes from the CALLER.** A `ProcessDef` has no field meaning
"these steps belong to that group": `pd-data-schema` would take the list and
`SubProcess` would take it as delimited text, and both are a field filled with a
plausible value. The caller holds the `Pipeline`, so it can answer exactly, and
`pipe-group-members` is how it does.

**Two placements are FORCED, not chosen.** `PipelineRun.codex` is in the APP
quire because library rule 2 forbids a foreword chapter citing `WorkflowTypes`;
and each subject pipeline is built inside its test, because a generator chapter
declares `opening` and a chapter declaring an entry point can be cited by
nothing (CDX3001, L-UNCITABLE).

**It does NOT cite `WorkflowEngine`**, whose `evaluate-transitions` and
`eval-condition` do what is wanted: that chapter cites the whole data-server
stack, and citing it would put a database in the unit of every build tool that
wants to read a pipeline. Edge selection here matches the two condition shapes
this translation emits and REFUSES anything else.

**RUNNING IT FOUND FIVE DEFECTS READING IT HAD NOT**, which is why the runner's
first subject must be a pipeline whose stages are known to fail in known ways: a
runner that has only ever seen a green build is an instrument that cannot fail
(L-FALSIF).

## The next unit: fan-out

**The model already says which stages fan out, and no fifth constructor is
needed.** A stage that fans out is a stage that needs more than one guest, and
`ps-needs` says so: `bvt` s08 and s10 declare `PrDriven RkGuests (SeVar "Jobs")`,
and `bvt` and `check-errors` declare `PrGuests 8`. So the runner reads
`PrDriven RkGuests <expr>`, or `PrGuests n` with n above one, as a
`ParallelSplit`, the branch count being what the driver resolves for the
expression, and a `ParallelJoin` after it. This only became visible BECAUSE
`PrDriven` was added: before it, `bvt`'s two parallel phases declared `PrKernel`
alone and the model could not have been asked the question.

**A branch carries its own `StepStatus`, and the join counts `StepSkipped` and
`StepCompleted` apart** instead of summing one exit code. The arm that proves it
is `bvt` s08, whose guest count comes from a parameter.

After fan-out: driving a run from a REAL script's observed exits rather than a
scripted list.

## Findings this campaign surfaced that belong to other owners

Each was found by having to write down what a stage consumes, produces and
means. None is repaired here, because this migration's gate is byte-identity and
each repair changes behaviour.

- **`test-cross` returns 0 for eight states and six of them are SKIPS** (a
  `.skip` sidecar, a `.no-cross` sidecar, multi-core, slow, fatal, and an error
  test the frontend alone can judge). A caller aggregating exit codes counts all
  six as passes. Belongs to whoever owns the cross battery's aggregation.
- **`sweep-apps` accepts `-Jobs` and reads it nowhere**; the sweep waits on each
  compile before starting the next.
- **`build/plug-build.ps1` declares `-Force` and no line reads it**, and the
  builder it calls has no such parameter to forward it to.
- **`check-plug-ports` checks its host half only where a `run.ps1` exists, and
  the absence is SILENT**, so a plug carrying a guest source and no runner passes
  a check whose name promises both halves.
- **`concat-codex-self` assigns `$ForewordDir` and nothing reads it.** Foreword
  directories resolve through `$QuireDirs` now, so the hardcoded path is a stale
  second answer a reader takes as authoritative.
- **`build-explorer-pages` cannot fail**: it spells no `ScExit` anywhere, so no
  caller can tell a run that built five pages from one that built none
  (L-BAILVALUE). `lint-unused-cites` and `clean-zombies` are the same.
- **`test-self-verify` ends at zero whatever the verification said**, so a caller
  reading only the exit code cannot tell "THE SEED VERIFIES ITSELF" from
  "SIGNATURE INVALID". The answer is the TEXT, which is why the seed path
  requires the output to be read rather than the run gated on.
- **`check-vm-differential` has two zeros that mean opposite things**, one VM
  host skipping and two hosts agreeing byte for byte.
- **`check-sidecars`' orphan refusal WINS**: a run carrying both defects reports
  the orphans and exits before the newline list prints, so the second finding
  stays invisible until the first is fixed.
- **`test-renode` refuses only on the BED being absent, never on the run**, and
  ends at zero whether the UART said anything or not.

## Traps this campaign paid for

- **A `.codex` file is UTF-8 WITHOUT BOM in the workspace, whatever Perforce
  types it.** `p4 diff2` calls these files `unicode`, and rewriting one as UTF-16
  produces a WHOLE-FILE diff with every line changed while the content is
  correct. Prefer the editing path; when a whole-file rewrite is warranted, count
  `^[+-][^+-]` lines from `p4 diff -du3` before believing it landed clean (P-EOL,
  a second face of it).
- **A quotation mark inside `ps-why` ends the string**, and the result is several
  `CDX3002`s naming ordinary English words, which reads as a lexer defect. A
  cheap guard before any run: count `"` per stage line and refuse an odd number.
- **Renaming half a set is a compile error that reads like a design problem.**
  `check-generated-scripts` reports it as `COMPILE FAILED` with no diagnostic;
  `compile.ps1` with an explicit `-Log` names it in one line. Ask the compiler,
  not the harness.
- **`-Only test-run` selects TWO generators**, because `testrunScript.codex` and
  `testrunBashScript.codex` both emit the name `test-run` and `-Only` filters on
  the emitted name.
- **A sabotage must be checked for having HAPPENED before its colour is read.**
  Two ablations on this campaign silently failed to ablate (a write to a
  read-only file, and a bad regex that left the entry in place) and both runs
  read green (L-VACUOUS).

## Owner

reek. `check-generated-scripts` and the shipped-script drift gate are fester's.
`ShellDslReadability.md` is the same piece of work: a name inside a raw payload
cannot be reached by `pipe-ref`, so every raw node converted is a name the
pipeline model can resolve and a verdict the check can decide.
