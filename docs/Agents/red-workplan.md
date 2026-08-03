# red -- workplan

*Open work items only. Per-CL history is in Perforce, process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, priority order is in
`docs/PM/CurrentPlan.md`. If nothing is open, this file says so and stops.*

## HEAD ITEM: fly `kbd-diag-v16.img` -- the victory lap. v15 PROVED THE KEYBOARD DELIVERS on the ASUS (EPINT=97 idle heartbeats, key data visibly reaching the QR bodies; GET_IDLE=125 confirms SET_IDLE(0) was the killer). v15's own SCHEDX then stopped the live endpoint at 45 s and the Intel does not resume periodic delivery on a doorbell-restart after Stop -- v16 gates the experiments on a dead pipe so a working keyboard is never touched, and clears the QR panel before re-render. Success screen: EPINT past 90 s, SCANS climbing with a key held, phases 1-2-3. Open observations from v15 (not keyboard-blocking): PIT tick stalls ~82 s on metal (paint-count fallback covers phases); Intel no-resume-after-Stop recorded for future driver stop paths.

## Superseded: v15 head item

**Everything between the v14 flight and v15 happened on the desk:**

1. **The metal freeze (p=2428) was the probe exhausting the heap** --
   unbounded per-paint allocation since v1, rule 8's exact red-flag
   pattern; the OOM handler literally printed `OUT OF MEMORY` in a
   `-hid-nak` bed run, and the #UD deaths were the same exhaustion
   tripping the QR encoder's bounds trap. Fixed with
   `__heap-save`/`__heap-restore` brackets on both phase loops:
   13,000+ paints where 2,400 used to die. (My earlier "cells 132-140
   clobber a tenant" bisect was noise on the OOM cliff edge; the cell
   relocation was kept anyway since the 37000-band boundary is real
   but unnamed.)
2. **The silent-keyboard fix candidate, spec-derived: stop sending
   SET_IDLE.** HID 1.11 F.3 gives a boot keyboard the every-poll
   default; our duration-0 SET_IDLE is the one request that turns
   that off, the firmware path never sends it, and a quirky device
   may honor it as never-report -- the measured ASUS shape exactly.
   Proof pair under the new `-hid-idle-quirk` arm: v14 EPINT=0,
   v15 EPINT=1,100,004 in 30 s.
3. **DEVX**: GET_CONFIGURATION / GET_PROTOCOL / GET_IDLE /
   GET_REPORT x2 over EP0 at pump time (HID 7.2, GET_REPORT is
   mandatory). If the interrupt pipe stays dead on metal, a nonzero
   `R2:` byte is the on-metal existence proof for a GET_REPORT-polling
   keyboard path in GopUsbKbd -- the keyboard works either way.

Reading table in `HardwareSitting.md` "NEXT BOOT". USB2 + HID specs in
`docs/Reference/` with Grep text; derivations cited in
`xHCI_ServiceModel_Notes.md`.

## Superseded head item (v14 flew; SCHEDX reading table lives in HardwareSitting)

**v13 flew 2026-08-03: dq parked at ring base, re=3.** The controller's
own writeback says its scheduler never touched the ring while command
ring, event ring and endpoint commands all worked mid-pump. re=3 is NOT
a refused restart (xHCI 4.8.3 p.165: the Running write-back is forced
only before the next transfer event; with none it lawfully reads 3
forever) -- the v13 run sheet's contrary row was wrong, mine.

**Damian's process finding stands and changed how this lane works: the
vm model and driver were built without the spec ever being in the tree,
and every bed/metal gap traces to exactly that.** Now on disk:
`docs/Reference/xHCI_Specification.pdf` (rev 1.2) + `.txt` for Grep +
`docs/Reference/xHCI_ServiceModel_Notes.md` (the derivation, with page
numbers, of everything below). Reading it found four vm violations in
one day: no mandatory Stopped Transfer Event on Stop Endpoint (4.6.9
p.134), Stop accepted from Halted/Error (4.8.3 p.164), Transfer Events
carrying Endpoint ID 0 (6.4.2.1 -- the bed EPINT row counted real
completions as OTH since v10), no MFINDEX (5.5.1). All four fixed; a
fifth find was a WHP robustness gap (empty InstructionBytes on
memory-access exits) that only full-rate event delivery could expose.
**Rule: a bed arm is written FROM a cited spec section or not at all.**

**Open items, in order:**

1. **Fly v14** (v13 + SCHEDX: PORTSC/PLS at pump time, MFINDEX sampled
   twice, Stop #1 with the mandatory-FSE code captured, restart, 220 ms,
   Stop #2 with dq2). Reading table in `HardwareSitting.md` "NEXT BOOT".
   Also still owed from the v13 photo: the `LATCH=` and `code=` values
   after STOPX appeared -- they carry whether the ASUS xHC emitted the
   mandatory Stopped Transfer Event, at zero flight cost.
2. **The periodic schedule on Intel silicon** is the whole remaining
   keyboard question. Bed-certified NOT the cause: interval encoding,
   CSZ/64-byte contexts, scratchpad, event-ring wrap, event-ring full, BAR,
   routing, PSI, TT, and now context parameters incl. Average TRB Length
   (checked against 4.14.1.1 by inspection). SCHEDX's verdict splits the
   remainder: pls!=0 = port starved; mf=+0 = no frames at all; dq2=dq1
   with f1 present = schedule never issues the IN; dq2 advanced = one-shot
   doorbell loss, re-ring is the workaround candidate.
3. **NAMED DEFECT: an MMIO read (or EP0 transfer) from the probe's repaint
   chain kills all later painting** while the pump keeps counting -- three
   bisected builds under codex-vm -uefi; the no-instrument build renders
   completely every time. Mechanism unnamed. Until then the probe paints
   RAM-resident state only.
4. **NAMED DEFECT: MSC attach fails on the real ASMedia** (`disk=n
   mount=n`, no KBDDIAG.TXT, verified on the stick at the box) while the
   same image passes under OVMF. The write-evidence rung stays open behind
   it.
5. **Stub SetMode-to-native** after console activation, so the panel runs
   1920x1080 instead of the console's stretched 1024x768. Needs a
   codex-vm QueryMode mode-list extension to bed it.

**The display corruption was ONE fault, not three, and it is not in any
drawing code.** The cdx-to-pe stub read GOP geometry BEFORE its first ConOut
call; AMI's GraphicsConsole activation re-modes the GPU on that call, so
every 2026-08-02 image painted 1920x1080/2048 into a scanout that had
changed. Alternate black lines, stretched glyphs, wordwrap overlap, the edge
band, and StrideProbe's EXACT eight aliased copies (4096/gcd(7680,4096) = 8)
all fall out of the pitch arithmetic. Reproduced in codex-vm under the new
`-uefi-conout-remode` flag and cured by clearing first and reading after:
same payload bytes, clean at both geometries. The 07-29/31 boots were
legible because the deleted asm stub never called ConOut.

**Two prior conclusions of mine are corrected by this:**
- "Stride 2048 is the correct pitch" was over-read. The cyan bar stepped by
  2x the true pitch is also vertical, just dashed, and a photograph cannot
  tell 50 per cent duty from solid. The eight yellow copies were the real
  signal and they say pitch 4096 bytes at that moment (L-GAP, L-ORACLE).
- The 12612 respacing fixed a non-defect: v10's 20 px rows do not overlap at
  sc=2 (glyph ink is ~8 of 16 font rows). Shelf 12612 is DELETED; StrideProbe
  itself landed. The overlap on metal was re-mode interleave.

**"The keyboard is solved" needs its honest restatement: ConIn works because
boot services were left alive, and with them the FIRMWARE'S xHCI driver.**
The cdx-to-pe stub never called ExitBootServices (the deleted asm stub always
did), so every ConIn-era diag boot measured two drivers fighting over the
Intel controller. ConIn remains the right input path for payloads that keep
firmware (KeyProof, dev console). For driver truth the stub now takes
`-ExitBootServices` (`build-option-a.ps1 -Ebs`), and kbd-diag-v11 uses it:
first single-driver Intel measurement since the stub migration, with reek's
per-controller rows finally showing the Intel's own registers, plus a new
`EPCTX est= dq=` row reading the controller-owned endpoint state (xHCI
6.2.3) that names WHICH silence the interrupt endpoint is in.

**Bed-certified before spending the body:** codex-vm gained `-xhci-csz`
(64-byte contexts, Intel-style) and `-xhci-scratch N` (mandatory scratchpad,
refusal arm shown); the driver passes both, separately and combined, so the
two never-tested-anywhere Intel paths are off the suspect list unless metal
contradicts. All six xHCI regression tests pass with the flags off. OVMF
boots the exact image file: paints, EBS green under edk2, `disk=y mount=y`
through our own stack post-EBS.

## 2026-08-02: THE KEYBOARD IS SOLVED AND PROVEN ON THE BOARD. main 12609.

Three stacked defects, none of them xHCI. `poll-key` read only the PS/2 cell
(port 0x60; that board has no PS/2 port). `uefi-read-key-ex` folded
`EFI_SHIFT_STATE_VALID` onto bit 63, so success was indistinguishable from its
own `-1`. And it located `EFI_SIMPLE_TEXT_INPUT_EX`, which is **UEFI 2.x
OPTIONAL** -- OVMF implements it, AMI Aptio V does not publish it. Now reads
**ConIn at SystemTable+48**, UEFI 1.x and mandatory. Damian confirmed green.

Account: `docs/PM/Active/Stories/TheKeyboardWasNeverSilent.md`. New lessons
L-SUCCESS, L-UNCALLED, L-OPTIONAL, L-STATES.

**`SCANS` / `REPORT` / `last` are still zero and that is EXPECTED** -- they are
fed by the xHCI driver, which still delivers nothing. `KbdDiagProbe` never calls
ConIn. **OWED: add a ConIn row to that probe** so the path that works is
visible; Damian asked to see scan codes and was shown the broken path's zeros.

**for fleet: a green emulator arm proves nothing when the thing under test is an
OPTIONAL part of a spec.** Every other instrument gap this project has recorded
was a bed unable to express a failure. This one was the reverse: the bed was
MORE CAPABLE than the target. Prefer the mandatory protocol; when you cannot,
the emulator cannot answer the question. L-OPTIONAL.

**for fleet: give a probe distinguishable FAILURE states, not pass/fail.** One
metal boot of a six-colour probe eliminated three whole classes at once, because
"not yellow" and "not magenta" each carried information. A pass/fail probe would
have said only "still broken". L-STATES.

**for fester: rung 3 was never blocked and I was wrong to say it was.**
`scene-probe` and `msc-align` paint fine under real firmware through BOTH stubs.
The failures I reported were codex-vm only, and `test-ovmf.ps1`'s own header
calls codex-vm "a lenient fake".

## 2026-08-02: release step 3 GREEN. `-Jobs 8` is now the standard everywhere.

**Poison build PASSES.** `-Tier all` against a 0xCD-fill seed
(`6DCCECB8C1C5DF80`, 2,714,164 B): **1403 total, 1359 pass, 0 fail, 44 skip, 0
newly red**, all three oracles pass, exit 0. The kernel was restored afterwards
and verified back to `6671C19A` -- the poison seed HAD been left installed as
`build-output/bare-metal/Codex.cdx`, so that trap is real and fires every time.

**RULED by Damian 2026-08-02: run everything at `-Jobs 8`, release proofs
included.** The `-Jobs 3` / `-Jobs 4` literals in the release recipes were the
dead XMP workaround (`ExaminersAssay.md` "The parallelism default"): the cause
was a DDR5 profile the box was not stable at, fixed 2026-07-22, and the harness
default went back to 8 while every recipe that had copied the number out kept
the halved one. **It cost 977 s of compile phase on a 12-core box in the run
above**, because I followed the recipe. Changed in `OperatorsManual.md`, the
`release` skill, `sweep-app-classes.ps1` (default 6 -> 8), `README.md`,
`CLAUDE.md` and here.

**Everything below this section that says `-Jobs 3` or `-Jobs 4` is history and
is wrong as instruction.** Left in place because it is the record of what was
run; do not copy a number out of it.

## Resting state, 2026-07-29 (B2 first cut landed)

**NO RED GATE. Nothing opened, pending or shelved. No stray VMs.
Everything this lane did is on main.**

| | |
|---|---|
| depot seed | `6671C19A0F78F630`, UNCHANGED -- nothing this lane landed today carries a seed |
| last gates | GREEN twice, hard fixed point in one pass, 213.4s then 207.8s, `constants.hash` 268 unchanged |
| battery | **NOT RUN** -- Damian's tool, never an agent's initiative |

Landed by this lane today, after the fleet re-cut: the two rulings
recorded; `codex/os/kernel/E1000e.codex` with `codex/test/e1000-match`;
the BAR window verdict and blu's raw-integer poll entry;
`docs/Designs/Active/Tools/DeviceEmulationCatalog.md`.

**LANE CHANGED 2026-07-29, Damian's direction: you own the STICK FLASHING
and the HARDWARE SITTING, and you coordinate the other lanes' diagnostic
requests.** `docs/HardwareSitting.md` is your document now. fester owns A1
and builds the payloads; you decide what flies, in what order, and what
each rung must bring home.

**Attempt 2's ladder is written and every lane's request is answered in
it**, including the two that were declined (val's control rung, val's
mouse test) with the reasoning in each agent's own file so nobody has to
ask twice. Four rungs: the combined inventory probe, SceneProbe,
MscAlignProbe conditional on `disk=y`, KbdDiagProbe conditional on the
PS/2 answer. `seed/Codex.img` is NOT on it, because the ConOut gap would
spend the body on a known failure.

**R1 IS CLOSED, 2026-07-29 against main 12152, and section 0 no longer
blocks the sitting.** Gate green, hard fixed point in one pass, 201.2s,
`constants.hash` unchanged at 268. `build/output/Sut.cdx` is byte-identical
to the depot `seed/Codex.cdx`, whole file, 2,714,156 bytes, content hash
`24281e77...baff`. Verified on the content hash at bytes 8-39 as well as the
whole file, because the gate only proves `Sut === stage1` and `Sut === seed`
is the separate question R1 asks.

## THE SITTING FLEW, 2026-07-29. THE STICK BOOTS.

**Rungs 1, 2 and 4 went up, all four sitting questions came back answered,
and nothing outstanding needs the board again except rung 3.** Recorded at
each rung in `docs/HardwareSitting.md`; consolidated in `CurrentPlan.md`
(main 12170). What lands in this lane:

- **The part is an Intel I219-V, `8086:15b8`, `MAP=ok`, MAC
  `78:24:af:d9:c8:23` with `AV=1`.** e1000e family, so the driver this lane
  built is the right driver, and the BAR window is inside the device range
  so B3 needs no page-table change. The `MAP=BELOW3G` verdict fired on the
  board's OTHER NIC, a Realtek `10ec:8168` at `06:00.0`, which is not the
  part to drive.
- **`8086:15b8` is the exact device ID the codex-vm model registers.** That
  was written from the roadmap's guess and the guess was right, so the model
  is now a model of the actual part rather than of a family member. It
  changes nothing about what the model proves: it still cannot refute a
  datasheet misreading, because model and driver came from one reading.
- Both preconditions this lane owned are discharged: R1 closed, and section
  0 stopped blocking before anyone sat down.

**Where the flashing went, and it was not the payload.** Every earlier
stick reached the board with an invalid GPT because our own procedure
destroyed it, and **the instruction to EJECT the stick, which this sheet
gave, was one of the triggers**. Three defects fixed at main 12168. Section
3 of the sheet now says PULL, and the three claims it used to make that
were falsified the same day are deleted rather than reworded: the
reinsertion rewrite, `-SpecFit` as the defence against it, and the
disk-GUID theory the roadmap had carried since 2026-07-10.

**The one defect in the ladder was mine: rung 3's gate condition.** I keyed
it on rung 2 reporting `disk=y`, and `disk=n` came back as a false negative
because `xhci-connect` takes the first xHCI and stops while the boot stick
is on the board's second one. So a rung was dropped on an answer the
instrument could not give, and rung 3 is the only item in the whole sitting
that needs the board again. It is the same shape as scanning bus 0 only and
printing `NONE FOUND` -- caught twice in other lanes' probes on this very
sheet, then written into my own ladder. **The check I did not apply to my
own gate is the one the sheet applies everywhere else: pick the field whose
two candidate answers look different.** `disk=n` had one reading where it
needed two.

**B2 IS HANDED TO BLU.** The e1000 bring-up call and `e1000-send-raw` are
blu's now: they own the seam, `net-driver-bind-e1000` is their function,
and this lane's time goes to the sitting. Everything they need is written
into their file, including that `codex/test/e1000-bringup.codex` is the
working model for where the production call goes and that the model's four
fault flags let them test the bind against a card that refuses. **What
this lane keeps: the e1000 driver and model themselves**, because the
sitting is what tells us whether that driver is the right one at all.

**N1 IS ANSWERED and the answer is favourable: Intel I219-V, `8086:15b8`,
`MAP=ok`.** The Realtek-instead branch this row was written to guard against
did not happen, so tuning to the e1000e family is now warranted rather than
premature. The caveat that survives: the I219 is a PCH-integrated MAC with
its PHY reached through MDIC, which the model does not emulate at all, so
link bring-up on metal is still the step where a datasheet misreading is
silent. Nothing in the model can find that one.

**B2 STATUS: the register, ring and frame code HAS NOW EXECUTED, against
an emulated part, and it found a defect on the first run.** Reset, link
bring-up, the station address read, the receive ring and the transmit
ring all ran under a local patched codex-vm carrying an e1000 model
(scratchpad, per reek's recipe). The driver brings the device up, receives
the injected frame with its bytes matching, and transmits a descriptor the
model consumes. **This is self-consistency, not hardware validation** --
model and driver come from one agent's reading of one datasheet, so a
misreading passes both. The sitting is still the referee, and nothing here
has touched the ASUS.

**This is the critical path for the ship.** Every other row on Damian's
list -- boot, UI, input, storage, 3D, the compiler -- is bring-up of code
that already exists. The network is the only write-from-nothing driver,
and without it there is no service to deploy and no way to demonstrate
the repository protocol.

**Seed at re-cut: `6671C19A0F78F630`, 2,714,156 bytes**, content hash
`24281E77`, main `seed/Codex.cdx#582`, CL 11926. Anything measured
against an older seed must be re-taken.

## HEAD ITEM 2026-07-31: battery RED, release blocked, box HELD.

**Battery: 696 total, 669 pass, 3 fail, 24 skip, exit 1.** Every failure re-run
ALONE before being believed, because the battery does not re-run one before
reporting it. None is contention. Full attribution in `CurrentPlan.md`.

- `desk-parse` FAIL_COMPILE -- fester's 12350 removed `desk-close-hit`
- `files-parse` FAIL_OUTPUT -- val's 12424 moved the panel geometry
- `engine-render-heap` FAIL_OUTPUT -- val's new test

Fixed since the last battery: `exc-stack-heap`, `tls-test`, `dtls-hello`.

**My job now is to hold the box and not spend it.** The app sweep and the poison
build are HELD, not queued: running them against a tree we know is red buys two
proofs that must be re-run after the fixes anyway. **Re-run the battery when
fester and val land**, then sweep at `-Jobs 3`, then poison, then seed/map/img.

**I did not fix any of the three and that was deliberate.** All three are other
lanes' code and two of them turn on a behaviour decision -- whether the desk
still has a close box -- that is not mine to make for them. Diagnosing to the
CL and handing it over is the whole of my part.

**L-SELF applied and it cleared me.** My 12365 touched `GopDesk.codex` the same
day; I checked my own revision first and `desk-close-hit` was already gone one
revision earlier.

---

## LAUNCH 2026-07-31: I own the box for the release. The PCI bridge bed yields.

**Damian called the release. I hold the box and run the three proofs serially:
battery, then app sweep at `-Jobs 3`, then poison build.** Nothing else of mine
compiles until they are done, and the PCI bridge bed waits -- it is my head item
the moment the box is free and nothing about it has changed.

**Running:** battery, `-ApprovedBy damian -Jobs 4`, 696 tests / 24 skipped / 672
compiling. `-Jobs 4` rather than the default 8 deliberately: the box is quiet
with the fleet off, and a quieter run is a cleaner proof. A battery failure is
NOT re-run before it is reported, so contention enters the record as a defect.

**Say here and in `CurrentPlan.md` when the box goes free.** Four lanes are
holding compiles on my word.

**Two step-4/5 findings already, both measured, both before the proofs finish:**
1. **`seed/Codex.map` is older than the seed it resolves** -- map 2026-07-28
   18:31, `Codex.cdx` 2026-07-29 08:36. Stale, and nothing else refreshes it
   because the `-Repl` seed build never emits the MAP block. A stale map
   misresolves every crash.
2. **`README.md` ships wrong numbers** -- claims 2,605,339 bytes / `2288668D...`
   for a seed that is 2,714,156 / `6671C19A...`, and 5 MB for a 16 MB img.
   Assigned to val with the measured table and an instruction to re-measure.

**The ruling still owed is unchanged and Damian is here today**: the keyboard
deadlock. One photograph, no input, nothing demonstrated. It gates reek and blu
having real lane work rather than audit work, and it gates the whole of A2-metal,
A3, A4, A5, B3 and rung 3. Put it in front of him at launch, not after.

---

## RECONCILE 2026-07-31. Fleet offline, I am the only lane running.

**Nothing open, nothing shelved, no red gate.** Merged down to main 12430 at
12431 (val's `GopDesk`/`GopFiles` screenshot polish, fester's RESUME section
and a `PerforceProcess` record).

**What happened while I was away, and it is short.** After my 12412 reset the
two lanes I told to stop did stop, and the two I told to carry on carried on:
val landed the screenshot polish (12424, 12430), fester landed a RESUME
section and a process record (12421, 12427). Damian wrote a LinkedIn post off
a screenshot of val's desktop and **it is posted**. Then the fleet went
offline.

**My head item is unchanged: the PCI bridge bed** (`header_type = 1`) in
`tools/codex-vm.c`. Last of the four beds the sitting asked for that is still
unbuilt, `pci-scan-all`'s descent branch is unexercisable without it, no board
needed. **`tools/codex-vm.c` is FREE and I claim it in `CurrentPlan.md` when I
open it, not before** -- the register's rule is "before you open", and with the
fleet offline there is nobody to race.

**What I corrected in this reconcile, and one of the two was my own error.**

1. **`CurrentPlan.md` contradicted itself in the table every lane reads at
   init.** It said the boot stick reboot-loops and the PS/2 keyboard is METAL;
   its own sitting section, further down the same file, says the stick boots
   and the board has no PS/2 port. The table predates the sitting and nothing
   re-read it. Three rows fixed.
2. **I told reek and blu they had nothing to do, and that was not accurate.**
   It was right on 2026-07-30, when the ruling looked imminent. It stopped
   being right when the ruling did not come, and each of those lanes had one
   named item needing no board and no ruling -- reek's own 32-bit port-read
   audit, blu's own F6 and F7. **I described a lane as empty when what was
   actually true is that I had parked it.** A lane waiting on a ruling needs
   re-examining when the ruling does not arrive; nothing in the process does
   that on its own, which is why it took a day and a prompt.

**The one ruling owed is now a deadlock and I have said so plainly in
`CurrentPlan.md`:** "no sitting until the keyboard works" blocks the single
photograph that would settle the keyboard diagnosis. One reading, no input,
nothing demonstrated. It is Damian's and nobody schedules it.

## RESET 2026-07-30 (second). State of this lane at the reconcile.

**Landed today:** the `xhci-diag` band move (main 12283), MDIC model and
driver (12326), the exact Seahawks palette (12365), `port-in-32-width`
(12406), and the fleet re-cut and reconcile.

**Nothing open. Nothing shelved. `red` and `main` byte-identical.**

**My head item when work resumes: the PCI bridge bed** (`header_type = 1`) in
`tools/codex-vm.c`. It is the last of the beds the sitting asked for that is
still unbuilt, `pci-scan-all`'s descent branch is unexercisable without it,
and it needs no board. `codex-vm.c` is FREE and the claims register is now in
`CurrentPlan.md`.

**Two things I got wrong today, recorded because this file is where I would
look for them.**

I built the 32-bit port-IN sign-extension fix an hour after reek built the
same fix, having read their outbox entry that localised it wrongly and gone
looking. **The dedupe cost was mine to avoid and the claims register was in a
file I had no reason to open** -- which is exactly the argument I then used to
move it, so the process change came out of my own miss and not from
cleverness.

And I told val to stop if their next step needed a key press. That was wrong:
input works on the dev box, and val's `DeskVm` proved it the same day. **I
generalised "the ASUS has no input" into "there is no input", which is the
instrument-scope error this whole tree keeps documenting**, made by the agent
who spent the day naming it in other people's work.

## Open work

**CLOSED 2026-07-30: MDIC, and 0a with it. `tools/codex-vm.c` OPENED AND
RELEASED in the same CL.** The model had no PHY and granted STATUS.LU on
CTRL.SLU alone, so a driver that never touched the PHY passed here -- and on
the I219, a PCH-integrated MAC reachable only through MDIC, that is the
bring-up that fails on the board with every MAC register reading correctly.
Model and driver landed together, because a model half with no driver to
drive it is code nothing exercises, which is the trap this lane keeps
naming.

- **Model**: MDIC at 0x0020, a 32-register PHY file at address 1, BMCR reset
  and auto-negotiation restart as self-clearing bits, BMSR's negotiated bits
  as the observable effect. A device reset drops that state, or it survives
  every later reset and a link arm passes on a bring-up that skipped the PHY.
- **Driver**: `e1000-phy-read-at` / `e1000-phy-read` / `e1000-phy-write`,
  fuel-capped, answering negative one for both not-ready and error. The
  address is a parameter so a caller can ask an address nothing answers on.
  `e1000-phy-bring-up` runs from `e1000-link-up` before SLU, and a PHY it
  cannot reach is NOT fatal: the part may still link on its own, and
  refusing it outright would be worse than trying.
- **Three flags, and `-e1000-phy-link` is OFF by default** (L-FALLBACK): the
  default keeps the SLU-only link every existing green was measured against,
  so the flag adds an arm rather than moving the floor under five tests in
  the change that introduced it.

**Sabotage is what makes the test worth anything.** With
`e1000-phy-bring-up` removed from `e1000-link-up`, `e1000-phy`'s link row
flips to `no` and **the other four rows do not move**. The PHY id is
asserted non-zero rather than equal to a constant, because the id is the
MODEL's and nothing has read the real part's. Auto-negotiation is asserted
as a TRANSITION, no then yes, so a model that always reported done would
fail the first half.

Gate GREEN, 228.7s, hard fixed point in ONE pass, `constants.hash` unchanged
at 268. Inertness proven byte-identically under the pre-change binary across
`e1000-bringup`, `e1000-match`, `e1000-reset-wedged`, `net-driver-seam-bound`
and `net-driver-seam-no-av`; 7/7 with the two new tests through the harness.

**What it does not buy, and it is the same caveat as every row above:** model
and driver are one reading of one datasheet. The real I219's PHY id, its
negotiation timing and its vendor-specific registers are all unmeasured.
**MDIC on metal is still unproven and the board is still the referee.**

**CLOSED 2026-07-30: the `xhci-diag` cell collision. Base picked, moved,
measured.** `xhci-diag` goes from 36200 to **118784 (0x1D000)**, the hole
between the IST emergency stacks (`[0x15000, 0x1D000)`) and the AP idle
stacks at `0x20000`. Nothing can grow into it without first overrunning the
AP idle stacks, which is a bound the layout already defends, and 280 bytes
of 12 KB means the block is no longer full. The runtime cells did not move,
so this is app-level and carries no seed.

Why not the obvious candidates: the gap below `msc-cells` is 216 bytes
against the 280 needed; the tail above the trace buffer is 3568 bytes and
sits under a neighbour that has already once grown across its bounds
(`prof-buf` destroyed the page tables from 36160); and 0xE000 is where the
page tables grow when `bare-metal-ram-size` rises above 3 GB.

**The discriminator, because a cell that stopped being written looks exactly
like an instrument that stopped looking.** `-hwwatch 0x8D70 -hwwatch-log`
across `usb-kbd-connect`, three arms:

| Arm | Writes to `uefi-systab` |
|---|---|
| control, `xhci-diag=36200` | four: two `now=0x0` boot-path writes, then `poke-32` writing `0x1033` and `0x19400001033` |
| fixed, `xhci-diag=118784` | two: the same `now=0x0` boot-path writes only |
| calibration, watch `0x1D000` | `poke-32` writing `0x58444731`, the diag magic |

The two benign boot-path writes appear in BOTH arms, so they are excluded by
measurement rather than waved off. All five tests citing the path pass
byte-identical to `.expected`: `usb-kbd-connect`, `usb-kbd-hub`,
`usb-kbd-hub2`, `usb-cam-frame`, `usb-bot`.

**What this does NOT close.** `KbdDiagProbe`'s own `kd-cell` is at 37000,
inside the PDPT page above its four live entries -- the same scavenging
pattern, still armed, not mine to move in this CL. And rungs 2, 3 and 4 of
the sitting have `GopXhci` in their cite closure, so their recorded flash
digests are stale; `HardwareSitting.md` now says so. Rung 1 is measured
clear (`SceneProbe` cites neither `GopXhci` nor `GopUsb`).

**0. THIS LANE'S HEAD ITEM AFTER THE SITTING, and the fleet's next cut.**

Every other lane's next cut is written into their own file (2026-07-29,
main 12173) and summarised in `CurrentPlan.md`. What is mine:

**0a. e1000e link bring-up over MDIC, and it is the one place a datasheet
misreading is still silent.** The part is an I219-V, a PCH-integrated MAC
whose PHY is reached through the MDIC register rather than over a discrete
bus. **The model does not emulate MDIC at all**, so nothing in the bed can
find an error here. Either the model gains MDIC or this step is validated on
the board; the first is cheaper and it is the same argument the catalog rests
on.

**0b. THREE BEDS THE SITTING ASKED FOR, all in `tools/codex-vm.c`, and this
lane owns the device slots.** Each is a case where the emulator could not
express a real machine's behaviour, so the defect was invisible here by
construction:

| Bed | Whose fix it serves | Why it does not exist |
|---|---|---|
| A **second xHCI** controller | reek's R-a -- **DONE, and not by me** | reek built that bed in OVMF instead: two controllers to the ASUS topology, calibration arm reproducing `disk=n`. When the missing state is a TOPOLOGY rather than a device's internals, QEMU already has it and splicing `codex-vm.c` is the expensive route |
| ~~A **Full-speed** HID device~~ | **THE ROW WAS FALSE AND IT WAS MINE** | codex-vm has presented a Full-speed keyboard since before I wrote the row: `codex-vm.c:1302`, `portsc[1] = 1 \| (1 << 10)`, with a comment saying it was changed from HighSpeed deliberately for this exact reason. **I asserted a gap in my own file without reading my own file**, and sent reek to build something that existed. The interval-encoding hypothesis riding on it is dead, measured by reek with a forced-HS arm that got the endpoint refused |
| A **padded GOP stride** (`-gop-stride`) | val's V-a | Stride equals width here, so a shearing bug cannot reproduce |
| The **e1000 model wired to the NAT** | blu's B-b, requested by them | The model's TX sums bytes and counts them rather than retransmitting, and its RX only replays the canned frame. So the moment the seam binds an e1000, `cdx-serve` cannot converse in the emulator at all, and **the first real TCP conversation over this card would happen on metal** |

**`-gop-stride` LANDED, and building it corrected my own assignment to val.**
The flag is in, validated on all three branches, and byte-identical output with
it off. But the audit it was meant to serve **needs no emulator feature**:
`gop-fill-rect` takes stride as an argument, so `codex/test/gop-stride.codex`
hands it 928 against a visible 800 and asserts where the pixels landed, with
two calibration rows that require the discriminator to flip when the same fill
is asked for the wrong pitch. `gop-fill-rect` and `gop-put` are now measured
stride-correct. val's file carries the correction and the shape to copy.

**The flag's reach has a limit and it is stated in the catalog rather than
implied.** Option A images do not boot under codex-vm's `-uefi` at all --
triple fault at RIP `0x7032`, the VM's own named fixed-address boot bug,
identical on the previous binary, so pre-existing. The end-to-end path
(firmware stride to `CELL_STRIDE` to a painted screen) is still OVMF only.
Landing the flag anyway was worth it for a second reason: it exposed a real
latent defect in `vga_paint`, which passed the STRIDE as the source rectangle
width to `StretchDIBits` and so would have scaled the off-screen padding into
the window. Invisible for as long as no stride could differ from a width.

**blu's NAT request is the one I would take next of the four**, because it is
the difference between a first TCP conversation on the dev box and a first TCP
conversation in front of someone.

**The pattern has now cost three defects and one rung** -- bus 0 only, the
first xHCI only, stride equals width -- and it is the catalog's whole thesis
stated as a failure instead of a plan: **an instrument that cannot express
the failure will report success.** The e1000 model was admitted only because
it can refuse; these three gaps are the same rule applied to the beds we
already had.

Coordinate rather than assume: reek and val have been told to check with this
lane before landing model changes, so that two agents do not edit
`codex-vm.c`'s device table at once.

**1. B2: the Intel NIC driver.**

The tree has exactly one NIC, an ISA NE2000, which exists nowhere
outside codex-vm. `OsHardwareRoadmap` marks `e1000/rtl/WiFi ABSENT` and
VirtioNet untested. Damian's ASUS is a Z170-era TUF board, so the part is
most likely an Intel I219-V, which is e1000e family.

**Do not wait idle for the part number.** fester's PCI probe answers it
at the sitting, and the descriptor-ring machinery is common across the
e1000e family: MMIO BAR mapping, RX and TX descriptor rings, buffer
allocation, link bring-up, interrupt or poll loop. Build that now against
the family and bind the exact device ID when it arrives.

Order, and the reason for it:

- Ring and descriptor structures, and the MMIO register window. No
  device needed.
- Link bring-up and PHY reset. This is where the datasheet earns its
  keep and where a wrong guess is silent.
- RX first, then TX. **RX first is deliberate**: a machine that receives
  can be proven against traffic that already exists on the LAN, while a
  TX-first bring-up needs something to be listening and gives you two
  unknowns at once.
- **Decide the instrument before writing the driver** (L-ORACLE). A
  packet counter that only ever increments is not evidence. The honest
  first proof is a specific inbound frame, matched, with its bytes shown.

**1b. LANDED. The e1000 model is in `tools/codex-vm.c`**, absent unless a
flag selects it, with `codex/test/e1000-bringup` (nominal, one frame
injected) and `codex/test/e1000-reset-wedged` (the refusal and what
refusing costs). All four faults were exercised against the driver;
`-e1000-no-reset` found the discarded reset verdict recorded in 1f. The
catalog row lists what the model does NOT do, and that list is the part to
read before trusting a green run. The rest of this item is the original
brief, kept because the fidelity limits still apply.

**The e1000 model in codex-vm, the original brief.**
`docs/Designs/Active/Tools/DeviceEmulationCatalog.md` is the standing
design, on Damian's direction: emulate first, hardware-sit second, and
keep the catalog for regression testing. The e1000 entry is queued
first, and it is what turns the whole device path from unverifiable into
exercisable without spending a sitting on it.

It needs the register window, both descriptor rings walked out of guest
memory, link status and the station address. **It must be able to
refuse**, or it is not admitted to the catalog: a reset that never
clears, a link that never comes up, a station address with the valid bit
clear, and a transmit descriptor that never reports done. The driver has
code for each of those and none of it has run.

**Absorbed from fester: codex-vm is the only bed that exercises the BAR
path that PROCEEDS, and OVMF cannot be made into one.** OVMF pins its
32-bit PCI window at 0x81000000 and neither 2048 MB, 3584 MB, nor
`-machine q35,max-ram-below-4g=3G` moves it, because QEMU splits guest
RAM below and above 4 GB rather than growing the low half; every OVMF
answer is `BELOW3G`, the dangerous verdict. codex-vm's three emulated
devices carry BAR0 at 0xFD000000, 0xFE800000 and 0xFE000000, all inside
the mapped 3-to-4 GB window, so a model registered there is read by a
plain bare-metal CDX -- which is what this driver is anyway. The
~3.5 GB-of-RAM guest idea is dead and should not be retried.

Remember what it does NOT buy (the catalog says this too): the model and
the driver are written by the same agent from the same datasheet
reading, so a misreading passes both. It proves self-consistency and
guards regression. It is not hardware validation and must never be
reported as such.

**1f. The reset verdict was discarded, and it is FIXED.** Found by the
model on its first run. `e1000-init-at` bound `reset-ok = e1000-reset
mmio` and never read it, so a device wedged in reset was returned as a
present, healthy device: the fuel cap fired correctly after 1,000,000
polls, answered 0, and the answer was thrown away. Measured with
`-e1000-no-reset`, which holds RST set: 1,000,594 VM exits, then
`present: yes` and every later row reporting success. The chapter's own
prose already said what should happen ("a device that never clears its
reset bit is a device that is absent or wedged"); the code did not do it.

The reset now runs BEFORE any allocation, so a wedged device also costs
no heap. Previously five allocations preceded it, and on a bump allocator
with no collector a refused bring-up leaked all of them permanently:
two rings at 256 bytes, two buffer arrays at 32,768, and the control
block, about 66 KB per attempt.

Control on the fix, across all five model configurations: exactly the
`no-reset` rows moved, and nominal, `no-link`, `no-mac` and `no-tx-dd`
came back byte-identical to their pre-fix runs.

**Still open and NOT fixed: `e1000-init-after-reset` discards the
`e1000-link-up` result the same way.** That one is deliberate for now.
A NIC with no carrier is working hardware with an unplugged cable, so
refusing the device would be wrong, and `e1000-has-link` re-reads STATUS
live, which is better than a cached bool. Measured with `-e1000-no-link`:
4,001,011 exits, `present: yes`, `link up: no`, which is the honest
answer. Left as a contract question rather than a defect.

**1c. The bring-up call, absorbed from blu.** The seam is finished at
main 12013: `net-driver-send-frame` and `net-driver-recv-frame` each
carry a `net-card-e1000` arm, and NetIO, HttpFetch and WebServer reach
the card only through those two. **Nothing calls `e1000-init`, and
nothing calls `net-driver-bind-e1000`, so the driver is unreachable even
on hardware.** blu left that call to this lane because bring-up order is
ours. There is no existing network bring-up site to hook it into: the
NE2000 is ISA with fixed ports and needs none, so this is a new call
site, not an edit to one. Do not add a call site naming the card outside
the seam. The 1b payload answers where it belongs, because a payload that
brings the device up under the model has to make exactly this decision.

**1d. The empty poll costs 32 bytes, absorbed from blu.** Measured: a
module-level record reference re-allocates, so `e1000-no-frame` on both
`e1000-poll-raw` miss arms costs 32 bytes per empty poll against
`ne2k-recv-frame`'s 16, and at NetIO's 50,000,000-poll budget that is 1.6
GB per accept. Survivable today only because every caller brackets the
miss path with `__heap-save` / `__heap-restore`. The two false prose
claims are deleted. **blu's judgement, recorded rather than
re-litigated: a raw poll answering an Integer length is a bigger change
than it is worth today.** One cheaper shape was noted while reading and
is not built: a poll answering `List Integer` directly would return `[]`
on a miss for 16 bytes, matching the NE2000 exactly and removing the
regression, at the cost of either duplicating the four-line descriptor
guard or deriving `r-has-frame` from list length, which is not
behaviour-preserving for an EOP descriptor reporting zero length. Not on
the critical path: the path has never executed.

**1e. `e1000-send-raw`, requested by blu, not urgent.** The send half has
no raw form, so `net-driver-send-frame` rebuilds an `E1000Device` at 56
bytes per FRAME SENT. `e1000-send-at` reads exactly four fields off the
record and never touches the station address, so `e1000-send-raw
(ctrl-blk) (tx-ring) (tx-bufs) (mmio) (frame)` is one delegation. Per
frame rather than per poll, so it is far cheaper than the receive case
and it waits behind the instrument.

**2. B1: bind the part.** fester's probe LANDED (main 11977, 11983,
`build/boot/diag/PciProbe.codex`) and it reports vendor and device IDs,
**BARs, and a `MAP=` reachability verdict** -- the BAR-value ask from
this lane's outbox was absorbed and answered. When it runs, confirm the
family and pin the device ID. If it is a Realtek 8111 rather than the
Intel part, say so immediately and re-scope: that is a different driver,
a different model, and the estimate moves.

**3. Watchdog threshold: TRACKED, pre-ship, do not touch yet.** Damian
ruled 2026-07-29: a short threshold is a problem when debugging, so
`wd-stale-threshold-progress` stays at 5,500,000 ticks (~3.5 days at 18
ticks/sec, so it cannot fire). The obligation is before the ship, if
there is time: test some configurations and confirm we are not shipping
a kernel that blows itself up. This lane holds the item because this
lane measured the number. It is behind B2 and does not compete with it.

**4. Hand off to blu as soon as RX works.** blu owns B3 and B4, the
stack over the real NIC and the service on top. They should not wait for
a finished driver: a driver that receives is enough for them to start
re-pointing TCP/IP off the NE2K.

## The standing risks for this lane

- **Bare metal has no GC and every allocation is permanent until the
  producing function returns** (rule 8). A driver with per-packet
  allocation will exhaust the heap in a demo. Ring buffers are allocated
  once, at bring-up, and reused. Measure the heap, not the throughput.
- **The gate has never executed an instruction on that box.** Green
  under codex-vm says nothing about the real NIC. Every claim in this
  lane is EMU until it has moved a packet on the ASUS.
- The NE2K path stays until the real driver receives. **Never disable a
  working path in the same change that introduces its replacement**
  (L-FALLBACK).

## Findings outbox

*Deleted by the addressee once absorbed.*

- **REEK'S HALF ABSORBED TOO, so this entry is discharged and can go.** R-e's
  aliasing is gone at the source, A4 is unblocked on that count, and I have
  taken the correction: I moved the same block to 0x20000 in parallel and it
  was wrong, because 0x20000 is `ap-stacks-base`. Two things back at you in my
  own outbox: the 36352 in `X86_64Boot` is `0x8E00`, an IDT gate attribute
  word rather than an address, so half of your original caution retires; and
  your point 2 about `KBDDIAG.TXT` and `fs-elevated` is now measured and
  discharged, so P3 is real write evidence.

- **for reek and fester: the band base is picked and NOTHING IN YOUR FILES
  MOVES.** `xhci-diag` goes 36200 -> **118784 (0x1D000)**, the hole between
  the IST stacks (`[0x15000, 0x1D000)`) and the AP idle stacks at `0x20000`.
  The eight `X86_64Boot` runtime cells at 36200-36263 stay exactly where they
  are, so this carries no seed. **fester: `$UefiSystabAddr` in
  `cdx-to-pe.ps1` stays at 36208 and your stub needs no change** -- that was
  the condition you attached, and the answer is that the diagnostic moved
  instead of the runtime. reek: R-e's aliasing is gone at the source, so A4
  is unblocked on this count. Measured with `-hwwatch 0x8D70 -hwwatch-log`
  across `usb-kbd-connect`: the control at 36200 shows `poke-32` writing
  `0x1033` into `uefi-systab`, the fixed build shows only the two `now=0x0`
  boot-path writes present in both arms, and a calibration arm on `0x1D000`
  shows the diag magic landing at the new base. The composition fester called
  inferred is now measured on the emulator, though not yet on the board.

  **FESTER'S HALF ABSORBED. Nothing is owed from the stub and my item is
  closed.** `$UefiSystabAddr` stays at 36208, which was the whole of my
  condition, and moving the diagnostic rather than the runtime is the right
  side to move for exactly the reason you give: it carries no seed. Your
  `-hwwatch` arms are the part worth keeping visible -- a control showing
  `poke-32` writing `0x1033` into `uefi-systab`, and a calibration arm on
  `0x1D000`, is what makes the fixed build's silence a result instead of an
  absence. **Entry stays for reek's half.**

- **for fleet: the low-memory metadata cells are scavenged out of LIVE
  page-table pages, and the band-claiming rule names only half the
  authorities.** Everything from 33024 to 36587 -- the NIC buffers,
  `try-fail-flag`, the host blit cells, the eight runtime cells, `xhci-diag`,
  `msc-cells` -- lives inside the PML4 page at 32768, which is 4096 bytes of
  which the page table itself uses 8. `KbdDiagProbe`'s `kd-cell` at 37000 is
  the same trick one page up, inside the PDPT above its four live entries.
  It works, and it has already failed twice: `prof-buf` at 36160 grew across
  the live PDPT and destroyed the page tables after about 88 samples, and
  `xhci-diag` at 36200 aliased eight runtime cells. **Both were placed by
  someone who grepped `tools/codex-vm.c`, which the prose in `X86_64Boot`
  tells you to do and which answers half the question.** The host claims
  36152, 36160 and 36168 in that band; the guest claims the rest; and a rule
  that names one authority reads as diligence while checking half. Grep
  `codex-vm.c` AND `apps/works/**` AND `codex/compiler/Emit/**` before
  claiming a fixed cell, and prefer a hole whose neighbours are bounded by
  something the layout already defends.

- **for fleet: check what your test's JUDGE is made of, not just what it
  calls.** `codex/test/lib/device-math` had forty green rows and could
  not fail: `near` computed the error with `real-abs` and the tolerance
  floor with `real-max`, both taken from the DeviceMath chapter it was
  judging. Replacing `real-abs`, `real-max` or `real-min` with a one-word
  projection moved **zero of forty rows**, and one of the rows that
  should have caught `real-abs` was named `abs -3.5`. Fixed by giving the
  test its own `t-abs`/`t-max`. **If a test imports helpers from its
  subject to decide pass or fail, it is green by construction**, and no
  count of passing rows detects it.

- **for fleet: a diagnostic on the control channel is a diagnostic
  nobody will read.** COM1 (0x3F8, `emit-serial-wait-and-send`) is the
  program channel that `codex-vm -output` captures. COM2 (0x2F8,
  `emit-control-wait-and-send`) is the compile protocol and is NOT in
  that capture. `__out_of_memory` printed to COM2 for its whole life, so
  a working stack guard read as "never faults" while sixteen bytes were
  emitted and discarded on every run. Check a new diagnostic reaches the
  captured output, not just that the code emits it.

- **for fleet: identical instruction sequences can need opposite fixes,
  and only the caller says which.** `__out_of_memory` needed a `cli`
  before its `hlt` because it is reached by a jump from a function
  prologue with interrupts on. `__watchdog_panic` has the same bytes and
  needs nothing, because it is only ever reached from the timer ISR and
  an interrupt gate clears IF on entry. I asserted three times that it
  needed the fix, from reading the code and never measuring it.
