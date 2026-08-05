# The Device Emulation Catalog

*Opened 2026-07-29 by red, on Damian's direction. Status: the convention
is settled and the catalog below is measured; the queue at the end is
open work.*

## Why this exists

Damian, 2026-07-29:

> why don't we make it more official than a scratchpad. there will
> undoubtedly be more drivers and classes of drivers to emulate first,
> hardwaresit second, and it would be nice to keep the catalog for
> regression testing.

The immediate cause was the e1000e NIC driver. codex-vm emulates no
Intel NIC, so the driver landed with the whole of its device path never
executed, and the only route to exercising it was reek's scratchpad
recipe: copy `codex-vm.c` somewhere temporary, patch a model in, build
it there. That works, and it throws the model away afterwards. **The
model is the expensive part and the reusable part.**

So the order of operations for every driver from here is **emulate
first, hardware-sit second**. The hardware sitting is the scarcest
device on the bus (L-HUMAN) and must not be spent discovering things a
model would have caught. The catalog is what makes the model outlive the
sitting: it becomes a regression test that runs in the battery forever.

## The machinery already exists. Do not build a second one.

This is the part to read before proposing anything (L-READ). Three
pieces are already in the tree and they compose into exactly what is
wanted:

1. **codex-vm's device models.** `pci_add_device(vendor, device, class,
   subclass, progif, bar0, irq)` at `tools/codex-vm.c:262` registers a
   PCI function; the model is a pair of BAR read and write handlers
   beside it. Three are registered today.
2. **The `.vmargs` sidecar.** A per-test file of extra codex-vm flags,
   for tests whose subject is the MACHINE rather than the program.
   `check-sidecars.ps1` already validates it and `test-run.ps1` already
   passes it.
3. **Flags that change the machine.** 45 today, and some already do
   exactly the job this document is about: `-xhci-no-root-kbd` removes a
   device so the bus walk has to find another route, and `-board-mmio`
   moves a peripheral window.

**A new device model is therefore not new infrastructure.** It is a
registration, two handlers, one or more fault flags, a row in the table
below, and a test with a `.vmargs`. Anything proposing more than that
should be read sceptically.

## The rule that separates a test from a rubber stamp

reek, 2026-07-29, on USB mass storage:

> an emulated device that cannot say no is not a test of the driver that
> handles no.

codex-vm's BOT model set `csw_status = 0` on every recognised command
and had no unit-attention state, so USB mass storage skipped the CHECK
CONDITION handshake every real target requires, and `usb-bot` passed for
its whole life while proving nothing about the failure path.

**So a model that can only succeed is not admitted to this catalog.**
Every entry must be able to refuse, and the refusals it can produce are
part of its specification, listed in the table. A driver's error
handling is the half most likely to be wrong and the half a compliant
model will never touch.

The mirror of the rule is how an entry earns its row: break the driver's
handling of a fault and require exactly the asserting test rows to move.

## The honest limit, and it is a real one

**A model is not an independent oracle.** The model and the driver are
usually written by the same agent from the same reading of the same
datasheet, so a misreading passes both and the test is green by
construction. This is L-ORACLE in its most seductive form, because a
device model feels like ground truth and is not.

What a model does prove: that the driver's own logic is
self-consistent, that its state machine advances, that it survives the
failures the model can produce, and that a later change did not break
any of it. That is a great deal and it is worth the cost. What it does
not prove is that either party read the silicon correctly.

**Therefore the hardware sitting stays the referee** (L-REFEREE), and a
row in this catalog is never reported as hardware validation. Where a
model's behaviour is taken from something other than our own reading of
the spec, say so in its row, because that is the difference between a
mirror and an oracle.

## The catalog

Measured 2026-07-29 against `tools/codex-vm.c`. **Re-measure before
quoting** (L-COUNT).

Fidelity is about the MODEL against the silicon, and is a separate axis
from `OsHardwareRoadmap`'s METAL/EMU verdicts, which are about the
DRIVER against hardware.

| Device | PCI id | BAR | Fidelity | Can it say no? |
|---|---|---|---|---|
| Bochs VGA / GOP framebuffer | 1234:1111 | 0xFD000000 | display only, in-RAM, no MMIO trap. **`-gop-stride N` reports a scanline wider than the visible width**, which every panel we have measured does and no bed here could produce before 2026-07-29. It sets PixelsPerScanLine at mode-info +32, which is the field `cdx-to-pe.ps1`'s stub reads into the handoff block at +0x20 (the deleted `option_a_stub.asm` read the same field into its `CELL_STRIDE`), and it survives a guest `SetMode` because padding is a panel property rather than a mode choice. NOT modelled: the host-side triangle rasterizer and its post passes, which address rows by visible width and **refuse a padded stride outright** rather than shear; a bare-metal (non-UEFI) guest, which is handed width and height at GPA 0x7C4/0x7C8 and no stride cell at all | **yes.** A stride not wider than the mode, or one whose framebuffer would exceed `GOP_FB_SIZE`, is refused by name with the byte count rather than clamped into something that looks like it worked |
| NEC xHCI USB 3.x | 1033:0194 | 0xFE800000 | mass storage, HID keyboard, UVC camera, hubs. **Root port count is variable (`-xhci-ports N`, up to 32)**; the four modelled devices sit on ports 1-4 and everything above is an empty POWERED port, which is what a real wide controller reports. It reported four ports until 2026-08-03, and QEMU reports eight | partial, and less than it looks, but no longer only on the happy path. **`-usb-cfgval N` makes the storage device number its configuration N and REFUSE any other value** (USB 2.0 9.4.7: a request error on a control pipe is a stall); **`-usb-setcfg-fault N` answers SET_CONFIGURATION with completion code N** whatever is sent; `-xhci-no-root-kbd` removes a device. **CLOSED 2026-08-03, and it was the entry that named this rule.** The BOT model now presents the power-on UNIT ATTENTION every conforming target presents: the first command after a controller reset answers CHECK CONDITION, `REQUEST_SENSE` returns sense key 0x06 / ASC 0x29 rather than 18 zero bytes, and reading it is what clears the condition. `-usb-no-unit-attention` restores the old always-ready target |
| Intel HDA audio | 8086:2668 | 0xFE000000 | host waveOut | no |
| Intel e1000e NIC | 8086:15B8 | 0xFE400000 | **absent unless a flag selects it.** CTRL with self-clearing RST, STATUS.LU gated on CTRL.SLU, RAL/RAH with the AV bit, both descriptor rings walked out of guest memory, canned frame injection into the receive ring, transmit descriptors consumed with DD written back. NOT modelled: interrupts (the driver polls), multi-descriptor frames, checksum offload, PHY registers, statistics, and a failed reset does not otherwise disable the part the way wedged silicon would | **yes, four ways.** `-e1000-no-reset` holds RST set, `-e1000-no-link` never raises LU, `-e1000-no-mac` clears the AV bit, `-e1000-no-tx-dd` never reports a transmit done. `-e1000-inject N` sets how many frames arrive. Each of the four was run against the driver and the first found a real defect: the reset verdict was computed and discarded (fixed, CL 12079) |
| NE2000 NIC | ISA, not PCI | ports 0x300 | exists nowhere outside codex-vm; no real machine has had one for twenty years | no |
| IDE disk, HPET, IOAPIC, LAPIC, PS/2, CMOS RTC, PC speaker, UEFI firmware | not PCI | see OperatorsManual | varies | no |

**Every BAR above is inside 3 to 4 GB, and that is load-bearing rather
than incidental.** The runtime page tables map 0 to 3 GB identity as RAM
(the heap and stack arena), one directory for 3 to 4 GB as devices, and
nothing above 4 GB. A model placed outside that window is testing the
guest's page tables, not the driver. It is also why the emulated case
cannot on its own exercise a driver's handling of a badly placed BAR:
firmware on a real box picks the address, and OVMF was measured putting
a NIC BAR at 0x81060000, below the window entirely.

## Adding an entry

1. Register the function with `pci_add_device` and give it a BAR inside
   3 to 4 GB.
2. Implement the BAR read and write handlers, and any DMA the device
   does on its own initiative. A descriptor-ring device fetches its own
   descriptors, so the model must walk guest memory the way the silicon
   would.
3. **Give it at least one way to refuse**, as a flag. Without this the
   entry does not belong here.
4. Add the row to the table above, including what it cannot do. An entry
   that overstates its fidelity is worse than no entry, because the
   driver author will stop looking.
5. Write the test, with a `.vmargs` selecting the model and any fault.
6. Sabotage-check it: break the driver's handling and require exactly
   the asserting rows to move.

## Queue

**1. e1000e, Intel gigabit Ethernet. LANDED, and it paid for itself on the
first run.** Model in `tools/codex-vm.c`, tests `codex/test/e1000-bringup`
(nominal, one frame injected) and `codex/test/e1000-reset-wedged` (the
refusal, plus the heap cost of refusing). The driver's reset, link
bring-up, station address read, receive ring and transmit ring have all
now executed, and the received bytes match the injected frame.

**What it found immediately:** `e1000-init-at` computed the reset verdict
and threw it away, so a part wedged in reset came back as a healthy device
with a valid address and a working ring pair. The fuel cap had always
worked; nothing read its answer. Fixed in CL 12079, and the reset moved
ahead of the ring allocations in the same change so a refused bring-up no
longer leaks about 66 KB permanently. That is the rule in this document
earning its keep on its first entry: the defect was in the failure path,
and no compliant model would ever have touched it.

Still open from the same session, deliberately not changed: the
`e1000-link-up` result is discarded the same way. A NIC with no carrier is
working hardware with an unplugged cable, and `e1000-has-link` re-reads
STATUS live, so that one is a contract question rather than a defect.

**`-e1000-nat`, blu 2026-07-30: the model carries a conversation now, and
that makes it easier to mistake for silicon rather than less.** The TX path
hands the frame to `nat_handle_tx` and a counterpart to `ne2k_inject_rx`
drains the same NAT queue into the receive ring, so `cdx-serve` answers a
real repository-protocol request over the e1000 branch. What is still NOT
modelled is unchanged and now matters more: no interrupts, no
multi-descriptor frames, no checksum offload, and no multicast filter.
**A green conversation here is evidence about the stack over a descriptor
ring, not about the card.**

**MDIC and the PHY, red 2026-07-30. This closes the gap the sitting named
as still silent.** The I219 is a PCH-integrated MAC whose PHY is reachable
only through MDIC, the model had no PHY at all, and it granted STATUS.LU on
CTRL.SLU alone -- so a driver that never touched the PHY passed here, and
that is exactly the bring-up that would fail on the board with every MAC
register reading correctly. The model now answers MDIC at 0x0020 with a
32-register PHY file at address 1, honouring BMCR reset and
auto-negotiation restart as self-clearing bits and setting BMSR's
negotiated bits as the observable effect. A device reset drops that state,
because otherwise it survives every later reset and a test's link arm
passes on a bring-up that skipped the PHY.

| Flag | What it does |
|---|---|
| `-e1000-no-phy` | MDIC never reports ready: a PHY that is not answering |
| `-e1000-phy-err` | MDIC reports the error bit: a transaction the MAC rejects |
| `-e1000-phy-link` | STATUS.LU requires auto-negotiation complete, not merely SLU |
| `-e1000-mdio-window` | MDIC answers nothing for 10 ms after CTRL.RST: the settle the I219 requires |
| `-e1000-mdio-slow` | MDIO reads answer E until reduced-frequency mode is set in page 769 register 16 |

**`-e1000-mdio-window` is the first arm in this catalogue written from a
cited spec section rather than from the driver.** Intel I219 datasheet rev
2.02 section 9.2: *"After LCD reset to the I219 a delay of 10 ms is required
before attempting to access MDIO registers."* A closed window answers with
neither R nor E, the same shape `-e1000-no-phy` produces, because silicon
that is not listening yet and silicon that is not there are not
distinguishable to a driver and a model must not make them so. It carries an
assumption it cannot settle: the datasheet opens the window at an LCD reset
and the arm opens it at CTRL.RST, which the 82583V distinguishes from
CTRL.PHY_RST. `docs/Reference/E1000_ServiceModel_Notes.md` has the audit.

**`-e1000-mdio-slow` states its own inventions, which is the part to copy.**
Two things in it are modelling decisions rather than citations, and both are
written at the flag's declaration in `tools/codex-vm.c`. The bootstrap
exemption: reads are gated but the page register and 769.16 are not, because
9.2 read strictly forbids the writes that would satisfy it and a literal arm
would make the requirement unsatisfiable. And the failure shape: E was picked
over a floating-bus 0xFFFF because E cannot be confused with a legitimate
register value. **What the arm tests is that the driver sets slow mode, which
is the citable part; it does not claim to reproduce the electrical failure.**
An arm that hides which half of itself is invented is not evidence.

The model's PHY reset clears the paged state deliberately, so ORDER is
observable: slow mode set before a PHY reset is gone by the time reads need
it. Without that the arm would test that a write happened rather than that it
holds, and both sabotage runs confirmed it moves the row it should.

**`-e1000-phy-link` is OFF by default and that is deliberate** (L-FALLBACK):
the default keeps the SLU-only behaviour every existing green was measured
against, so the flag ADDS an arm rather than moving the floor. Switching it
on by default would have re-pointed five passing tests at a new contract in
the same change that introduced it.

**Calibrated by sabotage, not by argument.** With `e1000-phy-bring-up`
removed from `e1000-link-up`, `codex/test/e1000-phy`'s link row flips to
`no` and the other four rows do not move, so that row is load-bearing on the
driver's PHY path and nothing else in the test is. The PHY id is asserted
non-zero rather than equal to a constant, and the id values are the MODEL's:
**nothing here has read the real I219's PHY**, so the row proves a
transaction completed, not that we recognise the part. The address arm reads
an address nothing answers on and requires the error sentinel rather than
zero, which is what separates an addressed transaction from one whose
address field is ignored.

**What it still does not buy.** Model and driver are one agent's reading of
one datasheet, so a misreading passes both, and the real part's PHY id, its
negotiation timing and its vendor-specific registers are all unmeasured. The
first honest verdict on this path is still the board.

Two constraints the flag is built to keep. It selects the model, which is
absent otherwise, so every historical green stays over the NE2000; measured
byte-identically across the five tests this touches. And **the NAT is one
wire**: with `-e1000-nat` set, `ne2k_inject_rx` returns without draining,
because the guest's stack brings the NE2000 up whether or not it binds the
Intel part, and with both cards on one queue the NE2000 took every frame
and the e1000 received nothing. That was the first symptom and it read as a
dead receive path.

**A fifth gap of the same shape, closed 2026-07-30: the NAT answered no
DHCP.** Not a missing device but a missing SERVICE of one we model, and it
had a document asserting the opposite (`OperatorsManual`'s NIC entry, which
has claimed a DHCP offer for as long as it existed). A DISCOVER went out to
a host socket and nothing answered. It survived because no guest had ever
sent one: `Dhcp.codex` was pure logic with no caller, so the absent server
and the absent client hid each other. The server is real now and
`codex/test/dhcp-acquire` drives it over both cards. **Two components that
can only be exercised together are a single untested thing, however many
green rows each has.**

**The receive filter, and why it needed a knob.** The model drops a frame
addressed to neither its station address nor broadcast, which is what makes
a stack sourcing the wrong MAC fail here the way it would fail on metal.
Our own driver sets RCTL.UPE at bring-up, deliberately, so on this bed the
filter is open and that branch is unreachable: `-e1000-strict-filter`
refuses to honour UPE and is the only way to reach it. **Calibrated by
sabotage** (a scratchpad build whose filter matches no address): strict, the
conversation dies and stderr names the address; the same binary with the
filter open passes. So the filter can say no, and the pass above is
attributable to the addressing rather than to a conduit that accepts
everything.

**The original brief, kept for what it asked and what remains:** The driver is
`codex/os/kernel/E1000e.codex` and every line of it that touches a
register, a ring or a frame is unexecuted. The model needs: the register
window, the receive and transmit descriptor rings walked out of guest
memory, link status, and the station address. Faults it must be able to
produce, because the driver has code for each and none of it has run: a
reset that never clears, a link that never comes up, a station address
with the valid bit clear, and a transmit descriptor that never reports
done. The registration convention above puts its BAR in the window, so
it exercises the accepting path of `e1000-bar-verdict` immediately;
the two refusing paths stay arithmetic-only until fester's beds.

**2. Whatever the hardware sitting names. ANSWERED 2026-07-29, and it named
three gaps that are not devices.** The part is an Intel I219-V, `8086:15b8`,
so the e1000e row above is a model of the actual silicon and no new NIC is
needed. What the sitting produced instead is a class of gap this document had
not accounted for: **not a missing device, but a missing STATE of a device we
already model.** Three of them, each a case where the real machine does
something the bed cannot express, so a defect in that direction is invisible
here by construction:

| Gap | Landed | The defect it hid |
|---|---|---|
| A scanline wider than the visible width | **`-gop-stride`, this CL** | Anything stepping framebuffer rows by width. The ASUS pads by 128 pixels |
| A **second xHCI** controller | **still absent here, and it did not block: reek built that bed in OVMF instead** | `xhci-connect` took the first controller and stopped. The board has two and the boot stick is on the second, which returned `disk=n` and cost a rung. reek's fix is gated against a two-controller OVMF bed whose calibration arm reproduces `disk=n` exactly. **The lesson is that this catalog is not the only place a bed can live**: when the missing state is a TOPOLOGY rather than a device's internals, QEMU already has it and splicing `codex-vm.c` is the expensive route |
| The e1000 model **wired to the NAT** | **`-e1000-nat`, blu, this CL** | Its TX summed the bytes and counted them rather than retransmitting, and its RX replayed only the canned frame, so `cdx-serve` could not converse over the e1000 at any flag combination and **the first real TCP conversation over this card would have happened on metal.** It now completes here: the full repository-protocol exchange passes over the e1000 branch, sourced from the address the model answers RAL/RAH with |
| A storage device that numbers its configuration anything but 1 | **`-usb-cfgval N`, reek, this CL** | `msc-open-endpoints` sent a hardcoded `SET_CONFIGURATION(1)` while the keyboard, mouse, camera and hub paths all read `bConfigurationValue` from descriptor byte 5. **Every bed in reach hid it in a different way**: this model reported 1, and QEMU's `usb-storage` reports 1 *and then accepts any value sent*, so neither could refuse. Calibrated as a 2x2 rather than one arm -- old driver plus `-usb-cfgval 2` is the only cell that fails, which is what shows the model refuses when it should AND only then. Fixed at main 12730 |
| A control request that can FAIL | **`-usb-setcfg-fault N`, reek, this CL** | Nothing. It reproduces the ASUS symptom (`connect=FAILED` from a transaction-errored SET_CONFIGURATION) so the host's handling can be built here instead of at the board. **It injects a symptom, not a cause, and must never be used to confirm a diagnosis** -- a checker fed a fault you chose will agree with you |
| A controller with **more than eight root ports** | **`-xhci-ports N`, reek, this CL** | `xhci-diag-ports` wrote cell `20 + port` up to port 16 while the snapshot band is 20-27, laying PORTSC over the handback flag that gates `kbd-pump` and over the BAR verdict. **Neither bed could show it and for the same reason twice**: this model reported four ports, QEMU reports eight, and eight is exactly the last cell the band owns. Both sat on or under the boundary. Empty ports above the modelled four report PP set, because a zero there would reproduce the overrun and hide its consequence. Fixed at main 12803 |
| A storage target that is **not ready on its first command** | **UNIT ATTENTION by default, reek, this CL** | `msc-wait-ready`'s entire retry loop -- TEST UNIT READY, REQUEST SENSE, retry -- had **never executed**, because a recognised command always succeeded here. That is the rung-4-to-5 path of the MSC ladder, the next rung the board reaches after the one it is stuck on. Calibrated by sabotaging `msc-wait-ready` to skip the handshake: it fails with the condition armed and passes under `-usb-no-unit-attention`, which is what shows the handshake is load-bearing rather than decorative. **The first version of this arm did not fail when it should have**, because `xhci_reset_ctl` memsets the BOT state and the guest issues HCRST during bring-up, so arming at init was wiped before the first command. Arming belongs in the reset path, which is also what ASC 0x29 describes. Closing the entry reek opened 2026-07-29 |
| A connected device **above root port 7** | **`-usb-disk-port N`, reek, this CL** | Anything that reads a wide bus through a narrow window. The probe prints eight PORTSC rows and index 18 is a COUNT, so a 26-port controller answering `connected=4` named none of them -- and the ASUS boot stick was on port 9 the whole time, which is how a sitting concluded "all four devices are Full or Low speed" from a display that could not show the SuperSpeed one. **No bed could hold such a device:** this model had four ports, and QEMU refuses attachment above its eighth whatever HCSPARAMS1 claims, so the 16-port splice reports 16 and still cannot seat a device high. `-xhci-ports 26 -usb-disk-port 10` reproduces the board exactly (`port=9 speed=4`), and it is what the connected-port BITMASK at index 46 was verified against -- the mask lights bit 9, which nothing before could make it do |
| A **PCI bridge**, so a bus walk can descend | queued, found by blu | `pci_add_device` hardcodes `header_type = 0` for every function, so no bridge exists on the emulated bus and **the descent branch of `pci-scan-all` cannot execute here at all.** The ASUS presents 21 devices over four buses; this bed presents one and cannot tell a complete walk from a bus-0 walk. Found by READING rather than by a failing test, which is the only way a gap of this shape is found |

### Running the USB storage arms by hand

Damian's ruling 2026-08-03: these are hand-run, and the battery is left
alone. So the recipe is the test, and it lives here rather than in
`codex/test/apps/`, where the glob would pick it up.

```powershell
build/compile.ps1 -Src codex/test/apps/usb-bot.codex -Out bot.cdx -Log bot.log -Kernel seed/Codex.cdx
# the .vmargs sidecar is how flags reach codex-vm; -DiskFile is MANDATORY
# for usb-bot (L-SIDECAR) or it fails as connect=FAILED for the wrong reason
'-usb-cfgval 2' | Set-Content cfg2.vmargs
build/test-run.ps1 -Kernel bot.cdx -OutFile out.txt -DiskFile codex/test/apps/usb-bot.disk -VmArgsFile cfg2.vmargs
```

The 2x2 measured 2026-08-03, and the point is that only one cell fails:

| driver | device reports | result |
|---|---|---|
| reads descriptor byte 5 | `-usb-cfgval 1` | `connect=ok sectors=2048` |
| reads descriptor byte 5 | `-usb-cfgval 2` | `connect=ok sectors=2048` |
| **hardcoded 1** | **`-usb-cfgval 2`** | **`connect=FAILED`** |
| hardcoded 1 | `-usb-cfgval 1` | `connect=ok sectors=2048` |

A single arm would not have been worth running. If the model accepted
every value, the top three rows look identical to a model that refuses
correctly; it is the fourth arm that separates "the driver is right" from
"the device cannot say no". The first attempt at this ran the sabotage
against a Perforce read-only file, the write threw, and the "old driver"
arm silently ran the FIXED driver and passed. **`p4 edit` before splicing,
and check the sabotage actually landed before believing the arm.**

`-xhci-ports 26` and `-usb-setcfg-fault 4` were exercised the same way:
26 ports changes nothing for a correct guest, and fault 4 gives
`connect=FAILED`, which is the ASUS reading reproduced on the dev box.

**A FOURTH ROW WAS IN THIS TABLE AND IT WAS FALSE. I wrote it, about my own
file, without reading the file.** It claimed codex-vm could not present a
Full-speed HID device, so the real keyboard's speed class was untestable here.
reek checked the source and it is the opposite: `xhci_init` has set
`portsc[1] = 1 | (1 << 10)`, speed 1, Full-speed, since **before** I wrote the
claim, and the comment beside it says it was HighSpeed "for no reason but
convenience" and was changed deliberately for exactly this reason. Verified
against `tools/codex-vm.c:1295-1302` before deleting the row. The bed I said
was missing had already been built, in the file this document is about, by the
agent I assigned to build it.

Two things follow and the second is the useful one. The interval-encoding
hypothesis that rode on that claim is dead: reek measured
`xhci-ep-interval` implementing both speed classes per xHCI Table 6-12, and
proved the instrument could say otherwise by forcing the HS encoding and
watching the endpoint be refused. And **the gap-table has the same failure mode
as the models it tracks.** A row asserting an absence is a claim about source,
it is not observed by any gate, and mine was written from a symptom report
rather than from `codex-vm.c`. Before adding a row here, read the file. That is
rule 2 applied to this table, and I broke it on the entry I was most confident
about.

**The tally is three defects and one rung**: bus 0 only, the first xHCI only,
stride equals width. Stated as the rule it is, because it generalises past
these three: **an instrument that cannot express the failure will report
success.** The false row above is the same rule turned on this document: a
claim nothing runs is a claim nothing checks. This document's admission test already says a model must be able
to refuse. The sitting adds the other half -- **a model must also be able to
be UNLIKE the machine in the ways the machine actually varies**, and the
place to look for those is a field the driver reads and no bed ever changes.

**A caveat on the first row, stated because the alternative is overstating
it.** `-gop-stride` sets the field correctly and refuses the values it cannot
serve, but the payloads that consume a stride are Option A images, and those
do not boot under codex-vm's own `-uefi` path at all: both the current and
the previous binary triple-fault at RIP `0x7032`, which the VM itself names
the fixed-address boot bug. So the flag's reach today is the mode info and
the framebuffer geometry, not a painted screen, and the end-to-end proof is
still OVMF. What made the row worth landing anyway is that the row-stepping
defect turned out **not to need the bed at all** -- see
`codex/test/gop-stride.codex`, which passes a stride unequal to the width as
an ordinary argument and needs no emulator feature. The bed is for the day
the boot path works; the test is the instrument now.

**Not in scope for the ship:** modelling a GTX 970, NVMe, or anything
else no ship row depends on. The catalog earns its keep by covering
drivers we are actually shipping, not by completeness.
