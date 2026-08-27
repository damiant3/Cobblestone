# The Desk Contract

What a desk pane may assume and what it must not break. `GopDesk` is one
chapter that thirteen panes reach through, and the invariants below are not
visible from inside any one of them.

Read this before adding a pane, changing how a pane exits, or taking a `ds`
cell. Two defects in one week came from invariants that were true and unwritten:
a `ds` cell taken by two authors, and a `ds` cell read by one pane and written
by nobody.

`apps/works/works-backlog.md` is the register of what is still missing. This
file is the standing rules, not the open work.

## 0. Two kinds of pane

**Every pane is a STEP. No pane owns a loop.** All fourteen: Monitor, Calendar,
Appearance, Calculator, Clock, Programs, Diffusion, Issues, Console, Files,
Browser, 3D View, Aquarium, Editor. Sections 1 to 7 below still hold and are
written in terms of the loop the desk itself runs.

This paragraph carried the transition's own leftovers until 2026-08-21, and
they said both "no pane is a loop any more" and "every other pane is still a
loop" four sentences apart. If you are adding a pane, it is a step.

**To convert a pane, or to add one as a step**, it is the same two places
section 4 already names for a loop pane: a line in `desk-step-of`, which is
the only function that knows which id means which app, and a line in
`desk-dispatch` that takes the focus id and does the entry paint. `desk-loop`
names no pane at all. A pane whose entry paint is one-shot, like the Calendar's
month or the Diffusion form, puts that paint in its `-open` function and needs
no step of its own at all: it takes the shared `desk-quiet-step`, which answers
Esc and F12 and nothing else, so such a pane costs one line in `desk-step-of`
and no new function. A pane that repaints on a clock, like the Monitor, carries
that gate in its own step.

A step is `<pane>-step : ..., sc, clicked -> Integer`: it handles ONE event,
paints if it needs to, and answers **0 to close, 1 to stay open, a NEGATIVE
number to stay open without the frame restore, `desk-step-hide` to leave the
pane ALIVE and return to the desk, and anything else as a SCANCODE to dispatch
after closing**. `desk-step-hide` is 1,000,000, which no scancode can reach. The scancode answer is the launcher's, which does
not merely close but names the app to open; it is unambiguous rather than a
convention, because every scancode `desk-dispatch` tests is 2 or more, 1 is Esc
and already means close, and 0 is no key. The negative answer is Files', and
what it is for is under "Typed state" below.
**The event is
the pair**, a scancode and whether the pointer was clicked this iteration; a
pane that reads only keys ignores the second, and `desk-step-of` passes both to
everyone. `desk-loop` keeps the keyboard, pumps the mouse and moves the cursor
BEFORE any step runs, which is what `dk-pump` used to do inside each pane's own
loop, so no step handles the pointer's motion itself. What follows, and all of
it is measured:

- **The desk goes on running while the pane is up.** Its clock keeps time, its
  mouse keeps answering. Under a loop pane the taskbar clock freezes for
  exactly as long as the pane is open: measured 4 s and 18 s at two open
  times before the conversion, and 0 after.
- **A step does not stack a frame**, because it returns. The visit path for a
  step pane is not the mutual recursion section 1 describes.
- **A step MUST bracket its own allocations.** `desk-loop` brackets each
  iteration, but a step that parks a pointer somewhere persistent defeats
  that the same way section 3 describes for lazy allocations.
- **State that outlives a step lives in `ds` or in `desk-run`**, never in a
  `let` above a loop, because there is no loop. `desk-mon-step` re-parses
  ACPI on its repaint edge for exactly this reason: `AcpiInfo` is a record
  and a `ds` cell holds raw addresses.
- **Typed state goes in the `DeskApps` record threaded beside `root`, never in a
  `ds` cell.** Cells hold integers and raw addresses; a `Fat16Volume` or a
  `BrowserState` is neither. A pane fills its field in its `-open`, which runs
  after `desk-run` took the base mark and before `desk-loop` takes its
  per-iteration mark, so the value sits between the two: the per-iteration
  restore never reaches it and it survives every step the pane is open for.
  **It does NOT survive a close, deliberately** -- `desk-app-close` restores to
  the base mark, which frees it along with the pane's entry paint, so both
  close paths DROP the record they were handed and build an empty one AFTER the
  restore. Carrying the old one across would carry pointers into memory just
  handed back. State that must outlive a close still belongs in a `ds` block
  allocated in `desk-run`, which is how the Calculator keeps its number.
  **`GopEdit`'s 9 MB must not move into this record**: its POINTER is parked
  in a cell and only Edit's typed values belong here. This bullet said the
  cell has no restore so the base mark cannot reach the buffer; the pointer
  survives a restore, the memory does not, and section 3 carries the
  correction and the fix.
- **A step may STORE into its record in place, and what it stores decides how
  it must answer.** `__record-set` is an in-place field store, so a step
  changes its own state without a new record and without a parameter. Storing
  an INTEGER is free: it is not a pointer into the frame, so the step answers 1
  and the restore is correct. Storing a new LIST or a new record is not: the
  value was allocated inside this iteration and the restore frees exactly it,
  leaving the record pointing at reclaimed memory that the next repaint walks.
  Such a step answers NEGATIVE, which keeps the frame. Files does both: moving
  the selection stores two integers and answers 1, changing directory stores
  three lists and answers negative. **The negative answer is not a convenience
  and must not be reached for on a repaint** -- it retains that iteration's
  transient allocations along with the state, so it is affordable only at
  human-decision rate. Measured on Files: heap frontier and desk mark after ten
  directory changes are bit-identical to after none, because `desk-app-close`
  reclaims the lot; 100 changes in one session still render correctly.
- **A step that restores BELOW the desk's frame mark must answer negative, and
  this is the sharper edge of the same rule.** A pane with its own heap
  discipline, like the Browser releasing its memoized widget tree, restores to a
  mark it took when it opened, which is under the mark `desk-loop` took this
  iteration. If the desk then restores to its own frame mark it pushes the
  frontier back UP, over memory the pane has just allocated, and the next
  allocation hands out live memory twice. The negative answer is what keeps the
  desk's hands off it.
- **Whatever holds the pointer to a pane's state must be allocated BEFORE the
  pane takes any mark it will later restore to.** The desk builds `DeskApps`
  around the value a pane's `-open` returns, so an `-open` that paints (and
  therefore takes a mark) before returning hands back a record that its own
  first keystroke frees. This is not theoretical: written that way the Browser
  crashed the guest on the third scancode, because the desk went on reading a
  record the pane had released. `gbr-new` and `gbr-first-paint` are two calls
  for exactly this reason, and a heavy pane added later should be shaped the
  same way.
- **A step whose work is a FRAME, not a poll, needs nothing special, and the 3D
  panes are the proof.** Everything a frame builds is freed by `desk-loop`'s own
  bracket, which is the job the pane's loop mark used to do, and the toggles and
  counters are integers stored in place, so nothing here ever keeps a frame.
  What changes is the desk's PACE: an iteration is now a frame, so the clock
  advances no finer than the frame rate -- about 16 ms in the bed on the host
  rasterizer, 86 to 135 ms on the software path, near a second on metal. It
  still advances, which is the whole point.
- **A step must drain the keyboard itself if its iteration is slow.**
  `kbd-pump-one` advances a THREE-phase state machine one step per call and
  `kbd-take` calls it once, so one take per iteration is one PHASE per
  iteration. `desk-loop` takes exactly once. On a pane whose iteration costs a
  frame that is a report every three frames, and on metal it measured as keys
  swallowed unless held down and Esc failing to close the pane (ASUS,
  2026-08-08, which is why `gsc-poll` exists at all). `gsc-step` uses the desk's
  scancode when it found one and polls further when it did not. Any future step
  slower than a poll needs the same, and a step that also OWNS a key must read
  it from its own poll rather than from the desk's `sc`, or the poll will
  swallow it.
- **Paint the chrome through `dk-chrome-paint`, never `comp-render` directly.**
  The band is painted from the widget tree, whose `task-clock` label is empty
  because the clock's TEXT is hand-painted over it once a second, so every
  chrome render erases the clock. `dk-chrome-paint` renders and then
  invalidates the clock gate, so the next `desk-clock` repaints microseconds
  later instead of up to a second later. Calling `comp-render` yourself brings
  the flicker back, and it was a race rather than a constant: the desk gates on
  `rtc-seconds` and the Clock pane on `rtc-seconds-unguarded`, which see the
  second turn over on different iterations, so before this existed the clock
  measured present at 26 s open and absent at 14 s.
- **A still screen inside a pane is a MODE, not a nested loop.** The Files
  preview was the last modal loop in the desk and it needed no state at all
  beyond one integer, because nothing repaints it. Before reaching for a record
  to hold a sub-screen, ask whether anything will ever redraw it: if not, paint
  it once, take a mode, and let the bracket drop everything the paint built.
- **Anything hand-painted into the taskbar band is transient** for a step
  pane. The desk repaints that band once a second, so the F12 verdict shows
  and is gone within the second. Under a loop pane it survived, because the
  desk was not running to overwrite it.

## 0.5 An app may now stay ALIVE while you use another one

**Files, and since 2026-08-19 the two 3D panes.** Tab in the Files listing answers
`desk-step-hide`, which returns to the desk WITHOUT closing: the focus cell
goes to none, the record is carried through untouched, and nothing is
restored. Pressing `f` again re-enters through `desk-files-focus`, which
repaints from the existing `FilesState` rather than calling `desk-files-open`.
Measured: Files left at `ESP:/EFI/`, the Calculator opened and closed, `f`
again, and the pane comes back at `ESP:/EFI/`. The prediction that would have
falsified it was written first, and it is that the capture reads `ESP:/` with
four entries and the selection at 0.

**The mark stack is what makes it correct, and the LIFO is not negotiable.**
`desk-mark-cell` is one mark and one mark can only free everything. Keeping an
app alive means the frontier at each open is remembered: cell 20 holds a
stack of `(focus id, mark)` pairs pushed by a heavy pane's `-open`. On close
the pane's entry is marked DEAD rather than popped, then every dead entry on
TOP is popped and the lowest mark among them restored. An app that quits under
a live one therefore keeps its bytes until the app above it quits, which is
the cascade paying them all back at once. When the stack empties the close
restores to `desk-mark-cell` exactly as it always did, so a single-app session
is byte-for-byte the behaviour that shipped before.

**The cost, measured, because it is real.** Each hide-and-return costs a desk
root: heap frontier `0x672848` after one switch against `0x6a3a18` after ten,
so **22,352 bytes per switch** while the app stays alive. It is all reclaimed
when the app finally closes: frontier `0x645ce8` and desk mark `0x63fda8` are
bit-identical after one switch-then-close and after ten. So this is bounded by
switches-while-open at a human's rate, not by time, and it is the same
per-visit root cost section 1 already describes.

**The two 3D panes joined on 2026-08-19 and are a different shape from Files.**
Scene and Fish share `da-scene` and share `gsc-step`, so one change wired both.
Three things are worth knowing before wiring another pane against this
precedent:

- **Re-entry needs no repaint of the pane itself.** `gsc-step` renders a whole
  frame every step, so `desk-scene-reenter` only redraws the desk CHROME and
  hands focus back. The chrome redraw is not optional: the intervening app
  paints outside the 3D viewport, and the scene never touches those pixels.
- **Tab must release the GPU viewport clip.** `gsc-step` answers
  `gsc-step-hide` from an arm that first calls `gs-viewport-release`, exactly
  as its Esc arm does. Leaving the clip armed would confine whatever draws
  next to the hidden pane's rectangle. `gsc-step-hide` is 2 and not
  `desk-step-hide`, because `GopScene` cannot cite `GopDesk`; `desk-scene-step`
  maps the one to the other.
- **Two panes over one field means the loser's entry must be killed.** Opening
  Fish while Scene is alive overwrites `da-scene`, so the Scene's state becomes
  unreachable while its stack entry is still LIVE, and a live entry nothing can
  kill is the exact condition that stops every later close from reclaiming.
  `desk-scene-open` kills the sibling's entry first. Measured: Scene opened,
  hidden, Fish opened, Fish closed leaves the frontier at `0x645ce8`, which is
  the same figure a single open-and-close leaves. Without the sibling kill that
  arm strands 8.2 MB for the rest of the session.

**A hidden 3D pane costs 8,215,112 bytes**, measured as the frontier with one
Scene alive and hidden (`0x0e1b6e8`) against a desk that never opened one
(`0x645ca0`). That is its render target, colour and depth at the viewport size,
and it is not reducible while the pane is alive: freeing it IS closing the
pane. A switch-away-and-back round trip costs a further 166,720 bytes. All of
it comes back on close, and it does not accumulate: one open-and-close, two
open-and-close, and two switch cycles then close all leave the frontier at
`0x645ce8`.

**Edit joined on 2026-08-19 too**, and it is the pane where wiring the stack
turned up a defect that had nothing to do with the stack: see "The one pane
this does not fit" in section 3 for the dangling 9 MB buffer and its fix. Tab
answers `ged-step-hide` only in the list and edit modes; `big` and `nofat` are
dead ends the user leaves with Esc and there is nothing in them to keep.
Re-entry repaints from `EditState` through `ged-reenter`, which picks the list
paint or the editor repaint by `ed-mode`.

**The Browser joined on 2026-08-19 and it is the one pane with a RULE attached:
it may only ever be the topmost heavy pane, so every other heavy `-open` evicts
it.** The reason is a property no other pane has. `gbr-repaint` takes
`bs-laid-mark` at the CURRENT frontier and rebuilds `bp-st` there, so a Browser
re-entered above another pane has its state above THAT pane's mark; when the
pane below closes and the cascade pops to its mark, the Browser's state goes
with it and the record still points at it. Files, Edit and the 3D panes cannot
do this, because their state is built once by their own `-open`, above their own
mark and below everything later.

So `desk-files-open`, `desk-edit-open` and `desk-scene-open` each call
`desk-browser-evict` and set `da-browser = None`. What this buys is the common
case: the Browser stays alive under any of the nine LIGHT panes, because a light
pane pushes no mark, and its close finds the Browser's entry live on top,
reclaims nothing, and leaves the Browser's marks untouched. Measured: two tabs
open, Tab, the Calculator opened and closed, `b` again, and the frame is
identical to a Browser that never left except for the strip named below, where a
fresh one differs across the whole tab bar (26,856 pixels). The eviction arm is
sharper still: two tabs, Tab, Files opened and closed, `b` again returns a
Browser **byte-identical to a fresh one-tab Browser**, and the frontier is back
to `0x645ce8`, so the cascade popped the dead Files entry and the dead evicted
Browser entry together.

**A live hidden Browser holds 43,800 bytes**, which is the cheapest of the four
by three orders of magnitude (the 3D panes hold 8.2 MB, Edit 9.6 MB).

**One visible rough edge, and it is NOT the desk's: `BROWSER-5`.** After a hide
and return the Browser's bottom edge sits 12 device rows higher than it did
before the hide, and the desk background shows in the gap. **The desk is not
what moved it.** Measured 2026-08-19 with nothing else running: open the Browser
and its bottom edge is at y 827; press ANY key and it moves to y 839 and stays
there, with the diff exactly the band y 828..839 and nothing else in the frame
changing, not even the address bar text. So the Browser's FIRST paint lays the
page 12 rows shorter than every repaint after it, and re-entry re-runs the first
paint through `gbr-first-paint`, which is why the edge goes back up. Neither
figure is the pane's actual bottom (843), so both layouts are wrong and by
different amounts. **My first diagnosis of this was wrong** -- it read as
`gbr-repaint` being incremental and leaving stale rows, and a fill of the region
before the repaint was tried on that theory and did not close it. The fill is
not in the tree; the row is in `apps/browser/browser-backlog.md`.

### The taskbar names what is alive

The `tasks` slot between the `Codex` label and the clock was an empty flex
spacer. It now reads the MARK STACK rather than a list of its own, and that is
what makes it correct by construction: an app is alive exactly when it holds a
live entry there, so the row cannot drift from the thing it reports. A dead
entry takes `desk-focus-none` and names nothing. Only the five heavy panes ever
push, so only `Files`, `Web`, `3D`, `Fish` and `Edit` can appear.

It is rebuilt by `desk-draw`, which runs at the two moments the live set changes
AND the desk is visible: `desk-app-hide` and `desk-app-close`. An open does not
redraw the desk and does not need to, because the pane covers it.

**The two arms that could have falsified it both ran.** With Files hidden the
row reads `Files`, and with the 3D pane hidden over it `Files   3D`. Closing
Files leaves a frame **byte-identical to a desk that never opened anything**, so
the row empties with no residue. And the eviction arm: Browser hidden, then
Files opened, reads `Files` ALONE rather than `Web   Files`, so the row tells
the truth about a pane the desk dropped rather than reporting what was asked for.

### The start menu is the launcher COLLAPSED, anchored above the pill

`gpr-menu-tree` shows one row per GROUP and expands the group holding the
selection. `gpr-key` and `gpr-id-scan` are still its whole app behaviour, so a
row and the key it names arrive by the same road and there is no second table
to keep in step. It shares `desk-prog-cell`, and the shared SELECTION is the
point rather than a saving: one model, two views. **The open group is a
FUNCTION of `gpr-sel`, not state of its own**, which is why up and down walking
off the end of a group opens the next one with no code that does it.

**A HEADING'S ID CARRIES `gpr-group-prefix` SO `gpr-id-scan` CANNOT MATCH IT.**
If the two namespaces overlapped, clicking a group would launch an app.
`codex/test/apps/desk-menu-groups` asserts both directions, because one
direction passes with a single function answering for both.

**A PANEL DOES SIZE TO ITS CHILDREN, AND THIS FILE SAID IT DOES NOT.**
`widget-measure` sets `wn-min-h` to `box-max` of the node's own minimum and the
SUM of its children's, and `widget-layout` is `widget-arrange (widget-measure w
th)`, so a minimum propagates. What actually bit is one line further down the
same page of `Widget.codex`: `widget-panel` defaults flex to 1 and
`widget-set-min` PRESERVES flex, so a box given only a minimum stretched rather
than collapsed. Both look like "the anchoring did not work" from the far side
of a capture. **A flex-0 body under a flex-1 spacer is the whole of the
anchoring**, and `flex-col-place` gives the body exactly its measured minimum.

**A SPACER IS A LABEL, NOT A PANEL.** A flex-1 `widget-panel` paints its own
background, so the anchoring spacer drew as an empty box above the menu. The
deleted sidebar used `widget-label "sidebar-spacer" ""` for this and it was the
proven shape; `sysmenu-tgap` is a label for the same reason. Caught by a
capture, invisible to every arm.

**WHAT THE ROOM ACTUALLY IS, MEASURED, AND WHY THE FLAT LIST DOES NOT FIT.**
The box above the pill has about 400 logical rows at 1600x900. Captured with
`gpr-tree` in it, the Shutdown row landed past the bottom of the glass, the
taskbar was behind it, and the box measured 1000 device pixels wide because a
panel minimum is a floor and `gpr-tree`'s own was larger than `desk-menu-w`.
At 1280x800 the same tree FITTED, which is the usual direction here: the
bigger screen is the tighter one.

**AND THE INSTRUMENT FOR THAT CANNOT COMPARE THE MENU TO THE BAND.** An
over-tall menu does not overlap the taskbar, it PUSHES it down: `flex-col-place`
moves the band ahead of it, so "the menu ends above the band" reported yes on
the broken shape while the band sat 38 rows off the bottom of a 450-row screen.
Two rectangles that move together cannot adjudicate each other. The row that
fails compares the band against the GLASS, which does not move.

**A ROW BUILT WITH `widget-set-min ... 0 h` RESERVES NO WIDTH AT ALL.**
`gpr-row` sets the width minimum to zero and lets the column stretch, so the
whole menu measures to 40 against the 520 it draws at and `desk-menu-w` is what
actually sizes it. That also disqualifies the menu as a specimen for anything
measuring the widget layer's eight-pixel cell: a tree that never asks the cell
for a width cannot measure it, and `desk-label-metrics` walks the taskbar for
that reason.

**Driving a click at this desk, because it is not obvious and it cost several
boots.** `-mouse` takes a host-tracked position starting at `0,0` and delivers
the DELTA between consecutive events, clamped to +-127 a sample, so a screen
coordinate handed to it straight moves the pointer 127 pixels and no further.
The desk used to need movement and button in the same report, because `clicked`
was `mv == 1 & mouse-clicked`. **It no longer does, and a scripted press need
not carry a movement** (val, 2026-08-26): cell 16 is a latch, `desk-loop` reads
it with `mouse-take-click`, and a press consumed on any earlier report is
answered on the next iteration whether or not one arrived. Moving a pixel on
the press sample is still harmless and still what the timelines here do.
A pointer starting centred at 800,450
reaches the taskbar button at 420,857 in four samples of about `-95,+102`,
ending at `-380,407`; negatives are how you go left and up. `OperatorsManual.md`
carries the wire.

**The taskbar's `menu` id did not change and must not.** It became a button
rather than a label, and that is the only visible change to a plain desk: 3,228
pixels in its own box. Renaming it to `sysmenu` broke `desk-taskbar-hit`, which
asserts that id resolves in the band. The test was right and the rename bought
nothing.

## 0.6 The annotation surface

`GopEdit` marks a definition line whose definition carries a trusted
annotation, reads them on F6, and writes one on F7. WORKS-15 is closed; what
follows is what a pane touching that surface must not re-learn.

**Do NOT use `CodeBrowser`'s `SourceIndex` to join annotations to lines.** It
takes the whole file as one Text, then holds every line as Text a second time,
appends with `list-snoc` once per definition, and recurses once per line. The
pane exists to open a 2,896,050-byte file of 62,184 lines, and section "Bytes
To Text" in `GopEdit` records why it never builds a whole-file Text at all.
Each of those four is disqualifying alone. The join reads BYTES: a target
becomes a byte pattern once, the buffer is scanned for the matching signature
line, and a row paints one bit out of a bitmap, which is why `ged-draw-row`
takes no annotation argument.

**The files on the medium end CRLF, and `ged-chars` renders every byte under
32 as a space.** So a chapter compared as Text reads `Name ` and never equals
`Name`. Compare chapter and name as BYTES, or trim, and expect this the next
time anything on the desk compares a line to a stored string. It cost a blank
gutter and a staged probe to find.

**`create-annotation` takes the AUTHOR before the body.** Written the other way
round the store is still valid, the right line is still marked, and every count
still agrees; only rendering the two fields separately shows it, which is what
the bubble was the first thing to do. A test that counts annotations passes
either way.

**Reads and writes go through `GopFacts` over the desk's medium, never
`disk-load`.** The kernel road is the ATA syscall on the active drive, so it is
IDE-only and answers zero facts under UEFI or on a stick (WORKS-46). The
annotation surface is proven on both roads: seeded through the kernel, read
back through the desk.

**Trust is not a desk decision and needed no ruling.** The medium already
persists vouches (kind 32); `RepoVouch`'s voucher, subject and level are
exactly `lattice-add-vouch`'s three arguments; `trust-default-threshold` is
already a named constant. The loaded identity is added DIRECT at `trust-max`,
because an author who cannot vouch for themselves would need someone else's
vouch before their own note appeared on their own machine. **The consequence
worth knowing: with no identity loaded the lattice is empty and NOTHING is
above threshold, so no annotation shows.** That is the specified behaviour and
it means the default `desk.ps1` bed, which loads no identity, shows none.

**To exercise any of this under UEFI**, bake an identity rather than walking
the first-boot wizard: injected Enter and navigation reach the wizard but
printable characters never do, so the passphrase answers "Too short" forever.
`build/build-img.ps1 -Identity build/boot/BEDIDENT.DAT` skips the ceremony.
Boot with `-kernel <img> -uefi -disk <THE SAME img>`, both and not either, or
the guest paints "No disk answered on any controller"; drive it with
`-keys-file` (`-keys` is unpaced) plus `-hid-nak-unchanged`; and allow a long
runway, about 66 s of boot diagnostics answered by Enter, before anything the
desk sees will land.

## 1. The desk never unwinds

A pane does not return to the desk. It tail-calls `desk-loop` again, and
`desk-loop` tail-calls `desk-dispatch`, which tail-calls the next pane. The
frame that opened the browser is still there when you open the calculator from
inside it.

Two consequences, and both have cost something:

- **Nothing is reclaimed by returning.** There is no return. Memory comes back
  only where the code explicitly restores it (section 3).
- **The `root` widget tree each pane exit rebuilds is a NEW allocation.** The
  one it replaces is still referenced by a frame that will never resume, so
  without a restore each visit permanently costs one root.
- **A pane visit also stacks a FRAME, and nothing reclaims that.** The idle
  path is self-recursive and safe; the visit path is `desk-loop` ->
  `desk-dispatch` -> pane -> `desk-loop`, which is MUTUAL recursion and
  therefore not a tail call. The prose at `GopDesk.codex:1309-1315` records the
  pathological version measured from the other direction: routing the IDLE
  iteration through the dispatch as well makes every poll stack a frame and the
  desk double-faults (`!EXC=08`, CR2 beside RSP) within seconds of boot. Once
  per visit is survivable and once per poll is not, which is why only a real
  keystroke or click may cross into `desk-dispatch`.

**The heap grows up and the stack grows down through the same arena**, so the
two accumulations converge. The bare-metal boot image gives the whole thing 128
MB (`build/boot/build-option-a.ps1:20`: page tables, memmap, heap and stack).
The heap half is fixed by section 3. **The stack half is not fixed, has no
owner and no instrument** -- `stack-min-rsp-addr` only moves in a
`trace-alloc` build, so an ordinary desk never updates it.

## 2. The `ds` block, and the rule about pointers in it

`ds` is `alloc-zeroed 128 64` in `desk-run`: thirty-two 32-bit cells at offsets
0..124. It is how a pane gets state without every pane signature growing a
parameter. It was 64 bytes until 2026-08-21, when the click sound took the
first two cells of the second half.

| offset | constant | holds |
|---:|---|---|
| 0 | `desk-review-cell` | pointer: the Review pane's state block (red, 2026-08-19). Its own cells: 0 selection, 4 mode, 8 pointer to the reason buffer, 12 reason length, 16 the verdict kind being reasoned about, 20 shift state, 24 the detail's scroll offset in pixels, 28 the view height in rows (written by the paint that measured it, read by the key that pages, because a page key cannot compute room that depends on the theme's padding), 32 the proposal being superseded and 36 the replacement being picked. The reason BUFFER is a second pointer one level down, so `grv-init` is called from `desk-run` before the base mark for the same reason `gcon-init` is (val, 2026-08-20) |
| 4 | `desk-mark-cell` | the base heap mark (section 3) |
| 8 | `desk-second-cell` | last RTC second the taskbar clock painted |
| 12 | `desk-focus-cell` | which app the desk is stepping (0 none, 1 Monitor, 2 Calendar, 3 Appearance, 4 Calculator, 5 Clock, 6 Programs, 7 Diffusion, 8 Issues, 9 Console, 10 Files, 11 Browser, 12 3D View, 13 Aquarium, 14 Editor, 15 system menu, 16 Review) |
| 16 | (bare literal) | the Monitor pane's repaint second |
| 20 | `desk-marks-cell` | pointer: the app mark stack (val, 2026-08-19) |
| 24 | (bare literal) | pointer: the calculator's state block |
| 28 | `desk-edit-cell` | pointer: the editor's state block, **192 bytes since 2026-08-25 and no longer 128** (val). `GopEdit` kept four things in exactly the desk's window slot: the filename buffer at bytes 44 to 55, the marks pointer at 56, the chapters pointer at 60 and the draft length at 64. They are `ged-name-off` 108, `ged-marks-cell` 120, `ged-chaps-cell` 124 and `ged-draft-len-cell` 128 now, with the draft buffer at 132, and the block grew to hold them. **A test that builds one builds a 192-byte one**, for the reason the `ds` rule below gives. `codex/test/apps/edit-block` is the arm and its census of bytes 44 to 67 is the assertion; the two round trips beside it are the control, because a buffer works perfectly at either offset |
| 32 | (bare literal) | the tracker's filter |
| 36 | `desk-console-cell` | pointer: the console's state block |
| 40 | (bare literal) | the tracker's selected row |
| 44 | `desk-prog-cell` | pointer: the launcher's state block |
| 48 | `desk-gpu-cell` | is a GPU present |
| 52 | `dk-scheme-cell` | colour scheme index |
| 56 | `dk-adorn-cell` | adornment bits |
| 60 | `desk-clock-cell` | pointer: the clock pane's state block |
| 64 | `dk-sound-cell` | pointer: the click sound's block (val, 2026-08-21). Its own cells: 0..15 the HDA buffer descriptor list, 16 the rendered PCM buffer, 20 its byte length. Both are read by DMA after `hda-play-pcm-at` returns, so `dk-snd-init` is called from `desk-run` before the base mark for the same reason `grv-init` is |
| 68 | `dk-sound-on-cell` | are sounds on (0 off, and off is the default) |
| 72 | `dk-icons-cell` | pointer: the drawn icon set (val, 2026-08-21). Offset 0 is the size every entry was rasterized at and 4..27 are six coverage blobs. `dk-icons-init` runs from `desk-run` before the base mark, for the reason every other pointer here does: a paint's allocations are freed by `desk-loop`'s bracket and a pane exit restores past them |
| 76 | `dk-task-band-cell` | the taskbar band in DEVICE pixels, measured once by `dk-task-init` from `desk-run` (val, 2026-08-25). Not a pointer, so it needs no allocation before the base mark; it is written after the face loads because the band follows the face. Zero means unwritten and `dk-task-px` falls back to `dk-task-h * ui-wscale w` |
| 80 | `dk-wr-cell` | pointer: the window registry (val, 2026-08-25). Offset 0 is the count and entry `i` is a focus id at `4 * i + 4`, up to `dk-wr-max` of them. THE ARRAY ORDER IS THE Z ORDER: entry 0 is the bottom, the last entry is the top, and the top is the focused window. Allocated in `desk-run` before the base mark, for the reason every other pointer here is. **`dk-wr-max` is 15, which is the 64-byte block's own bound** (four bytes for the count, four an entry); it was 4, and 4 was the vertical cascade room divided by the step rather than anything about the block |
| 84 | `dk-cal-cell` | pointer: the Calendar's block (val, 2026-08-25). It uses only the window slot at 44..60; the pane keeps no other state, which is what made it quiet, and a window is the first thing it has ever had to remember |
| 88 | `dk-dif-cell` | pointer: the Diffusion pane's block (val, 2026-08-25), same shape and for the same reason |
| 92 | `dk-sty-cell` | pointer: the Appearance pane's block (val, 2026-08-25). Same shape as the two above: the pane's own state is `dk-scheme-cell` and `dk-adorn-cell`, both integers in `ds`, so the window slot is the only thing this block holds |
| 96 | `dk-trk-cell` | pointer: the Issues pane's block (val, 2026-08-25). Same shape again, and the pane it belongs to keeps state without keeping a block: the filter is bare cell 32 and the selected row bare cell 40, two integers, so there was nothing a window slot could sit in |
| 100 | `dk-mon-cell` | pointer: the Monitor's block (val, 2026-08-25). The first block that holds something the PAINTER cannot reach as well as the window slot: cell 0 is whether a USB HID keyboard was enumerated and cell 4 whether a mouse was, written once by `desk-mon-open`. `desk-wnd-paint-all` is handed the screen and `ds` and nothing else, so `kbd` and `mouse` are not in scope where this pane's tree is built, and what it wants of them is settled before `desk-run` takes the base mark and cannot change afterwards |
| 104 | `dk-scene-cell` | pointer: the 3D View's block (val, 2026-08-25). The window slot and nothing else: everything a scene pane keeps lives in `ScenePane`, which `DeskApps` carries, and everything about where it is drawn is derived from the rect these cells hold |
| 108 | `dk-fish-cell` | pointer: the Aquarium's block, same shape. Two cells and not one shared, because two focus ids over one block is two windows over one rectangle |
| 112 | `dk-bro-cell` | pointer: the Browser's block (val, 2026-08-25). The window slot and nothing else, for the same reason the two 3D panes' blocks hold nothing else: a heavy pane's own state is a typed record in `DeskApps`, which no cell can hold |
| 116 | `dk-files-cell` | pointer: the Files pane's block (val, 2026-08-25). The window slot and nothing else; Files' own state is a `FilesState` in `DeskApps`, which no cell can hold. **The Editor needed no new cell**: its window slot is 44 through 60 of the block `desk-edit-cell` has always pointed at, which grew to 192 bytes so those offsets could be the desk's |
| 120 | `dk-drag-cell` | which window is being dragged by its titlebar, `desk-focus-none` when none (val, 2026-08-26). Holding the id IS the in-progress flag; there is no separate one |
| 124 | `dk-drag-off-cell` | the grab offset, `dx * 65536 + dy` (val, 2026-08-26). Packed because this is the last cell the block has: `dx` is bounded by the window's width and `dy` by the titlebar's height, and both are non-negative because the press was inside the bar |

| 128 | `dk-rate-n-cell` | `desk-loop` iterations counted so far in the open window (val, 2026-08-26) |
| 132 | `dk-rate-t0-cell` | the HPET tick the window opened at, masked to 32 bits because `peek-32` zero-extends and the counter passes 2^32 in about five minutes |
| 136 | `dk-rate-cell` | the last completed rate in iterations per second, which is what the topbar paints |
| 140 | `dk-rate-armed-cell` | whether the counter has taken its first sample. It exists so the first iteration can force one paint: a readout that appears only on success cannot report a stopped HPET (L-STATES) |

| 144 | `dk-size-cell` | which resize zone is in progress, `dk-size-none` when none (val, 2026-08-26). Holding the zone IS the in-progress flag, the way `dk-drag-cell` holds the id |
| 148 | `dk-size-fid-cell` | which window is being resized |
| 152 | `dk-size-x0-cell` | the window's x at GRAB time. Signed through `dk-cell-signed`, because a window may sit at a negative x and `peek-32` zero-extends |
| 156 | `dk-size-y0-cell` | its y at grab time |
| 160 | `dk-size-w0-cell` | its width at grab time |
| 164 | `dk-size-h0-cell` | its height at grab time. **The rect at grab time and not the current one**: the step rewrites the block every sample, so computing from the live rect would compound each sample onto the last and the window would run away from the pointer |

| 168 | `dk-task-edge-cell` | which edge the task band is docked to, one of `dk-edge-bottom`/`top`/`left`/`right`. Written from settings by `dk-settings-into` |
| 172..200 | the flick's eight cells | `dk-flick-p1`, `-t1`, `-p0`, `-t0` are the two-sample velocity baseline; `-cell` is the armed window's focus id, `-dir` the edge the release vector pointed at, `-org` the release point and `-rt` the release time |
| 204, 208 | `dk-pillc-cell`, `dk-pillc-t-cell` | the double-click latch on a pill: which pill was last clicked and when |
| 212 | `dk-pedge-cell` | pointer: a block of `dk-pedge-slots` (17) entries, one per focus id, holding the edge that app's pill is docked to (val, 2026-08-27, ShellRefinement 6.7.1). **Stored as the edge PLUS ONE so that zero means unwritten**, because `alloc-zeroed` hands back zeros and `dk-edge-bottom` is 0; without the offset a never-flicked app and one flicked to the bottom are the same bits. `dk-pedge-get` falls back to `dk-task-edge ds` for an unwritten or out-of-range id. Allocated by `dk-pedge-init` from `desk-run` before the base mark, for the reason every other pointer cell here is |

**THE BLOCK GREW TO 256 BYTES ON 2026-08-26 (val). Cells 0 through 212 are now
taken, so 216 through 252 are free -- nine cells.** This paragraph said "168
through 252 are free" until 2026-08-27, which was true the day it was written
and stopped being true within hours: `dk-task-edge-cell`, the flick's eight and
the pill latch's two took 168 through 208 that same week and none of them added
a row here (L-COUNT). The rows above are the re-measurement, taken from the
definitions rather than from this list. It grew because the
scheduler needs a period per pane for fourteen panes and the desk-loop rate
counter needs three more, and the block had been full since `dk-drag-off-cell`
took cell 124 that morning. Announce first, the way this section already asks.

**GROWING IT AGAIN COSTS 29 SITES, NOT FIVE.** This paragraph said "the five
test chapters that build a `ds`" and that was wrong when it was written:
re-measured 2026-08-26 the growth touched `desk-run` plus **28 sites across 18
test files** (L-COUNT). Grep for `alloc-zeroed <size> 64` and read the BINDING
before editing any of them: `xhci-speed-psi` has eleven sites and
`disk-enum-parse` two that allocate the same size and are NOT a `ds`, and
`desk-run` itself has a second one for `desk-marks-cell`. A blind
search-and-replace takes all four of those with it, which is how this edit
nearly shipped a wrongly grown marks block.

**A WINDOW MAY HANG OFF THE GLASS, AND WHAT KEEPS THE TASKBAR IS THE PAINT
ORDER** (val, 2026-08-26, superseding the rule that stood here earlier the same
day). `dk-drag-keep` pixels of a dragged window stay on screen on both axes, so
there is always something to grab and no window can be lost; the TOP is the one
edge that stays closed, because above the topbar the titlebar is unreachable and
no gesture the desk has can bring it back. The bottom stops at the content box
rather than the glass so `shadow-subtle` has somewhere to land.

This paragraph previously said a window may NOT leave the content box, because
`desk-wnd-paint-all` painted the chrome FIRST and walked the windows after it,
so a window over the band was drawn on top of it: dragged to 101,486 at
1600x900 the Calculator buried the Cobblestone pill. **The clamp was the wrong
place to fix that.** `desk-wnd-band` re-renders the taskbar subtree AFTER the
windows, so the band is always on top and the clamp is free to let a window
leave the box. **No arm could see either version** -- the clamp did exactly what
it was written to do and the arithmetic test agreed with both, which is why this
rule is stated here rather than left to a test.

**THE START MENU IS AN OVERLAY AND IS PAINTED LAST, AFTER THE WINDOWS** (val,
2026-08-27). `desk-menu-draw` painted the menu through `dk-chrome-paint` and
never walked the window registry, so opening the menu erased every open window
from the glass. The windows were still open, still in the registry and still
named by their pills, and nothing on the screen said so. Measured against a
depot-built control on one mouse timeline (open the Calculator, then open the
menu): the control paints the desktop, the menu, and the Calculator's PILL with
the window itself absent; the fix paints the window and the menu over it. **The
pill is what makes that pair readable, because it is the row the defect cannot
move** -- without it the two frames differ only by a window, which a reader
could take for a window that never opened.

It is `desk-wnd-paint-all` and then a re-render of the laid `sysmenu-body`
subtree, which is the shape `desk-wnd-band` already uses to keep the taskbar on
top, and it inherits that shape's caveat: a bare subtree re-render draws the
labels and not the gutter icons, so `desk-gpr-icons` and the `shutdown` icon go
back on after it.

**FRONTMOST HERE MEANS PAINTED LAST, NOT A REGISTRY ENTRY.** The rule below in
this section, that the system menu shares the launcher's block and must not be
given a `desk-wnd-blk` arm, is unchanged and is what keeps it out of `dk-wr`.

**AND ONE LAYOUT SERVES BOTH THE PAINT AND THE HIT.** `dk-menu-laid` is that
one call; `desk-menu-draw` and `dk-menu-hit` both take it. The two were already
the same expression written out twice, which is the drift this file names for
the Issues table, one menu over.

**THE BAND DOCKS TO AN EDGE, AND THE CONTENT BOX IS THE ONLY THING THAT HAD TO
LEARN IT** (val, 2026-08-27). Damian: the task bar is now more of a task frame,
the whole edge of the OS should be dockable, bottom being the default. The edge
is `dk-task-edge-cell` (168) and a persisted setting; `desk-chrome-face` places
the band per edge and `desk-taskbar` builds a `DirRow` for a horizontal one and
a `DirColumn` for a vertical one. Everything else follows from `dk-cbox-x/y/w/h`,
which all four take `ds` now: every window rectangle, drag clamp, maximise and
the 3D viewport already read the box through them, so nothing else needed to
know where the band is. That arity change is 40 call sites in `GopDesk` and 43
across seven test chapters, and it is the whole cost.

**THE DEFAULT IS PROVEN INERT, NOT ASSUMED INERT.** An unwritten cell reads
zero, zero is `dk-edge-bottom`, so a desk with no `SETTINGS.DAT` is what it
always was. Measured: the bottom-edge frame differs from a depot-built
pre-change frame in 24 rows, twelve topbar and twelve taskbar, which are the
`it/s` readout and the clock. **Zero content rows differ.** `desk-pane-origin`'s
first four rows are byte-identical across the change for the same reason and are
the calibration; its four new rows set each edge and assert the laid `content`
slot still sits inside the box, which is the row that fails if the two roads to
"where the band is" ever disagree.

**`dk-task-init` MUST MEASURE THE TREE THE CHROME PAINTS, WHICH MEANS THE
FITTED ONE.** It measured the unfitted tree, and that was invisible while the
band was horizontal: `comp-fit-node` only ever rewrites `wn-min-w`, so a row
band's `wn-min-h` is identical fitted or not. A column band takes its width from
that same `min-w`, so the constructor's eight-pixels-a-character guess became the
band's size. With `desk-task-clock-w`'s 220 in the column that measured **464
device pixels at 1600**; fitting the measured tree and giving the clock a zero
width minimum on the vertical axis takes it to 280 faceless and 256 with the
CMUNSS face loaded. The clock's 220 is a HORIZONTAL reservation for
right-aligned text and means nothing in a column.

**A CHROME LAYOUT CHANGE MUST REBUILD `root`, AND ANSWERING A SCANCODE IS THE
PATH THAT ALREADY DOES IT.** `root` is threaded through `desk-loop` and rebuilt
only by `desk-app-hide` and `desk-app-close`; `desk-taskbar-clock` re-renders
the time into it once a second. Moving the band with a stale `root` paints the
clock where the band used to be, which is the same failure section 0.5 records
for a pill. The Appearance edge row answers `dk-sc-style`, so
`desk-app-close-to` rebuilds `root` and re-dispatches, and the pane reopens
where the user left it. Every other row on that pane changes colours only and
may answer 1.

**AND AN ALREADY-PLACED WINDOW DOES NOT KNOW THE BOX MOVED.** A rect is a stored
fact, so docking the band left leaves each window where it was, UNDER the band
rather than clipped by it -- which reads as a window that lost a strip.
`dk-wnd-refit` pulls each one back inside on an edge change, skipping maximised
and never-placed windows because both already derive from the box. A drag may
leave the box on purpose and still does; an edge change is not the user moving a
window.

**THE WELCOME FRAME IS WALLPAPER WEARING WINDOW CHROME, AND THAT IS THE ONE
THING TO KNOW BEFORE TOUCHING IT** (val, 2026-08-27, from the code review
Damian asked for). `desk-window` paints a body, a titlebar strip and a
"Welcome" caption straight to the framebuffer from `desk-draw`. It is not in
`dk-wr`, it has no `desk-wnd-blk` arm, and **nothing in the tree hit-tests its
rectangle**: the only readers of `dk-win-x`/`dk-win-y`/`dk-win-h` are two
paint sites and, since 2026-08-27, the two arms that measure them
(`codex/test/apps/desk-welcome-box` and `desk-welcome-scale`). So it cannot be
focused, raised, moved, minimised or closed, and it is not a window that is
missing its buttons -- it is the desktop's backdrop, drawn to look like one.

**All four of its placement functions take `ds` and read the content box**
(val, 2026-08-27, WORKS-52). They used to reserve the chrome themselves in the
frame's own scale, so a band docked to the top put the frame inside the band's
strip, and even at the bottom the frame sat 28 device pixels high at 1280x800
because `dk-task-h * s` is not `dk-task-px`. If you add a placement function
here, route it through `dk-cbox-*` like everything else on this desk; that is
the rule the sidebar constant (WORKS-35) and this frame have now each paid for
once.

**It FITS at every supported geometry on both font paths, and the tightest is
1024x768 faceless at 44 device pixels of slack** (val, 2026-08-27, measured by
`codex/test/apps/desk-welcome-scale`). Worth knowing before adding a line to
`dk-para-intro` or `dk-para-hint`: the body is fifteen wrapped lines and each
one costs `dk-line-step` device pixels, so at scale 2 one more line is 36 to 52
pixels and eats that margin whole. **When you check that, load the face at
`dk-ui-ppem w` for the width you are checking.** It answers 16 at 1600 and
above and 14 below, and a sweep holding one face across widths measures a
configuration the desk never builds -- that mistake produced a published
finding of a 34-pixel overflow at 640x480 that does not exist, retracted the
same day. At the ppem the desk actually loads there, 640x480 needs 404 against
a 430 box.

**The hint text on it is NOT a promise it breaks, and an earlier note of mine
said it was.** `dk-para-hint` reads "An app opens in a window you can move by
its title bar, and close with the x", which is a true sentence about APP
windows and says nothing about the frame it is printed on. What is real is
the juxtaposition: a paragraph describing titlebars that drag and x's that
close, printed inside a titlebar that does neither. That is worth knowing and
it is not the same claim, so it is written here as the weaker one it is.

The frame's geometry has three open findings from that review: **WORKS-52**
(the vertical placement never learned the band's edge), **WORKS-53** (whether
the scale reduction is reachable at all, which gates 54's repair) and
**WORKS-54** (`desk-window`'s dead `s0`). Do not fix 52 before answering 53.

**A SECOND CLICK ON THE SAME PILL WITHIN 400 ms PUTS THE WINDOW BACK AWAY**
(val, 2026-08-27), which is Damian's toggle. The first click of the pair has
already run and raised the window, so the second is what makes the gesture a
toggle rather than a second raise. Putting it away is
`poke dk-wnd-st-cell dk-wnd-st-min` plus `desk-app-hide`, which is the path
the minimise button already takes and which redraws the band, hands focus down
to whatever is still visible, and brackets its own cursor.

**It is keyed on the PILL and not on the pointer.** Two clicks in time on
different pills are two raises. The band re-lays on every raise, so a
position-keyed test would answer yes for two different windows whose pills
happened to fall under the same point. **The record is cleared when the pair
fires**, so a third click is a fresh first click rather than another double
calling the hide path on a window that is already away. Cells 204 and 208.

**Double-clicking an ALREADY minimised pill restores it and puts it straight
back**, because the first click of the pair is a real restore. That is the
toggle behaving as specified rather than a defect, and it is what "a second
click puts it away" means when the first click already acts.

**A FLICK IS A DRAG THAT KEEPS GOING AFTER THE BUTTON COMES UP** (val,
2026-08-27). Damian: a drag for moving purposes usually stops after the mouse
up but a flick follows through in the direction of the edge to which the
window pill should be attached. Both halves are load-bearing. Release speed
alone throws away a window somebody was placing briskly; follow-through alone
fires on the commonest thing a hand does after dropping a window, which is
reach for something else. The gesture ends as `desk-wnd-ev-min`, so it is the
dock the desk already had and all twelve windowed panes got it at once.

**THE VELOCITY BASELINE IS TWO SAMPLES DEEP AND ONE WILL NOT DO, and the
failure a one-deep baseline gives is the opposite of the obvious one.** The
desk goes round about 20,000 times a second idle against a mouse's 125, so a
baseline rolled every iteration is the release position itself; rolled on a
timer it is degenerate whenever the release lands just after a roll. Keeping
the previous window as well puts the measured interval between one and two
roll periods always. Sabotaged to one deep, the expectation was that a
stopped hand would read fast; **measured, every release reads an interval of
0 ms, `dk-flick-fast` refuses that outright, and the whole gesture goes dead
while the stopped-hand control stays green.** A suite carrying only the
negative arm would have scored a deleted gesture as a pass.

**A BOARD WITH NO HPET FLICKS NOTHING**, and so does a bed with a pinned
clock. Every arm of the gesture is a speed or a deadline, so `build/desk.ps1
-Rtc` cannot flick and a frozen-clock capture is not the instrument for it.

**A PILL GOES IN THE TASKBAR TREE, NEVER PAINTED INTO THE BAND** (val,
2026-08-25). `desk-taskbar-clock` re-renders the whole taskbar subtree once a
second to repaint the clock, so anything drawn into that band from outside the
widget tree is erased within a second of appearing. `desk-taskbar` builds one
button per minimised window from the registry, which also gives the pill its
hit test for free. The same rule binds anything else that ever wants to live in
the band.

**AND ANY PATH THAT REPAINTS MUST BRACKET THE CURSOR** (section 5). The arrow
keeps the pixels it covered so it can put them back, so a repaint underneath a
shown cursor is undone by that restore the moment the pointer next moves. The
pill restore shipped without `cursor-hide` in one draft and left a fragment of
the pill on the band; a capture caught it and no arm could have.

**A `ds` SHORTER THAN 128 BYTES CANNOT LEGALLY HOLD THE CELLS ABOVE 63**, and
five test chapters were allocating 64 (`browser-pane-fit`, `desk-chrome-icons`,
`desk-calc-click`, `desk-taskbar-hit`, `desk-style-render`). That predates cell
76: `dk-icons-cell` is 72 and has been read off a 64-byte block since it was
added. It never showed because the arena is bump-allocated and 64-aligned, so
the over-read landed on padding, answered zero, and every reader's
absent-is-zero branch took over -- which is the same reading a legitimately
empty cell gives, so no arm could tell them apart. All five now allocate 128,
which is what `desk-run` allocates. **A test that builds a `ds` builds a
128-byte one.**

**THE BLOCK IS 256 BYTES AND CELLS 0..200 ARE TAKEN. Re-measured 2026-08-27
against `desk-run` (L-COUNT); this paragraph said 128 bytes and 0..72 for six
days after the block had been grown.** It was 128 for red's Review cell 0
(2026-08-19) and val's 64, 68 and 72 (2026-08-21); the grow to 256 came with
the desk `ds` change at 19874, and 76..168 went to the taskbar band, the
window registry, the panes, the rate counter, the resize gesture and the band
edge. val took **172..200 for the flick on 2026-08-27** (`dk-flick-p1`,
`dk-flick-t1`, `dk-flick-p0`, `dk-flick-t0`, `dk-flick`, `dk-flick-dir`,
`dk-flick-org`, `dk-flick-rt`). The next pane that needs a cell takes one of
204..252 and adds a row here rather than growing the block again. Announce
before you take one, the way the file claims
table in `docs/PM/CurrentPlan.md` asks. Cell 12 was taken by val on 2026-08-18
for the focus id and cell 20 by val on 2026-08-19 for the mark stack; the count
above said three, then two. Two agents took cell 48 independently on
2026-08-11; each change was green alone and the collision only surfaced in the
merge, because nothing in the tree cross-checks this block.

`dk-mon-tick-cell = 28672` is NOT a `ds` offset. It is an absolute address, read
as `peek-32 dk-mon-tick-cell 0`.

### The clock pane's own block, which is not `ds` either

`desk-clock-cell` (60) holds a pointer to a 64-byte block, and that block has
its own split: **0 to 36 are `GopClock`'s and 40 and up are the desk's.**
Nothing in the tree cross-checks it, which is why it is written here as well as
in the chapter.

| offset | name | what |
|---|---|---|
| 0..36 | | `GopClock`'s, do not take one |
| 40 | `dk-clkp-last-cell` | the RTC second this pane last painted, a repaint cache |
| 44 | `dk-wnd-st-cell` | the window state: `dk-wnd-st-normal`, `dk-wnd-st-max` or `dk-wnd-st-min` (val, 2026-08-25). It was `dk-clkp-state-cell` while the clock was the only windowed pane; the Calculator keeps its state at the same offset, so the name is the desk's rather than the clock's. A MINIMISED window stays in the registry and keeps its rect at 48..60; it is simply not drawn and focus skips it, which is why restoring needs no kept rectangle |
| 48 | `dk-wnd-rx-cell` | the normal rect's x, in DEVICE pixels (val, 2026-08-25) |
| 52 | `dk-wnd-ry-cell` | its y |
| 56 | `dk-wnd-rw-cell` | its width, and the unwritten sentinel for all four |
| 60 | `dk-wnd-rh-cell` | its height |

**44 to 60 are the WINDOW SLOT and every windowed pane reserves the same four
offsets plus the state**, which is why they are named `dk-wnd-*` and not
`dk-clkp-*`: the clock was the first pane to have a window and is no longer the
only one, and a second pane that put its rect somewhere else would need a
second set of readers. The block is 64 bytes and cell 60 ends at byte 63, so
**a windowed pane's block is full** and the next thing a window needs (a dock
edge, a restore rect distinct from the normal one) grows the allocation in
`desk-run` rather than taking an offset here.

**THE CALCULATOR IS THE SECOND WINDOWED PANE AND THE CONSOLE THE THIRD** (val,
2026-08-25). The Calculator's block is the one `ds` cell 24 points at, `GopCalc`
uses offsets 0, 8 and 16 of it; the Console's is the one cell 36 points at,
`GopConsole` owns 0 through 12 and the desk 16. For both, the window slot at 44
through 60 is free space the block was already carrying. A pane
becomes windowed by taking a line in each of `desk-wnd-blk`, `desk-wnd-title`
and `desk-wnd-tree`, and by having 44 through 60 free in its own block. Nothing
in the tree cross-checks the second half of that sentence.

**THE LAUNCHER IS THE FOURTH, AND IT ADDED TWO THINGS THE FIRST THREE DID NOT
NEED** (val, 2026-08-25).

- **`desk-wnd-over` is a FOURTH per-pane fact**, called by `desk-wnd-one` after
  the window is drawn and handed the laid tree. A pane that paints over its own
  laid tree cannot do it from its `-draw` any more, because the painter renders
  every open window and hands the laid trees to nobody. The launcher's icons
  ride the laid tree rather than the widget tree, for the reason its own section
  gives, so they have to be drawn where THIS window put them.
- **`dk-wnd-wants-box` gives a pane the whole content box as its first
  placement.** The launcher's tree is two columns sized for the region the
  chrome used to hand it; at three quarters of that box the tail of each column
  falls past the bottom edge and `comp-render-at` clips it. Measured at 1600x900
  the first time it was drawn as a window: fourteen entries in the tree, TWELVE
  on the screen, the Review pane and the Browser gone, and nothing saying so.
  **No hit test can find this**, because `widget-layout` places a child past its
  container rather than clipping it (section 5.1), so the arm reports all
  fourteen reachable under either rect. The layout and the paint disagree and
  only the paint is what a person can click.

**A TICK REPAINTS ONE WINDOW AND WHATEVER IS OVER IT, NEVER THE DESK** (val,
2026-08-25). A pane whose content changes on a clock had been calling
`desk-wnd-paint-all`, which is the chrome and every open window, on the
reasoning that repainting one pane would erase the window beside it. That is
true of a pane that paints the screen and false of one that paints inside its
own rectangle. What it bought was a full-screen rebuild at 1 Hz, and with two
windows open a person watches the whole desk rebuild itself once a second.
`desk-wnd-paint-from` starts the walk at that window's registry index, which
is the z order, so anything over it is redrawn and nothing else is.

**OPENING AND TICKING MUST NOT SHARE A PAINT FUNCTION.** They did, and
narrowing the tick silently narrowed the open with it: the wall went down,
the window went on top of it, and the chrome was never drawn at all. It
looked right for one second, because the next tick drew the chrome under the
old arrangement. An open paints everything; a tick paints one window.

**AND NOTHING IN THE BAND EXCEPT THE TIME IS REPAINTED ON A TICK.** The
taskbar clock used to `comp-walk` the whole subtree, redraw the menu icon the
walk had erased, and only then draw the time -- so once a second the clock was
ABSENT for the length of the walk, which on a framebuffer with no back buffer
is what a person sees as the clock flashing. The erase is now `comp-box` over
the taskbar's own box CLIPPED to the clock's cell, which is not the same as
filling the cell: a panel's background may be a gradient computed across its
whole box, so a flat sub-fill would seam.

**THE DESK'S OWN HAND-DRAWN TEXT TAKES THE FACE.** The brand in the top bar,
the Welcome caption and the taskbar clock were all `gop-draw-text` straight to
the bitmap grid while every widget beside them was proportional. They go
through `desk-line` now, and the clock's right edge is measured with
`gfont-text-w` rather than assumed to be eight pixels a character.

**EVERY PANE IS A WINDOW** (val, 2026-08-25). All fourteen. The last two were
Files and the Editor, and they took the rect the sweep below had already made
a parameter.

**A CONVERTED PANE MUST NOT KEEP ITS OWN TITLE BAR.** Both of these drew one
through `gfl-draw-frame`, with a caption and a close box, and under the
desk's titlebar that is two bars saying the same word and two `x`es at
different heights meaning different things. The bar stays and is a LOCATION
now: the window title says which application, the bar says which directory or
which file, which is the one thing the title cannot know. `gfl-close-hit` is
deleted rather than left unreachable, and **the capability that went with it
is named here rather than left to be noticed** -- that `x` returned from a
preview to the listing and dismissed the editor's big-file notice, its naming
prompt and its annotation bubble. Esc does all four, Enter does the first,
and the window's own close button closes the pane.

**AND THE PANEL GEOMETRY TAKES A RECT NOW, WHICH IS THE OTHER HALF OF THE
SAME SENTENCE** (val, 2026-08-25). `GopFiles` used to answer where the Files
panel was in four functions of the SCREEN -- `gfl-px`, `gfl-py`, `gfl-pw`,
`gfl-ph` -- and `GopEdit` called them, so three chapters agreed about the
panel by each doing the same arithmetic. They are deleted. Every drawing and
hit function in both chapters takes a `LayoutRect` instead, and the desk says
which one: `dk-pane-box` for a pane that owns the desktop, `dk-wnd-content`
for one that is a window. **A pane becomes a window by being handed the other
rect**, which is the whole of what is left for those two.

Two things carry the screen as well as the box and it is not an oversight:
`cursor-update` clamps to the glass, so the functions that put the pointer
back after a repaint take `w` and `h` too. **The evidence that the sweep
moved where a panel can be and not what its arithmetic answers is that
`codex/test/files-parse` and `codex/test/apps/desk-pane-origin` are both
UNCHANGED across it**, with the rect spelled out in the first as exactly what
the four deleted functions computed at 1280x800.

**THE CONTENT BOX IS `dk-wnd-content` AND NOTHING COMPUTES IT AGAIN** (val,
2026-08-25). Read the block, read the window state, take the titlebar off the
top: six lines that had been written out four times before the Browser wanted
a fifth. It answers DEVICE pixels, because that is what a pane's own layout
call and `comp-render-at` both want. Four copies of one arithmetic is how the
sidebar constant reached four different values in WORKS-35.

**A HEAVY PANE NEEDS ONE THING A LIGHT ONE DOES NOT: IT MUST REPAINT ITSELF
AFTER A CHROME EVENT** (val, 2026-08-25, the Browser). `desk-wnd-chrome-step`
answers `stay` after a raise, a Tab cycle or a maximise, and the repaint it
did on the way draws that window's EMPTY body panel. A 3D pane gets its
picture back on the next frame; a pane that only redraws on an event has no
next frame until somebody presses a key, so it would sit blank looking exactly
like a pane that had crashed. The Browser paints itself on `stay`, at whatever
rect the event left it.

**A PANE THAT RENDERS A FRAMEBUFFER IS NOT GIVEN A TREE AT ALL. IT IS GIVEN
A RECT, AND IT PAINTS OVER ITS OWN WINDOW** (val, 2026-08-25, the two 3D
panes and the Browser). `desk-wnd-tree` answers an empty panel for both, so `dk-wnd-frame`
draws the border, the titlebar and the three buttons and leaves the content
box empty; the pane's own loop then writes inside that box at the frame rate.
The chrome is painted ONCE, at open and on re-entry, and not per frame,
because nothing in it moves and a frame's budget should not go on pixels that
did not change.

**The rect is read again before every frame rather than cached**, which is
what makes a window that was moved or maximised between two frames simply
correct on the next one. `gsc-place` writes four fields in place: the GPU
view, the content height, the framebuffer row pointer, and a render target
rebuilt from buffers that do not move. **The buffers are allocated once at
the CONTENT BOX size**, the largest rectangle a window here can take, so a
resize costs two small records rather than a megabyte-scale target; a target
reallocated per resize would strand the old one above the pane's own heap
mark until it closed, which is 6.4's LIFO tail reached by pressing a button.
`codex/test/apps/scene-place` measures the two placements at 1,392 bytes
against 256,000 for one buffer at its test size.

**A PANE THAT DRAWS TEXT RATHER THAN BUILDING A TREE IS CONVERTED BY GIVING
IT A TREE, AND THE COST IS WHAT ITS DRAW COULD REACH THAT A TREE CANNOT**
(val, 2026-08-25, the Monitor). Absolute `gop-draw-text` at an x and a y can read
anything its caller had; a tree is built by the painter, which is handed the
screen and `ds`. Three kinds of thing came out of that, and the third is the
rule: what the tree can compute for itself stays in the arm (the RTC, the
ACPI walk, the heap frontier); what is a FACT ABOUT THE MACHINE that cannot
shrink into 32 bits is added to `desk-wnd-tree`'s arguments (`base`, the
framebuffer address, is 64 bits and there is no `poke-qword`); and what is
settled before the base mark and cannot change afterwards goes in the block
(whether a keyboard and a mouse were enumerated). **The block case is the one
that fails quietly: an unwritten cell reads zero, and zero here is `mailbox`
and `no`, which is precisely what a machine with no USB HID reports.**
`codex/test/apps/desk-mon-block` asks the two blocks to DISAGREE for that
reason; either reading alone is unfalsifiable.

**AND THE FIFTH FACT IS THAT A PANE'S TREE MAY BE A FUNCTION OF THE ROOM IT
WAS GIVEN, WHICH IS WHY `desk-wnd-tree` TAKES THE SCREEN** (val, 2026-08-25).
The Issues table is built at the height the layout engine measured for it
(WORKS-23), and inside a window that room is the window's content box rather
than the pane's, so the arm needs `w`, `h` and `tf` to find it.
`desk-wnd-one` is the only caller and had all three, which is what made the
widening three lines. The trap underneath is that the measuring pass and the
paint must agree: a table measured against the whole chrome pages for a
height nothing draws at, and the reader gets a page size that matches no
rectangle on the screen.

**THE BIGGER SCREEN IS THE TIGHTER ONE, BY NEARLY HALF, AND EVERY CLIPPED LINE
SO FAR WAS CLIPPED AT 1600** (val, 2026-08-25). A tree is laid out in LOGICAL
pixels and `ui-wscale` is 1 below 1600 and 2 at or above it, so in the units a
tree is measured in the whole content box goes from 1120 by 728 at 1280x800 to
640 by 400 at 1600x900, and three quarters of it goes from 840 by 546 to 480 by
300. Every instinct says to check the small size. The Calendar lost
`Esc returns.` at 1600 and showed it at 1280; the Diffusion pane lost its whole
status row the same way. **Look at a converted pane at BOTH sizes and expect
the large one to be where it breaks.**

**AND A FULL-BOX WINDOW IS STILL ONE TITLEBAR SHORT OF THE PANE IT REPLACES.**
The content box is what a chrome-painted pane was given whole; a window inside
it must spend `dk-wnd-bar-h` on its own titlebar, so a tree that exactly filled
the region overflows by about a line the moment it is windowed. Measured
against a depot-built control, that is what cost the Diffusion pane its last
line even with `dk-wnd-wants-box`. The repair was to delete the line rather
than to find room for it: `Esc returns.` is a hint about the only way out, and
a windowed pane has a close button, a menu and Esc.

**THE CASCADE WRAPS, AND EACH AXIS WRAPS IN ITS OWN ROOM** (val, 2026-08-25).
The default rect is three quarters of the content box centred, so the free
margin on each side is an eighth of the box, and an offset past it hangs the
window off the screen. Measured at 1280x800 the box is 1120 by 728, the margins
are 140 and 91 against a step of 24, and **the fifth window's offset of 96
already left the box vertically -- which is why `dk-wr-max` was 4.** Wrapping
both axes together would be worse than not wrapping: the window after the wrap
lands exactly under the first, which is the collision the cascade exists to
prevent and which nothing but a photograph has ever caught. Separate spans, 6
and 4 at 1280x800, repeat only after twelve. `desk-window-registry`'s
`cascade at` rows ask for the worst offset against both margins AND for the
number of distinct positions, because a single span passes the first and fails
the second.

**THE SYSTEM MENU SHARES THE LAUNCHER'S BLOCK AND MUST NOT BE GIVEN A
`desk-wnd-blk` ARM.** Sharing `desk-prog-cell` is deliberate and is what makes a
row chosen in one presentation the row chosen in the other. A window slot is not
shared state of that kind: two focus ids over one block is two windows over one
rectangle, which no registry row can express.

**A MISSING `desk-wnd-blk` ARM ANSWERS ZERO, AND ZERO IS A WRITABLE ADDRESS**
(the same trap section 2's rule already names for an unwritten `ds` cell). The
window then places itself over physical page zero, reads its rect back from
whatever else lives there, and lands on the never-placed default -- so it draws,
it looks plausible, and it sits exactly where the first window sits. No count,
top or array order can see it. `codex/test/apps/desk-window-registry`'s
`three open` row asks for the block by identity and the positions pairwise for
this reason; sabotaged, three of its fields move and every registry field
stays put.

**Width is the sentinel and it is the only one of the four that can be.** A
zero x or y is a legal position on this screen; a zero width is not a window.
So an untouched block reads as never-placed and `dk-wnd-rx` and its three
siblings answer the arithmetic over the content box instead, which is exactly
what they answered before a rectangle could be stored at all --
`desk-window-frame`'s first ten rows are unchanged across that change and are
the calibration that says so.

The window state is here rather than in `ds` because it is a fact about THIS
pane's window: the next pane to get a window keeps its own, and a `ds` cell
would make one window's state the desk's. An unwritten cell reads zero, which
is `dk-wnd-st-normal`, so a pane that never touches it opens at the
three-quarter rect.

### The rule

**A cell that holds a POINTER must be allocated in `desk-run`, before the base
mark.** Every pane exit restores the heap to that mark, so anything allocated
later is gone, and a cell still pointing at it is a stale pointer the next visit
will use.

This is why `desk-run` allocates `ks`, `ns`, the launcher block, the clock block
and the editor block up front, and why `gcon-init` is called there rather than
lazily on the first console visit.

The two ways it has gone wrong:

- **A cell nobody writes reads as zero, and zero is a writable address.**
  `desk-edit` read cell 28 from the day it was written and nothing ever wrote
  it, so `ged-init` parked an 8 MB buffer pointer and a 1 MB index pointer at
  physical addresses 0, 4, 8 and 12. It worked, because nothing else on this
  platform uses those bytes. Measured 2026-08-15 on the desk under codex-vm:
  `peek-32 0 0` reads 0 before the Edit pane is opened and `0x639100` after.
  Fixed at main 15306.
- **A lazy allocation parked in a persistent cell defeats the restore.** See
  section 3.

## 3. The base heap mark, and how a pane exits

`desk-run` stores the heap frontier in `desk-mark-cell` after the state blocks
are allocated and BEFORE the first `root`. A pane exit restores to it:

```
    let dropped = __heap-restore (peek-32 ds desk-mark-cell)
    in let root = desk-draw base w h stride font (ui-scale w) tf ds
```

Order is load-bearing. Restore FIRST, then rebuild `root`, so `root` is
allocated above the mark and survives; restoring after would free the tree you
are about to hand `desk-loop`.

**A mark taken inside the pane does not work**, and it is the obvious thing to
try. The root is allocated after the pane returns, so a pane-local mark cannot
reach it. Measured on the browser pane, four visits, base `0x6320a8`: a
pane-local bracket left the frontier at `0x64c500`, indistinguishable from no
bracket at all, while the base mark left it at `0x6376e0` and flat in the
number of visits.

### The one pane this does not fit, and why

`ged-init` allocates 8 MB plus 1 MB on first use and parks both pointers in cell
28, which is persistent, so a restore over them frees the memory and leaves the
cell pointing at it.

**Since 2026-08-19 it allocates a third block, the undo ring**, 4096 records of
12 bytes for a fixed 49,152, on the same lifetime as the other two and reclaimed
by the same close. The 9,598,200 figure below was measured BEFORE the ring
existed and has not been re-measured since; the ring's own cost is exact by
construction rather than measured, because it is one fixed allocation that never
grows. A keystroke stores three integers into it and allocates nothing, so the
Edit step still answers 1. This section said the Edit pane had no restore and was
therefore safe. **It was not: the restore was always happening.**

`ged-init` runs from `desk-edit-open`, which is ABOVE the base mark, so both
close paths restore over the buffer, and `ged-ensure` (`GopEdit.codex:49`) only
allocates when the pointer is zero. The next Edit session would therefore write
through an address the bump allocator had already handed to something else.
Measured 2026-08-19: an Edit pane alive and hidden holds **9,598,200 bytes**,
and after it closes the frontier is 72 bytes over a desk that never opened one,
so the 9,437,184 that `ged-init` allocates is demonstrably reclaimed.

**Both close paths now zero the pointer when the restore target is at or below
it**, so the next open re-runs `ged-init`. The arm: open Edit, close it, open it
again and hide it, and the frontier reads `0x0f6d1e0`, which is the same figure
a FIRST session gives (`0x0f6d198`) plus the standing 72-byte offset. Had the
pointer survived, the second session would have skipped `ged-init` and the
reading would have been about 9.4 MB lower, so the two outcomes could not be
confused. The cost is one re-allocation per Edit session, of memory the close
reclaims anyway.

**The 9 MB is therefore no longer one-time**, and the sentence saying it was
rested on `ged-ensure` being idempotent, which is true of the function and was
never true of the lifetime. Multitasking is what made this urgent rather than
lucky: before an app could stay alive, the reused region was merely stale, and
now it can belong to a LIVE pane.

**So the test before adding a restore to a pane: does anything it calls park an
allocation in a cell?** Grep the pane's chapter for `poke-32 <state> <n>` with
an allocation on the right. Integers are always safe; pointers are the question.

### `desk-loop` brackets each iteration

The idle loop allocates: `desk-clock` builds Text and walks the taskbar subtree
on every RTC second edge, and nothing reclaimed it. **Measured before the fix,
the idle desk leaked 3,594 bytes a second** -- 69,896 B of gap after 10 seconds
idle against 213,656 B after 50. On the 128 MB bare-metal arena that is about
ten hours to exhaustion, and it had never been measured because nothing ran the
desk idle and then looked.

`desk-loop` now takes a mark at the top of each iteration and restores it on
every path that continues the loop. The mark sits ABOVE `root`, so `root`
survives; the close path does not restore it, because `desk-mon-close` restores
to the BASE mark instead and rebuilds `root` above that. **After the fix the
frontier is identical at 22 s and 62 s with a pane open and at 50 s idle**, all
three `0x645b80`.

This matters more under the step model than it did before. A loop pane replaced
the leaking idle loop with its own bracketed one, so opening a pane paused the
leak; a step pane leaves `desk-loop` running, so without the bracket the leak
would run the whole time an app is up.

### The stack half has a row now, and it reports its own absence

The Monitor's `stack` row reads `stack-min-rsp-addr` (28736) and
`ram-size-addr` (4072) with `peek-qword`. The boot prologue seeds the minimum
with `ram-size` unconditionally (`X86_64Chapter.codex:385`) and only the
per-function prologue narrows it, under `trace-alloc` (`X86_64.codex:12`), so
the two reading EQUAL is exactly what "nothing ever wrote a lower RSP" looks
like and the row says so rather than printing a depth of zero.

**`compile.ps1 -Trace` DOES enable it**, and a paragraph here said the opposite
until 2026-08-21. `compile-to-cdx-with-exit-mode` takes the emitter's `trace`
as a parameter and `compile.ps1 -Trace` sets the mode word; `compile-to-cdx`
passes `False` for it, which is why an ORDINARY desk reads never-narrowed and
that reading is correct rather than a fault. Ask for a stack figure with
`-Trace` and nothing else.

### The instrument

The Monitor pane's `memory` row prints the frontier and the base mark. The gap
between them is what the current pane stack holds; it should not grow with the
number of visits. That row exists because this whole class was invisible without
it.

**What that row can and cannot settle.** It reads the HEAP, and the heap is
fixed by the base mark, so the gap has been flat across pane visits since long
before the step model: measured 41,800 B after one visit and 31,288 B after ten,
which is smaller. It therefore cannot show whether a per-visit STACK frame went
away, and a design that asked it to was corrected on 2026-08-18. What it does
settle is a leak in time, which is how the 3,594 B/s above was found.

## 4. Adding a pane

The desk owns the loop and the chrome; the pane chapter owns the state and what
a key means (`GopDesk.codex:657-659`). `GopPrograms` is the smallest complete
model of the split.

- **Wire it into `desk-dispatch` and nowhere else.** One `let` in the hit block
  and one `else if` in the chain. A second dispatch table is the thing the
  launcher exists to avoid: a row in the launcher and the key it names must
  arrive by the same road.
- **Only a keystroke or a click may reach the dispatch.** Section 1 says what
  happens if the idle case goes through it too.
- **The desk rows carry `Identity` since 2026-08-19.** Every `[Device.Port,
  Gpu.Compute, Gpu.Memory, Identity]` signature in `GopDesk`, and `DeskVm`'s
  entry, so a pane may sign with the box identity (`key-sign-bytes`); a step
  that does declares `[Device.Port, Identity]`, the Review step is the model.
- **Carry the F12 arm.** Every pane loop has `sc == 88 -> desk-shot`. A pane
  that omits it silently loses the screenshot key, which is also how flights
  are photographed.
- **A pane that wants the POINTER takes `UsbMouse` and reads it in the step.**
  `desk-loop` pumps the mouse before calling the step, so `mouse-x` and
  `mouse-y` are current, and a pane that tracks movement keeps its own last
  position rather than asking the desk for a delta. Both 3D panes and the
  Editor pack it as `x * 4096 + y` in a record field, with ZERO meaning never
  sampled; without that sentinel the first frame reads a delta against an
  origin the pointer was never at.
- **Scancodes are a shared namespace.** Taken today: 4, 16, 18, 19, 20, 23, 25,
  30, 32, 33, 37, 38, 41, 46, 48, 50, plus 1 for Esc, 15 for Tab (leave the pane
  alive, section 0.5) and 88 for F12. 41 is the backtick and opens the system
  menu, which is also the taskbar's `menu` button. Check `desk-dispatch` and
  `dk-style-key` before choosing.
- **A launcher row is not free.** `gpr-split` in `GopPrograms` is an ENTRY
  INDEX that must land on a group boundary, so inserting before it moves the
  cut and the second column loses its heading. Appending at the end is free.
- **There is no sidebar.** It was deleted 2026-08-25 (Damian: *"lets remove
  the whole left pane, put the shutdown option inside the cobblestone menu
  pill on the bottom so it acts more like the start menu than a full screen
  app"*). A new pane costs a launcher row and nothing else, which is what the
  launcher existed for even before the column ran out at thirteen. `Programs`,
  `Files`, `Edit` and `Console` were already rows in `gpr-entries`, so only
  `Shutdown` had to move, and it moved into the start menu.

## 5. Painting under the cursor

The cursor is a saved-pixels sprite, not a hardware overlay. Any repaint under
it must be bracketed:

```
    let hd = cursor-hide cs base stride
    in let p = <paint>
    in dk-cursor base w h stride cs mouse
```

Without the hide, a repaint under a displayed pointer leaves the save buffer
describing pixels that no longer exist, and the next pointer move stamps them
back onto the screen.

`cursor-update` restores what it covered before drawing itself at the new place,
so a pane that repaints nothing needs no bracket.

## 5.1 `comp-render` clips to the box it was given

`gop-put` is `poke-32 base ((y * stride + x) * 4) color` and there is no height
anywhere in the drawing layer. `gop-draw-text` bounds x against `stride`,
because a glyph past the end of a row lands on the NEXT row and shreds it, and
that clip is documented where it lives. **The y axis has no such bound at all**,
so a pane that lays a node out below the last scanline writes past the end of
the framebuffer, silently, on every paint.

Measured 2026-08-20 at 1600x900, framebuffer at `0xBF000000`, last valid row
899. With the Browser pane open, `-hwwatch` fires at row 950, 960, 970, 976 and
977 with `writer=0x1084c0` and `now=0x101010`, the page background; rows 978 and
above are silent, and so is row 950 on a desk with no Browser open. So the pane
writes rows 900 to 977, which is 78 rows and 499,200 bytes past the end. In the
bed that address is plain RAM and whatever follows it is corrupted without a
fault; on metal it is past the GOP aperture the firmware handed us.

The cause is not the Browser's alone and any pane can reach it: `widget-arrange`
places a child at its measured minimum, `flex-col-place` gives a flex child
`box-max mh share` so a content minimum larger than the share wins, and
`comp-walk` paints every node it is handed. Clamping the node would not have
helped -- the deepest node in the Browser's tree is the one that would be
clamped, so its children would overflow instead.

**So the bound is a clip rect carried through the walk.** `comp-render` builds
it from the `w` and `h` it was given, so every existing caller gained the bound
without changing, and a pane rendered into a box smaller than the screen is
bounded to the PANE and not merely to the framebuffer. `comp-fill` is
`gop-fill-rect` intersected with that rect and every rectangle this compositor
draws goes through it.

The clip does NOT narrow as the walk descends. A child painting outside its own
parent is still wrong and the goldens must be able to say so; narrowing per node
would hide the layout defect rather than surface it.

Two things this does not do. **Text is all-or-nothing**: `gop-draw-text` paints
a glyph as sixteen scaled rows with no row bound, so a line straddling the clip
is dropped rather than cut, and a bounded glyph primitive is what that would
need. **The layout is unchanged**: a child is still laid out taller than its
parent and `codex/test/apps/browser-pane-fit` still reports the Browser's tree
reaching past the pane it was granted. What the clip does is stop that damaging
anything; reaching the rest of the page is section 5.2.

**The sweep this section keeps citing is `build/desk-goldens.ps1`**, landed
2026-08-20 with `build/bmpdiff.ps1` beside it. Until then it lived in one
agent's scratchpad, so a criterion this file states as the acceptance test for
a fleet-wide widget change had no runner anybody else could reach. Both carry
their own reasons in their headers, including the two that are easy to get
wrong: the kernel is a parameter so a sweep cannot rebuild its own subject
underneath itself, and `-Disk` decides whether the TrueType path is even awake,
so a type change measured without a font-bearing image reports fourteen
identical panes and reads as "my change did nothing".

Proven before it was relied on: two independent sweeps of the same kernel are
byte-identical on all fourteen panes, which is the property the whole idea
rests on, and the comparator was shown to report MOVED and exit 1 when a pane
genuinely differs rather than only ever agreeing.

What it measured, on a golden sweep of thirteen panes against the same seed:
nine byte-identical, and the four that moved are the ones that were painting
outside their box. Two escapes showed up, not one. The vertical case is the
Browser's page, and the horizontal case is a fill running past `x = w` and
wrapping onto the NEXT row's left edge, which had been breaking the left border
of the Browser and Review panes in bands -- the same failure `gop-draw-text`
bounds x to avoid, happening to rectangles.

One bound that looked obviously right and was not: clipping the taskbar clock's
own repaint to the taskbar band moved 408 pixels on every pane, because the
band's child widgets carry styles whose boxes reach outside the node's bounds.
`desk-taskbar-clock` takes `h` and clips to the screen for that reason.

**A pane's height is MEASURED from the laid-out band, not computed.**
`desk-bro-h` was `h - dk-task-h * ui-wscale w`, which assumes the band is flush
with the bottom of the screen; the root panel's padding leaves 8 logical rows
below it and `desk-taskbar-hit.expected` has said so since it was written. It
reads `widget-find root "taskbar"` now, which is why `root` is threaded through
`desk-step-of` and `desk-browser-step`. Any pane added below the band should
take its height the same way rather than reproducing the arithmetic.

**The Monitor pane's golden is build-sensitive by construction** and this is
not a regression to chase: its `memory` row prints `heap frontier 0x...` as
hex, so any change to the binary moves those pixels. It moved on both sweeps
that produced this section.

## 5.2 A pane that scrolls arbitrary content

A pane whose content is a list of equal-height rows scrolls with
`scroll-slice`, which windows the list by row INDEX. A rendered page has no row
height to give it -- labels, separators and links are all different heights --
so the Browser needed the other shape, and this is what it is. Any pane in the
same position should copy it rather than invent a third.

**Lay out, move the subtree, walk it again under a tighter clip.** Three calls,
in this order, and each one exists for a reason that bites if it is skipped.

1. `comp-walk-except` paints the tree with the scrolling subtree SKIPPED, under
   the pane's clip. That is the chrome.
2. `comp-draw-node` paints the scrolling subtree's own box where the layout put
   it, unshifted, under the tighter clip. Without it a scrolled subtree leaves
   the bottom of the viewport holding the previous frame, because the panel that
   would have covered it moved up with its children.
3. `comp-translate-kids` moves the children by the offset and `comp-walk-kids`
   paints them under the tighter clip.

**The tighter clip is the subtree's box INTERSECTED with the pane**, which
`comp-clip` does. The subtree's own bounds are not the visible region and using
them alone is the mistake to avoid: since WORKS-45 a flex child is not shrunk
below the minimum its content declares, so the node laid into the pane is taller
than the pane. Handing the shifted children the PANE's clip instead is the other
mistake, and it is the one with a visible symptom -- content shifted up by the
offset paints over the chrome above the viewport, which the pane clip cannot
stop because the chrome is inside the pane.

The tree handed back is the SHIFTED one. `comp-translate` stores into the nodes
rather than copying them, so a memoized tree and the painted geometry are the
same records and a hit test follows the scroll for free. Anything that memoizes
an unshifted tree gets clicks at the pre-scroll positions.

**The scroll state has to be fitted from a layout, and fitted BELOW the frame
mark.** `tab-new` builds a viewport of 0 by 0 and a content of 800 by 600
because a tab exists before any layout has said how big either is, so until it
is fitted `scroll-by` clamps against numbers that have nothing to do with the
page. `browser-scroll-fit` lays the tree out in a `__heap-save` /
`__heap-restore` bracket, reads two integers out of it, and writes the fitted
state before the pane takes its frame mark -- a state allocated above that mark
is freed on the pane's next event while the tab still points at it.

What it measured, at 1600x900 against the same seed: the thirteen-pane golden
sweep is byte-identical before and after, which is what a wiring that changes
nothing at offset zero must be. The proof it works is the pair either side of
one Page Down. Before, the keystroke produces a byte-identical frame, which is
the whole of what BROWSER-5 complained about. After, it moves 478,420 pixels,
every one of them in rows 228 to 827: nothing in the tab bar or the address bar
above, and nothing in the taskbar band at 828 and below.

## 6. The palette arrives as a parameter, not by citation

`GopDesk` cites `GopEdit`, so `GopEdit` reading `dk-pal ds` would be a citation
cycle. Any pane that needs the colour scheme takes it as an argument from the
desk. **Done for `GopFiles` and `GopEdit` 2026-08-19 (WORKS-17):** both take a
`Palette` and paint through named role accessors (`gfl-bg`, `ged-cursor` and
their neighbours) instead of the fixed `gfl-col-*` and `ged-col-*` ramps.

**The cycle is about where `dk-pal` LIVES, and a `Palette` parameter is the
answer for a second reason that outlives it.** The accessor could be moved
below both panes -- `GopStyleKit` cites nothing but `UI chapter Theme` -- and
the cycle would go. It would not help: `gfl-draw-frame` and most of its
neighbours never receive `ds` either, so a pane reading the palette from `ds`
would have to be handed `ds` through exactly the same functions. What is
threaded is the only real choice, and a `Palette` is what those functions
want, so nothing has to move.

**The palette has ten roles and a pane needs about seven pens, so some
collapse.** `dim` and `green` are both `pal-primary`, a selection band and the
editor's current-line band are both `pal-border`. Say so where you do it: a
collapse that reads as an accident gets "fixed" by the next person into a role
that means something else.

## 7. Running the desk

```powershell
build/desk.ps1 -Force -Disk seed/Codex.img -Shot out.bmp -ShotDelayMs 14000 -Keys '8000:33'
```

`-Keys` is `milliseconds:scancode`, semicolon separated. Esc is 1. The pane
scancodes are the ones `desk-dispatch` tests.

**Only the DELTA transfers from a bed run.** `desk.ps1` defaults to `-Mem 3072`
and the boot image gives the real desk 128 MB, so any absolute exhaustion
figure measured here is about twenty-four times too generous. Per-visit growth
is the number that means the same thing in both places.

**Carry a no-keystroke control.** Two captures, one with the key and one
without, is what distinguishes a pane that did not open from a pane that opened
and painted something you did not expect. It is also the arm that settles a
stale claim: the Edit pane was recorded on 2026-08-13 as unopenable from the
keyboard, and the same comparison on 2026-08-15 shows the two frames differing
and the file picker on screen.

**Pass `-Force` when you have changed a chapter other than `DeskVm.codex`.** The
staleness check stats every `.codex` under `apps/works` and `codex` as of main
15306, but a workspace synced before that judges staleness from `DeskVm.codex`
alone -- nine lines that call `desk-run` and never change -- and silently serves
the previous binary.
