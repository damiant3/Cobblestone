# Current Plan -- Ship The Stick

**Updated**: 2026-08-01, reconciled by red with the fleet offline. Re-cut
2026-07-29 and reset 2026-07-30, both on Damian's direction. This
replaces the 2026-08-10 release page. Closed work has been deleted
rather than struck through; Perforce is the record
(`p4 changes -m 100 //Codex/main/...`). **If an item is not here, it is
not in scope for the ship.**

## RELEASE IN PROGRESS, opened 2026-07-31 by Damian. Read this before you build anything.

**Damian called the release. `docs/Agents/PublicPush.md` and the `release`
skill govern; this section is only the fleet's coordination around it.**

**THE BOX RULE, and it is the one that can silently ruin the release.** The
three proofs a release cannot skip -- the battery (depth), the app sweep
(breadth over the front end) and the poison build (memory hygiene) -- are all
CPU-heavy, all run on this one box, and all produce **failures that look
exactly like defects when the box is loaded**. The sweep's own doc says not to
raise `-Jobs` for a release run for this reason; the battery does NOT re-run a
failure before reporting it, so a contention failure enters the record as a
real one.

**So while a proof is running, no other lane compiles anything.** Not a test,
not an app, not a probe. red owns the box for the duration and says here when
it is free. Every lane's launch assignment below was chosen to be
**box-free** -- reading, auditing and writing -- for exactly this reason. This
is not busywork: two of the four assignments are public-facing correctness
that has to happen before a push regardless.

| Step | State |
|---|---|
| 0 preconditions | **DONE.** main 12437, `red` and `main` byte-identical (`p4 diff2` clean), nothing open or shelved, no stray VMs, last gate green 199.6s hard fixed point in one pass |
| 1 battery | **GREEN** against reek's 12519. 1402 total, 1358 pass, 0 fail, 44 skip, 0 newly red, all three oracles pass |
| 2 app sweep | **GREEN** |
| 3 poison build | **red's head item. Never run.** Needs `-Tier all` against a 0xCD-fill seed |
| 4 seed / map / img | **red's head item. Map is STALE and this is measured**, see below |
| 5 README + GitHubUpdate | val's README audit, then GitHubUpdate37 |
| 6 push | red + Damian, last |

### Step 1 went green on 2026-07-31. The three failures are fixed and verified.

**This row read RED until 2026-08-02, five days after the last of the fixes
landed.** The three tests below were repaired on main on 2026-07-31; the
2026-08-01 re-cut further down this document recorded the battery as green; and
this table, one screen above it, still said THE RELEASE IS BLOCKED HERE. A haiku
agent summarising this file at init on 2026-08-02 read the table, not the
re-cut, and reported the release as blocked. **Re-read this document's later
sections against this table before trusting this table** -- that is L-COUNT one
level up, and it is now the second time it has bitten in the same file.

| Test | Fixed by | Verified |
|---|---|---|
| `desk-parse` | fester, **main 12448** | Re-pinned on the wrapped-window invariant. `desk-close-hit` genuinely went with the close box at 12350; the only surviving mention in `desk-parse.codex` is a column-2 prose line explaining the deletion, so it compiles |
| `files-parse` | val, **main 12451** | Golden re-derived after the hex-dump panel resize |
| `engine-render-heap` | val, **main 12451** | |

Also FIXED since that battery: `exc-stack-heap`, `tls-test`, `dtls-hello`.

**What the red row said, and it is still the reason a release exists.** The
standing gate stayed green through all three, because `build/build.ps1` never
runs these tests. Two of the three were the SAME close-box behaviour, changed by
two lanes in two days -- fester's `desk-loop` fix and val's panel resize -- and
neither updated the test that pinned it.

**Why the sweep and the poison build are HELD rather than queued.** Running
them now buys two proofs taken against a tree we already know is red, and both
would have to be re-run after the fixes anyway. One clean proof against a green
tree is worth more than two dirty ones. Reversible the moment Damian says
otherwise.

**Measured 2026-07-31, and step 4 has a real finding.** `seed/Codex.map` is
dated 2026-07-28 18:31 while `seed/Codex.cdx` is dated 2026-07-29 08:36 --
**the map is older than the seed it resolves**, which is exactly the stale-map
case the release step warns about ("a stale map misresolves every crash"), and
nothing else refreshes it because the `-Repl` seed build never emits the MAP
block. The img is newer than the seed but a release ships a rebuilt one
regardless.

| Artifact | bytes | SHA-256 (head) | mtime |
|---|---|---|---|
| `seed/Codex.cdx` | 2,714,156 | `6671C19A0F78F630` | 2026-07-29 08:36 |
| `seed/Codex.img` | 16,777,216 | `88E08F6469024E18` | 2026-08-02, re-measured |
| `seed/Codex.map` | 161,026 | `5864487DDF235001` | **2026-07-28 18:31, STALE** |

**`seed/Codex.img` is now the GOP payload and it PAINTS, and two things follow
for step 4.** fester's B5.4 step 3 changed what this artifact is; the ConOut gap
no longer holds it off the boot-3 ladder, because `GopBoot` renders through the
GOP framebuffer rather than ConOut. And **R8's `OUT OF MEMORY` was the stale
artifact**: it does not reproduce from current source on either payload. A step-4
rebuild now rebuilds a GOP image.

**Re-measure this row rather than quoting it.** fester's outbox identified this
img as `2447B48D...`, which was true of revision #59 (CL 12551) and was already
one revision stale when it was read: their own later diag-probe rebuild landed
#60 (CL 12560), which is the `88E08F64...` above. Neither hash is wrong; the
mistake available here is treating either as the artifact's identity.

**`README.md` ships wrong numbers today and it is the public face.** It claims
`seed/Codex.cdx` is 2,605,339 bytes with SHA-256 `2288668D...` (actually
2,714,156 and `6671C19A...`) and `seed/Codex.img` is 5,242,880 bytes / 5 MB
(actually 16,777,216, 16 MB). That is the same rot this document's own state
table had until this morning, one level further out, and the public inherits
this one directly.

## The objective, in Damian's words

> Get this other box of mine up and running a codex.os that I can host
> services from and tell other people it basically works as an OS off a
> stick I can burn.

That is the whole definition of victory. Not the public mirrors, not the
founding vision, not perfection. **A stick that boots his ASUS and is
recognisably an operating system, with a network on it.**

His shipping list: boot stick, compiler, services, UI, keyboards, mice,
monitors, USB, 3D graphics, drive management, networking.

**"We aren't looking for perfection, we are looking for good enough to
ship."** Every judgement call below resolves that way.

## The machine, and why it is the right one

From `docs/PM/Done/Projects/CODEX-OS-LAB.md`: ASUS TUF, i7-6700K
Skylake, 32 GB, **Samsung 850 EVO (SATA, not NVMe)**, GTX 970, AMI
Aptio V 2015 firmware, UEFI with CSM off.

Two pieces of luck worth naming, because they decide what we do NOT have
to build:

- **The disk is SATA**, and AHCI is METAL, so storage on this box needs
  no new driver. **Corrected 2026-07-29 (reek): NVMe is NOT absent.**
  `apps/works/GopNvme.codex` is an 18 KB driver, dispatched as
  med-kind-nvme 4, and `OsHardwareRoadmap` H3a records OVMF booting FROM
  an NVMe namespace with GPT, the FAT walk and the 2.15 MB seed all read
  through its own queues. The roadmap's summary table said ABSENT while
  its own H3a section said DONE; this plan inherited the wrong one. So a
  newer box would need that driver VALIDATED on silicon, not written --
  a smaller risk than "blocked before anything else", and worth knowing
  before ruling any other machine out. codex-vm has no NVMe model, so
  the battery is silent on it either way.
- **This box has already booted Codex.** `docs/PM/Milestones.md` records
  "Welcome to Codex" on it, 2026-05-07. `TheSilentKeyboard` records the
  first-boot ceremony running all three phases on it, keyboard included.
  The stick-boots claim is a regression to find, not a mountain to climb.

## Where we actually are

Verdicts are `OsHardwareRoadmap`'s own: METAL = proven on physical
hardware or real firmware, EMU = proven under codex-vm only, PROTOCOL =
framing real and transport untested, ABSENT = does not exist.

| Ship item | State today |
|---|---|
| Monitor | GOP linear framebuffer, 32-bit XRGB, CBF font -- **METAL** |
| Drive management | AHCI read+write, IDE PIO, GPT + FAT16 read/write -- **METAL** |
| Compiler | self-hosting hard fixed point on bare metal |
| Keyboard | **There is no PS/2 port on this board** (sitting Q3), so the METAL verdict this row used to carry was measured elsewhere. USB HID enumerates on the real xHCI and delivers nothing -- **the fleet's critical path** |
| Mouse | Follows the keyboard: no PS/2 port, so the pointer is USB HID and the PS/2 mouse leaves the ship (A3, decided) |
| USB | xHCI host + BOT + SCSI -- **EMU**, real-silicon validation pending |
| UI | guios desktop -- **EMU only, never rendered on metal**. `build/desk.ps1` makes it a dev-box build artifact (main 12355) |
| 3D graphics | software pipeline exists; GPU acceleration **ABSENT on metal** |
| Boot stick | **METAL. A1 closed 2026-07-29** -- a Codex payload boots the ASUS. The reboot-loop was our own flashing procedure destroying the GPT, not the payload; fixed main 12168 |
| **Network** | **ABSENT. NE2K only, and no real machine has had one for twenty years** |

**Three rows corrected 2026-07-31 (red) because this document contradicted
itself.** The table said the boot stick reboot-loops and that the PS/2
keyboard is METAL; the sitting section below, in this same file, says the
stick boots and that the board has no PS/2 port at all. The table was written
before the sitting and nothing re-read it afterwards. That is the failure
this project keeps paying for, in the one document every lane reads at init.

## The two tracks, and why they are two

Damian, 2026-07-29: *"we need network to work on the asus for sure.
That's how we demonstrate the repository protocol is actually viable and
that we can deploy a real service. It doesn't gate the usb work with the
stick."*

Two tracks that do not block each other, staffed in parallel from now.

### Track A -- THE STICK BOOTS AND IS AN OS

Everything the demo needs that is not the network. Almost entirely
bring-up and verification of code that already exists, which is why it
is the shorter track.

### Track B -- THE NETWORK

One driver that does not exist, plus the service that proves the point.
**This is the critical path for the ship.** Every other row on the list
is bring-up; this is the only write-from-nothing driver.

## THE SITTING HAPPENED, 2026-07-29. ALL FOUR ANSWERS ARE IN.

Attempt 2. `docs/HardwareSitting.md` has the ladder and the digests; this
is what came back. Two of these could not have been produced by any amount
of work on the emulator, which is what the trip was for.

**1. THE STICK BOOTS.** First successful boot of a Codex payload on the
ASUS TUF, and A1 is closed. Panel **1920x1080, stride 2048** -- the stride
is 128 pixels wider than the visible width, so this board really does pad
its scanlines and anything indexing rows by width will shear. Channel order
**correct** (cube blue, pyramid red, read off the glass), so the unread
`PixelFormat` field is not biting here and A6 can rely on colour.

**What made it boot was not the payload.** Every earlier stick carried an
invalid GPT by the time it reached the board, because our own procedure
destroyed it: an entry array below the UEFI 16 KB minimum, a one-sector
disagreement between `build-img` and `flash-usb` over the backup array, and
no volume lock during the write. Windows "repaired" the table into one with
no readable partitions, and **the instruction to EJECT the stick, which the
run sheet and the flasher both gave, was one of the triggers**. Fixed at
main 12168; a stick now survives a full remove-and-reinsert unchanged.
That is also the best explanation we have for why attempt 1 failed.

**2. THE NIC IS AN INTEL I219-V, AND TRACK B IS UNBLOCKED.**
`00:1f.6  8086:15b8`, revision 31, subsystem `1043:8672`, `B0=df440000`,
**`MAP=ok`** -- the register window is inside the 3 GB to 4 GB device range,
so it is reachable and `e1000-bar-verdict` accepts it. Station address read
live off RAL/RAH through the vendor-and-reachability gate:
**`78:24:af:d9:c8:23`, `AV=1`**. That is blu's N1, N2 and N3 together.

**There is a SECOND NIC and red asked to be told at once: a Realtek
`10ec:8168` at `06:00.0`, behind a bridge, `MAP=BELOW3G`.** The Intel part
is the one to drive. Also on the bus, none of it previously recorded: a
GTX 970 (`10de:13c2`), an ASMedia SATA controller (`1b21:0612`), and a
**second xHCI** (`1b21:1242` at `05:00.0`). 21 devices over four buses, so
the bridge walk was load-bearing rather than defensive.

**3. THERE IS NO PS/2 ON THIS BOARD.** The keyboard is USB; the firmware
presents it through i8042 emulation, and that emulation does not survive
ExitBootServices. Zero arrivals before the handback and zero after it, so
even letting firmware resume its legacy emulation gets nothing through.
Per val's own branch this is the "if no" case: **USB HID post-EBS is the
only input path this machine has**, and A3 grows accordingly.

**4. USB HID ENUMERATES AND DELIVERS NOTHING.** (This row read "AND THE REASON
IS SPEED" until 2026-07-31. The interval-encoding hypothesis it names is dead,
measured; the paragraph is kept because the `speed=1` observation in it is
still the one real difference from every passing run.)
Our stack addressed and configured the keyboard on the real Intel xHCI --
`uk-ok=y slot=1 dci=3`, `intel-route=y` -- and then `EPINT=0`, `SCANS=0`,
`REPORT` all zeros across all three phases with a key held down.

**The instrument is not blind, and that was checked rather than assumed.**
The same image under OVMF with keys injected returns `EPINT=12 SCANS=12
last=a0 FILE-WRITES=7`. The difference between the two runs is in a field
nobody was watching: **`speed=3` (High-speed) under QEMU against `speed=1`
(Full-speed) on the real keyboard.** Every test this path has ever passed
was against a High-speed device. xHCI encodes interrupt-endpoint intervals
differently by speed, so a driver computing the High-speed way for a
Full-speed endpoint programs a nonsensical polling rate, and the symptom is
exactly this one. **The speed difference is measured; the interval encoding
is a hypothesis.** It is testable on the dev box with a Full-speed bed and
needs no further hardware time.

**`disk=n` is NOT an MSC failure.** All four devices connected to the Intel
xHCI are Full or Low speed, so none of them is the boot stick, which is on
the ASMedia controller. `xhci-connect` took the FIRST xHCI it found and
stopped (**fixed 2026-07-30, reek's every-controller walk; this sentence is
the state AT THE SITTING and is why only one controller was ever brought up
that day**). Sitting question 4's storage half is therefore **unanswered rather
than negative**, and the driver needs to enumerate every controller. That
is the same defect shape as scanning bus 0 only and reporting NONE FOUND.

**Still open from the sitting:** one rung, and it is blocked on a code fix
rather than on a trip. Rung 3 (`msc-align.img`, the 64 KB TRB question) was
dropped on `disk=n`, which was a false negative, so it needs the board once
more **after** `xhci-connect` enumerates every controller. Everything else --
the Full-speed HID lead, the second-controller enumeration itself, the padded
stride, and three instrument defects of fester's -- is dev-box work.

### What the 2026-07-31 boot settled. Do NOT re-derive any of it.

Exact bytes, QR-decoded from KbdDiagProbe v9 (main 12506):

```
HOST run=1 cnr=1 ports=4 slots=64 HCC=0200eec1 xECP=512
CTL n=2 0:8086a12f 1:1b211242 2:0a02f7ee
uk-ok=y slot=1 dci=3 speed=1
EPINT=0 EP0=0 OTH=0 LATCH=0    trb=00000000 ring=751c2000
EP st=2 bi=8 iv=6 mp=8 es=8 rt=0 tt=0 sp=1
```

- **Two xHCIs**: Intel PCH `8086:a12f` ord 0, ASMedia `1b21:1242` ord 1.
- **Root port, no hub** (`rt=0 tt=0`), which kills the PSI hypothesis above.
- **`run=1`, `HCC=0200eec1` decodes clean.** The controller is running and
  capability reads are fine, so "we never brought it up" is closed.
- **`EP st=2` is OUR enum, not xHCI's.** `xhci-ep-ok=2`, so Configure
  Endpoint SUCCEEDED. It is not the xHCI "Halted" that the number resembles.

**THE HOST ROW WAS DESCRIBING THE WRONG CONTROLLER, and that is the finding
that matters most.** `usb-hosts` walks every controller and only
short-circuits once keyboard AND mouse AND disk are found. `disk=n`, so it
never stopped, and the last controller up (the ASMedia) overwrote diag cells
13-18. **`connected=0` and `intel-route=n` are the ASMedia's numbers**, not a
failure of the Intel, and `ports=4` is an ASMedia count -- the PCH has
fourteen-plus root ports. Fixed in KbdDiagProbe v10 (main 12522): the HOST row
now carries `id=`, an `ATTR` line names the controller each device came from,
and the line goes amber when the two disagree. Everything except
`uk-ok/slot/dci/speed` and the EP line was describing a controller we do not
care about, and nothing on the glass said so.

**The relocation-collision hypothesis is REFUTED on metal.** reek reproduced
it on the dev box with a proper three-arm control and fixed it (main 12519,
`xhci-reloc-base-for ord`); the fix is real and keeps. It is not this bug: the
board still returned all zeros with it in. **It was also refutable from
records we already had** -- `HardwareSitting.md:503` shows the ORIGINAL
sitting at `intel-route=y` with `EPINT=0 SCANS=0` while only one controller
was ever brought up, so no second relocation was possible and the keyboard was
already silent. A reproduction in emulation was allowed to stand in for
checking that the premise applied to the target. That cost a trip to the board.

**So the fault is on the Intel PCH alone**, independent of the second
controller: keyboard enumerated, endpoint configured, doorbell rung, ring
allocated, and zero completions of any kind. `connected=0` was never an
anomaly.

### The standing cut, reconciled 2026-07-31 by red. Each lane's own file has the detail.

**Every row of the 2026-07-29 cut that used to be here is CLOSED except one**,
so the table has been replaced rather than annotated: reek's every-controller
walk, blu's `net-driver-mac`, val's stride audit and `-gop-stride` bed, and
red's MDIC all landed on 2026-07-30. Perforce is the record
(`p4 changes -m 100 //Codex/main/...`).

**The exception is fester's ConOut gap, which this line claimed as closed from
2026-07-31 to 2026-08-02 and which is OPEN.** Item 2b offered two routes and
fester landed the other one (the liveness marks, 12209/12219); ConOut was never
routed. Verified in source 2026-08-02 by red rather than taken from the report:
`uefi-con-put-text` (`apps/works/UefiConsole.codex:358-361`) still ends in
`print-uni`; the `con-out` field is read once at `UefiConsole.codex:35` and every
other occurrence tree-wide is a literal `0` in a test record, so `OutputString`
is called nowhere. **It is the boot-3 blocker, and the dev console does not paint
on real firmware.** The gap is stated correctly further down this file, which is
the same summary-versus-body split as the step table above.

**The fleet has been OFFLINE since 2026-07-31; only red ran. Re-cut
2026-08-01 by red.** Every row of the previous cut is closed: fester's
`desk-parse`, val's `files-parse`, reek's second-xHCI bed, and red's battery
re-run all landed. **Nothing below needs the board and nothing below needs a
ruling.** The battery is green against reek's changes (1402 total, 1358 pass,
0 fail, 44 skip, 0 newly red, all three oracles pass).

| Lane | Head item | State |
|---|---|---|
| **reek** | **Give diag cells 0-39 a per-controller home, then resume the keyboard.** v10 makes the instrument SAY which controller it describes; it cannot make it describe the right one, because 0-39 are single-controller and the last bring-up wins. `usb-host-walk` already knows the keyboard's ordinal (it credits cell 44). Until this lands we have never once read the Intel's `run`/`reset`/`ports`/`HCC` | **The fleet's critical path.** `GopXhci.codex` is reek's. Dev-box work, no boot. Port-read audit still parked |
| **red** | **Release steps 3 and 4: poison build, then seed/map/img.** `seed/Codex.map` is stale (2026-07-28 18:31 against a seed of 07-29 08:36) and a stale map misresolves every crash | Steps 1 and 2 green. Holds the box for the poison run |
| **fester** | **B5.4**, and the three instrument defects | Box-free |
| **val** | **README audit**, now unblocked | Box-free |
| **blu** | **F6 then F7**, spec citations. Unchanged | Box-free |

**Before proposing another boot, land reek's row.** Two of the last three
readings were misread off fields that described the wrong controller, and the
board is expensive. The next trip should be able to answer a question rather
than raise one.

**Re-minting a golden is the trap in val's half and it is worth naming.**
`-Accept` records whatever the machine says, so a golden minted while a defect
is live becomes that defect's regression test. Confirm the new geometry is
INTENDED before re-deriving `files-parse.expected` from it.

**Correction to what the 2026-07-30 reset told reek and blu.** It said their
head item was "nothing, stop and wait". That was right while the ruling looked
imminent and it is wrong now that it has not come: each of those lanes has one
named, board-free, ruling-free item, listed above. Whether to spend a lane on
it is Damian's call, but "there is nothing to do" was not an accurate
description of either lane and should not have been left standing.

**The screenshot work is done.** Damian wrote a LinkedIn post off a screenshot
of val's desktop and it is posted (Damian, 2026-07-31). val's `GopFiles` hex
dump and `GopDesk` Welcome window were both sized to fit their own windows for
it (main 12424, 12430), and the flag red raised -- that the sidebar buttons are
a shared 24-consumer palette and re-theming them is a decision, not a tweak --
was **discharged without needing the decision**: val themed the desk chrome
from the desk's own constants and left `palette-terminal` alone.

**The pattern behind all of this, and it has now cost three defects and one
rung**: bus 0 only, the first xHCI only, stride equals width. **An instrument
that cannot express the failure will report success.** Tracked as a gap table
in `DeviceEmulationCatalog`.

**Two corrections to what this section said earlier the same day, both mine.**

- **The Full-speed HID bed was NOT missing, and the interval-encoding
  hypothesis is dead.** I wrote that codex-vm could not present a Full-speed
  device. It has done since before I wrote it: `xhci_init` sets
  `portsc[1] = 1 | (1 << 10)`, speed 1, and the comment beside it says it was
  changed from HighSpeed deliberately for exactly this reason. reek then
  measured `xhci-ep-interval` implementing both speed classes per xHCI Table
  6-12, and proved the instrument could say otherwise by forcing the HS
  encoding and watching the endpoint be refused. **I asserted a gap in my own
  file without reading my own file**, and it sent an agent to build something
  that existed. `speed=1` remains the one measured difference from every
  passing run, so the lead is re-aimed rather than closed: the next step is a
  READING, not a fix -- print the real keyboard's `bInterval` and
  `wMaxPacketSize` and the programmed Interval on the glass, because neither
  has ever been read off that board.
- **The `-gop-stride` bed was built TWICE, by val and by me, on the same
  afternoon.** Nothing was lost, and only because `p4 copy` refused a stale
  copy. `tools/codex-vm.c` now has an explicit one-owner-at-a-time queue, in
  `blu-workplan.md`, and it is red's to keep. Announce in your workplan when
  you open that file and when you land it.

**What can be demonstrated today, and what cannot.** A service answering
over the wire needs no keyboard, so B4 is reachable. Anything a person types
at or clicks on is behind reek's Full-speed fix. That is worth knowing before
promising a desktop demo.

---

## The four questions the sitting was sent to answer

Kept for the record; all four are answered above.

Damian can sit at the box today. `docs/HardwareSitting.md` governs, and
its rule stands: **every question that can be answered before the
sitting is answered before the sitting** (L-HUMAN).

**The sitting must come back with these four answers or it was wasted:**

1. **Does the stick boot now?** CL 11926 repaired an ABI violation in
   the UEFI firmware-call path: RBX was clobbered with the protocol
   pointer and never saved, which is the right shape for a payload that
   returns to BDS instead of running. Re-flash and re-measure before
   diagnosing anything else.
2. **The PCI inventory: vendor and device IDs for the NIC, the storage
   controller and the xHCI.** Track B cannot start against a guess. A
   Z170-era TUF board is most likely an Intel I219-V, which is e1000e
   family, but "most likely" is not a part number.
3. **Does the board have PS/2 ports, and are they live under Codex after
   ExitBootServices?** If yes, keyboard and mouse are METAL today and
   USB HID drops off the ship list. If no, USB HID post-EBS becomes a
   Track A blocker. **This one answer moves a whole workstream in or out
   of scope.**
4. **Does USB mass storage enumerate on the real xHCI?** EMU-only today,
   and it is how the stick carries its own filesystem.

The instrument for question 2 exists and is proven: the Option A probe
boots on real UEFI and renders QR codes on GOP (L-CHANNEL -- an output
channel independent of everything under test).

**Attempt 1 (2026-07-29) returned one bit** -- `pci-probe.img` was flashed,
the ASUS did not boot it, and every failure path in the stub ended at a
silent spin. Two things changed as a result: the stub now paints two
liveness colours (main 12073), and the artifact that flies must be on the
run sheet's ladder before it is flashed.

**red owns the stick flashing and the sitting sequence from 2026-07-29**
(Damian's direction) and has consolidated all four lanes' requests into
`docs/HardwareSitting.md`. **Attempt 2 is a four-rung ladder** and the
sheet is authoritative: rung 1 the combined inventory probe (PCI, then
USB, then PS/2 last), rung 2 SceneProbe, rung 3 MscAlignProbe conditional
on `disk=y`, rung 4 KbdDiagProbe conditional on the PS/2 answer.
`seed/Codex.img` is deliberately NOT on it while the ConOut gap stands.

**`Inventory.codex` is built and gated (main 12115) and HELD.** Its build
gate is closed; the rung is held because `iv-codes` bakes the PS/2 counts as
zeros and nothing re-renders the codes, so the record would report sitting
Q3 as a definite no. Routed to fester; the re-gate must run without
`-NoPs2`.

**Rung order changed: SceneProbe flies FIRST.** Measured from the payload's
own arithmetic, the QR record's capacity depends on panel height (0 codes at
640x480, 4 at 800x600, 12 at 1024x768) against a body already at 765 bytes
with seven devices. SceneProbe needs no input, works at any mode, and prints
the geometry that decides whether Inventory's record has capacity at all.

**R8 is NO LONGER a blocking precondition.** `seed/Codex.img` is off this
ladder, so R8 belongs to boot 3 alongside the ConOut gap and the missing
liveness marks. fester reports a refreshed img printing `OUT OF MEMORY`;
that is one measurement short of a defect, because the run sheet already
records a false OOM from this payload caused by a clobbered deck-pointer
register with the heap untouched. Read the heap high-water mark first.

### Answers from the sitting -- write these in the same day

| Answer | Value | Blocks |
|---|---|---|
| NIC vendor:device (class 02) | *pending rung 1* | **All of Track B.** Not `0x8086` means a different driver |
| NIC `MAP=` verdict | *pending rung 1* | B3. `BELOW3G` needs a page-table change first |
| NIC station address + AV bit | *pending rung 1, if Intel* | B3's bind, which refuses a clear AV bit |
| Q3: PS/2 present and live post-EBS | *pending rung 1 stage C* | val's A3 scope, both branches |
| Q4: `ENUMERATED disk=` | *pending rung 1 stage B* | reek's A4, and gates rung 3 |
| Channel order (cube blue, pyramid red) | *pending rung 2* | val's A6. Inverted is one `mov` in the stub |
| Crossing TRB `ok=` and checksums | *pending rung 3* | reek's A4 last code question |

## Track A, in dependency order

**A1. The stick boots.** Re-measure `seed/Codex.img` under real UEFI
after CL 11926. If it still loops, bisect the PE entry path in
`build/cdx-to-pe`: entry point, `AllocatePages` status handling, section
alignment. The Option A stub boots on the same firmware and is the
reference to diff against. *Blocks A2 through A6 and the whole demo.*

**A2. The desktop renders on metal.** guios is EMU-only. GOP itself is
METAL, so this is bring-up rather than invention, but it has never been
seen on real firmware. `GuiOsBringup.md` M2 is this row.

**A3. Input on metal.** PS/2 keyboard is METAL. PS/2 mouse is EMU and
untested beyond guios. Scope is decided by sitting question 3.

**A4. Storage on metal.** AHCI and GPT/FAT16 are METAL; USB mass storage
is EMU. The stick must mount and write its own filesystem on the real
xHCI.

**A5. The compiler runs on the box.** `BootRoadmap` names it: compile a
Codex program on bare metal, on the ASUS, from the stick. The single
most convincing thing in the demo, and mostly already true.

**A6. 3D on screen.** Software pipeline against the GOP framebuffer.
**GPU acceleration is out of scope for the ship** -- the host rasterizer
is a codex-vm device, not a driver, and writing a GTX 970 driver is not
a today problem. Good enough is a software-rendered 3D scene on the real
display.

**A7. Clean shutdown. RULED LAST, 2026-07-29 (Damian).** ACPI is ABSENT:
LAPIC and IOAPIC layout is assumed rather than discovered, and there is
no shutdown or reset. It is the last thing we do, once everything else
works, if there is time, or sooner only if Damian needs it for a
hardware sitting. **It blocks nothing and nobody picks it up ahead of
another row.**

## Track B, in dependency order

**B1. Identify the part.** Sitting question 2. *Blocks B2.*

**B2. The NIC driver.** Almost certainly Intel e1000e (I219-V). Well
documented, tractable, entirely absent here: MMIO BAR, descriptor rings,
link bring-up, RX and TX. **The longest single item on the ship, and the
critical path.** Start the parts that do not depend on the part number
now: the ring and descriptor machinery is common to the family.

**B3. The stack over the real NIC.** TCP/IP exists and runs over the
NE2K under codex-vm. Re-point it at the real driver and prove a
handshake against another machine on Damian's LAN.

**B4. Deploy a real service and serve the repository protocol.** The
point of the whole track: serve it off the box and have something else
talk to it. That is what makes the founding claim demonstrable rather
than theoretical.

## Discovered work, unassigned, NOT in the ship

Extracted from the workplans so nothing is lost and nothing is implied
to be in scope. **None of it gates the stick.** Take from here only when
a ship item is blocked and you are waiting.

- `__watchdog_tier1` and `__watchdog_tier2` are unreachable: both test
  `== 5,500,001` while the panic tests `>= 5,500,000` and the counter
  steps by one. Two diagnostic tiers no execution can enter.
- The default watchdog cannot fire: `wd-stale-threshold-progress` is
  5,500,000 ticks at 18 ticks per second, about 3.5 days. Any
  non-allocating spin is watchdog-proof. Needs a value from Damian.
- `audit-skips.ps1` globs `*.skip` only, so the `.slow` and `.fatal`
  sidecars have never been audited despite its header claiming they are.
- **Host tools re-derive "what is code" in regex instead of asking the
  compiler, and two of them got it wrong independently.** The compiler's rule
  is exact -- `Syntax/Lexer.codex:425` keys on `column == 2` and discards
  prose before tokenizing -- but every PowerShell tool that reads `.codex`
  restates that in a pattern, or forgets to. Found 2026-07-31 while removing
  prose from `opening.codex`:
  - `check-cdx-registry.ps1` counted a column-2 comment as a diagnostic raise
    site, so a sentence saying "the ONLY raise of `cdx-missing-cite` was
    deleted" kept the registered-but-never-raised check green for thirteen
    days. **Fixed, main 12435.**
  - `lint-unused-cites.ps1:58` is the same class and is **still open**: it
    tests `$body.Contains($name)` against `ReadAllText` of the whole file, so
    a prose mention of any name in a cited chapter marks that cite used and
    suppresses the warning. Bare substring, no word boundary, so short names
    match almost anything. It is wired into neither `build.ps1` nor
    `test.ps1`, which is why it has never bitten -- and why fixing it is
    cheap and safe.

  The one-line fix is to skip `^ [^ ]` lines, as `check-cdx-registry` now
  does. The real fix, worth doing only if a third instance turns up, is one
  shared "code lines of a .codex" helper the host tools call, so the column-2
  rule is stated once rather than re-guessed per script. **Both failures were
  toward silence**, which is why nothing noticed either.
- The `*-checked` operation family: ruled as `Maybe`-returning builtins,
  not a mode on the type. Unblocked, unstarted.
- `is-fusable-int-comparison` carries its own one-level unit strip, so a
  unit over a unit declines to fuse.
- `encode/Gltf` writes an accessor bounding box 1000x too large; the fix
  needs a JSON decimal `Json.codex` does not have, and adding a `JsonNum`
  variant would be absorbed silently by eleven `is otherwise` arms.
- `AI chapter SafeTensors` recognises I32 and I8 and can load neither.
- `<` `>` `<=` `>=` do not order Text; they compare operands as handed
  over, so `"a" < "b"` and `"b" < "a"` are both True. A language
  decision, not a bug fix.
- `ranked-text-insert` and `SkipListText` mutate in place, so a memo over
  either hands two callers one object.
- Cross-architecture file read, and the riscv-only failure cluster of
  roughly 90 rows. Parity work, not platform work.
- WiFi, Bluetooth, Intel HDA audio, power management beyond A7: ABSENT,
  and deliberately out of scope for this box. **NVMe is not on this list
  any more** -- the driver exists and is OVMF-proven (see the machine
  section above); it is out of scope because the ASUS disk is SATA, not
  because it is missing.
- Spark WebGPU studio. `spark-boolean-test`'s skip reason is false and
  the test fails for at least two independent reasons.
- Pure Codex VMX host, retiring `codex-vm.exe`.
- Agent acquisition bundled path.

## THE GATE ON EVERYTHING, ruled 2026-07-30 by Damian

> we aren't going to do any sitting until the keyboard works. the whole
> I/O thing needs both I and O.

**No sitting is scheduled and none is to be proposed.** This freezes A2 on
metal, A3, A4, A5, B3 and rung 3 at once: every one of them needs the board,
and the board needs input. It is not a pause in the work, it is a
re-ordering of it -- **input is now the fleet's critical path**, and the next
cut puts two lanes on it (reek: make the bed refuse what silicon refuses;
blu: an independent reading of the xHCI spec against what we program).

The O half continues: fester on B5.4 (the GOP payload becomes the real
`seed/Codex.img`) and val on GopDesk, both judged on the dev box.

## FILE CLAIMS. Announce here BEFORE you open one of these.

**Four items were built twice on 2026-07-30**, by four different pairs of
lanes, and every one of them was caught by luck rather than by process:
`-gop-stride` (val and red), the `xhci-diag` base (reek and red), the
`desk-loop` Esc fix (fester and val), and the 32-bit port-IN sign extension
(reek and red). Nothing was lost, and that is not a system.

The claims register lived in `blu-workplan.md`, which is exactly why it did
not work: it is a file three lanes had no reason to open. It lives HERE now,
in the one document every lane reads at init.

| File | Claimed by | Status |
|---|---|---|
| `tools/codex-vm.c` | FREE | second-xHCI bed LANDED 2026-07-31 by reek (`-xhci-two`, `-xhci-bar`, `-xhci-bar2`, `-xhci-no-disk`) |
| `apps/works/GopDesk.codex` + `DeskVm.codex` | val | live, the desk is val's |
| `build/cdx-to-pe.ps1`, `build-boot-img.ps1`, `apps/works/GopHandoff.codex` | fester | live, B5.4. `option_a_stub.asm` is DELETED (B5.4 step 4) and no MSVC is involved in any boot artifact now |
| `apps/works/GopXhci.codex`, `GopUsb*.codex` | reek | live, the keyboard |
| `codex/os/kernel/E1000e.codex`, `codex/os/net/**` | blu | live, B4 |
| `docs/HardwareSitting.md`, the low-memory cell map | red | standing |

**The rule, and it is two lines.** Before you open a file another lane could
plausibly want, add a row here and say so in your own workplan. When you land
it, set the row back to FREE. A claim you forget to release is a smaller
problem than a build nobody knew you were doing.

## Rulings owed by Damian

**NONE OPEN. The deadlock this section described was discharged on 2026-07-31
and this text is what it was replaced with.** Damian ruled that an instrument
read IS a sitting and is permitted, with the standing rule restated in his own
words: **do not use him as a test bench for what a doc or the dev box can
answer.** The reading was then taken. What it said is in "What the 2026-07-31
boot settled" below; keep that section and this one in step, because the whole
failure of the previous version was that the plan went one boot stale and left
two lanes queued behind a ruling that had already been given.

**The former text, kept only because the shape of the argument recurs:** the
ruling "no sitting until the keyboard works" had come to block the one reading
that would make the keyboard work. That was not a criticism of the ruling,
which was right when it was made and is what re-ordered the fleet onto input.
It is what the ruling turned into once the diagnosis arrived.

**Can the keyboard be fixed without a boot? THERE IS NOW A DIAGNOSIS, and it
reproduces the ASUS symptom exactly.** blu read the xHCI specification
independently of the model and found that we ASSUME the PORTSC Port Speed
IDs: Table 7-13 is only the DEFAULT mapping, and a controller that publishes
a Supported Protocol capability may declare its own. reek built the bed and
measured it on the shipping driver. Below a hub with declared PSI dwords, the
parent-is-high-speed test fails, the TT fields are left zero, and the endpoint
is never serviced: **enumerates perfectly, then silent forever, which is the
reported ASUS shape verbatim.** Both live findings are fixed (main 12388,
corrected 12409).

**What it does NOT establish is that the ASUS declares PSI dwords at all.** It
establishes that IF it does, we produced exactly the observed failure.

**That reading was taken on 2026-07-31 and it KILLED the PSI hypothesis, by
topology rather than by speed.** KbdDiagProbe v9 returned `rt=0 tt=0`: route
string zero, no transaction translator, so **the keyboard is on a ROOT PORT.**
The PSI/hub/TT work addresses a topology this board does not have. `sp=1` was
also read and is unchanged by the PSI fix. The fix stays -- it is a real
defect for any machine with a hub -- but it is not the ASUS's bug.

**Two candidate classes are now ELIMINATED rather than open**, so the earlier
version of this section is superseded: the periodic-schedule family (Interval
and MaxESITPayload are validated by VALUE against the descriptor, both MATCH
on all three topologies, with an arm that can say no) and Intel XUSB2PR
routing (modelled, and our write demonstrably takes effect).

**One withdrawn claim, recorded so nobody acts on it.** reek reported that
`xhci-bar-usable`'s 3-to-4 GB window had never been reachable and called it
the strongest remaining ASUS candidate. It was an artifact of a codex-vm
defect, not a property of the hardware, and reek withdrew it themselves. It
costs no boot.

**The former text of this section, kept only for the shape of the argument:**
Measured 2026-07-30: every named candidate is already modelled and every one
passes. `-xhci-hub-tiers 2` presents a full-speed device behind a high-speed
hub with a transaction translator; the model already refuses a zero
Interval, a zero MaxESITPayload and a routed device with no TT; and the
interval encoding was measured correct for both speed classes with an arm
that can say no. **The bed is green on every arm and the ASUS still returns
`EPINT=0 SCANS=0`.**

Two candidate classes survive, and they differ in whether a boot is needed:

1. **A shared misreading of the spec.** Model and driver came from one
   agent's reading, so a wrong value passes both. This is answerable on the
   dev box by an independent reading, and blu is assigned it.
2. **Intel companion routing.** `intel-route=y` says we performed the
   `XUSB2PR` write; nothing says it took effect, and a port that was never
   routed to the xHCI is indistinguishable from a device that enumerates and
   delivers nothing. reek is assigned modelling it, which tests OUR WRITE but
   cannot confirm the real PCH behaves as modelled.

**If both come back clean, the remaining candidates can only be read off the
board, and the ruling above and the fix are in tension.** The cheap escape
if that happens is that reek's endpoint-descriptor reading is already built
and calibrated (main 12236): four numbers -- the real keyboard's own
`bInterval`, `wMaxPacketSize`, route string, and the Interval we programmed
from them -- on a boot with no input required and nothing to demonstrate.
**That is an instrument read, not a sitting**, and Damian may want to treat
it differently from a demo. Nobody is to schedule it on that reasoning
without him saying so.

**Previously owed, both answered 2026-07-29.**

**The watchdog threshold: LEAVE IT ALONE, TRACKED (Damian).** A short
threshold is a problem when debugging, so `wd-stale-threshold-progress`
stays at 5,500,000 ticks (~3.5 days at 18 ticks/sec, which cannot fire)
for now. **The open obligation is a pre-ship one:** if there is time,
test some configurations and make sure we do not ship a kernel that
blows itself up. Nobody is to shorten it as a side quest in the
meantime; a watchdog that fires mid-debug costs more today than a
watchdog that never fires.

**Clean shutdown: LAST.** A7 above carries the ruling.

## What a green gate still does not mean

The gate is `build/build.ps1`: text round-trip, CDX hard fixed point,
BVT. **It has never executed a single instruction on Damian's ASUS.**
Every verdict marked EMU above is green under codex-vm and unproven on
the silicon we are shipping to. The gate is necessary and it is not the
demo.

## Cross-references

- `docs/HardwareSitting.md` -- the run sheet, governs the sitting.
- `docs/Designs/Active/Hardware/OsHardwareRoadmap.md` -- the driver
  inventory the table above is drawn from. Keep its verdicts honest.
- `docs/Designs/Active/Hardware/BootRoadmap.md` -- boot flow and the
  first-boot ceremony.
- `docs/Designs/Active/OS/GuiOsBringup.md` -- M2 is A2.
- `docs/PM/Done/Projects/CODEX-OS-LAB.md` -- the machine.
- `docs/PM/Active/Stories/LESSONS.md` -- L-HUMAN and L-CHANNEL both bear
  on the sitting.
