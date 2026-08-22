# The Diagnostic Stick -- one image that detects the box and says what needs to happen

*Opened 2026-08-18 by red at Damian's direction: "we need a better diagnostic
template project that takes from the existing ones we've done and organizes
them into something that works for someone downloading this and running on a
box we've never seen. a procedure for detecting and informing what needs to
happen." Status: approved as a campaign the same day, and built. Owner: red,
who also composes the sittings it flies on.*

## What it is for

Two readers, one image.

**Damian's sittings, now.** Every metal question the fleet has today rides a
separate hand-built image (`nicsitting.img`, `nicring.img`, `sinkladder.img`,
`asdeflight.img`, the A8 allocation probe, the GOP mode arms) and each costs a
flash, a boot, a photograph, and a human body at the box. Damian's ruling
2026-08-18: sittings are grouped, not serial. One boot per sitting carries
every question that is ready, in an order that banks before it risks
(L-BANK), and the lanes stop building one-question images.

**A stranger's box, next.** Someone downloads `diag.img` from the mirror,
writes it to a stick, boots a machine we have never seen, and gets three
things: a screen that says what worked and what did not, a file on the
stick that carries every reading, and a short instruction naming what to
send us and what to try. No wizard, no identity, no passphrase, no keyboard
required, and nothing written anywhere but the stick it booted from.

The far end of the same road is a resident agent on the stick that
diagnoses in firmware and rebuilds the kernel for the box it finds. That is
the direction. This design is the ladder that agent would climb; it is not
the agent.

## What already exists (do not rebuild it)

The inventory was measured 2026-08-18 from source. Everything below is in
the tree and flown; the design collects it.

| flown probe | question | reads | reports | bank |
|---|---|---|---|---|
| `build/boot/diag/PciProbe.codex` | what ARE the parts | PCI config, bus 0 and behind bridges to depth 3, BAR map verdict `ok/ABOVE4G/BELOW3G/none` | glass rows + QR | none |
| `build/boot/diag/Inventory.codex` | PCI + USB + PS/2 on one boot | as above plus xHCI diag block and a PS/2 poll, PS/2 last | glass + QR, per stage | none |
| `build/boot/diag/XhciTruthProbe.codex` | what our USB stack DID | diag cell block 0x1D000, PORTSC, legsup, MSC ladder rung 0-6 | glass, named completion codes | none |
| `build/boot/diag/KbdDiagProbe.codex` (v16) | enumerates, delivers no keys, why | TRB endpoint ids, completion codes, raw boot report, IRQ1 poll | glass + QR | `KBDDIAG.TXT` |
| `build/boot/diag/MscAlignProbe.codex` | a bulk TRB across 64 KB | same LBA aligned and straddling, plus a must-fail calibration | glass | none |
| `build/boot/diag/SceneProbe.codex`, `StrideProbe.codex`, `GeoTruth.codex`, `FbProbe.codex` | is the DISPLAY path right | GOP mode, stride, channel order, containment | glass or a printed line | none |
| `build/boot/diag/KeyProof.codex` | can firmware deliver a key at all | `uefi-read-key-ex` before ExitBootServices | whole-screen colour | none |
| `build/boot/diag/NicSittingProbe.codex`, `NicInitProbe.codex`, `NicRingProbe.codex`, `AsdeStageProbe.codex` | NIC-1..4, ASDE | e1000 registers pre-write, `e1000-init` step by step under HPET, RX descriptor map, ASDE writability | glass rows, each painted before the next is tried | `RING.TXT` (ring only) |
| `apps/works/BlockLadderProbe.codex`, `SinkLadderProbe.codex` | one sector write; a 2.7 MB streamed write | BPB, FAT chain, read-back | `MetalLadder` colours | none |
| the A5 sticks | can the compiler compile itself here | SOURCE.SRC off the ESP | colour ladder + heartbeat | `OUT.CDX`, `OUT.TXT` |

Shared pieces every stage will use: `MetalLadder` (last colour standing),
`GopDraw.gop-draw-text-wrap` (rows), `GopHandoff` (framebuffer geometry from
the stub's magic-gated block), `GopFat16.gfat-mount-esp` / `gfat-write-file`
with `gfat-note-stage` (the bank and its `no bank, mount stage N` reading),
`GopShot.shot-window` (the RTC-counted bounded window, L-BANK), the two stub
liveness colours (dark blue at GOP, dark green after ExitBootServices), and
`cdx-to-pe.ps1`'s `-Ebs` / `-EntryStart` / `-HeapPages` / `-Stdin` arms.
Beds: `build/boot/test-ovmf.ps1`, `build/gop-mode-arm.ps1`,
`ladder-arm.ps1`, `sink-arm.ps1`, `disk-arm.ps1`, `test-conout-remode.ps1`,
and codex-vm's fault switches (`-e1000-no-link`, `-e1000-ctrl-ro`,
`-usb-setcfg-fault`, `-hid-root-silent`, `-uefi-conout-remode`,
`-gop-max-mode`, `-uefi-strict`). `build/boot/diag/README.md` is the account
of the probes and stays the account of each stage's readings.

## The shape

One payload, `build/boot/diag/Diag.codex`, one image, `build/boot/diag.img`.
The payload runs a fixed, ORDERED ladder of stages and never returns. Each
stage is one of the existing probes, lifted into a stage shape:

```
  DiagStage
    name        : Text            -- what the row is called on the glass
    applies     : DiagCtx -> Bool -- run only if the box has the part
    run         : DiagCtx -> DiagResult
    risk        : DiagRisk        -- Passive | Touches | Writes | MayWedge
```

`DiagResult` is a small record: a state word from the stage's own
L-STATES vocabulary (never pass/fail; `KeyProof` has three, the MSC ladder
has seven, `PciProbe`'s map verdict has four), the raw readings as text
rows, and the bank lines. `DiagCtx` carries what the ladder has already
learned (the framebuffer, the PCI list, the ESP handle, the RTC) so a later
stage never re-discovers what an earlier one measured.

**Order is by risk, and risk is the whole design.** The ladder is:

1. **Passive, no device touched.** Firmware tables and geometry: GOP mode,
   stride, channel order (`SceneProbe`/`StrideProbe`), the memory map, ACPI
   (`GopAcpi`), **SMBIOS and EDID (new; the box has no name today, see
   gaps)**, CPU features (VT-x, the `vmx` MSR read), the PCI walk with the
   BAR verdict (`PciProbe`). Everything here is a read of a table the
   firmware already built.
2. **BANK 1.** Mount the ESP of the stick we booted from and write
   `DIAG.TXT` with everything above. From here on every stage APPENDS to
   the bank before it runs the next; a wedge after a bank is a free finding
   (L-BANK).
3. **USB, read-side.** xHCI bring-up and the diag block (`XhciTruthProbe`),
   HID enumeration and the boot-report read (`KbdDiagProbe`), the MSC ladder
   read-only rungs, `MscAlignProbe`. Bank.
4. **Storage, write-side, on the stick only.** `BlockLadderProbe` (one
   sector at a known LBA past the volume) then `SinkLadderProbe` (the 2.7 MB
   streamed write, WORKS-9's question). Bank. **`sink` KEEPS THIS NUMBER AND
   THIS ROW BUT EXECUTES LAST** (root, 2026-08-21): it can kill the medium,
   and the medium is the bank, so every stage after it in the list would lose
   its record. Sitting 7 paid for that reading -- `DIAG.TXT` held stages 1 to
   8 and stopped. Deferral is by `dg-stage-defers`, the main pass reserves the
   slot, and a labelled `before-deferred` summary is banked before the
   deferred stage runs. A stage that can END THE RUN rather than the medium
   (`asde`) is NOT deferred, because deferring it would guarantee it runs
   unbanked and the ASUS has no serial port.
5. **NIC, passive.** `e1000-find`, the pre-write register rows
   (`NicSittingProbe` NIC-1/2), the poll calibration. Bank.
6. **NIC, init and ring.** `NicInitProbe`'s stepwise `e1000-init` under
   HPET budgets, then the RX descriptor map (`NicRingProbe`, NIC-4's ring
   question). Bank.
7. **NIC, conversation.** B3: bring the stack up and hold one TCP
   conversation with a peer named in the config (or skip if none). Bank.
8. **NIC, the driver's own write read back.** `pchk1` (`DiagPchK1.codex`,
   added 2026-08-21): PHY page 770 register 17 again, this time AFTER
   `e1000-init` has written it. The passive `pch` stage reads the same
   register at the top of the ladder, so its reading is the platform's
   power-up value; the K1 write happens inside `e1000-init`, which the ladder
   reaches only through `net-driver-bring-up` in `b3`. Between them nothing
   read the register, so a flight where traffic still did not flow could not
   tell **a fix that was applied and did not help from a fix that was never
   applied at all**. That is the whole of what the stage is for.
   **Its position is a constraint, not a preference.** It must follow the
   writer and precede `asde`: `asde` calls `na-phy-kick`, which writes BMCR
   reset, and a PHY reset returns 770.17 to its NVM value, so a reading taken
   after `asde` reports the NVM setting on every run and would say the write
   never took even when it took. A later ladder that brings `e1000-init` up
   earlier moves this stage with it, not to a fixed number.
   Bed arms `k1-taken` (`-i219`) and `k1-blocked` (`-i219-mng-holds`) move the
   row from one binary. **Both name a peer**, because `b3` short-circuits on
   no-peer before bring-up: with no peer nothing writes K1, and the arm read
   `not-taken` until it dialled. `not-taken` with MDIO answering is BOARD-ONLY
   and declared as such in the chapter -- no bed knob lets the read succeed
   while the write fails.
9. **The day's questions.** Whatever a lane routed for this sitting, each a
   stage in its own file, run last among the risky ones: ASDE
   (`AsdeStageProbe`), the A8 allocation grant, the largest GOP mode and
   `SetMode`. Bank after each.
10. **`MayWedge`, never by default.** NIC-5 (what wedged the box on
   2026-08-11) and anything else terminal by construction runs only when the
   config names it, and it is always the last line of the ladder.

Every stage paints its row BEFORE it runs (the row says `running`), then
overwrites it with the state word in the stage's colour, so the last row
standing names where the box stopped, exactly as `NicInitProbe` and
`Inventory` do today. The screen ends with a summary band: stages run,
stages skipped and why, the bank's path and byte count, and the "what to
send us" line.

**`DIAG.CFG` selects the ladder without a rebuild.** A text file on the
stick's ESP, one stage name per line with `on`, `off`, or a parameter (the
peer address for B3, the LBA for the block ladder). Absent, the default
ladder runs stages 1-7 with the stranger's defaults and skips 8-9. A sitting
is a `DIAG.CFG` red writes for the day; the image bytes do not change
between sittings, which is what makes L-REHEARSE affordable: the rehearsed
image IS the flown image, and only the config differs, and the config is
rehearsed too. The stub's `-Stdin` arm is how the payload gets the config
today; the design keeps that and adds the file read for the case where the
stub was built without one.

## The output channels, and why there are six of them (L-CHANNEL)

The stick has been unwritable, the framebuffer has been the wrong shape,
the font has been unreadable, and each of those took a sitting to notice
because the probe of the day reported on one channel and that channel was
the thing under test. Damian, 2026-08-18: "we need to anticipate the disk
not being writable ... as well as going from vga->uefi->gop ... we got the
stick writes working finally and i took down the camera rig, but then we
ended up regressing and i had to set the rig back up." So the ladder
reports on EVERY channel that is alive, in this order, and a stage does not
get to choose:

| # | channel | alive when | what it carries | fails how (seen) |
|---|---|---|---|---|
| 1 | firmware text (VGA text mode or UEFI `ConOut`) | before ExitBootServices, and after it only on a box that kept text mode | the first line: image hash, kernel digest, `-Ebs`/`-Uefi` world | `ConOut` re-mode changes the geometry under the stub (`-uefi-conout-remode` arm; `GeoTruth`) |
| 2 | screen colour (whole-panel fills) | as soon as GOP is acquired; needs no font, no stride | liveness (dark blue, dark green), then the last colour standing per stage | stride wrong paints a diagonal, still readable as a colour (`StrideProbe`) |
| 3 | GOP text rows | GOP plus a font that renders | the readings, one row per stage | stride 2048 versus 1920 on the ASUS sheared the rows; the font proof row is the check |
| 4 | QR on the glass | GOP, and a panel wide enough for the chosen scale | the summary and the readings, machine-readable off a photograph (`tools/qr-read.ps1`) | scale 2 decodes as nothing and looks like success; a fifth code truncates on 1280 wide |
| 5 | the bank file `DIAG.TXT` | the ESP mounts and the volume is writable | everything, appended per stage, `END` last | `no esp s1 m3 c4` (F12 bank, 08-13); the seed medium lock refusing a seedless stick; the second write on metal (WORKS-9) |
| 6 | serial | a box with a port, or the bed | everything, streamed | codex-vm cannot screenshot a halting payload, serial is how the bed reads it |

Channels 2, 3 and 4 need the camera. **The camera rig is a standing
instrument, not scaffolding**: it came down when the bank started working
and went back up when the bank regressed, and the design assumes it is
there. A sitting is composed on the premise that channel 5 may fail and
the photograph is the record. Conversely the bank does not wait for the
glass: it is written first after every stage, and the glass is repainted
from what was banked, so a photograph and the file never disagree.

**The page is FIXED so photographs compare across boots.** Today the
diags paint the `12345678` font proof, then colour bars, then a text page,
and every image lays it out differently, so two photographs a week apart
cannot be read against each other. The diagnostic page is one layout, and
a stage may not draw outside its row:

```
  row 0   12345678 ABCDEFGH abcdefgh        <- font and stride proof, always
  row 1   [8 colour bars]                    <- channel order proof, always
  row 2   diag <hash8> kernel <digest8> world=EBS|UEFI cfg=<n stages>
  row 3   box: <SMBIOS product or "unnamed"> fb=<w>x<h> stride=<s> ram=<MB>
  rows 4..N   <stage> <state> <readings, wrapped, at most 3 rows>
  band    SUMMARY  run=<n> skip=<n> bank=<ok NNNN bytes | lost at=<stage> size=<n> | no bank, mount stage N>
  below   QR (summary), scale chosen 6/5/4/3, never 2
```

Rows 0-3 are painted before the first stage runs. A stage that cannot
fit its readings in three rows banks the rest and paints `+more in bank`.
The colour of a stage row is its state word's colour and nothing else is
ever coloured, so "what colour is row 7" is a question a photograph
answers.

**`bank=ok` used to mean "a medium was selected and the first write
landed", and nothing more.** Every later write's answer was discarded, so
a stick that went wedged mid-ladder produced a truncated `DIAG.TXT` under
a SUMMARY painting `bank=ok`. **Naming the loss was never the same as
stopping it, and sitting 7 is what made the difference visible: the row said
`bank=lost at=sink` perfectly correctly over a file missing the nine readings
the flight existed for.** The stage order is what stops it (step 4 above);
the row below is what reports it. That is what came back from metal twice
(`HardwareSitting`, sittings 2 and 3). The bank's truth is the FILE: each
write reads the size back from the directory entry, a disagreement is a
refusal, and the first stage that loses an append is banked in cell 90 and
named in the row. The verdict is on the serial as well as the glass now,
which it never was, so the record can carry it even when the medium
cannot. Bed arm: `bank-lost`, which wedges the medium from the refusal
onward rather than transiently.

**A REHEARSAL CERTIFIES AN IMAGE, so what counts as stale decides what the
record is worth.** `diag-arm.ps1` refuses an image older than any
`build/boot/diag/Diag*.codex`, and since 2026-08-19 also one older than
`seed/Codex.cdx`: the seed moves on any merge-down, nothing rebuilds the
image when it does, and a rehearsal would otherwise certify an image the
current compiler never built. That case is not hypothetical, it fired on
the ladder the hour it was added. **Two holes are left and neither has a
runner.** The scan covers only the `Diag*.codex` chapters, not the 57
chapters the image BUNDLES, so a change to `GopFat16` or `GopUsbMsc` leaves
the image looking fresh; widening it to the bundled set is a superset that
can only over-refuse, which is the safe direction. And all of this is
mtime, which a `p4 sync` sets to the sync time rather than the content's
age, so it is a proxy for the question and not the question. The recipe
records the `kernel=` digest the image was compiled with, which IS the
question, but comparing it needs the current seed's digest and that costs a
compiler run.

**The bank path has a permanent runner so it cannot regress silently
again.** `diag-arm.ps1` boots the exact image in codex-vm and under OVMF
with a USB stick image attached, reads `DIAG.TXT` back off the stick image
(`build/read-stick.ps1` shape) and requires it to end in `END` and to
agree with the serial transcript row for row; and it runs the same with
the medium made read-only (`-usb-bot-drop` and a write-protected image)
and requires the ladder to reach the summary band saying `no bank` while
the QR still decodes. That pair is the negative control: a bank that stops
working fails the arm the day it stops, in the bed, before anybody sets
up a rig. Where the runner lives is decided by what it costs: it is a
battery row if it fits the battery's budget and a release-proof row
otherwise; either way it runs before every flight (`flash-usb.ps1
-Rehearsed` refuses an image the arm has not passed).

**`p4 edit` the three depot artifacts before rebuilding or rehearsing.**
`build/boot/diag.img`, `build/boot/diag.rehearsed` and `tools/codex-vm.exe`
are read-only until opened, and two of the three fail SILENTLY: `build-img`
cannot write the image and `build-diag.ps1` reports only "build-img failed"
with no cause, and `diag-arm.ps1` runs the whole ladder green and then
cannot record it, so the image stays unflashable after a ten-minute run.
Measured 2026-08-19, all three in one sitting's worth of work.

The account of every channel failure so far (the F12 bank, the stride,
the ConOut re-mode, the medium lock, the seed-less stick) is
`docs/Hardware/HardwareSitting.md`, and this section is the design
reading of it: no probe reports on one channel again.

## The bank, and the one rule it changes

The bank is `DIAG.TXT` on the ESP of the medium we booted from, appended
after every stage, plain ASCII, one `stage=... state=... ` line then the
readings, and a final `END` line so a truncated bank is visible as
truncated. It is the record; the glass and the QR are conveniences.

**A `DIAG.CFG` ON THE ESP CAN ONLY SELECT STAGES THAT RUN AFTER THE BANK,
and that is a property of the order rather than of the parser.** The passive
stages run before the bank opens, because opening it means `usb-attach` and
the medium lock, and a passive stage touches no device. The stub's `-Stdin`
ring is read first and selects ALL of them. Both sources are read, the header
row says which arrived (`src=stdin`, `stdin+file`, `default`) and the bank row
says how many file lines came with it.

Today `GopMedium` writes only to a volume whose ESP holds `CODEX.CDX`
(`GopMedium.codex:19`), which is why every seedless probe stick paints its
F12 shots OFF and why only three of the flown probes have a bank at all.
The rule exists so a desk never writes to the wrong disk. The diagnostic
keeps the intent and changes the marker: **it writes only to a volume whose
ESP holds `DIAG.ID`, a file the image builder puts there, and the ID's
content (the image's own SHA-256 prefix) must match the payload's built-in
constant.** A stick that is not this image, or another disk with a stale
`DIAG.ID` from a previous image, is refused. That is a stricter lock than
the seed marker, not a looser one, and it lets the probe image carry no
seed at all.

If the mount fails the ladder does not stop: it paints `no bank, mount
stage N`, switches to QR for the summary (bounded to what the panel can
carry: the scale is chosen, scale 2 is not offered, exactly as `PciProbe`
does), and continues, because a box whose ESP we cannot mount is a finding
worth every reading after it.

## The report: what needs to happen

The last thing the ladder does is print, and bank, a verdict block that a
stranger can act on without us. It is a table from stage states to a
sentence, written in the design so it can be reviewed and in the payload
so it is printed. The first rows:

| reading | what it means | what to do |
|---|---|---|
| screen unchanged from firmware | the image was never loaded, or GOP was refused | check the boot order and Secure Boot; send a photograph of the firmware boot menu |
| solid dark blue only | the stub died before ExitBootServices (allocation, memory map) | send the photograph; the allocation question is ours |
| solid dark green, no rows | the payload died before its first row | send the photograph and the stick's `DIAG.ID` |
| stage 1 `BELOW3G` on any BAR | a device window sits inside our RAM arena; the box needs a mapping fix from us before any driver runs | send `DIAG.TXT`; do not expect the network or USB rows to be right |
| stage 3 keyboard `EPINT` present, no report | HID enumerated and is silent (the `SET_IDLE` shape) | send `DIAG.TXT`; the fix is ours |
| stage 4 `no bank, mount stage N` | the stick's ESP could not be mounted from the payload | photograph the QR codes; try a different stick, FAT16, under 32 GB |
| stage 6 `e1000-init` last row `aneg` or `link` | link took longer than its budget | plug the cable, boot again; if it repeats send `DIAG.TXT` |
| every row green through 7 | the box runs the whole stack we have | send `DIAG.TXT` anyway: a green box is a data point |

The table grows one row per new state; the discipline is that no state
word exists in a stage without a row here, checked by a script over the
stage vocabularies (`build/check-diag-verdicts.ps1`, step 3).

## What a stranger does

1. Download `diag.img` and its SHA-256 from the release. Verify the hash.
2. Write it to a USB stick: `build/flash-usb.ps1 -Image diag.img -DiskNumber N`
   on Windows, or the `dd` line the README gives on anything else. The
   image is under 16 MB and carries no seed and no identity.
3. Boot the box from the stick. Wait for the summary band (under two
   minutes on the default ladder; every stage has an HPET or RTC budget, and
   a stage that has no clock is spin-fuelled).
4. Read the last line. If it says `bank ok NNNN bytes`, put the stick back
   in a computer and send us `DIAG.TXT`. If it says `no bank`, photograph
   the screen and the QR codes and send those.
5. The verdict block tells you whether anything can be done at your end
   (cable, boot order, a different stick). Everything else is ours.

That is the whole procedure and it goes in `docs/UsersHandbook.md` beside
the USB stick build recipe, and in the release notes.

## What a lane does with a metal question

A lane never proposes a flight. It writes a stage: a chapter under
`build/boot/diag/` in the `DiagStage` shape, with its L-STATES vocabulary,
its verdict rows for the table above, its bed arm (the codex-vm switch or
OVMF configuration that forces each failure state, run by `diag-arm.ps1`),
and the expected readings on the ASUS written down BEFORE the boot. It
routes one line to red: stage name, CL, expected readings. Red adds it to
the sitting's `DIAG.CFG`, rehearses the exact image plus config in both
beds, records the image hash and the config in `HardwareSitting.md`'s
flight card, and Damian sits once. After the boot the readings are
transcribed from `DIAG.TXT` into the sitting doc, and the lane reads them
there. The lane's own row in its register is where its conclusion lives.

## Rehearsal, because a green bed says nothing about a hostile choice

`build/boot/diag-arm.ps1` runs the whole default ladder in codex-vm and
under OVMF and requires: every stage reaches a state, the bank exists and
ends in `END`, and each stage's forced-failure switch moves exactly that
stage's state and no other (L-FALSIF, L-INSTRUMENT). It also runs the
resource envelope of the flying image, not the bed's generosity (L-ARENA):
the payload is built with the same `-HeapPages` and no larger arena than
the stick gets. The rehearsed hash is the only hash that flies (L-REHEARSE),
and `flash-usb.ps1` gains a `-Rehearsed <hash>` check that refuses to
flash an image whose hash is not in the rehearsal record (LESSONS row
L-REHEARSE names exactly this candidate runner). Where the bed cannot
express a state (OVMF has no `MAP=ok`, `CYAN` has never fired on either
ladder), the design says so in the stage's account rather than claiming
coverage (L-GAP).

### A SITTING image cannot be rehearsed by a suite that knows one baseline

The arms were written against the DEFAULT image, and a sitting bakes its
questions in. `sink ladder=1` moves the sink baseline from `ok` to
`ladder-all`; a baked `b3 peer=` moves `pass` from `no-peer` to `no-part`;
and with the ladder on a refused small write lands as `rung-1-refused`
rather than `write-refused`. Ten arms read as MISMATCH on 2026-08-20 for
those three reasons and none of them was a defect. That is L-REHEARSE
pulling against the suite: the exact bytes that fly are the ones the suite
could not read.

`diag-arm.ps1` now takes the baseline from `build-output/diag-recipe.txt`,
and only when that recipe describes the image in hand, so a config the
bytes do not carry cannot be assumed (L-SAMEVER). Two scopes matter and
both were learned by getting them wrong: only an arm booting the
UNMODIFIED subject carries the subject config, because a `New-Variant` arm
lays down its own `DIAG.CFG`; and the ESP config is read only AFTER the
bank opens, so `no-medium` and `fat-full` run the default baseline however
the image was built.

**A third scope, and until 2026-08-21 it was a composition the suite could
not rehearse AT ALL: a stage turned OFF.** red built a sitting with `asde
off` and five arms disagreed (`pass`, `nic-pass`, `nic-nolink`,
`asde-differs`, `asde-ctrlro`), every one on the same row, `stage=asde
state=skipped risk=writes cfg=off`. The first two scopes moved a baseline;
this one had no expressible arm, so the composer either flew unrehearsed
bytes or abandoned the composition, and red abandoned his. It is two
repairs rather than one, because the five arms are not one population. A
general arm does not care what the sitting composed: `diag-arm.ps1` reads
the recipe's config the way `diag-cfg-find` does (first match, bare key is
`on`, only the exact word `off` disables, ring before ESP) and expects
`skipped` for every stage the subject turns off. An arm ABOUT the stage
must not inherit that, since an asde arm that silently accepts asde being
skipped is an instrument that cannot fail (L-FALSIF): `asde-differs` and
`asde-ctrlro` are `New-Variant` arms now, laying down the subject's own
composition with `asde on` forced ahead of it so first-match takes the
force and everything else stays as composed. Measured on an `asde off`
subject: all five green, `asde-differs` reading `differs` (which a skipped
stage cannot produce), and `pass` on the default image unchanged.

### A stage banks as it goes (root, 2026-08-21, off sitting 9)

Sitting 9 died inside `b3`'s bring-up and `DIAG.TXT` came back whole through
`nicring` and then `END`: the only thing the medium said about the stage
that killed the run was that it was absent. The glass had `b3 -> bring-up`,
which names a function with four things inside it, and the ASUS has no
serial port, so the `b3 entering X` lines that name steps on the wire were
never going to reach anyone. A stage could not bank mid-run at all: the
ladder holds the `DiagBank` and the lines so far, banks between stages, and
hands each stage a `DiagCtx` whose `dc-bank` is a Boolean.

The ctx now carries the bank (`dc-vol`) and the lines banked so far
(`dc-lines`, set by the runner before each stage), and `DiagStage` owns the
write path (`diag-bank-write-vol`, the file body, the name buffer) so a
stage can call `diag-bank-note c "stage=b3 step=reset"`: the file is
rewritten as everything banked so far plus that note, and the ladder
rewrites it again when the stage returns. A note therefore lives on the
medium exactly as long as the stage is between it and its result, which is
the interval a wedge freezes. Each note costs one file's length of heap
(the buffer is allocated before the mark by design).

`b3` uses it two ways. **Sub-steps:** bring-up is stepped through the
driver's own functions in the driver's own order (`e1000-reset`,
`e1000-init-after-reset`, `e1000-pch-prepare`, bind, `net-driver-calibrate`),
each painted and banked, and the K1 readback is banked the moment
`e1000-pch-prepare` returns, so a wedge in calibrate, link wait or ARP keeps
it. The semaphore and `e1000-link-up` sit inside `e1000-init-after-reset`
and cannot be separate steps from outside the driver; that half is blu's.
The cost is that `b3` now mirrors `net-driver-bring-up` rather than calling
it, at the granularity of the functions it is made of; the drift risk is
named here because no arm can measure it. **The clock control:** before
bring-up, `hpet-ticks` is read across 100000 reads and `clk=y/n dt=N
moved=N/100000 hpet=N` is banked at once. Sitting 9 proved the clock
advancing at stage 12 (`nicinit`'s budgets landed to the microsecond), so
at stage 14 this is a control that should read `y`, and a reading that does
not is the finding. `clock-stuck` refuses before bring-up, because a rate
nothing validates over a counter that does not move makes every clocked
wait in the driver effectively endless: `e1000-await-link-clocked` is
100000 batches of 4096 STATUS reads. `-hpet-frozen` in codex-vm models the
undecoded-window shape blu named, all-ones everywhere, period 0xFFFFFFFF
deriving a bogus nonzero 232830 Hz; the `b3-clockstuck` arm turns the three
nic stages off by cfg so nothing ahead of b3 spends its fuel on the same
stuck clock, and asserts `clk=n` on the refusing row so the verdict carries
its measurement.

**Sitting 10 flew the stepped `b3` and it named the hang** (red, 2026-08-21
night): the ladder ran through `nicring`, the glass painted `b3 -> clock` then
`b3 -> reset`, and the last line before `END` on the medium was `stage=b3
step=reset`. The hang is INSIDE `e1000-reset`, before the semaphore, before
link-up, before K1, the first time it has had a name. The same flight caught
the instrument dropping its own control row (L-BANK): the clock line was
banked first and was not in the file, because a note was built as the ctx's
lines plus one note and the ctx's lines are fixed for the stage's run, so
every note REPLACED the one before it. A note now reads the medium back,
strips `END` and appends, so the trail reads clock, reset, rings-link, k1,
k1=N, calibrate; the `b3-pass` arm refuses a run whose `banked=N` values do
not strictly grow across the notes, which is the replacing shape exactly.
The first version of that fix read the file back through `gfat-text`, which
builds Text with `acc &` per character, eleven megabytes of prefixes per
4.8 KB read-back, and the seventh note walked the heap into the stack of the
128 MB arena (EXC=06 at a garbage RIP, R10 past RBP). The file is copied as
bytes now, and every step note carries `heap=N`, the bump pointer at that
step, so the arena is read on the medium. Measured on the bed: bring-up's
steps cost a quarter to half a megabyte each, and the TCP `exchange` step
costs 33.8 MB, a quarter of the flight arena (L-ARENA); what it costs on
metal is a bank row now, and the number is for whoever owns the net stack's
heap rather than for this design.

**The composition queue after sitting 10, in red's order, one change per
flight.** Sitting 11: split `reset` into its seven operations (imc,
ctrl-read, rst-write, await-reset, settle-mdio, imc, icr), each painted and
banked; `NicInitProbe.codex:206-224` runs the same seven at stage 12 in
milliseconds and `b3` hangs in them at stage 14, same code, different part
state. **BUILT (root, 2026-08-21 night): `db3-reset` in `DiagB3.codex`**,
the seven in `e1000-reset`'s own order, each a `db3-step` so it paints
`b3 -> reset-X`, banks `stage=b3 step=reset-X heap=N`, and the `rst-write`
and `settle-mdio` notes carry the CTRL value read and the `settled` answer
(the `k1=N` in the row is `e1000-pch-prepare`'s code, 0..7 since main
18736: 3 owned and stuck, 2 owned and not, 6/7 MDIO refused owned/unowned);
the value returned is `settled`, so bring-up's absent decision stays the
driver's. The `b3-pass` arm now requires twelve notes. Then: a LISTEN after the K1
write. reek measured that no stage listens after K1 (order is nicinit,
nicring, b3, pchk1, asde, and nicinit never does the K1 step), so the board
can never show DD landing once K1 is disabled; a nicring-shaped window after
`b3`'s k1 step, or `pchk1` growing one, is the flight-shape change that would
prove the campaign's claim on metal. **BUILT (root, 2026-08-21 night), for
sitting 12: `pchk1` listens 1.2 s on the production ring `b3` bound, through
the driver's receive path, GPRC fenced before and counted after, DD counted
on the ring, and says `quiet` / `arrived-visible` / `arrived-invisible` /
`skipped` in its `listen-after-k1` row. The bed pair is the two K1 arms with
one armed frame released by pchk1's own GPRC read (`nicring off` in their
cfg): `taken` reads `arrived-visible`, `no-mdio` under MNG held reads
`arrived-invisible`, K1 the only difference.**

**Sitting 11 flew the seven reset operations and the medium died INSIDE
bring-up** (red, 2026-08-21 evening): the trail on the stick read clock and
all seven reset operations and `rings-link`, each with `heap=`, then `END`;
the `k1` and `calibrate` notes never landed, b3's exchange completed on the
real I219 (the dev box echoed 13 bytes), and the glass said `BANK LOST AT
STAGE 15 pchk1`, the stage whose whole-stage write the LADDER first saw
refused. The medium stopped taking writes between the `rings-link` note and
the `k1` note, during `e1000-init-after-reset`, and nothing said so where it
happened: a step's note already answers -1 when its write is refused or its
size readback disagrees (`diag-note-bytes` reads the root directory sector
back fresh), and `db3-step` printed that as `banked=-1` on a serial wire the
ASUS does not have, then painted the step name without it. The bank is not
independent of the subsystem under test (xHCI and the I219 share the PCH),
which is L-CHANNEL in the campaign's own words, so the glass is the only
channel once the medium dies and it has to name the step.

**BUILT (root, 2026-08-22), for sitting 12: a step paints its own refusal.**
`db3-step` now paints `BANK LOST AT <step>` in `diag-col-bad`, the colour
class of the ladder's band, and says `b3 bank lost at <step>` on serial, the
moment `diag-bank-note` answers -1 with a bank open (`dc-bank`; no medium at
all is not loss and paints nothing new). The arm is `b3-banklost`, and its
death is keyed to the thing under test rather than to an ordinal: the census
shows every bank rewrite taking a fresh cluster (LBA 3489, 3494, 3500, ...
climbing) with a length that steps with the file, so `-usb-bot-die-len 5632
-usb-bot-die-lba 3400` kills the medium on the first eleven-sector file
write, which by the measured note sizes (4788 to 5460 bytes across b3) is
the seventh b3 note, `reset-imc-again` at 5141 bytes; the bank's FAT and
directory writes sit at 2049 and 2153, below the key. Measured: the first
`bank lost` line names `reset-imc-again`, every note before it banked and
every note after it `-1`, the medium's last b3 note is `reset-settle-mdio`,
the summary says `bank=lost at=b3`, and codex-vm's own line says the target
died on a 5632-byte write at LBA 3617. Ten seconds; a dead medium refuses at
once. `b3-pass` is the control (fifteen notes, none refused).

**The slot paint is transient, and the screenshot said so before the record
could claim otherwise.** `dg-paint-result` overwrites the stage's slot with
its own row when the stage returns, and on sitting 11 `b3` RETURNED (the
exchange completed), so `BANK LOST AT k1` would have been gone from the glass
before anyone photographed it; the step paint survives only the wedge shape
(sittings 9 and 10). So `db3-run` zeroes two diagnostic cells at entry
(metal RAM is not zeroed: cell 92 counts the stage's notes, cell 93 holds the
first refused one) and at its single exit stamps `bank-lost-note=N` onto
`b3`'s FIRST glass line and first bank line and turns the row red, whatever
its state. The first line is the one the slot keeps and the QR is built from,
so the ordinal reaches the photograph and the QR both. It is an ordinal and
not a name on purpose: two step names carry live values and the DHCP path
adds a step, so a name table would drift, and the count is what the serial
trail and the stick's own trail already use. **Read the two together**: the
stick's trail ends at note N-1 and the glass says N, and a medium that
ACCEPTED a write and lost it, the one shape no in-band readback can see
because the size readback goes through the same controller, shows as a GAP
between the two numbers. Sitting 11's trail ended at `rings-link` with
`b3`'s own result missing and the ladder's whole-stage write after `b3`
apparently accepted, which is consistent with that shape; the next flight's
numbers decide. What no arm can judge is the glass itself; the serial line
and the paint are one branch of one function, and the row was looked at by
eye under `-screenshot` when it was built.

**BUILT (root, 2026-08-22), for sitting 12: `rings-link` split into the six
parts of `e1000-init-after-reset`**, in the driver's order, each painted and
banked before it runs, the same shape as the reset split: `rings-quiesce`
(quiesce, the ring and buffer allocations, the MTA clear, the MAC read, the
record), `setup-rx`, `setup-tx`, `swflag` (the acquire, 2,000
read-modify-writes of EXTCNF_CTRL with MNG held; the note after it carries
`sem=`), `link-up` (the CTRL|SLU write; the note after it carries `link=`)
and `swflag-release`. reek measured that sitting 11's medium survived
`nicinit`'s whole bring-up and died inside this function, and of its parts
the SWFLAG acquire and the SLU write are the two `nicinit` never did, while
the ring setup is what it did under a receiver that was never quiesced; the
next flight's trail names which. `db3-init-after-reset` mirrors the driver
function at the granularity of its parts and rebuilds the `E1000Device`
record verbatim, so a field added to the driver refuses here at compile time
rather than drifting. `b3-pass` now requires twenty notes; `b3-banklost`'s
seventh note is unchanged, so its key still lands on `reset-imc-again`.

### Landing a diag CL while main moves: rebuild after every unshelve

The stale guard compares `diag.img` against the chapters by mtime, and the
shelve/merge/unshelve dance rewrites every shelved chapter AFTER the image
that was built from them, so the guard refuses the rehearsal on the very
first arm even though the bytes are the same. Measured twice on 2026-08-21
landing the stepped-b3 CL, two cycles of thirty minutes spent reaching a
refusal. The order that lands is: merge down, unshelve, **rebuild the image**
(deterministic, so an unchanged source re-hashes to the same image and the
record already carries it), rehearse only when the bed or a chapter actually
changed, then gate. And keep the window short: rehearse and gate in one chain
and copy up the moment it is green, because three merges in one evening each
brought `tools/codex-vm.c` and the exe has to be rebuilt from merged source
before the arms certify anything.

### The subject must not move under the run

Every arm copies the image fresh, so a `p4` sync or revert landing
mid-rehearsal swaps it for every arm after that point. On 2026-08-20 a
handoff revert did exactly this: arms before it booted payload `be035dc8`
and `asde-ctrlro` booted `6e825d95`, a payload whose stage list predates
`gopmode`, and the verdict read `(no gopmode stage row)`, which is a
defect-shaped answer to an integrity failure. `Assert-Subject` re-hashes
the image before every arm and refuses, naming both hashes. The startup
stale check cannot see this: it runs once, before arm one.

## What is still open

Everything else this design describes has landed, and the depot is the
record of it. Two rows and one direction are left.

- **`gop-mode-arm.ps1` asserts the wrong half.** Its six arms read
  `GOP: SetMode N` off codex-vm's stderr and check the BMP geometry, so
  handoff v3's banked maxmode, mode-before, mode-chosen and `EFI_STATUS` are
  asserted by nothing, `maxmode1` included. That is red's row: the stage
  reports the bank, and nothing checks the bank it reports.
- **`DIAG.RCP` truncates silently past 12 lines or 2 KB.** The recipe fits
  today. This is the thing to tighten first if it grows.
- **The road, not scheduled.** A stage that compiles a chapter off the stick,
  a stage that writes a rebuilt kernel back, and the resident agent that
  chooses stages from what stage 1 found. Named so the ladder is built with
  it in mind: the ctx is the agent's memory, the verdict table its first
  policy.

## Cost

The payload is one bare-metal CDX with no GC; each stage's allocations are
bounded by what the flown probe already allocated, and stages run in
sequence from one `DiagCtx`, so the high-water mark is the largest single
stage (the sink ladder's 2.7 MB buffer) plus the ctx, well inside the
32768-page arena the A5 sticks fly with. Time is the sum of the stage
budgets: every loop that waits on hardware carries an HPET or RTC deadline
(the NIC-4 lesson: a count is not a duration), and the default ladder is
under two minutes by construction. Nothing is quadratic in the readings; the
bank is one rewrite per stage of every line so far (`gfat` has no append),
so its cost is linear in stages times bank size, not constant.

## Non-goals

Not the desk, not the wizard, not a shell. No write to any medium but the
booted stick's ESP, and only under `DIAG.ID`. No stage that can wedge the
box in the default ladder. No claim of hardware coverage the bed did not
express: the design says where the bed is silent.
