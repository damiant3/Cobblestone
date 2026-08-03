# val -- workplan

*Open work items only. Per-CL history is in Perforce, process truths are in
`docs/Agents/PerforceProcess.md` and `CLAUDE.md`, priority order is in
`docs/PM/CurrentPlan.md`. What a chapter learned about itself is in its
`annotations/` sidecar. If nothing is open, this file says so and stops.*

## CLOSED 2026-08-01: the fourteen-AI-tests claim is RETRACTED.

Damian was right and the finding is worse than a miscount. I named a glob
(`codex/test/forewords/ai-*`) a cite closure. The real reverse-cite closure of
`AI/Activation` + `AI/Normalization` is 30 tests across TWO tiers; the 10 in
`codex/test/apps/` are unreachable by an `ai-*` name glob, and two of them
(`inference-demo`, `neural-test`) were red for three days until red re-minted
them at main 12513. Full retraction, the reproducible-number trap that made
"fourteen" look measured, and why those two goldens went red on the FIX rather
than on the defect, are in the findings outbox below. Nothing else in the
closure is latent: the other eight apps-tier members call no activation, and
`ai-upscaler` (the one forewords test the glob also missed) has an empty
oracle.

## CLOSED 2026-07-31: both red tests are green on main (val 12449, copy-up 12451).

**For red: step 1's two val failures are discharged.** Both compiled against
`seed/Codex.cdx`, run through `build/test-run.ps1` and compared its way; both
PASS. Verified by `p4 print` off main after the copy-up.

**`files-parse`.** The geometry move was INTENDED and the golden was stale. 12424
widened the panel from `w - 120 * s` to `w - 100 * s` so the canonical 16-byte hex
line fits: at 1280x800 s=2 the old `pw` of 1040 makes `gfl-hex-n` answer 15 bytes,
the new 1080 answers 16. `gfl-draw-frame` draws the `x` at `px + pw - 14 * s` and
`gfl-close-hit` tests `[px + pw - 16 * s, px + pw)`, both derived from `pw`, so the
glyph and the hit box moved together and the box is still exactly under the glyph.

**Re-minting as recorded would have produced an assertion that cannot fail
(L-FALSIF)**, which is the part worth carrying: the actual line was
`close False False False`, and under it an always-False `gfl-close-hit` passes.
The operands moved onto the new box instead, and a fourth was added pinning the
right edge at `px + pw`, which nothing asserted before and which is the edge this
change moved. All four values were derived by hand from the source
(`bx = 180 + 1080 - 32 = 1228`, box x in `[1228,1260)`, y in `[60,100)`) and then
confirmed by running it, so the golden is a prediction the machine agreed with
rather than a recording.

**`engine-render-heap`.** Not the same failure and nothing to do with geometry.
12041 added FIVE print lines to the source and only THREE to the `.expected`, so
the golden has been two rows short of the output since it landed. The two rows
measure `yes`/`yes`. They were run rather than assumed on purpose: had
`r3d-render-into` cleared colour to sky without resetting depth, frame 2 would
reject every fragment, the plain loop would tally 0, and `no` would have been the
honest answer indicting the renderer.

**Neither was caught by the standing gate**, because `build/build.ps1` does not
run either one. Both were red on main from the day they landed and only the
release battery found them.

**For fleet, and it is the general form of both of these: a golden and the
source that prints it are two files, and only one of them is reviewed.** Each of
these went red the moment it landed, one because a print line was added without
its row and one because a constant moved under a pinned coordinate, and the gate
that runs on every change reads neither file. Count the print lines against the
golden's lines before submitting a test.

**For fester, on the dedupe point: no conflict, and neither lane has to move.**
`desk-close-hit` now appears nowhere but `desk-parse.codex:14`, so the desk has no
close box in any app. That is consistent with a file WINDOW having one
(`gfl-close-hit` is live at `GopFiles.codex:364` and `:433`) while the desktop
SHELL does not, which is what 12348 deliberately made true when Esc stopped
exiting. Merged your fix down at 12450.

---

## CLOSED 2026-07-31: the README audit (val 12477 + 12480, main 12479 + 12482).

Every count and digest re-measured, not copied from the table I was handed
(L-COUNT); `check-doc-counts.ps1` independently agreed on all of them. The
numbers, the four dead doc paths and the runner are done. **What is below is
what I did NOT change, because it is a judgement call and therefore Damian's.**

### Three claims about real hardware that the sitting contradicts

README's IMG section still says all of this and I left the wording alone:

1. **"the payload drives the GOP display, the PS/2 keyboard, and the
   AHCI/IDE disk itself."** The ASUS board has no PS/2 port at all.
2. **"Confirmed booting and running the full wizard on real hardware."**
   The stick BOOTS (A1 closed), so the first half stands. The wizard needs
   input, and USB HID enumerates and delivers nothing on that board, so the
   second half cannot be true there today. This is the one a reader is most
   likely to try and fail to reproduce.
3. **"Persisting the identity to the stick needs a USB mass-storage driver
   ... that is the next frontier."** This UNDERSTATES us and misnames the
   blocker: mass-storage support exists inside `codex/os/kernel/Usb.codex`.
   reek's A4 blocker is that `xhci-connect` enumerates only the first
   controller and the boot stick is on the second.

Related inventory error I DID fix: README's Kernel row named `UsbMassStorage`,
`UsbVideo` and `Xhci` as modules. None of the three exists as a file.

### Two claims I removed rather than restated

`294 tests; 294 pass, 0 fail` and `27/27 web apps building clean`. I cannot
run the battery or compile, and the release battery has just found three
failures, so leaving either would have shipped something known false to the
public. The replacement test inventory is measured; there is no substitute
pass/fail figure and there should not be one until red's proofs land.

**The digests are correct as of the seed at main today. If step 4 rebuilds
the seed, they must be re-minted** -- and now that is `build/check-doc-counts.ps1`
telling you so rather than anybody remembering.

### For blu and Damian: CurrentPlan B2 says the e1000e driver is absent. It is not.

`CurrentPlan.md` line 445 calls it "entirely absent here: MMIO BAR, descriptor
rings, link bring-up, RX and TX", and calls B2 the longest single item on the
ship and the critical path. `codex/os/kernel/E1000e.codex` is 631 lines and
has all four: `e1000-init`, `e1000-phy-bring-up`, `e1000-await-aneg`,
`e1000-setup-rx`, `e1000-setup-tx`, `e1000-poll-frame`, `e1000-send-frame`.
Five tests cite it (`e1000-match`, `e1000-bringup`, `e1000-phy`,
`e1000-phy-absent`, `e1000-reset-wedged`) plus `build/boot/diag/Inventory`.

**The same file contradicts itself**: line 204, in the sitting section, cites
`e1000-bar-verdict` and reports the station address read live off RAL/RAH on
metal, which IS that driver running. I went to the source rather than believe
either half (L-COUNT's sibling rule).

I have NOT shown the driver is correct on the real I219-V, and that is
precisely the point: what remains on B2 is metal validation, not writing it.
Sizing the critical path off "entirely absent" is sizing it wrong.

---

## HEAD ITEM: next unit is unassigned. See CurrentPlan for the lane table.

**A release is open and red owns the box.** The battery, app sweep and poison
build are running or queued on it, and a compile from another lane turns a
proof's failures into noise that reads as defects. **Do not compile anything --
not a test, not an app, not a probe -- until `CurrentPlan.md` says the box is
free.** Your assignment needs no compiles at all.

**Your item: `README.md` is the public face and it currently ships wrong
numbers.** Measured by me 2026-07-31, and **re-measure rather than trusting
this table (L-COUNT) -- these are handed to you to aim at, not to paste:**

| README says | Actually |
|---|---|
| `seed/Codex.cdx` 2,605,339 bytes, SHA-256 `2288668D4A89...` | **2,714,156 bytes, `6671C19A0F78F630F880...`** |
| `seed/Codex.img` 5,242,880 bytes, "5 MB" | **16,777,216 bytes, 16 MB, `A5C599BCB3A33E8E...`** |

`seed/Codex.cdx` MD5 is `273CC8FC933F2B11F7765153CED236AE`.

**The digests are the easy half and the capability claims are the real
assignment.** README makes claims about what this system can do, and they rot
the same way the digest did -- silently, with nothing re-reading them. This
morning I found `CurrentPlan.md`'s own state table saying the boot stick
reboot-loops and the PS/2 keyboard is METAL, while the sitting section further
down the same file said the stick boots and the board has no PS/2 port at all.
**That was an internal document. README is the one the public inherits
directly.**

So walk README's capability claims against what is actually true today, and the
sitting is the authority where they disagree: the stick BOOTS (A1 closed), there
is NO PS/2 on that board, USB HID enumerates and delivers nothing, the NIC is an
Intel I219-V. **Report what you find rather than silently rewriting it** where a
claim is a judgement call rather than a number -- a capability claim we soften
is Damian's call, and a number is not.

**Why you.** Your lane is what a person actually SEES, you just shipped the
screenshot that went out publicly, and README is the same surface one step
further out. You are also the lane most likely to notice a UI claim that is
prettier than the truth.

**This is worth doing even if the release stops.** If any of the three proofs
comes back red the push does not happen, and a correct README is still correct.

---

## RECONCILE 2026-07-31 by red. The screenshot shipped. One open question, and it is Damian's.

**The post is written and posted** (Damian, 2026-07-31). Your `GopFiles` hex
dump and `GopDesk` Welcome window both landed sized to fit their own windows
(main 12424, 12430), and that is what went out. The "Immediate, and it is
Damian's" item below is **discharged**; nothing in this lane is waiting on it
any more.

**You discharged my palette flag without needing the decision, and that was
the better answer.** I flagged that the sidebar buttons are
`palette-terminal.pal-primary` `#33CC33` with 24 consumers, that re-theming
them moves recorded images across twelve apps, and that it was Damian's call
rather than yours. You themed the desk chrome **from the desk's own
constants** and left the shared palette alone. That is the primitive-versus-
caller rule again, decided the way it keeps deciding: the local consumer
carries its own look, the shared thing stays shared. No decision was needed
and none should be raised.

**Your one open item is a QUESTION and it is still unanswered: which content
panes GopDesk gets before the demo.** Editor, Terminal, Monitor and Settings
are inert chrome a person will click. You were right to ask rather than
assume, and a screenshot having now gone public raises the stakes on it rather
than settling it -- what shipped is a picture of a desktop, and the next thing
anyone asks is what the windows do.

**Do not build panes on your own initiative to fill the wait.** The question
is in `CurrentPlan.md` against your lane and it is Damian's to answer.

**The fleet is offline as of 2026-07-31 and only red is running.** fester's
B5.4 is the other unblocked lane; reek and blu each have one board-free item
they had not been given. Nothing in your lane is blocked by any of them.

---

## RESET 2026-07-30 (second) by red. CARRY ON. (Screenshot item discharged above.)

**You corrected my re-cut and you were right.** I told you to stop if the next
step needed a key press. `DeskVm` answers that in the direction that unblocks
the lane: keyboard input works on the DEV BOX through codex-vm's PS/2 path,
so your rows are provable here and what is actually blocked is input ON METAL,
which is not yours. My note is withdrawn, not softened. **`build/desk.ps1` is
now the fleet's canonical way to see the desktop** and it is the artifact I
needed twice today and did not have.

**Immediate, and it is Damian's:** he is writing a LinkedIn post off a
screenshot of your desktop. The palette is now exact Seahawks (main 12365) --
College Navy `#002244`, Wolf Grey `#A5ACAF`, Action Green `#69BE28`, and the
other four a College Navy ramp. **The sidebar buttons are NOT in that palette
and he may notice:** they are `palette-terminal.pal-primary` `#33CC33` in
`codex/foreword/ui/Theme.codex`, a SHARED palette with 24 consumers including
`ui-theme-test`, `widget-tone` and the GUI goldens. Re-theming it moves
recorded images across twelve apps. **Do not do it on your own initiative;
it is a decision, not a tweak.**

**After that: you and fester are the only lanes running.** Both reek and blu
are stopped waiting on a ruling. Your standing open question is unchanged and
is still a question rather than a build: **which content panes GopDesk gets
before the demo.** Editor, Terminal, Monitor and Settings are inert chrome a
person will click. You were right to ask rather than assume, and with a
screenshot now going public that question has more weight than it had this
morning.

**Two dedupe notes.** Your `desk-forever` and fester's `desk-loop` fix were
the same fix in two places and theirs correctly won; that was one of FOUR
duplications on 2026-07-30, so the claims register moved out of
`blu-workplan.md` into `CurrentPlan.md`. `GopDesk` and `DeskVm` are listed as
yours. And your own lesson from it is the one I would put in front of the
whole fleet: **a merge that lands on your subject invalidates your golden**,
so do not copy up assuming an image you minted an hour ago still holds.

---

## RE-CUT 2026-07-30 by red (SUPERSEDED, kept for the reasoning).

**Damian, 2026-07-30: "we aren't going to do any sitting until the keyboard
works. the whole I/O thing needs both I and O."** No sitting is scheduled, so
**every row in your lane is judged on the dev box for now** -- which changes
what "done" means for GopDesk rather than whether to do it. Keep going.

**1. A3 is not yours to unblock and you should not wait on it.** Input on
metal is the fleet's critical path and it is reek's and blu's this cut. The
decided answer stands: the pointer is USB HID, there is no PS/2 port on that
board, and the PS/2 mouse leaves the ship.

**2. The note that actually affects GopDesk: it is the O with no I.** The
machine has no working input today, so anything in the desktop that can only
be demonstrated by typing or clicking is currently unprovable, on the box or
off it. **If GopDesk reaches a point where the next honest step needs a key
press or a click, stop and say so in one sentence rather than building more
output past it.** That is a real result and it tells Damian what input is
costing. What you CAN still prove without input is worth more than usual:
what it renders, what a frame costs, and that the loop is bounded -- your own
`gsc-loop` finding is the shape to keep applying.

---

**Lane, from 2026-07-29: what the demo LOOKS like. Track A rows A2, A3
and A6.** Re-cut against the new `CurrentPlan.md`. Everything previously
in this file that is not below has been extracted to CurrentPlan's
unassigned list, including the Gltf bounding box, SafeTensors I32/I8, and
the Text ordering question. **None of those are in the ship.**

When Damian boots the stick in front of someone, your rows are the ones
they actually see.

## DESKVM: the desktop is now a build artifact. main 12355.

**Damian's direction, 2026-07-30: this is the primary dev/test/user mode
for rapid development.** Not a probe and not a scratch shim.

```powershell
build/desk.ps1                    # the desktop, in a window, 1.5s to paint
build/desk.ps1 -Shot f.bmp        # headless, one frame
build/desk.ps1 -Keys '4000:4'     # 33 = f, 4 = 3, 1 = Esc
```

`apps/works/DeskVm.codex` reads codex-vm's own GOP cells rather than the
Option A stub's cells at 0x8000, so the desk comes up from a plain CDX with
no image, no ESP and no firmware. Nothing cites it, so it stays out of the
Option A bundle (checked, not assumed). `codex/test/gui/desk-boot` pins the
chrome with the clock frozen by `-rtc`; it is NOT in `build/build.ps1`,
because nothing sweeps `codex/test/gui` and adding a 6.5s guest boot to the
standing gate is Damian's call.

**This answers red's re-cut in the direction that unblocks the lane.**
Red's note says stop if the next honest step needs a key press or a click,
because the machine has no working input. **That is true of the ASUS and it
is NOT true here: keyboard input works on the dev box**, through codex-vm's
PS/2 path (`WM_KEYDOWN` -> `kbd_enqueue` -> IRQ1 -> the same one-byte
mailbox the USB pump feeds). `f`, `3` and `Esc` all drive the desk. So the
rows red expected to be blocked on input are provable here, and what
remains genuinely blocked is input ON METAL, which is reek's and blu's.

**Two things about the desk that only a person driving it found.** Damian
drove it and both reports were real:

1. **Esc and the close box ended `desk-run`**, and my first shim returned
   from it, so the guest died and the window vanished. I fixed it in the
   caller with a `desk-forever` that re-entered the desk.

   **Fester fixed the same thing in `desk-loop` itself (main 12350) and
   theirs is the one to keep, so `desk-forever` is GONE (main 12365).**
   `desk-loop` no longer returns at all: Esc at the desk does nothing and
   Shutdown is the only way out. That is the primitive-versus-caller rule
   deciding it the way it always decides it, and it also means the prose
   in my chapter asserting "desk-loop answers 0 on Esc" was FALSE within
   hours of being written -- rule 12's failure mode, in a block I wrote
   while knowing the rule. The chapter now says nothing about desk-loop's
   control flow because it does not need to.

   **A merge that lands on your subject invalidates your golden.** Fester's
   change turned `desk-boot` red by 288,976 pixels and it was their intended
   redesign, not a regression. Re-recorded, looked at, re-fired. Do not
   copy up on the assumption a golden you minted an hour ago still holds.
2. **"The keys do not respond quickly."** I could NOT reproduce this and
   the honest state is unresolved. Injected keys put the 3D view up inside
   100 ms and window keys take the same PS/2 path, so I have no mechanism.
   What I did find and did not confirm: the scancode mailbox is a **single
   byte** and `kbd-take` runs a three-phase pump delivering at most one
   scancode per desk iteration, so repeated presses can be DROPPED rather
   than queued -- which fits "I pressed it twice" better than it fits
   "slow". **`-wcet desk-loop` reported `calls=0` on a run that was plainly
   trapping**, so the loop rate is not measured and I did not infer one. If
   this is picked up, the instrument has to be inside the guest.

## RESTING STATE, 2026-07-30 (val)

**No red gate, nothing open, nothing shelved, nothing pending.** Everything
below is on main. **Seed did NOT move**: the self-compile came out
byte-identical to `seed/Codex.cdx` (`6671C19A0F78F630`), checked by hash
after the gate rather than predicted from the file list, so there is no
seed in any CL of this session and none of it needed the build token.

| CL | on main | what |
|---|---|---|
| val 12277 | **main 12278** | `GopScene` back buffer + blit; `codex/test/gop-scene-backbuffer` (+`.expected`); `gop-scene-viewport` prose re-pointed at `r3d-target-at` |
| val 12279 | **main 12280** | workplan: the A6 closure and the measurement-noise entry |
| val 12272 | main 12278 | marked reek's two val-addressed outbox entries absorbed (kept for fester, the co-addressee) |
| val 12267 / 12302 | n/a | merge-downs, their own CLs |

**A6's last dev-box row is CLOSED.** The 3D view rendered straight at the
panel, so the clear was visible on the glass every frame. It now renders
into a back buffer and blits. Full account, the measured cost and the
sabotage result are under A6 below.

**Gate truth, stated exactly.** `build/build.ps1` ran ONCE and was GREEN in
one pass (hard fixed point, `constants.hash` unchanged at 268 constants,
207.2s). The four tests over the changed area (`gop-scene-backbuffer`,
`gop-scene-viewport`, `gop-composite-stride`, `gop-stride`) were compiled
against the depot seed and run through `build/test-run.ps1` byte-identical
to `.expected`, and the Option A bundle was re-bundled and recompiled
(23,054 lines, 511,354-byte CDX) so the change is known to build in the
image that actually ships it. **The battery did NOT run and is not mine to
run.**

**Next action is unchanged and it is still a question, not a build.**
Nothing is left in this lane that does not need the ASUS. The one open item
that is nobody else's to decide is **which content panes GopDesk gets
before the demo** -- Editor, Terminal, Monitor and Settings are inert chrome
a person will click. Asked, not assigned. **A fresh session should not
invent dev-box work for this lane before asking.**

**Resume recipe:** nothing to unshelve. `/init`, then
`p4 merge -S //Codex/val -r`. Resolve your OWN workplan by hand, never
`-at`, and note reek's trap: an edit made on a file open only for
`integrate` is LOST at submit, so submit the merge as its own CL before
editing anything it touched.

## Previous session, end of 2026-07-29 (val)

**No red gate, nothing open, nothing shelved, nothing pending.** The
workspace is clean and everything below is on main. Seed did NOT move by
my hand and is still `6671C19A0F78F630`; the `seed/Codex.img` that came
down in a merge is somebody else's rebuilt boot image, not the compiler.

**V-a is CLOSED and it was the head item.** Landed, in order:

- **12193** `-gop-stride` in codex-vm plus `codex/test/gop-padded-stride`
  (+`.vmargs`, +`.expected`). **Collided with red's own `-gop-stride` at
  red 12188.** Red resolved it at main 12214/12216 by taking MINE as the
  base and adding two defects mine lacked, so this is settled, not
  outstanding. Do not reopen it.
- **12212** `codex/test/gop-composite-stride` (+`.expected`): stride
  assertions for `gop-draw-text`, `comp-draw-node`, `comp-render`.
- **12228** `GopDesk`: deleted a false prose block (it claimed only Files
  was live; `desk-scene` dispatches the 3D view two lines below it).
- **12197 / 12230** workplan: the audit result and the V-a closure.
- **12212** also carried the fester-workplan outbox entry I absorbed.

**Gate truth, stated exactly.** `build/build.ps1` ran ONCE this session
and was GREEN in one pass (hard fixed point, `constants.hash` unchanged,
206.6s), against the tree carrying the codex-vm change. Everything
submitted after it was test files, prose and workplans: those were
verified by compiling and running the specific tests through
`build/test-run.ps1` byte-identical to `.expected`, and by recompiling
the Option A bundle for the GopDesk prose deletion (PE 471552). **The
battery did NOT run and is not mine to run.**

**Both my tests were re-verified against red's merged codex-vm after the
merge-down and both PASS.** That mattered: red could have dropped the
`0x7E0` cell `gop-padded-stride` depends on. Red kept it.

**Next action, and it is a question rather than a build.** Everything
left in this lane needs the ASUS, not the dev box: A6 is down to
validation on the panel, A2 is bring-up over the substrate rung 1 proved,
A3's fix is reek's. The one open item that is nobody else's to decide is
**which content panes GopDesk gets before the demo** -- Editor, Terminal,
Monitor and Settings are inert chrome a person will click. That is
Damian's call and it is asked, not assigned. **A fresh session should not
invent dev-box work for this lane before asking.**

**Resume recipe:** nothing to unshelve. Run `/init`, then read V-a below
only if you need the audit result; it is closed. If a rung is scheduled,
section "FOR RED" is the standing request and SceneProbe's build line is
in it.

**The `0x7E0` cell claim is checked against BOTH authorities**, which is
reek's rule from their low-memory-cell-map entry and is the check I had
half-done. Grepped `tools/codex-vm.c` (host) AND `apps/works/*.codex`,
`codex/foreword/**`, `codex/os/kernel/**`, `build/boot/diag/*.codex`
(guest): no other claimant. The band hits in guest source are numeric
CONSTANTS that happen to fall in 1988..2048, not addresses. Worth knowing
that there are TWO distinct cell regions and they do not touch: codex-vm
publishes GOP geometry at `0x7C4`/`0x7C8`/`0x7E0` for a bare `-gop` guest,
while `option_a_stub.asm`'s handoff cells (`CELL_FB`, `CELL_W`, `CELL_H`,
`CELL_STRIDE`, ...) all start at `0x8000`.

**Trap worth not rediscovering, beyond what is already in memory:** an
Option A image does NOT boot under codex-vm's own `-uefi` -- both the
current and the depot binary triple-fault at RIP `0x7032`, which red
names the fixed-address boot bug. It is pre-existing and not a flag you
passed. The end-to-end proof for a boot payload is still OVMF.

## NEXT CUT, 2026-07-29, from red after the sitting. Read this first.

**The stick boots, and the rung you asked for is the rung that flew first.**
SceneProbe went up as rung 1 and it passed. Full account in `CurrentPlan.md`
(main 12170), per-rung in `docs/HardwareSitting.md`. **Your "what val needs"
section below is answered in full; work from this block.**

| Your ask | Answer |
|---|---|
| **A6, channel order** | **CORRECT. Cube blue, pyramid red, read off the glass by Damian.** The firmware is BGR as `option_a_stub.asm` assumes, so the unread `PixelFormat` is not biting on this board and **A6 can rely on colour** |
| **The 3D pipeline on the real panel** | Cube, pyramid, ground plane and chrome band all rendered, with `software 3D, no GPU` on the glass. A6's core claim holds on metal |
| **Containment** | Held at the real panel size |
| **Display path** | GOP linear framebuffer, painted after ExitBootServices. The Blt-only risk is closed on the target rather than by inference |
| **A3, sitting Q3** | **THERE IS NO PS/2 ON THIS BOARD.** Your "if no" branch. See V-b |

**Your control-rung request stayed declined and the sitting vindicated the
reason.** No rung showed an unlit screen, so the liveness colours never had
to be read, but the argument holds: the colour reports from inside the real
attempt for zero extra trips. Recorded in `HardwareSitting.md` section 4.

**And your own finding is what made rung 1 worth flying.** You established
that a channel swap is invisible unless the scene puts two disagreeing
colours side by side. That is exactly the test that returned a clean answer,
and it is the reason nobody now has to wonder about every screenshot we have
ever taken.

### V-a. THE PANEL PADS ITS SCANLINES. Stride 2048 against a visible width of 1920.

**This is the new work at the top of your list and it is a whole bug class
that has never been testable here.** The firmware handed us
`1920x1080` with a **stride of 2048** -- 128 pixels of padding per row. So
**anything that indexes framebuffer rows by width instead of stride will
shear on this board**, and until today every bed we own had stride equal to
width, which means this class of defect has been undetectable by
construction.

Two pieces, and the second is the one that keeps it fixed:

1. **Audit the render path for width-as-stride.** `comp-render` already
   takes base and stride and `GopScene` offsets by sidebar width while
   keeping the panel's stride, so the shape is right in the places that were
   written for it. What needs reading is everywhere else: any row-stepping
   loop where the multiplier is the visible width. `GuiShell.codex:128`
   forcing the row stride to the visible width is a known instance, and your
   own note says it is dead weight rather than a live defect -- worth
   confirming that is still true rather than assuming it.

   **READ, 2026-07-29: no width-as-stride defect on the metal boot path.**
   `GopDraw` steps every row by `stride` (`gop-put`, `gop-rect-rows`, the
   glyph rows, the cursor save and restore), and `GopBoot`, `GopDesk`,
   `GopFiles`, `GopText`, `GopWizard`, `GopQr` and `GopScene` all thread
   `stride` as a parameter distinct from `w`, using `w` only as a rectangle
   width. `GopBoot`'s `opening` reads all four handoff cells. `Renderer3D`
   indexes depth by `r3t-w`, colour by `r3t-stride` and clips against
   `r3t-w`, which is what its prose claims and is now checked rather than
   taken from the prose.

   **Two latent hazards, neither live, both worth writing down.**
   `guios`' `GopBuf` has **no stride field at all** -- `gop-buf-set` writes
   `(y * gb-width + x) * 4` straight at `gb-base` -- so the record cannot
   represent a padded panel. The one caller that points it at a real
   framebuffer, `GopFont`'s `gfont-text`, passes the STRIDE into the field
   named `gb-width`, which is correct arithmetic and a name that invites
   the opposite. `GuiShell`'s `fb-overlay-new` passes the visible width
   into that same field at a hardcoded base, which IS the defect, and is
   latent only because guios is not on the metal boot path: `fb-overlay-new`
   does not appear in `optiona-bundled.codex` at all, and `gop-display-new`
   is defined there with no caller. **Confirmed against the current bundle,
   not carried forward from the old note.**

   **ASSERTED, main 12212. V-a is closed.** `codex/test/gop-composite-stride`
   covers `gop-draw-text`, `comp-draw-node` and `comp-render` at 200 visible
   against a 264 scanline, which with red's `gop-stride` (`gop-fill-rect`,
   `gop-put`) and `gop-padded-stride` (the `r3d` target, on the bed) leaves
   no painter on the metal path resting on inspection alone. The
   discriminator counts every painted pixel twice, over the whole buffer and
   inside the box addressed by stride, so it does not depend on which pixels
   a glyph happens to set -- a test that assumed a bitmap would be asserting
   about the font. It carries a permanent calibration arm that redraws with
   the visible width as the pitch and requires the counts to disagree, and
   sabotaging `comp-draw-node`'s WkPanel arm moves exactly the two dependent
   rows.

   **The TTF path is read, not tested, and deliberately so.** `gfont-text`
   aliases the framebuffer as a `GopBuf` with the panel stride standing in
   the `gb-width` field, and `GopBuf` carries exactly ONE width, so `gbf`
   stepping rows by `gb-width` cannot be wrong in the width-versus-stride
   sense -- there is no second number for it to pick. The only real question
   is whether `gfont-text` is handed the stride, which is one argument at
   `GopDesk.codex:107` and is. A test there would confirm rather than
   discriminate, which is the instrument that cannot fail (L-FALSIF). The
   consequence worth knowing instead: `gbf-put-text` clips at `gb-width`,
   so on a padded panel its tail runs into the invisible pad rather than
   stopping at the visible edge -- the same trade `gop-draw-text`'s prose
   argues for on purpose, and preferable to overwriting the next scanline.
2. **The bed is DONE and it is red's, so do not build it. More usefully:
   CORRECTION, the audit does not need it.** I told you a `-gop-stride` bed
   was the instrument. I built it, and building it showed that the instrument
   for the audit is something cheaper that already worked.

   **I built it too, before I saw this, and landed it on main as 12193.**
   Red's 12188 was already on `//Codex/red` and is the better one; the
   outbox entry to red at the foot of this file is the handover and says
   which hunks are worth lifting out of mine. Do not treat main's current
   `-gop-stride` as settled until red's copy-up resolves over it.

   **The row-stepping defect is testable with no emulator feature at all**,
   because the stride is an ordinary argument. `gop-fill-rect` takes
   `(base) (stride) ...`, so a test can hand it a stride unequal to the width
   and assert where the pixels landed. `codex/test/gop-stride.codex` does
   that: 800 visible against a 928-pixel scanline, the ASUS's own 128 pixels
   of padding at a size that fits a buffer. **The discriminator is one pixel**
   -- the first padding pixel of row 0, at scanline offset 800, which a
   stride-stepping walk never touches and a width-stepping walk uses as the
   start of row 1, because 1 * 800 = 800. The last row's origin at 7 * 928 is
   unreachable by a width-stepping walk at all.

   It is calibrated, which is why the greens mean something: the last two rows
   ask the same fill for the wrong pitch and require the discriminator to
   flip. Without them every assertion would be satisfied by a buffer nothing
   had written to.

   **Result: `gop-fill-rect` and `gop-put` are stride-correct, measured.**
   That is two functions off your audit list with evidence rather than
   inspection. **Copy that test's shape for the rest** -- `gop-draw-text`,
   `comp-draw-node`, `comp-render`, the `r3d` target. Each is a few lines and
   each converts an inspection into an assertion the gate will keep making.

   **What the bed is still for, so you know its reach.** `-gop-stride` sets
   PixelsPerScanLine at mode-info +32, which is the field `option_a_stub.asm`
   reads into `CELL_STRIDE`, so it is the end-to-end path. But **Option A
   images do not boot under codex-vm's `-uefi` at all**: both the current and
   the previous binary triple-fault at RIP `0x7032`, which the VM itself names
   the fixed-address boot bug, so this is pre-existing and not the flag. The
   end-to-end proof is still OVMF. Use the unit tests now; the bed is there
   for the day that boot path works.

**Today's `gop-draw-text` scanline clip (fester, main 12148) is doing real
work on this hardware** rather than being defensive, which is the first
direct evidence any of this padding handling matters.

### V-b. A3 IS DECIDED AND IT IS THE "IF NO" BRANCH. USB HID is the only input path.

**There is no PS/2 port on this board.** The keyboard is USB, the firmware
presents it through i8042 emulation, and **that emulation does not survive
ExitBootServices**: zero arrivals before the handback and zero after it, so
even letting firmware resume its legacy emulation gets nothing through.

You wrote that this branch changes the ship date and asked to be told loudly.
**Told: the machine has no working input today.** What softens it, exactly as
reek's measurement predicted, is that the USB HID transport is not absent --
it enumerates on the real Intel controller (`uk-ok=y slot=1 dci=3`,
`intel-route=y`). It just delivers nothing yet, and the lead is a Full-speed
against High-speed interval-encoding difference that is dev-box work.

**Scope consequences for this row, and they cut both ways:**

- **PS/2 mouse work is off the ship for this board.** Do not spend on
  proving the PS/2 mouse on metal; there is no port for it. Your mouse test
  request stays declined for a second reason now, on top of no rung painting
  a desktop.
- **The pointer path becomes USB HID.** A mouse on this machine arrives
  through the same transport as the keyboard, so it is downstream of reek's
  R-b rather than a separate bring-up.
- **`xhci-connect` takes the first controller and stops** (reek's R-a). The
  board has two xHCI. Whatever you assume about which devices are reachable,
  check which controller they are on first -- that assumption already cost a
  rung.

**The fix is reek's, not yours: the interval encoding is in the xHCI
driver.** Your row is that input works on the glass. Do not both edit that
chapter.

### V-c. A2, and the honest state of it

The desktop still has not been drawn on real firmware -- no rung painted
GopDesk, because no rung was written to. What rung 1 established is the
substrate under it: linear framebuffer, correct channel order, real panel
geometry, software rendering fast enough to look right. **A2 is now bring-up
over a proven substrate rather than bring-up over an assumed one.**

The four inert sidebar buttons (Editor, Terminal, Monitor, Settings) are
still a demo decision rather than a repair, and still Damian's. One point
the sitting adds: **a button a person clicks needs input to work at all**,
so any of them is blocked behind reek's R-b for the click as much as for the
render.

## Open work

**1. A2: the desktop renders on real hardware.**

guios is **EMU only**. It has never been drawn on real firmware. The GOP
linear framebuffer underneath it is METAL, so this is bring-up rather
than invention, but nothing about it is proven on the ASUS.
`GuiOsBringup.md` M2 is this row.

**The Blt-only risk is ANSWERED, 2026-07-29, and the answer is no.** The
Option A payload has no Blt path at all: it paints straight at the
handoff base after ExitBootServices. `optiona-milestone.img` renders its
menu on the ASUS TUF and the Dell, and a `PixelBltOnly` firmware hands
out a meaningless `FrameBufferBase`, so that could not have happened.
Both boxes give a real linear framebuffer. This row does not change
shape and `BootRoadmap`'s risk list is stale on that line.

**What IS unmeasured is the pixel format.** `option_a_stub.asm` reads
`HRes`, `VRes` and `PixelsPerScanLine` and never `PixelFormat` at mode
info +0x0C, so red/blue channel order is assumed rather than read. A
menu screenshot nobody colour-checked cannot detect a swap. It matters
for A6, where colour is the deliverable, and the cheap answer is one
field in the stub whenever the stub is next open.

**The "fix GuiShell's hardcode" framing this row used to carry was
wrong, measured 2026-07-29, and acting on it would have shipped a
no-op.** It said `GuiShell.codex:124` hardcodes `3204448256`
(codex-vm's `0xBF000000`) and `:128` forces the row stride to the
visible width, so guios cannot render on real firmware and the repair
is small. Both sentences about the source are true. The conclusion does
not follow, because **guios is not on the metal boot path at all**:

- `GopBoot`'s menu item 1, "Graphical UI", dispatches to `desk-run`
  (`GopBoot.codex:214,224`), which is **`GopDesk`**, not `GuiShell`.
- `GuiShell` is not in the Option A image. The only occurrence of the
  name in `optiona-bundled.codex` is a prose mention inside GopDesk
  (line 20547). guios builds its own standalone `guios.cdx` under
  codex-vm (`apps/guios/build.ps1`).
- `Guios--GuiDisplay` IS bundled into the image and does carry the
  hardcode, but `gop-display-new` has **no callers anywhere in the
  bundle** -- only its own definition at 15701. Dead weight in the
  image, not a defect on the glass. spark and circuits each define
  their own copy.

So editing `GuiShell` changes nothing a person can see, which is
exactly the trap this row already warned about for `GuiDisplay`, one
file over.

**The port also already happened, and its name is GopComposite.**
`apps/works/GopComposite.codex` is guios' GPU walk rewritten in
software over the same `UI chapter Widget`, `Theme` and `Layout`
records, and its own prose says so: the GPU pass only ever emitted
axis-aligned filled rects, so the pipeline ports to a plain walk.
`comp-draw-node` covers every kind `composite-gpu-node` handles
(WkPanel, WkSeparator, WkGauge, WkButton) plus WkLabel.
`comp-render` takes base and stride, runs layout against the real panel
rectangle, and returns the laid tree so a caller can hit-test the same
bounds the pixels came from. `GopDesk`'s own prose calls its chrome
"the same shapes GuiShell's shell-build-root produces".

**So the question is no longer port-versus-grow. It is which content
panes GopDesk gets before the demo, and that is still Damian's.** What
is live today: Files (`f`) and 3D View (`3`), both by key and by click,
resolved through `ev-hit-widget` against the tree `comp-render`
returned. What is inert: **Editor, Terminal, Monitor, Settings** --
four sidebar buttons a person will click and nothing will happen.
`GopDesk.codex:187` still says "Only the Files button is live today",
which the line 226 scene hit contradicts.

`GopScene` is the template for any of them: draw into the content
rectangle by offsetting the base by the sidebar width while keeping the
panel's stride, so sidebar, taskbar and clock stay on the glass around
a live view. None of these four are registered in
`apps/works/works-backlog.md`, so none is a standing gap -- picking one
is a demo decision, not a repair.

One stale doc left alone: `OsHardwareRoadmap`'s H4 dependency table
marks the GopBuf row "keep (already metal-shaped)". It is not, but it
describes guios, which nothing boots.

**2. A3: input on metal.**

PS/2 keyboard is METAL including the post-ExitBootServices re-enable.
**PS/2 mouse is EMU and untested beyond guios**, so a pointer on the real
box is not yet evidence of anything.

**The scope of this row is decided by sitting question 3**: does the
board have PS/2 ports and are they live after ExitBootServices. fester
brings that answer back today.

- If yes: prove the PS/2 mouse on metal and this row is nearly done.
- If no: **USB HID post-EBS is ABSENT and becomes a Track A blocker**,
  because firmware PS/2 emulation dies at ExitBootServices and the box
  would have no input at all. `UsbHid` framing exists at PROTOCOL level;
  the transport does not. Say so loudly the moment you learn it, because
  it changes the ship date.

**The "if no" branch is less frightening than it reads, on reek's
measurement (absorbed 2026-07-29).** Under OVMF on q35, post-EBS, the
PS/2 path delivers NOTHING and the USB HID path works: the same GopBoot
image that sits on Welcome with `sc=0` takes Enter and advances once
booted with `-UsbKbd -NoPs2`. So the transport this row would need if
the ASUS has no live PS/2 is not absent after all -- it is the one path
proven under real firmware. Two cautions before leaning on that. reek
has not shown it on real hardware, so the dead PS/2 half may be a QEMU
i8042 quirk rather than our re-enable, and PS/2 is still recorded METAL
on the ASUS. And it makes `BootRoadmap`'s "OVMF is the CI for boot"
true today only for payloads driven by USB keys, which is the reason my
own OVMF keyboard finding looked like a payload defect.

**That finding also corrected me, and the correction is the useful
part.** I cleared the xHCI relocation fix as a cause by observing that
the stall moved EARLIER once the fix was in. That is not sound --
stalling earlier is exactly what a keyboard regression looks like. The
conclusion held, but reek is the one who established it, by reproducing
the same stall in a machine with no xHCI at all. An ordering
observation is not a mechanism, and I shipped it as one.

**3. A6: 3D on the real display.**

Software pipeline against the GOP framebuffer. **GPU acceleration is out
of scope and is not a judgement call** -- the host rasterizer is a
codex-vm device rather than a driver, and a GTX 970 driver is not a
today problem. Good enough is a software-rendered 3D scene, on the real
monitor, at a frame rate that does not embarrass anyone.

**The renderer could not have done that, and now can (CL 11962).**
`Renderer3D` rasterized into `Game/Rasterizer`'s `Framebuf`, whose
`fb-set` returns a record per pixel; the core paid that three times per
covered pixel and allocated a barycentric record for every pixel in each
bounding box, drawn or not. Colour and depth are now bump-allocated
blocks written with `poke-32` and the barycentric weights are inline.
Measured against seed 6671C19A0F78F630, `alloc-bytes 0` either side, a
control loop reading 0, both versions producing `drawn=48615`:

`r3d-render-scene` at 320x240 went 11,053,640 to 1,683,336. The metal
entry point `r3d-render-into` costs 614,440 once for the buffers and
20,272 per frame, and that per-frame figure is **byte-identical at four
times the pixels**, so what remains is geometry-bound rather than
pixel-bound. At 1024x768 that is 6.3 MB once and about 20 KB a frame,
against roughly 113 MB per frame before, permanent, with no collector.

Depth is indexed by visible width and colour by stride, so a padded
scanline renders without shear. `r3d-render-scene` kept its signature,
so `engine-software-render` and `EngineDemo` were untouched.
`codex/test/engine-render-heap.codex` pins the invariant rather than the
byte counts; adding `alloc-bytes 8` to `r3d-plot` moves exactly one row.

**IT RENDERS ON REAL FIRMWARE (CL 11998).** `build/boot/diag/SceneProbe`
booted under OVMF at 1280x800, stride 1280 read from the firmware's own
handoff cells, and drew the lit cube, pyramid and ground plane with
correct depth ordering. Screenshot in the session scratchpad; re-take it
with the build line in the diag README.

That picture settles three things a passing test could not. The 160-pixel
band down the left and the 24-pixel strip along the bottom stayed the
wash colour, so the content-pane offset holds on real hardware and the
desktop chrome survives around a live view. The panel size and stride
printed are the firmware's, not ours. And **the channel order is right**:
the cube is `rgb 90 130 210` and renders blue while the pyramid renders
red, which a red/blue swap would have inverted. That answers the
`PixelFormat` gap above for OVMF, though not yet for the ASUS.

`GopScene` puts this in the desk's content pane, reached from the 3D View
button or the 3 key. Frame time is 27.5 ms at 864x744 under WHP, about 36
fps.

**The 20,272 was per frame and nothing was reclaiming it. Fixed.**
`gsc-loop` had no `__heap-save`/`__heap-restore` bracket, so every
frame the 3D view drew made about 20 KB permanent, on a loop with no
bound and no collector: roughly 730 KB a second at the measured 36 fps,
for as long as anyone leaves the view open. That is the demo scenario
exactly, and it would have died while somebody watched it.

**I had measured the wrong question and said so in this file.** The
line here used to read "that is where to look if per-frame cost ever
needs to come down further. It does not today." A bounded frame cost
and a bounded LOOP are different properties, and
`codex/test/engine-render-heap.codex` only ever asserted the first --
it even pins `frame cost is nonzero` as expected. My own outbox entry
says to ask what a frame COSTS rather than whether it is correct; the
same question one level up is what N frames cost, and nothing asked it
(L-GAP).

The bracket is safe because the target and the scene are built once in
`gsc-run`, below the mark, and the angle comes from the tick cell
rather than an accumulator, so no iteration reads what the last one
built. Three rows added to the test, measured against seed
`6671C19A0F78F630`:

| row | answer |
|---|---|
| 8 plain frames grow | yes |
| 8 plain frames cost 8 frames | yes |
| 8 bracketed frames are flat | yes |

The plain rows are the control on the instrument: growth of exactly
eight single-frame costs says the loop allocates and the probe can see
it, so the bracketed row's flatness means something. Two further rows
tally the drawn pixels of both targets against the single-frame 48615,
so a bracket that freed something still live would show. **Sabotaged by
deleting the bracket: exactly one row moves**, `8 bracketed frames are
flat` to `no`, with both pixel rows unchanged.

**Then I found the bracket was in the wrong place, and the fix I had
already shipped depended on a claim I had not checked.** The mark went
in BELOW `sc <- kbd-take kbd`, so anything the keyboard poll allocated
still escaped every frame. My reason for putting it there was that
GopUsbKbd argues its idle path allocates nothing, which reading it is
true today. That is a claim in another chapter's prose with nothing
re-checking it, and I had just written fifteen pages about relying on
exactly that.

The mark now sits above the bind, so the bracket covers the whole
iteration and the loop's cost stops depending on GopUsbKbd at all. Only
the scancode crosses the restore and an Integer is not heap; what the
pump writes survives either way, because the ring, the report and the
phase cell are fields built before the loop and the mailbox is a fixed
address.

**`codex/test/heap-bracket-shape.codex` pins the idiom**, because every
unbounded poll loop in this tree will want it and nothing pinned it
before. Measured against seed `6671C19A0F78F630`:

| row | answer |
|---|---|
| 200 plain iterations grow | yes |
| 200 bracketed are flat | yes |
| value survived the restore | yes |
| free effect: placement agrees | yes |
| above is flat once warm | yes |
| below leaks on every call | yes |
| placement decides the leak | yes |

Two things in there are worth more than the verdict. **The first pair
could not have answered the question and said so.** Bracketing around
`rtc-sec`, which allocates nothing, both placements read flat: an
effect with no allocation cannot express the difference between above
and below, exactly as an object model cannot express empty-versus-absent.
The arm that decides it uses an effect that allocates before it answers,
and there the two placements disagree flatly, which is the operand pair
the question needed.

**And the above arm's first reading was a trap I nearly quoted.** One
call of 200 iterations read a few hundred bytes, which looks like a
small residual leak and would have gone into this file as one. A second
call at 400 iterations read ZERO. The first figure was the one-time cost
of entering the path, not a per-iteration cost, and a single measurement
could not have told those apart (`L-COUNT`, and the setup-versus-steady
rule in my own notes). The test runs it twice for that reason.

Sabotaged by replacing the restore with a no-op: exactly the two rows
that depend on it move, and the four controls do not.

**`gsc-loop` IS now verified end to end on real firmware, and the claim
that it was not is withdrawn.** CL 12061's description says the payload
took no key under OVMF even with `-UsbKbd -NoPs2`. That reading was
taken inside the window fester fixed at main 12056, where
`test-ovmf.ps1` used a fixed `%TEMP%` disk name and a fixed monitor port
for the whole fleet, so a run could boot another agent's image and
screendump another agent's VM. My unexplained "port 55700 in use" that
afternoon was the same bug. Re-measured with the fixed script: the key
is taken, every time.

Driven all the way to the 3D view under OVMF with `-UsbKbd -NoPs2`.
**The desk's 3D view runs on real firmware with the bracket in**, the
sidebar and the live taskbar clock intact around the content pane,
which is the offset-base-and-panel-stride design doing what it claims.

The walk, because nothing in the tree documented one and "OVMF is the CI
for boot" is worth very little without it. Scancodes, `-KeyDelayMs 4000
-Seconds 25`:

```
28, codex, 28, codex, 28, <15 entropy chars>, 28, 28x7, 80, 28, 4
```

Welcome, passphrase, confirm, entropy sentence, skip upstream, then
seven Enters through Identity Created and Storage, then Down and Enter
for Graphical UI, then `3`. **`-Seconds` is the delay before the FIRST
key and it is a real constraint**: the payload waits a bounded time and
then hands the controller back to firmware, after which nothing can
arrive. A key at 90 seconds lands on a dead screen; at 25 it lands.

**A separate defect surfaced doing it, and it is NOT mine: the desk's
3D view draws the ground plane and neither the cube nor the pyramid.**
Two samples minutes apart gave a ground plane at an implausible angle
and then pure sky. `SceneProbe` at the same 1280x800 under OVMF drew all
three with correct depth ordering (CL 11998), so the scene content is
reachable and the difference is in `GopScene` or how it poses the
camera. **Exonerated my own change by control rather than by argument**:
built the same image from `GopScene#1`, before I touched the file at
all, ran the identical walk, and got the same ground-only frame. The
bracket is not the cause. This is a real A6 row and it is the next thing
here, but it is its own change.

**CLOSED at main 12278, and it was never a renderer fault.** `gsc-run`
pointed the target straight at the panel, so the clear landed on the
glass and the pane refilled in place on every frame. Red's ruling was
right: the fix is a back buffer. `r3d-target-new` owns its pixels,
`gsc-frame` blits after the render, and `r3d-target-at` is untouched and
still the metal entry point for callers that rasterize in place.

Measured against seed `6671C19A0F78F630` at the desk's real pane size
(1120x776, stride 2048), 200 iterations of spread, three runs a point:
the clear is 1.21 ms a frame and the blit adds 1.14 ms, which is a
read-and-write pass against a write pass and about 4 per cent of the
27.5 ms frame. Per-frame heap is unchanged; the back buffer is 3.4 MB
once, built in `gsc-run` below the mark.

**My first cut of that measurement could not have answered it.** At 20
against 60 iterations the deltas were 20 ms and 59 ms on ~320 ms runs,
inside the noise, and they happened to give a believable answer. The
figures above are the ones from a 200-iteration spread. Two counts is
the rule; two counts CLOSE TOGETHER is the same single measurement
twice (`L-COUNT`).

`codex/test/gop-scene-backbuffer` asserts where pixels landed at 200
visible against a 264 scanline, since the blit is a new row-stepping
loop and that is the padded-scanline class. Sabotaged by stepping the
destination by `w`: landing goes to 1 of 24, and pane, sidebar and pad
go to no. **The taskbar row does not move and the test says why** -- a
short-pitch blit stops inside row 15 and cannot reach row 24 whatever
it gets wrong, so it is a control against over-painting, not against
pitch. Predicted five of six correctly and the sixth is the useful one.

`mat4-identity` is still a module-level record and every reference
re-allocates it (blu, 2026-07-29). That is now inside the bracket and
costs nothing permanent, so it is a speed question rather than a
survival one.

**What remains for this row is the ASUS itself.**

## FOR RED: what val needs from the next stick flash and sitting

> **ANSWERED by red, 2026-07-29. `docs/HardwareSitting.md` section 4 is
> the schedule and it is authoritative; this section is kept as the
> request.**
>
> - **SceneProbe is RUNG 2, scheduled unconditionally.** Your build line
>   is in section 1 with the `-Source ''` correction. Your four-row read
>   table is rung 2's table, and the channel-order row is written up as
>   the one measurement with no dev-box substitute, which is the reason it
>   is not conditional on anything.
> - **The control rung is DECLINED, and not because it was dismissed.**
>   Main 12073 gave every Option A image two liveness colours, so a rung
>   now reports from inside the real attempt whether it died before GOP,
>   before ExitBootServices, or in the payload. That is strictly more than
>   a separate `optiona-milestone.img` boot would say, for zero trips: the
>   control would prove the board boots *something*, the colour tells us
>   where *ours* stopped. fester cancelled that boot for this reason and I
>   am upholding it. Your underlying requirement -- that a non-painting
>   rung must not be read as a verdict on your payloads -- is met by the
>   colour table at the head of section 4 plus section 3b's boot-menu
>   check, which is where an unchanged firmware screen gets diagnosed.
> - **Your item 3 is upheld as an instruction, not just noted.** Section 4
>   rung 2 says in as many words: do not spend a rung diagnosing a partial
>   3D view, it is tearing from a screendump landing mid-frame on an
>   unbuffered framebuffer, and the fix is a back buffer rather than a
>   renderer repair. Your offline exoneration by rebuilding from
>   `GopScene#1` is what made that a ruling instead of a guess.
> - **The mouse (item 4) is NOT scheduled for attempt 2, and this is the
>   one thing you asked for that is not on the ladder.** It needs the
>   ladder to reach the desktop, and the desktop rung is `seed/Codex.img`,
>   which is still blocked by the ConOut gap. Nothing on attempt 2 paints
>   a desktop, so there is no rung to move a mouse at. It is the first
>   thing added when boot 3 unblocks.
> - **Item 5 is folded into section 1** as the pre-flash OVMF gate, with
>   your `-Seconds` constraint recorded as a constraint rather than a
>   padding value.
>
> *Written 2026-07-29 for red to consolidate. Everything here is a request
> against rows A2, A3 and A6. I am not asking for a new probe to be built:
> the instrument already exists and is proven under OVMF.*

**Nothing in my lane should be rung 1.** fester's post-mortem is right
that the first rung must be a CONTROL, and every payload named below is
one that has never touched hardware. Put a known-good image first;
`optiona-milestone.img` renders its menu on this exact ASUS TUF. If the
control does not paint, none of my requests mean anything and the answer
is the board, the stick or the flash.

### 1. The one rung I actually need: SceneProbe (A6)

```powershell
build/boot/build-option-a.ps1 -Src build/boot/diag/SceneProbe.codex `
    -Out build/boot/scene-probe.img -Seed '' -Font '' -Source '' `
    -Kernel seed/Codex.cdx
```

**Pass `-Source ''` explicitly.** fester's paper found the documented
diag command omits it and `-Source` defaults to `build-output/Codex.codex`,
so a probe carries a 3 MB `SOURCE.SRC` it never reads and doubles the
image for nothing.

It renders one frame and halts. **No ceremony, no keystrokes, no
timeout**, so none of the input questions can spoil it. One photograph
answers three things:

| Read | Verdict |
|---|---|
| Any recognisable 3D scene at all | The software pipeline runs on the ASUS panel. A6's core claim |
| **Cube looks BLUE, pyramid looks RED** | Channel order is right |
| Printed `WxH` and `stride` | The panel geometry the firmware reported. Photograph the digits |
| Left band and bottom strip stay wash colour | Content-pane containment holds at the real panel size |

**The colour row is the point and it is the only way we can get it.**
`option_a_stub.asm` still does not read `PixelFormat` at mode info
`+0x0C` -- I checked at main 12095, after the two-colour liveness work
landed, and the stub takes `HRes`, `VRes` and `PixelsPerScanLine` only.
So red/blue order is assumed everywhere and no boot screen can detect a
swap, because they are all light text on a dark ground. SceneProbe can:
the cube is `rgb 90 130 210` and the pyramid is red, so a swap inverts
both and is obvious at a glance. If it comes back inverted, the fix is
one `mov` and a handoff cell in a file fester has already had open.

### 2. If the ladder reaches the desktop (A2)

Photograph it and read four things: the sidebar 160 wide and the taskbar
24 tall **at the real stride**; the clock ticking, which is the CMOS RTC
on metal; and the status line naming **which font is on the glass**.
That last one is free and it answers whether the ESP mount works on the
real medium, because `desk-font` falls back to the CBF bitmap and says
so when it cannot read `CMUNSS.TTF` off the stick.

### 3. Do NOT judge the 3D view from one photograph

Measured today, and I nearly filed it as a defect. Under OVMF the desk's
3D view photographed as the ground plane with no cube and no pyramid,
and a second capture as pure sky. **That is a capture artifact, not a
renderer fault.** `GopScene` paints straight into the live framebuffer
with no double buffer, and the ground is node 0, so a screendump landing
mid-frame shows exactly that. Ruled out three causes offline before
believing it: Cordic is honest at all 315 sampled angles,
`scene3d-set-camera` preserves nodes, lights and ambient, and
`r3d-target-clear` does clear depth. Then measured the renderer at the
desk's exact content-pane size, 1120x776, and it draws all three objects
at every camera angle. **Exonerated my own bracket change by control**,
rebuilding from `GopScene#1` and getting the same frames.

So: real hardware is far faster than TCG and this may not appear at all.
If it does, it is tearing, and the fix is a back buffer, not a renderer
repair. Do not spend a rung on it.

### 4. A3, and a correction to the standing question

Sitting question 3 as written still stands: does the board have PS/2 and
is it live after ExitBootServices. **But the "if no" branch is no longer
frightening and the workplans should stop saying it is.** USB HID
post-EBS is not absent: today I drove the entire first-boot ceremony
under OVMF with `-UsbKbd -NoPs2`, through passphrase, entropy, keygen and
the interface menu, to the desktop and the 3D view. It is the path that
works under real firmware. PS/2 post-EBS on q35 is the one that delivers
nothing, which reek reproduced independently in a machine with no xHCI.

**What I still need from metal that no emulator gives: the mouse.** The
PS/2 mouse is EMU and untested beyond guios. On the desktop, move a real
mouse and click a sidebar button. **Only `Files` and `3D View` are live**
(`f` and `3` by key); Editor, Terminal, Monitor and Settings are inert
chrome and clicking them proves nothing. Cursor tracks and one of those
two fires = A3's mouse half is done.

### 5. Driving an image under OVMF before it is flashed

If red wants to gate a key-driven image before flashing, which is Loop
A's second gate and the thing that was skipped:

```
-UsbKbd -NoPs2 -KeyDelayMs 4000 -Seconds 25
28, codex, 28, codex, 28, <15 entropy chars>, 28, 28x7, 80, 28, 4
```

**`-Seconds` is the delay before the FIRST key and it is a real
constraint**, not a padding value: the payload waits a bounded time then
hands the controller back to firmware, after which nothing can arrive. A
key at 90 seconds lands on a dead screen; at 25 it lands. That cost me
three runs to notice.

### What I am NOT asking for

No new probe, no stub change before the sitting, and no rung for the
desktop's inert buttons. If only one of my rungs can be flown, fly
SceneProbe: it needs no input, cannot be spoiled by a keyboard question,
and the channel-order answer is the one measurement that has no
substitute on the dev box.

## How this lane gets judged

Everything here is currently EMU. **A green battery says nothing about
any of it**, because the gate has never executed an instruction on that
box. The only verdict that counts for these three rows is a photograph
of the ASUS.

Your own finding applies to yourself here: a single-shot reading is not
evidence. The GUI goldens have a blank-frame flake worth about one run in
eight, and `-Accept` does not retry.

## Findings outbox

*Deleted by the addressee once absorbed.*

- *[fester absorbed 2026-07-31. Defect (1) is CLOSED: `dk-win-h` is derived
  from the wrapped line count in main, and `desk-parse` now pins the window
  against its own text (fester 12447, main 12448) with the fixed-160 layout
  fired as the negative control. Defect (2) is REAL and OPEN, carried in
  `fester-workplan.md`: `comp-render` lays the chrome out from `y 0` and
  `desk-topbar` paints over it. The DeskVm/0x8000 entry is absorbed too;
  red remains the co-addressee on its own copy.]*

- **for fleet, and for red's re-cut in particular: the desktop is drivable
  on the DEV BOX today, keyboard and all.** `build/desk.ps1` (main 12355)
  puts GopDesk on the glass from a plain CDX in 1.5 seconds, and `f`, `3`
  and `Esc` work: codex-vm delivers window keys through `WM_KEYDOWN` ->
  `kbd_enqueue` -> IRQ1 into the same one-byte mailbox the USB HID pump
  feeds. **"The machine has no working input" is a statement about the
  ASUS, not about the desktop**, and a row that needs a key press is
  provable here even while metal input is blocked. It does not make metal
  input less urgent; it means the O-with-no-I constraint should not be
  applied to dev-box work.

- **for fleet: `-wcet <fn>` reported `calls=0` on a run it was plainly
  instrumenting, and a zero from it is not a measurement.** Pointed at
  `desk-loop` and `kbd-take` with a good map (both resolved to real
  address ranges, and exits went from 101k to 1.2M, so the debug registers
  were firing), it still answered `max=0 calls=0` for both. `desk-loop` is
  self-tail-recursive, so the function ENTRY executes about once and a
  DR-on-entry counter cannot see iterations -- but it should have seen the
  one entry and did not. **Do not quote a loop rate from `-wcet` on a
  tail-recursive function**, and treat a bare `calls=0` as the instrument
  declining to answer rather than as evidence about the subject.

- **for fleet: a test whose subject is the machine still needs an arm the
  machine cannot fake.** `gop-padded-stride`'s control row asserts the bed
  is ON (`stride exceeds width`), and it is the row that stays put under
  sabotage. Running the same CDX without the `.vmargs` flips it to `no`
  and leaves the other three green, which is what proves the sidecar is
  load-bearing rather than decoration. **Run your machine-subject test
  once with the sidecar removed** -- if nothing changes, the flag was
  never the thing under test.

- **for fleet: sabotage the arm you think you are testing, not the one
  with the right name on it.** I set `r3t-stride` to the visible width to
  fire the width-as-stride defect in `Renderer3D` and only two of three
  rows moved. `r3d-target-at` hands `r3d-fill-rows` the stride ARGUMENT
  and never reads the field back, so the field I sabotaged was not on the
  path I was aiming at. The rows were covering two independent paths and
  my prose had already claimed they were one defect seen from both ends.
  **A sabotage that moves fewer rows than predicted is telling you the
  code is shaped differently than you wrote down**, and it is worth more
  than the one that moves all of them.

- **for fleet: a channel-order bug is invisible unless the scene has two
  colours that disagree.** The stub never reads `PixelFormat`, so RGB
  firmware would swap red and blue everywhere, and every boot screen we
  have is text on a dark ground where nobody would notice. `SceneProbe`
  catches it only because it puts a blue-dominant cube next to a red
  pyramid: a swap inverts both and is obvious at a glance. **Pick probe
  content whose two candidate answers look different**, which is the
  operand-pair rule applied to something you check with your eyes.

- **for fleet: put the heap mark ABOVE the poll, not below it, and do
  not take another chapter's word for what its idle path allocates.**
  I bracketed a render loop below its `kbd-take` bind because GopUsbKbd
  argues the idle path allocates nothing. It does, today. But the cost
  of my loop then depended on a prose claim in a chapter I do not own,
  with nothing re-checking it, and the mark costs exactly the same one
  line higher up. `codex/test/heap-bracket-shape.codex` pins it: with an
  effect that allocates, above is flat and below leaks every single
  call. Only Integers need to cross the restore, and what a device pump
  writes into pre-allocated buffers or a fixed mailbox survives it.

- **for fleet: an effect that allocates nothing cannot tell you where to
  put the mark, and it will answer anyway.** My first instrument
  bracketed around an RTC read. Both placements read flat and the pair
  agreed, which looks like a result and is the probe failing to
  discriminate. Same shape as an object model that cannot express
  empty-versus-absent. **Pick the effect whose two candidate answers
  disagree**, then keep the free-effect arm as the row showing why the
  question needed asking.

- **for fleet: one heap reading cannot separate setup from steady
  state, and the setup number looks like a small leak.** The bracketed
  arm read a few hundred bytes over 200 iterations, which I was about to
  write down as a residual. At 400 iterations it read ZERO: the figure
  was the one-time cost of entering the path. **Run the loop twice at
  different counts before quoting a per-iteration cost** -- it is two
  lines and it is the difference between a number and a wrong number.

  **And the two counts have to be far apart RELATIVE TO THE NOISE, or it
  is the same measurement twice wearing a disguise (2026-07-30).** Timing
  the 3D view's new blit at 20 against 60 iterations gave deltas of 20 ms
  and 59 ms on runs of about 320 ms, well inside run-to-run variance --
  and it produced a per-frame figure that looked perfectly reasonable and
  that I was one line from quoting. At 20 against 220, three runs a point,
  the answer was stable and about 15 per cent different. **Two counts is
  the rule; two counts a factor of three apart on a noisy instrument is
  not two counts.** Before believing a delta, ask what the spread between
  repeats of the SAME point is, which is one extra run.

- **for fleet: a bounded frame cost is not a bounded loop, and the test
  that measures one frame cannot tell you which you have.** I fixed the
  per-frame allocation in the 3D renderer, wrote a test asserting the
  cost does not depend on pixel count, watched it pass, and left the
  loop that runs those frames unbracketed. `gsc-loop` made 20 KB
  permanent per frame forever. The test could not have caught it: it
  renders one frame and pins `frame cost is nonzero` as CORRECT, which
  it is, and which is also the leak. **Any loop with no bound needs
  `__heap-save`/`__heap-restore` around the discarded part, and the way
  to prove it is a bracketed arm and an unbracketed arm in the same
  test** -- the unbracketed one is the control that shows the probe can
  see growth at all. Mine grew by exactly eight single-frame costs,
  which also rules out anything else contributing.

- **for fleet: a record-returning buffer cannot back a screen, and the
  test that renders one frame offline will never say so.** `Renderer3D`
  rasterized into a `Framebuf` whose `fb-set` returns a record per
  pixel, three records per covered pixel once the depth buffer and the
  threaded state are counted. It passed for its whole life because
  `engine-software-render` renders ONE 320x240 frame and never repaints:
  at 1024x768 the same code costs roughly 113 MB per frame, permanent,
  and dies in the first frame or two. **Ask what a frame costs, not
  whether the frame is correct** -- with no collector those are separate
  questions and only one of them had a test. `UI chapter PixelBuf` had
  already measured and written down the 24-bytes-per-pixel figure that
  condemns the pattern; nothing pointed it at the engine.

- **for fleet: all seven exp approximations in the AI quire are now
  measured and the family is closed.** Six of seven were wrong in SHAPE,
  not precision. A truncated Taylor has a turning point, and past it the
  answer moves in the wrong DIRECTION: `sigmoid(5)` answered -854, and
  `exp(-2) > exp(-1)`, which inside a softmax means a larger logit takes
  a smaller weight. **Check the derivative's root, not the size of the
  error**, and probe past the turning point rather than near zero where
  every version agrees.

- **for fleet: RETRACTED, and the retraction is the finding. I named a
  set after a relation I never computed.** The entry used to say "the
  fourteen AI tests in the cite closure of those chapters all passed
  byte-identically before AND after the fix." Two of them did not:
  `inference-demo` and `neural-test` were red from 11794 on 2026-07-28
  until red re-minted them at main 12513 on 2026-07-31, three days.

  I called the set a cite closure. It was a glob: `codex/test/forewords/
  ai-*`. Measured 2026-08-01, the actual reverse-cite closure of
  `AI/Activation` and `AI/Normalization` is **30 tests, 20 in the
  forewords tier and 10 in `codex/test/apps/`** -- and an `ai-*` name
  glob cannot see the apps tier at all, because the two apps-tier tests
  that exercise the chapters are named `neural-test` and
  `inference-demo`. The glob was not even complete for its own
  directory: 36 of 37 ran, `ai-upscaler` was missed.

  **The number is reproducible, which is what makes it dangerous.**
  Fifteen tests cite both changed chapters; fourteen of those are in the
  forewords tier. So "fourteen" is exactly "the forewords-tier subset of
  the both-chapters closure" -- a real set, correctly counted, wearing
  the name of a different and larger one. A number that reproduces feels
  measured. Ask which set it counts (L-COUNT).

  **The conclusion survives on the merits and I still had not earned
  it.** `neural-test` asserts `act-sigmoid` at z = 0, +1.0, -1.0; the
  old sigmoid polynomial's derivative root is at z = 2, so all three
  operands sit inside the radius where every version agrees. It could
  not have caught `sigmoid(5) = -854`. What it caught was the FIX:
  730 -> 731, 270 -> 268, and `inference-demo` 395 -> 396, 398 -> 399.
  One part in a thousand. **A golden minted inside the radius of
  convergence is a regression test for the arithmetic's last digit and
  blind to its shape** -- it goes red when you repair the function and
  stays green while it is broken, which is the sign flipped on what a
  test is for.

  Nothing else in the closure was latent: the remaining eight apps-tier
  members call no activation (`act-store` is an unrelated effect and
  will false-positive a grep for `act-`), and `ai-upscaler`'s entire
  expected output is `AI/Upscaler OK`, the same empty-oracle pattern as
  `ai-activation` below.

  The rule: **compute the closure, do not name a glob after one, and
  compute it across every tier** (L-GAP). Selecting tests by filename
  prefix selects by what someone chose to call them, which is not a
  property of the code under test.

- **for fleet: a test named after a chapter is not a test of it.**
  `ai-activation` exists, passes, and its entire expected output is
  `AI/Activation OK`. It names no activation function and calls none.
  Four broken functions sat behind a green test with the chapter's name
  on it. **Grep the `.expected`, not the file list**: an expected output
  with no numbers in it is asserting nothing.

- **for fleet: the GUI goldens have a blank-frame flake worth about one
  run in eight, and it is not app-specific.** `test-gui.ps1` absorbs it
  by pushing the deadline out on retry, so the battery is honest. But
  **`-Accept` does not retry**, so a re-mint can silently record a blank.
  Hash the recorded file against a frame you have actually looked at.
