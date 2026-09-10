# The Desk Scheduler

*Damian, 2026-08-26, on testing the window drag: "we need the os to distribute
these ticks somehow, or determine the rates, rather than have them baked into
the window. the current architecture there seems wrong to me."*

Status: PROPOSAL, PARKED by Damian, to be iterated later. Nothing below is
built except the rate counter, which is described under "How to verify".

## What is wrong

**Every pane invents its own rate policy inline, and the desk loop's period is
whatever the focused pane's step happens to cost.** There is no component whose
job is to decide when anything runs.

Four different policies are in the tree today, each written where it was
needed:

| pane | policy | where |
|---|---|---|
| Monitor | repaint on an RTC second it owns | `desk-mon-step`, gating on `ds` cell 16 |
| taskbar clock | repaint on an RTC second the DESK owns | `desk-clock`, gating on `desk-second-cell` |
| 3D View, Aquarium | render a full frame on EVERY call, no gate at all | `gsc-step` |
| the other eleven | repaint only when input arrives | each pane's own step |

The first two are the same policy invented twice, and
`works-desk-contract.md` already records that they disagree: the desk gates on
`rtc-seconds` and the Clock pane on `rtc-seconds-unguarded`, so the two see the
same second turn over on different iterations.

The third sets the pace of the whole desk. The contract states the consequence
as a fact rather than as a defect:

> What changes is the desk's PACE: an iteration is now a frame, so the clock
> advances no finer than the frame rate -- about 16 ms in the bed on the host
> rasterizer, 86 to 135 ms on the software path, near a second on metal. It
> still advances, which is the whole point.

That sentence is this design's problem statement. **A pane's cost per iteration
is the desk's input latency**, because the two share one loop and nothing
arbitrates.

**The window drag added a fifth policy, and it is the one that makes the shape
obvious.** `desk-loop` reads

```
ck <- if dk-drag-fid ds == desk-focus-none then desk-clock ... else act 0 end
```

which is an ad-hoc rate decision bolted into the loop because there was no
scheduler to ask. It is correct and it is the wrong shape.

**The case for the scheduler is its own and does not rest on any report of
choppiness.** A drag report that prompted this is resolved and the symptom is
gone; which of three landings moved it was never measured, and a mechanism that
explains a symptom is not its cause until a fix is shown to move it
(L-MECHANISM). **If choppiness returns, the rate counter below is the
instrument**, and the XOR outline drag, the state-block growth and the HPET
enable at desk boot are where to look.

## The design

**One tick source, owned by the desk.** `hpet-ticks` already exists and
`GopScene` already uses it, so there is no new device work. The RTC stays what
the clock DISPLAYS; it stops being what anything SCHEDULES on.

**A pane declares when it wants servicing, as a per-pane fact.** This is the
same shape the window world already uses: a pane joins by taking a line in
`desk-wnd-blk`, `desk-wnd-title` and `desk-wnd-tree`. Add a fourth, answering a
period rather than a gate:

- event-driven: paint only when input arrives (eleven panes today)
- every N milliseconds: the Monitor and the Clock pane, N = 1000
- every frame: the two 3D panes, which want whatever is left

The declaration replaces the gate inside each step. `desk-mon-step` stops
reading `ds` cell 16; `desk-clock` stops comparing `desk-second-cell`.

**`desk-loop` pumps input every iteration, unconditionally, and services a pane
only when it is due.** Painting never sits between the pointer and the screen.
That is the whole of the change from the loop's side.

**A drag suspends pane painting through the scheduler**, which deletes the
ad-hoc `dk-drag-fid` gate above and generalises it: any gesture that owns the
pointer can ask for the same.

## What it touches

- `desk-loop` and `desk-step-of` in `apps/works/GopDesk.codex`.
- Every pane's step that carries a gate today: `desk-mon-step`, `desk-clock`,
  `desk-clock-pane-step`, `gsc-step` in `apps/works/GopScene.codex`.
- `apps/works/works-desk-contract.md` section 0, which currently says a step
  "handles ONE event, paints if it needs to". The second half becomes the
  scheduler's decision and the contract has to say so, or the next pane written
  will invent a sixth policy.

## What it must not break

- **A step still may not stack a frame.** Section 1 of the contract: the desk
  never unwinds, and the visit path is mutual recursion. A scheduler that
  called panes from a new place would change that and must not.
- **A step that stores a pointer must still answer negative**, and one that
  restores below the desk's frame mark must still keep the frame. The
  scheduler decides WHEN a step runs, never what its answer means.
- **The taskbar clock must keep advancing while a pane is open.** That is what
  the step conversion bought (measured: 4 s and 18 s frozen before, 0 after)
  and a scheduler that starves it would give it back.
- **`desk-wnd-paint-all` paints the band last** so the taskbar stays above a
  window dragged off the glass. Unrelated to rates, easy to lose in a rewrite.

## How to verify

The arm this wants is a COUNT, not a picture: desk-loop iterations per second
with each pane focused, before and after. A pane whose step is expensive should
move that number a lot today and very little afterwards, and the two 3D panes
are the extreme case that makes the difference legible.

**The counter exists.** `dk-rate-tick` counts every `desk-loop` iteration and
the topbar paints the rate once a second, about 20,000 it/s idle at 1600x900.
It keeps updating during a drag, which the taskbar clock does not, because
`desk-loop` gates `desk-clock` off while a drag is in progress.

**THE PER-PANE COMPARISON CANNOT BE SCRIPTED, and that is not a tooling gap to
fix here.** App launch from the desktop by keystroke was removed, so
`build/desk.ps1 -Keys` cannot open a pane at all: four scancodes over nine
seconds against a bare desktop opened nothing, and three arms captured that way
all silently measured the same idle desktop. A pane has to be opened with the
mouse, which means driving `codex-vm` directly with `-mouse-file`. **Anyone
reading a per-pane number must confirm the pane is actually open in the frame
before believing it.**

`codex/test/apps/desk-window-drag` is the precedent for the arithmetic half:
the schedule decision (is this pane due at tick T) is pure and can be decided
without a screen, a face or a pointer. The pacing itself needs the bed.

**Sabotage before recording any expectation.** Collapsing a pane's declared
period to zero should move the count and nothing else.

## Open, and only Damian can call it

- **Does a pane declare a RATE or a BUDGET?** A rate is simpler and is what
  this document assumes. A budget ("you may have 4 ms") survives a slow pane
  better and is more machinery than the desk has anywhere else.
- **What happens when a pane misses its slot**, skip it or run it late? The
  3D panes want skip; the clock wants late, or it loses a second.

Neither is settled here. The rest of the design does not depend on the answer.
