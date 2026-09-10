# The Diagnostic Stick -- one image that detects the box and says what needs to happen

*Owner: root for the composition; the ladder's stages belong to the lanes named on them.*

**THERE ARE NO SITTINGS (2026-09-09).** The last sitting flew on 2026-09-09
and stopped at the ladder's first write to the part; the record is
`docs/Hardware/HardwareSitting.md` "THE SITTING QUEUE IS CLOSED". This design
no longer composes flights. Its one remaining reader is the stranger below;
every stage is graded in the beds, and a stage only metal could grade is
deleted rather than kept for a flight.

## What it is for

One reader, one image.

**A stranger's box.** Someone downloads `diag.img` from the mirror, writes it
to a stick, boots a machine we have never seen, and gets three things: a
screen that says what worked and what did not, a file on the stick that
carries every reading, and a short instruction naming what to send us and
what to try. No wizard, no identity, no passphrase, no keyboard required, and
nothing written anywhere but the stick it booted from.

The far end of the same road is a resident agent on the stick that diagnoses
in firmware and rebuilds the kernel for the box it finds. That is the
direction. This design is the ladder that agent would climb; it is not the
agent.

## What already exists (do not rebuild it)

Measured 2026-08-18 from source.

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

Shared pieces every stage uses: `MetalLadder` (last colour standing),
`GopDraw.gop-draw-text-wrap` (rows), `GopHandoff` (framebuffer geometry from
the stub's magic-gated block), `GopFat16.gfat-mount-esp` / `gfat-write-file`
with `gfat-note-stage` (the bank and its `no bank, mount stage N` reading),
`GopShot.shot-window` (the RTC-counted bounded window, L-BANK), the two stub
liveness colours (dark blue at GOP, dark green after ExitBootServices), and
`cdx-to-pe.ps1`'s `-Ebs` / `-EntryStart` / `-HeapPages` / `-Stdin` arms.
Beds: `build/boot/test-ovmf.ps1`, `build/gop-mode-arm.ps1`, `ladder-arm.ps1`,
`sink-arm.ps1`, `disk-arm.ps1`, `test-conout-remode.ps1`, and codex-vm's fault
switches (`-e1000-no-link`, `-e1000-ctrl-ro`, `-usb-setcfg-fault`,
`-hid-root-silent`, `-uefi-conout-remode`, `-gop-max-mode`, `-uefi-strict`).
`build/boot/diag/README.md` is the account of the probes and stays the account
of each stage's readings.

## The shape

One payload, `build/boot/diag/Diag.codex`, one image, `build/boot/diag.img`.
The payload runs a fixed, ORDERED ladder of stages and never returns. Each
stage is one of the probes above, lifted into a stage shape:

```
  DiagStage
    name        : Text            -- what the row is called on the glass
    applies     : DiagCtx -> Bool -- run only if the box has the part
    run         : DiagCtx -> DiagResult
    risk        : DiagRisk        -- Passive | Touches | Writes | MayWedge
```

`DiagResult` is a small record: a state word from the stage's own L-STATES
vocabulary (never pass/fail; `KeyProof` has three, the MSC ladder has seven,
`PciProbe`'s map verdict has four), the raw readings as text rows, and the
bank lines. `DiagCtx` carries what the ladder has already learned (the
framebuffer, the PCI list, the ESP handle, the RTC) so a later stage never
re-discovers what an earlier one measured.

**Order is by risk, and risk is the whole design.** The ladder is:

1. **Passive, no device touched.** Firmware tables and geometry: GOP mode,
   stride, channel order (`SceneProbe`/`StrideProbe`), the memory map, ACPI
   (`GopAcpi`), SMBIOS and EDID, CPU features (VT-x, the `vmx` MSR read), the
   PCI walk with the BAR verdict (`PciProbe`). Everything here is a read of a
   table the firmware already built.
2. **BANK 1.** Mount the ESP of the stick we booted from and write `DIAG.TXT`
   with everything above. From here on every stage APPENDS to the bank before
   it runs the next; a wedge after a bank is a free finding (L-BANK).
3. **USB, read-side.** xHCI bring-up and the diag block (`XhciTruthProbe`),
   HID enumeration and the boot-report read (`KbdDiagProbe`), the MSC ladder
   read-only rungs, `MscAlignProbe`. Bank.
4. **Storage, write-side, on the stick only.** `BlockLadderProbe` (one sector
   at a known LBA past the volume) then `SinkLadderProbe` (the 2.7 MB streamed
   write, WORKS-9's question). Bank. **`sink` KEEPS THIS ROW BUT EXECUTES
   LAST** (root, 2026-08-21): it can kill the medium, and the medium is the
   bank, so every stage after it in the list would lose its record. Deferral
   is by `dg-stage-defers`, the main pass reserves the slot, and a labelled
   `before-deferred` summary is banked before the deferred stage runs. A stage
   that can END THE RUN rather than the medium (`asde`) is NOT deferred,
   because deferring it would guarantee it runs unbanked and the ASUS has no
   serial port.
5. **NIC, passive.** `e1000-find`, the pre-write register rows
   (`NicSittingProbe` NIC-1/2), the poll calibration. Bank.
6. **NIC, init and ring.** `NicInitProbe`'s stepwise `e1000-init` under HPET
   budgets, then the RX descriptor map (`NicRingProbe`, NIC-4's ring
   question). Bank.
7. **NIC, conversation.** B3: bring the stack up and hold one TCP conversation
   with a peer named in the config (or skip if none). Bank.
8. **NIC, the segment's lease.** `lease` (`DiagLease.codex`): NIC-6's
   question, asked on the driver `b3` bound. A DHCP lease is requested, the
   leased gateway is ARPed for, and its MAC is banked beside the hop the record
   channel resolved (`leased-gw-is-hop=same|differs`). Nothing is re-addressed;
   the channel and `b3` keep DIAG.CFG's address. Bank.
9. **The RTC's write.** `rtcw` (`DiagRtcW.codex`): WORKS-24's question. The
   seconds register is written thirty away from its reading inside a Status B
   SET window, read back, and restored the same way: `accepted` on a part that
   takes the write, `ignored` on codex-vm, which drops every CMOS write. Bank.
10. **NIC, the driver's own write read back.** `pchk1` (`DiagPchK1.codex`):
   PHY page 770 register 17 AFTER `e1000-init` has written it. The passive
   `pch` stage reads the same register at the top of the ladder, so its reading
   is the platform's power-up value; the K1 write happens inside `e1000-init`,
   which the ladder reaches only through `net-driver-bring-up` in `b3`. Without
   this stage a flight where traffic still did not flow cannot tell **a fix
   that was applied and did not help from a fix that was never applied at
   all**.
   **Its position is a constraint, not a preference.** It must follow the
   writer and precede `asde`: `asde` calls `na-phy-kick`, which writes BMCR
   reset, and a PHY reset returns 770.17 to its NVM value, so a reading taken
   after `asde` reports the NVM setting on every run and would say the write
   never took even when it took. A later ladder that brings `e1000-init` up
   earlier moves this stage with it, not to a fixed number.
   `pchk1` also listens 1.2 s on the production ring `b3` bound, through the
   driver's receive path, GPRC fenced before and counted after, DD counted on
   the ring, and says `quiet` / `arrived-visible` / `arrived-invisible` /
   `skipped` in its `listen-after-k1` row. Bed arms `k1-taken` (`-i219`) and
   `k1-blocked` (`-i219-mng-holds`) move the row from one binary; the pair with
   one armed frame released by pchk1's own GPRC read (`nicring off` in their
   cfg) reads `arrived-visible` and `arrived-invisible`, K1 the only
   difference. **Both arms name a peer**, because `b3` short-circuits on
   no-peer before bring-up: with no peer nothing writes K1. `not-taken` with
   MDIO answering is BOARD-ONLY and declared as such in the chapter; no bed
   knob lets the read succeed while the write fails.
11. **The day's questions.** Whatever a lane routed for this sitting, each a
   stage in its own file, run last among the risky ones: ASDE
   (`AsdeStageProbe`), the A8 allocation grant, the largest GOP mode and
   `SetMode`. Bank after each.
12. **`MayWedge`, never by default.** NIC-5 and anything else terminal by
   construction runs only when the config names it, and it is always the last
   line of the ladder.

Every stage paints its row BEFORE it runs (the row says `running`), then
overwrites it with the state word in the stage's colour, so the last row
standing names where the box stopped. The screen ends with a summary band:
stages run, stages skipped and why, the bank's path and byte count, and the
"what to send us" line.

**`DIAG.CFG` selects the ladder without a rebuild.** A text file on the
stick's ESP, one stage name per line with `on`, `off`, or a parameter (the
peer address for B3, the LBA for the block ladder). Absent, the default ladder
runs stages 1-7 with the stranger's defaults and skips 8-9. A sitting is a
`DIAG.CFG` red writes for the day; the image bytes do not change between
sittings, which is what makes L-REHEARSE affordable: the rehearsed image IS
the flown image, only the config differs, and the config is rehearsed too. Two
channels carry it: the stub's `-Stdin` ring, baked into the PE by
`build-diag.ps1 -StdinCfg` and read before any stage runs, and the ESP file,
read by `dg-esp-cfg` (`Diag.codex:1092`) only after `usb-attach` mounts the
medium, so the file can select stages 7 and later and the ring alone reaches
stages 1 to 6 (`dg-run-passive` at `:1088` runs on the pre-merge ctx). First
match wins, the bare key is `on`, only the exact word `off` disables
(`dg-stage-enabled`, `:144`). `gfat-text` (`GopFat16.codex:666-667`) drops any
byte CCE cannot represent, so a CRLF file reads the same as an LF one.

## The output channels, and why there are six of them (L-CHANNEL)

The stick has been unwritable, the framebuffer has been the wrong shape, and
the font has been unreadable, each on a boot whose probe reported on one
channel that was itself the thing under test. So the ladder reports on EVERY
channel that is alive, in this order, and a stage does not get to choose:

| # | channel | alive when | what it carries | fails how (seen) |
|---|---|---|---|---|
| 1 | firmware text (VGA text mode or UEFI `ConOut`) | before ExitBootServices, and after it only on a box that kept text mode | the first line: image hash, kernel digest, `-Ebs`/`-Uefi` world | `ConOut` re-mode changes the geometry under the stub (`-uefi-conout-remode` arm; `GeoTruth`) |
| 2 | screen colour (whole-panel fills) | as soon as GOP is acquired; needs no font, no stride | liveness (dark blue, dark green), then the last colour standing per stage | stride wrong paints a diagonal, still readable as a colour (`StrideProbe`) |
| 3 | GOP text rows | GOP plus a font that renders | the readings, one row per stage | stride 2048 versus 1920 on the ASUS sheared the rows; the font proof row is the check |
| 4 | QR on the glass | GOP, and a panel wide enough for the chosen scale | the summary and the readings, machine-readable off a photograph (`tools/qr-read.ps1`) | scale 2 decodes as nothing and looks like success; a fifth code truncates on 1280 wide |
| 5 | the bank file `DIAG.TXT` | the ESP mounts and the volume is writable | everything, appended per stage, `END` last | `no esp s1 m3 c4`; the seed medium lock refusing a seedless stick; the second write on metal (WORKS-9) |
| 6 | serial | a box with a port, or the bed | everything, streamed | codex-vm cannot screenshot a halting payload, serial is how the bed reads it |
| 7 | the peer's record file (`build/boot/echo-peer.ps1`, `echo-peer.record` beside its log) | the passive stages are done and DIAG.CFG names `b3 peer=... ip=<static>`: the driver is brought up, the hop resolved, and the passive record shipped BEFORE the first bank write (`DiagRecord`, root's ruling 2026-09-07); `ip=dhcp` opens it at b3 instead | the passive record at open, then every bank line live over a fresh connection per bank step; the bank row carries `record=peer opened ...` and the summary `record=peer shipped=N conns=K lost=L`; `b3 ... record=off` in DIAG.CFG suppresses the channel, `record=none off`, and `b3` then carries the whole banked record on its own first send | a stage that resets the part between two ships (nicinit, nicring, b3, asde) leaves receive off, so the ship brings the driver up again and dials once more, `bringup=y` on its serial line; a stick that dies keeps nothing and this file still holds every line banked before the box stopped talking. `nicsit` reads the part's power-on registers as the last passive stage, ahead of that bring-up. Bed arm: `b3-record` |

Channels 2, 3 and 4 need the camera. **The camera rig is a standing
instrument, not scaffolding**, and the design assumes it is there. A sitting
is composed on the premise that channel 5 may fail and the photograph is the
record. Conversely the bank does not wait for the glass: it is written first
after every stage, and the glass is repainted from what was banked, so a
photograph and the file never disagree.

**The page is FIXED so photographs compare across boots.** The diagnostic page
is one layout, and a stage may not draw outside its row:

```
  row 0   12345678 ABCDEFGH abcdefgh        <- font and stride proof, always
  row 1   [8 colour bars]                    <- channel order proof, always
  row 2   diag <hash8> kernel <digest8> world=EBS|UEFI cfg=<n stages>
  row 3   box: <SMBIOS product or "unnamed"> fb=<w>x<h> stride=<s> ram=<MB>
  rows 4..N   <stage> <state> <readings, wrapped, at most 3 rows>
  band    SUMMARY  run=<n> skip=<n> bank=<ok NNNN bytes | lost at=<stage> size=<n> | no bank, mount stage N>
  below   QR (summary), scale chosen 6/5/4/3, never 2
```

Rows 0-3 are painted before the first stage runs. A stage that cannot fit its
readings in three rows banks the rest and paints `+more in bank`. The colour
of a stage row is its state word's colour and nothing else is ever coloured,
so "what colour is row 7" is a question a photograph answers.

**`bank=ok` means every write so far was committed and read back at its own
size.** The bank's truth is the FILE: each write (`diag-bank-write-text`,
`DiagStage.codex:148-169`; the note writer `diag-note-bytes`, `:187-213`)
issues SYNCHRONIZE CACHE (`disk-sync-cache`, `GopDisk.codex:372`;
`msc-sync-cache`, `GopUsbMsc.codex:488-493`, opcode 0x35, whole medium, IMMED
clear) BETWEEN `gfat-write-file` and the `gfat-file-size` readback, so the size
read back from the directory entry is a statement about flash and not about
the device's write-back cache (WORKS-62). The flush answers three states: 1
flushed, 0 this medium has no flush primitive (neither a failure nor a
certification), -1 refused, which fails the write. A size that disagrees is a
refusal, and the first stage that loses an append is banked in cell 90 and
named in the row, on the serial as well as the glass. Bed arm: `bank-lost`,
which wedges the medium from the refusal onward rather than transiently. No
bed can exercise the flush: codex-vm's BOT model completes and commits in one
step.

**A REHEARSAL CERTIFIES AN IMAGE, so what counts as stale decides what the
record is worth.** `diag-arm.ps1` refuses an image older than any
`build/boot/diag/Diag*.codex`, and also one older than `seed/Codex.cdx`: the
seed moves on any merge-down, nothing rebuilds the image when it does, and a
rehearsal would otherwise certify an image the current compiler never built.
**Two holes are left and neither has a runner.** The scan covers only the
`Diag*.codex` chapters, not the 57 chapters the image BUNDLES, so a change to
`GopFat16` or `GopUsbMsc` leaves the image looking fresh; widening it to the
bundled set is a superset that can only over-refuse, which is the safe
direction. And all of this is mtime, which a `p4 sync` sets to the sync time
rather than the content's age, so it is a proxy for the question and not the
question. The recipe records the `kernel=` digest the image was compiled with,
which IS the question, but comparing it needs the current seed's digest and
that costs a compiler run.

**The bank path has a permanent runner so it cannot regress silently.**
`diag-arm.ps1` boots the exact image in codex-vm and under OVMF with a USB
stick image attached, reads `DIAG.TXT` back off the stick image
(`build/read-stick.ps1` shape) and requires it to end in `END` and to agree
with the serial transcript row for row; and it runs the same with the medium
made read-only (`-usb-bot-drop` and a write-protected image) and requires the
ladder to reach the summary band saying `no bank` while the QR still decodes.
That pair is the negative control. It runs before every flight:
`flash-usb.ps1` refuses by default an image whose hash is in no rehearsal
record, `-Rehearsed` is a retained no-op, and `-UnrehearsedAnyway` is the only
waiver (`flash-usb.ps1:17-21`, `:113-124`).

**`p4 edit` the three depot artifacts before rebuilding or rehearsing.**
`build/boot/diag.img`, `build/boot/diag.rehearsed` and `tools/codex-vm.exe`
are read-only until opened, and two of the three fail SILENTLY: `build-img`
cannot write the image and `build-diag.ps1` reports only "build-img failed"
with no cause, and `diag-arm.ps1` runs the whole ladder green and then cannot
record it, so the image stays unflashable after a ten-minute run.

The account of every channel failure so far is
`docs/Hardware/HardwareSitting.md`, and this section is the design reading of
it: no probe reports on one channel again.

## The bank, and the one rule it changes

The bank is `DIAG.TXT` on the ESP of the medium we booted from, appended after
every stage, plain ASCII, one `stage=... state=... ` line then the readings,
and a final `END` line so a truncated bank is visible as truncated. It is the
record; the glass and the QR are conveniences.

**A `DIAG.CFG` ON THE ESP CAN ONLY SELECT STAGES THAT RUN AFTER THE BANK, and
that is a property of the order rather than of the parser.** The passive
stages run before the bank opens, because opening it means `usb-attach` and
the medium lock, and a passive stage touches no device. The stub's `-Stdin`
ring is read first and selects ALL of them. Both sources are read, the header
row says which arrived (`src=stdin`, `stdin+file`, `default`) and the bank row
says how many file lines came with it.

`GopMedium` writes only to a volume whose ESP holds `CODEX.CDX`
(`GopMedium.codex:19`), which is why every seedless probe stick paints its F12
shots OFF and why only three of the flown probes have a bank at all. The rule
exists so a desk never writes to the wrong disk. The diagnostic keeps the
intent and changes the marker: **it writes only to a volume whose ESP holds
`DIAG.ID`, a file the image builder puts there, and the ID's content (the
image's own SHA-256 prefix) must match the payload's built-in constant.** A
stick that is not this image, or another disk with a stale `DIAG.ID` from a
previous image, is refused. That is a stricter lock than the seed marker, not
a looser one, and it lets the probe image carry no seed at all.

If the mount fails the ladder does not stop: it paints `no bank, mount stage
N`, switches to QR for the summary (bounded to what the panel can carry: the
scale is chosen, scale 2 is not offered), and continues, because a box whose
ESP we cannot mount is a finding worth every reading after it.

**A stage banks as it goes.** The ctx carries the bank (`dc-vol`) and the
lines banked so far (`dc-lines`, set by the runner before each stage), and
`DiagStage` owns the write path (`diag-bank-write-vol`, the file body, the
name buffer), so a stage can call `diag-bank-note c "stage=b3 step=reset"`: the
note reads the medium back, strips `END` and appends, and the ladder rewrites
the file again when the stage returns. A note therefore lives on the medium
exactly as long as the stage is between it and its result, which is the
interval a wedge freezes. Each note costs one file's length of heap (the
buffer is allocated before the mark by design), and every step note carries
`heap=N`, the bump pointer at that step, so the arena is read on the medium.
The read-back is a byte copy, never `gfat-text`, whose `acc &` per character
costs eleven megabytes of prefixes per 4.8 KB read.

**`b3` mirrors `net-driver-bring-up` rather than calling it**, at the
granularity of the driver's own functions, each painted and banked:
`db3-reset` runs the seven operations of `e1000-reset` in its order (imc,
ctrl-read, rst-write, await-reset, settle-mdio, imc, icr), and
`db3-init-after-reset` runs the six parts of `e1000-init-after-reset`
(`rings-quiesce`, `setup-rx`, `setup-tx`, `swflag`, `link-up`,
`swflag-release`). It rebuilds the `E1000Device` record verbatim, so a field
added to the driver refuses here at compile time rather than drifting; the
drift risk in the STEP ORDER is named here because no arm can measure it. The
semaphore and `e1000-link-up` sit inside `e1000-init-after-reset` and cannot
be separate steps from outside the driver; that half is blu's. `b3-pass`
requires twenty notes and refuses a run whose `banked=N` values do not
strictly grow, which is the shape of a note replacing its predecessor rather
than appending.

**A step paints its own refusal.** `db3-step` paints `BANK LOST AT <step>` in
`diag-col-bad` and says `b3 bank lost at <step>` on serial the moment
`diag-bank-note` answers -1 with a bank open (no medium at all is not loss and
paints nothing new). Because `dg-paint-result` overwrites a stage's slot when
the stage RETURNS, that paint survives only a wedge, so `db3-run` zeroes two
diagnostic cells at entry (metal RAM is not zeroed: cell 92 counts the stage's
notes, cell 93 holds the first refused one) and at its single exit stamps
`bank-lost-note=N` onto `b3`'s FIRST glass line and first bank line and turns
the row red, whatever its state. The first line is the one the slot keeps and
the QR is built from, so the ordinal reaches the photograph and the QR both.
An ordinal rather than a name on purpose: two step names carry live values and
the DHCP path adds a step, so a name table would drift. **Read the two
together**: the stick's trail ends at note N-1 and the glass says N, and a
medium that ACCEPTED a write and lost it (the one shape no in-band readback
can see, because the size readback goes through the same controller) shows as
a GAP between the two numbers.

**The clock is a control, banked before bring-up.** `hpet-ticks` is read
across 100000 reads and `clk=y/n dt=N moved=N/100000 hpet=N` is banked at
once; `clock-stuck` refuses before bring-up, because a rate nothing validates
over a counter that does not move makes every clocked wait in the driver
effectively endless (`e1000-await-link-clocked` is 100000 batches of 4096
STATUS reads). `-hpet-frozen` in codex-vm models the undecoded-window shape,
all-ones everywhere, period 0xFFFFFFFF deriving a bogus nonzero 232830 Hz; the
`b3-clockstuck` arm turns the three nic stages off by cfg so nothing ahead of
b3 spends its fuel on the same stuck clock, and asserts `clk=n` on the
refusing row.

## The report: what needs to happen

The last thing the ladder does is print, and bank, a verdict block that a
stranger can act on without us.

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

The table grows one row per new state; the discipline is that no state word
exists in a stage without a row here, checked by a script over the stage
vocabularies (`build/check-diag-verdicts.ps1`, step 3).

## What a stranger does

1. Download `diag.img` and its SHA-256 from the release. Verify the hash.
2. Write it to a USB stick: `build/flash-usb.ps1 -Image diag.img -DiskNumber N`
   on Windows, or a raw whole-device write on anything else
   (`dd if=diag.img of=/dev/sdX bs=4M conv=fsync`, the whole disk and not a
   partition). The image is under 16 MB and carries no seed and no identity.
3. Boot the box from the stick. Wait for the summary band (under two minutes
   on the default ladder; every stage has an HPET or RTC budget, and a stage
   that has no clock is spin-fuelled).
4. Read the last line. If it says `bank ok NNNN bytes`, put the stick back in a
   computer and send us `DIAG.TXT`. If it says `no bank`, photograph the screen
   and the QR codes and send those.
5. The verdict block tells you whether anything can be done at your end (cable,
   boot order, a different stick). Everything else is ours.

That procedure goes in `docs/UsersHandbook.md` beside the USB stick build
recipe, and in the release notes.

## What a lane does with a metal question

A lane never proposes a flight. It writes a stage: a chapter under
`build/boot/diag/` in the `DiagStage` shape, with its L-STATES vocabulary, its
verdict rows for the table above, its bed arm (the codex-vm switch or OVMF
configuration that forces each failure state, run by `diag-arm.ps1`), and the
expected readings on the ASUS written down BEFORE the boot. It routes one line
to red: stage name, CL, expected readings. Red adds it to the sitting's
`DIAG.CFG`, rehearses the exact image plus config in both beds, records the
image hash and the config in `HardwareSitting.md`'s flight card, and Damian
sits once. After the boot the readings are transcribed from `DIAG.TXT` into
the sitting doc, and the lane reads them there. The lane's own row in its
register is where its conclusion lives.

## Rehearsal, because a green bed says nothing about a hostile choice

`build/boot/diag-arm.ps1` runs the whole default ladder in codex-vm and under
OVMF and requires: every stage reaches a state, the bank exists and ends in
`END`, and each stage's forced-failure switch moves exactly that stage's state
and no other (L-FALSIF, L-INSTRUMENT). It also runs the resource envelope of
the flying image, not the bed's generosity (L-ARENA): the payload is built
with the same `-HeapPages` and no larger arena than the stick gets. The
rehearsed hash is the only hash that flies (L-REHEARSE): `diag-arm.ps1`
appends the image's SHA-256 to `build/boot/diag.rehearsed` only after every arm
in both beds (`diag-arm.ps1:1951-1969`). Where the bed cannot express a state
(OVMF has no `MAP=ok`, `CYAN` has never fired on either ladder), the design
says so in the stage's account rather than claiming coverage (L-GAP).

### A SITTING image is rehearsed against ITS OWN baseline, never a fixed one

The arms were written against the DEFAULT image, and a sitting bakes its
questions in: `sink ladder=1` moves the sink baseline from `ok` to
`ladder-all`; a baked `b3 peer=` moves `pass` from `no-peer` to `no-part`; and
with the ladder on, a refused small write lands as `rung-1-refused` rather than
`write-refused`. So `diag-arm.ps1` derives the subject baseline from the
image's own ESP (`diag-arm.ps1:477-507`): `read-stick.ps1` pulls `DIAG.RCP` and
`DIAG.CFG` off the bytes under test, the ring half comes from the recipe's
`stdin=` line and the file half from `DIAG.CFG`, and a subject whose ESP cannot
be read is refused rather than judged against a guess.
`build-output/diag-recipe.txt` is provenance only (the `kernel=` digest,
`:743-748`); it describes whatever was built last and never the image `-Img`
names.

Three scopes decide which baseline an arm gets:

- Only an arm booting the UNMODIFIED subject carries the subject config,
  because a `New-Variant` arm lays down its own `DIAG.CFG` (`:896-897`).
- The ESP config is read only AFTER the bank opens, so `no-medium` and
  `fat-full` run the default baseline however the image was built
  (`:906-907`).
- A stage turned OFF is two repairs rather than one, because the arms are not
  one population. A GENERAL arm does not care what the sitting composed: it
  reads the recipe's config the way `diag-cfg-find` does (first match, bare key
  is `on`, only the exact word `off` disables, ring before ESP) and expects
  `skipped` for every stage the subject turns off. An arm ABOUT the stage must
  not inherit that, since an asde arm that silently accepts asde being skipped
  is an instrument that cannot fail (L-FALSIF): `asde-differs` and
  `asde-ctrlro` are `New-Variant` arms laying down the subject's own
  composition with `asde on` forced ahead of it, so first-match takes the force
  and everything else stays as composed.

### The subject must not move under the run

Every arm copies the image fresh, so a `p4` sync or revert landing
mid-rehearsal swaps it for every arm after that point, and the verdict reads
as a defect rather than as an integrity failure. `Assert-Subject` re-hashes the
image before every arm and refuses, naming both hashes. The startup stale check
cannot see this: it runs once, before arm one.

### Landing a diag CL while main moves: rebuild after every unshelve

The stale guard compares `diag.img` against the chapters by mtime, and the
shelve/merge/unshelve dance rewrites every shelved chapter AFTER the image that
was built from them, so the guard refuses the rehearsal on the very first arm
even though the bytes are the same. The order that lands is: merge down,
unshelve, **rebuild the image** (deterministic, so an unchanged source
re-hashes to the same image and the record already carries it), rehearse only
when the bed or a chapter actually changed, then gate. Keep the window short:
rehearse and gate in one chain and copy up the moment it is green, because a
merge that brings `tools/codex-vm.c` means the exe has to be rebuilt from
merged source before the arms certify anything.

## What is still open

Everything else this design describes has landed, and the depot is the record
of it.

- **The next flight is the FLUSH arm of the sitting write-loss.** Sitting 14
  (2026-09-07) flew `AFC6AD65` flush-less on the sitting-13 cfg and the medium
  came back holding only `dg-open`'s probe write, orphaned without its
  directory entry, while the glass ran to asde with no bank-lost paint
  (`HardwareSitting.md`, "FLOWN 2026-09-07 (third)"). So the heap-restore and
  CRLF fixes are not the cause, and the flush image (blu, hash `9BC6D0E8`,
  carrying `SYNCHRONIZE CACHE`) flies next once rehearsed. A bank that survives
  there establishes the write-back-cache reading. No bed can run either arm.
- **`b3-record` and `b3-clockstuck` BOTH ANSWER CORRECTLY UNDER SUSTAINED
  LOAD** (fester, 2026-09-09), which closes the row that said no safe
  instrument existed. Flown against a scratch image built on the current depot
  seed (`build-output/diag-load.img`, `18449B36`, kernel `7745AD053BCEEFC6`)
  with `build/box-load.ps1` (design: `BatteryReorg.md` step 12) running beside
  them at a 2.5 GiB floor: **247 compile guests over 147.3 s at duty 0.95, peak
  3 concurrent slots, zero failures**, and both arms read `ok` against their
  expected verdicts. `-Only` leaves `build/boot/diag.rehearsed` alone, so
  nothing here certifies an image for flight.
  **What that does and does not say.** The arms are now proven against
  CONTENTION, sustained guest churn beside them, and NOT against SCARCITY: free
  memory never fell below 3.1 GiB, because the generator's floor exists to keep
  the subject from being starved. Starving them is what makes a reading
  unattributable in the first place, so no arm should be read below that floor.
  The BVT was never the instrument for either: `bvt.ps1 -Jobs 4` runs 28.4 s
  against a rehearsal of about 50 minutes and is gone long before b3, and a
  sustained loop of it takes free RAM to 733 MiB, where a starved guest and a
  slow one are the same colour on the row. (`b3-pass` and `nic-kills-msc`
  answer correctly under a sustained 4-guest BVT, `nic-kills-msc` giving
  `record=peer lost=0`.)
- **A cfg FILE cannot configure stages 1 to 6, and nothing says so.**
  `dg-run-passive` is called with the pre-merge ctx at `Diag.codex:1088`,
  `dg-esp-cfg` reads the ESP file at `:1092`, and the merge produces `c2` at
  `:1094`, so the passive prefix is dispatched before the file is in the ctx at
  all. The prefix is stages 1 to 6: the walk stops at the first stage that is
  not `diag-risk-passive`, which is 7 (`dg-stage-risk`, `:113`; `xhci` at 8 and
  `kbd` at 9 are passive by risk but run after the merge, so the file reaches
  them). The ORDER is inherent, because those stages run before `usb-attach`
  mounts the medium the file lives on. The defect is the silence: `build-diag`
  validates a `-Cfg` naming `scene` exactly as it validates one naming `xhci`,
  so a key that can never take effect is spelled identically to one that works
  (L-ACCEPTED). It wants a refusal naming the reachable stages and pointing at
  `-StdinCfg` for the rest.
- **`gop-mode-arm.ps1` asserts the wrong half, and one ladder arm asserts the
  right one.** Its six arms read `GOP: SetMode N` off codex-vm's stderr and
  check the BMP geometry, so they never see the bank: that script boots the
  seed as a UEFI payload, and nothing in it reports handoff v3. The bank IS
  asserted in one place, `diag-arm.ps1`'s `gop-kept`, which pins the whole row
  (`max=4 before=3 chose=3 flags=1 status=00000000`) predicted before the run.
  So the gap is shaped by STATE: of `dgop-state`'s six, only `kept` has its
  numbers checked, `honoured` is asserted as a word by every ordinary arm and
  as numbers by none, and `single` has no ladder arm at all, which is the hole
  `-gop-max-mode 1` falls through. red's row. The arm to add is `-gop-max-mode
  1` asserting `single` and a row opening `max=1`; only that much is derivable
  from the flag, and pinning `before`, `chose` and `flags` from a first run
  would pin whatever happened rather than what is required (L-FALSIF).
- **`DIAG.RCP` truncates silently past 12 lines or 2 KB.** The recipe fits
  today. This is the thing to tighten first if it grows.
- **The image hash is NOT reproducible across workspaces, and the release
  publishes that hash.** `DIAG.RCP` records the cfg as an ABSOLUTE PATH, so the
  same source and the same seed give a different image in every workspace.
  Measured 2026-09-08 with identical seed `D9CF240465C3D0BC` and identical id
  `f2e07084211bb472`: two workspaces produced `2D8FA5AE` and `A7AFF2FC`, each
  carrying its own path. Within one workspace the build IS deterministic: two
  consecutive rebuilds agreed byte for byte. What this costs is the claim a
  stranger relies on: the release notes give the SHA-256, and the release skill
  says the image is reproducible from its source and seed. It is not, unless
  the stranger clones to the same directory name. Recording the cfg by
  REPO-RELATIVE path would close it; `diag-src-cl` is workspace-local for the
  same reason. Unowned; nothing in flight depends on it, and the rehearsal
  record is keyed by hash, so a rebuilt image is correctly refused until
  rehearsed either way.
- **The road, not scheduled.** A stage that compiles a chapter off the stick, a
  stage that writes a rebuilt kernel back, and the resident agent that chooses
  stages from what stage 1 found. Named so the ladder is built with it in mind:
  the ctx is the agent's memory, the verdict table its first policy.

## Cost

The payload is one bare-metal CDX with no GC; each stage's allocations are
bounded by what the flown probe already allocated, and stages run in sequence
from one `DiagCtx`, so the high-water mark is the largest single stage (the
sink ladder's 2.7 MB buffer) plus the ctx, well inside the 32768-page arena the
A5 sticks fly with. Measured on the bed, `b3`'s bring-up steps cost a quarter
to half a megabyte each and its TCP `exchange` step costs 33.8 MB, a quarter of
the flight arena (L-ARENA); what it costs on metal is a bank row, and the
number is for whoever owns the net stack's heap rather than for this design.
Time is the sum of the stage budgets: every loop that waits on hardware carries
an HPET or RTC deadline (a count is not a duration), and the default ladder is
under two minutes by construction. Nothing is quadratic in the readings; the
bank is one rewrite per stage of every line so far (`gfat` has no append), so
its cost is linear in stages times bank size, not constant.

## Non-goals

Not the desk, not the wizard, not a shell. No write to any medium but the
booted stick's ESP, and only under `DIAG.ID`. No stage that can wedge the box
in the default ladder. No claim of hardware coverage the bed did not express:
the design says where the bed is silent.
