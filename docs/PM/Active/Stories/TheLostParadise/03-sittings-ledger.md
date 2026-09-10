# Part 03. The sittings ledger

*Owner: blu. Part of `docs/PM/Active/Stories/TheLostParadise.md`, the accident
investigation ordered by Damian on 2026-09-09 at 02:55. Rules 1 to 7 of the
charter bind every sentence below. The per-boot ledger is
`A-sittings-table.md`; this part is the account the ledger supports.*

**Landing 1 of this part covers the two eras, the undocumented era, and the
findings the two eras produce. Section 03.6 states what remains unwritten.**

## 03.1 What the record says: the record begins after 76 percent of the project

Three dates bound every count in this part, and each of the three is
verified at its own pointer rather than carried from another part:

| event | date | evidence, verified by blu 2026-09-09 |
|---|---|---|
| the project began | 2026-03-14 | `CLAUDE.md`, "The project was started 3/14/2026" |
| the ASUS enters the depot | 2026-05-07 | CL 1104, `p4 describe -s 1104`: "PE stub stack alignment ... for real hardware UEFI boot. Asus firmware rejects high ImageBase with dummy .reloc" |
| the flight record is created | 2026-07-28 | CL 11775, `p4 filelog -i //Codex/main/docs/Hardware/HardwareSitting.md`, revision `#1 change 11775 add on 2026/07/28`, at the path `docs/HardwareSitting.md` |

**The file moved once and the move hides the beginning.** `p4 filelog` on the
current path answers `#1 change 14778 move/add on 2026/08/13`, therefore a
reader who asks the current path for the record's first revision is told
2026-08-13 and is 16 days wrong. The chain the same command prints is
`moved from //Codex/main/docs/HardwareSitting.md#83`, and the original path's
own first revision is CL 11775. Part 02 records the same trap independently
(`02-timeline.md` section 02.3), and the two readings agree.

**82 days separate the ASUS entering the depot from the flight record
existing.** From 2026-05-07 to 2026-07-28 is 24 days of May, 30 of June and
28 of July, which is 82. Through all 82 days the board was being flown, and
no file in the depot was dedicated to recording a flight.

## 03.2 What the record says: the undocumented era's own words

`docs/Hardware/HardwareSitting.md` carries no entry earlier than attempt 1
on 2026-07-29. The era before the entry is reconstructed from the documents
the era itself produced. Every quotation below is read at the line cited.

### In March 2026 the first physical target was a phone, not a board

`docs/PM/Done/Plans/CurrentPlan-2026-03-24-peak2-complete.md` line 23:
"**Phone hardware ready (2026-03-23).** Samsung SM-G935T (T-Mobile S7 Edge)
backed up, SIM removed, OEM unlock enabled, Odin connected." Line 286 of the
same file names the goal: "**Phone Phase 3 -- Codex.OS**: ARM64 bare metal on
the phone. No Linux. The summit."

`CurrentPlan-2026-03-24-peak3-evening.md` lines 37 to 39 record the campaign
on day 11 of the project: "Bootloader signature verification blocking all
custom recovery images. 9 flash attempts documented. Official TWRP
downloaded. USB state diagnosis in progress. See
`docs/Active/Projects/PHONE-WIPE.md` for full attempt log."

**Nine flash attempts existed by 2026-03-24, and the log naming them
SURVIVES.** The March plan cites the log as `docs/Active/Projects/PHONE-WIPE.md`
and no file stands at that path today, therefore an earlier draft of this part
reported the log as gone. The report was WRONG. The file moved four times and
is `docs/PM/Done/Suspended/Phone/PHONE-WIPE.md`, 264 lines, traced by
`p4 filelog -i` back to `#1 change 2 add on 2026/04/17`. Five siblings survive
beside the log: `CODEX-PHONE.md`, `TWRP-BUILD-HANDOFF.md`,
`TWRP-SESSION-HANDOFF.md`, `phone/EMULATOR-PLAN.md` and
`ARM64-QEMU-VERIFICATION.md`, with seven build scripts under `phone/`.

**The correction's mechanism is worth more than the correction.** The negative
was taken over the path the March plan NAMED and reported as a fact about the
FILE, and a negative is only ever as wide as the pattern producing the
negative. Red found the survival by asking `p4 filelog -i`, which follows a
move, rather than by asking a path. Red's own audit measures the general case:
357 of 366 cited documents in the tree have moved, therefore a date or an
absence taken over a path is wrong by default (RED-F20).

**What the log holds, and every one of the nine attempts is tabled by change
and by result** (`PHONE-WIPE.md:196-206`). Attempt 1 failed because Odin does
not recognise the partition name `recovery-fixed`. Attempts 2 to 5 failed at
`RQT_CLOSE` after the NAND write, across a rebuilt internal name, a USB port
change, a blanked board name and a full reboot cycle. Attempt 6 failed because
Heimdall 2.0.2 cannot handshake with the part. Attempts 7 and 8 failed at
`SetupConnection`. **Attempt 9 flew an OFFICIAL TWRP 3.7.0 image built by
TWRP's own Jenkins CI, on a rebooted PC with a clean USB state, and failed at
`RQT_CLOSE` exactly as the hand-packed image had.**

The log calls attempt 9 conclusive in its own words (`:236-242`): "An official
TWRP image built by TWRP's own Jenkins CI also fails at `RQT_CLOSE`. This
eliminates our hand-packed image as the cause. **The T-Mobile SM-G935T
bootloader is enforcing signature verification on the recovery partition.**"
The diagnosis names the reason the OEM Unlock toggle did not help: on Samsung
devices the toggle only PERMITS unlocking and does not PERFORM the unlock.

**The campaign was therefore stopped by the part rather than by the fleet**,
and the log's own next-steps section offers a fourth option that abandons the
flash entirely: "Skip TWRP entirely. Use `adb` from Android to push Codex
ARM64 binaries to `/data/local/tmp/` and run them under Android."

### The day-11 table states the whole accident in advance

`PHONE-WIPE.md:19-29` carries a verification table dated 2026-03-24, day 11 of
the project. Seven rows read "Proven": the header format, the `dt_size` field,
the SEANDROIDENFORCE trailer, the page alignment, a byte-match against a
known-good image, the components being real extracted files, and ARM64 Codex
binaries running on an Android emulator. **Two rows read otherwise, and both
are the physical ones:**

| row | status, verbatim |
|---|---|
| This image actually boots on an S7 Edge | **NOT PROVEN -- cannot be emulated** |
| Odin flash to SM-G935T (T-Mobile) | **FAILED -- RQT_CLOSE after NAND write (2026-03-24)** |

The paragraph under the table states the reason plainly: "No emulator exists
that can tell you 'this TWRP image will boot on this phone.' The format is
proven correct. Whether it boots is a physical test."

`phone/EMULATOR-PLAN.md:117-126` carries the same shape as a risk table on
2026-03-23. Every software step is rated "None": the QEMU run, the emulator
test, the `adb` push, the image build, the inspector. One row is rated
"Medium", the flash, and the mitigation reads "Only after all validations".
**Every validation passed and the flash then failed nine times**, therefore
the gate the plan relied on could not observe the thing the flash was stopped
by.

### In March 2026 the phrase "bare metal" named an emulator

`CurrentPlan-2026-03-26-evening.md` line 9 describes the project as "The
self-hosted Codex compiler -- running on bare metal x86-64 under QEMU". Line
41 of the same file names a test as "QEMU bare metal test". Line 121 lists as
a FUTURE item "Codex.OS on real hardware | WHPX (Nut's box), then actual boot
device".

**Real hardware was a future column in the plan on 2026-03-26, twelve days
into the project**, and the phrase that names today's compiler target was
already in use for something else.

### The release record carries no metal until 2026-07-08

Measured 2026-09-09 across all 53 files in `docs/PM/Done/GitHubUpdates`, by
counting occurrences of `flashed`, `flew`, `flight`, `sitting`, `the ASUS`,
`on metal`, `real hardware` and `the board` in each file:

| updates | metal mentions | reading |
|---|---|---|
| 2 to 33, excepting 9 and 25 | 0 | thirty updates in a row say nothing about a board |
| 9 | 1 | "real hardware through a series of diagnostic CLs" |
| 25 | 1 | "GPU compute needs real hardware validation" |
| 34 to 54 | 8, 1, 0, 10, 10, 9, 2, 1, 1, 1, 4, 1, 1, 10, 3, 32, 8, 3, 3, 2, 2 | the metal era of the release record |

**31 of the 53 updates carry no mention of a board at all.** Update 33 is
dated 2026-07-07 and Update 34 is dated 2026-07-08, therefore the release
record's metal era begins on one day, at Update 34, 116 days into the project.

### Update 34 claims a boot the flight record's own measurement contradicts

`GitHubUpdate34.md` lines 12 to 20, dated 2026-07-08: "A 1 GB USB stick,
flashed with `seed/Codex.img`, booted on a real UEFI machine and ran its own
first-boot ceremony ... Confirmed on hardware: the first-boot screen renders,
the **PS/2 keyboard works**, and the whole wizard runs". Line 29 of the same
file states the mechanism: the stub "hands the framebuffer to a Codex payload
that reads the real PS/2 controller (ports 0x60/0x64) after boot services
die". Lines 65 to 66 name the machines: the image "boots on an ASUS TUF
(2015 AMI), a Dell Inspiron, and OVMF".

Twenty-one days later, `HardwareSitting.md` rung 2 answers the same question
on the ASUS: "**THERE IS NO PS/2 ON THIS BOARD.** Zero arrivals before the
handback and zero after", and draws the consequence "the keyboard is USB
behind firmware i8042 emulation and that emulation does not survive
ExitBootServices" (`HardwareSitting.md#31:6438`).

**The two readings cannot both describe the same machine.** Update 34 states
that a payload read ports 0x60 and 0x64 after boot services died and took
keystrokes; rung 2 states that the same operation on the ASUS returns zero
arrivals. Three resolutions survive the evidence in hand, and the record
distinguishes none of the three:

1. the 2026-07-08 boot was the Dell Inspiron rather than the ASUS;
2. the 2026-07-08 boot was the ASUS with boot services still LIVE, therefore
   the keystrokes came from the firmware's i8042 emulation and the claim
   "after boot services die" is the release note's error;
3. the two machines called "ASUS TUF" in the two documents are two different
   boards.

**What would settle the question:** the CL range Update 34 covers is 7228 to
7355, and a changelist in that range naming the machine, or a build recipe
carrying `-Ebs`, decides between resolution 1 and resolution 2. Part 02 owns
the changelist ledger and part 05 owns the medium; the reading is routed to
both rather than guessed here.

## 03.3 What the record says: no count of the undocumented era is recoverable

Damian's charter states "hundreds of sittings" and "over 120 of them over 3
months or so" before the record began. **No number in that shape is
recoverable from the depot for the ASUS, and the reason is specific rather
than general. The PHONE half of the era IS recoverable and is recovered in
section 03.2.**

- The flight record did not exist, therefore no file was appending a row per
  boot.
- The one attempt log the era names SURVIVES and tables all nine attempts,
  therefore the phone campaign is recoverable in full and is recovered above.
- The release record is silent about a board for 31 of its 53 files.
- Perforce holds nothing at all from 2026-03-14 to 2026-04-16, which is the
  era in which the phone campaign's nine flash attempts happened.

**A number is therefore stated as a floor and never as a count.** What the
surviving documents NAME, counted 2026-09-09: nine phone flash attempts by
2026-03-24, and the boots Update 34 describes on three targets. The
enumeration of every boot named across `Done/Plans` (12 files),
`Done/Handoffs` (18 files), `Designs/Done/Hardware` (9 files),
`Done/Tools/HardwareBringUpPlaybook.md` and the transcripts is the next unit
of this part, and no floor is published before the enumeration finishes,
because a floor quoted from a partial sweep reads as a count the moment the
floor leaves its sentence (L-REQUEST).


## 03.3a What the record says: the enumeration of the undocumented era

Section 03.3 refused to publish a floor before the enumeration finished. The
enumeration is below, and it produces a number.

### The method, and what the method cannot see

A census over the three corpora the charter names, counting occurrences of
`flashed`, `flash attempt`, `flew`, `flight`, `sitting`, `booted`,
`boot attempt`, `Odin`, `on the phone`, `on the ASUS`, `on metal`,
`real hardware`, `the board` and `the stick` in each file, run 2026-09-09:

| corpus | files | files with any hit | the files that carry the era |
|---|---|---|---|
| `docs/PM/Done/Plans` | 12 | 4 | the four March plans, all read in section 03.2 |
| `docs/PM/Done/Handoffs` | 18 | 2, one hit each | neither hit is a boot event |
| `docs/Designs/Done/Hardware` | 9 | 5 | `BootRoadmap.md` 28 hits, `UEFI-BOOT-INVESTIGATION.md` 11, `REAL-HARDWARE-BRINGUP.md` 5 |

**Eighteen handoff documents across the whole pre-record era mention a board
twice, and neither mention is a boot.** The handoff corpus is the fleet
handing work between sessions, therefore a boot that happened and was handed
on would appear there, and none does.

**What the census cannot see** is a boot described without any of the fourteen
spellings. The count below is therefore a FLOOR over those spellings and is
not a count of boots (L-CENSUS, and the same limit BLU-F4 states about
itself).

### The floor: thirteen boot events are named, and each carries a citation

| # | date | what booted, and where | evidence |
|---|---|---|---|
| 1 to 9 | 2026-03-24 | nine Odin flash attempts against the Samsung SM-G935T, every one failing, attempt 9 on an official Jenkins-built TWRP | `PHONE-WIPE.md:196-206` |
| 10 | on or before 2026-05-05 | at least one USB boot of the ASUS TUF that failed, named as the document's own trigger: "VGA terminal demo works in QEMU; **USB boot fails on ASUS TUF board**" | `REAL-HARDWARE-BRINGUP.md:5` |
| 11 | on or before 2026-07-08 | `optiona-milestone.img` renders its menu on the ASUS TUF | `BootRoadmap.md:520-521` |
| 12 | on or before 2026-07-08 | `optiona-milestone.img` renders its menu on the Dell Inspiron 15 5000 | `BootRoadmap.md:520-521`, `:47-48` |
| 13 | 2026-07-08 | `optiona.img`, 16 MB, seed `1075CD32`, flashed to a 1 GB stick and booted on a real machine; the first-boot screen renders and the whole wizard runs | `BootRoadmap.md:10-14` |

**Thirteen boot events are named for the 136 days before the flight record
existed** by the three censused corpora. Appendix A holds 52 boots for the 43
days after. The two eras together therefore name **65 boots** from those
sources.

**CORRECTED 2026-09-09, and the correction raises the floor.** Section 03.3b
reads the eight stories, which the census of this section did not cover, and
the stories name boots the three corpora do not. Verified at their lines by
blu before this paragraph was written:

| added | date | what the story says | citation |
|---|---|---|---|
| 14 | 2026-07-13 | a three-phase probe "is flashed. It boots. It runs all three phases on the ASUS", and the stick comes back byte-identical | `TheSilentKeyboard.md:98-107` |

and one FIGURE rather than an enumeration, for the arc the census reached only
at its last day: **"six-plus human flash-walk-boot-photograph cycles"**
(`TheSilentKeyboard.md:38-39`), several of them performed with a back injury.

**The floor is therefore at least 14 named events plus a quoted six-plus
cycles, and 66 across both eras.** The floor is stated as rising rather than
restated as a new count, because the stories are an account rather than a
ledger and the census that produced 13 could not see them.

### Sixty-five named against roughly one hundred and fifty stated

`docs/Designs/Active/OS/I219IsNotAnE1000.md:38-39`, written by red on
2026-08-20, states the figure the campaign was organised around: "The
variables are not serially accessible and we made them serial. **Damian has
been to the machine roughly 150 times for five working things.**" Damian's own
charter states "hundreds of sittings" and "over 120 of them over 3 months or
so" before the record began.

**The record names 65 boots. The project's own documents describe roughly 150
visits to the machine. About 85 visits are named nowhere the census can
reach**, and the arithmetic is stated as a subtraction of a floor from an
estimate rather than as a count of missing boots, because neither number is a
census of the same thing: 65 is a floor over fourteen spellings, and 150 is a
figure a design document states without deriving.

**BLU-F18. The surviving record names 65 boots and the project's own documents
describe roughly 150 visits to the machine, therefore more than half of what
was spent at the board is recoverable from no document in the tree.**
Supporting evidence: the thirteen named events above with their citations;
appendix A's 52 boots; `I219IsNotAnE1000.md:38-39`; the charter's own
"over 120 of them over 3 months or so".
Falsifying evidence: a corpus outside the three censused that names boot
events, for which the transcripts are the candidate and are NOT read here; or
a derivation of the 150 figure showing the figure counts something other than
boots, which the document does not give.

### The 2026-07-08 boot, and what the boot narrows about BLU-F3

BLU-F3 records that `GitHubUpdate34.md` of 2026-07-08 claims a working PS/2
keyboard after boot services die, against rung 2 of 2026-07-29 answering that
the board has no PS/2 at all. `BootRoadmap.md` is the design-side account of
the same day and narrows the question in three ways.

**First, the roadmap names three targets rather than one** (`:47-48`): "Codex
boots on real UEFI hardware -- ASUS TUF (2015 AMI Aptio V), Dell Inspiron 15
5000, and edk2/OVMF". The 2026-07-08 ceremony entry (`:10-14`) says "booted it
on a real machine" and names neither of the two.

**Second, the roadmap records a detail about the machine that ran the
ceremony** (`:24-26`): "On the test machine the internal HDD was BIOS-disabled,
so AHCI found nothing and the write failed safely". A machine with an internal
HDD is a fact about that machine and a reader with both boxes in hand can
settle which one.

**Third, and this is the part that changes the reading: the B1.1 status entry
states the PS/2 evidence, and the evidence is a BED confirmation plus one
byte** (`:85-90`): "Status (2026-07-08): reading works on real firmware;
blocked on a compiler limit. **Confirmed under OVMF**: after ExitBootServices
the firmware leaves the PS/2 keyboard disabled, so `run-menu` re-enables it
(`0x64 <- 0xAE`, `0x60 <- 0xF4`); the keyboard then delivers -- we observe its
**`0xFA` ACK** read back from port `0x60`. **So the read path is correct on
metal.**"

Two things about that paragraph are load-bearing. The confirmation named is
OVMF, which is a bed, and the sentence draws a conclusion about metal from it.
And the observed byte is `0xFA`, which the ladder's own discriminator table
classifies three weeks later as chatter rather than a keystroke:
`HardwareSitting.md` rung 2 stage C reads "`fa` | ACK. Controller chatter,
**not a keystroke**", and the same table instructs the operator to hold a known
key and check that `last=` carries that key's scancode.

**The ceremony entry is not thereby refuted.** A wizard that accepts a
passphrase and an entropy sentence needs real keystrokes, and the ceremony
entry says the wizard ran. What the roadmap shows is that the PS/2 claim in
the release record rests on two different kinds of evidence stated in one
breath: a walked ceremony, which is strong, and a bed confirmation plus an ACK
byte, which the fleet itself later classified as not a keystroke.

**BLU-F19. The 2026-07-08 metal claim about PS/2 mixes a walked ceremony with
a bed confirmation and an ACK byte, and the ACK byte is the exact reading the
fleet's own ladder classified three weeks later as chatter rather than a
keystroke.**
Supporting evidence: `BootRoadmap.md:85-90` for "Confirmed under OVMF" and the
`0xFA` observation and the conclusion "the read path is correct on metal";
`BootRoadmap.md:10-14` for the walked ceremony; `HardwareSitting.md` rung 2
stage C for the `fa` row and for the hold-a-known-key instruction.
Falsifying evidence: a record of the 2026-07-08 boot naming its machine and
carrying a scancode rather than an ACK, which would make the claim a
measurement on a named board.

### The playbook carries the reading that resolves the PS/2 disagreement

`docs/Designs/Done/Tools/HardwareBringUpPlaybook.md` is the method distilled
from the keyboard campaign and records the campaign as "sixteen probe
versions, **five metal boots**, 2026-07-29 to 2026-08-03" (`:4-6`), which
agrees with appendix A's five `kbd-diag` boots in that window.

Its phase 2 photograph carries a reading no other document in this
investigation quotes (`:28-33`): "The driver halts the controller and hands it
back to the firmware; **`PS2=58 last=a2`** is keystrokes arriving over the
firmware's SMM PS/2 emulation, proving the no-USB-driver fallback is real on
this board."

**`last=a2` is a BREAK CODE by the ladder's own discriminator table.** Rung 2
stage C classifies `01`..`58` as a set-1 make code and `81`..`d8` as "a break
code, the release of a real key. **Also the answer**", against `fa`, `aa`,
`fe` and `ee`, which are controller chatter. `a2` falls inside the break range,
therefore the playbook's reading is a real key release and not an ACK.

**The two readings that looked contradictory are two different experiments,
and together the two readings state the board's actual behaviour.** Rung 2 measured the
raw PS/2 ports with OUR code owning the controller after ExitBootServices and
counted zero arrivals either side, and drew the conclusion "the keyboard is
USB behind firmware i8042 emulation and that emulation does not survive
ExitBootServices". Phase 2 measured the same board after HANDING THE
CONTROLLER BACK to the firmware, and keys arrive. The refinement rung 2's
sentence needs is one clause: the emulation does not survive our takeover, and
returns when the firmware is given the controller again.

**BLU-F20. The board delivers real PS/2 keystrokes through the firmware's SMM
emulation when the firmware owns the controller, and delivers nothing on the
raw ports when our code owns it, therefore the 2026-07-08 claim and the
2026-07-29 measurement are both readings of one machine under two different
ownerships.**
Supporting evidence: `HardwareBringUpPlaybook.md:28-33` for `PS2=58 last=a2`
after the handback; `HardwareSitting.md` rung 2 stage C for the break-code row
and for the zero-arrivals measurement under our own ownership; appendix A row
6, whose phase 2 read `rel=y reclaim=y` and KEYS DELIVERED on 2026-08-02.
Falsifying evidence: a boot in which the raw ports deliver a scancode with our
code owning the controller, which would make rung 2's zero a defect rather
than the board's shape.

**This does not close BLU-F19.** BLU-F19 is about what the 2026-07-08 record
CITED as its confirmation, which is OVMF and an ACK byte, and a later
measurement that happens to support the conclusion does not turn a bed
citation into a metal one.
### BLU-F3 IS CLOSED, by the story that names both the machine and the mechanism

`TheSilentKeyboard.md:61-70`, verified at its lines by blu, closes the
question this part opened. The entry covers 2026-07-04 to 2026-07-08: "The
boot arc lands on real metal. First-boot ceremony runs **on the ASUS**:
passphrase, entropy, Ed25519 keygen, on glass, no OS. **The keyboard that
types this ceremony is the USB keyboard -- impersonated as PS/2 by the BIOS''s
SMM legacy emulation.**"

The same story records what happened to that fallback three days later
(`:77-81`): the 2026-07-11 spec-fidelity campaign''s ownership handoff,
"done first and by the book, **disables the SMM keyboard emulation** -- the
fallback that had been typing ceremonies since 7/08. The pure path is not yet
proven; the impure path that worked is now dead. **Nobody files a flight-plan
amendment.**"

**BLU-F21. The 2026-07-08 machine was the ASUS, the "PS/2 keyboard" was a USB
keyboard impersonated as PS/2 by the BIOS''s SMM emulation, and the fleet''s
own ownership handoff disabled that emulation on 2026-07-11, therefore rung
2''s "no PS/2 on this board" on 2026-07-29 measured a board whose working
input fallback we had removed three weeks earlier.**
Supporting evidence: `TheSilentKeyboard.md:61-70` for the machine and the
mechanism; `:77-81` for the handoff disabling the emulation and for nobody
filing an amendment; `HardwareSitting.md` rung 2 stage C for the 2026-07-29
zero-arrivals measurement; BLU-F20 for the emulation returning when the
firmware is given the controller back.
Falsifying evidence: a record of the SMM emulation being live on 2026-07-29
with our code owning the controller.

**BLU-F19 stands and is sharpened rather than closed by BLU-F21.** BLU-F19 is
about what the release record CITED: `GitHubUpdate34.md` announced "the PS/2
keyboard works" for a keyboard that was USB behind an emulation, and
`BootRoadmap.md:85-90` cited OVMF and an ACK byte as the confirmation. The
mechanism being now known does not make either citation a metal measurement of
a PS/2 keyboard.

## 03.3b The stories' account of the boots

*Written by val at root's direction, 2026-09-09, because part 08 reads every
story anyway. Six stories are read in full here: `TheSilentKeyboard.md`
(16,873 bytes, 331 lines), `TheShotThatWorkedOnce.md` (6,651, 134),
`TheBedThatAlwaysSaidYes.md` (9,863, 175), `TheSecondStick.md` (28,491, 567),
`TheImageThatWasTwoDaysOld.md` (51,118, 973) and `ProseHasNoRunner.md`
(45,864, 934). `TheKeyboardWasNeverSilent.md` and `TheStickDidNotBoot.md` are
blu's, read and cited in section 03.5. Every row below is what a story SAYS,
cited to file and line; a story is not a flight record, therefore the ledger
drawn from `HardwareSitting.md` is the authority where the two differ.*

### What the record says

| # | date | image | what the story says happened | citation |
|---|---|---|---|---|
| S1 | 2026-07-04 to 2026-07-08 | not named | The boot arc lands on real metal; the first-boot ceremony runs on the ASUS, typed by the USB keyboard impersonated as PS/2 by the BIOS's SMM legacy emulation. Identity save to the stick FAILS. | `TheSilentKeyboard.md:61-70` |
| S2 | 2026-07-11 | not named | The spec-fidelity campaign (CLs 7460 to 7464) makes the bus visible, and the ownership handoff disables the SMM keyboard emulation that had been typing ceremonies. | `TheSilentKeyboard.md:72-81` |
| S3 | 2026-07-12 | not named | CL 7466 moves the interrupt endpoint from one completion ever to a serviced stream. The keyboard enumerates fully and keystrokes still do not decode. | `TheSilentKeyboard.md:83-96` |
| S4 | 2026-07-13 | the three-phase probe | The probe is flashed, boots, and runs all three phases on the ASUS. **The stick comes back byte-identical to what was flashed**, therefore every reading died with the framebuffer at power-off. The human's naked-eye report is the only surviving datum. | `TheSilentKeyboard.md:98-116` |
| S5 | 2026-07-29 | `pci-probe.img` | Flashed to the 28.9 GB stick; the ASUS TUF did not boot it. Unexplained, and `TheStickDidNotBoot.md` is the authority. | `TheSecondStick.md:16-26` |
| S6 | 2026-07-29 | `pci-probe.img` | The same afternoon: the flasher threw on the readback of the last 34 sectors of a correctly written stick, the run sheet routed the operator to the second stick, and the second flash failed identically because the defect is deterministic. Fixed at main 12035, 14:01:40. | `TheSecondStick.md:95-107`, `:441-446` |
| S7 | 2026-07-29 | `pci-probe.img` | What came home from that sitting: "stick doesn't boot", one bit, no photograph. Rung 3 was never run. | `ProseHasNoRunner.md:44-45` |
| S8 | date not given in the story | `seed/Codex.img` | Fester's A1 row, quoted verbatim by the story: "reboot-looped under real UEFI: 159 attempts in 360 seconds, nothing painted, not one byte on COM1, while the Option A stub booted on the same firmware". The story names the control inside that quotation as the part that makes the measurement mean something. | `TheImageThatWasTwoDaysOld.md:417-419` |
| S9 | before 2026-08-05 | `build/boot/ceremonyboot.img`, `C423418D` | The firstboot ceremony flights: nine or ten F12 shots taken on one stick, before and after a reboot, past the ASUS's 54-second timer death. Every shot landed. | `TheShotThatWorkedOnce.md:21-25`, `:107-109` |
| S10 | 2026-08-06 onward | `F4D76CC4` and `AFDCE0EA` | Both A6 flights: F12 saved exactly one shot per boot, and every later press failed with "no ESP" although the partition read back intact on the dev box. The keyboard kept working. | `TheShotThatWorkedOnce.md:27-31` |
| S11 | 2026-08-07 | back to `C423418D` | The decision: A6 comes off the flight stick, and the stick returns to the last configuration whose behaviour was proven by use. | `TheShotThatWorkedOnce.md:103-113` |
| S12 | about 2026-08-10 to 2026-08-14 | not named individually | By Damian's count, about twenty misfire flashes across four days, about 3 million tokens by one lane and another half million by its relief. | `TheBedThatAlwaysSaidYes.md:8-11` |
| S13 | within S12 | three images | Three dead images carried `-EntryStart`, which spins forever under live firmware. | `TheBedThatAlwaysSaidYes.md:15-19` |
| S14 | within S12 | not named | A wrong-volume MAGENTA stall: `LocateProtocol` bound the first Block I/O handle, and a foreign volume mounted clean and held no SOURCE.SRC. | `TheBedThatAlwaysSaidYes.md:27-33` |
| S15 | within S12 | the `a5fix` return | A second MAGENTA: the stub's allocation came back holding a seed value, so the heap base was this board's framebuffer and records read back as two magenta pixels. | `TheBedThatAlwaysSaidYes.md:35-41` |
| S16 | within S12 | not named | The seven-hour ORANGE: `AllocateAnyPages` satisfied from the top of a 32 GB board, a bounded heap-position signature trapped, and the machine was halted rather than slow, with the last painted screen still up. | `TheBedThatAlwaysSaidYes.md:49-57` |
| S17 | 2026-08-14 | `a5flight-returned-20260814.img` | The green flight: the board's OUT.CDX byte-identical to the host control. | `TheBedThatAlwaysSaidYes.md:3-8` |

### What the record means

**VAL-S1. Seven of the seventeen accounts name no image at all, and the two
largest episodes name none individually.** S12 is "about twenty misfire
flashes across four days" and S1 to S4 is "six-plus human
flash-walk-boot-photograph cycles" (`TheSilentKeyboard.md:38-40`). No
per-boot ledger is recoverable from the stories for either episode,
therefore a count of the era these stories cover carries that gap as its
error bar rather than as a number.

**VAL-S2. The most expensive boots returned the least data, and one returned
none at all.** S4 flashed a probe built to answer three named questions, ran
all three phases on the board, and came home byte-identical: the telemetry
channel was USB mass storage, which S1 records as never having moved one
byte on that machine (`TheSilentKeyboard.md:68-70`). S7 returned one bit and
no photograph. A boot that returns one bit cannot distinguish the five
failures the run sheet's own table enumerates.

**VAL-S3. Three separate boots, three separate lanes, one shape: the
instrument was routed through the subject.** S4 lost every reading to a
stick that was the subject of the campaign. S6 lost an afternoon to a
verifier whose failure the run sheet had pre-assigned to the medium. S8's
image could not contain the change the instruction to re-flash existed to
verify.

**VAL-S4. One of the seventeen is a green flight, and the green flight is
the one whose exact bytes had completed the full mission in the bed first.**
S17 is the single unambiguous success in this set, and
`TheBedThatAlwaysSaidYes.md:99-118` records that the image nearly did not
fly: at the 8 MB default the write-back died `DISK-OUT: FAILED -1`, caught
because the exact bytes were run through boot, read, compile, write, extract
and byte-compare in the bed during the wait for the human.

Falsifying evidence for any row above is the flight record itself: where
`HardwareSitting.md` and a story disagree about a date, an image or an
outcome, the flight record is the authority and this section is the
secondary account.

## 03.4 What the record means: findings

**BLU-F1. The flight record was created on 2026-07-28, after 76 percent of
the project's calendar and 82 days after the ASUS entered the depot, and no
per-boot record of the first 82 days survives.**
Supporting evidence: CL 11775 adds `docs/HardwareSitting.md` on 2026-07-28;
CL 1104 names the ASUS on 2026-05-07; the arithmetic of section 03.1.
Falsifying evidence: a per-boot record for 2026-05-07 to 2026-07-28 under
another path, which the enumeration of section 03.3 is the search for.

**BLU-F2. The project's first physical target was a phone, the campaign
reached nine flash attempts by day 11, and every attempt failed in the
part's bootloader rather than in anything the fleet built.**
Supporting evidence: `CurrentPlan-2026-03-24-peak2-complete.md:23` and
`:286`; `CurrentPlan-2026-03-24-peak3-evening.md:37-39`;
`docs/PM/Done/Suspended/Phone/PHONE-WIPE.md:196-206` for the nine attempts
and `:236-242` for the diagnosis; attempt 9 flying an official Jenkins-built
TWRP image and failing identically.
Falsifying evidence: a tenth attempt recorded elsewhere, or an unlock of the
bootloader that changed the result.
**CORRECTED 2026-09-09.** An earlier revision of this finding claimed the
attempt log did not survive, on a negative taken over the path the March plan
named rather than over the file. Red found the file at its moved path with
`p4 filelog -i`; the claim was blu's and the correction is recorded here
rather than silently swapped, because the ERROR is the same shape the finding
now describes.

**BLU-F2a. On day 11 of the project the fleet wrote down that the boot
question could not be answered by any bed, and the project then spent five and
a half months answering board questions in beds.**
Supporting evidence: `PHONE-WIPE.md:19-29`, a verification table dated
2026-03-24 in which seven bed-checkable rows read "Proven" and the row "This
image actually boots on an S7 Edge" reads "NOT PROVEN -- cannot be emulated";
`PHONE-WIPE.md:31-34`, "No emulator exists that can tell you 'this TWRP image
will boot on this phone'"; `phone/EMULATOR-PLAN.md:117-126`, a risk table
rating every software step "None" and the flash "Medium" with the mitigation
"Only after all validations".
Falsifying evidence: a document between 2026-03-24 and 2026-07-28 that carries
the same statement forward into the ASUS campaign, which would show the
knowledge was kept rather than suspended with the phone.

**BLU-F3. The release record and the flight record disagree about whether the
board has a working PS/2 keyboard, twenty-one days apart, and neither
document names the machine precisely enough to resolve the disagreement.**
Supporting evidence: `GitHubUpdate34.md:12-20`, `:29` and `:65-66` against
`HardwareSitting.md#31:6438`.
Falsifying evidence: a changelist in the range 7228 to 7355 naming the
machine of the 2026-07-08 boot, or naming `-Ebs` on the payload that took the
keystrokes.

**BLU-F4. The release record carried no claim about a board for the first 33
of its 53 cycles, therefore the one artefact published outside the fleet
could not have told a reader that a hardware campaign was running.**
Supporting evidence: the measured table of section 03.2, 31 files of 53 with
zero mentions; Update 33 dated 2026-07-07 and Update 34 dated 2026-07-08.
Falsifying evidence: a metal claim in updates 2 to 33 spelled a way the eight
search terms cannot see, which is the limit this count states about itself
(L-CENSUS).

**BLU-F5. The undocumented era is recoverable for the phone and unrecoverable
for the ASUS, and the difference is that the phone campaign kept a per-attempt
log and the ASUS campaign kept none until 2026-07-28.**
Supporting evidence: `PHONE-WIPE.md` tables nine attempts by change and result
and survives at its moved path; no equivalent file exists for the 82 days of
section 03.1; the release record named no board for 31 of 53 cycles (BLU-F4);
Perforce holds nothing before 2026-04-17 (part 02, section 02.1).
Falsifying evidence: a per-boot record of the ASUS between 2026-05-07 and
2026-07-28 surviving under any path, which `p4 filelog -i` over the candidate
paths is the search for.
**CORRECTED 2026-09-09**, together with BLU-F2: the earlier revision named the
attempt log as one of four mechanisms and the log survives.

**BLU-F6. On 17 of the 52 boots the record names the bank or the medium as
having failed to carry the reading, and the channel independent of the
subsystem under test flew once, on the last flight.**
Supporting evidence: rows 6, 16, 26, 29, 34, 36, 37, 39, 40, 41, 44, 45, 49,
50, 52, 53 and 54 of `A-sittings-table.md`. Rows where the ladder stopped with
the bank healthy (47, 48) are excluded, because a stopped ladder is not a
failed medium. The record channel is `DiagRecord`, aboard first on sitting 15.
Falsifying evidence: a flight before sitting 15 carrying a register that does
not pass through the medium, which the peer log of sittings 11, 12 and 13
partly is, and which is why the count above is of the BANK rather than of the
flights that returned nothing.

**BLU-F7. The rate of flights lost to our own instrument did not fall across
the campaign: the first flight and the last flight are both instrument
losses.**
Supporting evidence: row 1 (a stub whose every failure path ended at
`jmp fatal`, and a flash procedure that destroyed the GPT) and row 54 (a
record channel with no serial line ahead of its three steps); the eleven rows
of the instrument table in `A-sittings-table.md`, spread across 2026-07-29,
2026-08-05, 2026-08-09, 2026-08-10, 2026-08-11, 2026-08-16, 2026-08-21 and
2026-09-09.
Falsifying evidence: a measure of instrument losses per boot by period showing
a fall, which the eleven rows across eight dates do not support.

**BLU-F8. Every campaign that ENDED was ended by a control arm rather than by
a fix, and the control arm was in each case cheaper than the flights that
preceded it.**
Supporting evidence: the keyboard campaign ended on a probe answering in six
whole-screen colours, which eliminated three classes in one metal reading
(`TheKeyboardWasNeverSilent.md:24-31`); the link question was settled by a
read-only touch row taken before any write (row 29); the sink threshold came
from a rung ladder that banked each rung before the next risked the medium
(row 44); `rdh-writable=y` came from writing the register and reading it back
(row 44). The A5 campaign ended when the artifact was compared against a host
control byte for byte (row 35).
Falsifying evidence: a campaign in this record closed by a repair with no
control arm beside it.

**BLU-F9. Damian is named in the decision column of 6 of the 54 rows, and the
other 48 decisions were taken by the fleet.**
Supporting evidence: rows 14, 18, 25, 42, 51 and 54 of `A-sittings-table.md`,
counted mechanically from the table's last column. No row's decision column is
blank.
Falsifying evidence: a decision recorded elsewhere for a row whose column
names an agent, which part 09's rulings ledger is the place to look.

## 03.5 What the record says: the documented era, and where the ledger lives

Every boot from attempt 1 on 2026-07-29 to sitting 15 on 2026-09-09 is one
row of `A-sittings-table.md`, landed on main at CL 25224 and verified at head
at 54 rows. The table's own counts, computed from the table rather than
carried forward: 52 boots, of which 17 answered every question aboard, 26
answered part, and 9 answered nothing; two rows are not boots. **Eleven rows
were lost to our own instrument rather than to the board**, and the table
names the mechanism for each of the eleven.

### 03.5.1 The ladder, 2026-07-29, rows 1 to 5

Attempt 1 flashed `pci-probe.img` and the board did not boot the image. **The
whole observational record of that flight is four words**, "stick doesn't
boot", and the paper written the same afternoon states the reason in its own
first line: "I do not know why the stick did not boot, and the reason I do not
know is a process failure that is mine" (`TheStickDidNotBoot.md:11-12`). The
paper names three errors before the stick went in, and the first is the one
every later flight inherits: an image was verified under OVMF with the
script's DEFAULT arguments and a DIFFERENT image was flashed, built with
`-Seed '' -Font ''`. The payload bytes were identical and were reported as
provenance, correctly; **the payload is the cargo and the failure modes at
issue live in the vehicle** (`:98-120`).

Attempt 2 flew the same afternoon as a four-rung ladder and answered three of
the four questions. Rung 1 established that the stick boots, at 1920x1080 with
a stride 128 pixels wider than the visible width. Rung 2 named the part the
whole of Track B was blocked on, an Intel I219-V at `00:1f.6` with `MAP=ok`,
and answered the input question in the direction nobody wanted: no PS/2 at
all, therefore USB HID after ExitBootServices is the only input path the
machine has. Rung 4 flew on that answer and returned `EPINT=0 SCANS=0` with a
key held, against `EPINT=12 SCANS=12` for the same image under OVMF with keys
injected.

**Rung 3 never flew, and the gate that dropped it was the ladder's own
defect.** Rung 2 reported `disk=n`, the rung was gated on that field, and
`disk=n` was a false negative: the stick sits on the SECOND xHCI and
`xhci-connect` took the first controller and stopped. The sheet records the
shape in its own words: "a probe that cannot report 'I did not look there'
reports 'it is not there'".

### 03.5.2 The keyboard, 2026-08-02 to 2026-08-05, rows 6 to 14

Nine boots over four days, and the campaign the nine boots ended had been running for
two months. The arc is five probe versions and then three desk boots.

`kbd-diag-v11` closed the display defect on metal and took the first Intel
xHCI readings ever taken off the board. v12 and v13 narrowed the silence to
the controller's own testimony: the endpoint claims RUNNING, the internal
dequeue never leaves the ring base. **v14 killed the v13 verdict with a
spec-derived instrument**, reading FSE code 26, Stopped-TD-IN-PROGRESS, which
says the controller fetched the TRB and is issuing transactions on the wire.
v14 also froze at paint 2,428, and the freeze was the probe's own unbounded
allocation from v1 onward, which is R-COST's exact red flag present in every
version.

**v15 delivered.** `EPINT=97` and climbing, and the cause of sixteen versions
of silence was `SET_IDLE(0)`, a request the driver had sent from the day the
driver was written. `TheKeyboardWasNeverSilent.md:200-211` states what the
finding cost and why the contrast misled: "The firmware never sends it, which
is exactly why BIOS setup always typed fine on the same hardware -- and we
read that contrast as proof our bug was somewhere else, because the part
demonstrably worked when somebody else drove it."

The same file records a second ending, and the second ending is the one this
investigation needs. The fix that passed in the emulator was the WRONG fix:
`uefi-read-key-ex` locates `EFI_SIMPLE_TEXT_INPUT_EX`, which is UEFI 2.x and
OPTIONAL. OVMF implements the protocol and the board's AMI Aptio V of 2015
does not, therefore the two-arm proof went green on the dev box and the same
build returned -1 forever on the board (`:7-16`). **A green emulator arm is
not a proof about hardware when the thing under test is an OPTIONAL part of a
specification** (`:18-22`), which is L-OPTIONAL, and the row that ended it was
a probe answering in six whole-screen colours rather than in pass and fail
(`:24-31`).

The desk boots then closed the input campaign on metal: row 12 typed through
our own driver and proved FOUR keyboard-shaped interfaces exist with the
typed-on one bound fourth, row 13 typed and then died mid-session on the
completion-steal defect, and row 14 came back all green with the mouse, the
clicks and the F12 shots working. Damian's own words on row 14 are "it all
works".

### 03.5.3 Storage and the write path, 2026-08-05 to 2026-08-11, rows 15 to 27

The storage arc answered a question a bed could not seat and then failed
repeatedly on the write. Row 15 read the disk sitting ABOVE root port 7, an
arrangement no bed can produce. Row 17 answered the 64 KB crossing with a
checksum pair and a calibrated negative arm beside the two green rows. Row 19
put WORKS-8 on metal and found a data-phase timeout three defects deep, and
two of the three defects were in code that had never once executed.

**Rows 20 to 23 are four consecutive flights of the write path, and three of
the four answered nothing about the write path.** Row 20 flew a payload whose
block I/O was raw IDE port access against a USB stick. Row 21 flew a `-Uefi`
payload whose only success signal was a channel that had never rendered a
character on the board. Row 22 flew a ladder that printed to ConOut BEFORE
painting on every rung, therefore green was the only answer reachable for any
failure at or before the first print. Row 23, the same ladder painting first,
returned WHITE and proved the UEFI block write path on the board with the
evidence on the medium rather than in the guest's own readback.

Row 27 then took the same question to 2.7 MB and held ORANGE, and the returned
stick settled what the glass could not: no directory entry was ever created,
therefore the fault is at or before the first allocation.

### 03.5.4 A5, the compiler on the box, 2026-08-09 to 2026-08-14, rows 20, 21, 28, 33 and 35

Three flights wrote nothing and one flight closed the campaign. Row 28 held
the stub's dark green for twenty minutes, and the diagnosis was `-EntryStart`:
the flag enters `__start`, which takes the hardware over with boot services
still live, and `emit-wait-for-tick` never returns on a board codex-vm
emulates and the board does not. Row 33 diagnosed every MAGENTA stall as the
WRONG VOLUME, because `LocateProtocol` returns the first Block I/O handle and
the UEFI specification leaves the order unspecified. **Every bed has exactly
one disk, therefore no bed could ever present a wrong first handle.**

Row 35 closed A5: the compiler compiled itself on the ASUS and handed back
`OUT.CDX` byte-identical to the host control. The defect that had held the
campaign for days was AMI satisfying `AllocateAnyPages` from the TOP of RAM,
therefore the heap landed above 4 GB and bounded heap-pointer types trapped on
the first `pitch`. OVMF and codex-vm both allocate below 4 GB at every tested
size, therefore no stock bed could express the condition.

### 03.5.5 The NIC, 2026-08-11 to 2026-08-16, rows 26, 29, 30, 34, 36 and 37

Part 04 owns the technical thread. The ledger records what each flight
decided. Row 26 died in the reset before either arm the image existed to
compare. Row 29 reversed the order, painted every row, and proved the link
comes up under our code with a read-only touch row as the control. Row 30
established that CTRL is read-only on this part, which killed the reset
hypothesis outright and left the 2026-08-11 wedge unexplained again. Row 34
answered NIC-1 and NIC-2 and wedged on NIC-3. Row 36 decomposed the wedge into
ten steps and found no wedge at all: 93 seconds, of which 92.9 are one loop,
and the cost had been diagnosed on 2026-08-04 and routed AROUND rather than
bounded. **Row 37 then hung on the fix for row 36**, because bounding the
aneg wait removed the dead time in which the link came up.

### 03.5.6 The grouped ladder, sittings 1 to 15, 2026-08-18 to 2026-09-09, rows 38 to 54

Seventeen boots of one instrument. The ladder answered a great deal: the box
named from SMBIOS, `pci ok` on metal, the sink threshold at 16 sectors,
`rdh-writable=y`, the frame arriving in the window and not being written back,
K1 enabled on the board, firmware holding the MDIO ownership, the hang located
inside `e1000-reset`, and a real TCP conversation with the dev box on three
separate flights.

**And across those seventeen boots the medium is the recurring subject rather
than the recurring instrument.** Sittings 7 to 12 each lost the bank at or
before the stage the flight existed to read, at four different places: the
sink at stage 9, `rings-link` inside the NIC bring-up, b3's fourth note, and
`pchk1`. Sitting 13 lost it at `kbd`, stage 9, eight stages before the ladder
noticed. Sitting 14 kept only the first write and not that write's directory
entry, which is the write-back-cache reading of WORKS-62. Sitting 15 never
opened the bank at all.

The independent channel that would have made the medium a subject rather than
the register was designed, built and flown ONCE, on sitting 15, the last
flight there will ever be. On that flight the channel never printed, and the
composition carried no serial line ahead of the three steps the box stopped
inside, therefore the glass cannot say which of the three stopped it.

## 03.6 Still to be written

Each item names the corpus the charter assigns, therefore a reader of this
partial landing knows what is absent rather than inferring completeness:

- **03.3a is WRITTEN, above.** What remains of the enumeration is the
  transcripts through `build/transcript-extract.ps1` and
  `docs/Designs/Done/Tools/HardwareBringUpPlaybook.md` (8,233 bytes), neither
  read; the floor of 13 named boot events is a floor over the three censused
  corpora and rises if either names a boot.
- **The eight stories are READ.** `TheKeyboardWasNeverSilent` and
  `TheStickDidNotBoot` are read and cited in section 03.5; the other six are
  read in full in section 03.3b (val, 2026-09-09), whose table carries every
  boot each story names, with dates, images and outcomes cited to file and
  line. What remains is the reconciliation of 03.3b's seventeen accounts
  against the flight record's own rows, which belongs with the ledger.

## 03.7 Coverage: what this part read, in full

| path | size | revision |
|---|---|---|
| `docs/Hardware/HardwareSitting.md` | 437,270 bytes, 6,904 lines | `#31` at main 25193 |
| `docs/PM/Active/Stories/TheLostParadise.md` | 13,216 bytes | `#1` at main 25188 |
| `docs/PM/Done/Plans/CurrentPlan-2026-03-24-peak2-complete.md` | 17,319 bytes | at main 25255, lines 23 and 286 |
| `docs/PM/Done/Plans/CurrentPlan-2026-03-24-peak3-evening.md` | 4,922 bytes | at main 25255, lines 37 to 39 |
| `docs/PM/Done/Plans/CurrentPlan-2026-03-26-evening.md` | 5,997 bytes | at main 25255, lines 9, 41 and 121 |
| `docs/PM/Done/GitHubUpdates/GitHubUpdate34.md` | 10,874 bytes | at main 25255, lines 1 to 60 |
| `docs/PM/Done/Suspended/Phone/PHONE-WIPE.md` | 10,912 bytes, 264 lines | at main 25306 |
| `docs/PM/Done/Suspended/Phone/CODEX-PHONE.md` | 13,069 bytes | at main 25306 |
| `docs/PM/Done/Suspended/Phone/TWRP-BUILD-HANDOFF.md` | 8,583 bytes | at main 25306 |
| `docs/PM/Done/Suspended/Phone/TWRP-SESSION-HANDOFF.md` | 2,282 bytes | at main 25306 |
| `docs/PM/Done/Suspended/Phone/phone/EMULATOR-PLAN.md` | 5,613 bytes | at main 25306 |
| `docs/PM/Done/Suspended/Phone/ARM64-QEMU-VERIFICATION.md` | 3,284 bytes | at main 25306 |
| `docs/PM/Active/Stories/TheKeyboardWasNeverSilent.md` | 12,527 bytes, 212 lines | at main 25317 |
| `docs/PM/Active/Stories/TheStickDidNotBoot.md` | 35,896 bytes, sections 0 to 2.1 | at main 25317 |
| `docs/Designs/Done/Hardware/REAL-HARDWARE-BRINGUP.md` | 4,541 bytes, 99 lines | at main 25333 |
| `docs/Designs/Done/Hardware/BootRoadmap.md` | 34,420 bytes; lines 1 to 200 and 515 to 543 read, the remainder by targeted search | at main 25333 |
| `docs/Designs/Active/OS/I219IsNotAnE1000.md` | lines 38 to 39, the `roughly 150 times` figure | `#1` at CL 18317 |
| `docs/Designs/Done/Tools/HardwareBringUpPlaybook.md` | 8,233 bytes, 168 lines | at main 25346 |
| `docs/PM/Active/Stories/TheLostParadise/02-timeline.md` | 36,715 bytes | sections 02.11 and 02.12 at main 25255 |
| `docs/PM/Active/Stories/TheSilentKeyboard.md` | 16,873 bytes, 331 lines | in full, at main 25350 (val, section 03.3b) |
| `docs/PM/Active/Stories/TheShotThatWorkedOnce.md` | 6,651 bytes, 134 lines | in full, at main 25350 (val, section 03.3b) |
| `docs/PM/Active/Stories/TheBedThatAlwaysSaidYes.md` | 9,863 bytes, 175 lines | in full, at main 25350 (val, section 03.3b) |
| `docs/PM/Active/Stories/TheSecondStick.md` | 28,491 bytes, 567 lines | in full, at main 25350 (val, section 03.3b) |
| `docs/PM/Active/Stories/TheImageThatWasTwoDaysOld.md` | 51,118 bytes, 973 lines | in full, at main 25350 (val, section 03.3b) |
| `docs/PM/Active/Stories/ProseHasNoRunner.md` | 45,864 bytes, 934 lines | in full, at main 25350 (val, section 03.3b) |

Extractions run, exactly as invoked:

```powershell
p4 describe -s 1104
p4 filelog -i //Codex/main/docs/Hardware/HardwareSitting.md
p4 describe -s 11775
p4 filelog -i //Codex/main/docs/PM/Done/Suspended/Phone/PHONE-WIPE.md
p4 files //Codex/main/docs/PM/Done/Suspended/Phone/...
# the 03.3a census, over Done/Plans, Done/Handoffs and Designs/Done/Hardware
$pat = '(?i)\b(flashed|flash attempt|flew|flight|sitting|booted|boot attempt|Odin|on the phone|on the ASUS|on metal|real hardware|the board|the stick)\b'
Get-ChildItem docs\PM\Done\GitHubUpdates -File | ForEach-Object {
  $t = [System.IO.File]::ReadAllText($_.FullName)
  ([regex]::Matches($t,'(?i)\b(flashed|flew|flight|sitting|the ASUS|on metal|real hardware|the board)\b')).Count }
```

**What is NOT read, and why.** The four corpora of section 03.6, because the
enumeration is the next unit rather than a decision already taken.
`docs/PM/Done/GitHubUpdates` beyond Update 34's first
60 lines, because the count of section 03.2 is a census over all 53 files and
the reading of one file is the follow-up the census points at.

**One limit this part states about itself.** The census of section 03.2
counts eight spellings. A metal claim written without any of the eight is
invisible to the census, therefore BLU-F4 is a statement about the eight
spellings and not about every possible sentence (L-CENSUS).
