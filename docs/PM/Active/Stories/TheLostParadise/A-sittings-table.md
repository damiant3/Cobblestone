# Appendix A. The sittings table

*Part of `docs/PM/Active/Stories/TheLostParadise.md`. Owner: blu. Every
flight of a Codex image on the ASUS **that `docs/Hardware/HardwareSitting.md`
records**, one row each, in flight order.*

**CORRECTED 2026-09-09: the scope sentence used to read "every flight" and the
table is not that.** The table is drawn from the flight record, therefore a
boot the flight record does not carry is absent from the table. Three are
known absent and are named here rather than left for a reader to discover:
the two A6 flights of images `F4D76CC4` and `AFDCE0EA` from 2026-08-06 onward,
on which F12 saved exactly one shot per boot
(`docs/PM/Active/Stories/TheShotThatWorkedOnce.md:27-31`), and the 2026-07-13
three-phase probe boot, which predates the record
(`TheSilentKeyboard.md:98-107`). The counts below are counts OF THIS TABLE and
are therefore counts of the flight record rather than of the campaign.

## What a row is, and what a row is not

A row is one BOOT of one image on the target machine. A row is not a
pre-flight card, a rehearsal, a bed run, or a plan. Four boots of one image
are four rows, because four boots produced four readings.

**The board, on every row below, is one machine.** SMBIOS names the board
`ASUSTeK COMPUTER INC SABERTOOTH Z170 MARK 1`, the processor
`Intel Core i7-6700K` at 4 cores, the memory 32,768 MB in 4 parts, and the
firmware `American Megatrends Inc 0901 08/31/2015`
(`docs/Hardware/HardwareSitting.md#31:2211-2212`, sitting 1's bank). The
board column is therefore omitted from the table and stated once here.

**Every hash in the table is a SHA-256 prefix as the record carries the
prefix.** A prefix is provenance against a stated tree state and against
nothing else (`HardwareSitting.md#31:6153-6161`).

**The column "who decided" names the agent or the person who ruled on what
happened next, where the record names one.** A blank in that column means
the record names nobody, and a blank is itself a finding of this part.

## The documented era, 2026-07-29 to 2026-09-09

The record begins at attempt 1 on 2026-07-29. Damian's charter states that
over 120 sittings preceded the record over about three months. The
reconstruction of the undocumented era is the second half of this appendix
and is NOT yet written; the section below states what remains to be read.

| # | date | image and hash | questions aboard | what returned | what the flight decided | who decided |
|---|---|---|---|---|---|---|
| 1 | 2026-07-29 | `pci-probe.img`, hash not recorded | attempt 1, the first boot of a Codex payload | the board did not boot the stick, and every failure path in `option_a_stub.asm` ended at `jmp fatal`, therefore ONE bit came home | first, the stub paints two liveness colours (main 12073); then, an artifact must be on the ladder before a flash. Account: `TheStickDidNotBoot.md` | red owns the sheet from 2026-07-29 |
| 2 | 2026-07-29 | `scene-probe.img` `5DC0C6C3` | rung 1: does the display path work, is the channel order right, what mode | PASS. 1920x1080, stride 2048, cube blue and pyramid red, GOP linear framebuffer painted after ExitBootServices | A1 answered: the stick boots. Stride exceeds width by 128 pixels on this panel, therefore row indexing by width shears | val's ask, flown unconditionally |
| 3 | 2026-07-29 | `inventory.img` `F2CFEE5C` | rung 2: the parts, and what our stack did with the parts | NIC `00:1f.6 8086:15b8` I219-V rev 31, `MAP=ok`, station address `78:24:af:d9:c8:23 AV=1`; NO PS/2, zero arrivals either side of the handback; HID `uk-ok=y slot=1 dci=3`; `disk=n`; 21 devices over four buses | Track B unblocked on an e1000e part needing no page-table change. USB HID after ExitBootServices is the only input path the machine has | fester built and gated; val and reek certified the readings |
| 4 | 2026-07-29 | `msc-align.img` `0D4C2431` | rung 3: the 64 KB crossing TRB | DROPPED, never flown | the gate condition was WRONG: `disk=n` was a false negative, because the stick sits on the SECOND xHCI (ASMedia `1b21:1242`) and `xhci-connect` took the first controller and stopped | red, recording the ladder's one defect at the rung |
| 5 | 2026-07-29 | `kbd-probe.img` `46CE613F` | rung 4: does USB HID deliver after ExitBootServices | `EPINT=0 SCANS=0`, `REPORT` all zeros across three phases with a key held. The same image under OVMF with keys injected returned `EPINT=12 SCANS=12 last=a0 FILE-WRITES=7` | the zero is real rather than a blind probe. The lead is SPEED: `speed=1` Full-speed on the board against `speed=3` High-speed in every bed | red |
| 6 | 2026-08-02 | `kbd-diag-v11.img` `F0BD5738` | the display defect, and the first single-driver xHCI measurement | screen legible, `GEO w=1024 h=768 stride=1024`; `HOST id=8086a12f run=y ports=26 slots=64 HCC=200077c1`, `CSZ=0`; `EPINT=0` with `EPCTX est=1 dq=<ring base>`; phase 2 `rel=y reclaim=y` and KEYS DELIVERED; `disk=n mount=n FILE-WRITES=0` | the display defect closes on metal (AMI re-modes on the first ConOut call). The firmware fallback is real on the board. Our MSC attach fails on the real ASMedia | red |
| 7 | 2026-08-03 | `kbd-diag-v12.img` `53D85234` | does the endpoint claim RUNNING with one driver | `EPCTX est=1` | configure accepted, doorbell rung, every state software can read correct, and the controller still never fetches a TRB | red |
| 8 | 2026-08-03 | `kbd-diag-v13.img` `6A6FB5CC` | did the scheduler ever walk our ring | `STOPX e=y cc=1 est=3 dq=<ring base>` | the controller's internal dequeue never left the ring base. `re=3` was read as a refused restart and the reading was WRONG: xHCI 4.8.3 makes the write-back mandatory only before a transfer event | red |
| 9 | 2026-08-03 | `kbd-diag-v14.img` `CDF7E707` | the spec-derived schedule instrument | `SCHEDX p=00000603 pls=0 mf=+857 f1=1a`: port in U0, MFINDEX ticking, FSE code 26 Stopped-TD-IN-PROGRESS. The probe FROZE at `p=2428` | the controller fetches the TRB and issues transactions, therefore the v13 verdict is dead. The freeze is the probe's own unbounded allocation from v1 onward, R-COST's exact red flag | red |
| 10 | 2026-08-03 | `kbd-diag-v15.img` `C12179E2` | does removing SET_IDLE(0) revive the pipe | `EPINT=97` climbing to 48 s; `DEVX f=1f cfg=1 p=0 i=125`; EPINT then FROZE at 97; `R2:` all zeros | the interrupt pipe delivers, and SET_IDLE(0) was the killer. SCHEDX killed the pipe the flight was built to prove. `R2:` zeros were recorded as no finding and were THE finding, corrected 2026-08-04 | red |
| 11 | 2026-08-04 | `gopdesk-a2b.img` `02FF3DD9` | the seven-pane desk on the panel | reached the first-boot ceremony and stopped, because the ceremony needs a keyboard | the desk question and the keyboard question were holding each other hostage, therefore `DeskBoot.codex` drops the ceremony | red |
| 12 | 2026-08-04 | `deskboot.img` `CAE755B1` | the keyboard FIX, four interfaces bound | the desktop opened on a keystroke through our own driver; top bar `k4 e0n0s0 |e1n0 |e0n0 |e397n88`; Monitor `mouse no` | THE KEYBOARD WORKS ON METAL. FOUR keyboard-shaped interfaces exist and the typed-on interface is the FOURTH bound, therefore first-match binding could never have worked | red, closing the keyboard campaign |
| 13 | 2026-08-05 | `deskboot.img` `23C4A936` | the mouse, F12 screenshots, the HID table | the table was photographed and named the bus (Logitech Unifying `046d:c52b`, one slot, kbd dci=3, mouse dci=5, raw dci=7); the keyboard typed then DIED mid-session; the mouse never moved | `xhci-wait-xfer` matched transfer events by SLOT alone, therefore sibling endpoints on one slot steal each other's completions | red, fix 13128 |
| 14 | 2026-08-05 | `deskboot.img` `ADA7CC4D` | the completion-steal fix | ALL GREEN. Damian at the glass: "it all works". Mouse, clicks, Shutdown, F12 shots on the stick with the taskbar verdict | A3 closed, the write path proven on real hardware, the camera stands down | Damian |
| 15 | 2026-08-05 18:35 | `xhci-probe.img` `AF3A6B45` | reek's A4, the xHCI truth | `MSC rung=6 disk usable`, `dev on ctl0 port=8 speed=3 slot=4`, `sectors=60506112`; `SET-CONFIG completion: USB TRANSACTION ERROR retry: success`; `ctl1 0000:0000 NEVER-OPENED` | the disk sits ABOVE ROOT PORT 7, an observation no bed can seat. The first SET_CONFIGURATION errors on the wire every boot and the retry gets past the error | reek |
| 16 | 2026-08-05 | `msc-align.img` `A1C0F205` | the 64 KB crossing, then the ASDE arm | the four storage rows painted, then `ASDE: eligible at 0:31.6 -- entering bring-up`, then the machine STALLED. F12 never armed, therefore the storage rows were lost | the first metal execution of `na-bring-up` wedges the real I219 | reek, routing to blu |
| 17 | 2026-08-05 19:10 | `msc-align.img` `4A2C05F5` | the same two questions, with a shot window ahead of the arm | `ALIGNED ok=y chk=e173b96d`, `CROSSING ok=y chk=e173b96d`, `data identical=y`, `LIVENESS lba=60506128 ok=n` | a 32 KB bulk TRB crossing a 64 KB boundary delivers byte-identical data on the real controller. The ASDE wedge is DETERMINISTIC across two boots | reek |
| 18 | 2026-08-05 | `ceremonyboot.img` `C423418D` | the first-boot ceremony and the unlock | GREEN. Damian: "it all worked." `IDENTITY.DAT` 124 bytes on the ESP, written by the guest on real ASMedia; the returning boot unlocked | the USB mass-storage WRITE path works on the real part. A2's ceremony campaign closes | Damian |
| 19 | 2026-08-09 | `worksflight.img` `9A5705B9` | WORKS-8, the FAT write path | shot 1 landed at 2,359,350 bytes and renders; shot 2 failed `s7 m3 c256 p2 w14`, a 32 KB data-phase transfer with no completion event inside `xhci-fuel` | WORKS-8 passes on metal. The shot-2 timeout is three defects deep, all three in code that had never once executed | blu measured and named no cause; reek fixed, main 14447 |
| 20 | 2026-08-09 | `a5flight.img` (2026-08-09 build) | A5, the compiler compiles on the box | WROTE NOTHING. The returned volume differs from the master in LBA 0 and LBA 1 alone | the payload's block I/O was raw IDE port access against a USB stick, therefore the payload could not have written | reek |
| 21 | 2026-08-10 | `a5flight2.img` (2026-08-10 build) | A5 with a `-Uefi` payload | WROTE NOTHING. Exactly two sectors differ. The screen held the stub's dark green | the arm's ONLY success signal was `DISK-OUT:` over ConOut, and `__uefi_print` had never rendered a character on the board, therefore a dead payload and a working payload were indistinguishable | reek |
| 22 | 2026-08-10 | `blockladder.img` `837F79FA` | the UEFI block write path, rung by rung | GREEN, and the green was the ladder's own fault: the build printed to ConOut BEFORE painting on every rung | a firmware call that does not return takes the colour channel with the call, therefore green was the only reachable answer for every failure at or before the first print | reek |
| 23 | 2026-08-10 | `blockladder.img` `FF5CC67F` | the same rungs, painting first | WHITE. LBA 30000 carries `A5 3C 90 43 4F 44 45 58` with the `55AA` signature intact | `LocateProtocol(EFI_BLOCK_IO)`, `ReadBlocks` and `WriteBlocks` all work on this board after the kernel installs its own CR3. The CR3-remap hypothesis is dead | reek |
| 24 | 2026-08-10 | `seed/Codex.img` `4564D27F` | the Welcome Back hang | the hang did NOT reproduce; the passphrase was accepted and the identity unlocked | recorded as an open intermittent rather than as fixed: the only payload change is a heartbeat in `gt-read-line`, and no mechanism repairs a keyboard | red |
| 25 | 2026-08-11 | `browserflight.img` `1A5F8B05` | the mouse, the click dispatch, the browser hit test | ALL GREEN. Damian: "the arrow cursor works, it clicks, those clicks work, the app works, the pages render" | WORKS-10 closes. No scripted input in the bed moves the desk cursor, therefore no green bed run is evidence about the pointer in either direction | Damian |
| 26 | 2026-08-11 | `asdeflight.img` `4145AA69` | B2 Finding 4, the ASDE bit | two rows painted, `eligible at 0:31.6` and the read-only touch (`mmio=0xDF400000 status=0x40080080 ctrl=0x180240`), then nothing. The bank did NOT write | the RESET wedges and ASDE is EXONERATED, because the run never reached either arm. Finding 4 is UNTESTED rather than disproved | blu |
| 27 | 2026-08-11 | `sinkladder.img` `34A6BC00` | the 2.7 MB streamed FAT write | ORANGE, held. The returned root directory holds `EFI`, `CODEX.CDX`, `CMUNSS.TTF` and NO `BIG.CDX` | no directory entry was ever created, therefore the fault is at or before the first allocation rather than in the chain walk. The glass could not separate slow from dead, therefore reek built the heartbeat bar | reek |
| 28 | 2026-08-11 | `a5bigflight.img` `9E6E35AC` | A5, the compiler reproducing itself | WROTE NOTHING and the ladder did not fire. The stub's dark green held for twenty minutes | the payload dies between the stub's jump and the first line of `opening`, upstream of the first rung. `-EntryStart` enters `__start`, which takes the hardware over with boot services still live, and `emit-wait-for-tick` never returns on this board | blu |
| 29 | 2026-08-13 | `asdeflight.img` `BB99E629` | Finding 4, with both arms ahead of the reset | every row painted. Touch `LU=0` with the cable in a live switch; both arms `LU=1 FD=1 SPEED=1000`; `RESET done: settled=1 ICR=260`. F12 answered `no esp s1 m3 c4 p1 w1964712320 f945044 l1 r1` | THE LINK COMES UP UNDER OUR CODE, and the read-only touch row is what proves the claim. The F12 failure is a USB transaction error in the CBW phase, therefore the standing eject-and-reinsert hypothesis is REFUTED | blu |
| 30 | 2026-08-13 | `asdeflight.img` `BB99E629`, boots 3 and 4 | does the ASDE write take, does a cleared SLU clear | `CTRLback=1573440 ASDEbit=n` on both arms; all four rows `CTRL=1573440 SLUbit=y STATUS=1074266243 LU=y` after clearing SLU | CTRL IS READ-ONLY ON THIS PART, therefore `e1000-reset` never sets RST and `e1000-await-reset` answers `settled=1` on its first read. The cold-versus-warm reset hypothesis is DEAD and the 2026-08-11 wedge is unexplained again | blu, correcting blu |
| 31 | 2026-08-13 | `vmxprobe.img` `8E95E062` | is VT-x available on the box | `IA32_FEATURE_CONTROL = 5`, `VT-x available`, `VMX revision id 4` | VT-x is switched on and locked, and the bed had answered 1 twice. An instrument can be reproducible and still be pointed at the wrong thing | blu |
| 32 | 2026-08-13 | the DIAG build (`disk-diag`) | four questions the earlier silent flights left | the stamp printed; `scope 0 net 0`; `ring w 32 r 16` against `w 16 r 16` in every bed; `DIAG path len` never printed | the board runs exactly what is flashed. A machine with no 16550 floats the LSR ports to 0xFF, therefore the drain appends one phantom byte per character read | blu |
| 33 | 2026-08-13 | `a5fix.img` `4FA6CB66` | A5 with the volume binding repaired | the flight card was written; the entry records the diagnosis rather than a boot | every MAGENTA stall was the WRONG VOLUME: `LocateProtocol` returns the first Block I/O handle and the UEFI spec leaves the order unspecified, therefore a foreign FAT volume mounted cleanly and held no `SOURCE.SRC` | reek, main 15041 on 2026-08-14 (reek 15039) |
| 34 | 2026-08-14 | `nicsitting.img` `E7128273` | NIC-1, NIC-2 and NIC-3 on one boot | `RCTL=0 EN=n`, `TCTL=0x3003F0F8 EN=n`, `RDBAL=0x5C805420 RDLEN=0`; `hpet-hz=23999999`, 1,000,000 empty polls = 32,606 us against 13,034 in the bed; then the NIC-3 banner and NOTHING | NIC-1: the part arrives with no live receiver at handoff. NIC-2: the calibration TRANSFERS at a factor of 2.50, and the 100000 poll fallback is 30.7x short on this metal. NIC-3: `e1000-init` was entered and did not return | blu |
| 35 | 2026-08-14 | `a5flight.img` `DBE8DC52` | A5, the whole of A5 | GREEN. `OUT.CDX` 2,790,018 bytes, byte-identical to the host control, plus `OUT.TXT` | THE COMPILER COMPILED ITSELF ON THE ASUS. The defect that held A5 for days: AMI satisfies AllocateAnyPages from the TOP of RAM, therefore the heap landed above 4 GB and bounded heap-pointer types trapped on the first `pitch` | reek |
| 36 | 2026-08-15 | `nicinit.img` `4C6F61DA` | which of the ten steps inside `e1000-init` hangs | every step returned. `s9 phy-bring-up ret=0 in 92892733us`, `INIT COMPLETE RDH=15 RDT=15` | `e1000-init` does NOT hang: the run costs 93 seconds and 92.9 of the seconds are `e1000-await-aneg` burning 1,000,000 fuel at 92.89 us per MDIO read. The cost was diagnosed on 2026-08-04 and routed AROUND rather than bounded, therefore the defect cost a second flight eleven days later | blu |
| 37 | 2026-08-16 | `nicring.img` `2D4414F4` | do frames actually move, and is the ring written back | three rows painted, then nothing for over ten minutes. `arrival RCTL=0 RDT=0 hpet=23999999`. The bank did not mount | the hang was the PREVIOUS flight's fix: bounding aneg to 3 s removed the dead time in which the link came up, therefore `e1000-await-link`'s bare four-million-read count ran against a settling link | blu, fix 15588 |
| 38 | 2026-08-18 | `diag.img` `C487773C` | the grouped ladder, five stages | the box named from SMBIOS, five stages, five QR codes, 4,350 bytes banked and read back. `pci BELOW3G` on `06:00.0 10ec:8168` | the stick works as a stranger's instrument. The one red row is the LADDER's defect: `pp-map-of` read an I/O-space BAR as a memory address | root, fixed the same day |
| 39 | 2026-08-19 | `diag.img` `601103D9`, sitting 2 | ten stages, the default ladder | `pci ok`; `sink write-refused wstage=14`; `DIAG.TXT` ends after `block`; `nicring` grey for more than ten minutes and pulled | `pci ok` on metal, therefore the mapping verdict is closed for this box. WORKS-9 reproduced with a number. The bank DIED with the sink. `nicring` is not bounded on this box | blu, correcting the mechanism at main 17357 |
| 40 | 2026-08-19 | `diag.img` `C46DE4DD`, sitting 3 | the same ladder with nicring bounded | the ladder ran to SUMMARY. `nicring quiet rdh=5 rdh-writable=y sent=1 txdd=1 received=0`; `sink wr=0 cc=256 lba=6081 rty=1 ph=3 after=0`; `SUMMARY bank=ok` against a `DIAG.TXT` of 4,577 bytes ending at `block` | nicring returned. The sink refusal is instrumented and the medium is wedged from the refusal onward. `bank=ok` is painted and the bank is NOT ok, therefore the bank's truth test must be the file's byte count | reek, answering the same day |
| 41 | 2026-08-19 | `diag.img` `ED90B46A`, sitting 4 | the honest bank verdict, and the ring | `SUMMARY bank=lost at=sink size=4577`; `stats gprc=1 rnbo=0 mpc=0 crcerrs=0` with `received=0 ddset=0` | the bank verdict is honest now. THE FRAME ARRIVES AND IS INVISIBLE: the MAC counted one good frame and the frame reached no descriptor of ours. The sink wedge lands on the FIRST CHUNK both flights, therefore transfer size is a candidate | reek corrected the mid-run reading the same day |
| 42 | 2026-08-19 | `desk.img` `157852B7` at `-AllocPages 131072` | A8: does AMI grant the desk's 512 MB heap | the first-boot wizard came up; the refusal colour never appeared; the keyboard delivered nothing in the wizard | A8 ANSWERED: AMI grants the 512 MB `AllocateMaxAddress` below the aperture. A second flight the same day on `optiona.img` `1D557517` typed the whole ceremony and ran the desk and the mouse, therefore the silence is INTERMITTENT | fester built; Damian flew both |
| 43 | 2026-08-20 | `diag.img` `346B4000`, sitting 5 | fourteen stages, every instrument switched on | `xhci running ctls=2 binds=5 kbd=y mouse=y disk=y`; `gopmode honoured max=10 before=3 chose=0`; `nicinit s9 us=3000387`; `asde` painted `-> RESET, warm reset` and sat there | THE KEYBOARD BINDS, therefore the question moves from attach to report. ASDE wedged and the mechanism is named: `e1000-reset` never quiesces, therefore the reset pulses RST on a receiver running with descriptors in flight. TWO of the five questions were never asked, because red shipped the stick with no `DIAG.CFG` | blu named the mechanism; red owns the composition error |
| 44 | 2026-08-21 | `diag.img` `63EFDB8A`, sitting 6 | the rung ladder, b3, asde | `sink ladder-stop done=4 rung-sectors=16`; `nicring quiet pre=3 rdh-writable=y sent=1 txdd=1 received=0`; `b3 running` and nothing after; `asde` NEVER RAN | THE SINK THRESHOLD IS 16 SECTORS, a number where four sittings had returned one bit. `rdh-writable=y`, therefore the 2026-08-15 RDH movement was real consumption. `b3` has no fuel cap, therefore a stage that hangs forever costs every stage behind the stage | blu corrected the eliminated-invisible claim the same day |
| 45 | 2026-08-21 | `diag.img` `C5744A6D`, sitting 7 | nine I219 readings at stage 10 | `DIAG.TXT` holds stages 1 to 8 and STOPS. The nine readings were never obtained | the composition error is red's: an absent cfg ENABLES every stage rather than disabling any, therefore the sink ran at stage 9 and took the bank. Three instruments gave three different answers and only the stage rows were honest | red, and root fixed the off-by-one glass verdict within the hour |
| 46 | 2026-08-21 | `diag.img` `1F46A225`, sitting 8 | the campaign's K1 question | `pch 770.17 = d104`, `giga-k1-dis = n`, `k1-en = y`, and nine `pch` readings banked. `asde` wedged for the third consecutive sitting | K1 IS ENABLED ON THIS BOARD, therefore blu's K1 layer is needed. The bed powers `770.17` up at `0x4000` and the board reads `0xd104`, therefore a constant write would have cleared firmware state no model holds | blu; red refused the `kmrn` overclaim |
| 47 | 2026-08-21 | `diag.img` `ECC60AF4`, sitting 9 | the first flight with the K1 write and the SWFLAG semaphore on | `DIAG.TXT` whole through `nicring`, then `END`. `extcnf 0x00F00=002c0089` with `swflag=n`; `nicring gprc-before=2`, then `gp=1 ddset=0`, `aim rdba=ours match=y` | FIRMWARE HOLDS THE MDIO OWNERSHIP, therefore `e1000-swflag-acquire` cannot succeed and the K1 write goes out with ownership refused. THE RING SUCCESSOR IS ANSWERED: the frame arrives in the window, on our ring, and is not written back | blu; red measured the bed's own GPRC defect the same afternoon |
| 48 | 2026-08-21 | `diag.img` `C6B1CEAC`, sitting 10 | which line of the bring-up hangs | the glass painted `b3 -> clock`, then `b3 -> reset`, and stopped. The medium carries `stage=b3 step=reset` | THE HANG IS INSIDE `e1000-reset`, before the semaphore and before the K1 write, therefore all three of sitting 9's suspects are exonerated. `nicinit` ran the identical seven operations two stages earlier in under 11 ms, therefore the cause is the part's STATE rather than the code | blu, taking the discriminator |
| 49 | 2026-08-21 | `diag.img` `2C7030D7`, sitting 11 | the seven split reset operations, and b3 | all seven operations ran and the sitting-10 hang did NOT reproduce. At 17:01:11 the dev box logged `CONNECTION 28 from 192.168.6.200:49157`, 13 bytes echoed and closed clean. The bank's last line is `step=rings-link` | THE ASUS TALKED TO THE DEV BOX over the real I219. The hang is state-dependent or intermittent. The medium died inside `e1000-init-after-reset` rather than at `pchk1` | root took the `banked=n` paint; reek took WORKS-9's moved mechanism; blu kept the hang open |
| 50 | 2026-08-24 | `diag.img` `8CDF3617`, sitting 12 | which of swflag or the `CTRL|SLU` write kills the medium | `b3 ok pe=192.168.6.141:7 sent=13/13 rx=13 lk=1` with the peer log agreeing; `pchk1 taken 770.17=f104 giga-k1-dis=y`; `bank-lost-note=4`; asde reached `s2` and stopped | NEITHER CANDIDATE CAN: the medium's b3 trail ends at the third note, therefore both candidates are downstream of a medium already gone. THE K1 WRITE TOOK. The death MOVED between sittings 11 and 12, therefore the framing that asks which single line kills the medium is wrong | blu mastered; red on a build |
| 51 | 2026-09-07 | `diag.img` `AC7399ED`, the default cfg | does the medium survive a b3 that dials nothing | `bank=ok medium=usb cfg-file=9` with pch, nicsit, nicinit, nicring, b3 and pchk1 all banked. NO stage 16 row. The glass stops at `asde running -> RESET s2` | THE HEADLINE PREDICTION HELD, therefore the medium survives b3 when b3 returns at its first branch. ASDE WEDGED WITH THE MEDIUM ALIVE, which separates asde's silence from a dead bank for the first time. The card was wrong three times | blu composed and flashed; Damian flew |
| 52 | 2026-09-07 | `diag.img` `CB1AE335`, sitting 13 | the same cfg with b3 dialling | the medium died at `kbd`, STAGE 9, and `kbd` RAN with `kbd off` in the cfg the payload read. b3 green, recorded ONLY in the peer log. The glass said stage 17 and the medium ends at stage 9 | WORKS-62: `GopUsbMsc` issues six SCSI opcodes and SYNCHRONIZE CACHE is not one, therefore a write-back cache answers the readback with what was never committed. blu's controlled pair on b3 was NOT single-variable and the claim was withdrawn | blu, retracting blu |
| 53 | 2026-09-07 | `diag.img` `AFC6AD65`, sitting 14 | the restore fix and the CRLF fix | the ladder ran to `asde` with NO bank-lost paint at any stage. The medium kept the two FAT copies and nine data sectors from the FIRST write, without the directory entry | seventeen acknowledged writes against a medium that kept sectors from only the first is the write-back-cache reading with nothing beside the reading. No echo peer was listening, therefore b3 has no register on this flight | root; the flush image is blu's next arm |
| 54 | 2026-09-09 | `diag-sitting15.img` `47F29D50` | THE LAST SITTING: asde, NIC-4, NIC-6, WORKS-24, the WORKS-62 flush, the record channel | rows 1-7 painted; `nicsit` ended on `poll 1000000 empty=33152us tick100k=3315us hpet-hz=23999999`; the record channel never printed; the bank never opened; `nicinit` and everything after never reached | the box stopped inside one of three steps that print nothing before running: `usb-attach`, the ESP select and cfg read, or `net-driver-bring-up` in `drec-open`. EVERY question aboard received one metal answer: not reached | Damian ruled the queue closed |

## What the table says about the corpus

The table holds 54 rows across 43 days, from 2026-07-29 to 2026-09-09. Two
rows are not boots: row 4 was composed and DROPPED before flight, and row
33 records a diagnosis rather than a boot. **52 boots remain.** Every count
below is computed from the table above rather than carried forward
(L-COUNT, computed 2026-09-09), and the four classes partition the 54 rows
with no row in two classes:

| outcome | count | rows |
|---|---|---|
| the flight answered every question aboard | 17 | 2, 3, 5, 12, 14, 15, 17, 18, 23, 25, 29, 30, 31, 32, 35, 36, 42 |
| the flight answered part of what was aboard | 26 | 6, 7, 8, 9, 10, 13, 16, 19, 24, 26, 27, 34, 38, 39, 40, 41, 43, 44, 46, 47, 48, 49, 50, 51, 52, 53 |
| the flight answered nothing the flight was built to ask | 9 | 1, 11, 20, 21, 22, 28, 37, 45, 54 |
| never flown, or not a boot | 2 | 4, 33 |

17 plus 26 plus 9 plus 2 is 54, which is the table's own row count. The
first draft of this section published 12, 22, 12 and 8, which sums to 46
against 54 rows and left eight rows in no class at all. The error was the
author's arithmetic rather than the table's, was caught by adding the
column, and is recorded here because a count nobody adds up is the shape
[[check-your-own-instrument]] names.

**Eleven of the 54 rows were lost to our own instrument rather than to the
board**, and the eleven cut across three of the four classes above:

| row | what our instrument did |
|---|---|
| 1 | every failure path in the stub ended at `jmp fatal`, and our own flash procedure had destroyed the GPT |
| 4 | the rung was gated on `disk=n`, a field the probe could not distinguish from "the probe did not look there" |
| 16 | the shot window sat DOWNSTREAM of an arm that wedges, therefore the storage rows had no channel |
| 20 | the payload's block I/O was raw IDE port access against a USB stick |
| 21 | the arm's only success signal was a channel that had never rendered a character on the board |
| 22 | the ladder printed to ConOut BEFORE painting on every rung |
| 26 | the reset ran AHEAD of the two arms the image existed to compare |
| 28 | `-EntryStart` took the hardware over with boot services still live |
| 37 | the hang was the previous flight's own fix, unbounded where the removed wait had been the bound |
| 45 | an absent cfg ENABLES every stage, therefore the sink ran and took the bank |
| 54 | the record channel carries no serial line ahead of its three steps, therefore the glass cannot say which step stopped |

**Nine boots answered nothing at all, and EIGHT of the nine appear in the
instrument table above**: rows 1, 20, 21, 22, 28, 37, 45 and 54. The one
exception is row 11, where the payload reached the first-boot ceremony and
stopped because the ceremony requires a keyboard the board had not yet been
shown to deliver, which is a dependency between two open questions rather
than a defect in the image. Part 03 carries the account of each row; part
04 carries the I219 thread; part 06 owns the ladder and the images as an
instrument.

## The undocumented era: NOT YET RECONSTRUCTED

Damian's charter states that over 120 sittings took place over about three
months before the record began. `HardwareSitting.md` carries no entry
earlier than 2026-07-29, and the file's first flight entry is attempt 1
(`HardwareSitting.md#31:5659-5665`).

**This section is a stated gap rather than a finding.** The sources the
charter names for the reconstruction are `docs/PM/Done/GitHubUpdates` (53
files), `docs/PM/Done/Hardware`, `docs/Designs/Done/Hardware`,
`Done/Tools/HardwareBringUpPlaybook.md`, the transcripts through
`build/transcript-extract.ps1`, and CL descriptions. None of the six is
read at the time this section is written, therefore no count, no date range
and no error bar is stated here. A number written before the reading would
be an estimate presented as a measurement, which is the failure
[[L-AMORTISED]] records at one level down.

## Coverage: what this appendix read, in full

| path | size | revision |
|---|---|---|
| `docs/Hardware/HardwareSitting.md` | 437,270 bytes, 6,904 lines | `#31` at main 25193 |

**What is NOT read, and why.** The undocumented era's six sources above,
because the reading is the next unit rather than a decision already taken.
`docs/Designs/Done/Hardware/*` and the eight stories the charter names for
part 03, because the stories carry the ACCOUNT rather than the ledger and
part 03 reads each story before the story's flight is analysed.
