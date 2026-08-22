# The Modern Desk -- multitasking, a bottom taskbar, a system menu, and the 3D surface

*Opened 2026-08-18 by red as a brief at Damian's direction. Design written by
val 2026-08-18. Status: **ALL STAGES DONE 2026-08-19** (stage 1 closed with no
code change; stage 0 is closed as far as a desk change can close it, and the
rest of it is one line in the compiler, named in its row). All fourteen panes are steps and no pane owns a loop (stage 9,
2026-08-18).
**MULTITASKING IS IN, all of it landed 2026-08-19.** Stage 10 put the mark
stack in `ds` cell 20 with Files as the pane that proves it; stage 11 added the
two 3D panes, so a hidden Scene keeps its render target and its toggles across
a visit to another app; stage 12 added Edit; stage 13 added the Browser and
closed WORKS-42. All four heavy panes can be left alive. The Browser carries
one rule, that it may only ever be the topmost heavy pane, so every other heavy
`-open` evicts it, and it stays alive under any of the nine light panes.
**Stage 4 closed with them: the taskbar names what is alive**, reading the mark
stack rather than a list of its own.
**Two defects turned up that had nothing to do with the stack and both are
fixed:** Edit's 9 MB buffer was never out of reach of the base mark (seven docs
said it was), and the Browser rebuilds its state at the current frontier on
every event.
**Stage 14 landed 2026-08-19: the system menu**, which is the launcher's tree
in a bounded left-anchored box, opened by scancode 41 or the taskbar button,
both verified. Damian's brief asked for it to float above that button and the
widget layer will not do it; the row says exactly why.
**Stage 15 landed 2026-08-19: 3D mouse-look**, which closes WORKS-13's 3D half.
Both 3D panes take the mouse now, and the first pointer movement takes the
camera off the HPET orbit for good.
**Stage 0 closed 2026-08-19 as far as this design can close it**: the Monitor
has a `stack` row wired to the real cells, and it reports that no CDX build can
narrow them, because `compile-to-cdx-with-exit-mode` passes the emitter's
`trace` argument as a literal `False` (`opening.codex:1325`). Threading that one
argument is a COMPILER change and not this design's to make; the row starts
answering the day it happens.
**Every stage of this design is now DONE.** The one
rough edge stage 13 left, the Browser's bottom edge moving
12 rows on re-entry, was rediagnosed on 2026-08-19 and is NOT the desk's: its
first paint and its repaints disagree about the page height with nothing else
running. It left this design as `BROWSER-5`.
This header said "stage 1 and the multitasking stages are open" until
2026-08-19, and it misrouted an assignment the next morning: stage 1 had been
closed by ruling and stage 9 had completed, both recorded in the stage table
below the sentence saying otherwise. Sixteen stage rows landed in one day and
the header was not touched once. Re-read the table before quoting this line.
Register rows live in `apps/works/works-backlog.md` (WORKS-30 the camera's
1.333 aspect, WORKS-31 the sidebar clipping, WORKS-32 the instruments,
WORKS-33 the taskbar and menu, WORKS-34 the band too short for a button,
WORKS-35 the Monitor's hard-coded sidebar; WORKS-22 carries the multitasking
measurement and now points here).
**Ids start at 30 because 26 and 28 are retired** -- both were used and closed
earlier (`docs/PM/Done/GitHubUpdates/GitHubUpdate42.md:56` records WORKS-26
closed at main 15001), so searching a reused id returns two unrelated items.
This file is the shape and the reasoning.*

## Damian's direction, verbatim (2026-08-18)

> i'd like to get val possibly working on the OS UI stuff. we need to lift
> that up to a more modern experience. feels very windows 3.1-ish still. i'd
> like to get that 3d stuff working better, and app multitasking. with the
> task bar at the bottom and some kind of start like system menu.

## Damian's rulings, 2026-08-18

Both arrived after the proposal was written and both settle something this
document had left open. They are recorded here rather than only in a CL
description because the stages below are read by whoever picks this up next.

1. **The shape: cooperative panes with saved state.** Invert the loop; panes
   become steps the desk's single loop calls. That is section 2's proposal,
   ruled as written, so the stages that were waiting on it are open. No cores
   are involved, and section 2 says what would have to be true before they
   could be.
2. **`widget-panel` flex defaults to 1** (rulings queue 9). It already did,
   so stage 1 closes with no code change. This document had the default
   backwards and asked for a ruling on a premise that was false; the section
   below says so in full, because the wrong claim is now in three files and
   whoever meets it next should meet the correction with it.

## 1. What exists, measured 2026-08-18

Every number below came from `build/desk.ps1` against the Release 46 seed
`12B07296419847B2` on this box, not from reading. Two arms per claim where a
claim could be made by either.

**The desk is fourteen panes reached by direct call.** `desk-dispatch`
(`GopDesk.codex:1329`) tests a scancode or a widget hit and calls one of
`files, scene, fish, monitor, calc, console, cal, trk, dif, browser, edit,
style, programs, clock`, plus shutdown and the F12 snap. Each pane runs its
OWN loop until Esc, then restores the heap to the base mark, rebuilds `root`,
and tail-calls `desk-loop`. `desk-monitor` is the whole shape in twelve lines.

**The bottom bar already exists in the tree and is empty.** The brief says it
does not. It does: `desk-taskbar` is a `DirRow` panel with a 24px minimum
sitting in the chrome's bottom slot (`desk-chrome-with`), and it carries a
`CODEX` label and a flex spacer and nothing else. It is also not inert -- F12
paints its verdict strip into that band. **So the taskbar is a content
problem, not a structure problem**, which makes it much cheaper than the brief
assumes.

**The clock is in the TOP bar, not the taskbar,** despite `ds` cell 8 being
named `desk-second-cell` "last RTC second the taskbar clock painted".
`desk-clock` draws through `desk-draw-clock` into the top band. The name is
the only thing pointing at a taskbar.

### Two layout defects visible in an ordinary boot

Both are in the screenshots taken for this design and neither has a register
row today.

- **The first sidebar button is cut in half by the top bar.** The
  no-keystroke control frame shows `Programs` clipped to about half its
  height and the `CODEX` brand row gone entirely; `Files`, `Edit` and
  `Console` below it are clean. This is the failure `desk-chrome-with`'s own
  prose says was fixed by giving BOTH columns a top gap, so the fix does not
  hold here.

  **Corrected 2026-08-18, after the fix: the cause is not the one first
  written here, and the defect is not confined to 1024.** This paragraph
  blamed `dk-bar-h * ui-scale w / ui-wscale w` for mixing the two scales at
  the width where they disagree. That expression is right at every width: it
  yields 22, 44 and 44 logical against a bar of 22, 44 and 44 physical at 800,
  1024 and 1600. The actual cause is that `desk-draw` lays out `desk-chrome`,
  a hand-copied duplicate of `desk-chrome-with` that never received the gap,
  so the fix held for every PANE and failed on the DESK -- at 1920 as well,
  which the first reading would not have predicted and the capture confirms.
  The diagnosis was reasoned from the one expression that looked suspicious
  instead of from which tree the failing frame was drawn by.
- **At 1600x900 the sidebar buttons are cut off on the RIGHT.** `Programs` and
  `Shutdown` lose their last characters under the content region. The desk
  sidebar column is 160 logical wide and the 3D pane starts at its own
  `gsc-sidebar-w = 160` PHYSICAL, and at 1600 the widget layer is at scale 2
  while that constant is not scaled. `GopScene`'s prose already admits the
  chrome sizes are "hand-synced" with `GopDesk`; this is what hand-syncing
  costs.

### The 3D surface: the scene is stretched at every aspect but one

**Measured, with the camera frozen so both arms are the same instant**
(`-Rtc 2026-08-18T06:00:00`, which pins the HPET as well as the CMOS, so the
RTC-driven orbit angle is identical in the two frames):

| mode | content region | sphere bounding box | w/h |
|---|---|---|---|
| 1024x768 | 864 x 668 | 224 x 216 | **1.037** |
| 1600x900 | 1440 x 800 | 373 x 259 | **1.440** |

The subject is the yellow sphere, isolated by `B < 20 & R > 40 & G > 30 &
0.6R < G < R`, a filter calibrated against sampled pixels rather than guessed
(the first attempt thresholded at `R > 130` and measured only the specular
highlight, giving a 106x36 box that is not the sphere).

**The mechanism, corrected 2026-08-18 after a naive-reader probe found the
first version of this section wrong.** It said "there is no aspect term
anywhere on that path". **That is false, and the correction changes the fix.**

There IS an aspect term, and it is fed a constant:

- `camera3d-new` (`codex/foreword/engine/Scene3D.codex:56`) takes eye, target
  and fov and **no aspect argument at all**, and its constructor sets
  `c3-aspect = 1.333` (`:64`), a literal.
- `camera3d-proj` (`:71`) hands that to `mat4-perspective`, which applies it
  to X only: `fx = if aspect ~ 0.0 then f else f / aspect`
  (`codex/foreword/math/Matrix4.codex:87`).
- **`c3-aspect` has exactly three occurrences in the tree, all inside
  `Scene3D.codex` (53, 64, 71). No caller anywhere overrides it**, and
  `GopScene.codex:45` builds its camera with the three-argument
  `camera3d-new`, so the desk's 3D pane runs at 1.333 whatever the mode is.
- Only then does `r3d-project-clip`
  (`codex/foreword/engine/Renderer3D.codex:177-178`) map `ndc-x` by half-WIDTH
  and `ndc-y` by half-HEIGHT, adding no further compensation.

So the on-screen aspect of an object is `viewport_aspect / 1.333`: the scene
is authored for 4:3 and stretches everywhere else in proportion.

**The 1.333 cancels between the two arms, which is why the RATIO still
predicts to 0.2 per cent and why the measurement alone could not have caught
the error:**

- predicted stretch, 1600x900 against 1024x768: `(1440/800) / (864/668)` =
  `1.8000 / 1.2934` = **1.3917**
- measured stretch, sphere ratio against sphere ratio: `1.440 / 1.037` =
  **1.3886**

**The residual, and the arithmetic error that inflated it.** This section
first reported a 7 per cent unexplained offset. That was wrong: it divided by
the SOFTWARE target height (`ch`) while the frame being measured came from the
host rasterizer, whose viewport is `ch - gsc-label-band`, 20 rows shorter.
Against the correct viewport the residual is **3.7 per cent**, and it is not a
renderer defect at all: the ball is `mesh-sphere 700 12 8`, a twelve-segment
polyhedron whose silhouette ratio reaches `1/cos(15 degrees)` = **1.0353**
against a measured 1.037. **So 1.037 is the mesh's own floor**, and the fix
converging to it rather than to 1.000 is the correct result, not a shortfall.

**Two viewports, not one, and the fix has to respect that.** The software path
renders into `tgt` at `cw x ch`; the host path renders into `gv` at
`cw x (ch - 20)`. They are different aspects, so each path takes its own. That
also means the two paths frame the scene slightly differently, which is
pre-existing and unaddressed: software renders 20 rows that the label band
then covers.

**Both render paths share the camera**, so this is not a software-versus-GPU
question: `GpuScene.codex:110` takes the same `camera3d-vp` and `:325-327`
does the same half-width/half-height mapping. Toggling `G` in the pane must
not change the symptom, which makes it a free path-independence arm.

This also settles what "the 3D stuff working better" should mean first: it is
not more triangles or a faster rasterizer, it is that a circle is a circle at
16:9.

### What is NOT measured, and must not be assumed

- ~~**Frame time in the bed, at any resolution.**~~ **MEASURED 2026-08-18**,
  stage 0. `gsc-hud` prints `f<n> <ms>ms` in the label band, so a rate reads
  off one screenshot:

  | viewport | host rasterizer | software |
  |---|---|---|
  | 1024x768 | **16 ms/frame** | **86 ms/frame** |
  | 1600x900 | **16 ms/frame** | **135 ms/frame** |

  **The host figure is a 60 Hz pace, not a counter cap.** 959 frames at 20 s
  and 1799 at 34 s is 840 frames across the intervening 14 s, 16.67 ms apiece,
  so it scales linearly with wall time and something outside this renderer
  sets the rate. In this range the host path is therefore PACED rather than
  resolution-limited, and only the software path pays for pixels. It pays
  sub-linearly: 1.57 times the time for 1.78 times the pixels, which is the
  resolution-independent geometry and setup cost showing through.

  **Metal is still unmeasured.** Every figure above is this box under
  codex-vm. `GopScene`'s prose, "about a second a frame on metal"
  (2026-08-08), is the ASUS and neither confirms nor contradicts them.
- **The stack half of the arena.** The desk contract states plainly that the
  heap half is fixed by the base mark and the stack half "has no owner and no
  instrument": `stack-min-rsp-addr` only moves in a `trace-alloc` build. The
  contract is right and `docs/ArchitectsSketchbook.md` was wrong about it;
  `emit-prologue` (`codex/compiler/Emit/X86_64.codex:12`) guards the RSP
  minimum store with `if st1.trace-alloc`, so an ordinary desk never writes it.
  Corrected in the Sketchbook 2026-08-18.

### The stage 5 arm was void, and this is the correction

**Measured 2026-08-18, Monitor `memory` row, `-Force` bed at 1600x900:**

| visits | heap frontier | desk mark | gap |
|---|---|---|---|
| 1 | `0x649ff0` | `0x63fca8` | 41,800 B |
| 10 | `0x6476e0` | `0x63fca8` | **31,288 B** |

**The gap after ten visits is SMALLER than after one.** The heap is already
flat, because the base heap mark already fixes it -- that is exactly what the
contract's section 3 says it does, and it has been true since long before this
design. So "the frontier-to-mark gap must be flat" is satisfied by the code as
it stands and cannot tell a converted pane from an unconverted one. It was an
instrument that could not fail.

### What a LIVE app costs, and the flatness above is now conditional

**The paragraph above was unconditional until 2026-08-19 and is not any more.**
Stages 10 and 11 let an app stay alive across another app's visit, and a live
app's bytes sit ABOVE the base mark by design, so the frontier is flat only
while nothing is alive. Measured the same way, Monitor `memory` row, `-Force`
bed at 1600x900:

| state | heap frontier | held over a desk that never opened one |
|---|---|---|
| nothing alive | `0x645ca0` | -- |
| one 3D pane alive and hidden | `0x0e1b6e8` | **8,215,112 B** |
| one further switch round trip | `0x0e44228` | 166,720 B more |
| after the pane closes | `0x645ce8` | 72 B |

**The 8.2 MB is the pane's render target, colour and depth at the viewport
size, and it is not reducible while the pane is alive: freeing it IS closing
the pane.** It does not accumulate. One open-and-close, two open-and-close, and
two switch cycles then close all leave the frontier at `0x645ce8`, so the 72 B
is a one-time offset and not a per-close leak. Files, the other wired pane,
costs 22,352 B per switch and holds far less. The 3D panes are the expensive
case, and they are the reason the taskbar's live-app row is worth building:
8.2 MB held by something invisible is the kind of thing a user should be able
to see and close.

**The error underneath it was conflating the two halves of the arena.** The
heap is fixed; the STACK is what the per-visit frame accumulates in, and the
stack is what this design's model changes. The old arm pointed at the fixed
half.

**And the stack half cannot be measured by cycling either.** A frame is five
pushes plus locals, the visit chain is a few frames deep, and the arena is 128
MB, so the number of open/close cycles needed to make it visible is in the
hundreds of thousands. **So "measurable on the first converted pane" was
false in both halves** and is withdrawn.

**What replaces it measures the feature instead of the memory.** With a pane
open, the desk's loop is not running, so the taskbar clock stops while the
pane's own clock row keeps going. The lag between them in a SINGLE frame is
exactly how long the pane has owned the loop: measured 4 s at four seconds
open and 18 s at eighteen. Under panes-as-steps the desk's loop keeps running,
so that lag must go to 0. It is one capture, it is calibrated, and it fails
loudly if the conversion does not do what the design says.

### What stage 5 actually found, 2026-08-18

The arm passed, and two things it was not aimed at came out of the same run.

**The idle desk was leaking 3,594 bytes a second, and had been all along.**
Measured on the Monitor row: 69,896 B of gap after 10 seconds idle against
213,656 B after 50, linear. `desk-clock` builds Text and walks the taskbar
subtree on each RTC edge and `desk-loop` never reclaimed it. On the 128 MB
bare-metal arena that is roughly ten hours to exhaustion. Nobody had seen it
because reading it requires sitting idle and then opening the Monitor, and
every previous measurement opened a pane immediately.

**The step model would have made it worse, and that is what surfaced it.** A
loop pane replaced the leaking idle loop with its own bracketed one, so opening
a pane PAUSED the leak; a step pane leaves `desk-loop` running, so the leak
would have run for as long as an app was up. `desk-loop` now brackets each
iteration, and the frontier is identical at 22 s open, 62 s open and 50 s idle.

**One regression, accepted and recorded rather than fixed here.** The desk
repaints the taskbar band once a second, so anything hand-painted into it by a
STEP pane is transient: F12's verdict appears and is gone within the second.
Under a loop pane it survived because the desk was not running to overwrite it.
Where transient notifications live is a question for the stage 6 conversions,
not a patch to make here.

## 2. Multitasking: invert the loop, and do not reach for cores

### The two questions WORKS-22 asked, answered

That row (2026-08-13) declined to design this and left two questions, and they
are the right two:

**"What would a second core RUN?"** In this design, nothing. SMP is
deliberately out of scope, and the reason is not timidity: the desk is not
concurrency-bound. `desk-loop` is a spin poll with no halt, and WORKS-21
already established that what felt clunky was REPAINT COST, a drawing problem.
Adding a core does not make a repaint cheaper; it adds a second writer to a
framebuffer that has no compositor and no lock. The scheduler chapters under
`codex/os/sched/` exist and are cited by nothing, and `os-state-new` starts at
`core-table-new 1` with `smp-active = False`. Wiring them is a real project
and it is not this one.

**"Who owns the framebuffer while it runs?"** The desk, always and only. No
app paints outside its content rectangle; the chrome, the taskbar and the
cursor belong to the desk. This is already true and the design keeps it true,
which is what makes concurrency unnecessary rather than merely deferred.

### The model: apps are STEPS, the desk owns the loop

Today a pane is a function that takes the keyboard and does not give it back
until Esc. That single fact is what makes "two apps at once" impossible, and
it is also the cause of the accumulation the desk contract opens with:

> A pane does not return to the desk. It tail-calls `desk-loop` again ... A
> pane visit also stacks a FRAME, and nothing reclaims that.

**Invert it.** A pane stops owning a loop and becomes a step:

```
    <pane>-step : <state>, Event -> Integer      -- handle one event, paint, return
```

The desk keeps one loop. It polls the keyboard and mouse once, decides which
app has focus, and calls that app's step. An app is "running" because its
state block persists in `ds` (or in a table the desk owns), not because a
frame of its loop is on the stack.

**Three things fall out of this, and the third is the argument.**

1. **Multitasking is then free.** N apps are alive at once because N state
   blocks exist. Switching focus is choosing a different step to call. Nothing
   is saved or restored on a switch because nothing was ever on the stack.
2. **The taskbar has something to show.** A list of live apps is a list of
   allocated state blocks, which is exactly what the taskbar needs to paint.
3. **The mutual recursion disappears, and with it the per-visit frame.**
   `desk-loop -> desk-dispatch -> pane -> desk-loop` is the cycle the contract
   calls out as not-a-tail-call. With steps there is no cycle: the desk's loop
   is self-recursive, which the contract already certifies as safe ("the idle
   path is self-recursive and safe"). **So this design does not merely avoid
   making the stack problem worse; it is the fix for it.**

That third point is why this model is chosen over the alternatives rather than
as a compromise between them. Green threads or a core per app would each ADD a
stack discipline to an arena whose stack half nobody measures.

### What it costs, honestly

**Fourteen panes have to be turned inside out, and they are not equal.**
`desk-monitor`, `desk-cal`, `desk-calc` and `desk-style` are shallow loops
over a small state and convert cheaply. `GopEdit` and `GopConsole` carry their
own multi-level loops and their own state machines and are the expensive ones;
`GopScene` runs a render loop with a heap bracket per frame and a keyboard
poll of twelve steps per iteration, which is a step function already in all
but name.

**`desk-edit` is the known exception and stays exceptional.** `ged-init` parks
an 8 MB buffer pointer and a 1 MB index pointer in a persistent `ds` cell.
**This paragraph said the contract records why the pane has no heap restore;
it always had one, and that was the defect stage 12 found** (2026-08-19): the
pointer is below the base mark and survives, the memory is above it and does
not. Under this design that stops being a special case and becomes the NORMAL
case -- a long-lived app with a
long-lived state block is what multitasking is -- but the conversion must not
be attempted until the cheap panes have proven the protocol.

**So the order is: protocol, then one cheap pane, then the rest one CL each,
and Edit last.**

### The `ds` block is nearly full and this design needs cells

Three cells are free (0, 12, 20) and the contract requires announcing before
taking one, after two agents took cell 48 independently on 2026-08-11. This
design needs a focused-app id and a pointer to the app table, which is two of
the three. **Taking the last free cell for a table pointer is the wrong
shape**: the app table should be one allocation in `desk-run`, below the base
mark, with its address in one cell, and everything else indexed inside it.
That spends one cell, not two.

### Stage 9 did not deliver more than one app ALIVE, and this is where it stops

**Measured 2026-08-19 (val) by reading `GopDesk.codex`, against the depot at
main 17265.** All fourteen panes are steps, the desk keeps painting and keeps
time while a pane is up, and that is real. It is not two apps alive. Every
`desk-*-open` builds a `DeskApps` with its own field `Just` and the other
three `None` (`desk-edit-open`, `desk-files-open`, and the rest), and both
close paths do the same:

```
in let dropped = __heap-restore (peek-32 ds desk-mark-cell)
in let fresh = DeskApps { da-files = None, da-browser = None, ... }
```

**The prose above `desk-app-close-to` states the reason and it is correct:**
the restore frees everything above the desk's base mark, which is exactly
where a pane's `-open` put its state, so carrying the record forward would
carry pointers into memory just handed back. So this is not a field that was
left unset. **It is a heap-lifetime property, and no change to `DeskApps` as a
threaded parameter can alter it**, because a threaded record lives above the
base mark by construction.

**Section 6 solved a different problem than the one the brief asked for.**
Option 3 carried typed state across ITERATIONS of one pane's own life. Staying
alive across ANOTHER pane's close is a second question, it was never asked
there, and closing all fourteen conversions did not answer it. That is
L-CAPABILITY read off our own campaign: fourteen closed features, capability
absent.

**Correction, same day, and it is against the paragraph above.** This section
first said the shape that works was already written one section up, in the
`ds` paragraph: *"the app table should be one allocation in `desk-run`, below
the base mark, with its address in one cell"*, and that **"below the base mark
is the whole of it"**. That last sentence is wrong and it was published to
main at 17291 before `GopDesk.codex:633` was read.

Below the base mark is the whole of it for the RECORD. It is not for what the
record POINTS AT. A pane's state is allocated in its `-open`, above the base
mark by construction, and putting the four-field table underneath does not
move the `FilesState` or the 4.6 MB `R3dTriState` it names. Building those at
boot instead is what the contract already refuses for Edit's 9 MB.

**`GopDesk.codex:633` had the real answer written down before this design
existed, and it is sharper than anything above:**

> Several apps alive at once, rather than one focused app with its state,
> would need the base mark to RISE when an app opens and FALL when it quits.
> The heap is a bump allocator with save and restore and nothing else, so that
> is a mark STACK, and a mark stack is only correct if apps quit in the order
> they opened. That is a separate step and this record does not pretend to it.

So the stage is a mark stack, not a relocated record, and it carries a
constraint neither the `ds` paragraph nor this section had: **LIFO**. An app
that quits while another opened after it is still alive cannot have its memory
reclaimed, because the frontier above it is live. It can be marked dead and
its bytes held until the apps above it quit. That is correct, it is bounded by
the number of heavy panes rather than by time, and it is the honest cost of a
bump allocator with no compaction.

## 3. The taskbar and the system menu

Both are widget-tree work in `desk-chrome-with`, and the slot for one of them
already exists.

**Taskbar (bottom).** `desk-taskbar` gains: a system-menu entry at the left,
one button per live app in the middle, and the clock at the right, moved down
from the top bar. The band is painted by the widget layer, so it follows the
palette automatically, unlike the top bar which `desk-topbar` paints outside
the tree and which the layout has to be told about twice.

**That last sentence is the whole reason the clock repaints the way it does.**
The clock changes once a second and the desk loop does not rebuild its tree,
so the band under the text has to be restored by hand. It is restored by
running `comp-walk` over the taskbar node -- the real renderer, over the
bounds it already laid -- and not by a fill of our own, because the band
carries a vertical gradient under the default adornments and a flat fill
sized to the text leaves a seam. That costs one band-height fill and three
labels a second against a full `comp-render` of the chrome, which this loop
must not pay for at that rate.

**System menu.** A panel that opens above the taskbar button listing every
pane by group -- which is what `GopPrograms` already is. **The launcher should
be reused rather than reimplemented**, and the contract is explicit about why:
"a row in the launcher and the key it names must arrive by the same road", and
a second dispatch table is the thing `GopPrograms` exists to avoid. So the
system menu is a second PRESENTATION of the launcher's model, not a new list.

**RULED 2026-08-18 by Damian: `widget-panel` flex DEFAULTS TO 1. That is what
it already did, so stage 1 is CLOSED with no code change.**

**This paragraph used to state the default backwards, and the error reached a
ruling.** It said `widget-panel` flex defaults to 0 and framed stage 1 as
changing it tree-wide. `Widget.codex:44` sets `wn-flex = 1` in the
constructor and has since before this design was written (last touched at
main 16020, 2026-08-16). `widget-label`, `widget-button` and
`widget-separator` default it to 0, and that asymmetry is presumably what got
transposed.

**The source said so plainly and was cited without being read.**
`apps/browser/browser-backlog.md` BROWSER-2, the row this design pointed at
for the question, opens with "`widget-panel` defaults `wn-flex` to 1" and
warns that defaulting it to 0 "should not be done casually -- every panel in
`apps/` and `codex/foreword/ui/` currently relies on the flex-1 default". So
the real open question was whether to flip it TO 0, and the ruling declines
to. Nothing shipped wrong, because the answer and the code agree.

**The trap BROWSER-2 exists to record is real and untouched by this.**
`widget-set-min` sets a minimum and PRESERVES flex, so a panel asked for a
fixed height with `widget-set-min` alone stays flexible and the minimum never
binds; the fix is an explicit `widget-set-flex ... 0`. That is why the
taskbar's own `widget-set-flex ... 0` is load-bearing, and it is worth more
than the default ever was.
It is this design's: a taskbar of live apps is a row of buttons that must
share the space, and a menu is a column that must not stretch its rows. The
first stage settles the default tree-wide and takes the consequences in one
CL, rather than every future panel repeating `widget-set-flex ... 1`.

## 4. Stages

One CL each. Every stage names the arm that proves it, and every arm is a bed
run because goldens over desk chrome are PARKED (`ExaminersAssay.md`), so
panes are verified by capture with a no-keystroke control.

| # | Stage | Arm |
|---|---|---|
| 0 | **The instrument is IN 2026-08-19 and it reports that the measurement is impossible, which is the finding.** The Monitor grew a `stack` row reading `stack-min-rsp-addr` and `ram-size-addr` (28736 and 4072, hand-synced literals because this chapter cannot cite the compiler) with `peek-qword`. The boot prologue stores `ram-size` into the minimum cell unconditionally (`X86_64Chapter.codex:385`) and only the per-function prologue narrows it, guarded by `trace-alloc` (`X86_64.codex:12`), so the two cells reading EQUAL is exactly what "nothing ever wrote a lower RSP" looks like and the row says that rather than printing a depth of zero as though it had measured one. **`compile.ps1 -Trace` does NOT enable it**, which I expected it to and tested: a traced desk is byte-for-byte the same SIZE as an ordinary one and the row still reads never-narrowed. The cause is one line: `compile-to-cdx-with-exit-mode` passes the emitter's `trace` argument as a literal `False` (`opening.codex:1325`) and takes no parameter that could carry the mode word, so **no CDX the shipped path produces can narrow that cell**. | **The frame-counter half was met 2026-08-18** and is unchanged: `gsc-hud` prints `f<n> <ms>ms`, so one capture yields a rate, and under a pinned clock the scene is byte-identical across ten seconds while the counter goes 598 to 1196, separating frames PRODUCED from picture CHANGED by measurement. **The stack half is now instrumented but still unmeasurable**, and the blocker is named to a line rather than left as "no instrument": thread `trace` through `compile-to-cdx-with-exit-mode` and the row starts answering with no further change here. That is a compiler change and is not this design's to make. Ordinary build reads `never narrowed -- trace-alloc is off in every CDX build`; a plain desk is byte-identical to before the CL and five desk tests pass. |
| 1 | **CLOSED 2026-08-18, NO CODE CHANGE.** Ruled: `widget-panel` flex defaults to 1, which is what `Widget.codex:44` already does. This stage existed because this document stated the default backwards; see the ruling section. | Not needed. The arm was "capture every pane before and after", and there is no after: nothing changes, so nothing can move. |
| 2 | **DONE 2026-08-18.** The two sidebar clipping defects. The top clip (all widths, not 1024 only) and the 1600 right clip, including the hand-synced `gsc-sidebar-w`. | Met. Captures at all three widths, both arms off the same seed with `-Rtc` pinned: first button and brand row fully visible, no text truncated. Null arm held -- at 1024 the 3D view region is 0 differing pixels of 663,552 while the sidebar column differs only below y=44. |
| 3 | **DONE (val, 2026-08-18), and the arm ran.** Sphere ratio 1.037 / 1.042 / 1.038 at viewport aspects 1.3333 / 1.8462 / 1.8333, against 1.440 at 16:9 before. The 1024 arm did not move, and at 1600 only the WIDTH of the bounding box changed (373x259 to 270x259), which is the signature of an X-only correction. The residual 1.037 is the ball's own faceting (`mesh-sphere 700 12 8`, `1/cos(15 degrees)` = 1.0353), not a defect. Fixed in `GopScene` by setting the field, so no foreword change and no seed. **Give the camera the viewport's aspect.** Set `c3-aspect` from the content region at the caller, which means `camera3d-new` grows an argument or `GopScene` sets the field. **Do NOT add a term to `r3d-project-clip` instead**: the camera already divides X by `c3-aspect`, so a second correction squashes the scene the other way at every resolution. If the fix is ever moved into the projection, `c3-aspect` must become 1.0 in the SAME CL. | The sphere-ratio measurement above, re-run: 1.037 at 4:3 must stay, and 1.440 at 16:9 must come to within a few per cent of it. The prediction is written down, so the arm can fail. Plus the `G` toggle, which must move identically because both paths share the camera. **And a 4:3 content region at a DIFFERENT resolution** (a mode whose content is 4:3 but is not 1024x768): the ratio must return to the 4:3 value, which separates aspect from resolution and is the arm that would falsify the whole account. |
| 4 | **PART DONE 2026-08-18.** Taskbar content: system-menu entry and clock landed; live-app buttons wait on the shape ruling. | Met for what landed. Clock advanced 09:09:51 to 09:10:32 across two shots; bands captured at all three resolutions; under `lcars` the band goes black-to-grey and the clock blue with no seam behind it, which is the arm that a hand-painted fill would fail. `codex/test/apps/desk-taskbar-hit.codex` adds what a capture cannot see: a pointer at the entry's centre resolves to `menu`, not to the band. **Two corrections to this row's assumptions.** The entry is a LABEL, not a button: the band is `dk-task-h` = 28 logical and a themed button needs 36 to draw its box, so a button renders as a sliver with its text cut through (WORKS-34). And the middle slot is a transparent spacer rather than a panel, because an empty panel paints its own box and shows as an inner rectangle under `lcars`. |
| 5 | **DONE 2026-08-18.** The step protocol plus `desk-monitor` converted. | **Met.** Clock lag 18 s before and **0 after** at identical timing, and 0 again at 58 s open. Controls: Esc returns to the desk, F12 still fires, Programs still opens, the three desk tests still match their sidecars. **The run also found a leak the arm was not looking for**: the idle desk was losing 3,594 B/s and nobody had measured it. `desk-loop` now brackets each iteration; frontier identical at 22 s open, 62 s open and 50 s idle. |
| 6 | **Calendar converted, 2026-08-18.** The dispatcher generalised with it: `desk-step-of` is the only function that knows which focus id means which app, and `desk-loop` names no pane. | **Met, and calibrated against the HOST clock rather than an in-guest reference.** With the Calendar open the taskbar had no clock at all before; after, it reads 11:11:16 against a host time of 11:11:16 at the same instant. Monitor still steps through the new dispatcher (11:12:36 both clocks, host 11:12:36). Esc returns, F12 fires, three desk tests unchanged. |
| 7 | **Appearance converted, 2026-08-18.** The step protocol grew to carry the whole event, `sc` and `clicked`, because this is the first pane driven by the pointer as well as the key. | **Met.** Clock 12:03:34 against host 12:03:34 with the pane open. Function preserved: two space presses still cycle seahawk to terminal to lcars and repaint the whole desk, with the taskbar band following the palette. Esc returns, three desk tests unchanged. |
| 7 | **Calculator converted, 2026-08-18.** | **Met.** Clock 12:08:34 against host 12:08:34. Function preserved and checked for the one thing this conversion could quietly break: `gcalc-key` APPLIES the key as well as answering whether it meant anything, so evaluating it twice would enter every digit twice. 1 2 3 gives `123`, not `112233`, and 7 + 3 = gives 10. Esc returns, three desk tests unchanged. |
| 7 | **Clock converted, 2026-08-18.** | **Met, and it is the strongest single frame so far**: the pane's own clock, the taskbar clock and the HOST clock all read 12:13:53 together. Set-mode still opens with every field. **Two things came out of it.** The pane's `last` was a loop parameter and a step has no loop, so it moved to offset 40 of the pane's own state block rather than spending one of the two remaining `ds` cells on a repaint cache; offsets 0 to 36 there are `GopClock`'s and 40 up are the desk's, now written down. And the pane never carried the F12 arm the contract requires, so the screenshot key was silently dead in it; added and verified. **Residual, recorded in WORKS-37**: the taskbar band is contested and the clock flickers in step panes. |
| 7 | **Programs converted, 2026-08-18. Stage 7 complete.** The launcher is the pane that made the protocol grow its third answer: a step returns 0 to close, 1 to stay open, and anything else as a scancode to dispatch after closing. Unambiguous because every dispatch scancode is 2 or more. | **Met, and it exercises the whole chain**: Enter on the launcher closes it and opens the Clock, and down-down-Enter opens the Calendar, each of which is itself a step. So a step pane closes into another step pane through the one dispatch. Esc returns to the desk. Three desk tests unchanged. |
| 7.5 | **WORKS-37, the contested taskbar band, fixed 2026-08-18.** | **Met, and not by the return-value change the row proposed.** Every chrome render goes through `dk-chrome-paint`, which renders and then invalidates the clock gate, so the next `desk-clock` repaints microseconds later. One band repaint per chrome render instead of one per loop iteration, no protocol change, no signature churn across six panes. Verified on the Clock pane, the worst case since it repaints every second: taskbar clock present at 14, 17, 21 and 26 seconds open, and present in the exact keypress-driven frame that was blank before. |
| 8 | **Diffusion converted, 2026-08-18.** The still-picture panes share one step. | **Met.** Clock 13:11:24 against host 13:11:24. `desk-cal-step` became `desk-quiet-step` and now serves both the Calendar and Diffusion; Calendar re-verified on it. Esc returns, F12 fires, three desk tests unchanged. |
| 8 | **Issues converted, 2026-08-18.** | **Met.** Clock 13:18:25 against host 13:18:25. Function preserved: up/down selects and `f` cycles the filter, all to open, 14 rows to 9, matching the header's own count. The pane owns `f` while focused even though `f` opens Files from the desk, which is what a focused step is for. Its `DbServer` rebuilds per key rather than per poll, which is cheaper than the loop it replaces. Esc returns, F12 fires (caught at 250 ms, transient per WORKS-37). |
| 8 | **Console converted, 2026-08-18. Nine panes are steps.** | **Met.** Clock 13:25:42 against host 13:25:42. Function preserved: `ls` types one character per key and runs, listing the real ESP (EFI, SOURCE.SRC, CODEX.CDX, SRC). Its `mods` shift state moved from a loop parameter to offset 16 of the console block, the same desk-owns-the-tail split as the clock. Esc returns, three desk tests unchanged. |
| 9 | **UNBLOCKED 2026-08-18. Option 3 ruled and landed (main 17055).** `DeskApps` rides beside `root` through `desk-loop`, one `Maybe` per heavy pane; see section 6. Landed as its own CL with nothing reading it, so the nine could be re-verified against a channel that changes no behaviour. | **Met.** All nine panes open with a live taskbar clock after the change, and the three desk tests match their sidecars. Re-run after the seed moved to `55E53A81`, because the first run was against `12B07296` and would not have counted. |
| 9 | **Files converted, 2026-08-18. Ten panes are steps, and the first with typed state.** `FilesState` carries the volume, the listing, the cluster stack and the names; `gfl-loop`, `gfl-move`, `gfl-activate` and `gfl-back` are replaced by `desk-files-step` and four helpers. `GopFiles` keeps every measure and every pixel. **The step protocol grew a fourth answer**: a NEGATIVE number means stay open and do NOT restore the frame, which is what a step that just grew durable state needs; see the loop's own prose. | **Met.** With Files open for 37 s the taskbar clock read 14:17:3x against a host capture time of 14:17:37, so the desk kept painting the whole time the pane was up. Function preserved: root lists, Enter descends to `ESP:/EFI/`, Esc ascends, Esc at root closes to the full desk, and a source file previews with syntax highlighting. Reached both by `f` and through the launcher, which is the `desk-app-close-to` route and the one that had to be checked -- the state is allocated after that path's base-mark restore, not before it. **Heap: the frontier and the desk mark after ten directory changes are bit-identical to after none** (`0x645bc0` / `0x63fcb8` both arms), and 100 navigations in one session still render correctly. All ten panes byte-identical across the `7590CCA1` and `CAE56FBC` seeds; three desk tests pass on both. |
| 9 | **Browser converted, 2026-08-18. Eleven panes are steps.** `BrowserPane` holds the `BrowserState` and the modifier bits; `gbr-step` and `gbr-open`/`gbr-first-paint` replace `gbr-loop` and `gbr-run`, keeping the old loop's branch structure because its heap discipline is the delicate part. The pane now opens at `h - dk-task-h * ui-wscale w`, so the taskbar strip belongs to the desk instead of being stamped over a page once a second. | **Met, and it is an exact reading**: with the Browser open the taskbar clock says 14:59:15 and the host says 14:59:15, having been opened 38 s earlier. Function preserved and proved through the whole key path: Ctrl+T opens a second tab and it stays open, which exercises modifier tracking, key decode, dispatch, state rebuild and repaint in one arm; `t` is the desk's Console key and the Console did not open, so the focused step owns it. Esc returns to the full desk. **Heap: frontier and desk mark after ten browser events are bit-identical to after none** (`0x645be0` / `0x63fcc8`). Ten of the eleven panes are byte-identical to before the CL; the Monitor differs by 64 pixels, which is the heap-frontier digits moving because `DeskApps` gained a field. Three desk tests pass. |
| 9 | **3D View and Aquarium converted, 2026-08-18. Thirteen panes are steps and the Editor is the only loop left.** One step serves both, because they always shared `gsc-loop`: the scene BUILDER stays a parameter and the desk passes `gsc-scene` or `gf-scene` by focus id. It cannot be a field on `ScenePane` instead, since `GopFish` cites `GopScene` and a discriminator resolved there would have to call back. **This pane needed no negative answer at all** -- the toggles and the frame counter are integers stored in place, and everything a frame builds is freed by `desk-loop`'s bracket, which is exactly the job `gsc-loop`'s own mark did. | **Met, and it is the strongest arm in the campaign**: with a scene rendering continuously the taskbar clock reads 15:24:47 against a host 15:24:47, and the HUD says `f2245 16ms`, so the desk kept exact time while the pane held a real 60 Hz pace. Both scenes render (3D View's cube/ball/pyramid/ring, the Aquarium's fish, kelp, stones and coral). Both toggles work and both reset the counter: `g` drops to the software pipeline with its coarse 256-map shadows, `s` turns shadows off. F12 fires (verdict `shot write FAILED`, which is WORKS-38 and pre-existing). Esc returns to the full desk. **Heap, and this is the pane where it matters: ten open/close cycles are bit-identical to one** (`0x645c00` / `0x63fcd8`), so the 4,617,256-byte render target is fully reclaimed on every exit. Eleven of the thirteen panes are byte-identical to before the CL; the Monitor differs by 64 pixels, the frontier digits moving because `DeskApps` gained a field. Three desk tests pass. **One thing the conversion had to carry over deliberately**: `gsc-step` drains the keyboard itself, because `kbd-take` advances a three-phase machine one step per call and `desk-loop` takes once per iteration, which on a pane whose iteration is a frame is the exact defect measured on the ASUS on 2026-08-08. |
| 9 | **Editor converted, 2026-08-18. STAGE 9 COMPLETE: all fourteen panes are steps and no pane owns a loop.** It was TWO loops, a file list with the editor nested inside `ged-activate` and returning to it on Esc, and a step cannot nest; they are one step over a mode in `EditState`, and leaving the editor is a mode change rather than a stack unwind. **The 9 MB buffer did not move and must not**: it stays parked in `desk-edit-cell` and is handed to the step as `es` exactly as it was handed to the loops. (This row said "with no restore, precisely so the base mark cannot reach it". The POINTER is out of reach; the memory never was, and stage 12 measured it and fixed it on 2026-08-19.) Only the volume, the open file's path and the directory position ride in the record. | **Met.** Taskbar clock 15:41:50 against host 15:41:50 with SOURCE.SRC open, 61,908 lines and 2,881,715 bytes read and indexed while the desk kept time. Function preserved: the list browses, Enter opens a file with syntax highlighting and line numbers, typing inserts (three keys took the counter 2,881,715 to 2,881,718 and raised the dirty flag), Esc returns to the list with the selection intact, and **Esc at the root closes to a desk that is 0 differing pixels from the plain one**. Twelve of the fourteen panes are byte-identical to before the CL; the Monitor moves 80 pixels (frontier digits, `DeskApps` gained a field) and the two 3D panes 6 each (their own frame-counter digits). Three desk tests pass. **Not exercised, and said plainly rather than implied: F2 save and F3 revert.** Their branches are the loop's unchanged and they touch the `es` block rather than the heap, but no arm ran them, and WORKS-18 says a save on this file takes minutes. |
| 10 | **A second app stays ALIVE, 2026-08-19. The mark stack, and Files as the pane that proves it.** Cell 20 holds a stack of `(focus id, mark)` pairs; a heavy `-open` pushes, a close marks its entry dead and pops every dead entry on top, restoring the lowest mark among them. When the stack empties it restores to `desk-mark-cell` exactly as before. Tab answers the new `desk-step-hide` and returns to the desk without closing; `f` re-enters through `desk-files-focus`, repainting from the surviving `FilesState`. The other four heavy panes clear the stack in their `-open` and keep the full reset (WORKS-42). | **Met, and the falsifying prediction was written before the run.** Files left at `ESP:/EFI/`, Calculator opened and closed in between, `f` again: the pane returns at **`ESP:/EFI/`** showing `BOOT`. Had the record not survived it would read `ESP:/` with four entries at selection 0, which is what the same timeline produced when the Enter landed on a file instead of a directory. Mid-sequence capture confirms the Calculator really held the screen. **Heap, both halves:** 22,352 B per switch while alive (`0x672848` at one switch against `0x6a3a18` at ten), and **all of it reclaimed on close** -- frontier `0x645ce8` and desk mark `0x63fda8` bit-identical after one switch-then-close and after ten. **The guard has its own arm**: Files alive, then the Browser opened and closed, returns the frontier to `0x645ce8`, the same value a clean close gives, so the stack-clear really does prevent the stranded entry that would otherwise disable reclaim for the session. Three no-keystroke captures byte-identical across both builds; five desk tests pass. |
| 11 | **The two 3D panes join the stack, 2026-08-19.** They share `da-scene` and share `gsc-step`, so one change wires both. Three things made this different from Files. **Re-entry needs no repaint of the pane**: `gsc-step` draws a whole frame every iteration, so `desk-scene-reenter` redraws the desk CHROME (which the intervening app painted over, outside the 3D viewport) and hands focus back. **Tab releases the GPU viewport clip** in an arm that mirrors the Esc arm; leaving it armed would confine whatever draws next to the hidden pane's rectangle. `gsc-step` answers `gsc-step-hide` = 2 rather than `desk-step-hide`, because `GopScene` cannot cite `GopDesk` for the reason stage 9 records, and `desk-scene-step` maps the one to the other. **Two panes over one field means the loser's entry must be killed**: opening Fish over a live Scene makes the Scene's state unreachable while its entry is still LIVE, and a live entry nothing can kill is exactly the condition that stops every later close from reclaiming, so `desk-scene-open` kills the sibling first. | **Met, and the discriminator was chosen so the two answers could not look alike.** The pane's own shadow toggle is the fingerprint: `S` turns shadows off, and a fresh open would bring them back on. Over a 1250x750 crop of the render (the HUD's frame counter excluded, since it moves), the control with shadows off hashes `482d4e0d2b9a` at 693,723 dark pixels, and shadows ON hashes `093a1ae91425` at 775,799 -- 82,076 pixels apart, so the arms separate. **The test arm hashes `482d4e0d2b9a`, byte-identical to the control**: Scene opened, shadows off, Tab, Calculator opened and closed, Scene again, and it returns with its toggle intact. A mid-sequence capture at 20 s shows the Calculator holding the whole screen with no trace of the scene, so the pass is not the keys being dropped. **Heap.** One live hidden 3D pane holds **8,215,112 B** (`0x0e1b6e8` against `0x645ca0` for a desk that never opened one) -- its render target, colour and depth, and not reducible while the pane is alive, because freeing it IS closing the pane. A switch-away-and-back round trip costs a further 166,720 B. **None of it accumulates**: one open-and-close, two open-and-close, and two switch cycles then close all leave the frontier at `0x645ce8`. **The sibling kill has its own arm**: Scene opened, hidden, Fish opened, Fish closed returns `0x645ce8`, the same figure a single open-and-close gives; without the kill that arm would strand 8.2 MB for the session. Five desk tests pass. |
| 12 | **Edit joins the stack, 2026-08-19, and the wiring turned up a defect that has nothing to do with the stack.** `ged-step` answers `ged-step-hide` on Tab in the list and edit modes only; `big` and `nofat` are dead ends the user leaves with Esc and hold nothing worth keeping. `ged-reenter` picks the list paint or the editor repaint by `ed-mode`, and `desk-edit-reenter` redraws the desk chrome around it. **The defect: three places said the 9 MB buffer was out of reach of the base mark, and it never was.** `ged-init` runs from `desk-edit-open`, which is ABOVE the base mark, so both close paths restore over it, and `ged-ensure` (`GopEdit.codex:49`) only allocates when the pointer is zero. A second Edit session would have written through an address the bump allocator had already handed out again. Both close paths now zero the pointer when the restore target is at or below it. | **Met.** Edit opened, descended into `EFI/`, Tab, Calculator opened and closed, `e` again: the pane returns showing `Edit  EFI/` with `BOOT`, and the capture is byte-identical over the WHOLE frame to a control that never left (`01A3BCC3F8A9`). A fresh open reads `Edit  choose a file` with four entries (`AABBAD9DED9D`), so the arms separate. **The defect's arm was predicted before it ran and the two outcomes are 9.4 MB apart.** An Edit pane alive and hidden holds **9,598,200 B**, of which `ged-init` is 9,437,184; after it closes the frontier is 72 B over a desk that never opened one, so the buffer is demonstrably reclaimed. Open, close, open again and hide reads `0x0f6d1e0` against a first session's `0x0f6d198`, so the second session DID re-allocate; had the pointer survived it would have skipped `ged-init` and read about 9.4 MB lower. **No regression from changing the shared close path**: the 3D capability crop is unchanged at `482d4e0d2b9a`, the orphan-guard arm still returns `0x645ce8`, and the no-keystroke desk frame is byte-identical across the two builds (`F6920D45B51A`). Five desk tests pass. |
| 13 | **The Browser joins the stack, 2026-08-19, and it is the one pane with a RULE attached: it may only ever be the topmost heavy pane.** `gbr-repaint` takes `bs-laid-mark` at the CURRENT frontier and rebuilds `bp-st` there, so a Browser re-entered above another pane has its state above THAT pane's mark; when the pane below closes and the cascade pops to its mark, the Browser's state goes with it and the record still points at it. No other pane can do this, because the other three build their state once in their own `-open`, above their own mark and below everything later. So `desk-files-open`, `desk-edit-open` and `desk-scene-open` each call `desk-browser-evict` and clear `da-browser`. What that buys is the common case: the Browser stays alive under any of the NINE light panes, since a light pane pushes no mark and its close finds the Browser's entry live on top, reclaims nothing, and leaves its marks untouched. The re-entry itself needs no new machinery: `gbr-first-paint` re-takes the mark at the current frontier, which is what puts it above whatever light pane ran in between. | **Met, and the eviction arm is the sharp one.** Two tabs opened with Ctrl+T, Tab, Calculator opened and closed, `b` again: the frame is identical to a Browser that never left except for the 12-row strip below, where a FRESH Browser differs across the whole tab bar (26,856 pixels), so the arms separate. **Eviction: two tabs, Tab, Files opened and closed, `b` again returns a Browser byte-IDENTICAL to a fresh one-tab Browser**, and the frontier is back to `0x645ce8`, so the cascade popped the dead Files entry and the dead evicted Browser entry together and nothing was stranded. **A live hidden Browser holds 43,800 B**, the cheapest of the four by three orders of magnitude. Five desk tests pass. **One visible rough edge, and my first diagnosis of it was WRONG.** After a hide and return the Browser's bottom edge sits 12 device rows higher and the desk background shows in the gap; I read that as `gbr-repaint` being incremental and leaving stale rows, and a fill of the region before the repaint was tried on that theory and did not close it. **Measured properly on 2026-08-19 the desk is not what moves it**: open the Browser with nothing else running and its bottom edge is at y 827; press ANY key and it moves to y 839 and stays, the diff being exactly the band y 828..839 and nothing else in the frame, not even the address bar text. So the Browser's FIRST paint lays the page 12 rows shorter than every repaint after it, and re-entry re-runs the first paint. Neither figure is the pane's actual bottom (843), so both layouts are wrong by different amounts. It is BROWSER-5 in the Browser's own register, and the fill is not in the tree. |
| 4 | **The taskbar's live-app row, 2026-08-19, which closes the residual half of this stage.** The `tasks` slot between the `Codex` label and the clock was an empty flex spacer; it reads the MARK STACK now rather than a list of its own. That is the whole design decision: an app is alive exactly when it holds a live entry in cell 20, so the row cannot drift from what it reports, and a dead entry takes `desk-focus-none` and names nothing. Only the five heavy panes push, so only `Files`, `Web`, `3D`, `Fish` and `Edit` can appear. Rebuilt by `desk-draw`, which runs at the two moments the live set changes AND the desk is visible. | **Met, and both falsifying arms ran.** Files hidden reads `Files`; the 3D pane hidden over it reads `Files   3D`. **Closing leaves a frame byte-identical to a desk that never opened anything**, so the row empties with no residue, and a desk with nothing alive is byte-identical to before the CL (`F6920D45B51A`), which is the arm that says an empty stack still renders an empty row. **The eviction arm is the one that matters**: Browser hidden, then Files opened, reads `Files` ALONE and not `Web   Files`, so the row reports what the desk actually kept rather than what the user asked for. Five desk tests pass, `desk-taskbar-hit` among them. **Not shown, deliberately: how much each live pane is holding.** The figures exist (43,800 B to 9,598,200 B) and the band is 28 pixels tall at scale 1; a number per app there is a design question, not a wiring one. |
| 14 | **The system menu, 2026-08-19, and it is the launcher in a different box.** `desk-menu-tree` wraps `gpr-tree` UNCHANGED; `gpr-key` and `gpr-id-scan` are its whole behaviour, and it shares `desk-prog-cell` so the two presentations share a SELECTION rather than drifting. Opened by scancode 41 and by the taskbar `menu` widget, which became a button. **The design asked for a panel floating above that button and the widget layer will not do it**: a panel does not size to its children on either axis, so a bottom-anchored box with a flex spacer above gets zero height and draws off the glass, and nothing clips or scrolls, so one column of thirteen entries and four headings (366 logical plus padding) overflows the 400-logical content region at 1600x900. Shrinking rows to 18 clips the button chrome against its own label. So it takes the launcher's two columns, anchored left at 500 logical with the right third free. | **Met by key AND by click.** Scancode 41 opens the menu with all four groups, all thirteen entries, the key hints and the help line, nothing clipped (565,655 pixels differ from a plain desk). Down then Enter opens the **Calculator**, which exercises the selection, `gpr-key` and the close-and-dispatch answer in one arm. Esc returns a frame **byte-identical to the plain desk**. Five desk tests pass, and `desk-taskbar-hit` earned its keep: renaming the taskbar id to `sysmenu` failed it, the id went back, and the only visible change to a plain desk is 3,228 pixels in that widget's own box where the label became a button. **The click arm was reported unverified at main 17458 and that was WRONG on both counts** -- the bed can drive a pointer, and my reason for saying it could not was a misreading of the wire. `-mouse` takes a host-tracked position starting at `0,0`, sends the DELTA between consecutive events, and clamps it to +-127 a sample, so my single jump to a screen coordinate moved the pointer 127 pixels and my "control" proved only that I was scripting it wrong. Corrected in `OperatorsManual.md`. Walked properly, four samples of about -95,+102 from the centre to `-380,407`, the pointer lands on the taskbar button and **the click opens the menu: 448 pixels differ from the key-opened frame and the bbox spans only the two cursor positions, so the pane itself is byte-identical and both roads reach the same place**, which is the "same road" the contract asks for. Clicking `Calculator` in the open menu then opens the Calculator, so the whole mouse road works end to end. |
| n | **System menu** presenting the launcher's model. | Open by button and by key, both reaching the same pane, which is the "same road" the contract requires. |
| 15 | **3D mouse-look, 2026-08-19, and it closes WORKS-13's 3D half.** `desk-scene-step` and `gsc-step` both take `UsbMouse` now, so the signature grew as the row said it must. **The design question was settled as a TAKEOVER rather than an addition**: the angle came from `gsc-orbit`, which the HPET drives and never stops, so adding a mouse offset to a moving angle gives a view that drifts out from under the hand. The first pointer movement latches `sp-look` and the orbit stops being consulted from then on, so the scene turns itself until you touch it and after that you turn it; there is no way back short of closing the pane, which is the honest cost of one bit. `sp-yaw`, `sp-look` and `sp-ptr` are integers stored in place, so the desk's per-iteration restore is harmless to them, the same reason the toggles are safe. `sp-ptr` packs the pointer as `x * 4096 + y` and ZERO means never sampled, without which the first frame reads a delta against an origin the pointer was never at and the view snaps. The gain is 8 milliradians a pixel, so a full turn is 785 pixels of travel, and the delta is clamped to +-64 BEFORE use, because an unclamped jump plus one `gsc-turn` can still land negative and `int-mod` of a negative is not the wrap this wants. | **Met, and the control is the one this row asked for.** With `-Rtc` pinned the HPET is pinned too, so the orbit angle is constant and an unwired mouse would leave all three arms identical. Over a 1250x750 crop of the render: no mouse hashes `093a1ae91425` at 775,799 dark pixels; +200 px of travel hashes `432245167b9e` at 755,889; +400 px hashes `8be92207f705` at 767,244. Three distinct cameras, and the third differs from the second, so it tracks the AMOUNT of movement and not merely the presence of a pointer. Both moved frames are coherent orbits with geometry and shadows intact, not corruption. **The Aquarium gets it from the same change** because it shares the step: `15644d21ca8e` without the mouse against `54bf1b97e210` with it. A plain desk is byte-identical to before the CL, and five desk tests pass. |

## 6. The last five panes need one decision, and it is not five decisions

**Scoped 2026-08-18, after nine conversions.** The remaining panes are Files,
Browser, 3D View, Aquarium and Edit, and they are blocked on the same thing.

**Every one of them carries a TYPED value across loop iterations. None of the
nine converted ones did.** That is the whole difference, and it is why the
first nine went one CL each and these will not.

| pane | typed values its loop carries |
|---|---|
| Files, `gfl-loop` -- **CONVERTED 2026-08-18** | `Fat16Volume`, `List Fat16DirEntry`, `List Integer` (the directory stack), `List Text` (the names), plus two integers |
| Browser, `gbr-loop` -- **CONVERTED 2026-08-18** | `BrowserState` |
| Edit, `ged-loop` -- **CONVERTED 2026-08-18** | `Fat16Volume`, `Text` (the filename) |
| 3D View and Aquarium, `gsc-loop` -- **CONVERTED 2026-08-18** | `R3dTriState`, the scene builder function, `GpuView` |

What the nine had instead was one of three things, and all three are cheap: an
integer in a `ds` cell; a state block the pane manages itself with `peek-32`
and `poke-32`, which holds raw addresses and no types; or state cheap enough to
rebuild, as the Monitor rebuilds `AcpiInfo` and Issues rebuilds its `DbServer`.

**Rebuilding does not work here and that is worth being precise about.** Files
would have to `gfat-mount-esp` and re-list the directory on every keystroke,
which is disk I/O per key; and its `clusters` and `names` are the user's
POSITION in the tree, which cannot be rebuilt from nothing at all. The 3D
panes' `R3dTriState` is a 4.6 MB render target. Flattening into a raw block is
imaginable for Files and is not for `BrowserState` or `R3dTriState`.

### The options, and a recommendation

1. **Thread each pane's state through `desk-loop` as its own parameter.**
   Works, and `tf` and `root` are already threaded that way. But `desk-loop`
   grows a parameter per heavy pane, every call site in the file grows with it,
   and each value has to exist even when its pane is shut.
2. **One record threaded through `desk-loop`, a field per heavy pane.** One
   parameter instead of five. Same objection about existing while shut, and
   building all of them at boot is what the contract already refuses for Edit's
   9 MB.
3. **A record of `Maybe` per heavy pane, built EMPTY in `desk-run` and filled
   on open.** One parameter, no boot cost, no `ds` cells, and the types stay
   types. `Maybe` is ordinary here; `widget-find` answers one.
4. **Leave these five as loop panes.** A mixed model, honest but permanent: the
   desk stops while any of the five runs, which is exactly today's behaviour.

**RULED 2026-08-18 by Damian via red: option 3**, and the channel landed as its own CL before any of the five panes, verified against the nine already converted. Option 4 was declined in terms worth keeping: it is not saved state. The original recommendation follows.

**Recommendation: option 3**, and it wants to be its own CL before any pane
conversion. The protocol change first, verified against the nine panes already
converted, then the five panes one at a time on top of it. Option 4 is a real
answer rather than a failure, and if the five are rare enough it may be the
right one; that is a call worth making deliberately rather than by drifting.

### What the first conversion on top of option 3 found, 2026-08-18

The channel alone was not enough, and the missing half is worth writing down
before the other four meet it.

A record handed to a step lives above the base mark and below the frame mark,
so the per-iteration restore never reaches it. That covers the values a pane
is BORN with, and it covers any INTEGER a step stores into the record later,
because an integer is not a pointer into the freed region. It does not cover a
step that stores a new LIST or a new record: those are allocated inside the
iteration, and the restore at the end of the iteration frees exactly them.

Files hits this the moment the user changes directory. The fix is one arm in
`desk-loop`: a step may answer a NEGATIVE number, meaning stay open and skip
the frame restore. The frame that holds the new state is kept, along with that
iteration's transient allocations, which is the price. It is paid per
directory change rather than per poll, and `desk-app-close` reclaims all of it
at once, measured. Only a step that actually grew state may answer negative;
answering it on an ordinary repaint puts the idle leak back.

Browser and the 3D panes will need this same arm, and so does Edit: only its
POINTER stays in the `ds` cell, and the 9 MB the pointer names is above the
base mark and reclaimed by every close (stage 12, 2026-08-19). This sentence
said Edit may not need it.

**Not in this design, deliberately:** SMP or a core per app (section 2 says
what would have to be true first); a compositor or overlapping windows, which
is a much larger claim than "more than one app alive"; and any renderer work
beyond the aspect term until stage 0 can say what a frame costs.

## 5. What this design must not break

Pointers, not restatements. `apps/works/works-desk-contract.md` is the
standing rules and every one of them survives this design except the one it
deliberately changes:

- **Section 1 changes.** The desk still never unwinds, but panes stop
  participating in the cycle, so the per-visit FRAME goes away. The base heap
  mark in section 3 stays exactly as it is; it just fires on focus change
  instead of on pane exit.
- **Section 2 rule stands**: a `ds` cell holding a pointer is allocated in
  `desk-run` before the base mark. The app table obeys it.
- **Section 5 stands**: any repaint under the cursor is bracketed by
  `cursor-hide` and `dk-cursor`. A taskbar that repaints on a clock tick is a
  repaint under the cursor.
- **Section 6 stands**: the palette arrives as a parameter, because `GopDesk`
  cites `GopEdit` and the reverse would be a citation cycle.
- **`build/desk.ps1 -Force`** whenever a chapter other than `DeskVm.codex`
  changed, or the staleness check serves the previous binary.
- **Only the DELTA transfers from the bed.** The bed gives 3072 MB and the
  boot image gives the real desk 128 MB, so no absolute exhaustion figure
  measured here means anything on metal. Per-visit growth does.
