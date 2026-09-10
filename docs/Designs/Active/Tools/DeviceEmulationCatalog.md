# The Device Emulation Catalog

*Opened 2026-07-29 by red, on Damian's direction. The convention is settled,
the catalog below is measured, and the queue at the end is the open work.*

## Why this exists

Damian, 2026-07-29:

> why don't we make it more official than a scratchpad. there will
> undoubtedly be more drivers and classes of drivers to emulate first,
> hardwaresit second, and it would be nice to keep the catalog for
> regression testing.

**The order of operations for every driver is emulate first, hardware-sit
second.** The hardware sitting is the scarcest device on the bus (L-HUMAN) and
must not be spent discovering what a model would have caught. A scratchpad
model thrown away after one run wastes the expensive half: the catalog is what
makes a model outlive the sitting by becoming a regression test.

## The machinery already exists. Do not build a second one.

Read this before proposing anything (L-READ). Three pieces compose into what is
wanted:

1. **codex-vm's device models.** `pci_add_device(vendor, device, class,
   subclass, progif, bar0, irq)` in `tools/codex-vm.c` registers a PCI
   function; the model is a pair of BAR read and write handlers beside it.
2. **The `.vmargs` sidecar.** A per-test file of extra codex-vm flags, for
   tests whose subject is the MACHINE rather than the program.
   `check-sidecars.ps1` validates it and `test-run.ps1` passes it.
3. **Flags that change the machine.** Some already do exactly this job:
   `-xhci-no-root-kbd` removes a device so the bus walk must find another
   route, and `-board-mmio` moves a peripheral window.

**A new device model is therefore not new infrastructure.** It is a
registration, two handlers, one or more fault flags, a row in the table below,
and a test with a `.vmargs`. Anything proposing more should be read sceptically.

## The two rules that make a row worth having

**A model that can only succeed is not admitted.** reek, 2026-07-29, on USB
mass storage: *"an emulated device that cannot say no is not a test of the
driver that handles no."* Every entry must be able to refuse, the refusals it
can produce are part of its specification, and an entry earns its row by
sabotage: break the driver's handling of a fault and require exactly the
asserting rows to move.

**A model is not an independent oracle.** The model and the driver are usually
written by the same agent from the same reading of the same datasheet, so a
misreading passes both and the test is green by construction (L-ORACLE at its
most seductive, because a device model feels like ground truth). What a model
proves: that the driver's logic is self-consistent, that its state machine
advances, that it survives the failures the model can produce, and that a later
change did not break any of it. What it cannot prove is that either party read
the silicon correctly. **The hardware sitting stays the referee** (L-REFEREE),
a row here is never reported as hardware validation, and where a model's
behaviour comes from something other than our own reading of the spec, its row
says so.

**A row asserting an ABSENCE is a claim about source code that no gate
observes.** One such row here was false for weeks: it said codex-vm could not
present a Full-speed HID device, while `xhci_init` had set `portsc[1]` to
speed 1 before the claim was written. Read `tools/codex-vm.c` before adding a
row that says something is missing.

## The catalog

Measured 2026-07-29 against `tools/codex-vm.c`, with later rows dated where they
moved. **Re-measure before quoting** (L-COUNT).

Fidelity is about the MODEL against the silicon, and is a separate axis from
`OsHardwareRoadmap`'s METAL/EMU verdicts, which are about the DRIVER against
hardware.

| Device | PCI id | BAR | Fidelity | Can it say no? |
|---|---|---|---|---|
| Bochs VGA / GOP framebuffer | 1234:1111 | 0xFD000000 | display only, in-RAM, no MMIO trap. **`-gop-stride N` reports a scanline wider than the visible width**, which every panel measured here does. It sets PixelsPerScanLine at mode-info +32, the field `cdx-to-pe.ps1`'s stub reads into the handoff block at +0x20, and it survives a guest `SetMode` because padding is a panel property rather than a mode choice. NOT modelled: the host-side triangle rasterizer and its post passes, which address rows by visible width and **refuse a padded stride outright**; a bare-metal (non-UEFI) guest, which is handed width and height at GPA 0x7C4/0x7C8 and no stride cell at all | **yes.** A stride not wider than the mode, or one whose framebuffer would exceed `GOP_FB_SIZE`, is refused by name with the byte count rather than clamped |
| NEC xHCI USB 3.x | 1033:0194 | 0xFE800000 | mass storage, HID keyboard, UVC camera, hubs. Root port count is variable (`-xhci-ports N`, up to 32); the four modelled devices sit on ports 1-4 and everything above is an empty POWERED port, which is what a real wide controller reports | **yes.** `-usb-cfgval N` makes the storage device number its configuration N and REFUSE any other value (USB 2.0 9.4.7: a request error on a control pipe is a stall); `-usb-setcfg-fault N` answers SET_CONFIGURATION with completion code N whatever is sent; `-xhci-no-root-kbd` removes a device. The BOT model presents the power-on UNIT ATTENTION every conforming target presents: the first command after a controller reset answers CHECK CONDITION, `REQUEST_SENSE` returns sense key 0x06 / ASC 0x29, and reading it clears the condition. `-usb-no-unit-attention` restores an always-ready target |
| Intel HDA audio | 8086:2668 | 0xFE000000 | host waveOut | no |
| Intel e1000e NIC | 8086:100E | 0xFE400000 | **absent unless a flag selects it.** The common 8254x core: CTRL with self-clearing RST, STATUS.LU gated on CTRL.SLU, RAL/RAH with the AV bit, MDIC with a 32-register PHY file at address 1, both descriptor rings walked out of guest memory, canned frame injection, transmit descriptors consumed with DD written back. GPRC counts a good frame WHERE THE MAC ACCEPTS IT, above the ring poison, the K1 stall and the bus-master gate, so a stopped receive reads `gprc>0 ddset=0` rather than `gprc=0`; RNBC stays at the ring-full test. NOT modelled: interrupts (the driver polls), multi-descriptor frames, checksum offload, statistics, and a failed reset does not otherwise disable the part the way wedged silicon would | **yes.** `-e1000-no-reset` holds RST set, `-e1000-no-link` never raises LU, `-e1000-no-mac` clears the AV bit, `-e1000-no-tx-dd` never reports a transmit done, `-e1000-strict-filter` refuses to honour RCTL.UPE so the address filter is reachable, `-e1000-inject N` sets how many frames arrive, `-e1000-inject-armed` holds them until the guest has READ GPRC. PHY faults are in the table below |
| Intel I219 NIC | 8086:15B8 | 0xFE400000 | **absent unless `-i219` selects it.** The 82540EM model above plus the one PCH requirement with a named failure mode: PHY page 770 register 17, `Giga_K1_disable` bit 13 and `K1 enable` bit 14, cited to I219 rev 2.02 section 9.5.5.2, and a MAC that makes no progress while K1 is enabled at 1 Gbps; plus the MDIO/NVM semaphore at `EXTCNF_CTRL`. NOT modelled: ULP, SMBus and LANPHYPC, the LCD reload after PHY reset, LTR. Four of the eight rows, and silence here is not agreement | **yes.** `-i219-k1-nvm 0` powers up with K1 off, the control that says the stall comes from the K1 bits. `-i219-swflag` refuses MDIO unless the caller holds `EXTCNF_CTRL` SW ownership (0x00F00 bit 5, protocol 82583V 4.5.2, offset family-corroborated only). `-i219-mng-holds` starts with firmware holding MNG, so a correct acquisition is still refused. Both pairs and all controls are in `I219IsNotAnE1000.md` |
| NE2000 NIC | ISA, not PCI | ports 0x300 | exists nowhere outside codex-vm; no real machine has had one for twenty years | no |
| PCI-to-PCI bridge | 1b36:000c | header type 1 | **absent unless a flag selects it.** `-pci-bridge` puts one on bus 0 forwarding to bus 1 with a `1af4:1041` endpoint behind it; `-pci-bridge-deep` adds a second at `01:01.0` forwarding to bus 2; `-pci-bridge-levels N` chains N of them, bus 0 to N. Config space is bus-aware (`pci_find(bus, slot)`, and the `0xCF8` decode passes the bus byte), and a bridge answers offset `0x18` with its primary, secondary and subordinate bus numbers. NOT modelled: a driven device behind a bridge, and a subordinate-bus RANGE any caller reads | **yes.** `-pci-bridge-backward` points the deepest bridge's secondary at bus 0, which is what an unconfigured bridge reads and what `pci-bridge-one`'s guard refuses |
| IDE disk, HPET, IOAPIC, LAPIC, PS/2, CMOS RTC, PC speaker, UEFI firmware | not PCI | see OperatorsManual | varies | no |

**A bed must not advertise an id it does not implement** (red's ruling,
2026-08-20). The e1000 model is `8086:100E`, an 82540EM: there is no
EXTCNF_CTRL, no SWSM and no MSI-X, so 82574 would overclaim and I219 overclaims
further, its MAC sitting in the PCH behind a semaphore no line models. `15B8`
is reserved for the model written from the I219 datasheet. Nothing in the tree
observes the id, which is how it stayed wrong: the driver matches on vendor plus
class plus subclass (`E1000e.codex` `e1000-is-candidate`) and
`codex/test/e1000-match` uses an invented FIXTURE rather than a reading. The
check, if it is needed again, is a probe calling `pci-scan-bus 0` and
`e1000-find` and printing `d.pci-device-id`.

**Every BAR above is inside 3 to 4 GB, and that is load-bearing.** The runtime
page tables map 0 to 3 GB identity as RAM (the heap and stack arena), one
directory for 3 to 4 GB as devices, and nothing above 4 GB. A model placed
outside that window tests the guest's page tables, not the driver. It is also
why the emulated case cannot exercise a driver's handling of a badly placed
BAR: firmware on a real box picks the address, and OVMF was measured putting a
NIC BAR at 0x81060000, below the window entirely.

## Adding an entry

1. Register the function with `pci_add_device` and give it a BAR inside 3 to
   4 GB.
2. Implement the BAR read and write handlers, and any DMA the device does on
   its own initiative. A descriptor-ring device fetches its own descriptors, so
   the model must walk guest memory the way the silicon would.
3. **Give it at least one way to refuse**, as a flag. Without this the entry
   does not belong here.
4. Add the row above, including what it cannot do. An entry that overstates its
   fidelity is worse than no entry, because the driver author stops looking.
5. Write the test, with a `.vmargs` selecting the model and any fault.
6. Sabotage-check it: break the driver's handling and require exactly the
   asserting rows to move.

## The PHY and MDIO flags

| Flag | What it does |
|---|---|
| `-e1000-no-phy` | MDIC never reports ready: a PHY that is not answering |
| `-e1000-phy-err` | MDIC reports the error bit: a transaction the MAC rejects |
| `-e1000-phy-link` | STATUS.LU requires auto-negotiation complete, not merely SLU |
| `-e1000-mdio-window` | MDIC answers nothing for 10 ms after CTRL.RST |
| `-e1000-mdio-slow` | MDIO reads answer E until reduced-frequency mode is set in page 769 register 16 |

**`-e1000-mdio-window` is written from a cited spec section rather than from the
driver.** Intel I219 datasheet rev 2.02 section 9.2: *"After LCD reset to the
I219 a delay of 10 ms is required before attempting to access MDIO registers."*
A closed window answers with neither R nor E, the same shape `-e1000-no-phy`
produces, because silicon that is not listening yet and silicon that is not
there are not distinguishable to a driver and a model must not make them so. It
carries an assumption it cannot settle: the datasheet opens the window at an LCD
reset and the arm opens it at CTRL.RST, which the 82583V distinguishes from
CTRL.PHY_RST. `docs/Reference/E1000_ServiceModel_Notes.md` has the audit.

**`-e1000-mdio-slow` states its own inventions at the flag's declaration in
`tools/codex-vm.c`, which is the part to copy.** The bootstrap exemption: reads
are gated but the page register and 769.16 are not, because 9.2 read strictly
forbids the writes that would satisfy it. And the failure shape: E was picked
over a floating-bus 0xFFFF because E cannot be confused with a legitimate
register value. **What the arm tests is that the driver sets slow mode, which is
the citable part; it does not claim to reproduce the electrical failure.** An
arm that hides which half of itself is invented is not evidence.

The model's PHY reset clears the paged state deliberately, so ORDER is
observable: slow mode set before a PHY reset is gone by the time reads need it.

**`-e1000-phy-link` is OFF by default and that is deliberate** (L-FALLBACK):
the default keeps the SLU-only behaviour every existing green was measured
against, so the flag ADDS an arm rather than moving the floor.

## Standing contracts a caller must know

- **The NAT is one wire.** With `-e1000-nat` set, `ne2k_inject_rx` returns
  without draining, because the guest's stack brings the NE2000 up whether or
  not it binds the Intel part, and with both cards on one queue the NE2000 took
  every frame and the e1000 received nothing.
- **A green conversation over `-e1000-nat` is evidence about the stack over a
  descriptor ring, not about the card.** No interrupts, no multi-descriptor
  frames, no checksum offload, no multicast filter.
- **`truncated` on `PciScanResult` means the CAP stopped the walk, not "the
  answer is complete".** A backward-pointing bridge leaves its subtree unfound
  and `truncated` stays False, measured. Conflating the two would make the flag
  mean "something somewhere may be missing", which no caller can act on.
  `pci-collect` threads a `PciWalk` so a refusal at the deepest point survives
  back out through every sibling bridge above it. `pci-sec-bus` takes byte 1 of
  offset `0x18` and nothing reads byte 2, so the subordinate bus is correct in
  the model and unconsumed by the scanner.
- **Aiming the dying storage target takes a BAND, not a length.** `-usb-bot-die-len N`
  kills the target permanently at a write of N bytes or more; `-usb-bot-drop-len`
  refuses one command and the device keeps answering. Length alone cannot
  separate the sink from the bank, which also issues 32768-byte writes, at two
  fixed LBAs. Censused with `-usb-bot-census`: `len >= 32768 AND lba >= 3000`
  catches the sink and nothing else, with the gap from 2153 to 3548 as margin.
  `-usb-bot-die-lba N` is the LBA gate, and the `before-deferred bank=ok`
  reading is half the result: it says the run was healthy up to the write that
  killed it, which separates this from a medium that was never usable. Arms
  `sink-dies` and `sink-drop` in `diag-arm.ps1`.
- **`-e1000-inject-armed` is keyed on the GPRC read rather than on an ordinal.**
  The injector otherwise empties its budget at the `RCTL.EN` write, one stage
  before anything asks about a receive. An ordinal key (`-e1000-inject-late N`)
  was built first and its band is ONE value wide, which makes it a property of
  the whole run rather than of the thing under test. Arms `nic-invisible` and
  `nic-armed`.
- **`-usb-setcfg-fault` injects a symptom, not a cause, and must never be used
  to confirm a diagnosis.** A checker fed a fault you chose will agree with you.

## Running the USB storage arms by hand

Damian's ruling 2026-08-03: these are hand-run and the battery is left alone, so
the recipe lives here rather than under `codex/test/apps/` where the glob would
pick it up.

```powershell
build/compile.ps1 -Src codex/test/apps/usb-bot.codex -Out bot.cdx -Log bot.log -Kernel seed/Codex.cdx
# the .vmargs sidecar is how flags reach codex-vm; -DiskFile is MANDATORY
# for usb-bot (L-SIDECAR) or it fails as connect=FAILED for the wrong reason
'-usb-cfgval 2' | Set-Content cfg2.vmargs
build/test-run.ps1 -Kernel bot.cdx -OutFile out.txt -DiskFile codex/test/apps/usb-bot.disk -VmArgsFile cfg2.vmargs
```

**Calibrate as a 2x2, not as one arm.** Measured 2026-08-03, only one cell
fails: a driver reading descriptor byte 5 passes at `-usb-cfgval 1` and `2`, a
driver hardcoding 1 passes at `1` and FAILS at `2`. If the model accepted every
value the first three rows would look identical to a model that refuses
correctly; the fourth arm is what separates "the driver is right" from "the
device cannot say no". **`p4 edit` before splicing a sabotage, and check the
sabotage landed before believing the arm**: the first attempt wrote to a
read-only depot file, the write threw, and the "old driver" arm silently ran the
fixed driver and passed.

## Queue

**1. A target that REVIVES after it died. Owner none**, asked for by red
2026-08-21 for reek's WORKS-9 recovery path. `usb_bot_dead` is latched on
purpose (at the `-usb-bot-die-len` check): once the target dies it never answers
again, which is what `sink-dies` needs and what the metal target does. WORKS-9
needs the other half, a variant where a device that died at `-usb-bot-die-lba`
comes back on a port reset or an endpoint reset, whichever the driver can
actually issue, so a recovery path has an arm that can pass AND an arm that
cannot. Keep the latched default and name the new flag beside it.

**Measure before designing it**: which reset the driver issues, and whether this
model sees it, is the whole question. If codex-vm's xHCI does not already
observe a port or endpoint reset on the BOT path, the flag has nothing to hang
off and that is the first finding, not a detail.

**2. Per-controller device attachment. Owner none, and WORKS-25's ARM is parked
behind it.** `-xhci-two` gives a second controller with NOTHING on it: measured
2026-08-21, `ctl1` reports `kbd=n mouse=n disk=n`, `-usb-disk-port` selects a
root PORT rather than a controller, and every device model (`hid_*`,
`usb_bot_*`, `xhci_no_disk`) is a global singleton, so the second controller is
register-only. The code half is closed (`GopUsbMsc`, `GopUsbKbd` and
`CamCapture` walk controllers and stop at the first that yields the device each
wants); what is missing is an arm, because an arm asserting "the keyboard was
found" agrees with the walk and with the old ordinal-0 code while `ctl1` carries
nothing.

**DEFERRED by red 2026-08-21, with the size measured so the next person sees a
number rather than an adjective**: 42 singleton declarations, about 227
references to them, plus 53 on the `bot` struct, in a 15,942-line file, and the
control surface is 19 arms (10 in `diag-arm.ps1`, 9 in `codex/test`). It
rewrites the device models underneath the disk and keyboard boot path, and
nothing in the campaign needs `ctl1` because the stick rides `ctl0`. Damian can
overrule.

**3. A caveat that bounds `-gop-stride`.** It sets the field correctly and
refuses the values it cannot serve, but the payloads that consume a stride are
Option A images, and those do not boot under codex-vm's own `-uefi` path:
both the current and the previous binary triple-fault at RIP `0x7032`, which the
VM itself names the fixed-address boot bug. So the flag's reach today is the
mode info and the framebuffer geometry, not a painted screen, and the
end-to-end proof is still OVMF. The row-stepping defect turned out not to need
the bed at all: `codex/test/gop-stride.codex` passes a stride unequal to the
width as an ordinary argument and needs no emulator feature.

**Not in scope for the ship:** modelling a GTX 970, NVMe, or anything else no
ship row depends on. The catalog earns its keep by covering drivers we actually
ship, not by completeness.

## What this catalog taught, stated as the rule rather than as the stories

**An instrument that cannot express the failure will report success.** Three
defects and one rung came from exactly that: the bus walk that saw bus 0 only,
the USB probe that saw the first controller only, the framebuffer that assumed
stride equals width, and a storage target that was ready on its first command.
Each was invisible here by construction until the bed learned to be UNLIKE the
machine in the way the machine actually varies, and **the place to look for the
next one is a field the driver reads and no bed ever changes.**
