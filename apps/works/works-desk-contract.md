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

## 0. Two kinds of pane, since 2026-08-18

**A pane is now either a LOOP or a STEP, and the rest of this file describes
the loop.** **Every pane is a step as of 2026-08-18.** All fourteen: Monitor,
Calendar, Appearance, Calculator, Clock, Programs, Diffusion, Issues, Console,
Files, Browser, 3D View, Aquarium, Editor. Sections 1 to 7 below still hold,
but no pane is a loop any more. The model is
`docs/Designs/Active/OS/ModernDesk.md` section 2, ruled by Damian on
2026-08-18. Every other pane is still a loop and sections 1 to 7 apply to it
unchanged.

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
number to stay open without the frame restore, and anything else as a SCANCODE
to dispatch after closing**. The scancode answer is the launcher's, which does
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
  **`GopEdit`'s 9 MB must not move into this record**: it is parked in a cell
  with no restore precisely so the base mark cannot reach it, and only Edit's
  typed values belong here.
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

`ds` is `alloc-zeroed 64 64` in `desk-run`: sixteen 32-bit cells at offsets
0..60. It is how a pane gets state without every pane signature growing a
parameter.

| offset | constant | holds |
|---:|---|---|
| 0 | -- | free |
| 4 | `desk-mark-cell` | the base heap mark (section 3) |
| 8 | `desk-second-cell` | last RTC second the taskbar clock painted |
| 12 | `desk-focus-cell` | which app the desk is stepping (0 none, 1 Monitor, 2 Calendar, 3 Appearance, 4 Calculator, 5 Clock, 6 Programs, 7 Diffusion, 8 Issues, 9 Console) |
| 16 | (bare literal) | the Monitor pane's repaint second |
| 20 | -- | free |
| 24 | (bare literal) | pointer: the calculator's state block |
| 28 | `desk-edit-cell` | pointer: the editor's state block |
| 32 | (bare literal) | the tracker's filter |
| 36 | `desk-console-cell` | pointer: the console's state block |
| 40 | (bare literal) | the tracker's selected row |
| 44 | `desk-prog-cell` | pointer: the launcher's state block |
| 48 | `desk-gpu-cell` | is a GPU present |
| 52 | `dk-scheme-cell` | colour scheme index |
| 56 | `dk-adorn-cell` | adornment bits |
| 60 | `desk-clock-cell` | pointer: the clock pane's state block |

**Two cells are free (0 and 20). Announce before you take one**, the way the
file claims table in `docs/PM/CurrentPlan.md` asks. Cell 12 was taken by val on
2026-08-18 for the focus id; the count above said three until then. Two agents took cell 48 independently on
2026-08-11; each change was green alone and the collision only surfaced in the
merge, because nothing in the tree cross-checks this block.

`dk-mon-tick-cell = 28672` is NOT a `ds` offset. It is an absolute address, read
as `peek-32 dk-mon-tick-cell 0`.

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

`desk-edit` has NO restore, deliberately. `ged-init` allocates 8 MB plus 1 MB on
first use and parks both pointers in cell 28, which is persistent, so restoring
would free the memory and leave the cell pointing at it. The fix belongs in
`GopEdit` -- allocate on entry and release on exit -- or the block moves into
`desk-run`, which costs 9 MB at boot for a pane that may never be opened.
`GopConsole` had the same shape at about 1 KB and took the cheap way out.

The 9 MB itself is one-time: `ged-ensure` is idempotent. What that pane pays on
EVERY visit is the root plus whatever `ged-run` allocates outside its own inner
brackets, `gfat-mount-esp` and the directory listing among them, and **that
figure has never been measured.** Until it is, "Edit is the unfixed one" is a
statement about the mechanism and not about the size.

**So the test before adding a restore to a pane: does anything it calls park an
allocation in a cell?** Grep the pane's chapter for `poke-32 <state> <n>` with
an allocation on the right. Integers are always safe; pointers are the question.

### `desk-loop` brackets each iteration, since 2026-08-18

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
- **Carry the F12 arm.** Every pane loop has `sc == 88 -> desk-shot`. A pane
  that omits it silently loses the screenshot key, which is also how flights
  are photographed.
- **Scancodes are a shared namespace.** Taken today: 4, 16, 18, 20, 23, 25, 30,
  32, 33, 37, 38, 46, 48, 50, plus 1 for Esc and 88 for F12. Check
  `desk-dispatch` and `dk-style-key` before choosing.
- **A launcher row is not free.** `gpr-split` in `GopPrograms` is an ENTRY
  INDEX that must land on a group boundary, so inserting before it moves the
  cut and the second column loses its heading. Appending at the end is free.
- **The sidebar is full.** It held thirteen at 1024x768 and that is why the
  launcher exists. A new pane costs a launcher row, not a sidebar button.

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

## 6. The palette arrives as a parameter, not by citation

`GopDesk` cites `GopEdit`, so `GopEdit` reading `dk-pal ds` would be a citation
cycle. Any pane that needs the colour scheme takes it as an argument from the
desk. This is structural, not a matter of taste, and it is why `GopEdit` and
`GopFiles` still paint from fixed constants (`works-backlog.md` WORKS-17).

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
