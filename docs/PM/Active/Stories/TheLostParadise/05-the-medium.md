# Part 05. The stick as medium

*Owner: fester. Part 05 of `docs/PM/Active/Stories/TheLostParadise.md`. The
subject is the medium: every bank loss, GPT rewrite, lost payload, wrong
volume, stale image, dump and archive; the flush; the sink; the USB stack on
the ASUS against the stack in the beds; what the medium cost in flights, and
why the record channel arrived as late as the record channel arrived.*

**Status: in progress. Sections land as each section verifies (charter rule 7).
Sections 1 through 6 are complete and verified. Section 7 is not written yet. FESTER-F11 in section 2 was corrected when section 3 measured the bed at
head.**

## How to read the two labels

Each section carries "What the record says", which is fact with a citation on
every claim, and then "What the record means", which is interpretation.
Findings are numbered `FESTER-F<n>`, one sentence each, with the evidence that
supports the finding and the evidence that would falsify the finding. Every
date is absolute and is the box's local time.

Charter rule 6 binds every sentence written here. Material inside quotation
marks is quoted verbatim from the source the citation names, therefore a quoted
sentence carries the source's words rather than part 05's.

**Two numbering notes, because the record uses three schemes.** The charter's
Terms fix "sitting" against "attempt", and a third scheme sits under both: the
archive rows in `HardwareSitting.md` name the early grouped boots "THE SECOND
GROUPED DIAG SITTING" and upward, and the later rows name theirs "SITTING 7"
upward. Where section 2 writes "sitting 2", "sitting 3" and "sitting 4", the
subject is the second, third and fourth GROUPED DIAG sitting of 2026-08-19,
which are attempts 39, 40 and 41 of appendix A. Every other sitting number in
part 05 is the record's own. The charter's "record channel" is the network
channel of part 06; part 05 says "the bank" throughout for the record on the
medium, per the Terms.

---

## 1. The first day of the medium: 2026-07-29

The medium entered the record on one afternoon, and three separate papers were
written about that one afternoon by three lanes. The three papers disagree
about which artifact failed, and the disagreement is itself evidence.

### 1.1 What the record says

**F-1.1.** A 28.9 GB USB stick was flashed with `pci-probe.img` and the ASUS TUF
did not boot the stick (`TheStickDidNotBoot.md` subtitle and section 1; the
paper reached main at CL 12045, verified by `p4 describe -s 12045`).

**F-1.2.** The complete observational record of the boot attempt was four words,
"stick doesn't boot" (`TheStickDidNotBoot.md` section 1). The paper names five
materially different events the four words collapse into: the firmware never
listed the stick; the firmware listed the stick and rejected the stick; the
stub began executing and died before painting; the stub reached the payload and
the payload faulted; the payload painted something unrecognisable
(`TheStickDidNotBoot.md` section 1.1).

**F-1.3.** The image that was verified under OVMF and the image that was flashed
were two different files built from one source with different arguments. The
verified image used the script's default arguments. The flashed image used
`-Seed '' -Font ''`, the command documented in `build/boot/diag/README.md`.
Payload bytes were confirmed identical (`text=140293 rodata=1468
opening=0x1933F PE=143872`) and image bytes were not
(`TheStickDidNotBoot.md` section 2.1).

**F-1.4.** The doctrine forbidding exactly that substitution existed on
2026-07-10, nineteen days before the flight, in `OsHardwareRoadmap`, "The
boot-image iteration loop": Loop A carries two gates, a structural GPT
validation and an OVMF boot of the image FILE, and the doctrine states that
payload work never needs a physical flash to iterate
(`TheStickDidNotBoot.md` section 2.1, quoting the roadmap). Neither gate was run
on the flashed image. The cost of running both gates was about ninety seconds
of QEMU (`TheStickDidNotBoot.md` section 2.1).

**F-1.5.** The ladder's first rung was specified as `xhci-probe.img` for a stated
reason, the narrowest question asked first. The flown first rung was
`pci-probe.img`, a payload written that day inside an image shape built that
day, and no revision of either had touched hardware before the flight
(`TheStickDidNotBoot.md` section 2.2).

**F-1.6.** The stick stayed inserted in a Windows box across two full writes and
one raw read, against a rule the roadmap records as learned in blood: flash,
verify, PULL, and the stick never returns to a Windows box between flash and
boot test (`TheStickDidNotBoot.md` section 2.3, quoting `OsHardwareRoadmap`
"Loop B"). The flasher's own log recorded that the stick could not be taken
offline: `could not offline disk: Removable media cannot be set to offline`
(`TheStickDidNotBoot.md` section 2.3).

**F-1.7.** The roadmap's second stick-handling bullet, randomize the disk GUID at
flash time, carried the words "pending patch" and was never implemented, and
`build-img` therefore stamps a deterministic disk GUID, and every image the
project has ever built therefore presents to Windows as one disk, and partmgr
caches a repair ruling by disk GUID (`TheStickDidNotBoot.md` section 2.3).

**F-1.8.** A structural comparison of the flashed image against a control image
the same motherboard demonstrably boots, `build/boot/optiona-milestone.img`,
found the flashed image sound at every layer: a valid protective MBR, a valid
GPT carrying the correct ESP type GUID `C12A7328-F81F-11D2-BA4B-00A0C93EC93B`,
a legal FAT16 volume of 26,350 clusters inside the legal range 4085 to 65524,
`\EFI\BOOT\BOOTX64.EFI` present at the fallback path, and PE headers identical
to the control in every structural field including Subsystem 10 and
AddressOfEntryPoint `0x1000` (`TheStickDidNotBoot.md` sections 3.1 through 3.4).

**F-1.9.** Every failure path in the stub's firmware-call chain ended in a
two-byte spin, `fatal: jmp fatal`, with no message, no beep, no colour and no
serial byte, therefore a board that cannot satisfy `LocateProtocol(GOP)`, cannot
grant 128 MB, or refuses `ExitBootServices` twice produces a machine
indistinguishable from a machine that never loaded the payload
(`TheStickDidNotBoot.md` section 6.1, quoting `build/boot/option_a_stub.asm`).

**F-1.10.** The flasher wrote the image, verified all 16,777,216 bytes by
readback, and then threw and exited 1 on the two `-SpecFit` GPT blobs at the
disk tail with "The drive cannot find the sector requested"
(`TheStickDidNotBoot.md` section 5; `TheSecondStick.md` sections 1 and 2).

**F-1.11.** The mechanism of the false failure was a buffer size. One handle
carried a 1 MB `FileStream` buffer, correct for the write. A buffered read of
512 bytes positioned 17,408 bytes from the end of a 60,506,112-sector device
issues a 1,048,576-byte `ReadFile`, overruns the medium by 2,014 sectors, and
the device refuses the read deterministically, on any good stick, of any size,
whenever `-SpecFit` is used (`TheSecondStick.md` section 2, with the arithmetic
per sector).

**F-1.12.** The full-image verify passed because the full-image verify never
reads near the end of the medium, therefore the defect's signature was
"everything near the start of the stick is fine and the far end cannot be
read", which is a textbook description of a worn flash device
(`TheSecondStick.md` section 3).

**F-1.13.** `HardwareSitting.md` section 3 had already assigned that signature one
cause: "The flasher verifies the whole image by readback; if it reports a
verify failure, the stick is the problem -- take the second one"
(`TheSecondStick.md` sections 0a and 6, quoting the run sheet). The operator was
therefore routed into swapping hardware and re-flashing, and the second flash
failed identically because the defect is deterministic
(`TheSecondStick.md` sections 6 and 8).

**F-1.14.** Nothing in the tree ran the flasher. A search returned two hits, both
inside `build/flash-usb.ps1` and both in the header comment, and no `*flash*`
test file existed anywhere in the repository. The gate is a text round trip, a
CDX fixed point and the BVT, none of which involve a block device
(`TheSecondStick.md` section 4).

**F-1.15.** The defect was unreachable by every instrument the project owned,
because the defect needs three conditions at once: a raw physical device, the
`-SpecFit` flag, and a real stick's geometry. Every emulator path the project
owns consumes image FILES, and a file short-reads at end of file rather than
failing (`TheSecondStick.md` section 4).

**F-1.16.** The question "does the flasher exit zero on a good stick" is
answerable on the dev box with a stick and an elevated shell, and the run
sheet's own governing rule at line 8 says every question answerable before the
sitting is answered before the sitting (`TheSecondStick.md` section 4, quoting
`HardwareSitting.md`).

**F-1.17.** I diagnosed the flasher correctly, went around my own instrument
before changing anything by reading the three tail sectors through an
unbuffered handle, checked structure rather than re-running the script's
arithmetic (`AlternateLBA=60506111`, backup header `MyLBA=60506111
AlternateLBA=1 EntryLBA=60506078`, the ESP type GUID), fixed the reader, and
closed an adjacent short-read hole in the same change
(CL 12033, copied up at CL 12035, both verified by `p4 describe`;
`TheSecondStick.md` section 5).

**F-1.18.** A third paper, `TheImageThatWasTwoDaysOld.md`, was written the same
day about a different artifact, `seed/Codex.img`, and carries a correction at
the top written within the hour stating that the paper answers the wrong
question, because the flashed artifact was `pci-probe.img`
(`TheImageThatWasTwoDaysOld.md`, the correction block, lines 1 through 24).

**F-1.19.** The measurements inside that third paper are correct and remain
correct: `seed/Codex.img` stood at depot revision #57 from CL 11000 of
2026-07-27 and `seed/Codex.cdx` stood at revision #582 from CL 11926, with 23
seed revisions absent from the image (`TheImageThatWasTwoDaysOld.md` section 1
and appendix A, rows A1 through A3; CL 11000 and CL 11926 verified by
`p4 describe`).

**F-1.20.** The image and the seed carried one identical modification time on
disk, 7/29/2026 2:07:57 PM, because `p4 sync -f` wrote both files at one moment,
therefore the modification time answers the freshness question wrongly rather
than merely failing to answer the freshness question
(`TheImageThatWasTwoDaysOld.md` section 3.4 and appendix A row A6).

**F-1.21.** The precondition that names the failure exactly, R8, is marked
BLOCKING, names the artifact, names the script, and predicts the symptom in
the words "a stale img is the failure that looks like a compiler bug", and R8
is discharged by "Ask red, do not infer", with no command and no exit code
(`TheImageThatWasTwoDaysOld.md` section 4.1 and appendix A row A11).

**F-1.22.** Among the 23 absent seed revisions, CL 11833 moved the panic printer
to COM1 and added the `cli` before `hlt`, and the run sheet told the operator to
listen on COM1 for that reason, therefore an operator listening where the sheet
said would have watched the one port a stale payload never writes to
(`TheImageThatWasTwoDaysOld.md` section 14.1; CL 11833 verified by
`p4 describe`). Three further absent revisions, CL 11098, CL 11112 and CL 11179,
are the guards that turned unchecked out-of-range memory access into traps
(`TheImageThatWasTwoDaysOld.md` section 14.2 and the table in section 14).

**F-1.23.** I fixed the instrument gap the same afternoon: every Option A image
gained two liveness marks painted from `option_a_stub.asm`, a dark blue after
GOP acquisition and a dark green after the page tables, and the run sheet gained
a table reading the three resulting states; both marks were confirmed by
ablation under OVMF rather than by inspection
(`TheImageThatWasTwoDaysOld.md`, the correction block, "What fester fixed before
this paper reached main", checked by blu at main CL 12083, verified by
`p4 describe -s 12083`).

**F-1.24.** Two findings of that third paper survived the same check and remained
open at CL 12083: R8 still had no runner, and `PciProbe` was still absent from
the ladder in the run sheet, therefore the artifact that flew was still the
artifact the governing document did not cover, with no digest recording, no
telemetry proof, and no symptom table written for the artifact
(`TheImageThatWasTwoDaysOld.md`, the correction block, "Two of section 8's
findings do still hold").

### 1.2 What the record means

**The medium's first day produced three papers and zero answers about the
board.** One flight was spent, and the flight returned one bit. The bit was
consistent with five different events, and the project could not tell the five
apart, therefore the flight bought nothing about the ASUS.

**The cause of the emptiness was decided before the stick entered the machine,
and the decision was mine.** Three choices compounded. First, I flashed an
artifact no gate had booted, treating payload identity as image identity, and
the doctrine forbidding exactly that had existed for nineteen days. Then, I put
an experiment where the ladder specifies a control, therefore a black screen
carried no information about anything above the first rung. Finally, the stub's
only failure mechanism was a silent spin, therefore no care at the machine could
have separated the five events. The first two choices are judgement. The third
is design, and the third is the one that made the other two unrecoverable.

**The tree already held every correction, in prose, unexecuted.** Loop A's two
gates existed. The ladder's control-first ordering existed. The flash-verify-PULL
rule existed. The disk GUID randomization existed as the words "pending patch".
R8 existed in bold under a heading reading BLOCKING. Each of the five was
correct, and no one of the five carried an exit code, therefore each of the five
was a comment. The medium's first day is the clearest instance in the whole
record of the project's characteristic failure: a correct instruction with no
runner, believed because the instruction is written down.

**The flasher episode shows the same shape one level lower, and shows the cost
of an instrument that cannot describe its own failure.** The flasher could fail
for two reasons and reported both identically. The run sheet then converted the
undistinguished failure into an instruction to swap hardware. A human spent an
afternoon on a PowerShell buffer size. The remedy that closes the class is not
the buffer fix: the remedy is an instrument whose output separates "the bytes
differ" from "I could not read the bytes", and that remedy remained unbuilt on
2026-07-29.

**The third paper is evidence about the fleet rather than about the medium.**
Three lanes wrote three papers about one afternoon within hours, two of the
three papers analysed an artifact that was never flown, and one of the two
corrected itself only after reading the other. The fleet's capacity to
investigate exceeded the fleet's capacity to establish which artifact had
actually flown. The cost was two lanes' sessions, and the cause was that no one
recorded the flown artifact's digest at the moment of the flight, which is
precisely what the run sheet's step 1 exists to do and precisely what the flown
artifact skipped by never appearing on the ladder.

### 1.3 Findings

**FESTER-F1.** The first flight of a stick was flown with an artifact no gate had
booted, and the doctrine requiring both gates predates the flight by nineteen
days.
*Supported by:* F-1.3, F-1.4.
*Falsified by:* evidence that `pci-probe.img` as flashed passed a structural GPT
validation and an OVMF boot of that exact file before the flash. No such record
exists in the depot at CL 12045.

**FESTER-F2.** The first rung of the ladder was an experiment rather than a
control, therefore the flight's result could not be attributed to the board, the
stick, the flash or the payload.
*Supported by:* F-1.5, F-1.2.
*Falsified by:* a control boot flown the same sitting. The record carries none.

**FESTER-F3.** The stub's single silent failure mechanism made five distinct
events indistinguishable at the machine, therefore no amount of care by the
operator could have produced an attributable result.
*Supported by:* F-1.9, F-1.2.
*Falsified by:* any channel present in the flown image that distinguishes the
five. The flown image carried a screen channel for the success case alone
(F-1.2).

**FESTER-F4.** A verified instrument reported a false failure whose signature
matched a documented hardware fault, and the governing document had already
assigned that signature one cause, therefore the operator was routed away from
the truth by procedure rather than by inference.
*Supported by:* F-1.10 through F-1.13.
*Falsified by:* a run-sheet revision at the time of the flight naming two
candidate causes for a verify failure. Revision #4 named one (F-1.13).

**FESTER-F5.** The flasher had zero automated coverage by construction, and the
question the coverage would have answered was answerable on the dev box, which
the run sheet's own governing rule required to be answered before the sitting.
*Supported by:* F-1.14, F-1.15, F-1.16.
*Falsified by:* any invocation of `build/flash-usb.ps1` from `build/`. A tree
search returned two hits, both inside the file's own header comment (F-1.14).

**FESTER-F6.** Five correct instructions covering the medium existed in the tree
before the flight, and no one of the five carried an exit code, therefore all
five were comments.
*Supported by:* F-1.4, F-1.5, F-1.6, F-1.7, F-1.21.
*Falsified by:* a runner for any of the five at CL 12045. R8's runner was still
absent at CL 12083 (F-1.24).

**FESTER-F7.** A derived artifact tracked in version control under a mandatory
`p4 sync -f` carries a modification time that answers the freshness question
wrongly rather than failing to answer, therefore a diligent check of R8 returns
a false yes.
*Supported by:* F-1.19, F-1.20, F-1.21.
*Falsified by:* a modification time on `seed/Codex.img` that differs from the
seed's after a force sync. Both read 7/29/2026 2:07:57 PM (F-1.20).

**FESTER-F8.** The two liveness marks that convert a black screen into a
positioned failure were proposed, built, verified by ablation and folded into
the governing document inside one afternoon, therefore the instrument gap was
cheap to close and was closed only after a flight was spent.
*Supported by:* F-1.23, F-1.9.
*Falsified by:* evidence of the marks before 2026-07-29. The stub's failure path
at the time of the flight was `fatal: jmp fatal` alone (F-1.9).

---

## 2. The bank: the medium as the record channel, and every loss

From 2026-08-18 the diagnostic ladder wrote findings to the stick as a file
named `DIAG.TXT`, the bank. The bank is the record channel the charter asks
about. The bank failed or stopped short on eleven attempts, each time differently
enough to look like a new defect, and the mechanism was one missing SCSI
opcode.

### 2.1 What the record says

**F-2.1.** Damian ruled on 2026-08-11 that a stick is dumped before an image is
flashed over the stick, the dump goes to `D:\Projects\stick-archive\`, and a
row is added to the archive table (`HardwareSitting.md:1-5`). The dump command
is read-only and does not mount, because mounting a FAT volume makes Windows
allocate clusters and manufacture the evidence the sitting asks about
(`HardwareSitting.md:86-90`).

**F-2.2.** The archive holds a returned stick per flight from 2026-08-13 onward,
each with a SHA-256 and a description of what the guest wrote
(`HardwareSitting.md:100-149`, the archive table, 33 rows).

**F-2.3.** Sitting 2 (2026-08-19, image `601103D9`) returned a `DIAG.TXT` of
4,577 bytes ending after the `block` stage, and the sink refusal took the bank
with the sink refusal (`HardwareSitting.md`, archive row
`diag2-returned-20260819.img`).

**F-2.4.** Sitting 3 (2026-08-19, image `C46DE4DD`) returned a `DIAG.TXT` of
4,577 bytes ending after `block` exactly as sitting 2 did, and the flight's own
entry records that the bank after the sink refusal is lost on metal at the same
time as the SUMMARY row paints `bank=ok`
(`HardwareSitting.md`, archive row `diag3-returned-20260819.img`, and the
flight entry at `HardwareSitting.md:1915`).

**F-2.5.** Sitting 4 (2026-08-19, image `ED90B46A`) returned a `DIAG.TXT` of
4,577 bytes ending after `block`, and the flight entry's headline records the
first honest verdict from the medium itself, `bank=lost at=sink size=4577`
(`HardwareSitting.md`, archive row `diag4-returned-20260819.img`, and the
flight entry at `HardwareSitting.md:1795`).

**F-2.6.** Sitting 7 (2026-08-21, image `C5744A6D`) returned a `DIAG.TXT` of
4,994 bytes carrying stages 1 to 8 only, stopping one row short of the `pch`
row the flight existed to produce. The payload id was checked against the
flashed bytes rather than assumed, both reading `90466df54ddd274f`
(`HardwareSitting.md`, archive row `diag7-returned-20260821.img`, citing
L-SAMEVER).

**F-2.7.** Sitting 9 (2026-08-21, image `ECC60AF4`) returned 6,871 bytes with no
b3 row, and the ladder was pulled after 14 minutes. Sitting 10 (image
`C6B1CEAC`) returned 6,892 bytes and named the hang on the medium,
`stage=b3 step=reset`. Sitting 11 (image `2C7030D7`) returned 7,469 bytes, and
the medium stopped taking writes inside `e1000-init-after-reset` at the same
time as b3 completed a full exchange (`HardwareSitting.md`, archive rows
`diag9-`, `diag10-` and `diag11-returned-20260821.img`).

**F-2.8.** Sitting 12 (2026-08-24, image `8CDF3617`) returned 7,177 bytes, and
the medium stopped taking writes at b3's fourth step note, `reset-rst-write`.
The glass row confirmed the same ordinal independently as `bank-lost-note=4`,
therefore the medium died before `swflag` and before the `CTRL|SLU` write and
neither can be the cause of the wedge (`HardwareSitting.md`, archive row
`diag12-returned-20260824.img`).

**F-2.9.** Sitting 13 (2026-09-07, image `CB1AE335`) returned 5,149 bytes and the
medium died at `kbd`, stage 9, therefore eight stages produced no record. Stages
10 through 16 each returned success, therefore the ladder was told every write
landed
(`HardwareSitting.md:745-760`).

**F-2.10.** Sitting 14 (2026-09-07, image `AFC6AD65`) painted no `BANK LOST` at
any stage, therefore `dg-bank-step` returned success for all seventeen banks and
every size readback answered the new length. The medium kept the two FAT copies
at LBA 2054 and LBA 2158, a nine-cluster chain at clusters 1283 to 1291, and
nine data sectors from LBA 3570, and nothing else below the GPT: the root
directory came back byte-identical to the flashed image, `block`'s scratch
sector at LBA 30000 was untouched, and no later generation of the file existed
anywhere. The surviving 4,312 bytes are `dg-open`'s probe write from stages 1
to 6 (`HardwareSitting.md:708-742`).

**F-2.11.** The mechanism is one absent SCSI opcode. `GopUsbMsc.codex` issued six
opcodes and SYNCHRONIZE CACHE was not among the six, therefore the size readback
after each bank write was served out of the device-side write-back cache the
write had entered, and a stick that acknowledges into volatile cache and hands
the updated directory entry straight back passes the check
(`HardwareSitting.md:745-760`, registered as WORKS-62).

**F-2.12.** The readback itself was never the weak half: `diag-bank-write-text`
re-reads the size through `gfat-file-size`, which resolves through
`gfat-scan-root` to `disk-read-sector`, a real device read rather than a read of
our own memory (`HardwareSitting.md:745-760`).

**F-2.13.** WORKS-62's flush landed on main at CL 23069 on 2026-09-07 at 19:51,
described as "the MSC driver issues SYNCHRONIZE CACHE and both diag bank writers
call it between the write and its size readback", apps and diag only, no seed
(`p4 describe -s 23069`). Verified at head: the opcode table now holds seven
opcodes with `scsi-op-sync-cache : Integer = 53`
(`apps/works/GopUsbMsc.codex:34`), the command is `msc-sync-cache`
(`apps/works/GopUsbMsc.codex:513`), the port-level entry is `usb-sync-cache`
(`apps/works/GopUsbMsc.codex:744`), and both diag bank writers call
`disk-sync-cache` behind the cfg switch `diag-flush-wanted`
(`build/boot/diag/DiagStage.codex:196` and `:239`).

**F-2.14.** The flush has never run on the part. Sitting 15 flew on 2026-09-09,
stopped inside the ladder's first write to the part, and every metal question
aboard, the WORKS-62 flush among them, received the one answer "not reached"
(`TheLostParadise.md`, "The event under investigation").

**F-2.15.** The FIRST flight of 2026-09-07, image `AC7399ED` on the default cfg,
measured a second instrument gap, L-BANK. `asde` rides last as one row and banks
nothing until `asde` completes (`DiagAsde.codex:25`), and `asde` is the only
stage that can wedge the box, therefore `asde`'s reading is reachable only
through the thing the reading measures. That flight is the first boot on which
the bank was healthy at stage 16 and `asde` still wrote nothing, therefore the
gap became measured rather than argued (`HardwareSitting.md:844-936`; appendix
A, attempt 51, which carries no sitting number and is a different boot from
sitting 14, attempt 53, image `AFC6AD65`).

**F-2.16.** Three preserved returned sticks were destroyed on 2026-08-11 by the
`clean` phase of a gate run, because the fleet kept returned sticks in
`build-output/`, which every gate wipes and which is p4-ignored, therefore
nothing in the depot noticed the loss. The archive lives outside every
workspace, where no `clean` owns the archive and no `sync -f` reaches the
archive, verified by running a full gate over the archive after the first file
landed (`HardwareSitting.md`, "Why not `build-output/`").

**F-2.17.** Ten of the thirteen depot images carry a stub predating 2026-08-15,
and the three anybody still flies are the current three. `build/cdx-to-pe.ps1`
changed twice on the evening of 2026-08-15, at CL 15469 and CL 15503, and
15503's own description states the cost: "the layout of every path shifts,
INCLUDING -EntryStart ... the next A5 flight carries stub bytes that have not
flown" (`HardwareSitting.md:13-30`).

**F-2.18.** The rehearsal ruling of 2026-08-14 gained a runner on 2026-08-20 and
the runner is on by default: `build/flash-usb.ps1` refuses any image whose
SHA-256 is in no rehearsal record, with no switch required. The rule was opt-in
`-Rehearsed` until that day, and measured then, a flash of a 2026-08-14 stub
went through in silence. All four arms of the check were exercised
(`HardwareSitting.md:31-55`, citing L-BODY).

### 2.2 What the record means

**The record channel failed nine times and each failure looked like a new
defect.** Sittings 2, 3 and 4 stopped at one size, 4,577 bytes. Sitting 7
stopped one row short of the row the flight existed for. Sittings 9, 10, 11 and
12 stopped at four different points inside one stage. Sitting 13 stopped eight
stages early. Sitting 14 kept one write out of seventeen and dropped that
write's own directory entry. A reader looking at any one of the nine sees a
plausible local cause, and a reader looking at all nine sees one cause: the
guest asked a device to remember and never asked the device to commit.

**The bank's own verdict was honest before the bank's contents were.** Sitting 4
painted `bank=lost at=sink size=4577` from the medium itself, therefore the
ladder could say the bank had died at the same time as the ladder could not say
what the bank had lost. That is the correct order of instrument repair, and the
project reached the correct order after three flights rather than before any.

**The mechanism was in reach of a bed the entire time.** SYNCHRONIZE CACHE is a
mandatory opcode for a device that acknowledges into volatile cache. A bed that
models write-back caching would have failed the readback check with no flight
spent. The bed modelled a device that commits on acknowledgement, which is the
generous direction, and generosity does not look like a gap (L-ARENA). The
project spent eleven attempts measuring a property of real sticks that no bed
expressed.

**The flush is built, switchable, and unflown.** CL 23069 landed at 19:51 on
2026-09-07, one sitting before the last sitting. Sitting 15 stopped before the
ladder's first write to the part, therefore the repair for the failure that cost
eleven attempts has never once run on the hardware the repair was written for. The
metal question closed with the answer unmeasured.

**L-BANK is the same failure one level up, and sitting 14 is the only boot that
could have found the gap.** An instrument reachable only through the subsystem the
instrument measures cannot report on that subsystem's failure. `asde` had that
shape from the first `asde` flight, and the shape stayed invisible for as long
as the medium died upstream, because an upstream death explains an absent row
just as well. The gap became visible on the one flight where the medium was
healthy at stage 16, which is to say the project needed the WORKS-62 symptom to
recede before the project could see the defect underneath the symptom.

**The archive is the one part of the medium story that worked.** A ruling, a
command, a location outside every workspace, and a table row per stick produced
33 rows of evidence that survive today, and three of the rows are the only copy
of what a guest wrote on real hardware. The ruling arrived on 2026-08-11, after
three preserved sticks had already been destroyed by a gate's own `clean` phase.

### 2.3 Findings

**FESTER-F9.** The bank failed or stopped short on the ten flights named in
F-2.3 through F-2.10, and on attempt 51 of F-2.15, which is eleven attempts in
all (F-6.3), from one cause, an absent SYNCHRONIZE CACHE, and each failure
presented differently enough to read as a separate defect.
*Supported by:* F-2.3 through F-2.11.
*Falsified by:* a returned stick whose bank loss survives the flush. No such
stick exists, because the flush has never flown (F-2.14).

*(An earlier revision of FESTER-F9, landed at main 25230, said "nine consecutive
flights". The count and the word were both wrong: ten flights are named above,
and the ten are not consecutive, because the boots between them include flights
whose bank held. Appendix A's own count of rows lost to our own instrument
rather than to the board is eleven, over a wider class than the bank alone
(`03-sittings-ledger.md` 03.5). The finding is replaced rather than annotated.)*

**FESTER-F10.** Every bank write was acknowledged and every size readback
answered the new length, therefore the guest's own check could not observe the
loss, and the check was not naive: the readback is a real device read.
*Supported by:* F-2.9, F-2.10, F-2.12.
*Falsified by:* a readback served from guest memory. `gfat-file-size` resolves
to `disk-read-sector` (F-2.12).

**FESTER-F11.** Throughout the eleven attempts that lost or truncated the bank, no bed the project
owned expressed a device that acknowledges into volatile cache, therefore the
mechanism was outside every instrument the project could run without a human.
The bed gained the model on 2026-09-07, one day before the last sitting
(section 3, F-3.9).
*Supported by:* F-2.11, F-2.13, F-3.9.
*Falsified by:* a bed arm reproducing a lost bank against a write-back model
before 2026-09-07. The model landed at CL 23233 (F-3.9).

**FESTER-F12.** The repair for the nine lost banks landed one day before the last
sitting and has never run on the part.
*Supported by:* F-2.13, F-2.14.
*Falsified by:* a returned stick dumped after a flight carrying
`diag-flush-wanted`. Sitting 15 returned before the first write to the part
(F-2.14).

**FESTER-F13.** An instrument reachable only through the subsystem the instrument
measures cannot report that subsystem's failure, and the project could not see
that shape in `asde` for as long as a second defect upstream produced the same
absent row. The boot that made the shape visible is attempt 51 of 2026-09-07,
image `AC7399ED`, rather than sitting 14.
*Supported by:* F-2.15.
*Falsified by:* an `asde` row banked on any earlier flight. The record carries
none (F-2.15).

**FESTER-F14.** Returned sticks were kept in a directory that every gate wipes and
that the depot ignores, therefore three unique records of guest behaviour on real
hardware were destroyed by a routine action, and the ruling that fixed the
practice arrived after the loss.
*Supported by:* F-2.16, F-2.1.
*Falsified by:* a depot copy of any of the three. `build-output/` is p4-ignored
(F-2.16).

**FESTER-F15.** The stick-handling rules gained runners in the order of their
cost: rehearsal became a default refusal in `flash-usb.ps1` on 2026-08-20, six
days after the ruling, and the flasher had until that day silently written a
stub that had never flown.
*Supported by:* F-2.18, F-2.17.
*Falsified by:* a refusal in `flash-usb.ps1` before 2026-08-20. The switch was
opt-in `-Rehearsed` until that day (F-2.18).

---

## 3. The sink, the write ceiling, and the repair that nearly cost the record

The bank is what the ladder writes. The sink is what the ladder writes WITH: a
sustained write to the medium, the question WORKS-9 exists to answer. The sink
refused on the board and completed in the bed for the whole campaign, and the
repair for the bank's own defect came within one signature of destroying the
last sitting's record.

### 3.1 What the record says

**F-3.1.** WORKS-9 asks why a sustained write refuses on the ASUS. Three driver
defects behind the shot-2 timeout were fixed at main CL 14447, described as "USB
MSC: a timed-out transfer now recovers and retries. Three defects, all in code
that had never executed" (`p4 describe -s 14447`; `works-backlog.md`, row
WORKS-9).

**F-3.2.** Sitting 6 on 2026-08-20 returned the first number from the sink on
metal: `sink ladder-stop done=4 rung-sectors=16 rung-bytes=8192
payload-bytes=65536 note=1, wr=128 cc=256 lba=2169 rty=1 ph=2 after=0 chunk=16`.
Four sittings of one bit each preceded that reading, and every later reading is
a comparison against the reading (`works-backlog.md`, row WORKS-9).

**F-3.3.** The sitting-6 reading was glass only, because the bank dies at the
sink, therefore a photograph was the only other copy (`works-backlog.md`, row
WORKS-9, citing L-BANK).

**F-3.4.** The original WORKS-9 question is unanswered at the close of the
campaign: `xhci-fuel` is a spin count and nobody converted the count to a
duration on that box. No bed can convert the count, because codex-vm completes
every transfer before the guest spins once, therefore the fuel cell reads
exactly 1000000 in the bed (`works-backlog.md`, row WORKS-9).

**F-3.5.** The standing open question at the close is why the sink refuses on the
board at `rty=1` where the bed reaches `rty=2`, which is L-ARENA
(`docs/PM/CurrentPlan.md`, Track A, the WORKS-9 row).

**F-3.6.** An arm routed to WORKS-9 was wrong and is recorded as wrong in order
that nobody rebuilds the arm: the arm called `xhci-recover-endpoint`, which leads with Reset
Endpoint, defined only for a HALTED endpoint, and a timeout leaves the endpoint
RUNNING, therefore the device answers Context State Error and recovery refuses
(`works-backlog.md`, row WORKS-9).

**F-3.7.** The first version of the WORKS-62 flush broke every bank write.
SYNCHRONIZE CACHE is OPTIONAL in SCSI, a device without the opcode answers CHECK
CONDITION, and the first version refused the bank on that answer, therefore the
first version banked NOTHING. codex-vm's BOT model at that moment allowed
exactly the six opcodes predating the flush, therefore 0x35 was refused and 43
of 47 rehearsal arms went to `bank=none write refused` on one signature
(`works-backlog.md`, row WORKS-62, blu 2026-09-07, citing L-FALLBACK).

**F-3.8.** The rehearsal caught the regression before any flash, and on metal the
same regression would have cost the last sitting the entire record. The repair
makes the flush's outcome decide the MEANING of the size readback and never the
bank, and `disk-sync-cache` answers three states for a reader that wants to
know: 1 flushed, 0 no primitive, -1 refused (`works-backlog.md`, row WORKS-62;
`build/boot/diag/DiagStage.codex:157`).

**F-3.9.** The bed learned the missing model at main CL 23233 on 2026-09-07,
"codex-vm accepts SYNCHRONIZE CACHE and `-usb-writeback` models a write-back
cache" (`p4 describe -s 23233`). The model is off by default, therefore every existing
arm keeps the write-through target the arm was written against, and the model's
own comment in the source states the reason for existing: "THE DEFAULT TARGET
CANNOT EXPRESS A MISSING FLUSH ... a run with no SYNCHRONIZE CACHE and a run
with one are the same colour here, while sittings 13 and 14 lost the bank on the
board", citing L-FREEDOM (`tools/codex-vm.c:1449-1469`, with the commit path at
`:2488` and the dirty-span tracking at `:2499`).

**F-3.10.** A second candidate for sitting 13's gap was raised and priced rather
than adopted: `DiagMsc.codex` ran `__heap-save` and `__heap-advance dma-span`
with no `__heap-restore`, leaking 262,144 bytes permanently, at stage 10, which
is exactly where the banked data stopped. The arithmetic refused the candidate:
worst case the reads reach 162,815 bytes above the save point inside the 262,144
reserved, therefore no scribble past the reservation follows, and 256 KB against
`alloc-pages=32768` is a fraction of one percent, therefore starvation does not
follow either (`works-backlog.md`, row WORKS-62, "SECOND CANDIDATE").

**F-3.11.** That leak is closed at head. `DiagMsc.codex` now pairs, with
`__heap-save` at `:102`, `__heap-advance` at `:103` and `__heap-restore` at
`:111`, landed at main CL 22995 as "DiagMsc's paired heap restore"
(`p4 describe -s 22995`; verified at head by counting save, restore and advance
across all thirteen files in `build/boot/diag/`). The WORKS-62 row still states
"DiagMsc is save=1 restore=0 advance=1", which is stale, and the row is corrected
in the same changelist as section 3.

**F-3.12.** One file in `build/boot/diag/` is unpaired at head,
`MscAlignProbe.codex` with save=1, restore=0, advance=1, and the file is not a
defect: the chapter is a standalone probe whose `opening` ends in `shot-wait`
and never returns, therefore no caller inherits the advance
(`build/boot/diag/MscAlignProbe.codex:108-134`).

### 3.2 What the record means

**The sink is the one metal question that produced a number and still closed
unanswered.** Four sittings bought one bit each, sitting 6 bought a full line of
cells, three driver defects were found and fixed inside code that had never
executed, and the original question, how long the fuel budget actually is on
that box, needed one more flight and never received one. The gap between `rty=1`
on the board and `rty=2` in the bed is the whole of what remains, and the gap is
L-ARENA: a bed more generous than the target does not look like a gap.

**The sink and the bank shared a failure and hid each other.** The bank dies at
the sink, therefore the sink's own reading was glass only, therefore the sink's
evidence depended on a camera and a person. Two defects in one path, each
removing the instrument that would have measured the other, is the same shape as
L-BANK one level out.

**The rehearsal system paid for itself once, and once is enough to justify the
whole system.** The flush repaired the defect that had cost eleven attempts, and the
first version of the repair would have destroyed the record of the final flight
on any device lacking an optional opcode, which is most devices. No reading of
the code caught the regression. The bed rehearsal caught the regression, as 43
red arms out of 47 on one signature, and the bed caught the regression because
0x35, which is to say the bed's LIMITATION produced the finding. A more faithful
bed would have answered GOOD and hidden the regression until metal.

**L-FALLBACK is the rule the first flush broke, and the rule is expensive
exactly here.** A working path was disabled by the change introducing the
replacement. The corrected design keeps the record unconditional and demotes the
flush to deciding what the record MEANS, which is the right ordering for an
instrument whose whole purpose is bringing a record home.

**The bed reached the board's behaviour one day before the board became
unreachable.** CL 23233 landed on 2026-09-07 and the last sitting flew on
2026-09-09. The instrument that would have found the bank defect without
spending a human existed for two days at the end of a five-month campaign, and
the model is off by default, therefore no existing arm exercises the model
without being asked.

**A candidate was priced and refused rather than adopted by relief, and the
discipline held.** The heap leak was real, sat at the exact stage where the data
stopped, and would have been an attractive answer. The arithmetic refused the
answer, the leak was fixed on the leak's own merits at CL 22995, and the cache
reading stayed labelled as a mechanism that fits rather than a cause established
(L-MECHANISM).

### 3.3 Findings

**FESTER-F16.** WORKS-9 produced three real driver fixes in code that had never
executed and closed with the original question unmeasured, because the fuel cell
is a spin count that no bed can convert to a duration.
*Supported by:* F-3.1, F-3.2, F-3.4.
*Falsified by:* a duration for `xhci-fuel` measured on the ASUS. The record
carries a spin count alone (F-3.4).

**FESTER-F17.** The sink's readings were glass only for the whole campaign,
because the defect the sink measures kills the channel that would record the
sink.
*Supported by:* F-3.2, F-3.3.
*Falsified by:* a banked sink row on any returned stick. Sittings 2, 3 and 4 end
at the sink (F-2.3 through F-2.5).

**FESTER-F18.** The first version of the bank's repair would have destroyed the
final flight's record on any device lacking an optional opcode, and the bed's
own refusal of that opcode is what exposed the regression before a flash.
*Supported by:* F-3.7, F-3.8.
*Falsified by:* a rehearsal that passed the first flush version. 43 of 47 arms
failed on one signature (F-3.7).

**FESTER-F19.** The bed acquired the write-back model that would have found the
bank defect on 2026-09-07, two days before the campaign ended, and the
model is off by default.
*Supported by:* F-3.9, F-2.11.
*Falsified by:* a `-usb-writeback` arm in any gate or battery. The model's own
comment states the default is write-through (F-3.9).

**FESTER-F20.** A competing mechanism for the bank loss was priced against the
arithmetic and refused, and the underlying defect was fixed on the defect's own
merits, therefore the investigation did not adopt an attractive answer by
relief.
*Supported by:* F-3.10, F-3.11.
*Falsified by:* a bank claim resting on the heap leak. The row labels the leak
as not established (F-3.10).

---

## 4. The host rewrote the medium, and the medium refused to mount

Two separate failures put the stick out of reach on either side of the flight.
On the dev-box side the host rewrote a verified GPT between the flash and the
boot. On the board side the guest's own reader failed to mount a stick the
firmware had booted from. Each cost flights, each was diagnosed with a control,
and one of the two closed with the mechanism still a hypothesis.

### 4.1 What the record says

**F-4.1.** Measured 2026-07-29: a stick flashed and verified clean by the
flasher's own readback, then ejected and reinserted once, came back with LBA 1
rewritten. `PartitionEntryLBA` moved from 2 to 2047, which holds 512 bytes of
zeros; the header CRC was recomputed and the array CRC left stale; the good
array was orphaned at LBA 2; the backup was repointed from 60506078 to
60506110 the same way (`build/flash-usb.ps1:288-300`).

**F-4.2.** Both GPTs then fail validation, therefore firmware sees NO partitions,
therefore the failure "firmware never lists the stick" arrives silently after a
successful flash (`build/flash-usb.ps1:296-300`).

**F-4.3.** The claim that a conformant GPT means Windows finds nothing to fix is
recorded in the flasher as false, measured rather than argued
(`build/flash-usb.ps1:299-300`).

**F-4.4.** The disk-GUID theory carried in `OsHardwareRoadmap`'s Loop B as a
pending patch from 2026-07-10 onward is wrong, and a control killed the theory: the
stick was flashed with GUID `6222f486-5f70-4c24-9007-62b2537617f6`, a value
partmgr had provably never seen, one eject-and-reinsert produced byte for byte
the same rewrite, and the GUID on the medium was untouched afterwards, therefore
nothing cached by GUID can explain the rewrite
(`build/flash-usb.ps1:302-308`).

**F-4.5.** The surviving hypothesis is that Windows normalises the entry-array
POSITIONS to its own convention, primary array immediately below
`FirstUsableLBA` and backup immediately below the backup header, and the
hypothesis is recorded as untested (`build/flash-usb.ps1:309-312`).

**F-4.6.** The hazard is fixed at the cause rather than by instruction:
`build-img.ps1` writes a conforming table of 128 entries at the UEFI 16 KB
minimum with `FirstUsableLBA` 34, therefore Windows has nothing to normalise,
and the flasher takes the disk offline and locks every volume for the whole
write, therefore the eject that triggered the rewrite is not reachable. The
instruction telling the operator to eject is retired, and the retired text is
kept beside the fix in order that the pending patch is not invited back
(`build/flash-usb.ps1:273-286` and `:422-452`).

**F-4.7.** Reinsertion and eject are not hazards (Damian, 2026-08-18), and the
ruling was re-measured on 2026-08-20: disk 2 dumped before the fifth diag flight
came back byte-identical to `diag4-returned-20260819.img` across every insertion
between the two, which a live rewrite could not survive
(`build/flash-usb.ps1:280-286`).

**F-4.8.** On the board side, the flight of 2026-08-13 failed to mount at mount
stage 1, the read itself, and the diag cells name the failure exactly: `m` 3
(`med-kind-usb`, the USB medium was selected), `c` 4
(`trb-cc-usb-transaction-error`, a real completion event), `p` 1
(`msc-ph-cbw`, the failure is in the Command Block Wrapper phase), `f` 945044 of
`xhci-fuel` 1000000, `l` 1 (the GPT header sector), `r` 1
(`msc-retry-recover-failed`, reset recovery ran and failed)
(`HardwareSitting.md:3095-3106`).

**F-4.9.** That reading refutes the standing content hypothesis. A rewritten LBA
1 predicts a SUCCESSFUL read carrying wrong content, which lands on mount stage
2 or stage 4, and the observed failure is stage 1, a failed read. A content
theory cannot produce a failed read (`HardwareSitting.md:3106-3112`).

**F-4.10.** That reading is also not WORKS-9: WORKS-9 is a data-phase timeout with
no completion event, and the observed failure is a completion event arriving,
with an error, in the command phase, before any data moves. The fuel reading of
945044 of 1000000 clears fuel by WORKS-9's own stated criterion
(`HardwareSitting.md:3112-3117`).

**F-4.11.** What the reading does not settle is stated in the record: the ASUS
presents several USB interfaces behind a Unifying receiver, and nothing in the
reading proves the MSC driver bound the STICK rather than another bulk endpoint
(`HardwareSitting.md:3118-3120`).

**F-4.12.** Two flights were reduced to photographs by a mount failure at stage 2,
no `EFI PART` signature: `nicsitting.img` on 2026-08-14, whose entry reads
"Photograph only; the stick did not mount, so nothing banked. Every row below is
read off the glass", and `nicring.img` on 2026-08-16, whose entry reads "the
glass, which is the whole record because the bank did not mount"
(`HardwareSitting.md:2637-2640` and `:2338-2341`).

**F-4.13.** A flight was abandoned for want of a file bank, and the record judges
the abandonment unjustified, because a wedge leaves the glass readable, proven
on 2026-08-05 with rows painted, the machine stalled and the rows still there.
The repair makes the rows paint whether or not a volume mounts, and writes the
markers when a volume happens to be there (`HardwareSitting.md:3294-3299`).

**F-4.14.** The probe now prints WHICH of the seven mount stages failed, read off
diag cell 80, a value `GopFat16` had always recorded and nothing had ever
published (`HardwareSitting.md:3305-3308`).

### 4.2 What the record means

**The medium had two independent ways to be out of reach, and each mimicked a
board fault.** A GPT rewritten by the host after a verified flash presents as
"firmware never lists the stick", which is a board verdict. A stick the guest
cannot mount presents as a flight with no bank, which reads as a payload that
died early. Neither is the board, and both consumed flights before the
instruments could name the cause.

**The host-side rewrite is the clearest case in the whole record of a theory
surviving because nobody built the control.** The disk-GUID mechanism sat in the
roadmap from 2026-07-10 as a pending patch, was cited in
`TheStickDidNotBoot.md` on 2026-07-29 as the unaddressed mechanism, and was
false. One flash with a GUID partmgr had never seen killed the theory in a
single measurement. The patch would have been written, would have changed
nothing, and would have been believed, because the symptom recurs at a rate that
would have made any change look like an improvement.

**The repair that worked was at the cause rather than at the operator.** The
project stopped telling the operator not to eject and instead made the eject
unreachable and the table unremarkable to Windows. The retired instruction is
kept beside the fix for a stated reason: deleting the account invites the wrong
patch back. That is the correct treatment of a dead theory, and the same
treatment was not applied to R8 or to the ladder ordering, which stayed as prose.

**The guest-side mount failure was diagnosed by an instrument that already had
the answer and had never published the answer.** `GopFat16` had recorded the mount stage
in cell 80 all along. Two flights were reduced to photographs before anything
printed the cell. That is L-UNHEARD in the medium: a detector whose output goes
to a channel nobody reads is not a detector.

**The single most costly habit in the medium's record is treating an absent bank
as a reason to stop.** A wedge leaves the glass readable, proven on 2026-08-05,
and a flight was still abandoned for want of a file bank afterwards. The bank was
the newer channel and the newer channel displaced the older one in the
operator's procedure, rather than adding to the older one.

**One mechanism in section 4 is still a hypothesis at the close of the
campaign**, the entry-array normalisation of F-4.5, and the hypothesis is
labelled as untested in the source rather than carried as a conclusion.

### 4.3 Findings

**FESTER-F21.** A verified flash could be silently undone by one eject and
reinsert on the dev box, and the resulting failure presents as the board
refusing to list the stick.
*Supported by:* F-4.1, F-4.2.
*Falsified by:* a post-eject readback matching the flashed bytes. The measured
readback differs at LBA 1 and at the backup header (F-4.1).

**FESTER-F22.** The mechanism named in the roadmap for that rewrite was wrong for
nineteen days and was killed by one control, therefore the pending patch would
have shipped a change that fixed nothing.
*Supported by:* F-4.4, F-1.7.
*Falsified by:* a rewrite that does not reproduce under a fresh GUID. The
control reproduced the rewrite byte for byte (F-4.4).

**FESTER-F23.** The medium's host-side hazard was closed at the cause, by a
conforming table and a locked volume, rather than by an instruction to the
operator, and the closure is the only medium rule in the record that stopped
depending on a human remembering.
*Supported by:* F-4.6, F-4.7.
*Falsified by:* a recurrence of the rewrite after 2026-08-18. The 2026-08-20
re-measurement found the stick byte-identical across every insertion (F-4.7).

**FESTER-F24.** The guest could not mount a stick the firmware had booted, on at
least three flights, and the cell naming which mount stage failed existed in the
code before the first of the three and was published after the last.
*Supported by:* F-4.8, F-4.12, F-4.14.
*Falsified by:* a mount-stage reading in any flight entry before 2026-08-16. The
first published reading is the 2026-08-13 reconstruction (F-4.8).

**FESTER-F25.** The failed mount of 2026-08-13 was measured precisely enough to
refute two standing explanations, and precisely enough to expose a third
question nobody could answer: whether the driver had bound the stick at all.
*Supported by:* F-4.8, F-4.9, F-4.10, F-4.11.
*Falsified by:* evidence that the MSC driver bound the stick on that boot. The
record states the opposite is unproven (F-4.11).

---

## 5. The payload that wrote nothing, and the volume that was not ours

Between 2026-08-09 and 2026-08-13 the guest wrote nothing to the stick on four
flights. The four had three different causes and one shared property: the glass
could not tell a dead payload from a working one. The campaign closed green on
2026-08-14 with an artifact byte-identical to the host's, and the defect that
had held the campaign was a property of the board's firmware that no bed the
project owned could express.

### 5.1 What the record says

**F-5.1.** Attempt 20, 2026-08-09, `a5flight.img`: WROTE NOTHING, and the
returned volume differs from the master at LBA 0 and LBA 1 alone. The cause was
that the payload's block I/O was raw IDE port access against a USB stick,
therefore the payload could not have written (`A-sittings-table.md`, attempt 20).

**F-5.2.** Attempt 21, 2026-08-10, `a5flight2.img` with a `-Uefi` payload: WROTE
NOTHING, exactly two sectors differ, and the screen held the stub's dark green.
The arm's ONLY success signal was `DISK-OUT:` over ConOut, and `__uefi_print`
had never rendered a character on the board, therefore a dead payload and a
working payload were indistinguishable
(`A-sittings-table.md`, attempt 21).

**F-5.3.** Attempt 27, 2026-08-11, `sinkladder.img`: ORANGE, held. The returned
root directory holds `EFI`, `CODEX.CDX`, `CMUNSS.TTF` and no `BIG.CDX`,
therefore no directory entry was ever created and the fault is at or before the
first allocation rather than in the chain walk. The glass could not separate
slow from dead, and the heartbeat bar was built for that reason
(`A-sittings-table.md`, attempt 27).

**F-5.4.** Attempt 28, 2026-08-11, `a5bigflight.img`: WROTE NOTHING, the ladder
did not fire, and the stub's dark green held for twenty minutes. The payload
dies between the stub's jump and the first line of `opening`, upstream of the
first rung, because `-EntryStart` enters `__start`, which takes the hardware
over with boot services still live, and `emit-wait-for-tick` never returns on
the ASUS (`A-sittings-table.md`, attempt 28).

**F-5.5.** The MAGENTA stalls were the WRONG VOLUME. The `-Uefi` block helpers
bound their device with `LocateProtocol`, which returns the FIRST Block I/O
handle in the firmware's handle database, and the UEFI specification leaves that
order unspecified (`HardwareSitting.md:4569-4573`).

**F-5.6.** No bed could present a wrong first handle, because every bed has
exactly one disk, and a real machine presents raw disks AND per-partition
handles for everything attached in whatever order the firmware built them
(`HardwareSitting.md:4571-4575`, citing L-GAP).

**F-5.7.** A foreign FAT volume mounts cleanly, because `bps == 512` is the only
property the volume rung checks, and the foreign volume holds no `SOURCE.SRC`,
which is exactly the metal picture: volume rung green, literal path
unresolvable, scope clean (`HardwareSitting.md:4575-4578`).

**F-5.8.** The repair is three layers, each with a fallback to the old path: the
stub stashes the firmware's ImageHandle at cell 30712 beside the SystemTable;
both UEFI block helpers bind `ImageHandle -> LoadedImage -> DeviceHandle ->
Block I/O`, the device the image BOOTED from, using mandatory protocols only,
and fall back to `LocateProtocol` when the cell is zero or any link refuses; and
`fat16-boot-volume` probes LBA 0 as a BPB before the 2048 fallback, because a
DeviceHandle is normally a PARTITION handle through which LBA 0 is the volume's
own boot sector and the GPT read fails (`HardwareSitting.md:4580-4590`).

**F-5.9.** The probe reads bytes 11 and 13 raw before letting `fat16-init` near
the sector, because `fat16-parse-bpb` divides by both and a protective MBR holds
zeroes there, and the `disk-arm` nosource arm hung on exactly that divide until
the probe went in (`HardwareSitting.md:4586-4590`).

**F-5.10.** The bed evidence for the repair includes one arm the record itself
labels vacuous: OVMF with the OLD binding plus a decoy passed, because OVMF
happened to order the stick's raw disk first, therefore NO BED REPRODUCES THE
ASUS'S ORDERING and the metal evidence stands alone. The record adds that the
fix does not depend on ordering at all (`HardwareSitting.md:4592-4600`).

**F-5.11.** The campaign closed green on 2026-08-14: the returned stick carries
`OUT.CDX` of 2,790,018 bytes, digest `AB3A207EFB9279A6`, byte-identical to the
host control, plus `OUT.TXT` reading "OK OUT.CDX 2790018"
(`HardwareSitting.md:2274-2283`).

**F-5.12.** The defect that had held the campaign for days is a firmware freedom.
The ASUS's AMI firmware satisfies `AllocateAnyPages` from the TOP of RAM,
therefore the payload's heap lands ABOVE 4 GB. Several compiler types declared
heap pointers as `Integer between 0 and 4294967295`, and bounded signatures are
enforced with UD2 traps, therefore the first `pitch` at compile start trapped,
the firmware's own exception handler wedged with nothing on the glass, and the
screen froze on whatever was last painted, which is the seven-hour ORANGE of the
`a5heap` flight (`HardwareSitting.md:2285-2296`).

**F-5.13.** OVMF and codex-vm allocate BELOW 4 GB at every tested RAM size,
therefore no stock bed could express the condition. The condition was reproduced
by patching the stub to `AllocateAddress` at a fixed 5 GB base under an 8 GB
OVMF bed, giving `#UD` at `pitch+0x46`, and the fix was verified the same way
(`HardwareSitting.md:2296-2300`).

**F-5.14.** The returned stick of the `a5heap` flight holds `SOURCE.SRC` and no
`OUT.CDX` and no `OUT.TXT`, therefore the guest died before writing a byte,
which is what the UD2-at-pitch diagnosis predicted
(`HardwareSitting.md`, archive row `a5heap-returned-20260814.img`).

**F-5.15.** One citation carried by both the run sheet and appendix A does not
resolve: the wrong-volume repair is cited as "The fix (CL 14694)"
(`HardwareSitting.md:4580`) and as "reek, CL 14694" (`A-sittings-table.md`,
attempt 33), and `p4 describe -s 14694` answers "no such changelist" at head,
with main's changelists either side dated 2026-08-11 rather than 2026-08-13.

### 5.2 What the record means

**Four flights returned the same observation and the observation had three
causes.** A payload writing nothing looks identical whether the block I/O talks
to the wrong bus, the success channel has never rendered a character, the
payload dies before its first line, or the volume mounted is somebody else's.
The project spent one flight per cause, and each cause was found by reading
rather than by flying, after the flight that raised the question.

**The wrong volume is the campaign's purest instance of a bed that cannot
express a defect.** A bed with one disk cannot present a wrong first handle, and
the specification's unspecified ordering is precisely a freedom the board
exercises and the bed does not. The record goes further than most entries here
and labels its own confirming arm vacuous: the old binding PASSED under OVMF
with a decoy, because OVMF happened to order the disks the convenient way. An
arm that passes for a reason unrelated to the fix is worse than no arm, and
naming the arm vacuous is the discipline the report keeps finding in the ladder's own
documents and rarely elsewhere.

**The A5 defect is the sharpest single fact in part 05.** A firmware that
allocates from the top of RAM, a bounded type on a heap pointer, and a trap
handler that wedges, together produce seven hours of an unchanging orange
screen. Nothing in the chain is a mistake in isolation: the allocation is
specification-legal, the bounded type is the safety property the project sells,
and the UD2 is the enforcement working. The failure is the composition, and no
bed the project owned allocated high enough to compose the three.

**The repair for the wrong volume is the right shape, and part 05 records the
fact.** Three layers, each falling back to the old path, mandatory protocols only,
and a probe that reads two bytes raw because the parser divides by both. That is
a fix written by somebody who expected the next surprise rather than the last
one, and the `disk-arm` nosource arm's hang is the evidence that the expectation
was earned.

**A citation in two documents points at a changelist that does not exist.** The
wrong-volume repair is real, landed and evidenced by the bed table beside the entry,
therefore the defect is in the pointer rather than in the work. A reader
checking the repair against `p4 describe -s 14694` is told the changelist does
not exist and cannot tell that from a repair that never landed, which is L-ROWROT
kind 3 in the two documents an accident report rests on.

### 5.3 Findings

**FESTER-F26.** Four consecutive A5-era flights returned "the guest wrote
nothing", from three different causes, and no channel aboard any of the four
could separate a dead payload from a working one.
*Supported by:* F-5.1, F-5.2, F-5.3, F-5.4.
*Falsified by:* a success signal on any of the four that rendered. The only
signal was `DISK-OUT:` over a ConOut that had never rendered a character on the
board (F-5.2).

**FESTER-F27.** The wrong-volume defect was unreachable by every bed the project
owned, because a bed with one disk cannot express an unspecified handle
ordering, and the confirming bed arm passed for a reason unrelated to the fix.
*Supported by:* F-5.5, F-5.6, F-5.10.
*Falsified by:* a bed presenting more than one Block I/O handle in an order the
firmware chose. The record states no bed reproduces the ordering (F-5.10).

**FESTER-F28.** The defect that cost the A5 campaign its longest delay is a
composition of three individually correct behaviours, and the composition
required an allocation above 4 GB that no stock bed performs.
*Supported by:* F-5.12, F-5.13, F-5.14.
*Falsified by:* a stock bed allocating above 4 GB at any tested size. The
condition needed a patched stub and a forced address (F-5.13).

**FESTER-F29.** The A5 campaign ended with the compiler compiling itself on the
board and writing back an artifact byte-identical to the host's, therefore the
medium's write path was proven end to end on metal on 2026-08-14.
*Supported by:* F-5.11.
*Falsified by:* a difference between `OUT.CDX` and the host control. The record
states byte-identical and names the digest (F-5.11).

**FESTER-F30.** The wrong-volume repair is cited in the run sheet and in appendix
A by a changelist number that does not exist, therefore a reader auditing the
repair is told nothing distinguishable from a repair that never landed.
*Supported by:* F-5.15.
*Falsified by:* a changelist 14694 in any stream. `p4 describe -s 14694` answers
"no such changelist" (F-5.15).

---

## 6. What the medium cost, and when the network channel arrived

The charter asks part 05 for two numbers: what the medium cost in flights, and
why, in the charter's words, "the record channel came so late". Both are answerable from the record, and
the first is answerable only by enumeration, because a text census of the record
under-counts the very events the census exists to find.

### 6.1 What the record says

**F-6.1.** The archive holds 36 image rows: 28 returned sticks and 15 pre-flash
dumps, counted at head, and 3 of the 36 are marked the only copy of what a guest
wrote on real hardware (`HardwareSitting.md`, the QUICKREF archive table,
counted 2026-09-09).

**F-6.2.** Appendix A holds 54 attempt rows for the documented era
(`A-sittings-table.md`, counted at head 2026-09-09; part 03 section 03.5 states
52 boots, of which 17 answered every question aboard, 26 answered part and 9
answered nothing, with two rows that are not boots).

**F-6.3.** **The medium lost, truncated or refused part or all of the reading on
17 of the 54 attempts**, each established earlier in part 05 with its own
citation, and enumerated here by attempt number:

| class | attempts | count |
|---|---|---|
| the guest wrote nothing | 20, 21, 27, 28 | 4 |
| the volume did not mount, and the flight became a photograph | 34, 37 | 2 |
| the bank was lost or stopped short | 39, 40, 41, 45, 47, 48, 49, 50, 51, 52, 53 | 11 |

**F-6.4.** The sitting-to-attempt mapping the enumeration rests on, read from
appendix A at head: sitting 7 is attempt 45, sitting 8 is 46, sitting 9 is 47,
sitting 10 is 48, sitting 11 is 49, sitting 12 is 50, sitting 13 is 52, sitting
14 is 53 and sitting 15 is 54. Attempt 51 carries no sitting number. The second,
third and fourth GROUPED DIAG sittings of 2026-08-19 are attempts 39, 40 and 41
(`A-sittings-table.md`).

**F-6.5.** A text census of appendix A for the medium's own vocabulary returns 9
of those 17 rows. The census searched `WROTE NOTHING`, `did not mount`, `no
bank`, `bank=lost`, `bank-lost`, `DIAG.TXT ends`, `Photograph only`, `banked
nothing` and `no directory entry`, and missed rows whose text spells the same
event another way, including the three 2026-08-19 rows that write the file name
inside backticks (measured 2026-09-09; L-CENSUS).

**F-6.6.** The bank became the ladder's record on 2026-08-18, the date of the
grouping ruling and of the ladder's first build script
(`p4 describe -s 16822`, 2026-08-18 05:29; `HardwareSitting.md:56-70`).

**F-6.7.** A TCP conversation between the board and the dev box completed on
2026-08-21: attempt 49, sitting 11, "At 17:01:11 the dev box logged `CONNECTION
28 from 192.168.6.200:49157`, 13 bytes echoed and closed clean"
(`A-sittings-table.md`, attempt 49).

**F-6.8.** The network record channel landed at main CL 23532 on 2026-09-08 at
03:54, described as "diag ladder NETWORK RECORD CHANNEL (opens before the first
bank write, ships every bank line live), plus lease (NIC-6) and rtcw (WORKS-24)
stages. Rehearsed 50 arms" (`p4 describe -s 23532`).

**F-6.9.** Eighteen days separate the capability being proven on the board from
the channel existing, and the bank was lost or stopped short on four further
flights inside those eighteen days: attempts 50, 51, 52 and 53
(F-6.3, F-6.7, F-6.8).

**F-6.10.** The channel took one flight, attempt 54, and never printed a line
(section 06, G-4.7).

### 6.2 What the record means

**The medium cost seventeen of fifty-four attempts, and the number is a floor
rather than a total.** Every one of the seventeen is a flight where a human sat
at a board and the reading came home damaged or absent for a reason on our side
of the boundary. The number counts what part 05 established individually; a
flight whose medium behaved and whose reading was thin for another reason is not
in the seventeen, and neither is any row the enumeration has not yet reached.

**The census under-counted by nearly half, and the under-count is the useful
part.** Nine rows answered a search built from the medium's own vocabulary, and
seventeen rows describe the event. A reader who trusted the census would
conclude the medium cost nine flights, would be wrong by eight, and would have
no signal that the number was low. The record spells one event several ways
because the record was written flight by flight by different lanes, which is
correct for a log and fatal for a census. Any count over the record is an
enumeration or an under-count.

**The channel was buildable eighteen days before the channel was built.** The
network channel needs one capability the board does not always give: a TCP
conversation with a peer. The board gave one on 2026-08-21 and the record logged
the bytes. The channel landed on 2026-09-08. In between, four more flights lost
part or all of their record to the medium, which is exactly the loss the channel
removes.

**The reason for the eighteen days is legible in the record and is not
inattention.** Through that window the fleet was answering the question the
channel would have made cheap, by repairing the medium instead: the flush was
found and landed (2026-09-07), the heap pairing was fixed, the CRLF guard went
in, and each repair was a correct response to the symptom in front of the fleet. The
channel is the fix that makes the medium's reliability irrelevant to the record,
and the fix that removes a class always competes against the fix that removes
the instance, with the instance winning because the instance is in front of you.
That is the shape of the whole medium story: eleven attempts of repairing the bank,
one flight of not needing the bank.

**The archive is the counter-example and cost almost nothing.** Thirty-six rows,
three of them the only surviving record of what a guest wrote on real hardware,
produced by one ruling and one command. The archive was cheap, was ordered once,
and is the only part of the medium story that never failed.

### 6.3 Findings

**FESTER-F31.** The medium lost, truncated or refused part or all of the reading
on 17 of the 54 documented attempts, and the seventeen are enumerable by attempt
number.
*Supported by:* F-6.3, F-6.4.
*Falsified by:* an attempt in the seventeen whose reading the medium did not
damage. Each of the seventeen is cited individually in sections 2, 4 and 5.

**FESTER-F32.** A text census over the record's own vocabulary under-counts the
medium's cost by eight of seventeen, therefore any count over the record must
be an enumeration.
*Supported by:* F-6.5, F-6.3.
*Falsified by:* a search string set returning all seventeen. The nine-row result
used the medium's own vocabulary (F-6.5).

**FESTER-F33.** The capability the network channel needed was proven on the board
on 2026-08-21 and the channel landed on 2026-09-08, and four flights lost part
or all of their record to the medium inside the interval.
*Supported by:* F-6.7, F-6.8, F-6.9.
*Falsified by:* a blocker recorded between the two dates. The record carries the
medium repairs of that window rather than a blocker (section 6.2).

**FESTER-F34.** Through the same interval the fleet repaired the medium four
times and each repair was a correct answer to the symptom in front of the fleet,
therefore the delay is a competition between removing an instance and removing a
class rather than an oversight.
*Supported by:* F-2.13, F-3.11, F-6.9, G-1.11.
*Falsified by:* a proposal for the channel earlier than 2026-09-07 that was
declined. The record carries none.

**FESTER-F35.** The archive is the one medium instrument that never failed, cost
one ruling and one command, and holds three records that exist nowhere else.
*Supported by:* F-6.1, F-2.1, F-2.16.
*Falsified by:* an archive row lost or unreadable. All 36 are present at head
(F-6.1).

---

## Coverage

### Files read in full for section 1

| path | size | revision read |
|---|---|---|
| `docs/PM/Active/Stories/TheStickDidNotBoot.md` | 35,896 | workspace at CL 25192 |
| `docs/PM/Active/Stories/TheSecondStick.md` | 28,491 | workspace at CL 25192 |
| `docs/PM/Active/Stories/TheImageThatWasTwoDaysOld.md` | 51,118 | workspace at CL 25192 |
| `docs/PM/Active/Stories/TheLostParadise.md` | 13,216 | `//Codex/root` at CL 25185 |

### Files read for section 2

| path | extent read | revision read |
|---|---|---|
| `docs/Hardware/HardwareSitting.md` | lines 1-151 (QUICKREF, the rulings, the 33-row archive table), 708-760 (sittings 14 and 13), 914-936 (the L-BANK gap), plus the heading index of all 6,904 lines | workspace at CL 25192 |
| `apps/works/GopUsbMsc.codex` | lines 20-40 and the `sync` occurrences at 353, 504-517, 744-748 of 30,893 bytes | workspace at CL 25192 |
| `build/boot/diag/DiagStage.codex` | the `sync-cache` occurrences at 157, 196, 239 | workspace at CL 25192 |

### Files read for section 3

| path | extent read | revision read |
|---|---|---|
| `apps/works/works-backlog.md` | rows WORKS-62 and WORKS-9 in full, and the row index for WORKS-8, 16, 24 | workspace at CL 25229 |
| `tools/codex-vm.c` | lines 1444-1472 (the write-back model and its reason), the `0x35` occurrences at 2469, 2481-2499 | workspace at CL 25229 |
| `build/boot/diag/DiagMsc.codex` | the three heap occurrences at 102, 103, 111 | workspace at CL 25229 |
| `build/boot/diag/MscAlignProbe.codex` | lines 108-134, the whole probe body | workspace at CL 25229 |
| `docs/PM/CurrentPlan.md` | Track A, the WORKS-9 row | workspace at CL 25229 |

### Files read for section 4

| path | extent read | revision read |
|---|---|---|
| `build/flash-usb.ps1` | lines 190-212 and 259-312 (the reinsertion account and the disk-GUID control), and the retired-instruction block at 422-452 by line index | workspace at CL 25240 |
| `docs/Hardware/HardwareSitting.md` | lines 2330-2345, 2630-2650, 3095-3120, 3294-3315 | workspace at CL 25240 |

### Files read for section 5

| path | extent read | revision read |
|---|---|---|
| `docs/PM/Active/Stories/TheLostParadise/A-sittings-table.md` | the header and rows 1-6, 20, 21, 27, 28, 33, 34, 49, 50, 51, 52, 53 of 29,895 bytes | workspace at CL 25296 |
| `docs/Hardware/HardwareSitting.md` | lines 2273-2300 (the green A5 flight and the heap defect), 4566-4600 (the wrong-volume diagnosis and its bed table) | workspace at CL 25296 |
| `docs/PM/Active/Stories/TheLostParadise/01-the-accident.md` | 18,667 bytes, in full | workspace at CL 25296 |
| `docs/PM/Active/Stories/TheLostParadise/03-sittings-ledger.md` | 14,676 bytes, in full | workspace at CL 25296 |
| `docs/PM/Active/Stories/TheLostParadise/07-the-beds.md` | REEK-F1 to REEK-F4 at lines 359-410 of 28,183 bytes | workspace at CL 25296 |
| `docs/PM/Active/Stories/TheLostParadise.md` | the Terms section at head | workspace at CL 25296 |

### Files read for section 6

| path | extent read | revision read |
|---|---|---|
| `docs/PM/Active/Stories/TheLostParadise/A-sittings-table.md` | every attempt row's number, date and sitting label; rows 20, 21, 27, 28, 33, 34, 49, 50, 51, 52, 53 in full | workspace at CL 25302 |
| `docs/Hardware/HardwareSitting.md` | the QUICKREF archive table counted row by row | workspace at CL 25302 |

### Extractions run for section 6

```powershell
p4 describe -s 16822    # the ladder's first build script, 2026-08-18 05:29
p4 describe -s 23532    # the network record channel, 2026-09-08 03:54
# the archive count, by row shape rather than by hand
(Get-Content docs/Hardware/HardwareSitting.md | Where-Object { $_ -match '^\| `[a-z0-9\-]+.*\.img`' }).Count
# the census of F-6.5, which under-counts and is reported as under-counting
```

### Extractions run for section 5

```powershell
p4 describe -s 14694    # answers "no such changelist"; see F-5.15
p4 changes -m 3 //Codex/main/...@14690,14700
```

**READ A ROW OF `A-sittings-table.md` WHOLE.** Splitting a row on the pipe
character shifts every column of any row whose cell text contains a pipe, and row
50's question cell contains `` `CTRL|SLU` ``. Part 06's coverage note records the
two wrong counts that defect produced.

### Extractions run for section 4

```powershell
Select-String -Path build/flash-usb.ps1 -Pattern 'PartitionEntryLBA|2047|eject|reinsert'
Select-String -Path docs/Hardware/HardwareSitting.md -Pattern 'did not mount|no medium|medium=none'
```

### Extractions run for section 3

```powershell
p4 describe -s 14447    # USB MSC: a timed-out transfer recovers and retries
p4 describe -s 22995    # DiagMsc's paired heap restore
p4 describe -s 23233    # codex-vm accepts SYNCHRONIZE CACHE and -usb-writeback
p4 filelog -m 6  //Codex/main/build/boot/diag/DiagMsc.codex
p4 filelog -m 30 //Codex/main/tools/codex-vm.c
# the pairing census over every ladder chapter, counting the three intrinsics
Get-ChildItem build/boot/diag -Filter *.codex | ForEach-Object { ... }
```

### Extractions run for section 2

```powershell
p4 describe -s 23069                                     # WORKS-62, the MSC flush
Select-String -Path apps/works/GopUsbMsc.codex -Pattern '^\s*scsi-op-\S+\s*:\s*Integer'
Get-ChildItem apps,codex,build -Recurse -Include *.codex |
    Select-String -Pattern 'usb-sync-cache|msc-sync-cache'
Get-ChildItem build/boot -Recurse -Include *.codex | Select-String -Pattern 'sync-cache'
```

### Extractions run for section 1

```powershell
p4 describe -s 11000    # seed/Codex.img rebuilt, 16 MB, two partitions
p4 describe -s 11833    # OOM handler COM1 + cli
p4 describe -s 11926    # UEFI firmware-call ABI fix (RBX save) + ExitUefi wiring
p4 describe -s 12033    # flash-usb: verify the SpecFit tail blobs unbuffered
p4 describe -s 12035    # copy-up of 12033
p4 describe -s 12045    # copy-up of TheStickDidNotBoot.md
p4 describe -s 12083    # merge down carrying the stub, the sheet and the diag README
```

### What section 1 did not read, and why

The charter names `codex/os/kernel/Xhci*.codex` and `codex/os/kernel/Block*.codex`
in part 05's corpus. Neither exists at CL 25192: a tree search over `codex/`
returns no `Xhci*.codex` at any path, and the block layer is
`codex/os/kernel/DiskFacts.codex` (26,288 bytes) and
`codex/os/kernel/DriveManager.codex` (19,470 bytes). The FAT implementation is
`codex/foreword/core/Fat16.codex` (107,336 bytes) and
`codex/foreword/core/Fat32.codex` (41,017 bytes) rather than a `Fat*.codex`
under `codex/os/kernel/`, which holds `FatReader.codex` (7,992 bytes) alone. The
corpus row is corrected here rather than in the charter, and the named
substitutes are read for section 2.

`docs/Hardware/HardwareSitting.md` (437,270 bytes) is read section by section
for sections 2 onward rather than in one pass, and each section names the lines
read.
