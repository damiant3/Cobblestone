# Part 11. Recommendations

*Part of `docs/PM/Active/Stories/TheLostParadise.md`. Owner: root. First
draft written 2026-09-09 04:00 against the root causes of part 10. Each
recommendation names the cause answered, the form the recommendation
takes (a runner, a rule, a register row, or a decision of Damian's), and
the check that would show the recommendation is in force. A
recommendation with no check is prose, and part 08 records what happens to
prose (VAL-F3, VAL-F4). The recommendations are written under the ruling
that no metal remains; section 3 says what that ruling leaves.*

## 1. What would have delivered a metal kernel

**R-1. A flight is conditioned on a bed reproduction, mechanically (RC-1,
RC-4).** No image flies to ask a question until a named bed arm has
reproduced the failure the question is about, and the arm's id is on the
flight card. Form: `flash-usb.ps1` gains `-Arm <id>` beside `-ExpectHash`,
refuses without one, and checks the id against `diag.rehearsed`'s arm list;
the pre-flight card's per-row prediction table is generated from the arms,
not written by hand. Check: `flash-usb.ps1` refuses a rehearsed image whose
card names no arm. An arm that exists is not enough: the A5 handle-order
arm existed, passed, and never reached the code (REEK-F3), therefore R-1
requires the arm to have been OBSERVED REFUSING at least once, and the card
names the refusing run. Had R-1 existed on 2026-08-09, two of the A5
campaign's three freedoms would have been arms before flights (REEK-F9)
and the third would have been refused for a vacuous pass. No arm
reproduces sitting 15's stop today; REEK-F4's falsifier is unmet. The
positive case exists in the record: the one green flight in the story set
is the flight whose exact bytes completed the full mission in the bed first
(VAL-S4).

**R-2. A flight has a price, a counter and a bound (RC-2).** Form: a file
`docs/Hardware/flights.csv` appended by `flash-usb.ps1` on every flash
(date, image, arm, questions aboard, outcome to be filled by the reading),
and a bound Damian sets (flights per week) that the flasher enforces and
that only he can raise, by editing the bound line himself. Check: the
flasher refuses the N+1th flash in a week. Had R-2 existed with a bound of
two, 2026-08-21's six boots would have been one composition read once; the
counterfactual is an illustration of the bound's effect, not a measurement,
and the same is true of every "had R-n existed" sentence in part 11.

**R-3. Every step before a probe's first record line prints a line (RC-6,
CC-2).** `L-STATES` becomes a check: `build/check-diag-states.ps1` walks
the ladder's opening from the last passive stage to the first bank or ship
call and fails the image build if any call between them lacks a `say`
before the call. Check: the current `drec-open` fails the check as written.
Sitting 15's stop would have named its step.

**R-4. The record channel is the only register, and the medium is a
subject (RC-1, L-CHANNEL).** Form: `build-diag.ps1` refuses a cfg whose
record channel is off unless `-AllowGlassOnly` is passed, and the flight
card names the peer, the port, the firewall profile and the rule. Check:
the builder refuses the sitting-13 cfg; the card template carries the four
fields. The firewall profile mismatch of 2026-09-09 (ROOT-A4) would have
been on the card.

**R-5. The one document every session reads carries the flight rules (RC-3).**
Form: `CLAUDE.md` gains one tier-2 rule, `R-FLIGHT`: a flight needs a bed
arm, a card with a prediction per row, a record channel, and a counter row;
the init skill's Step 5 table gains the row "A flight, a sitting, a stick |
`HardwareSitting.md` QUICKREF and `flights.csv`". Check: `p4 grep` for
`R-FLIGHT` in `CLAUDE.md` at head. VAL-F1's eight-term zero becomes nonzero.

**R-6. A lesson with no runner is a register row with an owner, or is
deleted (RC-3).** Form: `check-doc-counts.ps1` gains a check over
`LESSONS.md`: every row whose runner column reads `none` must be named in
a `CurrentPlan.md` row or a backlog row; otherwise the gate warns with the
id. Check: the current index produces one warning per such row (46 bare
`none` and 9 candidates at 25409, appendix D). The
five guards of val's 2026-07-29 post-mortem would have had owners by
2026-08-28.

**R-7. A recommendation has an owner the day the recommendation lands
(RC-3, VAL-F4).** Form: a post-mortem's recommendations section is a table
with an owner column and a register-row column, and the story is not
landed until both are filled; root fills them at the review. Check: section 4
of part 11 below.

**R-8. A document's date is `p4 filelog -i`; a claim's date is `p4
annotate -c -i`; the register's rulings are kept in a ledger the register
cannot replace (RC-5).** Form: `build/p4-metal-diffs.ps1` generalised to a
`build/p4-ruling-ledger.ps1` run at each release, appending every added or
deleted line of `CurrentPlan.md` matching a ruling pattern to
`docs/PM/Active/Rulings.md`; the charter's rule 1 date method copied into
`PerforceProcess.md`. Check: `Rulings.md` exists and grows at release time.

**R-9. The commander puts the second reading to Damian when his ask admits
two, and lands no rule in his name that he did not type (RC-6).** Form: a
sentence in the commander-init skill under Grants: "A rule attributed to
Damian quotes his words; a reading of his words is attributed to root, and
where his ask admits two readings both are put to him in one line before
either is executed." Check: `HardwareSitting.md` line 185's attribution is
corrected to read "root's reading of Damian's 21:10 turn, ratified by him
2026-09-09 02:21". The correction is made in the assembly CL.

**R-10. The endgame of a campaign is not composed during the human's sleep
(RC-6, RED-F10).** Form: the same skill: "A flight's composition, its
ruling and its sign-off wait for Damian's next turn; the fleet builds the
arms meanwhile." Check: no sign-off CL between his last turn of a day and
his first of the next.

**R-11. The human sets a bound on his own presence at the board, and the
system refuses on his behalf (CC-8).** R-11 is Damian's alone: a
number of flights per week, or zero, written into `flights.csv`'s bound
line by his hand. Nothing in the fleet's rules can supply the number, and
R-2's check refuses on the number he writes.

**R-12. A returned stick is dumped before anything else happens to the
stick, and the dump is the record (RC-5, FESTER-F14).** The rule exists
(the QUICKREF ruling of 2026-08-11); the runner does not. Form:
`flash-usb.ps1` refuses to write a disk whose current first sector's hash
is in `diag.rehearsed` and is not in `stick-archive`'s ledger. Check: the
flasher refuses to overwrite the sitting-15 stick until the stick is
dumped.

## 2. What to do with no metal remaining

**R-13. The bed program replaces the flight program.** REEK-F7 names the
one kind of divergence that closes by building more model, a model absent;
every question that rode sitting 15 is classified by that finding: asde and
NIC-4 (models present and wrong on the ring and reset paths, closable by
the `-usb-writeback`-style approach of naming the freedom and building the
hostile choice); WORKS-24 and the PHY 770.17 case (firmware state,
unmodellable, deleted as unanswerable by ruling); NIC-6 (the bed's NAT
answers every ARP with one MAC, `tools/codex-vm.c#34:7855`, fleet-owned code
in REEK-F7's generous class, therefore a model repair with a named arm, not
a deletion); the flush (already reproduced in the bed,
FESTER-F19; the arm becomes a gate arm, on by default); the network
channel (bed-proven, kept as the ladder's register for any future board).
Form: a `codex-vm` row per question in `DeviceEmulationCatalog.md` with the
freedom named and the arm id; the `-usb-writeback` model on by default in
the diag rehearsal. Check: `diag-arm.ps1` carries an arm per closable
question and the catalogue names the deleted ones as unanswerable.

**R-14. The sitting-15 stick is dumped and read (R-12).** The dump
separates candidate 1 (the USB attach wrote something) from candidates 2
and 3 (the ESP read or the bring-up), which is the only further metal
reading the ruling permits, because the reading costs no boot. Form:
Damian's hand on `dump-stick.ps1` per the QUICKREF; root reads the dump
against `Diag.codex` lines 1123 to 1127. Check: a row in the stick-archive
ledger for 2026-09-09.

**R-15. The metal-gated rows are closed the way the ruling says (Track A,
2026-09-09).** Each owner's docs CL: bed-answerable stays as a bed item
with its arm named; metal-only is deleted as unanswerable; "rides the last
sitting" appears in no register. Check: `p4 grep` for "sitting" in
`CurrentPlan.md` and the backlogs returns the standing rule and nothing
open.

**R-16. The public record is amended once.** Update 34 reported "the PS/2
keyboard works" on the ASUS; the keyboard was USB behind the BIOS's SMM
emulation, and the fleet disabled that emulation three days later with no
amendment filed (BLU-F21, BLU-F19). The next GitHubUpdate carries a
one-paragraph amendment naming the mechanism, the date the fallback was
removed, and a pointer to the present report. Check: the paragraph exists
in the next update.

## 3. What the recommendations cost, and what the recommendations do not claim

R-1 to R-4, R-6, R-8 and R-12 are runners: about a day of a lane each,
docs and PowerShell, no seed, no token. R-5, R-7, R-9 and R-10 are rule
text: an hour each. R-11 is Damian's number. R-13 is the one program: the
closable questions are two (asde, NIC-4), each a model of a freedom the
part exercises, priced by REEK-F9 at minutes to days once the freedom is
named. R-14 is one hand. None of the sixteen claims to deliver a metal
kernel: the ruling forecloses the flight that would prove one, and the
report's spine (part 10, section 6, finding 1) is that the fleet had the
knowledge to condition flights on beds from day 11 and did not. The
recommendations make that condition mechanical for any board the project
ever meets again, and give the human a number to refuse on.

## 4. Owners

| recommendation | owner | register row |
|---|---|---|
| R-1, R-2, R-12 (`flash-usb.ps1` refusals, `flights.csv`) | fester | `apps/works/works-backlog.md`, one row |
| R-3 (`check-diag-states.ps1`) | fester | the same row |
| R-4 (`build-diag.ps1` record-channel refusal, card fields) | fester | the same row |
| R-5 (`R-FLIGHT`, the init row) | root | `CurrentPlan.md`, root's row |
| R-6 (the lessons runner check) | val | `CurrentPlan.md`, val's row |
| R-7, R-9, R-10 (rule text) | root | root's row |
| R-8 (`p4-ruling-ledger.ps1`, `Rulings.md`) | red | red's row |
| R-11 | Damian | the bound line of `flights.csv` |
| R-13 (the bed program, catalogue rows, arms) | reek, blu for the NIC arms | `codex/plugs/plugs-backlog.md`, blu's row |
| R-14 (the dump) | Damian's hand, root reads | `HardwareSitting.md` QUICKREF |
| R-15 (the reclassification CLs) | blu, reek, fester | ordered 2026-09-09 02:45 |
| R-16 (the amendment paragraph) | red (releases, personally and end to end, per red's standing column) | `GitHubUpdate58.md`, red's row |

None of the rows above exists at the time of the draft. Under R-7 the
assembly CL creates them or part 11 is not landed as final.
