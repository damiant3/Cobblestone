# The Hardware Sitting -- Run Sheet

## FLOWN 2026-08-05, ALL GREEN: `deskboot.img` (`ADA7CC4D9837B66097B89745EB7699445F9E8FCC8F5CEE6D04F074BEE0BFA004`). The completion-steal fix. Flight 3.

**Flight 3 verdict, Damian on the glass: "it all works." The mouse
works, clicks work (the Shutdown button clicks and powers off), F12
screenshots land on the stick with the taskbar verdict posted, and the
shot files appear in the Files app.** A3 is CLOSED, the write path is
proven on real hardware, and the camera stands down. Both flight-2
defects (keyboard death mid-session, mouse dead) are cured by the
(slot, DCI) latch below; fix red 13128, main 13133.

Flashed and read back byte-for-byte 2026-08-05, disk 2. Supersedes the
flown `23C4A936` build (below): same payload -- mouse walk, F12 shots,
table dwell -- plus the fix for what flight 2 exposed.

**What flight 2 taught** (table photographed, upside down and perfect):
port 1 is a Logitech Unifying receiver (`046d:c52b`, kbd dci=3, mouse
dci=5, raw DJ dci=7, ONE slot), port 7 the wired keyboard (`046d:c31c`),
`bound=5`. The desktop typed, then the keyboard DIED mid-session; the
mouse never moved. One defect: `xhci-wait-xfer` matched transfer events
by SLOT alone, so sibling endpoints armed on one slot steal each other's
completions -- the kbd/raw pumps poll the dongle's slot before the mouse
pump and ate its completions from frame one, and the wired keyboard lost
a 500 ms heartbeat to its own raw sibling minutes in and starved forever
(the thief generates no traffic of its own, so the slot goes silent).
Flight 1 survived only because the old walk never armed the siblings.

**The fix on this image:** the completion latch is keyed by
(slot, DCI) and every waiter passes its endpoint -- control, storage,
keyboard, mouse, camera. Proven both directions in a bed that could not
previously express starvation: new codex-vm `-hid-nak-unchanged` makes
interrupt endpoints NAK until they have news (the instant-complete
default made completions too plentiful for a scarcity defect to show);
under it `usb-hid-steal` answers `pos=0,0` on the old code and
`pos=80,40 btn=1` on this one. Rehearsed on THIS file, UEFI-booted with
a combo receiver and the image attached as its own disk: keyboard opened
Monitor, the USB mouse crossed the screen and CLICKED OPEN the Files
pane, TrueType loaded from the ESP.

Reading, beyond the standing rows (the table below still applies):

| Read | Verdict |
|---|---|
| Keyboard types AND KEEPS TYPING past a few minutes | **The steal fix holds on metal.** Flight 2's death was minutes in; longevity is this flight's keyboard reading |
| Cursor moves, click opens a pane | **The mouse works through our driver.** A3 closes on this photograph or its F12 shot |
| Keyboard dies again mid-session | Photograph the top bar `k`/`e`/`n` counters THE MOMENT it dies: whether `e` still climbs after keys stop decides event-theft vs endpoint death |
| Mouse still dead but keyboard lives | Photograph the HID table rows: if hid2 (`c52b` dci=5 mouse) is present, the dongle's boot-protocol mouse path is the suspect, not the walk |

## FLOWN 2026-08-05 (was READY TO FLASH 2026-08-04 NIGHT): `deskboot.img` (`23C4A936E6CC56ABE8591ACFCB690B86E5E9B472EB1F62AE398DFDDFF1E89744`). The mouse and the camera's replacement.

**Flight 2 verdict: the table was photographed and named the bus (see
the entry above); the keyboard typed then died mid-session, the mouse
never moved -- both explained by the completion-steal defect the entry
above fixes. F12 was never exercised (no BMP on the ESP afterward).**

Supersedes the flown `CAE755B1` build (below); same payload plus three
capabilities, all bed-proven on this exact file or its dev-loop twin:

1. **Every boot MOUSE is bound, per-interface** (`usb-hid-walk`): a
   combo device -- keyboard on one interface, mouse on the next, the
   dongle shape -- now contributes both. `usb-hid-combo` proves it
   (`same-dev=1 got=30 pos=60,40 btn=1`; against the old walk:
   `mouse: ok=0`), and a codex-vm desk capture shows the Files pane
   opened by a USB mouse click. The reading on the glass: Monitor's
   `input` row says `mouse yes`, and the cursor moves and clicks.
2. **F12 writes the screen to this stick as `SHhhmmss.BMP`** -- on the
   desktop and inside every GopDesk pane -- through a new multi-cluster
   FAT16 chain writer (bulk FAT flush, 64-sector data runs). Proven:
   `fat-write-big` (3 MB written and read back through the independent
   bulk reader, zero bad bytes) and a full desk arm whose extracted
   Monitor-pane BMP is pixel-exact. The verdict paints in the taskbar:
   `shot SHhhmmss.BMP ok` or a named failure. **This is the telemetry
   channel that retires the camera**: shoot, later pull the stick, and
   the frames are files on the ESP.
3. **The HID table survives a pre-loaded scancode** (drained mailbox +
   3 s dwell floor), so the table that flashed past unseen on the last
   boot holds still; rows now tag kbd / mouse / raw, so the four
   interfaces get NAMES (VID:PID) this boot.

Flash block and stick discipline: identical to the flown entry below
(same flasher line, `-Image build\boot\deskboot.img`, hash above,
PULL do not eject). Reading, beyond the standing keyboard rows:

| Read | Verdict |
|---|---|
| Cursor moves, click opens a pane | **The mouse works through our driver.** A3 closes on this photograph -- or on an F12 shot of it |
| Monitor `input` row `mouse no`, cursor dead | The typed-on mouse is none of the bound interfaces; photograph the HID table -- the mouse's `id=` row (bit `mouse`) or its absence names the next move |
| F12 -> taskbar `shot SH....BMP ok` | **The write path works on the real stick.** Frames come home as files; the camera stands down |
| F12 -> `shot write FAILED` | The chain writer failed on real hardware; the stick still boots (both FAT copies stay consistent); photograph the taskbar and note how many shots preceded |
| F12 -> `shot: no ESP mount` | The mount failed in the shot's own context; photograph |
| F12 -> nothing in the taskbar | The key never decoded: check whether the top bar `s` moved on the press; if `s` moves with no verdict, that is a dispatch defect, photograph |
| ~10 s pause after F12 | Normal: a 1024x768 frame is ~2.3 MB through 64-sector bulk writes. The desk resumes by itself |

Shots persist across boots; each is named by the RTC second. Read them
back on the dev box by mounting the stick's ESP (a plain FAT16
partition Windows can read); two shots within one second overwrite
each other, deliberately.

## FLOWN 2026-08-04 EVENING: `deskboot.img` (`CAE755B1...6B3A`). THE KEYBOARD WORKS ON METAL. THE CAMPAIGN IS CLOSED.

Flashed to disk 2 (` USB DISK 2.0`, 28.9 GB), full readback verified,
booted the ASUS first try. What the glass answered (Damian, at the board):

| Read | Verdict |
|---|---|
| Bars, then the desktop directly; the keyboard hold skipped on its first poll | A scancode was already pending -- the boot-menu Enter still releasing as our driver took the controller. **That keystroke, and every one after it, travelled our own USB driver end to end** |
| Files, Calendar, Issues and Monitor panes all open and answer keys | Decode, mailbox and every pane loop hold on metal. **A2's keyboard question is closed** |
| Top bar `k4 e0n0s0 \|e1n0 \|e0n0 \|e397n88` | **FOUR keyboard-shaped interfaces on this bus, and the typed-on one is the FOURTH bound**: e climbing at the ~500 ms idle cadence, n=88 key-bearing reports. The first three carry nothing. First-match binding could never have worked on this machine, whatever the transport under it did -- the diagnosis exactly |
| Monitor `input` row: `keyboard USB HID   mouse no` | The mouse is unbound/undelivered: A3, the named next campaign, now with a live desktop to instrument it from |
| The keyboard table was never seen | The pending scancode skipped the hold inside one frame and the desk painted over the table. The identities remain in diag cells 80-95; follow-up: drain the mailbox once before the hold or give the table a minimum dwell, so the table survives a pre-loaded key. Not worth a reflash today |

The interface identities (which VID:PID each of the four blocks is) were
not photographed and are the first thing the next boot of this same stick
brings home for free -- boot it once with no key touched and the table
holds for its full ten seconds.

## THE ENTRY BELOW IS THE FLIGHT THIS RECORD ANSWERS, kept for the procedure and the reading table.

## READY TO FLASH, 2026-08-04 EVENING: `deskboot.img` (`CAE755B1C2189A2CD7897FCD1FB07D875038CFF1CD7ADDFE3364616921EF6B3A`). The keyboard FIX, not another instrument.

**This supersedes both `DA2556FBCA0188F0` (below) and the unflashed
`D0F61D751F9680F9` (the n=/b= instrument boot that was to decide the
DMA-address fork -- decided by inspection instead): it carries their
readings AND the repair. It also supersedes the "NEXT BOOT:
kbd-diag-v16" section and the 2026-08-03 STATUS section's "USB HID
proven on the board" claim further down -- both are historical record
now, per the corrections in the v15 row.** Built
`build/boot/build-option-a.ps1 -Src apps/works/DeskBoot.codex -Kernel
seed/Codex.cdx -Ebs` against seed `52E0A3A00218E19F`; the file at
`build/boot/deskboot.img` hashes to the digest above and that exact file
passed the OVMF gate (boots real UEFI firmware to the full desktop,
TrueType from its own ESP) and a codex-vm `-uefi` boot.

### The diagnosis this flies on

Assembled from readings already home, no new boot spent: under factory
idle the bound interface delivers an all-zero report every 500 ms (v15
`EPINT=97` at 48 s, desktop `a`/`e` lockstep, `c=1`, `p=3/3`, `r=` zeros)
-- so DMA, ring and events all work and the DEVICE reports no keys. Its
own control pipe says the same (`R2:` zeros with `f=1f`, and `i=125`
proves nonzero control-IN data lands in the same arena, killing the
address-truncation candidate). Back in the SET_IDLE(0) era a keypress --
which report-on-change semantics oblige a report for -- produced zero
events in 90 s (v14). And the same physical keyboard delivers keys to the
firmware's boot-protocol poller (v11 phase 2). One conclusion survives:
**the interface `usb-attach` binds first is not the device the keys are
typed on.** The walk bound the FIRST boot-keyboard interface and stopped.

### The fix, and how it was proven without the board

`usb-attach` now binds every boot-keyboard interface on every controller
(hub-descended included) up to FOUR -- the primary plus three peers,
`kbd-peer-cap` -- plus the primary device's other HID interrupt-IN
interfaces as raw counting listeners inside the same cap; all pump into
the one scancode mailbox. A fifth interface is silently not bound; the
table's `bound=` count against the visible rows is what would say so. codex-vm gained the arm no bed could produce (`-hid-root-silent`:
first keyboard completes SUCCESS with eight zero bytes, second carries the
keys) and `-hid-keys` (keys reach the guest ONLY through USB HID -- the
first bed in this tree that can prove a scancode crossed the interrupt-IN
DMA path). `codex/test/apps/usb-kbd-silent` reproduces the board's exact
state and answers `got=30 primary-live=0 peer-live=Y peer-scans=Y`;
sabotaging the multi-bind back to first-wins answers `peers=0 got=0`.
The flash image itself, UEFI-booted under that arm, opened the Monitor
pane from a keystroke that had no route but the second keyboard's pipe.

### The boot

**PULL THE STICK OUT, DO NOT EJECT** (standing). Pre-flash, hash the file
and confirm the digest above, then:

```powershell
Get-Disk | Where-Object BusType -eq 'USB'      # find N -- check it twice
Start-Process pwsh -Verb RunAs -PassThru -ArgumentList '-NoProfile','-File',
  'D:\Projects\NewRepository-red\build\flash-usb.ps1','-Image','D:\Projects\NewRepository-red\build\boot\deskboot.img',
  '-DiskNumber','N','-SpecFit','-Force','-Log','D:\Projects\NewRepository-red\build-output\flash.log'
```

Sequence on the glass: bars + mode (held 10 s) -> trace lines -> **the
keyboard table** (one row per bound interface: `id=` VID:PID, port,
route, dci, boot/raw, speed, buffer) -> **keyboard hold, 10 s, ANY KEY
SKIPS** with a live per-interface counters row -> desktop.

**Operator instruction, and the failure rows are meaningless without
it: TAP A KEY as soon as the hold row appears. If the hold does not
skip, HOLD A LETTER KEY DOWN for the remainder of the window and
photograph the table with the live row.** "Hold expires untouched"
in the table below means no scancode ARRIVED, not that no key was
pressed -- the `n` counters can only name where keys arrive if keys
were being sent while they counted.

| Read | Verdict |
|---|---|
| **A tap ends the hold at once and the desktop appears** | **The keyboard works through our own USB driver.** `f 3 c l i d m` open panes; Esc returns; the campaign closes on a photograph of the desktop with a pane open |
| Hold expires untouched and typing does nothing on the desktop | Still dead. Photograph the table and the live row: any interface whose `n` moved while a key was held is where the keys arrive, and its `id=` names the device. `raw` rows count undecoded interfaces -- `n>0` there means the keys ride a report format we do not parse yet |
| `kbd interfaces bound=1` and its `n` stays 0, key held | Only one keyboard-shaped interface exists and it carries no keys: photograph its `id=` -- the device identity is the whole next move |
| `bound=` 2 or more, all `n=0`, key held | Keys reach none of the bound interfaces; the id rows say what WAS bound and the un-bound classes become the suspects |
| The table is ABSENT | The boot died before or during `usb-attach`; the last trace line standing names the stage. A missing table is a reading, not a formatting fault |
| The hold skips with no tap | A phantom scancode reached the mailbox; note it and continue -- the desktop is still typable for the per-pane checks |
| Top bar `k=n` on the desktop | No boot keyboard bound at all; photograph the `usb OK` trace line and the table |
| The hold ends but the desktop never paints | Death in `medium-select` or `desk-run`; the last trace line standing names the stage (`medium kind=` is trace row 9) |
| No bars at all: firmware screen unchanged / solid dark blue / solid dark green | Pre-USB liveness states; read them against the colour table in section 4 ("Read the screen COLOUR"), not this one |

The desktop's top bar keeps the live per-interface counters
(`k<N> e..n..s.. |e..n..`) repainting every RTC second; on any failure
it is the photograph that decides, exactly as the hold row is.

Photographs to bring home: the keyboard table + hold row (one frame
covers both), and the desktop -- with a pane open if typing works.

---

## THE BOARD IS RED'S, 2026-08-04. The stick last held reek's `msc-align.img`.

Damian's ruling after reek's two boots. **Reflash before assuming anything
about what is on the stick**: reek's A4a blocker is closed on metal (`rung=6
disk usable`, cured by EP0 recover-then-retry, and the first SET_CONFIGURATION
errors on the wire every boot with the recovery getting past it), and rung 3
is NOT answered for the reason recorded in section 1.

## READY TO FLASH, 2026-08-04: `deskboot.img` (DA2556FBCA0188F0). A2b without a keyboard.

**Built, not yet flashed.** `build/boot/build-option-a.ps1 -Src
apps/works/DeskBoot.codex -Kernel seed/Codex.cdx -Ebs`, depot seed
`37A7EF8E4EF603AE`.

### Why this replaces the flight below before it has flown twice

`gopdesk-a2b.img` reached the first-boot ceremony and stopped there, because
the ceremony needs a keyboard and this board's keyboard is the open question.
**A2b does not need a keyboard.** The mode, the aspect, the seven panes and the
Monitor readings are all visible on a desktop nobody has typed at, so one
unknown was holding the other hostage. `apps/works/DeskBoot.codex` (main 12991)
is spine, `usb-attach`, `medium-select`, `desk-run` and nothing else.

### The three defects the 2026-08-04 boot found, all fixed in this image

| Found on the glass | Fix |
|---|---|
| `pit=979` at `s=573` AND at `s=3978` -- the timer stops at ~54 s and never resumes, while IRQ1 keeps delivering and the RTC keeps counting | `desk-clock` and `desk-mon-loop` gated on that dead cell, so **the taskbar clock and the whole Monitor pane would have frozen on this board**. Both now gate on the RTC second (main 12989). The `ticks` figure is now READ rather than waited on, so a dead timer reports itself instead of freezing its own instrument |
| A 30 s countdown advancing in 11 s jumps, read as a hung machine | The wizard painted its vitals every 262144 SPINS, and this board runs 24,000 iterations a second. Now on the RTC second and on every keystroke (main 12990). Same mistake the same chapter had already corrected one screen away for `wz-noclock-spins` |
| The boot diag "flash on screen too quickly to line up the shot" | Held five RTC seconds, not `boot-spin 150000000` |

### What is NOT fixed, and is the reason to fly this

**The keyboard, both paths.** Pre-handback our USB HID delivered nothing;
post-handback the firmware fallback delivered nothing either, for 3978 seconds
-- proven by the elapsed counter, which any real key resets to zero. The second
half **contradicts the v11 flight**, which recorded `rel=y reclaim=y` and keys
delivered and is the basis for calling the fallback real on this board (R-3).
Two boots of one machine disagree, so R-3 is narrower than it reads. Not
chased here: this image is built so the panel answers do not wait on it.

### Reading it

No keys needed. Bars (red, green, blue, white) and the mode, held five
seconds -- **photograph this** -- then three trace lines, then the desktop.

| Read | Verdict |
|---|---|
| Desktop paints, seven live buttons over Shutdown | **A2b passes** |
| Font is proportional, not the blocky CBF | The stick's own `CMUNSS.TTF` was read back through our FAT16 |
| `usb OK ... disk=Y` on trace line 2 | Our MSC attach works on the real ASMedia, still an open defect from v11 |
| Taskbar clock advancing past a minute | The RTC gate holds where the PIT one would already be dead |
| Monitor pane `ticks` frozen at ~979 while the pane stays live | The PIT stall, now reported instead of fatal |
| `display` / `framebuffer` rows | The mode actually in use, which is the input to the aspect work |
| `sci` value | A real GSI here means codex-vm's `0x2000` is purely an emulator artifact |

---

## FLOWN 2026-08-04: `gopdesk-a2b.img` (02FF3DD9A4C07D89). Reached the ceremony, stopped there.

**Flashed and verified 2026-08-04**, disk 2 (` USB DISK 2.0`, 28.9 GB), by
`build/flash-usb.ps1 -SpecFit -Force`: all 16,777,216 bytes read back matching,
GPT refitted to this stick (backup header at LBA 60506111), all four patched
blobs verified. Transcript `build-output/flash.log`.

**Provenance.** `build/boot/build-option-a.ps1 -Src apps/works/GopBoot.codex
-Kernel seed/Codex.cdx -Ebs`, built against depot seed `37A7EF8E4EF603AE`.
Carries CODEX.CDX (2,712,066 B), SOURCE.SRC (2,993,576 B) and CMUNSS.TTF, so
`medium-select` can lock onto its own disk, the Files pane has source to draw,
and the desk speaks TrueType rather than CBF. **`-Ebs` is deliberate**: GopBoot
brings up its own USB stack through `usb-attach`, so this is a driver-truth
boot and the firmware's xHCI driver must be gone. It is not a ConIn payload.

**What flies for the first time:** the seven-pane GopDesk with no dead controls
(main 12905), and the ACPI RSDP memory search (main 12905), which is what lets
the Monitor pane read tables on a payload the stub did not hand a pointer to.

### Pre-flight, stated including the part that did not pass

| Gate | Result |
|---|---|
| Image boots under OVMF post-EBS, exact file | **PASS.** Wizard paints legibly at 1280x800, `pit` and `rtc` both climbing |
| Driven to the desktop under OVMF | **NOT ACHIEVED.** 11 keys over 100 s, `sc=0` throughout, the wizard never left its Welcome screen and its 30 s window expired to `Handed back to firmware` |

**The second row is honest and it is not a reason the stick was held.** What it
establishes is that no scancode reached the payload from QEMU's USB HID
keyboard in this bed; it does NOT establish that the payload cannot read a
keyboard, and the two are different claims (L-OPTIONAL, and the inverse in the
`cured-defect` case: a bed can be too quiet as well as too capable). Checked
rather than assumed: `apps/works/GopUsbKbd.codex:102-106` still carries the
metal-proven cure, the SET_IDLE that is deliberately NOT sent, and the vitals
line showed the loop alive (`spin` climbing) with both clocks running, so the
silence is on the delivery path and not in a hung guest. **This board already
answered the USB HID question on metal on 2026-08-03 (main 12627).** The boot
below is what re-answers it for this payload.

### The boot

**PULL THE STICK OUT. DO NOT EJECT IT.** Windows rewrites the partition table
on re-enumeration and destroys the GPT the flasher just verified. Boot UEFI
with CSM/Legacy off.

1. **Boot diag.** Four bars top to bottom: red, green, blue, white, then
   `w= h= stride=`. Banded smear instead of clean bars is a stride or mode
   mismatch; wrong bar ORDER is a BGR/RGB swap. Then green trace lines
   top-left of the black region, one per stage. **On a machine that hangs, the
   last line standing names the stage that died** -- photograph it.
2. **Wizard.** `Press Enter to begin.` The vitals line under it is the
   instrument: `spin` climbing means the loop is alive, and **`sc=` is the
   keyboard answer.** Any key, not just Enter, restarts the window and proves
   delivery. If `sc` stays 0 for 30 s the payload hands the controller back to
   firmware and says so on the glass; that costs the USB stick it booted from,
   which is the accepted trade.
3. Through passphrase, entropy, upstream (Enter accepts each), then the
   keypair generates, then Complete.
4. Four screens follow: disk probe, disks, xHCI, wake. Enter through them.
5. **Menu.** Down once to `Graphical UI`, Enter.

### What a pass looks like, and what to bring home

| Read | Verdict |
|---|---|
| `sc=` moves when a key is struck | **The USB HID path holds on this board for the desktop payload.** A2b's blocking question |
| Desktop paints, seven sidebar buttons over Shutdown | **A2b passes.** Photograph it: this is the screen the project has been working toward |
| Any button does nothing when clicked | A regression -- as of main 12905 every drawn button has a handler |
| Monitor pane (`m`): `acpi` row reads `rev N tables N` | The stub published an RSDP and the parse works on real firmware |
| Monitor pane: `sci` value | **Worth reading deliberately.** codex-vm publishes a bogus `0x2000` (8192) here (`codex-vm.c:3388`). A real GSI, 9 or similar, on the panel confirms that value is purely an emulator artifact |
| Monitor pane: `display` and `framebuffer` rows | The geometry the payload believes, against the panel you are looking at |

Photographs to bring home: the boot diag, the desktop, and the Monitor pane.
Any screen that hangs, with its last trace line legible.

**Every reading table in this sheet must say what an ABSENT reading means,
not only what each present one does.** Routed by reek 2026-08-04 and it cost
them a boot: their sheet gave two branches for a retry line, `success` and
`USB TRANSACTION ERROR`, and the board answered with a third state the sheet
did not admit -- the line simply was not there, which meant the first attempt
had succeeded and no retry ever ran. A table that enumerates outcomes and
silently omits one sends its reader looking for a fault that is not there.
This is L-MISROUTE's quieter cousin: that row is about a sheet pre-assigning
the wrong cause, this is about a sheet having no row at all for what the
glass actually shows. When you write a reading table, write the missing-line
row first.

---

## STATUS 2026-08-03: READ THIS BEFORE THE REST OF THE SHEET

**The keyboard campaign that occupies most of this document is CLOSED.** Both
input paths work on the ASUS: the firmware path (main 12609) and USB HID (main
12627, proven on the board 2026-08-03). Everything below about rungs, the
`kbd-diag-v*` series, SET_IDLE, EPINT and the silent keyboard is **the record
of how that was answered, not open work.** Do not plan against it and do not
re-derive any of it.

It is kept rather than trimmed because it is the evidence: the ladder, the
QR-decoded bytes off the glass, and which hypotheses were killed by which
measurement. Four of them were wrong in ways worth not repeating.

**What this sheet still governs:** the procedure. Red owns it. Any future
sitting is planned here, top to bottom, under the rule below, and the standing
constraints in section 4 are all still live -- most of all **PULL THE STICK
OUT, DO NOT EJECT IT** (Windows rewrites the partition table on
re-enumeration and destroys the GPT the flasher just verified).

**What is actually open and would justify a sitting:**

1. ~~**The ASUS display defect.**~~ **CLOSED. This entry was stale when it was
   written and it is struck rather than deleted, because it sent work at the
   wrong target twice.** It is the ConOut re-mode of item 1 under v11/v12 below:
   AMI's GraphicsConsole re-modes the scanout on the stub's first ConOut call, so
   a stub that read `Mode->Info` first published geometry that was correct for a
   mode that no longer existed -- which is exactly the "geometry reads back
   correct" this entry treated as ruling the cause out. Cured in
   `build/cdx-to-pe.ps1` (clear first, ask after), confirmed on the glass by v11
   on 2026-08-02, and gated since 2026-08-03 by
   `build/boot/test-conout-remode.ps1`. **`test-ovmf.ps1` was never the answer**:
   OVMF does not re-mode on ClearScreen, so no QEMU geometry could express it.
   Only the residual 4:3 aspect stretch is open, and that is a native-mode
   SetMode in the stub rather than a defect.
2. **Rung 3, `msc-align.img`, the 64 KB TRB crossing.** Reek's, and it was
   dropped last time on a false negative. **The account of that false negative
   was itself wrong and is corrected here (reek, verified in source
   2026-08-03).** It read "every device on the Intel controller was Full or Low
   speed, so none was the boot stick" -- but that was read off a display showing
   **8 PORTSC rows for a controller reporting 26 ports**, and a SuperSpeed
   device sits on port 9 where the probe could not show it. Same shape as
   scanning bus 0 only and reporting NONE FOUND, which is the shape the entry
   itself invoked.

   The port-cell overflow behind it is fixed (`xhci-port-cells = 8` now guards
   the write in `xhci-diag-ports`, `apps/works/GopXhci.codex`). It mattered
   beyond the display: cell 28 is `xhci-released`, `kbd-pump` stops dead on it
   being non-zero, and port 8's PORTSC was landing there with Port Power alone
   setting it. That was blamed on firmware residue in unzeroed metal RAM, and
   `xhci-reset-handback` already zeroes 28 and 29 on the way in, so residue was
   never the mechanism. **QEMU could not have shown it: its controller reports
   exactly eight ports, so the bed sat precisely on the boundary.**

With 1 closed, rung 3 is the only open row and it is not scheduled. **Do not
propose a boot for it until its dev-box work lands.** The next thing that would
justify a body is A2b, the desktop itself on the panel, and that is red's to
plan here when a GopDesk payload has been up this ladder.

---

Release row R6: *the stick boots on real hardware.* It is the only row an
agent cannot finish, and the human body it needs is the scarcest device on
the bus (R-5). This sheet exists so that body is spent **once**.

The governing rule: **every question that can be answered before the
sitting is answered before the sitting.** What is left is the set of
questions only the target machine can answer.

Run it top to bottom. Nothing here needs an agent in the loop.

**Owner, from 2026-07-29: red owns this sheet, the stick flashing and the
sitting sequence.** fester owns A1 and builds the payloads; red decides
what flies, in what order, and what each rung has to bring home. The four
lanes' requests are consolidated in section 4 and every one of them has
been answered there, including the ones that were declined. An agent
asking "will my thing fly" should find the answer in this document rather
than in a workplan exchange.

**Attempt 1 (2026-07-29) returned one bit.** `pci-probe.img` was flashed
and the ASUS did not boot it, and because every failure path in
`option_a_stub.asm` ended at `fatal`, which is `jmp fatal`, there was
nothing to read. Two things changed as a result and both are load-bearing
below: the stub now paints two liveness colours (main 12073), and the
artifact that flies must be ON this ladder before it is flashed. The full
account is `docs/Stories/TheStickDidNotBoot.md`.

**ATTEMPT 2 (2026-07-29) FLEW AND THE STICK BOOTS.** Three of the four
rungs went up, all four sitting questions came back answered, and nothing
outstanding needs the board again. What each rung returned is recorded at
that rung below; the consolidated answers are in `CurrentPlan.md` (main
12170). Rungs flown and dropped, which section 6 makes red's obligation to
record:

| Rung | Flew | Outcome |
|---|---|---|
| 1 `scene-probe.img` | **yes** | PASS. 1920x1080, stride 2048, channel order correct |
| 2 `inventory.img` | **yes** | PASS. Intel I219-V `MAP=ok` with its station address, no PS/2, xHCI enumerates HID |
| 3 `msc-align.img` | **no** | Gated off by rung 2's `disk=n`, and **that gate was wrong** -- see the rung. **Its blocker is now cleared and it owes one more trip, carrying a second payload** |
| 4 `kbd-probe.img` | **yes** | Flown on rung 2's PS/2 answer as the ladder specifies. HID enumerates and delivers nothing |

**The one thing this ladder got wrong is rung 3's gate condition**, and it
is written up at that rung rather than softened here: `disk=n` was a false
negative from a defect in the instrument, so a rung was dropped on an
answer the probe could not give. It costs a trip.

---

## FLOWN 2026-08-02 EVENING: `kbd-diag-v11.img` (F0BD5738). What it brought home:

| Read | Verdict |
|---|---|
| Screen legible, `GEO w=1024 h=768 stride=1024 sc=1` | **The display defect is CLOSED on metal and the re-mode mechanism is confirmed**: AMI's console picked 1024x768 and the payload learned it. Aspect is stretched 4:3; a native-mode SetMode in the stub is the follow-up, not a defect |
| `HOST id=8086a12f run=y ports=26 slots=64 HCC=200077c1 xECP=8192` | First Intel readings ever taken. `CSZ=0` -- this part uses 32-byte contexts, measured |
| Phase 1 and 3: `EPINT=0`, `REPORT` all zero with key held, `EPCTX est=1 dq=<ring base>` | Endpoint reports RUNNING; no DMA ever landed. Single-driver (EBS ran), so the two-driver excuse is gone |
| **Phase 2: `rel=y reclaim=y` and KEYS DELIVERED** | **The firmware handback works and the PS/2-emulation fallback is REAL on this board (R-3)**. The physical keyboard, port and controller are all healthy; the delta is our periodic schedule vs the firmware's |
| `disk=n mount=n FILE-WRITES=0`, no KBDDIAG.TXT on the stick (verified at the box) | **Our MSC attach fails on the real ASMedia** even with the multi-controller walk in. OVMF passes the same image. Named defect, open |
| `ATTR disk-ctl=2760958976 walked=7f BAR=672`, ghost `CTL 2:` entry | Firmware leftovers in never-written diag cells -- metal RAM is not zeroed, every bed's is. Fixed in v12: the block is zeroed at attach |

## FLOWN 2026-08-03: `kbd-diag-v12.img` (53D85234). What it brought home:

| Read | Verdict |
|---|---|
| `EPCTX est=1` (Damian, from the glass) | **The endpoint claims RUNNING in a single-driver world with a clean face.** Configure accepted, doorbell rung, every state software can read is correct, and the controller still never fetches a TRB. The remaining question is the one nothing passive can see: whether the scheduler ever walked our ring. v13 carries the instrument that forces the answer |

## FLOWN 2026-08-03 (second flight): `kbd-diag-v13.img` (6A6FB5CC). What it brought home:

| Read | Verdict |
|---|---|
| `STOPX e=y cc=1 est=3 dq=<ring base>` (Damian: "re=3, otherwise as expected") | **The controller's own testimony: its internal dequeue never left the ring base.** Command ring, event ring and endpoint commands all work mid-pump; the transfer side never started. Combined with phase 2's delivered keys (device answers polls), the fault is between the slot doorbell and the periodic schedule |
| `re=3` (bed said `re=1`) | NOT a refused restart, and the v13 run-sheet row claiming so was wrong. xHCI 4.8.3 p.165: the Running write-back to the output context is mandatory only "before any Transfer Events are generated" -- with zero events it may lawfully read 3 forever. `re=3` therefore restates EPINT=0. The bed said 1 because the bed generates a transfer event immediately at restart, forcing the write-back; metal generated none |
| Still unread from the photo: `LATCH=` and `code=` after STOPX appeared | xHCI 4.6.9 p.134 makes a **Stopped Transfer Event (code 26/27/28) MANDATORY before the stop's command completion, even on an idle ring**. The probe latches it; it is already on the flown glass. LATCH=1/code=1A-1C means transfer-event generation works end-to-end. LATCH=0/code=00 means the xHC skipped a mandatory transfer event for this endpoint -- the transfer half of this endpoint is dead in a way even a stop cannot wake |

**Process finding, Damian's, and it stands:** every one of v10-v13's bed
gaps traces to the vm model being written from what the driver expected
instead of from the spec, and the spec was never even in the tree. It is
now: `docs/Reference/xHCI_Specification.pdf` (rev 1.2, May 2019, with
`xHCI_Specification.txt` for Grep), and the derivation of every claim above
with page numbers is `docs/Reference/xHCI_ServiceModel_Notes.md`. Reading
it found, same day: the mandatory Stopped Transfer Event (absent from the
vm), Stop-from-Halted/Error must refuse with Context State Error (the vm
accepted), MFINDEX (absent -- a bed could not tell a dead frame counter
from a live one), and **Transfer Events carrying Endpoint ID zero** (xHCI
6.4.2.1), which had the probe's EPINT row counting real bed completions as
OTH since v10. A bed arm is now written FROM a spec section, cited, or not
written.

## FLOWN 2026-08-03 (third flight): `kbd-diag-v14.img` (CDF7E707). THE FAULT MOVES OFF THE CONTROLLER.

| Read | Verdict |
|---|---|
| `SCHEDX p=00000603 pls=0 mf=+857 f1=1a` | Port in U0, powered, enabled, full-speed. MFINDEX ticking at the right rate: frames exist. And `f1=1a` = FSE code 26 = **Stopped, TD IN PROGRESS (4.6.9 p.134): the controller fetched the TRB and is issuing transactions on the wire.** The idle-ring answer would have been 27. The "scheduler never touched the ring" verdict from v13 is DEAD, killed by this instrument |
| `S2 e=y cc=1 dq2=751b40c0 f2=1a` | Same after restart: TD back in progress, still no data. Both stops answered with the mandatory event -- transfer-event generation works end to end |
| `EPINT=0`, `REPORT` all zero, key held; phase 2 (prior flights) delivers keys | **The device never completes an IN for our driver while completing them for the firmware.** With the controller now proven to be polling, the remaining delta is device/transaction state: data toggle, idle rate, protocol -- USB2/HID territory, and neither of those specs is in the tree yet. They go in before any hypothesis is coded |
| **Probe FROZE at p=2428**, ~7 s after the experiment; phases 2/3 never ran this boot | ROOT CAUSE FOUND ON THE DESK, and it was never a controller defect: **the probe's paint loop has allocated unboundedly since v1** (row texts, file body, QR matrices, every iteration, bare metal, no GC) and exhausts the heap at roughly 2,400 paints. The `-hid-nak` bed reproduced the death; one run died early enough for the OOM handler to say `OUT OF MEMORY` on COM1 in so many words; the other deaths were the same exhaustion reaching the QR encoder as a failed allocation and tripping its bounds trap (#UD). On metal COM1 goes nowhere, so it photographs as a freeze. Rule 8's exact red-flag pattern, present in every probe version, tipped over by v14's longer rows. FIXED in v15: `__heap-save`/`__heap-restore` bracket both phase loops; bed runs 13,000+ paints where 2,400 used to die |

**The USB2 and HID specs are in the tree**
(`USB_2_0_Specification.pdf`, `HID_1_11_Specification.pdf`, both with
Grep text; derivation in `xHCI_ServiceModel_Notes.md` "The device
side"). The board has answered every question the xHCI spec knows how
to ask; v15 asks the device-side ones.

## FLOWN 2026-08-03 (fourth flight): `kbd-diag-v15.img` (C12179E2). THE PIPE DELIVERS.

| Read | Verdict |
|---|---|
| **`EPINT=97` and climbing until 48 s; QR bodies visibly change with key presses** | **THE INTERRUPT PIPE IS ALIVE ON THE ASUS -- first delivery through our driver in fifteen versions.** The 97 events are the keyboard's 500 ms idle heartbeats; key data reaches the reports. SET_IDLE(0) was the killer, exactly as the F.3 derivation said |
| `DEVX f=1f cfg=1 p=0 i=125` | The device's own testimony seals it: GET_IDLE reads 125 x 4 ms = 500 ms -- the factory default idle rate, restored the moment we stopped zeroing it. All five EP0 requests answered |
| **EPINT freezes at 97 at ~48 s, pump keeps running** | **SCHEDX killed the pipe it was built to autopsy**: the 45 s Stop Endpoint pair ran against a LIVE endpoint, and on this Intel the doorbell-restart after a Stop does not resume periodic delivery. Invisible in v13/v14 (pipe already dead). Fixed in v16: the experiments fire only if EPINT is still 0 at trigger time. The non-resuming restart is a real Intel behavior worth knowing for any future driver stop path -- recorded, not chased |
| Phase countdown froze at "7 s" (~82 s wall) while p climbed past 15,000 | The PIT tick source stalls at ~82 s on metal. The paint-count fallback (p=20,000) still advances phases, so the boot is not stuck, but the tick stall is a new, real observation -- open item, not keyboard-blocking |
| QR panel re-render overpaints the old codes without clearing | Cosmetic probe bug; v16 clears the panel band before re-rendering |
| `R2:` all zeros | **Recorded as "no finding" and it was THE finding.** Corrected 2026-08-04. R1/R2 are `kd-devx`'s GET_REPORT samples over the CONTROL pipe (`KbdDiagProbe.codex:348-354`), the only report DATA this probe ever read, and they were zero. The heading above claims the pipe delivers on the strength of EPINT, which counts EVENTS. **v15 proved events arrive and GET_IDLE reads 125; it never proved data lands anywhere**, which is exactly what the desktop measured on 2026-08-04: SUCCESS on our own endpoint, buffer untouched. Same events-versus-data conflation as the SCANS gap, one layer earlier |

## NEXT BOOT: `kbd-diag-v16.img` -- the victory-lap boot

Changes from v15: experiments (SCHEDX + DEVX) gated on a DEAD pipe
(EPINT=0 at 45 s), so a working keyboard is never touched; QR panel
cleared before re-render. Reading: **`EPINT` climbing past 90 s,
`SCANS` climbing while a key is held, phases advancing 1 to 2 to 3 --
that screen closes the keyboard campaign.** If SCANS stays 0 while
EPINT climbs and a key is held, the report-decode path is the last
open item (kbd-drain), and the QR bytes carry the raw reports to
diagnose it from the photo.

## SUPERSEDED RUN SHEET (v15 flew): `kbd-diag-v15.img` -- the fix candidate flies with its own proof instruments

**Changes from v14, each bed-proven before asking for a body:**

1. **The freeze is fixed** (heap bracket, above): 100 s in the
   `-hid-nak` bed, 13,000+ paints, all three phases reachable.
2. **The driver no longer sends SET_IDLE.** HID 1.11 F.3: a boot
   keyboard shall report on EVERY interrupt poll by default; Set_Idle
   duration 0 -- which we sent at setup since the driver was written --
   is the one request that overrides that into report-only-on-change
   silence, and a quirky device may over-honor it as never-report.
   That is the exact measured shape of the ASUS (controller polls,
   device NAKs, firmware that never sends it gets keys). Proof pair
   under the new `-hid-idle-quirk` bed arm: v14 image EPINT=0 (silent),
   v15 image EPINT=1,100,004 in 30 s. Whether the ASUS keyboard is
   precisely this quirk is what the flight decides; the change is
   correct under F.3 regardless.
3. **New DEVX row** (fires ~4 s after SCHEDX, from the pump path): the
   device's own answers over the EP0 pipe that demonstrably works --
   GET_CONFIGURATION, GET_PROTOCOL, GET_IDLE, and GET_REPORT twice,
   ~1 s apart (HID 7.2.1, mandatory). `f=1f` means all five answered.
4. SCHEDX/STOPX cells moved off the 37000 band (an unnamed tenant owns
   bytes 37132+; boundary measured, not derived) onto the probe-owned
   xdiag page.

**Reading it:**

| Read | Verdict |
|---|---|
| `EPINT` climbs with a key held | **THE KEYBOARD WORKS. SET_IDLE was the killer.** The driver fix ships as-is; close the campaign |
| `EPINT=0` but `DEVX R2:` shows the held key's usage code | The interrupt pipe is still dead but the device is healthy, configured, and readable over EP0 -- **the GET_REPORT-polling fallback is proven on metal** and becomes the keyboard path in `GopUsbKbd` |
| `DEVX i=` nonzero | The device reports a nonzero idle rate nobody set -- firmware state surviving our takeover; a SET_IDLE(500ms) experiment is next, spec-cited |
| `DEVX f=` not 1f | Named EP0 requests stall on metal; the failing bits say which HID requests this keyboard refuses |
| `DEVX R2:` all zeros with key held and `f=1f` | GET_REPORT answers but empty while a key is down: protocol state suspect; compare `p=` (0 = boot as we set, 1 = report -- our SET_PROTOCOL did not take) |

## SUPERSEDED RUN SHEET (v14 flew; kept for the SCHEDX reading table)

One change from v13: STOPX grows into **SCHEDX**, the spec-derived
instrument set for the one remaining question. Same trigger (once, 45 s
into phase 1, pump path), in order:

1. **PORTSC of the keyboard's root port** (5.4.8): PLS must be U0(0). A
   port in U3(3) is suspended and starves exactly the periodic schedule
   while the firmware handback still works. One MMIO read, from the safe
   path.
2. **MFINDEX twice across ~110 ms** (5.5.1; halt rule 4.14.2 p.260): if
   the delta is 0 the controller believes every port is down and NO
   periodic pipe has frames -- keyboard silence becomes a symptom, not
   the defect.
3. **Stop #1** as in v13, now also capturing the Stopped Transfer Event
   code from the latch (`f1=`).
4. **Restart, wait ~220 ms (>> IST+ESIT), Stop #2** (`S2 cc= dq2= f2=`):
   if `dq2` moved, the restart scheduled and the loss is downstream; if
   identical to `dq1`, the doorbell-to-schedule path is dead, measured
   twice, with the mandatory-event behavior sampled both times.

| SCHEDX reads (with key held, EPINT=0) | Verdict |
|---|---|
| `pls=` not 0 | **Port not in U0 at pump time.** The schedule is starved by link state; the question becomes who moved the port and when |
| `mf=+0` | **No frames exist.** The controller thinks all ports are down (4.14.2 p.260); the keyboard is collateral |
| `pls=0`, `mf=` advancing, `f1=00`, `dq2=dq1` | Frames exist, port live, stop honored on the command side -- and the mandatory Stopped Transfer Event never came and the restart never scheduled. **The transfer/event half of this endpoint is dead while its command half answers.** That is an errata-class controller behavior; next step is comparing against how firmware configures the endpoint (its context is readable before takeover) |
| `f1=1A/1B/1C` and `dq2=dq1` | Transfer events DO generate at pump time -- the silence is purely the schedule never issuing the IN. Firmware-context comparison, same as above, but with event delivery proven |
| `dq2` advanced | The restart scheduled. The original doorbell's schedule entry was lost -- a one-shot loss, not a dead path; re-ring after configure becomes the workaround candidate |

Bed reference (codex-vm, 1920x1080): SCHEDX `p=00000403 pls=0 mf=+476
f1=1b`, S2 `e=y cc=1 dq2=<same as dq1> f2=1b`, EPINT in the millions (the
epid fix makes the bed's EPINT row honest for the first time). `dq2=dq1`
is CORRECT in the bed: its ring is drained empty at experiment time, as
EPINT proves. On metal the armed TRB is pending and unfetched, so dq
readings stay the restart discriminator; the bed's is the empty-ring
control arm, not the metal prediction. Supporting vm work, each from a
cited section: mandatory FSE on Stop (4.6.9), Stop refused from
Halted/Error (4.8.3), Endpoint ID in Transfer Events (6.4.2.1), MFINDEX
(5.5.1), and a WHP robustness fix (instruction bytes refetched via GVA
translation when the exit carries none -- first exposed by full-rate bed
event delivery).

Carried from v13 unchanged: v14 stamp on the glass, diag block zeroed,
CTL row masked, full-width rows at 1024, both withdrawn repaint
instruments stay withdrawn (SCHEDX runs from the pump path, where
`kbd-arm` and `xr-release` already touch MMIO safely).

**What flew underneath v11/v12 (all bed-proven before asking for a body;
v13 carries every item unchanged):**

1. **The display corruption has a found cause and a fix in this image.** The
   cdx-to-pe stub read the GOP geometry and THEN made its first ConOut call;
   on AMI that call activates the GraphicsConsole, which sets its own
   graphics mode, so every 2026-08-02 image painted splash-mode geometry
   (1920x1080/2048) into a re-moded scanout. That one mechanism produces
   every measured symptom: alternate lines black, glyphs stretched, long-line
   tails overpainting the next row, the dark right-edge band, StrideProbe's
   width-stepped bar shattering into exactly eight aliased copies
   (4096/gcd(7680,4096) = 8), and its stride-stepped bar photographing
   "solid" because a step of twice the true pitch is vertical too, dashed in
   a way a photo cannot resolve. Reproduced in codex-vm under
   `-uefi-conout-remode`, then cured by clearing FIRST and reading the
   geometry AFTER: same payload bytes render clean at 1920x1080/2048 and at
   1024x768/1024. The 2026-07-29/31 boots were legible because the deleted
   asm stub never called ConOut at all.
2. **This image calls ExitBootServices (stub `-Ebs`), which no cdx-to-pe
   image ever did.** Until now every ConIn-era boot ran with boot services
   alive, meaning the FIRMWARE'S xHCI driver stayed live on the Intel
   controller while ours reset and drove it -- two drivers, one controller,
   and nothing the diag reported about the Intel was a single-driver
   measurement. ConIn "working" was the firmware driver, not ours. This boot
   is the first driver-truth measurement on this board since the stub
   migration. (KeyProof and the dev console must keep boot services and must
   NOT be built `-Ebs`.)
3. **The Intel's own registers become readable for the first time.** reek's
   per-controller re-record (main 12543) is in this payload's source, so the
   HOST row finally shows `8086a12f` with ITS `run/reset/ports/HCC/CSZ`
   rather than the ASMedia's. Nothing has ever read the Intel's CSZ or
   MaxPorts on metal.
4. **The driver's two Intel-only paths are now bed-certified.** codex-vm
   gained `-xhci-csz` (64-byte contexts) and `-xhci-scratch N` (mandatory
   scratchpad array, refusal arm demonstrated): the driver passes both and
   both combined, so 64-byte contexts and scratchpad are off the suspect
   list unless the metal readings contradict the bed.
5. **New row: `EPCTX est= dq= cyc=`** reads the CONTROLLER'S OWN output
   endpoint context for the keyboard's interrupt endpoint (xHCI 6.2.3). This
   is the row that names which silence it is.

**Flight: flash (elevated), boot, hold a key during phase 1, photograph the
screen and then the QR codes after the final-counts line; bring KBDDIAG.TXT
home.** Flash, verify, pull the stick (the eject hazard is fixed at the cause; see the superseded note below).

```powershell
Get-Disk | Where-Object BusType -eq 'USB'
Start-Process pwsh -Verb RunAs -PassThru -ArgumentList '-NoProfile','-File',
  'D:\Projects\NewRepository-red\build\flash-usb.ps1','-Image','<full path to img>',
  '-DiskNumber','N','-SpecFit','-Force','-Log','D:\Projects\NewRepository-red\build-output\flash.log'
```

**Reading it:**

| Read | Verdict |
|---|---|
| Screen fully legible, GEO states the mode | **The display defect is closed on metal.** Whatever geometry AMI's console picked, the payload learned it after activation |
| Screen still corrupt | The re-mode happens later than ClearScreen on this firmware; photograph GEO and the corruption pattern, both carry the pitch arithmetic |
| `EPINT` climbs with a key held | **The xHCI driver delivers on Intel silicon post-EBS. The keyboard question closes** |
| `EPINT=0`, `EPCTX est=1` with `dq=` parked at ring base | The v11/v12 signature, already measured. **The STOPX row is now the discriminator** -- read it against the NEXT BOOT table above |
| `EPINT=0`, `EPCTX est=2` or `est=4` | Halted / error: the device rejected something after configure; a Reset Endpoint experiment is next |
| `EPINT=0`, `EPCTX est=0` | Configure Endpoint never took despite reporting success -- the completion-code path is the suspect, not the schedule |
| `SCANS` climbs | Full path works; anything still dead is above the driver |

## 0. Preconditions -- BLOCKING

Do not flash anything until both are true. They are red's rows, not
yours, and neither is a formality.

| | What | Why it blocks |
|---|---|---|
| **R1** | **CLOSED 2026-07-29 against main 12152.** Gate green, hard fixed point in one pass, 201.2s, `constants.hash` unchanged at 268 constants. **`build/output/Sut.cdx` is BYTE-IDENTICAL to the depot `seed/Codex.cdx`**, whole file, 2,714,156 bytes, content hash `24281e77...baff` at bytes 8-39. The seed reproduces from its source on main, so its digest `6671C19A0F78F630` may now be quoted as provenance. The `F67D4605` this row used to warn about is two seeds stale |
| **R8** | **RE-SCOPED 2026-07-29: no longer a precondition for this ladder.** See below |

**Both preconditions are resolved: R1 is green and R8 is out of scope for
this ladder, so section 0 no longer blocks the sitting.** R1 was verified on
the content hash at bytes 8-39 as well as the whole file, because a green
gate alone does NOT establish that the seed matches its source: the gate
proves `Sut === stage1`, and `Sut === seed` is the separate question this row
exists to ask.

**R8 moved, and it is not a formality that it moved.** It was blocking
because the old boot 3 flashed `seed/Codex.img`, and that rung is off the
attempt-2 ladder. **R8 is now a precondition for boot 3 only**, alongside
the ConOut gap and the missing liveness marks. Nobody should spend on it
for this sitting.

fester reports (2026-07-29) that refreshing the img today produces an image
which boots, paints the full dev console, and then prints `OUT OF MEMORY`,
described as pre-existing and confirmed against a control. **Do not record
that as a memory-exhaustion finding yet.** The boot 3 block below already
documents an `OUT OF MEMORY` from this exact payload that was a **false
report from a clobbered deck-pointer register, with the heap untouched**,
and it is written there specifically so nobody revives it. A control
showing the message appears is not a control showing the heap is exhausted;
the measurement that separates them is the heap high-water mark, not the
string. This tree's OOM diagnostics have a history here: the handler
printed to COM2 for its whole life, so a working guard read as never
firing.

So R8's real state is: **not needed now, and its one reported symptom needs
one more measurement before it is a defect.**

---

## 1. Build the artifacts (dev box)

Four images for attempt 2, **listed in the order they fly**, which is not
the order they were written. All four payloads exist. `Inventory.codex` is
built and gated as of main 12142, digest `0DC6C755...00BE3`; **the digest in
CL 12115 is SUPERSEDED and that image must not fly.**

```powershell
# Rung 1. Display, channel order, AND THE PANEL MODE. No input, halts.
# It flies first because rung 2's QR capacity depends on the mode this reports.
build/boot/build-option-a.ps1 -Src build/boot/diag/SceneProbe.codex `
    -Out build/boot/scene-probe.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx

# Rung 2. The combined inventory probe. See rung 2 for the QR body budget.
build/boot/build-option-a.ps1 -Src build/boot/diag/Inventory.codex `
    -Out build/boot/inventory.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx

# Rung 3, conditional. The 64 KB TRB boundary question. No input, halts.
build/boot/build-option-a.ps1 -Src build/boot/diag/MscAlignProbe.codex `
    -Out build/boot/msc-align.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx

# Rung 4, conditional. Three timed phases, and the only WRITE evidence.
build/boot/build-option-a.ps1 -Src build/boot/diag/KbdDiagProbe.codex `
    -Out build/boot/kbd-probe.img -Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx
```

**`seed/Codex.img` is NOT built for this attempt.** Boot 3 is not on the
attempt-2 ladder, for the reason in its own block below, so do not spend
`build/build-boot-img.ps1` or a flash on it.

**Pass `-Source ''` on every line.** It is not covered by `-Seed ''` and
defaults to `build-output/Codex.codex`, so a probe otherwise carries a
~3 MB `SOURCE.SRC` it never reads and doubles the image for nothing.

**Pass `-Ebs` on every DRIVER-TRUTH line, and it is missing from all four
above.** Routed by reek 2026-08-04 and corrected here: their entry said the
invocation was recorded nowhere, and it is recorded -- it is the block above,
and has been -- but **not one of those four lines carries the switch**, which
is the half that matters. Without it boot services stay alive, the FIRMWARE'S
xHCI driver keeps driving the controller under test, and nothing the probe
reports about that controller is a single-driver measurement. `ConIn
"working"` on the 2026-07-29 ladder was the firmware's driver, not ours.
`cdx-to-pe.ps1` makes the switch the discriminator: set it for anything
measuring our own drivers, leave it OFF only for payloads that call
ConIn/ConOut, which is KeyProof and the dev console. `DeskBoot` and `GopBoot`
are driver-truth and are built with it.

**Rung 3 has an artifact now** -- `build/boot/msc-align.img` (reek,
2026-08-04) -- and the chapter had existed since the ladder was written with no
image ever built from it, which is why that rung kept being "one trip away"
with nothing to take.

**It is STILL NOT ANSWERED, and I recorded the opposite here for part of a
day.** The image flew and its liveness arm read a hardcoded LBA 100000: out of
range on the 16 MB emulator image, an ordinary sector on the 28.9 GB stick. So
on metal it answered `ok=y` and the probe correctly announced that it had
proved nothing, which left the two green rows above it unclaimable. Fixed by
deriving the LBA from `md-sectors`. Corrected from reek's outbox, and the
correction is the point: **an artifact existing and a question being answered
are different claims**, and I folded the first in as though it were the second.

**A run sheet that says "this arm must read N" must also say what the arm
assumes about the MEDIUM.** reek's, 2026-08-04, and it is the third entry in
this family today alongside the absent-reading rule below. A constant that is
out of range on the bed and in range on the stick reads as a pass on the one
machine you cannot re-run cheaply, and neither number is visibly wrong on its
own. State the assumption next to the constant.

**Pass `-Kernel`.** Each build prints the kernel it used and it must say
`seed\Codex.cdx` with the seed's digest. A NOTE saying the kernel is not
the seed means the argument did not take and the artifact has no
provenance.

**Then gate the FILE you are about to flash, not one built from the same
source with different arguments.** Both of Loop A's gates, on the exact
file: the GPT structural check and an OVMF boot of the image file.
Different arguments give a different disk size, FAT geometry, file set and
partition count, and the failure modes live in the vehicle. Skipping this
is what cost attempt 1.

```powershell
build/boot/test-ovmf.ps1 -Img build/boot/inventory.img -Out probe.png `
    -UsbDisk -UsbKbd -UsbMouse -NoPs2
```

### The four images do not exist yet, and the digest is the only provenance

**Measured 2026-07-29: none of the four `.img` files exists on disk in any
workspace, including the one that built and gated `inventory.img`.** They
are build outputs, so each workspace makes its own and they do not travel.
What survived of fester's gated build is its **digest**, and that digest is
therefore the entire tie between "the artifact we proved" and "the artifact
that goes on the stick". Attempt 1's failure was flashing an artifact the
governing document did not cover; this is the same hazard one step along.

**So two checks, and neither is optional.**

**1. Rebuild `inventory.img` and confirm it hashes to
`0DC6C755B450F1A538F569E4E1C162180D52948A8FCD23E5779826032CC00BE3`.** With
`-Kernel seed/Codex.cdx` pinning the compiler, the build should reproduce
byte for byte. **If it does not reproduce, the recorded digest is not
provenance and that is a finding, not a nuisance** -- it means the recipe
does not determine the artifact, and no digest recorded at any sitting would
mean anything. Report it rather than working around it.

**2. Immediately before each flash, hash the file you are about to write.**
One line, and it is precisely the check attempt 1 did not have:

```powershell
Get-FileHash build/boot/<image>.img -Algorithm SHA256
```

Confirm it matches the digest recorded for that rung. A mismatch means you
are flashing something nobody gated. Do not proceed on the assumption that
it is "the same source" -- different arguments give a different disk size,
FAT geometry, file set and partition count, and the failure modes live in
the vehicle.

**A note for after the sitting, not for now:** `optiona-milestone.img` IS in
the depot as a tracked binary, so there is precedent for submitting these
four and making "the gated artifact is the flashed artifact" mechanical
rather than a discipline. That is a process change and it should not be made
in the hour before someone sits down.

Record the digests before leaving the dev box. They are what a later
disagreement is settled against, and without them a photograph is an
anecdote.

```powershell
Get-FileHash build/boot/inventory.img, build/boot/scene-probe.img, `
    build/boot/msc-align.img, build/boot/kbd-probe.img -Algorithm SHA256
```

### RUNG 1 FLEW AND PASSED, 2026-07-29. A1 IS ANSWERED: THE STICK BOOTS.

First successful boot of a Codex payload on the ASUS TUF. `scene-probe.img`
rendered the cube, the pyramid, the ground plane and the chrome band, with
`software 3D, no GPU` on the glass.

| Answer | Value | Consequence |
|---|---|---|
| **Mode** | **1920x1080** | Rung 2's QR budget is set by this. At 1080 high the code space is roughly 650 px against 372 at 1280x800, so Inventory should land at scale 6 rather than scraping 3. Red's split condition does not fire |
| **Stride** | **2048** | **128 pixels wider than the visible width, so this panel really does pad its scanlines.** The padded-scanline case is now METAL rather than assumed, and anything indexing rows by width instead of stride will shear on this board |
| **Channel order** | **cube blue, pyramid red** | Correct. The firmware is BGR as the stub assumes, so the unread `PixelFormat` field is not biting here. val's A6 can rely on colour |
| **Display path** | GOP linear framebuffer, painted after ExitBootServices | The Blt-only risk stays closed, now on the target rather than by inference |

**What made it boot is not in the payload.** Every earlier stick carried an
invalid GPT by the time it reached the board, because our own procedure
destroyed it: see the closing note in `build/flash-usb.ps1` and CL 12168.
Three defects together -- a partition entry array below the UEFI 16 KB
minimum, a one-sector disagreement between the two writers over the backup
array, and no volume lock during the write -- let Windows "repair" the table
into one with no readable partitions. The instruction to EJECT the stick,
which this sheet and the flasher both used to give, was one of the triggers.
A stick is now proven to survive a full remove-and-reinsert unchanged.

### RECORDED DIGESTS, all four, 2026-07-29 (fester)

Built in flight order against seed `6671C19A0F78F630` with
`-Seed '' -Font '' -Source '' -Kernel seed/Codex.cdx`, and each FILE booted
under OVMF and confirmed painting, not merely built.

**STALE as of 2026-07-30: rungs 2, 3 and 4 must be rebuilt before anything
is flashed.** `xhci-diag` moved off the PML4 page (36200 -> 0x1D000) and
`GopXhci` is in the cite closure of `inventory.img`, `msc-align.img` and
`kbd-probe.img`, so all three digests below are superseded. Measured, not
assumed: `SceneProbe` cites neither `GopXhci` nor `GopUsb`, so rung 1 is the
one entry the change cannot reach. Re-take at flight time against the tree
state that flies, per F-d -- recording a fresh number now would only go stale
again while the fleet is active.

**RE-TAKEN after the GPT geometry fix (CL 12168), which moves every image.
The digests below supersede the earlier set, and the earlier set must not be
flashed.** Rung 1's entry is the image that actually booted the ASUS.

| Rung | Image | SHA256 | Gate flags |
|---|---|---|---|
| 1 | `scene-probe.img` | `5DC0C6C303B8288B38CED8D64DF2FDE1B48A154923B967AFDD839FDC3982D4CF` | none |
| 2 | `inventory.img` | `F2CFEE5C08A38C9DD9BD25CF4E93E68B6186106B979250192640DB57D97B481D` | `-UsbDisk -UsbKbd -UsbMouse` |
| 3 | `msc-align.img` | `0D4C2431214156F4F34E1EA8368BF2CD0EC55639A2611C0C42D195091AD5D4FA` | `-UsbDisk` |
| 4 | `kbd-probe.img` | `46CE613FBC49F03FFAD4CFE7FF33C4A05EAAEDB583D658E7EFC56338FC6E1AE2` | `-UsbDisk -UsbKbd` |

Superseded and dead: `42DE7A04`, `B41A4697`, `3DB347F1`, `6E1FDDBE`, and
`0DC6C755` before them. A digest is provenance only against a stated tree
state, and this one is main 12168.

**Check 1 is answered: the recipe DOES determine the artifact.** Inventory
built twice from identical source gave byte-identical images, and all four
digests above reproduced exactly on a later, fully-synced tree.

**The earlier `0DC6C755...00BE3` is superseded and must not be flashed**, and
the reason is a source change rather than non-determinism. `GopDraw` gained a
scanline clip at main 12148, and it is compiled into all four. Demonstrated
rather than argued: rebuilding Inventory with `GopDraw#3` restored reproduces
`0DC6C755` byte for byte, and with `GopDraw#4` reproduces `B41A4697` byte for
byte. One named file moves the digest and putting it back brings the old one
back.

**A digest is only provenance against a stated tree state.** These are
against main at 12159. Any change to a chapter in a payload's cite closure
moves them, whoever makes it, so re-take all four after any such change
rather than assuming an unrelated lane cannot reach them. What did NOT move
them, measured: blu's `pci-scan-all` at main 12147, because the compiler
emits by reachability and nothing calls it from these payloads. Reasoning
about which changes matter is how a stale digest survives; rebuilding is
cheap.

---

## 2. Prove the telemetry channel (R-1) -- before you need it

R-1 is a **precondition, not advice**: no hardware campaign launches
without an output channel that does not depend on the subsystem under
test. On a board with no serial port and no working storage, that
channel is the QR codes GopQr paints on the GOP framebuffer, photographed
and decoded back to exact bytes.

Confirm the channel works on this tree before the sitting:

```powershell
pwsh build/qr-decode-test.ps1
```

Both directions must pass -- the codes as rendered, and the codes as
photographed. Then confirm it end to end on the actual image you are
about to flash:

```powershell
tools/codex-vm.exe -kernel build/boot/kbd-probe.img -uefi -gop `
    -gop-width 1024 -gop-height 768 -headless `
    -screenshot build-output/kbd-probe.bmp -screenshot-delay 25000
pwsh tools/qr-read.ps1 -Path build-output/kbd-probe.bmp
```

It must print a `KBDDIAG v8` report. Verified this way 2026-07-28.

**A partial report is the one thing not to read past.** The decoder says
`WARNING: n of m chunks` when a code is missing. A truncated report drops
the leading lines, which are the verdict fields (`uk-ok`, `EPINT`,
`code`). Re-shoot. Do not reason from a short report.

---

## 3. The kit

- The target machine, and its firmware set to **UEFI with CSM/Legacy off**
- A USB stick. A second, different stick, because stick wear is a
  documented cause of "same image, sometimes boots"
- A phone camera. That is the whole telemetry rig
- The dev box, to re-flash between boots and to read `KBDDIAG.TXT`

```powershell
Get-Disk | Where-Object BusType -eq 'USB'      # find N -- check it twice

Start-Process pwsh -Verb RunAs -PassThru -ArgumentList '-NoProfile','-File',
  'D:\Projects\NewRepository-red\build\flash-usb.ps1','-Image','<full path to img>',
  '-DiskNumber','N','-SpecFit','-Force','-Log','D:\Projects\NewRepository-red\build-output\flash.log'
```

**This is the whole flash procedure. Do not write a wrapper .ps1 around
the flasher.** `-Log` writes the transcript where a non-elevated session
can read it, `$p.WaitForExit()` + exit code says pass/fail, and the log's
"Verified: all N bytes match" plus the four "fixup: verified" lines are
the evidence. Every one-off wrapper a session invents is flash-procedure
variance, and variance here has already cost trust.

`-SpecFit` refits the GPT to the stick in your hand. The flasher verifies
the whole image by readback; if it reports a verify failure, the stick is
the problem -- take the second one.

**SUPERSEDED 2026-08-02 (Damian): the eject hazard is fixed at the cause, and
this is no longer a procedural rule you can get wrong.** Two changes did it, and
both are structural rather than instructional:

1. **A proper Windows-compatible flash layout**, so partmgr has nothing it wants
   to "repair".
2. **The script takes the disk offline and holds it, the way Rufus does.**
   `Set-Disk -IsOffline $true`, then every volume on the target is LOCKed and
   DISMOUNTed by `DeviceIoControl` and **the handles are held until the physical
   write is closed** (`flash-usb.ps1`, the offline call and the lock loop below
   it). So the stick is not mounted while it is being written.

The observed result on this box: the stick does not automount on re-insertion,
and Explorer offers no Eject at all, so the action that used to destroy the GPT
is not reachable. Pull it out when you are ready.

**Kept because the history is the lesson, not the instruction.** What follows is
what this block said and why, and it was true of the tree that had it:
Explorer's Eject re-enumerates the device, and Windows
rewrites the partition table on arrival into one with no readable
partitions, which is exactly the "firmware never lists the stick" failure
arriving silently after a flash that reported success. Everything is
flushed and read back byte for byte before the flasher's last line, so the
eject buys no safety at all. After a locked write the volume is left
dismounted and there is nothing in Explorer to eject anyway.

**Three claims this section used to make are FALSIFIED and are gone rather
than reworded** (measured 2026-07-29, CLs 12166 through 12168):

- *"A stick that has been re-inserted into Windows has had its GPT
  rewritten underneath it"* and the standing order to re-flash before every
  boot. **A conforming stick now survives a full physical remove-and-
  reinsert byte-identical**, header CRC `0ee691ff` before and after. What
  destroyed the earlier sticks was our own image geometry, not the
  insertion: an entry array below the UEFI 16 KB minimum, a one-sector
  disagreement between `build-img` and `flash-usb` over the backup array,
  and no volume lock during the write. Fixed at main 12168. Re-flashing
  between rungs is still fine and still cheap; it is no longer a defence
  against anything.
- *"Without `-SpecFit`... Windows rewrites the disk on every insertion."*
  `-SpecFit` never stopped that rewrite. A stick flashed and verified clean
  by this script's own full readback came back with LBA 1 rewritten after
  one eject.
- **The disk-GUID theory is dead.** `OsHardwareRoadmap` has blamed partmgr
  caching its repair ruling by our deterministic disk GUID since
  2026-07-10 and carried a pending patch for it. The patch is now in, and
  it was falsified by the measurement it enabled: a stick flashed with a
  fresh GUID partmgr had provably never seen was rewritten byte for byte
  identically, and the GUID on the medium was untouched afterwards. The
  randomisation is kept for forensics, so each written stick is
  identifiable, and for nothing else.

---

## 3b. Before you power anything on -- FREE, and do this first

Two observations that cost no boot and no photograph, and the first has
the highest documented base rate of any failure on this board.

1. **Is the stick listed in the UEFI boot menu, in the UEFI section, not
   the legacy list?** Ten seconds. `OsHardwareRoadmap` records this
   board's legacy F12 list as HARDCODED, so its contents prove nothing,
   and USB UEFI entries as appearing only after several reboot and
   BIOS-visit cycles.
2. **If it is not listed, add the manual boot option** before concluding
   anything: BIOS, Add Boot Option, `\EFI\BOOT\BOOTX64.EFI`. The roadmap
   calls this the way to make this board deterministic.

Attempt 1's whole result is consistent with the stick never having been
selected, and this is the check that separates that from a payload fault
without spending a rung.

## 4. The ladder

**Who builds and who reads.** Settled 2026-07-29, answering fester's
question. **fester builds and gates all four images; the payload owners own
what the screen means.** The seam is that fester certifies the vehicle --
does this image boot and paint on this firmware, with `-Kernel` and
`-Source ''` honoured, the exact file gated on both Loop A gates, and its
digest recorded -- and val and reek certify the reading, which they have
already done under OVMF for SceneProbe, MscAlignProbe and KbdDiagProbe.
Nothing in rungs 2 through 4 needs a payload built: all three exist and are
proven. One agent building all four keeps one `-Kernel` discipline and one
digest list, which is what section 1 asks for. **If a build fails to paint,
it goes back to the payload owner; if it paints and the reading is
ambiguous, that is the owner's table to fix.**

Four rungs for attempt 2, each answering **named questions** (R-2), in
this order. Reassurance runs are not flights. Take a photograph at every
rung whether or not it looks interesting: the photograph costs nothing and
a second sitting costs a day.

**Read the screen COLOUR before anything else, at every rung.** Every
Option A image paints two liveness marks. They came from `option_a_stub.asm`
(main 12073, both confirmed by ablation under OVMF rather than by inspection);
that stub was deleted at B5.4 step 4 and **the surviving `cdx-to-pe.ps1` stub
paints the same two colours at the same two points**, so this table is unchanged.
Verified in source 2026-08-02 rather than assumed from the migration note:
`GopAcquire` calls `LocateProtocol(GOP)` and `GopFill $GopDarkBlue`
(`0x00202060`) follows it, `GopFill $GopDarkGreen` (`0x00104020`) follows the
page-table and handoff work. This table applies to all four rungs and is the
reason no separate control boot is scheduled:

| Screen | How far it got |
|---|---|
| Firmware's own screen, unchanged | Never loaded, or `LocateProtocol(GOP)` failed. **A boot-selection or medium problem, not a payload one** -- go back to section 3b |
| **Solid dark blue** and nothing more | GOP acquired. Died in `AllocatePages`, `GetMemoryMap` or `ExitBootServices` |
| **Solid dark green** and nothing more | Through ExitBootServices and our own page tables, and the handoff framebuffer is writable by us. Died in the payload |
| A painted report | Ran |

### RUNG ORDER CHANGED 2026-07-29: SceneProbe goes FIRST

**`scene-probe.img` is now rung 1 and `inventory.img` is rung 2.** The
reason is measured, not stylistic.

`Inventory`'s QR record is placed by `iv-codes` with `top` hardcoded at
388, so the summary occupies a fixed 388 px whatever the panel is and the
codes get whatever remains. Running that arithmetic against the payload's
own `iv-fits` / `iv-qs`:

| Panel | Code space | Max codes | Body budget |
|---|---|---|---|
| 640x480 | 52 px | **0** | **nothing decodes at all** |
| 800x600 | 172 px | 4 | 400 bytes |
| 1024x768 | 340 px | 12 | 1200 bytes |
| 1280x800 | 372 px | 14 | 1400 bytes |

**The body is 765 bytes at only seven devices, so it already exceeds
capacity at 800x600 and is annihilated at 640x480.** A Z170 presenting
~25 functions lands past the 1024x768 budget too. And it is a cliff rather
than a slope: going from two rows of codes to three costs 159 px in one
step while the space available is fixed, so the outcome is either a full
record or a partial one worth nothing.

**Device count is the second term. Panel height is the first, and it is
unknown until we boot.** That is what settles the order: SceneProbe needs
no input, cannot be spoiled by a keyboard question, renders text and pixels
rather than codes so it works at ANY mode, and it **prints the `WxH` and
stride the firmware handed us.** It removes the unknown that decides
whether rung 2's record has any capacity, and it was already a scheduled
rung, so this costs no extra trip.

So: fly SceneProbe, read the panel geometry off the photograph, then fly
Inventory with a body sized for that mode -- split by stage if the mode is
short. **Deciding this on the dev box after rung 1 beats discovering it at
the machine**, because the overflow path yields a partial report the run
sheet forbids reading past, so discovery there costs the rung and you come
back anyway.

Credit where it is due: `iv-overflow` makes this loud rather than silent.
At 640x480 it computes zero rows that fit and prints `ONLY 0 DRAWN --
SPLIT THE IMAGE` in red, so an operator learns the record is absent before
leaving the machine instead of photographing codes that were never there.

**val asked for a known-good control image as rung 1. Declined, and this
table is why.** A separate `optiona-milestone.img` boot would tell us the
board can boot some image; the colour tells us where our own payload died,
from inside the real attempt, which is strictly more information for zero
trips. fester's cancellation of that boot is correct and it stands. If a
rung shows the unchanged firmware screen, section 3b is the diagnosis, not
another rung.

### Rung 1 -- `scene-probe.img`: does the display path work, is the channel order right, and WHAT MODE ARE WE IN?

**val's one ask, scheduled unconditionally.** No input, no ceremony, no
timeout, so none of the input questions can spoil it. It renders one frame
and halts. One photograph answers four things:

| Read | Verdict |
|---|---|
| Any recognisable 3D scene | The software pipeline runs on the ASUS panel. A6's core claim |
| **Cube BLUE, pyramid RED** | Channel order is right. **Inverted means the firmware is RGB and nothing reads `PixelFormat`** |
| The printed `WxH` and `stride` | The panel geometry the firmware reported. Photograph the digits |
| Left band and bottom strip stay wash colour | Content-pane containment holds at the real panel size |

**The colour row is the point and there is no substitute for it.**
**Red/blue order is still assumed everywhere**, and every other screen we have
is light text on a dark ground where a swap is invisible. The MECHANISM behind
that changed at B5.4 step 4 and the conclusion did not, which is worth stating
carefully because the migration note routed to red said this warning was now
stale. It is not. The old `option_a_stub.asm` did not read `PixelFormat` at all
(checked at main 12095). The surviving `cdx-to-pe.ps1` stub DOES read it -- `mov
eax, [rcx+0x0C]` then `mov [rdi+0x24], eax` -- so it is published to the handoff
block. **Nothing consumes it.** `GopHandoff.codex` exposes `boot-fb-base`, `-w`,
`-h` and `-stride` and has no pixel-format accessor; `handoff-stride` takes the
LOW half of the quadword at `+0x20` and the format sits unread in the high half.
So the value is now measured, carried across the handoff, and dropped, and the
photograph below is still the only thing that answers the question. This scene has a blue-dominant cube beside a red pyramid
precisely so the two candidate answers look different. If it comes back
inverted, the fix is one `mov` and a handoff cell in a file fester has
already had open.

**Do NOT spend a rung diagnosing a partial 3D view.** val measured the
desk's 3D view photographing as ground-plane-only and then as pure sky
under OVMF, ruled out three causes offline, and established it is a
capture artifact: `GopScene` paints straight into the live framebuffer
with no double buffer and the ground is node 0, so a screendump landing
mid-frame shows exactly that. Real hardware is far faster than TCG and it
may not appear at all. If it does, it is tearing, the fix is a back
buffer, and it is not a renderer fault.

### Rung 2 -- `inventory.img`: what are the parts, and what did our stack do with them?

**FLEW AND PASSED, 2026-07-29. This rung answered sitting questions 2, 3
and the HID half of 4, plus blu's N1/N2/N3 and reek's P2, in one boot.**

| Answer | Value | Consequence |
|---|---|---|
| **Q2, the NIC** | `00:1f.6` **`8086:15b8`**, an Intel **I219-V**, rev 31, subsystem `1043:8672`, `B0=df440000` | **e1000e family, so red's driver is the right one and Track B is unblocked.** This is the field blu's whole lane was blocked on |
| **Its `MAP=`** | **`ok`** | The register window lands inside the 3 GB to 4 GB device range, so `e1000-bar-verdict` accepts it and **B3 needs no page-table change.** The dangerous `BELOW3G` verdict did not fire on the part we drive |
| **Station address** | **`78:24:af:d9:c8:23`, `AV=1`** | Read live off RAL/RAH through the vendor-and-reachability gate this sheet required. blu's N3 |
| **A second NIC** | Realtek **`10ec:8168`** at `06:00.0`, behind a bridge, **`MAP=BELOW3G`** | Not the part to drive. Recorded because this sheet asked to hear about a non-Intel NIC at once, and because it is the one device on the board whose window WOULD alias the arena |
| **Q3, PS/2** | **THERE IS NO PS/2 ON THIS BOARD.** Zero arrivals before the handback and zero after | The keyboard is USB behind firmware i8042 emulation and that emulation does not survive ExitBootServices. val's "if no" branch, so **USB HID post-EBS is the only input path this machine has** |
| **Q4, HID** | `uk-ok=y slot=1 dci=3`, `intel-route=y` | Our stack addresses and configures a keyboard on real Intel xHCI silicon |
| **Q4, storage** | `disk=n`, and **this is NOT an answer** | See rung 3 |
| **Bus walk** | **21 devices over four buses** | The depth-3 bridge walk was load-bearing, not defensive. A GTX 970, an ASMedia SATA controller and a **second xHCI** were all found and none was previously recorded |

**Two rulings on this sheet were vindicated by the same photograph and one
was made irrelevant.** Vindicated: gating the station-address read on
vendor `0x8086` **and** `MAP=ok`, because a second NIC really was present
and reading RAL/RAH off a Realtek would have yielded garbage shaped like a
MAC; and putting PS/2 last, because it turned out to be the stage with
nothing to give. Irrelevant: the QR split condition, which rung 1 answered
by reporting a 1920x1080 panel with roughly 650 px of code space.

> **HOLD CLEARED at main 12142.** The rung is ready. `iv-codes` used to run
> before the PS/2 stage with literal zeros and nothing re-rendered, so the
> record went home reading `PS2 irq1=0 poll60=0 last=00` whatever the board
> did. Fixed with a real 1620-tick window read off `xhci-tick-cell`, one
> re-render with the final counts, then endless and glass-only. The glass
> counts down (`codes refresh in Ns`) and then says `CODES REFRESHED WITH
> THESE COUNTS -- photograph them now`, so **the operator can tell whether
> the record is final. Do not photograph the codes before that line
> appears.**
>
> fester gated it on the MECHANISM rather than on the symptom, which is what
> this defect required: an ablation build with the window at 36 ticks and the
> counters seeded 7/3/5a, run WITHOUT `-NoPs2`. The decoded body came back
> `irq1=8 poll60=3 last=fa`, so a non-zero proves the re-render fired and
> irq1 moving 7 to 8 proves the re-rendered body carries LIVE counts rather
> than the seed. Three states told apart by one control. Both dev-box asks
> are also in: `top` is derived from the last painted row, and the caption
> states the body against the mode's budget.
>
> Flying digest `0DC6C755...00BE3`. **`FADB5DD2...9505` from CL 12115 is
> superseded and must not fly.**

**fester builds this and it is the one new payload for attempt 2.** It
replaces the old boot 1 (`xhci-probe.img`) and the unrecorded
`pci-probe.img` from attempt 1 with a single non-halting image that walks
the machine and paints each answer as it gets it, so the last thing on
screen is where it stopped.

**Required contents, in this stage order:**

| Stage | Carries | Answers, and for whom |
|---|---|---|
| **A. PCI** | `PciProbe`'s three call-outs (NIC class 02, STORAGE class 01, USB 0c.03), each with vendor:device, revision, subsystem, interrupt line, BARs 0/1/5 and the `MAP=` verdict. Buses walked to depth 3 behind every bridge | Sitting Q2. **blu N1** (the class-2 vendor:device, BLOCKING all of B3 and B4) and **blu N2** (its `MAP=` verdict, BLOCKING B3) |
| **A2. Station address** | Gated on the class-2 device's vendor being `0x8086` **and** its `MAP=` being `ok`: call `e1000-read-mac (mmio)` and `e1000-mac-present (mmio)` on its BAR0 and print the six bytes plus the AV bit | **blu N3.** Approved. Both functions are pure and already exist in `codex/os/kernel/E1000e.codex`. **Gate it on the vendor ID or omit it** -- RAL/RAH at those offsets are Intel-specific and reading them off a Realtek yields garbage that looks like a MAC |
| **B. USB** | `XhciTruthProbe`'s body: controller vendor:device, caplen, HCCPARAMS1, ownership handoff, **`verdict=` / `judged=` (both dwords) / `op=`**, slots/ports/reset/cnr/run/connected, Intel routing, PORTSC per port, and `ENUMERATED kbd= mouse= disk=` | Sitting Q4. **reek P2** (the BAR verdict line, free, it is already on this screen). `disk=` is the gate on rung 3 |
| **C. PS/2, LAST** | Post-ExitBootServices PS/2 arrival counters on both routes: the IRQ1 mailbox and a floating-bus-guarded port 0x60 poll, plus `last=`, the last byte seen | Sitting Q3, the one question OVMF cannot pre-answer |

**READING STAGE C, and this is the part to get right, because a counter
climbing is NOT the answer.** fester observed `irq1=1 last=fa` under OVMF
with i8042 present and correctly refused to call it a keystroke: **0xFA is
the controller's ACK.** The counters increment on any byte the controller
puts in the output buffer, so controller chatter alone makes them non-zero.
The discriminator is the `last=` byte:

| `last=` | What it is |
|---|---|
| `fa` | ACK. Controller chatter, **not a keystroke** |
| `aa` | Self-test passed. Chatter |
| `fe` / `ee` | Resend / echo. Chatter |
| `01`..`58` | **A set-1 make code. This is a keystroke and this is the answer** |
| `81`..`d8` | A break code, the release of a real key. Also the answer |

**So the operator instruction is: hold a KNOWN key and check `last=` is that
key's scancode.** Hold `A` and expect `1e`; hold `Esc` and expect `01`.
Press two different keys and watch the value change. A non-zero `irq1` with
`last=fa` and nothing else means PS/2 is wired but delivering no keys, which
is a DIFFERENT answer from PS/2 being live, and it is the answer val's A3
scope turns on. This is the operand-pair rule applied to a byte: pick the
check whose two candidate answers look different.

**PS/2 goes last and that is deliberate, not fester's original order.** It
is the only stage that hands the controller back to firmware and waits a
bounded time, so it is the stage most likely to end the run. Last means a
failure there costs nothing already gathered. It is also the only stage
with no dev-box bed at all: reek reproduced PS/2 post-EBS delivering
nothing under OVMF on q35 in a machine with no xHCI, so the emulator
cannot distinguish a QEMU i8042 quirk from our re-enable, and PS/2 is
recorded METAL on this board.

**Screen budget, and this is a real constraint on the build.** Put only
the compact summary on the glass: the three call-outs, the xHCI verdict
and enumeration lines, and the PS/2 counters. **Do not paint the full PCI
device list.** `PciProbe` already warns `devices=N ONLY M FIT ON SCREEN`
in red, and a Z170 board presents far more than six devices; on its first
real gate it said `devices=6 listed=4` while silently dropping two.

**The QR codes are the record and the screen is the convenience**, so the
full body goes in the codes. **Hard constraint: if the combined body
pushes the chosen QR scale below 3, do not ship the combined image --
split it back into two.** Scale 2 is the failure that looks like success:
the decoder finds the finder patterns and reads none of the codes. Print
the chosen scale on the glass so the operator knows to shoot close and
square at 3.

| What you see | What it means |
|---|---|
| A painted report | The payload boots and GOP works. The ladder can continue |
| `devices=0` | The config-space accessors answered nothing. A probe fault, not a bare machine |
| A device in the list but missing from its call-out | Its class code is not what was expected. **Take the vendor:device off the list** -- that is the answer that was wanted |
| NIC call-out says `NONE FOUND ON ANY BUS` in red | Record it and say so immediately. Track B has no card and B3's shape changes |
| `MAP=BELOW3G` on the NIC | **The dangerous verdict, and it reads like the milder one.** Mapped as ordinary RAM inside the arena `alloc-bytes` hands out. B3 needs a page-table change before anything else |
| `found=n` on the USB stage | No xHCI on the bus, an EHCI-only board. The USB rungs will not mean what they normally mean |
| `connected=0` with devices plugged in | Below enumeration: port power or chipset routing. Read the Intel routing line and PPC |

**What must be written down the same day, before anything else:** the
class-2 device's vendor:device and its `MAP=`, into `CurrentPlan.md`.
blu's entire lane is blocked on those two fields and nothing else on this
sheet outranks them.

### Rung 3 -- `msc-align.img`: DROPPED 2026-07-29, AND THE GATE THAT DROPPED IT WAS WRONG

**This rung did not fly because rung 2 reported `disk=n`, and `disk=n` was
a false negative.** All four devices on the Intel xHCI came back Full or
Low speed, so none of them was the boot stick: the stick is on the
**second** xHCI, the ASMedia `1b21:1242` that rung 2 discovered on the same
screen. `xhci-connect` takes the FIRST xHCI it finds and stops. So the
storage half of sitting question 4 is **UNANSWERED rather than negative**,
and A4's last open code question is still open.

**The gate condition on this rung is mine and it is the one defect in this
ladder.** I keyed a rung on a field without asking what the instrument does
when the answer is elsewhere, and the failure mode is the one this project
keeps meeting: a probe that cannot report "I did not look there" reports
"it is not there". It is the same shape as scanning bus 0 only and printing
`NONE FOUND ON ANY BUS`, which this sheet had already caught once in blu's
scan and once in fester's, and I wrote the third instance into the ladder
anyway. `disk=n` had exactly one candidate reading where it should have had
two.

**What it costs: this rung needs the board again**, which is the only item
on the whole sitting that does. Do not schedule it until
`xhci-connect` enumerates every controller rather than the first, because
until then the probe reads a controller with no disk on it and returns
`disk=n` a second time.

**THE BLOCKER IS CLEARED: reek landed the multi-controller walk, gated against
a two-xHCI OVMF bed built to the ASUS topology**, keyboard on controller 1 and
stick on controller 2, with a calibration arm capped to one host that
reproduces rung 2's `disk=n` exactly. A controller nobody opened now reads
`NEVER-OPENED` in amber rather than as a blank or as "no disk", which is the
defect this rung's gate condition was made of.

**AND THIS RUNG NOW CARRIES A SECOND PAYLOAD, so the trip answers two things.**
reek's Full-speed HID lead came back needing a READING rather than a fix: the
interval-encoding hypothesis is dead (measured, both speed classes are
spec-correct), and the four surviving candidates all turn on fields **nobody has
ever read off that board.** So the trip carries:

| Payload | Brings home |
|---|---|
| `msc-align.img` | reek's P1, the 64 KB crossing-TRB question, and `sectors=` as a free geometry cross-check |
| an endpoint-descriptor reading | The keyboard's own `bInterval` and `wMaxPacketSize`, the Interval we programmed, MaxESITPayload, the route string, and whether a transaction translator was named |

**The second one must print what the device ASKED FOR beside what we
PROGRAMMED**, so each row is a comparison rather than a value. A value alone is
satisfied by any plausible number; a mismatch is the finding. And, from this
sheet's own `disk=n` defect: **make "we did not read it" look different from
"it is zero".** A `bInterval` of 0 because the descriptor fetch failed and a
`bInterval` genuinely 0 must not photograph the same.

Both payloads need no input and neither waits, so the pair still costs one boot
and two photographs. **reek built and calibrated the endpoint reading (main
12236), so rung 3 is ready to fly on both counts.**

**ONE CAVEAT ON `KBDDIAG.TXT`, and it is about a result rather than about the
boot.** reek found `xhci-diag` aliasing the runtime cell band, and one of the
cells a USB bring-up overwrites is `fs-elevated`, the token the filesystem
syscall is gated on. `KbdDiagProbe` writes its file **after** a bring-up has set
that cell non-zero. So **a successful `KBDDIAG.TXT` may be evidence of that
defect rather than of the write path**, which is the one piece of WRITE evidence
in the whole sitting. Bring the file home as section 6 says, and do not book P3
as proven until the cell collision is untangled. **Rung 3 itself is not blocked
and nothing already photographed is in doubt** -- the corruption runs
diag-to-runtime, and every diag payload is single-core and takes no core ids.

**CAVEAT LIFTED 2026-07-30, by measurement rather than by argument.**
`xhci-diag` moved to 118784 (red, main 12283), so a bring-up no longer writes
`fs-elevated` at all -- verified on this bed, all eight runtime cells
unchanged across a bring-up that demonstrably ran. `KbdDiagProbe` was then
rebuilt on the moved base and booted under OVMF with the medium on USB
(`-UsbDisk -UsbKbd -NoPs2`): `disk=y mount=y`, phase 3/3, `SCANS=18`,
**`FILE-WRITES=12`**, and `KBDDIAG.TXT` is present in the booted image. So the
write path does not depend on the stray elevation, and P3 can be booked as
write evidence. The source says the same thing and is worth stating because it
changes what the defect WAS: the servicer sets and clears `fs-elevated` around
its own span, so the bring-up's write never enabled a write that would
otherwise fail -- it left the block-syscall bypass window permanently open,
which is a worse defect than the one feared and is now closed. The first arm of
this run is the calibration: the same probe with the medium on IDE stops at
phase 1/3 with `disk=n mount=n FILE-WRITES=0` and no file, so the check can
say no.

**reek's P1. Fly it only if rung 2's USB stage enumerated the disk**, and
drop it without hesitation if the trip budget is short: reek states it is
droppable and affects nothing else on the ladder. No input, waits for
nothing, ends in a halt loop that keeps painting, so it survives a board
whose keyboard is dead. It reads the boot stick itself, so there is
nothing to plug in.

If rung 2 says `disk=n`, every row in this probe is meaningless and the
boot must not be spent.

**Frame all five lines or the boot is wasted.** The bottom row is
`LIVENESS out-of-range lba=100000 ok=n` and it is the instrument's own
calibration: **it must read `n`.** If it reads `y` the probe can only say
yes and the two rows above it prove nothing, exactly as QEMU's silent
stderr proved nothing.

| Read | Verdict |
|---|---|
| `ALIGNED` and `CROSSING` both `ok=y` with equal checksums | A bulk TRB may cross a 64 KB boundary on this silicon. **A4's last open code question closes** |
| `CROSSING` fails, or its checksum differs | A real defect on the seed read path. The fix is a TRB split at the boundary and reek needs the spec or a second controller to write it safely |
| `LIVENESS` reads `ok=y` | The probe is not calibrated. Discard the other two rows |
| `sectors=` | Free cross-check. On the real stick it should read 60506112, which checks the `-SpecFit` geometry against what our own stack reads back |

### Rung 4 -- `kbd-probe.img`: FLEW 2026-07-29, and it produced the sitting's best lead

Rung 2 reported no PS/2, which is the condition this rung is written to fly
on, so it flew. **USB HID enumerates and delivers nothing:** `EPINT=0`,
`SCANS=0`, `REPORT` all zeros across all three phases with a key held down,
against a controller our stack had just addressed and configured.

**The instrument was checked rather than assumed, and that is what makes
the zero worth anything.** The same image under OVMF with keys injected
returns `EPINT=12 SCANS=12 last=a0 FILE-WRITES=7`, so the path can report
arrivals and the zero on metal is real rather than a blind probe. That
control is the difference between a finding and a shrug, and it is the rule
this sheet applies to the `LIVENESS` row at rung 3 and the `last=fa` row at
rung 2: **pick the check whose two candidate answers look different.**

**The lead is SPEED, and it was in a field nobody was watching.**
`speed=3` (High-speed) under QEMU against **`speed=1` (Full-speed)** on the
real keyboard. Every test this path has ever passed was against a
High-speed device. xHCI encodes interrupt-endpoint intervals differently by
speed, so a driver computing the High-speed way for a Full-speed endpoint
programs a nonsensical polling rate, and this is the symptom that produces.
**The speed difference is measured; the interval encoding is a
hypothesis.** It is testable on the dev box with a Full-speed bed and needs
no further hardware time, which is the right place for it.

Three timed phases in one boot (~90s, ~45s, then forever). **Hold a key
during each phase.** Findings render as QR codes and are also written to
`KBDDIAG.TXT` on the stick's own ESP.

**Why it is conditional and where it sits.** Rung 2 stage C already
answers sitting Q3, so this rung is no longer the place that question is
settled. Fly it when rung 2 says PS/2 delivers nothing, because then USB
HID post-EBS is the critical input path and these three phases are what
characterise it; skip it if the trip budget is spent and rung 2 answered
Q3 cleanly.

**It carries reek's P3 regardless, and that is its other reason to
exist:** `KBDDIAG.TXT` on the stick's ESP is the **only** evidence in the
whole sitting of a WRITE through the USB stack on real hardware. Mount the
stick at the dev box and bring the file home even if the screen was
uninformative.

| Phase | Question | Read |
|---|---|---|
| 1 | Does the interrupt endpoint ever deliver? | `EPINT` climbing is the verdict number |
| 2 | Does the firmware revive PS/2 when we hand the controller back? | `PS2` climbing means the fallback is real (R-3). `reclaim=y` means the BIOS re-took ownership |
| 3 | Can ownership be juggled per phase? | `reacq kbd/disk/mount` all `y` is the strongest result |

Note from reek, measured: under OVMF `legsup=n`, because QEMU's xHCI
exposes no USB Legacy Support capability, so `xhci-take-ownership` writes
nothing and phase 2 has nothing to hand back. **OVMF cannot be the bed for
that experiment. This rung is the only place it can be run.**

**Photograph the codes at the end of each phase.** Fill the frame, shoot
straight, kill the glare. Then at the dev box:

```powershell
pwsh tools/qr-read.ps1 -Path <photo>.jpg -Save phase1.txt
```

### Not on the attempt-2 ladder

**`seed/Codex.img` (the old boot 3, R6).** Still blocked, and there are now
**two** reasons rather than one.

**PARTLY ADDRESSED at main 12125, and the rung is still blocked.** fester
added five progress marks to `cdx-to-pe.ps1`'s stub, and corrected this
document's claim while doing it: the gap was never "no signal", it was
"bytes on the AllocatePages failure paths only", so the img could report a
named allocation failure and nothing else. The marks go to **both UARTs**.
**The sitting's telemetry rig is a phone camera on a board with no serial
port** (section 2), so a human at the machine still cannot tell "booted,
invisible" from "dead". Good for the local bed and for `-uefi-strict`; not
the condition. **Boot 3 unblocks on a channel a CAMERA can read** -- ConOut
text on the real screen, or GOP text out of that stub -- not on more marks.

**RESOLVED at B5.4. The half of item 2b that landed is the marks, and this
paragraph is what it closed.** The original finding, kept because the mechanism
is why the marks went to serial rather than to the glass, read: there are two
stubs and only one can paint -- `option_a_stub.asm` carries both marks and every
diag probe goes through it, while `seed/Codex.img` is built through
`cdx-to-pe.ps1`, whose stub never calls `LocateProtocol(GOP)` or paints anything
at all; so the colour table at the head of section 4 does not apply to this rung,
and combined with the ConOut gap it would return a black screen worth zero bits.

**Every clause of that is now false, and the shipping path has one stub.**
`option_a_stub.asm` and its ml64 builder `build-option-a-legacy.ps1` were
**deleted 2026-08-03**. They had been kept as the reference for the ASUS display
defect; that defect is closed (the ConOut re-mode, cured in `cdx-to-pe.ps1` and
gated by `build/boot/test-conout-remode.ps1`), and the legacy stub rendered on
that panel only because it never called ConOut at all.
`cdx-to-pe.ps1`'s stub acquires GOP and paints
both colours; `seed/Codex.img` is the GOP payload and paints under OVMF, which
fester measured and red confirmed in the stub source independently. **So the
colour table DOES apply to this rung**, and boot 3 is no longer held off the
ladder by the black-screen risk.

**The other half of item 2b, ConOut, was never routed and is OPEN.** The dev
console still does not paint on real firmware. Boot 3 unblocks on a channel a
camera can read; the colour marks are now that channel for the GOP payload, and
the dev console is a separate blocker below.

The original block, unchanged: the dev console runs on real UEFI and
paints, but its output goes to COM1 and there is **no ConOut path**, so on
real firmware the screen stays black. A human sitting down today would see an unlit screen
and could not tell it from a dead machine. The rung would spend them on a
known failure. Remove this only when the console reaches its menu under
OVMF, then re-run the ladder end to end before anyone sits down. Details
in the boot 3 block below, which is retained deliberately.

**A5, the compiler running on the box.** Not a sitting-2 ask, on reek's
own instruction. It needs a working console and keyboard, so it sits
behind boot 3, which is blocked. Do not budget a compile-on-metal rung.
If boot 3 unexpectedly becomes available, ask reek then and they will
write the scripted sequence against what the box actually did.

**A separate control image.** Declined; see the liveness colour table
above.

**Any link-up test, ping or traffic.** blu ruled these out themselves:
they need the driver bound and running, which needs N1 and N2 to come back
favourable first, so a result now could not be interpreted.

### Boot 3 -- `seed/Codex.img`: does the stick boot? (this is R6)

> **DO NOT RUN THIS RUNG YET, and do not flash the console stick.**
> The reason changed on 2026-07-29 and the rung is still not ready.
>
> **The reboot loop is fixed.** It was two defects in
> `build/cdx-to-pe.ps1`: `AllocatePages` asked for a fixed `0x1000000`
> that edk2 refuses and discarded both statuses, and the stub never
> filled `ram-size-addr`, the cell every panic handler relocates its
> stack to -- so the guest's own out-of-memory handler set RSP to 0 and
> triple-faulted instead of reporting. Measured under OVMF: 21 triple
> faults in 40 seconds before, **0 after**.
>
> **The console now RUNS on real UEFI firmware.** Four stub defects are
> fixed; it boots once, indexes, and paints a live header with a clock:
> `Indexed 95 definitions, 0 disk facts`.
>
> **What still blocks this rung: that output goes to COM1 and the screen
> stays black.** `uefi-con-put-text` lowers to `__serial_put`, which is
> codex-vm's blit cell or a COM1 `out`; there is **no ConOut path**, so on
> real firmware nothing reaches the UEFI text buffer. codex-vm draws the
> console itself, which is exactly why this never showed there. A human
> sitting down today would see an unlit screen and could not tell it from
> a dead machine, so the rung would still spend them on a known failure.
>
> Two earlier stories in this block were wrong and are recorded so nobody
> revives them. A `#GP` here was real and is fixed (SYSCALL was handed
> selectors the firmware GDT defines backwards). An `OUT OF MEMORY` was a
> **false report** from a clobbered deck-pointer register, with the heap
> untouched. Neither is a live defect.
>
> Two things this bought, and they are worth having before the sitting:
> the failure is now *diagnosable* rather than silent, and it is
> reproducible locally with `codex-vm -uefi-strict` without QEMU. Attach
> both serial ports when you run it -- the panic printer moved from COM2
> to COM1 at main 11837, and one attached port is how this stayed
> invisible.
>
> Boots 1 and 2 are unaffected and still worth doing on their own.
> Remove this block when the console reaches its menu under OVMF, and
> re-run the ladder end to end before anyone sits down.

The release question. Success is self-evident and needs no instrument:
the dev console comes up and you can drive it.

| What you see | Verdict |
|---|---|
| The dev console menu, navigable | **R6 passes.** Photograph it. Walk the menu, then stop |
| `Indexed 0 defs, 0 chapters` | Boots, but source loading failed -- `LocateProtocol` found the wrong Block I/O instance. R6 still passes; record it as a known defect, it is not a boot failure |
| Black screen or hang | **R6 fails.** Boots 1 and 2 are what diagnose it, which is why they came first |
| Firmware does not list the stick | Re-flash with `-SpecFit`, then the second stick. Not a boot failure until both are tried |

**Know this limitation going in:** the dev console has **no QR channel**.
It cites neither `GopQr` nor `GopDraw`, and its only output is UEFI text.
If boot 3 goes dark it cannot tell you why, and the evidence you will
have is whatever boots 1 and 2 already told you about this board. That is
the reason for the ladder, and the reason not to skip a rung that looks
boring.

---

## 5. Abort conditions

Stop the sitting, do not improvise, and bring what you have home:

- **The FIRST rung flown shows solid dark blue and nothing more:** the PE stub's
  `AllocatePages` is the leading suspect and this board may be out of
  memory below 1 MB. Nothing further on this machine is informative
- **Two different sticks both fail to appear in the boot menu**, after
  section 3b's manual boot option has been added: the problem is the
  firmware's USB path, not our image
- **The first rung never paints and section 3b was skipped:** go back and do 3b
  rather than spending a second rung
- Any rung needs a decision that is not on this sheet

**Flash, verify, PULL.** Do not eject; see section 3 for the mechanism and
for the three claims this paragraph used to make that were falsified on
2026-07-29. The short form: the GUID theory is dead, `-SpecFit` was never
what stopped the rewrite, and a conforming stick now survives reinsertion
unchanged. What is left of the rule is the useful half, and it is the half
attempt 1 broke three times: **the eject is the operation that corrupts the
table**, so end at the flasher's last line and walk to the board. If the
flasher reports a verify failure, the stick is the problem -- take the
second one.

---

## 6. What comes home

One folder per rung:

- every photograph, unedited, including the boring ones
- the decoded reports (`-Save`), one file per photograph
- `KBDDIAG.TXT` from the stick, if rung 4 flew. **This is the only WRITE
  evidence in the sitting** (reek P3)
- the four SHA-256 digests from step 1
- the firmware's own identification (vendor, version) off the setup screen

The digests are what tie the photographs to a commit. Without them a
report is an anecdote.

### Written up the same day, before anything else

These are obligations, not suggestions, and each has a lane blocked
behind it.

| Into | What | Blocks |
|---|---|---|
| `CurrentPlan.md` | The class-2 device's **vendor:device** | **All of blu's B3 and B4.** It decides whether red's e1000e driver drives this board at all. If it is not `0x8086`, say so immediately: different driver, different model, different estimate |
| `CurrentPlan.md` | The class-2 device's **`MAP=` verdict** | **B3.** `BELOW3G` means B3 needs a page-table change before anything else |
| `CurrentPlan.md` | Sitting Q3: **PS/2 present, and live post-EBS?** | **val's A3 scope, both branches.** This single answer moves a whole workstream in or out |
| `CurrentPlan.md` | Sitting Q4: `ENUMERATED disk=` | **reek's A4**, and it is the gate on rung 3 |
| `CurrentPlan.md` | Rung 1's channel-order verdict | **val's A6.** An inverted result is one `mov` in the stub |
| `red-workplan.md` | Which rungs flew, which were dropped, and why | The next attempt's ladder |

**A partial QR report is the one thing not to read past.** The decoder
says `WARNING: n of m chunks` when a code is missing, and a truncated
report drops the LEADING lines, which are the verdict fields. Re-shoot.
Do not reason from a short report.
