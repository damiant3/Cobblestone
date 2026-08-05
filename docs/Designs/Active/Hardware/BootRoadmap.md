# The Road to Bare Metal: Boot, First-Boot, and the Self-Contained Stick

**Created:** 2026-07-08 (fester)
**Status:** Active roadmap
**Precedes:** the immediate work; follows the UEFI boot breakthrough
and `build/boot/MILESTONE.md` (the boot-proof image).

## REAL HARDWARE, 2026-07-08: the ceremony runs on metal

Flashed `optiona.img` (16 MB, seed 1075CD32) to a 1 GB USB stick and booted
it on a real machine. Confirmed on hardware, no OS beneath it: the first-boot
screen renders, the **PS/2 keyboard works**, and the whole wizard runs --
passphrase accepted, entropy page, Ed25519 fingerprint printed. Every pixel
drawn by a Codex program the compiler compiled.

**The one failure: "Could not save to the stick" -- and it is the boot
medium, not a write bug.** The stick is a **USB mass storage** device
(xHCI + Bulk-Only Transport + SCSI); the storage drivers are AHCI and IDE
PIO, which cannot address USB at all. Under codex-vm the disk is IDE, under
OVMF it is SATA/AHCI, so persistence "worked" in both emulators -- but the
thing you actually run to the hills with is a USB stick, which needs the USB
stack. Worse, `ahci-find` picks the *first SATA disk*, which on a real
multi-disk machine is an internal drive, not the boot medium -- an unguarded
write could land on the wrong disk. (On the test machine the internal HDD was
BIOS-disabled, so AHCI found nothing and the write failed safely; nothing was
touched.)

Two consequences, both handled/planned:
- **Safety guard shipped:** `wz-save-identity` now writes only to a volume
  that carries our own `CODEX.CDX` (the disk we booted from). A stray
  internal FAT ESP lacks it, so we decline rather than risk another disk.
- **USB mass storage is the real next frontier (was B5.5, now load-bearing).**
  Post-EBS xHCI host driver + USB enumeration + BOT + SCSI READ/WRITE, wired
  as a third path under `disk-read-into`/`disk-write-into`. **This is shipped
  and the driver to read is `apps/works/GopXhci.codex` + `GopUsbMsc.codex`,
  not the kernel.** The `codex/os/kernel` USB transport this paragraph used to
  point at (`Xhci`, `UsbMassStorage`, `UsbVideo`) was a duplicate that nothing
  outside its own tests ever cited, and it has been retired; only
  `codex/os/kernel/Usb.codex` survives, for the descriptor structures `UsbHid`
  needs. codex-vm emulates the mass storage device, the HID keyboard, a
  two-tier hub and a UVC camera, so the whole stack is developable and testable
  in the emulator before hardware -- same method as AHCI vs OVMF. It pairs with
  B4.3 (xHCI HID keyboard): both live on the xHCI stack.

## Where we are

Codex boots on **real UEFI hardware** -- ASUS TUF (2015 AMI Aptio V), Dell
Inspiron 15 5000, and edk2/OVMF -- and renders a keyboard-shaped GOP menu,
all compiled by itself, no OS beneath it. Two blockers fell: the FAT12/16
mislabel (the real, years-long bug) and the ASUS CSM/Fast-Boot config. We
now have a **real-firmware test bed** (OVMF in QEMU) so we never guess-and-
flash again. This was the keystone for the entire GOP / bare-metal / first-
boot aspect of the project.

## The destination (from the founding vision)

A single USB stick you can *run to the hills* with: boot it on any UEFI
machine, walk a first-boot ceremony (choose a model, set your private key,
save it to the stick), and land in the full self-hosted Codex OS and
compiler -- everything drawn on the GOP framebuffer, everything a fixed
point of itself, the seed verifying itself from the stick before it acts.
That is Ascent V ("the cord is cut") made physical.

## What is actually on the stick today vs. the destination

| | Today | Destination |
|---|---|---|
| Boot | Option A stub -> GOP -> Codex, reliable | same, signed |
| UI | static menu, top-left, fixed pixels | responsive, centered, real widgets |
| Keyboard | codex-vm cell 28680 (VM-only) | real PS/2 + USB HID post-EBS |
| Storage | none (menu is a stub) | AHCI/IDE + FAT r/w post-EBS |
| Identity | none | Ed25519 keygen, encrypted, saved to stick |
| Seed | not read | read + WakeCeremony-verified from FAT |
| Payload | menu only | full compiler + source + dev console |

## Phases (each ends in a demo on real hardware)

### B1 - Interactive on metal (unblocks everything)

- **B1.1 Real keyboard.** Replace the cell-28680 read in `GopBoot.codex`
  with PS/2 controller reads (ports `0x60`/`0x64`) via the `[Device.Port]`
  effect. Validate arrow-key nav under OVMF (`test-ovmf.ps1 -Keys`, QEMU
  emulates PS/2).

  **Status (2026-07-08): reading works on real firmware; blocked on a
  compiler limit.** Confirmed under OVMF: after ExitBootServices the
  firmware leaves the PS/2 keyboard disabled, so `run-menu` re-enables it
  (`0x64 <- 0xAE`, `0x60 <- 0xF4`); the keyboard then delivers -- we
  observe its `0xFA` ACK read back from port `0x60`. So the read path is
  correct on metal.

  Both compiler blockers are FIXED (2026-07-08, fester):
  1. **Hang watchdog -> `pet` compile flag.** `CDX pet` in the mode header
     (compile.ps1 `-Pet`) selects `WatchdogPet`: every prologue and every
     TCO loop head calls `__pet_watchdog` (zeroes the stale-tick), and the
     watchdog ISR panics only after ~20 quiet ticks (a genuine hang, e.g.
     a stuck instruction). `build-option-a.ps1` always compiles its
     payload with `-Pet`. The pet call had to be emitted at the TCO
     loop-top too -- a TCO'd poll loop never re-runs its prologue, so
     prologue-only petting would still panic after ~1.1 s.
  2. **Effectful-loop TCO -- root-caused and fixed.** The stack growth was
     real: `emit-act-stmts` cleared the TCO tail-position flag to emit a
     bind's value, then threaded the CLEARED state into the remaining
     statements, so any act block with a bind before its final statement
     lost tail position and the closing self-call compiled as a real
     `call`. Fixed by restoring the saved flag between statements (the
     same save/restore `emit-let`/`emit-match` already did). Verified by
     `codex/test/act-tco-loop.codex`: a poll-shaped act loop runs 4 M
     iterations constant-stack; pre-fix it collided stack into heap at
     `__out_of_memory` within ~1 M. Note TCO remains self-recursion-only:
     `GopBoot`'s loop was restructured so the dispatch helpers RETURN the
     new selection and `menu-loop` tail-calls itself.

  The same sweep hardened the block emitters whose cleanup must run after
  their bodies (`handle` bodies + clauses, `with-timeout` bodies, `try`
  fallback/fail lists): each now explicitly clears tail position, so a
  tail self-call inside them can never TCO-jump over handler pops or
  expiry resets.
- **B1.2 Responsive layout. DONE (2026-07-08, fester).** Every position
  derives from the handoff cells: the menu block is centered on the panel
  and glyphs draw as scale-by-scale squares (2x on panels >= 1024 wide).
  Validated at 640x480 (scale 1) and 1024x768 (scale 2) under codex-vm
  and 1280x800 under OVMF. codex-vm fidelity fix: the fake firmware's
  initial GOP mode-info table hardcoded 640x480 regardless of
  `-gop-width`/`-gop-height`, so a stub that reads the current mode (ours)
  drew 640-wide rows into a wider display -- it now reports the
  CLI-selected mode, like real firmware reporting the native panel mode.
- **Demo:** navigate the menu with the arrow keys on the ASUS TUF and Dell
  (deferred -- physical flashing batched with B2+; OVMF is the verdict).

### B2 - The first-boot ceremony (the vision's front door)

- **B2.1 GOP text input. DONE (2026-07-08, fester).** `GopText.codex`: a
  framebuffer text field (echo, block cursor, backspace, capacity limit,
  masked mode for passphrases) whose scancodes come from the raw PS/2
  ports and whose character mapping is the kernel keyboard chapter's pure
  layout machinery (`kb-process-scancode` -- shift, caps, dead keys, all
  layouts). The shared drawing primitives moved to `GopDraw.codex`.
  Battery test `gop-text-field` drives a realistic scancode stream
  (breaks, held shift, nav-key collision, over-capacity) against a
  scratch framebuffer. `Keyboard.codex` got its CDX2051 sweep as part of
  this (bounded scancode parameter + narrows), which revived the
  pre-existing `keyboard-test` failure.
- **B2.2 First-boot wizard on GOP. DONE (2026-07-08, fester).**
  `GopWizard.codex` mirrors FirstBoot's phase model (welcome ->
  passphrase+confirm -> entropy sentence -> upstream -> complete) as GOP
  screens; `GopBoot.opening` runs it before the interface menu. The
  original `FirstBoot.codex` stays ConOut/DiskFacts-bound; the wizard is
  the Option A-native flow, and until B3 lands every boot is a first
  boot.
- **B2.3 Identity keygen. DONE (2026-07-08, fester).** IdentityManager's
  recipe, GOP-native: SHA-256 over (device-seed cell 30576 + typed
  sentence + passphrase + tick count) -> Ed25519 seed; fingerprint =
  sha256-to-hex of the public key, shown on the complete screen; private
  seed wrapped AES-256-CBC under an HKDF-derived key. The wrapped key is
  memory-only until B3. HONEST ENTROPY NOTE (found by testing): the same
  scripted ceremony under codex-vm (WHP) and OVMF (TCG) produced the SAME
  fingerprint -- the device-seed cell was zero on this path and the tick
  sample constant, so identical typed input yielded an identical key on
  any machine. Fixed by B2.4 below before B3 persists any key.
- **B2.4 Real entropy. DONE (2026-07-08, fester).** The Option A stub
  fills the device-seed cell (30576) with 32 hardware-random bytes after
  ExitBootServices: RDRAND gated on CPUID.01H:ECX[30], bounded retries
  per qword, RDTSC degradation for CPUs/emulators without it (TCG). The
  text field folds a per-keystroke HPET main-counter sample (0xFED00000,
  reachable because the stub maps 4 GB) plus the tick cell and scancode
  into an entropy accumulator returned beside the text (GtLine); the
  wizard mixes all three fields' accumulators into the key material.
  ACCEPTANCE: the identical scripted ceremony run twice under codex-vm
  now produces two different fingerprints.
- **Demo:** walk the whole first-boot wizard on real hardware (persistence
  still stubbed). Deferred to the batched physical session; validated
  end-to-end under codex-vm (-keys scripted ceremony, screenshots) and
  OVMF (sendkey ceremony).

### B3 - Bare-metal storage (persistence + the real seed)

The hard part: after ExitBootServices, UEFI's disk and file protocols are
gone. We own the machine and must drive the disk ourselves.

- **B3.1a Post-EBS legacy IDE PIO read. DONE (2026-07-08, fester).**
  `GopDisk.codex` drives the primary IDE channel directly (0x1F0-0x1F7):
  LBA28 READ SECTORS, fuel-bounded BSY/DRQ polls (a missing or wedged
  disk returns a status, never hangs), 256-word data-port transfer into
  a bump-allocated 512-byte buffer. Written in Codex on the existing
  `port-in-16`/`port-in-byte` builtins -- no compiler change needed.
  Battery test `ide-pio-read` reads sectors 0 and 2 of a crafted `.disk`
  sidecar and checks distinct signatures (proves LBA programming, byte
  order, and back-to-back commands). Boot-image proof: `GopBoot`'s
  storage screen reads LBA 1 of its own boot medium after
  ExitBootServices and finds the "EFI PART" GPT signature -- the running
  program reached its own stick with no firmware beneath it. Note
  `VmIde.codex` is the *guest-side emulation* of a controller (for the
  VMX host), not a driver; this is the driver.
- **B3.1b AHCI. DONE (2026-07-08, fester).** `GopAhci.codex`: PCI class
  0x01/0x06 discovery (reusing `Pci.codex`), memory-space AND **bus
  master** enable (the HBA is a DMA master -- without bus master the
  command completes and the buffer stays empty), `GHC.AE`, first port
  with `SSTS.DET == 3` and a SATA-disk signature, port **stopped** before
  its registers are repointed (firmware left the controller running; an
  HBA reading a stale command list would DMA into memory that is now
  ours), self-allocated + zeroed command list / FIS receive area /
  command table, one `READ DMA EXT` through a single PRDT entry,
  fuel-bounded polls that abort on `IS.TFES`. All registers touched at
  32 bits via `peek-32`/`poke-32` -- never another width. `GopDisk`'s
  `disk-read-sector` tries AHCI, then legacy IDE, and names the
  controller that answered.
  Verified: **OVMF q35 (AHCI-only, real firmware) reads its own GPT
  header post-EBS via AHCI**; codex-vm (no AHCI) falls through to IDE PIO
  and says so; battery test `ahci-encode` checks the command header, the
  register H2D FIS, and the PRDT entry against values derived
  independently from the AHCI 1.3.1 / ATA spec (a wrong byte here is a
  wrong command that hardware executes without complaint).
  NOTE the nullary-def trap found writing that test: `scratch =
  alloc-zeroed ...` as a zero-argument def is **re-evaluated at every
  mention**, so each read sees a fresh zeroed buffer and every check
  passes vacuously. Thread the buffer as a parameter.
- **B3.2 FAT16 read post-EBS. DONE (2026-07-08, fester).**
  `GopFat16.codex` mounts the ESP (GPT header -> partition entry array ->
  ESP type GUID -> starting LBA) and reads through `disk-read-sector`.
  It reuses every *pure* helper in the foreword `Fat16` (BPB parse,
  dir-entry decode, name match, path split) and reimplements only the
  sector-reading ones, because `Fat16`'s reads go through the
  `block-read-sector` **syscall**, which the Option A stub never
  configures. File reads write into a flat buffer; `Fat16`'s own reader
  accumulates a `List Integer` per byte, ruinous for a 2 MB seed (B3.3).
  Boot proof: the storage screen mounts the stick and resolves
  `EFI/BOOT/BOOTX64.EFI`, reporting 296960 bytes -- byte-exact with the
  `optiona.efi` on disk. The payload found, through its own drivers and
  its own filesystem code, the file the firmware booted it from.
  Battery test `gop-fat16` runs against a GPT+FAT16 image built
  independently from the specs: ESP discovery, BPB layout, a three-level
  directory walk, a 600-byte file spanning two clusters (proving the FAT
  chain), and a negative lookup.

  **Three CCE bugs fixed at the root in foreword `Fat16` (seed rebuild,
  457E4E37).** Disk bytes are ASCII; `code-to-char` takes a *CCE* code,
  so a raw byte must pass through `from-unicode` first. As written:
  `fat16-extract-chars` made an on-disk "EFI" never equal the literal
  `"EFI"`; `fat16-bytes-to-text` mis-decoded every file's contents (and
  `SOURCE.SRC` is plain ASCII, so DISK compile mode read garbage); and
  `fat16-split-loop` compared the separator against a bare `47`, so no
  path ever split -- `"EFI/BOOT/BOOTX64.EFI"` was looked up verbatim in
  the root. Same family as the wire-encoder bug in `Mqtt`: never feed a
  byte to `code-to-char`.
- **B3.2b FAT write.** Deferred with B3.5 -- it needs the drivers' write
  side (`WRITE DMA EXT` / IDE WRITE SECTORS + flush) and FAT/dir-entry
  mutation.
- **B3.3 Read the real seed. DONE (2026-07-08, fester).**
  `build-img.ps1` takes a `-Seed` and writes it to the ESP root as
  `CODEX.CDX` (the Option A image is now 16 MB to hold it).
  `GopBoot`'s storage screen reads it back with the payload's own
  drivers: **2139712 bytes, CDX magic verified** -- the compiler that
  compiled this program, recovered from the medium it booted from.

  Multi-sector reads were the enabling work. One command per sector is
  ~4200 commands for a 2 MB seed, and on IDE PIO each sector is 256 port
  reads. Both controllers take a count: `ahci-read-into` carries the
  whole transfer in one PRD (one DMA), `ide-read-into` chunks at the
  8-bit count register's 256-sector limit. Above them
  `disk-read-into (lba) (count) (dest)`. `GopFat16` gained a bulk reader
  that loads the FAT **once**, follows the chain in memory, and coalesces
  consecutive clusters into runs -- a freshly written file is contiguous,
  so the seed is a single run of 4180 clusters and one command. The naive
  path cost two reads per sector (data + the FAT sector holding the next
  cluster).

  **codex-vm IDE bug found by the driver.** `ide_advance` added 512 to
  `buf_off`, but `ide_read_data` had already advanced it 2 bytes per word
  across all 256 words -- so every *other* sector was skipped. Invisible
  until something issued a multi-sector `READ SECTORS` (count > 1); real
  hardware was always correct. Guarded now by `gop-fat16`'s `show-bulk`,
  which reads LBA 0..3 as one command and expects "EFI PART" at buffer
  offset 512 (a skipping controller lands the partition-entry array
  there instead).
- **B3.4 WakeCeremony over the real seed. DONE (2026-07-08, fester).
  ASCENT V RUNG 3 IS CLOSED.** On real boot, from bytes it read off its own
  stick, the machine reports:

  ```
  WAKE: verifying self before speaking
    magic: True
    content-hash: True sha256:33dc86e10460bb09
    signature: True author:17b188e7087c217a
  I am Codex, a self-sustaining compiler seed of 2139712 bytes.
  ```

  Confirmed against host truth: the same digest over the same 2018967
  content bytes, and the same author key. The guest really recomputed
  SHA-256 over the seed and really checked an Ed25519 signature.

  `GopWake.codex` runs the ceremony over a **buffer**. `wake-verify` takes
  a `List Integer` -- millions of cells for a 2 MB seed -- so the checks
  are recomputed over memory with `sha256-buf` (constant heap,
  byte-identical to `sha256`), and only the short fields (stored hash,
  author key, signature) become lists. It builds a real `WakeReport`, so
  `wake-sound` and the introduction's prose are shared, not reimplemented.
  A header claiming content past the bytes actually read is refused, never
  hashed. Battery test `gop-wake` builds a CDX-shaped fixture in memory
  whose hash is computed the *other* way (list-based `sha256`), then
  checks: good, one content byte flipped, bad magic, overrun, short, and
  that an unsound seed actually speaks "I will not act".
- **B3.5 Save the key to the stick. DONE (2026-07-08, fester).** The
  write half of the stack, proven across a power cycle: **first boot
  generates the identity and writes it as `IDENTITY.DAT`; a second boot
  against the same durable disk detects it and shows "Welcome Back",
  skipping generation.** The written file is a real serialized identity
  (magic "CIDN", version, salt, iv, public key, passphrase-wrapped seed --
  124 bytes, verified byte-for-byte on the host image), and only the
  wrapped key is secret; it never leaves memory unencrypted.

  The write path mirrors the read path. `ahci-issue-on` now serves read
  and write (the only differences are the command byte 0x35 vs 0x25 and
  the header's write bit); `ide-write-into` chunks `WRITE SECTORS` and
  issues a `FLUSH CACHE` at the end (without the flush a write can sit in
  a volatile cache and a power loss loses exactly the identity we saved).
  `disk-write-into` dispatches AHCI then IDE. `GopFat16` gained a
  single-cluster file writer: find a free cluster, write both FAT copies
  (a FAT with two disagreeing copies is corrupt), write the cluster data,
  and add or replace a root directory entry (a re-save overwrites rather
  than duplicates).

  **codex-vm IDE write bug found by the driver.** `ide_handle_out`
  handled registers 2-7 but dropped register 0 (the data port), so a
  driver writing the data phase with single 16-bit OUTs (not `REP
  OUTSW`) wrote nothing. Never exercised before -- this is the audit the
  B3.3 note demanded, and it paid off. Fixed to route reg 0 to
  `ide_write_data`.

  Battery tests: `disk-write` (sector round-trip + a two-sector write, so
  the write count path can't repeat one sector) and `fat-write` (write a
  file into a mounted volume, read it back, confirm an existing file
  still reads -- the FAT was not corrupted).

- **B3.6 Unlock on return.** A returning boot detects `IDENTITY.DAT` but
  does not yet open it. Re-derive the HKDF key from the passphrase,
  AES-256-CBC-decrypt the wrapped seed, and confirm it against the public
  key. That completes the ceremony: the stick knows you, and proves you
  know it.
- **Demo:** first boot generates + saves your key; second boot unlocks with
  the passphrase; the seed announces it verifies itself, from the stick.

### B4 - The real OS on the framebuffer

- **B4.1 Launch the actual dev console / compiler** from the mode menu (the
  menu items become real, not a stub).
- **B4.2 GOP shell + editor + compile pipeline** rendered on the
  framebuffer (the DevConsole/ConsoleEditor exist for ConOut; port their
  rendering to the GOP text layer from B1/B2).
- **B4.3 USB HID keyboard (xHCI).** The real gap for PS/2-less modern
  laptops *after* ExitBootServices (firmware PS/2 emulation dies with boot
  services). codex.os.kernel has `Xhci`/`Usb`; wire an HID interrupt path.
- **Demo:** compile a Codex program on bare metal, on the ASUS, from the
  GOP shell - no host, no OS.

### B5 - Production hardening

- **B5.1 Sign `BOOTX64.EFI`** so Secure Boot boots it without the user
  disabling it (self-signed + enroll, or a shim).
- **B5.2 Memory-map robustness.** OVMF fit our 16 KB `GetMemoryMap` buffer;
  large AMI maps may not. Size it dynamically + handle `EFI_BUFFER_TOO_SMALL`.
- **B5.3 Self-hosted stub.** Port the proven `option_a_stub.asm` sequence
  into the Codex-emitted PE builder (`PeWriter.codex` / `cdx-to-pe.ps1`) and
  collapse the two builder families to one source of truth. The stub should
  be emitted by Codex, not assembled by MSVC.
- **B5.4 `seed/Codex.img` becomes the real artifact** - full compiler +
  source + first-boot, built through the Option A path (not the stale
  legacy image).

### RULED 2026-07-30 (Damian delegated it permanently: "do whatever makes maximum flexibility, and maximum sense", and it must never come back to him)

**One stub survives IN THE SHIPPING PATH, and it is `cdx-to-pe.ps1`'s.
`option_a_stub.asm` and its ml64 invocation are retired.** That single decision
settles B5.3, most of
B5.4, and the address question below at once, because two stubs is what
created the address question in the first place. `cdx-to-pe.ps1` already
emits its stub as machine code from PowerShell and already acquires GOP, so
the surviving path has no MSVC in it. **MSVC is permitted for `codex-vm` and
nothing else** (Damian, 2026-07-30); `build/boot/build-a1.ps1` carries the
same ml64 call and goes with it.

**0x8000 holds the UEFI SystemTable. It does not hold the framebuffer.**
The compiler's own emitted `__start` reads the SystemTable from 0x8000, so
that meaning is baked into codegen and is the expensive one to move;
`cdx-to-pe.ps1:127` already agrees with it. `option_a_stub.asm`'s
`CELL_FB EQU 08000h` was the other claimant and it is the one that loses.

**The framebuffer moves into a versioned handoff block, not to another bare
address.** A bare address is what let two builders mean two things with
nothing detecting it: booted through the wrong stub, `GopBoot` read a
SystemTable pointer as a framebuffer base, got control, and painted nothing
(measured 2026-07-30 -- serial `s v c h g o`, solid `#104020`). The block
carries a magic, a version, then the framebuffer base/width/height/stride and
`PixelFormat`. **A payload that does not find the magic says so instead of
drawing into a pointer**, which is the property that stops this recurring: new
fields append, and a stub/payload mismatch is loud and specific rather than
silent. `PixelFormat` being in the block also closes the standing gap where
the stub never read it and channel order was assumed.

**The block does NOT live at 0x8000, because 0x8000 is transient and cannot
hold anything.** `X86_64Boot.codex:123-142` is explicit: 0x8000 is where
`emit-build-process-page-tables` puts the PML4 and `emit-start` loads CR3 with
it, so `emit-start` snapshots `[0x8000]` into `uefi-systab-addr` (36208)
BEFORE `emit-process-setup` overwrites it. The slot is live only from
stub-exit until paging is built, and reading it later gets page-table entry
zero -- an invalid-opcode fault the chapter says "cannot ever have worked".
That is why `cdx-to-pe.ps1` already writes the SystemTable to BOTH 0x8000 and
36208: on the UEFI path the stub calls `opening` directly, `__start` never
runs, and the snapshot never happens. **The framebuffer block therefore has to
be durable low memory, written by the stub the same way, and 0x8000 stays a
handoff slot for the SystemTable and nothing else.**

**The block is at `0x1F000` (126976), 48 bytes**, in the hole red vetted for
`xhci-diag`: bounded below by the IST stacks ending at 0x1D000 and above by
the AP idle stacks at 0x20000, so nothing can grow into it without first
overrunning a bound the layout already defends. It sits 8 KB clear of
`xhci-diag` at 0x1D000 and 4 KB below the AP idle stacks. **Checked both
authorities before claiming it, which is the rule those two collisions
taught:** `0x1F000` and `126976` appear in neither `tools/codex-vm.c` nor
`codex/compiler/Emit/**`, `codex/foreword/**` or `apps/works/*.codex`.

    +0x00  magic     8   "CDXHANDF"
    +0x08  version   4   = 1
    +0x0C  size      4   = 48
    +0x10  fb_base   8
    +0x18  fb_width  4
    +0x1C  fb_height 4
    +0x20  fb_stride 4   pixels per scan line, NOT width
    +0x24  fb_format 4   EFI_GRAPHICS_PIXEL_FORMAT, Info+0x0C
    +0x28  acpi_rsdp 8   RSDP from SystemTable->ConfigurationTable

**Step 1 is DONE and every field is measured, not asserted.** `cdx-to-pe.ps1`
publishes the block from inside `GopAcquire`, where `rcx` still holds Info and
`r12` the framebuffer base; the stub grew 774 -> 842 bytes. Read back live
through `-hwwatch <addr> -hwwatch-log`, which was calibrated first against
`0x8000` (fires, `now=0xf0000`, the UEFI tables the emulator reports) so a
miss at the block would have meant something:

| field | read back | bed reports |
|---|---|---|
| magic | `0x46444e4148584443` | "CDXHANDF" exactly |
| fb_base | `0xbf000000` | `GOP: ... framebuffer at 0xbf000000` |
| width, height | `0x280`, `0x1e0` | 640x480 |
| stride, format | `0x280`, `1` | format 1 is BGR |

`fb_format = 1` is BGR, which independently corroborates val's blue-cube /
red-pyramid sitting result from the other end: the stub's long-standing
assumption was right on this firmware, and now the payload is told rather than
left to assume.

**Step 1b is DONE too: the block carries the ACPI RSDP.** `AcpiPublish` walks
`SystemTable->ConfigurationTable` (count at +0x68, array at +0x70, 24 bytes per
entry) for `EFI_ACPI_20_TABLE_GUID` and stores its VendorTable. It runs
unconditionally and OUTSIDE `GopAcquire`, because it needs `r15` and nothing
from GOP, and that body is reached by a rel8 `jnz` with a 127-byte budget; the
scan carries a build-time assertion on its own length so the hand-computed
displacements cannot rot silently. Stub 842 -> 927 bytes.

**The header is written unconditionally and zeroes every payload field before
the scanners run**, which is what makes the magic worth having: read back live,
`acpi_rsdp` takes `0` from the header and then `0xe0000` from the scan, and
`fb_base` takes `0` and then `0xbf000000`, both matching what the bed prints.
So a field left zero provably means "the stub looked and did not find one"
rather than "nobody wrote here" -- the two are indistinguishable at a bare
address, and telling them apart is the entire reason this block exists.

`GopAcquire` in `cdx-to-pe.ps1` currently keeps only FrameBufferBase (R12) and
`VRes * PixelsPerScanLine` (R13d), so it has to read HRes (Info+0x04), VRes
(+0x08), PixelFormat (+0x0C) and PixelsPerScanLine (+0x20) to fill this in.
Stride and width are separate fields on purpose: the real board reports stride
2048 against a visible 1920, and conflating them is what leaves a stripe.

Sequenced this way so no working path is disabled before its replacement is
gated (L-FALLBACK): publish the block from `cdx-to-pe.ps1` alongside the
existing writes; teach `GopBoot` to prefer the block and fall back to
`gop-cell-base`; gate the GOP payload through `build-boot-img.ps1
-BootSource apps\works\GopBoot.codex -Pet`; then make that the default and
delete `option_a_stub.asm`, `build-option-a.ps1`'s ml64 call and
`build-a1.ps1`.
  **Done 2026-08-01. The one deliberate exception is now closed too: the .asm
  and `build-option-a-legacy.ps1` were DELETED 2026-08-03 (Damian's call).**
  They had been kept as the reference for the ASUS display defect, on the
  reasoning that the legacy stub was the only artifact rendering correctly on
  that panel while the new stub was keyboard-without-display. **That defect is
  closed** -- it was the ConOut re-mode (AMI's GraphicsConsole re-modes the
  scanout on the stub's first ConOut call), cured in `cdx-to-pe.ps1` by clearing
  before reading the geometry, and gated since 2026-08-03 by
  `build/boot/test-conout-remode.ps1`. The legacy stub "rendered correctly" only
  because it never called ConOut at all, so it was never the contrast it was
  being read as. One stub now exists anywhere in the tree.
- **B5.5 Storage breadth** - NVMe, more AHCI controllers, USB mass storage.

## Test infrastructure (the discipline that makes this fast)

- **OVMF is the CI for boot.** Every change to the image builder, the stub,
  or GopBoot boots under `test-ovmf.ps1` and is screenshotted before any
  flash. `validate-img.py` statically checks GPT/FAT/PE on every image.
- **Keyboard/nav regression** via QEMU `sendkey` (`-Keys`), and the whole
  first-boot ceremony scriptable end-to-end: codex-vm `-keys` takes Set-1
  make AND break codes, `test-ovmf.ps1 -Keys` takes make codes only (it
  emits both) plus `-AfterKeys` seconds to cover TCG-slow keygen.
- **Machine profile matters for storage.** `test-ovmf.ps1 -Machine pc`
  gives a legacy IDE controller (post-EBS PIO path); the default `q35` is
  AHCI-only and the payload correctly reports no legacy controller there.
- Later: a signed-image + Secure-Boot-on OVMF profile; a small board matrix.
- **codex-vm stays the fast inner loop** (`-uefi-strict` for the boot
  contract), but the *verdict* is OVMF. Never trust codex-vm's fake
  firmware for a boot claim again.

## Risks / unknowns (named so they don't ambush us)

- **Post-EBS USB keyboard** on PS/2-less boards (B4.3) - the biggest
  unknown; a full xHCI HID path is real work.
- **AHCI/NVMe variety** across boards (B3.1, B5.5).
- ~~**GOP Blt-only firmware** (no linear framebuffer)~~ **CLOSED.** The
  Option A payload has no Blt path at all: it paints straight at the
  handoff base after ExitBootServices. A `PixelBltOnly` firmware
  publishes a meaningless `FrameBufferBase`, so a menu on the glass IS
  the proof that the framebuffer is linear, and
  `optiona-milestone.img` renders its menu on both the ASUS TUF and the
  Dell. Neither box is Blt-only and no sitting question is needed.
- **Secure Boot signing** logistics (B5.1).

## Why this serves the other priorities (IoT, codegen)

- The bare-metal GOP UI + storage + **per-device Ed25519 identity from a
  first-boot ceremony** IS the CRA / ETSI EN 303 645 provisioning story for
  IoT firmware - the same code that boots a laptop provisions a device.
- The boot binary is just a CDX, so every codegen improvement (NoAlias,
  WCET, physical-cost) flows into it for free - a smaller, faster boot.
- WakeCeremony-verifying the seed from the stick is the trust-lattice root
  made physical: the device proves its own toolchain before it runs.

## Immediate next action

**B3.6 (unlock on return), then the hardware session.** The whole read/write
storage stack is complete: the machine boots itself, drives its own
controller, mounts its own filesystem, reads and verifies its own seed, and
now writes and re-detects its own identity across a power cycle. Two things
remain.

1. **B3.6 unlock.** A returning boot detects `IDENTITY.DAT`; open it. Read
   the salt and iv, prompt for the passphrase (the GOP text field, masked),
   re-derive the HKDF key, AES-256-CBC-decrypt the wrapped seed, and confirm
   `ed25519-public-key(seed)` equals the stored public key. On success the
   private key is live in memory and the machine is authenticated; on
   failure, refuse and re-prompt. That is the last rung of the ceremony.

2. **The hardware session** (batched, deliberately): flash the 16 MB image
   and walk the whole thing on the ASUS and the Dell -- boot, real PS/2
   keyboard, wizard, RDRAND entropy, AHCI/IDE storage, wake verification,
   and an identity that survives a real power cycle. Everything is validated
   under OVMF (both machine profiles); the hardware run is the physical
   confirmation, not a discovery step. B1, B2,
and B3.1/B3.2 are done: the payload reaches its own boot medium on both
controller families, mounts its own ESP, walks directories, and follows
FAT chains -- it already reads the exact loader the firmware started.

B3.3 puts `seed/Codex.cdx` on the image (`build-img.ps1` takes a
`-Source`; the seed needs the same treatment) and reads it into a flat
buffer with `gfat-read-file`. Watch two things: a 2.1 MB read is ~4200
single-sector commands (a per-command sector *count* would collapse that
-- AHCI has a count field, IDE a sector-count register), and the buffer
must be bump-allocated before the wizard's own allocations.

B3.4 then points `codex/os/verify/WakeCeremony` at that buffer: magic +
recomputed content hash + signature, and it speaks only checked claims or
refuses. That closes Ascent V rung 3 -- the machine proves its own
toolchain, from its own stick, before it acts.

B3.5 (persist the wrapped identity) needs the **write** side of both
drivers (`WRITE DMA EXT` / IDE WRITE SECTORS + flush) and FAT/dir-entry
mutation.

Physical flashing stays deliberately batched: everything is validated
under real firmware (OVMF, both machine profiles), and one hardware
session at the end of B3 can walk the whole ceremony on the ASUS and the
Dell -- with a stick that remembers.
