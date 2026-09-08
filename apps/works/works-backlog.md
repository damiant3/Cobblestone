# Works -- open capabilities

App-domain backlog. There is no platform-wide register any more:
`docs/PM/BACKLOG.md` was deleted 2026-07-23 and must not be recreated.
`docs/PM/CurrentPlan.md` carries the shape and the priority order for
the platform. Anything that is this application's own behaviour lives
here.

The rules are the same ones: an entry says what is still missing and
nothing else, a closed entry is DELETED rather than annotated, and a
gap that is still real is never quietly dropped.

**`works-desk-contract.md` is the standing rules for the desk** -- the `ds`
cell map and the rule about pointers in it, the base heap mark and how a pane
exits, the cursor bracket, why the palette arrives as a parameter. Read it
before adding a pane or taking a cell. This file is what is missing; that one
is what must not be broken.

## WORKS-63: `DiskTestRunner` is compiled by the app sweep and run by nothing

Stage 1 of `Build.md` Phase B mounts the boot volume, lists the root, looks one
entry up by name and reads it back, and the proof of that is a bed run by hand
rather than an arm: `test-run.ps1 -Kernel <runner.cdx> -DiskFile <image>` prints
five entries and 628 characters of `SOURCE.SRC`, and the no-disk control prints
`REFUSED`. Nothing runs either.

The mechanism for an arm is a `.disk` sidecar in `codex/test/apps`, which the
battery already uses fifty times and which costs 8 MB of binary in the depot per
arm, because the sidecar IS the image rather than a pointer to one. Either that
price is paid deliberately or a smaller image is built; leaving the runner
compiled-but-unrun is the shape `CurrentPlan` names for 43 other graders.

## WORKS-60: minimising while a 3D pane is alive TRIPLE FAULTS through the page tables

Found 2026-09-07 by Damian driving the desk by hand, and it is the same
FAMILY as WORKS-59 below (heap given back under a pane that is still live)
with a different pane and a much worse signature: not a halt, a wild write
that destroys address translation.

**The sequence, his:** open Clock, Calculator, Aquarium and Issues; open and
close the Cobblestone menu; restore Issues; click MINIMISE ON THE WINDOW
TITLEBAR. Reproduced twice, on `desk.ps1` at 1600x900 against seed
81F9E8171DCF6268.

**The evidence, from codex-vm's own crash report:**

```
CRASH: Triple fault (unrecoverable) CR2=0x6080 RIP=0x108a1b after 279480735 exits
Fault: Address 0x6080  Access: WRITE
  #0 poke-32+88              mov [rdi],rdx
  #1 r3d-target-clear+123
  #2 gsc-frame+740
  #3 gsc-step+969
  #4 desk-scene-step+1384
  #5 desk-step-of+4648
  #6 desk-loop+1932
  #7 desk-app-hide+1263
```

The word being written is `0x3b9ac9ff`, which is 999,999,999, which is
`r3d-far-depth`, and the memory at the fault address is that word repeated.
So it is the depth half of `r3d-target-clear`:
`r3d-fill-32 (st.r3t-depth) r3d-far-depth 0 (r3t-w * r3t-h)`.
**`r3t-depth` is at or near zero**, so the fill marches from low memory
upward writing the far-depth sentinel, crosses the PML4 page at 0x8000,
destroys address translation, and the next fault cannot be delivered: triple
fault rather than a diagnosable one. `r3d-fill-32` validates no base, so a
null base is a wild write through the kernel and nothing in the renderer can
refuse it.

**What is established:** the path, the value, and that `desk-app-hide` is the
frame under it, so a minimise is what starts it. `desk-app-hide` gives heap
back, and the scene's next `gsc-frame` in the same `desk-loop` iteration
clears a target whose depth pointer no longer points at anything the pane
owns.

**What is NOT established, and must not be written down as though it were:**
WHY `r3t-depth` reads near zero rather than as a stale heap address. A
dangling pointer into the arena would be a large value; ~0 says the record
was rebuilt empty or read from memory that had been handed back and zeroed.
The two close paths already "DROP the record they were handed and build an
empty one AFTER the restore" (`works-desk-contract.md` section 0), and
whether the hide path does something similar is the first thing to read. No
fix should be believed until it moves this symptom (L-MECHANISM).

**Two cheap arms nobody has run**, both of which WORKS-59's investigation
would have covered had it reached this pane: the same sequence with the
Aquarium never opened, and the same sequence against the revision before
this lane's stage 3 CL (main 22380). Neither of the functions in the
backtrace is one that CL changed and it allocates nothing, so it is unlikely
to be implicated, but unlikely is not cleared.

**PART 1 IS LANDED (main 22455) AND IT DOES NOT CLOSE THIS ROW.** `r3d-base-ok`
gates the four renderer fill functions at 0x100000, the address the guest
kernel loads at, so a null base refuses instead of writing; `r3d-target-clear`
answers the refusal rather than reporting success either way (L-BAILVALUE).
Measured by `codex/test/engine-fill-guard`, with a real-target control that
fails in the other direction, and with the broken arm run: a scratchpad
chapter filling from address 0 printed its first line and then nothing, its
"survived" falsifier never firing. **A machine kill is now a frame that
declines to draw. WHY THE TARGET IS BAD IS STILL UNKNOWN.**

**WHAT WAS ESTABLISHED HUNTING THE CAUSE, AND WHERE IT STOPPED PAYING.** The
reclaim in `desk-app-hide` is `desk-root-reclaim`, whose decision is
`desk-root-may-reclaim ms s e`: it frees back to `s`, the frontier before the
current root was built, unless `desk-marks-live-above ms e` finds a LIVE entry
whose mark is at or above `e`, the frontier after it. A pane that opened
BEFORE the current root has its mark below `e`, so that test does not protect
anything it allocated after the root was built. `gsc-place` allocates a fresh
`R3dTriState` on every step and stores it into `sp-tgt` with `__record-set`,
which is exactly the "pane that rebuilds its state somewhere else" the
contract says must remark -- and the contract also says `desk-browser-reenter`
is **the only pane that does**. So the 3D panes violate a stated obligation.

**That is a real defect and it is NOT yet the proven cause, because the call
order argues against the obvious story.** `desk-scene-step` calls `gsc-place`
IMMEDIATELY before `gsc-step` on the same line, so the target is rebuilt fresh
after any reclaim and before `gsc-frame` reads it; `gsc-frame` takes it as a
PARAMETER; and the frame's own bracket is taken inside `gsc-step`, after
`gsc-place`. A freed-record explanation has to say which restore runs between
the rebuild and the read, and none of the ones visible here does. Do not write
the reclaim up as the cause until a change to it MOVES this symptom
(L-MECHANISM); the previous three wrong diagnoses on this desk were all
mechanisms that read well.

**PART 2 IS LANDED (main 22656) AND IT DOES NOT CLOSE THIS ROW EITHER.**
`gsc-frame` paints the offending `r3t-base`, `r3t-depth`, `r3t-w` and `r3t-h`
in red where the frame would have been, because a pane that declines to draw
is indistinguishable on the glass from a pane that is idle. The check runs
before `scene3d-set-camera`, since `gsc-aspect` divides by `r3t-h`. Reading
those numbers off the glass during Damian's sequence is what remains to be
done, and it needs him at the keyboard.

**A SECOND AND SEPARATE DEFECT, FOUND HUNTING THE ORIGIN, FIXED IN THE SAME
CL.** Nothing tied the target's extent to the buffer's size. `gsc-new` sizes
`sp-px` and `sp-dp` from the content box AT THE MOMENT THE PANE OPENS;
`gsc-place` sets `r3t-w` and `r3t-h` from the window rect every frame; and
`r3d-target-clear` fills `r3t-w * r3t-h` words. The content box is the screen
less a strip on each of the four edges, and which edges carry one is a setting
the user changes while the pane is up. Measured 2026-09-07 by
`codex/test/apps/desk-cbox-drift` at 1600x900: the box is 1600x800 with the
band on a horizontal edge and 1544x856 on a vertical one, so a pane born under
the band and cleared after the band moved writes **41,664 words, 166 KB, past
the end of BOTH buffers on every frame**. `r3d-base-ok` cannot see this: the
base is a real address and it is the EXTENT that is wrong. `gsc-fit` clamps
both and `ScenePane` carries `sp-bh` to clamp against.

**THE DEGENERATE CONTENT BOX IS RULED OUT.** It was the better candidate for a
near-zero pointer, since `dk-cbox-w` and `dk-cbox-h` subtract four strips with
no floor and `gsc-new` hands the result straight to `alloc-bytes`. The same
test measures the smallest dimension over the four task edges as 800 at
1600x900 and 430 at 640x480, so a zero or negative box is NOT reachable by the
task edge alone. That is narrower than "not reachable": pinned-pill strips on
several edges at once are not covered by any arm here.

**So the near-zero `r3t-depth` of the original fault still has no established
origin.** The overrun above is real and was worth fixing, but it writes past a
HIGH address and cannot produce `CR2=0x6080`.

**WHY THIS LOOKED LIKE A FREEZE FOR AN HOUR, and it is the reusable half.**
`build/desk.ps1` starts codex-vm with `Start-Process` and no redirection, so
the guest's serial goes to a console nobody reads. codex-vm produced all of
the above -- the banner, the register dump, the fault address, the memory at
it, the disassembly at RIP, a symbol-resolved backtrace, and an interactive
`dbg>` prompt -- and every byte of it was discarded. The desk was diagnosed
as a hang twice, on zero-CPU measurements, because the process really does
sit at zero CPU: it is stopped at the debugger prompt, not looping. This is
L-UNHEARD exactly: a correct detector wired to nothing. **Run the desk with
its output redirected to a file whenever a crash is in play**, and the fix
worth making is to have `desk.ps1` do it by default.

## WORKS-59: `desk-root-reclaim` must not free a live pane's state

A mark records where a state WAS, so a reclaim decides on the address the
state left rather than the address it holds now, and only a mark whose pane
is dead may be reclaimed. Breaking this halts the guest on the second pill
restore with `!EXC=06`, rendering nothing at all rather than a bad frame.
`codex/test/desk-root-guard` pins the decision and the BVT runs it;
`GopDesk.codex` cites this at its mark stack.

## WORKS-58: a desktop rebuild frees the root it replaced

Minimise and pill restore each rebuild the desktop root, and a rebuild frees
the root it replaced, so the frontier is FLAT in the number of cycles rather
than climbing by one root per cycle. `works-desk-contract.md` cites this for
`ds` cells 224 and 228, `desk-root-cell` and `desk-rootend-cell`, which hold
the frontier immediately before and immediately after the current root.

## WORKS-57: a window close keeps every surviving pane's state

`desk-wnd-close-to` kills the closing pane's entry, reclaims dead entries
from the top, and restores to the reclaimed target or to nothing when a live
entry sits above; it does not hand down `desk-apps-empty`.
`desk-pill-restore` dispatches to the pane's own `-focus`, which RE-ENTERS a
live pane and opens a dead one, and that re-entry is not optional: without it
a close leaves a bare desktop and a restore paints an empty window.
`GopDesk.codex` cites this in three places.

## WORKS-56: the band's cached depth covers a band holding a pill

`dk-task-init` measures the band and a one-specimen strip separately and
caches the LARGER, so the content box reserves the depth the band can need
whatever the window state: 36 at 1280 and 72 at 1600, band empty equal to
band with a pill. The measure runs at boot and on an edge change and never
again, so a cache taken from the empty band under-reserves by 16 device
pixels the moment the first window is minimised, and the bottom of the
content box sits under the band. `codex/test/apps/desk-pane-origin` asserts
containment with an empty registry and with one minimised window; an
under-reserving cache turns `slot inside box` and `equal` to NO.

## WORKS-50: one pane, two names, depending on where you look

**DONE.** One surviving full name per pane, for the launcher AND the title
bar: `Text Editor`, `Browser`, `System Info`, `Program Runner`. `desk-wnd-title`
and `gpr-entries` now agree on all four, and Programs has a launcher entry
under `Program Runner` (scan 25, icon `code`). The taskbar's short names in
`desk-focus-name` are unchanged, which is deliberate.

**The new entry goes in `Settings`, not `Productivity`, and the reason is a
measurement rather than taste.** Put in `Productivity` it made that group 11
rows, and `desk-menu-groups` then read `band on the glass NO` at w1600 (band
418..454 of 450): the start menu is bottom-anchored and the tallest OPEN group
sets its height, so a 16th row in the tallest group pushes the taskbar band off
the bottom. That arm exists for exactly this overflow and its own prose records
the build it caught. In `Settings` the group is 4 rows, `Productivity` stays the
tallest at 10, and the arm is byte-identical. **A further entry in any group
that reaches 11 rows at 1600 will fail the same way; the menu has no second
column and no scroll, which is the design question if the launcher grows.**

Arms: `desk-menu-groups`, `desk-menu-anchor`, `desk-label-metrics`,
`desk-window-registry`, `desk-taskbar-hit` and `desk-pill-pinned` are
byte-identical. Three moved and every delta is accounted for by name:
`desk-mon-block` (title Monitor to System Info), `desk-chrome-icons` (15 rows
to 16), and `desk-prog-click` (title, plus scan 25 for the new entry, plus
**scan 17 which was already missing from that golden before this change** --
Web Server's launcher entry landed earlier and the sidecar was never
re-recorded, so that arm was red at head in no lane's gate, L-NOGATE).

**What remains on this row, and it is now a PRICED choice rather than an
obvious tidy-up.** The join against `gpr-entries` does hold: all fifteen focus
ids in `dk-pill-icon` match a `ge-label` exactly, so the second table CAN go.
What it costs was not measured when this row was written. `dk-pill-icon` is a
fifteen-branch chain on an Integer, answered in one comparison; the join is a
linear scan of sixteen rows comparing TEXT, and its one caller
(`GopDesk.codex:778`) runs it once per pill inside the chrome paint, so a
taskbar of fifteen pills pays about 240 text comparisons per chrome repaint
against fifteen integer compares today. That is a paint path, not a pixel path,
so the cost is small; it is still a cost paid to hold one register instead of
two (L-LESS). **Whoever closes this decides which they want, and the honest
framing is that the duplication is now SAFE rather than wrong: the defect the
two tables caused was the four disagreeing names, and that is fixed.** The
one thing that would settle it is a measured chrome repaint before and after.

Found 2026-08-25 (val) while giving the taskbar pill its app icon, and left
unfixed deliberately because deciding which name wins is not an icon change's
business.

**`desk-wnd-title` and `gpr-entries`'s `ge-label` disagree for four of the
fifteen panes.** The launcher row says `Editor` and its window titlebar says
`Edit`; `Browser` against `Web`; `System Info` against `Monitor`; and
`Programs` has a window title but no launcher entry at all. A person opening
Browser from the start menu gets a window that calls itself Web.

**There is a third table and it is NOT the defect**: `desk-focus-name` gives
deliberately short names for the taskbar's `tasks` slot (`Web`, `3D`, `Fish`,
`Edit`) because a band is narrow. That one is fine. The question is only
whether the two FULL names should agree.

**The cost of the divergence is already paid once.** `dk-pill-icon` had to
become a second table keyed by focus id rather than a join against
`gpr-entries`, because the join misses on those four and `gicon-named` answers
the `file` icon for an unknown name rather than refusing, so four pills would
have worn the wrong picture with every count still agreeing. Fixing the names
would make that join sound and let one table go.

**What settling it needs.** Pick the surviving name per pane, then change
whichever table loses. `desk-wnd-title` is read by the titlebar, the pill and
`desk-window-registry`'s expectation; `ge-label` is read by the launcher, the
start menu, and `desk-gpr-icons`'s id join (`"gpr-" & ge-label`), so a label
change moves widget ids and `gpr-id-scan` with them. Neither is a rename in
one place, and `codex/test/apps/desk-chrome-icons` counts rows by those ids.

## WORKS-48: the Web Server pane and `ds` cell 248

`desk-focus-web` is the pane and `ds` cell 248 (`dk-web-cell`) is the block
the desk shares with the spawned web service. The pane offers Stop, Start,
the service's state, and the last sixteen requests its route answered.
`web-dispatch` takes an observer beside the route and calls it for EVERY
request with the response actually returned, so `/api/health` and
`/api/status`, which `web-standard` answers before the route is consulted,
reach the log as well. `works-desk-contract.md` cites cell 248 and
`PreemptiveScheduler.md` stage 3 holds the design.

## WORKS-25: every USB entry point picks its controller by what it finds there

**The contract: a caller takes the first xHCI controller that yields the
device the caller wants, never the first controller.** `GopUsbMsc.msc-hosts`,
`GopUsbKbd.kbd-hosts` and `CamCapture.cam-hosts` each recurse on ordinal over
`xhci-connect-scan`'s one PCI scan, bringing up each controller through
`xhci-connect-one` and stopping at the first that answers: a disk for the mass
storage chapter, a boot keyboard for the keyboard chapter, a frame at
`cam-port-index` for the camera. A controller that fails to initialise is
skipped rather than ending the walk, which is `GopUsb.usb-hosts`' rule. The
bound is `xhci-max-hosts`, 8.

`GopXhci.xhci-connect-first` is the deliberate first-controller entry and is
named for what it does. Its one caller is `codex/test/apps/usb-cam-frame`,
whose bed has exactly one controller.

**What is still unobservable, and it is the reason to keep this row.** No arm
can show a caller finding its device on ordinal 1, because codex-vm's second
controller carries no device: the device models are global singletons attached
by root PORT, so `-xhci-two` gives a register-only `ctl1`
(`docs/Designs/Active/Tools/DeviceEmulationCatalog.md`). The arm that WOULD
discriminate on today's bed asserts the SIDE EFFECT rather than the result: a
probe calling `kbd-connect` under `-xhci-two` leaves `ctl1 ... running` in the
diag ctl table, where the old code left ctl1 never opened. That arm is not
written. Until a device can sit behind `ctl1`, an arm asserting "the keyboard
was found" agrees with both the old code and the new one (L-VACUOUS).

## Standing check: every chapter must compile on its own cites

```powershell
build/check-subset-cites.ps1 -Root apps\works -Jobs 8 -Kernel seed\Codex.cdx
```

**Run it after any cite change and after adding a chapter.** It builds each
chapter as its own unit -- the chapter plus the transitive closure of what it
cites -- and lets the compiler answer, so a chapter using a name it never cited
is caught here instead of by whoever next assembles a subset. That is L-SUBSET,
and the glob build cannot see it. The gate does NOT run it.

**Read the coverage line, not the verdict.** A unit that does not compile is a
chapter that was not judged, and a run can have failures above a clean verdict.
Since 2026-08-21 the script names them and says INCOMPLETE rather than OK.

| # | Capability | State of the gap |
|---|---|---|
| WORKS-47 | **A sidebar or taskbar button's ICON is drawn outside the button, below its bottom edge** | Found 2026-08-21 (val) while proving ShellRefinement stage 5's persistence on the glass, so it is not that work's doing and predates it. **It is present in the DEFAULT scheme, and that is the reading that rules out the colour scheme as the cause.** The pristine arm (seahawk, no keys, untouched ESP) paints the Shutdown icon's stand and base BELOW the button's rounded bottom, onto the wallpaper, and the Console icon carries the same overhang in the sidebar. `terminal` only makes it easy to SEE, because a dark icon against a light grey desktop is legible where the same pixels against navy are not, and reading it as a `terminal` defect is the wrong turn this row exists to prevent. Reproduced on three independent boots at 1600x900, and again after a rebuild. **The class is the one `comp-fit-px` closed for text and nobody closed for icons**: the icon is placed and drawn with no vertical bound against the box it was given, which is WORKS-41's unclipped panel one widget along. **DIAGNOSED AND HARDENED 2026-09-08 (val), AND NOT REPRODUCIBLE AT HEAD.** The unbounded axis is real and it is in `desk-icon-in`, not in `comp-custom`: `comp-icon` already drops an icon whose square would leave its clip, but the desk's own icon painter scaled from the GUTTER'S TEXT WIDTH alone, and the artwork is square, so a box shorter than that width drew an icon taller than the box. `sy` centres only when the box is the taller of the two and otherwise parks the icon at the top, and `gicon-blit` bounds columns against the stride while knowing nothing of a rectangle, so the excess lands on whatever is under the button. The scale is now the minimum of the gutter and the content height, which bounds the axis that was unbounded and leaves the under-eight case drawing at scale 1 as before. **The sighting itself does NOT reproduce.** Captured at 1600x900 in the default scheme with the start menu open, the fixed build is byte-identical to the unfixed one everywhere except the clock strip, so no icon in that menu overhangs today and the change is INERT on this surface (L-FALSIF: it is a hardening, not a demonstrated repair). Half the original sighting names a surface that no longer exists, the Console icon in the SIDEBAR, and the menu has been rebuilt into groups since. **What would settle it is the theme and geometry the 2026-08-21 boots used**, since the row's own reading is that the default scheme shows it; anyone reproducing it should capture first and only then believe a fix moved something. |
| WORKS-62 | **The MSC driver never asks the device to commit, so a banked write that reads back correctly can still be gone at power-off** | Found 2026-09-07 (blu) explaining sitting 13's eight-stage gap: the returned stick's `DIAG.TXT` ended at stage 9 while the ladder ran on and only noticed the bank was lost at 17. **`GopUsbMsc.codex` issues exactly six SCSI opcodes** -- TEST UNIT READY (0), REQUEST SENSE (3), INQUIRY (18), READ CAPACITY (37), READ10 (40), WRITE10 (42), all at `:27-32`. **There is no SYNCHRONIZE CACHE (0x35 = 53), and no other flush or commit anywhere in the storage path.** The consequence is structural, not a guess: `diag-bank-write-text` (`DiagStage.codex:137`) writes, then re-reads the size with `gfat-file-size`, which resolves through `gfat-scan-root` to `disk-read-sector` -- a REAL device read, so the readback is not reading our own memory. But it reads back through the SAME device-side write-back cache the write landed in, so a device that acknowledges into volatile cache and serves the updated directory entry from that cache passes the check while having committed nothing. **The guard added after sitting 3 cannot see this failure mode by construction**: `dg-bank-write`'s prose says "the file's own directory entry is the truth, so every write reads the size back", and the directory entry is exactly what a write-back cache will happily hand you. **What is fact and what is not.** The absent opcode and the readback path are read off the source and are facts. That this produced sitting 13's gap is a mechanism that FITS and is not established (L-MECHANISM): stages 10-16 each returned success from `dg-bank-step`, so the ladder was told the writes landed, and the stick came back holding stage 9. **The falsifier is cheap and moves the symptom**: issue SYNCHRONIZE CACHE after the bank write (or at minimum once before the ladder ends) and re-fly; if the returned stick then carries the full ladder, the mechanism is the cause. No bed can answer it -- codex-vm's BOT model completes and commits in one step, which is why every arm is green on a path that has never been asked to flush. This is also a candidate explanation for sitting 3's 4,577-byte `DIAG.TXT` returned with `bank=ok` painted. **MEASURED 2026-09-07 against the returned stick, and it discriminates the two readings.** The board changed 56 sectors of the 16 MB image: MBR/GPT (0, 1), one FAT sector in each copy (2054, 2158), the root directory (2257), LBA 30000 (the `block` stage's scratch write), and 50 data sectors, 3570 to 3619. `gfat-write-file` allocates a FRESH chain per write, so those 50 hold **five successive generations of `DIAG.TXT`** at offsets 0, 4608, 9728, 14848 and 19968, each adding exactly one stage: gopmode, gopmode+rcp, block, xhci, kbd. The last is 5,149 bytes and is the one the directory points at. **Nothing from stage 10 onward exists in ANY generation.** Set that beside the ladder's own record: the summary names stage 17 as where the bank was lost (`dg-stage-name (lost - 1)`, so the first recorded failure was at 17), which means `dg-bank-step` returned success for stages 10 through 16, size readback included. **Both are only true together under the cache reading.** For the readback to pass, `gfat-file-size` had to return the NEW size each time, and that requires the device to hand back an updated directory entry for data it never committed. The competing reading, that the writes simply STOPPED at stage 9, is inconsistent with the ladder recording its first failure seven stages later. So the writes continued, were acknowledged, and were lost at power-off. **What this does NOT settle:** why the device began caching without committing at that moment. mscalign (stage 10) ran on this flight despite `mscalign off`, because of the CRLF parse defect red found, and its liveness probe reads past the end of the medium and takes a Bulk-Only reset; a BOT reset immediately before the first lost write is a candidate trigger and is not established. The flush is worth adding regardless: without it we cannot tell a committed bank from a cached one on any stick, on any flight. **THE INFERENTIAL STEP, SPELLED OUT, because it is the load-bearing one and there are no rows for stages 10-16 to read instead.** Those rows do not exist: the medium holds nothing from stage 10 on, which is the finding, so what follows is inference from the ladder's own record and must be checkable without its author. (1) `dg-bank-step (c) (b) (lines) (at)` calls `dg-bank-write`; on a return below zero it records `gfat-note-banklost (at + 1)`, guarded by `xdiag-get gfat-cell-banklost == 0`, so the FIRST failure wins and later ones cannot overwrite it. (2) On the same failure it calls `dg-paint-banklost c at`, and that function prints `"BANK LOST AT STAGE " & show i & " " & dg-stage-name i & ", NOTHING AFTER THIS IS ON THE MEDIUM"` with `i` bound to `at` (`Diag.codex:321-325`). (3) Damian read exactly that sentence off the glass with `17 pchk1`, and 17 IS pchk1 in the 18-stage ladder, so `at = 17` and the stored note is 18. The summary form is the same claim by another route: it prints `dg-stage-name (lost - 1)`, which is 17. (4) `dg-bank-step` is called after EVERY non-deferred stage, skipped ones included, since `dg-run-one` returns skip lines and the caller banks them regardless. (5) Therefore stages 10 through 16 each ran `dg-bank-write` and each returned at or above zero. (6) `diag-bank-write-text` returns at or above zero only if `gfat-write-file` reported success AND the subsequent `gfat-file-size` equalled the byte count just handed over (`if sz == n then n else -1`), and `n` grows every stage. So for seven consecutive stages the device answered a size query with the NEW length. (7) None of that data is on the medium. A device that reports the new length and retains nothing would be answering from its own buffer -- **but that is one candidate of two and neither is established.** **The one thing this does NOT establish, and no instrument we own can:** our side issues READ10 and gets an answer; whether that answer came from flash or from the device's cache is not observable from here, which is precisely why the flush is the fix rather than more reading. **Artifacts, so none of this has to be taken on trust:** raw dump `stick-archive\diag13-returned-20260907.img`, extraction and peer log in `stick-archive\diag13-20260907\`. The 56-sector map and the five generation offsets are reproducible from the dump alone by diffing it against `build-output/diag-sitting13.img`. **SECOND CANDIDATE, and it does not require the device to misbehave at all (red, confirmed by root and re-verified here 2026-09-07).** `DiagMsc.codex:102-103` runs `__heap-save` then `__heap-advance dma-span` with NO `__heap-restore`, and it is the ONLY ladder stage file that does not pair: counted across `build/boot/diag/*.codex`, DiagMsc is save=1 restore=0 advance=1 while B3, NicRing, NicSit, PchK1, Scene, Sink, Stage, Kbd and NicSitting all balance. It leaks `dma-span` = 262,144 bytes permanently, and it is stage 10, exactly where the banked data stops. **What the arithmetic says against it, so nobody adopts it by relief:** the reservation is not overrun. Worst case the reads reach `65535 + dma-cross-off (64,512) + dma-sectors * 512 (32,768)` = 162,815 bytes above the save point, inside the 262,144 reserved, so the two `msc-read-into` calls do not scribble past it. And 256 KB against `alloc-pages=32768` is a fraction of a percent, so simple starvation does not follow from these numbers either. The leak is a real defect and must be fixed on its own merits; the path from it to seven consecutive bank writes reporting success against a GROWING `n` is not established, any more than the cache path is. **THE FIRST VERSION OF THE FLUSH BROKE EVERY BANK WRITE, and the rehearsal is what caught it (blu, 2026-09-07).** It failed the bank when the flush was refused, which sounds strict and is the opposite: SYNCHRONIZE CACHE is OPTIONAL in SCSI, a device that does not implement it answers CHECK CONDITION, and refusing the bank there banks NOTHING. codex-vm's BOT model allows exactly the six opcodes that predate the flush (`codex-vm.c:2409`), so 0x35 is refused and **43 of 47 rehearsal arms went to `bank=none write refused` on one signature**; on metal the same would happen on any device without 0x35, and it would have cost the last sitting its entire record. That is L-FALLBACK: a working path disabled by the change that introduced its replacement. Fixed so the flush's outcome decides the MEANING of the size readback and never the bank: an uncertified record beats no record, and this ladder exists to bring a record home. `disk-sync-cache` still answers 1 flushed, 0 no primitive, -1 refused for a reader that wants to know. **The bed cannot yet SHOW the flush mattering** (reek, 2026-09-07: teach the BOT model 0x35 and add a write-back arm that loses uncommitted writes at power-off); until that lands, a green rehearsal says the flush does no harm, not that it does good.

**THE FLUSH IS IN (blu, 2026-09-07), so what remains is the flight.** `scsi-op-sync-cache` (0x35) with LBA 0 and a block count of 0, stamped like TEST UNIT READY with a 10-byte block and IMMED left clear so the device cannot answer before committing; `msc-sync-cache`, then `usb-sync-cache` and `disk-sync-cache` on the dispatch layer. Those two answer THREE states and not a Boolean, because a medium we cannot flush must not read identically to one we did (L-BAILVALUE): 1 flushed, 0 no flush primitive for this medium, -1 issued and refused. `diag-bank-write-text` and the note writer both call it BETWEEN `gfat-write-file` and the `gfat-file-size` readback, which is the ordering the whole finding turns on: the readback is only a statement about flash if the commit precedes it. A -1 fails the bank rather than reporting a success nobody can trust; a 0 is neither a failure nor a certification. **The separator is now a FLIGHT and it is Damian's**: re-fly with the flush in, and if the bank survives the cache reading is the cause, while if it still dies at the same stage the fault is ours and `DiagMsc.codex:102-103`'s unpaired `__heap-save` is where to look. **THE BED CAN NOW ANSWER PART OF IT, so "no bed can answer it, because codex-vm's BOT model commits in one step" no longer holds** (reek, 2026-09-07, plugs 2.46, main 23233): codex-vm accepts 0x35 and `-usb-writeback` makes a BOT write durable only at that commit, dropping what is uncommitted when the machine stops. Measured by hand on that flag: with the flush, 16 commits and `DIAG.TXT` whole; on a kernel six minutes older than the flush, 0 commits, 4,618,240 bytes pending, **`DIAG.TXT` ABSENT from the image while the guest still reported `bank=ok medium=usb`**. That is the mechanism demonstrated and shown to be invisible from inside the guest. It is NOT the cause of sittings 13 and 14, whose media died mid-run rather than at power-off (blu's precision, L-MECHANISM), and the flight remains the separator for that.

**THE `-usb-writeback` PAIR UNDER `diag-arm` DOES NOT SEPARATE YET, and the cause is the ABLATION, not the flag or the harness** (reek, 2026-09-07, from blu's shelf 23298, verified against blu's own census files rather than reasoned). Both arms: 186 `USB WRITEBACK: cached` lines and 186 `BOT-CENSUS: write` lines, so the flag reached the BOT write path and the medium is `usb` in both. Commits: `bank-writeback` 16, matching the hand run exactly; `bank-writeback-noflush` **1, not 0** -- `USB WRITEBACK: commit n=1 off=1049088 len=781824 pending-was=109568`. One commit is enough to leave `DIAG.TXT` on the medium, which is why both arms come back with the file present. **Two theories are refuted by that count.** The kill theory is wrong: `diag-arm` killing codex-vm at END cannot skip the drop, because the drop is the ABSENCE of `ide_flush` and uncommitted bytes never reach the image at all. And the summary line being absent from the console is BY DESIGN, not a symptom: the instrument is the per-write census lines, `fflush`ed one at a time, and `Invoke-Vm` sends the census to a FILE, never to stderr. **What is left for the next blu** is blu's own diagnosis: the ablation cell is set only after the ESP cfg is read and an unset cell reads as flush-wanted, so every bank write before the cfg is parsed still commits; carrying `flush off` in the STUB RING via `stdinCfg`, which is live before the first bank, is the fix. |
| WORKS-46 | **CLOSED 2026-09-07 (red): no consumer of the kernel road runs on a stick.** | Audited caller by caller, and the three the row named are not live. **FirstBoot is superseded and dead**: no chapter cites `Works chapter FirstBoot` and no `first-boot-*` function is called anywhere in the tree; the desk's live wizard is `GopBoot`'s `wizard-run`. **IdentityManager** is cited only by FirstBoot and three unit tests, so its sysdb path is unreachable in any shipping image. **The repo persist** (`RepoProtocolPersist`, 23 call sites, and `FactArchive`) is cited only by `codex/test/apps/*`, which run under codex-vm against an attached IDE image where `block-read-sector` is the correct road; the remaining kernel-road users (`apps/browser`, `apps/secrets`, `apps/cvmm`, `apps/helm` and the rest through `AppLog`) are standalone `[Console]` programs, not desk panes. The desk's own road was fixed 2026-08-19 (`GopFacts` over `GopDisk.disk-read-into`/`disk-write-into`, crossed by `codex/test/apps/gopfacts-cross`) and `GopEdit`/`GopReview` read it today. **The layering also forbids the fix this row implied**: `codex.os` may not cite `apps/`(DevelopersRulebook, "codex.foreword to codex to codex.os to app quires, never to its right"), so the kernel road can never reach the desk-selected medium. What is genuinely open is a different thing, and it is WORKS-61. |
| WORKS-61 | **The identity is an ESP file and is absent from the fact store. PRICED 2026-09-07 AND IT BUYS NOTHING YET: do not take it until a CONSUMER exists.** | Priced by measurement rather than argument, and the answer is that the missing piece is not a representation. **Nothing in the desk consumes the identity at all**: no chapter under `apps/works` outside `GopWizard` references `Keypair` or `sign-content`, none loads a keypair from the stored identity, and `GopReview` and `GopEdit` (the two panes that write signed-looking things) reference neither. The only readers are `GopWizard` itself, for the unlock flow, and `GopBoot`'s `key-zero` for locking. So promoting the identity to a fact would add a second representation with no reader, which is the exact shape that made WORKS-46 closeable: a path nothing calls (L-UNCALLED). The prerequisite is something that needs to SIGN with the identity, and when that lands it will say what shape it wants. Until then this row is a note, not work. The identity IS taken and IS persisted. `GopWizard`'s `wizard-run` runs the full ceremony (passphrase, entropy, Ed25519 keygen) and `wz-save-identity` writes salt, IV, public key, the wrapped private key and the vouch to `IDENTITY.DAT` through `gfat-mount-esp`, on the desk-selected medium, with `wz-identity-present` and `wz-load-identity` reading it back. What `GopBoot.codex:383` means by "Persisting your identity here arrives with B3.5" is only that the identity is not a FACT in the store shown on that Storage screen, so nothing in the trust lattice or the repository protocol can reference it as one. The `sysdb-kind-identity` record in `IdentityManager` is a written format for exactly that, stranded on the kernel road layering forbids the desk to reach; a fact write would go beside `gfs-write-and-checkpoint`, the way `GopReview` persists a verdict. Found 2026-09-07 (red) auditing WORKS-46. B3.5 is named in the source and registered nowhere else. |
| WORKS-41 | **The newtab page overflows a short box, and everything laid past the bottom is clipped away with no way to scroll to it** | Found 2026-08-18 (val) converting the Browser to a step. **MEASURED 2026-08-26 (val) and the mechanism this row used to state is wrong**, so the arm is `codex/test/apps/browser-newtab-overflow`, which drives the same functions `gbr-repaint` drives. Width held at 1024, only the height moves: at a 768-tall box nothing overflows (`info` ends at 750), at 400 five nodes are laid past the bottom (`links`, three of its buttons, and `info` at y 466), and at 1600 nothing overflows, which is the control. **`links` is the only flexible child and absorbs the slack** -- 470 tall at 768, 1302 at 1600, with a floor of 202 -- so overflow does not begin with ANY height reduction, it begins only once the box forces `links` below that floor, which is a box around 500 logical pixels. **The footer is NOT bottom-anchored**: `info` is the last child of the same `DirColumn` and is placed by the same accumulation as every other child. The real mechanism is one layer down in `flex-col-place` (`codex/foreword/ui/Layout.codex:72-83`), which places each child at a cursor advanced by `h + gap` with **no clamp against the container's bottom**, while `flex-col` clamps `avail` at zero. **And the content does not paint through the footer or into the taskbar**: `gbr-paint` walks the page under `vclip`, and `comp-clip` (`GopComposite.codex:628`) is a true intersection, so the overflow is invisible rather than overlapping. That makes this a REACHABILITY defect, not a painting one. What is still missing is what WORKS-23 wants one widget along: nothing scrolls a panel to its box. **Which screen modes cross the threshold is not settled here** -- this arm lays out against a box it is handed and does not compute the pane rect, which is shorter than the screen by the chrome, the titlebar and the taskbar strip. |
| WORKS-40 | **A very long Files session accumulates one frame per directory change, and nothing reclaims it until the pane closes** | Found and bounded 2026-08-18 (val), by design rather than by accident. A step that stores a new list into its state must keep the frame that holds it, so each directory change retains that iteration's repaint garbage along with the listing. **Measured: it is fully reclaimed on close** -- frontier and desk mark after ten directory changes are bit-identical to after none, and 100 changes in one session still render correctly. The exposure is one Files session left open across thousands of directory changes on the 128 MB boot arena, which is not a session anyone has run. **The unmeasured number is the per-change retention itself**, because reading the heap frontier needs the Monitor pane, and only one pane is focused at a time, so the instrument cannot observe the subject. That is the real gap here: an in-guest heap reading that does not require focus, which is the same missing instrument WORKS-32 asks for one level along. Until it exists this row is a bound, not a measurement. |
| WORKS-33 | **The system menu is still the full-screen Programs pane rather than a panel opening above its button** | The taskbar itself is done (2026-08-18, val): `desk-taskbar` is `menu` / `tasks` / `task-clock`, the clock moved out of the top bar into the band, `desk-dispatch` folds the `menu` id into the same `prg` the sidebar's Programs button uses so there is one dispatch and not two, and the `tasks` slot renders live apps from the marks cell. What is left is the menu's SHAPE. Its arm is `codex/test/apps/desk-taskbar-hit`. |
| WORKS-37 | **The idle desk was leaking 3.6 KB a second, and F12's verdict is now transient** | Both found 2026-08-18 (val) while converting `desk-monitor` to a step, neither by the arm that was running. **The leak is FIXED in the same CL.** Measured on the Monitor `memory` row: 69,896 B of gap after 10 seconds idle against 213,656 B after 50, linear at 3,594 B/s. `desk-clock` builds Text and walks the taskbar subtree on every RTC-second edge and `desk-loop` reclaimed none of it. On the 128 MB bare-metal arena that is about ten hours to exhaustion. It had never been seen because reading it means sitting idle and THEN opening the Monitor, and every earlier measurement opened a pane at once, which paused the leak by replacing the idle loop with the pane's own bracketed one. `desk-loop` now takes a mark per iteration and restores it on every continuing path; the mark sits above `root` so `root` survives, and the close path does not restore it because `desk-mon-close` restores to the BASE mark instead. After: frontier identical at 22 s open, 62 s open and 50 s idle, all `0x645b80`. **The clock half is FIXED 2026-08-18; the F12 half is still open.** The band is CONTESTED, not merely shared. Any pane that renders `desk-chrome-with` paints the taskbar from the widget tree, whose `task-clock` label is empty by design, so it ERASES the clock text the desk painted; and `desk-loop` calls `desk-clock` BEFORE the step, so in an iteration where both fire the pane wins and the band is left blank until the next second. Worse, the two gates read the clock differently -- the desk uses `rtc-seconds` (guarded, retries across the RTC update window) and the Clock pane uses `rtc-seconds-unguarded` -- so they see the second turn over on different iterations and which one lands last is a race. Measured in the Clock pane: taskbar clock present at 26 s open, absent at 14 s after a keypress-driven repaint. Every step pane has the milder version of this; the Clock pane has the worst because it repaints every second. **Proposed fix, one CL across all five step panes**: the step returns 2 for "alive and repainted" beside 1 for "alive", and `desk-loop` forces exactly one clock repaint when it sees 2. That costs one repaint per pane repaint instead of one per loop iteration, which is why it cannot simply be forced unconditionally. FIXED, and not by the return-value change proposed above: every chrome render now goes through `dk-chrome-paint`, which renders and then sets `desk-second-cell` to a value no second can equal, so the next `desk-clock` repaints unconditionally and the next one is microseconds away rather than up to a second. That is one band repaint per chrome render instead of one per loop iteration, and it needed no protocol change and no signature churn across six panes. `desk-draw` does the same for the desk's own paint. Verified on the Clock pane, which is the worst case because it repaints every second: the taskbar clock is present in four captures at 14, 17, 21 and 26 seconds open, and present in the exact keypress-driven frame that was blank before. Appearance after two scheme changes shows it too, in the lcars accent. **Still open: F12's verdict is still transient**, for the same reason and now the only one, and 2026-08-18 it has a number: in the Issues pane the verdict was on the glass 250 ms after the keypress and gone at 400 ms, and the window varies with where in the RTC second the shot lands, which is exactly what being erased by the next clock repaint looks like -- it is hand-painted into the band and the next chrome render takes it. Where transient notifications live is still the open question. Also still open, and unrelated to this fix: an UNCONVERTED loop pane freezes the clock rather than blanking it, because a pane like Files draws a window instead of re-rendering the chrome, so the desk's last-painted text simply stays. Converting those panes is what fixes that. |
| WORKS-34 | **The taskbar band is too short to host a themed button** | Found 2026-08-18 (val) while building the taskbar's system-menu entry, and it is why that entry is a label rather than a button. Measured: the band is `dk-task-h` = 28 LOGICAL at every resolution. A themed button needs 36 to draw its box -- the taskbar panel's own padding takes 8 (`edges-uniform 4` in `dk-theme-palette`'s `base`), the button's margin 4, its padding 8, and `comp-glyph-h` is 16. At 28 the box comes out 16 tall with 16-tall text overflowing it, which renders as a green sliver with the text cut through the middle; captured before the entry was changed to a label. Labels do not have the problem because `comp-draw-node` draws a `WkLabel` as text only, with no box to clip against. **Raising it is not a one-line change**: `dk-task-h` is also what the hand-drawn panes reserve (`desk-mon-draw`, `dk-win-y`), and `GopScene` hand-syncs its own `gsc-taskbar-h = 28` to it, so the band, the reservation and the 3D pane's copy have to move together or the band will cover pane content at 1600 and above. |
| WORKS-35 | **The Monitor pane's rows are hard-coded to a 160-pixel sidebar and land under it at 1600** | Found 2026-08-18 (val) while closing WORKS-31, which was the same defect one file over. `dk-mon-x = 160` is PHYSICAL and unscaled, and `desk-mon-row` draws at `dk-mon-x + 12 * s`. The desk's sidebar column is 160 LOGICAL, so at 1600 and above (`ui-wscale` = 2) it is 320 physical and the Monitor's label column starts at 184, inside it. Unmeasured on the glass: the reasoning is from the source, and the capture has not been taken. The fix is the one WORKS-31 used -- scale by `ui-wscale`, or better, read the sidebar's laid width off the tree instead of restating it. |
| WORKS-30 | **The 3D camera's aspect is the literal 1.333. FIXED for the desk's 3D pane; three other surfaces still carry it** | **DONE for `GopScene` (val, 2026-08-18).** `gsc-camera` now takes an aspect and applies it with `__record-set c "c3-aspect"`, and `gsc-frame` computes it per render path, because **the two paths do not share a viewport**: software renders into `tgt` at `cw x ch` while the host rasterizer renders into `gv` at `cw x (ch - gsc-label-band)`, a 20-row difference. Setting the field rather than changing `camera3d-new` leaves the other eighteen callers and every engine golden untouched, and keeps the change out of `codex/foreword`, so it is not seed-affecting. **Measured, frozen clock, three aspects including one the fix had never seen:** sphere ratio **1.037 at 1024x768** (viewport 1.3333), **1.042 at 1600x900** (1.8462, was 1.440), **1.038 at 1920x1080** (1.8333). All within 0.5 per cent of each other. **And the software path was checked separately, because the fix gives each path its own aspect**: toggling `G` at 1600x900 reads **1.041** against the host path's 1.042, so the two agree to 0.1 per cent (the bounding boxes differ slightly, 277x266 against 270x259, which is the 20-row viewport difference and is expected). **Two controls passed.** The 1024 arm, which was already correct because its GPU viewport is exactly 1.3333, did not move at all (224x216 before and after). And at 1600 the bounding box went 373x259 to 270x259 -- **the height is identical and only the width changed**, which is what an X-only correction must do and what a wrong fix would not have produced. **The old 7 per cent residual was my arithmetic, not the renderer**: the first version of this row divided by the SOFTWARE target height while the measured path was the host rasterizer. Against the correct viewport the residual is 3.7 per cent, and it is explained -- the ball is `mesh-sphere 700 12 8`, a 12-segment polyhedron whose silhouette ratio reaches `1/cos(15 degrees)` = 1.0353 against the measured 1.037, so **1.037 is the mesh's own floor and not a defect**. **What is still open:** `GopFish`, `apps/globe/GlobeDemo.codex` and `apps/engine-demo/EngineDemo.codex` all build cameras with the same three-argument `camera3d-new` and therefore still run at 1.333; none has been measured. The durable question they raise is whether `camera3d-new` should keep a default at all, or require the aspect and force all nineteen callers to say what their viewport is. Previous measurement, retained because the arm is reusable: | (`-Rtc`, which pins the HPET as well as the CMOS so the RTC-driven orbit angle is identical) so the two frames differ only in resolution. The yellow sphere's bounding box is **224x216, ratio 1.037, at 1024x768** and **373x259, ratio 1.440, at 1600x900**: a circle at 4:3 and a visibly stretched ellipse at 16:9. **The mechanism is a constant, not a missing term, and the first version of this row said the opposite.** `camera3d-new` (`codex/foreword/engine/Scene3D.codex:56`) takes eye, target and fov and NO aspect argument; its constructor sets `c3-aspect = 1.333` (`:64`); `camera3d-proj` (`:71`) hands it to `mat4-perspective`, which divides X by it (`codex/foreword/math/Matrix4.codex:87`). **`c3-aspect` occurs exactly three times in the tree, all in `Scene3D.codex`, and no caller overrides it.** So on-screen aspect is `viewport_aspect / 1.333`. **The fix is to set `c3-aspect` from the content region at the caller. Do NOT add a correction to `r3d-project-clip` instead** -- the camera already divides by it and a second correction squashes the scene the other way at every resolution; if it ever moves into the projection, `c3-aspect` must become 1.0 in the same CL. Both render paths share the camera (`GpuScene.codex:110`), so the `G` toggle must not change the symptom. **The RATIO between arms predicts to 0.2 per cent** (predicted `(1440/800)/(864/668)` = 1.3917 against measured `1.440/1.037` = 1.3886) **but the ABSOLUTE prediction is out by a consistent 7 per cent** (0.970 and 1.350 predicted against 1.037 and 1.440 measured, both high by 1.069) and that residual is unexplained -- mesh non-sphericity, the pixel filter clipping the silhouette, or the Cordic fov are all uneliminated. Do not close this row on the ratio alone: if the arms converge after the fix to a value far from 1.0, something else is scaling the sphere. **Calibrate the pixel filter before trusting it**: a first attempt thresholded at `R > 130`, measured only the specular highlight, and returned a 106x36 box that is not the sphere. The sphere is `B < 20 & R > 40 & G > 30 & 0.6R < G < R`. |
| WORKS-24 | **The Clock's write to the RTC has never run on hardware that accepts it** | The Clock accessory (main 14905) sets hour, minute, second, day, month and year by writing the MC146818 inside a Status-B SET window. **No bed run can tell a correct write from a broken one**: `tools/codex-vm.c` answers CMOS reads from the host clock and drops every write on the floor -- the line is literally `/* write to CMOS -- ignore */` at 11098. Measured 2026-08-13: bump the hour, press Enter, and the face comes back showing the old hour, which is the emulator behaving as written and says nothing about the guest. **What IS verified is the part that carries the risk.** `codex/test/apps/clock-encode-test` round-trips `clk-decode-hour (clk-encode-hour h b) h` for all 24 hours across all four modes the part can be in (BCD or binary, 12-hour or 24-hour) and every field range, and it pins four values computed by hand FROM THE SPEC rather than from the code -- 18, 146, 129, 35 -- so a round trip that passes with both halves wrong still fails. The port writes themselves are two instructions. **What is left is one boot on metal**: open Clock, `s`, change the minute, Enter, then leave the pane and re-enter it. If the new time sticks, the SET window and the encoding are right on real silicon. Until someone does that, do not describe setting the clock as working. The diag ladder's `rtcw` stage (`build/boot/diag/DiagRtcW.codex`) is that boot on every flight of the stick: it writes the seconds register inside a SET window, reads it back and restores it, `accepted` or `ignored`; codex-vm reads `ignored`. |
| WORKS-16 | **An editor crash that went away, with no proof of what fixed it** | 2026-08-11: Damian opened `SOURCE.SRC` in the Edit pane on the interactive VM, saw the first screen render, and the VM died. **It does not happen any more** -- he opened the whole file and navigated it on the same path after main 14685. Four headless beds never reproduced it at any point (28 keys over 70 s, 50 keys at 300 ms, an open/close/reopen cycle, 120 scripted mouse samples across the list), so **the two defects fixed in 14683 are a plausible cause and not a demonstrated one.** The plausible half: he picked the file with the pointer, and until 14683 every hovered-row change ran a two-function cycle that both left stack frames behind and skipped its `__heap-restore`. That is the one path his run exercised heavily and the bed did not, because the bed's mouse never moved until the last arm. **New evidence 2026-08-11, and it argues against the fix explaining it.** Three headless runs that day ended with codex-vm exiting early, no frame captured, **and no `!EXC=` line, no "Guest halted", no watchpoint** -- the stderr simply stops mid-word. That is not the shape of a guest fault; it is the shape of the HOST process dying, which is also what "crashed the whole vm" describes. Other runs with the same command line and the same guest completed normally, so it is intermittent. Nobody has looked at whether codex-vm can fault on the host side under this load. **Left open deliberately.** If it stays gone through real use, delete this row; if it returns, the guest's own `!EXC=` line is the thing to capture (`build/desk.ps1 -Wait -Force -Disk seed/Codex.img` prints it to the launching console) rather than another headless arm. |
| WORKS-19 | **Typing near the top of a large file moves the whole tail, and MEASURED it is not slow. The gap buffer is not indicated; what is unmeasured is metal** | The buffer is flat, so an insert at offset 0 of `SOURCE.SRC` shifts the whole tail one byte and reindexes every line. That much is true and it is the design. **What this row used to claim past that was "it is visibly slow on that file", and 2026-08-19 (val) measured it false in the bed.** Per keystroke at offset 0 of 2,896,050 bytes: `ged-shift-up` **5 ms**, `ged-reindex` **5 ms**, building the 30 visible rows of Text **2 ms**. End to end on the real surface, ten characters typed 60 ms apart (16 a second, faster than most people type) ALL land: the pane reads `L1:11  2896060 bytes`, and a 307-byte control typed identically reads `L1:11  317 bytes`. The two are indistinguishable, which is the arm that matters, because a cost proportional to file size would separate them. So a gap buffer would be a data-structure change against a 12 ms keystroke, and nothing we can measure asks for it. **Two corrections to the old row while here:** the line count is 62,184 and not 59,461 (L-COUNT, it was carried forward), and the first version of the probe reported 2 ms for the shift because the loop results were unused and the work was being ELIDED -- the numbers above are from the version that threads an accumulator and prints it, and 400 reindexes summing to exactly 400 x 62,184 is what says the loop ran. **What is genuinely open is metal**, where the desk's iteration is far slower for reasons that have nothing to do with this buffer, and where nobody has typed into a 2.77 MB file. Probe: `docs/Probes/edit-keystroke-cost.codex`. |
| WORKS-17 | **The syntax scheme is still the editor's own** | The frame half is DONE (main 17702) and the contrast half is DONE (main 17716): `GopFiles` and `GopEdit` take a `Palette` from the desk and paint through named role accessors, and a selected row is the accent as ground with `theme-ink-on` picking a legible ink from that palette's own two text colours. Computed rather than a stored `pal-on-accent` field, because there are 36 `Palette` literals in 32 files and a stored token fails silently when somebody adds the 37th; `codex/test/ui/theme-ink-on` pins it and prints "SAME, the ground is being ignored" if the helper ever stops reading the ground. A scheme wanting to override a specific pair can still be given a field. **What is left is the syntax question, and it should not be answered by reflex** (blu, 2026-08-11): `syn-colour` maps a token class to a colour and this work did not touch it, so a code editor keeps its own scheme. Decide whether `Theme` grows a syntax block or `SyntaxHighlight` keeps its own and only the frame follows the desk. A `minimal` desk on a white ground needs a light syntax set that does not exist, so it is real work either way and not a rename. |
| WORKS-12 | **A pane visit is not reclaimed. Twelve panes done, one deliberately not** | Noticed 2026-08-11 (blu) bracketing the browser pane's own loop. **The stated cause was only the smaller half** (reek, 2026-08-15). Stranded pane state is real, but the dominant per-visit cost is the desk never unwinding: every pane tail-calls `desk-loop` again, so the `root` tree each exit rebuilds is stacked on top of the root it replaces and neither is ever freed. A mark taken inside the pane cannot reach that root, because the root is allocated after the pane returns. **The fix is one base mark, not a bracket per pane**: `desk-run` stores the frontier in `desk-mark-cell` (ds 4) after the state blocks and BEFORE the first root, and a pane exit restores to it before `desk-draw` rebuilds. **Measured on the desk under codex-vm, four browser visits, base 0x6320a8:** frontier 0x6611f0 without the restore (193,352 bytes above base, about 48 KB a visit) against 0x6376e0 with it (22,072, the live root plus the Monitor pane's own paint, and flat in the number of visits). **DONE for browser, calc, cal, trk, dif, files, monitor, programs, clock, style, console, fish and scene** -- the last two had their own local marks, which reclaimed the pane but not the root, and now restore to the base like the rest. **NOT DONE for `desk-edit`, and that is the interesting one.** `ged-init` allocates an 8 MB buffer and a 1 MB line index on first use and parks both POINTERS in a persistent `ds` cell, so a base-mark restore would free the memory and leave the cell pointing at it, and `ged-ensure` would hand the next visit freed heap. The honest fix is in `GopEdit`: allocate on entry and release on exit, or reserve the block in `desk-run` where every other pointer cell already lives, which costs 9 MB at boot for a pane that may never be opened. That is a GopEdit decision rather than a desk one. `GopConsole` had the same shape at 1 KB and was fixed the cheap way: `gcon-init` runs eagerly in `desk-run` now. **The rule this leaves behind:** a pointer parked in a `ds` cell must be allocated in `desk-run`, below the mark. The instrument is the Monitor pane's `memory` row, which prints the frontier and the base mark; the gap between them is the answer. |
| WORKS-13 | **A tracker row is not clickable, and the 3D pane never receives the mouse** | The two gaps left after the mouse was wired into every desk pane 2026-08-11 (blu). **Issues:** `gtk-tree` builds its rows through the generic `data-table-widget`, which gives no per-row widget id, so `ev-hit-widget` has nothing to answer with and a click cannot say WHICH row it landed on. Selection stays keyboard-only (up/down). Fixing it means either per-row ids from the table widget, which is a `codex/foreword/ui` change affecting every table in the tree, or the pane computing a row index from the table's own bounds, which re-derives layout arithmetic the layout engine already did. **3D View: CLOSED 2026-08-19 (val).** `desk-scene-step` and `gsc-step` both take `UsbMouse` now and the camera answers it. The design question it was waiting on was settled as a TAKEOVER rather than an addition: the angle came from `gsc-orbit`, which the HPET drives and never stops, so adding an offset to a moving angle gives a view that drifts out from under the hand. The first pointer movement latches `sp-look` and the orbit stops being consulted, so the scene turns itself until you touch it and after that you turn it; there is no way back short of closing the pane, which is the honest cost of one bit. Both 3D panes get it from the one change because they share the step. Neither blocks anything: the cursor tracks in every OTHER pane and the keyboard reaches everything. | **CLOSED 2026-08-19 (fester), and the premise above was stale.** The per-row ids already existed: `dt-row-widget` names each row `<table>-row-<absolute index>` and `dt-cell-widgets` names each cell `<table>-cell-<row key>-<column>`. What was missing was the other end, so `data-table-row-of-hit` reads either shape back to a row index (`DataTable.codex`); `ev-hit-widget` answers with the DEEPEST node, so an ordinary click lands on a cell and only a click in the row padding lands on the row panel, and a helper reading one shape would have missed most clicks. `gtk-row-at` asks it and `desk-trk-hit` brackets a layout, reads the integer and drops the tree -- the shape the desk already documents for hit tests -- and `desk-trk-step` takes `clicked` and moves the selection. **What is proven and what is not:** 14 arms in `codex/test/ui/data-table-hit`, three of which go the whole path a click goes (lay out, `ev-hit-widget` at a POINT, id, row index) and the rest of which cover the misses -- another table, the header, a header button, the footer, an absent key, an empty id. **The mouse itself is unexercised: no bed here can press it.** `desk.ps1` scripts scancodes and codex-vm takes `-keys` and no pointer, so "the desk delivers a click to this pane" is carried by the same machinery five other panes already use and is not tested here. The fear in the original entry -- that this needed per-row ids from the table widget, a `codex/foreword/ui` change affecting every table -- did not arise.
| WORKS-9 | **The ASUS shot-2 timeout is explained** (reek). **The flight card for the next sitting is `docs/Hardware/HardwareSitting.md`, "WHICH BYTES ARE QUEUED: REBUILT AND RE-REHEARSED 2026-08-18"** -- image, hash, flash command, what the operator watches, and what not to conclude. | **The three DRIVER defects behind it are fixed at main 14447** and the account is `docs/Hardware/HardwareSitting.md`, "THE SHOT-2 TIMEOUT ABOVE IS THREE DEFECTS DEEP". A retried chunk now survives a dropped completion, so a second sustained write is no longer lost either way. **What is NOT explained is the original question**: `xhci-fuel` is a spin count and nobody has converted it to a duration on that box, and no bed can, because codex-vm completes every transfer before the guest spins once, so `f` reads exactly 1000000 there. The instrument for the next flight is the `f`, `l`, `r` cells on the shot line rather than the reset-and-retry arm this row used to recommend: `f` is the SMALLEST fuel any COMPLETED transfer left behind, which reads off the transfers that succeeded instead of the one that failed, so a small `f` says the budget was marginal and an `f` near 1000000 says the fuel is innocent and the device stopped answering. **The arm blu originally routed here was wrong and is recorded as wrong so nobody rebuilds it**: it called `xhci-recover-endpoint`, which leads with Reset Endpoint, defined only for a HALTED endpoint, and a timeout leaves the endpoint RUNNING, so it answers Context State Error and recovery refuses. New bed levers for anyone reproducing this: `-usb-bot-drop N` and Bulk-Only Mass Storage Reset, both in `docs/OperatorsManual.md`. **SITTING 6, 2026-08-20: the ladder returned a NUMBER on metal for the first time.** Flown by red; banked here because the reading was GLASS ONLY and the bank dies at the sink (L-BANK), so a photograph was the only other copy. `sink ladder-stop done=4 rung-sectors=16 rung-bytes=8192 payload-bytes=65536 note=1, wr=128 cc=256 lba=2169 rty=1 ph=2 after=0 chunk=16`. Four sittings of one bit each reached this; it is the threshold reading and every later one is a comparison against it. |
| WORKS-6 | **The boot path verifies under Secure Boot, and GetMemoryMap is sized dynamically** | Re-homed from BootRoadmap.md (B5.1/B5.2) when that design moved to Done 2026-08-05: the shipped stub boots with Secure Boot off; signing the PE for Secure Boot has no design and no owner. GetMemoryMap's buffer is statically sized; a machine with a bigger map than the allowance fails at ExitBootServices. Neither blocks any current flight. |
| WORKS-7 | **DevDebugger hardware watchpoints and VGA split-screen** | Re-homed from DEVELOPER-DEBUGGER.md Phase 4 when that design moved to Done 2026-08-05: DR-register watchpoints are blocked on `mov dr` builtins (a compiler change plus a seed cycle -- neither exists); VGA split-screen was never built. The I/O-bitmap breakpoint row was cut per the design's own reclassify-or-cut ruling. |
| WORKS-5 | **Dev Console and Serial REPL are reachable from the boot menu** | Removed from GopBoot's interface menu 2026-08-05 (they painted "Selected:" and returned; a menu that only offers what works beats one that promises). Wiring Dev Console means running DevConsoleBoot's console without the UEFI ConIn/ConOut it is written against (gone on the Option A path), and its source-tree indexing returns placeholders (README completeness line); as an alternate payload under OVMF it reaches a black screen and OUT OF MEMORY (build-boot-img.ps1 header). Serial REPL means chaining from the payload into the REPL the compiler serves, which is loader work, not menu wiring. Each is more than a session. When one is wired and proven, add its row back to menu-label/menu-count and give it a dispatcher in menu-loop. |
| WORKS-2 | **A GGUF this app WRITES is readable by llama.cpp** | The read direction is closed and measured (main 10603): `build/gguf-foreign-test.ps1` parses four real llama.cpp models, up to a 3.2 GB gemma3 with a 15.7 MB metadata block, agreeing with an independent host parse on version, tensor count, KV count, architecture, tensor-table offset and first tensor name. Nothing checks the other direction. `build/make-agent-bundle.ps1` writes the bundled model and `Foreword chapter Gguf` reads it, so a green `agent-bundle-test` still says only that the two halves agree with each other. Ollama is installed on this box, so the instrument exists: feed it a Codex-written GGUF and see whether it loads. Deferred by Damian 2026-07-26, not blocked. |
| WORKS-3 | **The diffusion app is finished** | `apps/works/GopDiffusion.codex` (ported from `apps/guios/DiffusionApp.codex`; the port closed the GuiDisplay/GuiTimer/GopRender gating) is an unfinished AssetForge UI: the Generate button dispatches to nothing. `df-draw-generating` fills a progress bar from a counter to 20 and produces no image; there is no model load, no sampler, no inference. Finishing it means the button doing something, which is a capability question and not a UI one -- the foreword has `ai/` (NeuralNet, Tensor, Activation) at the scale of `inference-demo` and `neural-test`, which is a long way from a latent diffusion sampler. **Not blocked, and not urgent.** |
