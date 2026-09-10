# Part 07. The beds as instruments

*Evidence chapter of The Lost Paradise, owned by reek. Opened 2026-09-09.
The subject is every bed the project used to answer a hardware question:
`tools/codex-vm.c`, OVMF, QEMU and Renode. The question the chapter must
answer is narrow and hard: at each point where a bed returned green and the
ASUS returned red, what freedom did the bed decline to model, could a model
have been built before the flight, and what would the model have cost against
the flights the model would have saved.*

## Scope and method

The chapter carries evidence first and interpretation second, under the
charter's rule 2. Findings are numbered `REEK-F1` upward, one sentence each,
each with the evidence supporting the finding and the evidence falsifying the
finding.

The chapter names reek's own failures by changelist, under the charter's rule
3. reek owns `tools/codex-vm.c` for the fleet, owns the A5 campaign of
2026-08-14, and owns the two bed levers added on 2026-09-07, therefore a
large fraction of what follows is an accounting of reek's own instruments.

Every count below carries the date of measurement, under L-COUNT. Every
extraction command appears in the coverage section at the end of the chapter,
under the charter's rule 5.

One deliberate exception to the charter's rule 6 runs through the chapter.
The three CPL axioms bind every sentence reek writes. A sentence QUOTED from
a changelist description, from a document or from the UEFI specification is
reproduced verbatim, banned words included, because altering a quotation to
suit a style rule falsifies the evidence the charter's rule 1 requires. Every
such word below sits inside quotation marks.

## What the record says

### 7.1 The four beds, and what each one is

The project used four beds, and the four are not four of a kind. Naming the
four apart matters, because a claim of the form "the bed was green" carries a
different weight for each one.

**First, `tools/codex-vm.exe`, the fleet's own hypervisor.** The program is a
single C file, `tools/codex-vm.c`, at revision 240 as of changelist 23601
(2026-09-08), built by `tools/build-vm.ps1`. The program runs a guest under
the Windows Hypervisor Platform and presents device models written by the
fleet. Ownership matters to the investigation: every device the guest sees
inside codex-vm exists because a lane wrote the model, therefore every
freedom the silicon holds and the model does not is a freedom the fleet chose
by omission rather than inherited.

**Second, OVMF, the UEFI firmware image the fleet did not write.** OVMF is
the firmware under which a UEFI payload boots in the bed, and OVMF is the one
bed component whose behaviour the fleet cannot edit. The A5 campaign's three
killing defects were three points where OVMF makes one legal choice and the
ASUS's AMI firmware makes the opposite legal choice
(`docs/PM/Active/Stories/TheBedThatAlwaysSaidYes.md#1`, sections 1 through 3).

**Third, QEMU, used for the cross targets rather than for the ASUS.** QEMU
answers questions about RISC-V and AArch64 kernels, and answers no question
about the ASUS's parts.

**Fourth, Renode, used for the board chapters and the cross battery.** Renode
answers questions about the nine HAL boards.

Of the four, only the first is under fleet control, and only the first models
the ASUS's parts. Therefore every finding below about a modelled freedom is a
finding about `tools/codex-vm.c`, and every finding about an unmodellable
freedom is a finding about OVMF against AMI.

### 7.2 codex-vm as a growing machine: the 240 changelists

`tools/codex-vm.c` carries 240 submitted changelists, from changelist 1154 on
2026-05-07 to changelist 23601 on 2026-09-08. Extracted 2026-09-09 with
`p4 changes -l //Codex/main/tools/codex-vm.c`, then bucketed by keyword over
each description.

| subject | changelists | first | last |
|---|---|---|---|
| IDE, ATA and disk | 85 | 1154 (2026-05-07) | 22810 (2026-09-07) |
| SMP and APIC | 53 | 2053 (2026-05-23) | 23283 (2026-09-07) |
| xHCI and USB | 51 | 2053 (2026-05-23) | 23233 (2026-09-07) |
| I219 and e1000e | 38 | 1696 (2026-05-18) | 22810 (2026-09-07) |
| GOP and video | 34 | 1696 (2026-05-18) | 22810 (2026-09-07) |
| UEFI, OVMF and boot | 25 | 1154 (2026-05-07) | 18011 (2026-08-20) |
| PCI, PCH and chipset | 17 | 2053 (2026-05-23) | 19212 (2026-08-24) |
| HPET, timer and RTC | 14 | 2053 (2026-05-23) | 18799 (2026-08-21) |
| NE2000 | 9 | 1696 (2026-05-18) | 16941 (2026-08-18) |

A changelist touching two subjects counts under each subject, therefore the
column sums past 240 and is a measure of attention rather than a partition.

Two readings of the table bear on the accident, and both are stated here as
observations rather than as conclusions.

First, the storage path received the most attention of any subject, 85
changelists, and the storage path is where sitting 15 stopped. Attention
measured in changelists is therefore not a predictor of a part answering on
metal.

Second, the NIC models received 38 changelists and arrived on 2026-05-18,
which is 72 days before the first recorded flight entry of 2026-07-29
(`docs/Hardware/HardwareSitting.md#91`, rung 1). The NIC model existed long
before the NIC questions flew.

### 7.3 What the device models can refuse, and what the models decline to model

The Device Emulation Catalogue states each model's fidelity beside the model's
faults, and the catalogue's second rule is the one that matters to the
investigation: an entry must give the model at least one way to refuse, as a
flag, and an entry without a refusal does not belong in the catalogue
(`docs/Designs/Active/Tools/DeviceEmulationCatalog.md#27`, "The two rules that
make a row worth having", and "Adding an entry" step 3).

The catalogue's own "NOT modelled" column is the honest inventory of the
freedoms the bed declines. Quoted from revision 27, measured 2026-07-29 with
later rows dated where each row moved:

- **Intel I219 NIC, `8086:15B8`, absent unless `-i219` selects the model.**
  Modelled: PHY page 770 register 17, the `Giga_K1_disable` bit 13 and the
  `K1 enable` bit 14, cited to I219 revision 2.02 section 9.5.5.2, plus a MAC
  making no progress with K1 enabled at 1 Gbps, plus the MDIO and NVM
  semaphore at `EXTCNF_CTRL`. NOT modelled: ULP, SMBus, LANPHYPC, the LCD
  reload after a PHY reset, and LTR. The catalogue's own words about the gap
  are "Four of the eight rows, and silence here is not agreement".
- **Intel e1000e NIC, `8086:100E`.** NOT modelled: interrupts, because the
  driver polls; multi-descriptor frames; checksum offload; statistics; and a
  failed reset leaving the part otherwise disabled the way wedged silicon
  leaves a part.
- **NEC xHCI USB 3.x, `1033:0194`.** Modelled: mass storage, a HID keyboard,
  a UVC camera, hubs, and a variable root port count.
- **Bochs VGA and the GOP framebuffer, `1234:1111`.** Display only, in RAM,
  with no MMIO trap. NOT modelled: the host-side triangle rasteriser and the
  post passes, each addressing rows by visible width and refusing a padded
  stride outright.
- **IDE disk, HPET, IOAPIC, LAPIC, PS/2, CMOS RTC, PC speaker and the UEFI
  firmware.** Fidelity varies, and the catalogue records "no" in the refusal
  column for every one of the eight.

The last row is the largest single gap in the inventory. Eight device models,
including the IDE disk and the UEFI firmware, carry no way to refuse at all,
therefore no arm built on any of the eight can make the hostile choice a real
part makes. The IDE disk sits on the storage path, and sitting 15 stopped
inside the first write to the part.

One further constraint applies to every model and is not a per-device gap.
Every BAR in the catalogue sits inside 3 to 4 GB, because the runtime page
tables map 0 to 3 GB as RAM, one directory for 3 to 4 GB as devices, and
nothing above 4 GB. The catalogue states the consequence plainly: the
emulated case cannot exercise a driver's handling of a badly placed BAR,
because firmware on a real box picks the address, and OVMF was measured
putting a NIC BAR at `0x81060000`, below the window entirely
(`DeviceEmulationCatalog.md#27`, "Every BAR above is inside 3 to 4 GB").

### 7.4 The A5 campaign: three legal choices, three flights

The A5 campaign of 2026-08-14 is the clearest recorded case of a bed green
and a board red, and reek owns the campaign and the account
(`docs/PM/Active/Stories/TheBedThatAlwaysSaidYes.md#1`). The campaign's goal
was to boot the compiler from a USB stick on bare UEFI firmware, compile the
compiler's own source off the stick, write `OUT.CDX` back, and stop.

The campaign shipped on 2026-08-14 with the board's `OUT.CDX` byte-identical
to the host control. The cost before the green flight, in Damian's own count
recorded in the story: about twenty misfire flashes across four days, about 3
million tokens spent by one lane and another half million by the relief lane,
and a human back paying for every sitting.

Two of the early killers were ordinary mistakes with ordinary morals, and the
story records both: first, `-EntryStart` spins forever under live firmware,
and three dead images carried the flag; then, a payload compiled without
`-Uefi` receives the bare-metal input and output helpers and mounts zeroes.

The three defects consuming the rest of the campaign were one shape, and the
shape is the finding. In each of the three, the UEFI specification leaves a
choice open, the bed's firmware makes the friendly choice, and the ASUS's AMI
firmware makes the hostile choice.

1. **Handle order.** The block helpers bound a device with `LocateProtocol`,
   returning the FIRST Block I/O handle in the firmware's database, with the
   order unspecified by the specification. Every bed presents one disk,
   therefore the first handle in the bed is always the stick. The board
   presents raw disks and per-partition handles in firmware order, and a
   foreign volume mounted clean and held no `SOURCE.SRC`.
2. **Allocation placement, low.** The stub asked `AllocateMaxAddress` under a
   3 GB ceiling and never validated the returned base. Bed firmware granted
   the request. On the board the in and out cell came back holding the seed
   value, therefore the heap base read as `0xC0000000`, which is the board's
   framebuffer. Records read back as `0x00FF00FF00FF00FF`, two magenta
   pixels, and every repaint bulldozed the heap.
3. **Allocation placement, high.** The repair for defect 2 switched to
   `AllocateAnyPages`. OVMF satisfies the request below 4 GB at every RAM size
   the fleet could configure. AMI satisfies the request from the top of a
   32 GB board. Several compiler types declare heap positions as
   `Integer between 0 and 4294967295`, bounded signatures are enforced with a
   UD2 trap, therefore the first `pitch` of the compile trapped into the
   firmware's invisible exception handler. The result was the seven-hour
   ORANGE: halted rather than slow, with the last painted screen still up.

The story states the accounting the investigation needs, and states the
accounting against reek's own campaign. Two of the three, once identified,
took MINUTES to express in the bed. First, `ImageHandle=2` at the bed's PE
entry inside codex-vm, because real firmware never passes 0 and a 0 silently
kept the bed on the fallback path. Then, the stub built with
`cdx-to-pe.ps1 -HeapAt 0x140000000` under `test-ovmf.ps1 -MemMB 8192`, which
forces the heap to 5 GB by `AllocateAddress`: the arm reproduced the board's
seven-hour hang as a `#UD at pitch+0x46` on the first run, and verified the
repair the same afternoon, with no sitting spent.

The third defect carries the caution keeping the lesson honest. The
handle-order repair received a decoy-disk arm, `test-ovmf.ps1 -Decoy`, and
the run sheet's own entry calls the arm's green VACUOUS: OVMF happened to
order the stick's raw disk first, therefore the wrong-disk topology never
reached the repair, and the binding repair is proven by metal alone as of 2026-09-09.

### 7.5 The freedoms, enumerated in advance

The A5 story states that the freedoms mattering to the campaign were
enumerable before the flights, and lists the four: handle enumeration order;
allocation placement; whether an allocation request is granted at all; and
what uninitialised memory holds. The story names the fourth with a measured
detail: cell 36320, the guard-page base, was written only by `emit-start`,
which a UEFI tenant never runs, and QEMU zeroes RAM where boards do not.

Each of the four is a line in the UEFI specification carrying the words "no
ordering is guaranteed" or "the firmware may".

The finding available from the enumeration is not that the freedoms were
unknown. The finding is that the freedoms were knowable by reading the
specification the payload leans on, and that the reading happened after three
flights rather than before the first flight.

### 7.6 Recorded cases of a bed green and the board red

Four cases are on the record with the divergence named. The A5 campaign's
three defects of section 7.4 are the first three, and the fourth through the
sixth follow.

**Fourth, sitting 7 on 2026-08-21: the sink kills the bank on the board and
leaves the bank alive in the bed.** The flight lost the nine I219 readings the
flight existed to obtain. `DIAG.TXT` held stages 1 through 8 and stopped, and
`pch` is stage 10. The composing lane recorded the cause as a composition
error rather than a board defect, and recorded the bed's part in the error in
plain words: "the rehearsal could not contradict me, because in the bed the
sink does not kill the bank (root measured that on 2026-08-20: all stages
reach `DIAG.TXT` in the bed, board-only, L-ARENA)"
(`docs/Hardware/HardwareSitting.md#91:1503-1549`; the composition is
changelist 18394, red, 2026-08-20, whose own description reads "34 arms
green, both beds").

The case is the purest instance of the chapter's subject. A rehearsal of 34
arms passed green in both beds, the flight was composed against a belief the
rehearsal could not contradict, and the board answered by taking the bank at
stage 9.

**Fifth, the medium-death candidate of 2026-08-21: the bed dies at one stage
and the board died at another.** reek built `-usb-bot-die-on-nic`, a bed for
the candidate that the I219 bring-up kills the USB mass storage device, and
recorded the divergence inside the changelist that shipped the arm:
"Measured: the first bring-up observable is RCTL.EN from nicinit, so the bed
dies at=nicinit while sitting 11 died at b3 rings-link; the arm records that
gap" (changelist 19018, reek, 2026-08-21). The arm reproduces the CLASS of
failure and does not reproduce the STAGE, and the changelist says as much
rather than claiming a match.

**Sixth, sitting 15 on 2026-09-09, the last flight: 50 of 50 arms green in
both beds, and the board stopped inside the first write to the part.** The
image `diag-sitting15.img`, SHA-256 `47F29D50`, was rehearsed as the exact
bytes, 50 of 50 arms, in both beds, recorded in `build/boot/diag.rehearsed`
at 2026-09-08T13:45:47Z, and signed off by root on 2026-09-08 at 06:55
(`HardwareSitting.md#91:937-966`). The board painted rows 1 through 7 and
ended on `nicsit`'s poll line. The record channel never printed, the peer log
holds zero connections, the bank never opened, and every question aboard
received the one answer "not reached"
(`HardwareSitting.md#91:151-184`).

The one measurement the last flight returned that a bed can be compared
against is the poll line: the board reported
`poll 1000000 empty=33152us tick100k=3315us hpet-hz=23999999` against the
bed's `13034us` and `1303us`. The board's empty poll is 2.5 times the bed's,
and the board's HPET runs at 24 MHz.

### 7.7 The bed's own defects, and every one below is reek's

reek authored 57 of the 240 changelists on `tools/codex-vm.c`, measured
2026-09-09 by filtering the ledger extraction on the client name. Five of the
57 record a defect in the bed itself, and each of the five is stated here
with the changelist, under the charter's rule 3.

**Changelist 14197, 2026-08-08: the bed triple-faulted a guest on addresses
the bed's own allocator returned.** The changelist's own words: "The emulator
advertised RAM above 4 GB via GetMemoryMap and allocated top-down into it,
but mapped only 4 GB, so it triple-faulted the guest on addresses its own
allocator returned." The repair maps one page directory per gigabyte. The
defect is the bed being WRONG rather than the bed being nice, and the defect
sat in the bed until a stub deliberately allocating high was written as a
positive control.

**Changelist 14398, 2026-08-09: an emulator gap hid a real block-path
defect.** The changelist's own summary names the gap as one of the three
things landing together: "Systab cell off the PML4, block write helper, and
the emulator gap that hid it."

**Changelist 14452, 2026-08-09: a whole class of USB failure was unreachable
on any bed.** The changelist landed three driver defects around timed-out
mass storage transfers, and landed `-usb-bot-drop` and BOT Mass Storage Reset
in codex-vm, "without which none of that class was reachable on any bed".
The bed's inability to express the class is the finding, and the driver
defects had been present with no arm able to reach the driver defects.

**Changelist 18324, 2026-08-20: the bed advertised a PCI identity the bed
does not implement.** The NIC model answered `8086:100E` where the model's
behaviour matched a different part. The catalogue records the ruling arising
from the defect: a bed must not advertise an id the bed does not implement,
and the catalogue records why the defect survived, which is that nothing in
the tree observes the id
(`DeviceEmulationCatalog.md#27`, "A bed must not advertise an id it does not
implement").

**Changelist 20108, 2026-08-27: the bed accepted an unrecognised argument and
dropped the argument in silence.** The repair makes codex-vm refuse the first
unrecognised argument. The first run of the repair found `-serial stdio` and
`-timeout` in `test-exception-handler`, neither ever parsed, therefore two
flags had been believed effective and had done nothing. The lesson is
L-ACCEPTED, and the emulator half of the lesson gained a runner on that day
rather than earlier.

### 7.8 What a bed could have modelled, and when

The A5 story answers the question for the campaign reek owns, and the answer
is short. Two of the three killing defects took MINUTES to express in the bed
once the freedom was named: first, `ImageHandle=2` at the bed's PE entry,
because real firmware never passes 0 and a 0 kept the bed silently on the
fallback path; then, the stub built with `cdx-to-pe.ps1 -HeapAt 0x140000000`
under `test-ovmf.ps1 -MemMB 8192`, forcing the heap to 5 GB, which reproduced
the board's seven-hour hang as a `#UD at pitch+0x46` on the first run and
verified the repair the same afternoon with no sitting spent
(`TheBedThatAlwaysSaidYes.md#1`).

The third defect answers the question in the other direction and is the
harder half of the chapter. The handle-order repair received a decoy-disk
arm, `test-ovmf.ps1 -Decoy`, and the arm's green is recorded as VACUOUS,
because OVMF happened to order the stick's raw disk first, therefore the
wrong-disk topology never reached the repair. Building the arm was not
enough. The arm had to be caught making the hostile choice at least once, and
the arm never was.

The cost comparison the charter asks for, for the A5 campaign alone, is
therefore:

| item | measured cost |
|---|---|
| flights before the green flight | about twenty misfire flashes across four days |
| tokens | about 3 million by one lane, about half a million by the relief lane |
| human cost | one back, per sitting, across four days |
| bed arms that would have caught two of the three | minutes each, once the freedom was named |
| bed arm for the third | built, green, and vacuous |

## What the record means

### REEK-F1

**Eight of the bed's device models carry no way to refuse, and the storage
path where the last flight stopped is among the eight.**

Supporting evidence: `DeviceEmulationCatalog.md#27`'s catalogue table records
"no" in the refusal column for the row holding IDE disk, HPET, IOAPIC, LAPIC,
PS/2, CMOS RTC, PC speaker and the UEFI firmware; the catalogue's own rule 3
under "Adding an entry" requires at least one refusal per entry; sitting 15
stopped inside `usb-attach`, the ESP select and cfg read, or
`net-driver-bring-up` (`HardwareSitting.md#91:170-176`).

Falsifying evidence: a refusal flag on any of the eight, present in
`tools/codex-vm.c` at revision 240 and absent from the catalogue row, falsifies
the finding for that device. The finding rests on the catalogue rather than on
a reading of all 240 revisions of the C file, and the coverage section names
the gap.

### REEK-F2

**The freedoms that killed the A5 campaign were enumerable from the UEFI
specification before the first flight, and the enumeration happened after the
third flight.**

Supporting evidence: `TheBedThatAlwaysSaidYes.md#1` lists the four freedoms
and states that each one is a line in the specification carrying the words
"no ordering is guaranteed" or "the firmware may"; the same story records the
three defects as consecutive, each found by a flight.

Falsifying evidence: a document dated before 2026-08-10 enumerating handle
order, allocation placement, allocation refusal and uninitialised memory as
risks to the A5 payload falsifies the finding.

### REEK-F3

**A bed arm expressing a hostile choice is not evidence until the arm has
been observed making the hostile choice, and the fleet has exactly one
recorded instance of building the arm and never catching the arm say no.**

Supporting evidence: the decoy-disk arm's green is called VACUOUS in the run
sheet's own entry, and the handle-order repair is proven by metal alone as of
2026-09-09 (`TheBedThatAlwaysSaidYes.md#1`).

Falsifying evidence: a run of `test-ovmf.ps1 -Decoy` in which the wrong-disk
topology is shown to reach the binding code falsifies the finding.

### REEK-F4

**A full rehearsal in both beds does not predict a metal stop, and the last
flight is the proof: 50 of 50 arms green as the exact flight bytes, and the
board stopped inside the first write to the part.**

Supporting evidence: `HardwareSitting.md#91:937-966` for the rehearsal record
and the sign-off; `HardwareSitting.md#91:161-184` for the result.

Falsifying evidence: a bed arm that reproduces the sitting 15 stop, built
after the flight from the flight's own reading, falsifies the finding by
showing the stop was expressible before the flight.
### 7.9 The bed and the board began on the same day, for different reasons

Routed by root from red's part 02, landing 5 (main 25254), and verified here
at the pointer on 2026-09-09 rather than carried on the relay.

**Changelist 1104, 2026-05-07 at 07:36:30**, is the ASUS's first entry in the
depot. The description reads: "fix: PE stub stack alignment (sub rsp 64->40) +
ImageBase 0x10000000->0 for real hardware UEFI boot. Asus firmware rejects
high ImageBase with dummy .reloc; SSE movaps faults on misaligned stack."

**Changelist 1154, the same day at 17:37:49**, ten hours later, adds
`tools/codex-vm.c` and `tools/build-vm.ps1`. The description reads: "feat:
codex-vm -- WHP-based VM host, replaces QEMU for dev. Serial TCP + IDE +
multiboot loader."

Three facts follow from the pair, and each one is a reading of the two
descriptions rather than an inference about intent.

First, the board's first recorded behaviour is a REFUSAL by the ASUS's
firmware, and the refusal is recorded before the fleet's own bed exists.

Second, the bed's stated purpose on the day of the bed's creation is to
replace QEMU for development. The purpose named is development speed. The
purpose named is not modelling the ASUS.

Third, the bed as created on 2026-05-07 carried a serial line over TCP, an
IDE disk and a multiboot loader. The bed carried no UEFI firmware. UEFI
emulation reached codex-vm on 2026-05-18 in changelist 1696, 11 days after
the board's first refusal, measured 2026-09-09 by filtering the ledger
extraction for the words UEFI and OVMF, which 22 of the 240 changelists
carry.

The first recorded flight entry in the run sheet is 2026-07-29, rung 1
(`docs/Hardware/HardwareSitting.md#91`), which is 83 days after the board's
first refusal.

What the pair does NOT establish is whether the UEFI emulation of 2026-05-18
could have expressed changelist 1104's defect, which is a firmware refusing a
PE image by ImageBase. Answering that question requires reading
`tools/codex-vm.c` at revision 1696 and later revisions, and the coverage
section records the reading as not yet done.

### REEK-F5

**The bed was created as a development convenience on the same day the ASUS
first refused a payload, and the bed's stated purpose never changed to
modelling the board.**

Supporting evidence: changelist 1104 (2026-05-07 07:36:30) records the ASUS
refusing a high ImageBase; changelist 1154 (2026-05-07 17:37:49) creates
codex-vm with the stated purpose "replaces QEMU for dev" and the stated
device list "Serial TCP + IDE + multiboot loader"; UEFI emulation arrives 11
days later in changelist 1696; the Device Emulation Catalogue, opened
2026-07-29, is the first document stating the order of operations "emulate
first, hardware-sit second" (`DeviceEmulationCatalog.md#27`, "Why this
exists"), 83 days after changelist 1104.

Falsifying evidence: a changelist or document dated between 2026-05-07 and
2026-07-29 stating that codex-vm's purpose includes modelling the ASUS's
parts falsifies the finding.

### REEK-F6

**The rule that would have prevented the accident's shape was written 83 days
after the board's first refusal, and the rule was written as a catalogue
convention rather than as a gate.**

Supporting evidence: the Device Emulation Catalogue was opened 2026-07-29 on
Damian's direction, and carries his words "why don't we make it more official
than a scratchpad"; the catalogue states "The order of operations for every
driver is emulate first, hardware-sit second" and states the reason, that the
hardware sitting is the scarcest device on the bus under L-HUMAN and must not
be spent discovering what a model would have caught
(`DeviceEmulationCatalog.md#27`, "Why this exists"); the catalogue's
enforcement is a convention for adding an entry rather than a runner, and
L-FREEDOM's runner column in `LESSONS.md` reads "none".

Falsifying evidence: a build gate refusing a flight image whose driver has no
catalogue entry, present in the tree at any date, falsifies the finding.
### 7.10 The full inventory of recorded cases, with the freedom named

Ten cases are on the record where a bed returned green, or returned nothing,
and the board returned red. Each row names the freedom the bed declined to
model and cites the line of the run sheet carrying the reading. Extracted
2026-09-09 by searching `docs/Hardware/HardwareSitting.md#91` for every
statement of a bed against the board.

| # | case | the freedom the bed did not model | citation |
|---|---|---|---|
| 1 | A5 handle order | `LocateProtocol` returns the first Block I/O handle and the specification fixes no order; every bed presents one disk | `TheBedThatAlwaysSaidYes.md#1` |
| 2 | A5 allocation, low | whether firmware grants an allocation at all, and what an ungranted cell holds | `TheBedThatAlwaysSaidYes.md#1` |
| 3 | A5 allocation, high | where firmware satisfies `AllocateAnyPages` on a 32 GB board | `HardwareSitting.md#91:2286-2296` |
| 4 | sitting 7, the sink and the bank | a medium death outlasting a stage boundary | `HardwareSitting.md#91:1523-1525` |
| 5 | the nicring reading | the model was present and WRONG rather than absent | `HardwareSitting.md#91:1429-1436` |
| 6 | PHY page 770 register 17 | firmware state left in the part before the driver runs | `HardwareSitting.md#91:1474-1478` |
| 7 | the refused write | a medium refusing a write at all | `HardwareSitting.md#91:2126-2140` |
| 8 | poll and transfer timing | the board's rate against the model's rate | `HardwareSitting.md#91:2680` and `:161-184` |
| 9 | `RDH` movement | whether the receive head is the driver's to write | `HardwareSitting.md#91:2512` |
| 10 | the keyboard topology | four keyboard-shaped interfaces behind one receiver, which is L-OPTIONAL: the bed is MORE capable than the target rather than less faithful | `HardwareSitting.md#91:3212` |

Six of the ten carry a detail worth stating in full, because the detail
decides whether more modelling would have helped.

**Case 3, the seven-hour ORANGE.** The run sheet states the limit of the
stock beds plainly: "OVMF and codex-vm allocate BELOW 4 GB at every tested
RAM size, so no stock bed could express the condition." The condition was
then reproduced by patching the stub to `AllocateAddress` at a fixed 5 GB.
The freedom was expressible on demand and was not expressed until after the
flight.

**Case 5, the nicring reading, and the case is the sharpest of the ten.**
The run sheet's own heading reads "THE BED CANNOT EXPRESS THE NICRING
READING, and the reason is a model defect, not a gap". codex-vm models the
K1 gigabit stall at `tools/codex-vm.c:4584` and makes `e1000_deliver_rx`
return before the ring is touched at `:4642`. GPRC increments only inside
the delivery loop, therefore the model cannot produce the board's pair of
readings. A model present and wrong is worse than a model absent, because a
present model invites the arm that cannot fail.

**Case 6, the near-miss.** The bed powers PHY page 770 register 17 up at
`0x4000`. The board reads `0xd104`, five bits apart, and the five bits are
firmware state present in no model the fleet holds. The run sheet's
conclusion is explicit: `e1000-k1-configure` read-modify-writes, therefore
the bits survive, and a constant write would have cleared the bits on every
boot, and "no bed arm could ever have caught it (L-FREEDOM)". The case is
the one kind of divergence more modelling does not close.

**Case 7, the refused write.** The bed recovers one drop every time,
therefore no bed had ever produced a refused write, and `-usb-bot-drops K`
was built for that reason. The lever then reproduced the refusal exactly as
metal showed the refusal, `wr=0 cc=256 lba=3574 rty=2 ph=1`, and the bank
rewrite after the stage landed anyway, with `DIAG.TXT` whole at 60 rows. The
run sheet draws the honest conclusion: the bed's wedge is transient and the
ASUS's was not.

**Case 8, timing, measured twice, 26 days apart, with the same ratio.** On
2026-08-14 the calibration read 32,606 microseconds on metal against 13,034
in the bed, "a factor of 2.50". On 2026-09-09, the last flight, `nicsit`
read `empty=33152us` against the bed's `13034us`, which is the same ratio
again. The bed is consistently 2.5 times faster than the board on the same
poll.

**Case 10, and the reason no bed settles the keyboard.** QEMU presents one
clean keyboard. The ASUS presents four keyboard-shaped interfaces behind a
Unifying receiver, which is the topology producing the completion-steal
defect of Update 38. The run sheet ends the passage with the sentence the
chapter exists to explain: "The bed runs the whole path green."

Two further statements of the same kind sit outside the ten, because each
one is a bed limit rather than a recorded board red.

First, `nat_arp_reply` at `tools/codex-vm.c#34:7855` answers EVERY ARP request,
whatever the target, and always with the same `nat_gw_mac`, copying the
requested target address back into the reply, therefore the answer looks
specific.
Booted with a peer existing nowhere, the ARP was answered and the stage
reached `refused` at the handshake rather than `no-arp`. The bed therefore
cannot separate the two routing choices NIC-6 exists to separate.

Then, cell 85 carries the smallest fuel any completed transfer left behind,
and under codex-vm the cell reads exactly 1,000,000, because "this bed
completes every transfer before the guest spins once, so no bed has ever put
a single spin of pressure on that constant".

### 7.11 The cost of modelling against the flights the modelling saves

The charter asks for the cost of building a model against the cost of the
flights the model would have saved. The record answers the question for four
cases and declines to answer for the rest, and the decline is itself the
finding.

| case | cost of the model, measured | what a flight cost |
|---|---|---|
| 3, allocation high | minutes, once the freedom was named: `cdx-to-pe.ps1 -HeapAt 0x140000000` under `test-ovmf.ps1 -MemMB 8192`, reproducing on the first run | one seven-hour ORANGE sitting |
| 1, handle order | an arm was built, `test-ovmf.ps1 -Decoy`, and the arm's green is recorded VACUOUS | one sitting, and the repair is proven by metal alone |
| 7, refused write | `-usb-bot-drops K` plus the index picking the state (300 recovers, 500 refuses, 1000 writes and fails the readback) | the lever arrived after the flights that needed the lever |
| 5, nicring | the model existed already and was wrong; the cost is a repair rather than a build | the reading the flight bought cannot be reproduced |

For cases 6, 8, 9 and 10 the record states that no bed answers the question,
therefore no modelling cost can be traded against a flight. Case 6 is
firmware state, case 8 is silicon rate, case 9 is a register's writability on
the real part, and case 10 is the ASUS's own USB topology.

The arithmetic available from the four rows is one-sided and worth stating
plainly. Every case where the record measures the cost of the model measures
the cost in MINUTES. Every case where the record measures the cost of the
flight measures the cost in SITTINGS, and a sitting costs a human back, a
flash, and, by Damian's count for the A5 campaign alone, about twenty
misfire flashes across four days and about 3.5 million tokens across two
lanes.

### REEK-F7

**The bed's divergences from the board fall into four kinds, and only one of
the four is closed by building more model.**

The four kinds, each with a case from the inventory above: a model ABSENT
(case 3, allocation above 4 GB, expressible on demand once named); a model
PRESENT AND WRONG (case 5, the nicring reading, where GPRC increments inside
a loop the stall skips); a model GENEROUS (cases 1, 7 and the ARP and fuel
statements, where the bed answers where a board refuses); and firmware or
silicon state UNMODELLABLE without the part (cases 6, 8, 9 and 10).

Supporting evidence: the ten rows of section 7.10 with their citations.

Falsifying evidence: a recorded case of a bed and board divergence fitting
none of the four kinds falsifies the finding.

### REEK-F8

**Every measured divergence runs in the same direction: the bed is faster,
more complete, more recoverable and more orderly than the board, therefore
the bed's silence reads as agreement rather than as absence.**

Supporting evidence: the bed is 2.5 times faster on the same poll, measured
2026-08-14 and again 2026-09-09; the bed completes every transfer before the
guest spins once, therefore cell 85 reads exactly 1,000,000; the bed recovers
one drop every time, therefore no bed had ever produced a refused write; the
bed answers every ARP request whatever the target; the bed presents one clean
keyboard against the board's four keyboard-shaped interfaces; every bed
presents one disk, therefore the first handle is always the stick. The
A5 story states the shape in one sentence: the bed "was NICER than the
board, at every point where the spec allowed it to be, and its silence at
those points read as agreement".

Falsifying evidence: a recorded case where the bed is HARSHER than the board,
refusing what the board grants, falsifies the finding.

### REEK-F9

**Where the record measures both sides, the model costs minutes and the
flight costs a sitting, and the fleet paid the sitting first in every
recorded case.**

Supporting evidence: the four rows of section 7.11; the A5 story's account of
two arms taking MINUTES once the freedom was named, each built after the
flight the arm would have saved.

Falsifying evidence: a bed arm built BEFORE the flight of the defect the arm
expresses, recorded anywhere in the run sheet, falsifies the finding.
### 7.12 The bed's own design document named two of the killing freedoms 83 days early

`docs/Designs/Done/Hardware/CODEX-VM-UEFI-BIOS.md` was created on 2026-05-23,
carries the status line "Active -- analysis complete, implementation not
started", and is the bed's own plan for simulating the ASUS's firmware. The
document is 12,300 bytes and is read in full for the chapter.

The document's stated motivation is the chapter's subject in one sentence:
"Building these into codex-vm means we can develop and test the UEFI app
without flashing USB sticks or juggling QEMU windows."

**The document names the freedom that later cost the seven-hour ORANGE.**
Under "Key differences from OVMF" the document lists, on 2026-05-23: "AMI's
DXE allocator prefers high-to-low allocation". The A5 campaign's third
defect, flown on 2026-08-14, is the ASUS's AMI firmware satisfying
`AllocateAnyPages` from the top of a 32 GB board. The two are the same fact,
83 days apart.

The document also states what a model of the fact was for. Phase 5 is a
realistic memory map, and the priority list gives Phase 5 the second place
with the reason attached: "realistic memory map catches allocation bugs
before they hit real hardware". Phase 5's estimated size is about 50 lines of
C, inside a total estimate of about 470 lines for all six phases.

**The document names the second freedom too, and names the exact value.**
Phase 4 reads: "Install Loaded Image on the image handle (passed as RCX to
the UEFI entry point -- currently 0, should be a real handle)". The A5 story
records `ImageHandle=2` at the bed's PE entry as one of the two arms that
took minutes once the freedom was named, "because real firmware never passes
0 and 0 silently kept the bed on the fallback path".

The bed's own revisions date the gap exactly. Measured 2026-09-09 by
bisecting `p4 print` over the 240 revisions for the assignment
`uefi_vals[0].Reg64 = 2`:

| revision | changelist | date | the ImageHandle the bed passes |
|---|---|---|---|
| 51 | 4670 | 2026-06-17 | `uefi_vals[0].Reg64 = 0;  /* ImageHandle */` |
| 135 | 11508 | 2026-07-28 | `uefi_vals[0].Reg64 = 0;  /* ImageHandle */` |
| 197 | 15041 | 2026-08-14 | `uefi_vals[0].Reg64 = 2;` with the pseudo-handle comment |

Changelist 15041 is the A5 completion copy-up, submitted 2026-08-14 at
10:24:25, whose own description begins "A5 SHIPPED". The correction the
design document asked for on 2026-05-23 reached the bed on the day the
campaign ended, after about twenty misfire flashes.

**One correction to a reading made and discarded during the chapter's work, recorded
because the trap is general.** `p4 annotate -c` first dated the working
`LocateProtocol` line to changelist 11508. Changelist 11508 is blu's em-dash
removal sweep of 2026-07-28, which rewrote the COMMENT on that line and
nothing else. Annotation reports the changelist that last touched a line,
therefore annotation over a tree with sweeps dates the sweep. The true dates
were then taken by bisecting `p4 print` over the revisions, which is what the
table above and the paragraph below use.

**A third statement in the document points the wrong way, and the wrong way
is the direction that cost the flights.** Under "USB Boot Enumeration" the
document states: "On the ASUS TUF, the USB boot disk's Block I/O handle is
typically the first handle enumerated (it's the boot device). This is why
`LocateProtocol` for Block I/O works on real hardware but fails on QEMU/OVMF
(where the first Block I/O is the NVRAM flash)." A5 measured the reverse.
Every bed presents one disk, therefore the first handle in the bed is always
the stick, and the board presented raw disks and per-partition handles in
firmware order, mounting a foreign volume holding no `SOURCE.SRC`
(`TheBedThatAlwaysSaidYes.md#1`). The document's belief was that hardware is
the safe case and the bed is the risky one.

Phases 1 and 2 of the plan, the handle infrastructure and Block I/O, were
implemented on the day the document was written: the GUID-dispatching
`LocateProtocol` and the `BLK_READBLOCKS` trap first appear at revision 17,
changelist 2053, 2026-05-23, measured by the same bisection. Phase 5, the
realistic memory map, is the phase the document tied to catching allocation
bugs before hardware, and the A5 campaign's third defect is an allocation bug
that reached hardware.

### 7.13 A third of the bed was written after the flying began

Every one of the 240 revisions of `tools/codex-vm.c` was measured for size on
2026-09-09 with `p4 sizes -a`, and each revision was joined to the changelist
date from the ledger extraction.

| month | revisions | size at the month's last revision |
|---|---|---|
| 2026-05 | 39 | 228,777 bytes (revision 39, changelist 2885) |
| 2026-06 | 30 | 303,096 bytes (revision 69, changelist 6420) |
| 2026-07 | 77 | 616,158 bytes (revision 146, changelist 12519) |
| 2026-08 | 86 | 839,357 bytes (revision 232, changelist 20487) |
| 2026-09 | 8 | 860,698 bytes (revision 240, changelist 23601) |

The first recorded flight entry in the run sheet is 2026-07-29. The last
revision before that date is revision 135, changelist 11508, at 568,890
bytes. The head is 860,698 bytes.

**291,808 bytes of the bed, which is 33.9 percent of the bed at head,
were written on or after the day of the first recorded flight, across 105 of
the 240 revisions.**

### REEK-F10

**A third of the bed was written after the flying began, therefore the bed
was substantially a product of the flights rather than a preparation for
them.**

Supporting evidence: the size measurement of all 240 revisions in section
7.13; the first flight entry of 2026-07-29 in
`docs/Hardware/HardwareSitting.md#91`; the Device Emulation Catalogue's own
order of operations, "emulate first, hardware-sit second", written 2026-07-29
(`DeviceEmulationCatalog.md#27`).

Falsifying evidence: a demonstration that the bytes added after 2026-07-29
serve subjects unrelated to the flights, for example the cross targets or the
SMP work, falsifies the finding as stated. The SMP bucket carries 53
changelists and is a genuine candidate, and the chapter has not yet split the
291,808 bytes by subject.

### REEK-F11

**The bed's own design document named two of the three freedoms that killed
the A5 campaign, 83 days before the campaign, and priced a model of the first
at about 50 lines of C.**

Supporting evidence: `CODEX-VM-UEFI-BIOS.md`, created 2026-05-23, states
"AMI's DXE allocator prefers high-to-low allocation" and gives Phase 5 the
reason "realistic memory map catches allocation bugs before they hit real
hardware" at an estimated 50 lines; the same document's Phase 4 states the
image handle is "currently 0, should be a real handle"; the bed passed 0
until revision 197, changelist 15041, 2026-08-14, measured by bisection.

Falsifying evidence: a revision of `tools/codex-vm.c` before 2026-08-14
carrying a nonzero ImageHandle, or a memory map honouring AMI's high-to-low
preference, falsifies the finding.

### REEK-F12

**The one document that modelled the ASUS's firmware behaviour stated the
handle-order fact backwards, and the error pointed away from the risk.**

Supporting evidence: `CODEX-VM-UEFI-BIOS.md`'s "USB Boot Enumeration"
section against `TheBedThatAlwaysSaidYes.md#1`'s defect 1.

Falsifying evidence: a flight reading showing the ASUS enumerating the USB
boot disk's Block I/O handle first falsifies the finding, and would move the
A5 defect 1 account rather than the finding above.
### 7.14 What the post-flight third of the bed was spent on

REEK-F10 named its own falsifier: the 291,808 bytes written on or after the
first flight can serve subjects unrelated to the flights. Measured
2026-09-09 by taking each positive size delta from revision 136 to revision
240 and bucketing the delta by keyword over the changelist description. A
changelist matching two subjects counts under each, therefore the column sums
past 100 percent.

| subject | bytes added after the first flight | share of 291,808 |
|---|---|---|
| USB and xHCI | 96,615 | 33.1 percent |
| NIC, I219 and e1000 | 91,431 | 31.3 percent |
| GOP and video | 25,303 | 8.7 percent |
| disk, IDE and FAT | 25,041 | 8.6 percent |
| UEFI and boot | 19,472 | 6.7 percent |
| SMP and APIC | 4,275 | 1.5 percent |
| no bucket matched | 69,495 | 23.8 percent |

The falsifier is answered and the finding stands. The SMP work, the candidate
named in REEK-F10, accounts for 1.5 percent of the growth. The NIC and the
USB path together account for 64.4 percent, and the NIC and the USB path are
the two subsystems the flights were failing on.

### 7.15 The highest-leverage lever the project built, and nothing runs the lever

`docs/Designs/Done/Hardware/UEFI-BOOT-INVESTIGATION.md` was created
2026-07-07 by fester, is 22,642 bytes, and is read in full for the chapter.
The document is the clearest statement in the tree of what the bed did to the
project, and the statement was made 22 days before the first recorded flight.

**The document names the mechanism exactly.** Under "Why It Works in the VM
(and hides the bug)" the document cites the bed's own source: the
`AllocatePages` handler's comment is "literally `/* AllocateAddress: caller
set *R9 to exact address. Just succeed. */` -- it returns `EFI_SUCCESS` for
**any** address, occupied or not", and `GetMemoryMap` "hardcodes an 'ASUS TUF
(AMI Aptio V) compatible' map that marks `[0x100000, guest_top)` as **type 7
(EfiConventionalMemory) = all free**. A real Aptio V map is riddled with
reserved / ACPI-NVS / runtime / MMIO holes below 512 MB."

The document's conclusion is one sentence: "The emulator is not modeling the
one firmware behavior that matters for this bug: **honoring the memory map.**"

**The document proposes the repair and prices the repair as the best
available.** Under "Cross-cutting, do regardless of A/B": "Teach `codex-vm`
to model a hostile memory map behind a flag (`-uefi-strict`): reserve
realistic holes, fail `AllocateAddress` on occupied ranges. Without this, the
VM will keep green-lighting code that bricks on metal. This is the
highest-leverage single change for iteration speed -- it turns a
hardware-only bug into a VM-reproducible one."

**The repair shipped the same day, and the document records the proof.** The
section headed "Progress -- `-uefi-strict` shipped, bug reproduced in-VM
(2026-07-07)" carries a two-row table over the existing `seed/Codex.img`:
under `-uefi` the image "Boots -- compiler runs at `0x100000`, menu path
reached. Every fixed-address gamble is granted"; under `-uefi-strict` the run
gives "`AllocateAddress(0x1000000, 131072 pages) -> EFI_NOT_FOUND`, then
`CRASH: fault at RIP=0x1001082 accessing CR2=0x8000 -- the UEFI app touched
firmware-owned low memory it never allocated`". The document states what the
pair means: "The hardware-only 'same code, different behavior' bug is now a
deterministic, one-command VM failure. Iteration on the fix happens here, not
on the stick."

**Nothing in the tree runs the lever.** Censused 2026-09-09 over every
`.ps1`, `.md` and `.codex` file in the workspace. `-uefi-strict` appears
nine times in `tools/codex-vm.c`, is named in seven documents
(`UEFI-BOOT-INVESTIGATION.md`, `UsersHandbook.md`, `DiagnosticStick.md`,
`BootRoadmap.md`, `OnboardingSandbox.md` twice, and `HardwareSitting.md`
twice), and appears in exactly one script line, which is a `Write-Host`
inside `build/boot/build-option-a.ps1:127` printing a command for a human to
type:

```powershell
Write-Host "  Strict:  tools/codex-vm.exe -kernel $outAbs -uefi-strict -headless"
```

No build script invokes the flag. No rehearsal harness invokes the flag. No
gate phase invokes the flag. No `.vmargs` sidecar carries the flag. Every
rehearsal that graded a flight image, including the 50-arm rehearsal of
sitting 15, ran the permissive firmware the document names as the thing
hiding the bug.

`docs/UsersHandbook.md` at line 445 instructs the reader in the document's own
words: a permissive boot "is not evidence about firmware. Use
**`-uefi-strict`**". The instruction is prose, addressed to a person, in a
document nobody re-reads at the moment of relevance.

### REEK-F13

**The single change the record calls the highest-leverage one for turning
metal bugs into bed bugs was built on 2026-07-07 and is invoked by nothing in
the tree, therefore every rehearsal after that date has graded flight images under the
permissive firmware the same document names as the thing hiding the bug.**

Supporting evidence: `UEFI-BOOT-INVESTIGATION.md`'s "Cross-cutting" section
for the leverage claim and the "Progress" section for the shipped lever and
the reproduced crash; the census of 2026-09-09 finding `-uefi-strict` in
seven documents and in one `Write-Host` line at
`build/boot/build-option-a.ps1:127`, and in no invocation anywhere.

Falsifying evidence: any script, sidecar or gate phase passing `-uefi-strict`
to `codex-vm.exe` falsifies the finding.

### REEK-F14

**The bed's permissiveness was diagnosed as the cause of the hardware-only
failures 22 days before the first recorded flight, in a document that also
built the cure, therefore the flights that followed were flown against a
known-permissive bed.**

Supporting evidence: `UEFI-BOOT-INVESTIGATION.md` created 2026-07-07; the
first run-sheet flight entry 2026-07-29
(`docs/Hardware/HardwareSitting.md#91`); the document's fixed-address
inventory appendix, which lists six addresses and states of each address that
the address is not legal pre-ExitBootServices on metal, ending "Under `codex-vm` every
claim is granted. On metal, each is an independent coin-flip, and the boot
succeeds only if **all** come up heads".

Falsifying evidence: evidence that the flights after 2026-07-29 ran their
rehearsals under `-uefi-strict`, or that the stub rewrite removed every row
of the fixed-address inventory before the first flight, falsifies the
finding.
### 7.16 A bed value escaping into the flight, which is L-BEDTRUE

The nine cases above are the bed failing to model the board. One case runs the
other way: a value true only in the bed travelling out of the bed and into a
flight's configuration as a default.

`build/boot/diag/DiagB3.codex` carries the account in the source, at the
change of 2026-08-20: "`ip=` IS REQUIRED AND THERE IS NO DEFAULT". The prose
beneath states what the previous default was and why the default was not one:
"It used to fall back to 10.0.2.15, and that is not a default: it is the QEMU
NAT guest address, so it is A GUESS ABOUT THE NETWORK that happens to be right
in the bed and is off-subnet on any real switch."

The cost is recorded, and the cost is not a row that looks wrong. "WHAT THAT
COST IS A WRONG VERDICT, not a wrong-looking row. On metal without `ip=` the
ARP still succeeds -- the request's target is the hop, so the hop answers
whatever our sender address claims -- and then the SYN goes out from an
address that belongs to nobody, no SYN-ACK comes back, and the stage answers
`refused`." The `refused` verdict row then directed the reader to check the
peer address, the port and reachability from the segment, and all three were
innocent.

The lesson is L-BEDTRUE, and the lesson's own words fix the detection
question: the question is not whether a default is arbitrary, the question is
whether the default is TRUE ONLY IN THE BED, and the answer is a refusal
rather than a guess. `b3-noaddr` is the arm, and the arm is the runner listed
in `LESSONS.md` for the lesson.

### 7.17 The arm that cannot fail, which is L-VACUOUS, and the manuals' own admissions

L-VACUOUS states that an arm agreeing with a hypothesis AND with the
hypothesis's negation measures nothing, and reads exactly like a
falsification, therefore an arm's colour must not be read before measuring
that the arm REACHED the condition.

The chapter's case 1 is the bed instance of the lesson. `test-ovmf.ps1
-Decoy` was built to express the hostile handle order, the arm passed, and
the run sheet's own entry calls the pass VACUOUS, because OVMF ordered the
stick's raw disk first and the wrong-disk topology never reached the code
under test. An arm built and never observed refusing is an arm whose green
carries no information, and the fleet's one recorded bed instance of building
such an arm is the arm guarding the defect that is proven by metal alone as
of 2026-09-09.

The two operating manuals state bed limits of the same kind in their own
words, and each statement is an honest admission rather than a gap the chapter
found.

`docs/OperatorsManual.md#234` at line 1088 states of a fuel measurement: "no
bed can, because codex-vm completes every transfer". The manual's device
table also records the levers built to make the bed able to refuse:
`-usb-bot-drops <K>` with the note "One drop is ..." recovered,
`-usb-cfgval <N>` where the device "refuses any other value" with a stall,
`-no-hpet` making the HPET window dead, and `-usb-disk-port <N>` carrying the
mass-storage device to another root port, where the old port "goes dark rather
than answering".

`docs/ExaminersAssay.md#254` carries four admissions of the same shape:
"mouse handle dies, bed-only. The image itself is fine" at line 852; "the bed
cannot answer it (the ASUS's largest mode and whether AMI's `SetMode` honours
it are the L-FREEDOM questions)" at lines 3437 and 3438; "the bench
auto-unlock is bed-only", ruled by Damian on 2026-08-18, at line 4013; and
"the bed cannot free a retransmit queue mid-send" at line 5991.

The pattern across the two manuals is worth stating once. Every admission is
recorded at the place a person reads AFTER choosing to look, and not one of
the admissions is a runner. The fleet knew, wrote down, and could not
mechanically act on the limits of its own instrument.

### REEK-F15

**The USB video design of 2026-05-23 states the bed-as-proxy assumption as a
premise, in the words "The only difference is the pixel content", and the
same document orders the work bed first and board second two months before
the catalogue made the order a rule.**

Supporting evidence: `docs/Designs/Done/Hardware/USB-VIDEO-DRIVER.md`, created
2026-05-23, 10,132 bytes, read in full: "The kernel UVC driver should produce
identical behavior on codex-vm (test pattern) and on real hardware (real
camera). The only difference is the pixel content", and the testing strategy
listing codex-vm first and the ASUS TUF second; the Device Emulation
Catalogue's rule dated 2026-07-29 (`DeviceEmulationCatalog.md#27`).

Falsifying evidence: a reading of the shipped `UsbVideo.codex` showing the
driver taking a different path on the two targets falsifies the premise as
stated, and would leave the document's claim as an unexecuted intention
rather than an assumption the code carries.
### 7.18 The bed levers of the final week, and the one that was a gap on the last flight's own path

The emulation rows of `codex/plugs/plugs-backlog.md` record four pieces of bed
work in the campaign's last days, three of them reek's, and one of the four
bears directly on the accident.

**Row 2.46, landed by reek 2026-09-07, two days before the last sitting.** The
row's own heading states the gap: "codex-vm refused SYNCHRONIZE CACHE and had
no write-back cache to flush, so the bed could not rehearse the commit the
last sitting flies". Two defects with one cause: the modelled BOT target was
write-through, and the command allow-list failed every command outside
`{0x00, 0x03, 0x12, 0x25, 0x28, 0x2A}` with CHECK CONDITION. The last
sitting's composition carried the WORKS-62 flush, therefore the bed could not
rehearse the very step the flight was carrying until two days before the
flight.

**Row 2.45, closed by reek 2026-09-07, measured by val.** codex-vm answered 0
to every device port read from an application processor, because `handle_io`
bound its register file to VP 0 by constant in 84 places. A guest reading a
device from an application processor therefore received a plausible zero
rather than a refusal, which is L-BAILVALUE inside the bed.

**Row 2.51, open, val's measurement in reek's area.** An application processor
polling the NE2000 convoys the bed's `io_lock`, and the boot processor's disk
load crawls behind the convoy. The bed's own concurrency changes what the
guest measures.

**Row 2.52, latent, reek 2026-09-08.** `build/check-plug-ports.ps1` skips its
host half in silence when a plug ships no `run.ps1`, therefore a check whose
name promises both halves passes on one.

The four rows share a shape with REEK-F13 and are worth naming as a group: two
of the four are cases of the bed answering where the bed must refuse, and
two are cases of an instrument that passes without reaching its subject.

### REEK-F16

**The bed could not rehearse the flush the last sitting carried until two days
before the last sitting, and the lever that made the rehearsal possible was
reek's own work landed on 2026-09-07.**

Supporting evidence: `codex/plugs/plugs-backlog.md` row 2.46, whose heading
states the bed "could not rehearse the commit the last sitting flies";
changelist 23233, reek, 2026-09-07, "codex-vm accepts SYNCHRONIZE CACHE and
-usb-writeback models a write-back cache"; the sitting 15 composition carrying
the WORKS-62 flush (`docs/Hardware/HardwareSitting.md#91:185-217`).

Falsifying evidence: a rehearsal record dated before 2026-09-07 exercising a
SYNCHRONIZE CACHE commit in the bed falsifies the finding.

## Coverage

Files read in full for the chapter:

| path | size | revision |
|---|---|---|
| `docs/PM/Active/Stories/TheLostParadise.md` | 13,216 bytes | #1 (change 25188) |
| `docs/PM/Active/Stories/TheBedThatAlwaysSaidYes.md` | 9,863 bytes | #1 (change 15041) |
| `docs/Designs/Active/Tools/DeviceEmulationCatalog.md` | 20,343 bytes | #27 (change 24990) |
| `docs/Designs/Done/Hardware/CODEX-VM-UEFI-BIOS.md` | 12,300 bytes | #4 (change 11508) |
| `docs/Designs/Done/Hardware/UEFI-BOOT-INVESTIGATION.md` | 22,642 bytes | #2 (change 11508) |
| `docs/Designs/Done/Hardware/USB-VIDEO-DRIVER.md` | 10,132 bytes | #4 (change 11508) |

Files read in part, by section, with the sections named in the text where each
reading is used: `docs/Hardware/HardwareSitting.md` #91 (437,270 bytes),
`docs/OperatorsManual.md` #234 (272,924 bytes),
`docs/ExaminersAssay.md` #254 (434,457 bytes),
`codex/plugs/plugs-backlog.md`, `build/boot/diag/DiagB3.codex`, and
`docs/PM/Active/Stories/LESSONS.md`.

Extractions run, each on 2026-09-09:

```powershell
p4 changes -l //Codex/main/tools/codex-vm.c          # 240 records, 136,083 bytes
p4 sizes -a //Codex/main/tools/codex-vm.c            # every revision's size
p4 print -q //Codex/main/tools/codex-vm.c#<rev>      # bisection, per feature
p4 describe -s 1104 ; p4 describe -s 1154            # the founding pair
p4 describe -s 18394 ; p4 describe -s 15041          # authorship and dating
p4 annotate -c //Codex/main/tools/codex-vm.c         # used, then DISCARDED (see 7.12)
Select-String -Path <every ps1, md and codex> -Pattern 'uefi-strict'
```

**What the chapter did not read, and why.** `tools/codex-vm.c` was NOT read
at each of its 240 revisions as text. The head revision is 860,698 bytes,
therefore reading 240 revisions in full is about 200 megabytes of source and
is beyond a session. What was done instead, and what the claims above rest
on: every revision's SIZE was measured; every revision's changelist
description was read; and each specific claim about when a behaviour entered
the bed was settled by BISECTING `p4 print` over the revisions for a string
naming that behaviour. Claims resting on the ledger descriptions rather than
on the source are marked in the text where each claim appears.

Two further limits are stated rather than left for a reader to discover.
First, the bucketing of changelists by keyword over descriptions counts a
changelist under every subject the description names, therefore the columns
in sections 7.2 and 7.14 measure attention rather than partition the total.
Then, the ten-case inventory of section 7.10 is drawn from
`docs/Hardware/HardwareSitting.md` and the stories, therefore a bed and board
divergence recorded only in a transcript or only in a changelist description
is absent from the inventory.