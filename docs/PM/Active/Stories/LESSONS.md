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
| L-BODY | The most expensive step in the plan must not be protected by the weakest guard in the tree. If spending a human is gated only by prose, it is not gated. | ProseHasNoRunner, TheStickDidNotBoot, TheSecondStick, TheImageThatWasTwoDaysOld (all in `docs/Stories/`) | none |
| L-ASSUME | Naming an assumption is not declining to build on it. Asked to explain one failure, four agents wrote four papers and three had to open with a correction saying they had structured a case around an event they never established. Settle which event you are explaining before you explain it. | the four `docs/Stories/` papers, 2026-07-29 | none |
| L-MISROUTE | A run sheet that pre-assigns the cause of a tool's failure is worse than one that says nothing: when the tool is the thing at fault, nothing in the procedure can contradict it. "If it reports a verify failure, the stick is the problem" sent a human to swap good hardware. | TheSecondStick | none |
| L-INTERRUPT | A standing obligation in a document loses to an interrupt fired at the moment of relevance. The outbox hook fired twice in an hour and caught a live correction; a run sheet's warning block cannot fire at all. | ProseHasNoRunner | the merge-down workplan hook |
| L-ARTIFACT | Gate the artifact you ship, not a sibling built from the same source. Payload bytes identical is not image identical, and the failure modes live in the vehicle. | TheStickDidNotBoot | none |
| L-SHARED | A fleet tool with a fixed scratch path or a fixed port silently reports another agent's run as yours. Derive both from the workspace, and refuse to start rather than report someone else's screen. | fester, main 12056 | test-ovmf.ps1 refuses a held port |
| L-SUCCESS | Check what SUCCESS looks like before concluding a call failed. `uefi-read-key-ex` shifted a shift-state word carrying bit 31 up by 32, so every successful key read came back with bit 63 set -- negative, indistinguishable from an error to every caller that tested `< 0`. Two months of hypotheses about a keyboard, spent on a function that was returning the right answer in a form nobody could read. | TheKeyboardWasNeverSilent | none |
| L-OPTIONAL | A green emulator arm proves nothing about hardware when the thing under test is an OPTIONAL part of a spec. `uefi-read-key-ex` located `EFI_SIMPLE_TEXT_INPUT_EX` (UEFI 2.x, optional): two arms green under OVMF, `-1` forever on AMI Aptio V. The bed was not less faithful, it was MORE CAPABLE than the target. Prefer the mandatory protocol; when you cannot, the emulator cannot answer the question. | TheKeyboardWasNeverSilent | none |
| L-STATES | Give a probe distinguishable FAILURE states, not pass/fail. One metal boot of a six-colour probe eliminated three whole classes at once -- SystemTable live, firmware calls working, fault confined to one lookup -- because "not yellow" and "not magenta" each carried information. A pass/fail probe would have reported only "still broken", and the next step would have been another guess. | TheKeyboardWasNeverSilent | none |
| L-PUBLISHED | A published value and a consumed one are different claims. The Option A stub really does read `PixelFormat` and publish it at `+0x24`, so "that note is stale" was literally true -- but nothing reads it, red/blue order is still assumed everywhere, and folding the report in as written would have deleted a live warning governing a photograph at the board. Grep for the consumer before retiring a warning. | fester outbox, 2026-08-02 | none |
| L-SIDECAR | A test run by hand must reproduce the runner's sidecar handling, or it measures the invocation. `usb-bot` fails as `connect=FAILED` with no `-DiskFile` and reads exactly like a regression; the control failing identically is what caught it. List `<name>.*` first -- the battery passes `.disk`, `.disk2`, `.vmargs`, `.keys` and `.smp` for you. | reek outbox; val's `.flags` case | none |
| L-SABOTAGE | A sabotage that moves FEWER rows than predicted is telling you the code is shaped differently than you wrote down, and it is worth more than one that moves all of them. Setting `r3t-stride` to the visible width moved two of three rows because `r3d-target-at` passes stride as an ARGUMENT and never reads the field back, so the sabotaged field was not on the path being aimed at. | val outbox, 2026-07-31 | none |
| L-DECODE | Behaviour identical, layout restored, symptom gone is the signature of a HOST decode gap, not a codegen defect. WHP hands over only the instruction bytes on the page the exit was taken on; a 68-byte code-size change moved one LAPIC store to the last byte of a page, the startup IPI never issued, no AP started, and it read as a broken SMP scheduler and was bisected as a compiler regression. | codex-vm page-straddle fix, 2026-08-03 | none |
| L-IDLE | A device that enumerates perfectly and delivers nothing may be obeying us. `SET_IDLE` duration 0 overrides HID 1.11 F.3's every-poll guarantee, our driver sent it since it was written, and the firmware never does -- which is why BIOS setup always typed fine and we read that as OUR bug being elsewhere. Sixteen probe versions. Ask what you are telling the device before asking what is wrong with it. | TheKeyboardWasNeverSilent | none |
| L-ERASED | A rule the compiler ENFORCES can be absent from the artifact it emits, so an auditor reading that artifact cannot see the rule at all. `linear T` and `T` produce byte-identical IR text and an effectful function loses its row the moment it takes a parameter, while CDX2061 and CDX2031 both fire on sabotage. Before planning to audit a claim from an artifact, compile the two programs the claim distinguishes and diff the artifact. If they agree, the claim is not in there. | IndependentRechecker.md section 4, val CL 12674 | none |
| L-INSTRUMENT | A test that reads a function to observe A is broken by that function correctly learning to do B. `gop-handoff`'s magic-gate arms read `acpi-boot-rsdp` to see WHICH SOURCE won; giving it a memory-search fallback made it answer the same address whichever source was read, and two arms went red for a change that fixed something. The repair is to split the function and point the arms at the part that still answers their question (`acpi-stub-rsdp`), never to soften the assertion. Before changing a function, grep its callers for TESTS as well as for code: a test is a caller whose question may be narrower than the function's job. | red CL 12901 | none |
| L-UNCALLED | A path nothing calls is a path nothing has tested. `uefi-read-key-ex` was compiled into every binary we ever shipped, invoked by nothing, and broken. `con-in` is read from the SystemTable and consumed by nothing to this day. Grep for callers before trusting that a capability exists. | TheKeyboardWasNeverSilent, ProseHasNoRunner | none |

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
