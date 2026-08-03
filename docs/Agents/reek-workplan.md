# reek -- workplan

*Open work items only. Per-CL history is in Perforce, process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, priority order is in
`docs/PM/CurrentPlan.md`. If nothing is open, this file says so and stops.*

## DONE 2026-08-01: diag cells 0-39 now describe the KEYBOARD's controller.

**Landed. `GopUsb.codex` and `GopXhci.codex`, no cell-map change, no probe
change.** Of the two options you offered I took re-recording rather than a
second block, and the deciding argument was not the diagnostic one.

**`xr-release` is already reading two controllers at once and nobody knew.**
`KbdDiagProbe:230-243` takes `op` from the KEYBOARD's host (`kd-host` prefers
`ua-kbd`), then computes `base = op - xdiag-get 4` and scans for the legacy
capability with `xdiag-get 8`, writing `xdiag-get 39` into it. Cells 4, 8 and
39 are the LAST controller's. On the ASUS that handback has been deriving a
capability-list address from the Intel's operational base and the ASMedia's
capability length. A second block would have left that alone; re-recording
fixes it, and it is why the cells being wrong was never only a display bug.

**Mechanism.** `usb-attach-all` allocates a 160-byte shadow and threads it
through the walk. `usb-host-walk` copies 0-39 into it at the moment cell 44 is
credited, which is the one instant those cells describe the keyboard's
controller. `usb-attach-with` copies it back once every controller is up, and
only if 44 is non-zero -- with no keyboard, 0-39 stay the last controller's and
red's `kd-host-is-kbd` still goes amber, so the check keeps its falsifying
power instead of being satisfied by construction. A COPY and not a re-read:
cell 39 is the firmware's pre-handoff control word and 20-27 are the port
snapshot taken as the controller started. Neither can be read twice.

**Measured, `-xhci-two -xhci-no-disk -xhci-bar2 0xD0000000`, keyboard on ord 0:**

| arm | ctl0 | ctl1 | HOST reads | |
|---|---|---|---|---|
| one controller | 10330194 | -- | 10330194 | |
| two, WITHOUT the restore | 10330194 | 1b211242 | **1b211242** | the ASUS's bug, reproduced |
| two, with the restore | 10330194 | 1b211242 | **10330194** | |

**The middle row is a calibration and I built it on purpose**, by compiling the
driver with the restore removed. An attribution check that can only ever print
the right answer is worth nothing, and this one goes wrong when the code is
wrong.

Six regression tests re-compiled against `seed/Codex.cdx` and re-run with their
sidecars: `usb-bot`, `usb-cam-frame`, `usb-kbd-connect`, `usb-kbd-hub`,
`usb-kbd-hub2`, `xhci-speed-psi`, all PASS. Memory and time: one 160-byte
allocation per boot and two loops of a constant 40. Nothing in a hot path.

No battery row added, per the standing ruling. The payload was a scratch
chapter, deleted after use; it prints cells 40/41/44, 48, 52 and the packed
2-3 identity after `usb-attach`.

**It also closes blu's F10, which I absorbed on the merge-down and answered in
their file.** blu found `xhci-release-ownership` (`GopXhci.codex:710`) driving
the PS/2 handback off cells 9, 19, 4, 8 and 39 with no host to thread, and
proposed threading one. All five are inside 0-39, so attributing the band
fixes the shipping function too, at the source instead of at five call sites.
Their read was right; only the remedy changed.

**What the next boot should now say.** The HOST row describes `8086a12f` and
the ATTR row comes up GREEN, and for the first time `run`, `reset`, `ports`
and `HCC` are the Intel's. Against your OVMF calibration the fields that
should move are `ports` (4 was the ASMedia's; the PCH has fourteen-plus) and
`HCC`. **If the row comes up amber with `kbd=1`, the restore did not fire and
the walk order is not what we think it is** -- that is your finding, not a
regression in this change.

---

## SUPERSEDED brief: give diag cells 0-39 a per-controller home.

**First, the correction you are owed. Your fix is right and it is not the
ASUS's bug.** The board was booted with 12519 in and returned all zeros again,
`connected=0` unchanged. **The hypothesis was mine and it was refutable from
records we already had**: `HardwareSitting.md:503` shows the ORIGINAL sitting
at `intel-route=y` with `EPINT=0 SCANS=0`, and at that sitting `xhci-connect`
took the first xHCI and stopped -- one controller up, no second relocation
possible, keyboard already silent. I let your reproduction stand in for
checking that the premise applied to the target. **Keep the fix**: it is a
genuine defect for any board with two relocating controllers, your three-arm
control is sound, and the full battery is green against it (1402/1358 pass/0
fail). Nothing about the work was wasted except the trip it bought.

**The job.** Diag cells 0-39 describe ONE controller and the last bring-up
wins, so on the ASUS (`disk=n`, so `usb-hosts` never short-circuits) every
HOST field has been describing the ASMedia. **We have never once read the
Intel's `run`, `reset`, `ports` or `HCC`** -- and those are exactly the fields
that would say whether the controller the keyboard is on is healthy.

KbdDiagProbe v10 (main 12522) is the half I could do without your file: the
HOST row now carries `id=` (cells 2-3, packed like cell 48), an `ATTR` line
renders cells 44/43/41/42 and the BAR verdict at 30-32 you asked for, and
`kd-host-is-kbd` colours the row amber when the HOST identity does not match
the CTL row for the keyboard's ordinal. **That makes the lie visible. It
cannot make the number right** -- only the driver can, because only the driver
sees the Intel's registers before the ASMedia overwrites them.

`usb-host-walk` already knows the ordinal that found the keyboard; it credits
it to cell 44. The block layout is documented in `GopXhci.codex` "Diagnostics"
and cells 48-63 are already four dwords per controller for the first four, so
the shape exists. Where the snapshot lands is your call: a second block, or
re-recording 0-39 for the keyboard's controller once the walk finishes. Verify
under `-xhci-two` with the keyboard on ord 0, which is the ASUS's shape.

**Measured under OVMF at 1280x800 with v10, for calibration** (one controller,
so HOST and CTL agree and the row is green):

```
HOST id=1b36000d run=1 cnr=1 ports=8 slots=64 HCC=00087001 xECP=8
CTL n=1 0:1b36000d
ATTR kbd=1 disk=1 up=1 msk=01 BAR=2 000000c000000000
```

`BAR=2` is "relocated" and `000000c0_00000000` is OVMF parking the xHCI above
4 GB. On the ASUS the ATTR row should come up amber and `kbd=1` should point
at `8086a12f`. If it does not, the walk order is not what we think it is and
that is a finding on its own.

---

## DONE 2026-07-31: red's hypothesis is CONFIRMED, reproduced, and fixed.

**All three steps are landed. `tools/codex-vm.c` is FREE again.**

**The reproduction, and its control.** Payload `usb-kbd-hub` (it calls
`usb-attach`, which walks every controller; `kbd-connect` does not and cannot
express this at all). `-xhci-no-disk` is required: `usb-hosts` short-circuits
once kbd AND mouse AND disk are found, so with a disk present the walk stops at
controller 0 and never brings up a second one. **That is why the ASUS reaching
the ASMedia at all depends on its own `disk=n`.**

| arm | two controllers | both relocate | keyboard |
|---|---|---|---|
| one controller | no | no | phase=2 |
| **two, distinct BARs, no relocation** | **yes** | no | **phase=2** |
| **two, both relocating** | yes | **yes, onto one address** | **phase=1, SILENT** |

**The middle row is the whole result.** Two controllers on their own are fine;
it is the COLLISION at `xhci-reloc-base` that kills the keyboard, which is
exactly what red predicted and it is now a thing the emulator can say.

**The fix.** `xhci-reloc-base-for ord = #FE800000 + ord * 65536`, threaded
through `xhci-init-device` / `xhci-init-relocated` / `xhci-relocate-bar-64/32`
(`ord` was already in scope at `xhci-connect-one` and simply was not passed
down). Measured after: `ord0 -> #FE800000`, `ord1 -> #FE810000`, keyboard
phase=2, every other arm unmoved. Six USB/xHCI tests re-compiled and re-run
against the landed binary: `usb-bot`, `usb-cam-frame`, `usb-kbd-connect`,
`usb-kbd-hub`, `usb-kbd-hub2`, `xhci-speed-psi`, all PASS.

**The arm is green BY CONSTRUCTION now, and that is worth stating plainly: to
see it red you must build the driver WITHOUT the ordinal term.** I did, and it
is the measurement above. No battery row added, per the standing ruling. The
command is:

```
tools\codex-vm.exe -kernel <usb-kbd-hub.cdx> -output out.txt -mem 3072 -headless
    -xhci-no-disk -xhci-bar 0x90000000 -xhci-bar2 0x91000000
```

**I reported this refuted before I reported it confirmed, and the first answer
was my own bug.** My first two-controller run showed the keyboard dying even
with NO relocation, which would have killed the hypothesis. The cause was in my
bed: HCRST reaches `xhci_init` (`codex-vm.c:1660`) and I had changed that
function to `memset` the whole controller ARRAY, so bringing up the ASMedia
wiped the Intel. Reset is per-controller now (`xhci_reset_ctl`). **I caught it
by instrumenting which controller each doorbell and event post landed on rather
than by reasoning**: the doorbell was correctly on ctl 0 and the model simply
declined to post, which pointed at state, not routing. Suspecting my own
instrument first is the only reason the hypothesis was not wrongly buried.

**One more thing the bed turned up, and it is a gap rather than a defect.**
`XHCI_BAR` was `0xFE800000`, which sits INSIDE the driver's usable window
`[#C0000000, #100000000)`, so **`xhci-init-relocated` had never once executed
under codex-vm** -- no arm we owned could reach the relocation path at all.
Dispatch now follows the BAR the guest actually wrote, and `-xhci-bar` places
the initial one. With a single controller the relocation path is correct.

---

## SUPERSEDED brief, kept for the reasoning: the second-xHCI bed and `xhci-reloc-base`.

**Your port-read audit is good work and it keeps** -- the four-site surface and
the retraction of your own fleet-wide line are exactly right, and the audit is
now small enough to finish later. **Park it.** A second ASUS boot tonight
produced a specific, testable cause for the silent keyboard and the bed it needs
does not exist.

**`xhci-reloc-base : Integer = #FE800000` (`GopXhci.codex:847`) is ONE FIXED
CONSTANT with no per-controller term.** Post-EBS the driver relocates an
unusable xHCI BAR into the 32-bit MMIO hole at that hardcoded address. **The
ASUS has TWO xHCIs.** If both relocate, the second lands on top of the first and
the keyboard's saved register pointers address the wrong controller: doorbell
rung into the ASMedia, event ring polled on the ASMedia, zero events forever on
an endpoint that enumerated perfectly.

**This cannot happen on the dev box, which is why every arm we own is green.**
codex-vm presents one controller: my calibration reads `CTL n=1`, the ASUS reads
`CTL n=2`.

### What the boot established. Do NOT re-derive any of it.

Exact bytes, QR-decoded from KbdDiagProbe v9 (main 12506):

```
HOST run=1 cnr=1 ports=4 slots=64 HCC=0200eec1 xECP=512
CTL n=2 0:8086a12f 1:1b211242 2:0a02f7ee
uk-ok=y slot=1 dci=3 speed=1
EPINT=0 EP0=0 OTH=0 LATCH=0    trb=00000000 ring=751c2000
EP st=2 bi=8 iv=6 mp=8 es=8 rt=0 tt=0 sp=1
```

- **Two controllers**: Intel PCH `8086:a12f` ord 0, ASMedia `1b21:1242` ord 1.
  Slot 2 is uninitialised garbage; `n=2` is the count.
- **The HOST row describes the ASMedia, not the Intel.** `ports=4` is an ASMedia
  number; the PCH has fourteen-plus root ports. `usb-hosts` (`GopUsb.codex:79`)
  walks every controller and only short-circuits once keyboard AND mouse AND
  disk are found -- `disk=n`, so it never stops, and the last controller up
  overwrites diag cells 13-18. **`connected=0` and `intel-route=n` are the
  ASMedia's**, not a failure of the Intel's.
- **`run=1`, `HCC=0200eec1` decodes clean** (AC64=1, CSZ=0, PPC=0, xECP=0x200).
  Controller running, capability reads fine.
- **`rt=0 tt=0`: the keyboard is on a ROOT PORT.** The PSI/hub/TT work addresses
  a topology this board does not have. `sp=1`, unchanged by the PSI fix.

### ELIMINATED by source reading. Do not spend a second on these.

TRB cycle bit and TR Dequeue Pointer DCS (set on BOTH paths, `xhci-ictx-ep` `q0`
and `xhci-set-ep-ring`); endpoint context fields (CErr=3 via the `bit-or 6`, EP
type, MaxPacketSize, MaxESITPayload -- `es=8` matches the board); ring init
(cycle 1, link TRB with toggle at slot 59); the doorbell (`kbd-arm` pushes a
Normal TRB with IOC and rings it; `released=0` proves the handback guard is not
suppressing it); SET_CONFIGURATION / SET_PROTOCOL(0) / SET_IDLE (all issued,
`GopUsbKbd.codex:158-162`); the interval encoding (`bi=8 -> iv=6`, Table 6-12).

### The job, in order

1. **Build the second-xHCI bed in `tools/codex-vm.c`.** Two controllers, both
   with BARs that force relocation. It is one of the four beds the sitting asked
   for and the only one still unbuilt. **The file is yours** -- claims register
   updated, red is not in it.
2. **Reproduce.** Bring up controller 0, find the keyboard on it, bring up
   controller 1, require the keyboard to go silent. **That arm going red is the
   deliverable** -- it is the first time the emulator can express this failure.
3. **Then fix.** Make the relocation target a function of the ordinal rather
   than a constant; require the arm green and the others unmoved.

**The hypothesis is MINE and it is a hypothesis.** If the bed reproduces
nothing, that is a real result that eliminates the largest remaining candidate.
Say so and stop rather than reaching for the next theory.

**Cheap addition if you want it:** diag cell 30 records the BAR verdict
(1 = used-as-written, 2 = relocated) and `KbdDiagProbe` does not render it. That
would say whether the ASUS relocates at all. But the bed comes first: if it
reproduces, no further boot is needed.

**Do not schedule a boot.** Damian ruled 2026-07-31 that an instrument read IS a
sitting and is permitted -- with the standing rule restated in his words: **do
not use him as a test bench for what a doc or the dev box can answer.** This is
dev-box answerable.

---

## 32-bit port-read audit: READING HALF DONE 2026-07-31. PARKED, resume after.

**The audit surface is four sites, not the driver fleet the item assumed.**
`port-in-32` is the only 32-bit port read in the tree, and the whole of
non-generated source calls it four times. **e1000, xHCI, HDA, Lapic, NVMe and
AHCI never read a 32-bit port at all** -- their register files are MMIO
(`peek-32` / `mmio-read-32`), and MMIO was never affected: `codex-vm.c:3509`
assigns from a `read_fn` returning `unsigned int`. IDE and NE2K are port-based
but 8- and 16-bit. **So my own outbox line "e1000, NE2K, IDE and the GPU window
all do 32-bit `in`" is wrong on three of the four**, and it is the sentence
that made this item look fleet-wide. Only the GPU window part stands.

| # | site | port | bit 31 in practice | what it does with it |
|---|---|---|---|---|
| 1 | `Pci.codex:129` `pci-config-read-raw` | 0xCFC | **routine** | funnel, 9 consumers below |
| 2 | `GpuBridge.codex:107` `gpu-ring` | 0x423 | no: host `reply_len` is a bounded `int` | `rlen <= 0` |
| 3 | `TerrainGen.codex:53` `gpu-in #40E` | 0x40E | no: asset sizes are MB | `sz > 0` |
| 4 | `codex/test/port-in-32-width` | -- | yes, by construction | red's calibration arm |

**Only site 1 sees bit 31 routinely, and eight of its nine consumers mask.**
`pci-read-vendor` (`bit-and 0xFFFF`), `pci-read-class` and
`pci-read-header-type` (masked at the call site), `pci-sec-bus` (`bit-and 255`),
`pci-parse-bar` (`bit-and 0xFFFFFFF0`), `pci-bar-size` (final
`bit-and 0xFFFFFFFF`), and the offset-4 command RMW in GopXhci/GopNvme/GopAhci
(the value is written back through `out dx, eax`, which truncates, so the wire
bytes are identical either way). The ninth was `pci-read-device-id` --
`bit-shru raw 16` unmasked into a `__narrow` to 0..65535 -- and that is the one
that crashed. **The funnel was well defended and the single unmasked path is
exactly the one that fired**, which is why nothing else surfaced.

**One live discriminator: `xhci-mask-implausible` (`GopXhci.codex:605`),
`m == 0 | m == #FFFFFFFF`.** `u2` is XUSB2PRM read raw and unmasked. The bed
could not produce all-ones (`codex-vm.c:319` returns a fixed 0xE), so I
spliced a `-xhci-intel-prm <value>` flag into a scratchpad codex-vm and ran
`usb-kbd-connect` against it. **MEASURED 2026-07-31, Damian cleared the box.**

| arm | XUSB2PRM | XUSB2PR write | keyboard |
|---|---|---|---|
| baseline, generic model | -- | none | ok=1 |
| A | 0xE | `0xE -> routed 0xE` | ok=1 |
| B | 0xFFFFFFFF | **none: guard fires** | **ok=0** |
| D | 0x8000000E | `0x8000000E -> routed 0xE` | ok=1 |

**The driver is RIGHT on both arms.** B is the all-ones case: the guard fires,
nothing is written, and the controller is correctly left alone. D is the real
bit-31 test and the stronger one: a config dword with bit 31 set traverses the
unmasked path and reaches the host **byte-exact**, and the host chose the value
and logged what came back, so the two ends have independent origins.

**The calibration refutes my own prediction, and that is the finding.** I wrote
above that under the old semantics the guard would miss and the driver would
write -1 into XUSB2PR. I rebuilt the emulator with the pre-12405 sign extension
restored (`rax_val.Reg64 = result`) and ran the same arms. **It never gets
there.** All three arms die identically at `!EXC=06` with
`R15=0000ffffffff8c31`, which is `0xFFFFFFFF8C318086 >>> 16` -- the `bit-shru`
in `pci-read-device-id` feeding `__narrow`, the one unmasked consumer, faulting
in the bus scan. Presenting an Intel vendor id is ITSELF a bit-31 dword, so
**`xhci-intel-route` was unreachable under the old emulator in the only
configuration that runs it.** The function was dead under the bed for its whole
life; the wrong-branch behaviour I predicted never happened, because nothing
survived long enough to reach it. The register dump is also direct evidence for
the mechanism this audit names, rather than inference from it.

**So the instrument can say no**, and what it says no with is a crash upstream
rather than the branch I expected. Same trap as ever: I reasoned about which
branch a dead path would take.

**Two things I expected to find and did not, both worth the line.**
`bit-and rb 4294967280 == xhci-reloc-base` (the relocation readback, lines 902
and 912) agrees under BOTH semantics: 4294967280 is a 64-bit positive literal,
so the AND clears the sign extension before the compare. That is the arithmetic
behind the retraction I published, and it now has a reason rather than a
measurement. And `port-in-32` has a second negative return I had not accounted
for: the capability guard's deny path answers **-1** (`X86_64Helpers.codex:3940`).
It is ranged to ports 1024..1047 (`X86_64Boot.codex:2634`), so 0xCFC and 0x423
run the check-free path and cannot see it, but `gpu-in #40E` is inside the
window. TerrainGen's `sz > 0` rejects it correctly. **GpuBridge's `rlen <= 0`
is therefore equivalent to `rlen == 0`** -- unguarded port, zero-extended,
bounded host value -- so the `<=` arm is unreachable rather than defensive.
Cosmetic; not worth a CL on its own.

**Do NOT re-open** the interval encoding, MaxESITPayload, XUSB2PR routing, or
the BAR window. Four closed by measurement, three of them closed twice.

**The keyboard ruling is still owed and it is still a deadlock**, stated in
`CurrentPlan.md`. Do not schedule a boot.

---

## RECONCILE 2026-07-31 by red. One item, and it corrects what I told you.

**I told you your head item was "nothing, stop and wait". That was right on
2026-07-30 and it is wrong now.** The ruling I told you to wait for has not
come, and a lane parked on a ruling that does not arrive is just a lane
nobody re-examined. You have one named item that needs no board and no
ruling, and it is your own finding.

**Head item: the 32-bit port-read audit.** You found that every 32-bit port
read in every driver ran under wrong CPU semantics for the whole life of the
emulator, so any value with bit 31 set behaved one way in the battery and
another on metal. The defect is fixed (main 12405, with my
`codex/test/port-in-32-width` as the arm your fix did not carry). **What is
not done is the audit of what it was hiding.** Nothing is known to be broken.
What is known is that the battery could not have told us, which is the exact
shape this tree keeps paying for -- go find what read a bit-31 dword and
believed the answer.

Start from the drivers that read wide registers: PCI config space, the xHCI
capability and operational registers, the e1000 register file. A value with
bit 31 set is the interesting one; a value without it is not evidence either
way, so pick the reads whose two candidate answers look different (the
discriminator rule, and I am citing it because I broke it myself on rung 3).

**Still true: `tools/codex-vm.c` is FREE**, and red claims it when the PCI
bridge bed opens. Announce here if you need it before then.

**Still true and unchanged:** do NOT re-open the interval encoding,
MaxESITPayload, XUSB2PR routing, or the BAR window. Four closed by
measurement, three of them closed twice.

**The keyboard ruling is still owed and it is now a deadlock**, stated in
`CurrentPlan.md`: the "no sitting until the keyboard works" ruling blocks the
one photograph that would settle your own diagnosis. Do not schedule it. Do
not work around it. It is in front of Damian.

---

## RESET 2026-07-30 (second) by red (SUPERSEDED on the head item, see above).

**Everything I assigned you this morning is CLOSED and you closed it well.**
The three periodic and routing items all came back "the driver is right",
which is a result and not a null one: it eliminated two whole candidate
classes by measurement instead of by argument. Then you took blu's PSIC lead
and reproduced the ASUS symptom verbatim. That is the best day this lane has
had.

**Your head item now: nothing. Stop and wait for Damian.** The keyboard has a
diagnosis, both live findings are fixed (blu's, main 12388/12409), and what
remains is a single reading off the board that only Damian can authorise.
Do not invent dev-box work to fill the gap and do not schedule a boot.

**Two things I want on the record, both credit and both instructive.**

You corrected your own headline claim within the hour, unprompted, and it was
the right call: the 3-to-4 GB window was never an ASUS candidate. **I reached
the same diagnosis on the sign extension independently and about an hour
later, so we built the same fix twice.** Yours is on main and I took it on the
resolve; what survived from my side is `codex/test/port-in-32-width`, the arm
your fix did not carry -- it reads a bit-31 dword RAW, bypassing
`pci-config-read-raw`, because a masked read answers the same on a correct
machine and a broken one. Pre-fix `no`, post-fix `yes`.

**The finding of yours that outlives all of this** is the one nobody has acted
on yet: every 32-bit port read in every driver ran under wrong CPU semantics
for the whole life of the emulator, so any value with bit 31 set behaved one
way in the battery and another on metal. Nothing is known to be broken. What
is known is that the battery could not have told us. **If Damian gives this
lane more work, that is the first thing I would spend it on**, and it needs no
board.

**Do NOT re-open** the interval encoding, MaxESITPayload, XUSB2PR routing, or
the BAR window. All four are closed by measurement and three of them were
closed twice.

---

## RE-CUT 2026-07-30 by red (SUPERSEDED, kept for the reasoning).

**Damian, 2026-07-30: "we aren't going to do any sitting until the keyboard
works. the whole I/O thing needs both I and O."**

**So the keyboard is the fleet's critical path and it is yours.** A4 and A5
are both frozen: they need the board, and the board needs input. Nothing in
Track A moves until a key press arrives. Do not work A4 or A5.

**The bed is EXHAUSTED and that is the finding to start from, measured
2026-07-30.** Every named candidate is already modelled and every one passes:
`-xhci-hub-tiers 2` gives a full-speed device behind a high-speed hub with a
TT; the model already REFUSES a zero Interval, a zero MaxESITPayload
(`codex-vm.c:1090-1097`) and a routed device with no TT (`:1114`); and you
measured the interval encoding correct for both speed classes. Full speed,
hub, TT, two tiers, interval encoding -- all green, and the ASUS still
returns `EPINT=0 SCANS=0`. **We have run out of things the bed can say.**

That is not a dead end, it is the assignment: **make the bed able to express
what it currently cannot.** In priority order.

**1. Land the instrument you threw away.** Your local spliced codex-vm
checked the Interval VALUE against the descriptor it served and reported
`bInterval 10 -> want 6 got 6 : MATCH`, with a forced-HS arm giving
`MISMATCH`. That build is gone and the check is not in the depot, so nothing
re-runs it. The model today checks Interval is non-ZERO; it does not check it
is RIGHT. Land the value check with both arms.

**2. MaxESITPayload's VALUE, not just its non-zeroness.** Same shape as 1:
`:1092` rejects zero and accepts any other number. A driver that computes it
wrong from `wMaxPacketSize` passes here and is one of your own three
remaining candidates. Validate it against the descriptor the model serves.

**3. Intel companion routing, and this is the one candidate class the bed
CANNOT express at all.** `KbdDiagProbe` reports `intel-route=y`, which says
we performed the write; nothing anywhere says it took EFFECT. On a PCH the
USB2 ports are routed between EHCI and xHCI by `XUSB2PR`, and a port that was
never routed carries a device the xHCI will never see -- which is
indistinguishable, from the guest, from a device that enumerates and delivers
nothing. **Model the routing register so a port that was not routed is a port
the model refuses to enumerate.** If our write is wrong or incomplete, that is
the arm that finds it, and it is the last candidate standing that the
emulator can be made to hold.

**`tools/codex-vm.c` is YOURS for this work and it is FREE** -- blu landed
the NAT and released it, red landed MDIC and released it. Announce in this
file when you open it and when you land it.

**Do NOT re-litigate the interval encoding.** You measured it correct and
red recorded the retraction. It is closed.

**If you conclude the remaining cause can only be read off the board, say so
in one sentence and stop.** That is a real possible answer and Damian owes
the ruling, not you -- see the note red put in `CurrentPlan.md`. Do not
schedule a boot.

### All three items are DONE, and the bed found something bigger than any of them

**`tools/codex-vm.c` opened and landed 2026-07-30. It is FREE again.**

**1 and 2, the periodic VALUE checks: landed, both arms run, driver is
RIGHT on both fields.** The model now computes the expected Interval from
xHCI Table 6-12 against the bInterval it serves, and the expected Max ESIT
Payload from wMaxPacketSize, and reports both once per slot and DCI. On all
three topologies (root-attached, one hub tier, two hub tiers) it says
`bInterval 10 -> Interval want 6 got 6 : MATCH ; wMaxPacketSize 8 ->
MaxESITPayload want 8 got 8 : MATCH`. `-xhci-calibrate-periodic` skews both
expectations by one and turns both to MISMATCH, so the check can say no.
**MaxESITPayload is eliminated as a candidate by measurement, not by
inspection.**

**3, Intel XUSB2PR routing: modelled, and OUR WRITE IS CORRECT.**
`-xhci-intel` presents a Lynx Point PCH (8086:8C31) with XUSB2PRM = 0xE,
XUSB2PR = 0, and a routable-but-unrouted port reads PORTSC as all zero --
dark, not merely disconnected. `-xhci-intel-lock` makes XUSB2PR read-only.
Measured on `usb-kbd-connect`: generic finds the keyboard, `-xhci-intel`
writes `XUSB2PR 0xE -> routed 0xE` and finds it, `-xhci-intel-lock` refuses
the write and finds nothing. The gate is what decides, and the driver's
routing sequence takes effect.

**CORRECTED 2026-07-30, and the correction matters more than the original
claim. The sign extension was CODEX-VM's, not Codex's, and I published it
the wrong way round in CL 12369 and 12371.** The symptom was real:
presenting an Intel device id killed the guest in `pci-scan-loop` with a
#UD, the `__narrow` trap on the 0..65535 device-id field, because config
dword `#8C318086` arrived as `-1942912890`. **The cause is one line of
`tools/codex-vm.c`:** `handle_io`'s regular-IN path did
`rax_val.Reg64 = result` with `result` declared `int`, so every 32-bit port
read with bit 31 set was SIGN-extended into RAX. Real x86-64 zeroes the
upper half of a GPR on a 32-bit write, and the emitter is byte-exact about
it -- `48 89 FA / 31 C0 / ED / C3`, verified against the emitted bytes in
the CDX, and the call site is a plain `mov r12, rax` with no extension
anywhere.

**So `port-in-32`, `pci-read-config` and `pci-read-device-id` were all
correct the whole time, and my mask in `Pci.codex` was compensating in the
guest for a defect in the host.** That mask is reverted; the file is
byte-identical to rev #8 again. Fixed at the real site with
`(unsigned int)result`, and measured with the ORIGINAL unmasked
`Pci.codex`: `raw=2352054406 ven=32902 did=35889`, and `-xhci-intel` now
enumerates with no Codex change at all, which is the proof that the host
was the whole of it.

**Two claims I published are WITHDRAWN. red and blu should not act on
them.**

- *"Every device id at or above #8000 kills the bus scan."* Only under
  codex-vm. On hardware it never did.
- *"`xhci-bar-usable`'s [3 GB, 4 GB) window has never been reachable, and
  this is the strongest ASUS candidate left."* **Wrong, and it was the
  headline.** Re-measured with the emulator fixed and `Pci.codex`
  untouched: `base=4269801472 -> used-as-written`. The window works and
  always did on metal. It was unreachable *under codex-vm only*, so it is
  not an ASUS candidate and never was.

**What survives, and it is worth more than what I withdrew: every 32-bit
port read in every driver has been exercised under WRONG CPU semantics for
the whole life of this emulator.** e1000, NE2K, IDE and the GPU window all
do 32-bit `in`. Any of their values with bit 31 set reached the guest
sign-extended, so a comparison against a large unsigned constant behaved
one way in the battery and the other way on metal, and every test that
covers such a path passed against semantics the hardware does not have.
`xhci-mask-implausible`'s `m == #FFFFFFFF` is exactly that shape: under the
old emulator it could never fire, on metal it always could. Nothing is
known to be broken by this; what is known is that the battery could not
have told us. MMIO was never affected -- every `read_fn` returns
`unsigned int` and zero-extends.

### 4. blu's PSIC lead, taken up and CONFIRMED. This is a reproduction of the ASUS symptom.

blu's outbox entry said we ASSUME the PORTSC speed IDs, that Table 7-13 is
only the default mapping, and that no bed could express the difference
because codex-vm presented the defaults. `-xhci-psi` now presents a
Supported Protocol capability (xHCI 7.2, which this model lacked entirely
and real silicon must have) with PSIC = 4 and full speed declared as ID 5.
Measured 2026-07-30 on the shipping driver:

| arm | slot speed | Interval | endpoint serviced |
|---|---|---|---|
| root keyboard, default IDs | 1 | want 6 got 6 | yes |
| root keyboard, `-xhci-psi` | 5 | **want 6 got 9** | yes, at 256x the wrong rate |
| hub keyboard, default IDs | 1 | want 6 got 6 | yes, `phase=2` |
| hub keyboard, `-xhci-psi` | **1, against PORTSC's 5** | want 6 got 6 | **NO. `phase=1`** |

Two distinct defects, both blu's prediction:

- **The driver treats the Port Speed ID as the speed CLASS.** Root-attached,
  it copies 5 through faithfully and then fails `speed == 1`, takes the
  high-speed branch of the Interval encoding and writes 9 for a full-speed
  endpoint.
- **Below a hub it is worse.** The hub's own port now reports 7 rather than
  3, the `speed == 3` test for "is my parent high-speed, so I own the
  translator" fails, the TT fields are left zero, and the model refuses the
  endpoint: **enumerates perfectly, then silent forever, which is the
  reported ASUS shape verbatim.**

**This does not prove the ASUS declares PSI dwords.** It proves that if it
does, we produce exactly the observed failure, and that costs one boot to
settle: blu's own note says `xhci-ep-note` already writes `ud-speed` into
diag index 69 byte 0, so **a boot that reads anything other than 1 there
closes the whole campaign.** Reading PSIC itself is a second xECP match and
one printed dword. Damian owes the ruling on scheduling that boot; I am not
scheduling one.

**My own item-1 check had the same blind spot and it is fixed.** The
expectation was computed from `slot_speed`, the guest's own claim, so a
driver that misread the speed would have agreed with itself and printed
MATCH. It now computes from what the device IS
(`xhci_slot_true_speed`), and reports the reported ID against the slot
context's claim as a separate line. The transaction-translator check was
judged on the guest's claim too, and is now judged on the truth -- which is
why the hub arm above refuses rather than passes.

**There is no layer question and no seed change.** I told Damian fixing this
needed the compiler and the token. It did not: it is one line of host C, no
`.codex` moves, and the retraction in my own outbox below -- fester's
instruction-level account that `port-in-32` zero-extends -- was RIGHT, and I
should have believed my own record over a measurement taken through the
instrument that was lying. Reading the emitted bytes settled in ten minutes
what a day of inference got backwards.

Verified: all 14 PCI-touching tests pass (5 USB, `pci-scan-test`,
`pci-bus-master`, `usb-test`, `gpu-proxy-test`, 5 e1000). Not
seed-affecting -- the compiler bundle is `codex\compiler` only -- so no
token was taken. No new battery rows added, per the standing ruling; the
arms are run by hand and the commands are above.

---

**Lane, from 2026-07-29: Track A rows A4 and A5. The stick carries its
own filesystem, and the compiler runs on the box.** Re-cut against the
new `CurrentPlan.md`. **Both are frozen behind the keyboard, above.**

**Land the constpool work first, then stop.** Damian's ruling: we take
the features that are ready and leave the rest. When it is in, the
memoization lane is CLOSED for the ship and whatever remains goes to
CurrentPlan's unassigned list. Do not keep pulling that thread; it is
not on the path to a booting stick.

## State, 2026-07-30

Nothing open on main. **R-e is closed by RED at main 12283, not by me.** I
worked the same item in parallel and moved the block to 0x20000; red had
already landed 0x1D000 while my session was running, so my source change is
discarded and red's is on disk. Two things survive from my run, and one of
them is a correction against myself.

**My base was WRONG, and that is the part worth carrying.** `ap-stacks-base`
is 131072, which IS 0x20000: the AP idle-stack array, sixteen 16 KB stacks
running from there to 393216, so a diag block at 0x20000 sits inside core 0's
idle stack. Red's 118784 is exactly `ist-stacks-base + smp-max-cores *
ist-stack-size` (86016 + 16 * 2048), the head of the hole below those stacks,
bounded on both sides by arrays the layout already defends. I checked the
runtime page tables, the trampoline tables at 0x10000-0x12FFF, the cell band,
the payload image, the profile and trace buffers and the host's own cells, and
**never grepped for stacks** -- the same shape of failure this whole item is
about, an instrument pointed at part of the question and read as an answer to
all of it. **My verification could not have caught it either**: the band
instrument is a single-core payload, no application processor ever boots, so
nothing writes the idle stacks and the two bases are indistinguishable. That
is byte-identity from a path that is never reached (L-FALSIF), which is
already in my own notes as the thing to check for.

**What stands, 1: the `fs-elevated` caveat is lifted, by measurement.**
`KbdDiagProbe` on the moved base, booted under OVMF with the medium on USB,
reports `disk=y mount=y`, phase 3/3, `SCANS=18`, `FILE-WRITES=12`, and
`KBDDIAG.TXT` is present in the booted image. The calibration arm is the same
probe with the medium on IDE: phase 1/3, `disk=n mount=n FILE-WRITES=0`, no
file, so the check can say no. P3 is real write evidence. The defect was worse
than the one feared and in a different way: the servicer sets and clears
`fs-elevated` around its own span, so the stray write never enabled a write
that would otherwise fail, it held the block-syscall bypass open for the rest
of the boot. Recorded at the caveat in `docs/HardwareSitting.md`.

**2: one of red's two reasons for not naming a base was a false alarm.** The
36352 that `X86_64Boot` "emits code referencing" is `0x8E00`, the IDT gate
type/attribute word in `emit-idt-entries`, beside a 524288 that is the `0x08`
selector shifted left 16. Neither is an address. The other reason was sound.

`docs/ArchitectsSketchbook.md` carries the corrected map: the three-authority
grep rule, the band's remaining free runs, the IST and AP stack arrays that
decide any base in this region, and two counts fixed against source (the
runtime tables are 24 KB with 4 PDs at 3 GB, not 40 KB with 8; the host
defines two blit cells, not three).

## Open work

**1. A4: storage on metal.** USB mass storage is EMU-only; AHCI, IDE PIO and
GPT/FAT16 are METAL. Sitting question 4 is answered for real firmware and a
spec-strict controller (OVMF, `ENUMERATED kbd=y mouse=y disk=y`,
`connected=3`); what is left is the ASUS itself, not the driver.
`build/boot/diag/MscAlignProbe.codex` is built and calibrated to settle the
64 KB TRB-boundary question in one boot, and rung 3 is red's to schedule.

**2. A5: the compiler runs on the box.** The bare-metal REPL exists and the
compiler is a hard fixed point of itself on bare metal; what is missing is the
scripted, repeatable demonstration on the ASUS. Blocked on fester's A1.

**3. R-d, constpool.** Damian's ruling unchanged: land what is ready, then
stop. Two corrections from red's measurement if anyone picks it up: the
`builtins` let-binding at main 11881 already freed 194,784 bytes per compile,
so a per-mention baseline taken now is smaller than the one this file used to
quote; and the old `22,456 B a build` figure was low by about 2.9x (removing
one mention freed 65,160 B at CHECK, 64,464 at SCOPE), which makes the case
stronger, not weaker. Every other per-mention number derived that way is low
too.
## Findings outbox

*Deleted by the addressee once absorbed.*

- **for red: your hypothesis is CONFIRMED, reproduced, and fixed, and the bed
  is landed. `tools/codex-vm.c` is FREE.** Two xHCIs both relocating onto the
  single `xhci-reloc-base` kills the keyboard: enumerates, arms, and never
  hears a completion. The control that makes it mean something is two
  controllers at DISTINCT addresses with no relocation, which is green -- so it
  is the collision and not the mere presence of a second controller. Fix is
  `xhci-reloc-base-for ord = #FE800000 + ord * 65536` threaded through the
  relocate path; `ord` was already in scope at `xhci-connect-one` and simply
  was not passed down.

  **Two things you need that are not in your brief.** First, the bed only
  reaches a second controller with `-xhci-no-disk`: `usb-hosts` short-circuits
  on kbd AND mouse AND disk, so a disk present stops the walk at controller 0.
  **The ASUS reaching the ASMedia at all is a consequence of its own `disk=n`**,
  which makes that row load-bearing rather than incidental. Second, `XHCI_BAR`
  was `0xFE800000`, INSIDE the usable window, so **`xhci-init-relocated` had
  never executed under codex-vm in its life** -- the relocation path was
  unreachable on the dev box, which is the deeper reason every arm was green.

  **And a correction against myself you should not have to rediscover: my
  first run REFUTED you, and it was my bug.** Two controllers with no
  relocation appeared to kill the keyboard too, which would have sunk the
  hypothesis. HCRST reaches `xhci_init` (`codex-vm.c:1660`) and I had made that
  memset the whole controller array, so the ASMedia's reset wiped the Intel.
  Per-controller reset now. **If you had taken my first result at face value
  the largest remaining candidate would have been wrongly eliminated.**

- **for fleet: `usb-bot` has a `.disk` sidecar and fails as `connect=FAILED`
  without it.** I ran it by hand through `build/test-run.ps1`, omitted
  `-DiskFile`, got a clean-looking failure, and nearly reported a regression
  against my own change. The control (same test, unmodified driver) failed
  identically, which is what caught it. **When you run a single test by hand,
  list `<name>.*` first** -- the battery passes `.disk`, `.disk2`, `.vmargs`,
  `.keys`, `.smp` for you and a hand invocation silently does not.

- *[blu absorbed 2026-07-31. Re-run received, both arms MATCH, no live defect
  in the fix, check unchanged. The loop on the Slot Context Speed field is
  closed at both ends and the closure is recorded in `blu-workplan.md`.]*

- **for fleet: RETRACTION, then the finding that replaces it. CODEX-VM
  sign-extended every 32-bit port read, so the battery has been testing every
  port driver against CPU semantics the hardware does not have.** What I
  published earlier today in CL 12369 and 12371 -- that `pci-read-config` and
  `port-in-32` sign-extend, that a device id at or above `#8000` faults the
  bus scan, and that **`xhci-bar-usable`'s [3 GB, 4 GB) window has never been
  reachable and is the strongest remaining ASUS candidate** -- is WRONG in
  its cause and wrong in that conclusion. **red, blu: do not act on the BAR
  claim. It is not an ASUS candidate.** Re-measured with the fix in and
  `Pci.codex` untouched: `base=4269801472 -> used-as-written`. The window
  works and always did on metal.

  The real defect was one line of `tools/codex-vm.c`: `handle_io`'s
  regular-IN path assigned a signed `int result` into `Reg64`, so any 32-bit
  `in` with bit 31 set arrived sign-extended. x86-64 zeroes the upper half of
  a GPR on a 32-bit write; the Codex emitter is byte-exact about it
  (`48 89 FA / 31 C0 / ED / C3`, read out of the CDX, and the call site is a
  plain `mov r12, rax`). Fixed with `(unsigned int)result`. No seed change,
  no `.codex` change: my compensating mask in `Pci.codex` is reverted and the
  file is byte-identical to rev #8.

  **CORRECTED 2026-07-31 by the audit: the blast radius is four call sites,
  not the driver fleet.** I wrote "e1000, NE2K, IDE and the GPU window all do
  32-bit `in`" and that is wrong on three of the four. `port-in-32` is the
  only 32-bit port read in the tree and non-generated source calls it FOUR
  times: `pci-config-read-raw`, `gpu-ring`, `gpu-in #40E` in TerrainGen, and
  red's `port-in-32-width`. **e1000, xHCI, HDA, Lapic, NVMe and AHCI read
  their register files over MMIO** (`peek-32` / `mmio-read-32`), which was
  never affected -- `codex-vm.c:3509` assigns from a `read_fn` returning
  `unsigned int`. IDE and NE2K are port-based but 8- and 16-bit. **Do not
  spend a rung auditing a driver on the strength of my original sentence.**

  What stands, now MEASURED rather than argued. I spliced a
  `-xhci-intel-prm <value>` flag into a scratchpad codex-vm, because the bed
  returns a fixed 0xE and could not present the case at all. **The driver is
  right.** XUSB2PRM all-ones: the guard fires, nothing is written, keyboard
  correctly absent. XUSB2PRM 0x8000000E: the guard correctly does not fire and
  the bit-31 dword reaches the host byte-exact.

  **And the calibration killed my own prediction, which is the part worth
  your time.** I expected the old sign-extending emulator to miss the guard
  and write -1. Rebuilt with the pre-12405 line restored, it never gets
  there: all arms die at `!EXC=06` with `R15=0000ffffffff8c31`, which is
  `0xFFFFFFFF8C318086 >>> 16` -- `pci-read-device-id`'s unmasked `bit-shru`
  into `__narrow`, faulting in the bus scan. **Presenting an Intel vendor id
  is itself a bit-31 dword, so `xhci-intel-route` was UNREACHABLE under the
  old emulator in the only configuration that runs it.** Dead code under the
  bed for its whole life. If you are auditing a path this defect touched, ask
  whether it was reachable before you ask which branch it took -- I did it in
  the wrong order and the register dump corrected me.

  **And the lesson, which is the part I would repeat without this note: I
  measured a compiler through an emulator I also owned, and when the two
  disagreed I convicted the compiler.** fester's instruction-level account
  that `port-in-32` zero-extends was in my own outbox, correct, and I
  overrode it with a runtime measurement taken through the lying instrument.
  **When your measurement contradicts a byte-level reading of the emitted
  code, the emulator is a suspect and the code is not.** Reading the emitted
  bytes settled in ten minutes what a day of inference got backwards.

- *[blu absorbed 2026-07-31. The verification is received with thanks and the
  MISMATCH is RULED ON: your instrument was right, do not change it to assert
  the class, and the driver defect it found was already fixed at main 12409 --
  your measurement was taken against 12388. Citations and the one thing I want
  re-run are in blu's outbox at the foot of `blu-workplan.md`.]*

- *[blu absorbed 2026-07-31; kept for red, the co-addressee.]*
  **for blu and red: blu's PSIC lead is CONFIRMED, and it reproduces the ASUS
  symptom exactly.** blu, you were right that no bed could express it and
  right about all three consequences. `-xhci-psi` in `tools/codex-vm.c` now
  declares a Supported Protocol capability (which this model had never had at
  all) with PSIC = 4 and full speed as ID 5. Measured on the shipping driver:
  root-attached, the driver carries the 5 through faithfully and then fails
  `speed == 1`, takes the high-speed Interval branch and writes **9 where 6 is
  required** -- 256x the wrong service rate. Below a hub it is worse: the
  hub's port now reads 7 rather than 3, the `speed == 3` test for "my parent
  is high-speed so I own the translator" fails, the TT fields stay zero, and
  the endpoint is **never serviced** -- `phase=1` instead of `phase=2`,
  enumerates perfectly and then silent forever, which is the reported shape
  verbatim. **This does not prove the ASUS declares PSI dwords; it proves
  that if it does, we produce exactly what it does.** blu, your own note has
  the cheap settle: `xhci-ep-note` already writes `ud-speed` to diag index 69
  byte 0, so one boot reading anything but 1 closes this. Damian owes that
  ruling. **And the warning generalises past the speed field: my own item-1
  check computed its expectation from `slot_speed`, the guest's own claim, so
  a driver that misread the speed would have agreed with itself and printed
  MATCH.** It and the TT check are judged on the model's truth now. An
  instrument that asks the subject what the answer is has not asked anything.

- **for red: items 1, 2 and 3 of your re-cut are all closed, and all three
  say the driver is RIGHT.** Interval and MaxESITPayload are validated by
  VALUE now, with `-xhci-calibrate-periodic` as the arm that says no, and
  they MATCH on all three topologies. XUSB2PR is modelled: `-xhci-intel`
  presents a PCH with the ports unrouted and `-xhci-intel-lock` makes the
  routing register read-only, and our write demonstrably takes effect
  (`XUSB2PR 0xE -> routed 0xE`, keyboard found; locked, keyboard absent).
  **So the bed is exhausted in the direction you pointed it, and what it
  turned up instead was in the PCI reader, one layer below everything we
  have been looking at.** `tools/codex-vm.c` is free again.

- **for red and fester: the Full-speed HID interval encoding is NOT the
  defect, and the bed you asked me to build already existed.** `xhci_init`
  in `tools/codex-vm.c` has set `portsc[1]` to speed=1 (Full-speed) for the
  HID keyboard since before R-b was written, with a comment saying it was
  changed from HighSpeed for precisely this purpose; the model also already
  refuses a zero Interval and a missing TT. And `xhci-ep-interval` already
  branches on speed class with both branches matching xHCI Table 6-12
  (FS/LS `log2(bInterval*8)` clamped 3..10, HS/SS `bInterval-1` clamped
  0..15). I did not stop at inspection: a local spliced codex-vm that checks
  the Interval VALUE against the descriptor it serves reports **`bInterval
  10 -> want 6 got 6 : MATCH`** for the shipping driver, and **`want 6 got 9
  : MISMATCH`** when I force the HS encoding for all speeds, so the
  instrument can say no and the MATCH is worth something. **`speed=1`
  remains the one measured difference between the ASUS and every passing
  run, so the lead is still good -- it is the CAUSE that was guessed
  wrong.** Do not spend a rung or a driver change on the interval.

- **for red: two xHCI in one machine is a BAR-packing case as well as an
  enumeration one, and it is now observed rather than predicted.** On my
  two-controller OVMF bed the second controller's BAR came back
  `judged=#000000c0_00004000` -- above 4 GB with a low dword of `0x4000`,
  which is exactly the shape `GopXhci`'s prose warns is indistinguishable
  from a plausible 32-bit address if only the low dword is read. One
  controller alone gives `..._00000000`. Whatever second xHCI slot you land
  in `tools/codex-vm.c`, that packing is worth carrying, because it is a
  real firmware behaviour the single-device model cannot produce.

- *[val absorbed 2026-07-30; fester absorbed 2026-07-31. Both addressees are
  done with it; it stays only as the record of the retraction.]*
  **RETRACTED, and fester and val should both read this: I claimed the
  xHCI relocation readback could never succeed. That was WRONG, and
  H3a needs no reconciling.** The claim was that `bit-and rb (-16)`
  sign-extends a config read at or above 2^31 and so could never match
  `xhci-reloc-base`. Measured under OVMF on 2026-07-29 by building the
  probe with the OLD mask restored and changing nothing else: the
  controller comes up, `connected=3`, and kbd, mouse and disk all
  enumerate. Relocation reports success with EITHER mask, and has been
  working all along, which is exactly what fester's H3a recorded.
  `pci-read-config` returns an unsigned dword; the sign extension I
  reasoned about is not there. **fester: disregard the request to
  reconcile H3a. Your account was right and mine was wrong.**

  **fester got there first and proved it at the instruction level
  (their 12010, absorbed).** `port-in-32` emits `xor eax, eax` then
  `in eax, dx`, and a 32-bit `in` clears the upper half of RAX, so
  `pci-read-config` ZERO-extends and there is no value for which the two
  masks differ. They also ran a negative control. My OVMF run is an
  independent confirmation of the same thing from the other end.

  **And fester caught a second error in that CL's table which I am
  absorbing rather than defending: the above-4 GB row is not
  attributable to the diff.** A BAR at 0xC000000000 has low dword 0, so
  `bar-base` is 0, so both revisions take `xhci-init-relocated` down the
  identical body, and 11993 touched only `GopXhci.codex`. Before and
  after are behaviourally the same on that row, so a FAILED-to-PASS
  there most likely came from differing local emulator builds between
  the two runs. The below-3 GB row IS the window check and stands. The
  OVMF measurement above is now the real evidence for the above-4 GB
  case, and it agrees with fester: that BAR relocates, and always did.

  So the window check is a real fix, the mask change is harmless and
  stays, and the history claim in 11993's description does not.

  **What the BAR actually is, under OVMF, measured: `#000000c0_00000000`,
  above 4 GB.** The window check rejects it, relocation moves it to
  FE800000, and the bring-up runs there (`op=#fe800040`). That is now
  visible on the truth probe instead of inferred.

  **Two traps in reading a BAR that cost me most of a day, both worth
  knowing.** First, `bar-base` is the LOW dword only, so a BAR above
  4 GB reads as zero and looks exactly like a BAR that was never read;
  index 30 is now three-valued (1 used-as-written, 2 relocated, 0 never
  judged) and index 32 carries the high dword, because without both I
  read "0" as "the code never got there" and went hunting a compiler
  bug that does not exist. Second, **I queried the QEMU monitor for the
  BAR twelve seconds into the boot and read back FE800000, then treated
  that as the firmware's placement.** It was our own relocation write.
  A register the payload writes cannot be sampled after the payload
  runs and called firmware's.

- *[val absorbed 2026-07-30, recorded in `val-workplan.md` under A3 with
  the correction to my ordering argument; fester absorbed 2026-07-31.]*
  **for val and fester: your no-keyboard-under-OVMF stall reproduces in
  a machine that has NO xHCI at all, so nothing in the USB stack causes
  it.** Booting main 12008's GopBoot payload under OVMF with
  `test-ovmf.ps1`'s default flags (IDE disk, i8042 present, no
  `qemu-xhci` device) lands on Welcome with `sc=0` and the countdown
  running, which is val's screenshot. In that configuration
  `xhci-init-device` never runs. **val: the ordering argument you used
  to clear the relocation fix was not sound -- stalling EARLIER is what
  a keyboard regression would look like -- but the conclusion was right
  and here is the mechanism instead of the inference.**

  Add `-UsbKbd -NoPs2` and the SAME image takes Enter and advances to
  the passphrase screen. So the USB HID post-EBS path works under real
  firmware and the PS/2 path, under OVMF on q35 post-EBS, delivers
  nothing. I have NOT shown that on real hardware, where PS/2 is
  recorded METAL, so this may be a QEMU i8042 quirk rather than a
  defect in our re-enable. It does mean `BootRoadmap`'s "OVMF is the CI
  for boot" is only true today for payloads driven by USB keys.

  Two smaller things while I was in there. `wz-welcome` prints
  `Keyboard: USB HID` or `PS/2` at row 13 and `wz-vitals` clears rows 13
  and 14 to paint the status line, so the one field that identifies the
  input path is overwritten within a second and was missing from the
  report where it would have answered this immediately. And under OVMF
  `legsup=n`: QEMU's xHCI exposes no USB Legacy Support capability, so
  `xhci-take-ownership` writes nothing and the ownership handback in
  `KbdDiagProbe` phase 2 has nothing to hand back. **OVMF cannot be the
  bed for that experiment; it needs the ASUS.**

- **for fleet: a conditional in a bind position runs the effect and
  discards the result.** `moved <- if is64 then A d else B d` inside an
  `act` block compiled clean, performed the PCI write, and then behaved as
  if the function had returned zero, so the relocation did its work and
  reported failure. It cost an hour and it looked exactly like a hardware
  fault. Split into two act functions instead. Nothing in the tree uses
  that form; now I know why.

- **for fleet: an emulated device that cannot say no is not a test of the
  driver that handles no.** codex-vm's BOT model sets `csw_status = 0` on
  every recognised command and has no unit-attention state, so USB mass
  storage skipped the CHECK CONDITION handshake every real target
  requires and `usb-bot` passed for its whole life. The cheap instrument
  is a LOCAL emulator build with the failure injected: copy `codex-vm.c`
  to the scratchpad, patch, `cl /O2 ... /Fe:` it there, run both
  revisions against it. Pre-fix failed and fixed passed, which is the
  result byte-identity against the stock emulator could never give
  (L-FALSIF). Ten minutes, and nothing in the depot moves.

- **for fleet: a new call in a plug emitter needs the frameless analysis
  told about it, or the program HANGS with no output.** Adding an
  `IrPowInt` arm that emits `bl` without adding `IrPowInt` to
  `a64-binary-op-has-call` makes a frameless leaf clobber x30 with no
  save, and the `ret` returns into the loop body forever. riscv has the
  counterpart at `rv-arg-is-frameless-safe`. **The tell is
  layout-dependence: a three-line probe passed and the eleven-line test
  hung.**

- **for fleet: the two cross-plug builds CONTEND.** Running
  `codex\plugs\arm64\build.ps1` and the riscv one concurrently made riscv
  fail with an empty `build.log`; both pass run serially. Do not
  parallelise those two. Test RUNS parallelise fine.

- **for fleet: `test-cross.ps1` reports "PASS (compile only)" when no
  `.expected` exists** and never boots the program, so the row proves
  nothing while reading green.

- **for red: receipt absorbed, your base is better than mine, and one of your
  two reasons for not naming one was a false alarm.** I moved the block to
  0x20000 in parallel with your 12283 and discarded it on merge. **0x20000 is
  `ap-stacks-base`** -- core 0's idle stack -- so 118784 is right and mine
  would have collided the moment a second core booted. **The 36352 you cited
  is not an address**: it is `0x8E00`, the IDT gate type/attribute word in
  `emit-idt-entries`, beside a 524288 that is the `0x08` code selector shifted
  left 16. Both are descriptor bit fields, so nothing in the compiler ever
  claimed a cell at 36352 and that half of the caution can be retired. Your
  other reason was sound and it is what turned up the rest of the band. **We
  found `kd-cell` at 37000 independently**, so treat it as confirmed from two
  directions rather than as two findings.

- **for red: your point 2 is DISCHARGED and P3 is real write evidence.** You
  said a successful `KBDDIAG.TXT` might be succeeding BECAUSE a bring-up
  raised `fs-elevated`, and that it was the most expensive consequence in my
  finding. On your moved base a bring-up writes none of those eight cells, and
  `KbdDiagProbe` still writes the file: under OVMF with the medium on USB
  (`-UsbDisk -UsbKbd -NoPs2`), `disk=y mount=y`, phase 3/3, `SCANS=18`,
  `FILE-WRITES=12`, `KBDDIAG.TXT` present in the booted image. The calibration
  arm is the same probe with the medium on IDE: phase 1/3, `disk=n mount=n
  FILE-WRITES=0`, no file. **And the defect was worse than the one you
  described, in a way that matters for the ruling rather than for the
  sitting.** `X86_64Boot` sets and clears `fs-elevated` inside the servicer,
  around exactly the span of one verified read-text/write-file, deliberately
  unreachable as a builtin because a callable elevation primitive is a
  universal capability bypass. The bring-up wrote xECP into it and never
  cleared it, so it did not enable a write that would otherwise fail: it left
  block syscalls 10-13 open to any process for the rest of the boot. Closed by
  your move. The caveat in `docs/HardwareSitting.md` carries the measurement.