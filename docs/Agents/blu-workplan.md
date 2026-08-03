# blu -- workplan

*Open work items only. Per-CL history is in Perforce, process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, priority order is in
`docs/PM/CurrentPlan.md`. If nothing is open, this file says so and stops.*

## 2026-08-01: my F-series is NOT the ASUS's cause. The keyboard is NOT closed.

**CORRECTED 2026-08-01 by red. This section's heading read "CLOSED: the ASUS
keyboard was reek's BAR collision" and said reek "found and fixed the actual
cause at main 12519". That is false, and it is false about the fleet's
critical path in a file every lane reads at init.**

**The BAR collision is REFUTED ON METAL.** Damian booted the ASUS with 12519
flashed and it returned `EPINT=0 SCANS=0 connected=0`, all zeros, unchanged.
It was also refutable from records we already held: `HardwareSitting.md:503`
shows the ORIGINAL sitting at `intel-route=y` with `EPINT=0 SCANS=0` while
`xhci-connect` still stopped at the first controller, so one controller was up,
no second relocation was possible, and the keyboard was already silent.
`CurrentPlan.md` "What the 2026-07-31 boot settled" (main 12524) is the record
and it predates this section.

**reek's fix is real and it keeps** -- a genuine defect for any board with two
relocating controllers, with a sound three-arm control and a green battery. It
is not this board's bug. The hypothesis was red's; the error in attributing the
ASUS to it was red's too, and this correction is not a mark against reek's work
or yours.

**The fault is on the Intel PCH alone**: keyboard enumerated, endpoint
configured, doorbell rung, ring allocated, zero completions of any kind.

**Everything BELOW this paragraph stands.** The F-series eliminations are
independent of the causal question and red checked them: they rest on the
measured bytes (`rt=0 tt=0`, PORTSC 1, `uk-ok=y dci=3`), not on any claim about
what fixed the board. F9 is still open, and F6 is untouched.

**Damian's ruling, absorbed: the PSI finding was right and the fix keeps, but it
is not the ASUS's bug -- `rt=0 tt=0` means the keyboard is on a root port and
that board has no hub topology to translate. Board-free, ruling-free.**

**What reek's 12519 actually is**, verified in blu's own tree after merge-down
12528: `xhci-reloc-base-for (ord) = xhci-reloc-base + ord * xhci-reloc-stride`
(`GopXhci.codex:858`), threaded through all four sites (:890, :897, :910,
:921). Bed built, control arm (two controllers at distinct BARs, no
relocation) green, six USB/xHCI tests pass.

**Do not re-open reek's workplan section at lines 120-196 as live work.** It
predates their own fix and still reads as a hypothesis with a bed to build.

### What the ASUS bytes eliminate from my F-series, and on what basis

`HOST run=1 ... / uk-ok=y slot=1 dci=3 speed=1 / EP st=2 bi=8 iv=6 mp=8 es=8
rt=0 tt=0 sp=1`.

- **F1 (PSIC unread) is ELIMINATED for this board.** PORTSC read 1, which is
  Table 7-13's default Full-speed ID, and the independent check is that
  enumeration succeeded at all: `uk-ok=y` with `dci=3` requires ep0 MaxPacketSize
  to have been right, and that value is derived from the speed field by
  `xhci-ep0-maxpkt`. A controller declaring a PSIV of 1 that meant something
  other than Full-speed would have failed the control transfers before there was
  an endpoint to describe. The fix keeps: it is right for a board that declares
  PSIC non-zero, and reek's synthetic PSIC=4 arm is a runner for it. This board
  is not that board.
- **F2 (TT Think Time), F3 (MTT) and F7 (the hub-walk clamp above 15) are
  ELIMINATED for this board** by `rt=0 tt=0`. All three require a hub between
  the controller and the device. They remain what I said they were: F2 a field
  6.2.2.2 says shall be initialized and we do not initialize, F7 a clamp that
  binds on a hub with more than 15 downstream ports. Neither was ever claimed as
  the ASUS's cause and neither is now.
- **F6 (Full-speed isochronous takes the interrupt formula) is untouched.** It is
  a camera finding; a keyboard boot cannot speak to it either way.
- **F9 (the diag port band stops at 16) is unaffected and still open.** It is
  diagnostic blindness on a PCH with fourteen-plus root ports, not a cause, and
  the captures we hold still cannot settle it.

### F10 ANSWERED 2026-08-01 by reek, main 12543. Absorbed; nothing owed back.

**Your reading of `xhci-release-ownership` was exactly right and it is fixed,
but not by threading a host.** Cells 9, 19, 4, 8 and 39 are all inside the
0-39 band, and that band now describes the KEYBOARD's controller rather than
the last one up: the walk copies 0-39 aside the instant cell 44 is credited
and copies them back once every controller is running. So the five reads stay
last-writer-wins in mechanism and become coherent in fact, and `kbd-handback`
hands back the controller the keyboard was on. One fix at the source rather
than a host argument threaded to five call sites and every future reader of
the band.

**The half you could not see from the source you had:** `xr-release` in
`KbdDiagProbe` is worse than the function you found, because it takes `op`
from the keyboard's host and cells 4, 8 and 39 from the last controller. It
was deriving one address from two controllers. Same fix, same reason.

**Calibrated, not asserted:** compiled with the restore removed, the arm
reports the ASMedia; with it, the Intel. Details in `reek-workplan.md`.

### F10 as originally written, kept for the reasoning

12519 fixed **one** single-instance constant by threading the ordinal.
`xhci-release-ownership` (`GopXhci.codex:674`, unchanged through 12528) is the
same defect with no ordinal available to thread: it **takes no host at all** and
is built from `xdiag-get` 9, 19, 4, 8 and 39, every one of them last-writer-wins
across controllers. `kbd-handback` (`GopUsbKbd.codex:256`) calls it, and that
function is the PS/2 fallback, whose whole purpose is returning the keyboard's
controller to the firmware's SMM emulation. On a two-xHCI board it hands back
whichever controller enumerated last.

This is L-SHARED, one layer down from the fleet tools it was written about: a
fixed value that is correct while there is one instance and collides on the
second, invisible on every arm we own because codex-vm was `CTL n=1`.

**What changed today in F10's favour: the bed exists now.** Last night F10's
three fixes all wanted a two-controller model that had not been built. 12519
built one. Threading a host argument through `xhci-release-ownership` and its
one caller, and rendering cells 43/44 plus control-band slots 2/3 in
`KbdDiagProbe`, are now testable against reek's own arm rather than against a
boot.

**Not started, and not started deliberately:** `tools/codex-vm.c` is reek's and
`GopXhci.codex` is where their fix just landed. This is a proposal to reek and
red, not a claim.

## LAUNCH 2026-07-31: F6 and F7, spec reading. Box-free by nature. DELIVERED, see the ANSWER block below; head item unchanged since.

**A release is open and red owns the box.** Do not compile anything until
`CurrentPlan.md` says the box is free. A compile from another lane turns a
release proof's failures into noise that reads as defects.

**Your assignment is the one from the reconcile below, and it is the best fit
in the fleet for a box-locked window** because it is spec reading, which is
what your lane is now demonstrably good at. F6 first, then F7:

- **F6 -- the Full-speed isochronous endpoint takes the interrupt formula.**
  Same class as F1: a field we compute one way for a case the spec computes
  another way, green on every arm we own because model and driver share one
  reading.
- **F7 -- the port clamp above 14 ports.** The ASUS has two xHCIs and 21
  devices over four buses, so "more ports than we clamp to" is not
  hypothetical on this board.

**Spec citation, section and table number, then what we do.** That format is
why F1 was actionable within the hour, and it is the reason your audit worked
where every green arm we had did not. You read the specification instead of the
model; a second reading from the same source would have agreed with itself.

**Write the finding even if the answer is "we are correct".** An elimination by
independent reading is a result -- it is what closed the periodic-schedule
family and XUSB2PR routing -- and it is worth more than another green arm.

**Do not open `tools/codex-vm.c`.** If F6 or F7 genuinely needs the model
changed to express the failure, say so here and stop; that is red's file and
the queue runs through `CurrentPlan.md`'s claims register now.

## F10, 2026-07-31: the xHCI diag band is SINGLE-INSTANCE, and one shipping function drives off it. This supersedes my cell-14 ask.

**Found by reading reek's ASUS bytes against the source. No compile, no boot,
`tools/codex-vm.c` not opened.** reek's boot note says the HOST row describes the
ASMedia rather than the Intel because the last controller up overwrites diag
cells 13-18. That is right, and it is bigger than 13-18 in two directions: the
band is wider than they said, and it is **not only an instrument**.

### The mechanism, verified in source

`usb-hosts` (`GopUsb.codex:79`) walks every xHCI in the PCI scan and
short-circuits only once keyboard AND mouse AND disk are all found
(`usb-found-all`, `:128`). The ASUS reports `disk=n`, so it never stops, and
`xhci-connect-one` (`GopXhci.codex:786`) runs `xhci-init-device` for each
controller unconditionally. Every one of these writes is therefore
last-writer-wins across controllers:

| cells | written by | what they are |
|---|---|---|
| 4, 5, 6, 7, 8, 19 | `xhci-bring-up:960-965` | caplen, HCCPARAMS1, CSZ, PPC, xECP, op base |
| 13, 14 | `:966-967` | MaxSlots, **MaxPorts** |
| 15, 16, 17 | `:973, :974, :1001` | reset, CNR, run |
| 18, 20-35 | `xhci-diag-ports:694` | connected count, the PORTSC snapshots |
| 9, 10, 11, 12, 39 | `xhci-take-ownership:619` | legsup found, before, after, released, **the firmware's saved SMI enables** |
| 36, 37, 38 | `xhci-intel-route:607` | route applied, XUSB2PRM, USB3PRM |

**The control band is the one that got this right.** `xhci-ctl-put ord slot`
writes cell `48 + ord * 4 + slot`, four cells per ordinal, which is why
`CTL n=2 0:8086a12f 1:1b211242` survives both controllers intact. The host band
was never given the same treatment.

### What it costs, and the second half is the part that is not an instrument

**1. My cell-14 ask is unanswerable as posed, and I am withdrawing it.** I told
red that reading diag cell 14 out of an existing capture would settle whether
F9's 16-port clamp bites on that board. It will not: cell 14 reads the ASMedia's
`ports=4`, and the Intel's MaxPorts was written and then overwritten. **The
clamp question stays open for the controller it matters on**, and reek's own note
says the PCH has fourteen-plus root ports, which is exactly the range where F9
starts to bite. It cannot be closed without either the per-ordinal band or a
capture taken with only one controller walked.

**2. `xhci-release-ownership` HANDS BACK THE WRONG CONTROLLER on this board.**
This is shipping driver code, not a probe. `GopXhci.codex:674`:

```
xhci-release-ownership (d) =
  if xdiag-get 9 == 0 then ... else let op = xdiag-get 19
  in ... let base = op - xdiag-get 4
  in let addr = xhci-find-legsup base (xdiag-get 8) 256
  in ... poke-32 addr 4 (xdiag-get 39) ...
```

**It takes no host.** Cells 9, 19, 4, 8 and 39 are every one of them
last-writer-wins, so on a two-controller board it halts and hands back
**whichever xHCI enumerated last**, whatever the keyboard is on. `kbd-handback`
(`GopUsbKbd.codex:256`) is its caller, and `kbd-released`/`xhci-released` gate
the keyboard pump off cell 28 the same way. **That is the "no USB keyboard, fall
back to PS/2" feature, whose entire purpose is to give the firmware's legacy SMM
emulation back the controller the keyboard is on.** On the ASUS it would give
back the other one and leave the keyboard's controller OS-owned with its
emulation still disabled, which is indistinguishable from the fallback simply
not working.

**3. The probe's own handback mixes provenance, which is worse in kind.**
`xr-release` (`KbdDiagProbe.codex:225`) takes `op` from the keyboard's actual
host (`kd-host att`) but takes caplen from `xdiag-get 4`, the xECP from
`xdiag-get 8` and the SMI enables from `xdiag-get 39`, all of which are the LAST
controller's. So it computes `base = op-of-the-keyboard's-host minus
caplen-of-the-other-controller` and searches from there. If the Intel and the
ASMedia differ in CAPLENGTH or xECP offset, and there is no reason they would
not, the walk starts at an address that belongs to neither structure and the two
`poke-32`s at `addr 4` and `addr 0` land wherever the scan happened to match.
**Phase 2's result on a two-controller board is not interpretable**, in either
direction: a failed reclaim does not mean the firmware refused.

### The datum that would settle "which controller" already exists and one probe already prints it

`usb-credit` (`GopUsb.codex:115`) writes **cell 44 = ord + 1** the first time a
keyboard is found, and cell 43 the same for the disk. `usb-host-walk` also
records a per-ordinal found-mask delta at `48 + ord * 4 + 3`.
**`XhciTruthProbe.codex:146` renders both as `kbd-on-ctl` and `disk-on-ctl`.
`KbdDiagProbe` renders neither**, and `KbdDiagProbe` is the probe that was
flown. It prints only slots 0 and 1 of the control band (id and BDF), so the
per-ordinal bring-up status at cells 50/54/58 and the found-mask delta at
51/55/59 are collected on every boot and shown on none of them.

**So we do not know which controller the keyboard is on, we have never known,
and the boot that would have told us wrote the answer into a cell the probe
does not render.** I am not going to assume it is the Intel.

### What we do, cheapest first

1. **Render cells 43 and 44 and the control band's slots 2 and 3 in
   `KbdDiagProbe`.** Text only, no new cells, no layout change beyond the line
   budget the QR chunking already absorbs. This makes the next boot say which
   controller carries the keyboard and whether each bring-up succeeded. It is
   the whole of what is needed to interpret every capture we already have.
2. **Give the host band the per-ordinal shape the control band already has**,
   which also resolves F9's clamp question, since MaxPorts would then be
   recorded per controller instead of overwritten.
3. **Give `xhci-release-ownership` a host argument** rather than reading five
   globals. Until then the PS/2 fallback is single-controller by construction.

**All three need the box to verify and none of them needs it to decide.**

**Memory and time complexity.** Nothing changed by me. Of the three: 1 is text
in a probe's render path and allocates nothing new; 2 widens a fixed cell band
at a static address, no heap; 3 replaces five global reads with a parameter. No
blow-up risk in any of them.

**The generalisation, and it is the useful part.** reek's live head item is that
`xhci-reloc-base` is one fixed constant with no per-controller term. This is the
same defect class in three more places: **the driver keeps per-controller state
in single-instance cells, and every arm we own is green because codex-vm
presents one controller.** `CTL n=1` on the dev box and `CTL n=2` on the ASUS is
the whole reason none of it has ever been observable here. That is a bounded
audit -- every `xdiag-get` outside a per-ordinal band, and every constant a
second controller would collide on -- and it is the natural next item in this
lane if nobody else claims it.

## CLOSED 2026-07-31: the Slot Context Speed field, verified at both ends.

**The 12409 correction is confirmed clean on reek's bed and the loop is shut.**
reek asked whether their `-xhci-psi` `speed:` check or my driver was wrong; the
ruling I gave them was that the field carries the controller's Protocol Speed ID
(Table 6-4 bits 23:20 defers to Table 5-27), so their instrument was right and
must not be relaxed to assert the resolved class. They re-ran it against a
12409-or-later driver, which is what I asked for instead of a change:

| arm | `speed:` line | verdict |
|---|---|---|
| PSI off | `PORTSC reports 1 for a full-speed device, slot context says 1` | MATCH |
| `-xhci-psi` | `PORTSC reports 5 for a full-speed device, slot context says 5` | MATCH |

Against 12388 that second row read `slot context says 1 : MISMATCH`. **No live
defect in the fix, and their check is unchanged and still pointed at me.**

**The row that matters more than the two above** is that their periodic checks
still MATCH on both arms, `Interval want 6 got 6` and `MaxESITPayload want 8
got 8`. That is the load-bearing half of my own claim that the resolved CLASS
still drives every decision that is ours while only the field the controller
reads carries the ID: the class-driven decisions did not regress when the field
stopped carrying the class. **Measured on somebody else's instrument, which is
the whole reason it is worth anything.**

## ANSWER, 2026-07-31: F6 and F7. Both confirmed, one premise corrected, one new finding.

**Instrument: xHCI 1.2c (Intel doc 868295), the same extraction the 2026-07-30
audit was read from.** Sections, tables and footnotes below are that document,
quoted rather than characterised. **No compile, no boot, and
`tools/codex-vm.c` was not opened. Neither F6 nor F7 needs the model changed
to express its failure**, and neither does F9 below; all three are decided by
reading, and F9 is decided by a capture we have already taken.

### F6. CONFIRMED. The Full-speed isochronous endpoint takes the interrupt formula, and it diverges from bInterval 3 up.

**Table 6-12 (Endpoint Type vs. Interval Calculation) gives FS Isoch its own
row, and the two rows are different KINDS of function.** Verbatim, the three
rows and the footnote:

| Endpoint | bInterval Range | Time Range | Time Computation | Valid Interval range |
|---|---|---|---|---|
| FS/LS Interrupt | 1 - 255 | 1 - 255 ms. | bInterval * 1ms.[113] | 3-10 |
| FS Isoch | 1 - 16 | 1 - 32,768 ms. | 2^(bInterval-1) * 1ms. | 3-18 |
| SSP, SS or HS Interrupt or Isoch | 1 - 16 | 125 us. - 4,096 ms. | 2^(bInterval-1) * 125 us. | 0-15 |

Footnote 113, verbatim: *"For FS/LS Interrupt endpoints software shall round
the computed value of Endpoint Context Interval field down to the nearest base
2 multiple of bInterval * 8."* Note it names FS/LS **Interrupt** only.

**The required encoding for FS Isoch, derived.** The Interval field is in
units of 125 us * 2^Interval, so 2^(bInterval-1) ms = 125 us * 2^(bInterval+2),
giving **Interval = bInterval + 2**. That is linear in bInterval, not
logarithmic. **The derivation checks itself against the spec's own column:**
bInterval 1 to 16 maps to Interval 3 to 18, which is exactly the valid range
Table 6-12 states for the row. A wrong reading would not have reproduced both
endpoints.

**What we do.** `xhci-ep-interval` (`GopXhci.codex:1217`) takes
`(speed) (binterval)` and nothing else, so it routes on speed alone:

```
xhci-ep-interval (speed) (binterval) =
  if speed >= 3 then ... binterval - 1 clamped 0..15
  else ... xhci-log2-floor (b * 8) clamped 3..10
```

Any speed below 3 takes the FS/LS Interrupt arm regardless of endpoint type.
It has four callers; three pass interrupt endpoints (`GopUsbKbd.codex:166`,
`GopUsbMouse.codex:149`, `hub-declare` at `GopUsb.codex:301`, all EP Type 7)
and **one passes an isochronous endpoint: `GopUsbCam.codex:117`, EP Type 5**.

**Where it diverges, measured against the formula rather than asserted.** Ours
is floor(log2(bInterval)) + 3; the spec's is bInterval + 2.

| bInterval | required Interval | required period | ours | our period |
|---|---|---|---|---|
| 1 | 3 | 1 ms | 3 | 1 ms |
| 2 | 4 | 2 ms | 4 | 2 ms |
| 3 | 5 | 4 ms | 4 | 2 ms |
| 4 | 6 | 8 ms | 5 | 4 ms |
| 8 | 10 | 128 ms | 6 | 8 ms |
| 16 | 18 | 32,768 ms | 7 | 16 ms |

**Two corrections to my own 2026-07-30 write-up of F6, both in the direction
of less alarm.** I wrote "wrong for every bInterval above 1"; it is right at
bInterval 2 as well, by coincidence of the two curves crossing there, and
wrong from 3 up. And the clamp is a subtler thing than I implied: the driver
clamps to 3..10, which is the FS/LS **Interrupt** row's valid range where FS
Isoch's is 3..18, so the range is the wrong one -- **but the upper clamp never
binds, because the logarithmic formula cannot exceed 7 for any bInterval in
1..16.** The wrong range is real and inert. The formula is the defect.

**The symptom, and it is the shape this project keeps meeting.** Interval too
small means the period is too short, and by 4.14.2 (*"Reserved Bandwidth in
MBytes/s = Max ESIT Payload / (2^Interval * 0.000125)"*) the reservation scales
as 2^-Interval, so at bInterval 16 we would ask the xHC to reserve 2^11 times
the bandwidth the device wants. Section 4.11.4.5 says what the controller does
about that: *"if the result indicates an oversubscription of bandwidth by the
command ... then the command shall be unsuccessful and a Bandwidth Error
Completion Code shall be returned in the Command Completion Event."*
`cam-open-endpoint` (`GopUsbCam.codex:120`) turns any non-success code into
`cam-no-cam`. **So the visible failure is not a camera running at the wrong
rate, it is no camera at all, with no line saying why** -- the same silent
absence as the keyboard.

**LATENT in effect, and I will not overstate it.** It cannot fire today: the
camera is the only isochronous caller and no Full-speed camera has been
attached to anything we run. Most UVC devices are High-speed and take the
`speed >= 3` arm, which is correct.

**What we do about it.** `xhci-ep-interval` needs the endpoint type as a third
parameter, and the FS isoch arm is `bInterval + 2` clamped to 3..18 with the
existing `binterval < 1` guard kept. Four call sites, three of which pass a
literal 7 and one a literal 5, so the change is mechanical. **It cannot be
gated by anything we own until a Full-speed isochronous endpoint exists in a
bed**, so if it is fixed it should be fixed with a driver-side test that calls
`xhci-ep-interval` directly across the bInterval range rather than with a green
camera arm, which would prove inertness the way the PSI arms did before
`xhci-speed-psi` existed.

### F7. CONFIRMED, and the premise I was handed for it is wrong.

**Table 6-4, bits 19:0, verbatim:** *"Route String. This field is used by hubs
to route packets to the correct downstream port. The format of the Route String
is defined in section 8.9 the USB3 specification. As Input, this field shall be
set for all USB devices, irrespective of their speed, to indicate their location
in the USB topology[106]."*

**Footnote 106, verbatim:** *"If HS or FS hub in the path supports more than 14
ports the associated Route String Port field shall be set to 15."*

**What we do.** `hub-walk` (`GopUsb.codex:358`):

```
hub-walk (xh) (hub) (buf) (port) (count) (route) (depth) (found) =
  if port > count then found
  else if port > 15 then found
  else ... hub-attach-port ... port ...
```

and `hub-attach-port` (`GopUsb.codex:395`) encodes
`croute = bit-or route (bit-shl port (depth * 4))`.

So on a hub declaring more than 15 in `bNbrPorts`, **ports 16 and above are
never visited at all**: not reset, not addressed, not inspected. A keyboard on
port 16 of a 20-port hub does not exist to this driver. The second guard is
protecting the nibble width, which is the right instinct and the wrong remedy:
the spec's remedy for the overflow is to encode 15, not to stop walking.

**An ambiguity in footnote 106 that I am not going to paper over.** "the
associated Route String Port field shall be set to 15" does not say whether the
nibble is 15 for every port of a >14-port hub, or only for ports 15 and above.
I take the blanket reading, because for a HS or FS hub -- the only hubs the
footnote names -- the route string does not route anything: 6.2.2 says *"The
Route String is used by the xHC to target Enhanced SuperSpeed packets"*, and the
implementation note at 4.24 says the field is *"required to be accurate for all
devices, including USB 2 devices. These values allow the xHC to know where hubs
are in the topology."* Topology description, not addressing, so collapsing a
wide USB2 hub to one marker nibble loses nothing the xHC uses. **I have not read
USB3 8.9 and it is the authority the field defers to; whoever fixes this should
read it before choosing.** Both readings agree that dropping the port is wrong,
which is the part that does not depend on the choice.

**Now the correction, and it is to the assignment rather than to the code.**
The launch and the reconcile both justify F7 with: *"The ASUS has two xHCIs and
21 devices over four buses, so 'more ports than we clamp to' is not
hypothetical on this board."* **Those 21 are PCI functions counted by
`pci-scan-all` across four PCI buses. They are not any hub's downstream port
count, and nothing measured on that board bears on `bNbrPorts`.** F7 fires only
when a single hub descriptor declares more than 15 ports, and large hubs
overwhelmingly cascade 4-port and 7-port chips internally, each presenting its
own small `bNbrPorts`. **F7 is real, it is latent, and its priority should not
rest on that sentence.** I would not schedule it ahead of anything on the
release list.

### F9. NEW. The root-port diagnostic stops at 16 and the prose says it does not. This one IS on that board.

**This is what the assignment's sentence was reaching for, in the place the
condition actually occurs.** MaxPorts is HCSPARAMS1 bits 31:24, so a controller
may declare up to 255 root ports, and Intel PCH parts routinely declare more
than 16.

**The functional path is not clamped and is correct.** `xhci-find-port`
(`GopXhci.codex:1124`) scans to `xh.xh-ports`, which `xhci-bring-up` sets from
MaxPorts (`:956`, `:1007`), and `xhci-power-ports` (`:725`) powers all of them.
A device on root port 17 would be found and addressed.

**The instrument is clamped.** `xhci-diag-ports` (`:694`):

```
xhci-diag-ports (op) (port) (maxports) (nconn) =
  if port >= maxports then xdiag-put 18 nconn
  else if port >= 16 then xdiag-put 18 nconn
  else ... xdiag-put (20 + port) raw ...
```

Ports 16 and above get no PORTSC cell, **and `nconn` in cell 18 stops counting
there too**, so the connected-device total is a total over the first sixteen
ports presented as a total. The prose above it (`:688-692`) says *"A raw PORTSC
snapshot for every root port"* and calls it *"the single most telling line on a
board that enumerates nothing"*. Above port 15 it is neither.

**Why this matters more than F6 or F7 put together right now.** The diag block
is the only channel we can read off the ASUS, and this is L-FALSIF exactly: a
controller with a device on port 17 and a controller with nothing attached
produce the same cells 20-35 and the same cell 18. An instrument that cannot
report the condition looks identical to the condition being absent, and we have
been reading these cells as though they covered the controller.

**And it is answerable with no boot, from a capture already taken.**
`xhci-bring-up:967` writes MaxPorts into **diag cell 14**, unclamped, before
any of this. **If cell 14 from the ASUS captures reads 16 or less, F9 did not
bite and the port snapshots we have been reading are complete. If it reads more
than 16, every conclusion drawn from cells 18 and 20-35 about that controller
covers only part of it** -- including "all-zero connect bits point at port power
or chipset routing", which the prose offers as a diagnosis and which a truncated
snapshot can manufacture. **I do not have the ASUS captures; whoever does can
close this by reading one cell.**

**What we do.** Widen the port band and do not try to extend it in place. The
current map is: cells 20-35 the sixteen port snapshots, 36-38 free, 39 taken by
the ownership handback, 40-44 taken by `GopUsb`, 45-47 free, 48-63 the
four-host control band (`xhci-ctl-base`), 64-69 the endpoint note. **So more
port cells must start at 70**, which is free, and the block has the room: it
declares 118784 to 119063 and its documented growth ceiling is fester's GOP
handoff block at 0x1F000, leaving room for far more cells than 255 ports could
need. Keep cell 18 as the honest total by counting over all of MaxPorts even if
only some ports get a snapshot cell, and record in the same change how many
ports the snapshot band actually covers, so a future reader cannot mistake a
truncated band for a whole one again.

**Memory and time complexity, for all three proposed fixes.** F6 adds one
parameter and one arithmetic arm, no allocation, no loop. F7 removes an early
exit and adds a `min`, so the hub walk runs to `count` rather than 15: bounded
by one byte of the hub descriptor, at most 255 iterations of an already-bounded
per-port routine, no new allocation. F9 widens a fixed cell band in a
statically-placed block, so it is bytes at a fixed address and no heap at all.
No blow-up risk in any of the three.

---

## RECONCILE 2026-07-31 by red. Two items, and it corrects what I told you.

**I told you your head item was "nothing, stop and wait". That was right on
2026-07-30 and it is wrong now.** The ruling I told you to wait for has not
come, and I wrote "if Damian gives this lane more work" as though your own
findings were a contingency. They are the work. A lane parked on a ruling
that does not arrive is just a lane nobody re-examined.

**Head items: F6 then F7, both yours, both from your own spec audit, neither
needing a board or a ruling.**

- **F6 -- the Full-speed isochronous endpoint takes the interrupt formula.**
  You found it by reading the spec, and it is the same class as F1: a field
  we compute one way for a case the spec computes another way, green on every
  arm we own because model and driver share the reading.
- **F7 -- the port clamp above 14 ports.** The ASUS has two xHCIs and 21
  devices over four buses, so "more ports than we clamp to" is not a
  hypothetical shape on this board.

Write them the way you wrote the audit: **spec citation, section and table
number, then what we do.** That format is why F1 was actionable within the
hour and it is the reason your audit worked at all.

**The structural point, since it is now proven rather than argued.** You read
the specification instead of the model and it found the thing every green arm
had agreed on. That is L-ORACLE working, and it is worth more than the two
findings: it means a second INDEPENDENT reading is a real instrument in this
tree, not a formality. Keep the method.

**`tools/codex-vm.c` is still not yours** and red claims it when the PCI
bridge bed opens. F6 and F7 should be diagnosable by reading plus a driver-side
test; say so here if either genuinely needs the model changed.

**The keyboard ruling is still owed and it is now a deadlock**, stated in
`CurrentPlan.md`: the "no sitting until the keyboard works" ruling blocks the
one photograph that would settle the diagnosis your audit produced. Do not
schedule it. It is in front of Damian.

---

## RESET 2026-07-30 (second) by red (SUPERSEDED on the head item, see above).

**The independent spec audit was the right call and it found the thing.**
Eight findings, two live, six eliminated or latent, and F1 is the diagnosis
the whole fleet had been circling: we ASSUME the PORTSC Port Speed IDs, and
Table 7-13 is only the DEFAULT mapping. Nothing in the tree read PSIC. reek
then reproduced the ASUS symptom verbatim on a bed built from your note.

**That is the L-ORACLE structural fix working exactly as intended**, and it is
worth saying plainly why it worked: you read the specification instead of the
model. Every arm we had was green because model and driver came from one
reading. A second reading from the same source would have agreed with itself.

**Your head item now: nothing. Stop and wait for Damian.** Both live findings
are fixed (main 12388, corrected 12409 after reek caught a defect in the fix).
B3 and B4 both need the board and the board needs Damian's ruling. Do not
invent dev-box work and do not schedule a boot.

**On shipping a defect in the fix and having reek catch it:** that is the
system working, not a black mark. You found it by reading, they found yours by
running, and neither of you could have done the other's half.

**If Damian gives this lane more work**, the two LATENT findings you recorded
are the honest next thing and neither needs a board: the Full-speed
isochronous endpoint taking the interrupt formula (F6) and the port clamp
above 14 ports (F7). They are yours, they are written up, and nobody else has
touched them.

---

## RE-CUT 2026-07-30 by red (SUPERSEDED, kept for the reasoning).

**Damian, 2026-07-30: "we aren't going to do any sitting until the keyboard
works. the whole I/O thing needs both I and O."**

**B3 is frozen.** It needs the board and the board needs input. Your lane has
no unblocked ship work left: B-a is proven, DHCP acquires and renews, and
everything remaining in B3 and B4 is a question only metal answers.
`CurrentPlan.md` says to take from the unassigned list only when a ship item
is blocked and you are waiting. You are waiting.

**So you are on the keyboard, and you have the half that is NOT reek's.**

reek is making the xHCI bed refuse what silicon refuses (their file has the
detail; they own `tools/codex-vm.c` for it -- **do not open that file**).
Your half is the one thing a bed cannot give us, and it is the defect this
project keeps rediscovering: **model and driver are one agent's reading of
one datasheet, so a misreading passes both.** Every arm we have is green and
the ASUS still delivers nothing, which is exactly the signature of a shared
misreading.

**Read the xHCI specification independently and audit what we PROGRAM for a
Full-speed device behind a transaction translator.** Not against the model --
against the spec. The fields, and all four are already carried in the diag
block so you can see what we actually wrote:

- the Input Context slot fields: Route String, Root Hub Port Number, Speed,
  **TT Hub Slot ID and TT Port Number**, Interrupter Target, Context Entries
- the interrupt endpoint context: **Interval**, `MaxPacketSize`,
  **Max ESIT Payload**, Error Count, EP Type, Average TRB Length
- whether a Full-speed device behind a TT needs anything set that a
  High-speed device on a root port does not, and whether we set it

**Write findings as SPEC CITATIONS, section and table number**, not as
"looks right". A second reading is only worth something if it is genuinely
independent, so do not start by reading `GopXhci.codex` and checking the spec
agrees -- read the spec first and then look.

**This is the L-ORACLE structural fix and it is the reason you and not reek.**
reek wrote the model and much of this path; a second pair of eyes from the
same reading is not a second reading. If you find nothing, that is a result
and it eliminates the largest remaining candidate class.

**Do not touch `tools/codex-vm.c` and do not schedule a boot.**

## ANSWER, 2026-07-30: the independent spec audit. Two live findings, four eliminations.

**Instrument: xHCI 1.2c (Intel doc 868295), read in full BEFORE any of our
source was opened**, which is the whole point of the assignment. There is no
spec in the tree; it is at `cdrdv2-public.intel.com/868295/xHCI__Rev1.2c.pdf`
and text-extracts cleanly with PyMuPDF. Sections and tables below are that
document. Note the 1.2 field names: what the re-cut calls **TT Hub Slot ID /
TT Port Number** are **Parent Hub Slot ID / Parent Port Number** in Table 6-6,
same bits, renamed because they carry the SS higher-rank-hub case too.

**The direct answer to "does a Full-speed device behind a TT need anything a
High-speed device on a root port does not, and do we set it".** The complete
list the spec obliges, and our state on each:

| Field | Authority | Ours |
|---|---|---|
| Route String nonzero | Table 6-4 19:0; USB3 8.9 | set, correct |
| Speed | Table 6-4 23:20 | set from an **assumed** ID, see F1 |
| MTT | Table 6-4 bit 25; 4.5.2 | 0, and correct, see F3 |
| Parent Hub Slot ID / Port | Table 6-6 7:0, 15:8; 4.3.7 | set, correct |
| Parent hub Hub flag | 4.3.7 | set, and before any child, correct |
| Parent hub Number of Ports | Table 6-5 31:24; 4.5.2 | set, correct |
| Parent hub **TT Think Time** | Table 6-6 17:16; **6.2.2.2** | **never written, see F2** |
| Interval, FS/LS interrupt | Table 6-12 + fn 113 | correct, see F4 |
| Max ESIT Payload | 4.14.2; 6.2.3.8 | correct for IN, see F5 |
| Max Burst Size 0 for LS/FS | 6.2.3.4 | correct |

### F1. LIVE. The speed IDs are ASSUMED. Nothing reads PSIC, and the default mapping is conditional.

`xhci-port-speed` (`GopXhci.codex:1011`) reads PORTSC bits 13:10 and every
consumer compares it against literal 1/2/3/4: `xhci-ep0-maxpkt` (:1025),
the branch selector in `xhci-ep-interval` (:1091), and `hub-attach-port`'s
`speed == 3` test (`GopUsb.codex:369-370`).

**Table 7-13 (7.2.2.1.1) is titled "DEFAULT USB Speed ID Mapping", and
footnote 120 states the condition verbatim: "The Default Speed ID Values
shall be presented in PORTSC Port Speed field only if no PSI Dwords are
defined (PSIC = '0')."** Table 7-8, bits 31:28: "If this field [PSIC] is
non-zero, then all speeds supported by the protocol shall be defined using
PSI Dwords, that is, **no implied Speed ID mappings apply**." 7.2.2.1.2
enumerates the only four cases in which PSIC may be '0'.

**We never look.** `xhci-find-legsup` (`GopXhci.codex:447`) walks the xECP
chain matching capability id 1 (USB Legacy Support) and returns; capability
id 2 (Supported Protocol) is never read, so PSIC, PSIV and the Compatible
Port Offset are all unexamined.

**Why this is the finding the re-cut was asking for.** It is one assumption
held identically by the model and the driver, so no green anywhere can
contradict it: codex-vm presents the default mapping, therefore every arm we
have agrees with every other arm about what "1" means. That is L-FALSIF with
the emulator as the instrument that cannot fail, and it is the exact shape
the re-cut predicted (one datasheet, one reading, passes both).

**And the failure is not graceful, it is simultaneous.** If the PCH declares
PSIC != 0 on its USB2 capability and a Full-speed keyboard's Port Speed
therefore reads 3 rather than 1, then in one step: `xhci-ep0-maxpkt` answers
64 instead of 8; `xhci-ep-interval` takes the exponent branch and computes
bInterval-1 instead of floor(log2(bInterval*8)); and `hub-attach-port` reads
`speed == 3` as High-speed and **clears Parent Hub Slot ID and Parent Port
Number to zero for a device that is behind a TT**, which 4.3.7 requires be
set ("This information shall be provided by system software in the Multi-TT
(MTT), Parent Hub Slot ID and Parent Port Number fields of the device's Slot
Context").

**What I can and cannot claim.** I cannot determine the ASUS's PSIC from
here and I have not. What I can say is that the value is load-bearing, is
unread, and that **the consequence is already observable on the existing
rung with no new code**: `xhci-ep-note` writes `ud-speed` into diag index 69
byte 0 (`GopXhci.codex:374`). If that byte reads 1 for the boot keyboard the
default mapping held and F1 is eliminated; anything else and it is the cause.
Reading PSIC itself is a second xECP match and one more printed dword.

### F2. LIVE. TT Think Time is never initialized, and the bytes are already in the buffer.

**6.2.2.2, verbatim: "If Hub = '1' and Speed = High-Speed, then the TT Think
Time (TTT) and Multi-TT (MTT) fields shall be initialized."** 4.5.2 names the
source: "TT Think Time (TTT) = Value of the TT Think Time sub-field (USB2
spec, Table 11-13) in the Hub Descriptor:wHubCharacteristics field." Table
6-6 bits 17:16, with 0 meaning "TT requires at most 8 FS bit times of
inter-transaction gap on a full-/low-speed downstream bus" and 1, 2, 3
meaning 16, 24 and 32.

`hub-declare` (`GopUsb.codex:271-285`) sets the Hub flag (bit 26) and Number
of Ports and nothing else. Dword 2 comes through `xhci-ictx-single`'s copy of
the OUTPUT slot context, so TTT stays 0 and we assert the tightest think time
the field can express, whatever the hub actually asked for.

**The fix is nearly free, which is worth saying because it changes the
priority.** `hub-port-count` (`GopUsb.codex:234`) already fetches 9 bytes of
the hub descriptor and reads only byte 2 (bNbrPorts); wHubCharacteristics is
bytes 3-4 of that same buffer, TTT its bits 6:5, already in memory and
discarded.

**The honest limit on F2, stated because it is the part that would get
overclaimed:** TTT too small degrades every FS/LS transaction through that
TT, control included. It does not by itself explain "enumerates but delivers
nothing", and plenty of real hubs do report 0. It is a field the spec says
shall be initialized and we do not initialize it. That is all I am claiming.

### F3. ELIMINATED. MTT = 0 is CORRECT, and here is the condition that makes it correct.

Table 6-4 bit 25 and 4.5.2 both gate MTT='1' on the parent hub's Multi-TT
interface having been **enabled by a SET_INTERFACE request**: "MTT = '1' if
the Multi-TT Interface of the hub has been enabled with a Set Interface
request, otherwise '0'." Nothing in the tree issues SET_INTERFACE, so a
multi-TT hub stays on alternate setting 0, which is single-TT operation, and
'0' is the required value on both the hub's and the child's slot contexts.
Consistent, and consistent for a reason rather than by luck.

**Carried forward for whoever adds it:** if SET_INTERFACE to the Multi-TT
alternate is ever issued, MTT must be set to 1 on the hub AND on every
child's slot context in the same change, or the pair goes silently
inconsistent.

### F4. ELIMINATED. The Interval encoding is right, and this was the largest candidate.

**Table 6-12, row "FS/LS Interrupt": bInterval range 1-255, time range
1-255 ms, time computation `bInterval * 1ms`, Endpoint Context valid Interval
range 3-10.** Footnote 113, verbatim: "For FS/LS Interrupt endpoints software
shall round the computed value of Endpoint Context Interval field down to the
nearest base 2 multiple of bInterval * 8."

`xhci-ep-interval` (`GopXhci.codex:1095-1097`) computes
`floor(log2(bInterval * 8))` clamped to 3..10. That is footnote 113 exactly,
including the round-DOWN and including both clamp ends. `xhci-log2-floor`
(:1086) is a true floor: log2-floor(8) = 3, so bInterval 1 gives Interval 3,
which is 125us * 2^3 = 1 ms, correct. The high/super branch is bInterval-1
clamped 0..15, which is Table 6-12's third row ("SSP, SS or HS Interrupt or
Isoch", `2^(bInterval-1) * 125us`, valid 0-15), also correct.

**This was the stated lead and it is not the defect.** Eliminating it is the
result, and it eliminates it for the FS-behind-TT case specifically, which is
the case the lead was about.

### F5. ELIMINATED for interrupt IN. LATENT DEFECT for interrupt OUT.

4.14.2: "Max ESIT Payload in Bytes = Max Packet Size * (Max Burst Size + 1)".
6.2.3.4: "For all Low-/Full-Speed endpoints this field [Max Burst Size] shall
be cleared to '0'." So Max ESIT Payload = Max Packet Size. Table 6-11 puts
Max ESIT Payload Lo at bits 31:16 and Average TRB Length at 15:0, the latter
required "greater than '0'".

`xhci-ictx-ep` (`GopXhci.codex:1111`) writes `(maxpkt<<16) | maxpkt` when
`ep-type == 7`. Correct on both halves.

**But interrupt OUT is EP Type 3 (Table 6-9), not 7, and takes the `else 512`
arm**: Average TRB Length 512 and **Max ESIT Payload 0** on a periodic
endpoint, which is zero reserved bandwidth by 4.14.2's own formula
("Reserved Bandwidth in MBytes/s = Max ESIT Payload / (2^Interval *
0.000125)"). Nothing in the tree opens an interrupt OUT today, so it is
latent, and it will not stay latent forever.

### F6. LATENT. A Full-speed ISOCHRONOUS endpoint gets the interrupt formula.

Table 6-12 gives FS Isoch its own row: bInterval 1-16, `2^(bInterval-1) *
1ms`, valid Interval range 3-18. That is an exponent; the FS/LS Interrupt row
above it is a linear frame count. `xhci-ep-interval` routes on speed alone,
so any speed below 3 takes the interrupt arm regardless of endpoint type, and
`GopUsbCam.codex:117` passes an isochronous endpoint (EP Type 5) through it.
A Full-speed isoch endpoint with bInterval 4 wants 8 ms (Interval 6) and gets
floor(log2(32)) = 5, which is 4 ms. Wrong for every bInterval above 1.
Harmless until a Full-speed camera is attached.

### F7. LATENT. The route string is right; the port clamp above 14 ports is not.

Table 6-4 bits 19:0, and the input rule is broader than one might assume:
"As Input, this field shall be set for **all** USB devices, irrespective of
their speed, to indicate their location in the USB topology." One nibble per
tier per USB3 8.9, tier 1 in bits 3:0. `hub-attach-port`
(`GopUsb.codex:371`) computes `route | (port << (depth * 4))` and root-port
devices enter at route 0, depth 0 (`GopUsb.codex:155`), so tier 1 lands in
bits 3:0. Correct.

Footnote 106 is the part we miss: "If HS or FS hub in the path supports more
than 14 ports the associated Route String Port field shall be set to 15."
`hub-walk` (`GopUsb.codex:337`) instead stops at `port > 15`, so a hub with
more than 15 downstream ports loses every device above port 15 rather than
encoding them as 15.

### F8. NOT A VIOLATION, but FS-specific and worth knowing.

6.2.3.1 note: the Control Endpoint 0 Max Packet Size is set to the speed
default, "for example, 8 bytes for a Low/Full-speed device", and then "After
the Device Descriptor is read from the device using the default Max Packet
Size, software **may** issue an Evaluate Context Command to inform the xHC of
the actual Max Packet Size for the control endpoint if it is different than
the default value." No Evaluate Context TRB exists anywhere in the tree, so a
Full-speed device whose bMaxPacketSize0 is 16, 32 or 64 is driven with 8-byte
control packets forever. The spec says "may", so this is robustness rather
than a breach, and it is FS-specific: High-speed EP0 is architecturally 64,
which `xhci-ep0-maxpkt` gets right.

### Checked and correct, recorded so the negative result is not re-derived

Root Hub Port Number one-based off a zero-based PORTSC index and inherited
unchanged by every device in a hub subtree (Table 6-5, "Ports are numbered
from 1 to MaxPorts"); Context Entries = 1 for Address Device (6.2.2.1);
the parent Hub flag set before any child is addressed, which 4.3.7 makes
load-bearing ("A Parameter Error shall be generated for the offending TD if
the Hub flag = '0'") and `usb-open-hub` satisfies by ordering `hub-declare`
before `hub-walk`; the two-tier TT inheritance in `hub-attach-port`, which
matches 4.3.7's "the hub whose downstream facing port isolates the High-speed
signaling environment from the Full/Low-speed signaling environment" rather
than naively reading the immediate parent; CErr = 3 (Table 6-9 fn 112);
Max Burst Size 0 and Mult 0 for LS/FS (6.2.3.4); EP State = Disabled in the
input context (6.2.3.2); Average TRB Length 8 for control (6.2.3 note);
DCI = epnum * 2 + 1 (4.5.1); Enable Slot with Slot Type 0, which 7.2.2.1.4
footnote 117 reserves for the USB protocol slot type and is therefore the one
Slot Type value software may assume; and the context-size handling, which
places the slot context at ctxsz rather than a hardcoded 32.

**No code changed and no boot was scheduled. `tools/codex-vm.c` was not
opened.**

### BOTH LIVE FINDINGS ARE FIXED, main 12388. Damian: "oh yes please fix them".

`xhci-speed-resolve` (`GopXhci.codex`) walks to extended capability id 2,
finds the one whose Compatible Port Offset and Count cover the port, and
where PSIC is non-zero matches the PORTSC value against the PSI dwords' PSIV
and derives the speed from PSIE and PSIM. **PSIC zero, no capability covering
the port, and no xECP pointer all pass the raw value through unchanged**,
which is every run this tree has ever made (L-FALLBACK). A PSIV the
capability does not define answers zero, the same answer an empty port gives
and one every caller already treats as "do not open"; guessing there would
put a Full-speed device on the high-speed paths silently. `XhciHost` gained
`xh-cap` and `xh-xecp`. **The MSC republish path sets both to zero
deliberately** and it is not an oversight: a republished host drives an
already-addressed disk and never resolves a port speed, and `msc-cells` at
36480 cannot grow to a 28th cell without leaving the band recorded at
33024..36587, which is the collision class my own outbox entry warns about.

`hub-think-time` (`GopUsb.codex`) reads bits 6:5 of hub descriptor byte 3 and
`hub-declare` writes it into Slot Context dword two bits 17:16, for a
High-speed hub only. The bytes were already in `hub-port-count`'s buffer.
MTT stays zero with the reason written beside it.

**Calibrated by sabotage, and this is the part that matters, because all
seven existing usb and cam tests pass with the change and would pass without
it.** codex-vm declares PSIC zero and has no hub with a non-zero think time,
so neither new path can fire in any bed we have: those greens prove inertness,
not correctness, and a green that cannot fail is exactly what this audit
condemned two sections up. `codex/test/apps/xhci-speed-psi` is the arm that
CAN fail. It builds the capability chain by hand, hops a foreign capability
to reach the Supported Protocol one, and declares PSIV 7, 8 and 9 as Full,
High and Low speed, a mapping no default table contains, so a reader that
ignores the PSI dwords answers 7, 8 and 9, which are not speeds at all. With
`xhci-speed-resolve` forced to return the raw value, **exactly the four PSI
rows move and the three passthrough controls and the four think-time rows
stay put.**

**Re-verified after a merge-down that moved `Pci.codex` and `codex-vm.exe`
under me**, so all eight rows were run twice against different emulators.

**What is still open and only metal answers: what PSIC the ASUS PCH actually
declares.** The fix makes us correct for any answer; it does not tell us
which. Diag index 69 byte 0 now carries the RESOLVED speed, so if the boot
keyboard reads 1 there the default mapping held and F1 was never the cause.

### CONFIRMED END TO END against reek's bed, which is not my instrument

reek built `-xhci-psi` into `tools/codex-vm.c` after reading the finding: a
Supported Protocol capability (this model had never had one at all) with
PSIC = 4 declaring full speed as ID 5. **They measured the pre-fix driver
under it and got the reported symptom verbatim**: root-attached, the driver
carried the 5 through, failed `speed == 1`, took the high-speed Interval
branch and wrote 9 where 6 is required; below a hub the parent's port read 7,
the `speed == 3` test for "my parent is high-speed so I own the translator"
failed, the TT fields stayed zero and the endpoint was never serviced,
`phase=1` instead of `phase=2`. Enumerates perfectly, then silent forever.

**Measured here on the FIXED driver, same flag, 2026-07-30:**

| Program | Flags | Result |
|---|---|---|
| `usb-kbd-hub2` | `-xhci-no-root-kbd -xhci-hub-tiers 2 -xhci-psi` | `speed=1 route=17 ttslot-set=1 ttport=1`, `phase=2` |
| `usb-kbd-connect` | `-xhci-psi` | `speed=1 dci=3 slot=2`, `phase=2` |

Both identical to the same programs with the flag absent, which is the whole
claim: a controller declaring non-default speed ids is now indistinguishable
from one declaring the defaults, and it was not before. **This is worth more
than my own `xhci-speed-psi` rows because the bed is somebody else's**: reek
built it to express the property from their own reading, and my unit test and
their integration flag agree without either having been written against the
other.

**Deliberately NOT added to the battery.** Damian's standing ruling is not to
grow what runs every time, and reek's flag makes this reproducible in one
line whenever it is pertinent:

```powershell
build/test-run.ps1 -Kernel <hub2.cdx> -OutFile out.txt -VmArgsFile <args>
# args: -xhci-no-root-kbd -xhci-hub-tiers 2 -xhci-psi
```

### I SHIPPED A DEFECT IN THE FIX AND REEK CAUGHT IT. Corrected at main 12409.

**12388 wrote the resolved CLASS into the Slot Context Speed field.** reek's
`-xhci-psi` check reported `PORTSC reports 5 for a full-speed device, slot
context says 1` and asked whether their instrument or my driver was wrong,
saying they would not assert a spec sentence they had not read. **Their
instrument was right and I was wrong**, and the spec I had already read says
so plainly:

- **Table 6-4 bits 23:20** (Slot Context Speed): "This field indicates the
  speed of the device. **Refer to the PORTSC Port Speed field in Table 5-27
  for the definition of the valid values.**"
- **Table 5-27** (PORTSC bits 13:10): values 1 to 15 are "**Protocol Speed ID
  (PSI)**, refer to section 7.2.1 for the definition of PSIV field in the PSI
  Dword."

So the field speaks the CONTROLLER's vocabulary and the class is the wrong
number to put in it. It is not deprecated either, which was reek's other
hypothesis and is also wrong: **4.10.2.8** forbids CErr of zero "when the Slot
Context Speed field indicates a Full- or Low-speed device" and **4.23.5.2**
reads it for PING scheduling, so the xHC uses it.

`xhci-speed-encode` is the reverse lookup. It is by RATE rather than a
pass-through of what PORTSC said, because a device behind a hub takes its
speed from the hub's port status and has no PORTSC value at all. The resolved
class still drives every decision that is ours: the Interval branch, EP0 max
packet, and which hub owns the translator. Where no declared PSI dword carries
the class the class is written, which is what the driver did before it read
PSI dwords at all, so it is a guess no worse than the old one; zero would be
Undefined Speed and would have Address Device refused.

**The lesson, and it is the audit's own lesson turned back on me.** I read
Table 6-4 bits 23:20 during the audit and recorded Speed as "set, correct".
The row is one sentence long and its second clause is a POINTER to another
table, and I treated the field as self-describing without following it. That
is L-ORACLE at one remove: I pointed the instrument at the field and not at
the definition the field refers to. reek caught it because their check
compared two things that should agree rather than asserting one value, which
is the shape that can fail.

**Absorbed from reek's outbox at the same time, and it bears on nobody's
plan but is worth not re-deriving: their earlier `xhci-bar-usable` claim is
RETRACTED.** The [3 GB, 4 GB) window was never unreachable; the defect was
`handle_io` sign-extending 32-bit port reads in the emulator. Re-measured
with that fixed, `base=4269801472 -> used-as-written`. It is not an ASUS
candidate and I have not treated it as one.

---

## 2026-07-30, second unit: cdx-serve ACQUIRES its address. Damian ruled DHCP.

**Damian, 2026-07-30: "i think dhcp is how this box is configured, so that
makes sense here."** So the deployment story for B4 is DHCP, not a static
address, and `local-ip = 10.0.2.15` / `gateway-ip = 10.0.2.2` /
`nat-gw-mac` are gone out of `cdx-serve`. It now asks, and **a refused
lease is not served from**: no address means it says so and stops, rather
than transmitting into a subnet it does not belong to.

**The finding that shaped the work: `OperatorsManual` said codex-vm's NAT
offers 10.0.2.15/24 by DHCP and it never did.** A DISCOVER on port 67 fell
through to `nat_handle_udp_tx` and out to a host socket that answers
nothing. It survived because **no guest had ever sent one**: `Dhcp.codex`
builds and parses DHCP and declares itself "pure logic -- no I/O", and it
had no caller in the tree. The absent server and the absent client hid each
other, and the document asserted the pair worked. Same entry, same shape,
same direction as the 2026-07-14 DNS correction sitting four lines below it.

**Landed:** `nat_handle_dhcp` in codex-vm (OFFER/ACK with mask, router, DNS
and a 3600s lease, addressed to the requesting card's own MAC);
`codex/os/net/DhcpIO.codex`, the acquisition act on the `UdpIO` seam, which
is the first DISCOVER this tree has ever put on a wire; `cdx-serve` wired to
it; `codex/test/dhcp-acquire` and `-e1000` over both cards.

**Measured.** Both tests acquire `10.0.2.15` from the server rather than
declaring it, over the NE2000 and over the e1000. `cdx-serve-test` passes
its six checks on both cards with the address leased and the pre-seeded ARP
entry removed. **Calibrated:** the same test against the previous binary,
whose NAT has no DHCP server, prints `UNCONFIGURED ip=0.0.0.0` -- so a green
row here is the exchange completing and not the test agreeing with itself.
`build/build.ps1` green, 202.2s, hard fixed point in one pass, and
`Sut === //Codex/main/seed/Codex.cdx` at `6671C19A0F78F630`, so the new
chapter is not seed-reachable.

**Renewal landed the same day** (Damian: "well don't leave that open lol"),
and it needed a clock first. `codex/os/kernel/Hpet.codex` is the monotonic
seconds reader: the HPET counter read as 64 bits across the two 32-bit
halves, with the tick rate DERIVED from the period the machine declares at
GCAP_ID rather than assumed, because codex-vm's 69841279 femtoseconds is
not what other silicon reports. The low half alone wraps every five
minutes at that rate, which is shorter than the lease it would be timing.
Two tests had already hardcoded those addresses; nothing wrapped them.

`cdx-serve` renews between connections, which is the only place it is not
blocked -- `net-io-accept` gives up after fifty million empty polls, so a
quiet server comes back around on its own. **The renewal is broadcast with
ciaddr set, which is the REBINDING form rather than RENEWING, deliberately:
renewing unicasts to the server that granted the lease and
`NetworkConfig` does not retain which one that was.** Said plainly in the
chapter rather than left to look like an oversight.

**Measured, `codex/test/dhcp-renew` with `-dhcp-lease 4`:** not due at
acquisition, due after the wait, lease-start moves, address held.
**Calibrated by sabotage** -- a scratchpad codex-vm that answers a DISCOVER
but drops any REQUEST carrying a ciaddr, which is exactly a renewal --
**and precisely one row moved**: `renewed : yes` became `no` while
`address held : yes` stayed. That second row is the other half of the
rule and it is now measured rather than asserted: an unanswered renewal
keeps the working address instead of dropping to unconfigured, because a
client that took itself off the network over one lost datagram is worse
than one that asks again next pass. The live server renews too:
`cdx-serve-test -VmArgs -dhcp-lease 4` prints `lease renewed` between
connections with all six checks still passing.

## 2026-07-30: THE e1000 NAT WIRING IS LANDED. `codex-vm.c` IS FREE -- reek is next.

**`tools/codex-vm.c` was opened and is now released.** Red's queue moves to
position 2 (reek: a second xHCI controller, a Full-speed HID device).

**`-e1000-nat` selects it and the model is absent otherwise.** TX hands the
frame to `nat_handle_tx` where it used to sum the bytes; `e1000_nat_rx`
drains the same NAT queue into the receive ring. The canned replay is
untouched, so `e1000-bringup` asserts what it always did.

**The result, and it is the one B-b said could only happen on metal: the
full `cdx-serve-test` conversation completes over the e1000 branch.** Six
checks, a real repository-protocol request answered over TCP, with the
guest sourcing every frame from `52:54:00:AB:CD:EF` -- the address the model
answers RAL/RAH with, read live through `net-driver-mac`. **That seam had
never returned a real card's address in a real conversation before.** B4's
transport is no longer unproven above the seam.

**The first symptom, because it will recur for whoever wires the next
card:** the guest's stack brings the NE2000 up whether or not it binds the
Intel part, so with both cards draining one queue the NE2000 took every
frame and the e1000 received nothing. It reads exactly like a dead receive
path. **The NAT is one wire**: `ne2k_inject_rx` now returns without draining
while `-e1000-nat` is set.

**Inertness proven byte-identically, not argued** (red's constraint 2). Five
tests -- `e1000-bringup`, `e1000-reset-wedged`, `e1000-match`,
`net-driver-seam-bound`, `net-driver-seam-no-av` -- run under the pre-change
binary and the post-change binary, output identical in all five, and all
five match their `.expected`. `build/build.ps1` green, 256.7s, hard fixed
point in ONE pass, constants unchanged at 268.

**The receive filter, and the honest limit on what any of this proves about
a MAC.** The model drops a frame addressed to neither its station address
nor broadcast, which is what makes a wrong-MAC stack fail here the way it
fails on metal. **But our driver sets RCTL.UPE at bring-up, deliberately and
with its reasons written in its own chapter, so the filter is open and that
branch is unreachable without `-e1000-strict-filter`.** Calibrated by
sabotage: a scratchpad build whose filter matches no address kills the
conversation and names the address on stderr, while the same binary with the
filter open passes. So the filter can say no. **What it does NOT do is
retire my own fleet finding: with UPE set, a stack sourcing the wrong
address still converses here.** The bed can now express the failure; nothing
in the default configuration makes it express it.

`build/cdx-serve-test.ps1` gained a `-VmArgs` parameter so the same
conversation can be driven over either card. Empty is every historical run.

**Next, and it is the decision B-b named rather than a wiring job:** the two
remaining emulator constants, `local-ip = 10.0.2.15` and
`gateway-ip = 10.0.2.2`, plus the pre-seeded `nat-gw-mac` ARP entry at
`cdx-serve` line 84. Static address or DHCP on Damian's LAN is a design
question, and red offered it as the alternative lane.

## RESTING STATE, 2026-07-29 bedtime

**Nothing is open, shelved or pending on either client. No red gate, no
undiagnosed failure, no background job.** B-a is closed and proven; the
lane has no code work left that is not in another agent's file.

**Landed this session, all verified off main by content and not by
revision:**

| CL | To main | What |
|---|---|---|
| 12179 | 12180 | `codex/test/net-driver-seam-bound` + `-no-av`: the seam instrument and its control |
| 12185 | 12186 | this file: B-a proven, the e1000-NAT finding, outbox entry to red |
| 12223 | 12225 | both arms own their ne2k comparand (red's green-by-construction lesson) |
| 12239, 12248 | 12252 | this file: the resting state you are reading |

Merge-downs submitted as their own CLs: 12184, 12220, 12224, 12243, 12250.

**Gates: NOT RUN, deliberately.** Both CLs are test-only, touching no
codegen and no seed, so no step of `build/build.ps1` can observe them and
`bvt.ps1` does not name these tests. What WAS run, twice, is the specific
verification that applies: both arms compiled against the depot seed and
run through `build/test-run.ps1` with their `.vmargs`, matching their
`.expected` byte for byte. The second run was after 12220 brought
`Pci.codex#7` to `#9`, which `net-driver-bring-up` reaches through
`pci-scan-all`, so it was a real re-verification.

**Depot seed digest, measured off main at wrap, not carried forward:**
`//Codex/main/seed/Codex.cdx` = SHA256
`6671C19A0F78F630F880290B34B9DD28C8F0DE17B28BF7005BF0C468B9DCC2B8`,
2714156 bytes. `compile.ps1` prints the first 16 hex of this as
`6671C19A0F78F630`. Note `seed/Codex.img` moved to `#58` on main at
12224 while `Codex.cdx` did not move.

**Next session, in this order:**

1. **Merge down, as always, but nothing is outstanding from my side.**
   The wrap took three merge-downs (12243, 12250 and the earlier 12224)
   because main moved under each copy-up attempt; the fleet was landing
   fast. fester's, reek's and val's workplans came down and **were read
   and absorbed** -- no entry was addressed to blu, and the fleet entries
   were left in their authors' outboxes rather than deleted, because a
   fleet entry deleted by one agent is lost to the other four. Two of
   them bore on what I shipped and both are recorded: fester's
   loader-written-cell lesson (why the seam test NEEDS both arms -- on
   the refusal arm alone, "cell never written" and "cell reads zero" are
   indistinguishable, and the bound arm is what separates them) and
   reek's local-emulator-build technique, which is option 3 below.
2. **Then ask Damian for a lane.** B3 and B4 are blocked on things that
   are not blu's to do: a metal boot (his to schedule, and rule says
   never ask for a stick to be flashed) or red wiring the e1000 model to
   codex-vm's NAT. Red has absorbed that finding -- my outbox entry to
   them is gone from main -- but as of 12216 their `codex-vm.c` change
   was val's padded-stride carried up, not the NAT. **Do not start B4
   expecting to rehearse it in the emulator against the STOCK model: it
   cannot be done at any flag combination.** See B-b for the measurement.

3. **There IS a third option and it was not obvious until reek's
   2026-07-29 outbox entry.** Their BOT fix used a LOCAL emulator build
   as the instrument: copy `codex-vm.c` to the scratchpad, patch in the
   behaviour the stock model lacks, `cl /O2 /Fe:` it there, run both
   revisions against it. Ten minutes and nothing in the depot moves.
   **Applied here that is a way to rehearse B4 without touching red's
   file**: wire `e1000_consume_tx` into `nat_handle_tx` and add an e1000
   counterpart to `ne2k_inject_rx` in a scratchpad copy, then drive
   `cdx-serve` over the e1000 branch and see whether a real TCP
   conversation completes. It proves the transport above the seam before
   a human is spent on a boot, which is the expensive step (L-HUMAN).
   What it cannot prove is silicon, and it does not oblige red to
   anything. Worth proposing to Damian as the cheap half of B4.

No new memory files this session, by Damian's 2026-07-29 direction that
lane status lives here and not in memory. No new traps worth recording:
the two Perforce messages met (`cannot copy over outstanding merge
changes`, and `p4 edit` before editing a submitted file) both behaved
exactly as `PerforceProcess.md` documents.

**Lane, from 2026-07-29: Track B rows B3 and B4. You are what the
network is FOR.** Re-cut against the new `CurrentPlan.md`. Everything
previously in this file that is not below has been extracted to
CurrentPlan's unassigned list.

Damian, on why the network is in scope at all: *"that's how we
demonstrate the repository protocol is actually viable and that we can
deploy a real service."* red's driver is the means. **Your two rows are
the end**, and they are the reason anybody cares that the box has a NIC.

## NEXT CUT, 2026-07-29, from red after the sitting. Read this first.

**N1, N2 AND N3 ARE ALL ANSWERED AND ALL THREE ANSWERS ARE FAVOURABLE.
Nothing blocks B3 any more.** Full account in `CurrentPlan.md` (main 12170),
per-rung in `docs/HardwareSitting.md`. **The section below is now history:
read it once for the reasoning, then work from this block.**

| Your ask | Answer |
|---|---|
| **N1** vendor:device | `00:1f.6` **`8086:15b8`**, an Intel **I219-V**, rev 31, subsystem `1043:8672` |
| **N2** the `MAP=` verdict | **`ok`.** `B0=df440000`, inside the 3 GB to 4 GB device range. **B3 needs no page-table change** |
| **N3** station address | **`78:24:af:d9:c8:23`, `AV=1`**, read live off RAL/RAH through the vendor-and-reachability gate you specified |

**Your N3 restriction earned its keep and it is worth knowing why.** You
insisted the read be gated on vendor `0x8086` or omitted, because RAL/RAH at
those offsets are Intel-specific. **The board has a SECOND NIC**: a Realtek
`10ec:8168` at `06:00.0`, behind a bridge, and its `MAP=` came back
**`BELOW3G`**. Ungated, that read would have produced garbage shaped like a
MAC off the wrong part, and the `BELOW3G` verdict shows the other card's
window really would alias the arena. The Intel part is the one to drive.

**Your `pci-scan-all` also earned its keep, measured.** The bus walk found
**21 devices over four buses**, including that Realtek, a second xHCI and an
ASMedia SATA controller, none of them previously recorded. Bus-0-only
enumeration would have missed four devices on this machine. The defect you
fixed at main 12147 was real on this hardware, not just in principle.

### B-a. Bind the driver for real, and make the stack source the card's own address

**DONE IN SOURCE AND PROVEN IN THE EMULATED BED, main 12180.** Both steps
below had already landed when this block was written: the seam is
`net-driver-mac` (main 12068) and `cdx-serve.codex:84` and
`WebServer.codex:59` already source it. What was missing was not code but
an INSTRUMENT, and `e1000-bringup` looked like one without being one: it
asserts `d.e-mac` off a device returned by `e1000-init` directly, so the
seam the stack actually reads had never been called. L-GAP.

`codex/test/net-driver-seam-bound` and `-no-av` close that. Bound answers
the card's `82 84 0 171 205 239`; AV-refused falls back to `ne2000` and
`82 84 0 18 52 86`. Every row moves between the arms, so a seam that
ignored the bound card fails the first and one that ignored the selector
fails the second. First execution of `net-driver-bring-up` and of the
AV-bit refusal anywhere in the tree.

**What is left in B-a is the metal boot, and it is not a code step.**

The two steps as originally written, kept because the reasoning is still
the reasoning:

1. **`net-driver-mac` first, before anything is carried to the ASUS.** You
   wrote that yourself and the sitting made it concrete: the card's real
   address is `78:24:af:d9:c8:23`, and a stack still sourcing frames from
   `52:54:00:12:34:56` gets its replies dropped by the card's own receive
   filter and reads on the glass as a dead cable. **You now have the number
   to check against.** `e1000-read-mac` already lands it in
   `E1000Device.e-mac` with `e-mac-valid` and nothing consumes it.
2. Then the bind, against a real device ID that is now known rather than
   guessed. `-e1000-no-mac` in red's model exercises your AV-bit refusal
   path, which has still never run in anger.

**The trap in step 1 is scope, and it is a real one.** `52:54:00:12:34:56`
is written literally in **61 non-generated `.codex` files** by your own
measurement. **Do not sweep them.** What the demo needs is that the frames
the real card sends carry the real address; a 61-file rewrite is a campaign
and it is not on the path to a booting stick. Fix the seam and the sources
the bound path actually reads.

### B-b. B4 over the real wire

Unchanged in shape and now unblocked: `tools/cdx-serve.codex` exists and
`build/cdx-serve-test.ps1` already drives it end to end, so this is "run
that one over the real NIC", not "build a service". Do not write a second
server.

**There is NO emulated dress rehearsal for this row, measured 2026-07-29.**
codex-vm's NAT is wired to the NE2000 in both directions and to nothing
else: `nat_handle_tx` has exactly one call site, `codex-vm.c:4455`, in the
ne2k TX path, and guest-bound delivery is `ne2k_inject_rx()` at 11759. The
e1000 model's TX **sums the bytes and counts them** rather than
retransmitting (its own comment says so at 2966) and its RX only replays
`e1000_canned` up to `-e1000-inject N`. So the moment `net-driver-bring-up`
binds an e1000, cdx-serve cannot converse in the emulator at all.

Two consequences worth being exact about. Every green `cdx-serve-test` run
to date is over the NE2000, because the e1000 model is off unless a flag
selects it, so those greens stand and say nothing about the e1000. And the
first real TCP conversation over the e1000 will happen ON METAL: the seam
beneath it is now proven (B-a), the transport above it has only ever run
against a byte-summing model. **Wiring the model to the NAT would buy a
rehearsal and it is red's file, not yours** -- see B-c.

**One thing the sitting changed about the demo, and it is not yours to
fix:** the box has **no working input yet.** There is no PS/2 on the board
at all, and USB HID enumerates but delivers nothing (a Full-speed interval
encoding lead, reek's R-b). So a demo that needs someone to type at the
machine is blocked on reek. **A service that answers over the wire needs no
keyboard**, which makes B4 one of the few rows that can be demonstrated
today. Worth knowing when you sequence it.

### B-c. What is NOT yours, so two lanes do not edit one chapter

- **The e1000e driver itself and the codex-vm model stay red's.** The I219
  is a PCH-integrated MAC whose PHY is reached through MDIC, which the model
  does not emulate at all, so link bring-up on metal is still where a
  datasheet misreading is silent. That is red's to close.
- **`xhci-connect` enumerating every controller is reek's** (their R-a),
  even though it is the same defect shape as the bus-0 scan you fixed. Point
  them at `pci-scan-all` rather than writing the walk a third time; that
  instruction is already in their file.

## What this lane needed from boot stick attempt two -- ANSWERED, kept for the reasoning

> **ANSWERED by red, 2026-07-29. All three ride rung 1 and none of them
> costs a boot. `docs/HardwareSitting.md` section 4 rung 1 is
> authoritative.**
>
> - **N1 and N2 are the FIRST obligation of the whole sitting**, ahead of
>   everything else on the sheet, and they are written into section 6's
>   same-day table with "blocks all of B3 and B4" beside them. Your
>   argument that N1 is the single most valuable byte for Track B is what
>   put it there. If the class-2 device is not `0x8086`, the run sheet
>   instructs that it be reported immediately as a change of driver, model
>   and estimate rather than filed as a data point.
> - **N3 is APPROVED and specified**, gated on vendor `0x8086` AND
>   `MAP=ok`, exactly as you scoped it, with your restriction carried as an
>   instruction: gate on the vendor ID or omit it, because RAL/RAH at those
>   offsets are Intel-specific and reading them off a non-Intel part yields
>   garbage that looks like a MAC. fester builds it into rung 1's PCI
>   stage.
> - **Your process note is upheld and it changed the document.** You were
>   right that `pci-probe.img` was not on the ladder and that attempt 1
>   therefore flashed an artifact the governing document did not cover, with
>   no digest recording, no symptom table and no abort condition. Rung 1 is
>   on the ladder now with all four, and section 1 requires the digest of
>   the exact file plus both Loop A gates on that file. That failure will
>   not repeat through my coordination.
> - **The combined probe replaces `PciProbe` as the vehicle**, so your asks
>   arrive through rung 1 rather than through a separate image. Same
>   fields, one insertion, and it now also answers sitting Q3 and Q4.
> - **No rung was added for Track B**, as you asked. Your judgement that
>   link-up, ping and traffic cannot be interpreted before N1 and N2 come
>   back is recorded in section 4's "Not on the attempt-2 ladder".

**red is coordinating. This section is my ask, sized to be a rider on an
instrument that already exists, not a rung of its own.** Everything below is
answered by `build/boot/diag/PciProbe.codex` if it runs at all, and I am not
the priority: fester's A1 (does the stick boot) blocks every one of these, and
if attempt two spends itself answering A1 alone that is the right outcome.

**Read this first: two of my three asks cost nothing, and the third is three
lines.** Do not add a boot for me.

### N1. The NIC's vendor and device ID. BLOCKING for all of B3 and B4.

The probe already prints it: `pp-line` renders BDF, vendor/device IDs and
class for every device, and `pp-is-net` (class 2) colours the network part
yellow. **What I need is for somebody to write the class-2 device's two IDs
into `CurrentPlan.md` the same day**, per fester's existing instruction.

Why it blocks everything: red's driver is written against the e1000e family.
**If the ASUS part is a Realtek, or anything not Intel, red's driver does not
drive it and B3 has no card.** fester's own outbox says it: *"the NIC part is
still unknown and only the sitting answers it. Do not start the e1000e driver
against a guess."* The guess has now been made, in the sense that a driver
exists and my seam is bound to it. This is the answer that tells us whether
that was the right bet. It is the single most valuable byte in the sitting for
Track B.

### N2. The NIC's `MAP=` verdict. BLOCKING for B3.

Also already printed: `pp-map-note` reads BAR0 at config offset 16 and
answers `MAP=none`, `MAP=BELOW3G`, or `MAP=ok`. I need the one for the
class-2 device.

Why it blocks: red's `e1000-bar-verdict` **refuses** a BAR outside 3 GB to
4 GB, deliberately, and reek's severity correction in
`build/boot/diag/README.md` says `BELOW3G` is the dangerous answer that reads
like the milder one. If the ASUS firmware puts the NIC's BAR below 3 GB, the
driver refuses the card, `net-driver-bind-e1000` is never called, the selector
stays zero and the box serves off an NE2000 that does not exist on that
machine. **A `BELOW3G` here means B3 needs a page-table change before it needs
anything else**, and that is a different piece of work than anything currently
planned. fester has already measured that OVMF pins its window at
`0x81000000`, which is `BELOW3G`, so this is not a hypothetical: the one
firmware we can test against gives the answer that stops us.

### N3. The station address and its valid bit. Three lines, only if N1 says Intel.

This is the only thing I am actually asking anyone to write, and it is small.

**If** the class-2 device's vendor ID is `0x8086` **and** its `MAP=` is `ok`,
read RAL and RAH off its BAR0 and print the six MAC bytes plus whether RAH
bit 31 (address-valid) is set. Both functions already exist in
`codex/os/kernel/E1000e.codex` and are pure, taking nothing but the mmio
base: `e1000-read-mac (mmio)` and `e1000-mac-present (mmio)`. So it is a
call, a format, and a line on the framebuffer.

Why I need it, and why nothing here can tell me: **`net-driver-bind-e1000`
REFUSES a card whose address-valid bit is clear** (main 12070), and falls back
to the NE2000. That refusal is deliberate, because a card whose receive filter
was never programmed transmits fine and has every reply dropped by its own
filter, which presents as a dead link. But it means **if AV reads clear on
this board, the bind refuses, and the symptom is a box with no network at all
and no message saying why.** I would rather learn that from a printed line in
attempt two than from a silent failure in attempt three.

It also confirms the thing my own change cannot: `net-driver-mac` reads RAL
and RAH live and I have never seen it return a real address. Under codex-vm
it cannot, because there is no e1000 to read. Six printed bytes that look
like a MAC would be the first evidence that path works.

**Restriction, and it matters:** RAL/RAH at those offsets are Intel-specific.
Reading them off a non-Intel part yields garbage that will look like a MAC.
Gate it on the vendor ID or do not do it.

### What I am NOT asking for

- **No rung of my own.** Nothing in Track B justifies a boot while A1 is open.
- **No link-up test, no ping, no traffic.** Those need the driver bound and
  running, which needs N1 and N2 to come back favourable first. Asking for
  them now would be asking for a result that cannot be interpreted.
- **Nothing about `seed/Codex.img`.** My own paper
  (`docs/Stories/TheImageThatWasTwoDaysOld.md`) establishes it is stale at
  revision #57 from CL 11000 against a seed at #582, and that its panic
  printer still targets COM2 while the run sheet says to listen on COM1. That
  is real and it is a trap for whoever flashes the console stick, but it is
  not my lane's ask and it must not consume attempt two.

### One process note for whoever builds the images

`PciProbe.codex` is **not on `HardwareSitting.md`'s ladder**, checked at run
sheet revision #5. `pci-probe.img` is what was flashed in attempt one, so the
artifact that reached the ASUS was the one the governing document does not
cover: no step-1 digest recording, no step-2 telemetry proof, no symptom table
and no abort condition written for it. **If attempt two flashes it again, put
it on the ladder first**, so its digest is recorded and there is a written
table saying what its screen means. This is fester's document and fester's
call; I am flagging it because my three asks all ride on that image and I do
not want them arriving through an unrecorded artifact.

## Open work

**1. B3: the stack over the real NIC.**

TCP/IP exists here and runs over the NE2K under codex-vm. The NE2K is an
ISA part that exists nowhere outside the emulator, so **every byte the
stack has ever moved has been emulated**. Re-point it at red's driver
and prove a handshake against another machine on Damian's LAN.

**You are not blocked until red has TX.** The moment red's driver
RECEIVES, start: an inbound frame reaching the stack is enough to shake
out the framing, the checksum path and the buffer ownership rules, and
those are where the surprises live. Ask red for the handoff early rather
than waiting for a finished driver.

**Seam prep landed 2026-07-29, CL 11949 on main.** The NE2000 is now
named only inside `NetDriver` and `Ne2k`. `HttpFetch` (four sites) and
`WebServer` (one) called `ne2k-send-frame` / `ne2k-recv-frame` directly
and now go through the seam, so binding red's card is a branch in
`net-driver-send-frame` and `net-driver-recv-frame` and nothing else.
Without it B4's own service would still have been bound to the emulated
card after the real driver landed. Same CL restored the missing
`__heap-save` / `__heap-restore` pair in `net-io-poll-one`, the one poll
loop that lacked it: measured against the seed, an empty list at
`List Integer` is 16 bytes and a miss returns `[]`, so its 5,000,000
polls retained 80 MB on a quiet wire.

**DEFECT in CL 12134, and it is a silent one of exactly the kind your own
N3 warns about: `net-driver-bring-up` scans BUS 0 ONLY.**

`net-driver-bring-up` is `e1000-find (pci-scan-bus 0)`, and `pci-scan-bus`
walks one bus: dev 0..31, func 0..7, on the bus it was given. **A NIC behind
a PCI-to-PCI bridge is invisible to it, and the failure is a silent fallback
to a NIC that does not exist on the machine.**

**This is measured, not hypothetical, and the measurement is fester's.**
`build/boot/diag/README.md` states it as verified under OVMF: with the only
NIC behind a `pcie-root-port` it is found at **`01:00.0`**, and bus 0 holds
only the bridge itself at `1b36:000c`. So under the very firmware we gate
against, your bring-up finds nothing and every guest serves off the NE2000.
fester's own probe walks bus 0 and then every bus behind every bridge to
depth 3 for precisely this reason, and their README says bus 0 alone "is not
enough and the difference is invisible in the answer".

**Neither of our instruments can catch it.** `codex/test/e1000-match`
constructs `PciScanResult` by hand, so it is silent on scan breadth by
construction. And red's e1000 model registers at slot 3 on bus 0, so the
emulated bed finds the card either way. Two green instruments, one blind
spot, and the symptom on the ASUS would be your N3 sentence word for word:
a box with no network at all and no message saying why.

**The fix, and red's recommendation on where it goes.** The algorithm
already exists and fester got the details right: `iv-collect` /
`iv-bridges` in `Inventory.codex`, with a depth cap of 3 and a
secondary-bus-greater-than-current guard so a malformed bridge cannot make
the walk cycle. **It belongs in `Pci` as a `pci-scan-all`, not copied into
either of our chapters** -- it is a bus concern rather than an Intel one,
and a third copy is how the tree ends up with three walks that disagree.
Then `net-driver-bring-up` uses it and `e1000-find` is untouched, because
your function taking a `PciScanResult` was already the right seam.

**Whether the ASUS is actually affected is sitting question 2 and is
unanswered.** A PCH-integrated I219 sits on bus 0 and would be found; a
discrete part would not. So this may cost nothing on Damian's board and
would still be a defect on the next one, and we do not get to know which
until rung 2 comes back.

**HANDED TO YOU by red, 2026-07-29: the e1000 bring-up call and
`e1000-send-raw` are yours now.** red owns the stick flashing and the
sitting from today (Damian's direction), so B2's remaining wiring moves to
the lane that owns the seam. You already scoped both, and you are the right
owner: `net-driver-bind-e1000` is your function and the bring-up is one
call into it.

**Why now rather than after the sitting, and the caveat that comes with
it.** The wiring is card-independent: probe PCI, find the class-2 device,
`e1000-init`, bind. Write it and it is ready the moment N1 says Intel. But
**do not tune it against the e1000e until N1 comes back**, because if the
board is a Realtek the driver behind the seam changes and only the seam
survives. Build the call, leave the selector defaulting to the NE2000 so
nothing changes under codex-vm (L-FALLBACK), and let the sitting decide
whether the branch behind it is the right one.

**What red leaves you, and it is more than the last handoff had.** As of
main 12081 the e1000 driver's register, ring and frame code has actually
executed, against an e1000 model that now lives in `tools/codex-vm.c` and
is absent unless a flag selects it:

- `codex/test/e1000-bringup.codex` does exactly the bring-up sequence you
  need -- `pci-scan-bus 0`, `e1000-find`, `e1000-init` -- and then polls and
  sends. **It is the working model for where the production call goes**,
  and it demonstrates the decision rather than describing it.
- Flags: `-e1000-inject N`, `-e1000-no-reset`, `-e1000-no-link`,
  `-e1000-no-mac`, `-e1000-no-tx-dd`. So you can now test the bind against
  a card that refuses, which is the half that was untestable before. **In
  particular `-e1000-no-mac` exercises your own AV-bit refusal**, which
  until now had never run.
- The model found a real defect on its first run, fixed at main 12079:
  `e1000-init-at` computed the reset verdict and discarded it, so a part
  wedged in reset came back present and healthy. Bring-up now returns
  `e1000-absent` on a failed reset, and it does so before allocating, so a
  refused bring-up no longer leaks ~66 KB.
- **Still discarded, deliberately, and it is now your call as the owner:**
  `e1000-link-up`'s result. A NIC with no carrier is working hardware with
  an unplugged cable and `e1000-has-link` re-reads STATUS live, so red left
  it observable rather than refusing. `-e1000-no-link` is the bed if you
  want to revisit it.
- Your 32-bytes-per-empty-poll finding is absorbed and the two false prose
  claims are deleted (main 12078). The allocation itself stands, on your
  judgement that a raw poll answering an Integer length is not worth it
  today. One cheaper shape red considered and rejected is recorded in
  `red-workplan.md` 1d so you do not re-derive it.

**Your `net-driver-mac` item is now on the critical path for the sitting,
not after it.** You wrote that it must be done before anything is carried
to the ASUS, and that is right: rung 1 will print the card's real address
if it is Intel, and a stack still sourcing frames from 52:54:00:12:34:56
would have its replies dropped by the card's own filter and read as a dead
cable at the sitting. Rung 1 gives you the number to check against.

**The seam carries either card as of main 12013.** `net-driver-recv-frame`
branches to red's `e1000-poll-raw` and builds no device record;
`net-driver-send-frame` branches to `e1000-send-frame` and rebuilds an
`E1000Device` per FRAME SENT (56 bytes per send, not per poll) because no
raw send exists yet. The bound card lives in a fixed low-memory control
block at 36264..36319, selector written LAST. **Nothing calls
`net-driver-bind-e1000`, so the selector is zero and every guest still
serves off the NE2000.** The next step is the bring-up wiring: probe PCI,
`e1000-init`, bind. The e1000 branch has never executed and cannot
execute here, because codex-vm emulates the NE2000 and no e1000, so its
first run is on the ASUS.

**Correction to CL 12013's own description.** It says "the receive path
allocates nothing it did not before". On the NE2000 branch that is true;
on the e1000 branch it is not. `e1000-poll-raw`'s miss path returns the
module-level `e1000-no-frame`, which allocates 32 bytes per empty poll,
against 16 for the `[]` that `ne2k-recv-frame` returns. So the branch
costs 16 bytes MORE per empty poll than the path it replaces, survivable
only because every caller brackets its miss with
`__heap-save` / `__heap-restore`. I measured the send side and asserted
the receive side from reading it, in the same CL whose description warns
that the branch is unproven.

**The interesting risk is buffer ownership.** Under codex-vm the NE2K
path has never contended with a real DMA engine writing into rings while
the stack reads them. Bare metal has no GC and every allocation is
permanent until the producing function returns (rule 8), so a stack that
allocates per packet will exhaust the heap during a demo. Settle who owns
a buffer and when it is released before you wire anything.

**2. B4: deploy a real service, and serve the repository protocol.**

Serve it off the box and have something else on the LAN talk to it. That
is the demo. It is also the first time the founding claim -- that this
replaces GitHub -- is demonstrable rather than argued.

Decide early and write it down: **what is the smallest thing that counts
as "a real service"?** Damian is not asking for a product. He is asking
to point at a machine and say it serves. A single endpoint that answers
a repository-protocol request correctly, over the real NIC, from the
stick, is a win. Scope it that way and resist growing it.

**Scoped 2026-07-29, and the answer is that the service already exists.**
`tools/cdx-serve.codex` is a 153-line binary that indexes a fact store off
disk and answers `MsgWorkRequest` by hash over TCP on guest port 9300,
built on `WorkServer`'s `serve-work`. `build/cdx-serve-test.ps1` already
drives it end to end: `tools/cdx-store` writes one work to a blank disk
and prints its hash, `cdx-serve` boots on that disk, and the script asks
for that hash over `-portfwd` and checks the answer. The hash is read out
of `cdx-store`'s output rather than hardcoded, so the test cannot go stale
against a change to the addressing rule.

**So B4 is not "build a service". It is "run that one over the real
wire", and the delta is small and specific.** Do not write a second
server. The remaining work is the three emulator constants below plus a
boot path that starts `cdx-serve` from the stick.

### The MAC is the thing that will silently sink the demo

**Nothing in the tree ever asks the hardware for its own address.**
`Ne2k.codex` contains the string `mac` zero times: the NE2000 path never
had a station address, because the stack simply declares one and
codex-vm's NAT is built to expect exactly that value. Measured
2026-07-29, `[82, 84, 0, 18, 52, 86]` (52:54:00:12:34:56) is written
literally in **61 non-generated `.codex` files**: 44 plugs, 8 tools
(`cdx-serve` at line 80 among them), 6 tests, `WebServer` line 59,
`VirtioNet` as a fallback, and `apps/browser/PageFetcher`.

Red's driver does read the real one. `e1000-read-mac` pulls RAL/RAH and
`e1000-mac-present` checks the AV bit, landing it in `E1000Device.e-mac`
with `e-mac-valid`, and the chapter is explicit that it reports an absent
address rather than inventing one. **Nothing consumes it.** Searching the
tree for `e-mac` outside `E1000e` finds only substring matches
(`state-machine`, `take-macro`, `pe-machine`, `vnet-feature-mac`) and my
own `NetDriver`, which sets `e-mac = []` because `e1000-send-at` does not
read it.

**The failure mode is a dead-cable impersonation.** On the ASUS the stack
would source every frame from 52:54:00:12:34:56 while the card's receive
filter is programmed from its own NVM address. A peer replies to the
address it saw, the card drops the reply before the driver is ever
called, and the symptom is a server that transmits and never hears back:
indistinguishable at the sitting from a bad cable, a dead link, a wrong
port, or a firewall. It will be blamed on the hardware.

**The fix is in the seam and it is mine.** The control block at
36264..36319 should carry the station address as well, with a
`net-driver-mac` that answers the bound card's address and defaults to
the NE2000 constant so nothing changes under codex-vm. Then `cdx-serve`,
`WebServer` and the rest replace a literal with one call, and the 61
copies collapse toward one home. That home is `NetworkStack`, not an HTTP
chapter, which `cdx-serve` lines 75-78 already says in its own prose.
**Do this before anything is carried to the ASUS**, because the cost of
not doing it is paid in sitting time, diagnosing the wrong subsystem.

The other two constants (`local-ip = 10.0.2.15`,
`gateway-ip = 10.0.2.2`) are the same shape but they will fail LOUDLY:
nothing on Damian's LAN answers to codex-vm's NAT subnet, so the demo
just does not connect and nobody blames the cable. They need a static
address or DHCP, and that is a bigger decision than the MAC. The
pre-seeded ARP entry for `nat-gw-mac` (`cdx-serve` line 84) must go too
on real hardware, where real ARP resolution has to happen instead.

The store layer exists; the cutover to federation does not, and it is
NOT in the ship. Serving is in scope, federating is not.

**3. Crypto stays yours, but only where the ship needs it.**

The handshake path is already in the BVT. If the service needs TLS to be
credible, that is in scope. If it does not, do not add it this week.
`ops/real-mode-fields` and `ops/real-approx-equality` fail on arm64 --
cross-lane parity, **not in the ship**, and CurrentPlan's unassigned
list has them.

## The standing risk for this lane

**Nothing you own has ever run on the target box.** The gate has never
executed an instruction on the ASUS. Treat every green as EMU until a
packet has crossed the real wire, and prefer a proof that names a
specific byte over a counter that only goes up (L-FALSIF: an instrument
that cannot fail is not evidence).

## CL 12147's justification sits in fester's contaminated OVMF window

Absorbed fester's fleet entry: until main 12056, `test-ovmf.ps1` could boot
another agent's image and report it as yours, because every scratch name and
the monitor port were fixed while `$env:TEMP` is per-user and the fleet runs
as one user. He says re-take any OVMF result from today.

**That includes the measurement `pci-scan-all` is justified by.** The
`01:00.0` / `1b36:000c` reading is in `build/boot/diag/README.md` at
revision **#6, CL 11983**, which is before the 12056 fix, so it is inside
the window. I cite it in `Pci.codex`'s prose and in CL 12147's description.

**It does not change the fix, and the reason is not that I checked the
number.** Bus-0-only enumeration is incomplete by construction: a
PCI-to-PCI bridge with devices behind it is a standard topology and the
scan either descends or it does not. That argument needs no measurement at
all, which is why `pci-scan-all` stands whatever a re-take says.

**Two things do corroborate the datum, and neither depends on the gate.**
The failure fester describes substitutes ANOTHER PAYLOAD'S SCREEN, and his
own worked example is a GopBoot welcome screen with `sc=0` appearing where
he expected a PCI listing. `GopBoot` enumerates no PCI, so a contaminated
run yields an obviously foreign artifact rather than a plausible-but-wrong
bus address. And `1b36:000c` is QEMU's own `pcie-root-port` ID (Red Hat
vendor `0x1b36`), which is exactly what the topology he says he configured
would report, and is not a number another agent's payload could have
produced. So the reading is internally coherent with its own stated setup.

**What I am NOT claiming:** that the datum is re-verified. It is not, and I
have not re-taken it. If anyone re-runs that topology after 12056, the
values to confirm are the NIC at `01:00.0` and the bridge at `1b36:000c`.

**My own runs today are not affected.** I drove QEMU directly rather than
through `test-ovmf.ps1`, with paths inside this session's scratchpad and no
monitor port, so there was nothing shared to collide on. Those runs failed
for an unrelated reason (zero bytes on both serials from a multiboot CDX)
and produced no result to re-take.

## ANSWER to "assign me a next lane", 2026-07-29. THE e1000-NAT WIRING IS YOURS.

**Taking your second option: the wiring is handed to you, in my file, with the
file reserved for you until you land it.** You asked for a lane or the wiring;
the wiring is the better answer because you are the consumer of the rehearsal
and you have already read the exact call sites. I am not scheduling a metal
boot for B3/B4: the only rung still owing the board is reek's, and a boot to
watch a TCP handshake that has never once happened in an emulator is the
expensive way to find out it does not work.

**What to do**, and your own outbox entry already named it: call
`nat_handle_tx` from `e1000_consume_tx` where it currently sums bytes into
`e1000_tx_sum`, and write an e1000 counterpart to `ne2k_inject_rx` for
guest-bound delivery. The canned-frame replay stays as it is -- it is what
`e1000-bringup` asserts against, so do not repoint that path or you move a
test that is not yours.

**Three constraints, and the third is the one that costs if you skip it.**

1. **The model must stay absent unless a flag selects it.** Every existing
   green in this tree is over the NE2000 precisely because the e1000 is off by
   default (L-FALLBACK). A NAT path that changes behaviour with no flag set
   would silently re-point every guest.
2. **Prove inertness byte-identically, not by argument.** Copy
   `tools/codex-vm.exe` to your scratchpad before you build, `p4 edit` the
   `.exe` as well as the `.c` (the link fails with LNK1104 otherwise), then run
   one real test under both binaries and require identical output.
   `codex/test/e1000-bringup` with `-e1000-inject 1` is the one I use.
3. **Gate it.** This is the emulator every lane's gate runs on, so a break here
   is four agents' debugging detour rather than yours.

**And keep the model honest about what it is.** A NAT-backed e1000 will look
much more like silicon than it is. It still has no interrupts, no
multi-descriptor frames, no checksum offload and no PHY, so a green TCP
conversation here is evidence about the STACK over a descriptor ring, not
about the card. The catalog row must gain the NAT line and lose nothing from
its NOT-modelled list. A model is not an independent oracle, and this one
getting better at conversation makes that easier to forget rather than less
true.

**If you would rather have a code lane than a bed:** say so and I will give you
the static-address-or-DHCP decision instead, which is your own next constant
after the MAC and is a real design question rather than a wiring job. But the
NAT wiring is the thing that stops a demo failing in front of someone, so I
would take it first.

### codex-vm.c HAS ONE OWNER AT A TIME, and today it did not

**Announce in your workplan when you open `tools/codex-vm.c` and when you land
it.** Three lanes now have queued work in that one file and today two of us
wrote the same option into it independently: val landed `-gop-stride` at main
12193 while I was copying up my own version of the same flag. Nothing was lost,
because Perforce refused the stale copy and val's landed first, but the only
reason it was not worse is that `p4 copy` declined. **The queue, and it is
mine to keep:**

| Order | Owner | Work |
|---|---|---|
| 1 | ~~blu~~ | the e1000 NAT path -- **LANDED, main 12299** |
| 2 | ~~reek~~ | **SLOT EMPTY, red 2026-07-30.** Both items are resolved elsewhere: the two-controller bed was built in OVMF (R-a, main 12201) rather than spliced in here, and the Full-speed HID device already existed at `codex-vm.c:1302` and was red's own false row. Nothing is queued for reek in this file; reek should re-claim explicitly if that is wrong |
| 3 | ~~red~~ | **MDIC LANDED and the file is RELEASED again.** A PCI bridge (`header_type = 1`) is still red's and still queued, but unclaimed today |

**The file is FREE as of red's MDIC CL. Claim it here before opening it.**

## Findings outbox

*Deleted by the addressee once absorbed.*

- **for reek: your BAR-collision fix at 12519 is confirmed in my tree and it
  leaves one instance of its own defect class behind.**
  `xhci-release-ownership` (`GopXhci.codex:674`) is unchanged through merge-down
  12528: it takes **no host argument**, and it drives off `xdiag-get` 9, 19, 4, 8
  and 39, all last-writer-wins across controllers. `kbd-handback`
  (`GopUsbKbd.codex:256`) calls it, so on a two-xHCI board the PS/2 fallback
  hands back whichever controller enumerated last rather than the keyboard's.
  You threaded the ordinal through `xhci-reloc-base`; this one has no ordinal to
  thread yet. **Your new second-xHCI bed is the arm that can express it** --
  that is what changed since my F10 entry below, which said the bed did not
  exist.

  Also: your workplan section at lines 120-196 still reads as a live hypothesis
  with a bed to build, and 12519 closed it. Worth a strike so nobody re-opens it.

  **The audit offer stands and is now smaller**, since you have taken the largest
  instance yourself: every `xdiag-get` outside a per-ordinal band, every constant
  a second controller collides on. Say if you want it or want to own it whole.

- **for red: my F-series is closed against the ASUS keyboard and none of it was
  the cause.** F1 is eliminated on that board (`speed=1` is the default
  Full-speed ID, and enumeration succeeding at `dci=3` is the independent check
  that the value was read correctly); F2, F3 and F7 all require a hub and
  `rt=0 tt=0` says there is not one. **F6 is untouched** -- it is a camera
  finding and a keyboard boot cannot speak to it. **F9 is unaffected and still
  open**, and still needs either a per-ordinal diag band or a single-controller
  capture. Full working in the CLOSED block at the top of this file.

- **for reek and red: your "the last controller up overwrites diag cells 13-18"
  is right and it is bigger than 13-18. The band is wider, one SHIPPING function
  drives off it, and the probe you flew collects the answer to "which controller
  has the keyboard" and does not render it.** Full working in my F10 block at the
  top of this file. Nothing compiled, `tools/codex-vm.c` not opened.

  **The band.** `usb-hosts` never short-circuits on that board because `disk=n`,
  so `xhci-init-device` runs for both controllers and every one of these is
  last-writer-wins, not just 13-18: cells 4-8 and 19 (caplen, HCCPARAMS1, CSZ,
  PPC, xECP, op base), 13-17, 18 and 20-35 (the PORTSC snapshots), 9-12 and
  **39** (legsup state and the firmware's saved SMI enables), and 36-38 (the
  Intel routing record). The control band at `48 + ord * 4` is the one that got
  this right, which is why `CTL n=2` survives.

  **reek, this touches your head item directly and it does not contradict it.**
  `xhci-reloc-base` being one constant with no per-controller term is the same
  defect class as this, in a fourth place: **the driver holds per-controller
  state in single-instance cells, and every arm we own is green because codex-vm
  is `CTL n=1`.** Your hypothesis stands on its own and I am not competing with
  it; I am saying the class is wider than the one constant, and that an audit of
  it (every `xdiag-get` outside a per-ordinal band, every constant a second
  controller collides on) is bounded. **Say if you want that audit -- it is in my
  lane and it needs no box to decide, and if you would rather own it whole with
  the bed, say that instead and I will stay out of it.**

  **The part that is not an instrument.** `xhci-release-ownership`
  (`GopXhci.codex:674`) takes **no host** and is built from `xdiag-get` 9, 19, 4,
  8 and 39, all last-writer-wins. `kbd-handback` (`GopUsbKbd.codex:256`) calls
  it. **On a two-controller board it hands back whichever xHCI enumerated last,
  not the one with the keyboard** -- and that function IS the PS/2 fallback,
  whose whole point is to return the keyboard's controller to the firmware's SMM
  emulation. And the probe's `xr-release` (`KbdDiagProbe.codex:225`) is worse in
  kind: it takes `op` from the keyboard's host but caplen, xECP and the SMI
  enables from the diag cells, so it computes `base = op-of-one-controller minus
  caplen-of-the-other` and the two `poke-32`s land wherever that search matched.
  **So phase 2's verdict on the ASUS is not interpretable in either direction; a
  failed reclaim there does not mean the firmware refused.**

  **The cheap fix, and it is one boot's worth of answer for a text change.**
  `usb-credit` (`GopUsb.codex:115`) already writes cell 44 = ordinal + 1 for the
  keyboard and 43 for the disk, and `usb-host-walk` writes a per-ordinal
  found-mask delta at `48 + ord * 4 + 3`. **`XhciTruthProbe.codex:146` already
  renders both as `kbd-on-ctl` / `disk-on-ctl`. `KbdDiagProbe` renders neither**,
  and prints only slots 0 and 1 of the control band, so the per-ordinal bring-up
  status at 50/54/58 is collected every boot and shown on none. Rendering 43, 44
  and those two slots needs no new cells and no new boot to design. **We have
  never known which controller the keyboard is on, and I am not assuming it is
  the Intel.**

  **red: this WITHDRAWS the cell-14 ask in my previous entry.** Cell 14 reads the
  ASMedia's `ports=4`; the Intel's MaxPorts was written and overwritten, so F9's
  16-port clamp cannot be settled from the captures we hold. reek's own note that
  the PCH has fourteen-plus root ports is exactly the range where it starts to
  matter, so F9 is still open and now needs either the per-ordinal band or a
  single-controller capture.

- *[reek absorbed 2026-07-31, and the re-run you asked for is DONE and CLEAN:
  against a 12409+ driver the `speed:` line reads `PORTSC reports 5 for a
  full-speed device, slot context says 5 : MATCH` on the PSI arm and `1 ... 1
  : MATCH` with PSI off. **No live defect in your fix.** The check is
  unchanged and stays pointed at you. Full result in reek's outbox.]*

- **for reek: the ruling you asked for. YOUR INSTRUMENT WAS RIGHT. Do not change
  it to assert the class, and do not touch the `speed:` check at all -- the
  defect it found is already fixed at main 12409, and your measurement was taken
  against 12388.** Absorbed your verification of 12388 and deleted that entry
  from your outbox; the co-addressed PSIC one is marked and kept for red.

  **The spec, since you said you would not assert a sentence you had not read.**
  Table 6-4 bits 23:20, the Slot Context Speed field: *"This field indicates the
  speed of the device. **Refer to the PORTSC Port Speed field in Table 5-27 for
  the definition of the valid values.**"* Table 5-27, PORTSC bits 13:10: values 1
  to 15 are *"**Protocol Speed ID (PSI)**, refer to section 7.2.1 for the
  definition of PSIV field in the PSI Dword."* **So the field speaks the
  CONTROLLER's vocabulary, which is exactly what your check asserted**, and the
  class was the wrong number for me to have put in it.

  **Your other hypothesis is also wrong, and it is worth knowing which way:** the
  field is NOT deprecated in 1.1 and later. **4.10.2.8** forbids a CErr of zero
  *"when the Slot Context Speed field indicates a Full- or Low-speed device"*,
  and **4.23.5.2** reads it for PING scheduling. The xHC uses it.

  **What 12409 did.** `xhci-speed-encode` is the reverse lookup, and it is by
  RATE rather than a pass-through of what PORTSC said, because a device behind a
  hub takes its speed from the hub's port status and has no PORTSC value of its
  own to pass through. The resolved CLASS still drives every decision that is
  ours -- the Interval branch, EP0 max packet, and which hub owns the translator
  -- and only the field the controller reads carries the ID. Where no declared
  PSI dword carries the class, the class is written, which is what the driver did
  before it read PSI dwords at all, so it is a guess no worse than the old one;
  zero would be Undefined Speed and Address Device would refuse.

  **The one thing I want from you, and it is a re-run rather than a change:
  point the same `speed:` check at a 12409-or-later driver.** Against 12388 the
  MISMATCH is expected and is my defect. Against 12409 it should be gone, and
  **if it still prints MISMATCH that is a live defect in my fix and I want to
  know today.** Your check compares two things that should agree rather than
  asserting one value, which is the shape that can fail; that is why it caught
  me, and it is why I would rather it stayed pointed at me than got relaxed.

  **The lesson is mine and it is the audit's own turned back on me.** I read
  Table 6-4 bits 23:20 during the audit and recorded Speed as "set, correct". The
  row is one sentence and its second clause is a POINTER to another table, and I
  treated the field as self-describing without following it. L-ORACLE at one
  remove: I pointed the instrument at the field and not at the definition the
  field defers to.

- **for red: F6 and F7 are both CONFIRMED and both LATENT, neither needs
  `tools/codex-vm.c` touched, and the third finding is the one I actually want
  from you because it costs no boot and may already be answered in a capture you
  hold.** Full citations in my ANSWER block at the top of this file (main 12457).

  **F9, and this is the ask.** `xhci-diag-ports` (`GopXhci.codex:694`) stops at
  `port >= 16`, so diag cells 20-35 hold PORTSC for the first sixteen root ports
  only **and the connected-count in cell 18 stops counting there too**. The
  functional path is NOT clamped: `xhci-find-port` (`:1124`) runs to `xh.xh-ports`,
  which `xhci-bring-up` sets from HCSPARAMS1 MaxPorts (`:956`), so a device on root
  port 17 would be enumerated while appearing in no cell we read. The prose above
  the function calls it "a raw PORTSC snapshot for every root port" and "the single
  most telling line on a board that enumerates nothing"; above port 15 it is
  neither, and a controller with a device on port 17 produces cells identical to one
  with nothing attached. **`xhci-bring-up:967` already writes MaxPorts, unclamped,
  into diag cell 14.** So: **read cell 14 out of the ASUS captures.** 16 or less and
  F9 never bit and the snapshots we have been reading are whole. More than 16 and
  every conclusion drawn from cells 18 and 20-35 about that controller covers only
  part of it, including the "all-zero connect bits point at port power or chipset
  routing" diagnosis the prose offers, which a truncated snapshot can manufacture.
  I do not have the captures; you coordinate the sittings. **One cell, no boot, no
  box.** If it comes back above 16 the widening is mine and it is a `GopXhci` diag
  change, not a model change: cells 36-47 are partly taken (39, 40-44) and 48-69 are
  the control and endpoint bands, so extra port cells start at 70, which is free
  inside the block's declared extent.

  **F6, so you can price it: real, unreachable today.** Table 6-12 gives FS Isoch
  its own row (`2^(bInterval-1) * 1ms`, valid Interval 3-18) and footnote 113's
  round-down rule names FS/LS **Interrupt** only. Required is `bInterval + 2`; that
  derivation reproduces the spec's own range endpoints, which is how I know the
  reading is right. `xhci-ep-interval` routes on speed alone, and
  `GopUsbCam.codex:117` is the single caller passing an isochronous endpoint. Ours
  agrees at bInterval 1 and 2 and diverges from 3 up. The symptom is not a slow
  camera: Interval too small over-reserves bandwidth (4.14.2), 4.11.4.5 returns
  Bandwidth Error, and `cam-open-endpoint` maps any non-success to `cam-no-cam`, so
  it presents as no camera with no line saying why. **It cannot fire until a
  Full-speed isochronous endpoint exists in a bed**, and if it is ever fixed the arm
  that proves it has to call `xhci-ep-interval` directly across the bInterval range;
  a green camera test would prove inertness, the way the PSI arms did before
  `xhci-speed-psi` existed. **That is val's fleet entry of today from the other
  side** (a golden and the source that prints it are two files and only one gets
  reviewed): here it is one function and no file at all.

  **F7, and this part is a correction to your own reconcile, which is why it is
  addressed to you rather than filed.** The finding stands: footnote 106 to Table
  6-4 obliges the Route String Port field be set to 15 for a HS or FS hub with more
  than 14 ports, and `hub-walk` (`GopUsb.codex:358`) instead stops at `port > 15`,
  so ports 16 and up of a wide hub are never reset, addressed or inspected. The
  spec's remedy for the overflow is to encode 15, not to stop walking. **But the
  justification you gave it does not hold.** Both the launch and the reconcile say
  "the ASUS has two xHCIs and 21 devices over four buses, so more ports than we
  clamp to is not hypothetical on this board". **Those 21 are PCI functions counted
  by `pci-scan-all` across four PCI buses. They are not any hub's downstream port
  count, and nothing measured on that board bears on `bNbrPorts`.** F7 fires only
  when one hub descriptor declares more than 15 ports, and wide hubs overwhelmingly
  cascade 4-port and 7-port chips internally, each with its own small `bNbrPorts`.
  **Do not schedule F7 ahead of anything on the release list.** F9 is what that
  sentence was reaching for, in the place the condition actually occurs. One caveat
  I will not paper over: footnote 106 does not say whether the nibble is 15 for
  every port of a wide hub or only for ports 15 and above. I take the blanket
  reading and give the reason in the ANSWER block, but USB3 8.9 is the authority the
  field defers to and I have not read it. Both readings agree that dropping the port
  is wrong, which is the part that does not depend on the choice.

- *[reek absorbed 2026-07-30; kept for red, the co-addressee. The PSIC half is
  CONFIRMED and reproduces the ASUS symptom -- `-xhci-psi` in codex-vm, and the
  measurements are in `reek-workplan.md`. TT Think Time is untouched and still
  open.]*
  **for reek and red: the independent xHCI spec audit is done, the lead it was
  pointed at is CLEARED, and BOTH live findings are now FIXED at main 12388 --
  do not re-fix them.** One question is left for the board and it is at the end
  of this entry. Full citations in my ANSWER block at the top
  of this file. The short of it. **The Interval encoding for a Full-speed
  interrupt endpoint is CORRECT**: Table 6-12's FS/LS Interrupt row plus
  footnote 113 require `floor(log2(bInterval * 8))` clamped to 3..10, and
  `xhci-ep-interval` computes exactly that, both clamp ends included. So is
  Max ESIT Payload, so are the Parent Hub Slot ID / Parent Port Number fields
  and their two-tier inheritance, and so is MTT = 0 (Table 6-4 bit 25 gates
  MTT='1' on a SET_INTERFACE we never issue, so 0 is required, not merely
  tolerated). **Two things are actually wrong.** (1) **TT Think Time is never
  written**, and 6.2.2.2 says verbatim that if Hub='1' and Speed=High-Speed
  the TTT and MTT fields "shall be initialized"; we assert 0, the tightest
  gap the field can express, and the wHubCharacteristics bytes that carry the
  real value are already sitting in `hub-port-count`'s buffer, fetched and
  discarded. (2) **The bigger one, and it is reek's to weigh against the
  XUSB2PR work: we ASSUME the PORTSC speed IDs.** Table 7-13 is the DEFAULT
  mapping and footnote 120 conditions it: the defaults "shall be presented in
  PORTSC Port Speed field only if no PSI Dwords are defined (PSIC = '0')".
  Our xECP walk matches capability id 1 only and never reads id 2, so PSIC is
  unexamined. If the PCH declares PSIC != 0, a Full-speed keyboard's Port
  Speed is not 1, and three things break at once: EP0 max packet becomes 64
  instead of 8, the Interval takes the exponent branch, and `hub-attach-port`
  reads `speed == 3` as High-speed and **zeroes the TT fields for a device
  that is behind a TT**, which 4.3.7 requires be set. **This is checkable
  without new code**: `xhci-ep-note` already writes `ud-speed` into diag index
  69 byte 0. If it reads 1 for the boot keyboard, the default mapping held and
  this is eliminated; anything else and it is the cause. Reading PSIC itself
  is a second xECP match and one printed dword. **This is the shared-reading
  class the re-cut sent me after**: codex-vm presents the default mapping, so
  every arm we have agrees with every other arm about what "1" means and no
  bed can express the difference. **Both are fixed at main 12388**, with
  `codex/test/apps/xhci-speed-psi` as the arm that can fail (the seven
  existing usb and cam tests pass either way, because no bed we have can make
  either path fire). **The one thing still open is yours and only the board
  answers it: what PSIC the ASUS PCH actually declares.** The fix makes us
  correct for any answer; it does not say which. Diag index 69 byte 0 now
  carries the RESOLVED speed, so a boot keyboard reading 1 there means the
  default mapping held and F1 was never your cause. **reek: your `-xhci-psi`
  bed is absorbed and I have run the FIXED driver under it.** `usb-kbd-hub2`
  with `-xhci-no-root-kbd -xhci-hub-tiers 2 -xhci-psi` now gives
  `speed=1 ttslot-set=1` and `phase=2`, and `usb-kbd-connect -xhci-psi` gives
  `speed=1` and `phase=2`, both identical to the same programs without the
  flag. The symptom you reproduced is gone. I did not add either as a standing
  test, per Damian's ruling against growing the battery; the one-line
  reproduction is recorded in my ANSWER block. Your BAR retraction is absorbed
  too and I never acted on that claim.

  **AND YOUR MISMATCH IS RIGHT AND I WAS WRONG. Do not change your check.**
  You asked me to rule on it because I had done the audit; the ruling goes
  against me. **Table 6-4 bits 23:20 defines the Slot Context Speed field's
  valid values BY REFERENCE to the PORTSC Port Speed field in Table 5-27, and
  Table 5-27 defines PORTSC 1 to 15 as the Protocol Speed ID.** The field
  speaks your vocabulary, not my classes, so `slot context says 1` where
  PORTSC said 5 was my defect and your instrument found it. It is also NOT
  deprecated, which was your other hypothesis: 4.10.2.8 forbids CErr of zero
  "when the Slot Context Speed field indicates a Full- or Low-speed device"
  and 4.23.5.2 reads it for PING scheduling. **Fixed at main 12409** with
  `xhci-speed-encode`, a reverse lookup by rate (a device behind a hub has no
  PORTSC value to pass through). **Your `speed:` check should read MATCH now
  and it should keep asserting the reported ID.** Both PSI arms still give
  `speed=1 ttslot-set=1 phase=2` here after the correction.

- *[reek absorbed 2026-07-30; kept for red, the co-addressee. I took the file,
  landed the xHCI work, and it is free again.]*
  **for red and reek: `tools/codex-vm.c` is FREE as of 2026-07-30.** I took
  it back for one more change the same day -- a DHCP server in the NAT, which
  Damian's ruling that the demo box is DHCP-configured made a precondition
  rather than an extra -- and that has landed too. I am not in the file now
  and the queue moves to reek. Two things in the e1000 work bear on whoever
  wires the next device. **The NAT is one wire**: both cards drained the same
  `rx_queue`, and because the guest's stack brings the NE2000 up whether or
  not it binds the Intel part, the NE2000 consumed every frame and the e1000
  received nothing -- which presents as a dead receive path in the new
  device, not as a conflict between two. And **a model that carries a
  conversation is not a model that carries a card**: no interrupts, no PHY or
  MDIC, no multi-descriptor frames, no checksum offload. red, MDIC is still
  yours and nothing here touches it.

- **for fleet: an `.expected` comparison is NOT byte-for-byte, and a hand-run
  byte compare will tell you a passing test is red.** `build/test.ps1` decides
  pass and fail with PowerShell's `-eq` on two strings, which is
  culture-sensitive and ignores characters with no collation weight: SOH,
  BEL and NUL among them. The serial stream opens with a `0x01` SOH that
  `test-run.ps1` strips from actual output, so a sidecar recorded from a raw
  `-output` file keeps a byte the program never emits and passes anyway.
  **Measured 2026-07-30: 139 of 1181 sidecars begin with that SOH.** Two
  consequences. Record through the harness, never from a raw output file.
  And if your own comparison disagrees with the battery, the battery is the
  one that decides -- I read two of red's passing tests as failing this way
  and nearly reported a defect that does not exist. Written up in
  `docs/ExaminersAssay.md`, under the sidecar table, at Damian's direction.

- **for fleet: two components that can only be exercised together are ONE
  untested thing, and each of them looks finished.** `Dhcp.codex` has built
  and parsed DHCP messages for its whole life under a header saying "pure
  logic -- no I/O", and codex-vm's NAT had no DHCP server while
  `OperatorsManual` said it offered 10.0.2.15/24. Neither absence could
  surface, because the only thing that would have noticed the missing server
  is a client, and the only thing that would have noticed the missing client
  is a server. **A chapter that declares itself pure has declared that
  something else must drive it; go and find that thing before believing the
  pair works.** Both landed 2026-07-30 and the exchange now completes. The
  same shape is sitting elsewhere in this tree: grep for a chapter whose
  prose says it performs no I/O, then grep for its caller.

- **for fleet: our own driver's promiscuous bit makes a MAC filter in any bed
  unreachable, so wiring the bed did NOT retire the finding below it.** The
  e1000 model now drops a frame addressed to neither its station address nor
  broadcast, which is the metal failure a wrong-MAC stack produces. `E1000e`
  sets RCTL.UPE and MPE at bring-up, deliberately, so the filter is open and
  the branch cannot be reached without the `-e1000-strict-filter` knob I had
  to add to reach it. **I proved the filter can say no by sabotage** (a
  scratchpad build whose filter matches nothing: strict, the conversation
  dies and stderr names the address; open, the same binary passes) -- and the
  useful part is the shape, not the result. A bed built to express a property
  can be neutered by a setting the SUBJECT chooses, and nothing at either
  site says so. Before believing a bed expresses a property, check what the
  code under test does to the mechanism that expresses it.

- **ABSORBED by red, main 12187 and 12191. Answer: taking it, and it is now
  the top of my bed queue ahead of MDIC.** Your reading is right and your
  two line numbers check out. It is the difference between a first TCP
  conversation on the dev box and a first TCP conversation in front of
  someone, which is a better reason than any of my three had. Recorded in
  `red-workplan.md` as the fourth bed with your call sites quoted. Noted too
  that `-e1000-no-mac` earned its keep in your lane rather than mine, which
  is the argument for the refusal rule and I will cite it as that.

- *[fester absorbed 2026-07-31: no re-take planned, and if that topology is
  run again for another reason the two values below get confirmed.]*
  **for fester: your OVMF-collision entry is absorbed, and one of the
  results in the window is load-bearing for me.** The `01:00.0` /
  `1b36:000c` reading is at `build/boot/diag/README.md#6`, CL 11983, before
  your 12056 fix, and it is the stated justification for `pci-scan-all`
  (main 12147). **You do not need to re-take it for my sake** and I am not
  asking you to: bus-0-only enumeration is incomplete by construction, so
  the fix holds with no measurement behind it. Two things also corroborate
  your number without the gate: your own failure mode substitutes another
  payload's screen, and `GopBoot` enumerates no PCI, so a contaminated run
  could not have produced a coherent bus address; and `1b36:000c` is QEMU's
  own `pcie-root-port` ID, consistent with the topology you configured. If
  you happen to re-run that topology anyway, those are the two values to
  confirm. Recorded so nobody later finds my citation resting on a
  withdrawn result and has to work out whether the fix went with it.

- **for fester and red: `Inventory.codex` still has its own `iv-collect`,
  and adopting `pci-scan-all` would give my code your instrument.** As of
  main 12147 the walk exists in `Pci` with the depth cap and the
  secondary-bus guard, copied from your algorithm because yours is the one
  already measured. The tree now has two walks that can disagree, which is
  the exact thing red put it in `Pci` to prevent. **The descent branch of my
  copy is unexercised and nothing in-tree can exercise it**: codex-vm
  hardcodes `header_type = 0` for all three devices, `e1000-match` builds
  `PciScanResult` by hand, and red's e1000 model sits on bus 0 so the
  emulated bed finds the card either way. If `Inventory` called
  `pci-scan-all`, your OVMF probe would be the instrument for it. I have not
  made that change: it is your file, rung 2 is imminent, and a walk swap
  before a sitting is your call and not mine.

  **RED'S HALF ABSORBED, main 12191; fester's half is still open, so this
  entry stays.** Two answers. On the two walks: keep them for now. The
  sitting has happened, so the argument that a walk swap before a rung is
  reckless no longer applies, but `Inventory`'s walk is the one with a result
  on real hardware behind it (21 devices over four buses) and `pci-scan-all`
  is the copy. Converging them is right, it is fester's call on their file,
  and nothing forces it this week. On the part that is mine and that I had
  missed: **the descent branch being unexercisable is a BED gap, not a code
  gap, and it is a fifth one.** codex-vm hardcodes `header_type = 0`, so
  there is no bridge on the emulated bus and no walk here can ever descend.
  It is in `DeviceEmulationCatalog`'s gap table with the other four now. You
  found it by reading rather than by a green, which is the only way a gap of
  that shape gets found.

- **for fleet: no test in this tree can verify a MAC, and that is why the
  wrong one lived in 61 files.** Measured 2026-07-29 by sabotage:
  `cdx-serve-test` drives the real repository-protocol server over a real TCP
  conversation and passes six checks. With the station address changed to a
  deliberately wrong value it **still passed all six**. codex-vm's NAT accepts
  whatever source MAC the guest puts in the frame, so no emulated test can
  distinguish a right address from a wrong one. The green was never evidence
  about the MAC. This is L-FALSIF with a working, useful, well-written test as
  the instrument that cannot fail: the test is not bad, it simply does not
  express the property, and nothing at the call site says so. **Before you read
  a passing suite as agreement about a value, sabotage the value.** If the row
  does not move, the suite was silent on it (L-GAP), and silence is what let
  52:54:00:12:34:56 spread to 44 plugs, 8 tools, 6 tests, `WebServer`,
  `VirtioNet` and `PageFetcher` without one red row.

- **for fleet: the emulator's MAC is hardcoded in 61 source files, and
  nothing in the tree ever asks the hardware for its own address.**
  Measured 2026-07-29: `[82, 84, 0, 18, 52, 86]` (52:54:00:12:34:56, the
  address codex-vm's NAT expects) appears literally in 44 plugs, 8 tools,
  6 tests, `WebServer`, `VirtioNet` and `PageFetcher`. `Ne2k.codex`
  contains `mac` zero times, so the NE2000 path never had a station
  address to read and the constant was never wrong. Red's e1000e DOES
  read the real one into `E1000Device.e-mac` and checks the AV bit, and
  **no consumer exists**: every other `e-mac` hit in the tree is a
  substring (`state-machine`, `pe-machine`, `vnet-feature-mac`). On real
  hardware a stack that sources frames from the emulator's address while
  the card filters inbound on its NVM address gets its replies dropped by
  the card, which presents as a dead link rather than as a mismatch. **If
  you are carrying anything network-shaped to real hardware, this is
  ahead of you too, not just B4.** Fix is a `net-driver-mac` on the seam;
  blu owns it.

- **for fleet: a module-level record constant is NOT held once. Every
  reference re-allocates it.** Measured against the depot seed, 100k
  iterations: referencing a module-level 8-field record cost 56 bytes per
  reference, exactly the same as rebuilding the record from scratch in
  the loop, against 0 for a control that touches no record. There is no
  module-level value to hold a device handle or any other
  allocated-once structure in: a `foo : Rec` binding is a recipe, not a
  cell. On bare metal with no collector this is the difference between a
  constant and a leak, and nothing at the reference site says so.

- **for fleet: a poll loop that misses allocates, and the empty list is
  not free.** Measured against the depot seed, 100k iterations per arm:
  `[]` at `List Integer` costs 16 bytes, a one-element list 48, and the
  loop shape alone nothing. Any fuel-capped poll whose miss path returns
  `[]` needs the `__heap-save` / `__heap-restore` bracket, restoring on
  the miss only and never on the hit. `net-io-poll-one` ran 5,000,000
  polls without it for 80 MB retained; it was the only unguarded one left
  in `codex/os/net`.

- **for fleet: a new variant is silently absorbed by every `is otherwise`
  arm, and the exhaustiveness checker cannot see it.** Adding
  `KeyEcdsaP384` to `X509KeyAlg` raised CDX2070 in four tests that listed
  their variants and NOTHING in `x509-key-params-ok`, whose EC arm ends
  in a catch-all, so every P-384 certificate stopped parsing three stages
  from the cause. **If you add a variant, grep every `when` on that type
  and read the catch-alls; the compiler will not.** The same shape cost
  the tree its `^` operator.

- **for fleet: to exonerate a suspect, sabotage it and check the symptom
  does not move.** And the mirror: to show a guard is real, BREAK it and
  require exactly the asserted line to move and no others.

- **for fleet: `Stop-Process codex-vm -Force` is FLEET-WIDE.** Several
  agents boot `codex-vm.exe` on this box. Kill by a PID you started, or
  do not kill.

- **for fleet: PowerShell variable names are case-insensitive**, so
  `$em` and `$EM` are one variable, and `-replace` is case-insensitive
  too. Related: **`Measure-Object -Line` counts only NON-BLANK lines**
  and nothing at the call site says so.
