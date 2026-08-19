# The Diagnostic Stick -- one image that detects the box and says what needs to happen

*Opened 2026-08-18 by red at Damian's direction: "we need a better diagnostic
template project that takes from the existing ones we've done and organizes
them into something that works for someone downloading this and running on a
box we've never seen. a procedure for detecting and informing what needs to
happen." Status: approved as a campaign the same day; step 0 is this document and
step 1 (the ladder framework, root) landed 2026-08-18. Owner: red, who also
composes the sittings it flies on. Step 3 (the SMBIOS, EDID and CPU rows and
the verdict checker) landed the same day.*

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
   streamed write, WORKS-9's question). Bank.
5. **NIC, passive.** `e1000-find`, the pre-write register rows
   (`NicSittingProbe` NIC-1/2), the poll calibration. Bank.
6. **NIC, init and ring.** `NicInitProbe`'s stepwise `e1000-init` under
   HPET budgets, then the RX descriptor map (`NicRingProbe`, NIC-4's ring
   question). Bank.
7. **NIC, conversation.** B3: bring the stack up and hold one TCP
   conversation with a peer named in the config (or skip if none). Bank.
8. **The day's questions.** Whatever a lane routed for this sitting, each a
   stage in its own file, run last among the risky ones: ASDE
   (`AsdeStageProbe`), the A8 allocation grant, the largest GOP mode and
   `SetMode`. Bank after each.
9. **`MayWedge`, never by default.** NIC-5 (what wedged the box on
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
  band    SUMMARY  run=<n> skip=<n> bank=<ok NNNN bytes | no bank, mount stage N>
  below   QR (summary), scale chosen 6/5/4/3, never 2
```

Rows 0-3 are painted before the first stage runs. A stage that cannot
fit its readings in three rows banks the rest and paints `+more in bank`.
The colour of a stage row is its state word's colour and nothing else is
ever coloured, so "what colour is row 7" is a question a photograph
answers.

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

The account of every channel failure so far (the F12 bank, the stride,
the ConOut re-mode, the medium lock, the seed-less stick) is
`docs/Hardware/HardwareSitting.md`, and this section is the design
reading of it: no probe reports on one channel again.

## The bank, and the one rule it changes

The bank is `DIAG.TXT` on the ESP of the medium we booted from, appended
after every stage, plain ASCII, one `stage=... state=... ` line then the
readings, and a final `END` line so a truncated bank is visible as
truncated. It is the record; the glass and the QR are conveniences.

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

## Gaps this design has to close (from the 2026-08-18 inventory)

1. **CLOSED 2026-08-18 (step 3).** No SMBIOS and no EDID reader in the tree; the
   box had no name and the monitor no identity in any bank. Now `DiagSmbios`,
   `DiagEdid` and `DiagCpu` are the first three stages, fed by the stub's
   handoff block v2 (see the step-3 record).
2. **Bank coverage.** Only three flown probes bank. Every stage banks after
   the `DIAG.ID` change.
3. **The seed lock versus the seedless probe.** Closed by `DIAG.ID` above.
4. **NIC stages have no bed arm and `test-ovmf.ps1` has no NIC.** The
   codex-vm e1000 switches are the arm; OVMF gains `-netdev` in
   `diag-arm.ps1` for the enumeration rows only.
5. **`-Ebs` versus `-Uefi` is a silent fork.** The payload checks the
   handoff block's magic and the SystemTable cell at its first row and
   names which world it is in; the wrong one is a state word, not zeros.
6. **Recipes no longer reproduce flown hashes.** The image builder writes
   the recipe (flags, kernel digest, source CLs) INTO the image's ESP as
   `DIAG.RCP` and into the bank's first lines, so a bank names the bytes
   that produced it.
7. **QR chunk count is unbounded.** The summary QR is bounded to the panel;
   the bank is the record. A `no bank` boot gets the summary only.
8. **A fresh image boots to the wizard.** Not this image: `Diag.codex` is
   its own payload with no desk and no identity, and it takes no input.

## Steps, each its own CL, none seed-affecting unless a stage touches the foreword

0. This design; the CurrentPlan Track A row; the lane rule (a metal
   question is a stage routed to red). DONE with this document.
1. **DONE 2026-08-18 (root); the record is the section below.** The ladder framework: `Diag.codex`, `DiagStage`/`DiagResult`/`DiagCtx`,
   the row and band painting, `DIAG.CFG` reading (stub `-Stdin` first, ESP
   file second), the `DIAG.ID` medium lock, `DIAG.TXT` bank with `END`,
   the verdict table printer, the fixed page (rows 0-3 and the band), and
   every channel in the table above wired. Two stages only, to prove the shape:
   `PciProbe` and `SceneProbe` lifted. Bed: codex-vm and OVMF, bank read
   back and diffed against the glass, and the read-only medium arm reaching
   `no bank` with the QR still decoding. Ships `diag.img` and `diag-arm.ps1`.
2. **Block ladder (6), sink ladder (7) and the NIC three (8-10) lifted 2026-08-18 (root); records below.** Lift the rest of the flown probes into stages in the ladder order:
   USB (xHCI truth, keyboard, MSC align), storage (block ladder, sink
   ladder), NIC (sitting, init, ring), and the day's-question stages (ASDE,
   the A8 allocation, the largest GOP mode). Each lift is its own CL and
   carries the stage's forced-failure arm. Owners: the lane that flew the
   probe lifts it (reek the sink ladder, blu the NIC three, fester the A8
   probe, red the rest); red merges the ladder.
3. **DONE 2026-08-18 (root); record below.** New passive readers: SMBIOS, EDID, the CPU feature row. And
   `check-diag-verdicts.ps1`: every state word has a verdict row.
4. **DONE 2026-08-18 (root); record below.** `flash-usb.ps1 -Rehearsed`, `DIAG.RCP` provenance, the UsersHandbook
   procedure, the release recipe carrying `diag.img` and its hash to the
   mirrors.
5. **The first grouped sitting.** `DIAG.CFG` for the ASUS carrying every
   standing question: A8 allocation, largest GOP mode and `SetMode`, the
   sink's 2.7 MB write, the e1000 ring, B3 with the desk box as peer, ASDE
   last; NIC-5 off. Rehearsed hash recorded, Damian sits once, `DIAG.TXT`
   transcribed to `HardwareSitting.md`, and each lane closes or advances its
   row from the bank.
6. Then the road: a stage that compiles a chapter off the stick (the A5
   ladder as a stage), a stage that writes a rebuilt kernel back, and the
   resident agent that chooses stages from what stage 1 found. Not
   scheduled; named so the ladder is built with it in mind (the ctx is the
   agent's memory, the verdict table is its first policy).

## Step 1, landed 2026-08-18 (root)

What ships: `build/boot/diag/Diag.codex` (the ladder), `DiagStage.codex`
(the stage shape and the colours), `DiagPci.codex` and `DiagScene.codex`
(the two lifted stages; `PciProbe.codex` and `SceneProbe.codex` now cite them
and keep only their one-question rendering), `build/boot/build-diag.ps1`
(the image: bundle, compile by the depot seed, `cdx-to-pe -ExitBootServices
-Stdin`, `build-img -Extra` with `DIAG.ID`), `build/boot/diag.img`, and
`build/boot/diag-arm.ps1` (seven arms, five in codex-vm and two under OVMF).
`build/quire-map.ps1` gained the `Diag` quire (`build\boot\diag`) so a stage
is `cites Diag chapter X`; `build/build-img.ps1` gained `-Extra
NAME.EXT=path;...` for files on the ESP root; `build/boot/test-ovmf.ps1`
gained `-ReadOnlyDisk`. Both generated scripts were changed through their
generators (0 drift).

**The stage shape as written, and where it differs from the sketch above.**
No record in the tree carries a function-typed field and the sketch's
`run : DiagCtx -> DiagResult` would have been the first, so the table is a
dispatch by stage number: `dg-stage-name`, `dg-stage-risk`,
`dg-stage-run` (a `when` over the id), `dg-stage-enabled` (the config),
`dg-stage-picture` (whether the stage draws in its slot). The sketch's
`applies` was dropped: a box without the part is a STATE the stage answers
(`no-nic`), never a silent skip; only the config skips. Stages are numbered
in ladder (risk) order, all passive ones first, and the first non-passive
lift extends `dg-stage-risk` rather than the run loop. A stage chapter is
named `Diag*.codex` because that is what `diag-arm.ps1`'s stale check
watches. A stage chapter
exports one function `<tag>-run : DiagCtx -> DiagResult` and its state
vocabulary. `DiagCtx` carries the geometry, the font, the world, the id
and kernel digest, the config lines, the PCI scan (taken once, before
stage 1, so no stage re-walks the bus) and the stage's own slot
(`dc-slot-x/y/w/h`); `DiagResult` is the state word, its colour, the glass
rows and the bank rows.

**Two channels the design assumed exist were measured not to, and each got
the honest substitute:**

- **`gfat` has no append.** Every `gfat-write-file` is a whole-file rewrite
  from a fresh chain, and the old chain is not freed. So the bank is
  REWRITTEN with every line so far after each stage, ending in `END` each
  time; the record still cannot be caught half-written, and a stage that
  wedges leaves the previous complete bank. The cost is one orphaned chain
  per rewrite, a few clusters a stage on a 16 MB stick; a `gfat-append`
  that extends the chain in place is the step-2 item that removes it.
- **`-Stdin` is where the id lives, not a compiled constant.** The payload
  cannot know its own hash, so `build-diag.ps1` hashes the compiled CDX,
  writes the prefix into the stub's serial ring as `id <hex>` beside
  `kernel <digest>`, and onto the ESP as `DIAG.ID`; the payload compares the
  two before its first write and refuses `no DIAG.ID`, `DIAG.ID mismatch`
  and `no id in the image` by name. Same bytes, same id, so a rebuild from
  identical source and seed still matches its stick. The ring is 120 bytes
  and the two lines take 42, which bounds `-StdinCfg`.

**Order, and one consequence for `DIAG.CFG`.** The passive stages run before
the bank opens because opening it means `usb-attach` (our xHCI and MSC
stack) and the medium lock, and stage 1 touches no device. So a `DIAG.CFG`
on the ESP can only select stages that run AFTER the bank; the stub ring
selects all of them. Both are read, the header row says which
(`src=stdin`, `stdin+file`, `default`) and the bank row says how many file
lines arrived (`cfg-file=N`). BANK 1 is a real write, not a mount: a
medium that mounts and locks but refuses the write is reported
`bank=none write refused, write stage N` (the `gfat` cell 83 code), never
`bank=ok`.

**The hold.** A bare self-call after the summary is a two-instruction loop
the runtime spine's watchdog reads as a hung guest; it panicked with a
`WD!` dump on serial about thirty seconds after `END`, in both beds. The
ladder holds by calling a real function each turn (pet mode pets on every
prologue) and paints a heartbeat square at the band's right edge, so a
photograph also says the machine is alive.

**The arms (`diag-arm.ps1`), all measured green 2026-08-18:**

| arm | bed | requires |
|---|---|---|
| `pass` | codex-vm, image as boot medium and disk | serial block `DIAG1`..`END` == `DIAG.TXT` row for row; `bank=ok`; both stages stated |
| `no-medium` | codex-vm, no `-disk` | summary reached, `bank=none ... mount stage`, no file |
| `fat-full` | codex-vm, every free cluster marked bad | mount and lock succeed, `bank=none write refused, write stage 14`, no file |
| `cfg-off` | second image, `scene off` in the ring | scene `state=skipped`, `bank=ok` |
| `esp-cfg` | second image, `DIAG.CFG` on the ESP | `cfg-file=1`, `bank=ok` |
| `ovmf` | QEMU+OVMF, qemu-xhci usb-storage | serial == file, `bank=ok`, the summary QR decodes off the screendump (`tools/qr-read.ps1`) to `DIAG1;...` |
| `ovmf-ro` | the same, drive `readonly=on` | `bank=none write refused`, QR still decodes |

Two things the arms taught. `-usb-bot-drop 1` (the sketch's read-only
control) is NOT a no-bank arm any more: the MSC driver's recovery path
re-issues the transfer and the bank lands, so the no-medium and fat-full
arms carry that role and the OVMF read-only drive is the honest
write-protect. And a read-only HOST file under codex-vm forces nothing
(the guest is served from memory; disk-arm.ps1 had already learned it).

**What the beds showed.** OVMF hands this stub a 2048x2048 GOP mode; the
page lays out at scale 2 and the QR block takes three codes at scale 6.
The pci stage answers `BELOW3G` under OVMF (NIC BAR0 at 0x81060000, AHCI
BAR5 at 0x81084000, as `build/boot/diag/README.md` records) and `ok` under
codex-vm, so the stage's worst-verdict rule is exercised in both directions
without a sabotage. Both beds reach END inside the arm deadlines (90 s
codex-vm, 100 s OVMF); the per-arm time was not measured finer than that.

**Not in step 1, by the design's own order.** SMBIOS/EDID/CPU rows (step 3:
`box: unnamed`, `ram=unread` are the placeholders and say so), the
verdict-vocabulary checker (step 3), `DIAG.RCP` on the ESP (step 4;
`build-output/diag-recipe.txt` carries it beside the image for now),
`flash-usb -Rehearsed` (step 4). Where the runner lives is still open:
codex-vm reaches `END` inside two seconds, so the five codex-vm arms are
about three minutes at their 30 s backstops and the two OVMF arms about four
more, which is a release-proof row rather than a battery row.
## Step 2, the storage lifts: block ladder and sink ladder, landed 2026-08-18 (root)

`build/boot/diag/DiagBlock.codex` is stage 6, `block`, risk `writes`, the
first non-passive stage, so it is also the first to exercise the run loop's
after-the-bank half: `dg-run-rest` starts at the first non-passive stage and
rewrites the bank after it. `DiagCtx` gained `dc-bank` (set when the bank
opened) because a write-side stage needs to know whether a medium is
selected at all; with none it answers `no-medium` and touches nothing. The
rungs are `BlockLadderProbe`'s in the ladder's own vocabulary: read the ESP
boot sector back through our driver on the bank's medium (`read-fail` on no
0x55AA), `bpb-bad`, write one marked sector at the scratch LBA 30000 (inside
the facts region, so nothing the volume reads is touched; `DIAG.CFG` `block
lba=N` moves it), `write-refused`, read it back (`readback-fail`,
`mark-lost`), `ok`. Bank row: `via= bps= lba= write= readback=`. Every word
has a verdict row (`check-diag-verdicts` 6 stages OK).

**Forced-failure arm:** `diag-arm.ps1 block-oob` builds the variant whose
`DIAG.CFG` says `block lba=999999999`; the medium refuses the write and the
row reads `state=write-refused`, every other stage as in `pass`. `no-medium`
and `fat-full` now assert `block=no-medium` (no bank, no medium). Eleven arms
green; the record's second line is this image (`EDD54A0E...`, kernel
`5B2DE4E6`). Measured on codex-vm: `via=USB bps=512 lba=30000 write=1
readback=1`; the OVMF pass arm banks the same row shape (`ovmf` compares
serial to file row for row, `block` included).

**The sink ladder followed the same day as stage 7 (`DiagSink.codex`, red's
reassignment from reek).** The 2.7 MB streamed write (`dsk-size` 2745998,
WORKS-9's number) goes through `gfat-write-file`, the bank's own writer, onto
the bank's medium as `SINK.CDX` (the stage re-mounts the ESP itself,
`mount-fail` if that refuses), then `gfat-file-size` and `gfat-read-file` read
it back whole and `dsk-bad` compares every byte against the pattern:
`write-refused` (row carries `wstage=`, the writer's stage cell, so a refusal
names where), `size-bad`, `read-fail`, `bad-bytes`, `ok`, `no-medium`. The
forced arm is the probe's own calibration, `DIAG.CFG` `sink shift=1`
(`sink-shift` in `diag-arm.ps1`): the write lands and the verify compares
against the pattern shifted by one, so the row must read `bad-bytes` with
`bad=2745998`, and every other stage stays as in `pass`. Measured on
codex-vm: `size=2745998 read=2745998 bad=0 shift=0 wstage=20`, ~30 s for the
whole pass arm; the 2.7 MB buffer plus the read-back copy sit inside the
payload's 128 MB arena with room. Twelve arms; the record's third line is
this image. `dg-stage-run` and the stage `-run` functions that touch a device
carry `[Device.Port]`, which the passive stages did not need.
## Step 2, the NIC three: nicsit, nicinit, nicring, landed 2026-08-18 (root; blu's probes, blu reviews)

Stages 8, 9 and 10 (`DiagNicSit`, `DiagNicInit`, `DiagNicRing`), the
mechanical lift of `NicSittingProbe` (NIC-1/NIC-2), `NicInitProbe` and
`NicRingProbe`; the NIC-3 init-and-frame tail of the sitting probe is the
init and ring stages. Three things changed shape on the way in:

- **codex-vm has no Intel card unless `-e1000`.** Every existing arm now
  asserts `nicsit=nicinit=nicring=no-part` (dim, not a fault, "nothing to do
  at your end"), and the NIC arms are `nic-pass` (`-e1000 -e1000-nat`: sit
  ok, init ok, ring `frames` because the NAT answers the ARP), `nic-nolink`
  (`-e1000-no-link`: init `no-link` with the step durations banked, ring
  `quiet`, sit ok) and `nic-nomac` (`-e1000-no-mac`: init `no-mac`). Each
  moves only its stage. OVMF has no NIC (gap 4 stands; the OVMF arms assert
  the three rows exist and read `no-part`).
- **Live progress lines are serial-only by design.** `nicinit` says
  `nicinit entering sN ...` on the wire before every step that can loop, so a
  hang is named by the last such line (the probe's L-STATES row); the bank
  cannot carry a line for a step that never returned. `diag-arm.ps1`'s
  `Compare-Rows` skips `^[a-z0-9]+ entering ` when it demands serial == file.
- **The link wait is budgeted.** `e1000-await-link mmio 0` is four million
  MMIO reads; under `-e1000-no-link` in the bed that is longer than the arm's
  deadline and on metal it is the four-minute spin the probe was written to
  tell from a hang. The stage uses `na-link-wait` (2 s HPET budget,
  `NicAsde`), so a no-link box reaches the summary and banks; measured s10 =
  2,000,068 us under the arm.

Measured on codex-vm with the card: `nicsit` poll 1,000,000 empty = 12,903 us
(bed figure 13,034), `nicinit` s3 settle-mdio 10,049 us, s4 quiesce 10,054 us,
s9 phy 91 us, s10 link 18 us, `nicring` init 21,397 us, ARP answered
(`received=1`), TX DD 1 (reader control passes), RDH writable, `rdh=1` after.
Fifteen arms; `dg-stage-run` and the run loop now carry `[Device.Port,
Device.Mmio, Console]`. `check-diag-verdicts` 10 stages OK.

**blu's review (same day), both taken:** `nicinit` and `nicring` now gate on
`e1000-bar-verdict` before touching MMIO (`bar-bad`, "the part was not
touched"), as `nicsit` already did; and `nicinit` says `entering s5 ring
alloc + zero` before the five allocations, so an allocation hang no longer
reads as s4.
## Step 3, landed 2026-08-18 (root)

**The stub carries the tables, because the payload runs after
ExitBootServices and the ConfigurationTable and the EDID protocol are only
valid before it.** `cdx-to-pe.ps1` (through `cdxtopeScript.codex`, 0 drift)
publishes handoff block VERSION 2, 200 bytes: the SMBIOS 3.0 entry the
ConfigurationTable names under `SMBIOS3_TABLE_GUID` at +0x30, the 2.x entry
under `SMBIOS_TABLE_GUID` at +0x38 (`ConfigTablePublish`, the `AcpiPublish`
walk parameterised by GUID and field), the EDID byte count at +0x40 and up to
128 bytes of EDID at +0x48 (`EdidPublish`: `LocateProtocol` for
`EFI_EDID_ACTIVE_PROTOCOL`, then `EFI_EDID_DISCOVERED_PROTOCOL` only if
active published nothing). Every new field is zeroed with the header, so a
reader of a v2 block reads "looked, found none" and `GopHandoff`'s new
readers (`handoff-smbios3`, `handoff-smbios2`, `handoff-edid-size`,
`handoff-edid-base`) answer zero on a v1 block rather than reading past it.
The SetMode path is byte-identical; the additions sit after `AcpiPublish`.

**Three passive stages, first in the ladder.** `smbios` (`DiagSmbios`:
entry point, structure walk with a 256-structure fuel and the table bounded
to the identity map; types 0/1/2/4/17 decoded, types 0/1/2/3/4/16/17/19
banked as `type/handle/len/strings`; states `ok`, `no-table`, `bad-anchor`,
`unmapped`, `no-system`); `edid` (`DiagEdid`: manufacturer, product,
name, native timing, size, version, checksum; the 128 bytes banked as four
hex rows; `ok`, `absent`, `short`, `bad-header`, `bad-checksum`); `cpu`
(`DiagCpu`: vendor, brand, family/model/stepping, a flag list, the
hypervisor bit, VMX with `IA32_FEATURE_CONTROL` read only behind
`GenuineIntel` and the VMX bit; `ok`, `hypervisor`, `vmx-locked-off`,
`no-brand`). Row 3 says `box: <manufacturer> <product> ... ram=<sum of type
17>` from the SMBIOS read the ctx takes once. Stages are now numbered
smbios, edid, cpu, pci, scene; a stage takes two rows (three when it draws)
and the scale drops to 1 when the page plus a row of codes would not fit
the panel at scale 2.

**`build/check-diag-verdicts.ps1`.** A stage chapter declares
`<tag>-states : List Text = [...]`; the checker requires a
`dg-verdict-<name>` with an `if s == "<word>" then` row for every word,
routed from `dg-verdict-stage`, and refuses a row for an undeclared word.
`build-diag.ps1` and `diag-arm.ps1` run it first. Its first run caught a real
mismatch (`pp-states` where the tag was `dpci`). Verdicts are now listed
worst first (red stages, then amber, then the rest, then the bank), so the
band's one sentence is the one that matters; `edid absent` and `pci ok` have
no sentence.

**The bed.** codex-vm's fake UEFI now publishes its (existing, legacy) SMBIOS
table in the ConfigurationTable with a 3.0 entry, gained a type 17 memory
device sized from `-mem`, answers `LocateProtocol` for both EDID GUIDs with
an EDID 1.4 block, and reports the hypervisor bit and a brand string
(`OperatorsManual.md` has the flags: `-no-smbios`, `-no-edid`, `-edid-bad`).
The first walk stopped at type 0 because the legacy string sets carried a
stray third NUL, which a spec walker reads as a zero-length structure; the
bed was wrong, not the walker, and it is fixed. `diag-arm.ps1` gained
`no-smbios`, `no-edid` and `edid-bad`; every arm now requires all five stage
rows and the pass arm the bed's own answers (`box=Codex Project Codex VM`,
`smbios ok`, `edid ok`, `cpu hypervisor`). Ten arms, all green 2026-08-18.
Under OVMF: SMBIOS 2.8 from QEMU (`QEMU Standard PC (Q35 + ICH9, 2009)`,
table at 0x7f587000), EDID `absent` (OVMF offers no EDID protocol, so the
`ok` path is proven only in codex-vm: L-GAP), cpu `hypervisor`
(`AuthenticAMD QEMU Virtual CPU`).

**Not exercised by any bed, said plainly:** `bad-anchor`, `unmapped`,
`no-system` (smbios), `short`, `bad-header` (edid), `vmx-locked-off`,
`no-brand` (cpu). Each has its verdict row; none has an arm.
## Step 4, landed 2026-08-18 (root)

**`DIAG.RCP` is inside the image and at the top of every bank.**
`build-diag.ps1` writes the recipe (id, kernel digest, payload and EFI
SHA-256, alloc pages, sectors, the stub ring text, the cfg file, the newest
`build/boot/diag/...` CL) onto the ESP as `DIAG.RCP` through `build-img
-Extra`, and `Diag.codex` reads it right after the bank opens (`dg-esp-rcp`,
the `DIAG.CFG` path with the same size caps) and banks and says each line as
`rcp key=value`, so serial and file still agree row for row and a `DIAG.TXT`
names the bytes that produced it. The image hash is NOT in the file, because
it cannot be inside the bytes it hashes, and there is deliberately no
timestamp: two builds of the same source and seed hash identically (measured
2026-08-18, `FD4D31DB...` twice), which is what closes gap 6 ("recipes no
longer reproduce flown hashes"). `build-output/diag-recipe.txt` beside the
image adds `image-sha256`, `kernel-path` and `built=`.

**The rehearsal record and the runner L-REHEARSE asked for.** `diag-arm.ps1`
appends `<sha256>  <utc>  arms=N  diag.img` to `build/boot/diag.rehearsed`
ONLY when it ran every arm in both beds and all agreed; `-Only` and
`-SkipOvmf` runs say in one line that the record was not touched.
`flash-usb.ps1 -Rehearsed` refuses an image whose SHA-256 is not on that
list (message names the rule and the fix), `-ExpectHash <sha>` additionally
pins the flight card's hash, and every run now prints the image hash. The
record ships in the depot beside the image, so the hash that flew is the
hash the tree records. First entry: `FD4D31DB3C31E7719F02797E65E35B41D195
2860E16AB2FE6F8618855B01AFC0`, ten arms, kernel `5B2DE4E6`.

**The stranger's procedure** is `UsersHandbook.md` "The diagnostic stick"
(download and verify, flash with `-ExpectHash`, boot, read the band, send
`DIAG.TXT` or the photograph); the fleet-side rule (`-Rehearsed`) is under
it. **The release recipe** (`.claude/skills/release/SKILL.md` step 5,
`PublicPush.md`) rebuilds `diag.img` against the release seed, rehearses it
fully, and ships the image, the `.rehearsed` record and the SHA-256 in the
GitHubUpdate report and README; `PublicPush.md` names the image as shipping
by design beside `kbd-diag-v16.img`.

**Not in step 4:** the bank carries one `diag-src-cl` for the directory, not
a CL per stage chapter, and the `DIAG.RCP` cap is 12 lines / 2 KB, which the
current nine-line recipe fits; a longer recipe truncates silently, the next
thing to tighten if the recipe grows.
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
