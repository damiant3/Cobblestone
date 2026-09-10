# Part 06. The diagnostic ladder and the image pipeline as an instrument

*Owner: fester. Part 06 of `docs/PM/Active/Stories/TheLostParadise.md`. The
subject is the instrument rather than the board: every stage and when each
stage was added, every composition error, every instrument gap found after a
flight, the stub and `-EntryStart` history, the rehearsal system and what a
rehearsal can and cannot prove, and the record channel's design with the
serial-line gap on sitting 15.*

**Status: in progress. Sections land as each section verifies (charter rule 7).
Sections 1 through 7 are complete and verified. Part 06 is complete against its charter row. FESTER-G4 was corrected when section 5 measured the image builder at head.**

## How to read the two labels

Each section carries "What the record says", which is fact with a citation on
every claim, and then "What the record means", which is interpretation.
Findings are numbered `FESTER-G<n>` in part 06, to keep the numbering distinct
from part 05's `FESTER-F<n>`. Every date is absolute and is the box's local
time. Charter rule 6 binds every sentence written here, and material inside
quotation marks is quoted verbatim from the source the citation names.

---

## 1. The ladder, and the configuration channel that failed in both directions

The ladder is the project's answer to a scarce human: one boot carries every
question that is ready, each question a stage, each stage reporting on every
channel alive. The design is sound and the design was reached after five
months. The failures that remain are not in the stages: the failures are in
the composition, which is to say in what the composer told the stages to do.

### 1.1 What the record says

**G-1.1.** Each diagnostic is a standalone Option A GOP payload that paints
findings to the framebuffer and sits, never returning, therefore a photograph
off the glass is always available as a channel
(`build/boot/diag/README.md:1-7`).

**G-1.2.** Damian ruled on 2026-08-18 that a metal question is a STAGE and not a
flight: sittings are grouped, one diagnostic boot per sitting carries every
question that is ready, composed by red. A lane with a question writes a stage
carrying its readings, its failure states, its bed arm and its expected values,
and routes one line to red; nobody builds a one-question image and nobody
proposes a flight (`HardwareSitting.md:56-70`;
`build/boot/diag/README.md:22-28`).

**G-1.3.** The same ruling makes the camera rig a standing instrument rather than
scaffolding, on the stated ground that the bank had regressed once after being
proven, and requires every diagnostic to report on every channel that is alive,
naming colour, rows, QR, bank and serial (`HardwareSitting.md:60-70`).

**G-1.4.** Every Option A image carries two liveness marks painted by the stub in
`cdx-to-pe.ps1` at the same two points the deleted `option_a_stub.asm` painted
them, and the equality was checked in source on 2026-08-02 rather than assumed
from the migration. Solid dark blue means GOP is acquired; solid dark green
means ExitBootServices and our own page tables are live; an unchanged firmware
screen means the payload was never loaded or `LocateProtocol(GOP)` failed
(`build/boot/diag/README.md:36-46`).

**G-1.5.** Without the two marks every one of those states is a black screen,
because every failure path in the stub ends at `fatal`, which is `jmp fatal`
(`build/boot/diag/README.md:44-46`; part 05, F-1.9).

**G-1.6.** On the fifth grouped sitting, flown 2026-08-20, the stick shipped with
no `DIAG.CFG` at all. b3 printed `no-peer  DIAG.CFG names no peer, so nothing
was dialled`, and the sink row read `write-refused ... chunk=64`, the old path
rather than reek's rung ladder (`HardwareSitting.md:1682-1690`).

**G-1.7.** Both stages behaved correctly and said plainly that each had nothing
to do; the composer had given each nothing to do. The sink therefore returned
the same single bit for the FIFTH consecutive sitting, `wr=0 cc=256 ph=2
after=0`, with only the LBA moving as the allocation moves, which the record
names as the precise failure Damian had objected to that morning, reproduced by
the person who had agreed to fix the failure
(`HardwareSitting.md:1686-1693`).

**G-1.8.** The instrument that would have answered was aboard the image and
switched off by omission: reek's ladder of 7 rungs, 1 to 64 sectors, a fixed
64 KB payload, banked before each rung risks the next
(`HardwareSitting.md:1695-1698`).

**G-1.9.** The record states the lesson in its own words: "a stage that reads its
own configuration is only as good as the configuration the composer ships", and
the composer had no check that every new instrument aboard was actually enabled
(`HardwareSitting.md:1698-1700`).

**G-1.10.** The configuration channel failed in the opposite direction on sitting
14, flown 2026-09-07: `mscalign`, stage 10, ran despite `mscalign off` in the
cfg, because of a carriage-return parse defect red found in the cfg reader
(`works-backlog.md`, row WORKS-62).

**G-1.11.** That defect is fixed at main CL 22995, "the CCE carriage-return guard
in `gfat-text` with the `esp-cfg-off` arm that proves it"
(`p4 describe -s 22995`). The arm is live at head and states the expected
reading in full: "xhci skipped from a CRLF DIAG.CFG on the ESP with NO stdin
cfg, scene still rendered, bank=ok" (`build/boot/diag-arm.ps1:925`).

**G-1.12.** A second cfg arm sits beside the first and grades the ordinary path,
`cfg-off`, expecting "scene skipped, bank=ok"
(`build/boot/diag-arm.ps1:924`, with the arm's description at `:171`).

### 1.2 What the record means

**The ladder is the correct design and the ladder arrived on 2026-08-18, five
months into the project.** Before that ruling every metal question was a flight,
therefore every question competed for the scarcest resource on the project
against every other question. Grouping the questions is what let sittings 8
through 14 answer several things per boot. The cost of the late arrival is
countable in flights and belongs to part 03's ledger rather than to part 06.

**The stages were reliable and the composition was not.** In every composition
failure the record holds, the stage did exactly what the stage was told and said
as much plainly on the glass. Sitting 5's b3 printed `no-peer` and the sink printed
`write-refused`; both are correct reports of an empty instruction. A reader of
those two rows learns nothing about the board and everything about the cfg, and
the flight was already spent by the time anybody read them.

**The configuration channel had no runner in either direction, and failed in
both.** A stage that must have run did not, because the composer shipped no
cfg. A stage that must have stayed idle ran, because a carriage return defeated
the parser. The two failures are one absence: nothing compared the cfg the
composer intended against the cfg the stages actually read. The repair for the
second direction is a bed arm, `esp-cfg-off`, which grades exactly that
comparison, and the arm exists because the defect was found. The first direction
gained a runner on 2026-08-21, one day after sitting 5, and the runner is a
refusal in the image builder rather than an arm: section 5, G-5.2.

**The sink's single bit repeating for five consecutive sittings is the clearest
measurable cost of the composition gap in the record.** Four of the five
returned one bit because the instrument did not exist yet. The fifth returned
one bit because the instrument existed, was aboard, and was switched off by
omission, on the morning Damian objected to exactly that repetition. A flight
spent reproducing a known non-answer is the most expensive kind, because the
result is indistinguishable from a board that has not changed.

**The two liveness marks are the one instrument in part 06 built before a
flight needed them rather than after.** The marks were proposed in a post-mortem,
built, confirmed by ablation, and folded into the governing document inside one
afternoon (part 05, F-1.23), and the marks then survived the migration from the
hand-written stub into `cdx-to-pe.ps1` with the equality checked in source rather
than assumed. That check is L-SAMEVER applied before a divergence rather than
after, and the record holds no more than two other instances.

### 1.3 Findings

**FESTER-G1.** Every composition failure in the record is a failure of the
instruction rather than of the instrument, and in each case the stage reported
its own idleness correctly on the glass.
*Supported by:* G-1.6, G-1.7, G-1.10.
*Falsified by:* a stage that misreported a reading the stage did take. The record's
composition failures all show correct reports of an empty instruction (G-1.7).

**FESTER-G2.** Nothing compared the cfg the composer intended against the cfg the
stages read, therefore the configuration channel could fail silently in both
directions, and did.
*Supported by:* G-1.6, G-1.9, G-1.10.
*Falsified by:* a check on the composer's side at either date. The record states
the composer had no such check (G-1.9), and the repair that exists grades the
reader rather than the composer (G-1.11).

**FESTER-G3.** The sink returned one bit on five consecutive sittings, and the
fifth repetition was caused by an instrument that was aboard and disabled by
omission on the morning the repetition was objected to.
*Supported by:* G-1.7, G-1.8.
*Falsified by:* a sink reading richer than `wr=0 cc=256 ph=2 after=0` on any of
the five. The first richer reading is sitting 6 (part 05, F-3.2).

**FESTER-G4.** Both directions of the configuration channel are closed at head, the
reader's half by the `esp-cfg-off` bed arm and the composer's half by a refusal
in the image builder that landed on 2026-08-21, one day after sitting 5.
*Supported by:* G-1.11, G-1.12, G-5.2, G-5.3.
*Falsified by:* an image built with a risky stage unnamed and no override.
`build-diag.ps1` throws unless `-AllowUnnamedStages` is passed (G-5.2).

*(An earlier revision of FESTER-G4, landed at main 25264, stated that the
composer's half had no runner at head. The statement was wrong: the runner is
`build-diag.ps1`'s refusal, measured in section 5. The finding is replaced rather
than annotated, and the error is recorded here because a reader of the earlier
revision would otherwise carry the wrong claim forward.)*

**FESTER-G5.** The liveness marks are the one instrument in part 06 built before
the flight that needed them, and the marks' equality across the stub migration
was checked in source rather than assumed.
*Supported by:* G-1.4, G-1.5.
*Falsified by:* a flight after 2026-07-29 reporting a black screen with no
colour state. The record's later flights all report a colour (G-1.4).

---

## 2. The stub: a panic that did not halt, and a hash pin that moved

The stub is the first code of ours that runs on the board and the last code
that can report before the payload exists. Part 05 established that every stub
failure path ended in a silent spin until 2026-07-29. Section 2 establishes
something worse and later: for a further seventeen days the stub's panic did
not stop the machine at all, and the governing register told every reader the
opposite.

### 2.1 What the record says

**G-2.1.** `AllocPanic` emitted `mov al,c`, `out 0x3F8`, `out 0x2F8` and `hlt`,
with no `cli` and no halt loop (`p4 describe -s 15503`).

**G-2.2.** All five panic sites run BEFORE `ExitBootServices`, where the firmware
timer is live, therefore the bare `hlt` resumes on the next tick and falls
through into code that assumes the allocation succeeded. The five sites are
named: C for code pages, H for heap pages, G for the GDT page, B for a null heap
base, and V for the framebuffer straddling the record arena
(`p4 describe -s 15503`).

**G-2.3.** The behaviour was measured rather than argued, by forcing a refusal
with a 2 GB heap in a 640 MB bed. The output was `s v c H V h g` followed by an
X64 general-protection fault in the firmware's own handler: H fell through, V
then fired on the garbage heap pointer and fell through as well, and the heap
and GDT progress marks printed regardless (`p4 describe -s 15503`).

**G-2.4.** `CurrentPlan` told the reader the stub raises H if the allocation is
refused. The sentence was false at the time of writing and is true after CL
15503, and the changelist says exactly that, citing L-MISROUTE
(`p4 describe -s 15503`).

**G-2.5.** A board refusing the allocation would therefore have shown a crash
rather than a refusal, which is to say the one failure the run sheet's boot-1
table called terminal for the machine would have arrived wearing the wrong
symptom (`p4 describe -s 15503`; part 05, F-1.9 for the table's earlier state).

**G-2.6.** The defect had never been seen because a sibling halt fired on the far
side of the boundary: reek's V halt earlier the same day runs AFTER
`ExitBootServices`, where IF is 0 and the `hlt` sticks, therefore one
instruction is terminal on one side of `ExitBootServices` and transparent on the
other (`p4 describe -s 15503`).

**G-2.7.** The repair is three instructions, `cli`, `hlt`, and a two-byte jump
back to the `hlt`, and the repair is live at head:
`build/cdx-to-pe.ps1:308-310` writes `0xFA`, `0xF4` and `0xEB 0xFD`, with the
comment "a panic must not fall through".

**G-2.8.** The panic block grew from 13 bytes to 16, therefore four displacements
skipping the block moved from +13 to +16 and the outer `jae` moved from +31 to
+34. The change is not size-neutral without dropping the second UART write,
which is a deliberate fallback (`p4 describe -s 15503`).

**G-2.9.** The stated cost is the whole of section 2's second half: "the layout of
every path shifts, INCLUDING -EntryStart, whose bytes red hash-pinned for A5.
All three arms change hash. L-DECODE class, so the next A5 flight carries stub
bytes that have not flown" (`p4 describe -s 15503`).

**G-2.10.** Two earlier attempts at the negative control were invalid and the
account records both rather than dropping them: 512 MB in a 384 MB bed killed OVMF
before the payload ran, and at 640 MB the firmware granted the 512 MB anyway
(`p4 describe -s 15503`).

**G-2.11.** The arms that established the fix ran on the exact bytes: the positive
arm at `-AllocPages 131072` prints `s v c h g x o` before and after, and the
negative arm prints `s v c H` and stops where the stub used to fall through
(`p4 describe -s 15503`).

**G-2.12.** The stub is generated rather than hand-written: the generator was
edited, the script regenerated from the generator, and `check-generated-scripts`
reported match with 0 drift in the same changelist
(`p4 describe -s 15503`, affecting `build/cdx-to-pe.ps1` and
`codex/build/cdxtopeScript.codex`).

**G-2.13.** The other stub change of the same evening, CL 15469 at 21:28, made the
stub pick the largest GOP mode the firmware enumerates, and recorded that both
`-EntryStart` arms were byte-identical to CL 15393, therefore that change did
not move the pin (`p4 describe -s 15469`).

**G-2.14.** The consequence for the fleet's images is measured in part 05: ten of
the thirteen depot images carry a stub predating the evening of 2026-08-15, and
the only cheap discriminator is the image's own content date against
2026-08-15 21:51 (part 05, F-2.17).

### 2.2 What the record means

**The stub's failure reporting was wrong in the direction that costs the most.**
A panic that halts loses the machine and tells the operator one true thing. A
panic that prints a letter and continues tells the operator one true thing and
then produces a second, later, unrelated symptom, and the second symptom is what
gets diagnosed. The measured trace makes the point exactly: after H the stub
printed `V`, then `h`, then `g`, therefore the glass showed progress marks for two
stages that could not have succeeded, and the run ended in a firmware fault
carrying no relation to the allocation.

**The register asserted the correct behaviour, which is worse than asserting
nothing.** A reader of `CurrentPlan` knew that a refused allocation raises H. An
operator seeing no H would have concluded the allocation succeeded. The sentence
was an assertion with no runner, in the same shape as R8 in part 05, and the
sentence survived because nothing had ever forced the path.

**One instruction that is terminal on one side of a boundary and transparent on
the other is the sharpest instrument trap in the whole record.** The stub carried
two halts of the same shape, both written by competent readings of the same
instruction. The difference is a flag the firmware controls. No inspection
distinguishes them and only forcing the path does, which is why the negative arm
matters and why two invalid attempts at the negative arm were worth recording
rather than discarding.

**The repair cost a hash pin, and the record names that cost before anybody asked.** A
three-instruction fix moved the layout of every path, including the path red had
pinned by hash for A5, therefore the next A5 flight would carry stub bytes that
had never flown. The changelist names the consequence, names the owner, and
names the class, and the fleet still flew ten images carrying the older stub for
the rest of the campaign, because a campaign artifact is a record of a flight
rather than a rebuild waiting to happen.

**The stub is generated, and the generator discipline held.** The one place where
a hand edit would have produced silent drift between the shipped script and its
source is checked by a runner in the same changelist. That is the counter-example
to the rest of part 06: an assertion with a runner, evaluated at the moment of
the change, reporting 0 drift.

### 2.3 Findings

**FESTER-G6.** For seventeen days after the liveness marks landed, the stub's
allocation panic printed a letter and continued executing, therefore a refused
allocation presented as a later unrelated fault rather than as a halt.
*Supported by:* G-2.1, G-2.2, G-2.3.
*Falsified by:* a halt observed after a forced refusal before CL 15503. The
measured trace continues through two further stages (G-2.3).

**FESTER-G7.** The fleet's own register stated the opposite of the stub's actual
behaviour, therefore a reader following the register would have read the absence
of H as a successful allocation.
*Supported by:* G-2.4, G-2.5.
*Falsified by:* a runner exercising the panic path before CL 15503. The
changelist establishes the arms as new (G-2.11).

**FESTER-G8.** The defect was invisible to inspection because the identical
instruction is terminal after `ExitBootServices` and transparent before, and
only forcing the path could distinguish the two.
*Supported by:* G-2.6, G-2.10, G-2.11.
*Falsified by:* a reading of the source that separates the two halts. Both are
`hlt` with no `cli` (G-2.1, G-2.6).

**FESTER-G9.** A three-instruction repair invalidated a hash pin and therefore
sent the next A5 flight up on stub bytes that had never flown, and the
changelist named that cost at the moment of the change.
*Supported by:* G-2.8, G-2.9, G-2.13.
*Falsified by:* an `-EntryStart` arm unchanged across CL 15503. All three arms
change hash (G-2.9).

**FESTER-G10.** The generated-script discipline is the one assertion in part 06
that carried a runner at the moment of the change and reported a result.
*Supported by:* G-2.12.
*Falsified by:* drift between `cdx-to-pe.ps1` and `cdxtopeScript.codex` at CL
15503. The check reported 0 drift (G-2.12).

---

## 3. The rehearsal: what fifty green arms prove, and what fifty green arms do not

The rehearsal system is the best instrument the project built. The rehearsal
system caught a regression that would have destroyed the final flight's record,
grades its own controls, and refused an unrehearsed flash by default from
2026-08-20 onward. The last sitting flew on fifty arms of fifty green and
returned nothing. Both halves of that are load-bearing.

### 3.1 What the record says

**G-3.1.** `diag-arm.ps1` boots the EXACT image that would fly, in codex-vm and
under OVMF, and requires every channel to agree
(`build/boot/diag-arm.ps1:1-3`).

**G-3.2.** Every arm is a control as much as a check, and the arms that must reach
`bank=none` are what show the bank rows can say no
(`build/boot/diag-arm.ps1:4-6`).

**G-3.3.** The harness carries 50 arms at head, counted from the arm table rather
than quoted (`build/boot/diag-arm.ps1`, 50 arm entries, measured 2026-09-09).

**G-3.4.** Sitting 15 was rehearsed as 50 of 50 arms on the exact flight bytes,
recorded in `build/boot/diag.rehearsed` against sha `47F29D50` on 2026-09-08,
and root signed the flight off after checking that the workspace file hashed to
`47F29D507C715DA8` and equalled the `diag.rehearsed` line, `arms=50`, timestamped
2026-09-08T13:45:47Z (`HardwareSitting.md:937-945`).

**G-3.5.** The paired sink arms are the harness's own statement of why one arm is
not enough: `sink-ladder-32` drives the bed to refuse exactly the 32-sector
command and requires `rung=32 done=5`, and `sink-ladder-16` moves the bed
threshold to 8192 and requires `rung=16 done=4`. The harness states the reason
in its own words: "One arm cannot tell a ladder that measures from a ladder that
always stops in the same place; two arms whose answers MOVE with the bed can"
(`build/boot/diag-arm.ps1:33-41`).

**G-3.6.** Both arms assert the exact rung rather than the presence of a rung,
because an earlier version asserted only that a rung appeared and passed on an
ordinal-keyed drop that was stopping rung 1 for an unrelated reason, measured
2026-08-20 (`build/boot/diag-arm.ps1:41-45`).

**G-3.7.** `-usb-bot-drop N` counts transfer events since boot, therefore the
lever is a property of the whole run rather than of the thing under test.
Inserting the `xhci` stage at position 8 moved the drop out of the sink's DATA
phase into the sink's MOUNT, and the arm reported `mount-fail`, which the
harness names as the danger: "a plausible word, not a nonsense one, which is
exactly what makes it dangerous" (`build/boot/diag-arm.ps1:47-53`).

**G-3.8.** The replacement lever keys on the command's own
`dCBWDataTransferLength` and cannot be moved by anything upstream, which is why
the ladder arms use the replacement (`build/boot/diag-arm.ps1:53-55`).

**G-3.9.** The harness records that the arm's INTENT is "the drop lands in the
sink's data phase", that the number is only how the intent is currently
expressed, and that the intent rather than the number must survive a stage being
added. The re-derivation method is written out: sweep `-usb-bot-drop N` and read
`stage=sink state=` for each N, where `write-refused` with `wr=0` is the data
phase, below is the mount and above is the readback. Measured 2026-08-20: 500,
520, 560, 620, 700 and 740 refuse the write, 770 and 800 read back, and the
instruction is to take the middle (`build/boot/diag-arm.ps1:55-62`).

**G-3.10.** An arm was retired for ceasing to test what the arm was written for:
`-usb-bot-drop 1` was the no-bank arm until the MSC driver's recovery path
re-issued the transfer and the bank landed, measured 2026-08-18
(`build/boot/diag-arm.ps1:19-22`).

**G-3.11.** The rehearsal caught the flush regression that would have cost the
last sitting the entire record, as 43 of 47 arms reaching `bank=none write
refused` on one signature (part 05, F-3.7 and F-3.8).

**G-3.12.** Sitting 15 flew on 2026-09-09 with all 50 arms green, stopped inside
the ladder's first write to the part, and every metal question aboard received
the answer "not reached" (`TheLostParadise.md`, "The event under investigation").

**G-3.13.** Two of sitting 15's stages had never been read on metal before the
flight, `lease` for NIC-6 and `rtcw` for WORKS-24, and the pre-flight card states
the fact plainly rather than implying coverage (`HardwareSitting.md:958-965`).

### 3.2 What the record means

**The rehearsal harness is the one instrument in the whole report that consistently
grades its own ability to fail.** The arms that must reach `bank=none` exist to
show the bank can say no. The sink arms come in pairs because one arm cannot
distinguish a measuring ladder from a stuck one. The lever was changed when the
old lever proved to be a property of the run rather than of the subject. Each of
those three is L-FALSIF applied before a failure rather than after, and the
harness is where the project's hardest-won lessons actually became executable.

**The most valuable thing the harness produced was a red, and the red came from
the bed's limitation rather than the bed's fidelity.** The first flush version
was refused by a BOT model that did not implement the optional opcode, and the
refusal is what exposed a change that would have banked nothing on most real
devices. A more faithful bed would have answered GOOD and passed the regression
through to metal, where the cost would have been the last record the project
will ever take.

**A plausible failure word is more dangerous than a nonsense one, and the harness
is a hazard the harness alone names.** The ordinal drop moved when a
stage was inserted, and the arm reported `mount-fail`, a real state, reachable,
correctly spelled and completely wrong about what was being tested. Every other
instrument gap in the whole report was found after a flight. That one was found in
the bed, written down, and the fix keyed the lever to the subject.

**Fifty green arms did not prevent the last flight from returning nothing, and
that is not a failure of the rehearsal.** A rehearsal proves that the image the
project built behaves as the project intends against the devices the project
models. A rehearsal cannot prove the board will reach the code being rehearsed.
Sitting 15 stopped inside the first write to the part, upstream of every question
aboard, therefore the rehearsal's fifty arms were about code the board never
ran. The correct reading is that rehearsal removes one class of loss, the class
where a flight fails for a reason the dev box already knew, and removes no
others.

**The pre-flight card was honest about the two stages that had never flown.** A
composition carrying an unflown stage is not a defect, and stating the fact on
the card is what lets an absent reading be attributed later. Both stages returned
"not reached", therefore the honesty cost nothing on sitting 15 and would have
been what separated a stage defect from a board answer on any flight that
reached them.

### 3.3 Findings

**FESTER-G11.** The rehearsal harness grades its own ability to fail, in three
distinct ways, and is the only instrument in the whole report that does.
*Supported by:* G-3.2, G-3.5, G-3.10.
*Falsified by:* an arm asserting a positive with no paired control. The bank
arms, the sink arms and the retired drop arm all carry one (G-3.2, G-3.5,
G-3.10).

**FESTER-G12.** The bed's LIMITATION rather than the bed's fidelity is what caught
the regression that would have destroyed the last flight's record.
*Supported by:* G-3.11.
*Falsified by:* a bed that implements the optional opcode catching the same
regression. A conforming device answers GOOD and the bank would have been
refused only on metal (part 05, F-3.7).

**FESTER-G13.** A test lever keyed to a property of the whole run rather than to
the subject produced a plausible, wrong verdict when an unrelated stage was
inserted, and the harness names the plausibility as the hazard.
*Supported by:* G-3.7, G-3.8, G-3.9.
*Falsified by:* a nonsense reading from the moved drop. The arm reported
`mount-fail`, a real and reachable state (G-3.7).

**FESTER-G14.** Fifty arms of fifty passed on the exact flight bytes and the
flight returned no answer to any question aboard, because the board stopped
upstream of everything the arms graded.
*Supported by:* G-3.4, G-3.12.
*Falsified by:* a sitting-15 reading from any stage. Every question aboard reads
"not reached" (G-3.12).

**FESTER-G15.** The rehearsal record was checked by hash against the workspace
file before sign-off rather than taken on the builder's word, therefore the bytes
rehearsed and the bytes signed off are established as the same bytes.
*Supported by:* G-3.4.
*Falsified by:* a sign-off naming no hash. The card names
`47F29D507C715DA8` and the `diag.rehearsed` line (G-3.4).

---

## 4. The record channel, and the serial line that was not in front of the channel

The record channel is the project's answer to the medium losing the bank: ship
every banked line to the dev box live, therefore a stick that dies keeps nothing and
the dev box holds everything. The channel was designed carefully, priced
honestly, given a give-up budget, and placed first in the ladder by ruling. On
the only flight the channel ever took, the channel never printed a line, and the
reason the fleet cannot say why is one missing serial line.

### 4.1 What the record says

**G-4.1.** The channel opens after the passive stages and before the first bank
write, ships the passive record, and then ships every bank line live over a
fresh connection per step, therefore a stick that dies keeps nothing and the dev
box holds every banked line (`HardwareSitting.md:947-955`).

**G-4.2.** A peer that never answers must not cost the record, and the design says
as much in the source: each bank step dials a fresh connection, therefore a down peer
would spend one connect budget per step and the ladder could miss END, which
loses the whole record on metal exactly as a stick that dies does. After
`drec-give-up` failed ships the channel stops dialling, the summary reads
`record=refused`, the banked lines already on the medium stand, and every later
step is a no-op, therefore the ladder reaches END
(`build/boot/diag/DiagRecord.codex:50-60`).

**G-4.3.** The give-up budget is three (`build/boot/diag/DiagRecord.codex:60`).

**G-4.4.** The channel's cost is stated rather than hidden: `transport-new`
reserves a 32 MB receive buffer per transport, which is what a fresh connection
per bank step costs the ladder's heap, and because nothing reads the peer's
echo, a ship takes a 16 KB cap and a full buffer drops the rest
(`build/boot/diag/DiagRecord.codex:44-48`).

**G-4.5.** The channel's first act is an ARP wait for the hop, and a failure there
answers `record=none no-arp` naming both addresses rather than failing silently
(`build/boot/diag/DiagRecord.codex:327-334`).

**G-4.6.** Sitting 15 was flashed by root with all 16,777,216 bytes and the four
SpecFit sectors verified, the peer was up on `192.168.6.141:7`, and the firewall
rule was widened to every profile before the boot, because the box sat on a
Public profile and the rule allowed Private only, which no earlier flight could
have passed either (`HardwareSitting.md:161-166`).

**G-4.7.** The predicted and actual rows of sitting 15 differ at exactly one
place and everything after: rows 1 to 7 and `nicsit` painted as predicted, with
`nicsit` ending on `poll 1000000 empty=33152us tick100k=3315us hpet-hz=23999999`
against the bed's 13034us and 1303us; the record channel's predicted
`record=peer opened` was never printed and the peer log holds zero connections
with `echo-peer.record` never created; the bank never opened, because the bank
opens after `drec-open`; and `nicinit` onward was never reached
(`HardwareSitting.md:168-174`).

**G-4.8.** The box stopped after `nicsit`'s last line, inside one of three steps
the ladder runs before printing again: `usb-attach`, the ESP select and cfg
read, or `net-driver-bring-up` inside `drec-open`, which is the e1000 reset
where sitting 10 and the first flight also stopped
(`HardwareSitting.md:176-180`).

**G-4.9.** The glass cannot tell the three apart, and the record names the cause:
the record channel's bring-up, which runs first by root's `b3` EARLY ruling,
carried no serial line before the bring-up, where `nicinit` carries one before
every step. The record names the owner and the finality in one sentence: "That
is root's composition error and it is recorded here once; there is no flight to
fix it on" (`HardwareSitting.md:180-184`).

**G-4.10.** Every question aboard sitting 15, `asde`, NIC-4, NIC-6, WORKS-24, the
WORKS-62 flush and the record channel itself, has one metal answer, "not
reached" (`HardwareSitting.md:184-186`).

**G-4.11.** The sitting queue closed on 2026-09-09 at 02:40, and the closure is
absolute: no metal question is admitted, no flight is composed, no image is
flashed to fly, a metal-gated item is answered in a bed or deleted as
unanswerable, and "rides the last sitting" is no longer a state any register may
carry (`HardwareSitting.md:151-159`).

### 4.2 What the record means

**The record channel was designed against the right failure and reached the
board too late to answer.** Nine flights lost the bank to the medium. The channel
removes the medium from the record path entirely, prices its own heap cost,
refuses to let a dead peer cost the END, and reports a failed ARP by naming both
addresses. Every one of those is the shape the project spent five months
learning. The channel took one flight and returned nothing, because the board
stopped before the channel opened.

**One missing serial line is the difference between a closed question and an
open one, permanently.** Three candidate steps sit between `nicsit`'s last line
and the next print. `nicinit` prints before every step and would have named
which. `drec-open` printed nothing before its bring-up, therefore the flight's
final reading is "the box stopped somewhere in three places", and no flight
remains to narrow the three. The instrument gap is not subtle and is not novel:
the gap is the same gap the stub had on 2026-07-29, which part 05 records, and
which was closed for the stub inside one afternoon.

**Placing the channel first was correct and cost the attribution.** The channel
must open before the first bank write, because the channel exists to carry lines
a dying medium loses. Running first also means running before any stage has
printed a step marker, therefore the earliest code is the code with the least
instrumentation in front of the code. The ordering and the instrumentation are
independent choices, and the ruling made the first without the second.

**The one number the last flight did return is a real measurement and belongs
beside the bed's.** The board's empty poll is 33152us against the bed's 13034us,
a factor of 2.5, with HPET at 24 MHz. That reading is the whole scientific yield
of the final sitting, and the reading is about the divergence between the bed
and the board, which is L-ARENA measured on the way past.

**The firewall detail is worth keeping because the detail invalidates earlier
absences.** The box sat on a Public profile with a rule allowing Private only,
and root widened the rule before the boot. No earlier flight could have passed
that rule either, therefore every earlier "the peer heard nothing" reading has a
second candidate explanation that has never been separated out, and no flight
remains in which to separate the two.

### 4.3 Findings

**FESTER-G16.** The record channel removes the medium from the record path,
prices its own cost, and refuses to let a dead peer cost the END, and the channel
returned nothing on its only flight because the board stopped upstream of the
channel's first line.
*Supported by:* G-4.1, G-4.2, G-4.4, G-4.7.
*Falsified by:* any line shipped to the peer on sitting 15. The peer log holds
zero connections (G-4.7).

**FESTER-G17.** The final flight's stop cannot be attributed to one of three
candidate steps, because the step that ran first printed nothing before running,
and no flight remains in which to add the line.
*Supported by:* G-4.8, G-4.9, G-4.11.
*Falsified by:* a serial line or a painted row between `nicsit`'s last line and
`drec-open`'s bring-up. The record states none exists (G-4.9).

**FESTER-G18.** Running the channel first is required by the channel's purpose and
placed the least instrumented code at the point of highest risk, and the two
choices were never separated.
*Supported by:* G-4.1, G-4.9.
*Falsified by:* a step marker in `drec-open` before `net-driver-bring-up`. The
record names the absence as the composition error (G-4.9).

**FESTER-G19.** The last sitting's only scientific yield is a bed-against-board
divergence: the board's empty poll is 2.5 times the bed's, with HPET at 24 MHz.
*Supported by:* G-4.7.
*Falsified by:* a second reading from sitting 15. Every other row reads "not
reached" (G-4.10).

**FESTER-G20.** A firewall rule was widened immediately before the last flight,
and no flight card in the record names the profile, therefore an earlier reading
of "the peer heard nothing" carries a second explanation that the record cannot
separate out.
*Supported by:* G-4.6, G-4.11, G-4.12.
*Falsified by:* a flight card naming the active profile. No card does (G-4.12).

**G-4.12.** THREE earlier flights recorded a connection from the ASUS: sitting
11 on 2026-08-21 (`A-sittings-table.md`, attempt 49, "CONNECTION 28 from
192.168.6.200:49157"), sitting 12 on 2026-08-24 (attempt 50, "`b3 ok
pe=192.168.6.141:7 sent=13/13 rx=13 lk=1` with the peer log agreeing"), and
sitting 13 on 2026-09-07 (attempt 52, b3 "recorded ONLY in the peer log"). The
last of the three connected two days before the Public profile was read on
2026-09-09.

*(FESTER-G20 has been replaced twice, and both earlier counts came from one
defective extraction. The revision landed at main 25279 said sitting 11 was the
only such flight; the revision landed at main 25292 said two. The count is
three, and the extraction defect is recorded in the coverage note for section 4
in order that no later reader repeats the defect.)*

---

## 5. The image builder, which is where the lessons became refusals

Sections 1 through 4 are largely a record of correct instructions that nothing
executed. Section 5 is the counter-case: `build/boot/build-diag.ps1` is where
the ladder's hardest lessons stopped being prose and became a program that
refuses to build. Every refusal in the builder can be dated to the flight that
bought the refusal.

### 5.1 What the record says

**G-5.1.** The builder compiles `Diag.codex` with the depot seed by default,
because "an image that goes near a stick must have provenance", and another
kernel is passed only for a dev loop
(`build/boot/build-diag.ps1:23-27`).

**G-5.2.** The builder REFUSES to build an image whose cfg leaves a risky stage
unnamed, landed at main CL 18574 on 2026-08-21, "build refuses a cfg leaving a
risky stage unnamed; checked-in default cfg"
(`p4 describe -s 18574`; `build/boot/build-diag.ps1:148`).

**G-5.3.** The builder derives the stage list from `Diag.codex` itself rather
than duplicating the list: the builder parses `dg-stage-name`, `dg-stage-count`
and `dg-stage-risk` out of the source, throws when any of the three fails to
parse, and states the reason for asking rather than evaluating: "`dg-stage-risk`
is one nested if. Evaluating it here would be a second implementation of it;
asking which stages it calls passive is not"
(`build/boot/build-diag.ps1:115-133`).

**G-5.4.** The override for G-5.2 is `-AllowUnnamedStages`, and the parameter's
own comment states the reason the override is narrow: "an override the routine
path takes stops being a signal, which is how `flash-usb.ps1 -Rehearsed` came to
certify nothing for a week" (`build/boot/build-diag.ps1:38-42`).

**G-5.5.** The builder refuses a config key named more than once and gives that
refusal NO override, on the stated ground that "unlike an unnamed stage a
duplicated key has no legitimate use: one stage's options go on one line". The
refusal explains the runtime behaviour the refusal protects against, that the box reads
the FIRST line for a key and drops the rest, and names the stub ring as a second
source of a duplicate (`build/boot/build-diag.ps1:92-111`; landed at main CL
18645 on 2026-08-21).

**G-5.6.** The payload's identity travels twice, into the stub's serial ring where
the payload reads `id <hex>` and onto the ESP as `DIAG.ID`, and a stick whose
`DIAG.ID` does not match the payload that booted is refused, therefore a stale
stick from an older image cannot be written to by mistake
(`build/boot/build-diag.ps1:11-16`).

**G-5.7.** The recipe `DIAG.RCP` stamps `id`, `kernel`, `payload-sha256`,
`bundled-sha256`, `efi-sha256`, `alloc-pages`, `total-sectors`, `stdin`, `cfg`
and `diag-src-cl` (`build/boot/build-diag.ps1:224-240`).

**G-5.8.** The staleness refusal is keyed on the BUNDLE digest rather than on the
payload's own directory, and the source says why: the bundle is "Diag.codex plus
the transitive cite closure `bundle-app` gathers from across `codex/`", because
"a stamp over `build/boot/diag` alone cannot see any of those chapters move".
The guard landed at main CL 24252 on 2026-09-08
(`build/boot/build-diag.ps1:229-232`; `p4 describe -s 24252`).

### 5.2 What the record means

**The builder is where the project's lessons became executable, and the dates
show the lessons being bought one flight at a time.** Sitting 5 flew on
2026-08-20 with no cfg and wasted a flight. The refusal that makes that
impossible landed on 2026-08-21. The duplicate-key refusal landed the same day,
from a different defect on the same flight. The staleness guard keyed to the
cite closure landed on 2026-09-08, one day before the end. Each refusal names
the failure that bought the refusal.

**The builder does not duplicate the thing the builder checks, and that is the difference
between a guard and a second implementation.** The stage table, the stage count
and the risk classification are parsed out of `Diag.codex` at build time, and the
builder throws rather than guessing when the parse fails. A builder carrying its
own copy of the stage list would have drifted the first time a stage was
inserted, which is exactly what moved the ordinal drop in section 3.

**The narrow override is the one place in the record where a previous failure
was cited in the design of its successor.** `-AllowUnnamedStages` exists, and the
parameter's comment says why the switch must stay off the routine path, naming
`flash-usb.ps1 -Rehearsed` certifying nothing for a week. A rule that carries its
own counter-example is the rule most likely to survive, and the duplicate-key
refusal went further by having no override at all.

**The staleness guard is the L-SAMEVER lesson keyed correctly rather than
plausibly.** A digest over the payload's own directory would answer the wrong
question, because the payload's source reaches across `codex/`. Keying on the
bundle is the difference between a guard that is easy to write and a guard that
can see the change the guard exists to catch.

**Section 5 is also where FESTER-G4's first revision was wrong.** The composer's
half of the configuration channel had a runner from 2026-08-21, and the earlier
revision of that finding, landed at main 25264, said the opposite. The error was
in part 06 rather than in the tree, and the tree is what corrected the error.

### 5.3 Findings

**FESTER-G21.** Every refusal in the image builder can be dated to the flight that
bought the refusal, and three of the four landed within a day of the failure.
*Supported by:* G-5.2, G-5.5, G-5.8.
*Falsified by:* a builder refusal with no antecedent failure in the record. The
three carry CL dates one day after sitting 5 and one day before the end
(G-5.2, G-5.5, G-5.8).

**FESTER-G22.** The builder derives the stage table from the ladder's own source
and throws when the parse fails, therefore the guard cannot drift from the thing
guarded.
*Supported by:* G-5.3.
*Falsified by:* a stage list written out in the builder. The builder parses
`dg-stage-name`, `dg-stage-count` and `dg-stage-risk` (G-5.3).

**FESTER-G23.** The two cfg refusals are graded by consequence rather than by
symmetry: an unnamed stage has a legitimate one-off use and carries a narrow
override, and a duplicated key has none and carries no override.
*Supported by:* G-5.2, G-5.4, G-5.5.
*Falsified by:* an override on the duplicate-key path. The source states none
exists (G-5.5).

**FESTER-G24.** The staleness guard is keyed to the payload's transitive cite
closure rather than to the payload's directory, therefore the guard can observe
the change the guard exists to catch.
*Supported by:* G-5.8.
*Falsified by:* a stamp over `build/boot/diag` alone. The recipe stamps
`bundled-sha256` (G-5.7, G-5.8).

---

## 6. Every stage, when the stage was added, and why

The charter asks part 06 for the stage-by-stage history. The ladder holds 20
stages at head. Fourteen of the 20 landed inside four days of the grouping
ruling, two landed on the day of the last build, and the dates carry the whole
argument about what the ladder was and when the ladder became one.

### 6.1 What the record says

**G-6.1.** The ladder declares 20 stages at head, and the table is parsed from
the source rather than transcribed: `dg-stage-count : Integer = 20`, with
`dg-stage-name` naming 1 `smbios`, 2 `edid`, 3 `cpu`, 4 `pci`, 5 `scene`, 6
`gopmode`, 7 `nicsit`, 8 `block`, 9 `xhci`, 10 `kbd`, 11 `mscalign`, 12 `sink`,
13 `pch`, 14 `nicinit`, 15 `nicring`, 16 `b3`, 17 `lease`, 18 `rtcw`, 19
`pchk1`, 20 `asde` (`build/boot/diag/Diag.codex`, read 2026-09-09).

**G-6.2.** Each stage's chapter carries a first depot revision, and the twenty
resolve to thirteen changelists over five days:

| stage | chapter | first CL | date |
|---|---|---|---|
| `pci`, `scene` | `DiagPci`, `DiagScene` | 16822 | 2026-08-18 |
| `smbios`, `edid`, `cpu` | `DiagSmbios`, `DiagEdid`, `DiagCpu` | 16851 | 2026-08-18 |
| `block` | `DiagBlock` | 17207 | 2026-08-18 |
| `sink` | `DiagSink` | 17210 | 2026-08-18 |
| `nicsit`, `nicinit`, `nicring` | `DiagNicSit`, `DiagNicInit`, `DiagNicRing` | 17225 | 2026-08-18 |
| `b3` | `DiagB3` | 18011 | 2026-08-20 |
| `gopmode`, `xhci` | `DiagGop`, `DiagXhci` | 18048 | 2026-08-20 |
| `asde` | `DiagAsde` | 18104 | 2026-08-20 |
| `pch` | `DiagPch` | 18373 | 2026-08-20 |
| `pchk1` | `DiagPchK1` | 18643 | 2026-08-21 |
| `mscalign` | `DiagMsc` | 22766 | 2026-09-07 |
| `kbd` | `DiagKbd` | 22788 | 2026-09-07 |
| `lease`, `rtcw` | `DiagLease`, `DiagRtcW` | 23532 | 2026-09-08 |

(`p4 filelog` on each chapter, first revision, read 2026-09-09.)

**G-6.3.** The ladder itself was created on 2026-08-18 at CL 16822, "the
diagnostic ladder `build/boot/diag/Diag.codex` + DiagStage/DiagPci/DiagScene",
as step 1 of `DiagnosticStick.md`, the same day as Damian's grouping ruling
(`p4 describe -s 16822`; part 06, G-1.2).

**G-6.4.** Nine stages existed by the end of 2026-08-18, added as the design's
numbered steps: step 3 brought the three passive rows (`p4 describe -s 16851`),
step 2's first lift brought `block` "with the block-oob forced arm; eleven arms
green" (17207), step 2's second brought `sink` "with the sink-shift forced arm;
twelve arms green" (17210), and step 2's third brought the NIC three "with arms
nic-pass/nic-nolink/nic-nomac; 15 arms green" (17225).

**G-6.5.** Every one of those four landings names its arm count in the changelist
description, therefore the arms grew with the stages rather than after them: 11,
12 and 15 arms across three landings in one day (`p4 describe -s 17207`,
`-s 17210`, `-s 17225`).

**G-6.6.** Four more stages landed on 2026-08-20: `b3` as "B3 as a diagnostic
stage" (18011), `gopmode` and `xhci` taking "the diag ladder to 13 stages" with
"the GOP mode bank and its ASUS answer" (18048), `asde` as "ASDE as diagnostic
stage 14, and the probe chapter that contradicted itself" (18104), and `pch` as
"pch-state, the whole payload of sitting 7" (18373).

**G-6.7.** `pchk1` landed on 2026-08-21 as stage 15, reading "770.17 back after
the K1 write", and the same changelist moved `asde` to 16, recording that "The
write is in `e1000-init` via b3 at 14, not `nicinit`"
(`p4 describe -s 18643`).

**G-6.8.** Seventeen days then pass with no new stage. The next two land on
2026-09-07: `mscalign` lifts the MSC 64 KB-crossing probe "into diag stage 9,
placed in risk order, liveness control carried" (22766), and `kbd` arrives with
"keyboard publish block on GopUsbKbd plus diag stage 9 kbd", whose description
ends "No seed, nothing flyable" (22788).

**G-6.9.** The final two stages landed on 2026-09-08 at CL 23532, the same
changelist that landed the network record channel: `lease` for NIC-6 and `rtcw`
for WORKS-24 (`p4 describe -s 23532`; part 05, F-6.8).

**G-6.10.** Both of the final two had never been read on metal when the last
flight flew, and the pre-flight card states the fact
(part 06, G-3.13).

### 6.2 What the record means

**The ladder went from nothing to nine stages in one day.** The grouping ruling
landed on 2026-08-18 and by the end of that day the ladder existed and carried
the passive rows, the block ladder, the sink ladder and the NIC three. The
design's numbered steps are visible in the changelist descriptions, and each
step names the arms that landed with the stage. A project that had spent months
flying one question per image built a nine-stage instrument in a day once the
question was posed as an instrument rather than as a flight.

**The arms grew with the stages, and that is why the ladder is the healthiest
instrument in the whole report.** Eleven arms, then twelve, then fifteen, each count
in the changelist that added the stage. Compare the medium, where the flusher's
arm arrived after nine lost flights, and the flasher, which had no arm at all
for a month. The difference is not skill; the difference is that the ladder was
designed after the project had learned what an instrument costs, and the medium
was inherited from before.

**The seventeen-day gap between 2026-08-21 and 2026-09-07 is the campaign's
quiet period, and the record explains the quiet.** No stage landed because no
flight flew: sitting 12 flew on 2026-08-24 and the next boot is 2026-09-07. The
ladder was complete for the questions then open, and the work of that window was
the medium's, which part 05 section 6 measures.

**The last two stages landed on the last build, the day before the last
flight.** `lease` and `rtcw` arrived in the same changelist as the channel that
was to carry their readings home, and neither had been read on metal. Under a
one-flight rule that is the correct decision, because a question with no stage
cannot ride at all, and the pre-flight card said plainly that both were new.
Both returned "not reached". The rule that says every question rides the last
sitting turns the last build into the widest build, and the widest build carries
the most code that has never met the board.

**`kbd`'s own changelist says "No seed, nothing flyable", which is the ladder's
discipline in four words.** A stage landed, the arms ran, and the description
states the limit of what the landing proves. The ladder's changelists do that
routinely and the medium's rarely, and the report's cross-part value is largely
in that contrast.

### 6.3 Findings

**FESTER-G25.** The ladder grew from zero to nine stages on the day of the
grouping ruling, and each of the four landings names the arm count that landed
with the stages.
*Supported by:* G-6.3, G-6.4, G-6.5.
*Falsified by:* a stage landing with no arm named in its changelist. The four
landings of 2026-08-18 name 11, 12 and 15 arms (G-6.5).

**FESTER-G26.** Fourteen of the twenty stages existed within four days of the
ruling, therefore the instrument was not what the campaign lacked after
2026-08-21.
*Supported by:* G-6.2, G-6.6, G-6.7.
*Falsified by:* a metal question open after 2026-08-21 with no stage. `lease`
and `rtcw` are the two, and both landed before the last flight (G-6.9).

**FESTER-G27.** No stage landed between 2026-08-21 and 2026-09-07 because no
flight flew in that window, therefore the ladder's development tracked flights
rather than calendar time.
*Supported by:* G-6.8, G-6.2.
*Falsified by:* a boot between sitting 12 on 2026-08-24 and 2026-09-07.
Appendix A's rows carry none.

**FESTER-G28.** The last build carried the two youngest stages in the ladder, both
unread on metal, because a one-flight rule makes the final build the widest one.
*Supported by:* G-6.9, G-6.10.
*Falsified by:* a metal reading for `lease` or `rtcw` before 2026-09-09. Both
answer "not reached" (part 06, G-4.10).

---

## 7. `-EntryStart`, the entry the payload takes, and the gaps by lesson

The charter asks part 06 for the stub and `-EntryStart` history and for every
instrument gap found after a flight. The two belong in one section, because the
stub's own source is where the gaps are written down, and because the sharpest
gap in the record is a symptom that two different defects produced on
consecutive flights.

### 7.1 What the record says

**G-7.1.** A payload's entry point is `opening` by default and `__start` under
`-EntryStart`. `__start` is the bare-metal runtime init: the GDT, the page
tables, CR3, the syscall MSR, and `emit-ata-init`. A payload entered at
`opening` never runs any of that, therefore every `block-read-sector` answers
-1 (`build/cdx-to-pe.ps1:203-212`).

**G-7.2.** The consequence was measured on 2026-08-08: the compiler in DISK mode
entered at `opening` triple-faults in `fat16-find-in-root` with CR2=0 on the
unchecked -1, and entered at `__start` compiles from the volume and emits a CDX
byte-identical to the host's (`build/cdx-to-pe.ps1:208-212`).

**G-7.3.** The default stays `opening`, because the shipping GOP payloads do not
use the compiler's block layer at all and read the stick through their own USB
mass-storage driver (`build/cdx-to-pe.ps1:213-217`).

**G-7.4.** `-EntryStart` is what killed attempt 28 on 2026-08-11: the payload dies
between the stub's jump and the first line of `opening`, because `-EntryStart`
enters `__start`, which takes the hardware over with boot services still live,
and `emit-wait-for-tick` never returns on the board
(`A-sittings-table.md`, attempt 28; part 05, F-5.4).

**G-7.5.** The stub refuses to emit a binary without the syscall dispatcher, and
the refusal's comment records why: a stub that does not program `IA32_LSTAR`
produces a binary that triple-faults on the first syscall, and "that is exactly
what the UEFI dev console did from the first image ever built until 2026-07-27",
where the payload "reached `opening`, went straight to `read-superblock ->
block-read-sector`, issued `syscall` against an unprogrammed MSR, landed at
0xa016, executed zeroes and wrote to address 0"
(`build/cdx-to-pe.ps1:229-241`).

**G-7.6.** The refusal is a throw rather than a warning: "`__syscall_handler` not
found in the CDX debug map ... Refusing to emit"
(`build/cdx-to-pe.ps1:239-241`).

**G-7.7.** A 3 GB allocation ceiling existed for `-EntryStart` payloads whose page
tables map only [0, 3 GB), and the stub's own comment calls the ceiling "worse
than useless on real firmware" for a stub that never loads CR3
(`build/cdx-to-pe.ps1:867-871`).

**G-7.8.** The ceiling's cost was the a5fix flight of 2026-08-13, which came back
with every heap record reading `00FF00FF00FF00FF`, two MAGENTA pixels, because
the heap pointer ended up AT the framebuffer and every repaint bulldozed the
guest's own records (`build/cdx-to-pe.ps1:871-875`).

**G-7.9.** Two mechanisms produce that reading and the stub now refuses both
rather than trusting either: a firmware that answers success without writing the
returned base into the in and out cell, where the old code seeded that cell with
the ceiling, therefore an unwritten cell READ BACK as `0xC0000000`, the board's
framebuffer base; and a heap range that genuinely overlaps the framebuffer
aperture (`build/cdx-to-pe.ps1:875-880`).

**G-7.10.** A second allocation guard, the `V` mark, was measured on 2026-08-14:
every `-Ebs` diag image built after the AnyPages change halts in the stub
printing `svcV`, with RAX the heap the firmware chose and RDX its record arena
end straddling codex-vm's in-RAM framebuffer at `0xBF000000`, and the record
states "The guard is CORRECT" (`build/cdx-to-pe.ps1:884-890`).

**G-7.11.** THE SAME SYMPTOM HAD TWO CAUSES ON CONSECUTIVE FLIGHTS. The run
sheet's heading of 2026-08-13 reads "DIAGNOSED AND FIXED 2026-08-13: every
MAGENTA stall was the WRONG VOLUME. Fly `a5fix.img`"
(`HardwareSitting.md:4567`), and the a5fix flight built to carry that fix
returned at 21:45 the same day with the magenta reading, from the heap-at-
framebuffer mechanism rather than from a volume
(`HardwareSitting.md`, archive row `a5fix-returned-20260813.img`;
`build/cdx-to-pe.ps1:871-875`).

**G-7.12.** The instrument gaps the charter names, each with the flight that
bought the gap and the state at head:

| lesson | what the gap was | bought by | state at head |
|---|---|---|---|
| L-CHANNEL | the payload reported only through the screen, therefore a payload that never executed could not report | attempt 1, 2026-07-29 (part 05, F-1.2) | closed by the two liveness marks (G-1.4) |
| L-STATES | every stub failure path ended in one silent spin, therefore five events were one observation | attempt 1 (part 05, F-1.9) | closed for the stub; OPEN for the ladder's pre-bank steps, which is what sitting 15 proved (G-4.9) |
| L-ARTIFACT | the gated image and the flashed image were different files built from one source | attempt 1 (part 05, F-1.3) | closed by the rehearsal refusal in `flash-usb.ps1` (part 05, F-2.18) |
| L-SAMEVER | a payload's identity was assumed rather than checked against the flashed bytes | sitting 7, attempt 45 (part 05, F-2.6) | closed twice: the payload id check, and the stub-migration equality check of 2026-08-02 (G-1.4) |
| L-BANK | an instrument reachable only through the subsystem the instrument measures | attempt 51, 2026-09-07 (part 05, F-2.15) | OPEN: `asde` still banks nothing until `asde` completes |
| L-REHEARSE | an image flew that had not run its full mission as the exact bytes | the 2026-08-14 ruling (part 05, F-2.18) | closed and on by default from 2026-08-20 |
| L-FALSIF | arms that could not fail, and a lever keyed to the run rather than the subject | the ladder arms, 2026-08-20 (G-3.6, G-3.7) | closed in the harness; the harness grades its own controls (G-3.2) |

### 7.2 What the record means

**The entry-point choice is a fork the project had to get right twice, and the
two correct answers are opposite.** A GOP payload must enter at `opening`,
because `__start` takes the hardware over with boot services still live and
never returns on the ASUS. A compiler payload reading its own volume must
enter at `__start`, because `opening` never runs the block init and every sector
read answers -1. Neither entry is right in general, the flag exists for exactly
that reason, and attempt 28 is what the wrong choice costs.

**The syscall refusal is the best single line of defence in the pipeline, and
the bug the refusal defends against shipped in every image the project ever built until
2026-07-27.** A payload reaching `opening`, issuing a syscall against an
unprogrammed MSR, landing at 0xa016 and executing zeroes is not a subtle
failure, and the project shipped the defect for months because nothing had ever reached
the first syscall on real firmware. The repair is a throw at build time rather
than a warning, which is the correct severity, and the comment beside the throw
says plainly that emitting without the dispatcher would be shipping the bug
again silently.

**The 3 GB ceiling is the clearest case in the whole report of a guard that was
correct for one payload shape and harmful for another.** The ceiling exists
because an `-EntryStart` payload's page tables map only the low 3 GB. The stub
that carries the ceiling never loads CR3, therefore the ceiling constrains an
allocation that had no constraint, and on the ASUS the constrained value was
the framebuffer's own base. A guard inherited across a shape change becomes a
defect, and the symptom was two magenta pixels where the guest's records should
have been.

**The same symptom had two causes on consecutive flights, and the run sheet
announced the first as settled.** "Every MAGENTA stall was the WRONG VOLUME" is
a heading dated the same day as the flight that returned magenta for an entirely
different reason. Neither reading is wrong about its own flight. The heading is
wrong about the future, and a heading that says "every" is a claim about flights
that have not happened. Part 05's F-5.5 states the wrong-volume diagnosis as the
record states the diagnosis, and G-7.11 is the other half.

**Two of the seven named gaps are still open at head, and both are the same
shape.** L-BANK is open because `asde` still banks nothing until `asde`
completes. L-STATES is closed for the stub and open for the ladder's pre-bank
steps, which is precisely the gap sitting 15 fell into. Both are instruments
that cannot report on the failure of the subsystem each runs through, and both were
measured rather than argued, and neither will be closed now, because closing
either requires a flight.

### 7.3 Findings

**FESTER-G29.** The entry-point flag exists because the two payload families need
opposite entries, and attempt 28 was lost to the wrong entry taking the hardware
over with boot services still live.
*Supported by:* G-7.1, G-7.2, G-7.3, G-7.4.
*Falsified by:* a GOP payload running correctly under `-EntryStart` on the
board. Attempt 28 died before `opening`'s first line (G-7.4).

**FESTER-G30.** Every image the project built until 2026-07-27 carried a payload
that would triple-fault on its first syscall, and nothing detected the defect
because nothing had reached a syscall on real firmware.
*Supported by:* G-7.5, G-7.6.
*Falsified by:* an image before that date whose payload completed a syscall on
the board. The record's account states the opposite (G-7.5).

**FESTER-G31.** A guard written for one payload shape and inherited by another
placed the heap at the board's framebuffer base, therefore the guest's own
records were overwritten by every repaint.
*Supported by:* G-7.7, G-7.8, G-7.9.
*Falsified by:* a magenta reading on a flight whose heap did not reach the
framebuffer. The stub now refuses both producing mechanisms (G-7.9).

**FESTER-G32.** One symptom had two unrelated causes on two consecutive flights of
2026-08-13, and the run sheet's heading declared the first cause to explain
every instance.
*Supported by:* G-7.11, G-7.8.
*Falsified by:* the a5fix flight's magenta having a volume cause. The stub's own
source attributes the flight's records to the heap (G-7.8).

**FESTER-G33.** Five of the seven instrument gaps the charter names are closed at
head and two remain open, and both open gaps are instruments reachable only
through the subsystem each measures.
*Supported by:* G-7.12.
*Falsified by:* a banked `asde` row, or a step marker before the ladder's
pre-bank steps. Neither exists at head (G-7.12).

---

## Coverage

### Files read in full for section 1

| path | extent read | revision read |
|---|---|---|
| `docs/PM/Active/Stories/TheLostParadise.md` | 13,216 bytes, in full | `//Codex/root` at CL 25185 |
| `build/boot/diag/README.md` | lines 1-46 of 53,215 bytes | workspace at CL 25250 |
| `docs/Hardware/HardwareSitting.md` | lines 1-151, 1681-1701 | workspace at CL 25250 |
| `build/boot/diag-arm.ps1` | the `cfg-off` and `esp-cfg-off` arms at 171, 924, 925, 1181, of 159,480 bytes | workspace at CL 25250 |
| `apps/works/works-backlog.md` | row WORKS-62 in full | workspace at CL 25250 |

### Files read for section 2

| path | extent read | revision read |
|---|---|---|
| `build/cdx-to-pe.ps1` | the `AllocPanic` generator at 302-333 and the panic sites at 970 and 989 | workspace at CL 25263 |

### Files read for section 3

| path | extent read | revision read |
|---|---|---|
| `build/boot/diag-arm.ps1` | lines 1-62 (the harness contract, the arm table's documented arms, the ordinal-drop account), and the 50 arm-table entries counted | workspace at CL 25270 |
| `docs/Hardware/HardwareSitting.md` | lines 937-966, the sitting-15 pre-flight card | workspace at CL 25270 |

### Files read for section 4

| path | extent read | revision read |
|---|---|---|
| `docs/Hardware/HardwareSitting.md` | lines 151-186, the closure of the queue and the sitting-15 result | workspace at CL 25275 |
| `build/boot/diag/DiagRecord.codex` | lines 44-64 (the cost and the give-up budget) and 327-334 (`drec-open-armed`), of 16,099 bytes | workspace at CL 25275 |

### Files read for section 5

| path | extent read | revision read |
|---|---|---|
| `build/boot/build-diag.ps1` | lines 1-48 (the contract and the parameters), 92-135 (the two cfg refusals and the stage-table parse), 148-153, 224-248 (the recipe and the bundle stamp), of 17,398 bytes | workspace at CL 25279 |

### Files read for section 6

| path | extent read | revision read |
|---|---|---|
| `build/boot/diag/Diag.codex` | the `dg-stage-name` and `dg-stage-count` tables, parsed rather than transcribed, of 65,185 bytes | workspace at CL 25371 |

### Files read for section 7

| path | extent read | revision read |
|---|---|---|
| `build/cdx-to-pe.ps1` | lines 203-246 (the entry contract and the syscall refusal) and 864-890 (the ceiling, the magenta mechanism and the V guard) | workspace at CL 25386 |
| `docs/Hardware/HardwareSitting.md` | line 4567 and the archive row `a5fix-returned-20260813.img` | workspace at CL 25386 |

### Extractions run for section 7

```powershell
Select-String -Path build/cdx-to-pe.ps1 -Pattern 'EntryStart'
Get-ChildItem build,codex,docs -Recurse -Include *.ps1,*.codex | Select-String -Pattern 'EntryStart' -List
```

### Extractions run for section 6

```powershell
# the stage table, parsed from the source
[regex]::Matches($src, 'i == (\d+) then "([a-z0-9-]+)"')
# the first depot revision of each of the twenty stage chapters
p4 filelog -m 50 //Codex/main/build/boot/diag/Diag<Stage>.codex
p4 describe -s 16822 16851 17207 17210 17225 18011 18048 18104 18373 18643 22766 22788 23532
```

### Extractions run for section 5

```powershell
p4 filelog -m 8 //Codex/main/build/boot/build-diag.ps1
p4 describe -s 18574    # build refuses a cfg leaving a risky stage unnamed
p4 describe -s 18645    # the diag build refuses a config key named twice
p4 describe -s 24252    # diag staleness guard keyed on the payload bundle closure
```

### Extractions run for section 4

```powershell
Select-String -Path build/boot/diag/DiagRecord.codex -Pattern 'drec-open|drec-give-up|drec-ship|net-driver-bring-up'
```

**ONE EXTRACTION HERE WAS DEFECTIVE AND PRODUCED TWO WRONG COUNTS, therefore the
method is recorded rather than the result alone.** Reading `A-sittings-table.md`
by splitting a row on the pipe character shifts every column of any row whose
CELL TEXT contains a pipe, and row 50's question cell contains `` `CTRL|SLU` ``.
That row therefore read as though the row carried no peer connection, and
FESTER-G20 was published twice with a count that was low, first by two and then
by one. Read a row of that table whole, or match the cell by name; never split
the row on the delimiter. The count is three and root's reading is what
corrected the count.

### Extractions run for section 3

```powershell
# the arm count, measured rather than quoted
(Select-String -Path build/boot/diag-arm.ps1 -Pattern "^\s*'[a-z0-9\-]+'\s*=\s*'").Count
```

### Extractions run for section 2

```powershell
p4 describe -s 15469    # the stub picks the largest GOP mode; -EntryStart arms unchanged
p4 describe -s 15503    # AllocPanic printed its letter and then kept running
Select-String -Path build/cdx-to-pe.ps1 -Pattern 'AllocPanic|0xFA|cli|hlt'
```

### Extractions run for section 1

```powershell
p4 describe -s 22995                                  # the CCE carriage-return guard and its arm
Get-ChildItem build/boot -File | Select-Object Name,Length      # the 13 images and the pipeline scripts
Get-ChildItem build/boot/diag -File | Select-Object Name,Length # the 38 probe and stage chapters
Select-String -Path build/boot/diag-arm.ps1 -Pattern 'esp-cfg-off|cfg-off'
```

### What section 1 did not read, and why

`build/boot/diag-arm.ps1` is 159,480 bytes and is read arm by arm for the
sections that need each arm rather than in one pass; section 1 needed the two
cfg arms alone. The 196 revisions of `build/boot/**` named in the charter are
read for section 2, which is the stage-by-stage history, rather than for
section 1.
