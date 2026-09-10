# Part 04. The I219 thread

*Part of `TheLostParadise.md`. **Section 1, "What the record says", is written
by red at root's dispatch because blu's context was needed for part 03.
Section 2, "What the record means", and every `BLU-F` finding in this part are
blu's and are not written yet.** Charter rules 1 to 7 bind both sections.*

## 1. What the record says

### 1.1 The driver's whole life, in revisions

`codex/os/kernel/E1000e.codex` carries **29 revisions on the main stream**,
from CL 11947 on 2026-07-29 to CL 19212 on 2026-08-24, taken with
`p4 filelog -i` rather than with `p4 changes` over the path, for the reason
part 02 section 02.5 records.

| date | CL | what the changelist did |
|---|---|---|
| 2026-07-29 | 11947, 11952 | The e1000e receive and transmit path arrives, marked hardware-untested |
| 2026-07-29 | 11975 | Refuses a BAR out of range |
| 2026-07-29 | 12078, 12081 | The chapter's claims narrowed; the device emulation catalogue |
| 2026-07-30 | 12326 | **MDIC, the PHY path the model did not emulate** |
| 2026-08-04 | 12920 | The MDIO window arm and the 10 ms settle the I219 requires |
| 2026-08-04 | 12963, 12980 | Prose moved; B2b findings 2 and 3 |
| 2026-08-05 | 13124 | Annotation extraction |
| 2026-08-10 | 14481 | B2 Finding 4 closed in the bed, driver clears ASDE |
| 2026-08-13 | 14985 | **Quiesce the receiver before programming its ring** |
| 2026-08-14 | 15028 | Poll count as duration |
| 2026-08-15 | 15158 | One source for the x86 side |
| 2026-08-15 | 15465 | **`e1000-await-aneg` bounded by a 3-second HPET budget** |
| 2026-08-16 | 15611 | **`e1000-await-link` bounded by time** |
| 2026-08-19 | 17492 | `nicring` banks GPRC, RNBC, MPC and CRCERRS, and reads RDBA back |
| 2026-08-20 | 18138 | **ASDE quiesces before it resets** |
| 2026-08-20 | 18353 | The PCH K1 layer, shipping OFF until a flight proves it |
| 2026-08-21 | 18444, 18468, 18505, 18562, 18567, 18632, 18736, 18819, 18862 | The K1 burst: nine revisions in one day |
| 2026-08-24 | 19212 | **The i219 acquire-loop defect fixed, with a falsifier** |

**Two facts about the shape of that list.**

First, **the driver's last change is CL 19212 on 2026-08-24, and the last
sitting flew on 2026-09-09.** The network driver that flew on the final flight
was sixteen days old and had received no change in the sixteen days. Part 02's
RED-F2 records the same shape for `codex/os/kernel` as a whole.

Second, **nine of the 29 revisions landed on one day, 2026-08-21**, and every
one of the nine concerns the PCH K1 power-management layer or the semaphore
that guards it. One day carries 31 percent of the driver's whole history.

### 1.2 The NIC flights in order, and what each proved

Every row below is a flight from appendix A, `A-sittings-table.md`, quoted at
its own row number.

| row | date | the question | what returned |
|---|---|---|---|
| 3 | 2026-07-29 | rung 2, the parts | NIC `00:1f.6 8086:15b8` I219-V rev 31, `MAP=ok`, station address read. **The part is identified and is an I219, not an e1000** |
| 8 | 2026-08-03 | did the scheduler ever walk our ring | the ring question opens |
| 26 | 2026-08-11 | B2 Finding 4, the ASDE bit | the ASDE thread opens |
| 29, 30 | 2026-08-13 | Finding 4 with both arms ahead of the reset; does the ASDE write take | the ASDE write is put ahead of the reset |
| 34 | 2026-08-14 | NIC-1, NIC-2 and NIC-3 on one boot | `RCTL=0 EN=n`, `TCTL=0x3003F0F8 EN=n`, `RDBAL=0x5C805420 RDLEN=0`; **1,000,000 empty polls cost 32,606 us against 13,034 in the bed**; then the NIC-3 banner and NOTHING |
| 36 | 2026-08-15 | which of the ten steps inside `e1000-init` hangs | **every step returned**, and `s9 phy-bring-up ret=0 in 92892733us`. `INIT COMPLETE RDH=15 RDT=15` |
| 37 | 2026-08-16 | do frames move, is the ring written back | three rows painted, then nothing for over ten minutes. `arrival RCTL=0 RDT=0`. **The hang was the previous flight's own fix, unbounded where the removed wait had been the bound** |
| 45 | 2026-08-21 | nine I219 readings at stage 10 | `DIAG.TXT` holds stages 1 to 8 and STOPS. The nine readings were never obtained |
| 48 | 2026-08-21 | which line of the bring-up hangs | the glass painted `b3 -> clock`, then `b3 -> reset`, and stopped. The medium carries `stage=b3 step=reset` |
| 49 | 2026-08-21 | the seven split reset operations, and b3 | **all seven ran and the sitting-10 hang did NOT reproduce.** The dev box logged `CONNECTION 28 from 192.168.6.200:49157`, 13 bytes echoed and closed clean |
| 50 | 2026-08-24 | which of `swflag` or the `CTRL.SLU` write kills the medium | the question is put |
| 54 | 2026-09-09 | THE LAST SITTING, NIC-4 and NIC-6 aboard | `nicsit` ended on `poll 1000000 empty=33152us tick100k=3315us hpet-hz=23999999`; `nicinit` and everything after **never reached** |

**What the sequence proves, stated as the record states it and no further.**

- **The part is an I219-V, `8086:15b8`, revision 31** (row 3, 2026-07-29).
- **Every step of `e1000-init` returns on the board** (row 36, 2026-08-15).
  The bring-up does not fail by returning an error. `phy-bring-up` returned
  zero after 92.89 seconds.
- **A reset path that hung on one flight ran clean when split into its seven
  operations** (row 49, 2026-08-21), and on that same flight the board opened
  a TCP connection to the dev box and echoed 13 bytes. The part can therefore
  carry a conversation.
- **The last flight never reached `nicinit`** (row 54). Every NIC question
  aboard received the same answer, which is not reached.

### 1.3 Where the driver, the bed and the part disagree

Four disagreements are recorded in the driver's own changelists. Each is
quoted from the changelist description that fixed it.

**One. The bed had no PHY, and granted link on the wrong register.** CL 12326
of 2026-07-30: "The I219 is a PCH-integrated MAC reachable only through MDIC.
The model had no PHY and granted `STATUS.LU` on `CTRL.SLU` alone, so a driver
that never touched the PHY passed here." A driver that could not work on the
board passed in the bed, and the bed's generosity is what let it pass
(L-ARENA).

**Two. The board requires a settle the bed did not.** CL 12920 of 2026-08-04
added "the MDIO window arm and the 10 ms settle the I219 requires", and added
`-e1000-mdio-window` to codex-vm **off by default**. The bed can express the
board's requirement only when a flag is passed.

**Three. An unbounded wait was affordable in the bed and not on the board.**
CL 15465 of 2026-08-15 bounded `e1000-await-aneg` by a 3-second HPET budget
"instead of a million-iteration count. On the ASUS that count cost 92.9 of the
93 seconds `e1000-init` took." The same changelist records the cost of the
delay in fixing it: "The cost was diagnosed 2026-08-04 and routed around
rather than fixed, so it cost a second flight eleven days later." CL 15611 of
2026-08-16 then bounded `e1000-await-link` the same way, and states "It is
what the NIC-4 flight hung in, and the defect is older than the change that
exposed it."

**Four. The bed's model of a firmware-owned bit was wrong in the direction
that hides a defect.** CL 19212 of 2026-08-24: "the i219 acquire-loop defect
is fixed, with a falsifier ... IT HAD NO FALSIFIER AND THAT WAS THE FIRST
THING TO FIX. codex-vm's `i219_extcnf_write` kept firmware's MNG bit whatever
software wrote."

**A fifth disagreement is a measurement rather than a defect, and it is the
last sitting's only scientific yield.** The board's empty poll costs about 2.5
times the bed's: 32,606 microseconds for 1,000,000 polls on 2026-08-14 (row
34) against 13,034 in the bed, and 33,152 microseconds on the last flight (row
54). The two board readings agree with each other across 26 days.
FESTER-G19 states the same measurement from the ladder's side.

### 1.4 Coverage

```
p4 filelog -i //Codex/main/codex/os/kernel/E1000e.codex     -> 29 revisions
p4 describe -s <CL>                                          -> the descriptions quoted in 1.1 and 1.3
```

Read in full for this section: `A-sittings-table.md` at main CL 25322, rows 3,
8, 26, 29, 30, 34, 36, 37, 45, 48, 49, 50 and 54.

**What section 1 did not read, and why.** `I219IsNotAnE1000.md` and
`docs/Hardware/HardwareSitting.md` in full are blu's corpus for section 2 and
are not read here; section 1 takes its flight rows from appendix A, which blu
compiled from `HardwareSitting.md`, and therefore section 1 inherits appendix
A's own limits rather than the source's. `build/boot/diag/DiagNic*.codex`,
`DiagAsde.codex` and `NicInitProbe.codex` are the ladder's side and belong to
section 2 and to part 06. No claim in section 1 rests on reading the driver's
source at any revision: every claim about what a revision changed is taken
from that revision's own changelist description, which is weaker evidence than
the diff and is marked as such here rather than left for a reader to discover.

## 2. What the record means

*Written by blu over red's section 1, with `docs/Designs/Active/OS/I219IsNotAnE1000.md`
and `docs/Hardware/HardwareSitting.md` read as blu's own corpus. Red wrote no
finding and drew no conclusion; every `BLU-F` below is blu's.*

### 2.1 The part was named on the first ladder flight and the driver never was

Rung 2 identified the part on 2026-07-29, the first day the record exists:
`00:1f.6 8086:15b8`, an Intel I219-V, revision 31 (section 1.2, row 3). The
driver that flew for the whole campaign is `codex/os/kernel/E1000e.codex`, and
the design document that states the mismatch is dated 2026-08-20, CL 18317,
**twenty-two days after the part was named**. That document's own title is the
cost: "The I219 is not an e1000, and that is why thirty sittings taught us
nothing".

What the document establishes about the part is structural rather than
incidental (`I219IsNotAnE1000.md:5-14`): on this family the MAC is inside the
PCH and the I219 is essentially the PHY, unlike the 825xx parts, which are a
combined MAC and PHY on a card. The consequence the document draws is the one
that governs every register reading in the record: **the I219's MAC CSR map is
in no document this tree holds**, therefore a MAC-side offset can be
corroborated by FAMILY from the 82583V and never cited for the part.

**BLU-F10. The part was identified on the first flight of the record and the
driver was written to a different part, and twenty-two days of flights ran
between the identification and the document that stated the difference.**
Supporting evidence: section 1.2 row 3 for the identification on 2026-07-29;
`p4 filelog -i //Codex/main/docs/Designs/Active/OS/I219IsNotAnE1000.md`
answering `#1 change 18317 add on 2026/08/20`; the driver's name and its 29
revisions in section 1.1.
Falsifying evidence: a document before 2026-08-20 stating that the board's MAC
is in the PCH and that the driver models a combined MAC and PHY.

### 2.2 On this part the bring-up does not fail, and no error-shaped instrument could have found that

Section 1.2 row 36 is the reading the whole NIC thread turns on. Every one of
the ten steps inside `e1000-init` RETURNED on the board. The step that cost 93
seconds returned zero, which is its failure value, after 92,892,733
microseconds. Nothing raised, nothing refused, nothing timed out into an error
path.

The same shape holds across the thread. `e1000-await-reset` answers
`settled=1` on its first read because `CTRL` is read-only on this part,
therefore a reset that never executed and a reset that completed are the same
reading (appendix A row 30). `e1000-await-aneg` returns 0 after its full
million because aneg-done is never set on this part, and the link comes up at
1000 Mb/s anyway (row 36). `nicring` reads `quiet` with every register
healthy.

**Every failure this campaign measured on the NIC is a DURATION or a STATE,
and not one of them is an error code.** An instrument built to catch a
returned error would have reported the part healthy on every flight, which is
what `e1000-init` returning `INIT COMPLETE` after 93 seconds means.

**BLU-F11. Every NIC failure the record measured presents as a duration or as
a state rather than as an error return, therefore the driver's own error paths
were never the instrument that could find one.**
Supporting evidence: row 36, every step returning with `s9` at 92,892,733
microseconds; row 30, `CTRL` read-only making `settled=1` unfalsifiable;
`e1000-await-aneg` returning 0 with the link up at gigabit; row 44's `quiet`
with `rdh-writable=y`.
Falsifying evidence: a NIC defect in the record found by a returned error
code.

### 2.3 The bed was more permissive than the part in every recorded disagreement

Section 1.3 records four disagreements. **Three of the four are the bed
granting what the part refuses**, and in each of the three a driver that could
not work on the board passed in the bed:

| disagreement | direction | what passed in the bed |
|---|---|---|
| the model had no PHY and granted `STATUS.LU` on `CTRL.SLU` alone (CL 12326) | bed more permissive | a driver that never touched the PHY |
| the board requires a 10 ms MDIO settle and the bed's flag is OFF by default (CL 12920) | bed more permissive | a driver that omits the settle |
| an unbounded wait costs nothing in the bed and 92.9 seconds on the board (CL 15465, CL 15611) | bed more permissive | a driver with no bound at all |
| `i219_extcnf_write` kept firmware's MNG bit whatever software wrote (CL 19212) | bed more permissive | an acquire loop with no falsifier |

The fourth is the same direction stated about the model rather than about the
driver. The design document generalises the reading in its own words
(`I219IsNotAnE1000.md:136-139`): "codex-vm models an e1000. The board has an
I219. Every requirement below is invisible to a model of the wrong part,
therefore every bed arm is green by construction and stays green however wrong
the driver is. **The bed is not less faithful; it is answering a different
question.**"

**The near-miss recorded at `:141-150` is the sharpest evidence in the thread
that a board reading buys what no bed can.** The bed powers register `770.17`
up at `0x4000` and the board reads `0xd104`, five bits apart, and the five bits
are platform firmware configuration present in no model this tree holds.
`e1000-k1-configure` read-modify-writes, therefore the five bits survive. **A
constant write, which is the obvious implementation and the one a bed alone
would have fully vindicated, would have cleared five bits of firmware state on
every boot**, and nothing in the bed could have caught the loss, because the
bed's power-up value has no firmware bits in it to lose.

**BLU-F12. In all four recorded bed-against-part disagreements the bed was
MORE PERMISSIVE than the part, therefore every one of the four was invisible
to a green bed arm by construction rather than by an oversight in the arm.**
Supporting evidence: the four changelist descriptions quoted in section 1.3;
`I219IsNotAnE1000.md:136-139` and `:141-150`.
Falsifying evidence: a recorded disagreement in which the bed REFUSED what the
part accepts, which would show the bed failing in the direction that costs a
false red rather than a false green.

### 2.4 One fix produced the next flight's hang, and the record says so in its own words

CL 15465 of 2026-08-15 bounded `e1000-await-aneg` to a 3-second budget, and
the bound removed the 92.9 seconds of dead time during which the link had been
coming up on its own. CL 15611 of 2026-08-16 then had to bound
`e1000-await-link`, and states the relationship plainly: the unbounded link
wait "is what the NIC-4 flight hung in, and the defect is older than the
change that exposed it".

Appendix A carries the two flights on either side of that pair: row 36 on
2026-08-15 answered NIC-3 and row 37 on 2026-08-16 hung, and the hang was the
previous flight's own fix.

**CL 15465 also states the cost of the delay in fixing it, in its own words:**
"The cost was diagnosed 2026-08-04 and routed around rather than fixed, so it
cost a second flight eleven days later." The diagnosis existed on 2026-08-04,
the repair taken then was to route AROUND the function in one caller, and the
defect stayed armed for every other caller.

**BLU-F13. A known cost was routed around in one caller rather than bounded at
its source, the routing left the defect armed for every other caller, and the
eventual bound then exposed a second unbounded wait that cost the next flight.**
Supporting evidence: CL 15465's own description quoting the 2026-08-04
diagnosis and the eleven-day gap; CL 15611 stating that the defect it fixed is
older than the change that exposed it; appendix A rows 36 and 37 on
consecutive days.
Falsifying evidence: a record of the 2026-08-04 diagnosis being routed to the
driver rather than to one caller.

### 2.5 Six boots and nine driver revisions on one day

Section 1.1 records nine of the driver's 29 revisions landing on 2026-08-21,
which is 31 percent of the driver's whole history in one day, all of the nine
on the PCH K1 layer or the semaphore guarding it. Appendix A records **six
boots on that same day**, counted mechanically from the table: rows 44 to 49,
sittings 6 through 11.

**The scarce resource this campaign was organised around is stated in the
design document as a number** (`I219IsNotAnE1000.md:38-39`): "The variables
are not serially accessible and we made them serial. Damian has been to the
machine roughly 150 times for five working things." The document's rule 1 is
"One boot answers everything", and the rule exists because a boot costs a
person rather than a machine.

**BLU-F14. On 2026-08-21 the fleet spent six boots and landed nine driver
revisions on the same subsystem on the same day, therefore the driver under
test changed underneath the campaign faster than the campaign could read the
board.**
Supporting evidence: section 1.1's nine revisions of 2026-08-21; appendix A
rows 44 to 49 all dated 2026-08-21, counted from the table;
`I219IsNotAnE1000.md:38-43` for the rule the six boots run against.
Falsifying evidence: evidence that the six boots flew ONE image, which would
make them one composition read six times rather than six spends of the scarce
resource. Rows 44 to 49 carry six distinct image hashes, which is the check
that was run.

### 2.6 The ASDE question rode eleven boots and never once produced a banked row

The ASDE question is aboard appendix A rows 16, 26, 29, 30, 43, 46, 50, 51,
52, 53 and 54, which is **eleven of the 52 boots across 35 days**, from
2026-08-05 to 2026-09-09. The question was never answered.

The reason is structural and `HardwareSitting.md` states it as an instrument
gap rather than as bad luck: asde rides last as ONE row and banks nothing
until it completes, and asde is also the only stage that can wedge the box.
**Those two facts together mean the reading is reachable only THROUGH the
thing it measures**, which is L-BANK exactly. The gap was invisible until the
2026-09-07 default-cfg flight, row 51, because on every earlier flight the
medium had already died upstream, therefore asde's silence could not be told
apart from a dead bank. Row 51 is the first boot with a healthy bank at stage
16 and no asde row, which is what turned the gap from an argument into a
measurement.

**BLU-F15. The ASDE question rode eleven boots over 35 days and produced no
banked result row on any of the eleven, because the stage banks nothing until
it completes and is the only stage that can end the boot.**
Supporting evidence: appendix A rows 16, 26, 29, 30, 43, 46, 50, 51, 52, 53
and 54; `HardwareSitting.md` "THE INSTRUMENT GAP THIS FLIGHT PROVES (L-BANK)";
row 51 separating asde's silence from a dead bank for the first time.
Falsifying evidence: a banked asde result row on any flight.

### 2.7 The last flight never reached the NIC, and its only yield was already known

Section 1.2 row 54 records the last sitting stopping after `nicsit`'s poll
line, before `nicinit` and before every NIC stage that followed. **No NIC gate
of any strength could have changed that outcome**, because the box stopped
upstream of the NIC work in one of three steps that print nothing before
running: `usb-attach`, the ESP select and cfg read, or `net-driver-bring-up`
inside `drec-open`.

The one measurement the flight returned is the empty-poll figure,
`poll 1000000 empty=33152us`, against 32,606 microseconds measured on
2026-08-14 (row 34). The two board readings agree with each other across 26
days. **The final flight's only scientific yield therefore re-measured a
figure the record had already held for 26 days.**

The driver that flew was sixteen days old, unchanged after CL 19212 on
2026-08-24 (section 1.1), and never executed.

**BLU-F16. The last sitting stopped upstream of every NIC stage, therefore its
outcome is not a NIC result at all, and the single measurement it returned
duplicated one taken 26 days earlier.**
Supporting evidence: section 1.2 row 54; the two poll readings, 32,606
microseconds on 2026-08-14 and 33,152 on 2026-09-09; section 1.1's last driver
revision on 2026-08-24.
Falsifying evidence: a reading from the last flight that the record did not
already hold.

### 2.8 What the fly gate was, and what the fly gate could not have done

`I219IsNotAnE1000.md:56-72` defines a FLY GATE: one falsifiable PAIR run on
the I219 model before any image is composed, a kill arm that must come back
RED with one required step omitted and a pass arm that must come back GREEN
with the step present, and the document states that until the pair exists,
composing a sitting is forbidden.

**The gate was met for K1 and the meeting is recorded** (`:334-348`): with
`-i219` the same transmit fails at the NVM K1 setting and succeeds once
`Giga_K1_disable` is written, with two control rows beside it. The document is
also explicit about what a green pair does NOT prove (`:364-366`): "A green
pair proves the driver CONFORMS TO THE CITATION. It does not prove the part
behaves as cited, and no bed can."

Eleven boots flew after the document landed on 2026-08-20. The gate is
therefore not a rule the campaign broke; **the gate is a rule whose own
statement of its limits is the finding.** A pair proves conformance to a
citation, the citation for this part does not exist for the MAC side
(`:11-14`), and the last flight stopped before reaching the part at all.

**BLU-F17. The fly gate was defined on 2026-08-20, was met for the one
requirement it was applied to, and states in its own text that a green pair
proves conformance to a citation rather than the part's behaviour; the
citation this part needs does not exist for the MAC side.**
Supporting evidence: `I219IsNotAnE1000.md:56-72`, `:334-348`, `:364-366` and
`:11-14`; the doc's creation at CL 18317 on 2026-08-20; eleven boots dated
2026-08-21 or later in appendix A.
Falsifying evidence: a MAC-side citation for the I219 in this tree, which
would make the gate's conformance proof a proof about the part.

### 2.9 Coverage for section 2

| path | extent | revision |
|---|---|---|
| `docs/Designs/Active/OS/I219IsNotAnE1000.md` | 41,541 bytes, 704 lines; lines 1 to 150 and 330 to 389 read in full, the remainder read by targeted search | `#1` at CL 18317, at main 25333 |
| `docs/Hardware/HardwareSitting.md` | 437,270 bytes, 6,904 lines, read in full for appendix A | at main 25193 |
| `04-the-nic.md` section 1 | 141 lines | at main 25328 |
| `A-sittings-table.md` | 54 rows | at main 25322 |

```powershell
p4 filelog -i //Codex/main/docs/Designs/Active/OS/I219IsNotAnE1000.md
# boots dated 2026-08-21 or later, and boots dated 2026-08-21, counted from the table
(Get-Content A-sittings-table.md) | Where-Object { $_ -match '^\| \d+ \| 2026-08-21 \|' }
```

**What section 2 did NOT read, and why.** The driver's source at any revision:
section 1 states that every claim about what a revision changed comes from the
changelist description rather than from the diff, and no finding above needs
the diff, because BLU-F10 to BLU-F17 rest on flight readings, on dates, and on
the design document's own text rather than on what a line of the driver says.
`build/boot/diag/DiagNic*.codex`, `DiagAsde.codex` and `NicInitProbe.codex`
belong to part 06 as instruments; BLU-F15 uses the asde stage's banking
BEHAVIOUR as `HardwareSitting.md` records the behaviour rather than as the
chapter states it, and a reader who needs the chapter must treat BLU-F15 as
resting on the flight record.
