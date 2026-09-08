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
`check-sidecars 111`, `check-errors 139`, `check-effect-vocab 113`,
`concat-codex-self 175`, `check-vm-differential 141`, `plug-build 31`, all
`extract-annotations 192`, `check-plug-types 159`, all `match / 0 drift`.
Twenty-five of the 58 generators (2026-09-08). Opened 2026-09-08 by root on
Damian's direction.**

## The census: what is left, counted rather than sampled (reek, 2026-09-08)

Measured over all 58 generators without a guest, resolving each assembly
through the body identifier `sh-script` actually names and following a loop or
`try` body one level into its helper definition.

| | count |
|---|---|
| migrated | 47 |
| flat, migrable today | 1 |
| BLOCKED by burr 4 | 10 |

**THE FLAT SET IS NOW GENUINELY EMPTY.** `cvmm-build match 69 / 0 drift` is the
forty-seventh and was taken the moment the correction below showed it was
takeable. The one flat generator left is `testrunBashScript`, which has no
target and is declared as such; **nothing else can be migrated until the tree
lands**, and the blocked ten are the whole remainder.

**CORRECTION, 2026-09-08: "TWO can never be graded" WAS WRONG, and this page has
said it since the census was first written.** `cvmm-build` grades and has graded
since 2026-08-07: its target moved to `apps\cvmm\build.ps1` and `$AltTarget` in
`check-generated-scripts.ps1` has carried that mapping ever since. Measured just
now: `cvmm-build match 69 / 0 drift`, `0 with no target`. **`cvmmbuildScript` is
therefore a TAKEABLE flat generator, not an ungradeable one**, and the flat set
is not empty. Exactly ONE generator in the tree is genuinely target-less:
`testrunBashScript`, which emits `build/test-run.sh`, a file that has never
shipped.

The claim survived because it was carried forward rather than measured, which is
the thing this campaign's own subject `check-doc-counts` exists to prevent
(L-COUNT). It was stated in this session's CL descriptions repeatedly before
being checked.

### The drift gate now REFUSES an undeclared missing target (reek, 2026-09-08)

**A generator with no target grades nothing, and the gate reported that and then
exited 0.** L-ACCEPTED's shape in the gate's own lane: an interface tolerating
what it does not recognise, so a generator can sit unchecked indefinitely while
the table beneath it reads `0 drifted`.

`check-generated-scripts.ps1` now carries `$NoTargetByDesign`, one entry with
its reason, and FAILS on any other missing target, naming the three repairs
(the target moved, so map it in `$AltTarget`; it is gone, so delete the
generator; it is absent on purpose, so declare it). The one declared entry is
`build\test-run.sh`: `testrunBashScript` is **kept rather than retired** because
it is the only bash generator in the tree, so the unhandled-node scan reaches
`BashEmit` through it and through nothing else. Retiring it would delete that
coverage to silence a warning.

**Graded by ABLATION, both arms, because a guard that has never fired is worth
nothing.** With the declaration removed the run printed the refusal and exited
1; with it restored the run printed `no target, by design` and exited 0. The
first attempt at the ablation FAILED TO ABLATE (a bad regex left the entry in
place) and the run passed, which would have read as the guard being fine: an
ablation must be checked for having actually happened before its colour is
read.

**`check-doc-counts` WAS THE EXTRACTION SUBJECT** (`match 446 / 0 drift`, the
forty-sixth). Its `ccd-full` interleaved six named sections with six inline
`ScSequence` blocks; the six inline blocks are now named bodies and the whole is
twelve stages. **Two mistakes were made getting there and both are worth the
line:**

- **Renaming half a set is a compile error that reads like a design problem.**
  The six inline blocks got `-body` names while the six ALREADY-NAMED sections
  kept theirs, so the stage records referenced `s02-body` where the file defined
  `s02`. `check-generated-scripts` reports this as `COMPILE FAILED` with no
  diagnostic; `compile.ps1` with an explicit `-Log` names it in one line. Ask
  the compiler, not the harness.
- **A quotation mark inside `ps-why` ends the string.** `"why saying "never
  carry a count forward" has not worked"` parsed as three fragments and raised
  five `CDX3002`s naming ordinary English words, which reads as a lexer defect
  rather than as an unescaped quote. A cheap guard before any run: count `"` per
  stage line and refuse an odd number.

**THE MECHANICAL PHASE OF THIS CAMPAIGN IS OVER.** `cdx-to-pe match 1482 / 0
drift` is the forty-fifth and the last rename-plus-separator subject. Everything
still unmigrated is one of three kinds: the ten waiting on the tree, the two
that can never be graded, and `check-doc-counts`, whose stage bodies do not
exist as named definitions yet. **File SIZE turned out to predict nothing**:
`cdx-to-pe` is 102 KB and four stages, `build-magic-pages` is 4.6 KB and
blocked. What predicts the work is the ASSEMBLY, every time.

**`vm-config` IS THE LARGEST SUBJECT SO FAR** (`match 1157 / 0 drift`, the
forty-fourth, twenty stages) and it earned a trap worth writing down.

**A `.codex` FILE IS UTF-8 WITHOUT BOM IN THE WORKSPACE, whatever Perforce types
it.** `p4 diff2` calls these files `unicode`, and rewriting one as UTF-16
produced a WHOLE-FILE diff, `@@ -1,107 +1,149 @@` with every line changed, while
the content was correct. Re-encoding to UTF-8 without BOM brought the same
content back to 69 changed lines in two hunks. Prefer the editing path, which
preserves encoding for free; when a whole-file rewrite is genuinely warranted,
count `^[+-][^+-]` lines from `p4 diff -du3` before believing it landed clean
(P-EOL, a second face of it).

**A FIFTH INDEPENDENTLY-WRITTEN VACUITY GUARD, in `build-arm64-img`**
(`match 252 / 0 drift`, the forty-third). Its DMA floor assertion says it in the
script's own words: "An unmatched pattern is a failure, not a skip", and cites
the `check-doc-counts` rule while doing so. Five shipped runners now carry this
guard, each written by a different hand: `check-effect-vocab`,
`check-plug-types`, `check-plug-builtins`, `compile-riscv`'s remap window, and
this. **L-VACUOUS has been independently rediscovered five times in this tree
and was recorded in none of the five places before this campaign**, which is a
stronger argument for the runner column of `LESSONS.md` than any of the five is
on its own.

`build-boot-img match 206 / 0 drift` is the forty-second: seven stages, one
refusal each, and the five separate tools the boot image passes through named in
order for the first time (bundle, compile, PE plug, optional agent, GPT image).

**`build-img` HAS NO SEPARATORS AT ALL, which is the other end of the
`plug-build-lib` case** (`match 760 / 0 drift`, the forty-first). Its assembly is
`[p01, p02, ... p10]` with no `ScBlank` anywhere, because the ten sections are
one continuous PowerShell program cut up for reading. No body took a leading
blank. Between this and `plug-build-lib` the separator rule is now demonstrated
at both extremes: read the flat assembly, do not assume a pattern.

**It is also the strongest case yet for `ps-why` over `ps-verdict`,** because
`build-img` exits nowhere. What the stage list buys is that the FAT16
cluster-count choice, which exists so the count lands solidly inside FAT16
rather than near a boundary some firmware reads as FAT12 or FAT32, is now a
named stage instead of a comment in the middle of a geometry block. Same for the
fact-store window, where a disagreement makes the guest find no region and
refuse every write forever with nothing saying why.

**`ablate-doctrine` CARRIES THE ONLY SELF-TEST STAGE ON THE CAMPAIGN, and its
refusal is the strongest verdict text written so far** (`match 576 / 0 drift`,
the fortieth). Before the harness scores a single agent run it must return FAIL
for a bad artifact and PASS for a good one, with no agent involved; three of its
six synthetic candidates are deliberately wrong in the ways that matter. So its
exit 1 does not mean "an arm failed", it means the INSTRUMENT failed and no
score from the run means anything. That distinction is invisible in the shipped
script and is exactly what `ps-verdict` exists for. It is L-FALSIF wired as a
gate rather than written as a lesson, and the only subject here that tests
itself before testing anything else.

**`compare-codex-semantic` IS FIFTEEN STAGES OF WHICH NINE EXIST ONLY TO STOP A
NON-DIFFERENCE COUNTING AS ONE** (`match 534 / 0 drift`, the thirty-ninth). Type
names canonicalised, emitter name mangling undone, redundant parentheses
stripped, operator aliases folded, and colliding names resolved by body and
signature: every one of those is there because the emitted text and the source
text differ in a way that is not a semantic difference, and treating any of them
as one would drown the real findings. The stage list is the first place that
whole apparatus is visible as apparatus.

**Its verdict is the honest kind and now says so:** the run passes only when
NOTHING was dropped and nothing differs. A dropped definition is one the
comparison never looked at, which is a hole rather than a pass, and an
instrument that reported it as a pass would be L-VACUOUS again.

**`test-compile-batch` SPELLS THE ONE VERDICT ON THIS CAMPAIGN THAT IS A DESIGN
DECISION RATHER THAN A DESCRIPTION** (`match 352 / 0 drift`, the thirty-eighth).
Exit `99` means the batch is unattributable, and every member is invalidated
rather than some being reported wrong: when guest serial bytes are dropped or
the stream ends early, positional attribution cannot be trusted, so reporting
nothing beats reporting the wrong subject as failing. Exit `7` is the adjacent
state, a corrupted REPL session emitting diagnostics that belong to no subject.
Both were reachable only by reading the script; both are now declared.

Its two `0`s are the empty-batch pair: the list named no sources, and no source
survived resolution. Neither is a pass, and both are spelled `0`.

**`test-cross` RETURNS 0 FOR EIGHT DIFFERENT STATES AND SIX OF THEM ARE SKIPS**
(`match 270 / 0 drift`, the thirty-seventh, seventeen verdicts). The eight: the
output matched `.expected`; the subject was refused by design and its tags
predicted the refusal; and six skips, which are multi-core, a `.skip` sidecar, a
`.no-cross` sidecar, slow, fatal, and an error test the frontend alone can
judge. **A caller aggregating exit codes counts every one of those six as a
pass**, which is L-DENOM's shape at the level of a single subject: the score's
denominator is the set that RAN, and nothing in the exit code says which set
that was. The skip lines are printed, so a person reading the console can tell;
a program cannot. This is a finding about `test-cross`, not about the model, and
it belongs to whoever owns the cross battery's aggregation.

**`test-cross` also separates two failures a mismatch would have hidden:** no
UART output at all is exit 1 from its own stage, distinct from output that
differs. A board that said nothing and a board that said the wrong thing are not
the same defect.

**`check-doc-counts` NEEDS EXTRACTION, NOT RENAMING, and is left for a session
with room.** Its `ccd-full` interleaves six named sections (`s02`, `s03`, `s06`,
`s08`, `s09`, `s10`) with four large INLINE `ScSequence` blocks, so the stage
bodies do not exist as named definitions yet. Every other subject on this
campaign has been a rename plus a separator; this one is the first that must
lift inline blocks into bodies before it can be staged, and each lift is a
chance to move a byte.

**AN ASSEMBLY IS NOT ALWAYS UNIFORMLY BLANK-SEPARATED, and assuming so drifts
the output** (`plug-build-lib match 246 / 0 drift`, the thirty-fifth).
`lib-body` reads `[... s03, s03b, ScBlank, s04, ...]`: two of its eight sections
follow their predecessor with NO `ScBlank`, because each is the continuation of
one PowerShell function. The migration rule is therefore not "every body after
the first takes a leading `ScBlank`" but "each body takes exactly the separator
the flat assembly held before it", and the two continuation stages take none.

**A SCRIPT THAT REFUSES BY `throw` HAS NO DECLARABLE EXIT, and `boot-arm64` is
the first subject where that is the whole failure surface** (`match 226 / 0
drift`, the thirty-fourth). Its five failure paths are all `throw`: the plug is
not built, the IR compile failed, codegen failed, the disk image failed. None of
them is an `ScExit`, so `ps-verdict` can declare only the one `PoExit 0` the
`-NoBoot` path spells, and the four refusals a caller most needs explained are
undeclarable. This is not the computed-exit gap and not the passthrough gap: it
is a THIRD way a refusal escapes `PipeOutcome`, and it wants naming in the same
second pass rather than a constructor of its own.

**`compile-riscv` CARRIES ONE CODE WITH THREE MEANINGS AND A GUARD THAT REFUSES
WHEN ITS OWN REGEX STOPS MATCHING** (`match 288 / 0 drift`, the thirty-third,
fifteen stages). Its remap-window stage exits `8` for three different states:
the image outgrew the window, so a low-map address is read out of RAM with no
diagnostic; the check cannot read `RiscVRuntime.codex`, so the window is
unknown; and the shift no longer matches the check's pattern. That third one is
the vacuity shape again, written by a fourth author who reached the same
conclusion in the script's own words: "a check whose regex stopped matching has
quietly stopped asking".

**`compile-arm64` IS THE FIRST SUBJECT WHOSE TWO EXIT CODES ARE A DIAGNOSIS**
(`match 233 / 0 drift`, the thirty-second). `3` is the IR compile failing, so no
wire was produced and the plug was never reached; `4` is the plug failing on IR
the compiler accepted, so the defect is in the plug or in the wire between them.
A caller reading only "compile-arm64 failed" cannot tell a compiler bug from a
plug bug, and those two send a reader to different files.

**A THIRD VACUITY GUARD, in `check-plug-builtins`** (`match 240 / 0 drift`, the
thirty-first generator). Its fourth stage refuses when either side extracted
nothing, in its own words again. Three shipped runners now carry that guard,
each written independently, and none of the three was recorded anywhere a
reader would find before this campaign. L-VACUOUS has been paid for three times
in this tree.

**A SECOND VACUITY GUARD, in `check-plug-types`.** Its fourth stage compares
nothing: it refuses when either side extracted zero forms, because an empty set
agrees with everything and a check that cannot fail is a comment. That is the
same guard `check-effect-vocab` carries, independently written, in two shipped
runners. L-VACUOUS has been paid for twice in this tree, and until this
campaign neither instance was recorded anywhere a reader would find it.

**CORRECTION, 2026-09-08: this table read 25 flat and 9 blocked until now, and
both were wrong by one.** The census searched for `ScForEach`,
`ScLabeledWhile`, `ScWhile` and `ScTry` and NOT for `ScIf`, so
`applyannotationsScript`, whose last section runs only under `ScIf (SeVar
"Apply")`, was counted flat. A conditional wrapping a numbered section blocks a
flat stage list exactly as a loop does. Found by reading that generator to
migrate it, not by the census, which is the third time on this campaign that a
census agreed with itself and was wrong (L-CENSUS).

**`ScIf` CHANGES WHAT SHAPE 3'S GROUP NODE MUST CARRY, again.** The blocked ten
now need three distinct things of a group: a REPEAT condition (`while`,
`foreach`), a HANDLER stage (`try`/`finally`), and a GUARD condition deciding
whether the group runs at all (`if`). A group carrying only a repeat expresses
two of the ten.

**Two of the 58 can never be graded and are not in any column above:**
`testrunBashScript` emits `build/test-run.sh` and `cvmmbuildScript` emits
`build/cvmm-build.ps1`, and NEITHER FILE EXISTS. The drift check reports both
under "no target", so a migration of either could not be proven byte-identical
and none was attempted (L-NOGATE). That is a gap in the drift gate rather than
in this campaign: a generator with no target is graded by nothing at all, and
two of them have been sitting that way.

**A FOURTH accepted-and-unused parameter**, found by the smallest generator in
the tree. `build/plug-build.ps1` declares `-Force` and no line reads it;
verified twice at head, once that `Force` appears only on its parameter line,
and once that the builder it calls has no such parameter to forward it to (the
only `Force` in `codex/plugs/common/plug-build-lib.ps1` is an unrelated
`New-Item -Force`). No wiring exists that could ever carry the switch, so a
caller asking for a rebuild gets an ordinary one. With `sweep-apps`' `-Jobs`,
`check-plug-ports`' silent half and `concat-codex-self`' dead `$ForewordDir`,
that is four of one shape in twenty-three generators, every one surfaced by
having to write down what a stage consumes and produces.

**A generator can be worth migrating with NO stage structure at all.**
`check-vm-differential`'s whole assembly is one `ScSequence`, so it migrated as
a pipeline of exactly one stage; splitting the raw body would gain a blank line
per boundary and drift. The return there is entirely the verdict list: six
exits, every one spelled inside a raw payload, and TWO OF THE SIX ARE ZEROS
THAT MEAN OPPOSITE THINGS, a machine with one VM host skipping and two hosts
agreeing byte for byte. A caller gating on that script's exit code cannot tell
the check from its own absence. Where stage structure is absent, `ps-verdict`
and `ps-needs` still pay.

The nine, by the construct wrapping their numbered sections: `stress-sweep` and
`CompileScript` (`while`); `build-magic-pages` and `test-exception-handler`
(`foreach`); `run-plug`, `plug-run`, `test-run`, `test-disk-compile` and
`gdb-watchpoint` (`try`/`finally`).

**`try`/`finally` is the MAJORITY construct among the blocked, five of nine.**
That settles the shape question this page priced earlier: a group node carrying
only a repeat condition leaves five of the nine still unmigrable, so the group
needs a handler field as well.

**OF THE FOUR SCRIPTS WHOSE STAGE STRUCTURE IS WORTH THE MOST, ONLY
`CompileScript` IS BLOCKED.** This page claimed `build.ps1`, `test.ps1` and
`bvt` were blocked with it, and the claim contradicted the enumeration three
lines above it, which names none of the three. Measured at head 2026-09-08:
`build-body`, `test-body` and `bvt-body` are each a plain `[ScSequence gNN,
ScBlank, ...]` list, and all of their numbered sections (10, 6 and 12) are
referenced directly from that list with none nested inside a loop, a `try` or
an `if`. All three are in the flat set and takeable without the burr-4 ruling.
The ruling still decides `CompileScript` and the other nine, and the arithmetic
was never wrong: 27 + 10 + 21 is 58 either way.

**ALL THREE ARE MIGRATED, WHICH SETTLES THE CLAIM BY CONSTRUCTION** rather than
by measurement: `bvt match 544 / 0 drift` (13 stages), `test match 1457 / 0
drift` (6 stages) and `build match 1449 / 0 drift` (10 stages). No ruling was
used and none was needed. `CompileScript` alone, of the four scripts whose stage
structure is worth the most, waits on burr 4.

**`build` IS THE CASE FOR ONE VERDICT PER STAGE RATHER THAN ONE PER `exit`.**
The script spells 48 exits and 47 are the same code. Forty-seven identical
`PoExit 1` entries would be a longer declaration saying less, so each stage
carries the one sentence separating its refusal from its neighbours': a
pre-build guard is not a fixed-point failure is not a plug disagreeing with
x86-64. The single `0` is g10's, and it is the only success the script has.

**`test` IS THE STRONGEST CASE THE VERDICT FIELD HAS HAD: eleven exits, every
one inside a raw PowerShell payload, in three currencies.** `1` is an approval
or a selector the caller got wrong, `2` is a kernel absent, unnamed or stale,
and `0` is TWO STATES THAT ARE NOT THE SAME, `-ListSubjects` having printed a
list and the battery having passed. A caller gating on the exit code cannot
tell the listing from the pass.

**A CENSUS OF `exit N` OVER `test` REPORTS FIVE EXITS THAT DO NOT EXIST.** T04
names `exit 7` and `exit 4` in prose, describing what the batch VM returns
(phantom diagnostics, a crash), not what `test.ps1` returns. The classification
that answers correctly asks whether each match sits inside an `ScRaw` or an
`ScComment`. Fourth time on this campaign a census agreed with itself and was
wrong (L-CENSUS), and the first where the wrong answer would have shipped as a
declaration.

**A GUEST COUNT DRIVEN BY A PARAMETER CANNOT BE DECLARED, third instance.**
`PrGuests` takes an `Integer`; `bvt` boots one guest per `-Jobs` slot in two
stages. Writing `PrGuests 8` would record the DEFAULT as though it were the run,
which is L-REQUEST's shape exactly. Both stages declare `PrKernel` alone, which
takes a `ShellExpr` and can name `stage0` honestly. All three vocabulary gaps
now carry two or more instances, and widening the three Integer resource
constructors is the one that has now been hit three times.

**The first two passes of this census were WRONG and agreed with themselves.**
A regex for `*-body` matched an earlier helper rather than the assembly, and a
second pass missed every generator whose loop body is a named helper rather
than an inline list. Both produced a clean-looking table. The third pass was
trusted only because it reproduced all four generators already blocked by hand
(L-CENSUS: a clean grep is a statement about the grep).

## What declaring an output asks, and one answer it got (reek, 2026-09-08)

`check-effect-vocab match 113 / 0 drift` and `concat-codex-self match 175 / 0
drift`.

**`concat-codex-self` PRODUCES `$ForewordDir` AND NOTHING READS IT.** Line 22
of the shipped script assigns `codex\foreword\core`; no later line mentions the
name, verified at head. It is vestigial, because foreword directories resolve
through `$QuireDirs` from `quire-map.ps1` now, so the hardcoded path is a
second and stale answer to a question the quire table already answers, and a
reader takes it as authoritative. Declaring it as an artifact is what asks who
consumes it, and the answer is nobody. Left in place: deleting it moves the
emitted bytes, and this migration's gate is byte-identity.

That is the third finding of this shape, after `sweep-apps`' unused `-Jobs` and
`check-plug-ports`' silently skipped half. The three together are the argument
for `ps-in` and `ps-out` being more than documentation: a declared input with
no producer, and a declared output with no consumer, are both decidable by a
future check, where prose about them is not.

**`check-effect-vocab` carries the campaign's clearest single verdict.** Its
third stage compares no lists at all: it asserts that both sides parsed
something, because two empty sets AGREE and a regex that has stopped matching
would otherwise report a clean comparison. That is L-VACUOUS built into a
shipped runner, and until this migration the only record of it was the shape of
the code.

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

**RULED BY DAMIAN, 2026-09-08, relayed through root: SHAPE 3.** `pl-stages`
becomes a tree. A step is a stage or a group; a group carries its own steps plus
an optional repeat condition, an optional guard condition, and an optional
handler stage. That is the shape the blocked ten need, and the three optional
fields are the three constructs measured below: repeat for `while` and
`foreach`, guard for `if`, handler for `try`/`finally`.

**THE PROOF OBLIGATION IS NAMED IN THE RULING: every already-migrated generator
must stay byte-identical.** Forty-two are on the model as of this CL, so the
tree lands only when `check-generated-scripts` reads `match / 0 drift` for all
of them, not for a sample. That is the one run on this campaign that is a
fan-out rather than a single guest, and it is asked of the commander.

**The second half of the ruling: the RUNNER, designed on this page and built
nowhere yet.** Executing a pipeline as a process instance uses
`apps/workflow/WorkflowTypes.codex`'s vocabulary rather than a second one
invented here: `ProcessInstance` and `ProcessStatus` for a run in flight,
`StepDef` and `StepStatus` for where each stage stands, `StepEvent` for what
happened, and `ConditionOp` for the guard and repeat conditions a group carries.
`StepStatus` already spells `StepPending`, `StepInProgress`, `StepCompleted`,
`StepSkipped` and `StepTimedOut`, which is exactly the state a build stage is
in, and `StepSkipped` is the state `test-cross` currently reports as exit 0
alongside a pass. Design first, build nothing.

The three shapes as they were priced, kept because the ruling chose among them:

1. **A stage carries the loop in its body** and the inner sections stop being
   stages. Cheapest, and it gives up exactly what the model exists to provide.
2. **`ps-repeat : Maybe ShellExpr` on `PipeStage`**, so a stage can say it runs
   while a condition holds. Handles a loop over ONE stage; `stress-sweep` needs
   a loop over FOUR consecutive stages, so it does not fit without grouping.
3. **`pl-stages` becomes a tree**, a `PipeStep` that is either a stage or a
   group carrying a repeat condition and its own stage list. Correct for every
   case named above, and it changes the lowering, the doc renderer and both
   fields' readers.

Shape 3 is what the scripts actually are, and shape 3 is what was ruled.

The shape as ruled, to be spelled in `Pipeline.codex`:

```
PipeStep =
  | PsStage (PipeStage)
  | PsGroup (Maybe ShellExpr)    -- repeat: while, foreach
              (Maybe ShellExpr)    -- guard: if
              (Maybe PipeStage)    -- handler: try/finally
              (List PipeStep)
```

`pl-stages` becomes `List PipeStep`. `pipe-stage-cmds` walks the tree instead of
a list, and `pipeline-doc` renders nesting. Every migrated generator becomes a
list of `PsStage`, which is why the byte-identical obligation is checkable
rather than hopeful: a flat pipeline lowered through the tree walker must emit
what the flat walker emitted.

**HOW THE PROOF RUNS, granted in principle by root 2026-09-08.**
`check-generated-scripts` boots ONE GUEST PER GENERATOR (57 on 2026-09-07), so
the all-42 proof is a fan-out and not a single-guest run. Conditions on the
grant: bounded `-Jobs`, four at most; ask at the time with the job count and the
expected minutes; root grants FIFO against the box then; and **merge down to the
seed at head before the proof**, because a proof under a superseded seed is void
and this campaign has already paid for that once.

## The runner: a pipeline executed as a process instance (design only)

The second half of the 2026-09-08 ruling. **Nothing is built here yet**, and
nothing in `apps/workflow` is touched: this section records how the two
vocabularies meet so that whoever builds it does not invent a third.

**THE RULE IS THAT `apps/workflow/WorkflowTypes.codex` IS THE VOCABULARY.** A
build is a process, a stage is a step, and every noun the runner needs already
exists there with a shipped definition. Inventing `PipeRun`, `PipeStageStatus`
and `PipeEvent` beside them would be the fourth copy of a concept this tree
already keeps once, which is the failure `check-facts-guid` and
`check-plug-types` both exist to catch in their own domains.

The correspondence, measured against the records at head:

| the pipeline model | the workflow vocabulary | what carries over unchanged |
|---|---|---|
| a `Pipeline` | `ProcessDef` | `pd-start-step`, `pd-transitions` |
| one run of a pipeline | `ProcessInstance` | `pi-status`, `pi-current-step`, `pi-history` |
| a `PipeStage` | `StepDef` | `sd-id`, `sd-name`, `sd-description` |
| where a stage stands | `StepStatus` | the five states below |
| what happened to a stage | `StepEvent` + `EventType` | `se-step-id`, `se-timestamp`, `se-details` |
| a group's repeat or guard | `ConditionOp` | `And`, `Or`, `Not`, `Always` |
| an edge between stages | `TransitionDef` | `td-from-step`, `td-to-step`, `td-condition` |

**`StepStatus` ALREADY SPELLS THE STATES A BUILD STAGE IS IN**, which is the
finding that makes this correspondence worth having rather than merely tidy:
`StepPending`, `StepInProgress`, `StepCompleted`, `StepSkipped`, `StepTimedOut`.
**`StepSkipped` is the one that pays.** `test-cross` reports six different skips
as exit `0`, indistinguishable by a caller from a pass, and a runner carrying
`StepStatus` cannot make that mistake: a skipped stage is a different value from
a completed one, so a tally over statuses reports what RAN as well as what
passed. That is L-DENOM answered by the type rather than by a convention.

**What does NOT carry over, said plainly.** `StepType`'s arms are about people:
`HumanTask`, `ApprovalGate`, `DocumentGate`, `Notification`. A build stage is an
`AutomatedAction` and almost nothing else, with `SubProcess` for a stage that
shells out to another pipeline and `ParallelSplit`/`ParallelJoin` for a fan-out
like the batch compile. `sd-form`, `sd-assignment`, `sd-required-docs` and
`AssignmentRule` have no meaning for a build and are left unset rather than
given invented values, because a field filled with a plausible default is the
`L-BEDTRUE` shape: a lie with a plausible value.

**The one thing the workflow vocabulary lacks and the pipeline model has** is
`PipeOutcome`. A `StepStatus` says a step finished; it does not say what the
exit code MEANT, which is the whole return of this campaign. So the runner keeps
`ps-verdict` on the stage and records the matched outcome in `se-details` when a
stage exits. That is the join point, and it is the only new idea the runner
needs.

### The execution semantics, stated (reek, 2026-09-08)

**A pipeline runs as a `ProcessInstance`, and every one of the four things a
runner must do maps onto a noun that already exists.**

**Stage entry and exit are `StepEvent`s.** Entering a stage appends
`EvStepEntered` with `se-step-id` the stage name and `se-timestamp` the clock;
leaving it appends a completion event whose `se-details` carries the matched
`PipeOutcome` text. `pi-history` is therefore the build log, in order, with the
MEANING of each exit beside it rather than a number a reader has to look up.
`pi-current-step` is what a watcher reads to say where a build is.

**A verdict is a transition.** `TransitionDef` already carries
`td-from-step`, `td-to-step` and `td-condition`, so a stage's `ps-verdict` list
becomes the outgoing edges of that stage: one `TransitionDef` per `PipeOutcome`,
whose condition tests the exit code and whose `td-label` is the outcome's own
sentence. **This is what makes the verdict field executable rather than
documentary**, which is the gap the model has carried since the first migration.
A stage with an empty `ps-verdict` has exactly one unconditional outgoing edge,
`Always`, which is the right reading of a stage that cannot fail.

**A group is a subgraph, and its three optional fields are three known shapes.**
A repeat condition is an edge from the group's last step back to its first, under
`td-condition`; a guard is the condition on the edge INTO the group, so a guarded
group that does not run is `StepSkipped` rather than absent; a handler is an edge
from every step in the group to the handler stage, which is what makes
`try`/`finally` expressible at all.

**Fan-out is `ParallelSplit` and `ParallelJoin`,** which `StepType` already
spells. `test-compile-batch`, `bvt` and `test` all fan out over subjects, and
the join is where a runner learns what a batch aggregate currently cannot see:
each branch carries its own `StepStatus`, so `StepSkipped` and `StepCompleted`
are counted apart at the join instead of being summed as one exit code.

**PRICED, and the price is mostly not the runner.** Three parts, in the order
they must happen:

| part | what it costs | why that number |
|---|---|---|
| the tree in `Pipeline.codex` | one CL, plus 46 one-line conversions | `PipeStep`, a tree walker for `pipe-stage-cmds`, a nested renderer for `pipeline-doc`; every generator's `pl-stages` becomes `PsStage`-wrapped |
| the byte-identical proof | ONE fan-out run, `-Jobs 4`, a box grant | 46 generators at one guest each; root's conditions are recorded above |
| the ten blocked generators | ten CLs, one each, one guest each | the same recipe as the 46, with a group where the control flow is |
| the runner itself | its own campaign, not costed here | nothing is built until the three above are green |

**The tree is the expensive part and the runner is not.** The runner is a
translation between two vocabularies that both exist; the tree touches every
file this campaign has already touched, which is why its proof is the fan-out
and why it wants a fresh session rather than the tail of one.

**Build order when it is built**, so the first pass cannot be graded on itself:
the tree first, with the byte-identical proof; then the runner over a pipeline
that is already proven; and the runner's own first subject is a pipeline whose
stages are known to fail in known ways, because a runner that has only ever seen
a green build is an instrument that cannot fail (L-FALSIF).

### Burr 4 is WIDER than a loop, measured over sixteen attempts (reek, 2026-09-08)

Three generators are blocked, and each is blocked by a DIFFERENT construct
wrapping its numbered sections:

| generator | what wraps the stages |
|---|---|
| `stress-sweep` | `ScLabeledWhile` around sections 3 to 6 |
| `build-magic-pages` | `ScForEach` over the page list around sections 3 to 5 |
| `run-plug` | `ScTry` around sections 3 to 7, with section 8 as its FINALLY |
| `test-exception-handler` | `ScForEach` over the sample table around sections 3 to 5 |
| `apply-annotations` | `ScIf (SeVar "Apply")` around section 5, a GUARD not a repeat |
| `plug-run`, `test-run`, `test-disk-compile`, `gdb-watchpoint` | `ScTry`, same shape as `run-plug` |
| `CompileScript` | `ScLabeledWhile`, same shape as `stress-sweep` |

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

**`check-cdx-registry` is the second generator to hit it** (`check-cdx-registry
match 213 / 0 drift`, the twenty-sixth on the model). Its S02 defines
`ConvertTo-Pascal` and its S06 calls it, so the same edge is carried by
PowerShell scope and declared nowhere. Two hits is the same count the computed
exit and the parameter-driven resource each carry, so all three gaps are
equally evidenced and go to the second pass as one decision.

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
