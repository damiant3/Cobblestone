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
| Intel e1000e NIC | 8086:100E | 0xFE400000 | **absent unless a flag selects it.** CTRL with self-clearing RST, STATUS.LU gated on CTRL.SLU, RAL/RAH with the AV bit, both descriptor rings walked out of guest memory, canned frame injection into the receive ring, transmit descriptors consumed with DD written back. GPRC counts a good frame WHERE THE MAC ACCEPTS IT, above the ring poison, the K1 stall and the bus-master gate, so a stopped receive reads gprc>0 ddset=0 rather than gprc=0; it was counted after the writeback until 2026-08-21, which made DiagNicRing's `frames arrived and we cannot see them` row unreachable here (arms: codex/test/e1000-rx-invisible, -off). RNBC stays at the ring-full test, which is a different row. NOT modelled: interrupts (the driver polls), multi-descriptor frames, checksum offload, PHY registers, statistics, and a failed reset does not otherwise disable the part the way wedged silicon would | **yes, four ways.** `-e1000-no-reset` holds RST set, `-e1000-no-link` never raises LU, `-e1000-no-mac` clears the AV bit, `-e1000-no-tx-dd` never reports a transmit done. `-e1000-inject N` sets how many frames arrive. Each of the four was run against the driver and the first found a real defect: the reset verdict was computed and discarded (fixed, CL 12079) |
| Intel I219 NIC | 8086:15B8 | 0xFE400000 | **absent unless `-i219` selects it.** The 82540EM model above plus the one PCH requirement with a named failure mode: PHY page 770 register 17, `Giga_K1_disable` bit 13 and `K1 enable` bit 14, cited to I219 rev 2.02 section 9.5.5.2, and a MAC that makes no progress while K1 is enabled at 1 Gbps; plus the MDIO/NVM semaphore at `EXTCNF_CTRL`. NOT modelled: ULP, SMBus and LANPHYPC, the LCD reload after PHY reset, LTR -- four of the eight rows, and silence here is not agreement | **yes, four ways.** `-i219-k1-nvm 0` powers up with K1 off, the control that says the stall comes from the K1 bits. `-i219-swflag` refuses MDIO unless the caller holds `EXTCNF_CTRL` SW ownership (0x00F00 bit 5, protocol 82583V 4.5.2, offset family-corroborated only). `-i219-mng-holds` starts with firmware holding MNG, so a correct acquisition is still refused. Both pairs and all controls are in `I219IsNotAnE1000.md` |
| NE2000 NIC | ISA, not PCI | ports 0x300 | exists nowhere outside codex-vm; no real machine has had one for twenty years | no |
| IDE disk, HPET, IOAPIC, LAPIC, PS/2, CMOS RTC, PC speaker, UEFI firmware | not PCI | see OperatorsManual | varies | no |

**The NIC advertised 8086:15B8 until 2026-08-20, which is an I219-LM, the
part on the ASUS, and it is not what this model implements.** What is
decoded is the common 8254x core: CTRL, STATUS, MDIC, ICR, RCTL, TCTL, the
two rings, RAL/RAH and four counters. There is no EXTCNF_CTRL, no SWSM and
no MSI-X, so 82574 would overclaim and I219 overclaims further, its MAC
sitting in the PCH behind a semaphore no line here models. It is 8086:100E,
an 82540EM, and 15B8 is reserved for a model written from the I219
datasheet. **A bed must not advertise an id it does not implement** (red's
ruling, 2026-08-20): an arm that checks the part then passes while the
behaviour is a different chip.

**Nothing in the tree observed the id, which is why it could be wrong for
as long as it was.** The driver matches on vendor plus class plus subclass
and never reads it (`E1000e.codex` `e1000-is-candidate`), and
`codex/test/e1000-match` uses 5560 as an invented FIXTURE rather than a
reading, so the wrong id bound correctly and every NIC arm stayed green.
The check, if you need it again, is a probe calling `pci-scan-bus 0` and
`e1000-find` and printing `d.pci-device-id`: it answers 4110 on this model
and answered 5560 before, so it is capable of telling them apart.
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
| A **second xHCI** controller | **PRESENT HERE since before 2026-08-21, and this row said otherwise until then (fester)**: `-xhci-two` adds the ASMedia `1b21:1242` as ordinal 1 beside ordinal 0, `-xhci-no-disk` unplugs the mass storage, `-xhci-bar2 N` places the second BAR. reek separately built an OVMF bed, which is what this row recorded, and the note that codex-vm lacked one was carried forward after it stopped being true -- a second bed was nearly built off it. **What was actually missing was an ARM: measured 2026-08-21, zero uses of all three flags across every arm and every `.vmargs`, so a two-controller model nobody had run.** `xhci-two` in `diag-arm.ps1` runs it now. Running it found the default `-xhci-bar2` at `0x91000000`, inside the 3 GB RAM arena, so the diag PCI stage answered `BELOW3G` -- "a device window sits inside our RAM arena ... do not expect the network or USB rows to be right". Every reading the second controller could have given was one the machine had already disowned. Default moved to `0xFE900000`, in the MMIO hole and clear of the other four BARs |
| The e1000 model **wired to the NAT** | **`-e1000-nat`, blu, this CL** | Its TX summed the bytes and counted them rather than retransmitting, and its RX replayed only the canned frame, so `cdx-serve` could not converse over the e1000 at any flag combination and **the first real TCP conversation over this card would have happened on metal.** It now completes here: the full repository-protocol exchange passes over the e1000 branch, sourced from the address the model answers RAL/RAH with |
| A storage device that numbers its configuration anything but 1 | **`-usb-cfgval N`, reek, this CL** | `msc-open-endpoints` sent a hardcoded `SET_CONFIGURATION(1)` while the keyboard, mouse, camera and hub paths all read `bConfigurationValue` from descriptor byte 5. **Every bed in reach hid it in a different way**: this model reported 1, and QEMU's `usb-storage` reports 1 *and then accepts any value sent*, so neither could refuse. Calibrated as a 2x2 rather than one arm -- old driver plus `-usb-cfgval 2` is the only cell that fails, which is what shows the model refuses when it should AND only then. Fixed at main 12730 |
| A control request that can FAIL | **`-usb-setcfg-fault N`, reek, this CL** | Nothing. It reproduces the ASUS symptom (`connect=FAILED` from a transaction-errored SET_CONFIGURATION) so the host's handling can be built here instead of at the board. **It injects a symptom, not a cause, and must never be used to confirm a diagnosis** -- a checker fed a fault you chose will agree with you |
| A controller with **more than eight root ports** | **`-xhci-ports N`, reek, this CL** | `xhci-diag-ports` wrote cell `20 + port` up to port 16 while the snapshot band is 20-27, laying PORTSC over the handback flag that gates `kbd-pump` and over the BAR verdict. **Neither bed could show it and for the same reason twice**: this model reported four ports, QEMU reports eight, and eight is exactly the last cell the band owns. Both sat on or under the boundary. Empty ports above the modelled four report PP set, because a zero there would reproduce the overrun and hide its consequence. Fixed at main 12803 |
| A storage target that is **not ready on its first command** | **UNIT ATTENTION by default, reek, this CL** | `msc-wait-ready`'s entire retry loop -- TEST UNIT READY, REQUEST SENSE, retry -- had **never executed**, because a recognised command always succeeded here. That is the rung-4-to-5 path of the MSC ladder, the next rung the board reaches after the one it is stuck on. Calibrated by sabotaging `msc-wait-ready` to skip the handshake: it fails with the condition armed and passes under `-usb-no-unit-attention`, which is what shows the handshake is load-bearing rather than decorative. **The first version of this arm did not fail when it should have**, because `xhci_reset_ctl` memsets the BOT state and the guest issues HCRST during bring-up, so arming at init was wiped before the first command. Arming belongs in the reset path, which is also what ASC 0x29 describes. Closing the entry reek opened 2026-07-29 |
| A connected device **above root port 7** | **`-usb-disk-port N`, reek, this CL** | Anything that reads a wide bus through a narrow window. The probe prints eight PORTSC rows and index 18 is a COUNT, so a 26-port controller answering `connected=4` named none of them -- and the ASUS boot stick was on port 9 the whole time, which is how a sitting concluded "all four devices are Full or Low speed" from a display that could not show the SuperSpeed one. **No bed could hold such a device:** this model had four ports, and QEMU refuses attachment above its eighth whatever HCSPARAMS1 claims, so the 16-port splice reports 16 and still cannot seat a device high. `-xhci-ports 26 -usb-disk-port 10` reproduces the board exactly (`port=9 speed=4`), and it is what the connected-port BITMASK at index 46 was verified against -- the mask lights bit 9, which nothing before could make it do |
| A **PCI bridge**, so a bus walk can descend | **`-pci-bridge`, root 2026-08-18** | `pci_add_device` hardcoded `header_type = 0` for every function, so no bridge existed on the emulated bus and **the descent branch of `pci-scan-all` could not execute here at all.** The ASUS presents 21 devices over four buses; this bed presented one and could not tell a complete walk from a bus-0 walk. Found by READING rather than by a failing test, which is the only way a gap of this shape is found |

**The PCI bridge, landed 2026-08-18 (root).** `-pci-bridge` puts a PCI-to-PCI
bridge (`1b36:000c`, class 06 subclass 04, header type 1, the `pcie-root-port`
fester saw on the board) on bus 0, forwarding to bus 1 where a `1af4:1041`
endpoint sits. The config space is bus-aware now (`pci_find(bus, slot)`; the
`0xCF8` decode passes the bus byte it had always discarded), and the bridge
answers offset `0x18` with its primary/secondary/subordinate bus numbers, which
is the register `pci-scan-all` reads to know where to descend. **OFF by default
(L-FALLBACK):** the bridge is a new bus-0 device, so a default-on flag would
move every existing `pci-scan-bus-0` count; measured, `e1000-bringup` is
byte-identical with the flag absent. **Calibrated by the flag itself, not an
argument:** `codex/test/pci-bridge-scan` calls `pci-scan-all` and counts
devices on bus 1. With `-pci-bridge` it reports `scan count=5 bus1=1` (the
descent ran and found the endpoint); the SAME binary without the flag reports
`count=3 bus1=0`, because with no bridge the descent branch cannot execute at
all. The flag moves the row, so the branch is load-bearing rather than
decorative. What it does not buy: this is a single bridge one level deep with an
inert endpoint, so multi-level descent, the subordinate-bus range, and a driven
device behind the bridge are all still unmodelled; the first honest verdict on a
real four-bus topology is still the board.

**Queue: a target that REVIVES after it died. Owner none, asked for by red
2026-08-21 for reek's WORKS-9 recovery path.** `usb_bot_dead` is latched on
purpose (`tools/codex-vm.c`, at the `-usb-bot-die-len` check): once the target
dies it never answers again, which is what `sink-dies` needs and what the
metal target does. WORKS-9 needs the other half -- a variant where a device
that died at `-usb-bot-die-lba` comes back on a port reset, or an endpoint
reset, whichever the driver can actually issue -- so that a recovery path has
an arm that can pass AND an arm that cannot. Name it beside the existing flag
and keep the latched default; tell reek the flag name when it lands.

**Measure before designing it**: which reset the driver issues, and whether
this model sees it, is the whole question. If codex-vm's xHCI does not already
observe a port or endpoint reset on the BOT path, the flag has nothing to hang
off and that is the first finding, not a detail.

**Queue: per-controller device attachment. Owner none, and WORKS-25 is parked
behind it.** `-xhci-two` gives a second controller with NOTHING on it:
measured 2026-08-21, `ctl1` reports `kbd=n mouse=n disk=n`, `-usb-disk-port`
selects a root PORT rather than a controller, and every device model
(`hid_*`, `usb_bot_*`, `xhci_no_disk`) is a global singleton. So the second
controller is register-only. `apps/works/works-backlog.md` WORKS-25 --
`xhci-connect` opens ordinal 0 while `usb-attach` walks, and `GopUsbMsc`,
`GopUsbKbd` and `CamCapture` still call it -- cannot be fixed until a device
can be put behind `ctl1`, because the fix is to the boot path and there would
be no arm able to show it works.

**DEFERRED by red 2026-08-21, with the size measured so the next person sees a
number and not an adjective**: 42 singleton declarations, about 227 references
to them, plus 53 on the `bot` struct, in a 15,942-line file, and the control
surface is 19 arms (10 in `diag-arm.ps1`, 9 in `codex/test`). It rewrites the
device models underneath the disk and keyboard boot path. The ruling was that
nothing in the campaign needs `ctl1` -- the stick rides `ctl0` -- so that is
risk bought for nothing the mission asks. Damian can overrule.

**A target that dies on a bulk write, landed 2026-08-21 (fester).**
`-usb-bot-die-len N`: the mass-storage target stops answering permanently
once it sees a write command of N bytes or more. `-usb-bot-drop-len` refuses
one command and the device keeps answering, so `sink-drop` reads
`sink=write-refused` with `bank=ok` and the file whole -- a handled refusal,
which is a different event from the one metal keeps delivering. The sink's
write has killed the bank on the board three times (sittings 7, 8, 9 lost the
deferred sink and everything after it).

**Length alone cannot aim it, and `-usb-bot-die-lba N` is what does.** "2.7 MB
as one write" is true of the sink's own call and false at the BOT layer.
Censused 2026-08-21 with `-usb-bot-census`, which prints every read and write
with its LBA and declared length -- 133 writes on a default run:

| what | length | LBA |
|---|---|---|
| bank, per banked row | 32768 | **2049** (fixed) |
| bank, per banked row | 20480 | 2113 |
| bank, per banked row | 32768 | **2153** (fixed) |
| bank, per banked row | 20480 | 2217 |
| bank, per banked row | 512 | 2257 |
| bank, file data | 2560..3584 | 3475..3541, growing |
| **the sink** | **32768 x 60** | **3548..7324, one burst** |

So the bank issues 32768-byte writes too, at two fixed LBAs, and no length
threshold separates the two. `len >= 32768 AND lba >= 3000` catches the sink
and nothing else, with the gap from 2153 to 3548 as margin -- a band, not a
value.

**Aimed, it reproduces the metal shape; unaimed it produces a different
failure entirely.** Same length threshold, LBA gate the only difference:

| | dies at | before-deferred | summary |
|---|---|---|---|
| `-usb-bot-die-lba 3000` | lba 3548, the sink's first write | `bank=ok` | `bank=lost at=sink size=3322` |
| no LBA gate | lba 2049, the FAT | `bank=none` | `bank=none write refused, write stage 13` |

The `before-deferred bank=ok` row is half the reading: it says the run was
healthy up to the write that killed it, which is what separates this from a
medium that was never usable. Arm `sink-dies` in `diag-arm.ps1`. `bank-lost`
beside it reaches the same summary by wedging the medium from transfer 950
onward, which is an ordinal with a band this tree has already had to
re-derive once; this arm's two keys are properties of the command itself.

**WHEN a frame arrives is a device state too, landed 2026-08-21 (fester).**
`-e1000-inject-armed` holds the canned frames until the guest has READ GPRC.
The injector empties its whole budget at the `RCTL.EN` write, which happens in
`nicinit`, one stage before anything that wants to ask about a receive.
DiagNicRing reads GPRC twice on purpose so a frame counted in an earlier
window cannot be read as one that arrived while it was looking, and measured
on the 18799 image with `-e1000-inject 1` it reported `pre=1 gp=0`: the
counter working exactly as designed, and sitting 9's row still not
expressible. **The gap was not what the MAC does with a frame but WHEN the
frame arrives**, which is a class this catalog had no entry for.

Keyed on the GPRC read rather than on an ordinal, deliberately.
`-e1000-inject-late 2` also puts the frame in the right window and was built
first; it is the `-usb-bot-drop N` defect `diag-arm.ps1` documents at length,
a property of the whole run rather than of the thing under test, and measured
the band was **one value wide** -- 1 lands in `nicinit`, 3 and above never
fire. The GPRC read is the stage's own action and nothing upstream can move
it. Arms `nic-invisible` and `nic-armed` in `diag-arm.ps1`.

**Multi-level descent, landed 2026-08-21 (fester).** `-pci-bridge-deep` puts a
second `1b36:000c` at `01:01.0` forwarding to bus 2 with an endpoint at
`02:00.0`, and sets the first bridge's SUBORDINATE to 2, which is the highest
bus behind it rather than its secondary. **`pci-scan-max-depth` is 3 and
`pci-collect` has always been written to recurse, but one bridge one level deep
is all any bed has ever presented, so depth 2 and 3 had never executed
anywhere.** A recursion that is reachable and untravelled reads exactly like a
tested walk, which is L-UNCALLED one level out: the path was not absent, its
second turn was.

**Calibrated three ways on ONE binary**, which is what separates "the second
level moved it" from "a bridge moved it":

| flags | `count` | `bus1` | `bus2` |
|---|---|---|---|
| `-pci-bridge-deep` | 7 | 2 | 1 |
| `-pci-bridge` | 5 | 1 | 0 |
| neither | 3 | 0 | 0 |

`bus2` is non-zero only in the first row. The middle row is the one that
matters: with a bridge present but no second level the descent still runs and
still reports `bus2=0`, so the arm is reading the recursion rather than the
presence of a bridge. **Its own flag rather than a change to `-pci-bridge`
(L-FALLBACK)**: the second level adds two devices, so folding it in would move
`pci-bridge-scan`'s counts and cost that arm its floor. Measured, that arm is
byte-identical with the new flag absent.

What it still does not buy: a subordinate-bus RANGE that anything reads --
`pci-sec-bus` takes byte 1 of offset `0x18` and nothing in the driver reads
byte 2, so the subordinate is correct in the model and unconsumed by the
scanner -- and a driven device behind either bridge. Arm:
`codex/test/pci-bridge-deep`.

**The depth cap, and the two branches that refuse, landed 2026-08-21
(fester).** `-pci-bridge-levels N` chains N bridges, bus 0 to N, each with an
endpoint on the bus it forwards to; 1 and 2 are the topologies `-pci-bridge`
and `-pci-bridge-deep` already built, so those are one mechanism now and both
arms are measured byte-identical across the change. `-pci-bridge-backward`
points the deepest bridge's secondary at bus 0.

**Two guards in `Pci.codex` had never executed anywhere.** `pci-collect`
refuses below `pci-scan-max-depth`, and no bed had presented a fourth level;
`pci-bridge-one` descends only when the secondary bus is above its own, and
nothing had ever handed it a bridge that fails that test, though an
unconfigured bridge reads 0 there and is a real part in a real state. A guard
that has never been shown to say no is an assertion rather than a guard
(L-FALSIF), and this tree has found a defect behind most of that shape.

| flags | `count` | `bus1` | `bus2` | `bus3` | `bus4` |
|---|---|---|---|---|---|
| `-pci-bridge` | 5 | 1 | 0 | 0 | 0 |
| `-pci-bridge-deep` | 7 | 2 | 1 | 0 | 0 |
| `-pci-bridge-levels 3` | 9 | 2 | 2 | 1 | 0 |
| `-pci-bridge-levels 4` | 10 | 2 | 2 | 2 | 0 |
| **`-pci-bridge-levels 5`** | **10** | **2** | **2** | **2** | **0** |
| `-pci-bridge-levels 2 -pci-bridge-backward` | 6 | 2 | 0 | 0 | 0 |

Arms `codex/test/pci-bridge-cap` (four levels), `pci-bridge-cap-under` (three,
the control that makes `bus4=0` the cap refusing rather than the topology
running out) and `pci-bridge-backward`.

**THE FINDING WAS THE FIFTH ROW, AND IT IS NOW FIXED.** Four levels and five
reported the same count and the same per-bus numbers, because `pci-collect`
answered `acc` at the cap and said nothing. Eleven devices exist at four
levels and the scan reported ten; the bus-4 endpoint was not missing from the
machine, it was missing from the answer, and no caller could tell a complete
walk from a truncated one. That is the exact failure this whole entry began
with -- "scanning bus 0 alone returns an answer that looks complete and is
empty" -- one level further down, and the ASUS has four buses.

`PciScanResult` carries `truncated` as of 2026-08-21 (fester). `pci-collect`
threads a `PciWalk` so a refusal at the deepest point survives back out
through every sibling bridge above it. The discriminating pair is the two
arms already here: `pci-bridge-cap-under` at three levels answers
`truncated=no` and `pci-bridge-cap` at four answers `truncated=yes`, so the
flag moves with the cap rather than with the presence of depth.

**It means the cap stopped the walk, not "the answer is complete."** A
backward-pointing bridge leaves its subtree unfound and `truncated` stays
False, measured: `pci-bridge-backward` reads `truncated=no` with bus 2 empty.
Conflating the two would make the flag mean "something somewhere may be
missing", which no caller can act on.

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
