# Boot Diagnostics

Standalone Option A GOP payloads for diagnosing hardware bring-up on
real machines, where there is no serial log and the ceremony cannot
advance if the keyboard does not work. Each paints its findings to the
framebuffer and sits (it never returns), so it can be photographed off
the glass.

`PciProbe` reads the PCI bus and answers what the parts ARE.
`XhciTruthProbe` and `KbdDiagProbe` answer what the USB stack DID with
them; both read the same diagnostic cell block (0x1D000) that the real
xHCI bring-up fills (`apps/works/GopXhci.codex`), so their numbers are
exactly what the boot path saw. `SceneProbe` answers whether the
DISPLAY path is right: the mode the firmware handed us, the row stride,
the channel order, and whether a rendered rectangle stays inside its
own bounds.

Use the USB two when a machine boots the Codex payload but finds no USB
keyboard, no boot stick, or nothing on the USB bus at all.

**Before reading any probe, read the screen COLOUR.** Every Option A
image carries two liveness marks from `option_a_stub.asm`: solid dark
blue once GOP is acquired, solid dark green once ExitBootServices and
our own page tables are live. An unchanged firmware screen means we were
never loaded or `LocateProtocol(GOP)` failed; blue alone means the stub
died in allocation, `GetMemoryMap` or `ExitBootServices`; green alone
means the payload died. Without them every one of those is a black
screen, because every failure path in the stub ends at `fatal`, which is
`jmp fatal`. `docs/HardwareSitting.md` boot 1 has the table.

## PciProbe.codex

Walks the PCI bus and renders every device's vendor and device ID, on
the glass and as QR codes. Run this FIRST on an unknown machine: a
driver cannot be written against a guess about the part, and this is
the probe that names it.

Bus 0 is walked, and then every bus behind every PCI-to-PCI bridge
found on it, to a depth of 3. **Bus 0 alone is not enough and the
difference is invisible in the answer.** A chipset-integrated NIC (the
PCH's own Ethernet) sits on bus 0; a discrete one sits behind a root
port on a higher bus, and a probe that scanned only bus 0 would report
NONE FOUND on a machine that plainly has a network port. Verified under
OVMF with the only NIC behind a `pcie-root-port`: it is found at
`01:00.0`, and bus 0 holds only the bridge itself at `1b36:000c`.

The screen carries three call-outs -- NIC (class 02), STORAGE (class
01) and USB (class 0c.03) -- each with its identity and, on the small
line beneath, revision, subsystem pair, interrupt line and BARs 0, 1
and 5. A call-out with nothing behind it says `NONE FOUND ON ANY BUS`
in red rather than going quiet. Below that is the full device list,
headed by `devices=N all listed` or, when the screen genuinely cannot
hold every row, `devices=N ONLY M FIT ON SCREEN` in red followed by the
statement that the codes carry all N.

That header used to read `devices=N listed=M` in ordinary white, and on
its first real gate it said `devices=6 listed=4` while silently dropping
two devices. The call-outs stayed correct, so the failure mode was a
reader taking an accurate NIC line off a list that was two thirds of the
truth. A Z170 board presents far more than six devices.

BAR0 is where a NIC and an xHCI put their register window; **BAR5 is
where an AHCI controller puts its**, so a reading that took BAR0 alone
would report the storage controller at address nothing (an ICH9 in AHCI
mode reads back zero for BAR0 through BAR4). BAR1 is carried because it
holds the high half of a 64-bit BAR0 -- QEMU's xHCI answers
`B0=00000004 B1=000000c0`, a window above 4 GB that BAR0 alone does not
locate. The values are raw config-space dwords, not this chapter's
reading of them: a one-shot measurement should carry what the register
held.

`MAP=` answers whether that window is REACHABLE, which is a different
question from where it is. The runtime page tables map 0 to 3 GB
identity as RAM (the heap and stack arena), one directory for 3 GB to
4 GB as devices, and nothing above 4 GB. The BAR examined is BAR5 for a
storage controller and BAR0 for everything else, matching where each
puts its registers.

| verdict | what it means |
|---|---|
| `ok` | inside the device window, readable once the device is enabled |
| `ABOVE4G` | a 64-bit BAR beyond the mapping. Faults on the first register read, which is LOUD |
| `BELOW3G` | **the dangerous one.** Mapped, as ordinary RAM, inside the arena `alloc-bytes` hands out |
| `none` | the BAR is unassigned |

**`BELOW3G` is worse than `ABOVE4G` and it reads like the milder
answer.** A BAR above 4 GB is unmapped, so the first register read
faults and says so. A BAR at 0x81060000 does not fault at all: it is
mapped as RAM, the driver and the heap end up pointed at the same
bytes, and the failure surfaces somewhere else entirely. Red's
`e1000-bar-verdict` refuses all three bad shapes before touching a
register, and `codex/test/e1000-match` pins both measured addresses and
both window boundaries.

Measured under OVMF: the NIC and the AHCI controller come back
`BELOW3G` and QEMU's xHCI comes back `ABOVE4G`. **`MAP=ok` has not been
observed here**, and OVMF cannot be made to produce it -- it pins its
32-bit PCI window at 0x81000000 at 2048 MB, at 3584 MB, and with
`-machine q35,max-ram-below-4g=3G` (accepted by QEMU without complaint,
and it moved nothing). The bed that does place BARs in the window is
**codex-vm**, whose three emulated devices carry BAR0 at 0xFD000000,
0xFE800000 and 0xFE000000. That is a reading of the emulator's source,
not of this probe running there: codex-vm cannot display a probe that
halts (see Build and run below), so an ordinary bare-metal CDX is how
that bed gets read.

The QR codes are the record and the screen is the convenience, so the
codes are sized BEFORE the screen is divided and the list takes what is
left. The scale is CHOSEN, not assumed: the largest of 6, 5, 4, 3 that
fits the space below the call-outs. Scale 2 is not offered, because at
scale 2 the decoder finds the finder patterns and reads none of the
codes, which is a failure that looks like success on the glass; scale 3
is flagged in yellow on the screen so the operator knows to shoot close
and square.

The codes used to take a fixed bottom half of the display. That wasted
whatever the grid did not need, cost two scale steps, and left the
device list two rows deep.

Photograph every code and decode the photo on the dev box with `pwsh
tools/qr-read.ps1 -Path photo.jpg [-Save report.txt]`. Chunks carry
`i/n;` prefixes and reassemble automatically; the reader says `WARNING:
n of m chunks` when the report is incomplete, and a partial report is
the one thing not to read past. Re-shoot rather than trust it.

Reading it: `devices=0` means the config-space accessors answered
nothing, which is a probe fault and not a bare machine. A device
present in the list but absent from its call-out means the class code
is not what was expected -- take the vendor:device from the list, which
is the answer that was actually wanted.

## XhciTruthProbe.codex

Runs the runtime spine, enumerates the USB bus (the full xHCI bring-up:
ownership handoff, halt/reset, port power, Intel EHCI->xHCI routing,
device enumeration), then paints the xHCI reading and halts:

- controller vendor:device, caplen
- HCCPARAMS1 with CSZ (32- vs 64-byte contexts), PPC, xECP
- ownership handoff: legsup present, before/after words, BIOS released
- the BAR window verdict, the address judged (both dwords), and the
  operational base the bring-up actually used
- slots / ports / reset / cnr / run / connected count
- Intel USB2 routing applied + masks
- raw PORTSC of every root port (CCS/PED/PR/speed)
- ENUMERATED: whether the keyboard, mouse, and disk were configured
- the per-machine xHCI summary and one row per controller (below)

Reading it: `found=n` means no xHCI on the PCI bus (an EHCI-only
board). `connected=0` with devices plugged in points below enumeration
-- port power or chipset routing (check the Intel routing line and
PPC). Live connect bits with no enumerated device point above it.

### Everything above the ENUMERATED line describes ONE controller

**A machine can have more than one xHCI, and this board does.** The ASUS
carries the Intel PCH controller and an ASMedia `1b21:1242` behind a bridge,
and the boot stick is on the ASMedia one. Each bring-up overwrites diag
indices 0 to 39, so the identity, PORTSC snapshot and BAR verdict at the top
of the screen belong to **whichever controller was brought up last** and say
nothing about the others. That is what the `xHCI seen=` line and the `ctlN`
rows below it exist for, and on a multi-controller machine they are the rows
to read first.

```
xHCI seen=2 opened=2 walked=#00000003 disk-on-ctl=2 kbd-on-ctl=1
ctl0 1b36:000d at 0:3.0 running        kbd=y mouse=n disk=n
ctl1 1b36:000d at 0:4.0 running        kbd=n mouse=n disk=y
```

`seen=` is how many class `0c.03` functions the whole bus tree holds,
`opened=` how many came up, `walked=` a bitmask of the ordinals walked.
`disk-on-ctl=` and `kbd-on-ctl=` are the controller's ordinal **plus one**,
so `0` means no controller yielded one rather than controller zero did.

Each `ctlN` row is that controller's own identity, `bus:dev.func`, outcome
and what was found on it. The outcome is the row that matters:

| outcome | meaning |
|---|---|
| `running` (green) | opened, brought up, and its ports walked |
| `BRINGUP-FAILED` (amber) | opened and the bring-up did not complete |
| `NEVER-OPENED` (amber) | **the walk never got to it.** Not the same as "no disk on it" |

### The HID endpoint rows: asked beside programmed

The last three lines are the boot keyboard's interrupt endpoint, and every
number is a PAIR. A value alone is satisfied by any plausible number, so
these print **what the device's descriptor asked for beside what went into
its endpoint context**, and a disagreement is the finding.

```
HID EP: bInterval asked=10 -> Interval set=6
        wMaxPacket asked=8 -> MaxESIT set=8
        speed=1 dci=3 route=#00000001 TT=n slot=0
```

`Interval` is not `bInterval` and must not look wrong for being different.
xHCI encodes the service interval by speed class (spec Table 6-12): a
Full- or Low-speed endpoint takes `log2(bInterval * 8)` clamped to 3..10, a
High- or SuperSpeed one takes `bInterval - 1` clamped to 0..15. So at
`speed=1` a `bInterval` of 10 SHOULD read `Interval set=6`, and at
`speed=3` a `bInterval` of 7 should read `6` as well by the other rule.
Both are confirmed under OVMF. **A Full-speed endpoint showing
`Interval set = bInterval - 1` is the High-speed rule applied to the wrong
speed class**, which programs a nonsensical polling rate and is the shape
of a keyboard that enumerates and then never delivers.

`route=#0` is root-attached. A non-zero route means the device is behind a
hub, and then `TT` matters: a Full- or Low-speed device below a
**high-speed** hub reaches the bus only through that hub's transaction
translator, so `TT=n slot=0` there is a defect and the symptom is silence
on the interrupt endpoint with enumeration working perfectly. Below a
full-speed hub no translator is needed and `TT=n` is correct. Measured
under OVMF with `-UsbHub`: QEMU's `usb-hub` is USB 1.1, so that bed gives
`speed=1 route=#1 TT=n` and is the correct-no-TT case, not the defect.

**`NOT READ` is a reading, and it is not zero.** If the endpoint was never
configured all three lines say so in amber rather than printing zeros:

```
HID EP: NOT READ -- endpoint never configured
        (no descriptor fields were taken)
        (no route, TT or speed recorded)
```

A `bInterval` of 0 because the descriptor fetch failed and a `bInterval`
genuinely 0 must not photograph the same. Verified by booting with no USB
keyboard attached.

`KbdDiagProbe` carries the same fields as one compact line in
`KBDDIAG.TXT` and in the QR body: `EP st=2 bi=10 iv=6 mp=8 es=8 rt=1 tt=0
sp=1`. **`st` comes first and is three-valued** -- `st=0` prints
`NOT-READ` and nothing after it, 1 is configure-failed, 2 is configured.

That line lengthened the QR body, and the chunk COUNT is derived
(`(total + 99) / 100`) while the panel width is not: `kd-qr-chunks` lays
the codes left to right at `40 + i * (45 * qs + 24)`, so at `qs=6` a fifth
code starts at x=1216 and needs 1486 px. **1920 fits it; 1280 does not.**
The count is not capped, so photograph every code that is drawn and let
`tools/qr-read.ps1` tell you if a chunk is missing -- it says
`WARNING: n of m chunks`, and a partial report is the one thing not to
read past. **This has not been verified by a decode of a real run**; the
arithmetic above is inspection, not measurement.

**`NEVER-OPENED` is the distinction this block was added for.** Attempt 2's
rung 2 reported `ENUMERATED disk=n` from a stack that stopped at the first
controller, and one glance from being written down as "our USB storage fails
on real hardware" when the truth was that the stick sat on a controller
nobody opened. A row saying `disk=n` and a row saying `NEVER-OPENED` are
different answers and the screen now prints which one it is. So: if
`ENUMERATED disk=n`, read `seen=` against `opened=` before concluding
anything about the storage stack.

`verdict=1` means the BAR was used where firmware put it, `2` means it
was relocated to FE800000, and `0` means the judgement was never
reached at all. Read `judged=` as one 64-bit address in two dwords:
under OVMF it is `#000000c0_00000000`, a BAR above 4 GB, which is why
the verdict there is 2 and `op=#fe800040` rather than an address near
the judged one. A low dword of zero is normal for such a BAR and is
not evidence that the BAR went unread.

## MscAlignProbe.codex

Answers whether a bulk TRB whose data buffer crosses a 64 KB boundary
works on the controller in front of you. xHCI is believed to forbid it,
and `msc-read-into` pushes a single 32 KB Normal TRB at whatever address
the caller passed, so on most boots the seed read issues about thirty
crossing TRBs. `count=64` is exactly one TRB, so each row is one
transfer and nothing is averaged.

Reads the same LBA twice: into a 64 KB-aligned buffer, where a 32 KB
transfer cannot cross, and into one starting 1024 bytes below a boundary,
where it must. Reports both the return code and a checksum of the
delivered bytes, because a controller that accepts the TRB and moves the
wrong bytes is what a return code alone would hide.

The last row is the instrument's own calibration: an out-of-range LBA
read that MUST report `ok=n`. If it says `y`, the probe can only say yes
and the two rows above it mean nothing.

Under OVMF on `qemu-xhci` (2026-07-29): both reads `ok=y` with identical
checksums, liveness `ok=n`. So QEMU does not reject a crossing TRB and
delivers it correctly. **That does not answer real silicon**, which is
why this probe exists to be flown on the ASUS.

## KbdDiagProbe.codex (v8)

For the case where the keyboard enumerates (`uk-ok=y`) but delivers no
keystrokes. Paints the xHCI summary and the keyboard endpoint
parameters, then runs three timed phases (~90s, ~45s, then forever;
tick-driven with a paint-count fallback). Hold a key in EACH phase:

- **Phase 1** -- the endpoint-attributed USB pump (below), with
  findings rewritten to KBDDIAG.TXT.
- **Phase 2** -- the OWNERSHIP HANDBACK experiment, the feasibility
  test for a permanent "no USB keyboard, fall back to PS/2" boot
  feature: halt the controller, restore the firmware's own SMI
  enables (recorded at diag index 39 by the handoff), clear OS-owned,
  then count PS/2 arrivals on both routes (IRQ1 mailbox + a
  floating-bus-guarded port 0x60 poll -- SMM emulation on some boards
  only answers the polled port). `PS2` climbing here = the firmware
  revived its legacy keyboard emulation = the fallback is real.
  `reclaim=y` = the BIOS re-took ownership. No file writes in this
  phase (the controller is the firmware's).
- **Phase 3** -- REACQUIRE: the full bring-up runs again and the
  phase-2 verdict is written to the file. `reacq kbd/disk/mount` all
  `y` proves ownership can be juggled per phase -- the strongest form
  of the fallback feature.

What the pump counters mean:

1. **Events are attributed to their endpoint.** The transfer event
   TRB's control dword carries the endpoint id in bits 20:16; earlier
   probes counted ANY transfer event, so one leftover EP0 control
   completion read as "the interrupt endpoint fired once." `EPINT`
   counts only completions whose endpoint id equals the keyboard's
   dci; `EP0` and `OTH` count the impostors; `LATCH` counts codes
   taken from the per-slot latch (endpoint id already lost there).
2. **Findings are written to KBDDIAG.TXT** on the boot stick's own
   ESP whenever the counters change (capped at 250 rewrites). After a
   real-hardware run, mount the stick and read the file -- no
   photographing the glass. Only the disk usb-attach itself published
   is written (the selection cells are pinned to the USB medium);
   internal AHCI/NVMe drives are never touched. No USB disk -> no
   file, screen only.

**Findings also render as QR codes** (R-1 of TheSilentKeyboard.md):
the same body that goes to KBDDIAG.TXT is drawn as three version-5
codes below the text, re-rendered whenever the counters change and
fully independent of the disk. On real hardware, PHOTOGRAPH THE
CODES with any phone camera -- decode the photo on the dev box with

```powershell
pwsh tools/qr-read.ps1 -Path photo.jpg [-Save report.txt]
```

Chunks carry `i/n;` prefixes and reassemble automatically; the reader
says `WARNING: n of m chunks` when the report is incomplete, and a
partial report is the one thing not to read past. Re-shoot rather
than trust it.

`tools/qr-read.ps1` is the decoder half of GopQr, written as its exact
inverse and covered by `build/qr-decode-test.ps1`. Earlier revisions of
this file pointed at a `qrshot.py` in a scratchpad, which needed cv2 and
no longer exists.

Press and HOLD a key:

- `EPINT` climbs when the interrupt-IN endpoint completes a transfer
  (this is the verdict number)
- `code` is the completion code (01 = success, 0d = short, other =
  error); `resid` is the event's residual byte count
- `ctl` is the raw event control dword; `trb` is the completed TRB's
  address, `ring` the keyboard transfer ring's base -- matching
  prefixes prove the completion points at our interrupt ring
- `SCANS` climbs when a scancode decodes from the report
- `REPORT` is the raw 8 bytes the controller DMAs into the boot-report
  buffer: `[modifiers, reserved, key1..key6]`. Hold a key and byte 2
  should show the key's HID usage.

Reading it:

| Observation | Meaning |
|---|---|
| `EPINT` stuck at 0, `EP0`/`LATCH` nonzero | The "one event then silence" was enumeration residue -- the interrupt endpoint has NEVER delivered; look at scheduling (interval, root-hub FS servicing) |
| `EPINT` stuck at 0, everything 0 | Controller never completes the transfer -- endpoint not polled (interval / doorbell / ring on real silicon) |
| `EPINT` climbs, `code` != 01 | Transfers complete with an error -- report-buffer or stall problem |
| `EPINT` climbs, `code`=01, `REPORT` all zero while held | Transfer completes but delivers no data |
| `REPORT` byte 2 nonzero while held, `SCANS`=0 | Data delivered; the HID decode is the bug |
| `EPINT` and `SCANS` both climb | The pump works; the bug is in the consumer wiring, not the driver |

The counters are zeroed at start and the status line repaints on a
fixed iteration count (not the PIT tick, which is unreliable on some
firmware), so nothing here depends on the timer, and the per-iteration
pump path allocates nothing.

## SceneProbe.codex

The software 3D pipeline on whatever framebuffer the firmware actually
reported, rendered once and then held so it can be photographed. It
exists because the desktop's 3D view is reached through the first-boot
wizard, and a wizard that stops taking keys hides whether the renderer
works at all: this payload takes no input and needs no ceremony.

It paints the whole panel a band colour, then renders into a rectangle
inset by the sidebar width, so **the untouched band down the left edge
and along the bottom is the containment proof**, visible without a
tool. It prints the panel size and stride it was handed, so a wrong
mode shows up as a number rather than as a smear.

Reading it:

| Observation | Meaning |
|---|---|
| Scene drawn, band intact on left and bottom | The renderer and the content-pane offset are both right |
| Scene drawn but the band is painted over | The base offset is wrong; the scene would eat the desktop chrome |
| Sheared or stepped image | Stride is wrong: compare the printed stride against the printed width |
| Cube red and pyramid blue | The firmware is RGB, not BGR, and nothing reads `PixelFormat` |
| Black panel, text visible | Rasterization ran but wrote nowhere useful; suspect the handoff base |

That fourth row is the one to look at on a new board. The cube is
blue-dominant and the pyramid is red, so a channel swap inverts both
and is obvious at a glance; the stub does not read `PixelFormat`, so
this picture is currently the only thing that would catch it.
Confirmed correct under OVMF at 1280x800.

## Build and run

```powershell
# Build a bootable image (menu-only, no seed/font/source needed):
build/boot/build-option-a.ps1 -Src build/boot/diag/PciProbe.codex `
    -Out build/boot/pci-probe.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx

# PASS -Source '' TOO. It is not covered by -Seed '', and it defaults to
# build-output/Codex.codex, so this command used to ship a 2,994,123-byte
# SOURCE.SRC that no probe here reads.

# THEN GATE THE FILE YOU ARE ABOUT TO FLASH. Not one built from the same
# source with different arguments: OsHardwareRoadmap's Loop A wants a GPT
# structural check AND an OVMF boot of the image FILE, and matching payload
# bytes are not a substitute, because different arguments give a different
# disk size, FAT geometry, file set and partition count. Skipping this cost
# a boot on the ASUS and returned one bit of information.

# PASS -Kernel. Without it the payload is compiled by whatever
# build.ps1 last left in build-output, which is not necessarily the
# depot seed -- an image about to be flashed should be built by the
# compiler the depot actually holds.

build/boot/build-option-a.ps1 -Src build/boot/diag/XhciTruthProbe.codex `
    -Out build/boot/xhci-probe.img -Seed '' -Font '' -Kernel seed/Codex.cdx

build/boot/build-option-a.ps1 -Src build/boot/diag/MscAlignProbe.codex `
    -Out build/boot/msc-align.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx

# Verify under real UEFI (OVMF). This is the instrument -- QEMU's
# monitor screendumps a LIVE guest, so a payload that never exits is
# still readable. Use it, not the codex-vm line build-option-a.ps1
# prints on success: codex-vm's -screenshot writes on EXIT, and every
# probe here ends in an infinite halt loop with no heap progress, so
# the watchdog fires, the process leaves with code -1 and NO BMP is
# written. That is true of the known-good XhciTruthProbe as well, so
# the empty result says nothing about the payload.
build/boot/test-ovmf.ps1 -Img build/boot/xhci-probe.img `
    -Out probe.png -UsbDisk -UsbKbd -UsbMouse -NoPs2
# -NecXhci swaps in a different controller model as a second opinion.

# On real hardware: flash and boot (re-flash before EVERY boot -- a
# stick that re-entered Windows has a corrupted GPT):
build/flash-usb.ps1 -Image build/boot/xhci-probe.img -DiskNumber N   # elevated
```

The keyboard probe is the same build/flash flow with
`-Src build/boot/diag/KbdDiagProbe.codex`. On the keyboard probe no
keypress is needed to read the screen; press keys only to exercise the
pump.
