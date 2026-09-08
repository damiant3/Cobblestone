# The build pipeline as a model: stages of input and transformation, emitted into any language

**Status: THE RECORD SHAPE IS APPROVED (Damian, 2026-09-08, having read it:
"looks decent to me. any burrs will irritate, and we can address them"), and
the migration runs through the remaining generators, one per CL, surfacing
burrs as each is found. The model's type is
`codex/foreword/shell/Pipeline.codex`. Migrated, each byte-identical to the
script that shipped before it: `bundle-app 44`, `run-plug-chain 65`,
`compliance-report 51`, `check-apps 65`, `build-explorer-pages 54`,
`clean-zombies 74`, `check-constants 76`, `lint-unused-cites 121`,
`sweep-apps 86`, `resolve-trace 121`, `build-apps 111`,
`profile-histogram 104`, `check-facts-guid 105`, `test-self-verify 73`,
`check-plug-ports 105`, `test-renode 72`, `test-boards 132`,
`check-sidecars 111`, `check-errors 139`, all `match / 0 drift`. Nineteen of
the 58 generators (2026-09-08). Opened 2026-09-08 by root on Damian's
direction.**

## The census: what is left, counted rather than sampled (reek, 2026-09-08)

Measured over all 58 generators without a guest, resolving each assembly
through the body identifier `sh-script` actually names and following a loop or
`try` body one level into its helper definition.

| | count |
|---|---|
| migrated | 19 |
| flat, migrable today | 30 |
| BLOCKED by burr 4 | 9 |

The nine, by the construct wrapping their numbered sections: `stress-sweep` and
`CompileScript` (`while`); `build-magic-pages` and `test-exception-handler`
(`foreach`); `run-plug`, `plug-run`, `test-run`, `test-disk-compile` and
`gdb-watchpoint` (`try`/`finally`).

**`try`/`finally` is the MAJORITY construct among the blocked, five of nine.**
That settles the shape question this page priced earlier: a group node carrying
only a repeat condition leaves five of the nine still unmigrable, so the group
needs a handler field as well. `build.ps1`, `test.ps1`, `bvt` and
`CompileScript` are all inside the blocked nine, and those four are the scripts
whose stage structure is worth the most.

**The first two passes of this census were WRONG and agreed with themselves.**
A regex for `*-body` matched an earlier helper rather than the assembly, and a
second pass missed every generator whose loop body is a named helper rather
than an inline list. Both produced a clean-looking table. The third pass was
trusted only because it reproduced all four generators already blocked by hand
(L-CENSUS: a clean grep is a statement about the grep).

## The second generator, and the two burrs it found (reek, 2026-09-08)

`run-plug-chain` was chosen for carrying a real RESOURCE and a real VERDICT
rather than for size, which is what this page asked of the second subject.
Both declared-only fields now have their first reader, and the choosing found
two places where the vocabulary could not state the truth.

**Burr 1, repaired here: a literal exit code cannot describe a passthrough.**
`run-plug-chain` exits with the code of the child stage that failed, and
`PoExit (Integer) (Text)` can spell only a constant. Declaring a number would
have been false and declaring nothing would have left the one exit a reader
most wants explained undocumented, so `PipeOutcome` gains `PoPassthrough
(Text)`. One constructor on a chapter with two citers; measured after,
`deck-headroom.ps1 -Quire codex\build -WithSelf -MinMargin 1.25` answers OK at
tightest margin 1.30 over 59 units, unchanged, and the generator is not among
the 25 tightest.

**Burr 2, NOT repaired, and the decision is open.** A resource driven by a
PARAMETER cannot say so. `PrMemoryMb (Integer)` takes a literal, and this
script's memory comes from `-MemMB`, default 4096. The declaration therefore
states the default and a caller's override is invisible to it. `PrKernel`
already takes a `ShellExpr`, so the type is inconsistent as well as
imprecise. Three options, none taken: widen the three Integer constructors to
`ShellExpr` (uniform, and a scheduler then reads an expression rather than a
number); carry both a default and an optional expression; or leave the
literal and accept that `ps-needs` describes the default invocation only.
Nothing reads `ps-needs` yet, therefore the cost of deciding later is one
edit to two generators.

**The declarations are load-bearing, provoked and observed.** Dropping
`dest-art` from the artifact table takes `run-plug-chain` from `match / 0` to
`DRIFTED / 2`, and restoring the declaration returns it to `match / 0`.

## The third burr, found by the fourth generator (reek, 2026-09-08)

`compliance-report` is migrated: `compliance-report match 51 / 0 drift`.

**An artifact DERIVED from another artifact cannot say so.** `cdx`, `log` and
`report` are each `SePathJoin (SeVar "outDir") ...`, and a `pa-from` that
resolved `outDir` through the table would be the circular definition that
faulted the model's first shape. Each therefore spells `SeVar "outDir"`
directly, and the dependency between the four is invisible: deleting
`out-dir-art` leaves the three productions emitting a correct script that
references an artifact no longer declared. The `pipe-ref` guard covers a
reference from a stage BODY and not a reference from a production.

The repair has the same shape the first migration used for stage bodies:
resolve a production against the artifacts declared BEFORE it rather than
against the whole table, which is well founded because an artifact can only
derive from an earlier one. That needs the table built in order rather than as
one literal list, and it is the second pass's work.

## `test-run` is migrated nowhere, and the reason is a gap in the gate

`testrunBashScript.codex` emits `build/test-run.sh`, WHICH DOES NOT EXIST, so
`check-generated-scripts.ps1` reports it under "no target" and grades nothing
it emits. A migration of that generator could not be proven byte-identical,
therefore no migration was made (L-NOGATE). Two facts about it are worth
having anyway: `-Only test-run` selects TWO generators, because
`testrunScript.codex` and `testrunBashScript.codex` both emit the name
`test-run` and `-Only` filters on the emitted name; and the bash generator
compiles clean standalone.

The first BASH subject is therefore still owed. The design's claim that
`pipeline-to-shell` is target-neutral rests on `emit-powershell` alone so far,
and a bash generator with a live target is what would test it.

## The fourth burr, and it is the shape of `Pipeline` itself (reek, 2026-09-08)

`check-apps` is migrated: `check-apps match 65 / 0 drift`. Five generators are
now on the model.

**A SCRIPT WHOSE STAGES RUN INSIDE CONTROL FLOW CANNOT KEEP ITS STAGES.**
`pl-stages` is a flat `List PipeStage` and `pipeline-to-shell` lowers it as a
flat sequence, one `ScSequence` per stage. `stress-sweep` puts four of its six
sections inside a `ScLabeledWhile`, so the model can hold two stages and one
opaque body, or six stages in an order the script does not run. Neither is
true, therefore `stress-sweep` is NOT migrated.

This is not one generator's problem. Every script with a retry loop, a per-job
fan-out or a per-target sweep has the same shape, and those are the scripts
whose stage structure is worth the most: `bvt`, `test` and `build.ps1` are all
in that class, and this page names them as the last to migrate.

**The decision, and it is Damian's because it changes the record he approved.**
Three shapes, in ascending cost:

1. **A stage carries the loop in its body** and the inner sections stop being
   stages. Cheapest, and it gives up exactly what the model exists to provide.
2. **`ps-repeat : Maybe ShellExpr` on `PipeStage`**, so a stage can say it runs
   while a condition holds. Handles a loop over ONE stage; `stress-sweep` needs
   a loop over FOUR consecutive stages, so it does not fit without grouping.
3. **`pl-stages` becomes a tree**, a `PipeStep` that is either a stage or a
   group carrying a repeat condition and its own stage list. Correct for every
   case named above, and it changes the lowering, the doc renderer and both
   fields' readers.

Shape 3 is what the scripts actually are. Nothing is built until Damian rules,
and flat generators keep migrating in the meantime.

### Burr 4 is WIDER than a loop, measured over sixteen attempts (reek, 2026-09-08)

Three generators are blocked, and each is blocked by a DIFFERENT construct
wrapping its numbered sections:

| generator | what wraps the stages |
|---|---|
| `stress-sweep` | `ScLabeledWhile` around sections 3 to 6 |
| `build-magic-pages` | `ScForEach` over the page list around sections 3 to 5 |
| `run-plug` | `ScTry` around sections 3 to 7, with section 8 as its FINALLY |
| `test-exception-handler` | `ScForEach` over the sample table around sections 3 to 5 |

`run-plug` is the one that changes the design question. A repeat condition on a
group, which is all shapes 2 and 3 were priced to carry, does not express a
try/finally: section 8 stops the VM and deletes the stderr file however
sections 3 to 7 end, and that is a CLEANUP relationship between a group and one
stage, not a repetition. So the group node in shape 3 needs two optional
fields, a repeat condition and a handler stage, or the third of these stays
unmigrable after the tree lands.

**The test for whether a loop blocks a generator is what the ASSEMBLY puts
inside it, not whether a loop exists.** `check-plug-ports` and `sweep-apps`
both loop and both migrated flat, because their loop bodies are helper lists
rather than numbered sections of the script's own assembly. **Four blocked of
twenty attempted (2026-09-08)** is the rate to weigh the tree against, and the
four split three ways by construct: two `foreach`, one `while`, one
`try`/`finally`.

`check-plug-ports match 105 / 0 drift` is that case proven rather than
asserted: its three per-plug checks are sections S03, S04 and S05 by name in
the chapter, and none of them is a top-level `ScSequence` in the assembly, so
four stages is the script's own structure rather than a demotion of it.
`test-boards match 132 / 0 drift` is the same shape and migrated the same way.

**Two more subjects, and what each cost to declare.** `test-renode match 72 / 0
drift` refuses only on the BED being absent, never on the run, and ends at zero
whether the UART said anything or not, so a caller gating on its exit code
cannot tell a board that ran from one that never started. `check-sidecars match
111 / 0 drift` checks two unrelated things, orphaned sidecars and unterminated
`.expected` files, and its orphan refusal WINS: a run carrying both defects
reports the orphans and exits before the newline list is printed, so the second
finding stays invisible until the first is fixed. Neither fact was reachable
without following the control flow to the end; both are now a line in a
`ps-verdict`.

**One thing declaring the stages exposed in that script.** Its host half is
checked only where a `run.ps1` exists, and the absence is SILENT, so a plug
carrying a guest source and no runner passes a check whose name promises both
halves. That is L-ACCEPTED's shape again, it belongs to whoever owns
`check-plug-ports`, and the migration only made it visible by having to write
down what the stage does.

## Two things the sixth and seventh subjects measured (reek, 2026-09-08)

`build-explorer-pages match 54 / 0 drift` and `clean-zombies match 74 / 0
drift`.

**`build-explorer-pages` CANNOT FAIL, and declaring the verdicts is what showed
it.** The script spells no `ScExit` anywhere: a missing CDX prints SKIP, a short
capture prints EMPTY, an absent capture prints NO output, and every path leaves
the exit code at zero, so no caller can tell a run that built five pages from
one that built none (L-BAILVALUE). Every `ps-verdict` in that generator is
therefore empty as a measurement rather than an omission. Repairing it changes
behaviour and belongs to whoever owns that script, not to a migration whose
gate is byte-identity.

**A generator's RAW COUNT bounds how much of it the model can ever describe.**
`clean-zombies` is almost entirely `ScRaw`, and a name inside a raw payload
cannot be reached by `pipe-ref`, so its declarations bind three assignments and
nothing else. That makes `ShellDslReadability.md` the same campaign as this one
rather than a neighbouring chore: every raw node converted is a name the
pipeline model can then resolve.

## The fifth burr, and what the eighth subject proved worth having (reek, 2026-09-08)

`check-constants match 76 / 0 drift` and `lint-unused-cites match 121 / 0
drift`.

**A STAGE WHOSE OUTPUT IS A FUNCTION CANNOT DECLARE IT.** `PipeKind` offers
`PkFile`, `PkDir`, `PkValue` and `PkText`, so `lint-unused-cites`, whose second
and third stages exist ONLY to define `Get-ChapterDefs` and `Lint-File` for the
fourth to call, declares `ps-out = []` on both. The dependency from the main
stage back to the two definitions is invisible, the same shape as a derived
artifact's. A `PkFunction` kind costs one constructor and is the cheapest of
the five burrs to close; it is left with the others so the second pass takes
the vocabulary questions together.

**`check-constants` is the case that pays for the verdict field.** It carries
five exits across two modes, and the same code `0` means "the recorded hash
already matched" in one branch and "the hash was rewritten and must be
submitted with the seed" in another. A reader of the shipped script recovers
that only by working out which branch an `exit 0` sits in. Two `PoExit 0`
entries with different text say it in the declaration, which is the first time
on this campaign that the verdict list carries something no reader could get
faster from the code.

**Three of the eight subjects so far cannot fail at all.**
`build-explorer-pages` and `lint-unused-cites` spell no `ScExit` anywhere, and
`clean-zombies` spells none either. That is not a finding about the model; it
is what declaring verdicts surfaces, and it belongs to whoever owns those
scripts.

## The sixth burr, and a defect the stage declarations found (reek, 2026-09-08)

`sweep-apps match 86 / 0 drift`, the ninth generator on the model.

**A COMPUTED EXIT CODE CANNOT BE DECLARED.** `sweep-apps` ends with
`ScExit (SeAdd crash timeout)`, and `PipeOutcome` spells a literal (`PoExit`)
or a child's own code (`PoPassthrough`) and nothing else. Declaring a number
would be false, so that stage's verdict is empty. **This is the second time the
verdict vocabulary has come up short**, the first being the passthrough
`PoPassthrough` was added for, and the pattern here, exit with a count of
failures, is the one `bvt` and `test` use as well. The vocabulary therefore
wants ONE decision covering literal, passthrough and computed rather than a
constructor added per generator, and that goes to the second pass with the
`PkFunction` question and the two resource questions.

**`sweep-apps` accepts `-Jobs` and uses it nowhere.** Measured at head: `Jobs`
appears exactly once in `build/sweep-apps.ps1`, on line 10, as the parameter
declaration. The sweep loop waits on each compile before starting the next,
therefore a caller passing `-Jobs 8` is promised a parallelism the script never
had, and a caller told to run at a lower job count cannot comply. That is the
same family as `plugs-backlog.md` 2.41, the 51 plug runners that hardcode
`-MemMB 3072`, and it is L-ACCEPTED: a setting that does nothing looks exactly
like one that works. Found because `ps-in` asks what a stage actually consumes
and `Jobs` answered nothing. Not repaired here, because removing a parameter
and adding parallelism both change behaviour, and this migration's gate is
byte-identity.

**A RAW NODE HIDES A REFUSAL FROM EVERY READER EXCEPT THE ONE HOLDING THE
TEXT** (`resolve-trace 121`, `build-apps 111`, both `match / 0 drift`).
`resolve-trace`'s binary branch carries `ScRaw "Write-Error \"Trace file too
small\"; exit 1"`, and two of `build-apps`' three exits are the same shape
(`exit 2` and `exit 3` inside raw payloads, `exit 1` an ordinary `ScExit`).

The first statement of this on 2026-09-08 said no declaration could name such
an exit, and that was wrong: a verdict is WRITTEN by an author who has read the
body, not derived from it, so all three of `build-apps`' exits are declared and
`resolve-trace`'s raw exit is declared too. What the raw spelling actually
costs is a CHECK. Nothing walking the statement tree can find an exit inside a
payload, therefore the runner this campaign might otherwise gain, one that
lists a stage's refusals from its body and fails when a declaration and the
code disagree, can never be complete while raw nodes carry exits. That is the
sharper form of what `clean-zombies` showed, and it is another reason
`ShellDslReadability.md` and this campaign are one piece of work.

## A rule the twelfth and thirteenth subjects settled (reek, 2026-09-08)

`profile-histogram match 104 / 0 drift` and `check-facts-guid match 105 / 0
drift`.

**A VERDICT IS DECLARED ON THE STAGE THAT EXITS, NOT THE ONE THAT DEFINES THE
EXIT.** `check-facts-guid` spells a `Fail` function in its reader stage, and
`Fail` exits nowhere until `Get-CodexGuid` or `Get-Ps1Guid` runs, which happens
in the comparison stage. The refusal is therefore declared on the comparison
stage. The rule generalises: a verdict says what an exit MEANS for the stage a
caller is standing in, so a definition stage carries none and every stage that
can reach the definition carries its own. Written down because the first
instinct on reading that script is to put the verdict beside the `ScExit`.

**A zero is not always success, and only a verdict distinguishes them.**
`profile-histogram` exits 0 on an empty profile after printing one line, which
means "nothing to do" rather than "the histogram was produced". That is the
third such case after `check-constants`' two `PoExit 0` branches, and together
they are the strongest argument the campaign has produced for the verdict field
existing at all.

## The fourteenth subject, and a second generator blocked by burr 4 (reek, 2026-09-08)

`test-self-verify match 73 / 0 drift`, and the FIRST stage anywhere to declare
`PrKernel`, carrying `SeVar "Kernel"`. That is the one resource constructor
already taking a `ShellExpr`, so it states the truth where `PrMemoryMb` cannot,
which is the argument for widening the other three (burr 2).

**`build-magic-pages` IS BLOCKED BY BURR 4, the second generator to be.** Three
of its five sections sit inside a `ScForEach` over the page list, exactly
`stress-sweep`'s shape with a foreach rather than a while. Two blocked
generators out of fourteen attempted is the rate to weigh the tree option
against, and both would be migrable under shape 3.

**A verdict this script does NOT have, and the seed path depends on the
absence.** `test-self-verify` prints what the verification program said and
ends at zero whatever that was, therefore a caller reading only the exit code
cannot tell "THE SEED VERIFIES ITSELF" from "SIGNATURE INVALID". Declaring the
stages made the gap explicit: the answer is the TEXT. That is why `CLAUDE.md`'s
seed path requires the output to be read rather than the run to be gated on,
and it is worth knowing before anyone wires this script into a check.

## The design pass, decided and measured (reek, 2026-09-08)

**The model is a record type in a chapter of its own, not new constructors in
`ShellTypes`.** Section 61 left three options open; the deck settles it. A
single new `ShellExpr` constructor cost `cdxtopeScript` 4.75 MB of check deck
and put three units on the 1.25 floor, because every generator bundles the type
(ShellDslReadability.md section 9). A separate chapter is paid for only by its
citers, which is Library Rule 7. Measured after the migration,
`deck-headroom.ps1 -Quire codex\build -WithSelf -MinMargin 1.25`: **OK,
tightest margin 1.30 over 59 units**, and the migrated generator is not among
the 25 tightest. The decision cost the corpus nothing.

**A declaration nothing reads is a comment, which is this page's own complaint
about the current model, so the first pass makes the declarations load-bearing
in the direction that can be tested.** A stage body does not spell `SeVar
"srcPath"`; it asks `pipe-ref` for the artifact named `srcPath`, and an
undeclared name resolves to `UNRESOLVED:<name>`, which changes the emitted
script. Provoked and observed: dropping one artifact from the table takes
`bundle-app` from `match / 0` to **`DRIFTED / 4`**, and restoring it returns to
`match / 0`.

**What is declared and NOT yet load-bearing, said plainly rather than implied.**
Resources and verdicts are documentation with a schema: a verdict reading "exit
3 means a cited chapter could not be resolved" sits beside an `ScExit (SeInt 3)`
inside a `try` arm that a generic lowering cannot place, and placing it needs
the named-operation vocabulary. A declared artifact that no body references is
likewise not caught. Both are the second pass.

**Three things the first migration found that no amount of reading would
have.**

1. **Resolution against the assembled pipeline is circular.** `pipe-ref` first
   took the `Pipeline`, so a stage body asked the pipeline for a name and the
   pipeline was built from the stages. The generator COMPILED CLEAN and faulted
   at run with `!EXC=08`, a stack overflow from a definition containing itself.
   Resolution is against a declared artifact table now.
2. **An artifact has two expressions and conflating them is wrong.** `Repo` is
   produced by `(Resolve-Path (Join-Path $PSScriptRoot '..')).Path` and referred
   to as `$Repo`. One field inlined the whole path computation into a call
   argument and came out one line from byte-identical. `pa-expr` is the
   reference, `pa-from` is the production, and `pa-from` is a `Maybe` because
   `rootLines` comes from a call statement rather than an expression assignment.
   Five of the six artifacts now have their producing line generated from the
   declaration.
3. **A migrated generator SILENTLY LEFT THE DRIFT GATE.**
   `check-generated-scripts.ps1` discovered generators by matching `sh-script
   "([^"]+)"` in the source, and a generator that does not match is not reported
   as broken, it is not reported at all. The first migration removed the call,
   so the gate would have gone on saying OK over a script it no longer checked.
   Discovery matches `pl-name` too now. **That file is fester's**, and the
   change is one line plus its reason.

**One shape question the migration answered.** Each stage lowers as its own
`ScSequence` rather than being concatenated flat, because `emit-ps-cmds` gives a
nested sequence a trailing newline that a flat list does not: concatenating the
bodies emitted a script three blank lines short. Blank-line placement therefore
stays inside the stage bodies where the generators already carry it, rather than
moving into the lowering where a better model would own it. That is a migration
constraint and not a preference, and it is the first thing to revisit once a
generator exists that has no shipped script to match.

**What the next generator should be chosen for.** Not size. `bundle-app` was
chosen because it carries ZERO raw nodes, so the model had to reproduce text
that constructors already emit rather than text a raw payload spells. The second
should carry a resource and a real verdict, so the two declared-only fields get
their first reader.

Damian, 2026-09-08 03:50: "we have a great deal of work to do on the .ps1
script generation instead of direct coding. we need first, however, to work
over the script ast to be more generic and not just a bunch of named
constants for spaces and specific powershell commands. we need to model the
actual steps and stages in terms of input, transformation, and then be able
to build the code into any language, not just powershell."

## What exists (measured 2026-09-08)

- `codex/foreword/shell/ShellTypes.codex`: a shell AST with about 80
  expression constructors (`Se*`) and the statement constructors (`Sc*`),
  emitted by `PowerShellEmit`, `BashEmit` and `KshEmit`.
- 58 generator chapters under `codex/build/*Script.codex` (2026-09-08), one
  per shipped `build/*.ps1`, checked by `check-generated-scripts` for zero
  drift against the shipped text.
- `ShellDslReadability.md`: the readability campaign, whose metric is the
  count of `ScRaw` and `SeRaw` sites (raw shell text carried through the
  AST untouched): 6,396 and 570 across 36 of the generators at the last
  census.

## What is wrong with the model, in one sentence each

- **The AST is the target language with Codex names.** `SeMethodCall`,
  `SeStaticCall`, `SeStaticProp`, `SeScriptRoot`, `SeErrorText`,
  `SeLastExit`, `SeHashLiteral` and `SeOrderedMap` are PowerShell and .NET
  semantics, so a generator written against them is a PowerShell script in
  disguise and `BashEmit` can only emit the subset that has a Bash shape.
- **A generator is a flat statement list.** Nothing names a stage, what a
  stage consumes, what a stage produces, what a stage needs from the box (a
  guest, a port, a kernel), or what a stage's failure means. `build.ps1`'s
  phases exist only as comments and variable names.
- **The readability campaign lowers the raw count without changing the
  model.** A `need-file` helper is a nicer PowerShell line; the pipeline is
  no more visible after the sweep than before.

## The proposal

1. **A pipeline model, not a shell AST, is the source.** A build is a
   graph of stages. A stage declares its inputs (files, digests, flags, the
   outputs of earlier stages), its transformation (one named operation:
   compile, link, sign, verify, boot-and-read, compare, census), its outputs,
   its resources (guests, memory, ports, a kernel), and its verdict (what
   each exit means). Ordering is by data dependency, not by list position.
2. **Two lowerings.** The model lowers to the existing shell AST, which
   keeps the 58 generators and their zero-drift check alive during the
   migration; and the model lowers directly to other targets when a target
   exists: Bash and Ksh today, Codex itself (the boot-image runner, `Build.md`
   Phase B), Python or a CI YAML when wanted.
3. **Migration is one generator per CL**, the shipped script byte-identical
   before and after (the drift check is the gate), the smallest generators
   first (`bundleapp`, `plugbuild`, `runplugchain`), `build.ps1` and
   `test.ps1` last.
4. **The metric moves from raw-text count to stage coverage:** how many of
   a script's phases are modelled stages with declared inputs and outputs.

## What this page does not decide

The model's exact shape (a record type, a Codex DSL, or prose in the
Codex Prose Language), the first target beyond the three shells, and
whether the readability campaign continues in parallel or stops. Those are
the design pass, and the design pass is the first unit of the campaign.

## Owner and order

reek (owner of `ShellDslReadability.md` since 2026-08-16; Damian pointed
reek at the script AST lift on 2026-09-08 05:15). First a ratchet runner
that fails the gate when the `ScRaw`/`SeRaw` count rises above the recorded
baseline (7,274 / 570 across 37 of 58 generators, measured 2026-09-08), then
the design pass: the model's type and one generator migrated end to end,
back to Damian for review before a second generator is touched. fester keeps
`check-generated-scripts` and the shipped-script drift gate.
