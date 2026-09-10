# Part 08. The organization as cause

*Owner: val. Part of `docs/PM/Active/Stories/TheLostParadise.md`, the
accident investigation ordered by Damian on 2026-09-09 at 02:55. Every claim
below carries a citation. Facts are separated from interpretation: each
section states what the record says, then what the record means. Written
under the three CPL axioms and R-DASH.*

## 08.0 What this part asks

Part 04 asks what the network card did. Part 07 asks what the beds modelled.
Part 08 asks a different question: given a fleet of six agents, a rulebook
loaded into every session, a lessons index, a commander, a handoff
procedure and twelve memory directories, what did the ORGANIZATION do that
produced a kernel unable to survive the first write to the part after five
and a half months.

The question is not whether the agents worked hard. The question is whether
the machinery the fleet built to govern itself pointed any agent at the
flight, at any moment, before the flight flew.

## 08.1 The rules about metal, and where the rules lived

### What the record says

**`CLAUDE.md` is the one document every session loads.** The document states
the fact in the sentence that closes the document: "This file is loaded into
every session's context by the harness"
(`//Codex/main/CLAUDE.md#134:680`, change 24954, 2026-09-08).

**Across all 134 revisions of `CLAUDE.md`, from revision 1 (change 2,
2026-04-17) to revision 134 (change 24954, 2026-09-08), the document never
contains the word `flight`, the word `flies`, the word `flown`, the word `flew`, the
word `stick`, the word `board`, the string `rehears`, or the phrase `the
ASUS`.** Measured by
printing every revision with `p4 print -o` and scanning each revision for
each term. The scan carries a positive control: the same scan finds `R-GATE`
three times in revision 134, therefore a zero is a statement about the
document rather than about the instrument (L-CENSUS).

**The word `sitting` occurs in 62 of the 134 revisions, and every occurrence
is one of two things that are not a sitting.** First, the filename
`HardwareSitting` in the on-demand reading table. Second, the doc-lifecycle
sentence "If you find a finished campaign sitting in `Active/`, file it"
(`CLAUDE.md#55` through `#116`). Revision 117 onward carries neither
occurrence outside the filename.

**The word `human` occurs in 129 of the 134 revisions, and in revision 134
every occurrence is inside the founding vision quoted at the top of the
file**: "condense all the good ideas humans have had in github", "any old
human designed language", "it exists for human reading and machine"
(`CLAUDE.md#134:18`, `#134:21`, `#134:23`).

**The lesson id `L-HUMAN` occurs in zero of the 134 revisions.** L-HUMAN is
the lesson that reads "A step requiring a human body is the most expensive
line in the plan. Minimise it the way you minimise heap"
(`docs/PM/Active/Stories/LESSONS.md#70:66`).

**The on-demand reading contract does not carry a flight row either.** The
init skill's Step 5 table, which every session is told is MANDATORY before
work touching a listed subject, has 17 rows
(`//Codex/main/.claude/skills/init/SKILL.md#28`, change 23496). The 17
subjects are: `.codex` source; compiler memory; builds and codex-vm flags;
editing a `build/*.ps1`; tests and sidecars; a GOP desk pane; web output;
IoT boards; compliance claims; VS Code setup and USB stick build and flash;
the app inventory; ethos; the founding vision; Perforce; the first gate or
token request; a LESSONS id; release history. No row names a flight, a
sitting, `docs/Hardware/HardwareSitting.md`, a rehearsal, or metal. The
only two occurrences of the string `flight` in the whole skill are "in-flight
lane state" and "in-flight run", both about a lane's own Perforce state.

**The flight rules existed, and the flight rules lived elsewhere.** The
rules that governed the most expensive and least reversible activity the
fleet performed were carried in `docs/Hardware/HardwareSitting.md` (427 KB,
6,900 lines, 91 changes, per the charter's measured corpus table),
in `docs/PM/CurrentPlan.md`'s Track A section, and in individual lanes'
memory files. None of the three is read at session start: the init skill
reads memory, the lesson index, three agent summaries and Perforce state,
and reaches `CurrentPlan.md` only through a subagent that returns a summary
(`SKILL.md#28`, Steps 2 through 4).

### What the record means

**VAL-F1. The document every session was guaranteed to read said nothing
about the activity the project existed to reach, for the entire life of the
project.** A new session of any lane, on any day between 2026-04-17 and
2026-09-08, could complete the whole init procedure exactly as written and
hold no rule about flying, no rule about rehearsing, no rule about banking a
reading, and no rule about the cost of a human step. The rules were real and
the rules were written; the rules were unreachable from the one path every
session takes.

Supporting evidence: the six-term zero above with a positive control; the
17-row on-demand table with no flight row; L-HUMAN absent from every
revision of the rulebook and present in the lessons index.

Falsifying evidence would be: any revision of `CLAUDE.md` carrying a rule
about a flight, a sitting, a rehearsal or the human step; or a row in the
init skill's table routing an agent to `HardwareSitting.md`. Neither exists
in the record.

**VAL-F2. The fleet's own reading model treated the flight as a specialist
subject and the compiler as the general one.** The Step 5 table carries a
row for a desk pane and a row for a `ds` cell, both of which are recoverable
mistakes inside a text editor. The table carries no row for the one
procedure whose failure costs a human body, a drive to the machine, a flash,
a boot, and a reading that cannot be repeated. The proportion is the
finding: the machinery guarded what was cheap to redo and left unguarded
what could be performed a bounded number of times.

## 08.2 The commander model, and when the fleet acquired one

### What the record says

**The campaign against metal ran 67 days.** The boot arc lands on real metal
on 2026-07-04 (`TheSilentKeyboard.md:61`), and sitting 15 flew on 2026-09-09
(`TheLostParadise.md:25-29`).

**Every fleet-coordination mechanism named in the rulebook arrived in the
last eight of those 67 days.** Measured by printing all 134 revisions of
`CLAUDE.md` and finding the first revision containing each term:

| mechanism | first revision of `CLAUDE.md` | change | date | days before the last sitting |
|---|---|---|---|---:|
| the word `commander` | #106 | 21241 | 2026-09-01 | 8 |
| "How the fleet's models are steered" | #116 | 21718 | 2026-09-02 | 7 |
| the message budget (`300 characters`) | #117 | 21725 | 2026-09-02 | 7 |
| `status.json` | #121 | 22068 | 2026-09-02 | 7 |
| the box's per-guest RAM rule | #102 | 20892 | 2026-09-01 | 8 |

**The commander's own procedure is two days old.**
`//Codex/main/.claude/skills/commander-init/SKILL.md` was added on
2026-09-07 as change 22634.

**The coordination document itself is older than the commander by seven
weeks.** `//Codex/main/docs/Agents/CoordinationProtocol.md` was added on
2026-07-13 as change 7617 and carries 74 revisions, the last on 2026-09-08.
The document therefore existed for the whole campaign, and the role that
reads it fleet-wide did not.

### What the record means

**VAL-F7. The fleet acquired a commander after the campaign it needed one
for was over.** For 59 of the 67 days, six lanes coordinated through a
register, a mailbox and per-lane memory, with no role whose job was to see
all six at once. The mechanisms that make a commander possible, a fresh
status file per lane and a measured context number, arrived in the same
week as the role.

Falsifying evidence would be an earlier revision of `CLAUDE.md` or of
`CoordinationProtocol.md` naming a commander, or an earlier skill carrying
the commander's procedure. The scan above covers every revision of the
rulebook, and the skill's own first revision is change 22634.

## 08.3 Handoffs and context exhaustion

### What the record says

**The handoff procedure is older than the campaign.**
`//Codex/main/.claude/skills/handoff/SKILL.md` was added on 2026-07-07 as
change 7268, three days after the boot arc reached metal, and carries 10
revisions.

**The depot's handoff corpus contains nothing from the campaign.**
`docs/PM/Done/Handoffs/` holds 18 files. Every one entered the depot at the
Perforce import of 2026-04-17, therefore the depot's own dates say only when
the import ran, and the authoring dates inside the files are March and April
2026. **Scanned for the words flight, sitting, stick, ASUS and metal, all 18
answer zero.** The formal handoff record stops before the campaign starts.

**The campaign's handoffs are one lane's private files.** Eight handoff
files exist across the twelve memory directories, and all eight are root's:
`session-2026-08-17`, `-08-18`, `-08-20`, `-08-21`, `-08-22`,
`session-2026-08-28-commander-handoff` (26,510 bytes), plus
`foreword-shadows-handoff` and `riscv-process-kernel-handoff`. No other lane
has one, in either of that lane's two directories.

**The instrument that decides when a session hands off arrived one day
before the last sitting.** `//Codex/main/build/measure-context.ps1` was
added on 2026-09-08 as change 23494. The rule the instrument serves is in
the handoff skill's own description: run "when MEASURED context passes 70%
used; never earlier on a lane's own estimate". **Neither the 70 per cent
rule nor the tool's name occurs in any of the 134 revisions of
`CLAUDE.md`.**

### What the record means

**VAL-F8. For the whole campaign the fleet's session-to-session memory of
metal was one lane's private directory.** Root's eight handoff files are the
only durable handoff artifacts from the period, and by VAL-F6 a memory file
is readable at session start by exactly one lane. A lane resuming work on a
flight question inherited what the registers carried and what that lane's
own memory carried, and nothing else.

**VAL-F9. The fleet measured the resource that ends sessions for the first
time on the campaign's penultimate day.** Before change 23494 a lane
estimated context, and the estimate and the measurement disagree by a
factor: this lane's own memory records an estimate of about 800k against a
measured 482k. Every judgement of the form "I have room to finish this"
made before 2026-09-08 rested on the estimate.

Falsifying evidence would be an earlier context tool in `build/`, or a
campaign-era handoff file in the depot or in a lane's memory other than
root's. The filelog of `measure-context.ps1` shows revision 1 at change
23494, and the memory census above lists every file whose name carries
`handoff` or `session-`.

## 08.4 The message budget

### What the record says

The budget entered `CLAUDE.md` at revision #117, change 21725, on
2026-09-02: at most 300 characters, one addressee, one message per event,
reply only when asked, pointers rather than contents, and `to: fleet`
reserved to the commander. The full rules are
`CoordinationProtocol.md`, "The message budget".

### What the record means

**VAL-F10. The budget is a cost control on the channel that carries a
falsifying finding between lanes, and the budget arrived seven days before
the last sitting.** The rule is sound for its stated purpose, which is that
every fleet-wide message costs six agents' context. What the record cannot
show is any earlier rule of the same kind, therefore the campaign ran for
59 days with no stated limit and then eight days with one, and no
measurement in this part distinguishes the two periods. Naming the finding
without a measurement to support a stronger claim is deliberate: the
transcript analysis that would settle whether findings travelled better
before or after 2026-09-02 is root's part 09.

## 08.5 The incentives the models are known to carry

### What the record says

`CLAUDE.md` names three behaviours, from revision #116, change 21718,
2026-09-02, under "How the fleet's models are steered": replies run long and
the model narrates readily during tool use; the model verifies and corrects
its own work unprompted, therefore telling the model to double-check makes
the checking happen twice; and both the commander's model and the lanes'
model can drift past the ask, fixing nearby code and extending behaviour the
task did not name.

**No line in that section, and no line elsewhere in any of the 134
revisions, names an incentive that bears on an irreversible action or on an
action that spends a person.** The section's three behaviours are about
length, about redundant self-verification, and about scope.

### What the record means

**VAL-F11. The rulebook's model of its own agents describes how the agents
write and how far the agents wander, and describes nothing about how the
agents behave when the next step costs a human body.** The record in part
03.3b shows the behaviour that is missing from the description: an agent
that validated in the bed and flew anyway (S1 to S4), an agent that routed
telemetry through the subsystem under test (S4), and four agents that each
built a confident structure on an event none had established, three of whom
opened with a correction saying so (`ProseHasNoRunner.md:15-22`). Those are
incentives with a record, and no revision of the rulebook names one.

Falsifying evidence would be any revision of `CLAUDE.md` naming a model
behaviour in connection with hardware, a flight, or the human step. VAL-F1's
scan answers zero for the vocabulary of all three.

## 08.6 The post-mortems, and whether the recommendations were executed

### What the record says

**`ProseHasNoRunner.md` is my own post-mortem, added on 2026-07-29 by val as
change 12046** (`p4 filelog //Codex/main/docs/PM/Active/Stories/ProseHasNoRunner.md`,
revision 1 of the pre-move path; the file moved at change 14155 and was
refiled at change 14778). The document's section 8, titled "What would
actually have caught it", carries five recommendations, each described in
the document's own words as "Concrete, mechanical, and each one cheap"
(`ProseHasNoRunner.md:556-558`).

The state of each recommendation at main head on 2026-09-09:

| # | the recommendation | state at head | evidence |
|---|---|---|---|
| 8.1 | a machine-readable `build/boot/blocked-images.txt` the flasher refuses on, with `-IKnowItIsBlocked` as the override | **NOT executed** | `p4 files //Codex/main/build/boot/blocked-images.txt` answers "no such file(s)"; `flash-usb.ps1` at head carries no `blocked` refusal |
| 8.2 | invert `test-ovmf.ps1`'s defaults to `-UsbDisk -UsbKbd -NoPs2`, add `-LegacyBed` | **NOT executed** | `//Codex/main/build/boot/test-ovmf.ps1#12`, change 17419: `[switch]$UsbDisk` line 28, `[switch]$UsbKbd` line 36, `[switch]$NoPs2` line 40, all opt-in; the string `LegacyBed` does not occur |
| 8.3 | a CONFIRM or EXPERIMENT confidence field on every artifact handed to a human | **NOT executed** | `HardwareSitting.md` at head carries no rung field of either name: the 7 occurrences of `EXPERIMENT` are prose about probes and the 37 occurrences of `CONFIRM` are hex digits inside image hashes and the word "confirms" |
| 8.4 | a runner asserting a pre-flash screenshot is not uniformly one colour | **NOT executed** | neither `build/boot/test-ovmf.ps1` nor `build/bmpdiff.ps1` (220 lines) contains `uniform`, `histogram`, `single colour` or `one colour` |
| 8.5 | give rung 3 a channel, per L-CHANNEL, before rung 3 is run again | **NOT executed as written** | `//Codex/main/apps/works/DevConsoleBoot.codex`, change 19293, cites 19 chapters and cites neither `GopQr` nor `GopDraw`; the chapter does cite `Dev chapter SerialBridge`, which is the COM1 path the post-mortem already described as the reason the screen stays black |

**One guard of the same shape landed 22 days later, and the guard is a
different one.** On 2026-08-20, change 18229 copied up "flash-usb refuses an
unrehearsed hash by DEFAULT; override split from -Force as
-UnrehearsedAnyway". The refusal enforces L-REHEARSE, which was written
after `TheBedThatAlwaysSaidYes`, and the refusal keys on a rehearsal record
rather than on a blocked-image manifest. The recommendation at 8.1 asked for
a refusal keyed on the blocked state of a named image, and no such refusal
exists at head.

**Zero of the five recommendations were executed in the 41 days between the
post-mortem and the last sitting.**

### What the record means

**VAL-F3, and the failure is mine by name.** I wrote a post-mortem on
2026-07-29 whose central claim is that a guard written as prose is not a
guard, and whose section 8 lists five cheap mechanical guards. I then wrote
no runner for any of the five. The post-mortem became prose about prose
having no runner, which is the exact failure the post-mortem names, and the
document sat in `Active/Stories/` for the 41 days the campaign continued.
The evidence is change 12046 for the writing and the five-row table above
for the not-doing.

**VAL-F4. The fleet's procedure has no step that converts a recommendation
into an owner.** A post-mortem is written, a `LESSONS.md` row is added, and
the arc ends there: no register row is created, no lane is assigned, and no
gate fails for as long as the recommendation is unbuilt. `CurrentPlan.md` is the
fleet's only cross-lane register of open work, and nothing in the
post-mortem procedure requires an entry in the register. A recommendation therefore
survives only as long as the author remembers, and an author is a session
that ends.

Falsifying evidence would be a register row carrying any of the five
recommendations. The search was run at main head on 2026-09-09 over
`docs/PM/CurrentPlan.md`, `apps/works/works-backlog.md`,
`docs/Hardware/HardwareSitting.md` and
`docs/Designs/Active/OS/DiagnosticStick.md` for the terms
`blocked-images`, `LegacyBed`, `IKnowItIsBlocked`, `uniformly one colour`
and `confidence field`, and each term answers zero. The same search finds
`WORKS-9` twice in `CurrentPlan.md`, therefore the zeros are statements
about the registers rather than about the instrument (L-CENSUS). What the
search cannot see is a row that carries a recommendation in different words,
and the reading of every register revision is red's part 02.

## 08.7 The twelve memory directories

### What the record says

**Memory is the one corpus outside Perforce that every session loads in
full.** The init skill's Step 2 reads: "Read your memory index (`MEMORY.md`
at the path in your system context) and every memory file it lists"
(`//Codex/main/.claude/skills/init/SKILL.md#28`, change 23496, Step 2).
Memory therefore reaches a session's context with the same certainty as
`CLAUDE.md`, and memory is the only place a lane can put a rule that a
future session of that lane is guaranteed to read.

**Inventory, measured 2026-09-09.** The twelve directories the charter names
hold 87 files and 357.7 KB, which agrees with the charter's own measurement
of 87 files and 358 KB.

| lane | Cobblestone files | NewRepository files |
|---|---:|---:|
| blu | 4 | 6 |
| fester | 3 | 5 |
| red | 5 | 6 |
| reek | 10 | 8 |
| root | 8 | 20 |
| val | 5 | 7 |

**36 of the 87 files contain at least one of the words flight, flew,
sitting, stick, ASUS, metal, board, or the string rehears.**

**Five lines across all 87 files are rule-shaped**, counting a line that
carries one of those words together with an imperative (must, never, refuse,
always, do not, no lane). Each of the five is quoted here in full:

| file | the line |
|---|---|
| `D--Projects-Cobblestone-fester/memory/fester-lane.md` | "**Never ask for a USB stick to be flashed.** A standing instruction about" |
| `D--Projects-NewRepository-fester/memory/fester-lane.md` | "**Never ask for a USB stick to be flashed.** Beyond L-CHANNEL and" |
| `D--Projects-Cobblestone-root/memory/session-2026-08-28-commander-handoff.md` | "wedge. Never suggest "the fix for the next flight": there is none." |
| `D--Projects-Cobblestone-val/memory/val-trackd-tcp.md` | "**ONE hardware sitting remains, for all time.** No lane asks for a sitting." |
| `D--Projects-NewRepository-root/memory/session-2026-08-21-handoff.md` | "rehearsal certifies BYTES, so do not churn the flashable hash mid-composition." |

Two further matches were excluded after reading, because both use the word
`stick` about Perforce discipline rather than about the medium:
`NewRepository-blu/memory/perforce-process.md` ("stick to it, and do not
explore with files outstanding") and the index line pointing at that file.
A census by spelling finds the spelling, therefore each hit was read before
counting (L-CENSUS).

### What the record means

**VAL-F5. Every flight rule that was guaranteed to reach a session was a
prohibition on ASKING, and none was an instruction on how to fly well.**
Four of the five lines forbid an action: do not ask for a flash, do not
suggest a next flight, no lane asks for a sitting, do not churn the hash.
The fifth states what a rehearsal certifies. No memory file in any of the
twelve directories carries the composition procedure, the rehearsal
procedure, the banking rule, or the reading procedure.

**VAL-F6. A memory file is private to one lane, therefore the fleet had no
shared automatic channel for a flight rule at all.** fester's prohibition
lives in fester's two directories; val's one-sitting rule lives in val's
`Cobblestone` directory; root's two lines live in root's. A lane reads only
the lane's own memory at init. The consequence is measurable in the record
above: the rule "No lane asks for a sitting" is a fleet-wide rule, stated in
fleet-wide words, stored where exactly one lane of six can read the rule.

Falsifying evidence would be a rule-shaped metal line in a memory file
belonging to a lane other than the lane the rule binds, or a shared memory
directory. The inventory above lists twelve per-lane directories and no
shared one.

## 08.C Coverage of this part so far

Files read in full for the sections above:

| path | revision | size |
|---|---|---|
| `//Codex/main/docs/PM/Active/Stories/TheLostParadise.md` | #1 (change 25188) | 147 lines |
| `//Codex/main/CLAUDE.md` | #1 through #134, every revision | 134 files |
| `//Codex/main/.claude/skills/init/SKILL.md` | #28 (change 23496) | full |
| `//Codex/main/docs/PM/Active/Stories/LESSONS.md` | #70 | full |
| `//Codex/main/docs/PM/Active/Stories/ProseHasNoRunner.md` | head | full, 45,864 bytes, 934 lines |
| `//Codex/main/build/boot/test-ovmf.ps1` | #12 (change 17419) | parameter block and the whole file scanned |
| `//Codex/main/build/flash-usb.ps1` | head | scanned for every refusal |
| `//Codex/main/build/bmpdiff.ps1` | head | 220 lines, scanned |
| `//Codex/main/apps/works/DevConsoleBoot.codex` | change 19293 | every cite line |
| `//Codex/main/docs/Hardware/HardwareSitting.md` | head | 5,783 lines, scanned for the confidence field |
| the twelve memory directories | 2026-09-09 | 87 files, 357.7 KB, every file scanned; every rule-shaped hit read in full |
| `//Codex/main/.claude/skills/handoff/SKILL.md` | head (10 revisions, change 23496) | full, 163 lines |
| `//Codex/main/.claude/skills/commander-init/SKILL.md` | filelog only | revision 1, change 22634, for the date |
| `//Codex/main/docs/Agents/CoordinationProtocol.md` | filelog only | revisions 1 and 74, for the dates |
| `//Codex/main/build/measure-context.ps1` | filelog only | revision 1, change 23494, for the date |
| `docs/PM/Done/Handoffs/` | head | all 18 files printed and scanned in full |
| `docs/PM/Active/Stories/TheSilentKeyboard.md` and five more | head | in full, for section 03.3b of part 03 |

Extractions run:

```powershell
p4 filelog -i //Codex/main/CLAUDE.md
p4 print -o <scratch>\claude-md\r<rev>.md //Codex/main/CLAUDE.md#<rev>   # every rev 1..134
p4 print -q //Codex/main/.claude/skills/init/SKILL.md
```

Extraction run for the memory census:

```powershell
# 12 named directories under C:\Users\Damian\.claude\projects\, each memory\ subtree
# scanned for \bflight\b|\bflew\b|\bsitting\b|\bstick\b|\bASUS\b|\bmetal\b|rehears
# then intersected with an imperative marker, and every hit read before counting
```

Not yet read, and named here rather than left silent: `docs/Agents/*`, the
18 handoffs and 19 suspended files under `docs/PM/Done/`, every story under
`docs/PM/Active/Stories/` other than `ProseHasNoRunner.md`, and the
transcripts. Each is a later section of this part. The dated cross-reference
of each lesson against the flight that produced the lesson is appendix D's
next section.
