# Boot Diagnostics

Standalone Option A GOP payloads for diagnosing hardware bring-up on
real machines, where there is no serial log and the ceremony cannot
advance if the keyboard does not work. Each paints its findings to the
framebuffer and sits (it never returns), so it can be photographed off
the glass.

`Diag` is the ladder that runs the probes as stages (below); it is what a
new metal question rides. `PciProbe` reads the PCI bus and answers what the parts ARE.
`XhciTruthProbe` and `KbdDiagProbe` answer what the USB stack DID with
them; both read the same diagnostic cell block (0x1D000) that the real
xHCI bring-up fills (`apps/works/GopXhci.codex`), so their numbers are
exactly what the boot path saw. `SceneProbe` answers whether the
DISPLAY path is right: the mode the firmware handed us, the row stride,
the channel order, and whether a rendered rectangle stays inside its
own bounds.

**Since 2026-08-18 a new metal question is a STAGE in the diagnostic ladder,
not a new one-question image, and it flies on a grouped sitting composed by
red**: `docs/Designs/Active/OS/DiagnosticStick.md` has the ladder order, the
stage shape and the lane procedure. The probes below are the stages' source
and this file stays the account of what each one reads. Before ANY flash,
`docs/Hardware/HardwareSitting.md` QUICKREF binds: dump the stick first,
rehearse the exact bytes in the bed, and name MOUNT-AND-COPY or FLASH before
the elevated command; the build-and-run section at the end of this file does
not repeat those rulings and is not a substitute for them.

Use the USB two when a machine boots the Codex payload but finds no USB
keyboard, no boot stick, or nothing on the USB bus at all.

**Before reading any probe, read the screen COLOUR.** Every Option A
image carries two liveness marks, painted by `cdx-to-pe.ps1`'s stub at
the same two points the deleted `option_a_stub.asm` painted them (checked
in source 2026-08-02, not assumed from the migration): solid dark
blue once GOP is acquired, solid dark green once ExitBootServices and
our own page tables are live. An unchanged firmware screen means we were
never loaded or `LocateProtocol(GOP)` failed; blue alone means the stub
died in allocation, `GetMemoryMap` or `ExitBootServices`; green alone
means the payload died. Without them every one of those is a black
screen, because every failure path in the stub ends at `fatal`, which is
`jmp fatal`. `docs/Hardware/HardwareSitting.md` boot 1 has the table.

## DIAG.RCP, the rehearsal record, and -Rehearsed

The ESP carries `DIAG.RCP` beside `DIAG.ID`: the recipe that produced the
payload (id, kernel digest, payload/EFI hashes, alloc pages, sectors, ring
text, cfg, newest `build/boot/diag/...` CL), written by `build-diag.ps1`; the
payload banks it as `rcp key=value` rows right after the bank row. No image
hash and no timestamp inside, so the same source and seed rebuild to the same
image hash. `diag-arm.ps1` appends the image's SHA-256 to
`build/boot/diag.rehearsed` only after a FULL run (every arm, both beds);
`build/flash-usb.ps1 -Rehearsed` refuses any image not on that list, and
`-ExpectHash` pins the flight card's hash. Partial runs (`-Only`, `-SkipOvmf`)
leave the record alone and say so.

## Diag.codex -- the ladder

One payload, one image, `build/boot/diag.img`. It runs the stages in the
design's order and never returns: `smbios`, `edid` and `cpu` (DiagSmbios,
DiagEdid, DiagCpu: the passive firmware-table and CPUID rows), `pci`
(DiagPci.codex, the walk and BAR verdict lifted out of PciProbe) and `scene`
(DiagScene.codex, the software-3D scene lifted out of SceneProbe, rendered
into the stage's own row slot), then it brings the USB stack up, finds the medium whose ESP
carries a `DIAG.ID` matching the id baked into the image, and writes
`DIAG.TXT`. Every channel is wired: the fixed page (font and stride proof,
eight colour bars, identity row, box row, three rows per stage, the summary
band, the QR block), the bank, and serial (the same lines the bank holds).

```powershell
build/boot/build-diag.ps1                     # compile by the depot seed -> build/boot/diag.img
                                              # takes diag-default.cfg, which NAMES every non-passive stage;
                                              # a cfg that leaves one unnamed ARMS it, and the build refuses.
                                              # It also refuses a key named TWICE: diag-cfg-find returns on its
                                              # FIRST match and the ring is read before the ESP file, so a later
                                              # line is dropped in silence. `b3 on` then `b3 peer=...` read as
                                              # asked to the composer and no-peer on the box (red, 2026-08-21)
build/boot/build-diag.ps1 -StdinCfg "scene off"   # a DIAG.CFG baked into the stub ring
build/boot/build-diag.ps1 -Cfg my.cfg         # a DIAG.CFG on the ESP (post-bank stages only)
build/boot/diag-arm.ps1                       # rehearse: 34 codex-vm arms + 2 OVMF arms; a full green run appends the image hash to diag.rehearsed
build/boot/diag-arm.ps1 -SkipOvmf             # the codex-vm thirty-four (does not touch the record)
build/check-diag-verdicts.ps1                 # every state word has a verdict row (both scripts run it first)
build/boot/test-ovmf.ps1 -Img build/boot/diag.img -Out diag.png -UsbDisk -Seconds 100
```

Reading it, top to bottom:

| row | what it says |
|---|---|
| `12345678 ABCDEFGH abcdefgh` | the font renders and the stride is right; sheared or doubled glyphs mean the stride the firmware reported is not the panel's |
| eight bars | white red green blue cyan magenta yellow black, in that order; a channel swap shows blue where red should be |
| `diag <id> kernel <digest> world=EBS cfg=N src=stdin` | the image's id (must equal the stick's `DIAG.ID`), the compiler that built it, whether firmware is still alive (`UEFI`) or ExitBootServices ran (`EBS`), how many config lines and where from. `cfg=N` counts every non-empty line the ring carried, the `id` and `kernel` lines included, so a default image says `cfg=2` |
| `box: <manufacturer> <product> fb=WxH stride=S ram=<MB>` | from SMBIOS type 1 and the sum of type 17; `unnamed` and `unread` when the firmware published no table |
| `smbios <state> ...` | `ok`, `no-table` (nothing in the ConfigurationTable), `bad-anchor`, `unmapped` (the table sits outside our identity map, so it is not read), `no-system` (no type 1). Rows: name/version/serial/family, BIOS/board/CPU/RAM/structure count; every type 0/1/2/3/4/16/17/19 structure banked as `type/handle/len/strings` |
| `edid <state> ...` | `ok`, `absent` (firmware offered none: dim, not a fault), `short`, `bad-header`, `bad-checksum`. Manufacturer, product code, monitor name, native timing, size, version, serial; the 128 bytes banked as hex |
| `cpu <state> ...` | `ok`, `hypervisor` (leaf 1 ecx bit 31: a VM), `vmx-locked-off` (Intel with VT-x switched off in firmware setup, the one CPU state a stranger can fix), `no-brand`. Vendor, brand, family/model/stepping, logical count, VMX and SVM, a flag list; the raw leaves banked |
| `pci <state> devices=N nic=.. / storage=.. usb=..` (row 4 of the stages) | the worst per-device `MAP=` verdict (the table under PciProbe below) among the parts a driver would bind, as a stage word: `ok`, `unassigned` (a `MAP=none`), `ABOVE4G`, `BELOW3G` (the dangerous one, red), `empty` (config space answered nothing: a probe fault). The full device list is `+more in bank` |
| `scene <state> ...` and the picture at the right | `rendered` (the render wrote inside its frame and the frame stood), `blank`, `spilled`, `no-fb`, `no-room`, read back off the framebuffer: `centre=` is the pixel at the middle of the render (not the band colour when something was drawn), `frame=a/b` two pixels of the surrounding band (both must still be the band, 8405024). Cube blue, pyramid red; swapped means the firmware is RGB and nothing reads `PixelFormat` |
| `gopmode <state> max=N before=M chose=K flags=F status=HHHHHHHH` (row 6, DiagGop.codex) | what the UEFI stub's mode selection actually did, read out of the v3 handoff block because the guest cannot observe it any other way: the selection runs before ExitBootServices and this row runs after, so QueryMode is gone by the time anything could ask. `honoured` (SetMode moved the firmware to the largest mode it enumerated), `kept` (the largest was already current, nothing asked), `single` (one mode offered, nothing to choose), `refused` (SetMode said no, so the picture is the firmware's own console mode and not our choice), and two instrument states, `noloop` (the bank contradicts itself) and `nostub` (an image built by a stub older than the bank). **Geometry alone cannot separate these** -- 1024x768 reads identically whether it was the largest mode or a refusal -- which is why the stub banks its selection and this row reports the bank rather than inferring from what is on the glass. Arms: every bed arm asserts `honoured` (codex-vm `max=3 chose=2`, OVMF `max=30 chose=27`), and `gop-kept` (`-gop-width 1600 -gop-height 900`) is the falsifier -- the firmware already boots in its largest mode, so `max=4 before=3 chose=3 flags=1` and nothing is called. `refused` cannot be produced in either bed and is the state the metal sitting exists to find |
| `block <state> via=USB bps=512 lba=30000 write=1 readback=1` (row 7, the first write-side stage, runs AFTER the bank on the medium the bank opened; DiagBlock.codex, the block ladder lifted from `apps/works/BlockLadderProbe.codex`) | reads the ESP boot sector back through our driver (`read-fail`: no 0x55AA), checks bytes-per-sector (`bpb-bad`), writes one marked sector at the scratch LBA inside the facts region (`write-refused`), reads it back (`readback-fail`, `mark-lost`), `ok`. `no-medium` when there is no bank. `DIAG.CFG` `block lba=N` moves the LBA; `block-oob` in `diag-arm.ps1` aims it past the medium and reads `write-refused`, every other stage unchanged: the stage's forced-failure arm |
| `xhci <state> ctls=N` plus one `ctl<i>` row each (row 8, DiagXhci.codex) | the USB controller census, read from cells the ORDINARY bring-up already wrote (`GopUsb.codex:54`), so it touches no device and issues no transfer. `running`, `no-disk`, `bringup-failed`, `none`. **`none` is deliberately ambiguous and the verdict says so out loud**: a box with no xHCI controller and a run where the bring-up never happened write the same zero, and these cells cannot tell them apart. Carries the HID endpoint's asked-against-programmed interval pair, which is the keyboard question stated numerically -- in the bed `asked int=10 maxpkt=8 programmed int=6 maxpkt=8`, the correct xHCI translation, which is what makes it a usable control |
| `sink <state> size=2745998 read=2745998 bad=0 shift=0 wstage=20` (row 8, DiagSink.codex, the sink ladder lifted from `apps/works/SinkLadderProbe.codex`) | the 2.7 MB streamed write (WORKS-9's question) through the bank's own FAT16 writer onto the bank's medium as `SINK.CDX`, then read back whole and compared byte for byte: `write-refused` (with `wstage=` the writer's stage cell), `size-bad`, `read-fail`, `bad-bytes`, `ok`; `mount-fail` when the volume will not mount a second time, `no-medium` with no bank. `DIAG.CFG` `sink shift=1` compares against a pattern shifted by one so every byte reads bad: `sink-shift` in `diag-arm.ps1` is that arm, the oracle proving it can say no (L-FALSIF) |
| `nicsit <state> part 0:3.0 verdict=ok mmio=...` and `poll 1000000 empty=12903us tick100k=1290us hpet-hz=...` (row 9, DiagNicSit.codex, NIC-1/NIC-2 of NicSittingProbe) | pure reads: the eligible Intel part, its BAR verdict, STATUS/CTRL/RCTL/TCTL and the RX ring registers as firmware left them (banked), and one million empty polls of a ring we own, the number NetIO spends its retransmit bounds in (bed 13034 us). `no-part` (dim; codex-vm has no Intel card unless `-e1000`), `rejected`, `bar-bad`, `no-hpet`, `ok` |
| `nicinit <state> part ... mac=y link=1 rdh=0 rdt=15` and per-step `s2 await-reset ret=1 us=22; ...` (row 10, DiagNicInit.codex, NicInitProbe) | e1000-init's own sequence step by step, a serial line `nicinit entering sN ...` BEFORE each step (serial-only by design: the last one on the wire names the step that hung, L-STATES; `diag-arm.ps1` skips them when comparing serial to file), each step's return and HPET duration banked; the link wait is `na-link-wait`'s 2 s budget so a no-link box still banks. `no-link`, `no-mac`, `no-hpet`, `no-part`, `rejected`, `ok`. Arms: `nic-nolink` (`-e1000-no-link` -> no-link, s10 = 2000068 us), `nic-nomac` (`-e1000-no-mac` -> no-mac) |
| `nicring <state> init=...us present=y mac=y received=1 ddset=0 sent=1 txdd=1 rdh-writable=y rdh=1` and the `after-listen` / `after-send` DD maps (row 11, DiagNicRing.codex, NicRingProbe) | NIC-4's question: a full `e1000-init` (timed), 1.2 s listening, the DD bit of every RX descriptor, one ARP for the gateway and 1.2 s more, the reader's own control (the TX descriptor the send completed on must show DD, else `reader-broken` and every RX bit is void), then the one write: RDH set to 7 and read back. `frames`, `quiet`, `reader-broken`, `send-refused`, `no-part`, `rejected`. `nic-pass` (`-e1000 -e1000-nat`) reads frames because the NAT answers the ARP; without NAT the bed is `quiet`. The stage's FIRST glass line is the answer row, `m=y rdh=1 wb=2 buf=y pre=0 dd=1 tx=1 d0=<32 hex>`, and it is first because the QR summary is built from each stage's first glass line only: it reaches you when the bank does not. `wb` counts nonzero bytes in descriptor 0's WRITEBACK half (8..15) alone, because `e1000-build-rx-descs` writes the buffer address into 0..7 itself, so a raw sixteen-byte test reads nonzero on every flight and answers "the part wrote our ring" unconditionally; `buf` is that half's control. `pre` is GPRC read BEFORE the attach (the counter clears on read, so the late read in the `stats` row counts only this stage's own window; sitting 4's single `gprc=1` could have belonged to nicinit's ring two stages earlier). `ln=0` appears only when the listen knob is off. `nicring listen=0` in DIAG.CFG sends the ARP and idles 1.2 s without polling: nothing recycles the descriptor, so the writeback survives to be read. That is `nic-noread`, and it is the only arm that shows the dump can read a writeback at all -- `nic-pass` cannot, because `e1000-poll-raw` recycles the descriptor it took the frame from and leaves `wb=0` behind |
| `b3 <state> card=e1000 mac=... poll-interval=N spend=N cap=N clamped=n clk=y dt=N moved=N/100000 hpet=N steps=reset,rings-link,k1,calibrate k1=N addr=static ...` (row 13, DiagB3.codex) | Track B's TCP conversation, the one stage that asks whether the STACK works on the part. Since 2026-08-21 it BANKS as it goes, not only at the end: every `b3 -> X` it paints is also written to `DIAG.TXT` as `stage=b3 step=X` on top of the lines banked so far, so a wedge leaves the step it died in on the medium as well as on the glass (sitting 9 died inside bring-up and the file said only that b3 was absent). Bring-up is stepped through the driver's own functions in the driver's own order, `reset`, `rings-link` (`e1000-init-after-reset`: quiesce, rings, the semaphore and `e1000-link-up`), `k1` (`e1000-pch-prepare`, whose readback is banked as `stage=b3 k1=N` the moment it returns; N is its code, 0..7 since main 18736: 3 owned and the disable bit stuck, 2 owned and not, 6/7 MDIO refused with/without ownership), `calibrate`; the driver half that would let the semaphore and link-up be separate steps is blu's. First, a CLOCK CONTROL independent of the part (L-CHANNEL): the HPET counter is read across 100000 reads and `clk=y/n dt=N moved=N` is banked at once; `clock-stuck` refuses before bring-up when the counter did not move, because a rate nothing validates over a stuck counter makes every clocked wait in the driver effectively endless. States add `clock-stuck`; arm `b3-clockstuck` (`-hpet-frozen`, nic stages off by cfg). **A step paints its own refusal** (since 2026-08-22, sitting 11's shape): when a step's note is refused with a bank open, the slot reads `BANK LOST AT <step>` in the bad colour and serial says `b3 bank lost at <step>`, so the glass names where the medium DIED rather than the stage whose whole-stage write the ladder noticed. The slot paint is overwritten by b3's own row when the stage returns, so that row is also stamped `bank-lost-note=N`, the ordinal of the first refused note counted as the serial trail counts them, and painted red whatever the state; the stick's trail ends at note N-1, and a gap between the two is a medium that accepted a write and lost it. Arm `b3-banklost` (`-usb-bot-die-len 5632 -usb-bot-die-lba 3400`, the first eleven-sector bank write, b3's seventh note). **`rings-link` is six banked steps since 2026-08-22**, the parts of `e1000-init-after-reset` in the driver's order: `rings-quiesce`, `setup-rx`, `setup-tx`, `swflag` (the acquire; `sem=` on the next note), `link-up` (CTRL|SLU; `link=` on the next note), `swflag-release`, so a trail that ends inside this function names the part that killed the medium. |
| `pchk1 <state> k1-after 770.17=d104 giga-k1-dis=n k1-en=y` (DiagPchK1.codex) | the same PHY register `pch` reads at stage 10, read again AFTER the driver has written it, because only the second reading is about the fix. The K1 write lives in `e1000-init`, which this ladder reaches only through `net-driver-bring-up` in `b3`, so before this stage existed nothing read 770.17 after the write and a flight where traffic still did not flow could not separate a fix that was applied and did not help from a fix that was never applied. `taken` (the disable bit is set: the driver's write landed), `not-taken` (MDIO answered and the bit is CLEAR, so the K1 theory is untested rather than disproved), `no-mdio` (the register did not answer, which says nothing either way), `not-pch` (not an I219, so there is no K1 step to read back), `no-part`, `rejected`, `bar-bad`. **It runs after the writer and before `asde`, and that is load-bearing**: `asde` calls `na-phy-kick`, which writes BMCR reset, and a PHY reset returns 770.17 to its NVM value, so a reading taken after `asde` would say the write never took on every run. Arms: `k1-taken` (`-i219`) and `k1-blocked` (`-i219-mng-holds`, firmware holds MNG so the semaphore cannot be acquired and MDIO is refused) move the row taken/no-mdio from one binary. Both name a peer, because `b3` short-circuits on no-peer before bring-up and then nothing writes K1 at all. **Since 2026-08-21 the stage also LISTENS after the write** (reek measured that no stage did, so the board could never show DD landing once K1 was disabled): 1.2 s on the production ring `b3` bound, through the driver's own receive path, GPRC read before (it clears on read, fencing the window) and after, and the DD count on the ring, as the row `listen-after-k1 word=W frames=N gprc-before=N gprc-after=N dd=N polls=N ms=1200`. `W` is nicring's vocabulary: `quiet`, `arrived-visible` (the poll returned a frame), `arrived-invisible` (the MAC counted one and nothing shows it: the stall), `skipped` (no peer, so `b3` never bound the driver). Both K1 arms carry `nicring off` and one armed frame released by the guest's first GPRC read, which is then pchk1's own: `k1-taken` reads `arrived-visible`, `k1-blocked` reads `arrived-invisible`, K1 the only difference |
| `SUMMARY run=N skip=N bank=ok medium=usb bytes=N` or `bank=none <why>` (on the glass; in the file the same is two lines, `bank=... cfg-file=N` after the stages and `summary run=N skip=N` after that, without a byte count since the file cannot know its own size) | the bank verdict names the medium it wrote through, or why it did not: `no bank, mount stage N`, `no DIAG.ID on any ESP, refused`, `DIAG.ID mismatch, refused`, `write refused, write stage N`, `no id in the image` |
| `todo: ...` | the first verdict row that applies; the bank has all of them |
| the QR block | the summary body, chunked at 100 bytes, scale 6/5/4/3 chosen to fit and never 2; `CUT` in the label means the body was truncated to what the panel could hold and the bank is the record |
| the green square at the band's right edge | the heartbeat: it toggles while the machine is alive |

`DIAG.TXT` and the serial transcript carry identical lines from `DIAG1 ...`
to `END`; `diag-arm.ps1` requires that row for row. A `DIAG.CFG` line is
`<stage> on|off|<param>`; the ring holds 120 bytes so the id and kernel
lines leave ~78 for stage lines, and the ESP file (up to 4 KB) is read
after the bank opens. A stage chapter is `Diag*.codex` under this directory
(the stale check in `diag-arm.ps1` watches that glob), exports one
`<tag>-run : DiagCtx -> DiagResult`, and is registered by number in
`Diag.codex`'s stage tables carry the shape.

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
4 GB as devices, and nothing above 4 GB. The BAR judged is the FIRST
MEMORY BAR of the six (`pp-map-judge`, a pure function over the six raw
dwords): a zero BAR is unimplemented and an I/O-space BAR (bit 0 set) is
a port number, so both are skipped; a 64-bit memory BAR takes its high
half from the next dword. That reaches BAR5 on an AHCI controller (BAR0
through BAR4 read zero, or are the vestigial IDE I/O BARs) and BAR2 on
a Realtek RTL8168, whose BAR0 is I/O.
`codex/test/diag-pci-map-judge` is the forced arm: the RTL8168 shape
answers `ok`, an I/O-only device `none`, and a genuine memory BAR at
`0x81060000` still answers `BELOW3G`.

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
`BELOW3G` and QEMU's xHCI comes back `ABOVE4G`. **`MAP=ok` is what codex-vm
answers** (its xHCI BAR0 at 0xFE800000 is inside the window; the ladder's
pci stage reads it there on every rehearsal since 2026-08-18, so the
paragraph that once said the verdict had never been observed is history).
OVMF cannot be made to produce it -- it pins its
32-bit PCI window at 0x81000000 at 2048 MB, at 3584 MB, and with
`-machine q35,max-ram-below-4g=3G` (accepted by QEMU without complaint,
and it moved nothing). The bed that does place BARs in the window is
**codex-vm**, whose three emulated devices carry BAR0 at 0xFD000000,
0xFE800000 and 0xFE000000, and the ladder reads them there over serial
and the bank (`diag-arm.ps1`); this one-question image still cannot be
screenshotted in codex-vm because it halts (see Build and run below).

The QR codes are the record and the screen is the convenience, so the
codes are sized BEFORE the screen is divided and the list takes what is
left. The scale is CHOSEN, not assumed: the largest of 6, 5, 4, 3 that
fits the space below the call-outs. Scale 2 is not offered, because at
scale 2 the decoder finds the finder patterns and reads none of the
codes, which is a failure that looks like success on the glass; scale 3
is flagged in yellow on the screen so the operator knows to shoot close
and square.

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
- the mass-storage bring-up ladder: which of six rungs the disk reached,
  with the Configure Endpoint code, block size and sector count

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

### The MSC rows: which of six ways the disk failed

`seen=` against `opened=` settles whether the stick's controller was even
looked at. The two `MSC:` lines settle the rest. The bring-up has six ways
to stop and they used to photograph identically, so a trip that ended in
`disk=n` bought one bit and returned the question it was booked to answer.
The rows carry **the furthest rung the bring-up ever reached**, green only
at 6.

```
MSC: rung=6 disk usable
     cfgv=1 cfgep=1 blocksize=512 sectors=32768
     dev on ctl0 port=0 speed=4 slot=1 route=#00000000
     SET-CONFIG completion: success
     dev PORTSC=#00021203 CCS=y PED=y PR=n spd=4
```

The fifth line is the device's OWN port register, read at the device. The
`root ports` block above prints **eight rows and this board has 26**, so on
a wide controller it cannot show the port that matters -- the ASUS stick is
on port 9. **`PED` is the first field to read against a transfer that failed
on the wire:** a port holding a device without being ENABLED explains a
transaction error that no amount of reading the driver will.

The `root ports` header also carries `connected=#...`, one bit per port up
to 32. `connected=4` is a COUNT and names none of them; the mask says which,
and for a port above the eighth it is the only place that port appears at
all.

The third line says WHERE the mass-storage device was, and it is the only
place on the screen that does. Everything above the ENUMERATED line is
restored from the snapshot taken when the KEYBOARD was credited, so on a
two-controller machine a disk failure has nothing naming the controller it
happened on. `ctl?` means the ordinal was never recorded. `route=#0` is
root-attached; non-zero is behind a hub, which is a different bring-up.

The fourth line is the SET_CONFIGURATION completion code, named rather
than numbered, and it is the line that matters at rung 2:

| completion | meaning |
|---|---|
| `STALL` | the device UNDERSTOOD the request and refused it. The request or the device's state is wrong, not the wire |
| `USB TRANSACTION ERROR` | the wire. Signalling, the hub or the port, not the request |
| `NO EVENT (fuel)` | nothing came back at all. **This is not a refusal** and must not be read as one -- the transfer never completed, so look at the ring, the doorbell or the slot, not at the device's opinion |
| `TRB ERROR` / `PARAMETER ERROR` | the controller rejected our TRB before the device saw it. Ours to fix |

**`retry:` appears on that line only when the first attempt was refused**, and
it is the answer to the question one attempt cannot settle: a transient
failure and a permanent one want opposite fixes. `SET-CONFIG completion: USB
TRANSACTION ERROR  retry: success` means a retry would have worked and is
worth writing; the same line ending `retry: USB TRANSACTION ERROR` means it
would not and nothing should be spent on one.

**The retry is asked and RECORDED, never acted on.** The first answer stays
the verdict. Adopting a retry because it might help would be a behaviour
change resting on a guess; one extra idempotent request turns the guess into
a measurement, and the evidence is then there to argue from. Both readings
are producible on the desk -- `codex-vm -usb-setcfg-fault 4` for permanent,
`-usb-setcfg-fault-once 4` for transient.

| rung | reached | what it means next |
|---|---|---|
| 0 | no mass-storage interface seen on any controller | the stick was never enumerated. Read the `ctlN` rows and the PORTSC block -- a bus or port problem, not a storage one |
| 1 | interface seen, no bulk pair | class 8/6/80 was found and no bulk IN+OUT beneath it. A descriptor problem, with the device in hand |
| 2 | bulk pair found, SET_CONFIGURATION refused | **read the `SET-CONFIG completion:` line, then `cfgv=`, then which controller.** `cfgv=` is the `bConfigurationValue` sent, written before the request so a refusal still shows what was refused; anything but 1 means the device numbered its configuration unconventionally. Reaching this rung PROVES ep0 control-IN works on that device, because its descriptor had to be read to get here, so a refusal here is never "ep0 is broken" |
| 3 | Configure Endpoint refused | `cfgep=` carries the controller's own completion code and IS the finding |
| 4 | endpoints up, never came ready | sixteen TEST UNIT READY / REQUEST SENSE rounds and the target never passed. The first rung where the medium itself is implicated |
| 5 | ready, capacity refused or not 512-byte | `blocksize=` splits it: 0 is a READ CAPACITY that failed, anything else is a medium this driver does not address |
| 6 | disk usable | `sectors=` is the medium's own count |

`cfgep=not issued` is a reading, not a zero completion code -- xHCI defines
none, so the cell says "never issued" without collision. `cfgep=no event`
means the command was posted and nothing came back.

**The rung is the furthest ever reached, not the last**, so on a
multi-controller machine a second controller with no stick on it cannot drag
the reading below what the first one proved.

All seven states were produced under OVMF before this shipped, each by
sabotaging the guard above it; the control arm is the unmodified driver
reading `rung=6 blocksize=512 sectors=32768` off a 16 MB `-UsbDisk`. A
ladder that has only ever printed its top rung is worth what no ladder is
worth.

**One thing this bed cannot show you, so do not read its silence as
agreement.** QEMU's `usb-storage` reports `bConfigurationValue` 1 and then
accepts ANY value sent to it. Measured 2026-08-03: passing byte 5 plus one
still came up `rung=6 disk usable` at `cfgv=2`. So no arm here can
reproduce a refused SET_CONFIGURATION *for the reason a real device would
refuse one*. A stall CAN be forced -- send an undefined `bRequest`, which
a conforming device must reject -- and that is how the completion-code
line above was calibrated.

**What the ASUS actually answered, 2026-08-03.** `rung=2` with `cfgv=1`:
the stick numbers its configuration 1, we sent 1, and it refused anyway.
The hardcoded `SET_CONFIGURATION(1)` that this rung first exposed was a
genuine defect and was NOT the cause of this refusal. Recorded so the next
reader does not re-buy that theory. The completion-code and location lines
exist because the reading that killed it could not say anything further.

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
inverse and covered by `build/qr-decode-test.ps1`.

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

## GeoTruth.codex

Prints the three published framebuffer numbers and paints nothing. It is
the only payload here that is not read by eye, and the only one with a
harness: `build/boot/test-conout-remode.ps1` boots it twice, with
codex-vm's `-uefi-conout-remode` off and on, and asserts that the geometry
the stub handed it tracks the LIVE console mode rather than the splash
mode. That is the ASUS display corruption of 2026-08-02, cured in
`build/cdx-to-pe.ps1` by clearing the screen before asking for the
geometry.

It paints nothing on purpose. The subject is the ORDER of two calls in a
PowerShell script, and a probe that also drew could fail for reasons that
have nothing to do with the question.

| Observation | Meaning |
|---|---|
| `GEOTRUTH w=1024 h=768 stride=1024` under `-uefi-conout-remode` | Correct. The stub asked after it cleared |
| `GEOTRUTH w=1920 h=1080 stride=2048` under `-uefi-conout-remode` | The corruption is back: the stub asked before it cleared, and every row the payload writes will span two scanlines |
| No `GEOTRUTH` line at all | The payload did not reach `print-line-uni`. Read stderr, not this table |

Run the harness rather than this payload by hand; a single arm proves
nothing, which is the whole reason there are two.

## Build and run

```powershell
# Build a bootable image (menu-only, no seed/font/source needed):
build/boot/build-option-a.ps1 -Src build/boot/diag/PciProbe.codex `
    -Out build/boot/pci-probe.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx

# PASS -Source '' TOO. It is not covered by -Seed '', and it defaults to
# build-output/Codex.codex, which ships a ~3 MB SOURCE.SRC no probe reads.

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

# These two carry the SEED (no -Seed '') since the probes gained F12
# screenshots: shot-take writes only to a medium whose ESP carries our
# own CODEX.CDX (the GopMedium lock), so a seedless probe stick paints
# "F12 shots OFF" and never writes anywhere. On a machine with internal
# drives that refusal is the point.
build/boot/build-option-a.ps1 -Src build/boot/diag/XhciTruthProbe.codex `
    -Out build/boot/xhci-probe.img -Font '' -Source '' -Kernel seed/Codex.cdx

build/boot/build-option-a.ps1 -Src build/boot/diag/MscAlignProbe.codex `
    -Out build/boot/msc-align.img -Font '' -Source '' -Kernel seed/Codex.cdx

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