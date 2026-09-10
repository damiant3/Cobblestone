# Appendix D. The lessons against the record

*Owner: val. Appendix to `docs/PM/Active/Stories/TheLostParadise.md`. Every
count below carries the date of measurement and the revision measured
(L-COUNT). Written under the three CPL axioms and R-DASH.*

## D.0 The instrument, and what the instrument can answer

`docs/PM/Active/Stories/LESSONS.md` is the fleet's index of hard-won
lessons. Each row carries four columns: a stable id, the lesson in one
sentence, the evidence the lesson was paid for, and a last column naming the
runner that enforces the lesson. The index states the purpose of the last
column in the index's own words: "**A lesson with no runner is a lesson
nobody is enforcing.** The last column says which have one. Most do not, and
that is the honest state rather than a gap to be embarrassed about"
(`//Codex/main/docs/PM/Active/Stories/LESSONS.md#70:35-38`, change 25102).

The index therefore answers two questions mechanically, with no
interpretation: the count of lessons enforced by a program, and the set of
lessons the metal campaign paid for.

## D.1 What the record says: the enforcement census

**Measured 2026-09-09 against `LESSONS.md#70` (change 25102): the index
carries 80 rows with 80 distinct ids.** The charter of this investigation
records 73 ids, measured earlier the same day; the index grew between the
two measurements, and the number to carry forward is 80 at revision 70.
Re-measure before quoting either number again (L-COUNT).

| the last column says | rows |
|---|---:|
| a runner exists | 20 |
| partial | 5 |
| `none` | 46 |
| `none (candidate: ...)`, a runner proposed and not built | 9 |
| **total** | **80** |

**55 of 80 lessons, 69 per cent, are enforced by nothing**, counting the 46
bare `none` rows and the 9 rows carrying a proposed runner nobody built.

## D.2 What the record says: the lessons the metal campaign paid for

A lesson is counted here as metal-born when the evidence column names a
flight, a stick, a board, a bed against metal, or `HardwareSitting.md`. The
census names the stories `TheSilentKeyboard`, `TheKeyboardWasNeverSilent`,
`TheBedThatAlwaysSaidYes`, `TheShotThatWorkedOnce`, `TheStickDidNotBoot`,
`TheSecondStick`, `TheImageThatWasTwoDaysOld`, `ProseHasNoRunner`,
`TheUnwovenStair`, and the files `HardwareSitting.md`, `DiagB3.codex` and
`HardwareBringUpPlaybook.md`.

**22 of the 80 lessons are metal-born. Three carry a runner, one is partial,
and 18 are enforced by nothing.**

| id | enforcement | the evidence column |
|---|---|---|
| L-FALSIF | partial | TheSilentKeyboard, ExaminersAssay |
| L-CHANNEL | none | TheSilentKeyboard |
| L-FALLBACK | none | TheSilentKeyboard |
| L-HUMAN | none | TheSilentKeyboard |
| L-REFEREE | none | TheUnwovenStair |
| L-BODY | none | ProseHasNoRunner, TheStickDidNotBoot, TheSecondStick, TheImageThatWasTwoDaysOld |
| L-MISROUTE | none | TheSecondStick |
| L-INTERRUPT | runner | ProseHasNoRunner |
| L-ARTIFACT | none | TheStickDidNotBoot |
| L-SUCCESS | none | TheKeyboardWasNeverSilent |
| L-OPTIONAL | none | TheKeyboardWasNeverSilent |
| L-STATES | none | TheKeyboardWasNeverSilent |
| L-IDLE | none | TheKeyboardWasNeverSilent |
| L-UNCALLED | none | TheKeyboardWasNeverSilent, ProseHasNoRunner |
| L-PUBLISHED | none | ProseHasNoRunner |
| L-SIDECAR | none | TheImageThatWasTwoDaysOld |
| L-GREEN | none | TheShotThatWorkedOnce |
| L-ROUTE | none | TheShotThatWorkedOnce |
| L-BANK | none | HardwareSitting.md msc-align entries, red CL 13355 |
| L-FREEDOM | none | TheBedThatAlwaysSaidYes |
| L-REHEARSE | runner | TheBedThatAlwaysSaidYes |
| L-BEDTRUE | runner | DiagB3.codex, "ip= IS REQUIRED" |

## D.3 What the record says: the index records recurrence in its own column

**One lesson's evidence column names four separate events.** L-BODY reads
"The most expensive step in the plan must not be protected by the weakest
guard in the tree. If spending a human is gated only by prose, it is not
gated", and the evidence column names `ProseHasNoRunner`,
`TheStickDidNotBoot`, `TheSecondStick` and `TheImageThatWasTwoDaysOld`
(`LESSONS.md#70:73`). The last column of that row reads `none`.

**Two further metal-born lessons name two events each**: L-UNCALLED names
`TheKeyboardWasNeverSilent` and `ProseHasNoRunner`; L-FALSIF names
`TheSilentKeyboard` and `ExaminersAssay`.

## D.4 What the record means

**VAL-D1. The campaign that cost the most produced the lessons least likely
to be enforced.** Across the whole index 25 per cent of lessons carry a
runner; across the 22 metal-born lessons 14 per cent carry one. The
direction is the finding: a lesson learned inside a text editor was more
likely to end as a program than a lesson learned on the machine, and the
machine was the scarce resource.

Falsifying evidence would be a runner existing for any of the 18 rows above
and the last column being stale. A reader who finds one corrects the row and
this count.

**VAL-D2. The lesson naming the human step is unenforced, and the lesson
saying that an unenforced human step is not gated is also unenforced.**
L-HUMAN reads "A step requiring a human body is the most expensive line in
the plan" with the last column `none` (`LESSONS.md#70:66`). L-BODY says a
human step gated only by prose is not gated, with the last column `none`
(`LESSONS.md#70:73`). The index states the failure and the index does not
close the failure, and the record of four separate events sits inside the
row that states the failure.

**VAL-D3. The index observed the recurrence and the recurrence continued.**
L-BODY's evidence column grew to four events. Growth of an evidence column
is the fleet noticing a repeat; the last column staying at `none` across
that growth is the fleet declining to build the thing that would stop the
repeat. Nothing in the fleet's procedure converts a second occurrence into
an obligation to write a runner: the index's own "How to use it" section
asks for a runner "When a lesson becomes mechanically checkable" and names
no trigger, no owner and no deadline (`LESSONS.md#70:145-152`).

## D.5 The dated cross-reference: which lesson was written after which
flight, and which later flight repeated the lesson

### What the record says

**Every story is dated by `p4 filelog -i`, which answers the file's first
revision, rather than by `p4 changes` over the file's present path, which
answers a MOVE.** The stories have moved at least once as a set:
`ProseHasNoRunner.md`'s own history carries change 14155 of 2026-08-08,
"Rename/move file(s)", against a first revision of change 12046 on
2026-07-29. A date taken over the present path therefore reads 2026-08-08
for a document written on 2026-07-29. No count of how many of the eight
moved is published here, because the instrument that would count them also
reports branches into other streams and cannot separate the two.

| story | first revision | date | the boots the story accounts for (part 03.3b) |
|---|---|---|---|
| `TheUnwovenStair` | change 7458 | 2026-07-10 | not in 03.3b's set |
| `TheSilentKeyboard` | change 7482 | 2026-07-13 | S1 to S4 |
| `TheSecondStick` | change 12042 | 2026-07-29 | S5, S6 |
| `TheStickDidNotBoot` | change 12043 | 2026-07-29 | S5 (blu's part 03.5) |
| `ProseHasNoRunner` | change 12046 | 2026-07-29 | S7 |
| `TheImageThatWasTwoDaysOld` | change 12047 | 2026-07-29 | S8 |
| `TheKeyboardWasNeverSilent` | change 12592 | 2026-08-02 | S1 to S4, re-read |
| `TheShotThatWorkedOnce` | change 13953 | 2026-08-07 | S9 to S11 |
| `TheBedThatAlwaysSaidYes` | change 15039 | 2026-08-14 | S12 to S17 |

**Four recurrences are visible in the record, each with the days between
the writing and the repeat:**

| lesson | written | the flight that earned it | the later boot that repeated it | days |
|---|---|---|---|---:|
| L-CHANNEL | 2026-07-13 | S4, the probe that ran all three phases and came home byte-identical | S7, a sitting whose whole return was "stick doesn't boot", one bit, no photograph (`ProseHasNoRunner.md:45`) | 16 |
| L-HUMAN | 2026-07-13 | S1 to S4, six-plus flash-walk-boot-photograph cycles | S6, a second flash cycle spent on a deterministic tool defect (`TheSecondStick.md:441-446`); then S12, about twenty misfire flashes across four days (`TheBedThatAlwaysSaidYes.md:8-11`) | 16, then 28 |
| L-ARTIFACT | 2026-07-29 | S5, gate the artifact you ship rather than a sibling | S12 and S13, three dead images carrying `-EntryStart`; the later story writes the extension itself, "gate the artifact's WHOLE MISSION, not its boot" (`TheBedThatAlwaysSaidYes.md:120-124`) | 16 |
| L-FALSIF and L-ORACLE | 2026-07-13 onward | the bed that could not reproduce the failure | S12's own decoy arm, which the story calls VACUOUS because OVMF happened to order the stick's raw disk first, so the arm never reached the fix (`TheBedThatAlwaysSaidYes.md:86-97`) | 32 |

**One metal-born lesson acquired a runner, and the gap is six days.**
L-REHEARSE was written on 2026-08-14 with `TheBedThatAlwaysSaidYes`, and the
refusal landed on 2026-08-20 as change 18229, "flash-usb refuses an
unrehearsed hash by DEFAULT; override split from -Force as
-UnrehearsedAnyway". That story had itself recorded the runner as a
candidate rather than a build: "Not built; recorded here so the row's last
column can stop saying none some day" (`TheBedThatAlwaysSaidYes.md:157-161`).

**One proposed lesson never entered the index.** `TheSecondStick.md:516`
proposes L-BLAME in the index's own table shape: "A procedure must not tell
the operator what a tool's failure MEANS unless that tool's own failure
modes have been exercised." Searched at head, the string `L-BLAME` occurs in
exactly two places in the whole tree, both inside `TheSecondStick.md` itself
(`:516` and `:538`). The lesson was proposed on 2026-07-29 and is absent
from `LESSONS.md` 41 days later.

**One row of the index cites a changelist that does not exist.** L-BANK's
evidence column reads "`docs/Hardware/HardwareSitting.md` msc-align entries,
red CL 13355" (`LESSONS.md#70:91`). `p4 describe -s 13355` answers "no such
changelist", and the same command answers normally for change 12046, and
changes 13359 and 13360 of 2026-08-05 exist on either side of the gap. The
number therefore names no submitted change.

### What the record means

**VAL-D4. The metal lessons were written promptly and repeated inside three
weeks.** The four recurrences above are 16, 16, 16 and 32 days after the
lesson was written down, and in three of the four the repeat was committed
by a different lane than the one that wrote the lesson. A row in an index
read at session start did not prevent any of them.

**VAL-D5. The one metal lesson that acquired a runner acquired it six days
after being written, and the runner exists because a person built it rather
than because the procedure required one.** The story that proposed it
recorded the runner as a candidate and said the row could "stop saying none
some day". Nothing in the fleet's procedure converts that sentence into
work, which is VAL-F4 in part 08 measured on a second corpus.

**VAL-D6. The index cannot be trusted as a citation instrument without
checking, and two of its rows prove it.** L-BLAME is a lesson a story
proposed and the index never gained, therefore the index under-reports what
the fleet learned. L-BANK's evidence cites a changelist that was never
submitted, therefore a reader chasing that row's evidence finds nothing and
cannot tell a bad citation from a deleted change. Both are the shape
L-ROWROT names, in the file that carries L-ROWROT.

Falsifying evidence for VAL-D6 would be a change 13355 on another server or
in another depot, or an L-BLAME row in a revision of `LESSONS.md` other than
the head. The positive control above establishes that the instrument
resolves changelists that exist.

## D.C Coverage of this appendix so far

| path | revision | what was read |
|---|---|---|
| `//Codex/main/docs/PM/Active/Stories/LESSONS.md` | #70 (change 25102) | every row, in full |

Extraction run:

```powershell
p4 print -o <scratch>\LESSONS-main.md //Codex/main/docs/PM/Active/Stories/LESSONS.md
# rows split on '|', classified by the last column
```

Not yet done, and named rather than left silent: reading each of the 22
metal-born stories in full to establish WHICH flight each lesson was written
after and WHICH later flight repeated the lesson. The counts above rest on
the index's own evidence column, which names events without dating them. The
dated cross-reference is the next section of this appendix, and the sittings
ledger blu owns (part 03) is the instrument that dates each event.
