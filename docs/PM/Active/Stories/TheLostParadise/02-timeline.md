# Part 02. The master timeline, 2026-03-14 to 2026-09-09

*Owner: red. Part of `TheLostParadise.md`, the accident investigation ordered
by Damian on 2026-09-09 at 02:55. Rules 1 to 7 of the charter bind every
sentence below.*

## 02.0 What this part is and how the part was built

This part carries the chronology the other parts stand on. The other parts
answer what happened to the network part, to the medium, to the beds and to
the organization. Part 02 answers when each of those events happened, in what
order, and against what else the fleet was doing on the same day.

Every figure below was produced by one of the commands in section 02.16,
"Coverage", and every command is written there in the exact form used. A
reader who re-runs a command and reads a different number has found either a
depot that has moved forward or an error in this part, and section 02.16
gives the measurement date for each figure so that a reader can tell the two
apart.

**A warning that governs every count in this part.** The word "sitting" occurs
in ordinary Codex prose as well as in the name of a hardware flight, therefore
a count of changelists whose description contains the word "sitting"
overstates the number of changelists about hardware flights. Section 02.3
gives the measurement, gives four discriminators rather than one, and states
what each discriminator can and cannot see. No single number in this part
stands alone where a second measurement was available.

## 02.1 What the record says: the two eras

The project began on 2026-03-14 (`CLAUDE.md`, "The project was started
3/14/2026"). Perforce holds nothing from the first thirty-four days. The
first changelist on the main stream is CL 2 on 2026-04-17, and CL 2 changed
`CLAUDE.md` itself (`p4 changes -l //Codex/main/CLAUDE.md`).

Therefore the record divides into two eras, and the division is not a matter
of interpretation:

| era | dates | length | what survives |
|---|---|---|---|
| Pre-Perforce | 2026-03-14 to 2026-04-16 | 34 days | `docs/PM/Done/Plans` and `docs/PM/Done/Handoffs` only |
| Perforce | 2026-04-17 to 2026-09-09 | 146 days | 7,220 changelists on the main stream |

Day counts in this part are inclusive of both endpoints. The whole project
measures 180 days on that convention. The Perforce era measures 146 of those
days, therefore 18.9 percent of the project's calendar leaves no changelist
evidence at all.

## 02.2 What the record says: the monthly spine

The main stream carries 7,220 changelists, measured 2026-09-09. The
distribution across months is uneven, and the shape matters to every finding
in this part:

| month | changelists |
|---|---|
| 2026-04 (from the 17th) | 379 |
| 2026-05 | 870 |
| 2026-06 | 750 |
| 2026-07 | 1,496 |
| 2026-08 | 2,387 |
| 2026-09 (to the 9th) | 1,338 |

August 2026 carries 2,387 changelists, which is the single busiest month and
is 33 percent of the whole depot. September 2026 carries 1,338 changelists in
nine days, which is a daily rate of 149 against August's daily rate of 77.
The fleet was working at twice August's rate during the nine days that ended
in the last sitting.

## 02.3 What the record says: when a changelist names a sitting

Four discriminators were measured over the same 7,220 changelist
descriptions. Each answers a different question, and the differences between
the four are the finding rather than a nuisance:

| discriminator | changelists | first occurrence |
|---|---|---|
| contains "sitting" in any form | 182 | 2026-04-26 |
| contains "HardwareSitting" | 73 | 2026-07-29 |
| contains an article and "sitting" (the, a, last, next, this) | 42 | 2026-07-29 |
| contains "sitting" followed by a number | 49 | 2026-08-14 |

**The broadest discriminator is wrong and the reason is visible in the first
rows.** CL 388 of 2026-04-26 is the earliest changelist containing the word
"sitting", and CL 388 replaced `hlt` with a poll-drain in two serial readers.
CL 7532, CL 7568 and CL 7572, all of 2026-07-13, are the 3D stack, the engine
foreword and the phase deck floors. Not one of the four names a hardware
flight. The word occurs in each as ordinary English.

Therefore the honest reading is:

- **The vocabulary of the flight record begins on 2026-07-29**, which is the
  first date on which a changelist names `HardwareSitting` or writes "the
  sitting" as a thing with an article.
- **Numbered sittings begin on 2026-08-14.** No changelist before 2026-08-14
  names a sitting by number. A reader who wants to know which flight was the
  fourth cannot learn the answer from a changelist description written before
  2026-08-14.

**A correction to the charter's own corpus table, and the trap that would hide
the correction.** The charter's table states "CLs naming a sitting in their
description | 217". The figure of 217 is a count of matching LINES rather than
a count of changelists. Measured two independent ways over the same corpus:
182 distinct changelists contain at least one line matching the word, those
182 changelists carry 217 such lines between them, and 19 of the 182 carry
more than one line. The arithmetic closes exactly, because 182 changelists
plus 35 additional lines gives 217. The trap is that a second and unrelated
expression, "sitting or flight or flew", also returns exactly 217
changelists, therefore a reader checking 217 by a different route can confirm
the figure and still be wrong about what the figure counts.

## 02.4 What the record says: when the flight record began

`HardwareSitting.md` is the fleet's record of flights. The file was created on
**2026-07-28** as `docs/HardwareSitting.md` (fester CL 11775). The file moved
to its present path `docs/Hardware/HardwareSitting.md` on **2026-08-13**
(fester CL 14777, main CL 14778, "Docs filing: move misfiled docs").

Two dates follow from the move, and confusing the two produces a wrong
timeline:

- The **record** of flights begins on 2026-07-28, CL 11775.
- The **directory** `docs/Hardware` begins on 2026-08-13, CL 14778, therefore
  a search scoped to `docs/Hardware/...` cannot see the first sixteen days of
  the flight record and reports 2026-08-13 as the beginning.

The project began on 2026-03-14. The flight record began on 2026-07-28.
**136 days of the project ran before the first flight was written down**,
which is 76 percent of the project's calendar.

Damian states in the charter that the undocumented era holds "over 120" of
"hundreds" of sittings across "3 months or so". Part 03 owns the
reconstruction of that count and its error bars. Part 02 states only what the
depot proves: no changelist before 2026-07-28 records a flight, and the
directory that now holds the record did not exist before 2026-08-13.

## 02.5 What the record says: the metal paths and when each was worked

Eight paths carry the metal work. Each was measured with
`p4 changes -l <path>` on 2026-09-09:

| path | changelists | first | last |
|---|---|---|---|
| `seed/...` | 906 | 2026-04-25 (CL 368) | 2026-09-09 (CL 25061) |
| `tools/codex-vm.c` | 240 | 2026-05-07 (CL 1154) | 2026-09-08 (CL 23601) |
| `build/boot/...` | 196 | 2026-07-08 (CL 7292) | 2026-09-08 (CL 24252) |
| `codex/os/net/...` | 149 | 2026-05-18 (CL 1630) | 2026-09-08 (CL 24862) |
| `codex/os/kernel/...` | 134 | 2026-05-18 (CL 1630) | 2026-09-02 (CL 22189) |
| `CLAUDE.md` | 134 | 2026-04-17 (CL 2) | 2026-09-08 (CL 24954) |
| `docs/Hardware/...` | 91 | 2026-08-13 (CL 14778) | 2026-09-09 (CL 25176) |
| `docs/PM/CurrentPlan.md` | 1,089 at this path, 1,090 revisions | 2026-05-11 (CL 1322) at this path; **2026-04-17 (CL 2) as a file** | 2026-09-09 (CL 25188) |

**The `CurrentPlan.md` row carries a correction, and the correction is the
same trap this part records in section 02.4.** The first three landings of
part 02 gave the file's first changelist as CL 1322 on 2026-05-11. That figure
is what `p4 changes` reports for the path `docs/PM/CurrentPlan.md`, and CL
1322 is titled "docs: move PM files": CL 1322 is the changelist that MOVED the
register into `docs/PM/`. The register itself was added as
`docs/CurrentPlan.md` by **CL 2 on 2026-04-17**, the initial import, and
`p4 filelog` follows the move and reports 1,090 revisions from that date.
A path-scoped search therefore cannot see the register's first 24 days, in
exactly the way a search scoped to `docs/Hardware/...` cannot see the flight
record's first 16 days. Appendix E reaches the earlier revisions and quotes
them.

The charter's corpus table carries the same uncorrected figure, and the
agreement between the charter and the first landings of this part was not
evidence, because both figures came from the same command.

**The other rows were then checked for the same trap and the check is
partial.** `p4 filelog` reports revision 1 of `tools/codex-vm.c` as an `add`
at CL 1154 on 2026-05-07 and revision 1 of `CLAUDE.md` as an `add` at CL 2 on
2026-04-17, therefore neither of those two rows hides a move. The four
directory rows cannot be cleared the same way: a single file moved INTO one of
those directories would show at its new path from the move date, and the
directory's first-changelist figure would be correct for the directory while
understating the age of that file's content. No such move is known and none
was searched for.

Every other figure above matches the charter's corpus table exactly, with
two expected differences: `CurrentPlan.md` reads 1,089 rather than 1,088
because the charter itself was registered between the two measurements, and
`seed/...` and `codex/os/net/...` are not in the charter's table.

### The distribution by month is the finding

The same eight paths, counted by month:

| path | 04 | 05 | 06 | 07 | 08 | 09 |
|---|---|---|---|---|---|---|
| `seed` | 29 | 216 | 128 | 304 | 165 | 64 |
| `tools/codex-vm.c` | 0 | 39 | 30 | 77 | 86 | 8 |
| `build/boot` | 0 | 0 | 0 | 26 | 147 | 23 |
| `codex/os/net` | 0 | 9 | 16 | 57 | 44 | 23 |
| `codex/os/kernel` | 0 | 9 | 19 | 64 | 41 | **1** |
| `CLAUDE.md` | 12 | 32 | 8 | 28 | 21 | 33 |
| `docs/Hardware` | 0 | 0 | 0 | 0 | 71 | 20 |
| `CurrentPlan.md` | 0 | 11 | 6 | 57 | 526 | 489 |

Three rows carry the shape of the accident.

**First, the kernel stopped changing before the last sittings.**
`codex/os/kernel` received 64 changelists in July 2026, 41 in August 2026, and
**one** in September 2026. The single September changelist is CL 22189 of
2026-09-02. Sitting 15 flew on 2026-09-09, therefore the kernel that flew on
the last sitting was seven days old and had received one change in the final
nine days of the project.

**Second, instrument work replaced kernel work in August.** During August
2026 the fleet wrote 147 changelists into `build/boot`, 86 into
`tools/codex-vm.c` and 71 into `docs/Hardware`, a total of 304 changelists
into the ladder, the bed and the flight record, against 41 into the kernel
under test. The ratio is 7.4 to 1.

**Third, process writing overtook every other activity.**
`docs/PM/CurrentPlan.md` received 526 changelists in August 2026 and 489 in
the first nine days of September 2026. The September figure alone exceeds the
whole-project total for `codex/os/kernel` (134) by a factor of 3.6.

## 02.6 What the record means

**RED-F1. The flight record began on 2026-07-28, after 76 percent of the
project's calendar had already run.**
Supporting evidence: `HardwareSitting.md` created by CL 11775 on 2026-07-28;
project start 2026-03-14 per `CLAUDE.md`; no changelist before 2026-07-28
names `HardwareSitting` or writes "the sitting" with an article (section
02.3). Falsifying evidence would be a flight record under another name before
2026-07-28, or a changelist before that date describing a flight and its
result. Part 03 owns the search of the undocumented era and can overturn this
finding.

**RED-F2. The kernel under test was effectively frozen for the final week,
receiving one changelist in the nine days that ended in sitting 15.**
Supporting evidence: `codex/os/kernel` monthly distribution above; last
changelist CL 22189 on 2026-09-02; sitting 15 flew 2026-09-09.
Falsifying evidence would be metal-relevant kernel changes landing through a
path outside `codex/os/kernel`, which `codex/os/net` (23 changelists in
September 2026, last CL 24862 on 2026-09-08) partly supplies; section 02.5's
claim is about the kernel path alone and part 04 owns the network driver.

**RED-F3. Through August 2026 the fleet spent 7.4 changelists on instruments
and the record for every 1 changelist on the kernel under test.**
Supporting evidence: 147 plus 86 plus 71 equals 304, against 41.
Falsifying evidence would be a demonstration that the instrument work was on
the critical path to the kernel, which parts 06 and 07 own; the finding as
stated is a ratio of effort and is not by itself a judgement that the ratio
was wrong.

**RED-F4. A changelist search for the word "sitting" overstates flight work by
about 2.5 times, and the charter's own corpus table carries the overstatement
in a second form.**
Supporting evidence: 182 changelists match the word against 73 naming
`HardwareSitting` and 49 naming a numbered sitting; the four earliest matches
are not flights (section 02.3); the charter's 217 is a line count.
Falsifying evidence would be that the changelists matching only the loose
discriminator are genuinely about flights, which a reader can test by reading
the 109 changelists in the difference.

## 02.7 What the record says: the nine days that ended in sitting 15

September 2026 carries 1,338 changelists in nine calendar days, and the
changelists are not spread across the nine days. Three of the nine days carry
no changelist at all:

| date | changelists |
|---|---|
| 2026-09-01 | 226 |
| 2026-09-02 | 230 |
| 2026-09-03 | 17 |
| 2026-09-04 | 0 |
| 2026-09-05 | 0 |
| 2026-09-06 | 0 |
| 2026-09-07 | 323 |
| 2026-09-08 | 480 |
| 2026-09-09 | 62 |

The depot is silent from the last changelist of 2026-09-03, CL 22349, until
the first changelist of 2026-09-07, CL 22355. Six changelist numbers separate
the two, therefore the silence is a real stop in the work rather than a stream
that landed elsewhere.

**The whole endgame is two days.** The composition of the last sitting, every
ruling that shaped the last sitting, the building of the instrument meant to
carry the last sitting's results, the certification of the flight image and
the sign-off all fall inside 2026-09-07 and 2026-09-08, which carry 803 of
September's 1,338 changelists between them.

### The endgame in order, from the flight record's own changelists

Every row below is a changelist that touched `docs/Hardware/...`. The twenty
September changelists on that path are the complete set for the month.

| date | CL | what the changelist did |
|---|---|---|
| 2026-09-07 | 22724 | Pre-flight card for image AC7399ED |
| 2026-09-07 | 22733 | The AC7399ED card names an in-place `DIAG.CFG` edit and refuses the edit |
| 2026-09-07 | 22740 | Corrects which ASDE entry was stale |
| 2026-09-07 | 22750 | **FLOWN, AC7399ED.** The card's headline prediction held |
| 2026-09-07 | 22767 | The glass after `pchk1`, read back by Damian; asde stops at a warm reset |
| 2026-09-07 | 22854 | **FLOWN, second flight of the day, CB1AE335.** The medium died at `kbd`, stage 9 |
| 2026-09-07 | 22859 | b3 is green on sitting 13 and the peer log is the only record |
| 2026-09-07 | 22868 | Retracts a classification-disagreement claim from the sitting 13 entry |
| 2026-09-07 | 22881 | Explains sitting 13's eight-stage gap |
| 2026-09-07 | 23193 | The sitting 14 card |
| 2026-09-07 | 23198 | **One sitting remains; the last sitting composed** |
| 2026-09-07 | 23204 | THE LAST SITTING's question set: six questions no bed can answer |
| 2026-09-07 | 23259 | The last sitting's ordering ruled, b3 early |
| 2026-09-08 | 23332 | `diag-sitting15.cfg` composed, plus the card |
| 2026-09-08 | 23341 | Root's rulings in the card: a static address for the channel |
| 2026-09-08 | 23532 | **The diagnostic ladder gains a NETWORK RECORD CHANNEL**, opening before the first bank write |
| 2026-09-08 | 23768 | The sitting-15 flight image certified, hash 47F29D50, 50 arms |
| 2026-09-08 | 23771 | THE LAST SITTING signed off |
| 2026-09-09 | 25176 | The last sitting has flown; the queue is closed |

### What the order shows

**The instrument that was to carry the answers was built one day before the
only flight remaining.** CL 23532 of 2026-09-08 added the network record
channel to the diagnostic ladder, and the channel was designed to open before
the first bank write. Sitting 15 flew on 2026-09-09 and stopped inside the
first write to the part (`TheLostParadise.md`, "The event under
investigation"). The instrument and the failure met on their first encounter.

**Two flights flew on 2026-09-07 and neither reached its questions.** The
first flight, AC7399ED at CL 22750, ended with asde stopped at a warm reset
(CL 22767). The second flight, CB1AE335 at CL 22854, died in the medium at
stage 9. The decision that one sitting remained, CL 23198, was taken on the
same day as both failures.

**The last sitting was composed, ruled, instrumented, certified and signed off
across two calendar days**, from CL 23198 on 2026-09-07 to CL 23771 on
2026-09-08.

## 02.8 What the record means, continued

**RED-F5. The final flight's recording instrument was one day old when the
flight flew.**
Supporting evidence: CL 23532 of 2026-09-08 added the network record channel;
sitting 15 flew on 2026-09-09; the channel opens before the first bank write
and the flight stopped inside the first write to the part.
Falsifying evidence would be an earlier changelist building the same channel
under another name, which part 06 owns and can supply.

**RED-F6. The work stopped for three full days immediately before the
endgame.**
Supporting evidence: zero changelists on 2026-09-04, 2026-09-05 and
2026-09-06; CL 22349 is the last of 2026-09-03 and CL 22355 the first of
2026-09-07.
Falsifying evidence would be work performed during those three days and landed
afterwards, which changelist dates cannot distinguish from work not performed;
part 09 owns the transcripts and can say whether the fleet ran.

**RED-F7. Every decision that shaped the last sitting was taken after both of
that day's flights had already failed.**
Supporting evidence: CL 22750 and CL 22854 are the two flights of 2026-09-07;
CL 23198, CL 23204 and CL 23259 are the composition and the rulings, and all
three carry higher changelist numbers than both flights.
Falsifying evidence would be a ruling recorded outside Perforce before the two
flights, which part 09's transcript corpus can supply.

## 02.8b What the record says: the endgame to the minute

Section 02.7 gives the endgame by changelist. The same changelists carry
timestamps, and the timestamps interleave with Damian's own turns closely
enough to change what the sequence means.

**Why the two clocks can be compared.** The changelist timestamps below come
from `p4 describe -s`, in the box's local time. The turn times come from
appendix C, `C-rulings-ledger.md`, which part 09 compiled from the
transcripts. The two are treated as one clock here for one reason only: the
rows interleave without contradiction, at 21:07, 21:10, 21:13, 21:16 and
21:17, and a clock offset of any size would break the interleaving. A reader
who finds an offset should read every conclusion in this section as suspect.

| time | source | event |
|---|---|---|
| 2026-09-07 15:12:46 | CL 22750 | Sitting 13 flies, image AC7399ED |
| 2026-09-07 17:32:02 | CL 22854 | Sitting 13 flies a second time, image CB1AE335; the medium dies at stage 9 |
| 2026-09-07 21:07:16 | CL 23193 | The sitting 14 card lands |
| 2026-09-07 **21:10** | appendix C | **Damian's ultimatum to root** |
| 2026-09-07 21:13:03 | CL 23198 | **"one sitting remains, the last sitting composed"** |
| 2026-09-07 **21:16** | appendix C | **Damian: "i am going to bed now"** |
| 2026-09-07 21:17:29 | CL 23204 | THE LAST SITTING's question set composed |
| 2026-09-07 21:38:23 | CL 23259 | The last sitting's ordering ruled, b3 early |
| 2026-09-08 02:05:16 | CL 23332 | `diag-sitting15.cfg` composed |
| 2026-09-08 02:07:12 | CL 23341 | Root's rulings in the card, a static address for the channel |
| 2026-09-08 03:54:32 | CL 23532 | The network record channel added to the ladder |
| 2026-09-08 06:48:18 | CL 23768 | The flight image certified, 47F29D50, 50 arms |
| 2026-09-08 06:50:17 | CL 23771 | THE LAST SITTING signed off |
| 2026-09-09 02:23:54 | CL 25176 | The last sitting has flown; the queue is closed |

### Three facts follow from the times, and each is arithmetic rather than reading

**First, the rule that one sitting remained was written three minutes after
the ultimatum that produced the rule.** Damian's turn is at 21:10 and CL
23198 is at 21:13:03.

**Second, the rule is a composition rather than a quotation.** Damian's words
at 21:10, quoted in full in appendix C, ask "so what's it going to be,
infinity hardware sittings with no progress or is one of you agents going to
level with me and produce a final and deteriminant test that doesn't actively
abuse me?". The words demand a determinant test and name the alternative as
infinity sittings. The words do not say that one sitting remains, and they do
not say for all time. Part 09 owns this finding and states the same
conclusion; part 02 records only that the ruling entered the depot at
21:13:03 as CL 23198 and that the turn it rests on is at 21:10.

**Third, every decision after 21:16 was taken while Damian slept.** He wrote
"i am going to bed now" at 21:16. The question set (21:17:29), the ordering
(21:38:23), the configuration (02:05:16), root's rulings on the card
(02:07:12), the network record channel (03:54:32), the certification
(06:48:18) and the sign-off (06:50:17) all carry later timestamps. Nine hours
and thirty-three minutes separate his last turn of the night from the
sign-off, and the fleet worked through all of it unattended.

## 02.8c What the record means, continued

**RED-F10. The last sitting was composed, ruled, instrumented, certified and
signed off in a single unattended overnight run, beginning one minute after
Damian went to bed.**
Supporting evidence: the table above; CL 23204 at 21:17:29 against the 21:16
turn; CL 23771 at 06:50:17.
Falsifying evidence would be a turn from Damian between 21:16 on 2026-09-07
and 06:50 on 2026-09-08, and appendix C carries no such turn; a reader can
falsify this finding by producing one from the transcripts.

**RED-F11. The ruling that fixed the number of remaining sittings at one
entered the depot three minutes after the turn it rests on, and the turn
contains no such number.**
Supporting evidence: appendix C, the 21:10 turn of 2026-09-07, quoted above;
CL 23198 at 21:13:03 titled "one sitting remains, the last sitting composed".
Falsifying evidence would be an earlier turn in which Damian sets the number,
or a later turn in which he confirms the number; part 09 and appendix C carry
the corpus in which such a turn would be found.

## 02.9 What the record says: the rules that governed metal

The charter assigns part 02 every revision of `CLAUDE.md` and of
`docs/PM/CurrentPlan.md` that ruled on metal, the sitting, the box, the board
or the bed. The measurement below was taken with `p4 annotate -c`, which
attributes every surviving line of a file to the changelist that introduced
the line.

**The instrument's one limit, stated before the result.** `p4 annotate`
reports only lines that survive in the current revision. A rule that was
written and later deleted is invisible to the instrument. Every claim below
about what a file DOES carry is therefore sound, and every claim about what a
file NEVER carried is checked a second way, against the full revision range.

### CLAUDE.md carries no rule about a hardware sitting

`CLAUDE.md` is loaded into every session's context by the harness, therefore
a rule written there governs every lane on every session without being sought
out. The file holds 686 lines at main CL 25190.

Seventeen of those 686 lines contain the word metal, sitting, box, board, bed,
ASUS or stick, and six changelists introduced all seventeen:

| CL | date | what the changelist did |
|---|---|---|
| 7663 | 2026-07-13 | Normalised line endings; carries the four "bare metal" lines about the compiler being a fixed point of itself |
| 21718 | 2026-09-02 | Operational docs tuned to the prompting guide; one line naming the box in the wait rule |
| 21725 | 2026-09-02 | War stories out, rules kept; one line naming bare-metal allocation |
| 22068 | 2026-09-02 | The box and main rule, from Damian's words |
| 22115 | 2026-09-02 | R-GATE head made consistent with the `-Internal` ban |
| 24954 | 2026-09-08 | The box rule aligned with `CoordinationProtocol` |

**Every one of the seventeen lines uses the words in one of two senses, and
neither sense is a hardware flight.** The four lines from CL 7663 and the one
from CL 21725 say "bare metal" meaning the compiler's own target, which is
codex-vm on x86-64 with no operating system. The remaining twelve say "box"
meaning the development machine and the memory a guest may take.

The check by the second route is decisive. The current `CLAUDE.md` contains
the word "sitting" zero times, "ASUS" zero times, "stick" zero times,
"flight" zero times and "flew" zero times. Across the whole revision range,
`p4 grep -i -e sitting //Codex/main/CLAUDE.md#1,#134` returns exactly one
line, in revision 134, and that line names `HardwareSitting` only as one of
four reference documents a lane should home a finding into.

**Therefore, across 134 revisions and 146 days, the document that every
session reads never carried a rule about flying the board.** Every rule
governing a flight lived somewhere a lane had to go and find.

### CurrentPlan's own history, recovered by tool

Section 02.9's original statement was that a day-by-day reconstruction of the
register was possible only by printing 1,089 revisions and was therefore not
attempted. The reconstruction has since been done by tool rather than by hand:
`build/p4-metal-diffs.ps1` walks every revision, diffs each against the one
before, and keeps the added and deleted lines that match the subject pattern.
Appendix E, `E-currentplan-metal-ledger.md`, is the output, and the three
results that change what this part says are below.

**First, metal was in the register from the project's first changelist.**
Revision 1 is CL 2 on 2026-04-17, the initial import, and the register already
carried "Hand someone a USB stick, they boot it", "compile-on-stick", "Codex
kernel-mode VMX entry on bare metal", and "**Real hardware boots** on Asus +
Dell UEFI x86-64". The USB stick and the ASUS are in the register on the day
Perforce opens, therefore neither is a later turn in the project.

**Second, June 2026 is empty.** The register carries 80 matching lines in
April, 107 in May, **zero in June**, 91 in July, 1,708 in August and 913 in
the nine days of September. June is not thinly covered; June is silent, and
section 02.5 gives the mechanism, which is that the register received 6
revisions that month against 526 in August.

**Third, the word "sitting" enters the register on 2026-05-03**, at CL 780,
and the line that introduces the word is a question about the human: "**2.
When is the hardware sitting?** R6 needs a human body and it is the". The
register names the human cost in the same line in which the register first
names a sitting.

### CurrentPlan cannot serve as the history of its own rulings

`docs/PM/CurrentPlan.md` carries 1,089 revisions and is the fleet's only
cross-lane register of open work. The current file holds 502 lines at main CL
25190, and 23 of those lines name a sitting, the ASUS, the stick, metal, a
flight or the board. Eight changelists introduced those 23 lines, and the
dates cluster hard: one on 2026-08-31, one on 2026-09-07, one on 2026-09-08
and four on 2026-09-09.

The clustering is not evidence that the register ignored metal until the last
days. The clustering is a consequence of R-HISTORY, the rule that a register
row is REPLACED in place rather than appended to. A register written under
that rule keeps no history of itself, therefore `p4 annotate` over the current
revision can only ever show the most recent rewrite of each row.

The revision stream tells the other half. Of the 1,089 changelists that
revised `CurrentPlan.md`, 68 name `HardwareSitting`, a numbered sitting, the
ASUS, the stick, a flight or a flying: 9 in July 2026, 48 in August 2026 and
11 in the nine days of September 2026.

**The consequence for this investigation is a limit on the evidence, and the
limit is worth stating plainly.** The register that held the fleet's open work
about metal destroys its own previous state by design. Reconstructing what
`CurrentPlan.md` said about metal on any given past day requires printing that
day's revision, and 1,089 revisions exist. Part 02 has measured the revision
stream and the current text; a day-by-day reconstruction of the register's
metal rows is not attempted here and is named in section 02.10 as absent.

## 02.10 What the record means, continued

**RED-F8. The document loaded into every session's context never carried a
rule about flying the board.**
Supporting evidence: 17 of 686 lines in `CLAUDE.md` contain a metal word and
all 17 use the word in the sense of the compiler's target or the development
machine; the words sitting, ASUS, stick, flight and flew occur zero times in
the current file; one occurrence of `HardwareSitting` exists across all 134
revisions and names a reference document rather than a rule.
Falsifying evidence would be a rule about flights in `CLAUDE.md` written under
vocabulary the search did not cover, for example "the part", "the machine" or
"the hardware"; a reader can test the finding by searching those words.

**RED-F17. The register carried the USB stick, the ASUS and real-hardware
booting from the project's first changelist, therefore the medium and the
board were the plan from 2026-04-17 rather than a later turn.**
Supporting evidence: appendix E, revision 1, CL 2, 2026-04-17, five quoted
lines.
Falsifying evidence would be a reading of those lines as aspiration rather
than plan, which the lines themselves cannot settle; a reader should weigh
them against part 09's record of what Damian asked for.

**RED-F18. The register said nothing about metal for the whole of June 2026.**
Supporting evidence: appendix E's monthly table, zero matching lines in June
2026 against 107 in May and 91 in July.
Falsifying evidence would be June metal work recorded under vocabulary the
pattern does not match, and appendix E states that limit as its second one.

**RED-F19. My own published figure for when the register began was wrong for
three landings, and the charter carries the same error.**
Supporting evidence: `p4 changes` reports CL 1322 on 2026-05-11 for the path;
CL 1322 is the move; CL 2 on 2026-04-17 added the file at `docs/CurrentPlan.md`;
`p4 filelog` reports 1,090 revisions from 2026-04-17. Section 02.5 carries the
correction.
Falsifying evidence would be that CL 2's file is a different document rather
than the same register moved, which `p4 filelog` settles by reporting one
continuous revision chain.

**RED-F9. The fleet's only cross-lane register of open work is by design
incapable of carrying the history of its own rulings.**
Supporting evidence: R-HISTORY requires a row be replaced in place; 1,089
revisions exist; `p4 annotate` over the current revision attributes 23 metal
lines to 8 changelists, of which 4 are from the last day.
Falsifying evidence would be a second register that preserved the rulings, and
`docs/Agents/CoordinationProtocol.md` together with part 09's rulings ledger
are where such a register would be found.

## 02.11 What the record says: the pre-Perforce era, and the hardware that was not the ASUS

The 34 days from 2026-03-14 to 2026-04-16 leave no changelist. What survives
is 12 files in `docs/PM/Done/Plans` and 18 in `docs/PM/Done/Handoffs`. Four of
the twelve are dated snapshots of the plan itself, two from 2026-03-24 and two
from 2026-03-26, and those four carry the era's own words about hardware.

**The reading changes the shape of the whole timeline, therefore the evidence
is quoted rather than summarised.**

### In March 2026 the phrase "bare metal" meant an emulator

`CurrentPlan-2026-03-26-evening.md` line 9 describes the project as "The
self-hosted Codex compiler -- running on bare metal x86-64 under QEMU". Line
41 of the same file names the test as "QEMU bare metal test". Line 121 lists
the future item "Codex.OS on real hardware | WHPX (Nut's box), then actual
boot device".

Therefore, in the era before Perforce, three things were named separately and
were not confused with one another: bare metal meaning a target with no
operating system, QEMU and WHPX meaning the machines that ran the target, and
"actual boot device" meaning physical hardware that had not yet been reached.

The distinction matters to this investigation because `CLAUDE.md` today
carries four lines about "bare metal" (section 02.9) and every one of the four
uses the March sense, which is the compiler's target rather than the ASUS
board.

### The first physical hardware target was a phone

`CurrentPlan-2026-03-24-peak2-complete.md` line 23 records "Phone hardware
ready (2026-03-23). Samsung SM-G935T (T-Mobile S7 Edge) backed up, SIM
removed, OEM unlock enabled, Odin connected." Line 286 of the same file names
the goal: "Phone Phase 3 -- Codex.OS: ARM64 bare metal on the phone. No Linux.
The summit."

`CurrentPlan-2026-03-24-peak3-evening.md` lines 37 to 39 record the state of
that campaign nine days into the project: "Bootloader signature verification
blocking all custom recovery images. 9 flash attempts documented. Official
TWRP downloaded. USB state diagnosis in progress."

**Nine flash attempts against a phone were already documented on 2026-03-24**,
which is day 11 of the project.

**A correction, and the correction recovers a document.** The first landing of
this section said the attempt log, named in the plan as
`docs/Active/Projects/PHONE-WIPE.md`, "does not exist in the depot today". The
log exists. The file moved four times and now sits at
`docs/PM/Done/Suspended/Phone/PHONE-WIPE.md`, and `p4 filelog -i` traces it to
CL 2 of 2026-04-17, the initial import. Six phone documents survive beside it:
`CODEX-PHONE.md`, `TWRP-BUILD-HANDOFF.md`, `TWRP-SESSION-HANDOFF.md`,
`EMULATOR-PLAN.md` and `ARM64-QEMU-VERIFICATION.md`. The claim of absence was
made by searching the path the March plan named rather than by following the
file, which is RED-F20's error a fourth time.

### What the recovered phone log says, and why it belongs in this report

`PHONE-WIPE.md` is 264 lines and is dated 2026-03-24 in its own header. Two
things in it bear directly on the accident five and a half months later.

**First, the log carries a verification table whose last row is the accident's
own shape.** Every row above the last is a bed result marked proven: the boot
image header matches the Samsung specification, the `dt_size` field is right,
the trailer is present, the pages align, the header byte-matches a known-good
image, and ARM64 Codex binaries run on the Android emulator. The last row
reads:

> **This image actually boots on an S7 Edge** | NOT PROVEN -- cannot be
> emulated

**On day 11 of the project the fleet had already written down, in one table,
that everything checkable in a bed was green and that the one question that
mattered could not be answered in a bed.** That is the same sentence sitting 15
proved with 50 of 50 rehearsal arms green (REEK-F4, FESTER-G14, ROOT-A2).

**Second, the nine attempts were a controlled series that reached a
conclusion.** The log's "Flash Attempt Log (2026-03-24)" tables nine attempts
by change and result, and attempt 9 flew an official TWRP image built by
TWRP's own continuous-integration system. The log's reading of that attempt is
explicit: "Attempt 9 is conclusive. An official TWRP image built by TWRP's own
Jenkins CI also fails at `RQT_CLOSE`. This eliminates our hand-packed image as
the cause." The cause is then named as the bootloader enforcing signature
verification, with the observation that the `OEM Unlock` toggle "only *permits*
unlocking, it doesn't *perform* it".

The nine attempts therefore ended in a diagnosis with a control, and the
campaign was suspended rather than abandoned in confusion. Part 03 owns the
undocumented era's count and should read this file before pricing the era.

### The switch to the ASUS is datable, and the switch itself is not recorded

The first changelist naming the ASUS is **CL 1104 on 2026-05-07**: "PE stub
stack alignment (sub rsp 64->40) + ImageBase 0x10000000->0 for real hardware
UEFI boot. Asus firmware rejec[ts]". The first changelist touching
`tools/codex-vm.c` is CL 1154 on the same day, 2026-05-07.

No changelist in the 7,220 on the main stream names Odin, Samsung or the
SM-G935T. The phone campaign therefore lived entirely in the era Perforce
does not hold, and no changelist records the decision to stop flying a phone
and start flying an ASUS.

### What the pre-Perforce era means for Damian's count

Damian states in the charter that the fleet held "over 120" sittings across
"3 months or so" before the record began. Part 03 owns the reconstruction.
Part 02 contributes three dated anchors that bound the reconstruction:

- The project began 2026-03-14.
- Nine flash attempts against the phone were documented by 2026-03-24, and
  the log naming them is gone from the depot.
- The ASUS enters the depot on 2026-05-07, and the flight record begins on
  2026-07-28, which leaves **82 days** in which the ASUS was being flown with
  no flight record and no changelist path dedicated to recording a flight.

## 02.12 What the record means, continued

**RED-F12. The phrase "bare metal", which `CLAUDE.md` still uses in four
places, was coined in March 2026 to mean a target with no operating system
running under QEMU, and never meant the ASUS board.**
Supporting evidence: `CurrentPlan-2026-03-26-evening.md` lines 9, 41 and 121;
the four surviving `CLAUDE.md` lines identified in section 02.9.
Falsifying evidence would be a document in which "bare metal" plainly denotes
the physical board; part 05 and part 07 read the hardware corpus and can
supply one.

**RED-F13 (corrected). The project's first physical-hardware campaign was
against a phone, reached nine documented flash attempts by day 11, and left no
changelist; its attempt log DOES survive, under a path four moves from the one
the March plan names.**
Supporting evidence: `CurrentPlan-2026-03-24-peak2-complete.md` line 23;
`CurrentPlan-2026-03-24-peak3-evening.md` lines 37 to 39; zero changelists
naming Odin, Samsung or SM-G935T among 7,220;
`docs/PM/Done/Suspended/Phone/PHONE-WIPE.md`, 264 lines, traced by
`p4 filelog -i` to CL 2 of 2026-04-17.
**The published form of this finding asserted that no attempt log survived.
That half was wrong, and the falsifier this finding itself named is what
refuted it.**

**RED-F22. The campaign document for the network part was opened 22 days
after the part was identified, and the identification was never in doubt.**
Supporting evidence: appendix A row 3, 2026-07-29, reads the part as
`8086:15b8` I219-V revision 31 with `MAP=ok` and its station address;
`docs/Designs/Active/OS/I219IsNotAnE1000.md` was added by **CL 18317 on
2026-08-20**, "Open the I219 campaign", taken with `p4 filelog -i`. The
interval is 22 days. blu found the date and routed it here; red verified it at
the source before using it.
Falsifying evidence would be an earlier design document about the part under
another name, which part 04 and part 07 read the design corpus and can supply.
**Read beside part 04 section 1.3:** the first recorded bed-against-part
disagreement is CL 12326 of 2026-07-30, one day after the part was identified
and 21 days before the campaign document existed, therefore the disagreements
were being found and fixed one at a time before anything gathered them.

**RED-F21. On days 10 and 11 of the project the fleet wrote down that every
bed-checkable property of its artifact was proven, rated the one unemulatable
step as merely medium risk on the strength of those same proofs, and the last
sitting failed in that shape five and a half months later.**
Supporting evidence, and the second document was found by blu after this
finding was first written:

- `PHONE-WIPE.md`, dated 2026-03-24, whose verification table proves every
  bed-checkable row and whose last row reads "This image actually boots on an
  S7 Edge | NOT PROVEN -- cannot be emulated".
- `docs/PM/Done/Suspended/Phone/phone/EMULATOR-PLAN.md`, dated **2026-03-23**,
  whose stated goal is "Validate everything in emulation before touching the
  real S7 Edge" and whose risk table at lines 119 to 126 rates QEMU ARM64,
  the Android emulator, `adb push`, building the boot image and inspecting it
  as risk **None**, and rates "Flash to phone" as **Medium**, mitigated by
  "Only after all validations". Every validation passed. The flash then failed
  nine times.
- REEK-F4, FESTER-G14 and ROOT-A2 for the same shape on 2026-09-09.

**The mitigation is the finding.** "Only after all validations" treats the
validations as reducing the risk of the one step the validations cannot reach,
and the nine failures are what that reasoning cost on day 11. BLU-F2a states
the same conclusion from the sittings ledger, independently derived.

Falsifying evidence would be a reading of the phone table as a routine caveat
rather than as the campaign's governing risk, which the log's own conclusion
resists: the campaign was suspended on the strength of that row.

**RED-F14. The ASUS was flown for 82 days before a flight record existed.**
Supporting evidence: CL 1104 of 2026-05-07 is the first changelist naming the
ASUS; `HardwareSitting.md` was created 2026-07-28 by CL 11775.
Falsifying evidence would be flights recorded elsewhere during those 82 days,
and part 03's reconstruction of the undocumented era is the search that would
find them.

## 02.13 What the record says: what each release cycle told the public about metal

The release notes are `docs/PM/Done/GitHubUpdates` (53 files) and
`docs/PM/Active/GitHubUpdates` (3 files), 56 files in total, numbered 2 to
57. **No file numbered 1 exists**, therefore a reader counting cycles from
the filenames counts 56 and not 57.

Each of the 56 was searched for the flight vocabulary. The distribution
across the series is the finding, because the series is the public record of
what the project claimed:

| updates | metal mentions each | reading |
|---|---|---|
| 2 to 33 | 0 to 6, and 12 of the 32 carry zero | Metal is barely mentioned for the first 32 cycles |
| 34 | 19 | The first cycle with a metal headline |
| 37 to 40 | 27, 14, 13, 11 | The sustained hardware period |
| 47 and 49 | 25 and 34 | The second and largest concentration |
| 50 to 57 | 3 to 10 | Declining, and the last cycle carries 4 |

### The two cycles that claimed metal succeeded

**A correction, and it is the third time the same trap has caught this part.**
The first landing of this section dated the updates with `p4 changes` over
their present paths and gave Update 34 as 2026-07-16 and Update 37 as
2026-08-05. Both figures are the dates of a MOVE. `p4 filelog -i` follows the
moves and gives the true dates below. The tell was visible in the first
landing and was not acted on: updates 47 and 49 were both dated 2026-08-25 at
the identical changelist 19755, and two different documents sharing one
changelist is a fingerprint rather than a coincidence (L-SUSPECT).

| update | added | first published under | previously stated here |
|---|---|---|---|
| 34 | 2026-07-08 | CL 7358 | 2026-07-16, CL 8450 |
| 37 | 2026-07-28 | CL 11669 | 2026-08-05, CL 13139 |
| 47 | 2026-08-17 | CL 16568 | 2026-08-25, CL 19755 |
| 49 | 2026-08-20 | CL 17850 | 2026-08-25, CL 19755 |
| 57 | 2026-09-08 | CL 24071 | 2026-09-08, CL 24073 |

**An added date is not a claim date, and the two differ here.** A cycle's file
can be added on one date and gain a claim on another, therefore each headline
below is dated by `p4 annotate -c -i`, which attributes the line itself.

**Update 34 made metal the headline, and the headline is in the file's first
revision, CL 7358 on 2026-07-08.** Line 11 reads "The headline: it boots on
metal". Line 13 reads "A 1 GB USB stick, flashed with `seed/Codex.img`, booted
on a real UEFI machine". Lines 65 and 66 record that "The image now boots on
an ASUS TUF (2015 AMI), a Dell Inspiron, and OVMF". All three lines carry CL
7358.

**Update 37 dated and named the flight, and the sentence entered the file
three days after the file did.** The file was added on 2026-07-28 by CL 11669;
the sentence at lines 266 to 268, "On 2026-07-29 a Codex payload ran on real
hardware: an ASUS TUF desktop, booted from a USB stick, no operating system
underneath it. The first rung of the ladder rendered its scene on the board's
own panel", carries **CL 12464 of 2026-07-31**. The claim therefore postdates
the flight it reports, which is the order a reader should expect and which the
file's own add date would have hidden.

The two headline sentences are narrow and, on their own terms, true: a payload
booted and a first rung rendered. **Update 34 does not stop at its headline.**
Lines 16 to 17 of the same file read "Confirmed on hardware: the first-boot
screen renders, the **PS/2 keyboard works**, and the whole wizard runs", and
line 29 has the payload reading "the real PS/2". That is a capability claim
about the board, and the flight record contradicts it: appendix A row 3 and
`HardwareSitting.md#31:6438` record "NO PS/2, zero arrivals either side of the
handback". BLU-F3 found the disagreement, part 03 owns it, and RED-F15 is
withdrawn and replaced below because of it.

### What the last cycle said

**Update 57, pushed 2026-09-08 (CL 24073), is the final public record before
sitting 15.** Four lines carry the flight vocabulary, and none of the four is
a claim about the board's capability. Line 78 records a rehearsal flake
closed. Line 102 records a removal on Damian's ruling. Line 154 records the
byte-identical seed check, which names `build-output/bare-metal` in the
compiler-target sense of section 02.11.

**Therefore the series made exactly one capability claim about the board that
the flight record contradicts, and made it early.** The series claimed a boot
and a working PS/2 keyboard on 2026-07-08, claimed a first rung on 2026-07-31,
and claimed nothing further about the board's capability through the remaining
cycles to 2026-09-08. No later update retracts the keyboard claim.

## 02.14 What the record means, continued

**RED-F15 IS WITHDRAWN AND REPLACED. The published form said "no cycle
overclaimed the board", and the published form is false.** Update 34 states,
at lines 16 to 17, "Confirmed on hardware: the first-boot screen renders, the
**PS/2 keyboard works**, and the whole wizard runs", and line 29 has the
payload reading "the real PS/2". The flight record contradicts the claim:
`HardwareSitting.md#31:6438` and appendix A row 3 record "NO PS/2, zero
arrivals either side of the handback". BLU-F3 found the disagreement and
BLU-F3 is right. My error was to read the two headline sentences of updates 34
and 37 and to generalise "no overclaim" across 56 files from them, which is a
claim about a corpus made from a sample of two (L-GAP: ask what the search
cannot express before reading its silence as agreement).

**RED-F15 (replacement). The public release series made one capability claim
about the board that the flight record contradicts, and the contradiction sat
unreconciled for the rest of the project.**
Supporting evidence: Update 34 lines 16 to 17 and line 29, all carrying CL
7358 of 2026-07-08; `HardwareSitting.md#31:6438`; appendix A row 3; no later
update retracts or revises the keyboard claim, which the mention profile of
updates 35 to 57 supports and which a reader can check directly.
Falsifying evidence would be a retraction in any update after 34, or evidence
that the machine of Update 34 and the machine of the flight record are
different machines, which BLU-F3 names as its own falsifier and which neither
document resolves.

**RED-F16. Metal was absent from the public record for the project's first
32 cycles, and the first metal headline came on 2026-07-08, nearly four months
after the start.**
Supporting evidence: updates 2 to 33 carry 0 to 6 mentions each and 12 of the
32 carry zero; Update 34 was added by CL 7358 on 2026-07-08 and its headline
lines carry that same changelist under `p4 annotate -c -i`.
Falsifying evidence would be metal work reported in those cycles under other
vocabulary, for example "the target", "the device" or "the machine".
**This finding was published with the date 2026-07-16, which was the date of a
later move of the file rather than the date of the claim.** BLU-F4 carries
2026-07-08 and BLU-F4 is right.

**RED-F20. Three of the dated figures this part published were the dates of a
file MOVE rather than of the content, and the same mistake produced all three.**
Supporting evidence: `CurrentPlan.md` given as 2026-05-11 when the file was
added 2026-04-17 (section 02.5); Update 34 given as 2026-07-16 when the file
and its headline were added 2026-07-08; Update 37 given as 2026-08-05 when the
file was added 2026-07-28 and its flight sentence entered 2026-07-31. In every
case `p4 changes` over the present path was used where `p4 filelog -i` was
needed.
Falsifying evidence would be a demonstration that `p4 changes` over a path
does report pre-move history, which the three cases above each refute.

## 02.15 Still to be written

Every subject the charter assigns to part 02 is written. Two pieces of work
are deliberately not attempted here, and each is named so that a reader treats
the absence as a decision rather than as an oversight.

- **A day-by-day reconstruction of what `CurrentPlan.md` said about metal** is NO LONGER absent. `build/p4-metal-diffs.ps1` produced it and appendix E carries it; sections 02.5 and 02.9 are corrected and extended from it.
- **The count of flights in the undocumented era.** Section 02.4 and section
  02.11 give the dated anchors that bound the count. The count itself is part
  03's corpus.

Appendix B, `B-cl-ledger.md`, is written and complete at 675 changelists.

## 02.16 Coverage

Every command below was run from `D:\Projects\Cobblestone-red` against
`//Codex/main` on 2026-09-09 between 03:05 and 03:30.

```
p4 changes -l //Codex/main/...                          -> 7,220 changelists
p4 changes -l //Codex/main/codex/os/kernel/...          -> 134
p4 changes -l //Codex/main/codex/os/net/...             -> 149
p4 changes -l //Codex/main/build/boot/...               -> 196
p4 changes -l //Codex/main/tools/codex-vm.c             -> 240
p4 changes -l //Codex/main/docs/Hardware/...            -> 91
p4 changes -l //Codex/main/seed/...                     -> 906
p4 changes -l //Codex/main/CLAUDE.md                    -> 134
p4 changes -l //Codex/main/docs/PM/CurrentPlan.md       -> 1,089
p4 filelog -i //Codex/main/docs/Hardware/HardwareSitting.md
p4 annotate -c //Codex/main/CLAUDE.md                   -> 686 lines
p4 annotate -c //Codex/main/docs/PM/CurrentPlan.md      -> 502 lines
p4 grep -i -e sitting //Codex/main/CLAUDE.md#1,#134     -> 1 line, revision 134
p4 describe -s <CL>                                     -> the timestamps of section 02.8b
p4 filelog //Codex/main/docs/PM/CurrentPlan.md           -> 1,090 revisions from 2026-04-17
pwsh build/p4-metal-diffs.ps1 -Out <file>                -> appendix E, 2,899 matching lines
p4 filelog -i <every cited doc>                          -> the move audit: 357 of 366 docs have moved
```

The whole changelist log was parsed once into records of changelist number,
date and flattened description, and every count in sections 02.2, 02.3, 02.7
and 02.8 was taken from those records rather than from a second `p4` call, so
that every count in this part shares one corpus:

```
p4 changes -l //Codex/main/... > all-cls.txt      (45,378 lines, 7,220 blocks)
regex over all-cls.txt: ^Change (\d+) on (\d{4}/\d\d/\d\d) ... to end of block
```

The four discriminators of section 02.3 were each applied to the flattened
description field of those records. The line count of 217 was taken twice: once
by `Select-String` over `all-cls.txt` without grouping, and once by walking the
file and attributing each matching line to the changelist block containing the
line. The two agreed at 217 lines over 182 changelists, with 19 changelists
carrying more than one line.

Files read in full for this landing:

| path | revision | size |
|---|---|---|
| `docs/PM/Active/Stories/TheLostParadise.md` | main CL 25188 | 118 lines |
| `CLAUDE.md` | red workspace at CL 25190 | project instructions |

Files read in full for the pre-Perforce section (02.11):

| path | size |
|---|---|
| `docs/PM/Done/Plans/CurrentPlan-2026-03-24-peak2-complete.md` | 17,319 bytes |
| `docs/PM/Done/Plans/CurrentPlan-2026-03-24-peak3-evening.md` | 4,922 bytes |
| `docs/PM/Done/Plans/CurrentPlan-2026-03-26-evening.md` | 5,997 bytes |
| `docs/PM/Done/Plans/CurrentPlan-2026-03-26-morning.md` | 7,040 bytes |

The remaining 8 files of `docs/PM/Done/Plans` and the 18 of `docs/PM/Done/Handoffs` were listed and searched for the flight vocabulary; the four above are the only ones that returned a hit, and only those four were read in full.

**What this part did not read, and why.**

- **The 1,089 revisions of `CurrentPlan.md` and the 134 of `CLAUDE.md` were
  not printed one by one.** Both files were measured with `p4 annotate -c`
  over the current revision and, for `CLAUDE.md`, with `p4 grep` over the full
  revision range. Section 02.9 states the limit that follows: a rule written
  and later deleted is invisible to annotate, which is why the `CLAUDE.md`
  claim is checked a second way and the `CurrentPlan.md` claim is stated as a
  limit rather than as a result.
- **The 56 GitHubUpdates were searched rather than read in full.** Every one
  of the 56 was searched for the flight vocabulary; updates 34, 37 and 57 were
  read at their matching lines and are quoted by line number. The other 53
  were not read in full.
- **The transcripts were not read.** The transcripts are part 09's corpus.
  Part 02 quotes appendix C, which part 09 compiled, and section 02.8b states
  the one assumption that quotation rests on.
- **`HardwareSitting.md` was not read.** The file in full is part 03's corpus.
  Part 02 reads only the file's Perforce history through `p4 filelog`.
- **`docs/Designs/` was not read.** The 272 design files are the corpus of
  parts 04 through 07.










