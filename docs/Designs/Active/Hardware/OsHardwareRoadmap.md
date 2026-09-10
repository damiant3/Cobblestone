# Hardware for the OS Experience

**Status:** Active roadmap
**Relations:** expands `BootRoadmap.md` phases B4/B5 (this doc is what "the
real OS on the framebuffer" needs from hardware); consumes `GuiOsBringup.md`
(the desktop that must land on metal).

---

## The shape of the problem

Two tracks had never met.

**Track A -- the boot arc.** Option A stub -> ExitBootServices -> GOP -> Codex
payload, proven on physical machines: GOP framebuffer rendering, PS/2
keyboard, AHCI + IDE storage, GPT + FAT16 read/write, the seed read from the
stick and WakeCeremony-verified, an identity that survives a power cycle.

**Track B -- the GUI OS.** `apps/guios/` GuiShell: a desktop with mouse,
TrueType fonts, a TimingWheel event loop, and a zero-leak render cycle,
booting through the compiler's NORMAL boot path.

**The spine that joins them is `runtime-init`.** The real IDT, exception
stubs, LAPIC setup, timer ISR and syscall MSRs are emitted by
`codex/compiler/Emit/X86_64Boot.codex` into the normal boot image; the Option
A stub jumps straight to `opening` and configures none of it, which is why
every early Track A driver was fuel-bounded polling with a frozen tick
counter, a `pet` watchdog flag and a dead block SYSCALL surface. `runtime-init`
installs the same services from the payload (H1a below), so the tick runs, a
fault raises the `!EXC` dump instead of a silent triple fault, and the guios
event-loop model works on the boot path.

---

## Inventory

Verdicts: METAL = proven on physical hardware or real firmware (OVMF); EMU =
proven under codex-vm only; PROTOCOL = parsing/framing logic real, transport
untested; UI = settings panel, no hardware behind it; ABSENT = does not exist.

### Display

| Piece | State |
|---|---|
| GOP linear framebuffer, 32-bit XRGB, responsive layout, CBF font 1x/2x | METAL |
| guios rendering (double-buffer, TrueType, shadows/gradients, mouse cursor) | EMU (normal boot path) |
| Software widget walk on the Option A path (`GopComposite`) | METAL (OVMF) |
| TrueType off the boot medium's own ESP (`GopFont`) | METAL (OVMF) |
| VGA text / Bochs VBE | EMU (real code, no real-hardware role post-UEFI) |
| GPU-accelerated display (host rasterizer is a codex-vm device; PTX/SPIR-V plugs are compute) | ABSENT on metal |
| Blt-only GOP firmware (no linear FB) | unhandled, rare (named risk in BootRoadmap) |

### Input

| Piece | State |
|---|---|
| PS/2 keyboard, incl. post-EBS re-enable (0xAE/0xF4) + layout machinery | METAL |
| PS/2 mouse (kernel cell packets) | EMU, untested beyond guios |
| USB HID boot keyboard and mouse post-EBS (`GopHid`, `GopUsbKbd`, `GopUsbMouse`) | METAL (OVMF, `-NoPs2`) |
| USB hub enumeration, nested, route strings | METAL (OVMF full-speed hub); the transaction-translator fields are written from the spec and no bed exercises them |
| Absolute-pointer usb-tablet | ABSENT -- boot protocol only |
| UsbHid protocol framing (GET/SET_REPORT, QMK) | PROTOCOL |

### Storage

| Piece | State |
|---|---|
| AHCI read+write (DMA, port stop/start, FLUSH) | METAL (OVMF q35) |
| IDE PIO read+write+flush | METAL-equivalent (OVMF -Machine pc, codex-vm) |
| USB mass storage: xHCI host + BOT + SCSI READ/WRITE(10), chunked bulk, durable | METAL (OVMF qemu-xhci + usb-storage) |
| GPT + FAT16 read/write, bulk cluster-run reads | METAL |
| NVMe (`GopNvme`, med-kind-nvme 4) | METAL (OVMF boots FROM the namespace). codex-vm has no NVMe model, so the battery cannot exercise it |
| FAT32 in the Gop path (`GopFat16` recognises the 32-bit layout) | METAL (OVMF, a 36 MB FAT32 ESP boots) |
| Volume identity (which disk is MINE): `GopMedium.medium-select` by content, `GopEnum` inventory | METAL (OVMF, decoy disk) |
| VirtioBlk / VirtioPci | real-looking code, untested on x86 |

### Interrupts, timers, SMP

| Piece | State |
|---|---|
| IDT + exception stubs + LAPIC + PIT/timer ISR + watchdog + syscall MSRs | METAL on both paths: emitted by `X86_64Boot` on the normal path, installed by `runtime-init` on the Option A path |
| HPET counter reads | METAL (entropy sampling uses it) |
| ACPI table parsing (RSDP/XSDT/RSDT, MADT, FADT, `_S5_` AML decode) | METAL (`GopAcpi`, OVMF) |
| SMP (atomics, AP boot, per-core sched/heap, IPI) | complete x86-64, normal path; Option A single-core; ARM64/RISC-V atomics+boot not ported |

### Network / Audio / Power / Peripherals

| Piece | State |
|---|---|
| NE2K NIC + NAT | EMU only (ISA NE2000 does not exist on real machines) |
| VirtioNet | untested; e1000/rtl/WiFi ABSENT on this path |
| Intel HDA driver | ABSENT (codex-vm emulates the device; no Codex driver, and `hda-codec-test` cites `UsbAudio`, so its name is wrong) |
| USB Audio / UVC camera | PROTOCOL (parse-level; UVC discovery tested) |
| ACPI S5 poweroff and reset (`acpi-poweroff`, `acpi-reset`) | METAL (OVMF genuinely reboots and powers off) |
| Battery, brightness, `[Power]` effect | ABSENT as DATA; `pm-battery-widget` honestly answers "No battery detected" |
| Bluetooth, printers, gamepads, touchpads | UI only, far |

### Boards beyond x86 (context, not this doc's scope)

Nine IoT boards with register-level drivers on MMIO stubs; ARM64 + RISC-V
backends at battery parity under Renode; GICv3 + generic timer + ECAM code
present, untested on silicon. TheLongFlight Ascent IV owns "battery green on
physical boards" and ARM self-hosting.

---

## The destination

Boot the stick on any UEFI x86-64 machine and land in the Codex desktop:
wizard or unlock, then a GOP desktop with interrupt-driven keyboard and mouse
(PS/2 or USB), a file browser over the stick's own filesystem, the editor and
compiler live, every disk in the machine enumerated and named, clean shutdown
from a power menu. Offline-first; network and audio are senses added later,
not gates.

---

## Phases

Each phase ends in a demo on real firmware (OVMF is the verdict; metal
sessions are batched confirmations, the BootRoadmap doctrine).

### H1 -- The spine: runtime services after ExitBootServices

**DONE.** `runtime-init` is a builtin registered in `TypeEnv.codex` and
`NameResolver.codex` and emitted by `emit-runtime-init-fn` in
`X86_64Boot.codex`, called from `x86-64-finalize-cdx`. A payload calls
`runtime-init 0` as its first statement, before any GOP draw.

**Why the change was seed-safe, and the property is reusable:** the builtin is
one the compiler source never calls, so the compiler's own boot path and every
existing function body are byte-unchanged, and the only effect on the binary is
the added helper's bytes shifting later offsets, which the CDX fixed point
absorbs deterministically. A bug in the body therefore cannot break
self-hosting.

What the body installs, in order: `cli` first (the stub arrives with whatever
IF state firmware left, and the lgdt-to-CS-reload window must not take an
interrupt through a stale IDT); the 5-entry GDT at `gdt64-base`; a CS reload to
selector 8 by far return, because `emit-idt-entries` writes selector 8 into
every gate; the data segments; the TSS at `tss-base` plus `ltr`; the IDT with
IDT[8] marked IST1; the four syscall MSRs (STAR/LSTAR/SFMASK/EFER.SCE); a zero
of the process table (512 qwords at `proc-table-base`) plus current-proc,
sched-ready-head, sched-current-task, sched-yield-flag, starve-counter,
prof-enabled and prof-cursor, because the timer ISR walks that state every tick
and firmware leaves arbitrary bytes there; then `emit-interrupt-setup`, which
ends in `sti`. The legacy PIT -> PIC -> IDT vector 0x20 path drives the tick and
the LAPIC is left disabled, as on the normal path.

`pet` is genuinely optional on the Option A path once a payload calls
`runtime-init`; `build-option-a.ps1` still passes it, harmlessly, and removing
it is a later cleanup.

**H1b, ACPI: DONE.** `apps/works/GopAcpi.codex` is a pure chapter (peek-* carry
no effect row), battery-tested against a table set built from the spec in a
heap buffer with no firmware and no device (`codex/test/acpi-parse`, ground
truth generated independently in Python; SLP_TYPa is deliberately 5, not the 0
QEMU happens to publish, so a driver that skips the AML decode and hardcodes
what it saw on one emulator fails). It covers RSDP rev 0 (RSDT, 32-bit
entries) and rev 2+ (XSDT, 64-bit, preferred), both checksums, MADT (LAPIC +
Type-5 override, IOAPIC), FADT (DSDT/X_DSDT, PM1a EVT+CNT, PM1b CNT,
PM1_CNT_LEN, SCI_INT), and a targeted `_S5_` AML decode.

**The RSDP is a vendor table, not a protocol, so LocateProtocol cannot find
it.** The stub walks the UEFI configuration table for the ACPI 2.0 GUID
(falling back to 1.0) and leaves the RSDP in cell 0x8028. On OVMF it sits in
firmware memory at 0x7F76xxxx, where an E-segment scan would have missed it.

**FADT offsets are spec, not folklore:** FIRMWARE_CTRL +36, DSDT +40, PM1a_EVT
+56, PM1a_CNT +64. Reading the event block as the control block means a
shutdown that does nothing.

Risks: IOAPIC routing variance across boards; double-fault paths on the boot
stack.

### H2 -- Input breadth: USB HID on the xHCI stack

**DONE**, on the topology real hardware has: boot medium reachable only through
USB, keyboard only USB, no i8042.

**H2a, the bed.** `test-ovmf.ps1 -UsbDisk` attaches the boot image as
`qemu-xhci` + `usb-storage`, so the machine boots from a real, spec-strict xHCI
and reaches its medium only through the USB stack. Drive a divergence with
`--trace usb_xhci_* --trace usb_msd_*`. Four requirements of real silicon that
a lenient emulator hides, each now met by the driver:

1. **A 64-bit BAR can sit above 4 GB.** OVMF parks the xHCI at 0xC000000000,
   past the identity map; the driver relocates the BAR into the 32-bit MMIO
   hole post-EBS (decoding off, write both dwords, decoding on, confirm by
   readback). "Is this BAR reachable" is a 64-bit question, and the answer
   changes when a second device joins the window: with NVMe and xHCI both in
   the high window a nonzero LOW dword alone (0x4000) reads as a plausible BAR
   and brings the controller up against low RAM. A LONE high device lands
   4 GB-aligned and reads zero, which is why trusting the low dword ever
   worked. QEMU monitor `info pci` is the assigned-BAR truth.
2. **ERSTBA latches on the HIGH dword.** A real controller latches the
   event-ring segment table when the high dword is written, so high-before-low
   latches a garbage ring address and every event goes nowhere. Write
   low-then-high.
3. **Rings carry cycle state and a terminal link TRB** with the toggle-cycle
   flag. Real hardware forbids walking linearly and rewinding dequeue pointers.
4. **Data and status must be separately awaited.** A real controller answers a
   disk READ from asynchronous backing storage, so queuing the data and CSW
   together leaves the status transfer pending forever. BOT is three
   separately-awaited transfers (CBW, data, CSW), each with IOC and a
   transfer-event wait, which is the EDK2/Linux pattern.

**A PCI config read is zero-extended, so a BAR value is never negative**
(measured 2026-07-29). `port-in-32` emits `xor eax, eax` then `in eax, dx`, and
a 32-bit `in` clears the upper half of RAX, so masking by `-16` and by
`4294967280` are interchangeable for every value `pci-read-config` can return.
The masks that matter are the WINDOW bounds: `[3 GB, 4 GB)` is the only mapped
device range, and below 3 GB is the silent failure, because it aliases the
arena rather than faulting.

**H2b, decode and transport.** `apps/works/GopHid.codex` turns 8-byte boot
reports into Set-1 scancodes, the same codes the PS/2 ISR produces, so every
consumer above is unchanged and cannot tell the difference; it is pure and
battery-tested with no controller and no keyboard (`codex/test/hid-decode`,
ground truth generated independently in Python). The report is a SET of held
keys, not a queue, while consumers take one scancode at a time from a one-byte
mailbox, so `hid-step` returns one event per call and advances `prev` one
change toward `cur`. Ordering is load-bearing and tested: modifier presses
precede key presses and key releases precede modifier releases, so a consumer
tracking shift state sees the sequence a PS/2 keyboard would have sent. An
ErrorRollOver report is discarded whole, leaving held keys held; unmapped
usages are consumed rather than reported, so prev cannot stall; Gui/RCtrl/RAlt
report nothing, which is honest, since mapping them to their left-hand twin
would be a lie.

`apps/works/GopUsbKbd.codex` enumerates the boot keyboard (class 3 / subclass 1
/ protocol 1), SET_PROTOCOLs it to the boot protocol, and configures its
interrupt IN endpoint through `xhci-ictx-single`. `xhci-ep-interval` derives
the Interval exponent from the descriptor's bInterval and the device speed
(floor-log frames for full/low speed clamped to the spec's 3-10 window;
bInterval-1 for high/super speed), and periodic endpoints get Max ESIT Payload
instead of the bulk 512 average. `kbd-pump` is a three-phase machine driven
from the consumer's own poll loop, one action per call: idle arms ONE interrupt
IN transfer; armed checks the event ring with fuel zero (so a NAKed transfer
costs nothing); drain walks `hid-step` one event per call, poking each Set-1
scancode into the cell-28680 mailbox exactly as the PS/2 ISR does, and re-arms
only after prev converges, so the report buffer is stable under the decoder. A
key pressed and released entirely between reports is dropped: the same
deliberate mailbox discipline as GopKey.

`apps/works/GopUsbMouse.codex` is the boot-protocol mouse (class 3/1/2) over
the same pump: byte 0 buttons, bytes 1-2 signed deltas folded into an absolute
position clamped to the panel, the left-click EDGE computed at consume time so
a press-and-release between polls still registers.

**Shared host.** `apps/works/GopUsb.codex` enumerates the bus ONCE
(`usb-attach`): one controller bring-up, one port walk classifying each device
by its configuration descriptor, keyboard, mouse and boot-stick disk configured
on the same running host. The disk handle publishes to a magic-guarded cell
block (36480-36567, magic "USB1" written last so firmware residue cannot
impersonate a handle); GopDisk's stateless USB dispatch rebuilds the records
from the cells and transfers WITHOUT a reset, so the keyboard survives every
storage call. Two requirements follow from sharing:

- **Slot-aware event routing.** Both devices share one event ring, so a storage
  wait filtering by event TYPE alone eats the keyboard's completion.
  Transfer events latch per-slot in the host state block; `xhci-wait-xfer (xh,
  slot, fuel) -> code` is the one transfer wait, integer-returning and
  allocation-free so poll loops may call it directly, and `xhci-wait-event`
  latches any transfer event it consumes while waiting for command completions.
- **Hub enumeration**, because real laptops route their built-in ports through
  internal hubs. `usb-inspect` classifies a hub interface (class 9): the hub is
  configured and declared to the controller (Configure Endpoint with the
  slot-context Hub flag and port count, the input context also carrying the
  hub's status-change interrupt endpoint), every downstream port is powered
  before any is examined, and each connected port is reset, speed-read from the
  hub port status, and enumerated at the route string extended by one nibble
  (`xhci-open-device-at`: route in slot-context dword 0, root port kept from the
  top of the chain, and the parent hub's slot/port in dword 2 when a low or
  full-speed device sits below a HIGH-speed hub and needs its transaction
  translator). The walk recurses through nested hubs until the route string's
  five nibbles are spent, and all hub waits are budgets of whole control
  transfers whose exhaustion FAILS the port. **The TT fields are written from
  the spec and no bed exercises them** (QEMU's usb-hub is full-speed, so the
  chain carries no splits); the first HS-hub encounter is a real-hardware
  session.

**Medium selection.** Dispatch order answers "which controller responds", not
"which medium is mine", and on a real machine the internal SATA drive responds
on AHCI before the boot stick responds on USB, so every read above GopDisk came
from the WRONG disk. `GopMedium.medium-select` (third act of boot-flow, after
usb-attach) probes each candidate by CONTENT -- USB first when usb-attach
published a handle, because probing through the connect fallback would reset
the shared controller and kill the keyboard, then each AHCI disk in port order,
then IDE behind the floating-bus gate -- by steering GopDisk's selection cells
(36608, magic "MED1" written last) and asking the FAT stack one question: does
this ESP carry our own CODEX.CDX? A hit locks the cells for every later read
and write; no hit anywhere clears them and the old dispatch order stands. Decoy
recipe for the bed: `build-option-a.ps1 -Out decoy.img -Seed ''` then rename
`BOOTX64 EFI` in the image bytes.

**THE FLOATING-BUS RULE, which bit three separate drivers.** An absent legacy
device reads 0xFF on its status port, and 0xFF sets every status bit at once,
so an unbounded wait never ends and a "did it succeed" test PASSES. It killed
the payload before its first pixel on a machine with no i8042 (`emit-kbd-init`
now reads 0x64 once and skips everything on 0xFF, and every wait is
fuel-bounded, which also fixes the NORMAL boot path on PS/2-less hardware); it
faked BSY+DRQ+ERR on q35's absent IDE at port 0x1F7, so the drain loop "read"
sectors of all-ones and reported success, outranking the real USB stick in
dispatch and poisoning every ESP mount (`ide-absent` now gates every IDE entry
point); and it appears again on mouse port 0xE4. **Read the port once, retire
the controller on 0xFF, and bound every wait.**

**THE POLL-LOOP ALLOCATION RULE.** `xhci-wait-event` returns a fresh
`XhciEvent` record per call, and `xhci-no-event` is a nullary def re-evaluated
at every mention, so an idle poll loop allocated about 32 bytes per poll and
`__out_of_memory` halted the machine in seconds, which reads as a dead keyboard
with no crash on screen. `kbd-check` peeks the event-ring cycle bit raw and
only runs the record-returning consumer when an event exists. **The idle path
of every Gop poll loop must allocate NOTHING, and a record-returning helper is
an allocation even when it returns a "constant".**

**H2c, still open:** `codex/os/kernel/Xhci.codex` is gone, so there is one xHCI
driver; what remains is the `hda-codec-test` misnomer, which cites `UsbAudio`.

- **Demo, achieved:** the full first-boot ceremony (welcome, masked passphrase
  twice, entropy sentence, upstream skip, Ed25519 keygen to the fingerprint
  screen) typed entirely over USB HID on a QEMU q35 with `i8042=off`, and
  again with the keyboard behind a hub.

### H3 -- Storage breadth: NVMe, FAT32, and "which disk is mine"

**DONE.**

**H3a, NVMe.** `apps/works/GopNvme.codex`: the admin queue pair bootstraps
everything (IDENTIFY namespace with the block size dug out of FLBAS -> LBAF,
refused honestly unless 512; CREATE IO CQ/SQ), then READ/WRITE/FLUSH are
opcodes on the one I/O queue pair. Completions are recognised by phase tag,
polled and fuel-bounded; data rides PRP1/PRP2 in eight-sector chunks so the
PRP-list form is never needed; a write ends with an NVM FLUSH. The bring-up
resets the controller and is far too heavy per read, so the first connect
publishes its handle to magic-guarded cells (36672, "NVM1" last). Dispatch
order is AHCI -> NVMe -> IDE -> USB. `codex/test/nvme-encode` builds every SQE
shape with the driver's encoders against Python-independent spec ground truth
(an LBA above 32 bits pins the dword split; an unaligned buffer pins PRP2).

**H3b, FAT32.** `GopFat16` recognises the 32-bit layout structurally (a zero
16-bit FAT size), maps FAT32's fields into the shared volume record, and widens
the chain walk, directory scan (high cluster word at entry offset 20), bulk
reader and writer. `build-img.ps1` / `build-option-a.ps1` carry `-Fat32` and
`-TotalSectors`; battery test `fat32-parse`. It landed with a compiler change
the 444 KB boot bundle forced: `demand-parse-keep-floor` raised 64 MB to
192 MB, because the keep-deck copy overflowed into the scratch it was reading
and died in a silent #GP. **A floor that is not generous enough fails exactly
like the survey it replaced.**

**H3c, the disk inventory.** `apps/works/GopEnum.codex` turns every candidate
GopMedium knows how to probe (AHCI ports 0-3, the NVMe namespace, legacy IDE
behind the floating-bus gate, the published USB handle) into a DiskInfo:
controller kind and index, model from the device's own identify data, sector
count, first-ESP volume label, CODEX.CDX presence. Driver surface:
`ahci-identify-on` (ATA IDENTIFY 0xEC through the same one-PRD command path,
LBA and count being n/a to the command), `nvme-identify-ctrl` (CNS 1, model at
bytes 24-63), `ide-identify` (PIO, absent-gated); USB reuses INQUIRY plus the
sectors already on MscDisk. Enumeration rides the med-cells probe seam and
SAVES and RESTORES the cells (magic last), so a locked boot medium stays
locked. Pure decoders (ATA pairwise-swapped strings, LBA48/LBA28 fallback, NVMe
model, BPB labels at 43/71, printable-stop plus space-trim through
from-unicode) are pinned by `disk-enum-parse` against hand-derived spec
fixtures.

- **Demo, achieved:** the boot stick lists every disk in the machine by name
  and points at itself.

### H4 -- The desktop lands (BootRoadmap B4 made concrete)

Port guios onto the H1 runtime on the Option A path. What guios leans on, and
the metal replacement for each:

| guios convenience | cite | metal replacement |
|---|---|---|
| FB hardcoded 0xBF000000, stride=width, 1024x768 consts | GuiDisplay.codex:13-32, GuiShell.codex:22-23,86 | handoff cells 0x8000 (base/w/h/stride) |
| Geometry via HOST RASTERIZER: MMIO 0xBE000000 cmd buf + ports 0x400-0x402 (GpuRender) | GpuRender.codex:18,59-65; GuiShell.codex:129-133 | DOES NOT EXIST on metal; the software rect walk in `GopComposite` (the GPU walk only ever emitted axis-aligned fills, so nothing was lost) |
| Text direct-to-FB via GopBuf aliasing the FB | GuiShell.codex:88-90,156-176 | keep (already metal-shaped) |
| Keyboard: cell 28680 via InputSource ri-take-key + a port-0x60 drain | InputSource.codex:12,48-62 | `kbd-take`; the 0x60 drain is DELETED, since it raced the IRQ1 handler for the byte |
| Mouse: ABSOLUTE ports 0xE1-0xE4 (codex-vm synthesized) | InputSource.codex:50-53 | the pointer mailbox at 36736, fed by `GopUsbMouse` |
| Pacing: port-out 224 x30/frame (VM-exit throttle) + assumed PIT 18 Hz | GuiShell.codex:116-120,364 | event-driven repaint + PIT tick cell 28672 gates; LAPIC timer later |
| Fonts: TTF over block-read-sector SYSCALL from an IDE FAT32 font disk | FontLoad.codex:161-180; GuiShell.codex:54-61 | `gfat-read-file-bulk` from the stick's own ESP; TTF files land on the ESP via build-img |
| Fallback fonts: SystemFont (baked 9x16) / fl-load-block-font | SystemFont.codex:16-21; FontLoad.codex:589-600 | keep, no disk needed |
| RTC: CMOS ports 0x70/0x71 | GuiShell.codex:38-39 | keep, real hardware |
| get-ticks intrinsic / __heap-save gauges | GuiShell.codex:357-392 | keep (the tick counter runs on the spine) |

**H4a, the desktop frame.** `apps/works/GopDesk.codex`: wallpaper, taskbar with
a live clock (CMOS RTC read once per second, gated by the PIT tick cell), a
welcome window with a close box, and the shared save-under cursor. Event-driven:
no frame loop, no pacing port, idle is a keyboard-mailbox peek plus the
allocation-free mouse pump.

**H4b, the software widget walk.** `apps/works/GopComposite.codex`:
`comp-render` lays a WidgetNode root out against the real panel rectangle
(widget-layout plus the foreword flex engine, untouched) and walks the bounded
tree painting with GopDraw, parent-first so children paint over, and returns the
bounded tree for hit-testing. **A widget-panel is BORN `wn-flex=1`**, so
fixed-edge chrome (sidebar, taskbar) must set flex 0 or it splits the axis with
its flexible sibling.

**H4c, TrueType from the stick.** `apps/works/GopFont.codex`: `gfont-load` reads
a TTF off the ESP with `gfat-read-file-bulk` (the same driver stack that reads
the seed, riding the medium lock) and hands the flat buffer to the guios
pipeline unchanged; `gfont-text` draws through `gbf-put-text` with a GopBuf
aliasing the real FB (panel stride as buffer width). `build-img.ps1` and
`build-option-a.ps1` carry `-Font` (default `fonts/cc0/cmunss.ttf` ->
`CMUNSS.TTF` on the ESP root). **A stale test `.cdx` makes a failed recompile
read as PASS: delete the output before compiling when using compile-then-run
checks.**

**H4d, input unification.** `codex/foreword/ui/InputSource.codex` gained the
metal backend without a signature change, so AppRunner and `bare-app-tick` are
unchanged. The key cell is the only keyboard surface, fed by the ISR or the USB
pump alike, and `ri-take-key` is the same single atomic exchange `gk-take` uses.
The pointer mailbox is at cells 36736 ([0] magic "PTR1" written last, [4] x, [8]
y, [12] buttons): `GopUsbMouse` publishes its folded absolute state there and
`raw-input-poll` prefers the mailbox, falling back to the legacy codex-vm ports
0xE1-0xE4 under the floating-bus rule. A foreword module cannot cite a Works
driver, so the magic-guarded cells are the bridge, the same pattern as the disk
handles. Battery test `input-metal` pins the surface with the cells standing in
for the driver.

**THE HOST-LOOP LAW, binding for any app host: tick the app when input is
pending, not on the PIT alone.** The key cell is a one-slot mailbox and QEMU
holds a key about 100 ms, so an 18 Hz-gated consumer loses the make whenever
the break lands first. The pattern is: pump keyboard and mouse hot
(allocation-free idle), and run the allocating `bare-app-tick` when the key cell
is non-zero, when the mouse pump consumed a report, or when the PIT tick
advanced. Allocation then stays bounded by events plus 18 Hz and no edge is
lost.

**H4e-1, the file manager.** `apps/works/GopFiles.codex` browses the boot
medium's ESP through the same gfat mount every storage screen uses: list,
descend, climb, and a one-sector hex+ASCII preview, because 128 bytes name a
file better than a two-megabyte read sitting on a heap with no collector.
`GopFat16` gained directory LISTING (`gfat-list-root` / `gfat-list-dir` /
`gfat-collect-entries`): the same sector walk as the search, spec-honest about
the terminal zero entry, which ends the directory GLOBALLY, so the walk stops at
the sector carrying it and stale garbage past the terminal on a dirty volume is
never presented as files. Battery test `files-parse` pins the pure surface
against a spec-built directory sector including a zombie entry past the terminal
that must never surface.

**THE CURSOR LAW: hide BEFORE a repaint**, which means restore the saved patch
and then clear the flag. Clearing alone is correct only when the repaint covers
the cursor; a pane covering part of the panel leaves the old cursor pixels
standing outside it, and the next save-under bakes them into the patch, a
permanent ghost the first restore stamps back.

**H4e remaining: the editor and compile pipeline, which is the B4 demo.**

**Ceremony script note.** A fresh first boot needs typed passphrases: welcome
Enter, then "test" Enter three times (pass, confirm, entropy), then five Enters
(upstream, complete, storage, disks, wake) to the menu. Graphical UI is the
menu's default row, so a plain Enter selects it.

- **Demo:** on metal, browse the stick in the file manager, open the editor,
  compile and run a Codex program, no host, no OS.

### H5 -- Power: the machine can turn itself off

**Shutdown and reset are DONE.** `acpi-poweroff` writes `(SLP_TYPa << 10) |
SLP_EN` to the PM1a control block the FADT named, and the same to PM1b when the
machine has a split block. Both the port and the sleep type are read out of the
firmware's own tables: the famous `outw(0x604, 0x2000)` is what this COMPUTES on
QEMU, not what it assumes. It refuses (returns -1, machine stays on, screen says
so) without a parsed FADT, a decoded `_S5_` and a control-block address. The
legacy ACPI-enable handshake (ACPI_ENABLE -> SMI_CMD, poll SCI_EN) is
deliberately not issued: UEFI firmware puts the machine in ACPI mode before
handing off, and poking a port we have not read would be a guess.

`acpi-reset` (`GopAcpi.codex:391`) honours the FADT's RESET_REG when the table
declares one (flags bit 10 at 112, the Generic Address Structure at 116, in I/O
or memory space, RESET_VALUE at 128, all length-guarded, since a 116-byte FADT
has no such fields and reading them is reading the next table), then falls back
to the universal 0xCF9 chipset port writing 2 then 6, then to the i8042 pulse
gated by the floating-bus check. Unlike poweroff, reset never refuses. The pure
decode is battery-pinned: `acpi-parse`'s rev-2 fixture carries a reset GAS at a
deliberately non-QEMU address (0x1234, value 0x42), and the rev-0 fixture's
short FADT pins the length guard.

**Genuinely remaining:** real battery and brightness DATA where trivially
reachable, which nothing supplies yet. A full EC/ACPI interpreter stays
explicitly out of scope. First rung of TheLongFlight IV's `[Power]` effect.

### H6 -- The optional senses: network and audio (later, ordered)

Offline-first means these do not gate the OS experience. Network order when
wanted: VirtioNet validated (VMs and cloud) -> e1000(e), the ubiquitous Intel
NIC -> USB CDC-ECM/NCM dongle on our own xHCI stack. WiFi stays out, because
vendor firmware blobs are exactly what "if we didn't build it, we don't trust
it" excludes. Audio order: USB Audio class on xHCI (the protocol layer exists)
-> HDA controller driver for built-in codecs. This is Ascent I's "speech at the
edges" substrate.

---

## Test doctrine

- **QEMU/OVMF real device models are the verdict** for every new driver:
  qemu-xhci, nvme, e1000, AHCI, usb-kbd, usb-storage. Never trust a
  codex-vm-only pass for a hardware claim.
- **codex-vm stays the fast inner loop**, growing device models only where
  iteration speed demands, each model fix logged.
- **Known-answer fixtures built independently from specs** (Python ground
  truth) for every wire or DMA structure.
- **Metal sessions batched** at phase ends; OVMF green is the entry ticket.

## The boot-image iteration loop

**Loop A -- the daily loop, file domain only.** Build the image, then two
gates: structural GPT validation (every header field and CRC, primary and
backup: the test-gpt PowerShell validator, with a Codex-side validate-img
replacement as the durable home) and an OVMF boot of the image FILE. Payload
work NEVER needs a physical flash to iterate. An image CL is submittable when
both gates are green; the depot artifact then IS the record that firmware calls
it bootable.

**Loop B -- the hardware checkpoint. Rare; only for questions ONLY metal can
answer** (real xHCI enumeration, real AHCI timing, panel geometry). The rules,
each learned from a boot that failed for a reason outside the payload:

- Flash, verify, PULL. The stick never lingers in and NEVER returns to a
  Windows box between flash and boot test: Windows GPT auto-repair rewrites any
  nonconforming disk ON EVERY INSERTION. A stick that re-entered Windows is
  presumed rewritten; reflash before trusting.
- Do not run QEMU, or anything, against the raw physical stick between flash and
  boot test; closing the raw handle invites Windows to re-examine the disk.
- On the Dell: cycle reboot and BIOS-visit until the UEFI entry appears before
  concluding anything, and prefer a one-time manual boot entry (BIOS Add Boot
  Option -> \EFI\BOOT\BOOTX64.EFI). Its hardcoded legacy F12 device list proves
  nothing and is never detection evidence.
- U3-era sticks reserve an unreliable tail INSIDE the reported capacity (writes
  silently dropped, reads sometimes I/O-error). This is firmware carve-out, not
  decay, but it means any layout Windows relocates to the reported end lands on
  sand.

**Loop C -- the endgame (USB install from Codex).** The booted system flashes
and verifies its own successor stick with its own drivers (USB WRITE(10), AHCI
and IDE all shipped in this arc). No Windows in the write path, no borrowed
disk stack, no unauthorized writers.

## Non-goals for v1

WiFi and Bluetooth radios; GPU-accelerated display on metal (the GOP
framebuffer is the display until the GPU driver arc opens); printers, gamepads,
touchpad gestures (UI panels may exist, no hardware work); exFAT and NTFS;
ARM64/RISC-V OS bring-up (owned by Ascent IV, a separate arc); retiring codex-vm
via the pure-Codex VMX host (its own mountain).
