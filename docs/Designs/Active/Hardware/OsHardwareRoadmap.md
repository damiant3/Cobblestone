# Hardware for the OS Experience

**Created:** 2026-07-09 (fester)
**Status:** Active roadmap
**Relations:** expands `BootRoadmap.md` phases B4/B5 (this doc is what
"the real OS on the framebuffer" needs from hardware); consumes
`GuiOsBringup.md` (the desktop that must land on
metal); supersedes the earlier real-hardware driver phases
(Phase 3/4 items are done or restated here).

---

## The shape of the problem

Two tracks are mature and have never met.

**Track A -- the boot arc (proven on real metal).** Option A stub ->
ExitBootServices -> GOP -> Codex payload. On physical machines today:
GOP framebuffer rendering, PS/2 keyboard, AHCI + IDE storage, GPT +
FAT16 read/write, the seed read from the stick and WakeCeremony-
verified, an identity that survives a power cycle. As of CL 7365/7368,
a post-EBS xHCI + Bulk-Only-Transport + SCSI driver reaches USB mass
storage (emulator-validated; real-xHCI validation pending). But the
payload is a menu and a ceremony, not the OS -- and everything polls.

**Track B -- the GUI OS (proven in the emulator).** `apps/guios/`
GuiShell: a 19-app desktop with mouse, TrueType fonts, a TimingWheel
event loop, and a zero-leak render cycle. It boots through the
compiler's NORMAL boot path and leans on codex-vm conveniences:
scancodes appearing in kernel cells, an IDE font disk, a port-224 VM
exit for pacing, GPA cells for core counts.

**The missing spine.** The real IDT, exception stubs, LAPIC setup,
timer ISR, and syscall MSRs all exist -- emitted by
`codex/compiler/Emit/X86_64Boot.codex` into the normal boot image.
The Option A stub jumps straight to `opening` and configures none of
it. That single omission is why every Track A driver is fuel-bounded
polling, why the tick counter is frozen on the boot path, why the
watchdog needs the `pet` flag, why DiskFacts and every block SYSCALL
is dead post-EBS, and why Track B cannot run on metal. Installing the
runtime services from the payload after ExitBootServices is the
highest-leverage piece of hardware work in the project: it turns
"payload drivers" into "the OS is running."

---

## Inventory -- what we have

Verdicts: METAL = proven on physical hardware or real firmware
(OVMF); EMU = proven under codex-vm only; PROTOCOL = parsing/framing
logic real, transport untested; UI = settings panel, no hardware
behind it; ABSENT = does not exist.

### Display

| Piece | State |
|---|---|
| GOP linear framebuffer, 32-bit XRGB, responsive layout, CBF font 1x/2x | METAL |
| guios rendering (double-buffer, TrueType, shadows/gradients, mouse cursor) | EMU (normal boot path) |
| VGA text / Bochs VBE | EMU (real code, no real-hardware role post-UEFI) |
| GPU-accelerated display (host rasterizer is a codex-vm device; PTX/SPIR-V plugs are compute) | ABSENT on metal |
| Blt-only GOP firmware (no linear FB) | unhandled, rare (named risk in BootRoadmap) |

### Input

| Piece | State |
|---|---|
| PS/2 keyboard, incl. post-EBS re-enable (0xAE/0xF4) + layout machinery | METAL |
| PS/2 mouse (kernel cell packets) | EMU, untested beyond guios |
| USB HID keyboard/mouse post-EBS | ABSENT -- the gap for every PS/2-less modern machine; firmware's PS/2 emulation dies at ExitBootServices |
| UsbHid protocol framing (GET/SET_REPORT, QMK) | PROTOCOL |

### Storage

| Piece | State |
|---|---|
| AHCI read+write (DMA, port stop/start, FLUSH) | METAL (OVMF q35) |
| IDE PIO read+write+flush | METAL-equivalent (OVMF -Machine pc, codex-vm) |
| USB mass storage: xHCI host + BOT + SCSI READ/WRITE(10), chunked bulk, durable | EMU (CL 7365); wired as GopDisk's third path (CL 7368); real-xHCI validation pending |
| GPT + FAT16 read/write, bulk cluster-run reads | METAL |
| NVMe | METAL (OVMF) -- `GopNvme.codex`, H3a below. OVMF boots FROM the namespace; GPT, FAT walk and the 2.15 MB seed all read through the driver's own queues. Dispatched as med-kind-nvme 4. codex-vm has no NVMe model, so the battery cannot exercise it |
| FAT32 | wired into the Gop path (H3b, CL 7442): GopFat16 recognizes the 32-bit layout, and a 36 MB FAT32 ESP boots |
| Volume identity across controllers (which disk is MINE) | ad-hoc (wz-save-identity checks CODEX.CDX); no enumerate-all-disks primitive |
| VirtioBlk / VirtioPci | real-looking code, untested |

### Interrupts, timers, SMP

| Piece | State |
|---|---|
| IDT + exception stubs + LAPIC + PIT/timer ISR + watchdog + syscall MSRs | REAL, but only on the compiler-emitted normal boot path; Option A path has NONE (polling + pet) |
| HPET counter reads | METAL (entropy sampling uses it) |
| ACPI table parsing (MADT/FADT/XSDT) | ABSENT -- LAPIC/IOAPIC layout is assumed, not discovered; no shutdown/reset |
| SMP (atomics, AP boot, per-core sched/heap, IPI) | complete x86-64, normal path; guios `-smp` black screen CANNOT REPRODUCE 2026-07-10 (seed 7756AB2C, three configs: 1-core / -smp 4 / -smp 4 + font disk, all render, all 3 APs boot on the low-memory stacks) -- resolved by intervening work, most plausibly the AP-stack relocation out of the demand-paged range (val CLs 7207-7211); Option A single-core; ARM64/RISC-V atomics+boot not ported |

### Network / Audio / Power / Peripherals

| Piece | State |
|---|---|
| NE2K NIC + NAT | EMU only (ISA NE2000 does not exist on real machines) |
| VirtioNet | untested; e1000/rtl/WiFi ABSENT |
| Intel HDA driver | ABSENT (codex-vm emulates the device; no Codex driver -- `hda-codec-test` actually tests UsbAudio) |
| USB Audio / UVC camera | PROTOCOL (parse-level; UVC discovery tested) |
| Power: ACPI S-states, battery, brightness, [Power] effect | ABSENT (PowerManager et al. in codex/os/dev are UI panels; TheLongFlight IV designs the effect) |
| Bluetooth, printers, gamepads, touchpads | UI only, far |

### Boards beyond x86 (context, not this doc's scope)

Nine IoT boards with register-level drivers on MMIO stubs; ARM64 +
RISC-V backends at battery parity under Renode; GICv3 + generic timer
+ ECAM code present, untested on silicon. TheLongFlight Ascent IV owns
"battery green on physical boards" and ARM self-hosting.

---

## The destination

Boot the stick on any UEFI x86-64 machine and land in the Codex
desktop: wizard or unlock, then a GOP desktop with interrupt-driven
keyboard and mouse (PS/2 or USB), a file browser over the stick's own
filesystem, the editor and compiler live, every disk in the machine
enumerated and named, clean shutdown from a power menu. Offline-first;
network and audio are senses added later, not gates.

---

## Phases

Each phase ends in a demo on real firmware (OVMF is the verdict;
metal sessions are batched confirmations -- the BootRoadmap doctrine).

### H1 -- The spine: runtime services after ExitBootServices

Install, from the payload, what the normal boot path gets from
`X86_64Boot`: IDT + exception stubs (the `!EXC` dump works post-EBS),
LAPIC init + timer tick, PIT or LAPIC-timer heartbeat, syscall MSRs.
Then the kernel's syscall surface (block-*, DiskFacts, process/spawn)
and the guios event-loop model work on the boot path, the tick
counter runs, and `pet` mode becomes unnecessary there.

**H1a -- the `runtime-init` builtin (implementation plan, reverse-engineered
2026-07-09; ready to execute in one focused session).** Decision made: a
new builtin, NOT a `__start` jump. The Option A stub (`option_a_stub.asm`)
keeps the firmware GDT (CS ≈ 0x38), builds a 4 GB identity map, sets
RSP/R10, and jumps straight to `opening`. It never installs a GDT with
CS at selector 8, an IDT, a TSS, the PIC/PIT, or the LAPIC. The IDT
entries `emit-idt-entries` writes reference **selector 8** (the
`add-ri reg-rax 524288` = `8 << 16`), so an IDT alone is not enough -- the
payload must first stand up a GDT with a 64-bit code segment at selector
8 and reload CS into it.

**Why this is seed-safe (the load-bearing property):** register the new
function as a builtin the *compiler source never calls*. The compiler
emits no call to it, so its own boot path and every existing function
body are byte-unchanged; the only effect on the compiler binary is the
added helper's bytes shifting later offsets, which the CDX fixed point
absorbs deterministically. A bug in the runtime-init body therefore
cannot break self-hosting -- worst case the payload demo misbehaves while
the seed still builds, converges one-pass, and ships. This is what makes
a boot-codegen change tolerable to attempt.

**The three edit sites:**
1. `codex/compiler/Types/TypeEnv.codex` (~line 268, beside `port-in-byte`):
   `env-bind ... "runtime-init" (FunTy int-ty-default (concrete-row "Device.Port") int-ty-default)` -- a one-arg (dummy Integer) effectful builtin returning Integer. The payload calls `runtime-init 0`.
2. `codex/compiler/Semantics/NameResolver.codex` (~line 101, the builtin-name list): add `"runtime-init"`.
3. `codex/compiler/Emit/X86_64Chapter.codex` `x86-64-finalize-cdx` (line 918): after `emit-isr-stubs`/`emit-syscall-handler`, before `emit-start`, call a new `emit-runtime-init-fn (state) (isr-result.first-stub-vaddr)` -- `first-stub-vaddr` is the value H1 needs and is only in scope here.

**`emit-runtime-init-fn (st) (first-stub-vaddr)` body** (new function in
`X86_64Boot.codex`; record `"runtime-init"` into `fo-names`/`fo-offsets`
at `st.code-len`, then):
1. Build the 5-entry GDT at `gdt64-base` (75776) exactly as the head of
   `emit-load-tss` does: null; `#00AF9A000000FFFF` (CS @ sel 8);
   `#00CF92000000FFFF` (DS @ sel 16); `#0000890130000067` (TSS lo @ sel
   24); 0 (TSS hi). `lgdt` a pseudo-descriptor (limit 39, base
   `gdt64-base`) via the `sub rsp 16 / store / mov rdi,rsp / lgdt [rdi] /
   add rsp 16` idiom already in `emit-load-tss`.
2. **Reload CS to 8 via a far return** (the one genuinely new sequence,
   because on the normal path CS is already 8 from the trampoline and
   `emit-load-tss` skips it). Emit exactly, so the forward vaddr is a
   fixed +13: `li reg-rax 8; push rax` (the new CS), then measure
   `q = st.code-len`, compute `after = bare-metal-load-addr + q + 13`,
   emit `[72,184] ++ le64(after)` (mov rax,imm64 = 10) ++ `[80]` (push
   rax = 1) ++ `[72,203]` (retfq = 2) = 13 bytes; the label lands at
   `q + 13`. retfq pops RIP then CS, so the stack order is push-CS then
   push-RIP (done above). Do NOT let `li` shrink the vaddr mov -- hand-emit
   `[72,184]++le64` so the size is invariant.
3. Reload data segments: `mov ax,16` then `8E D8` (ds) `8E C0` (es)
   `8E D0` (ss) `8E E0` (fs) `8E E8` (gs).
4. Build the TSS at `tss-base` (77824) + `ltr` selector 24 -- the tail of
   `emit-load-tss` (zero region, store `df-ist-stack-top` at +36,
   `ltr ax=24`). Safe now that CS/GDT are ours.
5. `emit-idt-entries st first-stub-vaddr` then `emit-load-idt`.
6. Mark IDT[8] IST1 (the last three instructions of `emit-load-tss`:
   store byte 1 at `idt-base + 8*16 + 4`).
7. `emit-interrupt-setup` -- this ALREADY does `emit-lapic-disable`,
   `emit-pit-init`, `emit-pic-init`, `emit-kbd-init`, zeroes the
   watchdog/tick/key cells, and ends with `sti`. The legacy PIT→PIC→IDT
   vector 0x20 path drives the tick (LAPIC is left disabled, as on the
   normal path); q35/OVMF emulate the 8259+PIT, so IRQ0 fires. Its
   `emit-vga-progress` writes to 0xB8000 are harmless under GOP (mapped,
   not shown).
8. `li reg-rax 0; ret` (near). Stack is balanced: the near `call`'s
   return frame is untouched (the retfq's 16 bytes were pushed and
   popped within the body), so the near `ret` returns to `opening` with
   CS now 8 -- a valid flat 64-bit code segment covering the same address
   space, so `opening` continues identically.

**Payload side:** `GopBoot.opening` (and the probe) call `runtime-init 0`
as the very first statement, before any GOP draw. Then the tick counter
at `tick-count-addr` (28672) advances and the `pet` compile flag becomes
unnecessary on the Option A path; a fault raises the `!EXC` ISR dump
instead of a silent triple fault.

**Test loop:** (a) full seed rebuild `build/build.ps1` -- MUST report
one-pass fixed point and stay poison-clean (proves the additive change is
fixed-point-safe); (b) `build/boot/build-option-a.ps1 -Src <probe>` where
the probe calls `runtime-init 0` then reads/prints `tick-count-addr` in a
loop; (c) `test-ovmf.ps1` -- the tick value must be non-zero and climbing.
Silent triple-fault = the CS reload or GDT is wrong; bisect by commenting
the `emit-interrupt-setup`/`sti` (no interrupts → no fault → confirms
GDT/IDT install is the culprit vs. an ISR). Keep every attempt on fester;
do not copy up until the demo is green AND the seed is one-pass.

- H1b: **DONE (fester, 2026-07-09).** `apps/works/GopAcpi.codex` -- a
  pure chapter (peek-* carry no effect row), so it is battery-tested
  against a table set built from the spec in a heap buffer with no
  firmware and no device: `codex/test/acpi-parse` (ground truth
  generated independently in Python; SLP_TYPa is deliberately 5, not
  the 0 QEMU happens to publish, so a driver that skips the AML decode
  and hardcodes what it saw on one emulator fails). Covers RSDP rev 0
  (RSDT, 32-bit entries) and rev 2+ (XSDT, 64-bit, preferred), both
  checksums, MADT (LAPIC + Type-5 override, IOAPIC), FADT (DSDT/X_DSDT,
  PM1a EVT+CNT, PM1b CNT, PM1_CNT_LEN, SCI_INT), and a targeted `_S5_`
  AML decode. The stub walks the UEFI configuration table for the ACPI
  2.0 GUID (falling back to 1.0) and leaves the RSDP in cell 0x8028 --
  the RSDP is a vendor table, not a protocol, so LocateProtocol cannot
  find it. Verified on OVMF: RSDP in firmware memory at 0x7F76xxxx (an
  E-segment scan would have missed it), 6 configuration tables, rev 2,
  XSDT root, LAPIC 0xFEE00000, IOAPIC 0xFEC00000, PM1a_CNT 0x604,
  SCI 9, `_S5_` SLP_TYPa 0.

  **Two codex-vm fidelity bugs fixed here.** (1) The fake SystemTable
  kept the GOP interface pointer at offset 112, which the EFI spec
  assigns to `ConfigurationTable` (count at 104); nothing read it, since
  GOP is reached through LocateProtocol, so the slot is now what the
  spec says and carries ACPI 2.0 + 1.0 GUID entries. (2) The emulated
  FADT put DSDT at +36 and the PM1a event/control blocks at +64/+72;
  the spec says FIRMWARE_CTRL +36, DSDT +40, PM1a_EVT +56, PM1a_CNT
  +64. A spec-derived parser read the event block as the control block
  -- a shutdown written there does nothing. The port values (0x600 /
  0x604) were already right and match QEMU. The RSDP is now revision 2
  with a real XSDT beside the RSDT, and the DSDT carries a real
  `Name (_S5_, Package (4) {...})` instead of an empty header.
- Risks: IOAPIC routing variance across boards; double-fault paths on
  the boot stack (the TSS/IST1 step above reuses the demand-paging
  hardening addresses). The CS-reload far return is the one delicate
  spot -- get its byte layout exactly as specified.
- **Demo:** on OVMF, the boot payload prints a live tick counter and
  echoes keys from the keyboard IRQ -- no polling loop.

**Status 2026-07-09: H1a DONE (fester).** Implemented exactly per the
plan above, first attempt green -- no triple-fault bisection needed.
Deltas from the plan as written:
- `emit-runtime-init-fn` takes a THIRD argument
  (`syscall-handler-offset`) and writes the four syscall MSRs
  (STAR/LSTAR/SFMASK/EFER.SCE) after the IDT install -- the H1 goal
  names syscalls and the offset is in scope at the finalize call site.
- Before `emit-interrupt-setup`, the body zeroes the process table
  (512 qwords at proc-table-base) plus current-proc, sched-ready-head,
  sched-current-task, sched-yield-flag, starve-counter, prof-enabled,
  and prof-cursor. The timer ISR walks that state every tick; on the
  normal path `emit-process-setup` initializes it, on the Option A
  path firmware leaves arbitrary bytes there, and a garbage process
  table sends the preemption path through a wild context restore.
- The body begins with `cli` (the stub arrives with whatever IF state
  firmware left; the fragile lgdt-to-CS-reload window must not take an
  interrupt through a stale IDT).
Verdicts: seed rebuilt two-pass -> converged one-pass (Sut === stage1
outside signature bytes); probe (`runtime-init 0`, then eight observed
tick-cell CHANGES drawn as GOP lines, then a key-echo loop over cell
28680) green under codex-vm, green under OVMF/q35 real firmware
(ticks 1..8 strictly climbing, scancode echoed -- and under OVMF only
our own IRQ1 ISR writes that cell), 25 s under `-uefi-strict` with no
fault. The `pet` compile flag is now genuinely optional on the Option
A path once a payload calls runtime-init; build-option-a.ps1 still
passes it (harmless), and removing it is a later cleanup. H1b (ACPI
MADT parse) remains open.

### H2 -- Input breadth: USB HID on the xHCI stack

The biggest named unknown (BootRoadmap B4.3), now de-risked: GopXhci
exists and is battery-tested. Add interrupt-endpoint transfers, HID
boot protocol (SET_PROTOCOL, 8-byte reports), keyboard + mouse class
drivers, and minimal hub enumeration (real laptops route ports
through internal hubs). Harden enumeration for real silicon: port
reset, real input contexts, Set TR Dequeue (replacing the ring-rewind
shortcut), completion via events when H1 lands.

- H2a: **qemu-xhci validation bed -- DONE (fester, 2026-07-09).**
  `test-ovmf.ps1 -UsbDisk` attaches the boot image as
  `qemu-xhci` + `usb-storage`, so the machine boots from a real,
  spec-strict xHCI and reaches its medium only through the USB stack.
  This forced the shipped MSC/xHCI driver from "passes in codex-vm"
  to "passes on real silicon", and surfaced four defects codex-vm's
  leniency hid, each fixed in both the driver and (where the emulator
  was wrong) codex-vm:
  1. **64-bit BAR above 4 GB.** OVMF parks the xHCI at
     0xC000000000, past the identity map. The driver now relocates
     the BAR into the 32-bit MMIO hole post-EBS (decoding off, write
     both dwords, decoding on, confirm by readback).
  2. **ERSTBA dword order.** A real controller latches the event-ring
     segment table when the *high* dword of ERSTBA is written; the
     driver wrote high-before-low, so the latch read a garbage ring
     address and every event went nowhere. Fixed to low-then-high.
     (codex-vm never latched, so it was blind to this.)
  3. **Cycle-managed rings + link TRBs.** The driver's rings now
     carry producer cycle state and a terminal link TRB with the
     toggle-cycle flag; codex-vm's command/transfer-ring walk became
     cycle-aware and link-following (it had walked linearly and
     rewound dequeue pointers, which real hardware forbids). Real
     input contexts (slot + endpoint, route/speed/port fields) and a
     port reset/speed path replaced the codex-vm-shaped shortcuts.
  4. **Async data + pipelined status.** The killer: a real
     controller answers a disk READ from asynchronous backing
     storage. Queuing the data and CSW together on the ring -- which
     codex-vm tolerated -- left QEMU's status transfer pending
     forever. Restructured BOT to three separately-awaited transfers
     (CBW, data, CSW), each with IOC and a transfer-event wait, the
     EDK2/Linux pattern; taught codex-vm to raise a transfer event
     for every bulk TRB that asks for one.
  Verdict screen (booting from the usb-storage stick, post-EBS,
  every pixel drawn by our own driver): enable-slot, address-device,
  config descriptor (44 bytes -- real SuperSpeed companion
  descriptors), endpoint discovery, READ CAPACITY (32768 sectors),
  INQUIRY ("QEMU QEMU HARDDISK"), `lba1: [EFI PART]` (read),
  `write-verify: True` (write + readback). Method for any future USB
  work: `test-ovmf.ps1 -UsbDisk`; drive the trace with
  `--trace usb_xhci_* --trace usb_msd_*` when something diverges.
- H2b: HID keyboard (polling first, IRQ under H1), reusing
  `kb-process-scancode` layout machinery via a HID-usage-to-scancode
  table. Then mouse.

  **Decode DONE (fester, 2026-07-09).** `apps/works/GopHid.codex` turns
  8-byte boot reports into Set-1 scancodes -- the same codes the PS/2 ISR
  produces, so every consumer above (kb-process-scancode, the wizard's
  text field, the boot menu) is unchanged and cannot tell the difference.
  Pure, therefore battery-tested with no controller and no keyboard:
  `codex/test/hid-decode`, ground truth generated independently in Python
  (two transcriptions of the HID usage tables that must agree).

  The report is a *set* of held keys, not a queue -- nothing in it says
  which key changed -- while the consumers take one scancode at a time
  from a one-byte mailbox. Rather than add a queue, `hid-step` returns
  **one event per call and advances `prev` one change toward `cur`**;
  prev converges after as many calls as there were changes. Ordering is
  load-bearing and tested: modifier presses precede key presses (Shift
  before H) and key releases precede modifier releases (H up before
  Shift), so a consumer tracking shift state sees exactly the sequence a
  PS/2 keyboard would have sent. An ErrorRollOver report is discarded
  whole, leaving held keys held. Unmapped usages are consumed rather than
  reported, so prev cannot stall. Gui/RCtrl/RAlt are extended keys and
  report nothing, which is honest; mapping them to their left-hand twin
  would be a lie.

  **Transport DONE (fester, 2026-07-09).** `apps/works/GopUsbKbd.codex`
  enumerates the boot keyboard (class 3 / subclass 1 / protocol 1) from
  the config descriptor, SET_PROTOCOLs it to the boot protocol, and
  configures its interrupt IN endpoint through the new single-endpoint
  input-context builder `xhci-ictx-single`. `xhci-ictx-ep` now writes
  endpoint-context dword 0: `xhci-ep-interval` derives the Interval
  exponent from the descriptor's bInterval and the device speed
  (floor-log frames for full/low speed clamped to the spec's 3-10
  window; bInterval-1 for high/super speed), and periodic endpoints get
  Max ESIT Payload instead of the bulk 512 average. The pump
  (`kbd-pump`) is a three-phase machine driven from the consumer's own
  poll loop, one action per call: idle arms ONE interrupt IN transfer;
  armed checks the event ring with fuel zero (non-blocking, so a NAKed
  transfer costs nothing); drain walks `hid-step` one event per call --
  poking each Set-1 scancode into the cell-28680 mailbox exactly as the
  PS/2 ISR does -- and re-arms only after prev converges, so the report
  buffer is stable under the decoder. A key pressed and released
  entirely between reports is dropped: the same deliberate mailbox
  discipline as GopKey.

  Verdict (spec-strict xHCI, `test-ovmf.ps1 -UsbKbd` = qemu-xhci +
  usb-kbd): enumerated slot 1, dci 3, and delivered H/I/Enter makes AND
  breaks in order through interrupt IN -> boot report -> `hid-step` ->
  mailbox, first try -- the Interval fix read out of the code before any
  build is why. Probes (untracked, `build-output/`): UsbKbdProbe (echo
  loop; allocation-free -- a payload must NEVER return, the stub jumps
  with no return address, so every path ends in a tail loop),
  UsbEnumProbe (per-port slot/class/iface table), UsbMscProbe (MSC
  regression: GPT read + write-readback, green on OVMF post-change).

  **Three codex-vm defects found and fixed (nothing had ever driven two
  devices or the HID path):** (1) ENABLE_SLOT never incremented -- every
  device got slot 1, and personality dispatch keyed on the doorbell
  number; slots now allocate in order and each ADDRESS_DEVICE latches
  its root port from the input context, with personality (storage/HID/
  UVC) keyed on the PORT as on real hardware. (2) The command-ring walk
  parked its resume position in CRCR and reloaded it with `& ~0x3F` --
  CRCR's address field is 64-byte aligned, so up to three consumed
  command TRBs replayed on every doorbell. Invisible while ENABLE_SLOT
  was stateless; slot allocation made each replay mint a phantom slot
  (slots went 1,3,7). Exact position now lives beside CRCR. (3)
  Interrupt IN completed silently -- no transfer event even with IOC --
  and the boot report was built by scanning the PS/2 EVENT QUEUE, so a
  key looked held until the PS/2 consumer drained it and a pure-USB
  guest saw keys stuck forever. Events post per IOC; reports come from
  a real held-key set updated make/break beside every `kbd_enqueue`.

  **Wiring DONE, demo achieved (fester, 2026-07-09): the full
  first-boot ceremony -- welcome, masked passphrase twice, entropy
  sentence, upstream skip, Ed25519 keygen to the fingerprint screen --
  typed entirely over USB HID on a QEMU q35 with `i8042=off` (no PS/2
  controller at all: `test-ovmf.ps1 -UsbKbd -NoPs2`).** The UsbKbd
  handle threads as `WzCtx.wz-kbd`, a `gt-input`/`gt-read-line`
  parameter, and a `menu-loop` argument; every consumer polls
  `kbd-take` (pump once, then `gk-take`), so with no USB keyboard the
  loops are unchanged and PS/2 arrives exactly as before. The welcome
  screen names its input source ("Keyboard: USB HID / PS/2").

  Two bugs found on the way, one in each half:

  1. **`runtime-init` hung forever on a machine with no i8042.** The
  emitted PS/2 init (`emit-kbd-init` / `emit-kbd-wait-ibf` in
  X86_64Boot) waited unboundedly on status-port bits; with nothing
  driving the bus, port 0x64 reads 0xFF and OBF/IBF look permanently
  set -- the payload died before its first pixel. Now the init reads
  0x64 once and skips everything on 0xFF (the floating-bus signature,
  same idiom as the LAPIC probe), and every wait is fuel-bounded.
  Codegen change: seed rebuilt (two-pass converged, one-pass fixed
  point, battery baseline exact). This also fixes the NORMAL boot
  path's `emit-interrupt-setup` on PS/2-less hardware.

  2. **The pump's idle path allocated ~32 bytes per poll.**
  `xhci-wait-event` returns a fresh XhciEvent record per call (and
  `xhci-no-event` is a nullary def -- re-evaluated at every mention),
  so a poll loop allocated its way across the heap until
  `__out_of_memory` halted the machine in seconds -- which reads as a
  dead keyboard with no crash on screen. `kbd-check` now peeks the
  event-ring cycle bit raw and only runs the record-returning consumer
  when an event exists (bounded by keystrokes, not polls). LESSON for
  every Gop poll loop: the idle path must allocate NOTHING, and a
  record-returning helper in a poll loop is an allocation even when
  it returns a "constant".

  **Shared host DONE (fester, 2026-07-09).** `apps/works/GopUsb.codex`
  enumerates the bus ONCE (`usb-attach`): one controller bring-up, one
  port walk classifying each device by its configuration descriptor,
  keyboard and boot-stick disk configured on the same running host.
  The disk handle publishes to a magic-guarded cell block (36480--36567,
  magic "USB1" written last so firmware residue cannot impersonate a
  handle); GopDisk's stateless USB dispatch rebuilds the records from
  the cells and transfers WITHOUT a reset -- the keyboard survives every
  storage call. Three requirements surfaced:

  1. **Slot-aware event routing.** Both devices share one event ring;
  a storage wait filtering by event TYPE alone eats the keyboard's
  completion (and the reverse). Transfer events now latch per-slot in
  the host state block; `xhci-wait-xfer (xh, slot, fuel) -> code` is
  the one transfer wait -- integer-returning and allocation-free, so
  poll loops may call it directly -- and `xhci-wait-event` latches any
  transfer event it consumes while waiting for command completions.

  2. **The IDE floating-bus phantom.** q35 has no legacy IDE; port
  0x1F7 reads 0xFF, which fakes BSY+DRQ+ERR at once -- the fuel-bounded
  waits timed out holding 0xFF, the DRQ test then PASSED, and the
  drain loop "read" sectors of all-ones and reported success. A
  phantom garbage disk outranked the real USB stick in the dispatch
  (and poisoned every ESP mount on q35: "no FAT16 ESP found", failed
  identity saves). Same floating-bus family as the i8042 hang;
  `ide-absent` (one status read, 0xFF retires the controller) now
  gates every IDE entry point.

  3. The whole-arc verdict, on the honest topology (`test-ovmf.ps1
  -UsbDisk -UsbKbd -NoPs2`: boot medium reachable only through USB,
  keyboard only USB, no i8042): full ceremony to Identity Created,
  **"Saved to the stick as IDENTITY.DAT"** through USB WRITE(10),
  storage screens all "via USB" (GPT header, our own BOOTX64.EFI
  through our own FAT walk, the 2.15 MB seed with CDX magic), and the
  WAKE CEREMONY verifying magic + content hash + author signature
  over the USB-read seed. Ascent V rung 3 now holds on the topology
  real hardware actually has. Keyboard-only topology (image on SATA)
  regression-green including the save via AHCI.

  **Hub enumeration DONE (fester, 2026-07-09).** Real laptops route
  their built-in ports through internal hubs, so a keyboard that
  enumerates fine on a QEMU root port simply is not there on the
  machine this arc exists for. `usb-inspect` now classifies a hub
  interface (class 9) alongside keyboard and disk: the hub is
  configured, declared to the controller (Configure Endpoint with the
  slot-context Hub flag and port count -- the input context also
  carries the hub's status-change interrupt endpoint so the command
  is fully formed), every downstream port is powered before any is
  examined, and each connected port is reset, speed-read from the hub
  port status, and enumerated at the route string extended by one
  nibble (`xhci-open-device-at`: route in slot-context dword 0,
  root port kept from the top of the chain, and the parent hub's
  slot/port in dword 2 when a low/full-speed device sits below a
  HIGH-speed hub and needs its transaction translator). The walk
  recurses through nested hubs until the route string's five nibbles
  are spent. All hub waits are budgets of whole control transfers and
  exhaustion FAILS the port (the floating-bus rule). Verdicts, both
  first try on the new topology flag (`test-ovmf.ps1 -UsbHub`: hub on
  root port 1, keyboard BEHIND it at 1.1, disk on root port 2):
  typed text landed masked in the passphrase field with `-UsbKbd
  -UsbHub -NoPs2`, and the FULL ceremony ran on `-UsbDisk -UsbKbd
  -UsbHub -NoPs2` -- boot from USB storage, every keystroke through
  the hub, storage screens all "via USB" including the 2.15 MB seed
  with CDX magic. The TT fields are written from the spec but no bed
  exercises them yet (QEMU's usb-hub is full-speed, so the chain
  carries no splits); first HS-hub encounter is the real-hardware
  session. codex-vm models no hub -- OVMF is the bed, per the H2a
  rule that USB truth comes from real firmware.

  **Medium selection DONE (fester, 2026-07-09).** Dispatch order
  answers "which controller responds", not "which medium is mine" --
  and on a real machine the internal SATA drive responds on AHCI
  before the boot stick responds on USB, so every read above GopDisk
  came from the WRONG disk. The boot medium is now identified by
  CONTENT: `GopMedium.medium-select` (third act of boot-flow, after
  usb-attach) probes each candidate -- USB first when usb-attach
  published a handle (probing through the connect fallback would
  reset the shared controller and kill the keyboard), then each AHCI
  disk in port order (`ahci-find-nth`), then IDE behind the
  floating-bus gate -- by steering GopDisk's new selection cells
  (36608, magic "MED1" written last) and asking the FAT stack one
  question: does this ESP carry our own CODEX.CDX? A hit locks the
  cells and every later read and write -- storage screens, wake
  ceremony, identity save -- lands on the proven medium; no hit
  anywhere clears them and the old dispatch order stands. The probe
  rides the same disk-read-sector seam it configures, so it needed
  no second FAT stack. Verdicts (`test-ovmf.ps1 -Decoy`, a real
  GPT+ESP image with its loader renamed so firmware cannot boot it):
  with the decoy on SATA and the boot medium on USB, the storage
  screen reads everything via USB with the seed verified -- the
  pre-selection behavior was AHCI answering first with the decoy;
  and with the decoy on SATA index 0 and the boot image on SATA
  index 1, the probe skips the CODEX-less disk and locks the second
  AHCI disk. codex-vm regression green (IDE+USB same image, USB
  selected). Decoy recipe: `build-option-a.ps1 -Out decoy.img -Seed
  ''` then rename `BOOTX64 EFI` in the image bytes.

  **Mouse DONE (fester, 2026-07-10).** `apps/works/GopUsbMouse.codex`:
  the boot-protocol mouse (class 3/1/2) over the same xHCI stack --
  SET_PROTOCOL(0), interrupt IN via the keyboard's three-phase pump
  (idle arms one TRB, armed peeks the ring allocation-free through
  the per-slot latch, one report consumed whole per call: byte 0
  buttons, bytes 1-2 signed deltas folded into an absolute position
  clamped to the panel; the left-click EDGE is computed at consume
  time so a press-and-release between polls still registers).
  usb-attach classifies protocol 2 in the same single bus walk
  (UsbAttach/UsbFound gained ua-mouse/uf-mouse). GopBoot: the menu
  loop pumps the mouse per iteration -- hover moves the selection
  bar, left-click fires the row exactly as Enter -- and the cursor is
  drawn save-under (patch + restore, invalidated by any repaint), so
  pointer motion never repaints the panel. Pure decode battery-pinned
  by mouse-decode (sext/clamp/fold/click-edge). Verdict: OVMF
  `-UsbDisk -UsbKbd -UsbMouse -NoPs2` -- full ceremony over the USB
  keyboard, then monitor mouse_move/mouse_button: cursor visible,
  hover re-selects "Graphical UI", click confirms it. test-ovmf.ps1
  gained -UsbMouse and -MouseCmds (semicolon-separated monitor lines
  after -Keys). codex-vm has no USB mouse model; OVMF is the bed
  (H2a rule). NOTE: an absolute-pointer usb-tablet is NOT matched --
  boot protocol only.

  Note codex-vm models BOTH keyboards but
  routes injected/window keys to PS/2 whenever the guest has IRQ1
  unmasked (`ps2_irq_route_live`), so no double delivery; OVMF with
  `-NoPs2` is the honest PS/2-less machine and the byte-exact bed.

  **H5 reset DONE (fester, 2026-07-09).** The menu gained "Restart"
  (five items now). `acpi-reset` honors the FADT's RESET_REG when
  declared -- flags bit 10 at 112, the Generic Address Structure at
  116 (I/O or memory space), RESET_VALUE at 128, all length-guarded
  (a 116-byte FADT has no such fields and reading them is reading the
  next table) -- then falls back to the universal 0xCF9 chipset port,
  then the i8042 pulse, itself gated by the floating-bus check (a
  0xFF status means no controller, and writing reset commands into a
  floating bus is theater). Unlike poweroff, reset never refuses.
  Verdicts: codex-vm prints `RESET: 0xCF9 system reset requested`
  and exits (its FADT now publishes the reset register like real
  chipsets, and 0xCF9 bit 2 is modeled); OVMF genuinely REBOOTS --
  two full BdsDxe boot cycles in one serial log. The pure decode is
  battery-pinned: acpi-parse's rev-2 fixture carries a reset GAS at
  a deliberately non-QEMU address (0x1234, value 0x42), and the
  rev-0 fixture's short FADT pins the length guard.
- H2c: retire or rebase the kernel `Xhci.codex` skeleton (hardcoded
  ring addresses, stub transfer API) onto GopXhci so there is one
  xHCI driver; fix the `hda-codec-test` misnomer.
- **Demo:** type the wizard passphrase on a machine with no PS/2.

### H3 -- Storage breadth: NVMe, FAT32, and "which disk is mine"

- **H3a DONE (fester, 2026-07-09).** `apps/works/GopNvme.codex`: the
  admin queue pair bootstraps everything (IDENTIFY namespace with the
  block size dug out of FLBAS→LBAF, refused honestly unless 512;
  CREATE IO CQ/SQ), then READ/WRITE/FLUSH are opcodes on the one I/O
  queue pair. Completions are recognized by phase tag, polled and
  fuel-bounded; data rides PRP1/PRP2 in eight-sector chunks so the
  PRP-list form is never needed; a write ends with an NVM FLUSH (the
  IDE FLUSH CACHE discipline). The bring-up resets the controller and
  is far too heavy per read, so the first connect publishes its
  handle to magic-guarded cells (36672, "NVM1" last) -- the GopUsbMsc
  pattern. Wired as med-kind-nvme 4: dispatch order AHCI → NVMe →
  IDE → USB, and GopMedium probes NVMe after AHCI. Battery-pinned:
  codex/test/nvme-encode builds every SQE shape with the driver's
  encoders against Python-independent spec ground truth (LBA above
  32 bits pins the dword split; an unaligned buffer pins PRP2).
  Battery baseline is now 339/324/0/15.

  **THE BUG THE BED CAUGHT -- a 4 GB-aligned gamble in BAR parsing.**
  On `-NvmeDisk -UsbKbd -NoPs2` the KEYBOARD died: with two 64-bit
  BAR devices OVMF packs the >4 GB window (NVMe at 0xC000000000,
  xHCI at 0xC000004000), and xhci-init-device trusted a nonzero LOW
  dword alone -- 0x4000 read as a plausible BAR and the controller
  was brought up against low RAM. It had only ever worked because a
  LONE high device lands 4 GB-aligned and reads zero. Diagnosed with
  QEMU monitor `info pci` (the assigned-BAR truth); both drivers now
  read the high dword and relocate when it is nonzero. Lesson for
  every future PCI driver here: "is this BAR reachable" is a
  64-bit question, and the answer changes when a second device
  joins the window.

  **A PCI config read is ZERO-extended, so a BAR value is never
  negative** (measured 2026-07-29 on seed 6671C19A0F78F630, after
  the readback confirmation above was challenged as impossible).
  `port-in-32` emits `xor eax, eax` then `in eax, dx`
  (`emit-port-in-32-helper`), and a 32-bit `in` clears the upper
  half of RAX; `port-out-32` returns 0, so `pci-config-read-raw`'s
  `w + read` is the read. With `rb = 0xFE800004`, masking by `-16`
  and by `4294967280` both give 4269801472 and both compare equal
  to `#FE800000`. The two idioms are interchangeable for every
  value `pci-read-config` can return, and neither loses the
  comparison to sign extension. The masks that do matter are the
  WINDOW bounds: `[3 GB, 4 GB)` is the only mapped device range,
  and below 3 GB is the silent failure because it aliases the
  arena rather than faulting.

  Verdict (`test-ovmf.ps1 -NvmeDisk`, OVMF boots FROM the NVMe
  namespace): full ceremony over USB HID with i8042=off, storage
  screens all "via NVMe" -- GPT header, our own loader through the
  FAT walk, the 2.15 MB seed with CDX magic verified -- every byte
  through the driver's own queues. codex-vm regression green (no
  NVMe model; the PCI scan finds nothing and the chain falls
  through -- OVMF is the NVMe bed, per the H2a rule).
- **H3b DONE (fester CL 7442, 2026-07-10).** FAT32 in the Gop path:
  GopFat16 recognizes the 32-bit layout structurally (a zero 16-bit
  FAT size), maps FAT32's fields into the shared volume record, and
  widens the chain walk, directory scan (high cluster word at entry
  offset 20), bulk reader, and writer. build-img.ps1/-option-a.ps1
  gained -Fat32 and -TotalSectors; a 36 MB FAT32 ESP boots the full
  ceremony under OVMF. Battery test fat32-parse. Landed with a
  compiler fix the 444 KB boot bundle forced: demand-parse-keep-floor
  raised 64 MB -> 192 MB (the keep-deck copy overflowed into the
  scratch it was reading and died in a silent #GP -- a floor that is
  not generous enough fails exactly like the survey it replaced).
- **H3c DONE (fester, 2026-07-10).** `apps/works/GopEnum.codex`: the
  disk inventory. Every candidate GopMedium knows how to probe --
  AHCI ports 0-3, the NVMe namespace, legacy IDE behind the
  floating-bus gate, the published USB handle -- becomes a DiskInfo:
  controller kind/index, model from the device's own identify data,
  sector count, first-ESP volume label, CODEX.CDX presence. New
  driver surface: ahci-identify-on (ATA IDENTIFY 0xEC through the
  same one-PRD command path -- LBA/count are n/a to the command),
  nvme-identify-ctrl (CNS 1; model at bytes 24-63), ide-identify
  (PIO, absent-gated); USB reuses INQUIRY + the sectors already on
  MscDisk. Enumeration rides the med-cells probe seam and SAVES/
  RESTORES the cells (magic last), so a locked boot medium stays
  locked. GopBoot's new Disks screen lists the inventory and marks
  the line whose kind/index match the restored lock. Pure decoders
  (ATA pairwise-swapped strings, LBA48/LBA28 fallback, NVMe model,
  BPB labels at 43/71, printable-stop + space-trim through
  from-unicode) are battery-pinned by disk-enum-parse against
  hand-derived spec fixtures. codex-vm fidelity fix: its IDE model
  never implemented IDENTIFY (the OperatorsManual overstated it) --
  0xEC now answers a synthesized identify sector (model "CODEX VM
  IDE DISK", both sector-count fields) instead of leaving DRQ unset.
  Verdicts: codex-vm -disk shows USB + IDE with USB marked; OVMF
  -UsbDisk -UsbKbd -NoPs2 -Decoy shows the decoy internal disk named
  by ATA IDENTIFY, unmarked (no CODEX.CDX), and the stick marked
  `<- boot medium`; OVMF -NvmeDisk names the controller. NOTE for
  scripted ceremony runs: the Disks screen adds ONE Enter between
  Storage and Wake (fresh first-boot is now 9 Enters to the menu:
  welcome, pass1, pass2, entropy, upstream, complete, storage,
  disks, wake).
- **Demo ACHIEVED:** the boot stick lists every disk in the machine
  by name and points at itself (build-output/ovmf-disks.png shape:
  decoy on SATA named but unmarked, stick on USB marked).

### H4 -- The desktop lands (BootRoadmap B4 made concrete)

Port guios onto the H1 runtime on the Option A path: keyboard/mouse
from real IRQs (PS/2 or H2 HID), LAPIC-timer pacing instead of the
port-224 VM exit, TrueType fonts read from the stick's own FAT via
GopFat16 instead of the IDE font disk, GPA-cell reads replaced with
real sources. Boot menu items become real: Desktop -> GuiShell;
Dev Console -> shell/editor/compile pipeline on the GOP text layer.
Root-cause the guios `-smp` black screen along the way (it blocks
BSP-renders/APs-work).

**The dependency map (surveyed 2026-07-10).** What guios actually
leans on, with the port for each:

| guios convenience | cite | metal replacement |
|---|---|---|
| FB hardcoded 0xBF000000, stride=width, 1024x768 consts | GuiDisplay.codex:13-32, GuiShell.codex:22-23,86 | handoff cells 0x8000 (base/w/h/stride) |
| Geometry via HOST RASTERIZER: MMIO 0xBE000000 cmd buf + ports 0x400-0x402 (GpuRender) | GpuRender.codex:18,59-65; GuiShell.codex:129-133 | DOES NOT EXIST on metal -- software rect walk of the widget tree to the FB (widgets are axis-aligned; GopDraw suffices) |
| Text direct-to-FB via GopBuf aliasing the FB | GuiShell.codex:88-90,156-176 | keep (already metal-shaped) |
| Keyboard: cell 28680 via InputSource ri-take-key + a port-0x60 drain | InputSource.codex:12,48-62 | kbd-take (H2 pump + PS/2 mailbox); DELETE the 0x60 drain -- it races the IRQ1 handler (the documented gk-take race) |
| Mouse: ABSOLUTE ports 0xE1-0xE4 (codex-vm synthesized; kernel 28684 packets unused) | InputSource.codex:50-53 | GopUsbMouse folded absolute state (mouse-x/y/buttons) |
| Pacing: port-out 224 x30/frame (VM-exit throttle) + assumed PIT 18 Hz | GuiShell.codex:116-120,364 | event-driven repaint + PIT tick cell 28672 gates; LAPIC timer later |
| Fonts: TTF over block-read-sector SYSCALL from an IDE FAT32 font disk | FontLoad.codex:161-180; GuiShell.codex:54-61 | gfat-read-file-bulk from the stick's own ESP (syscall path is dead post-EBS on Option A); TTF files land on the ESP via build-img |
| Fallback fonts: SystemFont (baked 9x16) / fl-load-block-font | SystemFont.codex:16-21; FontLoad.codex:589-600 | keep -- no disk needed |
| RTC: CMOS ports 0x70/0x71 | GuiShell.codex:38-39 | keep -- real hardware |
| get-ticks intrinsic / __heap-save gauges | GuiShell.codex:357-392 | keep (tick counter runs on the spine) |

- **H4a DONE (fester, 2026-07-10): the desktop frame on the real
  runtime.** `apps/works/GopDesk.codex`: wallpaper, taskbar with a
  live clock (CMOS RTC read once per second, gated by the PIT tick
  cell), a welcome window with a close box, and the shared save-under
  cursor (moved from GopBoot into GopDraw). Event-driven -- no frame
  loop, no pacing port: idle is a keyboard-mailbox peek plus the
  allocation-free mouse pump. Launched from the boot menu's
  "Graphical UI" item (Enter or mouse click); Esc or the close box
  returns to the menu. Pure pieces pinned by desk-parse (BCD, pad,
  close-box geometry).
- **H4b DONE (fester, 2026-07-10): the software widget walk.**
  `apps/works/GopComposite.codex`: comp-render lays a WidgetNode root
  out against the real panel rectangle (widget-layout + the foreword
  flex engine, untouched) and walks the bounded tree painting with
  GopDraw -- panel/button/gauge fills from the theme's resolved
  ws-bg, labels/buttons text at the GPU walk's own +4/+3 offsets,
  parent-first so children paint over. The host rasterizer is fully
  replaced: the GPU walk only ever emitted axis-aligned fills, so
  nothing was lost. GopDesk's chrome is now built with the real
  widget system (widget-panel/label/button + set-min/set-flex,
  theme-terminal) in GuiShell's root shape; the clock paints in the
  taskbar's own resolved style. GOTCHA recorded in prose: a
  widget-panel is BORN wn-flex=1 -- fixed-edge chrome (sidebar,
  taskbar) must set flex 0 or it splits the axis with its flexible
  sibling (the first OVMF run showed a 640-px sidebar). comp-render
  returns the bounded tree for hit-testing (the buttons go live with
  H4d/H4e). Verdict: OVMF click-to-desktop shows the widget chrome
  at 160/24 with the clock running.
- **H4c DONE (fester, 2026-07-10): TrueType from the stick.**
  `apps/works/GopFont.codex`: gfont-load reads a TTF off the ESP with
  gfat-read-file-bulk (the same driver stack that reads the seed,
  riding the medium lock) and hands the flat buffer to the guios
  pipeline unchanged -- fb-parse-ttf, fb-render-ascii at 16 ppem,
  fl-raster-to-gobf; gfont-text draws through gbf-put-text with a
  GopBuf aliasing the real FB (panel stride as buffer width).
  build-img.ps1/-option-a.ps1 gained -Font (default
  fonts/cc0/cmunss.ttf -> CMUNSS.TTF on the ESP root). The desk's
  window text renders TrueType with a CBF fallback and a status line
  naming which font is on the glass. FOUND + FIXED along the way:
  FontLoad was pre-EffectRows rot -- fl-read-font-sectors /
  fl-read-cluster-sectors / fl-load-fresh (split out fl-load-entry) /
  fl-load-into-role / fl-load-defaults now carry honest
  [Device.Block] rows with act-binds (guios's own GuiShell callers
  will need the same sweep when guios next builds). ALSO: a stale
  test .cdx made a failed recompile read as PASS -- delete the
  output before compile when using compile-then-run checks. Verdict:
  OVMF click-to-desktop shows antialiased CMU Sans body text and the
  TrueType status line.
- **H4d DONE (fester, 2026-07-10): input unification.**
  `codex/foreword/ui/InputSource.codex` gained the metal backend
  without a signature change, so AppRunner/bare-app-tick run
  unchanged. Keyboard: the port-0x60 drain is DELETED (it raced the
  IRQ1 handler for the byte -- the documented gk-take race) and
  ri-take-key is now the same single atomic-exchange gk-take uses;
  the key cell is the only keyboard surface, fed by the ISR or the
  USB pump alike. Pointer: a new pointer mailbox at cells 36736
  ([0] magic "PTR1" written last, [4] x, [8] y, [12] buttons) --
  GopUsbMouse publishes its folded absolute state there (zeroed
  fields then magic at open; consume and warp keep it current, the
  idle pump path untouched) and raw-input-poll prefers the mailbox,
  falling back to the legacy codex-vm ports 0xE1-0xE4 guarded by the
  floating-bus rule (0xFF on 0xE4 means no mouse, not "status bits
  set"). A foreword module cannot cite a Works driver; the
  magic-guarded cells are the bridge, same pattern as the disk
  handles. Battery test input-metal pins the whole surface with the
  cells standing in for the driver: four polls assert absolute
  read, button edges, key make/break events, clamp, and that the
  cell reads zero after a poll (atomic consume). Verdict: OVMF
  q35 -UsbKbd -UsbMouse -NoPs2, a probe running a real BareApp
  tick loop on the Option A runtime -- key-downs 2, key-ups 2,
  clicks 1, pointer at the exact scripted offset, all through the
  unchanged AppRunner path.
  **HOST-LOOP LAW learned here (binding for the H4e app host):
  tick the app when input is pending, not on the PIT alone.** The
  key cell is a one-slot mailbox; QEMU holds a key ~100 ms, so an
  18 Hz-gated consumer loses the make whenever the break lands
  first (observed: key-ups 2, key-downs 1). The probe's loop is
  the pattern: pump kbd+mouse hot (allocation-free idle), and run
  the allocating bare-app-tick when the key cell is non-zero, when
  the mouse pump consumed a report, or when the PIT tick advanced
  -- allocation stays bounded by events + 18 Hz and no edge is
  lost. Probe source: build-output/InputProbe.codex (untracked;
  copy in the session scratchpad).
- **H4e-1 DONE (fester, 2026-07-10): the file manager.**
  `apps/works/GopFiles.codex` browses the boot medium's ESP through
  the same gfat mount every storage screen uses (riding the medium
  lock): list, descend, climb, and a one-sector hex+ASCII preview --
  128 bytes name a file better than a two-megabyte read sitting on a
  heap with no collector. `GopFat16` gained directory LISTING
  (gfat-list-root / gfat-list-dir / gfat-collect-entries): the same
  sector walk as the search, collecting decoded entries, spec-honest
  about the terminal zero entry (it ends the directory GLOBALLY --
  the walk stops at the sector carrying it, so stale garbage past
  the terminal on a dirty volume is never presented as files), FAT32
  high-word clusters via the entry32 decoder, dot entries dropped.
  GopDesk wiring: desk-draw now returns the compositor's bounded
  tree and the sidebar went live -- desk-widget-hit resolves a click
  through ev-hit-widget against that tree, so the Files button opens
  the app (f is the keyboard shortcut). CURSOR LAW (new GopDraw
  cursor-hide, caught by the mouse verdict's ghost): hide BEFORE a
  repaint -- restore the saved patch, then clear the flag. Clearing
  alone is correct only when the repaint covers the cursor; a pane
  covering part of the panel leaves the old cursor pixels standing
  outside it and the next save-under bakes them into the patch, a
  permanent ghost the first restore stamps back. Every repaint site
  (list, preview, no-ESP pane, desk return) hides first and
  re-places after. The app
  loop keeps the desk's law: idle is the mailbox take plus the
  alloc-free mouse pump; listing, repaint, and the preview's single
  disk-read-sector run only on events. Battery test files-parse pins
  the pure surface against a spec-built directory sector (label, LFN,
  dir, dot, FAT32 high-word file, freed entry, terminal, and a
  zombie entry past the terminal that must never surface) decoded as
  both widths, plus geometry/scroll/pad/hex/path. Verdicts on OVMF
  q35 -UsbDisk -UsbKbd -UsbMouse -NoPs2: (A) desktop -> f -> root
  listing EFI <DIR> / CODEX.CDX 2157126 / CMUNSS.TTF 149416 /
  IDENTITY.DAT 124 -- the identity file written by the ceremony THAT
  SAME BOOT, so the listing is also a write-persistence proof;
  (B) Enter-descend EFI -> BOOT -> preview BOOTX64.EFI, 724480 bytes
  (byte-exact vs the host-built optiona.efi) opening 4d 5a "MZ";
  (C) mouse-only: click the sidebar Files button (the widget
  hit-test path), hover-select the CODEX.CDX row, click -> preview.
  CEREMONY SCRIPT NOTE: a fresh first boot needs typed passphrases --
  the key list is welcome-Enter, then "test"-Enter three times
  (pass/confirm/entropy), then five Enters (upstream/complete/
  storage/disks/wake) to the menu; Graphical UI is menu ROW 1 (Down,
  Enter), not the default row.
- **guios REVIVED + -smp CLOSED (fester, 2026-07-10).** GuiShell's
  pre-EffectRows rot is swept (the debt flagged at H4c): rtc-read/
  rtc-time/rtc-date carry [Device.Port] and the LOOP samples the
  clock once per frame, threading the text through ShellState
  (ss-date/ss-time) into pure builders -- shell-build-root/taskbar/
  content and the three clock views take it as data;
  gui-try-load-fonts/fat32-try-init carry [Device.Block];
  shell-loop/shell-frame-pace/shell-composite-render carry
  [Device.Port]; opening declares all three rows. guios.cdx builds
  again (758 KB) on the first post-sweep compile. The `-smp` BLACK
  SCREEN CANNOT REPRODUCE: 1-core, -smp 4, and -smp 4 + font disk
  all render the live desktop (clock running, APs 1-3 started on
  stacks 0x24000/0x28000/0x2C000). Resolved by intervening work --
  most plausibly the AP idle stacks moving from `0xC0000000 -
  i*0x10000` (inside today's demand-paged/GOP region) to
  always-present low memory in the demand-paging hardening, plus
  the timer-delivery and blit overhauls. Screenshots:
  build-output/guios-{1core,smp4,smp4-disk}.png (untracked).
- H4e remaining: the editor + compile pipeline (= the B4 demo).

- **Demo:** the B4 demo -- on metal: browse the stick in the file
  manager, open the editor, compile and run a Codex program, no host,
  no OS.

### H5 -- Power: the machine can turn itself off

**S5 shutdown DONE (fester, 2026-07-09).** `acpi-poweroff` writes
`(SLP_TYPa << 10) | SLP_EN` to the PM1a control block the FADT named,
and the same to PM1b when the machine has a split block. Both the port
and the sleep type are *read out of the firmware's own tables* -- the
famous `outw(0x604, 0x2000)` is what this computes on QEMU, not what it
assumes. It refuses (returns -1, machine stays on, screen says so)
without a parsed FADT, a decoded `_S5_`, and a control-block address.
The legacy ACPI-enable handshake (ACPI_ENABLE -> SMI_CMD, poll SCI_EN)
is deliberately not issued: UEFI firmware puts the machine in ACPI mode
before handing off, and poking a port we have not read would be a guess.

The boot menu's fourth item is now "Power Off" rather than "Reboot",
and it works. Verified: codex-vm exits at the keystroke with
`PM1a_CNT=0x2000` (a value derived from its parsed `_S5_`), and under
OVMF/q35 QEMU exits on its own -- with `-no-shutdown` it reports
`VM status: paused (shutdown)`, i.e. the guest reached the real ACPI
hardware. codex-vm grew a PM1a control block at 0x604 that honors
SLP_EN with SLP_TYP 0 and honestly ignores sleep states it does not
model.

Remaining for H5: reset (0xCF9 or the FADT's RESET_REG), battery and
brightness where trivially reachable (a full EC/ACPI interpreter is
explicitly out of scope), and the PowerManager panel showing real data
or saying honestly that none is available. First rung of
TheLongFlight IV's `[Power]` effect.

- **Demo:** "Power Off" in the boot menu powers the machine off. Done.

### H6 -- The optional senses: network and audio (later, ordered)

Offline-first means these do not gate the OS experience.
Network order when wanted: VirtioNet validated (VMs/cloud) ->
e1000(e) (the ubiquitous Intel NIC) -> USB CDC-ECM/NCM dongle on our
own xHCI stack. WiFi stays out (vendor firmware blobs are exactly
what "if we didn't build it, we don't trust it" excludes).
Audio order: USB Audio class on xHCI (the protocol layer exists) ->
HDA controller driver for built-in codecs. This is Ascent I's
"speech at the edges" substrate.

---

## Test doctrine (generalizing what already worked)

- **QEMU/OVMF real device models are the verdict** for every new
  driver: qemu-xhci, nvme, e1000, AHCI (already), usb-kbd/usb-storage.
  Never trust a codex-vm-only pass for a hardware claim (the fake-
  firmware lesson, the RTSOFF lesson).
- **codex-vm stays the fast inner loop**, growing device models only
  where iteration speed demands, each model fix logged (the emulator-
  fidelity ledger in UEFI-BOOT-INVESTIGATION / BootRoadmap style).
- **Known-answer fixtures built independently from specs** (Python
  ground truth) for every wire/DMA structure -- the ahci-encode /
  usb-bot method.
- **Metal sessions batched** at phase ends; OVMF green is the entry
  ticket.

## The boot-image iteration loop (doctrine, 2026-07-10)

Written after the day the working stick "stopped working" and the
cause turned out to be four stacked actors, none of them the payload:
images shipped without a backup GPT (fixed, CL 7455), Windows GPT
auto-repair rewriting any nonconforming disk ON EVERY INSERTION,
Windows stamping a random MBR signature into a zero
signature field mid-flash (fixed, CL 7457), and the Dell's lazy USB
UEFI enumeration (entries appear only after several reboot/BIOS-visit
cycles; its legacy F12 list is HARDCODED and proves nothing). Full
forensics: UEFI-BOOT-INVESTIGATION.md and the session memory.

The loop that iterates:

**Loop A -- the daily loop. File domain only.** Build the image, then
two gates: structural GPT validation (every header field and CRC,
primary and backup -- the test-gpt PowerShell validator; a Codex-side
validate-img replacement is the durable home) and an OVMF boot of the
image FILE (real edk2 firmware; ceremony on the glass or BdsDxe lines
on serial). Payload work NEVER needs a physical flash to iterate.
An image CL is submittable when both gates are green -- the depot
artifact then IS the record that firmware calls it bootable, which is
the whole point of keeping it in Perforce.

**Loop B -- the hardware checkpoint. Rare; only for questions ONLY
metal can answer** (real xHCI enumeration, real AHCI timing, panel
geometry). Rules learned in blood:
- Flash -> verify -> PULL. The stick never lingers in and NEVER
  returns to a Windows box between flash and boot test. A stick that
  re-entered Windows is presumed rewritten; reflash before trusting.
- Do not run QEMU (or anything) against the raw physical stick
  between flash and boot test -- closing the raw handle invites
  Windows to re-examine the disk.
- On the Dell: cycle reboot / BIOS-visit until the UEFI entry
  appears before concluding anything, and prefer a one-time manual
  boot entry (BIOS Add Boot Option -> \EFI\BOOT\BOOTX64.EFI) to make
  the machine deterministic. Never read its hardcoded legacy device
  list as detection evidence.
- U3-era sticks reserve an unreliable tail INSIDE the reported
  capacity (writes silently dropped, reads sometimes I/O-error).
  This is firmware carve-out, not decay -- but it means any layout
  Windows relocates to the reported end lands on sand. Healthy
  sticks make Windows's relocation harmless.

**Loop C -- the endgame (CurrentPlan gap 3, USB install from Codex).**
The booted system flashes and verifies its own successor stick with
its own drivers (USB WRITE(10) / AHCI / IDE all shipped in this arc).
No Windows in the write path, no borrowed disk stack, no unauthorized
writers. Tonight is the argument for that rung.

## Non-goals for v1 (named so nobody trips on them)

WiFi and Bluetooth radios; GPU-accelerated display on metal (GOP
framebuffer is the display until the GPU driver arc opens); printers,
gamepads, touchpad gestures (UI panels may exist; no hardware work);
exFAT/NTFS; ARM64/RISC-V OS bring-up (owned by Ascent IV, separate
arc); retiring codex-vm via the pure-Codex VMX host (CurrentPlan gap
4, its own mountain).

## Suggested order and why

H2a (qemu-xhci bed) is a day and validates shipped work -- first.
H1 is the multiplier -- everything after it gets events, syscalls, and
the OS stack on metal -- second, and worth doing carefully (it touches
the boot contract). H2 and H3 are independent of each other after H1
and can proceed in either order or in parallel lanes; H4 needs H1
(and H2 for PS/2-less machines); H5 is small once H1b exists; H6
floats. If only one lane exists, the straight line is:
H2a -> H1 -> H2 -> H4 -> H3 -> H5, with H3 promoted earlier if a
target machine's internal storage (NVMe) matters sooner.
