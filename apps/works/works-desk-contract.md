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
| 12 | -- | free |
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

**Three cells are free. Announce before you take one**, the way the file claims
table in `docs/PM/CurrentPlan.md` asks. Two agents took cell 48 independently on
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

### The instrument

The Monitor pane's `memory` row prints the frontier and the base mark. The gap
between them is what the current pane stack holds; it should not grow with the
number of visits. That row exists because this whole class was invisible without
it.

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
