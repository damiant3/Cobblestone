# The Lessons

*Proposed 2026-07-25 by blu, for review. This is an index, not a summary.*

## What this is for

The stories in this directory are about 180 KB. Every agent used to read all
of them at init; since 2026-07-28 (the init diet, Damian's direction --
session start had grown to ~190k tokens of reads) **this index is what init
reads, and a story is read in full the moment its lesson becomes
load-bearing for the session's work**. The original reason stands: a lesson
lives in the middle of a post-mortem, not in its title, and the failures
repeated for months while the accounts sat unread in `Done/`. The on-demand
rule is the counterweight -- an id you are about to lean on or breach OBLIGES
the full read, then the action.

But the compression has already been attempted once, and it is `CLAUDE.md`.
Extract the rules, delete the narrative, load the rules every session. On
2026-07-25 that document told every agent, in forty confident lines, a
mechanism that was false at every step; the ban it argued for had 761
counterexamples sitting in the tree; and `AgentCommunication.md` is an
autopsy of violating rule 10 written by an agent who had just read rule 10.

So the problem is not length. **`CLAUDE.md` is a test suite with no runner.**
Every line is an assertion, nothing evaluates any of them, and unevaluated
assertions rot exactly the way this project documents everywhere else.
Compressing harder produces a shorter document that is wrong faster.

This file is the other half of the answer: a stable id per lesson, one
sentence, and a pointer to the evidence. The id is the point. A CL
description, a review, or a probe can cite `L-ORACLE` and mean something
precise, and the story stays whole and unread until somebody needs to know
why.

**A lesson with no runner is a lesson nobody is enforcing.** The last column
says which have one. Most do not, and that is the honest state rather than a
gap to be embarrassed about: `check-sidecars.ps1` and `check-cdx-registry.ps1`
prove the pattern works, and each found real defects on its first run.

Runners come in two kinds. A **check** is a script that fails the build, and
is available whenever the lesson is mechanically decidable. A **probe** is a
question with a known answer and a paired control, for the lessons that are
about judgement; those live in `docs/Probes/`, which is deliberately **not**
in the init read path, because an agent that has read the answer key cannot
be tested with it.

## The index

| id | the lesson | evidence | runner |
|---|---|---|---|
| L-ORACLE | A harness validates only the half it points at. Write the direction you do not already have, first. | BrotliBeatsOpus | probe |
| L-FALSIF | An instrument that cannot fail is not evidence. A function that always answers the same thing looks exactly like one that works. | TheSilentKeyboard, ExaminersAssay | partial |
| L-GAP | Ask what your suite cannot express before reading its silence as agreement. | x86-64, ExaminersAssay | none |
| L-COUNT | Never carry a count forward. Re-measure before quoting. | CLAUDE.md, this file | check-doc-counts.ps1 |
| L-REPRO | The smallest program that fails is the debugging tool. Having paid for it, keep it as the test. | Opus | none |
| L-SELF | Suspect your own last change first. | ValPostMortem | none |
| L-LINEAGE | Ask how a change got here, not who made it. | ValPostMortem | none |
| L-HOLD | Under pressure, concede the framing and keep the finding. Coherence is not truth. | ValPostMortem | probe |
| L-READ | Read the file. Do not sweep, and do not rewrite a recipe that already exists. | AgentLinuxInferno, IGiveUp | none |
| L-DONTKNOW | Say "I do not know" at the second failed diagnosis, not the sixth. | IQuit, IGiveUp | none |
| L-OUTPUT | Trust the output over your model of the code. Look at what is actually there. | Reflections2, AgentLinux | none |
| L-SUSPECT | Identical where you expected different is a fingerprint, not luck. | VoodooChild | none |
| L-CHANNEL | No campaign against hardware without an output channel independent of the subsystem under test. | TheSilentKeyboard | none |
| L-FALLBACK | Never disable a working path in the same change that introduces its replacement. | TheSilentKeyboard | none |
| L-REFEREE | When the ground contradicts itself, find a referee with no stake, and put the verdict where the human can see it. | TheUnwovenStair | none |
| L-HUMAN | A step requiring a human body is the most expensive line in the plan. Minimise it the way you minimise heap. | TheSilentKeyboard | none |
| L-SHIP | Bootstrap with the tool that already works. Ship something. | GollumFailure | none |
| L-FINISH | A partial item with an essay attached is not done. Do not ask for permission you do not need. | AgentCommunication | none |
| L-CAPABILITY | Scope an item by capability, not by feature. Five closed features can leave the capability absent. | BrotliBeatsOpus | none |
| L-LESS | The machinery that manages a cost can exceed the cost. Ask first what can be removed. | VoodooChild | none |
| L-SHAPE | A truncated approximation fails in SHAPE, not precision. Find where it stops being valid and probe PAST that, because near zero every version agrees. | ExaminersAssay | none |
| L-NAMED | A test named for a chapter is not a test of it. Grep the `.expected` for a digit before believing a chapter is covered. | ExaminersAssay | none |
| L-BODY | The most expensive step in the plan must not be protected by the weakest guard in the tree. If spending a human is gated only by prose, it is not gated. | ProseHasNoRunner, TheStickDidNotBoot, TheSecondStick, TheImageThatWasTwoDaysOld (all in this directory) | none |
| L-ASSUME | Naming an assumption is not declining to build on it. Settle which event you are explaining before you explain it. | the four papers in this directory, 2026-07-29 | none |
| L-MISROUTE | A run sheet that pre-assigns the cause of a tool's failure is worse than one that says nothing: when the tool is what is at fault, nothing in the procedure can contradict it. | TheSecondStick | none |
| L-INTERRUPT | A standing obligation in a document loses to an interrupt fired at the moment of relevance. | ProseHasNoRunner | the merge-down workplan hook |
| L-ARTIFACT | Gate the artifact you ship, not a sibling built from the same source: payload bytes identical is not image identical, and the failure modes live in the vehicle. | TheStickDidNotBoot | none |
| L-SHARED | A fleet tool with a fixed scratch path or a fixed port silently reports another agent's run as yours. Derive both from the workspace, and refuse to start rather than report someone else's screen. | fester, main 12056 | test-ovmf.ps1 refuses a held port |
| L-SUCCESS | Check what SUCCESS looks like before concluding a call failed. Two months of hypotheses about a keyboard were spent on a function returning the right answer in a form nobody could read. | TheKeyboardWasNeverSilent | none |
| L-OPTIONAL | A green emulator arm proves nothing about hardware when the thing under test is an OPTIONAL part of a spec: the bed can be MORE capable than the target, not less faithful. | TheKeyboardWasNeverSilent | none |
| L-STATES | Give a probe distinguishable FAILURE states, not pass/fail. One metal boot of a six-colour probe eliminated three whole classes at once. | TheKeyboardWasNeverSilent | none |
| L-PUBLISHED | A published value and a consumed one are different claims. Grep for the consumer before retiring a warning. | ProseHasNoRunner | none |
| L-SIDECAR | A test run by hand must reproduce the runner's sidecar handling, or it measures the invocation rather than the subject. | TheImageThatWasTwoDaysOld | none |
| L-SABOTAGE | A sabotage that moves FEWER rows than predicted tells you the code is shaped differently than you wrote down, and is worth more than one that moves all of them. **Choosing a bit to corrupt is not choosing a bit the test INPUTS can express**: compute what the sabotage does to the inputs before believing the control. | TheSabotageThatChangedNothing | none |
| L-DECODE | Behaviour identical, layout restored, symptom gone is the signature of a HOST decode gap, not a codegen defect. Ask whether a fix changes BEHAVIOUR or only LAYOUT. | ThePageThatStraddled | none |
| L-IDLE | A device that enumerates perfectly and delivers nothing may be obeying us. Ask what you are telling the device before asking what is wrong with it. | TheKeyboardWasNeverSilent | none |
| L-ERASED | A rule the compiler ENFORCES can be absent from the artifact it emits, so an auditor reading that artifact cannot see the rule at all. Compile the two programs the claim distinguishes and diff the artifact. | IndependentRechecker.md section 4, val CL 12674 | none |
| L-INSTRUMENT | A test that reads a function to observe A is broken by that function correctly learning to do B. Split the function and point the arm at the part that still answers its question; never soften the assertion. | TwoArmsWentRedForAFix; red CL 12901 | none |
| L-GREEN | A "works" claim must carry its measurement: how many times, how long after boot, from which surface, at what geometry. | TheShotThatWorkedOnce | none |
| L-ROUTE | Route measurements, not theories. The falsifying test runs BEFORE the finding is published to another lane. | TheShotThatWorkedOnce | none |
| L-BANK | An instrument must not be reachable only THROUGH the thing it measures. "Rides last" and "runs after the instrument" are different statements and one negates the other; bank the reading in a bounded window before the risky arm. | `docs/Hardware/HardwareSitting.md` msc-align entries, red CL 13355 | none |
| L-UNCALLED | A path nothing calls is a path nothing has tested. Grep for callers before trusting that a capability exists. | TheKeyboardWasNeverSilent, ProseHasNoRunner | none |
| L-ARENA | A bed can be too GENEROUS to express a defect, and generosity does not look like a gap. Compare the bed's RESOURCE ENVELOPE against the shipping artifact's, not just its device model. | OperatorsManual "A BOOT IMAGE RUNS IN 128 MB"; fester 2026-08-08 | none |
| L-SAMEVER | When two implementations disagree, prove they are versions of the SAME THING before instrumenting either. One `LastWriteTime` comparison closes it in a minute and is the first thing to run, not the last. | TheBundleThatWasStale | none |
| L-MYSIDE | When the other implementation looks RIGHT and yours still refuses it, suspect yours first. | IndependentRechecker.md, val C2 `ctor-pat-field-type` | kill-rate corpus |
| L-REFUSED | A caller that ignores a primitive's refusal turns a handled error into silent data loss, and the loss lands on a ROUND NUMBER that names its own cause. Factor a suspicious byte count against the constants on its path before instrumenting anything. | val CL 14317, `codex/os/net/NetIO.codex` | none |
| L-CAPABILITY-LOST | A change that REMOVES a check reports the same aggregates as one that fixes it, so no aggregate can tell them apart. When a change alters HOW a comparison is decided, write the mutation aimed at that direction before believing it. | IndependentRechecker.md section 9, val 2026-08-09 | the `bounded-arg-into-plain-slot` kill-rate arm |
| L-CONTROL | A control that behaves correctly is evidence about the DIFFERENCE between the arms, and the difference can be a defect in the arm you called normal. | IndependentRechecker.md, val 2026-08-10 | the `tvar-spine-branch-arms` kill-rate arm |
| L-TAILGUARD | A guard on the loop is not a guard on the phase. Ask what allocates AFTER the guarded walk, on the same reservation, before calling a phase guarded. | DECK-SHORT-MISCOMPILE (docs/Test/Done/) | the post-loop re-measure in `opening.codex` raises CDX9002 |
| L-FREEDOM | The spec's unspecified choices are where the board and the bed diverge, and a green bed says nothing about any of them. Enumerate the freedoms the code leans on and falsify the arm BEFORE flying. | TheBedThatAlwaysSaidYes | none |
| L-THRESHOLD | A threshold answers "is this different ENOUGH"; a census of exact values answers "is this THE THING", and a threshold set just above the effect returns a null that reads as an all-clear. Count a candidate mechanism's signature rather than thresholding its magnitude. | TheShadowThatMeasuredPastItself | none |
| L-DILUTE | When a lever barely moves your metric, ask what fraction of the metric your subject actually is before concluding the lever is wrong. A difference against a control that REMOVES the subject beats an absolute count over a region. | TheShadowThatMeasuredPastItself | none |
| L-SUBSET | How a unit is ASSEMBLED decides which of its dependencies are declared, and a glob declares none of them. Build each chapter as its own unit and let the compiler answer. | GitHubUpdate43; GitHub PR 64 (Steve Howell), red 2026-08-15 | check-subset-cites.ps1 |
| L-STALL | To provoke a race between a producer and a consumer, STALL the consumer at the critical instant; slowing it uniformly changes nothing while the producer is still the bottleneck. | GitHubUpdate47 | the census line refuses a nonzero drop |
| L-PARTIAL | A fix that removes the mechanism you found can leave the loss intact, and only a census that names SITES can tell you so. Build the instrument before the repair. | GitHubUpdate47 | none |
| L-FASTER | A component that silently stops doing the work makes a timing probe report a BETTER number, so the reward gradient points straight at the defect. Before believing a speedup, check that the work still HAPPENED. | the IDE write census, blu 2026-08-20; OperatorsManual | the IDE CENSUS refusal counters |
| L-BOTHARMS | A diff finds only asymmetries, so a defect present in BOTH arms is invisible to it. Measure the artifact against its CONTRACT, not against another instance of itself. | BROWSER-5, `codex/test/apps/browser-pane-fit` | that test's `tree past the granted pane` line |
| L-SHORT | A truncated artifact and a wrong artifact are the same colour on a verdict line, so compare LENGTH before believing a red is codegen -- and a STRICT PREFIX, not a bare length difference, is what a cut-off capture looks like. | GitHubUpdate48, the Update 48 poison run | yes: `test-run.ps1`, `test.ps1`, `bvt.ps1`, and `check-compile-drop.ps1` for the compile path |
| L-REHEARSE | Fly nothing that has not completed its FULL MISSION in the bed as the exact bytes being flashed. Boot-and-read green is not mission green. | TheBedThatAlwaysSaidYes | yes: `flash-usb.ps1` refuses an image in no rehearsal record, by default |

| L-ADJECTIVE | When an argument is about which word describes a boundary, stop and sweep the boundary; an adjective standing in for a number is unfalsifiable and survives review. The same failure runs the other way: **a number standing in for a structure**, so ask of any count whether it is a number or a shape you have flattened. | `build/boot/diag-arm.ps1` header, reek 18060; `plugs-backlog.md` 1.46, reek 18122 | none |
| L-BEDTRUE | A default that is a guess about the ENVIRONMENT is not a default, it is a lie with a plausible value. The detection question is not "is this default arbitrary" but "is this default TRUE ONLY IN THE BED"; refuse rather than guess. | `build/boot/diag/DiagB3.codex`, "`ip=` IS REQUIRED" | `b3-noaddr` arm |

| L-CONSTRUCT | A corpus that builds its inputs one way never exercises the other, and no amount of running it will say so: a sabotage that changes no colour is the corpus saying it cannot reach the branch. | `codex/test/ops/real-approx-negate`, fester 18612/18629; reek 2026-08-31; val 21612, GAME-55 | partial: grades x86-64 and the cross battery, NOT the wasm plug -- a runner entry must name the backends it grades |

| L-NOGATE | A test that is in no lane's gate goes red at head and stays red until a release finds it, and a static instrument cannot observe a runtime defect. Ask not "is it green" but "could this instrument observe the failure I am risking". | GitHubUpdate49; val 2026-08-27 `codex/test/desk-parse`; blu 2026-08-28 COMPILER-32 | none (candidate: the gate RUNS, not merely compiles, the `codex/test` chapters citing changed source) |

| L-ACCEPTED | An interface that ignores what it does not recognise makes a setting that does nothing look exactly like one that works, and every caller inherits the lie. Enumerate what the callee actually accepts rather than what its help says. | `plugs-backlog.md` 1.41, reek 19149; OperatorsManual CLI flag table; two more 2026-09-08 (fester), `sweep-apps.ps1` accepting `-Jobs` and using it nowhere (main 23944, deleted) and DISK mode reading a path off the wire and compiling the constant anyway (fester 23967) | yes, both halves: codex-vm refuses the first unrecognised argument (reek), and `compile-plain` refuses an unknown mode (fester, main 20534) |

| L-PEROBJECT | A per-object cost is not a cost until you multiply it by the count, and R-COST's inspection checklist does not ask for the count. Ask of any data-layout change: how small is this object, and how many exist? | COMPILER-18, blu 2026-08-25; `codex/compiler/compiler-backlog.md` | none (candidate: a review question, "what is this object's size BEFORE the change, and its allocation count on the largest program we have?") |

| L-MECHANISM | A mechanism that explains a symptom is not its cause until the fix MOVES the symptom: name the line your mechanism runs through, and grep it, after reading every number the failure already handed you. | `docs/Test/Done/PLUG-CRASH-INVESTIGATION.md`; fester 19506/19524, 19701/19710; `plugs-backlog.md` 1.68-1.69 | none |

| L-AXIS | A classification is not a prediction about failures it does not name, and the one that breaks you is usually on another axis. When you widen a set, the control must be an element of the class you expect to FAIL, not the nearest one to hand. | `plugs-backlog.md` 1.73, reek main 19686 and 21557 | none |

| L-BAILVALUE | A guard that ANSWERS instead of refusing ships a wrong number with no diagnostic, and every consumer downstream inherits it as data. Ask of every bail, cap, fuel exhaustion and `is otherwise` fallback: does this path produce a VALUE? | `emit-wat-expr-at` in `codex/plugs/wasm/WasmEmitter.codex`; `plugs-backlog.md` | none (candidate: a review question, "what does this bail RETURN, and can a caller tell it fired?") |

| L-UNHEARD | A detector whose output goes to a channel nobody reads is not a detector, and the code's own comment will tell you somebody reads it. Ask of every warning and counter: who consumes this, and is that a program or a hope? | ExaminersAssay "The batch stream can lose bytes"; red 20450, fester 2026-08-28 and 2026-09-07 | partial: `check-compile-drop.ps1` grades ONE consumer (`compile.ps1` on its success path); no runner asks the question of any other diagnostic |

| L-REQUEST | A number that names a REQUEST reads as a CONSUMPTION the moment it leaves its sentence, and the two claims are spelled identically. Ask of any resource figure: is this what was ASKED FOR, or what was USED? | OperatorsManual `-Jobs 4` section, 20596; the 2026-08-28 investigation close | none (candidate: a review question, "request or consumption?") |

| L-DENOM | When a harness CHOOSES its subjects and reports "N of N", the denominator is the cap and not the corpus. Ask the SELECTOR for its set, never the directory, and ask what N is a fraction OF. | `plugs-backlog.md`, the wasm parity census, reek 2026-08-31; `hosted-elf-test.ps1` | none (candidate: a harness prints the size of the set it SELECTED FROM beside its score) |

| L-VACUOUS | An arm that agrees with a hypothesis AND its negation measures nothing, and it reads exactly like a falsification. Before reading an arm's colour, measure that it REACHED the condition. | `plugs-backlog.md` 2.17, reek 2026-09-01, main 21038 | none (candidate: a review question, "what would make this arm FAIL, and did you observe it reaching that condition?") |
| L-CENSUS | A census finds the spelling you searched for, and the defect it exists to find is the same shape spelled another way. Census by SHAPE and by BEHAVIOUR before trusting a census by constant, and treat a clean grep as a statement about the grep. | COMPILER-36 in `codex/compiler/compiler-backlog.md`, red 2026-09-02 | partial, and the candidate is RESOLVED: add/sub landed, so the shipping seed IS the trapping kernel and any wrap-by-design site an EXECUTED test reaches now fails on `!EXC=06`. It grades nothing nothing runs, which is the shape of all four recorded misses (Hamt's djb2, the evidence plug's u64 assembler, the classic-games LCG, oracle-vector's ballast): app sites are compile-checked by the gate and executed only by the battery, so a trap left there is a release finding. A runner that closes the rest must RUN the app corpus, not compile it |
| L-RENAMED | A reclassification can satisfy its own rule's WORDS while leaving the subject in the same state under another name, because the category you moved it INTO is itself a member of sets the rule never mentioned. Before believing a thing has moved, ask what other sets its new home belongs to and re-run the instrument that was supposed to change colour. | red's compiler-corpus ruling and `$softwareOnly` in `check-battery-coverage.ps1`, fester 2026-09-07: `.covers compiler` on all 13 subjects would have moved them from "in no battery" to "in the review queue", queue 0 to 13, with the ruling read as implemented | none (candidate: a review question, "which OTHER sets does the new category belong to, and did the number that was supposed to move actually move?") |
| L-ALIAS | **A list or record builtin that allocates nothing writes through the CALLER'S pointer, so a shared default written under one name changes every name holding it.** `list-push` (extend in place), `list-set-at` (bounds check, address, store, return the same pointer) and `__record-set` all store into the argument and hand the same value back, therefore a constant like `default-regions` is not a constant once anything appends to a copy of it. Build lists by concatenation in code that hands a structure back to a caller, and give every scenario in an arm a structure of the scenario's own. | Three bites in the edge-mesh arc alone, 2026-09-08 (blu): the design's own `GroupState` constraint; stage 3's wallet list, where the tell would have been a doubled balance; and `region-add` writing a discovered region into the shared `default-regions`, where a scenario making NO claim at all reported seven regions. **The worst instance is arithmetic ON the aliases rather than a shared default** (blu, 2026-09-08, `dtls-ep-server-emit-msgs`): a flight accumulated by `list-push` gave every output name the same list, so `list-length o1 - list-length o0` was always 0, and Certificate, CertificateVerify and Finished all shipped at record sequence 0 -- one AES-GCM key, one nonce, three plaintexts. A length difference between two accumulator names is not a count of what was added to it. | the arm that flies the SAME inputs in both orders, a scenario that makes no change at all and asserts the untouched count, and a CENSUS of the exact numbers a sequence is supposed to produce (`app hs-seq` in `dtls-app-loopback`) rather than a verdict about them |
| L-ROWROT | **A register row rots three ways and only the first is the one anybody looks for: CLOSED AND NEVER DELETED, TRUE BUT POINTING AT A MOVED LINE, and POINTING AT A ROW THAT NO LONGER EXISTS.** The second is the dangerous one, because a reader who checks a drifted citation finds unrelated code and cannot tell a moved line from a fixed defect; the third is worse still, because "the row it names was deleted as closed" and "the follow-up was never homed" look identical from the register. Verify a row against the TREE and the REMOTE before taking it, and before deleting one ask what it carries that lives nowhere else. | Seven audit slices over `codex/compiler/compiler-backlog.md`, red 2026-09-08 (main 23749, 23757, 23763, 23784, 23786, 23788, 23790): 18 rows carrying closure language verified at head, 6 deleted, 1 rewritten 9,044 characters to 1,964, 7 citations corrected. Kind 1: COMPILER-51 carried its own CLOSED verdict, and COMPILER-44's stated fix AND stated acceptance were both discharged by the auditor's own CL earlier the same session, unnoticed at the time. Kind 2: four rows, drifting 1073 to 1805, 1119 to 1406 (which had also CHANGED SHAPE), 1724 to 1776, 1000 to 1028. Kind 3: COMPILER-38 pointed at `plugs-backlog 2.18`, which does not exist; only reading the wasm emitter settled which reading was right. COMPILER-40's Perforce bisect trap lived in no other document and was rehomed as P-BISECTSEED rather than deleted with its row. | none (candidate: extend `check-backlog-ids.ps1`, which already refuses duplicate row ids, to resolve every `file:line` a row cites and fail when the named symbol is not within a few lines of it) |
| L-UNCITABLE | **A chapter that declares `opening` can be cited by NOTHING** (CDX3001, one entry point per program), so a module keeping its entry point beside definitions other modules need makes those modules uncompilable BY CONSTRUCTION rather than by omission. The symptom names the entry point, not the dependency, so it reads as a build mistake; the repair is a thin entry chapter over the citable rest. Ask of any chapter nothing can compile: does it reach an `opening` through its cites? | `apps/data/DbBoot.codex` split to `DbBootMain.codex`, fester 2026-09-08 (main 23877): `Dashboard` and `DbAdmin` cite `DbBoot`, so nothing had ever compiled either, and 7 of that application's chapters were red the first time anything did. The same rule decided `Build.md` Phase B: the on-disk runner cannot call the compiler while `opening.codex` carries the entry, and root ruled the split on 2026-09-08. | none (candidate: the app sweep already computes each entry chapter's cite closure, so it can report a chapter NO entry reaches) |
| L-AMORTISED | **A cost measured on ONE invocation contains that invocation's fixed setup, so multiplying it by a corpus prices the setup N times and can be wrong by orders of magnitude.** The pair to [[L-PEROBJECT]], which says a per-object cost is not a cost until you multiply it by the count: this says the multiplication is invalid when the sample is a whole invocation and the real run amortises. Before quoting a fan-out price, ask what the single sample paid ONCE that the batch pays once in total, and prefer one measured batch to any arithmetic over one sample. | blu, 2026-09-08: one `test-cross.ps1 -Arch arm64` run measured 66 s, so 668 subjects was reported to root as ~12 hours per pass and ~24 for the pair, and root's cheap compile-only experiment was talked out of on that number. The batch then compiled **555 subjects in 449 s**, about 0.8 s each, because the plug and kernel setup a single run pays is paid once per batch. The estimate was ~100x high and it changed a decision. | none (candidate: a review question, "is this price a measured batch, or one sample times N?") |
## How to use it

- Cite the id when a lesson is load-bearing in a CL, a review, or a design.
- Read the story when you need the reasoning, which is rarely, and which is
  exactly when its length is worth paying.
- When a session produces a new lesson, add a row here first. The story can
  follow; a row with no story is a pointer, and a story with no row is what
  `Done/` already proved does not get read.
- When a lesson becomes mechanically checkable, write the runner and change
  the last column. That is the only column that means anything.

## What this does not do

It does not replace the stories and it must not be allowed to. The row says
what the lesson is; it cannot say what it cost, and the cost is the part that
makes anyone believe it. `BrotliBeatsOpus` is one row here and forty
changelists of genuinely good work in the file.
